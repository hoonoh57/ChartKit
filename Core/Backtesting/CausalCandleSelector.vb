Option Strict On
Option Explicit On
Option Infer Off

Imports System.Collections.Generic
Imports ChartKit.Models

Namespace Core.Backtesting
    '' 포착시각 당시 이미 확정된 봉만 선택한다.
    '' 미래 봉의 종가를 포착가격으로 사용하는 look-ahead를 차단한다.
    Public NotInheritable Class CausalCandleSelector
        Private Sub New()
        End Sub

        Public Shared Function FindLastClosedIndex(
            candles As IReadOnlyList(Of CandleItem),
            capturedAt As DateTime) As Integer

            If candles Is Nothing OrElse candles.Count = 0 Then Return -1

            Dim selectedIndex As Integer = -1
            Dim selectedCloseTime As DateTime = DateTime.MinValue
            Dim targetDate As Date = capturedAt.Date

            For index As Integer = 0 To candles.Count - 1
                Dim candle As CandleItem = candles(index)
                If candle Is Nothing OrElse Not candle.IsFinal Then Continue For
                If candle.TradingDate <> targetDate Then Continue For

                Dim closeTime As DateTime = candle.EffectiveCloseTime
                If closeTime = DateTime.MinValue OrElse closeTime > capturedAt Then Continue For

                If selectedIndex < 0 OrElse closeTime >= selectedCloseTime Then
                    selectedIndex = index
                    selectedCloseTime = closeTime
                End If
            Next

            Return selectedIndex
        End Function
    End Class
End Namespace