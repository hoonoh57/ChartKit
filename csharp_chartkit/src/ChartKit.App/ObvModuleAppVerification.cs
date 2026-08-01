using System.Text.Json.Nodes;
using ChartKit.CSharp.Contracts;
using ChartKit.CSharp.Engine;
using ChartKit.CSharp.Modules.Abstractions;
using ChartKit.CSharp.Modules.Indicators;
using ChartKit.CSharp.Scene;
using ChartKit.CSharp.UiModel;

namespace ChartKit.CSharp.App;

internal static class ObvModuleAppVerification
{
    public static async Task RunAsync(CandleTimeframe timeframe)
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "chartkit-app-obv-self-test-" + Guid.NewGuid().ToString("N"));
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
                        item.Owner.ModuleId == ObvModule.Definition.ModuleId);
                ChartModulePlatformActionResult enabled =
                    await controller.ExecuteCommandAsync(toggle);
                if (!enabled.Succeeded || !enabled.Changed)
                    throw new InvalidOperationException("App OBV toggle failed.");

                await controller.UpdatePrimarySeriesAsync(
                    primary,
                    viewportVersion: 1,
                    themeVersion: 0,
                    visibleStartIndex: 0,
                    visibleEndExclusive: primary.Bars.Count);
                AssertPlan(controller.RenderPlan, primary, 20, "default");

                controller.Select(toggle.Owner);
                string[] propertyIds = controller.BuildUiCatalog()
                    .InspectorProperties
                    .Select(static item => item.Descriptor.PropertyId)
                    .OrderBy(static item => item, StringComparer.Ordinal)
                    .ToArray();
                string[] expected =
                [
                    "obv.signal.stroke",
                    "obv.value.stroke",
                    "signalPeriod"
                ];
                if (!propertyIds.SequenceEqual(expected))
                    throw new InvalidOperationException(
                        "App OBV property projection failed.");

                await RequireChangeAsync(
                    controller,
                    toggle.Owner.InstanceId,
                    "signalPeriod",
                    JsonValue.Create(7),
                    ChartChangeImpact.RecalculateModule);
                await RequireChangeAsync(
                    controller,
                    toggle.Owner.InstanceId,
                    ObvModule.SignalObjectId + ".stroke",
                    JsonValue.Create("#ABCDEF"),
                    ChartChangeImpact.RedrawOnly);

                AssertPlan(
                    controller.RenderPlan,
                    primary,
                    7,
                    "changed",
                    expectedSignalStroke: "#ABCDEF");
                await controller.SaveCurrentAsync();
            }

            using (var restored = new ChartModulePlatformController(profilePath))
            {
                await restored.InitializeAsync(timeframe.ToString());
                ChartUiCommandItem toggle = restored.BuildUiCatalog()
                    .ContextMenuItems.Single(static item =>
                        item.Kind == ChartUiCommandKind.ModuleToggle &&
                        item.Owner.ModuleId == ObvModule.Definition.ModuleId);
                if (!toggle.IsChecked)
                    throw new InvalidOperationException(
                        "App OBV enabled state was not restored.");

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
                    "restored",
                    expectedSignalStroke: "#ABCDEF");
            }

            Console.WriteLine("csharp_app_obv_module_data=PASS");
            Console.WriteLine("csharp_app_obv_module_parameters=PASS");
            Console.WriteLine("csharp_app_obv_module_style=PASS");
            Console.WriteLine("csharp_app_obv_panel_contract=PASS");
            Console.WriteLine("csharp_app_obv_module_roundtrip=PASS");
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
                $"App OBV property mutation failed: {propertyId}.");
        }
    }

    private static ChartPrimarySeriesSnapshot CreatePrimarySeries()
    {
        var bars = new ChartPrimaryBar[180];
        double previous = 100d;
        for (int index = 0; index < bars.Length; index++)
        {
            double close = 100d + Math.Sin(index / 5d) * 9d +
                           Math.Sin(index / 17d) * 4d + index * 0.02d;
            bars[index] = new ChartPrimaryBar(
                index,
                previous,
                Math.Max(previous, close) + 1.5d,
                Math.Min(previous, close) - 1.5d,
                close,
                3_000L + (index % 13) * 170L + index * 9L,
                true);
            previous = close;
        }
        return new ChartPrimarySeriesSnapshot(600, bars);
    }

    private static void AssertPlan(
        ChartRenderPlan plan,
        ChartPrimarySeriesSnapshot primary,
        int signalPeriod,
        string context,
        string? expectedSignalStroke = null)
    {
        Dictionary<string, RenderPrimitivePlan> series = plan.Primitives
            .Where(static item =>
                item.Identity.ModuleId == ObvModule.Definition.ModuleId)
            .ToDictionary(
                static item => item.Identity.ObjectId,
                StringComparer.Ordinal);
        if (series.Count != 2)
            throw new InvalidOperationException(
                $"App OBV {context} primitive count mismatch.");

        RenderPrimitivePlan obv = series[ObvModule.ObvObjectId];
        RenderPrimitivePlan signal = series[ObvModule.SignalObjectId];
        if (obv.PanelId != "indicator.5" || signal.PanelId != "indicator.5")
            throw new InvalidOperationException(
                $"App OBV {context} panel mismatch.");

        IReadOnlyList<IndicatorPoint> expected =
            new ObvIndicator(signalPeriod).Calculate(ToCandles(primary));
        for (int index = 0; index < expected.Count; index++)
        {
            Equal(
                expected[index].Value0,
                (float)obv.Points[index].Y,
                $"App OBV {context} value index={index}");
            Equal(
                expected[index].Value1,
                (float)signal.Points[index].Y,
                $"App OBV {context} signal index={index}");
        }

        if (expectedSignalStroke is not null &&
            signal.Style.Stroke != expectedSignalStroke)
        {
            throw new InvalidOperationException(
                $"App OBV {context} style mismatch.");
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
