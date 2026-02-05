using BazaarCompanionWeb.Context;
using BazaarCompanionWeb.Dtos;
using BazaarCompanionWeb.Entities;
using BazaarCompanionWeb.Interfaces;
using BazaarCompanionWeb.Interfaces.Database;
using BazaarCompanionWeb.Models.Pagination;
using BazaarCompanionWeb.Models.Pagination.MetaPaginations;
using BazaarCompanionWeb.Services;
using BazaarCompanionWeb.Utilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BazaarCompanionWeb.Queries;

public class ProductsPaginationQueryHelper(
    IDbContextFactory<DataContext> contextFactory, 
    IOptionsMonitor<Configuration> optionsMonitor,
    IOhlcRepository ohlcRepository)
    : IResourceQueryHelper<ProductPagination, ProductDataInfo>
{
    private readonly Configuration _configuration = optionsMonitor.CurrentValue;

    public async Task<PaginationContext<ProductDataInfo>> QueryResourceAsync(ProductPagination request,
        CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var query = context.Products
            .Include(x => x.Bid)
            .Include(x => x.Ask)
            .Include(x => x.Meta)
            .AsNoTracking()
            .AsQueryable();

        if (request.ToggleFilter)
        {
            query = ApplyFilterQuery(query);
        }

        // Apply advanced filters if provided
        if (request.AdvancedFilters != null)
        {
            query = ApplyAdvancedFilters(query, request.AdvancedFilters);
        }

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            if (request.UseFuzzySearch)
            {
                query = ApplyFuzzySearchQuery(query, request.Search);
            }
            else
            {
                query = ApplySearchQuery(query, request.Search);
            }
        }

        if (request.Sorts.Any())
            query = ApplySortQuery(query, request.Sorts);

        return await GetPagedData(request, query, cancellationToken);
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
        var searchWords = search.Split(' ');
        var regularSearchWords = searchWords.Where(x => x.Length >= 3);

        // ILike = case-insensitive (PostgreSQL); "ascen" matches "Ascension Rope"
        query = regularSearchWords.Aggregate(query, (current, word) =>
            current.Where(product => EF.Functions.ILike(product.FriendlyName, $"%{word}%")));

        return query;
    }

    private static IQueryable<EFProduct> ApplyFuzzySearchQuery(IQueryable<EFProduct> query, string search)
    {
        // For database queries we use ILike for case-insensitive substring match
        var searchWords = search.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var regularSearchWords = searchWords.Where(x => x.Length >= 2); // Lower threshold for fuzzy

        query = regularSearchWords.Aggregate(query, (current, word) =>
            current.Where(product => EF.Functions.ILike(product.FriendlyName, $"%{word}%")));

        return query;
    }

    private IQueryable<EFProduct> ApplyAdvancedFilters(IQueryable<EFProduct> query, AdvancedFilterOptions filters)
    {
        // Tier filter
        if (filters.SelectedTiers.Any())
        {
            query = query.Where(p => filters.SelectedTiers.Contains(p.Tier));
        }

        // Manipulation status
        if (filters.ManipulationStatus == ManipulationFilter.Manipulated)
        {
            query = query.Where(p => p.Meta.IsManipulated);
        }
        else if (filters.ManipulationStatus == ManipulationFilter.NotManipulated)
        {
            query = query.Where(p => !p.Meta.IsManipulated);
        }

        // Price range
        if (filters.MinPrice.HasValue)
        {
            query = query.Where(p => p.Bid.UnitPrice >= filters.MinPrice.Value);
        }
        if (filters.MaxPrice.HasValue)
        {
            query = query.Where(p => p.Bid.UnitPrice <= filters.MaxPrice.Value);
        }

        // Spread range
        if (filters.MinSpread.HasValue)
        {
            query = query.Where(p => p.Meta.Spread >= filters.MinSpread.Value);
        }
        if (filters.MaxSpread.HasValue)
        {
            query = query.Where(p => p.Meta.Spread <= filters.MaxSpread.Value);
        }

        // Volume range
        if (filters.MinVolume.HasValue)
        {
            query = query.Where(p => p.Meta.TotalWeekVolume >= filters.MinVolume.Value);
        }
        if (filters.MaxVolume.HasValue)
        {
            query = query.Where(p => p.Meta.TotalWeekVolume <= filters.MaxVolume.Value);
        }

        // Volume tier
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

        // Opportunity score range
        if (filters.MinOpportunityScore.HasValue)
        {
            query = query.Where(p => p.Meta.FlipOpportunityScore >= filters.MinOpportunityScore.Value);
        }
        if (filters.MaxOpportunityScore.HasValue)
        {
            query = query.Where(p => p.Meta.FlipOpportunityScore <= filters.MaxOpportunityScore.Value);
        }

        // Profit multiplier range
        if (filters.MinProfitMultiplier.HasValue)
        {
            query = query.Where(p => p.Meta.ProfitMultiplier >= filters.MinProfitMultiplier.Value);
        }
        if (filters.MaxProfitMultiplier.HasValue)
        {
            query = query.Where(p => p.Meta.ProfitMultiplier <= filters.MaxProfitMultiplier.Value);
        }

        // Order count range
        if (filters.MinOrderCount.HasValue)
        {
            query = query.Where(p => p.Bid.OrderCount >= filters.MinOrderCount.Value || p.Ask.OrderCount >= filters.MinOrderCount.Value);
        }
        if (filters.MaxOrderCount.HasValue)
        {
            query = query.Where(p => p.Bid.OrderCount <= filters.MaxOrderCount.Value && p.Ask.OrderCount <= filters.MaxOrderCount.Value);
        }

        // Note: Volatility, trend direction, and correlation filters would require
        // additional data that's not in the database. These would need to be calculated
        // separately or stored in the database. For now, we'll skip them in the query
        // and apply them client-side if needed.

        return query;
    }

    private static IQueryable<EFProduct> ApplySortQuery(IQueryable<EFProduct> query, IEnumerable<SortDescriptor> sorts)
    {
        var sortDescriptors = sorts as SortDescriptor[] ?? sorts.ToArray();

        // Apply the sort
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
            _ => current
        });
        return query;
    }

    private static async Task<PaginationContext<ProductDataInfo>> GetPagedData(Pagination request, IQueryable<EFProduct> query,
        CancellationToken cancellationToken)
    {
        var queryServerCount = await query.CountAsync(cancellationToken);

        var pagedData = await query
            .Skip(request.Skip)
            .Take(request.Top)
            .Select(server => MapProduct(server))
            .ToListAsync(cancellationToken: cancellationToken);

        return new PaginationContext<ProductDataInfo>
        {
            Data = pagedData,
            Count = queryServerCount
        };
    }

    private static ProductDataInfo MapProduct(EFProduct product)
    {
        return new ProductDataInfo
        {
            BidMarketDataId = product.Bid.Id,
            AskMarketDataId = product.Ask.Id,
            ItemId = product.ProductKey,
            ItemFriendlyName = product.FriendlyName,
            ItemTier = product.Tier,
            ItemUnstackable = product.Unstackable,
            SkinUrl = product.SkinUrl,
            BidUnitPrice = product.Bid.UnitPrice,
            BidWeekVolume = product.Bid.OrderVolumeWeek,
            BidCurrentOrders = product.Bid.OrderCount,
            BidCurrentVolume = product.Bid.OrderVolume,
            AskUnitPrice = product.Ask.UnitPrice,
            AskWeekVolume = product.Ask.OrderVolumeWeek,
            AskCurrentOrders = product.Ask.OrderCount,
            AskCurrentVolume = product.Ask.OrderVolume,
            OrderMetaPotentialProfitMultiplier = product.Meta.ProfitMultiplier,
            OrderMetaSpread = product.Meta.Spread,
            OrderMetaTotalWeekVolume = product.Meta.TotalWeekVolume,
            OrderMetaFlipOpportunityScore = product.Meta.FlipOpportunityScore,
            IsManipulated = product.Meta.IsManipulated,
            ManipulationIntensity = product.Meta.ManipulationIntensity,
            PriceDeviationPercent = product.Meta.PriceDeviationPercent,
        };
    }
}
