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
$installerOwnEntries = @('app', 'app.candidate', 'app.previous', 'install-transaction', 'install-transaction.json', 'runtime-status.json')
# 假迁移命令报告的迁移时刻（#282）：等价判定只有收到这个时刻才认可迁移结果。
$installerMigrationUtc = '2026-11-14T09:19:30.0000000+00:00'
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
    RegisterTask = {
        param([string] $Name, [object] $Definition, [object] $Session)
        $installerCase.Tasks[$Name] = [pscustomobject]@{
            State = 'Ready'
            Enabled = [bool] $Definition.Enabled
            Actions = @($Definition.Actions | ForEach-Object { [pscustomobject]@{ Type = $_.Type; Execute = $_.Execute; Arguments = $_.Arguments } })
            Triggers = @($Definition.Triggers | ForEach-Object { [pscustomobject]@{ Type = $_.Type; Enabled = $_.Enabled } })
            LogonType = $Definition.LogonType
            RunLevel = $Definition.RunLevel
            Priority = $Definition.Priority
            ExecutionTimeLimit = $Definition.ExecutionTimeLimit
            MultipleInstances = $Definition.MultipleInstances
            DisallowStartIfOnBatteries = $Definition.DisallowStartIfOnBatteries
            StopIfGoingOnBatteries = $Definition.StopIfGoingOnBatteries
            UserId = $Session.User
        }
    }
    StartTask = {
        param([string] $Name)
        # 任务按动作启动 EXE；EXE 存在时应用渲染首帧，在应用根（app\ 的上一级）写出运行状态回执，形同 C# 的 RuntimeReceipt。
        # 用例可改写这次启动：进程落在哪个会话、写完回执后是否还在运行、是否写出回执。
        $task = $installerCase.Tasks[$Name]
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
    [pscustomobject]@{
        Name = $Name
        PackageRoot = $packageRoot
        Root = $root
        ExePath = Join-Path $root 'app\MinminCountdown.exe'
        TaskName = "Minmin Countdown Installer $Name"
        Sessions = @([pscustomobject]@{ SessionId = 3; User = 'MINMIN-OFFICE\installer-account-sentinel' })
        Processes = @()
        Tasks = @{}
        NextProcessId = 6001
        Launch = [pscustomobject]@{ SessionId = 3; StaysAlive = $true; WritesReceipt = $true }
        OnStopProcess = $null
        OnMoveDirectory = $null
        OnStartTask = $null
        Rehearsal = [pscustomobject]@{ Migration = 'current-schema'; Equivalence = 'migrated' }
        Commands = [System.Collections.Generic.List[object]]::new()
        Observed = @{}
        Json = $null
    }
}

