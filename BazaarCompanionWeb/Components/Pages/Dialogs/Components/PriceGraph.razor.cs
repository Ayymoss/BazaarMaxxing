using BazaarCompanionWeb.Dtos;
using BazaarCompanionWeb.Entities;
using BazaarCompanionWeb.Interfaces.Database;
using BazaarCompanionWeb.Services;
using BazaarCompanionWeb.Utilities;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using System.Text.Json;

namespace BazaarCompanionWeb.Components.Pages.Dialogs.Components;

public partial class PriceGraph : ComponentBase, IAsyncDisposable
{
    private const string IndicatorStorageKey = "klinechart_indicators";
    
    [Parameter] public required ProductDataInfo Product { get; set; }
    [Parameter] public CandleInterval Interval { get; set; } = CandleInterval.FifteenMinute;
    [Parameter] public EventCallback<CandleInterval> IntervalChanged { get; set; }

    [Inject] private IJSRuntime JSRuntime { get; set; } = null!;
    [Inject] private IOhlcRepository OhlcRepository { get; set; } = null!;
    [Inject] private TechnicalAnalysisService TechnicalAnalysisService { get; set; } = null!;
    [Inject] private BrowserStorage BrowserStorage { get; set; } = null!;

    private IJSObjectReference? _chartModule;
    private readonly string _chartId = Guid.NewGuid().ToString("N")[..8];
    private bool _chartInitialized;
    private bool _indicatorsApplied; // Track if initial indicators have been applied
    private bool _indicatorsLoaded; // Track if we've loaded from localStorage
    
    // [LIGHTWEIGHT_CHARTS] Commented out for KLineChart test
    // private ChartIndicatorConfig _indicatorConfig = new();
    // private List<TechnicalIndicator> _indicators = new();
    // private List<IndicatorDataPoint> _spreadData = new();
    // private List<SupportResistanceLevel> _supportResistanceLevels = new();
    
    // KLineChart indicator configuration
    // Overlay indicators appear on the main candle pane
    private readonly List<KLineIndicatorOption> _overlayIndicators =
    [
        new("MA", "MA", false),     // Moving Average - disabled by default (can enable)
        new("EMA", "EMA", false),   // Exponential Moving Average  
        new("SMA", "SMA", false),   // Simple Moving Average
        new("BOLL", "BOLL", false), // Bollinger Bands
        new("SAR", "SAR", false),   // Parabolic SAR
    ];
    
    // Sub-pane indicators appear in separate panes below the main chart
    // VOL and MACD are added via layout, so they start enabled
    private readonly List<KLineIndicatorOption> _subPaneIndicators =
    [
        new("VOL", "VOL", true),   // Volume (enabled via layout)
        new("MACD", "MACD", true), // MACD (enabled via layout)
        new("RSI", "RSI", false),              // Relative Strength Index
        new("KDJ", "KDJ", false),              // KDJ Stochastic
        new("OBV", "OBV", false),              // On-Balance Volume
        new("ROC", "ROC", false)               // Rate of Change
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
            _chartModule = await JSRuntime.InvokeAsync<IJSObjectReference>("import", "./js/tradingview-chart.js");
            
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
        if (_chartModule is null || !_chartInitialized) return;
        
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
                volume = liveTick.Volume
            };

            // Use KLineChart tick update
            await _chartModule.InvokeVoidAsync("updateKLineChartWithTick", $"chart-container-{_chartId}", processedTick);
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
            var ohlcData = await OhlcRepository.GetCandlesAsync(Product.ItemId, Interval);
            
            if (ohlcData.Count == 0)
            {
                return;
            }

            // APPEND current price as the absolute latest tick
            if (Product.BuyOrderUnitPrice.HasValue)
            {
                var bucketedTime = DateTime.UtcNow.GetPeriodStart(Interval);
                var lastCandle = ohlcData.LastOrDefault();
                var price = Product.BuyOrderUnitPrice.Value;
                
                if (lastCandle != null && lastCandle.Time == bucketedTime)
                {
                    // Replace the last candle with updated values (records are immutable)
                    ohlcData[^1] = lastCandle with
                    {
                        High = Math.Max(lastCandle.High, price),
                        Low = Math.Min(lastCandle.Low, price),
                        Close = price
                    };
                }
                else
                {
                    // Add a new partial candle
                    ohlcData.Add(new OhlcDataPoint(
                        Time: bucketedTime,
                        Open: price,
                        High: price,
                        Low: price,
                        Close: price,
                        Volume: 0d,
                        Spread: 0d
                    ));
                }
            }

            // KLineChart format: timestamp in milliseconds
            var ohlcDataForKLine = ohlcData.Select(c => new
            {
                time = new DateTimeOffset(c.Time).ToUnixTimeMilliseconds(),
                open = c.Open,
                high = c.High,
                low = c.Low,
                close = c.Close,
                volume = c.Volume
            }).ToList();

