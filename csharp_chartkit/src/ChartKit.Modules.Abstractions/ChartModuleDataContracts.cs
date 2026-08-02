namespace ChartKit.CSharp.Modules.Abstractions;

public readonly record struct ChartPrimaryBar(
    long Sequence,
    DateOnly TradingDate,
    double Open,
    double High,
    double Low,
    double Close,
    long Volume,
    bool IsFinal)
{
    public ChartPrimaryBar(
        long sequence,
        double open,
        double high,
        double low,
        double close,
        long volume,
        bool isFinal)
        : this(
            sequence,
            DateOnly.MinValue,
            open,
            high,
            low,
            close,
            volume,
            isFinal)
    {
    }

    public bool HasTradingDate => TradingDate != DateOnly.MinValue;

    public void Validate()
    {
        if (!double.IsFinite(Open) ||
            !double.IsFinite(High) ||
            !double.IsFinite(Low) ||
            !double.IsFinite(Close))
        {
            throw new InvalidOperationException(
                "Primary bar OHLC values must be finite.");
        }
    }
}

public sealed class ChartPrimarySeriesSnapshot
{
    public ChartPrimarySeriesSnapshot(
        long dataVersion,
        IReadOnlyList<ChartPrimaryBar> bars)
    {
        ArgumentNullException.ThrowIfNull(bars);
        DataVersion = dataVersion;
        var copy = new ChartPrimaryBar[bars.Count];
        for (int index = 0; index < copy.Length; index++)
        {
            ChartPrimaryBar bar = bars[index];
            bar.Validate();
            copy[index] = bar;
        }
        Bars = copy;
    }

    public static ChartPrimarySeriesSnapshot Empty { get; } =
        new(0, Array.Empty<ChartPrimaryBar>());

    public long DataVersion { get; }
    public IReadOnlyList<ChartPrimaryBar> Bars { get; }
}

public interface IChartDataModule : IChartComputationModule
{
    void ApplyPrimarySeries(ChartPrimarySeriesSnapshot snapshot);
}
