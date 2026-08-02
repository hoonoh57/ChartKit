using System.Globalization;
using System.Net;
using System.Text;
using ChartKit.CSharp.Contracts;
using ChartKit.CSharp.DataSources;

namespace ChartKit.CSharp.EngineVerification;

internal static class KiwoomTickSourceOrderVerification
{
    private const int RequestedTargetCandles = 4_000;
    private const int TargetTicks = 120;
    private const int BaseTicks = 30;
    private const int GroupSize = TargetTicks / BaseTicks;
    private const int RequiredBaseRows =
        RequestedTargetCandles * GroupSize + GroupSize;
    private const int PageSize = 1_000;

    public static async Task RunAsync()
    {
        int chartPage = 0;
        var handler = new ScriptedHttpHandler((request, _) =>
        {
            string path = request.RequestUri?.AbsolutePath ?? string.Empty;
            if (path == "/oauth2/token")
            {
                return ScriptedHttpHandler.Json(
                    HttpStatusCode.OK,
                    "{\"token\":\"tick-order-token\",\"expires_in\":3600}");
            }

            if (path != "/api/dostk/chart")
                return ScriptedHttpHandler.Json(HttpStatusCode.NotFound, "{}");

            int sourceOffset = chartPage * PageSize;
            int remaining = RequiredBaseRows - sourceOffset;
            int rowCount = Math.Min(PageSize, remaining);
            string json = BuildTickPage(sourceOffset, rowCount);
            bool hasMore = sourceOffset + rowCount < RequiredBaseRows;
            chartPage++;

            return hasMore
                ? ScriptedHttpHandler.Json(
                    HttpStatusCode.OK,
                    json,
                    continuation: "Y",
                    nextKey: $"T{chartPage}")
                : ScriptedHttpHandler.Json(HttpStatusCode.OK, json);
        });

        await using var session = new KiwoomApiSession(
            KiwoomSessionVerification.Options(TimeSpan.Zero),
            handler,
            new FakeKiwoomClock());
        await using var source = new KiwoomRestDataSource(session: session);

        IReadOnlyList<Candle> candles = await source.GetHistoryAsync(
            new HistoryRequest(
                "034020_AL",
                CandleTimeframe.Tick(TargetTicks),
                RequestedTargetCandles),
            CancellationToken.None);

        VerifyResult(candles);
        VerifyRequestContract(handler);

        Console.WriteLine("csharp_kiwoom_120tick_4000_source_order=PASS");
        Console.WriteLine("csharp_kiwoom_equal_hhmm_tick_order=PASS");
        Console.WriteLine("csharp_kiwoom_tick_page_append_contract=PASS");
    }

    private static void VerifyResult(IReadOnlyList<Candle> candles)
    {
        if (candles.Count != RequestedTargetCandles)
        {
            throw new InvalidOperationException(
                $"120-tick paged history count mismatch: {candles.Count}.");
        }

        Candle first = candles[0];
        Candle last = candles[^1];
        if (first.Open != 4_001f ||
            first.High != 4_004f ||
            first.Low != 4_001f ||
            first.Close != 4_004f ||
            last.Open != 19_997f ||
            last.High != 20_000f ||
            last.Low != 19_997f ||
            last.Close != 20_000f)
        {
            throw new InvalidOperationException(
                "120-tick source/page direction changed the expected OHLC grouping.");
        }

        if (candles.Any(candle => candle.Volume != GroupSize))
        {
            throw new InvalidOperationException(
                "120-tick aggregation dropped or duplicated a 30-tick source row.");
        }

        for (int index = 0; index < candles.Count; index++)
        {
            float expectedOpen = 4_001f + index * GroupSize;
            float expectedClose = expectedOpen + GroupSize - 1;
            Candle candle = candles[index];
            if (candle.Open != expectedOpen ||
                candle.Low != expectedOpen ||
                candle.Close != expectedClose ||
                candle.High != expectedClose)
            {
                throw new InvalidOperationException(
                    $"120-tick array order mismatch at candle {index}: " +
                    $"expected {expectedOpen}-{expectedClose}, " +
                    $"actual O={candle.Open}, H={candle.High}, " +
                    $"L={candle.Low}, C={candle.Close}.");
            }
        }

        if (candles[0].Sequence != 1 || candles[^1].Sequence != 4_000)
        {
            throw new InvalidOperationException(
                "120-tick tail selection changed the established sequence contract.");
        }

        if (candles.Select(candle => candle.CloseTime).Distinct().Count() != 1)
        {
            throw new InvalidOperationException(
                "Equal-HHmm tick rows were reordered or collapsed by time.");
        }
    }

    private static void VerifyRequestContract(ScriptedHttpHandler handler)
    {
        HttpCall[] calls = handler.Calls
            .Where(call => call.Path == "/api/dostk/chart")
            .ToArray();
        int expectedPageCount =
            (RequiredBaseRows + PageSize - 1) / PageSize;

        if (calls.Length != expectedPageCount)
        {
            throw new InvalidOperationException(
                $"Expected {expectedPageCount} tick pages, got {calls.Length}.");
        }

        if (calls.Any(call => call.ApiId != "ka10079") ||
            calls.Any(call =>
                !call.Body.Contains(
                    "\"tic_scope\":\"30\"",
                    StringComparison.Ordinal)))
        {
            throw new InvalidOperationException(
                "120-tick history did not request the 30-tick base contract.");
        }

        if (calls[0].Continuation != "N" ||
            calls[0].NextKey.Length != 0)
        {
            throw new InvalidOperationException(
                "First tick page request must start without continuation.");
        }

        for (int index = 1; index < calls.Length; index++)
        {
            string expectedKey = $"T{index}";
            if (calls[index].Continuation != "Y" ||
                calls[index].NextKey != expectedKey)
            {
                throw new InvalidOperationException(
                    $"Tick page {index} continuation mismatch: " +
                    $"continuation={calls[index].Continuation}, " +
                    $"nextKey={calls[index].NextKey}, " +
                    $"expectedKey={expectedKey}.");
            }
        }
    }

    private static string BuildTickPage(int sourceOffset, int count)
    {
        var builder = new StringBuilder();
        builder.Append(
            "{\"last_tic_cnt\":\"30\",\"stk_tic_chart_qry\":[");

        for (int index = 0; index < count; index++)
        {
            if (index > 0) builder.Append(',');

            int value = 20_000 - sourceOffset - index;
            builder.Append("{\"cur_prc\":\"+")
                .Append(value.ToString(CultureInfo.InvariantCulture))
                .Append("\",\"open_pric\":\"+")
                .Append(value.ToString(CultureInfo.InvariantCulture))
                .Append("\",\"high_pric\":\"+")
                .Append(value.ToString(CultureInfo.InvariantCulture))
                .Append("\",\"low_pric\":\"+")
                .Append(value.ToString(CultureInfo.InvariantCulture))
                .Append("\",\"trde_qty\":\"1\",\"cntr_tm\":")
                .Append("\"20260730090100\"}");
        }

        builder.Append("]}");
        return builder.ToString();
    }
}
