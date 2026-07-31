using ChartKit.CSharp.Charting;
using ChartKit.CSharp.Contracts;

namespace ChartKit.CSharp.EngineVerification;

internal static class ChartCursorVerification
{
    public static void Run()
    {
        Candle[] candles = Fixture.CreateCandles(240).ToArray();
        var snapshot = new SymbolSnapshot(
            "CURSOR",
            candles,
            Array.Empty<IndicatorSeriesSnapshot>(),
            1L,
            DateTimeOffset.UtcNow);
        var viewport = new ChartViewport(120, 20, 500);
        ChartWindow window = viewport.Resolve(candles.Length);
        var frame = new ChartFrame();
        new ChartFrameBuilder().Build(
            snapshot,
            window,
            1200f,
            800f,
            target: frame);
        var cursor = new ChartCursorController();

        int expectedVisibleIndex = 17;
        ChartCursorSnapshot selected = cursor.Update(
            frame.X(expectedVisibleIndex),
            frame.MainPanel.MidY,
            snapshot,
            frame);
        if (!selected.IsVisible)
            throw new InvalidOperationException("Chart cursor was not activated.");
        if (selected.VisibleIndex != expectedVisibleIndex ||
            selected.CandleIndex != window.StartIndex + expectedVisibleIndex)
            throw new InvalidOperationException("Chart cursor selected the wrong candle.");
        if (selected.Price < frame.PriceRange.Minimum ||
            selected.Price > frame.PriceRange.Maximum)
            throw new InvalidOperationException("Chart cursor price was outside the price range.");
        if (selected.Candle.Sequence != candles[selected.CandleIndex].Sequence)
            throw new InvalidOperationException("Chart cursor candle payload mismatch.");

        for (int index = 0; index < 20; index++)
            cursor.Update(frame.X(10), frame.MainPanel.MidY, snapshot, frame);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int index = 0; index < 1_000; index++)
            cursor.Update(frame.X(index % window.Count), frame.MainPanel.MidY, snapshot, frame);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        if (allocated > 1_024)
            throw new InvalidOperationException(
                $"Chart cursor allocation exceeded bound: {allocated} bytes.");

        ChartCursorSnapshot hidden = cursor.Update(
            frame.MainPanel.Left - 1f,
            frame.MainPanel.Top,
            snapshot,
            frame);
        if (hidden.IsVisible)
            throw new InvalidOperationException("Chart cursor remained visible outside the chart.");

        Console.WriteLine($"chart_cursor_allocated_bytes={allocated}");
        Console.WriteLine("csharp_chart_cursor=PASS");
    }
}
