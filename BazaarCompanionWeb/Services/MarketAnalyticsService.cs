using BazaarCompanionWeb.Context;
using BazaarCompanionWeb.Dtos;
using BazaarCompanionWeb.Entities;
using BazaarCompanionWeb.Interfaces.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BazaarCompanionWeb.Services;

public class MarketAnalyticsService(
    IDbContextFactory<DataContext> contextFactory,
    IServiceScopeFactory scopeFactory,
    ILogger<MarketAnalyticsService> logger)
{
    private MarketMetrics? _cachedMetrics;
    private DateTime _metricsCacheTime = DateTime.MinValue;
    private readonly TimeSpan _metricsCacheDuration = TimeSpan.FromMinutes(5);

    private CorrelationMatrix? _cachedCorrelationMatrix;
    private DateTime _correlationCacheTime = DateTime.MinValue;
    private readonly TimeSpan _correlationCacheDuration = TimeSpan.FromMinutes(15);

    public async Task<MarketMetrics> GetMarketMetricsAsync(CancellationToken ct = default)
    {
        if (_cachedMetrics != null && DateTime.UtcNow - _metricsCacheTime < _metricsCacheDuration)
        {
            return _cachedMetrics;
        }

        await using var context = await contextFactory.CreateDbContextAsync(ct);

        var products = await context.Products
            .Include(p => p.Bid)
            .Include(p => p.Ask)
            .Include(p => p.Meta)
            .AsNoTracking()
            .ToListAsync(ct);

        var activeProducts = products.Where(p => p.Bid.OrderVolumeWeek > 0 || p.Ask.OrderVolumeWeek > 0).ToList();

        // Total Market Capitalization: Sum of (current buy price × total available volume)
        var totalMarketCap = activeProducts
            .Where(p => p.Bid.UnitPrice > 0 && p.Bid.OrderVolume > 0)
            .Sum(p => p.Bid.UnitPrice * p.Bid.OrderVolume);

        // For spread/health calculations, only consider liquid products (>10K volume on both sides)
        // to avoid dead/illiquid items skewing the metrics
        var liquidProducts = activeProducts
            .Where(p => p.Bid.UnitPrice > 0 && p.Ask.UnitPrice > 0
                && p.Bid.OrderVolumeWeek > 10_000 && p.Ask.OrderVolumeWeek > 10_000)
            .ToList();

        var spreads = liquidProducts
            .Select(p => (p.Ask.UnitPrice - p.Bid.UnitPrice) / p.Bid.UnitPrice) // Decimal ratio (e.g., 0.05 for 5%)
            .OrderBy(s => s)
            .ToList();

        // Use median spread instead of mean (robust to outliers)
        var averageSpread = spreads.Count > 0
            ? spreads[spreads.Count / 2]
            : 0;

        // Market Manipulation Index: Percentage of products currently flagged as manipulated
        var manipulatedCount = activeProducts.Count(p => p.Meta.IsManipulated);
        var manipulationIndex = activeProducts.Any() ? (double)manipulatedCount / activeProducts.Count * 100 : 0;

        // Market Health Score: Composite score (0-100)
        // Use IQR-based spread stability (robust to outliers)
        double spreadStability;
        if (spreads.Count >= 4)
        {
            var q1 = spreads[spreads.Count / 4];
            var q3 = spreads[spreads.Count * 3 / 4];
            var iqr = q3 - q1;
            spreadStability = 100.0 * (1.0 / (1.0 + iqr));
        }
        else
        {
            spreadStability = 50;
        }

        var volumeDistribution = CalculateVolumeDistributionScore(activeProducts);
        var manipulationScore = 100 - manipulationIndex; // Lower manipulation = better

        // Liquidity score: proportion of active products with balanced bid/ask volumes (ask ratio 0.25-0.75)
        var balancedCount = activeProducts.Count(p =>
        {
            var total = p.Bid.OrderVolumeWeek + p.Ask.OrderVolumeWeek;
            if (total <= 0) return false;
            var askRatio = (double)p.Ask.OrderVolumeWeek / total;
            return askRatio >= 0.25 && askRatio <= 0.75;
        });
        var liquidityScore = activeProducts.Any() ? (double)balancedCount / activeProducts.Count * 100 : 0;

        var marketHealthScore = (spreadStability * 0.3 + volumeDistribution * 0.3 + manipulationScore * 0.2 + liquidityScore * 0.2);

        // Volume Trends
        var volumeTrends = await CalculateVolumeTrendsAsync(context, ct);

        var metrics = new MarketMetrics
        {
            TotalMarketCapitalization = totalMarketCap,
            AverageSpread = averageSpread,
            MarketManipulationIndex = manipulationIndex,
            ActiveProductsCount = activeProducts.Count,
            MarketHealthScore = Math.Max(0, Math.Min(100, marketHealthScore)),
            VolumeTrends = volumeTrends
        };

        _cachedMetrics = metrics;
        _metricsCacheTime = DateTime.UtcNow;

        return metrics;
    }

    public async Task<CorrelationMatrix> GetCorrelationMatrixAsync(CancellationToken ct = default)
    {
        if (_cachedCorrelationMatrix != null && DateTime.UtcNow - _correlationCacheTime < _correlationCacheDuration)
        {
            return _cachedCorrelationMatrix;
        }

        // Get top 100 most-traded products by volume
        await using var scope = scopeFactory.CreateAsyncScope();
        var ohlcRepository = scope.ServiceProvider.GetRequiredService<IOhlcRepository>();
        
        await using var context = await contextFactory.CreateDbContextAsync(ct);
        var topProducts = await context.Products
            .Include(p => p.Meta)
            .AsNoTracking()
            .OrderByDescending(p => p.Meta.TotalWeekVolume)
            .Take(100)
            .ToListAsync(ct);

        var productKeys = topProducts.Select(p => p.ProductKey).ToList();
        var productNames = topProducts.ToDictionary(p => p.ProductKey, p => p.FriendlyName);

        // Get hourly candles for last 7 days for all products
        var productPriceData = new Dictionary<string, List<double>>();

        foreach (var productKey in productKeys)
        {
            var candles = await ohlcRepository.GetCandlesAsync(
                productKey,
                CandleInterval.OneHour,
                limit: 7 * 24, // 7 days * 24 hours
                ct);

            var prices = candles
                .OrderBy(c => c.Time)
                .Select(c => c.Close)
                .ToList();

            if (prices.Count >= 6) // Need at least 6 hours of data (lowered from 24)
            {
                productPriceData[productKey] = prices;
            }
        }

        logger.LogDebug(
            "Correlation matrix: {TopProductCount} top products, {ValidProductCount} with enough candle data",
            topProducts.Count,
            productPriceData.Count);

        // Calculate correlation matrix
        var matrix = new Dictionary<string, Dictionary<string, double>>();
        var validProducts = productPriceData.Keys.ToList();

        foreach (var product1 in validProducts)
        {
            matrix[product1] = new Dictionary<string, double>();
            var prices1 = productPriceData[product1];

            foreach (var product2 in validProducts)
            {
                if (product1 == product2)
                {
                    matrix[product1][product2] = 1.0;
                }
                else
                {
                    var prices2 = productPriceData[product2];
                    var correlation = CalculatePearsonCorrelation(prices1, prices2);
                    matrix[product1][product2] = correlation;
                }
            }
        }

        var correlationMatrix = new CorrelationMatrix
        {
            ProductKeys = validProducts,
            ProductNames = validProducts.Select(k => productNames.GetValueOrDefault(k, k)).ToList(),
            Matrix = matrix,
            CalculatedAt = DateTime.UtcNow
        };

        _cachedCorrelationMatrix = correlationMatrix;
        _correlationCacheTime = DateTime.UtcNow;

        return correlationMatrix;
    }

    public async Task<List<RelatedProduct>> GetRelatedProductsAsync(string productKey, int count = 5, CancellationToken ct = default)
    {
        var matrix = await GetCorrelationMatrixAsync(ct);

        if (!matrix.Matrix.ContainsKey(productKey))
        {
            return new List<RelatedProduct>();
        }

        var correlations = matrix.Matrix[productKey]
            .Where(kvp => kvp.Key != productKey && !double.IsNaN(kvp.Value))
            .Select(kvp => new
            {
                ProductKey = kvp.Key,
                Correlation = kvp.Value
            })
            .OrderByDescending(x => Math.Abs(x.Correlation))
            .Take(count)
            .ToList();

        var index = matrix.ProductKeys.IndexOf(productKey);
        var productNames = matrix.ProductNames;

        return correlations.Select(c =>
        {
            var relatedIndex = matrix.ProductKeys.IndexOf(c.ProductKey);
            var correlationType = Math.Abs(c.Correlation) switch
            {
                >= 0.7 => "Strong",
                >= 0.4 => "Moderate",
                _ => "Weak"
            };

            return new RelatedProduct
            {
                ProductKey = c.ProductKey,
                ProductName = relatedIndex >= 0 && relatedIndex < productNames.Count
                    ? productNames[relatedIndex]
                    : c.ProductKey,
                Correlation = c.Correlation,
                CorrelationType = correlationType
            };
        }).ToList();
    }

    public async Task<List<ProductTrend>> GetTrendingProductsAsync(int count = 10, CancellationToken ct = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var ohlcRepository = scope.ServiceProvider.GetRequiredService<IOhlcRepository>();
        
        await using var context = await contextFactory.CreateDbContextAsync(ct);

        var products = await context.Products
            .Include(p => p.Bid)
            .Include(p => p.Meta)
            .AsNoTracking()
            .Where(p => p.Bid.OrderVolumeWeek > 0)
            .ToListAsync(ct);

        var trends = new List<ProductTrend>();

        foreach (var product in products)
        {
            var candles = await ohlcRepository.GetCandlesAsync(
                product.ProductKey,
                CandleInterval.OneHour,
                limit: 7 * 24, // 7 days
                ct);

            if (candles.Count < 24) continue; // Need at least 24 hours of data

            var orderedCandles = candles.OrderBy(c => c.Time).ToList();
            var currentPrice = orderedCandles.Last().Close;

            // Find prices at different time points
            var price6hAgo = GetPriceAtTimeAgo(orderedCandles, TimeSpan.FromHours(6));
            var price24hAgo = GetPriceAtTimeAgo(orderedCandles, TimeSpan.FromHours(24));
            var price7dAgo = orderedCandles.First().Close;

            if (price6hAgo == null || price24hAgo == null) continue;

            var shortTermMomentum = ((currentPrice - price6hAgo.Value) / price6hAgo.Value) * 100;
            var mediumTermMomentum = ((currentPrice - price24hAgo.Value) / price24hAgo.Value) * 100;
            var longTermMomentum = ((currentPrice - price7dAgo) / price7dAgo) * 100;

            var trendDirection = DetermineTrendDirection(shortTermMomentum, mediumTermMomentum, longTermMomentum);
            var momentumStrength = Math.Sqrt(
                Math.Pow(shortTermMomentum, 2) +
                Math.Pow(mediumTermMomentum, 2) +
                Math.Pow(longTermMomentum, 2)
            );

            trends.Add(new ProductTrend
            {
                ProductKey = product.ProductKey,
                ProductName = product.FriendlyName,
                ShortTermMomentum = shortTermMomentum,
                MediumTermMomentum = mediumTermMomentum,
                LongTermMomentum = longTermMomentum,
                TrendDirection = trendDirection,
                MomentumStrength = momentumStrength,
                CurrentPrice = currentPrice,
                PriceChange6h = shortTermMomentum,
                PriceChange24h = mediumTermMomentum,
                PriceChange7d = longTermMomentum
            });
        }

        return trends
            .OrderByDescending(t => t.MomentumStrength)
            .Take(count)
            .ToList();
    }

    public async Task<MarketHeatmapData> GetMarketHeatmapAsync(CancellationToken ct = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var ohlcRepository = scope.ServiceProvider.GetRequiredService<IOhlcRepository>();
        
        await using var context = await contextFactory.CreateDbContextAsync(ct);

        var products = await context.Products
            .Include(p => p.Meta)
            .AsNoTracking()
            .Where(p => p.Meta.TotalWeekVolume > 0)
            .ToListAsync(ct);

        var points = new List<HeatmapPoint>();
        var volatilities = new List<double>();
        var volumes = new List<double>();

        foreach (var product in products)
        {
            var candles = await ohlcRepository.GetCandlesAsync(
                product.ProductKey,
                CandleInterval.OneHour,
                limit: 48, // 48 hours for volatility
                ct);

            if (candles.Count < 6) continue;

            var volatility = CalculateVolatility(candles);
            var volume = product.Meta.TotalWeekVolume;

            volatilities.Add(volatility);
            volumes.Add(volume);

            points.Add(new HeatmapPoint
            {
                ProductKey = product.ProductKey,
                ProductName = product.FriendlyName,
                Volatility = volatility,
                Volume = volume,
                OpportunityScore = product.Meta.FlipOpportunityScore
            });
        }

        var maxVolatility = volatilities.Any() ? volatilities.Max() : 1;
        var maxVolume = volumes.Any() ? volumes.Max() : 1;

        // Normalize X and Y coordinates (0-1)
        foreach (var point in points)
        {
            point.X = maxVolatility > 0 ? point.Volatility / maxVolatility : 0;
            point.Y = maxVolume > 0 ? point.Volume / maxVolume : 0;
        }

        return new MarketHeatmapData
        {
            Points = points,
            MaxVolatility = maxVolatility,
            MaxVolume = maxVolume
        };
    }

    private async Task<VolumeTrends> CalculateVolumeTrendsAsync(DataContext context, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var products = await context.Products
            .Include(p => p.Bid)
            .Include(p => p.Ask)
            .AsNoTracking()
            .ToListAsync(ct);

        var totalVolume24h = products.Sum(p => (p.Bid.OrderVolumeWeek + p.Ask.OrderVolumeWeek) / 7.0);
        var totalVolume7d = products.Sum(p => p.Bid.OrderVolumeWeek + p.Ask.OrderVolumeWeek);
        var totalVolume30d = totalVolume7d * 30.0 / 7.0; // Estimate

        // For time series, we'd need historical volume data
        // For now, create simple time series from current data
        var timeSeries24h = new List<VolumeDataPoint>();
        var timeSeries7d = new List<VolumeDataPoint>();
        var timeSeries30d = new List<VolumeDataPoint>();

        // Generate time series (simplified - would need historical data for real implementation)
        for (int i = 23; i >= 0; i--)
        {
            var time = now.AddHours(-i);
            timeSeries24h.Add(new VolumeDataPoint(time, totalVolume24h / 24.0));
        }

        for (int i = 6; i >= 0; i--)
        {
            var time = now.AddDays(-i);
            timeSeries7d.Add(new VolumeDataPoint(time, totalVolume7d / 7.0));
        }

        for (int i = 29; i >= 0; i--)
        {
            var time = now.AddDays(-i);
            timeSeries30d.Add(new VolumeDataPoint(time, totalVolume30d / 30.0));
        }

        return new VolumeTrends
        {
            Volume24h = totalVolume24h,
            Volume7d = totalVolume7d,
            Volume30d = totalVolume30d,
            TimeSeries24h = timeSeries24h,
            TimeSeries7d = timeSeries7d,
            TimeSeries30d = timeSeries30d
        };
    }

    private double CalculateVolumeDistributionScore(List<EFProduct> products)
    {
        if (!products.Any()) return 0;

        var volumes = products.Select(p => p.Meta.TotalWeekVolume).Where(v => v > 0).ToList();
        if (!volumes.Any()) return 0;

        // Calculate coefficient of variation (lower is better for distribution)
        var mean = volumes.Average();
        var stdDev = Math.Sqrt(volumes.Sum(v => Math.Pow(v - mean, 2)) / volumes.Count);
        var cv = mean > 0 ? stdDev / mean : 1.0;

        // Convert to score (0-100): lower CV = higher score
        return Math.Max(0, Math.Min(100, 100 - (cv * 50)));
    }

    private double CalculatePearsonCorrelation(List<double> x, List<double> y)
    {
        // Handle different array lengths by taking the minimum common length (most recent data)
        var minCount = Math.Min(x.Count, y.Count);
        if (minCount < 2) return 0;

        // Take the most recent data points (from the end)
        var xAligned = x.TakeLast(minCount).ToList();
        var yAligned = y.TakeLast(minCount).ToList();

        var n = minCount;
        var sumX = xAligned.Sum();
        var sumY = yAligned.Sum();
        var sumXY = xAligned.Zip(yAligned, (a, b) => a * b).Sum();
        var sumX2 = xAligned.Sum(v => v * v);
        var sumY2 = yAligned.Sum(v => v * v);

        var numerator = (n * sumXY) - (sumX * sumY);
        var denominator = Math.Sqrt(((n * sumX2) - (sumX * sumX)) * ((n * sumY2) - (sumY * sumY)));

        if (denominator == 0) return 0;

        return numerator / denominator;
    }

    private double? GetPriceAtTimeAgo(List<OhlcDataPoint> candles, TimeSpan timeAgo)
    {
        var targetTime = candles.Last().Time - timeAgo;
        var candle = candles.LastOrDefault(c => c.Time <= targetTime);
        return candle?.Close;
    }

    private string DetermineTrendDirection(double shortTerm, double mediumTerm, double longTerm)
    {
        var allPositive = shortTerm > 0 && mediumTerm > 0 && longTerm > 0;
        var allNegative = shortTerm < 0 && mediumTerm < 0 && longTerm < 0;

        if (allPositive) return "Bullish";
        if (allNegative) return "Bearish";

        var volatility = Math.Abs(shortTerm) + Math.Abs(mediumTerm) + Math.Abs(longTerm);
        if (volatility > 10) return "Volatile";

        return "Neutral";
    }

    private double CalculateVolatility(List<OhlcDataPoint> candles)
    {
        if (candles.Count < 2) return 0;

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

        if (!returns.Any()) return 0;

        var meanReturn = returns.Average();
        var variance = returns.Sum(r => Math.Pow(r - meanReturn, 2)) / returns.Count;
        var stdDev = Math.Sqrt(variance);

        var meanPrice = candles.Average(c => c.Close);
        return stdDev * meanPrice;
    }
}

// Extension method for standard deviation
public static class ListExtensions
{
    public static double StandardDeviation(this IEnumerable<double> values)
    {
        var valueList = values.ToList();
        if (!valueList.Any()) return 0;

        var avg = valueList.Average();
        var sumOfSquares = valueList.Sum(v => Math.Pow(v - avg, 2));
        return Math.Sqrt(sumOfSquares / valueList.Count);
    }
}
