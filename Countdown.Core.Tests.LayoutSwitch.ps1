# Countdown.Core.Tests.ps1 的 LayoutSwitch 分段：入口按顺序点源执行，共用入口的断言函数与已建立的状态，不单独运行。

# #287 一次性布局切换：在按办公室现有布局搭出的临时安装根上运行 Switch-CountdownInstallLayout.ps1（切换与 -Rollback 回退）。
# 现有布局：安装根里有 PowerShell adapter 与它的模块、启动器 CountdownLauncher.exe、根目录下的图标、角色图与随包节假日数据、
# 启动器与启动请求的状态文件和一个退役残留；登录任务经 pwsh 运行 adapter，adapter 启动 app\MinminCountdown.exe 后退出；另有遗留任务；
# 桌面上是启动器 EXE 与遗留的 .cmd。计划任务、进程、交互会话与桌面目录经测试 port 注入，桌面快捷方式用生产 port 读写临时桌面。
# 断言安装根、桌面、port 里的任务与进程、备份目录、结果 JSON 和状态命令（#276）的读回；不碰真实的计划任务、桌面或安装。
$switchScript = Join-Path $PSScriptRoot 'Switch-CountdownInstallLayout.ps1'
$switchStatusScript = Join-Path $PSScriptRoot 'Get-CountdownInstallStatus.ps1'
$switchTempRoot = [System.IO.Path]::GetTempPath()
$switchTestRoot = Join-Path $switchTempRoot ('clock-countdown-layout-switch-test-' + [guid]::NewGuid().ToString('N'))
$switchUtf8 = [System.Text.UTF8Encoding]::new($false)
# 每次运行用自己的锁名，不和本机真实安装或别处并行的测试争同一把全机锁。
$switchLockName = 'Local\MinminCountdownLayoutSwitchTest-' + [guid]::NewGuid().ToString('N')
# 包里的新版本：文件、各自的 SHA-256 与聚合哈希是独立算好的字面量（与 Installer 分段的 v2 相同），提交是本分段自己的。
$switchCommit = '3c91d5e7f9a1b3c5d7e9f1a3b5c7d9e1f3a5c7e9'
$switchAggregate = '2A451524B97FED26B6B7B535A88AAA1129446AE62535044D66DF007CF09D6105'
$switchPackageFiles = @(
    [pscustomobject]@{ Path = 'app\holiday-data\2027.json'; Text = 'installer holiday v2'; Sha256 = '5987A3F052924557E59C0E387723DDEED9E53B74997FB8FF418552117EDC90DB' }
    [pscustomobject]@{ Path = 'app\MinminCountdown.dll'; Text = 'installer dll v2'; Sha256 = 'DB2617243BF6059E225265C27CFFFE1E01CF5B9B78BDBB67B6789CB8BA8B548D' }
    [pscustomobject]@{ Path = 'app\MinminCountdown.exe'; Text = 'installer exe shared by every build'; Sha256 = 'C62B3E048197B6CDE441B0C583B62423D0B16175267038D991B6D1278CE28388' }
)
# 用户数据：设置、两代设置备份、恢复证据、诊断日志与节假日缓存（顶层条目），切换前后逐字节不变。
$switchUserDataFiles = [ordered]@{
    'settings.json' = '{"schemaVersion":10,"note":"settings-content-sentinel-287"}'
    'settings.json.backup1' = 'layout switch settings backup 1'
    'settings.json.backup2' = 'layout switch settings backup 2'
    'settings-recovery-evidence\settings.corrupt.20261101T0800000000000Z.6e2d1b5a3c9f4a7b8d0e2f4a6c8e0a2b.json' = 'layout switch recovery evidence'
    'logs\application.jsonl' = '{"event":"window-ready"}'
    'logs\application.1.jsonl' = '{"event":"rotated"}'
    'holiday-cache\2026.json' = 'layout switch holiday cache 2026'
}
$switchUserDataEntries = @('settings.json', 'settings.json.backup1', 'settings.json.backup2', 'settings-recovery-evidence', 'logs', 'holiday-cache')
# 现有布局里安装根的其余文件：旧安装器装的 adapter、模块、启动器、图标、角色图与随包节假日数据，启动器与启动请求的状态文件，
# 一个退役残留，旧版本的程序目录，以及用户自己放进安装根的一个文件。旧版本的 EXE 与新版本逐字节相同（真实构建各版本的
# MinminCountdown.exe 一样，版本只在 DLL 与载荷清单里）：切换不靠 EXE 的哈希分辨版本。
$switchOldLayoutFiles = [ordered]@{
    '敏敏子倒计时.ps1' = 'layout switch adapter'
    'Countdown.InstallTransaction.psm1' = 'layout switch module install transaction'
    'Countdown.Notes.psm1' = 'layout switch module notes'
    'Countdown.Runtime.psm1' = 'layout switch module runtime'
    'Countdown.Settings.psm1' = 'layout switch module settings'
    'Countdown.Tags.psm1' = 'layout switch module tags'
    'Countdown.Core.psm1' = 'layout switch retired module'
    'CountdownLauncher.exe' = 'layout switch desktop launcher'
    'countdown-icon.ico' = 'layout switch icon'
    'sanrio-official-mymelody.png' = 'layout switch mymelody'
    'sanrio-official-cinnamoroll.png' = 'layout switch cinnamoroll'
    'sanrio-official-kuromi.png' = 'layout switch kuromi'
    'holiday-data\2026.json' = 'layout switch packaged holiday 2026'
    'holiday-data\2027.json' = 'layout switch packaged holiday 2027'
    'holiday-data\README.md' = 'layout switch holiday readme'
    'holiday-data\LICENSE.txt' = 'layout switch holiday license'
    'assets\floating-plush\component-day-mascots.png' = 'layout switch retired asset'
    'launcher-state.json' = '{"schemaVersion":1}'
    'startup-request.json' = '{"schemaVersion":1,"acceptanceState":"released"}'
    'acceptance-status.json' = '{"schemaVersion":1}'
    'runtime-status.json' = '{"schemaVersion":4,"stage":"window-ready","processId":4101,"sessionId":3}'
    'app\MinminCountdown.exe' = 'installer exe shared by every build'
    'app\MinminCountdown.dll' = 'layout switch old dll'
    'app\assets\sanrio-official-kuromi.png' = 'layout switch old app asset'
    'user-notes.txt' = 'a file the user put into the install root'
}
$switchFixtureTimeUtc = [datetime]::new(2026, 11, 1, 8, 0, 0, [System.DateTimeKind]::Utc)
$switchSessionSid = 'S-1-5-21-2870000000-2870000001-2870000002-1001'
$switchPwsh = 'C:\Program Files\PowerShell\7\pwsh.exe'
$switchShortcutName = '敏敏子倒计时.lnk'
# 当前进程令牌里启用的 SID：登录用户、Users、INTERACTIVE、Authenticated Users、Everyone，提权时另有 Administrators。
$switchUnelevatedSids = @($switchSessionSid, 'S-1-5-32-545', 'S-1-5-4', 'S-1-5-11', 'S-1-1-0')
$switchElevatedSids = @($switchUnelevatedSids) + @('S-1-5-32-544')
# 任务的 DACL 继承自任务计划根文件夹：Administrators 与 SYSTEM 得到 0x1F019F（含写入与删除），CREATOR OWNER 换成注册时的
# 属主、得到全部权限（本机根文件夹的 DACL 如此）。提权注册的任务属主是 Administrators，只有 SYSTEM 与 Administrators 能改；
# 未提权注册的任务属主是登录用户，用户自己也能改。
$switchAdminTaskSddl = 'D:AI(A;ID;0x1f019f;;;BA)(A;ID;0x1f019f;;;SY)(A;ID;FA;;;BA)'
$switchUserTaskSddl = "D:AI(A;ID;0x1f019f;;;BA)(A;ID;0x1f019f;;;SY)(A;ID;FA;;;$switchSessionSid)"
Import-Module (Join-Path $PSScriptRoot 'Countdown.Installer.psm1') -Force
$switchWindowsPorts = New-CountdownInstallerWindowsPorts
$switchCase = $null

