Imports ChartKit.Abstractions
Imports ChartKit.Models

Namespace Indicators
    '' RSI (Wilder smoothing) + Signal(RSI의 SMA). 단일선+시그널 패턴.
    ''  Values: "RSI"(주선), "Signal"(RSI SMA), "Upper"=70, "Lower"=30
    Public Class RSI_Indicator
        Implements IIndicator

        Private _period As Integer = 14
        Private _signalPeriod As Integer = 9
        Private _params As New Dictionary(Of String, Object) From {{"Period", 14}, {"SignalPeriod", 9}}

        Public Sub New(Optional period As Integer = 14, Optional signalPeriod As Integer = 9)
            _period = period
            _signalPeriod = signalPeriod
            _params("Period") = _period
            _params("SignalPeriod") = _signalPeriod
        End Sub

        Public ReadOnly Property Name As String Implements IIndicator.Name
            Get
                Return $"RSI_{_period}"
            End Get
        End Property
        Public ReadOnly Property DisplayName As String Implements IIndicator.DisplayName
            Get
                Return $"RSI({_period})"
            End Get
        End Property
        Public ReadOnly Property PanelIndex As Integer Implements IIndicator.PanelIndex
            Get
                Return 1
            End Get
        End Property
        Public Property Parameters As Dictionary(Of String, Object) Implements IIndicator.Parameters
            Get
                Return _params
            End Get
            Set(value As Dictionary(Of String, Object))
                _params = value
                If _params.ContainsKey("Period") Then _period = CInt(_params("Period"))
                If _params.ContainsKey("SignalPeriod") Then _signalPeriod = CInt(_params("SignalPeriod"))
            End Set
        End Property

        Public Function Calculate(candles As IReadOnlyList(Of CandleItem)) As List(Of IndicatorResult) Implements IIndicator.Calculate
            Dim count = candles.Count
            Dim results As New List(Of IndicatorResult)(count)
            If count = 0 Then Return results

            Dim gains(count - 1) As Single
            Dim losses(count - 1) As Single
            For i = 1 To count - 1
                Dim diff = candles(i).Close - candles(i - 1).Close
                If diff > 0 Then gains(i) = diff Else losses(i) = Math.Abs(diff)
            Next

            '' RSI 값 배열 먼저 계산
            Dim rsiVals(count - 1) As Single
            Dim ag As Single = 0, al As Single = 0
            For i = 0 To count - 1
                If i < _period Then
                    rsiVals(i) = Single.NaN
                ElseIf i = _period Then
                    Dim sumG As Single = 0, sumL As Single = 0
                    For j = 1 To _period : sumG += gains(j) : sumL += losses(j) : Next
                    ag = sumG / _period
                    al = sumL / _period
                    rsiVals(i) = CalcRSI(ag, al)
                Else
                    ag = (ag * (_period - 1) + gains(i)) / _period
                    al = (al * (_period - 1) + losses(i)) / _period
                    rsiVals(i) = CalcRSI(ag, al)
                End If
            Next

            '' Signal = RSI 값의 SMA(_signalPeriod)
            Dim sig(count - 1) As Single
            ComputeSma(rsiVals, sig, _signalPeriod, count)

            For i = 0 To count - 1
                Dim r As New IndicatorResult With {.Name = Name, .Index = i, .PanelIndex = PanelIndex,
                    .Values = New Dictionary(Of String, Single)}
                r.Values("RSI") = rsiVals(i)
                r.Values("Signal") = sig(i)
                r.Values("Upper") = 70
                r.Values("Lower") = 30
                results.Add(r)
            Next
            Return results
        End Function

        Public Function UpdateLast(candles As IReadOnlyList(Of CandleItem), prevResults As List(Of IndicatorResult)) As IndicatorResult Implements IIndicator.UpdateLast
            '' 단순화: 전체 재계산 후 마지막 반환 (Signal SMA 정합성 우선)
            Dim full = Calculate(candles)
            If full.Count > 0 Then Return full(full.Count - 1)
            Dim r As New IndicatorResult With {.Name = Name, .Index = candles.Count - 1, .PanelIndex = PanelIndex,
                .Values = New Dictionary(Of String, Single)}
            r.Values("RSI") = Single.NaN
            r.Values("Signal") = Single.NaN
            r.Values("Upper") = 70
            r.Values("Lower") = 30
            Return r
        End Function

        '' NaN을 건너뛰고 유효값이 period개 모이면 SMA 산출
        Private Shared Sub ComputeSma(src() As Single, dst() As Single, period As Integer, count As Integer)
            Dim q As New Queue(Of Single)()
            Dim sum As Single = 0
            For i = 0 To count - 1
                Dim v = src(i)
                If Single.IsNaN(v) Then
                    dst(i) = Single.NaN
                    Continue For
                End If
                q.Enqueue(v)
                sum += v
                If q.Count > period Then sum -= q.Dequeue()
                If q.Count = period Then dst(i) = sum / period Else dst(i) = Single.NaN
            Next
        End Sub

        Private Function CalcRSI(ag As Single, al As Single) As Single
            If al = 0 Then Return 100.0F
            Return 100.0F - 100.0F / (1.0F + ag / al)
        End Function
    End Class
End Namespace
