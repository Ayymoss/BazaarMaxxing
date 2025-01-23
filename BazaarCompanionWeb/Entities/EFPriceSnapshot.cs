using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BazaarCompanionWeb.Entities;

public class EFPriceSnapshot
{
    [Key] public int Id { get; set; }

    public required double BuyUnitPrice { get; set; }
    public required double SellUnitPrice { get; set; }
    public required DateTime Taken { get; set; }

    public Guid ProductGuid { get; set; }
    [ForeignKey(nameof(ProductGuid))] public EFProduct Product { get; set; }
}
