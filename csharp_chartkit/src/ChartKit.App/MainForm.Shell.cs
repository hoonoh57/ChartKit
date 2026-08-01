using System.Globalization;
using ChartKit.CSharp.Charting;
using ChartKit.CSharp.Contracts;
using ChartKit.CSharp.DataSources;

namespace ChartKit.CSharp.App;

internal sealed partial class MainForm
{
    private static readonly TimeframeChoice[] TimeframeChoices =
    [
        new("1분", CandleTimeframe.Minute(1)),
        new("3분", CandleTimeframe.Minute(3)),
        new("5분", CandleTimeframe.Minute(5)),
        new("10분", CandleTimeframe.Minute(10)),
        new("20분", CandleTimeframe.Minute(20)),
        new("30분", CandleTimeframe.Minute(30)),
        new("60분", CandleTimeframe.Minute(60)),
        new("120분", CandleTimeframe.Minute(120)),
        new("일", CandleTimeframe.Day),
        new("주", CandleTimeframe.Week),
        new("월", CandleTimeframe.Month),
        new("1틱", CandleTimeframe.Tick(1)),
        new("5틱", CandleTimeframe.Tick(5)),
        new("10틱", CandleTimeframe.Tick(10)),
        new("30틱", CandleTimeframe.Tick(30)),
        new("60틱", CandleTimeframe.Tick(60)),
        new("120틱", CandleTimeframe.Tick(120))
    ];

    private static readonly int[] VisibleBarChoices =
        [30, 60, 120, 240, 500, 1_000, 2_000, 5_000];

    private readonly ToolStrip _toolbar = new();
    private readonly ToolStripComboBox _symbolSelector = new();
    private readonly ToolStripLabel _symbolNameLabel = new("종목명");
    private readonly ToolStripComboBox _timeframeSelector = new();
    private readonly ToolStripComboBox _visibleBarsSelector = new();
    private readonly ToolStripLabel _countLabel = new("표시 0 / 총 0");
    private readonly ToolStripButton _dateButton = new("일자");
    private readonly ToolStripButton _infoButton = new("종목정보");
    private readonly ToolStripDropDownButton _toolsButton = new("도구");
    private readonly StatusStrip _statusStrip = new();
    private readonly ToolStripStatusLabel _statusLabel = new();
    private readonly SplitContainer _workspaceSplit = new();
    private readonly TableLayoutPanel _infoTable = new();
    private readonly Dictionary<string, Label> _infoValues =
        new(StringComparer.Ordinal);
    private readonly ContextMenuStrip _chartContextMenu = new();
    private ToolStripMenuItem? _contextDates;
    private ToolStripMenuItem? _contextAxes;
    private ToolStripMenuItem? _contextLegend;
    private ToolStripMenuItem? _contextCrosshair;
    private ToolStripMenuItem? _contextInfo;
    private bool _updatingShellControls;
    private bool _workspaceSplitterInitialized;

