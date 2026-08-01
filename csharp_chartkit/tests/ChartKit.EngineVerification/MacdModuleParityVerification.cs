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

internal static class MacdModuleParityVerification
{
    public static void Run()
    {
        VerifyDefinitionAndMetadata();
        VerifyFullUpdateAppendAndRebuildParity();
        VerifyDisabledZeroAndContributions();
        VerifyLegacyPlacementMigration();
        VerifyParameterChange();
        VerifyReferenceBoundary();

#if RELEASE
        VerifyReleaseAssembly(typeof(MacdModule).Assembly);
        Console.WriteLine("csharp_macd_module_release_configuration=PASS");
#endif
        Console.WriteLine("csharp_macd_module_definition=PASS");
        Console.WriteLine("csharp_macd_module_metadata=PASS");
        Console.WriteLine("csharp_macd_full_parity=PASS");
        Console.WriteLine("csharp_macd_update_parity=PASS");
        Console.WriteLine("csharp_macd_append_parity=PASS");
        Console.WriteLine("csharp_macd_rebuild_parity=PASS");
        Console.WriteLine("csharp_macd_disabled_zero=PASS");
        Console.WriteLine("csharp_macd_contributions=PASS");
        Console.WriteLine("csharp_macd_style_override=PASS");
        Console.WriteLine("csharp_macd_panel_contract=PASS");
        Console.WriteLine("csharp_macd_legacy_panel_migration=PASS");
        Console.WriteLine("csharp_macd_parameter_change=PASS");
        Console.WriteLine("csharp_macd_reference_boundary=PASS");
        Console.WriteLine("csharp_macd_module_contracts=PASS");
    }

    private static void VerifyDefinitionAndMetadata()
    {
        ChartModuleDefinition definition = MacdModule.Definition;
        if (definition.ModuleId != "indicator.macd" ||
            definition.Category != "Indicators" ||
            definition.DefaultPanelId != MacdModule.DefaultPanelId ||
            definition.DefaultPanelId != "indicator.7" ||
            definition.DefaultEnabled ||
            !definition.Capabilities.HasFlag(ChartModuleCapabilities.Computation) ||
            !definition.SupportedPrimitiveKinds.SequenceEqual(
                [ChartPrimitiveKind.Polyline, ChartPrimitiveKind.Histogram]))
        {
            throw new InvalidOperationException(
                "MACD module definition is incomplete.");
        }

        MacdModule module = MacdModule.Create("macd-definition");
        module.Initialize(SystemChartModuleContext.Instance);
        module.ApplyProfile(CreateProfile("macd-definition", 12, 26, 9, false));
        var properties = new PropertyWriter();
        module.DescribeProperties(properties);
        string[] ids = properties.Items
            .Select(static item => item.PropertyId)
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
        if (!ids.SequenceEqual(expected))
            throw new InvalidOperationException("MACD property metadata is incomplete.");
    }

    private static void VerifyFullUpdateAppendAndRebuildParity()
    {
        List<Candle> candles = Fixture.CreateCandles(260);
        var legacy = new MacdIndicator(12, 26, 9);
        IReadOnlyList<IndicatorPoint> expected = legacy.Calculate(candles);

        MacdModule module = CreateActiveModule("macd-parity", 12, 26, 9);
        module.ApplyPrimarySeries(ToPrimary(candles, 1));
        AssertParity(expected, module.SnapshotValues(), "full");
        if (module.Diagnostics.FullCalculations != 1)
            throw new InvalidOperationException("MACD full path was not recorded.");

        candles[^1] = candles[^1] with
        {
            Close = candles[^1].Close + 9.5f,
            High = Math.Max(candles[^1].High, candles[^1].Close + 10f),
            IsFinal = false
        };
        IndicatorPoint expectedUpdate = legacy.UpdateLast(candles);
        module.ApplyPrimarySeries(ToPrimary(candles, 2));
        AssertPoint(expectedUpdate, module.SnapshotValues()[^1], "update");
        if (module.Diagnostics.LastUpdates != 1)
            throw new InvalidOperationException("MACD update path was not used.");

        Candle previous = candles[^1];
        candles.Add(new Candle(
            previous.CloseTime,
            previous.CloseTime.AddMinutes(1),
            previous.Close,
            previous.Close + 4f,
            previous.Close - 2f,
            previous.Close + 3f,
            previous.Volume + 100,
            true,
            previous.Sequence + 1));
        IndicatorPoint expectedAppend = legacy.UpdateLast(candles);
        module.ApplyPrimarySeries(ToPrimary(candles, 3));
        AssertPoint(expectedAppend, module.SnapshotValues()[^1], "append");
        if (module.Diagnostics.Appends != 1)
            throw new InvalidOperationException("MACD append path was not used.");

        List<Candle> rolling = candles.Skip(1).ToList();
        IReadOnlyList<IndicatorPoint> rollingExpected =
            new MacdIndicator(12, 26, 9).Calculate(rolling);
        module.ApplyPrimarySeries(ToPrimary(rolling, 4));
        AssertParity(rollingExpected, module.SnapshotValues(), "rebuild");
        if (module.Diagnostics.FullCalculations != 2)
            throw new InvalidOperationException("MACD rebuild path was not used.");
    }

