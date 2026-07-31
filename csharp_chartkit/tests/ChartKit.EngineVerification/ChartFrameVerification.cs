using ChartKit.CSharp.Charting;
using ChartKit.CSharp.Contracts;

namespace ChartKit.CSharp.EngineVerification;

internal static class ChartFrameVerification
{
    public static void Run()
    {
        Candle[] candles = Fixture.CreateCandles(240).ToArray();
        var snapshot = new SymbolSnapshot(
            "FRAME",
            candles,
            Array.Empty<IndicatorSeriesSnapshot>(),
            1L,
            DateTimeOffset.UtcNow);
        var viewport = new ChartViewport(120, 20, 500);
        ChartWindow window = viewport.Resolve(candles.Length);
        var builder = new ChartFrameBuilder();
        var frame = new ChartFrame();
        builder.Build(snapshot, window, 1200f, 800f, target: frame);

        if (frame.Window != window)
            throw new InvalidOperationException("Chart frame window mismatch.");
        if (frame.MainPanel.IsEmpty || frame.VolumePanel.IsEmpty || frame.TimeAxis.IsEmpty)
            throw new InvalidOperationException("Chart frame layout was not resolved.");
        if (frame.PriceTickCount < 2 || frame.TimeTickCount < 2)
            throw new InvalidOperationException("Chart axes did not produce enough ticks.");
        if (frame.PriceRange.Minimum > candles[window.StartIndex].Low ||
            frame.PriceRange.Maximum < candles[window.StartIndex].High)
            throw new InvalidOperationException("Chart price range excluded a visible candle.");
        if (frame.X(1) <= frame.X(0))
            throw new InvalidOperationException("Chart x coordinates are not increasing.");
        for (int index = 1; index < frame.TimeTickCount; index++)
        {
            if (frame.TimeTicks[index].CandleIndex <= frame.TimeTicks[index - 1].CandleIndex)
                throw new InvalidOperationException("Time axis candle indexes are not increasing.");
            if (frame.TimeTicks[index].Position <= frame.TimeTicks[index - 1].Position)
                throw new InvalidOperationException("Time axis positions are not increasing.");
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
                $"Chart frame allocation exceeded bound: {allocated} bytes.");

        Console.WriteLine($"chart_frame_allocated_bytes={allocated}");
        Console.WriteLine("csharp_chart_frame=PASS");
    }
}
