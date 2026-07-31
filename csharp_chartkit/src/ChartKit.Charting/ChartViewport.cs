namespace ChartKit.CSharp.Charting;

public readonly record struct ChartWindow(int StartIndex, int Count)
{
    public static ChartWindow Empty { get; } = new(0, 0);
    public int EndExclusive => StartIndex + Count;
    public bool IsEmpty => Count <= 0;
}

public sealed class ChartViewport
{
    private readonly int _defaultVisibleBars;
    private readonly int _minimumVisibleBars;
    private readonly int _maximumVisibleBars;
    private int _visibleBars;
    private int _rightOffsetBars;

    public ChartViewport(
        int visibleBars = 160,
        int minimumVisibleBars = 20,
        int maximumVisibleBars = 5_000)
    {
        if (minimumVisibleBars <= 0)
            throw new ArgumentOutOfRangeException(nameof(minimumVisibleBars));
        if (maximumVisibleBars < minimumVisibleBars)
            throw new ArgumentOutOfRangeException(nameof(maximumVisibleBars));
        if (visibleBars < minimumVisibleBars || visibleBars > maximumVisibleBars)
            throw new ArgumentOutOfRangeException(nameof(visibleBars));

        _defaultVisibleBars = visibleBars;
        _minimumVisibleBars = minimumVisibleBars;
        _maximumVisibleBars = maximumVisibleBars;
        _visibleBars = visibleBars;
    }

    public int VisibleBars => _visibleBars;
    public int RightOffsetBars => _rightOffsetBars;
    public bool IsFollowingLatest => _rightOffsetBars == 0;

    public ChartWindow Resolve(int totalBars)
    {
        if (totalBars <= 0) return ChartWindow.Empty;

        int count = Math.Min(_visibleBars, totalBars);
        int maximumOffset = Math.Max(0, totalBars - count);
        int offset = Math.Clamp(_rightOffsetBars, 0, maximumOffset);
        return new ChartWindow(totalBars - count - offset, count);
    }

    public ChartWindow Pan(int deltaBars, int totalBars)
    {
        if (totalBars <= 0)
        {
            _rightOffsetBars = 0;
            return ChartWindow.Empty;
        }

        int count = Math.Min(_visibleBars, totalBars);
        int maximumOffset = Math.Max(0, totalBars - count);
        _rightOffsetBars = Math.Clamp(
            _rightOffsetBars + deltaBars,
            0,
            maximumOffset);
        return Resolve(totalBars);
    }

    public ChartWindow Zoom(
        int wheelDelta,
        int totalBars,
        float anchorFraction = 0.5f)
    {
        if (wheelDelta == 0 || totalBars <= 0) return Resolve(totalBars);

        anchorFraction = Math.Clamp(anchorFraction, 0f, 1f);
        ChartWindow previous = Resolve(totalBars);
        int notches = Math.Max(1, Math.Abs(wheelDelta) / 120);
        double factor = wheelDelta > 0 ? 0.84d : 1.19d;
        double target = _visibleBars;
        for (int index = 0; index < notches; index++) target *= factor;

        int nextVisible = Math.Clamp(
            (int)Math.Round(target, MidpointRounding.AwayFromZero),
            _minimumVisibleBars,
            _maximumVisibleBars);
        nextVisible = Math.Min(nextVisible, totalBars);

        double anchorIndex = previous.IsEmpty
            ? totalBars - 1d
            : previous.StartIndex + (previous.Count - 1d) * anchorFraction;
        int nextStart = (int)Math.Round(
            anchorIndex - (nextVisible - 1d) * anchorFraction,
            MidpointRounding.AwayFromZero);
        nextStart = Math.Clamp(nextStart, 0, Math.Max(0, totalBars - nextVisible));

        _visibleBars = nextVisible;
        _rightOffsetBars = Math.Max(0, totalBars - (nextStart + nextVisible));
        return Resolve(totalBars);
    }

    public ChartWindow FollowLatest(int totalBars)
    {
        _rightOffsetBars = 0;
        return Resolve(totalBars);
    }

    public ChartWindow Reset(int totalBars)
    {
        _visibleBars = _defaultVisibleBars;
        _rightOffsetBars = 0;
        return Resolve(totalBars);
    }
}
