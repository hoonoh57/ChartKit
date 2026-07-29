Imports SkiaSharp
Imports ChartKit.Abstractions

Namespace Layers
    '' 원본 FastChartControl 의 그리드/축 로직을 그대로 이식.
    '' 좌표 참조만 ctx.* 로 치환. 계산식/상수/분기는 원본과 동일.
    Public Class GridAxisLayer
        Implements IChartLayer

        Public ReadOnly Property Id As String Implements IChartLayer.Id
            Get
                Return "GridAxis"
            End Get
        End Property
        Public Property IsVisible As Boolean = True Implements IChartLayer.IsVisible
        Public ReadOnly Property ZOrder As Integer Implements IChartLayer.ZOrder
            Get
                Return 0
            End Get
        End Property

        Private _paintGrid As SKPaint
        Private _paintAxisText As SKPaint

        Public Sub Draw(canvas As SKCanvas, ctx As ChartContext) Implements IChartLayer.Draw
            EnsurePaints(ctx.Theme)
            DrawGrid(canvas, ctx)
            DrawAxisY(canvas, ctx)
            DrawAxisX(canvas, ctx)
        End Sub

        Private Sub EnsurePaints(t As ChartTheme)
            If _paintGrid IsNot Nothing Then Return
            _paintGrid = New SKPaint With {.Style = SKPaintStyle.Stroke, .Color = t.Grid, .StrokeWidth = 1, .IsAntialias = False}
            _paintAxisText = New SKPaint With {.Color = t.AxisText, .TextSize = t.AxisFontSize, .IsAntialias = True, .Typeface = SKTypeface.FromFamilyName("Consolas")}
        End Sub

        '' ===== 원본 DrawGrid 그대로 (MARGIN_BOTTOM -> ctx.Theme.MarginBottom, _mainRect -> ctx.MainRect 등) =====
        Private Sub DrawGrid(canvas As SKCanvas, ctx As ChartContext)
            Dim priceRange = ctx.PriceHigh - ctx.PriceLow
            Dim gridStep = CalculateNiceStep(priceRange, 7)
            Dim p As Single = CSng(Math.Ceiling(ctx.PriceLow / gridStep) * gridStep)
            While p < ctx.PriceHigh
                Dim y = ctx.Mapper.PriceToY(p)
                If y >= ctx.MainRect.Top AndAlso y <= ctx.MainRect.Bottom Then
                    canvas.DrawLine(ctx.MainRect.Left, y, ctx.MainRect.Right, y, _paintGrid)
                End If
                p += CSng(gridStep)
            End While

            Dim s = Math.Max(0, ctx.StartIndex)
            Dim endI = Math.Min(ctx.Candles.Count - 1, ctx.EndIndex)
            If endI >= s Then
                Dim minuteStep = GetAxisMinuteStep(s, endI)
                Dim useIndexTicks = UsesIrregularTimeAxis(ctx, s, endI)
                For i As Integer = s To endI
                    If i < 0 OrElse i >= ctx.Candles.Count Then Continue For
                    Dim dt = ctx.Candles(i).Dt
                    If dt = DateTime.MinValue Then Continue For
                    If Not ShouldDrawAxisTick(ctx, i, dt, minuteStep, s, useIndexTicks) Then Continue For
                    Dim x = ctx.Mapper.IndexToX(i)
                    If x >= ctx.MainRect.Left AndAlso x <= ctx.MainRect.Right Then
                        canvas.DrawLine(x, ctx.MainRect.Top, x, ctx.TotalHeight - ctx.Theme.MarginBottom, _paintGrid)
                    End If
                Next
            End If

            If ctx.ShowDayChangeLines AndAlso endI > s Then
                Using dayPaint As New SKPaint()
                    dayPaint.Style = SKPaintStyle.Stroke
                    dayPaint.Color = New SKColor(120, 130, 155, 150)
                    dayPaint.StrokeWidth = 1
                    dayPaint.PathEffect = SKPathEffect.CreateDash({3, 3}, 0)
                    For i As Integer = Math.Max(1, s) To endI
                        If i >= ctx.Candles.Count Then Exit For
                        Dim prevDt = ctx.Candles(i - 1).Dt
                        Dim curDt = ctx.Candles(i).Dt
                        If prevDt = DateTime.MinValue OrElse curDt = DateTime.MinValue Then Continue For
                        If prevDt.Date = curDt.Date Then Continue For
                        Dim x = ctx.Mapper.IndexToX(i)
                        If x >= ctx.MainRect.Left AndAlso x <= ctx.MainRect.Right Then
                            canvas.DrawLine(x, ctx.MainRect.Top, x, ctx.TotalHeight - ctx.Theme.MarginBottom, dayPaint)
                        End If
                    Next
                End Using
            End If
        End Sub

        '' ===== 원본 CalculateNiceStep 그대로 =====
        Private Shared Function CalculateNiceStep(range As Single, targetLines As Integer) As Double
            If range <= 0 OrElse targetLines <= 0 Then Return 1
            Dim rawStep = range / targetLines
            Dim magnitude = Math.Pow(10, Math.Floor(Math.Log10(rawStep)))
            Dim normalized = rawStep / magnitude
            Dim niceNorm As Double
            If normalized <= 1 Then
                niceNorm = 1
            ElseIf normalized <= 2 Then
                niceNorm = 2
            ElseIf normalized <= 5 Then
                niceNorm = 5
            Else
                niceNorm = 10
            End If
            Return niceNorm * magnitude
        End Function

        '' ===== 원본 DrawAxisY 그대로 =====
        Private Sub DrawAxisY(canvas As SKCanvas, ctx As ChartContext)
            Dim gridStep = CSng(CalculateNiceStep(ctx.PriceHigh - ctx.PriceLow, 7))
            _paintAxisText.TextAlign = SKTextAlign.Left
            Dim x = ctx.MainRect.Right + 6
            Dim p = CSng(Math.Ceiling(ctx.PriceLow / gridStep) * gridStep)
            While p < ctx.PriceHigh
                Dim y = ctx.Mapper.PriceToY(p)
                If y >= ctx.MainRect.Top + 10 AndAlso y <= ctx.MainRect.Bottom - 5 Then
                    canvas.DrawText(FormatAxisPrice(p), x, y + 4, _paintAxisText)
                End If
                p += gridStep
            End While
        End Sub

        '' ===== 원본 DrawAxisX 그대로 =====
        Private Sub DrawAxisX(canvas As SKCanvas, ctx As ChartContext)
            _paintAxisText.TextAlign = SKTextAlign.Center
            Dim y = ctx.TotalHeight - ctx.Theme.MarginBottom + 14
            Dim s = Math.Max(0, ctx.StartIndex)
            Dim endI = Math.Min(ctx.Candles.Count - 1, ctx.EndIndex)
            If endI < s Then
                _paintAxisText.TextAlign = SKTextAlign.Left
                Return
            End If

            Dim minuteStep = GetAxisMinuteStep(s, endI)
            Dim useIndexTicks = UsesIrregularTimeAxis(ctx, s, endI)
            Dim minPixelGap As Single = 56.0F
            Dim lastDrawX As Single = Single.MinValue
            Dim lastDate As DateTime = DateTime.MinValue

            For i As Integer = s To endI
                If i < 0 OrElse i >= ctx.Candles.Count Then Continue For
                Dim c = ctx.Candles(i)
                If c.Dt = DateTime.MinValue Then Continue For
                If Not ShouldDrawAxisTick(ctx, i, c.Dt, minuteStep, s, useIndexTicks) Then Continue For
                Dim x = ctx.Mapper.IndexToX(i)
                If x < ctx.MainRect.Left OrElse x > ctx.MainRect.Right Then Continue For
                If lastDrawX <> Single.MinValue AndAlso (x - lastDrawX) < minPixelGap Then Continue For
                Dim label As String

                If c.Dt.TimeOfDay.TotalSeconds = 0 Then
                    label = c.Dt.ToString("MM/dd")
                Else
                    If lastDate = DateTime.MinValue OrElse c.Dt.Date <> lastDate.Date Then
                        label = c.Dt.ToString("MM-dd HH:mm")
                    Else
                        label = c.Dt.ToString("HH:mm")
                    End If
                End If

                '' 좌우 끝 레이블은 TextAlign.Center 때문에 차트 밖으로 잘리지 않도록 보정한다.
                Dim halfLabelWidth = _paintAxisText.MeasureText(label) / 2.0F
                Dim drawX = Math.Max(ctx.MainRect.Left + halfLabelWidth,
                                     Math.Min(ctx.MainRect.Right - halfLabelWidth, x))
                canvas.DrawText(label, drawX, y, _paintAxisText)
                lastDate = c.Dt
                lastDrawX = x
            Next
            _paintAxisText.TextAlign = SKTextAlign.Left
        End Sub

        '' ===== 원본 GetAxisMinuteStep 그대로 =====
        Private Shared Function GetAxisMinuteStep(startIdx As Integer, endIdx As Integer) As Integer
            Dim visibleCount = Math.Max(1, endIdx - startIdx + 1)
            Dim targetLabels = 8
            Dim rough = Math.Max(1, CInt(Math.Ceiling(visibleCount / CDbl(targetLabels))))
            Dim steps As Integer() = {1, 2, 3, 5, 10, 15, 30, 60, 120, 180, 240}
            For Each st In steps
                If rough <= st Then Return st
            Next
            Return 240
        End Function

        '' ===== 원본 ShouldDrawAxisTick 그대로 (_candles -> ctx.Candles) =====
        Private Shared Function ShouldDrawAxisTick(ctx As ChartContext, idx As Integer, dt As DateTime,
                                                   minuteStep As Integer, startIdx As Integer,
                                                   useIndexTicks As Boolean) As Boolean
            If idx = startIdx Then Return True
            If idx > 0 AndAlso idx < ctx.Candles.Count Then
                Dim prev = ctx.Candles(idx - 1).Dt
                If prev <> DateTime.MinValue AndAlso prev.Date <> dt.Date Then Return True
            End If
            '' 틱봉은 체결 완료 시각이 불규칙하고 초 값도 0이 아니므로 분 정각 조건을
            '' 적용할 수 없다. 화면에 보이는 봉 개수를 기준으로 균등하게 눈금을 선택한다.
            If useIndexTicks Then Return ((idx - startIdx) Mod minuteStep) = 0
            If dt.Second <> 0 Then Return False
            If minuteStep <= 60 Then
                Return (dt.Minute Mod minuteStep) = 0
            End If
            Dim totalMinutes = dt.Hour * 60 + dt.Minute
            Return (totalMinutes Mod minuteStep) = 0
        End Function

        '' 분봉은 보통 초가 00이고 간격이 일정하지만 틱봉은 완료 시각의 초가 불규칙하다.
        '' visible 구간에서 이를 판별해 기존 분봉 축 규칙과 틱봉 축 규칙을 분리한다.
        Private Shared Function UsesIrregularTimeAxis(ctx As ChartContext, startIdx As Integer, endIdx As Integer) As Boolean
            Dim previous As DateTime = DateTime.MinValue
            Dim expectedSeconds As Double = -1
            For i = startIdx To endIdx
                Dim dt = ctx.Candles(i).Dt
                If dt = DateTime.MinValue Then Continue For
                If dt.Second <> 0 Then Return True
                If previous <> DateTime.MinValue Then
                    Dim seconds = (dt - previous).TotalSeconds
                    If seconds <= 0 OrElse (seconds Mod 60) <> 0 Then Return True
                    If expectedSeconds < 0 Then
                        expectedSeconds = seconds
                    ElseIf seconds <> expectedSeconds Then
                        Return True
                    End If
                End If
                previous = dt
            Next
            Return False
        End Function

        '' ===== 원본 FormatAxisPrice 그대로 =====
        Private Shared Function FormatAxisPrice(price As Single) As String
            If price >= 1000 Then Return price.ToString("N0")
            If price >= 100 Then Return price.ToString("N1")
            Return price.ToString("N2")
        End Function
    End Class
End Namespace
