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
            p.Buy.OrderPrice, 
            p.Sell.OrderPrice,
            (long)p.Buy.CurrentVolume,
            (long)p.Sell.CurrentVolume
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
                BuyOrderUnitPrice = efProduct.Buy.UnitPrice,
                BuyOrderWeekVolume = efProduct.Buy.OrderVolumeWeek,
                BuyOrderCurrentOrders = efProduct.Buy.OrderCount,
                BuyOrderCurrentVolume = efProduct.Buy.OrderVolume,
                SellOrderUnitPrice = efProduct.Sell.UnitPrice,
                SellOrderWeekVolume = efProduct.Sell.OrderVolumeWeek,
                SellOrderCurrentOrders = efProduct.Sell.OrderCount,
                SellOrderCurrentVolume = efProduct.Sell.OrderVolume,
                OrderMetaPotentialProfitMultiplier = efProduct.Meta.ProfitMultiplier,
                OrderMetaMargin = efProduct.Meta.Margin,
                OrderMetaTotalWeekVolume = efProduct.Meta.TotalWeekVolume,
                OrderMetaFlipOpportunityScore = efProduct.Meta.FlipOpportunityScore,
                IsManipulated = efProduct.Meta.IsManipulated,
                ManipulationIntensity = efProduct.Meta.ManipulationIntensity,
                PriceDeviationPercent = efProduct.Meta.PriceDeviationPercent,
                // These are expensive to fetch and not strictly needed for the quick update, 
                // but we should at least clear them or preserve if needed.
                BuyMarketDataId = efProduct.Buy.Id,
                SellMarketDataId = efProduct.Sell.Id,
                BuyBook = product.Buy.OrderBook.Select(x => new Order(x.UnitPrice, x.Amount, x.Orders)).ToList(),
                SellBook = product.Sell.OrderBook.Select(x => new Order(x.UnitPrice, x.Amount, x.Orders)).ToList()
            };

            await hubContext.Clients.Group(efProduct.ProductKey).SendAsync("ProductUpdated", updateInfo, cancellationToken);
            
            // Broadcast a tick for the chart with proper OHLC aggregation
            var liveTick = liveCandleTracker.UpdateAndGetTick(
                efProduct.ProductKey,
                efProduct.Buy.UnitPrice,
                (double)(efProduct.Buy.OrderVolume + efProduct.Sell.OrderVolume));
            
            await hubContext.Clients.Group(efProduct.ProductKey).SendAsync("TickUpdated", liveTick, cancellationToken);
        }
    }

    private async Task<IEnumerable<ProductData>> BuildProductDataAsync(BazaarResponse bazaarResponse, ItemResponse itemResponse, CancellationToken cancellationToken)
    {
        var itemMap = itemResponse.Items.ToDictionary(x => x.Id, x => x);

        var productList = new List<ProductData>();
        
        foreach (var bazaar in bazaarResponse.Products.Values.Where(x => x.BuySummary.Count is not 0))
        {
            var item = itemMap.GetValueOrDefault(bazaar.ProductId);

            var buy = bazaar.BuySummary.FirstOrDefault();
            var sell = bazaar.SellSummary.FirstOrDefault();

            var buyOrderPrice = buy?.PricePerUnit ?? double.MaxValue;
            var sellOrderPrice = Math.Round(sell?.PricePerUnit ?? 0.1f, 1, MidpointRounding.ToZero);
            var buyMovingWeek = bazaar.QuickStatus.BuyMovingWeek;
            var sellMovingWeek = bazaar.QuickStatus.SellMovingWeek;

            var margin = buyOrderPrice - sellOrderPrice;
            var totalWeekVolume = buyMovingWeek + sellMovingWeek;
            var potentialProfitMultiplier = buyOrderPrice / sellOrderPrice;
            var buyingPower = (float)buyMovingWeek / sellMovingWeek;

            var friendlyName = item?.Name ?? ProductIdToName(bazaar.ProductId);

            // Calculate opportunity score using the new service
            var flipOpportunityScore = await opportunityScoringService.CalculateOpportunityScoreAsync(
                bazaar.ProductId,
                buyOrderPrice,
                sellOrderPrice,
                buyMovingWeek,
                sellMovingWeek,
                cancellationToken);

            // Calculate manipulation score to detect fire sale opportunities
            var manipulationScore = await opportunityScoringService.CalculateManipulationScoreAsync(
                bazaar.ProductId,
                buyOrderPrice,
                sellOrderPrice,
                cancellationToken);

            productList.Add(new ProductData
            {
                ItemId = bazaar.ProductId,
                Item = new Item
                {
                    FriendlyName = friendlyName,
                    Tier = item?.Tier ?? ItemTier.Common,
                    Unstackable = item?.Unstackable ?? false
                },
                Buy = new OrderInfo
                {
                    Last = bazaar.QuickStatus.BuyPrice,
                    OrderPrice = buyOrderPrice,
                    WeekVolume = buyMovingWeek,
                    CurrentOrders = bazaar.QuickStatus.BuyOrders,
                    CurrentVolume = bazaar.QuickStatus.BuyVolume,
                    OrderBook = bazaar.BuySummary.Select(x => new OrderBook
                    {
                        UnitPrice = x.PricePerUnit,
                        Orders = x.Orders,
                        Amount = x.Amount
                    })
                },
                Sell = new OrderInfo
                {
                    Last = bazaar.QuickStatus.SellPrice,
                    OrderPrice = sellOrderPrice,
                    WeekVolume = sellMovingWeek,
                    CurrentOrders = bazaar.QuickStatus.SellOrders,
                    CurrentVolume = bazaar.QuickStatus.SellVolume,
                    OrderBook = bazaar.SellSummary.Select(x => new OrderBook
                    {
                        UnitPrice = x.PricePerUnit,
                        Orders = x.Orders,
                        Amount = x.Amount
                    })
                },
                OrderMeta = new OrderMeta
                {
                    PotentialProfitMultiplier = potentialProfitMultiplier,
                    Margin = margin,
                    TotalWeekVolume = totalWeekVolume,
                    BuyOrderPower = buyingPower,
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
