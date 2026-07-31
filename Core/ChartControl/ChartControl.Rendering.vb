Imports SkiaSharp
Imports SkiaSharp.Views.Desktop
Imports ChartKit.Abstractions

Namespace Core
    Public Partial Class ChartControl
        Private Sub CalcLayout()
            Dim w = _sk.Width
            Dim h = _sk.Height
            Dim cL = _theme.MarginLeft
            Dim cR = w - _theme.MarginRight
            Dim cT = _theme.MarginTop
            Dim cB = h - _theme.MarginBottom
            Dim totalH = cB - cT

            _layoutPanelIndexes.Clear()
            If _indicatorEngine IsNot Nothing Then
                Dim indicators As IReadOnlyList(Of IIndicator) = _indicatorEngine.GetAllView()
                For indicatorIndex As Integer = 0 To indicators.Count - 1
                    Dim panelIndex As Integer = indicators(indicatorIndex).PanelIndex
                    If panelIndex > 0 AndAlso
                       Not _layoutPanelIndexes.Contains(panelIndex) Then
                        _layoutPanelIndexes.Add(panelIndex)
                    End If
                Next
            End If
            _layoutPanelIndexes.Sort()
            Dim nPanels = _layoutPanelIndexes.Count

            While _panelRatios.Count < nPanels : _panelRatios.Add(0.15F) : End While
            While _panelRatios.Count > nPanels : _panelRatios.RemoveAt(_panelRatios.Count - 1) : End While

            Dim volumeVisible = Not (
                _registry.Exists("Volume") AndAlso
                Not _registry.IsLayerVisible("Volume"))
            Dim panelsVisible = Not (
                _registry.Exists("Panels") AndAlso
                Not _registry.IsLayerVisible("Panels"))
            Dim layout = PanelLayoutCalculator.Calculate(
                totalH,
                _theme.VolumeRatio,
                _panelRatios,
                volumeVisible,
                panelsVisible)
            Dim mainH = layout.MainHeight
            Dim volH = layout.VolumeHeight

            _mainRect = New SKRect(cL, cT, cR, cT + mainH)
            _volumeRect = New SKRect(cL, _mainRect.Bottom, cR, _mainRect.Bottom + volH)
            _panelRects.Clear()
            If panelsVisible Then
                Dim y As Single = _volumeRect.Bottom
                For k = 0 To layout.PanelHeights.Count - 1
                    Dim ph As Single = layout.PanelHeights(k)
                    _panelRects.Add(New SKRect(cL, y, cR, y + ph))
                    y += ph
                Next
            End If
        End Sub

        Private Sub CalcPriceRange()
            _priceHigh = Single.MinValue
            _priceLow = Single.MaxValue
            _volumeMax = 0
            Dim s = Math.Max(0, _vs.StartIndex)
            Dim en = Math.Min(_candles.Count - 1, _vs.EndIndex)
            If s > en Then
                _priceHigh = 100
                _priceLow = 0
                _volumeMax = 1
                Return
            End If
            For i = s To en
                Dim c = _candles(i)
                If c.High > 0 Then
                    If c.High > _priceHigh Then _priceHigh = c.High
                    If c.Low < _priceLow Then _priceLow = c.Low
                End If
                If c.Volume > _volumeMax Then _volumeMax = c.Volume
            Next
            If _priceHigh = Single.MinValue OrElse _priceLow = Single.MaxValue Then
                _priceHigh = 100
                _priceLow = 0
            End If
            Dim margin = (_priceHigh - _priceLow) * 0.05F
            If margin < 1 Then margin = 1

            If _isAutoScaleY OrElse _manualMaxP = 0 OrElse _manualMinP = 0 Then
                _priceHigh += margin
                _priceLow -= margin
                _manualMaxP = _priceHigh
                _manualMinP = _priceLow
            Else
                _priceHigh = _manualMaxP
                _priceLow = _manualMinP
            End If
            If _volumeMax = 0 Then _volumeMax = 1
        End Sub

        Private Sub OnPaintSurface(sender As Object, e As SKPaintSurfaceEventArgs) Handles _sk.PaintSurface
            Dim canvas = e.Surface.Canvas
            canvas.Clear(_theme.Background)
            If _candles.Count = 0 Then Return

            CalcLayout()
            CalcPriceRange()
            Dim mapper As New CoordinateMapper(_mainRect, _volumeRect, _vs.StartIndex,
                                               _vs.CandleWidth, _vs.Gap, _priceHigh, _priceLow, _volumeMax)
            Dim ctx As New ChartContext With {
                .Candles = _candles, .Mapper = mapper, .Theme = _theme,
                .MainRect = _mainRect, .VolumeRect = _volumeRect,
                .CandleWidth = _vs.CandleWidth, .StartIndex = _vs.StartIndex, .EndIndex = _vs.EndIndex,
                .TotalWidth = _sk.Width, .TotalHeight = _sk.Height,
                .PriceHigh = _priceHigh, .PriceLow = _priceLow, .PctAxisMode = _pctAxisMode, .VolumeMax = _volumeMax,
                .ShowDayChangeLines = True, .Engine = _indicatorEngine,
                .MouseInside = _mouseInside, .ShowCrosshair = _vs.ShowCrosshair,
                .CrosshairX = _vs.CrosshairX, .CrosshairY = _vs.CrosshairY,
                .PanelRects = _panelRects, .PanelBaselines = _panelBaselines,
                .PanelZones = _panelZones, .ShadeRules = _shadeRules, .SignalRules = _signalRules,
                .StrategyCapture = _strategyCapture,
                .StrategyReentryOptions = _strategyReentryOptions,
                .PanelScales = _panelScales}
            PanelScaleCalculator.CalculateInto(ctx, _panelScales, _panelScaleIndexes)

            Dim orderedLayers As IReadOnlyList(Of IChartLayer) = _registry.OrderedView()
            For layerIndex As Integer = 0 To orderedLayers.Count - 1
                orderedLayers(layerIndex).Draw(canvas, ctx)
            Next
        End Sub

        Protected Overrides Sub Dispose(disposing As Boolean)
            If disposing Then
                If _dataSource IsNot Nothing Then
                    RemoveHandler _dataSource.CandleAppended, AddressOf OnCandleAppended
                    RemoveHandler _dataSource.CandleUpdated, AddressOf OnCandleUpdated
                    _dataSource.StopRealtime()
                    _dataSource = Nothing
                End If
                If _frameTimer IsNot Nothing Then
                    _frameTimer.Stop()
                    _frameTimer.Dispose()
                    _frameTimer = Nothing
                End If
                If _stateSaveTimer IsNot Nothing Then
                    If _stateSaveTimer.Enabled Then SaveState()
                    _stateSaveTimer.Stop()
                    _stateSaveTimer.Dispose()
                    _stateSaveTimer = Nothing
                End If
                _registry.Dispose()
            End If
            MyBase.Dispose(disposing)
        End Sub

        Private Function FindSubPanelSlot(y As Single) As Integer
            If _panelRects Is Nothing OrElse _panelRects.Count = 0 Then Return -1
            For k = 0 To _panelRects.Count - 1
                Dim r = _panelRects(k)
                If y >= r.Top AndAlso y <= r.Bottom Then Return k
            Next
            Return -1
        End Function

        Private Function HitPanelBorder(y As Single) As Integer
            If _panelRects Is Nothing OrElse _panelRects.Count = 0 Then Return -1
            For k = 0 To _panelRects.Count - 1
                If Math.Abs(y - _panelRects(k).Top) <= PANEL_HIT Then Return k
            Next
            Return -1
        End Function
    End Class
End Namespace
