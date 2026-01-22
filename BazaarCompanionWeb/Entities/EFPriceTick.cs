using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace BazaarCompanionWeb.Entities;

[Index(nameof(ProductKey), nameof(Timestamp))]
public sealed record EFPriceTick
{
    [Key] public long Id { get; set; }

    [MaxLength(64)] public required string ProductKey { get; set; }
    public required double BuyPrice { get; set; }
    public required double SellPrice { get; set; }
    public required DateTime Timestamp { get; set; }
    public required long BuyVolume { get; set; }
    public required long SellVolume { get; set; }

    [ForeignKey(nameof(ProductKey))] public EFProduct Product { get; set; } = null!;
}
