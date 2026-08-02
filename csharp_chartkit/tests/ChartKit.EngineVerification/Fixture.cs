using ChartKit.CSharp.Contracts;

namespace ChartKit.CSharp.EngineVerification;

internal static class Fixture
{
    public static List<Candle> CreateCandles(int count, int symbolOffset = 0)
    {
        var candles = new List<Candle>(count);
        DateTime start = new(2026, 7, 30, 9, 0, 0, DateTimeKind.Unspecified);
        float previous = 1000f + symbolOffset * 7f;
        for (int index = 0; index < count; index++)
        {
            float close = 1000f + symbolOffset * 7f + index * 0.18f +
                          (float)Math.Sin((index + symbolOffset) / 4d) * 4.5f;
            candles.Add(new Candle(
                start.AddMinutes(index),
                start.AddMinutes(index + 1),
                previous,
                Math.Max(previous, close) + 1f,
                Math.Min(previous, close) - 1f,
                close,
                1000L + symbolOffset * 11L + index * 17L,
                true,
                index));
            previous = close;
        }
        return candles;
    }

    public static void Equal(
        float expected,
        float actual,
        string context,
        float tolerance = 0.0001f)
    {
        if (float.IsNaN(expected) && float.IsNaN(actual)) return;
        float scale = Math.Max(1f, Math.Max(Math.Abs(expected), Math.Abs(actual)));
        if (Math.Abs(expected - actual) > tolerance * scale)
            throw new InvalidOperationException(
                $"{context}: expected={expected}, actual={actual}");
    }
}
