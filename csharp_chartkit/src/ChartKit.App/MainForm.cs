using System.Diagnostics.CodeAnalysis;
using ChartKit.CSharp.Charting;
using ChartKit.CSharp.Contracts;
using ChartKit.CSharp.DataSources;
using ChartKit.CSharp.Engine;
using ChartKit.CSharp.Rendering;
using SkiaSharp;
using SkiaSharp.Views.Desktop;

namespace ChartKit.CSharp.App;

internal sealed partial class MainForm : Form
{
    private readonly AppOptions _options;
    private readonly IMarketDataSource _source;
    private readonly MultiSymbolEngine _engine;
    private readonly ChartViewport _viewport;
    private readonly ChartWorkspaceState _workspace;
    private readonly ChartFrameBuilder _frameBuilder = new();
    private readonly ChartFrame _chartFrame = new();
    private readonly ChartLayoutOptions _layoutOptions = new();
    private readonly ChartCursorController _cursor = new();
    private readonly ChartLegendBuilder _legendBuilder = new();
    private readonly ChartLegendFrame _legendFrame = new();
    private readonly SkiaChartRenderer _renderer = new();
    private readonly ChartLegendRenderer _legendRenderer = new();
    private readonly ChartCrosshairRenderer _crosshairRenderer = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly SemaphoreSlim _reloadGate = new(1, 1);
    private readonly Dictionary<string, InstrumentMetadata> _metadataCache =
        new(StringComparer.Ordinal);
    private readonly SkiaSharp.Views.Desktop.SKControl _chart = new();
    private readonly System.Windows.Forms.Timer _frameTimer = new();
    private CancellationTokenSource? _streamStop;
    private Task? _streamTask;
    private InstrumentMetadata? _selectedMetadata;
    private string _selectedSymbol;
    private long _lastVersion = -1;
    private long _lastShellUpdateMilliseconds;
    private bool _dragging;
    private bool _draggingPricePanel;
    private int _lastDragX;
    private int _lastDragY;
    private double _dragBarRemainder;
    private int _closing;

    public MainForm(AppOptions options, IMarketDataSource source)
    {
        _options = options;
        _source = source;
        _selectedSymbol = options.Symbols[0];
        int initialBars = Math.Clamp(options.HistoryCount, 20, 240);
        _workspace = new ChartWorkspaceState(options.Timeframe, initialBars);
        _viewport = new ChartViewport(
            visibleBars: initialBars,
            minimumVisibleBars: 20,
            maximumVisibleBars: 5_000,
            rightBlankBars: 12,
            maximumRightBlankBars: 240);
        _engine = new MultiSymbolEngine(new MultiSymbolEngineOptions(
            WorkerCount: 0,
            QueueCapacityPerWorker: 8192,
            CandleCapacity: 100_000,
            SnapshotBars: Math.Max(5_000, options.HistoryCount),
            SnapshotInterval: TimeSpan.FromMilliseconds(50)));

        Text = $"ChartKit C# - {_source.Name}";
        Width = 1400;
        Height = 900;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(900, 600);

        _chart.Dock = DockStyle.Fill;
        _chart.BackColor = Color.Black;
        _chart.TabStop = true;
        _chart.PaintSurface += OnPaintSurface;
        _chart.MouseEnter += (_, _) => _chart.Focus();
        _chart.MouseLeave += OnChartMouseLeave;
        _chart.MouseWheel += OnChartMouseWheel;
        _chart.MouseDown += OnChartMouseDown;
        _chart.MouseMove += OnChartMouseMove;
        _chart.MouseUp += OnChartMouseUp;
        _chart.MouseDoubleClick += OnChartMouseDoubleClick;

        InitializeShell(options.Symbols);
        SynchronizeShellChecks();

        _frameTimer.Interval = 16;
        _frameTimer.Tick += OnFrame;
        Shown += OnShown;
        FormClosed += OnFormClosed;
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        Keys keyCode = keyData & Keys.KeyCode;
        if (!_symbolSelector.Focused &&
            !_timeframeSelector.Focused &&
            !_visibleBarsSelector.Focused &&
            HandleNavigationKey(keyCode))
            return true;
        return base.ProcessCmdKey(ref msg, keyData);
    }

