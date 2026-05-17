using BazaarCompanionWeb.Dtos;
using BazaarCompanionWeb.Entities;

namespace BazaarCompanionWeb.Interfaces.Database;

public interface IOhlcRepository
{
    /// <summary>
    /// Bulk inserts pre-built EFPriceTick rows via Npgsql binary COPY. Drops EF change-tracking
    /// overhead and is ~10-50x faster than AddRange+SaveChanges for large batches.
    /// </summary>
    Task CopyTicksAsync(IReadOnlyList<EFPriceTick> ticks, CancellationToken ct = default);
    Task<List<OhlcDataPoint>> GetCandlesAsync(string productKey, CandleInterval interval, int limit = 100, CancellationToken ct = default);

    /// <summary>
    /// Bulk load candles for many products in one or few queries. Returns up to limitPerProduct candles per product (most recent first by PeriodStart), in chronological order per product.
    /// </summary>
    Task<IReadOnlyDictionary<string, List<OhlcDataPoint>>> GetCandlesBulkAsync(IReadOnlyList<string> productKeys, CandleInterval interval, int limitPerProduct, CancellationToken ct = default);

    /// <summary>
    /// Get candles before a specific timestamp (for backward pagination/lazy loading)
    /// </summary>
    Task<List<OhlcDataPoint>> GetCandlesBeforeAsync(string productKey, CandleInterval interval, DateTime before, int limit = 100, CancellationToken ct = default);
    
    Task<List<EFPriceTick>> GetTicksForAggregationAsync(string productKey, DateTime since, CancellationToken ct = default);

    /// <summary>
    /// Bulk load ticks for many products since the given time. Returns ticks grouped by ProductKey.
    /// </summary>
    Task<IReadOnlyDictionary<string, List<EFPriceTick>>> GetTicksForAggregationBulkAsync(IReadOnlyList<string> productKeys, DateTime since, CancellationToken ct = default);
    Task SaveCandlesAsync(IEnumerable<EFOhlcCandle> candles, CancellationToken ct = default);
    Task<DateTime?> GetLatestCandleTimeAsync(string productKey, CandleInterval interval, CancellationToken ct = default);

    /// <summary>
    /// Loads all watermarks for incremental aggregation. Returns dictionary keyed by (ProductKey, Interval).
    /// </summary>
    Task<IReadOnlyDictionary<(string, CandleInterval), EFOhlcAggregationState>> GetAggregationStatesAsync(CancellationToken ct = default);

    /// <summary>
    /// Bulk upserts watermarks. Existing rows are updated; new ones inserted.
    /// </summary>
    Task UpsertAggregationStatesAsync(IReadOnlyList<EFOhlcAggregationState> states, CancellationToken ct = default);
    Task<List<string>> GetAllProductKeysAsync(CancellationToken ct = default);
    Task PruneOldTicksAsync(TimeSpan retention, CancellationToken ct = default);
    Task PruneOldCandlesAsync(CancellationToken ct = default);
    Task VacuumDatabaseAsync(CancellationToken ct = default);
}
