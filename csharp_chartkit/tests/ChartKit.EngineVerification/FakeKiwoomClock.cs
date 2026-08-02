using ChartKit.CSharp.DataSources;

namespace ChartKit.CSharp.EngineVerification;

internal sealed class FakeKiwoomClock : IKiwoomClock
{
    private readonly object _sync = new();
    private DateTimeOffset _utcNow = new(2026, 7, 31, 0, 0, 0, TimeSpan.Zero);
    private long _timestamp;
    private readonly List<TimeSpan> _delays = new();

    public DateTimeOffset UtcNow
    {
        get { lock (_sync) return _utcNow; }
    }

    public long Timestamp
    {
        get { lock (_sync) return _timestamp; }
    }

    public long Frequency => 1000L;

    public IReadOnlyList<TimeSpan> Delays
    {
        get { lock (_sync) return _delays.ToArray(); }
    }

    public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (delay <= TimeSpan.Zero) return Task.CompletedTask;
        lock (_sync)
        {
            _delays.Add(delay);
            _utcNow = _utcNow.Add(delay);
            _timestamp += (long)Math.Ceiling(delay.TotalSeconds * Frequency);
        }
        return Task.CompletedTask;
    }

    public void Advance(TimeSpan value)
    {
        lock (_sync)
        {
            _utcNow = _utcNow.Add(value);
            _timestamp += (long)Math.Ceiling(value.TotalSeconds * Frequency);
        }
    }
}
