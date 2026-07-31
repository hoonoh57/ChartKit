Option Strict On
Option Explicit On
Option Infer Off

Imports ChartKit.Abstractions
Imports ChartKit.Models

Namespace Core
    Public NotInheritable Class PanelScale
        Public Property Minimum As Single
        Public Property Maximum As Single
        Public ReadOnly Property Baselines As New Dictionary(Of String, Single)(StringComparer.Ordinal)
    End Class

    Public NotInheritable Class PanelScaleCalculator
        Private Sub New()
        End Sub

        '' 호환 API. 고빈도 렌더링 경로는 CalculateInto로 버퍼를 재사용한다.
        Public Shared Function Calculate(ctx As ChartContext) As Dictionary(Of Integer, PanelScale)
            Dim scales As New Dictionary(Of Integer, PanelScale)()
            Dim panelIndexes As New List(Of Integer)()
            CalculateInto(ctx, scales, panelIndexes)
            Return scales
        End Function

        Public Shared Sub CalculateInto(ctx As ChartContext,
                                        scales As Dictionary(Of Integer, PanelScale),
                                        panelIndexes As List(Of Integer))
            If scales Is Nothing Then Throw New ArgumentNullException(NameOf(scales))
            If panelIndexes Is Nothing Then Throw New ArgumentNullException(NameOf(panelIndexes))

            panelIndexes.Clear()

            If ctx Is Nothing OrElse ctx.Engine Is Nothing Then
                scales.Clear()
                Return
            End If

            Dim indicators As IReadOnlyList(Of IIndicator) = ctx.Engine.GetAllView()
            CollectPanelIndexes(indicators, panelIndexes)

            Dim range As ValueTuple(Of Integer, Integer) = ctx.VisibleRange()

            For slot As Integer = 0 To panelIndexes.Count - 1
                Dim scale As PanelScale = Nothing
                If Not scales.TryGetValue(slot, scale) Then
                    scale = New PanelScale()
                    scales(slot) = scale
                End If

                scale.Minimum = Single.MaxValue
                scale.Maximum = Single.MinValue
                scale.Baselines.Clear()

                Dim panelIndex As Integer = panelIndexes(slot)

                For indicatorIndex As Integer = 0 To indicators.Count - 1
                    Dim indicator As IIndicator = indicators(indicatorIndex)
                    If indicator.PanelIndex <> panelIndex Then Continue For

                    Dim results As IndicatorResultRingBuffer = Nothing
                    If Not ctx.Engine.TryGetResults(indicator, results) OrElse
                       results Is Nothing Then Continue For

                    Dim last As Integer = Math.Min(range.Item2, results.Count - 1)
                    For resultIndex As Integer = range.Item1 To last
                        Dim result As IndicatorResult = results(resultIndex)
                        If result Is Nothing OrElse result.Values Is Nothing Then Continue For

                        Dim valueEnumerator As Dictionary(Of String, Single).Enumerator =
                            result.Values.GetEnumerator()
                        While valueEnumerator.MoveNext()
                            Dim pair As KeyValuePair(Of String, Single) =
                                valueEnumerator.Current
                            If Single.IsNaN(pair.Value) Then Continue While

                            Select Case result.KindOf(pair.Key)
                                Case SeriesKind.Line
                                    scale.Minimum = Math.Min(scale.Minimum, pair.Value)
                                    scale.Maximum = Math.Max(scale.Maximum, pair.Value)

                                Case SeriesKind.Baseline
                                    scale.Baselines(CanonicalBaselineKey(pair.Key)) = pair.Value
                            End Select
                        End While
                    Next
                Next

                Dim baselineEnumerator As Dictionary(Of String, Single).ValueCollection.Enumerator =
                    scale.Baselines.Values.GetEnumerator()
                While baselineEnumerator.MoveNext()
                    Dim level As Single = baselineEnumerator.Current
                    scale.Minimum = Math.Min(scale.Minimum, level)
                    scale.Maximum = Math.Max(scale.Maximum, level)
                End While

                If scale.Minimum = Single.MaxValue OrElse
                   scale.Maximum = Single.MinValue Then
                    scale.Minimum = 0
                    scale.Maximum = 1
                Else
                    If scale.Maximum <= scale.Minimum Then scale.Maximum = scale.Minimum + 1
                    Dim padding As Single = (scale.Maximum - scale.Minimum) * 0.05F
                    scale.Minimum -= padding
                    scale.Maximum += padding
                End If
            Next

            '' 이 계산기가 만든 사전은 0부터 연속 slot을 사용한다.
            Dim staleSlot As Integer = panelIndexes.Count
            While scales.Remove(staleSlot)
                staleSlot += 1
            End While
        End Sub

        Private Shared Sub CollectPanelIndexes(indicators As IReadOnlyList(Of IIndicator),
                                               panelIndexes As List(Of Integer))
            For indicatorIndex As Integer = 0 To indicators.Count - 1
                Dim panelIndex As Integer = indicators(indicatorIndex).PanelIndex
                If panelIndex <= 0 Then Continue For
                InsertPanelIndex(panelIndexes, panelIndex)
            Next
        End Sub

        Private Shared Sub InsertPanelIndex(panelIndexes As List(Of Integer),
                                            panelIndex As Integer)
            Dim insertAt As Integer = 0
            While insertAt < panelIndexes.Count AndAlso
                  panelIndexes(insertAt) < panelIndex
                insertAt += 1
            End While

            If insertAt < panelIndexes.Count AndAlso
               panelIndexes(insertAt) = panelIndex Then Return

            panelIndexes.Insert(insertAt, panelIndex)
        End Sub

        Private Shared Function CanonicalBaselineKey(key As String) As String
            If String.Equals(key, "Upper", StringComparison.OrdinalIgnoreCase) Then Return "UPPER"
            If String.Equals(key, "Lower", StringComparison.OrdinalIgnoreCase) Then Return "LOWER"
            If String.Equals(key, "Baseline", StringComparison.OrdinalIgnoreCase) Then Return "BASELINE"
            Return If(key, String.Empty).ToUpperInvariant()
        End Function
    End Class
End Namespace
