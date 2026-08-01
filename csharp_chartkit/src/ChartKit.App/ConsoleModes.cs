using System.Text.Json.Nodes;
using ChartKit.CSharp.Contracts;
using ChartKit.CSharp.DataSources;
using ChartKit.CSharp.Engine;
using ChartKit.CSharp.Modules.Abstractions;
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

        try
        {
            using (var controller = new ChartModulePlatformController(profilePath))
            {
                await controller.InitializeAsync(timeframe.ToString());
                ChartUiCatalogSnapshot initial = controller.BuildUiCatalog();
                ChartUiCommandItem toggle = initial.ContextMenuItems.Single(
                    static item => item.Kind == ChartUiCommandKind.ModuleToggle);
                ChartUiCommandItem inspect = initial.QuickToolbarItems.Single(
                    static item => item.Kind == ChartUiCommandKind.ModuleCommand);

                if (initial.ContextMenuItems.Count < 2 ||
                    !initial.ContextMenuItems.Contains(toggle))
                {
                    throw new InvalidOperationException(
                        "App module context-menu projection failed.");
                }
                if (initial.QuickToolbarItems.Count < 2 ||
                    !initial.QuickToolbarItems.Contains(inspect))
                {
                    throw new InvalidOperationException(
                        "App module quick-toolbar projection failed.");
                }

                ChartModulePlatformActionResult enabled =
                    await controller.ExecuteCommandAsync(toggle);
                if (!enabled.Succeeded || !enabled.Changed)
                    throw new InvalidOperationException("App module toggle failed.");

                // Toggle already selects its owner. Inspect may therefore be an
                // idempotent no-op; final owner identity is the actual contract.
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
                        static item => item.Kind ==
                            ChartUiCommandKind.ModuleToggle);
                if (!restoredToggle.IsChecked)
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
            }

            Console.WriteLine("csharp_app_module_profile_load=PASS");
            Console.WriteLine("csharp_app_module_context_menu=PASS");
            Console.WriteLine("csharp_app_module_quick_toolbar=PASS");
            Console.WriteLine("csharp_app_module_property_inspector=PASS");
            Console.WriteLine("csharp_app_module_property_roundtrip=PASS");
            Console.WriteLine("csharp_app_module_render_plan=PASS");
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }
}
