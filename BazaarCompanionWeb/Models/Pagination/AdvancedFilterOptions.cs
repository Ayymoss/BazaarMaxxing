using BazaarCompanionWeb.Models.Api.Items;

namespace BazaarCompanionWeb.Models.Pagination;

public class AdvancedFilterOptions
{
    // Multi-select filters
    public List<ItemTier> SelectedTiers { get; set; } = new();
    public ManipulationFilter ManipulationStatus { get; set; } = ManipulationFilter.All;
    public VolumeTierFilter VolumeTier { get; set; } = VolumeTierFilter.All;
    
    // Range filters
    public double? MinPrice { get; set; }
    public double? MaxPrice { get; set; }
    public double? MinSpread { get; set; }
    public double? MaxSpread { get; set; }
    public long? MinVolume { get; set; }
    public long? MaxVolume { get; set; }
    public double? MinOpportunityScore { get; set; }
    public double? MaxOpportunityScore { get; set; }
    public double? MinProfitMultiplier { get; set; }
    public double? MaxProfitMultiplier { get; set; }
    public int? MinOrderCount { get; set; }
    public int? MaxOrderCount { get; set; }
    public double? MinPriceChangePercent { get; set; }
    public double? MaxPriceChangePercent { get; set; }
    
    // Advanced filters
    public TrendDirectionFilter? TrendDirection { get; set; }
    public string? CorrelationProductKey { get; set; }
    public double? CorrelationThreshold { get; set; } = 0.7;
    public int? UpdatedWithinMinutes { get; set; }
    
    // Volatility filter
    public VolatilityFilter Volatility { get; set; } = VolatilityFilter.All;
}

public enum ManipulationFilter
{
    All,
    Manipulated,
    NotManipulated
}

public enum VolumeTierFilter
{
    All,
    Low,      // < 100k
    Medium,   // 100k - 1M
    High      // > 1M
}

public enum TrendDirectionFilter
{
    Bullish,
    Bearish,
    Volatile,
    Neutral
}

public enum VolatilityFilter
{
    All,
    Low,
    Medium,
    High
}
