using ChartKit.CSharp.Contracts;
using ChartKit.CSharp.Engine;

namespace ChartKit.CSharp.EngineVerification;

internal static class MultiSymbolVerification
{
    public static async Task RunAsync()
    {
        const int symbolCount = 20;
        const int historyCount = 10;
        const int finalSequence = 19;

        await using var engine = new MultiSymbolEngine(new MultiSymbolEngineOptions(
            WorkerCount: 4,
            QueueCapacityPerWorker: 64,
            CandleCapacity: 128,
            SnapshotBars: 64,
            SnapshotInterval: TimeSpan.FromMilliseconds(1)));

        for (int symbolIndex = 0; symbolIndex < symbolCount; symbolIndex++)
        {
            string symbol = Symbol(symbolIndex);
            List<Candle> history = Fixture.CreateCandles(historyCount, symbolIndex);
            await engine.LoadHistoryAsync(symbol, history);
        }

        for (int sequence = historyCount; sequence <= finalSequence; sequence++)
        {
            for (int symbolIndex = 0; symbolIndex < symbolCount; symbolIndex++)
            {
                string symbol = Symbol(symbolIndex);
                Candle candle = Fixture.CreateCandles(finalSequence + 1, symbolIndex)[sequence];
                await engine.PublishAsync(CandleEvent.Create(
                    symbol,
                    MarketEventKind.Append,
                    candle,
                    sequence));
            }
        }

        long expectedProcessed = symbolCount * (finalSequence - historyCount + 1L);
        await WaitForProcessedAsync(engine, expectedProcessed);
        await Task.Delay(10);

        for (int symbolIndex = 0; symbolIndex < symbolCount; symbolIndex++)
        {
            string symbol = Symbol(symbolIndex);
            Candle updated = Fixture.CreateCandles(finalSequence + 1, symbolIndex)[finalSequence]
                with
                {
                    Close = 1100f + symbolIndex,
                    High = 1101f + symbolIndex,
                    IsFinal = false
                };
            await engine.PublishAsync(CandleEvent.Create(
                symbol,
                MarketEventKind.Update,
                updated,
                finalSequence + 1L));
        }

        expectedProcessed += symbolCount;
        await WaitForProcessedAsync(engine, expectedProcessed);
        await Task.Delay(20);

        for (int symbolIndex = 0; symbolIndex < symbolCount; symbolIndex++)
        {
            string symbol = Symbol(symbolIndex);
            if (!engine.TryGetSnapshot(symbol, out SymbolSnapshot? snapshot) || snapshot is null)
                throw new InvalidOperationException($"Missing snapshot: {symbol}");
            if (snapshot.Candles.Length != finalSequence + 1)
                throw new InvalidOperationException($"Candle count mismatch: {symbol}");
            if (snapshot.Candles[^1].Sequence != finalSequence)
                throw new InvalidOperationException($"FIFO sequence mismatch: {symbol}");
            Fixture.Equal(1100f + symbolIndex, snapshot.Candles[^1].Close,
                $"snapshot-last-close/{symbol}");

            foreach (IndicatorSeriesSnapshot series in snapshot.Indicators)
            {
                if (series.Points.Length != snapshot.Candles.Length)
                    throw new InvalidOperationException(
                        $"Indicator alignment mismatch: {symbol}/{series.Descriptor.Id}");
                if (series.Points[^1].Sequence != snapshot.Candles[^1].Sequence)
                    throw new InvalidOperationException(
                        $"Indicator sequence mismatch: {symbol}/{series.Descriptor.Id}");
            }
        }

        EngineMetrics metrics = engine.GetMetrics();
        if (metrics.ProcessingErrors != 0)
            throw new InvalidOperationException($"Engine errors: {metrics.ProcessingErrors}");
        if (metrics.ProcessedEvents != expectedProcessed)
            throw new InvalidOperationException("Processed event count mismatch.");

        Console.WriteLine($"multi_symbol_count={symbolCount}");
        Console.WriteLine($"processed_events={metrics.ProcessedEvents}");
        Console.WriteLine($"max_queue_depth={metrics.MaxQueueDepth}");
        Console.WriteLine("multi_symbol_fifo=PASS");
    }

    private static async Task WaitForProcessedAsync(
        MultiSymbolEngine engine,
        long expected)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        while (engine.GetMetrics().ProcessedEvents < expected)
        {
            await Task.Delay(2, timeout.Token);
        }
    }

    private static string Symbol(int index) => $"S{index:000}";
}
