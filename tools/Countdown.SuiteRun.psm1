#requires -Version 7.0
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Write-SuiteRunJson {
    param([string] $Path, [object] $Value)
    $temporaryPath = "$Path.$([guid]::NewGuid().ToString('N')).tmp"
    [System.IO.File]::WriteAllText($temporaryPath, (ConvertTo-Json -InputObject $Value -Depth 12), [System.Text.UTF8Encoding]::new($false))
    # 目标被任何句柄打开（杀毒扫描、读取方）时替换报 Access denied，临时文件被占时报共享冲突。
    # 这类占用是瞬时的：短暂重试，2 秒后仍失败才抛出，不让一次扫描把整轮套件判红。
    $deadline = [datetime]::UtcNow.AddSeconds(2)
    while ($true) {
        try {
            [System.IO.File]::Move($temporaryPath, $Path, $true)
            return
        }
        catch [System.UnauthorizedAccessException], [System.IO.IOException] {
            if ([datetime]::UtcNow -ge $deadline) {
                $failure = $_.Exception
                try { [System.IO.File]::Delete($temporaryPath) } catch { }
                throw [System.IO.IOException]::new("替换 $Path 失败，重试 2 秒后仍被占用：$($failure.Message)", $failure)
            }
            Start-Sleep -Milliseconds 100
        }
    }
}

function Save-SuiteRun {
    param([object] $Run)
    $Run.UpdatedAtUtc = [datetime]::UtcNow.ToString('o')
    Write-SuiteRunJson -Path $Run.StatePath -Value $Run
    # 保留原 summary 的阶段数组和字段；run.json 是含 owner 与整体终态的权威记录。
    Write-SuiteRunJson -Path $Run.SummaryPath -Value @($Run.Stages)
}

function Get-SuiteProcessIdentity {
    param([System.Diagnostics.Process] $Process)
    [pscustomobject]@{
        ProcessId = $Process.Id
        StartedAtUtc = $Process.StartTime.ToUniversalTime().ToString('o')
    }
}

function Test-SuiteProcessIdentity {
    param([object] $Identity)
    if ($null -eq $Identity) { return $false }
    $process = $null
    try {
        $process = [System.Diagnostics.Process]::GetProcessById([int]$Identity.ProcessId)
        return -not $process.HasExited -and
            $process.StartTime.ToUniversalTime().Ticks -eq ([datetime]$Identity.StartedAtUtc).ToUniversalTime().Ticks
    }
    catch [System.ArgumentException] { return $false }
    catch [System.ComponentModel.Win32Exception] { return $false }
    catch [System.InvalidOperationException] { return $false }
    finally { if ($null -ne $process) { $process.Dispose() } }
}

function Read-SuiteRun {
    [CmdletBinding()]
    param([Parameter(Mandatory)] [string] $Path)
    $run = Get-Content -LiteralPath $Path -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($run.SchemaVersion -ne 1) { throw "不支持的 suite run schema：$($run.SchemaVersion)" }
    if ($run.Status -eq 'Running' -and -not (Test-SuiteProcessIdentity $run.Owner)) {
        $run.Status = 'Interrupted'
        $run.ExitCode = 1
        $run.Failure = '启动器进程身份已失效；上次运行被中断，不能作为通过证据。'
        $run.FinishedAtUtc = [datetime]::UtcNow.ToString('o')
        foreach ($stage in $run.Stages) {
            if ($stage.Status -in @('Pending', 'Starting', 'Running')) {
                $stage.Status = if ($stage.Status -eq 'Pending') { 'Skipped' } else { 'Interrupted' }
                $stage.ExitCode = 1
                $stage.FinishedAtUtc = $run.FinishedAtUtc
                $stage.Failure = $run.Failure
            }
        }
        # 回读只修复记录，不按旧 PID 终止任何进程。
        Save-SuiteRun $run
    }
    return $run
}

function ConvertTo-SuiteLiteral {
    param([string] $Value)
    return "'" + $Value.Replace("'", "''") + "'"
}

