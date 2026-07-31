Option Strict On
Option Explicit On
Option Infer Off

Imports ChartKit.Core
Imports ChartKit.Models

Namespace Verification
    Public Module Program
        Public Function Main() As Integer
            Try
                VerifyFifoAndCoalescing()
                VerifyGenerationIsolation()
                VerifySnapshotIsolation()

                Console.WriteLine("realtime_fifo_verification=PASS")
                Return 0
            Catch ex As Exception
                Console.Error.WriteLine("realtime_fifo_verification=FAIL")
                Console.Error.WriteLine(ex.ToString())
                Return 1
            End Try
        End Function

        Private Sub VerifyFifoAndCoalescing()
            Dim buffer As New RealtimeEventBuffer()
            Dim generation As Long = buffer.BeginGeneration()

            Expect(
                buffer.Enqueue(
                    generation,
                    RealtimeCandleEventKind.Updated,
                    CreateCandle(0, 101.0F)),
                "첫 Update enqueue 실패")

            Expect(
                buffer.Enqueue(
                    generation,
                    RealtimeCandleEventKind.Updated,
                    CreateCandle(0, 102.0F)),
                "연속 Update enqueue 실패")

            Expect(
                buffer.Enqueue(
                    generation,
                    RealtimeCandleEventKind.Appended,
                    CreateCandle(1, 200.0F)),
                "첫 Append enqueue 실패")

            Expect(
                buffer.Enqueue(
                    generation,
                    RealtimeCandleEventKind.Updated,
                    CreateCandle(1, 201.0F)),
                "Append 이후 Update enqueue 실패")

            Expect(
                buffer.Enqueue(
                    generation,
                    RealtimeCandleEventKind.Appended,
                    CreateCandle(2, 300.0F)),
                "두 번째 Append enqueue 실패")

            Expect(
                buffer.PendingCount = 4,
                "연속 Update 병합 또는 Append 경계 보존 실패")

            ExpectEvent(
                buffer,
                RealtimeCandleEventKind.Updated,
                102.0F,
                "첫 Update")

            ExpectEvent(
                buffer,
                RealtimeCandleEventKind.Appended,
                200.0F,
                "첫 Append")

            ExpectEvent(
                buffer,
                RealtimeCandleEventKind.Updated,
                201.0F,
                "두 번째 Update")

            ExpectEvent(
                buffer,
                RealtimeCandleEventKind.Appended,
                300.0F,
                "두 번째 Append")

            Expect(
                Not buffer.HasPending,
                "FIFO 검증 후 큐가 비어 있지 않음")
        End Sub

        Private Sub VerifyGenerationIsolation()
            Dim buffer As New RealtimeEventBuffer()
            Dim staleGeneration As Long = buffer.BeginGeneration()

            Expect(
                buffer.Enqueue(
                    staleGeneration,
                    RealtimeCandleEventKind.Updated,
                    CreateCandle(0, 100.0F)),
                "기존 generation enqueue 실패")

            Dim currentGeneration As Long = buffer.BeginGeneration()

            Expect(
                Not buffer.Enqueue(
                    staleGeneration,
                    RealtimeCandleEventKind.Appended,
                    CreateCandle(1, 999.0F)),
                "이전 generation 이벤트가 허용됨")

            Expect(
                buffer.Enqueue(
                    currentGeneration,
                    RealtimeCandleEventKind.Appended,
                    CreateCandle(1, 200.0F)),
                "현재 generation 이벤트가 거부됨")

            Expect(
                buffer.PendingCount = 1,
                "generation 전환 시 기존 큐가 제거되지 않음")

            ExpectEvent(
                buffer,
                RealtimeCandleEventKind.Appended,
                200.0F,
                "현재 generation Append")
        End Sub

        Private Sub VerifySnapshotIsolation()
            Dim buffer As New RealtimeEventBuffer()
            Dim generation As Long = buffer.BeginGeneration()
            Dim source As CandleItem = CreateCandle(0, 401.0F)

            Expect(
                buffer.Enqueue(
                    generation,
                    RealtimeCandleEventKind.Updated,
                    source),
                "snapshot enqueue 실패")

            source.Close = 999.0F
            source.High = 999.0F

            ExpectEvent(
                buffer,
                RealtimeCandleEventKind.Updated,
                401.0F,
                "snapshot 복제")
        End Sub

        Private Sub ExpectEvent(buffer As RealtimeEventBuffer,
                                expectedKind As RealtimeCandleEventKind,
                                expectedClose As Single,
                                description As String)
            Dim queuedEvent As RealtimeCandleEvent = Nothing

            Expect(
                buffer.TryDequeue(queuedEvent),
                description & ": dequeue 실패")

            If queuedEvent Is Nothing Then
                Throw New InvalidOperationException(
                    description & ": 이벤트가 Nothing")
            End If

            Expect(
                queuedEvent.Kind = expectedKind,
                description & ": 이벤트 종류 불일치")

            Expect(
                Math.Abs(
                    queuedEvent.Candle.Close -
                    expectedClose) < 0.0001F,
                description & ": 종가 불일치")
        End Sub

        Private Function CreateCandle(minuteOffset As Integer,
                                      closeValue As Single) As CandleItem
            Return New CandleItem With {
                .Dt = New DateTime(
                    2026,
                    7,
                    31,
                    9,
                    0,
                    0,
                    DateTimeKind.Local).AddMinutes(minuteOffset),
                .Open = closeValue,
                .High = closeValue,
                .Low = closeValue,
                .Close = closeValue,
                .Volume = 1L
            }
        End Function

        Private Sub Expect(condition As Boolean,
                           message As String)
            If Not condition Then
                Throw New InvalidOperationException(message)
            End If
        End Sub
    End Module
End Namespace
