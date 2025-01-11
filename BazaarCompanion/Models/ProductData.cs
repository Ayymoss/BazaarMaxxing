namespace BazaarCompanion.Models;

public class ProductData
{
    public required string ItemId { get; set; }

    public required Item Item { get; set; }
    public required OrderInfo Buy { get; set; }
    public required OrderInfo Sell { get; set; }
    public required OrderMeta OrderMeta { get; set; }
    public decimal NpcProfit { get; set; }
    public decimal NpcMargin { get; set; }
}
