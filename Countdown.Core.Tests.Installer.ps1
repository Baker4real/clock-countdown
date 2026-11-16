# Countdown.Core.Tests.ps1 的 Installer 分段：入口按开关点源执行，共用入口的参数、断言函数与已建立的状态，不单独运行。

# #280 新安装器：在临时安装根上运行 Install-CountdownProgram.ps1，计划任务、进程、交互会话与目录改名经测试 port 注入。
# 测试 port 模拟 Windows：任务启动时按动作里的 EXE 起一个假进程，并像应用一样在安装根写出运行状态回执。断言安装根的
# 文件状态、port 里的任务与进程、结果 JSON，以及状态命令（#276）对安装结果的读回。
$installerScript = Join-Path $PSScriptRoot 'Install-CountdownProgram.ps1'
$installerStatusScript = Join-Path $PSScriptRoot 'Get-CountdownInstallStatus.ps1'
$installerTempRoot = [System.IO.Path]::GetTempPath()
$installerTestRoot = Join-Path $installerTempRoot ('clock-countdown-installer-test-' + [guid]::NewGuid().ToString('N'))
$installerUtf8 = [System.Text.UTF8Encoding]::new($false)
# 每次运行用自己的锁名，不和本机真实安装或别处并行的测试争同一把全机锁。
$installerLockName = 'Local\MinminCountdownInstallerTest-' + [guid]::NewGuid().ToString('N')
# 期望值是独立算好的字面量：各文件为 SHA-256(正文)，聚合哈希按构建脚本的算法（清单顺序的 路径\0哈希\n）另行算出。
# 两个版本共用同一个 EXE：真实构建的 MinminCountdown.exe 是 apphost，各版本逐字节相同，版本在 DLL 与载荷清单里（#288）。
# 用例因此按 DLL 或清单认版本，从不按 EXE 的哈希。
$installerSharedExe = [pscustomobject]@{ Path = 'app\MinminCountdown.exe'; Text = 'installer exe shared by every build'; Sha256 = 'C62B3E048197B6CDE441B0C583B62423D0B16175267038D991B6D1278CE28388' }
$installerCommitV2 = '2b80c4d6e8f0a1b3c5d7e9f1a2b4c6d8e0f2a4b6'
$installerAggregateV2 = '2A451524B97FED26B6B7B535A88AAA1129446AE62535044D66DF007CF09D6105'
$installerPackageFilesV2 = @(
    [pscustomobject]@{ Path = 'app\holiday-data\2027.json'; Text = 'installer holiday v2'; Sha256 = '5987A3F052924557E59C0E387723DDEED9E53B74997FB8FF418552117EDC90DB' }
    [pscustomobject]@{ Path = 'app\MinminCountdown.dll'; Text = 'installer dll v2'; Sha256 = 'DB2617243BF6059E225265C27CFFFE1E01CF5B9B78BDBB67B6789CB8BA8B548D' }
    $installerSharedExe
)
$installerCommitV1 = '1a70b3c5d7e9f0a2b4c6d8e0f1a3b5c7d9e1f3a5'
$installerAggregateV1 = '3F26C6760511CE0F37CBE56098B0E80661F31DC51CA331C72D8D109A38FEE8F7'
$installerProgramFilesV1 = @(
    [pscustomobject]@{ Path = 'app\MinminCountdown.dll'; Text = 'installer dll v1'; Sha256 = '41F7D2E165D4F4569FB1B0B6F95BAA18EBABE0F85F5B9A62AD448D7B2D0FE903' }
    $installerSharedExe
)
# 安装根里的用户数据：设置、两代设置备份、恢复证据、诊断日志与节假日缓存，时间戳固定在安装之前。
$installerUserDataFiles = [ordered]@{
    'settings.json' = '{"schemaVersion":10,"note":"settings-content-sentinel-280"}'
    'settings.json.backup1' = 'installer settings backup 1'
    'settings.json.backup2' = 'installer settings backup 2'
    'settings-recovery-evidence\settings.corrupt.20261101T0800000000000Z.5d1c0a4f2b8e4f6a9c3d7e1b0a2c4e6f.json' = 'installer recovery evidence'
    'logs\application.jsonl' = '{"event":"window-ready"}'
    'holiday-cache\2026.json' = 'installer holiday cache 2026'
}
$installerUserDataTimeUtc = [datetime]::new(2026, 11, 1, 8, 0, 0, [System.DateTimeKind]::Utc)
# 设置指纹的期望值：上面设置正文的长度与 SHA-256，修改时间即固定的安装前时间。
$installerSettingsFingerprint = 'True|59|2026-11-01T08:00:00.0000000Z|D1990AECF05D08877897A23666B929C3AAB7ADC93E5986E052E06529BEC82151'
# 安装器自己的条目；其余都是用户数据。
$installerOwnEntries = @('app', 'app.candidate', 'app.previous', 'install-transaction', 'install-transaction.json', 'runtime-status.json')
# 假迁移命令报告的迁移时刻（#282）：等价判定只有收到这个时刻才认可迁移结果。
$installerMigrationUtc = '2026-11-14T09:19:30.0000000+00:00'
# 交互会话登录用户与另一个用户的 SID（假值）：登录任务的主体与登录触发器按 SID 判断是不是交互用户（#281）。
$installerSessionSid = 'S-1-5-21-2810000000-2810000001-2810000002-1001'
$installerOtherSid = 'S-1-5-21-2810000000-2810000001-2810000002-2002'
# 桌面快捷方式的读写用生产 port（WScript.Shell），只落在每个用例自己的临时桌面目录里；桌面目录本身经假 port 给出（#281）。
Import-Module (Join-Path $PSScriptRoot 'Countdown.Installer.psm1') -Force
$installerWindowsPorts = New-CountdownInstallerWindowsPorts
$installerShortcutName = '敏敏子倒计时.lnk'
$installerCase = $null

