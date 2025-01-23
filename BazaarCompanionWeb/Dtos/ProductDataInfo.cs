using BazaarCompanionWeb.Models.Api.Items;

namespace BazaarCompanionWeb.Dtos;

public class ProductDataInfo
{
    public required Guid Guid { get; set; }
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
    public List<PriceHistorySnapshot>? PriceHistory { get; set; }
    
}
public record PriceHistorySnapshot(DateOnly Date, double Buy, double Sell);
