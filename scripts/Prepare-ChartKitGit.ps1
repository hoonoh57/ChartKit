[CmdletBinding()]
param(
    [string]$ProjectRoot = "E:\2026\gpt\vb\sciaChart\ChartKit",

    [string]$RemoteUrl = "https://github.com/hoonoh57/ChartKit.git",

    [string]$BranchName = "main",

    [string]$CommitMessage = "Sync latest ChartKit project",

    [switch]$SkipBuild,

    [switch]$RebaseRemote,

    [switch]$ReplaceOrigin,

    [switch]$Commit,

    [switch]$Push
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

try {
    [Console]::InputEncoding = New-Object System.Text.UTF8Encoding($false)
    [Console]::OutputEncoding = New-Object System.Text.UTF8Encoding($false)
    $OutputEncoding = New-Object System.Text.UTF8Encoding($false)
    cmd /c chcp 65001 *> $null
}
catch {
    # 콘솔 인코딩 변경 실패가 Git 작업을 막지는 않음
}

function Write-Step {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Message
    )

    Write-Host ""
    Write-Host "============================================================" -ForegroundColor DarkCyan
    Write-Host $Message -ForegroundColor Cyan
    Write-Host "============================================================" -ForegroundColor DarkCyan
}

function Invoke-GitChecked {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$Arguments
    )

    & git @Arguments

    if ($LASTEXITCODE -ne 0) {
        $commandText = "git " + ($Arguments -join " ")
        throw "Git 명령 실패: $commandText"
    }
}

function Get-GitText {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$Arguments
    )

    $result = & git @Arguments 2>&1

    if ($LASTEXITCODE -ne 0) {
        $commandText = "git " + ($Arguments -join " ")
        throw "Git 명령 실패: $commandText`r`n$($result -join "`r`n")"
    }

    return (($result | Out-String).Trim())
}

function Test-GitReference {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Reference
    )

    if ([string]::IsNullOrWhiteSpace($Reference)) {
        return $false
    }

    # rev-parse는 존재하지 않는 참조에서 fatal 메시지를 stderr로 출력한다.
    # Windows PowerShell 5.1에서는 이 stderr가 NativeCommandError로
    # 변환될 수 있으므로, 출력이 없는 show-ref --quiet를 사용한다.
    $PreviousErrorActionPreference = $ErrorActionPreference
    $ExitCode = 1

    try {
        $ErrorActionPreference = "SilentlyContinue"

        & git show-ref `
            --verify `
            --quiet `
            $Reference `
            2>$null

        $ExitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $PreviousErrorActionPreference
    }

    return ($ExitCode -eq 0)
}
function Get-AheadBehind {
    param(
        [Parameter(Mandatory = $true)]
        [string]$LeftReference,

        [Parameter(Mandatory = $true)]
        [string]$RightReference
    )

    $range = "$LeftReference...$RightReference"
    $text = Get-GitText -Arguments @(
        "rev-list",
        "--left-right",
        "--count",
        $range
    )

    $parts = $text -split "\s+"

    if ($parts.Count -lt 2) {
        throw "ahead/behind 결과를 분석하지 못했습니다: $text"
    }

    return [PSCustomObject]@{
        Ahead  = [int]$parts[0]
        Behind = [int]$parts[1]
    }
}

function Show-RepositoryStatus {
    Write-Host ""
    Write-Host "[Git 상태]" -ForegroundColor Yellow
    & git status --short --branch

    Write-Host ""
    Write-Host "[원격 저장소]" -ForegroundColor Yellow
    & git remote -v

    Write-Host ""
    Write-Host "[최근 커밋]" -ForegroundColor Yellow
    & git log --oneline --decorate -5
}

