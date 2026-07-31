using ChartKit.CSharp.Contracts;
using ChartKit.CSharp.DataSources;
using ChartKit.CSharp.Engine;

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

        EngineMetrics metrics = engine.GetMetrics();
        Console.WriteLine($"self_test_symbols={options.Symbols.Length}");
        Console.WriteLine($"self_test_processed={metrics.ProcessedEvents}");
        Console.WriteLine("csharp_app_self_test=PASS");
        return 0;
    }
}
