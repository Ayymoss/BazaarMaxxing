using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BazaarCompanionWeb.Entities;

public class EFMarketData
{
    [Key] public int Id { get; set; }

    public required double BuyLastPrice { get; set; }
    public required double BuyLastOrderVolumeWeek { get; set; }
    public required int BuyLastOrderVolume { get; set; }
    public required int BuyLastOrderCount { get; set; }

    public required double SellLastPrice { get; set; }
    public required double SellLastOrderVolumeWeek { get; set; }
    public required int SellLastOrderVolume { get; set; }
    public required int SellLastOrderCount { get; set; }

    public Guid ProductGuid { get; set; }
    [ForeignKey(nameof(ProductGuid))] public EFProduct Product { get; set; }
}
