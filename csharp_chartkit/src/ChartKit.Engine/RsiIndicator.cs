using ChartKit.CSharp.Contracts;
namespace ChartKit.CSharp.Engine;

public sealed class RsiIndicator : IncrementalIndicatorBase
{
    private readonly int _period;
    private readonly int _signalPeriod;
    private readonly float[] _signalValues;
    private float _previousClose, _upSum, _downSum, _averageUp, _averageDown;
    private int _diffCount, _signalHead, _signalCount;
    private double _signalSum;
    private float _savedPreviousClose, _savedUpSum, _savedDownSum, _savedAverageUp, _savedAverageDown, _savedSignalHeadValue;
    private int _savedDiffCount, _savedSignalHead, _savedSignalCount;
    private double _savedSignalSum;

    public RsiIndicator(int period = 14, int signalPeriod = 9)
    {
        _period = Math.Max(1, period);
        _signalPeriod = Math.Max(1, signalPeriod);
        _signalValues = new float[_signalPeriod];
        Descriptor = new($"RSI_{_period}", $"RSI({_period})", 1,
            ["RSI", "Signal", "Upper", "Lower"],
            [SeriesKind.Line, SeriesKind.Line, SeriesKind.Baseline, SeriesKind.Baseline]);
        Reset();
    }

    public override IndicatorDescriptor Descriptor { get; }

    protected override IndicatorPoint StepCandle(Candle candle, int index)
    {
        float rsi = float.NaN;
        if (index == 0) _previousClose = candle.Close;
        else
        {
            float difference = candle.Close - _previousClose;
            float up = Math.Max(difference, 0f);
            float down = Math.Max(-difference, 0f);
            _diffCount++;
            if (_diffCount <= _period)
            {
                _upSum += up;
                _downSum += down;
                if (_diffCount == _period)
                {
                    _averageUp = _upSum / _period;
                    _averageDown = _downSum / _period;
                    rsi = CalculateRsi(_averageUp, _averageDown);
                }
            }
            else
            {
                _averageUp = (_averageUp * (_period - 1) + up) / _period;
                _averageDown = (_averageDown * (_period - 1) + down) / _period;
                rsi = CalculateRsi(_averageUp, _averageDown);
            }
            _previousClose = candle.Close;
        }
        float signal = PushSignal(rsi);
        return new(candle.Sequence, rsi, signal, 70f, 30f);
    }

    private float PushSignal(float value)
    {
        if (float.IsNaN(value)) return float.NaN;
        if (_signalCount < _signalPeriod)
        {
            int slot = (_signalHead + _signalCount) % _signalPeriod;
            _signalValues[slot] = value;
            _signalCount++;
            _signalSum += value;
        }
        else
        {
            _signalSum -= _signalValues[_signalHead];
            _signalValues[_signalHead] = value;
            _signalSum += value;
            _signalHead = (_signalHead + 1) % _signalPeriod;
        }
        return _signalCount == _signalPeriod ? (float)(_signalSum / _signalPeriod) : float.NaN;
    }

    private static float CalculateRsi(float averageUp, float averageDown) =>
        averageDown == 0f ? 100f : 100f - 100f / (1f + averageUp / averageDown);

    protected override void SaveState()
    {
        _savedPreviousClose = _previousClose;
        _savedUpSum = _upSum;
        _savedDownSum = _downSum;
        _savedAverageUp = _averageUp;
        _savedAverageDown = _averageDown;
        _savedDiffCount = _diffCount;
        _savedSignalHead = _signalHead;
        _savedSignalCount = _signalCount;
        _savedSignalSum = _signalSum;
        if (_signalCount == _signalPeriod) _savedSignalHeadValue = _signalValues[_signalHead];
    }

    protected override void RestoreState()
    {
        _previousClose = _savedPreviousClose;
        _upSum = _savedUpSum;
        _downSum = _savedDownSum;
        _averageUp = _savedAverageUp;
        _averageDown = _savedAverageDown;
        _diffCount = _savedDiffCount;
        _signalHead = _savedSignalHead;
        _signalCount = _savedSignalCount;
        _signalSum = _savedSignalSum;
        if (_signalCount == _signalPeriod) _signalValues[_signalHead] = _savedSignalHeadValue;
    }

    protected override void ResetCore()
    {
        _previousClose = _upSum = _downSum = _averageUp = _averageDown = 0;
        _diffCount = _signalHead = _signalCount = 0;
        _signalSum = 0;
        Array.Clear(_signalValues);
    }
}
