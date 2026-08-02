using ChartKit.CSharp.Contracts;
using ChartKit.CSharp.DataSources;

namespace ChartKit.CSharp.EngineVerification;

internal static class MarketDataNormalizerVerification
{
    public static void Run()
    {
        VerifyMinuteFastPath();
        VerifyWholeReverseAndMinuteBoundaryDeduplication();
        VerifyEqualTimeTicksPreserveArrayOrder();
        VerifyMixedMinuteOrderIsRejected();
        VerifyInvalidRowsAreRejected();
        Console.WriteLine("csharp_market_data_normalizer=PASS");
        Console.WriteLine("csharp_tick_equal_time_order=PASS");
        Console.WriteLine("csharp_whole_array_reverse_only=PASS");
    }

    private static void VerifyMinuteFastPath()
    {
        var source = new List<Candle>
        {
            MakeMinute(new DateTime(2026, 7, 30, 9, 1, 0), 100f, 0),
            MakeMinute(new DateTime(2026, 7, 30, 9, 2, 0), 101f, 1)
        };

        List<Candle> result = MarketDataNormalizer.NormalizeHistory(
            source,
            CandleTimeframe.Minute(1),
            SourceArrayDirection.Forward,
            out MarketDataNormalizationReport report);

        if (!ReferenceEquals(source, result) ||
            report.UsedSlowPath ||
            report.Reversed ||
            report.InputCount != 2 ||
            report.OutputCount != 2)
            throw new InvalidOperationException(
                "Normalizer fast path changed valid minute input.");
    }

    private static void VerifyWholeReverseAndMinuteBoundaryDeduplication()
    {
        DateTime t1 = new(2026, 7, 30, 9, 1, 0);
        DateTime t2 = new(2026, 7, 30, 9, 2, 0);
        DateTime t3 = new(2026, 7, 30, 9, 3, 0);
        var newestFirst = new List<Candle>
        {
            MakeMinute(t3, 103f, 30),
            new(
                t2.AddMinutes(-1),
                t2,
                205f,
                200f,
                210f,
                207f,
                5,
                true,
                21),
            MakeMinute(t2, 102f, 20),
            MakeMinute(t1, 101f, 10)
        };

        List<Candle> result = MarketDataNormalizer.NormalizeHistory(
            newestFirst,
            CandleTimeframe.Minute(1),
            SourceArrayDirection.ReverseWhole,
            out MarketDataNormalizationReport report);

        if (result.Count != 3 ||
            result[0].CloseTime != t1 ||
            result[1].CloseTime != t2 ||
            result[2].CloseTime != t3)
            throw new InvalidOperationException(
                "Whole-array minute reversal failed.");
        if (result[1].Open != 205f ||
            result[1].Close != 207f ||
            result[1].High != 210f ||
            result[1].Low != 200f)
            throw new InvalidOperationException(
                "Minute boundary duplicate/range repair failed.");
        if (!report.Reversed ||
            !report.UsedSlowPath ||
            report.RemovedAdjacentDuplicates != 1 ||
            report.RepairedRanges != 1 ||
            report.OutputCount != 3)
            throw new InvalidOperationException(
                $"Minute normalizer report mismatch: {report}.");
    }

    private static void VerifyEqualTimeTicksPreserveArrayOrder()
    {
        DateTime sameMinute = new(2026, 7, 30, 9, 1, 0);
        var forward = new List<Candle>
        {
            MakeTick(sameMinute, 100f, 0),
            MakeTick(sameMinute, 101f, 1),
            MakeTick(sameMinute, 102f, 2)
        };

        List<Candle> preserved = MarketDataNormalizer.NormalizeHistory(
            forward,
            CandleTimeframe.Tick(1),
            SourceArrayDirection.Forward,
            out MarketDataNormalizationReport forwardReport);
        if (!ReferenceEquals(forward, preserved) ||
            preserved.Select(value => value.Open).SequenceEqual([100f, 101f, 102f]) is false ||
            forwardReport.RemovedAdjacentDuplicates != 0)
            throw new InvalidOperationException(
                "Equal-time tick order was changed or deduplicated.");

        List<Candle> reversed = MarketDataNormalizer.NormalizeHistory(
            forward,
            CandleTimeframe.Tick(1),
            SourceArrayDirection.ReverseWhole,
            out MarketDataNormalizationReport reverseReport);
        if (!reversed.Select(value => value.Open).SequenceEqual([102f, 101f, 100f]) ||
            reversed.Count != 3 ||
            reverseReport.RemovedAdjacentDuplicates != 0 ||
            !reverseReport.Reversed)
            throw new InvalidOperationException(
                "Tick data was not reversed strictly as one whole array.");
        for (int index = 0; index < reversed.Count; index++)
            if (reversed[index].Sequence != index)
                throw new InvalidOperationException(
                    "Reversed tick sequence was not reassigned continuously.");
    }

    private static void VerifyMixedMinuteOrderIsRejected()
    {
        var mixed = new List<Candle>
        {
            MakeMinute(new DateTime(2026, 7, 30, 9, 1, 0), 100f, 0),
            MakeMinute(new DateTime(2026, 7, 30, 9, 3, 0), 103f, 1),
            MakeMinute(new DateTime(2026, 7, 30, 9, 2, 0), 102f, 2)
        };
        try
        {
            _ = MarketDataNormalizer.NormalizeHistory(
                mixed,
                CandleTimeframe.Minute(1),
                SourceArrayDirection.Forward);
            throw new InvalidOperationException(
                "Mixed minute order should have been rejected.");
        }
        catch (InvalidDataException)
        {
        }
    }

    private static void VerifyInvalidRowsAreRejected()
    {
        DateTime time = new(2026, 7, 30, 9, 1, 0);
        var invalid = new List<Candle>
        {
            new(time, time, 100f, 101f, 99f, 100f, -1, true, 0)
        };
        try
        {
            _ = MarketDataNormalizer.NormalizeHistory(
                invalid,
                CandleTimeframe.Tick(1),
                SourceArrayDirection.Forward);
            throw new InvalidOperationException(
                "Invalid tick row should have been rejected, not removed.");
        }
        catch (InvalidDataException)
        {
        }
    }

    private static Candle MakeMinute(
        DateTime closeTime,
        float price,
        long sequence) =>
        new(
            closeTime.AddMinutes(-1),
            closeTime,
            price,
            price + 1f,
            price - 1f,
            price + 0.5f,
            100,
            true,
            sequence);

    private static Candle MakeTick(
        DateTime time,
        float price,
        long sequence) =>
        new(
            time,
            time,
            price,
            price + 0.5f,
            price - 0.5f,
            price + 0.25f,
            1,
            true,
            sequence);
}
