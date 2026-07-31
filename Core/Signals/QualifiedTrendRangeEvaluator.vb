Imports ChartKit.Abstractions
Imports ChartKit.Models
Imports System.Linq

Namespace Core.Signals
    Public NotInheritable Class QualifiedTrendRange
        Public Property StartIndex As Integer
        Public Property EndIndex As Integer
        Public Property SourceHit As SignalHit
    End Class

    Public NotInheritable Class QualifiedTrendRangeEvaluator
        Public Const MinimumEntryScore As Integer = 60
        Private Const ConfirmationBars As Integer = 2

        Private Sub New()
        End Sub

        Public Shared Function Evaluate(rule As OverlayShadeRule,
                                        signalRules As IEnumerable(Of SignalRule),
                                        results As IReadOnlyDictionary(Of String, IndicatorResultRingBuffer),
                                        startIndex As Integer,
                                        endIndex As Integer) As List(Of QualifiedTrendRange)
            Return Evaluate(rule, signalRules, results, startIndex, endIndex,
                            New TrendQualificationOptions())
        End Function

        Public Shared Function Evaluate(rule As OverlayShadeRule,
                                        signalRules As IEnumerable(Of SignalRule),
                                        results As IReadOnlyDictionary(Of String, IndicatorResultRingBuffer),
                                        startIndex As Integer,
                                        endIndex As Integer,
                                        options As TrendQualificationOptions) As List(Of QualifiedTrendRange)
            Dim ranges As New List(Of QualifiedTrendRange)
            If rule Is Nothing OrElse signalRules Is Nothing OrElse results Is Nothing Then Return ranges

            Dim matchingRules = signalRules.Where(
                Function(r) r IsNot Nothing AndAlso r.CrossUp AndAlso
                    String.Equals(r.IndicatorA, rule.IndicatorA, StringComparison.Ordinal) AndAlso
                    String.Equals(r.IndicatorB, rule.IndicatorB, StringComparison.Ordinal)).ToList()
            If matchingRules.Count = 0 Then
                '' JMA 음영 규칙은 그 자체로 A/B 상향돌파를 시작 조건으로 가진다.
                '' 사용자가 표시용 SignalRule을 삭제해도 음영/전략 의미가 사라지지 않게 한다.
                matchingRules.Add(New SignalRule With {
                    .Name = "JMA shade implicit cross-up",
                    .IndicatorA = rule.IndicatorA,
                    .IndicatorB = rule.IndicatorB,
                    .CrossUp = True,
                    .RequireBRising = False})
            End If

            Dim shortJma As IndicatorResultRingBuffer = Nothing
            Dim longJma As IndicatorResultRingBuffer = Nothing
            If Not results.TryGetValue(rule.IndicatorA, shortJma) OrElse
               Not results.TryGetValue(rule.IndicatorB, longJma) Then Return ranges

            Dim scanStart = Math.Max(0, startIndex - 10)
            For Each hit In SignalEvaluator.Evaluate(matchingRules, results, scanStart, endIndex)
                Dim confirmed = FindConfirmation(shortJma, longJma, hit.CandleIndex, endIndex, options)
                If confirmed < 0 Then Continue For
                Dim rangeEnd = confirmed
                For i = confirmed To endIndex
                    If Not IsBullish(shortJma, longJma, i) Then Exit For
                    rangeEnd = i
                Next
                If rangeEnd >= confirmed Then
                    AddMerged(ranges, New QualifiedTrendRange With {
                        .StartIndex = confirmed, .EndIndex = rangeEnd, .SourceHit = hit})
                End If
            Next
            Return ranges
        End Function

        Private Shared Function FindConfirmation(shortJma As IReadOnlyList(Of IndicatorResult),
                                                 longJma As IReadOnlyList(Of IndicatorResult),
                                                 hitIndex As Integer,
                                                 endIndex As Integer,
                                                 options As TrendQualificationOptions) As Integer
            If options Is Nothing Then options = New TrendQualificationOptions()
            Dim consecutive = 0
            For i = hitIndex To endIndex
                Dim shortValue = ValueAt(shortJma, i, "Value")
                Dim longValue = ValueAt(longJma, i, "Value")
                If Single.IsNaN(shortValue) OrElse Single.IsNaN(longValue) OrElse shortValue < longValue Then
                    Exit For
                End If
                If IsRising(longJma, i) Then
                    consecutive += 1
                Else
                    consecutive = 0
                End If
                If consecutive >= options.ConfirmationBars Then
                    Dim currentScore As JmaEntryScoreSnapshot = Nothing
                    If JmaEntryScoreCalculator.TryCalculate(longJma, i, currentScore) AndAlso
                       currentScore.Score >= options.MinimumEntryScore Then Return i
                End If
            Next
            Return -1
        End Function

        Private Shared Function IsBullish(shortJma As IReadOnlyList(Of IndicatorResult),
                                          longJma As IReadOnlyList(Of IndicatorResult),
                                          index As Integer) As Boolean
            Dim shortValue = ValueAt(shortJma, index, "Value")
            Dim longValue = ValueAt(longJma, index, "Value")
            Return Not Single.IsNaN(shortValue) AndAlso Not Single.IsNaN(longValue) AndAlso
                   shortValue >= longValue AndAlso IsNonFalling(longJma, index)
        End Function

        Private Shared Function IsRising(results As IReadOnlyList(Of IndicatorResult), index As Integer) As Boolean
            Dim current = ValueAt(results, index, "Value")
            Dim previous = ValueAt(results, index - 1, "Value")
            Return Not Single.IsNaN(current) AndAlso Not Single.IsNaN(previous) AndAlso current > previous
        End Function

        Private Shared Function IsNonFalling(results As IReadOnlyList(Of IndicatorResult), index As Integer) As Boolean
            Dim current = ValueAt(results, index, "Value")
            Dim previous = ValueAt(results, index - 1, "Value")
            Return Not Single.IsNaN(current) AndAlso Not Single.IsNaN(previous) AndAlso current >= previous
        End Function

        Private Shared Function ValueAt(results As IReadOnlyList(Of IndicatorResult),
                                        index As Integer,
                                        key As String) As Single
            If results Is Nothing OrElse index < 0 OrElse index >= results.Count Then Return Single.NaN
            Dim result = results(index)
            If result Is Nothing OrElse result.Values Is Nothing Then Return Single.NaN
            Dim value As Single
            Return If(result.Values.TryGetValue(key, value), value, Single.NaN)
        End Function

        Private Shared Sub AddMerged(ranges As List(Of QualifiedTrendRange),
                                     candidate As QualifiedTrendRange)
            If ranges.Count = 0 Then
                ranges.Add(candidate)
                Return
            End If
            Dim last = ranges(ranges.Count - 1)
            If candidate.StartIndex <= last.EndIndex + 1 Then
                last.EndIndex = Math.Max(last.EndIndex, candidate.EndIndex)
            Else
                ranges.Add(candidate)
            End If
        End Sub
    End Class
End Namespace
