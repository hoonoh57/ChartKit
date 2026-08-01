// <chart-module>
// Module-Id: indicator.disparity
// Module-Class: DisparityModule
// Module-Category: Indicators
// Registration: registry.Register<DisparityModule>()
// Profile-Key: modules[].instanceId
// Data-Requirements: PrimarySymbol.OHLCV
// Capabilities: DataRequirements, Computation, Visual, Properties, Commands
// Contributions: Polyline
// Default-Panel: indicator.6
// Renderer-Path: ContributionSet -> SceneCompiler -> ChartRenderPlan -> SkiaChartRenderer
// UI-Path: CommandDescriptor/PropertyDescriptor -> ContextMenu/QuickButton/PropertyInspector
// Persistence: ChartModuleProfile.Parameters, ChartModuleProfile.Style
// Verification: DisparityModuleParityVerification
// </chart-module>

using System.Text.Json.Nodes;
using ChartKit.CSharp.Modules.Abstractions;

namespace ChartKit.CSharp.Modules.Indicators;

public sealed class DisparityModule :
    IChartModule,
    IChartModuleFactory<DisparityModule>,
    IDataRequirementProvider,
    IChartDataModule,
    IChartVisualProvider,
    IChartPropertyProvider,
    IChartCommandProvider
{
    public const string ValueObjectId = "disparity.value";
    public const string UpperObjectId = "disparity.upper";
    public const string BaselineObjectId = "disparity.baseline";
    public const string LowerObjectId = "disparity.lower";

    private const int DefaultPeriod = 20;
    private const double DefaultUpper = 105d;
    private const double DefaultBaseline = 100d;
    private const double DefaultLower = 95d;
    private const string DefaultValueStroke = "#00BFA5";
    private const string DefaultUpperStroke = "#FFEB3B";
    private const string DefaultBaselineStroke = "#7E57C2";
    private const string DefaultLowerStroke = "#FFC107";

    private readonly DisparitySeriesRuntime _runtime = new(DefaultPeriod);
    private string? _panelId;
    private int _zIndex;
    private int _period = DefaultPeriod;
    private double _upper = DefaultUpper;
    private double _baseline = DefaultBaseline;
    private double _lower = DefaultLower;
    private string _valueStroke = DefaultValueStroke;
    private string _upperStroke = DefaultUpperStroke;
    private string _baselineStroke = DefaultBaselineStroke;
    private string _lowerStroke = DefaultLowerStroke;
    private bool _isActive;

    private DisparityModule(string instanceId)
    {
        InstanceId = RequireText(instanceId, nameof(instanceId));
    }

    public static ChartModuleDefinition Definition { get; } = new(
        moduleId: "indicator.disparity",
        displayName: "Disparity",
        category: "Indicators",
        description: "Close-to-SMA disparity ratio with configurable reference levels.",
        defaultPanelId: "indicator.6",
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

    public static DisparityModule Create(string instanceId) => new(instanceId);

    public ChartModuleDefinition ModuleDefinition => Definition;
    public string InstanceId { get; }
    public long DataVersion => _runtime.DataVersion;
    public DisparityRuntimeDiagnostics Diagnostics => _runtime.Diagnostics;

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

        int period = ReadPeriod(profile.Parameters, "period", DefaultPeriod);
        double upper = ReadLevel(profile.Parameters, "upper", DefaultUpper);
        double baseline = ReadLevel(
            profile.Parameters,
            "baseline",
            DefaultBaseline);
        double lower = ReadLevel(profile.Parameters, "lower", DefaultLower);
        ValidateLevelOrder(upper, baseline, lower);

        _period = period;
        _upper = upper;
        _baseline = baseline;
        _lower = lower;
        _panelId = RequireText(profile.Placement, nameof(profile.Placement));
        _zIndex = profile.ZIndex;
        _valueStroke = ReadText(
            profile.Style,
            ValueObjectId + ".stroke",
            DefaultValueStroke);
        _upperStroke = ReadText(
            profile.Style,
            UpperObjectId + ".stroke",
            DefaultUpperStroke);
        _baselineStroke = ReadText(
            profile.Style,
            BaselineObjectId + ".stroke",
            DefaultBaselineStroke);
        _lowerStroke = ReadText(
            profile.Style,
            LowerObjectId + ".stroke",
            DefaultLowerStroke);
        _runtime.SetPeriod(_period);
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
        _period = DefaultPeriod;
        _upper = DefaultUpper;
        _baseline = DefaultBaseline;
        _lower = DefaultLower;
        _valueStroke = DefaultValueStroke;
        _upperStroke = DefaultUpperStroke;
        _baselineStroke = DefaultBaselineStroke;
        _lowerStroke = DefaultLowerStroke;
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

        IReadOnlyList<DisparityValuePoint> values = _runtime.Values;
        if (values.Count == 0) return;

        var disparity = new ChartSeriesPoint[values.Count];
        var upper = new ChartSeriesPoint[values.Count];
        var baseline = new ChartSeriesPoint[values.Count];
        var lower = new ChartSeriesPoint[values.Count];
        for (int index = 0; index < values.Count; index++)
        {
            disparity[index] = new ChartSeriesPoint(index, values[index].Value);
            upper[index] = new ChartSeriesPoint(index, _upper);
            baseline[index] = new ChartSeriesPoint(index, _baseline);
            lower[index] = new ChartSeriesPoint(index, _lower);
        }

        writer.Add(CreateContribution(
            ValueObjectId,
            disparity,
            _valueStroke,
            1.5d));
        writer.Add(CreateContribution(
            UpperObjectId,
            upper,
            _upperStroke,
            1d));
        writer.Add(CreateContribution(
            BaselineObjectId,
            baseline,
            _baselineStroke,
            1d));
        writer.Add(CreateContribution(
            LowerObjectId,
            lower,
            _lowerStroke,
            1d));
    }

    public void DescribeProperties(IChartPropertyWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.Add(new ChartPropertyDescriptor(
            "period",
            "Period",
            "Disparity",
            ChartPropertyValueKind.Integer,
            _period,
            ChartChangeImpact.RecalculateModule,
            ChartPropertyStorage.Parameters,
            minimum: 1,
            maximum: 10_000));
        writer.Add(CreateLevelProperty("upper", "Upper", _upper));
        writer.Add(CreateLevelProperty("baseline", "Baseline", _baseline));
        writer.Add(CreateLevelProperty("lower", "Lower", _lower));
        writer.Add(CreateColorProperty(
            ValueObjectId + ".stroke",
            "Value Stroke",
            _valueStroke));
        writer.Add(CreateColorProperty(
            UpperObjectId + ".stroke",
            "Upper Stroke",
            _upperStroke));
        writer.Add(CreateColorProperty(
            BaselineObjectId + ".stroke",
            "Baseline Stroke",
            _baselineStroke));
        writer.Add(CreateColorProperty(
            LowerObjectId + ".stroke",
            "Lower Stroke",
            _lowerStroke));
    }

    public void DescribeCommands(IChartCommandWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.Add(new ChartCommandDescriptor(
            "indicator.disparity.inspect",
            "Inspect Disparity",
            "Indicators",
            false,
            ChartCommandPlacement.ContextMenu |
            ChartCommandPlacement.QuickToolbar |
            ChartCommandPlacement.PropertyInspector));
    }

    public DisparityValuePoint[] SnapshotValues() => _runtime.SnapshotValues();

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

    private static ChartPropertyDescriptor CreateLevelProperty(
        string propertyId,
        string displayName,
        double value) =>
        new(
            propertyId,
            displayName,
            "Levels",
            ChartPropertyValueKind.Decimal,
            value,
            ChartChangeImpact.RebuildVisuals,
            ChartPropertyStorage.Parameters,
            minimum: 0,
            maximum: 1_000);

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
                $"Disparity parameter '{key}' must be between 1 and 10000.");
        return period;
    }

    private static double ReadLevel(
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

        if (!double.IsFinite(result) || result < 0d || result > 1_000d)
            throw new InvalidOperationException(
                $"Disparity parameter '{key}' must be between 0 and 1000.");
        return result;
    }

    private static void ValidateLevelOrder(
        double upper,
        double baseline,
        double lower)
    {
        if (!(upper > baseline && baseline > lower))
            throw new InvalidOperationException(
                "Disparity levels must satisfy upper > baseline > lower.");
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
