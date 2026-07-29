Imports SkiaSharp

Namespace Abstractions
    '' 색상/여백 테마. 향후 테마 교체 대비하여 객체로 유지.
    Public Class ChartTheme
        Public Property MarginLeft As Single = 10
        Public Property MarginRight As Single = 80
        Public Property MarginTop As Single = 6
        Public Property MarginBottom As Single = 24
        Public Property VolumeRatio As Single = 0.15F
        Public Property AxisFontSize As Single = 11

        Public Property Background As SKColor = New SKColor(24, 26, 32)
        Public Property Grid As SKColor = New SKColor(40, 44, 52)
        Public Property AxisText As SKColor = New SKColor(140, 148, 160)
        Public Property BullCandle As SKColor = New SKColor(234, 57, 67)
        Public Property BearCandle As SKColor = New SKColor(46, 134, 222)
        Public Property BullVolume As SKColor = New SKColor(234, 57, 67, 90)
        Public Property BearVolume As SKColor = New SKColor(46, 134, 222, 90)

        '' ── 크로스헤어 (원본 ColCrosshair/ColCrosshairLabel/ColCrosshairText, CROSSHAIR_LABEL_H) ──
        Public Property Crosshair As SKColor = New SKColor(100, 110, 130, 180)
        Public Property CrosshairLabel As SKColor = New SKColor(55, 60, 72)
        Public Property CrosshairText As SKColor = New SKColor(220, 225, 235)
        Public Property CrosshairLabelHeight As Single = 18

        Public Shared Function CreateDefault() As ChartTheme
            Return New ChartTheme()
        End Function
    End Class
End Namespace