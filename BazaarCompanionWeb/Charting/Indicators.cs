using BazaarCompanionWeb.Dtos;

namespace BazaarCompanionWeb.Charting;

/// <summary>
/// Pure C# technical-indicator math. Lightweight Charts ships none of these (KLineCharts did),
/// so every series is computed here and fed to the chart as plain line/histogram data.
/// </summary>
public static class Indicators
{
    public const string UpColor = "rgba(16, 185, 129, 0.5)";   // emerald
    public const string DownColor = "rgba(239, 68, 68, 0.5)";  // red

    internal static long Sec(DateTime t) =>
        new DateTimeOffset(DateTime.SpecifyKind(t, DateTimeKind.Utc)).ToUnixTimeSeconds();

    /// <summary>Simple moving average. Emits a point only once <paramref name="period"/> closes exist.</summary>
    public static List<LinePoint> Sma(IReadOnlyList<OhlcDataPoint> c, int period)
    {
        var r = new List<LinePoint>(c.Count);
        double sum = 0;
        for (var i = 0; i < c.Count; i++)
        {
            sum += c[i].Close;
            if (i >= period) sum -= c[i - period].Close;
            if (i >= period - 1) r.Add(new LinePoint(Sec(c[i].Time), sum / period));
        }
        return r;
    }

    /// <summary>EMA aligned to the bar index; null before the SMA seed at index period-1.</summary>
    private static double?[] EmaArr(IReadOnlyList<OhlcDataPoint> c, int period)
    {
        var e = new double?[c.Count];
        var k = 2.0 / (period + 1);
        double seed = 0;
        for (var i = 0; i < c.Count; i++)
        {
            if (i < period - 1) { seed += c[i].Close; continue; }
            if (i == period - 1) { seed += c[i].Close; e[i] = seed / period; continue; }
            e[i] = (c[i].Close - e[i - 1]!.Value) * k + e[i - 1]!.Value;
        }
        return e;
    }

    /// <summary>EMA over a sparse source array (used for the MACD signal line). Skips leading nulls.</summary>
    private static double?[] EmaArr(double?[] src, int period)
    {
        var e = new double?[src.Length];
        var k = 2.0 / (period + 1);
        double seed = 0;
        var seen = 0;
        for (var i = 0; i < src.Length; i++)
        {
            if (src[i] is not { } v) continue;
            seen++;
            if (seen < period) { seed += v; continue; }
            if (seen == period) { seed += v; e[i] = seed / period; continue; }
            var prev = e[i - 1] ?? PrevNonNull(e, i);
            e[i] = (v - prev) * k + prev;
        }
        return e;

        static double PrevNonNull(double?[] a, int i)
        {
            for (var j = i - 1; j >= 0; j--) if (a[j] is { } d) return d;
            return 0;
        }
    }

    /// <summary>MACD(12,26,9): coloured histogram, MACD line, signal line.</summary>
    public static (List<HistPoint> hist, List<LinePoint> macd, List<LinePoint> signal) Macd(
        IReadOnlyList<OhlcDataPoint> c, int fast = 12, int slow = 26, int sig = 9)
    {
        var ef = EmaArr(c, fast);
        var es = EmaArr(c, slow);
        var macd = new double?[c.Count];
        for (var i = 0; i < c.Count; i++)
            if (ef[i] is { } a && es[i] is { } b) macd[i] = a - b;

        var signal = EmaArr(macd, sig);

        var macdLine = new List<LinePoint>();
        var signalLine = new List<LinePoint>();
        var hist = new List<HistPoint>();
        for (var i = 0; i < c.Count; i++)
        {
            var t = Sec(c[i].Time);
            if (macd[i] is { } m) macdLine.Add(new LinePoint(t, m));
            if (signal[i] is { } s) signalLine.Add(new LinePoint(t, s));
            if (macd[i] is { } mm && signal[i] is { } ss)
            {
                var h = mm - ss;
                hist.Add(new HistPoint(t, h, h >= 0 ? UpColor : DownColor));
            }
        }
        return (hist, macdLine, signalLine);
    }

    /// <summary>Wilder's RSI.</summary>
    public static List<LinePoint> Rsi(IReadOnlyList<OhlcDataPoint> c, int period = 14)
    {
        var r = new List<LinePoint>();
        if (c.Count <= period) return r;

        double gain = 0, loss = 0;
        for (var i = 1; i <= period; i++)
        {
            var ch = c[i].Close - c[i - 1].Close;
            if (ch >= 0) gain += ch; else loss -= ch;
        }
        gain /= period; loss /= period;
        r.Add(new LinePoint(Sec(c[period].Time), Rs(gain, loss)));

        for (var i = period + 1; i < c.Count; i++)
        {
            var ch = c[i].Close - c[i - 1].Close;
            double g = ch > 0 ? ch : 0, l = ch < 0 ? -ch : 0;
            gain = (gain * (period - 1) + g) / period;
            loss = (loss * (period - 1) + l) / period;
            r.Add(new LinePoint(Sec(c[i].Time), Rs(gain, loss)));
        }
        return r;

        static double Rs(double g, double l) => l == 0 ? 100 : 100 - 100 / (1 + g / l);
    }

    /// <summary>Bollinger Bands (period, k·σ). Population standard deviation over the window.</summary>
    public static (List<LinePoint> upper, List<LinePoint> middle, List<LinePoint> lower) Bollinger(
        IReadOnlyList<OhlcDataPoint> c, int period = 20, double k = 2.0)
    {
        var u = new List<LinePoint>();
        var m = new List<LinePoint>();
        var lo = new List<LinePoint>();
        for (var i = period - 1; i < c.Count; i++)
        {
            double sum = 0;
            for (var j = i - period + 1; j <= i; j++) sum += c[j].Close;
            var mean = sum / period;
            double sq = 0;
            for (var j = i - period + 1; j <= i; j++) { var d = c[j].Close - mean; sq += d * d; }
            var sd = Math.Sqrt(sq / period);
            var t = Sec(c[i].Time);
            u.Add(new LinePoint(t, mean + k * sd));
            m.Add(new LinePoint(t, mean));
            lo.Add(new LinePoint(t, mean - k * sd));
        }
        return (u, m, lo);
    }

    /// <summary>Volume histogram coloured by candle direction.</summary>
    public static List<HistPoint> Volume(IReadOnlyList<OhlcDataPoint> c)
    {
        var r = new List<HistPoint>(c.Count);
        foreach (var b in c)
            r.Add(new HistPoint(Sec(b.Time), b.Volume, b.Close >= b.Open ? UpColor : DownColor));
        return r;
    }

    /// <summary>Ask price as a line; gaps (null) where ask is missing/zero.</summary>
    public static List<LinePoint> AskLine(IReadOnlyList<OhlcDataPoint> c)
    {
        var r = new List<LinePoint>();
        foreach (var b in c)
            if (b.AskClose > 0) r.Add(new LinePoint(Sec(b.Time), b.AskClose));
        return r;
    }
}
