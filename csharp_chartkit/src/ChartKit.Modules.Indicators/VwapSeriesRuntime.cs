using ChartKit.CSharp.Modules.Abstractions;

namespace ChartKit.CSharp.Modules.Indicators;

public readonly record struct VwapValuePoint(
    long Sequence,
    float Value,
    float Upper1,
    float Lower1,
    float Upper2,
    float Lower2);

public readonly record struct VwapRuntimeDiagnostics(
    long FullCalculations,
    long LastUpdates,
    long Appends,
    long UnchangedSnapshots,
    long SessionResets);

internal sealed class VwapSeriesRuntime
{
    private double _stdDev1;
    private double _stdDev2;
    private double _priceVolume;
    private double _volume;
    private double _priceSquaredVolume;
    private DateOnly _lastTradingDate = DateOnly.MinValue;

    private double _savedPriceVolume;
    private double _savedVolume;
    private double _savedPriceSquaredVolume;
    private DateOnly _savedLastTradingDate = DateOnly.MinValue;

    private long _committedSequence = -1;
    private long[] _sourceSequences = Array.Empty<long>();
    private double[] _sourceHighs = Array.Empty<double>();
    private double[] _sourceLows = Array.Empty<double>();
    private double[] _sourceCloses = Array.Empty<double>();
    private long[] _sourceVolumes = Array.Empty<long>();
    private DateOnly[] _sourceTradingDates = Array.Empty<DateOnly>();
    private VwapValuePoint[] _values = Array.Empty<VwapValuePoint>();
    private long _fullCalculations;
    private long _lastUpdates;
    private long _appends;
    private long _unchangedSnapshots;
    private long _sessionResets;

    public VwapSeriesRuntime(double stdDev1, double stdDev2)
    {
        _stdDev1 = ValidateStdDev(stdDev1, nameof(stdDev1));
        _stdDev2 = ValidateStdDev(stdDev2, nameof(stdDev2));
    }

    public double StdDev1 => _stdDev1;
    public double StdDev2 => _stdDev2;
    public long DataVersion { get; private set; } = -1;
    public IReadOnlyList<VwapValuePoint> Values => _values;
    public VwapRuntimeDiagnostics Diagnostics => new(
        _fullCalculations,
        _lastUpdates,
        _appends,
        _unchangedSnapshots,
        _sessionResets);