    private void InitializeShell(IReadOnlyList<string> symbols)
    {
        SuspendLayout();

        _toolbar.Dock = DockStyle.Top;
        _toolbar.GripStyle = ToolStripGripStyle.Hidden;
        _toolbar.AutoSize = false;
        _toolbar.Height = 36;
        _toolbar.Padding = new Padding(4, 3, 4, 3);

        _symbolSelector.DropDownStyle = ComboBoxStyle.DropDownList;
        _symbolSelector.AutoSize = false;
        _symbolSelector.Width = 118;
        _symbolSelector.Items.AddRange(symbols.Cast<object>().ToArray());
        _symbolSelector.SelectedIndexChanged += async (_, _) =>
        {
            if (_updatingShellControls ||
                _symbolSelector.SelectedItem is not string symbol)
                return;
            await SelectSymbolAsync(symbol);
        };

        _symbolNameLabel.AutoSize = false;
        _symbolNameLabel.Width = 130;
        _symbolNameLabel.TextAlign = ContentAlignment.MiddleLeft;

        _timeframeSelector.DropDownStyle = ComboBoxStyle.DropDownList;
        _timeframeSelector.AutoSize = false;
        _timeframeSelector.Width = 72;
        _timeframeSelector.Items.AddRange(TimeframeChoices.Cast<object>().ToArray());
        _timeframeSelector.SelectedIndexChanged += async (_, _) =>
        {
            if (_updatingShellControls ||
                _timeframeSelector.SelectedItem is not TimeframeChoice choice)
                return;
            await ChangeTimeframeAsync(choice.Value);
        };

        _visibleBarsSelector.DropDownStyle = ComboBoxStyle.DropDownList;
        _visibleBarsSelector.AutoSize = false;
        _visibleBarsSelector.Width = 74;
        _visibleBarsSelector.Items.AddRange(
            VisibleBarChoices.Select(value => (object)value).ToArray());
        _visibleBarsSelector.SelectedIndexChanged += (_, _) =>
        {
            if (_updatingShellControls ||
                _visibleBarsSelector.SelectedItem is not int bars)
                return;
            ApplyVisibleBars(bars);
        };

        _countLabel.AutoSize = false;
        _countLabel.Width = 138;
        _countLabel.TextAlign = ContentAlignment.MiddleLeft;

        _dateButton.CheckOnClick = true;
        _dateButton.Checked = true;
        _dateButton.Click += (_, _) => SetDatesVisible(_dateButton.Checked);

        _infoButton.CheckOnClick = true;
        _infoButton.Checked = true;
        _infoButton.Click += (_, _) => SetInfoPanelVisible(_infoButton.Checked);

        BuildToolsMenu();
        _toolbar.Items.AddRange(
        [
            new ToolStripLabel("종목"),
            _symbolSelector,
            _symbolNameLabel,
            new ToolStripSeparator(),
            new ToolStripLabel("주기"),
            _timeframeSelector,
            new ToolStripLabel("표시"),
            _visibleBarsSelector,
            _countLabel,
            new ToolStripSeparator(),
            _dateButton,
            _infoButton,
            _toolsButton
        ]);

        _statusLabel.Spring = true;
        _statusLabel.TextAlign = ContentAlignment.MiddleLeft;
        _statusLabel.Text = "C# chart shell initializing...";
        _statusStrip.Items.Add(_statusLabel);
        _statusStrip.Dock = DockStyle.Bottom;

        _workspaceSplit.Dock = DockStyle.Fill;
        _workspaceSplit.Orientation = Orientation.Vertical;
        _workspaceSplit.FixedPanel = FixedPanel.Panel2;
        _workspaceSplit.SplitterWidth = 4;
        _workspaceSplit.Layout += OnWorkspaceSplitLayout;
        _workspaceSplit.Panel1.Controls.Add(_chart);
        BuildInstrumentInfoPanel();
        _workspaceSplit.Panel2.Controls.Add(_infoTable);

        _chart.ContextMenuStrip = _chartContextMenu;
        BuildChartContextMenu();

        Controls.Add(_workspaceSplit);
        Controls.Add(_statusStrip);
        Controls.Add(_toolbar);

        _updatingShellControls = true;
        try
        {
            _symbolSelector.SelectedItem = _selectedSymbol;
            SelectTimeframeControl(_workspace.Timeframe);
            SelectVisibleBarsControl(_workspace.RequestedVisibleBars);
        }
        finally
        {
            _updatingShellControls = false;
        }

        ResumeLayout(performLayout: true);
        InitializeWorkspaceSplitter();
    }

    private void OnWorkspaceSplitLayout(object? sender, LayoutEventArgs e) =>
        InitializeWorkspaceSplitter();

    private void InitializeWorkspaceSplitter()
    {
        if (_workspaceSplitterInitialized) return;

        const int panel1Minimum = 320;
        const int panel2Minimum = 220;
        const int panel2Preferred = 260;
        int availableWidth =
            _workspaceSplit.ClientSize.Width - _workspaceSplit.SplitterWidth;
        if (availableWidth < panel1Minimum + panel2Minimum) return;

        _workspaceSplitterInitialized = true;
        try
        {
            _workspaceSplit.Panel1MinSize = panel1Minimum;
            _workspaceSplit.Panel2MinSize = panel2Minimum;
            _workspaceSplit.SplitterDistance = Math.Clamp(
                availableWidth - panel2Preferred,
                panel1Minimum,
                availableWidth - panel2Minimum);
        }
        catch
        {
            _workspaceSplitterInitialized = false;
            throw;
        }
    }

