#requires -Version 7.0
<#
.SYNOPSIS
    低影响官方套件启动器：前提假设是「用户正在使用这台电脑」。
    整棵套件进程树以 BelowNormal 优先级运行（子进程自动继承），
    窗口套件默认放入不可见 Win32 桌面，不上用户屏幕、不抢焦点。
    -IsolatedDesktop 仍可显式传入（兼容旧命令）。关闭隔离必须
    -AllowUnisolatedDesktop；用户正在用电脑时禁止。官方内层命令由启动器转发。
    隔离桌面名按阶段唯一化（MinminSuiteIso-<Stage>），阶段间无同名桌面冲突。
    #127：Build 仍串行先行作门禁（Ui/adapter 依赖 Build 产物，不得早于 Build）；
    Build 绿之后其余阶段按 -MaxParallel（默认 2，保守）并行扇出。
    任一阶段红即取消全部在跑阶段（taskkill /T 强杀阶段进程树，含隔离桌面子进程），
    保留已完成阶段日志与 summary；未启动的剩余阶段不再调度（与串行红即停 break 语义一致）。
    -IsolatedDesktop 时隔离阶段经每阶段生成的 bootstrap 脚本
    （logs\suite-runs\<stamp>-<Stage>.iso.ps1）在包装进程内以真实数组绑定调用
    Start-IsolatedSuiteProcess.ps1（#123 合同不变），包装进程退出码即测试子进程退出码，
    启动器自身失败归一为退出码 1；包装进程自身输出落 <stamp>-<Stage>.iso.out.log / .iso.err.log。
    summary.json 在既有列基础上只增列 Status / LauncherStdout / LauncherStderr。
#>
[CmdletBinding()]
param(
    # 原生 argv 传入时逗号串是单个元素，故在函数体内自行拆分并校验。
    [Parameter(Mandatory)]
    [string[]] $Stage,

    [switch] $IsolatedDesktop,

    # 显式关闭桌面隔离。用户正在用电脑时禁止。
    [switch] $AllowUnisolatedDesktop,

    # Build 门禁之后并行扇出的最大同时阶段数；默认 2（用户可能同时使用电脑）。
    [ValidateRange(1, 5)]
    [int] $MaxParallel = 2
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$allowedStages = @('Build', 'Core', 'CSharp', 'Ui', 'SourceValidation', 'InstallValidate')
$Stage = @($Stage | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ })
$invalid = @($Stage | Where-Object { $_ -notin $allowedStages })
if ($Stage.Count -eq 0 -or $invalid.Count -gt 0) {
    throw "无效 Stage：$($invalid -join ', ')。允许值：$($allowedStages -join ', ')"
}
# 并行调度按每阶段唯一桌面/唯一日志设计，重复阶段去重并保持首次出现顺序。
$Stage = @($Stage | Select-Object -Unique)

