using BazaarCompanion.Models.Api.Items;

namespace BazaarCompanion.Models;

public class Item
{
    public required string FriendlyName { get; set; }
    public required ItemTier Tier { get; set; }
    public bool Unstackable { get; set; }
}
