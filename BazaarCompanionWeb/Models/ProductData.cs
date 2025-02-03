﻿using System.Text.Json;
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
            ProductKey = ItemId,
            FriendlyName = Item.FriendlyName,
            Tier = Item.Tier,
            Unstackable = Item.Unstackable,
            Meta = new EFProductMeta
            {
                ProfitMultiplier = Buy.UnitPrice / Sell.UnitPrice,
                Margin = Buy.UnitPrice - Sell.UnitPrice,
                TotalWeekVolume = Buy.WeekVolume + Sell.WeekVolume,
                FlipOpportunityScore = OrderMeta.FlipOpportunityScore,
                ProductKey = ItemId
            },
            Snapshots =
            [
                new EFPriceSnapshot
                {
                    BuyUnitPrice = Buy.UnitPrice,
                    SellUnitPrice = Sell.UnitPrice,
                    Taken = DateOnly.FromDateTime(TimeProvider.System.GetUtcNow().DateTime),
                    ProductKey = ItemId
                }
            ],
            Buy = new EFBuyMarketData
            {
                UnitPrice = Buy.UnitPrice,
                OrderVolumeWeek = Buy.WeekVolume,
                OrderVolume = Buy.CurrentVolume,
                OrderCount = Buy.CurrentOrders,
                BookValue = JsonSerializer.Serialize(Buy.OrderBook.Select(x => new OrderBook
                {
                    Amount = x.Amount,
                    Orders = x.Orders,
                    UnitPrice = x.UnitPrice,
                })),
                ProductKey = ItemId
            },
            Sell = new EFSellMarketData
            {
                UnitPrice = Sell.UnitPrice,
                OrderVolumeWeek = Sell.WeekVolume,
                OrderVolume = Sell.CurrentVolume,
                OrderCount = Sell.CurrentOrders,
                BookValue = JsonSerializer.Serialize(Sell.OrderBook.Select(x => new OrderBook
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