Import-Module -Name (Join-Path $PSScriptRoot 'Countdown.SuiteIsolation.psm1') -Force
$script:SuiteIsolation = Resolve-SuiteLauncherIsolation `
    -IsolatedDesktopBound:($PSBoundParameters.ContainsKey('IsolatedDesktop')) `
    -IsolatedDesktop:([bool] $IsolatedDesktop) `
    -AllowUnisolatedDesktop:([bool] $AllowUnisolatedDesktop)
if ($script:SuiteIsolation.Isolated) {
    Remove-Item Env:MINMIN_SUITE_ALLOW_UNISOLATED -ErrorAction SilentlyContinue
}
else {
    $env:MINMIN_SUITE_ALLOW_UNISOLATED = '1'
    Write-Warning '已显式 -AllowUnisolatedDesktop：窗口会进入用户桌面。用户正在用电脑时禁止。'
}

$script:SuiteLeakWatcher = $null
$script:SuiteLeakLog = $null
$script:SuiteLeakReported = $false

$suiteMutex = [System.Threading.Mutex]::new($false, 'Local\MinminCountdown.SuiteLowImpact')
if (-not $suiteMutex.WaitOne(0)) {
    throw '已有低影响套件启动器实例在运行，拒绝并发。'
}

try {
    try {
        [System.Diagnostics.Process]::GetCurrentProcess().PriorityClass =
            [System.Diagnostics.ProcessPriorityClass]::BelowNormal
    }
    catch {
        Write-Warning "无法设置 BelowNormal 优先级：$($_.Exception.Message)"
    }

    $repoRoot = Split-Path -Parent $PSScriptRoot
    $pwsh = (Get-Command pwsh).Source
    $logDir = Join-Path $repoRoot 'logs\suite-runs'
    [void] [System.IO.Directory]::CreateDirectory($logDir)
    $stamp = Get-Date -Format 'yyyyMMddTHHmmss'
    $suiteStopwatch = [System.Diagnostics.Stopwatch]::StartNew()
    $script:SuiteLeakLog = Join-Path $logDir "$stamp-desktop-leaks.log"
    if (Test-Path -LiteralPath $script:SuiteLeakLog -PathType Leaf) {
        Remove-Item -LiteralPath $script:SuiteLeakLog -Force
    }
    $script:SuiteLeakWatcher = Start-Process -FilePath $pwsh `
        -ArgumentList @(
            '-NoLogo', '-NoProfile', '-NonInteractive', '-File',
            (Join-Path $PSScriptRoot 'Watch-DesktopWindows.ps1'),
            '-Seconds', '86400', '-SuiteLeakOnly',
            '-OutputPath', $script:SuiteLeakLog
        ) `
        -WorkingDirectory $repoRoot `
        -RedirectStandardOutput (Join-Path $logDir "$stamp-watcher.out.log") `
        -RedirectStandardError (Join-Path $logDir "$stamp-watcher.err.log") `
        -WindowStyle Hidden -PassThru
    Write-Host "[suite] 用户桌面泄漏监测已启动（PID $($script:SuiteLeakWatcher.Id)）"

    $stageTable = @{
        Build = @{
            ArgumentList = @('-NoLogo', '-NoProfile', '-File', (Join-Path $repoRoot 'Build-CountdownApplication.ps1'), '-Clean')
            Windowed = $false
        }
        Core = @{
            ArgumentList = @('-NoLogo', '-NoProfile', '-File', (Join-Path $repoRoot 'Countdown.Core.Tests.ps1'))
            Windowed = $false
        }
        CSharp = @{
            ArgumentList = @('-NoLogo', '-NoProfile', '-STA', '-File', (Join-Path $repoRoot 'Countdown.CSharp.Tests.ps1'))
            Windowed = $true
        }
        Ui = @{
            ArgumentList = @('-NoLogo', '-NoProfile', '-STA', '-File', (Join-Path $repoRoot 'Countdown.Ui.Tests.ps1'))
            Windowed = $true
        }
        SourceValidation = @{
            ArgumentList = @('-NoLogo', '-NoProfile', '-STA', '-File', (Join-Path $repoRoot 'Invoke-CountdownSourceValidation.ps1'))
            Windowed = $false
        }
        InstallValidate = @{
            ArgumentList = @('-NoLogo', '-NoProfile', '-File', (Join-Path $repoRoot 'Install-Countdown.ps1'), '-ValidateOnly')
            Windowed = $false
        }
    }

    function Read-SuiteDesktopLeakFindings {
        if ([string]::IsNullOrWhiteSpace($script:SuiteLeakLog) -or
            -not (Test-Path -LiteralPath $script:SuiteLeakLog -PathType Leaf)) {
            return @()
        }
        return @(
            Get-Content -LiteralPath $script:SuiteLeakLog -Encoding UTF8 |
                Where-Object { $_ -and $_ -notmatch '^watcher-done' }
        )
    }

    function Test-SuiteDesktopLeakRed {
        $hits = @(Read-SuiteDesktopLeakFindings)
        if ($hits.Count -eq 0) {
            return $false
        }
        if (-not $script:SuiteLeakReported) {
            Write-Host "[suite] 红：用户桌面出现套件窗口泄漏：$($hits[0])"
            $script:SuiteLeakReported = $true
        }
        return $true
    }

    function Stop-SuiteDesktopLeakWatcher {
        if ($null -eq $script:SuiteLeakWatcher) {
            return
        }
        try {
            if (-not $script:SuiteLeakWatcher.HasExited) {
                [void] (& "$env:SystemRoot\System32\taskkill.exe" /PID $script:SuiteLeakWatcher.Id /T /F 2>&1)
                [void] $script:SuiteLeakWatcher.WaitForExit(5000)
            }
        }
        catch {
            Write-Warning "停止桌面泄漏监测失败：$($_.Exception.Message)"
        }
    }

    function ConvertTo-SingleQuotedLiteral {
        param([Parameter(Mandatory)] [string] $Value)
        "'" + ($Value -replace "'", "''") + "'"
    }

    # 阶段结果记录：既有七列含义不变，只增列 Status / LauncherStdout / LauncherStderr。
    function New-SuiteStageResult {
        param(
            [Parameter(Mandatory)] [object] $Run,
            [Parameter(Mandatory)] [int] $ExitCode,
            [Parameter(Mandatory)] [ValidateSet('Completed', 'Failed', 'Cancelled')] [string] $Status
        )
        [pscustomobject] @{
            Stage = $Run.Stage
            ExitCode = $ExitCode
            Status = $Status
            ElapsedSeconds = [int] $Run.Stopwatch.Elapsed.TotalSeconds
            Isolated = [bool] $Run.Isolated
            DesktopName = $Run.DesktopName
            Stdout = $Run.Stdout
            Stderr = $Run.Stderr
            LauncherStdout = $Run.LauncherStdout
            LauncherStderr = $Run.LauncherStderr
        }
    }

    # 启动一个阶段（不等待）。隔离模式经生成的 bootstrap 包装进程进程内调用隔离启动器，
    # 包装进程退出码即测试子进程退出码；非隔离模式与既有直启形状逐字一致。
    function Start-SuiteStagePhase {
        param(
            [Parameter(Mandatory)] [string] $Name,
            [Parameter(Mandatory)] [object] $Spec
        )
        $stdoutPath = Join-Path $logDir "$stamp-$Name.out.log"
        $stderrPath = Join-Path $logDir "$stamp-$Name.err.log"
        # 隔离桌面同时承接控制台与 WPF 窗口；SourceValidation 等阶段也会启动应用渲染窗口，一律隔离。
        # 桌面名按阶段唯一化：并行扇出下各阶段同名桌面冲突归零（#123 seam）。
        $isolated = [bool] $script:SuiteIsolation.Isolated
        $desktopName = if ($isolated) { "MinminSuiteIso-$Name" } else { $null }
        $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()

        if ($isolated) {
            $isoOutPath = Join-Path $logDir "$stamp-$Name.iso.out.log"
            $isoErrPath = Join-Path $logDir "$stamp-$Name.iso.err.log"
            $launcherPath = Join-Path $PSScriptRoot 'Start-IsolatedSuiteProcess.ps1'
            # 经命令行二次传递会让 -ArgumentList 的前导 '-' 元素被 binder 当作参数名截断，
            # 故把阶段命令写进每阶段 bootstrap，在包装进程内以真实数组绑定调用启动器。
            $argumentListLiteral =
                '@(' + (($Spec.ArgumentList | ForEach-Object { ConvertTo-SingleQuotedLiteral $_ }) -join ', ') + ')'
            $invokeLine =
                "& $(ConvertTo-SingleQuotedLiteral $launcherPath)" +
                " -FilePath $(ConvertTo-SingleQuotedLiteral $pwsh)" +
                " -ArgumentList $argumentListLiteral" +
                " -WorkingDirectory $(ConvertTo-SingleQuotedLiteral $repoRoot)" +
                " -StdoutPath $(ConvertTo-SingleQuotedLiteral $stdoutPath)" +
                " -StderrPath $(ConvertTo-SingleQuotedLiteral $stderrPath)" +
                " -DesktopName $(ConvertTo-SingleQuotedLiteral $desktopName)"
            $bootstrapPath = Join-Path $logDir "$stamp-$Name.iso.ps1"
            @"
# 由 Invoke-CountdownSuiteLowImpact.ps1 生成（run $stamp / 阶段 $Name）；运行期产物，勿手改。
`$ErrorActionPreference = 'Stop'
try {
    `$output = @($invokeLine)
    `$exitCode = [int] (`$output | Select-Object -Last 1)
}
catch {
    (`$_.Exception.Message + [Environment]::NewLine + (`$_ | Out-String)) |
        Add-Content -LiteralPath $(ConvertTo-SingleQuotedLiteral $stderrPath) -Encoding UTF8
    `$exitCode = 1
}
exit `$exitCode
"@ | Set-Content -LiteralPath $bootstrapPath -Encoding UTF8

            $process = Start-Process -FilePath $pwsh `
                -ArgumentList @('-NoLogo', '-NoProfile', '-NonInteractive', '-File', $bootstrapPath) `
                -WorkingDirectory $repoRoot `
                -RedirectStandardOutput $isoOutPath `
                -RedirectStandardError $isoErrPath `
                -WindowStyle Hidden -PassThru
            $launcherStdout = $isoOutPath
            $launcherStderr = $isoErrPath
        }
        else {
            $process = Start-Process -FilePath $pwsh `
                -ArgumentList $Spec.ArgumentList `
                -WorkingDirectory $repoRoot `
                -RedirectStandardOutput $stdoutPath `
                -RedirectStandardError $stderrPath `
                -WindowStyle Hidden -PassThru
            $launcherStdout = $null
            $launcherStderr = $null
        }

        try {
            $process.PriorityClass = [System.Diagnostics.ProcessPriorityClass]::BelowNormal
        }
        catch {
            Write-Warning "无法为阶段 $Name 进程设置 BelowNormal：$($_.Exception.Message)"
        }

        [pscustomobject] @{
            Stage = $Name
            Process = $process
            Stopwatch = $stopwatch
            Isolated = $isolated
            DesktopName = $desktopName
            Stdout = $stdoutPath
            Stderr = $stderrPath
            LauncherStdout = $launcherStdout
            LauncherStderr = $launcherStderr
        }
    }

    # 红即停：强杀阶段进程树（包装进程 + 隔离桌面子进程 + 孙进程）。
    function Stop-SuiteStagePhase {
        param([Parameter(Mandatory)] [object] $Run)
        try {
            if (-not $Run.Process.HasExited) {
                [void] (& "$env:SystemRoot\System32\taskkill.exe" /PID $Run.Process.Id /T /F 2>&1)
                [void] $Run.Process.WaitForExit(15000)
            }
            if (-not $Run.Process.HasExited) {
                Write-Warning "阶段 $($Run.Stage) 取消后 15 秒仍未退出（进程树可能残留，PID $($Run.Process.Id)）。"
            }
        }
        catch {
            Write-Warning "取消阶段 $($Run.Stage) 失败：$($_.Exception.Message)"
        }
    }

    # 唯一收口出口：按官方阶段顺序排序写 summary，红绿都经过这里。
    function Complete-SuiteRun {
        param([Parameter(Mandatory)] [bool] $Failed)
        Stop-SuiteDesktopLeakWatcher
        if (Test-SuiteDesktopLeakRed) {
            $Failed = $true
        }
        $suiteStopwatch.Stop()
        $sorted = @($results | Sort-Object { $allowedStages.IndexOf($_.Stage) })
        $summaryPath = Join-Path $logDir "$stamp-summary.json"
        $sorted | ConvertTo-Json | Set-Content -LiteralPath $summaryPath -Encoding UTF8
        $sorted | Format-Table Stage, ExitCode, Status, ElapsedSeconds, Isolated, DesktopName -AutoSize
        Write-Host "[suite] 总墙钟 $([int] $suiteStopwatch.Elapsed.TotalSeconds) 秒；summary：$summaryPath"
        if ($Failed) { exit 1 }
        exit 0
    }

    $results = [System.Collections.Generic.List[object]]::new()
    $runners = [System.Collections.Generic.List[object]]::new()
    $red = $false

    try {
        # Build 门禁：串行先行，红即停。此时无并行在跑阶段，行为与串行版本一致。
        foreach ($name in @($Stage | Where-Object { $_ -ieq 'Build' })) {
            $run = Start-SuiteStagePhase -Name $name -Spec $stageTable[$name]
            Write-Host "[suite] 阶段启动（Build 门禁，串行）：$name（PID $($run.Process.Id)）"
            $run.Process.WaitForExit()
            $run.Stopwatch.Stop()
            $exitCode = $run.Process.ExitCode
            $status = if ($exitCode -eq 0) { 'Completed' } else { 'Failed' }
            $results.Add((New-SuiteStageResult -Run $run -ExitCode $exitCode -Status $status))
            Write-Host "[suite] 阶段完成（Build 门禁）：$name 退出码 $exitCode 用时 $([int] $run.Stopwatch.Elapsed.TotalSeconds) 秒"
            if ($exitCode -ne 0 -or (Test-SuiteDesktopLeakRed)) {
                $red = $true
                break
            }
        }

        if (-not $red) {
            $fanoutStages = @($Stage | Where-Object { $_ -ine 'Build' })
            if ($fanoutStages.Count -gt 0) {
                Write-Host "[suite] Build 绿，并行扇出：$($fanoutStages -join ', ')（MaxParallel=$MaxParallel）"
            }
            $queue = [System.Collections.Generic.Queue[string]]::new([string[]] $fanoutStages)
            while ($true) {
                while (-not $red -and $runners.Count -lt $MaxParallel -and $queue.Count -gt 0) {
                    $name = $queue.Dequeue()
                    $run = Start-SuiteStagePhase -Name $name -Spec $stageTable[$name]
                    $runners.Add($run)
                    $desktopNote = if ($run.DesktopName) { "，桌面 $($run.DesktopName)" } else { '' }
                    Write-Host "[suite] 阶段启动：$name（PID $($run.Process.Id)$desktopNote）"
                }
                if ($runners.Count -eq 0) { break }
                Start-Sleep -Milliseconds 500
                if (Test-SuiteDesktopLeakRed) {
                    $red = $true
                }
                foreach ($run in @($runners)) {
                    if (-not $run.Process.HasExited) { continue }
                    # 无参 WaitForExit 立即返回，同时保证重定向输出已从管道排空再读退出码。
                    [void] $run.Process.WaitForExit()
                    $run.Stopwatch.Stop()
                    $exitCode = $run.Process.ExitCode
                    $status = if ($exitCode -eq 0) { 'Completed' } else { 'Failed' }
                    $results.Add((New-SuiteStageResult -Run $run -ExitCode $exitCode -Status $status))
                    [void] $runners.Remove($run)
                    Write-Host "[suite] 阶段完成：$($run.Stage) 退出码 $exitCode 用时 $([int] $run.Stopwatch.Elapsed.TotalSeconds) 秒"
                    if ($exitCode -ne 0) {
                        $red = $true
                    }
                }
                if ($red -and $runners.Count -gt 0) {
                    Write-Host "[suite] 红：取消在跑阶段：$((@($runners) | ForEach-Object { $_.Stage }) -join ', ')"
                    foreach ($run in @($runners)) {
                        Stop-SuiteStagePhase -Run $run
                        $run.Stopwatch.Stop()
                        $exitCode = if ($run.Process.HasExited) { $run.Process.ExitCode } else { -1 }
                        $results.Add((New-SuiteStageResult -Run $run -ExitCode $exitCode -Status 'Cancelled'))
                        Write-Host "[suite] 已取消：$($run.Stage)（退出码 $exitCode）"
                    }
                    $runners.Clear()
                }
                if ($red -and $queue.Count -gt 0) {
                    Write-Host "[suite] 红即停，未启动剩余阶段：$(@($queue) -join ', ')"
                    $queue.Clear()
                }
            }
        }

        Complete-SuiteRun -Failed:$red
    }
    catch {
        # 调度层意外终止：不留孤儿进程树，取消仍在跑的阶段后原样抛出（不写 summary，与既有行为一致）。
        foreach ($run in @($runners)) {
            if (-not $run.Process.HasExited) {
                [void] (& "$env:SystemRoot\System32\taskkill.exe" /PID $run.Process.Id /T /F 2>&1)
            }
        }
        throw
    }
    finally {
        Stop-SuiteDesktopLeakWatcher
    }
}
finally {
    $suiteMutex.ReleaseMutex()
    $suiteMutex.Dispose()
}
