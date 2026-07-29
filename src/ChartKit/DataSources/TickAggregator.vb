Imports ChartKit.Models

Namespace DataSources

    '' 원본(base) 틱봉 리스트를 목표 틱주기로 재집계.
    '' - 입력은 시간 오름차순(과거->최신) 가정
    '' - 뒤에서부터 groupSize 개씩 묶음 (최신봉이 정확히 완성되도록)
    '' - 앞쪽 자투리(groupSize 미만)는 버림
    Public Module TickAggregator

        '' 키움이 원본으로 제공하는 틱봉 base 후보 (내림차순)
        Private ReadOnly BaseCandidates As Integer() = {30, 10, 5, 1}

        '' 목표 틱수 T 에 대해 무손실 재집계 가능한 최대 base 선택.
        '' (T 가 base 의 배수여야 무손실 -> 약수 중 최대)
        Public Function ChooseBase(targetTicks As Integer) As Integer
            For Each b In BaseCandidates
                If targetTicks Mod b = 0 Then Return b
            Next
            Return 1  '' 안전망 (항상 1 로 떨어짐)
        End Function

        '' base 틱봉(baseTicks 단위) 을 목표(targetTicks) 로 묶는다.
        '' groupSize = targetTicks / baseTicks
        Public Function Aggregate(baseCandles As List(Of CandleItem),
                                  targetTicks As Integer,
                                  baseTicks As Integer) As List(Of CandleItem)
            If baseCandles Is Nothing OrElse baseCandles.Count = 0 Then Return New List(Of CandleItem)
            If baseTicks <= 0 Then Return baseCandles
            Dim groupSize = targetTicks \ baseTicks
            If groupSize <= 1 Then Return baseCandles   '' 묶을 필요 없음 (base == target)

            Dim n = baseCandles.Count
            Dim outp As New List(Of CandleItem)
            '' 뒤에서부터 groupSize 개씩. 완전한 그룹만 생성.
            Dim endIdx = n - 1
            Dim tmp As New List(Of CandleItem)
            Do While endIdx - groupSize + 1 >= 0
                Dim startIdx = endIdx - groupSize + 1
                tmp.Add(BuildBar(baseCandles, startIdx, endIdx))
                endIdx -= groupSize
            Loop
            '' tmp 는 최신->과거 순으로 쌓였으니 뒤집어 과거->최신 반환
            tmp.Reverse()
            Return tmp
        End Function

        '' [startIdx..endIdx] base 봉들을 하나의 봉으로 합침
        Private Function BuildBar(src As List(Of CandleItem), startIdx As Integer, endIdx As Integer) As CandleItem
            Dim o = src(startIdx).Open
            Dim c = src(endIdx).Close
            Dim hi = src(startIdx).High
            Dim lo = src(startIdx).Low
            Dim vol As Long = 0
            For i = startIdx To endIdx
                If src(i).High > hi Then hi = src(i).High
                If src(i).Low < lo Then lo = src(i).Low
                vol += src(i).Volume
            Next
            '' 봉의 시각은 그룹의 마지막(최신) 틱 시각 사용 (완성 시점 기준)
            Return New CandleItem With {
                .Dt = src(endIdx).Dt,
                .Open = o, .High = hi, .Low = lo, .Close = c, .Volume = vol}
        End Function

    End Module

End Namespace