using ChartKit.CSharp.Contracts;

namespace ChartKit.CSharp.DataSources;

public enum RealtimeConnectionState
{
    Idle = 0,
    Connecting = 1,
    Connected = 2,
    LoggedIn = 3,
    Registered = 4,
    Receiving = 5,
    Reconnecting = 6,
    Faulted = 7,
    Stopped = 8
}

public enum RealtimeBoundaryState
{
    None = 0,
    AwaitingFirstEvent = 1,
    SeedUpdated = 2,
    SeedAppended = 3,
    UnseededAppended = 4,
    RejectedStaleBeforeFirstEvent = 5
}

public readonly record struct RealtimeDiagnosticsSnapshot(
    string Symbol,
    CandleTimeframe Timeframe,
    RealtimeConnectionState ConnectionState,
    RealtimeBoundaryState BoundaryState,
    DateTime? SeedOpenTime,
    DateTime? SeedCloseTime,
    int SeedTickCount,
    DateTime? FirstRealtimeTime,
    MarketEventKind? FirstEventKind,
    long AcceptedEvents,
    long UpdateEvents,
    long AppendEvents,
    long RejectedStaleEvents,
    int ConnectionAttempts,
    int RegistrationCount,
    string LastError)
{
    public static RealtimeDiagnosticsSnapshot Empty(string symbol) =>
        new(
            symbol,
            default,
            RealtimeConnectionState.Idle,
            RealtimeBoundaryState.None,
            null,
            null,
            0,
            null,
            null,
            0,
            0,
            0,
            0,
            0,
            0,
            string.Empty);
}

public interface IRealtimeDiagnosticsSource
{
    RealtimeDiagnosticsSnapshot GetRealtimeDiagnostics(string symbol);
}

internal sealed class RealtimeDiagnosticsState
{
    private readonly object _gate = new();
    private string _symbol = string.Empty;
    private CandleTimeframe _timeframe;
    private RealtimeConnectionState _connectionState;
    private RealtimeBoundaryState _boundaryState;
    private DateTime? _seedOpenTime;
    private DateTime? _seedCloseTime;
    private int _seedTickCount;
    private DateTime? _firstRealtimeTime;
    private MarketEventKind? _firstEventKind;
    private long _acceptedEvents;
    private long _updateEvents;
    private long _appendEvents;
    private long _rejectedStaleEvents;
    private int _connectionAttempts;
    private int _registrationCount;
    private string _lastError = string.Empty;

    public void Reset(
        string symbol,
        CandleTimeframe timeframe,
        Candle? seed,
        int seedTickCount)
    {
        lock (_gate)
        {
            _symbol = symbol;
            _timeframe = timeframe;
            _connectionState = RealtimeConnectionState.Idle;
            _boundaryState = seed.HasValue
                ? RealtimeBoundaryState.AwaitingFirstEvent
                : RealtimeBoundaryState.None;
            _seedOpenTime = seed?.OpenTime;
            _seedCloseTime = seed?.CloseTime;
            _seedTickCount = Math.Max(0, seedTickCount);
            _firstRealtimeTime = null;
            _firstEventKind = null;
            _acceptedEvents = 0;
            _updateEvents = 0;
            _appendEvents = 0;
            _rejectedStaleEvents = 0;
            _connectionAttempts = 0;
            _registrationCount = 0;
            _lastError = string.Empty;
        }
    }

    public void SetConnectionState(
        RealtimeConnectionState state,
        string? error = null)
    {
        lock (_gate)
        {
            _connectionState = state;
            if (!string.IsNullOrWhiteSpace(error))
                _lastError = error.Trim();
        }
    }

    public void RecordConnectionAttempt(bool reconnecting)
    {
        lock (_gate)
        {
            _connectionAttempts++;
            _connectionState = reconnecting
                ? RealtimeConnectionState.Reconnecting
                : RealtimeConnectionState.Connecting;
        }
    }

    public void RecordRegistration()
    {
        lock (_gate)
        {
            _registrationCount++;
            _connectionState = RealtimeConnectionState.Registered;
        }
    }

    public void RecordAccepted(
        DateTime tradeTime,
        MarketEventKind kind,
        bool hadSeed)
    {
        lock (_gate)
        {
            _connectionState = RealtimeConnectionState.Receiving;
            _acceptedEvents++;
            if (kind == MarketEventKind.Update)
                _updateEvents++;
            else
                _appendEvents++;

            if (_firstRealtimeTime.HasValue) return;
            _firstRealtimeTime = tradeTime;
            _firstEventKind = kind;
            _boundaryState = hadSeed
                ? kind == MarketEventKind.Update
                    ? RealtimeBoundaryState.SeedUpdated
                    : RealtimeBoundaryState.SeedAppended
                : RealtimeBoundaryState.UnseededAppended;
        }
    }

    public void RecordRejectedStale(DateTime tradeTime)
    {
        lock (_gate)
        {
            _rejectedStaleEvents++;
            if (_firstRealtimeTime.HasValue) return;
            _firstRealtimeTime = tradeTime;
            _boundaryState = RealtimeBoundaryState.RejectedStaleBeforeFirstEvent;
        }
    }

    public RealtimeDiagnosticsSnapshot Snapshot()
    {
        lock (_gate)
        {
            return new RealtimeDiagnosticsSnapshot(
                _symbol,
                _timeframe,
                _connectionState,
                _boundaryState,
                _seedOpenTime,
                _seedCloseTime,
                _seedTickCount,
                _firstRealtimeTime,
                _firstEventKind,
                _acceptedEvents,
                _updateEvents,
                _appendEvents,
                _rejectedStaleEvents,
                _connectionAttempts,
                _registrationCount,
                _lastError);
        }
    }
}