# 测试 port 只读写当前 $installerCase：安装器回调时按动态作用域解析到这里的变量。
$installerPorts = [pscustomobject]@{
    GetInteractiveSessions = { $installerCase.Sessions }
    GetProcesses = {
        param([string] $Name)
        @($installerCase.Processes | Where-Object { $_.Name -eq $Name })
    }
    # 结束进程；由任务启动的进程一退出，任务实例随之结束（#279 实测 Stop-Process 后任务回到 Ready），上次结果是被结束的
    # 进程的退出码 0xFFFFFFFF（#288 实测）。
    StopProcess = {
        param([int] $ProcessId)
        foreach ($process in @($installerCase.Processes | Where-Object { $_.ProcessId -eq $ProcessId -and $_.StartedByTask })) {
            Set-CountdownInstallerTestTaskRun -Task $installerCase.Tasks[$process.StartedByTask] -State 'Ready' -LastTaskResult 0xFFFFFFFFL
        }
        $installerCase.Processes = @($installerCase.Processes | Where-Object { $_.ProcessId -ne $ProcessId })
        if ($null -ne $installerCase.OnStopProcess) { & $installerCase.OnStopProcess $ProcessId }
    }
    GetTask = {
        param([string] $Name)
        if ($installerCase.Tasks.ContainsKey($Name)) { $installerCase.Tasks[$Name] }
    }
    # 注册即整份替换（同名任务已存在时也一样），触发器与主体都给交互会话的登录用户；UserId 记注册时的显示名，
    # 只用来看出任务有没有被重新注册。
    RegisterTask = {
        param([string] $Name, [object] $Definition, [object] $Session)
        $installerCase.Tasks[$Name] = [pscustomobject]@{
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
    StartTask = {
        param([string] $Name)
        # 任务按动作启动 EXE；EXE 存在时应用渲染首帧，在应用根（app\ 的上一级）写出运行状态回执，形同 C# 的 RuntimeReceipt。
        # 用例可改写这次启动：进程落在哪个会话、写完回执后是否还在运行、是否写出回执、不在运行时的退出码。Launch 只管新版本
        # （DLL 与包里的相同），PreviousLaunch 管其余的启动，也就是回滚后重新启动的旧版本。OnStartTask 收到这次启动的 EXE 路径。
        # 与任务计划一样，停用的任务和不允许按需启动的任务不能按需启动。
        $task = $installerCase.Tasks[$Name]
        if (-not $task.Enabled -or -not $task.AllowDemandStart) { throw "任务 $Name 不能按需启动。" }
        Set-CountdownInstallerTestTaskRun -Task $task -State 'Running' -LastTaskResult 0x41301 -NewRun
        $exePath = ([string] $task.Actions[0].Execute).Trim('"')
        if ($null -ne $installerCase.OnStartTask) { & $installerCase.OnStartTask $exePath }
        if (-not (Test-Path -LiteralPath $exePath -PathType Leaf)) { return }
        $launch = if (Test-CountdownInstallerTestNewVersion -ExecutablePath $exePath) { $installerCase.Launch } else { $installerCase.PreviousLaunch }
        $processId = $installerCase.NextProcessId
        $installerCase.NextProcessId = $processId + 1
        $process = New-CountdownInstallerTestProcess -ProcessId $processId -ExecutablePath $exePath -StartedByTask $Name
        $process.SessionId = $launch.SessionId
        # 进程自己退出时任务实例随之结束，上次结果是它的退出码。
        if ($launch.StaysAlive) { $installerCase.Processes = @($installerCase.Processes) + @($process) }
        else { Set-CountdownInstallerTestTaskRun -Task $task -State 'Ready' -LastTaskResult $launch.ExitCode }
        if (-not $launch.WritesReceipt) { return }
        $applicationRoot = Split-Path -Parent (Split-Path -Parent $exePath)
        $receipt = [ordered]@{
            schemaVersion = 4
            stage = 'window-ready'
            launchToken = [guid]::NewGuid().ToString('N')
            processId = $processId
            sessionId = $launch.SessionId
            applicationPath = Join-Path $applicationRoot '敏敏子倒计时.ps1'
            readyAtUtc = '2026-11-14T09:20:00.0000000+00:00'
            calendarState = 'Pending'
            timeZoneId = 'China Standard Time'
        }
        [System.IO.File]::WriteAllText((Join-Path $applicationRoot 'runtime-status.json'), ($receipt | ConvertTo-Json), $installerUtf8)
    }
    # 真实改名。用例可让某次改名遇到占用（MoveLocks 的一条规则：From、To 是源与目标的目录名，Remaining 是还要失败几次，
    # -1 为一直失败）：改名期间源目录里的一个文件被别的句柄打开，像杀毒软件正在扫描刚运行过的 EXE，真实的 Directory.Move
    # 因此报拒绝访问（即使那个句柄允许删除也一样）。
    MoveDirectory = {
        param([string] $Source, [string] $Destination)
        $lock = $null
        $rule = @($installerCase.MoveLocks | Where-Object {
                $_.From -eq [System.IO.Path]::GetFileName($Source) -and $_.To -eq [System.IO.Path]::GetFileName($Destination) -and $_.Remaining -ne 0
            }) | Select-Object -First 1
        if ($null -ne $rule) {
            if ($rule.Remaining -gt 0) { $rule.Remaining-- }
            $lockedFile = @(Get-ChildItem -LiteralPath $Source -File -Recurse)[0].FullName
            $lock = [System.IO.File]::Open($lockedFile, 'Open', 'Read', 'ReadWrite, Delete')
        }
        try { [System.IO.Directory]::Move($Source, $Destination) }
        finally { if ($null -ne $lock) { $lock.Dispose() } }
        if ($null -ne $installerCase.OnMoveDirectory) { & $installerCase.OnMoveDirectory }
    }
    # 任务定义的导出与恢复（#283）：导出是不含运行状态的整份定义，恢复按导出整份替换；导出为空表示安装前没有这个任务，恢复即删除。
    ExportTask = {
        param([string] $Name)
        if ($installerCase.Tasks.ContainsKey($Name)) { Get-CountdownInstallerTestTaskDefinition -Task $installerCase.Tasks[$Name] }
    }
    RestoreTask = {
        param([string] $Name, [object] $Definition)
        if ($null -eq $Definition) {
            [void] $installerCase.Tasks.Remove($Name)
            return
        }
        $restored = [string] $Definition | ConvertFrom-Json
        $restored | Add-Member -NotePropertyName State -NotePropertyValue 'Ready'
        $installerCase.Tasks[$Name] = $restored
    }
    # 交互用户的桌面目录是用例自己的临时目录；用例把它置空时模拟读不到桌面位置。安装在前置检查里读它，中断恢复（#284）的用例
    # 经 OnGetDesktopDirectory 在这一刻记下恢复之后、这次安装动手之前的安装根。
    GetDesktopDirectory = {
        param([object] $Session)
        if ($null -ne $installerCase.OnGetDesktopDirectory) { & $installerCase.OnGetDesktopDirectory }
        if ($null -eq $installerCase.Desktop) { throw '读不到交互用户的桌面位置。' }
        $installerCase.Desktop
    }
    ReadShortcut = $installerWindowsPorts.ReadShortcut
    WriteShortcut = $installerWindowsPorts.WriteShortcut
    RemoveShortcut = $installerWindowsPorts.RemoveShortcut
    # 候选 EXE 的无界面命令：设置迁移（#278）与设置等价判定（#272），按 $installerCase.Rehearsal 作答并记下每次调用。
    # 迁移像产品一样只读写交给它的设置文件：Migration 为 migrated 时轮转两代备份、改写主文件并报告固定的迁移时刻，
    # current-schema 时不写文件，其他值是失败类别。等价判定在两份字节相同时报告未变化；字节不同时，只有收到这次迁移
    # 报告的时刻才按 Rehearsal.Equivalence 作答，否则报告数据不一致：迁移结果取决于迁移时刻，判定必须用同一时刻。
    RunHeadlessCommand = {
        param([string] $ExecutablePath, [string[]] $Arguments, [string] $WorkingDirectory)
        $installerCase.Commands.Add([pscustomobject]@{ ExecutablePath = $ExecutablePath; Arguments = @($Arguments); WorkingDirectory = $WorkingDirectory })
        $value = { param([string] $Option) $Arguments[[array]::IndexOf($Arguments, $Option) + 1] }
        if ($Arguments[0] -eq '--settings-migration') {
            $path = & $value '--settings-file'
            $category = [string] $installerCase.Rehearsal.Migration
            if ($category -eq 'migrated') {
                if (Test-Path -LiteralPath "$path.backup1") { [System.IO.File]::Copy("$path.backup1", "$path.backup2", $true) }
                [System.IO.File]::Copy($path, "$path.backup1", $true)
                [System.IO.File]::WriteAllText($path, '{"schemaVersion":10,"note":"migrated by the candidate"}', $installerUtf8)
            }
            $succeeded = $category -in @('migrated', 'current-schema')
            $report = [ordered]@{
                schemaVersion = 1
                succeeded = $succeeded
                migrated = $category -eq 'migrated'
                migrationUtc = if ($succeeded) { $installerMigrationUtc } else { $null }
                reasonCategory = $category
                message = 'fake settings migration'
            }
            return [pscustomobject]@{ ExitCode = if ($succeeded) { 0 } else { 1 }; Output = $report | ConvertTo-Json -Compress }
        }
        $sameBytes = (Get-FileHash -LiteralPath (& $value '--before-settings')).Hash -eq (Get-FileHash -LiteralPath (& $value '--after-settings')).Hash
        $category = if ($sameBytes) { 'unchanged' } elseif ((& $value '--migration-utc') -ne $installerMigrationUtc) { 'data-mismatch' } else { [string] $installerCase.Rehearsal.Equivalence }
        $equivalent = $category -in @('unchanged', 'migrated')
        $report = [ordered]@{ schemaVersion = 1; equivalent = $equivalent; reasonCategory = $category; message = 'fake settings equivalence' }
        [pscustomobject]@{ ExitCode = if ($equivalent) { 0 } else { 1 }; Output = $report | ConvertTo-Json -Compress }
    }
}

# 在临时桌面上直接用 WScript.Shell 写一个快捷方式（不经安装器），时间戳固定在安装之前：被改写时字节或时间会变。
function Write-CountdownInstallerTestShortcut {
    param(
        [Parameter(Mandatory)] [string] $Path,
        [Parameter(Mandatory)] [string] $TargetPath,
        [AllowEmptyString()] [string] $Arguments = '',
        [AllowEmptyString()] [string] $WorkingDirectory = '',
        [AllowEmptyString()] [string] $IconLocation = ''
    )

    $link = (New-Object -ComObject WScript.Shell).CreateShortcut($Path)
    $link.TargetPath = $TargetPath
    $link.Arguments = $Arguments
    $link.WorkingDirectory = $WorkingDirectory
    # WScript.Shell 不接受空的图标位置；不给时保留快捷方式默认的“无图标”。
    if (-not [string]::IsNullOrEmpty($IconLocation)) { $link.IconLocation = $IconLocation }
    $link.Save()
    [System.IO.File]::SetCreationTimeUtc($Path, $installerUserDataTimeUtc)
    [System.IO.File]::SetLastWriteTimeUtc($Path, $installerUserDataTimeUtc)
}

# 直接用 WScript.Shell 读回快捷方式：目标|参数|工作目录|图标。
function Read-CountdownInstallerTestShortcut {
    param([Parameter(Mandatory)] [string] $Path)
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { return '<missing>' }
    $link = (New-Object -ComObject WScript.Shell).CreateShortcut($Path)
    "$($link.TargetPath)|$($link.Arguments)|$($link.WorkingDirectory)|$($link.IconLocation)"
}

# 登录任务的运行状态，与任务计划一样（#279、#288 实测）：直接运行 EXE 的任务，实例与进程同生同灭；启动时更新上次运行时间，
# 上次结果为 0x41301（运行中），进程退出后任务回到 Ready，上次结果就是进程的退出码。
function Set-CountdownInstallerTestTaskRun {
    param(
        [Parameter(Mandatory)] [object] $Task,
        [Parameter(Mandatory)] [string] $State,
        [Parameter(Mandatory)] [long] $LastTaskResult,
        [switch] $NewRun
    )

    $Task.State = $State
    if ($NewRun) { $Task | Add-Member -NotePropertyName LastRunTime -NotePropertyValue ([DateTime]::Now) -Force }
    $Task | Add-Member -NotePropertyName LastTaskResult -NotePropertyValue $LastTaskResult -Force
}

function New-CountdownInstallerTestProcess {
    param([int] $ProcessId, [string] $ExecutablePath, [string] $StartedByTask)
    [pscustomobject]@{
        ProcessId = $ProcessId
        Name = 'MinminCountdown.exe'
        ExecutablePath = $ExecutablePath
        SessionId = 3
        CreationDate = [DateTimeOffset]::Parse('2026-11-14T09:19:58Z').LocalDateTime
        StartedByTask = $StartedByTask
    }
}

# 这次启动的是不是新版本：EXE 旁边的 DLL 与包里的 DLL 相同（各版本的 EXE 逐字节相同，认不出版本）。
function Test-CountdownInstallerTestNewVersion {
    param([Parameter(Mandatory)] [string] $ExecutablePath)
    $dll = Join-Path (Split-Path -Parent $ExecutablePath) 'MinminCountdown.dll'
    (Get-CountdownInstallerTestFileHash -Path $dll) -eq (Get-CountdownInstallerTestFileHash -Path (Join-Path $installerCase.PackageRoot 'app\MinminCountdown.dll'))
}

# 程序目录里现役的是哪一版：DLL 的 SHA-256。
function Get-CountdownInstallerTestInstalledDll {
    Get-CountdownInstallerTestFileHash -Path (Join-Path $installerCase.Root 'app\MinminCountdown.dll')
}

# 新版本启动时按正常流程迁移并保存现役设置（#259 第 8 步），像产品的两代备份轮转：合法的旧 backup 1 成为 backup 2，
# 迁移前的主文件成为 backup 1，主文件换成迁移结果。安装前没有 backup 2 时，迁移后才有。
function Invoke-CountdownInstallerTestStartupMigration {
    $settingsPath = Join-Path $installerCase.Root 'settings.json'
    if (Test-Path -LiteralPath "$settingsPath.backup1") { [System.IO.File]::Copy("$settingsPath.backup1", "$settingsPath.backup2", $true) }
    if (Test-Path -LiteralPath $settingsPath) { [System.IO.File]::Copy($settingsPath, "$settingsPath.backup1", $true) }
    [System.IO.File]::WriteAllText($settingsPath, '{"schemaVersion":11,"note":"migrated by the new version at startup"}', $installerUtf8)
}

function Write-CountdownInstallerTestProgram {
    param(
        [Parameter(Mandatory)] [string] $Root,
        [Parameter(Mandatory)] [object[]] $Files,
        [Parameter(Mandatory)] [string] $Commit,
        [Parameter(Mandatory)] [string] $AggregateSha256
    )

    foreach ($file in $Files) {
        $path = Join-Path $Root $file.Path
        New-Item -ItemType Directory -Path (Split-Path -Parent $path) -Force | Out-Null
        [System.IO.File]::WriteAllText($path, $file.Text, $installerUtf8)
    }
    $manifest = [ordered]@{
        schemaVersion = 1
        commit = $Commit
        aggregateSha256 = $AggregateSha256
        files = @($Files | ForEach-Object { [ordered]@{ path = $_.Path; sha256 = $_.Sha256 } })
    }
    [System.IO.File]::WriteAllText((Join-Path $Root 'app\published-payload.json'), ($manifest | ConvertTo-Json -Depth 4), $installerUtf8)
}

function New-CountdownInstallerTestCase {
    param([Parameter(Mandatory)] [string] $Name)

    $caseRoot = Join-Path $installerTestRoot $Name
    $packageRoot = Join-Path $caseRoot 'package'
    Write-CountdownInstallerTestProgram -Root $packageRoot -Files $installerPackageFilesV2 -Commit $installerCommitV2 -AggregateSha256 $installerAggregateV2
    $root = Join-Path $caseRoot 'install'
    $desktop = Join-Path $caseRoot 'desktop'
    New-Item -ItemType Directory -Path $desktop -Force | Out-Null
    [pscustomobject]@{
        Name = $Name
        PackageRoot = $packageRoot
        Root = $root
        Desktop = $desktop
        ExePath = Join-Path $root 'app\MinminCountdown.exe'
        TaskName = "Minmin Countdown Installer $Name"
        Sessions = @([pscustomobject]@{ SessionId = 3; User = 'MINMIN-OFFICE\installer-account-sentinel'; Sid = $installerSessionSid })
        Processes = @()
        Tasks = @{}
        NextProcessId = 6001
        Launch = [pscustomobject]@{ SessionId = 3; StaysAlive = $true; WritesReceipt = $true; ExitCode = 0 }
        PreviousLaunch = [pscustomobject]@{ SessionId = 3; StaysAlive = $true; WritesReceipt = $true; ExitCode = 0 }
        MoveLocks = @()
        OnStopProcess = $null
        OnMoveDirectory = $null
        OnStartTask = $null
        OnGetDesktopDirectory = $null
        Rehearsal = [pscustomobject]@{ Migration = 'current-schema'; Equivalence = 'migrated' }
        Commands = [System.Collections.Generic.List[object]]::new()
        Observed = @{}
        Json = $null
    }
}

# 与期望定义逐项一致的登录任务，属于交互会话的登录用户（SID 相同）。UserId 是当初注册时的显示名：账号改过名时
# 与交互会话的显示名不同，SID 不变。
function New-CountdownInstallerTestTask {
    param([string] $ExecutablePath, [string] $State, [string] $UserId)
    [pscustomobject]@{
        State = $State
        Enabled = $true
        Actions = @([pscustomobject]@{ Type = 'Exec'; Execute = $ExecutablePath; Arguments = $null; WorkingDirectory = $null })
        Triggers = @([pscustomobject]@{ Type = 'Logon'; Enabled = $true; UserSid = $installerSessionSid })
        UserSid = $installerSessionSid
        LogonType = 'Interactive'
        RunLevel = 'Limited'
        Priority = 4
        ExecutionTimeLimit = 'PT0S'
        MultipleInstances = 'IgnoreNew'
        DisallowStartIfOnBatteries = $false
        StopIfGoingOnBatteries = $false
        AllowDemandStart = $true
        AllowHardTerminate = $true
        UserId = $UserId
    }
}

# 任务定义的逐项快照（不含随运行变化的状态、上次运行时间与上次结果），用来比较安装前后有没有被动过。
function Get-CountdownInstallerTestTaskDefinition {
    param([AllowNull()] [object] $Task)
    if ($null -eq $Task) { return '<missing>' }
    $Task | Select-Object -Property * -ExcludeProperty State, LastRunTime, LastTaskResult | ConvertTo-Json -Depth 4 -Compress
}

function Write-CountdownInstallerTestUserData {
    param([Parameter(Mandatory)] [string] $Root)
    foreach ($relative in $installerUserDataFiles.Keys) {
        $path = Join-Path $Root $relative
        New-Item -ItemType Directory -Path (Split-Path -Parent $path) -Force | Out-Null
        [System.IO.File]::WriteAllText($path, $installerUserDataFiles[$relative], $installerUtf8)
        [System.IO.File]::SetCreationTimeUtc($path, $installerUserDataTimeUtc)
        [System.IO.File]::SetLastWriteTimeUtc($path, $installerUserDataTimeUtc)
    }
}

# 已装好的 v1：程序目录（含一个 v2 不再带的文件）、旧的运行状态回执、用户数据与登录任务；应用一份经任务、一份从桌面启动，
# 另有开发目录里的同名 EXE 在跑。
function Initialize-CountdownInstallerTestInstalledVersion {
    Write-CountdownInstallerTestProgram -Root $installerCase.Root -Files $installerProgramFilesV1 -Commit $installerCommitV1 -AggregateSha256 $installerAggregateV1
    [System.IO.File]::WriteAllText((Join-Path $installerCase.Root 'app\retired-in-v2.dll'), 'installer retired in v2', $installerUtf8)
    [System.IO.File]::WriteAllText((Join-Path $installerCase.Root 'runtime-status.json'), '{"schemaVersion":4,"stage":"window-ready","processId":4101,"sessionId":3}', $installerUtf8)
    Write-CountdownInstallerTestUserData -Root $installerCase.Root
    $installerCase.Tasks[$installerCase.TaskName] = New-CountdownInstallerTestTask -ExecutablePath $installerCase.ExePath -State 'Running' -UserId 'existing-task-owner'
    $installerCase.Processes = @(
        New-CountdownInstallerTestProcess -ProcessId 4101 -ExecutablePath $installerCase.ExePath -StartedByTask $installerCase.TaskName
        New-CountdownInstallerTestProcess -ProcessId 4102 -ExecutablePath $installerCase.ExePath
        New-CountdownInstallerTestProcess -ProcessId 4103 -ExecutablePath 'D:\dev-checkout\app\MinminCountdown.exe'
    )
}

function Get-CountdownInstallerTestRunning {
    (@($installerCase.Processes | ForEach-Object { [int] $_.ProcessId } | Sort-Object)) -join ','
}

function Get-CountdownInstallerTestTasks {
    (@($installerCase.Tasks.Keys | Sort-Object | ForEach-Object { "$_=$($installerCase.Tasks[$_].State)/$($installerCase.Tasks[$_].UserId)" })) -join ','
}

# 目录树清单（序数排序，目录以 \ 结尾）。-Detailed 时文件带长度、创建与修改时间和 SHA-256：被移动、复制或改写都会让它变。
function Get-CountdownInstallerTestTree {
    param(
        [Parameter(Mandatory)] [string] $Root,
        [string[]] $Exclude = @(),
        [switch] $Detailed
    )

    if (-not (Test-Path -LiteralPath $Root)) { return '<missing>' }
    $prefix = $Root.TrimEnd('\') + '\'
    [string[]] $entries = @(Get-ChildItem -LiteralPath $Root -Recurse -Force | ForEach-Object {
            $relative = $_.FullName.Substring($prefix.Length)
            if ($relative.Split('\')[0] -in $Exclude) { return }
            if ($_.PSIsContainer) { "$relative\" }
            elseif ($Detailed) { "$relative|$($_.Length)|$($_.CreationTimeUtc.ToString('o'))|$($_.LastWriteTimeUtc.ToString('o'))|$((Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash)" }
            else { $relative }
        })
    [Array]::Sort($entries, [System.StringComparer]::Ordinal)
    $entries -join ','
}

function Invoke-CountdownInstallerTest {
    param([switch] $CheckOnly, [object] $Ports = $installerPorts)

    $output = @(& $installerScript -PackageRoot $installerCase.PackageRoot -InstallRoot $installerCase.Root -TaskName $installerCase.TaskName -CheckOnly:$CheckOnly -Port $Ports -LockName $installerLockName)
    Assert-Equal 1 $output.Count "Installer writes exactly one JSON document: $($installerCase.Name)"
    $installerCase.Json = [string] $output[-1]
    $installerCase.Json | ConvertFrom-Json -DateKind String
}

function Get-CountdownInstallerTestFingerprint {
    param([AllowNull()] [object] $Fingerprint)
    if ($null -eq $Fingerprint) { return '<null>' }
    "$($Fingerprint.present)|$($Fingerprint.length)|$($Fingerprint.lastWriteTimeUtc)|$($Fingerprint.sha256)"
}

function Invoke-CountdownInstallerStatusRead {
    $output = @(& $installerStatusScript -InstallRoot $installerCase.Root -TaskName $installerCase.TaskName -Port $installerPorts)
    [string] $output[-1] | ConvertFrom-Json -DateKind String
}

function Get-CountdownInstallerTestFileHash {
    param([Parameter(Mandatory)] [string] $Path)
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { return 'missing' }
    (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash
}

# 设置及其两代备份的 SHA-256（不在的记 missing），核对快照与现役设置用。
function Get-CountdownInstallerTestSettingsFamily {
    param([Parameter(Mandatory)] [string] $Directory)
    (@('settings.json', 'settings.json.backup1', 'settings.json.backup2') | ForEach-Object { "$_=$(Get-CountdownInstallerTestFileHash -Path (Join-Path $Directory $_))" }) -join ';'
}

try {
    # 曳光弹：首次安装。安装根还不存在，也没有登录任务；装完后程序目录与包一致，登录任务已创建，应用经任务启动到启动就绪，
    # 事务标记、旧版本目录和候选目录都不在；状态命令读回运行中的已安装 EXE、与期望一致的登录任务和清单记录的提交。
    $installerCase = New-CountdownInstallerTestCase -Name 'first-install'
    $firstInstall = Invoke-CountdownInstallerTest
    Assert-Equal $true ([bool] $firstInstall.succeeded) 'First install succeeds'
    Assert-Equal $true ($null -eq $firstInstall.failedStage) 'First install reports no failed stage'
    Assert-Equal $true ([bool] $firstInstall.firstInstall) 'First install is reported as a first install'
    Assert-Equal 'created|0' "$($firstInstall.loginTask.action)|$(@($firstInstall.loginTask.changes).Count)" 'First install creates the missing login task'
    Assert-Equal "$installerShortcutName|created||0" "$($firstInstall.desktopShortcut.name)|$($firstInstall.desktopShortcut.action)|$($firstInstall.desktopShortcut.reason)|$(@($firstInstall.desktopShortcut.changes).Count)" 'First install creates the missing desktop shortcut'
    Assert-Equal "$($installerCase.ExePath)||$(Join-Path $installerCase.Root 'app')|$($installerCase.ExePath),0" (Read-CountdownInstallerTestShortcut -Path (Join-Path $installerCase.Desktop $installerShortcutName)) 'The desktop shortcut starts the installed EXE with no arguments from the program directory and shows the icon embedded in the EXE'
    Assert-Equal $installerShortcutName ((@(Get-ChildItem -LiteralPath $installerCase.Desktop -Force) | ForEach-Object { $_.Name }) -join ',') 'First install puts only the shortcut on the desktop'
    $firstTask = $installerCase.Tasks[$installerCase.TaskName]
    Assert-Equal '4|False|False' "$($firstTask.Priority)|$($firstTask.DisallowStartIfOnBatteries)|$($firstTask.StopIfGoingOnBatteries)" 'First install registers the login task at priority 4, starting and staying on battery power'
    Assert-Equal 6001 ([int] $firstInstall.readyProcess.processId) 'First install reports the process that reached startup readiness'
    Assert-Equal 3 ([int] $firstInstall.readyProcess.sessionId) 'First install reports the interactive session of the ready process'
    foreach ($file in $installerPackageFilesV2) {
        Assert-Equal $file.Sha256 (Get-CountdownInstallerTestFileHash -Path (Join-Path $installerCase.Root $file.Path)) "First install places the package file: $($file.Path)"
    }
    Assert-Equal (Get-CountdownInstallerTestFileHash -Path (Join-Path $installerCase.PackageRoot 'app\published-payload.json')) (Get-CountdownInstallerTestFileHash -Path (Join-Path $installerCase.Root 'app\published-payload.json')) 'First install keeps the payload manifest in the program directory'
    Assert-Equal $false (Test-Path -LiteralPath (Join-Path $installerCase.Root 'install-transaction.json')) 'First install leaves no transaction marker'
    Assert-Equal $false (Test-Path -LiteralPath (Join-Path $installerCase.Root 'app.previous')) 'First install leaves no previous-version directory'
    Assert-Equal $false (Test-Path -LiteralPath (Join-Path $installerCase.Root 'app.candidate')) 'First install leaves no candidate directory'
    $firstStatus = Invoke-CountdownInstallerStatusRead
    Assert-Equal $true ([bool] $firstStatus.running) 'Status reads the installed application running after the first install'
    Assert-Equal $true ([bool] $firstStatus.exe.matchesManifest) 'Status reads the installed EXE matching the manifest after the first install'
    Assert-Equal $installerCommitV2 ([string] $firstStatus.commit) 'Status reads the installed commit after the first install'
    Assert-Equal $true ([bool] $firstStatus.loginTask.matchesExpected) "Status reads the created login task as the expected definition: $(@($firstStatus.loginTask.mismatches) -join ',')"
    Assert-Equal $false ([bool] $firstStatus.transactionMarkerPresent) 'Status reads no unfinished transaction after the first install'

    # 升级：在已装好的 v1 上安装（设置、设置备份、恢复证据、诊断日志与节假日缓存都在）。升级与首次安装同一流程：
    # 只停已安装 EXE 的进程，整个替换程序目录，沿用已有登录任务；新版本启动时与提交后，用户数据都与安装前一致。
    $installerCase = New-CountdownInstallerTestCase -Name 'upgrade'
    Initialize-CountdownInstallerTestInstalledVersion
    $userDataBefore = Get-CountdownInstallerTestTree -Root $installerCase.Root -Exclude $installerOwnEntries -Detailed
    $upgradeTaskBefore = Get-CountdownInstallerTestTaskDefinition -Task $installerCase.Tasks[$installerCase.TaskName]
    $installerCase.OnStartTask = {
        $installerCase.Observed.UserData = Get-CountdownInstallerTestTree -Root $installerCase.Root -Exclude $installerOwnEntries -Detailed
        $installerCase.Observed.MarkerPresent = Test-Path -LiteralPath (Join-Path $installerCase.Root 'install-transaction.json')
        $installerCase.Observed.PreviousPresent = Test-Path -LiteralPath (Join-Path $installerCase.Root 'app.previous')
        $installerCase.Observed.ReceiptPresent = Test-Path -LiteralPath (Join-Path $installerCase.Root 'runtime-status.json')
        $installerCase.Observed.RunningBeforeStart = @($installerCase.Processes | ForEach-Object { $_.ProcessId }) -join ','
    }
    $upgrade = Invoke-CountdownInstallerTest
    Assert-Equal $true ([bool] $upgrade.succeeded) "Upgrade succeeds: $($upgrade.failedStage) $($upgrade.error)"
    Assert-Equal $false ([bool] $upgrade.firstInstall) 'Upgrade is reported as an upgrade'
    Assert-Equal 'unchanged|0' "$($upgrade.loginTask.action)|$(@($upgrade.loginTask.changes).Count)" 'Upgrade reports a login task that matches the expected definition as unchanged'
    Assert-Equal "$($installerCase.TaskName)=Running/existing-task-owner" (Get-CountdownInstallerTestTasks) 'Upgrade keeps the existing login task as it is'
    Assert-Equal $upgradeTaskBefore (Get-CountdownInstallerTestTaskDefinition -Task $installerCase.Tasks[$installerCase.TaskName]) 'Upgrade leaves every item of a matching login task as it was'
    Assert-Equal 2 ([int] $upgrade.stoppedProcessCount) 'Upgrade stops the task-started and the desktop-started application'
    Assert-Equal '4103' ([string] $installerCase.Observed.RunningBeforeStart) 'Upgrade stops only processes of the installed EXE before starting the new version'
    Assert-Equal '4103,6001' (Get-CountdownInstallerTestRunning) 'Upgrade leaves the same-named EXE outside the install root running'
    Assert-Equal 6001 ([int] $upgrade.readyProcess.processId) 'Upgrade reaches startup readiness with the newly started process'
    Assert-Equal 'MinminCountdown.dll,MinminCountdown.exe,holiday-data\,holiday-data\2027.json,published-payload.json' (Get-CountdownInstallerTestTree -Root (Join-Path $installerCase.Root 'app')) 'Upgrade replaces the whole program directory with the package'
    foreach ($file in $installerPackageFilesV2) {
        Assert-Equal $file.Sha256 (Get-CountdownInstallerTestFileHash -Path (Join-Path $installerCase.Root $file.Path)) "Upgrade places the package file: $($file.Path)"
    }
    Assert-Equal $true ([bool] $installerCase.Observed.MarkerPresent) 'Upgrade starts the new version while the transaction marker is present'
    Assert-Equal $true ([bool] $installerCase.Observed.PreviousPresent) 'Upgrade keeps the previous version until it commits'
    Assert-Equal $false ([bool] $installerCase.Observed.ReceiptPresent) 'Upgrade deletes the old runtime receipt before starting the new version'
    Assert-Equal $false (Test-Path -LiteralPath (Join-Path $installerCase.Root 'install-transaction.json')) 'Upgrade leaves no transaction marker after commit'
    Assert-Equal $false (Test-Path -LiteralPath (Join-Path $installerCase.Root 'app.previous')) 'Upgrade leaves no previous-version directory after commit'
    Assert-Equal $false (Test-Path -LiteralPath (Join-Path $installerCase.Root 'app.candidate')) 'Upgrade leaves no candidate directory after commit'
    Assert-Equal $userDataBefore ([string] $installerCase.Observed.UserData) 'User data is in place and unchanged when the new version starts'
    Assert-Equal $userDataBefore (Get-CountdownInstallerTestTree -Root $installerCase.Root -Exclude $installerOwnEntries -Detailed) 'User data is not moved, copied or rewritten by the upgrade'
    Assert-Equal $installerSettingsFingerprint (Get-CountdownInstallerTestFingerprint -Fingerprint $upgrade.settings.before) 'Result JSON fingerprints the settings before the upgrade'
    Assert-Equal $installerSettingsFingerprint (Get-CountdownInstallerTestFingerprint -Fingerprint $upgrade.settings.after) 'Result JSON fingerprints the settings after the upgrade'
    foreach ($secret in @('settings-content-sentinel-280', 'installer-account-sentinel', 'MINMIN-OFFICE')) {
        Assert-Equal $false ($installerCase.Json.Contains($secret, [System.StringComparison]::OrdinalIgnoreCase)) "Result JSON leaves out settings content and the account: $secret"
    }
    $upgradeStatus = Invoke-CountdownInstallerStatusRead
    Assert-Equal $installerCommitV2 ([string] $upgradeStatus.commit) 'Status reads the new commit after the upgrade'
    Assert-Equal '6001' ((@($upgradeStatus.processes) | ForEach-Object { $_.processId }) -join ',') 'Status reads only the new version running from the install root after the upgrade'

    # 结束进程前按 PID 重新读映像路径：4101 被结束的同时 4102 自行退出，它的 PID 被开发目录里的同名 EXE 复用。
    # 安装器不能凭先前的枚举结束这个进程。
    $installerCase = New-CountdownInstallerTestCase -Name 'pid-reused-before-stop'
    Initialize-CountdownInstallerTestInstalledVersion
    $installerCase.OnStopProcess = {
        param([int] $ProcessId)
        if ($ProcessId -eq 4101) {
            $installerCase.Processes = @($installerCase.Processes | Where-Object { $_.ProcessId -ne 4102 }) + @(New-CountdownInstallerTestProcess -ProcessId 4102 -ExecutablePath 'D:\dev-checkout\app\MinminCountdown.exe')
        }
    }
    $reusedPid = Invoke-CountdownInstallerTest
    Assert-Equal $true ([bool] $reusedPid.succeeded) "Install succeeds when a PID is reused while the application stops: $($reusedPid.failedStage) $($reusedPid.error)"
    Assert-Equal '4102,4103,6001' (Get-CountdownInstallerTestRunning) 'Install does not stop a process whose PID was reused by another program'

    # 登录任务与期望定义（#279「登录任务的最终定义」）逐项不一致（#281）：旧式动作（pwsh 加 adapter 参数与工作目录）、停用、
    # 触发器与主体都是另一个用户、密码登录、提升、默认优先级 7、运行时限 72 小时、允许并行、电池供电不启动且拔电即停、不能按需
    # 启动也不能强制结束。安装按期望定义重新注册它，结果逐项写明改前改后的值（用户只写类别，不写账号或 SID），
    # 新版本经更新后的任务启动到启动就绪。
    $installerCase = New-CountdownInstallerTestCase -Name 'login-task-update'
    Initialize-CountdownInstallerTestInstalledVersion
    $installerCase.Processes[0].StartedByTask = $null
    $legacyPwsh = Join-Path (Split-Path -Parent $installerCase.Root) 'no-such-pwsh\pwsh.exe'
    $legacyArguments = "-NoLogo -NoProfile -ExecutionPolicy RemoteSigned -WindowStyle Hidden -STA -File `"$(Join-Path $installerCase.Root '敏敏子倒计时.ps1')`""
    $installerCase.Tasks[$installerCase.TaskName] = [pscustomobject]@{
        State = 'Disabled'
        Enabled = $false
        Actions = @([pscustomobject]@{ Type = 'Exec'; Execute = $legacyPwsh; Arguments = $legacyArguments; WorkingDirectory = $installerCase.Root })
        Triggers = @([pscustomobject]@{ Type = 'Logon'; Enabled = $true; UserSid = $installerOtherSid })
        UserSid = $installerOtherSid
        LogonType = 'Password'
        RunLevel = 'Highest'
        Priority = 7
        ExecutionTimeLimit = 'PT72H'
        MultipleInstances = 'Parallel'
        DisallowStartIfOnBatteries = $true
        StopIfGoingOnBatteries = $true
        AllowDemandStart = $false
        AllowHardTerminate = $false
        UserId = 'legacy-task-owner'
    }
    $taskUpdate = Invoke-CountdownInstallerTest
    Assert-Equal $true ([bool] $taskUpdate.succeeded) "Install with a mismatched login task succeeds: $($taskUpdate.failedStage) $($taskUpdate.error)"
    Assert-Equal 'updated' ([string] $taskUpdate.loginTask.action) 'Result JSON reports the mismatched login task as updated'
    $expectedTaskChanges = @(
        'enabled=False->True'
        "action=Exec $legacyPwsh $legacyArguments (in $($installerCase.Root))->Exec $($installerCase.ExePath)"
        'trigger=Logon for other user->Logon for interactive user'
        'principal=other user->interactive user'
        'logonType=Password->Interactive'
        'runLevel=Highest->Limited'
        'priority=7->4'
        'executionTimeLimit=PT72H->PT0S'
        'multipleInstances=Parallel->IgnoreNew'
        'disallowStartIfOnBatteries=True->False'
        'stopIfGoingOnBatteries=True->False'
        'allowDemandStart=False->True'
        'allowHardTerminate=False->True'
    ) -join '|'
    Assert-Equal $expectedTaskChanges ((@($taskUpdate.loginTask.changes) | ForEach-Object { "$($_.item)=$($_.before)->$($_.after)" }) -join '|') 'Result JSON names every login task item that changed, with its value before and after'
    $updatedTask = $installerCase.Tasks[$installerCase.TaskName]
    $updatedAction = @($updatedTask.Actions)
    $updatedTrigger = @($updatedTask.Triggers)
    Assert-Equal "1|Exec|$($installerCase.ExePath)|||1|Logon|True|$installerSessionSid" "$($updatedAction.Count)|$($updatedAction[0].Type)|$($updatedAction[0].Execute)|$($updatedAction[0].Arguments)|$($updatedAction[0].WorkingDirectory)|$($updatedTrigger.Count)|$($updatedTrigger[0].Type)|$($updatedTrigger[0].Enabled)|$($updatedTrigger[0].UserSid)" 'The updated login task runs the installed EXE with no arguments at the interactive user''s logon'
    Assert-Equal "True|$installerSessionSid|Interactive|Limited|4|PT0S|IgnoreNew|False|False|True|True" "$($updatedTask.Enabled)|$($updatedTask.UserSid)|$($updatedTask.LogonType)|$($updatedTask.RunLevel)|$($updatedTask.Priority)|$($updatedTask.ExecutionTimeLimit)|$($updatedTask.MultipleInstances)|$($updatedTask.DisallowStartIfOnBatteries)|$($updatedTask.StopIfGoingOnBatteries)|$($updatedTask.AllowDemandStart)|$($updatedTask.AllowHardTerminate)" 'The updated login task has every other item of the expected definition'
    Assert-Equal 6001 ([int] $taskUpdate.readyProcess.processId) 'The new version starts through the updated login task'
    Assert-Equal $true ([bool] (Invoke-CountdownInstallerStatusRead).loginTask.matchesExpected) 'Status reads the updated login task as the expected definition'
    foreach ($secret in @('installer-account-sentinel', 'MINMIN-OFFICE', 'legacy-task-owner', 'S-1-5-21-2810000000')) {
        Assert-Equal $false ($installerCase.Json.Contains($secret, [System.StringComparison]::OrdinalIgnoreCase)) "Result JSON reports the login task change without accounts or SIDs: $secret"
    }

    # 桌面快捷方式的三种情况（#281），都在已装好的 v1 上升级；桌面上另有旧安装器的启动器 敏敏子倒计时.exe（遗留启动器属于
    # 一次性切换，安装器不碰）。属于安装器的判定：同名的 敏敏子倒计时.lnk 是一个 Shell Link，且目标是本安装根的已安装 EXE。
    # 是安装器的：与期望一致时不动（字节与时间都不变），参数、工作目录或图标不一致时改回期望，结果写明改了什么。
    # 不是安装器的（目标指向别处的快捷方式、碰巧同名的普通文件或目录）：原样保留，结果写明原因，安装照常成功。
    $installerShortcutCases = @(
        [pscustomobject]@{ Name = 'shortcut-owned-current'; Action = 'unchanged'; Reason = ''; Changes = { '' }; Arrange = {
                Write-CountdownInstallerTestShortcut -Path (Join-Path $installerCase.Desktop $installerShortcutName) -TargetPath $installerCase.ExePath -WorkingDirectory (Join-Path $installerCase.Root 'app') -IconLocation "$($installerCase.ExePath),0"
            } }
        [pscustomobject]@{ Name = 'shortcut-owned-stale'; Action = 'updated'; Reason = ''; Arrange = {
                Write-CountdownInstallerTestShortcut -Path (Join-Path $installerCase.Desktop $installerShortcutName) -TargetPath $installerCase.ExePath -Arguments '--legacy-launcher' -WorkingDirectory $installerCase.Root -IconLocation "$(Join-Path $installerCase.Root 'countdown-icon.ico'),0"
            }; Changes = { "arguments=--legacy-launcher->|workingDirectory=$($installerCase.Root)->$(Join-Path $installerCase.Root 'app')|iconLocation=$(Join-Path $installerCase.Root 'countdown-icon.ico'),0->$($installerCase.ExePath),0" } }
        [pscustomobject]@{ Name = 'shortcut-foreign-target'; Action = 'kept-foreign'; Reason = 'other-target'; Changes = { '' }; Arrange = {
                Write-CountdownInstallerTestShortcut -Path (Join-Path $installerCase.Desktop $installerShortcutName) -TargetPath (Join-Path (Split-Path -Parent $installerCase.Root) 'elsewhere\other-app.exe')
            } }
        [pscustomobject]@{ Name = 'shortcut-foreign-file'; Action = 'kept-foreign'; Reason = 'not-a-shell-link'; Changes = { '' }; Arrange = {
                $foreignPath = Join-Path $installerCase.Desktop $installerShortcutName
                [System.IO.File]::WriteAllText($foreignPath, 'my own notes that happen to share the shortcut name', $installerUtf8)
                [System.IO.File]::SetLastWriteTimeUtc($foreignPath, $installerUserDataTimeUtc)
            } }
        [pscustomobject]@{ Name = 'shortcut-foreign-directory'; Action = 'kept-foreign'; Reason = 'not-a-shell-link'; Changes = { '' }; Arrange = {
                $foreignDirectory = Join-Path $installerCase.Desktop $installerShortcutName
                New-Item -ItemType Directory -Path $foreignDirectory | Out-Null
                [System.IO.File]::WriteAllText((Join-Path $foreignDirectory 'inside.txt'), 'a folder that shares the shortcut name', $installerUtf8)
            } }
    )
    foreach ($shortcutCase in $installerShortcutCases) {
        $installerCase = New-CountdownInstallerTestCase -Name $shortcutCase.Name
        Initialize-CountdownInstallerTestInstalledVersion
        [System.IO.File]::WriteAllText((Join-Path $installerCase.Desktop '敏敏子倒计时.exe'), 'legacy desktop launcher', $installerUtf8)
        & $shortcutCase.Arrange
        $desktopBefore = Get-CountdownInstallerTestTree -Root $installerCase.Desktop -Detailed
        $restOfDesktopBefore = Get-CountdownInstallerTestTree -Root $installerCase.Desktop -Exclude @($installerShortcutName) -Detailed
        $shortcutResult = Invoke-CountdownInstallerTest
        Assert-Equal $true ([bool] $shortcutResult.succeeded) "Install succeeds whatever shares the shortcut name: $($shortcutCase.Name) $($shortcutResult.failedStage) $($shortcutResult.error)"
        $reportedChanges = (@($shortcutResult.desktopShortcut.changes) | ForEach-Object { "$($_.item)=$($_.before)->$($_.after)" }) -join '|'
        Assert-Equal "$installerShortcutName|$($shortcutCase.Action)|$($shortcutCase.Reason)|$(& $shortcutCase.Changes)" "$($shortcutResult.desktopShortcut.name)|$($shortcutResult.desktopShortcut.action)|$($shortcutResult.desktopShortcut.reason)|$reportedChanges" "Result JSON reports what the install did with the desktop shortcut: $($shortcutCase.Name)"
        Assert-Equal $restOfDesktopBefore (Get-CountdownInstallerTestTree -Root $installerCase.Desktop -Exclude @($installerShortcutName) -Detailed) "Install leaves the rest of the desktop, the legacy launcher included, as it was: $($shortcutCase.Name)"
        Assert-Equal $false ($installerCase.Json.Contains('other-app.exe', [System.StringComparison]::OrdinalIgnoreCase)) "Result JSON does not echo where a foreign shortcut points: $($shortcutCase.Name)"
        if ($shortcutCase.Action -eq 'updated') {
            Assert-Equal "$($installerCase.ExePath)||$(Join-Path $installerCase.Root 'app')|$($installerCase.ExePath),0" (Read-CountdownInstallerTestShortcut -Path (Join-Path $installerCase.Desktop $installerShortcutName)) "Install points its own stale shortcut back at the expected definition: $($shortcutCase.Name)"
        }
        else {
            Assert-Equal $desktopBefore (Get-CountdownInstallerTestTree -Root $installerCase.Desktop -Detailed) "Install leaves the same-named desktop item byte for byte as it was: $($shortcutCase.Name)"
        }
    }

    # #282 换程序前的设置保护：写下事务标记后，把设置及其两代备份快照到事务目录 install-transaction\settings-snapshot\，
    # 用候选 EXE 的无界面迁移在事务目录里的副本上演练，再用产品的等价判定核对快照与迁移结果。迁移等价时安装继续；
    # 安装器从不改写现役设置：假迁移改写交给它的文件，现役设置与两代备份在新版本启动时与提交后都与安装前逐字节相同。
    $installerCase = New-CountdownInstallerTestCase -Name 'rehearsal-equivalent'
    Initialize-CountdownInstallerTestInstalledVersion
    $installerCase.Rehearsal.Migration = 'migrated'
    $familyBefore = Get-CountdownInstallerTestSettingsFamily -Directory $installerCase.Root
    $userDataBefore = Get-CountdownInstallerTestTree -Root $installerCase.Root -Exclude $installerOwnEntries -Detailed
    $installerCase.OnStartTask = {
        $installerCase.Observed.Snapshot = Get-CountdownInstallerTestSettingsFamily -Directory (Join-Path $installerCase.Root 'install-transaction\settings-snapshot')
        $installerCase.Observed.UserData = Get-CountdownInstallerTestTree -Root $installerCase.Root -Exclude $installerOwnEntries -Detailed
    }
    $rehearsed = Invoke-CountdownInstallerTest
    Assert-Equal $true ([bool] $rehearsed.succeeded) "Install continues when the rehearsed migration is equivalent: $($rehearsed.failedStage) $($rehearsed.error)"
    Assert-Equal 'equivalent|migrated|migrated' "$($rehearsed.settingsRehearsal.outcome)|$($rehearsed.settingsRehearsal.migration)|$($rehearsed.settingsRehearsal.equivalence)" 'Result JSON names the rehearsal conclusion and both verdicts'
    Assert-Equal $familyBefore ([string] $installerCase.Observed.Snapshot) 'The transaction directory holds a snapshot of the settings and both backups'
    Assert-Equal (Join-Path $installerCase.Root 'app.candidate\MinminCountdown.exe') ((@($installerCase.Commands | ForEach-Object ExecutablePath | Sort-Object -Unique)) -join ',') 'The rehearsal runs the candidate EXE'
    Assert-Equal $userDataBefore ([string] $installerCase.Observed.UserData) 'Live settings and both backups are untouched when the new version starts'
    Assert-Equal $userDataBefore (Get-CountdownInstallerTestTree -Root $installerCase.Root -Exclude $installerOwnEntries -Detailed) 'The installer never writes the live settings or their backups'
    Assert-Equal $false (Test-Path -LiteralPath (Join-Path $installerCase.Root 'install-transaction')) 'Commit removes the transaction directory'
    Assert-Equal $false ($installerCase.Json.Contains('settings-content-sentinel-280', [System.StringComparison]::Ordinal)) 'Result JSON with a rehearsal leaves out settings content'

    # 迁移不等价，或演练没能完成（迁移命令报告设置损坏）时安装失败：清除事务标记、删除候选与事务目录、经登录任务重新启动
    # 旧版本并报告。程序目录、现役设置及两代备份与安装前逐字节相同，安装根不留事务标记、候选或事务目录，登录任务照旧。
    foreach ($refusedRehearsal in @(
            [pscustomobject]@{ Name = 'rehearsal-not-equivalent'; Migration = 'migrated'; Equivalence = 'data-mismatch'; Reason = 'settings-not-equivalent'; Report = 'not-equivalent|migrated|data-mismatch' }
            [pscustomobject]@{ Name = 'rehearsal-migration-failed'; Migration = 'invalid-settings'; Equivalence = 'migrated'; Reason = 'settings-rehearsal-failed'; Report = 'failed|invalid-settings|' }
        )) {
        $installerCase = New-CountdownInstallerTestCase -Name $refusedRehearsal.Name
        Initialize-CountdownInstallerTestInstalledVersion
        $installerCase.Rehearsal.Migration = $refusedRehearsal.Migration
        $installerCase.Rehearsal.Equivalence = $refusedRehearsal.Equivalence
        $programBefore = Get-CountdownInstallerTestTree -Root (Join-Path $installerCase.Root 'app') -Detailed
        $userDataBefore = Get-CountdownInstallerTestTree -Root $installerCase.Root -Exclude $installerOwnEntries -Detailed
        $tasksBefore = Get-CountdownInstallerTestTasks
        $refused = Invoke-CountdownInstallerTest
        Assert-Equal "False|settings-rehearsal|$($refusedRehearsal.Reason)" "$($refused.succeeded)|$($refused.failedStage)|$($refused.reason)" "Install fails when the settings rehearsal does not pass: $($refusedRehearsal.Name)"
        Assert-Equal $refusedRehearsal.Report "$($refused.settingsRehearsal.outcome)|$($refused.settingsRehearsal.migration)|$($refused.settingsRehearsal.equivalence)" "Result JSON names the rehearsal conclusion: $($refusedRehearsal.Name)"
        Assert-Equal $programBefore (Get-CountdownInstallerTestTree -Root (Join-Path $installerCase.Root 'app') -Detailed) "Program directory is byte-for-byte as before the install: $($refusedRehearsal.Name)"
        Assert-Equal $userDataBefore (Get-CountdownInstallerTestTree -Root $installerCase.Root -Exclude $installerOwnEntries -Detailed) "Live settings and both backups are byte-for-byte as before the install: $($refusedRehearsal.Name)"
        Assert-Equal 'app,runtime-status.json' ((@($installerOwnEntries | Where-Object { Test-Path -LiteralPath (Join-Path $installerCase.Root $_) })) -join ',') "No transaction marker, candidate or transaction directory is left behind: $($refusedRehearsal.Name)"
        Assert-Equal '4103,6001' (Get-CountdownInstallerTestRunning) "The previous version runs again: $($refusedRehearsal.Name)"
        Assert-Equal '6001|3' "$($refused.restartedProcess.processId)|$($refused.restartedProcess.sessionId)" "Result JSON reports the restarted previous version: $($refusedRehearsal.Name)"
        Assert-Equal $tasksBefore (Get-CountdownInstallerTestTasks) "The login task is left as it was: $($refusedRehearsal.Name)"
        Assert-Equal $false ($installerCase.Json.Contains('settings-content-sentinel-280', [System.StringComparison]::Ordinal)) "Result JSON of a refused rehearsal leaves out settings content: $($refusedRehearsal.Name)"
    }

    # 首次安装没有设置文件：跳过演练，安装继续，候选 EXE 一个命令都不运行。
    $installerCase = New-CountdownInstallerTestCase -Name 'rehearsal-first-install'
    $firstRehearsal = Invoke-CountdownInstallerTest
    Assert-Equal $true ([bool] $firstRehearsal.succeeded) "First install without a settings file continues: $($firstRehearsal.failedStage) $($firstRehearsal.error)"
    Assert-Equal 'skipped-no-settings||' "$($firstRehearsal.settingsRehearsal.outcome)|$($firstRehearsal.settingsRehearsal.migration)|$($firstRehearsal.settingsRehearsal.equivalence)" 'Result JSON reports the rehearsal skipped without a settings file'
    Assert-Equal 0 $installerCase.Commands.Count 'First install without a settings file runs no candidate command'

    # 前置检查失败：包内文件与载荷清单不符（文件被改、文件缺少）、没有交互会话、有多个交互会话。都在停止旧应用之前拒绝：
    # 除删除候选外安装根没有任何改动（首次安装时连安装根也不留下），应用照常运行，登录任务照旧，也不创建登录任务。
    # “只检查、不改动”同样核对包。上次安装没走完时的中断恢复（#284）见后面的 recover-* 各例。
    $installerRejections = @(
        [pscustomobject]@{ Name = 'reject-tampered-file'; Installed = $true; CheckOnly = $false; Stage = 'preflight'; Reason = 'payload-mismatch'; Arrange = { [System.IO.File]::WriteAllText((Join-Path $installerCase.PackageRoot 'app\MinminCountdown.exe'), 'installer exe v2 tampered', $installerUtf8) } }
        [pscustomobject]@{ Name = 'reject-missing-file'; Installed = $true; CheckOnly = $false; Stage = 'preflight'; Reason = 'payload-mismatch'; Arrange = { [System.IO.File]::Delete((Join-Path $installerCase.PackageRoot 'app\holiday-data\2027.json')) } }
        [pscustomobject]@{ Name = 'reject-no-session'; Installed = $true; CheckOnly = $false; Stage = 'preflight'; Reason = 'no-interactive-session'; Arrange = { $installerCase.Sessions = @() } }
        [pscustomobject]@{ Name = 'reject-two-sessions'; Installed = $true; CheckOnly = $false; Stage = 'preflight'; Reason = 'multiple-interactive-sessions'; Arrange = { $installerCase.Sessions = @($installerCase.Sessions) + @([pscustomobject]@{ SessionId = 5; User = 'MINMIN-OFFICE\second-account'; Sid = $installerOtherSid }) } }
        [pscustomobject]@{ Name = 'reject-no-desktop'; Installed = $true; CheckOnly = $false; Stage = 'preflight'; Reason = 'desktop-unavailable'; Arrange = { $installerCase.Desktop = $null } }
        [pscustomobject]@{ Name = 'reject-first-install-tampered'; Installed = $false; CheckOnly = $false; Stage = 'preflight'; Reason = 'payload-mismatch'; Arrange = { [System.IO.File]::WriteAllText((Join-Path $installerCase.PackageRoot 'app\MinminCountdown.dll'), 'installer dll v2 tampered', $installerUtf8) } }
        [pscustomobject]@{ Name = 'reject-check-only-tampered'; Installed = $true; CheckOnly = $true; Stage = 'preflight'; Reason = 'payload-mismatch'; Arrange = { [System.IO.File]::WriteAllText((Join-Path $installerCase.PackageRoot 'app\MinminCountdown.exe'), 'installer exe v2 tampered', $installerUtf8) } }
    )
    foreach ($rejection in $installerRejections) {
        $installerCase = New-CountdownInstallerTestCase -Name $rejection.Name
        if ($rejection.Installed) { Initialize-CountdownInstallerTestInstalledVersion }
        & $rejection.Arrange
        $treeBefore = Get-CountdownInstallerTestTree -Root $installerCase.Root -Detailed
        $rejectionDesktop = Join-Path (Split-Path -Parent $installerCase.Root) 'desktop'
        $desktopTreeBefore = Get-CountdownInstallerTestTree -Root $rejectionDesktop -Detailed
        $runningBefore = Get-CountdownInstallerTestRunning
        $tasksBefore = Get-CountdownInstallerTestTasks
        $rejected = Invoke-CountdownInstallerTest -CheckOnly:$rejection.CheckOnly
        Assert-Equal "False|$($rejection.Stage)|$($rejection.Reason)" "$($rejected.succeeded)|$($rejected.failedStage)|$($rejected.reason)" "The install is rejected: $($rejection.Name)"
        Assert-Equal $treeBefore (Get-CountdownInstallerTestTree -Root $installerCase.Root -Detailed) "Rejected install leaves the install root as it was: $($rejection.Name)"
        Assert-Equal $desktopTreeBefore (Get-CountdownInstallerTestTree -Root $rejectionDesktop -Detailed) "Rejected install leaves the desktop as it was: $($rejection.Name)"
        Assert-Equal $runningBefore (Get-CountdownInstallerTestRunning) "Rejected install stops no application: $($rejection.Name)"
        Assert-Equal $tasksBefore (Get-CountdownInstallerTestTasks) "Rejected install leaves the login tasks alone: $($rejection.Name)"
        if ($rejection.Installed) {
            Assert-Equal $installerSettingsFingerprint (Get-CountdownInstallerTestFingerprint -Fingerprint $rejected.settings.after) "Rejected install reports the settings fingerprint after it stopped: $($rejection.Name)"
        }
    }

    # 只检查、不改动：执行到前置检查为止（核对包本身，不放候选），报告将要执行的步骤；包、安装根、桌面、应用与登录任务都不变。
    # 已有登录任务与期望定义一致时不计划动它，不一致时计划更新；首次安装时计划创建，安装根也不会被建出来。设置快照总在计划里，
    # 迁移演练只在有设置文件时。只检查不读桌面上的快捷方式，计划里总有登记快捷方式这一步（到时缺失则创建、是安装器的则更新、
    # 不是则保留）。
    foreach ($checkOnly in @(
            [pscustomobject]@{ Name = 'check-only-upgrade'; Installed = $true; Arrange = $null; Steps = 'place-candidate,stop-application,write-transaction-marker,snapshot-settings,settings-rehearsal,replace-program-directory,register-desktop-shortcut,start-and-wait-ready,commit' }
            [pscustomobject]@{ Name = 'check-only-task-mismatch'; Installed = $true; Arrange = { $installerCase.Tasks[$installerCase.TaskName].Priority = 7 }; Steps = 'place-candidate,stop-application,write-transaction-marker,snapshot-settings,settings-rehearsal,replace-program-directory,update-login-task,register-desktop-shortcut,start-and-wait-ready,commit' }
            [pscustomobject]@{ Name = 'check-only-first-install'; Installed = $false; Arrange = $null; Steps = 'place-candidate,stop-application,write-transaction-marker,snapshot-settings,replace-program-directory,create-login-task,register-desktop-shortcut,start-and-wait-ready,commit' }
        )) {
        $installerCase = New-CountdownInstallerTestCase -Name $checkOnly.Name
        if ($checkOnly.Installed) { Initialize-CountdownInstallerTestInstalledVersion }
        if ($null -ne $checkOnly.Arrange) { & $checkOnly.Arrange }
        $taskDefinitionBefore = Get-CountdownInstallerTestTaskDefinition -Task $installerCase.Tasks[$installerCase.TaskName]
        $caseRoot = Split-Path -Parent $installerCase.Root
        $caseTreeBefore = Get-CountdownInstallerTestTree -Root $caseRoot -Detailed
        $runningBefore = Get-CountdownInstallerTestRunning
        $tasksBefore = Get-CountdownInstallerTestTasks
        $checked = Invoke-CountdownInstallerTest -CheckOnly
        Assert-Equal 'True|True|' "$($checked.succeeded)|$($checked.checkOnly)|$($checked.failedStage)" "Check-only passes the preflight: $($checkOnly.Name)"
        Assert-Equal $checkOnly.Steps (@($checked.plannedSteps) -join ',') "Check-only reports the steps it would take: $($checkOnly.Name)"
        Assert-Equal "$installerCommitV2|$installerAggregateV2" "$($checked.package.commit)|$($checked.package.aggregateSha256)" "Check-only names the package it checked: $($checkOnly.Name)"
        Assert-Equal $caseTreeBefore (Get-CountdownInstallerTestTree -Root $caseRoot -Detailed) "Check-only changes nothing in the package or the install root: $($checkOnly.Name)"
        Assert-Equal $runningBefore (Get-CountdownInstallerTestRunning) "Check-only stops no application: $($checkOnly.Name)"
        Assert-Equal $tasksBefore (Get-CountdownInstallerTestTasks) "Check-only creates or changes no login task: $($checkOnly.Name)"
        Assert-Equal $taskDefinitionBefore (Get-CountdownInstallerTestTaskDefinition -Task $installerCase.Tasks[$installerCase.TaskName]) "Check-only leaves every login task item as it was: $($checkOnly.Name)"
    }

    # 另一个安装正在进行（持有全机安装锁）时，第二个安装被拒绝：在第一个安装启动新版本的那一刻从另一个线程发起，
    # 它在安装锁处停下、不调用任何 port（它的 port 一被调用就失败），结果里仍有设置指纹；第一个安装照常提交。
    $installerCase = New-CountdownInstallerTestCase -Name 'concurrent'
    Initialize-CountdownInstallerTestInstalledVersion
    $installerCase.OnStartTask = {
        $secondInstall = Start-ThreadJob -ArgumentList $installerScript, $installerCase.PackageRoot, $installerCase.Root, $installerCase.TaskName, $installerLockName -ScriptBlock {
            param($Script, $PackageRoot, $InstallRoot, $TaskName, $LockName)
            $refuse = { throw '第二个安装越过了安装锁。' }
            $ports = [pscustomobject]@{ GetInteractiveSessions = $refuse; GetProcesses = $refuse; StopProcess = $refuse; GetTask = $refuse; RegisterTask = $refuse; StartTask = $refuse; ExportTask = $refuse; RestoreTask = $refuse; MoveDirectory = $refuse; GetDesktopDirectory = $refuse; ReadShortcut = $refuse; WriteShortcut = $refuse; RemoveShortcut = $refuse; RunHeadlessCommand = $refuse }
            & $Script -PackageRoot $PackageRoot -InstallRoot $InstallRoot -TaskName $TaskName -Port $ports -LockName $LockName
        }
        $installerCase.Observed.SecondInstall = @(Receive-CountdownTestThreadJob -Job $secondInstall)
    }
    $firstOfTwo = Invoke-CountdownInstallerTest
    Assert-Equal 1 @($installerCase.Observed.SecondInstall).Count 'The second install writes exactly one JSON document'
    $secondOfTwo = [string] @($installerCase.Observed.SecondInstall)[-1] | ConvertFrom-Json -DateKind String
    Assert-Equal 'False|lock|install-in-progress' "$($secondOfTwo.succeeded)|$($secondOfTwo.failedStage)|$($secondOfTwo.reason)" 'A second install is rejected while another install is in progress'
    Assert-Equal $installerSettingsFingerprint (Get-CountdownInstallerTestFingerprint -Fingerprint $secondOfTwo.settings.before) 'The rejected second install still reports the settings fingerprint'
    Assert-Equal $true ([bool] $firstOfTwo.succeeded) "The install holding the lock commits: $($firstOfTwo.failedStage) $($firstOfTwo.error)"
    Assert-Equal $false (Test-Path -LiteralPath (Join-Path $installerCase.Root 'install-transaction.json')) 'The install holding the lock leaves no transaction marker'

    # 下面几例经导出的 module 函数把停止、启动就绪与文件占用重试的时限缩短到 1 秒，都在已装好的 v1 上升级。
    # 启动就绪只认：回执由一个存活进程写出，其映像路径是已安装 EXE，会话是交互会话，EXE 哈希与载荷清单一致。每例只违反一条，
    # 等待时限内达不到启动就绪就不提交：失败在启动阶段，回滚（#283）停止新版本、换回旧版本并重新启动它，不留事务痕迹。
    # 新版本在启动就绪之前就退出时（登录任务的那次运行已经结束，已安装 EXE 也没有进程在运行）不再等，原因是 startup-exited，
    # 结果带它的退出码（#288）；其余各例没有退出码。
    # 登录任务有运行中的实例时，经它启动会被 IgnoreNew 悄悄拒绝（#279）：应用进程都结束后实例仍在（例如任务的动作不是
    # 已安装 EXE），就在写事务标记之前停下，候选删除，程序目录不变，不需要回滚；替换程序目录期间任务又运行起来时，不再触发它，
    # 回滚把程序目录换回，却同样没法经仍在运行的任务重新启动旧版本：回滚如实报告失败，事务标记与候选留给中断恢复。
    Import-Module (Join-Path $PSScriptRoot 'Countdown.Installer.psm1') -Force
    $installerShortWaits = @(
        [pscustomobject]@{ Name = 'not-ready-no-receipt'; Stage = 'start-and-wait-ready'; Reason = 'startup-not-ready'; ExitCode = ''; Rollback = 'rolled-back'; Running = '4103,6002'; Arrange = { $installerCase.Launch.WritesReceipt = $false } }
        [pscustomobject]@{ Name = 'exited-before-ready'; Stage = 'start-and-wait-ready'; Reason = 'startup-exited'; ExitCode = '1'; Rollback = 'rolled-back'; Running = '4103,6002'; Arrange = {
                $installerCase.Launch.StaysAlive = $false
                $installerCase.Launch.ExitCode = 1
            } }
        [pscustomobject]@{ Name = 'not-ready-other-session'; Stage = 'start-and-wait-ready'; Reason = 'startup-not-ready'; ExitCode = ''; Rollback = 'rolled-back'; Running = '4103,6002'; Arrange = { $installerCase.Launch.SessionId = 4 } }
        [pscustomobject]@{ Name = 'not-ready-exe-changed'; Stage = 'start-and-wait-ready'; Reason = 'startup-not-ready'; ExitCode = ''; Rollback = 'rolled-back'; Running = '4103,6002'; Arrange = {
                $installerCase.OnStartTask = { param([string] $ExePath) if (Test-CountdownInstallerTestNewVersion -ExecutablePath $ExePath) { [System.IO.File]::WriteAllText($ExePath, 'installer exe v2 changed after placement', $installerUtf8) } }
            } }
        [pscustomobject]@{ Name = 'task-instance-outlives-app'; Stage = 'stop-application'; Reason = 'login-task-still-running'; ExitCode = ''; Rollback = 'not-needed'; Running = '4103'; Arrange = { $installerCase.Processes[0].StartedByTask = $null } }
        [pscustomobject]@{ Name = 'task-runs-during-replace'; Stage = 'start-and-wait-ready'; Reason = 'login-task-still-running'; ExitCode = ''; Rollback = 'rollback-failed'; Running = '4103'; Arrange = { $installerCase.OnMoveDirectory = { $installerCase.Tasks[$installerCase.TaskName].State = 'Running' } } }
    )
    foreach ($shortWait in $installerShortWaits) {
        $installerCase = New-CountdownInstallerTestCase -Name $shortWait.Name
        Initialize-CountdownInstallerTestInstalledVersion
        & $shortWait.Arrange
        $shortWaitResult = Install-CountdownProgram -PackageRoot $installerCase.PackageRoot -InstallRoot $installerCase.Root -TaskName $installerCase.TaskName -Ports $installerPorts -LockName $installerLockName -ReadyTimeoutSeconds 1 -StopTimeoutSeconds 1 -FileLockRetrySeconds 1 |
            ConvertTo-Json -Depth 6 | ConvertFrom-Json -DateKind String
        Assert-Equal "False|$($shortWait.Stage)|$($shortWait.Reason)" "$($shortWaitResult.succeeded)|$($shortWaitResult.failedStage)|$($shortWaitResult.reason)" "Install does not commit: $($shortWait.Name) $($shortWaitResult.error)"
        Assert-Equal $shortWait.ExitCode ([string] $shortWaitResult.startupExitCode) "Result JSON carries an exit code only when the new version exited before startup readiness: $($shortWait.Name)"
        Assert-Equal $shortWait.Rollback ([string] $shortWaitResult.rollback.outcome) "Result JSON reports the rollback outcome: $($shortWait.Name) $(@($shortWaitResult.rollback.problems) -join '；')"
        Assert-Equal $shortWait.Running (Get-CountdownInstallerTestRunning) "Install starts the new version only through an idle login task, and the rollback the previous one: $($shortWait.Name)"
        Assert-Equal $installerProgramFilesV1[0].Sha256 (Get-CountdownInstallerTestInstalledDll) "Program directory is the installed version afterwards: $($shortWait.Name)"
        Assert-Equal $false (Test-Path -LiteralPath (Join-Path $installerCase.Root 'app.previous')) "No previous-version directory is left behind: $($shortWait.Name)"
        $rollbackFailed = $shortWait.Rollback -eq 'rollback-failed'
        Assert-Equal $rollbackFailed (Test-Path -LiteralPath (Join-Path $installerCase.Root 'install-transaction.json')) "Transaction marker stays only when the rollback failed: $($shortWait.Name)"
        Assert-Equal $rollbackFailed ([bool] $shortWaitResult.transactionMarkerPresent) "Result JSON says whether the transaction marker stays: $($shortWait.Name)"
        Assert-Equal $rollbackFailed (Test-Path -LiteralPath (Join-Path $installerCase.Root 'app.candidate')) "The new version is kept as the candidate only when the rollback failed: $($shortWait.Name)"
    }

    # 新版本启动即退出（#288 实测：v3 在 0.4 秒内以退出码 1 退出，安装器却等满了 30 秒的就绪时限才回滚）。就绪时限放到 60 秒：
    # 那次运行一结束就不再等，安装远在时限之前结束并回滚，旧版本重新运行；结果写明退出码，有界的错误摘要里也有它，
    # 诊断日志的位置照旧。
    $installerCase = New-CountdownInstallerTestCase -Name 'exited-at-once'
    Initialize-CountdownInstallerTestInstalledVersion
    $installerCase.Launch.StaysAlive = $false
    $installerCase.Launch.WritesReceipt = $false
    $installerCase.Launch.ExitCode = 1
    $exitedClock = [System.Diagnostics.Stopwatch]::StartNew()
    $exitedAtOnce = Install-CountdownProgram -PackageRoot $installerCase.PackageRoot -InstallRoot $installerCase.Root -TaskName $installerCase.TaskName -Ports $installerPorts -LockName $installerLockName -ReadyTimeoutSeconds 60 -StopTimeoutSeconds 1 -FileLockRetrySeconds 1 |
        ConvertTo-Json -Depth 6 | ConvertFrom-Json -DateKind String
    $exitedClock.Stop()
    Assert-Equal 'False|start-and-wait-ready|startup-exited|1' "$($exitedAtOnce.succeeded)|$($exitedAtOnce.failedStage)|$($exitedAtOnce.reason)|$($exitedAtOnce.startupExitCode)" "A new version that exits before startup readiness fails the install with its exit code: $($exitedAtOnce.error)"
    Assert-Equal $true ($exitedClock.Elapsed.TotalSeconds -lt 20) "The install does not wait out the readiness timeout once the new version has exited: $([int] $exitedClock.Elapsed.TotalSeconds) s"
    Assert-Equal 'rolled-back|6002|4103,6002' "$($exitedAtOnce.rollback.outcome)|$($exitedAtOnce.restartedProcess.processId)|$(Get-CountdownInstallerTestRunning)" "The install rolls back and the previous version runs again: $(@($exitedAtOnce.rollback.problems) -join '；')"
    Assert-Equal $true (([string] $exitedAtOnce.error).Contains('0x00000001') -and ([string] $exitedAtOnce.error).Length -le 401) "Result JSON carries a bounded error summary that names the exit code: $($exitedAtOnce.error)"
    Assert-Equal (Join-Path $installerCase.Root 'logs\application.jsonl') ([string] $exitedAtOnce.diagnosticLogPath) 'Result JSON of an early exit names where the diagnostic log is'

    # #283 失败回滚：换程序之后、新版本达到启动就绪之前任何一步失败，都停止新版本、把程序目录换回、用本次安装的快照恢复
    # 设置及其两代备份、恢复被改过的登录任务，再经登录任务启动旧版本并等到启动就绪；都做到才清除事务标记、删除候选与事务目录。
    # 下面各例经导出的 module 函数把停止、启动就绪与文件占用重试的时限缩短到 1 秒，结果按入口的方式转成 JSON 再读回。
    # 第一例：新版本起来了却一直没写出回执（启动就绪超时）。它启动时已按正常流程迁移并保存现役设置，安装前没有的 backup 2
    # 因迁移才出现；回滚后程序目录与设置及两代备份都与安装前逐字节相同（backup 2 又不在了），旧版本重新运行。
    function Invoke-CountdownInstallerModuleTest {
        $moduleResult = Install-CountdownProgram -PackageRoot $installerCase.PackageRoot -InstallRoot $installerCase.Root -TaskName $installerCase.TaskName -Ports $installerPorts -LockName $installerLockName -ReadyTimeoutSeconds 1 -StopTimeoutSeconds 1 -FileLockRetrySeconds 1
        $installerCase.Json = $moduleResult | ConvertTo-Json -Depth 6 -EscapeHandling EscapeNonAscii
        $installerCase.Json | ConvertFrom-Json -DateKind String
    }
    $installerSettingsFamilyNames = @('settings.json', 'settings.json.backup1', 'settings.json.backup2')
    $installerCase = New-CountdownInstallerTestCase -Name 'rollback-not-ready'
    Initialize-CountdownInstallerTestInstalledVersion
    [System.IO.File]::Delete((Join-Path $installerCase.Root 'settings.json.backup2'))
    $installerCase.Launch.WritesReceipt = $false
    $installerCase.OnStartTask = { param([string] $ExePath) if (Test-CountdownInstallerTestNewVersion -ExecutablePath $ExePath) { Invoke-CountdownInstallerTestStartupMigration } }
    $programBefore = Get-CountdownInstallerTestTree -Root (Join-Path $installerCase.Root 'app') -Detailed
    $familyBefore = Get-CountdownInstallerTestSettingsFamily -Directory $installerCase.Root
    $otherUserDataBefore = Get-CountdownInstallerTestTree -Root $installerCase.Root -Exclude ($installerOwnEntries + $installerSettingsFamilyNames) -Detailed
    $taskBefore = Get-CountdownInstallerTestTaskDefinition -Task $installerCase.Tasks[$installerCase.TaskName]
    $notReady = Invoke-CountdownInstallerModuleTest
    Assert-Equal 'False|start-and-wait-ready|startup-not-ready' "$($notReady.succeeded)|$($notReady.failedStage)|$($notReady.reason)" 'A new version that never reaches startup readiness fails the install'
    Assert-Equal 'rolled-back|restored||' "$($notReady.rollback.outcome)|$($notReady.rollback.settings)|$($notReady.rollback.loginTask)|$($notReady.rollback.desktopShortcut)" "Result JSON reports the rollback and that it put the settings back: $(@($notReady.rollback.problems) -join '；')"
    Assert-Equal $programBefore (Get-CountdownInstallerTestTree -Root (Join-Path $installerCase.Root 'app') -Detailed) 'Rollback puts the previous program directory back byte for byte'
    Assert-Equal $familyBefore (Get-CountdownInstallerTestSettingsFamily -Directory $installerCase.Root) 'Rollback restores the settings and both backups byte for byte, without the backup the new version added'
    Assert-Equal $otherUserDataBefore (Get-CountdownInstallerTestTree -Root $installerCase.Root -Exclude ($installerOwnEntries + $installerSettingsFamilyNames) -Detailed) 'Rollback leaves the other user data as it was'
    Assert-Equal '4103,6002' (Get-CountdownInstallerTestRunning) 'Rollback stops the new version and the previous version runs again'
    Assert-Equal '6002|3' "$($notReady.restartedProcess.processId)|$($notReady.restartedProcess.sessionId)" 'Result JSON reports the restarted previous version'
    Assert-Equal 'app,runtime-status.json' ((@($installerOwnEntries | Where-Object { Test-Path -LiteralPath (Join-Path $installerCase.Root $_) })) -join ',') 'Rollback leaves no transaction marker, candidate, previous-version or transaction directory'
    Assert-Equal $false ([bool] $notReady.transactionMarkerPresent) 'Result JSON reports no transaction marker after a complete rollback'
    Assert-Equal $taskBefore (Get-CountdownInstallerTestTaskDefinition -Task $installerCase.Tasks[$installerCase.TaskName]) 'Rollback leaves a login task this install did not change as it was'
    Assert-Equal (Join-Path $installerCase.Root 'logs\application.jsonl') ([string] $notReady.diagnosticLogPath) 'Result JSON names where the diagnostic log is'
    Assert-Equal $true (([string] $notReady.error).Length -gt 0 -and ([string] $notReady.error).Length -le 401) 'Result JSON carries a bounded error summary'
    foreach ($secret in @('settings-content-sentinel-280', 'migrated by the new version', 'installer-account-sentinel', 'MINMIN-OFFICE')) {
        Assert-Equal $false ($installerCase.Json.Contains($secret, [System.StringComparison]::OrdinalIgnoreCase)) "Result JSON of a rollback leaves out settings content and the account: $secret"
    }
    $notReadyStatus = Invoke-CountdownInstallerStatusRead
    Assert-Equal "$installerCommitV1|True|False" "$($notReadyStatus.commit)|$($notReadyStatus.running)|$($notReadyStatus.transactionMarkerPresent)" 'Status reads the previous version running and no unfinished install after the rollback'

    # 程序目录被占用、无法改名（#283 验收 2）：现役 app 改不成 app.previous，或改完之后候选改不成 app。占用持续过重试时限就
    # 回滚：不留半替换状态（现役目录是安装前的旧版本，没有旧版本目录、候选、事务标记或事务目录），旧版本重新运行，设置没被动过。
    foreach ($lockedRename in @(
            [pscustomobject]@{ Name = 'rollback-current-locked'; From = 'app'; To = 'app.previous' }
            [pscustomobject]@{ Name = 'rollback-candidate-locked'; From = 'app.candidate'; To = 'app' }
        )) {
        $installerCase = New-CountdownInstallerTestCase -Name $lockedRename.Name
        Initialize-CountdownInstallerTestInstalledVersion
        $installerCase.MoveLocks = @([pscustomobject]@{ From = $lockedRename.From; To = $lockedRename.To; Remaining = -1 })
        $programBefore = Get-CountdownInstallerTestTree -Root (Join-Path $installerCase.Root 'app') -Detailed
        $userDataBefore = Get-CountdownInstallerTestTree -Root $installerCase.Root -Exclude $installerOwnEntries -Detailed
        $tasksBefore = Get-CountdownInstallerTestTasks
        $locked = Invoke-CountdownInstallerModuleTest
        Assert-Equal 'False|replace-program-directory|program-directory-in-use' "$($locked.succeeded)|$($locked.failedStage)|$($locked.reason)" "A program directory that stays in use fails the install: $($lockedRename.Name) $($locked.error)"
        Assert-Equal 'rolled-back' ([string] $locked.rollback.outcome) "Result JSON reports the rollback: $($lockedRename.Name) $(@($locked.rollback.problems) -join '；')"
        Assert-Equal $programBefore (Get-CountdownInstallerTestTree -Root (Join-Path $installerCase.Root 'app') -Detailed) "The program directory is the previous version, not half replaced: $($lockedRename.Name)"
        Assert-Equal 'app,runtime-status.json' ((@($installerOwnEntries | Where-Object { Test-Path -LiteralPath (Join-Path $installerCase.Root $_) })) -join ',') "No previous-version directory, candidate, marker or transaction directory is left: $($lockedRename.Name)"
        Assert-Equal $userDataBefore (Get-CountdownInstallerTestTree -Root $installerCase.Root -Exclude $installerOwnEntries -Detailed) "Settings and the other user data are untouched: $($lockedRename.Name)"
        Assert-Equal '4103,6001' (Get-CountdownInstallerTestRunning) "The previous version runs again: $($lockedRename.Name)"
        Assert-Equal $tasksBefore (Get-CountdownInstallerTestTasks) "The login task is left as it was: $($lockedRename.Name)"
    }

    # 登录任务在本次安装里被改过（#283 验收 3）：安装前的任务与期望定义不一致（优先级、运行时限、两项电池设置），安装按期望
    # 定义重新注册它；新版本起不来时回滚把任务恢复成安装前的整份定义（连注册时的用户名都与之前相同），旧版本经它重新运行。
    # 安装前没有登录任务、由本次升级创建时，回滚保留它：它就是期望定义、指向同一个 EXE 路径，旧版本要经它重新启动。
    foreach ($taskRollback in @(
            [pscustomobject]@{ Name = 'rollback-login-task-restored'; TaskAction = 'updated'; Rollback = 'restored'; Arrange = {
                    $staleTask = $installerCase.Tasks[$installerCase.TaskName]
                    $staleTask.State = 'Ready'
                    $staleTask.Priority = 7
                    $staleTask.ExecutionTimeLimit = 'PT72H'
                    $staleTask.DisallowStartIfOnBatteries = $true
                    $staleTask.StopIfGoingOnBatteries = $true
                    $installerCase.Processes[0].StartedByTask = $null
                } }
            [pscustomobject]@{ Name = 'rollback-login-task-created-kept'; TaskAction = 'created'; Rollback = 'kept'; Arrange = {
                    [void] $installerCase.Tasks.Remove($installerCase.TaskName)
                    $installerCase.Processes[0].StartedByTask = $null
                } }
        )) {
        $installerCase = New-CountdownInstallerTestCase -Name $taskRollback.Name
        Initialize-CountdownInstallerTestInstalledVersion
        & $taskRollback.Arrange
        $installerCase.Launch.WritesReceipt = $false
        $taskDefinitionBefore = Get-CountdownInstallerTestTaskDefinition -Task $installerCase.Tasks[$installerCase.TaskName]
        $taskRolledBack = Invoke-CountdownInstallerModuleTest
        Assert-Equal "False|start-and-wait-ready|$($taskRollback.TaskAction)" "$($taskRolledBack.succeeded)|$($taskRolledBack.failedStage)|$($taskRolledBack.loginTask.action)" "The install changed the login task before the new version failed: $($taskRollback.Name) $($taskRolledBack.error)"
        Assert-Equal "rolled-back|$($taskRollback.Rollback)" "$($taskRolledBack.rollback.outcome)|$($taskRolledBack.rollback.loginTask)" "Result JSON reports what the rollback did with the login task: $($taskRollback.Name) $(@($taskRolledBack.rollback.problems) -join '；')"
        if ($taskRollback.Rollback -eq 'restored') {
            Assert-Equal $taskDefinitionBefore (Get-CountdownInstallerTestTaskDefinition -Task $installerCase.Tasks[$installerCase.TaskName]) 'Rollback restores every item of the login task as it was before the install'
        }
        else {
            Assert-Equal $true ([bool] (Invoke-CountdownInstallerStatusRead).loginTask.matchesExpected) 'Rollback keeps the login task this upgrade created, with the expected definition'
        }
        Assert-Equal '4103,6002' (Get-CountdownInstallerTestRunning) "The previous version runs again through the login task: $($taskRollback.Name)"
        Assert-Equal $installerProgramFilesV1[0].Sha256 (Get-CountdownInstallerTestInstalledDll) "The previous version is back in the program directory: $($taskRollback.Name)"
    }

    # 回滚自身失败（#283 验收 5）：结果如实写 rollback-failed 并列出没做到的那步，事务标记与事务目录（含设置快照）留下，交给
    # 中断恢复（#284）；状态命令读得到未完成的安装。第一例新版本起不来，回滚时现役目录一直被占用、改不回 app.candidate：程序目录
    # 仍是新版本、旧版本仍在 app.previous，设置不恢复（新版本可能还在用这些文件）。第二例程序目录与设置都换回了，旧版本却一直
    # 没达到启动就绪：同样不算回滚完成，事务标记与候选留下。
    foreach ($failedRollback in @(
            [pscustomobject]@{ Name = 'rollback-fails-renaming-back'; Problem = '换回程序目录失败'; ProgramDll = $installerPackageFilesV2[1].Sha256; Previous = $true; Candidate = $false; Running = '4103'; Settings = 'migrated'; Arrange = {
                    $installerCase.MoveLocks = @([pscustomobject]@{ From = 'app'; To = 'app.candidate'; Remaining = -1 })
                } }
            [pscustomobject]@{ Name = 'rollback-fails-restarting'; Problem = '重新启动旧版本失败'; ProgramDll = $installerProgramFilesV1[0].Sha256; Previous = $false; Candidate = $true; Running = '4103,6002'; Settings = 'restored'; Arrange = {
                    $installerCase.PreviousLaunch.WritesReceipt = $false
                } }
        )) {
        $installerCase = New-CountdownInstallerTestCase -Name $failedRollback.Name
        Initialize-CountdownInstallerTestInstalledVersion
        $installerCase.Launch.WritesReceipt = $false
        $installerCase.OnStartTask = { param([string] $ExePath) if (Test-CountdownInstallerTestNewVersion -ExecutablePath $ExePath) { Invoke-CountdownInstallerTestStartupMigration } }
        & $failedRollback.Arrange
        $familyBefore = Get-CountdownInstallerTestSettingsFamily -Directory $installerCase.Root
        $rollbackFailed = Invoke-CountdownInstallerModuleTest
        $problems = @($rollbackFailed.rollback.problems) -join '；'
        Assert-Equal 'False|start-and-wait-ready|rollback-failed' "$($rollbackFailed.succeeded)|$($rollbackFailed.failedStage)|$($rollbackFailed.rollback.outcome)" "Result JSON reports that the rollback itself failed: $($failedRollback.Name) $problems"
        Assert-Equal $true ($problems.StartsWith($failedRollback.Problem, [System.StringComparison]::Ordinal)) "Result JSON names the rollback step that failed: $($failedRollback.Name) $problems"
        Assert-Equal $true (@($rollbackFailed.rollback.problems | Where-Object { ([string] $_).Length -gt 401 }).Count -eq 0) "Rollback problems are bounded: $($failedRollback.Name)"
        Assert-Equal "True|True|True" "$($rollbackFailed.transactionMarkerPresent)|$(Test-Path -LiteralPath (Join-Path $installerCase.Root 'install-transaction.json'))|$(Test-Path -LiteralPath (Join-Path $installerCase.Root 'install-transaction\settings-snapshot\settings.json'))" "The transaction marker and the settings snapshot stay for the interruption recovery: $($failedRollback.Name)"
        Assert-Equal $failedRollback.ProgramDll (Get-CountdownInstallerTestInstalledDll) "Result matches the program directory left behind: $($failedRollback.Name)"
        Assert-Equal "$($failedRollback.Previous)|$($failedRollback.Candidate)" "$(Test-Path -LiteralPath (Join-Path $installerCase.Root 'app.previous'))|$(Test-Path -LiteralPath (Join-Path $installerCase.Root 'app.candidate'))" "The other program directory stays where recovery expects it: $($failedRollback.Name)"
        Assert-Equal $failedRollback.Running (Get-CountdownInstallerTestRunning) "The new version is stopped: $($failedRollback.Name)"
        if ($failedRollback.Settings -eq 'restored') {
            Assert-Equal $familyBefore (Get-CountdownInstallerTestSettingsFamily -Directory $installerCase.Root) "Settings were put back before the failing step: $($failedRollback.Name)"
        }
        else {
            Assert-Equal $true ((Get-CountdownInstallerTestSettingsFamily -Directory $installerCase.Root) -ne $familyBefore) "Settings are not restored while the program directory could not be put back: $($failedRollback.Name)"
        }
        Assert-Equal $true ([bool] (Invoke-CountdownInstallerStatusRead).transactionMarkerPresent) "Status reads the unfinished install after a failed rollback: $($failedRollback.Name)"
        Assert-Equal $false ($installerCase.Json.Contains('migrated by the new version', [System.StringComparison]::Ordinal)) "Result JSON of a failed rollback leaves out settings content: $($failedRollback.Name)"
    }

    # 新版本达到启动就绪之后（#283 验收 6）：提交时清除不了事务标记（被别的句柄占着），安装失败在提交阶段，但不再回滚，也不再
    # 用快照覆盖设置：新版本启动时迁移并保存的设置（以及用户随后的编辑）原样保留，新版本继续运行，事务标记留给下一次安装的
    # 中断恢复。
    $installerCase = New-CountdownInstallerTestCase -Name 'commit-fails-after-ready'
    Initialize-CountdownInstallerTestInstalledVersion
    $familyBefore = Get-CountdownInstallerTestSettingsFamily -Directory $installerCase.Root
    $installerCase.OnStartTask = {
        param([string] $ExePath)
        if (Test-CountdownInstallerTestNewVersion -ExecutablePath $ExePath) {
            Invoke-CountdownInstallerTestStartupMigration
            $installerCase.Observed.MarkerLock = [System.IO.File]::Open((Join-Path $installerCase.Root 'install-transaction.json'), 'Open', 'Read', 'Read')
        }
    }
    try { $afterReady = Invoke-CountdownInstallerModuleTest }
    finally { if ($installerCase.Observed.ContainsKey('MarkerLock')) { $installerCase.Observed.MarkerLock.Dispose() } }
    Assert-Equal 'False|commit|6001' "$($afterReady.succeeded)|$($afterReady.failedStage)|$($afterReady.readyProcess.processId)" "A commit that fails after startup readiness fails the install: $($afterReady.error)"
    Assert-Equal 'not-needed|' "$($afterReady.rollback.outcome)|$($afterReady.rollback.settings)" 'No rollback runs after the new version reached startup readiness'
    Assert-Equal $familyBefore (Get-CountdownInstallerTestSettingsFamily -Directory (Join-Path $installerCase.Root 'install-transaction\settings-snapshot')) 'The snapshot of the settings before the install is still there'
    Assert-Equal '{"schemaVersion":11,"note":"migrated by the new version at startup"}' ([System.IO.File]::ReadAllText((Join-Path $installerCase.Root 'settings.json'))) 'The installer does not overwrite the settings the new version saved with the snapshot'
    Assert-Equal '4103,6001' (Get-CountdownInstallerTestRunning) 'The new version keeps running'
    Assert-Equal "$($installerPackageFilesV2[1].Sha256)|True" "$(Get-CountdownInstallerTestInstalledDll)|$($afterReady.transactionMarkerPresent)" 'The new version stays installed and the marker stays for the interruption recovery'

    # 首次安装没有旧版本（#283 的决定）：回滚就是回到安装前，安装根里没有程序目录，本次创建的登录任务与桌面快捷方式删除，
    # 新版本启动时新建的设置文件也删除（安装前不存在）；没有旧版本可以重新启动。用户数据与日志不归安装器管，原样留下。
    $installerCase = New-CountdownInstallerTestCase -Name 'rollback-first-install'
    $installerCase.Launch.WritesReceipt = $false
    $installerCase.OnStartTask = { param([string] $ExePath) if (Test-CountdownInstallerTestNewVersion -ExecutablePath $ExePath) { Invoke-CountdownInstallerTestStartupMigration } }
    $firstRollback = Invoke-CountdownInstallerModuleTest
    Assert-Equal 'False|start-and-wait-ready|True' "$($firstRollback.succeeded)|$($firstRollback.failedStage)|$($firstRollback.firstInstall)" "A first install whose new version never gets ready fails: $($firstRollback.error)"
    Assert-Equal 'rolled-back|restored|removed|removed' "$($firstRollback.rollback.outcome)|$($firstRollback.rollback.settings)|$($firstRollback.rollback.loginTask)|$($firstRollback.rollback.desktopShortcut)" "Result JSON reports that the first install was undone: $(@($firstRollback.rollback.problems) -join '；')"
    Assert-Equal '' ((@($installerOwnEntries + $installerSettingsFamilyNames | Where-Object { Test-Path -LiteralPath (Join-Path $installerCase.Root $_) })) -join ',') 'Rollback of a first install leaves no program directory, transaction trace or settings file the new version created'
    Assert-Equal '|' "$(Get-CountdownInstallerTestTasks)|$(Get-CountdownInstallerTestRunning)" 'Rollback of a first install removes the login task it created and leaves nothing running'
    Assert-Equal '' ((@(Get-ChildItem -LiteralPath $installerCase.Desktop -Force) | ForEach-Object { $_.Name }) -join ',') 'Rollback of a first install removes the desktop shortcut it created'
    Assert-Equal $true ($null -eq $firstRollback.restartedProcess) 'Rollback of a first install has no previous version to restart'

    # 占用只是一阵子（#281 曾见迁移演练刚运行过候选 EXE 时第二次改名报拒绝访问，重跑就过）：重试时限之内放开就照常换程序并提交。
    $installerCase = New-CountdownInstallerTestCase -Name 'swap-transient-lock'
    Initialize-CountdownInstallerTestInstalledVersion
    $installerCase.MoveLocks = @([pscustomobject]@{ From = 'app.candidate'; To = 'app'; Remaining = 2 })
    $transientLock = Invoke-CountdownInstallerModuleTest
    Assert-Equal 'True|not-needed' "$($transientLock.succeeded)|$($transientLock.rollback.outcome)" "A short-lived lock on the candidate does not fail the install: $($transientLock.failedStage) $($transientLock.error)"
    Assert-Equal $installerPackageFilesV2[1].Sha256 (Get-CountdownInstallerTestInstalledDll) 'The new version replaces the previous one once the lock is released'
    Assert-Equal 'app,runtime-status.json' ((@($installerOwnEntries | Where-Object { Test-Path -LiteralPath (Join-Path $installerCase.Root $_) })) -join ',') 'The install commits after the lock is released'

    # #284 中断恢复：安装取得安装锁之后，先按恢复表把上一次没走完的安装收拾好，同一次调用再照常安装（包是 v2）。下面各例在已装好
    # 的 v1 上摆出一次换成 v2 的安装被打断（断电、进程被杀）时留下的样子：应用已停（开发目录里的同名 EXE 照旧运行，登录任务没有
    # 运行中的实例），事务标记记着候选 v2 的提交与聚合哈希，设置及两代备份已快照到事务目录，候选 v2 已放好。Phase 是换程序走到了
    # 哪一步：before-replace 还没改名；mid-replace 现役 app 已改成 app.previous，候选还没改成 app；after-replace 两次改名都已完成。
    function Set-CountdownInstallerTestInterruptedInstall {
        param([Parameter(Mandatory)] [ValidateSet('before-replace', 'mid-replace', 'after-replace')] [string] $Phase)

        $installerCase.Processes = @($installerCase.Processes | Where-Object { $_.ExecutablePath -ne $installerCase.ExePath })
        $installerCase.Tasks[$installerCase.TaskName].State = 'Ready'
        $marker = [ordered]@{ schemaVersion = 1; startedAtUtc = '2026-11-14T09:19:00.0000000+00:00'; candidateCommit = $installerCommitV2; candidateAggregateSha256 = $installerAggregateV2 }
        [System.IO.File]::WriteAllText((Join-Path $installerCase.Root 'install-transaction.json'), ($marker | ConvertTo-Json), $installerUtf8)
        $snapshot = Join-Path $installerCase.Root 'install-transaction\settings-snapshot'
        New-Item -ItemType Directory -Path $snapshot -Force | Out-Null
        foreach ($name in $installerSettingsFamilyNames) { [System.IO.File]::Copy((Join-Path $installerCase.Root $name), (Join-Path $snapshot $name)) }
        $staging = Join-Path (Split-Path -Parent $installerCase.Root) 'interrupted-candidate'
        Write-CountdownInstallerTestProgram -Root $staging -Files $installerPackageFilesV2 -Commit $installerCommitV2 -AggregateSha256 $installerAggregateV2
        [System.IO.Directory]::Move((Join-Path $staging 'app'), (Join-Path $installerCase.Root 'app.candidate'))
        [System.IO.Directory]::Delete($staging)
        if ($Phase -ne 'before-replace') { [System.IO.Directory]::Move((Join-Path $installerCase.Root 'app'), (Join-Path $installerCase.Root 'app.previous')) }
        if ($Phase -eq 'after-replace') { [System.IO.Directory]::Move((Join-Path $installerCase.Root 'app.candidate'), (Join-Path $installerCase.Root 'app')) }
    }
    # 前置检查读桌面目录的那一刻（恢复之后、这次安装动手之前）记下安装器自己的条目各在不在、程序目录、用户数据与在运行的进程。
    $installerObserveRecovery = {
        $installerCase.Observed.OwnEntries = (@($installerOwnEntries | Where-Object { Test-Path -LiteralPath (Join-Path $installerCase.Root $_) })) -join ','
        $installerCase.Observed.Program = Get-CountdownInstallerTestTree -Root (Join-Path $installerCase.Root 'app') -Detailed
        $installerCase.Observed.UserData = Get-CountdownInstallerTestTree -Root $installerCase.Root -Exclude $installerOwnEntries -Detailed
        $installerCase.Observed.Running = Get-CountdownInstallerTestRunning
    }
    function Get-CountdownInstallerTestRecovery {
        param([Parameter(Mandatory)] [object] $Result)
        "$($Result.recovery.state)|$($Result.recovery.action)|$($Result.recovery.outcome)|$($Result.recovery.reason)"
    }

    # 换程序之前被打断（候选还在、现役是旧版本）：旧版本和设置都没被动过，删除候选与事务目录、清除事务标记。两次改名之间被打断
    # （现役不在、旧版本在）：把旧版本改名换回，同样清除标记、删除候选与事务目录。两种都不启动任何东西，恢复后现役程序目录与
    # 安装前的 v1 逐字节相同，设置没被动过；随后同一次调用照常装上 v2。
    foreach ($interrupted in @(
            [pscustomobject]@{ Name = 'recover-before-replace'; Phase = 'before-replace'; Action = 'discarded-candidate' }
            [pscustomobject]@{ Name = 'recover-mid-replace'; Phase = 'mid-replace'; Action = 'renamed-back' }
        )) {
        $installerCase = New-CountdownInstallerTestCase -Name $interrupted.Name
        Initialize-CountdownInstallerTestInstalledVersion
        $programBefore = Get-CountdownInstallerTestTree -Root (Join-Path $installerCase.Root 'app') -Detailed
        $userDataBefore = Get-CountdownInstallerTestTree -Root $installerCase.Root -Exclude $installerOwnEntries -Detailed
        Set-CountdownInstallerTestInterruptedInstall -Phase $interrupted.Phase
        $installerCase.OnGetDesktopDirectory = $installerObserveRecovery
        $recovered = Invoke-CountdownInstallerTest
        Assert-Equal "$($interrupted.Phase)|$($interrupted.Action)|recovered|" (Get-CountdownInstallerTestRecovery -Result $recovered) "Result JSON reports what the recovery found and did: $($interrupted.Name)"
        Assert-Equal $programBefore ([string] $installerCase.Observed.Program) "Recovery leaves the previous version in the program directory byte for byte: $($interrupted.Name)"
        Assert-Equal 'app,runtime-status.json' ([string] $installerCase.Observed.OwnEntries) "Recovery leaves no candidate, previous-version directory, marker or transaction directory: $($interrupted.Name)"
        Assert-Equal $userDataBefore ([string] $installerCase.Observed.UserData) "Recovery leaves the settings and the other user data as they were: $($interrupted.Name)"
        Assert-Equal '4103' ([string] $installerCase.Observed.Running) "Recovery starts nothing: $($interrupted.Name)"
        Assert-Equal $true ([bool] $recovered.succeeded) "The same call goes on to install the package: $($interrupted.Name) $($recovered.failedStage) $($recovered.error)"
        Assert-Equal "$($installerPackageFilesV2[1].Sha256)|4103,6001" "$(Get-CountdownInstallerTestInstalledDll)|$(Get-CountdownInstallerTestRunning)" "The package is installed and runs after the recovery: $($interrupted.Name)"
    }

    # 两次改名都已完成、新版本还没确认启动就绪（after-replace）：补做启动验收，新版本达到启动就绪就提交。第一例新版本没在运行，
    # 经登录任务启动它；第二例它在打断之前已经起来、回执是它写的，不再经任务启动（任务已在运行时会悄悄忽略新实例，#279），直接按
    # 回执核对。提交清除事务标记、删除旧版本目录与事务目录，新版本逐字节留在程序目录照常运行，设置不动；随后同一次调用照常安装
    # （停下它，再装一遍 v2）。
    foreach ($accepted in @(
            [pscustomobject]@{ Name = 'recover-after-replace-starts'; Running = '4103,6001'; Final = '4103,6002'; Arrange = $null }
            [pscustomobject]@{ Name = 'recover-after-replace-running'; Running = '4103,5001'; Final = '4103,6001'; Arrange = {
                    $installerCase.Processes = @($installerCase.Processes) + @(New-CountdownInstallerTestProcess -ProcessId 5001 -ExecutablePath $installerCase.ExePath -StartedByTask $installerCase.TaskName)
                    $installerCase.Tasks[$installerCase.TaskName].State = 'Running'
                    [System.IO.File]::WriteAllText((Join-Path $installerCase.Root 'runtime-status.json'), '{"schemaVersion":4,"stage":"window-ready","processId":5001,"sessionId":3}', $installerUtf8)
                } }
        )) {
        $installerCase = New-CountdownInstallerTestCase -Name $accepted.Name
        Initialize-CountdownInstallerTestInstalledVersion
        $userDataBefore = Get-CountdownInstallerTestTree -Root $installerCase.Root -Exclude $installerOwnEntries -Detailed
        Set-CountdownInstallerTestInterruptedInstall -Phase 'after-replace'
        if ($null -ne $accepted.Arrange) { & $accepted.Arrange }
        $programBefore = Get-CountdownInstallerTestTree -Root (Join-Path $installerCase.Root 'app') -Detailed
        $installerCase.OnGetDesktopDirectory = $installerObserveRecovery
        $committed = Invoke-CountdownInstallerTest
        Assert-Equal 'after-replace|committed|recovered|' (Get-CountdownInstallerTestRecovery -Result $committed) "Result JSON reports that the recovery committed the new version: $($accepted.Name)"
        Assert-Equal $programBefore ([string] $installerCase.Observed.Program) "The new version stays in the program directory byte for byte: $($accepted.Name)"
        Assert-Equal 'app,runtime-status.json' ([string] $installerCase.Observed.OwnEntries) "The commit leaves no previous-version directory, marker or transaction directory: $($accepted.Name)"
        Assert-Equal $userDataBefore ([string] $installerCase.Observed.UserData) "The commit leaves the settings and the other user data as they were: $($accepted.Name)"
        Assert-Equal $accepted.Running ([string] $installerCase.Observed.Running) "The new version runs when the recovery has committed it, started only when it was not running: $($accepted.Name)"
        Assert-Equal "True|$($accepted.Final)" "$($committed.succeeded)|$(Get-CountdownInstallerTestRunning)" "The same call goes on to install the package: $($accepted.Name) $($committed.failedStage) $($committed.error)"
    }

    # 新版本达不到启动就绪（恢复补做验收时它卡在首帧之前；随后继续的安装里再启动就正常），设置及两代备份与快照逐字节相同：
    # 停下新版本，换回旧版本（新版本改回候选后删除），清除标记，删除事务目录，旧版本不重新启动。恢复后现役程序目录与安装前的 v1
    # 逐字节相同，设置没被动过；随后同一次调用照常装上 v2。时限经导出的 module 函数缩短到 1 秒。
    $installerCase = New-CountdownInstallerTestCase -Name 'recover-after-replace-swap-back'
    Initialize-CountdownInstallerTestInstalledVersion
    $programBefore = Get-CountdownInstallerTestTree -Root (Join-Path $installerCase.Root 'app') -Detailed
    $userDataBefore = Get-CountdownInstallerTestTree -Root $installerCase.Root -Exclude $installerOwnEntries -Detailed
    Set-CountdownInstallerTestInterruptedInstall -Phase 'after-replace'
    $installerCase.Observed.NewVersionStarts = 0
    $installerCase.OnStartTask = {
        param([string] $ExePath)
        if (Test-CountdownInstallerTestNewVersion -ExecutablePath $ExePath) {
            $installerCase.Launch.WritesReceipt = $installerCase.Observed.NewVersionStarts -gt 0
            $installerCase.Observed.NewVersionStarts++
        }
    }
    $installerCase.OnGetDesktopDirectory = $installerObserveRecovery
    $swappedBack = Invoke-CountdownInstallerModuleTest
    Assert-Equal 'after-replace|swapped-back|recovered|' (Get-CountdownInstallerTestRecovery -Result $swappedBack) 'Result JSON reports that the recovery swapped the previous version back'
    Assert-Equal $programBefore ([string] $installerCase.Observed.Program) 'The previous version is back in the program directory byte for byte'
    Assert-Equal 'app' ([string] $installerCase.Observed.OwnEntries) 'Swapping back leaves no candidate, previous-version directory, marker or transaction directory'
    Assert-Equal $userDataBefore ([string] $installerCase.Observed.UserData) 'Swapping back leaves the settings and the other user data as they were'
    Assert-Equal '4103' ([string] $installerCase.Observed.Running) 'Swapping back stops the new version and does not start the previous one'
    Assert-Equal "True|4103,6002" "$($swappedBack.succeeded)|$(Get-CountdownInstallerTestRunning)" "The same call goes on to install the package after swapping back: $($swappedBack.failedStage) $($swappedBack.error)"

    # 新版本达不到启动就绪，而设置已经变化。第一例新版本在打断之前起来过，按正常流程迁移并保存了设置，这次一启动就崩溃；第二例
    # 它卡在首帧之前，正要被停下时刚好保存了迁移后的设置。恢复不换回旧版本，也不用快照覆盖设置，结果写明交给维护者决定
    # （needs-maintainer），安装停在恢复阶段、不继续：两个程序目录、事务标记与设置快照原样留下，设置是新版本保存的样子。
    foreach ($changedSettings in @(
            [pscustomobject]@{ Name = 'recover-settings-changed-before'; Arrange = {
                    Invoke-CountdownInstallerTestStartupMigration
                    $installerCase.Launch.StaysAlive = $false
                    $installerCase.Launch.WritesReceipt = $false
                } }
            [pscustomobject]@{ Name = 'recover-settings-changed-while-stopping'; Arrange = {
                    $installerCase.Launch.WritesReceipt = $false
                    $installerCase.OnStopProcess = { param([int] $ProcessId) Invoke-CountdownInstallerTestStartupMigration }
                } }
        )) {
        $installerCase = New-CountdownInstallerTestCase -Name $changedSettings.Name
        Initialize-CountdownInstallerTestInstalledVersion
        $familyBefore = Get-CountdownInstallerTestSettingsFamily -Directory $installerCase.Root
        Set-CountdownInstallerTestInterruptedInstall -Phase 'after-replace'
        & $changedSettings.Arrange
        $programsBefore = "$(Get-CountdownInstallerTestTree -Root (Join-Path $installerCase.Root 'app') -Detailed)#$(Get-CountdownInstallerTestTree -Root (Join-Path $installerCase.Root 'app.previous') -Detailed)"
        $leftAlone = Invoke-CountdownInstallerModuleTest
        Assert-Equal 'False|recovery|settings-changed' "$($leftAlone.succeeded)|$($leftAlone.failedStage)|$($leftAlone.reason)" "The install stops at the recovery when the settings changed: $($changedSettings.Name) $($leftAlone.error)"
        Assert-Equal 'after-replace||needs-maintainer|settings-changed' (Get-CountdownInstallerTestRecovery -Result $leftAlone) "Result JSON says a maintainer has to decide: $($changedSettings.Name)"
        Assert-Equal $programsBefore "$(Get-CountdownInstallerTestTree -Root (Join-Path $installerCase.Root 'app') -Detailed)#$(Get-CountdownInstallerTestTree -Root (Join-Path $installerCase.Root 'app.previous') -Detailed)" "The new and the previous version stay where they were: $($changedSettings.Name)"
        Assert-Equal "True|True|$familyBefore" "$(Test-Path -LiteralPath (Join-Path $installerCase.Root 'install-transaction.json'))|$($leftAlone.transactionMarkerPresent)|$(Get-CountdownInstallerTestSettingsFamily -Directory (Join-Path $installerCase.Root 'install-transaction\settings-snapshot'))" "The marker and the settings snapshot stay: $($changedSettings.Name)"
        Assert-Equal '{"schemaVersion":11,"note":"migrated by the new version at startup"}' ([System.IO.File]::ReadAllText((Join-Path $installerCase.Root 'settings.json'))) "The recovery does not overwrite the settings the new version saved: $($changedSettings.Name)"
        Assert-Equal '4103' (Get-CountdownInstallerTestRunning) "Nothing from the install root runs: $($changedSettings.Name)"
        foreach ($secret in @('settings-content-sentinel-280', 'migrated by the new version')) {
            Assert-Equal $false ($installerCase.Json.Contains($secret, [System.StringComparison]::Ordinal)) "Result JSON of a recovery left to the maintainer has no settings content: $($changedSettings.Name) $secret"
        }
    }

    # 没有事务标记却有残留：视为已提交，删掉残留，现役程序目录与在运行的应用都不受影响，随后同一次调用照常安装。第一例是提交清除
    # 事务标记之后被打断，留下旧版本目录与事务目录，新版本在运行；第二例是回滚清除事务标记之后没删掉候选与事务目录，另有一个写
    # 事务标记时没来得及改名的临时文件。
    foreach ($leftover in @(
            [pscustomobject]@{ Name = 'recover-leftovers-after-commit'; Running = '4103,5001'; Final = '4103,6001'; Arrange = {
                    Set-CountdownInstallerTestInterruptedInstall -Phase 'after-replace'
                    [System.IO.File]::Delete((Join-Path $installerCase.Root 'install-transaction.json'))
                    $installerCase.Processes = @($installerCase.Processes) + @(New-CountdownInstallerTestProcess -ProcessId 5001 -ExecutablePath $installerCase.ExePath -StartedByTask $installerCase.TaskName)
                    $installerCase.Tasks[$installerCase.TaskName].State = 'Running'
                    [System.IO.File]::WriteAllText((Join-Path $installerCase.Root 'runtime-status.json'), '{"schemaVersion":4,"stage":"window-ready","processId":5001,"sessionId":3}', $installerUtf8)
                } }
            [pscustomobject]@{ Name = 'recover-leftovers-without-marker'; Running = '4103'; Final = '4103,6001'; Arrange = {
                    Set-CountdownInstallerTestInterruptedInstall -Phase 'before-replace'
                    [System.IO.File]::Move((Join-Path $installerCase.Root 'install-transaction.json'), (Join-Path $installerCase.Root 'install-transaction.json.tmp'))
                } }
        )) {
        $installerCase = New-CountdownInstallerTestCase -Name $leftover.Name
        Initialize-CountdownInstallerTestInstalledVersion
        $userDataBefore = Get-CountdownInstallerTestTree -Root $installerCase.Root -Exclude $installerOwnEntries -Detailed
        & $leftover.Arrange
        $programBefore = Get-CountdownInstallerTestTree -Root (Join-Path $installerCase.Root 'app') -Detailed
        $installerCase.OnGetDesktopDirectory = $installerObserveRecovery
        $cleaned = Invoke-CountdownInstallerTest
        Assert-Equal 'leftovers|removed-leftovers|recovered|' (Get-CountdownInstallerTestRecovery -Result $cleaned) "Result JSON reports that the recovery removed the leftovers: $($leftover.Name)"
        Assert-Equal $programBefore ([string] $installerCase.Observed.Program) "The live program directory is untouched: $($leftover.Name)"
        Assert-Equal 'app,runtime-status.json' ([string] $installerCase.Observed.OwnEntries) "No candidate, previous-version directory or transaction directory is left: $($leftover.Name)"
        Assert-Equal $false (Test-Path -LiteralPath (Join-Path $installerCase.Root 'install-transaction.json.tmp')) "The marker's temporary file is gone: $($leftover.Name)"
        Assert-Equal $userDataBefore ([string] $installerCase.Observed.UserData) "The settings and the other user data are untouched: $($leftover.Name)"
        Assert-Equal $leftover.Running ([string] $installerCase.Observed.Running) "Removing leftovers neither stops nor starts the application: $($leftover.Name)"
        Assert-Equal "True|$($leftover.Final)" "$($cleaned.succeeded)|$(Get-CountdownInstallerTestRunning)" "The same call goes on to install the package: $($leftover.Name) $($cleaned.failedStage) $($cleaned.error)"
    }

    # 恢复什么都不改、安装停在恢复阶段的情况：现役是新版本、要补做启动验收，却没有交互会话或有两个交互会话（保持现状并报告，
    # 等恰好一个用户登录后再装）；三个程序目录都在，分不清哪个是旧版本（交给维护者决定）。安装根、桌面、登录任务与进程都不变。
    foreach ($untouched in @(
            [pscustomobject]@{ Name = 'recover-no-session'; Recovery = 'after-replace||left-as-is|no-interactive-session'; Arrange = { $installerCase.Sessions = @() } }
            [pscustomobject]@{ Name = 'recover-two-sessions'; Recovery = 'after-replace||left-as-is|multiple-interactive-sessions'; Arrange = {
                    $installerCase.Sessions = @($installerCase.Sessions) + @([pscustomobject]@{ SessionId = 5; User = 'MINMIN-OFFICE\second-account'; Sid = $installerOtherSid })
                } }
            [pscustomobject]@{ Name = 'recover-unrecognized'; Recovery = 'unrecognized||needs-maintainer|unrecognized-state'; Arrange = {
                    Write-CountdownInstallerTestProgram -Root (Join-Path $installerCase.Root 'stray-layout') -Files $installerPackageFilesV2 -Commit $installerCommitV2 -AggregateSha256 $installerAggregateV2
                    [System.IO.Directory]::Move((Join-Path $installerCase.Root 'stray-layout\app'), (Join-Path $installerCase.Root 'app.candidate'))
                    [System.IO.Directory]::Delete((Join-Path $installerCase.Root 'stray-layout'))
                } }
        )) {
        $installerCase = New-CountdownInstallerTestCase -Name $untouched.Name
        Initialize-CountdownInstallerTestInstalledVersion
        Set-CountdownInstallerTestInterruptedInstall -Phase 'after-replace'
        & $untouched.Arrange
        $treeBefore = Get-CountdownInstallerTestTree -Root $installerCase.Root -Detailed
        $desktopBefore = Get-CountdownInstallerTestTree -Root $installerCase.Desktop -Detailed
        $tasksBefore = Get-CountdownInstallerTestTaskDefinition -Task $installerCase.Tasks[$installerCase.TaskName]
        $untouchedResult = Invoke-CountdownInstallerModuleTest
        $expectedReason = ($untouched.Recovery -split '\|')[-1]
        Assert-Equal "False|recovery|$expectedReason" "$($untouchedResult.succeeded)|$($untouchedResult.failedStage)|$($untouchedResult.reason)" "The install stops at the recovery: $($untouched.Name) $($untouchedResult.error)"
        Assert-Equal $untouched.Recovery (Get-CountdownInstallerTestRecovery -Result $untouchedResult) "Result JSON reports why the recovery left the install root alone: $($untouched.Name)"
        Assert-Equal $treeBefore (Get-CountdownInstallerTestTree -Root $installerCase.Root -Detailed) "The recovery changes nothing in the install root: $($untouched.Name)"
        Assert-Equal $desktopBefore (Get-CountdownInstallerTestTree -Root $installerCase.Desktop -Detailed) "The recovery changes nothing on the desktop: $($untouched.Name)"
        Assert-Equal "4103|$tasksBefore|Ready" "$(Get-CountdownInstallerTestRunning)|$(Get-CountdownInstallerTestTaskDefinition -Task $installerCase.Tasks[$installerCase.TaskName])|$($installerCase.Tasks[$installerCase.TaskName].State)" "The recovery starts nothing and leaves the login task alone: $($untouched.Name)"
        Assert-Equal $true ([bool] $untouchedResult.transactionMarkerPresent) "Result JSON reports the marker still there: $($untouched.Name)"
    }

    # 恢复自己没做成：两次改名之间被打断，旧版本目录却一直被占用、改不回 app。结果写明恢复失败，安装停在恢复阶段；安装根仍是
    # 恢复之前的样子（下一次安装再从这个状态恢复），设置不动。占用放开之后的下一次安装照常恢复并装上 v2。
    $installerCase = New-CountdownInstallerTestCase -Name 'recover-rename-back-locked'
    Initialize-CountdownInstallerTestInstalledVersion
    Set-CountdownInstallerTestInterruptedInstall -Phase 'mid-replace'
    $installerCase.MoveLocks = @([pscustomobject]@{ From = 'app.previous'; To = 'app'; Remaining = -1 })
    $treeBefore = Get-CountdownInstallerTestTree -Root $installerCase.Root -Detailed
    $lockedRecovery = Invoke-CountdownInstallerModuleTest
    Assert-Equal 'False|recovery|recovery-failed' "$($lockedRecovery.succeeded)|$($lockedRecovery.failedStage)|$($lockedRecovery.reason)" "The install stops when the recovery cannot finish: $($lockedRecovery.error)"
    Assert-Equal 'mid-replace||failed|recovery-failed' (Get-CountdownInstallerTestRecovery -Result $lockedRecovery) 'Result JSON reports that the recovery failed'
    Assert-Equal $treeBefore (Get-CountdownInstallerTestTree -Root $installerCase.Root -Detailed) 'A failed recovery leaves the install root as it found it'
    $installerCase.MoveLocks = @()
    $unlockedRecovery = Invoke-CountdownInstallerModuleTest
    Assert-Equal 'mid-replace|renamed-back|recovered||True' "$(Get-CountdownInstallerTestRecovery -Result $unlockedRecovery)|$($unlockedRecovery.succeeded)" "The next install recovers once the lock is gone: $($unlockedRecovery.failedStage) $($unlockedRecovery.error)"

    # #283 回滚自身失败后留下的安装根由下一次安装的恢复接手：新版本没达到启动就绪，回滚时现役目录又一直被占用、改不回候选，
    # 程序目录仍是新版本、旧版本在 app.previous，事务标记与快照留下。占用放开、新版本也能正常启动之后，下一次安装补做启动验收
    # 并提交，再照常装上 v2。
    $installerCase = New-CountdownInstallerTestCase -Name 'recover-after-failed-rollback'
    Initialize-CountdownInstallerTestInstalledVersion
    $installerCase.Launch.WritesReceipt = $false
    $installerCase.MoveLocks = @([pscustomobject]@{ From = 'app'; To = 'app.candidate'; Remaining = -1 })
    $failedRollbackFirst = Invoke-CountdownInstallerModuleTest
    Assert-Equal 'rollback-failed|True' "$($failedRollbackFirst.rollback.outcome)|$($failedRollbackFirst.transactionMarkerPresent)" "The first install leaves a failed rollback behind: $(@($failedRollbackFirst.rollback.problems) -join '；')"
    $installerCase.MoveLocks = @()
    $installerCase.Launch.WritesReceipt = $true
    $afterFailedRollback = Invoke-CountdownInstallerModuleTest
    Assert-Equal 'after-replace|committed|recovered|' (Get-CountdownInstallerTestRecovery -Result $afterFailedRollback) 'The next install commits the new version the failed rollback left behind once it gets ready'
    Assert-Equal "True|$($installerPackageFilesV2[1].Sha256)|app,runtime-status.json" "$($afterFailedRollback.succeeded)|$(Get-CountdownInstallerTestInstalledDll)|$((@($installerOwnEntries | Where-Object { Test-Path -LiteralPath (Join-Path $installerCase.Root $_) })) -join ',')" "The same call goes on to install the package: $($afterFailedRollback.failedStage) $($afterFailedRollback.error)"

    # 只检查时不恢复：上一次安装没走完（这里停在两次改名之间）时只报告看到的状态，恢复排在将要执行的步骤最前面；包、安装根、
    # 应用与登录任务都不变。恢复会把旧版本换回 app，结果因此报告这是一次升级，不是首次安装。
    $installerCase = New-CountdownInstallerTestCase -Name 'check-only-unfinished-install'
    Initialize-CountdownInstallerTestInstalledVersion
    Set-CountdownInstallerTestInterruptedInstall -Phase 'mid-replace'
    $caseRoot = Split-Path -Parent $installerCase.Root
    $caseTreeBefore = Get-CountdownInstallerTestTree -Root $caseRoot -Detailed
    $taskDefinitionBefore = Get-CountdownInstallerTestTaskDefinition -Task $installerCase.Tasks[$installerCase.TaskName]
    $checkedRecovery = Invoke-CountdownInstallerTest -CheckOnly
    Assert-Equal 'True|True|False' "$($checkedRecovery.succeeded)|$($checkedRecovery.checkOnly)|$($checkedRecovery.firstInstall)" "Check-only passes over an unfinished upgrade: $($checkedRecovery.failedStage) $($checkedRecovery.error)"
    Assert-Equal 'mid-replace||planned|' (Get-CountdownInstallerTestRecovery -Result $checkedRecovery) 'Check-only reports the unfinished install it would recover'
    Assert-Equal 'recover-unfinished-install,place-candidate,stop-application,write-transaction-marker,snapshot-settings,settings-rehearsal,replace-program-directory,register-desktop-shortcut,start-and-wait-ready,commit' (@($checkedRecovery.plannedSteps) -join ',') 'Check-only plans the recovery before the install'
    Assert-Equal $caseTreeBefore (Get-CountdownInstallerTestTree -Root $caseRoot -Detailed) 'Check-only recovers nothing'
    Assert-Equal "4103|$taskDefinitionBefore|Ready" "$(Get-CountdownInstallerTestRunning)|$(Get-CountdownInstallerTestTaskDefinition -Task $installerCase.Tasks[$installerCase.TaskName])|$($installerCase.Tasks[$installerCase.TaskName].State)" 'Check-only starts nothing and leaves the login task alone'
    Assert-Equal $true ([bool] $checkedRecovery.transactionMarkerPresent) 'Check-only reports the marker still there'

    # 首次安装在换上新版本之后、登记登录任务之前被打断：没有旧版本，也还没有登录任务。补做启动验收无从启动新版本；设置快照是
    # 空的、安装根也没有设置文件，于是把新版本改回候选后删除，安装根回到没有程序目录的样子，随后同一次调用照常做首次安装。
    $installerCase = New-CountdownInstallerTestCase -Name 'recover-first-install-after-replace'
    Write-CountdownInstallerTestProgram -Root $installerCase.Root -Files $installerPackageFilesV2 -Commit $installerCommitV2 -AggregateSha256 $installerAggregateV2
    $firstInstallMarker = [ordered]@{ schemaVersion = 1; startedAtUtc = '2026-11-14T09:19:00.0000000+00:00'; candidateCommit = $installerCommitV2; candidateAggregateSha256 = $installerAggregateV2 }
    [System.IO.File]::WriteAllText((Join-Path $installerCase.Root 'install-transaction.json'), ($firstInstallMarker | ConvertTo-Json), $installerUtf8)
    New-Item -ItemType Directory -Path (Join-Path $installerCase.Root 'install-transaction\settings-snapshot') -Force | Out-Null
    $installerCase.OnGetDesktopDirectory = $installerObserveRecovery
    $firstRecovered = Invoke-CountdownInstallerTest
    Assert-Equal 'after-replace|swapped-back|recovered|' (Get-CountdownInstallerTestRecovery -Result $firstRecovered) 'Result JSON reports that the recovery undid the interrupted first install'
    Assert-Equal '|' "$($installerCase.Observed.OwnEntries)|$($installerCase.Observed.Running)" 'The recovery leaves no program directory or transaction trace from the interrupted first install, and nothing runs'
    Assert-Equal 'True|True|created|6001' "$($firstRecovered.succeeded)|$($firstRecovered.firstInstall)|$($firstRecovered.loginTask.action)|$(Get-CountdownInstallerTestRunning)" "The same call goes on with a first install: $($firstRecovered.failedStage) $($firstRecovered.error)"

    # 没有交互会话时保持现状只针对 after-replace（补做启动验收要一个会话）。两次改名之间被打断时恢复不启动任何东西，没人登录也
    # 照样把旧版本改名换回：不然现役 app 不在，下次登录时旧版本起不来。随后的安装在前置检查处因没有交互会话停下。
    $installerCase = New-CountdownInstallerTestCase -Name 'recover-mid-replace-no-session'
    Initialize-CountdownInstallerTestInstalledVersion
    $programBefore = Get-CountdownInstallerTestTree -Root (Join-Path $installerCase.Root 'app') -Detailed
    Set-CountdownInstallerTestInterruptedInstall -Phase 'mid-replace'
    $installerCase.Sessions = @()
    $noSessionRecovery = Invoke-CountdownInstallerTest
    Assert-Equal 'mid-replace|renamed-back|recovered|' (Get-CountdownInstallerTestRecovery -Result $noSessionRecovery) 'Recovery renames the previous version back without an interactive session'
    Assert-Equal 'False|preflight|no-interactive-session' "$($noSessionRecovery.succeeded)|$($noSessionRecovery.failedStage)|$($noSessionRecovery.reason)" 'The install then stops at the preflight'
    Assert-Equal $programBefore (Get-CountdownInstallerTestTree -Root (Join-Path $installerCase.Root 'app') -Detailed) 'The previous version is back in the program directory for the next login'
    Assert-Equal 'app,runtime-status.json|4103' "$((@($installerOwnEntries | Where-Object { Test-Path -LiteralPath (Join-Path $installerCase.Root $_) })) -join ',')|$(Get-CountdownInstallerTestRunning)" 'No unfinished-install trace is left and nothing is started'

    # 用真实的候选 EXE 走通演练与判定（#282）。包是本次构建输出 app\（与下面只读一侧的实测同一份），候选 EXE 的无界面命令经
    # 生产 port 真的运行，在创建窗口之前返回、不开窗口；交互会话、进程、计划任务与目录改名仍是测试 port，新版本不会真的启动。
    # 设置都是夹具：等价的旧版本夹具是 schema v9（循环提醒已经开始，迁移出的 cursor 取决于迁移时刻）带两代备份，安装继续并
    # 提交；不等价反例是 schema v2，产品迁移得了，等价判定却只认 v3-v9 到 v10（unsupported-schema），安装失败、旧版本重新运行。
    # 第三例的候选 EXE 不是可执行文件（测试包里的文本），演练没能完成，同样不换程序。各例现役设置与两代备份都与安装前逐字节相同。
    Import-Module (Join-Path $PSScriptRoot 'Countdown.Installer.psm1') -Force
    $installerCommandPorts = $installerPorts.PSObject.Copy()
    $installerCommandPorts.RunHeadlessCommand = (New-CountdownInstallerWindowsPorts).RunHeadlessCommand
    $installerBuiltDllSha256 = Get-CountdownInstallerTestFileHash -Path (Join-Path $PSScriptRoot 'app\MinminCountdown.dll')
    $installerFixtureV9 = '{"schemaVersion":9,"timeZoneId":"China Standard Time","displayPrecision":"Second","monthlySalary":20000.50,"workStart":"09:00","workEnd":"18:30","themePreference":"Night","tags":[],"countdowns":[{"id":"countdown-v9","name":"版本九倒计时","targetUtc":"2031-09-10T11:12:13.0000000+00:00","tagIds":[]}],"notes":[{"id":"note-v9-pending","text":"待送达一次提醒","date":"2031-09-10","isCompleted":false,"tagIds":[],"reminder":{"type":"once","firstDueUtc":"2031-09-10T01:30:00.0000000+00:00","delivery":"pending"}},{"id":"note-v9-delivered","text":"已送达一次提醒","date":"2031-09-10","isCompleted":false,"tagIds":[],"reminder":{"type":"once","firstDueUtc":"2031-09-10T02:30:00.0000000+00:00","delivery":"delivered"}},{"id":"note-v9-repeat","text":"已开始循环提醒 settings-sentinel-282","date":"2031-09-10","isCompleted":false,"tagIds":[],"reminder":{"type":"repeat","firstDueUtc":"2026-01-01T00:00:00.0000000+00:00","intervalMinutes":30}}]}'
    $installerFixtureV2 = '{"schemaVersion":2,"timeZoneId":"China Standard Time","countdowns":[{"id":"countdown-v2","name":"倒计时 settings-sentinel-282","targetUtc":"2030-02-03T04:05:06.0000000+00:00"}]}'
    $installerFixtureBackups = @(
        '{"schemaVersion":10,"timeZoneId":"China Standard Time","displayPrecision":"Minute","monthlySalary":0,"workStart":"09:00","workEnd":"18:00","themePreference":"Day","tags":[],"countdowns":[],"notes":[],"reminderStates":{}}'
        '{"schemaVersion":1,"timeZoneId":"China Standard Time","targetUtc":"2030-01-02T03:04:05.0000000+00:00"}'
    )
    foreach ($realRehearsal in @(
            [pscustomobject]@{ Name = 'real-exe-v9-equivalent'; BuiltPackage = $true; Settings = $installerFixtureV9; Outcome = 'True||'; Report = 'equivalent|migrated|migrated' }
            [pscustomobject]@{ Name = 'real-exe-v2-not-equivalent'; BuiltPackage = $true; Settings = $installerFixtureV2; Outcome = 'False|settings-rehearsal|settings-not-equivalent'; Report = 'not-equivalent|migrated|unsupported-schema' }
            [pscustomobject]@{ Name = 'real-port-unrunnable-candidate'; BuiltPackage = $false; Settings = $installerFixtureV9; Outcome = 'False|settings-rehearsal|settings-rehearsal-failed'; Report = 'failed||' }
        )) {
        $installerCase = New-CountdownInstallerTestCase -Name $realRehearsal.Name
        if ($realRehearsal.BuiltPackage) { $installerCase.PackageRoot = $PSScriptRoot }
        Initialize-CountdownInstallerTestInstalledVersion
        foreach ($member in @(@('settings.json', $realRehearsal.Settings), @('settings.json.backup1', $installerFixtureBackups[0]), @('settings.json.backup2', $installerFixtureBackups[1]))) {
            $memberPath = Join-Path $installerCase.Root $member[0]
            [System.IO.File]::WriteAllText($memberPath, $member[1], $installerUtf8)
            [System.IO.File]::SetCreationTimeUtc($memberPath, $installerUserDataTimeUtc)
            [System.IO.File]::SetLastWriteTimeUtc($memberPath, $installerUserDataTimeUtc)
        }
        $programBefore = Get-CountdownInstallerTestTree -Root (Join-Path $installerCase.Root 'app') -Detailed
        $userDataBefore = Get-CountdownInstallerTestTree -Root $installerCase.Root -Exclude $installerOwnEntries -Detailed
        $realRun = Invoke-CountdownInstallerTest -Ports $installerCommandPorts
        Assert-Equal $realRehearsal.Outcome "$($realRun.succeeded)|$($realRun.failedStage)|$($realRun.reason)" "Install with the real candidate EXE ends as the rehearsal says: $($realRehearsal.Name) $($realRun.error)"
        Assert-Equal $realRehearsal.Report "$($realRun.settingsRehearsal.outcome)|$($realRun.settingsRehearsal.migration)|$($realRun.settingsRehearsal.equivalence)" "Result JSON names the real rehearsal conclusion: $($realRehearsal.Name)"
        Assert-Equal $userDataBefore (Get-CountdownInstallerTestTree -Root $installerCase.Root -Exclude $installerOwnEntries -Detailed) "Live settings and both backups are byte-for-byte as before: $($realRehearsal.Name)"
        if ([bool] $realRun.succeeded) {
            Assert-Equal $installerBuiltDllSha256 (Get-CountdownInstallerTestInstalledDll) "The built program replaces the previous version: $($realRehearsal.Name)"
        }
        else {
            Assert-Equal $programBefore (Get-CountdownInstallerTestTree -Root (Join-Path $installerCase.Root 'app') -Detailed) "Program directory is byte-for-byte as before: $($realRehearsal.Name)"
            Assert-Equal '4103,6001' (Get-CountdownInstallerTestRunning) "The previous version runs again: $($realRehearsal.Name)"
        }
        Assert-Equal 'app,runtime-status.json' ((@($installerOwnEntries | Where-Object { Test-Path -LiteralPath (Join-Path $installerCase.Root $_) })) -join ',') "No transaction marker, candidate or transaction directory is left behind: $($realRehearsal.Name)"
        Assert-Equal $false ($installerCase.Json.Contains('settings-sentinel-282', [System.StringComparison]::Ordinal)) "Result JSON leaves out the settings content: $($realRehearsal.Name)"
    }

    # 生产 port 的只读一侧：子进程按命令行方式运行入口，-CheckOnly 指向临时安装根和一个不存在的任务名，以本次构建输出为包。
    # 只查询交互会话、进程与任务，并从交互用户的注册表读出桌面目录的位置（不读桌面上的任何东西），不做任何改动。
    # 本机恰有一个交互会话时报告将要执行的步骤（含创建登录任务与登记桌面快捷方式），否则在前置检查拒绝。
    $installerRealParent = Join-Path $installerTestRoot 'real-ports'
    New-Item -ItemType Directory -Path $installerRealParent -Force | Out-Null
    $installerRealRoot = Join-Path $installerRealParent 'install'
    $installerRealTask = 'Minmin Countdown Installer Probe ' + [guid]::NewGuid().ToString('N')
    $installerRealLock = 'Local\MinminCountdownInstallerTest-' + [guid]::NewGuid().ToString('N')
    $installerRealOutput = @(& pwsh -NoLogo -NoProfile -NonInteractive -File $installerScript -PackageRoot $PSScriptRoot -InstallRoot $installerRealRoot -TaskName $installerRealTask -CheckOnly -LockName $installerRealLock)
    Assert-Equal 0 $LASTEXITCODE 'Installer entry runs check-only from the command line with real Windows queries'
    $installerRealText = $installerRealOutput -join "`n"
    $installerReal = $installerRealText | ConvertFrom-Json -DateKind String
    Assert-Equal $true ([bool] $installerReal.checkOnly) 'Real check-only reports check-only mode'
    if ([bool] $installerReal.succeeded) {
        Assert-Equal 'place-candidate,stop-application,write-transaction-marker,snapshot-settings,replace-program-directory,create-login-task,register-desktop-shortcut,start-and-wait-ready,commit' (@($installerReal.plannedSteps) -join ',') 'Real check-only plans to create the missing login task and to register the desktop shortcut'
        $installerBuiltAggregate = [string] ([System.IO.File]::ReadAllText((Join-Path $PSScriptRoot 'app\published-payload.json')) | ConvertFrom-Json).aggregateSha256
        Assert-Equal $installerBuiltAggregate ([string] $installerReal.package.aggregateSha256) 'Real check-only verifies this build output as the package'
    }
    else {
        Assert-Equal $true ("$($installerReal.failedStage)|$($installerReal.reason)" -in @('preflight|no-interactive-session', 'preflight|multiple-interactive-sessions')) "Real check-only fails only on the interactive session count: $($installerReal.failedStage) $($installerReal.reason) $($installerReal.error)"
    }
    Assert-Equal '<missing>' (Get-CountdownInstallerTestTree -Root $installerRealRoot) 'Real check-only creates no install root'
    $installerRealLeaks = @(@($env:COMPUTERNAME, $env:USERNAME) | Where-Object { [string] $_ -and ([string] $_).Length -ge 4 -and $installerRealText.IndexOf([string] $_, [System.StringComparison]::OrdinalIgnoreCase) -ge 0 })
    Assert-Equal 0 $installerRealLeaks.Count 'Real check-only output carries no host name or account'

    # 生产 port 的另两处只读查询（#281）。桌面目录按交互用户的注册表环境解析：当前用户就是交互用户时，与当前用户的 Desktop
    # 已知文件夹一致（DoNotVerify：两边都不访问桌面）。读一个已有的根目录任务（不读倒计时的任务）不出错。
    $installerRealSessions = @(& $installerWindowsPorts.GetInteractiveSessions)
    if ($installerRealSessions.Count -eq 1 -and [string]::Equals([string] $installerRealSessions[0].Sid, [System.Security.Principal.WindowsIdentity]::GetCurrent().User.Value, [System.StringComparison]::OrdinalIgnoreCase)) {
        $installerRealDesktop = [string] (& $installerWindowsPorts.GetDesktopDirectory $installerRealSessions[0])
        $installerKnownDesktop = [Environment]::GetFolderPath([Environment+SpecialFolder]::DesktopDirectory, [Environment+SpecialFolderOption]::DoNotVerify)
        Assert-Equal $true ([string]::Equals($installerRealDesktop.TrimEnd('\'), $installerKnownDesktop.TrimEnd('\'), [System.StringComparison]::OrdinalIgnoreCase)) 'The desktop port resolves the interactive user''s Desktop known folder from the registry'
    }
    $installerRealExistingTask = @(Get-ScheduledTask -TaskPath '\' -ErrorAction Stop | Where-Object { $_.TaskName -notmatch '敏敏子|Minmin|Countdown' } | Select-Object -First 1)
    if ($installerRealExistingTask.Count -eq 1) {
        $installerRealTaskRead = & $installerWindowsPorts.GetTask $installerRealExistingTask[0].TaskName
        Assert-Equal $true ($null -ne $installerRealTaskRead -and @($installerRealTaskRead.Actions).Count -ge 1) 'The task port reads an existing task'
        # 回滚恢复登录任务用的整份导出（#283）只读：一个已有的根目录任务导出为任务计划的 XML。
        $installerRealTaskXml = [string] (& $installerWindowsPorts.ExportTask $installerRealExistingTask[0].TaskName)
        Assert-Equal $true ($installerRealTaskXml.Contains('<Task', [System.StringComparison]::Ordinal) -and $installerRealTaskXml.Contains('<Actions', [System.StringComparison]::Ordinal)) 'The task export port exports an existing task as Task Scheduler XML'
    }
    Assert-Equal $true ($null -eq (& $installerWindowsPorts.ExportTask $installerRealTask)) 'The task export port reports a missing task as absent'
}
finally {
    Remove-CountdownIsolatedTestTree -Path $installerTestRoot -AllowedRoot $installerTempRoot
}
