Imports ChartKit.Abstractions
Imports ChartKit.Models

Namespace Indicators
    ''' <summary>
    ''' 진행 중인 마지막 봉의 반복 갱신과 새 봉 추가를 구분하는 증분 지표 기반 클래스.
    ''' 파생 클래스는 Calculate 완료 시 InitializeIncrementalIndex를 호출하고,
    ''' SaveState/RestoreState에 "마지막 봉 직전" 상태를 보관해야 한다.
    ''' </summary>
    Public MustInherit Class IncrementalIndicatorBase
        Implements IIndicator

        Private _committedSequence As Long = -1

        Public MustOverride ReadOnly Property Name As String Implements IIndicator.Name
        Public MustOverride ReadOnly Property DisplayName As String Implements IIndicator.DisplayName
        Public MustOverride ReadOnly Property PanelIndex As Integer Implements IIndicator.PanelIndex
        Public MustOverride Property Parameters As Dictionary(Of String, Object) Implements IIndicator.Parameters
        Public MustOverride Function Calculate(candles As IReadOnlyList(Of CandleItem)) As List(Of IndicatorResult) Implements IIndicator.Calculate

        Protected Sub InitializeIncrementalIndex(index As Integer)
            _committedSequence = index
        End Sub

        Protected Sub ResetIncrementalIndex()
            _committedSequence = -1
        End Sub

        Protected Sub InitializeIncrementalSequence(sequence As Long)
            _committedSequence = sequence
        End Sub

        Protected MustOverride Sub SaveState()
        Protected MustOverride Sub RestoreState()
        Protected MustOverride Function StepCandle(candle As CandleItem, index As Integer) As IndicatorResult

        Public Function UpdateLast(candles As IReadOnlyList(Of CandleItem),
                                   prevResults As IReadOnlyList(Of IndicatorResult)) As IndicatorResult Implements IIndicator.UpdateLast
            If candles Is Nothing OrElse candles.Count = 0 Then
                Throw New ArgumentException("증분 계산에는 최소 한 개의 캔들이 필요합니다.", NameOf(candles))
            End If

            Dim index = candles.Count - 1
            Dim ring = TryCast(candles, CandleRingBuffer)
            Dim sequence = If(ring Is Nothing, CLng(index), ring.LastSequence)
            If sequence = _committedSequence Then
                RestoreState()
            ElseIf sequence = _committedSequence + 1 Then
                SaveState()
            Else
                Dim rebuilt = Calculate(candles)
                Return rebuilt(rebuilt.Count - 1)
            End If

            Dim result = StepCandle(candles(index), index)
            _committedSequence = sequence
            Return result
        End Function
    End Class
End Namespace
