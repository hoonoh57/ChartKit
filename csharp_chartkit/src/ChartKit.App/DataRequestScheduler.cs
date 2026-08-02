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
    private Request? _running;
    private Request? _pending;
    private bool _runnerActive;
    private bool _disposed;
    private long _totalEnqueued;
    private long _totalCompleted;
    private long _totalCoalesced;
    private long _lastCompletedMilliseconds;
    private long _maxCompletedMilliseconds;
    private long _maxPendingWaitMilliseconds;

    public DataRequestScheduler(CancellationToken applicationToken)
    {
        _stop = CancellationTokenSource.CreateLinkedTokenSource(applicationToken);
    }

    public event Action? StateChanged;

    public Task<DataRequestOutcome> EnqueueAsync(
        string description,
        Func<CancellationToken, Task> operation)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        ArgumentNullException.ThrowIfNull(operation);

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
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
            pending = _pending;
            _pending = null;
        }

        pending?.Completion.TrySetCanceled(_stop.Token);
        _stop.Cancel();
        RaiseStateChanged();
        _stop.Dispose();
    }

    private async Task RunLoopAsync()
    {
        while (true)
        {
            Request? request;
            lock (_sync)
            {
                request = _pending;
                _pending = null;
                if (request is null)
                {
                    _running = null;
                    _runnerActive = false;
                    RaiseStateChangedAfterUnlock();
                    return;
                }

                request.StartedTimestamp = Stopwatch.GetTimestamp();
                long waitMilliseconds = ElapsedMilliseconds(
                    request.EnqueuedTimestamp,
                    request.StartedTimestamp);
                _maxPendingWaitMilliseconds = Math.Max(
                    _maxPendingWaitMilliseconds,
                    waitMilliseconds);
                _running = request;
            }

            RaiseStateChanged();
            try
            {
                await request.Operation(_stop.Token);
                request.Completion.TrySetResult(DataRequestOutcome.Completed);
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested)
            {
                request.Completion.TrySetCanceled(_stop.Token);
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

    private static long ElapsedMilliseconds(long start, long end) =>
        start <= 0 || end <= start
            ? 0
            : (long)((end - start) * 1000d / Stopwatch.Frequency);

    private void RaiseStateChanged()
    {
        Action? handler = StateChanged;
        handler?.Invoke();
    }

    private void RaiseStateChangedAfterUnlock() =>
        ThreadPool.QueueUserWorkItem(_ => RaiseStateChanged());
}
