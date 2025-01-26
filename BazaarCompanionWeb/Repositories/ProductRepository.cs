using BazaarCompanionWeb.Context;
using BazaarCompanionWeb.Dtos;
using BazaarCompanionWeb.Entities;
using BazaarCompanionWeb.Interfaces.Database;
using Microsoft.EntityFrameworkCore;

namespace BazaarCompanionWeb.Repositories;

public class ProductRepository(IDbContextFactory<DataContext> contextFactory) : IProductRepository
{
    public async Task UpdateOrAddProductsAsync(List<EFProduct> products, CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        var existingProductNames = await context.Products
            .Select(p => p.Name)
            .ToListAsync(cancellationToken);

        var productMap = products.ToDictionary(x => x.Name);
        var timeNow = TimeProvider.System.GetLocalNow().DateTime;

        var newProducts = products
            .Where(p => !existingProductNames.Contains(p.Name))
            .Select(x => new EFProduct
            {
                ProductGuid = Guid.CreateVersion7(),
                Name = x.Name,
                FriendlyName = x.FriendlyName,
                Tier = x.Tier,
                Unstackable = x.Unstackable,

                Meta = new EFProductMeta
                {
                    ProfitMultiplier = x.Meta.ProfitMultiplier,
                    Margin = x.Meta.Margin,
                    TotalWeekVolume = x.Meta.TotalWeekVolume,
                    FlipOpportunityScore = x.Meta.FlipOpportunityScore,
                },
                Snapshots =
                [
                    new EFPriceSnapshot
                    {
                        BuyUnitPrice = x.Buy.UnitPrice,
                        SellUnitPrice = x.Sell.UnitPrice,
                        Taken = timeNow,
                    }
                ],
                Buy = new EFBuyMarketData
                {
                    UnitPrice = x.Buy.UnitPrice,
                    OrderVolumeWeek = x.Buy.OrderVolumeWeek,
                    OrderVolume = x.Buy.OrderVolume,
                    OrderCount = x.Buy.OrderCount,
                    Book = x.Buy.Book
                },
                Sell = new EFSellMarketData
                {
                    UnitPrice = x.Sell.UnitPrice,
                    OrderVolumeWeek = x.Sell.OrderVolumeWeek,
                    OrderVolume = x.Sell.OrderVolume,
                    OrderCount = x.Sell.OrderCount,
                    Book = x.Sell.Book
                },
            })
            .ToList();

        var existingProducts = await context.Products
            .Include(x => x.Snapshots)
            .Include(x => x.Meta)
            .Include(x => x.Buy).ThenInclude(b => b.Book)
            .Include(x => x.Sell).ThenInclude(s => s.Book)
            .Where(x => productMap.Keys.Contains(x.Name))
            .ToListAsync(cancellationToken);

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            if (newProducts.Count > 0)
            {
                await context.AddRangeAsync(newProducts, cancellationToken);
            }

            foreach (var product in existingProducts)
            {
                var incomingProduct = productMap[product.Name];

                context.RemoveRange(product.Buy.Book);
                context.RemoveRange(product.Sell.Book);

                product.Name = incomingProduct.Name;
                product.FriendlyName = incomingProduct.FriendlyName;
                product.Tier = incomingProduct.Tier;
                product.Unstackable = incomingProduct.Unstackable;

                product.Buy.UnitPrice = incomingProduct.Buy.UnitPrice;
                product.Buy.OrderVolumeWeek = incomingProduct.Buy.OrderVolumeWeek;
                product.Buy.OrderVolume = incomingProduct.Buy.OrderVolume;
                product.Buy.OrderCount = incomingProduct.Buy.OrderCount;
                product.Buy.Book = incomingProduct.Buy.Book;

                product.Sell.UnitPrice = incomingProduct.Sell.UnitPrice;
                product.Sell.OrderVolumeWeek = incomingProduct.Sell.OrderVolumeWeek;
                product.Sell.OrderVolume = incomingProduct.Sell.OrderVolume;
                product.Sell.OrderCount = incomingProduct.Sell.OrderCount;
                product.Sell.Book = incomingProduct.Sell.Book;

                product.Meta.TotalWeekVolume = incomingProduct.Meta.TotalWeekVolume;
                product.Meta.ProfitMultiplier = incomingProduct.Meta.ProfitMultiplier;
                product.Meta.Margin = incomingProduct.Meta.Margin;

                product.Snapshots.Add(new EFPriceSnapshot
                {
                    BuyUnitPrice = incomingProduct.Buy.UnitPrice,
                    SellUnitPrice = incomingProduct.Sell.UnitPrice,
                    Taken = timeNow,
                    ProductGuid = product.ProductGuid,
                });
            }

            context.UpdateRange(existingProducts); // This line is likely redundant

            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (Exception e)
        {
            await transaction.RollbackAsync(cancellationToken);
        }
    }

    public async Task<List<Order>> GetOrderBookAsync(int marketDataId, CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var book = await context.Orders
            .Where(x => x.MarketDataId == marketDataId)
            .Select(x => new Order(x.UnitPrice, x.Amount, x.Orders))
            .ToListAsync(cancellationToken: cancellationToken);
        return book;
    }

    public async Task<List<PriceHistorySnapshot>> GetPriceHistoryAsync(Guid productGuid, CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var snapshots = await context.PriceSnapshots
            .Where(x => x.ProductGuid == productGuid)
            .Where(x => DateTime.Now.AddDays(-30) < x.Taken)
            .GroupBy(x => DateOnly.FromDateTime(x.Taken))
            .Select(x => new PriceHistorySnapshot(x.Key, x.Average(y => y.BuyUnitPrice), x.Average(y => y.SellUnitPrice)))
            .ToListAsync(cancellationToken);
        return snapshots;
    }
}
