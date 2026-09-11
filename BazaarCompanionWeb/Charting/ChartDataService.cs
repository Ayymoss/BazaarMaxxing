using BazaarCompanionWeb.Dtos;
using BazaarCompanionWeb.Entities;
using BazaarCompanionWeb.Interfaces.Database;
using BazaarCompanionWeb.Services;

namespace BazaarCompanionWeb.Charting;

/// <summary>
/// Builds <see cref="ChartPayload"/>s with all indicators computed server-side.
/// Every returned window is computed over (window + warmup) bars so indicator values are
/// correct at the left edge — the browser never recomputes; paging just prepends correct points.
/// </summary>
public sealed class ChartDataService(IOhlcRepository ohlcRepository, IndexAggregationService indexService)
{
    /// <summary>Extra older bars pulled purely to seed indicators (MA250 needs 250 priors).</summary>
    public const int WarmupBars = 250;

    /// <summary>Bars loaded on first paint — display window plus warmup so MA250 renders immediately.</summary>
    public const int InitialBars = 500;

    /// <summary>Bars fetched per backward page.</summary>
    public const int PageBars = 200;

    /// <summary>Compute all series over the supplied candles (chronological). No slicing.</summary>
    public static ChartPayload Build(IReadOnlyList<OhlcDataPoint> candles, bool includeAsk)
    {
        var (mh, ml, sig) = Indicators.Macd(candles);
        var (bu, bm, bl) = Indicators.Bollinger(candles);
        return new ChartPayload
        {
            Candles = candles.Select(c => new Candle(Indicators.Sec(c.Time), c.Open, c.High, c.Low, c.Close)).ToList(),
            Volume = Indicators.Volume(candles),
            Ma50 = Indicators.Sma(candles, 50),
            Ma250 = Indicators.Sma(candles, 250),
            BbUpper = bu,
            BbMiddle = bm,
            BbLower = bl,
            MacdHist = mh,
            MacdLine = ml,
            Signal = sig,
            Rsi = Indicators.Rsi(candles),
            AskCandles = includeAsk ? Indicators.AskCandles(candles) : [],
        };
    }

    /// <summary>The latest point of every series, for a live tick. Computed over a buffer with warmup.</summary>
    public static ChartTick LastTick(IReadOnlyList<OhlcDataPoint> buffer, bool includeAsk)
    {
        var p = Build(buffer, includeAsk);
        var last = buffer[^1];
        return new ChartTick
        {
            Candle = new Candle(Indicators.Sec(last.Time), last.Open, last.High, last.Low, last.Close),
            Volume = p.Volume[^1],
            Ma50 = Tail(p.Ma50),
            Ma250 = Tail(p.Ma250),
            BbUpper = Tail(p.BbUpper),
            BbMiddle = Tail(p.BbMiddle),
            BbLower = Tail(p.BbLower),
            MacdHist = p.MacdHist.Count > 0 ? p.MacdHist[^1] : null,
            MacdLine = Tail(p.MacdLine),
            Signal = Tail(p.Signal),
            Rsi = Tail(p.Rsi),
            AskCandle = includeAsk ? TailCandleAtTime(p.AskCandles, Indicators.Sec(last.Time)) : null,
        };
    }

    /// <summary>
    /// One page of product candles + indicators. <paramref name="beforeMs"/> null = most-recent page,
    /// otherwise the page immediately before that timestamp. Always computed over warmup bars so the
    /// returned page carries correct indicator values at its left edge.
    /// </summary>
    public async Task<ChartPayload> BuildProductPageAsync(
        string productKey, CandleInterval interval, long? beforeMs, int limit, CancellationToken ct)
    {
        var window = beforeMs is { } ms
            ? await ohlcRepository.GetCandlesBeforeAsync(productKey, interval, ToUtc(ms), limit, ct)
            : await ohlcRepository.GetCandlesAsync(productKey, interval, limit, ct);
        if (window.Count == 0) return new ChartPayload();

        var warmup = await ohlcRepository.GetCandlesBeforeAsync(productKey, interval, window[0].Time, WarmupBars, ct);
        return Slice(Build(Concat(warmup, window), includeAsk: true), Indicators.Sec(window[0].Time));
    }

    /// <summary>One page of index candles + indicators (no ask data).</summary>
    public async Task<ChartPayload> BuildIndexPageAsync(
        string slug, CandleInterval interval, long? beforeMs, int limit, CancellationToken ct)
    {
        var window = beforeMs is { } ms
            ? await indexService.GetAggregatedCandlesBeforeAsync(slug, interval, ToUtc(ms), limit, ct)
            : await indexService.GetAggregatedCandlesAsync(slug, interval, limit, ct);
        if (window.Count == 0) return new ChartPayload();

        var warmup = await indexService.GetAggregatedCandlesBeforeAsync(slug, interval, window[0].Time, WarmupBars, ct);
        return Slice(Build(Concat(warmup, window), includeAsk: false), Indicators.Sec(window[0].Time));
    }

    private static DateTime ToUtc(long unixMs) => DateTimeOffset.FromUnixTimeMilliseconds(unixMs).UtcDateTime;

    private static List<OhlcDataPoint> Concat(List<OhlcDataPoint> warmup, List<OhlcDataPoint> window)
    {
        var all = new List<OhlcDataPoint>(warmup.Count + window.Count);
        all.AddRange(warmup);
        all.AddRange(window);
        return all;
    }

    private static LinePoint? Tail(IReadOnlyList<LinePoint> l) => l.Count > 0 ? l[^1] : null;

    private static Candle? TailCandleAtTime(IReadOnlyList<Candle> l, long time) =>
        l.Count > 0 && l[^1].Time == time ? l[^1] : null;

    /// <summary>Drop everything older than the window start, leaving only the requested page.</summary>
    private static ChartPayload Slice(ChartPayload p, long fromSec) => new()
    {
        Candles = p.Candles.Where(x => x.Time >= fromSec).ToList(),
        Volume = p.Volume.Where(x => x.Time >= fromSec).ToList(),
        Ma50 = p.Ma50.Where(x => x.Time >= fromSec).ToList(),
        Ma250 = p.Ma250.Where(x => x.Time >= fromSec).ToList(),
        BbUpper = p.BbUpper.Where(x => x.Time >= fromSec).ToList(),
        BbMiddle = p.BbMiddle.Where(x => x.Time >= fromSec).ToList(),
        BbLower = p.BbLower.Where(x => x.Time >= fromSec).ToList(),
        MacdHist = p.MacdHist.Where(x => x.Time >= fromSec).ToList(),
        MacdLine = p.MacdLine.Where(x => x.Time >= fromSec).ToList(),
        Signal = p.Signal.Where(x => x.Time >= fromSec).ToList(),
        Rsi = p.Rsi.Where(x => x.Time >= fromSec).ToList(),
        AskCandles = p.AskCandles.Where(x => x.Time >= fromSec).ToList(),
    };
}
