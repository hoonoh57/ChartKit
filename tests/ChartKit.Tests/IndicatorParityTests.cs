using ChartKit.Abstractions;
using ChartKit.Indicators;
using ChartKit.Models;
using Xunit;

namespace ChartKit.Tests;

/// <summary>
/// P3(증분 계산 실구현)의 안전망.
/// Calculate 전체 결과 == UpdateLast 를 봉 단위로 누적한 결과 여야 한다.
/// </summary>
public class IndicatorParityTests
{
    public static TheoryData<string> Keys => new()
    {
        "SMA20","EMA20","WMA20","RSI14","MACD","OBV20","VWAP","DISP20","JMA14","ST10"
    };

    private static IIndicator Create(string key) => key switch
    {
        "SMA20"  => new MA_Indicator(20, "SMA"),
        "EMA20"  => new MA_Indicator(20, "EMA"),
        "WMA20"  => new MA_Indicator(20, "WMA"),
        "RSI14"  => new RSI_Indicator(14, 9),
        "MACD"   => new MACD_Indicator(12, 26, 9),
        "OBV20"  => new OBV_Indicator(20),
        "VWAP"   => new VWAP_Indicator(1.0f, 2.0f),
        "DISP20" => new Disparity_Indicator(20),
        "JMA14"  => new JMA_Indicator(14, 50, 2),
        "ST10"   => new SuperTrend_Indicator(10, 3.0f),
        _ => throw new ArgumentOutOfRangeException(nameof(key))
    };

    /// <summary>새 봉 확정 경로: prev.Count == candles.Count - 1</summary>
    [Theory, MemberData(nameof(Keys))]
    public void UpdateLast_Append_MatchesCalculate(string key)
    {
        var candles = CandleFactory.Deterministic(300);
        var expected = Create(key).Calculate(candles);

        var ind = Create(key);
        var grown = new List<CandleItem>();
        var acc = new List<IndicatorResult>();
        for (int i = 0; i < candles.Count; i++)
        {
            grown.Add(candles[i]);
            acc.Add(ind.UpdateLast(grown, acc));
        }

        Assert.Equal(expected.Count, acc.Count);
        for (int i = 0; i < expected.Count; i++) AssertSame(expected[i], acc[i], key, i);
    }

    /// <summary>
    /// 미확정 봉 갱신 경로: 같은 인덱스로 여러 번 호출해도 내부 상태가 오염되지 않아야 한다.
    /// SuperTrend/JMA 처럼 상태를 가진 지표가 여기서 걸린다.
    /// </summary>
    [Theory, MemberData(nameof(Keys))]
    public void UpdateLast_InProgress_IsIdempotent(string key)
    {
        var candles = CandleFactory.Deterministic(200);
        var expected = Create(key).Calculate(candles);

        var ind = Create(key);
        var grown = new List<CandleItem>();
        var acc = new List<IndicatorResult>();
        for (int i = 0; i < candles.Count; i++)
        {
            grown.Add(candles[i]);
            var r = ind.UpdateLast(grown, acc);   // 확정
            acc.Add(r);
            for (int k = 0; k < 3; k++)           // 같은 봉 재갱신 3회
                acc[acc.Count - 1] = ind.UpdateLast(grown, acc);
        }

        for (int i = 0; i < expected.Count; i++) AssertSame(expected[i], acc[i], key, i);
    }

    private static void AssertSame(IndicatorResult a, IndicatorResult b, string key, int i)
    {
        Assert.Equal(a.PanelIndex, b.PanelIndex);
        foreach (var kv in a.Values)
        {
            Assert.True(b.Values.ContainsKey(kv.Key), $"{key}[{i}] 키 누락: {kv.Key}");
            float x = kv.Value, y = b.Values[kv.Key];
            if (float.IsNaN(x)) { Assert.True(float.IsNaN(y), $"{key}[{i}].{kv.Key} NaN 불일치"); continue; }
            float tol = MathF.Max(1e-3f, MathF.Abs(x) * 1e-4f);
            Assert.True(MathF.Abs(x - y) <= tol, $"{key}[{i}].{kv.Key} 기대 {x} 실제 {y}");
        }
    }
}