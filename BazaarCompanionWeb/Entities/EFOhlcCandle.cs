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

    /// <summary>
    /// Units traded during the period: <see cref="BuyVolume"/> + <see cref="SellVolume"/>. Derived from the
    /// increments of Hypixel's moving-week counters, so a cancelled order never counts. Candles from
    /// before 2026-09-11 were zeroed — their "volume" was resting order-book depth summed across samples.
    /// </summary>
    public required double Volume { get; set; }

    /// <summary>Units instantly bought (asks consumed) during the period.</summary>
    public double BuyVolume { get; set; }
    /// <summary>Units instantly sold (bids consumed) during the period.</summary>
    public double SellVolume { get; set; }

    /// <summary>Estimated part of volume; null if any constituent has unknown provenance.</summary>
    public double? EstimatedVolume { get; set; }

    /// <summary>
    /// Average bid-ask spread during this candle period (Bid - Ask price).
    /// </summary>
    public required double Spread { get; set; }

    /// <summary>
    /// ASK price at candle close. Used for rendering the ASK candle on charts.
    /// </summary>
    public double AskClose { get; set; }

    /// <summary>Ask open/high/low. Zero on candles that pre-date the ask candle (only the close was kept).</summary>
    public double AskOpen { get; set; }
    public double AskHigh { get; set; }
    public double AskLow { get; set; }

    [ForeignKey(nameof(ProductKey))] public EFProduct Product { get; set; } = null!;
}
