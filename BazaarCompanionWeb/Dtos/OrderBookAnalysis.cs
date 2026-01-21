namespace BazaarCompanionWeb.Dtos;

/// <summary>
/// Order book imbalance analysis results.
/// </summary>
public sealed record OrderBookImbalance(
    double ImbalanceRatio,          // Range: -1 to +1 (sell pressure to buy pressure)
    double TotalBuyVolume,
    double TotalSellVolume,
    double BuyPressurePercent,
    double SellPressurePercent,
    ImbalanceTrend Trend            // Improving, Worsening, Stable
);

public enum ImbalanceTrend { Improving, Worsening, Stable }

/// <summary>
/// Large order (whale) detection result.
/// </summary>
public sealed record WhaleOrder(
    double UnitPrice,
    int Amount,
    int Orders,
    double ZScore,                  // Standard deviations from mean
    bool IsBuyOrder,
    TimeSpan? Age                   // If timestamp data available
);

/// <summary>
/// Order book depth analysis at specific price levels.
/// </summary>
public sealed record OrderBookDepthMetrics(
    double BidDepth5Percent,        // Volume within 5% of best bid
    double AskDepth5Percent,        // Volume within 5% of best ask
    double DepthImbalanceRatio,     // Bid/Ask depth ratio
    double LiquidityScore,          // Composite score (0-100)
    double Spread,                  // Best ask - best bid
    double SpreadPercent,           // Spread as % of mid price
    List<PriceWall> Walls           // Detected order walls
);

public sealed record PriceWall(
    double Price,
    int Volume,
    bool IsBuyWall,
    double PercentFromCurrent       // Distance from current price
);

/// <summary>
/// Cumulative volume data point for depth chart.
/// </summary>
public sealed record DepthChartPoint(
    double Price,
    double CumulativeVolume,
    bool IsBid
);

/// <summary>
/// Support/Resistance level derived from order book.
/// </summary>
public sealed record OrderBookLevel(
    double Price,
    string Type,                    // "Support" or "Resistance"
    double Strength,                // 0-1 based on volume and order count
    int TotalVolume,
    int OrderCount,
    double PercentFromCurrent
);

/// <summary>
/// Order book statistics summary.
/// </summary>
public sealed record OrderBookStats(
    int TotalBuyOrders,
    int TotalSellOrders,
    double AvgBuyOrderSize,
    double AvgSellOrderSize,
    double LargestBuyOrder,
    double LargestSellOrder,
    double BestBid,
    double BestAsk,
    double Spread,
    double MidPrice
);

/// <summary>
/// Complete order book analysis container.
/// </summary>
public sealed record OrderBookAnalysisResult(
    OrderBookImbalance Imbalance,
    OrderBookDepthMetrics DepthMetrics,
    OrderBookStats Stats,
    List<WhaleOrder> WhaleOrders,
    List<OrderBookLevel> SupportLevels,
    List<OrderBookLevel> ResistanceLevels,
    List<DepthChartPoint> DepthChart,
    DateTime CalculatedAt
);

/// <summary>
/// Heatmap data point for order book visualization over time.
/// </summary>
public sealed record HeatmapDataPoint(DateTime Time, double Price, int Volume);
