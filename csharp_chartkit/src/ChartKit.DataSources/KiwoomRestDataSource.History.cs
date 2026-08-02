using System.Globalization;
using System.Text.Json;
using ChartKit.CSharp.Contracts;

namespace ChartKit.CSharp.DataSources;

public sealed partial class KiwoomRestDataSource
{
    private const int MaximumContinuationPages = 1000;

    public async Task<IReadOnlyList<Candle>> GetHistoryAsync(
        HistoryRequest request,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(request);
        request.Timeframe.Validate();

        string symbol = string.IsNullOrWhiteSpace(request.Symbol)
            ? _session.Options.DefaultSymbol
            : request.Symbol.Trim();
        int count = Math.Max(1, request.Count);
        const string path = "/api/dostk/chart";

        switch (request.Timeframe.Unit)
        {
            case CandleUnit.Day:
            case CandleUnit.Week:
            case CandleUnit.Month:
            {
                string apiId = request.Timeframe.Unit switch
                {
                    CandleUnit.Day => "ka10081",
                    CandleUnit.Week => "ka10082",
                    _ => "ka10083"
                };
                string body = JsonSerializer.Serialize(new Dictionary<string, string>
                {
                    ["stk_cd"] = symbol,
                    ["base_dt"] = (request.To ?? DateTime.Now).ToString(
                        "yyyyMMdd", CultureInfo.InvariantCulture),
                    ["upd_stkpc_tp"] = _session.Options.AdjustPrice
                });
                PagedRows page = await FetchPagedAsync(
                    path, apiId, body, count, cancellationToken).ConfigureAwait(false);
                List<Candle> result = NormalizeAndTail(
                    ParseRows(page.Rows, request.Timeframe, daily: true),
                    request.Timeframe,
                    SourceArrayDirection.ReverseWhole,
                    count);
                SaveHistorySeed(symbol, result, 0);
                return result;
            }

            case CandleUnit.Tick:
            {
                int targetTicks = request.Timeframe.Value;
                int baseTicks = TickCandleAggregator.ChooseBase(targetTicks);
                int groupSize = targetTicks / baseTicks;
                int requiredBaseRows = count * groupSize + groupSize;
                string body = JsonSerializer.Serialize(new Dictionary<string, string>
                {
                    ["stk_cd"] = symbol,
                    ["tic_scope"] = baseTicks.ToString(CultureInfo.InvariantCulture),
                    ["upd_stkpc_tp"] = _session.Options.AdjustPrice
                });
                PagedRows page = await FetchPagedAsync(
                    path, "ka10079", body, requiredBaseRows, cancellationToken)
                    .ConfigureAwait(false);
                CandleTimeframe baseTimeframe = CandleTimeframe.Tick(baseTicks);
                List<Candle> baseCandles = MarketDataNormalizer.NormalizeHistory(
                    ParseRows(page.Rows, baseTimeframe, daily: false),
                    baseTimeframe,
                    SourceArrayDirection.ReverseWhole);
                List<Candle> result = NormalizeAndTail(
                    TickCandleAggregator.Aggregate(
                        baseCandles,
                        targetTicks,
                        baseTicks),
                    request.Timeframe,
                    SourceArrayDirection.Forward,
                    count);
                int currentTickCount = Math.Min(
                    targetTicks,
                    Math.Max(0,
                        (groupSize - 1) * baseTicks +
                        Math.Min(baseTicks, page.LastTickCount)));
                SaveHistorySeed(symbol, result, currentTickCount);
                return result;
            }

            default:
            {
                string body = JsonSerializer.Serialize(new Dictionary<string, string>
                {
                    ["stk_cd"] = symbol,
                    ["tic_scope"] = request.Timeframe.Value.ToString(
                        CultureInfo.InvariantCulture),
                    ["upd_stkpc_tp"] = _session.Options.AdjustPrice
                });
                PagedRows page = await FetchPagedAsync(
                    path, "ka10080", body, count, cancellationToken)
                    .ConfigureAwait(false);
                List<Candle> result = NormalizeAndTail(
                    ParseRows(page.Rows, request.Timeframe, daily: false),
                    request.Timeframe,
                    SourceArrayDirection.ReverseWhole,
                    count);
                SaveHistorySeed(symbol, result, 0);
                return result;
            }
        }
    }

