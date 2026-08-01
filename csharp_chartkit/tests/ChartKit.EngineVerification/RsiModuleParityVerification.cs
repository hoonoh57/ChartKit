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

internal static class RsiModuleParityVerification
{
    public static void Run()
    {
        VerifyDefinitionAndMetadata();
        VerifyFullUpdateAppendAndRebuildParity();
        VerifyHostDisabledZeroAndContributions();
        VerifyParameterChange();
        VerifyReferenceBoundary();

#if RELEASE
        VerifyReleaseAssembly(typeof(RsiModule).Assembly);
        Console.WriteLine("csharp_rsi_module_release_configuration=PASS");
#endif
        Console.WriteLine("csharp_rsi_module_definition=PASS");
        Console.WriteLine("csharp_rsi_module_metadata=PASS");
        Console.WriteLine("csharp_rsi_full_parity=PASS");
        Console.WriteLine("csharp_rsi_update_parity=PASS");
        Console.WriteLine("csharp_rsi_append_parity=PASS");
        Console.WriteLine("csharp_rsi_rebuild_parity=PASS");
        Console.WriteLine("csharp_rsi_disabled_zero=PASS");
        Console.WriteLine("csharp_rsi_contributions=PASS");
        Console.WriteLine("csharp_rsi_style_override=PASS");
        Console.WriteLine("csharp_rsi_parameter_change=PASS");
        Console.WriteLine("csharp_rsi_reference_boundary=PASS");
        Console.WriteLine("csharp_rsi_module_contracts=PASS");
    }

    private static void VerifyDefinitionAndMetadata()
    {
        ChartModuleDefinition definition = RsiModule.Definition;
        if (definition.ModuleId != "indicator.rsi" ||
            definition.Category != "Indicators" ||
            definition.DefaultPanelId != "indicator.1" ||
            definition.DefaultEnabled ||
            !definition.Capabilities.HasFlag(
                ChartModuleCapabilities.DataRequirements) ||
            !definition.Capabilities.HasFlag(
                ChartModuleCapabilities.Computation) ||
            !definition.Capabilities.HasFlag(
                ChartModuleCapabilities.Visual) ||
            !definition.SupportedPrimitiveKinds.SequenceEqual(
                [ChartPrimitiveKind.Polyline]))
        {
            throw new InvalidOperationException(
                "RSI module definition is incomplete.");
        }

        RsiModule module = RsiModule.Create("rsi-definition");
        module.Initialize(SystemChartModuleContext.Instance);
        module.ApplyProfile(CreateProfile(
            "rsi-definition",
            14,
            9,
            70d,
            30d,
            false));

        var requirements = new RequirementWriter();
        module.DescribeRequirements(requirements);
        if (requirements.Items.Count != 1 ||
            requirements.Items[0].DataKind != "OHLCV")
        {
            throw new InvalidOperationException(
                "RSI primary OHLCV requirement is missing.");
        }

        var properties = new PropertyWriter();
        module.DescribeProperties(properties);
        string[] expectedProperties =
        [
            "lower",
            "period",
            "rsi.lower.stroke",
            "rsi.signal.stroke",
            "rsi.upper.stroke",
            "rsi.value.stroke",
            "signalPeriod",
            "upper"
        ];
        if (!properties.Items
                .Select(static item => item.PropertyId)
                .OrderBy(static item => item, StringComparer.Ordinal)
                .SequenceEqual(expectedProperties))
        {
            throw new InvalidOperationException(
                "RSI property metadata is incomplete.");
        }
    }

