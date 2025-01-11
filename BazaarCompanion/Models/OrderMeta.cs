namespace BazaarCompanion.Models;

public class OrderMeta
{
    public required double PotentialProfitMultiplier { get; set; }
    public required double Margin { get; set; }
    public required double MarginPercentage { get; set; }
    public required double TotalWeekVolume { get; set; }
    public decimal NpcProfit { get; set; }
    public decimal NpcMargin { get; set; }
}
