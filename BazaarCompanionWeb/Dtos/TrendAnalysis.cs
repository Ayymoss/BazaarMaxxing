namespace BazaarCompanionWeb.Dtos;

public class ProductTrend
{
    public string ProductKey { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public double ShortTermMomentum { get; set; }
    public double MediumTermMomentum { get; set; }
    public double LongTermMomentum { get; set; }
    public string TrendDirection { get; set; } = string.Empty; // "Bullish", "Bearish", "Volatile", "Neutral"
    public double MomentumStrength { get; set; }
    public double CurrentPrice { get; set; }
    public double PriceChange6h { get; set; }
    public double PriceChange24h { get; set; }
    public double PriceChange7d { get; set; }
}

public class MarketHeatmapData
{
    public List<HeatmapPoint> Points { get; set; } = new();
    public double MaxVolatility { get; set; }
    public double MaxVolume { get; set; }
}

public class HeatmapPoint
{
    public string ProductKey { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public double Volatility { get; set; }
    public double Volume { get; set; }
    public double OpportunityScore { get; set; }
    public double X { get; set; } // Normalized volatility (0-1)
    public double Y { get; set; } // Normalized volume (0-1)
}
