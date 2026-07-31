Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$sourcePath = Join-Path $root 'DataSources\KiwoomRestSource.vb'
$sessionPath = Join-Path $root 'DataSources\KiwoomApiSession.vb'

function Read-Normalized([string]$path) {
    return [System.IO.File]::ReadAllText($path).Replace("`r`n", "`n").Replace("`r", "`n")
}

function Write-Utf8BomCrLf([string]$path, [string]$content) {
    $normalized = $content.Replace("`r`n", "`n").Replace("`r", "`n").Replace("`n", "`r`n")
    $encoding = New-Object System.Text.UTF8Encoding($true)
    [System.IO.File]::WriteAllText($path, $normalized, $encoding)
}

$session = Read-Normalized $sessionPath
$oldChecked = '            Return checked(baseDelay * (attempt + 1))'
$newChecked = @'
            Dim delayValue As Long = CLng(baseDelay) * CLng(attempt + 1)
            Return CInt(Math.Min(CLng(Integer.MaxValue), delayValue))
'@.TrimEnd()
if (-not $session.Contains($oldChecked)) {
    throw 'KiwoomApiSession checked 표현을 찾지 못했습니다.'
}
$session = $session.Replace($oldChecked, $newChecked)
Write-Utf8BomCrLf $sessionPath $session

$content = Read-Normalized $sourcePath

$oldFields = @'
        Private Shared ReadOnly _http As New HttpClient()
        Private ReadOnly _sync As New Object()
        Private _token As String = Nothing
        Private _lastCallTicks As Long = 0
        Private _lastTicCount As Integer = 0
'@.TrimEnd()
$newFields = @'
        Private ReadOnly _sync As New Object()
        Private ReadOnly _apiSession As KiwoomApiSession
        Private _lastTicCount As Integer = 0
'@.TrimEnd()
if (-not $content.Contains($oldFields)) {
    throw 'KiwoomRestSource 기존 인증 필드 블록을 찾지 못했습니다.'
}
$content = $content.Replace($oldFields, $newFields)

$content = $content.Replace("        Private Const RealMinIntervalMs As Integer = 220`n", '')
$content = $content.Replace("        Private Const MockMinIntervalMs As Integer = 1100`n", '')
$content = $content.Replace("        Private Const MaxRateLimitRetries As Integer = 4`n", '')

$eventMarker = @'
        Public Event CandleAppended As EventHandler(Of CandleAppendedEventArgs) Implements ICandleDataSource.CandleAppended
        Public Event CandleUpdated As EventHandler(Of CandleUpdatedEventArgs) Implements ICandleDataSource.CandleUpdated
'@.TrimEnd()
$eventReplacement = @'
        Public Event CandleAppended As EventHandler(Of CandleAppendedEventArgs) Implements ICandleDataSource.CandleAppended
        Public Event CandleUpdated As EventHandler(Of CandleUpdatedEventArgs) Implements ICandleDataSource.CandleUpdated

        Public Sub New()
            Me.New(KiwoomApiSessionProvider.GetDefault())
        End Sub

        Public Sub New(apiSession As KiwoomApiSession)
            If apiSession Is Nothing Then Throw New ArgumentNullException(NameOf(apiSession))
            _apiSession = apiSession
        End Sub
'@.TrimEnd()
if (-not $content.Contains($eventMarker)) {
    throw 'KiwoomRestSource 이벤트 선언 블록을 찾지 못했습니다.'
}
$content = $content.Replace($eventMarker, $eventReplacement)

$authStart = $content.IndexOf("        '' ── 토큰 발급", [System.StringComparison]::Ordinal)
$continuationStart = $content.IndexOf("        '' 연속조회 안전 상한", $authStart, [System.StringComparison]::Ordinal)
if ($authStart -lt 0 -or $continuationStart -lt 0) {
    throw 'KiwoomRestSource 기존 인증/호출 메서드 범위를 찾지 못했습니다.'
}
$newCallApi = @'
        '' ── 공통 호출: 인증·전역 throttle·재시도는 KiwoomApiSession이 담당 ──
        Private Function CallApi(path As String,
                                 apiId As String,
                                 body As String,
                                 contYn As String,
                                 nextKey As String,
                                 ByRef outCont As String,
                                 ByRef outNext As String) As JsonDocument
            Return _apiSession.PostJson(
                path,
                apiId,
                body,
                contYn,
                nextKey,
                outCont,
                outNext)
        End Function

'@
$content = $content.Substring(0, $authStart) + $newCallApi + $content.Substring($continuationStart)

$startRealtimeOld = @'
            EnsureToken()
            _realtimeSymbol = If(String.IsNullOrWhiteSpace(req.Symbol), EnvConfig.DefaultSymbol, req.Symbol.Trim())
'@.TrimEnd()
$startRealtimeNew = @'
            Dim ignoredAccessToken As String = _apiSession.GetAccessToken()
            _realtimeSymbol = If(String.IsNullOrWhiteSpace(req.Symbol), EnvConfig.DefaultSymbol, req.Symbol.Trim())
'@.TrimEnd()
if (-not $content.Contains($startRealtimeOld)) {
    throw 'StartRealtime의 EnsureToken 호출을 찾지 못했습니다.'
}
$content = $content.Replace($startRealtimeOld, $startRealtimeNew)

$sessionStartOld = @'
        Private Async Function RunRealtimeSessionAsync(cancel As CancellationToken) As Task
            Dim ws As New ClientWebSocket()
'@.TrimEnd()
$sessionStartNew = @'
        Private Async Function RunRealtimeSessionAsync(cancel As CancellationToken) As Task
            Dim accessToken As String = _apiSession.GetAccessToken()
            Dim ws As New ClientWebSocket()
'@.TrimEnd()
if (-not $content.Contains($sessionStartOld)) {
    throw 'RunRealtimeSessionAsync 시작 블록을 찾지 못했습니다.'
}
$content = $content.Replace($sessionStartOld, $sessionStartNew)

$loginOld = '{"trnm", "LOGIN"}, {"token", _token}}, cancel)'
$loginNew = '{"trnm", "LOGIN"}, {"token", accessToken}}, cancel)'
if (-not $content.Contains($loginOld)) {
    throw 'WebSocket LOGIN token 참조를 찾지 못했습니다.'
}
$content = $content.Replace($loginOld, $loginNew)

$loginFailureOld = @'
                            If resultCode <> 0 Then
                                Throw New InvalidOperationException(
                                    "키움 WebSocket 로그인 실패: " & JsonString(root, "return_msg"))
                            End If
'@.TrimEnd()
$loginFailureNew = @'
                            If resultCode <> 0 Then
                                _apiSession.InvalidateToken(accessToken)
                                Throw New InvalidOperationException(
                                    "키움 WebSocket 로그인 실패: " & JsonString(root, "return_msg"))
                            End If
'@.TrimEnd()
if (-not $content.Contains($loginFailureOld)) {
    throw 'WebSocket LOGIN 실패 블록을 찾지 못했습니다.'
}
$content = $content.Replace($loginFailureOld, $loginFailureNew)

if ($content.Contains('_token') -or $content.Contains('EnsureToken()') -or $content.Contains('Private Sub Throttle()')) {
    throw 'KiwoomRestSource에 이전 인증 상태 참조가 남아 있습니다.'
}

Write-Utf8BomCrLf $sourcePath $content

Write-Host 'Kiwoom session integration patch applied.'
git diff --check
git diff -- DataSources/KiwoomApiSession.vb DataSources/KiwoomRestSource.vb
