using System.Diagnostics;
using ChartKit.CSharp.Contracts;

namespace ChartKit.CSharp.Engine;

internal sealed class SymbolRuntime
{
    private sealed class IndicatorRuntime
    {
        public IndicatorRuntime(IIncrementalIndicator indicator, int capacity)
        {
            Indicator = indicator;
            Points = new IndicatorPointRingBuffer(capacity);
        }

        public IIncrementalIndicator Indicator { get; }
        public IndicatorPointRingBuffer Points { get; }
    }

    private readonly CandleRingBuffer _candles;
    private readonly IndicatorRuntime[] _indicators;
    private readonly int _snapshotBars;
    private readonly long _snapshotIntervalTicks;
    private long _lastSnapshotTimestamp;
    private long _version;

    public SymbolRuntime(string symbol, int candleCapacity, int snapshotBars, TimeSpan snapshotInterval)
    {
        Symbol = symbol;
        _candles = new CandleRingBuffer(candleCapacity);
        _indicators = DefaultIndicatorFactory.Create()
            .Select(indicator => new IndicatorRuntime(indicator, candleCapacity))
            .ToArray();
        _snapshotBars = snapshotBars;
        _snapshotIntervalTicks = Math.Max(1L,
            (long)(Stopwatch.Frequency * snapshotInterval.TotalSeconds));
    }

    public string Symbol { get; }

    public void LoadHistory(IReadOnlyList<Candle> candles)
    {
        _candles.Clear();
        int start = Math.Max(0, candles.Count - _candles.Capacity);
        for (int index = start; index < candles.Count; index++)
            _candles.Add(candles[index]);

        foreach (IndicatorRuntime runtime in _indicators)
        {
            runtime.Points.Clear();
            runtime.Indicator.Reset();
            IReadOnlyList<IndicatorPoint> points = runtime.Indicator.Calculate(_candles);
            foreach (IndicatorPoint point in points)
                runtime.Points.AddOrReplace(point);
        }
        _version++;
    }

    public void Apply(CandleEvent value)
    {
        if (_candles.Count > 0 &&
            (value.Kind == MarketEventKind.Update ||
             _candles.LastSequence == value.Candle.Sequence))
            _candles.ReplaceLast(value.Candle);
        else
            _candles.Add(value.Candle);

        foreach (IndicatorRuntime runtime in _indicators)
            runtime.Points.AddOrReplace(runtime.Indicator.UpdateLast(_candles));
        _version++;
    }

    public bool TryCreateSnapshot(bool force, out SymbolSnapshot? snapshot)
    {
        long now = Stopwatch.GetTimestamp();
        if (!force && now - _lastSnapshotTimestamp < _snapshotIntervalTicks)
        {
            snapshot = null;
            return false;
        }
        _lastSnapshotTimestamp = now;

        Candle[] candles = Tail(_candles.Snapshot(), _snapshotBars);
        var indicators = new IndicatorSeriesSnapshot[_indicators.Length];
        for (int index = 0; index < _indicators.Length; index++)
        {
            IndicatorRuntime runtime = _indicators[index];
            indicators[index] = new(
                runtime.Indicator.Descriptor,
                Tail(runtime.Points.Snapshot(), _snapshotBars));
        }
        snapshot = new(
            Symbol,
            candles,
            indicators,
            _version,
            DateTimeOffset.UtcNow);
        return true;
    }

    private static T[] Tail<T>(T[] source, int count)
    {
        if (source.Length <= count) return source;
        var result = new T[count];
        Array.Copy(source, source.Length - count, result, 0, count);
        return result;
    }
}
