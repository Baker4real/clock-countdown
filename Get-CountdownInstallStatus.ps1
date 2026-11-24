#requires -Version 7.0
<#
.SYNOPSIS
只读状态命令（#276）：读出一台设备上的安装现状，输出一份 JSON。
.DESCRIPTION
不写任何文件，不启动也不停止任何进程；目标设备上不需要任何项目脚本，可直接交给远端执行入口运行。
输出不含设置内容、凭据、主机名或账号。字段：commit 与 aggregateSha256 取自载荷清单，清单缺失或没记提交时为 null（未知）；
manifest 为 present/missing/invalid；exe.matchesManifest 在清单不可用时为 null；processes 只列映像路径是已安装 EXE 的进程；
loginTask.mismatches 列出与期望定义不符的项；settings 只有长度、修改时间与 SHA-256；transactionMarkerPresent 表示有未完成的安装；
installInProgress 表示此刻有安装正在运行（全机安装锁在），这时其余各项可能正在变。
-Port 供测试注入进程与计划任务查询；不传时查询真实 Windows。-LockName 供测试换用自己的锁名。
#>
[CmdletBinding()]
param(
    [string] $InstallRoot = 'D:\敏敏子倒计时',
    [string] $TaskName = 'Minmin Countdown',
    [object] $Port,
    # 与安装器同一把全机安装锁（Countdown.Installer.psm1 的 InstallLockName）；本命令自成一体，只能留一份副本。
    [string] $LockName = 'Global\MinminCountdownInstallTransaction-v1'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$field = { param($Object, [string] $Name) $property = $Object.PSObject.Properties[$Name]; if ($property) { $property.Value } }

if ($null -eq $Port) {
    $Port = [pscustomobject]@{
        GetProcesses = {
            param([string] $Name)
            @(Get-CimInstance Win32_Process -Filter "Name='$Name'" -OperationTimeoutSec 10 -ErrorAction Stop)
        }
        GetTask = {
            param([string] $Name)
            try { $t = Get-ScheduledTask -TaskPath '\' -TaskName $Name -ErrorAction Stop }
            catch { if ($_.CategoryInfo.Category -eq 'ObjectNotFound') { return $null }; throw }
            # 没有触发器时 CIM 属性是 $null，管道会把它当成一项，先滤掉。
            $kind = { param($o) $o.CimClass.CimClassName -replace '^MSFT_Task|(Action|Trigger)$' }
            $sid = {
                param([string] $Account)
                if ([string]::IsNullOrWhiteSpace($Account)) { return $null }
                try { ([Security.Principal.NTAccount] $Account).Translate([Security.Principal.SecurityIdentifier]).Value }
                catch { $Account }
            }
            [pscustomobject]@{
                State = [string] $t.State
                Enabled = [bool] $t.Settings.Enabled
                Actions = @($t.Actions | Where-Object { $_ } | ForEach-Object { [pscustomobject]@{ Type = & $kind $_; Execute = & $field $_ 'Execute'; Arguments = & $field $_ 'Arguments'; WorkingDirectory = & $field $_ 'WorkingDirectory' } })
                Triggers = @($t.Triggers | Where-Object { $_ } | ForEach-Object { [pscustomobject]@{ Type = & $kind $_; Enabled = [bool] $_.Enabled; UserSid = & $sid ([string] (& $field $_ 'UserId')) } })
                UserSid = & $sid ([string] $t.Principal.UserId)
                LogonType = [string] $t.Principal.LogonType
                RunLevel = [string] $t.Principal.RunLevel
                Priority = [int] $t.Settings.Priority
                ExecutionTimeLimit = [string] $t.Settings.ExecutionTimeLimit
                MultipleInstances = [string] $t.Settings.MultipleInstances
                DisallowStartIfOnBatteries = [bool] $t.Settings.DisallowStartIfOnBatteries
                StopIfGoingOnBatteries = [bool] $t.Settings.StopIfGoingOnBatteries
                AllowDemandStart = [bool] $t.Settings.AllowDemandStart
                AllowHardTerminate = [bool] $t.Settings.AllowHardTerminate
            }
        }
        # 只取交互会话的 SID 来核对任务主体与登录触发器；不读账号，也不把它写进输出。
        GetInteractiveSessions = {
            $sessions = @(Get-CimInstance Win32_Process -Filter "Name='explorer.exe'" -OperationTimeoutSec 10 -ErrorAction Stop |
                    Where-Object { [int] $_.SessionId -gt 0 } | Group-Object -Property SessionId)
            if ($sessions.Count -ne 1) { return @($sessions | ForEach-Object { [pscustomobject]@{ SessionId = [int] $_.Name; Sid = $null } }) }
            $ownerSid = Invoke-CimMethod -InputObject $sessions[0].Group[0] -MethodName GetOwnerSid -OperationTimeoutSec 10 -ErrorAction Stop
            if ([int] $ownerSid.ReturnValue -ne 0 -or [string]::IsNullOrWhiteSpace([string] $ownerSid.Sid)) { throw "无法读取交互会话 $($sessions[0].Name) 的登录用户 SID。" }
            [pscustomobject]@{ SessionId = [int] $sessions[0].Name; Sid = [string] $ownerSid.Sid }
        }
    }
}

$root = [IO.Path]::TrimEndingDirectorySeparator([IO.Path]::GetFullPath($ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($InstallRoot)))
$exePath = Join-Path $root 'app\MinminCountdown.exe'
$utc = { param([datetime] $Time) $Time.ToUniversalTime().ToString('o') }
# 共享读写删除打开：读取期间不妨碍应用原子替换设置；长度、修改时间和哈希取自同一个打开的文件。
$fingerprint = {
    param([string] $Path)
    $stream = [IO.File]::Open($Path, 'Open', 'Read', 'ReadWrite, Delete')
    try {
        [ordered]@{
            present = $true
            length = $stream.Length
            lastWriteTimeUtc = & $utc ([IO.File]::GetLastWriteTimeUtc($stream.SafeFileHandle))
            sha256 = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream))
        }
    }
    finally { $stream.Dispose() }
}

