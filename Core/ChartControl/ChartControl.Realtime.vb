Imports System.Windows.Forms
Imports ChartKit.Abstractions
Imports ChartKit.Models

Namespace Core
    Public Partial Class ChartControl
        Private _dataSource As ICandleDataSource
        Private ReadOnly _realtimeEventSync As New Object()
        Private ReadOnly _pendingFinalUpdates As New Queue(Of CandleItem)()
        Private ReadOnly _pendingAppendedCandles As New Queue(Of CandleItem)()
        Private _pendingUpdatedCandle As CandleItem
        Private _realtimeDrainScheduled As Integer

        Public Sub AttachDataSource(src As ICandleDataSource, req As CandleRequest)
            If _dataSource IsNot Nothing Then
                RemoveHandler _dataSource.CandleAppended, AddressOf OnCandleAppended
                RemoveHandler _dataSource.CandleUpdated, AddressOf OnCandleUpdated
                _dataSource.StopRealtime()
            End If
            _dataSource = src
            If src Is Nothing Then Return
            Dim bars = src.GetCandles(req)
            LoadCandles(bars)
            AddHandler src.CandleAppended, AddressOf OnCandleAppended
            AddHandler src.CandleUpdated, AddressOf OnCandleUpdated
            src.StartRealtime(req)
        End Sub

        Public Sub AttachRealtimeSource(src As ICandleDataSource, req As CandleRequest)
            If _dataSource IsNot Nothing Then
                RemoveHandler _dataSource.CandleAppended, AddressOf OnCandleAppended
                RemoveHandler _dataSource.CandleUpdated, AddressOf OnCandleUpdated
                _dataSource.StopRealtime()
            End If
            _dataSource = src
            If src Is Nothing Then Return
            AddHandler src.CandleAppended, AddressOf OnCandleAppended
            AddHandler src.CandleUpdated, AddressOf OnCandleUpdated
            src.StartRealtime(req)
        End Sub

        Private Sub OnCandleAppended(sender As Object, e As CandleAppendedEventArgs)
            If e Is Nothing OrElse e.Candle Is Nothing Then Return
            If InvokeRequired Then
                SyncLock _realtimeEventSync
                    If _pendingUpdatedCandle IsNot Nothing Then
                        _pendingFinalUpdates.Enqueue(_pendingUpdatedCandle)
                    End If
                    _pendingAppendedCandles.Enqueue(e.Candle)
                    _pendingUpdatedCandle = Nothing
                End SyncLock
                ScheduleRealtimeDrain()
                Return
            End If
            ApplyAppendedCandle(e.Candle)
        End Sub

        Private Sub OnCandleUpdated(sender As Object, e As CandleUpdatedEventArgs)
            If e Is Nothing OrElse e.Candle Is Nothing Then Return
            If InvokeRequired Then
                SyncLock _realtimeEventSync
                    _pendingUpdatedCandle = e.Candle
                End SyncLock
                ScheduleRealtimeDrain()
                Return
            End If
            ApplyUpdatedCandle(e.Candle)
        End Sub

        Private Sub ScheduleRealtimeDrain()
            If Threading.Interlocked.CompareExchange(_realtimeDrainScheduled, 1, 0) <> 0 Then Return
            Try
                BeginInvoke(New MethodInvoker(AddressOf DrainRealtimeEvents))
            Catch ex As InvalidOperationException
                Threading.Interlocked.Exchange(_realtimeDrainScheduled, 0)
            End Try
        End Sub

        Private Sub DrainRealtimeEvents()
            Do
                Dim appended As CandleItem = Nothing
                Dim updated As CandleItem = Nothing
                SyncLock _realtimeEventSync
                    If _pendingFinalUpdates.Count > 0 Then
                        updated = _pendingFinalUpdates.Dequeue()
                    ElseIf _pendingAppendedCandles.Count > 0 Then
                        appended = _pendingAppendedCandles.Dequeue()
                    Else
                        updated = _pendingUpdatedCandle
                        _pendingUpdatedCandle = Nothing
                    End If
                End SyncLock

                If appended Is Nothing AndAlso updated Is Nothing Then Exit Do
                If appended IsNot Nothing Then ApplyAppendedCandle(appended)
                If updated IsNot Nothing Then ApplyUpdatedCandle(updated)
            Loop

            Threading.Interlocked.Exchange(_realtimeDrainScheduled, 0)
            Dim hasPending As Boolean
            SyncLock _realtimeEventSync
                hasPending = _pendingFinalUpdates.Count > 0 OrElse
                             _pendingAppendedCandles.Count > 0 OrElse
                             _pendingUpdatedCandle IsNot Nothing
            End SyncLock
            If hasPending Then ScheduleRealtimeDrain()
        End Sub

        Private Sub ApplyAppendedCandle(candle As CandleItem)
            Dim wasLatestVisible = IsLatestCandleVisible()
            Dim oldestEvicted = _candles.Add(candle)
            _indicatorEngine.UpdateLast(_candles)
            If oldestEvicted Then _vs.StartIndex = Math.Max(0, _vs.StartIndex - 1)
            If wasLatestVisible Then MoveToLatestVisible()
            _needsRepaint = True
            RaiseEvent CandleCountChanged(Me, EventArgs.Empty)
        End Sub

        Private Sub ApplyUpdatedCandle(candle As CandleItem)
            If _candles Is Nothing OrElse _candles.Count = 0 Then
                _candles.Add(candle)
            Else
                _candles(_candles.Count - 1) = candle
            End If
            _indicatorEngine.UpdateLast(_candles)
            _needsRepaint = True
        End Sub
    End Class
End Namespace
