using BazaarCompanionWeb.Configurations;
using BazaarCompanionWeb.Dtos;
using BazaarCompanionWeb.Entities;
using BazaarCompanionWeb.Interfaces.Database;
using Microsoft.Extensions.Options;

namespace BazaarCompanionWeb.Services;

public class IndexAggregationService(
    IOhlcRepository ohlcRepository,
    IOptions<List<IndexConfiguration>> options)
{
    private readonly IOhlcRepository _ohlcRepository = ohlcRepository;
    private readonly List<IndexConfiguration> _indices = options.Value;

    public async Task<List<OhlcDataPoint>> GetAggregatedCandlesAsync(string slug, CandleInterval interval, int limit, CancellationToken ct = default)
    {
        // 1. Find index by slug (return empty if not found)
        var index = _indices.FirstOrDefault(i => i.Slug.Equals(slug, StringComparison.OrdinalIgnoreCase));
        if (index == null)
        {
            return [];
        }

        // 2. Fetch candles for all product keys in the index
        var productTasks = index.ProductKeys.Select(pk => _ohlcRepository.GetCandlesAsync(pk, interval, limit));
        var productCandlesResults = await Task.WhenAll(productTasks);

        // 3. Filter out products with no data
        // 4. Find first valid candle for each product to use as basePrice
        var validProductsData = productCandlesResults
            .Where(candles => candles != null && candles.Count > 0)
            .Select(candles => new
            {
                Candles = candles,
                // Using the first candle's Close as basePrice (Step 4)
                BasePrice = candles[0].Close,
                CandleMap = candles.ToDictionary(c => c.Time)
            })
            .Where(x => x.BasePrice > 0) // Ensure basePrice is valid for normalization
            .ToList();

        if (validProductsData.Count == 0)
        {
            return [];
        }

        // 6. Find INTERSECTION of timestamps
        var commonTimestamps = validProductsData
            .SelectMany(d => d.CandleMap.Keys)
            .GroupBy(t => t)
            .Where(g => g.Count() == validProductsData.Count)
            .Select(g => g.Key)
            .ToList();

        if (commonTimestamps.Count == 0)
        {
            return [];
        }

        // 7. For each common timestamp:
        var aggregatedCandles = commonTimestamps.Select(time =>
        {
            double sumOpen = 0, sumHigh = 0, sumLow = 0, sumClose = 0, sumAskClose = 0;

            foreach (var data in validProductsData)
            {
                var candle = data.CandleMap[time];
                var basePrice = data.BasePrice;

                // 5. Normalize: (price / basePrice) * 100
                sumOpen += (candle.Open / basePrice) * 100;
                sumHigh += (candle.High / basePrice) * 100;
                sumLow += (candle.Low / basePrice) * 100;
                sumClose += (candle.Close / basePrice) * 100;
                
                // AskClose = Average Normalized AskClose (if available) or 0
                if (candle.AskClose > 0)
                {
                    sumAskClose += (candle.AskClose / basePrice) * 100;
                }
            }

            int count = validProductsData.Count;
            return new OhlcDataPoint(
                time,
                sumOpen / count,
                sumHigh / count,
                sumLow / count,
                sumClose / count,
                0, // Volume = 0
                0, // Spread = 0
                sumAskClose / count
            );
        })
        // 8. Return list sorted by time.
        .OrderBy(c => c.Time)
        .ToList();

        return aggregatedCandles;
    }
}
