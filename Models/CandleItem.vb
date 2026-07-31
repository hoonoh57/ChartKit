Option Strict On
Option Explicit On
Option Infer Off

Namespace Models
    '' 캔들(봉) 하나의 OHLCV 데이터와 시간 의미.
    '' Dt는 기존 코드 호환용 대표 시각이다.
    '' 신규 데이터 소스는 OpenTime/CloseTime/IsFinal을 명시하는 것이 원칙이다.
    Public Class CandleItem
        Public Property Dt As DateTime
        Public Property Sequence As Long = -1L
        Public Property OpenTime As DateTime = DateTime.MinValue
        Public Property CloseTime As DateTime = DateTime.MinValue
        Public Property IsFinal As Boolean = True

        Public Property Open As Single
        Public Property High As Single
        Public Property Low As Single
        Public Property Close As Single
        Public Property Volume As Long

        Public ReadOnly Property EffectiveOpenTime As DateTime
            Get
                If OpenTime <> DateTime.MinValue Then Return OpenTime
                Return Dt
            End Get
        End Property

        Public ReadOnly Property EffectiveCloseTime As DateTime
            Get
                If CloseTime <> DateTime.MinValue Then Return CloseTime
                Return Dt
            End Get
        End Property

        Public ReadOnly Property TradingDate As Date
            Get
                Dim effectiveTime As DateTime = EffectiveOpenTime
                If effectiveTime = DateTime.MinValue Then effectiveTime = EffectiveCloseTime
                Return effectiveTime.Date
            End Get
        End Property

        Public ReadOnly Property HasExplicitTimeRange As Boolean
            Get
                Return OpenTime <> DateTime.MinValue OrElse
                       CloseTime <> DateTime.MinValue
            End Get
        End Property

        Public ReadOnly Property IsBullish As Boolean
            Get
                Return Close >= Open
            End Get
        End Property

        Public Function Copy() As CandleItem
            Return New CandleItem With {
                .Dt = Dt,
                .Sequence = Sequence,
                .OpenTime = OpenTime,
                .CloseTime = CloseTime,
                .IsFinal = IsFinal,
                .Open = Open,
                .High = High,
                .Low = Low,
                .Close = Close,
                .Volume = Volume
            }
        End Function
    End Class
End Namespace