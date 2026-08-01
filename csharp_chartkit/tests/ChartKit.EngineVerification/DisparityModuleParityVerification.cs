using System.Reflection;
using System.Text.Json.Nodes;
using ChartKit.CSharp.Composition;
using ChartKit.CSharp.Contracts;
using ChartKit.CSharp.Engine;
using ChartKit.CSharp.ModuleHost;
using ChartKit.CSharp.Modules.Abstractions;
using ChartKit.CSharp.Modules.Indicators;
using ChartKit.CSharp.Scene;

namespace ChartKit.CSharp.EngineVerification;

internal static class DisparityModuleParityVerification
{
    public static void Run()
    {
        VerifyDefinitionAndMetadata();
        VerifyFullUpdateAppendAndRebuildParity();
        VerifyDisabledZeroAndContributions();
        VerifyParameterChange();
        VerifyReferenceBoundary();

#if RELEASE
        VerifyReleaseAssembly(typeof(DisparityModule).Assembly);
        Console.WriteLine("csharp_disparity_module_release_configuration=PASS");
#endif
        Console.WriteLine("csharp_disparity_module_definition=PASS");
        Console.WriteLine("csharp_disparity_module_metadata=PASS");
        Console.WriteLine("csharp_disparity_full_parity=PASS");
        Console.WriteLine("csharp_disparity_update_parity=PASS");
        Console.WriteLine("csharp_disparity_append_parity=PASS");
        Console.WriteLine("csharp_disparity_rebuild_parity=PASS");
        Console.WriteLine("csharp_disparity_disabled_zero=PASS");
        Console.WriteLine("csharp_disparity_contributions=PASS");
        Console.WriteLine("csharp_disparity_panel_contract=PASS");
        Console.WriteLine("csharp_disparity_style_override=PASS");
        Console.WriteLine("csharp_disparity_parameter_change=PASS");
        Console.WriteLine("csharp_disparity_reference_boundary=PASS");
        Console.WriteLine("csharp_disparity_module_contracts=PASS");
    }

    private static void VerifyDefinitionAndMetadata()
    {
        ChartModuleDefinition definition = DisparityModule.Definition;
        if (definition.ModuleId != "indicator.disparity" ||
            definition.Category != "Indicators" ||
            definition.DefaultPanelId != "indicator.6" ||
            definition.DefaultEnabled ||
            !definition.Capabilities.HasFlag(ChartModuleCapabilities.Computation) ||
            !definition.SupportedPrimitiveKinds.SequenceEqual(
                [ChartPrimitiveKind.Polyline]))
        {
            throw new InvalidOperationException(
                "Disparity module definition is incomplete.");
        }

        DisparityModule module = DisparityModule.Create("disparity-definition");
        module.Initialize(SystemChartModuleContext.Instance);
        module.ApplyProfile(CreateProfile("disparity-definition", 20, false));
        var properties = new PropertyWriter();
        module.DescribeProperties(properties);
        string[] actual = properties.Items
            .Select(static item => item.PropertyId)
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
        if (!actual.SequenceEqual(expected))
            throw new InvalidOperationException(
                "Disparity property metadata is incomplete.");
    }