    private static void VerifyFullUpdateAppendAndRebuildParity()
    {
        List<Candle> candles = Fixture.CreateCandles(240);
        var legacy = new RsiIndicator(14, 9);
        IReadOnlyList<IndicatorPoint> expected = legacy.Calculate(candles);

        RsiModule module = CreateActiveModule("rsi-parity", 14, 9, 70d, 30d);
        module.ApplyPrimarySeries(ToPrimary(candles, 1));
        AssertParity(expected, module.SnapshotValues(), "full");
        if (module.Diagnostics.FullCalculations != 1)
            throw new InvalidOperationException(
                "RSI full calculation diagnostic was not recorded.");

        Candle changed = candles[^1] with
        {
            Close = candles[^1].Close - 8.5f,
            Low = Math.Min(candles[^1].Low, candles[^1].Close - 9f),
            IsFinal = false
        };
        candles[^1] = changed;
        IndicatorPoint expectedUpdate = legacy.UpdateLast(candles);
        module.ApplyPrimarySeries(ToPrimary(candles, 2));
        RsiValuePoint actualUpdate = module.SnapshotValues()[^1];
        Fixture.Equal(
            expectedUpdate.Value0,
            actualUpdate.Rsi,
            "RSI last update value parity");
        Fixture.Equal(
            expectedUpdate.Value1,
            actualUpdate.Signal,
            "RSI last update signal parity");
        if (module.Diagnostics.LastUpdates != 1)
            throw new InvalidOperationException(
                "RSI last-update path was not used.");

        Candle previous = candles[^1];
        var appended = new Candle(
            previous.CloseTime,
            previous.CloseTime.AddMinutes(1),
            previous.Close,
            previous.Close + 4f,
            previous.Close - 1f,
            previous.Close + 3f,
            previous.Volume + 150,
            true,
            previous.Sequence + 1);
        candles.Add(appended);
        IndicatorPoint expectedAppend = legacy.UpdateLast(candles);
        module.ApplyPrimarySeries(ToPrimary(candles, 3));
        RsiValuePoint actualAppend = module.SnapshotValues()[^1];
        Fixture.Equal(
            expectedAppend.Value0,
            actualAppend.Rsi,
            "RSI append value parity");
        Fixture.Equal(
            expectedAppend.Value1,
            actualAppend.Signal,
            "RSI append signal parity");
        if (module.Diagnostics.Appends != 1)
            throw new InvalidOperationException("RSI append path was not used.");

        List<Candle> rolling = candles.Skip(1).ToList();
        IReadOnlyList<IndicatorPoint> rollingExpected =
            new RsiIndicator(14, 9).Calculate(rolling);
        module.ApplyPrimarySeries(ToPrimary(rolling, 4));
        AssertParity(rollingExpected, module.SnapshotValues(), "rolling rebuild");
        if (module.Diagnostics.FullCalculations != 2)
            throw new InvalidOperationException(
                "RSI rolling snapshot did not fall back to full calculation.");
    }

    private static void VerifyHostDisabledZeroAndContributions()
    {
        var registry = new ChartModuleRegistry();
        registry.Register<RsiModule>();
        var host = new ChartModuleHost(registry);
        ChartModuleProfile profile = CreateProfile(
            "rsi-host",
            14,
            9,
            70d,
            30d,
            false);
        ChartModuleOperationResult hosted = host.UpsertProfile(profile);
        if (!hosted.Succeeded)
            throw new InvalidOperationException(hosted.Error);

        List<Candle> candles = Fixture.CreateCandles(120);
        ChartModuleDataUpdateResult disabled =
            host.ApplyPrimarySeries(ToPrimary(candles, 10));
        if (disabled.EligibleModules != 0 ||
            disabled.UpdatedModules != 0 ||
            disabled.FaultedModules != 0)
        {
            throw new InvalidOperationException(
                "Disabled RSI module performed a calculation.");
        }

        ChartModuleOperationResult enabled = host.SetEnabled("rsi-host", true);
        if (!enabled.Succeeded)
            throw new InvalidOperationException(enabled.Error);
        ChartModuleDataUpdateResult updated =
            host.ApplyPrimarySeries(ToPrimary(candles, 11));
        if (updated.EligibleModules != 1 ||
            updated.UpdatedModules != 1 ||
            updated.FaultedModules != 0)
        {
            throw new InvalidOperationException(
                "Enabled RSI module did not consume primary data.");
        }

        ChartRenderPlan plan = new ChartCompositionService(host).Compose(
            new ChartVisualContext(11, 1, 1, 80, 120));
        if (plan.Primitives.Count != 4)
            throw new InvalidOperationException(
                "RSI did not emit four render primitives.");

        Dictionary<string, RenderPrimitivePlan> byObject = plan.Primitives
            .ToDictionary(
                static item => item.Identity.ObjectId,
                StringComparer.Ordinal);
        string[] expectedObjects =
        [
            RsiModule.LowerObjectId,
            RsiModule.SignalObjectId,
            RsiModule.UpperObjectId,
            RsiModule.RsiObjectId
        ];
        if (!byObject.Keys
                .OrderBy(static item => item, StringComparer.Ordinal)
                .SequenceEqual(expectedObjects))
        {
            throw new InvalidOperationException(
                "RSI contribution identities are incomplete.");
        }

        foreach (RenderPrimitivePlan primitive in plan.Primitives)
        {
            if (primitive.PanelId != "indicator.1" ||
                primitive.PrimitiveKind != ChartPrimitiveKind.Polyline ||
                primitive.Points.Count != candles.Count)
            {
                throw new InvalidOperationException(
                    "RSI contribution contract is invalid.");
            }
        }

        AssertStyle(byObject[RsiModule.RsiObjectId], "#FF0000", 2f);
        AssertStyle(byObject[RsiModule.SignalObjectId], "#00FF00", 2f);
        AssertStyle(byObject[RsiModule.UpperObjectId], "#0000FF", 0.75f);
        AssertStyle(byObject[RsiModule.LowerObjectId], "#FFFF00", 2f);

        IReadOnlyList<IndicatorPoint> expected =
            new RsiIndicator(14, 9).Calculate(candles);
        for (int index = 0; index < candles.Count; index++)
        {
            Fixture.Equal(
                expected[index].Value0,
                (float)byObject[RsiModule.RsiObjectId].Points[index].Y,
                $"RSI contribution value index={index}");
            Fixture.Equal(
                expected[index].Value1,
                (float)byObject[RsiModule.SignalObjectId].Points[index].Y,
                $"RSI contribution signal index={index}");
            Fixture.Equal(
                70f,
                (float)byObject[RsiModule.UpperObjectId].Points[index].Y,
                $"RSI upper index={index}");
            Fixture.Equal(
                30f,
                (float)byObject[RsiModule.LowerObjectId].Points[index].Y,
                $"RSI lower index={index}");
        }
    }