function New-CountdownInstallerTestTask {
    param([string] $ExecutablePath, [string] $State, [string] $UserId)
    [pscustomobject]@{
        State = $State
        Enabled = $true
        Actions = @([pscustomobject]@{ Type = 'Exec'; Execute = $ExecutablePath; Arguments = $null })
        Triggers = @([pscustomobject]@{ Type = 'Logon'; Enabled = $true })
        LogonType = 'Interactive'
        RunLevel = 'Limited'
        Priority = 4
        ExecutionTimeLimit = 'PT0S'
        MultipleInstances = 'IgnoreNew'
        UserId = $UserId
    }
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
    Assert-Equal $true ([bool] $firstInstall.loginTaskCreated) 'First install creates the missing login task'
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
    Assert-Equal $false ([bool] $upgrade.loginTaskCreated) 'Upgrade does not create a login task when one exists'
    Assert-Equal "$($installerCase.TaskName)=Running/existing-task-owner" (Get-CountdownInstallerTestTasks) 'Upgrade keeps the existing login task as it is'
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

    # 前置检查失败：包内文件与载荷清单不符（文件被改、文件缺少）、没有交互会话、有多个交互会话。都在停止旧应用之前拒绝：
    # 除删除候选外安装根没有任何改动（首次安装时连安装根也不留下），应用照常运行，登录任务照旧，也不创建登录任务。
    # “只检查、不改动”同样核对包。上次安装没走完（留有事务标记与旧版本目录）时，中断恢复由 #284 实现，在那之前拒绝。
    $installerRejections = @(
        [pscustomobject]@{ Name = 'reject-tampered-file'; Installed = $true; CheckOnly = $false; Stage = 'preflight'; Reason = 'payload-mismatch'; Arrange = { [System.IO.File]::WriteAllText((Join-Path $installerCase.PackageRoot 'app\MinminCountdown.exe'), 'installer exe v2 tampered', $installerUtf8) } }
        [pscustomobject]@{ Name = 'reject-missing-file'; Installed = $true; CheckOnly = $false; Stage = 'preflight'; Reason = 'payload-mismatch'; Arrange = { [System.IO.File]::Delete((Join-Path $installerCase.PackageRoot 'app\holiday-data\2027.json')) } }
        [pscustomobject]@{ Name = 'reject-no-session'; Installed = $true; CheckOnly = $false; Stage = 'preflight'; Reason = 'no-interactive-session'; Arrange = { $installerCase.Sessions = @() } }
        [pscustomobject]@{ Name = 'reject-two-sessions'; Installed = $true; CheckOnly = $false; Stage = 'preflight'; Reason = 'multiple-interactive-sessions'; Arrange = { $installerCase.Sessions = @($installerCase.Sessions) + @([pscustomobject]@{ SessionId = 5; User = 'MINMIN-OFFICE\second-account' }) } }
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
        $runningBefore = Get-CountdownInstallerTestRunning
        $tasksBefore = Get-CountdownInstallerTestTasks
        $rejected = Invoke-CountdownInstallerTest -CheckOnly:$rejection.CheckOnly
        Assert-Equal "False|$($rejection.Stage)|$($rejection.Reason)" "$($rejected.succeeded)|$($rejected.failedStage)|$($rejected.reason)" "The install is rejected: $($rejection.Name)"
        Assert-Equal $treeBefore (Get-CountdownInstallerTestTree -Root $installerCase.Root -Detailed) "Rejected install leaves the install root as it was: $($rejection.Name)"
        Assert-Equal $runningBefore (Get-CountdownInstallerTestRunning) "Rejected install stops no application: $($rejection.Name)"
        Assert-Equal $tasksBefore (Get-CountdownInstallerTestTasks) "Rejected install leaves the login tasks alone: $($rejection.Name)"
        if ($rejection.Installed) {
            Assert-Equal $installerSettingsFingerprint (Get-CountdownInstallerTestFingerprint -Fingerprint $rejected.settings.after) "Rejected install reports the settings fingerprint after it stopped: $($rejection.Name)"
        }
    }

    # 只检查、不改动：执行到前置检查为止（核对包本身，不放候选），报告将要执行的步骤；包、安装根、应用与登录任务都不变。
    # 已有登录任务时不计划创建它；首次安装时计划创建，安装根也不会被建出来。
    foreach ($checkOnly in @(
            [pscustomobject]@{ Name = 'check-only-upgrade'; Installed = $true; Steps = 'place-candidate,stop-application,write-transaction-marker,replace-program-directory,start-and-wait-ready,commit' }
            [pscustomobject]@{ Name = 'check-only-first-install'; Installed = $false; Steps = 'place-candidate,stop-application,write-transaction-marker,replace-program-directory,create-login-task,start-and-wait-ready,commit' }
        )) {
        $installerCase = New-CountdownInstallerTestCase -Name $checkOnly.Name
        if ($checkOnly.Installed) { Initialize-CountdownInstallerTestInstalledVersion }
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
    }

    # 另一个安装正在进行（持有全机安装锁）时，第二个安装被拒绝：在第一个安装启动新版本的那一刻从另一个线程发起，
    # 它在安装锁处停下、不调用任何 port（它的 port 一被调用就失败），结果里仍有设置指纹；第一个安装照常提交。
    $installerCase = New-CountdownInstallerTestCase -Name 'concurrent'
    Initialize-CountdownInstallerTestInstalledVersion
    $installerCase.OnStartTask = {
        $secondInstall = Start-ThreadJob -ArgumentList $installerScript, $installerCase.PackageRoot, $installerCase.Root, $installerCase.TaskName, $installerLockName -ScriptBlock {
            param($Script, $PackageRoot, $InstallRoot, $TaskName, $LockName)
            $refuse = { throw '第二个安装越过了安装锁。' }
            $ports = [pscustomobject]@{ GetInteractiveSessions = $refuse; GetProcesses = $refuse; StopProcess = $refuse; GetTask = $refuse; RegisterTask = $refuse; StartTask = $refuse; MoveDirectory = $refuse }
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
    # 只查询交互会话、进程与任务，不做任何改动。本机恰有一个交互会话时报告将要执行的步骤（含创建登录任务），否则在前置检查拒绝。
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
        Assert-Equal 'place-candidate,stop-application,write-transaction-marker,replace-program-directory,create-login-task,start-and-wait-ready,commit' (@($installerReal.plannedSteps) -join ',') 'Real check-only plans to create the missing login task'
        $installerBuiltAggregate = [string] ([System.IO.File]::ReadAllText((Join-Path $PSScriptRoot 'app\published-payload.json')) | ConvertFrom-Json).aggregateSha256
        Assert-Equal $installerBuiltAggregate ([string] $installerReal.package.aggregateSha256) 'Real check-only verifies this build output as the package'
    }
    else {
        Assert-Equal $true ("$($installerReal.failedStage)|$($installerReal.reason)" -in @('preflight|no-interactive-session', 'preflight|multiple-interactive-sessions')) "Real check-only fails only on the interactive session count: $($installerReal.failedStage) $($installerReal.reason) $($installerReal.error)"
    }
    Assert-Equal '<missing>' (Get-CountdownInstallerTestTree -Root $installerRealRoot) 'Real check-only creates no install root'
    $installerRealLeaks = @(@($env:COMPUTERNAME, $env:USERNAME) | Where-Object { [string] $_ -and ([string] $_).Length -ge 4 -and $installerRealText.IndexOf([string] $_, [System.StringComparison]::OrdinalIgnoreCase) -ge 0 })
    Assert-Equal 0 $installerRealLeaks.Count 'Real check-only output carries no host name or account'
}
finally {
    Remove-CountdownIsolatedTestTree -Path $installerTestRoot -AllowedRoot $installerTempRoot
}
