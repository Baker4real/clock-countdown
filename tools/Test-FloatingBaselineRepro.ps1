#requires -Version 7.0
# 最小复现：以 SourceTree 模式跑应用 -ValidateOnly，触发悬浮窗与主窗口 v22 基线门。
# -Isolated 时经不可见桌面启动；否则直接在用户桌面跑（窗口可见，约一分钟）。
[CmdletBinding()]
param(
    [switch] $Isolated,
    # 诊断专用：生成模式，把当前实际帧写入（应为空的）v22 基线目录并生成 manifest。
    [switch] $GenerateBaseline
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$settingsPath = Join-Path $env:TEMP 'minmin-baseline-repro-settings.json'
if (Test-Path -LiteralPath $settingsPath) { Remove-Item -LiteralPath $settingsPath -Force }
$diagPath = Join-Path $env:TEMP 'minmin-baseline-repro-diag.jsonl'
$stdoutPath = Join-Path $env:TEMP 'minmin-baseline-repro.out.log'
$stderrPath = Join-Path $env:TEMP 'minmin-baseline-repro.err.log'

$argumentList = @(
    '-NoLogo', '-NoProfile', '-NonInteractive', '-STA',
    '-File', (Join-Path $repoRoot '敏敏子倒计时.ps1'),
    '-ValidateOnly', '-SettingsFile', $settingsPath,
    '-DiagnosticLogPath', $diagPath
)

$stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
if ($Isolated) {
    $env:MINMIN_COUNTDOWN_VISUAL_SOURCE_ROOT = $repoRoot
    if ($GenerateBaseline) {
        $env:MINMIN_COUNTDOWN_FLOATING_BASELINE_GENERATE = '1'
        $env:MINMIN_COUNTDOWN_MAIN_BASELINE_GENERATE = '1'
    }
    $exitCode = [int] (& (Join-Path $PSScriptRoot 'Start-IsolatedSuiteProcess.ps1') `
        -FilePath (Get-Command pwsh).Source `
        -ArgumentList $argumentList `
        -WorkingDirectory $repoRoot `
        -StdoutPath $stdoutPath -StderrPath $stderrPath | Select-Object -Last 1)
}
else {
    $env:MINMIN_COUNTDOWN_VISUAL_SOURCE_ROOT = $repoRoot
    $process = Start-Process -FilePath (Get-Command pwsh).Source `
        -ArgumentList $argumentList `
        -WorkingDirectory $repoRoot `
        -RedirectStandardOutput $stdoutPath -RedirectStandardError $stderrPath `
        -WindowStyle Hidden -PassThru
    $process.WaitForExit()
    $exitCode = $process.ExitCode
}
$stopwatch.Stop()

Write-Output "exit: $exitCode  elapsed: $([int] $stopwatch.Elapsed.TotalSeconds)s  isolated: $([bool] $Isolated)"
$stderr = Get-Content -LiteralPath $stderrPath -Raw
if (-not [string]::IsNullOrWhiteSpace($stderr)) {
    if ($stderr -match 'comparison=\[(.*?)\]') { Write-Output "comparison: $($Matches[1])" }
    if ($stderr -match 'falseGates=\[(.*?)\]') { Write-Output "falseGates: $($Matches[1])" }
}
$stdout = Get-Content -LiteralPath $stdoutPath -Raw
if ($stdout -match '"Valid":(\w+)') { Write-Output "Valid: $($Matches[1])" }
