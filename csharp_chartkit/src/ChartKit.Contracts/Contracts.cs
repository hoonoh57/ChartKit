using System.Diagnostics;

namespace ChartKit.CSharp.Contracts;

public readonly record struct Candle(
    DateTime OpenTime,
    DateTime CloseTime,
    float Open,
    float High,
    float Low,
    float Close,
    long Volume,
    bool IsFinal,
    long Sequence)
{
    public DateTime TradingDate => OpenTime.Date;
}

public readonly record struct Tick(
    string Symbol,
    DateTime Timestamp,
    float Price,
    long Quantity,
    long Sequence);

public enum CandleUnit
{
    Minute = 1,
    Tick = 2,
    Day = 3,
    Week = 4,
    Month = 5
}

public readonly record struct CandleTimeframe(CandleUnit Unit, int Value)
{
    public static CandleTimeframe Minute(int minutes) => new(CandleUnit.Minute, minutes);
    public static CandleTimeframe Tick(int ticks) => new(CandleUnit.Tick, ticks);
    public static CandleTimeframe Day => new(CandleUnit.Day, 1);
    public static CandleTimeframe Week => new(CandleUnit.Week, 1);
    public static CandleTimeframe Month => new(CandleUnit.Month, 1);

    public void Validate()
    {
        if (Value <= 0) throw new ArgumentOutOfRangeException(nameof(Value));
        if (Unit is CandleUnit.Day or CandleUnit.Week or CandleUnit.Month && Value != 1)
            throw new ArgumentOutOfRangeException(nameof(Value));
    }

    public override string ToString() => Unit switch
    {
        CandleUnit.Minute => $"{Value}m",
        CandleUnit.Tick => $"{Value}T",
        CandleUnit.Day => "D",
        CandleUnit.Week => "W",
        CandleUnit.Month => "M",
        _ => $"{Unit}:{Value}"
    };
}

public enum MarketEventKind
{
    Append = 1,
    Update = 2
}

public readonly record struct CandleEvent(
    string Symbol,
    MarketEventKind Kind,
    Candle Candle,
    long SourceSequence,
    long EnqueuedTimestamp)
{
    public static CandleEvent Create(
        string symbol,
        MarketEventKind kind,
        Candle candle,
        long sourceSequence = 0) =>
        new(symbol, kind, candle, sourceSequence, Stopwatch.GetTimestamp());
}

public enum SeriesKind
{
    Line = 0,
    Histogram = 1,
    Baseline = 2,
    Meta = 3
}

public sealed record IndicatorDescriptor(
    string Id,
    string DisplayName,
    int PanelIndex,
    string[] Keys,
    SeriesKind[] Kinds)
{
    public int ValueCount => Keys.Length;
}

public readonly record struct IndicatorPoint(
    long Sequence,
    float Value0,
    float Value1 = float.NaN,
    float Value2 = float.NaN,
    float Value3 = float.NaN,
    float Value4 = float.NaN)
{
    public float GetValue(int index) => index switch
    {
        0 => Value0,
        1 => Value1,
        2 => Value2,
        3 => Value3,
        4 => Value4,
        _ => throw new ArgumentOutOfRangeException(nameof(index))
    };
}

public sealed record IndicatorSeriesSnapshot(
    IndicatorDescriptor Descriptor,
    IndicatorPoint[] Points);

public sealed record SymbolSnapshot(
    string Symbol,
    Candle[] Candles,
    IndicatorSeriesSnapshot[] Indicators,
    long Version,
    DateTimeOffset PublishedAtUtc);

public readonly record struct EngineMetrics(
    long AcceptedEvents,
    long ProcessedEvents,
    long PublishedSnapshots,
    long ProcessingErrors,
    long MaxQueueDepth,
    long LastLatencyMicroseconds);

public sealed record HistoryRequest(
    string Symbol,
    CandleTimeframe Timeframe,
    int Count,
    DateTime? To = null);

public interface IMarketDataSource : IAsyncDisposable
{
    string Name { get; }

    Task<IReadOnlyList<Candle>> GetHistoryAsync(
        HistoryRequest request,
        CancellationToken cancellationToken);

    IAsyncEnumerable<CandleEvent> StreamAsync(
        IReadOnlyList<string> symbols,
        CandleTimeframe timeframe,
        CancellationToken cancellationToken);
}
