Imports ChartKit.Abstractions
Imports ChartKit.Models

Namespace Indicators
    Public Class MA_Indicator
        Inherits IncrementalIndicatorBase

        Private _period As Integer
        Private _maType As String
        Private _params As Dictionary(Of String, Object)
        Private _window() As Single
        Private _head, _count As Integer
        Private _sum, _weightedSum, _ema As Double

        Private _savedHead, _savedCount As Integer
        Private _savedSum, _savedWeightedSum, _savedEma As Double
        Private _savedHeadValue As Single

        Public Sub New(Optional period As Integer = 20, Optional maType As String = "SMA")
            SetOptions(period, maType)
        End Sub

        Public Overrides ReadOnly Property Name As String
            Get
                Return $"{_maType}_{_period}"
            End Get
        End Property
        Public Overrides ReadOnly Property DisplayName As String
            Get
                Return $"{_maType}({_period})"
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
                           If(p.ContainsKey("Type"), Convert.ToString(p("Type")), _maType))
            End Set
        End Property

        Private Sub SetOptions(period As Integer, maType As String)
            _period = Math.Max(1, period)
            _maType = If(maType, "SMA").ToUpperInvariant()
            If _maType <> "SMA" AndAlso _maType <> "EMA" AndAlso _maType <> "WMA" Then _maType = "SMA"
            _params = New Dictionary(Of String, Object) From {{"Period", _period}, {"Type", _maType}}
            _window = New Single(_period - 1) {}
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
            Dim value = CDbl(candle.Close)
            Dim result = Single.NaN
            Select Case _maType
                Case "EMA"
                    If _count < _period Then
                        _window(_count) = candle.Close
                        _sum += value
                        _count += 1
                        If _count = _period Then
                            _ema = _sum / _period
                            result = CSng(_ema)
                        End If
                    Else
                        Dim alpha = 2.0R / (_period + 1.0R)
                        _ema += alpha * (value - _ema)
                        result = CSng(_ema)
                    End If
                Case "WMA"
                    If _count < _period Then
                        _weightedSum += (_count + 1) * value
                        _sum += value
                        _window((_head + _count) Mod _period) = candle.Close
                        _count += 1
                    Else
                        Dim oldest = CDbl(_window(_head))
                        _weightedSum = _weightedSum - _sum + _period * value
                        _sum += value - oldest
                        _window(_head) = candle.Close
                        _head = (_head + 1) Mod _period
                    End If
                    If _count = _period Then result = CSng(_weightedSum / (_period * (_period + 1.0R) / 2.0R))
                Case Else
                    PushWindow(candle.Close)
                    If _count = _period Then result = CSng(_sum / _period)
            End Select
            Return MakeResult(index, result)
        End Function

        Private Sub PushWindow(value As Single)
            If _count < _period Then
                _window((_head + _count) Mod _period) = value
                _count += 1
                _sum += value
            Else
                _sum += CDbl(value) - _window(_head)
                _window(_head) = value
                _head = (_head + 1) Mod _period
            End If
        End Sub

        Protected Overrides Sub SaveState()
            _savedHead = _head : _savedCount = _count
            _savedSum = _sum : _savedWeightedSum = _weightedSum : _savedEma = _ema
            If _count = _period Then _savedHeadValue = _window(_head)
        End Sub
        Protected Overrides Sub RestoreState()
            _head = _savedHead : _count = _savedCount
            _sum = _savedSum : _weightedSum = _savedWeightedSum : _ema = _savedEma
            If _count = _period Then _window(_head) = _savedHeadValue
        End Sub
        Private Sub ResetState()
            _head = 0 : _count = 0 : _sum = 0 : _weightedSum = 0 : _ema = 0
            Array.Clear(_window, 0, _window.Length)
            ResetIncrementalIndex()
        End Sub
        Private Function MakeResult(index As Integer, value As Single) As IndicatorResult
            Return New IndicatorResult With {
                .Name = Name, .Index = index, .PanelIndex = PanelIndex,
                .Values = New Dictionary(Of String, Single) From {{"Value", value}},
                .SeriesKinds = New Dictionary(Of String, SeriesKind) From {{"Value", SeriesKind.Line}}
            }
        End Function
    End Class
End Namespace
