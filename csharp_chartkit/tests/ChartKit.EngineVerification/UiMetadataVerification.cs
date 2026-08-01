using System.Reflection;
using System.Text.Json.Nodes;
using ChartKit.CSharp.Composition;
using ChartKit.CSharp.ModuleHost;
using ChartKit.CSharp.Modules.Abstractions;
using ChartKit.CSharp.Modules.Platform;
using ChartKit.CSharp.Scene;
using ChartKit.CSharp.UiModel;

namespace ChartKit.CSharp.EngineVerification;

internal static class UiMetadataVerification
{
    public static void Run()
    {
        var registry = new ChartModuleRegistry();
        registry.Register<PlatformProbeModule>();
        var host = new ChartModuleHost(registry);

        ChartModuleOperationResult added = host.UpsertProfile(new ChartModuleProfile
        {
            ModuleId = PlatformProbeModule.Definition.ModuleId,
            InstanceId = "probe-ui-001",
            ModuleSchemaVersion = PlatformProbeModule.Definition.SchemaVersion,
            IsEnabled = true,
            ZIndex = 20,
            Placement = PlatformProbeModule.Definition.DefaultPanelId,
            Parameters = new JsonObject
            {
                ["level"] = 50d,
                ["amplitude"] = 5d
            },
            Style = new JsonObject
            {
                ["stroke"] = "accent"
            },
            PersistentState = new JsonObject()
        });
        if (!added.Succeeded || !added.Changed)
            throw new InvalidOperationException("Platform probe was not hosted.");

        var selection = new ChartSelectionService();
        if (!selection.SelectModule(
                PlatformProbeModule.Definition.ModuleId,
                "probe-ui-001"))
        {
            throw new InvalidOperationException("Module selection was not applied.");
        }

        var catalog = new ChartModuleUiCatalog(registry, host, selection);
        ChartUiCatalogSnapshot first = catalog.Build();
        ChartUiCatalogSnapshot second = catalog.Build();

        VerifyCommandProjection(first);
        VerifyInspectorProjection(first);
        VerifyDeterministic(first, second);

        if (!selection.Select(new ChartObjectIdentity(
                PlatformProbeModule.Definition.ModuleId,
                "probe-ui-001",
                "probe.polyline")))
        {
            throw new InvalidOperationException(
                "Chart object selection did not replace module selection.");
        }
        if (catalog.Build().InspectorProperties.Count != 3)
        {
            throw new InvalidOperationException(
                "Chart object selection did not resolve its owning module.");
        }

        var mutation = new ChartPropertyMutationService(host);
        ChartPropertyChangeResult levelChanged = mutation.Execute(
            new ChangeChartPropertyCommand(
                "probe-ui-001",
                "level",
                JsonValue.Create(75.5d)));
        if (!levelChanged.Succeeded || !levelChanged.Changed ||
            levelChanged.ChangeImpact != ChartChangeImpact.RebuildVisuals ||
            levelChanged.Profile?.Parameters["level"]?.GetValue<double>() != 75.5d)
        {
            throw new InvalidOperationException(
                "Generic level property mutation failed.");
        }

        var composition = new ChartCompositionService(host);
        ChartRenderPlan plan = composition.Compose(
            new ChartVisualContext(1, 1, 1));
        RenderPrimitivePlan primitive = plan.Primitives.Single();
        if (primitive.Points[0].Y != 75.5d ||
            primitive.Points[1].Y != 80.5d)
        {
            throw new InvalidOperationException(
                "Property mutation did not reach the render plan.");
        }

        ChartPropertyChangeResult noOp = mutation.Execute(
            new ChangeChartPropertyCommand(
                "probe-ui-001",
                "level",
                JsonValue.Create(75.5d)));
        if (!noOp.Succeeded || noOp.Changed ||
            noOp.ChangeImpact != ChartChangeImpact.None)
        {
            throw new InvalidOperationException(
                "Equivalent property mutation was not treated as a no-op.");
        }

        ChartPropertyChangeResult invalidAmplitude = mutation.Execute(
            new ChangeChartPropertyCommand(
                "probe-ui-001",
                "amplitude",
                JsonValue.Create(-1d)));
        if (invalidAmplitude.Succeeded || invalidAmplitude.Changed)
        {
            throw new InvalidOperationException(
                "Out-of-range property mutation was accepted.");
        }

        ChartPropertyChangeResult strokeChanged = mutation.Execute(
            new ChangeChartPropertyCommand(
                "probe-ui-001",
                "stroke",
                JsonValue.Create("#ff00ff")));
        if (!strokeChanged.Succeeded || !strokeChanged.Changed ||
            strokeChanged.ChangeImpact != ChartChangeImpact.RedrawOnly ||
            strokeChanged.Profile?.Style["stroke"]?.GetValue<string>() !=
                "#ff00ff")
        {
            throw new InvalidOperationException(
                "Style property mutation failed.");
        }

        ChartPropertyChangeResult missingProperty = mutation.Execute(
            new ChangeChartPropertyCommand(
                "probe-ui-001",
                "missing.property",
                JsonValue.Create(1)));
        if (missingProperty.Succeeded || missingProperty.Changed)
        {
            throw new InvalidOperationException(
                "Unknown property mutation was accepted.");
        }

        ChartModuleOperationResult disabled = host.SetEnabled(
            "probe-ui-001",
            false);
        if (!disabled.Succeeded || !disabled.Changed)
            throw new InvalidOperationException("Probe disable failed.");

        ChartUiCatalogSnapshot disabledCatalog = catalog.Build();
        ChartUiCommandItem disabledToggle = disabledCatalog.ContextMenuItems
            .Single(static item =>
                item.CommandId == ChartModuleUiCatalog.ModuleToggleCommandId);
        if (disabledToggle.IsChecked ||
            disabledCatalog.InspectorProperties.Count != 3 ||
            disabledCatalog.InspectorProperties.Any(
                static property => property.IsModuleEnabled))
        {
            throw new InvalidOperationException(
                "Disabled module UI metadata is inconsistent.");
        }

        if (!selection.Clear() ||
            catalog.Build().InspectorProperties.Count != 0)
        {
            throw new InvalidOperationException(
                "Clearing selection did not clear the inspector model.");
        }

        VerifyReferenceBoundary();

#if RELEASE
        VerifyReleaseAssembly(typeof(ChartModuleUiCatalog).Assembly);
        Console.WriteLine("csharp_ui_metadata_release_configuration=PASS");
#endif

        Console.WriteLine("csharp_ui_context_projection=PASS");
        Console.WriteLine("csharp_ui_quick_toolbar_projection=PASS");
        Console.WriteLine("csharp_ui_selection_inspector=PASS");
        Console.WriteLine("csharp_ui_property_mutation=PASS");
        Console.WriteLine("csharp_ui_property_validation=PASS");
        Console.WriteLine("csharp_ui_minimal_invalidation=PASS");
        Console.WriteLine("csharp_ui_metadata_deterministic=PASS");
        Console.WriteLine("csharp_ui_metadata_reference_boundary=PASS");
        Console.WriteLine("csharp_ui_metadata_contracts=PASS");
    }

