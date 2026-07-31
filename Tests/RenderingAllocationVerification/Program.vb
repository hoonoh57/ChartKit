Option Strict On
Option Explicit On
Option Infer Off

Imports SkiaSharp
Imports ChartKit.Abstractions
Imports ChartKit.Core
Imports ChartKit.Models

Namespace Verification
    Public Module Program
        Private Const CacheAllocationLimit As Long = 65536L
        Private Const PanelScaleAllocationLimit As Long = 131072L
        Private Const KindLookupAllocationLimit As Long = 4096L

        Public Function Main() As Integer
            Try
                VerifyLayerOrderingCache()
                VerifyIndicatorSnapshot()
                VerifyPanelScaleReuse()
                VerifySteadyStateAllocationBounds()
                VerifySeriesKindLookupAllocation()

                Console.WriteLine("rendering_allocation_verification=PASS")
                Return 0
            Catch ex As Exception
                Console.Error.WriteLine("rendering_allocation_verification=FAIL")
                Console.Error.WriteLine(ex.ToString())
                Return 1
            End Try
        End Function

        Private Sub VerifyLayerOrderingCache()
            Dim registry As New LayerRegistry()
            Dim firstEqual As New FakeLayer("equal-first", 10)
            Dim secondEqual As New FakeLayer("equal-second", 10)
            Dim high As New FakeLayer("high", 30)
            Dim middle As New FakeLayer("middle", 20)

            registry.Add(high)
            registry.Add(firstEqual)
            registry.Add(secondEqual)
            registry.Add(middle)

            Dim firstView As IReadOnlyList(Of IChartLayer) = registry.OrderedView()
            ExpectEqual(firstView.Count, 4, "초기 visible layer 수")
            ExpectReference(firstView(0), firstEqual, "동일 ZOrder 첫 등록 순서")
            ExpectReference(firstView(1), secondEqual, "동일 ZOrder 두 번째 등록 순서")
            ExpectReference(firstView(2), middle, "중간 ZOrder")
            ExpectReference(firstView(3), high, "높은 ZOrder")

            Dim repeatedView As IReadOnlyList(Of IChartLayer) = registry.OrderedView()
            ExpectReference(firstView, repeatedView, "변경 없는 정렬 snapshot 재사용")

            secondEqual.IsVisible = False
            Dim directMutationView As IReadOnlyList(Of IChartLayer) = registry.OrderedView()
            Expect(Not Object.ReferenceEquals(firstView, directMutationView),
                   "직접 visibility 변경이 cache에 반영되지 않음")
            ExpectEqual(directMutationView.Count, 3, "직접 visibility 변경 후 수")

            secondEqual.IsVisible = True
            registry.Toggle("middle", False)
            Dim toggledView As IReadOnlyList(Of IChartLayer) = registry.OrderedView()
            ExpectEqual(toggledView.Count, 3, "Toggle 후 visible layer 수")
            ExpectReference(toggledView(0), firstEqual, "Toggle 후 첫 순서")
            ExpectReference(toggledView(1), secondEqual, "Toggle 후 동일 순서")
            ExpectReference(toggledView(2), high, "Toggle 후 마지막 순서")

            registry.Remove("equal-first")
            Dim removedView As IReadOnlyList(Of IChartLayer) = registry.OrderedView()
            ExpectEqual(removedView.Count, 2, "Remove 후 visible layer 수")
            Expect(firstEqual.IsDisposed, "제거된 layer가 Dispose되지 않음")

            registry.Dispose()
            Expect(secondEqual.IsDisposed, "registry Dispose 누락")
            Expect(high.IsDisposed, "high layer Dispose 누락")
            Expect(middle.IsDisposed, "숨김 layer Dispose 누락")
        End Sub

        Private Sub VerifyIndicatorSnapshot()
            Dim engine As New IndicatorEngine()
            engine.Register(New FakeIndicator("panel-one", 1, 1.0F))

            Dim firstView As IReadOnlyList(Of IIndicator) = engine.GetAllView()
            Dim repeatedView As IReadOnlyList(Of IIndicator) = engine.GetAllView()
            ExpectReference(firstView, repeatedView, "지표 snapshot 재사용")
            ExpectEqual(firstView.Count, 1, "첫 지표 snapshot 수")

            Dim compatibilityCopy As List(Of IIndicator) = engine.GetAll()
            Expect(Not Object.ReferenceEquals(firstView, compatibilityCopy),
                   "GetAll 호환 API가 내부 snapshot을 노출함")

            engine.Register(New FakeIndicator("panel-two", 2, 2.0F))
            Dim expandedView As IReadOnlyList(Of IIndicator) = engine.GetAllView()
            Expect(Not Object.ReferenceEquals(firstView, expandedView),
                   "지표 등록 후 snapshot이 교체되지 않음")
            ExpectEqual(expandedView.Count, 2, "지표 등록 후 snapshot 수")

            Dim removeId As String = expandedView(0).Name
            engine.Remove(removeId)
            Dim reducedView As IReadOnlyList(Of IIndicator) = engine.GetAllView()
            ExpectEqual(reducedView.Count, 1, "지표 제거 후 snapshot 수")
            Expect(Not Object.ReferenceEquals(expandedView, reducedView),
                   "지표 제거 후 snapshot이 교체되지 않음")
        End Sub

        Private Sub VerifyPanelScaleReuse()
            Dim engine As New IndicatorEngine()
            engine.Register(New FakeIndicator("panel-one", 1, 1.0F))
            engine.Register(New FakeIndicator("panel-two", 2, 2.0F))

            Dim candles As List(Of CandleItem) = CreateCandles(32)
            engine.CalculateAll(candles)

            Dim ctx As New ChartContext With {
                .Candles = candles,
                .Engine = engine,
                .StartIndex = 0,
                .EndIndex = candles.Count - 1
            }
            Dim scales As New Dictionary(Of Integer, PanelScale)()
            Dim indexes As New List(Of Integer)()

            PanelScaleCalculator.CalculateInto(ctx, scales, indexes)
            ExpectEqual(indexes.Count, 2, "panel index 수")
            ExpectEqual(indexes(0), 1, "첫 panel index")
            ExpectEqual(indexes(1), 2, "두 번째 panel index")
            ExpectEqual(scales.Count, 2, "panel scale 수")

            Dim firstScale As PanelScale = scales(0)
            Dim secondScale As PanelScale = scales(1)
            Expect(firstScale.Minimum < firstScale.Maximum, "첫 scale 범위 오류")
            Expect(secondScale.Minimum < secondScale.Maximum, "두 번째 scale 범위 오류")

            PanelScaleCalculator.CalculateInto(ctx, scales, indexes)
            ExpectReference(firstScale, scales(0), "첫 PanelScale 객체 재사용")
            ExpectReference(secondScale, scales(1), "두 번째 PanelScale 객체 재사용")

            Dim secondId As String = engine.GetAllView()(1).Name
            engine.Remove(secondId)
            PanelScaleCalculator.CalculateInto(ctx, scales, indexes)
            ExpectEqual(scales.Count, 1, "지표 제거 후 stale scale 제거")
            Expect(Not scales.ContainsKey(1), "stale panel slot이 남음")
        End Sub

        Private Sub VerifySteadyStateAllocationBounds()
            Dim registry As New LayerRegistry()
            registry.Add(New FakeLayer("low", 10))
            registry.Add(New FakeLayer("middle", 20))
            registry.Add(New FakeLayer("high", 30))

            Dim engine As New IndicatorEngine()
            engine.Register(New FakeIndicator("panel-one", 1, 1.0F))
            engine.Register(New FakeIndicator("panel-two", 2, 2.0F))

            Dim candles As List(Of CandleItem) = CreateCandles(48)
            engine.CalculateAll(candles)
            Dim ctx As New ChartContext With {
                .Candles = candles,
                .Engine = engine,
                .StartIndex = 0,
                .EndIndex = candles.Count - 1
            }
            Dim scales As New Dictionary(Of Integer, PanelScale)()
            Dim indexes As New List(Of Integer)()

            For warmup As Integer = 0 To 199
                Dim warmLayers As IReadOnlyList(Of IChartLayer) = registry.OrderedView()
                Dim warmIndicators As IReadOnlyList(Of IIndicator) = engine.GetAllView()
                PanelScaleCalculator.CalculateInto(ctx, scales, indexes)
                GC.KeepAlive(warmLayers)
                GC.KeepAlive(warmIndicators)
            Next

            ForceCollection()
            Dim cacheBefore As Long = GC.GetAllocatedBytesForCurrentThread()
            Dim checksum As Integer
            For iteration As Integer = 0 To 19999
                Dim layers As IReadOnlyList(Of IChartLayer) = registry.OrderedView()
                Dim indicators As IReadOnlyList(Of IIndicator) = engine.GetAllView()
                checksum += layers.Count + indicators.Count
                checksum += layers(0).ZOrder + indicators(0).PanelIndex
            Next
            Dim cacheAllocated As Long =
                GC.GetAllocatedBytesForCurrentThread() - cacheBefore
            GC.KeepAlive(checksum)

            ForceCollection()
            Dim scaleBefore As Long = GC.GetAllocatedBytesForCurrentThread()
            For iteration As Integer = 0 To 4999
                PanelScaleCalculator.CalculateInto(ctx, scales, indexes)
                checksum += scales.Count + indexes.Count
            Next
            Dim scaleAllocated As Long =
                GC.GetAllocatedBytesForCurrentThread() - scaleBefore
            GC.KeepAlive(checksum)

            Console.WriteLine("render_cache_allocated_bytes=" & cacheAllocated.ToString())
            Console.WriteLine("panel_scale_allocated_bytes=" & scaleAllocated.ToString())

            Expect(cacheAllocated <= CacheAllocationLimit,
                   "steady-state render cache 할당 초과: " & cacheAllocated.ToString())
            Expect(scaleAllocated <= PanelScaleAllocationLimit,
                   "steady-state panel scale 할당 초과: " & scaleAllocated.ToString())

            registry.Dispose()
        End Sub

        Private Sub VerifySeriesKindLookupAllocation()
            Dim result As New IndicatorResult()
            Dim keys As String() = {"Value", "Hist", "Upper", "Direction"}
            Dim checksum As Integer

            For warmup As Integer = 0 To 999
                checksum += CInt(result.KindOf(keys(warmup And 3)))
            Next

            ForceCollection()
            Dim before As Long = GC.GetAllocatedBytesForCurrentThread()
            For iteration As Integer = 0 To 99999
                checksum += CInt(result.KindOf(keys(iteration And 3)))
            Next
            Dim allocated As Long = GC.GetAllocatedBytesForCurrentThread() - before
            GC.KeepAlive(checksum)

            Console.WriteLine("series_kind_allocated_bytes=" & allocated.ToString())
            Expect(allocated <= KindLookupAllocationLimit,
                   "SeriesKind key lookup 할당 초과: " & allocated.ToString())
        End Sub

        Private Sub ForceCollection()
            GC.Collect()
            GC.WaitForPendingFinalizers()
            GC.Collect()
        End Sub

        Private Function CreateCandles(count As Integer) As List(Of CandleItem)
            Dim candles As New List(Of CandleItem)(count)
            Dim startTime As New DateTime(2026, 7, 31, 9, 0, 0)

            For index As Integer = 0 To count - 1
                Dim closeValue As Single = 100.0F + CSng(index * 0.5R) +
                                           CSng(Math.Sin(index / 3.0R) * 2.0R)
                candles.Add(New CandleItem With {
                    .Dt = startTime.AddMinutes(index),
                    .Open = closeValue - 0.3F,
                    .High = closeValue + 0.8F,
                    .Low = closeValue - 0.9F,
                    .Close = closeValue,
                    .Volume = 1000L + index * 10L
                })
            Next

            Return candles
        End Function

        Private Sub Expect(condition As Boolean,
                           message As String)
            If Not condition Then Throw New InvalidOperationException(message)
        End Sub

        Private Sub ExpectEqual(actual As Integer,
                                expected As Integer,
                                description As String)
            If actual <> expected Then
                Throw New InvalidOperationException(
                    description & ": expected=" & expected.ToString() &
                    ", actual=" & actual.ToString())
            End If
        End Sub

        Private Sub ExpectReference(actual As Object,
                                    expected As Object,
                                    description As String)
            If Not Object.ReferenceEquals(actual, expected) Then
                Throw New InvalidOperationException(description)
            End If
        End Sub

        Private NotInheritable Class FakeLayer
            Implements IChartLayer

            Private ReadOnly _id As String
            Private ReadOnly _zOrder As Integer

            Public Sub New(id As String, zOrder As Integer)
                _id = id
                _zOrder = zOrder
            End Sub

            Public ReadOnly Property Id As String Implements IChartLayer.Id
                Get
                    Return _id
                End Get
            End Property

            Public Property IsVisible As Boolean = True Implements IChartLayer.IsVisible

            Public ReadOnly Property ZOrder As Integer Implements IChartLayer.ZOrder
                Get
                    Return _zOrder
                End Get
            End Property

            Public ReadOnly Property IsDisposed As Boolean
                Get
                    Return _isDisposed
                End Get
            End Property
            Private _isDisposed As Boolean

            Public Sub Draw(canvas As SKCanvas,
                            ctx As ChartContext) Implements IChartLayer.Draw
            End Sub

            Public Sub Dispose() Implements IDisposable.Dispose
                _isDisposed = True
            End Sub
        End Class

        Private NotInheritable Class FakeIndicator
            Implements IIndicator

            Private ReadOnly _legacyName As String
            Private ReadOnly _panelIndex As Integer
            Private ReadOnly _factor As Single
            Private _parameters As Dictionary(Of String, Object)

            Public Sub New(legacyName As String,
                           panelIndex As Integer,
                           factor As Single)
                _legacyName = legacyName
                _panelIndex = panelIndex
                _factor = factor
                _parameters = New Dictionary(Of String, Object) From {
                    {"PanelIndex", panelIndex},
                    {"Factor", factor}
                }
            End Sub

            Public ReadOnly Property Name As String Implements IIndicator.Name
                Get
                    Return _legacyName
                End Get
            End Property

            Public ReadOnly Property DisplayName As String Implements IIndicator.DisplayName
                Get
                    Return _legacyName
                End Get
            End Property

            Public ReadOnly Property PanelIndex As Integer Implements IIndicator.PanelIndex
                Get
                    Return _panelIndex
                End Get
            End Property

            Public Property Parameters As Dictionary(Of String, Object) Implements IIndicator.Parameters
                Get
                    Return _parameters
                End Get
                Set(value As Dictionary(Of String, Object))
                    _parameters = If(
                        value Is Nothing,
                        New Dictionary(Of String, Object)(),
                        New Dictionary(Of String, Object)(value))
                End Set
            End Property

            Public Function Calculate(candles As IReadOnlyList(Of CandleItem)) As List(Of IndicatorResult) Implements IIndicator.Calculate
                Dim results As New List(Of IndicatorResult)(candles.Count)
                For index As Integer = 0 To candles.Count - 1
                    results.Add(CreateResult(index, candles(index).Close))
                Next
                Return results
            End Function

            Public Function UpdateLast(candles As IReadOnlyList(Of CandleItem),
                                       prevResults As IReadOnlyList(Of IndicatorResult)) As IndicatorResult Implements IIndicator.UpdateLast
                Dim index As Integer = candles.Count - 1
                Return CreateResult(index, candles(index).Close)
            End Function

            Private Function CreateResult(index As Integer,
                                          closeValue As Single) As IndicatorResult
                Return New IndicatorResult With {
                    .Name = Name,
                    .Index = index,
                    .PanelIndex = PanelIndex,
                    .Values = New Dictionary(Of String, Single) From {
                        {"Value", closeValue * _factor}
                    },
                    .SeriesKinds = New Dictionary(Of String, SeriesKind) From {
                        {"Value", SeriesKind.Line}
                    }
                }
            End Function
        End Class
    End Module
End Namespace
