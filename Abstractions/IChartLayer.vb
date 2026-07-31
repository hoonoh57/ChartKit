Imports SkiaSharp

Namespace Abstractions
    '' 그릴 수 있는 모든 요소(캔들/거래량/지표/신호 등)의 공통 계약.
    Public Interface IChartLayer
        Inherits IDisposable
        ReadOnly Property Id As String
        Property IsVisible As Boolean
        ReadOnly Property ZOrder As Integer
        Sub Draw(canvas As SKCanvas, ctx As ChartContext)
    End Interface
End Namespace
