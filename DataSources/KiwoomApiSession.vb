Option Strict On
Option Explicit On
Option Infer Off

Imports System.Globalization
Imports System.Net
Imports System.Net.Http
Imports System.Text
Imports System.Text.Json
Imports System.Threading
Imports ChartKit.Core

Namespace DataSources
    Public Interface IKiwoomClock
        ReadOnly Property UtcNow As DateTimeOffset
        ReadOnly Property TickCount64 As Long
        Sub Sleep(milliseconds As Integer)
    End Interface

    Public NotInheritable Class SystemKiwoomClock
        Implements IKiwoomClock

        Public ReadOnly Property UtcNow As DateTimeOffset Implements IKiwoomClock.UtcNow
            Get
                Return DateTimeOffset.UtcNow
            End Get
        End Property

        Public ReadOnly Property TickCount64 As Long Implements IKiwoomClock.TickCount64
            Get
                Return Environment.TickCount64
            End Get
        End Property

        Public Sub Sleep(milliseconds As Integer) Implements IKiwoomClock.Sleep
            If milliseconds > 0 Then Thread.Sleep(milliseconds)
        End Sub
    End Class

    Public NotInheritable Class KiwoomApiSessionOptions
        Public Property RestHost As String = ""
        Public Property AppKey As String = ""
        Public Property SecretKey As String = ""
        Public Property IsMock As Boolean
        Public Property MinRequestIntervalMs As Integer
        Public Property MaxRateLimitRetries As Integer = 4
        Public Property TokenRefreshSkew As TimeSpan = TimeSpan.FromMinutes(5)
        Public Property TokenFallbackLifetime As TimeSpan = TimeSpan.FromHours(23)

        Public Sub Validate()
            If String.IsNullOrWhiteSpace(RestHost) Then
                Throw New ArgumentException("Kiwoom REST host가 비어 있습니다.", NameOf(RestHost))
            End If
            If String.IsNullOrWhiteSpace(AppKey) OrElse
               String.IsNullOrWhiteSpace(SecretKey) Then
                Throw New InvalidOperationException(
                    "키움 API 키 없음. .env 에 KIWOOM_APP_KEY / KIWOOM_SECRET_KEY (또는 REAL/MOCK) 설정 필요.")
            End If
            If MinRequestIntervalMs < 0 Then
                Throw New ArgumentOutOfRangeException(NameOf(MinRequestIntervalMs))
            End If
            If MaxRateLimitRetries < 0 Then
                Throw New ArgumentOutOfRangeException(NameOf(MaxRateLimitRetries))
            End If
            If TokenRefreshSkew < TimeSpan.Zero Then
                Throw New ArgumentOutOfRangeException(NameOf(TokenRefreshSkew))
            End If
            If TokenFallbackLifetime <= TimeSpan.Zero Then
                Throw New ArgumentOutOfRangeException(NameOf(TokenFallbackLifetime))
            End If
        End Sub
    End Class

    Public NotInheritable Class KiwoomApiSession
        Implements IDisposable

        Private ReadOnly _options As KiwoomApiSessionOptions
        Private ReadOnly _http As HttpClient
        Private ReadOnly _clock As IKiwoomClock
        Private ReadOnly _tokenSync As New Object()
        Private ReadOnly _rateSync As New Object()

        Private _token As String = ""
        Private _tokenExpiresUtc As DateTimeOffset = DateTimeOffset.MinValue
        Private _lastCallTicks As Long
        Private _blockedUntilTicks As Long
        Private _disposed As Boolean

        Public Sub New(options As KiwoomApiSessionOptions,
                       Optional handler As HttpMessageHandler = Nothing,
                       Optional clock As IKiwoomClock = Nothing)
            If options Is Nothing Then Throw New ArgumentNullException(NameOf(options))
            options.Validate()

            _options = options
            _clock = If(clock, New SystemKiwoomClock())
            _http = If(handler Is Nothing,
                       New HttpClient(),
                       New HttpClient(handler, disposeHandler:=True))
            _lastCallTicks = -CLng(_options.MinRequestIntervalMs)
        End Sub

        Public Function GetAccessToken() As String
            ThrowIfDisposed()

            SyncLock _tokenSync
                If IsTokenUsable() Then Return _token

                Dim issuedAt As DateTimeOffset = _clock.UtcNow
                Dim url As String = _options.RestHost.TrimEnd("/"c) & "/oauth2/token"
                Dim body As String = JsonSerializer.Serialize(
                    New Dictionary(Of String, String) From {
                        {"grant_type", "client_credentials"},
                        {"appkey", _options.AppKey},
                        {"secretkey", _options.SecretKey}})

                Using request As New HttpRequestMessage(HttpMethod.Post, url)
                    request.Content = New StringContent(body, Encoding.UTF8, "application/json")
                    Using response As HttpResponseMessage =
                        _http.SendAsync(request).GetAwaiter().GetResult()
                        Dim json As String =
                            response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
                        If Not response.IsSuccessStatusCode Then
                            Throw New HttpRequestException(
                                $"Kiwoom token request failed: HTTP {CInt(response.StatusCode)} {response.ReasonPhrase}; body={json}",
                                Nothing,
                                response.StatusCode)
                        End If

                        Using document As JsonDocument = JsonDocument.Parse(json)
                            Dim root As JsonElement = document.RootElement
                            _token = ReadToken(root)
                            _tokenExpiresUtc = ResolveTokenExpiry(root, issuedAt)
                        End Using
                    End Using
                End Using

                If String.IsNullOrWhiteSpace(_token) Then
                    _tokenExpiresUtc = DateTimeOffset.MinValue
                    Throw New InvalidOperationException("토큰 발급 실패")
                End If

                Return _token
            End SyncLock
        End Function

        Public Sub InvalidateToken(Optional expectedToken As String = Nothing)
            SyncLock _tokenSync
                If Not String.IsNullOrEmpty(expectedToken) AndAlso
                   Not String.Equals(_token, expectedToken, StringComparison.Ordinal) Then
                    Return
                End If
                _token = ""
                _tokenExpiresUtc = DateTimeOffset.MinValue
            End SyncLock
        End Sub

        Public Function PostJson(path As String,
                                 apiId As String,
                                 body As String,
                                 contYn As String,
                                 nextKey As String,
                                 ByRef outCont As String,
                                 ByRef outNext As String) As JsonDocument
            ThrowIfDisposed()

            Dim url As String = _options.RestHost.TrimEnd("/"c) & path
            Dim rateRetryCount As Integer = 0
            Dim authenticationRetried As Boolean = False

            Do
                Dim accessToken As String = GetAccessToken()
                WaitForRequestTurn()

                Using request As New HttpRequestMessage(HttpMethod.Post, url)
                    request.Content = New StringContent(body, Encoding.UTF8, "application/json")
                    request.Headers.TryAddWithoutValidation("authorization", "Bearer " & accessToken)
                    request.Headers.TryAddWithoutValidation("api-id", apiId)
                    request.Headers.TryAddWithoutValidation("cont-yn", If(contYn, "N"))
                    request.Headers.TryAddWithoutValidation("next-key", If(nextKey, ""))

                    Using response As HttpResponseMessage =
                        _http.SendAsync(request).GetAwaiter().GetResult()
                        Dim json As String =
                            response.Content.ReadAsStringAsync().GetAwaiter().GetResult()

                        If (response.StatusCode = HttpStatusCode.Unauthorized OrElse
                            response.StatusCode = HttpStatusCode.Forbidden) AndAlso
                           Not authenticationRetried Then
                            authenticationRetried = True
                            InvalidateToken(accessToken)
                            Continue Do
                        End If

                        If response.StatusCode = HttpStatusCode.TooManyRequests AndAlso
                           rateRetryCount < _options.MaxRateLimitRetries Then
                            Dim retryMs As Integer = GetRetryDelayMs(response, rateRetryCount)
                            rateRetryCount += 1
                            RegisterGlobalBackoff(retryMs)
                            ChartLog.Info(
                                $"[KIWOOM] 429 api-id={apiId}, retry={rateRetryCount}/{_options.MaxRateLimitRetries}, wait={retryMs}ms")
                            Continue Do
                        End If

                        If Not response.IsSuccessStatusCode Then
                            Throw New HttpRequestException(
                                $"Kiwoom {apiId} failed: HTTP {CInt(response.StatusCode)} {response.ReasonPhrase}; body={json}",
                                Nothing,
                                response.StatusCode)
                        End If

                        outCont = HeaderOrDefault(response, "cont-yn", "N")
                        outNext = HeaderOrDefault(response, "next-key", "")
                        Return JsonDocument.Parse(json)
                    End Using
                End Using
            Loop
        End Function

        Private Function IsTokenUsable() As Boolean
            If String.IsNullOrWhiteSpace(_token) Then Return False
            Return _clock.UtcNow < (_tokenExpiresUtc - _options.TokenRefreshSkew)
        End Function

        Private Sub WaitForRequestTurn()
            SyncLock _rateSync
                Do
                    Dim nowTicks As Long = _clock.TickCount64
                    Dim intervalReady As Long =
                        _lastCallTicks + CLng(_options.MinRequestIntervalMs)
                    Dim readyAt As Long = Math.Max(intervalReady, _blockedUntilTicks)
                    Dim waitTicks As Long = readyAt - nowTicks
                    If waitTicks <= 0L Then Exit Do

                    Dim waitMs As Integer = CInt(Math.Min(CLng(Integer.MaxValue), waitTicks))
                    _clock.Sleep(waitMs)
                Loop

                _lastCallTicks = _clock.TickCount64
            End SyncLock
        End Sub

        Private Sub RegisterGlobalBackoff(milliseconds As Integer)
            If milliseconds <= 0 Then Return
            SyncLock _rateSync
                Dim blockedUntil As Long = _clock.TickCount64 + CLng(milliseconds)
                If blockedUntil > _blockedUntilTicks Then
                    _blockedUntilTicks = blockedUntil
                End If
            End SyncLock
        End Sub

        Private Function GetRetryDelayMs(response As HttpResponseMessage,
                                         attempt As Integer) As Integer
            If response.Headers.RetryAfter IsNot Nothing Then
                If response.Headers.RetryAfter.Delta.HasValue Then
                    Return Math.Max(
                        _options.MinRequestIntervalMs,
                        CInt(Math.Ceiling(
                            response.Headers.RetryAfter.Delta.Value.TotalMilliseconds)))
                End If
                If response.Headers.RetryAfter.Date.HasValue Then
                    Dim milliseconds As Double =
                        (response.Headers.RetryAfter.Date.Value - _clock.UtcNow).TotalMilliseconds
                    If milliseconds > 0.0R Then
                        Return Math.Max(
                            _options.MinRequestIntervalMs,
                            CInt(Math.Ceiling(milliseconds)))
                    End If
                End If
            End If

            Dim baseDelay As Integer = Math.Max(1100, _options.MinRequestIntervalMs)
            Dim delayValue As Long = CLng(baseDelay) * CLng(attempt + 1)
            Return CInt(Math.Min(CLng(Integer.MaxValue), delayValue))
        End Function

        Private Function ResolveTokenExpiry(root As JsonElement,
                                            issuedAt As DateTimeOffset) As DateTimeOffset
            Dim expiresIn As JsonElement
            If root.TryGetProperty("expires_in", expiresIn) Then
                Dim seconds As Double
                If expiresIn.ValueKind = JsonValueKind.Number AndAlso
                   expiresIn.TryGetDouble(seconds) AndAlso seconds > 0.0R Then
                    Return issuedAt.AddSeconds(seconds)
                End If
                If expiresIn.ValueKind = JsonValueKind.String AndAlso
                   Double.TryParse(
                       expiresIn.GetString(),
                       NumberStyles.Float,
                       CultureInfo.InvariantCulture,
                       seconds) AndAlso seconds > 0.0R Then
                    Return issuedAt.AddSeconds(seconds)
                End If
            End If

            Dim expiryKeys As String() = {"expires_dt", "expires_at", "expiration"}
            For Each key As String In expiryKeys
                Dim expiryElement As JsonElement
                If Not root.TryGetProperty(key, expiryElement) OrElse
                   expiryElement.ValueKind <> JsonValueKind.String Then Continue For

                Dim parsed As DateTimeOffset
                If DateTimeOffset.TryParse(
                    expiryElement.GetString(),
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeLocal,
                    parsed) Then
                    Return parsed.ToUniversalTime()
                End If

                Dim localDate As DateTime
                If DateTime.TryParseExact(
                    expiryElement.GetString(),
                    New String() {"yyyyMMddHHmmss", "yyyy-MM-dd HH:mm:ss"},
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    localDate) Then
                    Return New DateTimeOffset(
                        localDate,
                        TimeZoneInfo.Local.GetUtcOffset(localDate)).ToUniversalTime()
                End If
            Next

            Return issuedAt.Add(_options.TokenFallbackLifetime)
        End Function

        Private Shared Function ReadToken(root As JsonElement) As String
            Dim element As JsonElement
            If root.TryGetProperty("token", element) Then
                Return If(element.GetString(), "")
            End If
            If root.TryGetProperty("access_token", element) Then
                Return If(element.GetString(), "")
            End If
            Return ""
        End Function

        Private Shared Function HeaderOrDefault(response As HttpResponseMessage,
                                                key As String,
                                                defaultValue As String) As String
            Dim values As IEnumerable(Of String) = Nothing
            If response.Headers.TryGetValues(key, values) Then
                For Each value As String In values
                    Return value
                Next
            End If
            Return defaultValue
        End Function

        Private Sub ThrowIfDisposed()
            If _disposed Then Throw New ObjectDisposedException(NameOf(KiwoomApiSession))
        End Sub

        Public Sub Dispose() Implements IDisposable.Dispose
            If _disposed Then Return
            _disposed = True
            _http.Dispose()
        End Sub
    End Class

    Public NotInheritable Class KiwoomApiSessionProvider
        Private Shared ReadOnly DefaultSession As New Lazy(Of KiwoomApiSession)(
            AddressOf CreateDefaultSession,
            LazyThreadSafetyMode.ExecutionAndPublication)

        Private Sub New()
        End Sub

        Public Shared Function GetDefault() As KiwoomApiSession
            Return DefaultSession.Value
        End Function

        Private Shared Function CreateDefaultSession() As KiwoomApiSession
            Dim options As New KiwoomApiSessionOptions With {
                .RestHost = EnvConfig.RestHost,
                .AppKey = EnvConfig.AppKey,
                .SecretKey = EnvConfig.SecretKey,
                .IsMock = EnvConfig.IsMock,
                .MinRequestIntervalMs = If(EnvConfig.IsMock, 1100, 220),
                .MaxRateLimitRetries = 4
            }
            Return New KiwoomApiSession(options)
        End Function
    End Class
End Namespace
