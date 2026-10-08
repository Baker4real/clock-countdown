#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $CandidateExecutable,
    [Parameter(Mandatory)] [string] $EvidenceDirectory
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
Import-Module (Join-Path $PSScriptRoot 'Countdown.SuiteIsolation.psm1') -Force
if (-not (Test-SuiteIsolatedDesktopName (Get-SuiteThreadDesktopName))) {
    throw 'TauriGate 只能经官方隔离桌面启动器运行。'
}
if (-not [IO.Path]::IsPathFullyQualified($CandidateExecutable) -or
    -not (Test-Path -LiteralPath $CandidateExecutable -PathType Leaf) -or
    [IO.Path]::GetFileName($CandidateExecutable) -ine 'minmin-countdown-slice.exe') {
    throw '必须显式指定实际 minmin-countdown-slice.exe 的绝对路径。'
}
if (-not [IO.Path]::IsPathFullyQualified($EvidenceDirectory) -or (Test-Path -LiteralPath $EvidenceDirectory)) {
    throw 'TauriGate 证据目录必须是尚不存在的绝对路径。'
}
$application = (Resolve-Path -LiteralPath $CandidateExecutable).Path
$applicationHash = (Get-FileHash -LiteralPath $application -Algorithm SHA256).Hash
$repoRoot = Split-Path -Parent $PSScriptRoot
$settingsPath = Join-Path $repoRoot 'settings.json'
function Get-SettingsFingerprint {
    if (-not (Test-Path -LiteralPath $settingsPath -PathType Leaf)) { return $null }
    $item = Get-Item -LiteralPath $settingsPath
    [ordered]@{ Length = $item.Length; LastWriteTimeUtc = $item.LastWriteTimeUtc.ToString('O'); Sha256 = (Get-FileHash -LiteralPath $settingsPath).Hash } | ConvertTo-Json -Compress
}
$settingsBefore = Get-SettingsFingerprint
$stateDirectory = Join-Path $EvidenceDirectory 'state'
$capturesDirectory = Join-Path $EvidenceDirectory 'captures'
[void][IO.Directory]::CreateDirectory($stateDirectory)
[void][IO.Directory]::CreateDirectory($capturesDirectory)
$environment = @{
    MINMIN_TAURI_STATE_DIR = $stateDirectory
    MINMIN_TAURI_EVIDENCE_DIR = $capturesDirectory
}
$previousEnvironment = @{}
$startedAt = [DateTime]::UtcNow
$exitCode = $null
$valid = $false
try {
    foreach ($name in $environment.Keys) {
        $previousEnvironment[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
        [Environment]::SetEnvironmentVariable($name, $environment[$name], 'Process')
    }
    # The official parent owns the isolated desktop, process Job, timeout and
    # cancellation. Do not kill or activate any pre-existing slice process.
    & $application '--floating-gate' 1> (Join-Path $EvidenceDirectory 'application.out.log') 2> (Join-Path $EvidenceDirectory 'application.err.log')
    $exitCode = $LASTEXITCODE
    if ($exitCode -ne 0) { throw "Tauri 切片退出码 $exitCode；保留全部运行证据。" }
    $receiptPath = Join-Path $capturesDirectory 'floating-gate-receipt.json'
    $receipt = Get-Content -LiteralPath $receiptPath -Raw | ConvertFrom-Json
    if ($receipt.schemaVersion -ne 1 -or $receipt.status -cne 'passed' -or
        $receipt.windowCount -ne 1 -or $receipt.width -ne 520 -or $receipt.height -ne 390 -or
        $receipt.criticalInteractionsPassed -ne $true) {
        throw 'Tauri 单浮窗或关键交互回执未通过。'
    }
    $expectedIds = @('floating-countdowns-day', 'long-notes-day-top', 'long-notes-day-bottom', 'notes-search-expanded-day')
    if (@($receipt.captures).Count -ne 4 -or
        @(Compare-Object $expectedIds @($receipt.captures.captureId)).Count -ne 0 -or
        @($receipt.captures.path | Sort-Object -Unique).Count -ne 4) {
        throw 'Tauri 闸门必须包含四个互不重复的悬浮场景。'
    }
    foreach ($capture in $receipt.captures) {
        $name = [string]$capture.path
        if ([IO.Path]::GetFileName($name) -cne $name -or [IO.Path]::GetExtension($name) -ine '.png') {
            throw 'Tauri 捕获路径必须是证据目录内的 PNG 文件名。'
        }
        $path = Join-Path $capturesDirectory $name
        $png = [IO.File]::ReadAllBytes($path)
        if ($png.Length -lt 24 -or [Convert]::ToHexString($png[0..7]) -cne '89504E470D0A1A0A') { throw "不是有效 PNG：$name" }
        $width = [uint32]$png[16] * 16777216 + [uint32]$png[17] * 65536 + [uint32]$png[18] * 256 + [uint32]$png[19]
        $height = [uint32]$png[20] * 16777216 + [uint32]$png[21] * 65536 + [uint32]$png[22] * 256 + [uint32]$png[23]
        if ($width -ne 520 -or $height -ne 390 -or $capture.width -ne $width -or $capture.height -ne $height -or
            (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ine $capture.sha256) {
            throw "Tauri 捕获尺寸或 SHA-256 不符：$name"
        }
    }
    if ((Get-FileHash -LiteralPath $application -Algorithm SHA256).Hash -cne $applicationHash) { throw '实验期间候选 EXE 发生变化。' }
    $valid = $true
}
finally {
    foreach ($name in $previousEnvironment.Keys) {
        [Environment]::SetEnvironmentVariable($name, $previousEnvironment[$name], 'Process')
    }
    $settingsAfter = Get-SettingsFingerprint
    $settingsUnchanged = $settingsBefore -ceq $settingsAfter
    [ordered]@{
        Valid = $valid -and $settingsUnchanged
        ApplicationPath = $application
        ApplicationSha256 = $applicationHash
        StartedAtUtc = $startedAt.ToString('O')
        FinishedAtUtc = [DateTime]::UtcNow.ToString('O')
        ExitCode = $exitCode
        SettingsUnchanged = $settingsUnchanged
        CaptureReceipt = Join-Path $capturesDirectory 'floating-gate-receipt.json'
    } | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $EvidenceDirectory 'suite-proof.json') -Encoding utf8
    if (-not $settingsUnchanged) { throw 'TauriGate 改变了权威根 settings.json，验证已闭锁。' }
}
Write-Output "TauriGate: PASS; evidence=$EvidenceDirectory"
