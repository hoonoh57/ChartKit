using System.Reflection;
using System.Text.Json.Nodes;
using ChartKit.CSharp.Composition;
using ChartKit.CSharp.ModuleHost;
using ChartKit.CSharp.Modules.Abstractions;
using ChartKit.CSharp.Modules.Platform;
using ChartKit.CSharp.Scene;

namespace ChartKit.CSharp.EngineVerification;

internal static class CompositionProbeVerification
{
    public static void Run()
    {
        VerifyDefinitionAndMetadata();

        var registry = new ChartModuleRegistry();
        registry.Register<PlatformProbeModule>();
        var host = new ChartModuleHost(registry, new FixedContext());
        var composition = new ChartCompositionService(host);

        ChartModuleProfile profile = NewProfile(false, 7, "probe.panel", 100d, 12d);
        ChartModuleOperationResult added = host.UpsertProfile(profile);
        if (!added.Succeeded || !added.Changed)
            throw new InvalidOperationException("Disabled platform probe was not hosted.");

        ChartRenderPlan disabledPlan = composition.Compose(new ChartVisualContext(1, 1, 1));
        if (disabledPlan.Primitives.Count != 0)
            throw new InvalidOperationException("Disabled platform probe produced a plan.");

        ChartModuleOperationResult enabled = host.SetEnabled("platform-probe-001", true);
        if (!enabled.Succeeded || !enabled.Changed)
            throw new InvalidOperationException("Platform probe was not enabled.");

        ChartRenderPlan first = composition.Compose(new ChartVisualContext(2, 3, 4));
        VerifyPlan(first, "probe.panel", 7, 100d, 112d, 100d);

        ChartRenderPlan second = composition.Compose(new ChartVisualContext(2, 3, 4));
        if (!PlansEqual(first, second))
            throw new InvalidOperationException("Composition was not deterministic.");

        ChartModuleOperationResult updated = host.UpsertProfile(
            NewProfile(true, 11, "probe.updated", 40d, 3d));
        if (!updated.Succeeded || !updated.Changed)
            throw new InvalidOperationException("Platform probe profile update failed.");

        ChartRenderPlan changed = composition.Compose(new ChartVisualContext(5, 6, 7));
        VerifyPlan(changed, "probe.updated", 11, 40d, 43d, 40d);

        ChartModuleOperationResult disabled = host.SetEnabled("platform-probe-001", false);
        if (!disabled.Succeeded || !disabled.Changed ||
            composition.Compose(new ChartVisualContext(8, 9, 10)).Primitives.Count != 0)
        {
            throw new InvalidOperationException("Disabled composition did not return zero primitives.");
        }

        VerifyReferenceBoundaries();

#if RELEASE
        VerifyReleaseAssembly(typeof(ChartCompositionService).Assembly);
        VerifyReleaseAssembly(typeof(PlatformProbeModule).Assembly);
        Console.WriteLine("csharp_composition_release_configuration=PASS");
        Console.WriteLine("csharp_platform_probe_release_configuration=PASS");
#endif

        Console.WriteLine("csharp_platform_probe_definition=PASS");
        Console.WriteLine("csharp_platform_probe_metadata=PASS");
        Console.WriteLine("csharp_composition_disabled_zero=PASS");
        Console.WriteLine("csharp_composition_enabled_plan=PASS");
        Console.WriteLine("csharp_composition_profile_update=PASS");
        Console.WriteLine("csharp_composition_deterministic=PASS");
        Console.WriteLine("csharp_composition_reference_boundary=PASS");
        Console.WriteLine("csharp_composition_probe_contracts=PASS");
    }

    private static void VerifyDefinitionAndMetadata()
    {
        ChartModuleDefinition definition = PlatformProbeModule.Definition;
        if (definition.ModuleId != "platform.probe" ||
            definition.Category != "Platform" ||
            definition.DefaultEnabled ||
            definition.DefaultPanelId != "price.main" ||
            !definition.SupportedPrimitiveKinds.SequenceEqual([ChartPrimitiveKind.Polyline]))
        {
            throw new InvalidOperationException("Platform probe definition is incorrect.");
        }

        PlatformProbeModule module = PlatformProbeModule.Create("metadata-probe");
        var properties = new PropertyWriter();
        var commands = new CommandWriter();
        module.DescribeProperties(properties);
        module.DescribeCommands(commands);

        Dictionary<string, ChartPropertyDescriptor> propertyById =
            properties.Items.ToDictionary(
                static item => item.PropertyId,
                StringComparer.Ordinal);
        if (propertyById.Count != 3 ||
            !propertyById.TryGetValue("level", out ChartPropertyDescriptor? level) ||
            !propertyById.TryGetValue(
                "amplitude",
                out ChartPropertyDescriptor? amplitude) ||
            !propertyById.TryGetValue("stroke", out ChartPropertyDescriptor? stroke) ||
            level.ValueKind != ChartPropertyValueKind.Decimal ||
            level.Storage != ChartPropertyStorage.Parameters ||
            level.ChangeImpact != ChartChangeImpact.RebuildVisuals ||
            amplitude.ValueKind != ChartPropertyValueKind.Decimal ||
            amplitude.Storage != ChartPropertyStorage.Parameters ||
            amplitude.ChangeImpact != ChartChangeImpact.RebuildVisuals ||
            amplitude.Minimum != 0.0 ||
            stroke.ValueKind != ChartPropertyValueKind.Color ||
            stroke.Storage != ChartPropertyStorage.Style ||
            stroke.ChangeImpact != ChartChangeImpact.RedrawOnly)
        {
            throw new InvalidOperationException(
                "Platform probe property metadata is incomplete.");
        }

        ChartCommandPlacement requiredPlacements =
            ChartCommandPlacement.ContextMenu |
            ChartCommandPlacement.QuickToolbar |
            ChartCommandPlacement.PropertyInspector;
        if (commands.Items.Count != 1 ||
            commands.Items[0].CommandId != "platform.probe.inspect" ||
            (commands.Items[0].Placement & requiredPlacements) != requiredPlacements)
        {
            throw new InvalidOperationException(
                "Platform probe command metadata is incomplete.");
        }
    }

