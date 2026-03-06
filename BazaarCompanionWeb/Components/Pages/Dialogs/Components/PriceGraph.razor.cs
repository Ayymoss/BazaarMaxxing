using BazaarCompanionWeb.Dtos;
using BazaarCompanionWeb.Entities;
using BazaarCompanionWeb.Interfaces.Database;
using BazaarCompanionWeb.Utilities;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using System.Text.Json;

namespace BazaarCompanionWeb.Components.Pages.Dialogs.Components;

public partial class PriceGraph : ComponentBase, IAsyncDisposable
{
    private const string IndicatorStorageKey = "klinechart_indicators";

    [Parameter] public required ProductDataInfo Product { get; set; }
    [Parameter] public CandleInterval Interval { get; set; } = CandleInterval.OneHour;
    [Parameter] public EventCallback<CandleInterval> IntervalChanged { get; set; }

    [Inject] private IJSRuntime JSRuntime { get; set; } = null!;
    [Inject] private IOhlcRepository OhlcRepository { get; set; } = null!;
    [Inject] private BrowserStorage BrowserStorage { get; set; } = null!;

    private IJSObjectReference? _chartModule;
    private readonly string _chartId = Guid.NewGuid().ToString("N")[..8];
    private bool _chartInitialized;
    private bool _indicatorsApplied; // Track if initial indicators have been applied
    private bool _indicatorsLoaded; // Track if we've loaded from localStorage
    private bool _disposed; // Track disposal to prevent JS calls after component is disposed
    private readonly CancellationTokenSource _disposalCts = new();

    // KLineChart indicator configuration
    // Overlay indicators appear on the main candle pane
    private readonly List<KLineIndicatorOption> _overlayIndicators =
    [
        new("ASK_LINE", "ASK", true), // ASK price overlay - enabled by default
        new("MA", "MA", false), // Moving Average - disabled by default (can enable)
        new("EMA", "EMA", false), // Exponential Moving Average  
        new("SMA", "SMA", false), // Simple Moving Average
        new("BOLL", "BOLL", false), // Bollinger Bands
        new("SAR", "SAR", false), // Parabolic SAR
    ];

    // Sub-pane indicators appear in separate panes below the main chart
    // VOL and MACD are added via layout, so they start enabled
    private readonly List<KLineIndicatorOption> _subPaneIndicators =
    [
        new("VOL", "VOL", true), // Volume (enabled via layout)
        new("MACD", "MACD", true), // MACD (enabled via layout)
        new("RSI", "RSI", false), // Relative Strength Index
        new("KDJ", "KDJ", false), // KDJ Stochastic
        new("OBV", "OBV", false), // On-Balance Volume
        new("ROC", "ROC", false) // Rate of Change
    ];

    /// <summary>
    /// Represents a KLineChart indicator option
    /// </summary>
    private class KLineIndicatorOption(string name, string label, bool enabled, string? paneId = null)
    {
        public string Name { get; } = name;
        public string Label { get; } = label;
        public bool Enabled { get; set; } = enabled;
        public string? PaneId { get; set; } = paneId; // null = create new pane, or specific pane ID
    }

