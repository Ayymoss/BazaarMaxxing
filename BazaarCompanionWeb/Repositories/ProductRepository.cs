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

        var productMap = products.ToDictionary(x => x.Name, product => product);

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
                    PotentialProfitMultiplier = x.Meta.PotentialProfitMultiplier,
                    Margin = x.Meta.Margin,
                    TotalWeekVolume = x.Meta.TotalWeekVolume,
                },
                Snapshots =
                [
                    new EFPriceSnapshot
                    {
                        BuyUnitPrice = x.MarketData.BuyLastPrice,
                        SellUnitPrice = x.MarketData.SellLastPrice,
                        Taken = timeNow,
                    }
                ],
                MarketData = new EFMarketData
                {
                    BuyLastPrice = x.MarketData.BuyLastPrice,
                    BuyLastOrderVolumeWeek = x.MarketData.BuyLastOrderVolumeWeek,
                    BuyLastOrderVolume = x.MarketData.BuyLastOrderVolume,
                    BuyLastOrderCount = x.MarketData.BuyLastOrderCount,
                    SellLastPrice = x.MarketData.SellLastPrice,
                    SellLastOrderVolumeWeek = x.MarketData.SellLastOrderVolumeWeek,
                    SellLastOrderVolume = x.MarketData.SellLastOrderVolume,
                    SellLastOrderCount = x.MarketData.SellLastOrderCount,
                }
            })
            .ToList();

        var existingProducts = await context.Products
            .Include(x => x.Snapshots)
            .Include(x => x.MarketData)
            .Include(x => x.Meta)
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
                product.Name = incomingProduct.Name;
                product.FriendlyName = incomingProduct.FriendlyName;
                product.Tier = incomingProduct.Tier;
                product.Unstackable = incomingProduct.Unstackable;

                product.MarketData.BuyLastPrice = incomingProduct.MarketData.BuyLastPrice;
                product.MarketData.BuyLastOrderVolumeWeek = incomingProduct.MarketData.BuyLastOrderVolumeWeek;
                product.MarketData.BuyLastOrderVolume = incomingProduct.MarketData.BuyLastOrderVolume;
                product.MarketData.BuyLastOrderCount = incomingProduct.MarketData.BuyLastOrderCount;
                product.MarketData.SellLastPrice = incomingProduct.MarketData.SellLastPrice;
                product.MarketData.SellLastOrderVolumeWeek = incomingProduct.MarketData.SellLastOrderVolumeWeek;
                product.MarketData.SellLastOrderVolume = incomingProduct.MarketData.SellLastOrderVolume;
                product.MarketData.SellLastOrderCount = incomingProduct.MarketData.SellLastOrderCount;

                product.Meta.TotalWeekVolume = incomingProduct.Meta.TotalWeekVolume;
                product.Meta.PotentialProfitMultiplier = incomingProduct.Meta.PotentialProfitMultiplier;
                product.Meta.Margin = incomingProduct.Meta.Margin;

                product.Snapshots.Add(new EFPriceSnapshot
                {
                    BuyUnitPrice = incomingProduct.MarketData.BuyLastPrice,
                    SellUnitPrice = incomingProduct.MarketData.SellLastPrice,
                    Taken = timeNow,
                    ProductGuid = product.ProductGuid,
                });
            }

            if (existingProducts.Count > 0)
            {
                context.UpdateRange(existingProducts);
            }

            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (Exception e)
        {
            await transaction.RollbackAsync(cancellationToken);
            Console.WriteLine($"Error updating or adding products: {e}");
        }
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
