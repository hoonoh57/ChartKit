Imports SkiaSharp
Imports ChartKit.Abstractions

Namespace Layers
    '' 좌상단 레전드. OHLCV + 오버레이(PanelIndex=0) 지표값만.
    '' 서브패널 지표값은 PanelLayer가 각 패널 안에 표시.
    Public Class LegendLayer
        Implements IChartLayer

        Public Sub Dispose() Implements IDisposable.Dispose
            '' Draw 내부의 모든 Skia 객체는 Using 범위에서 즉시 폐기된다.
        End Sub

        '' IndicatorLayer 와 반드시 동일한 팔레트 (색 일치용)
        Private Shared ReadOnly IndicatorColors As SKColor() = {
            New SKColor(255, 193, 7), New SKColor(0, 188, 212),
            New SKColor(233, 30, 99), New SKColor(76, 175, 80),
            New SKColor(255, 152, 0), New SKColor(171, 71, 188),
            New SKColor(255, 255, 255), New SKColor(139, 195, 74)}

        Public ReadOnly Property Id As String Implements IChartLayer.Id
            Get
                Return "Legend"
            End Get
        End Property
        Public Property IsVisible As Boolean = True Implements IChartLayer.IsVisible
        Public ReadOnly Property ZOrder As Integer Implements IChartLayer.ZOrder
            Get
                Return 950
            End Get
        End Property

        Public Sub Draw(canvas As SKCanvas, ctx As ChartContext) Implements IChartLayer.Draw
            If ctx.Candles Is Nothing OrElse ctx.Candles.Count = 0 Then Return

            Dim idx As Integer = ctx.Candles.Count - 1
            If ctx.MouseInside AndAlso ctx.ShowCrosshair Then
                Dim mx = ctx.CrosshairX
                If mx >= ctx.MainRect.Left AndAlso mx <= ctx.MainRect.Right Then
                    Dim hoverIdx = ctx.Mapper.XToIndex(mx)
                    If hoverIdx >= 0 AndAlso hoverIdx < ctx.Candles.Count Then idx = hoverIdx
                End If
            End If

            DrawCandleInfo(canvas, ctx, idx)
            DrawIndicatorValues(canvas, ctx, idx)
        End Sub

        Private Shared Sub DrawCandleInfo(canvas As SKCanvas, ctx As ChartContext, idx As Integer)
            If idx < 0 OrElse idx >= ctx.Candles.Count Then Return
            Dim c = ctx.Candles(idx)
            Dim info = $"O {c.Open:N0}  H {c.High:N0}  L {c.Low:N0}  C {c.Close:N0}  V {c.Volume:N0}"

            '' 종가(C) 직전봉 대비 변화율
            Dim pctTxt As String = Nothing
            Dim pctUp As Boolean = True
            If idx - 1 >= 0 AndAlso idx - 1 < ctx.Candles.Count Then
                Dim prevClose = ctx.Candles(idx - 1).Close
                If Math.Abs(prevClose) > 0.0000001F Then
                    Dim pct As Single = (c.Close - prevClose) / Math.Abs(prevClose) * 100.0F
                    pctUp = pct >= 0
                    pctTxt = $"  ({If(pctUp, "+", "")}{pct:N2}%)"
                End If
            End If

            Dim x = ctx.MainRect.Left + 8
            Dim y = ctx.MainRect.Top + 14
            Using tp As New SKPaint()
                tp.TextSize = 11
                tp.IsAntialias = True
                tp.Typeface = SKTypeface.FromFamilyName("Consolas")
                Dim tw = tp.MeasureText(info)
                Dim pw As Single = If(pctTxt Is Nothing, 0, tp.MeasureText(pctTxt))
                Using bgP As New SKPaint()
                    bgP.Style = SKPaintStyle.Fill
                    bgP.Color = New SKColor(24, 26, 32, 200)
                    canvas.DrawRect(x - 4, y - 12, tw + pw + 8, 16, bgP)
                End Using
                tp.Color = If(c.Close >= c.Open, ctx.Theme.BullCandle, ctx.Theme.BearCandle)
                canvas.DrawText(info, x, y, tp)
                If pctTxt IsNot Nothing Then
                    tp.Color = If(pctUp, ctx.Theme.BullCandle, ctx.Theme.BearCandle)
                    canvas.DrawText(pctTxt, x + tw, y, tp)
                End If
            End Using
        End Sub

        '' 오버레이(PanelIndex=0) 지표값. IndicatorLayer 와 동일한 colorIdx 규칙으로
        '' 색을 계산해 이름 앞에 색 마커를 찍고, 값 뒤에 직전봉 대비 변화율(%)을 표시.
        Private Shared Sub DrawIndicatorValues(canvas As SKCanvas, ctx As ChartContext, idx As Integer)
            If ctx.Engine Is Nothing Then Return

            Dim leftX = ctx.MainRect.Left + 8
            Dim y = ctx.MainRect.Top + 32
            Dim colorIdx As Integer = 0

            Using tp As New SKPaint()
                tp.TextSize = 11
                tp.IsAntialias = True
                tp.Typeface = SKTypeface.FromFamilyName("맑은 고딕")

                Using swatch As New SKPaint()
                    swatch.Style = SKPaintStyle.Fill
                    swatch.IsAntialias = True

                    For Each ind In ctx.Engine.GetAll()
                        If ind.PanelIndex > 0 Then Continue For
                        Dim results As ChartKit.Models.IndicatorResultRingBuffer = Nothing
                        If Not ctx.Engine.Results.TryGetValue(ind.Name, results) Then Continue For
                        If results Is Nothing OrElse results.Count = 0 Then Continue For

                        Dim sampleR = results.FirstOrDefault(Function(r) r IsNot Nothing AndAlso r.Values IsNot Nothing AndAlso r.Values.Count > 0)
                        If sampleR Is Nothing Then Continue For

                        Dim keyCount As Integer = 0
                        For Each valueKey In sampleR.Values.Keys
                            If Not IsOverlayPriceValueKey(valueKey) Then Continue For
                            keyCount += 1

                            '' IndicatorLayer 와 동일 순서 → 동일 색
                            Dim lineColor = IndicatorColors(colorIdx Mod IndicatorColors.Length)
                            colorIdx += 1

                            '' 현재 봉 값 / 직전 봉 값
                            Dim cur As Single = Single.NaN
                            Dim prev As Single = Single.NaN
                            If idx >= 0 AndAlso idx < results.Count AndAlso results(idx) IsNot Nothing Then
                                cur = results(idx).Val(valueKey)
                            End If
                            If idx - 1 >= 0 AndAlso idx - 1 < results.Count AndAlso results(idx - 1) IsNot Nothing Then
                                prev = results(idx - 1).Val(valueKey)
                            End If

                            '' 색 마커 (채운 사각형)
                            swatch.Color = lineColor
                            canvas.DrawRect(leftX, y - 9, 9, 9, swatch)

                            '' 라벨 : 여러 오버레이 키가 있으면 키 이름을 덧붙임
                            Dim labelName As String = ind.DisplayName
                            If sampleR.Values.Count > 1 AndAlso Not String.Equals(valueKey, "Value", StringComparison.OrdinalIgnoreCase) Then
                                labelName = ind.DisplayName & " " & valueKey
                            End If

                            Dim tx = leftX + 14
                            tp.Color = ctx.Theme.AxisText
                            Dim head As String
                            If Single.IsNaN(cur) Then
                                head = $"{labelName}  --"
                            Else
                                head = $"{labelName}  {cur:N0}"
                            End If
                            canvas.DrawText(head, tx, y, tp)

                            '' 변화율(%) : head 뒤에 색을 달리해 이어 그림
                            If Not Single.IsNaN(cur) AndAlso Not Single.IsNaN(prev) AndAlso Math.Abs(prev) > 0.0000001F Then
                                Dim pct As Single = (cur - prev) / Math.Abs(prev) * 100.0F
                                Dim sign As String = If(pct >= 0, "+", "")
                                Dim pctTxt As String = $"  ({sign}{pct:N2}%)"
                                Dim headW = tp.MeasureText(head)
                                tp.Color = If(pct >= 0, ctx.Theme.BullCandle, ctx.Theme.BearCandle)
                                canvas.DrawText(pctTxt, tx + headW, y, tp)
                            End If

                            y += 16
                        Next
                    Next
                End Using
            End Using
        End Sub

        '' IndicatorLayer 의 판정과 동일해야 함.
        Private Shared Function IsOverlayPriceValueKey(key As String) As Boolean
            If String.IsNullOrEmpty(key) Then Return False
            Select Case key.ToLowerInvariant()
                Case "upper", "lower", "upper1", "upper2", "lower1", "lower2",
                     "upperband", "lowerband", "mid", "middle",
                     "direction", "hist", "histogram", "signal",
                     "up", "down", "slope", "atr"
                    Return False
                Case Else
                    Return True
            End Select
        End Function
    End Class
End Namespace
