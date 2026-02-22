using BazaarCompanionWeb.Configurations;
using BazaarCompanionWeb.Context;
using BazaarCompanionWeb.Dtos;
using BazaarCompanionWeb.Dtos.Bot;
using BazaarCompanionWeb.Entities;
using BazaarCompanionWeb.Interfaces.Database;
using BazaarCompanionWeb.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BazaarCompanionWeb;

public static class ApiEndpoints
{
    private const double BazaarTaxRate = 0.01125;

    public static void MapApiEndpoints(this WebApplication app)
    {
        MapChartEndpoints(app);
        MapBotEndpoints(app);
    }

    private static void MapChartEndpoints(WebApplication app)
    {
        // Chart data API for lazy loading historical candles
        app.MapGet("/api/chart/{productKey}/{interval:int}", async (
            string productKey,
            int interval,
            long? before,
            int? limit,
            IOhlcRepository ohlcRepository,
            CancellationToken ct) =>
        {
            var candleInterval = (CandleInterval)interval;
            var dataLimit = Math.Min(limit ?? 200, 500); // Cap at 500 per request

            List<OhlcDataPoint> candles;
            if (before.HasValue)
            {
                // Load historical data before the specified timestamp
                var beforeTime = DateTimeOffset.FromUnixTimeMilliseconds(before.Value).UtcDateTime;
                candles = await ohlcRepository.GetCandlesBeforeAsync(productKey, candleInterval, beforeTime, dataLimit, ct);
            }
            else
            {
                // Initial load - get most recent candles
                candles = await ohlcRepository.GetCandlesAsync(productKey, candleInterval, dataLimit, ct);
            }

            // Return in KLineChart format (timestamp in milliseconds)
            var result = candles.Select(c => new
            {
                timestamp = new DateTimeOffset(c.Time).ToUnixTimeMilliseconds(),
                open = c.Open,
                high = c.High,
                low = c.Low,
                close = c.Close,
                volume = c.Volume,
                askClose = c.AskClose
            }).ToList();

            return Results.Ok(result);
        });

        // Index chart API for aggregated OHLC data (ETF-like indices)
        // Supports lazy loading: pass ?before=<unixMs> for historical data before that timestamp
        app.MapGet("/api/chart/index/{slug}/{interval:int}", async (
            string slug,
            int interval,
            long? before,
            int? limit,
            IndexAggregationService indexService,
            IOptions<List<IndexConfiguration>> indexOptions,
            CancellationToken ct) =>
        {
            var indices = indexOptions.Value;
            var index = indices.FirstOrDefault(i => i.Slug.Equals(slug, StringComparison.OrdinalIgnoreCase));
            if (index is null)
            {
                return Results.NotFound(new { error = $"Index '{slug}' not found" });
            }

            var candleInterval = (CandleInterval)interval;
            var dataLimit = Math.Min(limit ?? 200, 500);

            List<OhlcDataPoint> candles;
            if (before.HasValue)
            {
                var beforeTime = DateTimeOffset.FromUnixTimeMilliseconds(before.Value).UtcDateTime;
                candles = await indexService.GetAggregatedCandlesBeforeAsync(slug, candleInterval, beforeTime, dataLimit, ct);
            }
            else
            {
                candles = await indexService.GetAggregatedCandlesAsync(slug, candleInterval, dataLimit, ct);
            }

            var result = candles.Select(c => new
            {
                timestamp = new DateTimeOffset(c.Time).ToUnixTimeMilliseconds(),
                open = c.Open,
                high = c.High,
                low = c.Low,
                close = c.Close,
                volume = c.Volume,
                askClose = c.AskClose
            }).ToList();

            return Results.Ok(result);
        });
    }

