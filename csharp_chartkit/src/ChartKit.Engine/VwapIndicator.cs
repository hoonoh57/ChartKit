using ChartKit.CSharp.Contracts;
namespace ChartKit.CSharp.Engine;

public sealed class VwapIndicator : IncrementalIndicatorBase
{
    private readonly float _stdDev1;
    private readonly float _stdDev2;
    private double _priceVolume, _volume, _priceSquaredVolume;
    private DateTime _lastDate = DateTime.MinValue;
    private double _savedPriceVolume, _savedVolume, _savedPriceSquaredVolume;
    private DateTime _savedLastDate;

    public VwapIndicator(float stdDev1 = 1f, float stdDev2 = 2f)
    {
        _stdDev1 = stdDev1;
        _stdDev2 = stdDev2;
        Descriptor = new("VWAP", "VWAP", 0,
            ["Value", "Upper1", "Lower1", "Upper2", "Lower2"],
            [SeriesKind.Line, SeriesKind.Line, SeriesKind.Line, SeriesKind.Line, SeriesKind.Line]);
        Reset();
    }

    public override IndicatorDescriptor Descriptor { get; }

    protected override IndicatorPoint StepCandle(Candle candle, int index)
    {
        if (_lastDate != DateTime.MinValue && candle.TradingDate != _lastDate.Date)
        {
            _priceVolume = 0;
            _volume = 0;
            _priceSquaredVolume = 0;
        }
        _lastDate = candle.OpenTime;
        double typicalPrice = (candle.High + candle.Low + candle.Close) / 3d;
        double volume = candle.Volume;
        _priceVolume += typicalPrice * volume;
        _volume += volume;
        _priceSquaredVolume += typicalPrice * typicalPrice * volume;
        if (_volume <= 0) return new(candle.Sequence, float.NaN, float.NaN, float.NaN, float.NaN, float.NaN);

        double vwap = _priceVolume / _volume;
        double variance = Math.Max(0d, _priceSquaredVolume / _volume - vwap * vwap);
        double deviation = Math.Sqrt(variance);
        return new(candle.Sequence,
            (float)vwap,
            (float)(vwap + _stdDev1 * deviation),
            (float)(vwap - _stdDev1 * deviation),
            (float)(vwap + _stdDev2 * deviation),
            (float)(vwap - _stdDev2 * deviation));
    }

    protected override void SaveState()
    {
        _savedPriceVolume = _priceVolume;
        _savedVolume = _volume;
        _savedPriceSquaredVolume = _priceSquaredVolume;
        _savedLastDate = _lastDate;
    }

    protected override void RestoreState()
    {
        _priceVolume = _savedPriceVolume;
        _volume = _savedVolume;
        _priceSquaredVolume = _savedPriceSquaredVolume;
        _lastDate = _savedLastDate;
    }

    protected override void ResetCore()
    {
        _priceVolume = _volume = _priceSquaredVolume = 0;
        _lastDate = DateTime.MinValue;
    }
}
