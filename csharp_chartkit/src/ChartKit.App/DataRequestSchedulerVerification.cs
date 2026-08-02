namespace ChartKit.CSharp.App;

internal static class DataRequestSchedulerVerification
{
    public static async Task RunAsync()
    {
        using var applicationStop = new CancellationTokenSource();
        using var scheduler = new DataRequestScheduler(applicationStop.Token);
        var firstStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        int concurrent = 0;
        int maxConcurrent = 0;
        bool secondExecuted = false;
        bool thirdExecuted = false;

        Task<DataRequestOutcome> first = scheduler.EnqueueAsync(
            "first",
            async cancellationToken =>
            {
                int current = Interlocked.Increment(ref concurrent);
                maxConcurrent = Math.Max(maxConcurrent, current);
                firstStarted.TrySetResult();
                try
                {
                    await releaseFirst.Task.WaitAsync(cancellationToken);
                }
                finally
                {
                    Interlocked.Decrement(ref concurrent);
                }
            });

        await firstStarted.Task;
        Task<DataRequestOutcome> second = scheduler.EnqueueAsync(
            "second",
            _ =>
            {
                secondExecuted = true;
                return Task.CompletedTask;
            });
        Task<DataRequestOutcome> third = scheduler.EnqueueAsync(
            "third",
            _ =>
            {
                int current = Interlocked.Increment(ref concurrent);
                maxConcurrent = Math.Max(maxConcurrent, current);
                thirdExecuted = true;
                Interlocked.Decrement(ref concurrent);
                return Task.CompletedTask;
            });

        DataRequestSchedulerSnapshot waiting = scheduler.GetSnapshot();
        if (!waiting.IsRunning ||
            waiting.RunningDescription != "first" ||
            !waiting.HasPending ||
            waiting.PendingDescription != "third")
            throw new InvalidOperationException(
                "The scheduler did not preserve the running request and latest waiting request.");
        if (await second != DataRequestOutcome.Coalesced || secondExecuted)
            throw new InvalidOperationException(
                "The replaced waiting request was not coalesced.");

        releaseFirst.TrySetResult();
        if (await first != DataRequestOutcome.Completed)
            throw new InvalidOperationException(
                "The running request did not complete normally.");
        if (await third != DataRequestOutcome.Completed || !thirdExecuted)
            throw new InvalidOperationException(
                "The latest waiting request did not run after the current request.");
        if (maxConcurrent != 1)
            throw new InvalidOperationException(
                "Data requests executed concurrently.");

        DataRequestSchedulerSnapshot completed = scheduler.GetSnapshot();
        if (completed.TotalEnqueued != 3 ||
            completed.TotalCompleted != 2 ||
            completed.TotalCoalesced != 1 ||
            completed.MaxPendingWaitMilliseconds < 0)
            throw new InvalidOperationException(
                "Data request wait metrics are inconsistent.");

        using var stop = new CancellationTokenSource();
        using var stopScheduler = new DataRequestScheduler(stop.Token);
        Task<DataRequestOutcome> stopped = stopScheduler.EnqueueAsync(
            "stop",
            cancellationToken => Task.Delay(Timeout.Infinite, cancellationToken));
        stop.Cancel();
        bool cancelled = false;
        try
        {
            await stopped;
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
        }
        if (!cancelled)
            throw new InvalidOperationException(
                "Application stop did not cancel the running data request.");

        Console.WriteLine("csharp_app_data_request_running_completes=PASS");
        Console.WriteLine("csharp_app_data_request_pending_coalesced=PASS");
        Console.WriteLine("csharp_app_data_request_serial=PASS");
        Console.WriteLine("csharp_app_data_request_wait_metrics=PASS");
        Console.WriteLine("csharp_app_data_request_application_stop=PASS");
    }
}
