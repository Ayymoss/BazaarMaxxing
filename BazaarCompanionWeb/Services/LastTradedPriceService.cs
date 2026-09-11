using BazaarCompanionWeb.Services.Ingestion;

namespace BazaarCompanionWeb.Services;

/// <summary>
/// Estimates Last Traded Price (LTP) from the units that traded between consecutive API polls.
/// Hypixel doesn't provide LTP directly, but it does count fills per side: instant buys consume the
/// best asks and instant sells hit the best bids (see <see cref="TradedDelta"/>). The raw estimate is
/// the fill-weighted average of the two touch prices; an EMA with confidence-weighted alpha smooths
/// the one-minute sampling (the touch can move within a poll).
///
/// Until 2026-09-11 this inferred fills from drops in resting order-book depth, which counted every
/// cancelled order as a trade.
/// </summary>
public sealed class LastTradedPriceService
{
    private const double BaseAlpha = 0.3;
    private const double VolumeConfidenceScale = 1000.0;

    private readonly Lock _lock = new();
    private readonly Dictionary<string, double> _ltpEstimates = new();

    /// <summary>
    /// Fold one poll's fills into the smoothed LTP estimate for a product. Call this for every
    /// product on every poll (not just changed ones).
    /// </summary>
    public double? UpdateAndEstimate(string productKey, double bestBid, double bestAsk, TradedDelta traded)
    {
        lock (_lock)
        {
            var total = traded.Total;

            // Nothing traded — preserve current estimate
            if (total <= 0)
                return _ltpEstimates.TryGetValue(productKey, out var v) ? v : null;

            // Instant buys filled at the ask, instant sells at the bid
            var rawEstimate = (traded.Buy * bestAsk + traded.Sell * bestBid) / total;

            // EMA with confidence-weighted alpha
            var volumeFactor = Math.Clamp(total / VolumeConfidenceScale, 0, 1);
            var alpha = BaseAlpha + (1 - BaseAlpha) * volumeFactor * 0.5;

            if (_ltpEstimates.TryGetValue(productKey, out var previousLtp))
            {
                var smoothed = alpha * rawEstimate + (1 - alpha) * previousLtp;
                _ltpEstimates[productKey] = smoothed;
                return smoothed;
            }

            // First estimate — use raw value directly
            _ltpEstimates[productKey] = rawEstimate;
            return rawEstimate;
        }
    }

    /// <summary>
    /// Get the current LTP estimate without updating (for page loads between polls).
    /// </summary>
    public double? GetEstimate(string productKey)
    {
        lock (_lock)
        {
            return _ltpEstimates.TryGetValue(productKey, out var v) ? v : null;
        }
    }
}
