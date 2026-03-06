namespace BazaarCompanionWeb.Services;

/// <summary>
/// Estimates Last Traded Price (LTP) from volume deltas between consecutive API polls.
/// Hypixel doesn't provide LTP directly, so we infer trade direction from changes in
/// outstanding order volume: decreased bid volume = sells hit the bid, decreased ask
/// volume = buys hit the ask. An EMA with confidence-weighted alpha smooths noise
/// from cancellations and race conditions.
/// </summary>
public sealed class LastTradedPriceService
{
    private const double BaseAlpha = 0.3;
    private const double VolumeConfidenceScale = 1000.0;

    private readonly Lock _lock = new();
    private readonly Dictionary<string, (int BidVolume, int AskVolume)> _previousVolumes = new();
    private readonly Dictionary<string, double> _ltpEstimates = new();

    /// <summary>
    /// Update volume state and return the smoothed LTP estimate for a product.
    /// Call this for every product on every poll (not just changed ones).
    /// </summary>
    public double? UpdateAndEstimate(
        string productKey,
        double bestBid,
        double bestAsk,
        int currentBidVolume,
        int currentAskVolume)
    {
        lock (_lock)
        {
            if (!_previousVolumes.TryGetValue(productKey, out var prev))
            {
                // First poll for this product — store volumes, no estimate yet
                _previousVolumes[productKey] = (currentBidVolume, currentAskVolume);
                return _ltpEstimates.TryGetValue(productKey, out var v) ? v : null;
            }

            // Volume consumed = orders that were filled (or cancelled — EMA handles noise)
            var bidConsumed = Math.Max(0, prev.BidVolume - currentBidVolume);
            var askConsumed = Math.Max(0, prev.AskVolume - currentAskVolume);

            // Store current volumes for next poll
            _previousVolumes[productKey] = (currentBidVolume, currentAskVolume);

            var totalConsumed = bidConsumed + askConsumed;

            // No volume consumed on either side — preserve current estimate
            if (totalConsumed == 0)
                return _ltpEstimates.TryGetValue(productKey, out var v) ? v : null;

            // Raw LTP estimate weighted by which side was consumed
            double rawEstimate;
            if (bidConsumed > 0 && askConsumed > 0)
                rawEstimate = ((double)bidConsumed * bestBid + (double)askConsumed * bestAsk) / totalConsumed;
            else if (bidConsumed > 0)
                rawEstimate = bestBid;
            else
                rawEstimate = bestAsk;

            // EMA with confidence-weighted alpha
            var volumeFactor = Math.Clamp(totalConsumed / VolumeConfidenceScale, 0, 1);
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
