Option Strict On
Option Explicit On
Option Infer Off

Imports System.Collections.Concurrent
Imports System.Net
Imports System.Net.Http
Imports System.Net.Http.Headers
Imports System.Threading
Imports System.Threading.Tasks
Imports ChartKit.DataSources

Namespace Verification
    Public Module Program
        Public Function Main() As Integer
            Try
                VerifySharedSessionAcrossSources()
                VerifyConcurrentTokenSingleFlight()
                VerifyTokenExpiryRefresh()
                VerifyUnauthorizedRefresh()
                VerifyGlobalRateLimitBackoff()
                VerifyConcurrentRequestSpacing()

                Console.WriteLine("kiwoom_session_verification=PASS")
                Return 0
            Catch ex As Exception
                Console.Error.WriteLine("kiwoom_session_verification=FAIL")
                Console.Error.WriteLine(ex.ToString())
                Return 1
            End Try
        End Function

        Private Sub VerifySharedSessionAcrossSources()
            Dim clock As New FakeKiwoomClock()
            Dim handler As New FakeKiwoomHandler(clock)
            Dim options As KiwoomApiSessionOptions = CreateOptions(100)

            Using session As New KiwoomApiSession(options, handler, clock)
                Dim first As New KiwoomRestSource(session)
                Dim second As New KiwoomRestSource(session)

                ExpectEqual(first.GetStockName("005930"), "TEST", "첫 종목명")
                ExpectEqual(second.GetStockName("000660"), "TEST", "두 번째 종목명")

                ExpectEqual(handler.TokenRequestCount, 1, "두 source의 token 발급 횟수")
                ExpectEqual(handler.ApiRequestCount, 2, "두 source의 API 호출 수")

                Dim ticks As Long() = handler.ApiRequestTicks.ToArray()
                Array.Sort(ticks)
                Expect(ticks(1) - ticks(0) >= 100L,
                       "두 source가 전역 throttle을 공유하지 않음")
            End Using
        End Sub

        Private Sub VerifyConcurrentTokenSingleFlight()
            Dim clock As New FakeKiwoomClock()
            Dim handler As New FakeKiwoomHandler(clock) With {
                .TokenResponseDelayMs = 30
            }
            Dim options As KiwoomApiSessionOptions = CreateOptions(0)

            Using session As New KiwoomApiSession(options, handler, clock)
                Dim tasks As New List(Of Task(Of String))()
                For index As Integer = 0 To 15
                    tasks.Add(Task.Run(Function() session.GetAccessToken()))
                Next

                Task.WaitAll(tasks.Cast(Of Task)().ToArray())
                For Each task As Task(Of String) In tasks
                    ExpectEqual(task.Result, "TOKEN-1", "동시 token 결과")
                Next
                ExpectEqual(handler.TokenRequestCount, 1, "동시 token 단일 발급")
            End Using
        End Sub

        Private Sub VerifyTokenExpiryRefresh()
            Dim clock As New FakeKiwoomClock()
            Dim handler As New FakeKiwoomHandler(clock) With {
                .TokenLifetimeSeconds = 10
            }
            Dim options As KiwoomApiSessionOptions = CreateOptions(0)

            Using session As New KiwoomApiSession(options, handler, clock)
                ExpectEqual(session.GetAccessToken(), "TOKEN-1", "첫 token")
                clock.Advance(TimeSpan.FromSeconds(11))
                ExpectEqual(session.GetAccessToken(), "TOKEN-2", "만료 후 token")
                ExpectEqual(handler.TokenRequestCount, 2, "만료 후 재발급 횟수")
            End Using
        End Sub

        Private Sub VerifyUnauthorizedRefresh()
            Dim clock As New FakeKiwoomClock()
            Dim handler As New FakeKiwoomHandler(clock)
            handler.EnqueueApiResponse(HttpStatusCode.Unauthorized, "{}")
            handler.EnqueueApiResponse(HttpStatusCode.OK, "{""stk_nm"":""TEST""}")
            Dim options As KiwoomApiSessionOptions = CreateOptions(0)

            Using session As New KiwoomApiSession(options, handler, clock)
                Dim source As New KiwoomRestSource(session)
                ExpectEqual(source.GetStockName("005930"), "TEST", "401 재인증 결과")
                ExpectEqual(handler.TokenRequestCount, 2, "401 이후 token 재발급")

                Dim authorizations As String() = handler.AuthorizationValues.ToArray()
                ExpectEqual(authorizations.Length, 2, "401 API 시도 수")
                Expect(authorizations(0).EndsWith("TOKEN-1", StringComparison.Ordinal),
                       "첫 요청 token 오류")
                Expect(authorizations(1).EndsWith("TOKEN-2", StringComparison.Ordinal),
                       "재인증 요청 token 오류")
            End Using
        End Sub

        Private Sub VerifyGlobalRateLimitBackoff()
            Dim clock As New FakeKiwoomClock()
            Dim handler As New FakeKiwoomHandler(clock)
            handler.EnqueueApiResponse(
                HttpStatusCode.TooManyRequests,
                "{}",
                TimeSpan.FromSeconds(2))
            handler.EnqueueApiResponse(HttpStatusCode.OK, "{""stk_nm"":""TEST""}")
            Dim options As KiwoomApiSessionOptions = CreateOptions(100)

            Using session As New KiwoomApiSession(options, handler, clock)
                Dim source As New KiwoomRestSource(session)
                ExpectEqual(source.GetStockName("005930"), "TEST", "429 재시도 결과")
                ExpectEqual(handler.ApiRequestCount, 2, "429 API 시도 수")
                Expect(clock.TotalSleptMilliseconds >= 2000L,
                       "Retry-After 전역 대기 미적용")

                Dim ticks As Long() = handler.ApiRequestTicks.ToArray()
                Array.Sort(ticks)
                Expect(ticks(1) - ticks(0) >= 2000L,
                       "429 backoff 이후 요청 시각 오류")
            End Using
        End Sub

        Private Sub VerifyConcurrentRequestSpacing()
            Dim clock As New FakeKiwoomClock()
            Dim handler As New FakeKiwoomHandler(clock)
            Dim options As KiwoomApiSessionOptions = CreateOptions(75)

            Using session As New KiwoomApiSession(options, handler, clock)
                Dim tasks As New List(Of Task)()
                For index As Integer = 0 To 7
                    tasks.Add(Task.Run(
                        Sub()
                            Dim source As New KiwoomRestSource(session)
                            Dim ignored As String = source.GetStockName("005930")
                        End Sub))
                Next
                Task.WaitAll(tasks.ToArray())

                Dim ticks As Long() = handler.ApiRequestTicks.ToArray()
                ExpectEqual(ticks.Length, 8, "동시 API 호출 수")
                Array.Sort(ticks)
                For index As Integer = 1 To ticks.Length - 1
                    Expect(ticks(index) - ticks(index - 1) >= 75L,
                           "동시 요청 간격 위반 index=" & index.ToString())
                Next
                ExpectEqual(handler.TokenRequestCount, 1, "동시 API token 단일 발급")
            End Using
        End Sub

        Private Function CreateOptions(minimumIntervalMs As Integer) As KiwoomApiSessionOptions
            Return New KiwoomApiSessionOptions With {
                .RestHost = "https://unit.test",
                .AppKey = "app-key",
                .SecretKey = "secret-key",
                .IsMock = False,
                .MinRequestIntervalMs = minimumIntervalMs,
                .MaxRateLimitRetries = 3,
                .TokenRefreshSkew = TimeSpan.Zero,
                .TokenFallbackLifetime = TimeSpan.FromHours(1)
            }
        End Function

        Private Sub Expect(condition As Boolean, message As String)
            If Not condition Then Throw New InvalidOperationException(message)
        End Sub

        Private Sub ExpectEqual(actual As Integer, expected As Integer, message As String)
            If actual <> expected Then
                Throw New InvalidOperationException(
                    $"{message}: actual={actual}, expected={expected}")
            End If
        End Sub

        Private Sub ExpectEqual(actual As String, expected As String, message As String)
            If Not String.Equals(actual, expected, StringComparison.Ordinal) Then
                Throw New InvalidOperationException(
                    $"{message}: actual={actual}, expected={expected}")
            End If
        End Sub
    End Module

    Public NotInheritable Class FakeKiwoomClock
        Implements IKiwoomClock

        Private ReadOnly _sync As New Object()
        Private _utcNow As DateTimeOffset =
            New DateTimeOffset(2026, 7, 31, 0, 0, 0, TimeSpan.Zero)
        Private _ticks As Long
        Private _totalSlept As Long

        Public ReadOnly Property UtcNow As DateTimeOffset Implements IKiwoomClock.UtcNow
            Get
                SyncLock _sync
                    Return _utcNow
                End SyncLock
            End Get
        End Property

        Public ReadOnly Property TickCount64 As Long Implements IKiwoomClock.TickCount64
            Get
                SyncLock _sync
                    Return _ticks
                End SyncLock
            End Get
        End Property

        Public ReadOnly Property TotalSleptMilliseconds As Long
            Get
                SyncLock _sync
                    Return _totalSlept
                End SyncLock
            End Get
        End Property

        Public Sub Sleep(milliseconds As Integer) Implements IKiwoomClock.Sleep
            If milliseconds <= 0 Then Return
            SyncLock _sync
                _ticks += milliseconds
                _totalSlept += milliseconds
                _utcNow = _utcNow.AddMilliseconds(milliseconds)
            End SyncLock
        End Sub

        Public Sub Advance(value As TimeSpan)
            SyncLock _sync
                Dim milliseconds As Long = CLng(Math.Ceiling(value.TotalMilliseconds))
                _ticks += milliseconds
                _utcNow = _utcNow.AddMilliseconds(milliseconds)
            End SyncLock
        End Sub
    End Class

    Public NotInheritable Class FakeKiwoomHandler
        Inherits HttpMessageHandler

        Private ReadOnly _clock As FakeKiwoomClock
        Private ReadOnly _responses As New ConcurrentQueue(Of FakeApiResponse)()
        Private ReadOnly _apiTicks As New ConcurrentQueue(Of Long)()
        Private ReadOnly _authorizations As New ConcurrentQueue(Of String)()
        Private _tokenRequests As Integer
        Private _apiRequests As Integer

        Public Sub New(clock As FakeKiwoomClock)
            _clock = clock
        End Sub

        Public Property TokenLifetimeSeconds As Integer = 3600
        Public Property TokenResponseDelayMs As Integer

        Public ReadOnly Property TokenRequestCount As Integer
            Get
                Return Volatile.Read(_tokenRequests)
            End Get
        End Property

        Public ReadOnly Property ApiRequestCount As Integer
            Get
                Return Volatile.Read(_apiRequests)
            End Get
        End Property

        Public ReadOnly Property ApiRequestTicks As ConcurrentQueue(Of Long)
            Get
                Return _apiTicks
            End Get
        End Property

        Public ReadOnly Property AuthorizationValues As ConcurrentQueue(Of String)
            Get
                Return _authorizations
            End Get
        End Property

        Public Sub EnqueueApiResponse(statusCode As HttpStatusCode,
                                      body As String,
                                      Optional retryAfter As TimeSpan? = Nothing)
            _responses.Enqueue(New FakeApiResponse(statusCode, body, retryAfter))
        End Sub

        Protected Overrides Function SendAsync(request As HttpRequestMessage,
                                               cancellationToken As CancellationToken) As Task(Of HttpResponseMessage)
            Dim path As String = request.RequestUri.AbsolutePath
            If String.Equals(path, "/oauth2/token", StringComparison.Ordinal) Then
                Dim number As Integer = Interlocked.Increment(_tokenRequests)
                If TokenResponseDelayMs > 0 Then Thread.Sleep(TokenResponseDelayMs)
                Dim json As String =
                    $"{{""token"":""TOKEN-{number}"",""expires_in"":{TokenLifetimeSeconds}}}"
                Return Task.FromResult(CreateResponse(HttpStatusCode.OK, json, Nothing))
            End If

            Interlocked.Increment(_apiRequests)
            _apiTicks.Enqueue(_clock.TickCount64)

            Dim authorization As String = ""
            Dim values As IEnumerable(Of String) = Nothing
            If request.Headers.TryGetValues("authorization", values) Then
                authorization = values.FirstOrDefault()
            End If
            _authorizations.Enqueue(If(authorization, ""))

            Dim scripted As FakeApiResponse = Nothing
            If Not _responses.TryDequeue(scripted) Then
                scripted = New FakeApiResponse(
                    HttpStatusCode.OK,
                    "{""stk_nm"":""TEST""}",
                    Nothing)
            End If

            Return Task.FromResult(
                CreateResponse(scripted.StatusCode, scripted.Body, scripted.RetryAfter))
        End Function

        Private Shared Function CreateResponse(statusCode As HttpStatusCode,
                                               body As String,
                                               retryAfter As TimeSpan?) As HttpResponseMessage
            Dim response As New HttpResponseMessage(statusCode) With {
                .Content = New StringContent(body)
            }
            response.Headers.TryAddWithoutValidation("cont-yn", "N")
            response.Headers.TryAddWithoutValidation("next-key", "")
            If retryAfter.HasValue Then
                response.Headers.RetryAfter =
                    New RetryConditionHeaderValue(retryAfter.Value)
            End If
            Return response
        End Function
    End Class

    Public NotInheritable Class FakeApiResponse
        Public Sub New(statusCode As HttpStatusCode,
                       body As String,
                       retryAfter As TimeSpan?)
            Me.StatusCode = statusCode
            Me.Body = body
            Me.RetryAfter = retryAfter
        End Sub

        Public ReadOnly Property StatusCode As HttpStatusCode
        Public ReadOnly Property Body As String
        Public ReadOnly Property RetryAfter As TimeSpan?
    End Class
End Namespace
