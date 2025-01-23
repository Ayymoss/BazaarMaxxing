using System.Text.Json.Serialization;

namespace BazaarCompanionWeb.Models.Api.Bazaar;

public class Product
{
    [JsonPropertyName("product_id")]
    public string ProductId { get; set; }

    [JsonPropertyName("sell_summary")]
    public List<OrderSummary> SellSummary { get; set; }

    [JsonPropertyName("buy_summary")]
    public List<OrderSummary> BuySummary { get; set; }

    [JsonPropertyName("quick_status")]
    public QuickStatus QuickStatus { get; set; }
}
