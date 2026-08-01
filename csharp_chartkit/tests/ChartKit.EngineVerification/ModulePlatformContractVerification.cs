using System.Reflection;
using ChartKit.CSharp.Modules.Abstractions;
using ChartKit.CSharp.Scene;

namespace ChartKit.CSharp.EngineVerification;

internal static class ModulePlatformContractVerification
{
    public static void Run()
    {
        var definition = new ChartModuleDefinition(
            "probe.platform",
            "Platform Probe",
            "Verification",
            1,
            ChartModuleCapabilities.Visual |
            ChartModuleCapabilities.Properties |
            ChartModuleCapabilities.Commands,
            [ChartPrimitiveKind.Polyline]);

        if (definition.ModuleId != "probe.platform")
            throw new InvalidOperationException("Module definition was not preserved.");

        ChartContribution enabled = NewContribution(
            "probe.platform", "probe-001", "line-b", "panel-b", 20);
        ChartContribution first = NewContribution(
            "probe.platform", "probe-001", "line-a", "panel-a", 10);
        ChartContribution disabled = NewContribution(
            "probe.disabled", "probe-002", "hidden", "panel-a", 0);

        var compiler = new SceneCompiler();
        ChartRenderPlan plan = compiler.Compile(
        [
            new ModuleContributionSet(
                "probe.platform", "probe-001", true, [enabled, first]),
            new ModuleContributionSet(
                "probe.disabled", "probe-002", false, [disabled])
        ]);

        if (plan.Primitives.Count != 2)
            throw new InvalidOperationException("Disabled module contributed render work.");
        if (plan.Primitives[0].Identity.ObjectId != "line-a" ||
            plan.Primitives[1].Identity.ObjectId != "line-b")
            throw new InvalidOperationException("Scene order is not deterministic.");

        bool ownershipRejected = false;
        try
        {
            compiler.Compile(
            [
                new ModuleContributionSet(
                    "wrong.owner", "probe-001", true, [first])
            ]);
        }
        catch (InvalidOperationException)
        {
            ownershipRejected = true;
        }

        if (!ownershipRejected)
            throw new InvalidOperationException("Ownership mismatch was accepted.");

        bool duplicateRejected = false;
        try
        {
            compiler.Compile(
            [
                new ModuleContributionSet(
                    "probe.platform", "probe-001", true, [first, first])
            ]);
        }
        catch (InvalidOperationException)
        {
            duplicateRejected = true;
        }

        if (!duplicateRejected)
            throw new InvalidOperationException("Duplicate object identity was accepted.");

#if RELEASE
        VerifyReleaseAssembly(typeof(IChartModule).Assembly);
        VerifyReleaseAssembly(typeof(SceneCompiler).Assembly);
        Console.WriteLine("csharp_module_platform_release_configuration=PASS");
#endif

        Console.WriteLine("csharp_module_definition_contract=PASS");
        Console.WriteLine("csharp_scene_disabled_module_zero=PASS");
        Console.WriteLine("csharp_scene_deterministic_order=PASS");
        Console.WriteLine("csharp_scene_owner_identity=PASS");
        Console.WriteLine("csharp_module_platform_contracts=PASS");
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

    private static ChartContribution NewContribution(
        string moduleId,
        string instanceId,
        string objectId,
        string panelId,
        int zIndex) =>
        new(
            new ChartObjectIdentity(moduleId, instanceId, objectId),
            panelId,
            ChartPrimitiveKind.Polyline,
            zIndex,
            [new ChartSeriesPoint(1, 10d), new ChartSeriesPoint(2, 11d)]);
}
