using BazaarCompanionWeb.Entities;

namespace BazaarCompanionWeb.Models;

public class ProductData
{
    public required string ItemId { get; set; }

    public required Item Item { get; set; }
    public required OrderInfo Buy { get; set; }
    public required OrderInfo Sell { get; set; }
    public required OrderMeta OrderMeta { get; set; }

    public EFProduct Map()
    {
        return new EFProduct
        {
            Name = ItemId,
            FriendlyName = Item.FriendlyName,
            Tier = Item.Tier,
            Unstackable = Item.Unstackable,
            Meta = new EFProductMeta
            {
                ProfitMultiplier = Buy.UnitPrice / Sell.UnitPrice,
                Margin = Buy.UnitPrice - Sell.UnitPrice,
                TotalWeekVolume = Buy.WeekVolume + Sell.WeekVolume,
                FlipOpportunityScore = OrderMeta.FlipOpportunityScore,
            },
            Snapshots =
            [
                new EFPriceSnapshot
                {
                    BuyUnitPrice = Buy.UnitPrice,
                    SellUnitPrice = Sell.UnitPrice,
                    Taken = TimeProvider.System.GetUtcNow().DateTime,
                }
            ],
            Buy = new EFBuyMarketData
            {
                UnitPrice = Buy.UnitPrice,
                OrderVolumeWeek = Buy.WeekVolume,
                OrderVolume = Buy.CurrentVolume,
                OrderCount = Buy.CurrentOrders,
                Book = Buy.OrderBook.Select(x => new EFOrder
                {
                    Amount = x.Amount,
                    Orders = x.Orders,
                    UnitPrice = x.UnitPrice,
                }).ToList(),
            },
            Sell = new EFSellMarketData
            {
                UnitPrice = Sell.UnitPrice,
                OrderVolumeWeek = Sell.WeekVolume,
                OrderVolume = Sell.CurrentVolume,
                OrderCount = Sell.CurrentOrders,
                Book = Sell.OrderBook.Select(x => new EFOrder
                {
                    Amount = x.Amount,
                    Orders = x.Orders,
                    UnitPrice = x.UnitPrice,
                }).ToList(),
            }
        };
    }
}
