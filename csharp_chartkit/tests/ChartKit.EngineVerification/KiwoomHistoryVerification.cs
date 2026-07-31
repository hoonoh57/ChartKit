using System.Globalization;
using System.Net;
using System.Text;
using ChartKit.CSharp.Contracts;
using ChartKit.CSharp.DataSources;

namespace ChartKit.CSharp.EngineVerification;

internal static class KiwoomHistoryVerification
{
    public static async Task RunAsync()
    {
        await VerifyBasicHistoryAndMetadataAsync();
        await VerifyPagedHistoryCountAsync();
        Console.WriteLine("csharp_kiwoom_history=PASS");
        Console.WriteLine("csharp_kiwoom_paged_history_count=PASS");
        Console.WriteLine("csharp_instrument_metadata=PASS");
    }

    private static async Task VerifyBasicHistoryAndMetadataAsync()
    {
        int tokenCalls = 0;
        int chartCalls = 0;
        int metadataCalls = 0;
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
            if (path == "/api/dostk/stkinfo")
            {
                metadataCalls++;
                return ScriptedHttpHandler.Json(
                    HttpStatusCode.OK,
                    "{\"stk_nm\":\"SK하이닉스\"}");
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
        InstrumentMetadata metadata = await source.GetInstrumentMetadataAsync(
            "000660_AL",
            CancellationToken.None);

        if (tokenCalls != 1 || chartCalls != 1 || metadataCalls != 1)
            throw new InvalidOperationException("History/metadata request count failed.");
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
        HttpCall metadataCall = handler.Calls.Single(
            call => call.Path == "/api/dostk/stkinfo");
        if (metadataCall.ApiId != "ka10001" ||
            !metadataCall.Body.Contains("000660", StringComparison.Ordinal) ||
            metadataCall.Body.Contains("_AL", StringComparison.Ordinal))
            throw new InvalidOperationException("Instrument metadata request contract failed.");
        if (metadata.Symbol != "000660_AL" ||
            metadata.DisplayName != "SK하이닉스" ||
            metadata.Market != "NXT")
            throw new InvalidOperationException("Instrument metadata mapping failed.");
    }

    private static async Task VerifyPagedHistoryCountAsync()
    {
        int chartPage = 0;
        var handler = new ScriptedHttpHandler((request, _) =>
        {
            string path = request.RequestUri?.AbsolutePath ?? "";
            if (path == "/oauth2/token")
            {
                return ScriptedHttpHandler.Json(
                    HttpStatusCode.OK,
                    "{\"token\":\"paged-token\",\"expires_in\":3600}");
            }
            if (path != "/api/dostk/chart")
                return ScriptedHttpHandler.Json(HttpStatusCode.NotFound, "{}");

            int page = chartPage++;
            int newestValue = 300 - page * 100;
            string json = BuildMinutePage(newestValue, 100);
            return page switch
            {
                0 => ScriptedHttpHandler.Json(
                    HttpStatusCode.OK, json, continuation: "Y", nextKey: "K1"),
                1 => ScriptedHttpHandler.Json(
                    HttpStatusCode.OK, json, continuation: "Y", nextKey: "K2"),
                _ => ScriptedHttpHandler.Json(HttpStatusCode.OK, json)
            };
        });

        await using var session = new KiwoomApiSession(
            KiwoomSessionVerification.Options(TimeSpan.Zero),
            handler,
            new FakeKiwoomClock());
        await using var source = new KiwoomRestDataSource(session: session);
        IReadOnlyList<Candle> candles = await source.GetHistoryAsync(
            new HistoryRequest("000660", CandleTimeframe.Minute(1), 250),
            CancellationToken.None);

        if (candles.Count != 250)
            throw new InvalidOperationException(
                $"Paged history did not return the requested count: {candles.Count}.");
        if (candles[0].Close != 51f || candles[^1].Close != 300f)
            throw new InvalidOperationException("Paged history tail selection failed.");
        if (candles[0].Sequence != 0 || candles[^1].Sequence != 249)
            throw new InvalidOperationException("Paged history sequence mapping failed.");

        HttpCall[] calls = handler.Calls
            .Where(call => call.Path == "/api/dostk/chart")
            .ToArray();
        if (calls.Length != 3 ||
            calls[0].Continuation != "N" || calls[0].NextKey.Length != 0 ||
            calls[1].Continuation != "Y" || calls[1].NextKey != "K1" ||
            calls[2].Continuation != "Y" || calls[2].NextKey != "K2")
            throw new InvalidOperationException("Paged history continuation contract failed.");
    }

    private static string BuildMinutePage(int newestValue, int count)
    {
        DateTime latest = new(2026, 7, 30, 15, 30, 0);
        var builder = new StringBuilder();
        builder.Append("{\"stk_min_pole_chart_qry\":[");
        for (int index = 0; index < count; index++)
        {
            if (index > 0) builder.Append(',');
            int value = newestValue - index;
            int globalOffset = 300 - value;
            DateTime time = latest.AddMinutes(-globalOffset);
            builder.Append("{\"cur_prc\":\"+")
                .Append(value.ToString(CultureInfo.InvariantCulture))
                .Append("\",\"open_pric\":\"+")
                .Append(value.ToString(CultureInfo.InvariantCulture))
                .Append("\",\"high_pric\":\"+")
                .Append((value + 1).ToString(CultureInfo.InvariantCulture))
                .Append("\",\"low_pric\":\"+")
                .Append(Math.Max(1, value - 1).ToString(CultureInfo.InvariantCulture))
                .Append("\",\"trde_qty\":\"1\",\"cntr_tm\":\"")
                .Append(time.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture))
                .Append("\"}");
        }
        builder.Append("]}");
        return builder.ToString();
    }
}
