using System.Text.Json;
using System.Text.Json.Nodes;
using ChartKit.CSharp.Modules.Abstractions;

namespace ChartKit.CSharp.Persistence;

public sealed class ChartProfileCodec
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public string Serialize(ChartProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var root = new JsonObject
        {
            ["schemaVersion"] = profile.SchemaVersion,
            ["timeframe"] = profile.Timeframe,
            ["layout"] = profile.CloneLayout(),
            ["interaction"] = profile.CloneInteraction(),
            ["theme"] = profile.CloneTheme()
        };

        var modules = new JsonArray();
        foreach (ChartModuleProfile module in profile.CloneModules())
            modules.Add(SerializeModule(module));
        root["modules"] = modules;

        return root.ToJsonString(JsonOptions) + "\n";
    }

    public ChartProfile Deserialize(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new ArgumentException("Profile JSON is required.", nameof(json));

        JsonNode? parsed;
        try
        {
            parsed = JsonNode.Parse(json);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                "Chart profile JSON is invalid.",
                exception);
        }

        if (parsed is not JsonObject root)
        {
            throw new InvalidDataException(
                "Chart profile root must be a JSON object.");
        }

        int sourceVersion = ReadSchemaVersion(root);
        if (sourceVersion < 1)
        {
            throw new InvalidDataException(
                $"Chart profile schema version is invalid: {sourceVersion}");
        }
        if (sourceVersion > ChartProfile.CurrentSchemaVersion)
        {
            throw new NotSupportedException(
                $"Chart profile schema version {sourceVersion} is newer than " +
                $"supported version {ChartProfile.CurrentSchemaVersion}.");
        }

        JsonObject migrated = (JsonObject)root.DeepClone();
        while (sourceVersion < ChartProfile.CurrentSchemaVersion)
        {
            migrated = MigrateOneVersion(migrated, sourceVersion);
            sourceVersion++;
        }

        return ParseCurrent(migrated);
    }

    private static JsonObject MigrateOneVersion(
        JsonObject source,
        int sourceVersion)
    {
        if (sourceVersion != 1)
        {
            throw new NotSupportedException(
                $"No chart profile migration is registered for version " +
                $"{sourceVersion}.");
        }

        var migrated = (JsonObject)source.DeepClone();
        migrated["schemaVersion"] = 2;
        migrated["layout"] ??= new JsonObject();
        migrated["interaction"] ??= new JsonObject();
        migrated["theme"] ??= new JsonObject();
        migrated["modules"] ??= new JsonArray();
        return migrated;
    }

    private static ChartProfile ParseCurrent(JsonObject root)
    {
        int schemaVersion = ReadRequiredInt(root, "schemaVersion");
        if (schemaVersion != ChartProfile.CurrentSchemaVersion)
        {
            throw new InvalidDataException(
                $"Expected chart profile schema version " +
                $"{ChartProfile.CurrentSchemaVersion}, got {schemaVersion}.");
        }

        string timeframe = ReadRequiredString(root, "timeframe");
        JsonObject layout = ReadOptionalObject(root, "layout");
        JsonObject interaction = ReadOptionalObject(root, "interaction");
        JsonObject theme = ReadOptionalObject(root, "theme");
        IReadOnlyList<ChartModuleProfile> modules = ReadModules(root);

        return new ChartProfile(
            timeframe,
            layout,
            interaction,
            theme,
            modules,
            schemaVersion);
    }

    private static JsonObject SerializeModule(ChartModuleProfile module) =>
        new()
        {
            ["moduleId"] = module.ModuleId,
            ["instanceId"] = module.InstanceId,
            ["moduleSchemaVersion"] = module.ModuleSchemaVersion,
            ["isEnabled"] = module.IsEnabled,
            ["zIndex"] = module.ZIndex,
            ["placement"] = module.Placement,
            ["parameters"] = (JsonObject)module.Parameters.DeepClone(),
            ["style"] = (JsonObject)module.Style.DeepClone(),
            ["persistentState"] =
                (JsonObject)module.PersistentState.DeepClone()
        };

    private static IReadOnlyList<ChartModuleProfile> ReadModules(JsonObject root)
    {
        if (!root.TryGetPropertyValue("modules", out JsonNode? node) ||
            node is null)
        {
            return Array.Empty<ChartModuleProfile>();
        }
        if (node is not JsonArray array)
            throw new InvalidDataException("Property 'modules' must be an array.");

        var modules = new List<ChartModuleProfile>(array.Count);
        for (int index = 0; index < array.Count; index++)
        {
            if (array[index] is not JsonObject module)
            {
                throw new InvalidDataException(
                    $"Module profile at index {index} must be an object.");
            }

            modules.Add(new ChartModuleProfile
            {
                ModuleId = ReadRequiredString(module, "moduleId"),
                InstanceId = ReadRequiredString(module, "instanceId"),
                ModuleSchemaVersion = ReadOptionalInt(
                    module,
                    "moduleSchemaVersion",
                    1),
                IsEnabled = ReadOptionalBool(module, "isEnabled", false),
                ZIndex = ReadOptionalInt(module, "zIndex", 0),
                Placement = ReadOptionalString(
                    module,
                    "placement",
                    "price.main"),
                Parameters = ReadOptionalObject(module, "parameters"),
                Style = ReadOptionalObject(module, "style"),
                PersistentState = ReadOptionalObject(
                    module,
                    "persistentState")
            });
        }

        return modules;
    }

    private static int ReadSchemaVersion(JsonObject root)
    {
        if (!root.TryGetPropertyValue("schemaVersion", out JsonNode? node) ||
            node is null)
        {
            return 1;
        }

        return ReadIntNode(node, "schemaVersion");
    }

    private static string ReadRequiredString(
        JsonObject source,
        string propertyName)
    {
        if (!source.TryGetPropertyValue(propertyName, out JsonNode? node) ||
            node is null ||
            node is not JsonValue value ||
            !value.TryGetValue(out string? result) ||
            string.IsNullOrWhiteSpace(result))
        {
            throw new InvalidDataException(
                $"Property '{propertyName}' must be a non-empty string.");
        }

        return result.Trim();
    }

    private static string ReadOptionalString(
        JsonObject source,
        string propertyName,
        string defaultValue)
    {
        if (!source.TryGetPropertyValue(propertyName, out JsonNode? node) ||
            node is null)
        {
            return defaultValue;
        }

        if (node is JsonValue value &&
            value.TryGetValue(out string? result) &&
            !string.IsNullOrWhiteSpace(result))
        {
            return result.Trim();
        }

        throw new InvalidDataException(
            $"Property '{propertyName}' must be a non-empty string.");
    }

    private static int ReadRequiredInt(
        JsonObject source,
        string propertyName)
    {
        if (!source.TryGetPropertyValue(propertyName, out JsonNode? node) ||
            node is null)
        {
            throw new InvalidDataException(
                $"Property '{propertyName}' is required.");
        }

        return ReadIntNode(node, propertyName);
    }

    private static int ReadOptionalInt(
        JsonObject source,
        string propertyName,
        int defaultValue)
    {
        if (!source.TryGetPropertyValue(propertyName, out JsonNode? node) ||
            node is null)
        {
            return defaultValue;
        }

        return ReadIntNode(node, propertyName);
    }

    private static int ReadIntNode(JsonNode node, string propertyName)
    {
        if (node is JsonValue value && value.TryGetValue(out int result))
            return result;

        throw new InvalidDataException(
            $"Property '{propertyName}' must be an integer.");
    }

    private static bool ReadOptionalBool(
        JsonObject source,
        string propertyName,
        bool defaultValue)
    {
        if (!source.TryGetPropertyValue(propertyName, out JsonNode? node) ||
            node is null)
        {
            return defaultValue;
        }

        if (node is JsonValue value && value.TryGetValue(out bool result))
            return result;

        throw new InvalidDataException(
            $"Property '{propertyName}' must be a boolean.");
    }

    private static JsonObject ReadOptionalObject(
        JsonObject source,
        string propertyName)
    {
        if (!source.TryGetPropertyValue(propertyName, out JsonNode? node) ||
            node is null)
        {
            return new JsonObject();
        }

        return node is JsonObject value
            ? (JsonObject)value.DeepClone()
            : throw new InvalidDataException(
                $"Property '{propertyName}' must be an object.");
    }
}