    private static void VerifyParameterChange()
    {
        List<Candle> candles = Fixture.CreateCandles(100);
        var registry = new ChartModuleRegistry();
        registry.Register<RsiModule>();
        var host = new ChartModuleHost(registry);
        ChartModuleOperationResult initial = host.UpsertProfile(
            CreateProfile("rsi-parameters", 14, 9, 70d, 30d, true));
        if (!initial.Succeeded)
            throw new InvalidOperationException(initial.Error);
        host.ApplyPrimarySeries(ToPrimary(candles, 20));

        ChartModuleProfile changedProfile = CreateProfile(
            "rsi-parameters",
            5,
            3,
            80d,
            20d,
            true);
        ChartModuleOperationResult changed = host.UpsertProfile(changedProfile);
        if (!changed.Succeeded || !changed.Changed)
            throw new InvalidOperationException(
                changed.Error ?? "RSI parameter profile did not change.");
        host.ApplyPrimarySeries(ToPrimary(candles, 21));

        ChartRenderPlan plan = new ChartCompositionService(host).Compose(
            new ChartVisualContext(21, 1, 1, 50, 100));
        Dictionary<string, RenderPrimitivePlan> byObject = plan.Primitives
            .ToDictionary(
                static item => item.Identity.ObjectId,
                StringComparer.Ordinal);
        IReadOnlyList<IndicatorPoint> expected =
            new RsiIndicator(5, 3).Calculate(candles);
        for (int index = 0; index < expected.Count; index++)
        {
            Fixture.Equal(
                expected[index].Value0,
                (float)byObject[RsiModule.RsiObjectId].Points[index].Y,
                $"RSI period=5 index={index}");
            Fixture.Equal(
                expected[index].Value1,
                (float)byObject[RsiModule.SignalObjectId].Points[index].Y,
                $"RSI signal=3 index={index}");
            Fixture.Equal(
                80f,
                (float)byObject[RsiModule.UpperObjectId].Points[index].Y,
                $"RSI upper=80 index={index}");
            Fixture.Equal(
                20f,
                (float)byObject[RsiModule.LowerObjectId].Points[index].Y,
                $"RSI lower=20 index={index}");
        }
    }

    private static RsiModule CreateActiveModule(
        string instanceId,
        int period,
        int signalPeriod,
        double upper,
        double lower)
    {
        RsiModule module = RsiModule.Create(instanceId);
        module.Initialize(SystemChartModuleContext.Instance);
        module.ApplyProfile(CreateProfile(
            instanceId,
            period,
            signalPeriod,
            upper,
            lower,
            true));
        module.Activate();
        return module;
    }