    private static void VerifyFullUpdateAppendAndRebuildParity()
    {
        List<Candle> candles = Fixture.CreateCandles(260);
        var legacy = new DisparityIndicator(20);
        IReadOnlyList<IndicatorPoint> expected = legacy.Calculate(candles);

        DisparityModule module = CreateActiveModule("disparity-parity", 20);
        module.ApplyPrimarySeries(ToPrimary(candles, 1));
        AssertParity(expected, module.SnapshotValues(), "full");
        if (module.Diagnostics.FullCalculations != 1)
            throw new InvalidOperationException(
                "Disparity full path was not recorded.");

        Candle previousLast = candles[^1];
        candles[^1] = previousLast with
        {
            Close = previousLast.Close + 9f,
            IsFinal = false
        };
        IndicatorPoint expectedUpdate = legacy.UpdateLast(candles);
        module.ApplyPrimarySeries(ToPrimary(candles, 2));
        AssertPoint(expectedUpdate, module.SnapshotValues()[^1], "update");
        if (module.Diagnostics.LastUpdates != 1)
            throw new InvalidOperationException(
                "Disparity update path was not used.");

        Candle previous = candles[^1];
        candles.Add(new Candle(
            previous.CloseTime,
            previous.CloseTime.AddMinutes(1),
            previous.Close,
            previous.Close + 5f,
            previous.Close - 4f,
            previous.Close + 3f,
            previous.Volume + 100,
            true,
            previous.Sequence + 1));
        IndicatorPoint expectedAppend = legacy.UpdateLast(candles);
        module.ApplyPrimarySeries(ToPrimary(candles, 3));
        AssertPoint(expectedAppend, module.SnapshotValues()[^1], "append");
        if (module.Diagnostics.Appends != 1)
            throw new InvalidOperationException(
                "Disparity append path was not used.");

        List<Candle> rolling = candles.Skip(1).ToList();
        IReadOnlyList<IndicatorPoint> rollingExpected =
            new DisparityIndicator(20).Calculate(rolling);
        module.ApplyPrimarySeries(ToPrimary(rolling, 4));
        AssertParity(rollingExpected, module.SnapshotValues(), "rebuild");
        if (module.Diagnostics.FullCalculations != 2)
            throw new InvalidOperationException(
                "Disparity rebuild path was not used.");
    }

    private static void VerifyDisabledZeroAndContributions()
    {
        var registry = new ChartModuleRegistry();
        registry.Register<DisparityModule>();
        var host = new ChartModuleHost(registry);
        ChartModuleOperationResult hosted = host.UpsertProfile(
            CreateProfile("disparity-host", 20, false));
        if (!hosted.Succeeded)
            throw new InvalidOperationException(hosted.Error);

        List<Candle> candles = Fixture.CreateCandles(180);
        ChartModuleDataUpdateResult disabled =
            host.ApplyPrimarySeries(ToPrimary(candles, 10));
        if (disabled.EligibleModules != 0 || disabled.UpdatedModules != 0)
            throw new InvalidOperationException(
                "Disabled Disparity module performed calculation.");

        ChartModuleOperationResult enabled =
            host.SetEnabled("disparity-host", true);
        if (!enabled.Succeeded)
            throw new InvalidOperationException(enabled.Error);
        ChartModuleDataUpdateResult updated =
            host.ApplyPrimarySeries(ToPrimary(candles, 11));
        if (updated.EligibleModules != 1 ||
            updated.UpdatedModules != 1 ||
            updated.FaultedModules != 0)
        {
            throw new InvalidOperationException(
                "Enabled Disparity module did not consume data.");
        }

        ChartRenderPlan plan = new ChartCompositionService(host).Compose(
            new ChartVisualContext(11, 1, 1, 100, 180));
        if (plan.Primitives.Count != 4)
            throw new InvalidOperationException(
                "Disparity did not emit four primitives.");

        RenderPrimitivePlan value = Find(plan, DisparityModule.ValueObjectId);
        RenderPrimitivePlan upper = Find(plan, DisparityModule.UpperObjectId);
        RenderPrimitivePlan baseline = Find(plan, DisparityModule.BaselineObjectId);
        RenderPrimitivePlan lower = Find(plan, DisparityModule.LowerObjectId);
        if (plan.Primitives.Any(static item => item.PanelId != "indicator.6") ||
            value.Style.Stroke != "#00BFA5" ||
            upper.Style.Stroke != "#FFEB3B" ||
            baseline.Style.Stroke != "#7E57C2" ||
            lower.Style.Stroke != "#FFC107")
        {
            throw new InvalidOperationException(
                "Disparity contributions, panel or styles are incorrect.");
        }

        AssertConstant(upper, 105d, "upper");
        AssertConstant(baseline, 100d, "baseline");
        AssertConstant(lower, 95d, "lower");
    }

