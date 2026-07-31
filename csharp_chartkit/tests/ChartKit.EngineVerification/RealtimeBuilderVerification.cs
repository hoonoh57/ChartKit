using ChartKit.CSharp.Contracts;
using ChartKit.CSharp.DataSources;

namespace ChartKit.CSharp.EngineVerification;

internal static class RealtimeBuilderVerification
{
    public static void Run()
    {
        VerifyMinute();
        VerifyTick();
        Console.WriteLine("csharp_realtime_candle_builder=PASS");
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

        if (!builder.TryApply(
                time.AddSeconds(1), 102f, 10,
                out MarketEventKind updateKind,
                out Candle updated) ||
            updateKind != MarketEventKind.Update ||
            updated.Sequence != 7 ||
            updated.High != 102f)
            throw new InvalidOperationException("Tick update failed.");

        if (!builder.TryApply(
                time.AddSeconds(2), 103f, 11,
                out MarketEventKind appendKind,
                out Candle appended) ||
            appendKind != MarketEventKind.Append ||
            appended.Sequence != 8 ||
            appended.Open != 103f)
            throw new InvalidOperationException("Tick append failed.");
    }
}
