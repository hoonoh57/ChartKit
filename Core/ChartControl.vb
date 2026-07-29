Imports System.Linq
Imports System.Windows.Forms
Imports SkiaSharp
Imports SkiaSharp.Views.Desktop
Imports ChartKit.Abstractions
Imports ChartKit.Models

Namespace Core
    Public Class ChartControl
        Inherits UserControl

        Private Const MARGIN_LEFT As Single = 10
        Private Const MARGIN_RIGHT As Single = 80
        Private Const RIGHT_DRAG_PADDING_BARS As Integer = 12
        Private Const DEFAULT_INITIAL_VISIBLE_BARS As Integer = 100
        Private Const DEFAULT_INITIAL_CANDLE_WIDTH As Single = 8
        Private Const DEFAULT_INITIAL_GAP As Single = 2
        Private Const FRAME_INTERVAL_MS As Integer = 16
        Private Const CANDLE_RING_CAPACITY As Integer = 100000

        Private WithEvents _sk As SKControl
        Private ReadOnly _theme As ChartTheme = ChartTheme.CreateDefault()
        Private ReadOnly _registry As New LayerRegistry()
        Private ReadOnly _indicatorEngine As New IndicatorEngine()
        Private _candles As New CandleRingBuffer(CANDLE_RING_CAPACITY)
        Private ReadOnly _vs As New ChartViewState()
        Private _viewportInitialized As Boolean = False

        Private _mouseInside As Boolean = False
        Private WithEvents _frameTimer As Timer
        Private _needsRepaint As Boolean = True

        Private _mainRect As SKRect
        Private _volumeRect As SKRect
        Private _panelRects As New List(Of SKRect)()
        '' 서브패널 인덱스(0=첫 서브패널) → 사용자 기준선 값 목록
        Private _panelBaselines As New Dictionary(Of Integer, List(Of Single))()
        '' 서브패널 과열/침체 음영
        Private _panelZones As New Dictionary(Of Integer, ChartKit.Core.PanelZoneState)()
        '' 오버레이 배경 음영 규칙
        Private _shadeRules As New List(Of ChartKit.Core.OverlayShadeRule)()
        Private _signalRules As New List(Of ChartKit.Core.SignalRule)()
        '' 좌측 등락률축 모드: 0=끄기 1=전일종가대비 2=시가대비
        Private _pctAxisMode As Integer = 0
        '' RestoreState 가 복원한 지표 개수 (중복 재추가 방지용)
        Private _lastRestoreCount As Integer = -1
        Public ReadOnly Property LastRestoreIndicatorCount As Integer
            Get
                Return _lastRestoreCount
            End Get
        End Property
        Private _panelRatios As New List(Of Single)()   '' 슬롯별 높이 비율(패널 개수와 동기화)
        Private _isPanelResizing As Boolean = False
        Private _resizePanelSlot As Integer = -1
        Private _resizeStartY As Single = 0
        Private _resizeStartRatio As Single = 0
        Private Const PANEL_HIT As Single = 4.0F          '' 경계 히트존(px)
        Private Const PANEL_MIN_RATIO As Single = 0.06F   '' 패널 최소 비율
        Private _priceHigh As Single
        Private _priceLow As Single
        Private _volumeMax As Long

        '' ── 원본 드래그/스케일 상태 ──
        Private _isDragging As Boolean = False
        Private _isDraggingPrice As Boolean = False
        Private _dragStartX As Integer
        Private _dragStartY As Integer
        Private _dragStartIndex As Integer
        Private _dragStartMaxP As Single
        Private _dragStartMinP As Single
        Private _manualMaxP As Single = 0
        Private _manualMinP As Single = 0
        Private _isAutoScaleY As Boolean = True
        Private _lastMouseX As Single = 0
        Private _lastMouseY As Single = 0

        Public ReadOnly Property Layers As LayerRegistry
            Get
                Return _registry
            End Get
        End Property

        Public Sub New()
            _sk = New SKControl() With {.Dock = DockStyle.Fill}
            Controls.Add(_sk)

            AddHandler _sk.MouseMove, AddressOf OnGLMouseMove
            AddHandler _sk.MouseDown, AddressOf OnGLMouseDown
            AddHandler _sk.MouseUp, AddressOf OnGLMouseUp
            AddHandler _sk.MouseWheel, AddressOf OnGLMouseWheel
            AddHandler _sk.MouseEnter, AddressOf OnGLMouseEnter
            AddHandler _sk.MouseLeave, AddressOf OnGLMouseLeave
            AddHandler _sk.KeyDown, AddressOf OnGLKeyDown

            _frameTimer = New Timer()
            _frameTimer.Interval = FRAME_INTERVAL_MS
            _frameTimer.Enabled = True
            AddHandler _frameTimer.Tick, AddressOf OnFrameTimer
        End Sub

        Private _dataSource As ChartKit.Abstractions.ICandleDataSource
        Private ReadOnly _realtimeEventSync As New Object()
        Private ReadOnly _pendingFinalUpdates As New Queue(Of CandleItem)()
        Private ReadOnly _pendingAppendedCandles As New Queue(Of CandleItem)()
        Private _pendingUpdatedCandle As CandleItem
        Private _realtimeDrainScheduled As Integer = 0

        '' 데이터소스 연결: 과거봉을 받아 로드하고, 실시간 이벤트를 구독한다.
        '' 차트는 소스의 구체 타입을 모른다 (인터페이스만 의존).
        Public Sub AttachDataSource(src As ChartKit.Abstractions.ICandleDataSource, req As ChartKit.Abstractions.CandleRequest)
            If _dataSource IsNot Nothing Then
                RemoveHandler _dataSource.CandleAppended, AddressOf OnCandleAppended
                RemoveHandler _dataSource.CandleUpdated, AddressOf OnCandleUpdated
                _dataSource.StopRealtime()
            End If
            _dataSource = src
            If src Is Nothing Then Return
            Dim bars = src.GetCandles(req)
            LoadCandles(bars)
            AddHandler src.CandleAppended, AddressOf OnCandleAppended
            AddHandler src.CandleUpdated, AddressOf OnCandleUpdated
            src.StartRealtime(req)
        End Sub

        '' 이미 비동기로 로드한 과거봉은 유지하고 실시간 스트림만 교체한다.
        Public Sub AttachRealtimeSource(src As ChartKit.Abstractions.ICandleDataSource,
                                        req As ChartKit.Abstractions.CandleRequest)
            If _dataSource IsNot Nothing Then
                RemoveHandler _dataSource.CandleAppended, AddressOf OnCandleAppended
                RemoveHandler _dataSource.CandleUpdated, AddressOf OnCandleUpdated
                _dataSource.StopRealtime()
            End If
            _dataSource = src
            If src Is Nothing Then Return
            AddHandler src.CandleAppended, AddressOf OnCandleAppended
            AddHandler src.CandleUpdated, AddressOf OnCandleUpdated
            src.StartRealtime(req)
        End Sub

        Private Sub OnCandleAppended(sender As Object, e As ChartKit.Abstractions.CandleAppendedEventArgs)
            If e Is Nothing OrElse e.Candle Is Nothing Then Return
            If Me.InvokeRequired Then
                SyncLock _realtimeEventSync
                    If _pendingUpdatedCandle IsNot Nothing Then
                        '' 완성 직전 마지막 상태는 새 봉 추가보다 먼저 적용되어야 한다.
                        _pendingFinalUpdates.Enqueue(_pendingUpdatedCandle)
                    End If
                    _pendingAppendedCandles.Enqueue(e.Candle)
                    _pendingUpdatedCandle = Nothing
                End SyncLock
                ScheduleRealtimeDrain()
                Return
            End If
            ApplyAppendedCandle(e.Candle)
        End Sub

        Private Sub OnCandleUpdated(sender As Object, e As ChartKit.Abstractions.CandleUpdatedEventArgs)
            If e Is Nothing OrElse e.Candle Is Nothing Then Return
            If Me.InvokeRequired Then
                '' UI가 밀려도 모든 체결을 큐에 쌓지 않고 가장 최신 상태 하나만 유지한다.
                SyncLock _realtimeEventSync
                    _pendingUpdatedCandle = e.Candle
                End SyncLock
                ScheduleRealtimeDrain()
                Return
            End If
            ApplyUpdatedCandle(e.Candle)
        End Sub

        Private Sub ScheduleRealtimeDrain()
            If Threading.Interlocked.CompareExchange(_realtimeDrainScheduled, 1, 0) <> 0 Then Return
            Try
                BeginInvoke(New MethodInvoker(AddressOf DrainRealtimeEvents))
            Catch ex As InvalidOperationException
                Threading.Interlocked.Exchange(_realtimeDrainScheduled, 0)
            End Try
        End Sub

        Private Sub DrainRealtimeEvents()
            Do
                Dim appended As CandleItem = Nothing
                Dim updated As CandleItem = Nothing
                SyncLock _realtimeEventSync
                    If _pendingFinalUpdates.Count > 0 Then
                        updated = _pendingFinalUpdates.Dequeue()
                    ElseIf _pendingAppendedCandles.Count > 0 Then
                        appended = _pendingAppendedCandles.Dequeue()
                    Else
                        updated = _pendingUpdatedCandle
                        _pendingUpdatedCandle = Nothing
                    End If
                End SyncLock

                If appended Is Nothing AndAlso updated Is Nothing Then Exit Do
                If appended IsNot Nothing Then ApplyAppendedCandle(appended)
                If updated IsNot Nothing Then ApplyUpdatedCandle(updated)
            Loop

            Threading.Interlocked.Exchange(_realtimeDrainScheduled, 0)
            Dim hasPending As Boolean
            SyncLock _realtimeEventSync
                hasPending = _pendingFinalUpdates.Count > 0 OrElse
                             _pendingAppendedCandles.Count > 0 OrElse
                             _pendingUpdatedCandle IsNot Nothing
            End SyncLock
            If hasPending Then ScheduleRealtimeDrain()
        End Sub

        Private Sub ApplyAppendedCandle(candle As CandleItem)
            Dim wasLatestVisible = IsLatestCandleVisible()
            Dim oldestEvicted = _candles.Add(candle)
            If oldestEvicted Then
                '' 10만 봉 보존 한도에 도달한 경우에만 지표 인덱스를 새 head에 재정렬한다.
                _indicatorEngine.CalculateAll(_candles)
                _vs.StartIndex = Math.Max(0, _vs.StartIndex - 1)
            Else
                _indicatorEngine.UpdateLast(_candles)
            End If
            If wasLatestVisible Then MoveToLatestVisible()
            _needsRepaint = True
        End Sub

        Private Sub ApplyUpdatedCandle(candle As CandleItem)
            If _candles Is Nothing OrElse _candles.Count = 0 Then
                _candles.Add(candle)
            Else
                _candles(_candles.Count - 1) = candle
            End If
            _indicatorEngine.UpdateLast(_candles)
            _needsRepaint = True
        End Sub

        Public Sub LoadCandles(candles As List(Of CandleItem))
            If candles IsNot Nothing Then
                Dim capacity = Math.Max(CANDLE_RING_CAPACITY, candles.Count)
                Dim replacement As New CandleRingBuffer(capacity)
                For Each candle In candles
                    replacement.Add(candle)
                Next
                _candles = replacement
            End If
            _indicatorEngine.CalculateAll(_candles)
            ResetInitialViewportState()
            MoveToLatestVisible()
            _viewportInitialized = True
            _needsRepaint = True
        End Sub

        Public ReadOnly Property CandleCount As Integer
            Get
                Return If(_candles Is Nothing, 0, _candles.Count)
            End Get
        End Property

        '' 툴바 등 외부 UI에서 화면에 표시할 봉 개수를 지정한다.
        Public Sub SetVisibleCandleCount(count As Integer)
            If _candles Is Nothing OrElse _candles.Count = 0 Then Return
            Dim safeCount = Math.Max(1, Math.Min(count, _candles.Count))
            Dim chartWidth = Math.Max(1.0F, CSng(_sk.ClientSize.Width) - MARGIN_LEFT - MARGIN_RIGHT)
            _vs.VisibleCount = safeCount
            _vs.CandleWidth = Math.Max(0.5F, (chartWidth / (safeCount + GetDefaultRightPaddingBars())) - _vs.Gap)
            MoveToLatestVisible()
            _isAutoScaleY = True
            _needsRepaint = True
        End Sub

        '' 로드된 데이터 안에서 지정 거래일의 첫 캔들로 이동한다.
        Public Function MoveToDate(tradingDate As Date) As Boolean
            If _candles Is Nothing OrElse _candles.Count = 0 Then Return False
            Dim targetDate = tradingDate.Date
            Dim targetIndex = _candles.FindIndex(Function(c) c IsNot Nothing AndAlso c.Dt.Date = targetDate)
            If targetIndex < 0 Then Return False
            _vs.StartIndex = targetIndex
            _isAutoScaleY = True
            _needsRepaint = True
            Return True
        End Function

        Public Function GetFirstCandleDate() As Date?
            If _candles Is Nothing OrElse _candles.Count = 0 Then Return Nothing
            Return _candles(0).Dt.Date
        End Function

        Public Function GetLastCandleDate() As Date?
            If _candles Is Nothing OrElse _candles.Count = 0 Then Return Nothing
            Return _candles(_candles.Count - 1).Dt.Date
        End Function

        Public Sub AddIndicator(ind As ChartKit.Abstractions.IIndicator)
            If ind Is Nothing Then Return
            _indicatorEngine.Register(ind)
            If _candles IsNot Nothing AndAlso _candles.Count > 0 Then
                _indicatorEngine.CalculateAll(_candles)
            End If
            _needsRepaint = True
        End Sub

        '' ===== 지표 조회/삭제/파라미터 변경 API =====
        Public Function GetIndicators() As List(Of ChartKit.Abstractions.IIndicator)
            Return _indicatorEngine.GetAll()
        End Function

        Public Sub RemoveIndicator(name As String)
            _indicatorEngine.Remove(name)
            If _candles IsNot Nothing AndAlso _candles.Count > 0 Then
                _indicatorEngine.CalculateAll(_candles)
            End If
            _needsRepaint = True
        End Sub

        Public Sub ClearIndicators()
            _indicatorEngine.Clear()
            _needsRepaint = True
        End Sub

        '' ===== 컨텍스트 메뉴 =====
        Private Sub ShowContextMenu(pt As System.Drawing.Point)
            Dim menu As New ContextMenuStrip()

            '' ── 지표 삽입 (카탈로그 기반 자동 생성) ──
            Dim addMenu As New ToolStripMenuItem("지표 추가")
            For Each entry In ChartKit.Core.IndicatorCatalog.All()
                Dim key = entry.Key
                addMenu.DropDownItems.Add(entry.DisplayName, Nothing,
                    Sub(s, ev)
                        Dim ind = ChartKit.Core.IndicatorCatalog.Create(key)
                        If ind IsNot Nothing Then AddIndicator(ind)
                    End Sub)
            Next
            If addMenu.DropDownItems.Count = 0 Then
                addMenu.DropDownItems.Add("(등록된 지표 없음)").Enabled = False
            End If
            menu.Items.Add(addMenu)

            '' ── 지표 삭제 (등록된 지표 나열) ──
            Dim curIndicators = _indicatorEngine.GetAll()
            Dim delMenu As New ToolStripMenuItem("지표 삭제")
            If curIndicators.Count = 0 Then
                delMenu.Enabled = False
            Else
                For Each ind In curIndicators
                    Dim nm = ind.Name
                    delMenu.DropDownItems.Add(ind.DisplayName, Nothing,
                        Sub(s, ev) RemoveIndicator(nm))
                Next
            End If
            menu.Items.Add(delMenu)

            '' ── 지표 수정 (파라미터 인라인 변경) ──
            Dim editMenu As New ToolStripMenuItem("지표 수정")
            If curIndicators.Count = 0 Then
                editMenu.Enabled = False
            Else
                For Each ind In curIndicators
                    Dim indRef = ind
                    Dim indSub As New ToolStripMenuItem(ind.DisplayName)
                    If indRef.Parameters IsNot Nothing Then
                        For Each kv In indRef.Parameters
                            Dim pKey = kv.Key
                            Dim pVal = kv.Value
                            indSub.DropDownItems.Add($"{pKey} = {pVal}", Nothing,
                                Sub(s, ev) EditIndicatorParam(indRef, pKey))
                        Next
                    End If
                    If indSub.DropDownItems.Count = 0 Then
                        indSub.DropDownItems.Add("(수정 가능한 파라미터 없음)").Enabled = False
                    End If
                    editMenu.DropDownItems.Add(indSub)
                Next
            End If
            menu.Items.Add(editMenu)

            '' ── 기준선 (서브패널 전용, 값만 입력, 회색 점선 고정) ──
            Dim slot = FindSubPanelSlot(pt.Y)
            Dim baseMenu As New ToolStripMenuItem("기준선")
            If slot < 0 Then
                baseMenu.Enabled = False
                baseMenu.Text = "기준선 (서브패널에서 우클릭)"
            Else
                Dim slotRef = slot
                baseMenu.DropDownItems.Add("기준선 추가...", Nothing,
                    Sub(s, ev)
                        Dim inp = Microsoft.VisualBasic.Interaction.InputBox(
                            "기준선 값을 입력하세요.", "기준선 추가", "")
                        If Not String.IsNullOrWhiteSpace(inp) Then
                            Dim v As Single
                            If Single.TryParse(inp.Trim(), Globalization.NumberStyles.Float,
                                               Globalization.CultureInfo.InvariantCulture, v) Then
                                If Not _panelBaselines.ContainsKey(slotRef) Then
                                    _panelBaselines(slotRef) = New List(Of Single)()
                                End If
                                _panelBaselines(slotRef).Add(v)
                                Invalidate()
                            End If
                        End If
                    End Sub)
                Dim hasAny = _panelBaselines.ContainsKey(slotRef) AndAlso _panelBaselines(slotRef).Count > 0
                Dim clearItem = baseMenu.DropDownItems.Add("이 패널 기준선 모두 지우기", Nothing,
                    Sub(s, ev)
                        _panelBaselines.Remove(slotRef)
                        Invalidate()
                    End Sub)
                clearItem.Enabled = hasAny
            End If
            menu.Items.Add(baseMenu)

            '' ── 과열/침체 음영 (서브패널 전용, 값만 입력, 색 고정) ──
            Dim zoneMenu As New ToolStripMenuItem("음영")
            If slot < 0 Then
                zoneMenu.Enabled = False
                zoneMenu.Text = "음영 (서브패널에서 우클릭)"
            Else
                Dim zslot = slot
                zoneMenu.DropDownItems.Add("과열 음영 설정...(이상)", Nothing,
                    Sub(s, ev)
                        Dim inp = Microsoft.VisualBasic.Interaction.InputBox("과열 기준값(이상 음영):", "과열 음영", "")
                        If Not String.IsNullOrWhiteSpace(inp) Then
                            Dim v As Single
                            If Single.TryParse(inp.Trim(), Globalization.NumberStyles.Float, Globalization.CultureInfo.InvariantCulture, v) Then
                                If Not _panelZones.ContainsKey(zslot) Then _panelZones(zslot) = New ChartKit.Core.PanelZoneState()
                                _panelZones(zslot).OverValue = v
                                Invalidate()
                            End If
                        End If
                    End Sub)
                zoneMenu.DropDownItems.Add("침체 음영 설정...(이하)", Nothing,
                    Sub(s, ev)
                        Dim inp = Microsoft.VisualBasic.Interaction.InputBox("침체 기준값(이하 음영):", "침체 음영", "")
                        If Not String.IsNullOrWhiteSpace(inp) Then
                            Dim v As Single
                            If Single.TryParse(inp.Trim(), Globalization.NumberStyles.Float, Globalization.CultureInfo.InvariantCulture, v) Then
                                If Not _panelZones.ContainsKey(zslot) Then _panelZones(zslot) = New ChartKit.Core.PanelZoneState()
                                _panelZones(zslot).UnderValue = v
                                Invalidate()
                            End If
                        End If
                    End Sub)
                Dim hasZone = _panelZones.ContainsKey(zslot)
                Dim clearZone = zoneMenu.DropDownItems.Add("이 패널 음영 지우기", Nothing,
                    Sub(s, ev)
                        _panelZones.Remove(zslot)
                        Invalidate()
                    End Sub)
                clearZone.Enabled = hasZone
            End If
            menu.Items.Add(zoneMenu)

            '' ── 오버레이 배경 음영 규칙 (A >= B 구간) ──
            Dim overlays = _indicatorEngine.GetAll().Where(Function(x) x.PanelIndex = 0).ToList()
            Dim shadeMenu As New ToolStripMenuItem("배경 음영 규칙")
            Dim addShade As New ToolStripMenuItem("규칙 추가 (A >= B)")
            If overlays.Count < 2 Then
                addShade.Enabled = False
                addShade.Text = "규칙 추가 (오버레이 지표 2개 이상 필요)"
            Else
                For Each indA In overlays
                    Dim aName = indA.Name
                    Dim aDisp = indA.DisplayName
                    Dim aSub As New ToolStripMenuItem(aDisp & " >= ...")
                    For Each indB In overlays
                        If indB.Name = aName Then Continue For
                        Dim bName = indB.Name
                        aSub.DropDownItems.Add(aDisp & " >= " & indB.DisplayName, Nothing,
                            Sub(s, ev)
                                _shadeRules.Add(New ChartKit.Core.OverlayShadeRule With {
                                    .IndicatorA = aName, .IndicatorB = bName})
                                Invalidate()
                            End Sub)
                        aSub.DropDownItems.Add(aDisp & " >= " & indB.DisplayName & "  (B상승중만)", Nothing,
                            Sub(s, ev)
                                _shadeRules.Add(New ChartKit.Core.OverlayShadeRule With {
                                    .IndicatorA = aName, .IndicatorB = bName, .RequireBRising = True})
                                Invalidate()
                            End Sub)
                    Next
                    addShade.DropDownItems.Add(aSub)
                Next
            End If
            shadeMenu.DropDownItems.Add(addShade)
            Dim clearShade = shadeMenu.DropDownItems.Add("모든 음영 규칙 지우기", Nothing,
                Sub(s, ev)
                    _shadeRules.Clear()
                    Invalidate()
                End Sub)
            clearShade.Enabled = (_shadeRules.Count > 0)
            menu.Items.Add(shadeMenu)

            '' ── 신호 검색 (오버레이 A crossUp/crossDown B) ──
            Dim sigOverlays = _indicatorEngine.GetAll().Where(Function(x) x.PanelIndex = 0).ToList()
            Dim sigMenu As New ToolStripMenuItem("신호 검색")
            Dim addSig As New ToolStripMenuItem("신호 추가 (A 돌파 B)")
            If sigOverlays.Count < 2 Then
                addSig.Enabled = False
                addSig.Text = "신호 추가 (오버레이 지표 2개 이상 필요)"
            Else
                For Each iA In sigOverlays
                    Dim aName = iA.Name
                    Dim aDisp = iA.DisplayName
                    Dim aSub As New ToolStripMenuItem(aDisp & " 돌파 ...")
                    For Each iB In sigOverlays
                        If iB.Name = aName Then Continue For
                        Dim bName = iB.Name
                        Dim bDisp = iB.DisplayName
                        aSub.DropDownItems.Add(aDisp & " ▲상향돌파 " & bDisp, Nothing,
                            Sub(s, ev)
                                _signalRules.Add(New ChartKit.Core.SignalRule With {.IndicatorA = aName, .IndicatorB = bName, .CrossUp = True})
                                Invalidate()
                            End Sub)
                            aSub.DropDownItems.Add(aDisp & " ▲상향돌파 " & bDisp & "  (B상승중만)", Nothing,
                            Sub(s, ev)
                                _signalRules.Add(New ChartKit.Core.SignalRule With {.IndicatorA = aName, .IndicatorB = bName, .CrossUp = True, .RequireBRising = True})
                                Invalidate()
                            End Sub)
                        aSub.DropDownItems.Add(aDisp & " ▼하향돌파 " & bDisp, Nothing,
                            Sub(s, ev)
                                _signalRules.Add(New ChartKit.Core.SignalRule With {.IndicatorA = aName, .IndicatorB = bName, .CrossUp = False})
                                Invalidate()
                            End Sub)
                    Next
                    addSig.DropDownItems.Add(aSub)
                Next
            End If
            sigMenu.DropDownItems.Add(addSig)
            '' ── 등록된 신호 규칙 속성 편집 (PropertyGrid) ──
            If _signalRules.Count > 0 Then
                Dim sigEditMenu As New ToolStripMenuItem("신호 규칙 편집(속성)...")
                For si = 0 To _signalRules.Count - 1
                    Dim idxLocal = si
                    Dim r = _signalRules(si)
                    sigEditMenu.DropDownItems.Add(r.ToString(), Nothing,
                        Sub(s, ev)
                            Using dlg As New ChartKit.UI.SignalPropertyDialog(_signalRules(idxLocal))
                                If dlg.ShowDialog(Me) = System.Windows.Forms.DialogResult.OK Then
                                    Invalidate()
                                End If
                            End Using
                        End Sub)
                Next
                Dim sigDelMenu As New ToolStripMenuItem("신호 규칙 삭제")
                For si = 0 To _signalRules.Count - 1
                    Dim idxLocal = si
                    Dim r = _signalRules(si)
                    sigDelMenu.DropDownItems.Add(r.ToString(), Nothing,
                        Sub(s, ev)
                            _signalRules.RemoveAt(idxLocal)
                            Invalidate()
                        End Sub)
                Next
                sigMenu.DropDownItems.Add(New ToolStripSeparator())
                sigMenu.DropDownItems.Add(sigEditMenu)
                sigMenu.DropDownItems.Add(sigDelMenu)
            End If
            Dim clearSig = sigMenu.DropDownItems.Add("모든 신호 지우기", Nothing,
                Sub(s, ev)
                    _signalRules.Clear()
                    Invalidate()
                End Sub)
            clearSig.Enabled = (_signalRules.Count > 0)
            menu.Items.Add(sigMenu)

            menu.Items.Add(New ToolStripSeparator())

            '' ── 레이어 토글 ──
            Dim viewMenu As New ToolStripMenuItem("차트 요소")
            AddLayerToggle(viewMenu, "Crosshair", "크로스헤어")
            AddLayerToggle(viewMenu, "Indicators", "지표선")
            AddLayerToggle(viewMenu, "Legend", "레전드")
            AddLayerToggle(viewMenu, "Volume", "거래량")
            AddLayerToggle(viewMenu, "GridAxis", "그리드/축")

            '' ── 좌측 등락률축 (라디오: 끄기/전일종가대비/시가대비) ──
            Dim pctMenu As New ToolStripMenuItem("등락률축(좌)")
            Dim pOff As New ToolStripMenuItem("끄기") With {.Checked = (_pctAxisMode = 0)}
            Dim pPrev As New ToolStripMenuItem("전일종가대비") With {.Checked = (_pctAxisMode = 1)}
            Dim pOpen As New ToolStripMenuItem("시가대비") With {.Checked = (_pctAxisMode = 2)}
            AddHandler pOff.Click, Sub(s, ev)
                                       _pctAxisMode = 0
                                       Invalidate()
                                   End Sub
            AddHandler pPrev.Click, Sub(s, ev)
                                        _pctAxisMode = 1
                                        Invalidate()
                                    End Sub
            AddHandler pOpen.Click, Sub(s, ev)
                                        _pctAxisMode = 2
                                        Invalidate()
                                    End Sub
            pctMenu.DropDownItems.Add(pOff)
            pctMenu.DropDownItems.Add(pPrev)
            pctMenu.DropDownItems.Add(pOpen)
            viewMenu.DropDownItems.Add(pctMenu)
            menu.Items.Add(viewMenu)

            menu.Items.Add(New ToolStripSeparator())
            menu.Items.Add("전체 지표 삭제", Nothing, Sub(s, ev) ClearIndicators())
            menu.Items.Add("현재 차트 데이터 출력(CSV)", Nothing, Sub(s, ev) DumpChartDataCsv())
            menu.Items.Add("최신으로 이동", Nothing, Sub(s, ev)
                                                            MoveToLatestVisible()
                                                            _needsRepaint = True
                                                        End Sub)

            menu.Show(_sk, pt)
        End Sub

        Private Sub AddLayerToggle(parent As ToolStripMenuItem, layerId As String, label As String)
            Dim item As New ToolStripMenuItem(label)
            Dim vis = _registry.IsLayerVisible(layerId)
            item.Checked = vis
            AddHandler item.Click,
                Sub(s, ev)
                    _registry.Toggle(layerId, Not _registry.IsLayerVisible(layerId))
                    _needsRepaint = True
                End Sub
            parent.DropDownItems.Add(item)
        End Sub

        Private Sub EditIndicatorParam(ind As ChartKit.Abstractions.IIndicator, paramKey As String)
            If ind Is Nothing OrElse ind.Parameters Is Nothing Then Return
            Dim cur = ""
            If ind.Parameters.ContainsKey(paramKey) Then cur = ind.Parameters(paramKey).ToString()
            Dim input = Microsoft.VisualBasic.Interaction.InputBox(
                $"{ind.DisplayName} 의 {paramKey} 값 입력:", "지표 수정", cur)
            If String.IsNullOrWhiteSpace(input) Then Return

            '' 기존 파라미터 딕셔너리를 복제해 값 교체 후 재대입 (Setter 트리거)
            Dim newParams As New Dictionary(Of String, Object)(ind.Parameters)
            Dim oldVal = If(ind.Parameters.ContainsKey(paramKey), ind.Parameters(paramKey), Nothing)
            Dim parsed As Object = input
            If oldVal IsNot Nothing Then
                If TypeOf oldVal Is Integer Then
                    Dim iv As Integer
                    If Integer.TryParse(input, iv) Then parsed = iv Else Return
                ElseIf TypeOf oldVal Is Single Then
                    Dim sv As Single
                    If Single.TryParse(input, sv) Then parsed = sv Else Return
                ElseIf TypeOf oldVal Is Double Then
                    Dim dv As Double
                    If Double.TryParse(input, dv) Then parsed = dv Else Return
                End If
            End If
            newParams(paramKey) = parsed

            '' 이름이 파라미터로 바뀌는 지표(예: MA 기간)는 기존 것을 지우고 새로 등록
            Dim oldName = ind.Name
            ind.Parameters = newParams
            Dim newName = ind.Name
            If oldName <> newName Then
                '' 엔진 결과 키가 이름 기반이므로 재등록
                _indicatorEngine.Remove(oldName)
                _indicatorEngine.Register(ind)
            End If
            If _candles IsNot Nothing AndAlso _candles.Count > 0 Then
                _indicatorEngine.CalculateAll(_candles)
            End If
            _needsRepaint = True
        End Sub

        Private Sub OnFrameTimer(sender As Object, e As EventArgs)
            If Not _needsRepaint Then Return
            _needsRepaint = False
            _sk.Invalidate()
        End Sub

        '' ===== 원본 마우스 이동 (드래그 패닝 + 가격축 이동/줌) =====
        Private Sub OnGLMouseMove(sender As Object, e As MouseEventArgs)
            _vs.CrosshairX = e.X
            If _isPanelResizing AndAlso _resizePanelSlot >= 0 AndAlso _resizePanelSlot < _panelRatios.Count Then
                Dim h = _sk.Height
                Dim totalH = (h - _theme.MarginBottom) - _theme.MarginTop
                If totalH > 0 Then
                    '' 경계를 위로 끌면 위 패널(또는 볼륨)이 줄고 이 패널이 커짐 -> dy>0(아래로)면 이 패널 축소
                    Dim dy = e.Y - _resizeStartY
                    Dim newRatio = _resizeStartRatio - (dy / totalH)
                    If newRatio < PANEL_MIN_RATIO Then newRatio = PANEL_MIN_RATIO
                    If newRatio > 0.6F Then newRatio = 0.6F
                    _panelRatios(_resizePanelSlot) = newRatio
                End If
                _needsRepaint = True
                Return
            Else
                '' 경계 위에서 커서를 SizeNS 로
                If HitPanelBorder(e.Y) >= 0 Then
                    _sk.Cursor = Cursors.SizeNS
                Else
                    _sk.Cursor = Cursors.Default
                End If
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

        '' ===== 원본 마우스 다운 (좌: 패닝/가격축, 우: 예약) =====
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
                    _dragStartMinP = If(_isAutoScaleY OrElse _manualMaxP <= _manualMinP, _priceLow, _manualMinP)
                End If
            End If
        End Sub

        Private Sub OnGLMouseUp(sender As Object, e As MouseEventArgs)
            If e.Button = MouseButtons.Left Then
                _isDragging = False
                _isPanelResizing = False
                _resizePanelSlot = -1
                _isDraggingPrice = False
            End If
        End Sub

        '' ===== 원본 더블클릭: 오토스케일 복구 =====
        Private Sub OnGLDoubleClick(sender As Object, e As MouseEventArgs) Handles _sk.MouseDoubleClick
            _isAutoScaleY = True
            _needsRepaint = True
        End Sub

        '' ===== 원본 휠 줌 (커서 위치 봉 고정) =====
        Private Sub OnGLMouseWheel(sender As Object, e As MouseEventArgs)
            Dim latestVisible = IsLatestCandleVisible()
            Dim zoom = If(e.Delta > 0, 1.2F, 0.8F)
            _vs.CandleWidth *= zoom
            If _vs.CandleWidth < 1 Then _vs.CandleWidth = 1
            If _vs.CandleWidth > 50 Then _vs.CandleWidth = 50

            If _mainRect.Width <= 0 OrElse Single.IsNaN(_mainRect.Width) OrElse Single.IsInfinity(_mainRect.Width) Then
                Return
            End If

            Dim mouseIdx = XToIndex(e.X)
            Dim ratio As Double = (e.X - _mainRect.Left) / _mainRect.Width
            If Double.IsNaN(ratio) OrElse Double.IsInfinity(ratio) Then ratio = 0.5
            ratio = Math.Max(0.0, Math.Min(1.0, ratio))

            Dim denom As Double = _vs.CandleWidth + _vs.Gap
            If denom <= 0 OrElse Double.IsNaN(denom) OrElse Double.IsInfinity(denom) Then
                denom = 1.0
            End If

            Dim visibleD As Double = _mainRect.Width / denom
            If Double.IsNaN(visibleD) OrElse Double.IsInfinity(visibleD) Then
                visibleD = 1.0
            End If

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

        '' ===== 원본 키보드 조작 =====
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
                Case Keys.Subtract, Keys.OemMinus
                    _vs.CandleWidth *= 0.8F
                    _vs.VisibleCount = CInt(_mainRect.Width / (_vs.CandleWidth + _vs.Gap))
                Case Keys.A
                    If e.Control AndAlso _candles.Count > 0 Then
                        _vs.StartIndex = 0
                        _vs.VisibleCount = _candles.Count
                        _vs.CandleWidth = Math.Max(0.5F, (_mainRect.Width / (_vs.VisibleCount + 5)) - _vs.Gap)
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
        End Sub

        '' ===== 마우스 핸들러용 XToIndex (원본 공식, mapper 비의존) =====
        Private Function XToIndex(x As Single) As Integer
            Return _vs.StartIndex + CInt(Math.Floor((x - _mainRect.Left) / (_vs.CandleWidth + _vs.Gap)))
        End Function

        '' ===== 원본 OnChartViewportResize =====
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
        End Sub

        '' ===== 뷰포트 함수 묶음 (원본 그대로) =====
        Private Sub MoveToLatestVisible()
            _vs.StartIndex = GetLatestStartIndex() + GetDefaultRightPaddingBars()
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

        Private Sub CalcLayout()
            Dim w = _sk.Width
            Dim h = _sk.Height
            Dim cL = _theme.MarginLeft
            Dim cR = w - _theme.MarginRight
            Dim cT = _theme.MarginTop
            Dim cB = h - _theme.MarginBottom
            Dim totalH = cB - cT

            '' 활성 서브패널 인덱스 (오름차순)
            Dim panelIdxs As New List(Of Integer)()
            If _indicatorEngine IsNot Nothing Then
                For Each ind In _indicatorEngine.GetAll()
                    If ind.PanelIndex > 0 AndAlso Not panelIdxs.Contains(ind.PanelIndex) Then
                        panelIdxs.Add(ind.PanelIndex)
                    End If
                Next
            End If
            panelIdxs.Sort()
            Dim nPanels = panelIdxs.Count

            '' 슬롯 비율 리스트를 패널 개수에 동기화 (신규는 기본 0.15)
            While _panelRatios.Count < nPanels : _panelRatios.Add(0.15F) : End While
            While _panelRatios.Count > nPanels : _panelRatios.RemoveAt(_panelRatios.Count - 1) : End While

            Dim volH As Single = totalH * _theme.VolumeRatio
            '' 거래량 레이어 숨김이면 높이 접기 (등록됨 && 숨김)
            If _registry.Exists("Volume") AndAlso Not _registry.IsLayerVisible("Volume") Then volH = 0
            Dim panelTotal As Single = 0
            For k = 0 To nPanels - 1 : panelTotal += totalH * _panelRatios(k) : Next
            '' 서브패널 레이어(Panels) 숨김이면 서브패널 전체 접기
            Dim panelsHidden As Boolean = _registry.Exists("Panels") AndAlso Not _registry.IsLayerVisible("Panels")
            If panelsHidden Then panelTotal = 0
            Dim mainH As Single = totalH - volH - panelTotal

            '' main 최소치 보장 (부족하면 패널/볼륨 균등 축소)
            Dim minMain = totalH * 0.25F
            If mainH < minMain Then
                Dim over = minMain - mainH
                Dim shrinkable = volH + panelTotal
                If shrinkable > 0 Then
                    Dim scale = Math.Max(0.0F, (shrinkable - over) / shrinkable)
                    volH *= scale
                    For k = 0 To nPanels - 1 : _panelRatios(k) *= scale : Next
                    panelTotal *= scale
                End If
                mainH = totalH - volH - panelTotal
            End If

            _mainRect = New SKRect(cL, cT, cR, cT + mainH)
            _volumeRect = New SKRect(cL, _mainRect.Bottom, cR, _mainRect.Bottom + volH)

            _panelRects.Clear()
            If Not panelsHidden Then
                Dim y As Single = _volumeRect.Bottom
                For k = 0 To nPanels - 1
                    Dim ph As Single = totalH * _panelRatios(k)
                    _panelRects.Add(New SKRect(cL, y, cR, y + ph))
                    y += ph
                Next
            End If
        End Sub

        '' ===== 원본 가격범위 (수동 스케일 반영) =====
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
            For i As Integer = s To en
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
                .ShowDayChangeLines = True,
                .Engine = _indicatorEngine,
                .MouseInside = _mouseInside, .ShowCrosshair = _vs.ShowCrosshair,
                .CrosshairX = _vs.CrosshairX, .CrosshairY = _vs.CrosshairY,
                .PanelRects = _panelRects,
                .PanelBaselines = _panelBaselines,
                .PanelZones = _panelZones,
                .ShadeRules = _shadeRules, .SignalRules = _signalRules}

            For Each layer In _registry.Ordered()
                layer.Draw(canvas, ctx)
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
            End If
            MyBase.Dispose(disposing)
        End Sub
    
        '' 마우스 Y가 볼륨↔패널 / 패널↔패널 경계 히트존 안이면 해당 패널 슬롯, 아니면 -1
        Private Function FindSubPanelSlot(y As Single) As Integer
            '' 우클릭 Y좌표가 포함된 서브패널 슬롯(0=첫 서브패널) 반환. 없으면 -1.
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
                Dim topY = _panelRects(k).Top   '' 이 패널의 상단 경계
                If Math.Abs(y - topY) <= PANEL_HIT Then Return k
            Next
            Return -1
        End Function
    
        '' ============================================================
        '' 상태 영속화 (지표/뷰포트/패널비율/Y스케일/레이어토글)
        '' ============================================================
        Public Property StateProfile As String = "default"

        Public Sub SaveState(Optional profile As String = Nothing)
            Dim prof = If(profile, StateProfile)
            Dim st As New ChartState()
            st.CandleCount = If(_candles IsNot Nothing, _candles.Count, 0)
            st.StartIndex = _vs.StartIndex
            st.CandleWidth = _vs.CandleWidth
            st.Gap = _vs.Gap
            st.IsAutoScaleY = _isAutoScaleY
            st.ManualMaxP = _manualMaxP
            st.ManualMinP = _manualMinP
            st.PanelRatios = New List(Of Single)(_panelRatios)
            '' 사용자 편집 기준선 저장 (깊은 복사)
            st.PanelBaselines = New Dictionary(Of Integer, List(Of Single))()
            For Each kv In _panelBaselines
                st.PanelBaselines(kv.Key) = New List(Of Single)(kv.Value)
            Next

            '' 과열/침체 음영 저장
            st.PanelZones = New Dictionary(Of Integer, ChartKit.Core.PanelZoneState)()
            For Each zkv In _panelZones
                st.PanelZones(zkv.Key) = New ChartKit.Core.PanelZoneState With {
                    .OverValue = zkv.Value.OverValue, .UnderValue = zkv.Value.UnderValue}
            Next

            '' 배경 음영 규칙 저장
            st.PctAxisMode = _pctAxisMode
            st.SignalRules = New List(Of ChartKit.Core.SignalRule)()
            For Each sg In _signalRules
                st.SignalRules.Add(New ChartKit.Core.SignalRule With {.IndicatorA = sg.IndicatorA, .IndicatorB = sg.IndicatorB, .CrossUp = sg.CrossUp, .RequireBRising = sg.RequireBRising, .Side = sg.Side, .MarkerShape = sg.MarkerShape, .ColorArgb = sg.ColorArgb, .Name = sg.Name})
            Next
            st.ShadeRules = New List(Of ChartKit.Core.OverlayShadeRule)()
            For Each sr In _shadeRules
                st.ShadeRules.Add(New ChartKit.Core.OverlayShadeRule With {
                    .IndicatorA = sr.IndicatorA, .IndicatorB = sr.IndicatorB,
                    .ColorR = sr.ColorR, .ColorG = sr.ColorG, .ColorB = sr.ColorB, .ColorA = sr.ColorA, .RequireBRising = sr.RequireBRising})
            Next

            For Each ind In _indicatorEngine.GetAll()
                Dim isr As New IndicatorState()
                isr.TypeName = ind.GetType().AssemblyQualifiedName
                isr.Params = New Dictionary(Of String, String)
                If ind.Parameters IsNot Nothing Then
                    For Each kv In ind.Parameters
                        isr.Params(kv.Key) = Convert.ToString(kv.Value, Globalization.CultureInfo.InvariantCulture)
                    Next
                End If
                st.Indicators.Add(isr)
            Next

            For Each id In New String() {"Candle", "Crosshair", "GridAxis", "Indicators", "Legend", "Panels", "Volume"}
                st.Layers.Add(New LayerToggleState With {.Id = id, .Visible = _registry.IsLayerVisible(id)})
            Next

            st.Save(prof)
        End Sub

        Public Sub RestoreState(Optional profile As String = Nothing)
            Dim prof = If(profile, StateProfile)
            Dim st = ChartState.Load(prof)
            If st Is Nothing Then Return

            '' 사용자 편집 기준선 복원
            _panelBaselines.Clear()
            If st.PanelBaselines IsNot Nothing Then
                For Each kv In st.PanelBaselines
                    If kv.Value IsNot Nothing Then
                        _panelBaselines(kv.Key) = New List(Of Single)(kv.Value)
                    End If
                Next
            End If

            '' 과열/침체 음영 복원
            _panelZones.Clear()
            If st.PanelZones IsNot Nothing Then
                For Each zkv In st.PanelZones
                    If zkv.Value IsNot Nothing Then
                        _panelZones(zkv.Key) = New ChartKit.Core.PanelZoneState With {
                            .OverValue = zkv.Value.OverValue, .UnderValue = zkv.Value.UnderValue}
                    End If
                Next
            End If

            '' 배경 음영 규칙 복원
            _shadeRules.Clear()
            _pctAxisMode = st.PctAxisMode
            If st.ShadeRules IsNot Nothing Then
                For Each sr In st.ShadeRules
                    If sr IsNot Nothing Then
                        _shadeRules.Add(New ChartKit.Core.OverlayShadeRule With {
                            .IndicatorA = sr.IndicatorA, .IndicatorB = sr.IndicatorB,
                            .ColorR = sr.ColorR, .ColorG = sr.ColorG, .ColorB = sr.ColorB, .ColorA = sr.ColorA, .RequireBRising = sr.RequireBRising})
                    End If
                Next
            End If

            '' ── 지표 복원 : 기존 전부 제거 후 재생성 ──
            For Each existing In _indicatorEngine.GetAll().ToList()
                _indicatorEngine.Remove(existing.Name)
            Next
            _lastRestoreCount = If(st.Indicators IsNot Nothing, st.Indicators.Count, 0)
            If st.Indicators IsNot Nothing Then
                For Each isr In st.Indicators
                    Try
                        If String.IsNullOrWhiteSpace(isr.TypeName) Then Continue For
                        Dim t = Type.GetType(isr.TypeName)
                        Console.WriteLine("[Restore] TypeName=" & isr.TypeName & " -> " & If(t Is Nothing, "NULL(못찾음)", t.FullName))
                        If t Is Nothing Then Continue For
                        Dim obj = CreateWithOptionalCtor(t)
                        Dim ind = TryCast(obj, ChartKit.Abstractions.IIndicator)
                        If ind Is Nothing Then Continue For
                        '' 파라미터 복원 (원래 타입에 맞춰 변환)
                        If isr.Params IsNot Nothing AndAlso ind.Parameters IsNot Nothing Then
                            Dim newParams As New Dictionary(Of String, Object)(ind.Parameters)
                            For Each kv In isr.Params
                                If ind.Parameters.ContainsKey(kv.Key) Then
                                    newParams(kv.Key) = ConvertLike(ind.Parameters(kv.Key), kv.Value)
                                Else
                                    newParams(kv.Key) = kv.Value
                                End If
                            Next
                            ind.Parameters = newParams
                        End If
                        _indicatorEngine.Register(ind)
                    Catch ex As Exception
                        Console.WriteLine("[Restore] 지표 복원 예외: " & ex.GetType().Name & " : " & ex.Message)
                    End Try
                Next
            End If
            If _candles IsNot Nothing AndAlso _candles.Count > 0 Then
                _indicatorEngine.CalculateAll(_candles)
            End If

            '' ── 패널 비율 ──
            If st.PanelRatios IsNot Nothing AndAlso st.PanelRatios.Count > 0 Then
                _panelRatios = New List(Of Single)(st.PanelRatios)
            End If

            '' ── Y 스케일 : 복원하지 않고 항상 auto-scale 로 시작 (뷰포트가 최신봉 기준이므로) ──
            _isAutoScaleY = True
            _manualMaxP = 0
            _manualMinP = 0

            '' ── 뷰포트(StartIndex/CandleWidth/Gap)는 복원하지 않는다. ──
            '' 캔들 데이터는 매 실행마다 새로 로드되므로, LoadCandles 가 잡아둔
            '' 최신봉 오른쪽 정렬 상태를 그대로 유지한다. (기동 시 항상 정상 표시)

            '' ── 레이어 토글 ──
            If st.Layers IsNot Nothing Then
                For Each lt In st.Layers
                    _registry.Toggle(lt.Id, lt.Visible)
                Next
            End If

            Console.WriteLine("[Restore] indicators restored = " & _indicatorEngine.GetAll().Count)
            Console.WriteLine("[Restore] candles = " & If(_candles Is Nothing, 0, _candles.Count) & ", saved CandleCount = " & st.CandleCount)
            Console.WriteLine("[Restore] StartIndex = " & _vs.StartIndex & ", CandleWidth = " & _vs.CandleWidth)
            For Each ind In _indicatorEngine.GetAll()
                Console.WriteLine("   ind: " & ind.Name & " panel=" & ind.PanelIndex)
            Next

            _needsRepaint = True
        End Sub

        '' template 값의 타입에 맞춰 문자열을 변환
        Private Shared Function ConvertLike(template As Object, s As String) As Object
            Try
                If TypeOf template Is Integer Then Return Integer.Parse(s, Globalization.CultureInfo.InvariantCulture)
                If TypeOf template Is Single Then Return Single.Parse(s, Globalization.CultureInfo.InvariantCulture)
                If TypeOf template Is Double Then Return Double.Parse(s, Globalization.CultureInfo.InvariantCulture)
                If TypeOf template Is Boolean Then Return Boolean.Parse(s)
            Catch
            End Try
            Return s
        End Function
    
        '' Optional 인자만 있는 생성자를 리플렉션으로 생성 (기본값 자동 채움)
        Private Shared Function CreateWithOptionalCtor(t As Type) As Object
            '' 1) 진짜 매개변수 없는 생성자 우선
            Dim ctor0 = t.GetConstructor(Type.EmptyTypes)
            If ctor0 IsNot Nothing Then Return ctor0.Invoke(Nothing)

            '' 2) 파라미터가 가장 적은 생성자를 골라 Optional 기본값(Type.Missing)으로 채움
            Dim ctors = t.GetConstructors()
            If ctors Is Nothing OrElse ctors.Length = 0 Then Return Nothing
            Dim best = ctors(0)
            For Each c In ctors
                If c.GetParameters().Length < best.GetParameters().Length Then best = c
            Next
            Dim ps = best.GetParameters()
            Dim args(ps.Length - 1) As Object
            For i = 0 To ps.Length - 1
                If ps(i).HasDefaultValue Then
                    args(i) = ps(i).DefaultValue
                Else
                    args(i) = If(ps(i).ParameterType.IsValueType,
                                 Activator.CreateInstance(ps(i).ParameterType), Nothing)
                End If
            Next
            Return best.Invoke(args)
        End Function
    
    '' ===== 현재 차트 데이터 CSV 덤프 (신호 판정 검증용) =====
    Private Sub DumpChartDataCsv()
        If _candles Is Nothing OrElse _candles.Count = 0 Then Return

        '' 저장 경로
        Dim path As String
        Using dlg As New System.Windows.Forms.SaveFileDialog()
            dlg.Filter = "CSV 파일 (*.csv)|*.csv"
            dlg.FileName = "chartdump_" & DateTime.Now.ToString("yyyyMMdd_HHmmss") & ".csv"
            If dlg.ShowDialog() <> System.Windows.Forms.DialogResult.OK Then Return
            path = dlg.FileName
        End Using

        Dim res = _indicatorEngine.Results
        Dim inds = _indicatorEngine.GetAll()

        '' 지표별 컬럼 키 수집: "{지표명}.{값키}"
        Dim indCols As New List(Of Tuple(Of String, String))  '' (지표명, 값키)
        For Each ind In inds
            Dim rlist As List(Of ChartKit.Abstractions.IndicatorResult) = Nothing
            If res.TryGetValue(ind.Name, rlist) AndAlso rlist IsNot Nothing AndAlso rlist.Count > 0 Then
                '' 첫 non-null 결과에서 키 목록 확보
                Dim sample = rlist.FirstOrDefault(Function(x) x IsNot Nothing AndAlso x.Values IsNot Nothing AndAlso x.Values.Count > 0)
                If sample IsNot Nothing Then
                    For Each k In sample.Values.Keys
                        indCols.Add(Tuple.Create(ind.Name, k))
                    Next
                End If
            End If
        Next

        Dim sb As New System.Text.StringBuilder()

        '' ── 헤더 ──
        Dim hdr As New List(Of String) From {"Index", "DateTime", "Open", "High", "Low", "Close", "Volume"}
        For Each c In indCols : hdr.Add(c.Item1 & "." & c.Item2) : Next
        '' 신호 규칙별 판정 컬럼
        For si = 0 To _signalRules.Count - 1
            Dim r = _signalRules(si)
            Dim dir = If(r.CrossUp, "UP", "DN")
            hdr.Add($"SIG{si}[{r.IndicatorA}x{r.IndicatorB},{dir},reqB={r.RequireBRising}]")
        Next
        sb.AppendLine(String.Join(",", hdr))

        '' 지표 값 조회 헬퍼 (인덱스 정렬 가정: rlist(i).Index == i)
        Dim getVal = Function(indName As String, key As String, i As Integer) As Single
                         Dim rlist As List(Of ChartKit.Abstractions.IndicatorResult) = Nothing
                         If Not res.TryGetValue(indName, rlist) Then Return Single.NaN
                         If rlist Is Nothing OrElse i < 0 OrElse i >= rlist.Count Then Return Single.NaN
                         Dim rr = rlist(i)
                         If rr Is Nothing OrElse rr.Values Is Nothing Then Return Single.NaN
                         Dim v As Single
                         If rr.Values.TryGetValue(key, v) Then Return v
                         Return Single.NaN
                     End Function

        '' "Value" 우선 조회 (신호 판정용, ValAt 과 동일 규칙)
        Dim valOf = Function(indName As String, i As Integer) As Single
                        Dim rlist As List(Of ChartKit.Abstractions.IndicatorResult) = Nothing
                        If Not res.TryGetValue(indName, rlist) Then Return Single.NaN
                        If rlist Is Nothing OrElse i < 0 OrElse i >= rlist.Count Then Return Single.NaN
                        Dim rr = rlist(i)
                        If rr Is Nothing OrElse rr.Values Is Nothing Then Return Single.NaN
                        Dim v As Single
                        If rr.Values.TryGetValue("Value", v) Then Return v
                        For Each kv In rr.Values
                            If Not Single.IsNaN(kv.Value) Then Return kv.Value
                        Next
                        Return Single.NaN
                    End Function

        '' ── 각 봉 행 ──
        For i = 0 To _candles.Count - 1
            Dim c = _candles(i)
            Dim row As New List(Of String)
            row.Add(i.ToString())
            row.Add(c.Dt.ToString("yyyy-MM-dd HH:mm"))
            row.Add(c.Open.ToString("0.###"))
            row.Add(c.High.ToString("0.###"))
            row.Add(c.Low.ToString("0.###"))
            row.Add(c.Close.ToString("0.###"))
            row.Add(c.Volume.ToString())

            For Each col In indCols
                Dim v = getVal(col.Item1, col.Item2, i)
                row.Add(If(Single.IsNaN(v), "", v.ToString("0.####")))
            Next

            '' 신호 판정 (SignalLayer 와 동일 로직)
            For Each r In _signalRules
                Dim hitStr = ""
                If i >= 1 Then
                    Dim a0 = valOf(r.IndicatorA, i - 1) : Dim b0 = valOf(r.IndicatorB, i - 1)
                    Dim a1 = valOf(r.IndicatorA, i) : Dim b1 = valOf(r.IndicatorB, i)
                    If Not (Single.IsNaN(a0) OrElse Single.IsNaN(b0) OrElse Single.IsNaN(a1) OrElse Single.IsNaN(b1)) Then
                        Dim cu = (a0 <= b0) AndAlso (a1 > b1)
                        Dim cd = (a0 >= b0) AndAlso (a1 < b1)
                        Dim hit = If(r.CrossUp, cu, cd)
                        Dim brise = (b1 > b0)
                        If hit AndAlso r.RequireBRising AndAlso Not brise Then hit = False
                        '' 판정 상세: hit / cross / Brise
                        hitStr = $"hit={If(hit,1,0)};cross={If(If(r.CrossUp,cu,cd),1,0)};Brise={If(brise,1,0)};b1-b0={(b1 - b0):0.###}"
                    End If
                End If
                row.Add("""" & hitStr & """")
            Next

            sb.AppendLine(String.Join(",", row))
        Next

        System.IO.File.WriteAllText(path, sb.ToString(), New System.Text.UTF8Encoding(True))
        System.Windows.Forms.MessageBox.Show("CSV 저장 완료:" & Environment.NewLine & path, "차트 데이터 출력")
    End Sub
End Class
End Namespace
