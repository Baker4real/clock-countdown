#requires -Version 7.0
<#$
.SYNOPSIS
    无窗口验证 SuiteRun 的成功、非零、启动失败、超时、取消和中断回读。
#>
[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$settingsPath = Join-Path $repoRoot 'settings.json'
$settingsBefore = if (Test-Path -LiteralPath $settingsPath -PathType Leaf) {
    $item = Get-Item -LiteralPath $settingsPath
    [pscustomobject]@{ Length = $item.Length; LastWriteTimeUtc = $item.LastWriteTimeUtc; Hash = (Get-FileHash -LiteralPath $settingsPath -Algorithm SHA256).Hash }
} else { $null }
Import-Module (Join-Path $PSScriptRoot 'Countdown.SuiteRun.psm1') -Force
$pwsh = (Get-Command pwsh -ErrorAction Stop).Source

function Assert-Lifecycle {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) { throw "SuiteRun lifecycle 失败：$Message" }
}

function Test-LiveProcessIdentity {
    param([object]$Identity)
    if ($null -eq $Identity) { return $false }
    $process = $null
    try {
        $process = [System.Diagnostics.Process]::GetProcessById([int]$Identity.ProcessId)
        return -not $process.HasExited -and
            $process.StartTime.ToUniversalTime().Ticks -eq ([datetime]$Identity.StartedAtUtc).ToUniversalTime().Ticks
    }
    catch { return $false }
    finally { if ($null -ne $process) { $process.Dispose() } }
}

$tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) ('minmin-suite-run-' + [guid]::NewGuid().ToString('N'))
[void][System.IO.Directory]::CreateDirectory($tempRoot)
try {
    $makeSpec = {
        param([string]$Name, [string]$Code, [string]$File = $pwsh)
        [ordered]@{ Stage = $Name; FilePath = $File; ArgumentList = @('-NoLogo','-NoProfile','-NonInteractive','-Command',$Code); Isolated = $false }
    }
    $run = {
        param([object[]]$Specs, [int]$Timeout = 10, [scriptblock]$Initialize = $null)
        Invoke-SuiteRun -StageSpecs $Specs -RepositoryRoot $repoRoot -LogDirectory (Join-Path $tempRoot ([guid]::NewGuid().ToString('N'))) -MaxParallel 1 -StageTimeoutSeconds $Timeout -Initialize $Initialize
    }

    $success = & $run @(& $makeSpec 'Success' 'exit 0')
    Assert-Lifecycle ($success.Status -eq 'Completed' -and $success.ExitCode -eq 0 -and $success.Stages[0].Status -eq 'Completed') '成功阶段应完成且退出码为 0'

    $isolatedSpec = [ordered]@{
        Stage = 'IsolatedSuccess'; FilePath = $pwsh
        ArgumentList = @('-NoLogo', '-NoProfile', '-NonInteractive', '-Command', 'exit 0')
        Isolated = $true
    }
    $isolatedSuccess = & $run @($isolatedSpec)
    Assert-Lifecycle ($isolatedSuccess.Status -eq 'Completed' -and $isolatedSuccess.ExitCode -eq 0 -and $isolatedSuccess.Stages[0].Isolated) '隔离阶段应经 Start-IsolatedSuiteProcess 完成'

    $preflightPath = Join-Path $tempRoot 'preflight.ps1'
    @'
[CmdletBinding()]
param([string]$RepositoryRoot, [switch]$RequireClean, [switch]$Json)
[pscustomobject]@{ Valid = $true; Checks = @([pscustomobject]@{ Name = 'fake'; Valid = $true }); Failures = @() } | ConvertTo-Json -Depth 8
exit 0
'@ | Set-Content -LiteralPath $preflightPath -Encoding UTF8
    $preflightSpec = [ordered]@{
        Stage = 'Preflight'; FilePath = $pwsh
        ArgumentList = @('-NoLogo', '-NoProfile', '-NonInteractive', '-Command', 'exit 0')
        Isolated = $false; PreflightScript = $preflightPath
    }
    $preflightResult = & $run @($preflightSpec)
    $preflightArtifact = Get-Content -LiteralPath $preflightResult.Stages[0].PreflightPath -Raw -Encoding UTF8 | ConvertFrom-Json
    Assert-Lifecycle ($preflightResult.Status -eq 'Completed' -and [bool]$preflightArtifact.Valid) 'Build 预检多行 JSON 应被保存并解析'

    $failingPreflightPath = Join-Path $tempRoot 'failing-preflight.ps1'
    $buildMarkerPath = Join-Path $tempRoot 'build-started.marker'
    @'
[CmdletBinding()]
param([string]$RepositoryRoot, [switch]$RequireClean, [switch]$Json)
[pscustomobject]@{ Valid = $false; Checks = @([pscustomobject]@{ Name = 'fake'; Valid = $false }); Failures = @('preflight fixture failure') } | ConvertTo-Json -Depth 8
exit 1
'@ | Set-Content -LiteralPath $failingPreflightPath -Encoding UTF8
    $failingPreflightSpec = [ordered]@{
        Stage = 'BuildPreflightFailure'; FilePath = $pwsh
        ArgumentList = @('-NoLogo', '-NoProfile', '-NonInteractive', '-Command', "[IO.File]::WriteAllText('$($buildMarkerPath.Replace("'", "''"))','build-started')")
        Isolated = $false; PreflightScript = $failingPreflightPath
    }
    $failingPreflightResult = & $run @($failingPreflightSpec)
    $failingArtifact = Get-Content -LiteralPath $failingPreflightResult.Stages[0].PreflightPath -Raw -Encoding UTF8 | ConvertFrom-Json
    Assert-Lifecycle ($failingPreflightResult.Status -eq 'Failed' -and -not [bool]$failingArtifact.Valid) '失败预检应形成非成功终态并保留 artifact'
    Assert-Lifecycle (-not (Test-Path -LiteralPath $buildMarkerPath -PathType Leaf)) '失败预检不得启动 Build 命令'
    Write-Output ("preflight-failure: PASS artifact={0} build-not-started=true" -f $failingPreflightResult.Stages[0].PreflightPath)

    # 真正的 owner 硬中断：只终止本轮 owner Process，不递归杀树；wrapper 必须发现 owner identity 失效，
    # 关闭自己的 Job，避免隔离子进程成为孤儿。这个 fixture 不代表用户 Ctrl+C，只覆盖宿主异常终止后的回读。
    $ownerHandshakePath = Join-Path $tempRoot 'owner-handshake.json'
    $ownerFixturePath = Join-Path $tempRoot 'owner-fixture.ps1'
    @'
#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$HandshakePath,
    [Parameter(Mandatory)][string]$RepositoryRoot,
    [Parameter(Mandatory)][string]$LogDirectory,
    [Parameter(Mandatory)][string]$SuiteModulePath,
    [Parameter(Mandatory)][string]$PwshPath
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module -Name $SuiteModulePath -Force
$spec = [ordered]@{
    Stage = 'HardInterrupt'; FilePath = $PwshPath
    ArgumentList = @('-NoLogo', '-NoProfile', '-NonInteractive', '-Command', 'Start-Sleep -Seconds 60')
    Isolated = $true
}
$initialize = {
    param($run)
    $owner = [System.Diagnostics.Process]::GetCurrentProcess()
    $payload = [ordered]@{
        StatePath = $run.StatePath
        Owner = [ordered]@{ ProcessId = $owner.Id; StartedAtUtc = $owner.StartTime.ToUniversalTime().ToString('o') }
    }
    [System.IO.File]::WriteAllText($HandshakePath, ($payload | ConvertTo-Json -Depth 8), [System.Text.UTF8Encoding]::new($false))
}.GetNewClosure()
Invoke-SuiteRun -StageSpecs @($spec) -RepositoryRoot $RepositoryRoot -LogDirectory $LogDirectory -MaxParallel 1 -StageTimeoutSeconds 60 -Initialize $initialize | Out-Null
'@ | Set-Content -LiteralPath $ownerFixturePath -Encoding UTF8
    $ownerStdout = Join-Path $tempRoot 'owner.out.log'
    $ownerStderr = Join-Path $tempRoot 'owner.err.log'
    $owner = Start-Process -FilePath $pwsh -ArgumentList @(
        '-NoLogo', '-NoProfile', '-NonInteractive', '-File', $ownerFixturePath,
        '-HandshakePath', $ownerHandshakePath, '-RepositoryRoot', $repoRoot,
        '-LogDirectory', (Join-Path $tempRoot 'hard-interrupt-run'),
        '-SuiteModulePath', (Join-Path $PSScriptRoot 'Countdown.SuiteRun.psm1'), '-PwshPath', $pwsh
    ) -WorkingDirectory $repoRoot -WindowStyle Hidden -PassThru -RedirectStandardOutput $ownerStdout -RedirectStandardError $ownerStderr
    try { $owner.PriorityClass = 'BelowNormal' } catch { }
    $handshakeDeadline = [datetime]::UtcNow.AddSeconds(15)
    while (-not (Test-Path -LiteralPath $ownerHandshakePath -PathType Leaf) -and [datetime]::UtcNow -lt $handshakeDeadline) { Start-Sleep -Milliseconds 100 }
    Assert-Lifecycle (Test-Path -LiteralPath $ownerHandshakePath -PathType Leaf) '硬中断 fixture 未写出 handshake'
    $handshake = Get-Content -LiteralPath $ownerHandshakePath -Raw -Encoding UTF8 | ConvertFrom-Json
    $statePath = [string]$handshake.StatePath
    $running = $null
    $runningDeadline = [datetime]::UtcNow.AddSeconds(10)
    while ([datetime]::UtcNow -lt $runningDeadline) {
        $running = Get-Content -LiteralPath $statePath -Raw -Encoding UTF8 | ConvertFrom-Json
        if ($running.Status -eq 'Running' -and $running.Stages[0].Status -eq 'Running') { break }
        Start-Sleep -Milliseconds 100
    }
    Assert-Lifecycle ($running.Status -eq 'Running' -and $running.Stages[0].Status -eq 'Running') 'owner 硬中断前 run/stage 未处于 Running'
    Assert-Lifecycle (Test-LiveProcessIdentity $running.Owner) 'run owner identity 在硬中断前不存活'
    Assert-Lifecycle (Test-LiveProcessIdentity $running.Stages[0].Process) '隔离 wrapper identity 在硬中断前不存活'
    $childIdentityDeadline = [datetime]::UtcNow.AddSeconds(10)
    while (($null -eq $running.Stages[0].ChildProcess) -and [datetime]::UtcNow -lt $childIdentityDeadline) {
        Start-Sleep -Milliseconds 100
        $running = Get-Content -LiteralPath $statePath -Raw -Encoding UTF8 | ConvertFrom-Json
    }
    Assert-Lifecycle ($null -ne $running.Stages[0].ChildProcess) '隔离 child identity 未写入 run state'
    Assert-Lifecycle (Test-LiveProcessIdentity $running.Stages[0].ChildProcess) '隔离 child 在 owner 硬中断前不存活'
    $stageProcess = [System.Diagnostics.Process]::GetProcessById([int]$running.Stages[0].Process.ProcessId)
    try {
        # 只 Kill() 当前 owner；不使用 Kill($true)、taskkill、PID/name 查杀。
        $owner.Kill()
        [void]$owner.WaitForExit(5000)
        $recovered = Read-SuiteRun -Path $statePath
        Assert-Lifecycle ($recovered.Status -eq 'Interrupted' -and $recovered.ExitCode -eq 1) 'owner 硬中断后未回读为 Interrupted/非成功'
        $orphanDeadline = [datetime]::UtcNow.AddSeconds(8)
        while ((Test-LiveProcessIdentity $recovered.Stages[0].Process) -or (Test-LiveProcessIdentity $recovered.Stages[0].ChildProcess)) {
            if ([datetime]::UtcNow -ge $orphanDeadline) { break }
            Start-Sleep -Milliseconds 100
        }
        Assert-Lifecycle (-not (Test-LiveProcessIdentity $recovered.Stages[0].Process)) 'owner 死亡后隔离 wrapper 仍存活，可能形成孤儿'
        Assert-Lifecycle (-not (Test-LiveProcessIdentity $recovered.Stages[0].ChildProcess)) 'owner 死亡后隔离 child 仍存活，可能形成孤儿'
        Write-Output ("hard-interrupt: PASS ownerPid={0} wrapperPid={1} childPid={2} recovered={3}/{4} orphan=none evidence={5}" -f `
            $running.Owner.ProcessId, $running.Stages[0].Process.ProcessId, $running.Stages[0].ChildProcess.ProcessId,
            $recovered.Status, $recovered.ExitCode, $statePath)
    }
    finally {
        if (Test-LiveProcessIdentity $running.Owner) { try { $owner.Kill() } catch { } }
        if (Test-LiveProcessIdentity $running.Stages[0].Process) { try { $stageProcess.Kill($true) } catch { } }
    }
    $owner.Dispose()
    $stageProcess.Dispose()

    $failed = & $run @(& $makeSpec 'NonZero' 'exit 7')
    Assert-Lifecycle ($failed.Status -eq 'Failed' -and $failed.ExitCode -eq 1 -and $failed.Stages[0].ExitCode -eq 7) '非零阶段应失败并保留退出码'

    $startFailed = & $run @(& $makeSpec 'StartFailed' 'exit 0' (Join-Path $tempRoot 'missing-pwsh.exe'))
    Assert-Lifecycle ($startFailed.Status -eq 'Failed' -and $startFailed.ExitCode -eq 1 -and $startFailed.Stages[0].Status -eq 'Failed') '启动异常应形成失败终态'

    $timedOut = & $run @(& $makeSpec 'Timeout' 'Start-Sleep -Seconds 5') 1
    Assert-Lifecycle ($timedOut.Status -eq 'TimedOut' -and $timedOut.ExitCode -eq 1 -and $timedOut.Stages[0].Status -eq 'TimedOut') '超时应形成 TimedOut 终态'

    $cancelProcess = $null
    $cancelInit = {
        param($run)
        $escapedPath = $run.CancellationPath.Replace("'", "''")
        $cancelProcess = Start-Process -FilePath $pwsh -ArgumentList @(
            '-NoLogo', '-NoProfile', '-NonInteractive', '-Command',
            "[Threading.Thread]::Sleep(250); [IO.File]::WriteAllText('$escapedPath','cancel',[Text.UTF8Encoding]::new(`$false))"
        ) -WindowStyle Hidden -PassThru
    }
    $cancelled = & $run @(& $makeSpec 'Cancelled' 'Start-Sleep -Seconds 5') 10 $cancelInit
    if ($null -ne $cancelProcess -and -not $cancelProcess.HasExited) { $cancelProcess.Kill($true); [void]$cancelProcess.WaitForExit(5000) }
    Assert-Lifecycle ($cancelled.Status -eq 'Cancelled' -and $cancelled.ExitCode -eq 1 -and $cancelled.Stages[0].Status -eq 'Cancelled') '取消应回收已启动阶段并形成非成功终态'

    $interruptedDir = Join-Path $tempRoot 'interrupted'
    [void][System.IO.Directory]::CreateDirectory($interruptedDir)
    $interruptedPath = Join-Path $interruptedDir 'interrupted-run.json'
    $interrupted = [pscustomobject]@{
        SchemaVersion = 1; RunId = 'interrupted'; RepositoryRoot = $repoRoot; LogDirectory = $interruptedDir
        StatePath = $interruptedPath; SummaryPath = (Join-Path $interruptedDir 'interrupted-summary.json');
        CancellationPath = (Join-Path $interruptedDir 'cancel.request'); Owner = [pscustomobject]@{ ProcessId = 1; StartedAtUtc = '2000-01-01T00:00:00.0000000Z' }
        Status = 'Running'; ExitCode = $null; Failure = $null; StartedAtUtc = '2000-01-01T00:00:00Z'; FinishedAtUtc = $null; UpdatedAtUtc = $null
        Stages = @([pscustomobject]@{ Stage = 'Success'; Status = 'Running'; ExitCode = $null; Failure = $null; FinishedAtUtc = $null })
    }
    [System.IO.File]::WriteAllText($interruptedPath, (ConvertTo-Json $interrupted -Depth 8), [System.Text.UTF8Encoding]::new($false))
    $recovered = Read-SuiteRun -Path $interruptedPath
    Assert-Lifecycle ($recovered.Status -eq 'Interrupted' -and $recovered.ExitCode -eq 1 -and $recovered.Stages[0].Status -eq 'Interrupted') '失效 owner 的 Running 记录应回读为 Interrupted'

    if ($null -ne $settingsBefore) {
        $settingsAfterItem = Get-Item -LiteralPath $settingsPath
        $settingsAfter = [pscustomobject]@{ Length = $settingsAfterItem.Length; LastWriteTimeUtc = $settingsAfterItem.LastWriteTimeUtc; Hash = (Get-FileHash -LiteralPath $settingsPath -Algorithm SHA256).Hash }
        Assert-Lifecycle ($settingsAfter.Length -eq $settingsBefore.Length -and $settingsAfter.LastWriteTimeUtc -eq $settingsBefore.LastWriteTimeUtc -and $settingsAfter.Hash -eq $settingsBefore.Hash) '根 settings.json 指纹必须守恒'
    }
    Write-Output 'PASS: success, nonzero, start-failure, timeout, cancellation, interrupted-readback'
}
finally {
    if (Test-Path -LiteralPath $tempRoot) { Remove-Item -LiteralPath $tempRoot -Recurse -Force }
}
