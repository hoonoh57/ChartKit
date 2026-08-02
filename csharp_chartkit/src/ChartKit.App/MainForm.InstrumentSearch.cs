using ChartKit.CSharp.Contracts;

namespace ChartKit.CSharp.App;

internal sealed partial class MainForm
{
    private const int InstrumentSearchLimit = 20;
    private const int InstrumentSearchDebounceMilliseconds = 120;
    private const int WmKeyDown = 0x0100;

    private readonly ToolStripDropDown _instrumentSearchDropDown = new()
    {
        AutoClose = false,
        Padding = Padding.Empty
    };
    private readonly ListBox _instrumentSearchList = new()
    {
        BorderStyle = BorderStyle.FixedSingle,
        IntegralHeight = false,
        ItemHeight = 24
    };
    private ToolStripControlHost? _instrumentSearchHost;
    private SymbolEditorMessageFilter? _instrumentSearchMessageFilter;
    private CancellationTokenSource? _instrumentSearchStop;
    private long _instrumentSearchGeneration;
    private bool _instrumentSearchConfigured;
    private bool _applyingInstrumentChoice;
    private bool _instrumentSearchDisposed;

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        if (_instrumentSearchConfigured) return;

        _instrumentSearchConfigured = true;
        ConfigureInstrumentSearch();
        _instrumentSearchMessageFilter = new SymbolEditorMessageFilter(this);
        Application.AddMessageFilter(_instrumentSearchMessageFilter);
        FormClosing += OnInstrumentSearchFormClosing;
        Shown += (_, _) => StartInstrumentSearchWarmup();
    }

    private void ConfigureInstrumentSearch()
    {
        _dataSymbolEditor.ToolTipText =
            "종목명 또는 6자리 코드를 입력하고 검색 목록에서 선택하십시오.";
        _instrumentSearchList.Font = new Font(
            Font.FontFamily,
            Math.Max(9f, Font.Size),
            FontStyle.Regular);
        _instrumentSearchList.MouseClick += async (_, e) =>
        {
            if (e.Button != MouseButtons.Left ||
                _instrumentSearchList.SelectedItem is not InstrumentChoice choice)
                return;
            await ExecuteDataCommandAsync(
                () => CommitInstrumentChoiceAsync(choice.Value));
        };

        _instrumentSearchHost = new ToolStripControlHost(_instrumentSearchList)
        {
            AutoSize = false,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            Size = new Size(420, 288)
        };
        _instrumentSearchDropDown.Items.Add(_instrumentSearchHost);
        _dataSymbolEditor.TextChanged += OnInstrumentSearchTextChanged;
    }

    private void OnInstrumentSearchTextChanged(object? sender, EventArgs e)
    {
        if (_updatingDataControls ||
            _applyingInstrumentChoice ||
            _instrumentSearchDisposed)
            return;

        string query = _dataSymbolEditor.Text.Trim();
        if (query.Length == 0)
        {
            CloseInstrumentSearchDropDown();
            CancelInstrumentSearch();
            return;
        }

        QueueInstrumentSearch(query);
    }

    private void QueueInstrumentSearch(string query)
    {
        CancellationTokenSource next =
            CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
        CancellationTokenSource? previous = _instrumentSearchStop;
        _instrumentSearchStop = next;
        previous?.Cancel();
        long generation = ++_instrumentSearchGeneration;
        _ = RunInstrumentSearchAsync(query, generation, next);
    }

    private async Task RunInstrumentSearchAsync(
        string query,
        long generation,
        CancellationTokenSource request)
    {
        try
        {
            await Task.Delay(
                InstrumentSearchDebounceMilliseconds,
                request.Token);
            if (_source is not IInstrumentSearchSource searchSource)
            {
                CloseInstrumentSearchDropDown();
                return;
            }

            IReadOnlyList<InstrumentSearchResult> results =
                await searchSource.SearchInstrumentsAsync(
                    query,
                    InstrumentSearchLimit,
                    request.Token);
            if (request.IsCancellationRequested ||
                generation != _instrumentSearchGeneration ||
                IsDisposed)
                return;

            ApplyInstrumentSuggestions(query, results);
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            if (!IsDisposed && generation == _instrumentSearchGeneration)
            {
                CloseInstrumentSearchDropDown();
                _statusLabel.Text = "종목검색 오류: " + exception.Message;
            }
        }
        finally
        {
            if (ReferenceEquals(_instrumentSearchStop, request))
                _instrumentSearchStop = null;
            request.Dispose();
        }
    }

    private void ApplyInstrumentSuggestions(
        string query,
        IReadOnlyList<InstrumentSearchResult> results)
    {
        if (!_dataSymbolEditor.TextBox.Focused ||
            !string.Equals(
                NormalizeSearchQuery(_dataSymbolEditor.Text),
                NormalizeSearchQuery(query),
                StringComparison.Ordinal))
            return;

        _instrumentSearchList.BeginUpdate();
        try
        {
            _instrumentSearchList.Items.Clear();
            foreach (InstrumentSearchResult result in results)
                _instrumentSearchList.Items.Add(new InstrumentChoice(result));
            if (_instrumentSearchList.Items.Count > 0)
                _instrumentSearchList.SelectedIndex = 0;
        }
        finally
        {
            _instrumentSearchList.EndUpdate();
        }

        if (_instrumentSearchList.Items.Count == 0)
        {
            CloseInstrumentSearchDropDown();
            _statusLabel.Text = $"종목검색 결과 없음: {query}";
            return;
        }

        ShowInstrumentSearchDropDown();
        _statusLabel.Text =
            $"종목검색 {results.Count:N0}건 | 방향키로 이동 후 Enter";
    }

    private void ShowInstrumentSearchDropDown()
    {
        if (_instrumentSearchDisposed ||
            !_dataSymbolEditor.TextBox.IsHandleCreated)
            return;

        if (!_instrumentSearchDropDown.Visible)
        {
            Point location = _dataSymbolEditor.TextBox.PointToScreen(
                new Point(0, _dataSymbolEditor.TextBox.Height));
            _instrumentSearchDropDown.Show(location);
        }
        _dataSymbolEditor.TextBox.Focus();
    }

    private void HandleInstrumentSearchKey(Keys keyCode)
    {
        _ = HandleInstrumentSearchKeyAsync(keyCode);
    }

    private async Task HandleInstrumentSearchKeyAsync(Keys keyCode)
    {
        switch (keyCode)
        {
            case Keys.Down when _instrumentSearchDropDown.Visible:
                MoveInstrumentSelection(1);
                return;

            case Keys.Up when _instrumentSearchDropDown.Visible:
                MoveInstrumentSelection(-1);
                return;

            case Keys.Escape when _instrumentSearchDropDown.Visible:
                CloseInstrumentSearchDropDown();
                return;

            case Keys.Enter:
                InstrumentSearchResult? selected = ResolveSelectedInstrument();
                if (selected is not null)
                {
                    await ExecuteDataCommandAsync(
                        () => CommitInstrumentChoiceAsync(selected));
                    return;
                }

                string text = _dataSymbolEditor.Text.Trim();
                if (_source is not IInstrumentSearchSource ||
                    IsDirectSymbolText(text))
                {
                    await ExecuteDataCommandAsync(
                        () => CommitSymbolAsync(text));
                    return;
                }

                _statusLabel.Text = "검색 목록에서 종목을 선택하십시오.";
                QueueInstrumentSearch(text);
                return;
        }
    }

    private InstrumentSearchResult? ResolveSelectedInstrument()
    {
        string query = NormalizeSearchQuery(_dataSymbolEditor.Text);
        InstrumentSearchResult? only = null;
        foreach (object item in _instrumentSearchList.Items)
        {
            if (item is not InstrumentChoice choice) continue;
            only ??= choice.Value;
            string symbol = NormalizeSearchQuery(choice.Value.Symbol);
            string name = NormalizeSearchQuery(choice.Value.DisplayName);
            if (string.Equals(query, symbol, StringComparison.Ordinal) ||
                string.Equals(query, name, StringComparison.Ordinal))
                return choice.Value;
        }

        if (_instrumentSearchDropDown.Visible &&
            _instrumentSearchList.SelectedItem is InstrumentChoice selected)
            return selected.Value;
        return _instrumentSearchList.Items.Count == 1 ? only : null;
    }

    private void MoveInstrumentSelection(int delta)
    {
        int count = _instrumentSearchList.Items.Count;
        if (count == 0) return;
        int current = Math.Max(0, _instrumentSearchList.SelectedIndex);
        _instrumentSearchList.SelectedIndex =
            Math.Clamp(current + delta, 0, count - 1);
        _instrumentSearchList.TopIndex =
            Math.Max(0, _instrumentSearchList.SelectedIndex - 4);
    }

    private async Task CommitInstrumentChoiceAsync(
        InstrumentSearchResult choice)
    {
        CloseInstrumentSearchDropDown();
        CancelInstrumentSearch();
        _applyingInstrumentChoice = true;
        try
        {
            _dataSymbolEditor.Text = choice.Symbol;
        }
        finally
        {
            _applyingInstrumentChoice = false;
        }

        _metadataCache[choice.Symbol] = new InstrumentMetadata(
            choice.Symbol,
            choice.DisplayName,
            choice.Market,
            _source.Name,
            DateTimeOffset.UtcNow);
        await CommitSymbolAsync(choice.Symbol);
    }

    private void StartInstrumentSearchWarmup()
    {
        if (_source is not IInstrumentSearchSource searchSource) return;
        _ = WarmInstrumentSearchAsync(searchSource);
    }

    private async Task WarmInstrumentSearchAsync(
        IInstrumentSearchSource searchSource)
    {
        try
        {
            for (int attempt = 0;
                 attempt < 100 && !_frameTimer.Enabled;
                 attempt++)
                await Task.Delay(100, _stop.Token);

            await searchSource.SearchInstrumentsAsync(
                string.Empty,
                1,
                _stop.Token);
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            if (!IsDisposed)
                _statusLabel.Text = "종목검색 준비 실패: " + exception.Message;
        }
    }

    private void CancelInstrumentSearch()
    {
        CancellationTokenSource? request = _instrumentSearchStop;
        _instrumentSearchStop = null;
        request?.Cancel();
    }

    private void CloseInstrumentSearchDropDown()
    {
        if (!_instrumentSearchDisposed && _instrumentSearchDropDown.Visible)
            _instrumentSearchDropDown.Close();
    }

    private void OnInstrumentSearchFormClosing(
        object? sender,
        FormClosingEventArgs e)
    {
        DisposeInstrumentSearch();
    }

    private void DisposeInstrumentSearch()
    {
        if (_instrumentSearchDisposed) return;
        _instrumentSearchDisposed = true;
        CancelInstrumentSearch();
        if (_instrumentSearchMessageFilter is not null)
        {
            Application.RemoveMessageFilter(_instrumentSearchMessageFilter);
            _instrumentSearchMessageFilter = null;
        }
        if (_instrumentSearchDropDown.Visible)
            _instrumentSearchDropDown.Close();
        _instrumentSearchDropDown.Dispose();
    }

    private static bool IsDirectSymbolText(string value) =>
        value.Length is > 0 and <= 24 &&
        value.All(character =>
            char.IsAsciiLetterOrDigit(character) || character == '_');

    private static string NormalizeSearchQuery(string value) =>
        new string(value
            .Where(char.IsLetterOrDigit)
            .Select(char.ToUpperInvariant)
            .ToArray());

    private sealed record InstrumentChoice(InstrumentSearchResult Value)
    {
        public override string ToString() =>
            $"{Value.DisplayName,-20} [{Value.Symbol}]  {Value.Market}" +
            (Value.NxtEnabled ? "  NXT" : string.Empty);
    }

    private sealed class SymbolEditorMessageFilter(MainForm owner) : IMessageFilter
    {
        public bool PreFilterMessage(ref Message message)
        {
            if (message.Msg != WmKeyDown ||
                !owner._dataSymbolEditor.TextBox.IsHandleCreated ||
                message.HWnd != owner._dataSymbolEditor.TextBox.Handle)
                return false;

            Keys keyCode = (Keys)(message.WParam.ToInt64() & 0xffff);
            if (keyCode is not (Keys.Enter or Keys.Up or Keys.Down or Keys.Escape))
                return false;

            owner.HandleInstrumentSearchKey(keyCode);
            return true;
        }
    }
}
