using System.Text.Json.Nodes;
using ChartKit.CSharp.Modules.Abstractions;

namespace ChartKit.CSharp.ModuleHost;

public sealed record ChartHostedCommandDescriptor(
    string ModuleId,
    string InstanceId,
    string ModuleDisplayName,
    string ModuleCategory,
    bool IsEnabled,
    bool IsActive,
    bool IsFaulted,
    ChartCommandDescriptor Descriptor);

public sealed record ChartHostedPropertyDescriptor(
    string ModuleId,
    string InstanceId,
    string ModuleDisplayName,
    string ModuleCategory,
    bool IsEnabled,
    bool IsActive,
    bool IsFaulted,
    ChartPropertyDescriptor Descriptor);

public sealed partial class ChartModuleHost
{
    public bool TryGetSnapshot(
        string instanceId,
        out ChartModuleRuntimeSnapshot? snapshot)
    {
        string normalizedInstanceId = RequireText(
            instanceId,
            nameof(instanceId));

        lock (_gate)
        {
            if (_instances.TryGetValue(
                    normalizedInstanceId,
                    out HostedModule? entry))
            {
                snapshot = CreateSnapshot(entry);
                return true;
            }

            snapshot = null;
            return false;
        }
    }

    public IReadOnlyList<ChartHostedCommandDescriptor>
        CollectCommandDescriptors()
    {
        lock (_gate)
        {
            var result = new List<ChartHostedCommandDescriptor>();

            foreach (HostedModule entry in OrderedEntries())
            {
                if (entry.LastError is not null ||
                    entry.Module is not IChartCommandProvider provider)
                {
                    continue;
                }

                var writer = new OwnedCommandWriter();
                try
                {
                    EnsureCapability(
                        entry,
                        ChartModuleCapabilities.Commands,
                        nameof(IChartCommandProvider));
                    provider.DescribeCommands(writer);
                }
                catch (Exception exception)
                {
                    MarkFault(entry, exception, attemptDeactivate: true);
                    continue;
                }

                foreach (ChartCommandDescriptor descriptor in writer.Descriptors)
                {
                    result.Add(new ChartHostedCommandDescriptor(
                        entry.Profile.ModuleId,
                        entry.Profile.InstanceId,
                        entry.Module.ModuleDefinition.DisplayName,
                        entry.Module.ModuleDefinition.Category,
                        entry.Profile.IsEnabled,
                        entry.IsActive,
                        entry.LastError is not null,
                        CloneCommandDescriptor(descriptor)));
                }
            }

            return result.ToArray();
        }
    }

    public IReadOnlyList<ChartHostedPropertyDescriptor>
        CollectPropertyDescriptors(string instanceId)
    {
        string normalizedInstanceId = RequireText(
            instanceId,
            nameof(instanceId));

        lock (_gate)
        {
            if (!_instances.TryGetValue(
                    normalizedInstanceId,
                    out HostedModule? entry))
            {
                throw new KeyNotFoundException(
                    $"Chart module instance is not hosted: {normalizedInstanceId}");
            }

            if (entry.LastError is not null ||
                entry.Module is not IChartPropertyProvider provider)
            {
                return Array.Empty<ChartHostedPropertyDescriptor>();
            }

            var writer = new OwnedPropertyWriter();
            try
            {
                EnsureCapability(
                    entry,
                    ChartModuleCapabilities.Properties,
                    nameof(IChartPropertyProvider));
                provider.DescribeProperties(writer);
            }
            catch (Exception exception)
            {
                MarkFault(entry, exception, attemptDeactivate: true);
                return Array.Empty<ChartHostedPropertyDescriptor>();
            }

            return writer.Descriptors
                .Select(descriptor => new ChartHostedPropertyDescriptor(
                    entry.Profile.ModuleId,
                    entry.Profile.InstanceId,
                    entry.Module.ModuleDefinition.DisplayName,
                    entry.Module.ModuleDefinition.Category,
                    entry.Profile.IsEnabled,
                    entry.IsActive,
                    entry.LastError is not null,
                    ClonePropertyDescriptor(descriptor)))
                .ToArray();
        }
    }

