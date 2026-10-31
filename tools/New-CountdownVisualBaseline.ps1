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
Import-Module (Join-Path $repoRoot 'Countdown.Validation.psm1') -Force
$visualBaseline = Get-CountdownVisualBaseline -RepositoryRoot $repoRoot
$manifestPaths = foreach ($surface in @($visualBaseline.Main, $visualBaseline.Floating)) {
    $directory = Split-Path -Parent $surface.ManifestPath
    if (Test-Path -LiteralPath $directory) {
        throw "基线目录已存在，拒绝覆盖：$directory。保留历史目录并先更新源码中的基线版本。"
    }
    $surface.ManifestPath
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
    # 生成后的自检必须严格通过，不继承窗口套件的参考基线延后。
    MINMIN_COUNTDOWN_REFERENCE_BASELINE_DEFERRAL_LOG = $null
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
        throw "基线生成失败（exit=$LASTEXITCODE）；诊断：$runDirectory"
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
catch {
    # 应用先写帧与 manifest 再自检：失败时把这次新建的版本目录整体移进诊断目录，assets 里不留半成品，
    # 重跑不用先清理（生成器拒绝覆盖已有目录），现场仍和诊断放在一起。
    $failure = $_
    $moved = foreach ($manifestPath in $manifestPaths) {
        $directory = Split-Path -Parent $manifestPath
        if (-not (Test-Path -LiteralPath $directory)) { continue }
        $destination = Join-Path $runDirectory (Join-Path 'partial' ([System.IO.Path]::GetRelativePath($repoRoot, $directory)))
        try {
            [void][System.IO.Directory]::CreateDirectory((Split-Path -Parent $destination))
            Move-Item -LiteralPath $directory -Destination $destination
            $destination
        }
        catch {
            "（未能移走，仍在原处：$directory；$($_.Exception.Message)）"
        }
    }
    if (@($moved).Count -eq 0) { throw $failure }
    throw "$($failure.Exception.Message)；半成品版本目录已移出 assets：$(@($moved) -join '、')"
}
finally {
    foreach ($name in $previousEnvironment.Keys) {
        [Environment]::SetEnvironmentVariable($name, $previousEnvironment[$name], 'Process')
    }
    if ((Get-RootSettingsFingerprint) -cne $settingsBefore) {
        throw '基线生成改变了权威根 settings.json，验证已闭锁。'
    }
}
