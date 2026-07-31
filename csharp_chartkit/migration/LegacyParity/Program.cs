using System.Reflection;
using ChartKit.CSharp.Contracts;
using ChartKit.CSharp.Engine;
using LegacyCandle = ChartKit.Models.CandleItem;
using LegacyIndicator = ChartKit.Abstractions.IIndicator;
using LegacyResult = ChartKit.Abstractions.IndicatorResult;

namespace ChartKit.CSharp.LegacyParity;

internal static class Program
{
    private sealed record ParityCase(
        string Name,
        string LegacyTypeName,
        object[] LegacyArguments,
        Func<IIncrementalIndicator> ModernFactory);

    private static int Main()
    {
        try
        {
            ParityCase[] cases =
            [
                new("MA", "MA_Indicator", [20, "SMA"], () => new MaIndicator()),
                new("JMA", "JMA_Indicator", [14, 50, 2], () => new JmaIndicator()),
                new("RSI", "RSI_Indicator", [14, 9], () => new RsiIndicator()),
                new("MACD", "MACD_Indicator", [12, 26, 9], () => new MacdIndicator()),
                new("OBV", "OBV_Indicator", [20], () => new ObvIndicator()),
                new("SuperTrend", "SuperTrend_Indicator", [10, 3f], () => new SuperTrendIndicator()),
                new("VWAP", "VWAP_Indicator", [1f, 2f], () => new VwapIndicator()),
                new("Disparity", "Disparity_Indicator", [20], () => new DisparityIndicator())
            ];

            foreach (ParityCase parityCase in cases)
                VerifyCase(parityCase);

            Console.WriteLine($"legacy_parity_indicator_count={cases.Length}");
            Console.WriteLine("legacy_csharp_indicator_parity=PASS");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            Console.WriteLine("legacy_csharp_indicator_parity=FAIL");
            return 1;
        }
    }

    private static void VerifyCase(ParityCase parityCase)
    {
        List<Candle> modernCandles = CreateCandles(96);
        List<LegacyCandle> legacyCandles = modernCandles.Select(ToLegacy).ToList();
        LegacyIndicator legacy = CreateLegacy(parityCase);
        IIncrementalIndicator modern = parityCase.ModernFactory();

        List<LegacyResult> legacyResults = legacy.Calculate(legacyCandles);
        IReadOnlyList<IndicatorPoint> modernResults = modern.Calculate(modernCandles);
        CompareAll(parityCase.Name, legacyResults, modernResults, modern.Descriptor);

        Candle appended = CreateCandles(97)[^1];
        modernCandles.Add(appended);
        legacyCandles.Add(ToLegacy(appended));
        LegacyResult legacyLast = legacy.UpdateLast(legacyCandles, legacyResults);
        IndicatorPoint modernLast = modern.UpdateLast(modernCandles);
        legacyResults.Add(legacyLast);
        ComparePoint(parityCase.Name + "/append", legacyLast, modernLast, modern.Descriptor);

        Candle updated = modernCandles[^1] with
        {
            High = modernCandles[^1].High + 2.25f,
            Low = modernCandles[^1].Low - 1.75f,
            Close = modernCandles[^1].Close + 1.5f,
            Volume = modernCandles[^1].Volume + 777,
            IsFinal = false
        };
        modernCandles[^1] = updated;
        CopyToLegacy(updated, legacyCandles[^1]);
        legacyLast = legacy.UpdateLast(legacyCandles, legacyResults);
        modernLast = modern.UpdateLast(modernCandles);
        legacyResults[^1] = legacyLast;
        ComparePoint(parityCase.Name + "/update", legacyLast, modernLast, modern.Descriptor);

        Candle repeated = updated with
        {
            Close = updated.Close - 0.75f,
            Volume = updated.Volume + 91,
            IsFinal = true
        };
        modernCandles[^1] = repeated;
        CopyToLegacy(repeated, legacyCandles[^1]);
        legacyLast = legacy.UpdateLast(legacyCandles, legacyResults);
        modernLast = modern.UpdateLast(modernCandles);
        legacyResults[^1] = legacyLast;
        ComparePoint(parityCase.Name + "/repeat-update", legacyLast, modernLast, modern.Descriptor);

        LegacyIndicator freshLegacy = CreateLegacy(parityCase);
        IIncrementalIndicator freshModern = parityCase.ModernFactory();
        LegacyResult rebuiltLegacy = freshLegacy.Calculate(legacyCandles)[^1];
        IndicatorPoint rebuiltModern = freshModern.Calculate(modernCandles)[^1];
        ComparePoint(parityCase.Name + "/fresh-full", rebuiltLegacy, rebuiltModern, freshModern.Descriptor);
        ComparePoint(parityCase.Name + "/incremental-vs-full", rebuiltLegacy, modernLast, modern.Descriptor);

        Console.WriteLine($"legacy_parity_{parityCase.Name}=PASS");
    }

