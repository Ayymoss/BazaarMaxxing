using BazaarCompanionWeb.Interfaces.Api;
using BazaarCompanionWeb.Interfaces.Database;
using BazaarCompanionWeb.Models;
using BazaarCompanionWeb.Models.Api.Bazaar;
using BazaarCompanionWeb.Models.Api.Items;
using BazaarCompanionWeb.Utilities;
using Humanizer;
using Item = BazaarCompanionWeb.Models.Item;

namespace BazaarCompanionWeb.Services;

public class HyPixelService(IHyPixelApi hyPixelApi, IProductRepository productRepository, TimeCache timeCache)
{
    public async Task FetchDataAsync(CancellationToken cancellationToken)
    {
        var bazaarResponse = await hyPixelApi.GetBazaarAsync();
        var itemResponse = await hyPixelApi.GetItemsAsync();
        var products = BuildProductData(bazaarResponse, itemResponse).Select(x => x.Map()).ToList();
        await productRepository.UpdateOrAddProductsAsync(products, cancellationToken);
        timeCache.LastUpdated = TimeProvider.System.GetLocalNow();
    }

    private IEnumerable<ProductData> BuildProductData(BazaarResponse bazaarResponse, ItemResponse itemResponse)
    {
        var itemMap = itemResponse.Items.ToDictionary(x => x.Id, x => x);

        var products = bazaarResponse.Products.Values
            .Where(x => x.BuySummary.Count is not 0)
            .Select(bazaar =>
            {
                var item = itemMap.GetValueOrDefault(bazaar.ProductId);

                var buy = bazaar.BuySummary.FirstOrDefault();
                var sell = bazaar.SellSummary.FirstOrDefault();

                var buyPrice = buy?.PricePerUnit ?? double.MaxValue;
                var sellPrice = Math.Round(sell?.PricePerUnit ?? 0.1f, 1, MidpointRounding.ToZero);
                var buyMovingWeek = bazaar.QuickStatus.BuyMovingWeek;
                var sellMovingWeek = bazaar.QuickStatus.SellMovingWeek;

                var margin = buyPrice - sellPrice;
                var totalWeekVolume = buyMovingWeek + sellMovingWeek;
                var potentialProfitMultiplier = buyPrice / sellPrice;
                var buyingPower = (float)buyMovingWeek / sellMovingWeek;

                var friendlyName = item?.Name ?? ProductIdToName(bazaar.ProductId);

                return new ProductData
                {
                    ItemId = bazaar.ProductId,
                    Item = new Item
                    {
                        FriendlyName = friendlyName,
                        Tier = item?.Tier ?? ItemTier.Unknown,
                        Unstackable = item?.Unstackable ?? false
                    },
                    Buy = new OrderInfo
                    {
                        UnitPrice = buyPrice,
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
                        UnitPrice = sellPrice,
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
                        FlipOpportunityScore = FlipOpportunityScore(buyPrice, sellPrice, buyMovingWeek, sellMovingWeek,
                            potentialProfitMultiplier)
                    }
                };
            });
        return products;
    }

    private static string ProductIdToName(string productId)
    {
        var split = productId.Split("_");
        if (!int.TryParse(split.Last(), out var level)) return productId.Humanize().Titleize();

        var nameParts = split.Take(split.Length - 1).ToArray();
        var baseName = string.Join(" ", nameParts).Humanize().Titleize();
        return $"{baseName} {level.ToRoman()}";
    }

    private static double FlipOpportunityScore(double buyPrice, double sellPrice, long buyMovingWeek, long sellMovingWeek,
        double multiplier)
    {
        if (buyMovingWeek is 0 || sellMovingWeek is 0) return 0;

        var volumeRatio = buyMovingWeek / (float)sellMovingWeek;
        var ratioWeight = Math.Min(volumeRatio, 1 / volumeRatio);
        var hourlyVolume = buyMovingWeek / 7 / 24;
        var profitPerItem = buyPrice - sellPrice;

        const float riskTolerance = 0.1f;
        var riskAdjustment = 1.0 / (1.0 + sellPrice * (1.0 / (buyMovingWeek + 1)) * riskTolerance);

        var result = hourlyVolume * profitPerItem * ratioWeight * riskAdjustment *
                     (1 + Math.Log(multiplier));

        // Some arbitrary baseline that's achievable via working in-game
        const int inGameWorkEffortForCoins = 10_000_000;
        return result / inGameWorkEffortForCoins;
    }
}
