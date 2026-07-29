Imports SkiaSharp
Imports ChartKit.Abstractions

Namespace Layers
    '' 오버레이(PanelIndex=0) 지표 라인 그리기. 원본 DrawOverlayIndicators 이식.
    '' 서브패널(PanelIndex>0)은 PanelLayer에서 별도 처리 예정.
    Public Class IndicatorLayer
        Implements IChartLayer

        Public ReadOnly Property Id As String Implements IChartLayer.Id
            Get
                Return "Indicators"
            End Get
        End Property
        Public Property IsVisible As Boolean = True Implements IChartLayer.IsVisible
        Public ReadOnly Property ZOrder As Integer Implements IChartLayer.ZOrder
            Get
                Return 30
            End Get
        End Property

        Private Shared ReadOnly IndicatorColors As SKColor() = {
            New SKColor(255, 193, 7), New SKColor(0, 188, 212),
            New SKColor(233, 30, 99), New SKColor(76, 175, 80),
            New SKColor(255, 152, 0), New SKColor(171, 71, 188),
            New SKColor(255, 255, 255), New SKColor(139, 195, 74)}

        Private ReadOnly _paints As New Dictionary(Of String, SKPaint)
        Private ReadOnly _path As New SKPath()

        Public Sub Draw(canvas As SKCanvas, ctx As ChartContext) Implements IChartLayer.Draw
            If ctx.Engine Is Nothing Then Return
            If ctx.Candles Is Nothing OrElse ctx.Candles.Count = 0 Then Return

            Dim colorIdx = 0
            Dim s = Math.Max(0, ctx.StartIndex)
            Dim en = Math.Min(ctx.Candles.Count - 1, ctx.EndIndex)

            For Each ind In ctx.Engine.GetAll()
                If ind.PanelIndex > 0 Then Continue For
                Dim results As List(Of IndicatorResult) = Nothing
                If Not ctx.Engine.Results.TryGetValue(ind.Name, results) Then Continue For
                If results Is Nothing OrElse results.Count = 0 Then Continue For

                Dim sampleR = results.FirstOrDefault(Function(r) r IsNot Nothing AndAlso r.Values IsNot Nothing AndAlso r.Values.Count > 0)
                If sampleR Is Nothing Then Continue For

                '' 방향색 지표 판정 : Up/Down 키를 둘 다 가지면 방향별로 색을 다르게, Value 는 라인 생략
                Dim isDirectional As Boolean =
                    sampleR.Values.Keys.Any(Function(k) String.Equals(k, "Up", StringComparison.OrdinalIgnoreCase)) AndAlso
                    sampleR.Values.Keys.Any(Function(k) String.Equals(k, "Down", StringComparison.OrdinalIgnoreCase))

                If isDirectional Then
                    '' Value 본선을 끊김 없이 이어 그리되, 방향(Up=상승/Down=하락)에 따라 세그먼트 색만 전환
                    DrawDirectionalValue(canvas, ctx, results, ind.Name, s, en)
                    colorIdx += 1
                    Continue For
                End If

                '' 이 지표의 본선 색 (밴드가 있어도 colorIdx 는 본선 1개만 증가)
                Dim baseColor = IndicatorColors(colorIdx Mod IndicatorColors.Length)

                For Each valueKey In sampleR.Values.Keys
                    If Not IsOverlayPriceValueKey(valueKey) Then Continue For

                    Dim isBand = IsBandKey(valueKey)
                    Dim paint As SKPaint
                    If isBand Then
                        paint = GetBandPaint(ind.Name & "_" & valueKey, baseColor)
                    Else
                        paint = GetPaint(ind.Name & "_" & valueKey, colorIdx)
                    End If

                    _path.Reset()
                    Dim started = False
                    Dim maxI = Math.Min(en, results.Count - 1)
                    For i As Integer = s To maxI
                        Dim r = results(i)
                        If r Is Nothing OrElse r.Values Is Nothing Then Continue For
                        If Not r.Values.ContainsKey(valueKey) Then Continue For
                        Dim v = r.Values(valueKey)
                        If Single.IsNaN(v) OrElse v <= 0 Then
                            started = False
                            Continue For
                        End If
                        Dim px = ctx.Mapper.IndexToX(i)
                        Dim py = ctx.Mapper.PriceToY(v)
                        If Not started Then
                            _path.MoveTo(px, py)
                            started = True
                        Else
                            _path.LineTo(px, py)
                        End If
                    Next
                    If started Then canvas.DrawPath(_path, paint)
                Next

                colorIdx += 1
            Next
        End Sub

        '' Value 본선을 연속으로 그리며, 각 봉의 방향(Up non-NaN=상승, Down non-NaN=하락)에 맞춰
        '' 세그먼트 색만 전환. Value 는 NaN/0 이 없으므로 끊기지 않는다.
        Private Sub DrawDirectionalValue(canvas As SKCanvas, ctx As ChartContext,
                                         results As List(Of IndicatorResult),
                                         indName As String, s As Integer, en As Integer)
            Dim bullPaint = GetDirPaint(indName & "_bull", ctx.Theme.BullCandle)
            Dim bearPaint = GetDirPaint(indName & "_bear", ctx.Theme.BearCandle)

            Dim maxI = Math.Min(en, results.Count - 1)
            Dim havePrev = False
            Dim prevX As Single = 0, prevY As Single = 0
            Dim prevDir As Integer = 0   '' 1=상승, -1=하락

            For i As Integer = s To maxI
                Dim r = results(i)
                If r Is Nothing OrElse r.Values Is Nothing Then havePrev = False : Continue For
                Dim v = r.Val("Value")
                If Single.IsNaN(v) Then havePrev = False : Continue For

                '' 방향 판정 : Up 이 유효하면 상승, Down 이 유효하면 하락, 아니면 이전 방향 유지
                Dim up = r.Val("Up")
                Dim dn = r.Val("Down")
                Dim dir As Integer
                If Not Single.IsNaN(up) Then
                    dir = 1
                ElseIf Not Single.IsNaN(dn) Then
                    dir = -1
                Else
                    dir = If(prevDir = 0, 1, prevDir)
                End If

                Dim px = ctx.Mapper.IndexToX(i)
                Dim py = ctx.Mapper.PriceToY(v)

                If havePrev Then
                    '' 이 세그먼트 색 = 현재 봉 방향 (전환 봉도 자연스럽게 이어짐)
                    Dim segPaint = If(dir = 1, bullPaint, bearPaint)
                    canvas.DrawLine(prevX, prevY, px, py, segPaint)
                End If

                prevX = px : prevY = py : prevDir = dir : havePrev = True
            Next
        End Sub

        Private Function GetDirPaint(key As String, color As SKColor) As SKPaint
            If _paints.ContainsKey(key) Then Return _paints(key)
            Dim p As New SKPaint With {
                .Style = SKPaintStyle.Stroke, .Color = color,
                .StrokeWidth = 1.8F, .IsAntialias = True}
            _paints(key) = p
            Return p
        End Function

        '' 밴드 키 판정 (VWAP Upper1/Lower1/Upper2/Lower2, 볼린저 UpperBand/LowerBand 등)
        Private Shared Function IsBandKey(valueKey As String) As Boolean
            If String.IsNullOrWhiteSpace(valueKey) Then Return False
            Select Case valueKey.ToUpperInvariant()
                Case "UPPER1", "UPPER2", "LOWER1", "LOWER2", "UPPERBAND", "LOWERBAND", "UPPER", "LOWER"
                    Return True
                Case Else
                    Return False
            End Select
        End Function

        '' 밴드용 페인트 : 본선 색을 알파 90 + 얇게(0.8px)
        Private Function GetBandPaint(key As String, baseColor As SKColor) As SKPaint
            If _paints.ContainsKey(key) Then Return _paints(key)
            Dim p As New SKPaint With {
                .Style = SKPaintStyle.Stroke,
                .Color = New SKColor(baseColor.Red, baseColor.Green, baseColor.Blue, 90),
                .StrokeWidth = 0.8F, .IsAntialias = True}
            _paints(key) = p
            Return p
        End Function

        '' ===== 원본 IsOverlayPriceValueKey 그대로 =====
        Private Shared Function IsOverlayPriceValueKey(valueKey As String) As Boolean
            If String.IsNullOrWhiteSpace(valueKey) Then Return False
            Select Case valueKey.ToUpperInvariant()
                Case "VALUE", "UP", "DOWN", "MIDDLE",
                     "UPPER", "LOWER",
                     "UPPER1", "UPPER2", "LOWER1", "LOWER2",
                     "UPPERBAND", "LOWERBAND"
                    Return True
                Case Else
                    Return False
            End Select
        End Function

        Private Function GetPaint(key As String, colorIndex As Integer) As SKPaint
            If _paints.ContainsKey(key) Then Return _paints(key)
            Dim p As New SKPaint With {
                .Style = SKPaintStyle.Stroke,
                .Color = IndicatorColors(colorIndex Mod IndicatorColors.Length),
                .StrokeWidth = 1.5F, .IsAntialias = True}
            _paints(key) = p
            Return p
        End Function
    End Class
End Namespace
