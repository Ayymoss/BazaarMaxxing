using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace BazaarCompanionWeb.Entities;

[Index(nameof(ProductKey), nameof(Interval), nameof(PeriodStart), IsUnique = true)]
[Index(nameof(Interval), nameof(PeriodStart))] // For efficient cleanup queries by interval
public sealed record EFOhlcCandle
{
    [Key] public long Id { get; set; }

    [MaxLength(64)] public required string ProductKey { get; set; }
    public required CandleInterval Interval { get; set; }
    public required DateTime PeriodStart { get; set; }
    public required double Open { get; set; }
    public required double High { get; set; }
    public required double Low { get; set; }
    public required double Close { get; set; }
    public required double Volume { get; set; }

    /// <summary>
    /// Average bid-ask spread during this candle period (Bid - Ask price).
    /// </summary>
    public required double Spread { get; set; }
    
    /// <summary>
    /// ASK price at candle close. Used for rendering the ASK line overlay on charts.
    /// </summary>
    public double AskClose { get; set; }

    [ForeignKey(nameof(ProductKey))] public EFProduct Product { get; set; } = null!;
}
