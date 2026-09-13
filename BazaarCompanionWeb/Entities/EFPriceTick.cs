using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace BazaarCompanionWeb.Entities;

/// <summary>
/// One five-minute bar per product, built in RAM from the one-minute polls and upserted by the flusher.
/// <see cref="Timestamp"/> is the bucket start. Before 2026-09-11 a row was a single coalesced sample
/// (one per product per ten-minute flush) with no open/high/low; those rows were backfilled flat
/// (open = high = low = close) and carry zero traded volume.
/// </summary>
[Index(nameof(ProductKey), nameof(Timestamp), IsUnique = true)]
public sealed record EFPriceTick
{
    [Key] public long Id { get; set; }

    [MaxLength(64)] public required string ProductKey { get; set; }

    /// <summary>Best bid at the end of the bucket (the bar's close).</summary>
    public required double BidPrice { get; set; }
    /// <summary>Best ask at the end of the bucket (the bar's close).</summary>
    public required double AskPrice { get; set; }
    public required DateTime Timestamp { get; set; }

    /// <summary>Resting bid depth (Hypixel <c>sellVolume</c>) at the end of the bucket. Open interest, not volume.</summary>
    public required long BidVolume { get; set; }
    /// <summary>Resting ask depth (Hypixel <c>buyVolume</c>) at the end of the bucket. Open interest, not volume.</summary>
    public required long AskVolume { get; set; }

    public double BidOpen { get; set; }
    public double BidHigh { get; set; }
    public double BidLow { get; set; }
    public double AskOpen { get; set; }
    public double AskHigh { get; set; }
    public double AskLow { get; set; }

    /// <summary>
    /// Units instantly bought during the bucket (asks consumed) — the increase in Hypixel's
    /// <c>buyMovingWeek</c> counter. The counter only moves on fills, never on cancels.
    /// </summary>
    public long TradedBuy { get; set; }
    /// <summary>Units instantly sold during the bucket (bids consumed) — the increase in <c>sellMovingWeek</c>.</summary>
    public long TradedSell { get; set; }

    /// <summary>Units of TradedBuy + TradedSell that were estimated during an expiry burst rather than read off the counters.</summary>
    public long TradedEstimated { get; set; }

    /// <summary>False for legacy bars whose estimation provenance was not persisted.</summary>
    public bool FlowEvidenceKnown { get; set; }

    [ForeignKey(nameof(ProductKey))] public EFProduct Product { get; set; } = null!;
}
