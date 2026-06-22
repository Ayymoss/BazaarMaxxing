using System.Globalization;
using System.Text;
using BazaarCompanionWeb.Components.Pages.Components;
using BazaarCompanionWeb.Components.Pages.Dialogs.Components;
using BazaarCompanionWeb.Configurations;
using BazaarCompanionWeb.Dtos;
using BazaarCompanionWeb.Entities;
using BazaarCompanionWeb.Interfaces.Database;
using BazaarCompanionWeb.Services;
using BazaarCompanionWeb.Utilities;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Options;
using Serilog;

namespace BazaarCompanionWeb.Components.Pages;

public partial class Product(
    ProductDataCache productDataCache,
    TimeCache timeCache,
    MarketAnalyticsService marketAnalyticsService,
    OrderBookAnalysisService orderBookAnalysisService,
    ComparisonStateService comparisonStateService,
    LastTradedPriceService lastTradedPriceService,
    IOhlcRepository ohlcRepository,
    IOptions<UIConfig> uiConfig,
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

    internal CandleInterval _selectedInterval = CandleInterval.OneHour;
    private List<RelatedProduct> _relatedProducts = [];
    private bool _relatedProductsLoaded;
    private bool _relatedProductsFailed;
    private bool _joinedHubGroup;
    private bool _disposed;

    // Order book analysis
    private OrderBookAnalysisResult? _orderBookAnalysis;
    private bool _showOrderBookAnalysis;

    // Recent candles for the right-panel sparkline (fills far sooner than daily PriceSnapshots)
    private List<OhlcDataPoint> _sparkCandles = [];

    // Timer to refresh humanized "Last Updated" text
    private Timer? _refreshTimer;

    // Comparison state
    private bool _isInComparison;

    // Center workspace tabs: chart | book | analysis | trade | related
    private string _activeTab = "chart";
    private void SetTab(string tab) => _activeTab = tab;

    private Task OnIntervalChangedAsync(CandleInterval interval)
    {
        _selectedInterval = interval;
        StateHasChanged();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Builds an SVG polyline (and up/down direction) for the right-panel trend.
    /// Prefers recent OHLC candles (populate within minutes); falls back to daily price-history snapshots.
    /// </summary>
    private (string Points, bool Up) Sparkline(double w, double h)
    {
        var vals = _sparkCandles.Count >= 2
            ? _sparkCandles.Select(c => c.Close).ToList()
            : (_product?.PriceHistory ?? []).OrderBy(x => x.Date).Select(x => (x.Bid + x.Ask) / 2).ToList();
        if (vals.Count < 2) return (string.Empty, true);

        double min = vals.Min(), max = vals.Max(), range = max - min;
        if (range <= 0) range = 1;

        var sb = new StringBuilder();
        for (var i = 0; i < vals.Count; i++)
        {
            var x = (double)i / (vals.Count - 1) * w;
            var y = h - (vals[i] - min) / range * h;
            sb.Append(x.ToString("0.#", CultureInfo.InvariantCulture)).Append(',')
              .Append(y.ToString("0.#", CultureInfo.InvariantCulture)).Append(' ');
        }
        return (sb.ToString().Trim(), vals[^1] >= vals[0]);
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
                product.BidBook ??= _product.BidBook;
                product.AskBook ??= _product.AskBook;
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

        // Refresh the humanized "Last Updated" text on the configured cadence. Live data flows via SignalR.
        var interval = TimeSpan.FromSeconds(uiConfig.Value.LastUpdatedRefreshSeconds);
        _refreshTimer = new Timer(_ => InvokeAsync(StateHasChanged), null, interval, interval);
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender && _hubConnection is not null && !_disposed)
        {
            try
            {
                await _hubConnection.StartAsync();
                // Re-check disposed after async gap — user may have navigated away mid-handshake.
                if (_disposed) return;
                await _hubConnection.SendAsync("JoinProductGroup", ProductKey);
                _joinedHubGroup = true;
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
        _relatedProductsFailed = false;
        try
        {
            _relatedProducts = await marketAnalyticsService.GetRelatedProductsAsync(ProductKey, uiConfig.Value.RelatedProductsLimit, ct);
            _relatedProductsLoaded = true;
        }
        catch (Exception ex)
        {
            _relatedProductsFailed = true;
            Log.Warning(ex, "Error loading related products for {ProductKey}", ProductKey);
        }
        finally
        {
            await InvokeAsync(StateHasChanged);
        }
    }

    private async Task FetchProductDataAsync(CancellationToken ct = default)
    {
        try
        {
            _product = await productDataCache.GetProductAsync(ProductKey, ct);
            if (_product is not null)
            {
                _product.EstimatedLastTradedPrice ??= lastTradedPriceService.GetEstimate(ProductKey);
                _sparkCandles = await ohlcRepository.GetCandlesAsync(ProductKey, CandleInterval.OneHour, 72, ct);
            }
            _lastServerRefresh = timeCache.LastUpdated;

            // Load order book analysis if we have order book data
            if (_product?.BidBook is not null && _product.AskBook is not null)
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

    private (double Bid, double Ask) GetLastPriceHistoryAverage()
    {
        var bid = 0d;
        var ask = 0d;

        // PriceHistory can be an empty list (fresh DB, no snapshots yet) — guard before First().
        if (_product?.PriceHistory is { Count: > 0 } history)
        {
            var latest = history.OrderByDescending(x => x.Date).First();
            bid = latest.Bid;
            ask = latest.Ask;
        }

        return (bid, ask);
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
        _disposed = true;
        comparisonStateService.OnChange -= OnComparisonStateChanged;

        if (_hubConnection is not null)
        {
            try
            {
                // Only LeaveProductGroup if we actually Joined. Otherwise the server has no
                // matching subscription and we'd just log a warning on the hub side.
                if (_joinedHubGroup && _hubConnection.State == HubConnectionState.Connected)
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

        _refreshTimer?.Dispose();
        _cancellationTokenSource?.Cancel();
        _cancellationTokenSource?.Dispose();
    }
}
