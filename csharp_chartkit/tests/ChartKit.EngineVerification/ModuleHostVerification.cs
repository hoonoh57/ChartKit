using System.Reflection;
using System.Text.Json.Nodes;
using ChartKit.CSharp.ModuleHost;
using ChartKit.CSharp.Modules.Abstractions;

namespace ChartKit.CSharp.EngineVerification;

internal static class ModuleHostVerification
{
    public static void Run()
    {
        ProbeModule.ResetCreated();

        var registry = new ChartModuleRegistry();
        registry.Register<ProbeModule>();

        if (!registry.Contains(ProbeModule.Definition.ModuleId) ||
            registry.Definitions.Count != 1 ||
            !ReferenceEquals(registry.Definitions[0], ProbeModule.Definition))
        {
            throw new InvalidOperationException(
                "Module registry did not preserve the static definition.");
        }

        bool duplicateRejected = false;
        try
        {
            registry.Register<ProbeModule>();
        }
        catch (InvalidOperationException)
        {
            duplicateRejected = true;
        }

        if (!duplicateRejected)
            throw new InvalidOperationException("Duplicate module id was accepted.");

        bool unknownRejected = false;
        try
        {
            registry.Create("missing.module", "missing-001");
        }
        catch (KeyNotFoundException)
        {
            unknownRejected = true;
        }

        if (!unknownRejected)
            throw new InvalidOperationException("Unknown module creation was accepted.");

        var host = new ChartModuleHost(registry, new FixedModuleContext());
        var callerParameters = new JsonObject
        {
            ["primitive"] = "polyline"
        };
        ChartModuleProfile goodProfile = NewProfile(
            "z-good",
            isEnabled: true,
            zIndex: 10,
            callerParameters);

        ChartModuleOperationResult addGood = host.UpsertProfile(goodProfile);
        if (!addGood.Succeeded || !addGood.Changed || host.Count != 1)
            throw new InvalidOperationException("Enabled module was not hosted.");

        ProbeModule good = ProbeModule.Created["z-good"];
        if (good.InitializeCount != 1 ||
            good.ApplyProfileCount != 1 ||
            good.ActivateCount != 1 ||
            good.DeactivateCount != 0)
        {
            throw new InvalidOperationException(
                "Initial module lifecycle counts are incorrect.");
        }

        callerParameters["primitive"] = "marker";
        IReadOnlyList<ChartHostedContributionSet> firstContributions =
            host.CollectVisualContributions(new ChartVisualContext(1, 1, 1));
        if (firstContributions.Count != 1 ||
            firstContributions[0].Contributions.Count != 1 ||
            firstContributions[0].Contributions[0].PrimitiveKind !=
                ChartPrimitiveKind.Polyline)
        {
            throw new InvalidOperationException(
                "Host did not isolate its profile from caller mutation.");
        }

        ChartModuleOperationResult sameEnable = host.Execute(
            new SetModuleEnabledCommand("z-good", true));
        if (!sameEnable.Succeeded || sameEnable.Changed ||
            good.ActivateCount != 1)
        {
            throw new InvalidOperationException(
                "Idempotent enable triggered duplicate activation.");
        }

        ChartModuleOperationResult disabled = host.SetEnabled("z-good", false);
        if (!disabled.Succeeded || !disabled.Changed ||
            good.DeactivateCount != 1)
        {
            throw new InvalidOperationException("Disable lifecycle failed.");
        }

        int buildCountBeforeDisabledCollection = good.BuildCount;
        if (host.CollectVisualContributions(
                new ChartVisualContext(2, 2, 2)).Count != 0 ||
            good.BuildCount != buildCountBeforeDisabledCollection)
        {
            throw new InvalidOperationException(
                "Disabled module performed visual work.");
        }

        ChartModuleOperationResult reenabled = host.SetEnabled("z-good", true);
        if (!reenabled.Succeeded || !reenabled.Changed ||
            good.ActivateCount != 2)
        {
            throw new InvalidOperationException("Re-enable lifecycle failed.");
        }

        ChartModuleOperationResult addBad = host.UpsertProfile(NewProfile(
            "a-bad",
            isEnabled: true,
            zIndex: 10,
            new JsonObject { ["primitive"] = "marker" }));
        if (!addBad.Succeeded)
            throw new InvalidOperationException("Fault probe was not hosted.");

        IReadOnlyList<ChartHostedContributionSet> isolatedContributions =
            host.CollectVisualContributions(new ChartVisualContext(3, 3, 3));
        if (isolatedContributions.Count != 1 ||
            isolatedContributions[0].InstanceId != "z-good")
        {
            throw new InvalidOperationException(
                "Faulting module prevented a healthy module contribution.");
        }

        ChartModuleRuntimeSnapshot[] snapshots =
            host.GetSnapshots().ToArray();
        ChartModuleRuntimeSnapshot badSnapshot = snapshots.Single(
            static snapshot => snapshot.InstanceId == "a-bad");
        ChartModuleRuntimeSnapshot goodSnapshot = snapshots.Single(
            static snapshot => snapshot.InstanceId == "z-good");
        if (!badSnapshot.IsFaulted || badSnapshot.IsActive ||
            !goodSnapshot.IsActive || goodSnapshot.IsFaulted)
        {
            throw new InvalidOperationException(
                "Runtime fault state was not isolated per instance.");
        }

        ChartModuleOperationResult removeGood = host.Remove("z-good");
        if (!removeGood.Succeeded || !removeGood.Changed ||
            good.DeactivateCount != 2 || good.ResetCount != 1)
        {
            throw new InvalidOperationException("Module removal lifecycle failed.");
        }

        VerifyModuleHostReferences();

#if RELEASE
        VerifyReleaseAssembly(typeof(ChartModuleHost).Assembly);
        Console.WriteLine("csharp_module_host_release_configuration=PASS");
#endif

        Console.WriteLine("csharp_module_registry_contract=PASS");
        Console.WriteLine("csharp_module_host_lifecycle=PASS");
        Console.WriteLine("csharp_module_host_disabled_zero=PASS");
        Console.WriteLine("csharp_module_host_fault_isolation=PASS");
        Console.WriteLine("csharp_module_host_profile_copy=PASS");
        Console.WriteLine("csharp_module_host_reference_boundary=PASS");
        Console.WriteLine("csharp_module_host_contracts=PASS");
    }

