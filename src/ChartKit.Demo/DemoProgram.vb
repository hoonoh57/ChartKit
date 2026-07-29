Imports System.Windows.Forms
Imports System.Threading.Tasks
Imports ChartKit.Core
Imports ChartKit.Layers
Imports ChartKit.Models

Public Module DemoProgram
    <STAThread>
    Public Sub Main()
        Application.EnableVisualStyles()
        Application.SetCompatibleTextRenderingDefault(False)

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
        ChartKit.Core.IndicatorCatalog.Register("MACD", "MACD(12,26,9)", GetType(ChartKit.Indicators.MACD_Indicator), 12, 26, 9)
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
        chart.Layers.Add(New PanelLayer())        '' Z=35
        chart.Layers.Add(New ChartKit.Layers.LegendLayer())

                '' ── 데이터소스 선택 (차트는 소스를 모른다. 여기서만 결정) ──
        '' ── 데이터소스 선택: USE_KIWOOM 스위치 (키 없으면 Random 폴백) ──
        Dim useKiwoom = ChartKit.DataSources.EnvConfig.GetBool("USE_KIWOOM", False)
        Dim hasKey = Not String.IsNullOrEmpty(ChartKit.DataSources.EnvConfig.AppKey) _
                     AndAlso Not String.IsNullOrEmpty(ChartKit.DataSources.EnvConfig.SecretKey)
        Dim initialSymbolName As String = ""

        If useKiwoom AndAlso hasKey Then
            Dim kreq As New ChartKit.Abstractions.CandleRequest With {
                .Symbol = ChartKit.DataSources.EnvConfig.DefaultSymbol,
                .Interval = ChartKit.Abstractions.CandleInterval.Tick720,
                .Count = 200}
            Try
                Dim kiwoomSource As New ChartKit.DataSources.KiwoomRestSource()
                Try
                    initialSymbolName = kiwoomSource.GetStockName(kreq.Symbol)
                Catch
                    '' 종목명 실패가 차트 데이터 로드를 막아서는 안 된다.
                    initialSymbolName = ""
                End Try
                chart.AttachDataSource(kiwoomSource, kreq)
            Catch ex As Exception
                MessageBox.Show("키움 데이터 로드 실패, 랜덤으로 대체합니다:" & vbCrLf & ex.Message,
                                "KiwoomRestSource", MessageBoxButtons.OK, MessageBoxIcon.Warning)
                Dim rsrc As ChartKit.Abstractions.ICandleDataSource =
                    ChartKit.DataSources.DataSourceFactory.Create(ChartKit.DataSources.DataSourceKind.Random)
                Dim rreq As New ChartKit.Abstractions.CandleRequest With {
                    .Symbol = "TEST", .Interval = ChartKit.Abstractions.CandleInterval.Min1, .Count = 120}
                chart.LoadCandles(rsrc.GetCandles(rreq))
            End Try
        Else
            Dim source As ChartKit.Abstractions.ICandleDataSource =
                ChartKit.DataSources.DataSourceFactory.Create(ChartKit.DataSources.DataSourceKind.Random)
            Dim req As New ChartKit.Abstractions.CandleRequest With {
                .Symbol = "TEST", .Interval = ChartKit.Abstractions.CandleInterval.Min1, .Count = 120}
            chart.LoadCandles(source.GetCandles(req))
            initialSymbolName = "테스트"
            ' ChartKit.Abstractions.CandleInterval.Min1, .Count = 120

        End If
        chart.RestoreState()   '' 저장된 지표/뷰포트/패널/토글 복원
        If chart.LastRestoreIndicatorCount <= 0 Then
        chart.AddIndicator(New ChartKit.Indicators.MA_Indicator(20))
        chart.AddIndicator(New ChartKit.Indicators.MA_Indicator(60))
    End If

        Dim toolbar As New ChartKit.UI.ChartToolbar()
        toolbar.Initialize(ChartKit.DataSources.EnvConfig.DefaultSymbol, initialSymbolName,
                           ChartKit.Abstractions.CandleInterval.Tick720, 200, 100)
        toolbar.SetDateRange(chart.GetFirstCandleDate(), chart.GetLastCandleDate())
        toolbar.SetStatus(chart.CandleCount.ToString() & "개 로드")

        AddHandler toolbar.QueryRequested,
            Async Sub(sender As Object, e As ChartKit.UI.ChartQueryEventArgs)
                toolbar.SetBusy(True)
                toolbar.SetStatus("데이터 조회 중...")
                Try
                    Dim req As New ChartKit.Abstractions.CandleRequest With {
                        .Symbol = e.Symbol,
                        .Interval = e.Interval,
                        .Count = e.TotalCount}
                    Dim source As ChartKit.Abstractions.ICandleDataSource
                    If useKiwoom AndAlso hasKey Then
                        source = New ChartKit.DataSources.KiwoomRestSource()
                    Else
                        source = ChartKit.DataSources.DataSourceFactory.Create(
                            ChartKit.DataSources.DataSourceKind.Random)
                    End If

                    '' REST 연속조회 중에도 UI가 멈추지 않도록 백그라운드에서 로드한다.
                    Dim bars = Await Task.Run(Function() source.GetCandles(req))
                    Dim symbolName = e.SymbolName
                    Dim kiwoomSource = TryCast(source, ChartKit.DataSources.KiwoomRestSource)
                    If kiwoomSource IsNot Nothing Then
                        Try
                            symbolName = Await Task.Run(Function() kiwoomSource.GetStockName(e.Symbol))
                        Catch
                            symbolName = ""
                        End Try
                    End If
                    chart.LoadCandles(bars)
                    chart.SetVisibleCandleCount(e.VisibleCount)
                    chart.AttachRealtimeSource(source, req)
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

        AddHandler toolbar.DateRequested,
            Sub(sender As Object, e As ChartKit.UI.ChartDateEventArgs)
                If chart.MoveToDate(e.TradingDate) Then
                    toolbar.SetStatus(e.TradingDate.ToString("yyyy-MM-dd") & " 이동")
                Else
                    toolbar.SetStatus("선택 일자의 캔들이 없습니다.", True)
                End If
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
