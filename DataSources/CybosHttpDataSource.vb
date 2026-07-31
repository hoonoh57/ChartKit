Imports System.Globalization
Imports System.Net.Http
Imports System.Text.Json
Imports ChartKit.Abstractions
Imports ChartKit.Models

Namespace DataSources

    '' server32가 제공하는 CYBOS StockChart HTTP API용 데이터 소스.
    '' CYBOS COM 호출의 동시성/호출 제한은 server32가 소유하며 이 클래스는 우회하지 않는다.
    Public NotInheritable Class CybosHttpDataSource
        Implements ICandleDataSource

        Private Shared ReadOnly Client As New HttpClient With {
            .Timeout = TimeSpan.FromMinutes(5)}
        Private ReadOnly _baseUrl As String

        Public Sub New(Optional baseUrl As String = Nothing)
            _baseUrl = If(String.IsNullOrWhiteSpace(baseUrl),
                          EnvConfig.Get("CYBOS_API_URL", "http://localhost:8082"),
                          baseUrl).TrimEnd("/"c)
        End Sub

        Public ReadOnly Property Name As String Implements ICandleDataSource.Name
            Get
                Return "CYBOS server32"
            End Get
        End Property

        Public Function GetCandles(req As CandleRequest) As List(Of CandleItem) _
            Implements ICandleDataSource.GetCandles

            If req Is Nothing Then Throw New ArgumentNullException(NameOf(req))
            If String.IsNullOrWhiteSpace(req.Symbol) Then
                Throw New ArgumentException("종목코드가 비어 있습니다.", NameOf(req))
            End If

            Dim raw As List(Of CandleItem)
            Select Case req.Interval
                Case CandleInterval.Day
                    raw = RequestDaily(req)
                Case CandleInterval.Week
                    raw = AggregateCalendar(RequestDaily(req), True)
                Case CandleInterval.Month
                    raw = AggregateCalendar(RequestDaily(req), False)
                Case Else
                    raw = RequestIntraday(req)
            End Select

            '' 틱봉은 동일 초에 여러 봉이 존재할 수 있으므로 Dt로 중복 제거하면 안 된다.
            raw = raw.OrderBy(Function(c) c.Dt).ToList()
            If req.Count > 0 AndAlso raw.Count > req.Count Then
                raw = raw.Skip(raw.Count - req.Count).ToList()
            End If
            Return raw
        End Function

        Private Function RequestDaily(req As CandleRequest) As List(Of CandleItem)
            Dim untilDate = If(req.To, Date.Today)
            Dim fromDate = If(req.From, untilDate.AddDays(-Math.Max(365, req.Count * 3)))
            Dim path = "/api/market/candles/daily?code=" & Uri.EscapeDataString(req.Symbol) &
                       "&date=" & untilDate.ToString("yyyyMMdd", CultureInfo.InvariantCulture) &
                       "&stopDate=" & fromDate.ToString("yyyyMMdd", CultureInfo.InvariantCulture)
            Return RequestRows(path, "일자")
        End Function

        Private Function RequestIntraday(req As CandleRequest) As List(Of CandleItem)
            Dim isTick = CInt(req.Interval) >= 3000
            Dim requestedUnit = If(isTick, CInt(req.Interval) - 3000, CInt(req.Interval))
            Dim unit = If(isTick AndAlso requestedUnit > 60, 60, requestedUnit)
            If unit <= 0 Then Throw New NotSupportedException($"지원하지 않는 주기: {req.Interval}")

            Dim untilDate = If(req.To, Date.Today)
            Dim fromDate As DateTime
            If req.From.HasValue Then
                fromDate = req.From.Value
            Else
                Dim estimatedTradingDays As Integer
                If isTick Then
                    estimatedTradingDays = Math.Max(10, CInt(Math.Ceiling(req.Count / 300.0R)) * 3)
                Else
                    estimatedTradingDays = Math.Max(10, CInt(Math.Ceiling(req.Count * unit / 390.0R)) * 3)
                End If
                fromDate = untilDate.AddDays(-estimatedTradingDays)
            End If

            Dim kind = If(isTick, "tick", "minute")
            Dim path = $"/api/market/candles/{kind}?code={Uri.EscapeDataString(req.Symbol)}" &
                       $"&tick={unit}&stopTime={fromDate:yyyyMMddHHmmss}"
            Dim rows = RequestRows(path, "체결시간")
            If isTick AndAlso requestedUnit > unit Then
                Return AggregateFixedCount(rows, requestedUnit \ unit)
            End If
            Return rows
        End Function

        Private Shared Function AggregateFixedCount(source As List(Of CandleItem),
                                                    groupSize As Integer) As List(Of CandleItem)
            If groupSize <= 1 Then Return source
            Dim output As New List(Of CandleItem)(CInt(Math.Ceiling(source.Count / CDbl(groupSize))))
            For startIndex = 0 To source.Count - 1 Step groupSize
                Dim take = Math.Min(groupSize, source.Count - startIndex)
                Dim first = source(startIndex)
                Dim last = source(startIndex + take - 1)
                Dim high = first.High
                Dim low = first.Low
                Dim volume As Long = 0
                For offset = 0 To take - 1
                    Dim candle = source(startIndex + offset)
                    high = Math.Max(high, candle.High)
                    low = Math.Min(low, candle.Low)
                    volume += candle.Volume
                Next
                output.Add(New CandleItem With {
                    .Dt = last.Dt, .Open = first.Open, .High = high, .Low = low,
                    .Close = last.Close, .Volume = volume})
            Next
            Return output
        End Function

        Private Function RequestRows(path As String, timeKey As String) As List(Of CandleItem)
            Dim json As String
            Try
                json = Client.GetStringAsync(_baseUrl & path).GetAwaiter().GetResult()
            Catch ex As Exception
                Throw New InvalidOperationException(
                    $"CYBOS server32 호출 실패: {_baseUrl}{path}{Environment.NewLine}{ex.Message}", ex)
            End Try

            Using doc = JsonDocument.Parse(json)
                Dim root = doc.RootElement
                If Not ReadBoolean(root, "Success", "success") Then
                    Throw New InvalidOperationException(
                        "CYBOS server32 오류: " & ReadString(root, "Message", "message"))
                End If

                Dim data As JsonElement
                If Not TryProperty(root, data, "Data", "data") OrElse
                   data.ValueKind <> JsonValueKind.Array Then
                    Throw New InvalidOperationException("CYBOS server32 응답에 Data 배열이 없습니다.")
                End If

                Dim output As New List(Of CandleItem)(data.GetArrayLength())
                For Each row In data.EnumerateArray()
                    Dim dt = ParseTimestamp(ReadString(row, timeKey))
                    Dim candle As New CandleItem With {
                        .Dt = dt,
                        .Open = CSng(ReadDouble(row, "시가")),
                        .High = CSng(ReadDouble(row, "고가")),
                        .Low = CSng(ReadDouble(row, "저가")),
                        .Close = CSng(ReadDouble(row, "현재가", "종가")),
                        .Volume = CLng(ReadDouble(row, "거래량"))}
                    If candle.Dt <> DateTime.MinValue AndAlso candle.Close > 0 Then output.Add(candle)
                Next
                Return output
            End Using
        End Function

        Private Shared Function AggregateCalendar(source As List(Of CandleItem),
                                                   weekly As Boolean) As List(Of CandleItem)
            Return source.GroupBy(
                Function(c)
                    If weekly Then
                        Return $"{ISOWeek.GetYear(c.Dt):0000}-{ISOWeek.GetWeekOfYear(c.Dt):00}"
                    End If
                    Return c.Dt.ToString("yyyy-MM", CultureInfo.InvariantCulture)
                End Function).
                Select(Function(g)
                           Dim ordered = g.OrderBy(Function(c) c.Dt).ToList()
                           Return New CandleItem With {
                               .Dt = ordered.Last().Dt,
                               .Open = ordered.First().Open,
                               .High = ordered.Max(Function(c) c.High),
                               .Low = ordered.Min(Function(c) c.Low),
                               .Close = ordered.Last().Close,
                               .Volume = ordered.Sum(Function(c) c.Volume)}
                       End Function).OrderBy(Function(c) c.Dt).ToList()
        End Function

        Private Shared Function ParseTimestamp(value As String) As DateTime
            For Each dateFormat In New String() {"yyyyMMddHHmmss", "yyyyMMdd"}
                Dim parsed As DateTime
                If DateTime.TryParseExact(value, dateFormat, CultureInfo.InvariantCulture,
                                          DateTimeStyles.None, parsed) Then Return parsed
            Next
            Return DateTime.MinValue
        End Function

        Private Shared Function ReadBoolean(element As JsonElement,
                                            ParamArray names() As String) As Boolean
            Dim value As JsonElement
            If Not TryProperty(element, value, names) Then Return False
            If value.ValueKind = JsonValueKind.True Then Return True
            If value.ValueKind = JsonValueKind.False Then Return False
            Dim parsed As Boolean
            Return Boolean.TryParse(value.ToString(), parsed) AndAlso parsed
        End Function

        Private Shared Function ReadString(element As JsonElement,
                                           ParamArray names() As String) As String
            Dim value As JsonElement
            If Not TryProperty(element, value, names) Then Return ""
            Return If(value.ValueKind = JsonValueKind.String, value.GetString(), value.ToString())
        End Function

        Private Shared Function ReadDouble(element As JsonElement,
                                           ParamArray names() As String) As Double
            Dim text = ReadString(element, names).Replace(",", "")
            Dim value As Double
            If Double.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, value) Then
                Return Math.Abs(value)
            End If
            Return 0
        End Function

        Private Shared Function TryProperty(element As JsonElement,
                                            ByRef value As JsonElement,
                                            ParamArray names() As String) As Boolean
            For Each propertyName In names
                If element.TryGetProperty(propertyName, value) Then Return True
            Next
            Return False
        End Function

        Public Sub StartRealtime(req As CandleRequest) Implements ICandleDataSource.StartRealtime
            '' server32 실시간 API는 별도 WebSocket 계약이므로 과거 봉 소스에서는 시작하지 않는다.
        End Sub

        Public Sub StopRealtime() Implements ICandleDataSource.StopRealtime
        End Sub

        Public Event CandleAppended As EventHandler(Of CandleAppendedEventArgs) _
            Implements ICandleDataSource.CandleAppended
        Public Event CandleUpdated As EventHandler(Of CandleUpdatedEventArgs) _
            Implements ICandleDataSource.CandleUpdated
    End Class
End Namespace