            // Create the KLineChart (VOL and MACD are added via layout)
            await _chartModule.InvokeVoidAsync("createKLineChart", 
                $"chart-container-{_chartId}", 
                ohlcDataForKLine,
                new { productName = Product.ItemFriendlyName });
            
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
            foreach (var indicator in _overlayIndicators.Where(i => i.Enabled))
            {
                await _chartModule.InvokeVoidAsync("toggleKLineIndicator", 
                    $"chart-container-{_chartId}", 
                    indicator.Name, 
                    true, 
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

    // [LIGHTWEIGHT_CHARTS] Original LoadChartDataAsync - commented out for KLineChart test
    /*
    private async Task LoadChartDataAsync(bool fitContent = false)
    {
        if (_chartModule is null) return;

        try
        {
            var ohlcData = await OhlcRepository.GetCandlesAsync(Product.ItemId, Interval);
            
            if (ohlcData.Count == 0)
            {
                return;
            }

            // APPEND current price as the absolute latest tick BEFORE calculating indicators
            // This ensures indicators align with the live candle
            if (Product.BuyOrderUnitPrice.HasValue)
            {
                var bucketedTime = DateTime.UtcNow.GetPeriodStart(Interval);
                var lastCandle = ohlcData.LastOrDefault();
                var price = Product.BuyOrderUnitPrice.Value;
                
                if (lastCandle != null && lastCandle.Time == bucketedTime)
                {
                    // Replace the last candle with updated values (records are immutable)
                    ohlcData[^1] = lastCandle with
                    {
                        High = Math.Max(lastCandle.High, price),
                        Low = Math.Min(lastCandle.Low, price),
                        Close = price
                    };
                }
                else
                {
                    // Add a new partial candle using positional constructor
                    ohlcData.Add(new OhlcDataPoint(
                        Time: bucketedTime,
                        Open: price,
                        High: price,
                        Low: price,
                        Close: price,
                        Volume: 0d,
                        Spread: 0d
                    ));
                }
            }

            // Load indicators from the complete data (including live candle)
            if (_indicatorConfig.ShowSMA10 || _indicatorConfig.ShowSMA20 || _indicatorConfig.ShowSMA50 ||
                _indicatorConfig.ShowEMA12 || _indicatorConfig.ShowEMA26 || _indicatorConfig.ShowBollingerBands ||
                _indicatorConfig.ShowRSI || _indicatorConfig.ShowMACD || _indicatorConfig.ShowVWAP)
            {
                _indicators = TechnicalAnalysisService.CalculateIndicatorsFromCandles(ohlcData, _indicatorConfig);
            }
            else
            {
                _indicators = new List<TechnicalIndicator>();
            }

            // Load spread data if enabled
            if (_indicatorConfig.ShowSpread)
            {
                _spreadData = await TechnicalAnalysisService.CalculateSpreadAsync(Product.ItemId, Interval);
            }
            else
            {
                _spreadData = new List<IndicatorDataPoint>();
            }

            // Calculate support/resistance if enabled
            if (_indicatorConfig.ShowSupportResistance)
            {
                _supportResistanceLevels = TechnicalAnalysisService.CalculateSupportResistance(ohlcData);
            }
            else
            {
                _supportResistanceLevels = new List<SupportResistanceLevel>();
            }

            // Serialize indicators for JS (convert enum to string)
            var indicatorsForJs = _indicators.Select(i => new
            {
                name = i.Name,
                type = i.Type.ToString(),
                dataPoints = i.DataPoints.Select(dp => new { time = dp.Time, value = dp.Value }),
                color = i.Color,
                lineWidth = i.LineWidth
            }).ToArray();

            // Serialize support/resistance levels
            var srLevelsForJs = _supportResistanceLevels.Select(sr => new
            {
                price = sr.Price,
                type = sr.Type,
                strength = sr.Strength,
                touchCount = sr.TouchCount
            }).ToArray();

            // Include volume data in OHLC data for JS
            var ohlcDataWithVolume = ohlcData.Select(c => new
            {
                time = c.Time,
                open = c.Open,
                high = c.High,
                low = c.Low,
                close = c.Close,
                volume = c.Volume
            }).ToList();

            // Create or update chart with all data
            await _chartModule.InvokeVoidAsync("createChartWithIndicators", 
                $"chart-container-{_chartId}", 
                ohlcDataWithVolume, 
                indicatorsForJs,
                _spreadData.Select(dp => new { time = dp.Time, value = dp.Value }).ToArray(),
                srLevelsForJs,
                fitContent,
                _indicatorConfig.ShowVolume);
            
            _chartInitialized = true;
        }
        catch (JSException ex)
        {
            Console.WriteLine($"Error loading chart data: {ex.Message}");
        }
    }

    private async Task OnIndicatorConfigChanged(ChartIndicatorConfig config)
    {
        _indicatorConfig = config;
        if (_chartInitialized)
        {
            await LoadChartDataAsync();
        }
    }
    */

    public async ValueTask DisposeAsync()
    {
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
        }
    }
}
