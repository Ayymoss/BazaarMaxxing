﻿using System.ComponentModel.DataAnnotations;
using BazaarCompanionWeb.Models.Api.Items;

namespace BazaarCompanionWeb.Entities;

public class EFProduct
{
    [Key, MaxLength(64)] public required string ProductKey { get; set; }
    [MaxLength(64)] public required string FriendlyName { get; set; }
    public required ItemTier Tier { get; set; }
    public required bool Unstackable { get; set; }

    public required EFBuyMarketData Buy { get; set; }
    public required EFSellMarketData Sell { get; set; }
    public required EFProductMeta Meta { get; set; }
    public required ICollection<EFPriceSnapshot> Snapshots { get; set; }
}