    private async void OnShown(object? sender, EventArgs e)
    {
        try
        {
            await ReloadAsync(_workspace.Timeframe);
            _chart.Focus();
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            ShowFailure("ChartKit C# start failure", exception);
        }
    }

    private async Task ReloadAsync(CandleTimeframe timeframe)
    {
        await _reloadGate.WaitAsync(_stop.Token);
        try
        {
            _frameTimer.Stop();
            await StopStreamAsync();
            timeframe.Validate();
            _workspace.SetTimeframe(timeframe);
            SynchronizeShellChecks();
            _statusLabel.Text = $"{timeframe} 과거봉 조회 중...";

            foreach (string symbol in _options.Symbols)
            {
                IReadOnlyList<Candle> history = await _source.GetHistoryAsync(
                    new HistoryRequest(
                        symbol,
                        timeframe,
                        _options.HistoryCount),
                    _stop.Token);
                await _engine.LoadHistoryAsync(symbol, history, _stop.Token);
            }

            _lastVersion = -1;
            _cursor.Clear();
            if (TryGetSelectedSnapshot(out SymbolSnapshot? snapshot))
            {
                _viewport.Reset(snapshot.Candles.Length);
                _viewport.SetVisibleBars(
                    _workspace.RequestedVisibleBars,
                    snapshot.Candles.Length,
                    followLatest: true);
            }
            await RefreshSelectedMetadataAsync();

            if (SupportsRealtime(timeframe))
            {
                _streamStop = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
                CancellationToken streamToken = _streamStop.Token;
                _streamTask = PumpRealtimeAsync(timeframe, streamToken);
                _statusLabel.Text = $"{timeframe} 실시간 연결";
            }
            else
            {
                _statusLabel.Text = $"{timeframe} 과거 차트";
            }

            _frameTimer.Start();
            _chart.Invalidate();
        }
        finally
        {
            _reloadGate.Release();
        }
    }

    private static bool SupportsRealtime(CandleTimeframe timeframe) =>
        timeframe.Unit is CandleUnit.Minute or CandleUnit.Tick;

    private async Task StopStreamAsync()
    {
        CancellationTokenSource? streamStop = _streamStop;
        Task? streamTask = _streamTask;
        _streamStop = null;
        _streamTask = null;
        if (streamStop is null) return;

        streamStop.Cancel();
        if (streamTask is not null)
        {
            try
            {
                await streamTask;
            }
            catch (OperationCanceledException)
            {
            }
        }
        streamStop.Dispose();
    }

