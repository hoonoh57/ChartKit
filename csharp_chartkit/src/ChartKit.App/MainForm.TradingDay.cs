using ChartKit.CSharp.DataSources;

namespace ChartKit.CSharp.App;

internal sealed partial class MainForm
{
    private readonly object _tradingDayProbeGate = new();
    private TradingDayProbeSnapshot _tradingDayProbe =
        TradingDayProbeSnapshot.Empty(DateTime.Today);
    private int _tradingDayProbeRunning;

    private void EnsureTradingDayProbeStarted()
    {
        DateTime today = DateTime.Today;
        lock (_tradingDayProbeGate)
        {
            if (_tradingDayProbe.TradingDate == today &&
                _tradingDayProbe.CheckedAtUtc != DateTimeOffset.MinValue)
                return;
        }

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
            TradingDayProbeSnapshot result =
                _source is ITradingDayProbeSource probeSource
                    ? await probeSource.ProbeTradingDayAsync(
                        today,
                        _stop.Token)
                    : TradingDayProbeSnapshot.Empty(today);
            lock (_tradingDayProbeGate)
                _tradingDayProbe = result;
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested)
        {
        }
        finally
        {
            Interlocked.Exchange(ref _tradingDayProbeRunning, 0);
        }
    }

    private TradingDayProbeSnapshot GetTradingDayProbeSnapshot()
    {
        lock (_tradingDayProbeGate)
            return _tradingDayProbe;
    }

    private static TradingDayProbeState GetEffectiveTradingDayState(
        RealtimeDiagnosticsSnapshot realtime,
        TradingDayProbeSnapshot probe)
    {
        if (realtime.AcceptedEvents > 0)
            return TradingDayProbeState.TradingDay;
        return probe.TradingDate == DateTime.Today
            ? probe.State
            : TradingDayProbeState.Unknown;
    }

    private string FormatTradingDaySummary(
        RealtimeDiagnosticsSnapshot realtime)
    {
        TradingDayProbeSnapshot probe = GetTradingDayProbeSnapshot();
        TradingDayProbeState state = GetEffectiveTradingDayState(realtime, probe);
        string method = probe.Method switch
        {
            TradingDayProbeMethod.TodayMinute => "대표 1분봉",
            TradingDayProbeMethod.HistoricalDaily => "대표 일봉",
            _ => "미확인"
        };
        string evidence = probe.SymbolsWithData.Length > 0
            ? string.Join(",", probe.SymbolsWithData)
            : probe.SymbolsWithoutData.Length > 0
                ? "005930/000660 없음"
                : "-";
        return state switch
        {
            TradingDayProbeState.TradingDay => $"거래일, {method}, {evidence}",
            TradingDayProbeState.NoTradingDay => $"휴장, {method}, {evidence}",
            _ => string.IsNullOrWhiteSpace(probe.LastError)
                ? $"확인중, {method}"
                : "확인불가, 대표종목 조회 오류"
        };
    }

    private string FormatTradingDayStatus(
        RealtimeDiagnosticsSnapshot realtime)
    {
        TradingDayProbeSnapshot probe = GetTradingDayProbeSnapshot();
        return GetEffectiveTradingDayState(realtime, probe) switch
        {
            TradingDayProbeState.TradingDay => "trading",
            TradingDayProbeState.NoTradingDay => "closed",
            _ => "unknown"
        };
    }
}
