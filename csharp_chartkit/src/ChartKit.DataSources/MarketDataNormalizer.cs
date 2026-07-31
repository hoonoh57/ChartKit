using ChartKit.CSharp.Contracts;

namespace ChartKit.CSharp.DataSources;

public readonly record struct MarketDataNormalizationReport(
    int InputCount,
    int OutputCount,
    int DroppedInvalid,
    int RemovedDuplicates,
    int RepairedValues,
    bool Reordered,
    bool UsedSlowPath);

public static class MarketDataNormalizer
{
    public static List<Candle> NormalizeHistory(IReadOnlyList<Candle> source) =>
        NormalizeHistory(source, out _);

    public static List<Candle> NormalizeHistory(
        IReadOnlyList<Candle> source,
        out MarketDataNormalizationReport report)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.Count == 0)
        {
            report = new MarketDataNormalizationReport(
                0, 0, 0, 0, 0, false, false);
            return [];
        }

        if (IsFastPath(source))
        {
            report = new MarketDataNormalizationReport(
                source.Count,
                source.Count,
                0,
                0,
                0,
                false,
                false);
            return source as List<Candle> ?? source.ToList();
        }

        var candidates = new List<Candidate>(source.Count);
        int droppedInvalid = 0;
        int repairedValues = 0;
        bool reordered = false;
        DateTime previousClose = default;

        for (int index = 0; index < source.Count; index++)
        {
            Candle candle = source[index];
            if (!IsStructurallyValid(candle))
            {
                droppedInvalid++;
                continue;
            }

            if (index > 0 && candle.CloseTime < previousClose)
                reordered = true;
            previousClose = candle.CloseTime;

            float normalizedHigh = Math.Max(
                candle.High,
                Math.Max(candle.Open, candle.Close));
            float normalizedLow = Math.Min(
                candle.Low,
                Math.Min(candle.Open, candle.Close));
            long normalizedVolume = Math.Max(0L, candle.Volume);
            if (normalizedHigh != candle.High ||
                normalizedLow != candle.Low ||
                normalizedVolume != candle.Volume)
                repairedValues++;

            candidates.Add(new Candidate(
                candle with
                {
                    High = normalizedHigh,
                    Low = normalizedLow,
                    Volume = normalizedVolume
                },
                index));
        }

        candidates.Sort(static (left, right) =>
        {
            int time = left.Candle.CloseTime.CompareTo(right.Candle.CloseTime);
            return time != 0 ? time : left.SourceIndex.CompareTo(right.SourceIndex);
        });

        var output = new List<Candle>(candidates.Count);
        int removedDuplicates = 0;
        foreach (Candidate candidate in candidates)
        {
            Candle candle = candidate.Candle;
            if (output.Count > 0 && output[^1].CloseTime == candle.CloseTime)
            {
                output[^1] = candle with { Sequence = output.Count - 1L };
                removedDuplicates++;
                continue;
            }

            output.Add(candle with { Sequence = output.Count });
        }

        report = new MarketDataNormalizationReport(
            source.Count,
            output.Count,
            droppedInvalid,
            removedDuplicates,
            repairedValues,
            reordered,
            true);
        return output;
    }

    private static bool IsFastPath(IReadOnlyList<Candle> source)
    {
        DateTime previousClose = default;
        for (int index = 0; index < source.Count; index++)
        {
            Candle candle = source[index];
            if (!IsStructurallyValid(candle) ||
                candle.High < Math.Max(candle.Open, candle.Close) ||
                candle.Low > Math.Min(candle.Open, candle.Close) ||
                candle.Volume < 0 ||
                candle.Sequence != index ||
                index > 0 && candle.CloseTime <= previousClose)
                return false;
            previousClose = candle.CloseTime;
        }
        return true;
    }

    private static bool IsStructurallyValid(Candle candle) =>
        candle.OpenTime != default &&
        candle.CloseTime != default &&
        candle.CloseTime >= candle.OpenTime &&
        float.IsFinite(candle.Open) && candle.Open > 0f &&
        float.IsFinite(candle.High) && candle.High > 0f &&
        float.IsFinite(candle.Low) && candle.Low > 0f &&
        float.IsFinite(candle.Close) && candle.Close > 0f;

    private readonly record struct Candidate(Candle Candle, int SourceIndex);
}
