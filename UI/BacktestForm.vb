Imports System.Drawing
Imports System.IO
Imports System.Text
Imports System.Threading.Tasks
Imports System.Windows.Forms
Imports ChartKit.Abstractions
Imports ChartKit.Core.Backtesting
Imports ChartKit.Core.Strategies
Imports ChartKit.Models

Namespace UI
    Public NotInheritable Class BacktestForm
        Inherits Form

        Private ReadOnly _factory As Func(Of ICandleDataSource)
        Private ReadOnly _source As ICandleDataSource
        Private ReadOnly _symbols As New TextBox With {.Multiline = True, .Text = "000660"}
        Private ReadOnly _capturedAt As New DateTimePicker()
        Private ReadOnly _interval As New ComboBox()
        Private ReadOnly _count As New NumericUpDown()
        Private ReadOnly _shortJma As New TextBox With {.Text = "10,50,2"}
        Private ReadOnly _longJma As New TextBox With {.Text = "50,50,2"}
        Private ReadOnly _macd As New TextBox With {.Text = "10,20,5"}
        Private ReadOnly _score As New NumericUpDown()
        Private ReadOnly _confirm As New NumericUpDown()
        Private ReadOnly _entryCap As New NumericUpDown()
        Private ReadOnly _profit As New NumericUpDown()
        Private ReadOnly _loss As New NumericUpDown()
        Private ReadOnly _maxTrades As New NumericUpDown()
        Private ReadOnly _trainingDates As New NumericUpDown()
        Private ReadOnly _testDates As New NumericUpDown()
        Private ReadOnly _stepDates As New NumericUpDown()
        Private ReadOnly _minimumTrades As New NumericUpDown()
        Private ReadOnly _mddPenalty As New NumericUpDown()
        Private ReadOnly _neighborWeight As New NumericUpDown()
        Private ReadOnly _neighborCount As New NumericUpDown()
        Private ReadOnly _initialCapital As New NumericUpDown()
        Private ReadOnly _maxConcurrent As New NumericUpDown()
        Private ReadOnly _stabilityEnabled As New CheckBox With {
            .Text = "안정성 선택", .Checked = True, .AutoSize = True}
        Private ReadOnly _commission As New NumericUpDown()
        Private ReadOnly _buySlippage As New NumericUpDown()
        Private ReadOnly _sellSlippage As New NumericUpDown()
        Private ReadOnly _sellTax As New NumericUpDown()
        Private ReadOnly _profitMode As New ComboBox()
        Private ReadOnly _sameDay As New CheckBox With {.Text = "당일만", .Checked = True}
        Private ReadOnly _lossEnabled As New CheckBox With {.Text = "누적손실 차단", .Checked = True}
        Private ReadOnly _run As New Button With {.Text = "실행"}
        Private ReadOnly _sweep As New Button With {.Text = "조합 탐색"}
        Private ReadOnly _walkForward As New Button With {.Text = "워크포워드"}
        Private ReadOnly _import As New Button With {.Text = "포착목록 가져오기"}
        Private ReadOnly _export As New Button With {.Text = "거래 CSV"}
        Private ReadOnly _saveBaseline As New Button With {.Text = "현재 기준선 박제"}
        Private ReadOnly _loadBaseline As New Button With {.Text = "기준선 불러오기"}
        Private ReadOnly _clipboardCaptureButton As New Button With {.Text = "클립보드 포착"}
        Private ReadOnly _clipboardCapture As New ToolStripMenuItem("클립보드 포착")
        Private ReadOnly _chartVerify As New Button With {.Text = "차트검증", .Enabled = False}
        Private ReadOnly _status As New Label With {.AutoSize = True}
        Private ReadOnly _grid As New DataGridView()
        Private ReadOnly _cache As New Dictionary(Of String, List(Of CandleItem))(StringComparer.Ordinal)
        Private _captureRecords As New List(Of CaptureRecord)()
        Private _lastSummary As BacktestSummary
        Private _baseline As BacktestBaselineSnapshot
        Private _cacheInterval As CandleInterval?
        Private _cacheCount As Integer
        Private _displayedSymbolResults As New List(Of SymbolBacktestResult)()
        Private _lastRunInterval As CandleInterval?

        Public Sub New(factory As Func(Of ICandleDataSource))
            _factory = factory
            _source = _factory()
            If _source Is Nothing Then
                Throw New InvalidOperationException("캔들 데이터 소스를 생성하지 못했습니다.")
            End If
            Text = "ChartKit 전략 백테스트 / 파라미터 탐색"
            Text &= $" [{_source.Name}]"
            Width = 1280 : Height = 760
            BackColor = Color.FromArgb(20, 23, 29) : ForeColor = Color.White
            BuildUi()
            _status.Text = $"Data source: {_source.Name}"
        End Sub

        Private Sub BuildUi()
            _capturedAt.Format = DateTimePickerFormat.Custom
            _capturedAt.CustomFormat = "yyyy-MM-dd HH:mm"
            _capturedAt.Value = Date.Today.AddHours(9).AddMinutes(5)
            _interval.DropDownStyle = ComboBoxStyle.DropDownList
            _interval.Items.AddRange(New Object() {"1분", "3분", "5분", "15분", "30분", "60분",
                                                   "120틱", "240틱", "360틱", "720틱", "일봉", "주봉"})
            _interval.SelectedIndex = 7
            NumberBox(_count, 100, 100000, 2000)
            NumberBox(_score, 0, 100, 60)
            NumberBox(_confirm, 1, 100, 2)
            NumberBox(_entryCap, 0, 1000, 20)
            NumberBox(_profit, 0, 1000, 10)
            NumberBox(_loss, 0, 1000, 5)
            NumberBox(_maxTrades, 0, 10000, 0)
            NumberBox(_trainingDates, 1, 1000, 3, 0)
            NumberBox(_testDates, 1, 1000, 1, 0)
            NumberBox(_stepDates, 1, 1000, 1, 0)
            NumberBox(_minimumTrades, 0, 100000, 3, 0)
            NumberBox(_mddPenalty, 0, 100, 0.5D, 2)
            NumberBox(_neighborWeight, 0, 100, 0.5D, 2)
            NumberBox(_neighborCount, 1, 1000, 4, 0)
            NumberBox(_initialCapital, 1000000D, 1000000000000D, 100000000D, 0)
            _initialCapital.Width = 110
            NumberBox(_maxConcurrent, 1, 1000, 5, 0)
            NumberBox(_commission, 0, 10,
                      CDec(BacktestCostDefaults.KiwoomKrxCommissionPctPerSide), 3)
            NumberBox(_buySlippage, 0, 10000,
                      CDec(BacktestCostDefaults.DefaultSlippageBpsPerSide), 1)
            NumberBox(_sellSlippage, 0, 10000,
                      CDec(BacktestCostDefaults.DefaultSlippageBpsPerSide), 1)
            NumberBox(_sellTax, 0, 10,
                      CDec(BacktestCostDefaults.KrxStockSellTaxPct2026), 3)
            _profitMode.DropDownStyle = ComboBoxStyle.DropDownList
            _profitMode.Items.AddRange(New Object() {"누적 실현수익", "단일 거래수익"})
            _profitMode.SelectedIndex = 0
            AddHandler _run.Click, Async Sub(s, e) Await ExecuteAsync(False)
            AddHandler _sweep.Click, Async Sub(s, e) Await ExecuteAsync(True)
            AddHandler _walkForward.Click, Async Sub(s, e) Await ExecuteWalkForwardAsync()
            AddHandler _import.Click, AddressOf ImportClicked
            AddHandler _export.Click, AddressOf ExportClicked
            AddHandler _saveBaseline.Click, AddressOf SaveBaselineClicked
            AddHandler _loadBaseline.Click, AddressOf LoadBaselineClicked
            AddHandler _clipboardCaptureButton.Click, AddressOf ClipboardCaptureClicked
            AddHandler _chartVerify.Click, AddressOf ChartVerifyClicked
            AddHandler _grid.CellDoubleClick, AddressOf GridCellDoubleClick

            Dim inputs As New FlowLayoutPanel With {
                .Dock = DockStyle.Top, .Height = 168, .AutoScroll = True,
                .WrapContents = True, .BackColor = Color.Black, .Padding = New Padding(6)}
            AddInput(inputs, "포착시각", _capturedAt)
            AddInput(inputs, "주기", _interval)
            AddInput(inputs, "캔들수", _count)
            AddInput(inputs, "단기JMA P,Phase,Power", _shortJma)
            AddInput(inputs, "장기JMA P,Phase,Power", _longJma)
            AddInput(inputs, "MACD F,S,Signal", _macd)
            AddInput(inputs, "진입점수", _score)
            AddInput(inputs, "확정봉", _confirm)
            AddInput(inputs, "진입상한%(0=끔)", _entryCap)
            AddInput(inputs, "수익차단%", _profit)
            AddInput(inputs, "수익기준", _profitMode)
            AddInput(inputs, "손실차단%", _loss)
            AddInput(inputs, "최대거래(0=끔)", _maxTrades)
            AddInput(inputs, "학습일수", _trainingDates)
            AddInput(inputs, "검증일수", _testDates)
            AddInput(inputs, "이동일수", _stepDates)
            AddInput(inputs, "최소거래", _minimumTrades)
            AddInput(inputs, "MDD가중", _mddPenalty)
            AddInput(inputs, "인접가중", _neighborWeight)
            AddInput(inputs, "인접후보", _neighborCount)
            AddInput(inputs, "초기자본", _initialCapital)
            AddInput(inputs, "동시보유", _maxConcurrent)
            AddInput(inputs, "키움 KRX 편도수수료%", _commission)
            AddInput(inputs, "매수슬리피지bp(가정)", _buySlippage)
            AddInput(inputs, "매도슬리피지bp(가정)", _sellSlippage)
            AddInput(inputs, "2026 KRX 매도세%", _sellTax)
            inputs.Controls.Add(_lossEnabled) : inputs.Controls.Add(_sameDay)
            inputs.Controls.Add(_stabilityEnabled)
            inputs.Controls.Add(_clipboardCaptureButton)
            inputs.Controls.Add(_import)
            inputs.Controls.Add(_run) : inputs.Controls.Add(_sweep)
            inputs.Controls.Add(_walkForward)
            inputs.Controls.Add(_export)
            inputs.Controls.Add(_saveBaseline) : inputs.Controls.Add(_loadBaseline)
            inputs.Controls.Add(_chartVerify)
            inputs.Controls.Add(_status)

            _symbols.Dock = DockStyle.Top : _symbols.Height = 62
            _symbols.BackColor = Color.Black : _symbols.ForeColor = Color.White
            _symbols.ScrollBars = ScrollBars.Vertical
            _grid.Dock = DockStyle.Fill : _grid.ReadOnly = True
            _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
            _grid.BackgroundColor = BackColor : _grid.EnableHeadersVisualStyles = False
            _grid.DefaultCellStyle.BackColor = BackColor : _grid.DefaultCellStyle.ForeColor = Color.White
            _grid.ColumnHeadersDefaultCellStyle.BackColor = Color.Black
            _grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.White
            Dim captureMenu As New ContextMenuStrip()
            captureMenu.Items.Add(_clipboardCapture)
            AddHandler _clipboardCapture.Click, AddressOf ClipboardCaptureClicked
            ContextMenuStrip = captureMenu
            _symbols.ContextMenuStrip = captureMenu
            _grid.ContextMenuStrip = captureMenu
            Controls.Add(_grid) : Controls.Add(_symbols) : Controls.Add(inputs)
        End Sub

        Private Sub ClipboardCaptureClicked(sender As Object, e As EventArgs)
            Using dialog As New ClipboardCaptureForm()
                If dialog.ShowDialog(Me) <> DialogResult.OK Then Return
                Dim records = dialog.AcceptedRecords.
                    Select(Function(item) New CaptureRecord With {
                        .Symbol = item.Symbol,
                        .CapturedAt = _capturedAt.Value}).
                    ToList()
                If records.Count = 0 Then Return
                _captureRecords = records
                _symbols.Text = String.Join(
                    Environment.NewLine,
                    records.Select(Function(item) item.Symbol).Distinct())
                _status.Text = $"클립보드 포착 {records.Count}종목 적용 / {_capturedAt.Value:yyyy-MM-dd HH:mm}"
            End Using
        End Sub

        Private Async Function ExecuteAsync(sweep As Boolean) As Task
            Try
                Busy(True, "데이터 로드 중...")
                Dim symbols = ParseSymbols()
                Dim cases = CreateCases(symbols)
                Dim parameters = ReadParameters()
                Dim validation = parameters.Validate()
                If validation.Length > 0 Then Throw New InvalidOperationException(validation)
                Await LoadCasesAsync(cases)
                Dim candidates As List(Of StrategyParameterSet)
                If sweep Then
                    candidates = ParameterSweepEngine.Generate(
                        parameters, ListPart(_shortJma.Text, 0), ListPart(_longJma.Text, 0),
                        ListPart(_macd.Text, 0), ListPart(_macd.Text, 1), ListPart(_macd.Text, 2))
                Else
                    candidates = New List(Of StrategyParameterSet) From {parameters}
                End If
                Busy(True, $"{candidates.Count}개 조합 계산 중...")
                Dim ranked = Await Task.Run(
                    Function() New ParameterSweepEngine().EvaluateCases(
                        cases, candidates, If(sweep, ReadStabilityOptions(), Nothing),
                        ReadPortfolioOptions()))
                If sweep Then ShowRanking(ranked) Else ShowSymbols(ranked.First())
            Catch ex As Exception
                _status.Text = $"오류: {ex.Message}"
                MessageBox.Show(ex.Message, "백테스트", MessageBoxButtons.OK, MessageBoxIcon.Error)
            Finally
                Busy(False, Nothing)
            End Try
        End Function

        Private Async Function ExecuteWalkForwardAsync() As Task
            Try
                Busy(True, "워크포워드 데이터 로드 중...")
                Dim symbols = ParseSymbols()
                Dim cases = CreateCases(symbols)
                Dim parameters = ReadParameters()
                Dim validation = parameters.Validate()
                If validation.Length > 0 Then Throw New InvalidOperationException(validation)
                Await LoadCasesAsync(cases)
                Dim candidates = ParameterSweepEngine.Generate(
                    parameters, ListPart(_shortJma.Text, 0), ListPart(_longJma.Text, 0),
                    ListPart(_macd.Text, 0), ListPart(_macd.Text, 1), ListPart(_macd.Text, 2))
                Busy(True, $"{candidates.Count}개 조합 워크포워드 계산 중...")
                Dim options As New WalkForwardOptions With {
                    .TrainingDateCount = CInt(_trainingDates.Value),
                    .TestDateCount = CInt(_testDates.Value),
                    .StepDateCount = CInt(_stepDates.Value),
                    .Stability = ReadStabilityOptions(),
                    .Portfolio = ReadPortfolioOptions()}
                Dim result = Await Task.Run(
                    Function() New WalkForwardEngine().Evaluate(cases, candidates, options))
                ShowWalkForward(result)
            Catch ex As Exception
                _status.Text = $"오류: {ex.Message}"
                MessageBox.Show(ex.Message, "워크포워드", MessageBoxButtons.OK, MessageBoxIcon.Error)
            Finally
                Busy(False, Nothing)
            End Try
        End Function

        Private Async Function LoadCasesAsync(cases As IEnumerable(Of BacktestCase)) As Task
            Dim requestedInterval = SelectedInterval()
            Dim requestedCount = CInt(_count.Value)
            If Not _cacheInterval.HasValue OrElse _cacheInterval.Value <> requestedInterval OrElse
               _cacheCount <> requestedCount Then
                _cache.Clear()
                _cacheInterval = requestedInterval
                _cacheCount = requestedCount
            End If
            For Each item In cases
                Dim cacheKey = CandleCacheKey(item.Symbol, item.CapturedAt.Date)
                If Not _cache.ContainsKey(cacheKey) Then
                    _status.Text = $"{item.Symbol} {item.CapturedAt:yyyy-MM-dd} 로드 중..."
                    Dim request As New CandleRequest With {
                        .Symbol = item.Symbol, .Interval = requestedInterval, .Count = requestedCount,
                        .To = item.CapturedAt.Date}
                    _cache(cacheKey) = Await Task.Run(Function() _source.GetCandles(request))
                End If
                item.Candles = _cache(cacheKey)
            Next
        End Function

        Private Function CreateCases(symbols As IEnumerable(Of String)) As List(Of BacktestCase)
            Dim selected = symbols.ToHashSet(StringComparer.Ordinal)
            Dim records = _captureRecords.Where(Function(x) selected.Contains(x.Symbol)).ToList()
            For Each symbol In selected
                If Not records.Any(Function(x) x.Symbol = symbol) Then
                    records.Add(New CaptureRecord With {
                        .Symbol = symbol, .CapturedAt = _capturedAt.Value})
                End If
            Next
            ' A clipboard capture or a normal one-day import has one common capture
            ' timestamp. In that mode the picker is the execution date/time selector,
            ' so changing it must not keep using the timestamp stored in old records.
            ' Multi-timestamp imports are intentionally preserved for walk-forward tests.
            Dim distinctCaptureTimes = records.
                Where(Function(x) x.CapturedAt.HasValue).
                Select(Function(x) x.CapturedAt.Value).
                Distinct().
                Take(2).
                ToList()
            Dim useSelectedCaptureTime = distinctCaptureTimes.Count <= 1
            Dim output As New List(Of BacktestCase)()
            Dim seen As New HashSet(Of String)(StringComparer.Ordinal)
            For Each record In records
                Dim capturedAt = If(useSelectedCaptureTime OrElse Not record.CapturedAt.HasValue,
                                    _capturedAt.Value, record.CapturedAt.Value)
                Dim caseId = $"{record.Symbol}|{capturedAt:yyyyMMddHHmmss}"
                If seen.Add(caseId) Then
                    output.Add(New BacktestCase With {
                        .CaseId = caseId, .Symbol = record.Symbol, .CapturedAt = capturedAt})
                End If
            Next
            Return output
        End Function

        Private Shared Function CandleCacheKey(symbol As String, captureDate As DateTime) As String
            Return $"{symbol}|{captureDate:yyyyMMdd}"
        End Function

        Private Function ReadParameters() As StrategyParameterSet
            Dim sj = Triple(_shortJma.Text) : Dim lj = Triple(_longJma.Text) : Dim m = Triple(_macd.Text)
            Return New StrategyParameterSet With {
                .ShortJma = New JmaParameters With {.Period = sj(0), .Phase = sj(1), .Power = sj(2)},
                .LongJma = New JmaParameters With {.Period = lj(0), .Phase = lj(1), .Power = lj(2)},
                .Macd = New MacdParameters With {
                    .FastPeriod = m(0), .SlowPeriod = m(1), .SignalPeriod = m(2)},
                .Qualification = New Core.Signals.TrendQualificationOptions With {
                    .MinimumEntryScore = CInt(_score.Value), .ConfirmationBars = CInt(_confirm.Value)},
                .Safety = New StrategyReentryLockOptions With {
                    .Mode = If(_profitMode.SelectedIndex = 0,
                        StrategyReentryLockMode.CumulativeClosedReturn,
                        StrategyReentryLockMode.SingleTradeReturn),
                    .ThresholdPct = CDbl(_profit.Value),
                    .MaximumEntryGainPct = CDbl(_entryCap.Value),
                    .CumulativeLossLockEnabled = _lossEnabled.Checked,
                    .CumulativeLossThresholdPct = CDbl(_loss.Value),
                    .MaximumTradeCount = CInt(_maxTrades.Value),
                    .SameTradingDayOnly = _sameDay.Checked},
                .Costs = New BacktestCostOptions With {
                    .CommissionPctPerSide = CDbl(_commission.Value),
                    .BuySlippageBps = CDbl(_buySlippage.Value),
                    .SellSlippageBps = CDbl(_sellSlippage.Value),
                    .SellTaxPct = CDbl(_sellTax.Value)}}
        End Function

        Private Sub ShowSymbols(summary As BacktestSummary)
            _lastSummary = summary
            _lastRunInterval = SelectedInterval()
            _displayedSymbolResults = summary.Symbols.ToList()
            _grid.DataSource = summary.Symbols.Select(Function(x) New With {
                x.Symbol, .포착시각 = x.CapturedAt, .포착가 = x.CapturePrice,
                .거래수 = If(x.Evaluation Is Nothing, 0, x.Evaluation.ClosedTradeCount),
                .총수익률 = Math.Round(x.ReturnPct, 3),
                .순수익률 = Math.Round(x.NetReturnPct, 3),
                .MDD = Math.Round(x.MaximumDrawdownPct, 3),
                .승률 = Math.Round(x.WinRatePct, 2),
                .오류 = x.ErrorMessage}).ToList()
            _status.Text = $"순수익 {summary.EqualWeightNetReturnPct:F2}% / " &
                           $"포트 {If(summary.Portfolio Is Nothing, 0.0R, summary.Portfolio.NetReturnPct):F2}% / " &
                           $"실현MDD {If(summary.Portfolio Is Nothing, 0.0R, summary.Portfolio.RealizedMaximumDrawdownPct):F2}% / " &
                           $"{summary.TotalTradeCount}거래" & BaselineStatus(summary)
        End Sub

        Private Function BaselineStatus(summary As BacktestSummary) As String
            If _baseline Is Nothing Then Return ""
            Dim comparison = BacktestBaselineStore.Compare(
                summary, _baseline, SelectedInterval(), CInt(_count.Value),
                ReadPortfolioOptions())
            Dim comparable = If(comparison.SameUniverse AndAlso comparison.SameConfiguration,
                                "동일조건", "조건불일치")
            Return $" / 기준선({comparable}) Δ순익 {comparison.EqualWeightNetReturnDeltaPct:+0.00;-0.00;0.00}%p" &
                   $" Δ포트 {comparison.PortfolioNetReturnDeltaPct:+0.00;-0.00;0.00}%p" &
                   $" MDD개선 {comparison.MaximumDrawdownImprovementPct:+0.00;-0.00;0.00}%p"
        End Function

        Private Sub SaveBaselineClicked(sender As Object, e As EventArgs)
            If _lastSummary Is Nothing Then
                MessageBox.Show("먼저 단일 설정 백테스트를 실행하세요.", "기준선")
                Return
            End If
            Using dialog As New SaveFileDialog With {
                .Filter = "ChartKit 기준선 (*.json)|*.json",
                .FileName = $"ChartKit_Baseline_{Date.Now:yyyyMMdd_HHmmss}.json"}
                If dialog.ShowDialog(Me) <> DialogResult.OK Then Return
                _baseline = BacktestBaselineStore.Create(
                    _lastSummary, _source.Name, SelectedInterval(), CInt(_count.Value),
                    ReadPortfolioOptions())
                BacktestBaselineStore.Save(dialog.FileName, _baseline)
                _status.Text = $"기준선 박제 완료: {Path.GetFileName(dialog.FileName)}" &
                               BaselineStatus(_lastSummary)
            End Using
        End Sub

        Private Sub LoadBaselineClicked(sender As Object, e As EventArgs)
            Using dialog As New OpenFileDialog With {
                .Filter = "ChartKit 기준선 (*.json)|*.json"}
                If dialog.ShowDialog(Me) <> DialogResult.OK Then Return
                _baseline = BacktestBaselineStore.Load(dialog.FileName)
                _status.Text = $"기준선 로드: {Path.GetFileName(dialog.FileName)}"
                If _lastSummary IsNot Nothing Then _status.Text &= BaselineStatus(_lastSummary)
            End Using
        End Sub

        Private Sub ShowRanking(ranked As IEnumerable(Of BacktestSummary))
            _lastSummary = Nothing
            _displayedSymbolResults.Clear()
            _grid.DataSource = ranked.Select(Function(x) New With {
                .파라미터 = x.Parameters.ToString(),
                .총수익률 = Math.Round(x.EqualWeightReturnPct, 3),
                .순수익률 = Math.Round(x.EqualWeightNetReturnPct, 3),
                .최악종목MDD = Math.Round(x.WorstSymbolDrawdownPct, 3),
                .안정성점수 = Math.Round(x.StabilityScore, 3),
                .인접평균순수익률 = Math.Round(x.NeighborAverageNetReturnPct, 3),
                .최소거래충족 = x.MeetsMinimumTradeCount,
                .포트순수익률 = Math.Round(If(x.Portfolio Is Nothing, 0.0R, x.Portfolio.NetReturnPct), 3),
                .포트실현MDD = Math.Round(If(x.Portfolio Is Nothing, 0.0R, x.Portfolio.RealizedMaximumDrawdownPct), 3),
                .평균노출률 = Math.Round(If(x.Portfolio Is Nothing, 0.0R, x.Portfolio.AverageExposurePct), 2),
                .진입거절 = If(x.Portfolio Is Nothing, 0, x.Portfolio.RejectedEntryCount),
                .수익종목비율 = Math.Round(x.WinningSymbolRatePct, 2),
                .거래수 = x.TotalTradeCount, .종목수 = x.TestedSymbolCount}).ToList()
        End Sub

        Private Sub ShowWalkForward(result As WalkForwardResult)
            _lastSummary = Nothing
            _displayedSymbolResults.Clear()
            _grid.DataSource = result.Folds.Select(Function(x) New With {
                .Fold = x.FoldNumber,
                .학습구간 = $"{x.TrainingFrom:yyyy-MM-dd} ~ {x.TrainingTo:yyyy-MM-dd}",
                .검증구간 = $"{x.TestFrom:yyyy-MM-dd} ~ {x.TestTo:yyyy-MM-dd}",
                .선택파라미터 = x.SelectedParameters.ToString(),
                .학습순수익률 = Math.Round(x.TrainingSummary.EqualWeightNetReturnPct, 3),
                .학습안정성점수 = Math.Round(x.TrainingSummary.StabilityScore, 3),
                .인접평균순수익률 = Math.Round(x.TrainingSummary.NeighborAverageNetReturnPct, 3),
                .최소거래충족 = x.TrainingSummary.MeetsMinimumTradeCount,
                .검증순수익률 = Math.Round(x.TestSummary.EqualWeightNetReturnPct, 3),
                .검증포트순수익률 = Math.Round(If(x.TestSummary.Portfolio Is Nothing, 0.0R, x.TestSummary.Portfolio.NetReturnPct), 3),
                .검증포트실현MDD = Math.Round(If(x.TestSummary.Portfolio Is Nothing, 0.0R, x.TestSummary.Portfolio.RealizedMaximumDrawdownPct), 3),
                .검증평균노출률 = Math.Round(If(x.TestSummary.Portfolio Is Nothing, 0.0R, x.TestSummary.Portfolio.AverageExposurePct), 2),
                .검증진입거절 = If(x.TestSummary.Portfolio Is Nothing, 0, x.TestSummary.Portfolio.RejectedEntryCount),
                .검증최악종목MDD = Math.Round(x.TestSummary.WorstSymbolDrawdownPct, 3),
                .검증수익종목비율 = Math.Round(x.TestSummary.WinningSymbolRatePct, 2),
                .검증거래수 = x.TestSummary.TotalTradeCount}).ToList()
            _status.Text =
                $"OOS 평균 {result.AverageOutOfSampleNetReturnPct:F2}% / " &
                $"수익 Fold {result.ProfitableFoldRatePct:F1}% / {result.Folds.Count} Fold"
        End Sub

        Private Function ReadStabilityOptions() As StabilitySelectionOptions
            Return New StabilitySelectionOptions With {
                .Enabled = _stabilityEnabled.Checked,
                .MinimumTradeCount = CInt(_minimumTrades.Value),
                .DrawdownPenaltyWeight = CDbl(_mddPenalty.Value),
                .NeighborReturnWeight = CDbl(_neighborWeight.Value),
                .NeighborCount = CInt(_neighborCount.Value)}
        End Function

        Private Function ReadPortfolioOptions() As PortfolioSimulationOptions
            Return New PortfolioSimulationOptions With {
                .InitialCapital = CDbl(_initialCapital.Value),
                .MaximumConcurrentPositions = CInt(_maxConcurrent.Value)}
        End Function

        Private Sub ImportClicked(sender As Object, e As EventArgs)
            Using dialog As New OpenFileDialog With {
                .Filter = "포착 목록 (*.csv;*.txt)|*.csv;*.txt|모든 파일 (*.*)|*.*",
                .Title = "조건검색 포착 종목 목록 가져오기"}
                If dialog.ShowDialog(Me) <> DialogResult.OK Then Return
                Try
                    Dim records = CaptureListParser.ParseEventFile(dialog.FileName, _capturedAt.Value.Date)
                    If records.Count = 0 Then
                        Throw New InvalidOperationException(
                            "6자리 종목코드를 찾지 못했습니다. 종목코드 열의 앞자리 0이 보존되어야 합니다.")
                    End If
                    _captureRecords = records
                    _symbols.Text = String.Join(
                        Environment.NewLine, records.Select(Function(x) x.Symbol).Distinct())
                    Dim distinctTimes = records.Where(Function(x) x.CapturedAt.HasValue).
                        Select(Function(x) x.CapturedAt.Value).Distinct().ToList()
                    If distinctTimes.Count = 1 Then _capturedAt.Value = distinctTimes(0)
                    Dim symbolCount = records.Select(Function(x) x.Symbol).Distinct().Count()
                    Dim timedCount = records.Where(Function(x) x.CapturedAt.HasValue).Count()
                    _status.Text = $"{symbolCount}종목 / {records.Count}포착사례 / 시각 {timedCount}건"
                Catch ex As Exception
                    MessageBox.Show(ex.Message, "포착목록 가져오기",
                                    MessageBoxButtons.OK, MessageBoxIcon.Error)
                End Try
            End Using
        End Sub

        Private Sub ExportClicked(sender As Object, e As EventArgs)
            If _lastSummary Is Nothing Then
                MessageBox.Show("먼저 단일 설정 백테스트를 실행하세요.", "CSV")
                Return
            End If
            Using dialog As New SaveFileDialog With {
                .Filter = "CSV 파일 (*.csv)|*.csv",
                .FileName = $"ChartKit_Backtest_{Date.Now:yyyyMMdd_HHmmss}.csv"}
                If dialog.ShowDialog(Me) <> DialogResult.OK Then Return
                Dim rows As New List(Of String) From {
                    "Symbol,EntryTime,EntryPrice,ExitTime,ExitPrice,GrossReturnPct,NetReturnPct,MFEpct,MAEpct,ExitReason"}
                For Each symbol In _lastSummary.Symbols
                    If symbol.Evaluation Is Nothing Then Continue For
                    For Each trade In symbol.Evaluation.Trades
                        If trade.IsOpen Then Continue For
                        rows.Add(String.Join(",", {
                            Csv(symbol.Symbol),
                            Csv(trade.EntryTime.ToString("yyyy-MM-dd HH:mm:ss")),
                            trade.EntryPrice.ToString(Globalization.CultureInfo.InvariantCulture),
                            Csv(trade.ExitTime.ToString("yyyy-MM-dd HH:mm:ss")),
                            trade.ExitPrice.ToString(Globalization.CultureInfo.InvariantCulture),
                            trade.ReturnPct.ToString("F6", Globalization.CultureInfo.InvariantCulture),
                            SymbolBacktestResult.NetTradeReturnPct(trade, symbol.Costs).
                                ToString("F6", Globalization.CultureInfo.InvariantCulture),
                            trade.MaximumFavorableExcursionPct.ToString("F6", Globalization.CultureInfo.InvariantCulture),
                            trade.MaximumAdverseExcursionPct.ToString("F6", Globalization.CultureInfo.InvariantCulture),
                            Csv(trade.ExitReason.ToString())}))
                    Next
                Next
                File.WriteAllLines(dialog.FileName, rows, New UTF8Encoding(True))
                _status.Text = $"CSV 저장: {Path.GetFileName(dialog.FileName)}"
            End Using
        End Sub

        Private Shared Function Csv(value As String) As String
            If value Is Nothing Then Return ""
            Return """" & value.Replace("""", """""") & """"
        End Function

        Private Function ParseSymbols() As List(Of String)
            Dim result = _symbols.Text.Split({ControlChars.Cr, ControlChars.Lf, ","c, ";"c, " "c},
                StringSplitOptions.RemoveEmptyEntries).Select(Function(x) x.Trim()).Distinct().ToList()
            If result.Count = 0 Then Throw New InvalidOperationException("종목코드를 입력하세요.")
            Return result
        End Function

        Private Shared Function Triple(value As String) As Integer()
            Dim parts = value.Split(","c)
            If parts.Length <> 3 Then Throw New FormatException("JMA/MACD는 쉼표로 구분한 3개 값이어야 합니다.")
            Return parts.Select(Function(x) Integer.Parse(x.Split("|"c)(0).Trim())).ToArray()
        End Function

        Private Shared Function ListPart(value As String, index As Integer) As List(Of Integer)
            Dim parts = value.Split(","c)
            If parts.Length <> 3 Then Throw New FormatException("탐색 값은 쉼표 3개 항목, 후보는 | 로 구분하세요.")
            Return parts(index).Split("|"c).Select(Function(x) Integer.Parse(x.Trim())).Distinct().ToList()
        End Function

        Private Function SelectedInterval() As CandleInterval
            Dim values = {CandleInterval.Min1, CandleInterval.Min3, CandleInterval.Min5,
                          CandleInterval.Min15, CandleInterval.Min30, CandleInterval.Min60,
                          CandleInterval.Tick120, CandleInterval.Tick240, CandleInterval.Tick360,
                          CandleInterval.Tick720, CandleInterval.Day, CandleInterval.Week}
            Return values(_interval.SelectedIndex)
        End Function

        Private Sub Busy(value As Boolean, message As String)
            _run.Enabled = Not value : _sweep.Enabled = Not value
            _walkForward.Enabled = Not value
            _import.Enabled = Not value : _export.Enabled = Not value
            _chartVerify.Enabled = Not value AndAlso _displayedSymbolResults.Count > 0
            If message IsNot Nothing Then _status.Text = message
        End Sub

        Private Sub GridCellDoubleClick(sender As Object, e As DataGridViewCellEventArgs)
            If e.RowIndex < 0 Then Return
            _grid.CurrentCell = _grid.Rows(e.RowIndex).Cells("Symbol")
            OpenSelectedChartVerification()
        End Sub

        Private Sub ChartVerifyClicked(sender As Object, e As EventArgs)
            OpenSelectedChartVerification()
        End Sub

        Private Sub OpenSelectedChartVerification()
            If _lastSummary Is Nothing OrElse Not _lastRunInterval.HasValue OrElse
               _displayedSymbolResults.Count = 0 Then
                MessageBox.Show("먼저 단일 백테스트를 실행하세요.", "차트검증")
                Return
            End If
            If _grid.CurrentRow Is Nothing Then
                MessageBox.Show("검증할 종목을 선택하세요.", "차트검증")
                Return
            End If
            Dim symbol = Convert.ToString(_grid.CurrentRow.Cells("Symbol").Value).Trim()
            Dim result = _displayedSymbolResults.FirstOrDefault(
                Function(item) String.Equals(item.Symbol, symbol, StringComparison.OrdinalIgnoreCase))
            If result Is Nothing OrElse result.Evaluation Is Nothing Then
                MessageBox.Show("선택 종목에 표시할 전략 평가 결과가 없습니다.", "차트검증")
                Return
            End If
            Dim key = CandleCacheKey(result.Symbol, result.CapturedAt.Date)
            Dim candles As List(Of CandleItem) = Nothing
            If Not _cache.TryGetValue(key, candles) OrElse candles Is Nothing OrElse candles.Count = 0 Then
                MessageBox.Show("선택 종목의 캔들 캐시가 없습니다. 백테스트를 다시 실행하세요.", "차트검증")
                Return
            End If
            Try
                Dim viewer As New BacktestVerificationForm(
                    result, candles, _lastRunInterval.Value, _lastSummary.Parameters.Clone())
                viewer.Show(Me)
            Catch ex As Exception
                ChartKit.Core.ChartLog.Error("백테스트 차트 검증 화면을 열지 못했습니다.", ex)
                MessageBox.Show(ex.Message, "차트검증")
            End Try
        End Sub

        Private Shared Sub NumberBox(box As NumericUpDown, min As Decimal, max As Decimal,
                                     value As Decimal, Optional decimals As Integer = 1)
            box.Minimum = min : box.Maximum = max : box.Value = value : box.Width = 65
            box.DecimalPlaces = decimals : box.BackColor = Color.Black : box.ForeColor = Color.White
        End Sub

        Private Shared Sub AddInput(panel As FlowLayoutPanel, caption As String, control As Control)
            panel.Controls.Add(New Label With {
                .Text = caption, .AutoSize = True, .ForeColor = Color.White,
                .Padding = New Padding(5, 5, 2, 0)})
            control.BackColor = Color.Black : control.ForeColor = Color.White
            control.Width = Math.Max(control.Width, 75)
            panel.Controls.Add(control)
        End Sub
    End Class
End Namespace
