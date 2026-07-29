Imports System.Collections.Generic
Imports ChartKit.Models

Namespace Abstractions

    '' 봉 주기
    Public Enum CandleInterval
        '' 틱 (3000 오프셋, 목표 틱수 = 값 - 3000). 키움 원본틱봉(1/5/10/30) 을 base 로 재집계
        Tick1 = 3001
        Tick3 = 3003
        Tick5 = 3005
        Tick10 = 3010
        Tick30 = 3030
        Tick60 = 3060
        Tick120 = 3120
        Tick240 = 3240
        Tick360 = 3360
        Tick720 = 3720
        '' 분봉 (값 = 분 수). ka10080
        Min1 = 1
        Min3 = 3
        Min5 = 5
        Min15 = 15
        Min30 = 30
        Min60 = 60
        '' 일/주/월. ka10081/82/83
        Day = 1000
        Week = 1001
        Month = 1002
    End Enum

    '' 데이터 요청 파라미터 (차트는 이 구조를 몰라도 됨. 소스 선택 계층에서만 사용)
    Public Class CandleRequest
        Public Property Symbol As String = ""          '' 종목코드
        Public Property Interval As CandleInterval = CandleInterval.Min1
        Public Property Count As Integer = 120         '' 요청 봉 개수
        Public Property [From] As Date? = Nothing
        Public Property [To] As Date? = Nothing
    End Class

    '' 새 봉 실시간 수신 이벤트 인자
    Public Class CandleAppendedEventArgs
        Inherits EventArgs
        Public Property Candle As CandleItem
        Public Sub New(c As CandleItem)
            Me.Candle = c
        End Sub
    End Class

    '' 진행 중인 마지막 봉이 실시간 체결로 변경될 때 사용
    Public Class CandleUpdatedEventArgs
        Inherits EventArgs
        Public Property Candle As CandleItem
        Public Sub New(c As CandleItem)
            Me.Candle = c
        End Sub
    End Class

    '' 캔들 데이터 공급 계약. 구현체: Random / Kiwoom REST / Server32 / DB ...
    '' 차트는 이 인터페이스조차 직접 참조하지 않는다.
    '' (바깥 계층이 GetCandles 결과를 chart.LoadCandles 로 전달)
    Public Interface ICandleDataSource
        ReadOnly Property Name As String

        '' 요청에 맞는 과거 봉을 시간 오름차순으로 반환
        Function GetCandles(req As CandleRequest) As List(Of CandleItem)

        '' 실시간 스트리밍 시작/중지 (미지원 구현은 no-op)
        Sub StartRealtime(req As CandleRequest)
        Sub StopRealtime()

        '' 새 봉이 확정될 때 발생 (실시간 미지원이면 발생 안 함)
        Event CandleAppended As EventHandler(Of CandleAppendedEventArgs)
        '' 아직 확정되지 않은 마지막 봉의 OHLCV가 변경될 때 발생
        Event CandleUpdated As EventHandler(Of CandleUpdatedEventArgs)
    End Interface

End Namespace
