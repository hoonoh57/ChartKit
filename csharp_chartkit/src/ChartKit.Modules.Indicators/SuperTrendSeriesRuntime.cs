using ChartKit.CSharp.Modules.Abstractions;

namespace ChartKit.CSharp.Modules.Indicators;

public readonly record struct SuperTrendValuePoint(
    long Sequence,
    float Value,
    float Up,
    float Down,
    int Direction,
    float Atr);

public readonly record struct SuperTrendRuntimeDiagnostics(
    long FullCalculations,
    long LastUpdates,
    long Appends,
    long UnchangedSnapshots);

internal sealed class SuperTrendSeriesRuntime
{
    private int _period;
    private float _multiplier;
    private float _previousClose;
    private float _atr = float.NaN;
    private float _upper = float.NaN;
    private float _lower = float.NaN;
    private float _superTrend = float.NaN;
    private double _trSum;
    private int _count;
    private int _direction = 1;
    private bool _hasPrevious;

    private float _savedPreviousClose;
    private float _savedAtr = float.NaN;
    private float _savedUpper = float.NaN;
    private float _savedLower = float.NaN;
    private float _savedSuperTrend = float.NaN;
    private double _savedTrSum;
    private int _savedCount;
    private int _savedDirection = 1;
    private bool _savedHasPrevious;

    private long _committedSequence = -1;
    private long[] _sourceSequences = Array.Empty<long>();
    private double[] _sourceHighs = Array.Empty<double>();
    private double[] _sourceLows = Array.Empty<double>();
    private double[] _sourceCloses = Array.Empty<double>();
    private SuperTrendValuePoint[] _values = Array.Empty<SuperTrendValuePoint>();
    private long _fullCalculations;
    private long _lastUpdates;
    private long _appends;
    private long _unchangedSnapshots;

    public SuperTrendSeriesRuntime(int period, float multiplier)
    {
        ValidateParameters(period, multiplier);
        _period = period;
        _multiplier = multiplier;
    }

    public long DataVersion { get; private set; } = -1;
    public IReadOnlyList<SuperTrendValuePoint> Values => _values;
    public SuperTrendRuntimeDiagnostics Diagnostics => new(
        _fullCalculations,
        _lastUpdates,
        _appends,
        _unchangedSnapshots);

