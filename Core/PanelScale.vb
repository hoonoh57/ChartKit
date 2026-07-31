Imports ChartKit.Abstractions

Namespace Core
    Public NotInheritable Class PanelScale
        Public Property Minimum As Single
        Public Property Maximum As Single
        Public ReadOnly Property Baselines As New Dictionary(Of String, Single)
    End Class

    Public NotInheritable Class PanelScaleCalculator
        Private Sub New()
        End Sub

        Public Shared Function Calculate(ctx As ChartContext) As Dictionary(Of Integer, PanelScale)
            Dim scales As New Dictionary(Of Integer, PanelScale)
            If ctx Is Nothing OrElse ctx.Engine Is Nothing Then Return scales

            Dim indicators = ctx.Engine.GetAll()
            Dim panelIndexes = indicators.Where(Function(ind) ind.PanelIndex > 0).
                Select(Function(ind) ind.PanelIndex).Distinct().OrderBy(Function(x) x).ToList()
            Dim range = ctx.VisibleRange()

            For slot = 0 To panelIndexes.Count - 1
                Dim scale As New PanelScale With {
                    .Minimum = Single.MaxValue, .Maximum = Single.MinValue
                }
                Dim panelIndex = panelIndexes(slot)
                For Each indicator In indicators.Where(Function(ind) ind.PanelIndex = panelIndex)
                    Dim results As ChartKit.Models.IndicatorResultRingBuffer = Nothing
                    If Not ctx.Engine.Results.TryGetValue(indicator.Name, results) OrElse results Is Nothing Then Continue For
                    Dim last = Math.Min(range.Item2, results.Count - 1)
                    For i = range.Item1 To last
                        Dim result = results(i)
                        If result Is Nothing OrElse result.Values Is Nothing Then Continue For
                        For Each pair In result.Values
                            If Single.IsNaN(pair.Value) Then Continue For
                            Select Case result.KindOf(pair.Key)
                                Case SeriesKind.Line
                                    scale.Minimum = Math.Min(scale.Minimum, pair.Value)
                                    scale.Maximum = Math.Max(scale.Maximum, pair.Value)
                                Case SeriesKind.Baseline
                                    scale.Baselines(pair.Key.ToUpperInvariant()) = pair.Value
                            End Select
                        Next
                    Next
                Next

                For Each level In scale.Baselines.Values
                    scale.Minimum = Math.Min(scale.Minimum, level)
                    scale.Maximum = Math.Max(scale.Maximum, level)
                Next
                If scale.Minimum = Single.MaxValue OrElse scale.Maximum = Single.MinValue Then
                    scale.Minimum = 0
                    scale.Maximum = 1
                Else
                    If scale.Maximum <= scale.Minimum Then scale.Maximum = scale.Minimum + 1
                    Dim padding = (scale.Maximum - scale.Minimum) * 0.05F
                    scale.Minimum -= padding
                    scale.Maximum += padding
                End If
                scales(slot) = scale
            Next
            Return scales
        End Function
    End Class
End Namespace
