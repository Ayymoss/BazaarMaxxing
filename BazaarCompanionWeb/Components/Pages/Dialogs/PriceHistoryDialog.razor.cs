using BazaarCompanionWeb.Dtos;
using BazaarCompanionWeb.Interfaces.Database;
using Microsoft.AspNetCore.Components;

namespace BazaarCompanionWeb.Components.Pages.Dialogs;

public partial class PriceHistoryDialog(IProductRepository productRepository) : ComponentBase
{
    [Parameter] public required ProductDataInfo Product { get; set; }

    private bool _loading;

    protected override Task OnInitializedAsync()
    {
        if (Product.PriceHistory is not null) return Task.CompletedTask;

        var cancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource.CancelAfter(TimeSpan.FromMilliseconds(2000));

        _loading = true;
        var snapshots = productRepository.GetPriceHistoryAsync(Product.ProductGuid, cancellationTokenSource.Token)
            .ContinueWith(task => Product.PriceHistory = task.Result, cancellationTokenSource.Token);

        var buyBook = productRepository.GetOrderBookAsync(Product.BuyMarketDataId, cancellationTokenSource.Token)
            .ContinueWith(x => Product.BuyBook = x.Result.OrderBy(y => y.UnitPrice).ToList(), cancellationTokenSource.Token);

        var sellBook = productRepository.GetOrderBookAsync(Product.SellMarketDataId, cancellationTokenSource.Token)
            .ContinueWith(x => Product.SellBook = x.Result.OrderByDescending(y => y.UnitPrice).ToList(), cancellationTokenSource.Token);

        return Task.WhenAll(snapshots, buyBook, sellBook).ContinueWith(x => InvokeAsync(() =>
        {
            _loading = false;
            StateHasChanged();
        }), cancellationTokenSource.Token);
    }
}
