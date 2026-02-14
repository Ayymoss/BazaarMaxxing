using BazaarCompanionWeb.Entities;
using BazaarCompanionWeb.Services;
using BazaarCompanionWeb.Utilities;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace BazaarCompanionWeb.Components.Pages.Dialogs.Components;

public partial class IndexPriceGraph : ComponentBase, IAsyncDisposable
{
    private const string IndicatorStorageKey = "klinechart_index_indicators";

    [Parameter] public required string IndexSlug { get; set; }
    [Parameter] public required string IndexName { get; set; }
    [Parameter] public CandleInterval Interval { get; set; } = CandleInterval.OneHour;
    [Parameter] public EventCallback<CandleInterval> IntervalChanged { get; set; }

    [Inject] private IJSRuntime JSRuntime { get; set; } = null!;
    [Inject] private IndexAggregationService IndexAggregationService { get; set; } = null!;
    [Inject] private BrowserStorage BrowserStorage { get; set; } = null!;

    private IJSObjectReference? _chartModule;
    private readonly string _chartId = Guid.NewGuid().ToString("N")[..8];
    private bool _chartInitialized;
    private bool _indicatorsApplied;
    private bool _indicatorsLoaded;
    private bool _disposed;
    private readonly CancellationTokenSource _disposalCts = new();

    // Overlay indicators (on main candle pane)
    private readonly List<KLineIndicatorOption> _overlayIndicators =
    [
        new("MA", "MA", false),
        new("EMA", "EMA", false),
        new("SMA", "SMA", false),
        new("BOLL", "BOLL", false),
        new("SAR", "SAR", false),
    ];

    // Sub-pane indicators
    // NOTE: VOL and MACD are added by the JS layout by default
    // We set them to false here because: 
    // - VOL is meaningless for indices (aggregated data has no volume)
    // - MACD is already in the layout, we just leave it as-is
    private readonly List<KLineIndicatorOption> _subPaneIndicators =
    [
        new("VOL", "VOL", false), // Disabled by default for indices (volume = 0)
        new("MACD", "MACD", false), // Already in layout; false = don't try to add again
        new("RSI", "RSI", false),
        new("KDJ", "KDJ", false),
        new("OBV", "OBV", false),
        new("ROC", "ROC", false)
    ];

    private sealed class KLineIndicatorOption(string name, string label, bool enabled, string? paneId = null)
    {
        public string Name { get; } = name;
        public string Label { get; } = label;
        public bool Enabled { get; set; } = enabled;
        public string? PaneId { get; set; } = paneId;
    }

