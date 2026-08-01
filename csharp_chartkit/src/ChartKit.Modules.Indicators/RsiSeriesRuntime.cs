using ChartKit.CSharp.Modules.Abstractions;

namespace ChartKit.CSharp.Modules.Indicators;

public readonly record struct RsiValuePoint(
    long Sequence,
    float Rsi,
    float Signal);

public readonly record struct RsiRuntimeDiagnostics(
    long FullCalculations,
    long LastUpdates,
    long Appends,
    long UnchangedSnapshots);

internal sealed class RsiSeriesRuntime
{
    private int _period;
    private int _signalPeriod;
    private float[] _signalValues;
    private float _previousClose;
    private float _upSum;
    private float _downSum;
    private float _averageUp;
    private float _averageDown;
    private int _diffCount;
    private int _signalHead;
    private int _signalCount;
    private double _signalSum;
    private float _savedPreviousClose;
    private float _savedUpSum;
    private float _savedDownSum;
    private float _savedAverageUp;
    private float _savedAverageDown;
    private float _savedSignalHeadValue;
    private int _savedDiffCount;
    private int _savedSignalHead;
    private int _savedSignalCount;
    private double _savedSignalSum;
    private long _committedSequence = -1;
    private long[] _sourceSequences = Array.Empty<long>();
    private double[] _sourceCloses = Array.Empty<double>();
    private RsiValuePoint[] _values = Array.Empty<RsiValuePoint>();
    private long _fullCalculations;
    private long _lastUpdates;
    private long _appends;
    private long _unchangedSnapshots;

    public RsiSeriesRuntime(int period, int signalPeriod)
    {
        _period = ValidatePeriod(period, nameof(period));
        _signalPeriod = ValidatePeriod(signalPeriod, nameof(signalPeriod));
        _signalValues = new float[_signalPeriod];
    }

    public int Period => _period;
    public int SignalPeriod => _signalPeriod;
    public long DataVersion { get; private set; } = -1;
    public IReadOnlyList<RsiValuePoint> Values => _values;
    public RsiRuntimeDiagnostics Diagnostics => new(
        _fullCalculations,
        _lastUpdates,
        _appends,
        _unchangedSnapshots);

    public bool SetParameters(int period, int signalPeriod)
    {
        int normalizedPeriod = ValidatePeriod(period, nameof(period));
        int normalizedSignal = ValidatePeriod(signalPeriod, nameof(signalPeriod));
        if (normalizedPeriod == _period && normalizedSignal == _signalPeriod)
            return false;

        _period = normalizedPeriod;
        _signalPeriod = normalizedSignal;
        _signalValues = new float[_signalPeriod];
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

    public RsiValuePoint[] SnapshotValues() =>
        (RsiValuePoint[])_values.Clone();

    public void Reset()
    {
        ResetState(clearDiagnostics: true);
    }

    private void Recalculate(
        IReadOnlyList<ChartPrimaryBar> bars,
        long dataVersion)
    {
        ResetCalculationState();
        var values = new RsiValuePoint[bars.Count];
        var sequences = new long[bars.Count];
        var closes = new double[bars.Count];

        for (int index = 0; index < bars.Count; index++)
        {
            ChartPrimaryBar bar = bars[index];
            if (index == bars.Count - 1) SaveState();
            values[index] = Step(bar, index);
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
        _values[index] = Step(bar, index);
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
        RsiValuePoint point = Step(bar, index);

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

    private RsiValuePoint Step(ChartPrimaryBar bar, int index)
    {
        float close = (float)bar.Close;
        float rsi = float.NaN;
        if (index == 0)
        {
            _previousClose = close;
        }
        else
        {
            float difference = close - _previousClose;
            float up = Math.Max(difference, 0f);
            float down = Math.Max(-difference, 0f);
            _diffCount++;
            if (_diffCount <= _period)
            {
                _upSum += up;
                _downSum += down;
                if (_diffCount == _period)
                {
                    _averageUp = _upSum / _period;
                    _averageDown = _downSum / _period;
                    rsi = CalculateRsi(_averageUp, _averageDown);
                }
            }
            else
            {
                _averageUp = (_averageUp * (_period - 1) + up) / _period;
                _averageDown = (_averageDown * (_period - 1) + down) / _period;
                rsi = CalculateRsi(_averageUp, _averageDown);
            }
            _previousClose = close;
        }

        float signal = PushSignal(rsi);
        return new RsiValuePoint(bar.Sequence, rsi, signal);
    }

    private float PushSignal(float value)
    {
        if (float.IsNaN(value)) return float.NaN;
        if (_signalCount < _signalPeriod)
        {
            int slot = (_signalHead + _signalCount) % _signalPeriod;
            _signalValues[slot] = value;
            _signalCount++;
            _signalSum += value;
        }
        else
        {
            _signalSum -= _signalValues[_signalHead];
            _signalValues[_signalHead] = value;
            _signalSum += value;
            _signalHead = (_signalHead + 1) % _signalPeriod;
        }

        return _signalCount == _signalPeriod
            ? (float)(_signalSum / _signalPeriod)
            : float.NaN;
    }

    private static float CalculateRsi(float averageUp, float averageDown) =>
        averageDown == 0f
            ? 100f
            : 100f - 100f / (1f + averageUp / averageDown);

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

    private void SaveState()
    {
        _savedPreviousClose = _previousClose;
        _savedUpSum = _upSum;
        _savedDownSum = _downSum;
        _savedAverageUp = _averageUp;
        _savedAverageDown = _averageDown;
        _savedDiffCount = _diffCount;
        _savedSignalHead = _signalHead;
        _savedSignalCount = _signalCount;
        _savedSignalSum = _signalSum;
        if (_signalCount == _signalPeriod)
            _savedSignalHeadValue = _signalValues[_signalHead];
    }

    private void RestoreState()
    {
        _previousClose = _savedPreviousClose;
        _upSum = _savedUpSum;
        _downSum = _savedDownSum;
        _averageUp = _savedAverageUp;
        _averageDown = _savedAverageDown;
        _diffCount = _savedDiffCount;
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
        _values = Array.Empty<RsiValuePoint>();
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
        _upSum = 0f;
        _downSum = 0f;
        _averageUp = 0f;
        _averageDown = 0f;
        _diffCount = 0;
        _signalHead = 0;
        _signalCount = 0;
        _signalSum = 0d;
        _savedPreviousClose = 0f;
        _savedUpSum = 0f;
        _savedDownSum = 0f;
        _savedAverageUp = 0f;
        _savedAverageDown = 0f;
        _savedSignalHeadValue = 0f;
        _savedDiffCount = 0;
        _savedSignalHead = 0;
        _savedSignalCount = 0;
        _savedSignalSum = 0d;
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
