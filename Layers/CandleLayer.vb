Imports SkiaSharp
Imports ChartKit.Abstractions

Namespace Layers
    Public Class CandleLayer
        Implements IChartLayer

        Public ReadOnly Property Id As String Implements IChartLayer.Id
            Get
                Return "Candle"
            End Get
        End Property
        Public Property IsVisible As Boolean = True Implements IChartLayer.IsVisible
        Public ReadOnly Property ZOrder As Integer Implements IChartLayer.ZOrder
            Get
                Return 20
            End Get
        End Property

        Private _bullBody As SKPaint, _bearBody As SKPaint
        Private _bullWick As SKPaint, _bearWick As SKPaint
        Private _paintThemeVersion As Integer = -1

        Public Sub Draw(canvas As SKCanvas, ctx As ChartContext) Implements IChartLayer.Draw
            EnsurePaints(ctx.Theme)
            Dim r = ctx.VisibleRange()
            For i As Integer = r.Item1 To r.Item2
                Dim c = ctx.Candles(i)
                Dim x = ctx.Mapper.IndexToX(i)
                Dim halfW = ctx.CandleWidth / 2 - 0.5F
                Dim isBull = (c.Close >= c.Open)
                Dim bodyTop = ctx.Mapper.PriceToY(If(isBull, c.Close, c.Open))
                Dim bodyBot = ctx.Mapper.PriceToY(If(isBull, c.Open, c.Close))
                If bodyBot - bodyTop < 1 Then bodyBot = bodyTop + 1
                Dim wick = If(isBull, _bullWick, _bearWick)

                canvas.DrawLine(x, ctx.Mapper.PriceToY(c.High), x, ctx.Mapper.PriceToY(c.Low), wick)
                If ctx.CandleWidth >= 3 Then
                    canvas.DrawRect(x - halfW, bodyTop, ctx.CandleWidth - 1, bodyBot - bodyTop,
                                    If(isBull, _bullBody, _bearBody))
                Else
                    canvas.DrawLine(x, bodyTop, x, bodyBot, wick)
                End If
            Next
        End Sub

        Private Sub EnsurePaints(t As ChartTheme)
            If _bullBody IsNot Nothing AndAlso _paintThemeVersion = t.Version Then Return
            DisposePaints()
            _bullBody = New SKPaint With {.Style = SKPaintStyle.Fill, .Color = t.BullCandle, .IsAntialias = False}
            _bearBody = New SKPaint With {.Style = SKPaintStyle.Fill, .Color = t.BearCandle, .IsAntialias = False}
            _bullWick = New SKPaint With {.Style = SKPaintStyle.Stroke, .Color = t.BullCandle, .StrokeWidth = 1, .IsAntialias = False}
            _bearWick = New SKPaint With {.Style = SKPaintStyle.Stroke, .Color = t.BearCandle, .StrokeWidth = 1, .IsAntialias = False}
            _paintThemeVersion = t.Version
        End Sub

        Private Sub DisposePaints()
            _bullBody?.Dispose() : _bullBody = Nothing
            _bearBody?.Dispose() : _bearBody = Nothing
            _bullWick?.Dispose() : _bullWick = Nothing
            _bearWick?.Dispose() : _bearWick = Nothing
        End Sub

        Public Sub Dispose() Implements IDisposable.Dispose
            DisposePaints()
        End Sub
    End Class
End Namespace
