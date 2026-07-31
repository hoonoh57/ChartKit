using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading.Channels;
using ChartKit.CSharp.Contracts;

namespace ChartKit.CSharp.Engine;

public sealed class MultiSymbolEngine : IAsyncDisposable
{
    private readonly MultiSymbolEngineOptions _options;
    private readonly Channel<EngineMessage>[] _channels;
    private readonly Task[] _workers;
    private readonly CancellationTokenSource _stop = new();
    private readonly ConcurrentDictionary<string, SymbolSnapshot> _snapshots =
        new(StringComparer.Ordinal);

    private long _acceptedEvents;
    private long _processedEvents;
    private long _publishedSnapshots;
    private long _processingErrors;
    private long _maxQueueDepth;
    private long _lastLatencyMicroseconds;
    private long _pendingMessages;
    private int _disposed;

    public MultiSymbolEngine(MultiSymbolEngineOptions? options = null)
    {
        _options = options ?? new MultiSymbolEngineOptions();
        _options.Validate();

        _channels = new Channel<EngineMessage>[_options.EffectiveWorkerCount];
        _workers = new Task[_channels.Length];
        for (int index = 0; index < _channels.Length; index++)
        {
            _channels[index] = Channel.CreateBounded<EngineMessage>(
                new BoundedChannelOptions(_options.QueueCapacityPerWorker)
                {
                    FullMode = BoundedChannelFullMode.Wait,
                    SingleReader = true,
                    SingleWriter = false,
                    AllowSynchronousContinuations = false
                });
            int shardIndex = index;
            _workers[index] = Task.Run(() => RunShardAsync(shardIndex, _stop.Token));
        }
    }

    public int WorkerCount => _channels.Length;

    public async ValueTask PublishAsync(
        CandleEvent value,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (string.IsNullOrWhiteSpace(value.Symbol))
            throw new ArgumentException("Symbol is required.", nameof(value));

        Interlocked.Increment(ref _acceptedEvents);
        await WriteAsync(value.Symbol, new EventMessage(value), cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task LoadHistoryAsync(
        string symbol,
        IReadOnlyList<Candle> candles,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (string.IsNullOrWhiteSpace(symbol))
            throw new ArgumentException("Symbol is required.", nameof(symbol));
        ArgumentNullException.ThrowIfNull(candles);

        var completion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        await WriteAsync(
            symbol,
            new HistoryMessage(symbol, candles, completion),
            cancellationToken).ConfigureAwait(false);
        await completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public bool TryGetSnapshot(string symbol, out SymbolSnapshot? snapshot) =>
        _snapshots.TryGetValue(symbol, out snapshot);

    public EngineMetrics GetMetrics() => new(
        Interlocked.Read(ref _acceptedEvents),
        Interlocked.Read(ref _processedEvents),
        Interlocked.Read(ref _publishedSnapshots),
        Interlocked.Read(ref _processingErrors),
        Interlocked.Read(ref _maxQueueDepth),
        Interlocked.Read(ref _lastLatencyMicroseconds));

    private async ValueTask WriteAsync(
        string symbol,
        EngineMessage message,
        CancellationToken cancellationToken)
    {
        int shard = GetShard(symbol);
        long depth = Interlocked.Increment(ref _pendingMessages);
        UpdateMaximum(ref _maxQueueDepth, depth);
        try
        {
            await _channels[shard].Writer.WriteAsync(message, cancellationToken)
                .ConfigureAwait(false);
        }
        catch
        {
            Interlocked.Decrement(ref _pendingMessages);
            throw;
        }
    }

    private int GetShard(string symbol) =>
        (StringComparer.Ordinal.GetHashCode(symbol) & int.MaxValue) % _channels.Length;

    private async Task RunShardAsync(int shardIndex, CancellationToken cancellationToken)
    {
        var runtimes = new Dictionary<string, SymbolRuntime>(StringComparer.Ordinal);
        ChannelReader<EngineMessage> reader = _channels[shardIndex].Reader;

        try
        {
            await foreach (EngineMessage message in reader.ReadAllAsync(cancellationToken)
                               .ConfigureAwait(false))
            {
                Interlocked.Decrement(ref _pendingMessages);
                try
                {
                    ProcessMessage(runtimes, message);
                }
                catch
                {
                    Interlocked.Increment(ref _processingErrors);
                    if (message is HistoryMessage history)
                        history.Completion.TrySetException(
                            new InvalidOperationException("History load failed."));
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private void ProcessMessage(
        Dictionary<string, SymbolRuntime> runtimes,
        EngineMessage message)
    {
        SymbolRuntime runtime = GetRuntime(runtimes, message.Symbol);
        switch (message)
        {
            case EventMessage eventMessage:
                runtime.Apply(eventMessage.Value);
                Interlocked.Increment(ref _processedEvents);
                UpdateLatency(eventMessage.Value.EnqueuedTimestamp);
                PublishSnapshotIfDue(runtime, force: false);
                break;

            case HistoryMessage historyMessage:
                runtime.LoadHistory(historyMessage.Candles);
                PublishSnapshotIfDue(runtime, force: true);
                historyMessage.Completion.TrySetResult();
                break;
        }
    }

    private SymbolRuntime GetRuntime(
        Dictionary<string, SymbolRuntime> runtimes,
        string symbol)
    {
        if (runtimes.TryGetValue(symbol, out SymbolRuntime? runtime)) return runtime;

        runtime = new SymbolRuntime(
            symbol,
            _options.CandleCapacity,
            _options.SnapshotBars,
            _options.EffectiveSnapshotInterval);
        runtimes.Add(symbol, runtime);
        return runtime;
    }

    private void PublishSnapshotIfDue(SymbolRuntime runtime, bool force)
    {
        if (!runtime.TryCreateSnapshot(force, out SymbolSnapshot? snapshot)) return;
        _snapshots[runtime.Symbol] = snapshot!;
        Interlocked.Increment(ref _publishedSnapshots);
    }

    private void UpdateLatency(long enqueuedTimestamp)
    {
        if (enqueuedTimestamp <= 0) return;
        long elapsed = Stopwatch.GetTimestamp() - enqueuedTimestamp;
        long microseconds = elapsed * 1_000_000L / Stopwatch.Frequency;
        Interlocked.Exchange(ref _lastLatencyMicroseconds, Math.Max(0, microseconds));
    }

    private static void UpdateMaximum(ref long target, long candidate)
    {
        long current;
        do
        {
            current = Volatile.Read(ref target);
            if (candidate <= current) return;
        } while (Interlocked.CompareExchange(ref target, candidate, current) != current);
    }

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _disposed) != 0)
            throw new ObjectDisposedException(nameof(MultiSymbolEngine));
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

        foreach (Channel<EngineMessage> channel in _channels)
            channel.Writer.TryComplete();
        _stop.Cancel();
        try
        {
            await Task.WhenAll(_workers).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        _stop.Dispose();
    }
}
