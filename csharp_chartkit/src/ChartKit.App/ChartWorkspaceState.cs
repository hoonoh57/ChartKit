using ChartKit.CSharp.Contracts;
using ChartKit.CSharp.Rendering;

namespace ChartKit.CSharp.App;

internal sealed class ChartWorkspaceState
{
    public ChartWorkspaceState(CandleTimeframe timeframe, int visibleBars)
    {
        timeframe.Validate();
        Timeframe = timeframe;
        RequestedVisibleBars = Math.Max(20, visibleBars);
        RebuildRenderOptions();
    }

    public CandleTimeframe Timeframe { get; private set; }
    public int RequestedVisibleBars { get; private set; }
    public bool ShowDates { get; private set; } = true;
    public bool ShowAxes { get; private set; } = true;
    public bool ShowLegend { get; private set; } = true;
    public bool ShowCrosshair { get; private set; } = true;
    public bool ShowInfoPanel { get; private set; } = true;
    public ChartRenderOptions RenderOptions { get; private set; } =
        ChartRenderOptions.Default;

    public void SetTimeframe(CandleTimeframe timeframe)
    {
        timeframe.Validate();
        Timeframe = timeframe;
    }

    public void SetVisibleBars(int visibleBars) =>
        RequestedVisibleBars = Math.Clamp(visibleBars, 20, 5_000);

    public void SetDates(bool value)
    {
        ShowDates = value;
        RebuildRenderOptions();
    }

    public void SetAxes(bool value)
    {
        ShowAxes = value;
        RebuildRenderOptions();
    }

    public void SetLegend(bool value) => ShowLegend = value;
    public void SetCrosshair(bool value) => ShowCrosshair = value;
    public void SetInfoPanel(bool value) => ShowInfoPanel = value;

    private void RebuildRenderOptions() =>
        RenderOptions = new ChartRenderOptions(
            ShowText: true,
            ShowAxes: ShowAxes,
            ShowDateBoundaries: ShowDates);
}
