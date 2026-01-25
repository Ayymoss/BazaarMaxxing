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
            .Select(c => new OhlcDataPoint(c.PeriodStart, c.Open, c.High, c.Low, c.Close, c.Volume, c.Spread))
            .ToListAsync(ct);

        return candles;
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
            .Select(c => new OhlcDataPoint(c.PeriodStart, c.Open, c.High, c.Low, c.Close, c.Volume, c.Spread))
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

    public async Task SaveCandlesAsync(IEnumerable<EFOhlcCandle> candles, CancellationToken ct = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(ct);

        foreach (var candle in candles)
        {
            var existing = await context.OhlcCandles
                .FirstOrDefaultAsync(c =>
                    c.ProductKey == candle.ProductKey &&
                    c.Interval == candle.Interval &&
                    c.PeriodStart == candle.PeriodStart, ct);

            if (existing is not null)
            {
                existing.Open = candle.Open;
                existing.High = candle.High;
                existing.Low = candle.Low;
                existing.Close = candle.Close;
                existing.Volume = candle.Volume;
                existing.Spread = candle.Spread;
            }
            else
            {
                await context.OhlcCandles.AddAsync(candle, ct);
            }
        }

        await context.SaveChangesAsync(ct);
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
