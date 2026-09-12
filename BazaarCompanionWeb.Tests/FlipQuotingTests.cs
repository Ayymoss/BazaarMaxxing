using BazaarCompanionWeb.Entities;
using BazaarCompanionWeb.Models.Api.Items;
using BazaarCompanionWeb.Services;
using FluentAssertions;
using Xunit;

namespace BazaarCompanionWeb.Tests;

public class FlipQuotingTests
{
    internal static EFProduct Product(double bid, double ask, double bidWeek = 500_000, double askWeek = 500_000,
        int topBidDepth = 100, int topAskDepth = 100) => new()
    {
        ProductKey = "ENCHANTED_SLIME_BALL",
        FriendlyName = "Enchanted Slimeball",
        Tier = ItemTier.Uncommon,
        Unstackable = false,
        Bid = new EFBidMarketData
        {
            ProductKey = "ENCHANTED_SLIME_BALL", UnitPrice = bid, OrderVolumeWeek = bidWeek, OrderVolume = 10_000,
            OrderCount = 50, BookValue = $"[{{\"UnitPrice\":{bid},\"Orders\":1,\"Amount\":{topBidDepth}}}]"
        },
        Ask = new EFAskMarketData
        {
            ProductKey = "ENCHANTED_SLIME_BALL", UnitPrice = ask, OrderVolumeWeek = askWeek, OrderVolume = 10_000,
            OrderCount = 50, BookValue = $"[{{\"UnitPrice\":{ask},\"Orders\":1,\"Amount\":{topAskDepth}}}]"
        },
        Meta = new EFProductMeta
        {
            ProductKey = "ENCHANTED_SLIME_BALL", ProfitMultiplier = ask / bid, Spread = ask - bid,
            TotalWeekVolume = bidWeek + askWeek, FlipOpportunityScore = 5, IsManipulated = false,
            ManipulationIntensity = 0, PriceDeviationPercent = 0
        },
        Snapshots = []
    };

    /// <summary>
    /// The margin is quoted after THE CALLER'S tax. It was a constant 1.125% - the perk-maxed rate - and a
    /// fresh account pays 5%: at a 20,000 ask that is 775 coins a unit the quote did not have. Audit
    /// 2026-09-12, finding 13.
    /// </summary>
    [Fact]
    public void The_margin_is_quoted_after_the_callers_tax()
    {
        var quote = FlipQuoting.Quote(Product(bid: 18_000, ask: 20_000), taxRate: 0.05, budget: 1_000_000, maxFillMinutes: 30);

        quote.EstimatedProfitPerUnit.Should().BeApproximately(20_000 * 0.95 - 18_000, 0.01);
        quote.SuggestedProfit.Should().BeApproximately(quote.SuggestedQuantity * (20_000 * 0.95 - 18_000), 0.01);
    }

    [Fact]
    public void Without_a_stated_tax_the_perkless_rate_is_assumed()
    {
        FlipQuoting.PerklessTaxRate.Should().Be(0.05);
        FlipQuoting.TaxRateFor(null).Should().Be(0.05);
        FlipQuoting.TaxRateFor(0.01125).Should().Be(0.01125);
        FlipQuoting.TaxRateFor(0.9).Should().Be(0.05, "a rate no account pays is a bad request, not a discovery");
    }
}
