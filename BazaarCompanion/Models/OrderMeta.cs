namespace BazaarCompanion.Models;

public class OrderMeta
{
    public double PotentialProfitMultiplier { get; set; }
    public double Margin { get; set; }
    public double MarginPercentage { get; set; }
    public double TotalWeekVolume { get; set; }
    public decimal NpcProfit { get; set; }
    public decimal NpcMargin { get; set; }
}
