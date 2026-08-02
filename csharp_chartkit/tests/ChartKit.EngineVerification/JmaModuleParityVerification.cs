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

internal static class JmaModuleParityVerification
{
    public static void Run()
    {
        VerifyDefinitionAndMetadata();
        VerifyFullUpdateAppendAndRebuildParity();
        VerifyDisabledZeroAndContributions();
        VerifyParameterChange();
        VerifyReferenceBoundary();

#if RELEASE
        VerifyReleaseAssembly(typeof(JmaModule).Assembly);
        Console.WriteLine("csharp_jma_module_release_configuration=PASS");
#endif
        Console.WriteLine("csharp_jma_module_definition=PASS");
        Console.WriteLine("csharp_jma_module_metadata=PASS");
        Console.WriteLine("csharp_jma_full_parity=PASS");
        Console.WriteLine("csharp_jma_update_parity=PASS");
        Console.WriteLine("csharp_jma_append_parity=PASS");
        Console.WriteLine("csharp_jma_rebuild_parity=PASS");
        Console.WriteLine("csharp_jma_disabled_zero=PASS");
        Console.WriteLine("csharp_jma_contributions=PASS");
        Console.WriteLine("csharp_jma_panel_contract=PASS");
        Console.WriteLine("csharp_jma_style_override=PASS");
        Console.WriteLine("csharp_jma_parameter_change=PASS");
        Console.WriteLine("csharp_jma_reference_boundary=PASS");
        Console.WriteLine("csharp_jma_module_contracts=PASS");
    }

    private static void VerifyDefinitionAndMetadata()
    {
        ChartModuleDefinition definition = JmaModule.Definition;
        if (definition.ModuleId != "indicator.jma" ||
            definition.Category != "Indicators" ||
            definition.DefaultPanelId != "price.main" ||
            definition.DefaultEnabled ||
            !definition.Capabilities.HasFlag(ChartModuleCapabilities.Computation) ||
            !definition.SupportedPrimitiveKinds.SequenceEqual(
                [ChartPrimitiveKind.Polyline]))
        {
            throw new InvalidOperationException(
                "JMA module definition is incomplete.");
        }

        JmaModule module = JmaModule.Create("jma-definition");
        module.Initialize(SystemChartModuleContext.Instance);
        module.ApplyProfile(CreateProfile(
            "jma-definition",
            14,
            50,
            2,
            false));
        var properties = new PropertyWriter();
        module.DescribeProperties(properties);
        string[] actual = properties.Items
            .Select(static item => item.PropertyId)
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
        if (!actual.SequenceEqual(expected))
            throw new InvalidOperationException(
                "JMA property metadata is incomplete.");
    }

    private static void VerifyFullUpdateAppendAndRebuildParity()
    {
        List<Candle> candles = Fixture.CreateCandles(260);
        var legacy = new JmaIndicator(14, 50, 2);
        IReadOnlyList<IndicatorPoint> expected = legacy.Calculate(candles);

        JmaModule module = CreateActiveModule("jma-parity", 14, 50, 2);
        module.ApplyPrimarySeries(ToPrimary(candles, 1));
        AssertParity(expected, module.SnapshotValues(), "full");
        if (module.Diagnostics.FullCalculations != 1)
            throw new InvalidOperationException("JMA full path was not recorded.");

        Candle last = candles[^1];
        candles[^1] = last with
        {
            Close = last.Close + 9f,
            High = last.High + 9f,
            IsFinal = false
        };
        IndicatorPoint expectedUpdate = legacy.UpdateLast(candles);
        module.ApplyPrimarySeries(ToPrimary(candles, 2));
        AssertPoint(expectedUpdate, module.SnapshotValues()[^1], "update");
        if (module.Diagnostics.LastUpdates != 1)
            throw new InvalidOperationException("JMA update path was not used.");

        Candle previous = candles[^1];
        candles.Add(new Candle(
            previous.CloseTime,
            previous.CloseTime.AddMinutes(1),
            previous.Close,
            previous.Close + 4f,
            previous.Close - 3f,
            previous.Close - 6f,
            previous.Volume + 100,
            true,
            previous.Sequence + 1));
        IndicatorPoint expectedAppend = legacy.UpdateLast(candles);
        module.ApplyPrimarySeries(ToPrimary(candles, 3));
        AssertPoint(expectedAppend, module.SnapshotValues()[^1], "append");
        if (module.Diagnostics.Appends != 1)
            throw new InvalidOperationException("JMA append path was not used.");

        List<Candle> rolling = candles.Skip(1).ToList();
        IReadOnlyList<IndicatorPoint> rollingExpected =
            new JmaIndicator(14, 50, 2).Calculate(rolling);
        module.ApplyPrimarySeries(ToPrimary(rolling, 4));
        AssertParity(rollingExpected, module.SnapshotValues(), "rebuild");
        if (module.Diagnostics.FullCalculations != 2)
            throw new InvalidOperationException("JMA rebuild path was not used.");
    }

