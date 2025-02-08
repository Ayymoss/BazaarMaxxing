using BazaarCompanionWeb.Context;
using BazaarCompanionWeb.Dtos;
using BazaarCompanionWeb.Entities;
using BazaarCompanionWeb.Interfaces.Database;
using Microsoft.EntityFrameworkCore;

namespace BazaarCompanionWeb.Repositories;

public class ProductRepository(IDbContextFactory<DataContext> contextFactory, ILogger<ProductRepository> logger)
    : IProductRepository
{
    public async Task UpdateOrAddProductsAsync(List<EFProduct> products, CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        var existingProductNames = await context.Products
            .Select(p => p.ProductKey)
            .ToListAsync(cancellationToken);

        var productMap = products.ToDictionary(x => x.ProductKey);
        var timeNow = TimeProvider.System.GetLocalNow().DateTime;

        var newProducts = products
            .Where(p => !existingProductNames.Contains(p.ProductKey))
            .Select(x => new EFProduct
            {
                ProductKey = x.ProductKey,
                FriendlyName = x.FriendlyName,
                Tier = x.Tier,
                Unstackable = x.Unstackable,

                Meta = new EFProductMeta
                {
                    ProfitMultiplier = x.Meta.ProfitMultiplier,
                    Margin = x.Meta.Margin,
                    TotalWeekVolume = x.Meta.TotalWeekVolume,
                    FlipOpportunityScore = x.Meta.FlipOpportunityScore,
                    ProductKey = x.ProductKey
                },
                Snapshots =
                [
                    new EFPriceSnapshot
                    {
                        BuyUnitPrice = x.Buy.UnitPrice,
                        SellUnitPrice = x.Sell.UnitPrice,
                        Taken = DateOnly.FromDateTime(timeNow),
                        ProductKey = x.ProductKey
                    }
                ],
                Buy = new EFBuyMarketData
                {
                    UnitPrice = x.Buy.UnitPrice,
                    OrderVolumeWeek = x.Buy.OrderVolumeWeek,
                    OrderVolume = x.Buy.OrderVolume,
                    OrderCount = x.Buy.OrderCount,
                    BookValue = x.Buy.BookValue,
                    ProductKey = x.ProductKey
                },
                Sell = new EFSellMarketData
                {
                    UnitPrice = x.Sell.UnitPrice,
                    OrderVolumeWeek = x.Sell.OrderVolumeWeek,
                    OrderVolume = x.Sell.OrderVolume,
                    OrderCount = x.Sell.OrderCount,
                    BookValue = x.Sell.BookValue,
                    ProductKey = x.ProductKey
                },
            });

        var test = productMap.Keys.ToList();

        var existingProducts = await context.Products
            .Include(x => x.Snapshots)
            .Include(x => x.Meta)
            .Include(x => x.Buy)
            .Include(x => x.Sell)
            .Where(x => test.Contains(x.ProductKey))
            // Get 2 days of EMA
            .Where(x => x.Snapshots.Any(y => y.Taken >= DateOnly.FromDateTime(TimeProvider.System.GetLocalNow().AddDays(-1).Date)))
            .ToListAsync(cancellationToken);

        logger.LogInformation("Starting transaction...");

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            // ReSharper disable PossibleMultipleEnumeration
            if (newProducts.Any())
            {
                await context.AddRangeAsync(newProducts, cancellationToken);
            }
            // ReSharper enable PossibleMultipleEnumeration

            // 12 hour EMA (720 = 12 h * 60 m)
            const double alpha = 2d / (720 + 1);
            var todayDate = DateOnly.FromDateTime(TimeProvider.System.GetLocalNow().Date);
            var yesterdayDate = DateOnly.FromDateTime(TimeProvider.System.GetLocalNow().AddDays(-1).Date);

            foreach (var product in existingProducts)
            {
                var incomingProduct = productMap[product.ProductKey];

                product.ProductKey = incomingProduct.ProductKey;
                product.FriendlyName = incomingProduct.FriendlyName;
                product.Tier = incomingProduct.Tier;
                product.Unstackable = incomingProduct.Unstackable;

                product.Buy.UnitPrice = incomingProduct.Buy.UnitPrice;
                product.Buy.OrderVolumeWeek = incomingProduct.Buy.OrderVolumeWeek;
                product.Buy.OrderVolume = incomingProduct.Buy.OrderVolume;
                product.Buy.OrderCount = incomingProduct.Buy.OrderCount;
                product.Buy.BookValue = incomingProduct.Buy.BookValue;

                product.Sell.UnitPrice = incomingProduct.Sell.UnitPrice;
                product.Sell.OrderVolumeWeek = incomingProduct.Sell.OrderVolumeWeek;
                product.Sell.OrderVolume = incomingProduct.Sell.OrderVolume;
                product.Sell.OrderCount = incomingProduct.Sell.OrderCount;
                product.Sell.BookValue = incomingProduct.Sell.BookValue;

                product.Meta.TotalWeekVolume = incomingProduct.Meta.TotalWeekVolume;
                product.Meta.ProfitMultiplier = incomingProduct.Meta.ProfitMultiplier;
                product.Meta.Margin = incomingProduct.Meta.Margin;
                product.Meta.FlipOpportunityScore = incomingProduct.Meta.FlipOpportunityScore;

                if (product.Snapshots.Count is 0)
                {
                    product.Snapshots.Add(new EFPriceSnapshot
                    {
                        BuyUnitPrice = incomingProduct.Buy.UnitPrice,
                        SellUnitPrice = incomingProduct.Sell.UnitPrice,
                        Taken = todayDate,
                        ProductKey = product.ProductKey,
                    });
                }
                else if (product.Snapshots.All(x => x.Taken != todayDate))
                {
                    var yesterday = product.Snapshots.First(x => x.Taken == yesterdayDate);

                    product.Snapshots.Add(new EFPriceSnapshot
                    {
                        BuyUnitPrice = (incomingProduct.Buy.UnitPrice - yesterday.BuyUnitPrice) * alpha + yesterday.BuyUnitPrice,
                        SellUnitPrice = (incomingProduct.Sell.UnitPrice - yesterday.SellUnitPrice) * alpha + yesterday.SellUnitPrice,
                        Taken = todayDate,
                        ProductKey = product.ProductKey,
                    });
                }
                else
                {
                    var today = product.Snapshots.First(x => x.Taken == todayDate);

                    today.BuyUnitPrice = (incomingProduct.Buy.UnitPrice - today.BuyUnitPrice) * alpha + today.BuyUnitPrice;
                    today.SellUnitPrice = (incomingProduct.Sell.UnitPrice - today.SellUnitPrice) * alpha + today.SellUnitPrice;
                }
            }

            logger.LogInformation("Saving changes...");
            await context.SaveChangesAsync(cancellationToken);

            logger.LogInformation("Commiting transaction...");
            await transaction.CommitAsync(cancellationToken);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Failure during transaction, reverted");
            await transaction.RollbackAsync(cancellationToken);
        }
    }

    public async Task<List<Order>> GetOrderBookAsync(int marketDataId, CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        var marketData = await context.MarketData
            .AsNoTracking()
            .Where(x => x.Id == marketDataId)
            .ToListAsync(cancellationToken: cancellationToken);

        var books = marketData
            .SelectMany(x => x.Books)
            .Select(x => new Order(x.UnitPrice, x.Amount, x.Orders))
            .ToList();

        return books;
    }

    public async Task<ProductDataInfo> GetProductAsync(string productKey, CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        var product = await context.Products.Where(x => x.ProductKey == productKey)
            .Select(x => new ProductDataInfo
            {
                BuyMarketDataId = x.Buy.Id,
                SellMarketDataId = x.Sell.Id,
                ItemId = x.ProductKey,
                ItemFriendlyName = x.FriendlyName,
                ItemTier = x.Tier,
                ItemUnstackable = x.Unstackable,
                BuyOrderUnitPrice = x.Buy.UnitPrice,
                BuyOrderWeekVolume = x.Buy.OrderVolumeWeek,
                BuyOrderCurrentOrders = x.Buy.OrderCount,
                BuyOrderCurrentVolume = x.Buy.OrderVolume,
                SellOrderUnitPrice = x.Sell.UnitPrice,
                SellOrderWeekVolume = x.Sell.OrderVolumeWeek,
                SellOrderCurrentOrders = x.Sell.OrderCount,
                SellOrderCurrentVolume = x.Sell.OrderVolume,
                OrderMetaPotentialProfitMultiplier = x.Meta.ProfitMultiplier,
                OrderMetaMargin = x.Meta.Margin,
                OrderMetaTotalWeekVolume = x.Meta.TotalWeekVolume,
                OrderMetaFlipOpportunityScore = x.Meta.FlipOpportunityScore,
            }).FirstAsync(cancellationToken: cancellationToken);

        product.PriceHistory = await GetPriceHistoryAsync(product.ItemId, cancellationToken);

        product.BuyBook = (await GetOrderBookAsync(product.BuyMarketDataId, cancellationToken))
            .OrderBy(y => y.UnitPrice)
            .ToList();

        product.SellBook = (await GetOrderBookAsync(product.SellMarketDataId, cancellationToken))
            .OrderByDescending(y => y.UnitPrice)
            .ToList();

        return product;
    }

    public async Task<List<PriceHistorySnapshot>> GetPriceHistoryAsync(string productKey, CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var snapshots = await context.PriceSnapshots
            .Where(x => x.ProductKey == productKey)
            .Where(x => DateOnly.FromDateTime(DateTime.Now.AddDays(-30)) < x.Taken)
            .GroupBy(x => x.Taken)
            .Select(x => new PriceHistorySnapshot(x.Key, x.Max(y => y.BuyUnitPrice), x.Max(y => y.SellUnitPrice)))
            .ToListAsync(cancellationToken);
        return snapshots;
    }
}