    private static LegacyIndicator CreateLegacy(ParityCase parityCase)
    {
        Assembly assembly = typeof(LegacyCandle).Assembly;
        Type type = assembly.GetTypes().Single(candidate =>
            candidate.Namespace == "ChartKit.Indicators" &&
            candidate.Name == parityCase.LegacyTypeName);
        return (LegacyIndicator)(Activator.CreateInstance(
            type,
            BindingFlags.Public | BindingFlags.Instance,
            binder: null,
            args: parityCase.LegacyArguments,
            culture: null) ?? throw new InvalidOperationException(
                $"Could not construct {type.FullName}."));
    }

    private static void CompareAll(
        string context,
        IReadOnlyList<LegacyResult> legacy,
        IReadOnlyList<IndicatorPoint> modern,
        IndicatorDescriptor descriptor)
    {
        if (legacy.Count != modern.Count)
            throw new InvalidOperationException(
                $"{context}: count {legacy.Count} != {modern.Count}.");
        for (int index = 0; index < legacy.Count; index++)
            ComparePoint($"{context}/full/{index}", legacy[index], modern[index], descriptor);
    }

    private static void ComparePoint(
        string context,
        LegacyResult legacy,
        IndicatorPoint modern,
        IndicatorDescriptor descriptor)
    {
        for (int keyIndex = 0; keyIndex < descriptor.ValueCount; keyIndex++)
        {
            string key = descriptor.Keys[keyIndex];
            float expected = legacy.Values.TryGetValue(key, out float value)
                ? value
                : float.NaN;
            float actual = modern.GetValue(keyIndex);
            Equal(expected, actual, $"{context}/{key}");
        }
    }

    private static void Equal(
        float expected,
        float actual,
        string context,
        float tolerance = 0.0001f)
    {
        if (float.IsNaN(expected) && float.IsNaN(actual)) return;
        if (float.IsInfinity(expected) || float.IsInfinity(actual))
        {
            if (expected.Equals(actual)) return;
            throw new InvalidOperationException(
                $"{context}: expected={expected}, actual={actual}.");
        }
        float scale = Math.Max(1f, Math.Max(Math.Abs(expected), Math.Abs(actual)));
        if (Math.Abs(expected - actual) > tolerance * scale)
            throw new InvalidOperationException(
                $"{context}: expected={expected}, actual={actual}.");
    }

    private static List<Candle> CreateCandles(int count)
    {
        var candles = new List<Candle>(count);
        DateTime start = new(2026, 7, 30, 9, 0, 0);
        float previous = 1000f;
        for (int index = 0; index < count; index++)
        {
            float close = 1000f + index * 0.18f +
                          (float)Math.Sin(index / 4d) * 4.5f;
            candles.Add(new Candle(
                start.AddMinutes(index),
                start.AddMinutes(index + 1),
                previous,
                Math.Max(previous, close) + 1f,
                Math.Min(previous, close) - 1f,
                close,
                1000L + index * 17L,
                true,
                index));
            previous = close;
        }
        return candles;
    }

    private static LegacyCandle ToLegacy(Candle source)
    {
        var destination = new LegacyCandle();
        CopyToLegacy(source, destination);
        return destination;
    }

    private static void CopyToLegacy(Candle source, LegacyCandle destination)
    {
        destination.Dt = source.CloseTime;
        destination.Sequence = source.Sequence;
        destination.OpenTime = source.OpenTime;
        destination.CloseTime = source.CloseTime;
        destination.IsFinal = source.IsFinal;
        destination.Open = source.Open;
        destination.High = source.High;
        destination.Low = source.Low;
        destination.Close = source.Close;
        destination.Volume = source.Volume;
    }
}