$manifest = 'missing'
$commit = $null
$aggregate = $null
$expectedExe = $null
$manifestPath = Join-Path $root 'app\published-payload.json'
if (Test-Path -LiteralPath $manifestPath -PathType Leaf) {
    try {
        $document = [IO.File]::ReadAllText($manifestPath) | ConvertFrom-Json
        $commit = [string] (& $field $document 'commit')
        $aggregate = [string] (& $field $document 'aggregateSha256')
        $entry = @(& $field $document 'files' | Where-Object { [string] $_.path -eq 'app\MinminCountdown.exe' })
        $expectedExe = if ($entry.Count -eq 1) { [string] $entry[0].sha256 }
        $manifest = 'present'
    }
    catch { $manifest = 'invalid' }
}
if (-not $commit -or $manifest -ne 'present') { $commit = $null }
if (-not $aggregate -or $manifest -ne 'present') { $aggregate = $null }

$exe = [ordered]@{ present = $false; sha256 = $null; matchesManifest = $null }
if (Test-Path -LiteralPath $exePath -PathType Leaf) {
    $exe.present = $true
    $exe.sha256 = (& $fingerprint $exePath).sha256
}
if ($manifest -eq 'present') { $exe.matchesManifest = [bool] ($exe.sha256 -and $expectedExe -and $exe.sha256 -eq $expectedExe) }

$processes = @(
    & $Port.GetProcesses ([IO.Path]::GetFileName($exePath)) |
        Where-Object { $_.ExecutablePath -and [IO.Path]::GetFullPath([string] $_.ExecutablePath) -eq $exePath } |
        Sort-Object ProcessId |
        ForEach-Object {
            [ordered]@{
                processId = [int] $_.ProcessId
                path = [string] $_.ExecutablePath
                sessionId = [int] $_.SessionId
                startTimeUtc = if ($_.CreationDate) { & $utc $_.CreationDate } else { $null }
            }
        }
)

