using ChartKit.CSharp.Modules.Abstractions;

namespace ChartKit.CSharp.Modules.Indicators;

public readonly record struct SmaValuePoint(
    long Sequence,
    float Value);

public readonly record struct SmaRuntimeDiagnostics(
    long FullCalculations,
    long LastUpdates,
    long Appends,
    long UnchangedSnapshots);

internal sealed class SmaSeriesRuntime
{
    private int _period;
    private double[] _window;
    private int _head;
    private int _count;
    private double _sum;
    private int _savedHead;
    private int _savedCount;
    private double _savedSum;
    private double _savedHeadValue;
    private long _committedSequence = -1;
    private long[] _sourceSequences = Array.Empty<long>();
    private double[] _sourceCloses = Array.Empty<double>();
    private SmaValuePoint[] _values = Array.Empty<SmaValuePoint>();
    private long _fullCalculations;
    private long _lastUpdates;
    private long _appends;
    private long _unchangedSnapshots;

    public SmaSeriesRuntime(int period)
    {
        _period = ValidatePeriod(period);
        _window = new double[_period];
    }

    public int Period => _period;
    public long DataVersion { get; private set; } = -1;
    public IReadOnlyList<SmaValuePoint> Values => _values;
    public SmaRuntimeDiagnostics Diagnostics => new(
        _fullCalculations,
        _lastUpdates,
        _appends,
        _unchangedSnapshots);

    public bool SetPeriod(int period)
    {
        int normalized = ValidatePeriod(period);
        if (normalized == _period) return false;

        _period = normalized;
        _window = new double[_period];
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

    public SmaValuePoint[] SnapshotValues() =>
        (SmaValuePoint[])_values.Clone();

    public void Reset()
    {
        ResetState(clearDiagnostics: true);
    }

    private void Recalculate(
        IReadOnlyList<ChartPrimaryBar> bars,
        long dataVersion)
    {
        ResetCalculationState();
        var values = new SmaValuePoint[bars.Count];
        var sequences = new long[bars.Count];
        var closes = new double[bars.Count];

        for (int index = 0; index < bars.Count; index++)
        {
            ChartPrimaryBar bar = bars[index];
            if (index == bars.Count - 1) SaveState();
            float value = Step(bar.Close);
            values[index] = new SmaValuePoint(bar.Sequence, value);
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
        ChartPrimaryBar bar = bars[^1];
        float value = Step(bar.Close);
        _values[^1] = new SmaValuePoint(bar.Sequence, value);
        _sourceCloses[^1] = bar.Close;
        _committedSequence = bar.Sequence;
        DataVersion = dataVersion;
        _lastUpdates++;
    }

    private void Append(
        IReadOnlyList<ChartPrimaryBar> bars,
        long dataVersion)
    {
        SaveState();
        ChartPrimaryBar bar = bars[^1];
        float value = Step(bar.Close);

        Array.Resize(ref _values, bars.Count);
        Array.Resize(ref _sourceSequences, bars.Count);
        Array.Resize(ref _sourceCloses, bars.Count);
        int index = bars.Count - 1;
        _values[index] = new SmaValuePoint(bar.Sequence, value);
        _sourceSequences[index] = bar.Sequence;
        _sourceCloses[index] = bar.Close;
        _committedSequence = bar.Sequence;
        DataVersion = dataVersion;
        _appends++;
    }

    private bool IsUnchanged(IReadOnlyList<ChartPrimaryBar> bars)
    {
        if (bars.Count != _sourceSequences.Length || bars.Count == 0)
            return false;

        for (int index = 0; index < bars.Count; index++)
        {
            if (bars[index].Sequence != _sourceSequences[index] ||
                bars[index].Close != _sourceCloses[index])
            {
                return false;
            }
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
            if (bars[index].Sequence != _sourceSequences[index] ||
                bars[index].Close != _sourceCloses[index])
            {
                return false;
            }
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
            if (bars[index].Sequence != _sourceSequences[index] ||
                bars[index].Close != _sourceCloses[index])
            {
                return false;
            }
        }

        return bars[^1].Sequence == _committedSequence + 1;
    }

    private float Step(double value)
    {
        if (_count < _period)
        {
            _window[(_head + _count) % _period] = value;
            _count++;
            _sum += value;
        }
        else
        {
            _sum += value - _window[_head];
            _window[_head] = value;
            _head = (_head + 1) % _period;
        }

        return _count == _period
            ? (float)(_sum / _period)
            : float.NaN;
    }

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
        _values = Array.Empty<SmaValuePoint>();
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
        _savedHeadValue = 0d;
        _committedSequence = -1;
        Array.Clear(_window);
    }

    private static int ValidatePeriod(int period)
    {
        if (period < 1 || period > 10_000)
            throw new ArgumentOutOfRangeException(nameof(period));
        return period;
    }
}
