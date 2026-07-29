Imports ChartKit.Abstractions
Imports ChartKit.Models

Namespace Indicators
    '' Disparity_Indicator — 이격도 (Close / MA * 100). 서브패널(PanelIndex=6). 원본 로직 그대로.
    Public Class Disparity_Indicator
        Implements IIndicator

        Private _period As Integer = 20
        Private _params As New Dictionary(Of String, Object) From {{"Period", 20}}

        Public Sub New(Optional period As Integer = 20)
            _period = period
            _params("Period") = _period
        End Sub

        Public ReadOnly Property Name As String Implements IIndicator.Name
            Get
                Return $"DISP_{_period}"
            End Get
        End Property
        Public ReadOnly Property DisplayName As String Implements IIndicator.DisplayName
            Get
                Return $"이격도({_period})"
            End Get
        End Property
        Public ReadOnly Property PanelIndex As Integer Implements IIndicator.PanelIndex
            Get
                Return 6
            End Get
        End Property
        Public Property Parameters As Dictionary(Of String, Object) Implements IIndicator.Parameters
            Get
                Return _params
            End Get
            Set(value As Dictionary(Of String, Object))
                _params = value
                If _params.ContainsKey("Period") Then _period = CInt(_params("Period"))
            End Set
        End Property

        Public Function Calculate(candles As IReadOnlyList(Of CandleItem)) As List(Of IndicatorResult) Implements IIndicator.Calculate
            Dim count = candles.Count
            Dim results As New List(Of IndicatorResult)(count)
            Dim maSum As Single = 0
            For i = 0 To count - 1
                maSum += candles(i).Close
                If i >= _period Then maSum -= candles(i - _period).Close
                Dim r As New IndicatorResult With {.Name = Name, .Index = i, .PanelIndex = PanelIndex,
                    .Values = New Dictionary(Of String, Single)}
                If i < _period - 1 Then
                    r.Values("Value") = Single.NaN
                    r.Values("MA") = Single.NaN
                Else
                    Dim ma = maSum / _period
                    If ma > 0 Then
                        r.Values("Value") = (candles(i).Close / ma) * 100.0F
                    Else
                        r.Values("Value") = 100.0F
                    End If
                    r.Values("MA") = ma
                    r.Values("Upper") = 105
                    r.Values("Baseline") = 100
                    r.Values("Lower") = 95
                End If
                results.Add(r)
            Next
            Return results
        End Function

        Public Function UpdateLast(candles As IReadOnlyList(Of CandleItem), prevResults As List(Of IndicatorResult)) As IndicatorResult Implements IIndicator.UpdateLast
            Dim i = candles.Count - 1
            Dim r As New IndicatorResult With {.Name = Name, .Index = i, .PanelIndex = PanelIndex,
                .Values = New Dictionary(Of String, Single)}
            If i < _period - 1 Then
                r.Values("Value") = Single.NaN
                r.Values("MA") = Single.NaN
                Return r
            End If
            Dim sum As Single = 0
            For j = i - _period + 1 To i
                sum += candles(j).Close
            Next
            Dim ma = sum / _period
            If ma > 0 Then
                r.Values("Value") = candles(i).Close / ma * 100.0F
            Else
                r.Values("Value") = 100.0F
            End If
            r.Values("MA") = ma
            r.Values("Upper") = 105
            r.Values("Baseline") = 100
            r.Values("Lower") = 95
            Return r
        End Function
    End Class
End Namespace