# 测试 port 只读写当前 $switchCase：脚本与安装器回调时按动态作用域解析到这里的变量。
$switchPorts = [pscustomobject]@{
    GetInteractiveSessions = { $switchCase.Sessions }
    GetDesktopDirectory = { param([object] $Session) $switchCase.Desktop }
    GetProcesses = {
        param([string] $Name)
        @($switchCase.Processes | Where-Object { $_.Name -eq $Name })
    }
    # 由任务直接启动的进程一退出，任务实例随之结束。
    StopProcess = {
        param([int] $ProcessId)
        foreach ($process in @($switchCase.Processes | Where-Object { $_.ProcessId -eq $ProcessId -and $_.StartedByTask })) {
            if ($switchCase.Tasks.ContainsKey($process.StartedByTask)) { $switchCase.Tasks[$process.StartedByTask].State = 'Ready' }
        }
        $switchCase.Processes = @($switchCase.Processes | Where-Object { $_.ProcessId -ne $ProcessId })
    }
    GetTask = {
        param([string] $Name)
        if ($switchCase.Tasks.ContainsKey($Name)) { $switchCase.Tasks[$Name] }
    }
    # 注册即整份替换，触发器与主体给交互会话的登录用户；用例可经 OnRegisterTask 让注册失败。
    RegisterTask = {
        param([string] $Name, [object] $Definition, [object] $Session)
        if ($null -ne $switchCase.OnRegisterTask) { & $switchCase.OnRegisterTask $Name }
        $switchCase.Tasks[$Name] = [pscustomobject]@{
            State = 'Ready'
            Enabled = [bool] $Definition.Enabled
            Actions = @($Definition.Actions | ForEach-Object { [pscustomobject]@{ Type = $_.Type; Execute = $_.Execute; Arguments = $_.Arguments; WorkingDirectory = $_.WorkingDirectory } })
            Triggers = @($Definition.Triggers | ForEach-Object { [pscustomobject]@{ Type = $_.Type; Enabled = $_.Enabled; UserSid = $Session.Sid } })
            UserSid = $Session.Sid
            LogonType = $Definition.LogonType
            RunLevel = $Definition.RunLevel
            Priority = $Definition.Priority
            ExecutionTimeLimit = $Definition.ExecutionTimeLimit
            MultipleInstances = $Definition.MultipleInstances
            DisallowStartIfOnBatteries = $Definition.DisallowStartIfOnBatteries
            StopIfGoingOnBatteries = $Definition.StopIfGoingOnBatteries
            AllowDemandStart = $Definition.AllowDemandStart
            AllowHardTerminate = $Definition.AllowHardTerminate
            UserId = $Session.User
        }
    }
    # 现有布局的任务经 pwsh 运行 adapter：adapter 启动它所在安装根里的 app\MinminCountdown.exe 后退出，任务实例随之结束；
    # adapter 不在时什么都起不来。新布局的任务直接启动 EXE，实例跟着进程。EXE 起来后像应用一样在安装根写出运行状态回执。
    # OnStartTask 收到这次启动的 EXE 路径（起不来时为空）。
    StartTask = {
        param([string] $Name)
        $task = $switchCase.Tasks[$Name]
        if (-not $task.Enabled -or -not $task.AllowDemandStart) { throw "任务 $Name 不能按需启动。" }
        $action = $task.Actions[0]
        $execute = ([string] $action.Execute).Trim('"')
        $exePath = $null
        $startedByTask = $null
        if ([System.IO.Path]::GetFileName($execute) -eq 'pwsh.exe') {
            if ([string] $action.Arguments -match '-File "([^"]+)"' -and (Test-Path -LiteralPath $Matches[1] -PathType Leaf)) {
                $exePath = Join-Path (Split-Path -Parent $Matches[1]) 'app\MinminCountdown.exe'
            }
        }
        else {
            $exePath = $execute
            $startedByTask = $Name
        }
        if ($null -ne $exePath -and -not (Test-Path -LiteralPath $exePath -PathType Leaf)) { $exePath = $null }
        if ($null -ne $exePath) {
            $processId = $switchCase.NextProcessId
            $switchCase.NextProcessId = $processId + 1
            if ($null -ne $startedByTask) { $task.State = 'Running' }
            $switchCase.Processes = @($switchCase.Processes) + @(New-CountdownSwitchTestProcess -ProcessId $processId -ExecutablePath $exePath -StartedByTask $startedByTask)
            $receipt = [ordered]@{ schemaVersion = 4; stage = 'window-ready'; launchToken = [guid]::NewGuid().ToString('N'); processId = $processId; sessionId = 3; readyAtUtc = '2026-11-15T09:20:00.0000000+00:00' }
            [System.IO.File]::WriteAllText((Join-Path (Split-Path -Parent (Split-Path -Parent $exePath)) 'runtime-status.json'), ($receipt | ConvertTo-Json), $switchUtf8)
        }
        if ($null -ne $switchCase.OnStartTask) { & $switchCase.OnStartTask $exePath }
    }
    MoveDirectory = {
        param([string] $Source, [string] $Destination)
        [System.IO.Directory]::Move($Source, $Destination)
    }
    # 导出是不含运行状态的整份定义，恢复按导出整份替换；导出为空表示没有这个任务，恢复即删除。
    ExportTask = {
        param([string] $Name)
        if ($switchCase.Tasks.ContainsKey($Name)) { Get-CountdownSwitchTestTaskDefinition -Task $switchCase.Tasks[$Name] }
    }
    RestoreTask = {
        param([string] $Name, [object] $Definition)
        if ($null -eq $Definition) {
            [void] $switchCase.Tasks.Remove($Name)
            return
        }
        $restored = [string] $Definition | ConvertFrom-Json
        $restored | Add-Member -NotePropertyName State -NotePropertyValue 'Ready'
        $switchCase.Tasks[$Name] = $restored
    }
    ReadShortcut = $switchWindowsPorts.ReadShortcut
    WriteShortcut = $switchWindowsPorts.WriteShortcut
    RemoveShortcut = $switchWindowsPorts.RemoveShortcut
    # 当前进程的令牌（默认提权），用例可换成未提权的令牌。
    GetProcessIdentity = { $switchCase.Identity }
    # 任务的 DACL：用例登记过的按登记，其余（旧安装器提权注册的任务、提权运行的切换注册的任务）按提权注册的样子。
    GetTaskSecurity = {
        param([string] $Name)
        if ($switchCase.TaskSecurity.ContainsKey($Name)) { $switchCase.TaskSecurity[$Name] } else { $switchAdminTaskSddl }
    }
    # 候选 EXE 的无界面命令：设置已是当前格式，迁移不写文件，等价判定报告未变化。
    RunHeadlessCommand = {
        param([string] $ExecutablePath, [string[]] $Arguments, [string] $WorkingDirectory)
        $report = if ($Arguments[0] -eq '--settings-migration') {
            [ordered]@{ schemaVersion = 1; succeeded = $true; migrated = $false; migrationUtc = '2026-11-15T09:19:30.0000000+00:00'; reasonCategory = 'current-schema'; message = 'fake settings migration' }
        }
        else { [ordered]@{ schemaVersion = 1; equivalent = $true; reasonCategory = 'unchanged'; message = 'fake settings equivalence' } }
        [pscustomobject]@{ ExitCode = 0; Output = $report | ConvertTo-Json -Compress }
    }
}

