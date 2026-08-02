using ChartKit.CSharp.Contracts;
using ChartKit.CSharp.DataSources;

namespace ChartKit.CSharp.App;

internal static class RealtimeValidationProbe
{
    private const int InitialSampleCount = 10;
    private const int PeriodicSampleInterval = 500;

    public static async Task<int> RunAsync(AppOptions options)
    {
        await using var source = new KiwoomRestDataSource();
        var eventCounts = new Dictionary<string, long>(StringComparer.Ordinal);

        Console.WriteLine("kiwoom_realtime_validation_probe=START");
        Console.WriteLine($"source={source.Name}");
        Console.WriteLine($"timeframe={options.Timeframe}");
        Console.WriteLine($"history_count={options.HistoryCount}");
        Console.WriteLine($"realtime_seconds={options.RealtimeProbeSeconds}");
        Console.WriteLine($"symbol_count={options.Symbols.Length}");

        foreach (string symbol in options.Symbols)
        {
            IReadOnlyList<Candle> history = await source.GetHistoryAsync(
                new HistoryRequest(
                    symbol,
                    options.Timeframe,
                    options.HistoryCount),
                CancellationToken.None);
            if (history.Count == 0)
                throw new InvalidOperationException(
                    $"Kiwoom returned no candle data for {symbol}.");

            eventCounts[symbol] = 0;
            Candle first = history[0];
            Candle last = history[^1];
            Console.WriteLine(
                $"history={symbol},count={history.Count}," +
                $"first={first.OpenTime:yyyy-MM-dd HH:mm:ss}," +
                $"last={last.CloseTime:yyyy-MM-dd HH:mm:ss}," +
                $"last_close={last.Close},last_sequence={last.Sequence}");
        }

        if (options.RealtimeProbeSeconds <= 0)
        {
            Console.WriteLine("kiwoom_history_validation=PASS");
            return 0;
        }

        DateTimeOffset startedAt = DateTimeOffset.Now;
        long totalEvents = 0;
        using var timeout = new CancellationTokenSource(
            TimeSpan.FromSeconds(options.RealtimeProbeSeconds));

        try
        {
            await foreach (CandleEvent value in source.StreamAsync(
                               options.Symbols,
                               options.Timeframe,
                               timeout.Token))
            {
                totalEvents++;
                if (!eventCounts.TryAdd(value.Symbol, 1))
                    eventCounts[value.Symbol]++;

                if (totalEvents <= InitialSampleCount ||
                    totalEvents % PeriodicSampleInterval == 0)
                {
                    Console.WriteLine(
                        $"realtime_sample={totalEvents},symbol={value.Symbol}," +
                        $"kind={value.Kind},time={value.Candle.CloseTime:yyyy-MM-dd HH:mm:ss}," +
                        $"close={value.Candle.Close},volume={value.Candle.Volume}," +
                        $"sequence={value.Candle.Sequence}");
                }
            }
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
        }

        DateTimeOffset endedAt = DateTimeOffset.Now;
        bool diagnosticsConsistent = true;
        bool seedContinuityObserved = false;
        bool reconnectObserved = false;

        foreach (string symbol in options.Symbols)
        {
            RealtimeDiagnosticsSnapshot diagnostics =
                source.GetRealtimeDiagnostics(symbol);
            long observed = eventCounts.GetValueOrDefault(symbol);
            bool acceptedMatches = diagnostics.AcceptedEvents == observed;
            bool registrationValid =
                diagnostics.RegistrationCount <= diagnostics.ConnectionAttempts &&
                (diagnostics.AcceptedEvents == 0 || diagnostics.RegistrationCount > 0);
            bool boundaryValid = diagnostics.AcceptedEvents == 0 ||
                diagnostics.BoundaryState is
                    RealtimeBoundaryState.SeedUpdated or
                    RealtimeBoundaryState.SeedAppended or
                    RealtimeBoundaryState.UnseededAppended;

            diagnosticsConsistent &= acceptedMatches && registrationValid && boundaryValid;
            seedContinuityObserved |= diagnostics.BoundaryState is
                RealtimeBoundaryState.SeedUpdated or
                RealtimeBoundaryState.SeedAppended;
            reconnectObserved |= diagnostics.ConnectionAttempts >= 2 &&
                                 diagnostics.RegistrationCount >= 2;

            Console.WriteLine(
                $"diagnostics={symbol},state={diagnostics.ConnectionState}," +
                $"boundary={diagnostics.BoundaryState}," +
                $"seed_open={FormatTime(diagnostics.SeedOpenTime)}," +
                $"seed_close={FormatTime(diagnostics.SeedCloseTime)}," +
                $"first_realtime={FormatTime(diagnostics.FirstRealtimeTime)}," +
                $"first_kind={diagnostics.FirstEventKind?.ToString() ?? "none"}," +
                $"accepted={diagnostics.AcceptedEvents}," +
                $"updates={diagnostics.UpdateEvents}," +
                $"appends={diagnostics.AppendEvents}," +
                $"stale={diagnostics.RejectedStaleEvents}," +
                $"attempts={diagnostics.ConnectionAttempts}," +
                $"registrations={diagnostics.RegistrationCount}," +
                $"observed={observed}," +
                $"consistent={acceptedMatches && registrationValid && boundaryValid}," +
                $"last_error={Sanitize(diagnostics.LastError)}");
        }

        double elapsedSeconds = Math.Max(
            0d,
            (endedAt - startedAt).TotalSeconds);
        Console.WriteLine($"probe_started_at={startedAt:O}");
        Console.WriteLine($"probe_ended_at={endedAt:O}");
        Console.WriteLine($"probe_elapsed_seconds={elapsedSeconds:F3}");
        Console.WriteLine($"realtime_event_count={totalEvents}");
        Console.WriteLine($"rest_seed_continuity_observed={seedContinuityObserved}");
        Console.WriteLine($"physical_reconnect_observed={reconnectObserved}");
        Console.WriteLine($"diagnostics_consistent={diagnosticsConsistent}");

        if (totalEvents == 0)
        {
            Console.WriteLine("kiwoom_realtime_validation_probe=NO_EVENTS");
            return 2;
        }
        if (!diagnosticsConsistent)
        {
            Console.WriteLine("kiwoom_realtime_validation_probe=DIAGNOSTICS_MISMATCH");
            return 3;
        }

        Console.WriteLine("kiwoom_realtime_validation_probe=PASS");
        return 0;
    }

    private static string FormatTime(DateTime? value) =>
        value.HasValue
            ? value.Value.ToString("yyyy-MM-dd HH:mm:ss")
            : "none";

    private static string Sanitize(string value) =>
        string.IsNullOrWhiteSpace(value)
            ? "none"
            : value.Trim()
                .Replace('\r', ' ')
                .Replace('\n', ' ')
                .Replace(',', ';');
}
