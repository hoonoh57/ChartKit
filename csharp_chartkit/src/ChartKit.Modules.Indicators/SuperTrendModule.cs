// <chart-module>
// Module-Id: indicator.supertrend
// Module-Class: SuperTrendModule
// Module-Category: Indicators
// Registration: registry.Register<SuperTrendModule>()
// Profile-Key: modules[].instanceId
// Data-Requirements: PrimarySymbol.OHLCV
// Capabilities: DataRequirements, Computation, Visual, Properties, Commands
// Contributions: Polyline
// Default-Panel: price.main
// Renderer-Path: ContributionSet -> SceneCompiler -> ChartRenderPlan -> SkiaChartRenderer
// UI-Path: CommandDescriptor/PropertyDescriptor -> ContextMenu/QuickButton/PropertyInspector
// Persistence: ChartModuleProfile.Parameters, ChartModuleProfile.Style
// Verification: SuperTrendModuleParityVerification
// </chart-module>

using System.Text.Json.Nodes;
using ChartKit.CSharp.Modules.Abstractions;

namespace ChartKit.CSharp.Modules.Indicators;

public sealed class SuperTrendModule :
    IChartModule,
    IChartModuleFactory<SuperTrendModule>,
    IDataRequirementProvider,
    IChartDataModule,
    IChartVisualProvider,
    IChartPropertyProvider,
    IChartCommandProvider
{
    public const string UpObjectId = "supertrend.up";
    public const string DownObjectId = "supertrend.down";

    private const int DefaultPeriod = 10;
    private const float DefaultMultiplier = 3f;
    private const string DefaultUpStroke = "#00C853";
    private const string DefaultDownStroke = "#AB47BC";

    private readonly SuperTrendSeriesRuntime _runtime =
        new(DefaultPeriod, DefaultMultiplier);
    private string? _panelId;
    private int _zIndex;
    private int _period = DefaultPeriod;
    private float _multiplier = DefaultMultiplier;
    private string _upStroke = DefaultUpStroke;
    private string _downStroke = DefaultDownStroke;
    private bool _isActive;

    private SuperTrendModule(string instanceId)
    {
        InstanceId = RequireText(instanceId, nameof(instanceId));
    }

    public static ChartModuleDefinition Definition { get; } = new(
        moduleId: "indicator.supertrend",
        displayName: "SuperTrend",
        category: "Indicators",
        description: "ATR-based trend line split into up and down segments.",
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

    public static SuperTrendModule Create(string instanceId) => new(instanceId);

    public ChartModuleDefinition ModuleDefinition => Definition;
    public string InstanceId { get; }
    public long DataVersion => _runtime.DataVersion;
    public SuperTrendRuntimeDiagnostics Diagnostics => _runtime.Diagnostics;

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
        float multiplier = ReadMultiplier(
            profile.Parameters,
            "multiplier",
            DefaultMultiplier);
        _panelId = RequireText(profile.Placement, nameof(profile.Placement));
        _zIndex = profile.ZIndex;
        _period = period;
        _multiplier = multiplier;
        _upStroke = ReadText(
            profile.Style,
            UpObjectId + ".stroke",
            DefaultUpStroke);
        _downStroke = ReadText(
            profile.Style,
            DownObjectId + ".stroke",
            DefaultDownStroke);
        _runtime.SetParameters(_period, _multiplier);
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
        _multiplier = DefaultMultiplier;
        _upStroke = DefaultUpStroke;
        _downStroke = DefaultDownStroke;
        _runtime.SetParameters(DefaultPeriod, DefaultMultiplier);
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

        IReadOnlyList<SuperTrendValuePoint> values = _runtime.Values;
        if (values.Count == 0) return;
        var up = new ChartSeriesPoint[values.Count];
        var down = new ChartSeriesPoint[values.Count];
        for (int index = 0; index < values.Count; index++)
        {
            SuperTrendValuePoint value = values[index];
            up[index] = new ChartSeriesPoint(index, value.Up);
            down[index] = new ChartSeriesPoint(index, value.Down);
        }

        writer.Add(CreateContribution(UpObjectId, up, _upStroke));
        writer.Add(CreateContribution(DownObjectId, down, _downStroke));
    }

    public void DescribeProperties(IChartPropertyWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.Add(new ChartPropertyDescriptor(
            "period",
            "ATR Period",
            "SuperTrend",
            ChartPropertyValueKind.Integer,
            _period,
            ChartChangeImpact.RecalculateModule,
            ChartPropertyStorage.Parameters,
            minimum: 1,
            maximum: 10_000));
        writer.Add(new ChartPropertyDescriptor(
            "multiplier",
            "Multiplier",
            "SuperTrend",
            ChartPropertyValueKind.Decimal,
            (double)_multiplier,
            ChartChangeImpact.RecalculateModule,
            ChartPropertyStorage.Parameters,
            minimum: 0.01d,
            maximum: 1_000d));
        writer.Add(CreateColorProperty(
            UpObjectId + ".stroke",
            "Up Stroke",
            _upStroke));
        writer.Add(CreateColorProperty(
            DownObjectId + ".stroke",
            "Down Stroke",
            _downStroke));
    }

    public void DescribeCommands(IChartCommandWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.Add(new ChartCommandDescriptor(
            "indicator.supertrend.inspect",
            "Inspect SuperTrend",
            "Indicators",
            false,
            ChartCommandPlacement.ContextMenu |
            ChartCommandPlacement.QuickToolbar |
            ChartCommandPlacement.PropertyInspector));
    }

    public SuperTrendValuePoint[] SnapshotValues() => _runtime.SnapshotValues();

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

    private static int ReadPeriod(JsonObject source, string key, int fallback)
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
                $"SuperTrend parameter '{key}' must be between 1 and 10000.");
        return period;
    }

    private static float ReadMultiplier(
        JsonObject source,
        string key,
        float fallback)
    {
        if (!source.TryGetPropertyValue(key, out JsonNode? node) || node is null)
            return fallback;
        if (node is JsonValue value)
        {
            double number;
            if (value.TryGetValue<double>(out double doubleValue)) number = doubleValue;
            else if (value.TryGetValue<float>(out float floatValue)) number = floatValue;
            else if (value.TryGetValue<int>(out int integer)) number = integer;
            else if (value.TryGetValue<long>(out long longValue)) number = longValue;
            else throw new InvalidOperationException(
                $"Parameter '{key}' must be numeric.");

            if (!double.IsFinite(number) || number < 0.01d || number > 1_000d)
                throw new InvalidOperationException(
                    $"SuperTrend parameter '{key}' must be between 0.01 and 1000.");
            return (float)number;
        }
        throw new InvalidOperationException(
            $"Parameter '{key}' must be numeric.");
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
