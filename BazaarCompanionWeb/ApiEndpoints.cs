using BazaarCompanionWeb.Charting;
using BazaarCompanionWeb.Configurations;
using BazaarCompanionWeb.Context;
using BazaarCompanionWeb.Dtos;
using BazaarCompanionWeb.Dtos.Bot;
using BazaarCompanionWeb.Entities;
using BazaarCompanionWeb.Interfaces.Database;
using BazaarCompanionWeb.Models;
using BazaarCompanionWeb.Services;
using BazaarCompanionWeb.Services.Ingestion;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BazaarCompanionWeb;

public static class ApiEndpoints
{
    private const double Unknown = FlipQuoting.Unknown;

    private static double Serialisable(double value) => FlipQuoting.Serialisable(value);

    public static void MapApiEndpoints(this WebApplication app)
    {
        MapChartEndpoints(app);
        MapBotEndpoints(app);
    }

    private static void MapChartEndpoints(WebApplication app)
    {
        // Chart data API: one page of candles + server-computed indicators (Lightweight Charts payload).
        // ?before=<unixMs> pages backward; omit for the most-recent page.
        app.MapGet("/api/chart/{productKey}/{interval:int}", async (
            string productKey,
            int interval,
            long? before,
            int? limit,
            ChartDataService chartData,
            CancellationToken ct) =>
        {
            var dataLimit = Math.Min(limit ?? 200, 500);
            var payload = await chartData.BuildProductPageAsync(productKey, (CandleInterval)interval, before, dataLimit, ct);
            return Results.Ok(payload);
        });

        // Index chart API for aggregated OHLC (ETF-like indices). Same payload, no ask line.
        app.MapGet("/api/chart/index/{slug}/{interval:int}", async (
            string slug,
            int interval,
            long? before,
            int? limit,
            ChartDataService chartData,
            IOptions<List<IndexConfiguration>> indexOptions,
            CancellationToken ct) =>
        {
            var index = indexOptions.Value.FirstOrDefault(i => i.Slug.Equals(slug, StringComparison.OrdinalIgnoreCase));
            if (index is null)
                return Results.NotFound(new { error = $"Index '{slug}' not found" });

            var dataLimit = Math.Min(limit ?? 200, 500);
            var payload = await chartData.BuildIndexPageAsync(slug, (CandleInterval)interval, before, dataLimit, ct);
            return Results.Ok(payload);
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
            double? maxFillMinutes,
            string? sort,
            double? budget,
            double? taxRate,
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

            // Pull a wider candidate pool than the caller asked for: the fill-time filter and the fill/
            // throughput sorts operate on data that only exists after the order books are deserialized, so
            // trimming to resultLimit by score first would hide exactly the tradable flips they select for.
            var candidatePool = Math.Min(Math.Max(resultLimit * 5, resultLimit), 250);
            var products = await query
                .OrderByDescending(p => p.Meta.FlipOpportunityScore)
                .Take(candidatePool)
                .ToListAsync(ct);

            var result = products.Select(p => FlipQuoting.Quote(p, FlipQuoting.TaxRateFor(taxRate), budget, maxFillMinutes)).ToList();

            // Fill time is a filter and a sort, not just a readout: a bot asking for flips wants the ones it
            // can actually complete. Ordering by score alone puts a 677%-spread product that trades twice a
            // day above a 26% one that turns over millions a week, which is backwards for anything that has
            // to hold inventory while it waits.
            if (maxFillMinutes is { } fillCeiling)
                result = result.Where(f => f.EstimatedRoundTripMinutes <= fillCeiling).ToList();

            // A flip the caller cannot afford a single unit of is not an opportunity for them.
            if (budget is not null)
                result = result.Where(f => f.SuggestedQuantity > 0).ToList();

            result = (sort?.ToLowerInvariant() switch
            {
                "fill" => result.OrderBy(f => f.EstimatedRoundTripMinutes),
                "profit" => result.OrderByDescending(f => f.EstimatedProfitPerUnit),
                // Profit per unit is worthless if the flip takes a day; profit per minute is the honest
                // ranking for a bot that can only hold one position at a time.
                "throughput" => result.OrderByDescending(f =>
                    f.EstimatedRoundTripMinutes is > 0 and < Unknown
                        ? f.EstimatedProfitPerUnit / f.EstimatedRoundTripMinutes
                        : 0),
                _ => result.OrderByDescending(f => f.OpportunityScore)
            }).Take(resultLimit).ToList();

            return Results.Ok(result);
        });

        // Full product detail with order books and price history
        app.MapGet("/api/bot/products/{productKey}", async (
            string productKey,
            IProductRepository productRepository,
            BazaarSnapshotStore snapshotStore,
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

            // This product's own observation, not the last poll's: a product that has dropped out of the
            // feed keeps the numbers it had, and their age has to say so.
            var observed = snapshotStore.ObservationOf(productKey);
            var requestUtc = DateTime.UtcNow;

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
                DataAgeSeconds: observed is null
                    ? Unknown
                    : Serialisable(Math.Max(0, (requestUtc - observed.UpstreamUtc).TotalSeconds)),
                ObservedUtc: observed?.ObservedUtc,
                UpstreamUtc: observed?.UpstreamUtc,
                RequestUtc: requestUtc,
                BidBook: product.BidBook ?? [],
                AskBook: product.AskBook ?? [],
                PriceHistory: product.PriceHistory ?? []
            );

            return Results.Ok(detail);
        });

