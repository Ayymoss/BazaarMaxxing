using BazaarCompanionWeb.Context;
using BazaarCompanionWeb.Entities;
using BazaarCompanionWeb.Interfaces.Database;
using BazaarCompanionWeb.Utilities;
using Microsoft.EntityFrameworkCore;

namespace BazaarCompanionWeb.Services;

public class OhlcAggregationService(
    IServiceScopeFactory scopeFactory,
    ILogger<OhlcAggregationService> logger) : BackgroundService
{
    private static readonly TimeSpan AggregationInterval = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan TickRetention = TimeSpan.FromDays(7);
    private static readonly TimeSpan VacuumInterval = TimeSpan.FromHours(24);
    private bool _historySeeded;
    private bool _flatCandlesRepaired;
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

                if (!_flatCandlesRepaired)
                {
                    await RepairFlatDailyCandlesAsync(stoppingToken);
                    _flatCandlesRepaired = true;
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
            PeriodStart = DateTime.SpecifyKind(s.Taken.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc),
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

    /// <summary>
    /// Incremental aggregation: for each (product, interval) watermark, query ticks at or after
    /// the watermark's <c>LastSeenPeriodStart</c>, build candles for those periods, upsert, and
    /// advance the watermark. First-run (no watermark) falls back to a 7-day lookback.
    /// </summary>
    private async Task AggregateAllCandlesAsync(IOhlcRepository ohlcRepository, CancellationToken ct)
    {
        var productKeys = await ohlcRepository.GetAllProductKeysAsync(ct);
        var states = await ohlcRepository.GetAggregationStatesAsync(ct);
        var now = DateTime.UtcNow;

        var intervals = Enum.GetValues<CandleInterval>();

        // Compute the earliest watermark across all (product, interval) — single bulk tick load.
        DateTime sinceCutoff = now - TickRetention;
        foreach (var key in productKeys)
        foreach (var interval in intervals)
        {
            if (states.TryGetValue((key, interval), out var s) && s.LastSeenPeriodStart < sinceCutoff)
                sinceCutoff = s.LastSeenPeriodStart;
        }
        // sinceCutoff floor at now - retention to avoid pulling beyond what we keep.
        if (sinceCutoff < now - TickRetention) sinceCutoff = now - TickRetention;

        var ticksByProduct = await ohlcRepository.GetTicksForAggregationBulkAsync(productKeys, sinceCutoff, ct);
        var newStates = new System.Collections.Concurrent.ConcurrentBag<EFOhlcAggregationState>();

        await Parallel.ForEachAsync(
            productKeys,
            new ParallelOptions { MaxDegreeOfParallelism = 8, CancellationToken = ct },
            async (productKey, cancellationToken) =>
            {
                var ticks = ticksByProduct.GetValueOrDefault(productKey) ?? [];
                foreach (var interval in intervals)
                {
                    var state = states.GetValueOrDefault((productKey, interval));
                    var newState = await AggregateIntervalAsync(ohlcRepository, productKey, interval, state, ticks, now, cancellationToken);
                    if (newState is not null) newStates.Add(newState);
                }
            });

        if (!newStates.IsEmpty)
            await ohlcRepository.UpsertAggregationStatesAsync(newStates.ToList(), ct);
    }

    private async Task<EFOhlcAggregationState?> AggregateIntervalAsync(
        IOhlcRepository ohlcRepository, string productKey, CandleInterval interval,
        EFOhlcAggregationState? state, List<EFPriceTick> preloadedTicks, DateTime now,
        CancellationToken ct)
    {
        // Watermark cutoff: from the period we last touched (re-query the current open period),
        // or 7 days back on first run.
        var cutoff = state?.LastSeenPeriodStart ?? (now - TickRetention);

        var ticks = preloadedTicks
            .Where(t => t.Timestamp >= cutoff)
            .OrderBy(t => t.Timestamp)
            .ToList();
        if (ticks.Count == 0) return null;

        var grouped = ticks.GroupBy(t => t.Timestamp.GetPeriodStart(interval)).ToList();
        if (grouped.Count == 0) return null;

        var candles = grouped.Select(g =>
        {
            var orderedTicks = g.OrderBy(t => t.Timestamp).ToList();
            var totalVolume = orderedTicks.Sum(t => t.BidVolume + t.AskVolume);

            var spreads = orderedTicks
                .Where(t => t.AskPrice > 0 && t.BidPrice > 0)
                .Select(t => t.AskPrice - t.BidPrice)
                .Where(s => s > 0)
                .ToList();
            var avgSpread = spreads.Count > 0 ? spreads.Average() : 0;

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
                Spread = avgSpread,
            };
        }).ToList();

        await ohlcRepository.SaveCandlesAsync(candles, ct);

        var maxPeriod = grouped.Max(g => g.Key);
        return new EFOhlcAggregationState
        {
            ProductKey = productKey,
            Interval = interval,
            LastSeenPeriodStart = maxPeriod,
            UpdatedAt = now,
        };
    }

    /// <summary>
    /// One-time repair: find flat daily candles (O==H==L==C, from snapshot seeding) and rebuild
    /// them from existing hourly candles which have proper OHLC data.
    /// </summary>
    private async Task RepairFlatDailyCandlesAsync(CancellationToken ct)
    {
        logger.LogInformation("Checking for flat daily candles to repair from hourly data...");

        using var scope = scopeFactory.CreateScope();
        var contextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<DataContext>>();
        await using var context = await contextFactory.CreateDbContextAsync(ct);

        // Find flat daily candles (O==H==L==C means they came from single-value snapshot seeding)
        var flatDailyCandles = await context.OhlcCandles
            .Where(c => c.Interval == CandleInterval.OneDay
                        && c.Open == c.High && c.High == c.Low && c.Low == c.Close)
            .ToListAsync(ct);

        if (flatDailyCandles.Count == 0)
        {
            logger.LogInformation("No flat daily candles found, nothing to repair");
            return;
        }

        logger.LogInformation("Found {Count} flat daily candles to repair", flatDailyCandles.Count);

        // Group by product to batch the hourly lookups
        var byProduct = flatDailyCandles.GroupBy(c => c.ProductKey).ToList();
        var repairedCount = 0;

        foreach (var group in byProduct)
        {
            var productKey = group.Key;
            var flatCandles = group.ToList();

            // Get all hourly candles for this product
            var hourlyCandles = await context.OhlcCandles
                .AsNoTracking()
                .Where(c => c.ProductKey == productKey && c.Interval == CandleInterval.OneHour)
                .OrderBy(c => c.PeriodStart)
                .ToListAsync(ct);

            if (hourlyCandles.Count == 0) continue;

            // Group hourly candles by day
            var hourlyByDay = hourlyCandles
                .GroupBy(c => c.PeriodStart.Date)
                .ToDictionary(g => g.Key, g => g.OrderBy(c => c.PeriodStart).ToList());

            foreach (var flatCandle in flatCandles)
            {
                var day = flatCandle.PeriodStart.Date;
                if (!hourlyByDay.TryGetValue(day, out var dayHourly) || dayHourly.Count == 0)
                    continue;

                // Rebuild OHLC from hourly candles
                flatCandle.Open = dayHourly.First().Open;
                flatCandle.High = dayHourly.Max(h => h.High);
                flatCandle.Low = dayHourly.Min(h => h.Low);
                flatCandle.Close = dayHourly.Last().Close;
                flatCandle.Volume = dayHourly.Sum(h => h.Volume);
                flatCandle.AskClose = dayHourly.Last().AskClose;

                var spreads = dayHourly.Where(h => h.Spread > 0).Select(h => h.Spread).ToList();
                flatCandle.Spread = spreads.Count > 0 ? spreads.Average() : 0;

                repairedCount++;
            }
        }

        if (repairedCount > 0)
        {
            await context.SaveChangesAsync(ct);
            logger.LogInformation("Repaired {Count} flat daily candles from hourly data", repairedCount);
        }
        else
        {
            logger.LogInformation("No hourly data available to repair flat daily candles");
        }
    }
}
