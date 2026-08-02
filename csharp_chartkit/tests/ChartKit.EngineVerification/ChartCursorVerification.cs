using ChartKit.CSharp.Charting;
using ChartKit.CSharp.Contracts;

namespace ChartKit.CSharp.EngineVerification;

internal static class ChartCursorVerification
{
    public static void Run()
    {
        Candle[] candles = Fixture.CreateCandles(240).ToArray();
        IndicatorPoint[] points = candles
            .Select(candle => new IndicatorPoint(
                candle.Sequence,
                candle.Close * 0.01f,
                candle.Volume))
            .ToArray();
        var descriptor = new IndicatorDescriptor(
            "CURSOR_TEST",
            "CursorTest",
            1,
            ["Line", "Volume"],
            [SeriesKind.Line, SeriesKind.Histogram]);
        var snapshot = new SymbolSnapshot(
            "CURSOR",
            candles,
            [new IndicatorSeriesSnapshot(descriptor, points)],
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
        if (selected.PanelIndex != 0 ||
            selected.AxisValue < frame.PriceRange.Minimum ||
            selected.AxisValue > frame.PriceRange.Maximum)
            throw new InvalidOperationException("Main-panel cursor value was outside the price range.");
        if (selected.Candle.Sequence != candles[selected.CandleIndex].Sequence)
            throw new InvalidOperationException("Chart cursor candle payload mismatch.");

        ChartRectF indicatorPanel = frame.PanelRects[1];
        ChartCursorSnapshot indicatorSelected = cursor.Update(
            frame.X(expectedVisibleIndex),
            indicatorPanel.MidY,
            snapshot,
            frame);
        if (!indicatorSelected.IsVisible || indicatorSelected.PanelIndex != 1)
            throw new InvalidOperationException("Indicator-panel cursor was not selected.");
        if (indicatorSelected.AxisValue < frame.PanelRanges[1].Minimum ||
            indicatorSelected.AxisValue > frame.PanelRanges[1].Maximum)
            throw new InvalidOperationException("Indicator-panel cursor value was outside its range.");

        ChartCursorSnapshot volumeSelected = cursor.Update(
            frame.X(expectedVisibleIndex),
            frame.VolumePanel.MidY,
            snapshot,
            frame);
        if (!volumeSelected.IsVisible ||
            volumeSelected.PanelIndex != ChartCursorSnapshot.VolumePanelIndex ||
            volumeSelected.AxisValue < 0f ||
            volumeSelected.AxisValue > frame.VolumeMaximum)
            throw new InvalidOperationException("Volume-panel cursor value was invalid.");

        var legendBuilder = new ChartLegendBuilder();
        var legend = new ChartLegendFrame();
        legendBuilder.Build(snapshot, selected.CandleIndex, legend);
        if (legend.EntryCount != 2 || legend.CandleIndex != selected.CandleIndex)
            throw new InvalidOperationException("Selected-candle legend model was invalid.");
        if (!legend.Entries[0].HasValue ||
            legend.Entries[0].ColorIndex != 0 ||
            legend.Entries[1].ColorIndex != 1)
            throw new InvalidOperationException("Legend value or palette ordering was invalid.");
        if (!legend.Entries[0].IsIndicatorStart ||
            legend.Entries[1].IsIndicatorStart ||
            !ReferenceEquals(
                legend.Entries[0].IndicatorName,
                legend.Entries[1].IndicatorName))
            throw new InvalidOperationException("Legend indicator-name grouping was invalid.");
        legendBuilder.Build(snapshot, candles.Length - 1, legend);
        if (legend.CandleIndex != candles.Length - 1)
            throw new InvalidOperationException("Latest-candle legend fallback was invalid.");

        for (int index = 0; index < 20; index++)
        {
            cursor.Update(frame.X(10), indicatorPanel.MidY, snapshot, frame);
            legendBuilder.Build(snapshot, index, legend);
        }
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int index = 0; index < 1_000; index++)
        {
            cursor.Update(
                frame.X(index % window.Count),
                (index & 1) == 0 ? frame.MainPanel.MidY : indicatorPanel.MidY,
                snapshot,
                frame);
            legendBuilder.Build(snapshot, index % candles.Length, legend);
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        if (allocated > 1_024)
            throw new InvalidOperationException(
                $"Chart cursor/legend allocation exceeded bound: {allocated} bytes.");

        ChartCursorSnapshot hidden = cursor.Update(
            frame.MainPanel.Left - 1f,
            frame.MainPanel.Top,
            snapshot,
            frame);
        if (hidden.IsVisible)
            throw new InvalidOperationException("Chart cursor remained visible outside the chart.");

        Console.WriteLine($"chart_cursor_legend_allocated_bytes={allocated}");
        Console.WriteLine("csharp_chart_cursor=PASS");
        Console.WriteLine("csharp_chart_legend_model=PASS");
        Console.WriteLine("csharp_chart_legend_grouping=PASS");
    }
}