function Find-BuildTarget {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Root
    )

    $solution = Get-ChildItem `
        -LiteralPath $Root `
        -Filter "*.sln" `
        -File `
        -ErrorAction SilentlyContinue |
        Select-Object -First 1

    if ($null -ne $solution) {
        return $solution.FullName
    }

    $rootProject = Get-ChildItem `
        -LiteralPath $Root `
        -File `
        -ErrorAction SilentlyContinue |
        Where-Object {
            $_.Extension -in @(".vbproj", ".csproj")
        } |
        Select-Object -First 1

    if ($null -ne $rootProject) {
        return $rootProject.FullName
    }

    $recursiveProject = Get-ChildItem `
        -LiteralPath $Root `
        -Recurse `
        -File `
        -ErrorAction SilentlyContinue |
        Where-Object {
            $_.Extension -in @(".vbproj", ".csproj") -and
            $_.FullName -notmatch "[\\/](bin|obj)[\\/]"
        } |
        Select-Object -First 1

    if ($null -ne $recursiveProject) {
        return $recursiveProject.FullName
    }

    return $null
}

Write-Step "1. 프로젝트 경로와 개발 도구 확인"

if (-not (Test-Path -LiteralPath $ProjectRoot -PathType Container)) {
    throw "프로젝트 경로가 없습니다: $ProjectRoot"
}

if ($null -eq (Get-Command git -ErrorAction SilentlyContinue)) {
    throw "Git 실행 파일을 찾지 못했습니다."
}

Write-Host "프로젝트: $ProjectRoot"
Write-Host "원격 저장소: $RemoteUrl"
Write-Host "대상 브랜치: $BranchName"

Set-Location -LiteralPath $ProjectRoot

& git --version

Write-Step "2. Git 저장소 초기화 또는 기존 저장소 확인"

$GitDirectory = Join-Path $ProjectRoot ".git"
$NewRepository = -not (Test-Path -LiteralPath $GitDirectory -PathType Container)

if ($NewRepository) {
    Write-Host "Git 저장소가 없어 새로 초기화합니다." -ForegroundColor Yellow

    Invoke-GitChecked -Arguments @("init")

    # 커밋이 없는 저장소에서도 현재 브랜치를 main으로 지정
    Invoke-GitChecked -Arguments @(
        "symbolic-ref",
        "HEAD",
        "refs/heads/$BranchName"
    )
}
else {
    Write-Host "기존 Git 저장소를 사용합니다." -ForegroundColor Green
}

# 한글 파일명과 한글 커밋 메시지 표시 설정
Invoke-GitChecked -Arguments @(
    "config",
    "--local",
    "core.quotepath",
    "false"
)

Invoke-GitChecked -Arguments @(
    "config",
    "--local",
    "i18n.commitEncoding",
    "utf-8"
)

Invoke-GitChecked -Arguments @(
    "config",
    "--local",
    "i18n.logOutputEncoding",
    "utf-8"
)

# 줄바꿈은 .gitattributes를 기준으로 관리
Invoke-GitChecked -Arguments @(
    "config",
    "--local",
    "core.autocrlf",
    "false"
)

Invoke-GitChecked -Arguments @(
    "config",
    "--local",
    "core.safecrlf",
    "warn"
)

Write-Step "3. origin 원격 저장소 확인"

$Remotes = @(& git remote)

if ($Remotes -notcontains "origin") {
    Write-Host "origin 원격 저장소를 추가합니다."
    Invoke-GitChecked -Arguments @(
        "remote",
        "add",
        "origin",
        $RemoteUrl
    )
}
else {
    $CurrentOrigin = Get-GitText -Arguments @(
        "remote",
        "get-url",
        "origin"
    )

    Write-Host "현재 origin: $CurrentOrigin"

    if ($CurrentOrigin -ne $RemoteUrl) {
        if ($ReplaceOrigin) {
            Write-Host "origin 주소를 변경합니다." -ForegroundColor Yellow

            Invoke-GitChecked -Arguments @(
                "remote",
                "set-url",
                "origin",
                $RemoteUrl
            )
        }
        else {
            throw @"
origin 주소가 예상 저장소와 다릅니다.

현재 주소:
$CurrentOrigin

예상 주소:
$RemoteUrl

주소 변경이 맞다면 다음 옵션을 추가하여 다시 실행하십시오.

-ReplaceOrigin
"@
        }
    }
}

Write-Step "4. 원격 main 브랜치 조회"

Invoke-GitChecked -Arguments @(
    "fetch",
    "--prune",
    "origin"
)

$RemoteReference = "refs/remotes/origin/$BranchName"

if (-not (Test-GitReference -Reference $RemoteReference)) {
    throw "원격 브랜치를 찾지 못했습니다: origin/$BranchName"
}

$HasHead = Test-GitReference -Reference "HEAD"

if (-not $HasHead) {
    Write-Host "로컬 커밋이 없어 원격 이력과 연결합니다." -ForegroundColor Yellow
    Write-Host "작업 파일은 유지하고 Git 인덱스만 원격 기준으로 맞춥니다."

    Invoke-GitChecked -Arguments @(
        "reset",
        "--mixed",
        "origin/$BranchName"
    )
}

$CurrentBranch = (
    & git symbolic-ref --quiet --short HEAD 2>$null |
    Out-String
).Trim()

if ([string]::IsNullOrWhiteSpace($CurrentBranch)) {
    throw "현재 Git 브랜치를 확인하지 못했습니다."
}

if ($CurrentBranch -eq "master" -and $BranchName -eq "main") {
    Write-Host "master 브랜치를 main으로 변경합니다." -ForegroundColor Yellow

    Invoke-GitChecked -Arguments @(
        "branch",
        "-M",
        "main"
    )

    $CurrentBranch = "main"
}

if ($CurrentBranch -ne $BranchName) {
    throw @"
현재 브랜치가 push 대상 브랜치와 다릅니다.

현재 브랜치: $CurrentBranch
대상 브랜치: $BranchName

다른 기능 브랜치의 작업을 main에 자동으로 섞지 않도록 작업을 중단했습니다.
"@
}

Write-Step "5. 로컬과 원격 커밋 관계 확인"

& git merge-base HEAD "origin/$BranchName" *> $null

if ($LASTEXITCODE -ne 0) {
    throw @"
로컬과 원격 저장소의 공통 커밋을 찾지 못했습니다.

서로 무관한 Git 이력일 가능성이 있으므로 자동 병합이나 강제 push를 진행하지 않습니다.
"@
}

$Relation = Get-AheadBehind `
    -LeftReference "HEAD" `
    -RightReference "origin/$BranchName"

Write-Host "원격보다 앞선 로컬 커밋: $($Relation.Ahead)"
Write-Host "원격에만 있는 커밋: $($Relation.Behind)"

if ($Relation.Behind -gt 0) {
    if ($RebaseRemote) {
        Write-Host "원격 변경을 rebase 방식으로 반영합니다." -ForegroundColor Yellow

        Invoke-GitChecked -Arguments @(
            "pull",
            "--rebase",
            "--autostash",
            "origin",
            $BranchName
        )

        $Relation = Get-AheadBehind `
            -LeftReference "HEAD" `
            -RightReference "origin/$BranchName"
    }
    else {
        Show-RepositoryStatus

        throw @"
원격 main에 로컬에 없는 커밋이 있습니다.

자동 rebase를 허용하려면 다음 옵션을 추가하여 다시 실행하십시오.

-RebaseRemote
"@
    }
}

Write-Step "6. Release 빌드 검증"

if ($SkipBuild) {
    Write-Host "요청에 따라 빌드를 생략합니다." -ForegroundColor Yellow
}
else {
    if ($null -eq (Get-Command dotnet -ErrorAction SilentlyContinue)) {
        throw ".NET SDK의 dotnet 명령을 찾지 못했습니다."
    }

    $BuildTarget = Find-BuildTarget -Root $ProjectRoot

    if ([string]::IsNullOrWhiteSpace($BuildTarget)) {
        throw "빌드할 .sln, .vbproj 또는 .csproj 파일을 찾지 못했습니다."
    }

    Write-Host "빌드 대상: $BuildTarget"

    & dotnet build $BuildTarget `
        --configuration Release `
        --nologo

    if ($LASTEXITCODE -ne 0) {
        throw "Release 빌드 검증에 실패했습니다."
    }

    Write-Host "Release 빌드 성공" -ForegroundColor Green
}

Write-Step "7. .gitignore 기준으로 Git 인덱스 재구성"

# 실제 로컬 파일은 삭제하지 않고 Git 추적 인덱스만 초기화한다.
# git rm 재귀 처리에는 -r 옵션을 사용한다.
& git rm -r --cached --ignore-unmatch -- .

if ($LASTEXITCODE -ne 0) {
    throw "Git 인덱스 초기화에 실패했습니다."
}

# .gitignore 기준으로 필요한 소스 파일을 다시 staging한다.
Invoke-GitChecked -Arguments @(
    "add",
    "--all",
    "--",
    "."
)
Write-Step "8. 비밀정보 파일 staging 여부 확인"

$StagedFiles = @(
    & git diff `
        --cached `
        --name-only `
        --diff-filter=ACMR
)

