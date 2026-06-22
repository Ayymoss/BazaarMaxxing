using BazaarCompanionWeb.Charting;
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
    private const string IndicatorStorageKey = "lwc_indicators";

    [Parameter] public required ProductDataInfo Product { get; set; }
    [Parameter] public CandleInterval Interval { get; set; } = CandleInterval.OneHour;
    [Parameter] public EventCallback<CandleInterval> IntervalChanged { get; set; }

    [Inject] private IJSRuntime JSRuntime { get; set; } = null!;
    [Inject] private IOhlcRepository OhlcRepository { get; set; } = null!;
    [Inject] private BrowserStorage BrowserStorage { get; set; } = null!;

    private IJSObjectReference? _chartModule;
    private readonly string _chartId = Guid.NewGuid().ToString("N")[..8];
    private string ContainerId => $"chart-container-{_chartId}";

    private bool _chartInitialized;
    private bool _indicatorsLoaded;
    private bool _disposed;
    private readonly CancellationTokenSource _disposalCts = new();

    // In-memory candle buffer (window + warmup) kept so live ticks can recompute indicator tail values.
    private List<OhlcDataPoint> _candles = [];

    /// <summary>Indicator definitions for the toggle UI. Overlay = drawn on the price pane.</summary>
    private readonly record struct IndicatorDef(string Key, string Label, bool Overlay);

    private static readonly IndicatorDef[] Defs =
    [
        new("ASK", "ASK", true),
        new("MA", "MA 50/250", true),
        new("BB", "BB", true),
        new("VOL", "VOL", false),
        new("MACD", "MACD", false),
        new("RSI", "RSI", false),
    ];

    private readonly Dictionary<string, bool> _enabled = new()
    {
        ["ASK"] = true, ["MA"] = false, ["BB"] = false, ["VOL"] = true, ["MACD"] = true, ["RSI"] = false,
    };

    private sealed record IndicatorState(Dictionary<string, bool> Indicators);

    private object Flags() => new
    {
        ask = _enabled["ASK"],
        ma = _enabled["MA"],
        bb = _enabled["BB"],
        vol = _enabled["VOL"],
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
        try
        {
            _chartModule = await JSRuntime.InvokeAsync<IJSObjectReference>("import", "./js/chartInit.js");
            await LoadIndicatorStateAsync();
            await CreateChartAsync();
        }
        catch (JSException ex)
        {
            Console.WriteLine($"Error initializing chart: {ex.Message}");
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
        // Product can be null while the page is still resolving it (or when the key isn't found).
        if (_chartModule is null || Product is null) return;
        try
        {
            var candles = await OhlcRepository.GetCandlesAsync(Product.ItemId, Interval, ChartDataService.InitialBars);
            if (candles.Count == 0)
            {
                _candles = [];
                return;
            }

            // Fold the current live price into the latest (forming) candle.
            MergeLivePrice(candles, Product.BidUnitPrice, Product.AskUnitPrice);
            _candles = candles;

            var payload = ChartDataService.Build(candles, includeAsk: true);
            await _chartModule.InvokeVoidAsync("createOhlcChart", ContainerId, payload, new
            {
                productKey = Product.ItemId,
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
            Console.WriteLine($"Error creating chart: {ex.Message}");
        }
    }

    private void MergeLivePrice(List<OhlcDataPoint> candles, double bid, double ask)
    {
        var bucket = DateTime.UtcNow.GetPeriodStart(Interval);
        var last = candles[^1];
        if (last.Time == bucket)
            candles[^1] = last with
            {
                High = Math.Max(last.High, bid),
                Low = Math.Min(last.Low, bid),
                Close = bid,
                AskClose = ask,
            };
        else
            candles.Add(new OhlcDataPoint(bucket, bid, bid, bid, bid, 0d, 0d, ask));
    }

    public async Task UpdateTickAsync(object tick)
    {
        if (_disposed || _chartModule is null || !_chartInitialized || _candles.Count == 0) return;
        try
        {
            var json = tick.ToString();
            if (string.IsNullOrEmpty(json)) return;

            var liveTick = JsonSerializer.Deserialize<LiveTick>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (liveTick is null) return;

            var bucket = liveTick.Time.GetPeriodStart(Interval);
            var last = _candles[^1];
            if (last.Time == bucket)
                _candles[^1] = last with
                {
                    High = Math.Max(last.High, liveTick.High),
                    Low = Math.Min(last.Low, liveTick.Low),
                    Close = liveTick.Close,
                    Volume = liveTick.Volume,
                    AskClose = liveTick.AskClose,
                };
            else if (bucket > last.Time)
                _candles.Add(new OhlcDataPoint(bucket, liveTick.Open, liveTick.High, liveTick.Low, liveTick.Close, liveTick.Volume, 0d, liveTick.AskClose));
            else
                return; // stale tick older than current bar

            TrimBuffer();

            var update = ChartDataService.LastTick(_candles, includeAsk: true);
            await _chartModule.InvokeVoidAsync("updateOhlcTick", _disposalCts.Token, ContainerId, update);
        }
        catch (TaskCanceledException) { }
        catch (JSDisconnectedException) { }
        catch (Exception ex)
        {
            Console.WriteLine($"Error updating tick: {ex.Message}");
        }
    }

    // Keep the buffer bounded during long live sessions while retaining warmup history.
    private void TrimBuffer()
    {
        const int cap = 2000;
        if (_candles.Count > cap) _candles.RemoveRange(0, _candles.Count - 1200);
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
