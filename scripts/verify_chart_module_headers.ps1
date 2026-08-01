[CmdletBinding()]
param(
    [string]$SourceRoot = ".\csharp_chartkit\src"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$requiredKeys = @(
    "Module-Id",
    "Module-Class",
    "Module-Category",
    "Registration",
    "Profile-Key",
    "Data-Requirements",
    "Capabilities",
    "Contributions",
    "Default-Panel",
    "Renderer-Path",
    "UI-Path",
    "Persistence",
    "Verification"
)

$expectedRendererPath =
    "ContributionSet -> SceneCompiler -> ChartRenderPlan -> SkiaChartRenderer"

$forbiddenPatterns = @(
    '\bSKCanvas\b',
    '\bSKPaint\b',
    '\bSKPath\b',
    '\bSkiaChartRenderer\s*[.(]',
    '\bSystem\.Windows\.Forms\b',
    '\bControl\b\s+[A-Za-z_]'
)

if (-not (Test-Path -LiteralPath $SourceRoot)) {
    throw "Source root does not exist: $SourceRoot"
}

$moduleRoots = Get-ChildItem -LiteralPath $SourceRoot -Directory |
    Where-Object {
        $_.Name -eq "ChartKit.Modules" -or
        $_.Name -like "ChartKit.Modules.*"
    }

if (-not $moduleRoots) {
    Write-Host "chart_module_header_contract=PASS module_files=0"
    exit 0
}

$moduleFiles = foreach ($root in $moduleRoots) {
    Get-ChildItem -LiteralPath $root.FullName -Recurse -File -Filter "*Module.cs"
}

if (-not $moduleFiles) {
    Write-Host "chart_module_header_contract=PASS module_files=0"
    exit 0
}

$errors = [System.Collections.Generic.List[string]]::new()

foreach ($file in $moduleFiles) {
    $allLines = Get-Content -LiteralPath $file.FullName
    $headerLines = $allLines | Select-Object -First 80
    $header = $headerLines -join "`n"
    $content = $allLines -join "`n"
    $relativePath = Resolve-Path -LiteralPath $file.FullName -Relative
    $expectedClass = [System.IO.Path]::GetFileNameWithoutExtension($file.Name)

    if ($header -notmatch '(?m)^// <chart-module>\s*$') {
        $errors.Add("$relativePath: missing // <chart-module> in first 80 lines")
    }
    if ($header -notmatch '(?m)^// </chart-module>\s*$') {
        $errors.Add("$relativePath: missing // </chart-module> in first 80 lines")
    }

    $metadata = @{}
    foreach ($line in $headerLines) {
        if ($line -match '^//\s*([A-Za-z-]+):\s*(.*?)\s*$') {
            $metadata[$matches[1]] = $matches[2]
        }
    }

    foreach ($key in $requiredKeys) {
        if (-not $metadata.ContainsKey($key) -or
            [string]::IsNullOrWhiteSpace([string]$metadata[$key])) {
            $errors.Add("$relativePath: missing or empty header key '$key'")
        }
    }

    if ($metadata.ContainsKey("Module-Class") -and
        $metadata["Module-Class"] -ne $expectedClass) {
        $errors.Add(
            "$relativePath: Module-Class '$($metadata["Module-Class"])' " +
            "must match file/class '$expectedClass'")
    }

    if ($metadata.ContainsKey("Renderer-Path") -and
        $metadata["Renderer-Path"] -ne $expectedRendererPath) {
        $errors.Add(
            "$relativePath: Renderer-Path must be exactly '$expectedRendererPath'")
    }

    $classPattern =
        "(?s)\bclass\s+" + [regex]::Escape($expectedClass) +
        "\b.*?:.*?\bIChartModule\b"
    if ($content -notmatch $classPattern) {
        $errors.Add("$relativePath: $expectedClass must implement IChartModule")
    }

    if ($content -notmatch
        '\bstatic\s+ChartModuleDefinition\s+Definition\b') {
        $errors.Add(
            "$relativePath: missing static ChartModuleDefinition Definition")
    }

    if ($content -notmatch
        '\bChartModuleDefinition\s+ModuleDefinition\b') {
        $errors.Add(
            "$relativePath: missing public ModuleDefinition exposure")
    }

    foreach ($pattern in $forbiddenPatterns) {
        $matchesFound = [regex]::Matches($content, $pattern)
        if ($matchesFound.Count -gt 0) {
            $errors.Add(
                "$relativePath: forbidden renderer/UI dependency matched '$pattern'")
        }
    }
}

if ($errors.Count -gt 0) {
    foreach ($message in $errors) {
        Write-Error $message
    }
    throw "Chart module file contract verification failed with $($errors.Count) error(s)."
}

Write-Host "chart_module_header_contract=PASS module_files=$($moduleFiles.Count)"
