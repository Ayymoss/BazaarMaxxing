using System.Text.Json.Serialization;

namespace BazaarCompanion.Models.Api.Bazaar;

public class QuickStatus
{
    [JsonPropertyName("productId")]
    public string ProductId { get; set; }

    [JsonPropertyName("sellPrice")]
    public double SellPrice { get; set; }

    [JsonPropertyName("sellVolume")]
    public int SellVolume { get; set; }

    [JsonPropertyName("sellMovingWeek")]
    public long SellMovingWeek { get; set; }

    [JsonPropertyName("sellOrders")]
    public int SellOrders { get; set; }

    [JsonPropertyName("buyPrice")]
    public double BuyPrice { get; set; }

    [JsonPropertyName("buyVolume")]
    public int BuyVolume { get; set; }

    [JsonPropertyName("buyMovingWeek")]
    public long BuyMovingWeek { get; set; }

    [JsonPropertyName("buyOrders")]
    public int BuyOrders { get; set; }
}