    private static void VerifyDisabledZeroAndContributions()
    {
        var registry = new ChartModuleRegistry();
        registry.Register<MacdModule>();
        var host = new ChartModuleHost(registry);
        ChartModuleOperationResult hosted = host.UpsertProfile(
            CreateProfile("macd-host", 12, 26, 9, false));
        if (!hosted.Succeeded)
            throw new InvalidOperationException(hosted.Error);

        List<Candle> candles = Fixture.CreateCandles(160);
        ChartModuleDataUpdateResult disabled =
            host.ApplyPrimarySeries(ToPrimary(candles, 10));
        if (disabled.EligibleModules != 0 || disabled.UpdatedModules != 0)
            throw new InvalidOperationException(
                "Disabled MACD module performed calculation.");

        ChartModuleOperationResult enabled = host.SetEnabled("macd-host", true);
        if (!enabled.Succeeded)
            throw new InvalidOperationException(enabled.Error);
        ChartModuleDataUpdateResult updated =
            host.ApplyPrimarySeries(ToPrimary(candles, 11));
        if (updated.EligibleModules != 1 ||
            updated.UpdatedModules != 1 ||
            updated.FaultedModules != 0)
        {
            throw new InvalidOperationException(
                "Enabled MACD module did not consume data.");
        }

        ChartRenderPlan plan = new ChartCompositionService(host).Compose(
            new ChartVisualContext(11, 1, 1, 100, 160));
        AssertContributionContract(plan);
    }

    private static void VerifyLegacyPlacementMigration()
    {
        var registry = new ChartModuleRegistry();
        registry.Register<MacdModule>();
        var host = new ChartModuleHost(registry);
        ChartModuleOperationResult hosted = host.UpsertProfile(
            CreateProfile(
                "macd-legacy-placement",
                12,
                26,
                9,
                true,
                placement: "indicator.4"));
        if (!hosted.Succeeded)
            throw new InvalidOperationException(hosted.Error);

        List<Candle> candles = Fixture.CreateCandles(160);
        ChartModuleDataUpdateResult updated =
            host.ApplyPrimarySeries(ToPrimary(candles, 12));
        if (updated.UpdatedModules != 1 || updated.FaultedModules != 0)
            throw new InvalidOperationException(
                "Legacy MACD profile did not consume data.");

        ChartRenderPlan plan = new ChartCompositionService(host).Compose(
            new ChartVisualContext(12, 1, 1, 100, 160));
        if (plan.Primitives.Count != 3 ||
            plan.Primitives.Any(static item =>
                item.PanelId != MacdModule.DefaultPanelId))
        {
            throw new InvalidOperationException(
                "Legacy MACD panel placement was not migrated.");
        }
    }

    private static void VerifyParameterChange()
    {
        List<Candle> candles = Fixture.CreateCandles(180);
        var registry = new ChartModuleRegistry();
        registry.Register<MacdModule>();
        var host = new ChartModuleHost(registry);
        ChartModuleOperationResult initial = host.UpsertProfile(
            CreateProfile("macd-period", 12, 26, 9, true));
        if (!initial.Succeeded)
            throw new InvalidOperationException(initial.Error);
        host.ApplyPrimarySeries(ToPrimary(candles, 20));

        ChartModuleOperationResult changed = host.UpsertProfile(
            CreateProfile("macd-period", 5, 13, 4, true));
        if (!changed.Succeeded || !changed.Changed)
            throw new InvalidOperationException(
                changed.Error ?? "MACD parameter profile did not change.");
        host.ApplyPrimarySeries(ToPrimary(candles, 21));

        ChartRenderPlan plan = new ChartCompositionService(host).Compose(
            new ChartVisualContext(21, 1, 1, 100, 180));
        IReadOnlyList<IndicatorPoint> expected =
            new MacdIndicator(5, 13, 4).Calculate(candles);
        AssertPlanParity(expected, plan);
    }

