using System.Diagnostics;

namespace ChartKit.CSharp.DataSources;

public interface IKiwoomClock
{
    DateTimeOffset UtcNow { get; }
    long Timestamp { get; }
    long Frequency { get; }
    Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken);
}

public sealed class SystemKiwoomClock : IKiwoomClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
    public long Timestamp => Stopwatch.GetTimestamp();
    public long Frequency => Stopwatch.Frequency;

    public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) =>
        delay <= TimeSpan.Zero
            ? Task.CompletedTask
            : Task.Delay(delay, cancellationToken);
}
