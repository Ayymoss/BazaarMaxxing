using System.Text.Json;
using BazaarCompanionWeb.Dtos;
using BazaarCompanionWeb.Entities;

namespace BazaarCompanionWeb.Models;

public class ProductData
{
    public required string ItemId { get; set; }

    public required Item Item { get; set; }
    public required OrderInfo Bid { get; set; }
    public required OrderInfo Ask { get; set; }
    public required OrderMeta OrderMeta { get; set; }

    public EFProduct Map()
    {
        return new EFProduct
        {
            ProductKey = ItemId,
            FriendlyName = Item.FriendlyName,
            Tier = Item.Tier,
            Unstackable = Item.Unstackable,
            SkinUrl = Item.SkinUrl,
            Meta = new EFProductMeta
            {
                ProfitMultiplier = OrderMeta.PotentialProfitMultiplier,
                Spread = OrderMeta.Spread,
                TotalWeekVolume = Bid.WeekVolume + Ask.WeekVolume,
                FlipOpportunityScore = OrderMeta.FlipOpportunityScore,
                IsManipulated = OrderMeta.IsManipulated,
                ManipulationIntensity = OrderMeta.ManipulationIntensity,
                PriceDeviationPercent = OrderMeta.PriceDeviationPercent,
                ProductKey = ItemId
            },
            Snapshots =
            [
                new EFPriceSnapshot
                {
                    BidUnitPrice = Bid.Last,
                    AskUnitPrice = Ask.Last,
                    Taken = DateOnly.FromDateTime(TimeProvider.System.GetUtcNow().DateTime),
                    ProductKey = ItemId
                }
            ],
            Bid = new EFBidMarketData
            {
                UnitPrice = Bid.OrderPrice,
                OrderVolumeWeek = Bid.WeekVolume,
                OrderVolume = Bid.CurrentVolume,
                OrderCount = Bid.CurrentOrders,
                BookValue = JsonSerializer.Serialize(Bid.OrderBook.Select(x => new OrderBook
                {
                    Amount = x.Amount,
                    Orders = x.Orders,
                    UnitPrice = x.UnitPrice,
                })),
                ProductKey = ItemId
            },
            Ask = new EFAskMarketData
            {
                UnitPrice = Ask.OrderPrice,
                OrderVolumeWeek = Ask.WeekVolume,
                OrderVolume = Ask.CurrentVolume,
                OrderCount = Ask.CurrentOrders,
                BookValue = JsonSerializer.Serialize(Ask.OrderBook.Select(x => new OrderBook
                {
                    Amount = x.Amount,
                    Orders = x.Orders,
                    UnitPrice = x.UnitPrice,
                })),
                ProductKey = ItemId
            }
        };
    }
}
