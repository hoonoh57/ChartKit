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

internal static class SmaModuleParityVerification
{
    public static void Run()
    {
        VerifyDefinition();
        VerifyFullUpdateAppendAndRebuildParity();
        VerifyHostDisabledZeroAndContribution();
        VerifyPeriodChange();
        VerifyReferenceBoundary();

#if RELEASE
        VerifyReleaseAssembly(typeof(SmaModule).Assembly);
        Console.WriteLine("csharp_sma_module_release_configuration=PASS");
#endif
        Console.WriteLine("csharp_sma_module_definition=PASS");
        Console.WriteLine("csharp_sma_full_parity=PASS");
        Console.WriteLine("csharp_sma_update_parity=PASS");
        Console.WriteLine("csharp_sma_append_parity=PASS");
        Console.WriteLine("csharp_sma_rebuild_parity=PASS");
        Console.WriteLine("csharp_sma_disabled_zero=PASS");
        Console.WriteLine("csharp_sma_contribution=PASS");
        Console.WriteLine("csharp_sma_period_change=PASS");
        Console.WriteLine("csharp_sma_reference_boundary=PASS");
        Console.WriteLine("csharp_sma_module_contracts=PASS");
    }

    private static void VerifyDefinition()
    {
        ChartModuleDefinition definition = SmaModule.Definition;
        if (definition.ModuleId != "indicator.sma" ||
            definition.Category != "Indicators" ||
            definition.DefaultPanelId != "price.main" ||
            definition.DefaultEnabled ||
            !definition.Capabilities.HasFlag(ChartModuleCapabilities.DataRequirements) ||
            !definition.Capabilities.HasFlag(ChartModuleCapabilities.Computation) ||
            !definition.Capabilities.HasFlag(ChartModuleCapabilities.Visual) ||
            !definition.SupportedPrimitiveKinds.SequenceEqual(
                [ChartPrimitiveKind.Polyline]))
        {
            throw new InvalidOperationException(
                "SMA module definition is incomplete.");
        }

        SmaModule module = SmaModule.Create("sma-definition");
        module.Initialize(SystemChartModuleContext.Instance);
        module.ApplyProfile(CreateProfile("sma-definition", 20, false));
        var requirements = new RequirementWriter();
        module.DescribeRequirements(requirements);
        if (requirements.Items.Count != 1 ||
            requirements.Items[0].DataKind != "OHLCV")
        {
            throw new InvalidOperationException(
                "SMA primary OHLCV requirement is missing.");
        }
    }

    private static void VerifyFullUpdateAppendAndRebuildParity()
    {
        List<Candle> candles = Fixture.CreateCandles(240);
        var legacy = new MaIndicator(20, "SMA");
        IReadOnlyList<IndicatorPoint> expected = legacy.Calculate(candles);

        SmaModule module = CreateActiveModule("sma-parity", 20);
        module.ApplyPrimarySeries(ToPrimary(candles, 1));
        AssertParity(expected, module.SnapshotValues(), "full");
        if (module.Diagnostics.FullCalculations != 1)
            throw new InvalidOperationException(
                "SMA full calculation diagnostic was not recorded.");

        Candle changed = candles[^1] with
        {
            Close = candles[^1].Close + 7.25f,
            High = Math.Max(candles[^1].High, candles[^1].Close + 8f),
            IsFinal = false
        };
        candles[^1] = changed;
        IndicatorPoint expectedUpdate = legacy.UpdateLast(candles);
        module.ApplyPrimarySeries(ToPrimary(candles, 2));
        Fixture.Equal(
            expectedUpdate.Value0,
            module.SnapshotValues()[^1].Value,
            "SMA last update parity");
        if (module.Diagnostics.LastUpdates != 1)
            throw new InvalidOperationException(
                "SMA last-update path was not used.");

        Candle previous = candles[^1];
        var appended = new Candle(
            previous.CloseTime,
            previous.CloseTime.AddMinutes(1),
            previous.Close,
            previous.Close + 3f,
            previous.Close - 1f,
            previous.Close + 2f,
            previous.Volume + 100,
            true,
            previous.Sequence + 1);
        candles.Add(appended);
        IndicatorPoint expectedAppend = legacy.UpdateLast(candles);
        module.ApplyPrimarySeries(ToPrimary(candles, 3));
        Fixture.Equal(
            expectedAppend.Value0,
            module.SnapshotValues()[^1].Value,
            "SMA append parity");
        if (module.Diagnostics.Appends != 1)
            throw new InvalidOperationException("SMA append path was not used.");

        List<Candle> rolling = candles.Skip(1).ToList();
        IReadOnlyList<IndicatorPoint> rollingExpected =
            new MaIndicator(20, "SMA").Calculate(rolling);
        module.ApplyPrimarySeries(ToPrimary(rolling, 4));
        AssertParity(rollingExpected, module.SnapshotValues(), "rolling rebuild");
        if (module.Diagnostics.FullCalculations != 2)
            throw new InvalidOperationException(
                "SMA rolling snapshot did not fall back to full calculation.");
    }

