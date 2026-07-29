Imports SkiaSharp
Imports ChartKit.Abstractions
Imports ChartKit.Models

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

        Private Sub EnsurePaints()
            If _upPaint IsNot Nothing Then Return
            _upPaint = New SKPaint With {.Style = SKPaintStyle.Fill, .IsAntialias = True, .Color = New SKColor(60, 200, 110, 235)}
            _dnPaint = New SKPaint With {.Style = SKPaintStyle.Fill, .IsAntialias = True, .Color = New SKColor(235, 70, 90, 235)}
        End Sub

        Private Shared Function ValAt(results As System.Collections.Generic.List(Of IndicatorResult), i As Integer) As Single
            If results Is Nothing OrElse i < 0 OrElse i >= results.Count Then Return Single.NaN
            Dim r = results(i)
            If r Is Nothing OrElse r.Values Is Nothing Then Return Single.NaN
            Dim v As Single
            If r.Values.TryGetValue("Value", v) Then Return v
            For Each kv In r.Values
                If Not Single.IsNaN(kv.Value) Then Return kv.Value
            Next
            Return Single.NaN
        End Function

        Public Sub Draw(canvas As SKCanvas, ctx As ChartContext) Implements IChartLayer.Draw
            If ctx.Engine Is Nothing Then Return
            If ctx.Candles Is Nothing OrElse ctx.Candles.Count = 0 Then Return
            If ctx.SignalRules Is Nothing OrElse ctx.SignalRules.Count = 0 Then Return
            EnsurePaints()

            Dim s = Math.Max(0, ctx.StartIndex)
            Dim en = Math.Min(ctx.Candles.Count - 1, ctx.EndIndex)
            Dim sz As Single = Math.Max(4.0F, ctx.CandleWidth * 0.5F)

            Dim _buyStack As New System.Collections.Generic.Dictionary(Of Integer, Integer)()
                Dim _sellStack As New System.Collections.Generic.Dictionary(Of Integer, Integer)()
                For Each rule In ctx.SignalRules
                If rule Is Nothing OrElse String.IsNullOrEmpty(rule.IndicatorA) OrElse String.IsNullOrEmpty(rule.IndicatorB) Then Continue For
                Dim ra As System.Collections.Generic.List(Of IndicatorResult) = Nothing
                Dim rb As System.Collections.Generic.List(Of IndicatorResult) = Nothing
                If Not ctx.Engine.Results.TryGetValue(rule.IndicatorA, ra) Then Continue For
                If Not ctx.Engine.Results.TryGetValue(rule.IndicatorB, rb) Then Continue For

                                '' ── latch 상태: 교차 성립 후 B가 조건 만족하는 첫 봉에 발화 ──
                Const LATCH_MAX As Integer = 10   '' 교차 후 조건대기 최대 봉수 (만료 취소)
                Dim armed As Boolean = False
                Dim armedBar As Integer = -1

                For i = Math.Max(1, s) To en
                    Dim a0 = ValAt(ra, i - 1) : Dim b0 = ValAt(rb, i - 1)
                    Dim a1 = ValAt(ra, i) : Dim b1 = ValAt(rb, i)
                    If Single.IsNaN(a0) OrElse Single.IsNaN(b0) OrElse Single.IsNaN(a1) OrElse Single.IsNaN(b1) Then Continue For

                    Dim crossedUp = (a0 <= b0) AndAlso (a1 > b1)
                    Dim crossedDn = (a0 >= b0) AndAlso (a1 < b1)

                    Dim hit As Boolean = False

                    If Not rule.RequireBRising Then
                        hit = If(rule.CrossUp, crossedUp, crossedDn)
                    Else
                        If rule.CrossUp Then
                            If crossedUp Then armed = True : armedBar = i
                            If armed AndAlso (a1 < b1) Then armed = False
                            If armed AndAlso (i - armedBar) > LATCH_MAX Then armed = False
                            If armed AndAlso (b1 > b0) Then
                                hit = True
                                armed = False
                            End If
                        Else
                            If crossedDn Then armed = True : armedBar = i
                            If armed AndAlso (a1 > b1) Then armed = False
                            If armed AndAlso (i - armedBar) > LATCH_MAX Then armed = False
                            If armed AndAlso (b1 < b0) Then
                                hit = True
                                armed = False
                            End If
                        End If
                    End If

                    If Not hit Then Continue For

                    Dim c = ctx.Candles(i)
                    Dim px = ctx.Mapper.IndexToX(i)
                    Dim isBuy As Boolean = If(rule.Side < 0, rule.CrossUp, rule.Side = 0)
                    Dim paint As SKPaint = If(isBuy, _upPaint, _dnPaint)
                    Dim customPaint As SKPaint = Nothing
                    If rule.ColorArgb <> 0 Then
                        Dim ca = System.Drawing.Color.FromArgb(rule.ColorArgb)
                        customPaint = New SKPaint With {.Style = SKPaintStyle.Fill, .IsAntialias = True, .Color = New SKColor(ca.R, ca.G, ca.B, ca.A)}
                        paint = customPaint
                    End If

                    Dim gap As Single = sz + 4
                    If isBuy Then
                        Dim k As Integer = 0
                        _buyStack.TryGetValue(i, k)
                        Dim cy = ctx.Mapper.PriceToY(c.Low) + gap + k * (2 * sz + 3)
                        DrawMarker(canvas, rule.MarkerShape, True, px, cy, sz, paint)
                        _buyStack(i) = k + 1
                    Else
                        Dim k As Integer = 0
                        _sellStack.TryGetValue(i, k)
                        Dim cy = ctx.Mapper.PriceToY(c.High) - gap - k * (2 * sz + 3)
                        DrawMarker(canvas, rule.MarkerShape, False, px, cy, sz, paint)
                        _sellStack(i) = k + 1
                    End If
                    If customPaint IsNot Nothing Then customPaint.Dispose()
                Next
            Next
        End Sub
    
        '' ── 마커 모양별 그리기 (isUp: 삼각형 방향) ──
        Private Shared Sub DrawMarker(canvas As SKCanvas, shape As Integer, isUp As Boolean, cx As Single, cy As Single, sz As Single, paint As SKPaint)
            Dim path As New SKPath()
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
            path.Dispose()
        End Sub
    End Class
End Namespace