    private static void VerifyCommandProjection(ChartUiCatalogSnapshot catalog)
    {
        ChartUiCommandItem toggle = catalog.ContextMenuItems.Single(
            static item =>
                item.CommandId == ChartModuleUiCatalog.ModuleToggleCommandId);
        ChartUiCommandItem inspect = catalog.ContextMenuItems.Single(
            static item => item.CommandId == "platform.probe.inspect");

        if (!toggle.IsCheckable || !toggle.IsChecked || !toggle.IsEnabled ||
            toggle.Kind != ChartUiCommandKind.ModuleToggle ||
            inspect.Kind != ChartUiCommandKind.ModuleCommand ||
            !inspect.IsEnabled ||
            catalog.QuickToolbarItems.Count != 2 ||
            !catalog.QuickToolbarItems.Any(static item =>
                item.CommandId == ChartModuleUiCatalog.ModuleToggleCommandId) ||
            !catalog.QuickToolbarItems.Any(static item =>
                item.CommandId == "platform.probe.inspect"))
        {
            throw new InvalidOperationException(
                "Command descriptors were not projected to UI surfaces.");
        }
    }

    private static void VerifyInspectorProjection(ChartUiCatalogSnapshot catalog)
    {
        if (catalog.InspectorProperties.Count != 3)
        {
            throw new InvalidOperationException(
                "Platform probe properties were not projected.");
        }

        ChartPropertyDescriptor level = catalog.InspectorProperties
            .Single(static item => item.Descriptor.PropertyId == "level")
            .Descriptor;
        ChartPropertyDescriptor amplitude = catalog.InspectorProperties
            .Single(static item => item.Descriptor.PropertyId == "amplitude")
            .Descriptor;
        ChartPropertyDescriptor stroke = catalog.InspectorProperties
            .Single(static item => item.Descriptor.PropertyId == "stroke")
            .Descriptor;

        if (level.ValueKind != ChartPropertyValueKind.Decimal ||
            level.Storage != ChartPropertyStorage.Parameters ||
            level.ChangeImpact != ChartChangeImpact.RebuildVisuals ||
            amplitude.Minimum != 0d ||
            stroke.ValueKind != ChartPropertyValueKind.Color ||
            stroke.Storage != ChartPropertyStorage.Style ||
            stroke.ChangeImpact != ChartChangeImpact.RedrawOnly ||
            catalog.InspectorProperties.Any(
                static item => !item.IsEditable))
        {
            throw new InvalidOperationException(
                "Inspector property metadata is incomplete.");
        }
    }

