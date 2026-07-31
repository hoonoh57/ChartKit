namespace ChartKit.CSharp.Charting;

public readonly record struct ChartRectF(float Left, float Top, float Right, float Bottom)
{
    public static ChartRectF Empty { get; } = new(0f, 0f, 0f, 0f);
    public float Width => Math.Max(0f, Right - Left);
    public float Height => Math.Max(0f, Bottom - Top);
    public float MidX => Left + Width * 0.5f;
    public float MidY => Top + Height * 0.5f;
    public bool IsEmpty => Width <= 0f || Height <= 0f;
}

public readonly record struct NumericRange(float Minimum, float Maximum)
{
    public float Span => Maximum - Minimum;
    public bool IsValid => float.IsFinite(Minimum) &&
                           float.IsFinite(Maximum) &&
                           Maximum > Minimum;
}

public readonly record struct NumericAxisTick(float Value, float Position);

public readonly record struct TimeAxisTick(
    DateTime Time,
    int CandleIndex,
    float Position,
    bool IsDateBoundary);

public readonly record struct ChartViewTransform(float PricePanFraction)
{
    public static ChartViewTransform Default { get; } = new(0f);
}

public sealed record ChartLayoutOptions(
    float MainPanelRatio = 0.56f,
    float VolumePanelRatio = 0.10f,
    float LeftPadding = 10f,
    float RightPadding = 82f,
    float TopPadding = 24f,
    float BottomPadding = 28f,
    float PriceTopMarginRatio = 0.14f,
    float PriceBottomMarginRatio = 0.06f,
    int MinimumPriceMarginTicks = 4,
    int TargetPriceTickCount = 6,
    int TargetTimeTickCount = 8)
{
    public static ChartLayoutOptions Default { get; } = new();

    public void Validate()
    {
        if (MainPanelRatio <= 0f || MainPanelRatio >= 1f)
            throw new ArgumentOutOfRangeException(nameof(MainPanelRatio));
        if (VolumePanelRatio < 0f || MainPanelRatio + VolumePanelRatio >= 1f)
            throw new ArgumentOutOfRangeException(nameof(VolumePanelRatio));
        if (LeftPadding < 0f || RightPadding < 0f || TopPadding < 0f || BottomPadding < 0f)
            throw new ArgumentOutOfRangeException(nameof(LeftPadding));
        if (PriceTopMarginRatio < 0f || PriceBottomMarginRatio < 0f)
            throw new ArgumentOutOfRangeException(nameof(PriceTopMarginRatio));
        if (MinimumPriceMarginTicks < 1 || MinimumPriceMarginTicks > 100)
            throw new ArgumentOutOfRangeException(nameof(MinimumPriceMarginTicks));
        if (TargetPriceTickCount < 2 || TargetPriceTickCount > ChartFrame.MaximumAxisTickCount)
            throw new ArgumentOutOfRangeException(nameof(TargetPriceTickCount));
        if (TargetTimeTickCount < 2 || TargetTimeTickCount > ChartFrame.MaximumAxisTickCount)
            throw new ArgumentOutOfRangeException(nameof(TargetTimeTickCount));
    }
}

public sealed class ChartFrame
{
    public const int MaximumPanelIndex = 7;
    public const int MaximumAxisTickCount = 16;

    public ChartWindow Window { get; internal set; } = ChartWindow.Empty;
    public ChartRectF Bounds { get; internal set; } = ChartRectF.Empty;
    public ChartRectF MainPanel { get; internal set; } = ChartRectF.Empty;
    public ChartRectF VolumePanel { get; internal set; } = ChartRectF.Empty;
    public ChartRectF TimeAxis { get; internal set; } = ChartRectF.Empty;
    public NumericRange PriceRange { get; internal set; } = new(0f, 1f);
    public IPriceGrid PriceGrid { get; internal set; } = KoreanEquityPriceGrid.Instance;
    public long VolumeMaximum { get; internal set; } = 1L;
    public float BarStep { get; internal set; } = 1f;
    public float BodyWidth { get; internal set; } = 1f;
    public int PriceTickCount { get; internal set; }
    public int TimeTickCount { get; internal set; }

    public ChartRectF[] PanelRects { get; } = new ChartRectF[MaximumPanelIndex + 1];
    public bool[] PanelVisible { get; } = new bool[MaximumPanelIndex + 1];
    public NumericRange[] PanelRanges { get; } = new NumericRange[MaximumPanelIndex + 1];
    public NumericAxisTick[] PriceTicks { get; } = new NumericAxisTick[MaximumAxisTickCount];
    public TimeAxisTick[] TimeTicks { get; } = new TimeAxisTick[MaximumAxisTickCount];

    public float X(int visibleIndex) =>
        MainPanel.Left + (visibleIndex + 0.5f) * BarStep;

    public float PriceY(float value) =>
        MapY(value, PriceRange, MainPanel);

    public float PanelY(int panelIndex, float value)
    {
        if (panelIndex <= 0 || panelIndex > MaximumPanelIndex)
            throw new ArgumentOutOfRangeException(nameof(panelIndex));
        return MapY(value, PanelRanges[panelIndex], PanelRects[panelIndex]);
    }

    public static float MapY(float value, NumericRange range, ChartRectF rect)
    {
        if (!float.IsFinite(value) || !range.IsValid || rect.IsEmpty) return rect.MidY;
        float ratio = (value - range.Minimum) / range.Span;
        return rect.Bottom - ratio * rect.Height;
    }
}
