using BazaarCompanionWeb.Context;
using BazaarCompanionWeb.Dtos;
using BazaarCompanionWeb.Entities;
using BazaarCompanionWeb.Interfaces;
using BazaarCompanionWeb.Models.Pagination;
using BazaarCompanionWeb.Models.Pagination.MetaPaginations;
using BazaarCompanionWeb.Utilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BazaarCompanionWeb.Queries;

public class ProductsPaginationQueryHelper(IDbContextFactory<DataContext> contextFactory, IOptionsMonitor<Configuration> optionsMonitor)
    : IResourceQueryHelper<ProductPagination, ProductDataInfo>
{
    private readonly Configuration _configuration = optionsMonitor.CurrentValue;

    public async Task<PaginationContext<ProductDataInfo>> QueryResourceAsync(ProductPagination request,
        CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var query = context.Products
            .Include(x => x.Buy)
            .Include(x => x.Sell)
            .Include(x => x.Meta)
            .AsNoTracking()
            .AsQueryable();

        if (request.ToggleFilter)
        {
            query = ApplyFilterQuery(query);
        }

        if (!string.IsNullOrWhiteSpace(request.Search))
            query = ApplySearchQuery(query, request.Search);

        if (request.Sorts.Any())
            query = ApplySortQuery(query, request.Sorts);

        return await GetPagedData(request, query, cancellationToken);
    }

    private IQueryable<EFProduct> ApplyFilterQuery(IQueryable<EFProduct> query)
    {
        return query.Where(x => x.Meta.Margin > _configuration.MinimumMargin)
            .Where(x => x.Meta.ProfitMultiplier > _configuration.MinimumPotentialProfitMultiplier)
            .Where(x => x.Buy.OrderVolumeWeek / x.Sell.OrderVolumeWeek > _configuration.MinimumBuyOrderPower)
            .Where(x => x.Buy.OrderVolumeWeek > _configuration.MinimumWeekVolume)
            .Where(x => x.Sell.OrderVolumeWeek > _configuration.MinimumWeekVolume);
    }

    private static IQueryable<EFProduct> ApplySearchQuery(IQueryable<EFProduct> query, string search)
    {
        var searchWords = search.Split(' ');
        var regularSearchWords = searchWords.Where(x => x.Length >= 3);

        query = regularSearchWords.Aggregate(query, (current, word) =>
            current.Where(product => EF.Functions.Like(product.FriendlyName, $"%{word}%")));

        return query;
    }

    private static IQueryable<EFProduct> ApplySortQuery(IQueryable<EFProduct> query, IEnumerable<SortDescriptor> sorts)
    {
        var sortDescriptors = sorts as SortDescriptor[] ?? sorts.ToArray();

        // Apply the sort
        query = sortDescriptors.Aggregate(query, (current, sort) => sort.Property switch
        {
            nameof(ProductDataInfo.ItemFriendlyName) => current.ApplySortForName(sort, p => p.FriendlyName, p => p.Tier),
            nameof(ProductDataInfo.BuyOrderUnitPrice) => current.ApplySort(sort, p => p.Buy.UnitPrice),
            nameof(ProductDataInfo.SellOrderUnitPrice) => current.ApplySort(sort, p => p.Sell.UnitPrice),
            nameof(ProductDataInfo.OrderMetaMargin) => current.ApplySort(sort, p => p.Meta.Margin),
            nameof(ProductDataInfo.OrderMetaPotentialProfitMultiplier) => current.ApplySort(sort, p => p.Meta.ProfitMultiplier),
            nameof(ProductDataInfo.OrderMetaTotalWeekVolume) => current.ApplySort(sort, p => p.Meta.TotalWeekVolume),
            nameof(ProductDataInfo.BuyOrderWeekVolume) => current.ApplySort(sort, p => p.Buy.OrderVolumeWeek),
            nameof(ProductDataInfo.SellOrderWeekVolume) => current.ApplySort(sort, p => p.Sell.OrderVolumeWeek),
            nameof(ProductDataInfo.OrderMetaFlipOpportunityScore) => current.ApplySort(sort, p => p.Meta.FlipOpportunityScore),
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
            BuyMarketDataId = product.Buy.Id,
            SellMarketDataId = product.Sell.Id,
            ItemId = product.ProductKey,
            ItemFriendlyName = product.FriendlyName,
            ItemTier = product.Tier,
            ItemUnstackable = product.Unstackable,
            BuyOrderUnitPrice = product.Buy.UnitPrice,
            BuyOrderWeekVolume = product.Buy.OrderVolumeWeek,
            BuyOrderCurrentOrders = product.Buy.OrderCount,
            BuyOrderCurrentVolume = product.Buy.OrderVolume,
            SellOrderUnitPrice = product.Sell.UnitPrice,
            SellOrderWeekVolume = product.Sell.OrderVolumeWeek,
            SellOrderCurrentOrders = product.Sell.OrderCount,
            SellOrderCurrentVolume = product.Sell.OrderVolume,
            OrderMetaPotentialProfitMultiplier = product.Meta.ProfitMultiplier,
            OrderMetaMargin = product.Meta.Margin,
            OrderMetaTotalWeekVolume = product.Meta.TotalWeekVolume,
            OrderMetaFlipOpportunityScore = product.Meta.FlipOpportunityScore,
        };
    }
}
