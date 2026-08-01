using ChartKit.CSharp.Modules.Abstractions;

namespace ChartKit.CSharp.UiModel;

public enum ChartUiCommandKind
{
    ModuleToggle,
    ModuleCommand
}

public sealed record ChartUiCommandItem
{
    public ChartUiCommandItem(
        ChartObjectIdentity owner,
        string commandId,
        string displayName,
        string category,
        bool isCheckable,
        bool isChecked,
        bool isEnabled,
        ChartCommandPlacement placement,
        ChartUiCommandKind kind)
    {
        owner.Validate();
        Owner = owner;
        CommandId = RequireText(commandId, nameof(commandId));
        DisplayName = RequireText(displayName, nameof(displayName));
        Category = RequireText(category, nameof(category));
        IsCheckable = isCheckable;
        IsChecked = isChecked;
        IsEnabled = isEnabled;
        Placement = placement;
        Kind = kind;
    }

    public ChartObjectIdentity Owner { get; }
    public string CommandId { get; }
    public string DisplayName { get; }
    public string Category { get; }
    public bool IsCheckable { get; }
    public bool IsChecked { get; }
    public bool IsEnabled { get; }
    public ChartCommandPlacement Placement { get; }
    public ChartUiCommandKind Kind { get; }

    private static string RequireText(string value, string parameterName) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("Value must not be blank.", parameterName)
            : value.Trim();
}

public sealed record ChartUiPropertyItem
{
    public ChartUiPropertyItem(
        ChartObjectIdentity owner,
        string moduleDisplayName,
        string moduleCategory,
        bool isModuleEnabled,
        bool isEditable,
        ChartPropertyDescriptor descriptor)
    {
        owner.Validate();
        Owner = owner;
        ModuleDisplayName = RequireText(
            moduleDisplayName,
            nameof(moduleDisplayName));
        ModuleCategory = RequireText(moduleCategory, nameof(moduleCategory));
        IsModuleEnabled = isModuleEnabled;
        IsEditable = isEditable;
        Descriptor = descriptor ??
            throw new ArgumentNullException(nameof(descriptor));
    }

    public ChartObjectIdentity Owner { get; }
    public string ModuleDisplayName { get; }
    public string ModuleCategory { get; }
    public bool IsModuleEnabled { get; }
    public bool IsEditable { get; }
    public ChartPropertyDescriptor Descriptor { get; }

    private static string RequireText(string value, string parameterName) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("Value must not be blank.", parameterName)
            : value.Trim();
}

public sealed class ChartUiCatalogSnapshot
{
    public ChartUiCatalogSnapshot(
        ChartObjectIdentity? selection,
        IReadOnlyList<ChartUiCommandItem> contextMenuItems,
        IReadOnlyList<ChartUiCommandItem> quickToolbarItems,
        IReadOnlyList<ChartUiPropertyItem> inspectorProperties)
    {
        if (selection.HasValue)
            selection.Value.Validate();

        Selection = selection;
        ContextMenuItems = contextMenuItems is null
            ? throw new ArgumentNullException(nameof(contextMenuItems))
            : contextMenuItems.ToArray();
        QuickToolbarItems = quickToolbarItems is null
            ? throw new ArgumentNullException(nameof(quickToolbarItems))
            : quickToolbarItems.ToArray();
        InspectorProperties = inspectorProperties is null
            ? throw new ArgumentNullException(nameof(inspectorProperties))
            : inspectorProperties.ToArray();
    }

    public ChartObjectIdentity? Selection { get; }
    public IReadOnlyList<ChartUiCommandItem> ContextMenuItems { get; }
    public IReadOnlyList<ChartUiCommandItem> QuickToolbarItems { get; }
    public IReadOnlyList<ChartUiPropertyItem> InspectorProperties { get; }
}

public sealed class ChartSelectionService
{
    private readonly object _gate = new();
    private ChartObjectIdentity? _current;

    public ChartObjectIdentity? Current
    {
        get
        {
            lock (_gate)
                return _current;
        }
    }

    public bool Select(ChartObjectIdentity identity)
    {
        identity.Validate();
        lock (_gate)
        {
            if (_current.HasValue && _current.Value.Equals(identity))
                return false;

            _current = identity;
            return true;
        }
    }

    public bool SelectModule(string moduleId, string instanceId) =>
        Select(new ChartObjectIdentity(
            RequireText(moduleId, nameof(moduleId)),
            RequireText(instanceId, nameof(instanceId)),
            "module"));

    public bool Clear()
    {
        lock (_gate)
        {
            if (!_current.HasValue)
                return false;

            _current = null;
            return true;
        }
    }

    private static string RequireText(string value, string parameterName) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("Value must not be blank.", parameterName)
            : value.Trim();
}
