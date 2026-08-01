using ChartKit.CSharp.ModuleHost;
using ChartKit.CSharp.Modules.Abstractions;
using ChartKit.CSharp.Scene;

namespace ChartKit.CSharp.Composition;

public sealed class ChartCompositionService
{
    private readonly ChartModuleHost _moduleHost;
    private readonly SceneCompiler _sceneCompiler;

    public ChartCompositionService(
        ChartModuleHost moduleHost,
        SceneCompiler? sceneCompiler = null)
    {
        _moduleHost = moduleHost ??
            throw new ArgumentNullException(nameof(moduleHost));
        _sceneCompiler = sceneCompiler ?? new SceneCompiler();
    }

    public ChartRenderPlan Compose(ChartVisualContext context)
    {
        IReadOnlyList<ChartHostedContributionSet> hostedSets =
            _moduleHost.CollectVisualContributions(context);

        var sceneSets = new ModuleContributionSet[hostedSets.Count];
        for (int index = 0; index < hostedSets.Count; index++)
        {
            ChartHostedContributionSet hosted = hostedSets[index];
            sceneSets[index] = new ModuleContributionSet(
                hosted.ModuleId,
                hosted.InstanceId,
                true,
                hosted.Contributions);
        }

        return _sceneCompiler.Compile(sceneSets);
    }
}
