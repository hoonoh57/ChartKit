[CmdletBinding()]
param(
    [ValidateSet('Baseline', 'Reconnect', 'Soak')]
    [string]$Mode = 'Baseline',

    [Parameter(Mandatory = $true)]
    [string[]]$Symbols,

    [ValidateRange(1, 14400)]
    [int]$DurationSeconds = 0,

    [ValidateSet('1m', '3m', '5m', '10m', '15m', '30m', '60m')]
    [string]$Timeframe = '1m',

    [ValidateRange(1, 4000)]
    [int]$HistoryCount = 240,

    [string]$LogDirectory = 'artifacts/realtime-validation',

    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot

function Import-DotEnv {
    param([string]$Path)

    if (-not (Test-Path $Path)) { return }
    foreach ($line in Get-Content -LiteralPath $Path) {
        $text = $line.Trim()
        if ($text.Length -eq 0 -or $text.StartsWith('#')) { continue }
        $separator = $text.IndexOf('=')
        if ($separator -le 0) { continue }

        $name = $text.Substring(0, $separator).Trim()
        $value = $text.Substring($separator + 1).Trim()
        if ($value.Length -ge 2 -and
            (($value.StartsWith('"') -and $value.EndsWith('"')) -or
             ($value.StartsWith("'") -and $value.EndsWith("'")))) {
            $value = $value.Substring(1, $value.Length - 2)
        }

        if ([string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable($name))) {
            [Environment]::SetEnvironmentVariable($name, $value, 'Process')
        }
    }
}

function Test-Configured {
    param([string[]]$Names)
    foreach ($name in $Names) {
        if (-not [string]::IsNullOrWhiteSpace(
                [Environment]::GetEnvironmentVariable($name))) {
            return $true
        }
    }
    return $false
}

Import-DotEnv (Join-Path $repoRoot '.env')

if ($DurationSeconds -eq 0) {
    $DurationSeconds = switch ($Mode) {
        'Baseline' { 180 }
        'Reconnect' { 600 }
        'Soak' { 3600 }
    }
}

$normalizedSymbols = @(
    $Symbols |
        ForEach-Object { $_.Trim() } |
        Where-Object { $_.Length -gt 0 } |
        Select-Object -Unique
)
if ($normalizedSymbols.Count -eq 0) {
    throw 'At least one non-empty symbol is required.'
}

$isMock = -not ([Environment]::GetEnvironmentVariable('KIWOOM_MOCK') -match '^(false|0|no)$')
$credentialReady = if ($isMock) {
    (Test-Configured @('KIWOOM_MOCK_APP_KEY', 'KIWOOM_APP_KEY')) -and
    (Test-Configured @('KIWOOM_MOCK_SECRET_KEY', 'KIWOOM_SECRET_KEY'))
}
else {
    (Test-Configured @('KIWOOM_REAL_APP_KEY', 'KIWOOM_APP_KEY')) -and
    (Test-Configured @('KIWOOM_REAL_SECRET_KEY', 'KIWOOM_SECRET_KEY'))
}
if (-not $credentialReady) {
    throw "Kiwoom credentials are not configured for KIWOOM_MOCK=$isMock."
}

$resolvedLogDirectory = if ([System.IO.Path]::IsPathRooted($LogDirectory)) {
    $LogDirectory
}
else {
    Join-Path $repoRoot $LogDirectory
}
New-Item -ItemType Directory -Path $resolvedLogDirectory -Force | Out-Null

$timestamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$symbolLabel = ($normalizedSymbols -join '-') -replace '[^0-9A-Za-z_-]', '_'
$logPath = Join-Path $resolvedLogDirectory (
    "kiwoom-$($Mode.ToLowerInvariant())-$symbolLabel-$timestamp.log")

$head = (git rev-parse HEAD).Trim()
$branch = (git branch --show-current).Trim()
$header = @(
    "validation_mode=$Mode"
    "git_branch=$branch"
    "git_head=$head"
    "kiwoom_mock=$isMock"
    "symbols=$($normalizedSymbols -join ',')"
    "timeframe=$Timeframe"
    "history_count=$HistoryCount"
    "duration_seconds=$DurationSeconds"
    "started_at=$((Get-Date).ToString('O'))"
)
$header | Set-Content -LiteralPath $logPath -Encoding utf8
$header | ForEach-Object { Write-Host $_ }

if ($Mode -eq 'Reconnect') {
    Write-Host ''
    Write-Host 'PHYSICAL RECONNECT TEST:' -ForegroundColor Yellow
    Write-Host '1. Wait until realtime samples are arriving.' -ForegroundColor Yellow
    Write-Host '2. Physically disconnect the active network for 10-20 seconds.' -ForegroundColor Yellow
    Write-Host '3. Restore the same network and leave the probe running.' -ForegroundColor Yellow
    Write-Host 'The script requires attempts>=2 and registrations>=2.' -ForegroundColor Yellow
    Write-Host ''
}

if (-not $SkipBuild) {
    dotnet build `
        .\csharp_chartkit\ChartKit.CSharp.sln `
        -c Release `
        --no-incremental
    if ($LASTEXITCODE -ne 0) {
        throw "Release build failed with exit code $LASTEXITCODE."
    }
}

$symbolArgument = $normalizedSymbols -join ','
$arguments = @(
    'run',
    '--project', '.\csharp_chartkit\src\ChartKit.App\ChartKit.App.csproj',
    '-c', 'Release',
    '--no-build',
    '--',
    '--kiwoom-probe',
    '--symbols', $symbolArgument,
    '--timeframe', $Timeframe,
    '--count', $HistoryCount,
    '--realtime-seconds', $DurationSeconds
)

& dotnet @arguments 2>&1 |
    Tee-Object -FilePath $logPath -Append
$probeExitCode = $LASTEXITCODE

$log = Get-Content -LiteralPath $logPath -Raw
function Read-BoolMarker {
    param([string]$Name)
    $match = [regex]::Match(
        $log,
        "(?m)^$([regex]::Escape($Name))=(True|False)$",
        [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)
    return $match.Success -and $match.Groups[1].Value -ieq 'True'
}

$passMarker = $log -match '(?m)^kiwoom_realtime_validation_probe=PASS$'
$diagnosticsConsistent = Read-BoolMarker 'diagnostics_consistent'
$seedContinuity = Read-BoolMarker 'rest_seed_continuity_observed'
$reconnectObserved = Read-BoolMarker 'physical_reconnect_observed'
$eventMatch = [regex]::Match($log, '(?m)^realtime_event_count=(\d+)$')
$eventCount = if ($eventMatch.Success) {
    [long]$eventMatch.Groups[1].Value
}
else {
    0L
}

$success = $probeExitCode -eq 0 -and
           $passMarker -and
           $diagnosticsConsistent -and
           $seedContinuity -and
           $eventCount -gt 0
if ($Mode -eq 'Reconnect') {
    $success = $success -and $reconnectObserved
}

$result = if ($success) { 'PASS' } else { 'FAIL' }
$footer = @(
    "validation_result=$result"
    "probe_exit_code=$probeExitCode"
    "validated_event_count=$eventCount"
    "validated_seed_continuity=$seedContinuity"
    "validated_reconnect=$reconnectObserved"
    "validated_diagnostics_consistent=$diagnosticsConsistent"
    "ended_at=$((Get-Date).ToString('O'))"
    "log_path=$logPath"
)
$footer | Add-Content -LiteralPath $logPath -Encoding utf8
$footer | ForEach-Object { Write-Host $_ }

if (-not $success) {
    throw "Kiwoom $Mode validation failed. Review $logPath"
}
