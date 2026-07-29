using ChartKit.Models;

namespace ChartKit.Tests;

/// <summary>결정적(고정 시드) 캔들 생성기. 골든 테스트의 입력 고정용.</summary>
public static class CandleFactory
{
    public static List<CandleItem> Deterministic(int count, int seed = 20260729)
    {
        var rnd = new Random(seed);
        var list = new List<CandleItem>(count);
        float price = 10000f;
        var t = new DateTime(2026, 7, 28, 9, 0, 0);
        for (int i = 0; i < count; i++)
        {
            float o = price;
            float c = MathF.Max(1000f, o + (float)((rnd.NextDouble() - 0.5) * 400));
            float h = MathF.Max(o, c) + (float)(rnd.NextDouble() * 150);
            float l = MathF.Min(o, c) - (float)(rnd.NextDouble() * 150);
            list.Add(new CandleItem {
                Dt = t.AddMinutes(i), Open = o, High = h, Low = l, Close = c,
                Volume = rnd.Next(1000, 9000)
            });
            price = c;
        }
        return list;
    }
}