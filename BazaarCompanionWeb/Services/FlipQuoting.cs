using BazaarCompanionWeb.Dtos.Bot;
using BazaarCompanionWeb.Entities;
using BazaarCompanionWeb.Models;

namespace BazaarCompanionWeb.Services;

/// <summary>What the bot is told about one flip: prices, sizing and the fill-time estimates behind them.</summary>
public static class FlipQuoting
{
    public const double BazaarTaxRate = 0.01125;
    private const double MinutesPerWeek = 7 * 24 * 60;

    /// <summary>
    /// Weekly volume divided by minutes overstates how fast a queue actually drains: the volume is spread
    /// across every price level and every hour, while an order only ever consumes flow arriving at ITS level
    /// during the hours it is posted. Measured against a live order on ENCHANTED_SLIME_BALL (9.3M/week, so a
    /// naive 923 units/min) the observed rate was ~170 units/min — a fifth. Estimates are scaled by that
    /// rather than published as-is, because a bot deciding what it can finish deserves the pessimistic number.
    /// </summary>
    private const double QueueDrainFactor = 0.2;

    /// <summary>
    /// Stand-in for "not knowable" in fields that are otherwise a duration.
    ///
    /// Infinity is the mathematically honest answer for a queue that never drains, or the age of a snapshot
    /// that has never been taken — but System.Text.Json refuses to write non-finite numbers and throws, which
    /// turns one odd product into a 500 for the whole endpoint. A large finite value serialises, sorts to the
    /// bottom, and is comfortably past any threshold a caller would filter on. Roughly 28 hours in either
    /// unit, so it also reads as absurd rather than plausible.
    /// </summary>
    public const double Unknown = 99_999;

    /// <summary>Replaces anything JSON cannot represent — NaN and both infinities — with <see cref="Unknown"/>.</summary>
    public static double Serialisable(double value) => double.IsFinite(value) ? value : Unknown;

    private const int HypixelMaxOrderUnits = 71_680;

    // Depth at the best price on each side, read from the stored book. This is the queue a bot joins
    // when it posts at the top — and the reason a fat spread can still be untradable: 11,000 units
    // parked at the best bid is an hour of waiting, during which anyone can undercut by 0.1 and reset
    // the wait entirely.
    private static int TopDepth(IReadOnlyList<OrderBook> book) => book.Count > 0 ? book[0].Amount : 0;

    // How many units this budget can buy, bounded by what the market can absorb in the time the
    // caller is willing to wait.
    //
    // THE OLD BOUND MEASURED THE WRONG THING. It was a quarter of the depth at the single best ask
    // level, floored at one. Units queued at the best ask are OTHER SELLERS - competition, not
    // capacity - and one level is a few seconds of a busy book. A product whose best level happened
    // to hold four units came back as "buy 1", whatever its weekly turnover.
    //
    // Measured against a live bot on 2026-09-11: 22 of 36 positions opened committed under 200,000
    // coins, the median 122,573 against a per-position budget near 3,500,000. Hypercharge Chip at
    // 20,000 a unit was suggested at ONE unit - 20,001 coins - and that position then cost an order
    // slot and a cycle of repricing like any other. An account holding 35m had a fifth of it
    // deployed, because the suggestions it was given could not use the rest.
    //
    // Throughput is the honest bound and the file already computes it for FillMinutes: units the ask
    // side actually turns over per minute, times the minutes the caller said it would wait. Where
    // there is no volume to reason from, the budget decides and the caller's own slot rules cap it.
    private static int SuggestedUnits(double? budgetCoins, double bidPrice, double askWeekVolume, double waitMinutes)
    {
        if (budgetCoins is not { } coins || bidPrice <= 0) return 0;

        var affordable = (int)Math.Floor(coins / bidPrice);

        var absorbable = askWeekVolume > 0
            ? (int)Math.Floor(askWeekVolume / MinutesPerWeek * QueueDrainFactor * waitMinutes)
            : affordable;

        return Math.Clamp(Math.Min(affordable, absorbable), 0, HypixelMaxOrderUnits);
    }

    private static double FillMinutes(int depth, double weekVolume) =>
        weekVolume <= 0
            ? Unknown
            : Serialisable(depth / (weekVolume / MinutesPerWeek * QueueDrainFactor));

    public static FlipOpportunity Quote(EFProduct p, double taxRate, double? budget, double? maxFillMinutes)
    {
        // What the caller said it would wait for a round trip, halved because the suggestion sizes ONE
        // leg and a round trip is two. Defaulted rather than required: the parameter is optional, and a
        // caller that does not care still wants a size it can sell.
        var sizingWaitMinutes = Math.Max(1, (maxFillMinutes ?? 30) / 2.0);
        var units = SuggestedUnits(budget, p.Bid.UnitPrice, p.Ask.OrderVolumeWeek, sizingWaitMinutes);
        var profitPerUnit = (p.Ask.UnitPrice * (1 - taxRate)) - p.Bid.UnitPrice;

        return new FlipOpportunity(
            ProductKey: p.ProductKey,
            Name: p.FriendlyName,
            Tier: p.Tier,
            Unstackable: p.Unstackable,
            BestBidPrice: p.Bid.UnitPrice,
            BidOrders: p.Bid.OrderCount,
            BidVolume: p.Bid.OrderVolume,
            BidWeekVolume: p.Bid.OrderVolumeWeek,
            BestAskPrice: p.Ask.UnitPrice,
            AskOrders: p.Ask.OrderCount,
            AskVolume: p.Ask.OrderVolume,
            AskWeekVolume: p.Ask.OrderVolumeWeek,
            Spread: p.Meta.Spread,
            SpreadPercent: p.Bid.UnitPrice > 0 ? Serialisable(p.Meta.Spread / p.Bid.UnitPrice * 100) : 0,
            ProfitMultiplier: p.Meta.ProfitMultiplier,
            OpportunityScore: p.Meta.FlipOpportunityScore,
            EstimatedProfitPerUnit: Serialisable(profitPerUnit),
            TopBidDepth: TopDepth(p.Bid.Books),
            TopAskDepth: TopDepth(p.Ask.Books),
            EstimatedBuyFillMinutes: FillMinutes(TopDepth(p.Bid.Books), p.Bid.OrderVolumeWeek),
            EstimatedSellFillMinutes: FillMinutes(TopDepth(p.Ask.Books), p.Ask.OrderVolumeWeek),
            EstimatedRoundTripMinutes: FillMinutes(TopDepth(p.Bid.Books), p.Bid.OrderVolumeWeek)
                                       + FillMinutes(TopDepth(p.Ask.Books), p.Ask.OrderVolumeWeek),
            SuggestedQuantity: units,
            SuggestedCost: units * p.Bid.UnitPrice,
            SuggestedProfit: units * profitPerUnit,
            // Honest about what it is: these rows come from the database, which lags the live snapshot by
            // the flush interval. Screening on them is fine; pricing an order is not.
            DataAgeSeconds: Serialisable(Math.Max(0, (DateTime.UtcNow - p.LastSeenAt).TotalSeconds)),
            IsManipulated: p.Meta.IsManipulated,
            ManipulationIntensity: p.Meta.ManipulationIntensity,
            PriceDeviationPercent: p.Meta.PriceDeviationPercent);
    }
}
