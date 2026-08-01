using System.Reflection;
using System.Text.Json.Nodes;
using ChartKit.CSharp.Modules.Abstractions;
using ChartKit.CSharp.Persistence;

namespace ChartKit.CSharp.EngineVerification;

internal static class ProfilePersistenceVerification
{
    public static async Task RunAsync()
    {
        var codec = new ChartProfileCodec();
        JsonObject callerLayout = new()
        {
            ["primaryPanel"] = "price.main"
        };
        JsonObject callerInteraction = new()
        {
            ["crosshair"] = true
        };
        JsonObject callerTheme = new()
        {
            ["name"] = "dark"
        };
        JsonObject probeParameters = new()
        {
            ["level"] = 100d,
            ["amplitude"] = 2d
        };
        JsonObject unknownPersistentState = new()
        {
            ["opaqueToken"] = "preserve-me"
        };

        ChartModuleProfile probe = NewModule(
            "platform.probe",
            "probe-001",
            true,
            20,
            "price.main",
            probeParameters,
            new JsonObject { ["stroke"] = "accent" },
            new JsonObject());
        ChartModuleProfile unknown = NewModule(
            "external.unavailable",
            "external-001",
            false,
            50,
            "external.panel",
            new JsonObject { ["vendorSetting"] = 7 },
            new JsonObject { ["vendorStyle"] = "opaque" },
            unknownPersistentState);

        var profile = new ChartProfile(
            "5m",
            callerLayout,
            callerInteraction,
            callerTheme,
            [probe, unknown]);

        string first = codec.Serialize(profile);
        string second = codec.Serialize(profile);
        if (!StringComparer.Ordinal.Equals(first, second))
        {
            throw new InvalidOperationException(
                "Chart profile serialization is not deterministic.");
        }

        ChartProfile restored = codec.Deserialize(first);
        string roundTrip = codec.Serialize(restored);
        if (!StringComparer.Ordinal.Equals(first, roundTrip))
        {
            throw new InvalidOperationException(
                "Chart profile JSON did not round-trip deterministically.");
        }
        if (restored.SchemaVersion != ChartProfile.CurrentSchemaVersion ||
            restored.Timeframe != "5m" ||
            restored.Modules.Count != 2)
        {
            throw new InvalidOperationException(
                "Chart profile round-trip changed root fields.");
        }

        ChartModuleProfile restoredUnknown = restored.Modules.Single(
            static module => module.ModuleId == "external.unavailable");
        if (restoredUnknown.InstanceId != "external-001" ||
            restoredUnknown.Placement != "external.panel" ||
            restoredUnknown.PersistentState["opaqueToken"]?
                .GetValue<string>() != "preserve-me")
        {
            throw new InvalidOperationException(
                "Unavailable module profile was not preserved.");
        }

        callerLayout["primaryPanel"] = "caller-mutated";
        callerInteraction["crosshair"] = false;
        callerTheme["name"] = "caller-mutated";
        probeParameters["level"] = 999d;
        unknownPersistentState["opaqueToken"] = "caller-mutated";

        JsonObject exposedLayout = profile.Layout;
        exposedLayout["primaryPanel"] = "getter-mutated";
        IReadOnlyList<ChartModuleProfile> exposedModules = profile.Modules;
        exposedModules[0].Parameters["level"] = -1d;
        exposedModules[1].PersistentState["opaqueToken"] = "getter-mutated";

        if (!StringComparer.Ordinal.Equals(first, codec.Serialize(profile)))
        {
            throw new InvalidOperationException(
                "Chart profile defensive copy boundary was violated.");
        }

        VerifyLegacyMigration(codec);
        VerifyFutureVersionRejection(codec);
        VerifyValidation(codec);
        await VerifyAtomicStoreAsync(codec, profile).ConfigureAwait(false);
        VerifyReferenceBoundary();

#if RELEASE
        VerifyReleaseAssembly(typeof(ChartProfile).Assembly);
        Console.WriteLine(
            "csharp_profile_persistence_release_configuration=PASS");
#endif

        Console.WriteLine("csharp_chart_profile_roundtrip=PASS");
        Console.WriteLine("csharp_chart_profile_defensive_copy=PASS");
        Console.WriteLine("csharp_chart_profile_deterministic=PASS");
        Console.WriteLine("csharp_chart_profile_migration=PASS");
        Console.WriteLine("csharp_chart_profile_missing_module_preserved=PASS");
        Console.WriteLine("csharp_chart_profile_future_rejected=PASS");
        Console.WriteLine("csharp_chart_profile_validation=PASS");
        Console.WriteLine("csharp_chart_profile_atomic_store=PASS");
        Console.WriteLine("csharp_profile_persistence_reference_boundary=PASS");
        Console.WriteLine("csharp_profile_persistence_contracts=PASS");
    }

