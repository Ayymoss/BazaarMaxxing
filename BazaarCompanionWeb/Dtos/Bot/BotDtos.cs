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
    // Queue position — what actually decides whether a limit order ever fills.
    // A flip needs BOTH legs to fill, and an order joins the back of a price level's queue. These are the
    // depths sitting at the best price on each side: post at that price and this is what clears before you.
    int TopBidDepth,
    int TopAskDepth,
    // Rough minutes for that queue to clear at the product's own average pace. Weekly volume is spread over
    // every price level and every hour of the day, so treat these as a RANKING signal, not a promise —
    // observed fill rates run several times slower than the average implies.
    double EstimatedBuyFillMinutes,
    double EstimatedSellFillMinutes,
    double EstimatedRoundTripMinutes,
    // Sizing for a given budget. Only populated when the caller passes ?budget=, and derived rather than
    // guessed: a bot should not carry a hardcoded quantity that ignores both its purse and the depth of the
    // book it is about to trade into.
    int SuggestedQuantity,
    double SuggestedCost,
    double SuggestedProfit,
    // How old the snapshot behind these numbers is. The bot re-prices against this data, so it has to be able
    // to tell "the book moved" from "our copy of the book is stale".
    double DataAgeSeconds,
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
    /// <summary>
    /// The tax the server takes on sell proceeds, as this service assumes it. Published so a client cannot
    /// silently disagree with the figure used in every profit number here — both apps had it hardcoded.
    /// </summary>
    double BazaarTaxRate,
    /// <summary>Seconds since the most recently refreshed product. Large values mean the ingest has stalled.</summary>
    double DataAgeSeconds,
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
