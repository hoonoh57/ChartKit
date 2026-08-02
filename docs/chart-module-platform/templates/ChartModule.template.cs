// <chart-module>
// Module-Id: category.feature
// Module-Class: FeatureModule
// Module-Category: Category
// Registration: registry.Register<FeatureModule>()
// Profile-Key: modules[].instanceId
// Data-Requirements: None
// Capabilities: Visual, Properties, Commands
// Contributions: Polyline
// Default-Panel: price.main
// Renderer-Path: ContributionSet -> SceneCompiler -> ChartRenderPlan -> SkiaChartRenderer
// UI-Path: CommandDescriptor/PropertyDescriptor -> ContextMenu/QuickButton/PropertyInspector
// Persistence: ChartModuleProfile.Parameters, ChartModuleProfile.Style
// Verification: FeatureModuleVerification
// </chart-module>

using ChartKit.CSharp.Modules.Abstractions;

namespace ChartKit.CSharp.Modules.Category;

/// <summary>
/// 플랫폼 연결 진입점입니다.
/// 계산·상태·설정이 커지면 별도 파일로 분리하되,
/// Registry/UI/Contribution 연결은 이 Module 파일에서 추적 가능해야 합니다.
/// </summary>
public sealed class FeatureModule :
    IChartModule,
    IChartModuleFactory<FeatureModule>,
    IChartVisualProvider,
    IChartPropertyProvider,
    IChartCommandProvider
{
    private ChartModuleProfile? _profile;

    private FeatureModule(string instanceId)
    {
        InstanceId = string.IsNullOrWhiteSpace(instanceId)
            ? throw new ArgumentException(
                "InstanceId is required.",
                nameof(instanceId))
            : instanceId.Trim();
    }

    public static ChartModuleDefinition Definition { get; } =
        new(
            moduleId: "category.feature",
            displayName: "Feature",
            category: "Category",
            description: "기능 설명",
            defaultPanelId: "price.main",
            defaultEnabled: false,
            schemaVersion: 1,
            capabilities:
                ChartModuleCapabilities.Visual |
                ChartModuleCapabilities.Properties |
                ChartModuleCapabilities.Commands,
            supportedPrimitiveKinds:
            [
                ChartPrimitiveKind.Polyline
            ]);

    public static FeatureModule Create(string instanceId) =>
        new(instanceId);

    public ChartModuleDefinition ModuleDefinition => Definition;
    public string InstanceId { get; }

    public void Initialize(IChartModuleContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
    }

    public void ApplyProfile(ChartModuleProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (!StringComparer.Ordinal.Equals(profile.ModuleId, Definition.ModuleId))
            throw new InvalidOperationException("ModuleId mismatch.");
        if (!StringComparer.Ordinal.Equals(profile.InstanceId, InstanceId))
            throw new InvalidOperationException("InstanceId mismatch.");

        _profile = profile;
    }

    public void Activate()
    {
        // 구독·계산 시작. 중복 호출되어도 안전하게 구현합니다.
    }

    public void Deactivate()
    {
        // 구독·계산 중지. 비활성 상태에서는 작업 비용이 없어야 합니다.
    }

    public void Reset()
    {
        _profile = null;
    }

    public void BuildContributions(
        ChartVisualContext context,
        IChartContributionWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        if (_profile is null)
            return;

        // writer.Add(new ChartContribution(...));
    }

    public void DescribeProperties(IChartPropertyWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);

        // writer.Add(new ChartPropertyDescriptor(...));
    }

    public void DescribeCommands(IChartCommandWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);

        // writer.Add(new ChartCommandDescriptor(...));
    }
}
