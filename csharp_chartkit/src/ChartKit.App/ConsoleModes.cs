using System.Text.Json.Nodes;
using ChartKit.CSharp.Contracts;
using ChartKit.CSharp.DataSources;
using ChartKit.CSharp.Engine;
using ChartKit.CSharp.Modules.Abstractions;
using ChartKit.CSharp.Modules.Indicators;
using ChartKit.CSharp.Modules.Platform;
using ChartKit.CSharp.Scene;
using ChartKit.CSharp.UiModel;

namespace ChartKit.CSharp.App;

internal static class ConsoleModes
{
    public static async Task<int> RunProbeAsync(AppOptions options)
    {
        await using var source = new KiwoomRestDataSource();
        string symbol = options.Symbols[0];
        IReadOnlyList<Candle> history = await source.GetHistoryAsync(
            new HistoryRequest(
                symbol,
                options.Timeframe,
                options.HistoryCount),
            CancellationToken.None);
        if (history.Count == 0)
            throw new InvalidOperationException("Kiwoom returned no candle data.");

        Console.WriteLine("kiwoom_csharp_probe=PASS");
        Console.WriteLine($"source={source.Name}");
        Console.WriteLine($"symbol={symbol}");
        Console.WriteLine($"timeframe={options.Timeframe}");
        Console.WriteLine($"candle_count={history.Count}");
        Console.WriteLine($"first_time={history[0].OpenTime:yyyy-MM-dd HH:mm:ss}");
        Console.WriteLine($"last_time={history[^1].CloseTime:yyyy-MM-dd HH:mm:ss}");
        Console.WriteLine($"last_close={history[^1].Close}");

        if (options.RealtimeProbeSeconds <= 0) return 0;

        using var timeout = new CancellationTokenSource(
            TimeSpan.FromSeconds(options.RealtimeProbeSeconds));
        int eventCount = 0;
        try
        {
            await foreach (CandleEvent value in source.StreamAsync(
                               new[] { symbol },
                               options.Timeframe,
                               timeout.Token))
            {
                eventCount++;
                Console.WriteLine(
                    $"realtime={value.Kind},{value.Candle.CloseTime:HH:mm:ss}," +
                    $"{value.Candle.Close},{value.Candle.Volume}");
                if (eventCount >= 5) break;
            }
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
        }

        Console.WriteLine($"realtime_event_count={eventCount}");
        return eventCount > 0 ? 0 : 2;
    }

    public static async Task<int> RunSelfTestAsync(AppOptions options)
    {
        await using var source = new ReplayDataSource(new ReplayOptions(
            EventInterval: TimeSpan.FromMilliseconds(1),
            UpdatesPerCandle: 3));
        await using var engine = new MultiSymbolEngine(new MultiSymbolEngineOptions(
            WorkerCount: 4,
            QueueCapacityPerWorker: 256,
            CandleCapacity: 2048,
            SnapshotBars: 300,
            SnapshotInterval: TimeSpan.FromMilliseconds(1)));

        foreach (string symbol in options.Symbols)
        {
            IReadOnlyList<Candle> history = await source.GetHistoryAsync(
                new HistoryRequest(
                    symbol,
                    options.Timeframe,
                    Math.Min(options.HistoryCount, 300)),
                CancellationToken.None);
            await engine.LoadHistoryAsync(symbol, history);
        }

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        int accepted = 0;
        await foreach (CandleEvent value in source.StreamAsync(
                           options.Symbols,
                           options.Timeframe,
                           timeout.Token))
        {
            await engine.PublishAsync(value, timeout.Token);
            if (++accepted >= options.Symbols.Length * 10) break;
        }

        using var wait = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (engine.GetMetrics().ProcessedEvents < accepted)
            await Task.Delay(2, wait.Token);

        foreach (string symbol in options.Symbols)
        {
            if (!engine.TryGetSnapshot(symbol, out SymbolSnapshot? snapshot) ||
                snapshot is null || snapshot.Candles.Length == 0)
                throw new InvalidOperationException(
                    $"Self-test snapshot failed for {symbol}.");
        }

        await RunModulePlatformSelfTestAsync(options.Timeframe);

        EngineMetrics metrics = engine.GetMetrics();
        Console.WriteLine($"self_test_symbols={options.Symbols.Length}");
        Console.WriteLine($"self_test_processed={metrics.ProcessedEvents}");
        Console.WriteLine("csharp_app_self_test=PASS");
        return 0;
    }