    private static void VerifyParameterChange()
    {
        List<Candle> candles = Fixture.CreateCandles(200);
        var registry = new ChartModuleRegistry();
        registry.Register<DisparityModule>();
        var host = new ChartModuleHost(registry);
        ChartModuleOperationResult initial = host.UpsertProfile(
            CreateProfile("disparity-parameter", 20, true));
        if (!initial.Succeeded)
            throw new InvalidOperationException(initial.Error);
        host.ApplyPrimarySeries(ToPrimary(candles, 20));

        ChartModuleOperationResult changed = host.UpsertProfile(
            CreateProfile(
                "disparity-parameter",
                7,
                true,
                upper: 108d,
                baseline: 101d,
                lower: 94d,
                valueStroke: "#112233",
                upperStroke: "#223344",
                baselineStroke: "#334455",
                lowerStroke: "#445566"));
        if (!changed.Succeeded || !changed.Changed)
            throw new InvalidOperationException(
                changed.Error ?? "Disparity parameter profile did not change.");
        host.ApplyPrimarySeries(ToPrimary(candles, 21));

        ChartRenderPlan plan = new ChartCompositionService(host).Compose(
            new ChartVisualContext(21, 1, 1, 100, 200));
        IReadOnlyList<IndicatorPoint> expected =
            new DisparityIndicator(7).Calculate(candles);
        AssertPlanParity(expected, plan);

        RenderPrimitivePlan value = Find(plan, DisparityModule.ValueObjectId);
        RenderPrimitivePlan upper = Find(plan, DisparityModule.UpperObjectId);
        RenderPrimitivePlan baseline = Find(plan, DisparityModule.BaselineObjectId);
        RenderPrimitivePlan lower = Find(plan, DisparityModule.LowerObjectId);
        if (value.Style.Stroke != "#112233" ||
            upper.Style.Stroke != "#223344" ||
            baseline.Style.Stroke != "#334455" ||
            lower.Style.Stroke != "#445566")
        {
            throw new InvalidOperationException(
                "Disparity object-specific styles were not applied.");
        }
        AssertConstant(upper, 108d, "changed upper");
        AssertConstant(baseline, 101d, "changed baseline");
        AssertConstant(lower, 94d, "changed lower");
    }

    private static DisparityModule CreateActiveModule(
        string instanceId,
        int period)
    {
        DisparityModule module = DisparityModule.Create(instanceId);
        module.Initialize(SystemChartModuleContext.Instance);
        module.ApplyProfile(CreateProfile(instanceId, period, true));
        module.Activate();
        return module;
    }

    private static ChartModuleProfile CreateProfile(
        string instanceId,
        int period,
        bool enabled,
        double upper = 105d,
        double baseline = 100d,
        double lower = 95d,
        string valueStroke = "#00BFA5",
        string upperStroke = "#FFEB3B",
        string baselineStroke = "#7E57C2",
        string lowerStroke = "#FFC107") =>
        new()
        {
            ModuleId = DisparityModule.Definition.ModuleId,
            InstanceId = instanceId,
            ModuleSchemaVersion = DisparityModule.Definition.SchemaVersion,
            IsEnabled = enabled,
            ZIndex = 10,
            Placement = "indicator.6",
            Parameters = new JsonObject
            {
                ["period"] = period,
                ["upper"] = upper,
                ["baseline"] = baseline,
                ["lower"] = lower
            },
            Style = new JsonObject
            {
                [DisparityModule.ValueObjectId + ".stroke"] = valueStroke,
                [DisparityModule.UpperObjectId + ".stroke"] = upperStroke,
                [DisparityModule.BaselineObjectId + ".stroke"] = baselineStroke,
                [DisparityModule.LowerObjectId + ".stroke"] = lowerStroke
            },
            PersistentState = new JsonObject()
        };

