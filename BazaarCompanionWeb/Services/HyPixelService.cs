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
                var buy = bazaar.BuySummary.First();
                var sell = bazaar.SellSummary.FirstOrDefault();

                var margin = buy.PricePerUnit - (sell?.PricePerUnit ?? 0);
                var totalWeekVolume = bazaar.QuickStatus.SellMovingWeek + bazaar.QuickStatus.BuyMovingWeek;
                var potentialProfitMultiplier = buy.PricePerUnit / (sell?.PricePerUnit ?? 0.1);
                var buyingPower = (float)bazaar.QuickStatus.BuyMovingWeek / bazaar.QuickStatus.SellMovingWeek;

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
                        UnitPrice = buy.PricePerUnit,
                        WeekVolume = bazaar.QuickStatus.BuyMovingWeek,
                        CurrentOrders = bazaar.QuickStatus.BuyOrders,
                        CurrentVolume = bazaar.QuickStatus.BuyVolume
                    },
                    Sell = new OrderInfo
                    {
                        UnitPrice = sell?.PricePerUnit,
                        WeekVolume = bazaar.QuickStatus.SellMovingWeek,
                        CurrentOrders = bazaar.QuickStatus.SellOrders,
                        CurrentVolume = bazaar.QuickStatus.SellVolume
                    },
                    OrderMeta = new OrderMeta
                    {
                        PotentialProfitMultiplier = potentialProfitMultiplier,
                        Margin = margin,
                        TotalWeekVolume = totalWeekVolume,
                        BuyOrderPower = buyingPower,
                    }
                };
            });
        return products;
    }
}
