using BazaarCompanionWeb.Charting;
using BazaarCompanionWeb.Dtos;
using BazaarCompanionWeb.Entities;
using BazaarCompanionWeb.Interfaces.Database;
using BazaarCompanionWeb.Services.Ingestion;
using BazaarCompanionWeb.Utilities;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace BazaarCompanionWeb.Components.Pages.Dialogs.Components;

public partial class PriceGraph : ComponentBase, IAsyncDisposable
{
    private const string IndicatorStorageKey = "lwc_indicators";

    [Parameter] public required ProductDataInfo Product { get; set; }
    [Parameter] public CandleInterval Interval { get; set; } = CandleInterval.OneHour;
    [Parameter] public EventCallback<CandleInterval> IntervalChanged { get; set; }

    [Inject] private IJSRuntime JSRuntime { get; set; } = null!;
    [Inject] private IOhlcRepository OhlcRepository { get; set; } = null!;
    [Inject] private BazaarSnapshotStore SnapshotStore { get; set; } = null!;
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

    // The minute whose traded units were last folded into the forming bar, and how much it contributed.
    // A live tick is the forming MINUTE bar; two pushes for the same minute must replace, not re-add.
    private DateTime _liveMinute = DateTime.MinValue;
    private double _liveMinuteBuy, _liveMinuteSell;

    /// <summary>Indicator definitions for the toggle UI. Overlay = drawn on the price pane.</summary>
    private readonly record struct IndicatorDef(string Key, string Label, bool Overlay);

    private static readonly IndicatorDef[] Defs =
    [
        new("ASK", "ASK", true),
        new("MA", "MA 50/250", true),
        new("BB", "BB", true),
        new("VOL", "VOL", true),
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

            // The DB is minutes behind (10-min flush, 5-min aggregation); rebuild the forming candle from RAM.
            SeedFormingBar(candles);
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

    /// <summary>
    /// Bring the forming candle up to the last poll. When the RAM ring covers the whole bucket, the bar is
    /// rebuilt from the one-minute samples (open/high/low/close per side, units traded) and replaces whatever
    /// the DB had — the DB copy is at best a few minutes old. Otherwise (the app started mid-bucket, or the
    /// ring wrapped) the current price is folded into the DB candle and its volume kept.
    /// </summary>
    private void SeedFormingBar(List<OhlcDataPoint> candles)
    {
        var bid = Product.BidUnitPrice;
        var ask = Product.AskUnitPrice;
        var bucket = DateTime.UtcNow.GetPeriodStart(Interval);
        var last = candles[^1];
        var samples = SnapshotStore.GetTicksSnapshot(Product.ItemId);

        var ringCovers = SnapshotStore.FirstIngestUtc <= bucket
                         && (samples.Count < BazaarSnapshotStore.TickRingCapacity
                             || (samples.Count > 0 && samples[0].Timestamp <= bucket));

        if (ringCovers)
        {
            var inBucket = samples.Where(t => t.Timestamp >= bucket).ToList();
            var before = samples.LastOrDefault(t => t.Timestamp < bucket);
            // Open at the price the product had when the bucket started: the last sample before it, else the
            // DB's own open for this bucket, else the previous candle's close.
            var openBid = before?.BidPrice ?? (last.Time == bucket ? last.Open : last.Close);
            var openAsk = before?.AskPrice ?? (last.Time == bucket && last.AskOpen > 0 ? last.AskOpen : last.AskClose);
            if (openAsk <= 0) openAsk = ask;

            var bar = new OhlcDataPoint(bucket, openBid, openBid, openBid, openBid, 0, 0, openAsk, 0, 0, openAsk, openAsk, openAsk);
            foreach (var t in inBucket)
            {
                bar = bar with
                {
                    High = Math.Max(bar.High, t.BidPrice),
                    Low = Math.Min(bar.Low, t.BidPrice),
                    Close = t.BidPrice,
                    AskHigh = Math.Max(bar.AskHigh, t.AskPrice),
                    AskLow = Math.Min(bar.AskLow, t.AskPrice),
                    AskClose = t.AskPrice,
                    BuyVolume = bar.BuyVolume + t.TradedBuy,
                    SellVolume = bar.SellVolume + t.TradedSell,
                };
            }
            bar = bar with { Volume = bar.BuyVolume + bar.SellVolume };

            if (last.Time == bucket) candles[^1] = bar;
            else candles.Add(bar);

            // The newest sample in the bucket is this minute's contribution; a live tick for the same minute
            // (a poll racing the page load) must replace it rather than add to it.
            if (inBucket.Count > 0)
            {
                var newest = inBucket[^1];
                _liveMinute = new DateTime(newest.Timestamp.Year, newest.Timestamp.Month, newest.Timestamp.Day,
                    newest.Timestamp.Hour, newest.Timestamp.Minute, 0, DateTimeKind.Utc);
                _liveMinuteBuy = newest.TradedBuy;
                _liveMinuteSell = newest.TradedSell;
            }
            return;
        }

        if (last.Time == bucket)
            candles[^1] = last with
            {
                High = Math.Max(last.High, bid),
                Low = Math.Min(last.Low, bid),
                Close = bid,
                AskOpen = last.AskOpen > 0 ? last.AskOpen : ask,
                AskHigh = Math.Max(last.AskHigh, ask),
                AskLow = last.AskLow > 0 ? Math.Min(last.AskLow, ask) : ask,
                AskClose = ask,
            };
        else
            candles.Add(new OhlcDataPoint(bucket, bid, bid, bid, bid, 0d, 0d, ask, 0, 0, ask, ask, ask));
    }

    public async Task UpdateTickAsync(LiveTick liveTick)
    {
        if (_disposed || _chartModule is null || !_chartInitialized || _candles.Count == 0) return;
        try
        {
            var bucket = liveTick.Time.GetPeriodStart(Interval);
            var last = _candles[^1];
            if (last.Time == bucket)
            {
                // Add this minute's traded units once: back out what the same minute contributed before.
                var buy = last.BuyVolume + liveTick.BuyVolume;
                var sell = last.SellVolume + liveTick.SellVolume;
                if (_liveMinute == liveTick.Time)
                {
                    buy -= _liveMinuteBuy;
                    sell -= _liveMinuteSell;
                }

                _candles[^1] = last with
                {
                    High = Math.Max(last.High, liveTick.High),
                    Low = Math.Min(last.Low, liveTick.Low),
                    Close = liveTick.Close,
                    Volume = buy + sell,
                    BuyVolume = buy,
                    SellVolume = sell,
                    AskOpen = last.AskOpen > 0 ? last.AskOpen : liveTick.AskOpen,
                    AskHigh = Math.Max(last.AskHigh, liveTick.AskHigh),
                    AskLow = last.AskLow > 0 ? Math.Min(last.AskLow, liveTick.AskLow) : liveTick.AskLow,
                    AskClose = liveTick.AskClose,
                };
            }
            else if (bucket > last.Time)
                _candles.Add(new OhlcDataPoint(bucket, liveTick.Open, liveTick.High, liveTick.Low, liveTick.Close,
                    liveTick.Volume, 0d, liveTick.AskClose, liveTick.BuyVolume, liveTick.SellVolume,
                    liveTick.AskOpen, liveTick.AskHigh, liveTick.AskLow));
            else
                return; // stale tick older than current bar

            _liveMinute = liveTick.Time;
            _liveMinuteBuy = liveTick.BuyVolume;
            _liveMinuteSell = liveTick.SellVolume;

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
