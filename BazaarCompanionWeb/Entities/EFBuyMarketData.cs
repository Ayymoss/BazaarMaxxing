using System.ComponentModel.DataAnnotations.Schema;

namespace BazaarCompanionWeb.Entities;

public class EFBuyMarketData : EFMarketData
{
    public Guid ProductGuid { get; set; }
    [ForeignKey(nameof(ProductGuid))] public EFProduct? Product { get; set; }
}
