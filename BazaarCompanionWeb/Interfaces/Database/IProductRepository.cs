using BazaarCompanionWeb.Dtos;
using BazaarCompanionWeb.Entities;

namespace BazaarCompanionWeb.Interfaces.Database;

public interface IProductRepository
{
    Task UpdateOrAddProductsAsync(List<EFProduct> products, CancellationToken cancellationToken);
    Task<List<PriceHistorySnapshot>> GetPriceHistoryAsync(string productKey, CancellationToken cancellationToken);
    Task<List<Order>> GetOrderBookAsync(int marketDataId, CancellationToken cancellationToken);
    Task<ProductDataInfo> GetProductAsync(string productKey, CancellationToken cancellationToken);
    Task<List<ProductDataInfo>> GetProductsAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Resolves keys and regex patterns to matching product keys. Entries with <c>regex:</c> prefix are treated as PostgreSQL regex patterns.
    /// </summary>
    Task<List<string>> GetProductKeysMatchingAsync(IEnumerable<string> keysOrPatterns, CancellationToken ct = default);

    /// <summary>
    /// Bulk load products by exact keys. Returns lightweight ProductDataInfo (no PriceHistory, BidBook, AskBook).
    /// </summary>
    Task<List<ProductDataInfo>> GetProductsByKeysAsync(IReadOnlyList<string> productKeys, CancellationToken ct = default);

    /// <summary>
    /// Returns product keys that have TotalWeekVolume >= minVolume. Used to exclude low-volume items
    /// that are likely to be pruned by cleanup jobs and would truncate index aggregation.
    /// </summary>
    Task<List<string>> GetProductKeysWithMinVolumeAsync(IEnumerable<string> productKeys, double minVolume, CancellationToken ct = default);
    Task<List<EFPriceSnapshot>> GetPriceSnapshotsAsync(CancellationToken ct = default);
    
    /// <summary>
    /// Deletes products that haven't been seen in the API response for the specified number of days.
    /// </summary>
    Task<int> DeleteStaleProductsAsync(int staleAfterDays = 2, CancellationToken ct = default);
}
