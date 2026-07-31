Option Strict On
Option Explicit On
Option Infer Off

Imports System.Collections.Generic

Namespace Core
    Friend NotInheritable Class PanelLayoutPlan
        Public Sub New(mainHeight As Single,
                       volumeHeight As Single,
                       panelHeights As IReadOnlyList(Of Single))
            Me.MainHeight = mainHeight
            Me.VolumeHeight = volumeHeight
            Me.PanelHeights = New List(Of Single)(panelHeights)
        End Sub

        Public ReadOnly Property MainHeight As Single
        Public ReadOnly Property VolumeHeight As Single
        Public ReadOnly Property PanelHeights As IReadOnlyList(Of Single)
    End Class

    Friend NotInheritable Class PanelLayoutCalculator
        Private Sub New()
        End Sub

        Public Shared Function Calculate(totalHeight As Single,
                                         volumeRatio As Single,
                                         panelRatios As IReadOnlyList(Of Single),
                                         volumeVisible As Boolean,
                                         panelsVisible As Boolean,
                                         Optional minimumMainRatio As Single = 0.25F) As PanelLayoutPlan
            Dim safeTotalHeight As Single = Math.Max(0.0F, totalHeight)
            Dim safeMinimumMainRatio As Single = Math.Min(1.0F, Math.Max(0.0F, minimumMainRatio))
            Dim volumeHeight As Single = 0.0F

            If volumeVisible Then
                volumeHeight = safeTotalHeight * Math.Max(0.0F, volumeRatio)
            End If

            Dim panelHeights As New List(Of Single)()
            Dim panelTotal As Single = 0.0F

            If panelsVisible AndAlso panelRatios IsNot Nothing Then
                For index As Integer = 0 To panelRatios.Count - 1
                    Dim panelHeight As Single =
                        safeTotalHeight * Math.Max(0.0F, panelRatios(index))
                    panelHeights.Add(panelHeight)
                    panelTotal += panelHeight
                Next
            End If

            Dim mainHeight As Single = safeTotalHeight - volumeHeight - panelTotal
            Dim minimumMainHeight As Single = safeTotalHeight * safeMinimumMainRatio

            If mainHeight < minimumMainHeight Then
                Dim excessHeight As Single = minimumMainHeight - mainHeight
                Dim shrinkableHeight As Single = volumeHeight + panelTotal

                If shrinkableHeight > 0.0F Then
                    Dim scale As Single = Math.Max(
                        0.0F,
                        (shrinkableHeight - excessHeight) / shrinkableHeight)

                    volumeHeight *= scale
                    panelTotal = 0.0F

                    For index As Integer = 0 To panelHeights.Count - 1
                        panelHeights(index) *= scale
                        panelTotal += panelHeights(index)
                    Next
                End If

                mainHeight = safeTotalHeight - volumeHeight - panelTotal
            End If

            Return New PanelLayoutPlan(
                mainHeight,
                volumeHeight,
                panelHeights)
        End Function
    End Class
End Namespace
