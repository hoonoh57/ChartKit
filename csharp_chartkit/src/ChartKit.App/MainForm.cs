using System.Diagnostics.CodeAnalysis;
using ChartKit.CSharp.Charting;
using ChartKit.CSharp.Contracts;
using ChartKit.CSharp.DataSources;
using ChartKit.CSharp.Engine;
using ChartKit.CSharp.Rendering;
using SkiaSharp;
using SkiaSharp.Views.Desktop;

namespace ChartKit.CSharp.App;

internal sealed class MainForm : Form
{
    private readonly AppOptions _options;
    private readonly IMarketDataSource _source;
    private readonly MultiSymbolEngine _engine;
    private readonly ChartViewport _viewport;
    private readonly ChartFrameBuilder _frameBuilder = new();
    private readonly ChartFrame _chartFrame = new();
    private readonly ChartLayoutOptions _layoutOptions = new();
    private readonly ChartRenderOptions _renderOptions = new(ShowText: true, ShowAxes: true);
    private readonly ChartCursorController _cursor = new();
    private readonly ChartLegendBuilder _legendBuilder = new();
    private readonly ChartLegendFrame _legendFrame = new();
    private readonly SkiaChartRenderer _renderer = new();
    private readonly ChartLegendRenderer _legendRenderer = new();
    private readonly ChartCrosshairRenderer _crosshairRenderer = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly ComboBox _symbols = new();
    private readonly Label _status = new();
    private readonly SkiaSharp.Views.Desktop.SKControl _chart = new();
    private readonly System.Windows.Forms.Timer _frameTimer = new();
    private Task? _streamTask;
    private string _selectedSymbol;
    private long _lastVersion = -1;
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
        _viewport = new ChartViewport(
            visibleBars: Math.Clamp(options.HistoryCount, 20, 240),
            minimumVisibleBars: 20,
            maximumVisibleBars: 5_000,
            rightBlankBars: 12,
            maximumRightBlankBars: 240);
        _engine = new MultiSymbolEngine(new MultiSymbolEngineOptions(
            WorkerCount: 0,
            QueueCapacityPerWorker: 8192,
            CandleCapacity: 100_000,
            SnapshotBars: Math.Max(600, options.HistoryCount),
            SnapshotInterval: TimeSpan.FromMilliseconds(50)));

        Text = $"ChartKit C# - {_source.Name}";
        Width = 1400;
        Height = 900;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(900, 600);

        var toolbar = new Panel
        {
            Dock = DockStyle.Top,
            Height = 38,
            Padding = new Padding(8, 5, 8, 5)
        };
        _symbols.DropDownStyle = ComboBoxStyle.DropDownList;
        _symbols.Width = 150;
        _symbols.Items.AddRange(options.Symbols.Cast<object>().ToArray());
        _symbols.SelectedIndex = 0;
        _symbols.SelectedIndexChanged += (_, _) =>
        {
            if (_symbols.SelectedItem is string symbol)
            {
                _selectedSymbol = symbol;
                _lastVersion = -1;
                _cursor.Clear();
                if (TryGetSelectedSnapshot(out SymbolSnapshot? snapshot))
                    _viewport.Reset(snapshot.Candles.Length);
                _chart.Focus();
                _chart.Invalidate();
            }
        };

        _status.AutoSize = true;
        _status.Left = 170;
        _status.Top = 9;
        _status.Text = "Initializing C# engine...";
        toolbar.Controls.Add(_symbols);
        toolbar.Controls.Add(_status);

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
        Controls.Add(_chart);
        Controls.Add(toolbar);

