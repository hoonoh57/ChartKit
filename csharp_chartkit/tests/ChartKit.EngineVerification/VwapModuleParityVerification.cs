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

internal static class VwapModuleParityVerification
{
    public static void Run()
    {
        VerifyDefinitionAndMetadata();
        VerifyFullUpdateAppendSessionResetAndRebuildParity();
        VerifyHostDisabledZeroAndContributions();
        VerifyParameterChange();
        VerifyTradingDateRequirement();
        VerifyReferenceBoundary();

#if RELEASE
        VerifyReleaseAssembly(typeof(VwapModule).Assembly);
        Console.WriteLine("csharp_vwap_module_release_configuration=PASS");
#endif
        Console.WriteLine("csharp_vwap_module_definition=PASS");
        Console.WriteLine("csharp_vwap_module_metadata=PASS");
        Console.WriteLine("csharp_vwap_full_parity=PASS");
        Console.WriteLine("csharp_vwap_update_parity=PASS");
        Console.WriteLine("csharp_vwap_append_parity=PASS");
        Console.WriteLine("csharp_vwap_session_reset_parity=PASS");
        Console.WriteLine("csharp_vwap_rebuild_parity=PASS");
        Console.WriteLine("csharp_vwap_zero_volume_parity=PASS");
        Console.WriteLine("csharp_vwap_disabled_zero=PASS");
        Console.WriteLine("csharp_vwap_contributions=PASS");
        Console.WriteLine("csharp_vwap_panel_contract=PASS");
        Console.WriteLine("csharp_vwap_style_override=PASS");
        Console.WriteLine("csharp_vwap_parameter_change=PASS");
        Console.WriteLine("csharp_vwap_trading_date_contract=PASS");
        Console.WriteLine("csharp_vwap_reference_boundary=PASS");
        Console.WriteLine("csharp_vwap_module_contracts=PASS");
    }

    private static void VerifyDefinitionAndMetadata()
    {
        ChartModuleDefinition definition = VwapModule.Definition;
        if (definition.ModuleId != "indicator.vwap" ||
            definition.Category != "Indicators" ||
            definition.DefaultPanelId != "price.main" ||
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
                "VWAP module definition is incomplete.");
        }

        VwapModule module = VwapModule.Create("vwap-definition");
        module.Initialize(SystemChartModuleContext.Instance);
        module.ApplyProfile(CreateProfile(
            "vwap-definition",
            1d,
            2d,
            false));

        var requirements = new RequirementWriter();
        module.DescribeRequirements(requirements);
        if (requirements.Items.Count != 1 ||
            requirements.Items[0].DataKind != "OHLCV+TradingDate")
        {
            throw new InvalidOperationException(
                "VWAP OHLCV and TradingDate requirement is missing.");
        }

