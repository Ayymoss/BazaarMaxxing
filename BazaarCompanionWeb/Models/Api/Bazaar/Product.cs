using System.Text.Json.Serialization;

namespace BazaarCompanionWeb.Models.Api.Bazaar;

public class Product
{
    [JsonPropertyName("product_id")] public string ProductId { get; set; }
    [JsonPropertyName("buy_summary")] public List<OrderBookEntry> Asks { get; set; }
    [JsonPropertyName("sell_summary")] public List<OrderBookEntry> Bids { get; set; }
    [JsonPropertyName("quick_status")] public MarketTicker Ticker { get; set; }
}
