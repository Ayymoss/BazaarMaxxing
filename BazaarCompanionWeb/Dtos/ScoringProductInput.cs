namespace BazaarCompanionWeb.Dtos;

/// <summary>
/// Input for batch opportunity and manipulation scoring. One entry per product; order is preserved in batch results.
/// </summary>
public record ScoringProductInput(
    string ProductKey,
    double BidPrice,
    double AskPrice,
    long BidMovingWeek,
    long AskMovingWeek);
