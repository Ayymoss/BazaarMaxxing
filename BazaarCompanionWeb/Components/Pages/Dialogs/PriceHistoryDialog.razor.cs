using BazaarCompanionWeb.Dtos;
using BazaarCompanionWeb.Interfaces.Database;
using Microsoft.AspNetCore.Components;

namespace BazaarCompanionWeb.Components.Pages.Dialogs;

public partial class PriceHistoryDialog(IProductRepository productRepository) : ComponentBase, IDisposable
{
    [Parameter] public required ProductDataInfo Product { get; set; }

    private System.Timers.Timer? _refreshTimer;
    private CancellationTokenSource? _cancellationTokenSource;
    private bool _loading = true;
    private DateTime? _lastServerRefresh;

    protected override async Task OnInitializedAsync()
    {
        _cancellationTokenSource = new CancellationTokenSource();

        await FetchProductDataAsync(_cancellationTokenSource.Token);

        _refreshTimer = new System.Timers.Timer(TimeSpan.FromMinutes(1).TotalMilliseconds);
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
            var lastUpdated = Task.Run(() => productRepository.GetLastUpdatedAsync(cancellationToken), cancellationToken);
            var product = Task.Run(() => productRepository.GetProductAsync(Product.ProductGuid, cancellationToken),
                cancellationToken);

            await Task.WhenAll(lastUpdated, product);

            _lastServerRefresh = await lastUpdated;
            Product = await product;
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("Data fetch was canceled.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error fetching data: {ex.Message}");
        }
        finally
        {
            _loading = false;
            StateHasChanged();
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