    private static ChartModuleProfile CreateProfile(
        string instanceId,
        int period,
        int signalPeriod,
        double upper,
        double lower,
        bool enabled) =>
        new()
        {
            ModuleId = RsiModule.Definition.ModuleId,
            InstanceId = instanceId,
            ModuleSchemaVersion = RsiModule.Definition.SchemaVersion,
            IsEnabled = enabled,
            ZIndex = 20,
            Placement = "indicator.1",
            Parameters = new JsonObject
            {
                ["period"] = period,
                ["signalPeriod"] = signalPeriod,
                ["upper"] = upper,
                ["lower"] = lower
            },
            Style = new JsonObject
            {
                ["stroke"] = "#FFFFFF",
                ["strokeWidth"] = 2d,
                [RsiModule.RsiObjectId + ".stroke"] = "#FF0000",
                [RsiModule.SignalObjectId + ".stroke"] = "#00FF00",
                [RsiModule.UpperObjectId + ".stroke"] = "#0000FF",
                [RsiModule.UpperObjectId + ".strokeWidth"] = 0.75d,
                [RsiModule.LowerObjectId + ".stroke"] = "#FFFF00"
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
        IReadOnlyList<RsiValuePoint> actual,
        string context)
    {
        if (expected.Count != actual.Count)
            throw new InvalidOperationException(
                $"RSI {context} count mismatch: " +
                $"expected={expected.Count}, actual={actual.Count}.");

        for (int index = 0; index < expected.Count; index++)
        {
            if (expected[index].Sequence != actual[index].Sequence)
                throw new InvalidOperationException(
                    $"RSI {context} sequence mismatch at {index}.");
            Fixture.Equal(
                expected[index].Value0,
                actual[index].Rsi,
                $"RSI {context} value index={index}");
            Fixture.Equal(
                expected[index].Value1,
                actual[index].Signal,
                $"RSI {context} signal index={index}");
            Fixture.Equal(
                70f,
                expected[index].Value2,
                $"RSI {context} legacy upper index={index}");
            Fixture.Equal(
                30f,
                expected[index].Value3,
                $"RSI {context} legacy lower index={index}");
        }
    }

    private static void AssertStyle(
        RenderPrimitivePlan primitive,
        string stroke,
        float strokeWidth)
    {
        if (primitive.Style.Stroke != stroke ||
            Math.Abs(primitive.Style.StrokeWidth - strokeWidth) > 0.0001f)
        {
            throw new InvalidOperationException(
                $"RSI style mismatch for {primitive.Identity.ObjectId}: " +
                $"stroke={primitive.Style.Stroke}, " +
                $"width={primitive.Style.StrokeWidth}.");
        }
    }

    private static void VerifyReferenceBoundary()
    {
        Assembly assembly = typeof(RsiModule).Assembly;
        string[] references = assembly.GetReferencedAssemblies()
            .Select(static item => item.Name ?? string.Empty)
            .ToArray();
        if (!references.Contains(
                "ChartKit.Modules.Abstractions",
                StringComparer.Ordinal))
        {
            throw new InvalidOperationException(
                "Indicator modules do not reference module abstractions.");
        }

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
            {
                throw new InvalidOperationException(
                    $"Indicator modules have forbidden reference: {name}");
            }
        }
    }

    private static void VerifyReleaseAssembly(Assembly assembly)
    {
        string? configuration = assembly
            .GetCustomAttribute<AssemblyConfigurationAttribute>()?
            .Configuration;
        if (!StringComparer.Ordinal.Equals(configuration, "Release"))
            throw new InvalidOperationException(
                "Indicator modules were not built in Release configuration.");
    }

    private sealed class RequirementWriter : IDataRequirementWriter
    {
        private readonly List<ChartDataRequirement> _items = new();
        public IReadOnlyList<ChartDataRequirement> Items => _items;
        public void Add(ChartDataRequirement requirement) => _items.Add(requirement);
    }

    private sealed class PropertyWriter : IChartPropertyWriter
    {
        private readonly List<ChartPropertyDescriptor> _items = new();
        public IReadOnlyList<ChartPropertyDescriptor> Items => _items;
        public void Add(ChartPropertyDescriptor descriptor) => _items.Add(descriptor);
    }
}
