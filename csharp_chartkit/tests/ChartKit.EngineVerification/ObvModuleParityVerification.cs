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

internal static class ObvModuleParityVerification
{
    public static void Run()
    {
        VerifyDefinitionAndMetadata();
        VerifyFullUpdateAppendAndRebuildParity();
        VerifyDisabledZeroAndContributions();
        VerifyParameterChange();
        VerifyReferenceBoundary();

#if RELEASE
        VerifyReleaseAssembly(typeof(ObvModule).Assembly);
        Console.WriteLine("csharp_obv_module_release_configuration=PASS");
#endif
        Console.WriteLine("csharp_obv_module_definition=PASS");
        Console.WriteLine("csharp_obv_module_metadata=PASS");
        Console.WriteLine("csharp_obv_full_parity=PASS");
        Console.WriteLine("csharp_obv_update_parity=PASS");
        Console.WriteLine("csharp_obv_append_parity=PASS");
        Console.WriteLine("csharp_obv_rebuild_parity=PASS");
        Console.WriteLine("csharp_obv_disabled_zero=PASS");
        Console.WriteLine("csharp_obv_contributions=PASS");
        Console.WriteLine("csharp_obv_panel_contract=PASS");
        Console.WriteLine("csharp_obv_style_override=PASS");
        Console.WriteLine("csharp_obv_parameter_change=PASS");
        Console.WriteLine("csharp_obv_reference_boundary=PASS");
        Console.WriteLine("csharp_obv_module_contracts=PASS");
    }

    private static void VerifyDefinitionAndMetadata()
    {
        ChartModuleDefinition definition = ObvModule.Definition;
        if (definition.ModuleId != "indicator.obv" ||
            definition.Category != "Indicators" ||
            definition.DefaultPanelId != "indicator.5" ||
            definition.DefaultEnabled ||
            !definition.Capabilities.HasFlag(ChartModuleCapabilities.Computation) ||
            !definition.SupportedPrimitiveKinds.SequenceEqual(
                [ChartPrimitiveKind.Polyline]))
        {
            throw new InvalidOperationException(
                "OBV module definition is incomplete.");
        }

        ObvModule module = ObvModule.Create("obv-definition");
        module.Initialize(SystemChartModuleContext.Instance);
        module.ApplyProfile(CreateProfile("obv-definition", 20, false));
        var properties = new PropertyWriter();
        module.DescribeProperties(properties);
        string[] actual = properties.Items
            .Select(static item => item.PropertyId)
            .OrderBy(static item => item, StringComparer.Ordinal)
            .ToArray();
        string[] expected =
        [
            "obv.signal.stroke",
            "obv.value.stroke",
            "signalPeriod"
        ];
        if (!actual.SequenceEqual(expected))
            throw new InvalidOperationException(
                "OBV property metadata is incomplete.");
    }

    private static void VerifyFullUpdateAppendAndRebuildParity()
    {
        List<Candle> candles = Fixture.CreateCandles(260);
        var legacy = new ObvIndicator(20);
        IReadOnlyList<IndicatorPoint> expected = legacy.Calculate(candles);

        ObvModule module = CreateActiveModule("obv-parity", 20);
        module.ApplyPrimarySeries(ToPrimary(candles, 1));
        AssertParity(expected, module.SnapshotValues(), "full");
        if (module.Diagnostics.FullCalculations != 1)
            throw new InvalidOperationException(
                "OBV full path was not recorded.");

        Candle previousLast = candles[^1];
        candles[^1] = previousLast with
        {
            Close = previousLast.Close + 11f,
            Volume = previousLast.Volume + 777,
            IsFinal = false
        };
        IndicatorPoint expectedUpdate = legacy.UpdateLast(candles);
        module.ApplyPrimarySeries(ToPrimary(candles, 2));
        AssertPoint(expectedUpdate, module.SnapshotValues()[^1], "update");
        if (module.Diagnostics.LastUpdates != 1)
            throw new InvalidOperationException(
                "OBV update path was not used.");

        Candle previous = candles[^1];
        candles.Add(new Candle(
            previous.CloseTime,
            previous.CloseTime.AddMinutes(1),
            previous.Close,
            previous.Close + 4f,
            previous.Close - 3f,
            previous.Close - 2f,
            previous.Volume + 321,
            true,
            previous.Sequence + 1));
        IndicatorPoint expectedAppend = legacy.UpdateLast(candles);
        module.ApplyPrimarySeries(ToPrimary(candles, 3));
        AssertPoint(expectedAppend, module.SnapshotValues()[^1], "append");
        if (module.Diagnostics.Appends != 1)
            throw new InvalidOperationException(
                "OBV append path was not used.");

        List<Candle> rolling = candles.Skip(1).ToList();
        IReadOnlyList<IndicatorPoint> rollingExpected =
            new ObvIndicator(20).Calculate(rolling);
        module.ApplyPrimarySeries(ToPrimary(rolling, 4));
        AssertParity(rollingExpected, module.SnapshotValues(), "rebuild");
        if (module.Diagnostics.FullCalculations != 2)
            throw new InvalidOperationException(
                "OBV rebuild path was not used.");
    }

