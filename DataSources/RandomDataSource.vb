Imports System.Collections.Generic
Imports ChartKit.Abstractions
Imports ChartKit.Models

Namespace DataSources

    '' 테스트용 난수 데이터소스 (기존 DemoProgram 로직 이식)
    Public Class RandomDataSource
        Implements ICandleDataSource

        Private ReadOnly _seed As Integer
        Public Sub New(Optional seed As Integer = 42)
            _seed = seed
        End Sub

        Public ReadOnly Property Name As String Implements ICandleDataSource.Name
            Get
                Return "난수(테스트)"
            End Get
        End Property

        Public Function GetCandles(req As CandleRequest) As List(Of CandleItem) Implements ICandleDataSource.GetCandles
            Dim rnd As New Random(_seed)
            Dim list As New List(Of CandleItem)
            Dim price As Single = 10000
            Dim baseTime = New DateTime(2026, 7, 28, 9, 0, 0)
            Dim n = If(req IsNot Nothing AndAlso req.Count > 0, req.Count, 120)
            For i As Integer = 0 To n - 1
                Dim o = price
                Dim ch = CSng((rnd.NextDouble() - 0.5) * 400)
                Dim cl = Math.Max(1000, o + ch)
                Dim hi = Math.Max(o, cl) + CSng(rnd.NextDouble() * 150)
                Dim lo = Math.Min(o, cl) - CSng(rnd.NextDouble() * 150)
                list.Add(New CandleItem With {
                    .Dt = baseTime.AddMinutes(i), .Open = o, .High = hi,
                    .Low = lo, .Close = cl, .Volume = CLng(rnd.Next(1000, 9000))})
                price = cl
            Next
            Return list
        End Function

        Public Sub StartRealtime(req As CandleRequest) Implements ICandleDataSource.StartRealtime
            '' 테스트 소스는 실시간 미지원 (no-op)
        End Sub

        Public Sub StopRealtime() Implements ICandleDataSource.StopRealtime
        End Sub

        Public Event CandleAppended As EventHandler(Of CandleAppendedEventArgs) Implements ICandleDataSource.CandleAppended
        Public Event CandleUpdated As EventHandler(Of CandleUpdatedEventArgs) Implements ICandleDataSource.CandleUpdated
    End Class

End Namespace
