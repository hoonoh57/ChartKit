Imports System.Linq
Imports ChartKit.Abstractions

Namespace Core
    '' 레이어 등록/제거/토글/순서 관리.
    Public Class LayerRegistry
        Implements IDisposable
        Private ReadOnly _layers As New List(Of IChartLayer)

        Public Sub Add(layer As IChartLayer)
            If layer Is Nothing Then Return
            If _layers.Any(Function(l) l.Id = layer.Id) Then Return
            _layers.Add(layer)
        End Sub

        Public Sub Remove(id As String)
            Dim removed = _layers.Where(Function(l) l.Id = id).ToList()
            For Each layer In removed
                _layers.Remove(layer)
                layer.Dispose()
            Next
        End Sub

        Public Sub Toggle(id As String, visible As Boolean)
            Dim l = _layers.FirstOrDefault(Function(x) x.Id = id)
            If l IsNot Nothing Then l.IsVisible = visible
        End Sub

        Public Function Exists(id As String) As Boolean
            Return _layers.Any(Function(l) l.Id = id)
        End Function

        Public Function IsLayerVisible(id As String) As Boolean
            Dim l = _layers.FirstOrDefault(Function(x) x.Id = id)
            Return l IsNot Nothing AndAlso l.IsVisible
        End Function

        Public Function GetLayer(id As String) As IChartLayer
            Return _layers.FirstOrDefault(Function(x) x.Id = id)
        End Function

        Public Function Ordered() As IEnumerable(Of IChartLayer)
            Return _layers.Where(Function(l) l.IsVisible).OrderBy(Function(l) l.ZOrder)
        End Function

        Public Sub Dispose() Implements IDisposable.Dispose
            For Each layer In _layers
                layer.Dispose()
            Next
            _layers.Clear()
        End Sub
    End Class
End Namespace
