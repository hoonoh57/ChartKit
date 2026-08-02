// <chart-module>
// Module-Id: indicator.rsi
// Module-Class: RsiModule
// Module-Category: Indicators
// Registration: registry.Register<RsiModule>()
// Profile-Key: modules[].instanceId
// Data-Requirements: PrimarySymbol.OHLCV
// Capabilities: DataRequirements, Computation, Visual, Properties, Commands
// Contributions: Polyline
// Default-Panel: indicator.1
// Renderer-Path: ContributionSet -> SceneCompiler -> ChartRenderPlan -> SkiaChartRenderer
// UI-Path: CommandDescriptor/PropertyDescriptor -> ContextMenu/QuickButton/PropertyInspector
// Persistence: ChartModuleProfile.Parameters, ChartModuleProfile.Style
// Verification: RsiModuleParityVerification
// </chart-module>

using System.Text.Json.Nodes;
using ChartKit.CSharp.Modules.Abstractions;

namespace ChartKit.CSharp.Modules.Indicators;

public sealed class RsiModule :
    IChartModule,
    IChartModuleFactory<RsiModule>,
    IDataRequirementProvider,
    IChartDataModule,
    IChartVisualProvider,
    IChartPropertyProvider,
    IChartCommandProvider
{
    public const string RsiObjectId = "rsi.value";
    public const string SignalObjectId = "rsi.signal";
    public const string UpperObjectId = "rsi.upper";
    public const string LowerObjectId = "rsi.lower";

    private const int DefaultPeriod = 14;
    private const int DefaultSignalPeriod = 9;
    private const double DefaultUpper = 70d;
    private const double DefaultLower = 30d;
    private const string DefaultRsiStroke = "#FF9800";
    private const string DefaultSignalStroke = "#E91E63";
    private const string DefaultUpperStroke = "#00BCD4";
    private const string DefaultLowerStroke = "#CDDC39";

    private readonly RsiSeriesRuntime _runtime =
        new(DefaultPeriod, DefaultSignalPeriod);
    private string? _panelId;
    private int _zIndex;
    private int _period = DefaultPeriod;
    private int _signalPeriod = DefaultSignalPeriod;
    private double _upper = DefaultUpper;
    private double _lower = DefaultLower;
    private string _rsiStroke = DefaultRsiStroke;
    private string _signalStroke = DefaultSignalStroke;
    private string _upperStroke = DefaultUpperStroke;
    private string _lowerStroke = DefaultLowerStroke;
    private bool _isActive;

    private RsiModule(string instanceId)
    {
        InstanceId = RequireText(instanceId, nameof(instanceId));
    }

    public static ChartModuleDefinition Definition { get; } =
        new(
            moduleId: "indicator.rsi",
            displayName: "RSI",
            category: "Indicators",
            description: "Wilder RSI with a simple moving-average signal line.",
            defaultPanelId: "indicator.1",
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

    public static RsiModule Create(string instanceId) => new(instanceId);

    public ChartModuleDefinition ModuleDefinition => Definition;
    public string InstanceId { get; }
    public long DataVersion => _runtime.DataVersion;
    public RsiRuntimeDiagnostics Diagnostics => _runtime.Diagnostics;

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

        int period = ReadPeriod(profile.Parameters, "period", DefaultPeriod);
        int signalPeriod = ReadPeriod(
            profile.Parameters,
            "signalPeriod",
            DefaultSignalPeriod);
        double upper = ReadThreshold(profile.Parameters, "upper", DefaultUpper);
        double lower = ReadThreshold(profile.Parameters, "lower", DefaultLower);
        if (upper <= lower)
            throw new InvalidOperationException(
                "RSI upper threshold must be greater than lower threshold.");

        _panelId = RequireText(profile.Placement, nameof(profile.Placement));
        _zIndex = profile.ZIndex;
        _period = period;
        _signalPeriod = signalPeriod;
        _upper = upper;
        _lower = lower;
        _rsiStroke = ReadText(
            profile.Style,
            RsiObjectId + ".stroke",
            DefaultRsiStroke);
        _signalStroke = ReadText(
            profile.Style,
            SignalObjectId + ".stroke",
            DefaultSignalStroke);
        _upperStroke = ReadText(
            profile.Style,
            UpperObjectId + ".stroke",
            DefaultUpperStroke);
        _lowerStroke = ReadText(
            profile.Style,
            LowerObjectId + ".stroke",
            DefaultLowerStroke);
        _runtime.SetParameters(_period, _signalPeriod);
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
        _signalPeriod = DefaultSignalPeriod;
        _upper = DefaultUpper;
        _lower = DefaultLower;
        _rsiStroke = DefaultRsiStroke;
        _signalStroke = DefaultSignalStroke;
        _upperStroke = DefaultUpperStroke;
        _lowerStroke = DefaultLowerStroke;
        _runtime.SetParameters(DefaultPeriod, DefaultSignalPeriod);
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

        IReadOnlyList<RsiValuePoint> values = _runtime.Values;
        if (values.Count == 0) return;

        var rsiPoints = new ChartSeriesPoint[values.Count];
        var signalPoints = new ChartSeriesPoint[values.Count];
        var upperPoints = new ChartSeriesPoint[values.Count];
        var lowerPoints = new ChartSeriesPoint[values.Count];
        for (int index = 0; index < values.Count; index++)
        {
            RsiValuePoint value = values[index];
            rsiPoints[index] = new ChartSeriesPoint(index, value.Rsi);
            signalPoints[index] = new ChartSeriesPoint(index, value.Signal);
            upperPoints[index] = new ChartSeriesPoint(index, _upper);
            lowerPoints[index] = new ChartSeriesPoint(index, _lower);
        }

        writer.Add(CreateContribution(
            RsiObjectId,
            rsiPoints,
            _rsiStroke,
            1.5d));
        writer.Add(CreateContribution(
            SignalObjectId,
            signalPoints,
            _signalStroke,
            1.5d));
        writer.Add(CreateContribution(
            UpperObjectId,
            upperPoints,
            _upperStroke,
            1d));
        writer.Add(CreateContribution(
            LowerObjectId,
            lowerPoints,
            _lowerStroke,
            1d));
    }

    public void DescribeProperties(IChartPropertyWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.Add(new ChartPropertyDescriptor(
            "period",
            "Period",
            "RSI",
            ChartPropertyValueKind.Integer,
            _period,
            ChartChangeImpact.RecalculateModule,
            ChartPropertyStorage.Parameters,
            minimum: 1,
            maximum: 10_000));
        writer.Add(new ChartPropertyDescriptor(
            "signalPeriod",
            "Signal Period",
            "RSI",
            ChartPropertyValueKind.Integer,
            _signalPeriod,
            ChartChangeImpact.RecalculateModule,
            ChartPropertyStorage.Parameters,
            minimum: 1,
            maximum: 10_000));
        writer.Add(new ChartPropertyDescriptor(
            "upper",
            "Upper",
            "Levels",
            ChartPropertyValueKind.Decimal,
            _upper,
            ChartChangeImpact.RebuildVisuals,
            ChartPropertyStorage.Parameters,
            minimum: 0,
            maximum: 100));
        writer.Add(new ChartPropertyDescriptor(
            "lower",
            "Lower",
            "Levels",
            ChartPropertyValueKind.Decimal,
            _lower,
            ChartChangeImpact.RebuildVisuals,
            ChartPropertyStorage.Parameters,
            minimum: 0,
            maximum: 100));
        writer.Add(CreateColorProperty(
            RsiObjectId + ".stroke",
            "RSI Stroke",
            _rsiStroke));
        writer.Add(CreateColorProperty(
            SignalObjectId + ".stroke",
            "Signal Stroke",
            _signalStroke));
        writer.Add(CreateColorProperty(
            UpperObjectId + ".stroke",
            "Upper Stroke",
            _upperStroke));
        writer.Add(CreateColorProperty(
            LowerObjectId + ".stroke",
            "Lower Stroke",
            _lowerStroke));
    }

    public void DescribeCommands(IChartCommandWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.Add(new ChartCommandDescriptor(
            "indicator.rsi.inspect",
            "Inspect RSI",
            "Indicators",
            false,
            ChartCommandPlacement.ContextMenu |
            ChartCommandPlacement.QuickToolbar |
            ChartCommandPlacement.PropertyInspector));
    }

    public RsiValuePoint[] SnapshotValues() => _runtime.SnapshotValues();

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
                $"RSI parameter '{key}' must be between 1 and 10000.");
        return period;
    }

    private static double ReadThreshold(
        JsonObject source,
        string key,
        double fallback)
    {
        if (!source.TryGetPropertyValue(key, out JsonNode? node) || node is null)
            return fallback;

        double result;
        if (node is JsonValue value && value.TryGetValue(out double doubleValue))
            result = doubleValue;
        else if (node is JsonValue integer && integer.TryGetValue(out int intValue))
            result = intValue;
        else
            throw new InvalidOperationException(
                $"Parameter '{key}' must be numeric.");

        if (!double.IsFinite(result) || result < 0d || result > 100d)
            throw new InvalidOperationException(
                $"RSI parameter '{key}' must be between 0 and 100.");
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
