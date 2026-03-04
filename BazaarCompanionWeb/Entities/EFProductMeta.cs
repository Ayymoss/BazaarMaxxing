using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BazaarCompanionWeb.Entities;

public sealed record EFProductMeta
{
    [Key] public int Id { get; set; }

    public required double ProfitMultiplier { get; set; }
    public required double Spread { get; set; }
    public required double TotalWeekVolume { get; set; }
    public required double FlipOpportunityScore { get; set; }
    public required bool IsManipulated { get; set; }
    public required double ManipulationIntensity { get; set; }
    public required double PriceDeviationPercent { get; set; }

    // Trade recommendation fields
    public int? SuggestedBidVolume { get; set; }
    public double? SuggestedBidPrice { get; set; }
    public double? SuggestedAskPrice { get; set; }
    public double? EstimatedFillTimeHours { get; set; }
    public double? EstimatedProfitPerUnit { get; set; }
    public double? EstimatedTotalProfit { get; set; }
    public double? RecommendationConfidence { get; set; }

    [MaxLength(64)] public required string ProductKey { get; set; }
    [ForeignKey(nameof(ProductKey))] public EFProduct Product { get; set; } = null!;
}
