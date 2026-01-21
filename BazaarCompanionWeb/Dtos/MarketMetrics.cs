namespace BazaarCompanionWeb.Dtos;

public class MarketMetrics
{
    public double TotalMarketCapitalization { get; set; }
    public double AverageSpread { get; set; }
    public double MarketManipulationIndex { get; set; }
    public int ActiveProductsCount { get; set; }
    public double MarketHealthScore { get; set; }
    public VolumeTrends VolumeTrends { get; set; } = new();
}

public class VolumeTrends
{
    public double Volume24h { get; set; }
    public double Volume7d { get; set; }
    public double Volume30d { get; set; }
    public List<VolumeDataPoint> TimeSeries24h { get; set; } = new();
    public List<VolumeDataPoint> TimeSeries7d { get; set; } = new();
    public List<VolumeDataPoint> TimeSeries30d { get; set; } = new();
}

public record VolumeDataPoint(DateTime Time, double Volume);
