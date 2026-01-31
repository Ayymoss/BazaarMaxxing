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
                
                // Cleanup old ticks, candles, and stale products
                await ohlcRepository.PruneOldTicksAsync(TickRetention, stoppingToken);
                await ohlcRepository.PruneOldCandlesAsync(stoppingToken);
                await productRepository.DeleteStaleProductsAsync(staleAfterDays: 2, stoppingToken);
                
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
            Open = s.BidUnitPrice,
            High = s.BidUnitPrice,
            Low = s.BidUnitPrice,
            Close = s.BidUnitPrice,
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
        var now = DateTime.UtcNow;

        // Determine how far back to look for ticks
        // For daily/weekly intervals, ALWAYS look back the full tick retention period
        // This ensures seeded flat candles get properly re-aggregated with actual tick data
        DateTime lookbackStart;
        if (interval is CandleInterval.OneDay or CandleInterval.OneWeek)
        {
            // Always re-aggregate from the full tick retention window for larger intervals
            // This fixes seeded flat candles that have O=H=L=C
            lookbackStart = now - TickRetention;
        }
        else
        {
            // For smaller intervals, use the latest candle as the starting point for efficiency
            var latestCandle = await ohlcRepository.GetLatestCandleTimeAsync(productKey, interval, ct);
            lookbackStart = latestCandle ?? now.AddDays(-7);
        }

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
                    .Sum(t => t.BidVolume + t.AskVolume);

                // Calculate average spread (AskPrice - BidPrice) for the period
                // Ask (Seller's Price) is typically higher than Bid (Buyer's Price)
                var spreads = orderedTicks
                    .Where(t => t.AskPrice > 0 && t.BidPrice > 0)
                    .Select(t => t.AskPrice - t.BidPrice)
                    .Where(s => s > 0)
                    .ToList();
                var avgSpread = spreads.Count > 0 ? spreads.Average() : (double?)null;

                return new EFOhlcCandle
                {
                    ProductKey = productKey,
                    Interval = interval,
                    PeriodStart = g.Key,
                    Open = orderedTicks.First().BidPrice,
                    High = orderedTicks.Max(t => t.BidPrice),
                    Low = orderedTicks.Min(t => t.BidPrice),
                    Close = orderedTicks.Last().BidPrice,
                    AskClose = orderedTicks.Last().AskPrice,
                    Volume = totalVolume,
                    Spread = avgSpread ?? 0
                };
            })
        ];

        await ohlcRepository.SaveCandlesAsync(candles, ct);
    }
}
