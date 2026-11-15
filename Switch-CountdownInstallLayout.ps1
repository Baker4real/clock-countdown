#requires -Version 7.0
<#
.SYNOPSIS
一次性布局切换（#259 / #287）：把办公室的安装从现有布局切到新布局，输出一份结果 JSON。
.DESCRIPTION
现有布局：登录任务经 pwsh 运行安装根里的 adapter 敏敏子倒计时.ps1，桌面上是启动器 敏敏子倒计时.exe（可能还有遗留的 .cmd），
可能另有遗留任务；安装根里除了程序目录 app 与用户数据，还有 adapter 与它的模块、启动器、图标、角色图、随包节假日数据和状态文件。
新布局：登录任务直接启动 app\MinminCountdown.exe，桌面上是快捷方式 敏敏子倒计时.lnk，安装根里只有 app、运行状态回执与用户数据。

切换按顺序：前置检查（安装根存在，新安装器 -CheckOnly 通过）→ 停止应用 → 备份到 <安装根>.layout-switch-backup：
完整复制安装根到 root\，导出现役与遗留任务的定义到 tasks\，复制桌面上的启动器文件到 desktop\，复制本脚本与新安装器 module
到 tools\，逐项核对哈希后最后写 backup.json → 用新安装器装入程序目录、更新登录任务、登记桌面快捷方式并启动到启动就绪 →
移除遗留任务、属于旧安装器的桌面启动器与安装根里的旧文件。用户数据（设置及其两代备份、恢复证据、诊断日志、节假日缓存）
不被移动或改写；安装根里认不出的条目原样保留并在结果里列出。
-Port 供测试注入计划任务、进程、交互会话、桌面目录与快捷方式（与新安装器同一组 port），-LockName 供测试隔离安装锁；
不传时操作真实 Windows。输出不含设置内容、凭据、主机名或账号。
#>
[CmdletBinding()]
param(
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
if ($null -eq $Port) { $Port = New-CountdownInstallerWindowsPorts }

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
    try {
        if (-not (Test-Path -LiteralPath $root -PathType Container)) { throw (New-CountdownSwitchRejection -Reason 'install-root-missing' -Message "安装根不存在：$root") }
        $check = Install-CountdownProgram @installerArguments -CheckOnly
        if (-not $check.succeeded) {
            $result.installer = $check
            throw (New-CountdownSwitchRejection -Reason ([string] $check.reason) -Message "新安装器的前置检查没通过（阶段 $($check.failedStage)）：$($check.error)")
        }
        $session = @(& $Port.GetInteractiveSessions)[0]
        $desktopDirectory = [System.IO.Path]::GetFullPath([string] (& $Port.GetDesktopDirectory $session))

        $stage = 'stop-application'
        $result.stoppedProcessCount = Stop-CountdownSwitchApplication

        $stage = 'backup'
        $manifest = New-CountdownSwitchBackup -DesktopDirectory $desktopDirectory
        $result.backup.action = 'created'
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
    }
    $result.transactionMarkerPresent = Test-Path -LiteralPath (Join-Path $root 'install-transaction.json')
    $result
}

Invoke-CountdownLayoutSwitch | ConvertTo-Json -Depth 10 -EscapeHandling EscapeNonAscii
