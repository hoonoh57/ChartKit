using ChartKit.CSharp.Modules.Abstractions;

namespace ChartKit.CSharp.ModuleHost;

public sealed record ChartModuleDataUpdateResult(
    long DataVersion,
    int EligibleModules,
    int UpdatedModules,
    int FaultedModules);

public sealed partial class ChartModuleHost
{
    public ChartModuleDataUpdateResult ApplyPrimarySeries(
        ChartPrimarySeriesSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        lock (_gate)
        {
            int eligible = 0;
            int updated = 0;
            int faulted = 0;

            foreach (HostedModule entry in _instances.Values)
            {
                if (!entry.Profile.IsEnabled ||
                    !entry.IsActive ||
                    entry.LastError is not null ||
                    entry.Module is not IChartDataModule dataModule)
                {
                    continue;
                }

                eligible++;
                try
                {
                    dataModule.ApplyPrimarySeries(snapshot);
                    updated++;
                }
                catch (Exception exception)
                {
                    MarkFault(entry, exception, attemptDeactivate: true);
                    faulted++;
                }
            }

            return new ChartModuleDataUpdateResult(
                snapshot.DataVersion,
                eligible,
                updated,
                faulted);
        }
    }
}
