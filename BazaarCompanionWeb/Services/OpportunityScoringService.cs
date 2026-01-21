using BazaarCompanionWeb.Dtos;
using BazaarCompanionWeb.Entities;
using BazaarCompanionWeb.Interfaces;
using BazaarCompanionWeb.Interfaces.Database;

namespace BazaarCompanionWeb.Services;

public class OpportunityScoringService(
    IOhlcRepository ohlcRepository,
    ILogger<OpportunityScoringService> logger) : IOpportunityScoringService
{
    private const int MinCandlesForAnalysis = 6;
    private const int VolatilityLookbackHours = 48;
    private const double RiskBufferPercentage = 0.005; // 0.5% of mean price
    private const double MinSpreadStability = 0.1; // Minimum acceptable spread stability
    private const int ManipulationLookbackDays = 7;
    private const int ManipulationMinCandles = 24; // 24 hours minimum
    private const double ManipulationZScoreThreshold = 2.5;

    public async Task<double> CalculateOpportunityScoreAsync(
        string productKey,
        double buyPrice,
        double sellPrice,
        long buyMovingWeek,
        long sellMovingWeek,
        CancellationToken ct = default)
    {
        // Edge case: zero volume or invalid prices
        if (buyMovingWeek == 0 || sellMovingWeek == 0 || buyPrice <= sellPrice || sellPrice <= 0)
        {
            return 0;
        }

        try
        {
            // Try to get OHLC data for advanced metrics
            var candles = await ohlcRepository.GetCandlesAsync(
                productKey,
                CandleInterval.OneHour,
                limit: VolatilityLookbackHours,
                ct);

            // If we have sufficient data, use advanced scoring
            if (candles.Count >= MinCandlesForAnalysis)
            {
                return CalculateAdvancedScore(
                    buyPrice,
                    sellPrice,
                    buyMovingWeek,
                    sellMovingWeek,
                    candles);
            }

            // Fallback to simplified scoring for new products
            logger.LogDebug(
                "Insufficient OHLC data for {ProductKey} ({Count} candles), using simplified scoring",
                productKey,
                candles.Count);
            return CalculateSimplifiedScore(buyPrice, sellPrice, buyMovingWeek, sellMovingWeek);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Error calculating opportunity score for {ProductKey}, using simplified scoring", productKey);
            return CalculateSimplifiedScore(buyPrice, sellPrice, buyMovingWeek, sellMovingWeek);
        }
    }

    private double CalculateAdvancedScore(
        double buyPrice,
        double sellPrice,
        long buyMovingWeek,
        long sellMovingWeek,
        List<OhlcDataPoint> candles)
    {
        // Calculate individual metrics
        var volatility = CalculateVolatility(candles);
        var spreadStability = CalculateSpreadStability(candles);
        var volumeScore = CalculateVolumeScore(buyMovingWeek, sellMovingWeek);
        var trendFactor = CalculateTrendFactor(candles);
        var expectedReturn = CalculateExpectedReturn(buyPrice, sellPrice, spreadStability);

        // Risk buffer: minimum volatility to prevent division by zero
        var meanPrice = candles.Average(c => c.Close);
        var riskBuffer = meanPrice * RiskBufferPercentage;
        var adjustedVolatility = Math.Max(volatility, riskBuffer);

        // Main scoring formula: (ExpectedReturn * VolumeScore * SpreadStabilityFactor) / (Volatility + RiskBuffer) * TrendMultiplier
        var numerator = expectedReturn * volumeScore * spreadStability;
        var denominator = adjustedVolatility + riskBuffer;

        if (denominator <= 0)
        {
            return 0;
        }

        var baseScore = numerator / denominator;
        var finalScore = baseScore * trendFactor;

        // Normalize to a reasonable range (0-10+ similar to current UI expectations)
        // Apply logarithmic scaling to prevent extreme values
        var normalizedScore = Math.Log(1 + finalScore) * 2;

        return Math.Max(0, normalizedScore);
    }

    private double CalculateVolatility(List<OhlcDataPoint> candles)
    {
        if (candles.Count < 2)
        {
            return 0;
        }

        // Calculate returns: (Close[i] - Close[i-1]) / Close[i-1]
        var returns = new List<double>();
        for (int i = 1; i < candles.Count; i++)
        {
            var prevClose = candles[i - 1].Close;
            if (prevClose > 0)
            {
                var returnValue = (candles[i].Close - prevClose) / prevClose;
                returns.Add(returnValue);
            }
        }

        if (returns.Count == 0)
        {
            return 0;
        }

        // Calculate standard deviation of returns
        var meanReturn = returns.Average();
        var variance = returns.Sum(r => Math.Pow(r - meanReturn, 2)) / returns.Count;
        var stdDev = Math.Sqrt(variance);

        // Return as absolute volatility (standard deviation of returns)
        // Multiply by mean price to get price volatility in absolute terms
        var meanPrice = candles.Average(c => c.Close);
        return stdDev * meanPrice;
    }

    private double CalculateSpreadStability(List<OhlcDataPoint> candles)
    {
        // For spread stability, we need buy and sell prices over time
        // Since candles only have buy prices (Close), we'll use the high-low range as a proxy
        // In a real implementation, we might want to track spreads separately

        if (candles.Count < 2)
        {
            return MinSpreadStability;
        }

        // Calculate spread proxy: (High - Low) / Close as a measure of price stability
        var spreadRatios = candles
            .Where(c => c.Close > 0)
            .Select(c => (c.High - c.Low) / c.Close)
            .ToList();

        if (spreadRatios.Count == 0)
        {
            return MinSpreadStability;
        }

        var meanSpread = spreadRatios.Average();
        var variance = spreadRatios.Sum(s => Math.Pow(s - meanSpread, 2)) / spreadRatios.Count;
        var stdDev = Math.Sqrt(variance);

        // Coefficient of variation: lower is better (more stable)
        var coefficientOfVariation = meanSpread > 0 ? stdDev / meanSpread : 1.0;

        // Convert to stability factor: lower CV = higher stability = higher multiplier
        // Use inverse relationship with bounds
        var stabilityFactor = 1.0 / (1.0 + coefficientOfVariation);
        return Math.Max(MinSpreadStability, Math.Min(1.0, stabilityFactor));
    }

    private double CalculateVolumeScore(long buyMovingWeek, long sellMovingWeek)
    {
        var totalVolume = buyMovingWeek + sellMovingWeek;

        if (totalVolume <= 0)
        {
            return 0;
        }

        // Use logarithmic scaling to normalize volume
        // This prevents extremely high volumes from dominating
        // log(1 + volume) / log(1 + max_reasonable_volume)
        // For a game economy, reasonable max might be 1 billion per week
        const double maxReasonableVolume = 1_000_000_000;
        var logVolume = Math.Log(1 + totalVolume);
        var logMax = Math.Log(1 + maxReasonableVolume);

        var normalizedVolume = logVolume / logMax;

        // Also consider volume balance: balanced markets are better
        var volumeRatio = buyMovingWeek / (double)sellMovingWeek;
        var balanceFactor = Math.Min(volumeRatio, 1.0 / volumeRatio); // Closer to 1.0 is better

        // Combine normalized volume with balance factor
        return normalizedVolume * (0.7 + 0.3 * balanceFactor);
    }

    private double CalculateTrendFactor(List<OhlcDataPoint> candles)
    {
        if (candles.Count < 3)
        {
            return 1.0; // Neutral if insufficient data
        }

        // Calculate simple moving average of recent closes
        var recentCandles = candles.TakeLast(Math.Min(6, candles.Count)).ToList();
        var sma = recentCandles.Average(c => c.Close);
        var currentPrice = candles.Last().Close;

        // If price is above SMA, trending up (opportunity may close faster)
        // If price is below SMA, trending down (opportunity may persist)
        var priceRatio = currentPrice / sma;

        // Small multiplier range (0.9-1.1) to avoid over-weighting
        // Trending up slightly reduces score (opportunity closing)
        // Trending down slightly increases score (opportunity persisting)
        var trendMultiplier = 1.0 + (1.0 - priceRatio) * 0.1;
        return Math.Max(0.9, Math.Min(1.1, trendMultiplier));
    }

    private double CalculateExpectedReturn(double buyPrice, double sellPrice, double spreadStability)
    {
        var margin = buyPrice - sellPrice;
        if (margin <= 0)
        {
            return 0;
        }

        // Expected return adjusted by spread stability
        // More stable spreads suggest more reliable returns
        return margin * spreadStability;
    }

    private double CalculateSimplifiedScore(double buyPrice, double sellPrice, long buyMovingWeek, long sellMovingWeek)
    {
        // Simplified version for products without sufficient OHLC history
        // Uses similar logic to original but with better constants

        if (buyMovingWeek == 0 || sellMovingWeek == 0)
        {
            return 0;
        }

        var margin = buyPrice - sellPrice;
        if (margin <= 0)
        {
            return 0;
        }

        var totalVolume = buyMovingWeek + sellMovingWeek;
        var volumeRatio = buyMovingWeek / (double)sellMovingWeek;
        var balanceFactor = Math.Min(volumeRatio, 1.0 / volumeRatio);

        // Hourly volume estimate
        var hourlyVolume = totalVolume / (7.0 * 24.0);

        // Profit per item
        var profitPerItem = margin;

        // Simplified risk adjustment: higher price = higher risk
        var priceRisk = 1.0 / (1.0 + sellPrice * 0.0001);

        // Calculate base score
        var baseScore = hourlyVolume * profitPerItem * balanceFactor * priceRisk;

        // Normalize using logarithmic scaling
        // Target: scores in 0-10 range for typical opportunities
        var normalizedScore = Math.Log(1 + baseScore / 1_000_000.0) * 2;

        return Math.Max(0, normalizedScore);
    }

    public async Task<ManipulationScore> CalculateManipulationScoreAsync(
        string productKey,
        double currentBuyPrice,
        double currentSellPrice,
        CancellationToken ct = default)
    {
        // Edge cases: invalid prices
        if (currentBuyPrice <= 0 || currentSellPrice <= 0)
        {
            return new ManipulationScore(false, 0, 0, 0);
        }

        try
        {
            // Fetch historical OHLC candles (1-hour interval, last 7-14 days)
            var lookbackHours = ManipulationLookbackDays * 24;
            var candles = await ohlcRepository.GetCandlesAsync(
                productKey,
                CandleInterval.OneHour,
                limit: lookbackHours,
                ct);

            // Need at least minimum candles for reliable statistics
            if (candles.Count < ManipulationMinCandles)
            {
                logger.LogDebug(
                    "Insufficient OHLC data for manipulation detection for {ProductKey} ({Count} candles, need {Min})",
                    productKey,
                    candles.Count,
                    ManipulationMinCandles);
                return new ManipulationScore(false, 0, 0, 0);
            }

            // Calculate mean and standard deviation of closing prices
            var closingPrices = candles.Select(c => c.Close).Where(p => p > 0).ToList();
            if (closingPrices.Count < ManipulationMinCandles)
            {
                return new ManipulationScore(false, 0, 0, 0);
            }

            var mean = closingPrices.Average();
            var variance = closingPrices.Sum(p => Math.Pow(p - mean, 2)) / closingPrices.Count;
            var stdDev = Math.Sqrt(variance);

            // Edge case: zero or very small standard deviation
            // Use percentage deviation as fallback
            if (stdDev < mean * 0.001) // Less than 0.1% of mean
            {
                var buyDeviationPercent = ((currentBuyPrice - mean) / mean) * 100;
                var sellDeviationPercent = ((currentSellPrice - mean) / mean) * 100;
                var maxDeviationPercent = Math.Max(Math.Abs(buyDeviationPercent), Math.Abs(sellDeviationPercent));

                // Use percentage threshold: >15% deviation indicates manipulation
                var manipulated = maxDeviationPercent > 15.0;
                var manipulationIntensity = manipulated ? Math.Min(1.0, maxDeviationPercent / 50.0) : 0.0;

                return new ManipulationScore(
                    manipulated,
                    0, // No Z-score available
                    maxDeviationPercent,
                    manipulationIntensity);
            }

            // Calculate Z-score for current buy and sell prices
            var buyZScore = (currentBuyPrice - mean) / stdDev;
            var sellZScore = (currentSellPrice - mean) / stdDev;

            // Use the more extreme Z-score (further from zero)
            var extremeZScore = Math.Abs(buyZScore) > Math.Abs(sellZScore) ? buyZScore : sellZScore;
            var currentPrice = Math.Abs(buyZScore) > Math.Abs(sellZScore) ? currentBuyPrice : currentSellPrice;

            // Determine if manipulated: Z-score exceeds threshold
            var isManipulated = Math.Abs(extremeZScore) > ManipulationZScoreThreshold;

            // Calculate intensity: normalized to 0-1 scale
            // Z-score of 2.5 = intensity 0.71, Z-score of 3.5 = intensity 1.0
            var intensity = isManipulated
                ? Math.Min(1.0, Math.Abs(extremeZScore) / 3.5)
                : 0.0;

            // Calculate deviation percentage
            var deviationPercent = ((currentPrice - mean) / mean) * 100;

            return new ManipulationScore(
                isManipulated,
                extremeZScore,
                deviationPercent,
                intensity);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Error calculating manipulation score for {ProductKey}", productKey);
            return new ManipulationScore(false, 0, 0, 0);
        }
    }
}
