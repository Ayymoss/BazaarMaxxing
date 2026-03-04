namespace BazaarCompanionWeb.Dtos;

/// <summary>
/// Per-product scoring result combining opportunity score, manipulation detection, and trade recommendation.
/// </summary>
public record ScoringResult(
    double OpportunityScore,
    bool IsManipulated,
    double ManipulationIntensity,
    double PriceDeviationPercent,
    TradeRecommendation? Recommendation);
