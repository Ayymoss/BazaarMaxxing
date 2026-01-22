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
    private const double HotProductThreshold = 5.0;         // 5% change in 15 min
    private const double VolumeSurgeThreshold = 2.0;        // 2x average volume
    private const double SpreadWideningThreshold = 20.0;    // 20% spread increase
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
            var fireSales = await CalculateFireSalesAsync(context, now, ct);
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
            .Include(p => p.Buy)
            .AsNoTracking()
            .Where(p => p.Buy.OrderVolumeWeek > 0)
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

            var changePercent = ((currentPrice - price15MinAgo) / price15MinAgo) * 100;
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
            .Include(p => p.Buy)
            .Include(p => p.Sell)
            .AsNoTracking()
            .Where(p => p.Buy.OrderVolumeWeek > 0)
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
            .Include(p => p.Buy)
            .Include(p => p.Sell)
            .Include(p => p.Meta)
            .AsNoTracking()
            .Where(p => p.Buy.OrderVolumeWeek > 0)
            .ToListAsync(ct);

        foreach (var product in products)
        {
            var currentSpread = product.Buy.UnitPrice - product.Sell.UnitPrice;
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
    /// Calculates fire sale alerts using existing manipulation detection.
    /// </summary>
    private async Task<List<FireSaleInsight>> CalculateFireSalesAsync(
        DataContext context,
        DateTime now,
        CancellationToken ct)
    {
        var manipulatedProducts = await context.Products
            .Include(p => p.Buy)
            .Include(p => p.Sell)
            .Include(p => p.Meta)
            .AsNoTracking()
            .Where(p => p.Meta.IsManipulated && p.Meta.ManipulationIntensity > 0)
            .OrderByDescending(p => p.Meta.ManipulationIntensity)
            .Take(MaxInsightsPerCategory)
            .ToListAsync(ct);

        return manipulatedProducts.Select(p =>
        {
            var currentPrice = p.Sell.UnitPrice;
            var deviationPercent = p.Meta.PriceDeviationPercent;
            var estimatedFairPrice = deviationPercent != 0
                ? currentPrice / (1 + (deviationPercent / 100))
                : currentPrice;

            return new FireSaleInsight(
                p.ProductKey,
                p.FriendlyName,
                p.Tier.ToString(),
                now,
                p.Meta.ManipulationIntensity,
                deviationPercent,
                currentPrice,
                estimatedFairPrice
            );
        }).ToList();
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
            .Include(p => p.Buy)
            .Include(p => p.Sell)
            .AsNoTracking()
            .Where(p => p.Buy.OrderVolumeWeek > 0)
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

            var changePercent = ((currentPrice - price24hAgo) / price24hAgo) * 100;
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

    [LoggerMessage(Level = LogLevel.Information, Message = "Market insights refreshed: {HotCount} hot, {SurgeCount} surges, {SpreadCount} spreads, {FireCount} fire sales, {MoverCount} movers")]
    private partial void LogInsightsRefreshed(int hotCount, int surgeCount, int spreadCount, int fireCount, int moverCount);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error refreshing market insights")]
    private partial void LogRefreshError(Exception ex);
}
