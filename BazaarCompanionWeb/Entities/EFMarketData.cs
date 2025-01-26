using System.ComponentModel.DataAnnotations;

namespace BazaarCompanionWeb.Entities;

public abstract class EFMarketData
{
    [Key] public int Id { get; set; }

    public required double UnitPrice { get; set; }
    public required double OrderVolumeWeek { get; set; }
    public required int OrderVolume { get; set; }
    public required int OrderCount { get; set; }

    public required ICollection<EFOrder> Book { get; set; }
}
