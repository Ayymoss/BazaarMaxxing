using System.Collections.Concurrent;
using BazaarCompanionWeb.Dtos;
using BazaarCompanionWeb.Services.Ingestion;

namespace BazaarCompanionWeb.Services;

/// <summary>
/// Tracks the forming one-minute bar for each product so live pushes carry proper OHLC data.
/// Bars are per minute (the API polling interval); the chart folds them into its own interval.
/// Volume is the units traded in the minute, accumulated across polls that land in the same minute.
/// </summary>
public class LiveCandleTracker
{
    private readonly ConcurrentDictionary<string, CandleState> _candleStates = new();

    /// <summary>
    /// Updates the bar for a product and returns its current values.
    /// </summary>
    /// <param name="productKey">The product identifier</param>
    /// <param name="bidPrice">Current best bid (the main candle)</param>
    /// <param name="askPrice">Current best ask (the ask candle)</param>
    /// <param name="traded">Units traded since the previous poll</param>
    public LiveTick UpdateAndGetTick(string productKey, double bidPrice, double askPrice, TradedDelta traded)
    {
        var now = DateTime.UtcNow;
        var periodStart = GetMinutePeriodStart(now);

        var state = _candleStates.AddOrUpdate(
            productKey,
            _ => CandleState.Start(periodStart, bidPrice, askPrice, traded),
            (_, existing) =>
            {
                // A new minute starts a fresh bar.
                if (existing.PeriodStart < periodStart)
                    return CandleState.Start(periodStart, bidPrice, askPrice, traded);

                existing.High = Math.Max(existing.High, bidPrice);
                existing.Low = Math.Min(existing.Low, bidPrice);
                existing.Close = bidPrice;
                existing.AskHigh = Math.Max(existing.AskHigh, askPrice);
                existing.AskLow = Math.Min(existing.AskLow, askPrice);
                existing.AskClose = askPrice;
                existing.BuyVolume += traded.Buy;
                existing.SellVolume += traded.Sell;
                return existing;
            });

        return new LiveTick(
            periodStart,
            state.Open,
            state.High,
            state.Low,
            state.Close,
            state.BuyVolume + state.SellVolume,
            state.AskClose,
            state.BuyVolume,
            state.SellVolume,
            state.AskOpen,
            state.AskHigh,
            state.AskLow);
    }

    /// <summary>
    /// Gets the start of the current minute period.
    /// </summary>
    private static DateTime GetMinutePeriodStart(DateTime timestamp)
    {
        return new DateTime(timestamp.Year, timestamp.Month, timestamp.Day,
            timestamp.Hour, timestamp.Minute, 0, DateTimeKind.Utc);
    }

    /// <summary>
    /// Cleans up old candle states (call periodically to prevent memory leaks).
    /// </summary>
    public void CleanupOldStates()
    {
        var threshold = DateTime.UtcNow.AddMinutes(-5);
        var keysToRemove = _candleStates
            .Where(kvp => kvp.Value.PeriodStart < threshold)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var key in keysToRemove)
        {
            _candleStates.TryRemove(key, out _);
        }
    }

    private class CandleState
    {
        public DateTime PeriodStart { get; set; }
        public double Open { get; set; }
        public double High { get; set; }
        public double Low { get; set; }
        public double Close { get; set; }
        public double AskOpen { get; set; }
        public double AskHigh { get; set; }
        public double AskLow { get; set; }
        public double AskClose { get; set; }
        public double BuyVolume { get; set; }
        public double SellVolume { get; set; }

        public static CandleState Start(DateTime periodStart, double bid, double ask, TradedDelta traded) => new()
        {
            PeriodStart = periodStart,
            Open = bid,
            High = bid,
            Low = bid,
            Close = bid,
            AskOpen = ask,
            AskHigh = ask,
            AskLow = ask,
            AskClose = ask,
            BuyVolume = traded.Buy,
            SellVolume = traded.Sell,
        };
    }
}