    public async Task<string> GetStockNameAsync(
        string symbol,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        string code = string.IsNullOrWhiteSpace(symbol)
            ? _session.Options.DefaultSymbol
            : symbol.Trim();
        string body = JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["stk_cd"] = code
        });
        using KiwoomJsonResponse response = await _session.PostJsonAsync(
            "/api/dostk/stkinfo", "ka10001", body,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        return KiwoomJson.Text(response.Document.RootElement, "stk_nm").Trim();
    }

    private async Task<PagedRows> FetchPagedAsync(
        string path,
        string apiId,
        string body,
        int maximumRows,
        CancellationToken cancellationToken)
    {
        var rows = new List<JsonElement>(Math.Min(maximumRows, 4096));
        string continuation = "N";
        string nextKey = "";
        int lastTickCount = 0;

        for (int pageIndex = 0;
             pageIndex < MaximumContinuationPages;
             pageIndex++)
        {
            string requestedKey = nextKey;
            using KiwoomJsonResponse response = await _session.PostJsonAsync(
                path,
                apiId,
                body,
                continuation,
                nextKey,
                cancellationToken).ConfigureAwait(false);
            if (pageIndex == 0 && apiId == "ka10079")
                lastTickCount = KiwoomJson.ReadInt(
                    response.Document.RootElement, "last_tic_cnt");
            KiwoomJson.AppendFirstObjectArray(response.Document.RootElement, rows);
            continuation = response.Continuation.Trim().ToUpperInvariant();
            nextKey = response.NextKey.Trim();

            if (rows.Count >= maximumRows || continuation != "Y") break;
            if (nextKey.Length == 0 ||
                string.Equals(nextKey, requestedKey, StringComparison.Ordinal))
                break;
        }

        return new PagedRows(rows, lastTickCount);
    }

    private static List<Candle> ParseRows(
        List<JsonElement> rows,
        CandleTimeframe timeframe,
        bool daily)
    {
        var output = new List<Candle>(rows.Count);
        foreach (JsonElement row in rows)
        {
            double? closeValue = KiwoomJson.Number(row, "cur_prc");
            if (!closeValue.HasValue) continue;
            double openValue = KiwoomJson.Number(row, "open_pric") ?? closeValue.Value;
            double highValue = KiwoomJson.Number(row, "high_pric") ?? closeValue.Value;
            double lowValue = KiwoomJson.Number(row, "low_pric") ?? closeValue.Value;
            double volumeValue = KiwoomJson.Number(row, "trde_qty") ?? 0d;
            string timeText = daily
                ? KiwoomJson.Text(row, "dt", "stck_bsop_date")
                : KiwoomJson.Text(row, "cntr_tm", "dt");
            DateTime reference = KiwoomJson.ParseTime(timeText, daily);
            DateTime openTime;
            DateTime closeTime;
            if (daily)
            {
                openTime = reference.Date;
                closeTime = timeframe.Unit switch
                {
                    CandleUnit.Week => openTime.AddDays(7),
                    CandleUnit.Month => openTime.AddMonths(1),
                    _ => openTime.AddDays(1)
                };
            }
            else if (timeframe.Unit == CandleUnit.Minute)
            {
                closeTime = reference;
                openTime = closeTime.AddMinutes(-timeframe.Value);
            }
            else
            {
                openTime = reference;
                closeTime = reference;
            }

            long sequence = output.Count;
            output.Add(new Candle(
                openTime,
                closeTime,
                (float)Math.Abs(openValue),
                (float)Math.Abs(highValue),
                (float)Math.Abs(lowValue),
                (float)Math.Abs(closeValue.Value),
                (long)Math.Abs(volumeValue),
                true,
                sequence));
        }
        return output;
    }

    private void SaveHistorySeed(
        string symbol,
        IReadOnlyList<Candle> candles,
        int tickCount)
    {
        if (candles.Count > 0) SaveSeed(symbol, candles[^1], tickCount);
    }

    private static List<Candle> NormalizeAndTail(
        IReadOnlyList<Candle> source,
        CandleTimeframe timeframe,
        SourceArrayDirection direction,
        int count)
    {
        List<Candle> normalized = MarketDataNormalizer.NormalizeHistory(
            source,
            timeframe,
            direction);
        return Tail(normalized, count);
    }

    private static List<Candle> Tail(List<Candle> source, int count)
    {
        if (source.Count <= count) return source;
        return source.GetRange(source.Count - count, count);
    }

    private sealed record PagedRows(List<JsonElement> Rows, int LastTickCount);
}
