using System.Text.Json.Serialization;

namespace BazaarCompanionWeb.Models.Api.Bazaar;

public class MarketTicker
{
    [JsonPropertyName("productId")] public string ProductId { get; set; }

    // --- BID SIDE (Hypixel "Sell" / Demand) ---
    [JsonPropertyName("sellPrice")] public double BestBidPrice { get; set; }
    [JsonPropertyName("sellVolume")] public int TotalBidVolume { get; set; }
    [JsonPropertyName("sellMovingWeek")] public long MovingWeekSells { get; set; }
    [JsonPropertyName("sellOrders")] public int ActiveBidOrders { get; set; }

    // --- ASK SIDE (Hypixel "Buy" / Supply) ---
    [JsonPropertyName("buyPrice")] public double BestAskPrice { get; set; }
    [JsonPropertyName("buyVolume")] public int TotalAskVolume { get; set; }
    [JsonPropertyName("buyMovingWeek")] public long MovingWeekBuys { get; set; }
    [JsonPropertyName("buyOrders")] public int ActiveAskOrders { get; set; }
}