$SensitiveFiles = @(
    $StagedFiles |
    Where-Object {
        $normalized = $_.Replace("\", "/")

        (
            $normalized -match "(^|/)\.env($|\.)" -and
            $normalized -notmatch "(^|/)\.env\.example$"
        ) -or
        $normalized -match "\.(pfx|p12|pem|key|cer)$"
    }
)

if ($SensitiveFiles.Count -gt 0) {
    Write-Host ""
    Write-Host "다음 비밀정보 파일이 staging에서 발견되었습니다:" -ForegroundColor Red

    foreach ($file in $SensitiveFiles) {
        Write-Host "  $file" -ForegroundColor Red
    }

    & git restore --staged -- $SensitiveFiles

    throw "비밀정보 파일을 staging에서 제거하고 작업을 중단했습니다."
}

$TrackedIgnoredFiles = @(
    & git ls-files `
        --cached `
        --ignored `
        --exclude-standard
)

if ($TrackedIgnoredFiles.Count -gt 0) {
    Write-Host ""
    Write-Host "아직 Git이 추적하는 제외 대상 파일:" -ForegroundColor Red

    foreach ($file in $TrackedIgnoredFiles) {
        Write-Host "  $file" -ForegroundColor Red
    }

    throw ".gitignore 제외 대상이 Git 인덱스에 남아 있습니다."
}

Write-Step "9. staging 결과 확인"

& git status --short --branch

Write-Host ""
Write-Host "[staging 변경 통계]" -ForegroundColor Yellow
& git diff --cached --stat

& git diff --cached --quiet
$DiffExitCode = $LASTEXITCODE

if ($DiffExitCode -eq 0) {
    $HasStagedChanges = $false
    Write-Host ""
    Write-Host "새로 커밋할 파일 변경은 없습니다." -ForegroundColor Green
}
elseif ($DiffExitCode -eq 1) {
    $HasStagedChanges = $true
    Write-Host ""
    Write-Host "커밋할 변경이 staging 되었습니다." -ForegroundColor Green
}
else {
    throw "staging 변경 상태를 확인하지 못했습니다."
}

$ShouldCommit = $Commit.IsPresent -or $Push.IsPresent

if ($ShouldCommit -and $HasStagedChanges) {
    Write-Step "10. 로컬 커밋 생성"

    Invoke-GitChecked -Arguments @(
        "commit",
        "-m",
        $CommitMessage
    )
}
elseif ($ShouldCommit) {
    Write-Host ""
    Write-Host "새 staged 변경이 없어 추가 커밋은 생성하지 않습니다." -ForegroundColor Yellow
}

if ($Push) {
    Write-Step "11. push 직전 원격 상태 재확인"

    Invoke-GitChecked -Arguments @(
        "fetch",
        "--prune",
        "origin"
    )

    $FinalRelation = Get-AheadBehind `
        -LeftReference "HEAD" `
        -RightReference "origin/$BranchName"

    Write-Host "push 대상 로컬 커밋: $($FinalRelation.Ahead)"
    Write-Host "원격에만 있는 커밋: $($FinalRelation.Behind)"

    if ($FinalRelation.Behind -gt 0) {
        throw "push 직전에 원격 신규 커밋이 발견되어 push를 중단했습니다."
    }

    Write-Step "12. origin/main push"

    Invoke-GitChecked -Arguments @(
        "push",
        "--set-upstream",
        "origin",
        $BranchName
    )

    Write-Host ""
    Write-Host "원격 push가 완료되었습니다." -ForegroundColor Green
}

Write-Step "최종 상태"

Show-RepositoryStatus

if (-not $ShouldCommit) {
    Write-Host ""
    Write-Host "현재는 push 준비 단계까지만 완료되었습니다." -ForegroundColor Green
    Write-Host ""
    Write-Host "변경 내용을 검토하십시오:" -ForegroundColor Yellow
    Write-Host "  git diff --cached --stat"
    Write-Host "  git diff --cached"
    Write-Host ""
    Write-Host "검토 후 commit과 push를 실행하십시오:" -ForegroundColor Yellow
    Write-Host "  powershell -NoProfile -ExecutionPolicy Bypass ``"
    Write-Host "    -File `".\scripts\Prepare-ChartKitGit.ps1`" ``"
    Write-Host "    -Commit -Push ``"
    Write-Host "    -CommitMessage `"Sync latest ChartKit project`""
}