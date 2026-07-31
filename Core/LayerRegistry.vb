Option Strict On
Option Explicit On
Option Infer Off

Imports ChartKit.Abstractions

Namespace Core
    '' 레이어 등록/제거/토글/순서 관리.
    '' 렌더링 중에는 변경이 없는 한 동일한 정렬 snapshot을 재사용한다.
    Public Class LayerRegistry
        Implements IDisposable

        Private ReadOnly _layers As New List(Of IChartLayer)()
        Private _orderedVisible As IChartLayer() = Array.Empty(Of IChartLayer)()
        Private _observedLayers As IChartLayer() = Array.Empty(Of IChartLayer)()
        Private _observedVisibility As Boolean() = Array.Empty(Of Boolean)()
        Private _observedZOrders As Integer() = Array.Empty(Of Integer)()
        Private _orderedCacheValid As Boolean

        Public Sub Add(layer As IChartLayer)
            If layer Is Nothing Then Return

            For index As Integer = 0 To _layers.Count - 1
                If String.Equals(
                    _layers(index).Id,
                    layer.Id,
                    StringComparison.Ordinal) Then Return
            Next

            _layers.Add(layer)
            InvalidateOrderedCache()
        End Sub

        Public Sub Remove(id As String)
            Dim removed As Boolean

            For index As Integer = _layers.Count - 1 To 0 Step -1
                Dim layer As IChartLayer = _layers(index)
                If Not String.Equals(layer.Id, id, StringComparison.Ordinal) Then Continue For

                _layers.RemoveAt(index)
                layer.Dispose()
                removed = True
            Next

            If removed Then InvalidateOrderedCache()
        End Sub

        Public Sub Toggle(id As String, visible As Boolean)
            For index As Integer = 0 To _layers.Count - 1
                Dim layer As IChartLayer = _layers(index)
                If Not String.Equals(layer.Id, id, StringComparison.Ordinal) Then Continue For

                If layer.IsVisible <> visible Then
                    layer.IsVisible = visible
                    InvalidateOrderedCache()
                End If
                Return
            Next
        End Sub

        Public Function Exists(id As String) As Boolean
            Return GetLayer(id) IsNot Nothing
        End Function

        Public Function IsLayerVisible(id As String) As Boolean
            Dim layer As IChartLayer = GetLayer(id)
            Return layer IsNot Nothing AndAlso layer.IsVisible
        End Function

        Public Function GetLayer(id As String) As IChartLayer
            For index As Integer = 0 To _layers.Count - 1
                Dim layer As IChartLayer = _layers(index)
                If String.Equals(layer.Id, id, StringComparison.Ordinal) Then Return layer
            Next
            Return Nothing
        End Function

        '' 호환 API. 신규 렌더링 코드는 OrderedView를 인덱스로 순회한다.
        Public Function Ordered() As IEnumerable(Of IChartLayer)
            Return OrderedView()
        End Function

        Public Function OrderedView() As IReadOnlyList(Of IChartLayer)
            If Not IsOrderedCacheCurrent() Then RebuildOrderedCache()
            Return _orderedVisible
        End Function

        Private Function IsOrderedCacheCurrent() As Boolean
            If Not _orderedCacheValid Then Return False
            If _observedLayers.Length <> _layers.Count Then Return False

            For index As Integer = 0 To _layers.Count - 1
                Dim layer As IChartLayer = _layers(index)
                If Not Object.ReferenceEquals(_observedLayers(index), layer) Then Return False
                If _observedVisibility(index) <> layer.IsVisible Then Return False
                If _observedZOrders(index) <> layer.ZOrder Then Return False
            Next

            Return True
        End Function

        Private Sub RebuildOrderedCache()
            Dim layerCount As Integer = _layers.Count

            If layerCount = 0 Then
                _observedLayers = Array.Empty(Of IChartLayer)()
                _observedVisibility = Array.Empty(Of Boolean)()
                _observedZOrders = Array.Empty(Of Integer)()
                _orderedVisible = Array.Empty(Of IChartLayer)()
                _orderedCacheValid = True
                Return
            End If

            Dim observedLayers(layerCount - 1) As IChartLayer
            Dim observedVisibility(layerCount - 1) As Boolean
            Dim observedZOrders(layerCount - 1) As Integer
            Dim visibleCount As Integer

            For index As Integer = 0 To layerCount - 1
                Dim layer As IChartLayer = _layers(index)
                observedLayers(index) = layer
                observedVisibility(index) = layer.IsVisible
                observedZOrders(index) = layer.ZOrder
                If layer.IsVisible Then visibleCount += 1
            Next

            Dim ordered As IChartLayer()
            If visibleCount = 0 Then
                ordered = Array.Empty(Of IChartLayer)()
            Else
                ReDim ordered(visibleCount - 1)
                Dim targetIndex As Integer
                For index As Integer = 0 To layerCount - 1
                    Dim layer As IChartLayer = _layers(index)
                    If Not layer.IsVisible Then Continue For
                    ordered(targetIndex) = layer
                    targetIndex += 1
                Next

                '' 기존 LINQ OrderBy와 동일하게 같은 ZOrder의 등록 순서를 유지한다.
                For index As Integer = 1 To ordered.Length - 1
                    Dim item As IChartLayer = ordered(index)
                    Dim position As Integer = index - 1
                    While position >= 0 AndAlso ordered(position).ZOrder > item.ZOrder
                        ordered(position + 1) = ordered(position)
                        position -= 1
                    End While
                    ordered(position + 1) = item
                Next
            End If

            _observedLayers = observedLayers
            _observedVisibility = observedVisibility
            _observedZOrders = observedZOrders
            _orderedVisible = ordered
            _orderedCacheValid = True
        End Sub

        Private Sub InvalidateOrderedCache()
            _orderedCacheValid = False
        End Sub

        Public Sub Dispose() Implements IDisposable.Dispose
            For index As Integer = 0 To _layers.Count - 1
                _layers(index).Dispose()
            Next
            _layers.Clear()
            _orderedVisible = Array.Empty(Of IChartLayer)()
            _observedLayers = Array.Empty(Of IChartLayer)()
            _observedVisibility = Array.Empty(Of Boolean)()
            _observedZOrders = Array.Empty(Of Integer)()
            _orderedCacheValid = True
        End Sub
    End Class
End Namespace
