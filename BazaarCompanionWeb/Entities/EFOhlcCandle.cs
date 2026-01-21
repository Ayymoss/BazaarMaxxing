using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace BazaarCompanionWeb.Entities;

[Index(nameof(ProductKey), nameof(Interval), nameof(PeriodStart))]
public class EFOhlcCandle
{
    [Key] public long Id { get; set; }

    [MaxLength(64)] public required string ProductKey { get; set; }
    public required CandleInterval Interval { get; set; }
    public required DateTime PeriodStart { get; set; }
    public required double Open { get; set; }
    public required double High { get; set; }
    public required double Low { get; set; }
    public required double Close { get; set; }
    public double? Volume { get; set; }

    [ForeignKey(nameof(ProductKey))] public EFProduct? Product { get; set; }
}