    private static ChartModuleProfile NewProfile(
        bool enabled,
        int zIndex,
        string placement,
        double level,
        double amplitude) =>
        new()
        {
            ModuleId = PlatformProbeModule.Definition.ModuleId,
            InstanceId = "platform-probe-001",
            ModuleSchemaVersion = PlatformProbeModule.Definition.SchemaVersion,
            IsEnabled = enabled,
            ZIndex = zIndex,
            Placement = placement,
            Parameters = new JsonObject
            {
                ["level"] = level,
                ["amplitude"] = amplitude
            },
            Style = new JsonObject(),
            PersistentState = new JsonObject()
        };

    private static void VerifyPlan(
        ChartRenderPlan plan,
        string panelId,
        int zIndex,
        params double[] expectedY)
    {
        if (plan.Primitives.Count != 1)
            throw new InvalidOperationException("Composition did not produce one primitive.");
        RenderPrimitivePlan primitive = plan.Primitives[0];
        if (primitive.Identity.ModuleId != PlatformProbeModule.Definition.ModuleId ||
            primitive.Identity.InstanceId != "platform-probe-001" ||
            primitive.Identity.ObjectId != "probe.polyline" ||
            primitive.PanelId != panelId ||
            primitive.ZIndex != zIndex ||
            primitive.PrimitiveKind != ChartPrimitiveKind.Polyline ||
            !primitive.Points.Select(static point => point.Y).SequenceEqual(expectedY))
        {
            throw new InvalidOperationException("Composed render plan is incorrect.");
        }
    }

    private static bool PlansEqual(ChartRenderPlan left, ChartRenderPlan right) =>
        left.Primitives.Count == right.Primitives.Count &&
        left.Primitives.Zip(right.Primitives).All(static pair =>
            pair.First.Identity == pair.Second.Identity &&
            pair.First.PanelId == pair.Second.PanelId &&
            pair.First.PrimitiveKind == pair.Second.PrimitiveKind &&
            pair.First.ZIndex == pair.Second.ZIndex &&
            pair.First.Points.SequenceEqual(pair.Second.Points));

    private static void VerifyReferenceBoundaries()
    {
        VerifyNoReferences(
            typeof(ChartCompositionService).Assembly,
            "ChartKit.Rendering", "ChartKit.DataSources", "SkiaSharp", "System.Windows.Forms");
        VerifyNoReferences(
            typeof(PlatformProbeModule).Assembly,
            "ChartKit.Rendering", "ChartKit.Scene", "ChartKit.ModuleHost",
            "ChartKit.DataSources", "SkiaSharp", "System.Windows.Forms");
    }

    private static void VerifyNoReferences(Assembly assembly, params string[] forbidden)
    {
        string[] references = assembly.GetReferencedAssemblies()
            .Select(static item => item.Name ?? string.Empty)
            .ToArray();
        foreach (string name in forbidden)
        {
            if (references.Contains(name, StringComparer.Ordinal))
                throw new InvalidOperationException(
                    $"{assembly.GetName().Name} has forbidden reference: {name}");
        }
    }

    private static void VerifyReleaseAssembly(Assembly assembly)
    {
        string? configuration = assembly
            .GetCustomAttribute<AssemblyConfigurationAttribute>()?
            .Configuration;
        if (!string.Equals(configuration, "Release", StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"{assembly.GetName().Name} was not built in Release configuration.");
    }

    private sealed class FixedContext : IChartModuleContext
    {
        public DateTimeOffset UtcNow { get; } =
            new(2026, 8, 1, 7, 45, 0, TimeSpan.Zero);
    }

    private sealed class PropertyWriter : IChartPropertyWriter
    {
        public List<ChartPropertyDescriptor> Items { get; } = [];
        public void Add(ChartPropertyDescriptor descriptor) => Items.Add(descriptor);
    }

    private sealed class CommandWriter : IChartCommandWriter
    {
        public List<ChartCommandDescriptor> Items { get; } = [];
        public void Add(ChartCommandDescriptor descriptor) => Items.Add(descriptor);
    }
}
