using System.Collections.Concurrent;
using ChartKit.CSharp.Contracts;

namespace ChartKit.CSharp.DataSources;

public sealed partial class KiwoomRestDataSource :
    IMarketDataSource,
    IInstrumentMetadataSource
{
    private readonly KiwoomApiSession _session;
    private readonly bool _ownsSession;
    private readonly ConcurrentDictionary<string, RealtimeSeed> _realtimeSeeds =
        new(StringComparer.Ordinal);
    private int _disposed;

    public KiwoomRestDataSource(
        KiwoomOptions? options = null,
        KiwoomApiSession? session = null)
    {
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

    private void SaveSeed(string symbol, Candle candle, int tickCount) =>
        _realtimeSeeds[symbol] = new RealtimeSeed(candle, Math.Max(0, tickCount));

    private bool TryGetSeed(string symbol, out RealtimeSeed seed) =>
        _realtimeSeeds.TryGetValue(symbol, out seed);

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
