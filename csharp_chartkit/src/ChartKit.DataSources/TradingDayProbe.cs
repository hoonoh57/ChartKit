using System.Globalization;
using System.Text.Json;
using ChartKit.CSharp.Contracts;

namespace ChartKit.CSharp.DataSources;

public enum TradingDayProbeState
{
    Unknown = 0,
    TradingDay = 1,
    NoTradingDay = 2
}

public enum TradingDayProbeMethod
{
    None = 0,
    TodayMinute = 1,
    HistoricalDaily = 2
}

public readonly record struct TradingDayProbeSnapshot(
    DateTime TradingDate,
    TradingDayProbeState State,
    TradingDayProbeMethod Method,
    string[] SymbolsWithData,
    string[] SymbolsWithoutData,
    int SuccessfulQueries,
    int FailedQueries,
    DateTimeOffset CheckedAtUtc,
    string LastError)
{
    public static TradingDayProbeSnapshot Empty(DateTime tradingDate) =>
        new(
            tradingDate.Date,
            TradingDayProbeState.Unknown,
            TradingDayProbeMethod.None,
            [],
            [],
            0,
            0,
            DateTimeOffset.MinValue,
            string.Empty);
}

public interface ITradingDayProbeSource
{
    Task<TradingDayProbeSnapshot> ProbeTradingDayAsync(
        DateTime tradingDate,
        CancellationToken cancellationToken = default);
}

public sealed partial class KiwoomRestDataSource
{
    private static readonly string[] TradingDayBenchmarkSymbols =
        ["005930", "000660"];

    public async Task<TradingDayProbeSnapshot> ProbeTradingDayAsync(
        DateTime tradingDate,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        DateTime targetDate = tradingDate.Date;
        DateTime today = DateTime.Today;
        if (targetDate > today)
        {
            return new TradingDayProbeSnapshot(
                targetDate,
                TradingDayProbeState.Unknown,
                TradingDayProbeMethod.None,
                [],
                [],
                0,
                0,
                DateTimeOffset.UtcNow,
                "Future trading dates are not probed.");
        }

        TradingDayProbeMethod method = targetDate == today
            ? TradingDayProbeMethod.TodayMinute
            : TradingDayProbeMethod.HistoricalDaily;
        var withData = new List<string>(TradingDayBenchmarkSymbols.Length);
        var withoutData = new List<string>(TradingDayBenchmarkSymbols.Length);
        var errors = new List<string>(TradingDayBenchmarkSymbols.Length);
        int successfulQueries = 0;
        int failedQueries = 0;

        foreach (string symbol in TradingDayBenchmarkSymbols)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                DateTime? latestDate = method == TradingDayProbeMethod.TodayMinute
                    ? await GetLatestMinuteTradingDateAsync(
                        symbol,
                        cancellationToken).ConfigureAwait(false)
                    : await GetLatestDailyTradingDateAsync(
                        symbol,
                        targetDate,
                        cancellationToken).ConfigureAwait(false);
                successfulQueries++;
                if (latestDate.HasValue && latestDate.Value.Date == targetDate)
                    withData.Add(symbol);
                else
                    withoutData.Add(symbol);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                failedQueries++;
                errors.Add(symbol + ": " + exception.Message);
            }
        }

        TradingDayProbeState state = withData.Count > 0
            ? TradingDayProbeState.TradingDay
            : successfulQueries == TradingDayBenchmarkSymbols.Length
                ? TradingDayProbeState.NoTradingDay
                : TradingDayProbeState.Unknown;

        return new TradingDayProbeSnapshot(
            targetDate,
            state,
            method,
            withData.ToArray(),
            withoutData.ToArray(),
            successfulQueries,
            failedQueries,
            DateTimeOffset.UtcNow,
            string.Join(" | ", errors));
    }

    private async Task<DateTime?> GetLatestMinuteTradingDateAsync(
        string symbol,
        CancellationToken cancellationToken)
    {
        string body = JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["stk_cd"] = symbol,
            ["tic_scope"] = "1",
            ["upd_stkpc_tp"] = _session.Options.AdjustPrice
        });
        PagedRows page = await FetchPagedAsync(
            "/api/dostk/chart",
            "ka10080",
            body,
            maximumRows: 1,
            cancellationToken).ConfigureAwait(false);
        return ReadLatestTradingDate(
            page.Rows,
            CandleTimeframe.Minute(1),
            daily: false);
    }

    private async Task<DateTime?> GetLatestDailyTradingDateAsync(
        string symbol,
        DateTime targetDate,
        CancellationToken cancellationToken)
    {
        string body = JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["stk_cd"] = symbol,
            ["base_dt"] = targetDate.ToString(
                "yyyyMMdd",
                CultureInfo.InvariantCulture),
            ["upd_stkpc_tp"] = _session.Options.AdjustPrice
        });
        PagedRows page = await FetchPagedAsync(
            "/api/dostk/chart",
            "ka10081",
            body,
            maximumRows: 1,
            cancellationToken).ConfigureAwait(false);
        return ReadLatestTradingDate(
            page.Rows,
            CandleTimeframe.Day,
            daily: true);
    }

    private static DateTime? ReadLatestTradingDate(
        List<System.Text.Json.JsonElement> rows,
        CandleTimeframe timeframe,
        bool daily)
    {
        if (rows.Count == 0) return null;
        List<Candle> candles = MarketDataNormalizer.NormalizeHistory(
            ParseRows(rows, timeframe, daily),
            timeframe,
            SourceArrayDirection.ReverseWhole);
        return candles.Count == 0 ? null : candles[^1].TradingDate;
    }
}
