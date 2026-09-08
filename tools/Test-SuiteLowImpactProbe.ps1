#requires -Version 7.0
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# 探针 1：BelowNormal 父进程的子进程是否继承优先级
[System.Diagnostics.Process]::GetCurrentProcess().PriorityClass = 'BelowNormal'
$childOut = Join-Path $env:TEMP 'prio-child.txt'
$child = Start-Process -FilePath pwsh -ArgumentList @(
    '-NoProfile', '-Command',
    '[System.Diagnostics.Process]::GetCurrentProcess().PriorityClass.ToString()'
) -RedirectStandardOutput $childOut -PassThru
$child.WaitForExit()
$childPriority = (Get-Content -LiteralPath $childOut -Raw).Trim()
Write-Output "probe1-parent: BelowNormal"
Write-Output "probe1-child: $childPriority"

# 探针 2：不可见桌面启动普通子进程
$isoOut = Join-Path $env:TEMP 'iso-child.out.txt'
$isoErr = Join-Path $env:TEMP 'iso-child.err.txt'
$isoExit = [int] (& (Join-Path $PSScriptRoot 'Start-IsolatedSuiteProcess.ps1') `
    -FilePath (Get-Command pwsh).Source `
    -ArgumentList @('-NoProfile', '-Command', "Set-Content -LiteralPath '$isoOut.result' -Value 'iso-ok'") `
    -WorkingDirectory $PSScriptRoot `
    -StdoutPath $isoOut -StderrPath $isoErr | Select-Object -Last 1)
$isoResult = if (Test-Path -LiteralPath "$isoOut.result") { (Get-Content -LiteralPath "$isoOut.result" -Raw).Trim() } else { '<missing>' }
Write-Output "probe2-exit: $isoExit"
Write-Output "probe2-result: $isoResult"
