using System.Text.Json.Nodes;
using ChartKit.CSharp.Contracts;
using ChartKit.CSharp.Engine;
using ChartKit.CSharp.Modules.Abstractions;
using ChartKit.CSharp.Modules.Indicators;
using ChartKit.CSharp.Scene;
using ChartKit.CSharp.UiModel;

namespace ChartKit.CSharp.App;

internal static class DisparityModuleAppVerification
{
    public static async Task RunAsync(CandleTimeframe timeframe)
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "chartkit-app-disparity-self-test-" + Guid.NewGuid().ToString("N"));
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
                        item.Owner.ModuleId == DisparityModule.Definition.ModuleId);
                ChartModulePlatformActionResult enabled =
                    await controller.ExecuteCommandAsync(toggle);
                if (!enabled.Succeeded || !enabled.Changed)
                    throw new InvalidOperationException(
                        "App Disparity toggle failed.");

                await controller.UpdatePrimarySeriesAsync(
                    primary,
                    viewportVersion: 1,
                    themeVersion: 0,
                    visibleStartIndex: 0,
                    visibleEndExclusive: primary.Bars.Count);
                AssertPlan(
                    controller.RenderPlan,
                    primary,
                    period: 20,
                    upper: 105d,
                    baseline: 100d,
                    lower: 95d,
                    context: "default");

                controller.Select(toggle.Owner);
                string[] propertyIds = controller.BuildUiCatalog()
                    .InspectorProperties
                    .Select(static item => item.Descriptor.PropertyId)
                    .OrderBy(static item => item, StringComparer.Ordinal)
                    .ToArray();
                string[] expected =
                [
                    "baseline",
                    "disparity.baseline.stroke",
                    "disparity.lower.stroke",
                    "disparity.upper.stroke",
                    "disparity.value.stroke",
                    "lower",
                    "period",
                    "upper"
                ];
                if (!propertyIds.SequenceEqual(expected))
                    throw new InvalidOperationException(
                        "App Disparity property projection failed.");

                await RequireChangeAsync(
                    controller,
                    toggle.Owner.InstanceId,
                    "period",
                    JsonValue.Create(7),
                    ChartChangeImpact.RecalculateModule);
                await RequireChangeAsync(
                    controller,
                    toggle.Owner.InstanceId,
                    "upper",
                    JsonValue.Create(108d),
                    ChartChangeImpact.RebuildVisuals);
                await RequireChangeAsync(
                    controller,
                    toggle.Owner.InstanceId,
                    "baseline",
                    JsonValue.Create(101d),
                    ChartChangeImpact.RebuildVisuals);
                await RequireChangeAsync(
                    controller,
                    toggle.Owner.InstanceId,
                    "lower",
                    JsonValue.Create(94d),
                    ChartChangeImpact.RebuildVisuals);
                await RequireChangeAsync(
                    controller,
                    toggle.Owner.InstanceId,
                    DisparityModule.BaselineObjectId + ".stroke",
                    JsonValue.Create("#ABCDEF"),
                    ChartChangeImpact.RedrawOnly);

                AssertPlan(
                    controller.RenderPlan,
                    primary,
                    period: 7,
                    upper: 108d,
                    baseline: 101d,
                    lower: 94d,
                    context: "changed",
                    expectedBaselineStroke: "#ABCDEF");
                await controller.SaveCurrentAsync();
            }

            using (var restored = new ChartModulePlatformController(profilePath))
            {
                await restored.InitializeAsync(timeframe.ToString());
                ChartUiCommandItem toggle = restored.BuildUiCatalog()
                    .ContextMenuItems.Single(static item =>
                        item.Kind == ChartUiCommandKind.ModuleToggle &&
                        item.Owner.ModuleId == DisparityModule.Definition.ModuleId);
                if (!toggle.IsChecked)
                    throw new InvalidOperationException(
                        "App Disparity enabled state was not restored.");

                await restored.UpdatePrimarySeriesAsync(
                    primary,
                    viewportVersion: 1,
                    themeVersion: 0,
                    visibleStartIndex: 0,
                    visibleEndExclusive: primary.Bars.Count);
                AssertPlan(
                    restored.RenderPlan,
                    primary,
                    period: 7,
                    upper: 108d,
                    baseline: 101d,
                    lower: 94d,
                    context: "restored",
                    expectedBaselineStroke: "#ABCDEF");
            }

            Console.WriteLine("csharp_app_disparity_module_data=PASS");
            Console.WriteLine("csharp_app_disparity_module_parameters=PASS");
            Console.WriteLine("csharp_app_disparity_module_style=PASS");
            Console.WriteLine("csharp_app_disparity_panel_contract=PASS");
            Console.WriteLine("csharp_app_disparity_module_roundtrip=PASS");
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
                $"App Disparity property mutation failed: {propertyId}.");
        }
    }

    private static ChartPrimarySeriesSnapshot CreatePrimarySeries()
    {
        var bars = new ChartPrimaryBar[180];
        double previous = 100d;
        for (int index = 0; index < bars.Length; index++)
        {
            double close = 100d + Math.Sin(index / 6d) * 8d +
                           Math.Sin(index / 21d) * 5d + index * 0.03d;
            bars[index] = new ChartPrimaryBar(
                index,
                previous,
                Math.Max(previous, close) + 1.5d,
                Math.Min(previous, close) - 1.5d,
                close,
                2_500L + index * 13L,
                true);
            previous = close;
        }
        return new ChartPrimarySeriesSnapshot(700, bars);
    }

    private static void AssertPlan(
        ChartRenderPlan plan,
        ChartPrimarySeriesSnapshot primary,
        int period,
        double upper,
        double baseline,
        double lower,
        string context,
        string? expectedBaselineStroke = null)
    {
        Dictionary<string, RenderPrimitivePlan> series = plan.Primitives
            .Where(static item =>
                item.Identity.ModuleId == DisparityModule.Definition.ModuleId)
            .ToDictionary(
                static item => item.Identity.ObjectId,
                StringComparer.Ordinal);
        if (series.Count != 4)
            throw new InvalidOperationException(
                $"App Disparity {context} primitive count mismatch.");

        RenderPrimitivePlan value = series[DisparityModule.ValueObjectId];
        RenderPrimitivePlan upperLine = series[DisparityModule.UpperObjectId];
        RenderPrimitivePlan baselineLine =
            series[DisparityModule.BaselineObjectId];
        RenderPrimitivePlan lowerLine = series[DisparityModule.LowerObjectId];
        if (series.Values.Any(static item => item.PanelId != "indicator.6"))
            throw new InvalidOperationException(
                $"App Disparity {context} panel mismatch.");

        IReadOnlyList<IndicatorPoint> expected =
            new DisparityIndicator(period).Calculate(ToCandles(primary));
        for (int index = 0; index < expected.Count; index++)
        {
            Equal(
                expected[index].Value0,
                (float)value.Points[index].Y,
                $"App Disparity {context} value index={index}");
            Equal(
                (float)upper,
                (float)upperLine.Points[index].Y,
                $"App Disparity {context} upper index={index}");
            Equal(
                (float)baseline,
                (float)baselineLine.Points[index].Y,
                $"App Disparity {context} baseline index={index}");
            Equal(
                (float)lower,
                (float)lowerLine.Points[index].Y,
                $"App Disparity {context} lower index={index}");
        }

        if (expectedBaselineStroke is not null &&
            baselineLine.Style.Stroke != expectedBaselineStroke)
        {
            throw new InvalidOperationException(
                $"App Disparity {context} style mismatch.");
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
