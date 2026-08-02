using ChartKit.CSharp.Charting;
using ChartKit.CSharp.Contracts;
using ChartKit.CSharp.Engine;
using ChartKit.CSharp.Rendering;
using SkiaSharp;

namespace ChartKit.CSharp.EngineVerification;

internal static class RenderingVerification
{
    public static async Task RunAsync()
    {
        await using var engine = new MultiSymbolEngine(new MultiSymbolEngineOptions(
            WorkerCount: 1,
            QueueCapacityPerWorker: 64,
            CandleCapacity: 512,
            SnapshotBars: 240,
            SnapshotInterval: TimeSpan.FromMilliseconds(1)));

        List<Candle> history = Fixture.CreateCandles(240);
        await engine.LoadHistoryAsync("RENDER", history);
        if (!engine.TryGetSnapshot("RENDER", out SymbolSnapshot? snapshot) || snapshot is null)
            throw new InvalidOperationException("Render snapshot was not published.");

        using var bitmap = new SKBitmap(
            new SKImageInfo(1200, 800, SKColorType.Bgra8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bitmap);
        using var renderer = new SkiaChartRenderer();
        using var legendRenderer = new ChartLegendRenderer();
        using var crosshairRenderer = new ChartCrosshairRenderer();
        var viewport = new ChartViewport(180, 20, 500);
        var frameBuilder = new ChartFrameBuilder();
        var frame = new ChartFrame();
        ChartWindow window = viewport.Resolve(snapshot.Candles.Length);
        frameBuilder.Build(snapshot, window, bitmap.Width, bitmap.Height, target: frame);
        var options = new ChartRenderOptions(ShowText: false, ShowAxes: false);

        for (int index = 0; index < 20; index++)
            renderer.Render(canvas, snapshot, frame, options);
        canvas.Flush();

        Exception? reentryFailure = null;
        Parallel.For(
            0,
            128,
            _ =>
            {
                try
                {
                    renderer.Render(canvas, snapshot, frame, options);
                }
                catch (Exception exception)
                {
                    Interlocked.CompareExchange(
                        ref reentryFailure,
                        exception,
                        null);
                }
            });
        canvas.Flush();
        if (reentryFailure is not null)
        {
            throw new InvalidOperationException(
                "Renderer reentry guard failed.",
                reentryFailure);
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int index = 0; index < 200; index++)
            renderer.Render(canvas, snapshot, frame, options);
        canvas.Flush();
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        if (allocated > 32_768)
            throw new InvalidOperationException(
                $"Rendering allocation exceeded bound: {allocated} bytes.");

        var legendBuilder = new ChartLegendBuilder();
        var legend = new ChartLegendFrame();
        legendBuilder.Build(snapshot, window.EndExclusive - 1, legend);
        legendRenderer.Render(canvas, frame, legend);
        canvas.Flush();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        long legendBefore = GC.GetAllocatedBytesForCurrentThread();
        for (int index = 0; index < 200; index++)
            legendRenderer.Render(canvas, frame, legend);
        canvas.Flush();
        long legendAllocated =
            GC.GetAllocatedBytesForCurrentThread() - legendBefore;
        if (legendAllocated > 32_768)
            throw new InvalidOperationException(
                $"Legend rendering allocation exceeded bound: {legendAllocated} bytes.");

        var cursorController = new ChartCursorController();
        ChartRectF cursorPanel = frame.MainPanel;
        for (int panel = 1; panel <= ChartFrame.MaximumPanelIndex; panel++)
        {
            if (!frame.PanelVisible[panel]) continue;
            cursorPanel = frame.PanelRects[panel];
            break;
        }
        ChartCursorSnapshot cursor = cursorController.Update(
            frame.X(40),
            cursorPanel.MidY,
            snapshot,
            frame);
        if (!cursor.IsVisible)
            throw new InvalidOperationException("Panel crosshair fixture was not visible.");
        crosshairRenderer.Render(canvas, frame, cursor);
        canvas.Flush();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        long crosshairBefore = GC.GetAllocatedBytesForCurrentThread();
        for (int index = 0; index < 200; index++)
            crosshairRenderer.Render(canvas, frame, cursor);
        canvas.Flush();
        long crosshairAllocated =
            GC.GetAllocatedBytesForCurrentThread() - crosshairBefore;
        if (crosshairAllocated > 32_768)
            throw new InvalidOperationException(
                $"Crosshair rendering allocation exceeded bound: {crosshairAllocated} bytes.");

        ChartWindow panned = viewport.Pan(40, snapshot.Candles.Length);
        if (panned.StartIndex >= window.StartIndex)
            throw new InvalidOperationException("Renderer viewport did not move to older candles.");
        frameBuilder.Build(snapshot, panned, bitmap.Width, bitmap.Height, target: frame);
        renderer.Render(
            canvas,
            snapshot,
            frame,
            new ChartRenderOptions(ShowText: true, ShowAxes: true));
        legendBuilder.Build(snapshot, snapshot.Candles.Length - 1, legend);
        legendRenderer.Render(canvas, frame, legend);
        canvas.Flush();

        SKColor background = new(11, 15, 20);
        int changedSamples = 0;
        for (int y = 0; y < bitmap.Height; y += 8)
        {
            for (int x = 0; x < bitmap.Width; x += 8)
            {
                if (bitmap.GetPixel(x, y) != background) changedSamples++;
            }
        }
        if (changedSamples < 100)
            throw new InvalidOperationException(
                $"Rendered image contained too few chart pixels: {changedSamples}.");

        Console.WriteLine("csharp_rendering_reentry_guard=PASS");
        Console.WriteLine($"render_allocated_bytes={allocated}");
        Console.WriteLine($"legend_allocated_bytes={legendAllocated}");
        Console.WriteLine($"crosshair_allocated_bytes={crosshairAllocated}");
        Console.WriteLine($"render_changed_samples={changedSamples}");
        Console.WriteLine("csharp_rendering_verification=PASS");
    }
}
