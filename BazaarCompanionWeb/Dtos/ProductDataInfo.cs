using BazaarCompanionWeb.Models.Api.Items;

namespace BazaarCompanionWeb.Dtos;

public class ProductDataInfo
{
    public required int AskMarketDataId { get; set; }
    public required int BidMarketDataId { get; set; }
    public required string ItemId { get; set; }
    public string ItemFriendlyName { get; set; }
    public ItemTier ItemTier { get; set; }
    public bool ItemUnstackable { get; set; }
    public string? SkinUrl { get; set; }
    public double BidUnitPrice { get; set; }
    public double BidWeekVolume { get; set; }
    public int BidCurrentOrders { get; set; }
    public int BidCurrentVolume { get; set; }
    public double AskUnitPrice { get; set; }
    public double AskWeekVolume { get; set; }
    public int AskCurrentOrders { get; set; }
    public int AskCurrentVolume { get; set; }
    public double OrderMetaPotentialProfitMultiplier { get; set; }
    public double OrderMetaSpread { get; set; }
    public double OrderMetaTotalWeekVolume { get; set; }
    public double OrderMetaFlipOpportunityScore { get; set; }
    public bool IsManipulated { get; set; }
    public double ManipulationIntensity { get; set; }
    public double PriceDeviationPercent { get; set; }
    public List<PriceHistorySnapshot>? PriceHistory { get; set; }
    public List<Order>? AskBook { get; set; }
    public List<Order>? BidBook { get; set; }
}

public record PriceHistorySnapshot(DateOnly Date, double Bid, double Ask);

public record Order(double UnitPrice, int Amount, int Orders);