function New-CountdownSwitchTestProcess {
    param([int] $ProcessId, [string] $ExecutablePath, [AllowNull()] [string] $StartedByTask)
    [pscustomobject]@{
        ProcessId = $ProcessId
        Name = 'MinminCountdown.exe'
        ExecutablePath = $ExecutablePath
        SessionId = 3
        CreationDate = [DateTimeOffset]::Parse('2026-11-15T09:19:58Z').LocalDateTime
        StartedByTask = $StartedByTask
    }
}

# 任务定义的逐项快照（不含随运行变化的状态）。
function Get-CountdownSwitchTestTaskDefinition {
    param([AllowNull()] [object] $Task)
    if ($null -eq $Task) { return '<missing>' }
    $Task | Select-Object -Property * -ExcludeProperty State | ConvertTo-Json -Depth 4 -Compress
}

function Get-CountdownSwitchTestTaskDefinitions {
    $definition = { param([string] $Name) Get-CountdownSwitchTestTaskDefinition -Task $(if ($switchCase.Tasks.ContainsKey($Name)) { $switchCase.Tasks[$Name] }) }
    "current=$(& $definition $switchCase.TaskName);legacy=$(& $definition $switchCase.LegacyTaskName)"
}

# 现有布局的任务：经 pwsh 运行 adapter，属于交互会话的登录用户。
function New-CountdownSwitchTestOldTask {
    param([string] $Arguments, [string] $WorkingDirectory, [int] $Priority)
    [pscustomobject]@{
        State = 'Ready'
        Enabled = $true
        Actions = @([pscustomobject]@{ Type = 'Exec'; Execute = $switchPwsh; Arguments = $Arguments; WorkingDirectory = $WorkingDirectory })
        Triggers = @([pscustomobject]@{ Type = 'Logon'; Enabled = $true; UserSid = $switchSessionSid })
        UserSid = $switchSessionSid
        LogonType = 'Interactive'
        RunLevel = 'Limited'
        Priority = $Priority
        ExecutionTimeLimit = 'PT0S'
        MultipleInstances = 'IgnoreNew'
        DisallowStartIfOnBatteries = $false
        StopIfGoingOnBatteries = $false
        AllowDemandStart = $true
        AllowHardTerminate = $true
        UserId = 'old-installer-task-owner'
    }
}

