using System.Text.Json.Serialization;

namespace BazaarCompanion.Models.Api.Bazaar;

public class BazaarResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("lastUpdated")]
    public long LastUpdated { get; set; }

    [JsonPropertyName("products")]
    public Dictionary<string, Product> Products { get; set; }
}
