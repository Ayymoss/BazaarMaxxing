using BazaarCompanionWeb.Entities;
using BazaarCompanionWeb.Interfaces.Database;
using BazaarCompanionWeb.Utilities;

namespace BazaarCompanionWeb.Services;

public class OhlcAggregationService(
    IServiceScopeFactory scopeFactory,
    ILogger<OhlcAggregationService> logger) : BackgroundService
{
    private static readonly TimeSpan AggregationInterval = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan TickRetention = TimeSpan.FromDays(7);
    private static readonly TimeSpan VacuumInterval = TimeSpan.FromHours(24);
    private bool _historySeeded;
    private DateTime _lastVacuumTime = DateTime.MinValue;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Initial delay to let the app start
        await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var ohlcRepository = scope.ServiceProvider.GetRequiredService<IOhlcRepository>();
                var productRepository = scope.ServiceProvider.GetRequiredService<IProductRepository>();

                if (!_historySeeded)
                {
                    await SeedHistoryAsync(ohlcRepository, productRepository, stoppingToken);
                    _historySeeded = true;
                }

                await AggregateAllCandlesAsync(ohlcRepository, stoppingToken);
                
                // Cleanup old ticks and candles
                await ohlcRepository.PruneOldTicksAsync(TickRetention, stoppingToken);
                await ohlcRepository.PruneOldCandlesAsync(stoppingToken);
                
                // Run VACUUM once per day to reclaim disk space
                if (DateTime.UtcNow - _lastVacuumTime > VacuumInterval)
                {
                    logger.LogInformation("Running database VACUUM to reclaim disk space...");
                    await ohlcRepository.VacuumDatabaseAsync(stoppingToken);
                    _lastVacuumTime = DateTime.UtcNow;
                    logger.LogInformation("Database VACUUM completed");
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error during OHLC aggregation cycle");
            }

            await Task.Delay(AggregationInterval, stoppingToken);
        }
    }

    private async Task SeedHistoryAsync(IOhlcRepository ohlcRepository, IProductRepository productRepository, CancellationToken ct)
    {
        logger.LogInformation("Seeding historical daily candles from PriceSnapshots...");

        var snapshots = await productRepository.GetPriceSnapshotsAsync(ct);

        if (snapshots.Count == 0) return;

        var candles = snapshots.Select(s => new EFOhlcCandle
        {
            ProductKey = s.ProductKey,
            Interval = CandleInterval.OneDay,
            PeriodStart = s.Taken.ToDateTime(TimeOnly.MinValue),
            Open = s.BuyUnitPrice,
            High = s.BuyUnitPrice,
            Low = s.BuyUnitPrice,
            Close = s.BuyUnitPrice,
            Volume = 0, // Historical snapshots don't have volume
            Spread = 0 // Historical snapshots don't have spread data
        }).ToList();

        await ohlcRepository.SaveCandlesAsync(candles, ct);
        logger.LogInformation("Seeded {Count} daily candles", candles.Count);
    }

    private async Task AggregateAllCandlesAsync(IOhlcRepository ohlcRepository, CancellationToken ct)
    {
        var productKeys = await ohlcRepository.GetAllProductKeysAsync(ct);

        foreach (var productKey in productKeys)
        {
            foreach (var interval in Enum.GetValues<CandleInterval>())
            {
                await AggregateIntervalAsync(ohlcRepository, productKey, interval, ct);
            }
        }
    }

    private async Task AggregateIntervalAsync(IOhlcRepository ohlcRepository, string productKey, CandleInterval interval,
        CancellationToken ct)
    {
        var intervalMinutes = (int)interval;
        var now = DateTime.UtcNow;

        // Determine how far back to look for new ticks
        var latestCandle = await ohlcRepository.GetLatestCandleTimeAsync(productKey, interval, ct);
        var lookbackStart = latestCandle ?? now.AddDays(-7);

        var ticks = await ohlcRepository.GetTicksForAggregationAsync(productKey, lookbackStart, ct);
        if (ticks.Count is 0) return;

        // Group ticks into period buckets
        var grouped = ticks
            .GroupBy(t => t.Timestamp.GetPeriodStart(interval))
            .ToList();

        if (grouped.Count is 0) return;

        List<EFOhlcCandle> candles =
        [
            ..grouped.Select(g =>
            {
                var orderedTicks = g.OrderBy(t => t.Timestamp).ToList();
                var totalVolume = orderedTicks
                    .Sum(t => t.BuyVolume + t.SellVolume);

                // Calculate average spread (BuyPrice - SellPrice) for the period
                var spreads = orderedTicks
                    .Where(t => t.BuyPrice > 0 && t.SellPrice > 0)
                    .Select(t => t.BuyPrice - t.SellPrice)
                    .Where(s => s > 0)
                    .ToList();
                var avgSpread = spreads.Count > 0 ? spreads.Average() : (double?)null;

                return new EFOhlcCandle
                {
                    ProductKey = productKey,
                    Interval = interval,
                    PeriodStart = g.Key,
                    Open = orderedTicks.First().BuyPrice,
                    High = orderedTicks.Max(t => t.BuyPrice),
                    Low = orderedTicks.Min(t => t.BuyPrice),
                    Close = orderedTicks.Last().BuyPrice,
                    Volume = totalVolume,
                    Spread = avgSpread ?? 0
                };
            })
        ];

        await ohlcRepository.SaveCandlesAsync(candles, ct);
    }
}
