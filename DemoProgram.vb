Imports System.Windows.Forms
Imports System.Threading.Tasks
Imports ChartKit.Core
Imports ChartKit.Layers
Imports ChartKit.Models

Public Module DemoProgram
    Private Function CreateBacktestDataSource() As ChartKit.Abstractions.ICandleDataSource
        Dim cybosUrl = ChartKit.DataSources.EnvConfig.Get("CYBOS_API_URL", "").Trim()
        If cybosUrl.Length > 0 Then
            Return New ChartKit.DataSources.CybosHttpDataSource(cybosUrl)
        End If
        Return New ChartKit.DataSources.KiwoomRestSource()
    End Function

    <STAThread>
    Public Sub Main()
        Application.EnableVisualStyles()
        Application.SetCompatibleTextRenderingDefault(False)

        If Environment.GetCommandLineArgs().
            Any(Function(x) String.Equals(x, "--backtest-ui", StringComparison.OrdinalIgnoreCase)) Then
            Dim backtest As New ChartKit.UI.BacktestForm(
                Function() CreateBacktestDataSource())
            Application.Run(backtest)
            Return
        End If

        '' 지표 카탈로그 등록 (컨텍스트 메뉴가 이 목록을 사용)
        ChartKit.Core.IndicatorCatalog.Register("MA5", "이동평균 SMA(5)", GetType(ChartKit.Indicators.MA_Indicator), 5, "SMA")
        ChartKit.Core.IndicatorCatalog.Register("MA20", "이동평균 SMA(20)", GetType(ChartKit.Indicators.MA_Indicator), 20, "SMA")
        ChartKit.Core.IndicatorCatalog.Register("MA60", "이동평균 SMA(60)", GetType(ChartKit.Indicators.MA_Indicator), 60, "SMA")
        ChartKit.Core.IndicatorCatalog.Register("EMA20", "지수이평 EMA(20)", GetType(ChartKit.Indicators.MA_Indicator), 20, "EMA")
        ChartKit.Core.IndicatorCatalog.Register("RSI14", "RSI(14)", GetType(ChartKit.Indicators.RSI_Indicator), 14, 9)
        ChartKit.Core.IndicatorCatalog.Register("OBV20", "OBV(MA20)", GetType(ChartKit.Indicators.OBV_Indicator), 20)
        ChartKit.Core.IndicatorCatalog.Register("VWAP", "VWAP", GetType(ChartKit.Indicators.VWAP_Indicator), 1.0F, 2.0F)
        ChartKit.Core.IndicatorCatalog.Register("DISP20", "이격도(20)", GetType(ChartKit.Indicators.Disparity_Indicator), 20)
        ChartKit.Core.IndicatorCatalog.Register("JMA14", "JMA(14,50,2)", GetType(ChartKit.Indicators.JMA_Indicator), 14, 50, 2)
        ChartKit.Core.IndicatorCatalog.Register("MACD", "MACD(10,20,5)", GetType(ChartKit.Indicators.MACD_Indicator), 10, 20, 5)
        ChartKit.Core.IndicatorCatalog.Register("ST10", "SuperTrend(10,3.0)", GetType(ChartKit.Indicators.SuperTrend_Indicator), 10, 3.0F)

        Dim chart As New ChartControl() With {.Dock = DockStyle.Fill}
        chart.Layers.Add(New GridAxisLayer())    '' Z=0
        chart.Layers.Add(New ChartKit.Layers.PctAxisLayer())  '' Z=5 좌측 등락률축
        chart.Layers.Add(New VolumeLayer())      '' Z=10
        chart.Layers.Add(New CandleLayer())      '' Z=20
    chart.Layers.Add(New ChartKit.Layers.OverlayShadeLayer())  '' Z=25 (캔들 위, 지표선 아래)
        chart.Layers.Add(New CrosshairLayer())   '' Z=100
        chart.Layers.Add(New IndicatorLayer())    '' Z=30
        chart.Layers.Add(New ChartKit.Layers.SignalLayer())  '' Z=40 신호 화살표
        chart.Layers.Add(New ChartKit.Layers.StrategyTradeLayer()) '' Z=45 전략 진입/청산
        chart.Layers.Add(New PanelLayer())        '' Z=35
        chart.Layers.Add(New ChartKit.Layers.LegendLayer())

        '' 키움 설정이 명시된 경우에만 실행한다. 실패를 랜덤 데이터로 숨기지 않는다.
        Dim useKiwoom = ChartKit.DataSources.EnvConfig.GetBool("USE_KIWOOM", False)
        Dim hasKey = Not String.IsNullOrEmpty(ChartKit.DataSources.EnvConfig.AppKey) _
                     AndAlso Not String.IsNullOrEmpty(ChartKit.DataSources.EnvConfig.SecretKey)
        Dim initialSymbolName As String = ""
        Dim activeKiwoomSource As ChartKit.DataSources.KiwoomRestSource = Nothing

        If useKiwoom AndAlso hasKey Then
            Dim kreq As New ChartKit.Abstractions.CandleRequest With {
                .Symbol = ChartKit.DataSources.EnvConfig.DefaultSymbol,
                .Interval = ChartKit.Abstractions.CandleInterval.Tick720,
                .Count = 200}
            Try
                Dim kiwoomSource As New ChartKit.DataSources.KiwoomRestSource()
                activeKiwoomSource = kiwoomSource
                Try
                    initialSymbolName = kiwoomSource.GetStockName(kreq.Symbol)
                Catch ex As Exception
                    '' 종목명 실패가 차트 데이터 로드를 막아서는 안 된다.
                    ChartKit.Core.ChartLog.Warning($"종목명 조회 실패: {kreq.Symbol}", ex)
                    initialSymbolName = ""
                End Try
                chart.AttachDataSource(kiwoomSource, kreq)
            Catch ex As Exception
                ChartKit.Core.ChartLog.Error("키움 초기 데이터 로드 실패", ex)
                MessageBox.Show("키움 데이터 로드에 실패했습니다. 랜덤 데이터로 대체하지 않습니다." &
                                vbCrLf & vbCrLf & ex.Message,
                                "KiwoomRestSource", MessageBoxButtons.OK, MessageBoxIcon.Error)
            End Try
        Else
            Dim reason = If(Not useKiwoom,
                            "USE_KIWOOM=true 설정이 필요합니다.",
                            "키움 App Key 또는 Secret Key가 비어 있습니다.")
            MessageBox.Show(reason & vbCrLf & "설정 파일: " & ChartKit.DataSources.EnvConfig.SourcePath,
                            "키움 설정 오류", MessageBoxButtons.OK, MessageBoxIcon.Error)
        End If
        chart.RestoreState()   '' 저장된 지표/뷰포트/패널/토글 복원
        If chart.LastRestoreIndicatorCount <= 0 Then
        chart.AddIndicator(New ChartKit.Indicators.MA_Indicator(20))
        chart.AddIndicator(New ChartKit.Indicators.MA_Indicator(60))
    End If

        Dim toolbar As New ChartKit.UI.ChartToolbar()
        toolbar.Initialize(ChartKit.DataSources.EnvConfig.DefaultSymbol, initialSymbolName,
                           ChartKit.Abstractions.CandleInterval.Tick720,
                           Math.Max(10, chart.CandleCount), 100)
        toolbar.SetDateRange(chart.GetFirstCandleDate(), chart.GetLastCandleDate())
        toolbar.SetStatus(chart.CandleCount.ToString() & "개 로드")

        AddHandler toolbar.VisibleCountChanged,
            Sub(sender As Object, e As EventArgs)
                chart.SetVisibleCandleCount(toolbar.VisibleCount)
            End Sub
        AddHandler chart.VisibleCandleCountChanged,
            Sub(sender As Object, e As EventArgs)
                toolbar.SetVisibleCount(chart.VisibleCandleCount)
            End Sub
        AddHandler toolbar.QueryRequested,
            Async Sub(sender As Object, e As ChartKit.UI.ChartQueryEventArgs)
                toolbar.SetBusy(True)
                toolbar.SetStatus("데이터 조회 중...")
                Try
                    Dim req As New ChartKit.Abstractions.CandleRequest With {
                        .Symbol = e.Symbol,
                        .Interval = e.Interval,
                        .Count = e.TotalCount}
                    If Not useKiwoom OrElse Not hasKey Then
                        Throw New InvalidOperationException(
                            "키움 REST 설정이 유효하지 않습니다. " & ChartKit.DataSources.EnvConfig.SourcePath)
                    End If
                    Dim source As New ChartKit.DataSources.KiwoomRestSource()

                    '' REST 연속조회 중에도 UI가 멈추지 않도록 백그라운드에서 로드한다.
                    Dim bars = Await Task.Run(Function() source.GetCandles(req))
                    Dim symbolName = e.SymbolName
                    Dim kiwoomSource = TryCast(source, ChartKit.DataSources.KiwoomRestSource)
                    If kiwoomSource IsNot Nothing Then
                        Try
                            symbolName = Await Task.Run(Function() kiwoomSource.GetStockName(e.Symbol))
                        Catch ex As Exception
                            ChartKit.Core.ChartLog.Warning($"종목명 조회 실패: {e.Symbol}", ex)
                            symbolName = ""
                        End Try
                    End If
                    chart.LoadCandles(bars)
                    chart.SetVisibleCandleCount(e.VisibleCount)
                    chart.AttachRealtimeSource(source, req)
                    activeKiwoomSource = source
                    toolbar.SetSymbolName(symbolName)
                    toolbar.SetDateRange(chart.GetFirstCandleDate(), chart.GetLastCandleDate())
                    toolbar.SetStatus($"{e.Symbol} {bars.Count}개 로드 · 실시간")
                Catch ex As Exception
                    toolbar.SetStatus("조회 실패", True)
                    MessageBox.Show(ex.Message, "차트 데이터 조회",
                                    MessageBoxButtons.OK, MessageBoxIcon.Error)
                Finally
                    toolbar.SetBusy(False)
                End Try
            End Sub

        AddHandler toolbar.MoreRequested,
            Async Sub(sender As Object, e As ChartKit.UI.ChartQueryEventArgs)
                If activeKiwoomSource Is Nothing Then
                    toolbar.SetStatus("먼저 조회를 실행하세요.", True)
                    Return
                End If
                toolbar.SetBusy(True)
                toolbar.SetStatus("이전 캔들 추가 조회 중...")
                Try
                    Dim req As New ChartKit.Abstractions.CandleRequest With {
                        .Symbol = e.Symbol,
                        .Interval = e.Interval,
                        .Count = e.TotalCount}
                    Dim older = Await Task.Run(Function() activeKiwoomSource.GetOlderCandles(req))
                    Dim added = chart.PrependCandles(older)
                    toolbar.SetDateRange(chart.GetFirstCandleDate(), chart.GetLastCandleDate())
                    If added > 0 Then
                        toolbar.SetStatus($"{added}개 이전 봉 추가 · 총 {chart.CandleCount}개")
                    Else
                        toolbar.SetStatus("더 이전 데이터가 없습니다.")
                    End If
                Catch ex As Exception
                    ChartKit.Core.ChartLog.Error("이전 캔들 추가 조회 실패", ex)
                    toolbar.SetStatus("추가 조회 실패", True)
                    MessageBox.Show(ex.Message, "이전 캔들 추가 조회",
                                    MessageBoxButtons.OK, MessageBoxIcon.Error)
                Finally
                    toolbar.SetBusy(False)
                End Try
            End Sub

        AddHandler toolbar.DateRequested,
            Sub(sender As Object, e As ChartKit.UI.ChartDateEventArgs)
                If chart.MoveToDate(e.TradingDate) Then
                    toolbar.SetStatus(e.TradingDate.ToString("yyyy-MM-dd") & " 이동")
                Else
                    toolbar.SetStatus("선택 일자의 캔들이 없습니다.", True)
                End If
            End Sub

        AddHandler toolbar.BacktestRequested,
            Sub(sender As Object, e As EventArgs)
                If Not useKiwoom OrElse Not hasKey Then
                    MessageBox.Show("키움 REST 설정이 필요합니다.", "백테스트",
                                    MessageBoxButtons.OK, MessageBoxIcon.Warning)
                    Return
                End If
                Dim backtest As New ChartKit.UI.BacktestForm(
                    Function() CreateBacktestDataSource())
                backtest.Show()
            End Sub

        Dim f As New Form() With {
            .Text = "ChartKit Demo - Crosshair", .Width = 1200, .Height = 700,
            .BackColor = Drawing.Color.Black, .ForeColor = Drawing.Color.White}
        Dim layout As New TableLayoutPanel() With {
            .Dock = DockStyle.Fill, .BackColor = Drawing.Color.Black,
            .ColumnCount = 1, .RowCount = 2, .Margin = New Padding(0), .Padding = New Padding(0)}
        layout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100.0F))
        layout.RowStyles.Add(New RowStyle(SizeType.Absolute, 38.0F))
        layout.RowStyles.Add(New RowStyle(SizeType.Percent, 100.0F))
        toolbar.Dock = DockStyle.Fill
        toolbar.Margin = New Padding(0)
        chart.Dock = DockStyle.Fill
        chart.Margin = New Padding(0)
        layout.Controls.Add(toolbar, 0, 0)
        layout.Controls.Add(chart, 0, 1)
        f.Controls.Add(layout)
        AddHandler f.FormClosing, Sub(s, ev) chart.SaveState()
        Application.Run(f)
    End Sub
End Module
