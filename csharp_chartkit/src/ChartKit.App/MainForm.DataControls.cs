using System.Globalization;
using ChartKit.CSharp.Charting;
using ChartKit.CSharp.Contracts;

namespace ChartKit.CSharp.App;

internal sealed partial class MainForm
{
    private const int MinimumHistoryCount = 20;
    private const int MaximumHistoryCount = 5_000;

    private readonly ToolStripComboBox _dataSymbolEditor = new();
    private readonly ToolStripComboBox _dataTimeframeEditor = new();
    private readonly ToolStripTextBox _historyCountEditor = new();
    private readonly ToolStripComboBox _displayCountEditor = new();
    private readonly ToolStripButton _reloadDataButton = new("조회");
    private readonly List<string> _activeSymbols = new();
    private bool _dataControlsInitialized;
    private bool _updatingDataControls;
    private int _requestedHistoryCount;
    private int _lastSynchronizedVisibleBars = -1;

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        InitializeDataControls();
    }

    private void InitializeDataControls()
    {
        if (_dataControlsInitialized) return;
        _dataControlsInitialized = true;
        _requestedHistoryCount = Math.Clamp(
            _options.HistoryCount,
            MinimumHistoryCount,
            MaximumHistoryCount);
        foreach (string symbol in _options.Symbols)
            AddActiveSymbol(NormalizeSymbol(symbol));

        ConfigureSymbolEditor();
        ConfigureTimeframeEditor();
        ConfigureHistoryCountEditor();
        ConfigureDisplayCountEditor();
        RebuildDataToolbar();
        RebuildDataContextMenu();
        SynchronizeDataControls();
        _frameTimer.Tick += (_, _) => SynchronizeViewportDisplayCount();
    }

    private void ConfigureSymbolEditor()
    {
        _dataSymbolEditor.DropDownStyle = ComboBoxStyle.DropDown;
        _dataSymbolEditor.AutoSize = false;
        _dataSymbolEditor.Width = 118;
        _dataSymbolEditor.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
        _dataSymbolEditor.AutoCompleteSource = AutoCompleteSource.ListItems;
        _dataSymbolEditor.Items.AddRange(_activeSymbols.Cast<object>().ToArray());
        _dataSymbolEditor.KeyDown += async (_, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            e.SuppressKeyPress = true;
            await ExecuteDataCommandAsync(
                () => CommitSymbolAsync(_dataSymbolEditor.Text));
        };
        _dataSymbolEditor.SelectedIndexChanged += async (_, _) =>
        {
            if (_updatingDataControls ||
                _dataSymbolEditor.SelectedItem is not string symbol)
                return;
            await ExecuteDataCommandAsync(() => CommitSymbolAsync(symbol));
        };
    }

    private void ConfigureTimeframeEditor()
    {
        _dataTimeframeEditor.DropDownStyle = ComboBoxStyle.DropDownList;
        _dataTimeframeEditor.AutoSize = false;
        _dataTimeframeEditor.Width = 72;
        _dataTimeframeEditor.Items.AddRange(
            TimeframeChoices.Cast<object>().ToArray());
        _dataTimeframeEditor.SelectedIndexChanged += async (_, _) =>
        {
            if (_updatingDataControls ||
                _dataTimeframeEditor.SelectedItem is not TimeframeChoice choice ||
                choice.Value == _workspace.Timeframe)
                return;
            await ExecuteDataCommandAsync(
                () => ReloadActiveSymbolsAsync(choice.Value, reloadAll: true));
        };
    }

    private void ConfigureHistoryCountEditor()
    {
        _historyCountEditor.AutoSize = false;
        _historyCountEditor.Width = 72;
        _historyCountEditor.TextBox.TextAlign = HorizontalAlignment.Right;
        _historyCountEditor.ToolTipText =
            $"실제 다운로드할 총 봉수 ({MinimumHistoryCount:N0}~{MaximumHistoryCount:N0})";
        _historyCountEditor.KeyDown += async (_, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            e.SuppressKeyPress = true;
            await ExecuteDataCommandAsync(ApplyHistoryCountAndReloadAsync);
        };
    }

    private void ConfigureDisplayCountEditor()
    {
        _displayCountEditor.DropDownStyle = ComboBoxStyle.DropDown;
        _displayCountEditor.AutoSize = false;
        _displayCountEditor.Width = 72;
        _displayCountEditor.Items.AddRange(
            VisibleBarChoices.Select(value => (object)value).ToArray());
        _displayCountEditor.ToolTipText =
            "화면 표시 봉수. 마우스 휠 줌과 자동 동기화됩니다.";
        _displayCountEditor.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            e.SuppressKeyPress = true;
            ApplyDisplayCountFromEditor();
        };
        _displayCountEditor.SelectedIndexChanged += (_, _) =>
        {
            if (_updatingDataControls ||
                _displayCountEditor.SelectedItem is not int bars)
                return;
            ApplyVisibleBars(bars);
            SynchronizeViewportDisplayCount(force: true);
        };
    }

    private void RebuildDataToolbar()
    {
        _toolbar.Items.Clear();
        _toolbar.Items.AddRange(
        [
            new ToolStripLabel("종목"),
            _dataSymbolEditor,
            _symbolNameLabel,
            new ToolStripSeparator(),
            new ToolStripLabel("주기"),
            _dataTimeframeEditor,
            new ToolStripLabel("총"),
            _historyCountEditor,
            _reloadDataButton,
            new ToolStripLabel("표시"),
            _displayCountEditor,
            _countLabel,
            new ToolStripSeparator(),
            _dateButton,
            _infoButton,
            _toolsButton
        ]);
        _reloadDataButton.ToolTipText = "종목·주기·총 봉수를 적용해 다시 조회";
        _reloadDataButton.Click += async (_, _) =>
            await ExecuteDataCommandAsync(ApplyEditorsAndReloadAsync);
    }

    private void RebuildDataContextMenu()
    {
        _chartContextMenu.Items.Clear();
        _chartContextMenu.Items.Add("최신봉으로", null, (_, _) => FollowLatest());
        _chartContextMenu.Items.Add("화면 초기화", null, (_, _) => ResetView());
        _chartContextMenu.Items.Add(new ToolStripSeparator());

        _contextCrosshair = CreateCheckedMenu(
            "십자선", _workspace.ShowCrosshair, SetCrosshairVisible);
        _contextLegend = CreateCheckedMenu(
            "레전드", _workspace.ShowLegend, SetLegendVisible);
        _contextAxes = CreateCheckedMenu(
            "축", _workspace.ShowAxes, SetAxesVisible);
        _contextDates = CreateCheckedMenu(
            "일자 경계", _workspace.ShowDates, SetDatesVisible);
        _contextInfo = CreateCheckedMenu(
            "종목정보 패널", _workspace.ShowInfoPanel, SetInfoPanelVisible);
        _chartContextMenu.Items.AddRange(
            [_contextCrosshair, _contextLegend, _contextAxes, _contextDates, _contextInfo]);

        var timeframeMenu = new ToolStripMenuItem("주기");
        foreach (TimeframeChoice choice in TimeframeChoices)
        {
            TimeframeChoice captured = choice;
            timeframeMenu.DropDownItems.Add(
                choice.Text,
                null,
                async (_, _) => await ExecuteDataCommandAsync(
                    () => ReloadActiveSymbolsAsync(captured.Value, reloadAll: true)));
        }
        _chartContextMenu.Items.Add(timeframeMenu);

        var historyMenu = new ToolStripMenuItem("총 다운로드 봉수");
        foreach (int count in new[] { 120, 240, 500, 1_000, 2_000, 5_000 })
        {
            int captured = count;
            historyMenu.DropDownItems.Add(
                count.ToString("N0", CultureInfo.InvariantCulture),
                null,
                async (_, _) =>
                {
                    _requestedHistoryCount = captured;
                    SynchronizeDataControls();
                    await ExecuteDataCommandAsync(
                        () => ReloadActiveSymbolsAsync(
                            _workspace.Timeframe,
                            reloadAll: true));
                });
        }
        _chartContextMenu.Items.Add(historyMenu);

        var barsMenu = new ToolStripMenuItem("화면 표시 봉수");
        foreach (int bars in VisibleBarChoices)
        {
            int captured = bars;
            barsMenu.DropDownItems.Add(
                bars.ToString("N0", CultureInfo.InvariantCulture),
                null,
                (_, _) =>
                {
                    ApplyVisibleBars(captured);
                    SynchronizeViewportDisplayCount(force: true);
                });
        }
        _chartContextMenu.Items.Add(barsMenu);
    }

    private async Task ApplyEditorsAndReloadAsync()
    {
        string symbol = NormalizeSymbol(_dataSymbolEditor.Text);
        int count = ParseBoundedCount(
            _historyCountEditor.Text,
            "총 다운로드 봉수",
            MinimumHistoryCount,
            MaximumHistoryCount);
        _requestedHistoryCount = count;
        bool added = AddActiveSymbol(symbol);
        _selectedSymbol = symbol;
        _selectedMetadata = null;
        SynchronizeDataControls();
        await ReloadActiveSymbolsAsync(
            _workspace.Timeframe,
            reloadAll: !added || _activeSymbols.Count > 1);
    }

    private async Task ApplyHistoryCountAndReloadAsync()
    {
        _requestedHistoryCount = ParseBoundedCount(
            _historyCountEditor.Text,
            "총 다운로드 봉수",
            MinimumHistoryCount,
            MaximumHistoryCount);
        SynchronizeDataControls();
        await ReloadActiveSymbolsAsync(_workspace.Timeframe, reloadAll: true);
    }

    private void ApplyDisplayCountFromEditor()
    {
        int bars = ParseBoundedCount(
            _displayCountEditor.Text,
            "화면 표시 봉수",
            20,
            5_000);
        ApplyVisibleBars(bars);
        SynchronizeViewportDisplayCount(force: true);
    }

    private async Task CommitSymbolAsync(string text)
    {
        string symbol = NormalizeSymbol(text);
        bool added = AddActiveSymbol(symbol);
        _selectedSymbol = symbol;
        _selectedMetadata = null;
        _lastVersion = -1;
        _cursor.Clear();
        SynchronizeDataControls();

        if (!added && TryGetSelectedSnapshot(out SymbolSnapshot? snapshot))
        {
            _viewport.Reset(snapshot.Candles.Length);
            _viewport.SetVisibleBars(
                _workspace.RequestedVisibleBars,
                snapshot.Candles.Length,
                followLatest: true);
            await RefreshSelectedMetadataAsync();
            _chart.Focus();
            _chart.Invalidate();
            return;
        }

        await ReloadActiveSymbolsAsync(
            _workspace.Timeframe,
            reloadAll: false,
            onlySymbol: symbol);
    }

    private async Task ReloadActiveSymbolsAsync(
        CandleTimeframe timeframe,
        bool reloadAll,
        string? onlySymbol = null)
    {
        await _reloadGate.WaitAsync(_stop.Token);
        try
        {
            _frameTimer.Stop();
            await StopStreamAsync();
            timeframe.Validate();
            _workspace.SetTimeframe(timeframe);
            SynchronizeDataControls();

            string[] symbols = reloadAll
                ? _activeSymbols.ToArray()
                : [NormalizeSymbol(onlySymbol ?? _selectedSymbol)];
            _statusLabel.Text =
                $"{timeframe} 과거봉 {_requestedHistoryCount:N0}개 조회 중...";

            foreach (string symbol in symbols)
            {
                IReadOnlyList<Candle> history = await _source.GetHistoryAsync(
                    new HistoryRequest(
                        symbol,
                        timeframe,
                        _requestedHistoryCount),
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
                string[] streamSymbols = _activeSymbols.ToArray();
                _streamStop = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
                CancellationToken streamToken = _streamStop.Token;
                _streamTask = PumpRealtimeSymbolsAsync(
                    streamSymbols,
                    timeframe,
                    streamToken);
                _statusLabel.Text =
                    $"{timeframe} 실시간 연결 | 총 {_requestedHistoryCount:N0}봉";
            }
            else
            {
                _statusLabel.Text =
                    $"{timeframe} 과거 차트 | 총 {_requestedHistoryCount:N0}봉";
            }

            _frameTimer.Start();
            SynchronizeViewportDisplayCount(force: true);
            _chart.Focus();
            _chart.Invalidate();
        }
        finally
        {
            _reloadGate.Release();
        }
    }

    private async Task PumpRealtimeSymbolsAsync(
        IReadOnlyList<string> symbols,
        CandleTimeframe timeframe,
        CancellationToken cancellationToken)
    {
        try
        {
            await foreach (CandleEvent value in _source.StreamAsync(
                               symbols,
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

    private async Task ExecuteDataCommandAsync(Func<Task> command)
    {
        try
        {
            _reloadDataButton.Enabled = false;
            await command();
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            SynchronizeDataControls();
            ShowFailure("차트 데이터 명령 실패", exception);
        }
        finally
        {
            if (!IsDisposed) _reloadDataButton.Enabled = true;
        }
    }

    private void SynchronizeDataControls()
    {
        if (!_dataControlsInitialized) return;
        _updatingDataControls = true;
        try
        {
            _dataSymbolEditor.Text = _selectedSymbol;
            SelectDataTimeframe(_workspace.Timeframe);
            _historyCountEditor.Text =
                _requestedHistoryCount.ToString(CultureInfo.InvariantCulture);
            SynchronizeViewportDisplayCount(force: true);
        }
        finally
        {
            _updatingDataControls = false;
        }
    }

    private void SynchronizeViewportDisplayCount(bool force = false)
    {
        if (!_dataControlsInitialized) return;
        int visibleBars = _viewport.VisibleBars;
        if (!force && visibleBars == _lastSynchronizedVisibleBars) return;
        _lastSynchronizedVisibleBars = visibleBars;
        _workspace.SetVisibleBars(visibleBars);

        bool previous = _updatingDataControls;
        _updatingDataControls = true;
        try
        {
            _displayCountEditor.SelectedIndex = -1;
            _displayCountEditor.Text =
                visibleBars.ToString(CultureInfo.InvariantCulture);
        }
        finally
        {
            _updatingDataControls = previous;
        }
    }

    private void SelectDataTimeframe(CandleTimeframe timeframe)
    {
        for (int index = 0; index < _dataTimeframeEditor.Items.Count; index++)
        {
            if (_dataTimeframeEditor.Items[index] is TimeframeChoice choice &&
                choice.Value == timeframe)
            {
                _dataTimeframeEditor.SelectedIndex = index;
                return;
            }
        }
    }

    private bool AddActiveSymbol(string symbol)
    {
        if (_activeSymbols.Contains(symbol, StringComparer.Ordinal)) return false;
        _activeSymbols.Add(symbol);
        if (_dataControlsInitialized)
            _dataSymbolEditor.Items.Add(symbol);
        return true;
    }

    private static string NormalizeSymbol(string? value)
    {
        string symbol = (value ?? string.Empty).Trim().ToUpperInvariant();
        if (symbol.Length == 0)
            throw new ArgumentException("종목코드를 입력하십시오.");
        if (symbol.Length > 24 ||
            symbol.Any(character =>
                !char.IsLetterOrDigit(character) && character != '_'))
            throw new ArgumentException("종목코드 형식이 올바르지 않습니다.");
        return symbol;
    }

    private static int ParseBoundedCount(
        string? text,
        string name,
        int minimum,
        int maximum)
    {
        string normalized = (text ?? string.Empty).Replace(",", string.Empty).Trim();
        if (!int.TryParse(
                normalized,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out int value) ||
            value < minimum ||
            value > maximum)
            throw new ArgumentException(
                $"{name}는 {minimum:N0}~{maximum:N0} 범위의 정수여야 합니다.");
        return value;
    }
}
