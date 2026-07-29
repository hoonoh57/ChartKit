Imports System.Linq
Imports ChartKit.Abstractions
Imports ChartKit.Models

Namespace Core
    '' 지표 등록/관리 + 전체/증분 계산. 원본 IndicatorEngine 그대로.
    Public Class IndicatorEngine
        Private ReadOnly _indicators As New List(Of IIndicator)
        Private ReadOnly _results As New Dictionary(Of String, List(Of IndicatorResult))

        Public ReadOnly Property Results As Dictionary(Of String, List(Of IndicatorResult))
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
            For Each ind In _indicators
                Try
                    _results(ind.Name) = ind.Calculate(candles)
                Catch ex As Exception
                    _results(ind.Name) = New List(Of IndicatorResult)
                End Try
            Next
        End Sub

        Public Sub UpdateLast(candles As IReadOnlyList(Of CandleItem))
            If candles Is Nothing OrElse candles.Count = 0 Then Return
            For Each ind In _indicators
                Try
                    Dim prevResults As List(Of IndicatorResult) = Nothing
                    _results.TryGetValue(ind.Name, prevResults)
                    Dim lastResult = ind.UpdateLast(candles, prevResults)
                    If prevResults Is Nothing Then
                        _results(ind.Name) = New List(Of IndicatorResult) From {lastResult}
                    ElseIf prevResults.Count = candles.Count Then
                        '' 진행 중인 마지막 봉 변경: 마지막 결과만 교체
                        prevResults(prevResults.Count - 1) = lastResult
                    ElseIf prevResults.Count = candles.Count - 1 Then
                        '' 새 봉 확정: 결과 한 건만 추가
                        prevResults.Add(lastResult)
                    Else
                        '' 데이터가 외부에서 대량 교체된 비정상 경로만 전체 재계산
                        _results(ind.Name) = ind.Calculate(candles)
                    End If
                Catch
                End Try
            Next
        End Sub

        Public Sub Clear()
            _indicators.Clear()
            _results.Clear()
        End Sub
    End Class
End Namespace
