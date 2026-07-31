using ChartKit.CSharp.Contracts;

namespace ChartKit.CSharp.DataSources;

public enum SourceArrayDirection
{
    Forward = 0,
    ReverseWhole = 1
}

public readonly record struct MarketDataNormalizationReport(
    int InputCount,
    int OutputCount,
    int RemovedAdjacentDuplicates,
    int RepairedRanges,
    bool Reversed,
    bool UsedSlowPath);

public static class MarketDataNormalizer
{
    public static List<Candle> NormalizeHistory(
        IReadOnlyList<Candle> source,
        CandleTimeframe timeframe,
        SourceArrayDirection direction) =>
        NormalizeHistory(source, timeframe, direction, out _);

    public static List<Candle> NormalizeHistory(
        IReadOnlyList<Candle> source,
        CandleTimeframe timeframe,
        SourceArrayDirection direction,
        out MarketDataNormalizationReport report)
    {
        ArgumentNullException.ThrowIfNull(source);
        timeframe.Validate();
        if (source.Count == 0)
        {
            report = new MarketDataNormalizationReport(
                0, 0, 0, 0, false, false);
            return [];
        }

        bool reverse = direction == SourceArrayDirection.ReverseWhole;
        bool tick = timeframe.Unit == CandleUnit.Tick;
        ValidateRows(source);

        if (!reverse && IsFastPath(source, tick))
        {
            report = new MarketDataNormalizationReport(
                source.Count,
                source.Count,
                0,
                0,
                false,
                false);
            return source as List<Candle> ?? source.ToList();
        }

        var output = new List<Candle>(source.Count);
        int removedAdjacentDuplicates = 0;
        int repairedRanges = 0;
        DateTime previousClose = default;

        for (int logicalIndex = 0; logicalIndex < source.Count; logicalIndex++)
        {
            int sourceIndex = reverse
                ? source.Count - 1 - logicalIndex
                : logicalIndex;
            Candle sourceValue = source[sourceIndex];
            float high = Math.Max(
                Math.Max(sourceValue.Open, sourceValue.Close),
                Math.Max(sourceValue.High, sourceValue.Low));
            float low = Math.Min(
                Math.Min(sourceValue.Open, sourceValue.Close),
                Math.Min(sourceValue.High, sourceValue.Low));
            if (high != sourceValue.High || low != sourceValue.Low)
                repairedRanges++;

            Candle normalized = sourceValue with
            {
                High = high,
                Low = low,
                Sequence = output.Count
            };

            if (!tick && output.Count > 0)
            {
                if (normalized.CloseTime < previousClose)
                    throw new InvalidDataException(
                        "Non-tick candle order remains mixed after applying the " +
                        "source-declared whole-array direction. Sorting is forbidden.");
                if (normalized.CloseTime == previousClose)
                {
                    normalized = normalized with { Sequence = output.Count - 1L };
                    output[^1] = normalized;
                    removedAdjacentDuplicates++;
                    continue;
                }
            }

            output.Add(normalized);
            previousClose = normalized.CloseTime;
        }

        report = new MarketDataNormalizationReport(
            source.Count,
            output.Count,
            removedAdjacentDuplicates,
            repairedRanges,
            reverse,
            true);
        return output;
    }

    private static bool IsFastPath(
        IReadOnlyList<Candle> source,
        bool tick)
    {
        DateTime previousClose = default;
        for (int index = 0; index < source.Count; index++)
        {
            Candle candle = source[index];
            if (candle.High < Math.Max(candle.Open, candle.Close) ||
                candle.Low > Math.Min(candle.Open, candle.Close) ||
                candle.High < candle.Low ||
                candle.Sequence != index)
                return false;
            if (!tick && index > 0 && candle.CloseTime <= previousClose)
                return false;
            previousClose = candle.CloseTime;
        }
        return true;
    }

    private static void ValidateRows(IReadOnlyList<Candle> source)
    {
        for (int index = 0; index < source.Count; index++)
        {
            Candle candle = source[index];
            if (candle.OpenTime == default ||
                candle.CloseTime == default ||
                candle.CloseTime < candle.OpenTime ||
                !float.IsFinite(candle.Open) || candle.Open <= 0f ||
                !float.IsFinite(candle.High) || candle.High <= 0f ||
                !float.IsFinite(candle.Low) || candle.Low <= 0f ||
                !float.IsFinite(candle.Close) || candle.Close <= 0f ||
                candle.Volume < 0)
                throw new InvalidDataException(
                    $"Invalid candle at source index {index}. " +
                    "Rows are never silently removed because that would corrupt " +
                    "tick counts and history continuity.");
        }
    }
}
