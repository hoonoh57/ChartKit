Imports ChartKit.Abstractions
Imports ChartKit.Models

Namespace Indicators
    Public Class Disparity_Indicator
        Inherits IncrementalIndicatorBase

        Private _period As Integer
        Private _params As Dictionary(Of String, Object)
        Private _window() As Single
        Private _head, _count As Integer
        Private _sum As Double
        Private _savedHead, _savedCount As Integer
        Private _savedSum As Double
        Private _savedHeadValue As Single

        Public Sub New(Optional period As Integer = 20)
            SetPeriod(period)
        End Sub
        Public Overrides ReadOnly Property Name As String
            Get
                Return $"DISP_{_period}"
            End Get
        End Property
        Public Overrides ReadOnly Property DisplayName As String
            Get
                Return $"이격도({_period})"
            End Get
        End Property
        Public Overrides ReadOnly Property PanelIndex As Integer
            Get
                Return 6
            End Get
        End Property
        Public Overrides Property Parameters As Dictionary(Of String, Object)
            Get
                Return _params
            End Get
            Set(value As Dictionary(Of String, Object))
                Dim p = If(value, New Dictionary(Of String, Object))
                SetPeriod(If(p.ContainsKey("Period"), Convert.ToInt32(p("Period")), _period))
            End Set
        End Property
        Private Sub SetPeriod(value As Integer)
            _period = Math.Max(1, value)
            _params = New Dictionary(Of String, Object) From {{"Period", _period}}
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
            If _count < _period Then
                _window((_head + _count) Mod _period) = candle.Close
                _count += 1
                _sum += candle.Close
            Else
                _sum += CDbl(candle.Close) - _window(_head)
                _window(_head) = candle.Close
                _head = (_head + 1) Mod _period
            End If
            Dim ma = If(_count = _period, CSng(_sum / _period), Single.NaN)
            Dim value = If(Single.IsNaN(ma), Single.NaN, If(ma > 0, candle.Close / ma * 100.0F, 100.0F))
            Return MakeResult(index, value, ma)
        End Function
        Protected Overrides Sub SaveState()
            _savedHead = _head : _savedCount = _count : _savedSum = _sum
            If _count = _period Then _savedHeadValue = _window(_head)
        End Sub
        Protected Overrides Sub RestoreState()
            _head = _savedHead : _count = _savedCount : _sum = _savedSum
            If _count = _period Then _window(_head) = _savedHeadValue
        End Sub
        Private Sub ResetState()
            _head = 0 : _count = 0 : _sum = 0
            Array.Clear(_window, 0, _window.Length)
            ResetIncrementalIndex()
        End Sub
        Private Function MakeResult(index As Integer, value As Single, ma As Single) As IndicatorResult
            Return New IndicatorResult With {
                .Name = Name, .Index = index, .PanelIndex = PanelIndex,
                .Values = New Dictionary(Of String, Single) From {
                    {"Value", value}, {"MA", ma}, {"Upper", 105.0F}, {"Baseline", 100.0F}, {"Lower", 95.0F}
                },
                .SeriesKinds = New Dictionary(Of String, SeriesKind) From {
                    {"Value", SeriesKind.Line}, {"MA", SeriesKind.Meta}, {"Upper", SeriesKind.Baseline},
                    {"Baseline", SeriesKind.Baseline}, {"Lower", SeriesKind.Baseline}
                }
            }
        End Function
    End Class
End Namespace
