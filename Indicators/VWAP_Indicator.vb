Imports ChartKit.Abstractions
Imports ChartKit.Models

Namespace Indicators
    '' Volume Weighted Average Price (거래일별 리셋).
    Public Class VWAP_Indicator
        Inherits IncrementalIndicatorBase

        Private _stdDev1 As Single = 1.0F
        Private _stdDev2 As Single = 2.0F
        Private _params As New Dictionary(Of String, Object) From {{"StdDev1", 1.0F}, {"StdDev2", 2.0F}}

        Private _cumulativePriceVolume As Double
        Private _cumulativeVolume As Double
        Private _cumulativePriceSquaredVolume As Double
        Private _lastDate As DateTime = DateTime.MinValue

        Private _savedCumulativePriceVolume As Double
        Private _savedCumulativeVolume As Double
        Private _savedCumulativePriceSquaredVolume As Double
        Private _savedLastDate As DateTime

        Public Sub New(Optional stdDev1 As Single = 1.0F, Optional stdDev2 As Single = 2.0F)
            _stdDev1 = stdDev1
            _stdDev2 = stdDev2
            _params("StdDev1") = _stdDev1
            _params("StdDev2") = _stdDev2
        End Sub

        Public Overrides ReadOnly Property Name As String
            Get
                Return "VWAP"
            End Get
        End Property

        Public Overrides ReadOnly Property DisplayName As String
            Get
                Return "VWAP"
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
                _params = If(value, New Dictionary(Of String, Object))
                If _params.ContainsKey("StdDev1") Then _stdDev1 = Convert.ToSingle(_params("StdDev1"))
                If _params.ContainsKey("StdDev2") Then _stdDev2 = Convert.ToSingle(_params("StdDev2"))
            End Set
        End Property

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
            If _lastDate <> DateTime.MinValue AndAlso candle.Dt.Date <> _lastDate.Date Then
                _cumulativePriceVolume = 0
                _cumulativeVolume = 0
                _cumulativePriceSquaredVolume = 0
            End If
            _lastDate = candle.Dt

            Dim typicalPrice = (CDbl(candle.High) + candle.Low + candle.Close) / 3.0R
            Dim volume = CDbl(candle.Volume)
            _cumulativePriceVolume += typicalPrice * volume
            _cumulativeVolume += volume
            _cumulativePriceSquaredVolume += typicalPrice * typicalPrice * volume

            Dim values As New Dictionary(Of String, Single)
            If _cumulativeVolume > 0 Then
                Dim vwap = _cumulativePriceVolume / _cumulativeVolume
                Dim variance = Math.Max(0.0R, _cumulativePriceSquaredVolume / _cumulativeVolume - vwap * vwap)
                Dim standardDeviation = Math.Sqrt(variance)
                values("Value") = CSng(vwap)
                values("Upper1") = CSng(vwap + _stdDev1 * standardDeviation)
                values("Lower1") = CSng(vwap - _stdDev1 * standardDeviation)
                values("Upper2") = CSng(vwap + _stdDev2 * standardDeviation)
                values("Lower2") = CSng(vwap - _stdDev2 * standardDeviation)
            Else
                For Each key In {"Value", "Upper1", "Lower1", "Upper2", "Lower2"}
                    values(key) = Single.NaN
                Next
            End If

            Return New IndicatorResult With {
                .Name = Name, .Index = index, .PanelIndex = PanelIndex, .Values = values,
                .SeriesKinds = New Dictionary(Of String, SeriesKind) From {
                    {"Value", SeriesKind.Line}, {"Upper1", SeriesKind.Line},
                    {"Lower1", SeriesKind.Line}, {"Upper2", SeriesKind.Line},
                    {"Lower2", SeriesKind.Line}
                }
            }
        End Function

        Protected Overrides Sub SaveState()
            _savedCumulativePriceVolume = _cumulativePriceVolume
            _savedCumulativeVolume = _cumulativeVolume
            _savedCumulativePriceSquaredVolume = _cumulativePriceSquaredVolume
            _savedLastDate = _lastDate
        End Sub

        Protected Overrides Sub RestoreState()
            _cumulativePriceVolume = _savedCumulativePriceVolume
            _cumulativeVolume = _savedCumulativeVolume
            _cumulativePriceSquaredVolume = _savedCumulativePriceSquaredVolume
            _lastDate = _savedLastDate
        End Sub

        Private Sub ResetState()
            _cumulativePriceVolume = 0
            _cumulativeVolume = 0
            _cumulativePriceSquaredVolume = 0
            _lastDate = DateTime.MinValue
            ResetIncrementalIndex()
        End Sub
    End Class
End Namespace
