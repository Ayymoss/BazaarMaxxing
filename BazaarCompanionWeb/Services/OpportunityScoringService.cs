using BazaarCompanionWeb.Dtos;
using BazaarCompanionWeb.Interfaces;

namespace BazaarCompanionWeb.Services;

public class OpportunityScoringService(ILogger<OpportunityScoringService> logger) : IOpportunityScoringService
{
    private const int MinCandlesForAnalysis = 6;
    private const double RiskBufferPercentage = 0.005;
    private const double MinSpreadStability = 0.1;
    private const int ManipulationMinCandles = 24;
    private const double ManipulationZScoreThreshold = 1.5;

    // Hypixel Specific Constants
    private const double BazaarTaxRate = 0.01125; // 1.125% (Standard for God Potion/Cookie users)
    private const long MinWeeklyVolumeForFlipping = 150_000;

    // Sweet Spot Pricing Constants
    private const double TargetPriceMidpoint = 50_000; // The "perfect" price point
    private const double PriceRangeWidth = 1.5; // Controls how fast the score drops outside the sweet spot
    private const double MinWorthwhilePrice = 100.0; // Items below this are considered "dust" unless ROI is insane

    // Hard rejection thresholds
    private const long MinAskWeeklyVolume = 25_000; // Must be able to sell
    private const double MinAskRatio = 0.30; // At least 30% of volume must be on the ask side (matches balance factor cutoff)
    private const double MinAbsoluteSpread = 100.0; // Spread must be at least 100 coins to be worth executing
    private const double MinBidPrice = 100.0; // Items below this bid price are impractical to flip at scale

    // Z-score normalization constants
    private const double ZScoreBase = 2.0; // Mean raw score maps to this (average tradeable item)
    private const double ZScoreScale = 1.5; // Each standard deviation adds this many points
    // Result: mean→2.0, +1σ→3.5, +2σ→5.0 (exceptional), +3.3σ→7.0 (incredible), +5.3σ→10.0 (unattainable)
    private const int MinSamplesForZScore = 10; // Need enough data for meaningful statistics

    public (IReadOnlyList<double> OpportunityScores, IReadOnlyList<ManipulationScore> ManipulationScores) CalculateScoresBatch(
        IReadOnlyList<ScoringProductInput> products,
        IReadOnlyDictionary<string, List<OhlcDataPoint>> candlesByProduct)
    {
        var rawScores = new double[products.Count];
        var manipulationScores = new ManipulationScore[products.Count];

        // Phase 1: Calculate raw (log-compressed) scores for each product
        for (var i = 0; i < products.Count; i++)
        {
            var p = products[i];
            var candles = candlesByProduct.TryGetValue(p.ProductKey, out var c) ? c : [];

            double rawScore;
            if (p.BidPrice >= p.AskPrice || p.AskPrice <= 0)
            {
                rawScore = 0;
            }
            else if (FailsHardGates(p.BidPrice, p.AskPrice, p.BidMovingWeek, p.AskMovingWeek))
            {
                rawScore = 0;
            }
            else if (candles.Count >= MinCandlesForAnalysis)
            {
                rawScore = CalculateAdvancedScore(p.BidPrice, p.AskPrice, p.BidMovingWeek, p.AskMovingWeek, candles);
            }
            else
            {
                rawScore = CalculateSimplifiedScore(p.BidPrice, p.AskPrice, p.BidMovingWeek, p.AskMovingWeek);
                logger.LogDebug("Insufficient candles ({Candles}/{TargetCandles}) for {ProductKey}, using simplified scoring",
                    candles.Count, MinCandlesForAnalysis, p.ProductKey);
            }

            rawScores[i] = rawScore;
            manipulationScores[i] = CalculateManipulationScoreFromCandles(p.BidPrice, candles);
        }

        // Phase 2: Z-score normalize across all non-zero scores
        var opportunityScores = NormalizeToZScores(rawScores);

        return (opportunityScores, manipulationScores);
    }

    /// <summary>
    /// Normalizes raw scores to a 0-10 scale using z-score distribution.
    /// Zero scores (hard-rejected items) remain zero. Non-zero scores are
    /// positioned relative to the market: mean→2.0, each σ adds 1.5 points.
    /// This ensures >5 is exceptional (+2σ, top ~2.5%) and 10 is unattainable (+5.3σ).
    /// </summary>
    private static double[] NormalizeToZScores(double[] rawScores)
    {
        var result = new double[rawScores.Length];

        // Collect non-zero scores for statistics
        var nonZero = rawScores.Where(s => s > 0).ToList();

        if (nonZero.Count < MinSamplesForZScore)
        {
            // Not enough data for meaningful z-scores — use simple rank-based fallback
            // This handles edge cases like first startup or very few tradeable items
            if (nonZero.Count == 0) return result;

            var maxRaw = nonZero.Max();
            for (var i = 0; i < rawScores.Length; i++)
            {
                if (rawScores[i] > 0)
                    result[i] = Math.Clamp((rawScores[i] / maxRaw) * 4.0, 0.1, 4.0);
            }

            return result;
        }

        var mean = nonZero.Average();
        var stdDev = Math.Sqrt(nonZero.Sum(s => Math.Pow(s - mean, 2)) / nonZero.Count);

        // Safety: if all scores are identical, stdDev is 0
        if (stdDev < mean * 0.001)
            stdDev = mean * 0.001;

        for (var i = 0; i < rawScores.Length; i++)
        {
            if (rawScores[i] <= 0)
            {
                result[i] = 0;
                continue;
            }

            var z = (rawScores[i] - mean) / stdDev;
            result[i] = Math.Clamp(ZScoreBase + (z * ZScoreScale), 0.1, 10.0);
        }

        return result;
    }

