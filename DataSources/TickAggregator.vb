Option Strict On
Option Explicit On
Option Infer Off

Imports System
Imports System.Collections.Generic
Imports ChartKit.Models

Namespace DataSources

    '' 원본(base) 틱봉을 목표 틱주기로 재집계한다.
    ''
    '' 정렬/위상 원칙:
    '' - 입력은 시간 오름차순이어야 한다.
    '' - 거래일별로 완전히 분리하여 어떤 결과 봉도 날짜 경계를 넘지 않는다.
    '' - 각 거래일의 최신 봉을 기준으로 역방향 정렬한다.
    ''   따라서 같은 거래일의 더 오래된 페이지를 앞에 추가해도 이미 계산된
    ''   최신 구간의 봉 경계는 바뀌지 않는다.
    '' - 거래일 앞쪽의 groupSize 미만 자투리는 버린다.
    ''
    '' 이 모듈은 현재 Kiwoom 연속조회 구조의 pagination 안정성을 우선한다.
    '' 세션 시작 기준 위상은 누적 체결 순번 또는 해당 거래일 전체 원본이
    '' 확보된 경우에만 별도 정책으로 도입해야 한다.
    Public Module TickAggregator

        '' 키움이 원본으로 제공하는 틱봉 base 후보 (내림차순)
        Private ReadOnly BaseCandidates As Integer() = {30, 10, 5, 1}

        '' 목표 틱수에 대해 무손실 재집계 가능한 최대 base를 선택한다.
        Public Function ChooseBase(targetTicks As Integer) As Integer
            If targetTicks <= 0 Then
                Throw New ArgumentOutOfRangeException(
                    NameOf(targetTicks),
                    "목표 틱수는 1 이상이어야 합니다.")
            End If

            For Each candidate As Integer In BaseCandidates
                If targetTicks Mod candidate = 0 Then Return candidate
            Next

            Return 1
        End Function

        '' base 틱봉(baseTicks 단위)을 목표(targetTicks) 틱봉으로 묶는다.
        Public Function Aggregate(baseCandles As List(Of CandleItem),
                                  targetTicks As Integer,
                                  baseTicks As Integer) As List(Of CandleItem)
            ValidateArguments(baseCandles, targetTicks, baseTicks)

            Dim output As New List(Of CandleItem)()
            If baseCandles Is Nothing OrElse baseCandles.Count = 0 Then Return output

            ValidateAscending(baseCandles)

            Dim groupSize As Integer = targetTicks \ baseTicks
            If groupSize = 1 Then
                For Each candle As CandleItem In baseCandles
                    output.Add(candle.Copy())
                Next
                Return output
            End If

            Dim dayStart As Integer = 0
            Dim index As Integer = 1

            Do While index <= baseCandles.Count
                Dim reachedEnd As Boolean = index = baseCandles.Count
                Dim changedTradingDate As Boolean = False

                If Not reachedEnd Then
                    changedTradingDate =
                        baseCandles(index).TradingDate <>
                        baseCandles(dayStart).TradingDate
                End If

                If reachedEnd OrElse changedTradingDate Then
                    AggregateTradingDay(
                        baseCandles,
                        dayStart,
                        index - 1,
                        groupSize,
                        output)
                    dayStart = index
                End If

                index += 1
            Loop

            Return output
        End Function

        Private Sub ValidateArguments(baseCandles As List(Of CandleItem),
                                      targetTicks As Integer,
                                      baseTicks As Integer)
            If targetTicks <= 0 Then
                Throw New ArgumentOutOfRangeException(
                    NameOf(targetTicks),
                    "목표 틱수는 1 이상이어야 합니다.")
            End If

            If baseTicks <= 0 Then
                Throw New ArgumentOutOfRangeException(
                    NameOf(baseTicks),
                    "원본 틱수는 1 이상이어야 합니다.")
            End If

            If targetTicks Mod baseTicks <> 0 Then
                Throw New ArgumentException(
                    "목표 틱수는 원본 틱수의 정수 배수여야 합니다.",
                    NameOf(targetTicks))
            End If

            If baseCandles Is Nothing Then Return

            For index As Integer = 0 To baseCandles.Count - 1
                If baseCandles(index) Is Nothing Then
                    Throw New ArgumentException(
                        $"원본 틱봉 {index}번 항목이 Nothing입니다.",
                        NameOf(baseCandles))
                End If
            Next
        End Sub

        Private Sub ValidateAscending(baseCandles As List(Of CandleItem))
            Dim previousTime As DateTime = baseCandles(0).EffectiveCloseTime

            For index As Integer = 1 To baseCandles.Count - 1
                Dim currentTime As DateTime =
                    baseCandles(index).EffectiveCloseTime

                If currentTime < previousTime Then
                    Throw New ArgumentException(
                        "원본 틱봉은 시간 오름차순이어야 합니다.",
                        NameOf(baseCandles))
                End If

                previousTime = currentTime
            Next
        End Sub

        Private Sub AggregateTradingDay(source As List(Of CandleItem),
                                        dayStart As Integer,
                                        dayEnd As Integer,
                                        groupSize As Integer,
                                        output As List(Of CandleItem))
            Dim newestFirst As New List(Of CandleItem)()
            Dim groupEnd As Integer = dayEnd

            Do While groupEnd - groupSize + 1 >= dayStart
                Dim groupStart As Integer = groupEnd - groupSize + 1
                newestFirst.Add(BuildBar(source, groupStart, groupEnd))
                groupEnd -= groupSize
            Loop

            newestFirst.Reverse()
            output.AddRange(newestFirst)
        End Sub

        '' [startIndex..endIndex] 원본 봉들을 하나의 목표 봉으로 합친다.
        Private Function BuildBar(source As List(Of CandleItem),
                                  startIndex As Integer,
                                  endIndex As Integer) As CandleItem
            Dim first As CandleItem = source(startIndex)
            Dim last As CandleItem = source(endIndex)
            Dim highValue As Single = first.High
            Dim lowValue As Single = first.Low
            Dim totalVolume As Long = 0L
            Dim allFinal As Boolean = True

            For index As Integer = startIndex To endIndex
                Dim item As CandleItem = source(index)

                If item.High > highValue Then highValue = item.High
                If item.Low < lowValue Then lowValue = item.Low
                totalVolume += item.Volume
                If Not item.IsFinal Then allFinal = False
            Next

            Dim closeTime As DateTime = last.EffectiveCloseTime

            Return New CandleItem With {
                .Dt = closeTime,
                .Sequence = last.Sequence,
                .OpenTime = first.EffectiveOpenTime,
                .CloseTime = closeTime,
                .IsFinal = allFinal,
                .Open = first.Open,
                .High = highValue,
                .Low = lowValue,
                .Close = last.Close,
                .Volume = totalVolume
            }
        End Function

    End Module

End Namespace
