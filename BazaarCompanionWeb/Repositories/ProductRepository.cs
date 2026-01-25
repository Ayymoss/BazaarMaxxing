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
                    Spread = x.Meta.Spread,
                    TotalWeekVolume = x.Meta.TotalWeekVolume,
                    FlipOpportunityScore = x.Meta.FlipOpportunityScore,
                    IsManipulated = x.Meta.IsManipulated,
                    ManipulationIntensity = x.Meta.ManipulationIntensity,
                    PriceDeviationPercent = x.Meta.PriceDeviationPercent,
                    ProductKey = x.ProductKey
                },
                Snapshots =
                [
                    new EFPriceSnapshot
                    {
                        BidUnitPrice = x.Bid.UnitPrice,
                        AskUnitPrice = x.Ask.UnitPrice,
                        Taken = DateOnly.FromDateTime(timeNow),
                        ProductKey = x.ProductKey
                    }
                ],
                Bid = new EFBidMarketData
                {
                    UnitPrice = x.Bid.UnitPrice,
                    OrderVolumeWeek = x.Bid.OrderVolumeWeek,
                    OrderVolume = x.Bid.OrderVolume,
                    OrderCount = x.Bid.OrderCount,
                    BookValue = x.Bid.BookValue,
                    ProductKey = x.ProductKey
                },
                Ask = new EFAskMarketData
                {
                    UnitPrice = x.Ask.UnitPrice,
                    OrderVolumeWeek = x.Ask.OrderVolumeWeek,
                    OrderVolume = x.Ask.OrderVolume,
                    OrderCount = x.Ask.OrderCount,
                    BookValue = x.Ask.BookValue,
                    ProductKey = x.ProductKey
                },
            });

        var test = productMap.Keys.ToList();

        var existingProducts = await context.Products
            .Include(x => x.Snapshots)
            .Include(x => x.Meta)
            .Include(x => x.Bid)
            .Include(x => x.Ask)
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

                // Map top-level properties
                product.ProductKey = incomingProduct.ProductKey;
                product.FriendlyName = incomingProduct.FriendlyName;
                product.Tier = incomingProduct.Tier;
                product.Unstackable = incomingProduct.Unstackable;

                // Map Meta properties
                product.Meta.ProfitMultiplier = incomingProduct.Meta.ProfitMultiplier;
                product.Meta.Spread = incomingProduct.Meta.Spread;
                product.Meta.TotalWeekVolume = incomingProduct.Meta.TotalWeekVolume;
                product.Meta.FlipOpportunityScore = incomingProduct.Meta.FlipOpportunityScore;

                // Map Bid properties
                product.Bid.UnitPrice = incomingProduct.Bid.UnitPrice;
                product.Bid.OrderVolumeWeek = incomingProduct.Bid.OrderVolumeWeek;
                product.Bid.OrderVolume = incomingProduct.Bid.OrderVolume;
                product.Bid.OrderCount = incomingProduct.Bid.OrderCount;
                product.Bid.BookValue = incomingProduct.Bid.BookValue;

                // Map Ask properties
                product.Ask.UnitPrice = incomingProduct.Ask.UnitPrice;
                product.Ask.OrderVolumeWeek = incomingProduct.Ask.OrderVolumeWeek;
                product.Ask.OrderVolume = incomingProduct.Ask.OrderVolume;
                product.Ask.OrderCount = incomingProduct.Ask.OrderCount;
                product.Ask.BookValue = incomingProduct.Ask.BookValue;

                // Snapshots are intentionally NOT mapped (preserved from existing product)

                if (product.Snapshots.Count is 0)
                {
                    product.Snapshots.Add(new EFPriceSnapshot
                    {
                        BidUnitPrice = incomingProduct.Bid.UnitPrice,
                        AskUnitPrice = incomingProduct.Ask.UnitPrice,
                        Taken = todayDate,
                        ProductKey = product.ProductKey,
                    });
                }
                else if (product.Snapshots.All(x => x.Taken != todayDate))
                {
                    var yesterday = product.Snapshots.First(x => x.Taken == yesterdayDate);

                    product.Snapshots.Add(new EFPriceSnapshot
                    {
                        BidUnitPrice = (incomingProduct.Bid.UnitPrice - yesterday.BidUnitPrice) * alpha + yesterday.BidUnitPrice,
                        AskUnitPrice = (incomingProduct.Ask.UnitPrice - yesterday.AskUnitPrice) * alpha + yesterday.AskUnitPrice,
                        Taken = todayDate,
                        ProductKey = product.ProductKey,
                    });
                }
                else
                {
                    var today = product.Snapshots.First(x => x.Taken == todayDate);

                    today.BidUnitPrice = (incomingProduct.Bid.UnitPrice - today.BidUnitPrice) * alpha + today.BidUnitPrice;
                    today.AskUnitPrice = (incomingProduct.Ask.UnitPrice - today.AskUnitPrice) * alpha + today.AskUnitPrice;
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
                BidMarketDataId = x.Bid.Id,
                AskMarketDataId = x.Ask.Id,
                ItemId = x.ProductKey,
                ItemFriendlyName = x.FriendlyName,
                ItemTier = x.Tier,
                ItemUnstackable = x.Unstackable,
                BidUnitPrice = x.Bid.UnitPrice,
                BidWeekVolume = x.Bid.OrderVolumeWeek,
                BidCurrentOrders = x.Bid.OrderCount,
                BidCurrentVolume = x.Bid.OrderVolume,
                AskUnitPrice = x.Ask.UnitPrice,
                AskWeekVolume = x.Ask.OrderVolumeWeek,
                AskCurrentOrders = x.Ask.OrderCount,
                AskCurrentVolume = x.Ask.OrderVolume,
                OrderMetaPotentialProfitMultiplier = x.Meta.ProfitMultiplier,
                OrderMetaSpread = x.Meta.Spread,
                OrderMetaTotalWeekVolume = x.Meta.TotalWeekVolume,
                OrderMetaFlipOpportunityScore = x.Meta.FlipOpportunityScore,
                IsManipulated = x.Meta.IsManipulated,
                ManipulationIntensity = x.Meta.ManipulationIntensity,
                PriceDeviationPercent = x.Meta.PriceDeviationPercent,
            }).FirstAsync(cancellationToken: cancellationToken);

        product.PriceHistory = await GetPriceHistoryAsync(product.ItemId, cancellationToken);

        product.BidBook = (await GetOrderBookAsync(product.BidMarketDataId, cancellationToken))
            .OrderByDescending(y => y.UnitPrice) // Bids: Highest first
            .ToList();

        product.AskBook = (await GetOrderBookAsync(product.AskMarketDataId, cancellationToken))
            .OrderBy(y => y.UnitPrice) // Asks: Lowest first
            .ToList();

        return product;
    }

    public async Task<List<PriceHistorySnapshot>> GetPriceHistoryAsync(string productKey, CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var snapshots = await context.PriceSnapshots
            .Where(x => x.ProductKey == productKey)
            .Where(x => DateOnly.FromDateTime(DateTime.Now.AddDays(-64)) < x.Taken)
            .GroupBy(x => x.Taken)
            .Select(x => new PriceHistorySnapshot(x.Key, x.Max(y => y.BidUnitPrice), x.Max(y => y.AskUnitPrice)))
            .ToListAsync(cancellationToken);
        return snapshots;
    }
    public async Task<List<ProductDataInfo>> GetProductsAsync(CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.Products
            .AsNoTracking()
            .Select(x => new ProductDataInfo
            {
                BidMarketDataId = x.Bid.Id,
                AskMarketDataId = x.Ask.Id,
                ItemId = x.ProductKey,
                ItemFriendlyName = x.FriendlyName,
                ItemTier = x.Tier,
                ItemUnstackable = x.Unstackable,
                BidUnitPrice = x.Bid.UnitPrice,
                AskUnitPrice = x.Ask.UnitPrice,
            }).ToListAsync(cancellationToken);
    }

    public async Task<List<EFPriceSnapshot>> GetPriceSnapshotsAsync(CancellationToken ct = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(ct);
        return await context.PriceSnapshots
            .AsNoTracking()
            .OrderBy(x => x.Taken)
            .ToListAsync(ct);
    }
}
