using BazaarCompanionWeb.Dtos;
using BazaarCompanionWeb.Models.Api.Bazaar;
using FluentAssertions;
using Xunit;

namespace BazaarCompanionWeb.Tests;

public class ScoringProductInputTests
{
    private static Product Feed(double? bestAsk, double? bestBid) => new()
    {
        ProductId = "ENCHANTED_SLIME_BALL",
        Asks = bestAsk is { } a ? [new OrderBookEntry { PricePerUnit = a, Amount = 10, OrderCount = 1 }] : [],
        Bids = bestBid is { } b ? [new OrderBookEntry { PricePerUnit = b, Amount = 10, OrderCount = 1 }] : [],
        Ticker = new MarketTicker { ProductId = "ENCHANTED_SLIME_BALL" }
    };

    [Fact]
    public void The_order_prices_are_the_top_of_each_side()
    {
        var input = ScoringProductInput.From(Feed(bestAsk: 1_500, bestBid: 1_300));

        input.AskPrice.Should().Be(1_500);
        input.BidPrice.Should().Be(1_300);
    }

    /// <summary>
    /// Instant selling fills our BUY orders and moves sellMovingWeek; instant buying fills our SELL offers and
    /// moves buyMovingWeek. The scorer was handed them the other way round, so a product with 100,000 of
    /// exit-side demand and 20,000 of entry-side supply was gated as if it had 20,000 of demand - and
    /// rejected for the very strength that made it worth flipping. Audit 2026-09-12, finding 2.
    /// </summary>
    [Fact]
    public void The_volume_sides_follow_what_fills_our_orders()
    {
        var product = Feed(bestAsk: 1_500, bestBid: 1_300);
        product.Ticker.MovingWeekBuys = 100_000;
        product.Ticker.MovingWeekSells = 20_000;

        var input = ScoringProductInput.From(product);

        input.AskMovingWeek.Should().Be(100_000, "instant buys consume asks: that is the volume our sell offers see");
        input.BidMovingWeek.Should().Be(20_000, "instant sells consume bids: that is the volume our buy orders see");
    }

    [Fact]
    public void An_empty_side_is_priced_a_tick_off_the_other()
    {
        ScoringProductInput.From(Feed(bestAsk: null, bestBid: 1_300)).AskPrice.Should().BeApproximately(1_300.1, 1e-6);
        ScoringProductInput.From(Feed(bestAsk: 1_500, bestBid: null)).BidPrice.Should().BeApproximately(1_499.9, 1e-6);
    }
}
