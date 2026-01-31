using BazaarCompanionWeb.Context;
using BazaarCompanionWeb.Dtos;
using BazaarCompanionWeb.Entities;
using BazaarCompanionWeb.Interfaces.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BazaarCompanionWeb.Services;

/// <summary>
/// Service for calculating and caching real-time market insights.
/// Provides hot products, volume surges, spread opportunities, fire sales, and market movers.
/// </summary>
public sealed partial class MarketInsightsService(
    IDbContextFactory<DataContext> contextFactory,
    IServiceScopeFactory scopeFactory,
    ILogger<MarketInsightsService> logger)
{
    // Configuration thresholds
    private const double HotProductThreshold = 5.0; // 5% change in 15 min
    private const double VolumeSurgeThreshold = 2.0; // 2x average volume
    private const double SpreadWideningThreshold = 20.0; // 20% spread increase
    private const long MinimumWeeklyVolumeForInsights = 10_000; // Filter out low-volume items
    private const int MaxInsightsPerCategory = 10;

    // Cache infrastructure
    private MarketInsights? _cachedInsights;
    private readonly HashSet<string> _previousHotProductKeys = [];
    private readonly SemaphoreSlim _calculationLock = new(1, 1);

    /// <summary>
    /// Gets the current market insights (uses cache).
    /// </summary>
    public async Task<MarketInsights> GetInsightsAsync(CancellationToken ct = default)
    {
        if (_cachedInsights is not null)
            return _cachedInsights;

        await RefreshInsightsAsync(ct);
        return _cachedInsights ?? CreateEmptyInsights();
    }

    /// <summary>
    /// Refreshes insights cache. Called after data fetch cycle.
    /// </summary>
    public async Task RefreshInsightsAsync(CancellationToken ct = default)
    {
        if (!await _calculationLock.WaitAsync(TimeSpan.FromSeconds(5), ct))
        {
            LogRefreshSkipped();
            return;
        }

        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var ohlcRepository = scope.ServiceProvider.GetRequiredService<IOhlcRepository>();

            await using var context = await contextFactory.CreateDbContextAsync(ct);
            var now = DateTime.UtcNow;

            var hotProducts = await CalculateHotProductsAsync(context, ohlcRepository, now, ct);
            var volumeSurges = await CalculateVolumeSurgesAsync(context, ohlcRepository, now, ct);
            var spreadOpportunities = await CalculateSpreadOpportunitiesAsync(context, ohlcRepository, now, ct);
            var fireSales = await CalculateFireSalesAsync(context, ohlcRepository, now, ct);
            var (gainers, losers) = await CalculateMarketMoversAsync(context, ohlcRepository, now, ct);

            // Track new hot products
            var currentHotKeys = hotProducts.Select(h => h.ProductKey).ToHashSet();
            var newCount = currentHotKeys.Except(_previousHotProductKeys).Count();
            _previousHotProductKeys.Clear();
            _previousHotProductKeys.UnionWith(currentHotKeys);

            _cachedInsights = new MarketInsights(
                hotProducts,
                volumeSurges,
                spreadOpportunities,
                fireSales,
                gainers,
                losers,
                now,
                newCount
            );

            LogInsightsRefreshed(
                hotProducts.Count,
                volumeSurges.Count,
                spreadOpportunities.Count,
                fireSales.Count,
                gainers.Count + losers.Count);
        }
        catch (Exception ex)
        {
            LogRefreshError(ex);
        }
        finally
        {
            _calculationLock.Release();
        }
    }

    /// <summary>
    /// Calculates hot products - products with rapid price changes in last 15 minutes.
    /// </summary>
    private async Task<List<HotProductInsight>> CalculateHotProductsAsync(
        DataContext context,
        IOhlcRepository ohlcRepository,
        DateTime now,
        CancellationToken ct)
    {
        List<HotProductInsight> results = [];

        var products = await context.Products
            .Include(p => p.Bid)
            .AsNoTracking()
            .Where(p => p.Bid.OrderVolumeWeek >= MinimumWeeklyVolumeForInsights)
            .ToListAsync(ct);

        foreach (var product in products)
        {
            var candles = await ohlcRepository.GetCandlesAsync(
                product.ProductKey,
                CandleInterval.FifteenMinute,
                limit: 2,
                ct);

            if (candles.Count < 2) continue;

            var ordered = candles.OrderBy(c => c.Time).ToList();
            var price15MinAgo = ordered[^2].Close;
            var currentPrice = ordered[^1].Close;

            if (price15MinAgo <= 0) continue;

            var changePercent = (currentPrice - price15MinAgo) / price15MinAgo;
            var absChange = Math.Abs(changePercent);

            if (absChange < HotProductThreshold) continue;

            var isNew = !_previousHotProductKeys.Contains(product.ProductKey);

            results.Add(new HotProductInsight(
                product.ProductKey,
                product.FriendlyName,
                product.Tier.ToString(),
                now,
                absChange,
                changePercent > 0,
                currentPrice,
                isNew
            ));
        }

        return results
            .OrderByDescending(h => h.PriceChangePercent)
            .Take(MaxInsightsPerCategory)
            .ToList();
    }

    /// <summary>
    /// Calculates volume surges - products with unusual volume activity.
    /// </summary>
    private async Task<List<VolumeSurgeInsight>> CalculateVolumeSurgesAsync(
        DataContext context,
        IOhlcRepository ohlcRepository,
        DateTime now,
        CancellationToken ct)
    {
        List<VolumeSurgeInsight> results = [];

        var products = await context.Products
            .Include(p => p.Bid)
            .Include(p => p.Ask)
            .AsNoTracking()
            .Where(p => p.Bid.OrderVolumeWeek >= MinimumWeeklyVolumeForInsights)
            .ToListAsync(ct);

        var oneHourAgo = now.AddHours(-1);
        var twentyFourHoursAgo = now.AddHours(-24);

        foreach (var product in products)
        {
            // Get candles for volume analysis
            var candles = await ohlcRepository.GetCandlesAsync(
                product.ProductKey,
                CandleInterval.OneHour,
                limit: 25, // Last 24 hours + current
                ct);

            if (candles.Count < 2) continue;

            var ordered = candles.OrderBy(c => c.Time).ToList();
            var currentHourCandle = ordered.LastOrDefault();
            var historicalCandles = ordered.SkipLast(1).ToList();

            if (currentHourCandle?.Volume is null or 0 || historicalCandles.Count == 0) continue;

            var currentHourVolume = (long)currentHourCandle.Volume;
            var avgHourlyVolume = historicalCandles
                .Where(c => c.Volume > 0)
                .Select(c => c.Volume)
                .DefaultIfEmpty(1)
                .Average();

            if (avgHourlyVolume <= 0) continue;

            var surgeRatio = currentHourVolume / avgHourlyVolume;

            if (surgeRatio < VolumeSurgeThreshold) continue;

            // Determine if buying or selling surge based on price movement
            var priceChange = currentHourCandle.Close - currentHourCandle.Open;
            var isBuyingSurge = priceChange >= 0;

            results.Add(new VolumeSurgeInsight(
                product.ProductKey,
                product.FriendlyName,
                product.Tier.ToString(),
                now,
                surgeRatio,
                isBuyingSurge,
                currentHourVolume,
                avgHourlyVolume
            ));
        }

        return results
            .OrderByDescending(v => v.SurgeRatio)
            .Take(MaxInsightsPerCategory)
            .ToList();
    }

    /// <summary>
    /// Calculates spread opportunities - products where spread has widened significantly.
    /// </summary>
    private async Task<List<SpreadOpportunityInsight>> CalculateSpreadOpportunitiesAsync(
        DataContext context,
        IOhlcRepository ohlcRepository,
        DateTime now,
        CancellationToken ct)
    {
        List<SpreadOpportunityInsight> results = [];

        var products = await context.Products
            .Include(p => p.Bid)
            .Include(p => p.Ask)
            .Include(p => p.Meta)
            .AsNoTracking()
            .Where(p => p.Bid.OrderVolumeWeek >= MinimumWeeklyVolumeForInsights)
            .ToListAsync(ct);

        foreach (var product in products)
        {
            var currentSpread = product.Ask.UnitPrice - product.Bid.UnitPrice;
            if (currentSpread <= 0) continue;

            // Get 7-day candle history for spread calculation
            var candles = await ohlcRepository.GetCandlesAsync(
                product.ProductKey,
                CandleInterval.OneHour,
                limit: 7 * 24,
                ct);

            if (candles.Count < 24) continue;

            var historicalSpreads = candles
                .Where(c => c.Spread > 0)
                .Select(c => c.Spread)
                .ToList();

            // If no historical spread data, skip this product (data not yet populated)
            if (historicalSpreads.Count < 12) continue;

            var avgSpread = historicalSpreads.Average();
            if (avgSpread <= 0) continue;

            var spreadChangePercent = ((currentSpread - avgSpread) / avgSpread) * 100;

            if (spreadChangePercent < SpreadWideningThreshold) continue;

            results.Add(new SpreadOpportunityInsight(
                product.ProductKey,
                product.FriendlyName,
                product.Tier.ToString(),
                now,
                currentSpread,
                avgSpread,
                spreadChangePercent,
                product.Meta.FlipOpportunityScore,
                product.Meta.ProfitMultiplier
            ));
        }

        return results
            .OrderByDescending(s => s.SpreadChangePercent)
            .Take(MaxInsightsPerCategory)
            .ToList();
    }

    /// <summary>
    /// Calculates fire sale alerts using stacked indicators:
    /// 1. Price 20%+ below 24h average (short-term deviation)
    /// 2. Price 10%+ below 7-day low (breaking historical support)
    /// 3. Volume 50%+ above average (panic selling confirmation)
    /// </summary>
    private async Task<List<FireSaleInsight>> CalculateFireSalesAsync(
        DataContext context,
        IOhlcRepository ohlcRepository,
        DateTime now,
        CancellationToken ct)
    {
        // Fire sale thresholds
        const double priceDeviation24hThreshold = 0.20; // 20% below 24h average
        const double priceBelow7dLowThreshold = 0.10;   // 10% below 7-day low
        const double volumeSpikeThreshold = 1.50;       // 50% above average volume

        List<FireSaleInsight> results = [];

        var products = await context.Products
            .Include(p => p.Bid)
            .Include(p => p.Ask)
            .AsNoTracking()
            .Where(p => p.Bid.OrderVolumeWeek >= MinimumWeeklyVolumeForInsights)
            .ToListAsync(ct);

        foreach (var product in products)
        {
            // Get 7 days of hourly candles for analysis
            var candles = await ohlcRepository.GetCandlesAsync(
                product.ProductKey,
                CandleInterval.OneHour,
                limit: 7 * 24, // 168 hours
                ct);

            if (candles.Count < 24) continue; // Need at least 24h of data

            var ordered = candles.OrderBy(c => c.Time).ToList();
            var currentAskPrice = product.Ask.UnitPrice;
            if (currentAskPrice <= 0) continue;

            // Calculate 24h average price
            var last24hCandles = ordered.TakeLast(24).ToList();
            var avg24hPrice = last24hCandles.Average(c => c.Close);
            if (avg24hPrice <= 0) continue;

            // Calculate 7-day low
            var low7dPrice = ordered.Min(c => c.Low);
            if (low7dPrice <= 0) continue;

            // Calculate current hour volume vs average
            var currentHourCandle = ordered.LastOrDefault();
            var historicalCandles = ordered.SkipLast(1).ToList();
            var avgHourlyVolume = historicalCandles
                .Where(c => c.Volume > 0)
                .Select(c => c.Volume)
                .DefaultIfEmpty(1)
                .Average();

            var currentVolume = currentHourCandle?.Volume ?? 0;

            // === STACKED INDICATOR CHECKS ===
            // 1. Price must be 20%+ below 24h average
            var priceBelow24hAvg = currentAskPrice < avg24hPrice * (1 - priceDeviation24hThreshold);

            // 2. Price must be 10%+ below 7-day low (breaking support)
            var priceBelow7dLow = currentAskPrice < low7dPrice * (1 - priceBelow7dLowThreshold);

            // 3. Volume must be 50%+ above average (panic selling)
            var volumeSpike = avgHourlyVolume > 0 && currentVolume > avgHourlyVolume * volumeSpikeThreshold;

            // All three conditions must be met
            if (!priceBelow24hAvg || !priceBelow7dLow || !volumeSpike) continue;

            // Calculate deviation percentage for display
            var deviationPercent = ((currentAskPrice - avg24hPrice) / avg24hPrice) * 100;
            var intensityScore = Math.Min(1.0, Math.Abs(deviationPercent) / 50); // 0-1 scale

            results.Add(new FireSaleInsight(
                product.ProductKey,
                product.FriendlyName,
                product.Tier.ToString(),
                now,
                intensityScore,
                deviationPercent,
                currentAskPrice,
                avg24hPrice // Use 24h average as "fair price" estimate
            ));
        }

        return results
            .OrderByDescending(f => Math.Abs(f.PriceDeviationPercent))
            .Take(MaxInsightsPerCategory)
            .ToList();
    }


    /// <summary>
    /// Calculates market movers - top gainers and losers over 24 hours.
    /// </summary>
    private async Task<(List<MarketMoverInsight> Gainers, List<MarketMoverInsight> Losers)> CalculateMarketMoversAsync(
        DataContext context,
        IOhlcRepository ohlcRepository,
        DateTime now,
        CancellationToken ct)
    {
        List<MarketMoverInsight> allMovers = [];

        var products = await context.Products
            .Include(p => p.Bid)
            .Include(p => p.Ask)
            .AsNoTracking()
            .Where(p => p.Bid.OrderVolumeWeek >= MinimumWeeklyVolumeForInsights)
            .ToListAsync(ct);

        foreach (var product in products)
        {
            var candles = await ohlcRepository.GetCandlesAsync(
                product.ProductKey,
                CandleInterval.OneHour,
                limit: 25,
                ct);

            if (candles.Count < 24) continue;

            var ordered = candles.OrderBy(c => c.Time).ToList();
            var price24hAgo = ordered.First().Open;
            var currentPrice = ordered.Last().Close;

            if (price24hAgo <= 0) continue;

            var changePercent = (currentPrice - price24hAgo) / price24hAgo;
            var volume24h = (long)ordered.Sum(c => c.Volume);

            allMovers.Add(new MarketMoverInsight(
                product.ProductKey,
                product.FriendlyName,
                product.Tier.ToString(),
                now,
                changePercent,
                currentPrice,
                volume24h,
                changePercent >= 0
            ));
        }

        var gainers = allMovers
            .Where(m => m.IsGainer)
            .OrderByDescending(m => m.PriceChangePercent24h)
            .Take(MaxInsightsPerCategory)
            .ToList();

        var losers = allMovers
            .Where(m => !m.IsGainer)
            .OrderBy(m => m.PriceChangePercent24h)
            .Take(MaxInsightsPerCategory)
            .ToList();

        return (gainers, losers);
    }

    private static MarketInsights CreateEmptyInsights() => new(
        [], [], [], [], [], [],
        DateTime.UtcNow,
        0
    );

    // Source-generated logging
    [LoggerMessage(Level = LogLevel.Debug, Message = "Insights refresh skipped - calculation already in progress")]
    private partial void LogRefreshSkipped();

    [LoggerMessage(Level = LogLevel.Information,
        Message =
            "Market insights refreshed: {HotCount} hot, {SurgeCount} surges, {SpreadCount} spreads, {FireCount} fire sales, {MoverCount} movers")]
    private partial void LogInsightsRefreshed(int hotCount, int surgeCount, int spreadCount, int fireCount, int moverCount);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error refreshing market insights")]
    private partial void LogRefreshError(Exception ex);
}
