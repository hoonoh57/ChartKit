Imports System.Net.Http
Imports System.Net
Imports System.Net.WebSockets
Imports System.IO
Imports System.Text
Imports System.Text.Json
Imports System.Threading
Imports System.Globalization
Imports System.Threading.Tasks
Imports ChartKit.Abstractions
Imports ChartKit.Models

Namespace DataSources

    '' 키움 REST 직접 호출 데이터소스 (core.py KiwoomREST 규약 이식).
    '' 차트는 이 클래스를 모른다. 바깥 계층이 GetCandles 결과를 chart.LoadCandles 로 전달.
    Public Class KiwoomRestSource
        Implements ICandleDataSource

        Private Shared ReadOnly _http As New HttpClient()
        Private ReadOnly _sync As New Object()
        Private _token As String = Nothing
        Private _lastCallTicks As Long = 0
        Private _lastTicCount As Integer = 0
        Private _realtimeCts As CancellationTokenSource
        Private _realtimeSocket As ClientWebSocket
        Private _realtimeTask As Task
        Private _realtimeSymbol As String = ""
        Private _realtimeTargetTicks As Integer = 0
        Private _realtimeIntervalMinutes As Integer = 0
        Private _realtimeTickCount As Integer = 0
        Private _realtimeCandle As CandleItem
        '' 국내주식 조회 TR: 실서버 초당 5회, 모의서버는 TR별 초당 1회.
        Private Const RealMinIntervalMs As Integer = 220
        Private Const MockMinIntervalMs As Integer = 1100
        Private Const MaxRateLimitRetries As Integer = 4

        Public ReadOnly Property Name As String Implements ICandleDataSource.Name
            Get
                Return If(EnvConfig.IsMock, "키움 REST(모의)", "키움 REST(실)")
            End Get
        End Property

        Public Event CandleAppended As EventHandler(Of CandleAppendedEventArgs) Implements ICandleDataSource.CandleAppended
        Public Event CandleUpdated As EventHandler(Of CandleUpdatedEventArgs) Implements ICandleDataSource.CandleUpdated

        '' ── 토큰 발급 (POST /oauth2/token, JSON, 응답 token) ──
        Private Sub EnsureToken()
            If Not String.IsNullOrEmpty(_token) Then Return
            Dim ak = EnvConfig.AppKey, sk = EnvConfig.SecretKey
            If String.IsNullOrEmpty(ak) OrElse String.IsNullOrEmpty(sk) Then
                Throw New InvalidOperationException("키움 API 키 없음. .env 에 KIWOOM_APP_KEY / KIWOOM_SECRET_KEY (또는 REAL/MOCK) 설정 필요.")
            End If
            Dim url = EnvConfig.RestHost & "/oauth2/token"
            Dim body = JsonSerializer.Serialize(New Dictionary(Of String, String) From {
                {"grant_type", "client_credentials"}, {"appkey", ak}, {"secretkey", sk}})
            Using req As New HttpRequestMessage(HttpMethod.Post, url)
                req.Content = New StringContent(body, Encoding.UTF8, "application/json")
                Dim resp = _http.SendAsync(req).GetAwaiter().GetResult()
                resp.EnsureSuccessStatusCode()
                Dim json = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult()
                Using doc = JsonDocument.Parse(json)
                    Dim el As JsonElement
                    If doc.RootElement.TryGetProperty("token", el) Then
                        _token = el.GetString()
                    ElseIf doc.RootElement.TryGetProperty("access_token", el) Then
                        _token = el.GetString()
                    End If
                End Using
            End Using
            If String.IsNullOrEmpty(_token) Then Throw New InvalidOperationException("토큰 발급 실패")
        End Sub

        Private Sub Throttle()
            SyncLock _sync
                Dim minIntervalMs = If(EnvConfig.IsMock, MockMinIntervalMs, RealMinIntervalMs)
                Dim now = Environment.TickCount64
                Dim wait = minIntervalMs - CInt(now - _lastCallTicks)
                If wait > 0 Then Thread.Sleep(wait)
                _lastCallTicks = Environment.TickCount64
            End SyncLock
        End Sub

        '' ── 공통 호출 (api-id / cont-yn / next-key 헤더) ──
        '' 반환: (JsonDocument, contYn, nextKey)
        Private Function CallApi(path As String, apiId As String, body As String,
                              contYn As String, nextKey As String,
                              ByRef outCont As String, ByRef outNext As String) As JsonDocument
            EnsureToken()
            Dim url = EnvConfig.RestHost & path
            For attempt = 0 To MaxRateLimitRetries
                Throttle()
                Using req As New HttpRequestMessage(HttpMethod.Post, url)
                    req.Content = New StringContent(body, Encoding.UTF8, "application/json")
                    req.Headers.TryAddWithoutValidation("authorization", "Bearer " & _token)
                    req.Headers.TryAddWithoutValidation("api-id", apiId)
                    req.Headers.TryAddWithoutValidation("cont-yn", If(contYn, "N"))
                    req.Headers.TryAddWithoutValidation("next-key", If(nextKey, ""))
                    Using resp = _http.SendAsync(req).GetAwaiter().GetResult()
                        Dim json = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult()
                        If resp.StatusCode = HttpStatusCode.TooManyRequests AndAlso attempt < MaxRateLimitRetries Then
                            Dim retryMs = GetRetryDelayMs(resp, attempt)
                            System.Diagnostics.Debug.WriteLine(
                                $"[KIWOOM] 429 api-id={apiId}, retry={attempt + 1}/{MaxRateLimitRetries}, wait={retryMs}ms")
                            Thread.Sleep(retryMs)
                            Continue For
                        End If
                        If Not resp.IsSuccessStatusCode Then
                            Throw New HttpRequestException(
                                $"Kiwoom {apiId} failed: HTTP {CInt(resp.StatusCode)} {resp.ReasonPhrase}; body={json}",
                                Nothing, resp.StatusCode)
                        End If
                        outCont = HeaderOrDefault(resp, "cont-yn", "N")
                        outNext = HeaderOrDefault(resp, "next-key", "")
                        Return JsonDocument.Parse(json)
                    End Using
                End Using
            Next
            Throw New HttpRequestException($"Kiwoom {apiId} failed after rate-limit retries.")
        End Function

        Private Shared Function GetRetryDelayMs(resp As HttpResponseMessage, attempt As Integer) As Integer
            If resp.Headers.RetryAfter IsNot Nothing Then
                If resp.Headers.RetryAfter.Delta.HasValue Then
                    Return Math.Max(1100, CInt(Math.Ceiling(resp.Headers.RetryAfter.Delta.Value.TotalMilliseconds)))
                End If
                If resp.Headers.RetryAfter.Date.HasValue Then
                    Dim ms = (resp.Headers.RetryAfter.Date.Value - DateTimeOffset.Now).TotalMilliseconds
                    If ms > 0 Then Return Math.Max(1100, CInt(Math.Ceiling(ms)))
                End If
            End If
            Return 1100 * (attempt + 1)
        End Function

        Private Shared Function HeaderOrDefault(resp As HttpResponseMessage, key As String, dflt As String) As String
            Dim vals As IEnumerable(Of String) = Nothing
            If resp.Headers.TryGetValues(key, vals) Then
                For Each v In vals : Return v : Next
            End If
            Return dflt
        End Function

        '' ── 연속조회 (최대 pages, max_rows) ──
        Private Function Paged(path As String, apiId As String, body As String,
                               maxRows As Integer, Optional pages As Integer = 8) As List(Of JsonElement)
            Dim rows As New List(Of JsonElement)
            Dim cont = "N", nkey = ""
            For p = 0 To pages - 1
                Dim oc = "N", onk = ""
                Using doc = CallApi(path, apiId, body, cont, nkey, oc, onk)
                    If p = 0 AndAlso apiId = "ka10079" Then
                        _lastTicCount = ReadRootInt(doc.RootElement, "last_tic_cnt")
                    End If
                    Dim lst = FindList(doc.RootElement)
                    For Each e In lst
                        '' JsonElement 는 doc 수명에 묶이므로 Clone 필요
                        rows.Add(e.Clone())
                    Next
                End Using
                cont = oc : nkey = onk
                If rows.Count >= maxRows OrElse cont <> "Y" Then Exit For
            Next
            Return rows
        End Function

        Private Shared Function ReadRootInt(root As JsonElement, key As String) As Integer
            Dim el As JsonElement
            If root.ValueKind <> JsonValueKind.Object OrElse Not root.TryGetProperty(key, el) Then Return 0
            Dim value As Integer
            If el.ValueKind = JsonValueKind.Number AndAlso el.TryGetInt32(value) Then Return value
            If el.ValueKind = JsonValueKind.String AndAlso
               Integer.TryParse(el.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, value) Then Return value
            Return 0
        End Function

        '' 첫 번째 "객체들의 배열" 프로퍼티를 리스트로 반환 (core.py _find_list)
        Private Shared Function FindList(root As JsonElement) As List(Of JsonElement)
            Dim res As New List(Of JsonElement)
            If root.ValueKind <> JsonValueKind.Object Then Return res
            For Each prop In root.EnumerateObject()
                If prop.Name.StartsWith("_") Then Continue For
                If prop.Value.ValueKind = JsonValueKind.Array Then
                    Dim arr = prop.Value
                    If arr.GetArrayLength() > 0 AndAlso arr(0).ValueKind = JsonValueKind.Object Then
                        For Each e In arr.EnumerateArray() : res.Add(e) : Next
                        Return res
                    End If
                End If
            Next
            Return res
        End Function

        '' ka10001 주식기본정보에서 종목명을 조회한다.
        Public Function GetStockName(symbol As String) As String
            Dim code = If(String.IsNullOrWhiteSpace(symbol), EnvConfig.DefaultSymbol, symbol.Trim())
            Dim body = JsonSerializer.Serialize(New Dictionary(Of String, String) From {
                {"stk_cd", code}})
            Dim cont = "N", nextKey = ""
            Using doc = CallApi("/api/dostk/stkinfo", "ka10001", body, "N", "", cont, nextKey)
                Return JsonString(doc.RootElement, "stk_nm").Trim()
            End Using
        End Function

        '' ── ICandleDataSource.GetCandles ──
        Public Function GetCandles(req As CandleRequest) As List(Of CandleItem) Implements ICandleDataSource.GetCandles
            Dim code = req.Symbol
            If String.IsNullOrWhiteSpace(code) Then code = EnvConfig.DefaultSymbol
            Dim maxBars = Math.Max(1, req.Count)
            Dim adj = EnvConfig.AdjustPrice
            Dim path = "/api/dostk/chart"

            Dim rows As List(Of JsonElement)
            Select Case req.Interval
                Case CandleInterval.Day, CandleInterval.Week, CandleInterval.Month
                    Dim api = If(req.Interval = CandleInterval.Day, "ka10081",
                                 If(req.Interval = CandleInterval.Week, "ka10082", "ka10083"))
                    Dim body = JsonSerializer.Serialize(New Dictionary(Of String, String) From {
                        {"stk_cd", code}, {"base_dt", Date.Now.ToString("yyyyMMdd")}, {"upd_stkpc_tp", adj}})
                    rows = Paged(path, api, body, maxBars)
                    Return ParseRows(rows, dailyMode:=True)
                Case CandleInterval.Tick1, CandleInterval.Tick3, CandleInterval.Tick5, CandleInterval.Tick10, CandleInterval.Tick30,
                     CandleInterval.Tick60, CandleInterval.Tick120, CandleInterval.Tick240, CandleInterval.Tick360, CandleInterval.Tick720
                    '' 목표 틱수 = enum - 3000. base 를 골라 ka10079 로 받고 TickAggregator 로 집계.
                    Dim targetTicks = CInt(req.Interval) - 3000
                    Dim baseTicks = TickAggregator.ChooseBase(targetTicks)
                    Dim groupSize = targetTicks \ baseTicks
                    '' 목표봉 maxBars 개 만들려면 base 봉이 (maxBars * groupSize) + 여유 필요
                    Dim needBase = (maxBars * groupSize) + groupSize
                    Dim tbody = JsonSerializer.Serialize(New Dictionary(Of String, String) From {
                        {"stk_cd", code}, {"tic_scope", baseTicks.ToString(CultureInfo.InvariantCulture)}, {"upd_stkpc_tp", adj}})
                    _lastTicCount = 0
                    Dim baseRows = Paged(path, "ka10079", tbody, needBase, pages:=20)
                    Dim baseCandles = ParseRows(baseRows, dailyMode:=False)
                    System.Diagnostics.Debug.WriteLine($"[TICK] target={targetTicks} base={baseTicks} group={groupSize} needBase={needBase} gotBase={baseCandles.Count}")
                    Dim agg = TickAggregator.Aggregate(baseCandles, targetTicks, baseTicks)
                    System.Diagnostics.Debug.WriteLine($"[TICK] aggregated bars={agg.Count}")
                    '' 최근 maxBars 개만
                    If agg.Count > maxBars Then agg = agg.GetRange(agg.Count - maxBars, maxBars)
                    SyncLock _sync
                        _realtimeTargetTicks = targetTicks
                        _realtimeIntervalMinutes = 0
                        _realtimeTickCount = Math.Min(targetTicks,
                            Math.Max(0, (groupSize - 1) * baseTicks + Math.Min(baseTicks, _lastTicCount)))
                        _realtimeCandle = If(agg.Count > 0, CloneCandle(agg(agg.Count - 1)), Nothing)
                    End SyncLock
                    Return agg
                Case Else
                    Dim tic = CInt(req.Interval).ToString(CultureInfo.InvariantCulture)  '' 분 수
                    Dim body = JsonSerializer.Serialize(New Dictionary(Of String, String) From {
                        {"stk_cd", code}, {"tic_scope", tic}, {"upd_stkpc_tp", adj}})
                    rows = Paged(path, "ka10080", body, maxBars)
                    Dim minuteCandles = ParseRows(rows, dailyMode:=False)
                    SyncLock _sync
                        _realtimeTargetTicks = 0
                        _realtimeIntervalMinutes = CInt(req.Interval)
                        _realtimeTickCount = 0
                        _realtimeCandle = If(minuteCandles.Count > 0,
                            CloneCandle(minuteCandles(minuteCandles.Count - 1)), Nothing)
                    End SyncLock
                    Return minuteCandles
            End Select
        End Function

        '' 응답 rows(최신->과거) 를 뒤집어 시간 오름차순 CandleItem 으로 변환
        Private Function ParseRows(rows As List(Of JsonElement), dailyMode As Boolean) As List(Of CandleItem)
            rows.Reverse()
            Dim outp As New List(Of CandleItem)
            For Each row In rows
                Dim c = Num(row, "cur_prc")
                If Not c.HasValue Then Continue For
                Dim o = Num(row, "open_pric")
                Dim hi = Num(row, "high_pric")
                Dim lo = Num(row, "low_pric")
                Dim v = Num(row, "trde_qty")
                Dim tRaw = If(dailyMode, Str(row, "dt", "stck_bsop_date"), Str(row, "cntr_tm", "dt"))
                If outp.Count < 3 Then
                    System.Diagnostics.Debug.WriteLine("[KIWOOM] rawTime=""" & tRaw & """  keys=" & DumpKeys(row))
                End If
                Dim dt = ParseKiwoomTime(tRaw, dailyMode)
                outp.Add(New CandleItem With {
                    .Dt = dt,
                    .Open = CSng(Math.Abs(If(o, c.Value))),
                    .High = CSng(Math.Abs(If(hi, c.Value))),
                    .Low = CSng(Math.Abs(If(lo, c.Value))),
                    .Close = CSng(Math.Abs(c.Value)),
                    .Volume = CLng(Math.Abs(If(v, 0.0)))})
            Next
            Return outp
        End Function

        Private Shared Function ParseKiwoomTime(s As String, dailyMode As Boolean) As Date
            If String.IsNullOrEmpty(s) Then Return Date.Now
            Dim dt As Date
            If dailyMode Then
                If Date.TryParseExact(s.Trim(), "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, dt) Then Return dt
            Else
                Dim t = s.Trim()
                For Each fmt In {"yyyyMMddHHmmss", "yyyyMMddHHmm", "HHmmss", "HHmm"}
                    If Date.TryParseExact(t, fmt, CultureInfo.InvariantCulture, DateTimeStyles.None, dt) Then Return dt
                Next
            End If
            Return Date.Now
        End Function

        '' core.py _n : ",","+"제거 후 float, 실패시 Nothing
        Private Shared Function Num(row As JsonElement, key As String) As Double?
            Dim el As JsonElement
            If Not row.TryGetProperty(key, el) Then Return Nothing
            Dim raw As String
            Select Case el.ValueKind
                Case JsonValueKind.String : raw = el.GetString()
                Case JsonValueKind.Number : Return el.GetDouble()
                Case Else : Return Nothing
            End Select
            If String.IsNullOrEmpty(raw) Then Return Nothing
            raw = raw.Replace(",", "").Replace("+", "").Trim()
            Dim d As Double
            If Double.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, d) Then Return d
            Return Nothing
        End Function

        '' 진단용: row 의 프로퍼티 키 나열
        Private Shared Function DumpKeys(row As JsonElement) As String
            Dim sb As New System.Text.StringBuilder()
            For Each pr In row.EnumerateObject()
                sb.Append(pr.Name).Append(",")
            Next
            Return sb.ToString()
        End Function

        Private Shared Function Str(row As JsonElement, ParamArray keys() As String) As String
            For Each k In keys
                Dim el As JsonElement
                If row.TryGetProperty(k, el) AndAlso el.ValueKind = JsonValueKind.String Then
                    Dim v = el.GetString()
                    If Not String.IsNullOrEmpty(v) Then Return v
                End If
            Next
            Return ""
        End Function

        Public Sub StartRealtime(req As CandleRequest) Implements ICandleDataSource.StartRealtime
            StopRealtime()
            If req Is Nothing Then Return
            Dim intervalValue = CInt(req.Interval)
            Dim isTickInterval = intervalValue > 3000 AndAlso intervalValue < 4000
            Dim isMinuteInterval = intervalValue >= 1 AndAlso intervalValue <= 60
            If Not isTickInterval AndAlso Not isMinuteInterval Then Return

            EnsureToken()
            _realtimeSymbol = If(String.IsNullOrWhiteSpace(req.Symbol), EnvConfig.DefaultSymbol, req.Symbol.Trim())
            If isTickInterval Then
                _realtimeTargetTicks = intervalValue - 3000
                _realtimeIntervalMinutes = 0
            Else
                _realtimeTargetTicks = 0
                _realtimeIntervalMinutes = intervalValue
            End If
            _realtimeCts = New CancellationTokenSource()
            Dim token = _realtimeCts.Token
            _realtimeTask = Task.Run(Async Function()
                                         Await RealtimeReconnectLoopAsync(token)
                                     End Function, token)
        End Sub

        Public Sub StopRealtime() Implements ICandleDataSource.StopRealtime
            Dim cts = _realtimeCts
            _realtimeCts = Nothing
            If cts IsNot Nothing Then
                Try
                    cts.Cancel()
                Catch
                End Try
                cts.Dispose()
            End If

            Dim socket = _realtimeSocket
            _realtimeSocket = Nothing
            If socket IsNot Nothing Then
                Try
                    socket.Abort()
                    socket.Dispose()
                Catch
                End Try
            End If
        End Sub

        Private Async Function RealtimeReconnectLoopAsync(cancel As CancellationToken) As Task
            While Not cancel.IsCancellationRequested
                Try
                    Await RunRealtimeSessionAsync(cancel)
                Catch ex As OperationCanceledException When cancel.IsCancellationRequested
                    Exit While
                Catch ex As Exception
                    System.Diagnostics.Debug.WriteLine("[KIWOOM-WS] " & ex.Message)
                Finally
                    Dim staleSocket = _realtimeSocket
                    _realtimeSocket = Nothing
                    If staleSocket IsNot Nothing Then
                        Try
                            staleSocket.Dispose()
                        Catch
                        End Try
                    End If
                End Try
                If Not cancel.IsCancellationRequested Then
                    Await Task.Delay(2000, cancel)
                End If
            End While
        End Function

        Private Async Function RunRealtimeSessionAsync(cancel As CancellationToken) As Task
            Dim ws As New ClientWebSocket()
            _realtimeSocket = ws
            Dim host = If(EnvConfig.IsMock, "mockapi.kiwoom.com", "api.kiwoom.com")
            Dim uri As New Uri($"wss://{host}:10000/api/dostk/websocket")
            Await ws.ConnectAsync(uri, cancel)

            Await SendWebSocketJsonAsync(ws, New Dictionary(Of String, Object) From {
                {"trnm", "LOGIN"}, {"token", _token}}, cancel)

            While ws.State = WebSocketState.Open AndAlso Not cancel.IsCancellationRequested
                Dim json = Await ReceiveWebSocketTextAsync(ws, cancel)
                If String.IsNullOrEmpty(json) Then Exit While
                Using doc = JsonDocument.Parse(json)
                    Dim root = doc.RootElement
                    Dim trnm = JsonString(root, "trnm")
                    Select Case trnm
                        Case "LOGIN"
                            Dim resultCode = ReadRootInt(root, "return_code")
                            If resultCode <> 0 Then
                                Throw New InvalidOperationException(
                                    "키움 WebSocket 로그인 실패: " & JsonString(root, "return_msg"))
                            End If
                            Await SendRegisterAsync(ws, cancel)
                        Case "PING"
                            '' 서버가 보낸 PING JSON을 그대로 돌려보내야 세션이 유지된다.
                            Await SendWebSocketTextAsync(ws, json, cancel)
                        Case "REAL"
                            ProcessRealtimeMessage(root)
                        Case "REG"
                            If ReadRootInt(root, "return_code") <> 0 Then
                                Throw New InvalidOperationException(
                                    "키움 WebSocket 실시간 등록 실패: " & JsonString(root, "return_msg"))
                            End If
                    End Select
                End Using
            End While
        End Function

        Private Async Function SendRegisterAsync(ws As ClientWebSocket, cancel As CancellationToken) As Task
            Dim registration = New Dictionary(Of String, Object) From {
                {"trnm", "REG"},
                {"grp_no", "1"},
                {"refresh", "0"},
                {"data", New Object() {
                    New Dictionary(Of String, Object) From {
                        {"item", New String() {_realtimeSymbol}},
                        {"type", New String() {"0B"}}
                    }}}}
            Await SendWebSocketJsonAsync(ws, registration, cancel)
        End Function

        Private Sub ProcessRealtimeMessage(root As JsonElement)
            Dim data As JsonElement
            If Not root.TryGetProperty("data", data) OrElse data.ValueKind <> JsonValueKind.Array Then Return
            For Each entry In data.EnumerateArray()
                If JsonString(entry, "type") <> "0B" Then Continue For
                Dim item = JsonString(entry, "item")
                If item <> _realtimeSymbol Then Continue For
                Dim values As JsonElement
                If Not entry.TryGetProperty("values", values) OrElse values.ValueKind <> JsonValueKind.Object Then Continue For

                Dim price = RealtimeNumber(values, "10")
                If Not price.HasValue Then Continue For
                Dim quantity = RealtimeNumber(values, "15")
                Dim tradeTime = ParseRealtimeTime(JsonString(values, "20"))
                ApplyRealtimeTick(tradeTime, CSng(Math.Abs(price.Value)),
                                  CLng(Math.Abs(If(quantity, 0.0))))
            Next
        End Sub

        Private Sub ApplyRealtimeTick(tradeTime As DateTime, price As Single, quantity As Long)
            If _realtimeIntervalMinutes > 0 Then
                ApplyRealtimeMinute(tradeTime, price, quantity)
                Return
            End If
            Dim changed As CandleItem = Nothing
            Dim appended As Boolean = False
            SyncLock _sync
                If _realtimeCandle Is Nothing OrElse
                   _realtimeTickCount >= _realtimeTargetTicks OrElse
                   _realtimeCandle.Dt.Date <> tradeTime.Date Then
                    _realtimeCandle = New CandleItem With {
                        .Dt = tradeTime, .Open = price, .High = price, .Low = price,
                        .Close = price, .Volume = quantity}
                    _realtimeTickCount = 1
                    appended = True
                Else
                    _realtimeCandle.Dt = tradeTime
                    If price > _realtimeCandle.High Then _realtimeCandle.High = price
                    If price < _realtimeCandle.Low Then _realtimeCandle.Low = price
                    _realtimeCandle.Close = price
                    _realtimeCandle.Volume += quantity
                    _realtimeTickCount += 1
                End If
                changed = CloneCandle(_realtimeCandle)
            End SyncLock

            If appended Then
                RaiseEvent CandleAppended(Me, New CandleAppendedEventArgs(changed))
            Else
                RaiseEvent CandleUpdated(Me, New CandleUpdatedEventArgs(changed))
            End If
        End Sub

        Private Sub ApplyRealtimeMinute(tradeTime As DateTime, price As Single, quantity As Long)
            Dim interval = Math.Max(1, _realtimeIntervalMinutes)
            Dim totalMinutes = tradeTime.Hour * 60 + tradeTime.Minute
            Dim bucketMinutes = (totalMinutes \ interval) * interval
            Dim bucketTime = tradeTime.Date.AddMinutes(bucketMinutes)
            Dim changed As CandleItem = Nothing
            Dim appended As Boolean = False

            SyncLock _sync
                If _realtimeCandle Is Nothing OrElse _realtimeCandle.Dt < bucketTime Then
                    _realtimeCandle = New CandleItem With {
                        .Dt = bucketTime, .Open = price, .High = price, .Low = price,
                        .Close = price, .Volume = quantity}
                    appended = True
                ElseIf _realtimeCandle.Dt = bucketTime Then
                    If price > _realtimeCandle.High Then _realtimeCandle.High = price
                    If price < _realtimeCandle.Low Then _realtimeCandle.Low = price
                    _realtimeCandle.Close = price
                    _realtimeCandle.Volume += quantity
                Else
                    '' 지연 도착한 이전 시간 버킷 체결은 현재 봉을 역행시키지 않는다.
                    Return
                End If
                changed = CloneCandle(_realtimeCandle)
            End SyncLock

            If appended Then
                RaiseEvent CandleAppended(Me, New CandleAppendedEventArgs(changed))
            Else
                RaiseEvent CandleUpdated(Me, New CandleUpdatedEventArgs(changed))
            End If
        End Sub

        Private Shared Function ParseRealtimeTime(raw As String) As DateTime
            If String.IsNullOrWhiteSpace(raw) Then Return DateTime.Now
            Dim parsed As DateTime
            Dim text = raw.Trim()
            If DateTime.TryParseExact(text, "yyyyMMddHHmmss", CultureInfo.InvariantCulture,
                                      DateTimeStyles.None, parsed) Then Return parsed
            If DateTime.TryParseExact(text, "HHmmss", CultureInfo.InvariantCulture,
                                      DateTimeStyles.None, parsed) Then
                Return DateTime.Today.Add(parsed.TimeOfDay)
            End If
            Return DateTime.Now
        End Function

        Private Shared Function RealtimeNumber(values As JsonElement, key As String) As Double?
            Dim el As JsonElement
            If Not values.TryGetProperty(key, el) Then Return Nothing
            Dim raw = If(el.ValueKind = JsonValueKind.String, el.GetString(), el.ToString())
            If String.IsNullOrWhiteSpace(raw) Then Return Nothing
            raw = raw.Replace(",", "").Replace("+", "").Trim()
            Dim value As Double
            If Double.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, value) Then Return value
            Return Nothing
        End Function

        Private Shared Function JsonString(root As JsonElement, key As String) As String
            Dim el As JsonElement
            If root.ValueKind = JsonValueKind.Object AndAlso root.TryGetProperty(key, el) Then
                If el.ValueKind = JsonValueKind.String Then Return If(el.GetString(), "")
                Return el.ToString()
            End If
            Return ""
        End Function

        Private Shared Function CloneCandle(source As CandleItem) As CandleItem
            If source Is Nothing Then Return Nothing
            Return New CandleItem With {
                .Dt = source.Dt, .Open = source.Open, .High = source.High,
                .Low = source.Low, .Close = source.Close, .Volume = source.Volume}
        End Function

        Private Shared Async Function SendWebSocketJsonAsync(ws As ClientWebSocket, payload As Object,
                                                              cancel As CancellationToken) As Task
            Await SendWebSocketTextAsync(ws, JsonSerializer.Serialize(payload), cancel)
        End Function

        Private Shared Async Function SendWebSocketTextAsync(ws As ClientWebSocket, text As String,
                                                              cancel As CancellationToken) As Task
            Dim bytes = Encoding.UTF8.GetBytes(text)
            Await ws.SendAsync(New ArraySegment(Of Byte)(bytes), WebSocketMessageType.Text, True, cancel)
        End Function

        Private Shared Async Function ReceiveWebSocketTextAsync(ws As ClientWebSocket,
                                                                 cancel As CancellationToken) As Task(Of String)
            Dim buffer(8191) As Byte
            Using stream As New MemoryStream()
                Do
                    Dim result = Await ws.ReceiveAsync(New ArraySegment(Of Byte)(buffer), cancel)
                    If result.MessageType = WebSocketMessageType.Close Then Return Nothing
                    If result.MessageType <> WebSocketMessageType.Text Then Continue Do
                    stream.Write(buffer, 0, result.Count)
                    If result.EndOfMessage Then Exit Do
                Loop
                Return Encoding.UTF8.GetString(stream.ToArray())
            End Using
        End Function

    End Class

End Namespace
