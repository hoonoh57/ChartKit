Imports System.Drawing
Imports System.Linq
Imports System.Windows.Forms
Imports ChartKit.Abstractions
Imports ChartKit.Core
Imports ChartKit.Core.Backtesting
Imports ChartKit.Indicators
Imports ChartKit.Layers
Imports ChartKit.Models

Namespace UI
    ''' <summary>
    ''' 백테스트 표의 한 종목을 동일한 캔들, 파라미터, 평가 결과로 재현한다.
    ''' 폼을 연 뒤 원본 백테스트 입력값을 바꾸어도 이 화면의 결과는 변하지 않는다.
    ''' </summary>
    Public NotInheritable Class BacktestVerificationForm
        Inherits Form

        Public Sub New(result As SymbolBacktestResult,
                       candles As IReadOnlyList(Of CandleItem),
                       interval As CandleInterval,
                       parameters As StrategyParameterSet)
            If result Is Nothing Then Throw New ArgumentNullException(NameOf(result))
            If result.Evaluation Is Nothing Then
                Throw New ArgumentException("평가 결과가 없는 종목입니다.", NameOf(result))
            End If
            If candles Is Nothing OrElse candles.Count = 0 Then
                Throw New ArgumentException("검증할 캔들이 없습니다.", NameOf(candles))
            End If
            If parameters Is Nothing Then Throw New ArgumentNullException(NameOf(parameters))

            Text = $"차트검증 - {result.Symbol} - {result.CapturedAt:yyyy-MM-dd HH:mm}"
            BackColor = Color.FromArgb(18, 21, 27)
            ForeColor = Color.White
            WindowState = FormWindowState.Maximized
            MinimumSize = New Size(1100, 700)

            Dim header As New Label With {
                .Dock = DockStyle.Fill,
                .AutoSize = False,
                .TextAlign = ContentAlignment.MiddleLeft,
                .Padding = New Padding(12, 0, 4, 0),
                .BackColor = Color.Black,
                .ForeColor = Color.White,
                .Text = BuildHeader(result, interval, parameters)}

            Dim chart As New ChartControl With {.Dock = DockStyle.Fill}
            Dim tradeLayer As New StrategyTradeLayer()
            tradeLayer.SetEvaluation(result.Evaluation)

            chart.Layers.Add(New GridAxisLayer())
            chart.Layers.Add(New PctAxisLayer())
            chart.Layers.Add(New VolumeLayer())
            chart.Layers.Add(New CandleLayer())
            chart.Layers.Add(New OverlayShadeLayer())
            chart.Layers.Add(New IndicatorLayer())
            chart.Layers.Add(New SignalLayer())
            chart.Layers.Add(tradeLayer)
            chart.Layers.Add(New PanelLayer())
            chart.Layers.Add(New LegendLayer())
            chart.Layers.Add(New CrosshairLayer())

            Dim shortJma As New JMA_Indicator(parameters.ShortJma.Period,
                                             parameters.ShortJma.Phase,
                                             parameters.ShortJma.Power)
            Dim longJma As New JMA_Indicator(parameters.LongJma.Period,
                                            parameters.LongJma.Phase,
                                            parameters.LongJma.Power)
            Dim macd As New MACD_Indicator(parameters.Macd.FastPeriod,
                                           parameters.Macd.SlowPeriod,
                                           parameters.Macd.SignalPeriod)
            chart.AddIndicator(shortJma)
            chart.AddIndicator(longJma)
            chart.AddIndicator(New RSI_Indicator(14, 9))
            chart.AddIndicator(New Disparity_Indicator(20))
            chart.AddIndicator(macd)

            chart.AddShadeRule(New OverlayShadeRule With {
                .Name = "백테스트 강세구간",
                .IndicatorA = shortJma.Name,
                .IndicatorB = longJma.Name})
            chart.AddSignalRule(New SignalRule With {
                .Name = "백테스트 JMA 상향돌파",
                .IndicatorA = shortJma.Name,
                .IndicatorB = longJma.Name,
                .CrossUp = True})

            chart.LoadCandles(candles.ToList())
            chart.SetStrategyCapture(result.CaptureIndex, result.CapturePrice)

            ' 지표는 포착 전 데이터로 워밍업하지만 화면에는 포착 시각 이후만 표시한다.
            Dim visibleCount = Math.Max(1, candles.Count - Math.Max(0, result.CaptureIndex))
            chart.SetVisibleCandleCount(visibleCount)

            Dim layout As New TableLayoutPanel With {
                .Dock = DockStyle.Fill,
                .ColumnCount = 1,
                .RowCount = 2,
                .Margin = Padding.Empty,
                .Padding = Padding.Empty}
            layout.RowStyles.Add(New RowStyle(SizeType.Absolute, 42.0F))
            layout.RowStyles.Add(New RowStyle(SizeType.Percent, 100.0F))
            layout.Controls.Add(header, 0, 0)
            layout.Controls.Add(chart, 0, 1)
            Controls.Add(layout)
        End Sub

        Private Shared Function BuildHeader(result As SymbolBacktestResult,
                                            interval As CandleInterval,
                                            parameters As StrategyParameterSet) As String
            Dim tradeCount = result.Evaluation.ClosedTradeCount
            Return $"{result.Symbol}  |  포착 {result.CapturedAt:yyyy-MM-dd HH:mm}  |  " &
                   $"주기 {interval}  |  거래 {tradeCount}회  |  " &
                   $"순수익 {result.NetReturnPct:+0.000;-0.000;0.000}%  |  " &
                   $"MDD {result.MaximumDrawdownPct:0.000}%  |  {parameters}"
        End Function
    End Class
End Namespace
