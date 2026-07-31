Imports ChartKit.Abstractions
Imports ChartKit.Core.Signals
Imports ChartKit.Models
Imports System.Globalization

Namespace Core
    Public Partial Class ChartControl
        Public Sub SetStrategyCapture(candleIndex As Integer, capturePrice As Single)
            If _candles Is Nothing OrElse candleIndex < 0 OrElse candleIndex >= _candles.Count Then
                Throw New ArgumentOutOfRangeException(NameOf(candleIndex))
            End If
            If capturePrice <= 0 Then Throw New ArgumentOutOfRangeException(NameOf(capturePrice))
            _strategyCapture = New Strategies.StrategyCapture With {
                .CandleIndex = candleIndex,
                .CapturedAt = _candles(candleIndex).Dt,
                .CapturePrice = capturePrice}
            _needsRepaint = True
        End Sub

        Public Sub ClearStrategyCapture()
            _strategyCapture = Nothing
            _needsRepaint = True
        End Sub

        Public Sub LoadCandles(candles As List(Of CandleItem))
            If candles IsNot Nothing Then
                Dim capacity = Math.Max(CANDLE_RING_CAPACITY, candles.Count)
                Dim replacement As New CandleRingBuffer(capacity)
                For Each candle In candles
                    replacement.Add(candle)
                Next
                _candles = replacement
            End If
            _indicatorEngine.CalculateAll(_candles)
            ResetInitialViewportState()
            MoveToLatestVisible()
            _viewportInitialized = True
            _needsRepaint = True
            RaiseVisibleCandleCountChanged()
            RaiseEvent CandleCountChanged(Me, EventArgs.Empty)
        End Sub

        Public Function PrependCandles(olderCandles As List(Of CandleItem)) As Integer
            If olderCandles Is Nothing OrElse olderCandles.Count = 0 Then Return 0
            Dim oldCount = CandleCount
            Dim oldStart = _vs.StartIndex
            '' 틱봉은 서로 다른 봉이 같은 체결시각을 가질 수 있다.
            '' Dt.Ticks 하나로 병합하면 정상 봉까지 유실되므로 전체 봉 식별값으로
            '' 연속조회 경계의 완전 동일 중복만 제거한다.
            Dim merged As New Dictionary(Of String, CandleItem)(StringComparer.Ordinal)
            For Each candle In olderCandles
                If candle IsNot Nothing Then merged(CandleIdentity(candle)) = candle
            Next
            For Each candle In _candles
                If candle IsNot Nothing Then merged(CandleIdentity(candle)) = candle
            Next
            Dim ordered = merged.Values.OrderBy(Function(c) c.Dt).ToList()
            Dim replacement As New CandleRingBuffer(Math.Max(CANDLE_RING_CAPACITY, ordered.Count))
            For Each candle In ordered
                replacement.Add(candle)
            Next
            _candles = replacement
            Dim added = _candles.Count - oldCount
            If added <= 0 Then Return 0
            _indicatorEngine.CalculateAll(_candles)
            _vs.StartIndex = Math.Min(Math.Max(0, _candles.Count - 1), oldStart + added)
            _isAutoScaleY = True
            _needsRepaint = True
            ScheduleStateSave()
            RaiseEvent CandleCountChanged(Me, EventArgs.Empty)
            Return added
        End Function

        Private Shared Function CandleIdentity(candle As CandleItem) As String
            Return String.Concat(
                candle.Dt.Ticks.ToString(CultureInfo.InvariantCulture), "|",
                candle.Open.ToString("R", CultureInfo.InvariantCulture), "|",
                candle.High.ToString("R", CultureInfo.InvariantCulture), "|",
                candle.Low.ToString("R", CultureInfo.InvariantCulture), "|",
                candle.Close.ToString("R", CultureInfo.InvariantCulture), "|",
                candle.Volume.ToString(CultureInfo.InvariantCulture))
        End Function

        Public ReadOnly Property CandleCount As Integer
            Get
                Return If(_candles Is Nothing, 0, _candles.Count)
            End Get
        End Property

        Public ReadOnly Property VisibleCandleCount As Integer
            Get
                If _candles Is Nothing OrElse _candles.Count = 0 Then Return 0
                Return Math.Min(_vs.VisibleCount, _candles.Count)
            End Get
        End Property

        Public Sub UpdateTheme(update As Action(Of ChartTheme))
            If update Is Nothing Then Throw New ArgumentNullException(NameOf(update))
            update(_theme)
            _theme.Invalidate()
            _needsRepaint = True
        End Sub

        Public Sub SetVisibleCandleCount(count As Integer)
            If _candles Is Nothing OrElse _candles.Count = 0 Then Return
            Dim safeCount = Math.Max(1, Math.Min(count, _candles.Count))
            Dim chartWidth = Math.Max(1.0F, CSng(_sk.ClientSize.Width) - MARGIN_LEFT - MARGIN_RIGHT)
            Dim pitch = chartWidth / safeCount
            _vs.VisibleCount = safeCount
            _vs.Gap = Math.Min(DEFAULT_INITIAL_GAP, Math.Max(0.0F, pitch * 0.15F))
            _vs.CandleWidth = Math.Max(0.1F, pitch - _vs.Gap)
            MoveToLatestVisible()
            _isAutoScaleY = True
            _needsRepaint = True
            RaiseVisibleCandleCountChanged()
        End Sub

        Private Sub RaiseVisibleCandleCountChanged()
            RaiseEvent VisibleCandleCountChanged(Me, EventArgs.Empty)
        End Sub

        Public Function MoveToDate(tradingDate As Date) As Boolean
            If _candles Is Nothing OrElse _candles.Count = 0 Then Return False
            Dim targetIndex = _candles.FindIndex(
                Function(c) c IsNot Nothing AndAlso c.Dt.Date = tradingDate.Date)
            If targetIndex < 0 Then Return False
            _vs.StartIndex = targetIndex
            _isAutoScaleY = True
            _needsRepaint = True
            Return True
        End Function

        Public Function GetFirstCandleDate() As Date?
            If _candles Is Nothing OrElse _candles.Count = 0 Then Return Nothing
            Return _candles(0).Dt.Date
        End Function

        Public Function GetLastCandleDate() As Date?
            If _candles Is Nothing OrElse _candles.Count = 0 Then Return Nothing
            Return _candles(_candles.Count - 1).Dt.Date
        End Function

        Public Sub AddIndicator(ind As IIndicator)
            If ind Is Nothing Then Return
            _indicatorEngine.Register(ind)
            If _candles IsNot Nothing AndAlso _candles.Count > 0 Then _indicatorEngine.CalculateAll(_candles)
            _needsRepaint = True
            ScheduleStateSave()
        End Sub

        Public Sub AddShadeRule(rule As OverlayShadeRule)
            If rule Is Nothing Then Throw New ArgumentNullException(NameOf(rule))
            _shadeRules.Add(rule)
            _needsRepaint = True
        End Sub

        Public Sub AddSignalRule(rule As SignalRule)
            If rule Is Nothing Then Throw New ArgumentNullException(NameOf(rule))
            _signalRules.Add(rule)
            _needsRepaint = True
        End Sub

        Public Function GetIndicators() As List(Of IIndicator)
            Return _indicatorEngine.GetAll()
        End Function

        Public Sub RemoveIndicator(name As String)
            _indicatorEngine.Remove(name)
            If _candles IsNot Nothing AndAlso _candles.Count > 0 Then _indicatorEngine.CalculateAll(_candles)
            _needsRepaint = True
            ScheduleStateSave()
        End Sub

        Public Sub ClearIndicators()
            _indicatorEngine.Clear()
            _needsRepaint = True
            ScheduleStateSave()
        End Sub
    End Class
End Namespace