function Write-CountdownSwitchTestFile {
    param([Parameter(Mandatory)] [string] $Path, [Parameter(Mandatory)] [string] $Text, [System.Text.Encoding] $Encoding = $switchUtf8)
    New-Item -ItemType Directory -Path (Split-Path -Parent $Path) -Force | Out-Null
    [System.IO.File]::WriteAllText($Path, $Text, $Encoding)
    [System.IO.File]::SetCreationTimeUtc($Path, $switchFixtureTimeUtc)
    [System.IO.File]::SetLastWriteTimeUtc($Path, $switchFixtureTimeUtc)
}

function New-CountdownSwitchTestCase {
    param([Parameter(Mandatory)] [string] $Name)

    $caseRoot = Join-Path $switchTestRoot $Name
    $packageRoot = Join-Path $caseRoot 'package'
    foreach ($file in $switchPackageFiles) { Write-CountdownSwitchTestFile -Path (Join-Path $packageRoot $file.Path) -Text $file.Text }
    $manifest = [ordered]@{
        schemaVersion = 1
        commit = $switchCommit
        aggregateSha256 = $switchAggregate
        files = @($switchPackageFiles | ForEach-Object { [ordered]@{ path = $_.Path; sha256 = $_.Sha256 } })
    }
    Write-CountdownSwitchTestFile -Path (Join-Path $packageRoot 'app\published-payload.json') -Text ($manifest | ConvertTo-Json -Depth 4)
    $root = Join-Path $caseRoot 'install'
    [pscustomobject]@{
        Name = $Name
        PackageRoot = $packageRoot
        Root = $root
        Backup = "$root.layout-switch-backup"
        Desktop = Join-Path $caseRoot 'desktop'
        ExePath = Join-Path $root 'app\MinminCountdown.exe'
        TaskName = "Minmin Countdown Layout Switch $Name"
        LegacyTaskName = "敏敏子倒计时 Layout Switch $Name"
        Sessions = @([pscustomobject]@{ SessionId = 3; User = 'MINMIN-OFFICE\layout-switch-account-sentinel'; Sid = $switchSessionSid })
        Processes = @()
        Tasks = @{}
        NextProcessId = 7001
        Identity = [pscustomobject]@{ Elevated = $true; Sids = $switchElevatedSids }
        TaskSecurity = @{}
        OnRegisterTask = $null
        OnStartTask = $null
        Json = $null
    }
}

