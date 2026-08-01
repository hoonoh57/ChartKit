using System.Globalization;
using System.Text.Json.Nodes;
using ChartKit.CSharp.Modules.Abstractions;

namespace ChartKit.CSharp.Scene;

public sealed record ModuleContributionSet
{
    public ModuleContributionSet(
        string moduleId,
        string instanceId,
        bool isEnabled,
        IReadOnlyList<ChartContribution> contributions)
        : this(
            moduleId,
            instanceId,
            isEnabled,
            new JsonObject(),
            contributions)
    {
    }

    public ModuleContributionSet(
        string moduleId,
        string instanceId,
        bool isEnabled,
        JsonObject style,
        IReadOnlyList<ChartContribution> contributions)
    {
        ModuleId = RequireText(moduleId, nameof(moduleId));
        InstanceId = RequireText(instanceId, nameof(instanceId));
        IsEnabled = isEnabled;
        Style = style is null
            ? throw new ArgumentNullException(nameof(style))
            : (JsonObject)style.DeepClone();
        Contributions = contributions is null
            ? throw new ArgumentNullException(nameof(contributions))
            : contributions.ToArray();
    }

    public string ModuleId { get; }
    public string InstanceId { get; }
    public bool IsEnabled { get; }
    public JsonObject Style { get; }
    public IReadOnlyList<ChartContribution> Contributions { get; }

    private static string RequireText(string value, string parameterName) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("Value must not be blank.", parameterName)
            : value.Trim();
}

public enum RenderPrimitiveKind
{
    Candle,
    Polyline,
    Histogram,
    HorizontalHistogram,
    Line,
    Marker,
    Rectangle,
    FillArea,
    Text,
    HeatCell,
    Image
}

public readonly record struct RenderPoint(long X, double Y);

public readonly record struct RenderPrimitiveStyle
{
    public RenderPrimitiveStyle(
        string stroke,
        string fill,
        float strokeWidth,
        float opacity)
    {
        Stroke = string.IsNullOrWhiteSpace(stroke)
            ? "accent"
            : stroke.Trim();
        Fill = string.IsNullOrWhiteSpace(fill)
            ? string.Empty
            : fill.Trim();
        if (!float.IsFinite(strokeWidth) || strokeWidth <= 0f)
            throw new ArgumentOutOfRangeException(nameof(strokeWidth));
        if (!float.IsFinite(opacity) || opacity < 0f || opacity > 1f)
            throw new ArgumentOutOfRangeException(nameof(opacity));
        StrokeWidth = strokeWidth;
        Opacity = opacity;
    }

    public static RenderPrimitiveStyle Default { get; } =
        new("accent", string.Empty, 1.5f, 1f);

    public string Stroke { get; }
    public string Fill { get; }
    public float StrokeWidth { get; }
    public float Opacity { get; }
}

public sealed class ChartRenderPlan
{
    public ChartRenderPlan(IReadOnlyList<RenderPrimitivePlan> primitives)
    {
        Primitives = primitives is null
            ? throw new ArgumentNullException(nameof(primitives))
            : primitives.ToArray();
    }

    public static ChartRenderPlan Empty { get; } =
        new(Array.Empty<RenderPrimitivePlan>());

    public IReadOnlyList<RenderPrimitivePlan> Primitives { get; }
}

public sealed record RenderPrimitivePlan
{
    public RenderPrimitivePlan(
        ChartObjectIdentity identity,
        string panelId,
        ChartPrimitiveKind primitiveKind,
        int zIndex,
        IReadOnlyList<ChartSeriesPoint> points)
        : this(
            identity,
            panelId,
            primitiveKind,
            zIndex,
            points,
            RenderPrimitiveStyle.Default)
    {
    }

    public RenderPrimitivePlan(
        ChartObjectIdentity identity,
        string panelId,
        ChartPrimitiveKind primitiveKind,
        int zIndex,
        IReadOnlyList<ChartSeriesPoint> points,
        RenderPrimitiveStyle style)
    {
        identity.Validate();
        Identity = identity;
        PanelId = string.IsNullOrWhiteSpace(panelId)
            ? throw new ArgumentException("PanelId is required.", nameof(panelId))
            : panelId.Trim();
        PrimitiveKind = primitiveKind;
        RenderKind = MapPrimitiveKind(primitiveKind);
        ZIndex = zIndex;
        if (points is null)
            throw new ArgumentNullException(nameof(points));
        Points = points.ToArray();
        var renderPoints = new RenderPoint[Points.Count];
        for (int index = 0; index < renderPoints.Length; index++)
        {
            ChartSeriesPoint point = Points[index];
            renderPoints[index] = new RenderPoint(point.X, point.Y);
        }
        RenderPoints = renderPoints;
        Style = style;
    }

    public ChartObjectIdentity Identity { get; }
    public string PanelId { get; }
    public ChartPrimitiveKind PrimitiveKind { get; }
    public RenderPrimitiveKind RenderKind { get; }
    public int ZIndex { get; }
    public IReadOnlyList<ChartSeriesPoint> Points { get; }
    public IReadOnlyList<RenderPoint> RenderPoints { get; }
    public RenderPrimitiveStyle Style { get; }

