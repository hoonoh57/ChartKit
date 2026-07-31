Option Strict On
Option Explicit On
Option Infer Off

Imports System
Imports System.Collections.Generic
Imports ChartKit.DataSources
Imports ChartKit.Models

Namespace Verification
    Public Module TickAggregationProgram
        Public Function Main() As Integer
            Try
                VerifyTradingDayIsolation()
                VerifySameDayPrependInvariance()
                VerifyPreviousDayPrependInvariance()
                VerifyMetadataAndSourceIsolation()
                VerifyValidation()

                Console.WriteLine("tick_aggregation_verification=PASS")
                Return 0
            Catch ex As Exception
                Console.Error.WriteLine("tick_aggregation_verification=FAIL")
                Console.Error.WriteLine(ex.ToString())
                Return 1
            End Try
        End Function

        Private Sub VerifyTradingDayIsolation()
            Dim firstDay As DateTime = New DateTime(2026, 7, 30)
            Dim secondDay As DateTime = New DateTime(2026, 7, 31)
            Dim source As New List(Of CandleItem)()

            source.AddRange(CreateCandles(firstDay, 9, 0, 4, 100.0F, 0L))
            source.AddRange(CreateCandles(secondDay, 9, 0, 5, 200.0F, 100L))

            Dim aggregated As List(Of CandleItem) =
                TickAggregator.Aggregate(source, 30, 10)

            Expect(aggregated.Count = 2,
                   "거래일별 완전 그룹 수가 올바르지 않습니다.")
            Expect(aggregated(0).TradingDate = firstDay.Date,
                   "첫 결과 봉의 거래일이 잘못되었습니다.")
            Expect(aggregated(1).TradingDate = secondDay.Date,
                   "두 번째 결과 봉의 거래일이 잘못되었습니다.")
            Expect(aggregated(0).Open = 101.0F AndAlso
                   aggregated(0).Close = 103.0F,
                   "첫 거래일의 최신 기준 그룹 경계가 잘못되었습니다.")
            Expect(aggregated(1).Open = 202.0F AndAlso
                   aggregated(1).Close = 204.0F,
                   "둘째 거래일의 최신 기준 그룹 경계가 잘못되었습니다.")
            Expect(aggregated(0).OpenTime.Date =
                   aggregated(0).CloseTime.Date,
                   "결과 봉이 거래일 경계를 넘었습니다.")
            Expect(aggregated(1).OpenTime.Date =
                   aggregated(1).CloseTime.Date,
                   "결과 봉이 거래일 경계를 넘었습니다.")
        End Sub

        Private Sub VerifySameDayPrependInvariance()
            Dim tradingDay As DateTime = New DateTime(2026, 7, 31)
            Dim initial As List(Of CandleItem) =
                CreateCandles(tradingDay, 9, 2, 7, 100.0F, 10L)
            Dim expanded As New List(Of CandleItem)()

            expanded.AddRange(
                CreateCandles(tradingDay, 9, 0, 2, 98.0F, 8L))
            expanded.AddRange(CopyCandles(initial))

            Dim initialBars As List(Of CandleItem) =
                TickAggregator.Aggregate(initial, 30, 10)
            Dim expandedBars As List(Of CandleItem) =
                TickAggregator.Aggregate(expanded, 30, 10)

            Expect(initialBars.Count = 2,
                   "초기 데이터의 결과 봉 수가 잘못되었습니다.")
            Expect(expandedBars.Count = 3,
                   "과거 데이터 추가 후 결과 봉 수가 잘못되었습니다.")

            CompareBar(initialBars(0), expandedBars(1),
                       "같은 거래일 과거 prepend 후 첫 기존 봉")
            CompareBar(initialBars(1), expandedBars(2),
                       "같은 거래일 과거 prepend 후 둘째 기존 봉")
        End Sub

        Private Sub VerifyPreviousDayPrependInvariance()
            Dim previousDay As DateTime = New DateTime(2026, 7, 30)
            Dim currentDay As DateTime = New DateTime(2026, 7, 31)
            Dim current As List(Of CandleItem) =
                CreateCandles(currentDay, 9, 0, 6, 300.0F, 100L)
            Dim expanded As New List(Of CandleItem)()

            expanded.AddRange(
                CreateCandles(previousDay, 15, 0, 4, 200.0F, 0L))
            expanded.AddRange(CopyCandles(current))

            Dim currentBars As List(Of CandleItem) =
                TickAggregator.Aggregate(current, 30, 10)
            Dim expandedBars As List(Of CandleItem) =
                TickAggregator.Aggregate(expanded, 30, 10)
            Dim expandedCurrent As New List(Of CandleItem)()

            For Each item As CandleItem In expandedBars
                If item.TradingDate = currentDay.Date Then
                    expandedCurrent.Add(item)
                End If
            Next

            Expect(expandedCurrent.Count = currentBars.Count,
                   "이전 거래일 prepend 후 현재 거래일 봉 수가 변했습니다.")

            For index As Integer = 0 To currentBars.Count - 1
                CompareBar(currentBars(index), expandedCurrent(index),
                           $"이전 거래일 prepend 후 현재 봉 {index}")
            Next
        End Sub

        Private Sub VerifyMetadataAndSourceIsolation()
            Dim tradingDay As DateTime = New DateTime(2026, 7, 31)
            Dim source As List(Of CandleItem) =
                CreateCandles(tradingDay, 9, 0, 3, 400.0F, 500L)

            source(1).High = 450.0F
            source(1).Low = 390.0F
            source(2).IsFinal = False

            Dim aggregated As List(Of CandleItem) =
                TickAggregator.Aggregate(source, 30, 10)

            Expect(aggregated.Count = 1,
                   "메타데이터 검증용 결과 봉 수가 잘못되었습니다.")

            Dim result As CandleItem = aggregated(0)

            Expect(result.OpenTime = source(0).EffectiveOpenTime,
                   "결과 봉의 OpenTime이 첫 원본 봉과 다릅니다.")
            Expect(result.CloseTime = source(2).EffectiveCloseTime,
                   "결과 봉의 CloseTime이 마지막 원본 봉과 다릅니다.")
            Expect(result.Sequence = source(2).Sequence,
                   "결과 봉의 Sequence가 마지막 원본 봉과 다릅니다.")
            Expect(Not result.IsFinal,
                   "미확정 원본 봉을 포함한 결과가 확정으로 표시되었습니다.")
            Expect(result.High = 450.0F AndAlso result.Low = 390.0F,
                   "고가 또는 저가 집계가 잘못되었습니다.")
            Expect(result.Volume = 6L,
                   "거래량 합계가 잘못되었습니다.")

            result.Close = 9999.0F
            Expect(source(2).Close <> result.Close,
                   "결과 봉이 원본 객체를 그대로 재사용했습니다.")
        End Sub

        Private Sub VerifyValidation()
            Expect(TickAggregator.ChooseBase(720) = 30,
                   "720틱의 base 선택이 잘못되었습니다.")
            Expect(TickAggregator.ChooseBase(3) = 1,
                   "3틱의 base 선택이 잘못되었습니다.")

            Dim tradingDay As DateTime = New DateTime(2026, 7, 31)
            Dim source As List(Of CandleItem) =
                CreateCandles(tradingDay, 9, 0, 3, 500.0F, 0L)

            ExpectThrows(Of ArgumentException)(
                Sub()
                    TickAggregator.Aggregate(source, 25, 10)
                End Sub,
                "정수 배수가 아닌 틱주기가 허용되었습니다.")

            Dim descending As New List(Of CandleItem) From {
                source(1).Copy(),
                source(0).Copy()
            }

            ExpectThrows(Of ArgumentException)(
                Sub()
                    TickAggregator.Aggregate(descending, 20, 10)
                End Sub,
                "시간 역순 입력이 허용되었습니다.")
        End Sub

        Private Function CreateCandles(tradingDay As DateTime,
                                       startHour As Integer,
                                       startMinute As Integer,
                                       count As Integer,
                                       startingPrice As Single,
                                       startingSequence As Long) As List(Of CandleItem)
            Dim output As New List(Of CandleItem)()
            Dim firstClose As DateTime =
                tradingDay.Date.AddHours(startHour).AddMinutes(startMinute)

            For index As Integer = 0 To count - 1
                Dim closeTime As DateTime = firstClose.AddMinutes(index)
                Dim price As Single = startingPrice + index

                output.Add(New CandleItem With {
                    .Dt = closeTime,
                    .Sequence = startingSequence + index,
                    .OpenTime = closeTime.AddSeconds(-30),
                    .CloseTime = closeTime,
                    .IsFinal = True,
                    .Open = price,
                    .High = price + 0.5F,
                    .Low = price - 0.5F,
                    .Close = price,
                    .Volume = index + 1L
                })
            Next

            Return output
        End Function

        Private Function CopyCandles(source As List(Of CandleItem)) As List(Of CandleItem)
            Dim output As New List(Of CandleItem)()

            For Each item As CandleItem In source
                output.Add(item.Copy())
            Next

            Return output
        End Function

        Private Sub CompareBar(expected As CandleItem,
                               actual As CandleItem,
                               description As String)
            Expect(expected.OpenTime = actual.OpenTime,
                   description & ": OpenTime 불일치")
            Expect(expected.CloseTime = actual.CloseTime,
                   description & ": CloseTime 불일치")
            Expect(expected.Open = actual.Open,
                   description & ": Open 불일치")
            Expect(expected.High = actual.High,
                   description & ": High 불일치")
            Expect(expected.Low = actual.Low,
                   description & ": Low 불일치")
            Expect(expected.Close = actual.Close,
                   description & ": Close 불일치")
            Expect(expected.Volume = actual.Volume,
                   description & ": Volume 불일치")
            Expect(expected.Sequence = actual.Sequence,
                   description & ": Sequence 불일치")
        End Sub

        Private Sub ExpectThrows(Of TException As Exception)(action As Action,
                                                              message As String)
            Try
                action()
            Catch ex As TException
                Return
            End Try

            Throw New InvalidOperationException(message)
        End Sub

        Private Sub Expect(condition As Boolean,
                           message As String)
            If Not condition Then
                Throw New InvalidOperationException(message)
            End If
        End Sub
    End Module
End Namespace
