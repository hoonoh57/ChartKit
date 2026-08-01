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

using System;

namespace ChartKit.CSharp.Modules.Category;

/// <summary>
/// 플랫폼 연결 진입점입니다.
/// 계산·상태·설정이 커지면 별도 파일로 분리하되,
/// Registry/UI/Contribution 연결은 이 Module 파일에서 추적 가능해야 합니다.
/// </summary>
public sealed class FeatureModule :
    IChartModule,
    IChartVisualProvider,
    IChartPropertyProvider,
    IChartCommandProvider
{
    public static ChartModuleDefinition Definition { get; } =
        new(
            moduleId: "category.feature",
            displayName: "Feature",
            category: "Category",
            description: "기능 설명",
            defaultPanelId: "price.main",
            defaultEnabled: false,
            capabilities:
                ChartModuleCapabilities.Visual |
                ChartModuleCapabilities.Properties |
                ChartModuleCapabilities.Commands,
            supportedPrimitiveKinds:
            [
                ChartPrimitiveKind.Polyline
            ]);

    public ChartModuleDefinition ModuleDefinition => Definition;

    public void Initialize(IChartModuleContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
    }

    public void Activate()
    {
    }

    public void Deactivate()
    {
    }

    public void Reset()
    {
    }

    public void BuildContributions(
        ChartVisualContext context,
        IChartContributionWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);

        // writer.RequestPanel(...);
        // writer.AddPolyline(...);
    }

    public void DescribeProperties(
        ChartPropertyContext context,
        IChartPropertyWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);

        // writer.AddBoolean(...);
        // writer.AddInteger(...);
        // writer.AddColor(...);
        // writer.AddPanel(...);
    }

    public void DescribeCommands(IChartCommandWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);

        // writer.AddToggle(...);
        // writer.AddCommand(...);
    }
}
