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
    private const string TickColumns =
        "\"ProductKey\", \"Timestamp\", \"BidOpen\", \"BidHigh\", \"BidLow\", \"BidPrice\", " +
        "\"AskOpen\", \"AskHigh\", \"AskLow\", \"AskPrice\", \"BidVolume\", \"AskVolume\", \"TradedBuy\", \"TradedSell\", \"TradedEstimated\", \"FlowEvidenceKnown\"";

    /// <summary>
    /// Upserts five-minute bars by (product, bucket). A forming bar is flushed every cycle it changes, so the
    /// same key arrives repeatedly with fuller numbers each time; the last write wins. Rows go in through a
    /// binary COPY into a temp table and one INSERT ... ON CONFLICT, which keeps the batch at COPY speed.
    /// </summary>
    public async Task CopyTicksAsync(IReadOnlyList<EFPriceTick> ticks, CancellationToken ct = default)
    {
        if (ticks.Count == 0) return;

        var sw = System.Diagnostics.Stopwatch.StartNew();
        await using var context = await contextFactory.CreateDbContextAsync(ct);
        var conn = (NpgsqlConnection)context.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open)
            await conn.OpenAsync(ct);

        await using var tx = await conn.BeginTransactionAsync(ct);

        await using (var create = new NpgsqlCommand(
                         $"CREATE TEMP TABLE tmp_ticks ON COMMIT DROP AS SELECT {TickColumns} FROM \"EFPriceTicks\" WITH NO DATA",
                         conn, tx))
            await create.ExecuteNonQueryAsync(ct);

        await using (var importer = await conn.BeginBinaryImportAsync(
                         $"COPY tmp_ticks ({TickColumns}) FROM STDIN (FORMAT BINARY)", ct))
        {
            foreach (var t in ticks)
            {
                await importer.StartRowAsync(ct);
                await importer.WriteAsync(t.ProductKey, NpgsqlDbType.Varchar, ct);
                var ts = t.Timestamp.Kind == DateTimeKind.Utc ? t.Timestamp : DateTime.SpecifyKind(t.Timestamp, DateTimeKind.Utc);
                await importer.WriteAsync(ts, NpgsqlDbType.TimestampTz, ct);
                await importer.WriteAsync(t.BidOpen, NpgsqlDbType.Double, ct);
                await importer.WriteAsync(t.BidHigh, NpgsqlDbType.Double, ct);
                await importer.WriteAsync(t.BidLow, NpgsqlDbType.Double, ct);
                await importer.WriteAsync(t.BidPrice, NpgsqlDbType.Double, ct);
                await importer.WriteAsync(t.AskOpen, NpgsqlDbType.Double, ct);
                await importer.WriteAsync(t.AskHigh, NpgsqlDbType.Double, ct);
                await importer.WriteAsync(t.AskLow, NpgsqlDbType.Double, ct);
                await importer.WriteAsync(t.AskPrice, NpgsqlDbType.Double, ct);
                await importer.WriteAsync(t.BidVolume, NpgsqlDbType.Bigint, ct);
                await importer.WriteAsync(t.AskVolume, NpgsqlDbType.Bigint, ct);
                await importer.WriteAsync(t.TradedBuy, NpgsqlDbType.Bigint, ct);
                await importer.WriteAsync(t.TradedSell, NpgsqlDbType.Bigint, ct);
                await importer.WriteAsync(t.TradedEstimated, NpgsqlDbType.Bigint, ct);
                await importer.WriteAsync(t.FlowEvidenceKnown, NpgsqlDbType.Boolean, ct);
            }

            await importer.CompleteAsync(ct);
        }

        await using (var upsert = new NpgsqlCommand(
                         $"INSERT INTO \"EFPriceTicks\" ({TickColumns}) SELECT {TickColumns} FROM tmp_ticks " +
                         "ON CONFLICT (\"ProductKey\", \"Timestamp\") DO UPDATE SET " +
                         "\"BidOpen\" = EXCLUDED.\"BidOpen\", \"BidHigh\" = EXCLUDED.\"BidHigh\", \"BidLow\" = EXCLUDED.\"BidLow\", " +
                         "\"BidPrice\" = EXCLUDED.\"BidPrice\", \"AskOpen\" = EXCLUDED.\"AskOpen\", \"AskHigh\" = EXCLUDED.\"AskHigh\", " +
                         "\"AskLow\" = EXCLUDED.\"AskLow\", \"AskPrice\" = EXCLUDED.\"AskPrice\", \"BidVolume\" = EXCLUDED.\"BidVolume\", " +
                         "\"AskVolume\" = EXCLUDED.\"AskVolume\", \"TradedBuy\" = EXCLUDED.\"TradedBuy\", \"TradedSell\" = EXCLUDED.\"TradedSell\", \"TradedEstimated\" = EXCLUDED.\"TradedEstimated\", \"FlowEvidenceKnown\" = EXCLUDED.\"FlowEvidenceKnown\"",
                         conn, tx))
            await upsert.ExecuteNonQueryAsync(ct);

        await tx.CommitAsync(ct);
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
            .Select(c => new OhlcDataPoint(c.PeriodStart, c.Open, c.High, c.Low, c.Close, c.Volume, c.Spread, c.AskClose,
                c.BuyVolume, c.SellVolume, c.AskOpen, c.AskHigh, c.AskLow, c.EstimatedVolume))
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

        // Push a time cutoff into the SQL WHERE so we don't pull the entire history per product
        // and then trim client-side. 2x safety margin covers gaps / carry-forward candles.
        var intervalSpan = interval switch
        {
            CandleInterval.FiveMinute => TimeSpan.FromMinutes(5),
            CandleInterval.FifteenMinute => TimeSpan.FromMinutes(15),
            CandleInterval.OneHour => TimeSpan.FromHours(1),
            CandleInterval.FourHour => TimeSpan.FromHours(4),
            CandleInterval.OneDay => TimeSpan.FromDays(1),
            CandleInterval.OneWeek => TimeSpan.FromDays(7),
            _ => TimeSpan.FromHours(1)
        };
        var cutoff = DateTime.UtcNow - TimeSpan.FromTicks(intervalSpan.Ticks * limitPerProduct * 2);

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
                .Where(c => chunk.Contains(c.ProductKey) && c.Interval == interval && c.PeriodStart >= cutoff)
                .OrderBy(c => c.ProductKey)
                .ThenByDescending(c => c.PeriodStart)
                .Select(c => new { c.ProductKey, c.PeriodStart, c.Open, c.High, c.Low, c.Close, c.Volume, c.Spread, c.AskClose,
                    c.BuyVolume, c.SellVolume, c.AskOpen, c.AskHigh, c.AskLow, c.EstimatedVolume })
                .ToListAsync(ct);

            totalRows += rows.Count;

            foreach (var group in rows.GroupBy(r => r.ProductKey))
            {
                var candles = group
                    .Take(limitPerProduct)
                    .OrderBy(x => x.PeriodStart)
                    .Select(x => new OhlcDataPoint(x.PeriodStart, x.Open, x.High, x.Low, x.Close, x.Volume, x.Spread, x.AskClose,
                        x.BuyVolume, x.SellVolume, x.AskOpen, x.AskHigh, x.AskLow, x.EstimatedVolume))
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
            .Select(c => new OhlcDataPoint(c.PeriodStart, c.Open, c.High, c.Low, c.Close, c.Volume, c.Spread, c.AskClose,
                c.BuyVolume, c.SellVolume, c.AskOpen, c.AskHigh, c.AskLow, c.EstimatedVolume))
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
                    existingCandle.BuyVolume = candle.BuyVolume;
                    existingCandle.SellVolume = candle.SellVolume;
                    existingCandle.EstimatedVolume = candle.EstimatedVolume;
                    existingCandle.Spread = candle.Spread;
                    existingCandle.AskClose = candle.AskClose;
                    existingCandle.AskOpen = candle.AskOpen;
                    existingCandle.AskHigh = candle.AskHigh;
                    existingCandle.AskLow = candle.AskLow;
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
