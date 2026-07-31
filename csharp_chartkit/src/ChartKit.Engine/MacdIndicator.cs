using ChartKit.CSharp.Contracts;
namespace ChartKit.CSharp.Engine;

public sealed class MacdIndicator : IncrementalIndicatorBase
{
    private readonly int _fast;
    private readonly int _slow;
    private readonly int _signal;
    private int _closeCount, _signalCount;
    private double _fastSeedSum, _slowSeedSum, _signalSeedSum;
    private float _fastEma = float.NaN, _slowEma = float.NaN, _signalEma = float.NaN;
    private int _savedCloseCount, _savedSignalCount;
    private double _savedFastSeedSum, _savedSlowSeedSum, _savedSignalSeedSum;
    private float _savedFastEma, _savedSlowEma, _savedSignalEma;

    public MacdIndicator(int fast = 12, int slow = 26, int signal = 9)
    {
        _fast = Math.Max(1, fast);
        _slow = Math.Max(_fast + 1, slow);
        _signal = Math.Max(1, signal);
        Descriptor = new($"MACD_{_fast}_{_slow}_{_signal}", $"MACD({_fast},{_slow},{_signal})", 7,
            ["MACD", "Signal", "Hist"],
            [SeriesKind.Line, SeriesKind.Line, SeriesKind.Histogram]);
        Reset();
    }

    public override IndicatorDescriptor Descriptor { get; }

    protected override IndicatorPoint StepCandle(Candle candle, int index)
    {
        _closeCount++;
        _fastEma = StepEma(candle.Close, _fast, _closeCount, ref _fastSeedSum, _fastEma);
        _slowEma = StepEma(candle.Close, _slow, _closeCount, ref _slowSeedSum, _slowEma);
        float macd = !float.IsNaN(_fastEma) && !float.IsNaN(_slowEma) ? _fastEma - _slowEma : float.NaN;
        float signalValue = float.NaN;
        if (!float.IsNaN(macd))
        {
            _signalCount++;
            if (_signalCount < _signal) _signalSeedSum += macd;
            else if (_signalCount == _signal)
            {
                _signalSeedSum += macd;
                _signalEma = (float)(_signalSeedSum / _signal);
                signalValue = _signalEma;
            }
            else
            {
                float factor = 2f / (_signal + 1);
                _signalEma = macd * factor + _signalEma * (1f - factor);
                signalValue = _signalEma;
            }
        }
        float histogram = float.IsNaN(macd) || float.IsNaN(signalValue) ? float.NaN : macd - signalValue;
        return new(candle.Sequence, macd, signalValue, histogram);
    }

    private static float StepEma(float close, int period, int count, ref double seedSum, float current)
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

    protected override void SaveState()
    {
        _savedCloseCount = _closeCount;
        _savedFastSeedSum = _fastSeedSum;
        _savedSlowSeedSum = _slowSeedSum;
        _savedFastEma = _fastEma;
        _savedSlowEma = _slowEma;
        _savedSignalCount = _signalCount;
        _savedSignalSeedSum = _signalSeedSum;
        _savedSignalEma = _signalEma;
    }

    protected override void RestoreState()
    {
        _closeCount = _savedCloseCount;
        _fastSeedSum = _savedFastSeedSum;
        _slowSeedSum = _savedSlowSeedSum;
        _fastEma = _savedFastEma;
        _slowEma = _savedSlowEma;
        _signalCount = _savedSignalCount;
        _signalSeedSum = _savedSignalSeedSum;
        _signalEma = _savedSignalEma;
    }

    protected override void ResetCore()
    {
        _closeCount = _signalCount = 0;
        _fastSeedSum = _slowSeedSum = _signalSeedSum = 0;
        _fastEma = _slowEma = _signalEma = float.NaN;
    }
}
