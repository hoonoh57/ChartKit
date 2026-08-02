using System.Net;
using ChartKit.CSharp.Contracts;
using ChartKit.CSharp.DataSources;

namespace ChartKit.CSharp.EngineVerification;

internal static class InstrumentSearchVerification
{
    public static async Task RunAsync()
    {
        var handler = new ScriptedHttpHandler((_, call) => call switch
        {
            1 => ScriptedHttpHandler.Json(
                HttpStatusCode.OK,
                "{\"token\":\"instrument-token\",\"expires_in\":3600}"),
            2 => ScriptedHttpHandler.Json(
                HttpStatusCode.OK,
                "{\"list\":[" +
                "{\"code\":\"005930\",\"name\":\"삼성전자\",\"nxtEnable\":\"Y\"}," +
                "{\"code\":\"000660\",\"name\":\"SK하이닉스\",\"nxtEnable\":\"Y\"}" +
                "],\"return_code\":0}"),
            3 => ScriptedHttpHandler.Json(
                HttpStatusCode.OK,
                "{\"list\":[" +
                "{\"code\":\"006400\",\"name\":\"삼성SDI\",\"nxtEnable\":\"Y\"}," +
                "{\"code\":\"035720\",\"name\":\"카카오\",\"nxtEnable\":\"Y\"}" +
                "],\"return_code\":0}"),
            4 => ScriptedHttpHandler.Json(
                HttpStatusCode.OK,
                "{\"list\":[" +
                "{\"code\":\"069500\",\"name\":\"KODEX 200\",\"nxtEnable\":\"N\"}" +
                "],\"return_code\":0}"),
            _ => throw new InvalidOperationException(
                $"Unexpected instrument search HTTP call {call}.")
        });
        var options = new KiwoomOptions(
            IsMock: false,
            AppKey: "app",
            SecretKey: "secret",
            RestBaseUri: new Uri("https://api.test"),
            WebSocketUri: new Uri("wss://socket.test"),
            AdjustPrice: "1",
            DefaultSymbol: "005930",
            RequestInterval: TimeSpan.Zero,
            RequestTimeout: TimeSpan.FromSeconds(5));

        await using var session = new KiwoomApiSession(options, handler);
        await using var source = new KiwoomRestDataSource(options, session);

        IReadOnlyList<InstrumentSearchResult> samsung =
            await source.SearchInstrumentsAsync("삼성", 10);
        Require(
            samsung.Select(static item => item.Symbol)
                .OrderBy(static item => item, StringComparer.Ordinal)
                .SequenceEqual(["005930", "006400"]),
            "Korean partial-name search failed.");

        IReadOnlyList<InstrumentSearchResult> exactName =
            await source.SearchInstrumentsAsync("삼성 전자", 10);
        Require(
            exactName.Count > 0 && exactName[0].Symbol == "005930",
            "Whitespace-normalized exact-name ranking failed.");

        IReadOnlyList<InstrumentSearchResult> exactCode =
            await source.SearchInstrumentsAsync("005930", 10);
        Require(
            exactCode.Count > 0 &&
            exactCode[0].DisplayName == "삼성전자" &&
            exactCode[0].NxtEnabled,
            "Exact-code search failed.");

        IReadOnlyList<InstrumentSearchResult> etf =
            await source.SearchInstrumentsAsync("KODEX", 10);
        Require(
            etf.Count == 1 && etf[0].Symbol == "069500",
            "ETF name search failed.");

        _ = await source.SearchInstrumentsAsync("카카오", 10);
        Require(
            handler.Calls.Count == 4,
            "Instrument catalog must be loaded once and reused from memory.");
        Require(
            handler.Calls.Skip(1).All(static call =>
                call.Path == "/api/dostk/stkinfo" &&
                call.ApiId == "ka10099"),
            "Instrument search used an unexpected Kiwoom endpoint.");

        Console.WriteLine("kiwoom_instrument_search_name=PASS");
        Console.WriteLine("kiwoom_instrument_search_code=PASS");
        Console.WriteLine("kiwoom_instrument_search_cache=PASS");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
