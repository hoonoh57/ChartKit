using ChartKit.CSharp.Contracts;
using ChartKit.CSharp.Engine;

namespace ChartKit.CSharp.EngineVerification;

internal static class IndicatorVerification
{
    public static void Run()
    {
        IIncrementalIndicator[] indicators = DefaultIndicatorFactory.Create();
        if (indicators.Length != 8)
            throw new InvalidOperationException($"Expected 8 indicators, got {indicators.Length}.");

        foreach (IIncrementalIndicator indicator in indicators)
            VerifyIndicator(indicator);

        Console.WriteLine($"indicator_count={indicators.Length}");
        Console.WriteLine("incremental_equivalence=PASS");
    }

    private static void VerifyIndicator(IIncrementalIndicator indicator)
    {
        List<Candle> candles = Fixture.CreateCandles(96);
        IReadOnlyList<IndicatorPoint> initial = indicator.Calculate(candles);
        if (initial.Count != candles.Count)
            throw new InvalidOperationException(
                $"{indicator.Descriptor.Id}: initial count mismatch.");

        Candle appended = Fixture.CreateCandles(97)[^1];
        candles.Add(appended);
        CompareLast(indicator, candles, "append");

        Candle updated = candles[^1] with
        {
            High = candles[^1].High + 2.25f,
            Low = candles[^1].Low - 1.75f,
            Close = candles[^1].Close + 1.5f,
            Volume = candles[^1].Volume + 777,
            IsFinal = false
        };
        candles[^1] = updated;
        CompareLast(indicator, candles, "update");

        candles[^1] = updated with
        {
            Close = updated.Close - 0.75f,
            Volume = updated.Volume + 91,
            IsFinal = true
        };
        CompareLast(indicator, candles, "repeat-update");

        Candle discontinuity = Fixture.CreateCandles(98)[^1] with { Sequence = 105 };
        candles.Add(discontinuity);
        CompareLast(indicator, candles, "discontinuity-rebuild");
    }

    private static void CompareLast(
        IIncrementalIndicator incremental,
        List<Candle> candles,
        string scenario)
    {
        IndicatorPoint actual = incremental.UpdateLast(candles);
        IIncrementalIndicator fresh = CreateFresh(incremental);
        IndicatorPoint expected = fresh.Calculate(candles)[^1];

        if (actual.Sequence != expected.Sequence)
            throw new InvalidOperationException(
                $"{incremental.Descriptor.Id}/{scenario}: sequence mismatch.");

        for (int keyIndex = 0;
             keyIndex < incremental.Descriptor.ValueCount;
             keyIndex++)
        {
            Fixture.Equal(
                expected.GetValue(keyIndex),
                actual.GetValue(keyIndex),
                $"{incremental.Descriptor.Id}/{scenario}/" +
                incremental.Descriptor.Keys[keyIndex]);
        }
    }

    private static IIncrementalIndicator CreateFresh(
        IIncrementalIndicator indicator) => indicator switch
    {
        MaIndicator => new MaIndicator(),
        JmaIndicator => new JmaIndicator(),
        RsiIndicator => new RsiIndicator(),
        MacdIndicator => new MacdIndicator(),
        ObvIndicator => new ObvIndicator(),
        SuperTrendIndicator => new SuperTrendIndicator(),
        VwapIndicator => new VwapIndicator(),
        DisparityIndicator => new DisparityIndicator(),
        _ => throw new NotSupportedException(indicator.GetType().FullName)
    };
}
