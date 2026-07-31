Imports System.Collections
Imports ChartKit.Abstractions

Namespace Models
    '' 고정 용량 지표 결과 저장소. 선두 삭제와 배열 이동 없이 O(1)로 추가/교체한다.
    Public NotInheritable Class IndicatorResultRingBuffer
        Implements IReadOnlyList(Of IndicatorResult)

        Private ReadOnly _items() As IndicatorResult
        Private _head As Integer
        Private _count As Integer

        Public Sub New(capacity As Integer)
            If capacity <= 0 Then Throw New ArgumentOutOfRangeException(NameOf(capacity))
            ReDim _items(capacity - 1)
        End Sub

        Public ReadOnly Property Capacity As Integer
            Get
                Return _items.Length
            End Get
        End Property

        Public ReadOnly Property Count As Integer Implements IReadOnlyCollection(Of IndicatorResult).Count
            Get
                Return _count
            End Get
        End Property

        Default Public Property Item(index As Integer) As IndicatorResult
            Get
                If index < 0 OrElse index >= _count Then Throw New ArgumentOutOfRangeException(NameOf(index))
                Return _items(PhysicalIndex(index))
            End Get
            Set(value As IndicatorResult)
                If index < 0 OrElse index >= _count Then Throw New ArgumentOutOfRangeException(NameOf(index))
                _items(PhysicalIndex(index)) = value
            End Set
        End Property

        Private ReadOnly Property ReadOnlyItem(index As Integer) As IndicatorResult Implements IReadOnlyList(Of IndicatorResult).Item
            Get
                Return Item(index)
            End Get
        End Property

        Public Function Add(value As IndicatorResult) As Boolean
            If _count < Capacity Then
                _items(PhysicalIndex(_count)) = value
                _count += 1
                Return False
            End If
            _items(_head) = value
            _head = (_head + 1) Mod Capacity
            Return True
        End Function

        Public Sub Clear()
            Array.Clear(_items, 0, _items.Length)
            _head = 0
            _count = 0
        End Sub

        Private Function PhysicalIndex(logicalIndex As Integer) As Integer
            Return (_head + logicalIndex) Mod Capacity
        End Function

        Public Iterator Function GetEnumerator() As IEnumerator(Of IndicatorResult) Implements IEnumerable(Of IndicatorResult).GetEnumerator
            Dim snapshotHead = _head
            Dim snapshotCount = _count
            For i = 0 To snapshotCount - 1
                Yield _items((snapshotHead + i) Mod _items.Length)
            Next
        End Function

        Private Function GetNonGenericEnumerator() As IEnumerator Implements IEnumerable.GetEnumerator
            Return GetEnumerator()
        End Function
    End Class
End Namespace
