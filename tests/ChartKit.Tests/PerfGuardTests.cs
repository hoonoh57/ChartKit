using System.Diagnostics;
using ChartKit.Abstractions;
using ChartKit.Indicators;
using ChartKit.Models;
using Xunit;

namespace ChartKit.Tests;

/// <summary>
/// P3 완료 전까지 RED 인 것이 정상. 게이트에서는 Category!=Perf 로 제외한다.
/// UpdateLast 1회 비용이 봉 개수에 비례하면(=전체 재계산) 실패한다.
/// </summary>
[Trait("Category", "Perf")]
public class PerfGuardTests
{
    [Theory]
    [InlineData("RSI14")]
    [InlineData("MACD")]
    public void UpdateLast_DoesNotScaleWithHistory(string key)
    {
        double small = MeasureNs(key, 1_000);
        double large = MeasureNs(key, 8_000);
        Assert.True(large / small < 3.0,
            $"{key}: 이력 8배 증가에 갱신 비용 {large / small:F1}배 → O(N) 재계산 의심");
    }

    private static double MeasureNs(string key, int n)
    {
        IIndicator ind = key switch
        {
            "RSI14" => new RSI_Indicator(14, 9),
            "MACD"  => new MACD_Indicator(12, 26, 9),
            _ => throw new ArgumentOutOfRangeException(nameof(key))
        };
        var candles = CandleFactory.Deterministic(n);
        var prev = ind.Calculate(candles);
        for (int i = 0; i < 20; i++) ind.UpdateLast(candles, prev);   // 워밍업

        var sw = Stopwatch.StartNew();
        const int iter = 200;
        for (int i = 0; i < iter; i++) ind.UpdateLast(candles, prev);
        sw.Stop();
        return sw.Elapsed.TotalMilliseconds * 1_000_000 / iter;
    }
}