namespace BazaarCompanionWeb.Services.Ingestion;

/// <summary>
/// One poll's worth of state for a product that changed. <see cref="BidVolume"/>/<see cref="AskVolume"/> are
/// resting depth; <see cref="TradedBuy"/>/<see cref="TradedSell"/> are the units that actually traded since the
/// previous poll (see <see cref="TradedDelta"/>).
/// </summary>
public sealed record TickSample(
    double BidPrice,
    double AskPrice,
    long BidVolume,
    long AskVolume,
    long TradedBuy,
    long TradedSell,
    DateTime Timestamp);