        // Name -> key lookup, for callers that only ever see the display name.
        //
        // A bot reading Hypixel's own order menu has "Foxtrot Shard" and nothing else; every other endpoint
        // here is keyed by product key, so without this it cannot ask about a position it can plainly see.
        // The name it holds may also be word-inverted relative to the API's ("Shard Foxtrot"), which is why
        // the match is order-insensitive rather than an equality test.
        app.MapGet("/api/bot/products/lookup", async (
            string name,
            IProductRepository productRepository,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(name))
                return Results.BadRequest(new { error = "Query parameter 'name' is required" });

            var matches = await productRepository.FindProductsByNameAsync(name, ct);
            if (matches.Count == 0)
                return Results.NotFound(new { error = $"No product matches the name '{name}'" });

            return Results.Ok(matches
                .Take(10)
                .Select(m => new { productKey = m.ProductKey, name = m.Name })
                .ToList());
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
                // Units traded in the period (buy + sell); the split says which side was hitting.
                volume = c.Volume,
                buyVolume = c.BuyVolume,
                sellVolume = c.SellVolume,
                spread = c.Spread,
                askOpen = c.AskOpen,
                askHigh = c.AskHigh,
                askLow = c.AskLow,
                askClose = c.AskClose
            }).ToList();

            return Results.Ok(result);
        });

        // Market health score with trading recommendation
        app.MapGet("/api/bot/market/health", async (
            MarketAnalyticsService marketAnalyticsService,
            BazaarSnapshotStore snapshotStore,
            CancellationToken ct) =>
        {
            var metrics = await marketAnalyticsService.GetMarketMetricsAsync(ct);

            // Ingest freshness, measured at the poll rather than at the database flush: a bot needs to know
            // when this service stopped hearing from Hypixel, and the flush interval would otherwise show as
            // staleness that does not exist.
            var dataAgeSeconds = snapshotStore.LastIngestUtc == DateTime.MinValue
                ? Unknown
                : Serialisable(Math.Max(0, (DateTime.UtcNow - snapshotStore.LastIngestUtc).TotalSeconds));

            var (recommendation, reason) = metrics.MarketHealthScore switch
            {
                >= 75 => ("Aggressive", "Market conditions are favorable with wide spreads and low manipulation"),
                >= 50 => ("Normal", "Standard market conditions"),
                >= 25 => ("Conservative", "Elevated manipulation or thin order books detected"),
                _ => ("HaltTrading", "Market is unhealthy, trading is not recommended")
            };

            var result = new BotMarketHealth(
                BazaarTaxRate: FlipQuoting.PerklessTaxRate,
                DataAgeSeconds: dataAgeSeconds,
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
