using ChartKit.CSharp.Contracts;

namespace ChartKit.CSharp.Engine;

public interface IIncrementalIndicator
{
    IndicatorDescriptor Descriptor { get; }
    IReadOnlyList<IndicatorPoint> Calculate(IReadOnlyList<Candle> candles);
    IndicatorPoint UpdateLast(IReadOnlyList<Candle> candles);
    void Reset();
}

public abstract class IncrementalIndicatorBase : IIncrementalIndicator
{
    private long _committedSequence = -1;

    public abstract IndicatorDescriptor Descriptor { get; }

    public IReadOnlyList<IndicatorPoint> Calculate(IReadOnlyList<Candle> candles)
    {
        ArgumentNullException.ThrowIfNull(candles);
        Reset();
        var results = new List<IndicatorPoint>(candles.Count);
        for (int index = 0; index < candles.Count; index++)
        {
            if (index == candles.Count - 1) SaveState();
            results.Add(StepCandle(candles[index], index));
        }
        if (candles.Count > 0) _committedSequence = candles[candles.Count - 1].Sequence;
        return results;
    }

    public IndicatorPoint UpdateLast(IReadOnlyList<Candle> candles)
    {
        ArgumentNullException.ThrowIfNull(candles);
        if (candles.Count == 0) throw new ArgumentException("At least one candle is required.", nameof(candles));

        int index = candles.Count - 1;
        long sequence = candles[index].Sequence;
        if (sequence == _committedSequence)
        {
            RestoreState();
        }
        else if (sequence == _committedSequence + 1)
        {
            SaveState();
        }
        else
        {
            IReadOnlyList<IndicatorPoint> rebuilt = Calculate(candles);
            return rebuilt[^1];
        }

        IndicatorPoint result = StepCandle(candles[index], index);
        _committedSequence = sequence;
        return result;
    }

    public void Reset()
    {
        _committedSequence = -1;
        ResetCore();
    }

    protected abstract IndicatorPoint StepCandle(Candle candle, int index);
    protected abstract void SaveState();
    protected abstract void RestoreState();
    protected abstract void ResetCore();
}

public static class DefaultIndicatorFactory
{
    public static IIncrementalIndicator[] Create() =>
    [
        new MaIndicator(),
        new JmaIndicator(),
        new RsiIndicator(),
        new MacdIndicator(),
        new ObvIndicator(),
        new SuperTrendIndicator(),
        new VwapIndicator(),
        new DisparityIndicator()
    ];
}
