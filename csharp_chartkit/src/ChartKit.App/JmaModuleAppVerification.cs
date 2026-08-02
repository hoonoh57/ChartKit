using System.Text.Json.Nodes;
using ChartKit.CSharp.Contracts;
using ChartKit.CSharp.Engine;
using ChartKit.CSharp.Modules.Abstractions;
using ChartKit.CSharp.Modules.Indicators;
using ChartKit.CSharp.Scene;
using ChartKit.CSharp.UiModel;

namespace ChartKit.CSharp.App;

internal static class JmaModuleAppVerification
{
    public static async Task RunAsync(CandleTimeframe timeframe)
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "chartkit-app-jma-self-test-" + Guid.NewGuid().ToString("N"));
        string profilePath = Path.Combine(directory, "chart-profile.json");
        ChartPrimarySeriesSnapshot primary = CreatePrimarySeries();

        try
        {
            using (var controller = new ChartModulePlatformController(profilePath))
            {
                await controller.InitializeAsync(timeframe.ToString());
                ChartUiCommandItem toggle = controller.BuildUiCatalog()
                    .ContextMenuItems.Single(static item =>
                        item.Kind == ChartUiCommandKind.ModuleToggle &&
                        item.Owner.ModuleId == JmaModule.Definition.ModuleId);
                ChartModulePlatformActionResult enabled =
                    await controller.ExecuteCommandAsync(toggle);
                if (!enabled.Succeeded || !enabled.Changed)
                    throw new InvalidOperationException("App JMA toggle failed.");

                await controller.UpdatePrimarySeriesAsync(
                    primary,
                    viewportVersion: 1,
                    themeVersion: 0,
                    visibleStartIndex: 0,
                    visibleEndExclusive: primary.Bars.Count);
                AssertPlan(controller.RenderPlan, primary, 14, 50, 2, "default");

                controller.Select(toggle.Owner);
                string[] propertyIds = controller.BuildUiCatalog()
                    .InspectorProperties
                    .Select(static item => item.Descriptor.PropertyId)
                    .OrderBy(static item => item, StringComparer.Ordinal)
                    .ToArray();
                string[] expected =
                [
                    "jma.down.stroke",
                    "jma.up.stroke",
                    "period",
                    "phase",
                    "power"
                ];
                if (!propertyIds.SequenceEqual(expected))
                    throw new InvalidOperationException(
                        "App JMA property projection failed.");

                await RequireChangeAsync(
                    controller,
                    toggle.Owner.InstanceId,
                    "period",
                    JsonValue.Create(7),
                    ChartChangeImpact.RecalculateModule);
                await RequireChangeAsync(
                    controller,
                    toggle.Owner.InstanceId,
                    "phase",
                    JsonValue.Create(-25),
                    ChartChangeImpact.RecalculateModule);
                await RequireChangeAsync(
                    controller,
                    toggle.Owner.InstanceId,
                    "power",
                    JsonValue.Create(3),
                    ChartChangeImpact.RecalculateModule);
                await RequireChangeAsync(
                    controller,
                    toggle.Owner.InstanceId,
                    JmaModule.DownObjectId + ".stroke",
                    JsonValue.Create("#ABCDEF"),
                    ChartChangeImpact.RedrawOnly);

                AssertPlan(
                    controller.RenderPlan,
                    primary,
                    7,
                    -25,
                    3,
                    "changed",
                    expectedDownStroke: "#ABCDEF");
                await controller.SaveCurrentAsync();
            }

            using (var restored = new ChartModulePlatformController(profilePath))
            {
                await restored.InitializeAsync(timeframe.ToString());
                ChartUiCommandItem toggle = restored.BuildUiCatalog()
                    .ContextMenuItems.Single(static item =>
                        item.Kind == ChartUiCommandKind.ModuleToggle &&
                        item.Owner.ModuleId == JmaModule.Definition.ModuleId);
                if (!toggle.IsChecked)
                    throw new InvalidOperationException(
                        "App JMA enabled state was not restored.");

                await restored.UpdatePrimarySeriesAsync(
                    primary,
                    viewportVersion: 1,
                    themeVersion: 0,
                    visibleStartIndex: 0,
                    visibleEndExclusive: primary.Bars.Count);
                AssertPlan(
                    restored.RenderPlan,
                    primary,
                    7,
                    -25,
                    3,
                    "restored",
                    expectedDownStroke: "#ABCDEF");
            }

            Console.WriteLine("csharp_app_jma_module_data=PASS");
            Console.WriteLine("csharp_app_jma_module_parameters=PASS");
            Console.WriteLine("csharp_app_jma_module_style=PASS");
            Console.WriteLine("csharp_app_jma_panel_contract=PASS");
            Console.WriteLine("csharp_app_jma_module_roundtrip=PASS");
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    private static async Task RequireChangeAsync(
        ChartModulePlatformController controller,
        string instanceId,
        string propertyId,
        JsonNode? value,
        ChartChangeImpact expectedImpact)
    {
        ChartPropertyChangeResult result = await controller.ChangePropertyAsync(
            instanceId,
            propertyId,
            value);
        if (!result.Succeeded ||
            !result.Changed ||
            result.ChangeImpact != expectedImpact)
        {
            throw new InvalidOperationException(
                $"App JMA property mutation failed: {propertyId}.");
        }
    }

    private static ChartPrimarySeriesSnapshot CreatePrimarySeries()
    {
        var bars = new ChartPrimaryBar[180];
        double previous = 100d;
        for (int index = 0; index < bars.Length; index++)
        {
            double close = 100d + Math.Sin(index / 7d) * 15d +
                           Math.Sin(index / 19d) * 8d + index * 0.04d;
            bars[index] = new ChartPrimaryBar(
                index,
                previous,
                Math.Max(previous, close) + 2d,
                Math.Min(previous, close) - 2d,
                close,
                4_000L + index * 20L,
                true);
            previous = close;
        }
        return new ChartPrimarySeriesSnapshot(500, bars);
    }

    private static void AssertPlan(
        ChartRenderPlan plan,
        ChartPrimarySeriesSnapshot primary,
        int period,
        int phase,
        int power,
        string context,
        string? expectedDownStroke = null)
    {
        Dictionary<string, RenderPrimitivePlan> series = plan.Primitives
            .Where(static item =>
                item.Identity.ModuleId == JmaModule.Definition.ModuleId)
            .ToDictionary(
                static item => item.Identity.ObjectId,
                StringComparer.Ordinal);
        if (series.Count != 2)
            throw new InvalidOperationException(
                $"App JMA {context} primitive count mismatch.");

        RenderPrimitivePlan up = series[JmaModule.UpObjectId];
        RenderPrimitivePlan down = series[JmaModule.DownObjectId];
        if (up.PanelId != "price.main" || down.PanelId != "price.main")
            throw new InvalidOperationException(
                $"App JMA {context} panel mismatch.");

        IReadOnlyList<IndicatorPoint> expected =
            new JmaIndicator(period, phase, power).Calculate(ToCandles(primary));
        bool hasUp = false;
        bool hasDown = false;
        for (int index = 0; index < expected.Count; index++)
        {
            Equal(
                expected[index].Value1,
                (float)up.Points[index].Y,
                $"App JMA {context} up index={index}");
            Equal(
                expected[index].Value2,
                (float)down.Points[index].Y,
                $"App JMA {context} down index={index}");
            hasUp |= float.IsFinite(expected[index].Value1);
            hasDown |= float.IsFinite(expected[index].Value2);
        }
        if (!hasUp || !hasDown)
            throw new InvalidOperationException(
                $"App JMA {context} fixture did not exercise both directions.");

        if (expectedDownStroke is not null &&
            down.Style.Stroke != expectedDownStroke)
        {
            throw new InvalidOperationException(
                $"App JMA {context} style mismatch.");
        }
    }

    private static IReadOnlyList<Candle> ToCandles(
        ChartPrimarySeriesSnapshot primary)
    {
        var candles = new Candle[primary.Bars.Count];
        DateTime start = new(2026, 8, 2, 9, 0, 0);
        for (int index = 0; index < candles.Length; index++)
        {
            ChartPrimaryBar bar = primary.Bars[index];
            candles[index] = new Candle(
                start.AddMinutes(index),
                start.AddMinutes(index + 1),
                (float)bar.Open,
                (float)bar.High,
                (float)bar.Low,
                (float)bar.Close,
                bar.Volume,
                bar.IsFinal,
                bar.Sequence);
        }
        return candles;
    }

    private static void Equal(
        float expected,
        float actual,
        string context,
        float tolerance = 0.0001f)
    {
        if (float.IsNaN(expected) && float.IsNaN(actual)) return;
        float scale = Math.Max(1f, Math.Max(Math.Abs(expected), Math.Abs(actual)));
        if (Math.Abs(expected - actual) > tolerance * scale)
            throw new InvalidOperationException(
                $"{context}: expected={expected}, actual={actual}.");
    }
}
