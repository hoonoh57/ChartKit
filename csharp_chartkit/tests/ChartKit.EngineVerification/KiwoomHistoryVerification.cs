using System.Net;
using ChartKit.CSharp.Contracts;
using ChartKit.CSharp.DataSources;

namespace ChartKit.CSharp.EngineVerification;

internal static class KiwoomHistoryVerification
{
    public static async Task RunAsync()
    {
        int tokenCalls = 0;
        int chartCalls = 0;
        var handler = new ScriptedHttpHandler((request, _) =>
        {
            string path = request.RequestUri?.AbsolutePath ?? "";
            if (path == "/oauth2/token")
            {
                tokenCalls++;
                return ScriptedHttpHandler.Json(
                    HttpStatusCode.OK,
                    "{\"token\":\"history-token\",\"expires_in\":3600}");
            }
            if (path == "/api/dostk/chart")
            {
                chartCalls++;
                string json = """
                {
                  "stk_min_pole_chart_qry": [
                    {"cur_prc":"+103","open_pric":"+102","high_pric":"+104","low_pric":"+101","trde_qty":"1,300","cntr_tm":"20260730100300"},
                    {"cur_prc":"+102","open_pric":"+101","high_pric":"+103","low_pric":"+100","trde_qty":"1,200","cntr_tm":"20260730100200"},
                    {"cur_prc":"+101","open_pric":"+100","high_pric":"+102","low_pric":"+99","trde_qty":"1,100","cntr_tm":"20260730100100"}
                  ]
                }
                """;
                return ScriptedHttpHandler.Json(HttpStatusCode.OK, json);
            }
            return ScriptedHttpHandler.Json(HttpStatusCode.NotFound, "{}");
        });

        await using var session = new KiwoomApiSession(
            KiwoomSessionVerification.Options(TimeSpan.Zero),
            handler,
            new FakeKiwoomClock());
        await using var source = new KiwoomRestDataSource(session: session);
        IReadOnlyList<Candle> candles = await source.GetHistoryAsync(
            new HistoryRequest("000660", CandleTimeframe.Minute(1), 3),
            CancellationToken.None);

        if (tokenCalls != 1 || chartCalls != 1)
            throw new InvalidOperationException("History request count failed.");
        if (candles.Count != 3)
            throw new InvalidOperationException("History row count failed.");
        if (candles[0].Close != 101f || candles[2].Close != 103f)
            throw new InvalidOperationException("History chronological order failed.");
        if (candles[0].OpenTime != new DateTime(2026, 7, 30, 10, 0, 0) ||
            candles[0].CloseTime != new DateTime(2026, 7, 30, 10, 1, 0))
            throw new InvalidOperationException("History minute time mapping failed.");
        if (candles[2].Volume != 1300 || candles[2].Sequence != 2)
            throw new InvalidOperationException("History numeric parsing failed.");

        HttpCall chart = handler.Calls.Single(call => call.Path == "/api/dostk/chart");
        if (chart.ApiId != "ka10080" || chart.Continuation != "N" ||
            !chart.Body.Contains("000660", StringComparison.Ordinal))
            throw new InvalidOperationException("History request contract failed.");

        Console.WriteLine("csharp_kiwoom_history=PASS");
    }
}
