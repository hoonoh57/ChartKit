using System.Globalization;
using System.Net;
using ChartKit.CSharp.DataSources;

namespace ChartKit.CSharp.EngineVerification;

internal static class TradingDayProbeVerification
{
    public static async Task RunAsync()
    {
        await VerifyTodayUsesMinuteCandlesAsync();
        await VerifyHistoricalDateUsesDailyCandlesAsync();
        await VerifyProbeFailureDoesNotBecomeHolidayAsync();
        Console.WriteLine("csharp_trading_day_today_minute=PASS");
        Console.WriteLine("csharp_trading_day_history_daily=PASS");
        Console.WriteLine("csharp_trading_day_failure_unknown=PASS");
    }

    private static async Task VerifyTodayUsesMinuteCandlesAsync()
    {
        DateTime today = DateTime.Today;
        string timestamp = today.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + "090100";
        var handler = new ScriptedHttpHandler((request, _) =>
        {
            string path = request.RequestUri?.AbsolutePath ?? string.Empty;
            if (path == "/oauth2/token")
            {
                return ScriptedHttpHandler.Json(
                    HttpStatusCode.OK,
                    "{\"token\":\"probe-today-token\",\"expires_in\":3600}");
            }

            return ScriptedHttpHandler.Json(
                HttpStatusCode.OK,
                BuildMinuteResponse(timestamp));
        });

        await using var session = new KiwoomApiSession(
            KiwoomSessionVerification.Options(TimeSpan.Zero),
            handler,
            new FakeKiwoomClock());
        await using var source = new KiwoomRestDataSource(session: session);

        TradingDayProbeSnapshot result = await source.ProbeTradingDayAsync(today);
        if (result.State != TradingDayProbeState.TradingDay ||
            result.Method != TradingDayProbeMethod.TodayMinute ||
            result.SuccessfulQueries != 2 ||
            result.FailedQueries != 0 ||
            result.SymbolsWithData.Length != 2)
        {
            throw new InvalidOperationException("Today minute trading-day probe failed.");
        }

        HttpCall[] calls = handler.Calls
            .Where(call => call.Path == "/api/dostk/chart")
            .ToArray();
        if (calls.Length != 2 ||
            calls.Any(call => call.ApiId != "ka10080") ||
            calls.Any(call =>
                !call.Body.Contains("\"tic_scope\":\"1\"", StringComparison.Ordinal)))
        {
            throw new InvalidOperationException(
                "Today trading-day probe did not use one-row 1-minute requests.");
        }
    }

    private static async Task VerifyHistoricalDateUsesDailyCandlesAsync()
    {
        DateTime targetDate = DateTime.Today.AddDays(-10);
        string dateText = targetDate.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        var handler = new ScriptedHttpHandler((request, _) =>
        {
            string path = request.RequestUri?.AbsolutePath ?? string.Empty;
            if (path == "/oauth2/token")
            {
                return ScriptedHttpHandler.Json(
                    HttpStatusCode.OK,
                    "{\"token\":\"probe-history-token\",\"expires_in\":3600}");
            }

            return ScriptedHttpHandler.Json(
                HttpStatusCode.OK,
                BuildDailyResponse(dateText));
        });

        await using var session = new KiwoomApiSession(
            KiwoomSessionVerification.Options(TimeSpan.Zero),
            handler,
            new FakeKiwoomClock());
        await using var source = new KiwoomRestDataSource(session: session);

        TradingDayProbeSnapshot result = await source.ProbeTradingDayAsync(targetDate);
        if (result.State != TradingDayProbeState.TradingDay ||
            result.Method != TradingDayProbeMethod.HistoricalDaily ||
            result.SuccessfulQueries != 2 ||
            result.SymbolsWithData.Length != 2)
        {
            throw new InvalidOperationException("Historical daily trading-day probe failed.");
        }

        HttpCall[] calls = handler.Calls
            .Where(call => call.Path == "/api/dostk/chart")
            .ToArray();
        if (calls.Length != 2 ||
            calls.Any(call => call.ApiId != "ka10081") ||
            calls.Any(call =>
                !call.Body.Contains(
                    $"\"base_dt\":\"{dateText}\"",
                    StringComparison.Ordinal)))
        {
            throw new InvalidOperationException(
                "Historical trading-day probe did not use one-row daily requests.");
        }
    }

    private static async Task VerifyProbeFailureDoesNotBecomeHolidayAsync()
    {
        DateTime targetDate = DateTime.Today.AddDays(-20);
        int chartCalls = 0;
        var handler = new ScriptedHttpHandler((request, _) =>
        {
            string path = request.RequestUri?.AbsolutePath ?? string.Empty;
            if (path == "/oauth2/token")
            {
                return ScriptedHttpHandler.Json(
                    HttpStatusCode.OK,
                    "{\"token\":\"probe-failure-token\",\"expires_in\":3600}");
            }

            chartCalls++;
            return chartCalls == 1
                ? ScriptedHttpHandler.Json(HttpStatusCode.InternalServerError, "{\"error\":\"failed\"}")
                : ScriptedHttpHandler.Json(HttpStatusCode.OK, "{\"stk_dt_pole_chart_qry\":[]}");
        });

        await using var session = new KiwoomApiSession(
            KiwoomSessionVerification.Options(TimeSpan.Zero),
            handler,
            new FakeKiwoomClock());
        await using var source = new KiwoomRestDataSource(session: session);

        TradingDayProbeSnapshot result = await source.ProbeTradingDayAsync(targetDate);
        if (result.State != TradingDayProbeState.Unknown ||
            result.SuccessfulQueries != 1 ||
            result.FailedQueries != 1 ||
            string.IsNullOrWhiteSpace(result.LastError))
        {
            throw new InvalidOperationException(
                "Probe request failure was incorrectly classified as a holiday.");
        }
    }

    private static string BuildMinuteResponse(string timestamp) =>
        "{\"stk_min_pole_chart_qry\":[{" +
        "\"cur_prc\":\"+100\"," +
        "\"open_pric\":\"+100\"," +
        "\"high_pric\":\"+101\"," +
        "\"low_pric\":\"+99\"," +
        "\"trde_qty\":\"1\"," +
        $"\"cntr_tm\":\"{timestamp}\"" +
        "}]}";

    private static string BuildDailyResponse(string dateText) =>
        "{\"stk_dt_pole_chart_qry\":[{" +
        "\"cur_prc\":\"+100\"," +
        "\"open_pric\":\"+100\"," +
        "\"high_pric\":\"+101\"," +
        "\"low_pric\":\"+99\"," +
        "\"trde_qty\":\"1\"," +
        $"\"dt\":\"{dateText}\"" +
        "}]}";
}
