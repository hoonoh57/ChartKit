Namespace Models
    '' 캔들(봉) 하나의 OHLCV 데이터
    Public Class CandleItem
        Public Property Dt As DateTime
        Public Property Open As Single
        Public Property High As Single
        Public Property Low As Single
        Public Property Close As Single
        Public Property Volume As Long

        Public ReadOnly Property IsBullish As Boolean
            Get
                Return Close >= Open
            End Get
        End Property
    End Class
End Namespace