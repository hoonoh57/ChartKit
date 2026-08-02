using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using ChartKit.CSharp.Contracts;

namespace ChartKit.CSharp.DataSources;

public sealed partial class KiwoomRestDataSource :
    IMarketDataSource,
    IInstrumentMetadataSource,
    IRealtimeDiagnosticsSource,
    ITradingDayProbeSource
{
    private readonly KiwoomApiSession _session;
    private readonly bool _ownsSession;
    private readonly Func<IKiwoomWebSocket> _webSocketFactory;
    private readonly ConcurrentDictionary<string, RealtimeSeed> _realtimeSeeds =
        new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, RealtimeDiagnosticsState>
        _realtimeDiagnostics = new(StringComparer.Ordinal);
    private int _disposed;

    public KiwoomRestDataSource(
        KiwoomOptions? options = null,
        KiwoomApiSession? session = null)
        : this(
            options,
            session,
            static () => new ClientKiwoomWebSocket())
    {
    }

    internal KiwoomRestDataSource(
        KiwoomOptions? options,
        KiwoomApiSession? session,
        Func<IKiwoomWebSocket> webSocketFactory)
    {
        _webSocketFactory = webSocketFactory ??
            throw new ArgumentNullException(nameof(webSocketFactory));
        if (session is not null)
        {
            _session = session;
            _ownsSession = false;
        }
        else
        {
            _session = new KiwoomApiSession(options ?? KiwoomOptions.FromEnvironment());
            _ownsSession = true;
        }
    }

    public string Name => _session.Options.IsMock
        ? "Kiwoom CSharp REST mock"
        : "Kiwoom CSharp REST real";

    public RealtimeDiagnosticsSnapshot GetRealtimeDiagnostics(string symbol)
    {
        string normalized = string.IsNullOrWhiteSpace(symbol)
            ? string.Empty
            : symbol.Trim();
        return normalized.Length > 0 &&
               _realtimeDiagnostics.TryGetValue(
                   normalized,
                   out RealtimeDiagnosticsState? state)
            ? state.Snapshot()
            : RealtimeDiagnosticsSnapshot.Empty(normalized);
    }

    private void SaveSeed(string symbol, Candle candle, int tickCount) =>
        _realtimeSeeds[symbol] = new RealtimeSeed(candle, Math.Max(0, tickCount));

    private bool TryGetSeed(string symbol, out RealtimeSeed seed) =>
        _realtimeSeeds.TryGetValue(symbol, out seed);

    private RealtimeDiagnosticsState ResetRealtimeDiagnostics(
        string symbol,
        CandleTimeframe timeframe,
        Candle? seed,
        int seedTickCount)
    {
        RealtimeDiagnosticsState state = _realtimeDiagnostics.GetOrAdd(
            symbol,
            static _ => new RealtimeDiagnosticsState());
        state.Reset(symbol, timeframe, seed, seedTickCount);
        return state;
    }

    private bool TryGetRealtimeDiagnosticsState(
        string symbol,
        [NotNullWhen(true)] out RealtimeDiagnosticsState? state) =>
        _realtimeDiagnostics.TryGetValue(symbol, out state);

    private void SetRealtimeConnectionState(
        IReadOnlyList<string> symbols,
        RealtimeConnectionState state,
        string? error = null)
    {
        foreach (string symbol in symbols)
        {
            if (TryGetRealtimeDiagnosticsState(symbol, out RealtimeDiagnosticsState? diagnostics))
                diagnostics.SetConnectionState(state, error);
        }
    }

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _disposed) != 0)
            throw new ObjectDisposedException(nameof(KiwoomRestDataSource));
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        if (_ownsSession) await _session.DisposeAsync().ConfigureAwait(false);
    }

    private readonly record struct RealtimeSeed(Candle Candle, int TickCount);
}
