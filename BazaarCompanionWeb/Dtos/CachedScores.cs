namespace BazaarCompanionWeb.Dtos;

/// <summary>
/// Cached scores from the previous run for unchanged products.
/// </summary>
public record CachedScores(
    double FlipOpportunityScore,
    bool IsManipulated,
    double ManipulationIntensity,
    double PriceDeviationPercent);
