Imports ChartKit.Models

Namespace Abstractions
    '' 모든 지표 공통 인터페이스. 원본 IIndicator 그대로.
    Public Interface IIndicator
        ReadOnly Property Name As String
        ReadOnly Property DisplayName As String
        ReadOnly Property PanelIndex As Integer   '' 0=오버레이, 1+=하단 패널
        Property Parameters As Dictionary(Of String, Object)

        Function Calculate(candles As IReadOnlyList(Of CandleItem)) As List(Of IndicatorResult)
        Function UpdateLast(candles As IReadOnlyList(Of CandleItem),
                            prevResults As List(Of IndicatorResult)) As IndicatorResult
    End Interface
End Namespace
