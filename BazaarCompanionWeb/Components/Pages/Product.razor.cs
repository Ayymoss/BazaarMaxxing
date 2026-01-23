using BazaarCompanionWeb.Components.Pages.Components;
using BazaarCompanionWeb.Components.Pages.Dialogs.Components;
using BazaarCompanionWeb.Dtos;
using BazaarCompanionWeb.Entities;
using BazaarCompanionWeb.Interfaces.Database;
using BazaarCompanionWeb.Services;
using BazaarCompanionWeb.Utilities;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.SignalR.Client;
using Serilog;

namespace BazaarCompanionWeb.Components.Pages;

public partial class Product(
    IProductRepository productRepository,
    TimeCache timeCache,
    MarketAnalyticsService marketAnalyticsService,
    OrderBookAnalysisService orderBookAnalysisService,
    ComparisonStateService comparisonStateService,
    NavigationManager navigationManager) : ComponentBase, IAsyncDisposable
{
    [Parameter] public required string ProductKey { get; set; }
    
    private PriceGraph? _priceGraph;
    private HubConnection? _hubConnection;
    private ProductDataInfo? _product;
    private ProductDataInfo? ProductData => _product;

    private CancellationTokenSource? _cancellationTokenSource;
    private bool _loading = true;
    private DateTimeOffset? _lastServerRefresh;

    internal CandleInterval _selectedInterval = CandleInterval.FifteenMinute;
    private List<RelatedProduct> _relatedProducts = [];

    // Order book analysis
    private OrderBookAnalysisResult? _orderBookAnalysis;
    private bool _showOrderBookAnalysis;

    // Comparison state
    private bool _isInComparison;

    private Task OnIntervalChangedAsync(CandleInterval interval)
    {
        _selectedInterval = interval;
        StateHasChanged();
        return Task.CompletedTask;
    }

    protected override async Task OnInitializedAsync()
    {
        _cancellationTokenSource = new CancellationTokenSource();
        _isInComparison = comparisonStateService.Contains(ProductKey);
        comparisonStateService.OnChange += OnComparisonStateChanged;

        await FetchProductDataAsync(_cancellationTokenSource.Token);
        await LoadRelatedProductsAsync(_cancellationTokenSource.Token);

        // Setup SignalR
        _hubConnection = new HubConnectionBuilder()
            .WithUrl(navigationManager.ToAbsoluteUri("/hubs/products"))
            .WithAutomaticReconnect()
            .Build();

        _hubConnection.On<ProductDataInfo>("ProductUpdated", async (product) =>
        {
            if (_product is not null)
            {
                // Preserve PriceHistory as it's not sent in the live update DTO to save bandwidth
                product.PriceHistory = _product.PriceHistory;
                
                // Only update books if the incoming data has them
                product.BuyBook ??= _product.BuyBook;
                product.SellBook ??= _product.SellBook;
            }
            
            _product = product;
            _lastServerRefresh = timeCache.LastUpdated;
            
            // Trigger UI re-render to update TradingDesk and other components
            await InvokeAsync(StateHasChanged);
        });

        _hubConnection.On<object>("TickUpdated", async (tick) =>
        {
            if (_priceGraph is not null)
            {
                await _priceGraph.UpdateTickAsync(tick);
            }
        });
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender && _hubConnection is not null)
        {
            try
            {
                await _hubConnection.StartAsync();
                await _hubConnection.SendAsync("JoinProductGroup", ProductKey);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to start SignalR connection for {ProductKey}", ProductKey);
            }
        }
    }

    private void OnComparisonStateChanged()
    {
        _isInComparison = comparisonStateService.Contains(ProductKey);
        InvokeAsync(StateHasChanged);
    }

    private async Task LoadRelatedProductsAsync(CancellationToken ct = default)
    {
        try
        {
            _relatedProducts = await marketAnalyticsService.GetRelatedProductsAsync(ProductKey, 5, ct);
            await InvokeAsync(StateHasChanged);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Error loading related products for {ProductKey}", ProductKey);
        }
    }

    private async Task FetchProductDataAsync(CancellationToken ct = default)
    {
        try
        {
            _product = await productRepository.GetProductAsync(ProductKey, ct);
            _lastServerRefresh = timeCache.LastUpdated;

            // Load order book analysis if we have order book data
            if (_product?.BuyBook is not null && _product.SellBook is not null)
            {
                _orderBookAnalysis = await orderBookAnalysisService.AnalyzeAsync(ProductKey, ct);
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
            _loading = false;
        }
    }

    private (double Buy, double Sell) GetLastPriceHistoryAverage()
    {
        var buy = 0d;
        var sell = 0d;

        if (_product?.PriceHistory is not null)
        {
            buy = _product.PriceHistory.OrderByDescending(x => x.Date).First().Buy;
            sell = _product.PriceHistory.OrderByDescending(x => x.Date).First().Sell;
        }

        return (buy, sell);
    }

    private void ToggleOrderBookAnalysis()
    {
        _showOrderBookAnalysis = !_showOrderBookAnalysis;
    }

    private void ToggleCompare()
    {
        if (_isInComparison)
        {
            comparisonStateService.Remove(ProductKey);
        }
        else
        {
            comparisonStateService.Add(ProductKey);
        }
    }

    private string GetCompareButtonClasses()
    {
        const string baseClasses = "flex items-center gap-2 px-4 py-2 rounded-lg text-sm font-medium transition-all";
        
        return _isInComparison
            ? $"{baseClasses} bg-blue-600/20 text-blue-400 border border-blue-500/50"
            : $"{baseClasses} bg-slate-800 text-slate-300 border border-slate-700/50 hover:bg-slate-700 hover:text-white";
    }

    public async ValueTask DisposeAsync()
    {
        comparisonStateService.OnChange -= OnComparisonStateChanged;
        
        if (_hubConnection is not null)
        {
            try
            {
                // Only send if connection is active
                if (_hubConnection.State == HubConnectionState.Connected)
                {
                    await _hubConnection.SendAsync("LeaveProductGroup", ProductKey);
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Error leaving product group during dispose");
            }
            
            await _hubConnection.DisposeAsync();
        }

        _cancellationTokenSource?.Cancel();
        _cancellationTokenSource?.Dispose();
    }
}
