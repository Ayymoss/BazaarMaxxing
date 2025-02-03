using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BazaarCompanionWeb.Entities;

public class EFProductMeta
{
    [Key] public int Id { get; set; }

    public required double ProfitMultiplier { get; set; }
    public required double Margin { get; set; }
    public required double TotalWeekVolume { get; set; }
    public required double FlipOpportunityScore { get; set; }

    [MaxLength(64)] public required string ProductKey { get; set; }
    [ForeignKey(nameof(ProductKey))] public EFProduct? Product { get; set; }
}
