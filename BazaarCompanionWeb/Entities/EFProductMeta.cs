using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BazaarCompanionWeb.Entities;

public class EFProductMeta
{
    [Key] public int Id { get; set; }
    public required double PotentialProfitMultiplier { get; set; }
    public required double Margin { get; set; }
    public required double TotalWeekVolume { get; set; }
    public Guid ProductGuid { get; set; }
    [ForeignKey(nameof(ProductGuid))] public EFProduct Product { get; set; }
}
