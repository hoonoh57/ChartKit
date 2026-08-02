using ChartKit.CSharp.Charting;
using ChartKit.CSharp.Contracts;

namespace ChartKit.CSharp.EngineVerification;

internal static class ChartPanelAxisVerification
{
    public static void Run()
    {
        Candle[] candles = Fixture.CreateCandles(240).ToArray();
        IndicatorSeriesSnapshot[] indicators =
        [
            CreateSeries(candles, 1, "RSI_AXIS", index =>
                20f + 60f * index / Math.Max(1f, candles.Length - 1f)),
            CreateSeries(candles, 2, "OBV_AXIS", index =>
                -80_000f + 55_000f * index / Math.Max(1f, candles.Length - 1f)),
            CreateSeries(candles, 3, "DISPARITY_AXIS", index =>
                94f + 12f * index / Math.Max(1f, candles.Length - 1f)),
            CreateSeries(candles, 4, "MACD_AXIS", index =>
                -1_200f + 2_400f * index / Math.Max(1f, candles.Length - 1f))
        ];
        var snapshot = new SymbolSnapshot(
            "PANEL_AXIS",
            candles,
            indicators,
            1L,
            DateTimeOffset.UtcNow);
        var viewport = new ChartViewport(120, 20, 500);
        ChartWindow window = viewport.Resolve(candles.Length);
        var builder = new ChartFrameBuilder();
        var frame = new ChartFrame();
        builder.Build(snapshot, window, 1200f, 800f, target: frame);

        for (int panel = 1; panel <= 4; panel++)
        {
            if (!frame.PanelVisible[panel])
                throw new InvalidOperationException($"Panel {panel} was not visible.");
            int count = frame.PanelTickCounts[panel];
            if (count < 2)
                throw new InvalidOperationException(
                    $"Panel {panel} produced too few axis ticks: {count}.");

            NumericAxisTick previous = frame.GetPanelTick(panel, 0);
            AssertTick(panel, previous, frame);
            for (int index = 1; index < count; index++)
            {
                NumericAxisTick current = frame.GetPanelTick(panel, index);
                AssertTick(panel, current, frame);
                if (current.Value <= previous.Value)
                    throw new InvalidOperationException(
                        $"Panel {panel} axis values were not increasing.");
                if (current.Position >= previous.Position)
                    throw new InvalidOperationException(
                        $"Panel {panel} axis positions were not descending.");
                previous = current;
            }
        }

        for (int index = 0; index < 20; index++)
            builder.Build(snapshot, window, 1200f, 800f, target: frame);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int index = 0; index < 500; index++)
            builder.Build(snapshot, window, 1200f, 800f, target: frame);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        if (allocated > 4_096)
            throw new InvalidOperationException(
                $"Panel axis allocation exceeded bound: {allocated} bytes.");

        Console.WriteLine($"panel_axis_allocated_bytes={allocated}");
        Console.WriteLine("csharp_subchart_axes=PASS");
    }

    private static IndicatorSeriesSnapshot CreateSeries(
        Candle[] candles,
        int panelIndex,
        string id,
        Func<int, float> valueFactory)
    {
        var descriptor = new IndicatorDescriptor(
            id,
            id,
            panelIndex,
            ["Value"],
            [SeriesKind.Line]);
        var points = new IndicatorPoint[candles.Length];
        for (int index = 0; index < candles.Length; index++)
            points[index] = new IndicatorPoint(
                candles[index].Sequence,
                valueFactory(index));
        return new IndicatorSeriesSnapshot(descriptor, points);
    }

    private static void AssertTick(
        int panel,
        NumericAxisTick tick,
        ChartFrame frame)
    {
        NumericRange range = frame.PanelRanges[panel];
        ChartRectF rect = frame.PanelRects[panel];
        if (!float.IsFinite(tick.Value) ||
            tick.Value < range.Minimum || tick.Value > range.Maximum)
            throw new InvalidOperationException(
                $"Panel {panel} axis value was outside its range.");
        if (!float.IsFinite(tick.Position) ||
            tick.Position < rect.Top || tick.Position > rect.Bottom)
            throw new InvalidOperationException(
                $"Panel {panel} axis position was outside its panel.");
    }
}
