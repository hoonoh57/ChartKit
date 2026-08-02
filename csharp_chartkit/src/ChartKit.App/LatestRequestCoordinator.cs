namespace ChartKit.CSharp.App;

internal sealed class LatestRequestCoordinator : IDisposable
{
    private readonly object _sync = new();
    private CancellationTokenSource? _current;
    private long _generation;
    private bool _disposed;

    public RequestLease Begin(CancellationToken applicationToken)
    {
        var next = CancellationTokenSource.CreateLinkedTokenSource(applicationToken);
        CancellationTokenSource? previous;
        long generation;

        lock (_sync)
        {
            if (_disposed)
            {
                next.Dispose();
                throw new ObjectDisposedException(nameof(LatestRequestCoordinator));
            }

            previous = _current;
            _current = next;
            generation = checked(++_generation);
        }

        TryCancel(previous);
        return new RequestLease(
            this,
            next,
            generation,
            replacedCurrent: previous is not null);
    }

    public void CancelCurrent()
    {
        CancellationTokenSource? current;
        lock (_sync)
            current = _current;
        TryCancel(current);
    }

    public void Dispose()
    {
        CancellationTokenSource? current;
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
            current = _current;
            _current = null;
        }

        TryCancel(current);
    }

    private bool IsCurrent(
        long generation,
        CancellationTokenSource source)
    {
        lock (_sync)
        {
            return !_disposed &&
                   generation == _generation &&
                   ReferenceEquals(_current, source);
        }
    }

    private void Complete(
        long generation,
        CancellationTokenSource source)
    {
        lock (_sync)
        {
            if (generation == _generation &&
                ReferenceEquals(_current, source))
                _current = null;
        }
    }

    private static void TryCancel(CancellationTokenSource? source)
    {
        if (source is null) return;
        try
        {
            source.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    internal sealed class RequestLease : IDisposable
    {
        private readonly LatestRequestCoordinator _owner;
        private readonly CancellationTokenSource _source;
        private int _disposed;

        internal RequestLease(
            LatestRequestCoordinator owner,
            CancellationTokenSource source,
            long generation,
            bool replacedCurrent)
        {
            _owner = owner;
            _source = source;
            Generation = generation;
            ReplacedCurrent = replacedCurrent;
        }

        public long Generation { get; }

        public bool ReplacedCurrent { get; }

        public CancellationToken Token => _source.Token;

        public bool IsCurrent =>
            Volatile.Read(ref _disposed) == 0 &&
            _owner.IsCurrent(Generation, _source);

        public void ThrowIfSuperseded()
        {
            Token.ThrowIfCancellationRequested();
            if (!IsCurrent)
            {
                throw new OperationCanceledException(
                    "The data request was superseded by a newer request.",
                    Token);
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            _owner.Complete(Generation, _source);
            _source.Dispose();
        }
    }
}
