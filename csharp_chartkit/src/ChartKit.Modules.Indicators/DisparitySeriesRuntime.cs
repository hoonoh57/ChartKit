using ChartKit.CSharp.Modules.Abstractions;

namespace ChartKit.CSharp.Modules.Indicators;

public readonly record struct DisparityValuePoint(
    long Sequence,
    float Value,
    float MovingAverage);

public readonly record struct DisparityRuntimeDiagnostics(
    long FullCalculations,
    long LastUpdates,
    long Appends,
    long UnchangedSnapshots);

internal sealed class DisparitySeriesRuntime
{
    private int _period;
    private float[] _window;
    private int _head;
    private int _count;
    private double _sum;

    private int _savedHead;
    private int _savedCount;
    private double _savedSum;
    private float _savedHeadValue;

    private long _committedSequence = -1;
    private long[] _sourceSequences = Array.Empty<long>();
    private double[] _sourceCloses = Array.Empty<double>();
    private DisparityValuePoint[] _values = Array.Empty<DisparityValuePoint>();
    private long _fullCalculations;
    private long _lastUpdates;
    private long _appends;
    private long _unchangedSnapshots;

    public DisparitySeriesRuntime(int period)
    {
        _period = ValidatePeriod(period, nameof(period));
        _window = new float[_period];
    }

    public int Period => _period;
    public long DataVersion { get; private set; } = -1;
    public IReadOnlyList<DisparityValuePoint> Values => _values;
    public DisparityRuntimeDiagnostics Diagnostics => new(
        _fullCalculations,
        _lastUpdates,
        _appends,
        _unchangedSnapshots);

    public bool SetPeriod(int period)
    {
        int normalized = ValidatePeriod(period, nameof(period));
        if (normalized == _period) return false;

        _period = normalized;
        _window = new float[_period];
        ResetState(clearDiagnostics: false);
        return true;
    }

    public void Apply(ChartPrimarySeriesSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        IReadOnlyList<ChartPrimaryBar> bars = snapshot.Bars;

        if (bars.Count == 0)
        {
            ResetState(clearDiagnostics: false);
            DataVersion = snapshot.DataVersion;
            return;
        }

        if (IsUnchanged(bars))
        {
            DataVersion = snapshot.DataVersion;
            _unchangedSnapshots++;
            return;
        }

        if (CanUpdateLast(bars))
        {
            UpdateLast(bars, snapshot.DataVersion);
            return;
        }

        if (CanAppend(bars))
        {
            Append(bars, snapshot.DataVersion);
            return;
        }

        Recalculate(bars, snapshot.DataVersion);
    }

    public DisparityValuePoint[] SnapshotValues() =>
        (DisparityValuePoint[])_values.Clone();

    public void Reset() => ResetState(clearDiagnostics: true);

    private void Recalculate(
        IReadOnlyList<ChartPrimaryBar> bars,
        long dataVersion)
    {
        ResetCalculationState();
        var values = new DisparityValuePoint[bars.Count];
        var sequences = new long[bars.Count];
        var closes = new double[bars.Count];

        for (int index = 0; index < bars.Count; index++)
        {
            ChartPrimaryBar bar = bars[index];
            if (index == bars.Count - 1) SaveState();
            values[index] = Step(bar);
            sequences[index] = bar.Sequence;
            closes[index] = bar.Close;
        }

        _values = values;
        _sourceSequences = sequences;
        _sourceCloses = closes;
        _committedSequence = bars[^1].Sequence;
        DataVersion = dataVersion;
        _fullCalculations++;
    }

    private void UpdateLast(
        IReadOnlyList<ChartPrimaryBar> bars,
        long dataVersion)
    {
        RestoreState();
        int index = bars.Count - 1;
        ChartPrimaryBar bar = bars[index];
        _values[index] = Step(bar);
        _sourceCloses[index] = bar.Close;
        _committedSequence = bar.Sequence;
        DataVersion = dataVersion;
        _lastUpdates++;
    }

