using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;
using BazaarCompanionWeb.Models;

namespace BazaarCompanionWeb.Entities;

public abstract record EFMarketData
{
    [Key] public int Id { get; set; }

    public required double UnitPrice { get; set; }
    public required double OrderVolumeWeek { get; set; }
    public required int OrderVolume { get; set; }
    public required int OrderCount { get; set; }

    [MaxLength(8192)] public required string BookValue { get; set; }
    [NotMapped] public IReadOnlyList<OrderBook> Books => JsonSerializer.Deserialize<List<OrderBook>>(BookValue) ?? [];
}
