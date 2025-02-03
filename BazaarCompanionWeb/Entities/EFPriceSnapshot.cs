using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BazaarCompanionWeb.Entities;

public class EFPriceSnapshot
{
    [Key] public int Id { get; set; }

    public required double BuyUnitPrice { get; set; }
    public required double SellUnitPrice { get; set; }
    public required DateOnly Taken { get; set; }

    [MaxLength(64)] public required string ProductKey { get; set; }
    [ForeignKey(nameof(ProductKey))] public EFProduct? Product { get; set; }
}
