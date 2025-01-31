using BazaarCompanionWeb.Dtos;
using BazaarCompanionWeb.Entities;

namespace BazaarCompanionWeb.Interfaces.Database;

public interface IProductRepository
{
    Task<DateTime> GetLastUpdatedAsync(CancellationToken cancellationToken);
    Task UpdateOrAddProductsAsync(List<EFProduct> products, CancellationToken cancellationToken);
    Task<List<PriceHistorySnapshot>> GetPriceHistoryAsync(Guid productGuid, CancellationToken cancellationToken);
    Task<List<Order>> GetOrderBookAsync(int marketDataId, CancellationToken cancellationToken);
    Task<ProductDataInfo> GetProductAsync(Guid productGuid, CancellationToken cancellationToken);
}