    private static void VerifyLegacyMigration(ChartProfileCodec codec)
    {
        const string legacyJson = """
        {
          "schemaVersion": 1,
          "timeframe": "15m",
          "theme": {
            "name": "legacy-dark"
          },
          "modules": [
            {
              "moduleId": "legacy.unavailable",
              "instanceId": "legacy-001",
              "parameters": {
                "period": 20
              }
            }
          ]
        }
        """;

        ChartProfile migrated = codec.Deserialize(legacyJson);
        ChartModuleProfile migratedModule = migrated.Modules.Single();
        if (migrated.SchemaVersion != ChartProfile.CurrentSchemaVersion ||
            migrated.Timeframe != "15m" ||
            migrated.Layout.Count != 0 ||
            migrated.Interaction.Count != 0 ||
            migrated.Theme["name"]?.GetValue<string>() != "legacy-dark" ||
            migratedModule.ModuleId != "legacy.unavailable" ||
            migratedModule.ModuleSchemaVersion != 1 ||
            migratedModule.Placement != "price.main" ||
            migratedModule.Parameters["period"]?.GetValue<int>() != 20)
        {
            throw new InvalidOperationException(
                "Schema version 1 migration did not produce version 2 defaults.");
        }

        string migratedJson = codec.Serialize(migrated);
        if (!migratedJson.Contains(
                "\"schemaVersion\": 2",
                StringComparison.Ordinal) ||
            !migratedJson.Contains(
                "\"interaction\": {}",
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Migrated profile was not serialized as current schema.");
        }
    }

    private static void VerifyFutureVersionRejection(ChartProfileCodec codec)
    {
        const string futureJson = """
        {
          "schemaVersion": 3,
          "timeframe": "1m"
        }
        """;

        bool rejected = false;
        try
        {
            codec.Deserialize(futureJson);
        }
        catch (NotSupportedException)
        {
            rejected = true;
        }

        if (!rejected)
        {
            throw new InvalidOperationException(
                "Future chart profile schema version was accepted.");
        }
    }

    private static void VerifyValidation(ChartProfileCodec codec)
    {
        bool duplicateRejected = false;
        try
        {
            _ = new ChartProfile(
                "1m",
                modules:
                [
                    NewModule("module.one", "duplicate", false, 0),
                    NewModule("module.two", "duplicate", false, 0)
                ]);
        }
        catch (InvalidOperationException)
        {
            duplicateRejected = true;
        }

        bool blankIdRejected = false;
        try
        {
            _ = new ChartProfile(
                "1m",
                modules:
                [
                    NewModule(" ", "blank-id", false, 0)
                ]);
        }
        catch (ArgumentException)
        {
            blankIdRejected = true;
        }

        bool invalidModulesRejected = false;
        try
        {
            codec.Deserialize(
                "{\"schemaVersion\":2,\"timeframe\":\"1m\",\"modules\":{}}");
        }
        catch (InvalidDataException)
        {
            invalidModulesRejected = true;
        }

        if (!duplicateRejected || !blankIdRejected ||
            !invalidModulesRejected)
        {
            throw new InvalidOperationException(
                "Chart profile validation accepted invalid data.");
        }
    }

    private static async Task VerifyAtomicStoreAsync(
        ChartProfileCodec codec,
        ChartProfile profile)
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "ChartKit.ProfilePersistenceVerification",
            Guid.NewGuid().ToString("N"));
        string path = Path.Combine(directory, "chart-profile.json");

