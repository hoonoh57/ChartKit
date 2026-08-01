using System.Text.Json.Nodes;
using ChartKit.CSharp.Composition;
using ChartKit.CSharp.ModuleHost;
using ChartKit.CSharp.Modules.Abstractions;
using ChartKit.CSharp.Modules.Indicators;
using ChartKit.CSharp.Modules.Platform;
using ChartKit.CSharp.Persistence;
using ChartKit.CSharp.Scene;
using ChartKit.CSharp.UiModel;

namespace ChartKit.CSharp.App;

internal sealed record ChartModulePlatformActionResult(
    bool Succeeded,
    bool Changed,
    string? Error)
{
    public static ChartModulePlatformActionResult Success(bool changed) =>
        new(true, changed, null);

    public static ChartModulePlatformActionResult Failure(string error) =>
        new(false, false, error);
}

internal sealed class ChartModulePlatformController : IDisposable
{
    private readonly string _profilePath;
    private readonly ChartProfileStore _profileStore = new();
    private readonly ChartModuleRegistry _registry = new();
    private readonly ChartModuleHost _host;
    private readonly ChartCompositionService _composition;
    private readonly ChartSelectionService _selection = new();
    private readonly ChartModuleUiCatalog _uiCatalog;
    private readonly ChartPropertyMutationService _propertyMutation;
    private readonly SemaphoreSlim _saveGate = new(1, 1);
    private readonly SemaphoreSlim _dataGate = new(1, 1);
    private ChartProfile? _profile;
    private ChartRenderPlan _renderPlan = ChartRenderPlan.Empty;
    private ChartVisualContext _visualContext = new(0, 0, 0);
    private ChartPrimarySeriesSnapshot _primarySeries =
        ChartPrimarySeriesSnapshot.Empty;
    private bool _disposed;

    public ChartModulePlatformController(string profilePath)
    {
        _profilePath = RequirePath(profilePath);
        _registry.Register<PlatformProbeModule>();
        _registry.Register<SmaModule>();
        _registry.Register<RsiModule>();
        _host = new ChartModuleHost(_registry);
        _composition = new ChartCompositionService(_host);
        _uiCatalog = new ChartModuleUiCatalog(_registry, _host, _selection);
        _propertyMutation = new ChartPropertyMutationService(_host);
    }

    public bool IsInitialized => _profile is not null;

    public string ProfilePath => _profilePath;

    public ChartRenderPlan RenderPlan => _renderPlan;

    public ChartProfile Profile =>
        _profile ?? throw new InvalidOperationException(
            "Chart module platform is not initialized.");

    public async Task<ChartProfile> InitializeAsync(
        string fallbackTimeframe,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (_profile is not null)
            return CloneProfile(_profile);

        ChartProfile loaded = File.Exists(_profilePath)
            ? await _profileStore.LoadAsync(_profilePath, cancellationToken)
                .ConfigureAwait(false)
            : CreateDefaultProfile(fallbackTimeframe);

        ChartProfile normalized = EnsureRegisteredDefaults(loaded);
        ApplyRegisteredProfiles(normalized.Modules);
        _profile = normalized;
        RecomposeCurrent();
        return CloneProfile(normalized);
    }

    public ChartUiCatalogSnapshot BuildUiCatalog()
    {
        ThrowIfDisposed();
        EnsureInitialized();
        return _uiCatalog.Build();
    }

    public bool SelectModule(string moduleId, string instanceId)
    {
        ThrowIfDisposed();
        EnsureInitialized();
        return _selection.SelectModule(moduleId, instanceId);
    }

    public bool Select(ChartObjectIdentity identity)
    {
        ThrowIfDisposed();
        EnsureInitialized();
        return _selection.Select(identity);
    }

    public async Task<ChartModulePlatformActionResult> ExecuteCommandAsync(
        ChartUiCommandItem command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ThrowIfDisposed();
        EnsureInitialized();

        if (command.Kind == ChartUiCommandKind.ModuleCommand)
        {
            bool selectionChanged = _selection.Select(command.Owner);
            return ChartModulePlatformActionResult.Success(selectionChanged);
        }

        if (command.Kind != ChartUiCommandKind.ModuleToggle ||
            !StringComparer.Ordinal.Equals(
                command.CommandId,
                ChartModuleUiCatalog.ModuleToggleCommandId))
        {
            return ChartModulePlatformActionResult.Failure(
                $"Unsupported chart UI command: {command.CommandId}");
        }

        ChartModuleOperationResult operation = _host.SetEnabled(
            command.Owner.InstanceId,
            !command.IsChecked);
        if (!operation.Succeeded)
        {
            return ChartModulePlatformActionResult.Failure(
                operation.Error ?? "Module toggle failed.");
        }

        _selection.Select(command.Owner);
        if (operation.Changed)
        {
            RefreshProfileFromHost();
            if (!command.IsChecked && _primarySeries.Bars.Count > 0)
            {
                await ApplyCurrentPrimarySeriesAsync(cancellationToken)
                    .ConfigureAwait(false);
            }
            RecomposeCurrent();
            await SaveCurrentAsync(cancellationToken).ConfigureAwait(false);
        }

        return ChartModulePlatformActionResult.Success(operation.Changed);
    }

