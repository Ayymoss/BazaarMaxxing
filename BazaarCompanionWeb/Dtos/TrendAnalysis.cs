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