$loginTask = [ordered]@{ name = $TaskName; present = $false; state = $null; matchesExpected = $false; mismatches = @() }
$task = & $Port.GetTask $TaskName
if ($null -ne $task) {
    # 与安装器 Get-CountdownLoginTaskDefinition 相同：交互用户的登录触发器与主体、交互式非提升、优先级 4、不限时长、
    # IgnoreNew、电池供电也启动且拔电不停、允许按需启动与强制结束；动作只有已安装 EXE，不带参数或固定工作目录。
    $sessions = @(& $Port.GetInteractiveSessions)
    $sessionSid = if ($sessions.Count -eq 1) { [string] (& $field $sessions[0] 'Sid') } else { $null }
    $hasSession = -not [string]::IsNullOrWhiteSpace($sessionSid)
    $action = @($task.Actions)
    $trigger = @($task.Triggers)
    $limit = try { [Xml.XmlConvert]::ToTimeSpan([string] $task.ExecutionTimeLimit) } catch { $null }
    $checks = [ordered]@{
        action = $action.Count -eq 1 -and $action[0].Type -eq 'Exec' -and ([string] $action[0].Execute).Trim('"') -eq $exePath -and
            [string]::IsNullOrWhiteSpace([string] $action[0].Arguments) -and [string]::IsNullOrWhiteSpace([string] (& $field $action[0] 'WorkingDirectory'))
        trigger = $trigger.Count -eq 1 -and $trigger[0].Type -eq 'Logon' -and [bool] $trigger[0].Enabled -and
            $hasSession -and [string] (& $field $trigger[0] 'UserSid') -eq $sessionSid
        principal = $hasSession -and [string] (& $field $task 'UserSid') -eq $sessionSid
        logonType = $task.LogonType -eq 'Interactive'
        runLevel = $task.RunLevel -eq 'Limited'
        priority = $task.Priority -eq 4
        executionTimeLimit = $limit -eq [TimeSpan]::Zero
        multipleInstances = $task.MultipleInstances -eq 'IgnoreNew'
        disallowStartIfOnBatteries = -not [bool] (& $field $task 'DisallowStartIfOnBatteries')
        stopIfGoingOnBatteries = -not [bool] (& $field $task 'StopIfGoingOnBatteries')
        allowDemandStart = [bool] (& $field $task 'AllowDemandStart')
        allowHardTerminate = [bool] (& $field $task 'AllowHardTerminate')
        enabled = [bool] $task.Enabled
    }
    $loginTask.present = $true
    $loginTask.state = [string] $task.State
    $loginTask.mismatches = @($checks.Keys | Where-Object { -not $checks[$_] })
    $loginTask.matchesExpected = $loginTask.mismatches.Count -eq 0
}

$settingsPath = Join-Path $root 'settings.json'
$settings = if (Test-Path -LiteralPath $settingsPath -PathType Leaf) { & $fingerprint $settingsPath } else { [ordered]@{ present = $false; length = $null; lastWriteTimeUtc = $null; sha256 = $null } }

# 只打开已有的安装锁看它在不在：不新建、不取锁、不等待。锁在而没有权限打开它（提权的运行建的）也算在。
$inProgress = $false
try {
    $mutex = $null
    if ([Threading.Mutex]::TryOpenExisting($LockName, [ref] $mutex)) { $mutex.Dispose(); $inProgress = $true }
}
catch [UnauthorizedAccessException] { $inProgress = $true }

[ordered]@{
    schemaVersion = 1
    installRoot = $root
    manifest = $manifest
    commit = $commit
    aggregateSha256 = $aggregate
    exe = $exe
    running = $processes.Count -gt 0
    processes = $processes
    loginTask = $loginTask
    settings = $settings
    transactionMarkerPresent = Test-Path -LiteralPath (Join-Path $root 'install-transaction.json')
    installInProgress = $inProgress
} | ConvertTo-Json -Depth 5 -EscapeHandling EscapeNonAscii
