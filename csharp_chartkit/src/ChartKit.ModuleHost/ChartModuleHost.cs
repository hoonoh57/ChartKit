using System.Text.Json.Nodes;
using ChartKit.CSharp.Modules.Abstractions;

namespace ChartKit.CSharp.ModuleHost;

public sealed record SetModuleEnabledCommand(
    string InstanceId,
    bool IsEnabled);

public sealed record ChartModuleOperationResult(
    string InstanceId,
    bool Succeeded,
    bool Changed,
    string? Error)
{
    public static ChartModuleOperationResult Success(
        string instanceId,
        bool changed) =>
        new(instanceId, true, changed, null);

    public static ChartModuleOperationResult Failure(
        string instanceId,
        bool changed,
        string error) =>
        new(instanceId, false, changed, error);
}

public sealed record ChartModuleRuntimeSnapshot(
    string ModuleId,
    string InstanceId,
    bool IsEnabled,
    bool IsActive,
    bool IsFaulted,
    string? LastError,
    ChartModuleProfile Profile);

public sealed record ChartHostedContributionSet
{
    public ChartHostedContributionSet(
        string moduleId,
        string instanceId,
        int zIndex,
        IReadOnlyList<ChartContribution> contributions)
    {
        ModuleId = RequireText(moduleId, nameof(moduleId));
        InstanceId = RequireText(instanceId, nameof(instanceId));
        ZIndex = zIndex;
        Contributions = contributions is null
            ? throw new ArgumentNullException(nameof(contributions))
            : contributions.ToArray();
    }

    public string ModuleId { get; }
    public string InstanceId { get; }
    public int ZIndex { get; }
    public IReadOnlyList<ChartContribution> Contributions { get; }

    private static string RequireText(string value, string parameterName) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("Value must not be blank.", parameterName)
            : value.Trim();
}

public sealed class SystemChartModuleContext : IChartModuleContext
{
    public static SystemChartModuleContext Instance { get; } = new();

    private SystemChartModuleContext()
    {
    }

    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

public sealed class ChartModuleHost
{
    private readonly object _gate = new();
    private readonly ChartModuleRegistry _registry;
    private readonly IChartModuleContext _context;
    private readonly Dictionary<string, HostedModule> _instances =
        new(StringComparer.Ordinal);

    public ChartModuleHost(
        ChartModuleRegistry registry,
        IChartModuleContext? context = null)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _context = context ?? SystemChartModuleContext.Instance;
    }

    public int Count
    {
        get
        {
            lock (_gate)
                return _instances.Count;
        }
    }

    public ChartModuleOperationResult UpsertProfile(ChartModuleProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        lock (_gate)
        {
            ChartModuleProfile normalized;
            ChartModuleDefinition definition;
            try
            {
                normalized = CloneAndValidateProfile(profile, out definition);
            }
            catch (Exception exception)
            {
                return ChartModuleOperationResult.Failure(
                    profile.InstanceId ?? string.Empty,
                    false,
                    exception.Message);
            }

            if (!_instances.TryGetValue(
                    normalized.InstanceId,
                    out HostedModule? entry))
            {
                return AddInstance(normalized, definition);
            }

            if (!StringComparer.Ordinal.Equals(
                    entry.Profile.ModuleId,
                    normalized.ModuleId))
            {
                return ChartModuleOperationResult.Failure(
                    normalized.InstanceId,
                    false,
                    "ModuleId cannot change for an existing instance.");
            }

            bool profileChanged = !ProfilesEquivalent(entry.Profile, normalized);
            bool retryFaultedEnable =
                normalized.IsEnabled && entry.LastError is not null;

            if (!profileChanged && !retryFaultedEnable)
            {
                return ChartModuleOperationResult.Success(
                    normalized.InstanceId,
                    false);
            }

            bool wasActive = entry.IsActive;
            bool disabling = wasActive && !normalized.IsEnabled;

            if (disabling)
            {
                try
                {
                    entry.Module.Deactivate();
                    entry.IsActive = false;
                }
                catch (Exception exception)
                {
                    MarkFault(entry, exception, attemptDeactivate: false);
                    return ChartModuleOperationResult.Failure(
                        normalized.InstanceId,
                        true,
                        entry.LastError!);
                }
            }

            try
            {
                entry.Module.ApplyProfile(CloneProfile(normalized));
                entry.Profile = normalized;
                entry.LastError = null;
            }
            catch (Exception exception)
            {
                MarkFault(entry, exception, attemptDeactivate: wasActive && !disabling);
                return ChartModuleOperationResult.Failure(
                    normalized.InstanceId,
                    true,
                    entry.LastError!);
            }

            if (normalized.IsEnabled && !entry.IsActive)
            {
                try
                {
                    entry.Module.Activate();
                    entry.IsActive = true;
                    entry.LastError = null;
                }
                catch (Exception exception)
                {
                    MarkFault(entry, exception, attemptDeactivate: false);
                    return ChartModuleOperationResult.Failure(
                        normalized.InstanceId,
                        true,
                        entry.LastError!);
                }
            }

            return ChartModuleOperationResult.Success(
                normalized.InstanceId,
                true);
        }
    }

