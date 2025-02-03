using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BazaarCompanionWeb.Entities;

public class EFBuyMarketData : EFMarketData
{
    [MaxLength(64)] public required string ProductKey { get; set; }
    [ForeignKey(nameof(ProductKey))] public EFProduct? Product { get; set; }
}
