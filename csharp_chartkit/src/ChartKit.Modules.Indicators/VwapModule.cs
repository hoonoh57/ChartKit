// <chart-module>
// Module-Id: indicator.vwap
// Module-Class: VwapModule
// Module-Category: Indicators
// Registration: registry.Register<VwapModule>()
// Profile-Key: modules[].instanceId
// Data-Requirements: PrimarySymbol.OHLCV + TradingDate
// Capabilities: DataRequirements, Computation, Visual, Properties, Commands
// Contributions: Polyline
// Default-Panel: price.main
// Renderer-Path: ContributionSet -> SceneCompiler -> ChartRenderPlan -> SkiaChartRenderer
// UI-Path: CommandDescriptor/PropertyDescriptor -> ContextMenu/QuickButton/PropertyInspector
// Persistence: ChartModuleProfile.Parameters, ChartModuleProfile.Style
// Verification: VwapModuleParityVerification
// </chart-module>

using System.Text.Json.Nodes;
using ChartKit.CSharp.Modules.Abstractions;

namespace ChartKit.CSharp.Modules.Indicators;

public sealed class VwapModule :
    IChartModule,
    IChartModuleFactory<VwapModule>,
    IDataRequirementProvider,
    IChartDataModule,
    IChartVisualProvider,
    IChartPropertyProvider,
    IChartCommandProvider
{
    public const string ValueObjectId = "vwap.value";
    public const string Upper1ObjectId = "vwap.upper1";
    public const string Lower1ObjectId = "vwap.lower1";
    public const string Upper2ObjectId = "vwap.upper2";
    public const string Lower2ObjectId = "vwap.lower2";

    private const double DefaultStdDev1 = 1d;
    private const double DefaultStdDev2 = 2d;
    private const string DefaultValueStroke = "#00E5FF";
    private const string DefaultUpper1Stroke = "#FFEB3B";
    private const string DefaultLower1Stroke = "#FFEB3B";
    private const string DefaultUpper2Stroke = "#FF7043";
    private const string DefaultLower2Stroke = "#FF7043";

    private readonly VwapSeriesRuntime _runtime =
        new(DefaultStdDev1, DefaultStdDev2);
    private string? _panelId;
    private int _zIndex;
    private double _stdDev1 = DefaultStdDev1;
    private double _stdDev2 = DefaultStdDev2;
    private string _valueStroke = DefaultValueStroke;
    private string _upper1Stroke = DefaultUpper1Stroke;
    private string _lower1Stroke = DefaultLower1Stroke;
    private string _upper2Stroke = DefaultUpper2Stroke;
    private string _lower2Stroke = DefaultLower2Stroke;
    private bool _isActive;

    private VwapModule(string instanceId)
    {
        InstanceId = RequireText(instanceId, nameof(instanceId));
    }

    public static ChartModuleDefinition Definition { get; } = new(
        moduleId: "indicator.vwap",
        displayName: "VWAP",
        category: "Indicators",
        description: "Session VWAP with two volume-weighted standard-deviation bands.",
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

    public static VwapModule Create(string instanceId) => new(instanceId);

    public ChartModuleDefinition ModuleDefinition => Definition;
    public string InstanceId { get; }
    public long DataVersion => _runtime.DataVersion;
    public VwapRuntimeDiagnostics Diagnostics => _runtime.Diagnostics;

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

        _stdDev1 = ReadStdDev(
            profile.Parameters,
            "stdDev1",
            DefaultStdDev1);
        _stdDev2 = ReadStdDev(
            profile.Parameters,
            "stdDev2",
            DefaultStdDev2);
        _panelId = RequireText(profile.Placement, nameof(profile.Placement));
        _zIndex = profile.ZIndex;
        _valueStroke = ReadText(
            profile.Style,
            ValueObjectId + ".stroke",
            DefaultValueStroke);
        _upper1Stroke = ReadText(
            profile.Style,
            Upper1ObjectId + ".stroke",
            DefaultUpper1Stroke);
        _lower1Stroke = ReadText(
            profile.Style,
            Lower1ObjectId + ".stroke",
            DefaultLower1Stroke);
        _upper2Stroke = ReadText(
            profile.Style,
            Upper2ObjectId + ".stroke",
            DefaultUpper2Stroke);
        _lower2Stroke = ReadText(
            profile.Style,
            Lower2ObjectId + ".stroke",
            DefaultLower2Stroke);
        _runtime.SetParameters(_stdDev1, _stdDev2);
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
        _stdDev1 = DefaultStdDev1;
        _stdDev2 = DefaultStdDev2;
        _valueStroke = DefaultValueStroke;
        _upper1Stroke = DefaultUpper1Stroke;
        _lower1Stroke = DefaultLower1Stroke;
        _upper2Stroke = DefaultUpper2Stroke;
        _lower2Stroke = DefaultLower2Stroke;
        _runtime.SetParameters(DefaultStdDev1, DefaultStdDev2);
        _runtime.Reset();
    }

    public void DescribeRequirements(IDataRequirementWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.Add(new ChartDataRequirement(
            "primary",
            "current",
            "current",
            "OHLCV+TradingDate"));
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

        IReadOnlyList<VwapValuePoint> values = _runtime.Values;
        if (values.Count == 0) return;

        var value = new ChartSeriesPoint[values.Count];
        var upper1 = new ChartSeriesPoint[values.Count];
        var lower1 = new ChartSeriesPoint[values.Count];
        var upper2 = new ChartSeriesPoint[values.Count];
        var lower2 = new ChartSeriesPoint[values.Count];
        for (int index = 0; index < values.Count; index++)
        {
            value[index] = new ChartSeriesPoint(index, values[index].Value);
            upper1[index] = new ChartSeriesPoint(index, values[index].Upper1);
            lower1[index] = new ChartSeriesPoint(index, values[index].Lower1);
            upper2[index] = new ChartSeriesPoint(index, values[index].Upper2);
            lower2[index] = new ChartSeriesPoint(index, values[index].Lower2);
        }

        writer.Add(CreateContribution(
            ValueObjectId,
            value,
            _valueStroke,
            1.75d));
        writer.Add(CreateContribution(
            Upper1ObjectId,
            upper1,
            _upper1Stroke,
            1d));
        writer.Add(CreateContribution(
            Lower1ObjectId,
            lower1,
            _lower1Stroke,
            1d));
        writer.Add(CreateContribution(
            Upper2ObjectId,
            upper2,
            _upper2Stroke,
            1d));
        writer.Add(CreateContribution(
            Lower2ObjectId,
            lower2,
            _lower2Stroke,
            1d));
    }

    public void DescribeProperties(IChartPropertyWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.Add(CreateStdDevProperty("stdDev1", "StdDev 1", _stdDev1));
        writer.Add(CreateStdDevProperty("stdDev2", "StdDev 2", _stdDev2));
        writer.Add(CreateColorProperty(
            ValueObjectId + ".stroke",
            "VWAP Stroke",
            _valueStroke));
        writer.Add(CreateColorProperty(
            Upper1ObjectId + ".stroke",
            "Upper 1 Stroke",
            _upper1Stroke));
        writer.Add(CreateColorProperty(
            Lower1ObjectId + ".stroke",
            "Lower 1 Stroke",
            _lower1Stroke));
        writer.Add(CreateColorProperty(
            Upper2ObjectId + ".stroke",
            "Upper 2 Stroke",
            _upper2Stroke));
        writer.Add(CreateColorProperty(
            Lower2ObjectId + ".stroke",
            "Lower 2 Stroke",
            _lower2Stroke));
    }

    public void DescribeCommands(IChartCommandWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.Add(new ChartCommandDescriptor(
            "indicator.vwap.inspect",
            "Inspect VWAP",
            "Indicators",
            false,
            ChartCommandPlacement.ContextMenu |
            ChartCommandPlacement.QuickToolbar |
            ChartCommandPlacement.PropertyInspector));
    }

    public VwapValuePoint[] SnapshotValues() => _runtime.SnapshotValues();

    private ChartContribution CreateContribution(
        string objectId,
        IReadOnlyList<ChartSeriesPoint> points,
        string stroke,
        double strokeWidth) =>
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
                ["strokeWidth"] = strokeWidth
            });

    private static ChartPropertyDescriptor CreateStdDevProperty(
        string propertyId,
        string displayName,
        double value) =>
        new(
            propertyId,
            displayName,
            "VWAP",
            ChartPropertyValueKind.Decimal,
            value,
            ChartChangeImpact.RecalculateModule,
            ChartPropertyStorage.Parameters,
            minimum: 0,
            maximum: 100);

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

    private static double ReadStdDev(
        JsonObject source,
        string key,
        double fallback)
    {
        if (!source.TryGetPropertyValue(key, out JsonNode? node) || node is null)
            return fallback;

        double result;
        if (node is JsonValue value && value.TryGetValue<double>(out double number))
            result = number;
        else if (node is JsonValue integer && integer.TryGetValue<int>(out int intValue))
            result = intValue;
        else
            throw new InvalidOperationException(
                $"Parameter '{key}' must be numeric.");

        if (!double.IsFinite(result) || result < 0d || result > 100d)
            throw new InvalidOperationException(
                $"VWAP parameter '{key}' must be between 0 and 100.");
        return result;
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
