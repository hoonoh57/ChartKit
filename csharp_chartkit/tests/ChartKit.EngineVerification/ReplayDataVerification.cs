using ChartKit.CSharp.Contracts;
using ChartKit.CSharp.DataSources;

namespace ChartKit.CSharp.EngineVerification;

internal static class ReplayDataVerification
{
    public static async Task RunAsync()
    {
        await using var source = new ReplayDataSource(new ReplayOptions(
            EventInterval: TimeSpan.FromMilliseconds(1),
            UpdatesPerCandle: 3,
            Seed: 1516));

        IReadOnlyList<Candle> history = await source.GetHistoryAsync(
            new HistoryRequest("AAA", CandleTimeframe.Minute(1), 50),
            CancellationToken.None);
        if (history.Count != 50 || history[^1].Sequence != 49)
            throw new InvalidOperationException("Replay history generation failed.");

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var events = new List<CandleEvent>();
        await foreach (CandleEvent value in source.StreamAsync(
                           new[] { "AAA", "BBB" },
                           CandleTimeframe.Minute(1),
                           timeout.Token))
        {
            events.Add(value);
            if (events.Count == 24) break;
        }

        if (events.Count != 24)
            throw new InvalidOperationException("Replay stream was incomplete.");
        if (events.Select(value => value.Symbol).Distinct().Count() != 2)
            throw new InvalidOperationException("Replay stream did not cover all symbols.");
        if (!events.Any(value => value.Kind == MarketEventKind.Append) ||
            !events.Any(value => value.Kind == MarketEventKind.Update))
            throw new InvalidOperationException("Replay stream did not produce append and update events.");

        foreach (IGrouping<string, CandleEvent> group in events.GroupBy(value => value.Symbol))
        {
            long previous = -1;
            foreach (CandleEvent value in group)
            {
                if (value.Candle.Sequence < previous ||
                    value.Candle.Sequence > previous + 1)
                    throw new InvalidOperationException(
                        $"Replay sequence failed for {group.Key}.");
                previous = value.Candle.Sequence;
            }
        }

        Console.WriteLine("csharp_replay_datasource=PASS");
    }
}