    public bool SetParameters(int period, float multiplier)
    {
        ValidateParameters(period, multiplier);
        if (period == _period && multiplier.Equals(_multiplier)) return false;
        _period = period;
        _multiplier = multiplier;
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

    public SuperTrendValuePoint[] SnapshotValues() =>
        (SuperTrendValuePoint[])_values.Clone();

    public void Reset() => ResetState(clearDiagnostics: true);

    private void Recalculate(IReadOnlyList<ChartPrimaryBar> bars, long dataVersion)
    {
        ResetCalculationState();
        var values = new SuperTrendValuePoint[bars.Count];
        var sequences = new long[bars.Count];
        var highs = new double[bars.Count];
        var lows = new double[bars.Count];
        var closes = new double[bars.Count];
        for (int index = 0; index < bars.Count; index++)
        {
            ChartPrimaryBar bar = bars[index];
            if (index == bars.Count - 1) SaveState();
            values[index] = Step(bar);
            sequences[index] = bar.Sequence;
            highs[index] = bar.High;
            lows[index] = bar.Low;
            closes[index] = bar.Close;
        }
        _values = values;
        _sourceSequences = sequences;
        _sourceHighs = highs;
        _sourceLows = lows;
        _sourceCloses = closes;
        _committedSequence = bars[^1].Sequence;
        DataVersion = dataVersion;
        _fullCalculations++;
    }

    private void UpdateLast(IReadOnlyList<ChartPrimaryBar> bars, long dataVersion)
    {
        RestoreState();
        int index = bars.Count - 1;
        ChartPrimaryBar bar = bars[index];
        _values[index] = Step(bar);
        _sourceHighs[index] = bar.High;
        _sourceLows[index] = bar.Low;
        _sourceCloses[index] = bar.Close;
        _committedSequence = bar.Sequence;
        DataVersion = dataVersion;
        _lastUpdates++;
    }

    private void Append(IReadOnlyList<ChartPrimaryBar> bars, long dataVersion)
    {
        SaveState();
        int index = bars.Count - 1;
        ChartPrimaryBar bar = bars[index];
        SuperTrendValuePoint point = Step(bar);
        Array.Resize(ref _values, bars.Count);
        Array.Resize(ref _sourceSequences, bars.Count);
        Array.Resize(ref _sourceHighs, bars.Count);
        Array.Resize(ref _sourceLows, bars.Count);
        Array.Resize(ref _sourceCloses, bars.Count);
        _values[index] = point;
        _sourceSequences[index] = bar.Sequence;
        _sourceHighs[index] = bar.High;
        _sourceLows[index] = bar.Low;
        _sourceCloses[index] = bar.Close;
        _committedSequence = bar.Sequence;
        DataVersion = dataVersion;
        _appends++;
    }

    private SuperTrendValuePoint Step(ChartPrimaryBar bar)
    {
        float high = (float)bar.High;
        float low = (float)bar.Low;
        float close = (float)bar.Close;
        float trueRange = _hasPrevious
            ? Math.Max(
                high - low,
                Math.Max(
                    Math.Abs(high - _previousClose),
                    Math.Abs(low - _previousClose)))
            : high - low;

        _count++;
        if (_count <= _period)
        {
            _trSum += trueRange;
            if (_count == _period) _atr = (float)(_trSum / _period);
        }
        else
        {
            _atr = (_atr * (_period - 1) + trueRange) / _period;
        }

        if (_count >= _period)
        {
            float midpoint = (high + low) / 2f;
            float basicUpper = midpoint + _multiplier * _atr;
            float basicLower = midpoint - _multiplier * _atr;
            if (float.IsNaN(_upper) ||
                basicUpper < _upper ||
                _previousClose > _upper)
            {
                _upper = basicUpper;
            }
            if (float.IsNaN(_lower) ||
                basicLower > _lower ||
                _previousClose < _lower)
            {
                _lower = basicLower;
            }
            if (_direction == 1)
            {
                if (close < _lower) _direction = -1;
            }
            else if (close > _upper)
            {
                _direction = 1;
            }
            _superTrend = _direction == 1 ? _lower : _upper;
        }

        _previousClose = close;
        _hasPrevious = true;
        return new SuperTrendValuePoint(
            bar.Sequence,
            _superTrend,
            _direction == 1 ? _superTrend : float.NaN,
            _direction == -1 ? _superTrend : float.NaN,
            _direction,
            _atr);
    }

    private bool IsUnchanged(IReadOnlyList<ChartPrimaryBar> bars)
    {
        if (bars.Count != _sourceSequences.Length || bars.Count == 0) return false;
        for (int index = 0; index < bars.Count; index++)
        {
            if (!Matches(bars[index], index)) return false;
        }
        return true;
    }

    private bool CanUpdateLast(IReadOnlyList<ChartPrimaryBar> bars)
    {
        if (bars.Count != _sourceSequences.Length || bars.Count == 0) return false;
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
        bar.High == _sourceHighs[index] &&
        bar.Low == _sourceLows[index] &&
        bar.Close == _sourceCloses[index];

    private void SaveState()
    {
        _savedPreviousClose = _previousClose;
        _savedAtr = _atr;
        _savedUpper = _upper;
        _savedLower = _lower;
        _savedSuperTrend = _superTrend;
        _savedTrSum = _trSum;
        _savedCount = _count;
        _savedDirection = _direction;
        _savedHasPrevious = _hasPrevious;
    }

    private void RestoreState()
    {
        _previousClose = _savedPreviousClose;
        _atr = _savedAtr;
        _upper = _savedUpper;
        _lower = _savedLower;
        _superTrend = _savedSuperTrend;
        _trSum = _savedTrSum;
        _count = _savedCount;
        _direction = _savedDirection;
        _hasPrevious = _savedHasPrevious;
    }

    private void ResetState(bool clearDiagnostics)
    {
        ResetCalculationState();
        _sourceSequences = Array.Empty<long>();
        _sourceHighs = Array.Empty<double>();
        _sourceLows = Array.Empty<double>();
        _sourceCloses = Array.Empty<double>();
        _values = Array.Empty<SuperTrendValuePoint>();
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
        _atr = float.NaN;
        _upper = float.NaN;
        _lower = float.NaN;
        _superTrend = float.NaN;
        _trSum = 0d;
        _count = 0;
        _direction = 1;
        _hasPrevious = false;
        _savedPreviousClose = 0f;
        _savedAtr = float.NaN;
        _savedUpper = float.NaN;
        _savedLower = float.NaN;
        _savedSuperTrend = float.NaN;
        _savedTrSum = 0d;
        _savedCount = 0;
        _savedDirection = 1;
        _savedHasPrevious = false;
        _committedSequence = -1;
    }

    private static void ValidateParameters(int period, float multiplier)
    {
        if (period < 1 || period > 10_000)
            throw new ArgumentOutOfRangeException(nameof(period));
        if (!float.IsFinite(multiplier) || multiplier < 0.01f || multiplier > 1_000f)
            throw new ArgumentOutOfRangeException(nameof(multiplier));
    }
}
