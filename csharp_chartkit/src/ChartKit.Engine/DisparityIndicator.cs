using ChartKit.CSharp.Contracts;
namespace ChartKit.CSharp.Engine;

public sealed class DisparityIndicator : IncrementalIndicatorBase
{
    private readonly int _period;
    private readonly float[] _window;
    private int _head, _count;
    private double _sum;
    private int _savedHead, _savedCount;
    private double _savedSum;
    private float _savedHeadValue;

    public DisparityIndicator(int period = 20)
    {
        _period = Math.Max(1, period);
        _window = new float[_period];
        Descriptor = new($"DISP_{_period}", $"Disparity({_period})", 6,
            ["Value", "MA", "Upper", "Baseline", "Lower"],
            [SeriesKind.Line, SeriesKind.Meta, SeriesKind.Baseline, SeriesKind.Baseline, SeriesKind.Baseline]);
        Reset();
    }

    public override IndicatorDescriptor Descriptor { get; }

    protected override IndicatorPoint StepCandle(Candle candle, int index)
    {
        if (_count < _period)
        {
            _window[(_head + _count) % _period] = candle.Close;
            _count++;
            _sum += candle.Close;
        }
        else
        {
            _sum += candle.Close - _window[_head];
            _window[_head] = candle.Close;
            _head = (_head + 1) % _period;
        }
        float ma = _count == _period ? (float)(_sum / _period) : float.NaN;
        float value = float.IsNaN(ma) ? float.NaN : ma > 0f ? candle.Close / ma * 100f : 100f;
        return new(candle.Sequence, value, ma, 105f, 100f, 95f);
    }

    protected override void SaveState()
    {
        _savedHead = _head;
        _savedCount = _count;
        _savedSum = _sum;
        if (_count == _period) _savedHeadValue = _window[_head];
    }

    protected override void RestoreState()
    {
        _head = _savedHead;
        _count = _savedCount;
        _sum = _savedSum;
        if (_count == _period) _window[_head] = _savedHeadValue;
    }

    protected override void ResetCore()
    {
        _head = _count = 0;
        _sum = 0;
        Array.Clear(_window);
    }
}
