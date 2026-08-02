using ChartKit.CSharp.Contracts;
using ChartKit.CSharp.DataSources;

namespace ChartKit.CSharp.EngineVerification;

internal static class RealtimeBuilderVerification
{
    public static void Run()
    {
        VerifyMinute();
        VerifyTick();
        VerifyDiagnostics();
        Console.WriteLine("csharp_realtime_candle_builder=PASS");
        Console.WriteLine("csharp_realtime_stale_tick_rejection=PASS");
        Console.WriteLine("csharp_realtime_boundary_diagnostics=PASS");
    }

    private static void VerifyMinute()
    {
        DateTime open = new(2026, 7, 31, 9, 0, 0);
        Candle seed = new(
            open,
            open.AddMinutes(5),
            100f,
            103f,
            99f,
            102f,
            1000,
            false,
            10);
        var builder = new RealtimeCandleBuilder(
            CandleTimeframe.Minute(5), seed, 0);

        if (!builder.TryApply(
                open.AddMinutes(3), 104f, 50,
                out MarketEventKind updateKind,
                out Candle updated) ||
            updateKind != MarketEventKind.Update ||
            updated.High != 104f ||
            updated.Volume != 1050)
            throw new InvalidOperationException("Minute update failed.");

        if (builder.TryApply(
                open.AddMinutes(-1), 90f, 5,
                out _, out _))
            throw new InvalidOperationException("Delayed minute trade was not rejected.");

        if (!builder.TryApply(
                open.AddMinutes(5), 105f, 60,
                out MarketEventKind appendKind,
                out Candle appended) ||
            appendKind != MarketEventKind.Append ||
            appended.Sequence != 11 ||
            appended.OpenTime != open.AddMinutes(5))
            throw new InvalidOperationException("Minute append failed.");
    }

    private static void VerifyTick()
    {
        DateTime time = new(2026, 7, 31, 9, 0, 0);
        Candle seed = new(time, time, 100f, 101f, 99f, 100f, 100, false, 7);
        var builder = new RealtimeCandleBuilder(
            CandleTimeframe.Tick(3), seed, 2);

        if (builder.TryApply(
                time.AddSeconds(-1), 98f, 1,
                out _, out _))
            throw new InvalidOperationException(
                "Strictly stale tick trade was not rejected.");

        if (!builder.TryApply(
                time.AddSeconds(1), 102f, 10,
                out MarketEventKind updateKind,
                out Candle updated) ||
            updateKind != MarketEventKind.Update ||
            updated.Sequence != 7 ||
            updated.High != 102f ||
            updated.CloseTime != time.AddSeconds(1))
            throw new InvalidOperationException("Tick update failed.");

        if (!builder.TryApply(
                time.AddSeconds(1), 103f, 11,
                out MarketEventKind appendKind,
                out Candle appended) ||
            appendKind != MarketEventKind.Append ||
            appended.Sequence != 8 ||
            appended.Open != 103f ||
            appended.OpenTime != time.AddSeconds(1))
            throw new InvalidOperationException(
                "Equal-time next tick candle append failed.");
    }

    private static void VerifyDiagnostics()
    {
        DateTime open = new(2026, 7, 31, 9, 0, 0);
        Candle seed = new(
            open,
            open.AddMinutes(5),
            100f,
            103f,
            99f,
            102f,
            1000,
            false,
            10);
        var state = new RealtimeDiagnosticsState();
        state.Reset(
            "000660",
            CandleTimeframe.Minute(5),
            seed,
            seedTickCount: 0);
        state.RecordConnectionAttempt(reconnecting: false);
        state.SetConnectionState(RealtimeConnectionState.Connected);
        state.SetConnectionState(RealtimeConnectionState.LoggedIn);
        state.RecordRegistration();
        state.RecordRejectedStale(open.AddMinutes(-1));

        RealtimeDiagnosticsSnapshot rejected = state.Snapshot();
        if (rejected.BoundaryState !=
                RealtimeBoundaryState.RejectedStaleBeforeFirstEvent ||
            rejected.RejectedStaleEvents != 1 ||
            rejected.LastRejectedStaleTime != open.AddMinutes(-1) ||
            rejected.FirstRealtimeTime.HasValue)
            throw new InvalidOperationException(
                "Realtime stale boundary diagnostics failed.");

        state.RecordAccepted(
            open.AddMinutes(3),
            MarketEventKind.Update,
            hadSeed: true);
        state.RecordAccepted(
            open.AddMinutes(5),
            MarketEventKind.Append,
            hadSeed: true);

        RealtimeDiagnosticsSnapshot snapshot = state.Snapshot();
        if (snapshot.ConnectionState != RealtimeConnectionState.Receiving ||
            snapshot.BoundaryState != RealtimeBoundaryState.SeedUpdated ||
            snapshot.FirstRealtimeTime != open.AddMinutes(3) ||
            snapshot.FirstEventKind != MarketEventKind.Update ||
            snapshot.AcceptedEvents != 2 ||
            snapshot.UpdateEvents != 1 ||
            snapshot.AppendEvents != 1 ||
            snapshot.RejectedStaleEvents != 1 ||
            snapshot.ConnectionAttempts != 1 ||
            snapshot.RegistrationCount != 1)
            throw new InvalidOperationException(
                $"Realtime continuity diagnostics mismatch: {snapshot}.");
    }
}
