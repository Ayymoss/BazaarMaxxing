using BazaarCompanionWeb.Models.Api.Items;

namespace BazaarCompanionWeb.Dtos.Bot;

public sealed record FlipOpportunity(
    string ProductKey,
    string Name,
    ItemTier Tier,
    bool Unstackable,
    // Bid side (buy order placement data)
    double BestBidPrice,
    int BidOrders,
    int BidVolume,
    double BidWeekVolume,
    // Ask side (sell offer placement data)
    double BestAskPrice,
    int AskOrders,
    int AskVolume,
    double AskWeekVolume,
    // Flip metrics
    double Spread,
    double SpreadPercent,
    double ProfitMultiplier,
    double OpportunityScore,
    double EstimatedProfitPerUnit,
    // Risk indicators
    bool IsManipulated,
    double ManipulationIntensity,
    double PriceDeviationPercent
);

public sealed record BotProductDetail(
    string ProductKey,
    string Name,
    ItemTier Tier,
    bool Unstackable,
    // Current prices
    double BidPrice,
    double AskPrice,
    double Spread,
    // Volume
    double BidWeekVolume,
    double AskWeekVolume,
    double TotalWeekVolume,
    int BidOrders,
    int AskOrders,
    int BidVolume,
    int AskVolume,
    // Scoring
    double OpportunityScore,
    double ProfitMultiplier,
    // Manipulation
    bool IsManipulated,
    double ManipulationIntensity,
    double PriceDeviationPercent,
    // Order books
    List<Order> BidBook,
    List<Order> AskBook,
    // Price history (daily snapshots)
    List<PriceHistorySnapshot> PriceHistory
);

public sealed record BotProductSummary(
    string ProductKey,
    string Name,
    double BidPrice,
    double AskPrice,
    double Spread,
    double OpportunityScore,
    bool IsManipulated,
    double BidWeekVolume,
    double AskWeekVolume
);

public sealed record BotMarketHealth(
    double HealthScore,
    double AverageSpread,
    double ManipulationIndex,
    int ActiveProductsCount,
    double TotalMarketCap,
    double Volume24h,
    double Volume7d,
    string Recommendation,
    string RecommendationReason
);
