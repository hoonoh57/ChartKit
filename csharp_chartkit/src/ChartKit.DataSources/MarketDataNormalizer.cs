using ChartKit.CSharp.Contracts;

namespace ChartKit.CSharp.DataSources;

public enum MarketDataOrder
{
    Empty = 0,
    Ascending = 1,
    Descending = 2,
    EqualTimeOnly = 3
}

public readonly record struct MarketDataNormalizationReport(
    int InputCount,
    int OutputCount,
    int RemovedAdjacentDuplicates,
    int RepairedRanges,
    bool Reversed,
    bool UsedSlowPath,
    MarketDataOrder ObservedOrder);

public static class MarketDataNormalizer
{
    public static List<Candle> NormalizeHistory(
        IReadOnlyList<Candle> source,
        CandleTimeframe timeframe) =>
        NormalizeHistory(source, timeframe, out _);

    public static List<Candle> NormalizeHistory(
        IReadOnlyList<Candle> source,
        CandleTimeframe timeframe,
        out MarketDataNormalizationReport report)
    {
        ArgumentNullException.ThrowIfNull(source);
        timeframe.Validate();
        if (source.Count == 0)
        {
            report = new MarketDataNormalizationReport(
                0, 0, 0, 0, false, false, MarketDataOrder.Empty);
            return [];
        }

        ValidateRows(source);
        MarketDataOrder order = DetectOrder(source);
        bool reverse = order == MarketDataOrder.Descending;
        bool preserveEqualTimes = timeframe.Unit == CandleUnit.Tick;

        if (!reverse && IsFastPath(source, preserveEqualTimes))
        {
            report = new MarketDataNormalizationReport(
                source.Count,
                source.Count,
                0,
                0,
                false,
                false,
                order);
            return source as List<Candle> ?? source.ToList();
        }

        var output = new List<Candle>(source.Count);
        int removedAdjacentDuplicates = 0;
        int repairedRanges = 0;

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

            if (!preserveEqualTimes &&
                output.Count > 0 &&
                output[^1].CloseTime == normalized.CloseTime)
            {
                normalized = normalized with { Sequence = output.Count - 1L };
                output[^1] = normalized;
                removedAdjacentDuplicates++;
                continue;
            }

            output.Add(normalized);
        }

        report = new MarketDataNormalizationReport(
            source.Count,
            output.Count,
            removedAdjacentDuplicates,
            repairedRanges,
            reverse,
            true,
            order);
        return output;
    }

    private static MarketDataOrder DetectOrder(IReadOnlyList<Candle> source)
    {
        int direction = 0;
        for (int index = 1; index < source.Count; index++)
        {
            int comparison = source[index].CloseTime.CompareTo(
                source[index - 1].CloseTime);
            if (comparison == 0) continue;
            int currentDirection = comparison > 0 ? 1 : -1;
            if (direction == 0)
            {
                direction = currentDirection;
                continue;
            }
            if (direction != currentDirection)
                throw new InvalidDataException(
                    "Candle order is mixed. The source is preserved and rejected; " +
                    "time-based sorting is not permitted.");
        }

        return direction switch
        {
            > 0 => MarketDataOrder.Ascending,
            < 0 => MarketDataOrder.Descending,
            _ => MarketDataOrder.EqualTimeOnly
        };
    }

    private static bool IsFastPath(
        IReadOnlyList<Candle> source,
        bool preserveEqualTimes)
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
            if (index > 0)
            {
                if (candle.CloseTime < previousClose)
                    return false;
                if (!preserveEqualTimes && candle.CloseTime == previousClose)
                    return false;
            }
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
