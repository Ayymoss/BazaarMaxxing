using System.Text.Json.Serialization;

namespace BazaarCompanionWeb.Models.Api.Bazaar;

public class QuickStatus
{
    [JsonPropertyName("productId")] public string ProductId { get; set; }
    [JsonPropertyName("sellVolume")] public int SellVolume { get; set; }
    [JsonPropertyName("sellMovingWeek")] public long SellMovingWeek { get; set; }
    [JsonPropertyName("sellOrders")] public int SellOrders { get; set; }
    [JsonPropertyName("buyVolume")] public int BuyVolume { get; set; }
    [JsonPropertyName("buyMovingWeek")] public long BuyMovingWeek { get; set; }
    [JsonPropertyName("buyOrders")] public int BuyOrders { get; set; }
}