    private static void AssertContributionContract(ChartRenderPlan plan)
    {
        if (plan.Primitives.Count != 3)
            throw new InvalidOperationException("MACD did not emit three primitives.");

        RenderPrimitivePlan macd = plan.Primitives.Single(
            static item => item.Identity.ObjectId == MacdModule.MacdObjectId);
        RenderPrimitivePlan signal = plan.Primitives.Single(
            static item => item.Identity.ObjectId == MacdModule.SignalObjectId);
        RenderPrimitivePlan histogram = plan.Primitives.Single(
            static item => item.Identity.ObjectId == MacdModule.HistogramObjectId);
        if (macd.PrimitiveKind != ChartPrimitiveKind.Polyline ||
            signal.PrimitiveKind != ChartPrimitiveKind.Polyline ||
            histogram.PrimitiveKind != ChartPrimitiveKind.Histogram ||
            macd.PanelId != MacdModule.DefaultPanelId ||
            signal.PanelId != MacdModule.DefaultPanelId ||
            histogram.PanelId != MacdModule.DefaultPanelId ||
            macd.Style.Stroke != "#795548" ||
            signal.Style.Stroke != "#009688" ||
            histogram.Style.Stroke != "#FFEB3B")
        {
            throw new InvalidOperationException(
                "MACD contributions, panel placement, or styles are incorrect.");
        }
    }

    private static MacdModule CreateActiveModule(
        string instanceId,
        int fast,
        int slow,
        int signal)
    {
        MacdModule module = MacdModule.Create(instanceId);
        module.Initialize(SystemChartModuleContext.Instance);
        module.ApplyProfile(CreateProfile(instanceId, fast, slow, signal, true));
        module.Activate();
        return module;
    }

    private static ChartModuleProfile CreateProfile(
        string instanceId,
        int fast,
        int slow,
        int signal,
        bool enabled,
        string? placement = null) =>
        new()
        {
            ModuleId = MacdModule.Definition.ModuleId,
            InstanceId = instanceId,
            ModuleSchemaVersion = MacdModule.Definition.SchemaVersion,
            IsEnabled = enabled,
            ZIndex = 10,
            Placement = placement ?? MacdModule.DefaultPanelId,
            Parameters = new JsonObject
            {
                ["fastPeriod"] = fast,
                ["slowPeriod"] = slow,
                ["signalPeriod"] = signal
            },
            Style = new JsonObject
            {
                [MacdModule.MacdObjectId + ".stroke"] = "#795548",
                [MacdModule.SignalObjectId + ".stroke"] = "#009688",
                [MacdModule.HistogramObjectId + ".stroke"] = "#FFEB3B"
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
        IReadOnlyList<MacdValuePoint> actual,
        string context)
    {
        if (expected.Count != actual.Count)
            throw new InvalidOperationException(
                $"MACD {context} count mismatch.");
        for (int index = 0; index < expected.Count; index++)
        {
            if (expected[index].Sequence != actual[index].Sequence)
                throw new InvalidOperationException(
                    $"MACD {context} sequence mismatch at {index}.");
            AssertPoint(expected[index], actual[index], $"{context} index={index}");
        }
    }

    private static void AssertPoint(
        IndicatorPoint expected,
        MacdValuePoint actual,
        string context)
    {
        Fixture.Equal(expected.Value0, actual.Macd, "MACD " + context + " value");
        Fixture.Equal(expected.Value1, actual.Signal, "MACD " + context + " signal");
        Fixture.Equal(expected.Value2, actual.Histogram, "MACD " + context + " histogram");
    }

    private static void AssertPlanParity(
        IReadOnlyList<IndicatorPoint> expected,
        ChartRenderPlan plan)
    {
        AssertContributionContract(plan);
        RenderPrimitivePlan macd = plan.Primitives.Single(
            static item => item.Identity.ObjectId == MacdModule.MacdObjectId);
        RenderPrimitivePlan signal = plan.Primitives.Single(
            static item => item.Identity.ObjectId == MacdModule.SignalObjectId);
        RenderPrimitivePlan histogram = plan.Primitives.Single(
            static item => item.Identity.ObjectId == MacdModule.HistogramObjectId);
        for (int index = 0; index < expected.Count; index++)
        {
            Fixture.Equal(expected[index].Value0, (float)macd.Points[index].Y, "MACD parameter value");
            Fixture.Equal(expected[index].Value1, (float)signal.Points[index].Y, "MACD parameter signal");
            Fixture.Equal(expected[index].Value2, (float)histogram.Points[index].Y, "MACD parameter histogram");
        }
    }

    private static void VerifyReferenceBoundary()
    {
        Assembly assembly = typeof(MacdModule).Assembly;
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
                "MACD module assembly is not Release.");
    }

    private sealed class PropertyWriter : IChartPropertyWriter
    {
        private readonly List<ChartPropertyDescriptor> _items = new();
        public IReadOnlyList<ChartPropertyDescriptor> Items => _items;
        public void Add(ChartPropertyDescriptor descriptor) => _items.Add(descriptor);
    }
}
