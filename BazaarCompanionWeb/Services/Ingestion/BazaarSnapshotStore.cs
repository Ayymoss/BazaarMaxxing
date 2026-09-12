using System.Collections.Concurrent;
using BazaarCompanionWeb.Dtos;
using BazaarCompanionWeb.Entities;
using BazaarCompanionWeb.Models;
using BazaarCompanionWeb.Utilities;

namespace BazaarCompanionWeb.Services.Ingestion;

/// <summary>
/// Singleton RAM store for Bazaar ingestion. Holds latest product state, per-product tick
/// ring buffers, the forming five-minute bars, and cached scores. Acts as the source feed for
/// FlushService.
///
/// Crash-loss is accepted: there is no WAL. New polls overwrite RAM; flushes drain deltas
/// to the DB every ~10 min.
/// </summary>
public sealed class BazaarSnapshotStore
{
    /// <summary>Per-product tick ring size: 24h @ 1min poll. A ring at capacity has wrapped and lost its oldest samples.</summary>
    public const int TickRingCapacity = 1440;

    /// <summary>Granularity of the bars handed to the flusher — the smallest chart interval.</summary>
    public const CandleInterval BarInterval = CandleInterval.FiveMinute;

    /// <summary>
    /// (product, side) pairs whose moving-week counter went DOWN in one poll before the poll is treated as a
    /// window-expiry burst. Measured 2026-09-11: outside a burst at most ~6 pairs per 20s feed update go down
    /// (≤ ~18 per one-minute poll); inside one, 300–1000+ per update.
    /// </summary>
    private const int BurstNegativeThreshold = 40;

    private readonly Lock _diffLock = new();

    private readonly ConcurrentDictionary<string, EFProduct> _latestProducts = new();
    // Full live product (incl. order books) so the web UI can read current state from RAM, not the DB.
    private readonly ConcurrentDictionary<string, ProductData> _latestData = new();
    private readonly ConcurrentDictionary<string, ProductState> _latestState = new();
    private readonly ConcurrentDictionary<string, Observation> _observed = new();
    private readonly ConcurrentDictionary<string, RingBuffer<TickSample>> _ticks = new();
    private readonly ConcurrentDictionary<string, CachedScores> _scores = new();

    // Units traded per product in the most recent poll, and the most recent figure not taken during a burst
    // (the stand-in for a negative delta inside one).
    private readonly ConcurrentDictionary<string, TradedDelta> _lastTraded = new();
    private readonly ConcurrentDictionary<string, TradedDelta> _lastCleanTraded = new();

    // Forming five-minute bars and the completed ones awaiting a flush. Both guarded by _diffLock.
    private readonly Dictionary<string, PendingBar> _openBars = new();
    private readonly List<EFPriceTick> _closedBars = [];

    // Products that changed since the last DrainForFlush. Reset after drain.
    private readonly ConcurrentDictionary<string, byte> _dirtySinceFlush = new();

    /// <summary>
    /// When the last poll landed in memory. This — not the database — is how current the served prices are:
    /// ingest runs every minute, while the DB flush is batched, so a row's LastSeenAt lags by minutes even
    /// when the numbers being served are seconds old. Anything reporting freshness to a trading client has to
    /// use this, or the client sees staleness that is not there.
    /// </summary>
    public DateTime LastIngestUtc { get; private set; } = DateTime.MinValue;

    /// <summary>The time Hypixel stamped on the last poll's snapshot - the market's own clock, not ours.</summary>
    public DateTime LastUpstreamUtc { get; private set; } = DateTime.MinValue;

    /// <summary>When the first poll landed — the ring buffers hold nothing from before this.</summary>
    public DateTime FirstIngestUtc { get; private set; } = DateTime.MaxValue;

    /// <summary>What the last poll's volume derivation saw — for the poll log line.</summary>
    public IngestDiagnostics LastIngest { get; private set; } = new(0, false, 0, 0);

    /// <summary>
    /// Every product key Hypixel returned in the last poll — i.e. everything that still exists, whether or not
    /// its numbers moved. Presence and change are different questions, and the stale-product sweep needs the
    /// former.
    /// </summary>
    public IReadOnlyCollection<string> KnownProductKeys => _latestState.Keys.ToList();

