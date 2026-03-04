namespace BazaarCompanionWeb.Dtos;

/// <summary>
/// Actionable trade recommendation for a product flip.
/// </summary>
public record TradeRecommendation(
    int SuggestedBidVolume,
    double SuggestedBidPrice,
    double SuggestedAskPrice,
    double EstimatedFillTimeHours,
    double EstimatedProfitPerUnit,
    double EstimatedTotalProfit,
    double Confidence);
