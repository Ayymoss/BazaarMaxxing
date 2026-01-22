using BazaarCompanionWeb.Dtos;
using BazaarCompanionWeb.Entities;
using BazaarCompanionWeb.Interfaces.Database;

namespace BazaarCompanionWeb.Services;

public class TechnicalAnalysisService(IOhlcRepository ohlcRepository, ILogger<TechnicalAnalysisService> logger)
{
    public async Task<List<TechnicalIndicator>> CalculateIndicatorsAsync(
        string productKey,
        CandleInterval interval,
        ChartIndicatorConfig config,
        CancellationToken ct = default)
    {
        var candles = await ohlcRepository.GetCandlesAsync(productKey, interval, limit: 200, ct);
        return CalculateIndicatorsFromCandles(candles, config);
    }

    /// <summary>
    /// Calculate indicators from provided candle data (including live candles)
    /// </summary>
    public List<TechnicalIndicator> CalculateIndicatorsFromCandles(
        List<OhlcDataPoint> candles,
        ChartIndicatorConfig config)
    {
        if (candles.Count < 2)
        {
            return new List<TechnicalIndicator>();
        }

        var indicators = new List<TechnicalIndicator>();
        var orderedCandles = candles.OrderBy(c => c.Time).ToList();

        // Calculate price-based indicators
        if (config.ShowSMA10)
        {
            indicators.Add(CalculateSMA(orderedCandles, 10, "SMA 10", "#3b82f6"));
        }

        if (config.ShowSMA20)
        {
            indicators.Add(CalculateSMA(orderedCandles, 20, "SMA 20", "#8b5cf6"));
        }

        if (config.ShowSMA50)
        {
            indicators.Add(CalculateSMA(orderedCandles, 50, "SMA 50", "#ec4899"));
        }

        if (config.ShowEMA12)
        {
            indicators.Add(CalculateEMA(orderedCandles, 12, "EMA 12", "#10b981"));
        }

        if (config.ShowEMA26)
        {
            indicators.Add(CalculateEMA(orderedCandles, 26, "EMA 26", "#f59e0b"));
        }

        if (config.ShowBollingerBands)
        {
            var bb = CalculateBollingerBands(orderedCandles, 20, 2.0);
            indicators.AddRange(bb);
        }

        if (config.ShowRSI)
        {
            indicators.Add(CalculateRSI(orderedCandles, 14));
        }

        if (config.ShowMACD)
        {
            var macd = CalculateMACD(orderedCandles, 12, 26, 9);
            indicators.AddRange(macd);
        }

        if (config.ShowVWAP)
        {
            // VWAP requires volume data, which we may not have
            // For now, calculate simple VWAP using typical volume estimate
            indicators.Add(CalculateVWAP(orderedCandles));
        }

        return indicators;
    }

    public async Task<List<IndicatorDataPoint>> CalculateSpreadAsync(
        string productKey,
        CandleInterval interval,
        CancellationToken ct = default)
    {
        // Get buy and sell prices from ticks
        var ticks = await ohlcRepository.GetTicksForAggregationAsync(
            productKey,
            DateTime.UtcNow.AddDays(-7),
            ct);

        if (!ticks.Any())
        {
            return new List<IndicatorDataPoint>();
        }

        // Group ticks by time period based on interval
        var intervalMinutes = (int)interval;
        var grouped = ticks
            .GroupBy(t => GetPeriodStart(t.Timestamp, intervalMinutes))
            .OrderBy(g => g.Key)
            .ToList();

        var spreadPoints = new List<IndicatorDataPoint>();

        foreach (var group in grouped)
        {
            var avgBuy = group.Average(t => t.BuyPrice);
            var avgSell = group.Average(t => t.SellPrice);
            var spread = avgBuy > 0 && avgSell > 0 
                ? ((avgBuy - avgSell) / avgSell) * 100 
                : 0;

            spreadPoints.Add(new IndicatorDataPoint(group.Key, spread));
        }

        return spreadPoints;
    }

