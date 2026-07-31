Imports SkiaSharp
Imports ChartKit.Abstractions
Imports ChartKit.Models
Imports ChartKit.Core.Signals

Namespace Layers
    '' 신호 검색: 오버레이 지표 A 가 B 를 상향/하향 돌파하는 봉에 화살표 표시.
    '' ZOrder=40 : 지표선(30) 위.
    Public Class SignalLayer
        Implements IChartLayer

        Public ReadOnly Property Id As String Implements IChartLayer.Id
            Get
                Return "Signal"
            End Get
        End Property
        Public Property IsVisible As Boolean = True Implements IChartLayer.IsVisible
        Public ReadOnly Property ZOrder As Integer Implements IChartLayer.ZOrder
            Get
                Return 40
            End Get
        End Property

        Private _upPaint As SKPaint
        Private _dnPaint As SKPaint
        Private _scorePaint As SKPaint
        Private ReadOnly _customPaints As New Dictionary(Of Integer, SKPaint)

        Private Sub EnsurePaints()
            If _upPaint IsNot Nothing Then Return
            _upPaint = New SKPaint With {.Style = SKPaintStyle.Fill, .IsAntialias = True, .Color = New SKColor(60, 200, 110, 235)}
            _dnPaint = New SKPaint With {.Style = SKPaintStyle.Fill, .IsAntialias = True, .Color = New SKColor(235, 70, 90, 235)}
            _scorePaint = New SKPaint With {
                .Style = SKPaintStyle.Fill, .IsAntialias = True,
                .TextAlign = SKTextAlign.Center, .TextSize = 11.0F,
                .Typeface = SKTypeface.FromFamilyName("Malgun Gothic")}
        End Sub

        Public Sub Draw(canvas As SKCanvas, ctx As ChartContext) Implements IChartLayer.Draw
            If ctx.Engine Is Nothing Then Return
            If ctx.Candles Is Nothing OrElse ctx.Candles.Count = 0 Then Return
            If ctx.SignalRules Is Nothing OrElse ctx.SignalRules.Count = 0 Then Return
            EnsurePaints()

            Dim s = Math.Max(0, ctx.StartIndex)
            Dim en = Math.Min(ctx.Candles.Count - 1, ctx.EndIndex)
            Dim sz As Single = Math.Max(4.0F, ctx.CandleWidth * 0.5F)

            Dim _buyStack As New Dictionary(Of Integer, Integer)()
            Dim _sellStack As New Dictionary(Of Integer, Integer)()
            For Each signalHit In SignalEvaluator.Evaluate(ctx.SignalRules, ctx.Engine.Results, s, en)
                    Dim i = signalHit.CandleIndex
                    Dim rule = signalHit.Rule
                    Dim c = ctx.Candles(i)
                    Dim px = ctx.Mapper.IndexToX(i)
                    Dim isBuy As Boolean = If(rule.Side < 0, rule.CrossUp, rule.Side = 0)
                    Dim paint As SKPaint = If(isBuy, _upPaint, _dnPaint)
                    If rule.ColorArgb <> 0 Then
                        If Not _customPaints.TryGetValue(rule.ColorArgb, paint) Then
                            Dim ca = System.Drawing.Color.FromArgb(rule.ColorArgb)
                            paint = New SKPaint With {.Style = SKPaintStyle.Fill, .IsAntialias = True,
                                .Color = New SKColor(ca.R, ca.G, ca.B, ca.A)}
                            _customPaints(rule.ColorArgb) = paint
                        End If
                    End If

                    Dim gap As Single = sz + 4
                    If isBuy Then
                        Dim k As Integer = 0
                        _buyStack.TryGetValue(i, k)
                        Dim cy = ctx.Mapper.PriceToY(c.Low) + gap + k * (2 * sz + 3)
                        DrawMarker(canvas, rule.MarkerShape, True, px, cy, sz, paint)
                        If signalHit.EntryScore IsNot Nothing Then
                            DrawEntryScore(canvas, px, cy + sz + 13.0F, signalHit.EntryScore)
                        End If
                        _buyStack(i) = k + 1
                    Else
                        Dim k As Integer = 0
                        _sellStack.TryGetValue(i, k)
                        Dim cy = ctx.Mapper.PriceToY(c.High) - gap - k * (2 * sz + 3)
                        DrawMarker(canvas, rule.MarkerShape, False, px, cy, sz, paint)
                        _sellStack(i) = k + 1
                    End If
            Next
        End Sub

        Private Sub DrawEntryScore(canvas As SKCanvas,
                                   x As Single,
                                   y As Single,
                                   score As JmaEntryScoreSnapshot)
            If score.Score >= 75 Then
                _scorePaint.Color = New SKColor(90, 225, 135, 245)
            ElseIf score.Score >= 50 Then
                _scorePaint.Color = New SKColor(245, 195, 65, 245)
            Else
                _scorePaint.Color = New SKColor(185, 190, 200, 235)
            End If
            Dim text = $"E{score.Score}  L{score.LongSlope:+0.0;-0.0;0.0}%/{score.BarsSinceLongTurn}"
            canvas.DrawText(text, x, y, _scorePaint)
        End Sub
    
        '' ── 마커 모양별 그리기 (isUp: 삼각형 방향) ──
        Private Shared Sub DrawMarker(canvas As SKCanvas, shape As Integer, isUp As Boolean, cx As Single, cy As Single, sz As Single, paint As SKPaint)
            Using path As New SKPath()
            Select Case shape
                Case 0  '' 화살표 (삼각형과 동일 처리)
                    If isUp Then
                        path.MoveTo(cx, cy - sz) : path.LineTo(cx - sz, cy + sz) : path.LineTo(cx + sz, cy + sz)
                    Else
                        path.MoveTo(cx, cy + sz) : path.LineTo(cx - sz, cy - sz) : path.LineTo(cx + sz, cy - sz)
                    End If
                    path.Close() : canvas.DrawPath(path, paint)
                Case 1  '' 위 삼각형
                    path.MoveTo(cx, cy - sz) : path.LineTo(cx - sz, cy + sz) : path.LineTo(cx + sz, cy + sz)
                    path.Close() : canvas.DrawPath(path, paint)
                Case 2  '' 아래 삼각형
                    path.MoveTo(cx, cy + sz) : path.LineTo(cx - sz, cy - sz) : path.LineTo(cx + sz, cy - sz)
                    path.Close() : canvas.DrawPath(path, paint)
                Case 3  '' 다이아몬드
                    path.MoveTo(cx, cy - sz) : path.LineTo(cx + sz, cy) : path.LineTo(cx, cy + sz) : path.LineTo(cx - sz, cy)
                    path.Close() : canvas.DrawPath(path, paint)
                Case 4  '' 원
                    canvas.DrawCircle(cx, cy, sz, paint)
                Case 5  '' 별 (5각)
                    Dim n = 5
                    For j = 0 To n * 2 - 1
                        Dim r As Single = If(j Mod 2 = 0, sz, sz * 0.45F)
                        Dim ang As Double = -Math.PI / 2 + j * Math.PI / n
                        Dim x = cx + CSng(r * Math.Cos(ang))
                        Dim y = cy + CSng(r * Math.Sin(ang))
                        If j = 0 Then path.MoveTo(x, y) Else path.LineTo(x, y)
                    Next
                    path.Close() : canvas.DrawPath(path, paint)
                Case 6  '' 네모
                    canvas.DrawRect(cx - sz, cy - sz, sz * 2, sz * 2, paint)
                Case 7  '' 십자
                    Dim t As Single = sz * 0.35F
                    canvas.DrawRect(cx - t, cy - sz, t * 2, sz * 2, paint)
                    canvas.DrawRect(cx - sz, cy - t, sz * 2, t * 2, paint)
                Case Else
                    canvas.DrawCircle(cx, cy, sz, paint)
            End Select
            End Using
        End Sub

        Public Sub Dispose() Implements IDisposable.Dispose
            _upPaint?.Dispose() : _upPaint = Nothing
            _dnPaint?.Dispose() : _dnPaint = Nothing
            _scorePaint?.Dispose() : _scorePaint = Nothing
            For Each paint In _customPaints.Values
                paint.Dispose()
            Next
            _customPaints.Clear()
        End Sub
    End Class
End Namespace
