Imports System.Linq
Imports System.Windows.Forms
Imports ChartKit.Abstractions

Namespace Core
    Public Partial Class ChartControl
        Private Sub ShowContextMenu(pt As Drawing.Point)
            Dim menu As New ContextMenuStrip()
            Dim addMenu As New ToolStripMenuItem("지표 추가")
            For Each entry In IndicatorCatalog.All()
                Dim key = entry.Key
                addMenu.DropDownItems.Add(entry.DisplayName, Nothing,
                    Sub(s, ev)
                        Dim ind = IndicatorCatalog.Create(key)
                        If ind IsNot Nothing Then AddIndicator(ind)
                    End Sub)
            Next
            If addMenu.DropDownItems.Count = 0 Then addMenu.DropDownItems.Add("(등록된 지표 없음)").Enabled = False
            menu.Items.Add(addMenu)

            Dim curIndicators = _indicatorEngine.GetAll()
            Dim delMenu As New ToolStripMenuItem("지표 삭제")
            If curIndicators.Count = 0 Then
                delMenu.Enabled = False
            Else
                For Each ind In curIndicators
                    Dim nm = ind.Name
                    delMenu.DropDownItems.Add(ind.DisplayName, Nothing, Sub(s, ev) RemoveIndicator(nm))
                Next
            End If
            menu.Items.Add(delMenu)

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
                            indSub.DropDownItems.Add($"{pKey} = {pVal}", Nothing, Sub(s, ev) EditIndicatorParam(indRef, pKey))
                        Next
                    End If
                    If indSub.DropDownItems.Count = 0 Then indSub.DropDownItems.Add("(수정 가능한 파라미터 없음)").Enabled = False
                    editMenu.DropDownItems.Add(indSub)
                Next
            End If
            menu.Items.Add(editMenu)

            Dim slot = FindSubPanelSlot(pt.Y)
            Dim baseMenu As New ToolStripMenuItem("기준선")
            If slot < 0 Then
                baseMenu.Enabled = False
                baseMenu.Text = "기준선 (서브패널에서 우클릭)"
            Else
                Dim slotRef = slot
                baseMenu.DropDownItems.Add("기준선 추가...", Nothing,
                    Sub(s, ev)
                        Dim inp = Microsoft.VisualBasic.Interaction.InputBox("기준선 값을 입력하세요.", "기준선 추가", "")
                        If String.IsNullOrWhiteSpace(inp) Then Return
                        Dim v As Single
                        If Single.TryParse(inp.Trim(), Globalization.NumberStyles.Float, Globalization.CultureInfo.InvariantCulture, v) Then
                            If Not _panelBaselines.ContainsKey(slotRef) Then _panelBaselines(slotRef) = New List(Of Single)
                            _panelBaselines(slotRef).Add(v)
                            ScheduleStateSave()
                            Invalidate()
                        End If
                    End Sub)
                Dim clearItem = baseMenu.DropDownItems.Add("이 패널 기준선 모두 지우기", Nothing,
                    Sub(s, ev)
                        _panelBaselines.Remove(slotRef)
                        ScheduleStateSave()
                        Invalidate()
                    End Sub)
                clearItem.Enabled = _panelBaselines.ContainsKey(slotRef) AndAlso _panelBaselines(slotRef).Count > 0
            End If
            menu.Items.Add(baseMenu)

            Dim zoneMenu As New ToolStripMenuItem("음영")
            Dim isMainChart = pt.Y >= _mainRect.Top AndAlso pt.Y <= _mainRect.Bottom
            If slot >= 0 Then
                Dim zslot = slot
                zoneMenu.Text = "음영 (서브패널)"
                zoneMenu.DropDownItems.Add("과열 음영 설정...(이상)", Nothing,
                    Sub(s, ev) SetPanelZoneValue(zslot, True))
                zoneMenu.DropDownItems.Add("침체 음영 설정...(이하)", Nothing,
                    Sub(s, ev) SetPanelZoneValue(zslot, False))
                Dim clearZone = zoneMenu.DropDownItems.Add("이 패널 음영 지우기", Nothing,
                    Sub(s, ev)
                        _panelZones.Remove(zslot)
                        ScheduleStateSave()
                        Invalidate()
                    End Sub)
                clearZone.Enabled = _panelZones.ContainsKey(zslot)
            ElseIf isMainChart Then
                zoneMenu.Text = "음영 (오버레이)"
                PopulateOverlayShadeMenu(zoneMenu)
            Else
                zoneMenu.Enabled = False
                zoneMenu.Text = "음영"
            End If
            menu.Items.Add(zoneMenu)

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
                            Sub(s, ev) AddShadeRule(aName, bName, False))
                        aSub.DropDownItems.Add(aDisp & " >= " & indB.DisplayName & "  (B상승중만)", Nothing,
                            Sub(s, ev) AddShadeRule(aName, bName, True))
                    Next
                    addShade.DropDownItems.Add(aSub)
                Next
            End If
            shadeMenu.DropDownItems.Add(addShade)
            Dim clearShade = shadeMenu.DropDownItems.Add("모든 음영 규칙 지우기", Nothing,
                Sub(s, ev)
                    _shadeRules.Clear()
                    ScheduleStateSave()
                    Invalidate()
                End Sub)
            clearShade.Enabled = _shadeRules.Count > 0
            menu.Items.Add(shadeMenu)

            Dim sigMenu As New ToolStripMenuItem("신호 검색")
            Dim addSig As New ToolStripMenuItem("신호 추가 (A 돌파 B)")
            If overlays.Count < 2 Then
                addSig.Enabled = False
                addSig.Text = "신호 추가 (오버레이 지표 2개 이상 필요)"
            Else
                For Each iA In overlays
                    Dim aName = iA.Name
                    Dim aDisp = iA.DisplayName
                    Dim aSub As New ToolStripMenuItem(aDisp & " 돌파 ...")
                    For Each iB In overlays
                        If iB.Name = aName Then Continue For
                        Dim bName = iB.Name
                        Dim bDisp = iB.DisplayName
                        aSub.DropDownItems.Add(aDisp & " ▲상향돌파 " & bDisp, Nothing,
                            Sub(s, ev) AddSignalRule(aName, bName, True, False))
                        aSub.DropDownItems.Add(aDisp & " ▲상향돌파 " & bDisp & "  (B상승중만)", Nothing,
                            Sub(s, ev) AddSignalRule(aName, bName, True, True))
                        aSub.DropDownItems.Add(aDisp & " ▼하향돌파 " & bDisp, Nothing,
                            Sub(s, ev) AddSignalRule(aName, bName, False, False))
                    Next
                    addSig.DropDownItems.Add(aSub)
                Next
            End If
            sigMenu.DropDownItems.Add(addSig)
            If _signalRules.Count > 0 Then
                Dim sigEditMenu As New ToolStripMenuItem("신호 규칙 편집(속성)...")
                Dim sigDelMenu As New ToolStripMenuItem("신호 규칙 삭제")
                For Each r In _signalRules.ToList()
                    Dim editId = r.Id
                    sigEditMenu.DropDownItems.Add(r.ToString(), Nothing, Sub(s, ev) EditSignalRule(editId))
                    Dim deleteId = r.Id
                    sigDelMenu.DropDownItems.Add(r.ToString(), Nothing,
                        Sub(s, ev)
                            _signalRules.RemoveAll(Function(rule) rule.Id = deleteId)
                            ScheduleStateSave()
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
                    ScheduleStateSave()
                    Invalidate()
                End Sub)
            clearSig.Enabled = _signalRules.Count > 0
            menu.Items.Add(sigMenu)

            Dim strategyMenu As New ToolStripMenuItem("기본 매매전략 v1")
            Dim captureIndex = IndexAtChartPoint(pt.X)
            Dim setCapture = strategyMenu.DropDownItems.Add(
                "현재 봉 종가를 조건검색 포착가로 지정", Nothing,
                Sub(s, ev)
                    If captureIndex < 0 OrElse captureIndex >= _candles.Count Then Return
                    SetStrategyCapture(captureIndex, _candles(captureIndex).Close)
                End Sub)
            setCapture.Enabled = isMainChart AndAlso captureIndex >= 0 AndAlso captureIndex < _candles.Count
            If _strategyCapture IsNot Nothing Then
                strategyMenu.DropDownItems.Add(
                    $"포착: {_strategyCapture.CapturedAt:MM-dd HH:mm} / {_strategyCapture.CapturePrice:N0}").Enabled = False
            End If
            Dim strategyLayer = TryCast(_registry.GetLayer("StrategyTrades"), Layers.StrategyTradeLayer)
            If strategyLayer IsNot Nothing Then
                strategyMenu.DropDownItems.Add("상태: " & strategyLayer.LastStatus).Enabled = False
            End If
            Dim reentryMenu As New ToolStripMenuItem("재진입 차단 기준")
            Dim cumulativeItem = DirectCast(
                reentryMenu.DropDownItems.Add(
                    "누적 실현 총수익률", Nothing,
                    Sub(s, ev)
                        SetStrategyReentryLockMode(
                            Strategies.StrategyReentryLockMode.CumulativeClosedReturn)
                    End Sub),
                ToolStripMenuItem)
            cumulativeItem.Checked =
                _strategyReentryOptions.Mode =
                Strategies.StrategyReentryLockMode.CumulativeClosedReturn
            Dim singleItem = DirectCast(
                reentryMenu.DropDownItems.Add(
                    "단일 거래 수익률", Nothing,
                    Sub(s, ev)
                        SetStrategyReentryLockMode(
                            Strategies.StrategyReentryLockMode.SingleTradeReturn)
                    End Sub),
                ToolStripMenuItem)
            singleItem.Checked =
                _strategyReentryOptions.Mode =
                Strategies.StrategyReentryLockMode.SingleTradeReturn
            reentryMenu.DropDownItems.Add(New ToolStripSeparator())
            reentryMenu.DropDownItems.Add(
                $"임계값 변경 (현재 {_strategyReentryOptions.ThresholdPct:0.##}%)", Nothing,
                Sub(s, ev) EditStrategyReentryLockThreshold())
            strategyMenu.DropDownItems.Add(reentryMenu)
            Dim clearCapture = strategyMenu.DropDownItems.Add(
                "전략 포착점 해제", Nothing, Sub(s, ev) ClearStrategyCapture())
            clearCapture.Enabled = _strategyCapture IsNot Nothing
            menu.Items.Add(strategyMenu)

            menu.Items.Add(New ToolStripSeparator())
            Dim viewMenu As New ToolStripMenuItem("차트 요소")
            AddLayerToggle(viewMenu, "Crosshair", "크로스헤어")
            AddLayerToggle(viewMenu, "Indicators", "지표선")
            AddLayerToggle(viewMenu, "Legend", "레전드")
            AddLayerToggle(viewMenu, "Volume", "거래량")
            AddGridToggle(viewMenu)
            AddPctAxisMenu(viewMenu)
            menu.Items.Add(viewMenu)

            menu.Items.Add(New ToolStripSeparator())
            menu.Items.Add("전체 지표 삭제", Nothing, Sub(s, ev) ClearIndicators())
            menu.Items.Add("현재 차트 데이터 출력(CSV)", Nothing, Sub(s, ev) DumpChartDataCsv())
            menu.Items.Add("최신으로 이동", Nothing,
                Sub(s, ev)
                    MoveToLatestVisible()
                    _needsRepaint = True
                End Sub)
            menu.Show(_sk, pt)
        End Sub

        Private Sub SetStrategyReentryLockMode(mode As Strategies.StrategyReentryLockMode)
            _strategyReentryOptions.Mode = mode
            ScheduleStateSave()
            Invalidate()
        End Sub

        Private Sub EditStrategyReentryLockThreshold()
            Dim input = Microsoft.VisualBasic.Interaction.InputBox(
                "재진입을 영구 차단할 실현수익률(%)을 입력하세요.",
                "재진입 차단 임계값",
                _strategyReentryOptions.ThresholdPct.ToString(
                    "0.##", Globalization.CultureInfo.CurrentCulture))
            If String.IsNullOrWhiteSpace(input) Then Return

            Dim threshold As Double
            If Not Double.TryParse(input.Trim(),
                                   Globalization.NumberStyles.Float,
                                   Globalization.CultureInfo.CurrentCulture,
                                   threshold) AndAlso
               Not Double.TryParse(input.Trim(),
                                   Globalization.NumberStyles.Float,
                                   Globalization.CultureInfo.InvariantCulture,
                                   threshold) Then Return
            If threshold <= 0.0R OrElse threshold > 10000.0R Then Return

            _strategyReentryOptions.ThresholdPct = threshold
            ScheduleStateSave()
            Invalidate()
        End Sub

        Private Function IndexAtChartPoint(x As Integer) As Integer
            If _candles Is Nothing OrElse _candles.Count = 0 Then Return -1
            Dim pitch = _vs.CandleWidth + _vs.Gap
            If pitch <= 0 OrElse x < _mainRect.Left OrElse x > _mainRect.Right Then Return -1
            Return _vs.StartIndex + CInt(Math.Floor((x - _mainRect.Left) / pitch))
        End Function

        Private Sub SetPanelZoneValue(slot As Integer, over As Boolean)
            Dim title = If(over, "과열 음영", "침체 음영")
            Dim prompt = If(over, "과열 기준값(이상 음영):", "침체 기준값(이하 음영):")
            Dim inp = Microsoft.VisualBasic.Interaction.InputBox(prompt, title, "")
            If String.IsNullOrWhiteSpace(inp) Then Return
            Dim v As Single
            If Not Single.TryParse(inp.Trim(), Globalization.NumberStyles.Float, Globalization.CultureInfo.InvariantCulture, v) Then Return
            If Not _panelZones.ContainsKey(slot) Then _panelZones(slot) = New PanelZoneState()
            If over Then _panelZones(slot).OverValue = v Else _panelZones(slot).UnderValue = v
            ScheduleStateSave()
            Invalidate()
        End Sub

        Private Sub PopulateOverlayShadeMenu(parent As ToolStripMenuItem)
            Dim candidates = _signalRules.Where(
                Function(rule) rule IsNot Nothing AndAlso rule.CrossUp AndAlso
                    Not String.IsNullOrWhiteSpace(rule.IndicatorA) AndAlso
                    Not String.IsNullOrWhiteSpace(rule.IndicatorB) AndAlso
                    rule.IndicatorA.StartsWith("JMA_", StringComparison.OrdinalIgnoreCase) AndAlso
                    rule.IndicatorB.StartsWith("JMA_", StringComparison.OrdinalIgnoreCase)).
                GroupBy(Function(rule) rule.IndicatorA & ChrW(0) & rule.IndicatorB).
                Select(Function(group) group.First()).ToList()

            If candidates.Count = 0 Then
                Dim empty = parent.DropDownItems.Add("JMA 매수 신호 규칙이 필요합니다")
                empty.Enabled = False
                Return
            End If

            For Each signal In candidates
                Dim indicatorA = signal.IndicatorA
                Dim indicatorB = signal.IndicatorB
                Dim enabled = _shadeRules.Any(
                    Function(rule) String.Equals(rule.IndicatorA, indicatorA, StringComparison.Ordinal) AndAlso
                                   String.Equals(rule.IndicatorB, indicatorB, StringComparison.Ordinal))
                Dim item As New ToolStripMenuItem(
                    $"{indicatorA} ≥ {indicatorB} · Entry 60+ 조건 음영") With {
                    .Checked = enabled, .CheckOnClick = False}
                AddHandler item.Click,
                    Sub(s, ev)
                        Dim exists = _shadeRules.Any(
                            Function(rule) String.Equals(rule.IndicatorA, indicatorA, StringComparison.Ordinal) AndAlso
                                           String.Equals(rule.IndicatorB, indicatorB, StringComparison.Ordinal))
                        If exists Then
                            _shadeRules.RemoveAll(
                                Function(rule) String.Equals(rule.IndicatorA, indicatorA, StringComparison.Ordinal) AndAlso
                                               String.Equals(rule.IndicatorB, indicatorB, StringComparison.Ordinal))
                        Else
                            _shadeRules.Add(New OverlayShadeRule With {
                                .IndicatorA = indicatorA, .IndicatorB = indicatorB,
                                .RequireBRising = True})
                        End If
                        item.Checked = Not exists
                        ScheduleStateSave()
                        _needsRepaint = True
                        Invalidate()
                    End Sub
                parent.DropDownItems.Add(item)
            Next
        End Sub

        Private Sub AddShadeRule(a As String, b As String, requireBRising As Boolean)
            _shadeRules.Add(New OverlayShadeRule With {.IndicatorA = a, .IndicatorB = b, .RequireBRising = requireBRising})
            ScheduleStateSave()
            Invalidate()
        End Sub

        Private Sub AddSignalRule(a As String, b As String, crossUp As Boolean, requireBRising As Boolean)
            _signalRules.Add(New SignalRule With {
                .IndicatorA = a, .IndicatorB = b, .CrossUp = crossUp, .RequireBRising = requireBRising})
            ScheduleStateSave()
            Invalidate()
        End Sub

        Private Sub EditSignalRule(id As String)
            Dim target = _signalRules.FirstOrDefault(Function(rule) rule.Id = id)
            If target Is Nothing Then Return
            Using dlg As New UI.SignalPropertyDialog(target)
                If dlg.ShowDialog(Me) = DialogResult.OK Then
                    ScheduleStateSave()
                    Invalidate()
                End If
            End Using
        End Sub

        Private Sub AddPctAxisMenu(parent As ToolStripMenuItem)
            Dim pctMenu As New ToolStripMenuItem("등락률축(좌)")
            Dim labels = New String() {"끄기", "전일종가대비", "시가대비"}
            For mode = 0 To 2
                Dim selectedMode = mode
                Dim item As New ToolStripMenuItem(labels(mode)) With {.Checked = (_pctAxisMode = mode)}
                AddHandler item.Click,
                    Sub(s, ev)
                        _pctAxisMode = selectedMode
                        ScheduleStateSave()
                        Invalidate()
                    End Sub
                pctMenu.DropDownItems.Add(item)
            Next
            parent.DropDownItems.Add(pctMenu)
        End Sub

        Private Sub AddLayerToggle(parent As ToolStripMenuItem, layerId As String, label As String)
            Dim item As New ToolStripMenuItem(label) With {.Checked = _registry.IsLayerVisible(layerId)}
            AddHandler item.Click,
                Sub(s, ev)
                    _registry.Toggle(layerId, Not _registry.IsLayerVisible(layerId))
                    _needsRepaint = True
                    ScheduleStateSave()
                End Sub
            parent.DropDownItems.Add(item)
        End Sub

        Private Sub AddGridToggle(parent As ToolStripMenuItem)
            Dim gridLayer = TryCast(_registry.GetLayer("GridAxis"), Layers.GridAxisLayer)
            Dim item As New ToolStripMenuItem("그리드") With {
                .Checked = gridLayer IsNot Nothing AndAlso gridLayer.GridVisible}
            AddHandler item.Click,
                Sub(s, ev)
                    Dim target = TryCast(_registry.GetLayer("GridAxis"), Layers.GridAxisLayer)
                    If target Is Nothing Then Return
                    target.GridVisible = Not target.GridVisible
                    item.Checked = target.GridVisible
                    _needsRepaint = True
                    ScheduleStateSave()
                End Sub
            parent.DropDownItems.Add(item)
        End Sub

        Private Sub EditIndicatorParam(ind As IIndicator, paramKey As String)
            If ind Is Nothing OrElse ind.Parameters Is Nothing Then Return
            Dim cur = If(ind.Parameters.ContainsKey(paramKey), ind.Parameters(paramKey).ToString(), "")
            Dim input = Microsoft.VisualBasic.Interaction.InputBox($"{ind.DisplayName} 의 {paramKey} 값 입력:", "지표 수정", cur)
            If String.IsNullOrWhiteSpace(input) Then Return
            Dim newParams As New Dictionary(Of String, Object)(ind.Parameters)
            Dim oldVal = If(ind.Parameters.ContainsKey(paramKey), ind.Parameters(paramKey), Nothing)
            Dim parsed As Object = input
            If TypeOf oldVal Is Integer Then
                Dim v As Integer
                If Not Integer.TryParse(input, v) Then Return Else parsed = v
            ElseIf TypeOf oldVal Is Single Then
                Dim v As Single
                If Not Single.TryParse(input, v) Then Return Else parsed = v
            ElseIf TypeOf oldVal Is Double Then
                Dim v As Double
                If Not Double.TryParse(input, v) Then Return Else parsed = v
            End If
            newParams(paramKey) = parsed
            Dim oldName = ind.Name
            ind.Parameters = newParams
            If oldName <> ind.Name Then
                _indicatorEngine.Remove(oldName)
                _indicatorEngine.Register(ind)
            End If
            If _candles IsNot Nothing AndAlso _candles.Count > 0 Then _indicatorEngine.CalculateAll(_candles)
            _needsRepaint = True
            ScheduleStateSave()
        End Sub
    End Class
End Namespace
