namespace BazaarCompanionWeb.Dtos;

/// <summary>
/// Minimal per-product state from the API used for change detection between runs.
/// </summary>
public record ProductState(
    string ProductKey,
    double BidOrderPrice,
    double AskOrderPrice,
    long MovingWeekSells,
    long MovingWeekBuys);
