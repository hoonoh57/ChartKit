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

internal static class SuperTrendModuleParityVerification
{
    public static void Run()
    {
        VerifyDefinitionAndMetadata();
        VerifyFullUpdateAppendAndRebuildParity();
        VerifyDisabledZeroAndContributions();
        VerifyParameterChange();
        VerifyReferenceBoundary();

#if RELEASE
        VerifyReleaseAssembly(typeof(SuperTrendModule).Assembly);
        Console.WriteLine("csharp_supertrend_module_release_configuration=PASS");
#endif
        Console.WriteLine("csharp_supertrend_module_definition=PASS");
        Console.WriteLine("csharp_supertrend_module_metadata=PASS");
        Console.WriteLine("csharp_supertrend_full_parity=PASS");
        Console.WriteLine("csharp_supertrend_update_parity=PASS");
        Console.WriteLine("csharp_supertrend_append_parity=PASS");
        Console.WriteLine("csharp_supertrend_rebuild_parity=PASS");
        Console.WriteLine("csharp_supertrend_disabled_zero=PASS");
        Console.WriteLine("csharp_supertrend_contributions=PASS");
        Console.WriteLine("csharp_supertrend_panel_contract=PASS");
        Console.WriteLine("csharp_supertrend_style_override=PASS");
        Console.WriteLine("csharp_supertrend_parameter_change=PASS");
        Console.WriteLine("csharp_supertrend_reference_boundary=PASS");
        Console.WriteLine("csharp_supertrend_module_contracts=PASS");
    }

    private static void VerifyDefinitionAndMetadata()
    {
        ChartModuleDefinition definition = SuperTrendModule.Definition;
        if (definition.ModuleId != "indicator.supertrend" ||
            definition.Category != "Indicators" ||
            definition.DefaultPanelId != "price.main" ||
            definition.DefaultEnabled ||
            !definition.Capabilities.HasFlag(ChartModuleCapabilities.Computation) ||
            !definition.SupportedPrimitiveKinds.SequenceEqual(
                [ChartPrimitiveKind.Polyline]))
        {
            throw new InvalidOperationException(
                "SuperTrend module definition is incomplete.");
        }

        SuperTrendModule module = SuperTrendModule.Create("st-definition");
        module.Initialize(SystemChartModuleContext.Instance);
        module.ApplyProfile(CreateProfile("st-definition", 10, 3d, false));
        var properties = new PropertyWriter();
        module.DescribeProperties(properties);
        string[] actual = properties.Items
            .Select(static item => item.PropertyId)
            .OrderBy(static item => item, StringComparer.Ordinal)
            .ToArray();
        string[] expected =
        [
            "multiplier",
            "period",
            "supertrend.down.stroke",
            "supertrend.up.stroke"
        ];
        if (!actual.SequenceEqual(expected))
            throw new InvalidOperationException(
                "SuperTrend property metadata is incomplete.");
    }

    private static void VerifyFullUpdateAppendAndRebuildParity()
    {
        List<Candle> candles = Fixture.CreateCandles(260);
        var legacy = new SuperTrendIndicator(10, 3f);
        IReadOnlyList<IndicatorPoint> expected = legacy.Calculate(candles);

        SuperTrendModule module = CreateActiveModule("st-parity", 10, 3d);
        module.ApplyPrimarySeries(ToPrimary(candles, 1));
        AssertParity(expected, module.SnapshotValues(), "full");
        if (module.Diagnostics.FullCalculations != 1)
            throw new InvalidOperationException(
                "SuperTrend full path was not recorded.");

        Candle previousLast = candles[^1];
        candles[^1] = previousLast with
        {
            High = previousLast.High + 12f,
            Low = previousLast.Low - 4f,
            Close = previousLast.Close + 7f,
            IsFinal = false
        };
        IndicatorPoint expectedUpdate = legacy.UpdateLast(candles);
        module.ApplyPrimarySeries(ToPrimary(candles, 2));
        AssertPoint(expectedUpdate, module.SnapshotValues()[^1], "update");
        if (module.Diagnostics.LastUpdates != 1)
            throw new InvalidOperationException(
                "SuperTrend update path was not used.");

        Candle previous = candles[^1];
        candles.Add(new Candle(
            previous.CloseTime,
            previous.CloseTime.AddMinutes(1),
            previous.Close,
            previous.Close + 5f,
            previous.Close - 8f,
            previous.Close - 6f,
            previous.Volume + 100,
            true,
            previous.Sequence + 1));
        IndicatorPoint expectedAppend = legacy.UpdateLast(candles);
        module.ApplyPrimarySeries(ToPrimary(candles, 3));
        AssertPoint(expectedAppend, module.SnapshotValues()[^1], "append");
        if (module.Diagnostics.Appends != 1)
            throw new InvalidOperationException(
                "SuperTrend append path was not used.");

        List<Candle> rolling = candles.Skip(1).ToList();
        IReadOnlyList<IndicatorPoint> rollingExpected =
            new SuperTrendIndicator(10, 3f).Calculate(rolling);
        module.ApplyPrimarySeries(ToPrimary(rolling, 4));
        AssertParity(rollingExpected, module.SnapshotValues(), "rebuild");
        if (module.Diagnostics.FullCalculations != 2)
            throw new InvalidOperationException(
                "SuperTrend rebuild path was not used.");
    }

