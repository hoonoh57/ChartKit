Imports ChartKit.Abstractions
Imports ChartKit.Models

Namespace Indicators
    Public Class JMA_Indicator
        Inherits IncrementalIndicatorBase

        Private _period, _phase, _power As Integer
        Private _params As Dictionary(Of String, Object)
        Private _e0, _e1, _e2, _lastJma, _warmSum As Double
        Private _direction, _count As Integer
        Private _initialized As Boolean

        Private _savedE0, _savedE1, _savedE2, _savedLastJma, _savedWarmSum As Double
        Private _savedDirection, _savedCount As Integer
        Private _savedInitialized As Boolean

        Public Sub New(Optional period As Integer = 14, Optional phase As Integer = 50, Optional power As Integer = 2)
            SetOptions(period, phase, power)
        End Sub
        Public Overrides ReadOnly Property Name As String
            Get
                Return $"JMA_{_period}"
            End Get
        End Property
        Public Overrides ReadOnly Property DisplayName As String
            Get
                Return $"JMA({_period},{_phase},{_power})"
            End Get
        End Property
        Public Overrides ReadOnly Property PanelIndex As Integer
            Get
                Return 0
            End Get
        End Property
        Public Overrides Property Parameters As Dictionary(Of String, Object)
            Get
                Return _params
            End Get
            Set(value As Dictionary(Of String, Object))
                Dim p = If(value, New Dictionary(Of String, Object))
                SetOptions(If(p.ContainsKey("Period"), Convert.ToInt32(p("Period")), _period),
                           If(p.ContainsKey("Phase"), Convert.ToInt32(p("Phase")), _phase),
                           If(p.ContainsKey("Power"), Convert.ToInt32(p("Power")), _power))
            End Set
        End Property
        Private Sub SetOptions(period As Integer, phase As Integer, power As Integer)
            _period = Math.Max(1, period)
            _phase = Math.Max(-100, Math.Min(100, phase))
            _power = Math.Max(1, power)
            _params = New Dictionary(Of String, Object) From {
                {"Period", _period}, {"Phase", _phase}, {"Power", _power}
            }
            ResetState()
        End Sub
        Public Overrides Function Calculate(candles As IReadOnlyList(Of CandleItem)) As List(Of IndicatorResult)
            Dim results As New List(Of IndicatorResult)(candles.Count)
            ResetState()
            For i = 0 To candles.Count - 1
                If i = candles.Count - 1 Then SaveState()
                results.Add(StepCandle(candles(i), i))
            Next
            If candles.Count > 0 Then
                Dim ring = TryCast(candles, CandleRingBuffer)
                InitializeIncrementalSequence(If(ring Is Nothing, candles.Count - 1L, ring.LastSequence))
            End If
            Return results
        End Function
        Protected Overrides Function StepCandle(candle As CandleItem, index As Integer) As IndicatorResult
            Dim src = CDbl(candle.Close)
            If Not _initialized Then
                _e0 = src : _e1 = 0 : _e2 = 0 : _lastJma = src
                _initialized = True
            End If
            Dim beta = 0.45R * (_period - 1) / (0.45R * (_period - 1) + 2.0R)
            Dim alpha = Math.Pow(beta, _power)
            _e0 = (1.0R - alpha) * src + alpha * _e0
            _e1 = (src - _e0) * (1.0R - beta) + beta * _e1
            _e2 = (_e0 + CalcPhaseRatio(_phase) * _e1 - _lastJma) *
                  Math.Pow(1.0R - alpha, 2) + Math.Pow(alpha, 2) * _e2

            _count += 1
            _warmSum += src
            Dim current = If(_count <= _period,
                             Math.Round(_warmSum / _count, 4),
                             Math.Round(_e2 + _lastJma, 4))
            Dim previous = _lastJma
            If current > previous Then
                _direction = 1
            ElseIf current < previous Then
                _direction = -1
            ElseIf _direction = 0 Then
                _direction = 1
            End If
            Dim slope = If(previous <> 0, CSng(Math.Round((current / previous - 1.0R) * 100.0R, 1)), 0.0F)
            _lastJma = current
            Return MakeResult(index, CSng(current), _direction, slope)
        End Function
        Protected Overrides Sub SaveState()
            _savedE0 = _e0 : _savedE1 = _e1 : _savedE2 = _e2
            _savedLastJma = _lastJma : _savedWarmSum = _warmSum
            _savedDirection = _direction : _savedCount = _count : _savedInitialized = _initialized
        End Sub
        Protected Overrides Sub RestoreState()
            _e0 = _savedE0 : _e1 = _savedE1 : _e2 = _savedE2
            _lastJma = _savedLastJma : _warmSum = _savedWarmSum
            _direction = _savedDirection : _count = _savedCount : _initialized = _savedInitialized
        End Sub
        Private Sub ResetState()
            _e0 = 0 : _e1 = 0 : _e2 = 0 : _lastJma = 0 : _warmSum = 0
            _direction = 0 : _count = 0 : _initialized = False
            ResetIncrementalIndex()
        End Sub
        Private Function MakeResult(index As Integer, value As Single, direction As Integer, slope As Single) As IndicatorResult
            Return New IndicatorResult With {
                .Name = Name, .Index = index, .PanelIndex = PanelIndex,
                .Values = New Dictionary(Of String, Single) From {
                    {"Value", value}, {"Up", If(direction = 1, value, Single.NaN)},
                    {"Down", If(direction = -1, value, Single.NaN)}, {"Slope", slope}
                },
                .SeriesKinds = New Dictionary(Of String, SeriesKind) From {
                    {"Value", SeriesKind.Line}, {"Up", SeriesKind.Line},
                    {"Down", SeriesKind.Line}, {"Slope", SeriesKind.Meta}
                }
            }
        End Function
        Private Shared Function CalcPhaseRatio(phase As Integer) As Double
            Return phase / 100.0R + 1.5R
        End Function
    End Class
End Namespace