    /// <summary>
    /// Hard rejection gates — items failing these are fundamentally untradeable.
    /// No amount of ROI or volume should rescue them.
    /// </summary>
    private static bool FailsHardGates(double bidPrice, double askPrice, long bidMovingWeek, long askMovingWeek)
    {
        // Items below minimum bid price are impractical to flip at scale
        if (bidPrice < MinBidPrice)
            return true;

        // Must have minimum ask-side volume to ensure we can sell
        if (askMovingWeek < MinAskWeeklyVolume)
            return true;

        // Spread must be worth executing
        var spread = askPrice - bidPrice;
        if (spread < MinAbsoluteSpread)
            return true;

        // Must have minimum proportion of volume on the ask (sell) side
        var totalVolume = bidMovingWeek + askMovingWeek;
        if (totalVolume > 0)
        {
            var askRatio = (double)askMovingWeek / totalVolume;
            if (askRatio < MinAskRatio)
                return true;
        }

        return false;
    }

    private double CalculateAdvancedScore(
        double bidPrice,
        double askPrice,
        long bidMovingWeek,
        long askMovingWeek,
        List<OhlcDataPoint> candles)
    {
        var netProfit = (askPrice * (1.0 - BazaarTaxRate)) - bidPrice;
        if (netProfit <= 0) return 0;

        var roi = bidPrice > 0 ? netProfit / bidPrice : 0;

        var volatility = CalculateVolatility(candles);
        var spreadStability = CalculateSpreadStability(candles);
        var volumeScore = CalculateVolumeScore(bidMovingWeek, askMovingWeek);
        var trendFactor = CalculateTrendFactor(candles);
        var meanPrice = candles.Average(c => c.Close);
        var riskBuffer = meanPrice * RiskBufferPercentage;
        var adjustedVolatility = Math.Max(volatility, riskBuffer);

        var sweetSpotFactor = CalculateSweetSpotFactor(bidPrice);

        // Spread-percent based capital factor: penalizes excessive spread percentages
        var spreadPercent = (askPrice - bidPrice) / bidPrice;
        var capitalFactor = CalculateSpreadPercentFactor(spreadPercent);

        var denominator = adjustedVolatility + (bidPrice * 0.001);
        var baseScore = (netProfit * volumeScore * spreadStability * sweetSpotFactor * capitalFactor) / denominator;

        // Capped ROI boost: prevent high ROI from rescuing bad fundamentals
        var roiBoost = 1.0 + Math.Log10(1.0 + Math.Min(roi, 2.0)) * 0.3;
        var finalScore = baseScore * trendFactor * roiBoost;

        // Return log-compressed raw score (normalization happens in batch via z-scores)
        return Math.Log10(1 + Math.Max(0, finalScore));
    }

    /// <summary>
    /// Spread-percent factor: no penalty up to 500% spread.
    /// Gentle decay from 500% to 10000% (Hypixel markets are volatile).
    /// High spreads with healthy ask volume can still be genuine opportunities
    /// (e.g., bid-side depression).
    /// </summary>
    private static double CalculateSpreadPercentFactor(double spreadPercent)
    {
        if (spreadPercent <= 5.0)
            return 1.0; // No penalty up to 500% spread

        if (spreadPercent <= 100.0)
        {
            // Gentle decay from 1.0 at 500% to 0.3 at 10000%
            return 1.0 - ((spreadPercent - 5.0) / 95.0 * 0.7);
        }

        // Beyond 10000% — minimal score but not zero (volume gates are the real protection)
        return 0.3;
    }

    private static double CalculateVolumeScore(long bidMovingWeek, long askMovingWeek)
    {
        var totalVolume = bidMovingWeek + askMovingWeek;
        if (totalVolume <= 0) return 0;

        var executionFactor = totalVolume >= MinWeeklyVolumeForFlipping
            ? 1.0
            : Math.Pow((double)totalVolume / MinWeeklyVolumeForFlipping, 2);

        var hourlyVolume = totalVolume / 168.0;
        var throughputScore = Math.Min(1.0, hourlyVolume / 5_000.0);

        // Calculate the proportion of volume on the asking side (0.0 to 1.0)
        var askRatio = (double)askMovingWeek / totalVolume;
        double balanceFactor;

        if (askRatio >= 0.5)
        {
            // PREFERRED SKEW: Ask volume >= Bid volume (Easy to offload)
            // Slight penalty for extreme ask dominance, but still good.
            balanceFactor = 1.0 - ((askRatio - 0.5) * 0.6);
        }
        else if (askRatio >= 0.3)
        {
            // MARGINAL SKEW: Bid volume is higher but not critically so.
            // Square root ramp from 0 at 0.3 to 1.0 at 0.5.
            // Gentle near 0.5 (near-balanced markets aren't over-penalized),
            // steep near 0.3 cutoff.
            balanceFactor = Math.Sqrt((askRatio - 0.3) / 0.2);
        }
        else
        {
            // DANGEROUS SKEW: Less than 30% ask volume — hard kill.
            // Should already be caught by FailsHardGates, but safety net.
            balanceFactor = 0;
        }

        return throughputScore * executionFactor * balanceFactor;
    }

