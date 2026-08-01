// <chart-module>
// Module-Id: indicator.sma
// Module-Class: SmaModule
// Module-Category: Indicators
// Registration: registry.Register<SmaModule>()
// Profile-Key: modules[].instanceId
// Data-Requirements: PrimarySymbol.OHLCV
// Capabilities: DataRequirements, Computation, Visual, Properties, Commands
// Contributions: Polyline
// Default-Panel: price.main
// Renderer-Path: ContributionSet -> SceneCompiler -> ChartRenderPlan -> SkiaChartRenderer
// UI-Path: CommandDescriptor/PropertyDescriptor -> ContextMenu/QuickButton/PropertyInspector
// Persistence: ChartModuleProfile.Parameters, ChartModuleProfile.Style
// Verification: SmaModuleParityVerification
// </chart-module>

using System.Text.Json.Nodes;
using ChartKit.CSharp.Modules.Abstractions;

namespace ChartKit.CSharp.Modules.Indicators;

public sealed class SmaModule :
    IChartModule,
    IChartModuleFactory<SmaModule>,
    IDataRequirementProvider,
    IChartDataModule,
    IChartVisualProvider,
    IChartPropertyProvider,
    IChartCommandProvider
{
    private const int DefaultPeriod = 20;
    private const string DefaultStroke = "#FFC107";
    private readonly SmaSeriesRuntime _runtime = new(DefaultPeriod);
    private string? _panelId;
    private int _zIndex;
    private int _period = DefaultPeriod;
    private string _stroke = DefaultStroke;
    private bool _isActive;

    private SmaModule(string instanceId)
    {
        InstanceId = RequireText(instanceId, nameof(instanceId));
    }

    public static ChartModuleDefinition Definition { get; } =
        new(
            moduleId: "indicator.sma",
            displayName: "SMA",
            category: "Indicators",
            description: "Simple moving average of the primary close series.",
            defaultPanelId: "price.main",
            defaultEnabled: false,
            schemaVersion: 1,
            capabilities:
                ChartModuleCapabilities.DataRequirements |
                ChartModuleCapabilities.Computation |
                ChartModuleCapabilities.Visual |
                ChartModuleCapabilities.Properties |
                ChartModuleCapabilities.Commands,
            supportedPrimitiveKinds:
            [
                ChartPrimitiveKind.Polyline
            ]);

    public static SmaModule Create(string instanceId) => new(instanceId);

    public ChartModuleDefinition ModuleDefinition => Definition;
    public string InstanceId { get; }
    public long DataVersion => _runtime.DataVersion;
    public SmaRuntimeDiagnostics Diagnostics => _runtime.Diagnostics;

    public void Initialize(IChartModuleContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
    }

    public void ApplyProfile(ChartModuleProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (!StringComparer.Ordinal.Equals(profile.ModuleId, Definition.ModuleId))
            throw new InvalidOperationException("ModuleId mismatch.");
        if (!StringComparer.Ordinal.Equals(profile.InstanceId, InstanceId))
            throw new InvalidOperationException("InstanceId mismatch.");
        if (profile.ModuleSchemaVersion != Definition.SchemaVersion)
            throw new InvalidOperationException("Module schema mismatch.");
        if (profile.Parameters is null)
            throw new ArgumentNullException(nameof(profile.Parameters));
        if (profile.Style is null)
            throw new ArgumentNullException(nameof(profile.Style));

        _panelId = RequireText(profile.Placement, nameof(profile.Placement));
        _zIndex = profile.ZIndex;
        _period = ReadPeriod(profile.Parameters, "period", DefaultPeriod);
        _stroke = ReadText(profile.Style, "stroke", DefaultStroke);
        _runtime.SetPeriod(_period);
    }

    public void Activate()
    {
        if (_panelId is null)
            throw new InvalidOperationException(
                "Profile must be applied before activation.");
        _isActive = true;
    }

    public void Deactivate()
    {
        _isActive = false;
    }

    public void Reset()
    {
        _isActive = false;
        _panelId = null;
        _zIndex = 0;
        _period = DefaultPeriod;
        _stroke = DefaultStroke;
        _runtime.SetPeriod(DefaultPeriod);
        _runtime.Reset();
    }

    public void DescribeRequirements(IDataRequirementWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.Add(new ChartDataRequirement(
            "primary",
            "current",
            "current",
            "OHLCV"));
    }

    public void ApplyPrimarySeries(ChartPrimarySeriesSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (!_isActive) return;
        _runtime.Apply(snapshot);
    }

    public void BuildContributions(
        ChartVisualContext context,
        IChartContributionWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        if (!_isActive ||
            _panelId is null ||
            _runtime.DataVersion != context.DataVersion)
        {
            return;
        }

        IReadOnlyList<SmaValuePoint> values = _runtime.Values;
        if (values.Count == 0) return;

        var points = new ChartSeriesPoint[values.Count];
        for (int index = 0; index < values.Count; index++)
            points[index] = new ChartSeriesPoint(index, values[index].Value);

        writer.Add(new ChartContribution(
            new ChartObjectIdentity(
                Definition.ModuleId,
                InstanceId,
                "sma.value"),
            _panelId,
            ChartPrimitiveKind.Polyline,
            _zIndex,
            points));
    }

    public void DescribeProperties(IChartPropertyWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.Add(new ChartPropertyDescriptor(
            "period",
            "Period",
            "SMA",
            ChartPropertyValueKind.Integer,
            _period,
            ChartChangeImpact.RecalculateModule,
            ChartPropertyStorage.Parameters,
            minimum: 1,
            maximum: 10_000));
        writer.Add(new ChartPropertyDescriptor(
            "stroke",
            "Stroke",
            "SMA",
            ChartPropertyValueKind.Color,
            _stroke,
            ChartChangeImpact.RedrawOnly,
            ChartPropertyStorage.Style));
    }

    public void DescribeCommands(IChartCommandWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.Add(new ChartCommandDescriptor(
            "indicator.sma.inspect",
            "Inspect SMA",
            "Indicators",
            false,
            ChartCommandPlacement.ContextMenu |
            ChartCommandPlacement.QuickToolbar |
            ChartCommandPlacement.PropertyInspector));
    }

    public SmaValuePoint[] SnapshotValues() => _runtime.SnapshotValues();

    private static int ReadPeriod(
        JsonObject source,
        string key,
        int fallback)
    {
        if (!source.TryGetPropertyValue(key, out JsonNode? node) || node is null)
            return fallback;

        if (node is JsonValue value)
        {
            if (value.TryGetValue<int>(out int integer))
                return ValidatePeriod(integer);
            if (value.TryGetValue<long>(out long longValue) &&
                longValue >= int.MinValue && longValue <= int.MaxValue)
            {
                return ValidatePeriod((int)longValue);
            }
            if (value.TryGetValue<double>(out double doubleValue) &&
                double.IsFinite(doubleValue) &&
                doubleValue == Math.Truncate(doubleValue) &&
                doubleValue >= int.MinValue &&
                doubleValue <= int.MaxValue)
            {
                return ValidatePeriod((int)doubleValue);
            }
        }

        throw new InvalidOperationException(
            $"Parameter '{key}' must be an integer.");
    }

    private static int ValidatePeriod(int period)
    {
        if (period < 1 || period > 10_000)
            throw new InvalidOperationException(
                "SMA period must be between 1 and 10000.");
        return period;
    }

    private static string ReadText(
        JsonObject source,
        string key,
        string fallback)
    {
        if (!source.TryGetPropertyValue(key, out JsonNode? node) || node is null)
            return fallback;

        if (node is JsonValue value &&
            value.TryGetValue(out string? result) &&
            !string.IsNullOrWhiteSpace(result))
        {
            return result.Trim();
        }

        throw new InvalidOperationException(
            $"Property '{key}' must be a non-empty string.");
    }

    private static string RequireText(string value, string parameterName) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("Value must not be blank.", parameterName)
            : value.Trim();
}
