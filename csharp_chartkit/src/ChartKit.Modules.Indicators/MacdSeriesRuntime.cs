using ChartKit.CSharp.Modules.Abstractions;

namespace ChartKit.CSharp.Modules.Indicators;

public readonly record struct MacdValuePoint(
    long Sequence,
    float Macd,
    float Signal,
    float Histogram);

public readonly record struct MacdRuntimeDiagnostics(
    long FullCalculations,
    long LastUpdates,
    long Appends,
    long UnchangedSnapshots);

internal sealed class MacdSeriesRuntime
{
    private int _fastPeriod;
    private int _slowPeriod;
    private int _signalPeriod;
    private int _closeCount;
    private int _signalCount;
    private double _fastSeedSum;
    private double _slowSeedSum;
    private double _signalSeedSum;
    private float _fastEma = float.NaN;
    private float _slowEma = float.NaN;
    private float _signalEma = float.NaN;
    private int _savedCloseCount;
    private int _savedSignalCount;
    private double _savedFastSeedSum;
    private double _savedSlowSeedSum;
    private double _savedSignalSeedSum;
    private float _savedFastEma;
    private float _savedSlowEma;
    private float _savedSignalEma;
    private long _committedSequence = -1;
    private long[] _sourceSequences = Array.Empty<long>();
    private double[] _sourceCloses = Array.Empty<double>();
    private MacdValuePoint[] _values = Array.Empty<MacdValuePoint>();
    private long _fullCalculations;
    private long _lastUpdates;
    private long _appends;
    private long _unchangedSnapshots;

    public MacdSeriesRuntime(int fastPeriod, int slowPeriod, int signalPeriod)
    {
        ValidateParameters(fastPeriod, slowPeriod, signalPeriod);
        _fastPeriod = fastPeriod;
        _slowPeriod = slowPeriod;
        _signalPeriod = signalPeriod;
    }

    public int FastPeriod => _fastPeriod;
    public int SlowPeriod => _slowPeriod;
    public int SignalPeriod => _signalPeriod;
    public long DataVersion { get; private set; } = -1;
    public IReadOnlyList<MacdValuePoint> Values => _values;
    public MacdRuntimeDiagnostics Diagnostics => new(
        _fullCalculations,
        _lastUpdates,
        _appends,
        _unchangedSnapshots);

    public bool SetParameters(int fastPeriod, int slowPeriod, int signalPeriod)
    {
        ValidateParameters(fastPeriod, slowPeriod, signalPeriod);
        if (fastPeriod == _fastPeriod &&
            slowPeriod == _slowPeriod &&
            signalPeriod == _signalPeriod)
        {
            return false;
        }

        _fastPeriod = fastPeriod;
        _slowPeriod = slowPeriod;
        _signalPeriod = signalPeriod;
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

    public MacdValuePoint[] SnapshotValues() =>
        (MacdValuePoint[])_values.Clone();

    public void Reset() => ResetState(clearDiagnostics: true);

    private void Recalculate(
        IReadOnlyList<ChartPrimaryBar> bars,
        long dataVersion)
    {
        ResetCalculationState();
        var values = new MacdValuePoint[bars.Count];
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
        MacdValuePoint point = Step(bar);

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

    private MacdValuePoint Step(ChartPrimaryBar bar)
    {
        _closeCount++;
        float close = (float)bar.Close;
        _fastEma = StepEma(
            close,
            _fastPeriod,
            _closeCount,
            ref _fastSeedSum,
            _fastEma);
        _slowEma = StepEma(
            close,
            _slowPeriod,
            _closeCount,
            ref _slowSeedSum,
            _slowEma);

        float macd = !float.IsNaN(_fastEma) && !float.IsNaN(_slowEma)
            ? _fastEma - _slowEma
            : float.NaN;
        float signal = float.NaN;
        if (!float.IsNaN(macd))
        {
            _signalCount++;
            if (_signalCount < _signalPeriod)
            {
                _signalSeedSum += macd;
            }
            else if (_signalCount == _signalPeriod)
            {
                _signalSeedSum += macd;
                _signalEma = (float)(_signalSeedSum / _signalPeriod);
                signal = _signalEma;
            }
            else
            {
                float factor = 2f / (_signalPeriod + 1);
                _signalEma = macd * factor + _signalEma * (1f - factor);
                signal = _signalEma;
            }
        }

        float histogram = float.IsNaN(macd) || float.IsNaN(signal)
            ? float.NaN
            : macd - signal;
        return new MacdValuePoint(bar.Sequence, macd, signal, histogram);
    }

    private static float StepEma(
        float close,
        int period,
        int count,
        ref double seedSum,
        float current)
    {
        if (count < period)
        {
            seedSum += close;
            return float.NaN;
        }
        if (count == period)
        {
            seedSum += close;
            return (float)(seedSum / period);
        }
        float factor = 2f / (period + 1);
        return close * factor + current * (1f - factor);
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

    private void SaveState()
    {
        _savedCloseCount = _closeCount;
        _savedSignalCount = _signalCount;
        _savedFastSeedSum = _fastSeedSum;
        _savedSlowSeedSum = _slowSeedSum;
        _savedSignalSeedSum = _signalSeedSum;
        _savedFastEma = _fastEma;
        _savedSlowEma = _slowEma;
        _savedSignalEma = _signalEma;
    }

    private void RestoreState()
    {
        _closeCount = _savedCloseCount;
        _signalCount = _savedSignalCount;
        _fastSeedSum = _savedFastSeedSum;
        _slowSeedSum = _savedSlowSeedSum;
        _signalSeedSum = _savedSignalSeedSum;
        _fastEma = _savedFastEma;
        _slowEma = _savedSlowEma;
        _signalEma = _savedSignalEma;
    }

    private void ResetState(bool clearDiagnostics)
    {
        ResetCalculationState();
        _sourceSequences = Array.Empty<long>();
        _sourceCloses = Array.Empty<double>();
        _values = Array.Empty<MacdValuePoint>();
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
        _closeCount = 0;
        _signalCount = 0;
        _fastSeedSum = 0d;
        _slowSeedSum = 0d;
        _signalSeedSum = 0d;
        _fastEma = float.NaN;
        _slowEma = float.NaN;
        _signalEma = float.NaN;
        _savedCloseCount = 0;
        _savedSignalCount = 0;
        _savedFastSeedSum = 0d;
        _savedSlowSeedSum = 0d;
        _savedSignalSeedSum = 0d;
        _savedFastEma = float.NaN;
        _savedSlowEma = float.NaN;
        _savedSignalEma = float.NaN;
        _committedSequence = -1;
    }

    private static void ValidateParameters(
        int fastPeriod,
        int slowPeriod,
        int signalPeriod)
    {
        if (fastPeriod < 1 || fastPeriod > 10_000)
            throw new ArgumentOutOfRangeException(nameof(fastPeriod));
        if (slowPeriod <= fastPeriod || slowPeriod > 10_000)
            throw new ArgumentOutOfRangeException(nameof(slowPeriod));
        if (signalPeriod < 1 || signalPeriod > 10_000)
            throw new ArgumentOutOfRangeException(nameof(signalPeriod));
    }
}
