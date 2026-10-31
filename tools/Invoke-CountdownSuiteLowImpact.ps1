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
    [ValidateRange(1, 86400)] [int] $StageTimeoutSeconds = 1800,
    # 单实例锁按会话共享（含别的 worktree）。默认被占即拒绝；给秒数则最多等这么久。
    [ValidateRange(0, 86400)] [int] $WaitForLockSeconds = 0,
    # 后台负载预检报忙、用户确认仍要照跑时才加。
    [switch] $SkipLoadPreflight,
    [string] $TauriExecutable,
    [string] $TauriEvidenceDirectory,
    [string] $AppPath,
    [string] $ExpectedDllSha256
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$allowedStages = @('Build', 'GeometryPreflight', 'Core', 'CSharp', 'Ui', 'SourceValidation', 'InstallValidate', 'ValidationHarness', 'Tooling', 'BaselineGenerate', 'TauriGate')
$Stage = @($Stage | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ } | Select-Object -Unique)
$invalid = @($Stage | Where-Object { $_ -notin $allowedStages })
if ($Stage.Count -eq 0 -or $invalid.Count -gt 0) {
    throw "无效 Stage：$($invalid -join ', ')。允许值：$($allowedStages -join ', ')"
}
if ('BaselineGenerate' -in $Stage -and $Stage.Count -ne 1) {
    throw 'BaselineGenerate 会创建新基线，必须单独运行；生成后另行运行回归套件。'
}
if ('GeometryPreflight' -in $Stage) {
    if (-not $AppPath -or -not $ExpectedDllSha256) { throw 'GeometryPreflight 必须显式指定 -AppPath 和 -ExpectedDllSha256。' }
    if ($Stage.Count -ne 1 -or $AllowUnisolatedDesktop) { throw 'GeometryPreflight 必须单独运行并保留隔离桌面。' }
} elseif ($AppPath -or $ExpectedDllSha256) { throw '候选 DLL 参数只能用于 GeometryPreflight。' }
if ('TauriGate' -in $Stage) {
    if ($Stage.Count -ne 1 -or $AllowUnisolatedDesktop) {
        throw 'TauriGate 必须单独运行并保留隔离桌面。'
    }
    if ([string]::IsNullOrWhiteSpace($TauriExecutable) -or [string]::IsNullOrWhiteSpace($TauriEvidenceDirectory)) {
        throw 'TauriGate 必须显式指定 -TauriExecutable 和全新的 -TauriEvidenceDirectory。'
    }
} elseif ($TauriExecutable -or $TauriEvidenceDirectory) {
    throw 'Tauri 参数只能用于 TauriGate 阶段。'
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

# 命名 mutex 查不到持有者：按命令行列出别的启动器进程，只作诊断。排除本进程和祖先，
# 因为调用方（如 Agent 宿主的 pwsh -Command）的命令文本里也会出现脚本名。
function Format-SuiteLauncherHolders {
    $ancestors = [System.Collections.Generic.HashSet[int]]::new()
    $process = Get-Process -Id $PID
    while ($null -ne $process -and $ancestors.Count -lt 32 -and $ancestors.Add($process.Id)) { $process = $process.Parent }
    $holders = @(Get-CimInstance Win32_Process -Filter "Name='pwsh.exe'" -ErrorAction SilentlyContinue |
        Where-Object { -not $ancestors.Contains([int] $_.ProcessId) -and "$($_.CommandLine)" -match 'Invoke-CountdownSuiteLowImpact\.ps1' } |
        ForEach-Object { $_.ProcessId })
    if ($holders.Count -eq 0) { return '未找到别的启动器进程' }
    return "疑似持有者 PID $($holders -join ', ')"
}

# 后台负载采样三次取平均：CPU 总占用，GPU 取最忙的引擎类型（同类引擎跨进程求和），并记下各自最忙的进程。
# GPU 引擎实例随进程增减，偶发 PDH 缓冲区错误，重取即可；计数器一直读不到时返回 $null，不挡运行。
function Measure-SuiteBackgroundLoad {
    $sampleCount = 3
    $cpuPercent = 0.0
    $engineTotals = @{}
    $engineByPid = @{}
    $cpuByProcess = @{}
    for ($sample = 0; $sample -lt $sampleCount; $sample++) {
        $counterSet = $null
        for ($attempt = 1; $null -eq $counterSet; $attempt++) {
            try {
                $counterSet = Get-Counter -ErrorAction Stop -Counter @(
                    '\Processor(_Total)\% Processor Time', '\GPU Engine(*)\Utilization Percentage', '\Process(*)\% Processor Time')
            }
            catch { if ($attempt -ge 3) { return $null } }
        }
        foreach ($counter in $counterSet.CounterSamples) {
            if ($counter.Path -match '\\processor\(_total\)\\') {
                $cpuPercent += $counter.CookedValue / $sampleCount
            }
            elseif ($counter.InstanceName -match '^pid_(\d+)_.*_engtype_(.+)$') {
                $engine = $Matches[2]
                $engineTotals[$engine] += $counter.CookedValue / $sampleCount
                if (-not $engineByPid.ContainsKey($engine)) { $engineByPid[$engine] = @{} }
                $engineByPid[$engine][[int] $Matches[1]] += $counter.CookedValue / $sampleCount
            }
            elseif ($counter.Path -match '\\process\(' -and $counter.InstanceName -notin @('_total', 'idle')) {
                $cpuByProcess[$counter.InstanceName] += $counter.CookedValue / $sampleCount / [Environment]::ProcessorCount
            }
        }
    }
    $busiest = @($engineTotals.GetEnumerator() | Sort-Object Value -Descending | Select-Object -First 1)
    $formatTop = {
        param([hashtable] $byOwner, [scriptblock] $name)
        @($byOwner.GetEnumerator() | Sort-Object Value -Descending | Select-Object -First 3 |
            Where-Object Value -ge 1 | ForEach-Object { '{0} {1:N0}%' -f (& $name $_.Key), $_.Value }) -join '、'
    }
    [pscustomobject]@{
        CpuPercent = $cpuPercent
        CpuTop = & $formatTop $cpuByProcess { param($key) $key }
        GpuEngine = if ($busiest.Count -gt 0) { $busiest[0].Key } else { '3d' }
        GpuPercent = if ($busiest.Count -gt 0) { $busiest[0].Value } else { 0.0 }
        GpuTop = if ($busiest.Count -gt 0) {
            & $formatTop $engineByPid[$busiest[0].Key] {
                param($key) $process = Get-Process -Id $key -ErrorAction Ignore; if ($process) { $process.ProcessName } else { "PID $key" }
            }
        } else { '' }
    }
}

$suiteMutex = [System.Threading.Mutex]::new($false, 'Local\MinminCountdown.SuiteLowImpact')
$mutexHeld = $false
try {
    try { $mutexHeld = $suiteMutex.WaitOne(0) }
    catch [System.Threading.AbandonedMutexException] { $mutexHeld = $true }
    if (-not $mutexHeld -and $WaitForLockSeconds -gt 0) {
        Write-Host "[suite] 已有低影响套件启动器实例在运行（$(Format-SuiteLauncherHolders)），最多等待 $WaitForLockSeconds 秒。"
        $lockDeadline = [datetime]::UtcNow.AddSeconds($WaitForLockSeconds)
        while (-not $mutexHeld -and [datetime]::UtcNow -lt $lockDeadline) {
            # 分段等待，Ctrl+C 能在两段之间生效。
            try { $mutexHeld = $suiteMutex.WaitOne(1000) }
            catch [System.Threading.AbandonedMutexException] { $mutexHeld = $true }
        }
    }
    if (-not $mutexHeld) {
        $waited = if ($WaitForLockSeconds -gt 0) { "等待 $WaitForLockSeconds 秒后仍被占用，" } else { '' }
        throw "已有低影响套件启动器实例在运行（$(Format-SuiteLauncherHolders)），$($waited)拒绝并发。可加 -WaitForLockSeconds <秒> 排队。"
    }
    try { [System.Diagnostics.Process]::GetCurrentProcess().PriorityClass = 'BelowNormal' }
    catch { Write-Warning "无法设置 BelowNormal 优先级：$($_.Exception.Message)" }

    # 挖矿或游戏吃满 CPU/GPU 时，窗口阶段的参考帧 300ms 超时会被误报成回归：开跑前采样，超阈值就列出最忙的进程
    # 并停下。只报告，不结束任何进程；CI 机器专用，不查。
    if (-not $SkipLoadPreflight -and $env:GITHUB_ACTIONS -ne 'true' -and
        @($Stage | Where-Object { $_ -notin @('Build', 'Tooling') }).Count -gt 0) {
        $load = Measure-SuiteBackgroundLoad
        if ($null -eq $load) {
            Write-Host '[suite] 读不到 CPU/GPU 性能计数器，跳过后台负载预检。'
        }
        elseif ($load.CpuPercent -ge 80 -or $load.GpuPercent -ge 50) {
            throw ("后台负载过高，窗口阶段可能把参考帧超时误报成回归：CPU {0:N0}%（{1}），GPU {2} 引擎 {3:N0}%（{4}）。" +
                '请先暂停这些程序再跑；确认照跑时加 -SkipLoadPreflight。启动器不会结束任何进程。') -f
                $load.CpuPercent, $load.CpuTop, $load.GpuEngine, $load.GpuPercent, $load.GpuTop
        }
        else {
            # 放行也留一行：之后某阶段报超时，可以对照开跑时的负载。
            Write-Host ('[suite] 后台负载：CPU {0:N0}%，GPU {1} 引擎 {2:N0}%。' -f $load.CpuPercent, $load.GpuEngine, $load.GpuPercent)
        }
    }

    $repoRoot = Split-Path -Parent $PSScriptRoot
    $pwsh = (Get-Command pwsh -CommandType Application -ErrorAction Stop | Select-Object -First 1).Source
    $stageTable = @{
        Build = @('-NoLogo', '-NoProfile', '-NonInteractive', '-File', (Join-Path $repoRoot 'Build-CountdownApplication.ps1'), '-Clean')
        GeometryPreflight = @('-NoLogo', '-NoProfile', '-NonInteractive', '-STA', '-File', (Join-Path $PSScriptRoot 'Test-CountdownWindowPreflight.ps1'), '-AppPath', $AppPath, '-ExpectedDllSha256', $ExpectedDllSha256)
        Core = @('-NoLogo', '-NoProfile', '-NonInteractive', '-File', (Join-Path $repoRoot 'Countdown.Core.Tests.ps1'))
        CSharp = @('-NoLogo', '-NoProfile', '-NonInteractive', '-STA', '-File', (Join-Path $repoRoot 'Countdown.CSharp.Tests.ps1'))
        Ui = @('-NoLogo', '-NoProfile', '-NonInteractive', '-STA', '-File', (Join-Path $repoRoot 'Countdown.Ui.Tests.ps1'))
        SourceValidation = @('-NoLogo', '-NoProfile', '-NonInteractive', '-STA', '-File', (Join-Path $repoRoot 'Invoke-CountdownSourceValidation.ps1'))
        InstallValidate = @('-NoLogo', '-NoProfile', '-NonInteractive', '-File', (Join-Path $repoRoot 'Install-Countdown.ps1'), '-ValidateOnly')
        ValidationHarness = @('-NoLogo', '-NoProfile', '-NonInteractive', '-STA', '-File', (Join-Path $PSScriptRoot 'Test-PopupValidationContract.ps1'))
        Tooling = @('-NoLogo', '-NoProfile', '-NonInteractive', '-File', (Join-Path $PSScriptRoot 'Test-CountdownTooling.ps1'))
        BaselineGenerate = @('-NoLogo', '-NoProfile', '-NonInteractive', '-STA', '-File', (Join-Path $PSScriptRoot 'New-CountdownVisualBaseline.ps1'))
        TauriGate = @('-NoLogo', '-NoProfile', '-NonInteractive', '-File', (Join-Path $PSScriptRoot 'Test-CountdownTauriGate.ps1'), '-CandidateExecutable', $TauriExecutable, '-EvidenceDirectory', $TauriEvidenceDirectory)
    }
    $specs = @($allowedStages | Where-Object { $_ -in $Stage } | ForEach-Object {
        $spec = [ordered]@{ Stage = $_; FilePath = $pwsh; ArgumentList = $stageTable[$_]; Isolated = [bool]$isolation.Isolated -and $_ -ne 'Tooling' }
        if ($_ -eq 'Build') { $spec.PreflightScript = Join-Path $PSScriptRoot 'Test-CountdownBuildPreflight.ps1' }
        $spec
    })

    $watcher = @{ Process = $null; LeakLog = $null }
    # 办公室部署只安装、不跑测试，上传前按提交核对本机套件证据：开跑与收尾各记一次 HEAD
    # 和工作树是否干净，两次是同一提交且都干净，这轮结果才代表该提交。
    $readSourceState = {
        param([string] $Root)
        $head = @(& git -C $Root rev-parse --verify HEAD 2>$null)
        $headExitCode = $LASTEXITCODE
        $status = @(& git -C $Root --no-optional-locks status --porcelain=v1 --untracked-files=all 2>$null)
        $statusExitCode = $LASTEXITCODE
        [pscustomobject]@{
            Commit = if ($headExitCode -eq 0 -and $head.Count -eq 1) { ([string] $head[0]).Trim() } else { $null }
            Clean = $statusExitCode -eq 0 -and $status.Count -eq 0
        }
    }
    $initialize = {
        param($run)
        $sourceAtStart = & $readSourceState $repoRoot
        $run | Add-Member -NotePropertyName Source -NotePropertyValue ([pscustomobject]@{
                Commit = $sourceAtStart.Commit; CleanAtStart = $sourceAtStart.Clean; CommitAtEnd = $null; CleanAtEnd = $null })
        $watcher.LeakLog = Join-Path $run.LogDirectory "$($run.RunId)-desktop-leaks.log"
        $watcher.Process = Start-Process -FilePath $pwsh -ArgumentList @('-NoLogo', '-NoProfile', '-NonInteractive', '-File', (Join-Path $PSScriptRoot 'Watch-DesktopWindows.ps1'), '-Seconds', '86400', '-SuiteLeakOnly', '-OutputPath', $watcher.LeakLog) -WorkingDirectory $repoRoot -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $run.LogDirectory "$($run.RunId)-watcher.out.log") -RedirectStandardError (Join-Path $run.LogDirectory "$($run.RunId)-watcher.err.log")
    }.GetNewClosure()
    $finalize = {
        param($run)
        try {
            if ($null -ne $watcher.Process) {
                try { if (-not $watcher.Process.HasExited) { $watcher.Process.Kill($true); [void]$watcher.Process.WaitForExit(5000) } }
                finally { $watcher.Process.Dispose() }
            }
        }
        finally {
            if ($null -ne $run.PSObject.Properties['Source']) {
                $sourceAtEnd = & $readSourceState $repoRoot
                $run.Source.CommitAtEnd = $sourceAtEnd.Commit
                $run.Source.CleanAtEnd = $sourceAtEnd.Clean
            }
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
    foreach ($failedStage in @($result.Stages | Where-Object Status -In @('Failed', 'TimedOut'))) {
        $excerpt = Get-SuiteStageFailureExcerpt -Stage $failedStage
        if ($null -eq $excerpt) { continue }
        Write-Host "[suite] $($failedStage.Stage) 失败摘要（$($excerpt.Path)）："
        foreach ($line in $excerpt.Lines) { Write-Host "    $line" }
    }
    exit $result.ExitCode
}
finally {
    if ($mutexHeld) { $suiteMutex.ReleaseMutex() }
    $suiteMutex.Dispose()
}
