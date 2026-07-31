Option Strict On
Option Explicit On
Option Infer Off

Imports ChartKit.Core.Signals
Imports ChartKit.Core.Strategies
Imports ChartKit.Indicators
Imports ChartKit.Models

Namespace Core.Backtesting
    Public NotInheritable Class StrategyBacktestEngine
        Public Function Evaluate(symbol As String,
                                 candles As IReadOnlyList(Of CandleItem),
                                 capturedAt As DateTime,
                                 parameters As StrategyParameterSet) As SymbolBacktestResult
            Dim output As New SymbolBacktestResult With {
                .Symbol = symbol,
                .CapturedAt = capturedAt,
                .Costs = If(parameters?.Costs?.Clone(), New BacktestCostOptions())}
            If candles Is Nothing OrElse candles.Count < 2 Then
                output.ErrorMessage = "캔들 데이터가 부족합니다."
                Return output
            End If
            If parameters Is Nothing Then Throw New ArgumentNullException(NameOf(parameters))

            Dim validation As String = parameters.Validate()
            If validation.Length > 0 Then Throw New ArgumentException(validation, NameOf(parameters))

            Dim captureIndex As Integer = CausalCandleSelector.FindLastClosedIndex(candles, capturedAt)
            If captureIndex < 0 OrElse captureIndex >= candles.Count - 1 Then
                output.ErrorMessage = "지정 시각까지 확정된 당일 캔들이 없거나 이후 실행 봉이 없습니다."
                Return output
            End If

            Dim shortJma As New JMA_Indicator(parameters.ShortJma.Period,
                                              parameters.ShortJma.Phase,
                                              parameters.ShortJma.Power)
            Dim longJma As New JMA_Indicator(parameters.LongJma.Period,
                                             parameters.LongJma.Phase,
                                             parameters.LongJma.Power)
            Dim macd As New MACD_Indicator(parameters.Macd.FastPeriod,
                                           parameters.Macd.SlowPeriod,
                                           parameters.Macd.SignalPeriod)
            Dim engine As New IndicatorEngine()
            engine.Register(shortJma)
            engine.Register(longJma)
            engine.Register(macd)
            engine.CalculateAll(candles)

            Dim shade As New OverlayShadeRule With {
                .IndicatorA = shortJma.Name, .IndicatorB = longJma.Name}
            Dim signal As New SignalRule With {
                .Name = "Backtest JMA cross",
                .IndicatorA = shortJma.Name,
                .IndicatorB = longJma.Name,
                .CrossUp = True}
            Dim ranges As List(Of QualifiedTrendRange) = QualifiedTrendRangeEvaluator.Evaluate(
                shade, {signal}, engine.Results, captureIndex, candles.Count - 1,
                parameters.Qualification)
            Dim macdResults As IndicatorResultRingBuffer = Nothing
            If Not engine.Results.TryGetValue(macd.Name, macdResults) Then
                output.ErrorMessage = "MACD 계산 결과가 없습니다."
                Return output
            End If

            output.CaptureIndex = captureIndex
            output.CapturePrice = candles(captureIndex).Close
            output.Evaluation = BasicJmaMacdStrategy.Evaluate(
                candles, macdResults, ranges,
                New StrategyCapture With {
                    .CandleIndex = captureIndex,
                    .CapturedAt = capturedAt,
                    .CapturePrice = output.CapturePrice},
                parameters.Safety)
            Return output
        End Function
    End Class
End Namespace