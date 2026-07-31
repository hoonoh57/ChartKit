Imports System.Linq

Namespace Core
    Public Partial Class ChartControl
        Public Property StateProfile As String = "default"

        Public Sub SaveState(Optional profile As String = Nothing)
            Dim prof = If(profile, StateProfile)
            Dim st As New ChartState With {
                .CandleCount = If(_candles IsNot Nothing, _candles.Count, 0),
                .StartIndex = _vs.StartIndex, .CandleWidth = _vs.CandleWidth, .Gap = _vs.Gap,
                .IsAutoScaleY = _isAutoScaleY, .ManualMaxP = _manualMaxP, .ManualMinP = _manualMinP,
                .PanelRatios = New List(Of Single)(_panelRatios),
                .PanelBaselines = New Dictionary(Of Integer, List(Of Single)),
                .PanelZones = New Dictionary(Of Integer, PanelZoneState),
                .PctAxisMode = _pctAxisMode,
                .StrategyReentryLockMode = CInt(_strategyReentryOptions.Mode),
                .StrategyReentryLockThresholdPct = _strategyReentryOptions.ThresholdPct,
                .SignalRules = New List(Of SignalRule),
                .ShadeRules = New List(Of OverlayShadeRule)}
            For Each kv In _panelBaselines
                st.PanelBaselines(kv.Key) = New List(Of Single)(kv.Value)
            Next
            For Each zkv In _panelZones
                st.PanelZones(zkv.Key) = New PanelZoneState With {
                    .OverValue = zkv.Value.OverValue, .UnderValue = zkv.Value.UnderValue}
            Next
            For Each sg In _signalRules
                st.SignalRules.Add(New SignalRule With {
                    .Id = sg.Id, .IndicatorA = sg.IndicatorA, .IndicatorB = sg.IndicatorB,
                    .CrossUp = sg.CrossUp, .RequireBRising = sg.RequireBRising, .Side = sg.Side,
                    .MarkerShape = sg.MarkerShape, .ColorArgb = sg.ColorArgb, .Name = sg.Name})
            Next
            For Each sr In _shadeRules
                st.ShadeRules.Add(New OverlayShadeRule With {
                    .IndicatorA = sr.IndicatorA, .IndicatorB = sr.IndicatorB,
                    .ColorR = sr.ColorR, .ColorG = sr.ColorG, .ColorB = sr.ColorB,
                    .ColorA = sr.ColorA, .RequireBRising = sr.RequireBRising})
            Next
            For Each ind In _indicatorEngine.GetAll()
                Dim isr As New IndicatorState With {
                    .TypeName = IndicatorIdentity.SourceTypeName(ind),
                    .Params = New Dictionary(Of String, String)}
                If ind.Parameters IsNot Nothing Then
                    For Each kv In ind.Parameters
                        isr.Params(kv.Key) = Convert.ToString(kv.Value, Globalization.CultureInfo.InvariantCulture)
                    Next
                End If
                st.Indicators.Add(isr)
            Next
            For Each id In New String() {"Candle", "Crosshair", "Indicators", "Legend", "Panels", "Volume"}
                st.Layers.Add(New LayerToggleState With {.Id = id, .Visible = _registry.IsLayerVisible(id)})
            Next
            Dim gridLayer = TryCast(_registry.GetLayer("GridAxis"), Layers.GridAxisLayer)
            st.Layers.Add(New LayerToggleState With {
                .Id = "Grid", .Visible = gridLayer Is Nothing OrElse gridLayer.GridVisible})
            st.Save(prof)
        End Sub

        Public Sub RestoreState(Optional profile As String = Nothing)
            Dim prof = If(profile, StateProfile)
            Dim st = ChartState.Load(prof)
            If st Is Nothing Then Return
            _isRestoringState = True
            _strategyReentryOptions = New Strategies.StrategyReentryLockOptions With {
                .Mode = If([Enum].IsDefined(GetType(Strategies.StrategyReentryLockMode),
                                           st.StrategyReentryLockMode),
                           CType(st.StrategyReentryLockMode, Strategies.StrategyReentryLockMode),
                           Strategies.StrategyReentryLockMode.CumulativeClosedReturn),
                .ThresholdPct = If(st.StrategyReentryLockThresholdPct > 0.0R,
                                   st.StrategyReentryLockThresholdPct,
                                   Strategies.StrategyReentryLockOptions.DefaultThresholdPct)}

            _panelBaselines.Clear()
            If st.PanelBaselines IsNot Nothing Then
                For Each kv In st.PanelBaselines
                    If kv.Value IsNot Nothing Then _panelBaselines(kv.Key) = New List(Of Single)(kv.Value)
                Next
            End If
            _panelZones.Clear()
            If st.PanelZones IsNot Nothing Then
                For Each zkv In st.PanelZones
                    If zkv.Value IsNot Nothing Then
                        _panelZones(zkv.Key) = New PanelZoneState With {
                            .OverValue = zkv.Value.OverValue, .UnderValue = zkv.Value.UnderValue}
                    End If
                Next
            End If
            _shadeRules.Clear()
            _pctAxisMode = st.PctAxisMode
            If st.ShadeRules IsNot Nothing Then
                For Each sr In st.ShadeRules
                    If sr IsNot Nothing Then
                        _shadeRules.Add(New OverlayShadeRule With {
                            .IndicatorA = sr.IndicatorA, .IndicatorB = sr.IndicatorB,
                            .ColorR = sr.ColorR, .ColorG = sr.ColorG, .ColorB = sr.ColorB,
                            .ColorA = sr.ColorA, .RequireBRising = sr.RequireBRising})
                    End If
                Next
            End If
            _signalRules.Clear()
            If st.SignalRules IsNot Nothing Then
                For Each signal In st.SignalRules
                    If signal Is Nothing Then Continue For
                    _signalRules.Add(New SignalRule With {
                        .Id = If(String.IsNullOrWhiteSpace(signal.Id), Guid.NewGuid().ToString("N"), signal.Id),
                        .Name = signal.Name, .IndicatorA = signal.IndicatorA, .IndicatorB = signal.IndicatorB,
                        .CrossUp = signal.CrossUp, .RequireBRising = signal.RequireBRising,
                        .Side = signal.Side, .MarkerShape = signal.MarkerShape, .ColorArgb = signal.ColorArgb})
                Next
            End If

            For Each existing In _indicatorEngine.GetAll().ToList()
                _indicatorEngine.Remove(existing.Name)
            Next
            _lastRestoreCount = If(st.Indicators IsNot Nothing, st.Indicators.Count, 0)
            If st.Indicators IsNot Nothing Then
                For Each isr In st.Indicators
                    Try
                        If String.IsNullOrWhiteSpace(isr.TypeName) Then Continue For
                        Dim t = Type.GetType(isr.TypeName)
                        ChartLog.Info("[Restore] TypeName=" & isr.TypeName & " -> " & If(t Is Nothing, "NULL(못찾음)", t.FullName))
                        If t Is Nothing Then Continue For
                        Dim ind = TryCast(CreateWithOptionalCtor(t), Abstractions.IIndicator)
                        If ind Is Nothing Then Continue For
                        If isr.Params IsNot Nothing AndAlso ind.Parameters IsNot Nothing Then
                            Dim newParams As New Dictionary(Of String, Object)(ind.Parameters)
                            For Each kv In isr.Params
                                newParams(kv.Key) = If(ind.Parameters.ContainsKey(kv.Key),
                                                       ConvertLike(ind.Parameters(kv.Key), kv.Value), kv.Value)
                            Next
                            ind.Parameters = newParams
                        End If
                        _indicatorEngine.Register(ind)
                    Catch ex As Exception
                        ChartLog.Warning("[Restore] 지표 복원 예외", ex)
                    End Try
                Next
            End If
            If _candles IsNot Nothing AndAlso _candles.Count > 0 Then _indicatorEngine.CalculateAll(_candles)
            If st.PanelRatios IsNot Nothing AndAlso st.PanelRatios.Count > 0 Then
                _panelRatios = New List(Of Single)(st.PanelRatios)
            End If
            _isAutoScaleY = True
            _manualMaxP = 0
            _manualMinP = 0
            If st.Layers IsNot Nothing Then
                For Each lt In st.Layers
                    If String.Equals(lt.Id, "Grid", StringComparison.OrdinalIgnoreCase) Then
                        Dim gridLayer = TryCast(_registry.GetLayer("GridAxis"), Layers.GridAxisLayer)
                        If gridLayer IsNot Nothing Then gridLayer.GridVisible = lt.Visible
                    ElseIf Not String.Equals(lt.Id, "GridAxis", StringComparison.OrdinalIgnoreCase) Then
                        _registry.Toggle(lt.Id, lt.Visible)
                    End If
                Next
            End If

            ' 축 레이어는 항상 활성 상태다. 과거 GridAxis=False 상태는 무시한다.
            _registry.Toggle("GridAxis", True)

            ChartLog.Info("[Restore] indicators restored = " & _indicatorEngine.GetAll().Count)
            ChartLog.Info("[Restore] candles = " & If(_candles Is Nothing, 0, _candles.Count) & ", saved CandleCount = " & st.CandleCount)
            ChartLog.Info("[Restore] StartIndex = " & _vs.StartIndex & ", CandleWidth = " & _vs.CandleWidth)
            For Each ind In _indicatorEngine.GetAll()
                ChartLog.Info("   ind: " & ind.Name & " panel=" & ind.PanelIndex)
            Next
            _needsRepaint = True
            _isRestoringState = False
        End Sub

        Private Shared Function ConvertLike(template As Object, s As String) As Object
            Try
                If TypeOf template Is Integer Then Return Integer.Parse(s, Globalization.CultureInfo.InvariantCulture)
                If TypeOf template Is Single Then Return Single.Parse(s, Globalization.CultureInfo.InvariantCulture)
                If TypeOf template Is Double Then Return Double.Parse(s, Globalization.CultureInfo.InvariantCulture)
                If TypeOf template Is Boolean Then Return Boolean.Parse(s)
            Catch ex As Exception
                ChartLog.Warning($"지표 파라미터 변환 실패: '{s}'", ex)
            End Try
            Return s
        End Function

        Private Shared Function CreateWithOptionalCtor(t As Type) As Object
            Dim ctor0 = t.GetConstructor(Type.EmptyTypes)
            If ctor0 IsNot Nothing Then Return ctor0.Invoke(Nothing)
            Dim ctors = t.GetConstructors()
            If ctors Is Nothing OrElse ctors.Length = 0 Then Return Nothing
            Dim best = ctors(0)
            For Each c In ctors
                If c.GetParameters().Length < best.GetParameters().Length Then best = c
            Next
            Dim ps = best.GetParameters()
            Dim args(ps.Length - 1) As Object
            For i = 0 To ps.Length - 1
                args(i) = If(ps(i).HasDefaultValue, ps(i).DefaultValue,
                             If(ps(i).ParameterType.IsValueType, Activator.CreateInstance(ps(i).ParameterType), Nothing))
            Next
            Return best.Invoke(args)
        End Function
    End Class
End Namespace
