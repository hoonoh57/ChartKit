using System.Reflection;
using System.Text.Json.Nodes;
using ChartKit.CSharp.Charting;
using ChartKit.CSharp.Composition;
using ChartKit.CSharp.Contracts;
using ChartKit.CSharp.Engine;
using ChartKit.CSharp.ModuleHost;
using ChartKit.CSharp.Modules.Abstractions;
using ChartKit.CSharp.Modules.Platform;
using ChartKit.CSharp.Rendering;
using ChartKit.CSharp.Scene;
using SkiaSharp;

namespace ChartKit.CSharp.EngineVerification;

internal static class RenderPlanRenderingVerification
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
        await engine.LoadHistoryAsync("MODULE-RENDER", history);
        if (!engine.TryGetSnapshot(
                "MODULE-RENDER",
                out SymbolSnapshot? snapshot) ||
            snapshot is null)
        {
            throw new InvalidOperationException(
                "Render-plan snapshot was not published.");
        }

        var viewport = new ChartViewport(40, 20, 500);
        ChartWindow window = viewport.Resolve(snapshot.Candles.Length);
        if (window.StartIndex <= 0)
            throw new InvalidOperationException(
                "Visible-window fixture must start after candle index zero.");

        var frame = new ChartFrame();
        new ChartFrameBuilder().Build(
            snapshot,
            window,
            1200,
            800,
            target: frame);

        double span = frame.PriceRange.Span;
        double level = frame.PriceRange.Minimum + span * 0.45d;
        double amplitude = span * 0.20d;

        var registry = new ChartModuleRegistry();
        registry.Register<PlatformProbeModule>();
        var host = new ChartModuleHost(registry);
        var composition = new ChartCompositionService(host);
        ChartModuleOperationResult hosted = host.UpsertProfile(new ChartModuleProfile
        {
            ModuleId = PlatformProbeModule.Definition.ModuleId,
            InstanceId = "platform-render-probe",
            ModuleSchemaVersion = PlatformProbeModule.Definition.SchemaVersion,
            IsEnabled = true,
            ZIndex = 3,
            Placement = "price.main",
            Parameters = new JsonObject
            {
                ["level"] = level,
                ["amplitude"] = amplitude
            },
            Style = new JsonObject
            {
                ["stroke"] = "#00FF00",
                ["strokeWidth"] = 2.5d,
                ["opacity"] = 1d
            },
            PersistentState = new JsonObject()
        });
        if (!hosted.Succeeded)
            throw new InvalidOperationException(
                "Render-plan platform probe could not be hosted: " + hosted.Error);

        ChartRenderPlan plan = composition.Compose(
            new ChartVisualContext(
                1,
                1,
                1,
                window.StartIndex,
                window.EndExclusive));
        if (plan.Primitives.Count != 1 ||
            plan.Primitives[0].RenderKind != RenderPrimitiveKind.Polyline ||
            plan.Primitives[0].RenderPoints.Count != 3 ||
            plan.Primitives[0].Style.Stroke != "#00FF00" ||
            plan.Primitives[0].Style.StrokeWidth != 2.5f)
        {
            throw new InvalidOperationException(
                "Scene did not produce a renderer-ready styled primitive.");
        }

        RenderPrimitivePlan probe = plan.Primitives[0];
        if (probe.RenderPoints.Any(point =>
                point.X < window.StartIndex || point.X >= window.EndExclusive) ||
            probe.RenderPoints.Select(static point => point.X)
                .SequenceEqual([0L, 1L, 2L]))
        {
            throw new InvalidOperationException(
                "Platform probe did not use absolute indices in the visible window.");
        }

        using var bitmap = new SKBitmap(
            new SKImageInfo(1200, 800, SKColorType.Bgra8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bitmap);
        using var renderer = new SkiaChartRenderPlanRenderer();
        canvas.Clear(new SKColor(11, 15, 20));
        ChartRenderPlanRenderResult result = renderer.Render(canvas, plan, frame);
        canvas.Flush();
        if (result.RenderedPrimitives != 1 ||
            result.SkippedPrimitives != 0 ||
            result.RenderedPoints != 3)
        {
            throw new InvalidOperationException(
                $"Unexpected render-plan result: {result}.");
        }

        VerifyGreenPixelsAreClipped(bitmap, frame.MainPanel);
        VerifyUnknownPanelIsSkipped(renderer, canvas, frame, plan);
        VerifyUnsupportedPrimitiveIsSkipped(renderer, canvas, frame, plan);

        for (int index = 0; index < 20; index++)
            renderer.Render(canvas, plan, frame);
        canvas.Flush();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int index = 0; index < 200; index++)
            renderer.Render(canvas, plan, frame);
        canvas.Flush();
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        if (allocated > 32_768)
        {
            throw new InvalidOperationException(
                $"Render-plan allocation exceeded bound: {allocated} bytes.");
        }

        VerifyReferenceBoundary();

