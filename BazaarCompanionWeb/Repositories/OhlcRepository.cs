using BazaarCompanionWeb.Context;
using BazaarCompanionWeb.Dtos;
using BazaarCompanionWeb.Entities;
using BazaarCompanionWeb.Interfaces.Database;
using Microsoft.EntityFrameworkCore;

namespace BazaarCompanionWeb.Repositories;

public class OhlcRepository(IDbContextFactory<DataContext> contextFactory) : IOhlcRepository
{
    public async Task RecordTicksAsync(
        IEnumerable<(string ProductKey, double BidPrice, double AskPrice, long BidVolume, long AskVolume)> ticks,
        CancellationToken ct = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(ct);
        var timestamp = DateTime.UtcNow;

        List<EFPriceTick> entities =
        [
            ..ticks.Select(t => new EFPriceTick
            {
                ProductKey = t.ProductKey,
                BidPrice = t.BidPrice,
                AskPrice = t.AskPrice,
                Timestamp = timestamp,
                BidVolume = t.BidVolume,
                AskVolume = t.AskVolume
            })
        ];

        await context.PriceTicks.AddRangeAsync(entities, ct);
        await context.SaveChangesAsync(ct);
    }

    public async Task<List<OhlcDataPoint>> GetCandlesAsync(
        string productKey,
        CandleInterval interval,
        int limit = 100,
        CancellationToken ct = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(ct);

        var candles = await context.OhlcCandles
            .AsNoTracking()
            .Where(c => c.ProductKey == productKey && c.Interval == interval)
            .OrderByDescending(c => c.PeriodStart)
            .Take(limit)
            .OrderBy(c => c.PeriodStart)
            .Select(c => new OhlcDataPoint(c.PeriodStart, c.Open, c.High, c.Low, c.Close, c.Volume, c.Spread, c.AskClose))
            .ToListAsync(ct);

        return candles;
    }

    public async Task<IReadOnlyDictionary<string, List<OhlcDataPoint>>> GetCandlesBulkAsync(
        IReadOnlyList<string> productKeys,
        CandleInterval interval,
        int limitPerProduct,
        CancellationToken ct = default)
    {
        if (productKeys.Count == 0)
            return new Dictionary<string, List<OhlcDataPoint>>();

        const int chunkSize = 500;
        var result = new Dictionary<string, List<OhlcDataPoint>>();

        for (var i = 0; i < productKeys.Count; i += chunkSize)
        {
            var chunk = productKeys.Skip(i).Take(chunkSize).ToList();
            await using var context = await contextFactory.CreateDbContextAsync(ct);

            var rows = await context.OhlcCandles
                .AsNoTracking()
                .Where(c => chunk.Contains(c.ProductKey) && c.Interval == interval)
                .OrderBy(c => c.ProductKey)
                .ThenByDescending(c => c.PeriodStart)
                .Select(c => new { c.ProductKey, c.PeriodStart, c.Open, c.High, c.Low, c.Close, c.Volume, c.Spread, c.AskClose })
                .ToListAsync(ct);

            foreach (var group in rows.GroupBy(r => r.ProductKey))
            {
                var candles = group
                    .Take(limitPerProduct)
                    .OrderBy(x => x.PeriodStart)
                    .Select(x => new OhlcDataPoint(x.PeriodStart, x.Open, x.High, x.Low, x.Close, x.Volume, x.Spread, x.AskClose))
                    .ToList();
                result[group.Key] = candles;
            }
        }

        return result;
    }

    public async Task<List<OhlcDataPoint>> GetCandlesBeforeAsync(
        string productKey,
        CandleInterval interval,
        DateTime before,
        int limit = 100,
        CancellationToken ct = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(ct);

        // Get candles BEFORE the specified timestamp, ordered chronologically
        var candles = await context.OhlcCandles
            .AsNoTracking()
            .Where(c => c.ProductKey == productKey && c.Interval == interval && c.PeriodStart < before)
            .OrderByDescending(c => c.PeriodStart)
            .Take(limit)
            .OrderBy(c => c.PeriodStart)
            .Select(c => new OhlcDataPoint(c.PeriodStart, c.Open, c.High, c.Low, c.Close, c.Volume, c.Spread, c.AskClose))
            .ToListAsync(ct);

        return candles;
    }

