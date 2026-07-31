using ChartKit.CSharp.Contracts;
namespace ChartKit.CSharp.Engine;

public sealed class SuperTrendIndicator : IncrementalIndicatorBase
{
    private readonly int _period;
    private readonly float _multiplier;
    private float _previousClose;
    private float _atr = float.NaN, _upper = float.NaN, _lower = float.NaN, _superTrend = float.NaN;
    private double _trSum;
    private int _count, _direction = 1;
    private bool _hasPrevious;
    private float _savedPreviousClose, _savedAtr, _savedUpper, _savedLower, _savedSuperTrend;
    private double _savedTrSum;
    private int _savedCount, _savedDirection;
    private bool _savedHasPrevious;

    public SuperTrendIndicator(int atrPeriod = 10, float multiplier = 3f)
    {
        _period = Math.Max(1, atrPeriod);
        _multiplier = Math.Max(0.01f, multiplier);
        Descriptor = new($"ST_{_period}_{_multiplier:F1}", $"SuperTrend({_period},{_multiplier:F1})", 0,
            ["Value", "Up", "Down", "Direction", "ATR"],
            [SeriesKind.Line, SeriesKind.Line, SeriesKind.Line, SeriesKind.Meta, SeriesKind.Meta]);
        Reset();
    }

    public override IndicatorDescriptor Descriptor { get; }

    protected override IndicatorPoint StepCandle(Candle candle, int index)
    {
        float trueRange = _hasPrevious
            ? Math.Max(candle.High - candle.Low,
                Math.Max(Math.Abs(candle.High - _previousClose), Math.Abs(candle.Low - _previousClose)))
            : candle.High - candle.Low;
        _count++;
        if (_count <= _period)
        {
            _trSum += trueRange;
            if (_count == _period) _atr = (float)(_trSum / _period);
        }
        else _atr = (_atr * (_period - 1) + trueRange) / _period;

        if (_count >= _period)
        {
            float midpoint = (candle.High + candle.Low) / 2f;
            float basicUpper = midpoint + _multiplier * _atr;
            float basicLower = midpoint - _multiplier * _atr;
            if (float.IsNaN(_upper) || basicUpper < _upper || _previousClose > _upper) _upper = basicUpper;
            if (float.IsNaN(_lower) || basicLower > _lower || _previousClose < _lower) _lower = basicLower;
            if (_direction == 1)
            {
                if (candle.Close < _lower) _direction = -1;
            }
            else if (candle.Close > _upper) _direction = 1;
            _superTrend = _direction == 1 ? _lower : _upper;
        }
        _previousClose = candle.Close;
        _hasPrevious = true;
        return new(candle.Sequence, _superTrend,
            _direction == 1 ? _superTrend : float.NaN,
            _direction == -1 ? _superTrend : float.NaN,
            _direction,
            _atr);
    }

    protected override void SaveState()
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

    protected override void RestoreState()
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

    protected override void ResetCore()
    {
        _previousClose = 0;
        _atr = _upper = _lower = _superTrend = float.NaN;
        _trSum = 0;
        _count = 0;
        _direction = 1;
        _hasPrevious = false;
    }
}
