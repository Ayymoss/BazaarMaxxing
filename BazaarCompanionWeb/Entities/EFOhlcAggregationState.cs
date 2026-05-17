using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace BazaarCompanionWeb.Entities;

/// <summary>
/// Per (product, interval) watermark for OhlcAggregationService. Tracks the most recent
/// candle period start we've built. Subsequent aggregation cycles only re-query ticks
/// at or after this watermark, replacing 7-day full rescans.
/// </summary>
[PrimaryKey(nameof(ProductKey), nameof(Interval))]
public sealed record EFOhlcAggregationState
{
    [MaxLength(64)] public required string ProductKey { get; set; }
    public required CandleInterval Interval { get; set; }

    /// <summary>
    /// The most recent period start for which we've built (or upserted) a candle.
    /// On next cycle we re-aggregate from this period forward — old completed periods are not re-queried.
    /// </summary>
    public required DateTime LastSeenPeriodStart { get; set; }

    public required DateTime UpdatedAt { get; set; }
}