    private static void VerifyDisabledZeroAndContributions()
    {
        var registry = new ChartModuleRegistry();
        registry.Register<SuperTrendModule>();
        var host = new ChartModuleHost(registry);
        ChartModuleOperationResult hosted = host.UpsertProfile(
            CreateProfile("st-host", 10, 3d, false));
        if (!hosted.Succeeded)
            throw new InvalidOperationException(hosted.Error);

        List<Candle> candles = Fixture.CreateCandles(180);
        ChartModuleDataUpdateResult disabled =
            host.ApplyPrimarySeries(ToPrimary(candles, 10));
        if (disabled.EligibleModules != 0 || disabled.UpdatedModules != 0)
            throw new InvalidOperationException(
                "Disabled SuperTrend module performed calculation.");

        ChartModuleOperationResult enabled = host.SetEnabled("st-host", true);
        if (!enabled.Succeeded)
            throw new InvalidOperationException(enabled.Error);
        ChartModuleDataUpdateResult updated =
            host.ApplyPrimarySeries(ToPrimary(candles, 11));
        if (updated.EligibleModules != 1 ||
            updated.UpdatedModules != 1 ||
            updated.FaultedModules != 0)
        {
            throw new InvalidOperationException(
                "Enabled SuperTrend module did not consume data.");
        }

        ChartRenderPlan plan = new ChartCompositionService(host).Compose(
            new ChartVisualContext(11, 1, 1, 100, 180));
        if (plan.Primitives.Count != 2)
            throw new InvalidOperationException(
                "SuperTrend did not emit two trend primitives.");

        RenderPrimitivePlan up = plan.Primitives.Single(
            static item => item.Identity.ObjectId == SuperTrendModule.UpObjectId);
        RenderPrimitivePlan down = plan.Primitives.Single(
            static item => item.Identity.ObjectId == SuperTrendModule.DownObjectId);
        if (up.PrimitiveKind != ChartPrimitiveKind.Polyline ||
            down.PrimitiveKind != ChartPrimitiveKind.Polyline ||
            up.PanelId != "price.main" ||
            down.PanelId != "price.main" ||
            up.Style.Stroke != "#00C853" ||
            down.Style.Stroke != "#AB47BC")
        {
            throw new InvalidOperationException(
                "SuperTrend contributions, panel or styles are incorrect.");
        }
    }

    private static void VerifyParameterChange()
    {
        List<Candle> candles = Fixture.CreateCandles(200);
        var registry = new ChartModuleRegistry();
        registry.Register<SuperTrendModule>();
        var host = new ChartModuleHost(registry);
        ChartModuleOperationResult initial = host.UpsertProfile(
            CreateProfile("st-parameter", 10, 3d, true));
        if (!initial.Succeeded)
            throw new InvalidOperationException(initial.Error);
        host.ApplyPrimarySeries(ToPrimary(candles, 20));

        ChartModuleOperationResult changed = host.UpsertProfile(
            CreateProfile(
                "st-parameter",
                5,
                2d,
                true,
                upStroke: "#112233",
                downStroke: "#445566"));
        if (!changed.Succeeded || !changed.Changed)
            throw new InvalidOperationException(
                changed.Error ?? "SuperTrend parameter profile did not change.");
        host.ApplyPrimarySeries(ToPrimary(candles, 21));

        ChartRenderPlan plan = new ChartCompositionService(host).Compose(
            new ChartVisualContext(21, 1, 1, 100, 200));
        IReadOnlyList<IndicatorPoint> expected =
            new SuperTrendIndicator(5, 2f).Calculate(candles);
        AssertPlanParity(expected, plan);
        RenderPrimitivePlan up = plan.Primitives.Single(
            static item => item.Identity.ObjectId == SuperTrendModule.UpObjectId);
        RenderPrimitivePlan down = plan.Primitives.Single(
            static item => item.Identity.ObjectId == SuperTrendModule.DownObjectId);
        if (up.Style.Stroke != "#112233" || down.Style.Stroke != "#445566")
            throw new InvalidOperationException(
                "SuperTrend object-specific styles were not applied.");
    }

