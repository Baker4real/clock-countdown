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
$installerCommitV2 = '2b80c4d6e8f0a1b3c5d7e9f1a2b4c6d8e0f2a4b6'
$installerAggregateV2 = 'A3F2FE256C10123C8BBDA2890A10D6985A0134D1DA42C737B6B8A05DB8010E78'
$installerPackageFilesV2 = @(
    [pscustomobject]@{ Path = 'app\holiday-data\2027.json'; Text = 'installer holiday v2'; Sha256 = '5987A3F052924557E59C0E387723DDEED9E53B74997FB8FF418552117EDC90DB' }
    [pscustomobject]@{ Path = 'app\MinminCountdown.dll'; Text = 'installer dll v2'; Sha256 = 'DB2617243BF6059E225265C27CFFFE1E01CF5B9B78BDBB67B6789CB8BA8B548D' }
    [pscustomobject]@{ Path = 'app\MinminCountdown.exe'; Text = 'installer exe v2'; Sha256 = '6DEDBE93409F73643FAECDBD4B4FB980CB3B328B16555D66B548EF5C846CB989' }
)
$installerCommitV1 = '1a70b3c5d7e9f0a2b4c6d8e0f1a3b5c7d9e1f3a5'
$installerAggregateV1 = 'E75412ECA2410C4B405F8541F7ED0841E4DF93AD3D16926A128177DFEE3DAC16'
$installerProgramFilesV1 = @(
    [pscustomobject]@{ Path = 'app\MinminCountdown.dll'; Text = 'installer dll v1'; Sha256 = '41F7D2E165D4F4569FB1B0B6F95BAA18EBABE0F85F5B9A62AD448D7B2D0FE903' }
    [pscustomobject]@{ Path = 'app\MinminCountdown.exe'; Text = 'installer exe v1'; Sha256 = 'BDD2EABFD93033B9B617242BA6D4D62F1C1D7B006CDCC20941D6446D43CDBB14' }
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
$installerOwnEntries = @('app', 'app.candidate', 'app.previous', 'install-transaction.json', 'runtime-status.json')
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
    # 结束进程；由任务启动的进程一退出，任务实例随之结束（#279 实测 Stop-Process 后任务回到 Ready）。
    StopProcess = {
        param([int] $ProcessId)
        foreach ($process in @($installerCase.Processes | Where-Object { $_.ProcessId -eq $ProcessId -and $_.StartedByTask })) {
            $installerCase.Tasks[$process.StartedByTask].State = 'Ready'
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
        # 用例可改写这次启动：进程落在哪个会话、写完回执后是否还在运行、是否写出回执。
        # 与任务计划一样，停用的任务和不允许按需启动的任务不能按需启动。
        $task = $installerCase.Tasks[$Name]
        if (-not $task.Enabled -or -not $task.AllowDemandStart) { throw "任务 $Name 不能按需启动。" }
        $task.State = 'Running'
        if ($null -ne $installerCase.OnStartTask) { & $installerCase.OnStartTask }
        $exePath = ([string] $task.Actions[0].Execute).Trim('"')
        if (-not (Test-Path -LiteralPath $exePath -PathType Leaf)) { return }
        $processId = $installerCase.NextProcessId
        $installerCase.NextProcessId = $processId + 1
        $process = New-CountdownInstallerTestProcess -ProcessId $processId -ExecutablePath $exePath -StartedByTask $Name
        $process.SessionId = $installerCase.Launch.SessionId
        if ($installerCase.Launch.StaysAlive) { $installerCase.Processes = @($installerCase.Processes) + @($process) }
        if (-not $installerCase.Launch.WritesReceipt) { return }
        $applicationRoot = Split-Path -Parent (Split-Path -Parent $exePath)
        $receipt = [ordered]@{
            schemaVersion = 4
            stage = 'window-ready'
            launchToken = [guid]::NewGuid().ToString('N')
            processId = $processId
            sessionId = $installerCase.Launch.SessionId
            applicationPath = Join-Path $applicationRoot '敏敏子倒计时.ps1'
            readyAtUtc = '2026-11-14T09:20:00.0000000+00:00'
            calendarState = 'Pending'
            timeZoneId = 'China Standard Time'
        }
        [System.IO.File]::WriteAllText((Join-Path $applicationRoot 'runtime-status.json'), ($receipt | ConvertTo-Json), $installerUtf8)
    }
    MoveDirectory = {
        param([string] $Source, [string] $Destination)
        [System.IO.Directory]::Move($Source, $Destination)
        if ($null -ne $installerCase.OnMoveDirectory) { & $installerCase.OnMoveDirectory }
    }
    # 交互用户的桌面目录是用例自己的临时目录；用例把它置空时模拟读不到桌面位置。
    GetDesktopDirectory = {
        param([object] $Session)
        if ($null -eq $installerCase.Desktop) { throw '读不到交互用户的桌面位置。' }
        $installerCase.Desktop
    }
    ReadShortcut = $installerWindowsPorts.ReadShortcut
    WriteShortcut = $installerWindowsPorts.WriteShortcut
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
        Launch = [pscustomobject]@{ SessionId = 3; StaysAlive = $true; WritesReceipt = $true }
        OnStopProcess = $null
        OnMoveDirectory = $null
        OnStartTask = $null
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

# 任务定义的逐项快照（不含随运行变化的状态），用来比较安装前后有没有被动过。
function Get-CountdownInstallerTestTaskDefinition {
    param([AllowNull()] [object] $Task)
    if ($null -eq $Task) { return '<missing>' }
    $Task | Select-Object -Property * -ExcludeProperty State | ConvertTo-Json -Depth 4 -Compress
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
    param([switch] $CheckOnly)

    $output = @(& $installerScript -PackageRoot $installerCase.PackageRoot -InstallRoot $installerCase.Root -TaskName $installerCase.TaskName -CheckOnly:$CheckOnly -Port $installerPorts -LockName $installerLockName)
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

    # 前置检查失败：包内文件与载荷清单不符（文件被改、文件缺少）、没有交互会话、有多个交互会话。都在停止旧应用之前拒绝：
    # 除删除候选外安装根没有任何改动（首次安装时连安装根也不留下），应用照常运行，登录任务照旧，也不创建登录任务。
    # “只检查、不改动”同样核对包。上次安装没走完（留有事务标记与旧版本目录）时，中断恢复由 #284 实现，在那之前拒绝。
    $installerRejections = @(
        [pscustomobject]@{ Name = 'reject-tampered-file'; Installed = $true; CheckOnly = $false; Stage = 'preflight'; Reason = 'payload-mismatch'; Arrange = { [System.IO.File]::WriteAllText((Join-Path $installerCase.PackageRoot 'app\MinminCountdown.exe'), 'installer exe v2 tampered', $installerUtf8) } }
        [pscustomobject]@{ Name = 'reject-missing-file'; Installed = $true; CheckOnly = $false; Stage = 'preflight'; Reason = 'payload-mismatch'; Arrange = { [System.IO.File]::Delete((Join-Path $installerCase.PackageRoot 'app\holiday-data\2027.json')) } }
        [pscustomobject]@{ Name = 'reject-no-session'; Installed = $true; CheckOnly = $false; Stage = 'preflight'; Reason = 'no-interactive-session'; Arrange = { $installerCase.Sessions = @() } }
        [pscustomobject]@{ Name = 'reject-two-sessions'; Installed = $true; CheckOnly = $false; Stage = 'preflight'; Reason = 'multiple-interactive-sessions'; Arrange = { $installerCase.Sessions = @($installerCase.Sessions) + @([pscustomobject]@{ SessionId = 5; User = 'MINMIN-OFFICE\second-account'; Sid = $installerOtherSid }) } }
        [pscustomobject]@{ Name = 'reject-no-desktop'; Installed = $true; CheckOnly = $false; Stage = 'preflight'; Reason = 'desktop-unavailable'; Arrange = { $installerCase.Desktop = $null } }
        [pscustomobject]@{ Name = 'reject-first-install-tampered'; Installed = $false; CheckOnly = $false; Stage = 'preflight'; Reason = 'payload-mismatch'; Arrange = { [System.IO.File]::WriteAllText((Join-Path $installerCase.PackageRoot 'app\MinminCountdown.dll'), 'installer dll v2 tampered', $installerUtf8) } }
        [pscustomobject]@{ Name = 'reject-check-only-tampered'; Installed = $true; CheckOnly = $true; Stage = 'preflight'; Reason = 'payload-mismatch'; Arrange = { [System.IO.File]::WriteAllText((Join-Path $installerCase.PackageRoot 'app\MinminCountdown.exe'), 'installer exe v2 tampered', $installerUtf8) } }
        [pscustomobject]@{ Name = 'reject-unfinished-install'; Installed = $true; CheckOnly = $false; Stage = 'recovery'; Reason = 'unfinished-install'; Arrange = {
                [System.IO.File]::WriteAllText((Join-Path $installerCase.Root 'install-transaction.json'), '{"schemaVersion":1}', $installerUtf8)
                Write-CountdownInstallerTestProgram -Root (Join-Path $installerCase.Root 'previous-layout') -Files $installerProgramFilesV1 -Commit $installerCommitV1 -AggregateSha256 $installerAggregateV1
                [System.IO.Directory]::Move((Join-Path $installerCase.Root 'previous-layout\app'), (Join-Path $installerCase.Root 'app.previous'))
                [System.IO.Directory]::Delete((Join-Path $installerCase.Root 'previous-layout'))
            } }
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
    # 已有登录任务与期望定义一致时不计划动它，不一致时计划更新；首次安装时计划创建，安装根也不会被建出来。只检查不读桌面上的
    # 快捷方式，计划里总有登记快捷方式这一步（到时缺失则创建、是安装器的则更新、不是则保留）。
    foreach ($checkOnly in @(
            [pscustomobject]@{ Name = 'check-only-upgrade'; Installed = $true; Arrange = $null; Steps = 'place-candidate,stop-application,write-transaction-marker,replace-program-directory,register-desktop-shortcut,start-and-wait-ready,commit' }
            [pscustomobject]@{ Name = 'check-only-task-mismatch'; Installed = $true; Arrange = { $installerCase.Tasks[$installerCase.TaskName].Priority = 7 }; Steps = 'place-candidate,stop-application,write-transaction-marker,replace-program-directory,update-login-task,register-desktop-shortcut,start-and-wait-ready,commit' }
            [pscustomobject]@{ Name = 'check-only-first-install'; Installed = $false; Arrange = $null; Steps = 'place-candidate,stop-application,write-transaction-marker,replace-program-directory,create-login-task,register-desktop-shortcut,start-and-wait-ready,commit' }
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
            $ports = [pscustomobject]@{ GetInteractiveSessions = $refuse; GetProcesses = $refuse; StopProcess = $refuse; GetTask = $refuse; RegisterTask = $refuse; StartTask = $refuse; MoveDirectory = $refuse; GetDesktopDirectory = $refuse; ReadShortcut = $refuse; WriteShortcut = $refuse }
            & $Script -PackageRoot $PackageRoot -InstallRoot $InstallRoot -TaskName $TaskName -Port $ports -LockName $LockName
        }
        $installerCase.Observed.SecondInstall = @(Receive-Job -Job $secondInstall -Wait -AutoRemoveJob)
    }
    $firstOfTwo = Invoke-CountdownInstallerTest
    Assert-Equal 1 @($installerCase.Observed.SecondInstall).Count 'The second install writes exactly one JSON document'
    $secondOfTwo = [string] @($installerCase.Observed.SecondInstall)[-1] | ConvertFrom-Json -DateKind String
    Assert-Equal 'False|lock|install-in-progress' "$($secondOfTwo.succeeded)|$($secondOfTwo.failedStage)|$($secondOfTwo.reason)" 'A second install is rejected while another install is in progress'
    Assert-Equal $installerSettingsFingerprint (Get-CountdownInstallerTestFingerprint -Fingerprint $secondOfTwo.settings.before) 'The rejected second install still reports the settings fingerprint'
    Assert-Equal $true ([bool] $firstOfTwo.succeeded) "The install holding the lock commits: $($firstOfTwo.failedStage) $($firstOfTwo.error)"
    Assert-Equal $false (Test-Path -LiteralPath (Join-Path $installerCase.Root 'install-transaction.json')) 'The install holding the lock leaves no transaction marker'

    # 下面几例经导出的 module 函数把停止与启动就绪的等待时限缩短到 1 秒，都在已装好的 v1 上升级。
    # 启动就绪只认：回执由一个存活进程写出，其映像路径是已安装 EXE，会话是交互会话，EXE 哈希与载荷清单一致。每例只违反一条，
    # 等待时限内达不到启动就绪就不提交：失败在启动阶段，事务标记与旧版本目录都还在（回滚由 #283 实现）。
    # 登录任务有运行中的实例时，经它启动会被 IgnoreNew 悄悄拒绝（#279）：应用进程都结束后实例仍在（例如任务的动作不是
    # 已安装 EXE），就在写事务标记之前停下，候选删除，程序目录不变；替换程序目录期间任务又运行起来时，不再触发它。
    Import-Module (Join-Path $PSScriptRoot 'Countdown.Installer.psm1') -Force
    $installerShortWaits = @(
        [pscustomobject]@{ Name = 'not-ready-no-receipt'; Stage = 'start-and-wait-ready'; Reason = 'startup-not-ready'; Running = '4103,6001'; Arrange = { $installerCase.Launch.WritesReceipt = $false } }
        [pscustomobject]@{ Name = 'not-ready-process-exited'; Stage = 'start-and-wait-ready'; Reason = 'startup-not-ready'; Running = '4103'; Arrange = { $installerCase.Launch.StaysAlive = $false } }
        [pscustomobject]@{ Name = 'not-ready-other-session'; Stage = 'start-and-wait-ready'; Reason = 'startup-not-ready'; Running = '4103,6001'; Arrange = { $installerCase.Launch.SessionId = 4 } }
        [pscustomobject]@{ Name = 'not-ready-exe-changed'; Stage = 'start-and-wait-ready'; Reason = 'startup-not-ready'; Running = '4103,6001'; Arrange = { $installerCase.OnStartTask = { [System.IO.File]::WriteAllText($installerCase.ExePath, 'installer exe v2 changed after placement', $installerUtf8) } } }
        [pscustomobject]@{ Name = 'task-instance-outlives-app'; Stage = 'stop-application'; Reason = 'login-task-still-running'; Running = '4103'; Arrange = { $installerCase.Processes[0].StartedByTask = $null } }
        [pscustomobject]@{ Name = 'task-runs-during-replace'; Stage = 'start-and-wait-ready'; Reason = 'login-task-still-running'; Running = '4103'; Arrange = { $installerCase.OnMoveDirectory = { $installerCase.Tasks[$installerCase.TaskName].State = 'Running' } } }
    )
    foreach ($shortWait in $installerShortWaits) {
        $installerCase = New-CountdownInstallerTestCase -Name $shortWait.Name
        Initialize-CountdownInstallerTestInstalledVersion
        & $shortWait.Arrange
        $shortWaitResult = Install-CountdownProgram -PackageRoot $installerCase.PackageRoot -InstallRoot $installerCase.Root -TaskName $installerCase.TaskName -Ports $installerPorts -LockName $installerLockName -ReadyTimeoutSeconds 1 -StopTimeoutSeconds 1 |
            ConvertTo-Json -Depth 6 | ConvertFrom-Json -DateKind String
        Assert-Equal "False|$($shortWait.Stage)|$($shortWait.Reason)" "$($shortWaitResult.succeeded)|$($shortWaitResult.failedStage)|$($shortWaitResult.reason)" "Install does not commit: $($shortWait.Name) $($shortWaitResult.error)"
        Assert-Equal $shortWait.Running (Get-CountdownInstallerTestRunning) "Install starts the new version only through an idle login task: $($shortWait.Name)"
        $transactionOpened = $shortWait.Stage -ne 'stop-application'
        Assert-Equal $transactionOpened (Test-Path -LiteralPath (Join-Path $installerCase.Root 'install-transaction.json')) "Transaction marker stays only once the transaction began: $($shortWait.Name)"
        Assert-Equal $transactionOpened (Test-Path -LiteralPath (Join-Path $installerCase.Root 'app.previous')) "Previous version is kept aside only once the transaction began: $($shortWait.Name)"
        Assert-Equal $false (Test-Path -LiteralPath (Join-Path $installerCase.Root 'app.candidate')) "No candidate is left behind: $($shortWait.Name)"
        if (-not $transactionOpened) {
            Assert-Equal $installerProgramFilesV1[1].Sha256 (Get-CountdownInstallerTestFileHash -Path $installerCase.ExePath) "Program directory stays the installed version before the transaction began: $($shortWait.Name)"
        }
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
        Assert-Equal 'place-candidate,stop-application,write-transaction-marker,replace-program-directory,create-login-task,register-desktop-shortcut,start-and-wait-ready,commit' (@($installerReal.plannedSteps) -join ',') 'Real check-only plans to create the missing login task and to register the desktop shortcut'
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
    }
}
finally {
    Remove-CountdownIsolatedTestTree -Path $installerTestRoot -AllowedRoot $installerTempRoot
}
