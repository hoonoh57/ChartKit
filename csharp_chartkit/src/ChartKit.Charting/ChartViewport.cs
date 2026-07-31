namespace ChartKit.CSharp.Charting;

public readonly record struct ChartWindow(
    int StartIndex,
    int Count,
    int RightBlankBars = 0)
{
    public static ChartWindow Empty { get; } = new(0, 0, 0);
    public int EndExclusive => StartIndex + Count;
    public int VisibleSlotCount => Count + Math.Max(0, RightBlankBars);
    public bool IsEmpty => Count <= 0;
}

public sealed class ChartViewport
{
    private readonly int _defaultVisibleBars;
    private readonly int _minimumVisibleBars;
    private readonly int _maximumVisibleBars;
    private readonly int _defaultRightBlankBars;
    private readonly int _maximumRightBlankBars;
    private int _visibleBars;
    private int _horizontalShiftBars;
    private int _currentRightOffsetBars;
    private int _currentRightBlankBars;
    private float _pricePanFraction;

    public ChartViewport(
        int visibleBars = 160,
        int minimumVisibleBars = 20,
        int maximumVisibleBars = 5_000,
        int rightBlankBars = 12,
        int maximumRightBlankBars = 240)
    {
        if (minimumVisibleBars <= 0)
            throw new ArgumentOutOfRangeException(nameof(minimumVisibleBars));
        if (maximumVisibleBars < minimumVisibleBars)
            throw new ArgumentOutOfRangeException(nameof(maximumVisibleBars));
        if (visibleBars < minimumVisibleBars || visibleBars > maximumVisibleBars)
            throw new ArgumentOutOfRangeException(nameof(visibleBars));
        if (rightBlankBars < 0)
            throw new ArgumentOutOfRangeException(nameof(rightBlankBars));
        if (maximumRightBlankBars < rightBlankBars)
            throw new ArgumentOutOfRangeException(nameof(maximumRightBlankBars));

        _defaultVisibleBars = visibleBars;
        _minimumVisibleBars = minimumVisibleBars;
        _maximumVisibleBars = maximumVisibleBars;
        _defaultRightBlankBars = rightBlankBars;
        _maximumRightBlankBars = maximumRightBlankBars;
        _visibleBars = visibleBars;
        _currentRightBlankBars = rightBlankBars;
    }

    public int VisibleBars => _visibleBars;
    public int RightOffsetBars => _currentRightOffsetBars;
    public int RightBlankBars => _currentRightBlankBars;
    public float PricePanFraction => _pricePanFraction;
    public ChartViewTransform Transform => new(_pricePanFraction);
    public bool IsFollowingLatest => _currentRightOffsetBars == 0;

    public ChartWindow Resolve(int totalBars)
    {
        if (totalBars <= 0)
        {
            _currentRightOffsetBars = 0;
            _currentRightBlankBars = _defaultRightBlankBars;
            return ChartWindow.Empty;
        }

        int baseCount = Math.Min(_visibleBars, totalBars);
        int totalSlots = Math.Max(1, baseCount + _defaultRightBlankBars);
        int maximumBlank = Math.Min(
            _maximumRightBlankBars,
            Math.Max(0, totalSlots - 1));
        int blank = Math.Clamp(
            _defaultRightBlankBars - _horizontalShiftBars,
            0,
            maximumBlank);
        int offset = Math.Max(
            0,
            _horizontalShiftBars - _defaultRightBlankBars);
        offset = Math.Min(offset, Math.Max(0, totalBars - 1));

        int capacity = Math.Max(1, totalSlots - blank);
        int available = Math.Max(1, totalBars - offset);
        int count = Math.Min(capacity, available);
        int start = Math.Max(0, totalBars - offset - count);

        _currentRightOffsetBars = offset;
        _currentRightBlankBars = blank;
        return new ChartWindow(start, count, blank);
    }

    public ChartWindow Pan(int deltaBars, int totalBars)
    {
        if (totalBars <= 0)
        {
            _horizontalShiftBars = 0;
            return Resolve(totalBars);
        }

        int totalSlots = Math.Max(
            1,
            Math.Min(_visibleBars, totalBars) + _defaultRightBlankBars);
        int maximumBlank = Math.Min(
            _maximumRightBlankBars,
            Math.Max(0, totalSlots - 1));
        int minimumShift = _defaultRightBlankBars - maximumBlank;
        int maximumShift = _defaultRightBlankBars + Math.Max(0, totalBars - 1);
        long candidate = (long)_horizontalShiftBars + deltaBars;
        _horizontalShiftBars = (int)Math.Clamp(
            candidate,
            minimumShift,
            maximumShift);
        return Resolve(totalBars);
    }

    public void PanPricePixels(float deltaPixels, float panelHeight)
    {
        if (!float.IsFinite(deltaPixels) || panelHeight <= 0f) return;
        _pricePanFraction = Math.Clamp(
            _pricePanFraction + deltaPixels / panelHeight,
            -5f,
            5f);
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
        double targetSlots = Math.Max(1, previous.VisibleSlotCount);
        for (int index = 0; index < notches; index++) targetSlots *= factor;

        int nextVisible = Math.Clamp(
            (int)Math.Round(
                targetSlots - _defaultRightBlankBars,
                MidpointRounding.AwayFromZero),
            _minimumVisibleBars,
            _maximumVisibleBars);
        nextVisible = Math.Min(nextVisible, totalBars);
        int nextTotalSlots = Math.Max(1, nextVisible + _defaultRightBlankBars);

        double previousAnchorSlot =
            anchorFraction * Math.Max(0, previous.VisibleSlotCount - 1d);
        double previousVisibleIndex = Math.Min(
            Math.Max(0d, previousAnchorSlot),
            Math.Max(0d, previous.Count - 1d));
        double anchorIndex = previous.StartIndex + previousVisibleIndex;
        double nextAnchorSlot =
            anchorFraction * Math.Max(0, nextTotalSlots - 1d);
        int nextStart = (int)Math.Round(
            anchorIndex - nextAnchorSlot,
            MidpointRounding.AwayFromZero);
        nextStart = Math.Clamp(nextStart, 0, Math.Max(0, totalBars - 1));

        _visibleBars = nextVisible;
        int nextEndBySlots = nextStart + nextTotalSlots;
        if (nextEndBySlots > totalBars)
        {
            int desiredBlank = Math.Min(
                _maximumRightBlankBars,
                nextEndBySlots - totalBars);
            _horizontalShiftBars = _defaultRightBlankBars - desiredBlank;
        }
        else
        {
            int desiredOffset = totalBars - nextEndBySlots;
            _horizontalShiftBars = _defaultRightBlankBars + desiredOffset;
        }
        return Resolve(totalBars);
    }

    public ChartWindow SetVisibleBars(
        int visibleBars,
        int totalBars,
        bool followLatest = false)
    {
        bool wasFollowingLatest = IsFollowingLatest;
        _visibleBars = Math.Clamp(
            visibleBars,
            _minimumVisibleBars,
            _maximumVisibleBars);
        if (followLatest || wasFollowingLatest)
            _horizontalShiftBars = 0;
        return Resolve(totalBars);
    }

    public ChartWindow FollowLatest(int totalBars)
    {
        _horizontalShiftBars = 0;
        return Resolve(totalBars);
    }

    public ChartWindow Reset(int totalBars)
    {
        _visibleBars = _defaultVisibleBars;
        _horizontalShiftBars = 0;
        _pricePanFraction = 0f;
        return Resolve(totalBars);
    }
}