    private void Append(
        IReadOnlyList<ChartPrimaryBar> bars,
        long dataVersion)
    {
        SaveState();
        int index = bars.Count - 1;
        ChartPrimaryBar bar = bars[index];
        DisparityValuePoint point = Step(bar);

        Array.Resize(ref _values, bars.Count);
        Array.Resize(ref _sourceSequences, bars.Count);
        Array.Resize(ref _sourceCloses, bars.Count);
        _values[index] = point;
        _sourceSequences[index] = bar.Sequence;
        _sourceCloses[index] = bar.Close;
        _committedSequence = bar.Sequence;
        DataVersion = dataVersion;
        _appends++;
    }

    private DisparityValuePoint Step(ChartPrimaryBar bar)
    {
        float close = (float)bar.Close;
        if (_count < _period)
        {
            _window[(_head + _count) % _period] = close;
            _count++;
            _sum += close;
        }
        else
        {
            _sum += close - _window[_head];
            _window[_head] = close;
            _head = (_head + 1) % _period;
        }

        float movingAverage = _count == _period
            ? (float)(_sum / _period)
            : float.NaN;
        float value = float.IsNaN(movingAverage)
            ? float.NaN
            : movingAverage > 0f
                ? close / movingAverage * 100f
                : 100f;
        return new DisparityValuePoint(
            bar.Sequence,
            value,
            movingAverage);
    }

    private bool IsUnchanged(IReadOnlyList<ChartPrimaryBar> bars)
    {
        if (bars.Count != _sourceSequences.Length || bars.Count == 0)
            return false;
        for (int index = 0; index < bars.Count; index++)
        {
            if (!Matches(bars[index], index)) return false;
        }
        return true;
    }

    private bool CanUpdateLast(IReadOnlyList<ChartPrimaryBar> bars)
    {
        if (bars.Count != _sourceSequences.Length || bars.Count == 0)
            return false;
        int last = bars.Count - 1;
        for (int index = 0; index < last; index++)
        {
            if (!Matches(bars[index], index)) return false;
        }
        return bars[last].Sequence == _committedSequence;
    }

    private bool CanAppend(IReadOnlyList<ChartPrimaryBar> bars)
    {
        if (bars.Count != _sourceSequences.Length + 1 ||
            _sourceSequences.Length == 0)
        {
            return false;
        }
        for (int index = 0; index < _sourceSequences.Length; index++)
        {
            if (!Matches(bars[index], index)) return false;
        }
        return bars[^1].Sequence == _committedSequence + 1;
    }

    private bool Matches(ChartPrimaryBar bar, int index) =>
        bar.Sequence == _sourceSequences[index] &&
        bar.Close == _sourceCloses[index];

    private void SaveState()
    {
        _savedHead = _head;
        _savedCount = _count;
        _savedSum = _sum;
        if (_count == _period)
            _savedHeadValue = _window[_head];
    }

    private void RestoreState()
    {
        _head = _savedHead;
        _count = _savedCount;
        _sum = _savedSum;
        if (_count == _period)
            _window[_head] = _savedHeadValue;
    }

    private void ResetState(bool clearDiagnostics)
    {
        ResetCalculationState();
        _sourceSequences = Array.Empty<long>();
        _sourceCloses = Array.Empty<double>();
        _values = Array.Empty<DisparityValuePoint>();
        DataVersion = -1;
        if (clearDiagnostics)
        {
            _fullCalculations = 0;
            _lastUpdates = 0;
            _appends = 0;
            _unchangedSnapshots = 0;
        }
    }

    private void ResetCalculationState()
    {
        _head = 0;
        _count = 0;
        _sum = 0d;
        _savedHead = 0;
        _savedCount = 0;
        _savedSum = 0d;
        _savedHeadValue = 0f;
        _committedSequence = -1;
        Array.Clear(_window);
    }

    private static int ValidatePeriod(int period, string parameterName)
    {
        if (period < 1 || period > 10_000)
            throw new ArgumentOutOfRangeException(parameterName);
        return period;
    }
}
