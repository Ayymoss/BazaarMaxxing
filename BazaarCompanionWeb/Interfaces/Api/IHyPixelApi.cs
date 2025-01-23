using BazaarCompanionWeb.Models.Api.Bazaar;
using BazaarCompanionWeb.Models.Api.Items;
using Refit;

namespace BazaarCompanionWeb.Interfaces.Api;

public interface IHyPixelApi
{
    [Get("/v2/skyblock/bazaar")]
    Task<BazaarResponse> GetBazaarAsync();

    [Get("/v2/resources/skyblock/items")]
    Task<ItemResponse> GetItemsAsync();
}