    private static void VerifyDisabledZeroAndContributions()
    {
        var registry = new ChartModuleRegistry();
        registry.Register<ObvModule>();
        var host = new ChartModuleHost(registry);
        ChartModuleOperationResult hosted = host.UpsertProfile(
            CreateProfile("obv-host", 20, false));
        if (!hosted.Succeeded)
            throw new InvalidOperationException(hosted.Error);

        List<Candle> candles = Fixture.CreateCandles(180);
        ChartModuleDataUpdateResult disabled =
            host.ApplyPrimarySeries(ToPrimary(candles, 10));
        if (disabled.EligibleModules != 0 || disabled.UpdatedModules != 0)
            throw new InvalidOperationException(
                "Disabled OBV module performed calculation.");

        ChartModuleOperationResult enabled = host.SetEnabled("obv-host", true);
        if (!enabled.Succeeded)
            throw new InvalidOperationException(enabled.Error);
        ChartModuleDataUpdateResult updated =
            host.ApplyPrimarySeries(ToPrimary(candles, 11));
        if (updated.EligibleModules != 1 ||
            updated.UpdatedModules != 1 ||
            updated.FaultedModules != 0)
        {
            throw new InvalidOperationException(
                "Enabled OBV module did not consume data.");
        }

        ChartRenderPlan plan = new ChartCompositionService(host).Compose(
            new ChartVisualContext(11, 1, 1, 100, 180));
        if (plan.Primitives.Count != 2)
            throw new InvalidOperationException(
                "OBV did not emit two primitives.");

        RenderPrimitivePlan obv = plan.Primitives.Single(
            static item => item.Identity.ObjectId == ObvModule.ObvObjectId);
        RenderPrimitivePlan signal = plan.Primitives.Single(
            static item => item.Identity.ObjectId == ObvModule.SignalObjectId);
        if (obv.PrimitiveKind != ChartPrimitiveKind.Polyline ||
            signal.PrimitiveKind != ChartPrimitiveKind.Polyline ||
            obv.PanelId != "indicator.5" ||
            signal.PanelId != "indicator.5" ||
            obv.Style.Stroke != "#7E57C2" ||
            signal.Style.Stroke != "#FFC107")
        {
            throw new InvalidOperationException(
                "OBV contributions, panel or styles are incorrect.");
        }
    }