    private static void MapBotEndpoints(WebApplication app)
    {
        // Top flip opportunities sorted by opportunity score
        app.MapGet("/api/bot/flips", async (
            double? minPrice,
            double? maxPrice,
            double? minVolume,
            double? minAskVolume,
            bool? excludeManipulated,
            double? minScore,
            int? maxResults,
            IDbContextFactory<DataContext> contextFactory,
            CancellationToken ct) =>
        {
            var filterManipulated = excludeManipulated ?? true;
            var scoreThreshold = minScore ?? 3.0;
            var resultLimit = Math.Clamp(maxResults ?? 20, 1, 100);
            var askVolumeFloor = minAskVolume ?? 25_000;

            await using var context = await contextFactory.CreateDbContextAsync(ct);

            var query = context.Products
                .Include(p => p.Bid)
                .Include(p => p.Ask)
                .Include(p => p.Meta)
                .AsNoTracking()
                // Hard safety filters
                .Where(p => p.Bid.UnitPrice > 0 && p.Ask.UnitPrice > 0)
                .Where(p => p.Ask.OrderVolumeWeek >= askVolumeFloor)
                .Where(p => p.Bid.UnitPrice >= 100) // Min bid price for practical flipping
                .Where(p => (p.Ask.UnitPrice - p.Bid.UnitPrice) >= 100) // Min 100 coin spread
                .Where(p => p.Ask.OrderVolumeWeek >= 0.30 * (p.Ask.OrderVolumeWeek + p.Bid.OrderVolumeWeek)) // Min 30% ask ratio
                .Where(p => p.Meta.FlipOpportunityScore >= scoreThreshold);

            if (filterManipulated)
                query = query.Where(p => !p.Meta.IsManipulated);

            if (minPrice.HasValue)
                query = query.Where(p => p.Bid.UnitPrice >= minPrice.Value);

            if (maxPrice.HasValue)
                query = query.Where(p => p.Bid.UnitPrice <= maxPrice.Value);

            if (minVolume.HasValue)
                query = query.Where(p => p.Meta.TotalWeekVolume >= minVolume.Value);

            var products = await query
                .OrderByDescending(p => p.Meta.FlipOpportunityScore)
                .Take(resultLimit)
                .ToListAsync(ct);

            var result = products.Select(p => new FlipOpportunity(
                ProductKey: p.ProductKey,
                Name: p.FriendlyName,
                Tier: p.Tier,
                Unstackable: p.Unstackable,
                BestBidPrice: p.Bid.UnitPrice,
                BidOrders: p.Bid.OrderCount,
                BidVolume: p.Bid.OrderVolume,
                BidWeekVolume: p.Bid.OrderVolumeWeek,
                BestAskPrice: p.Ask.UnitPrice,
                AskOrders: p.Ask.OrderCount,
                AskVolume: p.Ask.OrderVolume,
                AskWeekVolume: p.Ask.OrderVolumeWeek,
                Spread: p.Meta.Spread,
                SpreadPercent: p.Bid.UnitPrice > 0 ? p.Meta.Spread / p.Bid.UnitPrice * 100 : 0,
                ProfitMultiplier: p.Meta.ProfitMultiplier,
                OpportunityScore: p.Meta.FlipOpportunityScore,
                EstimatedProfitPerUnit: (p.Ask.UnitPrice * (1 - BazaarTaxRate)) - p.Bid.UnitPrice,
                IsManipulated: p.Meta.IsManipulated,
                ManipulationIntensity: p.Meta.ManipulationIntensity,
                PriceDeviationPercent: p.Meta.PriceDeviationPercent
            )).ToList();

            return Results.Ok(result);
        });

        // Full product detail with order books and price history
        app.MapGet("/api/bot/products/{productKey}", async (
            string productKey,
            IProductRepository productRepository,
            CancellationToken ct) =>
        {
            ProductDataInfo product;
            try
            {
                product = await productRepository.GetProductAsync(productKey, ct);
            }
            catch (InvalidOperationException)
            {
                return Results.NotFound(new { error = $"Product '{productKey}' not found" });
            }

            var detail = new BotProductDetail(
                ProductKey: product.ItemId,
                Name: product.ItemFriendlyName,
                Tier: product.ItemTier,
                Unstackable: product.ItemUnstackable,
                BidPrice: product.BidUnitPrice,
                AskPrice: product.AskUnitPrice,
                Spread: product.OrderMetaSpread,
                BidWeekVolume: product.BidWeekVolume,
                AskWeekVolume: product.AskWeekVolume,
                TotalWeekVolume: product.OrderMetaTotalWeekVolume,
                BidOrders: product.BidCurrentOrders,
                AskOrders: product.AskCurrentOrders,
                BidVolume: product.BidCurrentVolume,
                AskVolume: product.AskCurrentVolume,
                OpportunityScore: product.OrderMetaFlipOpportunityScore,
                ProfitMultiplier: product.OrderMetaPotentialProfitMultiplier,
                IsManipulated: product.IsManipulated,
                ManipulationIntensity: product.ManipulationIntensity,
                PriceDeviationPercent: product.PriceDeviationPercent,
                BidBook: product.BidBook ?? [],
                AskBook: product.AskBook ?? [],
                PriceHistory: product.PriceHistory ?? []
            );

            return Results.Ok(detail);
        });

        // Batch product lookup (lightweight, no order books/history)
        app.MapGet("/api/bot/products/batch", async (
            string keys,
            IProductRepository productRepository,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(keys))
                return Results.BadRequest(new { error = "Query parameter 'keys' is required" });

            var productKeys = keys.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (productKeys.Length > 50)
                return Results.BadRequest(new { error = "Maximum 50 keys per request" });

            var products = await productRepository.GetProductsByKeysAsync(productKeys, ct);

            var result = products.Select(p => new BotProductSummary(
                ProductKey: p.ItemId,
                Name: p.ItemFriendlyName,
                BidPrice: p.BidUnitPrice,
                AskPrice: p.AskUnitPrice,
                Spread: p.OrderMetaSpread,
                OpportunityScore: p.OrderMetaFlipOpportunityScore,
                IsManipulated: p.IsManipulated,
                BidWeekVolume: p.BidWeekVolume,
                AskWeekVolume: p.AskWeekVolume
            )).ToList();

            return Results.Ok(result);
        });

