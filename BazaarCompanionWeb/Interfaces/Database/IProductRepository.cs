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
    Task<List<EFPriceSnapshot>> GetPriceSnapshotsAsync(CancellationToken ct = default);
    
    /// <summary>
    /// Deletes products that haven't been seen in the API response for the specified number of days.
    /// </summary>
    Task<int> DeleteStaleProductsAsync(int staleAfterDays = 2, CancellationToken ct = default);
}
