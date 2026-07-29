Imports ChartKit.Abstractions
Imports ChartKit.Models

Namespace Indicators
    '' 이동평균 (SMA/EMA/WMA). 원본 MA_Indicator 그대로.
    Public Class MA_Indicator
        Implements IIndicator

        Private _period As Integer = 20
        Private _maType As String = "SMA"
        Private _params As New Dictionary(Of String, Object) From {{"Period", 20}, {"Type", "SMA"}}

        Public Sub New(Optional period As Integer = 20, Optional maType As String = "SMA")
            _period = period
            _maType = maType.ToUpper()
            _params("Period") = _period
            _params("Type") = _maType
        End Sub

        Public ReadOnly Property Name As String Implements IIndicator.Name
            Get
                Return $"{_maType}_{_period}"
            End Get
        End Property
        Public ReadOnly Property DisplayName As String Implements IIndicator.DisplayName
            Get
                Return $"{_maType}({_period})"
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
                If _params.ContainsKey("Period") Then _period = CInt(_params("Period"))
                If _params.ContainsKey("Type") Then _maType = _params("Type").ToString().ToUpper()
            End Set
        End Property

        Public Function Calculate(candles As IReadOnlyList(Of CandleItem)) As List(Of IndicatorResult) Implements IIndicator.Calculate
            Dim count = candles.Count
            Dim results As New List(Of IndicatorResult)(count)
            Dim vals(count - 1) As Single
            Select Case _maType
                Case "EMA"
                    Dim k As Single = 2.0F / (_period + 1)
                    For i = 0 To count - 1
                        If i < _period - 1 Then
                            vals(i) = Single.NaN
                        ElseIf i = _period - 1 Then
                            Dim s As Single = 0
                            For j = 0 To _period - 1
                                s += candles(j).Close
                            Next
                            vals(i) = s / _period
                        Else
                            vals(i) = candles(i).Close * k + vals(i - 1) * (1 - k)
                        End If
                    Next
                Case "WMA"
                    Dim denom = _period * (_period + 1) / 2.0F
                    For i = 0 To count - 1
                        If i < _period - 1 Then
                            vals(i) = Single.NaN
                        Else
                            Dim s As Single = 0
                            For j = 0 To _period - 1
                                s += candles(i - _period + 1 + j).Close * (j + 1)
                            Next
                            vals(i) = s / denom
                        End If
                    Next
                Case Else
                    Dim runSum As Single = 0
                    For i = 0 To count - 1
                        runSum += candles(i).Close
                        If i >= _period Then runSum -= candles(i - _period).Close
                        If i >= _period - 1 Then
                            vals(i) = runSum / _period
                        Else
                            vals(i) = Single.NaN
                        End If
                    Next
            End Select
            For i = 0 To count - 1
                Dim r As New IndicatorResult With {.Name = Name, .Index = i, .PanelIndex = 0,
                    .Values = New Dictionary(Of String, Single)}
                r.Values("Value") = vals(i)
                results.Add(r)
            Next
            Return results
        End Function

        Public Function UpdateLast(candles As IReadOnlyList(Of CandleItem), prevResults As List(Of IndicatorResult)) As IndicatorResult Implements IIndicator.UpdateLast
            Dim i = candles.Count - 1
            Dim r As New IndicatorResult With {.Name = Name, .Index = i, .PanelIndex = 0,
                .Values = New Dictionary(Of String, Single)}
            If i < _period - 1 Then
                r.Values("Value") = Single.NaN
                Return r
            End If

            Select Case _maType
                Case "EMA"
                    If i = _period - 1 Then
                        Dim seed As Single = 0
                        For j = 0 To _period - 1 : seed += candles(j).Close : Next
                        r.Values("Value") = seed / _period
                    ElseIf prevResults IsNot Nothing AndAlso prevResults.Count >= i Then
                        Dim prev = prevResults(i - 1).Values("Value")
                        Dim k As Single = 2.0F / (_period + 1)
                        r.Values("Value") = candles(i).Close * k + prev * (1 - k)
                    Else
                        r.Values("Value") = Single.NaN
                    End If
                Case "WMA"
                    Dim weighted As Single = 0
                    Dim denom = _period * (_period + 1) / 2.0F
                    For j = 0 To _period - 1
                        weighted += candles(i - _period + 1 + j).Close * (j + 1)
                    Next
                    r.Values("Value") = weighted / denom
                Case Else
                    Dim sum As Single = 0
                    For j = i - _period + 1 To i : sum += candles(j).Close : Next
                    r.Values("Value") = sum / _period
            End Select
            Return r
        End Function
    End Class
End Namespace
