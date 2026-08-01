using System.Text.Json.Nodes;
using ChartKit.CSharp.Modules.Abstractions;

namespace ChartKit.CSharp.Persistence;

public sealed class ChartProfile
{
    public const int CurrentSchemaVersion = 2;

    private readonly JsonObject _layout;
    private readonly JsonObject _interaction;
    private readonly JsonObject _theme;
    private readonly ChartModuleProfile[] _modules;

    public ChartProfile(
        string timeframe,
        JsonObject? layout = null,
        JsonObject? interaction = null,
        JsonObject? theme = null,
        IEnumerable<ChartModuleProfile>? modules = null,
        int schemaVersion = CurrentSchemaVersion)
    {
        if (schemaVersion != CurrentSchemaVersion)
        {
            throw new ArgumentOutOfRangeException(
                nameof(schemaVersion),
                schemaVersion,
                $"ChartProfile must use schema version {CurrentSchemaVersion}.");
        }

        SchemaVersion = schemaVersion;
        Timeframe = RequireText(timeframe, nameof(timeframe));
        _layout = CloneObject(layout ?? new JsonObject());
        _interaction = CloneObject(interaction ?? new JsonObject());
        _theme = CloneObject(theme ?? new JsonObject());
        _modules = (modules ?? Array.Empty<ChartModuleProfile>())
            .Select(CloneAndValidateModule)
            .ToArray();

        var instanceIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (ChartModuleProfile module in _modules)
        {
            if (!instanceIds.Add(module.InstanceId))
            {
                throw new InvalidOperationException(
                    $"Duplicate chart module instance id: {module.InstanceId}");
            }
        }
    }

    public int SchemaVersion { get; }
    public string Timeframe { get; }
    public JsonObject Layout => CloneObject(_layout);
    public JsonObject Interaction => CloneObject(_interaction);
    public JsonObject Theme => CloneObject(_theme);

    public IReadOnlyList<ChartModuleProfile> Modules =>
        _modules.Select(CloneModule).ToArray();

    internal JsonObject CloneLayout() => CloneObject(_layout);
    internal JsonObject CloneInteraction() => CloneObject(_interaction);
    internal JsonObject CloneTheme() => CloneObject(_theme);

    internal IReadOnlyList<ChartModuleProfile> CloneModules() =>
        _modules.Select(CloneModule).ToArray();

    internal static ChartModuleProfile CloneModule(ChartModuleProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        return new ChartModuleProfile
        {
            ModuleId = RequireText(profile.ModuleId, nameof(profile.ModuleId)),
            InstanceId = RequireText(profile.InstanceId, nameof(profile.InstanceId)),
            ModuleSchemaVersion = ValidateModuleSchemaVersion(
                profile.ModuleSchemaVersion),
            IsEnabled = profile.IsEnabled,
            ZIndex = profile.ZIndex,
            Placement = RequireText(profile.Placement, nameof(profile.Placement)),
            Parameters = CloneRequiredObject(
                profile.Parameters,
                nameof(profile.Parameters)),
            Style = CloneRequiredObject(profile.Style, nameof(profile.Style)),
            PersistentState = CloneRequiredObject(
                profile.PersistentState,
                nameof(profile.PersistentState))
        };
    }

    private static ChartModuleProfile CloneAndValidateModule(
        ChartModuleProfile profile) =>
        CloneModule(profile);

    private static int ValidateModuleSchemaVersion(int value)
    {
        if (value < 1)
            throw new ArgumentOutOfRangeException(nameof(value));
        return value;
    }

    private static JsonObject CloneRequiredObject(
        JsonObject? value,
        string parameterName) =>
        value is null
            ? throw new ArgumentNullException(parameterName)
            : CloneObject(value);

    private static JsonObject CloneObject(JsonObject value) =>
        (JsonObject)value.DeepClone();

    private static string RequireText(string? value, string parameterName) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException(
                "Value must not be blank.",
                parameterName)
            : value.Trim();
}
