using System.Collections.Concurrent;
using BazaarCompanionWeb.Dtos;

namespace BazaarCompanionWeb.Services;

/// <summary>
/// Tracks the current candle state for each product to send proper OHLC data via SignalR.
/// Candles are tracked per-minute (the API polling interval) and aggregated properly.
/// </summary>
public class LiveCandleTracker
{
    private readonly ConcurrentDictionary<string, CandleState> _candleStates = new();
    
    /// <summary>
    /// Updates the candle state for a product and returns the current OHLC values.
    /// </summary>
    /// <param name="productKey">The product identifier</param>
    /// <param name="bidPrice">Current bid price (used for OHLC candles)</param>
    /// <param name="askPrice">Current ask price (used for line overlay)</param>
    /// <param name="volume">Current volume</param>
    /// <returns>A LiveTick with proper OHLC aggregation</returns>
    public LiveTick UpdateAndGetTick(string productKey, double bidPrice, double askPrice, double volume)
    {
        var now = DateTime.UtcNow;
        var periodStart = GetMinutePeriodStart(now);
        
        var state = _candleStates.AddOrUpdate(
            productKey,
            // Add new state if not exists
            _ => new CandleState
            {
                PeriodStart = periodStart,
                Open = bidPrice,
                High = bidPrice,
                Low = bidPrice,
                Close = bidPrice,
                AskClose = askPrice,
                Volume = volume
            },
            // Update existing state
            (_, existing) =>
            {
                // If we're in a new period, reset the candle
                if (existing.PeriodStart < periodStart)
                {
                    return new CandleState
                    {
                        PeriodStart = periodStart,
                        Open = bidPrice,
                        High = bidPrice,
                        Low = bidPrice,
                        Close = bidPrice,
                        AskClose = askPrice,
                        Volume = volume
                    };
                }
                
                // Same period - update high/low/close
                existing.High = Math.Max(existing.High, bidPrice);
                existing.Low = Math.Min(existing.Low, bidPrice);
                existing.Close = bidPrice;
                existing.AskClose = askPrice; // Always use latest ASK for line
                existing.Volume = volume; // Use latest volume snapshot
                return existing;
            });
        
        return new LiveTick(
            periodStart,
            state.Open,
            state.High,
            state.Low,
            state.Close,
            state.Volume,
            state.AskClose);
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
        public double AskClose { get; set; }
        public double Volume { get; set; }
    }
}
