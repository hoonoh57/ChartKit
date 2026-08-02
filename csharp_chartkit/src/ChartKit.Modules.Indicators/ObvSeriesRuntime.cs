using ChartKit.CSharp.Modules.Abstractions;

namespace ChartKit.CSharp.Modules.Indicators;

public readonly record struct ObvValuePoint(
    long Sequence,
    float Obv,
    float Signal,
    float Direction);

public readonly record struct ObvRuntimeDiagnostics(
    long FullCalculations,
    long LastUpdates,
    long Appends,
    long UnchangedSnapshots);

internal sealed class ObvSeriesRuntime
{
    private int _signalPeriod;
    private double[] _signalValues;
    private float _previousClose;
    private bool _hasPrevious;
    private double _obv;
    private double _signalSum;
    private int _signalHead;
    private int _signalCount;

    private float _savedPreviousClose;
    private bool _savedHasPrevious;
    private double _savedObv;
    private double _savedSignalSum;
    private double _savedSignalHeadValue;
    private int _savedSignalHead;
    private int _savedSignalCount;

    private long _committedSequence = -1;
    private long[] _sourceSequences = Array.Empty<long>();
    private double[] _sourceCloses = Array.Empty<double>();
    private long[] _sourceVolumes = Array.Empty<long>();
    private ObvValuePoint[] _values = Array.Empty<ObvValuePoint>();
    private long _fullCalculations;
    private long _lastUpdates;
    private long _appends;
    private long _unchangedSnapshots;

    public ObvSeriesRuntime(int signalPeriod)
    {
        _signalPeriod = ValidatePeriod(signalPeriod, nameof(signalPeriod));
        _signalValues = new double[_signalPeriod];
    }

    public int SignalPeriod => _signalPeriod;
    public long DataVersion { get; private set; } = -1;
    public IReadOnlyList<ObvValuePoint> Values => _values;
    public ObvRuntimeDiagnostics Diagnostics => new(
        _fullCalculations,
        _lastUpdates,
        _appends,
        _unchangedSnapshots);

    public bool SetSignalPeriod(int signalPeriod)
    {
        int normalized = ValidatePeriod(signalPeriod, nameof(signalPeriod));
        if (normalized == _signalPeriod) return false;

        _signalPeriod = normalized;
        _signalValues = new double[_signalPeriod];
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

    public ObvValuePoint[] SnapshotValues() =>
        (ObvValuePoint[])_values.Clone();

    public void Reset() => ResetState(clearDiagnostics: true);

    private void Recalculate(
        IReadOnlyList<ChartPrimaryBar> bars,
        long dataVersion)
    {
        ResetCalculationState();
        var values = new ObvValuePoint[bars.Count];
        var sequences = new long[bars.Count];
        var closes = new double[bars.Count];
        var volumes = new long[bars.Count];

        for (int index = 0; index < bars.Count; index++)
        {
            ChartPrimaryBar bar = bars[index];
            if (index == bars.Count - 1) SaveState();
            values[index] = Step(bar);
            sequences[index] = bar.Sequence;
            closes[index] = bar.Close;
            volumes[index] = bar.Volume;
        }

        _values = values;
        _sourceSequences = sequences;
        _sourceCloses = closes;
        _sourceVolumes = volumes;
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
        _sourceVolumes[index] = bar.Volume;
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
        ObvValuePoint point = Step(bar);

        Array.Resize(ref _values, bars.Count);
        Array.Resize(ref _sourceSequences, bars.Count);
        Array.Resize(ref _sourceCloses, bars.Count);
        Array.Resize(ref _sourceVolumes, bars.Count);
        _values[index] = point;
        _sourceSequences[index] = bar.Sequence;
        _sourceCloses[index] = bar.Close;
        _sourceVolumes[index] = bar.Volume;
        _committedSequence = bar.Sequence;
        DataVersion = dataVersion;
        _appends++;
    }

    private ObvValuePoint Step(ChartPrimaryBar bar)
    {
        float close = (float)bar.Close;
        if (!_hasPrevious)
        {
            _obv = bar.Volume;
            _hasPrevious = true;
        }
        else if (close > _previousClose)
        {
            _obv += bar.Volume;
        }
        else if (close < _previousClose)
        {
            _obv -= bar.Volume;
        }

        _previousClose = close;
        PushSignal(_obv);
        float signal = _signalCount == _signalPeriod
            ? (float)(_signalSum / _signalPeriod)
            : float.NaN;
        float value = (float)_obv;
        float direction = float.IsNaN(signal)
            ? float.NaN
            : value > signal ? 1f : -1f;
        return new ObvValuePoint(bar.Sequence, value, signal, direction);
    }

    private void PushSignal(double value)
    {
        if (_signalCount < _signalPeriod)
        {
            int slot = (_signalHead + _signalCount) % _signalPeriod;
            _signalValues[slot] = value;
            _signalCount++;
            _signalSum += value;
        }
        else
        {
            _signalSum += value - _signalValues[_signalHead];
            _signalValues[_signalHead] = value;
            _signalHead = (_signalHead + 1) % _signalPeriod;
        }
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
        bar.Close == _sourceCloses[index] &&
        bar.Volume == _sourceVolumes[index];

    private void SaveState()
    {
        _savedPreviousClose = _previousClose;
        _savedHasPrevious = _hasPrevious;
        _savedObv = _obv;
        _savedSignalHead = _signalHead;
        _savedSignalCount = _signalCount;
        _savedSignalSum = _signalSum;
        if (_signalCount == _signalPeriod)
            _savedSignalHeadValue = _signalValues[_signalHead];
    }

    private void RestoreState()
    {
        _previousClose = _savedPreviousClose;
        _hasPrevious = _savedHasPrevious;
        _obv = _savedObv;
        _signalHead = _savedSignalHead;
        _signalCount = _savedSignalCount;
        _signalSum = _savedSignalSum;
        if (_signalCount == _signalPeriod)
            _signalValues[_signalHead] = _savedSignalHeadValue;
    }

    private void ResetState(bool clearDiagnostics)
    {
        ResetCalculationState();
        _sourceSequences = Array.Empty<long>();
        _sourceCloses = Array.Empty<double>();
        _sourceVolumes = Array.Empty<long>();
        _values = Array.Empty<ObvValuePoint>();
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
        _previousClose = 0f;
        _hasPrevious = false;
        _obv = 0d;
        _signalSum = 0d;
        _signalHead = 0;
        _signalCount = 0;
        _savedPreviousClose = 0f;
        _savedHasPrevious = false;
        _savedObv = 0d;
        _savedSignalSum = 0d;
        _savedSignalHeadValue = 0d;
        _savedSignalHead = 0;
        _savedSignalCount = 0;
        _committedSequence = -1;
        Array.Clear(_signalValues);
    }

    private static int ValidatePeriod(int period, string parameterName)
    {
        if (period < 1 || period > 10_000)
            throw new ArgumentOutOfRangeException(parameterName);
        return period;
    }
}