    private static RenderPrimitiveKind MapPrimitiveKind(
        ChartPrimitiveKind primitiveKind) =>
        primitiveKind switch
        {
            ChartPrimitiveKind.Candle => RenderPrimitiveKind.Candle,
            ChartPrimitiveKind.Polyline => RenderPrimitiveKind.Polyline,
            ChartPrimitiveKind.Histogram => RenderPrimitiveKind.Histogram,
            ChartPrimitiveKind.HorizontalHistogram =>
                RenderPrimitiveKind.HorizontalHistogram,
            ChartPrimitiveKind.Line => RenderPrimitiveKind.Line,
            ChartPrimitiveKind.Marker => RenderPrimitiveKind.Marker,
            ChartPrimitiveKind.Rectangle => RenderPrimitiveKind.Rectangle,
            ChartPrimitiveKind.FillArea => RenderPrimitiveKind.FillArea,
            ChartPrimitiveKind.Text => RenderPrimitiveKind.Text,
            ChartPrimitiveKind.HeatCell => RenderPrimitiveKind.HeatCell,
            ChartPrimitiveKind.Image => RenderPrimitiveKind.Image,
            _ => throw new ArgumentOutOfRangeException(nameof(primitiveKind))
        };
}

public sealed class SceneCompiler
{
    public ChartRenderPlan Compile(IEnumerable<ModuleContributionSet> modules)
    {
        ArgumentNullException.ThrowIfNull(modules);

        var plans = new List<RenderPrimitivePlan>();
        var identities = new HashSet<ChartObjectIdentity>();

        foreach (ModuleContributionSet module in modules)
        {
            if (!module.IsEnabled)
                continue;
            if (string.IsNullOrWhiteSpace(module.ModuleId))
                throw new InvalidOperationException("ModuleId is required.");
            if (string.IsNullOrWhiteSpace(module.InstanceId))
                throw new InvalidOperationException("InstanceId is required.");
            if (module.Contributions is null)
                throw new InvalidOperationException("Contributions are required.");

            foreach (ChartContribution contribution in module.Contributions)
            {
                if (!StringComparer.Ordinal.Equals(
                        contribution.Identity.ModuleId,
                        module.ModuleId) ||
                    !StringComparer.Ordinal.Equals(
                        contribution.Identity.InstanceId,
                        module.InstanceId))
                {
                    throw new InvalidOperationException(
                        "Contribution ownership does not match its module envelope.");
                }

                if (!identities.Add(contribution.Identity))
                    throw new InvalidOperationException(
                        $"Duplicate chart object identity: {contribution.Identity}");

                RenderPrimitiveStyle style = ParseStyle(
                    module.Style,
                    contribution.Identity.ObjectId);
                plans.Add(new RenderPrimitivePlan(
                    contribution.Identity,
                    contribution.PanelId,
                    contribution.PrimitiveKind,
                    contribution.ZIndex,
                    contribution.Points,
                    style));
            }
        }

        plans.Sort(static (left, right) =>
        {
            int result = StringComparer.Ordinal.Compare(left.PanelId, right.PanelId);
            if (result != 0) return result;
            result = left.ZIndex.CompareTo(right.ZIndex);
            if (result != 0) return result;
            result = StringComparer.Ordinal.Compare(
                left.Identity.ModuleId,
                right.Identity.ModuleId);
            if (result != 0) return result;
            result = StringComparer.Ordinal.Compare(
                left.Identity.InstanceId,
                right.Identity.InstanceId);
            if (result != 0) return result;
            return StringComparer.Ordinal.Compare(
                left.Identity.ObjectId,
                right.Identity.ObjectId);
        });

        return new ChartRenderPlan(plans);
    }

    private static RenderPrimitiveStyle ParseStyle(
        JsonObject source,
        string objectId)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (string.IsNullOrWhiteSpace(objectId))
            throw new ArgumentException("ObjectId is required.", nameof(objectId));

        string stroke = ReadString(
            source,
            objectId + ".stroke",
            ReadString(source, "stroke", "accent", allowBlank: false),
            allowBlank: false);
        string fill = ReadString(
            source,
            objectId + ".fill",
            ReadString(source, "fill", string.Empty, allowBlank: true),
            allowBlank: true);
        float strokeWidth = ReadSingle(
            source,
            objectId + ".strokeWidth",
            ReadSingle(source, "strokeWidth", 1.5f, 0.01f, 64f),
            0.01f,
            64f);
        float opacity = ReadSingle(
            source,
            objectId + ".opacity",
            ReadSingle(source, "opacity", 1f, 0f, 1f),
            0f,
            1f);
        return new RenderPrimitiveStyle(stroke, fill, strokeWidth, opacity);
    }

    private static string ReadString(
        JsonObject source,
        string key,
        string fallback,
        bool allowBlank)
    {
        if (!source.TryGetPropertyValue(key, out JsonNode? node) || node is null)
            return fallback;
        if (node is not JsonValue value ||
            !value.TryGetValue(out string? result) ||
            result is null)
        {
            throw new InvalidOperationException(
                $"Style '{key}' must be a string.");
        }

        string normalized = result.Trim();
        if (!allowBlank && normalized.Length == 0)
            throw new InvalidOperationException(
                $"Style '{key}' must not be blank.");
        return normalized;
    }

    private static float ReadSingle(
        JsonObject source,
        string key,
        float fallback,
        float minimum,
        float maximum)
    {
        if (!source.TryGetPropertyValue(key, out JsonNode? node) || node is null)
            return fallback;

        double value;
        if (node is JsonValue json && json.TryGetValue(out double doubleValue))
            value = doubleValue;
        else if (node is JsonValue integer && integer.TryGetValue(out int intValue))
            value = intValue;
        else
            throw new InvalidOperationException($"Style '{key}' must be numeric.");

        if (!double.IsFinite(value) || value < minimum || value > maximum)
        {
            throw new InvalidOperationException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Style '{key}' must be between {minimum} and {maximum}."));
        }
        return (float)value;
    }
}
