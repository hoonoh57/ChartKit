Imports System.Linq
Imports ChartKit.Abstractions
Imports ChartKit.Models

Namespace Core
    '' 지표 등록/관리 + 고정 용량 결과 링버퍼.
    Public Class IndicatorEngine
        Private Const DefaultResultCapacity As Integer = 100000
        Private ReadOnly _indicators As New List(Of IIndicator)
        Private ReadOnly _results As New Dictionary(Of String, IndicatorResultRingBuffer)

        Public ReadOnly Property Results As Dictionary(Of String, IndicatorResultRingBuffer)
            Get
                Return _results
            End Get
        End Property

        Public Sub Register(ind As IIndicator)
            If ind Is Nothing Then Return
            If _indicators.Any(Function(x) x.Name = ind.Name) Then Return
            _indicators.Add(ind)
        End Sub

        Public Sub Remove(name As String)
            Dim found = _indicators.FirstOrDefault(Function(x) x.Name = name)
            If found IsNot Nothing Then
                _indicators.Remove(found)
                _results.Remove(name)
            End If
        End Sub

        Public Function GetAll() As List(Of IIndicator)
            Return _indicators.ToList()
        End Function

        Public Sub CalculateAll(candles As IReadOnlyList(Of CandleItem))
            If candles Is Nothing OrElse candles.Count = 0 Then Return
            Dim capacity = ResultCapacity(candles)
            Dim firstSequence = CandleFirstSequence(candles)
            For Each ind In _indicators
                Try
                    Dim calculated = ind.Calculate(candles)
                    Dim buffer As New IndicatorResultRingBuffer(capacity)
                    For i = 0 To calculated.Count - 1
                        calculated(i).Sequence = firstSequence + i
                        buffer.Add(calculated(i))
                    Next
                    _results(ind.Name) = buffer
                Catch ex As Exception
                    ChartLog.Error($"지표 전체 계산 실패: {ind.Name}", ex)
                    _results(ind.Name) = New IndicatorResultRingBuffer(capacity)
                End Try
            Next
        End Sub

        Public Sub UpdateLast(candles As IReadOnlyList(Of CandleItem))
            If candles Is Nothing OrElse candles.Count = 0 Then Return
            Dim candleSequence = CandleLastSequence(candles)
            For Each ind In _indicators
                Try
                    Dim previous As IndicatorResultRingBuffer = Nothing
                    _results.TryGetValue(ind.Name, previous)
                    Dim lastResult = ind.UpdateLast(candles, previous)
                    lastResult.Sequence = candleSequence

                    If previous Is Nothing OrElse previous.Count = 0 Then
                        Dim created As New IndicatorResultRingBuffer(ResultCapacity(candles))
                        created.Add(lastResult)
                        _results(ind.Name) = created
                    ElseIf previous(previous.Count - 1).Sequence = candleSequence Then
                        '' 동일 미확정 봉: 마지막 슬롯만 O(1) 교체
                        previous(previous.Count - 1) = lastResult
                    ElseIf previous(previous.Count - 1).Sequence + 1 = candleSequence Then
                        '' 새 봉: 가득 찼으면 결과 링버퍼도 선두를 O(1) 퇴출
                        previous.Add(lastResult)
                    Else
                        CalculateOne(ind, candles)
                    End If
                Catch ex As Exception
                    ChartLog.Error($"지표 증분 계산 실패: {ind.Name}", ex)
                End Try
            Next
        End Sub

        Private Sub CalculateOne(ind As IIndicator, candles As IReadOnlyList(Of CandleItem))
            Dim calculated = ind.Calculate(candles)
            Dim buffer As New IndicatorResultRingBuffer(ResultCapacity(candles))
            Dim firstSequence = CandleFirstSequence(candles)
            For i = 0 To calculated.Count - 1
                calculated(i).Sequence = firstSequence + i
                buffer.Add(calculated(i))
            Next
            _results(ind.Name) = buffer
        End Sub

        Private Shared Function ResultCapacity(candles As IReadOnlyList(Of CandleItem)) As Integer
            Dim ring = TryCast(candles, CandleRingBuffer)
            Return If(ring Is Nothing, Math.Max(DefaultResultCapacity, candles.Count), ring.Capacity)
        End Function

        Private Shared Function CandleFirstSequence(candles As IReadOnlyList(Of CandleItem)) As Long
            Dim ring = TryCast(candles, CandleRingBuffer)
            Return If(ring Is Nothing, 0L, ring.FirstSequence)
        End Function

        Private Shared Function CandleLastSequence(candles As IReadOnlyList(Of CandleItem)) As Long
            Dim ring = TryCast(candles, CandleRingBuffer)
            Return If(ring Is Nothing, candles.Count - 1L, ring.LastSequence)
        End Function

        Public Sub Clear()
            _indicators.Clear()
            _results.Clear()
        End Sub
    End Class
End Namespace
