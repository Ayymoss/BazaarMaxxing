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
        // Hypixel names the counters by what the TAKER did: buyMovingWeek is instant buys, which consume
        // asks, so it is the ASK side's volume - the units our sell offers compete to supply. Passed the other
        // way round, the scorer's ask-volume gate rejected products with strong exit-side demand.
        return new ScoringProductInput(product.ProductId, bidOrderPrice, askOrderPrice,
            BidMovingWeek: product.Ticker.MovingWeekSells,
            AskMovingWeek: product.Ticker.MovingWeekBuys,
            BidOrders: product.Ticker.ActiveBidOrders,
            AskOrders: product.Ticker.ActiveAskOrders,
            BidVolume: product.Ticker.TotalBidVolume,
            AskVolume: product.Ticker.TotalAskVolume);
    }
}
