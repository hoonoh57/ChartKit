Imports SkiaSharp
Imports ChartKit.Abstractions

Namespace Layers
    '' 십자선 + Y축 라벨 + 하단 시간 라벨. OHLCV 정보창은 LegendLayer가 담당.
    Public Class CrosshairLayer
        Implements IChartLayer

        Public ReadOnly Property Id As String Implements IChartLayer.Id
            Get
                Return "Crosshair"
            End Get
        End Property
        Public Property IsVisible As Boolean = True Implements IChartLayer.IsVisible
        Public ReadOnly Property ZOrder As Integer Implements IChartLayer.ZOrder
            Get
                Return 900
            End Get
        End Property

        Private ReadOnly _paintLine As SKPaint
        Private ReadOnly _paintLabel As SKPaint
        Private ReadOnly _paintText As SKPaint

        Public Sub New()
            _paintLine = New SKPaint With {
                .Style = SKPaintStyle.Stroke, .StrokeWidth = 1, .IsAntialias = False,
                .PathEffect = SKPathEffect.CreateDash({4, 3}, 0)}
            _paintLabel = New SKPaint With {.Style = SKPaintStyle.Fill, .IsAntialias = False}
            _paintText = New SKPaint With {
                .IsAntialias = True, .Typeface = SKTypeface.FromFamilyName("Consolas")}
        End Sub

        Public Sub Draw(canvas As SKCanvas, ctx As ChartContext) Implements IChartLayer.Draw
            If Not ctx.MouseInside OrElse Not ctx.ShowCrosshair Then Return
            If ctx.Candles Is Nothing OrElse ctx.Candles.Count = 0 Then Return

            _paintLine.Color = ctx.Theme.Crosshair
            _paintLabel.Color = ctx.Theme.CrosshairLabel
            _paintText.Color = ctx.Theme.CrosshairText
            _paintText.TextSize = ctx.Theme.AxisFontSize

            Dim mainRect = ctx.MainRect
            Dim volumeRect = ctx.VolumeRect
            Dim marginBottom = ctx.Theme.MarginBottom
            Dim labelH = ctx.Theme.CrosshairLabelHeight
            Dim mx = ctx.CrosshairX
            Dim my = ctx.CrosshairY

            If mx < mainRect.Left OrElse mx > mainRect.Right Then Return
            If my < mainRect.Top OrElse my > ctx.TotalHeight - marginBottom Then Return

            canvas.DrawLine(mx, mainRect.Top, mx, ctx.TotalHeight - marginBottom, _paintLine)

            If my <= mainRect.Bottom Then
                canvas.DrawLine(mainRect.Left, my, mainRect.Right, my, _paintLine)
                DrawCrosshairYLabel(canvas, ctx, mainRect.Right, my, FormatAxisPrice(ctx.Mapper.YToPrice(my)))
            ElseIf my <= volumeRect.Bottom Then
                canvas.DrawLine(volumeRect.Left, my, volumeRect.Right, my, _paintLine)
                DrawCrosshairYLabel(canvas, ctx, volumeRect.Right, my, FormatAxisPrice(ctx.Mapper.YToVolume(my)))
            Else
                '' ── 서브패널 영역 ──
                Dim slot = FindPanelSlot(ctx, my)
                If slot >= 0 Then
                    Dim rect = ctx.PanelRects(slot)
                    canvas.DrawLine(rect.Left, my, rect.Right, my, _paintLine)
                    Dim vmin As Single, vmax As Single
                    If GetPanelRange(ctx, slot, vmin, vmax) Then
                        Dim t = (rect.Bottom - my) / rect.Height
                        Dim val = vmin + t * (vmax - vmin)
                        '' ★ 라벨을 서브패널 오른쪽 축에 그림
                        DrawCrosshairYLabel(canvas, ctx, rect.Right, my, val.ToString("N1"))
                    End If
                End If
            End If

            Dim idx = ctx.Mapper.XToIndex(mx)
            If idx >= 0 AndAlso idx < ctx.Candles.Count Then
                Dim c = ctx.Candles(idx)
                Dim timeTxt = c.Dt.ToString("MM/dd HH:mm")
                Dim ttw = _paintText.MeasureText(timeTxt)
                Dim tly = ctx.TotalHeight - marginBottom
                canvas.DrawRect(mx - ttw / 2 - 5, tly, ttw + 10, labelH, _paintLabel)
                canvas.DrawText(timeTxt, mx - ttw / 2, tly + 14, _paintText)
            End If
        End Sub

        Private Shared Function FindPanelSlot(ctx As ChartContext, y As Single) As Integer
            If ctx.PanelRects Is Nothing Then Return -1
            For k = 0 To ctx.PanelRects.Count - 1
                Dim r = ctx.PanelRects(k)
                If y >= r.Top AndAlso y <= r.Bottom Then Return k
            Next
            Return -1
        End Function

        Private Shared Function GetPanelRange(ctx As ChartContext, slot As Integer, ByRef vmin As Single, ByRef vmax As Single) As Boolean
            vmin = Single.MaxValue : vmax = Single.MinValue
            If ctx.Engine Is Nothing Then Return False

            Dim panelIdxs As New List(Of Integer)()
            For Each ind In ctx.Engine.GetAll()
                If ind.PanelIndex > 0 AndAlso Not panelIdxs.Contains(ind.PanelIndex) Then panelIdxs.Add(ind.PanelIndex)
            Next
            panelIdxs.Sort()
            If slot < 0 OrElse slot >= panelIdxs.Count Then Return False
            Dim panelIndex = panelIdxs(slot)

            Dim s = Math.Max(0, ctx.StartIndex)
            Dim en = Math.Min(ctx.Candles.Count - 1, ctx.EndIndex)
            For Each ind In ctx.Engine.GetAll()
                If ind.PanelIndex <> panelIndex Then Continue For
                Dim results As List(Of IndicatorResult) = Nothing
                If Not ctx.Engine.Results.TryGetValue(ind.Name, results) Then Continue For
                If results Is Nothing Then Continue For
                Dim maxI = Math.Min(en, results.Count - 1)
                For i = s To maxI
                    Dim r = results(i)
                    If r Is Nothing OrElse r.Values Is Nothing Then Continue For
                    For Each kv In r.Values
                        Dim v = kv.Value
                        If Single.IsNaN(v) Then Continue For
                        Dim ku = kv.Key.ToUpperInvariant()
                        If ku = "DIRECTION" OrElse ku = "HIST" OrElse ku = "HISTOGRAM" OrElse ku = "MA" OrElse ku = "ATR" OrElse ku = "SLOPE" Then Continue For
                        If v < vmin Then vmin = v
                        If v > vmax Then vmax = v
                    Next
                Next
            Next
            If vmin = Single.MaxValue OrElse vmax = Single.MinValue Then Return False
            If vmax <= vmin Then vmax = vmin + 1
            Dim pad = (vmax - vmin) * 0.05F
            vmin -= pad : vmax += pad
            Return True
        End Function

        '' rightX = 라벨을 붙일 오른쪽 축 X (메인/볼륨/서브패널별로 다름)
        Private Sub DrawCrosshairYLabel(canvas As SKCanvas, ctx As ChartContext, rightX As Single, y As Single, text As String)
            Dim labelH = ctx.Theme.CrosshairLabelHeight
            Dim tw = _paintText.MeasureText(text)
            canvas.DrawRect(rightX, y - labelH / 2, tw + 10, labelH, _paintLabel)
            canvas.DrawText(text, rightX + 5, y + 4, _paintText)
        End Sub

        Private Shared Function FormatAxisPrice(price As Single) As String
            If price >= 1000 Then Return price.ToString("N0")
            If price >= 100 Then Return price.ToString("N1")
            Return price.ToString("N2")
        End Function
    End Class
End Namespace