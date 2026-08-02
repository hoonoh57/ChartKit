using System.Text.Json.Nodes;
using ChartKit.CSharp.Contracts;
using ChartKit.CSharp.Engine;
using ChartKit.CSharp.Modules.Abstractions;
using ChartKit.CSharp.Modules.Indicators;
using ChartKit.CSharp.Scene;
using ChartKit.CSharp.UiModel;

namespace ChartKit.CSharp.App;

internal static class MacdModuleAppVerification
{
    public static async Task RunAsync(CandleTimeframe timeframe)
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "chartkit-app-macd-self-test-" + Guid.NewGuid().ToString("N"));
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
                        item.Owner.ModuleId == MacdModule.Definition.ModuleId);
                ChartModulePlatformActionResult enabled =
                    await controller.ExecuteCommandAsync(toggle);
                if (!enabled.Succeeded || !enabled.Changed)
                    throw new InvalidOperationException("App MACD toggle failed.");

                await controller.UpdatePrimarySeriesAsync(
                    primary,
                    viewportVersion: 1,
                    themeVersion: 0,
                    visibleStartIndex: 0,
                    visibleEndExclusive: primary.Bars.Count);
                AssertPlan(controller.RenderPlan, primary, 12, 26, 9, "default");

                controller.Select(toggle.Owner);
                string[] propertyIds = controller.BuildUiCatalog()
                    .InspectorProperties
                    .Select(static item => item.Descriptor.PropertyId)
                    .OrderBy(static item => item, StringComparer.Ordinal)
                    .ToArray();
                string[] expected =
                [
                    "fastPeriod",
                    "macd.histogram.stroke",
                    "macd.signal.stroke",
                    "macd.value.stroke",
                    "signalPeriod",
                    "slowPeriod"
                ];
                if (!propertyIds.SequenceEqual(expected))
                    throw new InvalidOperationException(
                        "App MACD property projection failed.");

                await RequireChangeAsync(
                    controller,
                    toggle.Owner.InstanceId,
                    "fastPeriod",
                    JsonValue.Create(5),
                    ChartChangeImpact.RecalculateModule);
                await RequireChangeAsync(
                    controller,
                    toggle.Owner.InstanceId,
                    "slowPeriod",
                    JsonValue.Create(13),
                    ChartChangeImpact.RecalculateModule);
                await RequireChangeAsync(
                    controller,
                    toggle.Owner.InstanceId,
                    "signalPeriod",
                    JsonValue.Create(4),
                    ChartChangeImpact.RecalculateModule);
                await RequireChangeAsync(
                    controller,
                    toggle.Owner.InstanceId,
                    MacdModule.HistogramObjectId + ".stroke",
                    JsonValue.Create("#ABCDEF"),
                    ChartChangeImpact.RedrawOnly);

                AssertPlan(
                    controller.RenderPlan,
                    primary,
                    5,
                    13,
                    4,
                    "changed",
                    "#ABCDEF");
                await controller.SaveCurrentAsync();
            }

            using (var restored = new ChartModulePlatformController(profilePath))
            {
                await restored.InitializeAsync(timeframe.ToString());
                ChartUiCommandItem toggle = restored.BuildUiCatalog()
                    .ContextMenuItems.Single(static item =>
                        item.Kind == ChartUiCommandKind.ModuleToggle &&
                        item.Owner.ModuleId == MacdModule.Definition.ModuleId);
                if (!toggle.IsChecked)
                    throw new InvalidOperationException(
                        "App MACD enabled state was not restored.");

                await restored.UpdatePrimarySeriesAsync(
                    primary,
                    viewportVersion: 1,
                    themeVersion: 0,
                    visibleStartIndex: 0,
                    visibleEndExclusive: primary.Bars.Count);
                AssertPlan(
                    restored.RenderPlan,
                    primary,
                    5,
                    13,
                    4,
                    "restored",
                    "#ABCDEF");
            }

            Console.WriteLine("csharp_app_macd_module_data=PASS");
            Console.WriteLine("csharp_app_macd_module_parameters=PASS");
            Console.WriteLine("csharp_app_macd_module_style=PASS");
            Console.WriteLine("csharp_app_macd_module_roundtrip=PASS");
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
                $"App MACD property mutation failed: {propertyId}.");
        }
    }

    private static ChartPrimarySeriesSnapshot CreatePrimarySeries()
    {
        var bars = new ChartPrimaryBar[120];
        double previous = 100d;
        for (int index = 0; index < bars.Length; index++)
        {
            double close = 100d + index * 0.18d +
                           Math.Sin(index / 4d) * 5d;
            bars[index] = new ChartPrimaryBar(
                index,
                previous,
                Math.Max(previous, close) + 1d,
                Math.Min(previous, close) - 1d,
                close,
                3_000L + index * 20L,
                true);
            previous = close;
        }
        return new ChartPrimarySeriesSnapshot(300, bars);
    }

    private static void AssertPlan(
        ChartRenderPlan plan,
        ChartPrimarySeriesSnapshot primary,
        int fast,
        int slow,
        int signalPeriod,
        string context,
        string? expectedHistogramStroke = null)
    {
        Dictionary<string, RenderPrimitivePlan> macd = plan.Primitives
            .Where(static item =>
                item.Identity.ModuleId == MacdModule.Definition.ModuleId)
            .ToDictionary(
                static item => item.Identity.ObjectId,
                StringComparer.Ordinal);
        if (macd.Count != 3)
            throw new InvalidOperationException(
                $"App MACD {context} primitive count mismatch.");

        IndicatorPoint expected = new MacdIndicator(fast, slow, signalPeriod)
            .Calculate(ToCandles(primary))[^1];
        Equal(
            expected.Value0,
            (float)macd[MacdModule.MacdObjectId].Points[^1].Y,
            $"App MACD {context} value");
        Equal(
            expected.Value1,
            (float)macd[MacdModule.SignalObjectId].Points[^1].Y,
            $"App MACD {context} signal");
        Equal(
            expected.Value2,
            (float)macd[MacdModule.HistogramObjectId].Points[^1].Y,
            $"App MACD {context} histogram");

        if (expectedHistogramStroke is not null &&
            macd[MacdModule.HistogramObjectId].Style.Stroke !=
                expectedHistogramStroke)
        {
            throw new InvalidOperationException(
                $"App MACD {context} style mismatch.");
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
