Imports SkiaSharp
Imports ChartKit.Abstractions

Namespace Layers
    '' 서브패널(PanelIndex>0) 지표 렌더링. X=Mapper.IndexToX, Y=패널 자체 min/max.
    '' 여러 라인 키(Value/OBV/Signal 등)를 각기 다른 색으로 그림.
    '' Upper/Lower=기준선, Direction 등 비라인 키는 제외.
    Public Class PanelLayer
        Implements IChartLayer

        Public ReadOnly Property Id As String Implements IChartLayer.Id
            Get
                Return "Panels"
            End Get
        End Property
        Public Property IsVisible As Boolean = True Implements IChartLayer.IsVisible
        Public ReadOnly Property ZOrder As Integer Implements IChartLayer.ZOrder
            Get
                Return 35
            End Get
        End Property

        Private Shared ReadOnly LineColors As SKColor() = {
            New SKColor(255, 193, 7), New SKColor(0, 188, 212),
            New SKColor(233, 30, 99), New SKColor(76, 175, 80),
            New SKColor(171, 71, 188), New SKColor(255, 152, 0)}

        Private ReadOnly _linePaint As New SKPaint With {
            .Style = SKPaintStyle.Stroke, .StrokeWidth = 1.4F, .IsAntialias = True}
        Private ReadOnly _basePaint As New SKPaint With {
            .Style = SKPaintStyle.Stroke, .StrokeWidth = 1.0F, .IsAntialias = True,
            .Color = New SKColor(120, 120, 120), .PathEffect = SKPathEffect.CreateDash(New Single() {4, 4}, 0)}
        '' 과열 음영 (키움 참조: 연주황) / 침체 음영 (연하늘) - 반투명
        Private ReadOnly _overZonePaint As New SKPaint With {
            .Style = SKPaintStyle.Fill, .IsAntialias = False,
            .Color = New SKColor(255, 140, 90, 45)}
        Private ReadOnly _underZonePaint As New SKPaint With {
            .Style = SKPaintStyle.Fill, .IsAntialias = False,
            .Color = New SKColor(120, 180, 235, 45)}
        '' MACD 히스토그램 막대: 양수(붉은계열)/음수(푸른계열)
        Private ReadOnly _histUpPaint As New SKPaint With {.Style = SKPaintStyle.Fill, .IsAntialias = False, .Color = New SKColor(230, 90, 100, 200)}
        Private ReadOnly _histDnPaint As New SKPaint With {.Style = SKPaintStyle.Fill, .IsAntialias = False, .Color = New SKColor(80, 150, 230, 200)}
        Private ReadOnly _borderPaint As New SKPaint With {
            .Style = SKPaintStyle.Stroke, .StrokeWidth = 1.0F, .IsAntialias = True,
            .Color = New SKColor(70, 70, 70)}
        Private _axisText As SKPaint
        Private _nameText As SKPaint
        Private ReadOnly _path As New SKPath()
        Private _paintThemeVersion As Integer = -1

        '' 라인으로 그리지 않는 키 (기준선/플래그)
        Private Shared Function IsBaselineKey(k As String) As Boolean
            Select Case k.ToUpperInvariant()
                Case "UPPER", "LOWER", "BASELINE" : Return True
                Case Else : Return False
            End Select
        End Function
        Private Shared Function IsIgnoredKey(k As String) As Boolean
            Select Case k.ToUpperInvariant()
                Case "DIRECTION", "HIST", "HISTOGRAM", "MA", "ATR", "SLOPE" : Return True
                Case Else : Return False
            End Select
        End Function

        Public Sub Draw(canvas As SKCanvas, ctx As ChartContext) Implements IChartLayer.Draw
            If ctx.Engine Is Nothing Then Return
            If ctx.PanelRects Is Nothing OrElse ctx.PanelRects.Count = 0 Then Return
            If ctx.Candles Is Nothing OrElse ctx.Candles.Count = 0 Then Return

            EnsurePaints(ctx.Theme)

            Dim panelIdxs As New List(Of Integer)()
            For Each ind In ctx.Engine.GetAll()
                If ind.PanelIndex > 0 AndAlso Not panelIdxs.Contains(ind.PanelIndex) Then panelIdxs.Add(ind.PanelIndex)
            Next
            panelIdxs.Sort()

            Dim s = Math.Max(0, ctx.StartIndex)
            Dim en = Math.Min(ctx.Candles.Count - 1, ctx.EndIndex)

            Dim legendIdx As Integer = ctx.Candles.Count - 1
            If ctx.MouseInside AndAlso ctx.ShowCrosshair Then
                Dim mx = ctx.CrosshairX
                If mx >= ctx.MainRect.Left AndAlso mx <= ctx.MainRect.Right Then
                    Dim hoverIdx = ctx.Mapper.XToIndex(mx)
                    If hoverIdx >= 0 AndAlso hoverIdx < ctx.Candles.Count Then legendIdx = hoverIdx
                End If
            End If

            For slot = 0 To panelIdxs.Count - 1
                If slot >= ctx.PanelRects.Count Then Exit For
                Dim rect = ctx.PanelRects(slot)
                Dim panelIndex = panelIdxs(slot)
                canvas.DrawRect(rect, _borderPaint)

                Dim inds = ctx.Engine.GetAll().Where(Function(x) x.PanelIndex = panelIndex).ToList()

                Dim scale As ChartKit.Core.PanelScale = Nothing
                If ctx.PanelScales Is Nothing OrElse Not ctx.PanelScales.TryGetValue(slot, scale) Then Continue For
                Dim vmin = scale.Minimum
                Dim vmax = scale.Maximum
                Dim baselines = scale.Baselines

                '' 과열/침체 음영 패스 (지표선/축/기준선보다 먼저 = 뒤에 깔림)
                If ctx.PanelZones IsNot Nothing Then
                    Dim zone As ChartKit.Core.PanelZoneState = Nothing
                    If ctx.PanelZones.TryGetValue(slot, zone) AndAlso zone IsNot Nothing Then
                        If zone.OverValue.HasValue Then
                            Dim yv = ValueToY(rect, zone.OverValue.Value, vmin, vmax)
                            yv = Math.Max(rect.Top, Math.Min(rect.Bottom, yv))
                            canvas.DrawRect(New SKRect(rect.Left, rect.Top, rect.Right, yv), _overZonePaint)
                        End If
                        If zone.UnderValue.HasValue Then
                            Dim yv2 = ValueToY(rect, zone.UnderValue.Value, vmin, vmax)
                            yv2 = Math.Max(rect.Top, Math.Min(rect.Bottom, yv2))
                            canvas.DrawRect(New SKRect(rect.Left, yv2, rect.Right, rect.Bottom), _underZonePaint)
                        End If
                    End If
                End If
                DrawPanelAxisY(canvas, rect, vmin, vmax)
                '' 모든 기준선 그리기 (Baseline=중심선은 옅은 실선으로 강조)
                For Each bk In baselines
                    Dim emphasize As Boolean = (bk.Key = "BASELINE")
                    DrawBaseline(canvas, rect, bk.Value, vmin, vmax, emphasize)
                Next
                '' ── 사용자 편집 기준선 (값만, 회색 점선 고정) ──
                If ctx.PanelBaselines IsNot Nothing Then
                    Dim userLevels As List(Of Single) = Nothing
                    If ctx.PanelBaselines.TryGetValue(slot, userLevels) AndAlso userLevels IsNot Nothing Then
                        For Each lv In userLevels
                            If lv >= vmin AndAlso lv <= vmax Then
                                DrawBaseline(canvas, rect, lv, vmin, vmax, False)
                            End If
                        Next
                    End If
                End If

                '' ── MACD 히스토그램 막대 패스 (Hist 키, 0선 기준, 라인보다 먼저) ──
                For Each ind In inds
                    Dim hres As ChartKit.Models.IndicatorResultRingBuffer = Nothing
                    If Not ctx.Engine.Results.TryGetValue(ind.Name, hres) Then Continue For
                    If hres Is Nothing OrElse hres.Count = 0 Then Continue For
                    Dim hasHist = False
                    For Each rr In hres
                        If rr IsNot Nothing AndAlso rr.Values IsNot Nothing AndAlso rr.Values.ContainsKey("Hist") Then hasHist = True : Exit For
                    Next
                    If Not hasHist Then Continue For
                    Dim y0 = ValueToY(rect, 0.0F, vmin, vmax)
                    y0 = Math.Max(rect.Top, Math.Min(rect.Bottom, y0))
                    Dim bw As Single = Math.Max(1.0F, ctx.CandleWidth * 0.6F)
                    Dim maxIh = Math.Min(en, hres.Count - 1)
                    For i = s To maxIh
                        Dim r = hres(i)
                        If r Is Nothing OrElse r.Values Is Nothing Then Continue For
                        Dim hv As Single
                        If Not r.Values.TryGetValue("Hist", hv) Then Continue For
                        If Single.IsNaN(hv) Then Continue For
                        Dim yv = ValueToY(rect, hv, vmin, vmax)
                        yv = Math.Max(rect.Top, Math.Min(rect.Bottom, yv))
                        Dim px = ctx.Mapper.IndexToX(i)
                        Dim top = Math.Min(y0, yv)
                        Dim bot = Math.Max(y0, yv)
                        canvas.DrawRect(New SKRect(px - bw / 2, top, px + bw / 2, bot), If(hv >= 0, _histUpPaint, _histDnPaint))
                    Next
                Next

                '' ── 각 지표의 라인 키들을 색을 바꿔가며 그림 + 패널 레전드 ──
                Dim colorIdx = 0
                Dim legendX = rect.Left + 4
                Dim legendY = rect.Top + 12
                For Each ind In inds
                    Dim results As ChartKit.Models.IndicatorResultRingBuffer = Nothing
                    If Not ctx.Engine.Results.TryGetValue(ind.Name, results) Then Continue For
                    If results Is Nothing OrElse results.Count = 0 Then Continue For

                    '' 라인 키 목록 (샘플 결과에서 추출, 무시/기준선 제외)
                    Dim lineKeys = GetLineKeys(results)

                    '' 지표명 레전드 헤더
                    _nameText.Color = New SKColor(200, 200, 200)
                    canvas.DrawText(ind.DisplayName, legendX, legendY, _nameText)
                    legendX += _nameText.MeasureText(ind.DisplayName) + 8

                    For Each key In lineKeys
                        Dim col = LineColors(colorIdx Mod LineColors.Length)
                        _linePaint.Color = col
                        _path.Reset()
                        Dim started = False
                        Dim maxI = Math.Min(en, results.Count - 1)
                        For i = s To maxI
                            Dim r = results(i)
                            If r Is Nothing OrElse r.Values Is Nothing Then started = False : Continue For
                            If Not r.Values.ContainsKey(key) Then started = False : Continue For
                            Dim v = r.Values(key)
                            If Single.IsNaN(v) Then started = False : Continue For
                            Dim px = ctx.Mapper.IndexToX(i)
                            Dim py = ValueToY(rect, v, vmin, vmax)
                            If Not started Then
                                _path.MoveTo(px, py) : started = True
                            Else
                                _path.LineTo(px, py)
                            End If
                        Next
                        If started Then canvas.DrawPath(_path, _linePaint)

                        '' 레전드: "키 값" + (직전봉 대비 변화율%)
                        Dim lv As Single = Single.NaN
                        Dim pv As Single = Single.NaN
                        If legendIdx >= 0 AndAlso legendIdx < results.Count Then lv = results(legendIdx).Val(key)
                        If legendIdx - 1 >= 0 AndAlso legendIdx - 1 < results.Count Then pv = results(legendIdx - 1).Val(key)
                        Dim txt As String = If(Single.IsNaN(lv), $"{key} --", $"{key} {FormatLegend(lv)}")
                        _nameText.Color = col
                        canvas.DrawText(txt, legendX, legendY, _nameText)
                        legendX += _nameText.MeasureText(txt) + 6

                        '' 변화율(%) : 색을 달리해 이어 그림
                        If Not Single.IsNaN(lv) AndAlso Not Single.IsNaN(pv) AndAlso Math.Abs(pv) > 0.0000001F Then
                            Dim pct As Single = (lv - pv) / Math.Abs(pv) * 100.0F
                            Dim sign As String = If(pct >= 0, "+", "")
                            Dim pctTxt As String = $"({sign}{pct:N2}%)"
                            _nameText.Color = If(pct >= 0, ctx.Theme.BullCandle, ctx.Theme.BearCandle)
                            canvas.DrawText(pctTxt, legendX, legendY, _nameText)
                            legendX += _nameText.MeasureText(pctTxt) + 10
                        Else
                            legendX += 4
                        End If
                        colorIdx += 1
                    Next
                    legendX += 6
                Next
            Next
        End Sub

        '' 결과에서 라인으로 그릴 키 목록 (첫 유효 결과 기준, 순서 보존)
        Private Shared Function GetLineKeys(results As IReadOnlyList(Of IndicatorResult)) As List(Of String)
            Dim keys As New List(Of String)()
            For Each r In results
                If r Is Nothing OrElse r.Values Is Nothing OrElse r.Values.Count = 0 Then Continue For
                For Each k In r.Values.Keys
                    If r.KindOf(k) <> SeriesKind.Line Then Continue For
                    If Not keys.Contains(k) Then keys.Add(k)
                Next
                If keys.Count > 0 Then Exit For
            Next
            Return keys
        End Function

        Private Sub EnsurePaints(t As ChartTheme)
            If _axisText Is Nothing OrElse _paintThemeVersion <> t.Version Then
                _axisText?.Dispose()
                _nameText?.Dispose()
                _axisText = New SKPaint With {
                    .Color = t.AxisText, .TextSize = t.AxisFontSize, .IsAntialias = True,
                    .Typeface = SKTypeface.FromFamilyName("Consolas"), .TextAlign = SKTextAlign.Left}
            End If
            If _nameText Is Nothing Then
                _nameText = New SKPaint With {
                    .TextSize = 11, .IsAntialias = True,
                    .Typeface = SKTypeface.FromFamilyName("맑은 고딕")}
                _paintThemeVersion = t.Version
            End If
        End Sub

        Private Sub DrawPanelAxisY(canvas As SKCanvas, rect As SKRect, vmin As Single, vmax As Single)
            Dim gridStep = CSng(CalculateNiceStep(vmax - vmin, 4))
            If gridStep <= 0 Then Return
            Dim x = rect.Right + 6
            Dim p = CSng(Math.Ceiling(vmin / gridStep) * gridStep)
            While p < vmax
                Dim y = ValueToY(rect, p, vmin, vmax)
                If y >= rect.Top + 8 AndAlso y <= rect.Bottom - 4 Then
                    canvas.DrawText(FormatAxisValue(p), x, y + 4, _axisText)
                End If
                p += gridStep
            End While
        End Sub

        Private Sub DrawBaseline(canvas As SKCanvas, rect As SKRect, level As Single, vmin As Single, vmax As Single, Optional emphasize As Boolean = False)
            Dim y = ValueToY(rect, level, vmin, vmax)
            If emphasize Then
                Using p As New SKPaint With {
                    .Style = SKPaintStyle.Stroke, .StrokeWidth = 1.0F, .IsAntialias = True,
                    .Color = New SKColor(200, 200, 200, 140)}
                    canvas.DrawLine(rect.Left, y, rect.Right, y, p)
                End Using
            Else
                canvas.DrawLine(rect.Left, y, rect.Right, y, _basePaint)
            End If
            '' 기준선 값 레이블 (우측 축)
            If y >= rect.Top + 8 AndAlso y <= rect.Bottom - 4 Then
                canvas.DrawText(FormatAxisValue(level), rect.Right + 6, y + 4, _axisText)
            End If
        End Sub

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

        Private Shared Function FormatAxisValue(v As Single) As String
            Dim a = Math.Abs(v)
            If a >= 1000000 Then Return (v / 1000000).ToString("N1") & "M"
            If a >= 1000 Then Return (v / 1000).ToString("N0") & "K"
            If a >= 100 Then Return v.ToString("N0")
            Return v.ToString("N1")
        End Function

        Private Shared Function FormatLegend(v As Single) As String
            Dim a = Math.Abs(v)
            If a >= 1000000 Then Return (v / 1000000).ToString("N2") & "M"
            If a >= 1000 Then Return v.ToString("N0")
            If a >= 100 Then Return v.ToString("N0")
            Return v.ToString("N1")
        End Function

        Private Shared Function ValueToY(rect As SKRect, v As Single, vmin As Single, vmax As Single) As Single
            Dim t = (v - vmin) / (vmax - vmin)
            Return rect.Bottom - t * rect.Height
        End Function

        Public Sub Dispose() Implements IDisposable.Dispose
            _linePaint.Dispose()
            _basePaint.Dispose()
            _overZonePaint.Dispose()
            _underZonePaint.Dispose()
            _histUpPaint.Dispose()
            _histDnPaint.Dispose()
            _borderPaint.Dispose()
            _axisText?.Dispose() : _axisText = Nothing
            _nameText?.Dispose() : _nameText = Nothing
            _path.Dispose()
        End Sub
    End Class
End Namespace
