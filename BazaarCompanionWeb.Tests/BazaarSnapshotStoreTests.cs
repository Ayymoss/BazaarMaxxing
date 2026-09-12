using BazaarCompanionWeb.Dtos;
using BazaarCompanionWeb.Models;
using BazaarCompanionWeb.Models.Api.Items;
using BazaarCompanionWeb.Services.Ingestion;
using FluentAssertions;
using Xunit;

namespace BazaarCompanionWeb.Tests;

public class BazaarSnapshotStoreTests
{
    private static readonly DateTime T0 = new(2026, 9, 12, 12, 0, 0, DateTimeKind.Utc);

    private static ProductData Product(string key, double bid = 1_000, double ask = 1_200) => new()
    {
        ItemId = key,
        Item = new Models.Item { FriendlyName = key, Tier = ItemTier.Common, Unstackable = false },
        Bid = new OrderInfo { Last = bid, OrderPrice = bid, WeekVolume = 100_000, CurrentOrders = 1, CurrentVolume = 10, OrderBook = [] },
        Ask = new OrderInfo { Last = ask, OrderPrice = ask, WeekVolume = 100_000, CurrentOrders = 1, CurrentVolume = 10, OrderBook = [] },
        OrderMeta = new OrderMeta { PotentialProfitMultiplier = ask / bid, Spread = ask - bid, FlipOpportunityScore = 1 }
    };

    private static void Ingest(BazaarSnapshotStore store, DateTime at, DateTime upstream, params ProductData[] products)
    {
        var mapped = products.Select(p => p.Map()).ToList();
        store.Ingest(products, mapped, new Dictionary<string, CachedScores>(), at, upstream);
    }

    /// <summary>
    /// A product's age is its own. The detail endpoint stamped every product with the last poll's time, so
    /// one that had dropped out of the feed looked as fresh as the poll that no longer carried it. Audit
    /// 2026-09-12, finding 3.
    /// </summary>
    [Fact]
    public void A_product_missing_from_the_latest_poll_keeps_the_observation_it_had()
    {
        var store = new BazaarSnapshotStore();
        Ingest(store, T0, T0.AddSeconds(-20), Product("A"), Product("B"));
        Ingest(store, T0.AddMinutes(1), T0.AddMinutes(1).AddSeconds(-20), Product("A"));

        store.LastIngestUtc.Should().Be(T0.AddMinutes(1));
        store.ObservationOf("A").Should().Be(new Observation(T0.AddMinutes(1), T0.AddMinutes(1).AddSeconds(-20)));
        store.ObservationOf("B").Should().Be(new Observation(T0, T0.AddSeconds(-20)),
            "B was last seen a minute ago, whatever landed since");
        store.ObservationOf("C").Should().BeNull();
    }

    [Fact]
    public void The_upstream_time_is_the_markets_clock_not_ours()
    {
        var store = new BazaarSnapshotStore();
        Ingest(store, T0, T0.AddSeconds(-45), Product("A"));

        store.LastUpstreamUtc.Should().Be(T0.AddSeconds(-45));
    }
}
