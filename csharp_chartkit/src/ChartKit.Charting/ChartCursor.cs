using ChartKit.CSharp.Contracts;

namespace ChartKit.CSharp.Charting;

public readonly record struct ChartCursorSnapshot(
    bool IsVisible,
    int CandleIndex,
    int VisibleIndex,
    float X,
    float Y,
    float Price,
    Candle Candle)
{
    public static ChartCursorSnapshot Hidden { get; } = new(
        false,
        -1,
        -1,
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
            pointerY < frame.MainPanel.Top || pointerY > frame.TimeAxis.Top)
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
        float y = Math.Clamp(pointerY, frame.MainPanel.Top, frame.MainPanel.Bottom);
        float ratio = (frame.MainPanel.Bottom - y) /
                      Math.Max(0.0001f, frame.MainPanel.Height);
        float price = frame.PriceRange.Minimum + ratio * frame.PriceRange.Span;

        Current = new ChartCursorSnapshot(
            true,
            candleIndex,
            visibleIndex,
            frame.X(visibleIndex),
            y,
            price,
            candle);
        return Current;
    }

    public void Clear() => Current = ChartCursorSnapshot.Hidden;
}