    private static void VerifyDeterministic(
        ChartUiCatalogSnapshot first,
        ChartUiCatalogSnapshot second)
    {
        static string CommandSignature(IEnumerable<ChartUiCommandItem> items) =>
            string.Join(
                "|",
                items.Select(static item =>
                    $"{item.Category}/{item.DisplayName}/" +
                    $"{item.Owner.InstanceId}/{item.CommandId}/" +
                    $"{item.IsChecked}/{item.IsEnabled}"));

        static string PropertySignature(IEnumerable<ChartUiPropertyItem> items) =>
            string.Join(
                "|",
                items.Select(static item =>
                    $"{item.Descriptor.Category}/" +
                    $"{item.Descriptor.DisplayName}/" +
                    $"{item.Descriptor.PropertyId}"));

        if (!StringComparer.Ordinal.Equals(
                CommandSignature(first.ContextMenuItems),
                CommandSignature(second.ContextMenuItems)) ||
            !StringComparer.Ordinal.Equals(
                CommandSignature(first.QuickToolbarItems),
                CommandSignature(second.QuickToolbarItems)) ||
            !StringComparer.Ordinal.Equals(
                PropertySignature(first.InspectorProperties),
                PropertySignature(second.InspectorProperties)))
        {
            throw new InvalidOperationException(
                "UI metadata projection is not deterministic.");
        }
    }

    private static void VerifyReferenceBoundary()
    {
        string[] forbidden =
        [
            "ChartKit.App",
            "ChartKit.Composition",
            "ChartKit.DataSources",
            "ChartKit.Persistence",
            "ChartKit.Rendering",
            "ChartKit.Scene",
            "SkiaSharp",
            "System.Windows.Forms"
        ];

        string[] references = typeof(ChartModuleUiCatalog).Assembly
            .GetReferencedAssemblies()
            .Select(static name => name.Name ?? string.Empty)
            .ToArray();

        if (!references.Contains(
                "ChartKit.ModuleHost",
                StringComparer.Ordinal) ||
            !references.Contains(
                "ChartKit.Modules.Abstractions",
                StringComparer.Ordinal))
        {
            throw new InvalidOperationException(
                "ChartKit.UiModel lost a required platform reference.");
        }

        foreach (string name in forbidden)
        {
            if (references.Contains(name, StringComparer.Ordinal))
            {
                throw new InvalidOperationException(
                    $"ChartKit.UiModel has forbidden reference: {name}");
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