    /// <summary>
    /// Splat one poll's worth of data into the store. Computes change-detection internally
    /// against the previous in-memory state. Returns the list of changed product keys so
    /// callers can decide whether to recompute scores etc.
    /// </summary>
    public IReadOnlyList<string> Ingest(
        IReadOnlyList<ProductData> products,
        IReadOnlyList<EFProduct> mapped,
        IReadOnlyDictionary<string, CachedScores> scores,
        DateTime timestamp,
        DateTime? upstreamUtc = null)
    {
        if (products.Count != mapped.Count)
            throw new ArgumentException("products and mapped must have matching length");

        var changed = new List<string>();
        var firstRun = _latestState.IsEmpty;
        LastIngestUtc = timestamp;
        LastUpstreamUtc = upstreamUtc ?? timestamp;
        if (firstRun) FirstIngestUtc = timestamp;
        var observation = new Observation(timestamp, LastUpstreamUtc);

        lock (_diffLock)
        {
            // Pass 1 — counter deltas for every product, so the burst decision is made on the whole poll
            // before any product is scored. A null delta means no previous state (first sighting).
            var deltas = new (long Buy, long Sell)?[products.Count];
            var negatives = 0;
            for (var i = 0; i < products.Count; i++)
            {
                var product = products[i];
                if (!_latestState.TryGetValue(product.ItemId, out var prev)) continue;
                var dBuy = (long)product.Ask.WeekVolume - prev.MovingWeekBuys;
                var dSell = (long)product.Bid.WeekVolume - prev.MovingWeekSells;
                deltas[i] = (dBuy, dSell);
                if (dBuy < 0) negatives++;
                if (dSell < 0) negatives++;
            }
            var burst = negatives >= BurstNegativeThreshold;
            long tradedUnits = 0;
            var estimated = 0;

            // Pass 2 — state, ticks, bars.
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

                var hadPrev = _latestState.TryGetValue(key, out var prev);
                var stateChanged = firstRun || !hadPrev || !StateEquals(prev!, newState);

                var traded = DeriveTraded(key, deltas[i], burst);
                _lastTraded[key] = traded;
                if (!traded.Estimated) _lastCleanTraded[key] = traded;
                tradedUnits += traded.Total;
                if (traded.Estimated) estimated++;

                _latestState[key] = newState;
                _latestProducts[key] = efProduct;
                _latestData[key] = product;
                _observed[key] = observation;

                if (stateChanged)
                {
                    changed.Add(key);
                    _dirtySinceFlush[key] = 0;

                    var ring = _ticks.GetOrAdd(key, _ => new RingBuffer<TickSample>(TickRingCapacity));
                    ring.Add(new TickSample(
                        product.Bid.OrderPrice,
                        product.Ask.OrderPrice,
                        product.Bid.CurrentVolume,
                        product.Ask.CurrentVolume,
                        traded.Buy,
                        traded.Sell,
                        timestamp,
                        traded.Estimated));

                    UpdateBar(key, product, hadPrev ? prev : null, traded, timestamp);
                }
            }

            LastIngest = new IngestDiagnostics(negatives, burst, estimated, tradedUnits);
        }

        foreach (var (key, value) in scores)
            _scores[key] = value;