    private async Task PumpRealtimeAsync(
        CandleTimeframe timeframe,
        CancellationToken cancellationToken)
    {
        try
        {
            await foreach (CandleEvent value in _source.StreamAsync(
                               _options.Symbols,
                               timeframe,
                               cancellationToken))
            {
                await _engine.PublishAsync(value, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            if (!IsDisposed && IsHandleCreated)
            {
                BeginInvoke(() =>
                {
                    _statusLabel.Text = "실시간 오류: " + exception.Message;
                });
            }
        }
    }

    private async Task ChangeTimeframeAsync(CandleTimeframe timeframe)
    {
        if (timeframe == _workspace.Timeframe) return;
        try
        {
            await ReloadAsync(timeframe);
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            SynchronizeShellChecks();
            ShowFailure("주기 변경 실패", exception);
        }
    }

    private async Task SelectSymbolAsync(string symbol)
    {
        if (string.IsNullOrWhiteSpace(symbol)) return;
        _selectedSymbol = symbol.Trim();
        _selectedMetadata = null;
        _lastVersion = -1;
        _cursor.Clear();
        if (TryGetSelectedSnapshot(out SymbolSnapshot? snapshot))
        {
            _viewport.Reset(snapshot.Candles.Length);
            _viewport.SetVisibleBars(
                _workspace.RequestedVisibleBars,
                snapshot.Candles.Length,
                followLatest: true);
        }
        await RefreshSelectedMetadataAsync();
        _chart.Focus();
        _chart.Invalidate();
    }

    private async Task RefreshSelectedMetadataAsync()
    {
        string symbol = _selectedSymbol;
        if (_metadataCache.TryGetValue(symbol, out InstrumentMetadata? cached))
        {
            _selectedMetadata = cached;
            return;
        }

        InstrumentMetadata metadata;
        if (_source is IInstrumentMetadataSource metadataSource)
        {
            try
            {
                metadata = await metadataSource.GetInstrumentMetadataAsync(
                    symbol,
                    _stop.Token);
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                metadata = CreateFallbackMetadata(symbol);
            }
        }
        else
        {
            metadata = CreateFallbackMetadata(symbol);
        }

        _metadataCache[symbol] = metadata;
        if (string.Equals(_selectedSymbol, symbol, StringComparison.Ordinal))
            _selectedMetadata = metadata;
    }

    private InstrumentMetadata CreateFallbackMetadata(string symbol) =>
        new(symbol, symbol, "-", _source.Name, DateTimeOffset.UtcNow);

    private void OnFrame(object? sender, EventArgs e)
    {
        SymbolSnapshot? snapshot = null;
        bool changed = TryGetSelectedSnapshot(out snapshot) &&
                       snapshot.Version != _lastVersion;
        if (changed && snapshot is not null)
        {
            _lastVersion = snapshot.Version;
            if (_workspace.ShowCrosshair && _cursor.Current.IsVisible)
                UpdateCursor((int)_cursor.Current.X, (int)_cursor.Current.Y, snapshot);
            _chart.Invalidate();
        }

        long now = Environment.TickCount64;
        if (!changed && now - _lastShellUpdateMilliseconds < 250) return;
        _lastShellUpdateMilliseconds = now;
        EngineMetrics metrics = _engine.GetMetrics();
        ChartWindow window = snapshot is null
            ? ChartWindow.Empty
            : _viewport.Resolve(snapshot.Candles.Length);
        UpdateShell(snapshot, window, metrics);
    }

    private void OnPaintSurface(object? sender, SKPaintSurfaceEventArgs e)
    {
        if (!TryGetSelectedSnapshot(out SymbolSnapshot? snapshot))
        {
            e.Surface.Canvas.Clear(new SKColor(11, 15, 20));
            return;
        }

        ChartWindow window = _viewport.Resolve(snapshot.Candles.Length);
        if (window.IsEmpty)
        {
            e.Surface.Canvas.Clear(new SKColor(11, 15, 20));
            return;
        }

        BuildChartFrame(snapshot, window, e.Info.Width, e.Info.Height);
        _renderer.Render(
            e.Surface.Canvas,
            snapshot,
            _chartFrame,
            _workspace.RenderOptions);

        if (_workspace.ShowLegend)
        {
            int legendCandleIndex =
                _workspace.ShowCrosshair && _cursor.Current.IsVisible
                    ? _cursor.Current.CandleIndex
                    : snapshot.Candles.Length - 1;
            _legendBuilder.Build(snapshot, legendCandleIndex, _legendFrame);
            _legendRenderer.Render(e.Surface.Canvas, _chartFrame, _legendFrame);
        }
        if (_workspace.ShowCrosshair)
        {
            _crosshairRenderer.Render(
                e.Surface.Canvas,
                _chartFrame,
                _cursor.Current);
        }
    }

    private void OnChartMouseWheel(object? sender, MouseEventArgs e)
    {
        if (!TryGetSelectedSnapshot(out SymbolSnapshot? snapshot)) return;
        float anchor = Math.Clamp(
            (float)e.X / Math.Max(1, _chart.ClientSize.Width),
            0f,
            1f);
        _viewport.Zoom(e.Delta, snapshot.Candles.Length, anchor);
        if (_workspace.ShowCrosshair) UpdateCursor(e.X, e.Y, snapshot);
        _chart.Invalidate();
    }

    private void OnChartMouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        _dragging = true;
        _lastDragX = e.X;
        _lastDragY = e.Y;
        _dragBarRemainder = 0d;
        _chart.Capture = true;
        _chart.Focus();
        if (TryGetSelectedSnapshot(out SymbolSnapshot? snapshot))
        {
            if (_workspace.ShowCrosshair) UpdateCursor(e.X, e.Y, snapshot);
            _draggingPricePanel = e.Y >= _chartFrame.MainPanel.Top &&
                                  e.Y <= _chartFrame.MainPanel.Bottom;
        }
    }

    private void OnChartMouseMove(object? sender, MouseEventArgs e)
    {
        if (!TryGetSelectedSnapshot(out SymbolSnapshot? snapshot)) return;
        ChartWindow window = _viewport.Resolve(snapshot.Candles.Length);
        if (window.IsEmpty) return;

        if (_dragging)
        {
            int deltaPixels = e.X - _lastDragX;
            int deltaY = e.Y - _lastDragY;
            _lastDragX = e.X;
            _lastDragY = e.Y;
            double pixelsPerBar = Math.Max(
                1d,
                (double)Math.Max(1, _chart.ClientSize.Width) /
                Math.Max(1, window.VisibleSlotCount));
            _dragBarRemainder += deltaPixels / pixelsPerBar;
            int deltaBars = (int)Math.Truncate(_dragBarRemainder);
            if (deltaBars != 0)
            {
                _dragBarRemainder -= deltaBars;
                _viewport.Pan(deltaBars, snapshot.Candles.Length);
            }

            if (_draggingPricePanel && deltaY != 0)
            {
                float panelHeight = _chartFrame.MainPanel.IsEmpty
                    ? Math.Max(1f, _chart.ClientSize.Height * _layoutOptions.MainPanelRatio)
                    : _chartFrame.MainPanel.Height;
                _viewport.PanPricePixels(deltaY, panelHeight);
            }
        }

        if (_workspace.ShowCrosshair) UpdateCursor(e.X, e.Y, snapshot);
        _chart.Invalidate();
    }

    private void OnChartMouseUp(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        _dragging = false;
        _draggingPricePanel = false;
        _dragBarRemainder = 0d;
        _chart.Capture = false;
    }

    private void OnChartMouseLeave(object? sender, EventArgs e)
    {
        if (_dragging) return;
        _cursor.Clear();
        _chart.Invalidate();
    }

    private void OnChartMouseDoubleClick(object? sender, MouseEventArgs e)
    {
        FollowLatest();
        if (_workspace.ShowCrosshair &&
            TryGetSelectedSnapshot(out SymbolSnapshot? snapshot))
            UpdateCursor(e.X, e.Y, snapshot);
    }

    private bool HandleNavigationKey(Keys keyCode)
    {
        if (keyCode is not (Keys.Left or Keys.Right or Keys.PageUp or
                            Keys.PageDown or Keys.Home or Keys.End or Keys.Escape))
            return false;
        if (!TryGetSelectedSnapshot(out SymbolSnapshot? snapshot)) return false;

        ChartWindow window = _viewport.Resolve(snapshot.Candles.Length);
        int page = Math.Max(1, window.Count / 2);
        switch (keyCode)
        {
            case Keys.Left:
                _viewport.Pan(1, snapshot.Candles.Length);
                break;
            case Keys.Right:
                _viewport.Pan(-1, snapshot.Candles.Length);
                break;
            case Keys.PageUp:
                _viewport.Pan(page, snapshot.Candles.Length);
                break;
            case Keys.PageDown:
                _viewport.Pan(-page, snapshot.Candles.Length);
                break;
            case Keys.Home:
                _viewport.Pan(snapshot.Candles.Length, snapshot.Candles.Length);
                break;
            case Keys.End:
                _viewport.FollowLatest(snapshot.Candles.Length);
                break;
            case Keys.Escape:
                _viewport.Reset(snapshot.Candles.Length);
                _viewport.SetVisibleBars(
                    _workspace.RequestedVisibleBars,
                    snapshot.Candles.Length,
                    followLatest: true);
                break;
        }

        _cursor.Clear();
        _chart.Invalidate();
        return true;
    }

    private void ApplyVisibleBars(int bars)
    {
        _workspace.SetVisibleBars(bars);
        if (TryGetSelectedSnapshot(out SymbolSnapshot? snapshot))
            _viewport.SetVisibleBars(bars, snapshot.Candles.Length);
        SynchronizeShellChecks();
        _cursor.Clear();
        _chart.Focus();
        _chart.Invalidate();
    }

    private void FollowLatest()
    {
        if (!TryGetSelectedSnapshot(out SymbolSnapshot? snapshot)) return;
        _viewport.FollowLatest(snapshot.Candles.Length);
        _cursor.Clear();
        _chart.Focus();
        _chart.Invalidate();
    }

    private void ResetView()
    {
        if (!TryGetSelectedSnapshot(out SymbolSnapshot? snapshot)) return;
        _viewport.Reset(snapshot.Candles.Length);
        _viewport.SetVisibleBars(
            _workspace.RequestedVisibleBars,
            snapshot.Candles.Length,
            followLatest: true);
        _cursor.Clear();
        _chart.Focus();
        _chart.Invalidate();
    }

    private void SetDatesVisible(bool value)
    {
        _workspace.SetDates(value);
        SynchronizeShellChecks();
        _chart.Invalidate();
    }

    private void SetAxesVisible(bool value)
    {
        _workspace.SetAxes(value);
        SynchronizeShellChecks();
        _chart.Invalidate();
    }

    private void SetLegendVisible(bool value)
    {
        _workspace.SetLegend(value);
        SynchronizeShellChecks();
        _chart.Invalidate();
    }

    private void SetCrosshairVisible(bool value)
    {
        _workspace.SetCrosshair(value);
        if (!value) _cursor.Clear();
        SynchronizeShellChecks();
        _chart.Invalidate();
    }

    private void SetInfoPanelVisible(bool value)
    {
        _workspace.SetInfoPanel(value);
        SynchronizeShellChecks();
        _chart.Invalidate();
    }

    private void UpdateCursor(int x, int y, SymbolSnapshot snapshot)
    {
        ChartWindow window = _viewport.Resolve(snapshot.Candles.Length);
        if (window.IsEmpty)
        {
            _cursor.Clear();
            return;
        }

        BuildChartFrame(
            snapshot,
            window,
            Math.Max(1, _chart.ClientSize.Width),
            Math.Max(1, _chart.ClientSize.Height));
        _cursor.Update(x, y, snapshot, _chartFrame);
    }

    private void BuildChartFrame(
        SymbolSnapshot snapshot,
        ChartWindow window,
        float width,
        float height)
    {
        _frameBuilder.Build(
            snapshot,
            window,
            width,
            height,
            _layoutOptions,
            _chartFrame,
            transform: _viewport.Transform);
    }

    private bool TryGetSelectedSnapshot(
        [NotNullWhen(true)] out SymbolSnapshot? snapshot) =>
        _engine.TryGetSnapshot(_selectedSymbol, out snapshot) && snapshot is not null;

    private void ShowFailure(string title, Exception exception)
    {
        _statusLabel.Text = title + ": " + exception.Message;
        MessageBox.Show(
            this,
            exception.ToString(),
            title,
            MessageBoxButtons.OK,
            MessageBoxIcon.Error);
    }

    private void OnFormClosed(object? sender, FormClosedEventArgs e)
    {
        if (Interlocked.Exchange(ref _closing, 1) != 0) return;
        _frameTimer.Stop();
        _stop.Cancel();
        _streamStop?.Cancel();
        try { _streamTask?.GetAwaiter().GetResult(); }
        catch (OperationCanceledException) { }
        _streamStop?.Dispose();
        _source.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _engine.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _crosshairRenderer.Dispose();
        _legendRenderer.Dispose();
        _renderer.Dispose();
        _frameTimer.Dispose();
        _reloadGate.Dispose();
        _stop.Dispose();
    }
}
