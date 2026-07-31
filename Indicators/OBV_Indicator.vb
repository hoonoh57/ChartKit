Imports ChartKit.Abstractions
Imports ChartKit.Models

Namespace Indicators
    Public Class OBV_Indicator
        Inherits IncrementalIndicatorBase

        Private _maPeriod As Integer
        Private _params As Dictionary(Of String, Object)
        Private _previousClose As Single
        Private _hasPrevious As Boolean
        Private _obv As Double
        Private _window() As Double
        Private _head, _count As Integer
        Private _sum As Double

        Private _savedPreviousClose As Single
        Private _savedHasPrevious As Boolean
        Private _savedObv, _savedSum, _savedHeadValue As Double
        Private _savedHead, _savedCount As Integer

        Public Sub New(Optional maPeriod As Integer = 20)
            SetPeriod(maPeriod)
        End Sub
        Public Overrides ReadOnly Property Name As String
            Get
                Return $"OBV_{_maPeriod}"
            End Get
        End Property
        Public Overrides ReadOnly Property DisplayName As String
            Get
                Return $"OBV(MA{_maPeriod})"
            End Get
        End Property
        Public Overrides ReadOnly Property PanelIndex As Integer
            Get
                Return 5
            End Get
        End Property
        Public Overrides Property Parameters As Dictionary(Of String, Object)
            Get
                Return _params
            End Get
            Set(value As Dictionary(Of String, Object))
                Dim p = If(value, New Dictionary(Of String, Object))
                SetPeriod(If(p.ContainsKey("MAPeriod"), Convert.ToInt32(p("MAPeriod")), _maPeriod))
            End Set
        End Property
        Private Sub SetPeriod(value As Integer)
            _maPeriod = Math.Max(1, value)
            _params = New Dictionary(Of String, Object) From {{"MAPeriod", _maPeriod}}
            _window = New Double(_maPeriod - 1) {}
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
            If Not _hasPrevious Then
                _obv = candle.Volume
                _hasPrevious = True
            ElseIf candle.Close > _previousClose Then
                _obv += candle.Volume
            ElseIf candle.Close < _previousClose Then
                _obv -= candle.Volume
            End If
            _previousClose = candle.Close
            Push(_obv)
            Dim signal = If(_count = _maPeriod, CSng(_sum / _maPeriod), Single.NaN)
            Return MakeResult(index, CSng(_obv), signal)
        End Function
        Private Sub Push(value As Double)
            If _count < _maPeriod Then
                _window((_head + _count) Mod _maPeriod) = value
                _count += 1
                _sum += value
            Else
                _sum += value - _window(_head)
                _window(_head) = value
                _head = (_head + 1) Mod _maPeriod
            End If
        End Sub
        Protected Overrides Sub SaveState()
            _savedPreviousClose = _previousClose : _savedHasPrevious = _hasPrevious
            _savedObv = _obv : _savedHead = _head : _savedCount = _count : _savedSum = _sum
            If _count = _maPeriod Then _savedHeadValue = _window(_head)
        End Sub
        Protected Overrides Sub RestoreState()
            _previousClose = _savedPreviousClose : _hasPrevious = _savedHasPrevious
            _obv = _savedObv : _head = _savedHead : _count = _savedCount : _sum = _savedSum
            If _count = _maPeriod Then _window(_head) = _savedHeadValue
        End Sub
        Private Sub ResetState()
            _previousClose = 0 : _hasPrevious = False : _obv = 0
            _head = 0 : _count = 0 : _sum = 0
            Array.Clear(_window, 0, _window.Length)
            ResetIncrementalIndex()
        End Sub
        Private Function MakeResult(index As Integer, obv As Single, signal As Single) As IndicatorResult
            Return New IndicatorResult With {
                .Name = Name, .Index = index, .PanelIndex = PanelIndex,
                .Values = New Dictionary(Of String, Single) From {
                    {"OBV", obv}, {"Signal", signal},
                    {"Direction", If(Single.IsNaN(signal), Single.NaN, If(obv > signal, 1.0F, -1.0F))}
                },
                .SeriesKinds = New Dictionary(Of String, SeriesKind) From {
                    {"OBV", SeriesKind.Line}, {"Signal", SeriesKind.Line}, {"Direction", SeriesKind.Meta}
                }
            }
        End Function
    End Class
End Namespace
