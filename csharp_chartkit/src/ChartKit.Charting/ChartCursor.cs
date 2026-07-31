using ChartKit.CSharp.Contracts;

namespace ChartKit.CSharp.Charting;

public readonly record struct ChartCursorSnapshot(
    bool IsVisible,
    int CandleIndex,
    int VisibleIndex,
    int PanelIndex,
    ChartRectF ActivePanel,
    float X,
    float Y,
    float AxisValue,
    Candle Candle)
{
    public const int VolumePanelIndex = -1;

    public static ChartCursorSnapshot Hidden { get; } = new(
        false,
        -1,
        -1,
        0,
        ChartRectF.Empty,
        0f,
        0f,
        float.NaN,
        default);
}

public sealed class ChartCursorController
{
    public ChartCursorSnapshot Current { get; private set; } = ChartCursorSnapshot.Hidden;

    public ChartCursorSnapshot Update(
        float pointerX,
        float pointerY,
        SymbolSnapshot snapshot,
        ChartFrame frame)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(frame);
        if (frame.Window.IsEmpty || frame.MainPanel.IsEmpty ||
            snapshot.Candles.Length == 0 ||
            pointerX < frame.MainPanel.Left || pointerX > frame.MainPanel.Right ||
            !TryResolvePanel(pointerY, frame, out int panelIndex, out ChartRectF panel))
        {
            Current = ChartCursorSnapshot.Hidden;
            return Current;
        }

        int visibleIndex = Math.Clamp(
            (int)Math.Floor((pointerX - frame.MainPanel.Left) /
                            Math.Max(0.0001f, frame.BarStep)),
            0,
            frame.Window.Count - 1);
        int candleIndex = frame.Window.StartIndex + visibleIndex;
        Candle candle = snapshot.Candles[candleIndex];
        float y = Math.Clamp(pointerY, panel.Top, panel.Bottom);
        float value = ResolveAxisValue(y, panelIndex, panel, frame);

        if (panelIndex == 0)
        {
            value = frame.PriceGrid.Snap(value, PriceSnapMode.Nearest);
            value = Math.Clamp(value, frame.PriceRange.Minimum, frame.PriceRange.Maximum);
            y = frame.PriceY(value);
        }
        else if (panelIndex == ChartCursorSnapshot.VolumePanelIndex)
        {
            value = MathF.Round(Math.Max(0f, value));
        }

        Current = new ChartCursorSnapshot(
            true,
            candleIndex,
            visibleIndex,
            panelIndex,
            panel,
            frame.X(visibleIndex),
            y,
            value,
            candle);
        return Current;
    }

    public void Clear() => Current = ChartCursorSnapshot.Hidden;

    private static bool TryResolvePanel(
        float pointerY,
        ChartFrame frame,
        out int panelIndex,
        out ChartRectF panel)
    {
        if (ContainsY(frame.MainPanel, pointerY))
        {
            panelIndex = 0;
            panel = frame.MainPanel;
            return true;
        }

        if (ContainsY(frame.VolumePanel, pointerY))
        {
            panelIndex = ChartCursorSnapshot.VolumePanelIndex;
            panel = frame.VolumePanel;
            return true;
        }

        for (int index = 1; index <= ChartFrame.MaximumPanelIndex; index++)
        {
            if (!frame.PanelVisible[index]) continue;
            ChartRectF candidate = frame.PanelRects[index];
            if (!ContainsY(candidate, pointerY)) continue;
            panelIndex = index;
            panel = candidate;
            return true;
        }

        panelIndex = 0;
        panel = ChartRectF.Empty;
        return false;
    }

    private static float ResolveAxisValue(
        float y,
        int panelIndex,
        ChartRectF panel,
        ChartFrame frame)
    {
        float ratio = (panel.Bottom - y) / Math.Max(0.0001f, panel.Height);
        if (panelIndex == ChartCursorSnapshot.VolumePanelIndex)
            return ratio * Math.Max(1L, frame.VolumeMaximum);

        NumericRange range = panelIndex == 0
            ? frame.PriceRange
            : frame.PanelRanges[panelIndex];
        return range.Minimum + ratio * range.Span;
    }

    private static bool ContainsY(ChartRectF rect, float y) =>
        !rect.IsEmpty && y >= rect.Top && y <= rect.Bottom;
}
