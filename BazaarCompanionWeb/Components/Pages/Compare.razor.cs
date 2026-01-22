using BazaarCompanionWeb.Dtos;
using BazaarCompanionWeb.Entities;
using BazaarCompanionWeb.Interfaces.Database;
using BazaarCompanionWeb.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Serilog;

namespace BazaarCompanionWeb.Components.Pages;

public partial class Compare : IAsyncDisposable
{
    [Inject] private ComparisonStateService ComparisonState { get; set; } = null!;
    [Inject] private IOhlcRepository OhlcRepository { get; set; } = null!;
    [Inject] private IProductRepository ProductRepository { get; set; } = null!;
    [Inject] private IJSRuntime JS { get; set; } = null!;

    private readonly string _chartId = Guid.NewGuid().ToString("N")[..8];
    private IJSObjectReference? _chartModule;
    
    private List<string> _productKeys = [];
    private Dictionary<string, ProductDataInfo> _productData = new();
    private CandleInterval _selectedInterval = CandleInterval.FifteenMinute;
    private bool _loading;
    
    // Search
    private string _searchQuery = string.Empty;
    private List<ProductDataInfo> _searchResults = [];
    private CancellationTokenSource? _searchCts;

    private static readonly string[] _colors = ["#3B82F6", "#10B981", "#F59E0B", "#EF4444"];

    private static readonly (string Label, CandleInterval Value)[] _intervals =
    [
        ("5m", CandleInterval.FiveMinute),
        ("15m", CandleInterval.FifteenMinute),
        ("1h", CandleInterval.OneHour),
        ("4h", CandleInterval.FourHour),
        ("1d", CandleInterval.OneDay),
        ("1w", CandleInterval.OneWeek)
    ];

    protected override async Task OnInitializedAsync()
    {
        _productKeys = [..ComparisonState.ProductKeys];
        ComparisonState.OnChange += OnComparisonStateChanged;
        
        await LoadProductDataAsync();
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            _chartModule = await JS.InvokeAsync<IJSObjectReference>("import", "./js/tradingview-chart.js");
            if (_productKeys.Count > 0)
            {
                await UpdateChartAsync();
            }
        }
    }

    private async void OnComparisonStateChanged()
    {
        _productKeys = [..ComparisonState.ProductKeys];
        await LoadProductDataAsync();
        await InvokeAsync(StateHasChanged);
        await UpdateChartAsync();
    }

    private async Task LoadProductDataAsync()
    {
        foreach (var productKey in _productKeys.Where(k => !_productData.ContainsKey(k)))
        {
            try
            {
                var product = await ProductRepository.GetProductAsync(productKey, default);
                if (product is not null)
                {
                    _productData[productKey] = product;
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to load product data for {ProductKey}", productKey);
            }
        }
    }

    private void RemoveProduct(string productKey)
    {
        ComparisonState.Remove(productKey);
        _productData.Remove(productKey);
    }

    private void ClearAll()
    {
        ComparisonState.Clear();
        _productData.Clear();
    }

    private async Task AddProduct(string productKey)
    {
        if (ComparisonState.Add(productKey))
        {
            _searchQuery = string.Empty;
            _searchResults.Clear();
            
            try
            {
                var product = await ProductRepository.GetProductAsync(productKey, default);
                if (product is not null)
                {
                    _productData[productKey] = product;
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to load product data for {ProductKey}", productKey);
            }
            
            await UpdateChartAsync();
        }
    }

    private async Task SetInterval(CandleInterval interval)
    {
        _selectedInterval = interval;
        await UpdateChartAsync();
    }

    private async Task UpdateChartAsync()
    {
        if (_chartModule is null || _productKeys.Count is 0) return;

        _loading = true;
        StateHasChanged();

        try
        {
            var allCandles = new Dictionary<string, List<OhlcDataPoint>>();

            foreach (var productKey in _productKeys)
            {
                var candles = await OhlcRepository.GetCandlesAsync(productKey, _selectedInterval, limit: 100);
                if (candles.Count > 0)
                {
                    allCandles[productKey] = candles;
                }
            }

            if (allCandles.Count > 0)
            {
                var normalizedData = new Dictionary<string, object>();

                foreach (var kvp in allCandles)
                {
                    var candles = kvp.Value.OrderBy(c => c.Time).ToList();
                    if (candles.Count is 0) continue;

                    var firstClose = candles[0].Close;
                    if (firstClose <= 0) continue;

                    var normalized = candles.Select(c => new
                    {
                        time = c.Time,
                        value = ((c.Close - firstClose) / firstClose) * 100
                    }).ToArray();

                    normalizedData[kvp.Key] = normalized;
                }

                await _chartModule.InvokeVoidAsync("createComparisonChart",
                    $"comparison-chart-{_chartId}",
                    normalizedData,
                    _productKeys.ToArray());
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Error updating comparison chart");
        }
        finally
        {
            _loading = false;
            await InvokeAsync(StateHasChanged);
        }
    }

    private string GetIntervalButtonClasses(CandleInterval interval)
    {
        const string baseClasses = "px-2 py-1 rounded text-[10px] font-medium transition-all";
        return _selectedInterval == interval
            ? $"{baseClasses} bg-blue-600 text-white"
            : $"{baseClasses} text-slate-400 hover:text-white hover:bg-slate-800";
    }

    // Search functionality with debounce
    private async Task OnSearchInput(ChangeEventArgs e)
    {
        _searchQuery = e.Value?.ToString() ?? string.Empty;
        
        _searchCts?.Cancel();
        _searchCts = new CancellationTokenSource();
        
        if (string.IsNullOrWhiteSpace(_searchQuery))
        {
            _searchResults.Clear();
            return;
        }

        try
        {
            await Task.Delay(300, _searchCts.Token);
            
            var allProducts = await ProductRepository.GetProductsAsync(_searchCts.Token);
            _searchResults = allProducts
                .Where(p => p.ItemFriendlyName.Contains(_searchQuery, StringComparison.OrdinalIgnoreCase)
                         || p.ItemId.Contains(_searchQuery, StringComparison.OrdinalIgnoreCase))
                .Where(p => !_productKeys.Contains(p.ItemId))
                .Take(5)
                .ToList();
                
            StateHasChanged();
        }
        catch (OperationCanceledException)
        {
            // Debounce cancelled
        }
    }

    public async ValueTask DisposeAsync()
    {
        ComparisonState.OnChange -= OnComparisonStateChanged;
        _searchCts?.Cancel();
        _searchCts?.Dispose();
        
        if (_chartModule is not null)
        {
            try
            {
                await _chartModule.InvokeVoidAsync("disposeChart", $"comparison-chart-{_chartId}");
                await _chartModule.DisposeAsync();
            }
            catch (JSDisconnectedException)
            {
                // Ignore if JS is disconnected
            }
        }
    }
}
