Imports SkiaSharp
Imports ChartKit.Core
Imports ChartKit.Models

Namespace Abstractions
    Public Class ChartContext
        Public Property Candles As IReadOnlyList(Of CandleItem)
        Public Property Mapper As CoordinateMapper
        Public Property Theme As ChartTheme
        Public Property MainRect As SKRect
        Public Property VolumeRect As SKRect
        Public Property CandleWidth As Single
        Public Property StartIndex As Integer
        Public Property EndIndex As Integer
        Public Property TotalWidth As Single
        Public Property TotalHeight As Single
        Public Property PriceHigh As Single
        Public Property PriceLow As Single
        '' 좌측 등락률축 모드: 0=끄기, 1=전일종가대비, 2=시가대비
        Public Property PctAxisMode As Integer = 0
        Public Property VolumeMax As Long
        Public Property ShowDayChangeLines As Boolean = True
        Public Property Engine As ChartKit.Core.IndicatorEngine

        '' ── 사용자 편집 기준선: key=서브패널 인덱스(0=첫 서브패널, PanelIndex-1), value=기준선 값 목록 ──
        Public Property PanelBaselines As System.Collections.Generic.Dictionary(Of Integer, System.Collections.Generic.List(Of Single))
        '' 과열/침체 음영: key=서브패널 인덱스 → (과열값?, 침체값?)
        Public Property PanelZones As System.Collections.Generic.Dictionary(Of Integer, ChartKit.Core.PanelZoneState)
        '' 오버레이 배경 음영 규칙 (A>=B 구간)
        Public Property ShadeRules As System.Collections.Generic.List(Of ChartKit.Core.OverlayShadeRule)
        Public Property SignalRules As System.Collections.Generic.List(Of ChartKit.Core.SignalRule)

        '' ── 서브패널 영역 (PanelIndex>0 지표용, 인덱스 0=첫 서브패널) ──
        Public Property PanelRects As System.Collections.Generic.List(Of SkiaSharp.SKRect)
        '' ── 크로스헤어 상태 (원본 _vs.CrosshairX/Y, _mouseInside, _vs.ShowCrosshair) ──
        Public Property MouseInside As Boolean = False
        Public Property ShowCrosshair As Boolean = True
        Public Property CrosshairX As Single = 0
        Public Property CrosshairY As Single = 0

        Public Function VisibleRange() As ValueTuple(Of Integer, Integer)
            Dim s = Math.Max(0, StartIndex)
            Dim e = Math.Min(Candles.Count - 1, EndIndex)
            Return New ValueTuple(Of Integer, Integer)(s, e)
        End Function
    End Class
End Namespace