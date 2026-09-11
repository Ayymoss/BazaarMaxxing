namespace BazaarCompanionWeb.Dtos;

/// <summary>
/// The forming one-minute bar for a product, pushed on every poll that changed it. Volume figures are the
/// units traded within this minute (derived from the moving-week counter increments), so a consumer folding
/// this into a longer bar must add them once per minute, not overwrite.
/// </summary>
public record LiveTick(
    DateTime Time,
    double Open,
    double High,
    double Low,
    double Close,
    double Volume,
    double AskClose,
    double BuyVolume = 0,
    double SellVolume = 0,
    double AskOpen = 0,
    double AskHigh = 0,
    double AskLow = 0);
