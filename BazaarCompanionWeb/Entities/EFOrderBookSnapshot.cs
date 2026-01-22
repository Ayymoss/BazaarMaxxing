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

    public required int BuyVolume { get; set; }

    public required int SellVolume { get; set; }

    public required int BuyOrderCount { get; set; }

    public required int SellOrderCount { get; set; }
}
