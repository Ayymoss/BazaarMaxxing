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

    /// <summary>
    /// The gate every flip passes, and the one an included product passes too. Audit 2026-09-12, finding 4:
    /// a product pulled in on its record must never bypass the checks the rest are held to.
    /// </summary>
    [Fact]
    public void A_product_is_tradable_on_price_spread_and_ask_side_demand()
    {
        var tradable = FlipQuoting.Tradable(askVolumeFloor: 25_000, excludeManipulated: true).Compile();

        tradable(Product(bid: 18_000, ask: 20_000)).Should().BeTrue();
        tradable(Product(bid: 18_000, ask: 18_050)).Should().BeFalse("under 100 coins of spread");
        tradable(Product(bid: 50, ask: 400)).Should().BeFalse("under 100 coins a unit");
        tradable(Product(bid: 18_000, ask: 20_000, askWeek: 10_000)).Should().BeFalse("too little demand on the ask side");
        tradable(Product(bid: 18_000, ask: 20_000, bidWeek: 900_000, askWeek: 100_000)).Should().BeFalse("under 30% of the volume on the ask side");

        var rigged = Product(bid: 18_000, ask: 20_000);
        rigged.Meta.IsManipulated = true;
        tradable(rigged).Should().BeFalse();
        FlipQuoting.Tradable(25_000, excludeManipulated: false).Compile()(rigged).Should().BeTrue();
    }

    [Fact]
    public void A_quote_is_as_old_as_the_observation_it_was_given()
    {
        var quote = FlipQuoting.Quote(Product(bid: 18_000, ask: 20_000), 0.05, null, null,
            observedUtc: DateTime.UtcNow.AddSeconds(-90));

        quote.DataAgeSeconds.Should().BeApproximately(90, 2);
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
