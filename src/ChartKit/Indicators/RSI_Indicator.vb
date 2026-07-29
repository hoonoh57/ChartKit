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

        '' 증분 상태: 확정된 마지막 봉 기준
        Private _stateIndex As Integer = -1
        Private _ag As Single = 0
        Private _al As Single = 0
        Private _sigQ As New Queue(Of Single)()
        Private _sigSum As Single = 0
        '' 미확정봉 재갱신을 위한 직전 확정 스냅샷
        Private _pAg As Single = 0
        Private _pAl As Single = 0
        Private _pSigQ As Single() = New Single() {}
        Private _pSigSum As Single = 0

        Public Function UpdateLast(candles As IReadOnlyList(Of CandleItem), prevResults As List(Of IndicatorResult)) As IndicatorResult Implements IIndicator.UpdateLast
            Dim i = candles.Count - 1
            If i < 0 Then Return NaNResult(0)

            If _stateIndex = i - 1 Then
                '' 새 봉 확정: 현재 상태를 스냅샷으로 남기고 한 스텝 전진
                _pAg = _ag : _pAl = _al
                _pSigQ = _sigQ.ToArray() : _pSigSum = _sigSum
            ElseIf _stateIndex = i Then
                '' 같은 봉 재갱신: 스냅샷에서 되돌린 뒤 다시 한 스텝
                _ag = _pAg : _al = _pAl
                _sigQ = New Queue(Of Single)(_pSigQ) : _sigSum = _pSigSum
            Else
                '' 상태 불일치(점프/최초): 전체 재계산으로 상태를 재구축
                Return RebuildAndReturnLast(candles)
            End If

            Return StepOne(candles, i)
        End Function

        '' 상태(_ag,_al,_sigQ)를 i 봉으로 한 스텝 전진시키고 결과 반환
        Private Function StepOne(candles As IReadOnlyList(Of CandleItem), i As Integer) As IndicatorResult
            Dim rsi As Single = Single.NaN
            If i < _period Then
                rsi = Single.NaN
            ElseIf i = _period Then
                Dim sumG As Single = 0, sumL As Single = 0
                For j = 1 To _period
                    Dim d = candles(j).Close - candles(j - 1).Close
                    If d > 0 Then sumG += d Else sumL += Math.Abs(d)
                Next
                _ag = sumG / _period
                _al = sumL / _period
                rsi = CalcRSI(_ag, _al)
            Else
                Dim diff = candles(i).Close - candles(i - 1).Close
                Dim g As Single = 0, l As Single = 0
                If diff > 0 Then g = diff Else l = Math.Abs(diff)
                _ag = (_ag * (_period - 1) + g) / _period
                _al = (_al * (_period - 1) + l) / _period
                rsi = CalcRSI(_ag, _al)
            End If

            Dim sig As Single = Single.NaN
            If Not Single.IsNaN(rsi) Then
                _sigQ.Enqueue(rsi)
                _sigSum += rsi
                If _sigQ.Count > _signalPeriod Then _sigSum -= _sigQ.Dequeue()
                If _sigQ.Count = _signalPeriod Then sig = _sigSum / _signalPeriod
            End If

            _stateIndex = i

            Dim r As New IndicatorResult With {.Name = Name, .Index = i, .PanelIndex = PanelIndex,
                .Values = New Dictionary(Of String, Single)}
            r.Values("RSI") = rsi
            r.Values("Signal") = sig
            r.Values("Upper") = 70
            r.Values("Lower") = 30
            Return r
        End Function

        '' 상태를 처음부터 다시 쌓는다. 결과는 Calculate 와 동일해야 한다.
        Private Function RebuildAndReturnLast(candles As IReadOnlyList(Of CandleItem)) As IndicatorResult
            _ag = 0 : _al = 0
            _sigQ = New Queue(Of Single)() : _sigSum = 0
            _stateIndex = -1
            Dim last As IndicatorResult = Nothing
            For k = 0 To candles.Count - 1
                If k = candles.Count - 1 Then
                    _pAg = _ag : _pAl = _al
                    _pSigQ = _sigQ.ToArray() : _pSigSum = _sigSum
                End If
                last = StepOne(candles, k)
            Next
            If last Is Nothing Then Return NaNResult(0)
            Return last
        End Function

        Private Function NaNResult(idx As Integer) As IndicatorResult
            Dim r As New IndicatorResult With {.Name = Name, .Index = idx, .PanelIndex = PanelIndex,
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
