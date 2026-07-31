Imports System.Windows.Forms

Namespace Core
    Public Partial Class ChartControl
        Private Sub OnFrameTimer(sender As Object, e As EventArgs)
            If Not _needsRepaint Then Return
            _needsRepaint = False
            _sk.Invalidate()
        End Sub

        Private Sub OnGLMouseMove(sender As Object, e As MouseEventArgs)
            _vs.CrosshairX = e.X
            If _isPanelResizing AndAlso _resizePanelSlot >= 0 AndAlso _resizePanelSlot < _panelRatios.Count Then
                Dim h = _sk.Height
                Dim totalH = (h - _theme.MarginBottom) - _theme.MarginTop
                If totalH > 0 Then
                    Dim dy = e.Y - _resizeStartY
                    Dim newRatio = _resizeStartRatio - (dy / totalH)
                    If newRatio < PANEL_MIN_RATIO Then newRatio = PANEL_MIN_RATIO
                    If newRatio > 0.6F Then newRatio = 0.6F
                    _panelRatios(_resizePanelSlot) = newRatio
                End If
                _needsRepaint = True
                Return
            ElseIf HitPanelBorder(e.Y) >= 0 Then
                _sk.Cursor = Cursors.SizeNS
            Else
                _sk.Cursor = Cursors.Default
            End If
            _vs.CrosshairY = e.Y

            If _isDragging Then
                Dim dx = e.X - _dragStartX
                Dim candleShift = CInt(dx / (_vs.CandleWidth + _vs.Gap))
                _vs.StartIndex = Math.Max(0, Math.Min(_dragStartIndex - candleShift, GetMaxDragStartIndex()))

                Dim dy = e.Y - _dragStartY
                Dim range = _dragStartMaxP - _dragStartMinP
                If range <= 0 Then range = Math.Max(1.0F, _priceHigh - _priceLow)

                If _mainRect.Height > 0 Then
                    Dim delta = dy * (range / _mainRect.Height)
                    _isAutoScaleY = False
                    _manualMaxP = _dragStartMaxP + delta
                    _manualMinP = Math.Max(0.0F, _dragStartMinP + delta)
                End If
            ElseIf _isDraggingPrice Then
                Dim dy = e.Y - _dragStartY
                Dim range = _manualMaxP - _manualMinP
                If range <= 0 Then range = Math.Max(1.0F, _priceHigh - _priceLow)

                Dim zoomFactor = 1.0F + (dy * 0.01F)
                If zoomFactor < 0.2F Then zoomFactor = 0.2F
                If zoomFactor > 5.0F Then zoomFactor = 5.0F

                Dim center = (_manualMaxP + _manualMinP) / 2.0F
                If center <= 0 Then center = (_priceHigh + _priceLow) / 2.0F

                Dim newRange = Math.Max(1.0F, range * zoomFactor)
                _manualMaxP = center + (newRange / 2.0F)
                _manualMinP = Math.Max(0.0F, center - (newRange / 2.0F))
                _dragStartY = e.Y
            End If

            _lastMouseX = e.X
            _lastMouseY = e.Y
            _needsRepaint = True
        End Sub

        Private Sub OnGLMouseDown(sender As Object, e As MouseEventArgs)
            _sk.Focus()
            If e.Button = MouseButtons.Right Then
                ShowContextMenu(e.Location)
                Return
            End If
            _lastMouseX = e.X
            _lastMouseY = e.Y

            If e.Button = MouseButtons.Left Then
                Dim rs = HitPanelBorder(e.Y)
                If rs >= 0 Then
                    _isPanelResizing = True
                    _resizePanelSlot = rs
                    _resizeStartY = e.Y
                    _resizeStartRatio = If(rs < _panelRatios.Count, _panelRatios(rs), 0.15F)
                    Return
                End If
                If e.X > _mainRect.Right Then
                    _isDraggingPrice = True
                    _isAutoScaleY = False
                    If _manualMaxP <= _manualMinP Then
                        _manualMaxP = _priceHigh
                        _manualMinP = _priceLow
                    End If
                    _dragStartY = e.Y
                Else
                    _isDragging = True
                    _dragStartX = e.X
                    _dragStartY = e.Y
                    _dragStartIndex = _vs.StartIndex
                    _dragStartMaxP = If(_isAutoScaleY OrElse _manualMaxP <= _manualMinP, _priceHigh, _manualMaxP)
                    _dragStartMinP = If(_isAutoScaleY OrElse _manualMinP >= _manualMaxP, _priceLow, _manualMinP)
                End If
            End If
        End Sub

        Private Sub OnGLMouseUp(sender As Object, e As MouseEventArgs)
            If e.Button = MouseButtons.Left Then
                Dim stateChanged = _isDragging OrElse _isPanelResizing OrElse _isDraggingPrice
                _isDragging = False
                _isPanelResizing = False
                _resizePanelSlot = -1
                _isDraggingPrice = False
                If stateChanged Then ScheduleStateSave()
            End If
        End Sub

        Private Sub OnGLDoubleClick(sender As Object, e As MouseEventArgs) Handles _sk.MouseDoubleClick
            _isAutoScaleY = True
            _needsRepaint = True
            ScheduleStateSave()
        End Sub

        Private Sub OnGLMouseWheel(sender As Object, e As MouseEventArgs)
            Dim latestVisible = IsLatestCandleVisible()
            Dim zoom = If(e.Delta > 0, 1.2F, 0.8F)
            _vs.CandleWidth *= zoom
            If _vs.CandleWidth < 0.1F Then _vs.CandleWidth = 0.1F
            If _vs.CandleWidth > 50 Then _vs.CandleWidth = 50

            If _mainRect.Width <= 0 OrElse Single.IsNaN(_mainRect.Width) OrElse Single.IsInfinity(_mainRect.Width) Then Return

            Dim mouseIdx = XToIndex(e.X)
            Dim ratio As Double = (e.X - _mainRect.Left) / _mainRect.Width
            If Double.IsNaN(ratio) OrElse Double.IsInfinity(ratio) Then ratio = 0.5
            ratio = Math.Max(0.0, Math.Min(1.0, ratio))

            Dim denom As Double = _vs.CandleWidth + _vs.Gap
            If denom <= 0 OrElse Double.IsNaN(denom) OrElse Double.IsInfinity(denom) Then denom = 1.0

            Dim visibleD As Double = _mainRect.Width / denom
            If Double.IsNaN(visibleD) OrElse Double.IsInfinity(visibleD) Then visibleD = 1.0

            Dim newVisibleCount = Math.Max(1, CInt(Math.Truncate(visibleD)))
            Dim leftCount = CLng(Math.Truncate(CDbl(newVisibleCount) * ratio))
            Dim maxStart = Math.Max(0, _candles.Count - newVisibleCount)
            Dim desiredStart As Long = CLng(mouseIdx) - leftCount
            If desiredStart < 0 Then desiredStart = 0
            If desiredStart > maxStart Then desiredStart = maxStart

            If latestVisible Then
                KeepLatestVisibleForNewVisibleCount(newVisibleCount)
            Else
                _vs.VisibleCount = newVisibleCount
                _vs.StartIndex = CInt(desiredStart)
            End If

            _needsRepaint = True
            RaiseVisibleCandleCountChanged()
            ScheduleStateSave()
        End Sub

        Private Sub OnGLMouseEnter(sender As Object, e As EventArgs)
            _mouseInside = True
            _needsRepaint = True
        End Sub

        Private Sub OnGLMouseLeave(sender As Object, e As EventArgs)
            _mouseInside = False
            _isDragging = False
            _isDraggingPrice = False
            _needsRepaint = True
        End Sub

        Private Sub OnGLKeyDown(sender As Object, e As KeyEventArgs)
            Select Case e.KeyCode
                Case Keys.Left
                    _vs.StartIndex = Math.Max(0, _vs.StartIndex - 1)
                Case Keys.Right
                    _vs.StartIndex = Math.Min(Math.Max(0, _candles.Count - _vs.VisibleCount), _vs.StartIndex + 1)
                Case Keys.Home
                    _vs.StartIndex = 0
                Case Keys.End
                    MoveToLatestVisible()
                Case Keys.Add, Keys.Oemplus
                    _vs.CandleWidth *= 1.2F
                    _vs.VisibleCount = CInt(_mainRect.Width / (_vs.CandleWidth + _vs.Gap))
                    RaiseVisibleCandleCountChanged()
                Case Keys.Subtract, Keys.OemMinus
                    _vs.CandleWidth *= 0.8F
                    _vs.VisibleCount = CInt(_mainRect.Width / (_vs.CandleWidth + _vs.Gap))
                    RaiseVisibleCandleCountChanged()
                Case Keys.A
                    If e.Control AndAlso _candles.Count > 0 Then
                        _vs.StartIndex = 0
                        _vs.VisibleCount = _candles.Count
                        Dim pitch = _mainRect.Width / _vs.VisibleCount
                        _vs.Gap = Math.Min(DEFAULT_INITIAL_GAP, Math.Max(0.0F, pitch * 0.15F))
                        _vs.CandleWidth = Math.Max(0.1F, pitch - _vs.Gap)
                        RaiseVisibleCandleCountChanged()
                    End If
                Case Keys.C
                    _vs.ShowCrosshair = Not _vs.ShowCrosshair
                Case Keys.Up
                    If Not _isAutoScaleY Then
                        Dim range = _manualMaxP - _manualMinP
                        _manualMaxP += range * 0.1F
                        _manualMinP += range * 0.1F
                    End If
                Case Keys.Down
                    If Not _isAutoScaleY Then
                        Dim range = _manualMaxP - _manualMinP
                        _manualMaxP -= range * 0.1F
                        _manualMinP -= range * 0.1F
                    End If
            End Select
            _needsRepaint = True
            e.Handled = True
            ScheduleStateSave()
        End Sub

        Private Function XToIndex(x As Single) As Integer
            Return _vs.StartIndex + CInt(Math.Floor((x - _mainRect.Left) / (_vs.CandleWidth + _vs.Gap)))
        End Function

        Private Sub OnChartViewportResize(sender As Object, e As EventArgs) Handles _sk.Resize
            If _candles Is Nothing OrElse _candles.Count = 0 Then Return
            If Not _viewportInitialized Then Return
            If Not IsLatestCandleVisible() Then Return

            Dim denom As Double = _vs.CandleWidth + _vs.Gap
            If denom <= 0 OrElse Double.IsNaN(denom) OrElse Double.IsInfinity(denom) Then Return

            Dim chartWidth = Math.Max(1.0, CDbl(_sk.Width - MARGIN_LEFT - MARGIN_RIGHT))
            Dim newVisibleCount = Math.Max(1, CInt(Math.Truncate(chartWidth / denom)))
            KeepLatestVisibleForNewVisibleCount(newVisibleCount)
            _needsRepaint = True
            RaiseVisibleCandleCountChanged()
        End Sub

        Private Sub MoveToLatestVisible()
            _vs.StartIndex = GetLatestStartIndex()
        End Sub

        Private Function GetLatestStartIndex() As Integer
            Return GetLatestStartIndexForVisibleCount(_vs.VisibleCount)
        End Function

        Private Function GetLatestStartIndexForVisibleCount(visibleCount As Integer) As Integer
            Dim safeVisibleCount = Math.Max(1, visibleCount)
            Return Math.Max(0, _candles.Count - safeVisibleCount)
        End Function

        Private Function GetDefaultRightPaddingBars() As Integer
            Dim safeVisibleCount = Math.Max(1, _vs.VisibleCount)
            Dim preferredPadding = Math.Max(3, CInt(Math.Round(safeVisibleCount * 0.08R)))
            Return Math.Max(0, Math.Min(RIGHT_DRAG_PADDING_BARS, preferredPadding))
        End Function

        Private Function GetInitialVisibleCount() As Integer
            Dim denom As Double = DEFAULT_INITIAL_CANDLE_WIDTH + DEFAULT_INITIAL_GAP
            If denom <= 0 Then Return DEFAULT_INITIAL_VISIBLE_BARS
            Dim chartWidth = CDbl(_sk.Width) - MARGIN_LEFT - MARGIN_RIGHT
            If chartWidth <= 0 Then Return DEFAULT_INITIAL_VISIBLE_BARS
            Return Math.Max(10, CInt(Math.Truncate(chartWidth / denom)))
        End Function

        Private Sub ResetInitialViewportState()
            _vs.StartIndex = 0
            _vs.CandleWidth = DEFAULT_INITIAL_CANDLE_WIDTH
            _vs.Gap = DEFAULT_INITIAL_GAP
            _vs.VisibleCount = GetInitialVisibleCount()
            _isAutoScaleY = True
            _manualMaxP = 0
            _manualMinP = 0
        End Sub

        Private Function IsLatestCandleVisible() As Boolean
            If _candles Is Nothing OrElse _candles.Count = 0 Then Return False
            Dim latestIndex = _candles.Count - 1
            Return _vs.StartIndex <= latestIndex AndAlso _vs.EndIndex >= latestIndex
        End Function

        Private Function GetMaxDragStartIndex() As Integer
            Return Math.Max(0, GetLatestStartIndex() + RIGHT_DRAG_PADDING_BARS)
        End Function

        Private Function GetRightPaddingBars() As Integer
            Return Math.Max(0, _vs.StartIndex - GetLatestStartIndex())
        End Function

        Private Sub KeepLatestVisibleForNewVisibleCount(newVisibleCount As Integer)
            Dim safeNewVisible = Math.Max(1, newVisibleCount)
            Dim oldVisibleCount = Math.Max(1, _vs.VisibleCount)
            Dim rightPadding = GetRightPaddingBars()
            Dim scaledPadding = CInt(Math.Round(rightPadding * (safeNewVisible / CDbl(oldVisibleCount))))
            scaledPadding = Math.Max(0, Math.Min(RIGHT_DRAG_PADDING_BARS, scaledPadding))
            _vs.VisibleCount = safeNewVisible
            _vs.StartIndex = Math.Max(0, GetLatestStartIndexForVisibleCount(safeNewVisible) + scaledPadding)
        End Sub
    End Class
End Namespace
