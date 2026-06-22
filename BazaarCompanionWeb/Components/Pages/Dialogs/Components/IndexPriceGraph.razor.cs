using BazaarCompanionWeb.Charting;
using BazaarCompanionWeb.Entities;
using BazaarCompanionWeb.Services;
using BazaarCompanionWeb.Utilities;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace BazaarCompanionWeb.Components.Pages.Dialogs.Components;

public partial class IndexPriceGraph : ComponentBase, IAsyncDisposable
{
    private const string IndicatorStorageKey = "lwc_index_indicators";

    [Parameter] public required string IndexSlug { get; set; }
    [Parameter] public required string IndexName { get; set; }
    [Parameter] public CandleInterval Interval { get; set; } = CandleInterval.OneHour;
    [Parameter] public EventCallback<CandleInterval> IntervalChanged { get; set; }

    [Inject] private IJSRuntime JSRuntime { get; set; } = null!;
    [Inject] private IndexAggregationService IndexAggregationService { get; set; } = null!;
    [Inject] private BrowserStorage BrowserStorage { get; set; } = null!;

    private IJSObjectReference? _chartModule;
    private readonly string _chartId = Guid.NewGuid().ToString("N")[..8];
    private string ContainerId => $"chart-container-{_chartId}";

    private bool _chartInitialized;
    private bool _indicatorsLoaded;
    private bool _disposed;
    private readonly CancellationTokenSource _disposalCts = new();

    // Indices have no ask line and no meaningful volume.
    private readonly record struct IndicatorDef(string Key, string Label);

    private static readonly IndicatorDef[] Defs =
    [
        new("MA", "MA 50/250"),
        new("BB", "BB"),
        new("MACD", "MACD"),
        new("RSI", "RSI"),
    ];

    private readonly Dictionary<string, bool> _enabled = new()
    {
        ["MA"] = false, ["BB"] = false, ["MACD"] = true, ["RSI"] = false,
    };

    private sealed record IndicatorState(Dictionary<string, bool> Indicators);

    private object Flags() => new
    {
        ask = false,
        ma = _enabled["MA"],
        bb = _enabled["BB"],
        vol = false,
        macd = _enabled["MACD"],
        rsi = _enabled["RSI"],
    };

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender) await InitializeChartAsync();
    }

    private async Task OnIntervalChangedAsync(CandleInterval newInterval)
    {
        Interval = newInterval;
        await IntervalChanged.InvokeAsync(Interval);
        if (_chartInitialized) await CreateChartAsync();
    }

    private async Task InitializeChartAsync()
    {
        if (_disposed) return;
        try
        {
            _chartModule = await JSRuntime.InvokeAsync<IJSObjectReference>("import", _disposalCts.Token, "./js/chartInit.js");
            await LoadIndicatorStateAsync();
            await CreateChartAsync();
        }
        catch (JSException ex)
        {
            Console.WriteLine($"Error initializing index chart: {ex.Message}");
        }
    }

    private async Task LoadIndicatorStateAsync()
    {
        if (_indicatorsLoaded) return;
        try
        {
            var state = await BrowserStorage.GetAsync<IndicatorState>(IndicatorStorageKey);
            if (state?.Indicators is not null)
                foreach (var (key, enabled) in state.Indicators)
                    if (_enabled.ContainsKey(key))
                        _enabled[key] = enabled;
            _indicatorsLoaded = true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error loading indicator state: {ex.Message}");
        }
    }

    private async Task SaveIndicatorStateAsync()
    {
        try
        {
            await BrowserStorage.SetAsync(IndicatorStorageKey, new IndicatorState(new Dictionary<string, bool>(_enabled)));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error saving indicator state: {ex.Message}");
        }
    }

    private async Task CreateChartAsync()
    {
        if (_chartModule is null || string.IsNullOrEmpty(IndexSlug)) return;
        try
        {
            var candles = await IndexAggregationService.GetAggregatedCandlesAsync(IndexSlug, Interval, ChartDataService.InitialBars, _disposalCts.Token);
            if (candles.Count == 0) return;

            var payload = ChartDataService.Build(candles, includeAsk: false);
            await _chartModule.InvokeVoidAsync("createOhlcChart", ContainerId, payload, new
            {
                productKey = $"index:{IndexSlug}",
                interval = (int)Interval,
                flags = Flags(),
            });

            if (!_chartInitialized)
            {
                _chartInitialized = true;
                StateHasChanged();
            }
        }
        catch (Exception ex)
        {
            // Never let a chart failure tear down the Blazor circuit.
            Console.WriteLine($"Error creating index chart: {ex.Message}");
        }
    }

    private async Task ToggleIndicatorAsync(string key, bool enabled)
    {
        if (_chartModule is null || !_chartInitialized) return;
        _enabled[key] = enabled;
        try
        {
            await _chartModule.InvokeVoidAsync("applyOhlcConfig", ContainerId, Flags());
            await SaveIndicatorStateAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error toggling indicator {key}: {ex.Message}");
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
                await _chartModule.InvokeVoidAsync("disposeOhlcChart", ContainerId);
                await _chartModule.DisposeAsync();
            }
            catch (JSDisconnectedException) { }
            catch (TaskCanceledException) { }
        }
    }
}
