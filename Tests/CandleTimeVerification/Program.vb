Option Strict On
Option Explicit On
Option Infer Off

Imports System.Collections.Generic
Imports ChartKit.Core
Imports ChartKit.Core.Backtesting
Imports ChartKit.Models

Namespace Verification
    Public Module Program
        Public Function Main() As Integer
            Try
                VerifyExplicitCloseBoundary()
                VerifyIncompleteCandleExcluded()
                VerifyLegacyTimestampFallback()
                VerifyWrongTradingDateExcluded()
                VerifyRealtimeSnapshotPreservesMetadata()

                Console.WriteLine("candle_time_verification=PASS")
                Return 0
            Catch ex As Exception
                Console.Error.WriteLine("candle_time_verification=FAIL")
                Console.Error.WriteLine(ex.ToString())
                Return 1
            End Try
        End Function

        Private Sub VerifyExplicitCloseBoundary()
            Dim tradingDate As Date = New DateTime(2026, 7, 31)
            Dim candles As New List(Of CandleItem) From {
                CreateTimedCandle(
                    tradingDate.AddHours(9),
                    tradingDate.AddHours(9).AddMinutes(5),
                    True,
                    100.0F),
                CreateTimedCandle(
                    tradingDate.AddHours(9).AddMinutes(5),
                    tradingDate.AddHours(9).AddMinutes(10),
                    True,
                    110.0F)
            }

            Dim firstBoundary As Integer = CausalCandleSelector.FindLastClosedIndex(
                candles,
                tradingDate.AddHours(9).AddMinutes(5))
            Dim secondBoundary As Integer = CausalCandleSelector.FindLastClosedIndex(
                candles,
                tradingDate.AddHours(9).AddMinutes(10))

            Expect(firstBoundary = 0,
                   "09:05 포착에서 09:10 종가를 사용했습니다.")
            Expect(secondBoundary = 1,
                   "09:10 확정 봉을 선택하지 못했습니다.")
        End Sub

        Private Sub VerifyIncompleteCandleExcluded()
            Dim tradingDate As Date = New DateTime(2026, 7, 31)
            Dim candles As New List(Of CandleItem) From {
                CreateTimedCandle(
                    tradingDate.AddHours(9),
                    tradingDate.AddHours(9).AddMinutes(4),
                    True,
                    100.0F),
                CreateTimedCandle(
                    tradingDate.AddHours(9).AddMinutes(4),
                    tradingDate.AddHours(9).AddMinutes(5),
                    False,
                    999.0F)
            }

            Dim selected As Integer = CausalCandleSelector.FindLastClosedIndex(
                candles,
                tradingDate.AddHours(9).AddMinutes(5))

            Expect(selected = 0,
                   "미확정 봉이 포착가격 선택에 포함됐습니다.")
        End Sub

        Private Sub VerifyLegacyTimestampFallback()
            Dim tradingDate As Date = New DateTime(2026, 7, 31)
            Dim candles As New List(Of CandleItem) From {
                CreateLegacyCandle(tradingDate.AddHours(9).AddMinutes(4), 100.0F),
                CreateLegacyCandle(tradingDate.AddHours(9).AddMinutes(5), 101.0F),
                CreateLegacyCandle(tradingDate.AddHours(9).AddMinutes(6), 102.0F)
            }

            Dim selected As Integer = CausalCandleSelector.FindLastClosedIndex(
                candles,
                tradingDate.AddHours(9).AddMinutes(5))

            Expect(selected = 1,
                   "기존 Dt 기반 데이터의 호환 선택이 실패했습니다.")
        End Sub

        Private Sub VerifyWrongTradingDateExcluded()
            Dim targetDate As Date = New DateTime(2026, 7, 31)
            Dim previousDate As Date = targetDate.AddDays(-1)
            Dim candles As New List(Of CandleItem) From {
                CreateTimedCandle(
                    previousDate.AddHours(15),
                    previousDate.AddHours(15).AddMinutes(30),
                    True,
                    90.0F),
                CreateTimedCandle(
                    targetDate.AddHours(9),
                    targetDate.AddHours(9).AddMinutes(5),
                    True,
                    100.0F)
            }

            Dim selected As Integer = CausalCandleSelector.FindLastClosedIndex(
                candles,
                targetDate.AddHours(9).AddMinutes(4))

            Expect(selected = -1,
                   "이전 거래일 봉이 당일 포착봉으로 선택됐습니다.")
        End Sub

        Private Sub VerifyRealtimeSnapshotPreservesMetadata()
            Dim buffer As New RealtimeEventBuffer()
            Dim generation As Long = buffer.BeginGeneration()
            Dim source As CandleItem = CreateTimedCandle(
                New DateTime(2026, 7, 31, 9, 0, 0),
                New DateTime(2026, 7, 31, 9, 5, 0),
                False,
                100.0F)
            source.Sequence = 77L

            Expect(
                buffer.Enqueue(
                    generation,
                    RealtimeCandleEventKind.Updated,
                    source),
                "시간 메타데이터 snapshot enqueue 실패")

            source.Sequence = 999L
            source.CloseTime = source.CloseTime.AddHours(1)
            source.IsFinal = True

            Dim queuedEvent As RealtimeCandleEvent = Nothing
            Expect(buffer.TryDequeue(queuedEvent),
                   "시간 메타데이터 snapshot dequeue 실패")
            If queuedEvent Is Nothing Then
                Throw New InvalidOperationException(
                    "시간 메타데이터 snapshot이 Nothing입니다.")
            End If

            Expect(queuedEvent.Candle.Sequence = 77L,
                   "Sequence snapshot이 보존되지 않았습니다.")
            Expect(queuedEvent.Candle.CloseTime = New DateTime(2026, 7, 31, 9, 5, 0),
                   "CloseTime snapshot이 보존되지 않았습니다.")
            Expect(Not queuedEvent.Candle.IsFinal,
                   "IsFinal snapshot이 보존되지 않았습니다.")
        End Sub

        Private Function CreateTimedCandle(openTime As DateTime,
                                           closeTime As DateTime,
                                           isFinal As Boolean,
                                           closeValue As Single) As CandleItem
            Return New CandleItem With {
                .Dt = closeTime,
                .OpenTime = openTime,
                .CloseTime = closeTime,
                .IsFinal = isFinal,
                .Open = closeValue,
                .High = closeValue,
                .Low = closeValue,
                .Close = closeValue,
                .Volume = 1L
            }
        End Function

        Private Function CreateLegacyCandle(timestamp As DateTime,
                                            closeValue As Single) As CandleItem
            Return New CandleItem With {
                .Dt = timestamp,
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