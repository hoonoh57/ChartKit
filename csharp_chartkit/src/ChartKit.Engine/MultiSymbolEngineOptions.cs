namespace ChartKit.CSharp.Engine;

public sealed record MultiSymbolEngineOptions(
    int WorkerCount = 0,
    int QueueCapacityPerWorker = 8192,
    int CandleCapacity = 100_000,
    int SnapshotBars = 600,
    TimeSpan? SnapshotInterval = null)
{
    public int EffectiveWorkerCount =>
        WorkerCount > 0 ? WorkerCount : Math.Max(1, Environment.ProcessorCount / 2);

    public TimeSpan EffectiveSnapshotInterval =>
        SnapshotInterval ?? TimeSpan.FromMilliseconds(100);

    public void Validate()
    {
        if (QueueCapacityPerWorker <= 0)
            throw new ArgumentOutOfRangeException(nameof(QueueCapacityPerWorker));
        if (CandleCapacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(CandleCapacity));
        if (SnapshotBars <= 0)
            throw new ArgumentOutOfRangeException(nameof(SnapshotBars));
        if (EffectiveSnapshotInterval <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(SnapshotInterval));
    }
}
