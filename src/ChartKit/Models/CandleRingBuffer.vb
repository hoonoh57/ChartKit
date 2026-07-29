Imports System.Collections
Imports System.Collections.Generic

Namespace Models
    '' 고정 용량 캔들 저장소.
    '' - Add/마지막 교체/오래된 항목 제거가 모두 O(1)
    '' - 내부 배열은 생성 후 절대 resize 하지 않음
    '' - 열거자는 생성 시 head/count를 캡처하므로 구조 변경 예외를 발생시키지 않음
    Public NotInheritable Class CandleRingBuffer
        Implements IReadOnlyList(Of CandleItem)

        Private ReadOnly _items() As CandleItem
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

        Public ReadOnly Property Count As Integer Implements IReadOnlyCollection(Of CandleItem).Count
            Get
                Return _count
            End Get
        End Property

        Default Public Property Item(index As Integer) As CandleItem
            Get
                If index < 0 OrElse index >= _count Then Throw New ArgumentOutOfRangeException(NameOf(index))
                Return _items(PhysicalIndex(index))
            End Get
            Set(value As CandleItem)
                If index < 0 OrElse index >= _count Then Throw New ArgumentOutOfRangeException(NameOf(index))
                _items(PhysicalIndex(index)) = value
            End Set
        End Property

        Private ReadOnly Property ReadOnlyItem(index As Integer) As CandleItem Implements IReadOnlyList(Of CandleItem).Item
            Get
                Return Item(index)
            End Get
        End Property

        '' 꽉 찬 경우 가장 오래된 한 건을 덮어쓰고 True를 반환한다.
        Public Function Add(value As CandleItem) As Boolean
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

        Public Function FindIndex(predicate As Predicate(Of CandleItem)) As Integer
            If predicate Is Nothing Then Throw New ArgumentNullException(NameOf(predicate))
            For i = 0 To _count - 1
                If predicate(Item(i)) Then Return i
            Next
            Return -1
        End Function

        Private Function PhysicalIndex(logicalIndex As Integer) As Integer
            Return (_head + logicalIndex) Mod Capacity
        End Function

        Public Iterator Function GetEnumerator() As IEnumerator(Of CandleItem) Implements IEnumerable(Of CandleItem).GetEnumerator
            Dim snapshotHead = _head
            Dim snapshotCount = _count
            Dim snapshotItems = _items
            For i = 0 To snapshotCount - 1
                Yield snapshotItems((snapshotHead + i) Mod snapshotItems.Length)
            Next
        End Function

        Private Function GetNonGenericEnumerator() As IEnumerator Implements IEnumerable.GetEnumerator
            Return GetEnumerator()
        End Function
    End Class
End Namespace
