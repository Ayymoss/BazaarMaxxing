using BazaarCompanionWeb.Dtos;
using BazaarCompanionWeb.Interfaces;

namespace BazaarCompanionWeb.Services;

public sealed partial class OpportunityScoringService(ILogger<OpportunityScoringService> logger) : IOpportunityScoringService
{
    private const int MinCandlesForAnalysis = 6;

    // Hypixel Specific Constants
    private const double BazaarTaxRate = 0.01125; // 1.125% (Standard for God Potion/Cookie users)

    // Hard rejection thresholds
    private const double MinNetProfitAfterTax = 100.0; // Spread must yield at least 100 coins after tax
    private const double MinBidPrice = 100.0; // Items below this are impractical to flip at scale
    private const long MinAskWeeklyVolume = 25_000; // Must be able to sell
    private const double MinAskRatio = 0.30; // At least 30% of volume must be on the ask side

    // Bag risk threshold for manipulation flag
    private const double BagRiskManipulationThreshold = 0.5;

    // Trade recommendation constants
    private const double TargetFillHours = 2.0; // Target fill window
    private const double ThroughputFraction = 0.10; // Consume ≤10% of hourly throughput

    // Z-score normalization constants
    private const double ZScoreBase = 2.0;
    private const double ZScoreScale = 1.5;
    private const int MinSamplesForZScore = 10;

    // Bid overpricing penalty thresholds
    private const double BidOverpricingSafeRatio = 0.95; // Below 95% of avg ask = safe
    private const double BidOverpricingMaxRatio = 1.10; // Above 110% of avg ask = max penalty
    private const double BidOverpricingMaxPenalty = 0.60; // Maximum 60% score reduction

    public IReadOnlyList<ScoringResult> CalculateScoresBatch(
        IReadOnlyList<ScoringProductInput> products,
        IReadOnlyDictionary<string, List<OhlcDataPoint>> candlesByProduct)
    {
        var rawScores = new double[products.Count];
        var bagRisks = new double[products.Count];
        var deviationPercents = new double[products.Count];
        var spreadPersistences = new double[products.Count];
        var executionConfidences = new double[products.Count];
        var recommendations = new TradeRecommendation?[products.Count];

        // Phase 1: Calculate raw scores and components for each product
        for (var i = 0; i < products.Count; i++)
        {
            var p = products[i];
            var candles = candlesByProduct.TryGetValue(p.ProductKey, out var c) ? c : [];

            if (p.BidPrice >= p.AskPrice || p.AskPrice <= 0)
            {
                rawScores[i] = 0;
                continue;
            }

            if (FailsHardGates(p))
            {
                rawScores[i] = 0;
                continue;
            }

            if (candles.Count < MinCandlesForAnalysis)
            {
                rawScores[i] = CalculateSimplifiedScore(p);
                LogInsufficientCandles(candles.Count, MinCandlesForAnalysis, p.ProductKey);
                continue;
            }

            // Pre-compute candle-derived aggregates once, consumed by multiple scoring methods
            var stats = CandleStats.Compute(candles);

            // Calculate each component
            var spreadPersistence = CalculateSpreadPersistence(p.AskPrice - p.BidPrice, candles, stats);
            var executionConfidence = CalculateExecutionConfidence(p);
            var bagRisk = CalculateBagRisk(p.BidPrice, stats);
            var profitMagnitude = CalculateProfitMagnitude(p.BidPrice, p.AskPrice);

            spreadPersistences[i] = spreadPersistence;
            executionConfidences[i] = executionConfidence;
            bagRisks[i] = bagRisk;

            // Price deviation for display
            deviationPercents[i] = stats.MeanClose > 0
                ? ((p.BidPrice - stats.MeanClose) / stats.MeanClose) * 100
                : 0;

            // Composite raw score — weighted geometric mean so one weak component doesn't zero everything
            // Weights: profitMagnitude 35%, executionConfidence 30%, spreadPersistence 20%, safety 15%
            var safety = 1.0 - bagRisk;
            var compositeScore = Math.Pow(Math.Max(0.001, profitMagnitude), 0.35)
                               * Math.Pow(Math.Max(0.001, executionConfidence), 0.30)
                               * Math.Pow(Math.Max(0.001, spreadPersistence), 0.20)
                               * Math.Pow(Math.Max(0.001, safety), 0.15);

            // Bid overpricing penalty — deprioritize items where bid is above weekly avg ask
            var overpricingPenalty = CalculateBidOverpricingPenalty(p.BidPrice, stats.MeanAskClose);
            compositeScore *= 1.0 - (overpricingPenalty * BidOverpricingMaxPenalty);

            rawScores[i] = compositeScore;

            // Trade recommendation (only for products with meaningful scores)
            if (rawScores[i] > 0)
            {
                recommendations[i] = CalculateRecommendation(p, stats, spreadPersistence, executionConfidence, bagRisk);
            }
        }

        // Phase 2: Z-score normalize across all non-zero scores
        var normalizedScores = NormalizeToZScores(rawScores);

        // Phase 3: Build results
        var results = new ScoringResult[products.Count];
        for (var i = 0; i < products.Count; i++)
        {
            var bagRisk = bagRisks[i];
            results[i] = new ScoringResult(
                OpportunityScore: normalizedScores[i],
                IsManipulated: bagRisk > BagRiskManipulationThreshold,
                ManipulationIntensity: bagRisk,
                PriceDeviationPercent: deviationPercents[i],
                Recommendation: recommendations[i]);
        }

        return results;
    }

