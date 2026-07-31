Imports SkiaSharp
Imports ChartKit.Abstractions
Imports ChartKit.Models
Imports ChartKit.Core.Signals

Namespace Layers
    '' 오버레이 지표 A >= B 인 구간을 메인차트 배경에 음영으로 칠함.
    '' ZOrder=25 : 캔들(20) 위, 지표선(30) 아래.
    Public Class OverlayShadeLayer
        Implements IChartLayer

        Public Sub Dispose() Implements IDisposable.Dispose
            '' 영속 Skia 리소스 없음
        End Sub

        Public ReadOnly Property Id As String Implements IChartLayer.Id
            Get
                Return "OverlayShade"
            End Get
        End Property

        Public Property IsVisible As Boolean Implements IChartLayer.IsVisible

        Public ReadOnly Property ZOrder As Integer Implements IChartLayer.ZOrder
            Get
                Return 25
            End Get
        End Property

        Public Sub New()
            IsVisible = True
        End Sub

        Private Shared Function ValAt(results As IReadOnlyList(Of IndicatorResult), i As Integer) As Single
            If results Is Nothing OrElse i < 0 OrElse i >= results.Count Then Return Single.NaN
            Dim r = results(i)
            If r Is Nothing OrElse r.Values Is Nothing Then Return Single.NaN
            '' 오버레이 본선은 관례상 "VALUE" 키. 없으면 첫 유효 값 사용.
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
            If ctx.ShadeRules Is Nothing OrElse ctx.ShadeRules.Count = 0 Then Return

            Dim s = Math.Max(0, ctx.StartIndex)
            Dim en = Math.Min(ctx.Candles.Count - 1, ctx.EndIndex)
            Dim rect = ctx.MainRect

            For Each rule In ctx.ShadeRules
                If rule Is Nothing OrElse String.IsNullOrEmpty(rule.IndicatorA) OrElse String.IsNullOrEmpty(rule.IndicatorB) Then Continue For
                If rule.IndicatorA.StartsWith("JMA_", StringComparison.OrdinalIgnoreCase) AndAlso
                   rule.IndicatorB.StartsWith("JMA_", StringComparison.OrdinalIgnoreCase) Then
                    DrawQualifiedJmaRanges(canvas, ctx, rule, s, en, rect)
                    Continue For
                End If
                Dim ra As ChartKit.Models.IndicatorResultRingBuffer = Nothing
                Dim rb As ChartKit.Models.IndicatorResultRingBuffer = Nothing
                If Not ctx.Engine.Results.TryGetValue(rule.IndicatorA, ra) Then Continue For
                If Not ctx.Engine.Results.TryGetValue(rule.IndicatorB, rb) Then Continue For

                Using p As New SKPaint With {.Style = SKPaintStyle.Fill, .IsAntialias = False,
                        .Color = New SKColor(CByte(rule.ColorR), CByte(rule.ColorG), CByte(rule.ColorB), CByte(rule.ColorA))}
                    Dim runStart As Integer = -1
                    For i = s To en
                        Dim av = ValAt(ra, i)
                        Dim bv = ValAt(rb, i)
                        Dim on_ As Boolean = (Not Single.IsNaN(av)) AndAlso (Not Single.IsNaN(bv)) AndAlso av > 0 AndAlso bv > 0 AndAlso av >= bv
                        If on_ AndAlso rule.RequireBRising Then
                            '' B(장기) 상승 조건: 직전봉 대비 B값 증가. 시작봉(i=s) 또는 이전값 NaN 이면 제외.
                            If i <= s Then
                                on_ = False
                            Else
                                Dim bvPrev = ValAt(rb, i - 1)
                                If Single.IsNaN(bvPrev) OrElse Not (bv > bvPrev) Then on_ = False
                            End If
                        End If
                        If on_ Then
                            If runStart < 0 Then runStart = i
                        Else
                            If runStart >= 0 Then
                                Dim x0 = ctx.Mapper.IndexToX(runStart) - ctx.CandleWidth / 2
                                Dim x1 = ctx.Mapper.IndexToX(i - 1) + ctx.CandleWidth / 2
                                canvas.DrawRect(New SKRect(x0, rect.Top, x1, rect.Bottom), p)
                                runStart = -1
                            End If
                        End If
                    Next
                    If runStart >= 0 Then
                        Dim x0 = ctx.Mapper.IndexToX(runStart) - ctx.CandleWidth / 2
                        Dim x1 = ctx.Mapper.IndexToX(en) + ctx.CandleWidth / 2
                        canvas.DrawRect(New SKRect(x0, rect.Top, x1, rect.Bottom), p)
                    End If
                End Using
            Next
        End Sub

        Private Shared Sub DrawQualifiedJmaRanges(canvas As SKCanvas,
                                                  ctx As ChartContext,
                                                  rule As ChartKit.Core.OverlayShadeRule,
                                                  visibleStart As Integer,
                                                  visibleEnd As Integer,
                                                  rect As SKRect)
            Dim ranges = QualifiedTrendRangeEvaluator.Evaluate(
                rule, ctx.SignalRules, ctx.Engine.Results, visibleStart, visibleEnd)
            If ranges.Count = 0 Then Return

            Using paint As New SKPaint With {
                .Style = SKPaintStyle.Fill, .IsAntialias = False,
                .Color = New SKColor(CByte(rule.ColorR), CByte(rule.ColorG),
                                     CByte(rule.ColorB), CByte(rule.ColorA))}
                For Each range In ranges
                    Dim first = Math.Max(visibleStart, range.StartIndex)
                    Dim last = Math.Min(visibleEnd, range.EndIndex)
                    If first > last Then Continue For
                    Dim x0 = ctx.Mapper.IndexToX(first) - ctx.CandleWidth / 2
                    Dim x1 = ctx.Mapper.IndexToX(last) + ctx.CandleWidth / 2
                    canvas.DrawRect(New SKRect(x0, rect.Top, x1, rect.Bottom), paint)
                Next
            End Using
        End Sub
    End Class
End Namespace
