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
using Microsoft.Extensions.Options;
using Humanizer;
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
    ProductUpdateBus updateBus) : ComponentBase, IAsyncDisposable
{
    [Parameter] public required string ProductKey { get; set; }
    
    private PriceGraph? _priceGraph;
    private IDisposable? _updateSubscription;
    private ProductDataInfo? _product;
    private ProductDataInfo? ProductData => _product;

    private CancellationTokenSource? _cancellationTokenSource;
    private bool _loading = true;
    private DateTimeOffset? _lastServerRefresh;
    private DateTimeOffset? _lastLivePush;

    // Rendered footer state. Recomputed every second by the ticker; the page only re-renders when
    // one of these strings actually changes, so "now" -> "5 seconds ago" -> ... advances on its own.
    private string _lastRefreshText = "…";
    private string _liveDotClass = "bg-amber-500";

    internal CandleInterval _selectedInterval = CandleInterval.OneHour;
    private List<RelatedProduct> _relatedProducts = [];
    private bool _relatedProductsLoaded;
    private bool _relatedProductsFailed;
    private bool _disposed;

    // Order book analysis
    private OrderBookAnalysisResult? _orderBookAnalysis;
    private bool _showOrderBookAnalysis;

    // Recent candles for the right-panel sparkline (fills far sooner than daily PriceSnapshots)
    private List<OhlcDataPoint> _sparkCandles = [];

    // Ticks once a second to keep the humanized "Last Updated" text and live dot current
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

        // Live updates arrive in-process from the Hypixel poll (ProductUpdateBus) — the page is
        // InteractiveServer, so there is no network hop and nothing to reconnect.
        if (!_disposed)
            _updateSubscription = updateBus.Subscribe(ProductKey, OnLiveUpdateAsync);

        RefreshFooter();
        _refreshTimer = new Timer(_ => OnRefreshTick(), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
    }

    /// <summary>Recompute the footer strings; true if anything visible changed.</summary>
    private bool RefreshFooter()
    {
        var text = _lastServerRefresh?.Humanize() ?? "…";
        var dot = LiveDotClass();
        if (text == _lastRefreshText && dot == _liveDotClass) return false;
        _lastRefreshText = text;
        _liveDotClass = dot;
        return true;
    }

    private void OnRefreshTick()
    {
        if (_disposed || !RefreshFooter()) return;
        try
        {
            _ = InvokeAsync(StateHasChanged);
        }
        catch (Exception ex)
        {
            // A timer callback must never throw — that takes the whole process down.
            Log.Warning(ex, "Footer refresh failed for {ProductKey}", ProductKey);
        }
    }

    private async Task OnLiveUpdateAsync(ProductDataInfo product, LiveTick tick)
    {
        if (_disposed) return;

        if (_product is not null)
        {
            // Preserve PriceHistory as it's not carried in the live update DTO
            product.PriceHistory = _product.PriceHistory;

            // Only update books if the incoming data has them
            product.BidBook ??= _product.BidBook;
            product.AskBook ??= _product.AskBook;
        }

        _product = product;
        _lastServerRefresh = timeCache.LastUpdated;
        _lastLivePush = TimeProvider.System.GetLocalNow();
        RefreshFooter();

        if (_priceGraph is not null)
            await _priceGraph.UpdateTickAsync(tick);

        // Re-render TradingDesk and the rest of the page
        await InvokeAsync(StateHasChanged);
    }

    /// <summary>
    /// The footer dot is only green while we are subscribed AND the poll has pushed to this product
    /// recently. A product whose top-of-book has not moved gets no push — that is stale-by-design and
    /// shows amber rather than a false "live".
    /// </summary>
    private string LiveDotClass()
    {
        if (_updateSubscription is null) return "bg-red-500";
        var age = TimeProvider.System.GetLocalNow() - (_lastLivePush ?? _lastServerRefresh);
        return age is { } a && a < TimeSpan.FromSeconds(uiConfig.Value.LiveStaleAfterSeconds) ? "bg-emerald-500" : "bg-amber-500";
    }

    private string LiveDotTitle() => _updateSubscription is null
        ? "Not subscribed to live updates"
        : _lastLivePush is { } t
            ? $"Last live push at {t:HH:mm:ss}"
            : "No live push yet — waiting for the next poll to change this product";

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

        _updateSubscription?.Dispose();
        _updateSubscription = null;

        _refreshTimer?.Dispose();
        _cancellationTokenSource?.Cancel();
        _cancellationTokenSource?.Dispose();
    }
}
