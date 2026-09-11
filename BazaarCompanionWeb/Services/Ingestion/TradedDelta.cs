namespace BazaarCompanionWeb.Services.Ingestion;

/// <summary>
/// Units traded for a product between two consecutive polls, derived from the increments of Hypixel's
/// <c>buyMovingWeek</c> (instant buys — asks consumed) and <c>sellMovingWeek</c> (instant sells — bids
/// consumed). The counters only move on fills, so cancelled orders never appear here; that is what makes
/// them usable where order-book depth deltas are not.
///
/// The counters are rolling seven-day windows and Hypixel expires old trades in a batch roughly once an
/// hour. During such a poll a positive increment is a lower bound (trades minus expired) and a negative one
/// is meaningless; <see cref="Estimated"/> marks samples taken during a detected expiry burst, where the
/// figure is the larger of the delta and the product's previous clean poll.
/// </summary>
public readonly record struct TradedDelta(long Buy, long Sell, bool Estimated)
{
    public static readonly TradedDelta Zero = new(0, 0, false);
    public long Total => Buy + Sell;
}
