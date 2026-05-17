using System.Collections.Concurrent;
using BazaarCompanionWeb.Dtos;
using BazaarCompanionWeb.Entities;
using BazaarCompanionWeb.Models;

namespace BazaarCompanionWeb.Services.Ingestion;

/// <summary>
/// Singleton RAM store for Bazaar ingestion. Holds latest product state, per-product tick
/// ring buffers, and cached scores. Replaces BazaarRunCache + acts as the source feed for
/// FlushService.
///
/// Crash-loss is accepted: there is no WAL. New polls overwrite RAM; flushes drain deltas
/// to the DB every ~10 min.
/// </summary>
public sealed class BazaarSnapshotStore
{
    private const int TickRingCapacity = 1440; // 24h @ 1min poll

    private readonly Lock _diffLock = new();

    private readonly ConcurrentDictionary<string, EFProduct> _latestProducts = new();
    private readonly ConcurrentDictionary<string, ProductState> _latestState = new();
    private readonly ConcurrentDictionary<string, RingBuffer<TickSample>> _ticks = new();
    private readonly ConcurrentDictionary<string, CachedScores> _scores = new();

    // Products that changed since the last DrainForFlush. Reset after drain.
    private readonly ConcurrentDictionary<string, byte> _dirtySinceFlush = new();

    /// <summary>
    /// Splat one poll's worth of data into the store. Computes change-detection internally
    /// against the previous in-memory state. Returns the list of changed product keys so
    /// callers can decide whether to recompute scores etc.
    /// </summary>
    public IReadOnlyList<string> Ingest(
        IReadOnlyList<ProductData> products,
        IReadOnlyList<EFProduct> mapped,
        IReadOnlyDictionary<string, CachedScores> scores,
        DateTime timestamp)
    {
        if (products.Count != mapped.Count)
            throw new ArgumentException("products and mapped must have matching length");

        var changed = new List<string>();
        var firstRun = _latestState.IsEmpty;

        lock (_diffLock)
        {
            for (var i = 0; i < products.Count; i++)
            {
                var product = products[i];
                var efProduct = mapped[i];
                var key = product.ItemId;

                var newState = new ProductState(
                    key,
                    product.Bid.OrderPrice,
                    product.Ask.OrderPrice,
                    (long)product.Bid.WeekVolume,
                    (long)product.Ask.WeekVolume,
                    product.Bid.CurrentVolume,
                    product.Ask.CurrentVolume);

                var stateChanged = firstRun
                    || !_latestState.TryGetValue(key, out var prev)
                    || !StateEquals(prev, newState);

                _latestState[key] = newState;
                _latestProducts[key] = efProduct;

                if (stateChanged)
                {
                    changed.Add(key);
                    _dirtySinceFlush[key] = 0;

                    var ring = _ticks.GetOrAdd(key, _ => new RingBuffer<TickSample>(TickRingCapacity));
                    ring.Add(new TickSample(
                        product.Bid.OrderPrice,
                        product.Ask.OrderPrice,
                        (long)product.Bid.CurrentVolume,
                        (long)product.Ask.CurrentVolume,
                        timestamp));
                }
            }
        }

        foreach (var (key, value) in scores)
            _scores[key] = value;

        return changed;
    }

    public CachedScores? GetCachedScores(string productKey) =>
        _scores.TryGetValue(productKey, out var s) ? s : null;

    /// <summary>
    /// Non-mutating diff. Returns product keys whose state differs from the stored
    /// state (or are new). On first run returns all keys. Replaces BazaarRunCache.GetChangedProductKeys.
    /// </summary>
    public IReadOnlyList<string> DetectChanges(IReadOnlyDictionary<string, ProductState> currentState)
    {
        if (_latestState.IsEmpty)
            return currentState.Keys.ToList();

        var changed = new List<string>();
        foreach (var (key, current) in currentState)
        {
            if (!_latestState.TryGetValue(key, out var prev) || !StateEquals(prev, current))
                changed.Add(key);
        }
        return changed;
    }

    public EFProduct? GetLatestProduct(string productKey) =>
        _latestProducts.TryGetValue(productKey, out var p) ? p : null;

    public IReadOnlyList<TickSample> GetTicksSince(string productKey, DateTime cutoff) =>
        _ticks.TryGetValue(productKey, out var ring)
            ? ring.SnapshotSince(cutoff, t => t.Timestamp)
            : [];

    public IReadOnlyList<TickSample> GetTicksSnapshot(string productKey) =>
        _ticks.TryGetValue(productKey, out var ring) ? ring.Snapshot() : [];

    /// <summary>
    /// Drains all changes accumulated since the last call. Clears the dirty set.
    /// </summary>
    public SnapshotDrainResult DrainForFlush()
    {
        lock (_diffLock)
        {
            if (_dirtySinceFlush.IsEmpty)
                return new SnapshotDrainResult([], []);

            var dirtyKeys = _dirtySinceFlush.Keys.ToList();
            _dirtySinceFlush.Clear();

            var products = new List<EFProduct>(dirtyKeys.Count);
            var ticks = new List<EFPriceTick>(dirtyKeys.Count);

            foreach (var key in dirtyKeys)
            {
                if (_latestProducts.TryGetValue(key, out var product))
                    products.Add(product);

                if (_ticks.TryGetValue(key, out var ring))
                {
                    var snap = ring.Snapshot();
                    if (snap.Count > 0)
                    {
                        var latest = snap[^1];
                        ticks.Add(new EFPriceTick
                        {
                            ProductKey = key,
                            BidPrice = latest.BidPrice,
                            AskPrice = latest.AskPrice,
                            BidVolume = latest.BidVolume,
                            AskVolume = latest.AskVolume,
                            Timestamp = latest.Timestamp,
                        });
                    }
                }
            }

            return new SnapshotDrainResult(products, ticks);
        }
    }

    // Matches BazaarRunCache.Equals — price + weekly volume only.
    private static bool StateEquals(ProductState a, ProductState b) =>
        a.ProductKey == b.ProductKey
        && a.BidOrderPrice.Equals(b.BidOrderPrice)
        && a.AskOrderPrice.Equals(b.AskOrderPrice)
        && a.MovingWeekSells == b.MovingWeekSells
        && a.MovingWeekBuys == b.MovingWeekBuys;
}