    private static void VerifyDisabledZeroAndContributions()
    {
        var registry = new ChartModuleRegistry();
        registry.Register<JmaModule>();
        var host = new ChartModuleHost(registry);
        ChartModuleOperationResult hosted = host.UpsertProfile(
            CreateProfile("jma-host", 14, 50, 2, false));
        if (!hosted.Succeeded)
            throw new InvalidOperationException(hosted.Error);

        List<Candle> candles = Fixture.CreateCandles(180);
        ChartModuleDataUpdateResult disabled =
            host.ApplyPrimarySeries(ToPrimary(candles, 10));
        if (disabled.EligibleModules != 0 || disabled.UpdatedModules != 0)
            throw new InvalidOperationException(
                "Disabled JMA module performed calculation.");

        ChartModuleOperationResult enabled = host.SetEnabled("jma-host", true);
        if (!enabled.Succeeded)
            throw new InvalidOperationException(enabled.Error);
        ChartModuleDataUpdateResult updated =
            host.ApplyPrimarySeries(ToPrimary(candles, 11));
        if (updated.EligibleModules != 1 ||
            updated.UpdatedModules != 1 ||
            updated.FaultedModules != 0)
        {
            throw new InvalidOperationException(
                "Enabled JMA module did not consume data.");
        }

        ChartRenderPlan plan = new ChartCompositionService(host).Compose(
            new ChartVisualContext(11, 1, 1, 100, 180));
        if (plan.Primitives.Count != 2)
            throw new InvalidOperationException(
                "JMA did not emit two direction primitives.");

        RenderPrimitivePlan up = plan.Primitives.Single(
            static item => item.Identity.ObjectId == JmaModule.UpObjectId);
        RenderPrimitivePlan down = plan.Primitives.Single(
            static item => item.Identity.ObjectId == JmaModule.DownObjectId);
        if (up.PrimitiveKind != ChartPrimitiveKind.Polyline ||
            down.PrimitiveKind != ChartPrimitiveKind.Polyline ||
            up.PanelId != "price.main" ||
            down.PanelId != "price.main" ||
            up.Style.Stroke != "#AB47BC" ||
            down.Style.Stroke != "#00C853")
        {
            throw new InvalidOperationException(
                "JMA contributions, panel or styles are incorrect.");
        }
    }

    private static void VerifyParameterChange()
    {
        List<Candle> candles = Fixture.CreateCandles(200);
        var registry = new ChartModuleRegistry();
        registry.Register<JmaModule>();
        var host = new ChartModuleHost(registry);
        ChartModuleOperationResult initial = host.UpsertProfile(
            CreateProfile("jma-parameter", 14, 50, 2, true));
        if (!initial.Succeeded)
            throw new InvalidOperationException(initial.Error);
        host.ApplyPrimarySeries(ToPrimary(candles, 20));

        ChartModuleOperationResult changed = host.UpsertProfile(
            CreateProfile(
                "jma-parameter",
                7,
                -25,
                3,
                true,
                upStroke: "#112233",
                downStroke: "#445566"));
        if (!changed.Succeeded || !changed.Changed)
            throw new InvalidOperationException(
                changed.Error ?? "JMA parameter profile did not change.");
        host.ApplyPrimarySeries(ToPrimary(candles, 21));

        ChartRenderPlan plan = new ChartCompositionService(host).Compose(
            new ChartVisualContext(21, 1, 1, 100, 200));
        IReadOnlyList<IndicatorPoint> expected =
            new JmaIndicator(7, -25, 3).Calculate(candles);
        AssertPlanParity(expected, plan);
        RenderPrimitivePlan up = plan.Primitives.Single(
            static item => item.Identity.ObjectId == JmaModule.UpObjectId);
        RenderPrimitivePlan down = plan.Primitives.Single(
            static item => item.Identity.ObjectId == JmaModule.DownObjectId);
        if (up.Style.Stroke != "#112233" || down.Style.Stroke != "#445566")
            throw new InvalidOperationException(
                "JMA object-specific styles were not applied.");
    }

