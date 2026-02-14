using BazaarCompanionWeb.Configurations;
using BazaarCompanionWeb.Dtos;
using BazaarCompanionWeb.Entities;
using BazaarCompanionWeb.Interfaces.Database;
using Microsoft.Extensions.Options;

namespace BazaarCompanionWeb.Services;

public class IndexAggregationService(
    IOhlcRepository ohlcRepository,
    IProductRepository productRepository,
    IOptions<List<IndexConfiguration>> options)
{
    private readonly List<IndexConfiguration> _indices = options.Value;

    public async Task<List<OhlcDataPoint>> GetAggregatedCandlesAsync(string slug, CandleInterval interval, int limit, CancellationToken ct = default)
    {
        // 1. Find index by slug (return empty if not found)
        var index = _indices.FirstOrDefault(i => i.Slug.Equals(slug, StringComparison.OrdinalIgnoreCase));
        if (index == null)
        {
            return [];
        }

        // 2. Resolve keys/patterns to actual product keys
        var resolvedKeys = await productRepository.GetProductKeysMatchingAsync(index.ProductKeys, ct);
        if (resolvedKeys.Count == 0)
            return [];

        // 3. Exclude low-volume products (likely pruned by cleanup; would truncate index if included)
        const double minVolume = 100;
        var volumeFilteredKeys = await productRepository.GetProductKeysWithMinVolumeAsync(resolvedKeys, minVolume, ct);
        if (volumeFilteredKeys.Count == 0)
            return [];

        var productCandlesMap = await ohlcRepository.GetCandlesBulkAsync(volumeFilteredKeys, interval, limit, ct);
        var productCandlesResults = volumeFilteredKeys
            .Where(pk => productCandlesMap.TryGetValue(pk, out var candles) && candles.Count > 0)
            .Select(pk => productCandlesMap[pk])
            .ToArray();

        // 4. Filter out products with no data; find first valid candle for basePrice
        var validProductsData = productCandlesResults
            .Where(candles => candles != null && candles.Count > 0)
            .Select(candles => new
            {
                Candles = candles,
                BasePrice = candles[0].Close,
                CandleMap = candles.ToDictionary(c => c.Time)
            })
            .Where(x => x.BasePrice > 0)
            .ToList();

        if (validProductsData.Count == 0)
            return [];

        // 5. Partial aggregation: use UNION of timestamps (not intersection)
        //    For each timestamp, average the normalized values of products that have data there.
        //    New items no longer truncate the index; we display whatever is possible.
        const int minProductsPerTimestamp = 1;
        var allTimestamps = validProductsData
            .SelectMany(d => d.CandleMap.Keys)
            .Distinct()
            .OrderBy(t => t)
            .ToList();

        var aggregatedCandles = allTimestamps
            .Select(time =>
            {
                var productsAtTime = validProductsData
                    .Where(d => d.CandleMap.TryGetValue(time, out _))
                    .ToList();

                if (productsAtTime.Count < minProductsPerTimestamp)
                    return null;

                double sumOpen = 0, sumHigh = 0, sumLow = 0, sumClose = 0, sumAskClose = 0;
                foreach (var data in productsAtTime)
                {
                    var candle = data.CandleMap[time];
                    var basePrice = data.BasePrice;
                    sumOpen += (candle.Open / basePrice) * 100;
                    sumHigh += (candle.High / basePrice) * 100;
                    sumLow += (candle.Low / basePrice) * 100;
                    sumClose += (candle.Close / basePrice) * 100;
                    if (candle.AskClose > 0)
                        sumAskClose += (candle.AskClose / basePrice) * 100;
                }

                int count = productsAtTime.Count;
                return new OhlcDataPoint(
                    time,
                    sumOpen / count,
                    sumHigh / count,
                    sumLow / count,
                    sumClose / count,
                    0, 0, sumAskClose / count);
            })
            .Where(c => c != null)
            .Cast<OhlcDataPoint>()
            .OrderBy(c => c.Time)
            .ToList();

        return aggregatedCandles;
    }

    /// <summary>
    /// Get aggregated candles before a timestamp (for lazy loading when user pans left).
    /// </summary>
    public async Task<List<OhlcDataPoint>> GetAggregatedCandlesBeforeAsync(string slug, CandleInterval interval, DateTime before, int limit, CancellationToken ct = default)
    {
        var index = _indices.FirstOrDefault(i => i.Slug.Equals(slug, StringComparison.OrdinalIgnoreCase));
        if (index == null)
            return [];

        var resolvedKeys = await productRepository.GetProductKeysMatchingAsync(index.ProductKeys, ct);
        if (resolvedKeys.Count == 0)
            return [];

        const double minVolume = 100;
        var volumeFilteredKeys = await productRepository.GetProductKeysWithMinVolumeAsync(resolvedKeys, minVolume, ct);
        if (volumeFilteredKeys.Count == 0)
            return [];

        var productCandlesResults = await Task.WhenAll(
            volumeFilteredKeys.Select(pk => ohlcRepository.GetCandlesBeforeAsync(pk, interval, before, limit, ct)));

        var productCandlesMap = volumeFilteredKeys
            .Zip(productCandlesResults, (pk, candles) => (pk, candles))
            .Where(x => x.candles.Count > 0)
            .ToDictionary(x => x.pk, x => x.candles);

        var validProductsData = productCandlesMap
            .Select(kvp => new
            {
                Candles = kvp.Value,
                BasePrice = kvp.Value[0].Close,
                CandleMap = kvp.Value.ToDictionary(c => c.Time)
            })
            .Where(x => x.BasePrice > 0)
            .ToList();

        if (validProductsData.Count == 0)
            return [];

        const int minProductsPerTimestamp = 1;
        var allTimestamps = validProductsData
            .SelectMany(d => d.CandleMap.Keys)
            .Distinct()
            .OrderBy(t => t)
            .ToList();

        var aggregatedCandles = allTimestamps
            .Select(time =>
            {
                var productsAtTime = validProductsData
                    .Where(d => d.CandleMap.TryGetValue(time, out _))
                    .ToList();

                if (productsAtTime.Count < minProductsPerTimestamp)
                    return null;

                double sumOpen = 0, sumHigh = 0, sumLow = 0, sumClose = 0, sumAskClose = 0;
                foreach (var data in productsAtTime)
                {
                    var candle = data.CandleMap[time];
                    var basePrice = data.BasePrice;
                    sumOpen += (candle.Open / basePrice) * 100;
                    sumHigh += (candle.High / basePrice) * 100;
                    sumLow += (candle.Low / basePrice) * 100;
                    sumClose += (candle.Close / basePrice) * 100;
                    if (candle.AskClose > 0)
                        sumAskClose += (candle.AskClose / basePrice) * 100;
                }
                int count = productsAtTime.Count;
                return new OhlcDataPoint(
                    time,
                    sumOpen / count,
                    sumHigh / count,
                    sumLow / count,
                    sumClose / count,
                    0, 0, sumAskClose / count);
            })
            .Where(c => c != null)
            .Cast<OhlcDataPoint>()
            .OrderBy(c => c.Time)
            .ToList();

        return aggregatedCandles;
    }
}
