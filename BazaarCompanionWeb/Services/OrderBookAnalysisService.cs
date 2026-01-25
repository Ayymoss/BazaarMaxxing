using BazaarCompanionWeb.Context;
using BazaarCompanionWeb.Dtos;
using BazaarCompanionWeb.Entities;
using BazaarCompanionWeb.Interfaces.Database;
using Microsoft.EntityFrameworkCore;

namespace BazaarCompanionWeb.Services;

/// <summary>
/// Service for advanced order book analysis including imbalance, whale detection,
/// depth metrics, and support/resistance level detection.
/// </summary>
public sealed partial class OrderBookAnalysisService(
    IDbContextFactory<DataContext> contextFactory,
    IProductRepository productRepository,
    ILogger<OrderBookAnalysisService> logger)
{
    // Configuration
    private const double WhaleZScoreThreshold = 3.0; // 3 std deviations
    private const double WallVolumeMultiplier = 5.0; // 5x average for wall detection
    private const double LevelClusterPercent = 1.0; // 1% price clustering
    private const int MaxWhalesToShow = 10;
    private const int MaxLevelsToShow = 5;
    private const int SnapshotRetentionDays = 7;

    // Cache (per product, short TTL)
    private readonly Dictionary<string, (OrderBookAnalysisResult Result, DateTime CachedAt)> _cache = new();
    private readonly TimeSpan _cacheDuration = TimeSpan.FromSeconds(30);
    private readonly SemaphoreSlim _lock = new(1, 1);

    /// <summary>
    /// Analyzes order book for a product. Uses cache if available.
    /// </summary>
    public async Task<OrderBookAnalysisResult?> AnalyzeAsync(
        string productKey,
        CancellationToken ct = default)
    {
        if (_cache.TryGetValue(productKey, out var cached) &&
            DateTime.UtcNow - cached.CachedAt < _cacheDuration)
        {
            return cached.Result;
        }

        await _lock.WaitAsync(ct);
        try
        {
            var product = await productRepository.GetProductAsync(productKey, ct);
            if (product.BidBook is null || product.AskBook is null)
                return null;

            var result = Calculate(product.BidBook, product.AskBook);
            _cache[productKey] = (result, DateTime.UtcNow);
            return result;
        }
        finally
        {
            _lock.Release();
        }
    }

    private OrderBookAnalysisResult Calculate(List<Order> bidBook, List<Order> askBook)
    {
        var now = DateTime.UtcNow;

        var imbalance = CalculateImbalance(bidBook, askBook);
        var stats = CalculateStats(bidBook, askBook);
        var depthMetrics = CalculateDepthMetrics(bidBook, askBook, stats);
        var whaleOrders = DetectWhales(bidBook, askBook);
        var (support, resistance) = CalculateSupportResistance(bidBook, askBook, stats.MidPrice);
        var depthChart = BuildDepthChart(bidBook, askBook);

        return new OrderBookAnalysisResult(
            imbalance,
            depthMetrics,
            stats,
            whaleOrders,
            support,
            resistance,
            depthChart,
            now
        );
    }

    private static OrderBookImbalance CalculateImbalance(List<Order> bidBook, List<Order> askBook)
    {
        var totalBid = bidBook.Sum(o => (double)o.Amount);
        var totalAsk = askBook.Sum(o => (double)o.Amount);
        var total = totalBid + totalAsk;

        var ratio = total > 0 ? (totalBid - totalAsk) / total : 0;
        var bidPercent = total > 0 ? totalBid / total : 50;
        var askPercent = total > 0 ? totalAsk / total : 50;

        // Simple trend based on ratio magnitude
        var trend = Math.Abs(ratio) < 0.1 ? ImbalanceTrend.Stable
            : ratio > 0 ? ImbalanceTrend.Improving
            : ImbalanceTrend.Worsening;

        return new OrderBookImbalance(ratio, totalBid, totalAsk, bidPercent, askPercent, trend);
    }

    private static OrderBookStats CalculateStats(List<Order> bidBook, List<Order> askBook)
    {
        var bestBid = bidBook.Count > 0 ? bidBook.Max(o => o.UnitPrice) : 0;
        var bestAsk = askBook.Count > 0 ? askBook.Min(o => o.UnitPrice) : 0;
        var spread = bestAsk - bestBid;
        var midPrice = (bestAsk + bestBid) / 2;

        return new OrderBookStats(
            bidBook.Sum(o => o.Orders),
            askBook.Sum(o => o.Orders),
            bidBook.Count > 0 ? bidBook.Average(o => o.Amount) : 0,
            askBook.Count > 0 ? askBook.Average(o => o.Amount) : 0,
            bidBook.Count > 0 ? bidBook.Max(o => o.Amount) : 0,
            askBook.Count > 0 ? askBook.Max(o => o.Amount) : 0,
            bestBid,
            bestAsk,
            spread,
            midPrice
        );
    }

    private OrderBookDepthMetrics CalculateDepthMetrics(
        List<Order> bidBook, List<Order> askBook, OrderBookStats stats)
    {
        var bidDepth5 = bidBook
            .Where(o => o.UnitPrice >= stats.BestBid * 0.95)
            .Sum(o => (double)o.Amount);

        var askDepth5 = askBook
            .Where(o => o.UnitPrice <= stats.BestAsk * 1.05)
            .Sum(o => (double)o.Amount);

        var depthRatio = askDepth5 > 0 ? bidDepth5 / askDepth5 : bidDepth5 > 0 ? 10 : 1;

        // Liquidity score: 0-100 based on depth and spread
        var spreadPercent = stats.MidPrice > 0 ? (stats.Spread / stats.MidPrice) * 100 : 100;
        var liquidityScore = Math.Max(0, Math.Min(100,
            50 * (1 - Math.Min(spreadPercent / 10, 1)) + // Lower spread = higher score
            50 * Math.Min((bidDepth5 + askDepth5) / 100000, 1) // Higher depth = higher score
        ));

        var walls = DetectWalls(bidBook, askBook, stats);

        return new OrderBookDepthMetrics(
            bidDepth5, askDepth5, depthRatio, liquidityScore,
            stats.Spread, spreadPercent, walls
        );
    }

    private static List<PriceWall> DetectWalls(List<Order> bidBook, List<Order> askBook, OrderBookStats stats)
    {
        List<PriceWall> walls = [];

        var avgBidVolume = bidBook.Count > 0 ? bidBook.Average(o => o.Amount) : 0;
        var avgAskVolume = askBook.Count > 0 ? askBook.Average(o => o.Amount) : 0;

        foreach (var order in bidBook.Where(o => o.Amount > avgBidVolume * WallVolumeMultiplier))
        {
            var pctFromCurrent = stats.MidPrice > 0
                ? (order.UnitPrice - stats.MidPrice) / stats.MidPrice
                : 0;
            walls.Add(new PriceWall(order.UnitPrice, order.Amount, true, pctFromCurrent));
        }

        foreach (var order in askBook.Where(o => o.Amount > avgAskVolume * WallVolumeMultiplier))
        {
            var pctFromCurrent = stats.MidPrice > 0
                ? (order.UnitPrice - stats.MidPrice) / stats.MidPrice
                : 0;
            walls.Add(new PriceWall(order.UnitPrice, order.Amount, false, pctFromCurrent));
        }

        return walls.OrderByDescending(w => w.Volume).Take(10).ToList();
    }

    private static List<WhaleOrder> DetectWhales(List<Order> bidBook, List<Order> askBook)
    {
        List<WhaleOrder> whales = [];
        var allOrders = bidBook.Concat(askBook).ToList();

        if (allOrders.Count < 3) return whales;

        var mean = allOrders.Average(o => o.Amount);
        var stdDev = Math.Sqrt(allOrders.Average(o => Math.Pow(o.Amount - mean, 2)));

        if (stdDev <= 0) return whales;

        foreach (var order in bidBook)
        {
            var zScore = (order.Amount - mean) / stdDev;
            if (zScore >= WhaleZScoreThreshold)
            {
                whales.Add(new WhaleOrder(order.UnitPrice, order.Amount, order.Orders, zScore, true, null));
            }
        }

        foreach (var order in askBook)
        {
            var zScore = (order.Amount - mean) / stdDev;
            if (zScore >= WhaleZScoreThreshold)
            {
                whales.Add(new WhaleOrder(order.UnitPrice, order.Amount, order.Orders, zScore, false, null));
            }
        }

        return whales.OrderByDescending(w => w.ZScore).Take(MaxWhalesToShow).ToList();
    }

    private (List<OrderBookLevel> Support, List<OrderBookLevel> Resistance) CalculateSupportResistance(
        List<Order> bidBook, List<Order> askBook, double midPrice)
    {
        List<OrderBookLevel> support = [];
        List<OrderBookLevel> resistance = [];

        if (midPrice <= 0) return (support, resistance);

        // Cluster orders within 1% price bands
        var clusterSize = midPrice * LevelClusterPercent / 100;
        if (clusterSize <= 0) clusterSize = 1;

        var bidGroups = bidBook
            .GroupBy(o => Math.Round(o.UnitPrice / clusterSize))
            .Select(g => new
            {
                Price = g.Average(o => o.UnitPrice),
                Volume = g.Sum(o => o.Amount),
                OrderCount = g.Sum(o => o.Orders)
            })
            .OrderByDescending(g => g.Volume)
            .Take(MaxLevelsToShow);

        foreach (var group in bidGroups)
        {
            var strength = Math.Min(1.0, group.Volume / 10000.0); // Normalize
            var pctFromCurrent = (group.Price - midPrice) / midPrice;
            support.Add(new OrderBookLevel(group.Price, "Support", strength, group.Volume, group.OrderCount, pctFromCurrent));
        }

        var askGroups = askBook
            .GroupBy(o => Math.Round(o.UnitPrice / clusterSize))
            .Select(g => new
            {
                Price = g.Average(o => o.UnitPrice),
                Volume = g.Sum(o => o.Amount),
                OrderCount = g.Sum(o => o.Orders)
            })
            .OrderByDescending(g => g.Volume)
            .Take(MaxLevelsToShow);

        foreach (var group in askGroups)
        {
            var strength = Math.Min(1.0, group.Volume / 10000.0);
            var pctFromCurrent = (group.Price - midPrice) / midPrice;
            resistance.Add(new OrderBookLevel(group.Price, "Resistance", strength, group.Volume, group.OrderCount, pctFromCurrent));
        }

        return (support, resistance);
    }

    private static List<DepthChartPoint> BuildDepthChart(List<Order> bidBook, List<Order> askBook)
    {
        List<DepthChartPoint> points = [];

        // Bid side: cumulative from highest bid down
        var sortedBids = bidBook.OrderByDescending(o => o.UnitPrice).ToList();
        double cumBid = 0;
        foreach (var order in sortedBids)
        {
            cumBid += order.Amount;
            points.Add(new DepthChartPoint(order.UnitPrice, cumBid, true));
        }

        // Ask side: cumulative from lowest ask up
        var sortedAsks = askBook.OrderBy(o => o.UnitPrice).ToList();
        double cumAsk = 0;
        foreach (var order in sortedAsks)
        {
            cumAsk += order.Amount;
            points.Add(new DepthChartPoint(order.UnitPrice, cumAsk, false));
        }

        return points;
    }

    /// <summary>
    /// Stores a snapshot of current order book for heatmap analysis.
    /// Called periodically by HyPixelService.
    /// </summary>
    public async Task StoreSnapshotAsync(
        string productKey,
        List<Order> bidBook,
        List<Order> askBook,
        CancellationToken ct = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(ct);
        var now = DateTime.UtcNow;

        // Find mid price
        var bestBid = bidBook.Count > 0 ? bidBook.Max(o => o.UnitPrice) : 0;
        var bestAsk = askBook.Count > 0 ? askBook.Min(o => o.UnitPrice) : 0;
        var midPrice = (bestBid + bestAsk) / 2;

        if (midPrice <= 0) return;

        // Sample at 1% price intervals
        var priceStep = midPrice * 0.01;
        List<EFOrderBookSnapshot> snapshots = [];

        for (var factor = -20; factor <= 20; factor++)
        {
            var priceLevel = midPrice + (factor * priceStep);
            var halfStep = priceStep / 2;

            var bidVol = bidBook.Where(o => Math.Abs(o.UnitPrice - priceLevel) < halfStep).Sum(o => o.Amount);
            var askVol = askBook.Where(o => Math.Abs(o.UnitPrice - priceLevel) < halfStep).Sum(o => o.Amount);
            var bidOrders = bidBook.Where(o => Math.Abs(o.UnitPrice - priceLevel) < halfStep).Sum(o => o.Orders);
            var askOrders = askBook.Where(o => Math.Abs(o.UnitPrice - priceLevel) < halfStep).Sum(o => o.Orders);

            if (bidVol > 0 || askVol > 0)
            {
                snapshots.Add(new EFOrderBookSnapshot
                {
                    ProductKey = productKey,
                    Timestamp = now,
                    PriceLevel = priceLevel,
                    BidVolume = bidVol,
                    AskVolume = askVol,
                    BidOrderCount = bidOrders,
                    AskOrderCount = askOrders
                });
            }
        }

        if (snapshots.Count > 0)
        {
            await context.OrderBookSnapshots.AddRangeAsync(snapshots, ct);
            await context.SaveChangesAsync(ct);
        }
    }

    /// <summary>
    /// Gets heatmap data for the specified time range.
    /// </summary>
    public async Task<List<HeatmapDataPoint>> GetHeatmapDataAsync(
        string productKey,
        int hours = 24,
        CancellationToken ct = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(ct);
        var cutoff = DateTime.UtcNow.AddHours(-hours);

        var snapshots = await context.OrderBookSnapshots
            .AsNoTracking()
            .Where(s => s.ProductKey == productKey && s.Timestamp >= cutoff)
            .OrderBy(s => s.Timestamp)
            .ToListAsync(ct);

        return snapshots.Select(s => new HeatmapDataPoint(
            s.Timestamp,
            s.PriceLevel,
            s.BidVolume + s.AskVolume
        )).ToList();
    }

    /// <summary>
    /// Cleans up old snapshots (call periodically).
    /// Only affects order book snapshot data - does not touch OHLC/price history.
    /// </summary>
    public async Task CleanupOldSnapshotsAsync(CancellationToken ct = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(ct);
        var cutoff = DateTime.UtcNow.AddDays(-SnapshotRetentionDays);

        var deletedCount = await context.OrderBookSnapshots
            .Where(s => s.Timestamp < cutoff)
            .ExecuteDeleteAsync(ct);

        if (deletedCount > 0)
        {
            LogSnapshotCleanup(deletedCount, SnapshotRetentionDays);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Cleaned up {Count} order book snapshots older than {Days} days")]
    private partial void LogSnapshotCleanup(int count, int days);
}