#if RELEASE
        VerifyReleaseAssembly(typeof(SkiaChartRenderPlanRenderer).Assembly);
        Console.WriteLine(
            "csharp_renderplan_renderer_release_configuration=PASS");
#endif
        Console.WriteLine("csharp_renderplan_renderer_visible_range=PASS");
        Console.WriteLine("csharp_renderplan_renderer_polyline=PASS");
        Console.WriteLine("csharp_renderplan_renderer_style=PASS");
        Console.WriteLine("csharp_renderplan_renderer_panel_clip=PASS");
        Console.WriteLine("csharp_renderplan_renderer_unknown_panel=PASS");
        Console.WriteLine("csharp_renderplan_renderer_unsupported_skip=PASS");
        Console.WriteLine($"renderplan_allocated_bytes={allocated}");
        Console.WriteLine("csharp_renderplan_renderer_allocation=PASS");
        Console.WriteLine("csharp_renderplan_renderer_reference_boundary=PASS");
        Console.WriteLine("csharp_renderplan_renderer_contracts=PASS");
    }

    private static void VerifyGreenPixelsAreClipped(
        SKBitmap bitmap,
        ChartRectF panel)
    {
        int greenPixels = 0;
        for (int y = 0; y < bitmap.Height; y++)
        {
            for (int x = 0; x < bitmap.Width; x++)
            {
                SKColor color = bitmap.GetPixel(x, y);
                bool green = color.Green > 180 &&
                             color.Green > color.Red * 2 &&
                             color.Green > color.Blue * 2;
                if (!green) continue;
                greenPixels++;
                if (x < panel.Left || x > panel.Right ||
                    y < panel.Top || y > panel.Bottom)
                {
                    throw new InvalidOperationException(
                        "Render-plan primitive escaped its panel clip.");
                }
            }
        }

        if (greenPixels < 8)
            throw new InvalidOperationException(
                $"Styled module polyline was not visible: green={greenPixels}.");
    }

    private static void VerifyUnknownPanelIsSkipped(
        SkiaChartRenderPlanRenderer renderer,
        SKCanvas canvas,
        ChartFrame frame,
        ChartRenderPlan source)
    {
        RenderPrimitivePlan original = source.Primitives[0];
        var unknown = new ChartRenderPlan(
        [
            new RenderPrimitivePlan(
                original.Identity,
                "unknown.panel",
                original.PrimitiveKind,
                original.ZIndex,
                original.Points,
                original.Style)
        ]);
        ChartRenderPlanRenderResult result = renderer.Render(canvas, unknown, frame);
        if (result.RenderedPrimitives != 0 || result.SkippedPrimitives != 1)
            throw new InvalidOperationException("Unknown panel was not skipped.");
    }

    private static void VerifyUnsupportedPrimitiveIsSkipped(
        SkiaChartRenderPlanRenderer renderer,
        SKCanvas canvas,
        ChartFrame frame,
        ChartRenderPlan source)
    {
        RenderPrimitivePlan original = source.Primitives[0];
        var unsupported = new ChartRenderPlan(
        [
            new RenderPrimitivePlan(
                original.Identity with { ObjectId = "probe.text" },
                original.PanelId,
                ChartPrimitiveKind.Text,
                original.ZIndex,
                original.Points,
                original.Style)
        ]);
        ChartRenderPlanRenderResult result =
            renderer.Render(canvas, unsupported, frame);
        if (result.RenderedPrimitives != 0 || result.SkippedPrimitives != 1)
        {
            throw new InvalidOperationException(
                "Unsupported primitive was not isolated and skipped.");
        }
    }

    private static void VerifyReferenceBoundary()
    {
        Assembly assembly = typeof(SkiaChartRenderPlanRenderer).Assembly;
        string[] references = assembly.GetReferencedAssemblies()
            .Select(static item => item.Name ?? string.Empty)
            .ToArray();
        if (!references.Contains("ChartKit.Scene", StringComparer.Ordinal))
            throw new InvalidOperationException(
                "Rendering does not reference the Scene contract.");

        string[] forbidden =
        [
            "ChartKit.Modules.Abstractions",
            "ChartKit.Modules.Platform",
            "ChartKit.ModuleHost",
            "ChartKit.Composition",
            "ChartKit.DataSources",
            "ChartKit.App",
            "System.Windows.Forms"
        ];
        foreach (string name in forbidden)
        {
            if (references.Contains(name, StringComparer.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Rendering has forbidden reference: {name}");
            }
        }
    }

    private static void VerifyReleaseAssembly(Assembly assembly)
    {
        string? configuration = assembly
            .GetCustomAttribute<AssemblyConfigurationAttribute>()?
            .Configuration;
        if (!string.Equals(configuration, "Release", StringComparison.Ordinal))
            throw new InvalidOperationException(
                "Render-plan renderer was not built in Release configuration.");
    }
}
