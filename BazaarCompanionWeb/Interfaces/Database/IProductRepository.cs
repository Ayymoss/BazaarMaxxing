using BazaarCompanionWeb.Dtos;
using BazaarCompanionWeb.Entities;

namespace BazaarCompanionWeb.Interfaces.Database;

public interface IProductRepository
{
    Task UpdateOrAddProductsAsync(List<EFProduct> products, CancellationToken cancellationToken);
    Task<List<PriceHistorySnapshot>> GetPriceHistoryAsync(string productKey, CancellationToken cancellationToken);
    Task<List<Order>> GetOrderBookAsync(int marketDataId, CancellationToken cancellationToken);
    Task<ProductDataInfo> GetProductAsync(string productKey, CancellationToken cancellationToken);
}
