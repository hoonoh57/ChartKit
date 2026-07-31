Option Strict On
Option Explicit On
Option Infer Off

Imports System.Collections.Generic
Imports ChartKit.Models

Namespace Core
    Friend Enum RealtimeCandleEventKind
        Updated = 0
        Appended = 1
    End Enum

    Friend NotInheritable Class RealtimeCandleEvent
        Public Sub New(generation As Long,
                       kind As RealtimeCandleEventKind,
                       candle As CandleItem)
            Me.Generation = generation
            Me.Kind = kind
            Me.Candle = candle
        End Sub

        Public ReadOnly Property Generation As Long
        Public ReadOnly Property Kind As RealtimeCandleEventKind
        Public ReadOnly Property Candle As CandleItem
    End Class

    Friend NotInheritable Class RealtimeEventBuffer
        Private ReadOnly _sync As New Object()
        Private ReadOnly _events As New LinkedList(Of RealtimeCandleEvent)()
        Private _generation As Long

        Public Function BeginGeneration() As Long
            SyncLock _sync
                _generation += 1L
                _events.Clear()
                Return _generation
            End SyncLock
        End Function

        Public Function IsCurrentGeneration(generation As Long) As Boolean
            SyncLock _sync
                Return generation = _generation
            End SyncLock
        End Function

        Public Function Enqueue(generation As Long,
                                kind As RealtimeCandleEventKind,
                                candle As CandleItem) As Boolean
            If candle Is Nothing Then Return False

            Dim snapshot As CandleItem = CloneCandle(candle)

            SyncLock _sync
                If generation <> _generation Then Return False

                Dim queuedEvent As New RealtimeCandleEvent(
                    generation,
                    kind,
                    snapshot)
                Dim lastNode As LinkedListNode(Of RealtimeCandleEvent) = _events.Last

                ' 연속된 미확정 봉 Update만 최신 snapshot으로 교체한다.
                ' Append 경계는 절대 넘지 않으므로 원 이벤트 순서가 유지된다.
                If kind = RealtimeCandleEventKind.Updated AndAlso
                   lastNode IsNot Nothing AndAlso
                   lastNode.Value.Generation = generation AndAlso
                   lastNode.Value.Kind = RealtimeCandleEventKind.Updated Then

                    lastNode.Value = queuedEvent
                Else
                    _events.AddLast(queuedEvent)
                End If

                Return True
            End SyncLock
        End Function

        Public Function TryDequeue(ByRef queuedEvent As RealtimeCandleEvent) As Boolean
            SyncLock _sync
                If _events.Count = 0 Then
                    queuedEvent = Nothing
                    Return False
                End If

                queuedEvent = _events.First.Value
                _events.RemoveFirst()
                Return True
            End SyncLock
        End Function

        Public ReadOnly Property HasPending As Boolean
            Get
                SyncLock _sync
                    Return _events.Count > 0
                End SyncLock
            End Get
        End Property

        Public ReadOnly Property PendingCount As Integer
            Get
                SyncLock _sync
                    Return _events.Count
                End SyncLock
            End Get
        End Property

        Private Shared Function CloneCandle(source As CandleItem) As CandleItem
            Return New CandleItem With {
                .Dt = source.Dt,
                .Open = source.Open,
                .High = source.High,
                .Low = source.Low,
                .Close = source.Close,
                .Volume = source.Volume
            }
        End Function
    End Class
End Namespace
