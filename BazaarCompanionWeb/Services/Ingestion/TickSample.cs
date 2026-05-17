namespace BazaarCompanionWeb.Services.Ingestion;

public sealed record TickSample(
    double BidPrice,
    double AskPrice,
    long BidVolume,
    long AskVolume,
    DateTime Timestamp);