    private static ChartPrimarySeriesSnapshot ToPrimary(
        IReadOnlyList<Candle> candles,
        long dataVersion)
    {
        var bars = new ChartPrimaryBar[candles.Count];
        for (int index = 0; index < bars.Length; index++)
        {
            Candle candle = candles[index];
            bars[index] = new ChartPrimaryBar(
                candle.Sequence,
                candle.Open,
                candle.High,
                candle.Low,
                candle.Close,
                candle.Volume,
                candle.IsFinal);
        }
        return new ChartPrimarySeriesSnapshot(dataVersion, bars);
    }

    private static void AssertParity(
        IReadOnlyList<IndicatorPoint> expected,
        IReadOnlyList<DisparityValuePoint> actual,
        string context)
    {
        if (expected.Count != actual.Count)
            throw new InvalidOperationException(
                $"Disparity {context} count mismatch.");
        for (int index = 0; index < expected.Count; index++)
        {
            if (expected[index].Sequence != actual[index].Sequence)
                throw new InvalidOperationException(
                    $"Disparity {context} sequence mismatch at {index}.");
            AssertPoint(expected[index], actual[index], $"{context} index={index}");
        }
    }

    private static void AssertPoint(
        IndicatorPoint expected,
        DisparityValuePoint actual,
        string context)
    {
        Fixture.Equal(
            expected.Value0,
            actual.Value,
            "Disparity " + context + " value");
        Fixture.Equal(
            expected.Value1,
            actual.MovingAverage,
            "Disparity " + context + " moving average");
        Fixture.Equal(105f, expected.Value2, "Disparity legacy upper");
        Fixture.Equal(100f, expected.Value3, "Disparity legacy baseline");
        Fixture.Equal(95f, expected.Value4, "Disparity legacy lower");
    }

    private static void AssertPlanParity(
        IReadOnlyList<IndicatorPoint> expected,
        ChartRenderPlan plan)
    {
        RenderPrimitivePlan value = Find(plan, DisparityModule.ValueObjectId);
        for (int index = 0; index < expected.Count; index++)
        {
            Fixture.Equal(
                expected[index].Value0,
                (float)value.Points[index].Y,
                "Disparity parameter value");
        }
    }

    private static RenderPrimitivePlan Find(
        ChartRenderPlan plan,
        string objectId) =>
        plan.Primitives.Single(item => item.Identity.ObjectId == objectId);

    private static void AssertConstant(
        RenderPrimitivePlan primitive,
        double expected,
        string context)
    {
        if (primitive.Points.Any(point => point.Y != expected))
            throw new InvalidOperationException(
                $"Disparity {context} reference line mismatch.");
    }

    private static void VerifyReferenceBoundary()
    {
        Assembly assembly = typeof(DisparityModule).Assembly;
        string[] references = assembly.GetReferencedAssemblies()
            .Select(static item => item.Name ?? string.Empty)
            .ToArray();
        string[] forbidden =
        [
            "ChartKit.Engine",
            "ChartKit.Rendering",
            "ChartKit.DataSources",
            "ChartKit.App",
            "SkiaSharp",
            "System.Windows.Forms"
        ];
        foreach (string name in forbidden)
        {
            if (references.Contains(name, StringComparer.Ordinal))
                throw new InvalidOperationException(
                    $"Indicator modules have forbidden reference: {name}");
        }
    }

    private static void VerifyReleaseAssembly(Assembly assembly)
    {
        string? configuration = assembly
            .GetCustomAttribute<AssemblyConfigurationAttribute>()?
            .Configuration;
        if (!StringComparer.Ordinal.Equals(configuration, "Release"))
            throw new InvalidOperationException(
                "Disparity module assembly is not Release.");
    }

    private sealed class PropertyWriter : IChartPropertyWriter
    {
        private readonly List<ChartPropertyDescriptor> _items = new();
        public IReadOnlyList<ChartPropertyDescriptor> Items => _items;
        public void Add(ChartPropertyDescriptor descriptor) => _items.Add(descriptor);
    }
}