    private void BuildToolsMenu()
    {
        _toolsButton.DropDownItems.Add("최신봉으로", null, (_, _) => FollowLatest());
        _toolsButton.DropDownItems.Add("화면 초기화", null, (_, _) => ResetView());
        _toolsButton.DropDownItems.Add(new ToolStripSeparator());
        _toolsButton.DropDownItems.Add(
            CreateCheckedMenu("십자선", true, SetCrosshairVisible));
        _toolsButton.DropDownItems.Add(
            CreateCheckedMenu("레전드", true, SetLegendVisible));
        _toolsButton.DropDownItems.Add(
            CreateCheckedMenu("축", true, SetAxesVisible));
        _toolsButton.DropDownItems.Add(
            CreateCheckedMenu("일자 경계", true, SetDatesVisible));
    }

    private void BuildChartContextMenu()
    {
        _chartContextMenu.Items.Add("최신봉으로", null, (_, _) => FollowLatest());
        _chartContextMenu.Items.Add("화면 초기화", null, (_, _) => ResetView());
        _chartContextMenu.Items.Add(new ToolStripSeparator());

        _contextCrosshair = CreateCheckedMenu("십자선", true, SetCrosshairVisible);
        _contextLegend = CreateCheckedMenu("레전드", true, SetLegendVisible);
        _contextAxes = CreateCheckedMenu("축", true, SetAxesVisible);
        _contextDates = CreateCheckedMenu("일자 경계", true, SetDatesVisible);
        _contextInfo = CreateCheckedMenu("종목정보 패널", true, SetInfoPanelVisible);
        _chartContextMenu.Items.AddRange(
            [_contextCrosshair, _contextLegend, _contextAxes, _contextDates, _contextInfo]);

        var timeframeMenu = new ToolStripMenuItem("주기");
        foreach (TimeframeChoice choice in TimeframeChoices)
        {
            TimeframeChoice captured = choice;
            timeframeMenu.DropDownItems.Add(
                choice.Text,
                null,
                async (_, _) => await ChangeTimeframeAsync(captured.Value));
        }
        _chartContextMenu.Items.Add(timeframeMenu);

        var barsMenu = new ToolStripMenuItem("표시 봉수");
        foreach (int bars in VisibleBarChoices)
        {
            int captured = bars;
            barsMenu.DropDownItems.Add(
                bars.ToString("N0", CultureInfo.InvariantCulture),
                null,
                (_, _) => ApplyVisibleBars(captured));
        }
        _chartContextMenu.Items.Add(barsMenu);
    }

    private static ToolStripMenuItem CreateCheckedMenu(
        string text,
        bool initial,
        Action<bool> changed)
    {
        var item = new ToolStripMenuItem(text)
        {
            CheckOnClick = true,
            Checked = initial
        };
        item.Click += (_, _) => changed(item.Checked);
        return item;
    }