    public List<SupportResistanceLevel> CalculateSupportResistance(
        List<OhlcDataPoint> candles,
        int lookbackPeriods = 20)
    {
        if (candles.Count < lookbackPeriods * 2)
        {
            return new List<SupportResistanceLevel>();
        }

        var levels = new List<SupportResistanceLevel>();
        var orderedCandles = candles.OrderBy(c => c.Time).ToList();

        // Find local minima (support) and maxima (resistance)
        for (int i = lookbackPeriods; i < orderedCandles.Count - lookbackPeriods; i++)
        {
            var current = orderedCandles[i];
            var lookback = orderedCandles.Skip(i - lookbackPeriods).Take(lookbackPeriods).ToList();
            var forward = orderedCandles.Skip(i + 1).Take(lookbackPeriods).ToList();

            // Check for local minimum (support)
            if (lookback.All(c => c.Low >= current.Low) && forward.All(c => c.Low >= current.Low))
            {
                levels.Add(new SupportResistanceLevel
                {
                    Price = current.Low,
                    Type = "Support",
                    Strength = 0.5, // Will be recalculated
                    TouchCount = 1
                });
            }

            // Check for local maximum (resistance)
            if (lookback.All(c => c.High <= current.High) && forward.All(c => c.High <= current.High))
            {
                levels.Add(new SupportResistanceLevel
                {
                    Price = current.High,
                    Type = "Resistance",
                    Strength = 0.5,
                    TouchCount = 1
                });
            }
        }

        // Cluster nearby levels (within 2% of price)
        var clustered = new List<SupportResistanceLevel>();
        var processed = new HashSet<int>();

        for (int i = 0; i < levels.Count; i++)
        {
            if (processed.Contains(i)) continue;

            var level = levels[i];
            var cluster = new List<SupportResistanceLevel> { level };
            processed.Add(i);

            for (int j = i + 1; j < levels.Count; j++)
            {
                if (processed.Contains(j)) continue;

                var other = levels[j];
                var priceDiff = Math.Abs(level.Price - other.Price) / level.Price;

                if (priceDiff < 0.02 && level.Type == other.Type) // Within 2% and same type
                {
                    cluster.Add(other);
                    processed.Add(j);
                }
            }

            // Average the clustered levels
            var avgPrice = cluster.Average(l => l.Price);
            var totalTouches = cluster.Sum(l => l.TouchCount);
            var recency = CalculateRecency(cluster, orderedCandles);

            clustered.Add(new SupportResistanceLevel
            {
                Price = avgPrice,
                Type = level.Type,
                Strength = Math.Min(1.0, (totalTouches / 5.0) * 0.7 + recency * 0.3),
                TouchCount = totalTouches
            });
        }

        // Return top 5 strongest levels
        return clustered
            .OrderByDescending(l => l.Strength)
            .Take(5)
            .ToList();
    }

    private TechnicalIndicator CalculateSMA(List<OhlcDataPoint> candles, int period, string name, string color)
    {
        var dataPoints = new List<IndicatorDataPoint>();

        for (int i = period - 1; i < candles.Count; i++)
        {
            var sum = candles.Skip(i - period + 1).Take(period).Sum(c => c.Close);
            var sma = sum / period;
            dataPoints.Add(new IndicatorDataPoint(candles[i].Time, sma));
        }

        return new TechnicalIndicator
        {
            Name = name,
            Type = IndicatorType.SMA,
            DataPoints = dataPoints,
            Color = color,
            LineWidth = 1
        };
    }

    private TechnicalIndicator CalculateEMA(List<OhlcDataPoint> candles, int period, string name, string color)
    {
        var dataPoints = new List<IndicatorDataPoint>();
        var multiplier = 2.0 / (period + 1);

        if (candles.Count < period)
        {
            return new TechnicalIndicator { Name = name, Type = IndicatorType.EMA, DataPoints = dataPoints, Color = color };
        }

        // Start with SMA
        var sma = candles.Take(period).Average(c => c.Close);
        var ema = sma;
        dataPoints.Add(new IndicatorDataPoint(candles[period - 1].Time, ema));

        // Calculate EMA for remaining candles
        for (int i = period; i < candles.Count; i++)
        {
            ema = (candles[i].Close - ema) * multiplier + ema;
            dataPoints.Add(new IndicatorDataPoint(candles[i].Time, ema));
        }

        return new TechnicalIndicator
        {
            Name = name,
            Type = IndicatorType.EMA,
            DataPoints = dataPoints,
            Color = color,
            LineWidth = 1
        };
    }

