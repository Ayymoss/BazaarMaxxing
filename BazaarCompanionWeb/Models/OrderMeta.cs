namespace BazaarCompanionWeb.Models;

public class OrderMeta
{
    public double PotentialProfitMultiplier { get; set; }
    public double Spread { get; set; }
    public double TotalWeekVolume { get; set; }
    public double BidOrderPower { get; set; }
    public double FlipOpportunityScore { get; set; }
    public bool IsManipulated { get; set; }
    public double ManipulationIntensity { get; set; }
    public double PriceDeviationPercent { get; set; }
}
