using BazaarCompanionWeb.Context;
using BazaarCompanionWeb.Entities;
using BazaarCompanionWeb.Interfaces.Database;
using Microsoft.EntityFrameworkCore;

namespace BazaarCompanionWeb.Services;

public class OhlcAggregationService(
    IServiceScopeFactory scopeFactory,
    ILogger<OhlcAggregationService> logger) : BackgroundService
{
    private static readonly TimeSpan AggregationInterval = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan TickRetention = TimeSpan.FromDays(7);
    private bool _historySeeded = false;

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
                var dbContext = scope.ServiceProvider.GetRequiredService<DataContext>();
                
                if (!_historySeeded)
                {
                    await SeedHistoryAsync(ohlcRepository, dbContext, stoppingToken);
                    _historySeeded = true;
                }

                await AggregateAllCandlesAsync(ohlcRepository, stoppingToken);
                await ohlcRepository.PruneOldTicksAsync(TickRetention, stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error during OHLC aggregation cycle");
            }

            await Task.Delay(AggregationInterval, stoppingToken);
        }
    }

    private async Task SeedHistoryAsync(IOhlcRepository ohlcRepository, DataContext dbContext, CancellationToken ct)
    {
        logger.LogInformation("Seeding historical daily candles from PriceSnapshots...");
        
        var snapshots = await dbContext.PriceSnapshots
            .AsNoTracking()
            .OrderBy(x => x.Taken)
            .ToListAsync(ct);

        if (snapshots.Count == 0) return;

        var candles = snapshots.Select(s => new EFOhlcCandle
        {
            ProductKey = s.ProductKey,
            Interval = CandleInterval.Daily,
            PeriodStart = s.Taken.ToDateTime(TimeOnly.MinValue),
            Open = s.BuyUnitPrice,
            High = s.BuyUnitPrice,
            Low = s.BuyUnitPrice,
            Close = s.BuyUnitPrice,
            Volume = null // Historical snapshots don't have volume
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

    private async Task AggregateIntervalAsync(IOhlcRepository ohlcRepository, string productKey, CandleInterval interval, CancellationToken ct)
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
            .GroupBy(t => GetPeriodStart(t.Timestamp, interval))
            .ToList();

        if (grouped.Count is 0) return;

        List<EFOhlcCandle> candles =
        [
            ..grouped.Select(g =>
            {
                var orderedTicks = g.OrderBy(t => t.Timestamp).ToList();
                var totalVolume = orderedTicks
                    .Where(t => t.BuyVolume.HasValue && t.SellVolume.HasValue)
                    .Sum(t => (t.BuyVolume ?? 0) + (t.SellVolume ?? 0));
                
                return new EFOhlcCandle
                {
                    ProductKey = productKey,
                    Interval = interval,
                    PeriodStart = g.Key,
                    Open = orderedTicks.First().BuyPrice,
                    High = orderedTicks.Max(t => t.BuyPrice),
                    Low = orderedTicks.Min(t => t.BuyPrice),
                    Close = orderedTicks.Last().BuyPrice,
                    Volume = totalVolume > 0 ? totalVolume : null
                };
            })
        ];

        await ohlcRepository.SaveCandlesAsync(candles, ct);
    }

    private static DateTime GetPeriodStart(DateTime timestamp, CandleInterval interval)
    {
        if (interval == CandleInterval.Weekly)
        {
            // Start of week (Monday)
            var diff = (7 + (timestamp.DayOfWeek - DayOfWeek.Monday)) % 7;
            return timestamp.AddDays(-1 * diff).Date;
        }

        if (interval == CandleInterval.Daily)
        {
            return timestamp.Date;
        }

        var intervalMinutes = (int)interval;
        var totalMinutesSinceEpoch = (long)(timestamp - DateTime.UnixEpoch).TotalMinutes;
        var periodMinutes = totalMinutesSinceEpoch / intervalMinutes * intervalMinutes;
        return DateTime.UnixEpoch.AddMinutes(periodMinutes);
    }
}