    private List<TechnicalIndicator> CalculateBollingerBands(List<OhlcDataPoint> candles, int period, double stdDevMultiplier)
    {
        var sma = CalculateSMA(candles, period, "BB Middle", "#6b7280");
        var upper = new List<IndicatorDataPoint>();
        var lower = new List<IndicatorDataPoint>();

        for (int i = period - 1; i < candles.Count; i++)
        {
            var periodCandles = candles.Skip(i - period + 1).Take(period).ToList();
            var mean = periodCandles.Average(c => c.Close);
            var variance = periodCandles.Sum(c => Math.Pow(c.Close - mean, 2)) / period;
            var stdDev = Math.Sqrt(variance);

            var smaValue = sma.DataPoints.FirstOrDefault(d => d.Time == candles[i].Time)?.Value ?? mean;
            var upperValue = smaValue + (stdDev * stdDevMultiplier);
            var lowerValue = smaValue - (stdDev * stdDevMultiplier);

            upper.Add(new IndicatorDataPoint(candles[i].Time, upperValue));
            lower.Add(new IndicatorDataPoint(candles[i].Time, lowerValue));
        }

        return new List<TechnicalIndicator>
        {
            new TechnicalIndicator
            {
                Name = "BB Upper",
                Type = IndicatorType.BollingerUpper,
                DataPoints = upper,
                Color = "#ef4444",
                LineWidth = 1
            },
            sma,
            new TechnicalIndicator
            {
                Name = "BB Lower",
                Type = IndicatorType.BollingerLower,
                DataPoints = lower,
                Color = "#10b981",
                LineWidth = 1
            }
        };
    }

    private TechnicalIndicator CalculateRSI(List<OhlcDataPoint> candles, int period)
    {
        var dataPoints = new List<IndicatorDataPoint>();

        if (candles.Count < period + 1)
        {
            return new TechnicalIndicator { Name = "RSI", Type = IndicatorType.RSI, DataPoints = dataPoints };
        }

        var gains = new List<double>();
        var losses = new List<double>();

        for (int i = 1; i < candles.Count; i++)
        {
            var change = candles[i].Close - candles[i - 1].Close;
            gains.Add(change > 0 ? change : 0);
            losses.Add(change < 0 ? Math.Abs(change) : 0);
        }

        for (int i = period; i < gains.Count; i++)
        {
            var avgGain = gains.Skip(i - period + 1).Take(period).Average();
            var avgLoss = losses.Skip(i - period + 1).Take(period).Average();

            if (avgLoss == 0)
            {
                dataPoints.Add(new IndicatorDataPoint(candles[i + 1].Time, 100));
            }
            else
            {
                var rs = avgGain / avgLoss;
                var rsi = 100 - (100 / (1 + rs));
                dataPoints.Add(new IndicatorDataPoint(candles[i + 1].Time, rsi));
            }
        }

        return new TechnicalIndicator
        {
            Name = "RSI",
            Type = IndicatorType.RSI,
            DataPoints = dataPoints,
            Color = "#f59e0b",
            LineWidth = 1
        };
    }

    private List<TechnicalIndicator> CalculateMACD(List<OhlcDataPoint> candles, int fastPeriod, int slowPeriod, int signalPeriod)
    {
        var emaFast = CalculateEMA(candles, fastPeriod, "MACD Fast", "#3b82f6");
        var emaSlow = CalculateEMA(candles, slowPeriod, "MACD Slow", "#8b5cf6");

        // Use index-based alignment since both EMAs are from the same ordered candle list
        // Fast EMA[i] corresponds to candle[fastPeriod-1+i]
        // Slow EMA[i] corresponds to candle[slowPeriod-1+i]
        // To match slow EMA index I with fast EMA: fastIndex = slowPeriod - fastPeriod + I
        var fastOffset = slowPeriod - fastPeriod;
        
        var macdLine = new List<IndicatorDataPoint>();
        for (int i = 0; i < emaSlow.DataPoints.Count; i++)
        {
            var fastIndex = fastOffset + i;
            if (fastIndex >= 0 && fastIndex < emaFast.DataPoints.Count)
            {
                var slowPoint = emaSlow.DataPoints[i];
                var fastValue = emaFast.DataPoints[fastIndex].Value;
                var macd = fastValue - slowPoint.Value;
                macdLine.Add(new IndicatorDataPoint(slowPoint.Time, macd));
            }
        }

        // Calculate signal line (EMA of MACD)
        var signalLine = CalculateEMAFromValues(macdLine, signalPeriod, "MACD Signal", "#ec4899");

        // Calculate histogram using index alignment
        // Signal line[i] corresponds to macdLine[signalPeriod-1+i]
        var signalOffset = signalPeriod - 1;
        var histogram = new List<IndicatorDataPoint>();
        for (int i = 0; i < signalLine.DataPoints.Count; i++)
        {
            var macdIndex = signalOffset + i;
            if (macdIndex >= 0 && macdIndex < macdLine.Count)
            {
                var signalPoint = signalLine.DataPoints[i];
                var macdValue = macdLine[macdIndex].Value;
                var hist = macdValue - signalPoint.Value;
                histogram.Add(new IndicatorDataPoint(signalPoint.Time, hist));
            }
        }

        return new List<TechnicalIndicator>
        {
            new TechnicalIndicator
            {
                Name = "MACD",
                Type = IndicatorType.MACD,
                DataPoints = macdLine,
                Color = "#3b82f6",
                LineWidth = 2
            },
            signalLine,
            new TechnicalIndicator
            {
                Name = "MACD Hist",
                Type = IndicatorType.MACDHistogram,
                DataPoints = histogram,
                Color = "#10b981",
                LineWidth = 1
            }
        };
    }