    public async Task<ChartPropertyChangeResult> ChangePropertyAsync(
        string instanceId,
        string propertyId,
        JsonNode? value,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        EnsureInitialized();

        ChartPropertyChangeResult result = _propertyMutation.Execute(
            new ChangeChartPropertyCommand(instanceId, propertyId, value));
        if (!result.Succeeded || !result.Changed)
            return result;

        RefreshProfileFromHost();
        if (result.ChangeImpact >= ChartChangeImpact.RecalculateModule &&
            _primarySeries.Bars.Count > 0)
        {
            await ApplyCurrentPrimarySeriesAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        RecomposeCurrent();
        await SaveCurrentAsync(cancellationToken).ConfigureAwait(false);
        return result;
    }

    public async Task<ChartModuleDataUpdateResult> UpdatePrimarySeriesAsync(
        ChartPrimarySeriesSnapshot snapshot,
        long viewportVersion,
        long themeVersion,
        int visibleStartIndex,
        int visibleEndExclusive,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ThrowIfDisposed();
        EnsureInitialized();
        ValidateVisibleRange(visibleStartIndex, visibleEndExclusive);

        await _dataGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ChartModuleDataUpdateResult result = await Task.Run(
                    () => _host.ApplyPrimarySeries(snapshot),
                    cancellationToken)
                .ConfigureAwait(false);
            _primarySeries = snapshot;
            _visualContext = new ChartVisualContext(
                snapshot.DataVersion,
                viewportVersion,
                themeVersion,
                visibleStartIndex,
                visibleEndExclusive);
            RecomposeCurrent();
            return result;
        }
        finally
        {
            _dataGate.Release();
        }
    }

