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
    private readonly SkiaChartRenderer _renderer = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly ComboBox _symbols = new();
    private readonly Label _status = new();
    private readonly SkiaSharp.Views.Desktop.SKControl _chart = new();
    private readonly System.Windows.Forms.Timer _frameTimer = new();
    private Task? _streamTask;
    private string _selectedSymbol;
    private long _lastVersion = -1;
    private int _closing;

    public MainForm(AppOptions options, IMarketDataSource source)
    {
        _options = options;
        _source = source;
        _selectedSymbol = options.Symbols[0];
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
        _chart.PaintSurface += OnPaintSurface;
        Controls.Add(_chart);
        Controls.Add(toolbar);

        _frameTimer.Interval = 16;
        _frameTimer.Tick += OnFrame;
        Shown += OnShown;
        FormClosed += OnFormClosed;
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

            _status.Text = "C# realtime running";
            _frameTimer.Start();
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
        if (_engine.TryGetSnapshot(
                _selectedSymbol,
                out SymbolSnapshot? snapshot) &&
            snapshot is not null &&
            snapshot.Version != _lastVersion)
        {
            _lastVersion = snapshot.Version;
            _chart.Invalidate();
        }

        EngineMetrics metrics = _engine.GetMetrics();
        _status.Text =
            $"{_source.Name} | {_options.Timeframe} | " +
            $"events {metrics.ProcessedEvents:N0} | " +
            $"queue max {metrics.MaxQueueDepth:N0} | " +
            $"latency {metrics.LastLatencyMicroseconds:N0}us";
    }

    private void OnPaintSurface(object? sender, SKPaintSurfaceEventArgs e)
    {
        if (!_engine.TryGetSnapshot(
                _selectedSymbol,
                out SymbolSnapshot? snapshot) ||
            snapshot is null)
        {
            e.Surface.Canvas.Clear(new SKColor(11, 15, 20));
            return;
        }

        _renderer.Render(
            e.Surface.Canvas,
            new SKRect(0, 0, e.Info.Width, e.Info.Height),
            snapshot,
            new ChartRenderOptions(
                VisibleBars: Math.Min(240, Math.Max(1, snapshot.Candles.Length)),
                ShowText: true));
    }

    private void OnFormClosed(object? sender, FormClosedEventArgs e)
    {
        if (Interlocked.Exchange(ref _closing, 1) != 0) return;
        _frameTimer.Stop();
        _stop.Cancel();
        try { _streamTask?.GetAwaiter().GetResult(); }
        catch (OperationCanceledException) { }
        _source.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _engine.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _renderer.Dispose();
        _frameTimer.Dispose();
        _stop.Dispose();
    }
}
