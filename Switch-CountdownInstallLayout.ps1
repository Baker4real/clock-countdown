#requires -Version 7.0
<#
.SYNOPSIS
一次性布局切换（#259 / #287）：把办公室的安装从现有布局切到新布局，或（-Rollback）按切换前的备份回退；输出一份结果 JSON。
.DESCRIPTION
现有布局：登录任务经 pwsh 运行安装根里的 adapter 敏敏子倒计时.ps1，桌面上是启动器 敏敏子倒计时.exe（可能还有遗留的 .cmd），
可能另有遗留任务；安装根里除了程序目录 app 与用户数据，还有 adapter 与它的模块、启动器、图标、角色图、随包节假日数据和状态文件。
新布局：登录任务直接启动 app\MinminCountdown.exe，桌面上是快捷方式 敏敏子倒计时.lnk，安装根里只有 app、运行状态回执与用户数据。

切换与回退都要替换并删除计划任务，旧安装器的登录任务是在提权的 PowerShell 里注册的：两者都要在以管理员身份运行的进程里运行。
前置检查只读地看当前进程是否提权（结果的 elevated）、按两个现有任务的 DACL 能否替换并删除它们，不能就指名停下（task-access-denied）。
切换按顺序：前置检查（安装根存在，已有备份可沿用，任务权限够，新安装器 -CheckOnly 通过）→ 停止应用 → 备份到 <安装根>.layout-switch-backup：
完整复制安装根到 root\，导出现役与遗留任务的定义到 tasks\，复制桌面上的启动器文件到 desktop\，复制本脚本与新安装器 module
到 tools\，逐项核对哈希后最后写 backup.json → 用新安装器装入程序目录、更新登录任务、登记桌面快捷方式并启动到启动就绪 →
移除遗留任务、属于旧安装器的桌面启动器与安装根里的旧文件。用户数据（设置及其两代备份、恢复证据、诊断日志、节假日缓存）
不被移动或改写；安装根里认不出的条目原样保留并在结果里列出。
备份不完整时切换不开始：已有备份目录而其中没有完整的 backup.json、或副本对不上时，在前置检查停下，什么都不动；备份途中失败时
删掉本次写了一半的备份，经登录任务重新启动旧版本，其余什么都没改。备份之后任何一步失败都停下报告，备份保留：新安装器失败时
由它自己回滚程序目录与登录任务并重新启动旧版本。再次运行切换会沿用这份完整备份接着做（不重新备份）；用过回退的备份不再沿用。

