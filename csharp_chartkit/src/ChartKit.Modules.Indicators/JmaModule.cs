// <chart-module>
// Module-Id: indicator.jma
// Module-Class: JmaModule
// Module-Category: Indicators
// Registration: registry.Register<JmaModule>()
// Profile-Key: modules[].instanceId
// Data-Requirements: PrimarySymbol.OHLCV
// Capabilities: DataRequirements, Computation, Visual, Properties, Commands
// Contributions: Polyline
// Default-Panel: price.main
// Renderer-Path: ContributionSet -> SceneCompiler -> ChartRenderPlan -> SkiaChartRenderer
// UI-Path: CommandDescriptor/PropertyDescriptor -> ContextMenu/QuickButton/PropertyInspector
// Persistence: ChartModuleProfile.Parameters, ChartModuleProfile.Style
// Verification: JmaModuleParityVerification
// </chart-module>

using System.Text.Json.Nodes;
using ChartKit.CSharp.Modules.Abstractions;

namespace ChartKit.CSharp.Modules.Indicators;

public sealed class JmaModule :
    IChartModule,
    IChartModuleFactory<JmaModule>,
    IDataRequirementProvider,
    IChartDataModule,
    IChartVisualProvider,
    IChartPropertyProvider,
    IChartCommandProvider
{
    public const string UpObjectId = "jma.up";
    public const string DownObjectId = "jma.down";

    private const int DefaultPeriod = 14;
    private const int DefaultPhase = 50;
    private const int DefaultPower = 2;
    private const string DefaultUpStroke = "#AB47BC";
    private const string DefaultDownStroke = "#00C853";

    private readonly JmaSeriesRuntime _runtime =
        new(DefaultPeriod, DefaultPhase, DefaultPower);
    private string? _panelId;
    private int _zIndex;
    private int _period = DefaultPeriod;
    private int _phase = DefaultPhase;
    private int _power = DefaultPower;
    private string _upStroke = DefaultUpStroke;
    private string _downStroke = DefaultDownStroke;
    private bool _isActive;

    private JmaModule(string instanceId)
    {
        InstanceId = RequireText(instanceId, nameof(instanceId));
    }

    public static ChartModuleDefinition Definition { get; } = new(
        moduleId: "indicator.jma",
        displayName: "JMA",
        category: "Indicators",
        description: "Jurik-style moving average split into rising and falling segments.",
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

    public static JmaModule Create(string instanceId) => new(instanceId);

    public ChartModuleDefinition ModuleDefinition => Definition;
    public string InstanceId { get; }
    public long DataVersion => _runtime.DataVersion;
    public JmaRuntimeDiagnostics Diagnostics => _runtime.Diagnostics;

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

        int period = ReadInteger(
            profile.Parameters,
            "period",
            DefaultPeriod,
            1,
            10_000);
        int phase = ReadInteger(
            profile.Parameters,
            "phase",
            DefaultPhase,
            -100,
            100);
        int power = ReadInteger(
            profile.Parameters,
            "power",
            DefaultPower,
            1,
            10_000);

        _panelId = RequireText(profile.Placement, nameof(profile.Placement));
        _zIndex = profile.ZIndex;
        _period = period;
        _phase = phase;
        _power = power;
        _upStroke = ReadText(
            profile.Style,
            UpObjectId + ".stroke",
            DefaultUpStroke);
        _downStroke = ReadText(
            profile.Style,
            DownObjectId + ".stroke",
            DefaultDownStroke);
        _runtime.SetParameters(_period, _phase, _power);
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
        _phase = DefaultPhase;
        _power = DefaultPower;
        _upStroke = DefaultUpStroke;
        _downStroke = DefaultDownStroke;
        _runtime.SetParameters(DefaultPeriod, DefaultPhase, DefaultPower);
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

        IReadOnlyList<JmaValuePoint> values = _runtime.Values;
        if (values.Count == 0) return;
        var up = new ChartSeriesPoint[values.Count];
        var down = new ChartSeriesPoint[values.Count];
        for (int index = 0; index < values.Count; index++)
        {
            JmaValuePoint value = values[index];
            up[index] = new ChartSeriesPoint(index, value.Up);
            down[index] = new ChartSeriesPoint(index, value.Down);
        }

        writer.Add(CreateContribution(UpObjectId, up, _upStroke));
        writer.Add(CreateContribution(DownObjectId, down, _downStroke));
    }

    public void DescribeProperties(IChartPropertyWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.Add(CreateIntegerProperty(
            "period",
            "Period",
            _period,
            1,
            10_000));
        writer.Add(CreateIntegerProperty(
            "phase",
            "Phase",
            _phase,
            -100,
            100));
        writer.Add(CreateIntegerProperty(
            "power",
            "Power",
            _power,
            1,
            10_000));
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
            "indicator.jma.inspect",
            "Inspect JMA",
            "Indicators",
            false,
            ChartCommandPlacement.ContextMenu |
            ChartCommandPlacement.QuickToolbar |
            ChartCommandPlacement.PropertyInspector));
    }

    public JmaValuePoint[] SnapshotValues() => _runtime.SnapshotValues();

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

    private static ChartPropertyDescriptor CreateIntegerProperty(
        string propertyId,
        string displayName,
        int value,
        int minimum,
        int maximum) =>
        new(
            propertyId,
            displayName,
            "JMA",
            ChartPropertyValueKind.Integer,
            value,
            ChartChangeImpact.RecalculateModule,
            ChartPropertyStorage.Parameters,
            minimum,
            maximum);

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

    private static int ReadInteger(
        JsonObject source,
        string key,
        int fallback,
        int minimum,
        int maximum)
    {
        if (!source.TryGetPropertyValue(key, out JsonNode? node) || node is null)
            return fallback;
        if (node is JsonValue value)
        {
            int result;
            if (value.TryGetValue<int>(out int integer)) result = integer;
            else if (value.TryGetValue<long>(out long longValue) &&
                     longValue >= int.MinValue && longValue <= int.MaxValue)
            {
                result = (int)longValue;
            }
            else if (value.TryGetValue<double>(out double doubleValue) &&
                     double.IsFinite(doubleValue) &&
                     doubleValue == Math.Truncate(doubleValue) &&
                     doubleValue >= int.MinValue &&
                     doubleValue <= int.MaxValue)
            {
                result = (int)doubleValue;
            }
            else
            {
                throw new InvalidOperationException(
                    $"Parameter '{key}' must be an integer.");
            }

            if (result < minimum || result > maximum)
                throw new InvalidOperationException(
                    $"JMA parameter '{key}' must be between {minimum} and {maximum}.");
            return result;
        }
        throw new InvalidOperationException(
            $"Parameter '{key}' must be an integer.");
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
