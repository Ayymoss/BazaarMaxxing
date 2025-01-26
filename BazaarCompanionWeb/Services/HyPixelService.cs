using BazaarCompanionWeb.Interfaces.Api;
using BazaarCompanionWeb.Interfaces.Database;
using BazaarCompanionWeb.Models;
using BazaarCompanionWeb.Models.Api.Bazaar;
using BazaarCompanionWeb.Models.Api.Items;
using Item = BazaarCompanionWeb.Models.Item;

namespace BazaarCompanionWeb.Services;

public class HyPixelService(IHyPixelApi hyPixelApi, IProductRepository productRepository)
{
    public async Task FetchDataAsync(CancellationToken cancellationToken)
    {
        var bazaarResponse = await hyPixelApi.GetBazaarAsync();
        var itemResponse = await hyPixelApi.GetItemsAsync();
        var products = BuildProductData(bazaarResponse, itemResponse).Select(x => x.Map()).ToList();
        await productRepository.UpdateOrAddProductsAsync(products, cancellationToken);
    }

    private IEnumerable<ProductData> BuildProductData(BazaarResponse bazaarResponse, ItemResponse itemResponse)
    {
        var products = bazaarResponse.Products.Values
            .Where(x => x.BuySummary.Count is not 0)
            .Join(itemResponse.Items, bazaar => bazaar.ProductId, item => item.Id, (bazaar, item) =>
            {
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

                return new ProductData
                {
                    ItemId = bazaar.ProductId,
                    Item = new Item
                    {
                        FriendlyName = item.Name,
                        Tier = item.Tier,
                        Unstackable = item.Unstackable
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
                        FlipOpportunityScore = ProfitReferenceValue(buyPrice, sellPrice, buyMovingWeek, sellMovingWeek,
                            potentialProfitMultiplier)
                    }
                };
            });
        return products;
    }

    private static double ProfitReferenceValue(double buyPrice, double sellPrice, long buyMovingWeek, long sellMovingWeek,
        double multiplier)
    {
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