    private IEnumerable<HostedModule> OrderedEntries() =>
        _instances.Values
            .OrderBy(static entry => entry.Profile.ZIndex)
            .ThenBy(
                static entry => entry.Profile.InstanceId,
                StringComparer.Ordinal);

    private static ChartModuleRuntimeSnapshot CreateSnapshot(
        HostedModule entry) =>
        new(
            entry.Profile.ModuleId,
            entry.Profile.InstanceId,
            entry.Profile.IsEnabled,
            entry.IsActive,
            entry.LastError is not null,
            entry.LastError,
            CloneProfile(entry.Profile));

    private static void EnsureCapability(
        HostedModule entry,
        ChartModuleCapabilities capability,
        string providerName)
    {
        if ((entry.Module.ModuleDefinition.Capabilities & capability) == 0)
        {
            throw new InvalidOperationException(
                $"Module '{entry.Profile.ModuleId}' implements {providerName} " +
                $"without declaring capability '{capability}'.");
        }
    }

    private static ChartCommandDescriptor CloneCommandDescriptor(
        ChartCommandDescriptor descriptor) =>
        new(
            descriptor.CommandId,
            descriptor.DisplayName,
            descriptor.Category,
            descriptor.IsCheckable,
            descriptor.Placement);

    private static ChartPropertyDescriptor ClonePropertyDescriptor(
        ChartPropertyDescriptor descriptor) =>
        new(
            descriptor.PropertyId,
            descriptor.DisplayName,
            descriptor.Category,
            descriptor.ValueKind,
            CloneMetadataValue(descriptor.Value),
            descriptor.ChangeImpact,
            descriptor.Storage,
            descriptor.IsReadOnly,
            descriptor.Minimum,
            descriptor.Maximum,
            descriptor.AllowedValues.ToArray());

    private static object? CloneMetadataValue(object? value) =>
        value is JsonNode node
            ? node.DeepClone()
            : value;

    private sealed class OwnedCommandWriter : IChartCommandWriter
    {
        private readonly HashSet<string> _ids = new(StringComparer.Ordinal);
        private readonly List<ChartCommandDescriptor> _descriptors = new();

        public IReadOnlyList<ChartCommandDescriptor> Descriptors =>
            _descriptors;

        public void Add(ChartCommandDescriptor descriptor)
        {
            ArgumentNullException.ThrowIfNull(descriptor);
            string commandId = RequireText(
                descriptor.CommandId,
                nameof(descriptor.CommandId));
            _ = RequireText(
                descriptor.DisplayName,
                nameof(descriptor.DisplayName));
            _ = RequireText(descriptor.Category, nameof(descriptor.Category));

            if (descriptor.Placement == ChartCommandPlacement.None)
            {
                throw new InvalidOperationException(
                    $"Command '{commandId}' has no UI or keyboard placement.");
            }
            if (!_ids.Add(commandId))
            {
                throw new InvalidOperationException(
                    $"Duplicate chart command id: {commandId}");
            }

            _descriptors.Add(CloneCommandDescriptor(descriptor));
        }
    }

    private sealed class OwnedPropertyWriter : IChartPropertyWriter
    {
        private readonly HashSet<string> _ids = new(StringComparer.Ordinal);
        private readonly List<ChartPropertyDescriptor> _descriptors = new();

        public IReadOnlyList<ChartPropertyDescriptor> Descriptors =>
            _descriptors;

        public void Add(ChartPropertyDescriptor descriptor)
        {
            ArgumentNullException.ThrowIfNull(descriptor);
            string propertyId = RequireText(
                descriptor.PropertyId,
                nameof(descriptor.PropertyId));
            _ = RequireText(
                descriptor.DisplayName,
                nameof(descriptor.DisplayName));
            _ = RequireText(descriptor.Category, nameof(descriptor.Category));

            if (!_ids.Add(propertyId))
            {
                throw new InvalidOperationException(
                    $"Duplicate chart property id: {propertyId}");
            }

            _descriptors.Add(ClonePropertyDescriptor(descriptor));
        }
    }
}
