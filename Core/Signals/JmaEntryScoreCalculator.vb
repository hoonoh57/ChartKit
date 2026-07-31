Imports ChartKit.Abstractions
Imports ChartKit.Models

Namespace Core.Signals
    Public NotInheritable Class JmaEntryScoreSnapshot
        Public Property Score As Integer
        Public Property LongSlope As Single
        Public Property BarsSinceLongTurn As Integer
        Public Property MagnitudeScore As Integer
        Public Property PersistenceScore As Integer
        Public Property AccelerationScore As Integer
        Public Property FreshnessScore As Integer
    End Class

    ' 1차 실험용 점수. 미래 봉을 사용하지 않고 신호 발생 시점에 확정된
    ' 장기 JMA Slope와 상승전환 경과 봉수만으로 계산한다.
    Public NotInheritable Class JmaEntryScoreCalculator
        Private Const LookbackBars As Integer = 100
        Private Const FreshBars As Integer = 10

        Private Sub New()
        End Sub

        Public Shared Function TryCalculate(longJma As IReadOnlyList(Of IndicatorResult),
                                            signalIndex As Integer,
                                            ByRef snapshot As JmaEntryScoreSnapshot) As Boolean
            snapshot = Nothing
            If longJma Is Nothing OrElse signalIndex < 0 OrElse signalIndex >= longJma.Count Then Return False

            Dim slope = ValueAt(longJma, signalIndex, "Slope")
            If Single.IsNaN(slope) Then Return False

            Dim barsSinceTurn = CountPositiveSlopeBars(longJma, signalIndex)
            Dim score As Integer
            If slope <= 0.0F Then
                score = 0
            Else
                Dim percentile = PositiveSlopePercentile(longJma, signalIndex, slope)
                Dim magnitudeScore = CInt(Math.Round(35.0R * percentile))
                Dim persistenceRatio = Math.Min(1.0R, barsSinceTurn / 3.0R)
                Dim persistenceScore = CInt(Math.Round(30.0R * persistenceRatio))
                Dim acceleration = slope - ValueAt(longJma, signalIndex - 1, "Slope")
                Dim accelerationScore = CInt(Math.Round(20.0R *
                    PositiveAccelerationPercentile(longJma, signalIndex, acceleration)))
                Dim freshness = Math.Max(0.0R, 1.0R - (Math.Max(1, barsSinceTurn) - 1) / CDbl(FreshBars))
                Dim freshnessScore = CInt(Math.Round(15.0R * freshness))
                score = magnitudeScore + persistenceScore + accelerationScore + freshnessScore
                score = Math.Max(0, Math.Min(100, score))

                snapshot = New JmaEntryScoreSnapshot With {
                    .Score = score, .LongSlope = slope, .BarsSinceLongTurn = barsSinceTurn,
                    .MagnitudeScore = magnitudeScore, .PersistenceScore = persistenceScore,
                    .AccelerationScore = accelerationScore, .FreshnessScore = freshnessScore}
            End If

            If snapshot Is Nothing Then
                snapshot = New JmaEntryScoreSnapshot With {
                    .Score = score, .LongSlope = slope, .BarsSinceLongTurn = barsSinceTurn}
            End If
            Return True
        End Function

        Private Shared Function PositiveAccelerationPercentile(results As IReadOnlyList(Of IndicatorResult),
                                                               index As Integer,
                                                               currentAcceleration As Single) As Double
            If Single.IsNaN(currentAcceleration) OrElse currentAcceleration <= 0.0F Then Return 0.0R
            Dim first = Math.Max(1, index - LookbackBars + 1)
            Dim positiveCount = 0
            Dim lessOrEqualCount = 0
            For i = first To index
                Dim current = ValueAt(results, i, "Slope")
                Dim previous = ValueAt(results, i - 1, "Slope")
                If Single.IsNaN(current) OrElse Single.IsNaN(previous) Then Continue For
                Dim acceleration = current - previous
                If acceleration <= 0.0F Then Continue For
                positiveCount += 1
                If acceleration <= currentAcceleration Then lessOrEqualCount += 1
            Next
            If positiveCount = 0 Then Return 0.0R
            Return lessOrEqualCount / CDbl(positiveCount)
        End Function

        Private Shared Function CountPositiveSlopeBars(results As IReadOnlyList(Of IndicatorResult),
                                                       index As Integer) As Integer
            Dim count = 0
            Dim first = Math.Max(0, index - LookbackBars + 1)
            For i = index To first Step -1
                Dim slope = ValueAt(results, i, "Slope")
                If Single.IsNaN(slope) OrElse slope <= 0.0F Then Exit For
                count += 1
            Next
            Return count
        End Function

        Private Shared Function PositiveSlopePercentile(results As IReadOnlyList(Of IndicatorResult),
                                                        index As Integer,
                                                        currentSlope As Single) As Double
            Dim first = Math.Max(0, index - LookbackBars + 1)
            Dim positiveCount = 0
            Dim lessOrEqualCount = 0
            For i = first To index
                Dim slope = ValueAt(results, i, "Slope")
                If Single.IsNaN(slope) OrElse slope <= 0.0F Then Continue For
                positiveCount += 1
                If slope <= currentSlope Then lessOrEqualCount += 1
            Next
            If positiveCount = 0 Then Return 0.0R
            Return lessOrEqualCount / CDbl(positiveCount)
        End Function

        Private Shared Function ValueAt(results As IReadOnlyList(Of IndicatorResult),
                                        index As Integer,
                                        key As String) As Single
            If index < 0 OrElse index >= results.Count Then Return Single.NaN
            Dim result = results(index)
            If result Is Nothing OrElse result.Values Is Nothing Then Return Single.NaN
            Dim value As Single
            Return If(result.Values.TryGetValue(key, value), value, Single.NaN)
        End Function
    End Class
End Namespace
