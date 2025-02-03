using BazaarCompanionWeb.Components.Pages.Components;
using BazaarCompanionWeb.Dtos;
using BazaarCompanionWeb.Interfaces.Database;
using BazaarCompanionWeb.Utilities;
using Microsoft.AspNetCore.Components;
using Serilog;

namespace BazaarCompanionWeb.Components.Pages.Dialogs;

public partial class PriceHistoryDialog(IProductRepository productRepository, TimeCache timeCache) : ComponentBase, IDisposable
{
    [Parameter] public required ProductDataInfo Product { get; set; }

    private System.Timers.Timer? _refreshTimer;
    private CancellationTokenSource? _cancellationTokenSource;
    private bool _loading = true;
    private DateTimeOffset? _lastServerRefresh;

    private StatCard? _buyRef;
    private StatCard? _sellRef;

    protected override async Task OnInitializedAsync()
    {
        _cancellationTokenSource = new CancellationTokenSource();

        await FetchProductDataAsync(_cancellationTokenSource.Token);

        _refreshTimer = new System.Timers.Timer(TimeSpan.FromSeconds(32).TotalMilliseconds);
        _refreshTimer.Elapsed += async (sender, e) => await OnRefreshTimerElapsed();
        _refreshTimer.AutoReset = true;
        _refreshTimer.Enabled = true;
    }

    private async Task OnRefreshTimerElapsed()
    {
        await (_cancellationTokenSource?.CancelAsync() ?? Task.CompletedTask);
        _cancellationTokenSource = new CancellationTokenSource();
        await InvokeAsync(() => FetchProductDataAsync(_cancellationTokenSource.Token));
    }

    private async Task FetchProductDataAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            Product = await productRepository.GetProductAsync(Product.ItemId, cancellationToken);
            _lastServerRefresh = timeCache.LastUpdated;
        }
        catch (OperationCanceledException)
        {
            Log.Information("Data fetch was canceled");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error fetching data: {ExMessage}", ex.Message);
        }
        finally
        {
            await InvokeAsync(() =>
            {
                if (Product.PriceHistory is not null)
                {
                    // I feel like there's a better way to do this. I'm not sure of it.
                    // Cascading Value/Parameter maybe...
                    _buyRef?.UpdateValues(Product.BuyOrderUnitPrice ?? double.MaxValue, Product.PriceHistory.Average(x => x.Buy));
                    _sellRef?.UpdateValues(Product.SellOrderUnitPrice ?? 0.1, Product.PriceHistory.Average(x => x.Sell));
                }

                _loading = false;
                StateHasChanged();
            });
        }
    }

    public void Dispose()
    {
        _refreshTimer?.Stop();
        _refreshTimer?.Dispose();
        _cancellationTokenSource?.Cancel();
        _cancellationTokenSource?.Dispose();
    }
}
