Imports SkiaSharp
Imports ChartKit.Abstractions
Imports ChartKit.Core
Imports ChartKit.Core.Signals
Imports ChartKit.Core.Strategies
Imports ChartKit.Models

Namespace Layers
    '' BasicJmaMacdStrategy_v1 결과만 표시한다. 신호/음영/전략 계산식을 복제하지 않는다.
    Public NotInheritable Class StrategyTradeLayer
        Implements IChartLayer

        Private _cacheKey As String = ""
        Private _cached As StrategyEvaluation
        Private _fixedEvaluation As StrategyEvaluation
        Private _lastStatus As String = "포착점 미지정"
        Public ReadOnly Property LastStatus As String
            Get
                Return _lastStatus
            End Get
        End Property

        Public ReadOnly Property TradeCount As Integer
            Get
                Return If(_cached Is Nothing, 0, _cached.Trades.Count)
            End Get
        End Property

        Public ReadOnly Property Id As String Implements IChartLayer.Id
            Get
                Return "StrategyTrades"
            End Get
        End Property

        Public Property IsVisible As Boolean Implements IChartLayer.IsVisible

        Public Sub New()
            IsVisible = True
        End Sub

        Public Sub SetEvaluation(evaluation As StrategyEvaluation)
            _fixedEvaluation = evaluation
            _cached = evaluation
            _cacheKey = ""
            _lastStatus = If(evaluation Is Nothing, "평가 결과 없음",
                             $"고정 평가 · 거래 {evaluation.Trades.Count}건")
        End Sub

        Public ReadOnly Property ZOrder As Integer Implements IChartLayer.ZOrder
            Get
                Return 45
            End Get
        End Property

        Public Sub Draw(canvas As SKCanvas, ctx As ChartContext) Implements IChartLayer.Draw
            If ctx Is Nothing OrElse ctx.Engine Is Nothing Then
                _lastStatus = "지표 엔진 없음"
                Return
            End If
            If ctx.StrategyCapture Is Nothing Then
                _lastStatus = "포착점 미지정"
                Return
            End If
            If ctx.Candles Is Nothing OrElse ctx.Candles.Count < 2 Then
                _lastStatus = "캔들 부족"
                Return
            End If
            If _fixedEvaluation IsNot Nothing Then
                DrawTrades(canvas, ctx, _fixedEvaluation)
                Return
            End If

            Dim shadeRule = FirstUsableJmaShadeRule(ctx.ShadeRules, ctx.Engine)
            Dim macd = RequiredMacdResults(ctx.Engine)
            If shadeRule Is Nothing Then
                _lastStatus = "사용 가능한 JMA 음영 규칙 없음"
                Return
            End If
            If macd Is Nothing Then
                _lastStatus = "필수 MACD(10,20,5) 없음"
                Return
            End If

            Dim key = BuildCacheKey(ctx, shadeRule, macd)
            If _cached Is Nothing OrElse Not String.Equals(key, _cacheKey, StringComparison.Ordinal) Then
                Dim ranges = QualifiedTrendRangeEvaluator.Evaluate(
                    shadeRule, ctx.SignalRules, ctx.Engine.Results, 0, ctx.Candles.Count - 1)
                _cached = BasicJmaMacdStrategy.Evaluate(
                    ctx.Candles, macd, ranges, ctx.StrategyCapture, ctx.StrategyReentryOptions)
                _cacheKey = key
                _lastStatus = $"평가 완료 · 거래 {_cached.Trades.Count}건"
            End If
            DrawTrades(canvas, ctx, _cached)
        End Sub

        Private Shared Sub DrawTrades(canvas As SKCanvas,
                                      ctx As ChartContext,
                                      evaluation As StrategyEvaluation)
            If evaluation Is Nothing Then Return
            Using buyPaint As New SKPaint With {
                    .Color = New SKColor(46, 204, 113), .Style = SKPaintStyle.Fill,
                    .IsAntialias = True},
                  sellPaint As New SKPaint With {
                    .Color = New SKColor(255, 82, 82), .Style = SKPaintStyle.Stroke,
                    .StrokeWidth = 2.0F, .IsAntialias = True},
                  textPaint As New SKPaint With {
                    .Color = New SKColor(235, 235, 235), .TextSize = 11.0F,
                    .IsAntialias = True}
                For Each trade In evaluation.Trades
                    If trade.EntryIndex >= ctx.StartIndex AndAlso trade.EntryIndex <= ctx.EndIndex Then
                        Dim x = ctx.Mapper.IndexToX(trade.EntryIndex)
                        Dim y = ctx.Mapper.PriceToY(trade.EntryPrice)
                        canvas.DrawCircle(x, y, 5.0F, buyPaint)
                        canvas.DrawText("B", x + 7.0F, y + 4.0F, textPaint)
                    End If
                    If Not trade.IsOpen AndAlso trade.ExitIndex >= ctx.StartIndex AndAlso trade.ExitIndex <= ctx.EndIndex Then
                        Dim x = ctx.Mapper.IndexToX(trade.ExitIndex)
                        Dim y = ctx.Mapper.PriceToY(trade.ExitPrice)
                        canvas.DrawLine(x - 5.0F, y - 5.0F, x + 5.0F, y + 5.0F, sellPaint)
                        canvas.DrawLine(x - 5.0F, y + 5.0F, x + 5.0F, y - 5.0F, sellPaint)
                        canvas.DrawText($"S {trade.ReturnPct:+0.0;-0.0;0.0}%", x + 7.0F, y + 4.0F, textPaint)
                    End If
                Next
            End Using
        End Sub

        Private Shared Function FirstUsableJmaShadeRule(rules As IEnumerable(Of OverlayShadeRule),
                                                        engine As IndicatorEngine) As OverlayShadeRule
            If rules Is Nothing Then Return Nothing
            For Each rule In rules
                If rule IsNot Nothing AndAlso
                   rule.IndicatorA.StartsWith("JMA_", StringComparison.OrdinalIgnoreCase) AndAlso
                   rule.IndicatorB.StartsWith("JMA_", StringComparison.OrdinalIgnoreCase) AndAlso
                   engine.Results.ContainsKey(rule.IndicatorA) AndAlso
                   engine.Results.ContainsKey(rule.IndicatorB) Then Return rule
            Next
            Return Nothing
        End Function

        Private Shared Function RequiredMacdResults(engine As IndicatorEngine) As IndicatorResultRingBuffer
            Dim results As IndicatorResultRingBuffer = Nothing
            If engine.Results.TryGetValue(BasicJmaMacdStrategy.RequiredMacdName, results) Then
                Return results
            End If
            Return Nothing
        End Function

        Private Shared Function BuildCacheKey(ctx As ChartContext,
                                              rule As OverlayShadeRule,
                                              macd As IndicatorResultRingBuffer) As String
            Dim last = ctx.Candles(ctx.Candles.Count - 1)
            Dim options = If(ctx.StrategyReentryOptions, New StrategyReentryLockOptions())
            Return $"{ctx.Candles.Count}|{last.Dt.Ticks}|{ctx.StrategyCapture.CandleIndex}|" &
                   $"{ctx.StrategyCapture.CapturePrice:R}|{rule.IndicatorA}|{rule.IndicatorB}|{macd.Count}|" &
                   $"{CInt(options.Mode)}|{options.ThresholdPct:R}"
        End Function

        Public Sub Dispose() Implements IDisposable.Dispose
            _fixedEvaluation = Nothing
            _cached = Nothing
            _cacheKey = ""
        End Sub
    End Class
End Namespace