    public async Task UpdateShellProfileAsync(
        string timeframe,
        JsonObject layout,
        JsonObject interaction,
        JsonObject theme,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        EnsureInitialized();
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(interaction);
        ArgumentNullException.ThrowIfNull(theme);

        _profile = BuildProfileFromHost(
            timeframe,
            layout,
            interaction,
            theme);
        await SaveCurrentAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task SaveCurrentAsync(
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        EnsureInitialized();

        await _saveGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _profileStore.SaveAsync(
                    _profilePath,
                    Profile,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _saveGate.Release();
        }
    }

    public void Recompose(
        long dataVersion = 0,
        long viewportVersion = 0,
        long themeVersion = 0)
    {
        ThrowIfDisposed();
        EnsureInitialized();
        _visualContext = _visualContext with
        {
            DataVersion = dataVersion,
            ViewportVersion = viewportVersion,
            ThemeVersion = themeVersion
        };
        RecomposeCurrent();
    }

    public void Recompose(
        long dataVersion,
        long viewportVersion,
        long themeVersion,
        int visibleStartIndex,
        int visibleEndExclusive)
    {
        ThrowIfDisposed();
        EnsureInitialized();
        ValidateVisibleRange(visibleStartIndex, visibleEndExclusive);

        _visualContext = new ChartVisualContext(
            dataVersion,
            viewportVersion,
            themeVersion,
            visibleStartIndex,
            visibleEndExclusive);
        RecomposeCurrent();
    }

    public IReadOnlyList<ChartModuleRuntimeSnapshot> GetSnapshots()
    {
        ThrowIfDisposed();
        EnsureInitialized();
        return _host.GetSnapshots();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _saveGate.Dispose();
        _dataGate.Dispose();
    }

    private async Task<ChartModuleDataUpdateResult>
        ApplyCurrentPrimarySeriesAsync(CancellationToken cancellationToken)
    {
        await _dataGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await Task.Run(
                    () => _host.ApplyPrimarySeries(_primarySeries),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _dataGate.Release();
        }
    }

    private void RecomposeCurrent()
    {
        _renderPlan = _composition.Compose(_visualContext);
    }

    private ChartProfile CreateDefaultProfile(string timeframe) =>
        new(
            RequireText(timeframe, nameof(timeframe)),
            modules: _registry.Definitions.Select(CreateDefaultModuleProfile));

    private ChartProfile EnsureRegisteredDefaults(ChartProfile source)
    {
        IReadOnlyList<ChartModuleProfile> existing = source.Modules;
        var modules = new List<ChartModuleProfile>(existing);
        var registeredModuleIds = new HashSet<string>(
            existing.Select(static module => module.ModuleId),
            StringComparer.Ordinal);

        foreach (ChartModuleDefinition definition in _registry.Definitions)
        {
            if (!registeredModuleIds.Contains(definition.ModuleId))
                modules.Add(CreateDefaultModuleProfile(definition));
        }

        return new ChartProfile(
            source.Timeframe,
            source.Layout,
            source.Interaction,
            source.Theme,
            modules);
    }

    private void ApplyRegisteredProfiles(
        IReadOnlyList<ChartModuleProfile> modules)
    {
        foreach (ChartModuleProfile module in modules)
        {
            if (!_registry.Contains(module.ModuleId))
                continue;

            ChartModuleOperationResult result = _host.UpsertProfile(module);
            if (!result.Succeeded)
            {
                throw new InvalidOperationException(
                    $"Failed to load module profile '{module.InstanceId}': " +
                    (result.Error ?? "unknown error"));
            }
        }
    }

    private void RefreshProfileFromHost()
    {
        ChartProfile current = Profile;
        _profile = BuildProfileFromHost(
            current.Timeframe,
            current.Layout,
            current.Interaction,
            current.Theme);
    }

    private ChartProfile BuildProfileFromHost(
        string timeframe,
        JsonObject layout,
        JsonObject interaction,
        JsonObject theme)
    {
        ChartProfile current = Profile;
        IReadOnlyList<ChartModuleRuntimeSnapshot> snapshots = _host.GetSnapshots();
        var knownByInstance = snapshots.ToDictionary(
            static snapshot => snapshot.InstanceId,
            static snapshot => snapshot.Profile,
            StringComparer.Ordinal);
        var modules = new List<ChartModuleProfile>();
        var consumed = new HashSet<string>(StringComparer.Ordinal);

        foreach (ChartModuleProfile original in current.Modules)
        {
            if (knownByInstance.TryGetValue(
                    original.InstanceId,
                    out ChartModuleProfile? hosted))
            {
                modules.Add(hosted);
                consumed.Add(original.InstanceId);
            }
            else
            {
                modules.Add(original);
            }
        }

        foreach (ChartModuleRuntimeSnapshot snapshot in snapshots
                     .OrderBy(static item => item.Profile.ZIndex)
                     .ThenBy(
                         static item => item.InstanceId,
                         StringComparer.Ordinal))
        {
            if (consumed.Add(snapshot.InstanceId))
                modules.Add(snapshot.Profile);
        }

        return new ChartProfile(
            RequireText(timeframe, nameof(timeframe)),
            layout,
            interaction,
            theme,
            modules);
    }

    private static ChartModuleProfile CreateDefaultModuleProfile(
        ChartModuleDefinition definition) =>
        new()
        {
            ModuleId = definition.ModuleId,
            InstanceId = definition.ModuleId + ".default",
            ModuleSchemaVersion = definition.SchemaVersion,
            IsEnabled = definition.DefaultEnabled,
            ZIndex = 0,
            Placement = definition.DefaultPanelId,
            Parameters = new JsonObject(),
            Style = new JsonObject(),
            PersistentState = new JsonObject()
        };

    private static ChartProfile CloneProfile(ChartProfile profile) =>
        new(
            profile.Timeframe,
            profile.Layout,
            profile.Interaction,
            profile.Theme,
            profile.Modules);

    private void EnsureInitialized()
    {
        if (_profile is null)
        {
            throw new InvalidOperationException(
                "Chart module platform is not initialized.");
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private static void ValidateVisibleRange(
        int visibleStartIndex,
        int visibleEndExclusive)
    {
        if (visibleStartIndex < 0)
            throw new ArgumentOutOfRangeException(nameof(visibleStartIndex));
        if (visibleEndExclusive <= visibleStartIndex)
            throw new ArgumentOutOfRangeException(nameof(visibleEndExclusive));
    }

    private static string RequirePath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Profile path is required.", nameof(value));
        return Path.GetFullPath(value.Trim());
    }

    private static string RequireText(string? value, string parameterName) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("Value must not be blank.", parameterName)
            : value.Trim();
}
