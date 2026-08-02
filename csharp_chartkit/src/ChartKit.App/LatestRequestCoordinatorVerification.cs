namespace ChartKit.CSharp.App;

internal static class LatestRequestCoordinatorVerification
{
    public static Task RunAsync()
    {
        using var applicationStop = new CancellationTokenSource();
        using var coordinator = new LatestRequestCoordinator();

        LatestRequestCoordinator.RequestLease first =
            coordinator.Begin(applicationStop.Token);
        if (!first.IsCurrent || first.ReplacedCurrent)
            throw new InvalidOperationException(
                "The first data request did not become current.");

        LatestRequestCoordinator.RequestLease second =
            coordinator.Begin(applicationStop.Token);
        if (!second.IsCurrent || !second.ReplacedCurrent)
            throw new InvalidOperationException(
                "The second data request did not replace the first request.");
        if (!first.Token.IsCancellationRequested)
            throw new InvalidOperationException(
                "The superseded data request was not cancelled.");

        bool staleRejected = false;
        try
        {
            first.ThrowIfSuperseded();
        }
        catch (OperationCanceledException)
        {
            staleRejected = true;
        }

        if (!staleRejected)
            throw new InvalidOperationException(
                "A superseded data request remained publishable.");

        first.Dispose();
        if (!second.IsCurrent)
            throw new InvalidOperationException(
                "Completing a stale request cleared the latest request.");

        coordinator.CancelCurrent();
        if (!second.Token.IsCancellationRequested)
            throw new InvalidOperationException(
                "CancelCurrent did not cancel the latest request.");
        second.Dispose();

        LatestRequestCoordinator.RequestLease third =
            coordinator.Begin(applicationStop.Token);
        applicationStop.Cancel();
        if (!third.Token.IsCancellationRequested)
            throw new InvalidOperationException(
                "Application cancellation did not reach the active data request.");
        third.Dispose();

        Console.WriteLine("csharp_app_data_request_latest_wins=PASS");
        Console.WriteLine("csharp_app_data_request_cancel=PASS");
        Console.WriteLine("csharp_app_data_request_application_stop=PASS");
        return Task.CompletedTask;
    }
}