    private TechnicalIndicator CalculateVWAP(List<OhlcDataPoint> candles)
    {
        var dataPoints = new List<IndicatorDataPoint>();
        var cumulativePriceVolume = 0.0;
        var cumulativeVolume = 0.0;

        // Estimate volume if not available (use typical volume)

        foreach (var candle in candles)
        {
            var typicalPrice = (candle.High + candle.Low + candle.Close) / 3.0;
            var volume = candle.Volume;

            cumulativePriceVolume += typicalPrice * volume;
            cumulativeVolume += volume;

            if (cumulativeVolume > 0)
            {
                var vwap = cumulativePriceVolume / cumulativeVolume;
                dataPoints.Add(new IndicatorDataPoint(candle.Time, vwap));
            }
        }

        return new TechnicalIndicator
        {
            Name = "VWAP",
            Type = IndicatorType.VWAP,
            DataPoints = dataPoints,
            Color = "#f59e0b",
            LineWidth = 2
        };
    }

    private TechnicalIndicator CalculateEMAFromValues(List<IndicatorDataPoint> values, int period, string name, string color)
    {
        var dataPoints = new List<IndicatorDataPoint>();

        if (values.Count < period)
        {
            return new TechnicalIndicator { Name = name, Type = IndicatorType.EMA, DataPoints = dataPoints, Color = color };
        }

        var multiplier = 2.0 / (period + 1);
        var sma = values.Take(period).Average(v => v.Value);
        var ema = sma;

        dataPoints.Add(new IndicatorDataPoint(values[period - 1].Time, ema));

        for (int i = period; i < values.Count; i++)
        {
            ema = (values[i].Value - ema) * multiplier + ema;
            dataPoints.Add(new IndicatorDataPoint(values[i].Time, ema));
        }

        return new TechnicalIndicator
        {
            Name = name,
            Type = IndicatorType.EMA,
            DataPoints = dataPoints,
            Color = color,
            LineWidth = 1
        };
    }

    private double CalculateRecency(List<SupportResistanceLevel> cluster, List<OhlcDataPoint> candles)
    {
        if (!candles.Any() || !cluster.Any()) return 0.5;

        var latestTime = candles.Max(c => c.Time);
        var levelPrice = cluster.Average(l => l.Price);

        // Find most recent touch
        var recentTouch = candles
            .Where(c => Math.Abs(c.Low - levelPrice) / levelPrice < 0.02 || 
                       Math.Abs(c.High - levelPrice) / levelPrice < 0.02)
            .OrderByDescending(c => c.Time)
            .FirstOrDefault();

        if (recentTouch == null) return 0.3;

        var hoursAgo = (latestTime - recentTouch.Time).TotalHours;
        // More recent = higher score (max 1.0 for < 1 hour, decreasing to 0.3 for > 24 hours)
        return Math.Max(0.3, Math.Min(1.0, 1.0 - (hoursAgo / 24.0)));
    }

    private DateTime GetPeriodStart(DateTime timestamp, int intervalMinutes)
    {
        var totalMinutesSinceEpoch = (long)(timestamp - DateTime.UnixEpoch).TotalMinutes;
        var periodMinutes = totalMinutesSinceEpoch / intervalMinutes * intervalMinutes;
        return DateTime.UnixEpoch.AddMinutes(periodMinutes);
    }
}
