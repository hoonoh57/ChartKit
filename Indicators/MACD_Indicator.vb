Imports ChartKit.Abstractions
Imports ChartKit.Models

Namespace Indicators
    '' MACD = EMA(fast) - EMA(slow), Signal = EMA(signal) of MACD, Hist = MACD - Signal.
    ''  Values: "MACD"(주선), "Signal"(시그널), "Hist"(히스토그램=MACD-Signal)
    Public Class MACD_Indicator
        Implements IIndicator

        Private _fast As Integer = 12
        Private _slow As Integer = 26
        Private _signal As Integer = 9
        Private _params As New Dictionary(Of String, Object) From {{"Fast", 12}, {"Slow", 26}, {"Signal", 9}}

        Public Sub New(Optional fast As Integer = 12, Optional slow As Integer = 26, Optional signal As Integer = 9)
            _fast = fast
            _slow = slow
            _signal = signal
            _params("Fast") = _fast
            _params("Slow") = _slow
            _params("Signal") = _signal
        End Sub

        Public ReadOnly Property Name As String Implements IIndicator.Name
            Get
                Return $"MACD_{_fast}_{_slow}_{_signal}"
            End Get
        End Property
        Public ReadOnly Property DisplayName As String Implements IIndicator.DisplayName
            Get
                Return $"MACD({_fast},{_slow},{_signal})"
            End Get
        End Property
        Public ReadOnly Property PanelIndex As Integer Implements IIndicator.PanelIndex
            Get
                Return 7
            End Get
        End Property
        Public Property Parameters As Dictionary(Of String, Object) Implements IIndicator.Parameters
            Get
                Return _params
            End Get
            Set(value As Dictionary(Of String, Object))
                _params = value
                If _params.ContainsKey("Fast") Then _fast = CInt(_params("Fast"))
                If _params.ContainsKey("Slow") Then _slow = CInt(_params("Slow"))
                If _params.ContainsKey("Signal") Then _signal = CInt(_params("Signal"))
            End Set
        End Property

        '' 종가 EMA. i < period-1 은 NaN, i = period-1 은 SMA seed, 이후 EMA.
        Private Shared Sub CloseEma(candles As IReadOnlyList(Of CandleItem), period As Integer, dst() As Single, count As Integer)
            Dim k As Single = 2.0F / (period + 1)
            For i = 0 To count - 1
                If i < period - 1 Then
                    dst(i) = Single.NaN
                ElseIf i = period - 1 Then
                    Dim s As Single = 0
                    For j = 0 To period - 1
                        s += candles(j).Close
                    Next
                    dst(i) = s / period
                Else
                    dst(i) = candles(i).Close * k + dst(i - 1) * (1 - k)
                End If
            Next
        End Sub

        '' 배열 src(유효값은 NaN 아님)에 대한 EMA. 유효값 period개째에 SMA seed.
        Private Shared Sub SeriesEma(src() As Single, period As Integer, dst() As Single, count As Integer)
            Dim k As Single = 2.0F / (period + 1)
            Dim seeded As Boolean = False
            Dim seedBuf As New List(Of Single)()
            For i = 0 To count - 1
                dst(i) = Single.NaN
                Dim v = src(i)
                If Single.IsNaN(v) Then Continue For
                If Not seeded Then
                    seedBuf.Add(v)
                    If seedBuf.Count = period Then
                        Dim s As Single = 0
                        For Each x In seedBuf : s += x : Next
                        dst(i) = s / period
                        seeded = True
                    End If
                Else
                    dst(i) = v * k + dst(i - 1) * (1 - k)
                End If
            Next
        End Sub

        Public Function Calculate(candles As IReadOnlyList(Of CandleItem)) As List(Of IndicatorResult) Implements IIndicator.Calculate
            Dim count = candles.Count
            Dim results As New List(Of IndicatorResult)(count)
            If count = 0 Then Return results

            Dim emaFast(count - 1) As Single
            Dim emaSlow(count - 1) As Single
            CloseEma(candles, _fast, emaFast, count)
            CloseEma(candles, _slow, emaSlow, count)

            Dim macd(count - 1) As Single
            For i = 0 To count - 1
                If Single.IsNaN(emaFast(i)) OrElse Single.IsNaN(emaSlow(i)) Then
                    macd(i) = Single.NaN
                Else
                    macd(i) = emaFast(i) - emaSlow(i)
                End If
            Next

            Dim signal(count - 1) As Single
            SeriesEma(macd, _signal, signal, count)

            For i = 0 To count - 1
                Dim r As New IndicatorResult With {.Name = Name, .Index = i, .PanelIndex = PanelIndex,
                    .Values = New Dictionary(Of String, Single)}
                r.Values("MACD") = macd(i)
                r.Values("Signal") = signal(i)
                If Single.IsNaN(macd(i)) OrElse Single.IsNaN(signal(i)) Then
                    r.Values("Hist") = Single.NaN
                Else
                    r.Values("Hist") = macd(i) - signal(i)
                End If
                results.Add(r)
            Next
            Return results
        End Function

        Public Function UpdateLast(candles As IReadOnlyList(Of CandleItem), prevResults As List(Of IndicatorResult)) As IndicatorResult Implements IIndicator.UpdateLast
            Dim full = Calculate(candles)
            If full.Count > 0 Then Return full(full.Count - 1)
            Dim r As New IndicatorResult With {.Name = Name, .Index = candles.Count - 1, .PanelIndex = PanelIndex,
                .Values = New Dictionary(Of String, Single)}
            r.Values("MACD") = Single.NaN
            r.Values("Signal") = Single.NaN
            r.Values("Hist") = Single.NaN
            Return r
        End Function
    End Class
End Namespace
