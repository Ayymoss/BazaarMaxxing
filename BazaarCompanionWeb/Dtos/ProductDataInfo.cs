using BazaarCompanionWeb.Models.Api.Items;

namespace BazaarCompanionWeb.Dtos;

public class ProductDataInfo
{
    public required Guid ProductGuid { get; set; }
    public required int SellMarketDataId { get; set; }
    public required int BuyMarketDataId { get; set; }
    public string ItemId { get; set; }
    public string ItemFriendlyName { get; set; }
    public ItemTier ItemTier { get; set; }
    public bool ItemUnstackable { get; set; }
    public double? BuyOrderUnitPrice { get; set; }
    public double BuyOrderWeekVolume { get; set; }
    public int BuyOrderCurrentOrders { get; set; }
    public int BuyOrderCurrentVolume { get; set; }
    public double? SellOrderUnitPrice { get; set; }
    public double SellOrderWeekVolume { get; set; }
    public int SellOrderCurrentOrders { get; set; }
    public int SellOrderCurrentVolume { get; set; }
    public double OrderMetaPotentialProfitMultiplier { get; set; }
    public double OrderMetaMargin { get; set; }
    public double OrderMetaTotalWeekVolume { get; set; }
    public double OrderMetaFlipOpportunityScore { get; set; }
    public List<PriceHistorySnapshot>? PriceHistory { get; set; }
    public List<Order>? SellBook { get; set; }
    public List<Order>? BuyBook { get; set; }
}

public record PriceHistorySnapshot(DateOnly Date, double Buy, double Sell);

public record Order(double UnitPrice, int Amount, int Orders);
