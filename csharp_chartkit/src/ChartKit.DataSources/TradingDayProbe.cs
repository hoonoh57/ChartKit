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

public readonly record struct TradingDayProbeSnapshot(
    DateTime TradingDate,
    TradingDayProbeState State,
    string[] SymbolsWithTodayData,
    string[] SymbolsWithoutTodayData,
    int SuccessfulQueries,
    int FailedQueries,
    DateTimeOffset CheckedAtUtc,
    string LastError)
{
    public static TradingDayProbeSnapshot Empty(DateTime tradingDate) =>
        new(
            tradingDate.Date,
            TradingDayProbeState.Unknown,
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
        var withTodayData = new List<string>(TradingDayBenchmarkSymbols.Length);
        var withoutTodayData = new List<string>(TradingDayBenchmarkSymbols.Length);
        var errors = new List<string>(TradingDayBenchmarkSymbols.Length);
        int successfulQueries = 0;
        int failedQueries = 0;

        foreach (string symbol in TradingDayBenchmarkSymbols)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                DateTime? latestDate = await GetLatestMinuteTradingDateAsync(
                    symbol,
                    cancellationToken).ConfigureAwait(false);
                successfulQueries++;
                if (latestDate.HasValue && latestDate.Value.Date == targetDate)
                    withTodayData.Add(symbol);
                else
                    withoutTodayData.Add(symbol);
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

        TradingDayProbeState state = withTodayData.Count > 0
            ? TradingDayProbeState.TradingDay
            : successfulQueries == TradingDayBenchmarkSymbols.Length
                ? TradingDayProbeState.NoTradingDay
                : TradingDayProbeState.Unknown;

        return new TradingDayProbeSnapshot(
            targetDate,
            state,
            withTodayData.ToArray(),
            withoutTodayData.ToArray(),
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
        if (page.Rows.Count == 0) return null;

        CandleTimeframe timeframe = CandleTimeframe.Minute(1);
        List<Candle> candles = MarketDataNormalizer.NormalizeHistory(
            ParseRows(page.Rows, timeframe, daily: false),
            timeframe,
            SourceArrayDirection.ReverseWhole);
        return candles.Count == 0 ? null : candles[^1].TradingDate;
    }
}
