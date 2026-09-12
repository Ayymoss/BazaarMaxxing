namespace BazaarCompanionWeb.Dtos;

/// <summary>
/// Per-product scoring result combining opportunity score, manipulation detection, and trade recommendation.
/// </summary>
public record ScoringResult(
    double OpportunityScore,
    bool IsManipulated,
    double ManipulationIntensity,
    double PriceDeviationPercent,
    TradeRecommendation? Recommendation,
    // <summary>
    // True when the product had too little candle history for the full score and got the simplified
    // one - in which case the manipulation flag is a default, not a finding. "Insufficient evidence" used
    // to read as "not manipulated" (audit 2026-09-12, finding 14).
    // </summary>
    bool EvidenceLimited = false);
