using BazaarCompanionWeb.Components.Pages.Components;
using BazaarCompanionWeb.Dtos;
using BazaarCompanionWeb.Entities;
using BazaarCompanionWeb.Interfaces.Database;
using BazaarCompanionWeb.Services;
using BazaarCompanionWeb.Utilities;
using Microsoft.AspNetCore.Components;
using Serilog;

namespace BazaarCompanionWeb.Components.Pages.Dialogs;

public partial class PriceHistoryDialog(
    IProductRepository productRepository,
    TimeCache timeCache,
    MarketAnalyticsService marketAnalyticsService,
    OrderBookAnalysisService orderBookAnalysisService) : ComponentBase, IDisposable
{
    [Parameter] public required ProductDataInfo Product { get; set; }

    private System.Timers.Timer? _refreshTimer;
    private CancellationTokenSource? _cancellationTokenSource;
    private bool _loading = true;
    private DateTimeOffset? _lastServerRefresh;
    private bool _intention;

    private StatCard? _buyRef;
    private StatCard? _sellRef;
    private StatCard? _spreadRef;

    private double _spreadLast;
    private double _spreadOpen;
    internal CandleInterval _selectedInterval = CandleInterval.FifteenMinute;
    private List<RelatedProduct> _relatedProducts = new();

    // Order book analysis
    private OrderBookAnalysisResult? _orderBookAnalysis;
    private bool _showOrderBookAnalysis;

    private Task OnIntervalChangedAsync(CandleInterval interval)
    {
        _selectedInterval = interval;
        StateHasChanged();
        return Task.CompletedTask;
    }

    protected override async Task OnInitializedAsync()
    {
        _cancellationTokenSource = new CancellationTokenSource();

        await FetchProductDataAsync(_cancellationTokenSource.Token);
        await LoadRelatedProductsAsync(_cancellationTokenSource.Token);

        _refreshTimer = new System.Timers.Timer(TimeSpan.FromSeconds(12).TotalMilliseconds);
        _refreshTimer.Elapsed += async (sender, e) => await OnRefreshTimerElapsed();
        _refreshTimer.AutoReset = true;
        _refreshTimer.Enabled = true;
    }

    private async Task LoadRelatedProductsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _relatedProducts = await marketAnalyticsService.GetRelatedProductsAsync(Product.ItemId, 5, cancellationToken);
            await InvokeAsync(StateHasChanged);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Error loading related products for {ProductKey}", Product.ItemId);
        }
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

            // Load order book analysis if we have order book data
            if (Product.BuyBook is not null && Product.SellBook is not null)
            {
                _orderBookAnalysis = await orderBookAnalysisService.AnalyzeAsync(Product.ItemId, cancellationToken);
            }
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
                    var history = GetLastPriceHistoryAverage();
                    _buyRef?.UpdateValues(Product.BuyOrderUnitPrice ?? double.MaxValue, history.Buy);
                    _sellRef?.UpdateValues(Product.SellOrderUnitPrice ?? 0.1, history.Sell);
                    _spreadRef?.UpdateValues(Product.OrderMetaMargin, history.Buy - history.Sell);
                }

                _loading = false;
                StateHasChanged();
            });
        }
    }

    private (double Buy, double Sell ) GetLastPriceHistoryAverage()
    {
        var buy = 0d;
        var sell = 0d;

        if (Product.PriceHistory is not null)
        {
            buy = Product.PriceHistory.OrderByDescending(x => x.Date).First().Buy;
            sell = Product.PriceHistory.OrderByDescending(x => x.Date).First().Sell;
        }

        return (buy, sell);
    }

    private void ToggleOrderBookAnalysis()
    {
        _showOrderBookAnalysis = !_showOrderBookAnalysis;
    }

    private void NavigateToProduct(string productKey)
    {
        // Close current dialog and open new one
        // This would need to be handled by parent component
        // For now, we'll just log - parent component should handle navigation
        Log.Information("Navigate to product: {ProductKey}", productKey);
    }

    public void Dispose()
    {
        _refreshTimer?.Stop();
        _refreshTimer?.Dispose();
        _cancellationTokenSource?.Cancel();
        _cancellationTokenSource?.Dispose();
    }
}