    private static JmaModule CreateActiveModule(
        string instanceId,
        int period,
        int phase,
        int power)
    {
        JmaModule module = JmaModule.Create(instanceId);
        module.Initialize(SystemChartModuleContext.Instance);
        module.ApplyProfile(CreateProfile(
            instanceId,
            period,
            phase,
            power,
            true));
        module.Activate();
        return module;
    }

    private static ChartModuleProfile CreateProfile(
        string instanceId,
        int period,
        int phase,
        int power,
        bool enabled,
        string upStroke = "#AB47BC",
        string downStroke = "#00C853") =>
        new()
        {
            ModuleId = JmaModule.Definition.ModuleId,
            InstanceId = instanceId,
            ModuleSchemaVersion = JmaModule.Definition.SchemaVersion,
            IsEnabled = enabled,
            ZIndex = 10,
            Placement = "price.main",
            Parameters = new JsonObject
            {
                ["period"] = period,
                ["phase"] = phase,
                ["power"] = power
            },
            Style = new JsonObject
            {
                [JmaModule.UpObjectId + ".stroke"] = upStroke,
                [JmaModule.DownObjectId + ".stroke"] = downStroke
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
        IReadOnlyList<JmaValuePoint> actual,
        string context)
    {
        if (expected.Count != actual.Count)
            throw new InvalidOperationException($"JMA {context} count mismatch.");
        for (int index = 0; index < expected.Count; index++)
        {
            if (expected[index].Sequence != actual[index].Sequence)
                throw new InvalidOperationException(
                    $"JMA {context} sequence mismatch at {index}.");
            AssertPoint(expected[index], actual[index], $"{context} index={index}");
        }
    }

    private static void AssertPoint(
        IndicatorPoint expected,
        JmaValuePoint actual,
        string context)
    {
        Fixture.Equal(expected.Value0, actual.Value, "JMA " + context + " value");
        Fixture.Equal(expected.Value1, actual.Up, "JMA " + context + " up");
        Fixture.Equal(expected.Value2, actual.Down, "JMA " + context + " down");
        Fixture.Equal(expected.Value3, actual.Slope, "JMA " + context + " slope");
    }

    private static void AssertPlanParity(
        IReadOnlyList<IndicatorPoint> expected,
        ChartRenderPlan plan)
    {
        RenderPrimitivePlan up = plan.Primitives.Single(
            static item => item.Identity.ObjectId == JmaModule.UpObjectId);
        RenderPrimitivePlan down = plan.Primitives.Single(
            static item => item.Identity.ObjectId == JmaModule.DownObjectId);
        for (int index = 0; index < expected.Count; index++)
        {
            Fixture.Equal(
                expected[index].Value1,
                (float)up.Points[index].Y,
                "JMA parameter up");
            Fixture.Equal(
                expected[index].Value2,
                (float)down.Points[index].Y,
                "JMA parameter down");
        }
    }

    private static void VerifyReferenceBoundary()
    {
        Assembly assembly = typeof(JmaModule).Assembly;
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
                "JMA module assembly is not Release.");
    }

    private sealed class PropertyWriter : IChartPropertyWriter
    {
        private readonly List<ChartPropertyDescriptor> _items = new();
        public IReadOnlyList<ChartPropertyDescriptor> Items => _items;
        public void Add(ChartPropertyDescriptor descriptor) => _items.Add(descriptor);
    }
}
