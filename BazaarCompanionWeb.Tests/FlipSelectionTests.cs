using BazaarCompanionWeb.Dtos.Bot;
using BazaarCompanionWeb.Entities;
using BazaarCompanionWeb.Services;
using BazaarCompanionWeb.Services.Ingestion;
using BazaarCompanionWeb.Utilities;
using FluentAssertions;
using Xunit;

namespace BazaarCompanionWeb.Tests;

public class FlipSelectionTests
{
    private static List<FlipOpportunity> Select(IEnumerable<EFProduct> products, string[]? include = null,
        double? minPrice = null, double? maxPrice = null, double? minVolume = null, string sort = "throughput") =>
        FlipSelection.Select(products.Select(p => new ProductObservation(p, DateTime.UtcNow)),
            minPrice, maxPrice, minVolume, 25_000, true, 1, include ?? [], 0.05, 100_000, 45, sort, 10);

    [Fact]
    public void Nominated_products_obey_price_and_volume_limits()
    {
        var p = FlipQuotingTests.Product(1_000, 1_500);
        p.Meta.FlipOpportunityScore = 0;
        Select([p], [p.ProductKey]).Should().ContainSingle();
        Select([p], [p.ProductKey], minPrice: 1_001).Should().BeEmpty();
        Select([p], [p.ProductKey], maxPrice: 999).Should().BeEmpty();
        Select([p], [p.ProductKey], minVolume: 1_000_001).Should().BeEmpty();
        p.Meta.IsManipulated = true;
        Select([p], [p.ProductKey]).Should().BeEmpty();
    }

    [Fact]
    public void Throughput_selection_considers_products_beyond_the_old_database_shortlist()
    {
        var products = Enumerable.Range(0, 251).Select(i =>
        {
            var p = FlipQuotingTests.Product(1_000, 1_200);
            p.ProductKey = $"SLOW_{i}";
            p.Meta.FlipOpportunityScore = 100;
            return p;
        }).ToList();
        var fast = FlipQuotingTests.Product(1_000, 2_000, 50_400_000, 50_400_000);
        fast.ProductKey = "FAST";
        fast.Meta.FlipOpportunityScore = 2;
        products.Add(fast);
        Select(products).First().ProductKey.Should().Be("FAST");
    }

    [Fact]
    public void The_returned_quote_and_filters_use_the_same_observation()
    {
        var p = FlipQuotingTests.Product(1_000, 1_200);
        Select([p], maxPrice: 1_500).Should().ContainSingle();
        p.Bid.UnitPrice = 1_600;
        p.Ask.UnitPrice = 2_000;
        Select([p], maxPrice: 1_500).Should().BeEmpty();
    }

    [Fact]
    public void Version_two_rates_reproduce_the_quoted_round_trip_and_preserve_evidence()
    {
        var p = FlipQuotingTests.Product(1_000, 1_500);
        p.Meta.EvidenceLimited = true;
        var q = Select([p]).Single();
        q.QuoteVersion.Should().Be(2);
        ((q.TopBidDepth + q.SuggestedQuantity) / q.BuyUnitsPerMinute
            + (q.TopAskDepth + q.SuggestedQuantity) / q.SellUnitsPerMinute)
            .Should().BeApproximately(q.EstimatedRoundTripMinutes, 0.0001);
        ProductMapping.ToInfo(p).EvidenceLimited.Should().BeTrue();
    }
}