    private static void VerifyParameterChange()
    {
        List<Candle> candles = Fixture.CreateCandles(200);
        var registry = new ChartModuleRegistry();
        registry.Register<ObvModule>();
        var host = new ChartModuleHost(registry);
        ChartModuleOperationResult initial = host.UpsertProfile(
            CreateProfile("obv-parameter", 20, true));
        if (!initial.Succeeded)
            throw new InvalidOperationException(initial.Error);
        host.ApplyPrimarySeries(ToPrimary(candles, 20));

        ChartModuleOperationResult changed = host.UpsertProfile(
            CreateProfile(
                "obv-parameter",
                7,
                true,
                obvStroke: "#112233",
                signalStroke: "#445566"));
        if (!changed.Succeeded || !changed.Changed)
            throw new InvalidOperationException(
                changed.Error ?? "OBV parameter profile did not change.");
        host.ApplyPrimarySeries(ToPrimary(candles, 21));

        ChartRenderPlan plan = new ChartCompositionService(host).Compose(
            new ChartVisualContext(21, 1, 1, 100, 200));
        IReadOnlyList<IndicatorPoint> expected =
            new ObvIndicator(7).Calculate(candles);
        AssertPlanParity(expected, plan);

        RenderPrimitivePlan obv = plan.Primitives.Single(
            static item => item.Identity.ObjectId == ObvModule.ObvObjectId);
        RenderPrimitivePlan signal = plan.Primitives.Single(
            static item => item.Identity.ObjectId == ObvModule.SignalObjectId);
        if (obv.Style.Stroke != "#112233" ||
            signal.Style.Stroke != "#445566")
        {
            throw new InvalidOperationException(
                "OBV object-specific styles were not applied.");
        }
    }

    private static ObvModule CreateActiveModule(
        string instanceId,
        int signalPeriod)
    {
        ObvModule module = ObvModule.Create(instanceId);
        module.Initialize(SystemChartModuleContext.Instance);
        module.ApplyProfile(CreateProfile(
            instanceId,
            signalPeriod,
            true));
        module.Activate();
        return module;
    }

    private static ChartModuleProfile CreateProfile(
        string instanceId,
        int signalPeriod,
        bool enabled,
        string obvStroke = "#7E57C2",
        string signalStroke = "#FFC107") =>
        new()
        {
            ModuleId = ObvModule.Definition.ModuleId,
            InstanceId = instanceId,
            ModuleSchemaVersion = ObvModule.Definition.SchemaVersion,
            IsEnabled = enabled,
            ZIndex = 10,
            Placement = "indicator.5",
            Parameters = new JsonObject
            {
                ["signalPeriod"] = signalPeriod
            },
            Style = new JsonObject
            {
                [ObvModule.ObvObjectId + ".stroke"] = obvStroke,
                [ObvModule.SignalObjectId + ".stroke"] = signalStroke
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
        IReadOnlyList<ObvValuePoint> actual,
        string context)
    {
        if (expected.Count != actual.Count)
            throw new InvalidOperationException(
                $"OBV {context} count mismatch.");
        for (int index = 0; index < expected.Count; index++)
        {
            if (expected[index].Sequence != actual[index].Sequence)
                throw new InvalidOperationException(
                    $"OBV {context} sequence mismatch at {index}.");
            AssertPoint(expected[index], actual[index], $"{context} index={index}");
        }
    }

    private static void AssertPoint(
        IndicatorPoint expected,
        ObvValuePoint actual,
        string context)
    {
        Fixture.Equal(expected.Value0, actual.Obv, "OBV " + context + " value");
        Fixture.Equal(expected.Value1, actual.Signal, "OBV " + context + " signal");
        Fixture.Equal(expected.Value2, actual.Direction, "OBV " + context + " direction");
    }

    private static void AssertPlanParity(
        IReadOnlyList<IndicatorPoint> expected,
        ChartRenderPlan plan)
    {
        RenderPrimitivePlan obv = plan.Primitives.Single(
            static item => item.Identity.ObjectId == ObvModule.ObvObjectId);
        RenderPrimitivePlan signal = plan.Primitives.Single(
            static item => item.Identity.ObjectId == ObvModule.SignalObjectId);
        for (int index = 0; index < expected.Count; index++)
        {
            Fixture.Equal(
                expected[index].Value0,
                (float)obv.Points[index].Y,
                "OBV parameter value");
            Fixture.Equal(
                expected[index].Value1,
                (float)signal.Points[index].Y,
                "OBV parameter signal");
        }
    }

    private static void VerifyReferenceBoundary()
    {
        Assembly assembly = typeof(ObvModule).Assembly;
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
                "OBV module assembly is not Release.");
    }

    private sealed class PropertyWriter : IChartPropertyWriter
    {
        private readonly List<ChartPropertyDescriptor> _items = new();
        public IReadOnlyList<ChartPropertyDescriptor> Items => _items;
        public void Add(ChartPropertyDescriptor descriptor) => _items.Add(descriptor);
    }
}
