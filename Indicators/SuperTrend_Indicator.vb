Imports ChartKit.Abstractions
Imports ChartKit.Models

Namespace Indicators
    Public Class SuperTrend_Indicator
        Inherits IncrementalIndicatorBase

        Private _atrPeriod As Integer
        Private _multiplier As Single
        Private _params As Dictionary(Of String, Object)
        Private _previousClose, _atr, _upper, _lower, _superTrend As Single
        Private _trSum As Double
        Private _count, _direction As Integer
        Private _hasPrevious As Boolean

        Private _savedPreviousClose, _savedAtr, _savedUpper, _savedLower, _savedSuperTrend As Single
        Private _savedTrSum As Double
        Private _savedCount, _savedDirection As Integer
        Private _savedHasPrevious As Boolean

        Public Sub New(Optional atrPeriod As Integer = 10, Optional multiplier As Single = 3.0F)
            SetOptions(atrPeriod, multiplier)
        End Sub
        Public Overrides ReadOnly Property Name As String
            Get
                Return $"ST_{_atrPeriod}_{_multiplier:F1}"
            End Get
        End Property
        Public Overrides ReadOnly Property DisplayName As String
            Get
                Return $"SuperTrend({_atrPeriod},{_multiplier:F1})"
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
                SetOptions(If(p.ContainsKey("AtrPeriod"), Convert.ToInt32(p("AtrPeriod")), _atrPeriod),
                           If(p.ContainsKey("Multiplier"), Convert.ToSingle(p("Multiplier")), _multiplier))
            End Set
        End Property
        Private Sub SetOptions(period As Integer, multiplier As Single)
            _atrPeriod = Math.Max(1, period)
            _multiplier = Math.Max(0.01F, multiplier)
            _params = New Dictionary(Of String, Object) From {
                {"AtrPeriod", _atrPeriod}, {"Multiplier", _multiplier}
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
        Protected Overrides Function StepCandle(c As CandleItem, index As Integer) As IndicatorResult
            Dim tr = If(_hasPrevious,
                        Math.Max(c.High - c.Low, Math.Max(Math.Abs(c.High - _previousClose), Math.Abs(c.Low - _previousClose))),
                        c.High - c.Low)
            _count += 1
            If _count <= _atrPeriod Then
                _trSum += tr
                If _count = _atrPeriod Then _atr = CSng(_trSum / _atrPeriod)
            Else
                _atr = (_atr * (_atrPeriod - 1) + tr) / _atrPeriod
            End If

            If _count >= _atrPeriod Then
                Dim midpoint = (c.High + c.Low) / 2.0F
                Dim basicUpper = midpoint + _multiplier * _atr
                Dim basicLower = midpoint - _multiplier * _atr
                If Single.IsNaN(_upper) OrElse basicUpper < _upper OrElse _previousClose > _upper Then _upper = basicUpper
                If Single.IsNaN(_lower) OrElse basicLower > _lower OrElse _previousClose < _lower Then _lower = basicLower
                If _direction = 1 Then
                    If c.Close < _lower Then _direction = -1
                ElseIf c.Close > _upper Then
                    _direction = 1
                End If
                _superTrend = If(_direction = 1, _lower, _upper)
            End If
            _previousClose = c.Close
            _hasPrevious = True
            Return MakeResult(index)
        End Function
        Protected Overrides Sub SaveState()
            _savedPreviousClose = _previousClose : _savedAtr = _atr
            _savedUpper = _upper : _savedLower = _lower : _savedSuperTrend = _superTrend
            _savedTrSum = _trSum : _savedCount = _count : _savedDirection = _direction
            _savedHasPrevious = _hasPrevious
        End Sub
        Protected Overrides Sub RestoreState()
            _previousClose = _savedPreviousClose : _atr = _savedAtr
            _upper = _savedUpper : _lower = _savedLower : _superTrend = _savedSuperTrend
            _trSum = _savedTrSum : _count = _savedCount : _direction = _savedDirection
            _hasPrevious = _savedHasPrevious
        End Sub
        Private Sub ResetState()
            _previousClose = 0 : _atr = Single.NaN : _upper = Single.NaN
            _lower = Single.NaN : _superTrend = Single.NaN : _trSum = 0
            _count = 0 : _direction = 1 : _hasPrevious = False
            ResetIncrementalIndex()
        End Sub
        Private Function MakeResult(index As Integer) As IndicatorResult
            Return New IndicatorResult With {
                .Name = Name, .Index = index, .PanelIndex = PanelIndex,
                .Values = New Dictionary(Of String, Single) From {
                    {"Value", _superTrend}, {"Up", If(_direction = 1, _superTrend, Single.NaN)},
                    {"Down", If(_direction = -1, _superTrend, Single.NaN)},
                    {"Direction", CSng(_direction)}, {"ATR", _atr}
                },
                .SeriesKinds = New Dictionary(Of String, SeriesKind) From {
                    {"Value", SeriesKind.Line}, {"Up", SeriesKind.Line}, {"Down", SeriesKind.Line},
                    {"Direction", SeriesKind.Meta}, {"ATR", SeriesKind.Meta}
                }
            }
        End Function
    End Class
End Namespace