    private static async Task RunModulePlatformSelfTestAsync(
        CandleTimeframe timeframe)
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "chartkit-app-module-self-test-" + Guid.NewGuid().ToString("N"));
        string profilePath = Path.Combine(directory, "chart-profile.json");
        ChartPrimarySeriesSnapshot primary = CreateSmaPrimarySeries();

        try
        {
            using (var controller = new ChartModulePlatformController(profilePath))
            {
                await controller.InitializeAsync(timeframe.ToString());
                ChartUiCatalogSnapshot initial = controller.BuildUiCatalog();
                ChartUiCommandItem toggle = initial.ContextMenuItems.Single(
                    static item =>
                        item.Kind == ChartUiCommandKind.ModuleToggle &&
                        item.Owner.ModuleId == PlatformProbeModule.Definition.ModuleId);
                ChartUiCommandItem inspect = initial.QuickToolbarItems.Single(
                    static item =>
                        item.Kind == ChartUiCommandKind.ModuleCommand &&
                        item.Owner.ModuleId == PlatformProbeModule.Definition.ModuleId);
                ChartUiCommandItem smaToggle = initial.ContextMenuItems.Single(
                    static item =>
                        item.Kind == ChartUiCommandKind.ModuleToggle &&
                        item.Owner.ModuleId == SmaModule.Definition.ModuleId);

                if (initial.ContextMenuItems.Count < 4 ||
                    !initial.ContextMenuItems.Contains(toggle) ||
                    !initial.ContextMenuItems.Contains(smaToggle))
                {
                    throw new InvalidOperationException(
                        "App module context-menu projection failed.");
                }
                if (initial.QuickToolbarItems.Count < 4 ||
                    !initial.QuickToolbarItems.Contains(inspect))
                {
                    throw new InvalidOperationException(
                        "App module quick-toolbar projection failed.");
                }

                ChartModulePlatformActionResult enabled =
                    await controller.ExecuteCommandAsync(toggle);
                if (!enabled.Succeeded || !enabled.Changed)
                    throw new InvalidOperationException("App module toggle failed.");

                ChartModulePlatformActionResult selected =
                    await controller.ExecuteCommandAsync(inspect);
                if (!selected.Succeeded)
                    throw new InvalidOperationException("App module selection failed.");

                ChartUiCatalogSnapshot selectedCatalog =
                    controller.BuildUiCatalog();
                if (!selectedCatalog.Selection.HasValue ||
                    !selectedCatalog.Selection.Value.Equals(inspect.Owner))
                {
                    throw new InvalidOperationException(
                        "App module selection owner mismatch.");
                }
                if (selectedCatalog.InspectorProperties.Count != 3 ||
                    !selectedCatalog.InspectorProperties
                        .Select(static item => item.Descriptor.PropertyId)
                        .SequenceEqual(["amplitude", "level", "stroke"]))
                {
                    throw new InvalidOperationException(
                        "App module property-inspector projection failed.");
                }

                ChartPropertyChangeResult changed =
                    await controller.ChangePropertyAsync(
                        toggle.Owner.InstanceId,
                        "level",
                        JsonValue.Create(73d));
                if (!changed.Succeeded ||
                    !changed.Changed ||
                    changed.ChangeImpact != ChartChangeImpact.RebuildVisuals)
                {
                    throw new InvalidOperationException(
                        "App module property mutation failed.");
                }

                if (controller.RenderPlan.Primitives.Count != 1 ||
                    controller.RenderPlan.Primitives[0].Points[0].Y != 73d)
                {
                    throw new InvalidOperationException(
                        "App module render-plan recomposition failed.");
                }

                ChartModulePlatformActionResult smaEnabled =
                    await controller.ExecuteCommandAsync(smaToggle);
                if (!smaEnabled.Succeeded || !smaEnabled.Changed)
                    throw new InvalidOperationException("App SMA toggle failed.");

                await controller.UpdatePrimarySeriesAsync(
                    primary,
                    viewportVersion: 1,
                    themeVersion: 0,
                    visibleStartIndex: 0,
                    visibleEndExclusive: primary.Bars.Count);
                RenderPrimitivePlan smaPlan = controller.RenderPlan.Primitives.Single(
                    static item =>
                        item.Identity.ModuleId == SmaModule.Definition.ModuleId);
                AssertLastSma(smaPlan, 129.5d, "period 20");

                ChartPropertyChangeResult periodChanged =
                    await controller.ChangePropertyAsync(
                        smaToggle.Owner.InstanceId,
                        "period",
                        JsonValue.Create(5));
                if (!periodChanged.Succeeded ||
                    !periodChanged.Changed ||
                    periodChanged.ChangeImpact !=
                        ChartChangeImpact.RecalculateModule)
                {
                    throw new InvalidOperationException(
                        "App SMA period mutation failed.");
                }
                smaPlan = controller.RenderPlan.Primitives.Single(
                    static item =>
                        item.Identity.ModuleId == SmaModule.Definition.ModuleId);
                AssertLastSma(smaPlan, 137d, "period 5");

                await controller.UpdateShellProfileAsync(
                    timeframe.ToString(),
                    new JsonObject
                    {
                        ["visibleBars"] = 120,
                        ["infoPanelVisible"] = true
                    },
                    new JsonObject
                    {
                        ["datesVisible"] = true,
                        ["axesVisible"] = true,
                        ["legendVisible"] = true,
                        ["crosshairVisible"] = true
                    },
                    new JsonObject());
            }

            using (var restored = new ChartModulePlatformController(profilePath))
            {
                await restored.InitializeAsync(timeframe.ToString());
                ChartUiCatalogSnapshot restoredCatalog =
                    restored.BuildUiCatalog();
                ChartUiCommandItem restoredToggle =
                    restoredCatalog.ContextMenuItems.Single(
                        static item =>
                            item.Kind == ChartUiCommandKind.ModuleToggle &&
                            item.Owner.ModuleId ==
                                PlatformProbeModule.Definition.ModuleId);
                ChartUiCommandItem restoredSmaToggle =
                    restoredCatalog.ContextMenuItems.Single(
                        static item =>
                            item.Kind == ChartUiCommandKind.ModuleToggle &&
                            item.Owner.ModuleId == SmaModule.Definition.ModuleId);
                if (!restoredToggle.IsChecked || !restoredSmaToggle.IsChecked)
                    throw new InvalidOperationException(
                        "App module enabled state was not restored.");

                restored.Select(restoredToggle.Owner);
                ChartUiPropertyItem restoredLevel = restored
                    .BuildUiCatalog()
                    .InspectorProperties
                    .Single(static item =>
                        item.Descriptor.PropertyId == "level");
                if (Convert.ToDouble(restoredLevel.Descriptor.Value) != 73d ||
                    restored.Profile.Layout["visibleBars"]?.GetValue<int>() != 120 ||
                    restored.RenderPlan.Primitives.Count != 1 ||
                    restored.RenderPlan.Primitives[0].Points[0].Y != 73d)
                {
                    throw new InvalidOperationException(
                        "App module profile round-trip failed.");
                }

                restored.Select(restoredSmaToggle.Owner);
                ChartUiPropertyItem restoredPeriod = restored
                    .BuildUiCatalog()
                    .InspectorProperties
                    .Single(static item =>
                        item.Descriptor.PropertyId == "period");
                if (Convert.ToInt32(restoredPeriod.Descriptor.Value) != 5)
                    throw new InvalidOperationException(
                        "App SMA period was not restored.");

                await restored.UpdatePrimarySeriesAsync(
                    primary,
                    viewportVersion: 1,
                    themeVersion: 0,
                    visibleStartIndex: 0,
                    visibleEndExclusive: primary.Bars.Count);
                RenderPrimitivePlan restoredSma =
                    restored.RenderPlan.Primitives.Single(
                        static item =>
                            item.Identity.ModuleId ==
                                SmaModule.Definition.ModuleId);
                AssertLastSma(restoredSma, 137d, "restored period 5");
            }

            Console.WriteLine("csharp_app_module_profile_load=PASS");
            Console.WriteLine("csharp_app_module_context_menu=PASS");
            Console.WriteLine("csharp_app_module_quick_toolbar=PASS");
            Console.WriteLine("csharp_app_module_property_inspector=PASS");
            Console.WriteLine("csharp_app_module_property_roundtrip=PASS");
            Console.WriteLine("csharp_app_module_render_plan=PASS");
            Console.WriteLine("csharp_app_sma_module_data=PASS");
            Console.WriteLine("csharp_app_sma_module_period=PASS");
            Console.WriteLine("csharp_app_sma_module_roundtrip=PASS");
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    private static ChartPrimarySeriesSnapshot CreateSmaPrimarySeries()
    {
        var bars = new ChartPrimaryBar[40];
        for (int index = 0; index < bars.Length; index++)
        {
            double close = 100d + index;
            bars[index] = new ChartPrimaryBar(
                index,
                close - 0.5d,
                close + 1d,
                close - 1d,
                close,
                1_000L + index,
                true);
        }
        return new ChartPrimarySeriesSnapshot(100, bars);
    }

    private static void AssertLastSma(
        RenderPrimitivePlan plan,
        double expected,
        string context)
    {
        double actual = plan.Points[^1].Y;
        if (Math.Abs(actual - expected) > 0.0001d)
        {
            throw new InvalidOperationException(
                $"App SMA {context} mismatch: expected={expected}, actual={actual}.");
        }
    }
}
