Imports SkiaSharp
Imports ChartKit.Abstractions

Namespace Layers
    Public Class VolumeLayer
        Implements IChartLayer

        Public ReadOnly Property Id As String Implements IChartLayer.Id
            Get
                Return "Volume"
            End Get
        End Property
        Public Property IsVisible As Boolean = True Implements IChartLayer.IsVisible
        Public ReadOnly Property ZOrder As Integer Implements IChartLayer.ZOrder
            Get
                Return 10
            End Get
        End Property

        Private _bull As SKPaint, _bear As SKPaint
        Private _paintThemeVersion As Integer = -1

        Public Sub Draw(canvas As SKCanvas, ctx As ChartContext) Implements IChartLayer.Draw
            If _bull Is Nothing OrElse _paintThemeVersion <> ctx.Theme.Version Then
                _bull?.Dispose()
                _bear?.Dispose()
                _bull = New SKPaint With {.Style = SKPaintStyle.Fill, .Color = ctx.Theme.BullVolume, .IsAntialias = False}
                _bear = New SKPaint With {.Style = SKPaintStyle.Fill, .Color = ctx.Theme.BearVolume, .IsAntialias = False}
                _paintThemeVersion = ctx.Theme.Version
            End If
            Dim r = ctx.VisibleRange()
            For i As Integer = r.Item1 To r.Item2
                Dim c = ctx.Candles(i)
                Dim x = ctx.Mapper.IndexToX(i)
                Dim halfW = ctx.CandleWidth / 2 - 0.5F
                Dim yTop = ctx.Mapper.VolumeToY(c.Volume)
                Dim yBot = ctx.VolumeRect.Bottom
                canvas.DrawRect(x - halfW, yTop, ctx.CandleWidth - 1, yBot - yTop,
                                If(c.Close >= c.Open, _bull, _bear))
            Next
        End Sub

        Public Sub Dispose() Implements IDisposable.Dispose
            _bull?.Dispose() : _bull = Nothing
            _bear?.Dispose() : _bear = Nothing
        End Sub
    End Class
End Namespace
