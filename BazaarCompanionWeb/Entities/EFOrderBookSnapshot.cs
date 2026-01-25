using System.ComponentModel.DataAnnotations;

namespace BazaarCompanionWeb.Entities;

/// <summary>
/// Periodic snapshot of order book data for historical analysis (heatmap).
/// Uses separate 7-day retention policy - does not affect primary OHLC/price data.
/// </summary>
public sealed record EFOrderBookSnapshot
{
    [Key] public int Id { get; set; }

    [MaxLength(64)]
    public required string ProductKey { get; set; }

    public required DateTime Timestamp { get; set; }

    public required double PriceLevel { get; set; }

    public required int BidVolume { get; set; }

    public required int AskVolume { get; set; }

    public required int BidOrderCount { get; set; }

    public required int AskOrderCount { get; set; }
}
