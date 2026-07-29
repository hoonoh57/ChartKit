using ChartKit.DataSources;
using Xunit;

namespace ChartKit.Tests;

/// <summary>
/// EnvConfig.Pick 폴백 검증.
/// VB 의 2항 If 는 Nothing 만 판정하므로, 빈 문자열 폴백이 깨져 있었다.
/// </summary>
public class EnvConfigTests
{
    [Theory]
    [InlineData("real", "common", "real")]   // 전용키 우선
    [InlineData("", "common", "common")]     // 빈 문자열 -> 공용키 (기존 버그 지점)
    [InlineData("   ", "common", "common")]  // 공백만 -> 공용키
    [InlineData(null, "common", "common")]   // Nothing -> 공용키
    [InlineData(" real ", "common", "real")] // 트림
    [InlineData("", "", "")]                 // 둘 다 없음
    [InlineData("", null, "")]               // 공용키 Nothing 이어도 예외 없음
    public void Pick_FallsBackToCommonKey(string? specific, string? common, string expected)
        => Assert.Equal(expected, EnvConfig.Pick(specific!, common!));

    [Fact]
    public void CandidatePaths_HasNoHardcodedAbsolutePath()
    {
        // 하드코딩된 개발자 로컬 경로가 되살아나면 실패한다.
        var src = System.IO.File.ReadAllText(
            System.IO.Path.Combine(TestPaths.RepoRoot, "src", "ChartKit", "DataSources", "EnvConfig.vb"));
        Assert.DoesNotContain(@"chart_base_trading", src);
    }
}

internal static class TestPaths
{
    public static string RepoRoot
    {
        get
        {
            var d = new System.IO.DirectoryInfo(System.AppContext.BaseDirectory);
            while (d is not null && !System.IO.Directory.Exists(System.IO.Path.Combine(d.FullName, ".git")))
                d = d.Parent;
            Assert.NotNull(d);
            return d!.FullName;
        }
    }
}