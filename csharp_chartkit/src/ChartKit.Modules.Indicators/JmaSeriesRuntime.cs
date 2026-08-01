using ChartKit.CSharp.Modules.Abstractions;

namespace ChartKit.CSharp.Modules.Indicators;

public readonly record struct JmaValuePoint(
    long Sequence,
    float Value,
    float Up,
    float Down,
    float Slope);

public readonly record struct JmaRuntimeDiagnostics(
    long FullCalculations,
    long LastUpdates,
    long Appends,
    long UnchangedSnapshots);

internal sealed class JmaSeriesRuntime
{
    private int _period;
    private int _phase;
    private int _power;
    private double _e0;
    private double _e1;
    private double _e2;
    private double _lastJma;
    private double _warmSum;
    private int _direction;
    private int _count;
    private bool _initialized;

    private double _savedE0;
    private double _savedE1;
    private double _savedE2;
    private double _savedLastJma;
    private double _savedWarmSum;
    private int _savedDirection;
    private int _savedCount;
    private bool _savedInitialized;

    private long _committedSequence = -1;
    private long[] _sourceSequences = Array.Empty<long>();
    private double[] _sourceCloses = Array.Empty<double>();
    private JmaValuePoint[] _values = Array.Empty<JmaValuePoint>();
    private long _fullCalculations;
    private long _lastUpdates;
    private long _appends;
    private long _unchangedSnapshots;

    public JmaSeriesRuntime(int period, int phase, int power)
    {
        ValidateParameters(period, phase, power);
        _period = period;
        _phase = phase;
        _power = power;
    }

    public long DataVersion { get; private set; } = -1;
    public IReadOnlyList<JmaValuePoint> Values => _values;
    public JmaRuntimeDiagnostics Diagnostics => new(
        _fullCalculations,
        _lastUpdates,
        _appends,
        _unchangedSnapshots);

    public bool SetParameters(int period, int phase, int power)
    {
        ValidateParameters(period, phase, power);
        if (period == _period && phase == _phase && power == _power)
            return false;

        _period = period;
        _phase = phase;
        _power = power;
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

    public JmaValuePoint[] SnapshotValues() =>
        (JmaValuePoint[])_values.Clone();

    public void Reset() => ResetState(clearDiagnostics: true);

    private void Recalculate(
        IReadOnlyList<ChartPrimaryBar> bars,
        long dataVersion)
    {
        ResetCalculationState();
        var values = new JmaValuePoint[bars.Count];
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
        JmaValuePoint point = Step(bar);

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

    private JmaValuePoint Step(ChartPrimaryBar bar)
    {
        double source = bar.Close;
        if (!_initialized)
        {
            _e0 = source;
            _e1 = 0d;
            _e2 = 0d;
            _lastJma = source;
            _initialized = true;
        }

        double beta = 0.45d * (_period - 1) /
                      (0.45d * (_period - 1) + 2d);
        double alpha = Math.Pow(beta, _power);
        _e0 = (1d - alpha) * source + alpha * _e0;
        _e1 = (source - _e0) * (1d - beta) + beta * _e1;
        _e2 = (_e0 + (_phase / 100d + 1.5d) * _e1 - _lastJma) *
              Math.Pow(1d - alpha, 2d) + Math.Pow(alpha, 2d) * _e2;

        _count++;
        _warmSum += source;
        double current = _count <= _period
            ? Math.Round(_warmSum / _count, 4)
            : Math.Round(_e2 + _lastJma, 4);
        double previous = _lastJma;
        if (current > previous) _direction = 1;
        else if (current < previous) _direction = -1;
        else if (_direction == 0) _direction = 1;

        float slope = previous != 0d
            ? (float)Math.Round((current / previous - 1d) * 100d, 1)
            : 0f;
        _lastJma = current;
        float value = (float)current;
        return new JmaValuePoint(
            bar.Sequence,
            value,
            _direction == 1 ? value : float.NaN,
            _direction == -1 ? value : float.NaN,
            slope);
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
        _savedE0 = _e0;
        _savedE1 = _e1;
        _savedE2 = _e2;
        _savedLastJma = _lastJma;
        _savedWarmSum = _warmSum;
        _savedDirection = _direction;
        _savedCount = _count;
        _savedInitialized = _initialized;
    }

    private void RestoreState()
    {
        _e0 = _savedE0;
        _e1 = _savedE1;
        _e2 = _savedE2;
        _lastJma = _savedLastJma;
        _warmSum = _savedWarmSum;
        _direction = _savedDirection;
        _count = _savedCount;
        _initialized = _savedInitialized;
    }

    private void ResetState(bool clearDiagnostics)
    {
        ResetCalculationState();
        _sourceSequences = Array.Empty<long>();
        _sourceCloses = Array.Empty<double>();
        _values = Array.Empty<JmaValuePoint>();
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
        _e0 = 0d;
        _e1 = 0d;
        _e2 = 0d;
        _lastJma = 0d;
        _warmSum = 0d;
        _direction = 0;
        _count = 0;
        _initialized = false;
        _savedE0 = 0d;
        _savedE1 = 0d;
        _savedE2 = 0d;
        _savedLastJma = 0d;
        _savedWarmSum = 0d;
        _savedDirection = 0;
        _savedCount = 0;
        _savedInitialized = false;
        _committedSequence = -1;
    }

    private static void ValidateParameters(int period, int phase, int power)
    {
        if (period < 1 || period > 10_000)
            throw new ArgumentOutOfRangeException(nameof(period));
        if (phase < -100 || phase > 100)
            throw new ArgumentOutOfRangeException(nameof(phase));
        if (power < 1 || power > 10_000)
            throw new ArgumentOutOfRangeException(nameof(power));
    }
}
