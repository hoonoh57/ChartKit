using ChartKit.CSharp.ModuleHost;
using ChartKit.CSharp.Modules.Abstractions;

namespace ChartKit.CSharp.UiModel;

public sealed class ChartModuleUiCatalog
{
    public const string ModuleToggleCommandId = "chart.module.toggle";

    private readonly ChartModuleRegistry _registry;
    private readonly ChartModuleHost _host;
    private readonly ChartSelectionService _selection;

    public ChartModuleUiCatalog(
        ChartModuleRegistry registry,
        ChartModuleHost host,
        ChartSelectionService selection)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _selection = selection ??
            throw new ArgumentNullException(nameof(selection));
    }

    public ChartUiCatalogSnapshot Build()
    {
        IReadOnlyList<ChartHostedCommandDescriptor> hostedCommands =
            _host.CollectCommandDescriptors();
        IReadOnlyList<ChartModuleRuntimeSnapshot> snapshots =
            _host.GetSnapshots();
        var snapshotByInstance = snapshots.ToDictionary(
            static snapshot => snapshot.InstanceId,
            StringComparer.Ordinal);
        var allCommands = new List<ChartUiCommandItem>();

        foreach (ChartModuleRuntimeSnapshot snapshot in snapshots)
        {
            if (!_registry.TryGetDefinition(
                    snapshot.ModuleId,
                    out ChartModuleDefinition? definition) ||
                definition is null)
            {
                throw new InvalidOperationException(
                    $"Hosted module definition is unavailable: {snapshot.ModuleId}");
            }

            allCommands.Add(new ChartUiCommandItem(
                ModuleOwner(snapshot.ModuleId, snapshot.InstanceId),
                ModuleToggleCommandId,
                definition.DisplayName,
                definition.Category,
                isCheckable: true,
                isChecked: snapshot.IsEnabled,
                isEnabled: true,
                ChartCommandPlacement.ContextMenu |
                ChartCommandPlacement.QuickToolbar,
                ChartUiCommandKind.ModuleToggle));
        }

        foreach (ChartHostedCommandDescriptor hosted in hostedCommands)
        {
            allCommands.Add(new ChartUiCommandItem(
                ModuleOwner(hosted.ModuleId, hosted.InstanceId),
                hosted.Descriptor.CommandId,
                hosted.Descriptor.DisplayName,
                hosted.Descriptor.Category,
                hosted.Descriptor.IsCheckable,
                isChecked: false,
                isEnabled: !hosted.IsFaulted,
                hosted.Descriptor.Placement,
                ChartUiCommandKind.ModuleCommand));
        }

        ChartObjectIdentity? currentSelection = _selection.Current;
        IReadOnlyList<ChartUiPropertyItem> inspectorProperties =
            BuildInspectorProperties(currentSelection, snapshotByInstance);

        ChartUiCommandItem[] contextMenu = allCommands
            .Where(static item =>
                (item.Placement & ChartCommandPlacement.ContextMenu) != 0)
            .OrderBy(static item => item.Category, StringComparer.Ordinal)
            .ThenBy(static item => item.DisplayName, StringComparer.Ordinal)
            .ThenBy(static item => item.Owner.InstanceId, StringComparer.Ordinal)
            .ThenBy(static item => item.CommandId, StringComparer.Ordinal)
            .ToArray();
        ChartUiCommandItem[] quickToolbar = allCommands
            .Where(static item =>
                (item.Placement & ChartCommandPlacement.QuickToolbar) != 0)
            .OrderBy(static item => item.Category, StringComparer.Ordinal)
            .ThenBy(static item => item.DisplayName, StringComparer.Ordinal)
            .ThenBy(static item => item.Owner.InstanceId, StringComparer.Ordinal)
            .ThenBy(static item => item.CommandId, StringComparer.Ordinal)
            .ToArray();

        return new ChartUiCatalogSnapshot(
            currentSelection,
            contextMenu,
            quickToolbar,
            inspectorProperties);
    }

    private IReadOnlyList<ChartUiPropertyItem> BuildInspectorProperties(
        ChartObjectIdentity? selection,
        IReadOnlyDictionary<string, ChartModuleRuntimeSnapshot>
            snapshotByInstance)
    {
        if (!selection.HasValue ||
            !snapshotByInstance.TryGetValue(
                selection.Value.InstanceId,
                out ChartModuleRuntimeSnapshot? snapshot) ||
            !StringComparer.Ordinal.Equals(
                selection.Value.ModuleId,
                snapshot.ModuleId))
        {
            return Array.Empty<ChartUiPropertyItem>();
        }

        IReadOnlyList<ChartHostedPropertyDescriptor> hostedProperties =
            _host.CollectPropertyDescriptors(snapshot.InstanceId);
        if (_host.TryGetSnapshot(
                snapshot.InstanceId,
                out ChartModuleRuntimeSnapshot? refreshed) &&
            refreshed is not null)
        {
            snapshot = refreshed;
        }

        return hostedProperties
            .OrderBy(
                static property => property.Descriptor.Category,
                StringComparer.Ordinal)
            .ThenBy(
                static property => property.Descriptor.DisplayName,
                StringComparer.Ordinal)
            .ThenBy(
                static property => property.Descriptor.PropertyId,
                StringComparer.Ordinal)
            .Select(property => new ChartUiPropertyItem(
                selection.Value,
                property.ModuleDisplayName,
                property.ModuleCategory,
                snapshot.IsEnabled,
                !snapshot.IsFaulted && !property.Descriptor.IsReadOnly,
                property.Descriptor))
            .ToArray();
    }

    private static ChartObjectIdentity ModuleOwner(
        string moduleId,
        string instanceId) =>
        new(moduleId, instanceId, "module");
}
