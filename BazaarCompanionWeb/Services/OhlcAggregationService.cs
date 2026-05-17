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
                var pruneSw = System.Diagnostics.Stopwatch.StartNew();
                await ohlcRepository.PruneOldTicksAsync(TickRetention, stoppingToken);
                await ohlcRepository.PruneOldCandlesAsync(stoppingToken);
                await productRepository.DeleteStaleProductsAsync(staleAfterDays: 2, stoppingToken);
                pruneSw.Stop();
                logger.LogInformation("Prune cycle: {PruneMs}ms (ticks + candles + stale products)",
                    pruneSw.ElapsedMilliseconds);
                
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
        // Skip if any OneDay candle already exists. Otherwise we re-seed flat O==H==L==C every
        // restart, overwriting the repaired values from RepairFlatDailyCandlesAsync.
        using var scope = scopeFactory.CreateScope();
        var contextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<DataContext>>();
        await using (var context = await contextFactory.CreateDbContextAsync(ct))
        {
            var anyExists = await context.OhlcCandles.AnyAsync(c => c.Interval == CandleInterval.OneDay, ct);
            if (anyExists)
            {
                logger.LogInformation("Skipping daily-candle seed — table already populated");
                return;
            }
        }

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
    /// Short intervals that aggregate from raw ticks. OneDay/OneWeek are derived from candles instead
    /// (see <see cref="BuildLongIntervalCandlesAsync"/>) so we never pull a multi-day tick window.
    /// </summary>
    private static readonly CandleInterval[] TickAggregatedIntervals =
    [
        CandleInterval.FiveMinute,
        CandleInterval.FifteenMinute,
        CandleInterval.OneHour,
        CandleInterval.FourHour,
    ];

    /// <summary>
    /// Incremental aggregation: for each (product, interval) watermark, query ticks at or after
    /// the watermark's <c>LastSeenPeriodStart</c>, build candles for those periods, upsert, and
    /// advance the watermark. First-run (no watermark) falls back to a 7-day lookback.
    ///
    /// Only short intervals (5m, 15m, 1h, 4h) pull from ticks. OneDay/OneWeek are built from
    /// already-aggregated 1h/1d candles in <see cref="BuildLongIntervalCandlesAsync"/>.
    /// </summary>
    private async Task AggregateAllCandlesAsync(IOhlcRepository ohlcRepository, CancellationToken ct)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var productKeys = await ohlcRepository.GetAllProductKeysAsync(ct);
        var states = await ohlcRepository.GetAggregationStatesAsync(ct);
        var now = DateTime.UtcNow;
        logger.LogInformation("Aggregation cycle start: {Products} products, {States} existing watermarks",
            productKeys.Count, states.Count);

        // Compute the earliest watermark across short-interval (product, interval) pairs.
        // MIN gives us a single cutoff that covers all of them in one tick fetch.
        // If any (product, interval) is missing a watermark, we fall back to the 7-day retention floor.
        DateTime sinceCutoff = now;
        var anyMissingWatermark = false;
        foreach (var key in productKeys)
        foreach (var interval in TickAggregatedIntervals)
        {
            if (states.TryGetValue((key, interval), out var s))
            {
                if (s.LastSeenPeriodStart < sinceCutoff) sinceCutoff = s.LastSeenPeriodStart;
            }
            else
            {
                anyMissingWatermark = true;
            }
        }
        if (anyMissingWatermark)
        {
            // At least one (product, interval) has never been aggregated — pull from retention floor.
            sinceCutoff = now - TickRetention;
        }
        // Clamp: never pull further back than retention.
        if (sinceCutoff < now - TickRetention) sinceCutoff = now - TickRetention;

        var tickFetchSw = System.Diagnostics.Stopwatch.StartNew();
        var ticksByProduct = await ohlcRepository.GetTicksForAggregationBulkAsync(productKeys, sinceCutoff, ct);
        tickFetchSw.Stop();
        var totalTicks = ticksByProduct.Values.Sum(v => v.Count);
        logger.LogInformation("Aggregation tick fetch: {FetchMs}ms, {Ticks} ticks since {Cutoff:O}",
            tickFetchSw.ElapsedMilliseconds, totalTicks, sinceCutoff);

        var newStates = new System.Collections.Concurrent.ConcurrentBag<EFOhlcAggregationState>();

        await Parallel.ForEachAsync(
            productKeys,
            new ParallelOptions { MaxDegreeOfParallelism = 8, CancellationToken = ct },
            async (productKey, cancellationToken) =>
            {
                var ticks = ticksByProduct.GetValueOrDefault(productKey) ?? [];
                foreach (var interval in TickAggregatedIntervals)
                {
                    var state = states.GetValueOrDefault((productKey, interval));
                    var newState = await AggregateIntervalAsync(ohlcRepository, productKey, interval, state, ticks, now, cancellationToken);
                    if (newState is not null) newStates.Add(newState);
                }
            });

        if (!newStates.IsEmpty)
        {
            var upsertSw = System.Diagnostics.Stopwatch.StartNew();
            await ohlcRepository.UpsertAggregationStatesAsync(newStates.ToList(), ct);
            upsertSw.Stop();
            logger.LogInformation("Aggregation watermark upsert: {UpsertMs}ms, {Count} states",
                upsertSw.ElapsedMilliseconds, newStates.Count);
        }

        // Build long-interval candles (1d from 1h, 1w from 1d) without touching the tick table.
        await BuildLongIntervalCandlesAsync(ct);

        sw.Stop();
        logger.LogInformation("Aggregation cycle done: {TotalMs}ms total", sw.ElapsedMilliseconds);
    }

    /// <summary>
    /// Derives OneDay candles from OneHour, and OneWeek candles from OneDay. Only rebuilds the
    /// CURRENT day and CURRENT week — closed periods don't change so re-pulling them every cycle
    /// is wasted DB load. A one-time backfill across history happens in <see cref="RepairFlatDailyCandlesAsync"/>.
    /// </summary>
    private async Task BuildLongIntervalCandlesAsync(CancellationToken ct)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        using var scope = scopeFactory.CreateScope();
        var contextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<DataContext>>();
        await using var context = await contextFactory.CreateDbContextAsync(ct);

        var now = DateTime.UtcNow;
        // Today's 1d candle is "open"; yesterday's may have late-arriving hour-23 ticks aggregated post-midnight.
        var daySince = DateTime.SpecifyKind(now.Date.AddDays(-1), DateTimeKind.Utc);
        // Current ISO week — closed prior weeks don't move.
        var weekSince = StartOfIsoWeek(now);

        // --- OneDay from OneHour ---
        var hourly = await context.OhlcCandles
            .AsNoTracking()
            .Where(c => c.Interval == CandleInterval.OneHour && c.PeriodStart >= daySince)
            .Select(c => new { c.ProductKey, c.PeriodStart, c.Open, c.High, c.Low, c.Close, c.Volume, c.Spread, c.AskClose })
            .ToListAsync(ct);

        var dayCandles = hourly
            .GroupBy(h => new { h.ProductKey, Day = h.PeriodStart.Date })
            .Select(g =>
            {
                var ordered = g.OrderBy(h => h.PeriodStart).ToList();
                var spreads = ordered.Where(h => h.Spread > 0).Select(h => h.Spread).ToList();
                return new EFOhlcCandle
                {
                    ProductKey = g.Key.ProductKey,
                    Interval = CandleInterval.OneDay,
                    PeriodStart = DateTime.SpecifyKind(g.Key.Day, DateTimeKind.Utc),
                    Open = ordered.First().Open,
                    High = ordered.Max(h => h.High),
                    Low = ordered.Min(h => h.Low),
                    Close = ordered.Last().Close,
                    AskClose = ordered.Last().AskClose,
                    Volume = ordered.Sum(h => h.Volume),
                    Spread = spreads.Count > 0 ? spreads.Average() : 0,
                };
            })
            .ToList();

        // --- OneWeek from OneDay ---
        var daily = await context.OhlcCandles
            .AsNoTracking()
            .Where(c => c.Interval == CandleInterval.OneDay && c.PeriodStart >= weekSince)
            .Select(c => new { c.ProductKey, c.PeriodStart, c.Open, c.High, c.Low, c.Close, c.Volume, c.Spread, c.AskClose })
            .ToListAsync(ct);

        var weekCandles = daily
            .GroupBy(d => new { d.ProductKey, WeekStart = StartOfIsoWeek(d.PeriodStart) })
            .Select(g =>
            {
                var ordered = g.OrderBy(d => d.PeriodStart).ToList();
                var spreads = ordered.Where(d => d.Spread > 0).Select(d => d.Spread).ToList();
                return new EFOhlcCandle
                {
                    ProductKey = g.Key.ProductKey,
                    Interval = CandleInterval.OneWeek,
                    PeriodStart = g.Key.WeekStart,
                    Open = ordered.First().Open,
                    High = ordered.Max(d => d.High),
                    Low = ordered.Min(d => d.Low),
                    Close = ordered.Last().Close,
                    AskClose = ordered.Last().AskClose,
                    Volume = ordered.Sum(d => d.Volume),
                    Spread = spreads.Count > 0 ? spreads.Average() : 0,
                };
            })
            .ToList();

        var ohlcRepository = scope.ServiceProvider.GetRequiredService<IOhlcRepository>();
        if (dayCandles.Count > 0) await ohlcRepository.SaveCandlesAsync(dayCandles, ct);
        if (weekCandles.Count > 0) await ohlcRepository.SaveCandlesAsync(weekCandles, ct);

        sw.Stop();
        logger.LogInformation(
            "Long-interval rebuild: {ElapsedMs}ms — {Days} daily candles from {Hourly} hourly rows, {Weeks} weekly candles from {Daily} daily rows",
            sw.ElapsedMilliseconds, dayCandles.Count, hourly.Count, weekCandles.Count, daily.Count);
    }

    private static DateTime StartOfIsoWeek(DateTime date)
    {
        // ISO week starts on Monday. DayOfWeek: Sun=0, Mon=1 … Sat=6.
        var dayOfWeek = (int)date.DayOfWeek;
        var offset = dayOfWeek == 0 ? 6 : dayOfWeek - 1; // Sunday → -6, Monday → 0
        return DateTime.SpecifyKind(date.Date.AddDays(-offset), DateTimeKind.Utc);
    }

    private async Task<EFOhlcAggregationState?> AggregateIntervalAsync(
        IOhlcRepository ohlcRepository, string productKey, CandleInterval interval,
        EFOhlcAggregationState? state, List<EFPriceTick> preloadedTicks, DateTime now,
        CancellationToken ct)
    {
        // Watermark cutoff: from the period we last touched (re-query the current open period),
        // or 7 days back on first run.
        var cutoff = state?.LastSeenPeriodStart ?? (now - TickRetention);
        var currentPeriodStart = now.GetPeriodStart(interval);

        var ticks = preloadedTicks
            .Where(t => t.Timestamp >= cutoff)
            .OrderBy(t => t.Timestamp)
            .ToList();

        if (ticks.Count > 0)
        {
            var grouped = ticks.GroupBy(t => t.Timestamp.GetPeriodStart(interval)).ToList();
            if (grouped.Count > 0)
            {
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
            }
        }

        // Always advance watermark to currentPeriodStart (when greater than stored), regardless of
        // whether ticks were found this cycle. Without this, products that went quiet *after* a
        // prior cycle stay stuck on an old watermark because every cycle re-fetches their old ticks
        // and re-sets the watermark to the same old maxPeriod. Result: MIN cutoff never advances
        // and the bulk tick fetch keeps pulling days of data.
        //
        // Setting watermark = currentPeriodStart is safe because:
        //   1. The current open period gets re-aggregated next cycle (cutoff = currentPeriodStart,
        //      so ticks >= currentPeriodStart are fetched). SaveCandlesAsync upserts → idempotent.
        //   2. For active products, maxPeriod == currentPeriodStart anyway (their latest tick is in
        //      the open period), so this matches the prior behaviour.
        //   3. For quiet products, watermark advances unconditionally → MIN cutoff progresses.
        if (state == null || state.LastSeenPeriodStart < currentPeriodStart)
        {
            return new EFOhlcAggregationState
            {
                ProductKey = productKey,
                Interval = interval,
                LastSeenPeriodStart = currentPeriodStart,
                UpdatedAt = now,
            };
        }
        return null;
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
