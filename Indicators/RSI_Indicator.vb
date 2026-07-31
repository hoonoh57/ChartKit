Imports ChartKit.Abstractions
Imports ChartKit.Models

Namespace Indicators
    '' RSI (Wilder smoothing) + Signal(RSI의 SMA).
    Public Class RSI_Indicator
        Inherits IncrementalIndicatorBase

        Private _period As Integer = 14
        Private _signalPeriod As Integer = 9
        Private _params As New Dictionary(Of String, Object) From {{"Period", 14}, {"SignalPeriod", 9}}

        Private _previousClose As Single
        Private _gainSum As Single
        Private _lossSum As Single
        Private _averageGain As Single
        Private _averageLoss As Single
        Private _diffCount As Integer
        Private _signalValues() As Single
        Private _signalHead As Integer
        Private _signalCount As Integer
        Private _signalSum As Double

        Private _savedPreviousClose As Single
        Private _savedGainSum As Single
        Private _savedLossSum As Single
        Private _savedAverageGain As Single
        Private _savedAverageLoss As Single
        Private _savedDiffCount As Integer
        Private _savedSignalHead As Integer
        Private _savedSignalCount As Integer
        Private _savedSignalSum As Double
        Private _savedSignalHeadValue As Single

        Public Sub New(Optional period As Integer = 14, Optional signalPeriod As Integer = 9)
            SetPeriods(period, signalPeriod)
        End Sub

        Public Overrides ReadOnly Property Name As String
            Get
                Return $"RSI_{_period}"
            End Get
        End Property

        Public Overrides ReadOnly Property DisplayName As String
            Get
                Return $"RSI({_period})"
            End Get
        End Property

        Public Overrides ReadOnly Property PanelIndex As Integer
            Get
                Return 1
            End Get
        End Property

        Public Overrides Property Parameters As Dictionary(Of String, Object)
            Get
                Return _params
            End Get
            Set(value As Dictionary(Of String, Object))
                _params = If(value, New Dictionary(Of String, Object))
                Dim period = If(_params.ContainsKey("Period"), Convert.ToInt32(_params("Period")), _period)
                Dim signal = If(_params.ContainsKey("SignalPeriod"), Convert.ToInt32(_params("SignalPeriod")), _signalPeriod)
                SetPeriods(period, signal)
            End Set
        End Property

        Private Sub SetPeriods(period As Integer, signalPeriod As Integer)
            _period = Math.Max(1, period)
            _signalPeriod = Math.Max(1, signalPeriod)
            _params("Period") = _period
            _params("SignalPeriod") = _signalPeriod
            _signalValues = New Single(_signalPeriod - 1) {}
            ResetState()
        End Sub

        Public Overrides Function Calculate(candles As IReadOnlyList(Of CandleItem)) As List(Of IndicatorResult)
            Dim results As New List(Of IndicatorResult)(candles.Count)
            ResetState()
            If candles.Count = 0 Then Return results

            For i = 0 To candles.Count - 1
                If i = candles.Count - 1 Then SaveState()
                results.Add(StepCandle(candles(i), i))
            Next
            Dim ring = TryCast(candles, CandleRingBuffer)
            InitializeIncrementalSequence(If(ring Is Nothing, candles.Count - 1L, ring.LastSequence))
            Return results
        End Function

        Protected Overrides Function StepCandle(candle As CandleItem, index As Integer) As IndicatorResult
            Dim rsi = Single.NaN
            If index = 0 Then
                _previousClose = candle.Close
            Else
                Dim difference = candle.Close - _previousClose
                Dim gain = Math.Max(difference, 0.0F)
                Dim loss = Math.Max(-difference, 0.0F)
                _diffCount += 1

                If _diffCount <= _period Then
                    _gainSum += gain
                    _lossSum += loss
                    If _diffCount = _period Then
                        _averageGain = _gainSum / _period
                        _averageLoss = _lossSum / _period
                        rsi = CalcRsi(_averageGain, _averageLoss)
                    End If
                Else
                    _averageGain = (_averageGain * (_period - 1) + gain) / _period
                    _averageLoss = (_averageLoss * (_period - 1) + loss) / _period
                    rsi = CalcRsi(_averageGain, _averageLoss)
                End If
                _previousClose = candle.Close
            End If

            Dim signal = PushSignal(rsi)
            Return MakeResult(index, rsi, signal)
        End Function

        Protected Overrides Sub SaveState()
            _savedPreviousClose = _previousClose
            _savedGainSum = _gainSum
            _savedLossSum = _lossSum
            _savedAverageGain = _averageGain
            _savedAverageLoss = _averageLoss
            _savedDiffCount = _diffCount
            _savedSignalHead = _signalHead
            _savedSignalCount = _signalCount
            _savedSignalSum = _signalSum
            If _signalCount = _signalPeriod Then _savedSignalHeadValue = _signalValues(_signalHead)
        End Sub

        Protected Overrides Sub RestoreState()
            _previousClose = _savedPreviousClose
            _gainSum = _savedGainSum
            _lossSum = _savedLossSum
            _averageGain = _savedAverageGain
            _averageLoss = _savedAverageLoss
            _diffCount = _savedDiffCount
            _signalHead = _savedSignalHead
            _signalCount = _savedSignalCount
            _signalSum = _savedSignalSum
            If _signalCount = _signalPeriod Then _signalValues(_signalHead) = _savedSignalHeadValue
        End Sub

        Private Function PushSignal(value As Single) As Single
            If Single.IsNaN(value) Then Return Single.NaN

            If _signalCount < _signalPeriod Then
                Dim slot = (_signalHead + _signalCount) Mod _signalPeriod
                _signalValues(slot) = value
                _signalCount += 1
                _signalSum += value
            Else
                _signalSum -= _signalValues(_signalHead)
                _signalValues(_signalHead) = value
                _signalSum += value
                _signalHead = (_signalHead + 1) Mod _signalPeriod
            End If
            Return If(_signalCount = _signalPeriod, CSng(_signalSum / _signalPeriod), Single.NaN)
        End Function

        Private Sub ResetState()
            _previousClose = 0
            _gainSum = 0
            _lossSum = 0
            _averageGain = 0
            _averageLoss = 0
            _diffCount = 0
            _signalHead = 0
            _signalCount = 0
            _signalSum = 0
            Array.Clear(_signalValues, 0, _signalValues.Length)
            ResetIncrementalIndex()
        End Sub

        Private Function MakeResult(index As Integer, rsi As Single, signal As Single) As IndicatorResult
            Return New IndicatorResult With {
                .Name = Name,
                .Index = index,
                .PanelIndex = PanelIndex,
                .Values = New Dictionary(Of String, Single) From {
                    {"RSI", rsi}, {"Signal", signal}, {"Upper", 70.0F}, {"Lower", 30.0F}
                },
                .SeriesKinds = New Dictionary(Of String, SeriesKind) From {
                    {"RSI", SeriesKind.Line}, {"Signal", SeriesKind.Line},
                    {"Upper", SeriesKind.Baseline}, {"Lower", SeriesKind.Baseline}
                }
            }
        End Function

        Private Shared Function CalcRsi(averageGain As Single, averageLoss As Single) As Single
            If averageLoss = 0 Then Return 100.0F
            Return 100.0F - 100.0F / (1.0F + averageGain / averageLoss)
        End Function
    End Class
End Namespace
