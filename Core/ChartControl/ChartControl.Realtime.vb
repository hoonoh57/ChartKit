Option Strict On
Option Explicit On
Option Infer Off

Imports System.Collections.Generic
Imports System.Windows.Forms
Imports ChartKit.Abstractions
Imports ChartKit.Models

Namespace Core
    Public Partial Class ChartControl
        Private Const REALTIME_DRAIN_MAX_EVENTS As Integer = 256
        Private Const REALTIME_DRAIN_BUDGET_MS As Long = 4L

        Private _dataSource As ICandleDataSource
        Private ReadOnly _dataSourceSync As New Object()
        Private ReadOnly _realtimeEvents As New RealtimeEventBuffer()
        Private _dataSourceGeneration As Long
        Private _realtimeDrainScheduled As Integer

        Public Sub AttachDataSource(src As ICandleDataSource,
                                    req As CandleRequest)
            If req Is Nothing Then Throw New ArgumentNullException(NameOf(req))

            Dim generation As Long = ReplaceDataSource(src)
            If src Is Nothing Then Return

            Try
                Dim bars As List(Of CandleItem) = src.GetCandles(req)

                If Not IsCurrentDataSource(src, generation) Then Return

                LoadCandles(bars)
                src.StartRealtime(req)
            Catch
                DetachDataSourceIfCurrent(src, generation)
                Throw
            End Try
        End Sub

        Public Sub AttachRealtimeSource(src As ICandleDataSource,
                                        req As CandleRequest)
            If req Is Nothing Then Throw New ArgumentNullException(NameOf(req))

            Dim generation As Long = ReplaceDataSource(src)
            If src Is Nothing Then Return

            Try
                If Not IsCurrentDataSource(src, generation) Then Return
                src.StartRealtime(req)
            Catch
                DetachDataSourceIfCurrent(src, generation)
                Throw
            End Try
        End Sub

        Private Function ReplaceDataSource(src As ICandleDataSource) As Long
            ' generation을 먼저 바꿔 기존 source의 진행 중 callback을 무효화한다.
            Dim generation As Long = _realtimeEvents.BeginGeneration()
            Dim previousSource As ICandleDataSource = Nothing

            SyncLock _dataSourceSync
                previousSource = _dataSource
                _dataSource = Nothing
                _dataSourceGeneration = generation
            End SyncLock

            If previousSource IsNot Nothing Then
                RemoveHandler previousSource.CandleAppended, AddressOf OnCandleAppended
                RemoveHandler previousSource.CandleUpdated, AddressOf OnCandleUpdated

                Try
                    previousSource.StopRealtime()
                Catch ex As Exception
                    ChartLog.Warning("실시간 데이터 소스 종료 실패", ex)
                End Try
            End If

            SyncLock _dataSourceSync
                _dataSource = src
                _dataSourceGeneration = generation
            End SyncLock

            If src IsNot Nothing Then
                AddHandler src.CandleAppended, AddressOf OnCandleAppended
                AddHandler src.CandleUpdated, AddressOf OnCandleUpdated
            End If

            Return generation
        End Function

        Private Function IsCurrentDataSource(src As ICandleDataSource,
                                             generation As Long) As Boolean
            SyncLock _dataSourceSync
                Return Object.ReferenceEquals(_dataSource, src) AndAlso
                       _dataSourceGeneration = generation AndAlso
                       _realtimeEvents.IsCurrentGeneration(generation)
            End SyncLock
        End Function

        Private Sub DetachDataSourceIfCurrent(src As ICandleDataSource,
                                              generation As Long)
            Dim shouldDetach As Boolean

            SyncLock _dataSourceSync
                shouldDetach = Object.ReferenceEquals(_dataSource, src) AndAlso
                               _dataSourceGeneration = generation
            End SyncLock

            If Not shouldDetach Then Return

            _realtimeEvents.BeginGeneration()

            SyncLock _dataSourceSync
                If Object.ReferenceEquals(_dataSource, src) AndAlso
                   _dataSourceGeneration = generation Then
                    _dataSource = Nothing
                Else
                    Return
                End If
            End SyncLock

            RemoveHandler src.CandleAppended, AddressOf OnCandleAppended
            RemoveHandler src.CandleUpdated, AddressOf OnCandleUpdated

            Try
                src.StopRealtime()
            Catch ex As Exception
                ChartLog.Warning("실시간 데이터 소스 종료 실패", ex)
            End Try
        End Sub

        Private Sub OnCandleAppended(sender As Object,
                                     e As CandleAppendedEventArgs)
            If e Is Nothing OrElse e.Candle Is Nothing Then Return
            If IsDisposed OrElse Disposing Then Return

            Dim generation As Long

            SyncLock _dataSourceSync
                If Not Object.ReferenceEquals(sender, _dataSource) Then Return
                generation = _dataSourceGeneration
            End SyncLock

            EnqueueRealtimeEvent(
                generation,
                RealtimeCandleEventKind.Appended,
                e.Candle)
        End Sub

        Private Sub OnCandleUpdated(sender As Object,
                                    e As CandleUpdatedEventArgs)
            If e Is Nothing OrElse e.Candle Is Nothing Then Return
            If IsDisposed OrElse Disposing Then Return

            Dim generation As Long

            SyncLock _dataSourceSync
                If Not Object.ReferenceEquals(sender, _dataSource) Then Return
                generation = _dataSourceGeneration
            End SyncLock

            EnqueueRealtimeEvent(
                generation,
                RealtimeCandleEventKind.Updated,
                e.Candle)
        End Sub

        Private Sub EnqueueRealtimeEvent(generation As Long,
                                         kind As RealtimeCandleEventKind,
                                         candle As CandleItem)
            If Not _realtimeEvents.Enqueue(generation, kind, candle) Then Return
            ScheduleRealtimeDrain()
        End Sub

        Private Sub ScheduleRealtimeDrain()
            If IsDisposed OrElse Disposing OrElse Not IsHandleCreated Then Return

            If Threading.Interlocked.CompareExchange(
                _realtimeDrainScheduled,
                1,
                0) <> 0 Then Return

            Try
                BeginInvoke(New MethodInvoker(AddressOf DrainRealtimeEvents))
            Catch ex As ObjectDisposedException
                Threading.Interlocked.Exchange(_realtimeDrainScheduled, 0)
            Catch ex As InvalidOperationException
                Threading.Interlocked.Exchange(_realtimeDrainScheduled, 0)
            End Try
        End Sub

        Protected Overrides Sub OnHandleCreated(e As EventArgs)
            MyBase.OnHandleCreated(e)

            ' Handle 생성 전에 도착한 이벤트가 있으면 이 시점에 처리한다.
            If _realtimeEvents.HasPending Then ScheduleRealtimeDrain()
        End Sub

        Private Sub DrainRealtimeEvents()
            If IsDisposed OrElse Disposing Then
                Threading.Interlocked.Exchange(_realtimeDrainScheduled, 0)
                Return
            End If

            Dim drainWatch As System.Diagnostics.Stopwatch =
                System.Diagnostics.Stopwatch.StartNew()
            Dim processed As Integer = 0

            Do
                If processed >= REALTIME_DRAIN_MAX_EVENTS Then Exit Do

                If processed > 0 AndAlso
                   drainWatch.ElapsedMilliseconds >= REALTIME_DRAIN_BUDGET_MS Then
                    Exit Do
                End If

                Dim queuedEvent As RealtimeCandleEvent = Nothing

                If Not _realtimeEvents.TryDequeue(queuedEvent) Then Exit Do

                processed += 1

                If queuedEvent Is Nothing OrElse
                   Not _realtimeEvents.IsCurrentGeneration(
                       queuedEvent.Generation) Then
                    Continue Do
                End If

                Select Case queuedEvent.Kind
                    Case RealtimeCandleEventKind.Appended
                        ApplyAppendedCandle(queuedEvent.Candle)

                    Case RealtimeCandleEventKind.Updated
                        ApplyUpdatedCandle(queuedEvent.Candle)
                End Select
            Loop

            Threading.Interlocked.Exchange(_realtimeDrainScheduled, 0)

            If _realtimeEvents.HasPending Then ScheduleRealtimeDrain()
        End Sub

        Private Sub ApplyAppendedCandle(candle As CandleItem)
            Dim wasLatestVisible As Boolean = IsLatestCandleVisible()
            Dim oldestEvicted As Boolean = _candles.Add(candle)

            _indicatorEngine.UpdateLast(_candles)

            If oldestEvicted Then
                _vs.StartIndex = Math.Max(0, _vs.StartIndex - 1)
            End If

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
