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
    private const double BazaarTaxRate = 0.01125;
    private const double MinutesPerWeek = 7 * 24 * 60;

    /// <summary>
    /// Weekly volume divided by minutes overstates how fast a queue actually drains: the volume is spread
    /// across every price level and every hour, while an order only ever consumes flow arriving at ITS level
    /// during the hours it is posted. Measured against a live order on ENCHANTED_SLIME_BALL (9.3M/week, so a
    /// naive 923 units/min) the observed rate was ~170 units/min — a fifth. Estimates are scaled by that
    /// rather than published as-is, because a bot deciding what it can finish deserves the pessimistic number.
    /// </summary>
    private const double QueueDrainFactor = 0.2;

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

            // Depth at the best price on each side, read from the stored book. This is the queue a bot joins
            // when it posts at the top — and the reason a fat spread can still be untradable: 11,000 units
            // parked at the best bid is an hour of waiting, during which anyone can undercut by 0.1 and reset
            // the wait entirely.
            static int TopDepth(IReadOnlyList<OrderBook> book) => book.Count > 0 ? book[0].Amount : 0;
            // How many units this budget can buy, bounded by the book rather than by the purse alone. Taking
            // more than a slice of the top level means the sell side has to absorb an order larger than the
            // depth that was there when the decision was made — and Hypixel caps a single order anyway.
            const int hypixelMaxOrderUnits = 71_680;
            static int SuggestedUnits(double? budgetCoins, double bidPrice, int topAskDepth)
            {
                if (budgetCoins is not { } coins || bidPrice <= 0) return 0;
                var affordable = (int)Math.Floor(coins / bidPrice);
                var bookLimit = topAskDepth > 0 ? Math.Max(1, topAskDepth / 4) : affordable;
                return Math.Clamp(Math.Min(affordable, bookLimit), 0, hypixelMaxOrderUnits);
            }

            static double FillMinutes(int depth, double weekVolume) =>
                weekVolume <= 0
                    ? double.PositiveInfinity
                    : depth / (weekVolume / MinutesPerWeek * QueueDrainFactor);

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
                TopBidDepth: TopDepth(p.Bid.Books),
                TopAskDepth: TopDepth(p.Ask.Books),
                EstimatedBuyFillMinutes: FillMinutes(TopDepth(p.Bid.Books), p.Bid.OrderVolumeWeek),
                EstimatedSellFillMinutes: FillMinutes(TopDepth(p.Ask.Books), p.Ask.OrderVolumeWeek),
                EstimatedRoundTripMinutes: FillMinutes(TopDepth(p.Bid.Books), p.Bid.OrderVolumeWeek)
                                           + FillMinutes(TopDepth(p.Ask.Books), p.Ask.OrderVolumeWeek),
                SuggestedQuantity: SuggestedUnits(budget, p.Bid.UnitPrice, TopDepth(p.Ask.Books)),
                SuggestedCost: SuggestedUnits(budget, p.Bid.UnitPrice, TopDepth(p.Ask.Books)) * p.Bid.UnitPrice,
                SuggestedProfit: SuggestedUnits(budget, p.Bid.UnitPrice, TopDepth(p.Ask.Books))
                                 * ((p.Ask.UnitPrice * (1 - BazaarTaxRate)) - p.Bid.UnitPrice),
                // Honest about what it is: these rows come from the database, which lags the live snapshot by
                // the flush interval. Screening on them is fine; pricing an order is not.
                DataAgeSeconds: Math.Max(0, (DateTime.UtcNow - p.LastSeenAt).TotalSeconds),
                IsManipulated: p.Meta.IsManipulated,
                ManipulationIntensity: p.Meta.ManipulationIntensity,
                PriceDeviationPercent: p.Meta.PriceDeviationPercent
            )).ToList();

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
                    f.EstimatedRoundTripMinutes > 0 && !double.IsInfinity(f.EstimatedRoundTripMinutes)
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
                // This endpoint serves the in-memory snapshot, so its age is the age of the last poll — not
                // of the database row, which lags behind by however long the flush interval is.
                DataAgeSeconds: snapshotStore.LastIngestUtc == DateTime.MinValue
                    ? double.PositiveInfinity
                    : Math.Max(0, (DateTime.UtcNow - snapshotStore.LastIngestUtc).TotalSeconds),
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
            BazaarSnapshotStore snapshotStore,
            CancellationToken ct) =>
        {
            var metrics = await marketAnalyticsService.GetMarketMetricsAsync(ct);

            // Ingest freshness, measured at the poll rather than at the database flush: a bot needs to know
            // when this service stopped hearing from Hypixel, and the flush interval would otherwise show as
            // staleness that does not exist.
            var dataAgeSeconds = snapshotStore.LastIngestUtc == DateTime.MinValue
                ? double.PositiveInfinity
                : Math.Max(0, (DateTime.UtcNow - snapshotStore.LastIngestUtc).TotalSeconds);

            var (recommendation, reason) = metrics.MarketHealthScore switch
            {
                >= 75 => ("Aggressive", "Market conditions are favorable with wide spreads and low manipulation"),
                >= 50 => ("Normal", "Standard market conditions"),
                >= 25 => ("Conservative", "Elevated manipulation or thin order books detected"),
                _ => ("HaltTrading", "Market is unhealthy, trading is not recommended")
            };

            var result = new BotMarketHealth(
                BazaarTaxRate: BazaarTaxRate,
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
