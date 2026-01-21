namespace BazaarCompanionWeb.Dtos;

/// <summary>
/// Hot product insight - products with rapid price changes in last 15 minutes.
/// Threshold: > 5% change in 15 minutes.
/// </summary>
public sealed record HotProductInsight(
    string ProductKey,
    string ProductName,
    string Tier,
    DateTime DetectedAt,
    double PriceChangePercent,
    bool IsIncreasing,
    double CurrentPrice,
    bool IsNew
);

/// <summary>
/// Volume surge insight - products with unusual volume activity.
/// Threshold: > 2x average (200% surge).
/// </summary>
public sealed record VolumeSurgeInsight(
    string ProductKey,
    string ProductName,
    string Tier,
    DateTime DetectedAt,
    double SurgeRatio,
    bool IsBuyingSurge,
    long CurrentHourVolume,
    double Avg24hHourlyVolume
);

/// <summary>
/// Spread opportunity insight - products where spread has widened significantly.
/// Threshold: Spread widened > 20% from average.
/// </summary>
public sealed record SpreadOpportunityInsight(
    string ProductKey,
    string ProductName,
    string Tier,
    DateTime DetectedAt,
    double CurrentSpread,
    double Avg7DaySpread,
    double SpreadChangePercent,
    double OpportunityScore,
    double ProfitMultiplier
);

/// <summary>
/// Fire sale alert - products with detected manipulation (price significantly below normal).
/// Leverages existing manipulation detection from OpportunityScoringService.
/// </summary>
public sealed record FireSaleInsight(
    string ProductKey,
    string ProductName,
    string Tier,
    DateTime DetectedAt,
    double ManipulationIntensity,
    double PriceDeviationPercent,
    double CurrentPrice,
    double EstimatedFairPrice
);

/// <summary>
/// Market mover insight - top gainers and losers over 24 hours.
/// </summary>
public sealed record MarketMoverInsight(
    string ProductKey,
    string ProductName,
    string Tier,
    DateTime DetectedAt,
    double PriceChangePercent24h,
    double CurrentPrice,
    long Volume24h,
    bool IsGainer
);

/// <summary>
/// Aggregated container for all market insights.
/// </summary>
public sealed record MarketInsights(
    List<HotProductInsight> HotProducts,
    List<VolumeSurgeInsight> VolumeSurges,
    List<SpreadOpportunityInsight> SpreadOpportunities,
    List<FireSaleInsight> FireSales,
    List<MarketMoverInsight> Gainers,
    List<MarketMoverInsight> Losers,
    DateTime LastUpdated,
    int NewInsightsCount
);
