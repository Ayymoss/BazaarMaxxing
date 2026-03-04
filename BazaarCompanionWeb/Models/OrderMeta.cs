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

    // Trade recommendation fields
    public int? SuggestedBidVolume { get; set; }
    public double? SuggestedBidPrice { get; set; }
    public double? SuggestedAskPrice { get; set; }
    public double? EstimatedFillTimeHours { get; set; }
    public double? EstimatedProfitPerUnit { get; set; }
    public double? EstimatedTotalProfit { get; set; }
    public double? RecommendationConfidence { get; set; }
}
