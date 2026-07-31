using ChartKit.CSharp.Contracts;
using ChartKit.CSharp.DataSources;

namespace ChartKit.CSharp.EngineVerification;

internal static class TickDataVerification
{
    public static void Run()
    {
        if (TickCandleAggregator.ChooseBase(720) != 30 ||
            TickCandleAggregator.ChooseBase(120) != 30 ||
            TickCandleAggregator.ChooseBase(7) != 1)
            throw new InvalidOperationException("Tick base selection failed.");

        var source = new List<Candle>();
        AddDay(source, new DateTime(2026, 7, 29), 7, 0);
        AddDay(source, new DateTime(2026, 7, 30), 7, 7);
        List<Candle> aggregated = TickCandleAggregator.Aggregate(source, 30, 10);

        if (aggregated.Count != 4)
            throw new InvalidOperationException(
                $"Expected four aggregate candles, got {aggregated.Count}.");
        if (aggregated[0].TradingDate != new DateTime(2026, 7, 29) ||
            aggregated[2].TradingDate != new DateTime(2026, 7, 30))
            throw new InvalidOperationException("Tick aggregation crossed a trading date.");
        if (aggregated[0].Open != source[1].Open ||
            aggregated[0].Close != source[3].Close ||
            aggregated[1].Open != source[4].Open ||
            aggregated[1].Close != source[6].Close)
            throw new InvalidOperationException("Newest-backward grouping phase failed.");
        if (aggregated.Any(candle => candle.OpenTime.Date != candle.CloseTime.Date))
            throw new InvalidOperationException("Tick candle crossed midnight.");

        var pagedOutOfOrder = new List<Candle>(source.Count);
        pagedOutOfOrder.AddRange(source.GetRange(7, 7));
        pagedOutOfOrder.AddRange(source.GetRange(0, 7));
        List<Candle> normalized = TickCandleAggregator.Aggregate(
            pagedOutOfOrder, 30, 10);
        AssertSame(aggregated, normalized);

        Console.WriteLine("csharp_tick_aggregation=PASS");
        Console.WriteLine("csharp_tick_page_order_normalization=PASS");
    }

    private static void AssertSame(
        IReadOnlyList<Candle> expected,
        IReadOnlyList<Candle> actual)
    {
        if (expected.Count != actual.Count)
            throw new InvalidOperationException(
                $"Normalized tick count mismatch: {actual.Count} != {expected.Count}.");
        for (int index = 0; index < expected.Count; index++)
        {
            Candle left = expected[index];
            Candle right = actual[index];
            if (left.OpenTime != right.OpenTime ||
                left.CloseTime != right.CloseTime ||
                left.Open != right.Open ||
                left.High != right.High ||
                left.Low != right.Low ||
                left.Close != right.Close ||
                left.Volume != right.Volume)
                throw new InvalidOperationException(
                    $"Normalized tick candle mismatch at index {index}.");
        }
    }

    private static void AddDay(
        List<Candle> destination,
        DateTime date,
        int count,
        int sequenceOffset)
    {
        for (int index = 0; index < count; index++)
        {
            float price = 100f + sequenceOffset + index;
            DateTime time = date.AddHours(9).AddSeconds(index);
            destination.Add(new Candle(
                time,
                time,
                price,
                price + 1f,
                price - 1f,
                price + 0.5f,
                10 + index,
                true,
                sequenceOffset + index));
        }
    }
}
