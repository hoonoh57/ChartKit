namespace ChartKit.CSharp.App;

internal sealed partial class MainForm
{
    private readonly ToolStripStatusLabel _dataRequestStatusLabel = new()
    {
        AutoSize = true,
        BorderSides = ToolStripStatusLabelBorderSides.Left,
        Padding = new Padding(8, 0, 4, 0),
        Visible = false
    };
    private readonly System.Windows.Forms.Timer _dataRequestStatusTimer = new()
    {
        Interval = 100
    };
    private DataRequestScheduler? _dataRequestScheduler;
    private bool _dataRequestLifecycleInstalled;

    private void InstallDataRequestLifecycle()
    {
        if (_dataRequestLifecycleInstalled) return;
        _dataRequestLifecycleInstalled = true;

        _dataRequestScheduler = new DataRequestScheduler(_stop.Token);
        _dataRequestScheduler.StateChanged += OnDataRequestSchedulerStateChanged;
        _statusStrip.Items.Add(_dataRequestStatusLabel);
        _dataRequestStatusTimer.Tick += (_, _) => RefreshDataRequestStatus();
        _dataRequestStatusTimer.Start();

        Shown -= OnShown;
        Shown += OnDataControlsShown;
        FormClosing += OnDataControlsFormClosing;
    }

    private async void OnDataControlsShown(object? sender, EventArgs e)
    {
        await ExecuteDataCommandAsync(
            () => ReloadActiveSymbolsAsync(
                _workspace.Timeframe,
                reloadAll: true));
        if (!IsDisposed) _chart.Focus();
    }

    private void OnDataControlsFormClosing(
        object? sender,
        FormClosingEventArgs e)
    {
        _dataRequestStatusTimer.Stop();
        _dataRequestStatusTimer.Dispose();
        _streamStop?.Cancel();
        if (_dataRequestScheduler is not null)
        {
            _dataRequestScheduler.StateChanged -=
                OnDataRequestSchedulerStateChanged;
            _dataRequestScheduler.Dispose();
            _dataRequestScheduler = null;
        }
    }

    private Task<DataRequestOutcome> EnqueueDataCommandAsync(
        Func<Task> command)
    {
        ArgumentNullException.ThrowIfNull(command);
        DataRequestScheduler scheduler = _dataRequestScheduler ??
            throw new InvalidOperationException(
                "Data request scheduler is not initialized.");
        string description = BuildDataRequestDescription();
        return scheduler.EnqueueAsync(
            description,
            _ => command());
    }

    private string BuildDataRequestDescription()
    {
        string symbol = _dataSymbolEditor.Text.Trim();
        if (symbol.Length == 0) symbol = _selectedSymbol;
        string timeframe = _dataTimeframeEditor.Text.Trim();
        if (timeframe.Length == 0) timeframe = _workspace.Timeframe.ToString();
        string count = _historyCountEditor.Text.Trim();
        return count.Length == 0
            ? $"{symbol} {timeframe}"
            : $"{symbol} {timeframe} {count}봉";
    }

    private void OnDataRequestSchedulerStateChanged()
    {
        if (IsDisposed) return;
        if (IsHandleCreated && InvokeRequired)
        {
            try
            {
                BeginInvoke((Action)RefreshDataRequestStatus);
            }
            catch (InvalidOperationException)
            {
            }
            return;
        }
        RefreshDataRequestStatus();
    }

    private void RefreshDataRequestStatus()
    {
        if (IsDisposed) return;
        DataRequestScheduler? scheduler = _dataRequestScheduler;
        if (scheduler is null) return;

        DataRequestSchedulerSnapshot snapshot = scheduler.GetSnapshot();

        _reloadDataButton.Enabled = !_stop.IsCancellationRequested;
        if (snapshot.IsRunning)
        {
            _dataRequestStatusLabel.Visible = true;
            if (snapshot.HasPending)
            {
                _dataRequestStatusLabel.Text =
                    $"이전 요청 처리 중 {FormatRequestSeconds(snapshot.RunningElapsedMilliseconds)}" +
                    $" · 다음 요청 대기 {FormatRequestSeconds(snapshot.PendingWaitMilliseconds)}";
                _reloadDataButton.Text = "대기";
            }
            else
            {
                _dataRequestStatusLabel.Text =
                    $"데이터 처리 중 {FormatRequestSeconds(snapshot.RunningElapsedMilliseconds)}";
                _reloadDataButton.Text = "처리중";
            }
            return;
        }

        _reloadDataButton.Text = "조회";
        if (snapshot.TotalCompleted == 0)
        {
            _dataRequestStatusLabel.Visible = false;
            _dataRequestStatusLabel.Text = string.Empty;
            return;
        }

        _dataRequestStatusLabel.Visible = true;
        _dataRequestStatusLabel.Text =
            $"최근 {FormatRequestSeconds(snapshot.LastCompletedMilliseconds)}" +
            $" · 최대 {FormatRequestSeconds(snapshot.MaxCompletedMilliseconds)}" +
            (snapshot.TotalCoalesced == 0
                ? string.Empty
                : $" · 대기 병합 {snapshot.TotalCoalesced:N0}");
    }

    private static string FormatRequestSeconds(long milliseconds) =>
        $"{milliseconds / 1000d:0.0}s";
}
