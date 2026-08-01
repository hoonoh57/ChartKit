using ChartKit.CSharp.DataSources;

namespace ChartKit.CSharp.App;

internal sealed partial class MainForm
{
    private TradingDayProbeSnapshot _tradingDayProbe =
        TradingDayProbeSnapshot.Empty(DateTime.Today);
    private int _tradingDayProbeRunning;

    private void EnsureTradingDayProbeStarted()
    {
        DateTime today = DateTime.Today;
        if (_tradingDayProbe.TradingDate == today &&
            _tradingDayProbe.CheckedAtUtc != DateTimeOffset.MinValue)
            return;
        if (Interlocked.CompareExchange(
                ref _tradingDayProbeRunning,
                1,
                0) != 0)
            return;

        _ = RunTradingDayProbeAsync(today);
    }

    private async Task RunTradingDayProbeAsync(DateTime today)
    {
        try
        {
            if (_source is not ITradingDayProbeSource probeSource)
            {
                _tradingDayProbe = TradingDayProbeSnapshot.Empty(today);
                return;
            }

            _tradingDayProbe = await probeSource.ProbeTradingDayAsync(
                today,
                _stop.Token);
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested)
        {
        }
        finally
        {
            Interlocked.Exchange(ref _tradingDayProbeRunning, 0);
        }
    }

    private TradingDayProbeState GetEffectiveTradingDayState(
        RealtimeDiagnosticsSnapshot realtime)
    {
        if (realtime.AcceptedEvents > 0)
            return TradingDayProbeState.TradingDay;
        return _tradingDayProbe.TradingDate == DateTime.Today
            ? _tradingDayProbe.State
            : TradingDayProbeState.Unknown;
    }

    private string FormatTradingDaySummary(
        RealtimeDiagnosticsSnapshot realtime)
    {
        TradingDayProbeState state = GetEffectiveTradingDayState(realtime);
        string method = _tradingDayProbe.Method switch
        {
            TradingDayProbeMethod.TodayMinute => "대표 1분봉",
            TradingDayProbeMethod.HistoricalDaily => "대표 일봉",
            _ => "미확인"
        };
        string evidence = _tradingDayProbe.SymbolsWithData.Length > 0
            ? string.Join(",", _tradingDayProbe.SymbolsWithData)
            : _tradingDayProbe.SymbolsWithoutData.Length > 0
                ? "005930/000660 없음"
                : "-";
        return state switch
        {
            TradingDayProbeState.TradingDay => $"거래일, {method}, {evidence}",
            TradingDayProbeState.NoTradingDay => $"휴장, {method}, {evidence}",
            _ => string.IsNullOrWhiteSpace(_tradingDayProbe.LastError)
                ? $"확인중, {method}"
                : "확인불가, 대표종목 조회 오류"
        };
    }

    private string FormatTradingDayStatus(
        RealtimeDiagnosticsSnapshot realtime) =>
        GetEffectiveTradingDayState(realtime) switch
        {
            TradingDayProbeState.TradingDay => "trading",
            TradingDayProbeState.NoTradingDay => "closed",
            _ => "unknown"
        };
}
