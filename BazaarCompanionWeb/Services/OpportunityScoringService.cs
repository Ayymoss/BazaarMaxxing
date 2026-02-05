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

    public (IReadOnlyList<double> OpportunityScores, IReadOnlyList<ManipulationScore> ManipulationScores) CalculateScoresBatch(
        IReadOnlyList<ScoringProductInput> products,
        IReadOnlyDictionary<string, List<OhlcDataPoint>> candlesByProduct)
    {
        var opportunityScores = new double[products.Count];
        var manipulationScores = new ManipulationScore[products.Count];

        for (var i = 0; i < products.Count; i++)
        {
            var p = products[i];
            var candles = candlesByProduct.TryGetValue(p.ProductKey, out var c) ? c : [];

            double oppScore;
            if (p.BidPrice >= p.AskPrice || p.AskPrice <= 0)
            {
                oppScore = 0;
            }
            else if (candles.Count >= MinCandlesForAnalysis)
            {
                oppScore = CalculateAdvancedScore(p.BidPrice, p.AskPrice, p.BidMovingWeek, p.AskMovingWeek, candles);
            }
            else
            {
                oppScore = CalculateSimplifiedScore(p.BidPrice, p.AskPrice, p.BidMovingWeek, p.AskMovingWeek);
                logger.LogDebug("Insufficient candles ({Candles}/{TargetCandles}) for {ProductKey}, using simplified scoring",
                    candles.Count, MinCandlesForAnalysis, p.ProductKey);
            }

            opportunityScores[i] = oppScore;
            manipulationScores[i] = CalculateManipulationScoreFromCandles(p.BidPrice, candles);
        }

        return (opportunityScores, manipulationScores);
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

        // Align with simple: high-ROI opportunities get a higher floor so we don't over-penalize cheap items with huge spread
        var sweetSpotFactor = Math.Max(CalculateSweetSpotFactor(bidPrice), GetHighRoiSweetSpotFloor(roi));

        // Capital potential: spread/ask < 100 → rating < 1 (Meh). Tiers: 0–1 Meh, 1–2 OK, 2–3 Good, 3–4 Excellent, 5+ Exceptional.
        // Sigmoid so factor is near 0 below $100 and ramps to 1 above $100 (best flips = high spread + high volume + candle context).
        var spread = askPrice - bidPrice;
        const double capitalThreshold = 100.0;
        const double capitalSteepness = 6.0; // steeper so spread/ask in 70s → factor ~0.001 → rating < 1 (Meh)
        var spreadGate = 1.0 / (1.0 + Math.Exp(-(spread - capitalThreshold) / capitalSteepness));
        var askGate = 1.0 / (1.0 + Math.Exp(-(askPrice - capitalThreshold) / capitalSteepness));
        var capitalFactor = Math.Min(1.0, 4.0 * spreadGate * askGate); // 4 * 0.5 * 0.5 = 1 at spread=ask=100

        var denominator = adjustedVolatility + (bidPrice * 0.001);
        var baseScore = (netProfit * volumeScore * spreadStability * sweetSpotFactor * capitalFactor) / denominator;

        // Reward high ROI so advanced doesn't regress vs simple for obvious flips (e.g. BID $0.2, ASK $1.8, 700k vol)
        var roiBoost = 1.0 + Math.Log10(1.0 + Math.Min(roi, 100.0)) * 0.5;
        var finalScore = baseScore * trendFactor * roiBoost;

        var normalizedScore = Math.Log10(1 + finalScore) * 3.5;
        return Math.Clamp(normalizedScore, 0, 10);
    }

    /// <summary>For very high ROI, allow a higher sweet-spot floor so we don't cap at 0.15 for cheap, high-spread items.</summary>
    private static double GetHighRoiSweetSpotFloor(double roi)
    {
        if (roi >= 5.0) return 0.5;
        if (roi >= 3.0) return 0.35;
        if (roi >= 2.0) return 0.25;
        return 0.15;
    }

    private double CalculateVolumeScore(long bidMovingWeek, long askMovingWeek)
    {
        var totalVolume = bidMovingWeek + askMovingWeek;
        if (totalVolume <= 0) return 0;

        var executionFactor = totalVolume >= MinWeeklyVolumeForFlipping
            ? 1.0
            : Math.Pow((double)totalVolume / MinWeeklyVolumeForFlipping, 2);

        var hourlyVolume = totalVolume / 168.0;
        // Softer cap so 700k weekly (~4.2k/hr) gets a reasonable score; was 20k (required 3.36M/week for 1.0)
        var throughputScore = Math.Min(1.0, hourlyVolume / 5_000.0);

        var ratio = (double)bidMovingWeek / Math.Max(1, askMovingWeek);
        var balanceFactor = Math.Min(ratio, 1.0 / ratio);

        return throughputScore * executionFactor * (0.6 + 0.4 * balanceFactor);
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

        // FEASIBILITY CHECK: Massive ROI + High Volume usually means a "Spread Trap"
        // If an item has 10M volume but a 100x spread, it's impossible to maintain that spread.
        // This penalty triggers when ROI is high AND Volume is high.
        double feasibilityPenalty = 1.0;
        if (roi > 10.0 && volumeScore > 0.5)
        {
            // The more volume and ROI combined, the more we suspect it's a fake spread
            feasibilityPenalty = 1.0 / (1.0 + Math.Log10(roi) * volumeScore);
        }

        var baseValue = (roi * 10.0) * volumeScore * sweetSpotFactor * dustPenalty * feasibilityPenalty;

        return Math.Clamp(Math.Log10(1 + baseValue) * 4.0, 0, 10);
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
