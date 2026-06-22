using BazaarCompanionWeb.Dtos;
using BazaarCompanionWeb.Entities;
using BazaarCompanionWeb.Interfaces;
using BazaarCompanionWeb.Models.Pagination;
using BazaarCompanionWeb.Models.Pagination.MetaPaginations;
using BazaarCompanionWeb.Services;
using BazaarCompanionWeb.Services.Ingestion;
using BazaarCompanionWeb.Utilities;
using Microsoft.Extensions.Options;

namespace BazaarCompanionWeb.Queries;

/// <summary>
/// Serves the product list from the in-memory <see cref="BazaarSnapshotStore"/> (always current),
/// not the database. The DB is durable history only; the live snapshot is the read source here.
/// Filtering/sorting/paging run as LINQ-to-objects over a RAM snapshot.
/// </summary>
public class ProductsPaginationQueryHelper(
    BazaarSnapshotStore snapshotStore,
    IOptionsMonitor<Configuration> optionsMonitor)
    : IResourceQueryHelper<ProductPagination, ProductDataInfo>
{
    private readonly Configuration _configuration = optionsMonitor.CurrentValue;

    public Task<PaginationContext<ProductDataInfo>> QueryResourceAsync(ProductPagination request,
        CancellationToken cancellationToken)
    {
        var query = snapshotStore.GetAllProducts().AsQueryable();

        if (request.ToggleFilter)
            query = ApplyFilterQuery(query);

        if (request.AdvancedFilters != null)
            query = ApplyAdvancedFilters(query, request.AdvancedFilters);

        if (!string.IsNullOrWhiteSpace(request.Search))
            query = request.UseFuzzySearch
                ? ApplyFuzzySearchQuery(query, request.Search)
                : ApplySearchQuery(query, request.Search);

        if (request.Sorts.Any())
            query = ApplySortQuery(query, request.Sorts);

        return Task.FromResult(GetPagedData(request, query));
    }

    private IQueryable<EFProduct> ApplyFilterQuery(IQueryable<EFProduct> query)
    {
        return query.Where(x => x.Meta.Spread > _configuration.MinimumMargin)
            .Where(x => x.Meta.ProfitMultiplier > _configuration.MinimumPotentialProfitMultiplier)
            .Where(x => x.Bid.OrderVolumeWeek / x.Ask.OrderVolumeWeek > _configuration.MinimumBuyOrderPower)
            .Where(x => x.Bid.OrderVolumeWeek > _configuration.MinimumWeekVolume)
            .Where(x => x.Ask.OrderVolumeWeek > _configuration.MinimumWeekVolume);
    }

    private static IQueryable<EFProduct> ApplySearchQuery(IQueryable<EFProduct> query, string search)
    {
        var searchWords = search.Split(' ').Where(x => x.Length >= 3);
        return searchWords.Aggregate(query, (current, word) =>
            current.Where(product => product.FriendlyName.Contains(word, StringComparison.OrdinalIgnoreCase)));
    }

    private static IQueryable<EFProduct> ApplyFuzzySearchQuery(IQueryable<EFProduct> query, string search)
    {
        var searchWords = search.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(x => x.Length >= 2);
        return searchWords.Aggregate(query, (current, word) =>
            current.Where(product => product.FriendlyName.Contains(word, StringComparison.OrdinalIgnoreCase)));
    }

    private static IQueryable<EFProduct> ApplyAdvancedFilters(IQueryable<EFProduct> query, AdvancedFilterOptions filters)
    {
        if (filters.SelectedTiers.Any())
            query = query.Where(p => filters.SelectedTiers.Contains(p.Tier));

        if (filters.ManipulationStatus == ManipulationFilter.Manipulated)
            query = query.Where(p => p.Meta.IsManipulated);
        else if (filters.ManipulationStatus == ManipulationFilter.NotManipulated)
            query = query.Where(p => !p.Meta.IsManipulated);

        if (filters.MinPrice.HasValue)
            query = query.Where(p => p.Bid.UnitPrice >= filters.MinPrice.Value);
        if (filters.MaxPrice.HasValue)
            query = query.Where(p => p.Bid.UnitPrice <= filters.MaxPrice.Value);

        if (filters.MinSpread.HasValue)
            query = query.Where(p => p.Meta.Spread >= filters.MinSpread.Value);
        if (filters.MaxSpread.HasValue)
            query = query.Where(p => p.Meta.Spread <= filters.MaxSpread.Value);

        if (filters.MinVolume.HasValue)
            query = query.Where(p => p.Meta.TotalWeekVolume >= filters.MinVolume.Value);
        if (filters.MaxVolume.HasValue)
            query = query.Where(p => p.Meta.TotalWeekVolume <= filters.MaxVolume.Value);

        if (filters.VolumeTier != VolumeTierFilter.All)
        {
            query = filters.VolumeTier switch
            {
                VolumeTierFilter.Low => query.Where(p => p.Meta.TotalWeekVolume < 100_000),
                VolumeTierFilter.Medium => query.Where(p => p.Meta.TotalWeekVolume >= 100_000 && p.Meta.TotalWeekVolume <= 1_000_000),
                VolumeTierFilter.High => query.Where(p => p.Meta.TotalWeekVolume > 1_000_000),
                _ => query
            };
        }

        if (filters.MinOpportunityScore.HasValue)
            query = query.Where(p => p.Meta.FlipOpportunityScore >= filters.MinOpportunityScore.Value);
        if (filters.MaxOpportunityScore.HasValue)
            query = query.Where(p => p.Meta.FlipOpportunityScore <= filters.MaxOpportunityScore.Value);

        if (filters.MinProfitMultiplier.HasValue)
            query = query.Where(p => p.Meta.ProfitMultiplier >= filters.MinProfitMultiplier.Value);
        if (filters.MaxProfitMultiplier.HasValue)
            query = query.Where(p => p.Meta.ProfitMultiplier <= filters.MaxProfitMultiplier.Value);

        if (filters.MinOrderCount.HasValue)
            query = query.Where(p => p.Bid.OrderCount >= filters.MinOrderCount.Value || p.Ask.OrderCount >= filters.MinOrderCount.Value);
        if (filters.MaxOrderCount.HasValue)
            query = query.Where(p => p.Bid.OrderCount <= filters.MaxOrderCount.Value && p.Ask.OrderCount <= filters.MaxOrderCount.Value);

        return query;
    }

    private static IQueryable<EFProduct> ApplySortQuery(IQueryable<EFProduct> query, IEnumerable<SortDescriptor> sorts)
    {
        var sortDescriptors = sorts as SortDescriptor[] ?? sorts.ToArray();

        query = sortDescriptors.Aggregate(query, (current, sort) => sort.Property switch
        {
            nameof(ProductDataInfo.ItemFriendlyName) => current.ApplySortForName(sort, p => p.FriendlyName, p => p.Tier),
            nameof(ProductDataInfo.BidUnitPrice) => current.ApplySort(sort, p => p.Bid.UnitPrice),
            nameof(ProductDataInfo.AskUnitPrice) => current.ApplySort(sort, p => p.Ask.UnitPrice),
            nameof(ProductDataInfo.OrderMetaSpread) => current.ApplySort(sort, p => p.Meta.Spread),
            nameof(ProductDataInfo.OrderMetaPotentialProfitMultiplier) => current.ApplySort(sort, p => p.Meta.ProfitMultiplier),
            nameof(ProductDataInfo.OrderMetaTotalWeekVolume) => current.ApplySort(sort, p => p.Meta.TotalWeekVolume),
            nameof(ProductDataInfo.BidWeekVolume) => current.ApplySort(sort, p => p.Bid.OrderVolumeWeek),
            nameof(ProductDataInfo.AskWeekVolume) => current.ApplySort(sort, p => p.Ask.OrderVolumeWeek),
            nameof(ProductDataInfo.OrderMetaFlipOpportunityScore) => current.ApplySort(sort, p => p.Meta.FlipOpportunityScore),
            nameof(ProductDataInfo.BidCurrentOrders) => current.ApplySort(sort, p => p.Bid.OrderCount),
            nameof(ProductDataInfo.AskCurrentOrders) => current.ApplySort(sort, p => p.Ask.OrderCount),
            nameof(ProductDataInfo.EstimatedTotalProfit) => current.ApplySort(sort, p => p.Meta.EstimatedTotalProfit),
            nameof(ProductDataInfo.RecommendationConfidence) => current.ApplySort(sort, p => p.Meta.RecommendationConfidence),
            _ => current
        });
        return query;
    }

    private static PaginationContext<ProductDataInfo> GetPagedData(Pagination request, IQueryable<EFProduct> query)
    {
        var count = query.Count();
        var pagedData = query
            .Skip(request.Skip)
            .Take(request.Top)
            .Select(ProductMapping.ToInfo)
            .ToList();

        return new PaginationContext<ProductDataInfo>
        {
            Data = pagedData,
            Count = count
        };
    }
}