# 按办公室现有布局搭出安装根、两个任务、桌面与在运行的旧版本：旧版本由 adapter 启动（不算任务实例），
# 开发目录里另有一个同名 EXE 在跑。所有文件的时间戳固定在切换之前。
function Initialize-CountdownSwitchTestOldLayout {
    $root = $switchCase.Root
    foreach ($relative in $switchOldLayoutFiles.Keys) { Write-CountdownSwitchTestFile -Path (Join-Path $root $relative) -Text $switchOldLayoutFiles[$relative] }
    foreach ($relative in $switchUserDataFiles.Keys) { Write-CountdownSwitchTestFile -Path (Join-Path $root $relative) -Text $switchUserDataFiles[$relative] }
    New-Item -ItemType Directory -Path $switchCase.Desktop -Force | Out-Null
    $launcher = Join-Path $switchCase.Desktop '敏敏子倒计时.exe'
    [System.IO.File]::Copy((Join-Path $root 'CountdownLauncher.exe'), $launcher)
    [System.IO.File]::SetCreationTimeUtc($launcher, $switchFixtureTimeUtc)
    $legacyLauncher = "@echo off`r`nC:\Windows\System32\schtasks.exe /Run /TN `"$($switchCase.TaskName)`" >nul 2>&1`r`nexit /b %errorlevel%`r`n"
    Write-CountdownSwitchTestFile -Path (Join-Path $switchCase.Desktop '敏敏子倒计时.cmd') -Text $legacyLauncher -Encoding ([System.Text.Encoding]::ASCII)
    Write-CountdownSwitchTestFile -Path (Join-Path $switchCase.Desktop 'desktop-note.txt') -Text 'a file of the user on the desktop'
    $adapter = Join-Path $root '敏敏子倒计时.ps1'
    $switchCase.Tasks[$switchCase.TaskName] = New-CountdownSwitchTestOldTask -Arguments "-NoLogo -NoProfile -ExecutionPolicy RemoteSigned -WindowStyle Hidden -STA -File `"$adapter`"" -WorkingDirectory $root -Priority 7
    $switchCase.Tasks[$switchCase.LegacyTaskName] = New-CountdownSwitchTestOldTask -Arguments "-NoProfile -File `"$adapter`"" -WorkingDirectory $root -Priority 6
    $switchCase.Processes = @(
        New-CountdownSwitchTestProcess -ProcessId 4101 -ExecutablePath $switchCase.ExePath
        New-CountdownSwitchTestProcess -ProcessId 4103 -ExecutablePath 'D:\dev-checkout\app\MinminCountdown.exe'
    )
}

# 目录树清单（序数排序，目录以 \ 结尾）：文件带长度、修改时间与 SHA-256（-WithCreationTime 时加创建时间），-NamesOnly 时只列路径。
# -Only 与 -Exclude 按顶层条目名筛选。
function Get-CountdownSwitchTestTree {
    param(
        [Parameter(Mandatory)] [string] $Root,
        [string[]] $Only = @(),
        [string[]] $Exclude = @(),
        [switch] $WithCreationTime,
        [switch] $NamesOnly
    )

    if (-not (Test-Path -LiteralPath $Root)) { return '<missing>' }
    $prefix = $Root.TrimEnd('\') + '\'
    [string[]] $entries = @(Get-ChildItem -LiteralPath $Root -Recurse -Force | ForEach-Object {
            $relative = $_.FullName.Substring($prefix.Length)
            $top = $relative.Split('\')[0]
            if ($top -in $Exclude -or ($Only.Count -gt 0 -and $top -notin $Only)) { return }
            if ($_.PSIsContainer) { "$relative\" }
            elseif ($NamesOnly) { $relative }
            else {
                $created = if ($WithCreationTime) { "|$($_.CreationTimeUtc.ToString('o'))" } else { '' }
                "$relative|$($_.Length)$created|$($_.LastWriteTimeUtc.ToString('o'))|$((Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash)"
            }
        })
    [Array]::Sort($entries, [System.StringComparer]::Ordinal)
    $entries -join ','
}

function Get-CountdownSwitchTestRunning {
    (@($switchCase.Processes | ForEach-Object { [int] $_.ProcessId } | Sort-Object)) -join ','
}

function Read-CountdownSwitchTestShortcut {
    param([Parameter(Mandatory)] [string] $Path)
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { return '<missing>' }
    $link = (New-Object -ComObject WScript.Shell).CreateShortcut($Path)
    "$($link.TargetPath)|$($link.Arguments)|$($link.WorkingDirectory)|$($link.IconLocation)"
}

function Invoke-CountdownSwitchTest {
    param([switch] $Rollback, [string] $Script = $switchScript)

    $arguments = @{ InstallRoot = $switchCase.Root; TaskName = $switchCase.TaskName; LegacyTaskName = $switchCase.LegacyTaskName; Port = $switchPorts; LockName = $switchLockName }
    if ($Rollback) { $arguments.Rollback = $true } else { $arguments.PackageRoot = $switchCase.PackageRoot }
    $output = @(& $Script @arguments)
    Assert-Equal 1 $output.Count "Layout switch writes exactly one JSON document: $($switchCase.Name)"
    $switchCase.Json = [string] $output[-1]
    $switchCase.Json | ConvertFrom-Json -DateKind String
}

function Invoke-CountdownSwitchStatusRead {
    $output = @(& $switchStatusScript -InstallRoot $switchCase.Root -TaskName $switchCase.TaskName -Port $switchPorts)
    [string] $output[-1] | ConvertFrom-Json -DateKind String
}

try {
    # 曳光弹：现有布局切到新布局。切换前先停下旧版本并做完整备份，再用新安装器装入程序目录、更新登录任务、登记桌面快捷方式并
    # 启动到启动就绪，最后移除遗留任务、桌面上的旧启动器与安装根里的旧文件。
    $switchCase = New-CountdownSwitchTestCase -Name 'switch'
    Initialize-CountdownSwitchTestOldLayout
    $userDataBefore = Get-CountdownSwitchTestTree -Root $switchCase.Root -Only $switchUserDataEntries -WithCreationTime
    $rootBefore = Get-CountdownSwitchTestTree -Root $switchCase.Root
    $tasksBefore = Get-CountdownSwitchTestTaskDefinitions
    $desktopBefore = Get-CountdownSwitchTestTree -Root $switchCase.Desktop
    $currentTaskBefore = Get-CountdownSwitchTestTaskDefinition -Task $switchCase.Tasks[$switchCase.TaskName]
    $legacyTaskBefore = Get-CountdownSwitchTestTaskDefinition -Task $switchCase.Tasks[$switchCase.LegacyTaskName]
    $switch = Invoke-CountdownSwitchTest
    Assert-Equal 'True||created|True' "$($switch.succeeded)|$($switch.failedStage)|$($switch.backup.action)|$($switch.backup.complete)" 'The switch on the current office layout succeeds after creating a complete backup'
    Assert-Equal '1|7001' "$($switch.stoppedProcessCount)|$($switch.readyProcess.processId)" 'The switch stops the running old version and the new version reaches startup readiness'
    Assert-Equal 'True' "$($switch.elevated)" 'The switch reports that it ran elevated'

    # 状态命令读回新布局：清单记的提交、EXE 与清单一致、应用在运行、登录任务与期望定义一致、没有事务标记。
    $switchStatus = Invoke-CountdownSwitchStatusRead
    Assert-Equal "present|$switchCommit|True|True|7001|True||False" "$($switchStatus.manifest)|$($switchStatus.commit)|$($switchStatus.exe.matchesManifest)|$($switchStatus.running)|$(@($switchStatus.processes | ForEach-Object { $_.processId }) -join ',')|$($switchStatus.loginTask.matchesExpected)|$(@($switchStatus.loginTask.mismatches) -join ',')|$($switchStatus.transactionMarkerPresent)" 'After the switch the status command reads the new layout'

    Assert-Equal $userDataBefore (Get-CountdownSwitchTestTree -Root $switchCase.Root -Only $switchUserDataEntries -WithCreationTime) 'Settings, setting backups, recovery evidence, diagnostic logs and the holiday cache stay byte-identical across the switch, timestamps included'
    Assert-Equal 'app\,app\MinminCountdown.dll,app\MinminCountdown.exe,app\holiday-data\,app\holiday-data\2027.json,app\published-payload.json,runtime-status.json,user-notes.txt' (Get-CountdownSwitchTestTree -Root $switchCase.Root -Exclude $switchUserDataEntries -NamesOnly) 'After the switch the install root holds only the program directory, the runtime receipt, the user data and the file it does not know'
    Assert-Equal 'a file the user put into the install root' ([System.IO.File]::ReadAllText((Join-Path $switchCase.Root 'user-notes.txt'))) 'The switch keeps a file in the install root it does not know'
    Assert-Equal 'removed|敏敏子倒计时.exe=removed,敏敏子倒计时.exe.new=absent,敏敏子倒计时.cmd=removed|user-notes.txt' "$($switch.legacy.task)|$((@($switch.legacy.desktop) | ForEach-Object { "$($_.name)=$($_.action)" }) -join ',')|$(@($switch.legacy.unknownRootEntries) -join ',')" 'The switch reports the removed legacy task and desktop launchers and the root entry it kept'

    Assert-Equal "desktop-note.txt,$switchShortcutName" (Get-CountdownSwitchTestTree -Root $switchCase.Desktop -NamesOnly) 'After the switch the desktop holds the shortcut instead of the launcher EXE and the legacy .cmd; other desktop files stay'
    Assert-Equal "$($switchCase.ExePath)||$(Join-Path $switchCase.Root 'app')|$($switchCase.ExePath),0" (Read-CountdownSwitchTestShortcut -Path (Join-Path $switchCase.Desktop $switchShortcutName)) 'The desktop shortcut starts the installed EXE from the program directory'
    Assert-Equal "False|$($switchCase.ExePath)|" "$($switchCase.Tasks.ContainsKey($switchCase.LegacyTaskName))|$($switchCase.Tasks[$switchCase.TaskName].Actions[0].Execute)|$($switchCase.Tasks[$switchCase.TaskName].Actions[0].Arguments)" 'After the switch the legacy task is gone and the login task starts the installed EXE directly'
    Assert-Equal '4103,7001' (Get-CountdownSwitchTestRunning) 'After the switch only the new version and the unrelated process with the same name run'

    # 备份：安装根的完整副本（路径、长度、修改时间与内容都与切换前相同）、两个任务导出的定义与桌面上的启动器文件。
    Assert-Equal $rootBefore (Get-CountdownSwitchTestTree -Root (Join-Path $switchCase.Backup 'root')) 'The backup holds a full copy of the install root as it was before the switch'
    Assert-Equal "$currentTaskBefore|$legacyTaskBefore" "$([System.IO.File]::ReadAllText((Join-Path $switchCase.Backup 'tasks\current.task')))|$([System.IO.File]::ReadAllText((Join-Path $switchCase.Backup 'tasks\legacy.task')))" 'The backup holds the exported definitions of the current and the legacy task'
    Assert-Equal ($desktopBefore -replace 'desktop-note\.txt\|[^,]*,', '') (Get-CountdownSwitchTestTree -Root (Join-Path $switchCase.Backup 'desktop')) 'The backup holds copies of the desktop launcher files'
    Assert-Equal $true ($tasksBefore -ne (Get-CountdownSwitchTestTaskDefinitions)) 'The switch changed the task definitions it backed up'

    # 备份不完整时切换不开始：备份途中读不了安装根里的一个文件（被别的程序独占打开）。切换删掉写了一半的备份，经登录任务
    # 重新启动旧版本，安装根、两个任务与桌面都没动（运行状态回执由重新启动的旧版本重写）。
    $switchCase = New-CountdownSwitchTestCase -Name 'backup-fails'
    Initialize-CountdownSwitchTestOldLayout
    $rootBefore = Get-CountdownSwitchTestTree -Root $switchCase.Root -Exclude 'runtime-status.json'
    $tasksBefore = Get-CountdownSwitchTestTaskDefinitions
    $desktopBefore = Get-CountdownSwitchTestTree -Root $switchCase.Desktop
    $exclusiveHandle = [System.IO.File]::Open((Join-Path $switchCase.Root 'user-notes.txt'), 'Open', 'Read', 'None')
    try { $backupFails = Invoke-CountdownSwitchTest }
    finally { $exclusiveHandle.Dispose() }
    Assert-Equal 'False|backup|False|False' "$($backupFails.succeeded)|$($backupFails.failedStage)|$($backupFails.backup.complete)|$(Test-Path -LiteralPath $switchCase.Backup)" 'A backup that cannot read a file of the install root stops the switch and removes the half-written backup'
    Assert-Equal $rootBefore (Get-CountdownSwitchTestTree -Root $switchCase.Root -Exclude 'runtime-status.json') 'A failed backup leaves the install root as it was'
    Assert-Equal $tasksBefore (Get-CountdownSwitchTestTaskDefinitions) 'A failed backup leaves both tasks as they were'
    Assert-Equal $desktopBefore (Get-CountdownSwitchTestTree -Root $switchCase.Desktop) 'A failed backup leaves the desktop as it was'
    Assert-Equal '1|7001|4103,7001' "$($backupFails.stoppedProcessCount)|$($backupFails.restartedProcess.processId)|$(Get-CountdownSwitchTestRunning)" 'After a failed backup the old version runs again, started through its login task'

    # 已有一份不完整的备份（上一次在备份途中被打断，没写下 backup.json）：切换在前置检查停下，不停应用，什么都不动。
    $switchCase = New-CountdownSwitchTestCase -Name 'incomplete-backup'
    Initialize-CountdownSwitchTestOldLayout
    Write-CountdownSwitchTestFile -Path (Join-Path $switchCase.Backup 'root\settings.json') -Text 'half-copied settings'
    $backupBefore = Get-CountdownSwitchTestTree -Root $switchCase.Backup
    $rootBefore = Get-CountdownSwitchTestTree -Root $switchCase.Root
    $tasksBefore = Get-CountdownSwitchTestTaskDefinitions
    $incomplete = Invoke-CountdownSwitchTest
    Assert-Equal 'False|precheck|backup-incomplete|0' "$($incomplete.succeeded)|$($incomplete.failedStage)|$($incomplete.reason)|$($incomplete.stoppedProcessCount)" 'An existing incomplete backup stops the switch before anything changes'
    Assert-Equal "$backupBefore|$rootBefore|$tasksBefore|4101,4103" "$(Get-CountdownSwitchTestTree -Root $switchCase.Backup)|$(Get-CountdownSwitchTestTree -Root $switchCase.Root)|$(Get-CountdownSwitchTestTaskDefinitions)|$(Get-CountdownSwitchTestRunning)" 'The refused switch leaves the incomplete backup, the install root, the tasks and the running old version alone'

    # 未提权运行（#288 只实测了未提权的进程改自己未提权注册的任务）：现役登录任务是旧安装器提权注册的，只有 SYSTEM 与
    # Administrators 能改；遗留任务是登录用户自己注册的。切换在前置检查按两个任务的 DACL 判出改不了现役任务，指名停下，
    # 不停应用、不备份，什么都不动。
    $switchCase = New-CountdownSwitchTestCase -Name 'task-access'
    Initialize-CountdownSwitchTestOldLayout
    $switchCase.Identity = [pscustomobject]@{ Elevated = $false; Sids = $switchUnelevatedSids }
    $switchCase.TaskSecurity[$switchCase.LegacyTaskName] = $switchUserTaskSddl
    $rootBefore = Get-CountdownSwitchTestTree -Root $switchCase.Root
    $tasksBefore = Get-CountdownSwitchTestTaskDefinitions
    $desktopBefore = Get-CountdownSwitchTestTree -Root $switchCase.Desktop
    $accessDenied = Invoke-CountdownSwitchTest
    Assert-Equal 'False|precheck|task-access-denied|False|0' "$($accessDenied.succeeded)|$($accessDenied.failedStage)|$($accessDenied.reason)|$($accessDenied.elevated)|$($accessDenied.stoppedProcessCount)" "Without the rights to modify the login task the switch stops in the precheck: $($accessDenied.error)"
    Assert-Equal 'True|False' "$(([string] $accessDenied.error).Contains($switchCase.TaskName))|$(([string] $accessDenied.error).Contains($switchCase.LegacyTaskName))" "The refusal names the task the process may not modify and not the one it may: $($accessDenied.error)"
    Assert-Equal "<missing>|$rootBefore|$tasksBefore|$desktopBefore|4101,4103" "$(Get-CountdownSwitchTestTree -Root $switchCase.Backup)|$(Get-CountdownSwitchTestTree -Root $switchCase.Root)|$(Get-CountdownSwitchTestTaskDefinitions)|$(Get-CountdownSwitchTestTree -Root $switchCase.Desktop)|$(Get-CountdownSwitchTestRunning)" 'The refused switch writes no backup and leaves the install root, the tasks, the desktop and the running old version alone'

    # 切换中途失败：前置检查按 DACL 判定权限够，新安装器更新登录任务时却被任务计划拒绝。新安装器自己回滚到切换前的程序目录与
    # 登录任务并重新启动旧版本；切换停下报告，备份完整保留，遗留没动。
    $switchCase = New-CountdownSwitchTestCase -Name 'install-fails'
    Initialize-CountdownSwitchTestOldLayout
    $rootBefore = Get-CountdownSwitchTestTree -Root $switchCase.Root
    $tasksBefore = Get-CountdownSwitchTestTaskDefinitions
    $desktopBefore = Get-CountdownSwitchTestTree -Root $switchCase.Desktop
    $switchCase.OnRegisterTask = { param([string] $Name) throw '拒绝访问：不能替换这个计划任务。' }
    $installFails = Invoke-CountdownSwitchTest
    Assert-Equal 'False|install|register-login-task|rolled-back' "$($installFails.succeeded)|$($installFails.failedStage)|$($installFails.installer.failedStage)|$($installFails.rollback.outcome)" 'A failing install stops the switch and the new installer rolls back'
    Assert-Equal "$($switchCase.Backup)|True|$rootBefore" "$($installFails.backup.directory)|$(Test-Path -LiteralPath (Join-Path $switchCase.Backup 'backup.json'))|$(Get-CountdownSwitchTestTree -Root (Join-Path $switchCase.Backup 'root'))" 'The stopped switch reports and keeps its complete backup'
    Assert-Equal (($rootBefore -split ',' | Where-Object { $_ -notlike 'runtime-status.json|*' }) -join ',') (Get-CountdownSwitchTestTree -Root $switchCase.Root -Exclude 'runtime-status.json') 'After the failed install the install root is back as before the switch'
    Assert-Equal "$tasksBefore|$desktopBefore" "$(Get-CountdownSwitchTestTaskDefinitions)|$(Get-CountdownSwitchTestTree -Root $switchCase.Desktop)" 'After the failed install both tasks and the desktop are as before the switch'
    Assert-Equal '7001|4103,7001' "$($installFails.restartedProcess.processId)|$(Get-CountdownSwitchTestRunning)" 'After the failed install the old version runs again'

    # 排除原因后再次运行切换：沿用这份完整备份（不再停应用、不重新备份），切换走完；备份仍是第一次切换之前的样子。
    $switchCase.OnRegisterTask = $null
    $resumed = Invoke-CountdownSwitchTest
    Assert-Equal 'True|reused|True|0' "$($resumed.succeeded)|$($resumed.backup.action)|$($resumed.backup.complete)|$($resumed.stoppedProcessCount)" 'Running the switch again after the cause is fixed reuses the kept backup and completes the switch'
    Assert-Equal $rootBefore (Get-CountdownSwitchTestTree -Root (Join-Path $switchCase.Backup 'root')) 'The reused backup still holds the install root as it was before the first attempt'
    $resumedStatus = Invoke-CountdownSwitchStatusRead
    Assert-Equal "present|$switchCommit|True|True|True|False" "$($resumedStatus.manifest)|$($resumedStatus.commit)|$($resumedStatus.exe.matchesManifest)|$($resumedStatus.running)|$($resumedStatus.loginTask.matchesExpected)|$($resumedStatus.transactionMarkerPresent)" 'After the resumed switch the status command reads the new layout'
    Assert-Equal "desktop-note.txt,$switchShortcutName|False" "$(Get-CountdownSwitchTestTree -Root $switchCase.Desktop -NamesOnly)|$($switchCase.Tasks.ContainsKey($switchCase.LegacyTaskName))" 'The resumed switch replaces the desktop launchers with the shortcut and removes the legacy task'

    # 回退：切换之后用户在新版本里改过设置。从备份里的 tools\ 运行 -Rollback：停下新版本，安装根、两个任务的定义与桌面启动器
    # 回到切换前（安装根按路径、长度、修改时间与内容比较；运行状态回执由重新启动的旧版本重写），被覆盖的新设置先留在备份目录里，
    # 旧版本经恢复的登录任务重新启动。
    $switchCase = New-CountdownSwitchTestCase -Name 'rollback'
    Initialize-CountdownSwitchTestOldLayout
    $rootBefore = Get-CountdownSwitchTestTree -Root $switchCase.Root -Exclude 'runtime-status.json'
    $tasksBefore = Get-CountdownSwitchTestTaskDefinitions
    $desktopBefore = Get-CountdownSwitchTestTree -Root $switchCase.Desktop
    Assert-Equal $true ([bool] (Invoke-CountdownSwitchTest).succeeded) 'The switch before the rollback succeeds'
    $editedSettings = '{"schemaVersion":10,"note":"edited in the new version after the switch"}'
    [System.IO.File]::WriteAllText((Join-Path $switchCase.Root 'settings.json'), $editedSettings, $switchUtf8)

    # 回退同样先看权限：未提权时改不了提权运行的切换注册的登录任务，回退在前置检查停下，什么都不动，备份不记为已回退。
    $switchCase.Identity = [pscustomobject]@{ Elevated = $false; Sids = $switchUnelevatedSids }
    $rootSwitched = Get-CountdownSwitchTestTree -Root $switchCase.Root
    $tasksSwitched = Get-CountdownSwitchTestTaskDefinitions
    $backupSwitched = Get-CountdownSwitchTestTree -Root $switchCase.Backup
    $rollbackDenied = Invoke-CountdownSwitchTest -Rollback -Script (Join-Path $switchCase.Backup 'tools\Switch-CountdownInstallLayout.ps1')
    Assert-Equal 'False|precheck|task-access-denied|False' "$($rollbackDenied.succeeded)|$($rollbackDenied.failedStage)|$($rollbackDenied.reason)|$($rollbackDenied.elevated)" "Without the rights to modify the login task the rollback stops in the precheck: $($rollbackDenied.error)"
    Assert-Equal "$rootSwitched|$tasksSwitched|$backupSwitched|4103,7001" "$(Get-CountdownSwitchTestTree -Root $switchCase.Root)|$(Get-CountdownSwitchTestTaskDefinitions)|$(Get-CountdownSwitchTestTree -Root $switchCase.Backup)|$(Get-CountdownSwitchTestRunning)" 'The refused rollback leaves the install root, the tasks, the backup and the running new version alone'
    $switchCase.Identity = [pscustomobject]@{ Elevated = $true; Sids = $switchElevatedSids }

    $rollback = Invoke-CountdownSwitchTest -Rollback -Script (Join-Path $switchCase.Backup 'tools\Switch-CountdownInstallLayout.ps1')
    Assert-Equal 'True||1|restored|restored' "$($rollback.succeeded)|$($rollback.failedStage)|$($rollback.stoppedProcessCount)|$($rollback.tasks.current)|$($rollback.tasks.legacy)" 'The rollback from the backup succeeds after stopping the new version'
    Assert-Equal $rootBefore (Get-CountdownSwitchTestTree -Root $switchCase.Root -Exclude 'runtime-status.json') 'After the rollback the install root equals the state before the switch'
    Assert-Equal $tasksBefore (Get-CountdownSwitchTestTaskDefinitions) 'After the rollback both task definitions equal the state before the switch'
    Assert-Equal $desktopBefore (Get-CountdownSwitchTestTree -Root $switchCase.Desktop) 'After the rollback the desktop launcher files equal the state before the switch and the shortcut is gone'
    Assert-Equal $editedSettings ([System.IO.File]::ReadAllText((Join-Path $rollback.preservedDirectory 'root\settings.json'))) 'The rollback keeps the settings edited after the switch in the backup directory before restoring the old ones'
    $receipt = [System.IO.File]::ReadAllText((Join-Path $switchCase.Root 'runtime-status.json')) | ConvertFrom-Json
    Assert-Equal '7002|7002|4103,7002' "$($rollback.restartedProcess.processId)|$($receipt.processId)|$(Get-CountdownSwitchTestRunning)" 'After the rollback the old version runs again, started through the restored login task'

    # 用过回退的备份不再沿用：再次运行切换在前置检查停下，什么都不动。
    $rootAfterRollback = Get-CountdownSwitchTestTree -Root $switchCase.Root
    $afterRollback = Invoke-CountdownSwitchTest
    Assert-Equal 'False|precheck|backup-used-for-rollback' "$($afterRollback.succeeded)|$($afterRollback.failedStage)|$($afterRollback.reason)" 'A backup used for a rollback is not reused by another switch'
    Assert-Equal "$rootAfterRollback|$tasksBefore|4103,7002" "$(Get-CountdownSwitchTestTree -Root $switchCase.Root)|$(Get-CountdownSwitchTestTaskDefinitions)|$(Get-CountdownSwitchTestRunning)" 'The refused switch after a rollback changes nothing'
}
finally {
    Remove-CountdownIsolatedTestTree -Path $switchTestRoot -AllowedRoot $switchTempRoot
}
