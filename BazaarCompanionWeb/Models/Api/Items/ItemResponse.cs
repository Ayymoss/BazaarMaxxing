using System.Text.Json.Serialization;

namespace BazaarCompanionWeb.Models.Api.Items;

public class ItemResponse
{
    [JsonPropertyName("success")] public bool Success { get; set; }

    [JsonPropertyName("lastUpdated")] public long LastUpdated { get; set; }

    [JsonPropertyName("items")] public List<Item> Items { get; set; }
}