        // Deep order book analysis (whales, walls, support/resistance)
        app.MapGet("/api/bot/products/{productKey}/orderbook", async (
            string productKey,
            OrderBookAnalysisService orderBookAnalysisService,
            CancellationToken ct) =>
        {
            var analysis = await orderBookAnalysisService.AnalyzeAsync(productKey, ct);
            if (analysis is null)
                return Results.NotFound(new { error = $"No order book data for '{productKey}'" });

            return Results.Ok(analysis);
        });

        // OHLC candle history
        app.MapGet("/api/bot/products/{productKey}/candles", async (
            string productKey,
            int? interval,
            int? limit,
            long? before,
            IOhlcRepository ohlcRepository,
            CancellationToken ct) =>
        {
            var candleInterval = (CandleInterval)(interval ?? 60);
            var dataLimit = Math.Clamp(limit ?? 100, 1, 500);

            List<OhlcDataPoint> candles;
            if (before.HasValue)
            {
                var beforeTime = DateTimeOffset.FromUnixTimeMilliseconds(before.Value).UtcDateTime;
                candles = await ohlcRepository.GetCandlesBeforeAsync(productKey, candleInterval, beforeTime, dataLimit, ct);
            }
            else
            {
                candles = await ohlcRepository.GetCandlesAsync(productKey, candleInterval, dataLimit, ct);
            }

            var result = candles.Select(c => new
            {
                timestamp = new DateTimeOffset(c.Time).ToUnixTimeMilliseconds(),
                open = c.Open,
                high = c.High,
                low = c.Low,
                close = c.Close,
                volume = c.Volume,
                spread = c.Spread,
                askClose = c.AskClose
            }).ToList();

            return Results.Ok(result);
        });

        // Market health score with trading recommendation
        app.MapGet("/api/bot/market/health", async (
            MarketAnalyticsService marketAnalyticsService,
            CancellationToken ct) =>
        {
            var metrics = await marketAnalyticsService.GetMarketMetricsAsync(ct);

            var (recommendation, reason) = metrics.MarketHealthScore switch
            {
                >= 75 => ("Aggressive", "Market conditions are favorable with wide spreads and low manipulation"),
                >= 50 => ("Normal", "Standard market conditions"),
                >= 25 => ("Conservative", "Elevated manipulation or thin order books detected"),
                _ => ("HaltTrading", "Market is unhealthy, trading is not recommended")
            };

            var result = new BotMarketHealth(
                HealthScore: metrics.MarketHealthScore,
                AverageSpread: metrics.AverageSpread,
                ManipulationIndex: metrics.MarketManipulationIndex,
                ActiveProductsCount: metrics.ActiveProductsCount,
                TotalMarketCap: metrics.TotalMarketCapitalization,
                Volume24h: metrics.VolumeTrends.Volume24h,
                Volume7d: metrics.VolumeTrends.Volume7d,
                Recommendation: recommendation,
                RecommendationReason: reason
            );

            return Results.Ok(result);
        });

        // Market insights (hot products, volume surges, fire sales, movers)
        app.MapGet("/api/bot/market/insights", async (
            MarketInsightsService marketInsightsService,
            CancellationToken ct) =>
        {
            var insights = await marketInsightsService.GetInsightsAsync(ct);
            return Results.Ok(insights);
        });
    }
}
