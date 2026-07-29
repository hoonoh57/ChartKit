Imports ChartKit.Abstractions
Imports ChartKit.Models

Namespace Indicators
    '' VWAP_Indicator — Volume Weighted Average Price (당일 리셋). 원본 로직 그대로.
    Public Class VWAP_Indicator
        Implements IIndicator

        Private _stdDev1 As Single = 1.0F
        Private _stdDev2 As Single = 2.0F
        Private _params As New Dictionary(Of String, Object) From {{"StdDev1", 1.0F}, {"StdDev2", 2.0F}}

        Public Sub New(Optional stdDev1 As Single = 1.0F, Optional stdDev2 As Single = 2.0F)
            _stdDev1 = stdDev1
            _stdDev2 = stdDev2
            _params("StdDev1") = _stdDev1
            _params("StdDev2") = _stdDev2
        End Sub

        Public ReadOnly Property Name As String Implements IIndicator.Name
            Get
                Return "VWAP"
            End Get
        End Property
        Public ReadOnly Property DisplayName As String Implements IIndicator.DisplayName
            Get
                Return "VWAP"
            End Get
        End Property
        Public ReadOnly Property PanelIndex As Integer Implements IIndicator.PanelIndex
            Get
                Return 0
            End Get
        End Property
        Public Property Parameters As Dictionary(Of String, Object) Implements IIndicator.Parameters
            Get
                Return _params
            End Get
            Set(value As Dictionary(Of String, Object))
                _params = value
                If _params.ContainsKey("StdDev1") Then _stdDev1 = CSng(_params("StdDev1"))
                If _params.ContainsKey("StdDev2") Then _stdDev2 = CSng(_params("StdDev2"))
            End Set
        End Property

        Public Function Calculate(candles As IReadOnlyList(Of CandleItem)) As List(Of IndicatorResult) Implements IIndicator.Calculate
            Dim count = candles.Count
            Dim results As New List(Of IndicatorResult)(count)
            Dim cumPV As Double = 0
            Dim cumVol As Double = 0
            Dim cumPV2 As Double = 0
            Dim lastDate As DateTime = DateTime.MinValue
            For i = 0 To count - 1
                Dim c = candles(i)
                If c.Dt.Date <> lastDate.Date AndAlso lastDate <> DateTime.MinValue Then
                    cumPV = 0
                    cumVol = 0
                    cumPV2 = 0
                End If
                lastDate = c.Dt
                Dim tp = (c.High + c.Low + c.Close) / 3.0F
                cumPV += tp * c.Volume
                cumVol += c.Volume
                cumPV2 += tp * tp * c.Volume
                Dim r As New IndicatorResult With {.Name = Name, .Index = i, .PanelIndex = 0,
                    .Values = New Dictionary(Of String, Single)}
                If cumVol > 0 Then
                    Dim vwap = CSng(cumPV / cumVol)
                    Dim variance = CSng(cumPV2 / cumVol - vwap * vwap)
                    Dim sd = CSng(Math.Sqrt(Math.Max(0, variance)))
                    r.Values("Value") = vwap
                    r.Values("Upper1") = vwap + _stdDev1 * sd
                    r.Values("Lower1") = vwap - _stdDev1 * sd
                    r.Values("Upper2") = vwap + _stdDev2 * sd
                    r.Values("Lower2") = vwap - _stdDev2 * sd
                Else
                    r.Values("Value") = Single.NaN
                    r.Values("Upper1") = Single.NaN
                    r.Values("Lower1") = Single.NaN
                    r.Values("Upper2") = Single.NaN
                    r.Values("Lower2") = Single.NaN
                End If
                results.Add(r)
            Next
            Return results
        End Function

        Public Function UpdateLast(candles As IReadOnlyList(Of CandleItem), prevResults As List(Of IndicatorResult)) As IndicatorResult Implements IIndicator.UpdateLast
            Dim full = Calculate(candles)
            If full.Count > 0 Then Return full(full.Count - 1)
            Dim emptyR As New IndicatorResult With {.Name = Name, .Index = candles.Count - 1, .PanelIndex = 0}
            emptyR.Values = New Dictionary(Of String, Single)
            emptyR.Values("Value") = Single.NaN
            Return emptyR
        End Function
    End Class
End Namespace
