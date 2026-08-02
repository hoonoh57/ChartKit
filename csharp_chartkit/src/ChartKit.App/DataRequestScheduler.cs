using System.Diagnostics;

namespace ChartKit.CSharp.App;

internal enum DataRequestOutcome
{
    Completed,
    Coalesced
}

internal sealed record DataRequestSchedulerSnapshot(
    bool IsRunning,
    string RunningDescription,
    long RunningElapsedMilliseconds,
    bool HasPending,
    string PendingDescription,
    long PendingWaitMilliseconds,
    long TotalEnqueued,
    long TotalCompleted,
    long TotalCoalesced,
    long LastCompletedMilliseconds,
    long MaxCompletedMilliseconds,
    long MaxPendingWaitMilliseconds);

internal sealed class DataRequestScheduler : IDisposable
{
    private sealed class Request(
        string description,
        Func<CancellationToken, Task> operation)
    {
        public string Description { get; } = description;
        public Func<CancellationToken, Task> Operation { get; } = operation;
        public long EnqueuedTimestamp { get; } = Stopwatch.GetTimestamp();
        public long StartedTimestamp { get; set; }
        public TaskCompletionSource<DataRequestOutcome> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private readonly object _sync = new();
    private readonly CancellationTokenSource _stop;
    private readonly CancellationToken _token;
    private Request? _running;
    private Request? _pending;
    private bool _runnerActive;
    private bool _disposed;
    private int _stopDisposed;
    private long _totalEnqueued;
    private long _totalCompleted;
    private long _totalCoalesced;
    private long _lastCompletedMilliseconds;
    private long _maxCompletedMilliseconds;
    private long _maxPendingWaitMilliseconds;

    public DataRequestScheduler(CancellationToken applicationToken)
    {
        _stop = CancellationTokenSource.CreateLinkedTokenSource(applicationToken);
        _token = _stop.Token;
    }

    public event Action? StateChanged;

    public Task<DataRequestOutcome> EnqueueAsync(
        string description,
        Func<CancellationToken, Task> operation)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        ArgumentNullException.ThrowIfNull(operation);
        _token.ThrowIfCancellationRequested();

        var request = new Request(description.Trim(), operation);
        Request? coalesced = null;
        bool startRunner = false;

        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _totalEnqueued++;

            if (!_runnerActive)
            {
                _runnerActive = true;
                _pending = request;
                startRunner = true;
            }
            else
            {
                coalesced = _pending;
                _pending = request;
                if (coalesced is not null)
                    _totalCoalesced++;
            }
        }

        coalesced?.Completion.TrySetResult(DataRequestOutcome.Coalesced);
        RaiseStateChanged();
        if (startRunner) _ = RunLoopAsync();
        return request.Completion.Task;
    }

    public DataRequestSchedulerSnapshot GetSnapshot()
    {
        lock (_sync)
        {
            long now = Stopwatch.GetTimestamp();
            long runningElapsed = _running is null
                ? 0
                : ElapsedMilliseconds(_running.StartedTimestamp, now);
            long pendingWait = _pending is null
                ? 0
                : ElapsedMilliseconds(_pending.EnqueuedTimestamp, now);

            return new DataRequestSchedulerSnapshot(
                _running is not null,
                _running?.Description ?? string.Empty,
                runningElapsed,
                _pending is not null,
                _pending?.Description ?? string.Empty,
                pendingWait,
                _totalEnqueued,
                _totalCompleted,
                _totalCoalesced,
                _lastCompletedMilliseconds,
                _maxCompletedMilliseconds,
                Math.Max(_maxPendingWaitMilliseconds, pendingWait));
        }
    }

    public void Dispose()
    {
        Request? pending;
        bool disposeStop;
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
            pending = _pending;
            _pending = null;
            disposeStop = !_runnerActive;
        }

        pending?.Completion.TrySetCanceled(_token);
        _stop.Cancel();
        RaiseStateChanged();
        if (disposeStop) DisposeStop();
    }

    private async Task RunLoopAsync()
    {
        while (true)
        {
            Request? request;
            Request? cancelledPending = null;
            bool stopRunner = false;

            lock (_sync)
            {
                if (_disposed || _token.IsCancellationRequested)
                {
                    cancelledPending = _pending;
                    _pending = null;
                    _running = null;
                    _runnerActive = false;
                    stopRunner = true;
                    request = null;
                }
                else
                {
                    request = _pending;
                    _pending = null;
                    if (request is null)
                    {
                        _running = null;
                        _runnerActive = false;
                        stopRunner = true;
                    }
                    else
                    {
                        request.StartedTimestamp = Stopwatch.GetTimestamp();
                        long waitMilliseconds = ElapsedMilliseconds(
                            request.EnqueuedTimestamp,
                            request.StartedTimestamp);
                        _maxPendingWaitMilliseconds = Math.Max(
                            _maxPendingWaitMilliseconds,
                            waitMilliseconds);
                        _running = request;
                    }
                }
            }

            if (cancelledPending is not null)
                cancelledPending.Completion.TrySetCanceled(_token);
            if (stopRunner)
            {
                RaiseStateChanged();
                if (_disposed) DisposeStop();
                return;
            }

            RaiseStateChanged();
            try
            {
                await request!.Operation(_token);
                request.Completion.TrySetResult(DataRequestOutcome.Completed);
            }
            catch (OperationCanceledException) when (_token.IsCancellationRequested)
            {
                request.Completion.TrySetCanceled(_token);
            }
            catch (Exception exception)
            {
                request.Completion.TrySetException(exception);
            }
            finally
            {
                long completedTimestamp = Stopwatch.GetTimestamp();
                long duration = ElapsedMilliseconds(
                    request.StartedTimestamp,
                    completedTimestamp);
                lock (_sync)
                {
                    if (ReferenceEquals(_running, request))
                        _running = null;
                    _totalCompleted++;
                    _lastCompletedMilliseconds = duration;
                    _maxCompletedMilliseconds = Math.Max(
                        _maxCompletedMilliseconds,
                        duration);
                }
                RaiseStateChanged();
            }
        }
    }

    private void DisposeStop()
    {
        if (Interlocked.Exchange(ref _stopDisposed, 1) == 0)
            _stop.Dispose();
    }

    private static long ElapsedMilliseconds(long start, long end) =>
        start <= 0 || end <= start
            ? 0
            : (long)((end - start) * 1000d / Stopwatch.Frequency);

    private void RaiseStateChanged()
    {
        Action? handler = StateChanged;
        handler?.Invoke();
    }
}
