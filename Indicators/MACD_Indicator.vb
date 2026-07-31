Imports ChartKit.Abstractions
Imports ChartKit.Models

Namespace Indicators
    '' MACD = EMA(fast) - EMA(slow), Signal = EMA(signal) of MACD.
    Public Class MACD_Indicator
        Inherits IncrementalIndicatorBase

        Private _fast As Integer = 12
        Private _slow As Integer = 26
        Private _signal As Integer = 9
        Private _params As New Dictionary(Of String, Object) From {{"Fast", 12}, {"Slow", 26}, {"Signal", 9}}

        Private _closeCount As Integer
        Private _fastSeedSum As Double
        Private _slowSeedSum As Double
        Private _fastEma As Single = Single.NaN
        Private _slowEma As Single = Single.NaN
        Private _signalCount As Integer
        Private _signalSeedSum As Double
        Private _signalEma As Single = Single.NaN

        Private _savedCloseCount As Integer
        Private _savedFastSeedSum As Double
        Private _savedSlowSeedSum As Double
        Private _savedFastEma As Single
        Private _savedSlowEma As Single
        Private _savedSignalCount As Integer
        Private _savedSignalSeedSum As Double
        Private _savedSignalEma As Single

        Public Sub New(Optional fast As Integer = 12, Optional slow As Integer = 26, Optional signal As Integer = 9)
            SetPeriods(fast, slow, signal)
        End Sub

        Public Overrides ReadOnly Property Name As String
            Get
                Return $"MACD_{_fast}_{_slow}_{_signal}"
            End Get
        End Property

        Public Overrides ReadOnly Property DisplayName As String
            Get
                Return $"MACD({_fast},{_slow},{_signal})"
            End Get
        End Property

        Public Overrides ReadOnly Property PanelIndex As Integer
            Get
                Return 7
            End Get
        End Property

        Public Overrides Property Parameters As Dictionary(Of String, Object)
            Get
                Return _params
            End Get
            Set(value As Dictionary(Of String, Object))
                _params = If(value, New Dictionary(Of String, Object))
                SetPeriods(
                    If(_params.ContainsKey("Fast"), Convert.ToInt32(_params("Fast")), _fast),
                    If(_params.ContainsKey("Slow"), Convert.ToInt32(_params("Slow")), _slow),
                    If(_params.ContainsKey("Signal"), Convert.ToInt32(_params("Signal")), _signal))
            End Set
        End Property

        Private Sub SetPeriods(fast As Integer, slow As Integer, signal As Integer)
            _fast = Math.Max(1, fast)
            _slow = Math.Max(_fast + 1, slow)
            _signal = Math.Max(1, signal)
            _params("Fast") = _fast
            _params("Slow") = _slow
            _params("Signal") = _signal
            ResetState()
        End Sub

        Public Overrides Function Calculate(candles As IReadOnlyList(Of CandleItem)) As List(Of IndicatorResult)
            Dim results As New List(Of IndicatorResult)(candles.Count)
            ResetState()
            For i = 0 To candles.Count - 1
                If i = candles.Count - 1 Then SaveState()
                results.Add(StepCandle(candles(i), i))
            Next
            Dim ring = TryCast(candles, CandleRingBuffer)
            InitializeIncrementalSequence(If(ring Is Nothing, candles.Count - 1L, ring.LastSequence))
            Return results
        End Function

        Protected Overrides Function StepCandle(candle As CandleItem, index As Integer) As IndicatorResult
            _closeCount += 1
            _fastEma = StepCloseEma(candle.Close, _fast, _closeCount, _fastSeedSum, _fastEma)
            _slowEma = StepCloseEma(candle.Close, _slow, _closeCount, _slowSeedSum, _slowEma)

            Dim macd = Single.NaN
            If Not Single.IsNaN(_fastEma) AndAlso Not Single.IsNaN(_slowEma) Then
                macd = _fastEma - _slowEma
            End If

            Dim signalValue = Single.NaN
            If Not Single.IsNaN(macd) Then
                _signalCount += 1
                If _signalCount < _signal Then
                    _signalSeedSum += macd
                ElseIf _signalCount = _signal Then
                    _signalSeedSum += macd
                    _signalEma = CSng(_signalSeedSum / _signal)
                    signalValue = _signalEma
                Else
                    Dim factor = 2.0F / (_signal + 1)
                    _signalEma = macd * factor + _signalEma * (1.0F - factor)
                    signalValue = _signalEma
                End If
            End If

            Dim histogram = If(Single.IsNaN(macd) OrElse Single.IsNaN(signalValue),
                               Single.NaN, macd - signalValue)
            Return New IndicatorResult With {
                .Name = Name, .Index = index, .PanelIndex = PanelIndex,
                .Values = New Dictionary(Of String, Single) From {
                    {"MACD", macd}, {"Signal", signalValue}, {"Hist", histogram}
                },
                .SeriesKinds = New Dictionary(Of String, SeriesKind) From {
                    {"MACD", SeriesKind.Line}, {"Signal", SeriesKind.Line},
                    {"Hist", SeriesKind.Histogram}
                }
            }
        End Function

        Private Shared Function StepCloseEma(close As Single, period As Integer, count As Integer,
                                             ByRef seedSum As Double, current As Single) As Single
            If count < period Then
                seedSum += close
                Return Single.NaN
            End If
            If count = period Then
                seedSum += close
                Return CSng(seedSum / period)
            End If
            Dim factor = 2.0F / (period + 1)
            Return close * factor + current * (1.0F - factor)
        End Function

        Protected Overrides Sub SaveState()
            _savedCloseCount = _closeCount
            _savedFastSeedSum = _fastSeedSum
            _savedSlowSeedSum = _slowSeedSum
            _savedFastEma = _fastEma
            _savedSlowEma = _slowEma
            _savedSignalCount = _signalCount
            _savedSignalSeedSum = _signalSeedSum
            _savedSignalEma = _signalEma
        End Sub

        Protected Overrides Sub RestoreState()
            _closeCount = _savedCloseCount
            _fastSeedSum = _savedFastSeedSum
            _slowSeedSum = _savedSlowSeedSum
            _fastEma = _savedFastEma
            _slowEma = _savedSlowEma
            _signalCount = _savedSignalCount
            _signalSeedSum = _savedSignalSeedSum
            _signalEma = _savedSignalEma
        End Sub

        Private Sub ResetState()
            _closeCount = 0
            _fastSeedSum = 0
            _slowSeedSum = 0
            _fastEma = Single.NaN
            _slowEma = Single.NaN
            _signalCount = 0
            _signalSeedSum = 0
            _signalEma = Single.NaN
            ResetIncrementalIndex()
        End Sub
    End Class
End Namespace
