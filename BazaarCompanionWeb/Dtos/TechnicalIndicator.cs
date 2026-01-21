namespace BazaarCompanionWeb.Dtos;

public class TechnicalIndicator
{
    public string Name { get; set; } = string.Empty;
    public IndicatorType Type { get; set; }
    public List<IndicatorDataPoint> DataPoints { get; set; } = new();
    public string Color { get; set; } = "#3b82f6";
    public int LineWidth { get; set; } = 1;
}

public enum IndicatorType
{
    SMA,
    EMA,
    BollingerUpper,
    BollingerLower,
    BollingerMiddle,
    RSI,
    MACD,
    MACDSignal,
    MACDHistogram,
    VWAP,
    Volume,
    Spread
}

public record IndicatorDataPoint(DateTime Time, double Value);

public class ChartIndicatorConfig
{
    public bool ShowSMA10 { get; set; } = false;
    public bool ShowSMA20 { get; set; } = false;
    public bool ShowSMA50 { get; set; } = false;
    public bool ShowEMA12 { get; set; } = false;
    public bool ShowEMA26 { get; set; } = false;
    public bool ShowBollingerBands { get; set; } = false;
    public bool ShowRSI { get; set; } = false;
    public bool ShowMACD { get; set; } = false;
    public bool ShowVWAP { get; set; } = false;
    public bool ShowVolume { get; set; } = true;
    public bool ShowSpread { get; set; } = false;
    public bool ShowSupportResistance { get; set; } = false;
}

public class SupportResistanceLevel
{
    public double Price { get; set; }
    public string Type { get; set; } = string.Empty; // "Support" or "Resistance"
    public double Strength { get; set; } // 0-1, based on touches and recency
    public int TouchCount { get; set; }
}
