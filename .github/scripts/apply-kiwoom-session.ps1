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

function Replace-Required([string]$content, [string]$oldValue, [string]$newValue, [string]$description) {
    if (-not $content.Contains($oldValue)) {
        throw "필수 패턴을 찾지 못했습니다: $description"
    }
    return $content.Replace($oldValue, $newValue)
}

$session = Read-Normalized $sessionPath
$session = Replace-Required $session `
    '            Return checked(baseDelay * (attempt + 1))' `
    "            Dim delayValue As Long = CLng(baseDelay) * CLng(attempt + 1)`n            Return CInt(Math.Min(CLng(Integer.MaxValue), delayValue))" `
    'KiwoomApiSession checked 표현'
Write-Utf8BomCrLf $sessionPath $session

$content = Read-Normalized $sourcePath
$content = Replace-Required $content `
    '        Private Shared ReadOnly _http As New HttpClient()' `
    '        Private ReadOnly _apiSession As KiwoomApiSession' `
    '공용 HttpClient 필드'
$content = Replace-Required $content `
    "        Private _token As String = Nothing`n" `
    '' `
    '인스턴스 token 필드'
$content = Replace-Required $content `
    "        Private _lastCallTicks As Long = 0`n" `
    '' `
    '인스턴스 throttle 필드'
$content = Replace-Required $content `
    "        Private Const RealMinIntervalMs As Integer = 220`n" `
    '' `
    '실서버 throttle 상수'
$content = Replace-Required $content `
    "        Private Const MockMinIntervalMs As Integer = 1100`n" `
    '' `
    '모의서버 throttle 상수'
$content = Replace-Required $content `
    "        Private Const MaxRateLimitRetries As Integer = 4`n" `
    '' `
    '인스턴스 retry 상수'

$secondEvent = '        Public Event CandleUpdated As EventHandler(Of CandleUpdatedEventArgs) Implements ICandleDataSource.CandleUpdated'
$eventIndex = $content.IndexOf($secondEvent, [System.StringComparison]::Ordinal)
if ($eventIndex -lt 0) {
    throw 'CandleUpdated 이벤트 선언을 찾지 못했습니다.'
}
$insertAt = $eventIndex + $secondEvent.Length
$constructors = @'


        Public Sub New()
            Me.New(KiwoomApiSessionProvider.GetDefault())
        End Sub

        Public Sub New(apiSession As KiwoomApiSession)
            If apiSession Is Nothing Then Throw New ArgumentNullException(NameOf(apiSession))
            _apiSession = apiSession
        End Sub
'@
$content = $content.Substring(0, $insertAt) + $constructors + $content.Substring($insertAt)

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

$content = Replace-Required $content `
    "            EnsureToken()`n" `
    "            Dim ignoredAccessToken As String = _apiSession.GetAccessToken()`n" `
    'StartRealtime EnsureToken 호출'

$content = Replace-Required $content `
    "        Private Async Function RunRealtimeSessionAsync(cancel As CancellationToken) As Task`n            Dim ws As New ClientWebSocket()" `
    "        Private Async Function RunRealtimeSessionAsync(cancel As CancellationToken) As Task`n            Dim accessToken As String = _apiSession.GetAccessToken()`n            Dim ws As New ClientWebSocket()" `
    'RunRealtimeSessionAsync 시작 블록'

$content = Replace-Required $content `
    '{"trnm", "LOGIN"}, {"token", _token}}, cancel)' `
    '{"trnm", "LOGIN"}, {"token", accessToken}}, cancel)' `
    'WebSocket LOGIN token 참조'

$content = Replace-Required $content `
    "                            If resultCode <> 0 Then`n                                Throw New InvalidOperationException(`n                                    ""키움 WebSocket 로그인 실패: "" & JsonString(root, ""return_msg""))`n                            End If" `
    "                            If resultCode <> 0 Then`n                                _apiSession.InvalidateToken(accessToken)`n                                Throw New InvalidOperationException(`n                                    ""키움 WebSocket 로그인 실패: "" & JsonString(root, ""return_msg""))`n                            End If" `
    'WebSocket LOGIN 실패 블록'

if ($content.Contains('_token') -or $content.Contains('EnsureToken()') -or $content.Contains('Private Sub Throttle()')) {
    throw 'KiwoomRestSource에 이전 인증 상태 참조가 남아 있습니다.'
}

Write-Utf8BomCrLf $sourcePath $content

Write-Host 'Kiwoom session integration patch applied.'
git diff --check
git diff -- DataSources/KiwoomApiSession.vb DataSources/KiwoomRestSource.vb