    /// <summary>
    /// Hard rejection gates — items failing these are fundamentally untradeable.
    /// </summary>
    private static bool FailsHardGates(ScoringProductInput p)
    {
        if (p.BidPrice < MinBidPrice)
            return true;

        if (p.AskMovingWeek < MinAskWeeklyVolume)
            return true;

        // Net profit after tax must be at least 100 coins
        var netProfit = (p.AskPrice * (1.0 - BazaarTaxRate)) - p.BidPrice;
        if (netProfit < MinNetProfitAfterTax)
            return true;

        // Must have minimum proportion of volume on the ask (sell) side
        var totalVolume = p.BidMovingWeek + p.AskMovingWeek;
        if (totalVolume > 0)
        {
            var askRatio = (double)p.AskMovingWeek / totalVolume;
            if (askRatio < MinAskRatio)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Bid Overpricing Penalty (0–1): How far above the weekly average ask is the current bid?
    /// Items where bid > avg ask have a high probability of buying at a spike price.
    /// </summary>
    private static double CalculateBidOverpricingPenalty(double currentBid, double meanAskClose)
    {
        if (meanAskClose <= 0) return 0;

        var ratio = currentBid / meanAskClose;

        return ratio switch
        {
            <= BidOverpricingSafeRatio => 0,
            >= BidOverpricingMaxRatio => 1.0,
            _ => (ratio - BidOverpricingSafeRatio) / (BidOverpricingMaxRatio - BidOverpricingSafeRatio)
        };
    }

    /// <summary>
    /// Spread Persistence (0–1): Is this spread stable or a transient spike?
    /// Uses OHLC Spread field (historical hourly bid-ask spread).
    /// </summary>
    private static double CalculateSpreadPersistence(double currentSpread, List<OhlcDataPoint> candles, in CandleStats stats)
    {
        if (stats.SortedSpreads.Count == 0) return 0.5;

        // 1. Percentile score (35%): current spread vs 7-day distribution
        // Only penalize if spread is BELOW the 25th percentile (collapsed/unusable).
        var belowCount = stats.SortedSpreads.Count(s => s <= currentSpread);
        var percentileRank = (double)belowCount / stats.SortedSpreads.Count;
        var percentileScore = percentileRank < 0.25
            ? percentileRank / 0.25 // Linear ramp from 0→1 for bottom quartile
            : 1.0; // At or above 25th percentile = full score

        // 2. Profitable candle fraction (35%): what % of candles had profitable spread after tax?
        var softProfitThreshold = MinNetProfitAfterTax * 0.25;
        var profitableCount = candles.Count(c =>
        {
            var candleNetProfit = (c.AskClose * (1.0 - BazaarTaxRate)) - c.Close;
            return candleNetProfit >= softProfitThreshold;
        });
        var profitableFraction = (double)profitableCount / candles.Count;

        // 3. Spread trend (30%): slope of last 24 candle spreads — positive trend is good
        var trendFactor = 1.0;
        if (stats.RecentSpreads.Count >= 3)
        {
            var slope = LinearRegressionSlope(stats.RecentSpreads);
            var meanSpread = stats.RecentSpreads.Average();
            if (meanSpread > 0)
            {
                var normalizedSlope = (slope / meanSpread) * stats.RecentSpreads.Count;
                trendFactor = Math.Clamp(1.0 + normalizedSlope * 0.3, 0.7, 1.3);
            }
        }

        return (percentileScore * 0.35) + (profitableFraction * 0.35) + ((trendFactor - 0.7) / 0.6 * 0.30);
    }

    /// <summary>
    /// Execution Confidence (0–1): Can orders actually fill in a reasonable time?
    /// </summary>
    private static double CalculateExecutionConfidence(ScoringProductInput p)
    {
        var bidHourlyThroughput = p.BidMovingWeek / 168.0;
        var askHourlyThroughput = p.AskMovingWeek / 168.0;
        var minThroughput = Math.Min(bidHourlyThroughput, askHourlyThroughput);

        // 1. Throughput score (50%): sigmoid centered at 500/hr
        var throughputScore = 1.0 / (1.0 + Math.Exp(-Math.Log10(Math.Max(1, minThroughput) / 500.0) * 3.0));

        // 2. Balance score (35%): askThroughput / bidThroughput, ≥1.0 is ideal
        double balanceScore;
        if (bidHourlyThroughput <= 0)
        {
            balanceScore = 0;
        }
        else
        {
            var balanceRatio = askHourlyThroughput / bidHourlyThroughput;
            balanceScore = balanceRatio >= 1.0
                ? 1.0
                : Math.Sqrt(Math.Clamp((balanceRatio - 0.3) / 0.7, 0, 1));
        }

        // 3. Competition score (15%): fewer orders = faster fills (steeper curve for meaningful differentiation)
        var totalOrders = p.BidOrders + p.AskOrders;
        var competitionScore = 1.0 / (1.0 + Math.Log10(1 + totalOrders) * 0.3);

        return (throughputScore * 0.5) + (balanceScore * 0.35) + (competitionScore * 0.15);
    }

    /// <summary>
    /// Bag Risk (0–1): Probability of being stuck with inventory.
    /// Replaces separate ManipulationScore.
    /// </summary>
    private static double CalculateBagRisk(double currentBid, in CandleStats stats)
    {
        if (stats.Closes.Count < 2) return 0.5;

        var stdDev = stats.CloseStdDev < stats.MeanClose * 0.001 ? stats.MeanClose * 0.001 : stats.CloseStdDev;

        // 1. Price deviation risk (50%): asymmetric z-score — upward deviation is 1.5× more dangerous
        var rawZ = (currentBid - stats.MeanClose) / stdDev;
        var effectiveZ = rawZ > 0 ? rawZ * 1.5 : Math.Abs(rawZ);
        var deviationRisk = Math.Min(1.0, effectiveZ / 4.0);

        // 2. Spread narrowing risk (35%): are spreads getting smaller?
        double narrowingRisk = 0;
        if (stats.RecentSpreads.Count >= 3)
        {
            var slope = LinearRegressionSlope(stats.RecentSpreads);
            var meanSpread = stats.RecentSpreads.Average();
            if (slope < 0 && meanSpread > 0)
            {
                // Negative slope = narrowing. Normalize by mean spread and time window.
                narrowingRisk = Math.Min(1.0, Math.Abs(slope) / meanSpread * stats.RecentSpreads.Count);
            }
        }

        // 3. Volatility risk (15%): high recent volatility = unpredictable fills
        double volatilityRisk = 0;
        if (stats.Closes.Count >= 3)
        {
            List<double> returns = [];
            for (var i = 1; i < stats.Closes.Count; i++)
            {
                if (stats.Closes[i - 1] > 0)
                    returns.Add((stats.Closes[i] - stats.Closes[i - 1]) / stats.Closes[i - 1]);
            }

            if (returns.Count > 0)
            {
                var volStdDev = StdDev(returns);
                volatilityRisk = Math.Min(1.0, volStdDev * 10.0);
            }
        }

        // Deviation is the strongest signal for actual manipulation
        return (deviationRisk * 0.50) + (narrowingRisk * 0.35) + (volatilityRisk * 0.15);
    }

    /// <summary>
    /// Profit Magnitude: log-scaled net profit after tax.
    /// </summary>
    private static double CalculateProfitMagnitude(double bidPrice, double askPrice)
    {
        var netProfit = (askPrice * (1.0 - BazaarTaxRate)) - bidPrice;
        return netProfit <= 0 ? 0 : Math.Log10(1.0 + netProfit);
    }

    /// <summary>
    /// Simplified score for products with insufficient candle history.
    /// </summary>
    private static double CalculateSimplifiedScore(ScoringProductInput p)
    {
        var netProfit = (p.AskPrice * (1.0 - BazaarTaxRate)) - p.BidPrice;
        if (netProfit <= 0) return 0;

        var totalVolume = p.BidMovingWeek + p.AskMovingWeek;
        if (totalVolume <= 0) return 0;

        var hourlyThroughput = totalVolume / 168.0;
        var throughputFactor = Math.Min(1.0, hourlyThroughput / 5000.0);

        return Math.Log10(1.0 + netProfit) * throughputFactor * 0.5; // Penalized for lack of history
    }

    /// <summary>
    /// Calculate actionable trade recommendation for a product.
    /// </summary>
    private static TradeRecommendation? CalculateRecommendation(
        ScoringProductInput p,
        in CandleStats stats,
        double spreadPersistence,
        double executionConfidence,
        double bagRisk)
    {
        var bidHourlyThroughput = p.BidMovingWeek / 168.0;
        var askHourlyThroughput = p.AskMovingWeek / 168.0;

        if (bidHourlyThroughput <= 0 || askHourlyThroughput <= 0) return null;

        // Suggested Bid Price: penny ahead of best bid, capped at 75th percentile of last 24h closes
        var p75Bid = stats.RecentCloses.Count > 0
            ? stats.RecentCloses[(int)(stats.RecentCloses.Count * 0.75)]
            : p.BidPrice;
        var suggestedBidPrice = Math.Min(p.BidPrice + 0.1, p75Bid);

        // Suggested Ask Price: penny below best ask, floored at 25th percentile of last 24h ask closes
        var p25Ask = stats.RecentAskCloses.Count > 0
            ? stats.RecentAskCloses[(int)(stats.RecentAskCloses.Count * 0.25)]
            : p.AskPrice;
        var suggestedAskPrice = Math.Max(p.AskPrice - 0.1, p25Ask);

        // Net profit per unit after tax
        var profitPerUnit = (suggestedAskPrice * (1.0 - BazaarTaxRate)) - suggestedBidPrice;
        if (profitPerUnit <= 0) return null;

        // Suggested volume: target 2hr fill, consume ≤10% of hourly throughput
        var maxBidSide = bidHourlyThroughput * TargetFillHours * ThroughputFraction;
        var maxAskSide = askHourlyThroughput * TargetFillHours * ThroughputFraction;
        var suggestedVolume = (int)Math.Max(1, Math.Min(maxBidSide, maxAskSide));

        // Estimated fill time (round-trip: buy + sell)
        var bidFillHours = suggestedVolume / Math.Max(1, bidHourlyThroughput);
        var askFillHours = suggestedVolume / Math.Max(1, askHourlyThroughput);
        var totalFillHours = bidFillHours + askFillHours;

        // Confidence: composite of the three quality signals
        var confidence = Math.Clamp(spreadPersistence * executionConfidence * (1.0 - bagRisk), 0, 1);

        return new TradeRecommendation(
            SuggestedBidVolume: suggestedVolume,
            SuggestedBidPrice: Math.Round(suggestedBidPrice, 1),
            SuggestedAskPrice: Math.Round(suggestedAskPrice, 1),
            EstimatedFillTimeHours: Math.Round(totalFillHours, 2),
            EstimatedProfitPerUnit: Math.Round(profitPerUnit, 2),
            EstimatedTotalProfit: Math.Round(suggestedVolume * profitPerUnit, 2),
            Confidence: Math.Round(confidence, 3));
    }

    /// <summary>
    /// Z-score normalization: maps raw scores to 0–10 scale.
    /// Mean→2.0, each σ adds 1.5 points.
    /// </summary>
    private static double[] NormalizeToZScores(double[] rawScores)
    {
        var result = new double[rawScores.Length];
        var nonZero = rawScores.Where(s => s > 0).ToList();

        if (nonZero.Count < MinSamplesForZScore)
        {
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
        if (stdDev < mean * 0.001) stdDev = mean * 0.001;

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
    /// Standard deviation of a list of values.
    /// </summary>
    private static double StdDev(IList<double> values)
    {
        if (values.Count < 2) return 0;
        var mean = values.Average();
        return Math.Sqrt(values.Sum(v => Math.Pow(v - mean, 2)) / values.Count);
    }

    /// <summary>
    /// Simple linear regression slope (least-squares).
    /// X values are implicitly 0, 1, 2, ... N-1.
    /// </summary>
    private static double LinearRegressionSlope(IList<double> values)
    {
        if (values.Count < 2) return 0;

        var n = values.Count;
        var sumX = n * (n - 1) / 2.0;
        var sumX2 = n * (n - 1) * (2 * n - 1) / 6.0;
        var sumY = values.Sum();
        var sumXy = 0.0;

        for (var i = 0; i < n; i++)
            sumXy += i * values[i];

        var denominator = n * sumX2 - sumX * sumX;
        if (Math.Abs(denominator) < 1e-10) return 0;

        return (n * sumXy - sumX * sumY) / denominator;
    }

    /// <summary>
    /// Pre-computed candle-derived aggregates to avoid redundant iterations.
    /// </summary>
    private readonly struct CandleStats
    {
        public required List<double> Closes { get; init; }
        public required double MeanClose { get; init; }
        public required double CloseStdDev { get; init; }
        public required double MeanAskClose { get; init; }
        public required List<double> SortedSpreads { get; init; }
        public required List<double> RecentSpreads { get; init; }
        public required List<double> RecentCloses { get; init; }
        public required List<double> RecentAskCloses { get; init; }

        public static CandleStats Compute(List<OhlcDataPoint> candles)
        {
            var closes = candles.Select(c => c.Close).ToList();
            var meanClose = closes.Average();
            var closeStdDev = StdDev(closes);

            var askCloses = candles.Select(c => c.AskClose).ToList();
            var meanAskClose = askCloses.Count > 0 ? askCloses.Average() : 0;

            var sortedSpreads = candles.Select(c => c.Spread).Where(s => s > 0).OrderBy(s => s).ToList();

            var recentCount = Math.Min(24, candles.Count);
            var recentCandles = candles.TakeLast(recentCount);

            List<double> recentSpreads = [..recentCandles.Select(c => c.Spread)];
            List<double> recentCloses = [..recentCandles.Select(c => c.Close).OrderBy(x => x)];
            List<double> recentAskCloses = [..recentCandles.Select(c => c.AskClose).OrderBy(x => x)];

            return new CandleStats
            {
                Closes = closes,
                MeanClose = meanClose,
                CloseStdDev = closeStdDev,
                MeanAskClose = meanAskClose,
                SortedSpreads = sortedSpreads,
                RecentSpreads = recentSpreads,
                RecentCloses = recentCloses,
                RecentAskCloses = recentAskCloses
            };
        }
    }

    [LoggerMessage(Level = LogLevel.Debug,
        Message = "Insufficient candles ({Candles}/{Target}) for {ProductKey}, using simplified scoring")]
    private partial void LogInsufficientCandles(int candles, int target, string productKey);
}
