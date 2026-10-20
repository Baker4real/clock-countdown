#requires -Version 7.0
# 隔离启动器的崩溃对话框看门狗：子进程在隔离桌面弹出“<进程> has stopped working”时，
# Start-IsolatedSuiteProcess 应立即以退出码 126 结束它并把标题与正文写进 stderr 日志，而不是挂到超时。
# 对话框只弹在启动器自建的隔离桌面上，用户桌面看不到；可在用户 Default 桌面直跑。
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$workRoot = Join-Path ([System.IO.Path]::GetTempPath()) 'minmin-crash-dialog-watchdog'
[void] [System.IO.Directory]::CreateDirectory($workRoot)
$stdoutPath = Join-Path $workRoot 'probe.out.log'
$stderrPath = Join-Path $workRoot 'probe.err.log'
# 与输入法注入的 CrashRpt 在 CrashSender.exe 缺失时弹出的对话框同形：标准 MessageBox。
$probeCommand = "Add-Type -AssemblyName System.Windows.Forms; " +
    "[void] [System.Windows.Forms.MessageBox]::Show('Error launching CrashSender.exe', 'CrashDialogProbe has stopped working')"

$watch = [System.Diagnostics.Stopwatch]::StartNew()
$exitCode = [int] (& (Join-Path $PSScriptRoot 'Start-IsolatedSuiteProcess.ps1') `
    -FilePath (Get-Process -Id $PID).Path `
    -ArgumentList @('-NoLogo', '-NoProfile', '-NonInteractive', '-Command', $probeCommand) `
    -WorkingDirectory $workRoot `
    -StdoutPath $stdoutPath `
    -StderrPath $stderrPath `
    -DesktopName 'MinminSuiteIso-CrashDialogProbe' `
    -TimeoutSeconds 60 | Select-Object -Last 1)
$elapsed = $watch.Elapsed.TotalSeconds
$stderr = [System.IO.File]::ReadAllText($stderrPath, [System.Text.UTF8Encoding]::new($false))

$failures = [System.Collections.Generic.List[string]]::new()
if ($exitCode -ne 126) { $failures.Add("退出码应为 126，实际 $exitCode") }
if ($elapsed -ge 30) { $failures.Add(('应在 30 秒内结束，实际 {0:N1} 秒' -f $elapsed)) }
if ($stderr -notmatch 'CrashDialogProbe has stopped working' -or $stderr -notmatch 'Error launching CrashSender\.exe') {
    $failures.Add("stderr 未记录对话框标题与正文：$stderr")
}
if ($failures.Count -gt 0) {
    throw "crash-dialog-watchdog 失败：$($failures -join '；')"
}
Write-Output ('crash-dialog-watchdog: PASS（{0:N1} 秒）' -f $elapsed)
