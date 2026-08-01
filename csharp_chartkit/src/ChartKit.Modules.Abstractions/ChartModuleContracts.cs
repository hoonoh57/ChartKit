using System.Text.Json.Nodes;

namespace ChartKit.CSharp.Modules.Abstractions;

[Flags]
public enum ChartModuleCapabilities
{
    None = 0,
    DataRequirements = 1 << 0,
    Computation = 1 << 1,
    Visual = 1 << 2,
    Interaction = 1 << 3,
    Commands = 1 << 4,
    Properties = 1 << 5,
    Persistence = 1 << 6,
    SidePanel = 1 << 7
}

public enum ChartPrimitiveKind
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

public sealed record ChartModuleDefinition
{
    public ChartModuleDefinition(
        string moduleId,
        string displayName,
        string category,
        int schemaVersion,
        ChartModuleCapabilities capabilities,
        IReadOnlyList<ChartPrimitiveKind> supportedPrimitiveKinds)
    {
        ModuleId = RequireText(moduleId, nameof(moduleId));
        DisplayName = RequireText(displayName, nameof(displayName));
        Category = RequireText(category, nameof(category));
        if (schemaVersion < 1)
            throw new ArgumentOutOfRangeException(nameof(schemaVersion));
        SchemaVersion = schemaVersion;
        Capabilities = capabilities;
        SupportedPrimitiveKinds = supportedPrimitiveKinds is null
            ? throw new ArgumentNullException(nameof(supportedPrimitiveKinds))
            : supportedPrimitiveKinds.Distinct().ToArray();
    }

    public string ModuleId { get; }
    public string DisplayName { get; }
    public string Category { get; }
    public int SchemaVersion { get; }
    public ChartModuleCapabilities Capabilities { get; }
    public IReadOnlyList<ChartPrimitiveKind> SupportedPrimitiveKinds { get; }

    private static string RequireText(string value, string parameterName) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("Value must not be blank.", parameterName)
            : value.Trim();
}

public sealed record ChartModuleProfile
{
    public required string ModuleId { get; init; }
    public required string InstanceId { get; init; }
    public int ModuleSchemaVersion { get; init; } = 1;
    public bool IsEnabled { get; init; }
    public int ZIndex { get; init; }
    public string Placement { get; init; } = "price.main";
    public JsonObject Parameters { get; init; } = [];
    public JsonObject Style { get; init; } = [];
    public JsonObject PersistentState { get; init; } = [];
}

public interface IChartModule
{
    ChartModuleDefinition ModuleDefinition { get; }
    string InstanceId { get; }
    void Initialize(IChartModuleContext context);
    void ApplyProfile(ChartModuleProfile profile);
    void Activate();
    void Deactivate();
    void Reset();
}

public interface IChartModuleContext
{
    DateTimeOffset UtcNow { get; }
}

public interface IDataRequirementProvider
{
    void DescribeRequirements(IDataRequirementWriter writer);
}

public interface IDataRequirementWriter
{
    void Add(ChartDataRequirement requirement);
}

public sealed record ChartDataRequirement(
    string SourceKey,
    string SymbolKey,
    string TimeframeKey,
    string DataKind);

public interface IChartComputationModule
{
    long DataVersion { get; }
}

public interface IChartVisualProvider
{
    void BuildContributions(
        ChartVisualContext context,
        IChartContributionWriter writer);
}

public readonly record struct ChartVisualContext(
    long DataVersion,
    long ViewportVersion,
    long ThemeVersion);

public interface IChartContributionWriter
{
    void Add(ChartContribution contribution);
}

public interface IChartPropertyProvider
{
    void DescribeProperties(IChartPropertyWriter writer);
}

public interface IChartPropertyWriter
{
    void Add(ChartPropertyDescriptor descriptor);
}

public enum ChartChangeImpact
{
    None,
    RedrawOnly,
    RebuildVisuals,
    RecalculateModule,
    RebuildLayout,
    ReloadData,
    RestartSubscription,
    RebuildWorkspace
}

public sealed record ChartPropertyDescriptor(
    string PropertyId,
    string DisplayName,
    string Category,
    object? Value,
    ChartChangeImpact ChangeImpact);

public interface IChartCommandProvider
{
    void DescribeCommands(IChartCommandWriter writer);
}

public interface IChartCommandWriter
{
    void Add(ChartCommandDescriptor descriptor);
}

[Flags]
public enum ChartCommandPlacement
{
    None = 0,
    ContextMenu = 1 << 0,
    QuickToolbar = 1 << 1,
    MainMenu = 1 << 2,
    PropertyInspector = 1 << 3,
    KeyboardOnly = 1 << 4
}

public sealed record ChartCommandDescriptor(
    string CommandId,
    string DisplayName,
    string Category,
    bool IsCheckable,
    ChartCommandPlacement Placement);

public readonly record struct ChartObjectIdentity(
    string ModuleId,
    string InstanceId,
    string ObjectId)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ModuleId))
            throw new InvalidOperationException("ModuleId is required.");
        if (string.IsNullOrWhiteSpace(InstanceId))
            throw new InvalidOperationException("InstanceId is required.");
        if (string.IsNullOrWhiteSpace(ObjectId))
            throw new InvalidOperationException("ObjectId is required.");
    }
}

public readonly record struct ChartSeriesPoint(long X, double Y);

public sealed record ChartContribution
{
    public ChartContribution(
        ChartObjectIdentity identity,
        string panelId,
        ChartPrimitiveKind primitiveKind,
        int zIndex,
        IReadOnlyList<ChartSeriesPoint> points)
    {
        identity.Validate();
        Identity = identity;
        PanelId = string.IsNullOrWhiteSpace(panelId)
            ? throw new ArgumentException("PanelId is required.", nameof(panelId))
            : panelId.Trim();
        PrimitiveKind = primitiveKind;
        ZIndex = zIndex;
        Points = points is null
            ? throw new ArgumentNullException(nameof(points))
            : points.ToArray();
    }

    public ChartObjectIdentity Identity { get; }
    public string PanelId { get; }
    public ChartPrimitiveKind PrimitiveKind { get; }
    public int ZIndex { get; }
    public IReadOnlyList<ChartSeriesPoint> Points { get; }
}
