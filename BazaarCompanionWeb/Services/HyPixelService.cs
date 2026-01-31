using BazaarCompanionWeb.Dtos;
using BazaarCompanionWeb.Hubs;
using BazaarCompanionWeb.Interfaces;
using Microsoft.AspNetCore.SignalR;
using BazaarCompanionWeb.Interfaces.Api;
using BazaarCompanionWeb.Interfaces.Database;
using BazaarCompanionWeb.Models;
using BazaarCompanionWeb.Models.Api.Bazaar;
using BazaarCompanionWeb.Models.Api.Items;
using BazaarCompanionWeb.Utilities;
using Humanizer;
using Item = BazaarCompanionWeb.Models.Item;

namespace BazaarCompanionWeb.Services;

public class HyPixelService(
    IHyPixelApi hyPixelApi,
    IProductRepository productRepository,
    IOhlcRepository ohlcRepository,
    IOpportunityScoringService opportunityScoringService,
    MarketInsightsService marketInsightsService,
    OrderBookAnalysisService orderBookAnalysisService,
    IHubContext<ProductHub> hubContext,
    TimeCache timeCache,
    LiveCandleTracker liveCandleTracker)
{
    public async Task FetchDataAsync(CancellationToken cancellationToken)
    {
        var bazaarResponse = await hyPixelApi.GetBazaarAsync();
        var itemResponse = await hyPixelApi.GetItemsAsync();
        var products = await BuildProductDataAsync(bazaarResponse, itemResponse, cancellationToken);
        var mappedProducts = products.Select(x => x.Map()).ToList();
        await productRepository.UpdateOrAddProductsAsync(mappedProducts, cancellationToken);

        // Record granular price ticks for OHLC chart with volume
        var ticks = products.Select(p => (
            p.ItemId,
            p.Bid.OrderPrice,
            p.Ask.OrderPrice,
            (long)p.Bid.CurrentVolume,
            (long)p.Ask.CurrentVolume
        ));
        await ohlcRepository.RecordTicksAsync(ticks, cancellationToken);

        timeCache.LastUpdated = TimeProvider.System.GetLocalNow();

        // Trigger market insights recalculation after data is updated
        // Trigger market insights recalculation after data is updated
        await marketInsightsService.RefreshInsightsAsync(cancellationToken);

        // Broadcast updates via SignalR
        foreach (var (product, efProduct) in products.Zip(mappedProducts))
        {
            var updateInfo = new ProductDataInfo
            {
                ItemId = efProduct.ProductKey,
                ItemFriendlyName = efProduct.FriendlyName,
                ItemTier = efProduct.Tier,
                ItemUnstackable = efProduct.Unstackable,
                SkinUrl = efProduct.SkinUrl,
                BidUnitPrice = efProduct.Bid.UnitPrice,
                BidWeekVolume = efProduct.Bid.OrderVolumeWeek,
                BidCurrentOrders = efProduct.Bid.OrderCount,
                BidCurrentVolume = efProduct.Bid.OrderVolume,
                AskUnitPrice = efProduct.Ask.UnitPrice,
                AskWeekVolume = efProduct.Ask.OrderVolumeWeek,
                AskCurrentOrders = efProduct.Ask.OrderCount,
                AskCurrentVolume = efProduct.Ask.OrderVolume,
                OrderMetaPotentialProfitMultiplier = efProduct.Meta.ProfitMultiplier,
                OrderMetaSpread = efProduct.Meta.Spread,
                OrderMetaTotalWeekVolume = efProduct.Meta.TotalWeekVolume,
                OrderMetaFlipOpportunityScore = efProduct.Meta.FlipOpportunityScore,
                IsManipulated = efProduct.Meta.IsManipulated,
                ManipulationIntensity = efProduct.Meta.ManipulationIntensity,
                PriceDeviationPercent = efProduct.Meta.PriceDeviationPercent,
                // These are expensive to fetch and not strictly needed for the quick update, 
                // but we should at least clear them or preserve if needed.
                BidMarketDataId = efProduct.Bid.Id,
                AskMarketDataId = efProduct.Ask.Id,
                BidBook = product.Bid.OrderBook.Select(x => new Order(x.UnitPrice, x.Amount, x.Orders)).ToList(),
                AskBook = product.Ask.OrderBook.Select(x => new Order(x.UnitPrice, x.Amount, x.Orders)).ToList()
            };

            await hubContext.Clients.Group(efProduct.ProductKey).SendAsync("ProductUpdated", updateInfo, cancellationToken);

            // Broadcast a tick for the chart with proper OHLC aggregation
            var liveTick = liveCandleTracker.UpdateAndGetTick(
                efProduct.ProductKey,
                efProduct.Bid.UnitPrice,
                efProduct.Ask.UnitPrice,
                efProduct.Bid.OrderVolume + efProduct.Ask.OrderVolume);

            await hubContext.Clients.Group(efProduct.ProductKey).SendAsync("TickUpdated", liveTick, cancellationToken);
            
            // Store order book snapshot for heatmap analysis
            await orderBookAnalysisService.StoreSnapshotAsync(
                efProduct.ProductKey,
                updateInfo.BidBook!,
                updateInfo.AskBook!,
                cancellationToken);
        }
    }

    private async Task<IEnumerable<ProductData>> BuildProductDataAsync(BazaarResponse bazaarResponse, ItemResponse itemResponse,
        CancellationToken cancellationToken)
    {
        var itemMap = itemResponse.Items.ToDictionary(x => x.Id, x => x);

        var productList = new List<ProductData>();

        foreach (var bazaar in bazaarResponse.Products.Values.Where(x => x.Bids.Count is not 0))
        {
            var item = itemMap.GetValueOrDefault(bazaar.ProductId);

            var ask = bazaar.Asks.FirstOrDefault();
            var bid = bazaar.Bids.FirstOrDefault();

            var askOrderPrice = ask?.PricePerUnit ?? bid?.PricePerUnit + 0.1 ?? 0.1f;
            var bidOrderPrice = bid?.PricePerUnit ?? ask?.PricePerUnit - 0.1 ?? 0.1f;
            var movingWeekSells = bazaar.Ticker.MovingWeekSells;
            var movingWeekBuys = bazaar.Ticker.MovingWeekBuys;

            var spread = askOrderPrice - bidOrderPrice;
            var totalWeekVolume = movingWeekSells + movingWeekBuys;
            var potentialProfitMultiplier = askOrderPrice / bidOrderPrice;
            var buyingPower = (float)movingWeekSells / movingWeekBuys;

            var friendlyName = item?.Name ?? ProductIdToName(bazaar.ProductId);

            // Calculate opportunity score using the new service
            var flipOpportunityScore = await opportunityScoringService.CalculateOpportunityScoreAsync(
                bazaar.ProductId,
                askOrderPrice,
                bidOrderPrice,
                movingWeekSells,
                movingWeekBuys,
                cancellationToken);

            // Calculate manipulation score to detect fire sale opportunities
            var manipulationScore = await opportunityScoringService.CalculateManipulationScoreAsync(
                bazaar.ProductId,
                askOrderPrice,
                bidOrderPrice,
                cancellationToken);

            string? skinUrl = null;
            if (item?.Skin?.Value is not null)
            {
                try
                {
                    var json = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(item.Skin.Value));
                    using var doc = System.Text.Json.JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("textures", out var textures) &&
                        textures.TryGetProperty("SKIN", out var skin) &&
                        skin.TryGetProperty("url", out var url))
                    {
                        skinUrl = url.GetString();
                    }
                }
                catch
                {
                    // Ignore skin parse errors
                }
            }

            productList.Add(new ProductData
            {
                ItemId = bazaar.ProductId,
                Item = new Item
                {
                    FriendlyName = friendlyName,
                    Tier = item?.Tier ?? ItemTier.Common,
                    Unstackable = item?.Unstackable ?? false,
                    SkinUrl = skinUrl
                },
                Bid = new OrderInfo
                {
                    Last = bazaar.Ticker.BestBidPrice,
                    OrderPrice = bidOrderPrice,
                    WeekVolume = movingWeekSells,
                    CurrentOrders = bazaar.Ticker.ActiveBidOrders,
                    CurrentVolume = bazaar.Ticker.TotalBidVolume,
                    OrderBook = bazaar.Bids.Select(x => new OrderBook
                    {
                        UnitPrice = x.PricePerUnit,
                        Orders = x.OrderCount,
                        Amount = x.Amount
                    })
                },
                Ask = new OrderInfo
                {
                    Last = bazaar.Ticker.BestAskPrice,
                    OrderPrice = askOrderPrice,
                    WeekVolume = movingWeekBuys,
                    CurrentOrders = bazaar.Ticker.ActiveAskOrders,
                    CurrentVolume = bazaar.Ticker.TotalAskVolume,
                    OrderBook = bazaar.Asks.Select(x => new OrderBook
                    {
                        UnitPrice = x.PricePerUnit,
                        Orders = x.OrderCount,
                        Amount = x.Amount
                    })
                },
                OrderMeta = new OrderMeta
                {
                    PotentialProfitMultiplier = potentialProfitMultiplier,
                    Spread = spread,
                    TotalWeekVolume = totalWeekVolume,
                    BidOrderPower = buyingPower,
                    FlipOpportunityScore = flipOpportunityScore,
                    IsManipulated = manipulationScore.IsManipulated,
                    ManipulationIntensity = manipulationScore.ManipulationIntensity,
                    PriceDeviationPercent = manipulationScore.DeviationPercent
                }
            });
        }

        return productList;
    }

    private static string ProductIdToName(string productId)
    {
        var split = productId.Split("_");
        if (!int.TryParse(split.Last(), out var level)) return productId.Humanize().Titleize();

        var nameParts = split.Take(split.Length - 1).ToArray();
        var baseName = string.Join(" ", nameParts).Humanize().Titleize();
        return $"{baseName} {level.ToRoman()}";
    }
}
