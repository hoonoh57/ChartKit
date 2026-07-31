using ChartKit.CSharp.Contracts;
using ChartKit.CSharp.Engine;

namespace ChartKit.CSharp.EngineVerification;

internal static class RingBufferVerification
{
    public static void Run()
    {
        var candles = new CandleRingBuffer(3);
        List<Candle> fixture = Fixture.CreateCandles(5);
        foreach (Candle candle in fixture) candles.Add(candle);

        if (candles.Count != 3 || candles.FirstSequence != 2 || candles.LastSequence != 4)
            throw new InvalidOperationException("Candle ring eviction failed.");

        Candle replacement = candles[^1] with { Close = candles[^1].Close + 9f };
        candles.ReplaceLast(replacement);
        if (candles[^1].Close != replacement.Close)
            throw new InvalidOperationException("Candle last replacement failed.");

        var points = new IndicatorPointRingBuffer(2);
        points.AddOrReplace(new IndicatorPoint(1, 10f));
        points.AddOrReplace(new IndicatorPoint(1, 11f));
        points.AddOrReplace(new IndicatorPoint(2, 12f));
        points.AddOrReplace(new IndicatorPoint(3, 13f));
        if (points.Count != 2 || points[0].Sequence != 2 || points[1].Sequence != 3)
            throw new InvalidOperationException("Indicator ring behavior failed.");

        points.Clear();
        if (points.Count != 0)
            throw new InvalidOperationException("Indicator ring clear failed.");

        Console.WriteLine("ring_buffer_verification=PASS");
    }
}
