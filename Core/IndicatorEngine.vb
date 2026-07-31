Option Strict On
Option Explicit On
Option Infer Off

Imports System.Linq
Imports ChartKit.Abstractions
Imports ChartKit.Models

Namespace Core
    '' 지표 등록/관리 + 고정 용량 결과 링버퍼.
    '' 결과의 정식 키는 파라미터까지 포함한 InstanceId다.
    '' 기존 저장 상태의 Name은 동일 이름 지표가 하나뿐일 때만 별칭으로 제공한다.
    Public Class IndicatorEngine
        Private Const DefaultResultCapacity As Integer = 100000
        Private ReadOnly _indicators As New List(Of RegisteredIndicator)()
        Private ReadOnly _canonicalResults As New Dictionary(Of String, IndicatorResultRingBuffer)(StringComparer.Ordinal)
        Private ReadOnly _results As New Dictionary(Of String, IndicatorResultRingBuffer)(StringComparer.Ordinal)
        Private _indicatorSnapshot As IIndicator() = Array.Empty(Of IIndicator)()

        Public ReadOnly Property Results As Dictionary(Of String, IndicatorResultRingBuffer)
            Get
                Return _results
            End Get
        End Property

        Public Sub Register(ind As IIndicator)
            If ind Is Nothing Then Return

            Dim registration As RegisteredIndicator = TryCast(ind, RegisteredIndicator)
            If registration Is Nothing Then registration = New RegisteredIndicator(ind)

            Dim instanceId As String = registration.InstanceId
            If _indicators.Any(
                Function(existing) String.Equals(
                    existing.RegisteredInstanceId,
                    instanceId,
                    StringComparison.Ordinal) OrElse
                String.Equals(
                    existing.InstanceId,
                    instanceId,
                    StringComparison.Ordinal)) Then Return

            registration.MarkRegistered()
            _indicators.Add(registration)
            RefreshIndicatorSnapshot()
            RebuildPublishedResults()
        End Sub

        Public Sub Remove(reference As String)
            If String.IsNullOrWhiteSpace(reference) Then Return

            Dim found As RegisteredIndicator = FindRegistration(reference)
            If found Is Nothing Then Return

            _indicators.Remove(found)
            _canonicalResults.Remove(found.RegisteredInstanceId)
            If Not String.Equals(
                found.RegisteredInstanceId,
                found.InstanceId,
                StringComparison.Ordinal) Then
                _canonicalResults.Remove(found.InstanceId)
            End If
            RefreshIndicatorSnapshot()
            RebuildPublishedResults()
        End Sub

        '' 기존 호출자는 독립 복사본을 받는다.
        Public Function GetAll() As List(Of IIndicator)
            Return New List(Of IIndicator)(_indicatorSnapshot)
        End Function

        '' 렌더링 등 읽기 전용 고빈도 경로는 등록 변경 전까지 같은 snapshot을 재사용한다.
        Public Function GetAllView() As IReadOnlyList(Of IIndicator)
            Return _indicatorSnapshot
        End Function

        Public Sub CalculateAll(candles As IReadOnlyList(Of CandleItem))
            If candles Is Nothing OrElse candles.Count = 0 Then Return

            SynchronizeRegistrations()
            _canonicalResults.Clear()

            Dim capacity As Integer = ResultCapacity(candles)
            Dim firstSequence As Long = CandleFirstSequence(candles)

            For Each ind As RegisteredIndicator In _indicators
                Dim key As String = ind.RegisteredInstanceId
                Try
                    Dim calculated As List(Of IndicatorResult) = ind.Calculate(candles)
                    Dim buffer As New IndicatorResultRingBuffer(capacity)
                    For index As Integer = 0 To calculated.Count - 1
                        calculated(index).Sequence = firstSequence + index
                        buffer.Add(calculated(index))
                    Next
                    _canonicalResults(key) = buffer
                Catch ex As Exception
                    ChartLog.Error(
                        $"지표 전체 계산 실패: {ind.DisplayName} [{key}]",
                        ex)
                    _canonicalResults(key) = New IndicatorResultRingBuffer(capacity)
                End Try
            Next

            RebuildPublishedResults()
        End Sub

        Public Sub UpdateLast(candles As IReadOnlyList(Of CandleItem))
            If candles Is Nothing OrElse candles.Count = 0 Then Return

            SynchronizeRegistrations()
            Dim candleSequence As Long = CandleLastSequence(candles)

            For Each ind As RegisteredIndicator In _indicators
                Dim key As String = ind.RegisteredInstanceId
                Try
                    Dim previous As IndicatorResultRingBuffer = Nothing
                    _canonicalResults.TryGetValue(key, previous)

                    Dim lastResult As IndicatorResult = ind.UpdateLast(candles, previous)
                    lastResult.Sequence = candleSequence

                    If previous Is Nothing OrElse previous.Count = 0 Then
                        Dim created As New IndicatorResultRingBuffer(ResultCapacity(candles))
                        created.Add(lastResult)
                        _canonicalResults(key) = created
                    ElseIf previous(previous.Count - 1).Sequence = candleSequence Then
                        '' 동일 미확정 봉: 마지막 슬롯만 O(1) 교체
                        previous(previous.Count - 1) = lastResult
                    ElseIf previous(previous.Count - 1).Sequence + 1L = candleSequence Then
                        '' 새 봉: 가득 찼으면 결과 링버퍼도 선두를 O(1) 퇴출
                        previous.Add(lastResult)
                    Else
                        CalculateOne(ind, candles)
                    End If
                Catch ex As Exception
                    ChartLog.Error(
                        $"지표 증분 계산 실패: {ind.DisplayName} [{key}]",
                        ex)
                End Try
            Next

            RebuildPublishedResults()
        End Sub

        Private Sub CalculateOne(ind As RegisteredIndicator,
                                 candles As IReadOnlyList(Of CandleItem))
            Dim calculated As List(Of IndicatorResult) = ind.Calculate(candles)
            Dim buffer As New IndicatorResultRingBuffer(ResultCapacity(candles))
            Dim firstSequence As Long = CandleFirstSequence(candles)

            For index As Integer = 0 To calculated.Count - 1
                calculated(index).Sequence = firstSequence + index
                buffer.Add(calculated(index))
            Next

            _canonicalResults(ind.RegisteredInstanceId) = buffer
        End Sub

        Private Function FindRegistration(reference As String) As RegisteredIndicator
            Dim exactMatches As List(Of RegisteredIndicator) = _indicators.Where(
                Function(indicator) String.Equals(
                    indicator.RegisteredInstanceId,
                    reference,
                    StringComparison.Ordinal) OrElse
                String.Equals(
                    indicator.InstanceId,
                    reference,
                    StringComparison.Ordinal)).ToList()

            If exactMatches.Count = 1 Then Return exactMatches(0)
            If exactMatches.Count > 1 Then Return Nothing

            Dim legacyMatches As List(Of RegisteredIndicator) = _indicators.Where(
                Function(indicator) String.Equals(
                    indicator.LegacyName,
                    reference,
                    StringComparison.Ordinal)).ToList()

            If legacyMatches.Count = 1 Then Return legacyMatches(0)
            Return Nothing
        End Function

        Private Sub SynchronizeRegistrations()
            Dim currentIds As New HashSet(Of String)(StringComparer.Ordinal)

            For Each indicator As RegisteredIndicator In _indicators
                Dim currentId As String = indicator.InstanceId
                If Not currentIds.Add(currentId) Then
                    Throw New InvalidOperationException(
                        "동일한 지표 InstanceId가 중복 등록되었습니다: " & currentId)
                End If
            Next

            For Each indicator As RegisteredIndicator In _indicators
                Dim currentId As String = indicator.InstanceId
                If String.Equals(
                    indicator.RegisteredInstanceId,
                    currentId,
                    StringComparison.Ordinal) Then Continue For

                _canonicalResults.Remove(indicator.RegisteredInstanceId)
                indicator.MarkRegistered()
            Next
        End Sub

        Private Sub RefreshIndicatorSnapshot()
            If _indicators.Count = 0 Then
                _indicatorSnapshot = Array.Empty(Of IIndicator)()
                Return
            End If

            Dim snapshot(_indicators.Count - 1) As IIndicator
            For index As Integer = 0 To _indicators.Count - 1
                snapshot(index) = _indicators(index)
            Next
            _indicatorSnapshot = snapshot
        End Sub

        Private Sub RebuildPublishedResults()
            _results.Clear()

            For Each pair As KeyValuePair(Of String, IndicatorResultRingBuffer) In _canonicalResults
                _results(pair.Key) = pair.Value
            Next

            Dim aliases As New Dictionary(Of String, List(Of RegisteredIndicator))(StringComparer.Ordinal)
            For Each indicator As RegisteredIndicator In _indicators
                Dim aliasName As String = indicator.LegacyName
                If String.IsNullOrWhiteSpace(aliasName) Then Continue For

                Dim registrations As List(Of RegisteredIndicator) = Nothing
                If Not aliases.TryGetValue(aliasName, registrations) Then
                    registrations = New List(Of RegisteredIndicator)()
                    aliases(aliasName) = registrations
                End If
                registrations.Add(indicator)
            Next

            For Each aliasPair As KeyValuePair(Of String, List(Of RegisteredIndicator)) In aliases
                If aliasPair.Value.Count <> 1 Then Continue For
                If _results.ContainsKey(aliasPair.Key) Then Continue For

                Dim registration As RegisteredIndicator = aliasPair.Value(0)
                Dim buffer As IndicatorResultRingBuffer = Nothing
                If _canonicalResults.TryGetValue(
                    registration.RegisteredInstanceId,
                    buffer) Then
                    _results(aliasPair.Key) = buffer
                End If
            Next
        End Sub

        Private Shared Function ResultCapacity(candles As IReadOnlyList(Of CandleItem)) As Integer
            Dim ring As CandleRingBuffer = TryCast(candles, CandleRingBuffer)
            Return If(ring Is Nothing, Math.Max(DefaultResultCapacity, candles.Count), ring.Capacity)
        End Function

        Private Shared Function CandleFirstSequence(candles As IReadOnlyList(Of CandleItem)) As Long
            Dim ring As CandleRingBuffer = TryCast(candles, CandleRingBuffer)
            Return If(ring Is Nothing, 0L, ring.FirstSequence)
        End Function

        Private Shared Function CandleLastSequence(candles As IReadOnlyList(Of CandleItem)) As Long
            Dim ring As CandleRingBuffer = TryCast(candles, CandleRingBuffer)
            Return If(ring Is Nothing, candles.Count - 1L, ring.LastSequence)
        End Function

        Public Sub Clear()
            _indicators.Clear()
            _indicatorSnapshot = Array.Empty(Of IIndicator)()
            _canonicalResults.Clear()
            _results.Clear()
        End Sub
    End Class
End Namespace
