using System.Text.Json;
using System.Text.Json.Nodes;
using ChartKit.CSharp.Modules.Abstractions;
using ChartKit.CSharp.Modules.Indicators;
using ChartKit.CSharp.Scene;
using ChartKit.CSharp.UiModel;

namespace ChartKit.CSharp.App;

internal static class MacdPanelAppVerification
{
    public static async Task RunAsync(string timeframe)
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "chartkit-app-macd-panel-test-" + Guid.NewGuid().ToString("N"));
        string profilePath = Path.Combine(directory, "chart-profile.json");
        ChartPrimarySeriesSnapshot primary = CreatePrimarySeries();

        try
        {
            using (var controller = new ChartModulePlatformController(profilePath))
            {
                await controller.InitializeAsync(timeframe);
                ChartUiCommandItem toggle = FindMacdToggle(controller);
                ChartModulePlatformActionResult enabled =
                    await controller.ExecuteCommandAsync(toggle);
                if (!enabled.Succeeded || !enabled.Changed)
                    throw new InvalidOperationException("App MACD panel toggle failed.");

                await controller.UpdatePrimarySeriesAsync(
                    primary,
                    viewportVersion: 1,
                    themeVersion: 0,
                    visibleStartIndex: 0,
                    visibleEndExclusive: primary.Bars.Count);
                AssertPanel(controller.RenderPlan, "default profile");
                await controller.SaveCurrentAsync();
            }

            JsonNode root = JsonNode.Parse(await File.ReadAllTextAsync(profilePath))
                ?? throw new InvalidOperationException("MACD profile JSON is empty.");
            JsonArray modules = root["modules"]?.AsArray()
                ?? throw new InvalidOperationException("MACD profile modules are missing.");
            JsonObject macdProfile = modules
                .Select(static item => item?.AsObject())
                .Single(static item =>
                    item is not null &&
                    item["moduleId"]?.GetValue<string>() ==
                        MacdModule.Definition.ModuleId)!;
            macdProfile["placement"] = "indicator.4";
            await File.WriteAllTextAsync(
                profilePath,
                root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) +
                Environment.NewLine);

            using (var restored = new ChartModulePlatformController(profilePath))
            {
                await restored.InitializeAsync(timeframe);
                ChartUiCommandItem toggle = FindMacdToggle(restored);
                if (!toggle.IsChecked)
                    throw new InvalidOperationException(
                        "Legacy MACD profile enabled state was not restored.");

                await restored.UpdatePrimarySeriesAsync(
                    primary,
                    viewportVersion: 2,
                    themeVersion: 0,
                    visibleStartIndex: 0,
                    visibleEndExclusive: primary.Bars.Count);
                AssertPanel(restored.RenderPlan, "legacy indicator.4 profile");
            }

            Console.WriteLine("csharp_app_macd_panel_contract=PASS");
            Console.WriteLine("csharp_app_macd_legacy_panel_migration=PASS");
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    private static ChartUiCommandItem FindMacdToggle(
        ChartModulePlatformController controller) =>
        controller.BuildUiCatalog().ContextMenuItems.Single(static item =>
            item.Kind == ChartUiCommandKind.ModuleToggle &&
            item.Owner.ModuleId == MacdModule.Definition.ModuleId);

    private static void AssertPanel(ChartRenderPlan plan, string context)
    {
        RenderPrimitivePlan[] macd = plan.Primitives
            .Where(static item =>
                item.Identity.ModuleId == MacdModule.Definition.ModuleId)
            .ToArray();
        if (macd.Length != 3 ||
            macd.Any(static item => item.PanelId != MacdModule.DefaultPanelId))
        {
            throw new InvalidOperationException(
                $"App MACD {context} did not target {MacdModule.DefaultPanelId}.");
        }
    }

    private static ChartPrimarySeriesSnapshot CreatePrimarySeries()
    {
        var bars = new ChartPrimaryBar[100];
        double previous = 100d;
        for (int index = 0; index < bars.Length; index++)
        {
            double close = 100d + index * 0.2d + Math.Sin(index / 4d) * 4d;
            bars[index] = new ChartPrimaryBar(
                index,
                previous,
                Math.Max(previous, close) + 1d,
                Math.Min(previous, close) - 1d,
                close,
                1_000L + index,
                true);
            previous = close;
        }
        return new ChartPrimarySeriesSnapshot(700, bars);
    }
}
