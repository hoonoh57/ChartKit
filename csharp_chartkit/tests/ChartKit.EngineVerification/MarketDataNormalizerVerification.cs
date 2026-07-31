using ChartKit.CSharp.Contracts;
using ChartKit.CSharp.DataSources;

namespace ChartKit.CSharp.EngineVerification;

internal static class MarketDataNormalizerVerification
{
    public static void Run()
    {
        VerifyFastPath();
        VerifyRepairSortAndDeduplicate();
        Console.WriteLine("csharp_market_data_normalizer=PASS");
    }

    private static void VerifyFastPath()
    {
        var source = new List<Candle>
        {
            Make(new DateTime(2026, 7, 30, 9, 1, 0), 100f, 0),
            Make(new DateTime(2026, 7, 30, 9, 2, 0), 101f, 1)
        };

        List<Candle> result = MarketDataNormalizer.NormalizeHistory(
            source,
            out MarketDataNormalizationReport report);

        if (!ReferenceEquals(source, result) ||
            report.UsedSlowPath ||
            report.InputCount != 2 ||
            report.OutputCount != 2)
            throw new InvalidOperationException(
                "Normalizer fast path changed valid input.");
    }

    private static void VerifyRepairSortAndDeduplicate()
    {
        DateTime t1 = new(2026, 7, 30, 9, 1, 0);
        DateTime t2 = new(2026, 7, 30, 9, 2, 0);
        DateTime t3 = new(2026, 7, 30, 9, 3, 0);
        DateTime t4 = new(2026, 7, 30, 9, 4, 0);
        var source = new List<Candle>
        {
            Make(t3, 103f, 30),
            new(t4.AddMinutes(-1), t4, float.NaN, 1f, 1f, 1f, 1, true, 40),
            Make(t1, 101f, 10),
            Make(t2, 102f, 20),
            new(
                t2.AddMinutes(-1),
                t2,
                205f,
                200f,
                210f,
                207f,
                -5,
                true,
                21)
        };

        List<Candle> result = MarketDataNormalizer.NormalizeHistory(
            source,
            out MarketDataNormalizationReport report);

        if (result.Count != 3 ||
            result[0].CloseTime != t1 ||
            result[1].CloseTime != t2 ||
            result[2].CloseTime != t3)
            throw new InvalidOperationException(
                "Normalizer chronological ordering failed.");
        if (result[1].Open != 205f || result[1].Close != 207f)
            throw new InvalidOperationException(
                "Normalizer did not retain the latest duplicate row.");
        if (result[1].High != 207f ||
            result[1].Low != 205f ||
            result[1].Volume != 0)
            throw new InvalidOperationException(
                "Normalizer OHLCV repair failed.");
        for (int index = 0; index < result.Count; index++)
        {
            Candle candle = result[index];
            if (candle.Sequence != index ||
                candle.High < Math.Max(candle.Open, candle.Close) ||
                candle.Low > Math.Min(candle.Open, candle.Close) ||
                candle.Volume < 0)
                throw new InvalidOperationException(
                    "Normalizer output contract failed.");
        }
        if (!report.UsedSlowPath ||
            !report.Reordered ||
            report.DroppedInvalid != 1 ||
            report.RemovedDuplicates != 1 ||
            report.RepairedValues != 1 ||
            report.InputCount != 5 ||
            report.OutputCount != 3)
            throw new InvalidOperationException(
                $"Normalizer report mismatch: {report}.");
    }

    private static Candle Make(DateTime closeTime, float price, long sequence) =>
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
}