    private static SuperTrendModule CreateActiveModule(
        string instanceId,
        int period,
        double multiplier)
    {
        SuperTrendModule module = SuperTrendModule.Create(instanceId);
        module.Initialize(SystemChartModuleContext.Instance);
        module.ApplyProfile(CreateProfile(
            instanceId,
            period,
            multiplier,
            true));
        module.Activate();
        return module;
    }

    private static ChartModuleProfile CreateProfile(
        string instanceId,
        int period,
        double multiplier,
        bool enabled,
        string upStroke = "#00C853",
        string downStroke = "#AB47BC") =>
        new()
        {
            ModuleId = SuperTrendModule.Definition.ModuleId,
            InstanceId = instanceId,
            ModuleSchemaVersion = SuperTrendModule.Definition.SchemaVersion,
            IsEnabled = enabled,
            ZIndex = 10,
            Placement = "price.main",
            Parameters = new JsonObject
            {
                ["period"] = period,
                ["multiplier"] = multiplier
            },
            Style = new JsonObject
            {
                [SuperTrendModule.UpObjectId + ".stroke"] = upStroke,
                [SuperTrendModule.DownObjectId + ".stroke"] = downStroke
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
        IReadOnlyList<SuperTrendValuePoint> actual,
        string context)
    {
        if (expected.Count != actual.Count)
            throw new InvalidOperationException(
                $"SuperTrend {context} count mismatch.");
        for (int index = 0; index < expected.Count; index++)
        {
            if (expected[index].Sequence != actual[index].Sequence)
                throw new InvalidOperationException(
                    $"SuperTrend {context} sequence mismatch at {index}.");
            AssertPoint(expected[index], actual[index], $"{context} index={index}");
        }
    }

    private static void AssertPoint(
        IndicatorPoint expected,
        SuperTrendValuePoint actual,
        string context)
    {
        Fixture.Equal(expected.Value0, actual.Value, "SuperTrend " + context + " value");
        Fixture.Equal(expected.Value1, actual.Up, "SuperTrend " + context + " up");
        Fixture.Equal(expected.Value2, actual.Down, "SuperTrend " + context + " down");
        Fixture.Equal(expected.Value3, actual.Direction, "SuperTrend " + context + " direction");
        Fixture.Equal(expected.Value4, actual.Atr, "SuperTrend " + context + " atr");
    }

    private static void AssertPlanParity(
        IReadOnlyList<IndicatorPoint> expected,
        ChartRenderPlan plan)
    {
        RenderPrimitivePlan up = plan.Primitives.Single(
            static item => item.Identity.ObjectId == SuperTrendModule.UpObjectId);
        RenderPrimitivePlan down = plan.Primitives.Single(
            static item => item.Identity.ObjectId == SuperTrendModule.DownObjectId);
        for (int index = 0; index < expected.Count; index++)
        {
            Fixture.Equal(
                expected[index].Value1,
                (float)up.Points[index].Y,
                "SuperTrend parameter up");
            Fixture.Equal(
                expected[index].Value2,
                (float)down.Points[index].Y,
                "SuperTrend parameter down");
        }
    }

    private static void VerifyReferenceBoundary()
    {
        Assembly assembly = typeof(SuperTrendModule).Assembly;
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
                "SuperTrend module assembly is not Release.");
    }

    private sealed class PropertyWriter : IChartPropertyWriter
    {
        private readonly List<ChartPropertyDescriptor> _items = new();
        public IReadOnlyList<ChartPropertyDescriptor> Items => _items;
        public void Add(ChartPropertyDescriptor descriptor) => _items.Add(descriptor);
    }
}
