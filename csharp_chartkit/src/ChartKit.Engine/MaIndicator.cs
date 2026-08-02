using ChartKit.CSharp.Contracts;

namespace ChartKit.CSharp.Engine;

public sealed class MaIndicator : IncrementalIndicatorBase
{
    private readonly int _period;
    private readonly string _type;
    private readonly float[] _window;
    private int _head;
    private int _count;
    private double _sum;
    private double _weightedSum;
    private double _ema;
    private int _savedHead;
    private int _savedCount;
    private double _savedSum;
    private double _savedWeightedSum;
    private double _savedEma;
    private float _savedHeadValue;

    public MaIndicator(int period = 20, string type = "SMA")
    {
        _period = Math.Max(1, period);
        _type = (type ?? "SMA").ToUpperInvariant();
        if (_type is not ("SMA" or "EMA" or "WMA")) _type = "SMA";
        _window = new float[_period];
        Descriptor = new($"{_type}_{_period}", $"{_type}({_period})", 0,
            ["Value"], [SeriesKind.Line]);
        Reset();
    }

    public override IndicatorDescriptor Descriptor { get; }

    protected override IndicatorPoint StepCandle(Candle candle, int index)
    {
        double value = candle.Close;
        float result = float.NaN;
        switch (_type)
        {
            case "EMA":
                if (_count < _period)
                {
                    _window[_count] = candle.Close;
                    _sum += value;
                    _count++;
                    if (_count == _period)
                    {
                        _ema = _sum / _period;
                        result = (float)_ema;
                    }
                }
                else
                {
                    double alpha = 2d / (_period + 1d);
                    _ema += alpha * (value - _ema);
                    result = (float)_ema;
                }
                break;
            case "WMA":
                if (_count < _period)
                {
                    _weightedSum += (_count + 1) * value;
                    _sum += value;
                    _window[(_head + _count) % _period] = candle.Close;
                    _count++;
                }
                else
                {
                    double oldest = _window[_head];
                    _weightedSum = _weightedSum - _sum + _period * value;
                    _sum += value - oldest;
                    _window[_head] = candle.Close;
                    _head = (_head + 1) % _period;
                }
                if (_count == _period)
                    result = (float)(_weightedSum / (_period * (_period + 1d) / 2d));
                break;
            default:
                Push(candle.Close);
                if (_count == _period) result = (float)(_sum / _period);
                break;
        }
        return new(candle.Sequence, result);
    }

    private void Push(float value)
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
    }

    protected override void SaveState()
    {
        _savedHead = _head;
        _savedCount = _count;
        _savedSum = _sum;
        _savedWeightedSum = _weightedSum;
        _savedEma = _ema;
        if (_count == _period) _savedHeadValue = _window[_head];
    }

    protected override void RestoreState()
    {
        _head = _savedHead;
        _count = _savedCount;
        _sum = _savedSum;
        _weightedSum = _savedWeightedSum;
        _ema = _savedEma;
        if (_count == _period) _window[_head] = _savedHeadValue;
    }

    protected override void ResetCore()
    {
        _head = 0;
        _count = 0;
        _sum = 0;
        _weightedSum = 0;
        _ema = 0;
        Array.Clear(_window);
    }
}
