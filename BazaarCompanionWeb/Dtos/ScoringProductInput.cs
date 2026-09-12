using BazaarCompanionWeb.Models.Api.Bazaar;

namespace BazaarCompanionWeb.Dtos;

/// <summary>
/// Input for batch opportunity scoring. One entry per product; order is preserved in batch results.
/// </summary>
public record ScoringProductInput(
    string ProductKey,
    double BidPrice,
    double AskPrice,
    long BidMovingWeek,
    long AskMovingWeek,
    int BidOrders,
    int AskOrders,
    int BidVolume,
    int AskVolume)
{
    /// <summary>The scorer's view of one product as the Hypixel feed describes it.</summary>
    public static ScoringProductInput From(Product product)
    {
        var ask = product.Asks.FirstOrDefault();
        var bid = product.Bids.FirstOrDefault();
        var askOrderPrice = ask?.PricePerUnit ?? bid?.PricePerUnit + 0.1 ?? 0.1f;
        var bidOrderPrice = bid?.PricePerUnit ?? ask?.PricePerUnit - 0.1 ?? 0.1f;
        return new ScoringProductInput(product.ProductId, bidOrderPrice, askOrderPrice,
            product.Ticker.MovingWeekBuys, product.Ticker.MovingWeekSells,
            product.Ticker.ActiveBidOrders, product.Ticker.ActiveAskOrders,
            product.Ticker.TotalBidVolume, product.Ticker.TotalAskVolume);
    }
}
