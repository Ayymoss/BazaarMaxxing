using System.Text.Json.Serialization;
using BazaarCompanionWeb.Utilities;

namespace BazaarCompanionWeb.Models.Api.Items;

public class Item
{
    [JsonPropertyName("id")] public string Id { get; set; }
    [JsonPropertyName("name")] public string Name { get; set; }
    [JsonPropertyName("npc_sell_price")] public double? NpcSellPrice { get; set; }
    [JsonPropertyName("unstackable")] public bool Unstackable { get; set; } = false;

    [JsonPropertyName("tier"), JsonConverter(typeof(TierConverter))]
    public ItemTier Tier { get; set; }
}

public enum ItemTier
{
    Unknown,
    Common,
    Uncommon,
    Rare,
    Epic,
    Legendary,
    Mythic,
    Supreme,
    Special,
    VerySpecial,
    Unobtainable,
}
