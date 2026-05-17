using BazaarCompanionWeb.Context;
using BazaarCompanionWeb.Dtos;
using BazaarCompanionWeb.Entities;
using BazaarCompanionWeb.Interfaces.Database;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace BazaarCompanionWeb.Repositories;

public class OhlcRepository(IDbContextFactory<DataContext> contextFactory, ILogger<OhlcRepository> logger) : IOhlcRepository
{
    public async Task CopyTicksAsync(IReadOnlyList<EFPriceTick> ticks, CancellationToken ct = default)
    {
        if (ticks.Count == 0) return;

        var sw = System.Diagnostics.Stopwatch.StartNew();
        await using var context = await contextFactory.CreateDbContextAsync(ct);
        var conn = (NpgsqlConnection)context.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open)
            await conn.OpenAsync(ct);

        await using var importer = await conn.BeginBinaryImportAsync(
            "COPY \"EFPriceTicks\" (\"ProductKey\", \"BidPrice\", \"AskPrice\", \"Timestamp\", \"BidVolume\", \"AskVolume\") FROM STDIN (FORMAT BINARY)",
            ct);

        foreach (var t in ticks)
        {
            await importer.StartRowAsync(ct);
            await importer.WriteAsync(t.ProductKey, NpgsqlDbType.Varchar, ct);
            await importer.WriteAsync(t.BidPrice, NpgsqlDbType.Double, ct);
            await importer.WriteAsync(t.AskPrice, NpgsqlDbType.Double, ct);
            var ts = t.Timestamp.Kind == DateTimeKind.Utc ? t.Timestamp : DateTime.SpecifyKind(t.Timestamp, DateTimeKind.Utc);
            await importer.WriteAsync(ts, NpgsqlDbType.TimestampTz, ct);
            await importer.WriteAsync(t.BidVolume, NpgsqlDbType.Bigint, ct);
            await importer.WriteAsync(t.AskVolume, NpgsqlDbType.Bigint, ct);
        }

        await importer.CompleteAsync(ct);
        sw.Stop();
        if (sw.ElapsedMilliseconds > 1000)
            logger.LogWarning("Slow CopyTicksAsync: {ElapsedMs}ms, {Rows} rows",
                sw.ElapsedMilliseconds, ticks.Count);
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
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var totalRows = 0;

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

            totalRows += rows.Count;

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

        sw.Stop();
        // Warn-level only on outliers — successful queries are tracked at the calling layer.
        if (sw.ElapsedMilliseconds > 2000)
            logger.LogWarning(
                "Slow GetCandlesBulkAsync: {ElapsedMs}ms, {Products} keys, interval={Interval}, {Rows} rows",
                sw.ElapsedMilliseconds, productKeys.Count, interval, totalRows);

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

        var sw = System.Diagnostics.Stopwatch.StartNew();
        await using var context = await contextFactory.CreateDbContextAsync(ct);

        var ticks = await context.PriceTicks
            .AsNoTracking()
            .Where(t => productKeys.Contains(t.ProductKey) && t.Timestamp >= since)
            .OrderBy(t => t.ProductKey)
            .ThenBy(t => t.Timestamp)
            .ToListAsync(ct);

        sw.Stop();
        if (sw.ElapsedMilliseconds > 2000)
            logger.LogWarning(
                "Slow GetTicksForAggregationBulkAsync: {ElapsedMs}ms, {Products} keys, since={Since:O}, {Ticks} ticks",
                sw.ElapsedMilliseconds, productKeys.Count, since, ticks.Count);

        return ticks.GroupBy(t => t.ProductKey).ToDictionary(g => g.Key, g => g.ToList());
    }

    public async Task SaveCandlesAsync(IEnumerable<EFOhlcCandle> candles, CancellationToken ct = default)
    {
        var candleList = candles.ToList();
        if (candleList.Count == 0) return;
        var sw = System.Diagnostics.Stopwatch.StartNew();

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

        sw.Stop();
        if (sw.ElapsedMilliseconds > 5000)
            logger.LogWarning("Slow SaveCandlesAsync: {ElapsedMs}ms, {Rows} candles upserted",
                sw.ElapsedMilliseconds, candleList.Count);
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

    public async Task<IReadOnlyDictionary<(string, CandleInterval), EFOhlcAggregationState>> GetAggregationStatesAsync(CancellationToken ct = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(ct);
        var rows = await context.OhlcAggregationStates.AsNoTracking().ToListAsync(ct);
        return rows.ToDictionary(s => (s.ProductKey, s.Interval));
    }

    public async Task UpsertAggregationStatesAsync(IReadOnlyList<EFOhlcAggregationState> states, CancellationToken ct = default)
    {
        if (states.Count == 0) return;

        await using var context = await contextFactory.CreateDbContextAsync(ct);
        var keys = states.Select(s => (s.ProductKey, s.Interval)).ToHashSet();
        var existing = await context.OhlcAggregationStates
            .Where(s => states.Select(x => x.ProductKey).Contains(s.ProductKey))
            .ToListAsync(ct);
        var existingByKey = existing.ToDictionary(s => (s.ProductKey, s.Interval));

        foreach (var incoming in states)
        {
            var ts = incoming.LastSeenPeriodStart.Kind == DateTimeKind.Utc
                ? incoming.LastSeenPeriodStart
                : DateTime.SpecifyKind(incoming.LastSeenPeriodStart, DateTimeKind.Utc);
            var updatedAt = incoming.UpdatedAt.Kind == DateTimeKind.Utc
                ? incoming.UpdatedAt
                : DateTime.SpecifyKind(incoming.UpdatedAt, DateTimeKind.Utc);

            if (existingByKey.TryGetValue((incoming.ProductKey, incoming.Interval), out var current))
            {
                current.LastSeenPeriodStart = ts;
                current.UpdatedAt = updatedAt;
            }
            else
            {
                await context.OhlcAggregationStates.AddAsync(new EFOhlcAggregationState
                {
                    ProductKey = incoming.ProductKey,
                    Interval = incoming.Interval,
                    LastSeenPeriodStart = ts,
                    UpdatedAt = updatedAt,
                }, ct);
            }
        }

        await context.SaveChangesAsync(ct);
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
