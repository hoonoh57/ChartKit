// <chart-module>
// Module-Id: indicator.obv
// Module-Class: ObvModule
// Module-Category: Indicators
// Registration: registry.Register<ObvModule>()
// Profile-Key: modules[].instanceId
// Data-Requirements: PrimarySymbol.OHLCV
// Capabilities: DataRequirements, Computation, Visual, Properties, Commands
// Contributions: Polyline
// Default-Panel: indicator.5
// Renderer-Path: ContributionSet -> SceneCompiler -> ChartRenderPlan -> SkiaChartRenderer
// UI-Path: CommandDescriptor/PropertyDescriptor -> ContextMenu/QuickButton/PropertyInspector
// Persistence: ChartModuleProfile.Parameters, ChartModuleProfile.Style
// Verification: ObvModuleParityVerification
// </chart-module>

using System.Text.Json.Nodes;
using ChartKit.CSharp.Modules.Abstractions;

namespace ChartKit.CSharp.Modules.Indicators;

public sealed class ObvModule :
    IChartModule,
    IChartModuleFactory<ObvModule>,
    IDataRequirementProvider,
    IChartDataModule,
    IChartVisualProvider,
    IChartPropertyProvider,
    IChartCommandProvider
{
    public const string ObvObjectId = "obv.value";
    public const string SignalObjectId = "obv.signal";

    private const int DefaultSignalPeriod = 20;
    private const string DefaultObvStroke = "#7E57C2";
    private const string DefaultSignalStroke = "#FFC107";

    private readonly ObvSeriesRuntime _runtime = new(DefaultSignalPeriod);
    private string? _panelId;
    private int _zIndex;
    private int _signalPeriod = DefaultSignalPeriod;
    private string _obvStroke = DefaultObvStroke;
    private string _signalStroke = DefaultSignalStroke;
    private bool _isActive;

    private ObvModule(string instanceId)
    {
        InstanceId = RequireText(instanceId, nameof(instanceId));
    }

    public static ChartModuleDefinition Definition { get; } = new(
        moduleId: "indicator.obv",
        displayName: "OBV",
        category: "Indicators",
        description: "On-balance volume with a simple moving-average signal line.",
        defaultPanelId: "indicator.5",
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

    public static ObvModule Create(string instanceId) => new(instanceId);

    public ChartModuleDefinition ModuleDefinition => Definition;
    public string InstanceId { get; }
    public long DataVersion => _runtime.DataVersion;
    public ObvRuntimeDiagnostics Diagnostics => _runtime.Diagnostics;

    public void Initialize(IChartModuleContext context) =>
        ArgumentNullException.ThrowIfNull(context);

    public void ApplyProfile(ChartModuleProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (!StringComparer.Ordinal.Equals(profile.ModuleId, Definition.ModuleId))
            throw new InvalidOperationException("ModuleId mismatch.");
        if (!StringComparer.Ordinal.Equals(profile.InstanceId, InstanceId))
            throw new InvalidOperationException("InstanceId mismatch.");
        if (profile.ModuleSchemaVersion != Definition.SchemaVersion)
            throw new InvalidOperationException("Module schema mismatch.");

        _signalPeriod = ReadPeriod(
            profile.Parameters,
            "signalPeriod",
            DefaultSignalPeriod);
        _panelId = RequireText(profile.Placement, nameof(profile.Placement));
        _zIndex = profile.ZIndex;
        _obvStroke = ReadText(
            profile.Style,
            ObvObjectId + ".stroke",
            DefaultObvStroke);
        _signalStroke = ReadText(
            profile.Style,
            SignalObjectId + ".stroke",
            DefaultSignalStroke);
        _runtime.SetSignalPeriod(_signalPeriod);
    }

    public void Activate()
    {
        if (_panelId is null)
            throw new InvalidOperationException(
                "Profile must be applied before activation.");
        _isActive = true;
    }

    public void Deactivate() => _isActive = false;

    public void Reset()
    {
        _isActive = false;
        _panelId = null;
        _zIndex = 0;
        _signalPeriod = DefaultSignalPeriod;
        _obvStroke = DefaultObvStroke;
        _signalStroke = DefaultSignalStroke;
        _runtime.SetSignalPeriod(DefaultSignalPeriod);
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
        if (_isActive) _runtime.Apply(snapshot);
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

        IReadOnlyList<ObvValuePoint> values = _runtime.Values;
        if (values.Count == 0) return;

        var obv = new ChartSeriesPoint[values.Count];
        var signal = new ChartSeriesPoint[values.Count];
        for (int index = 0; index < values.Count; index++)
        {
            ObvValuePoint value = values[index];
            obv[index] = new ChartSeriesPoint(index, value.Obv);
            signal[index] = new ChartSeriesPoint(index, value.Signal);
        }

        writer.Add(CreateContribution(ObvObjectId, obv, _obvStroke));
        writer.Add(CreateContribution(SignalObjectId, signal, _signalStroke));
    }

    public void DescribeProperties(IChartPropertyWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.Add(new ChartPropertyDescriptor(
            "signalPeriod",
            "Signal Period",
            "OBV",
            ChartPropertyValueKind.Integer,
            _signalPeriod,
            ChartChangeImpact.RecalculateModule,
            ChartPropertyStorage.Parameters,
            minimum: 1,
            maximum: 10_000));
        writer.Add(CreateColorProperty(
            ObvObjectId + ".stroke",
            "OBV Stroke",
            _obvStroke));
        writer.Add(CreateColorProperty(
            SignalObjectId + ".stroke",
            "Signal Stroke",
            _signalStroke));
    }

    public void DescribeCommands(IChartCommandWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.Add(new ChartCommandDescriptor(
            "indicator.obv.inspect",
            "Inspect OBV",
            "Indicators",
            false,
            ChartCommandPlacement.ContextMenu |
            ChartCommandPlacement.QuickToolbar |
            ChartCommandPlacement.PropertyInspector));
    }

    public ObvValuePoint[] SnapshotValues() => _runtime.SnapshotValues();

    private ChartContribution CreateContribution(
        string objectId,
        IReadOnlyList<ChartSeriesPoint> points,
        string stroke) =>
        new(
            new ChartObjectIdentity(
                Definition.ModuleId,
                InstanceId,
                objectId),
            _panelId!,
            ChartPrimitiveKind.Polyline,
            _zIndex,
            points,
            new JsonObject
            {
                ["stroke"] = stroke,
                ["strokeWidth"] = 1.5d
            });

    private static ChartPropertyDescriptor CreateColorProperty(
        string propertyId,
        string displayName,
        string value) =>
        new(
            propertyId,
            displayName,
            "Style",
            ChartPropertyValueKind.Color,
            value,
            ChartChangeImpact.RedrawOnly,
            ChartPropertyStorage.Style);

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
                return ValidatePeriod(integer, key);
            if (value.TryGetValue<long>(out long longValue) &&
                longValue >= int.MinValue && longValue <= int.MaxValue)
            {
                return ValidatePeriod((int)longValue, key);
            }
            if (value.TryGetValue<double>(out double doubleValue) &&
                double.IsFinite(doubleValue) &&
                doubleValue == Math.Truncate(doubleValue) &&
                doubleValue >= int.MinValue &&
                doubleValue <= int.MaxValue)
            {
                return ValidatePeriod((int)doubleValue, key);
            }
        }
        throw new InvalidOperationException(
            $"Parameter '{key}' must be an integer.");
    }

    private static int ValidatePeriod(int period, string key)
    {
        if (period < 1 || period > 10_000)
            throw new InvalidOperationException(
                $"OBV parameter '{key}' must be between 1 and 10000.");
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