    public async Task<List<EFPriceTick>> GetTicksForAggregationAsync(
        string productKey,
        DateTime since,
        CancellationToken ct = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(ct);

        return await context.PriceTicks
            .AsNoTracking()
            .Where(t => t.ProductKey == productKey && t.Timestamp >= since)
            .OrderBy(t => t.Timestamp)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyDictionary<string, List<EFPriceTick>>> GetTicksForAggregationBulkAsync(
        IReadOnlyList<string> productKeys,
        DateTime since,
        CancellationToken ct = default)
    {
        if (productKeys.Count == 0)
            return new Dictionary<string, List<EFPriceTick>>();

        await using var context = await contextFactory.CreateDbContextAsync(ct);

        var ticks = await context.PriceTicks
            .AsNoTracking()
            .Where(t => productKeys.Contains(t.ProductKey) && t.Timestamp >= since)
            .OrderBy(t => t.ProductKey)
            .ThenBy(t => t.Timestamp)
            .ToListAsync(ct);

        return ticks.GroupBy(t => t.ProductKey).ToDictionary(g => g.Key, g => g.ToList());
    }

    public async Task SaveCandlesAsync(IEnumerable<EFOhlcCandle> candles, CancellationToken ct = default)
    {
        var candleList = candles.ToList();
        if (candleList.Count == 0) return;

        // PostgreSQL timestamp with time zone requires UTC; normalize any Unspecified to UTC
        foreach (var c in candleList)
        {
            if (c.PeriodStart.Kind != DateTimeKind.Utc)
                c.PeriodStart = DateTime.SpecifyKind(c.PeriodStart, DateTimeKind.Utc);
        }

        const int chunkSize = 800;
        for (var i = 0; i < candleList.Count; i += chunkSize)
        {
            var chunk = candleList.Skip(i).Take(chunkSize).ToList();
            var keys = chunk.Select(c => (c.ProductKey, c.Interval, c.PeriodStart)).ToHashSet();
            var pairs = chunk.Select(c => (c.ProductKey, c.Interval)).Distinct().ToList();

            await using var context = await contextFactory.CreateDbContextAsync(ct);

            List<EFOhlcCandle> existing = [];
            foreach (var (productKey, interval) in pairs)
            {
                var periodStarts = chunk.Where(c => c.ProductKey == productKey && c.Interval == interval).Select(c => c.PeriodStart).Distinct().ToList();
                var found = await context.OhlcCandles
                    .Where(c => c.ProductKey == productKey && c.Interval == interval && periodStarts.Contains(c.PeriodStart))
                    .ToListAsync(ct);
                existing.AddRange(found);
            }

            var existingByKey = existing.ToDictionary(c => (c.ProductKey, c.Interval, c.PeriodStart));

            foreach (var candle in chunk)
            {
                var key = (candle.ProductKey, candle.Interval, candle.PeriodStart);
                if (existingByKey.TryGetValue(key, out var existingCandle))
                {
                    existingCandle.Open = candle.Open;
                    existingCandle.High = candle.High;
                    existingCandle.Low = candle.Low;
                    existingCandle.Close = candle.Close;
                    existingCandle.Volume = candle.Volume;
                    existingCandle.Spread = candle.Spread;
                    existingCandle.AskClose = candle.AskClose;
                }
                else
                {
                    await context.OhlcCandles.AddAsync(candle, ct);
                }
            }

            await context.SaveChangesAsync(ct);
        }
    }

    public async Task<DateTime?> GetLatestCandleTimeAsync(
        string productKey,
        CandleInterval interval,
        CancellationToken ct = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(ct);

        return await context.OhlcCandles
            .AsNoTracking()
            .Where(c => c.ProductKey == productKey && c.Interval == interval)
            .OrderByDescending(c => c.PeriodStart)
            .Select(c => (DateTime?)c.PeriodStart)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<List<string>> GetAllProductKeysAsync(CancellationToken ct = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(ct);

        return await context.Products
            .AsNoTracking()
            .Select(p => p.ProductKey)
            .ToListAsync(ct);
    }

    public async Task PruneOldTicksAsync(TimeSpan retention, CancellationToken ct = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(ct);
        var cutoff = DateTime.UtcNow - retention;

        await context.PriceTicks
            .Where(t => t.Timestamp < cutoff)
            .ExecuteDeleteAsync(ct);
    }

    public async Task PruneOldCandlesAsync(CancellationToken ct = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(ct);
        var now = DateTime.UtcNow;

        // 5-minute candles: 7 days retention
        await context.OhlcCandles
            .Where(c => c.Interval == CandleInterval.FiveMinute && c.PeriodStart < now.AddDays(-7))
            .ExecuteDeleteAsync(ct);

        // 15-minute candles: 30 days retention
        await context.OhlcCandles
            .Where(c => c.Interval == CandleInterval.FifteenMinute && c.PeriodStart < now.AddDays(-30))
            .ExecuteDeleteAsync(ct);

        // 1-hour candles: 90 days retention
        await context.OhlcCandles
            .Where(c => c.Interval == CandleInterval.OneHour && c.PeriodStart < now.AddDays(-90))
            .ExecuteDeleteAsync(ct);

        // 4-hour candles: 1 year retention
        await context.OhlcCandles
            .Where(c => c.Interval == CandleInterval.FourHour && c.PeriodStart < now.AddDays(-365))
            .ExecuteDeleteAsync(ct);

        // 1-day and 1-week candles: kept forever (no cleanup)
    }

    public async Task VacuumDatabaseAsync(CancellationToken ct = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(ct);
        await context.Database.ExecuteSqlRawAsync("VACUUM", ct);
    }
}