        _frameTimer.Interval = 16;
        _frameTimer.Tick += OnFrame;
        Shown += OnShown;
        FormClosed += OnFormClosed;
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        Keys keyCode = keyData & Keys.KeyCode;
        if (!_symbols.Focused && HandleNavigationKey(keyCode)) return true;
        return base.ProcessCmdKey(ref msg, keyData);
    }

    private async void OnShown(object? sender, EventArgs e)
    {
        try
        {
            _status.Text = "Loading C# history...";
            foreach (string symbol in _options.Symbols)
            {
                IReadOnlyList<Candle> history = await _source.GetHistoryAsync(
                    new HistoryRequest(
                        symbol,
                        _options.Timeframe,
                        _options.HistoryCount),
                    _stop.Token);
                await _engine.LoadHistoryAsync(symbol, history, _stop.Token);
            }

            if (TryGetSelectedSnapshot(out SymbolSnapshot? snapshot))
                _viewport.Reset(snapshot.Candles.Length);
            _status.Text = "C# realtime running";
            _frameTimer.Start();
            _chart.Focus();
            _streamTask = Task.Run(PumpRealtimeAsync, CancellationToken.None);
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _status.Text = "Start failed: " + exception.Message;
            MessageBox.Show(
                this,
                exception.ToString(),
                "ChartKit C# start failure",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private async Task PumpRealtimeAsync()
    {
        try
        {
            await foreach (CandleEvent value in _source.StreamAsync(
                               _options.Symbols,
                               _options.Timeframe,
                               _stop.Token))
            {
                await _engine.PublishAsync(value, _stop.Token);
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            if (!IsDisposed && IsHandleCreated)
            {
                BeginInvoke(() =>
                {
                    _status.Text = "Realtime failed: " + exception.Message;
                });
            }
        }
    }

    private void OnFrame(object? sender, EventArgs e)
    {
        SymbolSnapshot? snapshot = null;
        if (TryGetSelectedSnapshot(out snapshot) &&
            snapshot.Version != _lastVersion)
        {
            _lastVersion = snapshot.Version;
            if (_cursor.Current.IsVisible)
                UpdateCursor((int)_cursor.Current.X, (int)_cursor.Current.Y, snapshot);
            _chart.Invalidate();
        }

        EngineMetrics metrics = _engine.GetMetrics();
        ChartWindow window = snapshot is null
            ? ChartWindow.Empty
            : _viewport.Resolve(snapshot.Candles.Length);
        _status.Text =
            $"{_source.Name} | {_options.Timeframe} | " +
            $"bars {window.Count:N0} gap {window.RightBlankBars:N0} " +
            $"offset {_viewport.RightOffsetBars:N0} | " +
            $"events {metrics.ProcessedEvents:N0} | " +
            $"queue max {metrics.MaxQueueDepth:N0} | " +
            $"latency {metrics.LastLatencyMicroseconds:N0}us";
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
            _renderOptions);

        int legendCandleIndex = _cursor.Current.IsVisible
            ? _cursor.Current.CandleIndex
            : snapshot.Candles.Length - 1;
        _legendBuilder.Build(snapshot, legendCandleIndex, _legendFrame);
        _legendRenderer.Render(e.Surface.Canvas, _chartFrame, _legendFrame);
        _crosshairRenderer.Render(
            e.Surface.Canvas,
            _chartFrame,
            _cursor.Current);
    }

    private void OnChartMouseWheel(object? sender, MouseEventArgs e)
    {
        if (!TryGetSelectedSnapshot(out SymbolSnapshot? snapshot)) return;
        float anchor = Math.Clamp(
            (float)e.X / Math.Max(1, _chart.ClientSize.Width),
            0f,
            1f);
        _viewport.Zoom(e.Delta, snapshot.Candles.Length, anchor);
        UpdateCursor(e.X, e.Y, snapshot);
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
            UpdateCursor(e.X, e.Y, snapshot);
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

        UpdateCursor(e.X, e.Y, snapshot);
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
        if (!TryGetSelectedSnapshot(out SymbolSnapshot? snapshot)) return;
        _viewport.FollowLatest(snapshot.Candles.Length);
        UpdateCursor(e.X, e.Y, snapshot);
        _chart.Invalidate();
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
                break;
        }

        _cursor.Clear();
        _chart.Invalidate();
        return true;
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

    private void OnFormClosed(object? sender, FormClosedEventArgs e)
    {
        if (Interlocked.Exchange(ref _closing, 1) != 0) return;
        _frameTimer.Stop();
        _stop.Cancel();
        try { _streamTask?.GetAwaiter().GetResult(); }
        catch (OperationCanceledException) { }
        _source.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _engine.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _crosshairRenderer.Dispose();
        _legendRenderer.Dispose();
        _renderer.Dispose();
        _frameTimer.Dispose();
        _stop.Dispose();
    }
}