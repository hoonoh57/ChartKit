using ChartKit.CSharp.Charting;
using ChartKit.CSharp.Contracts;
using ChartKit.CSharp.Modules.Abstractions;

namespace ChartKit.CSharp.App;

internal sealed partial class MainForm
{
    private bool _moduleVisualContextHooked;
    private bool _moduleVisualUpdatePending;
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

    private async void OnModuleVisualContextFrame(object? sender, EventArgs e)
    {
        if (_moduleVisualUpdatePending ||
            !_modulePlatformReady ||
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

        _moduleVisualUpdatePending = true;
        try
        {
            if (dataChanged)
            {
                ChartPrimarySeriesSnapshot primary =
                    CreatePrimarySeriesSnapshot(snapshot);
                await ModulePlatform.UpdatePrimarySeriesAsync(
                    primary,
                    _moduleVisualViewportVersion,
                    0,
                    window.StartIndex,
                    window.EndExclusive,
                    _stop.Token);
                _modulePlanDataVersion = snapshot.Version;
            }
            else
            {
                ModulePlatform.Recompose(
                    snapshot.Version,
                    _moduleVisualViewportVersion,
                    0,
                    window.StartIndex,
                    window.EndExclusive);
            }

            _moduleVisualSymbol = _selectedSymbol;
            _moduleVisualTimeframe = timeframe;
            _moduleVisibleStartIndex = window.StartIndex;
            _moduleVisibleEndExclusive = window.EndExclusive;
            _moduleRenderPlan = ModulePlatform.RenderPlan;
            UpdateModuleStatus();
            _chart.Invalidate();
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            ShowFailure("모듈 데이터 갱신 실패", exception);
        }
        finally
        {
            _moduleVisualUpdatePending = false;
        }
    }

    private static ChartPrimarySeriesSnapshot CreatePrimarySeriesSnapshot(
        SymbolSnapshot snapshot)
    {
        var bars = new ChartPrimaryBar[snapshot.Candles.Length];
        for (int index = 0; index < bars.Length; index++)
        {
            Candle candle = snapshot.Candles[index];
            bars[index] = new ChartPrimaryBar(
                candle.Sequence,
                candle.Open,
                candle.High,
                candle.Low,
                candle.Close,
                candle.Volume,
                candle.IsFinal);
        }
        return new ChartPrimarySeriesSnapshot(snapshot.Version, bars);
    }
}
