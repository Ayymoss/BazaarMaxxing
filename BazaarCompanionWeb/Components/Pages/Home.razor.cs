using BazaarCompanionWeb.Components.Pages.Components;

namespace BazaarCompanionWeb.Components.Pages;

public partial class Home
{
    private ProductList? _productList;

    private async Task OnInsightProductClicked(string productKey)
    {
        // Trigger product detail dialog through ProductList
        if (_productList is not null)
        {
            await _productList.ShowProductDetailAsync(productKey);
        }
    }
}
