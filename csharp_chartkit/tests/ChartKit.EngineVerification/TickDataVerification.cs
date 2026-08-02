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

        VerifyEqualTimeArrayOrder();

        Console.WriteLine("csharp_tick_aggregation=PASS");
        Console.WriteLine("csharp_tick_aggregator_order_preserved=PASS");
    }

    private static void VerifyEqualTimeArrayOrder()
    {
        DateTime time = new(2026, 7, 30, 9, 1, 0);
        var equalTime = new List<Candle>();
        for (int index = 0; index < 6; index++)
        {
            float price = 100f + index;
            equalTime.Add(new Candle(
                time,
                time,
                price,
                price,
                price,
                price,
                1,
                true,
                index));
        }

        List<Candle> aggregated = TickCandleAggregator.Aggregate(
            equalTime,
            targetTicks: 3,
            baseTicks: 1);
        if (aggregated.Count != 2 ||
            aggregated[0].Open != 100f ||
            aggregated[0].Close != 102f ||
            aggregated[1].Open != 103f ||
            aggregated[1].Close != 105f)
            throw new InvalidOperationException(
                "Equal-HHmm tick array order was not preserved by aggregation.");
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