    /// <summary>
    /// DTO for persisting indicator state to localStorage
    /// </summary>
    private record IndicatorState(Dictionary<string, bool> Indicators);

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        // ONLY initialize on first render - do NOT update chart on subsequent renders
        // Chart updates happen via: interval change, tick updates, or indicator toggles
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
            // Recreate chart with new interval data
            await RecreateChartAsync();
        }
    }

    private async Task InitializeChartAsync()
    {
        try
        {
            _chartModule = await JSRuntime.InvokeAsync<IJSObjectReference>("import", "./js/chartInit.js");

            // Load saved indicator state from localStorage
            await LoadIndicatorStateAsync();

            await CreateChartAsync();
        }
        catch (JSException ex)
        {
            Console.WriteLine($"Error initializing chart: {ex.Message}");
        }
    }

    /// <summary>
    /// Load indicator enabled states from localStorage
    /// </summary>
    private async Task LoadIndicatorStateAsync()
    {
        if (_indicatorsLoaded) return;

        try
        {
            var state = await BrowserStorage.GetAsync<IndicatorState>(IndicatorStorageKey);
            if (state?.Indicators is not null)
            {
                // Apply saved state to overlay indicators
                foreach (var indicator in _overlayIndicators)
                {
                    if (state.Indicators.TryGetValue(indicator.Name, out var enabled))
                    {
                        indicator.Enabled = enabled;
                    }
                }

                // Apply saved state to sub-pane indicators
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

    /// <summary>
    /// Save indicator enabled states to localStorage
    /// </summary>
    private async Task SaveIndicatorStateAsync()
    {
        try
        {
            var indicators = new Dictionary<string, bool>();

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

    /// <summary>
    /// Recreate the chart (used when interval changes)
    /// </summary>
    private async Task RecreateChartAsync()
    {
        if (_chartModule is null) return;
        _indicatorsApplied = false; // Reset so indicators get reapplied
        await CreateChartAsync();
    }

    public async Task UpdateTickAsync(object tick)
    {
        // Bail early if component is disposed or chart not ready
        if (_disposed || _chartModule is null || !_chartInitialized) return;

        try
        {
            // Parse the incoming tick (which is typically a JsonElement from SignalR)
            var json = tick.ToString();
            if (string.IsNullOrEmpty(json)) return;

            var liveTick = JsonSerializer.Deserialize<LiveTick>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (liveTick is null) return;

            // ALIGN the tick time to the current interval bucket start
            var bucketedTime = liveTick.Time.GetPeriodStart(Interval);

            // KLineChart uses timestamp in milliseconds
            var processedTick = new
            {
                time = new DateTimeOffset(bucketedTime).ToUnixTimeMilliseconds(),
                open = liveTick.Open,
                high = liveTick.High,
                low = liveTick.Low,
                close = liveTick.Close,
                volume = liveTick.Volume,
                askClose = liveTick.AskClose
            };

            // Use KLineChart tick update - pass cancellation token to handle disposal
            await _chartModule.InvokeVoidAsync("updateKLineChartWithTick", _disposalCts.Token, $"chart-container-{_chartId}",
                processedTick);
        }
        catch (TaskCanceledException)
        {
            // Expected during component disposal - silently ignore
        }
        catch (JSDisconnectedException)
        {
            // Expected when navigating away - silently ignore
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error updating tick: {ex.Message}");
        }
    }

    /// <summary>
    /// Create the KLineChart with data - called once on init and when interval changes
    /// </summary>
    private async Task CreateChartAsync()
    {
        if (_chartModule is null) return;

        try
        {
            // Load initial 200 candles (more data loads lazily when user pans left)
            var ohlcData = await OhlcRepository.GetCandlesAsync(Product.ItemId, Interval, limit: 200);

            if (ohlcData.Count == 0)
            {
                return;
            }

            // APPEND current price as the absolute latest tick
            var bucketedTime = DateTime.UtcNow.GetPeriodStart(Interval);
            var lastCandle = ohlcData.LastOrDefault();
            var price = Product.BidUnitPrice;

            if (lastCandle != null && lastCandle.Time == bucketedTime)
            {
                // Replace the last candle with updated values (records are immutable)
                var askPrice = Product.AskUnitPrice;
                ohlcData[^1] = lastCandle with
                {
                    High = Math.Max(lastCandle.High, price),
                    Low = Math.Min(lastCandle.Low, price),
                    Close = price,
                    AskClose = askPrice // Update ASK price as well
                };
            }
            else
            {
                // Add a new partial candle
                var askPrice = Product.AskUnitPrice;
                ohlcData.Add(new OhlcDataPoint(
                    Time: bucketedTime,
                    Open: price,
                    High: price,
                    Low: price,
                    Close: price,
                    Volume: 0d,
                    Spread: 0d,
                    AskClose: askPrice
                ));
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

            // Create the KLineChart with lazy loading support
            // Pass productKey and interval for API-based historical data loading
            await _chartModule.InvokeVoidAsync("createKLineChart",
                $"chart-container-{_chartId}",
                ohlcDataForKLine,
                new
                {
                    productName = Product.ItemFriendlyName,
                    productKey = Product.ItemId,
                    interval = (int)Interval
                });

            // Mark as initialized - do NOT call StateHasChanged here to avoid render loop
            if (!_chartInitialized)
            {
                _chartInitialized = true;
                // Only trigger one re-render to hide the loading overlay
                StateHasChanged();
            }

            // Apply overlay indicators that are enabled (only once per chart creation)
            if (!_indicatorsApplied)
            {
                await ApplyEnabledIndicatorsAsync();
                _indicatorsApplied = true;
            }
        }
        catch (JSException ex)
        {
            Console.WriteLine($"Error creating chart: {ex.Message}");
        }
    }

    /// <summary>
    /// Apply indicator state after chart creation
    /// VOL and MACD are in the layout by default - need to remove them if disabled
    /// </summary>
    private async Task ApplyEnabledIndicatorsAsync()
    {
        if (_chartModule is null) return;

        try
        {
            // Apply overlay indicators that are enabled
            // Skip ASK_LINE here - it's added automatically after chart creation
            // and handled via toggleIndicator when disabled
            foreach (var indicator in _overlayIndicators.Where(i => i.Enabled && i.Name != "ASK_LINE"))
            {
                await _chartModule.InvokeVoidAsync("toggleKLineIndicator",
                    $"chart-container-{_chartId}",
                    indicator.Name,
                    true,
                    "candle_pane");
            }

            // Handle ASK_LINE specially - it's added by default in JS, so only remove if disabled
            var askLineIndicator = _overlayIndicators.First(i => i.Name == "ASK_LINE");
            if (!askLineIndicator.Enabled)
            {
                await _chartModule.InvokeVoidAsync("toggleKLineIndicator",
                    $"chart-container-{_chartId}",
                    "ASK_LINE",
                    false,
                    "candle_pane");
            }

            // Handle VOL and MACD - they're in the layout, so remove if disabled
            var volIndicator = _subPaneIndicators.First(i => i.Name == "VOL");
            if (!volIndicator.Enabled)
            {
                await _chartModule.InvokeVoidAsync("toggleKLineIndicator",
                    $"chart-container-{_chartId}",
                    "VOL",
                    false,
                    "vol_pane");
            }

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

    /// <summary>
    /// Toggle an indicator on/off
    /// </summary>
    private async Task ToggleIndicatorAsync(KLineIndicatorOption indicator, bool enabled)
    {
        if (_chartModule is null || !_chartInitialized) return;

        indicator.Enabled = enabled;

        try
        {
            // Determine the pane ID - overlay indicators go on candle_pane, 
            // sub-pane indicators create their own pane or use existing
            var isOverlay = _overlayIndicators.Contains(indicator);
            var paneId = isOverlay ? "candle_pane" : indicator.PaneId;

            await _chartModule.InvokeVoidAsync("toggleKLineIndicator",
                $"chart-container-{_chartId}",
                indicator.Name,
                enabled,
                paneId ?? indicator.Name.ToLowerInvariant() + "_pane");

            // Update the pane ID if we created a new one
            if (!isOverlay && enabled && indicator.PaneId is null)
            {
                indicator.PaneId = indicator.Name.ToLowerInvariant() + "_pane";
            }

            // Persist indicator state to localStorage
            await SaveIndicatorStateAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error toggling indicator {indicator.Name}: {ex.Message}");
        }
    }

    public async ValueTask DisposeAsync()
    {
        // Mark as disposed first to stop any pending tick updates
        _disposed = true;

        // Cancel any pending JS interop calls
        await _disposalCts.CancelAsync();
        _disposalCts.Dispose();

        if (_chartModule is not null)
        {
            try
            {
                // Use KLineChart dispose
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