    private double CalculateVolatility(List<OhlcDataPoint> candles)
    {
        if (candles.Count < 2) return 0;
        var returns = new List<double>();
        for (var i = 1; i < candles.Count; i++)
        {
            if (candles[i - 1].Close > 0)
                returns.Add((candles[i].Close - candles[i - 1].Close) / candles[i - 1].Close);
        }

        if (returns.Count == 0) return 0;
        var meanReturn = returns.Average();
        var stdDev = Math.Sqrt(returns.Sum(r => Math.Pow(r - meanReturn, 2)) / returns.Count);
        return stdDev * candles.Average(c => c.Close);
    }

    private double CalculateSpreadStability(List<OhlcDataPoint> candles)
    {
        if (candles.Count < 2) return MinSpreadStability;
        var variations = candles.Select(c => (c.High - c.Low) / Math.Max(1, c.Close)).ToList();
        var avgVar = variations.Average();
        var stdDev = Math.Sqrt(variations.Sum(v => Math.Pow(v - avgVar, 2)) / variations.Count);
        return Math.Clamp(1.0 / (1.0 + stdDev), MinSpreadStability, 1.0);
    }

    private double CalculateTrendFactor(List<OhlcDataPoint> candles)
    {
        if (candles.Count < 5) return 1.0;
        var recent = candles.TakeLast(5).ToList();
        var sma = recent.Average(c => c.Close);
        var current = candles.Last().Close;
        var diff = (current - sma) / sma;
        return Math.Clamp(1.0 + diff, 0.8, 1.2);
    }

    private double CalculateSimplifiedScore(double bidPrice, double askPrice, long bidMovingWeek, long askMovingWeek)
    {
        var netProfit = (askPrice * (1.0 - BazaarTaxRate)) - bidPrice;
        if (netProfit <= 0) return 0;

        var roi = netProfit / bidPrice;
        var volumeScore = CalculateVolumeScore(bidMovingWeek, askMovingWeek);

        // SWEET SPOT LOGIC: Favor items between 100 and 100k
        var sweetSpotFactor = CalculateSweetSpotFactor(bidPrice);

        // DUST PENALTY: Specifically target items worth less than 100 coins.
        var dustPenalty = bidPrice < MinWorthwhilePrice
            ? Math.Pow(bidPrice / MinWorthwhilePrice, 2)
            : 1.0;

        // FEASIBILITY CHECK: High ROI alone is suspicious — spread traps.
        double feasibilityPenalty = 1.0;
        if (roi > 3.0)
        {
            feasibilityPenalty = 1.0 / (1.0 + Math.Log10(roi));
        }

        // Spread-percent penalty (same as advanced scoring)
        var spreadPercent = (askPrice - bidPrice) / bidPrice;
        var spreadFactor = CalculateSpreadPercentFactor(spreadPercent);

        var baseValue = (roi * 10.0) * volumeScore * sweetSpotFactor * dustPenalty * feasibilityPenalty * spreadFactor;

        // Return log-compressed raw score (normalization happens in batch via z-scores)
        return Math.Log10(1 + Math.Max(0, baseValue));
    }

    private double CalculateSweetSpotFactor(double price)
    {
        if (price <= 0) return 0.01;

        var logPrice = Math.Log10(price);
        var logTarget = Math.Log10(TargetPriceMidpoint);

        var exponent = -Math.Pow(logPrice - logTarget, 2) / (2 * Math.Pow(PriceRangeWidth, 2));
        var factor = Math.Exp(exponent);

        return Math.Max(0.15, factor);
    }

    private static ManipulationScore CalculateManipulationScoreFromCandles(double currentBid, List<OhlcDataPoint> candles)
    {
        if (candles.Count < ManipulationMinCandles) return new ManipulationScore(false, 0, 0, 0);
        var prices = candles.Select(c => c.Close).ToList();
        var mean = prices.Average();
        var stdDev = Math.Sqrt(prices.Sum(p => Math.Pow(p - mean, 2)) / prices.Count);
        if (stdDev < mean * 0.001) stdDev = mean * 0.001;
        var z = (currentBid - mean) / stdDev;
        var isManipulated = Math.Abs(z) > ManipulationZScoreThreshold;
        var deviation = ((currentBid - mean) / mean) * 100;
        return new ManipulationScore(isManipulated, z, deviation, isManipulated ? Math.Min(1.0, Math.Abs(z) / 5.0) : 0);
    }
}