function Start-SuiteOwnedProcess {
    param([object] $Spec, [object] $Stage, [object] $Run)
    $arguments = @($Spec.ArgumentList)
    if ($Spec.Contains('PreflightScript')) {
        $Stage.PreflightPath = Join-Path $Run.LogDirectory "$($Run.RunId)-$($Stage.Stage)-preflight.json"
        $entryPath = Join-Path $Run.LogDirectory "$($Run.RunId)-$($Stage.Stage).stage.ps1"
        $preflightLiteral = ConvertTo-SuiteLiteral $Spec.PreflightScript
        $rootLiteral = ConvertTo-SuiteLiteral $Run.RepositoryRoot
        $resultLiteral = ConvertTo-SuiteLiteral $Stage.PreflightPath
        $commandLiteral = ConvertTo-SuiteLiteral $Spec.FilePath
        $argumentsLiteral = '@(' + (($arguments | ForEach-Object { ConvertTo-SuiteLiteral $_ }) -join ', ') + ')'
        $entry = @"
`$ErrorActionPreference = 'Stop'
`$preflightJson = & $preflightLiteral -RepositoryRoot $rootLiteral -RequireClean -Json
`$preflightExit = `$LASTEXITCODE
[System.IO.File]::WriteAllText($resultLiteral, (`$preflightJson -join [Environment]::NewLine), [System.Text.UTF8Encoding]::new(`$false))
`$preflight = (`$preflightJson -join [Environment]::NewLine) | ConvertFrom-Json -ErrorAction Stop
if (`$preflightExit -ne 0 -or -not `$preflight.Valid) { exit 1 }
& $commandLiteral @argumentsLiteral
exit `$LASTEXITCODE
"@
        # 数组变量须在生成脚本中绑定，避免 -ArgumentList 二次命令行绑定。
        $entry = $entry.Replace('@argumentsLiteral', $argumentsLiteral)
        [System.IO.File]::WriteAllText($entryPath, $entry, [System.Text.UTF8Encoding]::new($false))
        $arguments = @('-NoLogo', '-NoProfile', '-NonInteractive', '-File', $entryPath)
    }

    if ($Stage.Isolated) {
        $launcherPath = Join-Path $PSScriptRoot 'Start-IsolatedSuiteProcess.ps1'
        $bootstrapPath = Join-Path $Run.LogDirectory "$($Run.RunId)-$($Stage.Stage).iso.ps1"
        $argumentsLiteral = '@(' + (($arguments | ForEach-Object { ConvertTo-SuiteLiteral $_ }) -join ', ') + ')'
        $invokeLine = '& ' + (ConvertTo-SuiteLiteral $launcherPath) +
            ' -FilePath ' + (ConvertTo-SuiteLiteral $Spec.FilePath) +
            ' -ArgumentList ' + $argumentsLiteral +
            ' -WorkingDirectory ' + (ConvertTo-SuiteLiteral $Run.RepositoryRoot) +
            ' -StdoutPath ' + (ConvertTo-SuiteLiteral $Stage.Stdout) +
            ' -StderrPath ' + (ConvertTo-SuiteLiteral $Stage.Stderr) +
            ' -DesktopName ' + (ConvertTo-SuiteLiteral $Stage.DesktopName) +
            ' -TimeoutSeconds ' + $Stage.TimeoutSeconds +
            ' -ProcessIdentityPath ' + (ConvertTo-SuiteLiteral $Stage.ChildIdentityPath) +
            ' -OwnerProcessId ' + $Run.Owner.ProcessId +
            ' -OwnerStartedAtUtc ' + (ConvertTo-SuiteLiteral $Run.Owner.StartedAtUtc)
        $bootstrap = @"
`$ErrorActionPreference = 'Stop'
try {
    `$output = @($invokeLine)
    exit [int] (`$output | Select-Object -Last 1)
}
catch {
    [System.IO.File]::AppendAllText($(ConvertTo-SuiteLiteral $Stage.Stderr), (`$_ | Out-String))
    exit 1
}
"@
        [System.IO.File]::WriteAllText($bootstrapPath, $bootstrap, [System.Text.UTF8Encoding]::new($false))
        $arguments = @('-NoLogo', '-NoProfile', '-NonInteractive', '-File', $bootstrapPath)
    }

    $startInfo = [System.Diagnostics.ProcessStartInfo]::new($Spec.FilePath)
    $startInfo.WorkingDirectory = $Run.RepositoryRoot
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.WindowStyle = [System.Diagnostics.ProcessWindowStyle]::Hidden
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    foreach ($argument in $arguments) { $startInfo.ArgumentList.Add($argument) }
    $stdout = if ($Stage.Isolated) { $Stage.LauncherStdout } else { $Stage.Stdout }
    $stderr = if ($Stage.Isolated) { $Stage.LauncherStderr } else { $Stage.Stderr }
    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    $outputStream = $null
    $errorStream = $null
    try {
        $outputStream = [System.IO.File]::Open($stdout, 'Create', 'Write', 'ReadWrite')
        $errorStream = [System.IO.File]::Open($stderr, 'Create', 'Write', 'ReadWrite')
        if (-not $process.Start()) { throw 'Process.Start 未启动进程。' }
        $identity = Get-SuiteProcessIdentity $process
        $Stage.Process = $identity
        try { $process.PriorityClass = 'BelowNormal' } catch { Write-Warning $_.Exception.Message }
        return [pscustomobject]@{
            Process = $process
            Stage = $Stage
            OutputStream = $outputStream
            ErrorStream = $errorStream
            OutputTask = $process.StandardOutput.BaseStream.CopyToAsync($outputStream)
            ErrorTask = $process.StandardError.BaseStream.CopyToAsync($errorStream)
            Stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
        }
    }
    catch {
        if ($null -ne $outputStream) { $outputStream.Dispose() }
        if ($null -ne $errorStream) { $errorStream.Dispose() }
        try { if ($process.Id -and -not $process.HasExited) { $process.Kill($true) } } catch { }
        $process.Dispose()
        throw
    }
}

function Complete-SuiteOwnedProcess {
    param([object] $Owned, [string] $Status, [int] $ExitCode, [string] $Failure)
    $stage = $Owned.Stage
    try {
        if (-not $Owned.Process.HasExited) {
            # 持有的 Process handle 是本轮创建的对象；绝不按名称或历史 PID 杀进程。
            $Owned.Process.Kill($true)
            if (-not $Owned.Process.WaitForExit(5000)) { throw '取消后 5 秒仍未退出。' }
        }
        $stage.ProcessExitCode = $Owned.Process.ExitCode
        $drained = [System.Threading.Tasks.Task]::WaitAll(
            [System.Threading.Tasks.Task[]]@($Owned.OutputTask, $Owned.ErrorTask), 5000)
        if (-not $drained) { throw '输出管道在进程退出后 5 秒仍未结束。' }
    }
    catch {
        $Failure = ($Failure + ' ' + $_.Exception.Message).Trim()
        if ($Status -eq 'Completed') { $Status = 'Failed'; $ExitCode = 1 }
    }
    finally {
        $Owned.OutputStream.Dispose()
        $Owned.ErrorStream.Dispose()
        $Owned.Process.Dispose()
    }
    $stage.Status = $Status
    $stage.ExitCode = $ExitCode
    $stage.Failure = $Failure
    $stage.ElapsedSeconds = [int]$Owned.Stopwatch.Elapsed.TotalSeconds
    $stage.FinishedAtUtc = [datetime]::UtcNow.ToString('o')
}

function Invoke-SuiteRun {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [object[]] $StageSpecs,
        [Parameter(Mandatory)] [string] $RepositoryRoot,
        [Parameter(Mandatory)] [string] $LogDirectory,
        [ValidateRange(1, 5)] [int] $MaxParallel = 2,
        [ValidateRange(1, 86400)] [int] $StageTimeoutSeconds = 1800,
        [scriptblock] $Initialize,
        [scriptblock] $Finalize,
        [scriptblock] $FailureProbe
    )
    [void][System.IO.Directory]::CreateDirectory($LogDirectory)
    foreach ($oldPath in [System.IO.Directory]::EnumerateFiles($LogDirectory, '*-run.json')) {
        $null = Read-SuiteRun -Path $oldPath
    }
    $runId = [datetime]::Now.ToString('yyyyMMddTHHmmssfff') + '-' + [guid]::NewGuid().ToString('N').Substring(0, 8)
    $run = [pscustomobject]@{
        SchemaVersion = 1; RunId = $runId; RepositoryRoot = $RepositoryRoot; LogDirectory = $LogDirectory
        StatePath = Join-Path $LogDirectory "$runId-run.json"
        SummaryPath = Join-Path $LogDirectory "$runId-summary.json"
        CancellationPath = Join-Path $LogDirectory "$runId-cancel.request"
        Owner = Get-SuiteProcessIdentity ([System.Diagnostics.Process]::GetCurrentProcess())
        Status = 'Running'; ExitCode = $null; Failure = $null
        StartedAtUtc = [datetime]::UtcNow.ToString('o'); FinishedAtUtc = $null; UpdatedAtUtc = $null
        Stages = @($StageSpecs | ForEach-Object {
            $name = $_.Stage
            $isolated = $_.Contains('Isolated') -and [bool]$_.Isolated
            [pscustomobject]@{
                Stage = $name; ExitCode = $null; ProcessExitCode = $null; Status = 'Pending'; ElapsedSeconds = 0
                Isolated = $isolated; DesktopName = $(if ($isolated) { "MinminSuiteIso-$name" } else { $null })
                Stdout = Join-Path $LogDirectory "$runId-$name.out.log"
                Stderr = Join-Path $LogDirectory "$runId-$name.err.log"
                LauncherStdout = $(if ($isolated) { Join-Path $LogDirectory "$runId-$name.iso.out.log" } else { $null })
                LauncherStderr = $(if ($isolated) { Join-Path $LogDirectory "$runId-$name.iso.err.log" } else { $null })
                ChildIdentityPath = $(if ($isolated) { Join-Path $LogDirectory "$runId-$name-process.json" } else { $null })
                Process = $null; ChildProcess = $null; PreflightPath = $null; Preflight = $null
                TimeoutSeconds = $StageTimeoutSeconds; StartedAtUtc = $null; FinishedAtUtc = $null; Failure = $null
            }
        })
    }
    Save-SuiteRun $run
    Write-Host "[suite] run：$($run.StatePath)；取消：创建 $($run.CancellationPath)"
    $ownedProcesses = [System.Collections.Generic.List[object]]::new()
    $normalReturn = $false
    try {
        if ($null -ne $Initialize) { & $Initialize $run }
        while ($true) {
            if ([System.IO.File]::Exists($run.CancellationPath)) {
                $run.Status = 'Cancelled'; $run.Failure = '收到本轮取消请求。'; Save-SuiteRun $run; break
            }
            if ($null -ne $FailureProbe -and (& $FailureProbe $run)) {
                $run.Status = 'Failed'; $run.Failure = '用户桌面出现套件窗口泄漏。'; Save-SuiteRun $run; break
            }
            foreach ($owned in @($ownedProcesses)) {
                $stage = $owned.Stage
                if ($stage.Isolated -and $null -eq $stage.ChildProcess -and [System.IO.File]::Exists($stage.ChildIdentityPath)) {
                    $stage.ChildProcess = Get-Content -LiteralPath $stage.ChildIdentityPath -Raw -Encoding UTF8 | ConvertFrom-Json
                    Save-SuiteRun $run
                }
                if ($null -ne $stage.PreflightPath -and $null -eq $stage.Preflight -and [System.IO.File]::Exists($stage.PreflightPath)) {
                    $stage.Preflight = Get-Content -LiteralPath $stage.PreflightPath -Raw -Encoding UTF8 | ConvertFrom-Json
                    Save-SuiteRun $run
                }
                if ($owned.Process.HasExited) {
                    $code = $owned.Process.ExitCode
                    $status = if ($code -eq 0) { 'Completed' } elseif ($code -eq 124) { 'TimedOut' } else { 'Failed' }
                    Complete-SuiteOwnedProcess $owned $status $code ''
                }
                elseif ($owned.Stopwatch.Elapsed.TotalSeconds -ge $stage.TimeoutSeconds) {
                    Complete-SuiteOwnedProcess $owned 'TimedOut' 124 "阶段超过 $($stage.TimeoutSeconds) 秒上限。"
                }
                else { continue }
                [void]$ownedProcesses.Remove($owned)
                Save-SuiteRun $run
                Write-Host "[suite] $($stage.Stage)：$($stage.Status)，退出码 $($stage.ExitCode)"
                if ($stage.Status -ne 'Completed') {
                    $run.Status = $stage.Status; $run.Failure = "阶段 $($stage.Stage) 未通过。"
                }
            }
            if ($run.Status -ne 'Running') { break }
            $build = @($run.Stages | Where-Object Stage -EQ 'Build')
            $gatePending = $build.Count -gt 0 -and $build[0].Status -ne 'Completed'
            foreach ($stage in @($run.Stages | Where-Object Status -EQ 'Pending')) {
                if ($ownedProcesses.Count -ge $MaxParallel) { break }
                if ($gatePending -and $stage.Stage -ne 'Build') { continue }
                $stage.Status = 'Starting'; $stage.StartedAtUtc = [datetime]::UtcNow.ToString('o')
                Save-SuiteRun $run
                try {
                    $spec = $StageSpecs | Where-Object Stage -EQ $stage.Stage | Select-Object -First 1
                    $owned = Start-SuiteOwnedProcess $spec $stage $run
                    $ownedProcesses.Add($owned)
                    $stage.Status = 'Running'
                    Save-SuiteRun $run
                    Write-Host "[suite] 启动 $($stage.Stage)，PID $($stage.Process.ProcessId)，时限 $StageTimeoutSeconds 秒"
                }
                catch {
                    $stage.Status = 'Failed'; $stage.ExitCode = 1; $stage.Failure = $_.Exception.Message
                    $stage.FinishedAtUtc = [datetime]::UtcNow.ToString('o')
                    throw
                }
            }
            if ($ownedProcesses.Count -eq 0) { $run.Status = 'Completed'; break }
            Start-Sleep -Milliseconds 100
        }
        $normalReturn = $true
    }
    catch {
        $run.Status = if ($_.Exception -is [System.Management.Automation.PipelineStoppedException]) { 'Cancelled' } else { 'Failed' }
        $run.Failure = $_.Exception.Message
        Save-SuiteRun $run
    }
    finally {
        if ($run.Status -eq 'Running') {
            $run.Status = 'Cancelled'; $run.Failure = '启动器执行被取消。'
        }
        foreach ($owned in @($ownedProcesses)) {
            Complete-SuiteOwnedProcess $owned 'Cancelled' 130 $run.Failure
        }
        foreach ($stage in $run.Stages) {
            if ($stage.Status -in @('Pending', 'Starting', 'Running')) {
                $stage.Status = if ($stage.Status -eq 'Pending') { 'Skipped' } else { 'Cancelled' }
                $stage.ExitCode = 1; $stage.Failure = $run.Failure; $stage.FinishedAtUtc = [datetime]::UtcNow.ToString('o')
            }
        }
        try { if ($null -ne $Finalize) { & $Finalize $run } }
        catch { $run.Status = 'Failed'; $run.Failure = $_.Exception.Message }
        try {
            if ($null -ne $FailureProbe -and (& $FailureProbe $run)) {
                $run.Status = 'Failed'; $run.Failure = '用户桌面出现套件窗口泄漏。'
            }
        }
        catch { $run.Status = 'Failed'; $run.Failure = $_.Exception.Message }
        $run.ExitCode = if ($run.Status -eq 'Completed') { 0 } else { 1 }
        $run.FinishedAtUtc = [datetime]::UtcNow.ToString('o')
        Save-SuiteRun $run
    }
    return $run
}

Export-ModuleMember -Function Invoke-SuiteRun, Read-SuiteRun, Test-SuiteProcessIdentity
