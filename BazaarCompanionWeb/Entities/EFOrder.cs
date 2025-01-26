using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BazaarCompanionWeb.Entities;

public class EFOrder
{
    [Key] public int Id { get; set; }

    public required int Amount { get; set; }
    public required int Orders { get; set; }
    public required double UnitPrice { get; set; }

    public int MarketDataId { get; set; }
    [ForeignKey(nameof(MarketDataId))] public EFMarketData? MarketData { get; set; }
}
