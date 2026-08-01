using ChartKit.CSharp.Modules.Abstractions;

namespace ChartKit.CSharp.Scene;

public sealed record ModuleContributionSet(
    string ModuleId,
    string InstanceId,
    bool IsEnabled,
    IReadOnlyList<ChartContribution> Contributions);

public sealed class ChartRenderPlan
{
    public ChartRenderPlan(IReadOnlyList<RenderPrimitivePlan> primitives)
    {
        Primitives = primitives is null
            ? throw new ArgumentNullException(nameof(primitives))
            : primitives.ToArray();
    }

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
    {
        Identity = identity;
        PanelId = panelId;
        PrimitiveKind = primitiveKind;
        ZIndex = zIndex;
        Points = points.ToArray();
    }

    public ChartObjectIdentity Identity { get; }
    public string PanelId { get; }
    public ChartPrimitiveKind PrimitiveKind { get; }
    public int ZIndex { get; }
    public IReadOnlyList<ChartSeriesPoint> Points { get; }
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

                plans.Add(new RenderPrimitivePlan(
                    contribution.Identity,
                    contribution.PanelId,
                    contribution.PrimitiveKind,
                    contribution.ZIndex,
                    contribution.Points));
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
}
