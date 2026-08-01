using ChartKit.CSharp.Charting;
using ChartKit.CSharp.Contracts;

namespace ChartKit.CSharp.App;

internal sealed partial class MainForm
{
    private bool _moduleVisualContextHooked;
    private string? _moduleVisualSymbol;
    private string? _moduleVisualTimeframe;
    private int _moduleVisibleStartIndex = -1;
    private int _moduleVisibleEndExclusive = -1;
    private long _moduleVisualViewportVersion;

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        if (_moduleVisualContextHooked) return;
        _frameTimer.Tick += OnModuleVisualContextFrame;
        _moduleVisualContextHooked = true;
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        if (_moduleVisualContextHooked)
        {
            _frameTimer.Tick -= OnModuleVisualContextFrame;
            _moduleVisualContextHooked = false;
        }
        base.OnHandleDestroyed(e);
    }

    private void OnModuleVisualContextFrame(object? sender, EventArgs e)
    {
        if (!_modulePlatformReady ||
            !TryGetSelectedSnapshot(out SymbolSnapshot? snapshot))
        {
            return;
        }

        ChartWindow window = _viewport.Resolve(snapshot.Candles.Length);
        if (window.IsEmpty) return;

        string timeframe = _workspace.Timeframe.ToString();
        bool viewportChanged =
            !StringComparer.Ordinal.Equals(_moduleVisualSymbol, _selectedSymbol) ||
            !StringComparer.Ordinal.Equals(_moduleVisualTimeframe, timeframe) ||
            _moduleVisibleStartIndex != window.StartIndex ||
            _moduleVisibleEndExclusive != window.EndExclusive;
        bool dataChanged = snapshot.Version != _modulePlanDataVersion;
        if (!viewportChanged && !dataChanged) return;

        if (viewportChanged)
            _moduleVisualViewportVersion++;

        _moduleVisualSymbol = _selectedSymbol;
        _moduleVisualTimeframe = timeframe;
        _moduleVisibleStartIndex = window.StartIndex;
        _moduleVisibleEndExclusive = window.EndExclusive;
        _modulePlanDataVersion = snapshot.Version;

        ModulePlatform.Recompose(
            snapshot.Version,
            _moduleVisualViewportVersion,
            themeVersion: 0,
            window.StartIndex,
            window.EndExclusive);
        _moduleRenderPlan = ModulePlatform.RenderPlan;
        UpdateModuleStatus();
    }
}
