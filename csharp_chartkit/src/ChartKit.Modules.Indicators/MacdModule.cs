// <chart-module>
// Module-Id: indicator.macd
// Module-Class: MacdModule
// Module-Category: Indicators
// Registration: registry.Register<MacdModule>()
// Profile-Key: modules[].instanceId
// Data-Requirements: PrimarySymbol.OHLCV
// Capabilities: DataRequirements, Computation, Visual, Properties, Commands
// Contributions: Polyline, Histogram
// Default-Panel: indicator.4
// Renderer-Path: ContributionSet -> SceneCompiler -> ChartRenderPlan -> SkiaChartRenderer
// UI-Path: CommandDescriptor/PropertyDescriptor -> ContextMenu/QuickButton/PropertyInspector
// Persistence: ChartModuleProfile.Parameters, ChartModuleProfile.Style
// Verification: MacdModuleParityVerification
// </chart-module>

using System.Text.Json.Nodes;
using ChartKit.CSharp.Modules.Abstractions;

namespace ChartKit.CSharp.Modules.Indicators;

public sealed class MacdModule :
    IChartModule,
    IChartModuleFactory<MacdModule>,
    IDataRequirementProvider,
    IChartDataModule,
    IChartVisualProvider,
    IChartPropertyProvider,
    IChartCommandProvider
{
    public const string MacdObjectId = "macd.value";
    public const string SignalObjectId = "macd.signal";
    public const string HistogramObjectId = "macd.histogram";

    private const int DefaultFast = 12;
    private const int DefaultSlow = 26;
    private const int DefaultSignal = 9;
    private const string DefaultMacdStroke = "#795548";
    private const string DefaultSignalStroke = "#009688";
    private const string DefaultHistogramStroke = "#FFEB3B";

    private readonly MacdSeriesRuntime _runtime =
        new(DefaultFast, DefaultSlow, DefaultSignal);
    private string? _panelId;
    private int _zIndex;
    private int _fastPeriod = DefaultFast;
    private int _slowPeriod = DefaultSlow;
    private int _signalPeriod = DefaultSignal;
    private string _macdStroke = DefaultMacdStroke;
    private string _signalStroke = DefaultSignalStroke;
    private string _histogramStroke = DefaultHistogramStroke;
    private bool _isActive;

    private MacdModule(string instanceId)
    {
        InstanceId = RequireText(instanceId, nameof(instanceId));
    }

    public static ChartModuleDefinition Definition { get; } = new(
        moduleId: "indicator.macd",
        displayName: "MACD",
        category: "Indicators",
        description: "Moving Average Convergence Divergence with signal and histogram.",
        defaultPanelId: "indicator.4",
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
            ChartPrimitiveKind.Polyline,
            ChartPrimitiveKind.Histogram
        ]);

    public static MacdModule Create(string instanceId) => new(instanceId);

    public ChartModuleDefinition ModuleDefinition => Definition;
    public string InstanceId { get; }
    public long DataVersion => _runtime.DataVersion;
    public MacdRuntimeDiagnostics Diagnostics => _runtime.Diagnostics;

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

        int fast = ReadPeriod(profile.Parameters, "fastPeriod", DefaultFast);
        int slow = ReadPeriod(profile.Parameters, "slowPeriod", DefaultSlow);
        int signal = ReadPeriod(profile.Parameters, "signalPeriod", DefaultSignal);
        if (slow <= fast)
            throw new InvalidOperationException(
                "MACD slow period must be greater than fast period.");

        _panelId = RequireText(profile.Placement, nameof(profile.Placement));
        _zIndex = profile.ZIndex;
        _fastPeriod = fast;
        _slowPeriod = slow;
        _signalPeriod = signal;
        _macdStroke = ReadText(profile.Style, MacdObjectId + ".stroke", DefaultMacdStroke);
        _signalStroke = ReadText(profile.Style, SignalObjectId + ".stroke", DefaultSignalStroke);
        _histogramStroke = ReadText(
            profile.Style,
            HistogramObjectId + ".stroke",
            DefaultHistogramStroke);
        _runtime.SetParameters(_fastPeriod, _slowPeriod, _signalPeriod);
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
        _fastPeriod = DefaultFast;
        _slowPeriod = DefaultSlow;
        _signalPeriod = DefaultSignal;
        _macdStroke = DefaultMacdStroke;
        _signalStroke = DefaultSignalStroke;
        _histogramStroke = DefaultHistogramStroke;
        _runtime.SetParameters(DefaultFast, DefaultSlow, DefaultSignal);
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

        IReadOnlyList<MacdValuePoint> values = _runtime.Values;
        if (values.Count == 0) return;

        var macd = new ChartSeriesPoint[values.Count];
        var signal = new ChartSeriesPoint[values.Count];
        var histogram = new ChartSeriesPoint[values.Count];
        for (int index = 0; index < values.Count; index++)
        {
            MacdValuePoint value = values[index];
            macd[index] = new ChartSeriesPoint(index, value.Macd);
            signal[index] = new ChartSeriesPoint(index, value.Signal);
            histogram[index] = new ChartSeriesPoint(index, value.Histogram);
        }

        writer.Add(CreateContribution(
            MacdObjectId,
            ChartPrimitiveKind.Polyline,
            macd,
            _macdStroke,
            1.5d));
        writer.Add(CreateContribution(
            SignalObjectId,
            ChartPrimitiveKind.Polyline,
            signal,
            _signalStroke,
            1.5d));
        writer.Add(CreateContribution(
            HistogramObjectId,
            ChartPrimitiveKind.Histogram,
            histogram,
            _histogramStroke,
            1d));
    }

    public void DescribeProperties(IChartPropertyWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.Add(CreatePeriodProperty("fastPeriod", "Fast Period", _fastPeriod));
        writer.Add(CreatePeriodProperty("slowPeriod", "Slow Period", _slowPeriod));
        writer.Add(CreatePeriodProperty("signalPeriod", "Signal Period", _signalPeriod));
        writer.Add(CreateColorProperty(
            MacdObjectId + ".stroke",
            "MACD Stroke",
            _macdStroke));
        writer.Add(CreateColorProperty(
            SignalObjectId + ".stroke",
            "Signal Stroke",
            _signalStroke));
        writer.Add(CreateColorProperty(
            HistogramObjectId + ".stroke",
            "Histogram Stroke",
            _histogramStroke));
    }

    public void DescribeCommands(IChartCommandWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.Add(new ChartCommandDescriptor(
            "indicator.macd.inspect",
            "Inspect MACD",
            "Indicators",
            false,
            ChartCommandPlacement.ContextMenu |
            ChartCommandPlacement.QuickToolbar |
            ChartCommandPlacement.PropertyInspector));
    }

    public MacdValuePoint[] SnapshotValues() => _runtime.SnapshotValues();

    private ChartContribution CreateContribution(
        string objectId,
        ChartPrimitiveKind primitiveKind,
        IReadOnlyList<ChartSeriesPoint> points,
        string stroke,
        double strokeWidth) =>
        new(
            new ChartObjectIdentity(
                Definition.ModuleId,
                InstanceId,
                objectId),
            _panelId!,
            primitiveKind,
            _zIndex,
            points,
            new JsonObject
            {
                ["stroke"] = stroke,
                ["strokeWidth"] = strokeWidth
            });

    private static ChartPropertyDescriptor CreatePeriodProperty(
        string propertyId,
        string displayName,
        int value) =>
        new(
            propertyId,
            displayName,
            "MACD",
            ChartPropertyValueKind.Integer,
            value,
            ChartChangeImpact.RecalculateModule,
            ChartPropertyStorage.Parameters,
            minimum: 1,
            maximum: 10_000);

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
                $"MACD parameter '{key}' must be between 1 and 10000.");
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
