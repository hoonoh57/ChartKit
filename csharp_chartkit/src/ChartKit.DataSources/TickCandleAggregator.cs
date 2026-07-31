using ChartKit.CSharp.Contracts;

namespace ChartKit.CSharp.DataSources;

public static class TickCandleAggregator
{
    private static readonly int[] BaseCandidates = [30, 10, 5, 1];

    public static int ChooseBase(int targetTicks)
    {
        if (targetTicks <= 0) throw new ArgumentOutOfRangeException(nameof(targetTicks));
        foreach (int candidate in BaseCandidates)
            if (targetTicks % candidate == 0) return candidate;
        return 1;
    }

    public static List<Candle> Aggregate(
        IReadOnlyList<Candle> source,
        int targetTicks,
        int baseTicks)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (targetTicks <= 0) throw new ArgumentOutOfRangeException(nameof(targetTicks));
        if (baseTicks <= 0) throw new ArgumentOutOfRangeException(nameof(baseTicks));
        if (targetTicks % baseTicks != 0)
            throw new ArgumentException("Target ticks must be divisible by base ticks.");

        var output = new List<Candle>();
        if (source.Count == 0) return output;
        int groupSize = targetTicks / baseTicks;
        if (groupSize == 1)
        {
            output.AddRange(source);
            return output;
        }

        int dayStart = 0;
        for (int index = 1; index <= source.Count; index++)
        {
            bool end = index == source.Count;
            bool dateChanged = !end &&
                source[index].TradingDate != source[dayStart].TradingDate;
            if (!end && !dateChanged) continue;
            AggregateDay(source, dayStart, index - 1, groupSize, output);
            dayStart = index;
        }
        return output;
    }

    private static void AggregateDay(
        IReadOnlyList<Candle> source,
        int dayStart,
        int dayEnd,
        int groupSize,
        List<Candle> output)
    {
        var newestFirst = new List<Candle>();
        for (int groupEnd = dayEnd;
             groupEnd - groupSize + 1 >= dayStart;
             groupEnd -= groupSize)
        {
            int groupStart = groupEnd - groupSize + 1;
            newestFirst.Add(Build(source, groupStart, groupEnd));
        }
        newestFirst.Reverse();
        output.AddRange(newestFirst);
    }

    private static Candle Build(
        IReadOnlyList<Candle> source,
        int start,
        int end)
    {
        Candle first = source[start];
        Candle last = source[end];
        float high = first.High;
        float low = first.Low;
        long volume = 0;
        bool allFinal = true;
        for (int index = start; index <= end; index++)
        {
            Candle value = source[index];
            high = Math.Max(high, value.High);
            low = Math.Min(low, value.Low);
            volume += value.Volume;
            allFinal &= value.IsFinal;
        }
        return new Candle(
            first.OpenTime,
            last.CloseTime,
            first.Open,
            high,
            low,
            last.Close,
            volume,
            allFinal,
            last.Sequence);
    }
}
