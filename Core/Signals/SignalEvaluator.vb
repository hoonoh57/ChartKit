Imports ChartKit.Models
Imports ChartKit.Abstractions

Namespace Core.Signals
    Public NotInheritable Class SignalHit
        Public Property CandleIndex As Integer
        Public Property Rule As SignalRule
        Public Property EntryScore As JmaEntryScoreSnapshot
    End Class

    Public NotInheritable Class SignalEvaluator
        Private Const LatchMaxBars As Integer = 10

        Private Sub New()
        End Sub

        Public Shared Function Evaluate(rules As IEnumerable(Of SignalRule),
                                        results As IReadOnlyDictionary(Of String, IndicatorResultRingBuffer),
                                        startIndex As Integer,
                                        endIndex As Integer) As List(Of SignalHit)
            Dim hits As New List(Of SignalHit)
            If rules Is Nothing OrElse results Is Nothing Then Return hits

            For Each rule In rules
                If rule Is Nothing OrElse String.IsNullOrEmpty(rule.IndicatorA) OrElse String.IsNullOrEmpty(rule.IndicatorB) Then Continue For
                Dim ra As IndicatorResultRingBuffer = Nothing
                Dim rb As IndicatorResultRingBuffer = Nothing
                If Not results.TryGetValue(rule.IndicatorA, ra) OrElse Not results.TryGetValue(rule.IndicatorB, rb) Then Continue For

                Dim armed = False
                Dim armedBar = -1
                For i = Math.Max(1, startIndex) To endIndex
                    Dim a0 = ValueAt(ra, i - 1)
                    Dim b0 = ValueAt(rb, i - 1)
                    Dim a1 = ValueAt(ra, i)
                    Dim b1 = ValueAt(rb, i)
                    If Single.IsNaN(a0) OrElse Single.IsNaN(b0) OrElse Single.IsNaN(a1) OrElse Single.IsNaN(b1) Then Continue For

                    Dim crossedUp = a0 <= b0 AndAlso a1 > b1
                    Dim crossedDown = a0 >= b0 AndAlso a1 < b1
                    Dim hit = False
                    If Not rule.RequireBRising Then
                        hit = If(rule.CrossUp, crossedUp, crossedDown)
                    ElseIf rule.CrossUp Then
                        If crossedUp Then armed = True : armedBar = i
                        If armed AndAlso a1 < b1 Then armed = False
                        If armed AndAlso i - armedBar > LatchMaxBars Then armed = False
                        If armed AndAlso b1 > b0 Then hit = True : armed = False
                    Else
                        If crossedDown Then armed = True : armedBar = i
                        If armed AndAlso a1 > b1 Then armed = False
                        If armed AndAlso i - armedBar > LatchMaxBars Then armed = False
                        If armed AndAlso b1 < b0 Then hit = True : armed = False
                    End If
                    If hit Then
                        Dim scoreSnapshot As JmaEntryScoreSnapshot = Nothing
                        If rule.CrossUp AndAlso
                           rule.IndicatorA.StartsWith("JMA_", StringComparison.OrdinalIgnoreCase) AndAlso
                           rule.IndicatorB.StartsWith("JMA_", StringComparison.OrdinalIgnoreCase) Then
                            JmaEntryScoreCalculator.TryCalculate(rb, i, scoreSnapshot)
                        End If
                        hits.Add(New SignalHit With {
                            .CandleIndex = i, .Rule = rule, .EntryScore = scoreSnapshot})
                    End If
                Next
            Next
            Return hits
        End Function

        Private Shared Function ValueAt(results As IReadOnlyList(Of IndicatorResult), index As Integer) As Single
            If results Is Nothing OrElse index < 0 OrElse index >= results.Count Then Return Single.NaN
            Dim result = results(index)
            If result Is Nothing OrElse result.Values Is Nothing Then Return Single.NaN
            Dim value As Single
            If result.Values.TryGetValue("Value", value) Then Return value
            For Each pair In result.Values
                If Not Single.IsNaN(pair.Value) Then Return pair.Value
            Next
            Return Single.NaN
        End Function
    End Class
End Namespace
