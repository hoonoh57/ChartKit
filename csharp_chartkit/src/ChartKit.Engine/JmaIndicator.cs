using ChartKit.CSharp.Contracts;

namespace ChartKit.CSharp.Engine;

public sealed class JmaIndicator : IncrementalIndicatorBase
{
    private readonly int _period;
    private readonly int _phase;
    private readonly int _power;
    private double _e0;
    private double _e1;
    private double _e2;
    private double _lastJma;
    private double _warmSum;
    private int _direction;
    private int _count;
    private bool _initialized;
    private double _savedE0;
    private double _savedE1;
    private double _savedE2;
    private double _savedLastJma;
    private double _savedWarmSum;
    private int _savedDirection;
    private int _savedCount;
    private bool _savedInitialized;

    public JmaIndicator(int period = 14, int phase = 50, int power = 2)
    {
        _period = Math.Max(1, period);
        _phase = Math.Clamp(phase, -100, 100);
        _power = Math.Max(1, power);
        Descriptor = new($"JMA_{_period}", $"JMA({_period},{_phase},{_power})", 0,
            ["Value", "Up", "Down", "Slope"],
            [SeriesKind.Line, SeriesKind.Line, SeriesKind.Line, SeriesKind.Meta]);
        Reset();
    }

    public override IndicatorDescriptor Descriptor { get; }

    protected override IndicatorPoint StepCandle(Candle candle, int index)
    {
        double source = candle.Close;
        if (!_initialized)
        {
            _e0 = source;
            _e1 = 0;
            _e2 = 0;
            _lastJma = source;
            _initialized = true;
        }

        double beta = 0.45d * (_period - 1) / (0.45d * (_period - 1) + 2d);
        double alpha = Math.Pow(beta, _power);
        _e0 = (1d - alpha) * source + alpha * _e0;
        _e1 = (source - _e0) * (1d - beta) + beta * _e1;
        _e2 = (_e0 + (_phase / 100d + 1.5d) * _e1 - _lastJma) *
              Math.Pow(1d - alpha, 2) + Math.Pow(alpha, 2) * _e2;

        _count++;
        _warmSum += source;
        double current = _count <= _period
            ? Math.Round(_warmSum / _count, 4)
            : Math.Round(_e2 + _lastJma, 4);
        double previous = _lastJma;
        if (current > previous) _direction = 1;
        else if (current < previous) _direction = -1;
        else if (_direction == 0) _direction = 1;
        float slope = previous != 0d
            ? (float)Math.Round((current / previous - 1d) * 100d, 1)
            : 0f;
        _lastJma = current;
        float value = (float)current;
        return new(candle.Sequence, value,
            _direction == 1 ? value : float.NaN,
            _direction == -1 ? value : float.NaN,
            slope);
    }

    protected override void SaveState()
    {
        _savedE0 = _e0;
        _savedE1 = _e1;
        _savedE2 = _e2;
        _savedLastJma = _lastJma;
        _savedWarmSum = _warmSum;
        _savedDirection = _direction;
        _savedCount = _count;
        _savedInitialized = _initialized;
    }

    protected override void RestoreState()
    {
        _e0 = _savedE0;
        _e1 = _savedE1;
        _e2 = _savedE2;
        _lastJma = _savedLastJma;
        _warmSum = _savedWarmSum;
        _direction = _savedDirection;
        _count = _savedCount;
        _initialized = _savedInitialized;
    }

    protected override void ResetCore()
    {
        _e0 = _e1 = _e2 = _lastJma = _warmSum = 0;
        _direction = _count = 0;
        _initialized = false;
    }
}