    private static void VerifyHostDisabledZeroAndContribution()
    {
        var registry = new ChartModuleRegistry();
        registry.Register<SmaModule>();
        var host = new ChartModuleHost(registry);
        ChartModuleProfile profile = CreateProfile("sma-host", 20, false);
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
                "Disabled SMA module performed a calculation.");
        }

        ChartModuleOperationResult enabled = host.SetEnabled("sma-host", true);
        if (!enabled.Succeeded)
            throw new InvalidOperationException(enabled.Error);
        ChartModuleDataUpdateResult updated =
            host.ApplyPrimarySeries(ToPrimary(candles, 11));
        if (updated.EligibleModules != 1 ||
            updated.UpdatedModules != 1 ||
            updated.FaultedModules != 0)
        {
            throw new InvalidOperationException(
                "Enabled SMA module did not consume primary data.");
        }

        var composition = new ChartCompositionService(host);
        ChartRenderPlan plan = composition.Compose(
            new ChartVisualContext(11, 1, 1, 80, 120));
        if (plan.Primitives.Count != 1 ||
            plan.Primitives[0].Identity.ModuleId != "indicator.sma" ||
            plan.Primitives[0].Identity.InstanceId != "sma-host" ||
            plan.Primitives[0].Identity.ObjectId != "sma.value" ||
            plan.Primitives[0].PrimitiveKind != ChartPrimitiveKind.Polyline ||
            plan.Primitives[0].Points.Count != candles.Count ||
            plan.Primitives[0].Style.Stroke != "#FFC107")
        {
            throw new InvalidOperationException(
                "SMA contribution was not compiled into the render plan.");
        }
    }

    private static void VerifyPeriodChange()
    {
        List<Candle> candles = Fixture.CreateCandles(80);
        var registry = new ChartModuleRegistry();
        registry.Register<SmaModule>();
        var host = new ChartModuleHost(registry);
        ChartModuleOperationResult initial =
            host.UpsertProfile(CreateProfile("sma-period", 20, true));
        if (!initial.Succeeded)
            throw new InvalidOperationException(initial.Error);
        host.ApplyPrimarySeries(ToPrimary(candles, 20));

        ChartModuleProfile periodFive =
            CreateProfile("sma-period", 5, true);
        ChartModuleOperationResult changed = host.UpsertProfile(periodFive);
        if (!changed.Succeeded || !changed.Changed)
            throw new InvalidOperationException(
                changed.Error ?? "SMA period profile did not change.");
        host.ApplyPrimarySeries(ToPrimary(candles, 21));

        ChartRenderPlan plan = new ChartCompositionService(host).Compose(
            new ChartVisualContext(21, 1, 1, 40, 80));
        IReadOnlyList<IndicatorPoint> expected =
            new MaIndicator(5, "SMA").Calculate(candles);
        IReadOnlyList<ChartSeriesPoint> actual = plan.Primitives.Single().Points;
        if (actual.Count != expected.Count)
            throw new InvalidOperationException("SMA period output count mismatch.");
        for (int index = 0; index < expected.Count; index++)
        {
            Fixture.Equal(
                expected[index].Value0,
                (float)actual[index].Y,
                $"SMA period=5 parity index={index}");
        }
    }

    private static SmaModule CreateActiveModule(string instanceId, int period)
    {
        SmaModule module = SmaModule.Create(instanceId);
        module.Initialize(SystemChartModuleContext.Instance);
        module.ApplyProfile(CreateProfile(instanceId, period, true));
        module.Activate();
        return module;
    }

    private static ChartModuleProfile CreateProfile(
        string instanceId,
        int period,
        bool enabled) =>
        new()
        {
            ModuleId = SmaModule.Definition.ModuleId,
            InstanceId = instanceId,
            ModuleSchemaVersion = SmaModule.Definition.SchemaVersion,
            IsEnabled = enabled,
            ZIndex = 10,
            Placement = "price.main",
            Parameters = new JsonObject
            {
                ["period"] = period
            },
            Style = new JsonObject
            {
                ["stroke"] = "#FFC107",
                ["strokeWidth"] = 1.5d,
                ["opacity"] = 1d
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
        IReadOnlyList<SmaValuePoint> actual,
        string context)
    {
        if (expected.Count != actual.Count)
            throw new InvalidOperationException(
                $"SMA {context} count mismatch: " +
                $"expected={expected.Count}, actual={actual.Count}.");

        for (int index = 0; index < expected.Count; index++)
        {
            if (expected[index].Sequence != actual[index].Sequence)
                throw new InvalidOperationException(
                    $"SMA {context} sequence mismatch at {index}.");
            Fixture.Equal(
                expected[index].Value0,
                actual[index].Value,
                $"SMA {context} index={index}");
        }
    }

    private static void VerifyReferenceBoundary()
    {
        Assembly assembly = typeof(SmaModule).Assembly;
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
}
