using BazaarCompanion.Models.Api.Bazaar;
using BazaarCompanion.Models.Api.Items;
using Refit;

namespace BazaarCompanion.Interfaces;

public interface IHyPixelApi
{
    [Get("/v2/skyblock/bazaar")]
    Task<BazaarResponse> GetBazaarAsync();

    [Get("/v2/resources/skyblock/items")]
    Task<ItemResponse> GetItemsAsync();
}