        return changed;
    }

    /// <summary>
    /// Turn a poll's counter deltas into units traded. Outside a burst a negative delta is one product's own
    /// window expiring (the trickle) and is clamped to zero. Inside one the true increment is unrecoverable
    /// (delta = trades − expired), so the figure is the larger of the delta and the product's last clean
    /// minute: a negative delta becomes that minute, and a positive one is a lower bound that the previous
    /// minute usually beats. Simulated on the 2026-09-11 capture, the first poll of a burst reports ~275k
    /// units bazaar-wide against a normal ~1.6M, so taking the delta alone would lose most of that minute.
    /// </summary>
    private TradedDelta DeriveTraded(string key, (long Buy, long Sell)? delta, bool burst)
    {
        if (delta is not { } d) return TradedDelta.Zero;

        if (!burst)
            return new TradedDelta(Math.Max(0, d.Buy), Math.Max(0, d.Sell), d.Buy < 0 || d.Sell < 0);

        var clean = _lastCleanTraded.GetValueOrDefault(key, TradedDelta.Zero);
        return new TradedDelta(Math.Max(d.Buy, clean.Buy), Math.Max(d.Sell, clean.Sell), true);
    }

    /// <summary>Fold one changed sample into the product's forming five-minute bar, rolling the bar over when the bucket moved.</summary>
    private void UpdateBar(string key, ProductData product, ProductState? prev, TradedDelta traded, DateTime timestamp)
    {
        var bucket = timestamp.GetPeriodStart(BarInterval);

        if (_openBars.TryGetValue(key, out var bar) && bar.Bucket < bucket)
        {
            _closedBars.Add(bar.ToEntity());
            _openBars.Remove(key);
            bar = null;
        }

        if (bar is null)
        {
            // A bar opens at the price the product had when the bucket started — the last known price — so
            // consecutive candles join up. A product seen for the first time opens at its own sample.
            bar = new PendingBar(key, bucket,
                prev?.BidOrderPrice ?? product.Bid.OrderPrice,
                prev?.AskOrderPrice ?? product.Ask.OrderPrice);
            _openBars[key] = bar;
        }

        bar.Apply(product.Bid.OrderPrice, product.Ask.OrderPrice,
            product.Bid.CurrentVolume, product.Ask.CurrentVolume, traded);
    }

    public CachedScores? GetCachedScores(string productKey) =>
        _scores.TryGetValue(productKey, out var s) ? s : null;

    /// <summary>Units traded for a product in the most recent poll (zero when it has only been seen once).</summary>
    public TradedDelta GetLastTraded(string productKey) =>
        _lastTraded.GetValueOrDefault(productKey, TradedDelta.Zero);

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

    /// <summary>When this product was last seen, or null if it never has been.</summary>
    public Observation? ObservationOf(string productKey) =>
        _observed.TryGetValue(productKey, out var o) ? o : null;

    /// <summary>The full live product (incl. order books) for a key, or null if not yet polled.</summary>
    public ProductData? GetLatestData(string productKey) =>
        _latestData.TryGetValue(productKey, out var d) ? d : null;

    /// <summary>Snapshot of every product's latest mapped entity — the read source for the product list.</summary>
    public IReadOnlyList<EFProduct> GetAllProducts() => _latestProducts.Values.ToList();

    /// <summary>True once at least one poll has populated the store.</summary>
    public bool HasData => !_latestProducts.IsEmpty;

    public IReadOnlyList<TickSample> GetTicksSince(string productKey, DateTime cutoff) =>
        _ticks.TryGetValue(productKey, out var ring)
            ? ring.SnapshotSince(cutoff, t => t.Timestamp)
            : [];

    public IReadOnlyList<TickSample> GetTicksSnapshot(string productKey) =>
        _ticks.TryGetValue(productKey, out var ring) ? ring.Snapshot() : [];

    /// <summary>
    /// Drains all changes accumulated since the last call. Clears the dirty set. Bars are every completed
    /// five-minute bar plus a snapshot of each forming bar touched since the last drain; the flusher upserts
    /// by (product, bucket), so a forming bar is simply overwritten by its later, fuller self.
    /// </summary>
    public SnapshotDrainResult DrainForFlush()
    {
        lock (_diffLock)
        {
            var dirtyKeys = _dirtySinceFlush.Keys.ToList();
            _dirtySinceFlush.Clear();

            var products = new List<EFProduct>(dirtyKeys.Count);
            foreach (var key in dirtyKeys)
            {
                if (_latestProducts.TryGetValue(key, out var product))
                    products.Add(product);
            }

            var bars = new List<EFPriceTick>(_closedBars);
            _closedBars.Clear();

            var currentBucket = DateTime.UtcNow.GetPeriodStart(BarInterval);
            foreach (var (key, bar) in _openBars.ToList())
            {
                if (bar.Dirty)
                {
                    bars.Add(bar.ToEntity());
                    bar.Dirty = false;
                }
                // A bar whose bucket has passed cannot change again; drop it so a quiet product does not
                // hold its last bar open for ever.
                if (bar.Bucket < currentBucket)
                    _openBars.Remove(key);
            }

            return new SnapshotDrainResult(products, bars);
        }
    }

    // Matches BazaarRunCache.Equals — price + weekly volume only.
    private static bool StateEquals(ProductState a, ProductState b) =>
        a.ProductKey == b.ProductKey
        && a.BidOrderPrice.Equals(b.BidOrderPrice)
        && a.AskOrderPrice.Equals(b.AskOrderPrice)
        && a.MovingWeekSells == b.MovingWeekSells
        && a.MovingWeekBuys == b.MovingWeekBuys;

    /// <summary>A five-minute bar under construction from the one-minute samples inside its bucket.</summary>
    private sealed class PendingBar(string productKey, DateTime bucket, double bidOpen, double askOpen)
    {
        public DateTime Bucket { get; } = bucket;
        public bool Dirty { get; set; }

        private double _bidHigh = bidOpen, _bidLow = bidOpen, _bidClose = bidOpen;
        private double _askHigh = askOpen, _askLow = askOpen, _askClose = askOpen;
        private long _bidDepth, _askDepth, _tradedBuy, _tradedSell, _tradedEstimated;

        public void Apply(double bid, double ask, long bidDepth, long askDepth, TradedDelta traded)
        {
            _bidHigh = Math.Max(_bidHigh, bid);
            _bidLow = Math.Min(_bidLow, bid);
            _bidClose = bid;
            _askHigh = Math.Max(_askHigh, ask);
            _askLow = Math.Min(_askLow, ask);
            _askClose = ask;
            _bidDepth = bidDepth;
            _askDepth = askDepth;
            _tradedBuy += traded.Buy;
            _tradedSell += traded.Sell;
            if (traded.Estimated) _tradedEstimated += traded.Total;
            Dirty = true;
        }

        public EFPriceTick ToEntity() => new()
        {
            ProductKey = productKey,
            Timestamp = Bucket,
            BidOpen = bidOpen,
            BidHigh = _bidHigh,
            BidLow = _bidLow,
            BidPrice = _bidClose,
            AskOpen = askOpen,
            AskHigh = _askHigh,
            AskLow = _askLow,
            AskPrice = _askClose,
            BidVolume = _bidDepth,
            AskVolume = _askDepth,
            TradedBuy = _tradedBuy,
            TradedSell = _tradedSell,
            TradedEstimated = _tradedEstimated,
        };
    }
}

/// <summary>
/// What one poll's volume derivation saw. <paramref name="Negatives"/> is (product, side) pairs whose counter
/// went down; <paramref name="Burst"/> is whether that crossed the expiry-burst threshold;
/// <paramref name="Estimated"/> is products whose figure was clamped or substituted;
/// <paramref name="TradedUnits"/> is the total derived across the whole bazaar.
/// </summary>
public readonly record struct IngestDiagnostics(int Negatives, bool Burst, int Estimated, long TradedUnits);