    private static ChartModuleProfile NewProfile(
        string instanceId,
        bool isEnabled,
        int zIndex,
        JsonObject parameters) =>
        new()
        {
            ModuleId = ProbeModule.Definition.ModuleId,
            InstanceId = instanceId,
            ModuleSchemaVersion = ProbeModule.Definition.SchemaVersion,
            IsEnabled = isEnabled,
            ZIndex = zIndex,
            Placement = ProbeModule.Definition.DefaultPanelId,
            Parameters = parameters,
            Style = new JsonObject(),
            PersistentState = new JsonObject()
        };

    private static void VerifyModuleHostReferences()
    {
        string[] forbidden =
        [
            "ChartKit.Rendering",
            "ChartKit.DataSources",
            "SkiaSharp",
            "System.Windows.Forms"
        ];

        string[] references = typeof(ChartModuleHost).Assembly
            .GetReferencedAssemblies()
            .Select(static name => name.Name ?? string.Empty)
            .ToArray();

        foreach (string name in forbidden)
        {
            if (references.Contains(name, StringComparer.Ordinal))
            {
                throw new InvalidOperationException(
                    $"ChartKit.ModuleHost has forbidden reference: {name}");
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

    private sealed class FixedModuleContext : IChartModuleContext
    {
        public DateTimeOffset UtcNow { get; } =
            new(2026, 8, 1, 6, 30, 0, TimeSpan.Zero);
    }

    private sealed class ProbeModule :
        IChartModule,
        IChartModuleFactory<ProbeModule>,
        IChartVisualProvider
    {
        private ChartModuleProfile? _profile;

        private ProbeModule(string instanceId)
        {
            InstanceId = instanceId;
        }

        public static ChartModuleDefinition Definition { get; } =
            new(
                moduleId: "verification.module-host-probe",
                displayName: "Module Host Probe",
                category: "Verification",
                description: "Verifies registry and host lifecycle contracts.",
                defaultPanelId: "price.main",
                defaultEnabled: false,
                schemaVersion: 1,
                capabilities: ChartModuleCapabilities.Visual,
                supportedPrimitiveKinds:
                [
                    ChartPrimitiveKind.Polyline
                ]);

        public static Dictionary<string, ProbeModule> Created { get; } =
            new(StringComparer.Ordinal);

        public static ProbeModule Create(string instanceId)
        {
            var module = new ProbeModule(instanceId);
            Created.Add(instanceId, module);
            return module;
        }

        public static void ResetCreated() => Created.Clear();

        public ChartModuleDefinition ModuleDefinition => Definition;
        public string InstanceId { get; }
        public int InitializeCount { get; private set; }
        public int ApplyProfileCount { get; private set; }
        public int ActivateCount { get; private set; }
        public int DeactivateCount { get; private set; }
        public int ResetCount { get; private set; }
        public int BuildCount { get; private set; }

        public void Initialize(IChartModuleContext context)
        {
            ArgumentNullException.ThrowIfNull(context);
            InitializeCount++;
        }

        public void ApplyProfile(ChartModuleProfile profile)
        {
            ArgumentNullException.ThrowIfNull(profile);
            if (!StringComparer.Ordinal.Equals(profile.InstanceId, InstanceId))
                throw new InvalidOperationException("Profile instance mismatch.");
            _profile = profile;
            ApplyProfileCount++;
        }

        public void Activate() => ActivateCount++;

        public void Deactivate() => DeactivateCount++;

        public void Reset()
        {
            _profile = null;
            ResetCount++;
        }

        public void BuildContributions(
            ChartVisualContext context,
            IChartContributionWriter writer)
        {
            ArgumentNullException.ThrowIfNull(writer);
            BuildCount++;

            string primitive =
                _profile?.Parameters["primitive"]?.GetValue<string>() ??
                "polyline";
            ChartPrimitiveKind kind =
                StringComparer.Ordinal.Equals(primitive, "marker")
                    ? ChartPrimitiveKind.Marker
                    : ChartPrimitiveKind.Polyline;

            writer.Add(new ChartContribution(
                new ChartObjectIdentity(
                    Definition.ModuleId,
                    InstanceId,
                    "probe-line"),
                _profile?.Placement ?? Definition.DefaultPanelId,
                kind,
                _profile?.ZIndex ?? 0,
                [
                    new ChartSeriesPoint(1, 10d),
                    new ChartSeriesPoint(2, 11d)
                ]));
        }
    }
}