    private void BuildInstrumentInfoPanel()
    {
        _infoTable.Dock = DockStyle.Fill;
        _infoTable.Padding = new Padding(8);
        _infoTable.AutoScroll = true;
        _infoTable.BackColor = Color.FromArgb(24, 28, 34);
        _infoTable.ForeColor = Color.Gainsboro;
        _infoTable.ColumnCount = 2;
        _infoTable.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 76f));
        _infoTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        _infoTable.RowCount = 0;

        AddInfoRow("코드", "Code");
        AddInfoRow("종목명", "Name");
        AddInfoRow("시장", "Market");
        AddInfoRow("데이터", "Source");
        AddInfoRow("주기", "Timeframe");
        AddInfoRow("봉수", "Counts");
        AddInfoRow("시각", "Time");
        AddInfoRow("시가", "Open");
        AddInfoRow("고가", "High");
        AddInfoRow("저가", "Low");
        AddInfoRow("종가", "Close");
        AddInfoRow("거래량", "Volume");
        AddInfoRow("실시간", "Realtime");
        AddInfoRow("경계", "Boundary");
        AddInfoRow("상태", "Connection");
    }

    private void AddInfoRow(string caption, string key)
    {
        int row = _infoTable.RowCount++;
        _infoTable.RowStyles.Add(new RowStyle(SizeType.Absolute, 25f));
        var captionLabel = new Label
        {
            Text = caption,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Color.DarkGray
        };
        var valueLabel = new Label
        {
            Text = "-",
            Dock = DockStyle.Fill,
            AutoEllipsis = true,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Color.Gainsboro
        };
        _infoValues.Add(key, valueLabel);
        _infoTable.Controls.Add(captionLabel, 0, row);
        _infoTable.Controls.Add(valueLabel, 1, row);
    }

    private void UpdateShell(
        SymbolSnapshot? snapshot,
        ChartWindow window,
        EngineMetrics metrics)
    {
        string name = _selectedMetadata?.DisplayName ?? _selectedSymbol;
        RealtimeDiagnosticsSnapshot realtime = GetRealtimeDiagnostics();
        _symbolNameLabel.Text = name;
        _countLabel.Text = snapshot is null
            ? "표시 0 / 총 0"
            : $"표시 {window.Count:N0} / 총 {snapshot.Candles.Length:N0}";
        _statusLabel.Text =
            $"{_source.Name} | {_workspace.Timeframe} | " +
            $"gap {window.RightBlankBars:N0} offset {_viewport.RightOffsetBars:N0} | " +
            $"events {metrics.ProcessedEvents:N0} queue {metrics.MaxQueueDepth:N0} | " +
            $"ws {FormatConnectionState(realtime.ConnectionState)} " +
            $"boundary {FormatBoundaryState(realtime.BoundaryState)} " +
            $"stale {realtime.RejectedStaleEvents:N0} | " +
            $"latency {metrics.LastLatencyMicroseconds:N0}us";
        Text = $"ChartKit C# - {_selectedSymbol} {name} [{_workspace.Timeframe}]";
        UpdateInfoPanel(snapshot, window, metrics, realtime);
    }

    private void UpdateInfoPanel(
        SymbolSnapshot? snapshot,
        ChartWindow window,
        EngineMetrics metrics,
        RealtimeDiagnosticsSnapshot realtime)
    {
        SetInfoValue("Code", _selectedSymbol);
        SetInfoValue("Name", _selectedMetadata?.DisplayName ?? _selectedSymbol);
        SetInfoValue("Market", _selectedMetadata?.Market ?? "-");
        SetInfoValue("Source", _selectedMetadata?.Source ?? _source.Name);
        SetInfoValue("Timeframe", _workspace.Timeframe.ToString());
        SetInfoValue(
            "Counts",
            snapshot is null ? "0 / 0" : $"{window.Count:N0} / {snapshot.Candles.Length:N0}");
        SetInfoValue("Realtime", FormatRealtimeSummary(realtime));
        SetInfoValue("Boundary", FormatBoundarySummary(realtime));
        SetInfoValue(
            "Connection",
            $"events {metrics.ProcessedEvents:N0}, errors {metrics.ProcessingErrors:N0}");

        if (snapshot is null || snapshot.Candles.Length == 0)
        {
            foreach (string key in new[] { "Time", "Open", "High", "Low", "Close", "Volume" })
                SetInfoValue(key, "-");
            return;
        }

        int index = _workspace.ShowCrosshair && _cursor.Current.IsVisible
            ? Math.Clamp(_cursor.Current.CandleIndex, 0, snapshot.Candles.Length - 1)
            : snapshot.Candles.Length - 1;
        Candle candle = snapshot.Candles[index];
        SetInfoValue("Time", candle.OpenTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
        SetInfoValue("Open", FormatInfoNumber(candle.Open));
        SetInfoValue("High", FormatInfoNumber(candle.High));
        SetInfoValue("Low", FormatInfoNumber(candle.Low));
        SetInfoValue("Close", FormatInfoNumber(candle.Close));
        SetInfoValue("Volume", candle.Volume.ToString("N0", CultureInfo.InvariantCulture));
    }

    private RealtimeDiagnosticsSnapshot GetRealtimeDiagnostics() =>
        _source is IRealtimeDiagnosticsSource diagnosticsSource
            ? diagnosticsSource.GetRealtimeDiagnostics(_selectedSymbol)
            : RealtimeDiagnosticsSnapshot.Empty(_selectedSymbol);

    private static string FormatRealtimeSummary(
        RealtimeDiagnosticsSnapshot realtime)
    {
        string state = FormatConnectionState(realtime.ConnectionState);
        string events =
            $"U {realtime.UpdateEvents:N0} / A {realtime.AppendEvents:N0}";
        string attempts =
            $"try {realtime.ConnectionAttempts:N0} / reg {realtime.RegistrationCount:N0}";
        return $"{state}, {events}, {attempts}";
    }

    private static string FormatBoundarySummary(
        RealtimeDiagnosticsSnapshot realtime)
    {
        string boundary = FormatBoundaryState(realtime.BoundaryState);
        string seed = realtime.SeedCloseTime.HasValue
            ? $"seed {realtime.SeedCloseTime.Value:MM-dd HH:mm:ss}"
            : "seed -";
        string first = realtime.FirstRealtimeTime.HasValue
            ? $"first {realtime.FirstRealtimeTime.Value:MM-dd HH:mm:ss}"
            : "first -";
        return $"{boundary}, {seed}, {first}, stale {realtime.RejectedStaleEvents:N0}";
    }

    private static string FormatConnectionState(RealtimeConnectionState state) =>
        state switch
        {
            RealtimeConnectionState.Idle => "idle",
            RealtimeConnectionState.Connecting => "connecting",
            RealtimeConnectionState.Connected => "connected",
            RealtimeConnectionState.LoggedIn => "login",
            RealtimeConnectionState.Registered => "registered",
            RealtimeConnectionState.Receiving => "receiving",
            RealtimeConnectionState.Reconnecting => "reconnecting",
            RealtimeConnectionState.Faulted => "faulted",
            RealtimeConnectionState.Stopped => "stopped",
            _ => state.ToString()
        };

    private static string FormatBoundaryState(RealtimeBoundaryState state) =>
        state switch
        {
            RealtimeBoundaryState.None => "none",
            RealtimeBoundaryState.AwaitingFirstEvent => "waiting",
            RealtimeBoundaryState.SeedUpdated => "seed-update",
            RealtimeBoundaryState.SeedAppended => "seed-append",
            RealtimeBoundaryState.UnseededAppended => "no-seed-append",
            RealtimeBoundaryState.RejectedStaleBeforeFirstEvent => "stale-before-first",
            _ => state.ToString()
        };

    private void SetInfoValue(string key, string value)
    {
        if (_infoValues.TryGetValue(key, out Label? label)) label.Text = value;
    }

    private static string FormatInfoNumber(float value) =>
        value.ToString(Math.Abs(value) >= 100f ? "N0" : "N2", CultureInfo.InvariantCulture);

    private void SynchronizeShellChecks()
    {
        _updatingShellControls = true;
        try
        {
            _dateButton.Checked = _workspace.ShowDates;
            _infoButton.Checked = _workspace.ShowInfoPanel;
            if (_contextDates is not null) _contextDates.Checked = _workspace.ShowDates;
            if (_contextAxes is not null) _contextAxes.Checked = _workspace.ShowAxes;
            if (_contextLegend is not null) _contextLegend.Checked = _workspace.ShowLegend;
            if (_contextCrosshair is not null) _contextCrosshair.Checked = _workspace.ShowCrosshair;
            if (_contextInfo is not null) _contextInfo.Checked = _workspace.ShowInfoPanel;
            _workspaceSplit.Panel2Collapsed = !_workspace.ShowInfoPanel;
            SelectTimeframeControl(_workspace.Timeframe);
            SelectVisibleBarsControl(_workspace.RequestedVisibleBars);
        }
        finally
        {
            _updatingShellControls = false;
        }
    }

    private void SelectTimeframeControl(CandleTimeframe timeframe)
    {
        for (int index = 0; index < _timeframeSelector.Items.Count; index++)
        {
            if (_timeframeSelector.Items[index] is TimeframeChoice choice &&
                choice.Value == timeframe)
            {
                _timeframeSelector.SelectedIndex = index;
                return;
            }
        }
    }

    private void SelectVisibleBarsControl(int visibleBars)
    {
        for (int index = 0; index < _visibleBarsSelector.Items.Count; index++)
        {
            if (_visibleBarsSelector.Items[index] is int value && value == visibleBars)
            {
                _visibleBarsSelector.SelectedIndex = index;
                return;
            }
        }
        _visibleBarsSelector.Text = visibleBars.ToString(CultureInfo.InvariantCulture);
    }

    private sealed record TimeframeChoice(string Text, CandleTimeframe Value)
    {
        public override string ToString() => Text;
    }
}
