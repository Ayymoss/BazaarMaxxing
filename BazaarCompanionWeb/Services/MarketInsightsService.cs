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

            var products = await context.Products
                .Include(p => p.Bid)
                .Include(p => p.Ask)
                .Include(p => p.Meta)
                .AsNoTracking()
                .Where(p => p.Bid.OrderVolumeWeek >= MinimumWeeklyVolumeForInsights)
                .ToListAsync(ct);

            var productKeys = products.Select(p => p.ProductKey).ToList();
            var candles15m = await ohlcRepository.GetCandlesBulkAsync(productKeys, CandleInterval.FifteenMinute, 2, ct);
            var candles1h = await ohlcRepository.GetCandlesBulkAsync(productKeys, CandleInterval.OneHour, 168, ct);

            var hotProducts = CalculateHotProducts(products, candles15m, now, _previousHotProductKeys);
            var volumeSurges = CalculateVolumeSurges(products, candles1h, now);
            var spreadOpportunities = CalculateSpreadOpportunities(products, candles1h, now);
            var fireSales = CalculateFireSales(products, candles1h, now);
            var (gainers, losers) = CalculateMarketMovers(products, candles1h, now);

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
    private static List<HotProductInsight> CalculateHotProducts(
        List<EFProduct> products,
        IReadOnlyDictionary<string, List<OhlcDataPoint>> candles15m,
        DateTime now,
        HashSet<string> previousHotProductKeys)
    {
        List<HotProductInsight> results = [];

        foreach (var product in products)
        {
            if (!candles15m.TryGetValue(product.ProductKey, out var candles) || candles.Count < 2) continue;

            var ordered = candles.OrderBy(c => c.Time).ToList();
            var price15MinAgo = ordered[^2].Close;
            var currentPrice = ordered[^1].Close;

            if (price15MinAgo <= 0) continue;

            var changePercent = (currentPrice - price15MinAgo) / price15MinAgo;
            var absChange = Math.Abs(changePercent);

            if (absChange < HotProductThreshold) continue;

            var isNew = !previousHotProductKeys.Contains(product.ProductKey);

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
    private static List<VolumeSurgeInsight> CalculateVolumeSurges(
        List<EFProduct> products,
        IReadOnlyDictionary<string, List<OhlcDataPoint>> candles1h,
        DateTime now)
    {
        List<VolumeSurgeInsight> results = [];

        foreach (var product in products)
        {
            if (!candles1h.TryGetValue(product.ProductKey, out var candles) || candles.Count < 2) continue;

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
    private static List<SpreadOpportunityInsight> CalculateSpreadOpportunities(
        List<EFProduct> products,
        IReadOnlyDictionary<string, List<OhlcDataPoint>> candles1h,
        DateTime now)
    {
        List<SpreadOpportunityInsight> results = [];

        foreach (var product in products)
        {
            var currentSpread = product.Ask.UnitPrice - product.Bid.UnitPrice;
            if (currentSpread <= 0) continue;

            if (!candles1h.TryGetValue(product.ProductKey, out var candles) || candles.Count < 24) continue;

            var historicalSpreads = candles
                .Where(c => c.Spread > 0)
                .Select(c => c.Spread)
                .ToList();

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
    private static List<FireSaleInsight> CalculateFireSales(
        List<EFProduct> products,
        IReadOnlyDictionary<string, List<OhlcDataPoint>> candles1h,
        DateTime now)
    {
        const double priceDeviation24hThreshold = 0.20;
        const double priceBelow7dLowThreshold = 0.10;
        const double volumeSpikeThreshold = 1.50;

        List<FireSaleInsight> results = [];

        foreach (var product in products)
        {
            if (!candles1h.TryGetValue(product.ProductKey, out var candles) || candles.Count < 24) continue;

            var ordered = candles.OrderBy(c => c.Time).ToList();
            var currentAskPrice = product.Ask.UnitPrice;
            if (currentAskPrice <= 0) continue;

            var last24hCandles = ordered.TakeLast(24).ToList();
            var avg24hPrice = last24hCandles.Average(c => c.Close);
            if (avg24hPrice <= 0) continue;

            var low7dPrice = ordered.Min(c => c.Low);
            if (low7dPrice <= 0) continue;

            var currentHourCandle = ordered.LastOrDefault();
            var historicalCandles = ordered.SkipLast(1).ToList();
            var avgHourlyVolume = historicalCandles
                .Where(c => c.Volume > 0)
                .Select(c => c.Volume)
                .DefaultIfEmpty(1)
                .Average();

            var currentVolume = currentHourCandle?.Volume ?? 0;

            var priceBelow24hAvg = currentAskPrice < avg24hPrice * (1 - priceDeviation24hThreshold);
            var priceBelow7dLow = currentAskPrice < low7dPrice * (1 - priceBelow7dLowThreshold);
            var volumeSpike = avgHourlyVolume > 0 && currentVolume > avgHourlyVolume * volumeSpikeThreshold;

            if (!priceBelow24hAvg || !priceBelow7dLow || !volumeSpike) continue;

            var deviationPercent = ((currentAskPrice - avg24hPrice) / avg24hPrice) * 100;
            var intensityScore = Math.Min(1.0, Math.Abs(deviationPercent) / 50);

            results.Add(new FireSaleInsight(
                product.ProductKey,
                product.FriendlyName,
                product.Tier.ToString(),
                now,
                intensityScore,
                deviationPercent,
                currentAskPrice,
                avg24hPrice
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
    private static (List<MarketMoverInsight> Gainers, List<MarketMoverInsight> Losers) CalculateMarketMovers(
        List<EFProduct> products,
        IReadOnlyDictionary<string, List<OhlcDataPoint>> candles1h,
        DateTime now)
    {
        List<MarketMoverInsight> allMovers = [];

        foreach (var product in products)
        {
            if (!candles1h.TryGetValue(product.ProductKey, out var candles) || candles.Count < 24) continue;

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
