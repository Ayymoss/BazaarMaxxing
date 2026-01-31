using System.ComponentModel.DataAnnotations;
using BazaarCompanionWeb.Models.Api.Items;
using Microsoft.EntityFrameworkCore;

namespace BazaarCompanionWeb.Entities;

[Index(nameof(LastSeenAt))] // For efficient stale product cleanup queries
public class EFProduct
{
    [Key, MaxLength(64)] public required string ProductKey { get; set; }
    [MaxLength(64)] public required string FriendlyName { get; set; }
    public required ItemTier Tier { get; set; }
    public required bool Unstackable { get; set; }
    public string? SkinUrl { get; set; }
    
    /// <summary>
    /// Timestamp of when this product was last seen in the API response.
    /// Used for stale product cleanup.
    /// </summary>
    public DateTime LastSeenAt { get; set; } = DateTime.UtcNow;

    public required EFBidMarketData Bid { get; set; }
    public required EFAskMarketData Ask { get; set; }
    public required EFProductMeta Meta { get; set; }
    public required ICollection<EFPriceSnapshot> Snapshots { get; set; }
}
