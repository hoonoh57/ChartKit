Imports System.Linq
Imports ChartKit.Abstractions

Namespace Core
    Public Class IndicatorCatalogEntry
        Public Property Key As String
        Public Property DisplayName As String
        Public Property IndicatorType As Type
        Public Property Args As Object()
    End Class

    Public NotInheritable Class IndicatorCatalog
        Private Shared ReadOnly _entries As New List(Of IndicatorCatalogEntry)

        Private Sub New()
        End Sub

        '' 지표 타입 + 생성자 인자를 등록. 델리게이트를 쓰지 않아 추론 문제 없음.
        Public Shared Sub Register(key As String, displayName As String, indicatorType As Type, ParamArray args As Object())
            If String.IsNullOrWhiteSpace(key) OrElse indicatorType Is Nothing Then Return
            Dim existing = _entries.FirstOrDefault(Function(e) e.Key = key)
            If existing IsNot Nothing Then
                existing.DisplayName = displayName
                existing.IndicatorType = indicatorType
                existing.Args = args
            Else
                _entries.Add(New IndicatorCatalogEntry With {
                    .Key = key, .DisplayName = displayName,
                    .IndicatorType = indicatorType, .Args = args})
            End If
        End Sub

        Public Shared Function All() As IReadOnlyList(Of IndicatorCatalogEntry)
            Return _entries
        End Function

        Public Shared Function Create(key As String) As IIndicator
            Dim e = _entries.FirstOrDefault(Function(x) x.Key = key)
            If e Is Nothing OrElse e.IndicatorType Is Nothing Then Return Nothing
            Dim obj = Activator.CreateInstance(e.IndicatorType, e.Args)
            Return TryCast(obj, IIndicator)
        End Function

        Public Shared ReadOnly Property Count As Integer
            Get
                Return _entries.Count
            End Get
        End Property
    End Class
End Namespace