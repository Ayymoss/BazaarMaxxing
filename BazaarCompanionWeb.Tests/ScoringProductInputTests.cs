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

    [Fact]
    public void An_empty_side_is_priced_a_tick_off_the_other()
    {
        ScoringProductInput.From(Feed(bestAsk: null, bestBid: 1_300)).AskPrice.Should().BeApproximately(1_300.1, 1e-6);
        ScoringProductInput.From(Feed(bestAsk: 1_500, bestBid: null)).BidPrice.Should().BeApproximately(1_499.9, 1e-6);
    }
}
