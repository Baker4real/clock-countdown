#requires -Version 7.0
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# 探针 3：真实应用有界 exercise 在不可见桌面中运行（用户屏幕应无任何窗口）
$repoRoot = Split-Path -Parent $PSScriptRoot
$probeSettings = Join-Path $env:TEMP 'minmin-iso-probe-settings.json'
if (Test-Path -LiteralPath $probeSettings) { Remove-Item -LiteralPath $probeSettings -Force }
$stdoutPath = Join-Path $env:TEMP 'minmin-iso-probe.out.log'
$stderrPath = Join-Path $env:TEMP 'minmin-iso-probe.err.log'

$stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
$exitCode = [int] (& (Join-Path $PSScriptRoot 'Start-IsolatedSuiteProcess.ps1') `
    -FilePath (Get-Command pwsh).Source `
    -ArgumentList @(
        '-NoLogo', '-NoProfile', '-NonInteractive', '-STA',
        '-File', (Join-Path $repoRoot '敏敏子倒计时.ps1'),
        '-ValidateOnly', '-ExerciseNoteAdd', '-SettingsFile', $probeSettings) `
    -WorkingDirectory $repoRoot `
    -StdoutPath $stdoutPath -StderrPath $stderrPath | Select-Object -Last 1)
$stopwatch.Stop()

Write-Output "probe3-exit: $exitCode"
Write-Output "probe3-elapsed: $([int] $stopwatch.Elapsed.TotalSeconds)s"
$json = Get-Content -LiteralPath $stdoutPath -Raw
Write-Output "probe3-stdout-head: $($json.Substring(0, [Math]::Min(400, $json.Length)))"
$stderr = Get-Content -LiteralPath $stderrPath -Raw
if (-not [string]::IsNullOrWhiteSpace($stderr)) {
    Write-Output "probe3-stderr-head: $($stderr.Substring(0, [Math]::Min(400, $stderr.Length)))"
}