    public ChartModuleOperationResult Execute(
        SetModuleEnabledCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        return SetEnabled(command.InstanceId, command.IsEnabled);
    }

    public ChartModuleOperationResult SetEnabled(
        string instanceId,
        bool isEnabled)
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
                return ChartModuleOperationResult.Failure(
                    normalizedInstanceId,
                    false,
                    "Chart module instance is not hosted.");
            }

            if (entry.Profile.IsEnabled == isEnabled &&
                !(isEnabled && entry.LastError is not null))
            {
                return ChartModuleOperationResult.Success(
                    normalizedInstanceId,
                    false);
            }

            return UpsertProfile(
                entry.Profile with { IsEnabled = isEnabled });
        }
    }

    public ChartModuleOperationResult Remove(string instanceId)
    {
        string normalizedInstanceId = RequireText(
            instanceId,
            nameof(instanceId));

        lock (_gate)
        {
            if (!_instances.Remove(
                    normalizedInstanceId,
                    out HostedModule? entry))
            {
                return ChartModuleOperationResult.Failure(
                    normalizedInstanceId,
                    false,
                    "Chart module instance is not hosted.");
            }

            var errors = new List<string>();
            if (entry.IsActive)
            {
                try
                {
                    entry.Module.Deactivate();
                }
                catch (Exception exception)
                {
                    errors.Add(exception.Message);
                }
            }

            try
            {
                entry.Module.Reset();
            }
            catch (Exception exception)
            {
                errors.Add(exception.Message);
            }

            return errors.Count == 0
                ? ChartModuleOperationResult.Success(
                    normalizedInstanceId,
                    true)
                : ChartModuleOperationResult.Failure(
                    normalizedInstanceId,
                    true,
                    string.Join(" | ", errors));
        }
    }

    public IReadOnlyList<ChartModuleRuntimeSnapshot> GetSnapshots()
    {
        lock (_gate)
        {
            return _instances.Values
                .OrderBy(static entry => entry.Profile.ZIndex)
                .ThenBy(
                    static entry => entry.Profile.InstanceId,
                    StringComparer.Ordinal)
                .Select(static entry => new ChartModuleRuntimeSnapshot(
                    entry.Profile.ModuleId,
                    entry.Profile.InstanceId,
                    entry.Profile.IsEnabled,
                    entry.IsActive,
                    entry.LastError is not null,
                    entry.LastError,
                    CloneProfile(entry.Profile)))
                .ToArray();
        }
    }

    public IReadOnlyList<ChartHostedContributionSet>
        CollectVisualContributions(ChartVisualContext context)
    {
        lock (_gate)
        {
            var sets = new List<ChartHostedContributionSet>();

            foreach (HostedModule entry in _instances.Values
                         .OrderBy(static value => value.Profile.ZIndex)
                         .ThenBy(
                             static value => value.Profile.InstanceId,
                             StringComparer.Ordinal))
            {
                if (!entry.Profile.IsEnabled ||
                    !entry.IsActive ||
                    entry.LastError is not null ||
                    entry.Module is not IChartVisualProvider visualProvider)
                {
                    continue;
                }

                var writer = new OwnedContributionWriter(entry);
                try
                {
                    visualProvider.BuildContributions(context, writer);
                }
                catch (Exception exception)
                {
                    MarkFault(entry, exception, attemptDeactivate: true);
                    continue;
                }

                if (writer.Contributions.Count > 0)
                {
                    sets.Add(new ChartHostedContributionSet(
                        entry.Profile.ModuleId,
                        entry.Profile.InstanceId,
                        entry.Profile.ZIndex,
                        writer.Contributions));
                }
            }

            return sets.ToArray();
        }
    }

    private ChartModuleOperationResult AddInstance(
        ChartModuleProfile profile,
        ChartModuleDefinition definition)
    {
        IChartModule module;
        try
        {
            module = _registry.Create(profile.ModuleId, profile.InstanceId);
            if (!ReferenceEquals(module.ModuleDefinition, definition))
            {
                throw new InvalidOperationException(
                    "Registry definition and module definition are inconsistent.");
            }

            module.Initialize(_context);
            module.ApplyProfile(CloneProfile(profile));
        }
        catch (Exception exception)
        {
            return ChartModuleOperationResult.Failure(
                profile.InstanceId,
                false,
                exception.Message);
        }

        var entry = new HostedModule(module, profile);
        _instances.Add(profile.InstanceId, entry);

        if (profile.IsEnabled)
        {
            try
            {
                module.Activate();
                entry.IsActive = true;
            }
            catch (Exception exception)
            {
                MarkFault(entry, exception, attemptDeactivate: false);
                return ChartModuleOperationResult.Failure(
                    profile.InstanceId,
                    true,
                    entry.LastError!);
            }
        }

        return ChartModuleOperationResult.Success(profile.InstanceId, true);
    }

    private ChartModuleProfile CloneAndValidateProfile(
        ChartModuleProfile profile,
        out ChartModuleDefinition definition)
    {
        string moduleId = RequireText(profile.ModuleId, nameof(profile.ModuleId));
        string instanceId = RequireText(
            profile.InstanceId,
            nameof(profile.InstanceId));
        string placement = RequireText(profile.Placement, nameof(profile.Placement));

        if (profile.ModuleSchemaVersion < 1)
            throw new ArgumentOutOfRangeException(nameof(profile.ModuleSchemaVersion));
        if (profile.Parameters is null)
            throw new ArgumentNullException(nameof(profile.Parameters));
        if (profile.Style is null)
            throw new ArgumentNullException(nameof(profile.Style));
        if (profile.PersistentState is null)
            throw new ArgumentNullException(nameof(profile.PersistentState));
        if (!_registry.TryGetDefinition(moduleId, out definition) ||
            definition is null)
        {
            throw new InvalidOperationException(
                $"Chart module is not registered: {moduleId}");
        }
        if (profile.ModuleSchemaVersion != definition.SchemaVersion)
        {
            throw new InvalidOperationException(
                $"Module schema mismatch for {moduleId}: profile=" +
                $"{profile.ModuleSchemaVersion}, registered={definition.SchemaVersion}.");
        }

        return new ChartModuleProfile
        {
            ModuleId = moduleId,
            InstanceId = instanceId,
            ModuleSchemaVersion = profile.ModuleSchemaVersion,
            IsEnabled = profile.IsEnabled,
            ZIndex = profile.ZIndex,
            Placement = placement,
            Parameters = CloneObject(profile.Parameters),
            Style = CloneObject(profile.Style),
            PersistentState = CloneObject(profile.PersistentState)
        };
    }

    private static ChartModuleProfile CloneProfile(ChartModuleProfile profile) =>
        new()
        {
            ModuleId = profile.ModuleId,
            InstanceId = profile.InstanceId,
            ModuleSchemaVersion = profile.ModuleSchemaVersion,
            IsEnabled = profile.IsEnabled,
            ZIndex = profile.ZIndex,
            Placement = profile.Placement,
            Parameters = CloneObject(profile.Parameters),
            Style = CloneObject(profile.Style),
            PersistentState = CloneObject(profile.PersistentState)
        };

    private static JsonObject CloneObject(JsonObject source) =>
        (JsonObject)source.DeepClone();

    private static bool ProfilesEquivalent(
        ChartModuleProfile left,
        ChartModuleProfile right) =>
        StringComparer.Ordinal.Equals(left.ModuleId, right.ModuleId) &&
        StringComparer.Ordinal.Equals(left.InstanceId, right.InstanceId) &&
        left.ModuleSchemaVersion == right.ModuleSchemaVersion &&
        left.IsEnabled == right.IsEnabled &&
        left.ZIndex == right.ZIndex &&
        StringComparer.Ordinal.Equals(left.Placement, right.Placement) &&
        JsonNode.DeepEquals(left.Parameters, right.Parameters) &&
        JsonNode.DeepEquals(left.Style, right.Style) &&
        JsonNode.DeepEquals(left.PersistentState, right.PersistentState);

    private static void MarkFault(
        HostedModule entry,
        Exception exception,
        bool attemptDeactivate)
    {
        string error = exception.Message;
        if (attemptDeactivate && entry.IsActive)
        {
            try
            {
                entry.Module.Deactivate();
            }
            catch (Exception deactivateException)
            {
                error += " | Deactivate: " + deactivateException.Message;
            }
        }

        entry.IsActive = false;
        entry.LastError = error;
    }

    private static string RequireText(string value, string parameterName) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("Value must not be blank.", parameterName)
            : value.Trim();

    private sealed class HostedModule
    {
        public HostedModule(
            IChartModule module,
            ChartModuleProfile profile)
        {
            Module = module;
            Profile = profile;
        }

        public IChartModule Module { get; }
        public ChartModuleProfile Profile { get; set; }
        public bool IsActive { get; set; }
        public string? LastError { get; set; }
    }

    private sealed class OwnedContributionWriter : IChartContributionWriter
    {
        private readonly HostedModule _entry;
        private readonly HashSet<ChartObjectIdentity> _identities = new();
        private readonly List<ChartContribution> _contributions = new();

        public OwnedContributionWriter(HostedModule entry)
        {
            _entry = entry;
        }

        public IReadOnlyList<ChartContribution> Contributions =>
            _contributions;

        public void Add(ChartContribution contribution)
        {
            ArgumentNullException.ThrowIfNull(contribution);

            if (!StringComparer.Ordinal.Equals(
                    contribution.Identity.ModuleId,
                    _entry.Profile.ModuleId) ||
                !StringComparer.Ordinal.Equals(
                    contribution.Identity.InstanceId,
                    _entry.Profile.InstanceId))
            {
                throw new InvalidOperationException(
                    "Contribution ownership does not match the hosted module.");
            }

            if (!_entry.Module.ModuleDefinition.SupportedPrimitiveKinds.Contains(
                    contribution.PrimitiveKind))
            {
                throw new InvalidOperationException(
                    $"Primitive '{contribution.PrimitiveKind}' is not declared by " +
                    $"module '{_entry.Profile.ModuleId}'.");
            }

            if (!_identities.Add(contribution.Identity))
            {
                throw new InvalidOperationException(
                    $"Duplicate chart object identity: {contribution.Identity}");
            }

            _contributions.Add(contribution);
        }
    }
}
