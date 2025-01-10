using System.Text.Json.Serialization;

namespace BazaarCompanion.Models.Api.Bazaar;

public class OrderSummary
{
    [JsonPropertyName("amount")]
    public int Amount { get; set; }

    [JsonPropertyName("pricePerUnit")]
    public double PricePerUnit { get; set; }

    [JsonPropertyName("orders")]
    public int Orders { get; set; }
}
