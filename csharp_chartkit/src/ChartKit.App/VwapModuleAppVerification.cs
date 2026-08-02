using System.Text.Json.Nodes;
using ChartKit.CSharp.Contracts;
using ChartKit.CSharp.Engine;
using ChartKit.CSharp.Modules.Abstractions;
using ChartKit.CSharp.Modules.Indicators;
using ChartKit.CSharp.Scene;
using ChartKit.CSharp.UiModel;

namespace ChartKit.CSharp.App;

internal static class VwapModuleAppVerification
{
    public static async Task RunAsync(CandleTimeframe timeframe)
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "chartkit-app-vwap-self-test-" + Guid.NewGuid().ToString("N"));
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
                        item.Owner.ModuleId == VwapModule.Definition.ModuleId);
                ChartModulePlatformActionResult enabled =
                    await controller.ExecuteCommandAsync(toggle);
                if (!enabled.Succeeded || !enabled.Changed)
                    throw new InvalidOperationException(
                        "App VWAP toggle failed.");

                await controller.UpdatePrimarySeriesAsync(
                    primary,
                    viewportVersion: 1,
                    themeVersion: 0,
                    visibleStartIndex: 0,
                    visibleEndExclusive: primary.Bars.Count);
                AssertPlan(
                    controller.RenderPlan,
                    primary,
                    stdDev1: 1f,
                    stdDev2: 2f,
                    context: "default");

                controller.Select(toggle.Owner);
                string[] propertyIds = controller.BuildUiCatalog()
                    .InspectorProperties
                    .Select(static item => item.Descriptor.PropertyId)
                    .OrderBy(static item => item, StringComparer.Ordinal)
                    .ToArray();
                string[] expected =
                [
                    "stdDev1",
                    "stdDev2",
                    "vwap.lower1.stroke",
                    "vwap.lower2.stroke",
                    "vwap.upper1.stroke",
                    "vwap.upper2.stroke",
                    "vwap.value.stroke"
                ];
                if (!propertyIds.SequenceEqual(expected))
                    throw new InvalidOperationException(
                        "App VWAP property projection failed.");

                await RequireChangeAsync(
                    controller,
                    toggle.Owner.InstanceId,
                    "stdDev1",
                    JsonValue.Create(1.5d),
                    ChartChangeImpact.RecalculateModule);
                await RequireChangeAsync(
                    controller,
                    toggle.Owner.InstanceId,
                    "stdDev2",
                    JsonValue.Create(3d),
                    ChartChangeImpact.RecalculateModule);
                await RequireChangeAsync(
                    controller,
                    toggle.Owner.InstanceId,
                    VwapModule.ValueObjectId + ".stroke",
                    JsonValue.Create("#ABCDEF"),
                    ChartChangeImpact.RedrawOnly);

                AssertPlan(
                    controller.RenderPlan,
                    primary,
                    stdDev1: 1.5f,
                    stdDev2: 3f,
                    context: "changed",
                    expectedValueStroke: "#ABCDEF");
                await controller.SaveCurrentAsync();
            }

            using (var restored = new ChartModulePlatformController(profilePath))
            {
                await restored.InitializeAsync(timeframe.ToString());
                ChartUiCommandItem toggle = restored.BuildUiCatalog()
                    .ContextMenuItems.Single(static item =>
                        item.Kind == ChartUiCommandKind.ModuleToggle &&
                        item.Owner.ModuleId == VwapModule.Definition.ModuleId);
                if (!toggle.IsChecked)
                    throw new InvalidOperationException(
                        "App VWAP enabled state was not restored.");

                await restored.UpdatePrimarySeriesAsync(
                    primary,
                    viewportVersion: 1,
                    themeVersion: 0,
                    visibleStartIndex: 0,
                    visibleEndExclusive: primary.Bars.Count);
                AssertPlan(
                    restored.RenderPlan,
                    primary,
                    stdDev1: 1.5f,
                    stdDev2: 3f,
                    context: "restored",
                    expectedValueStroke: "#ABCDEF");
            }

            Console.WriteLine("csharp_app_vwap_module_data=PASS");
            Console.WriteLine("csharp_app_vwap_module_parameters=PASS");
            Console.WriteLine("csharp_app_vwap_module_style=PASS");
            Console.WriteLine("csharp_app_vwap_panel_contract=PASS");
            Console.WriteLine("csharp_app_vwap_session_reset=PASS");
            Console.WriteLine("csharp_app_vwap_module_roundtrip=PASS");
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
                $"App VWAP property mutation failed: {propertyId}.");
        }
    }

    private static ChartPrimarySeriesSnapshot CreatePrimarySeries()
    {
        const int days = 3;
        const int barsPerDay = 40;
        var bars = new ChartPrimaryBar[days * barsPerDay];
        DateOnly firstDate = new(2026, 7, 29);
        double previous = 100d;
        long sequence = 1;
        int output = 0;
        for (int day = 0; day < days; day++)
        {
            DateOnly tradingDate = firstDate.AddDays(day);
            for (int index = 0; index < barsPerDay; index++)
            {
                double close = 100d + day * 4d +
                    Math.Sin((day * barsPerDay + index) / 6d) * 5d +
                    index * 0.05d;
                long volume = day == 2 && index == 0
                    ? 0L
                    : 1_000L + day * 500L + index * 23L;
                bars[output++] = new ChartPrimaryBar(
                    sequence++,
                    tradingDate,
                    previous,
                    Math.Max(previous, close) + 1.5d,
                    Math.Min(previous, close) - 1.25d,
                    close,
                    volume,
                    true);
                previous = close;
            }
        }
        return new ChartPrimarySeriesSnapshot(800, bars);
    }

    private static void AssertPlan(
        ChartRenderPlan plan,
        ChartPrimarySeriesSnapshot primary,
        float stdDev1,
        float stdDev2,
        string context,
        string? expectedValueStroke = null)
    {
        Dictionary<string, RenderPrimitivePlan> series = plan.Primitives
            .Where(static item =>
                item.Identity.ModuleId == VwapModule.Definition.ModuleId)
            .ToDictionary(
                static item => item.Identity.ObjectId,
                StringComparer.Ordinal);
        if (series.Count != 5)
            throw new InvalidOperationException(
                $"App VWAP {context} primitive count mismatch.");
        if (series.Values.Any(static item => item.PanelId != "price.main"))
            throw new InvalidOperationException(
                $"App VWAP {context} panel mismatch.");

        IReadOnlyList<IndicatorPoint> expected =
            new VwapIndicator(stdDev1, stdDev2).Calculate(ToCandles(primary));
        string[] objectIds =
        [
            VwapModule.ValueObjectId,
            VwapModule.Upper1ObjectId,
            VwapModule.Lower1ObjectId,
            VwapModule.Upper2ObjectId,
            VwapModule.Lower2ObjectId
        ];
        for (int index = 0; index < expected.Count; index++)
        {
            for (int valueIndex = 0; valueIndex < objectIds.Length; valueIndex++)
            {
                Equal(
                    expected[index].GetValue(valueIndex),
                    (float)series[objectIds[valueIndex]].Points[index].Y,
                    $"App VWAP {context} value={valueIndex} index={index}");
            }
        }

        int secondSessionStart = 40;
        float expectedSecondOpen = expected[secondSessionStart].Value0;
        float typicalSecondOpen = (float)(
            (primary.Bars[secondSessionStart].High +
             primary.Bars[secondSessionStart].Low +
             primary.Bars[secondSessionStart].Close) / 3d);
        Equal(
            typicalSecondOpen,
            expectedSecondOpen,
            $"App VWAP {context} second-session reset");

        int zeroVolumeSessionStart = 80;
        if (!float.IsNaN(expected[zeroVolumeSessionStart].Value0) ||
            !float.IsNaN(
                (float)series[VwapModule.ValueObjectId]
                    .Points[zeroVolumeSessionStart].Y))
        {
            throw new InvalidOperationException(
                $"App VWAP {context} zero-volume session reset mismatch.");
        }

        if (expectedValueStroke is not null &&
            series[VwapModule.ValueObjectId].Style.Stroke != expectedValueStroke)
        {
            throw new InvalidOperationException(
                $"App VWAP {context} style mismatch.");
        }
    }

    private static IReadOnlyList<Candle> ToCandles(
        ChartPrimarySeriesSnapshot primary)
    {
        var candles = new Candle[primary.Bars.Count];
        DateOnly currentDate = DateOnly.MinValue;
        int minuteIndex = 0;
        for (int index = 0; index < candles.Length; index++)
        {
            ChartPrimaryBar bar = primary.Bars[index];
            if (bar.TradingDate != currentDate)
            {
                currentDate = bar.TradingDate;
                minuteIndex = 0;
            }

            DateTime openTime = currentDate
                .ToDateTime(new TimeOnly(9, 0))
                .AddMinutes(minuteIndex++);
            candles[index] = new Candle(
                openTime,
                openTime.AddMinutes(1),
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
