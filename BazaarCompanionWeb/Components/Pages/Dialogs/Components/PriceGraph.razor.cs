using BazaarCompanionWeb.Dtos;
using BazaarCompanionWeb.Entities;
using BazaarCompanionWeb.Interfaces.Database;
using BazaarCompanionWeb.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace BazaarCompanionWeb.Components.Pages.Dialogs.Components;

public partial class PriceGraph : ComponentBase, IAsyncDisposable
{
    [Parameter] public required ProductDataInfo Product { get; set; }
    [Parameter] public CandleInterval Interval { get; set; } = CandleInterval.FifteenMinute;
    [Parameter] public EventCallback<CandleInterval> IntervalChanged { get; set; }

    [Inject] private IJSRuntime JSRuntime { get; set; } = null!;
    [Inject] private IOhlcRepository OhlcRepository { get; set; } = null!;
    [Inject] private TechnicalAnalysisService TechnicalAnalysisService { get; set; } = null!;

    private IJSObjectReference? _chartModule;
    private readonly string _chartId = Guid.NewGuid().ToString("N")[..8];
    private bool _chartInitialized;
    private ChartIndicatorConfig _indicatorConfig = new();
    private List<TechnicalIndicator> _indicators = new();
    private List<IndicatorDataPoint> _spreadData = new();
    private List<SupportResistanceLevel> _supportResistanceLevels = new();

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            await InitializeChartAsync();
        }
        else if (_chartInitialized)
        {
            await UpdateChartAsync();
        }
    }

    private async Task OnIntervalChangedAsync(CandleInterval newInterval)
    {
        Interval = newInterval;
        await IntervalChanged.InvokeAsync(Interval);
        if (_chartInitialized)
        {
            await UpdateChartAsync(true);
        }
    }

    private async Task InitializeChartAsync()
    {
        try
        {
            _chartModule = await JSRuntime.InvokeAsync<IJSObjectReference>("import", "./js/tradingview-chart.js");
            await LoadChartDataAsync();
        }
        catch (JSException ex)
        {
            Console.WriteLine($"Error initializing chart: {ex.Message}");
        }
    }

    private async Task UpdateChartAsync(bool fitContent = false)
    {
        if (_chartModule is null) return;
        await LoadChartDataAsync(fitContent);
    }

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

            // Load indicators if any are enabled
            if (_indicatorConfig.ShowSMA10 || _indicatorConfig.ShowSMA20 || _indicatorConfig.ShowSMA50 ||
                _indicatorConfig.ShowEMA12 || _indicatorConfig.ShowEMA26 || _indicatorConfig.ShowBollingerBands ||
                _indicatorConfig.ShowRSI || _indicatorConfig.ShowMACD || _indicatorConfig.ShowVWAP)
            {
                _indicators = await TechnicalAnalysisService.CalculateIndicatorsAsync(
                    Product.ItemId, Interval, _indicatorConfig);
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

            // Include volume data in OHLC data
            var ohlcDataWithVolume = ohlcData.Select(c => new
            {
                time = c.Time,
                open = c.Open,
                high = c.High,
                low = c.Low,
                close = c.Close,
                volume = c.Volume
            }).ToArray();

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

    public async ValueTask DisposeAsync()
    {
        if (_chartModule is not null)
        {
            try
            {
                await _chartModule.InvokeVoidAsync("disposeChart", $"chart-container-{_chartId}");
                await _chartModule.DisposeAsync();
            }
            catch (JSDisconnectedException)
            {
                // Ignore if JS is disconnected
            }
        }
    }
}
