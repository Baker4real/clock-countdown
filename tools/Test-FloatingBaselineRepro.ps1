#requires -Version 7.0
# 最小复现：以 SourceTree 模式直接启动独立 runner 跑 --validate-only，触发悬浮窗与主窗口现役基线门。
# -Isolated 时经不可见桌面启动；否则直接在用户桌面跑（窗口可见，约一分钟）。
[CmdletBinding()]
param(
    [switch] $Isolated,
    # 诊断专用：生成模式，把当前实际帧写入（应为空的）现役版本基线目录并生成 manifest。
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

$applicationPath = Join-Path $repoRoot 'window-verification\MinminCountdown.WindowVerification.exe'
$argumentList = @(
    '--validate-only', '--settings-file', $settingsPath,
    '--diagnostic-log-path', $diagPath
)

$stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
if ($Isolated) {
    $env:MINMIN_COUNTDOWN_VISUAL_SOURCE_ROOT = $repoRoot
    if ($GenerateBaseline) {
        $env:MINMIN_COUNTDOWN_FLOATING_BASELINE_GENERATE = '1'
        $env:MINMIN_COUNTDOWN_MAIN_BASELINE_GENERATE = '1'
    }
    $exitCode = [int] (& (Join-Path $PSScriptRoot 'Start-IsolatedSuiteProcess.ps1') `
        -FilePath $applicationPath `
        -ArgumentList $argumentList `
        -WorkingDirectory $repoRoot `
        -StdoutPath $stdoutPath -StderrPath $stderrPath | Select-Object -Last 1)
}
else {
    $env:MINMIN_COUNTDOWN_VISUAL_SOURCE_ROOT = $repoRoot
    # 原 -WindowStyle Hidden 只为藏 pwsh 控制台；直接启动 WPF 应用时它会改写首个窗口的显示方式，故不再传。
    $process = Start-Process -FilePath $applicationPath `
        -ArgumentList $argumentList `
        -WorkingDirectory $repoRoot `
        -RedirectStandardOutput $stdoutPath -RedirectStandardError $stderrPath `
        -PassThru
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
