#requires -Version 7.0
<#
.SYNOPSIS
    低影响官方套件入口。Build 串行门禁通过后，其余阶段最多并行 2 个。
    窗口与控制台默认进入不可见 Win32 桌面，BelowNormal；用户桌面泄漏即失败。
    每次启动立即写 run.json / summary.json，阶段超时默认 1800 秒。
    创建 run.json 中的 CancellationPath 可请求取消；Ctrl+C 也执行终态收口。
    强杀无法执行 finally，下一次启动或 Read-SuiteRun 将失效 owner 的记录标成 Interrupted。
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string[]] $Stage,
    [switch] $IsolatedDesktop,
    [switch] $AllowUnisolatedDesktop,
    [ValidateRange(1, 5)] [int] $MaxParallel = 2,
    [ValidateRange(1, 86400)] [int] $StageTimeoutSeconds = 1800
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$allowedStages = @('Build', 'Core', 'CSharp', 'Ui', 'SourceValidation', 'InstallValidate', 'ValidationHarness', 'Tooling')
$Stage = @($Stage | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ } | Select-Object -Unique)
$invalid = @($Stage | Where-Object { $_ -notin $allowedStages })
if ($Stage.Count -eq 0 -or $invalid.Count -gt 0) {
    throw "无效 Stage：$($invalid -join ', ')。允许值：$($allowedStages -join ', ')"
}
Import-Module (Join-Path $PSScriptRoot 'Countdown.SuiteIsolation.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'Countdown.SuiteRun.psm1') -Force
$isolation = Resolve-SuiteLauncherIsolation `
    -IsolatedDesktopBound:($PSBoundParameters.ContainsKey('IsolatedDesktop')) `
    -IsolatedDesktop:([bool]$IsolatedDesktop) -AllowUnisolatedDesktop:([bool]$AllowUnisolatedDesktop)
if ($isolation.Isolated) { Remove-Item Env:MINMIN_SUITE_ALLOW_UNISOLATED -ErrorAction SilentlyContinue }
else {
    $env:MINMIN_SUITE_ALLOW_UNISOLATED = '1'
    Write-Warning '已显式 -AllowUnisolatedDesktop：窗口会进入用户桌面。用户正在用电脑时禁止。'
}

$suiteMutex = [System.Threading.Mutex]::new($false, 'Local\MinminCountdown.SuiteLowImpact')
$mutexHeld = $false
try {
    try { $mutexHeld = $suiteMutex.WaitOne(0) }
    catch [System.Threading.AbandonedMutexException] { $mutexHeld = $true }
    if (-not $mutexHeld) { throw '已有低影响套件启动器实例在运行，拒绝并发。' }
    try { [System.Diagnostics.Process]::GetCurrentProcess().PriorityClass = 'BelowNormal' }
    catch { Write-Warning "无法设置 BelowNormal 优先级：$($_.Exception.Message)" }

    $repoRoot = Split-Path -Parent $PSScriptRoot
    $pwsh = (Get-Command pwsh -ErrorAction Stop).Source
    $stageTable = @{
        Build = @('-NoLogo', '-NoProfile', '-NonInteractive', '-File', (Join-Path $repoRoot 'Build-CountdownApplication.ps1'), '-Clean')
        Core = @('-NoLogo', '-NoProfile', '-NonInteractive', '-File', (Join-Path $repoRoot 'Countdown.Core.Tests.ps1'))
        CSharp = @('-NoLogo', '-NoProfile', '-NonInteractive', '-STA', '-File', (Join-Path $repoRoot 'Countdown.CSharp.Tests.ps1'))
        Ui = @('-NoLogo', '-NoProfile', '-NonInteractive', '-STA', '-File', (Join-Path $repoRoot 'Countdown.Ui.Tests.ps1'))
        SourceValidation = @('-NoLogo', '-NoProfile', '-NonInteractive', '-STA', '-File', (Join-Path $repoRoot 'Invoke-CountdownSourceValidation.ps1'))
        InstallValidate = @('-NoLogo', '-NoProfile', '-NonInteractive', '-File', (Join-Path $repoRoot 'Install-Countdown.ps1'), '-ValidateOnly')
        ValidationHarness = @('-NoLogo', '-NoProfile', '-NonInteractive', '-STA', '-File', (Join-Path $PSScriptRoot 'Test-PopupValidationContract.ps1'))
        Tooling = @('-NoLogo', '-NoProfile', '-NonInteractive', '-File', (Join-Path $PSScriptRoot 'Test-CountdownTooling.ps1'))
    }
    $specs = @($allowedStages | Where-Object { $_ -in $Stage } | ForEach-Object {
        $spec = [ordered]@{ Stage = $_; FilePath = $pwsh; ArgumentList = $stageTable[$_]; Isolated = [bool]$isolation.Isolated -and $_ -ne 'Tooling' }
        if ($_ -eq 'Build') { $spec.PreflightScript = Join-Path $PSScriptRoot 'Test-CountdownBuildPreflight.ps1' }
        $spec
    })

    $watcher = @{ Process = $null; LeakLog = $null }
    $initialize = {
        param($run)
        $watcher.LeakLog = Join-Path $run.LogDirectory "$($run.RunId)-desktop-leaks.log"
        $watcher.Process = Start-Process -FilePath $pwsh -ArgumentList @('-NoLogo', '-NoProfile', '-NonInteractive', '-File', (Join-Path $PSScriptRoot 'Watch-DesktopWindows.ps1'), '-Seconds', '86400', '-SuiteLeakOnly', '-OutputPath', $watcher.LeakLog) -WorkingDirectory $repoRoot -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $run.LogDirectory "$($run.RunId)-watcher.out.log") -RedirectStandardError (Join-Path $run.LogDirectory "$($run.RunId)-watcher.err.log")
    }.GetNewClosure()
    $finalize = {
        param($run)
        if ($null -ne $watcher.Process) {
            try { if (-not $watcher.Process.HasExited) { $watcher.Process.Kill($true); [void]$watcher.Process.WaitForExit(5000) } }
            finally { $watcher.Process.Dispose() }
        }
    }.GetNewClosure()
    $failureProbe = {
        param($run)
        if ([string]::IsNullOrWhiteSpace($watcher.LeakLog) -or -not [System.IO.File]::Exists($watcher.LeakLog)) { return $false }
        return @([System.IO.File]::ReadAllLines($watcher.LeakLog) | Where-Object { $_ -and $_ -notmatch '^watcher-done' }).Count -gt 0
    }.GetNewClosure()

    $result = Invoke-SuiteRun -StageSpecs $specs -RepositoryRoot $repoRoot -LogDirectory (Join-Path $repoRoot 'logs/suite-runs') -MaxParallel $MaxParallel -StageTimeoutSeconds $StageTimeoutSeconds -Initialize $initialize -Finalize $finalize -FailureProbe $failureProbe
    $result.Stages | Format-Table Stage, ExitCode, Status, ElapsedSeconds, Isolated, DesktopName -AutoSize
    Write-Host "[suite] $($result.Status)；summary：$($result.SummaryPath)"
    if ($result.Failure) { Write-Host "[suite] $($result.Failure)" }
    exit $result.ExitCode
}
finally {
    if ($mutexHeld) { $suiteMutex.ReleaseMutex() }
    $suiteMutex.Dispose()
}
