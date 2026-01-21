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
    private const double WhaleZScoreThreshold = 3.0;  // 3 std deviations
    private const double WallVolumeMultiplier = 5.0;  // 5x average for wall detection
    private const double LevelClusterPercent = 1.0;   // 1% price clustering
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
            if (product.BuyBook is null || product.SellBook is null)
                return null;

            var result = Calculate(product.BuyBook, product.SellBook);
            _cache[productKey] = (result, DateTime.UtcNow);
            return result;
        }
        finally
        {
            _lock.Release();
        }
    }

    private OrderBookAnalysisResult Calculate(List<Order> buyBook, List<Order> sellBook)
    {
        var now = DateTime.UtcNow;

        var imbalance = CalculateImbalance(buyBook, sellBook);
        var stats = CalculateStats(buyBook, sellBook);
        var depthMetrics = CalculateDepthMetrics(buyBook, sellBook, stats);
        var whaleOrders = DetectWhales(buyBook, sellBook);
        var (support, resistance) = CalculateSupportResistance(buyBook, sellBook, stats.MidPrice);
        var depthChart = BuildDepthChart(buyBook, sellBook);

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

    private static OrderBookImbalance CalculateImbalance(List<Order> buyBook, List<Order> sellBook)
    {
        var totalBuy = buyBook.Sum(o => (double)o.Amount);
        var totalSell = sellBook.Sum(o => (double)o.Amount);
        var total = totalBuy + totalSell;

        var ratio = total > 0 ? (totalBuy - totalSell) / total : 0;
        var buyPercent = total > 0 ? (totalBuy / total) * 100 : 50;
        var sellPercent = total > 0 ? (totalSell / total) * 100 : 50;

        // Simple trend based on ratio magnitude
        var trend = Math.Abs(ratio) < 0.1 ? ImbalanceTrend.Stable
            : ratio > 0 ? ImbalanceTrend.Improving
            : ImbalanceTrend.Worsening;

        return new OrderBookImbalance(ratio, totalBuy, totalSell, buyPercent, sellPercent, trend);
    }

    private static OrderBookStats CalculateStats(List<Order> buyBook, List<Order> sellBook)
    {
        var bestBid = buyBook.Count > 0 ? buyBook.Max(o => o.UnitPrice) : 0;
        var bestAsk = sellBook.Count > 0 ? sellBook.Min(o => o.UnitPrice) : 0;
        var spread = bestAsk - bestBid;
        var midPrice = (bestAsk + bestBid) / 2;

        return new OrderBookStats(
            buyBook.Sum(o => o.Orders),
            sellBook.Sum(o => o.Orders),
            buyBook.Count > 0 ? buyBook.Average(o => o.Amount) : 0,
            sellBook.Count > 0 ? sellBook.Average(o => o.Amount) : 0,
            buyBook.Count > 0 ? buyBook.Max(o => o.Amount) : 0,
            sellBook.Count > 0 ? sellBook.Max(o => o.Amount) : 0,
            bestBid,
            bestAsk,
            spread,
            midPrice
        );
    }

    private OrderBookDepthMetrics CalculateDepthMetrics(
        List<Order> buyBook, List<Order> sellBook, OrderBookStats stats)
    {
        var bidDepth5 = buyBook
            .Where(o => o.UnitPrice >= stats.BestBid * 0.95)
            .Sum(o => (double)o.Amount);

        var askDepth5 = sellBook
            .Where(o => o.UnitPrice <= stats.BestAsk * 1.05)
            .Sum(o => (double)o.Amount);

        var depthRatio = askDepth5 > 0 ? bidDepth5 / askDepth5 : bidDepth5 > 0 ? 10 : 1;

        // Liquidity score: 0-100 based on depth and spread
        var spreadPercent = stats.MidPrice > 0 ? (stats.Spread / stats.MidPrice) * 100 : 100;
        var liquidityScore = Math.Max(0, Math.Min(100,
            50 * (1 - Math.Min(spreadPercent / 10, 1)) + // Lower spread = higher score
            50 * Math.Min((bidDepth5 + askDepth5) / 100000, 1) // Higher depth = higher score
        ));

        var walls = DetectWalls(buyBook, sellBook, stats);

        return new OrderBookDepthMetrics(
            bidDepth5, askDepth5, depthRatio, liquidityScore,
            stats.Spread, spreadPercent, walls
        );
    }

    private static List<PriceWall> DetectWalls(List<Order> buyBook, List<Order> sellBook, OrderBookStats stats)
    {
        List<PriceWall> walls = [];

        var avgBuyVolume = buyBook.Count > 0 ? buyBook.Average(o => o.Amount) : 0;
        var avgSellVolume = sellBook.Count > 0 ? sellBook.Average(o => o.Amount) : 0;

        foreach (var order in buyBook.Where(o => o.Amount > avgBuyVolume * WallVolumeMultiplier))
        {
            var pctFromCurrent = stats.MidPrice > 0
                ? ((order.UnitPrice - stats.MidPrice) / stats.MidPrice) * 100
                : 0;
            walls.Add(new PriceWall(order.UnitPrice, order.Amount, true, pctFromCurrent));
        }

        foreach (var order in sellBook.Where(o => o.Amount > avgSellVolume * WallVolumeMultiplier))
        {
            var pctFromCurrent = stats.MidPrice > 0
                ? ((order.UnitPrice - stats.MidPrice) / stats.MidPrice) * 100
                : 0;
            walls.Add(new PriceWall(order.UnitPrice, order.Amount, false, pctFromCurrent));
        }

        return walls.OrderByDescending(w => w.Volume).Take(10).ToList();
    }

    private static List<WhaleOrder> DetectWhales(List<Order> buyBook, List<Order> sellBook)
    {
        List<WhaleOrder> whales = [];
        var allOrders = buyBook.Concat(sellBook).ToList();

        if (allOrders.Count < 3) return whales;

        var mean = allOrders.Average(o => o.Amount);
        var stdDev = Math.Sqrt(allOrders.Average(o => Math.Pow(o.Amount - mean, 2)));

        if (stdDev <= 0) return whales;

        foreach (var order in buyBook)
        {
            var zScore = (order.Amount - mean) / stdDev;
            if (zScore >= WhaleZScoreThreshold)
            {
                whales.Add(new WhaleOrder(order.UnitPrice, order.Amount, order.Orders, zScore, true, null));
            }
        }

        foreach (var order in sellBook)
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
        List<Order> buyBook, List<Order> sellBook, double midPrice)
    {
        List<OrderBookLevel> support = [];
        List<OrderBookLevel> resistance = [];

        if (midPrice <= 0) return (support, resistance);

        // Cluster orders within 1% price bands
        var clusterSize = midPrice * LevelClusterPercent / 100;
        if (clusterSize <= 0) clusterSize = 1;

        var buyGroups = buyBook
            .GroupBy(o => Math.Round(o.UnitPrice / clusterSize))
            .Select(g => new
            {
                Price = g.Average(o => o.UnitPrice),
                Volume = g.Sum(o => o.Amount),
                OrderCount = g.Sum(o => o.Orders)
            })
            .OrderByDescending(g => g.Volume)
            .Take(MaxLevelsToShow);

        foreach (var group in buyGroups)
        {
            var strength = Math.Min(1.0, group.Volume / 10000.0); // Normalize
            var pctFromCurrent = ((group.Price - midPrice) / midPrice) * 100;
            support.Add(new OrderBookLevel(group.Price, "Support", strength, group.Volume, group.OrderCount, pctFromCurrent));
        }

        var sellGroups = sellBook
            .GroupBy(o => Math.Round(o.UnitPrice / clusterSize))
            .Select(g => new
            {
                Price = g.Average(o => o.UnitPrice),
                Volume = g.Sum(o => o.Amount),
                OrderCount = g.Sum(o => o.Orders)
            })
            .OrderByDescending(g => g.Volume)
            .Take(MaxLevelsToShow);

        foreach (var group in sellGroups)
        {
            var strength = Math.Min(1.0, group.Volume / 10000.0);
            var pctFromCurrent = ((group.Price - midPrice) / midPrice) * 100;
            resistance.Add(new OrderBookLevel(group.Price, "Resistance", strength, group.Volume, group.OrderCount, pctFromCurrent));
        }

        return (support, resistance);
    }

    private static List<DepthChartPoint> BuildDepthChart(List<Order> buyBook, List<Order> sellBook)
    {
        List<DepthChartPoint> points = [];

        // Buy side: cumulative from highest bid down
        var sortedBuys = buyBook.OrderByDescending(o => o.UnitPrice).ToList();
        double cumBuy = 0;
        foreach (var order in sortedBuys)
        {
            cumBuy += order.Amount;
            points.Add(new DepthChartPoint(order.UnitPrice, cumBuy, true));
        }

        // Sell side: cumulative from lowest ask up
        var sortedSells = sellBook.OrderBy(o => o.UnitPrice).ToList();
        double cumSell = 0;
        foreach (var order in sortedSells)
        {
            cumSell += order.Amount;
            points.Add(new DepthChartPoint(order.UnitPrice, cumSell, false));
        }

        return points;
    }

    /// <summary>
    /// Stores a snapshot of current order book for heatmap analysis.
    /// Called periodically by HyPixelService.
    /// </summary>
    public async Task StoreSnapshotAsync(
        string productKey,
        List<Order> buyBook,
        List<Order> sellBook,
        CancellationToken ct = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(ct);
        var now = DateTime.UtcNow;

        // Find mid price
        var bestBid = buyBook.Count > 0 ? buyBook.Max(o => o.UnitPrice) : 0;
        var bestAsk = sellBook.Count > 0 ? sellBook.Min(o => o.UnitPrice) : 0;
        var midPrice = (bestBid + bestAsk) / 2;

        if (midPrice <= 0) return;

        // Sample at 1% price intervals
        var priceStep = midPrice * 0.01;
        List<EFOrderBookSnapshot> snapshots = [];

        for (var factor = -20; factor <= 20; factor++)
        {
            var priceLevel = midPrice + (factor * priceStep);
            var halfStep = priceStep / 2;

            var buyVol = buyBook.Where(o => Math.Abs(o.UnitPrice - priceLevel) < halfStep).Sum(o => o.Amount);
            var sellVol = sellBook.Where(o => Math.Abs(o.UnitPrice - priceLevel) < halfStep).Sum(o => o.Amount);
            var buyOrders = buyBook.Where(o => Math.Abs(o.UnitPrice - priceLevel) < halfStep).Sum(o => o.Orders);
            var sellOrders = sellBook.Where(o => Math.Abs(o.UnitPrice - priceLevel) < halfStep).Sum(o => o.Orders);

            if (buyVol > 0 || sellVol > 0)
            {
                snapshots.Add(new EFOrderBookSnapshot
                {
                    ProductKey = productKey,
                    Timestamp = now,
                    PriceLevel = priceLevel,
                    BuyVolume = buyVol,
                    SellVolume = sellVol,
                    BuyOrderCount = buyOrders,
                    SellOrderCount = sellOrders
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
            s.BuyVolume + s.SellVolume
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
