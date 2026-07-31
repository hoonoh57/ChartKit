using ChartKit.CSharp.Contracts;
namespace ChartKit.CSharp.Engine;

public sealed class ObvIndicator : IncrementalIndicatorBase
{
    private readonly int _period;
    private readonly double[] _window;
    private float _previousClose;
    private bool _hasPrevious;
    private double _obv, _sum;
    private int _head, _count;
    private float _savedPreviousClose;
    private bool _savedHasPrevious;
    private double _savedObv, _savedSum, _savedHeadValue;
    private int _savedHead, _savedCount;

    public ObvIndicator(int maPeriod = 20)
    {
        _period = Math.Max(1, maPeriod);
        _window = new double[_period];
        Descriptor = new($"OBV_{_period}", $"OBV(MA{_period})", 5,
            ["OBV", "Signal", "Direction"],
            [SeriesKind.Line, SeriesKind.Line, SeriesKind.Meta]);
        Reset();
    }

    public override IndicatorDescriptor Descriptor { get; }

    protected override IndicatorPoint StepCandle(Candle candle, int index)
    {
        if (!_hasPrevious)
        {
            _obv = candle.Volume;
            _hasPrevious = true;
        }
        else if (candle.Close > _previousClose) _obv += candle.Volume;
        else if (candle.Close < _previousClose) _obv -= candle.Volume;
        _previousClose = candle.Close;
        Push(_obv);
        float signal = _count == _period ? (float)(_sum / _period) : float.NaN;
        float value = (float)_obv;
        float direction = float.IsNaN(signal) ? float.NaN : value > signal ? 1f : -1f;
        return new(candle.Sequence, value, signal, direction);
    }

    private void Push(double value)
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
        _savedPreviousClose = _previousClose;
        _savedHasPrevious = _hasPrevious;
        _savedObv = _obv;
        _savedHead = _head;
        _savedCount = _count;
        _savedSum = _sum;
        if (_count == _period) _savedHeadValue = _window[_head];
    }

    protected override void RestoreState()
    {
        _previousClose = _savedPreviousClose;
        _hasPrevious = _savedHasPrevious;
        _obv = _savedObv;
        _head = _savedHead;
        _count = _savedCount;
        _sum = _savedSum;
        if (_count == _period) _window[_head] = _savedHeadValue;
    }

    protected override void ResetCore()
    {
        _previousClose = 0;
        _hasPrevious = false;
        _obv = _sum = 0;
        _head = _count = 0;
        Array.Clear(_window);
    }
}
