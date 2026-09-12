using BazaarCompanionWeb.Dtos;
using BazaarCompanionWeb.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BazaarCompanionWeb.Tests;

public class OpportunityScoringServiceTests
{
    private static ScoringProductInput Input(string key, double bid, double ask) =>
        new(key, bid, ask, BidMovingWeek: 500_000, AskMovingWeek: 500_000, BidOrders: 10, AskOrders: 10,
            BidVolume: 1_000, AskVolume: 1_000);

    private static readonly IReadOnlyDictionary<string, List<OhlcDataPoint>> NoCandles = new Dictionary<string, List<OhlcDataPoint>>();

    /// <summary>
    /// Too little history is not a clean bill. The simplified score left the manipulation flag at its
    /// default, so "insufficient evidence" read as "not manipulated". Audit 2026-09-12, finding 14.
    /// </summary>
    [Fact]
    public void A_product_scored_on_too_little_history_says_so()
    {
        var scorer = new OpportunityScoringService(NullLogger<OpportunityScoringService>.Instance);

        var result = scorer.CalculateScoresBatch([Input("P1", 1_000, 1_500)], NoCandles);

        result[0].EvidenceLimited.Should().BeTrue();
        result[0].IsManipulated.Should().BeFalse("a default, which is why the flag beside it has to say so");
    }

    /// <summary>
    /// A score is a place in the whole market, not in whichever products moved this minute. Scored on its
    /// own after the first poll, a product used to come back with the top mark of a one-product batch.
    /// Audit 2026-09-12, finding 14.
    /// </summary>
    [Fact]
    public void A_product_scored_alone_keeps_its_place_in_the_universe()
    {
        var scorer = new OpportunityScoringService(NullLogger<OpportunityScoringService>.Instance);
        var universe = Enumerable.Range(1, 12).Select(i => Input($"P{i}", 1_000, 1_000 + 150 * i)).ToList();

        var first = scorer.CalculateScoresBatch(universe, NoCandles);
        var alone = scorer.CalculateScoresBatch([universe[2]], NoCandles);

        alone[0].OpportunityScore.Should().BeApproximately(first[2].OpportunityScore, 1e-9);
        alone[0].OpportunityScore.Should().BeLessThan(first[11].OpportunityScore, "the widest spread still ranks above it");
    }
}