        try
        {
            var store = new ChartProfileStore(codec);
            await store.SaveAsync(path, profile).ConfigureAwait(false);
            ChartProfile firstLoad = await store.LoadAsync(path)
                .ConfigureAwait(false);
            if (!StringComparer.Ordinal.Equals(
                    codec.Serialize(profile),
                    codec.Serialize(firstLoad)))
            {
                throw new InvalidOperationException(
                    "Initial atomic profile save did not round-trip.");
            }

            var updated = new ChartProfile(
                "30m",
                profile.Layout,
                profile.Interaction,
                profile.Theme,
                profile.Modules);
            await store.SaveAsync(path, updated).ConfigureAwait(false);
            ChartProfile secondLoad = await store.LoadAsync(path)
                .ConfigureAwait(false);
            if (secondLoad.Timeframe != "30m")
            {
                throw new InvalidOperationException(
                    "Atomic profile overwrite did not replace the target.");
            }

            byte[] bytes = await File.ReadAllBytesAsync(path)
                .ConfigureAwait(false);
            if (bytes.Length >= 3 &&
                bytes[0] == 0xEF &&
                bytes[1] == 0xBB &&
                bytes[2] == 0xBF)
            {
                throw new InvalidOperationException(
                    "Profile store wrote an unexpected UTF-8 BOM.");
            }

            if (Directory.EnumerateFiles(
                    directory,
                    "*.tmp",
                    SearchOption.TopDirectoryOnly).Any())
            {
                throw new InvalidOperationException(
                    "Atomic profile store left a temporary file behind.");
            }
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    private static ChartModuleProfile NewModule(
        string moduleId,
        string instanceId,
        bool isEnabled,
        int zIndex,
        string placement = "price.main",
        JsonObject? parameters = null,
        JsonObject? style = null,
        JsonObject? persistentState = null) =>
        new()
        {
            ModuleId = moduleId,
            InstanceId = instanceId,
            ModuleSchemaVersion = 1,
            IsEnabled = isEnabled,
            ZIndex = zIndex,
            Placement = placement,
            Parameters = parameters ?? new JsonObject(),
            Style = style ?? new JsonObject(),
            PersistentState = persistentState ?? new JsonObject()
        };

    private static void VerifyReferenceBoundary()
    {
        string[] forbidden =
        [
            "ChartKit.App",
            "ChartKit.Composition",
            "ChartKit.DataSources",
            "ChartKit.ModuleHost",
            "ChartKit.Rendering",
            "ChartKit.Scene",
            "SkiaSharp",
            "System.Windows.Forms"
        ];

        string[] references = typeof(ChartProfile).Assembly
            .GetReferencedAssemblies()
            .Select(static name => name.Name ?? string.Empty)
            .ToArray();

        if (!references.Contains(
                "ChartKit.Modules.Abstractions",
                StringComparer.Ordinal))
        {
            throw new InvalidOperationException(
                "ChartKit.Persistence lost its module profile contract reference.");
        }

        foreach (string name in forbidden)
        {
            if (references.Contains(name, StringComparer.Ordinal))
            {
                throw new InvalidOperationException(
                    $"ChartKit.Persistence has forbidden reference: {name}");
            }
        }
    }

    private static void VerifyReleaseAssembly(Assembly assembly)
    {
        string? configuration = assembly
            .GetCustomAttribute<AssemblyConfigurationAttribute>()?
            .Configuration;
        if (!string.Equals(configuration, "Release", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"{assembly.GetName().Name} was loaded from configuration " +
                $"'{configuration ?? "<missing>"}' instead of Release.");
        }
    }
}
