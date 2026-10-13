#requires -Version 7.0
[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)
Import-Module (Join-Path $PSScriptRoot 'Countdown.SuiteIsolation.psm1') -Force
Assert-SuiteIsolatedDesktop

$repoRoot = Split-Path -Parent $PSScriptRoot
$applicationPath = Join-Path $repoRoot 'app/MinminCountdown.exe'
if (-not (Test-Path -LiteralPath $applicationPath -PathType Leaf)) {
    throw "未找到应用：$applicationPath。请先构建当前源码。"
}
$validationSource = Get-Content (Join-Path $repoRoot 'src/MinminCountdown/ValidationReport.cs') -Raw
$manifestPaths = foreach ($surface in @('Main', 'Floating')) {
    $versionPattern = 'const string {0}ReferenceBaselineVersion = "(?<version>[0-9]{{4}}-[0-9]{{2}}-[0-9]{{2}}-v[0-9]+)"' -f $surface
    $versionMatch = [regex]::Match($validationSource, $versionPattern)
    if (-not $versionMatch.Success) { throw "无法读取 $surface 当前基线版本。" }
    $relativeRoot = if ($surface -eq 'Main') { 'main-window/864x1000' } else { 'floating-window/520x390' }
    $directory = Join-Path $repoRoot "assets/ui-references/$relativeRoot/$($versionMatch.Groups['version'].Value)"
    if (Test-Path -LiteralPath $directory) {
        throw "基线目录已存在，拒绝覆盖：$directory。保留历史目录并先更新源码中的基线版本。"
    }
    Join-Path $directory 'manifest.json'
}

function Get-RootSettingsFingerprint {
    $path = Join-Path $repoRoot 'settings.json'
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { return $null }
    $item = Get-Item -LiteralPath $path
    [ordered]@{
        Length = $item.Length
        LastWriteTimeUtc = $item.LastWriteTimeUtc.ToString('O')
        Sha256 = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
    } | ConvertTo-Json -Compress
}

$settingsBefore = Get-RootSettingsFingerprint
$applicationSha256 = (Get-FileHash -LiteralPath $applicationPath -Algorithm SHA256).Hash
$runDirectory = Join-Path $repoRoot ("logs/visual-baseline-generation/" + [guid]::NewGuid().ToString('N'))
[void][System.IO.Directory]::CreateDirectory($runDirectory)
$stdoutPath = Join-Path $runDirectory 'validation.json'
$stderrPath = Join-Path $runDirectory 'validation.err.log'
$environment = @{
    MINMIN_COUNTDOWN_VISUAL_SOURCE_ROOT = $repoRoot
    MINMIN_COUNTDOWN_MAIN_BASELINE_GENERATE = '1'
    MINMIN_COUNTDOWN_FLOATING_BASELINE_GENERATE = '1'
    MINMIN_COUNTDOWN_EMPTY_DAY_PROBE = $null
    MINMIN_COUNTDOWN_VISUAL_EVIDENCE_DIR = Join-Path $runDirectory 'captures'
}
$previousEnvironment = @{}
try {
    foreach ($name in $environment.Keys) {
        $previousEnvironment[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
        [Environment]::SetEnvironmentVariable($name, $environment[$name], 'Process')
    }
    & $applicationPath '--validate-only' '--settings-file' (Join-Path $runDirectory 'settings.json') `
        '--diagnostic-log-path' (Join-Path $runDirectory 'application.jsonl') 2> $stderrPath |
        Set-Content -LiteralPath $stdoutPath -Encoding utf8
    if ($LASTEXITCODE -ne 0) {
        throw "基线生成失败（exit=$LASTEXITCODE）；保留已生成内容与诊断：$runDirectory"
    }
    $report = Get-Content -LiteralPath $stdoutPath -Raw | ConvertFrom-Json
    if ($report.Valid -ne $true -or $report.MainReferenceBaselineValidated -ne $true -or
        $report.FloatingReferenceBaselineValidated -ne $true) {
        throw "生成报告未通过双基线验证：$stdoutPath"
    }
    if ((Get-FileHash -LiteralPath $applicationPath -Algorithm SHA256).Hash -cne $applicationSha256) {
        throw '基线生成期间应用 EXE 发生变化。'
    }
    foreach ($manifestPath in $manifestPaths) {
        $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
        if ($manifest.executable.sha256 -cne $applicationSha256) {
            throw "基线未绑定实际执行的应用 EXE：$manifestPath"
        }
    }
    [pscustomobject]@{
        Valid = $true
        ApplicationSha256 = $applicationSha256
        Manifests = @($manifestPaths)
        ValidationReport = $stdoutPath
    } | ConvertTo-Json -Depth 4
}
finally {
    foreach ($name in $previousEnvironment.Keys) {
        [Environment]::SetEnvironmentVariable($name, $previousEnvironment[$name], 'Process')
    }
    if ((Get-RootSettingsFingerprint) -cne $settingsBefore) {
        throw '基线生成改变了权威根 settings.json，验证已闭锁。'
    }
}
