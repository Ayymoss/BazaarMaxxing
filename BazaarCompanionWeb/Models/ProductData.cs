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
                PotentialProfitMultiplier = (Buy.UnitPrice ?? double.MaxValue) / (Sell.UnitPrice ?? 0.1),
                Margin = (Buy.UnitPrice ?? double.MaxValue) - (Sell.UnitPrice ?? 0.1),
                TotalWeekVolume = Buy.WeekVolume + Sell.WeekVolume,
            },
            Snapshots =
            [
                new EFPriceSnapshot
                {
                    BuyUnitPrice = Buy.UnitPrice ?? double.MaxValue,
                    SellUnitPrice = Sell.UnitPrice ?? 0.1,
                    Taken = TimeProvider.System.GetUtcNow().DateTime,
                }
            ],
            MarketData = new EFMarketData
            {
                BuyLastPrice = Buy.UnitPrice ?? double.MaxValue,
                BuyLastOrderVolumeWeek = Buy.WeekVolume,
                BuyLastOrderVolume = Buy.CurrentVolume,
                BuyLastOrderCount = Buy.CurrentOrders,
                SellLastPrice = Sell.UnitPrice ?? 0.1,
                SellLastOrderVolumeWeek = Sell.WeekVolume,
                SellLastOrderVolume = Sell.CurrentVolume,
                SellLastOrderCount = Sell.CurrentOrders,
            }
        };
    }
}
