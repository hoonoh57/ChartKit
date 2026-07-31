using ChartKit.CSharp.Contracts;

namespace ChartKit.CSharp.Charting;

public readonly record struct ChartLegendEntry(
    int PanelIndex,
    int ColorIndex,
    string IndicatorName,
    string ValueKey,
    float Value,
    bool HasValue);

public sealed class ChartLegendFrame
{
    public const int MaximumEntryCount = 64;

    public ChartLegendEntry[] Entries { get; } =
        new ChartLegendEntry[MaximumEntryCount];

    public int EntryCount { get; internal set; }
    public int CandleIndex { get; internal set; } = -1;
    public long SnapshotVersion { get; internal set; } = -1;
}

public sealed class ChartLegendBuilder
{
    public ChartLegendFrame Build(
        SymbolSnapshot snapshot,
        int candleIndex,
        ChartLegendFrame? target = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.Candles.Length == 0)
            throw new ArgumentOutOfRangeException(nameof(snapshot));

        int selectedIndex = Math.Clamp(candleIndex, 0, snapshot.Candles.Length - 1);
        long selectedSequence = snapshot.Candles[selectedIndex].Sequence;
        ChartLegendFrame frame = target ?? new ChartLegendFrame();
        frame.EntryCount = 0;
        frame.CandleIndex = selectedIndex;
        frame.SnapshotVersion = snapshot.Version;

        int colorIndex = 0;
        foreach (IndicatorSeriesSnapshot series in snapshot.Indicators)
        {
            IndicatorDescriptor descriptor = series.Descriptor;
            for (int valueIndex = 0;
                 valueIndex < descriptor.ValueCount;
                 valueIndex++)
            {
                if (descriptor.Kinds[valueIndex] == SeriesKind.Meta) continue;
                if (frame.EntryCount >= ChartLegendFrame.MaximumEntryCount)
                    return frame;

                float value = ResolveValue(
                    series.Points,
                    selectedIndex,
                    selectedSequence,
                    valueIndex);
                frame.Entries[frame.EntryCount++] = new ChartLegendEntry(
                    descriptor.PanelIndex,
                    colorIndex,
                    descriptor.DisplayName,
                    descriptor.Keys[valueIndex],
                    value,
                    float.IsFinite(value));
                colorIndex++;
            }
        }

        return frame;
    }

    private static float ResolveValue(
        IndicatorPoint[] points,
        int candleIndex,
        long candleSequence,
        int valueIndex)
    {
        if ((uint)candleIndex < (uint)points.Length &&
            points[candleIndex].Sequence == candleSequence)
            return points[candleIndex].GetValue(valueIndex);

        int low = 0;
        int high = points.Length - 1;
        while (low <= high)
        {
            int middle = low + ((high - low) >> 1);
            long sequence = points[middle].Sequence;
            if (sequence == candleSequence)
                return points[middle].GetValue(valueIndex);
            if (sequence < candleSequence) low = middle + 1;
            else high = middle - 1;
        }

        return float.NaN;
    }
}
