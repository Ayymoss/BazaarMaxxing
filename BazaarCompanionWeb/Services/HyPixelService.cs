using BazaarCompanionWeb.Dtos;
using BazaarCompanionWeb.Entities;
using BazaarCompanionWeb.Hubs;
using BazaarCompanionWeb.Interfaces;
using Microsoft.AspNetCore.SignalR;
using BazaarCompanionWeb.Interfaces.Api;
using BazaarCompanionWeb.Interfaces.Database;
using BazaarCompanionWeb.Models;
using BazaarCompanionWeb.Models.Api.Bazaar;
using BazaarCompanionWeb.Models.Api.Items;
using BazaarCompanionWeb.Utilities;
using Humanizer;
using Item = BazaarCompanionWeb.Models.Item;

namespace BazaarCompanionWeb.Services;

public class HyPixelService(
    IHyPixelApi hyPixelApi,
    IProductRepository productRepository,
    IOhlcRepository ohlcRepository,
    IOpportunityScoringService opportunityScoringService,
    MarketInsightsService marketInsightsService,
    OrderBookAnalysisService orderBookAnalysisService,
    IHubContext<ProductHub> hubContext,
    TimeCache timeCache,
    LiveCandleTracker liveCandleTracker,
    IBazaarRunCache bazaarRunCache,
    ILogger<HyPixelService> logger)
{
    public async Task FetchDataAsync(CancellationToken cancellationToken)
    {
        var bazaarResponse = await hyPixelApi.GetBazaarAsync();
        var itemResponse = await hyPixelApi.GetItemsAsync();
        var (products, changedKeys) = await BuildProductDataAsync(bazaarResponse, itemResponse, cancellationToken);
        var productList = products.ToList();
        var mappedProducts = productList.Select(x => x.Map()).ToList();

        var stateByProduct = productList.ToDictionary(p => p.ItemId, p => new ProductState(
            p.ItemId, p.Bid.OrderPrice, p.Ask.OrderPrice, (long)p.Bid.WeekVolume, (long)p.Ask.WeekVolume));
        var scoresByProduct = mappedProducts.ToDictionary(ef => ef.ProductKey, ef => new CachedScores(
            ef.Meta.FlipOpportunityScore, ef.Meta.IsManipulated, ef.Meta.ManipulationIntensity, ef.Meta.PriceDeviationPercent));
        bazaarRunCache.Update(stateByProduct, scoresByProduct);

        await productRepository.UpdateOrAddProductsAsync(mappedProducts, cancellationToken);

        var ticks = productList.Select(p => (
            p.ItemId,
            p.Bid.OrderPrice,
            p.Ask.OrderPrice,
            (long)p.Bid.CurrentVolume,
            (long)p.Ask.CurrentVolume
        ));
        await ohlcRepository.RecordTicksAsync(ticks, cancellationToken);

        timeCache.LastUpdated = TimeProvider.System.GetLocalNow();

        await marketInsightsService.RefreshInsightsAsync(cancellationToken);

        var changedSet = changedKeys.ToHashSet();
        var snapshotItems = productList.Zip(mappedProducts)
            .Where(z => changedSet.Contains(z.First.ItemId))
            .Select(z =>
            {
                var (product, efProduct) = z;
                var bidBook = product.Bid.OrderBook.Select(x => new Order(x.UnitPrice, x.Amount, x.Orders)).ToList();
                var askBook = product.Ask.OrderBook.Select(x => new Order(x.UnitPrice, x.Amount, x.Orders)).ToList();
                return (efProduct.ProductKey, bidBook, askBook);
            }).ToList();
        if (snapshotItems.Count > 0)
            await orderBookAnalysisService.StoreSnapshotsBatchAsync(snapshotItems, cancellationToken);

        var broadcastItems = productList.Zip(mappedProducts).Where(z => changedSet.Contains(z.First.ItemId)).ToList();
        await Parallel.ForEachAsync(
            broadcastItems,
            new ParallelOptions { MaxDegreeOfParallelism = 16, CancellationToken = cancellationToken },
            async (item, ct) =>
            {
                var (product, efProduct) = item;
                var updateInfo = new ProductDataInfo
                {
                    ItemId = efProduct.ProductKey,
                    ItemFriendlyName = efProduct.FriendlyName,
                    ItemTier = efProduct.Tier,
                    ItemUnstackable = efProduct.Unstackable,
                    SkinUrl = efProduct.SkinUrl,
                    BidUnitPrice = efProduct.Bid.UnitPrice,
                    BidWeekVolume = efProduct.Bid.OrderVolumeWeek,
                    BidCurrentOrders = efProduct.Bid.OrderCount,
                    BidCurrentVolume = efProduct.Bid.OrderVolume,
                    AskUnitPrice = efProduct.Ask.UnitPrice,
                    AskWeekVolume = efProduct.Ask.OrderVolumeWeek,
                    AskCurrentOrders = efProduct.Ask.OrderCount,
                    AskCurrentVolume = efProduct.Ask.OrderVolume,
                    OrderMetaPotentialProfitMultiplier = efProduct.Meta.ProfitMultiplier,
                    OrderMetaSpread = efProduct.Meta.Spread,
                    OrderMetaTotalWeekVolume = efProduct.Meta.TotalWeekVolume,
                    OrderMetaFlipOpportunityScore = efProduct.Meta.FlipOpportunityScore,
                    IsManipulated = efProduct.Meta.IsManipulated,
                    ManipulationIntensity = efProduct.Meta.ManipulationIntensity,
                    PriceDeviationPercent = efProduct.Meta.PriceDeviationPercent,
                    BidMarketDataId = efProduct.Bid.Id,
                    AskMarketDataId = efProduct.Ask.Id,
                    BidBook = product.Bid.OrderBook.Select(x => new Order(x.UnitPrice, x.Amount, x.Orders)).ToList(),
                    AskBook = product.Ask.OrderBook.Select(x => new Order(x.UnitPrice, x.Amount, x.Orders)).ToList()
                };

                await hubContext.Clients.Group(efProduct.ProductKey).SendAsync("ProductUpdated", updateInfo, ct);

                var liveTick = liveCandleTracker.UpdateAndGetTick(
                    efProduct.ProductKey,
                    efProduct.Bid.UnitPrice,
                    efProduct.Ask.UnitPrice,
                    efProduct.Bid.OrderVolume + efProduct.Ask.OrderVolume);
                await hubContext.Clients.Group(efProduct.ProductKey).SendAsync("TickUpdated", liveTick, ct);
            });
    }

    private async Task<(IEnumerable<ProductData> Products, IReadOnlyList<string> ChangedKeys)> BuildProductDataAsync(
        BazaarResponse bazaarResponse, ItemResponse itemResponse, CancellationToken cancellationToken)
    {
        var itemMap = itemResponse.Items.ToDictionary(x => x.Id, x => x);
        var bazaarList = bazaarResponse.Products.Values.Where(x => x.Bids.Count is not 0).ToList();

        if (bazaarList.Count == 0)
            return ([], []);

        var currentState = bazaarList.ToDictionary(b => b.ProductId, b =>
        {
            var ask = b.Asks.FirstOrDefault();
            var bid = b.Bids.FirstOrDefault();
            var askOrderPrice = (double)(ask?.PricePerUnit ?? bid?.PricePerUnit + 0.1 ?? 0.1f);
            var bidOrderPrice = (double)(bid?.PricePerUnit ?? ask?.PricePerUnit - 0.1 ?? 0.1f);
            return new ProductState(b.ProductId, bidOrderPrice, askOrderPrice, b.Ticker.MovingWeekSells, b.Ticker.MovingWeekBuys);
        });

        var changedKeys = bazaarRunCache.GetChangedProductKeys(currentState);
        logger.LogDebug("Detected {ChangedCount} changed products out of {TotalCount} total products", changedKeys.Count, bazaarList.Count);

        IReadOnlyList<double> opportunityScores;
        IReadOnlyList<ManipulationScore> manipulationScores;

        if (changedKeys.Count == 0)
        {
            opportunityScores = [];
            manipulationScores = [];
        }
        else
        {
            const int lookbackHours = 7 * 24;
            var candlesByProduct =
                await ohlcRepository.GetCandlesBulkAsync(changedKeys, CandleInterval.OneHour, lookbackHours, cancellationToken);

            var changedInputs = changedKeys.Select(key =>
            {
                var b = bazaarList.First(x => x.ProductId == key);
                var ask = b.Asks.FirstOrDefault();
                var bid = b.Bids.FirstOrDefault();
                var askOrderPrice = ask?.PricePerUnit ?? bid?.PricePerUnit + 0.1 ?? 0.1f;
                var bidOrderPrice = bid?.PricePerUnit ?? ask?.PricePerUnit - 0.1 ?? 0.1f;
                return new ScoringProductInput(key, bidOrderPrice, askOrderPrice, b.Ticker.MovingWeekBuys, b.Ticker.MovingWeekSells);
            }).ToList();

            (opportunityScores, manipulationScores) = opportunityScoringService.CalculateScoresBatch(changedInputs, candlesByProduct);
        }

        var changedKeyToIndex = changedKeys.Select((key, idx) => (key, idx)).ToDictionary(x => x.key, x => x.idx);
        var productList = new List<ProductData>();

        foreach (var bazaar in bazaarList)
        {
            var item = itemMap.GetValueOrDefault(bazaar.ProductId);
            var ask = bazaar.Asks.FirstOrDefault();
            var bid = bazaar.Bids.FirstOrDefault();
            var askOrderPrice = ask?.PricePerUnit ?? bid?.PricePerUnit + 0.1 ?? 0.1f;
            var bidOrderPrice = bid?.PricePerUnit ?? ask?.PricePerUnit - 0.1 ?? 0.1f;
            var movingWeekSells = bazaar.Ticker.MovingWeekSells;
            var movingWeekBuys = bazaar.Ticker.MovingWeekBuys;
            var spread = askOrderPrice - bidOrderPrice;
            var totalWeekVolume = movingWeekSells + movingWeekBuys;
            var potentialProfitMultiplier = askOrderPrice / bidOrderPrice;
            var buyingPower = (float)movingWeekSells / movingWeekBuys;
            var friendlyName = item?.Name ?? ProductIdToName(bazaar.ProductId);

            string? skinUrl = null;
            if (item?.Skin?.Value is not null)
            {
                try
                {
                    var json = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(item.Skin.Value));
                    using var doc = System.Text.Json.JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("textures", out var textures) &&
                        textures.TryGetProperty("SKIN", out var skin) &&
                        skin.TryGetProperty("url", out var url))
                    {
                        skinUrl = url.GetString();
                    }
                }
                catch
                {
                    // Ignore skin parse errors
                }
            }

            double flipScore;
            bool isManipulated;
            double manipulationIntensity;
            double deviationPercent;

            if (changedKeyToIndex.TryGetValue(bazaar.ProductId, out var changedIdx))
            {
                flipScore = opportunityScores[changedIdx];
                var ms = manipulationScores[changedIdx];
                isManipulated = ms.IsManipulated;
                manipulationIntensity = ms.ManipulationIntensity;
                deviationPercent = ms.DeviationPercent;
            }
            else
            {
                var cached = bazaarRunCache.GetCachedScores(bazaar.ProductId);
                if (cached is not null)
                {
                    flipScore = cached.FlipOpportunityScore;
                    isManipulated = cached.IsManipulated;
                    manipulationIntensity = cached.ManipulationIntensity;
                    deviationPercent = cached.PriceDeviationPercent;
                }
                else
                {
                    flipScore = 0;
                    isManipulated = false;
                    manipulationIntensity = 0;
                    deviationPercent = 0;
                }
            }

            productList.Add(new ProductData
            {
                ItemId = bazaar.ProductId,
                Item = new Item
                {
                    FriendlyName = friendlyName,
                    Tier = item?.Tier ?? ItemTier.Common,
                    Unstackable = item?.Unstackable ?? false,
                    SkinUrl = skinUrl
                },
                Bid = new OrderInfo
                {
                    Last = bazaar.Ticker.BestBidPrice,
                    OrderPrice = bidOrderPrice,
                    WeekVolume = movingWeekSells,
                    CurrentOrders = bazaar.Ticker.ActiveBidOrders,
                    CurrentVolume = bazaar.Ticker.TotalBidVolume,
                    OrderBook = bazaar.Bids.Select(x => new OrderBook
                    {
                        UnitPrice = x.PricePerUnit,
                        Orders = x.OrderCount,
                        Amount = x.Amount
                    })
                },
                Ask = new OrderInfo
                {
                    Last = bazaar.Ticker.BestAskPrice,
                    OrderPrice = askOrderPrice,
                    WeekVolume = movingWeekBuys,
                    CurrentOrders = bazaar.Ticker.ActiveAskOrders,
                    CurrentVolume = bazaar.Ticker.TotalAskVolume,
                    OrderBook = bazaar.Asks.Select(x => new OrderBook
                    {
                        UnitPrice = x.PricePerUnit,
                        Orders = x.OrderCount,
                        Amount = x.Amount
                    })
                },
                OrderMeta = new OrderMeta
                {
                    PotentialProfitMultiplier = potentialProfitMultiplier,
                    Spread = spread,
                    TotalWeekVolume = totalWeekVolume,
                    BidOrderPower = buyingPower,
                    FlipOpportunityScore = flipScore,
                    IsManipulated = isManipulated,
                    ManipulationIntensity = manipulationIntensity,
                    PriceDeviationPercent = deviationPercent
                }
            });
        }

        return (productList, changedKeys);
    }

    private static string ProductIdToName(string productId)
    {
        var split = productId.Split("_");
        if (!int.TryParse(split.Last(), out var level)) return productId.Humanize().Titleize();

        var nameParts = split.Take(split.Length - 1).ToArray();
        var baseName = string.Join(" ", nameParts).Humanize().Titleize();
        return $"{baseName} {level.ToRoman()}";
    }
}
