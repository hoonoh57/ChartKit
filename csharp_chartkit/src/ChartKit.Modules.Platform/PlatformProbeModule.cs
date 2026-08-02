// <chart-module>
// Module-Id: platform.probe
// Module-Class: PlatformProbeModule
// Module-Category: Platform
// Registration: registry.Register<PlatformProbeModule>()
// Profile-Key: modules[].instanceId
// Data-Requirements: None
// Capabilities: Visual, Properties, Commands
// Contributions: Polyline
// Default-Panel: price.main
// Renderer-Path: ContributionSet -> SceneCompiler -> ChartRenderPlan -> SkiaChartRenderer
// UI-Path: CommandDescriptor/PropertyDescriptor -> ContextMenu/QuickButton/PropertyInspector
// Persistence: ChartModuleProfile.Parameters, ChartModuleProfile.Style
// Verification: PlatformProbeModuleVerification
// </chart-module>

using System.Text.Json.Nodes;
using ChartKit.CSharp.Modules.Abstractions;

namespace ChartKit.CSharp.Modules.Platform;

public sealed class PlatformProbeModule :
    IChartModule,
    IChartModuleFactory<PlatformProbeModule>,
    IChartVisualProvider,
    IChartPropertyProvider,
    IChartCommandProvider
{
    private const double DefaultLevel = 50.0;
    private const double DefaultAmplitude = 5.0;
    private const string DefaultStroke = "accent";

    private string? _panelId;
    private int _zIndex;
    private double _level = DefaultLevel;
    private double _amplitude = DefaultAmplitude;
    private string _stroke = DefaultStroke;
    private bool _isActive;

    private PlatformProbeModule(string instanceId)
    {
        InstanceId = RequireText(instanceId, nameof(instanceId));
    }

    public static ChartModuleDefinition Definition { get; } =
        new(
            moduleId: "platform.probe",
            displayName: "Platform Probe",
            category: "Platform",
            description: "Verifies the generic module-to-render-plan pipeline.",
            defaultPanelId: "price.main",
            defaultEnabled: false,
            schemaVersion: 1,
            capabilities:
                ChartModuleCapabilities.Visual |
                ChartModuleCapabilities.Properties |
                ChartModuleCapabilities.Commands,
            supportedPrimitiveKinds:
            [
                ChartPrimitiveKind.Polyline
            ]);

    public static PlatformProbeModule Create(string instanceId) =>
        new(instanceId);

    public ChartModuleDefinition ModuleDefinition => Definition;
    public string InstanceId { get; }

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
        _level = ReadFiniteDouble(
            profile.Parameters,
            "level",
            DefaultLevel,
            minimum: null);
        _amplitude = ReadFiniteDouble(
            profile.Parameters,
            "amplitude",
            DefaultAmplitude,
            minimum: 0.0);
        _stroke = ReadText(profile.Style, "stroke", DefaultStroke);
    }

    public void Activate()
    {
        if (_panelId is null)
            throw new InvalidOperationException("Profile must be applied before activation.");

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
        _level = DefaultLevel;
        _amplitude = DefaultAmplitude;
        _stroke = DefaultStroke;
    }

    public void BuildContributions(
        ChartVisualContext context,
        IChartContributionWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        if (!_isActive || _panelId is null)
            return;

        (long firstX, long middleX, long lastX) = ResolveProbeX(context);
        writer.Add(new ChartContribution(
            new ChartObjectIdentity(
                Definition.ModuleId,
                InstanceId,
                "probe.polyline"),
            _panelId,
            ChartPrimitiveKind.Polyline,
            _zIndex,
            [
                new ChartSeriesPoint(firstX, _level),
                new ChartSeriesPoint(middleX, _level + _amplitude),
                new ChartSeriesPoint(lastX, _level)
            ]));
    }

    public void DescribeProperties(IChartPropertyWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);

        writer.Add(new ChartPropertyDescriptor(
            "level",
            "Level",
            "Platform Probe",
            ChartPropertyValueKind.Decimal,
            _level,
            ChartChangeImpact.RebuildVisuals,
            ChartPropertyStorage.Parameters));
        writer.Add(new ChartPropertyDescriptor(
            "amplitude",
            "Amplitude",
            "Platform Probe",
            ChartPropertyValueKind.Decimal,
            _amplitude,
            ChartChangeImpact.RebuildVisuals,
            ChartPropertyStorage.Parameters,
            minimum: 0.0));
        writer.Add(new ChartPropertyDescriptor(
            "stroke",
            "Stroke",
            "Platform Probe",
            ChartPropertyValueKind.Color,
            _stroke,
            ChartChangeImpact.RedrawOnly,
            ChartPropertyStorage.Style));
    }

    public void DescribeCommands(IChartCommandWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);

        writer.Add(new ChartCommandDescriptor(
            "platform.probe.inspect",
            "Inspect platform probe",
            "Platform",
            false,
            ChartCommandPlacement.ContextMenu |
            ChartCommandPlacement.QuickToolbar |
            ChartCommandPlacement.PropertyInspector));
    }

    private static (long First, long Middle, long Last) ResolveProbeX(
        ChartVisualContext context)
    {
        if (!context.HasVisibleRange)
            return (0L, 1L, 2L);

        long start = context.VisibleStartIndex;
        long last = context.VisibleEndExclusive - 1L;
        long distance = Math.Max(0L, last - start);
        long first = start + distance / 4L;
        long middle = start + distance / 2L;
        long third = start + distance * 3L / 4L;

        if (middle <= first && last > first)
            middle = first + 1L;
        if (third <= middle && last > middle)
            third = last;

        return (first, middle, third);
    }

    private static double ReadFiniteDouble(
        JsonObject parameters,
        string key,
        double fallback,
        double? minimum)
    {
        if (!parameters.TryGetPropertyValue(key, out JsonNode? node) ||
            node is null)
        {
            return fallback;
        }

        double value;
        if (node is JsonValue jsonValue &&
            jsonValue.TryGetValue<double>(out double doubleValue))
        {
            value = doubleValue;
        }
        else if (node is JsonValue integerValue &&
                 integerValue.TryGetValue<int>(out int intValue))
        {
            value = intValue;
        }
        else
        {
            throw new InvalidOperationException(
                $"Parameter '{key}' must be numeric.");
        }

        if (!double.IsFinite(value))
            throw new InvalidOperationException(
                $"Parameter '{key}' must be finite.");
        if (minimum.HasValue && value < minimum.Value)
            throw new InvalidOperationException(
                $"Parameter '{key}' must be at least {minimum.Value}.");

        return value;
    }

    private static string ReadText(
        JsonObject source,
        string key,
        string fallback)
    {
        if (!source.TryGetPropertyValue(key, out JsonNode? node) ||
            node is null)
        {
            return fallback;
        }

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
