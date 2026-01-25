using System.Text.Json.Serialization;

namespace BazaarCompanionWeb.Models.Api.Bazaar;

public class OrderBookEntry
{
    [JsonPropertyName("amount")]
    public int Amount { get; set; }

    [JsonPropertyName("pricePerUnit")]
    public double PricePerUnit { get; set; }

    [JsonPropertyName("orders")]
    public int OrderCount { get; set; }
}
