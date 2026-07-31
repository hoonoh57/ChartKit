Imports SkiaSharp
Imports ChartKit.Abstractions

Namespace Layers
    '' 좌측 등락률(%) 축 + 가로선.
    '' 기준가: 화면 마지막 봉이 속한 날짜 기준.
    ''   모드1(전일종가대비) = 그 날짜 직전 거래일 마지막 봉 Close (없으면 당일 시가)
    ''   모드2(시가대비)     = 그 날짜 첫 봉 Open
    '' ZOrder=5 : 그리드축(0) 위, 크로스헤어(900) 아래.
    Public Class PctAxisLayer
        Implements IChartLayer

        Public ReadOnly Property Id As String Implements IChartLayer.Id
            Get
                Return "PctAxis"
            End Get
        End Property
        Public Property IsVisible As Boolean = True Implements IChartLayer.IsVisible
        Public ReadOnly Property ZOrder As Integer Implements IChartLayer.ZOrder
            Get
                Return 5
            End Get
        End Property

        Private _line As SKPaint
        Private _zeroLine As SKPaint
        Private _text As SKPaint
        Private _bg As SKPaint
        Private _paintThemeVersion As Integer = -1

        Private Sub EnsurePaints(t As ChartTheme)
            If _text IsNot Nothing AndAlso _paintThemeVersion = t.Version Then Return
            DisposePaints()
            _line = New SKPaint With {.Style = SKPaintStyle.Stroke, .Color = New SKColor(110, 120, 145, 90), .StrokeWidth = 1, .IsAntialias = False}
            _line.PathEffect = SKPathEffect.CreateDash({3, 3}, 0)
            _zeroLine = New SKPaint With {.Style = SKPaintStyle.Stroke, .Color = New SKColor(180, 190, 210, 200), .StrokeWidth = 1, .IsAntialias = False}
            _text = New SKPaint With {.Color = t.AxisText, .TextSize = t.AxisFontSize, .IsAntialias = True, .Typeface = SKTypeface.FromFamilyName("Consolas"), .TextAlign = SKTextAlign.Left}
            _bg = New SKPaint With {.Style = SKPaintStyle.Fill, .Color = New SKColor(20, 24, 34, 190), .IsAntialias = False}
            _paintThemeVersion = t.Version
        End Sub

        '' 기준가 계산. 못 구하면 NaN.
        Private Shared Function BaselinePrice(ctx As ChartContext) As Single
            Dim n = ctx.Candles.Count
            If n = 0 Then Return Single.NaN
            Dim endI = Math.Min(n - 1, ctx.EndIndex)
            If endI < 0 Then Return Single.NaN
            Dim refDate = ctx.Candles(endI).Dt.Date

            '' 해당 날짜의 첫 봉 인덱스 찾기
            Dim firstIdx = endI
            While firstIdx > 0 AndAlso ctx.Candles(firstIdx - 1).Dt.Date = refDate
                firstIdx -= 1
            End While

            If ctx.PctAxisMode = 2 Then
                '' 시가대비: 당일 첫 봉 Open
                Return ctx.Candles(firstIdx).Open
            Else
                '' 전일종가대비: 당일 첫 봉 직전 봉 Close, 없으면 당일 시가
                If firstIdx > 0 Then
                    Return ctx.Candles(firstIdx - 1).Close
                Else
                    Return ctx.Candles(firstIdx).Open
                End If
            End If
        End Function

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

        Public Sub Draw(canvas As SKCanvas, ctx As ChartContext) Implements IChartLayer.Draw
            If ctx.PctAxisMode = 0 Then Return
            If ctx.Candles Is Nothing OrElse ctx.Candles.Count = 0 Then Return
            EnsurePaints(ctx.Theme)

            Dim base_ = BaselinePrice(ctx)
            If Single.IsNaN(base_) OrElse base_ <= 0 Then Return

            Dim rect = ctx.MainRect
            '' 화면 가격 상/하한을 등락률(%)로 환산
            Dim pctHi = (ctx.PriceHigh - base_) / base_ * 100.0F
            Dim pctLo = (ctx.PriceLow - base_) / base_ * 100.0F
            Dim range = pctHi - pctLo
            If range <= 0 Then Return

            Dim step_ = CSng(CalculateNiceStep(range, 7))
            Dim labelX = rect.Left + 3

            Dim v = CSng(Math.Ceiling(pctLo / step_) * step_)
            While v < pctHi
                Dim price = base_ * (1.0F + v / 100.0F)
                Dim y = ctx.Mapper.PriceToY(price)
                If y >= rect.Top + 8 AndAlso y <= rect.Bottom - 3 Then
                    Dim isZero = (Math.Abs(v) < step_ / 100.0F)
                    canvas.DrawLine(rect.Left, y, rect.Right, y, If(isZero, _zeroLine, _line))
                    Dim lbl = If(v > 0, "+", "") & v.ToString("0.#") & "%"
                    Dim w = _text.MeasureText(lbl)
                    canvas.DrawRect(New SKRect(labelX - 2, y - 9, labelX + w + 3, y + 4), _bg)
                    canvas.DrawText(lbl, labelX, y + 3, _text)
                End If
                v += step_
            End While

            '' 0% 라인이 스텝에 안 걸려도 항상 표시
            Dim y0 = ctx.Mapper.PriceToY(base_)
            If y0 >= rect.Top AndAlso y0 <= rect.Bottom Then
                canvas.DrawLine(rect.Left, y0, rect.Right, y0, _zeroLine)
            End If

            '' 모드 라벨 (좌상단)
            Dim modeLbl = If(ctx.PctAxisMode = 2, "시가대비", "전일종가대비")
            Dim mw = _text.MeasureText(modeLbl)
            canvas.DrawRect(New SKRect(rect.Left + 1, rect.Top + 1, rect.Left + mw + 6, rect.Top + 15), _bg)
            canvas.DrawText(modeLbl, rect.Left + 3, rect.Top + 12, _text)
        End Sub

        Private Sub DisposePaints()
            _line?.Dispose() : _line = Nothing
            _zeroLine?.Dispose() : _zeroLine = Nothing
            _text?.Dispose() : _text = Nothing
            _bg?.Dispose() : _bg = Nothing
        End Sub

        Public Sub Dispose() Implements IDisposable.Dispose
            DisposePaints()
        End Sub
    End Class
End Namespace
