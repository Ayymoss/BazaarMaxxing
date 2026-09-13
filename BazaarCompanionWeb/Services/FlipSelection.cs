using BazaarCompanionWeb.Dtos.Bot;
using BazaarCompanionWeb.Services.Ingestion;

namespace BazaarCompanionWeb.Services;

public static class FlipSelection
{
    // Include nominates proven products below the score threshold, but never bypasses hard limits.
    public static List<FlipOpportunity> Select(IEnumerable<ProductObservation> snapshot,
        double? minPrice, double? maxPrice, double? minVolume, double askVolumeFloor,
        bool excludeManipulated, double minScore, IEnumerable<string> include,
        double taxRate, double? budget, double? maxFillMinutes, string? sort, int limit)
    {
        var nominated = include.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var tradable = FlipQuoting.Tradable(askVolumeFloor, excludeManipulated).Compile();
        var quotes = snapshot.Where(x => tradable(x.Product))
            .Where(x => minPrice is null || x.Product.Bid.UnitPrice >= minPrice)
            .Where(x => maxPrice is null || x.Product.Bid.UnitPrice <= maxPrice)
            .Where(x => minVolume is null || x.Product.Meta.TotalWeekVolume >= minVolume)
            .Where(x => x.Product.Meta.FlipOpportunityScore >= minScore || nominated.Contains(x.Product.ProductKey))
            .Select(x => FlipQuoting.Quote(x.Product, taxRate, budget, maxFillMinutes, x.UpstreamUtc))
            .Where(x => x.EstimatedProfitPerUnit > 0)
            .Where(x => maxFillMinutes is null || x.EstimatedRoundTripMinutes <= maxFillMinutes)
            .Where(x => budget is null || x.SuggestedQuantity > 0);
        return (sort?.ToLowerInvariant() switch
        {
            "fill" => quotes.OrderBy(x => x.EstimatedRoundTripMinutes),
            "profit" => quotes.OrderByDescending(x => x.EstimatedProfitPerUnit),
            "throughput" => quotes.OrderByDescending(FlipQuoting.Throughput),
            _ => quotes.OrderByDescending(x => x.OpportunityScore)
        }).ThenBy(x => x.ProductKey).Take(limit).ToList();
    }
}
