using System.Globalization;
using BazaarCompanionWeb.Dtos;
using BazaarCompanionWeb.Interfaces.Database;
using Microsoft.AspNetCore.Components;

namespace BazaarCompanionWeb.Components.Pages.Dialogs.Components;

public partial class PriceGraph(IProductRepository productRepository)
{

    [Parameter] public required ProductDataInfo Product { get; set; }

    private bool _loading;

    protected override void OnInitialized()
    {
        if (Product.PriceHistory is not null) return;

        var cancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource.CancelAfter(TimeSpan.FromMilliseconds(2000));

        _loading = true;

        _ = Task.Run(async () =>
        {
            var snapshots = await productRepository.GetPriceHistoryAsync(Product.Guid, cancellationTokenSource.Token);

            await InvokeAsync(() =>
            {
                Product.PriceHistory = snapshots;
                _loading = false;
                StateHasChanged();
            });
        }, cancellationTokenSource.Token);
    }

    private string FormatAsUsd(object value)
    {
        return ((double)value).ToString("C0", CultureInfo.CreateSpecificCulture("en-US"));
    }
}