        var properties = new PropertyWriter();
        module.DescribeProperties(properties);
        string[] expectedProperties =
        [
            "stdDev1",
            "stdDev2",
            "vwap.lower1.stroke",
            "vwap.lower2.stroke",
            "vwap.upper1.stroke",
            "vwap.upper2.stroke",
            "vwap.value.stroke"
        ];
        if (!properties.Items
                .Select(static item => item.PropertyId)
                .OrderBy(static item => item, StringComparer.Ordinal)
                .SequenceEqual(expectedProperties))
        {
            throw new InvalidOperationException(
                "VWAP property metadata is incomplete.");
        }
    }

    private static void VerifyFullUpdateAppendSessionResetAndRebuildParity()
    {
        List<Candle> candles = CreateMultiDayCandles(days: 2, barsPerDay: 40);
        var legacy = new VwapIndicator(1f, 2f);
        IReadOnlyList<IndicatorPoint> expected = legacy.Calculate(candles);

        VwapModule module = CreateActiveModule("vwap-parity", 1d, 2d);
        module.ApplyPrimarySeries(ToPrimary(candles, 1));
        AssertParity(expected, module.SnapshotValues(), "full");
        if (module.Diagnostics.FullCalculations != 1)
            throw new InvalidOperationException(
                "VWAP full calculation diagnostic was not recorded.");

        Candle changed = candles[^1] with
        {
            High = candles[^1].High + 6f,
            Low = candles[^1].Low - 2f,
            Close = candles[^1].Close + 4f,
            Volume = candles[^1].Volume + 1_000,
            IsFinal = false
        };
        candles[^1] = changed;
        IndicatorPoint expectedUpdate = legacy.UpdateLast(candles);
        module.ApplyPrimarySeries(ToPrimary(candles, 2));
        AssertPoint(expectedUpdate, module.SnapshotValues()[^1], "update");
        if (module.Diagnostics.LastUpdates != 1)
            throw new InvalidOperationException(
                "VWAP last-update path was not used.");

        Candle previous = candles[^1];
        var sameDayAppend = new Candle(
            previous.CloseTime,
            previous.CloseTime.AddMinutes(1),
            previous.Close,
            previous.Close + 3f,
            previous.Close - 1f,
            previous.Close + 2f,
            previous.Volume + 250,
            true,
            previous.Sequence + 1);
        candles.Add(sameDayAppend);
        IndicatorPoint expectedAppend = legacy.UpdateLast(candles);
        module.ApplyPrimarySeries(ToPrimary(candles, 3));
        AssertPoint(expectedAppend, module.SnapshotValues()[^1], "append");
        if (module.Diagnostics.Appends != 1)
            throw new InvalidOperationException(
                "VWAP same-session append path was not used.");

        DateTime nextSession = sameDayAppend.OpenTime.Date
            .AddDays(1)
            .AddHours(9);
        var zeroVolumeSessionOpen = new Candle(
            nextSession,
            nextSession.AddMinutes(1),
            sameDayAppend.Close,
            sameDayAppend.Close + 2f,
            sameDayAppend.Close - 2f,
            sameDayAppend.Close + 1f,
            0,
            true,
            sameDayAppend.Sequence + 1);
        candles.Add(zeroVolumeSessionOpen);
        IndicatorPoint expectedSessionReset = legacy.UpdateLast(candles);
        module.ApplyPrimarySeries(ToPrimary(candles, 4));
        AssertPoint(
            expectedSessionReset,
            module.SnapshotValues()[^1],
            "session reset zero volume");
        if (!float.IsNaN(module.SnapshotValues()[^1].Value))
            throw new InvalidOperationException(
                "VWAP zero-volume session open must produce NaN outputs.");
        if (module.Diagnostics.Appends != 2 ||
            module.Diagnostics.SessionResets < 2)
        {
            throw new InvalidOperationException(
                "VWAP next-session append did not reset accumulated state.");
        }

        var positiveVolume = new Candle(
            zeroVolumeSessionOpen.CloseTime,
            zeroVolumeSessionOpen.CloseTime.AddMinutes(1),
            zeroVolumeSessionOpen.Close,
            zeroVolumeSessionOpen.Close + 2.5f,
            zeroVolumeSessionOpen.Close - 0.5f,
            zeroVolumeSessionOpen.Close + 2f,
            5_000,
            true,
            zeroVolumeSessionOpen.Sequence + 1);
        candles.Add(positiveVolume);
        IndicatorPoint expectedAfterReset = legacy.UpdateLast(candles);
        module.ApplyPrimarySeries(ToPrimary(candles, 5));
        AssertPoint(
            expectedAfterReset,
            module.SnapshotValues()[^1],
            "session reset positive volume");

        List<Candle> rolling = candles.Skip(7).ToList();
        IReadOnlyList<IndicatorPoint> rollingExpected =
            new VwapIndicator(1f, 2f).Calculate(rolling);
        module.ApplyPrimarySeries(ToPrimary(rolling, 6));
        AssertParity(
            rollingExpected,
            module.SnapshotValues(),
            "rolling rebuild");
        if (module.Diagnostics.FullCalculations != 2)
            throw new InvalidOperationException(
                "VWAP rolling snapshot did not use full recalculation.");
    }

    private static void VerifyHostDisabledZeroAndContributions()
    {
        var registry = new ChartModuleRegistry();
        registry.Register<VwapModule>();
        var host = new ChartModuleHost(registry);
        ChartModuleOperationResult hosted = host.UpsertProfile(
            CreateProfile("vwap-host", 1d, 2d, false));
        if (!hosted.Succeeded)
            throw new InvalidOperationException(hosted.Error);

        List<Candle> candles = CreateMultiDayCandles(days: 3, barsPerDay: 40);
        ChartModuleDataUpdateResult disabled =
            host.ApplyPrimarySeries(ToPrimary(candles, 10));
        if (disabled.EligibleModules != 0 ||
            disabled.UpdatedModules != 0 ||
            disabled.FaultedModules != 0)
        {
            throw new InvalidOperationException(
                "Disabled VWAP module performed a calculation.");
        }

        ChartModuleOperationResult enabled = host.SetEnabled("vwap-host", true);
        if (!enabled.Succeeded)
            throw new InvalidOperationException(enabled.Error);
        ChartModuleDataUpdateResult updated =
            host.ApplyPrimarySeries(ToPrimary(candles, 11));
        if (updated.EligibleModules != 1 ||
            updated.UpdatedModules != 1 ||
            updated.FaultedModules != 0)
        {
            throw new InvalidOperationException(
                "Enabled VWAP module did not consume primary data.");
        }

        ChartRenderPlan plan = new ChartCompositionService(host).Compose(
            new ChartVisualContext(11, 1, 1, 0, candles.Count));
        Dictionary<string, RenderPrimitivePlan> byObject = plan.Primitives
            .ToDictionary(
                static item => item.Identity.ObjectId,
                StringComparer.Ordinal);
        string[] expectedObjects =
        [
            VwapModule.Lower1ObjectId,
            VwapModule.Lower2ObjectId,
            VwapModule.Upper1ObjectId,
            VwapModule.Upper2ObjectId,
            VwapModule.ValueObjectId
        ];
        if (plan.Primitives.Count != 5 ||
            !byObject.Keys
                .OrderBy(static item => item, StringComparer.Ordinal)
                .SequenceEqual(expectedObjects))
        {
            throw new InvalidOperationException(
                "VWAP did not emit the five required render primitives.");
        }

        foreach (RenderPrimitivePlan primitive in plan.Primitives)
        {
            if (primitive.PanelId != "price.main" ||
                primitive.PrimitiveKind != ChartPrimitiveKind.Polyline ||
                primitive.Points.Count != candles.Count)
            {
                throw new InvalidOperationException(
                    "VWAP contribution or panel contract is invalid.");
            }
        }

        if (byObject[VwapModule.ValueObjectId].Style.Stroke != "#123456")
            throw new InvalidOperationException(
                "VWAP object-specific style override was not applied.");

        IReadOnlyList<IndicatorPoint> expected =
            new VwapIndicator(1f, 2f).Calculate(candles);
        for (int index = 0; index < expected.Count; index++)
        {
            AssertContribution(
                expected[index],
                byObject,
                index,
                "host");
        }
    }

    private static void VerifyParameterChange()
    {
        List<Candle> candles = CreateMultiDayCandles(days: 2, barsPerDay: 40);
        var registry = new ChartModuleRegistry();
        registry.Register<VwapModule>();
        var host = new ChartModuleHost(registry);
        ChartModuleOperationResult initial = host.UpsertProfile(
            CreateProfile("vwap-parameters", 1d, 2d, true));
        if (!initial.Succeeded)
            throw new InvalidOperationException(initial.Error);
        host.ApplyPrimarySeries(ToPrimary(candles, 20));

        ChartModuleOperationResult changed = host.UpsertProfile(
            CreateProfile("vwap-parameters", 1.5d, 3d, true));
        if (!changed.Succeeded || !changed.Changed)
            throw new InvalidOperationException(
                changed.Error ?? "VWAP parameter profile did not change.");
        host.ApplyPrimarySeries(ToPrimary(candles, 21));

        ChartRenderPlan plan = new ChartCompositionService(host).Compose(
            new ChartVisualContext(21, 1, 1, 0, candles.Count));
        Dictionary<string, RenderPrimitivePlan> byObject = plan.Primitives
            .ToDictionary(
                static item => item.Identity.ObjectId,
                StringComparer.Ordinal);
        IReadOnlyList<IndicatorPoint> expected =
            new VwapIndicator(1.5f, 3f).Calculate(candles);
        for (int index = 0; index < expected.Count; index++)
        {
            AssertContribution(
                expected[index],
                byObject,
                index,
                "parameter change");
        }
    }

    private static void VerifyTradingDateRequirement()
    {
        VwapModule module = CreateActiveModule(
            "vwap-missing-date",
            1d,
            2d);
        ChartPrimarySeriesSnapshot missingDate = new(
            30,
            [
                new ChartPrimaryBar(
                    1,
                    100d,
                    102d,
                    99d,
                    101d,
                    1_000,
                    true)
            ]);
        try
        {
            module.ApplyPrimarySeries(missingDate);
        }
        catch (InvalidOperationException exception)
            when (exception.Message.Contains(
                "TradingDate",
                StringComparison.Ordinal))
        {
            return;
        }

        throw new InvalidOperationException(
            "VWAP accepted a primary bar without TradingDate.");
    }

    private static VwapModule CreateActiveModule(
        string instanceId,
        double stdDev1,
        double stdDev2)
    {
        VwapModule module = VwapModule.Create(instanceId);
        module.Initialize(SystemChartModuleContext.Instance);
        module.ApplyProfile(CreateProfile(
            instanceId,
            stdDev1,
            stdDev2,
            true));
        module.Activate();
        return module;
    }

    private static ChartModuleProfile CreateProfile(
        string instanceId,
        double stdDev1,
        double stdDev2,
        bool enabled) =>
        new()
        {
            ModuleId = VwapModule.Definition.ModuleId,
            InstanceId = instanceId,
            ModuleSchemaVersion = VwapModule.Definition.SchemaVersion,
            IsEnabled = enabled,
            ZIndex = 15,
            Placement = "price.main",
            Parameters = new JsonObject
            {
                ["stdDev1"] = stdDev1,
                ["stdDev2"] = stdDev2
            },
            Style = new JsonObject
            {
                [VwapModule.ValueObjectId + ".stroke"] = "#123456",
                [VwapModule.Upper1ObjectId + ".stroke"] = "#FFEB3B",
                [VwapModule.Lower1ObjectId + ".stroke"] = "#FFEB3B",
                [VwapModule.Upper2ObjectId + ".stroke"] = "#FF7043",
                [VwapModule.Lower2ObjectId + ".stroke"] = "#FF7043"
            },
            PersistentState = new JsonObject()
        };

    private static List<Candle> CreateMultiDayCandles(
        int days,
        int barsPerDay)
    {
        var candles = new List<Candle>(days * barsPerDay);
        DateTime firstDate = new(2026, 7, 29, 9, 0, 0);
        float previous = 100f;
        long sequence = 1;
        for (int day = 0; day < days; day++)
        {
            DateTime session = firstDate.AddDays(day);
            for (int index = 0; index < barsPerDay; index++)
            {
                DateTime openTime = session.AddMinutes(index);
                float close = 100f + day * 4f +
                    (float)Math.Sin((day * barsPerDay + index) / 6d) * 5f +
                    index * 0.05f;
                float high = Math.Max(previous, close) + 1.5f;
                float low = Math.Min(previous, close) - 1.25f;
                long volume = 1_000L + day * 500L + index * 23L;
                candles.Add(new Candle(
                    openTime,
                    openTime.AddMinutes(1),
                    previous,
                    high,
                    low,
                    close,
                    volume,
                    true,
                    sequence++));
                previous = close;
            }
        }
        return candles;
    }

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
                DateOnly.FromDateTime(candle.TradingDate),
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
        IReadOnlyList<VwapValuePoint> actual,
        string context)
    {
        if (expected.Count != actual.Count)
            throw new InvalidOperationException(
                $"VWAP {context} count mismatch: " +
                $"expected={expected.Count}, actual={actual.Count}.");
        for (int index = 0; index < expected.Count; index++)
        {
            AssertPoint(expected[index], actual[index], $"{context} index={index}");
        }
    }

    private static void AssertPoint(
        IndicatorPoint expected,
        VwapValuePoint actual,
        string context)
    {
        if (expected.Sequence != actual.Sequence)
            throw new InvalidOperationException(
                $"VWAP {context} sequence mismatch.");
        Fixture.Equal(expected.Value0, actual.Value, $"VWAP {context} value");
        Fixture.Equal(expected.Value1, actual.Upper1, $"VWAP {context} upper1");
        Fixture.Equal(expected.Value2, actual.Lower1, $"VWAP {context} lower1");
        Fixture.Equal(expected.Value3, actual.Upper2, $"VWAP {context} upper2");
        Fixture.Equal(expected.Value4, actual.Lower2, $"VWAP {context} lower2");
    }

    private static void AssertContribution(
        IndicatorPoint expected,
        IReadOnlyDictionary<string, RenderPrimitivePlan> byObject,
        int index,
        string context)
    {
        Fixture.Equal(
            expected.Value0,
            (float)byObject[VwapModule.ValueObjectId].Points[index].Y,
            $"VWAP {context} value index={index}");
        Fixture.Equal(
            expected.Value1,
            (float)byObject[VwapModule.Upper1ObjectId].Points[index].Y,
            $"VWAP {context} upper1 index={index}");
        Fixture.Equal(
            expected.Value2,
            (float)byObject[VwapModule.Lower1ObjectId].Points[index].Y,
            $"VWAP {context} lower1 index={index}");
        Fixture.Equal(
            expected.Value3,
            (float)byObject[VwapModule.Upper2ObjectId].Points[index].Y,
            $"VWAP {context} upper2 index={index}");
        Fixture.Equal(
            expected.Value4,
            (float)byObject[VwapModule.Lower2ObjectId].Points[index].Y,
            $"VWAP {context} lower2 index={index}");
    }

    private static void VerifyReferenceBoundary()
    {
        Assembly assembly = typeof(VwapModule).Assembly;
        string[] references = assembly.GetReferencedAssemblies()
            .Select(static item => item.Name ?? string.Empty)
            .ToArray();
        if (!references.Contains(
                "ChartKit.Modules.Abstractions",
                StringComparer.Ordinal))
        {
            throw new InvalidOperationException(
                "VWAP module does not reference module abstractions.");
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
                    $"VWAP module has forbidden reference: {name}");
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
                "VWAP module was not built in Release configuration.");
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