    public bool SetParameters(double stdDev1, double stdDev2)
    {
        double normalized1 = ValidateStdDev(stdDev1, nameof(stdDev1));
        double normalized2 = ValidateStdDev(stdDev2, nameof(stdDev2));
        if (normalized1 == _stdDev1 && normalized2 == _stdDev2)
            return false;

        _stdDev1 = normalized1;
        _stdDev2 = normalized2;
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

        ValidateTradingDates(bars);

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

    public VwapValuePoint[] SnapshotValues() =>
        (VwapValuePoint[])_values.Clone();

    public void Reset() => ResetState(clearDiagnostics: true);

    private void Recalculate(
        IReadOnlyList<ChartPrimaryBar> bars,
        long dataVersion)
    {
        ResetCalculationState();
        var values = new VwapValuePoint[bars.Count];
        var sequences = new long[bars.Count];
        var highs = new double[bars.Count];
        var lows = new double[bars.Count];
        var closes = new double[bars.Count];
        var volumes = new long[bars.Count];
        var tradingDates = new DateOnly[bars.Count];

        for (int index = 0; index < bars.Count; index++)
        {
            ChartPrimaryBar bar = bars[index];
            if (index == bars.Count - 1) SaveState();
            values[index] = Step(bar);
            sequences[index] = bar.Sequence;
            highs[index] = bar.High;
            lows[index] = bar.Low;
            closes[index] = bar.Close;
            volumes[index] = bar.Volume;
            tradingDates[index] = bar.TradingDate;
        }

        _values = values;
        _sourceSequences = sequences;
        _sourceHighs = highs;
        _sourceLows = lows;
        _sourceCloses = closes;
        _sourceVolumes = volumes;
        _sourceTradingDates = tradingDates;
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
        _sourceHighs[index] = bar.High;
        _sourceLows[index] = bar.Low;
        _sourceCloses[index] = bar.Close;
        _sourceVolumes[index] = bar.Volume;
        _sourceTradingDates[index] = bar.TradingDate;
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
        VwapValuePoint point = Step(bar);

        Array.Resize(ref _values, bars.Count);
        Array.Resize(ref _sourceSequences, bars.Count);
        Array.Resize(ref _sourceHighs, bars.Count);
        Array.Resize(ref _sourceLows, bars.Count);
        Array.Resize(ref _sourceCloses, bars.Count);
        Array.Resize(ref _sourceVolumes, bars.Count);
        Array.Resize(ref _sourceTradingDates, bars.Count);
        _values[index] = point;
        _sourceSequences[index] = bar.Sequence;
        _sourceHighs[index] = bar.High;
        _sourceLows[index] = bar.Low;
        _sourceCloses[index] = bar.Close;
        _sourceVolumes[index] = bar.Volume;
        _sourceTradingDates[index] = bar.TradingDate;
        _committedSequence = bar.Sequence;
        DataVersion = dataVersion;
        _appends++;
    }

    private VwapValuePoint Step(ChartPrimaryBar bar)
    {
        if (_lastTradingDate != DateOnly.MinValue &&
            bar.TradingDate != _lastTradingDate)
        {
            _priceVolume = 0d;
            _volume = 0d;
            _priceSquaredVolume = 0d;
            _sessionResets++;
        }

        _lastTradingDate = bar.TradingDate;
        double typicalPrice = (bar.High + bar.Low + bar.Close) / 3d;
        double volume = bar.Volume;
        _priceVolume += typicalPrice * volume;
        _volume += volume;
        _priceSquaredVolume += typicalPrice * typicalPrice * volume;

        if (_volume <= 0d)
        {
            return new VwapValuePoint(
                bar.Sequence,
                float.NaN,
                float.NaN,
                float.NaN,
                float.NaN,
                float.NaN);
        }

        double vwap = _priceVolume / _volume;
        double variance = Math.Max(
            0d,
            _priceSquaredVolume / _volume - vwap * vwap);
        double deviation = Math.Sqrt(variance);
        return new VwapValuePoint(
            bar.Sequence,
            (float)vwap,
            (float)(vwap + _stdDev1 * deviation),
            (float)(vwap - _stdDev1 * deviation),
            (float)(vwap + _stdDev2 * deviation),
            (float)(vwap - _stdDev2 * deviation));
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
        bar.High == _sourceHighs[index] &&
        bar.Low == _sourceLows[index] &&
        bar.Close == _sourceCloses[index] &&
        bar.Volume == _sourceVolumes[index] &&
        bar.TradingDate == _sourceTradingDates[index];

    private void SaveState()
    {
        _savedPriceVolume = _priceVolume;
        _savedVolume = _volume;
        _savedPriceSquaredVolume = _priceSquaredVolume;
        _savedLastTradingDate = _lastTradingDate;
    }

    private void RestoreState()
    {
        _priceVolume = _savedPriceVolume;
        _volume = _savedVolume;
        _priceSquaredVolume = _savedPriceSquaredVolume;
        _lastTradingDate = _savedLastTradingDate;
    }

    private void ResetState(bool clearDiagnostics)
    {
        ResetCalculationState();
        _sourceSequences = Array.Empty<long>();
        _sourceHighs = Array.Empty<double>();
        _sourceLows = Array.Empty<double>();
        _sourceCloses = Array.Empty<double>();
        _sourceVolumes = Array.Empty<long>();
        _sourceTradingDates = Array.Empty<DateOnly>();
        _values = Array.Empty<VwapValuePoint>();
        DataVersion = -1;
        if (clearDiagnostics)
        {
            _fullCalculations = 0;
            _lastUpdates = 0;
            _appends = 0;
            _unchangedSnapshots = 0;
            _sessionResets = 0;
        }
    }

    private void ResetCalculationState()
    {
        _priceVolume = 0d;
        _volume = 0d;
        _priceSquaredVolume = 0d;
        _lastTradingDate = DateOnly.MinValue;
        _savedPriceVolume = 0d;
        _savedVolume = 0d;
        _savedPriceSquaredVolume = 0d;
        _savedLastTradingDate = DateOnly.MinValue;
        _committedSequence = -1;
    }

    private static void ValidateTradingDates(
        IReadOnlyList<ChartPrimaryBar> bars)
    {
        for (int index = 0; index < bars.Count; index++)
        {
            if (!bars[index].HasTradingDate)
            {
                throw new InvalidOperationException(
                    $"VWAP requires TradingDate for primary bar index {index}.");
            }
        }
    }

    private static double ValidateStdDev(double value, string parameterName)
    {
        if (!double.IsFinite(value) || value < 0d || value > 100d)
            throw new ArgumentOutOfRangeException(parameterName);
        return value;
    }
}
