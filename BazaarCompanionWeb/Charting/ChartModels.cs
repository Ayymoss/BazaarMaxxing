namespace BazaarCompanionWeb.Charting;

// Series points in Lightweight Charts' native shapes. Time is a UNIX timestamp in SECONDS (UTCTimestamp).
// Serialized camelCase by both Blazor JS interop and the minimal-API JSON serializer.

public readonly record struct Candle(long Time, double Open, double High, double Low, double Close);

public readonly record struct LinePoint(long Time, double Value);

public readonly record struct HistPoint(long Time, double Value, string Color);

/// <summary>Full dataset for a window of candles, with every indicator pre-computed server-side.</summary>
public sealed class ChartPayload
{
    public List<Candle> Candles { get; init; } = [];
    public List<HistPoint> Volume { get; init; } = [];
    public List<LinePoint> Ma50 { get; init; } = [];
    public List<LinePoint> Ma250 { get; init; } = [];
    public List<LinePoint> BbUpper { get; init; } = [];
    public List<LinePoint> BbMiddle { get; init; } = [];
    public List<LinePoint> BbLower { get; init; } = [];
    public List<HistPoint> MacdHist { get; init; } = [];
    public List<LinePoint> MacdLine { get; init; } = [];
    public List<LinePoint> Signal { get; init; } = [];
    public List<LinePoint> Rsi { get; init; } = [];
    /// <summary>Ask price line (bid is the candle close). Empty when the source has no ask data (indices).</summary>
    public List<LinePoint> Ask { get; init; } = [];
}

/// <summary>The latest point of every series, pushed on each live tick via series.update().</summary>
public sealed class ChartTick
{
    public Candle Candle { get; init; }
    public HistPoint Volume { get; init; }
    public LinePoint? Ma50 { get; init; }
    public LinePoint? Ma250 { get; init; }
    public LinePoint? BbUpper { get; init; }
    public LinePoint? BbMiddle { get; init; }
    public LinePoint? BbLower { get; init; }
    public HistPoint? MacdHist { get; init; }
    public LinePoint? MacdLine { get; init; }
    public LinePoint? Signal { get; init; }
    public LinePoint? Rsi { get; init; }
    public LinePoint? Ask { get; init; }
}
