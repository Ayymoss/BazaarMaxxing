using System.Diagnostics;
using BazaarCompanionWeb.Interfaces.Database;

namespace BazaarCompanionWeb.Services.Ingestion;

/// <summary>
/// Drains <see cref="BazaarSnapshotStore"/> every <see cref="FlushInterval"/> and persists the
/// accumulated deltas to the database in batched form:
///   - Ticks via Npgsql binary COPY (one row per dirty product, coalesced).
///   - Products via per-flush EF upsert (delta set only, typically 50-300 rows).
///
/// Crash-loss is accepted: anything not yet flushed is lost on restart. Per project policy.
/// </summary>
public sealed class FlushService(
    BazaarSnapshotStore store,
    IServiceScopeFactory scopeFactory,
    ILogger<FlushService> logger) : BackgroundService
{
    private static readonly TimeSpan FlushInterval = TimeSpan.FromMinutes(10);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("FlushService started — flushing every {Interval}", FlushInterval);

        // Wait one interval before first flush so the store has something to drain.
        try { await Task.Delay(FlushInterval, stoppingToken); }
        catch (TaskCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await FlushOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "FlushService encountered an error during flush");
            }

            try { await Task.Delay(FlushInterval, stoppingToken); }
            catch (TaskCanceledException) { break; }
        }
    }

    private async Task FlushOnceAsync(CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var snapshot = store.DrainForFlush();

        if (snapshot.ChangedProducts.Count == 0 && snapshot.ChangedTicks.Count == 0)
            return;

        await using var scope = scopeFactory.CreateAsyncScope();
        var ohlcRepository = scope.ServiceProvider.GetRequiredService<IOhlcRepository>();
        var productRepository = scope.ServiceProvider.GetRequiredService<IProductRepository>();

        if (snapshot.ChangedTicks.Count > 0)
            await ohlcRepository.CopyTicksAsync(snapshot.ChangedTicks, ct);

        if (snapshot.ChangedProducts.Count > 0)
            await productRepository.UpdateOrAddProductsAsync(snapshot.ChangedProducts.ToList(), ct);

        sw.Stop();
        logger.LogInformation(
            "Flush complete — {Ticks} ticks, {Products} products in {ElapsedMs}ms",
            snapshot.ChangedTicks.Count, snapshot.ChangedProducts.Count, sw.ElapsedMilliseconds);
    }
}