回退（-Rollback，从备份目录的 tools\ 运行）：核对备份完整与任务权限 → 停止应用 → 让安装根与备份逐项一致（被覆盖或删除的现有文件先复制到
备份目录下的 after-switch-<时刻>\）→ 按导出的定义恢复两个任务（切换前没有的删除）→ 按备份恢复桌面启动器文件，删除切换建立的
快捷方式 → 写 rollback.json → 经恢复的登录任务启动旧版本并等到启动就绪。中途失败时停下报告，再次运行回退会接着做。
-Port 供测试注入计划任务、进程、交互会话、桌面目录与快捷方式（与新安装器同一组 port，另加只读的 GetProcessIdentity 与
GetTaskSecurity），-LockName 供测试隔离安装锁；不传时操作真实 Windows。输出不含设置内容、凭据、主机名、账号或 SID。
#>
[CmdletBinding()]
param(
    [switch] $Rollback,
    [string] $PackageRoot = $PSScriptRoot,
    [string] $InstallRoot = 'D:\敏敏子倒计时',
    [string] $TaskName = 'Minmin Countdown',
    [string] $LegacyTaskName = '敏敏子倒计时',
    [string] $BackupRoot,
    [object] $Port,
    [string] $LockName
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'Countdown.Installer.psm1') -Force
if ($null -eq $Port) {
    $Port = New-CountdownInstallerWindowsPorts
    # 切换自己的两个只读 port（#288）：当前进程的令牌，与任务计划服务给出的任务 DACL（SDDL）。
    $Port | Add-Member -NotePropertyName GetProcessIdentity -NotePropertyValue {
        $identity = [System.Security.Principal.WindowsIdentity]::GetCurrent()
        try {
            [pscustomobject]@{
                # Administrators 在令牌里启用即提权；UAC 过滤后的令牌里它只用于拒绝。
                Elevated = [System.Security.Principal.WindowsPrincipal]::new($identity).IsInRole([System.Security.Principal.WindowsBuiltInRole]::Administrator)
                # Groups 只列启用的组，不含只用于拒绝的组与登录 SID。
                Sids = @(@($identity.User) + @($identity.Groups) | ForEach-Object { $_.Value })
            }
        }
        finally { $identity.Dispose() }
    }
    $Port | Add-Member -NotePropertyName GetTaskSecurity -NotePropertyValue {
        param([string] $Name)
        $service = New-Object -ComObject 'Schedule.Service'
        $service.Connect()
        # 4 = DACL_SECURITY_INFORMATION：只取 DACL。
        [string] $service.GetFolder('\').GetTask($Name).GetSecurityDescriptor(4)
    }
}

$utf8 = [System.Text.UTF8Encoding]::new($false)
# 旧安装器放在桌面上的启动器：启动器 EXE、它替换时的临时文件与更早的 .cmd。
$launcherNames = @('敏敏子倒计时.exe', '敏敏子倒计时.exe.new', '敏敏子倒计时.cmd')
$shortcutName = '敏敏子倒计时.lnk'
# 旧安装器装在安装根里、新布局不再需要的文件（Countdown.ReleaseContract.psm1 的 InstalledFiles 与 RetiredRuntimeFiles 中 app\ 之外的部分），
# 以及旧安装器、adapter 与启动器的状态文件。新版本从 app\ 读图标、角色图与随包节假日数据。目录只在删空之后删除。
$obsoleteFiles = @(
    '敏敏子倒计时.ps1', 'Countdown.InstallTransaction.psm1', 'Countdown.Notes.psm1', 'Countdown.Runtime.psm1', 'Countdown.Settings.psm1',
    'Countdown.Tags.psm1', 'Countdown.App.psm1', 'Countdown.Core.psm1', 'CountdownLauncher.exe', 'countdown-icon.ico',
    'sanrio-official-mymelody.png', 'sanrio-official-cinnamoroll.png', 'sanrio-official-kuromi.png',
    'holiday-data\2026.json', 'holiday-data\2027.json', 'holiday-data\README.md', 'holiday-data\LICENSE.txt',
    'launcher-state.json', 'launcher-state.json.tmp', 'startup-request.json', 'acceptance-status.json'
) + @(foreach ($character in 'mymelody', 'cinnamoroll', 'kuromi') { foreach ($pose in 'front', 'pose', 'reference') { "assets\floating-plush\floating-plush-$character-$pose.png" } }) +
    @(foreach ($part in 'outer-shell', 'header-blank', 'card-blank', 'mascots') { "assets\floating-plush\component-day-$part.png" })
$obsoleteDirectories = @('holiday-data', 'assets\floating-plush', 'assets')
# 新布局与用户数据的顶层条目（设置及其备份按 settings.json* 认），以及新安装器自己的事务条目；其余都算认不出的条目。
$knownEntries = @('app', 'runtime-status.json', 'settings-recovery-evidence', 'logs', 'holiday-cache',
    'app.candidate', 'app.previous', 'install-transaction', 'install-transaction.json', 'install-transaction.json.tmp')

$root = [System.IO.Path]::TrimEndingDirectorySeparator([System.IO.Path]::GetFullPath($ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($InstallRoot)))
$backup = if ([string]::IsNullOrWhiteSpace($BackupRoot)) { "$root.layout-switch-backup" }
else { [System.IO.Path]::TrimEndingDirectorySeparator([System.IO.Path]::GetFullPath($ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($BackupRoot))) }
$backupManifestPath = Join-Path $backup 'backup.json'
$exePath = Join-Path $root 'app\MinminCountdown.exe'

function New-CountdownSwitchRejection {
    param([Parameter(Mandatory)] [string] $Reason, [Parameter(Mandatory)] [string] $Message)
    $exception = [System.InvalidOperationException]::new($Message)
    $exception.Data['CountdownSwitchReason'] = $Reason
    $exception
}

function ConvertTo-CountdownSwitchBoundedText {
    param([AllowEmptyString()] [string] $Text)
    $value = ([string] $Text -replace '\s+', ' ').Trim()
    if ($value.Length -gt 400) { $value = $value.Substring(0, 400) + '…' }
    $value
}

function Get-CountdownSwitchSha256 {
    param([Parameter(Mandatory)] [string] $Path)
    (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash
}

function Copy-CountdownSwitchFile {
    param([Parameter(Mandatory)] [string] $Source, [Parameter(Mandatory)] [string] $Destination)
    [void] [System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName($Destination))
    # File.Copy 保留修改时间。
    [System.IO.File]::Copy($Source, $Destination, $true)
}

# 目录树：相对路径的目录清单与文件清单（长度、修改时间、SHA-256）。
function Get-CountdownSwitchTree {
    param([Parameter(Mandatory)] [string] $Directory)
    $prefix = $Directory.TrimEnd('\') + '\'
    $directories = [System.Collections.Generic.List[string]]::new()
    $files = [System.Collections.Generic.List[object]]::new()
    foreach ($item in @(Get-ChildItem -LiteralPath $Directory -Recurse -Force)) {
        $relative = $item.FullName.Substring($prefix.Length)
        if ($item.PSIsContainer) { $directories.Add($relative); continue }
        $files.Add([ordered]@{ path = $relative; length = $item.Length; lastWriteTimeUtc = $item.LastWriteTimeUtc.ToString('o'); sha256 = Get-CountdownSwitchSha256 -Path $item.FullName })
    }
    [pscustomobject]@{ Directories = $directories.ToArray(); Files = $files.ToArray() }
}

# 当前进程能否替换并删除一个任务：按任务 DACL 的顺序，对令牌里启用的 SID 累计允许与拒绝的权限（同 Windows 的访问检查，
# 只用于拒绝的组与属主的隐含权限不计）。替换定义要 FILE_GENERIC_WRITE（0x120116），删除要 DELETE（0x10000）；
# GENERIC_ALL 与 GENERIC_WRITE 按文件的映射展开。没有 DACL 时谁都有全部权限。
function Test-CountdownSwitchTaskWritable {
    param([Parameter(Mandatory)] [string] $Sddl, [Parameter(Mandatory)] [System.Collections.Generic.HashSet[string]] $Sids)

    $acl = [System.Security.AccessControl.RawSecurityDescriptor]::new($Sddl).DiscretionaryAcl
    if ($null -eq $acl) { return $true }
    $required = 0x130116
    $granted = 0
    $denied = 0
    foreach ($ace in $acl) {
        if ($ace -isnot [System.Security.AccessControl.CommonAce] -or $ace.AceFlags.HasFlag([System.Security.AccessControl.AceFlags]::InheritOnly) -or
            -not $Sids.Contains($ace.SecurityIdentifier.Value)) { continue }
        $mask = $ace.AccessMask
        if ($mask -band 0x10000000) { $mask = $mask -bor 0x1F01FF }
        if ($mask -band 0x40000000) { $mask = $mask -bor 0x120116 }
        if ($ace.AceQualifier -eq [System.Security.AccessControl.AceQualifier]::AccessDenied) { $denied = $denied -bor ($mask -band -bnot $granted) }
        elseif ($ace.AceQualifier -eq [System.Security.AccessControl.AceQualifier]::AccessAllowed) { $granted = $granted -bor ($mask -band -bnot $denied) }
    }
    ($granted -band $required) -eq $required
}

# 前置检查（#288）：只读地看当前进程是否提权、能否替换并删除现有的两个任务（切换与回退都要改它们），改不了任何一个就停下，
# 指名是哪个任务。不存在的任务不查：新建任务不用改别的任务。
function Assert-CountdownSwitchTaskAccess {
    param([Parameter(Mandatory)] [System.Collections.IDictionary] $Result, [Parameter(Mandatory)] [string] $Action)

    $identity = & $Port.GetProcessIdentity
    $Result.elevated = [bool] $identity.Elevated
    $sids = [System.Collections.Generic.HashSet[string]]::new([string[]] @($identity.Sids), [System.StringComparer]::OrdinalIgnoreCase)
    $denied = @(foreach ($name in @($TaskName, $LegacyTaskName)) {
            if ($null -eq (& $Port.GetTask $name)) { continue }
            try { $writable = Test-CountdownSwitchTaskWritable -Sddl ([string] (& $Port.GetTaskSecurity $name)) -Sids $sids }
            catch { "$name（读不到它的 DACL：$(($_.Exception.Message -replace '\s+', ' ').Trim())）"; continue }
            if (-not $writable) { $name }
        })
    if ($denied.Count -gt 0) {
        $process = if ($Result.elevated) { '当前进程已提权' } else { '当前进程没有提权' }
        $advice = if ($Result.elevated) { '先查明这些任务的权限设置。' } else { "在以管理员身份运行的 PowerShell 里运行$Action。" }
        throw (New-CountdownSwitchRejection -Reason 'task-access-denied' -Message "$process，按任务的 DACL 不能替换或删除计划任务 $($denied -join '、')：$advice")
    }
}

# 已安装 EXE 的进程（映像路径是安装根的 app\MinminCountdown.exe）。
function Get-CountdownSwitchProcesses {
    @(& $Port.GetProcesses 'MinminCountdown.exe' | Where-Object {
            $property = $_.PSObject.Properties['ExecutablePath']
            $null -ne $property -and -not [string]::IsNullOrWhiteSpace([string] $property.Value) -and
                [string]::Equals([System.IO.Path]::GetFullPath([string] $property.Value), $exePath, [System.StringComparison]::OrdinalIgnoreCase)
        })
}

# 停止应用：结束已安装 EXE 的进程，直到它们都退出，并且登录任务没有运行中的实例（新布局的任务实例跟着进程）。
# 与新安装器一样不经计划任务停止。返回开始时在运行的进程数。
function Stop-CountdownSwitchApplication {
    $running = $null
    $deadline = [DateTime]::UtcNow.AddSeconds(10)
    while ($true) {
        $remaining = @(Get-CountdownSwitchProcesses)
        if ($null -eq $running) { $running = $remaining.Count }
        $task = & $Port.GetTask $TaskName
        $taskRunning = $null -ne $task -and [string] $task.State -eq 'Running'
        if ($remaining.Count -eq 0 -and -not $taskRunning) { return $running }
        if ([DateTime]::UtcNow -ge $deadline) {
            throw (New-CountdownSwitchRejection -Reason 'application-still-running' -Message "10 秒内没能停止应用：仍有 $($remaining.Count) 个已安装 EXE 的进程，或登录任务 $TaskName 仍在运行。")
        }
        foreach ($process in $remaining) { [void] (& $Port.StopProcess ([int] $process.ProcessId)) }
        Start-Sleep -Milliseconds 200
    }
}

# 备份：安装根的完整副本、两个任务导出的定义、桌面上的启动器文件（含已有的同名快捷方式）、回退要用的本脚本与新安装器 module；
# 逐项核对副本的哈希之后最后写 backup.json，它在就表示备份完整。返回读回的 backup.json。
function New-CountdownSwitchBackup {
    param([Parameter(Mandatory)] [string] $DesktopDirectory)

    [void] [System.IO.Directory]::CreateDirectory($backup)
    $tree = Get-CountdownSwitchTree -Directory $root
    [void] [System.IO.Directory]::CreateDirectory((Join-Path $backup 'root'))
    foreach ($directory in $tree.Directories) { [void] [System.IO.Directory]::CreateDirectory((Join-Path $backup "root\$directory")) }
    foreach ($file in $tree.Files) { Copy-CountdownSwitchFile -Source (Join-Path $root $file.path) -Destination (Join-Path $backup "root\$($file.path)") }

    $tasks = [ordered]@{}
    foreach ($entry in @(@('current', $TaskName), @('legacy', $LegacyTaskName))) {
        $definition = & $Port.ExportTask $entry[1]
        $tasks[$entry[0]] = [ordered]@{ name = $entry[1]; present = $null -ne $definition; sha256 = $null }
        if ($null -ne $definition) {
            $path = Join-Path $backup "tasks\$($entry[0]).task"
            [void] [System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName($path))
            [System.IO.File]::WriteAllText($path, [string] $definition, $utf8)
            $tasks[$entry[0]].sha256 = Get-CountdownSwitchSha256 -Path $path
        }
    }

    $desktop = @(foreach ($name in @($launcherNames) + @($shortcutName)) {
            $path = Join-Path $DesktopDirectory $name
            if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { continue }
            $sha256 = Get-CountdownSwitchSha256 -Path $path
            Copy-CountdownSwitchFile -Source $path -Destination (Join-Path $backup "desktop\$name")
            [ordered]@{ name = $name; sha256 = $sha256 }
        })

    $tools = @(foreach ($source in @($PSCommandPath, (Join-Path $PSScriptRoot 'Countdown.Installer.psm1'))) {
            $name = [System.IO.Path]::GetFileName($source)
            $sha256 = Get-CountdownSwitchSha256 -Path $source
            Copy-CountdownSwitchFile -Source $source -Destination (Join-Path $backup "tools\$name")
            [ordered]@{ name = $name; sha256 = $sha256 }
        })

    $document = [ordered]@{
        schemaVersion = 1
        complete = $true
        createdAtUtc = [DateTimeOffset]::UtcNow.ToString('o')
        installRoot = $root
        taskName = $TaskName
        legacyTaskName = $LegacyTaskName
        desktopDirectory = $DesktopDirectory
        root = [ordered]@{ directories = @($tree.Directories); files = @($tree.Files) }
        tasks = $tasks
        desktop = $desktop
        tools = $tools
    }
    $text = $document | ConvertTo-Json -Depth 6 -EscapeHandling EscapeNonAscii
    $manifest = $text | ConvertFrom-Json -DateKind String
    $problems = @(Test-CountdownSwitchBackup -Manifest $manifest)
    if ($problems.Count -gt 0) { throw (New-CountdownSwitchRejection -Reason 'backup-incomplete' -Message "备份副本与原件不一致：$($problems[0])") }
    $temporaryPath = "$backupManifestPath.tmp"
    [System.IO.File]::WriteAllText($temporaryPath, $text, $utf8)
    [System.IO.File]::Move($temporaryPath, $backupManifestPath)
    $manifest
}

# 核对备份：backup.json 列出的每一份副本都在，SHA-256 与清单一致。返回问题清单，完整时为空。
function Test-CountdownSwitchBackup {
    param([Parameter(Mandatory)] [object] $Manifest)

    $problems = [System.Collections.Generic.List[string]]::new()
    $check = {
        param([string] $Path, [string] $Sha256, [string] $Label)
        if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { $problems.Add("缺少$Label") }
        elseif ((Get-CountdownSwitchSha256 -Path $Path) -ne $Sha256) { $problems.Add("$Label 的 SHA-256 与 backup.json 不一致") }
    }
    foreach ($directory in @($Manifest.root.directories)) {
        if (-not (Test-Path -LiteralPath (Join-Path $backup "root\$directory") -PathType Container)) { $problems.Add("缺少安装根副本里的目录 $directory") }
    }
    foreach ($file in @($Manifest.root.files)) { & $check (Join-Path $backup "root\$($file.path)") $file.sha256 "安装根副本 $($file.path)" }
    foreach ($kind in @('current', 'legacy')) {
        $task = $Manifest.tasks.$kind
        if ($task.present) { & $check (Join-Path $backup "tasks\$kind.task") $task.sha256 "任务定义 $kind" }
    }
    foreach ($file in @($Manifest.desktop)) { & $check (Join-Path $backup "desktop\$($file.name)") $file.sha256 "桌面文件副本 $($file.name)" }
    foreach ($file in @($Manifest.tools)) { & $check (Join-Path $backup "tools\$($file.name)") $file.sha256 "回退工具 $($file.name)" }
    $problems.ToArray()
}

# 已有的备份：没有备份目录时返回空；有 backup.json 且逐项核对通过时返回它；目录在而备份不完整（没写下 backup.json、
# 读不出，或副本对不上）时拒绝，什么都不动。
function Read-CountdownSwitchBackup {
    if (-not (Test-Path -LiteralPath $backup)) { return $null }
    $manifest = $null
    try { $manifest = [System.IO.File]::ReadAllText($backupManifestPath) | ConvertFrom-Json -DateKind String }
    catch { $manifest = $null }
    $complete = $null -ne $manifest -and $null -ne $manifest.PSObject.Properties['complete'] -and $manifest.complete -eq $true
    $problems = @(if ($complete) { Test-CountdownSwitchBackup -Manifest $manifest } else { '没有完整的 backup.json' })
    if ($problems.Count -gt 0) {
        throw (New-CountdownSwitchRejection -Reason 'backup-incomplete' -Message "备份目录 $backup 已存在但备份不完整（$($problems[0])，共 $($problems.Count) 项问题）。")
    }
    $manifest
}

# 经登录任务启动应用并等到启动就绪：删除旧的运行状态回执后启动，新回执写明首帧已渲染，写它的进程在运行且映像路径是已安装 EXE。
# 返回该进程；30 秒内达不到时抛出。
function Start-CountdownSwitchApplication {
    $statusPath = Join-Path $root 'runtime-status.json'
    if (Test-Path -LiteralPath $statusPath) { [System.IO.File]::Delete($statusPath) }
    [void] (& $Port.StartTask $TaskName)
    $deadline = [DateTime]::UtcNow.AddSeconds(30)
    while ($true) {
        $receipt = $null
        try {
            $stream = [System.IO.File]::Open($statusPath, 'Open', 'Read', 'ReadWrite, Delete')
            try { $receipt = [System.IO.StreamReader]::new($stream, $utf8).ReadToEnd() | ConvertFrom-Json }
            finally { $stream.Dispose() }
        }
        catch { $receipt = $null }
        if ($null -ne $receipt -and $null -ne $receipt.PSObject.Properties['processId'] -and $null -ne $receipt.PSObject.Properties['stage'] -and [string] $receipt.stage -eq 'window-ready') {
            $process = @(Get-CountdownSwitchProcesses | Where-Object { [long] $_.ProcessId -eq [long] $receipt.processId })
            if ($process.Count -gt 0) { return [ordered]@{ processId = [int] $process[0].ProcessId; sessionId = [int] $process[0].SessionId } }
        }
        if ([DateTime]::UtcNow -ge $deadline) { throw "经登录任务 $TaskName 启动后 30 秒内没有达到启动就绪。" }
        Start-Sleep -Milliseconds 250
    }
}

# 移除遗留：遗留任务；桌面上属于旧安装器的启动器（EXE 与它的临时文件按内容等于备份里的 CountdownLauncher.exe 认，.cmd 按旧安装器
# 写的原文认），不属于的保留并写明；安装根里的旧文件与删空的旧目录。最后列出安装根里认不出的顶层条目（保留不动）。
function Remove-CountdownSwitchLegacy {
    param([Parameter(Mandatory)] [object] $Manifest)

    $report = [ordered]@{ task = 'absent'; desktop = @(); removedRootEntries = @(); unknownRootEntries = @() }
    if ($null -ne (& $Port.GetTask $LegacyTaskName)) {
        [void] (& $Port.RestoreTask $LegacyTaskName $null)
        $report.task = 'removed'
    }

    $launcher = @(@($Manifest.root.files) | Where-Object { $_.path -eq 'CountdownLauncher.exe' })
    $launcherSha256 = if ($launcher.Count -eq 1) { [string] $launcher[0].sha256 } else { $null }
    $legacyCommand = "@echo off`r`nC:\Windows\System32\schtasks.exe /Run /TN `"$TaskName`" >nul 2>&1`r`nexit /b %errorlevel%`r`n"
    $report.desktop = @(foreach ($name in $launcherNames) {
            $path = Join-Path ([string] $Manifest.desktopDirectory) $name
            if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { [ordered]@{ name = $name; action = 'absent' }; continue }
            $owned = if ($name.EndsWith('.cmd')) { [System.IO.File]::ReadAllText($path, [System.Text.Encoding]::ASCII) -ceq $legacyCommand }
            else { $null -ne $launcherSha256 -and (Get-CountdownSwitchSha256 -Path $path) -eq $launcherSha256 }
            if ($owned) {
                [System.IO.File]::Delete($path)
                [ordered]@{ name = $name; action = 'removed' }
            }
            else { [ordered]@{ name = $name; action = 'kept-foreign' } }
        })

    $removed = [System.Collections.Generic.List[string]]::new()
    foreach ($relative in $obsoleteFiles) {
        $path = Join-Path $root $relative
        if (Test-Path -LiteralPath $path -PathType Leaf) {
            [System.IO.File]::Delete($path)
            $removed.Add($relative)
        }
    }
    foreach ($relative in $obsoleteDirectories) {
        $path = Join-Path $root $relative
        if ((Test-Path -LiteralPath $path -PathType Container) -and @(Get-ChildItem -LiteralPath $path -Force).Count -eq 0) {
            [System.IO.Directory]::Delete($path)
            $removed.Add("$relative\")
        }
    }
    $report.removedRootEntries = $removed.ToArray()
    $report.unknownRootEntries = @(Get-ChildItem -LiteralPath $root -Force | Where-Object { $_.Name -notin $knownEntries -and $_.Name -notlike 'settings.json*' } |
            ForEach-Object { $_.Name } | Sort-Object)
    $report
}

function Invoke-CountdownLayoutSwitch {
    $result = [ordered]@{
        schemaVersion = 1
        mode = 'switch'
        succeeded = $false
        failedStage = $null
        reason = $null
        error = $null
        installRoot = $root
        taskName = $TaskName
        legacyTaskName = $LegacyTaskName
        # 前置检查读到的当前进程是否提权；没走到那一步时为空。
        elevated = $null
        backup = [ordered]@{ directory = $backup; action = $null; complete = $false; rootFileCount = $null; tasks = $null; desktopFiles = @() }
        stoppedProcessCount = 0
        installer = $null
        readyProcess = $null
        restartedProcess = $null
        # 与新安装器的结果同名：新安装器失败时是它报告的回滚结果，没走到安装时为 not-needed。
        rollback = [ordered]@{ outcome = 'not-needed' }
        legacy = $null
        transactionMarkerPresent = $false
        nextStep = $null
    }
    $installerArguments = @{ PackageRoot = $PackageRoot; InstallRoot = $root; TaskName = $TaskName; Ports = $Port }
    if (-not [string]::IsNullOrWhiteSpace($LockName)) { $installerArguments.LockName = $LockName }
    $stage = 'precheck'
    $backupStarted = $false
    try {
        if (-not (Test-Path -LiteralPath $root -PathType Container)) { throw (New-CountdownSwitchRejection -Reason 'install-root-missing' -Message "安装根不存在：$root") }
        $manifest = Read-CountdownSwitchBackup
        if ($null -ne $manifest) {
            $result.backup.complete = $true
            if (Test-Path -LiteralPath (Join-Path $backup 'rollback.json')) {
                throw (New-CountdownSwitchRejection -Reason 'backup-used-for-rollback' -Message "备份 $backup 已经用于回退，安装根回到了切换前，这份备份不再沿用。")
            }
        }
        Assert-CountdownSwitchTaskAccess -Result $result -Action '切换'
        $check = Install-CountdownProgram @installerArguments -CheckOnly
        if (-not $check.succeeded) {
            $result.installer = $check
            throw (New-CountdownSwitchRejection -Reason ([string] $check.reason) -Message "新安装器的前置检查没通过（阶段 $($check.failedStage)）：$($check.error)")
        }
        if ($null -eq $manifest) {
            $session = @(& $Port.GetInteractiveSessions)[0]
            $desktopDirectory = [System.IO.Path]::GetFullPath([string] (& $Port.GetDesktopDirectory $session))

            $stage = 'stop-application'
            $result.stoppedProcessCount = Stop-CountdownSwitchApplication

            $stage = 'backup'
            $backupStarted = $true
            $manifest = New-CountdownSwitchBackup -DesktopDirectory $desktopDirectory
            $result.backup.action = 'created'
        }
        else {
            # 上一次切换在备份之后停下：沿用那份完整备份接着做，不重新备份（安装根可能已被改过），停应用交给新安装器。
            $result.backup.action = 'reused'
        }
        $result.backup.complete = $true
        $result.backup.rootFileCount = @($manifest.root.files).Count
        $result.backup.tasks = [ordered]@{ current = [bool] $manifest.tasks.current.present; legacy = [bool] $manifest.tasks.legacy.present }
        $result.backup.desktopFiles = @(@($manifest.desktop) | ForEach-Object { $_.name })

        $stage = 'install'
        $installer = Install-CountdownProgram @installerArguments
        $result.installer = $installer
        $result.readyProcess = $installer.readyProcess
        $result.restartedProcess = $installer.restartedProcess
        $result.rollback = $installer.rollback
        if (-not $installer.succeeded) {
            throw (New-CountdownSwitchRejection -Reason ([string] $installer.reason) -Message "新安装器报告失败（阶段 $($installer.failedStage)，回滚 $($installer.rollback.outcome)）：$($installer.error)")
        }

        $stage = 'remove-legacy'
        $result.legacy = Remove-CountdownSwitchLegacy -Manifest $manifest
        $result.succeeded = $true
    }
    catch {
        $result.failedStage = $stage
        $reason = $_.Exception.Data['CountdownSwitchReason']
        $result.reason = if ($null -ne $reason) { [string] $reason } else { 'unexpected-error' }
        $result.error = ConvertTo-CountdownSwitchBoundedText -Text $_.Exception.Message
        if ($backupStarted -and $stage -eq 'backup') {
            # 备份阶段失败时除了停下应用什么都没改：删掉本次写了一半的备份（前置检查确认过之前没有备份目录）。
            try { if (Test-Path -LiteralPath $backup) { [System.IO.Directory]::Delete($backup, $true) } }
            catch { $result.error = ConvertTo-CountdownSwitchBoundedText -Text "$($result.error)；写了一半的备份没删掉：$($_.Exception.Message)" }
        }
        # 切换停下了旧版本、随后在动安装之前停下（备份失败，或新安装器什么都没改就失败）时，经登录任务重新启动旧版本。
        if ($result.stoppedProcessCount -gt 0 -and $stage -in @('backup', 'install') -and $result.rollback.outcome -eq 'not-needed' -and @(Get-CountdownSwitchProcesses).Count -eq 0) {
            try { $result.restartedProcess = Start-CountdownSwitchApplication }
            catch { $result.error = ConvertTo-CountdownSwitchBoundedText -Text "$($result.error)；重新启动旧版本失败：$($_.Exception.Message)" }
        }
    }
    $result.transactionMarkerPresent = Test-Path -LiteralPath (Join-Path $root 'install-transaction.json')
    if (-not $result.succeeded) {
        $result.nextStep = if ($result.reason -eq 'backup-used-for-rollback') {
            "要再次切换，先把备份目录 $backup 整个移走（里面的 after-switch-* 是回退时留下的切换后文件），再运行切换，它会重新备份。"
        }
        elseif ($result.backup.complete) {
            "切换没有走完，切换前的备份完整保留在 $backup：排除原因后再次运行切换，会沿用这份备份接着做；要回到切换前，就按这份备份回退（-Rollback）。"
        }
        elseif ($result.reason -eq 'backup-incomplete') {
            "备份目录 $backup 里的备份不完整，切换没有开始：确认里面没有要留的东西后把整个目录移走，再运行切换，它会重新备份。"
        }
        else { '切换没有开始，安装没有改动：排除原因后再运行切换。' }
    }
    $result
}

# 回退时让安装根与备份逐项一致：备份里没有的现有文件先复制到保留目录再删除，内容与备份不同的先复制到保留目录再按备份覆盖，
# 缺的与修改时间不同的按备份复制（File.Copy 保留修改时间），备份里没有的目录删空后删除；最后按路径、长度、修改时间与
# SHA-256 核对整个安装根。返回恢复、删除与保留了哪些条目。
function Restore-CountdownSwitchRoot {
    param([Parameter(Mandatory)] [object] $Manifest, [Parameter(Mandatory)] [string] $PreservedDirectory)

    $comparer = [System.StringComparer]::OrdinalIgnoreCase
    $expectedFiles = [System.Collections.Generic.Dictionary[string, object]]::new($comparer)
    foreach ($file in @($Manifest.root.files)) { $expectedFiles[[string] $file.path] = $file }
    $expectedDirectories = [System.Collections.Generic.HashSet[string]]::new([string[]] @($Manifest.root.directories), $comparer)
    $restored = [System.Collections.Generic.List[string]]::new()
    $removed = [System.Collections.Generic.List[string]]::new()
    $preserved = [System.Collections.Generic.List[string]]::new()
    $line = { param($File) "$($File.path)|$($File.length)|$($File.lastWriteTimeUtc)|$($File.sha256)" }

    [void] [System.IO.Directory]::CreateDirectory($root)
    $currentFiles = [System.Collections.Generic.Dictionary[string, object]]::new($comparer)
    foreach ($file in (Get-CountdownSwitchTree -Directory $root).Files) {
        $currentFiles[[string] $file.path] = $file
        $expected = if ($expectedFiles.ContainsKey($file.path)) { $expectedFiles[$file.path] } else { $null }
        if ($null -eq $expected -or $file.sha256 -ne $expected.sha256) {
            Copy-CountdownSwitchFile -Source (Join-Path $root $file.path) -Destination (Join-Path $PreservedDirectory "root\$($file.path)")
            $preserved.Add($file.path)
        }
        if ($null -eq $expected) {
            [System.IO.File]::Delete((Join-Path $root $file.path))
            $removed.Add($file.path)
        }
    }
    foreach ($directory in $expectedDirectories) { [void] [System.IO.Directory]::CreateDirectory((Join-Path $root $directory)) }
    foreach ($file in @($Manifest.root.files)) {
        if ($currentFiles.ContainsKey($file.path) -and (& $line $currentFiles[$file.path]) -eq (& $line $file)) { continue }
        Copy-CountdownSwitchFile -Source (Join-Path $backup "root\$($file.path)") -Destination (Join-Path $root $file.path)
        $restored.Add($file.path)
    }
    $prefix = $root + '\'
    foreach ($directory in @(Get-ChildItem -LiteralPath $root -Recurse -Force -Directory | Sort-Object { $_.FullName.Length } -Descending)) {
        $relative = $directory.FullName.Substring($prefix.Length)
        if (-not $expectedDirectories.Contains($relative)) {
            [System.IO.Directory]::Delete($directory.FullName)
            $removed.Add("$relative\")
        }
    }

    $after = Get-CountdownSwitchTree -Directory $root
    $actualText = (@(@($after.Directories) | ForEach-Object { "$_\" }) + @(@($after.Files) | ForEach-Object { & $line $_ }) | Sort-Object) -join "`n"
    $expectedText = (@(@($Manifest.root.directories) | ForEach-Object { "$_\" }) + @(@($Manifest.root.files) | ForEach-Object { & $line $_ }) | Sort-Object) -join "`n"
    if ($actualText -ne $expectedText) { throw (New-CountdownSwitchRejection -Reason 'root-mismatch' -Message '恢复之后安装根仍与备份不一致。') }
    [ordered]@{ restoredFiles = $restored.ToArray(); removedEntries = $removed.ToArray(); preservedFiles = $preserved.ToArray() }
}

# 回退一个任务：备份里有定义时按它整份恢复；切换前没有这个任务时，现在有就删除。
function Restore-CountdownSwitchTask {
    param([Parameter(Mandatory)] [string] $Name, [Parameter(Mandatory)] [object] $Entry, [Parameter(Mandatory)] [string] $Kind)

    if ([bool] $Entry.present) {
        [void] (& $Port.RestoreTask $Name ([System.IO.File]::ReadAllText((Join-Path $backup "tasks\$Kind.task"), $utf8)))
        return 'restored'
    }
    if ($null -eq (& $Port.GetTask $Name)) { return 'absent' }
    [void] (& $Port.RestoreTask $Name $null)
    'removed'
}

# 回退桌面：切换前在的启动器文件（与快捷方式）按备份恢复，被替换的现有文件先复制到保留目录；切换前没有、指向本安装根已安装
# EXE 的快捷方式是切换建立的，保留一份后删除；其余原样不动。
function Restore-CountdownSwitchDesktop {
    param([Parameter(Mandatory)] [object] $Manifest, [Parameter(Mandatory)] [string] $PreservedDirectory)

    $directory = [string] $Manifest.desktopDirectory
    $saved = @{}
    foreach ($file in @($Manifest.desktop)) { $saved[[string] $file.name] = $file }
    @(foreach ($name in @($launcherNames) + @($shortcutName)) {
            $path = Join-Path $directory $name
            $exists = Test-Path -LiteralPath $path -PathType Leaf
            if ($saved.ContainsKey($name)) {
                if ($exists -and (Get-CountdownSwitchSha256 -Path $path) -eq $saved[$name].sha256) { [ordered]@{ name = $name; action = 'unchanged' }; continue }
                if ($exists) { Copy-CountdownSwitchFile -Source $path -Destination (Join-Path $PreservedDirectory "desktop\$name") }
                Copy-CountdownSwitchFile -Source (Join-Path $backup "desktop\$name") -Destination $path
                [ordered]@{ name = $name; action = 'restored' }
                continue
            }
            if (-not $exists) { [ordered]@{ name = $name; action = 'absent' }; continue }
            $link = if ($name -eq $shortcutName) { & $Port.ReadShortcut $path } else { $null }
            if ($null -ne $link -and [bool] $link.IsShellLink -and [string]::Equals(([string] $link.TargetPath).Trim('"'), $exePath, [System.StringComparison]::OrdinalIgnoreCase)) {
                Copy-CountdownSwitchFile -Source $path -Destination (Join-Path $PreservedDirectory "desktop\$name")
                [void] (& $Port.RemoveShortcut $path)
                [ordered]@{ name = $name; action = 'removed' }
            }
            else { [ordered]@{ name = $name; action = 'kept' } }
        })
}

function Invoke-CountdownLayoutRollback {
    $result = [ordered]@{
        schemaVersion = 1
        mode = 'rollback'
        succeeded = $false
        failedStage = $null
        reason = $null
        error = $null
        installRoot = $root
        taskName = $TaskName
        legacyTaskName = $LegacyTaskName
        elevated = $null
        backupDirectory = $backup
        # 回退时被覆盖或删除的现有文件（切换后的程序目录、切换后改过的用户数据等）的副本，没有时为空。
        preservedDirectory = $null
        stoppedProcessCount = 0
        root = $null
        tasks = $null
        desktop = @()
        restartedProcess = $null
        nextStep = $null
    }
    $stage = 'precheck'
    try {
        $manifest = Read-CountdownSwitchBackup
        if ($null -eq $manifest) { throw (New-CountdownSwitchRejection -Reason 'backup-missing' -Message "没有切换前的备份：$backup") }
        Assert-CountdownSwitchTaskAccess -Result $result -Action '回退'

        $stage = 'stop-application'
        $result.stoppedProcessCount = Stop-CountdownSwitchApplication

        $stage = 'restore-root'
        $preserved = Join-Path $backup ('after-switch-' + [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfffZ'))
        $result.preservedDirectory = $preserved
        $result.root = Restore-CountdownSwitchRoot -Manifest $manifest -PreservedDirectory $preserved

        $stage = 'restore-tasks'
        $result.tasks = [ordered]@{
            current = Restore-CountdownSwitchTask -Name $TaskName -Entry $manifest.tasks.current -Kind 'current'
            legacy = Restore-CountdownSwitchTask -Name $LegacyTaskName -Entry $manifest.tasks.legacy -Kind 'legacy'
        }

        $stage = 'restore-desktop'
        $result.desktop = Restore-CountdownSwitchDesktop -Manifest $manifest -PreservedDirectory $preserved
        if (-not (Test-Path -LiteralPath $preserved)) { $result.preservedDirectory = $null }
        # 记下这份备份已用于回退：之后的切换不再沿用它。
        $record = [ordered]@{ schemaVersion = 1; rolledBackAtUtc = [DateTimeOffset]::UtcNow.ToString('o'); preservedDirectory = $result.preservedDirectory }
        [System.IO.File]::WriteAllText((Join-Path $backup 'rollback.json'), ($record | ConvertTo-Json -EscapeHandling EscapeNonAscii), $utf8)

        $stage = 'start'
        if ([bool] $manifest.tasks.current.present) { $result.restartedProcess = Start-CountdownSwitchApplication }
        $result.succeeded = $true
    }
    catch {
        $result.failedStage = $stage
        $reason = $_.Exception.Data['CountdownSwitchReason']
        $result.reason = if ($null -ne $reason) { [string] $reason } else { 'unexpected-error' }
        $result.error = ConvertTo-CountdownSwitchBoundedText -Text $_.Exception.Message
        $result.nextStep = if ($result.reason -in @('backup-missing', 'backup-incomplete')) { "回退没有开始，什么都没改：备份 $backup 不在或不完整，没法按它回退。" }
        elseif ($stage -eq 'precheck') { '回退没有开始，什么都没改：排除原因后再运行回退。' }
        else { "回退没有走完，备份原样保留在 $backup：排除原因后再次运行回退，它会接着把安装根、两个任务与桌面启动器恢复成备份的样子。" }
    }
    $result
}

$(if ($Rollback) { Invoke-CountdownLayoutRollback } else { Invoke-CountdownLayoutSwitch }) | ConvertTo-Json -Depth 10 -EscapeHandling EscapeNonAscii