    private sealed record IndicatorState(Dictionary<string, bool> Indicators);

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            await InitializeChartAsync();
        }
    }

    private async Task OnIntervalChangedAsync(CandleInterval newInterval)
    {
        Interval = newInterval;
        await IntervalChanged.InvokeAsync(Interval);
        if (_chartInitialized)
        {
            await RecreateChartAsync();
        }
    }

    private async Task InitializeChartAsync(CancellationToken ct = default)
    {
        if (_disposed) return;
        
        try
        {
            _chartModule = await JSRuntime.InvokeAsync<IJSObjectReference>("import", ct, "./js/chartInit.js");
            await LoadIndicatorStateAsync(ct);
            await CreateChartAsync(ct);
        }
        catch (JSException ex)
        {
            Console.WriteLine($"Error initializing index chart: {ex.Message}");
        }
    }

    private async Task LoadIndicatorStateAsync(CancellationToken ct = default)
    {
        if (_indicatorsLoaded) return;

        try
        {
            var state = await BrowserStorage.GetAsync<IndicatorState>(IndicatorStorageKey);
            if (state?.Indicators is not null)
            {
                foreach (var indicator in _overlayIndicators)
                {
                    if (state.Indicators.TryGetValue(indicator.Name, out var enabled))
                    {
                        indicator.Enabled = enabled;
                    }
                }

                foreach (var indicator in _subPaneIndicators)
                {
                    if (state.Indicators.TryGetValue(indicator.Name, out var enabled))
                    {
                        indicator.Enabled = enabled;
                    }
                }
            }

            _indicatorsLoaded = true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error loading indicator state: {ex.Message}");
        }
    }

    private async Task SaveIndicatorStateAsync(CancellationToken ct = default)
    {
        try
        {
            Dictionary<string, bool> indicators = [];

            foreach (var indicator in _overlayIndicators)
            {
                indicators[indicator.Name] = indicator.Enabled;
            }

            foreach (var indicator in _subPaneIndicators)
            {
                indicators[indicator.Name] = indicator.Enabled;
            }

            await BrowserStorage.SetAsync(IndicatorStorageKey, new IndicatorState(indicators));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error saving indicator state: {ex.Message}");
        }
    }

    private async Task RecreateChartAsync(CancellationToken ct = default)
    {
        if (_chartModule is null) return;
        _indicatorsApplied = false;
        await CreateChartAsync(ct);
    }

    private async Task CreateChartAsync(CancellationToken ct = default)
    {
        if (_chartModule is null) return;

        try
        {
            // Load aggregated candles from the index service
            var ohlcData = await IndexAggregationService.GetAggregatedCandlesAsync(IndexSlug, Interval, 200, ct);

            if (ohlcData.Count == 0)
            {
                return;
            }

            // KLineChart format: timestamp in milliseconds
            var ohlcDataForKLine = ohlcData.Select(c => new
            {
                time = new DateTimeOffset(c.Time).ToUnixTimeMilliseconds(),
                open = c.Open,
                high = c.High,
                low = c.Low,
                close = c.Close,
                volume = c.Volume,
                askClose = c.AskClose
            }).ToList();

            // Create the KLineChart with lazy loading (productKey "index:slug" routes to index API)
            await _chartModule.InvokeVoidAsync("createKLineChart",
                $"chart-container-{_chartId}",
                ohlcDataForKLine,
                new
                {
                    productName = IndexName,
                    productKey = $"index:{IndexSlug}",
                    interval = (int)Interval
                });

            if (!_chartInitialized)
            {
                _chartInitialized = true;
                StateHasChanged();
            }

            if (!_indicatorsApplied)
            {
                await ApplyEnabledIndicatorsAsync(ct);
                _indicatorsApplied = true;
            }
        }
        catch (JSException ex)
        {
            Console.WriteLine($"Error creating index chart: {ex.Message}");
        }
    }

    private async Task ApplyEnabledIndicatorsAsync(CancellationToken ct = default)
    {
        if (_chartModule is null) return;

        try
        {
            // Apply overlay indicators that are enabled
            foreach (var indicator in _overlayIndicators.Where(i => i.Enabled))
            {
                await _chartModule.InvokeVoidAsync("toggleKLineIndicator",
                    $"chart-container-{_chartId}",
                    indicator.Name,
                    true,
                    "candle_pane");
            }

            // Handle VOL - it's in the layout by default, so REMOVE if disabled
            var volIndicator = _subPaneIndicators.First(i => i.Name == "VOL");
            if (!volIndicator.Enabled)
            {
                await _chartModule.InvokeVoidAsync("toggleKLineIndicator",
                    $"chart-container-{_chartId}",
                    "VOL",
                    false,
                    "vol_pane");
            }

            // Handle MACD - it's in the layout by default, so REMOVE if disabled
            var macdIndicator = _subPaneIndicators.First(i => i.Name == "MACD");
            if (!macdIndicator.Enabled)
            {
                await _chartModule.InvokeVoidAsync("toggleKLineIndicator",
                    $"chart-container-{_chartId}",
                    "MACD",
                    false,
                    "macd_pane");
            }

            // Apply any additional sub-pane indicators that are enabled (excluding VOL/MACD)
            foreach (var indicator in _subPaneIndicators.Where(i => i.Enabled && i.Name != "VOL" && i.Name != "MACD"))
            {
                await _chartModule.InvokeVoidAsync("toggleKLineIndicator",
                    $"chart-container-{_chartId}",
                    indicator.Name,
                    true,
                    indicator.PaneId ?? indicator.Name.ToLowerInvariant() + "_pane");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error applying indicators: {ex.Message}");
        }
    }

    private async Task ToggleIndicatorAsync(KLineIndicatorOption indicator, bool enabled)
    {
        if (_chartModule is null || !_chartInitialized) return;

        indicator.Enabled = enabled;

        try
        {
            var isOverlay = _overlayIndicators.Contains(indicator);
            var paneId = isOverlay ? "candle_pane" : indicator.PaneId;

            await _chartModule.InvokeVoidAsync("toggleKLineIndicator",
                $"chart-container-{_chartId}",
                indicator.Name,
                enabled,
                paneId ?? indicator.Name.ToLowerInvariant() + "_pane");

            if (!isOverlay && enabled && indicator.PaneId is null)
            {
                indicator.PaneId = indicator.Name.ToLowerInvariant() + "_pane";
            }

            await SaveIndicatorStateAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error toggling indicator {indicator.Name}: {ex.Message}");
        }
    }

    public async ValueTask DisposeAsync()
    {
        _disposed = true;

        await _disposalCts.CancelAsync();
        _disposalCts.Dispose();

        if (_chartModule is not null)
        {
            try
            {
                await _chartModule.InvokeVoidAsync("disposeKLineChart", $"chart-container-{_chartId}");
                await _chartModule.DisposeAsync();
            }
            catch (JSDisconnectedException)
            {
                // Ignore if JS is disconnected
            }
            catch (TaskCanceledException)
            {
                // Expected during disposal
            }
        }
    }
}
