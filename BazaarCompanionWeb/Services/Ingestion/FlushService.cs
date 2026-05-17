using System.Diagnostics;
using BazaarCompanionWeb.Dtos;
using BazaarCompanionWeb.Interfaces.Database;

namespace BazaarCompanionWeb.Services.Ingestion;

/// <summary>
/// Drains <see cref="BazaarSnapshotStore"/> every <see cref="FlushInterval"/> and persists the
/// accumulated deltas to the database in batched form:
///   - Ticks via Npgsql binary COPY (one row per dirty product, coalesced).
///   - Products via per-flush EF upsert (delta set only, typically 50-300 rows).
///   - Order book snapshots via the existing OrderBookAnalysisService batch writer.
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

        if (snapshot.ChangedProducts.Count == 0
            && snapshot.ChangedTicks.Count == 0
            && snapshot.ChangedOrderBooks.Count == 0)
        {
            logger.LogDebug("Flush skipped — no dirty deltas in store");
            return;
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var ohlcRepository = scope.ServiceProvider.GetRequiredService<IOhlcRepository>();
        var productRepository = scope.ServiceProvider.GetRequiredService<IProductRepository>();
        var orderBookAnalysisService = scope.ServiceProvider.GetRequiredService<OrderBookAnalysisService>();

        var ticksWritten = 0;
        var productsWritten = 0;
        var booksWritten = 0;

        if (snapshot.ChangedTicks.Count > 0)
        {
            await ohlcRepository.CopyTicksAsync(snapshot.ChangedTicks, ct);
            ticksWritten = snapshot.ChangedTicks.Count;
        }

        if (snapshot.ChangedProducts.Count > 0)
        {
            await productRepository.UpdateOrAddProductsAsync(snapshot.ChangedProducts.ToList(), ct);
            productsWritten = snapshot.ChangedProducts.Count;
        }

        if (snapshot.ChangedOrderBooks.Count > 0)
        {
            var items = snapshot.ChangedOrderBooks
                .Select(b => (b.ProductKey, b.Bids.ToList(), b.Asks.ToList()))
                .ToList();
            await orderBookAnalysisService.StoreSnapshotsBatchAsync(items, ct);
            booksWritten = items.Count;
        }

        sw.Stop();
        logger.LogInformation(
            "Flush complete — {Ticks} ticks, {Products} products, {Books} order books in {ElapsedMs}ms",
            ticksWritten, productsWritten, booksWritten, sw.ElapsedMilliseconds);
    }
}
