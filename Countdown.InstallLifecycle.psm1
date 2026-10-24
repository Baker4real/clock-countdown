#requires -Version 7.0

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# 安装事务生命周期：本机安装器与办公室部署 adapter 共用的 Begin（装入候选）、Accept（事务验收）与
# Rollback（回滚）编排。计划任务、Windows 时间服务、进程与桌面图标经 port 调用：生产使用
# New-CountdownInstallWindowsPorts，测试注入记录调用顺序的 adapter。
Import-Module (Join-Path $PSScriptRoot 'Countdown.Runtime.psm1')
Import-Module (Join-Path $PSScriptRoot 'Countdown.Settings.psm1')
Import-Module (Join-Path $PSScriptRoot 'Countdown.InstallTransaction.psm1') -DisableNameChecking

$script:legacyManagedLauncherHash = 'E5E37A9D20669E4A888969F0DB95560D45D581913B68EA7600055DE50E20AC14'

function Get-CountdownAcceptanceSettingsState {
    param([Parameter(Mandatory)] [string] $Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        return [pscustomobject]@{
            Hash = ''
            Json = $null
            Length = 0L
            LastWriteTimeUtc = ''
        }
    }

    $item = Get-Item -LiteralPath $Path
    [pscustomobject]@{
        Hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $Path).Hash
        Json = [System.IO.File]::ReadAllText($Path, [System.Text.Encoding]::UTF8)
        Length = [long] $item.Length
        LastWriteTimeUtc = $item.LastWriteTimeUtc.ToUniversalTime().ToString('o')
    }
}

function Test-CountdownAcceptanceInteractiveContext {
    param(
        [Parameter(Mandatory)] [object] $Expected,
        [Parameter(Mandatory)] [object] $Actual
    )

    [string]::Equals([string] $Actual.User, [string] $Expected.User, [System.StringComparison]::OrdinalIgnoreCase) -and
        [int] $Actual.SessionId -eq [int] $Expected.SessionId -and
        [string]::Equals([string] $Actual.Desktop, [string] $Expected.Desktop, [System.StringComparison]::OrdinalIgnoreCase)
}

function Assert-CountdownAcceptanceEnvironment {
    param(
        [Parameter(Mandatory)] [string] $Phase,
        [Parameter(Mandatory)] [object] $ExpectedInteractiveContext,
        [Parameter(Mandatory)] [int] $ExpectedProcessId,
        [Parameter(Mandatory)] [string] $RuntimeStatusPath,
        [Parameter(Mandatory)] [string] $LaunchToken,
        [Parameter(Mandatory)] [int] $ExpectedSessionId,
        [Parameter(Mandatory)] [string] $ExpectedApplicationPath,
        [Parameter(Mandatory)] [string] $TaskName,
        [Parameter(Mandatory)] [ValidateNotNullOrEmpty()] [string] $ExpectedScheduledTaskIdentity,
        [Parameter(Mandatory)] [scriptblock] $GetInteractiveContext,
        [Parameter(Mandatory)] [scriptblock] $GetScheduledTask,
        [Parameter(Mandatory)] [scriptblock] $GetScheduledTaskIdentity,
        [Parameter(Mandatory)] [scriptblock] $GetScheduledTaskInfo,
        [AllowNull()] [scriptblock] $ProcessProbe,
        [switch] $SkipRuntimeReceipt
    )

    $actualContext = & $GetInteractiveContext
    if ($null -eq $actualContext -or -not (Test-CountdownAcceptanceInteractiveContext -Expected $ExpectedInteractiveContext -Actual $actualContext)) {
        throw "${Phase}交互用户、会话或桌面路径已变化；拒绝提交事务。"
    }

    if (-not $SkipRuntimeReceipt) {
        $runtimeArguments = @{
            Path = $RuntimeStatusPath
            LaunchToken = $LaunchToken
            ExpectedSessionId = $ExpectedSessionId
            ExpectedApplicationPath = $ExpectedApplicationPath
            TimeoutSeconds = 0
            PollMilliseconds = 1
        }
        if ($null -ne $ProcessProbe) { $runtimeArguments.ProcessProbe = $ProcessProbe }
        $runtimeStatus = Wait-CountdownWindowReady @runtimeArguments
        if ([int] $runtimeStatus.processId -ne $ExpectedProcessId) {
            throw "${Phase}受管应用进程已变化；拒绝提交事务。"
        }
    }

    $task = & $GetScheduledTask $TaskName
    $taskInfo = & $GetScheduledTaskInfo $TaskName
    $taskState = if ($null -eq $task) { 'Missing' } else { [string] $task.State }
    $lastTaskResult = if ($null -eq $taskInfo -or $null -eq $taskInfo.LastTaskResult) {
        $null
    }
    else {
        [long] $taskInfo.LastTaskResult
    }
    if ($null -eq $task -or $taskState -ne 'Ready' -or $null -eq $lastTaskResult -or $lastTaskResult -ne 0) {
        throw "${Phase}计划任务未回到 Ready 且 lastTaskResult=0：state=$taskState lastTaskResult=$lastTaskResult"
    }

    try {
        $taskIdentity = [string] (& $GetScheduledTaskIdentity $task)
    }
    catch {
        throw "${Phase}计划任务定义无法读取：$($_.Exception.Message)"
    }
    if (-not [string]::Equals($taskIdentity, $ExpectedScheduledTaskIdentity, [System.StringComparison]::Ordinal)) {
        throw "${Phase}计划任务定义已变化；拒绝提交事务。"
    }

    [pscustomobject]@{
        Task = $task
        State = $taskState
        LastTaskResult = [long] $lastTaskResult
    }
}

function Invoke-CountdownAcceptanceEnvironmentAssertion {
    param(
        [Parameter(Mandatory)] [hashtable] $Context,
        [Parameter(Mandatory)] [string] $Phase,
        [switch] $SkipRuntimeReceipt
    )

    $arguments = @{
        Phase = $Phase
        ExpectedInteractiveContext = $Context.ExpectedInteractiveContext
        ExpectedProcessId = $Context.ExpectedProcessId
        RuntimeStatusPath = $Context.RuntimeStatusPath
        LaunchToken = $Context.LaunchToken
        ExpectedSessionId = $Context.ExpectedSessionId
        ExpectedApplicationPath = $Context.ExpectedApplicationPath
        TaskName = $Context.TaskName
        ExpectedScheduledTaskIdentity = $Context.ExpectedScheduledTaskIdentity
        GetInteractiveContext = $Context.GetInteractiveContext
        GetScheduledTask = $Context.GetScheduledTask
        GetScheduledTaskIdentity = $Context.GetScheduledTaskIdentity
        GetScheduledTaskInfo = $Context.GetScheduledTaskInfo
    }
    if ($null -ne $Context.ProcessProbe) { $arguments.ProcessProbe = $Context.ProcessProbe }
    if ($SkipRuntimeReceipt) { $arguments.SkipRuntimeReceipt = $true }
    Assert-CountdownAcceptanceEnvironment @arguments
}

function Invoke-CountdownTransactionAcceptance {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [string] $StartupRequestPath,
        [Parameter(Mandatory)] [string] $RuntimeStatusPath,
        [Parameter(Mandatory)] [string] $AcceptanceStatusPath,
        [Parameter(Mandatory)] [string] $LaunchToken,
        [Parameter(Mandatory)] [object] $ExpectedInteractiveContext,
        [Parameter(Mandatory)] [ValidateRange(1, 2147483647)] [int] $ExpectedProcessId,
        [Parameter(Mandatory)] [ValidateRange(0, 2147483647)] [int] $ExpectedSessionId,
        [Parameter(Mandatory)] [string] $ExpectedApplicationPath,
        [Parameter(Mandatory)] [string] $TaskName,
        [Parameter(Mandatory)] [ValidateNotNullOrEmpty()] [string] $ExpectedScheduledTaskIdentity,
        [Parameter(Mandatory)] [string] $SettingsSnapshotPath,
        [Parameter(Mandatory)] [string] $SettingsPath,
        [Parameter(Mandatory)] [scriptblock] $GetInteractiveContext,
        [Parameter(Mandatory)] [scriptblock] $GetScheduledTask,
        [Parameter(Mandatory)] [scriptblock] $GetScheduledTaskIdentity,
        [Parameter(Mandatory)] [scriptblock] $GetScheduledTaskInfo,
        [Parameter(Mandatory)] [scriptblock] $CompleteTransaction,
        [Parameter(Mandatory)] [scriptblock] $RemoveCommittedTransaction,
        [AllowNull()] [scriptblock] $ProcessProbe,
        [ValidateRange(0, 300)] [int] $ReadyTimeoutSeconds = 30,
        [ValidateRange(1, 5000)] [int] $PollMilliseconds = 100
    )

    $migrationUtc = Get-CountdownStartupRequestMigrationUtc `
        -Path $StartupRequestPath `
        -LaunchToken $LaunchToken
    $settingsBaseline = Get-CountdownAcceptanceSettingsState -Path $SettingsSnapshotPath
    $settingsBefore = $null
    $settingsAfter = $null
    $settingsMutex = $null
    $environmentContext = @{
        ExpectedInteractiveContext = $ExpectedInteractiveContext
        ExpectedProcessId = $ExpectedProcessId
        RuntimeStatusPath = $RuntimeStatusPath
        LaunchToken = $LaunchToken
        ExpectedSessionId = $ExpectedSessionId
        ExpectedApplicationPath = $ExpectedApplicationPath
        TaskName = $TaskName
        ExpectedScheduledTaskIdentity = $ExpectedScheduledTaskIdentity
        GetInteractiveContext = $GetInteractiveContext
        GetScheduledTask = $GetScheduledTask
        GetScheduledTaskIdentity = $GetScheduledTaskIdentity
        GetScheduledTaskInfo = $GetScheduledTaskInfo
        ProcessProbe = $ProcessProbe
    }
    try {
        $settingsMutex = Enter-CountdownSettingsWriteMutex -Path $SettingsPath -TimeoutSeconds 10
        $settingsBefore = Get-CountdownAcceptanceSettingsState -Path $SettingsPath
        if (-not (Test-CountdownSettingsDeploymentTransition -BeforeHash $settingsBaseline.Hash -AfterHash $settingsBefore.Hash -BeforeJson $settingsBaseline.Json -AfterJson $settingsBefore.Json -MigrationUtc $migrationUtc)) {
            throw '验收前 settings.json 超出允许的数据等价范围。'
        }

        [void] (Invoke-CountdownAcceptanceEnvironmentAssertion `
            -Context $environmentContext `
            -Phase '验收提交前')

        if (-not (Complete-CountdownStartupRequest -Path $StartupRequestPath -LaunchToken $LaunchToken)) {
            throw '验收事务仍可回滚，但无法授权应用持久化 schema 迁移。'
        }
    }
    finally {
        if ($null -ne $settingsMutex) {
            Exit-CountdownSettingsWriteMutex -Mutex $settingsMutex
            $settingsMutex = $null
        }
    }

    [void] (Wait-CountdownStartupRequestState -Path $StartupRequestPath -LaunchToken $LaunchToken -ExpectedState ready -TimeoutSeconds $ReadyTimeoutSeconds -PollMilliseconds $PollMilliseconds)

    try {
        $settingsMutex = Enter-CountdownSettingsWriteMutex -Path $SettingsPath -TimeoutSeconds 10
        $settingsAfter = Get-CountdownAcceptanceSettingsState -Path $SettingsPath
        if (-not (Test-CountdownSettingsDeploymentTransition -BeforeHash $settingsBaseline.Hash -AfterHash $settingsAfter.Hash -BeforeJson $settingsBaseline.Json -AfterJson $settingsAfter.Json -MigrationUtc $migrationUtc)) {
            throw 'C# schema 迁移超出允许的数据等价范围。'
        }

        [void] (Invoke-CountdownAcceptanceEnvironmentAssertion `
            -Context $environmentContext `
            -Phase 'schema 迁移确认后')

        [void] (& $CompleteTransaction)
        if (-not (Set-CountdownStartupRequestFinalized -Path $StartupRequestPath -LaunchToken $LaunchToken)) {
            throw '验收已接受且仍可回滚，但无法最终启用应用窗口。'
        }

        $acceptanceArguments = @{
            Path = $AcceptanceStatusPath
            LaunchToken = $LaunchToken
            ExpectedProcessId = $ExpectedProcessId
            ExpectedSessionId = $ExpectedSessionId
            ExpectedApplicationPath = $ExpectedApplicationPath
            TimeoutSeconds = $ReadyTimeoutSeconds
            PollMilliseconds = $PollMilliseconds
        }
        if ($null -ne $ProcessProbe) { $acceptanceArguments.ProcessProbe = $ProcessProbe }
        $acceptanceStatus = Wait-CountdownAcceptanceReadyReceipt @acceptanceArguments
        if ([int] $acceptanceStatus.processId -ne $ExpectedProcessId) {
            throw '应用解锁确认的进程与 window-ready 进程不一致。'
        }

        $finalEnvironment = Invoke-CountdownAcceptanceEnvironmentAssertion `
            -Context $environmentContext `
            -Phase '应用解锁确认后' `
            -SkipRuntimeReceipt

        [void] (& $RemoveCommittedTransaction)
        $settingsUnchanged = [string] $settingsBefore.Hash -eq [string] $settingsAfter.Hash -and
            $settingsBefore.Length -eq $settingsAfter.Length -and
            [string] $settingsBefore.LastWriteTimeUtc -eq [string] $settingsAfter.LastWriteTimeUtc
        [pscustomobject]@{
            AcceptanceState = 'finalized-ready'
            RuntimeProcessId = $ExpectedProcessId
            TaskState = [string] $finalEnvironment.State
            LastTaskResult = [long] $finalEnvironment.LastTaskResult
            SettingsHashBefore = $settingsBefore.Hash
            SettingsHashAfter = $settingsAfter.Hash
            SettingsLengthBefore = $settingsBefore.Length
            SettingsLengthAfter = $settingsAfter.Length
            SettingsMtimeUtcBefore = $settingsBefore.LastWriteTimeUtc
            SettingsMtimeUtcAfter = $settingsAfter.LastWriteTimeUtc
            SettingsUnchanged = [bool] $settingsUnchanged
        }
    }
    finally {
        if ($null -ne $settingsMutex) {
            Exit-CountdownSettingsWriteMutex -Mutex $settingsMutex
        }
    }
}

function Wait-CountdownNetworkTimeSource {
    param(
        [ValidateRange(1, 120)] [int] $TimeoutSeconds = 30,
        [ValidateRange(50, 5000)] [int] $PollMilliseconds = 500,
        [scriptblock] $QueryTimeSourceAction
    )

    if ($null -eq $QueryTimeSourceAction) {
        $QueryTimeSourceAction = {
            $source = (w32tm.exe /query /source | Out-String).Trim()
            [pscustomobject]@{ ExitCode = $LASTEXITCODE; Source = $source }
        }
    }
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)
    $lastResult = '未返回时间源'
    do {
        $probe = & $QueryTimeSourceAction
        $exitCode = if ($null -eq $probe) { -1 } else { [int] $probe.ExitCode }
        $source = if ($null -eq $probe) { '' } else { [string] $probe.Source }
        if ($exitCode -eq 0 -and -not [string]::IsNullOrWhiteSpace($source) -and $source -notmatch '(?i)Local CMOS|Free-running|VM IC') {
            return $source
        }
        $lastResult = "exit=$exitCode; source=$source"
        if ([DateTimeOffset]::UtcNow -ge $deadline) { break }
        Start-Sleep -Milliseconds $PollMilliseconds
    } while ($true)
    throw "Windows Time did not report a network source within $TimeoutSeconds seconds: $lastResult"
}

function New-CountdownInstallWindowsPorts {
    [pscustomobject]@{
        GetInteractiveContext = { Get-CountdownInteractiveDesktopContext }
        EnableNetworkTime = {
            Set-Service -Name W32Time -StartupType Automatic
            Start-Service -Name W32Time
            $resync = (w32tm.exe /resync /rediscover | Out-String).Trim()
            if ($LASTEXITCODE -ne 0) {
                throw "Windows Time resync failed: $resync"
            }
            $source = Wait-CountdownNetworkTimeSource -TimeoutSeconds 30 -PollMilliseconds 500
            $status = (w32tm.exe /query /status | Out-String).Trim()
            if ($LASTEXITCODE -ne 0) {
                throw 'Windows Time status query failed.'
            }
            [pscustomobject]@{ Source = $source; Status = $status }
        }
        # $null 表示回滚由事务 module 按 journal 里的服务快照自行恢复 Windows 时间服务。
        RestoreNetworkTime = $null
        StopApplication = {
            param([object] $Layout)
            Stop-CountdownManagedApplication -InstallPath $Layout.InstallPath -TaskName $Layout.TaskName -LegacyTaskName $Layout.LegacyTaskName -FreezeFutureStarts
        }
        RemoveScheduledTask = {
            param([string] $TaskName)
            [void] (Remove-CountdownScheduledTaskIfPresent -TaskName $TaskName)
        }
        RegisterScheduledTask = {
            param([object] $Layout, [string] $PowerShell7Path, [string] $InteractiveUser)
            $action = New-ScheduledTaskAction -Execute $PowerShell7Path -Argument $Layout.TaskArguments -WorkingDirectory $Layout.InstallPath
            $trigger = New-ScheduledTaskTrigger -AtLogOn -User $InteractiveUser
            $principal = New-ScheduledTaskPrincipal -UserId $InteractiveUser -LogonType Interactive -RunLevel Limited
            $settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries `
                -ExecutionTimeLimit ([TimeSpan]::Zero) -MultipleInstances IgnoreNew
            Register-ScheduledTask -TaskName $Layout.TaskName -Action $action -Trigger $trigger -Principal $principal `
                -Settings $settings -Description '登录后启动敏敏子倒计时，按北京时间计算。' -Force | Out-Null
            Get-ScheduledTask -TaskName $Layout.TaskName -ErrorAction Stop
        }
        StartScheduledTask = {
            param([string] $TaskName)
            Start-ScheduledTask -TaskName $TaskName
        }
        GetScheduledTask = {
            param([string] $TaskName)
            Get-ScheduledTask -TaskName $TaskName -ErrorAction Stop
        }
        GetScheduledTaskIdentity = {
            param([object] $ScheduledTask)
            [string] (Export-ScheduledTask -InputObject $ScheduledTask -ErrorAction Stop)
        }
        GetScheduledTaskInfo = {
            param([string] $TaskName)
            Get-ScheduledTaskInfo -TaskName $TaskName -ErrorAction Stop
        }
        # $null 表示窗口就绪与解锁确认按真实进程核对 PID、会话与应用路径。
        ProcessProbe = $null
        RefreshDesktopIcons = { & "$env:SystemRoot\System32\ie4uinit.exe" -show 2>$null }
    }
}

function Get-CountdownInstallTransactionIdentity {
    param([Parameter(Mandatory)] [object] $Layout)

    @{
        ExpectedInstallPath = [string] $Layout.InstallPath
        ExpectedTaskName = [string] $Layout.TaskName
        ExpectedLegacyTaskName = [string] $Layout.LegacyTaskName
        ExpectedLauncherPath = [string] $Layout.LauncherPath
        ExpectedLegacyLauncherPath = [string] $Layout.LegacyLauncherPath
        ExpectedLauncherTempPath = [string] $Layout.LauncherTempPath
    }
}

function Assert-CountdownInstallInteractiveContextStable {
    param(
        [Parameter(Mandatory)] [object] $ExpectedContext,
        [Parameter(Mandatory)] [object] $Ports
    )

    $actualContext = & $Ports.GetInteractiveContext
    if ($null -eq $actualContext -or -not (Test-CountdownAcceptanceInteractiveContext -Expected $ExpectedContext -Actual $actualContext)) {
        throw '安装事务开始前交互用户、会话或桌面路径已变化；未创建安装事务。'
    }
    $actualContext
}

function Get-CountdownManagedDesktopLauncherOwnership {
    param(
        [Parameter(Mandatory)] [string] $LauncherPath,
        [Parameter(Mandatory)] [string] $LauncherStatePath,
        [Parameter(Mandatory)] [System.Collections.IDictionary] $Expected
    )

    if (-not (Test-Path -LiteralPath $LauncherPath -PathType Leaf)) {
        return [pscustomobject]@{ Exists = $false; Sha256 = $null; Managed = $true }
    }
    $currentLauncherHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $LauncherPath).Hash
    $managed = $false
    if (Test-Path -LiteralPath $LauncherStatePath -PathType Leaf) {
        $launcherStateJson = [System.IO.File]::ReadAllText($LauncherStatePath, [System.Text.Encoding]::UTF8)
        $previousLauncherState = $launcherStateJson | ConvertFrom-Json
        $pathMatches = [string]::Equals([string] $previousLauncherState.desktopPath, $LauncherPath, [System.StringComparison]::OrdinalIgnoreCase)
        $hashMatches = $currentLauncherHash -eq $previousLauncherState.desktopSha256 -or $currentLauncherHash -eq $Expected['CountdownLauncher.exe']
        $managed = $pathMatches -and $hashMatches
    }
    else {
        $managed = $currentLauncherHash -eq $script:legacyManagedLauncherHash
    }
    [pscustomobject]@{ Exists = $true; Sha256 = $currentLauncherHash; Managed = [bool] $managed }
}

function Restore-CountdownInterruptedInstallation {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [object] $Layout,
        [Parameter(Mandatory)] [string] $PowerShell7Path,
        [object] $Ports = (New-CountdownInstallWindowsPorts)
    )

    $identity = Get-CountdownInstallTransactionIdentity -Layout $Layout
    $identity.Remove('ExpectedInstallPath')
    Recover-CountdownInstallTransactions -InstallPath $Layout.InstallPath -PowerShell7Path $PowerShell7Path -RestoreWindowsTimeAction $Ports.RestoreNetworkTime @identity
}

function Undo-CountdownInstallation {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [object] $Layout,
        [Parameter(Mandatory)] [string] $TransactionPath,
        [Parameter(Mandatory)] [string] $PowerShell7Path,
        [object] $Ports = (New-CountdownInstallWindowsPorts)
    )

    $identity = Get-CountdownInstallTransactionIdentity -Layout $Layout
    # 任意失败都优先恢复应用、任务与时间服务；若用户在事务窗口替换桌面文件，只保留该文件和可审计 journal。
    $rollback = Undo-CountdownInstallTransaction -TransactionPath $TransactionPath -PowerShell7Path $PowerShell7Path -RestoreWindowsTimeAction $Ports.RestoreNetworkTime -PreserveUnmanagedLaunchers @identity
    [pscustomobject]@{
        PreservedUnmanagedLaunchers = $null -ne $rollback -and [bool] $rollback.PreservedUnmanagedLaunchers
    }
}

function Complete-CountdownInstallation {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [object] $Layout,
        [Parameter(Mandatory)] [string] $TransactionPath,
        [Parameter(Mandatory)] [object] $InteractiveContext,
        [Parameter(Mandatory)] [string] $LaunchToken,
        [Parameter(Mandatory)] [ValidateRange(1, 2147483647)] [int] $ExpectedProcessId,
        [Parameter(Mandatory)] [ValidateNotNullOrEmpty()] [string] $ExpectedScheduledTaskIdentity,
        [scriptblock] $OnStage,
        [scriptblock] $OnCommitted,
        [object] $Ports = (New-CountdownInstallWindowsPorts)
    )

    $identity = Get-CountdownInstallTransactionIdentity -Layout $Layout
    $completeTransaction = {
        if ($null -ne $OnStage) { & $OnStage '提交安装事务' }
        Complete-CountdownInstallTransaction -TransactionPath $TransactionPath @identity
    }
    $removeCommittedTransaction = {
        if ($null -ne $OnStage) { & $OnStage '清理已提交事务' }
        $moveCommittedTransaction = {
            param([string] $Source, [string] $Destination)
            # InstallTransaction 只在 journal 写入 committed 后调用此动作；移动失败也已不可回滚。
            if ($null -ne $OnCommitted) { & $OnCommitted }
            Move-Item -LiteralPath $Source -Destination $Destination -ErrorAction Stop
        }
        Remove-CountdownCommittedInstallTransaction -TransactionPath $TransactionPath -MoveAction $moveCommittedTransaction @identity
    }
    $acceptanceArguments = @{
        StartupRequestPath = $Layout.StartupRequestPath
        RuntimeStatusPath = $Layout.RuntimeStatusPath
        AcceptanceStatusPath = $Layout.AcceptanceStatusPath
        LaunchToken = $LaunchToken
        ExpectedInteractiveContext = $InteractiveContext
        ExpectedProcessId = $ExpectedProcessId
        ExpectedSessionId = [int] $InteractiveContext.SessionId
        ExpectedApplicationPath = $Layout.ApplicationPath
        TaskName = $Layout.TaskName
        ExpectedScheduledTaskIdentity = $ExpectedScheduledTaskIdentity
        SettingsSnapshotPath = Join-Path $TransactionPath 'install-backup\settings.json'
        SettingsPath = $Layout.SettingsPath
        GetInteractiveContext = $Ports.GetInteractiveContext
        GetScheduledTask = $Ports.GetScheduledTask
        GetScheduledTaskIdentity = $Ports.GetScheduledTaskIdentity
        GetScheduledTaskInfo = $Ports.GetScheduledTaskInfo
        CompleteTransaction = $completeTransaction
        RemoveCommittedTransaction = $removeCommittedTransaction
    }
    if ($null -ne $Ports.ProcessProbe) { $acceptanceArguments.ProcessProbe = $Ports.ProcessProbe }
    Invoke-CountdownTransactionAcceptance @acceptanceArguments
}

function Invoke-CountdownInstallation {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [object] $Layout,
        [Parameter(Mandatory)] [string] $StagingRoot,
        [Parameter(Mandatory)] [object] $ReleaseContract,
        [Parameter(Mandatory)] [string] $PowerShell7Path,
        [Parameter(Mandatory)] [object] $InteractiveContext,
        [Parameter(Mandatory)] [string] $InventoryEvidenceRoot,
        [string] $DeferredReceiptPath,
        [object] $Ports = (New-CountdownInstallWindowsPorts)
    )

    # 带延迟回执时只装到 window-ready 并保留待验收事务，由办公室部署 adapter 完成 Accept；否则就地验收。
    $deferCommit = -not [string]::IsNullOrWhiteSpace($DeferredReceiptPath)
    $expected = $ReleaseContract.IntegrityHashes
    $install = $Layout.InstallPath
    $launcherPath = $Layout.LauncherPath
    $launcherStatePath = $Layout.LauncherStatePath
    $legacyLauncherPath = $Layout.LegacyLauncherPath
    $identity = Get-CountdownInstallTransactionIdentity -Layout $Layout
    $installMutex = $null
    $installStage = 'prepare-transaction'
    $transactionStarted = $false
    try {
        # 即使父验收已持锁也再次获取同线程可重入 mutex；公开参数不能跳过真实安装锁。
        $installMutex = Enter-CountdownInstallTransactionMutex -TimeoutSeconds 120
        $InteractiveContext = Assert-CountdownInstallInteractiveContextStable -ExpectedContext $InteractiveContext -Ports $Ports
        # 先按 journal 恢复崩溃留下的候选启动器；否则候选 exe 与旧 launcher-state 的哈希不一致，会永久阻断恢复。
        $recoveryResult = Restore-CountdownInterruptedInstallation -Layout $Layout -PowerShell7Path $PowerShell7Path -Ports $Ports
        $recoveredTransactions = @($recoveryResult.RecoveredTransactionPaths)
        if (@($recoveryResult.UnresolvedCommittedTransactions).Count -gt 0) {
            throw '检测到已提交但未完成应用解锁确认的事务；已保留回滚证据，拒绝继续安装。'
        }
        if (@($recoveryResult.UnresolvedLauncherOwnershipConflicts).Count -gt 0) {
            throw '检测到用户替换的桌面启动器：应用、任务和时间服务已恢复，但事务证据被保留，拒绝继续安装。'
        }
        $initialLauncherOwnership = Get-CountdownManagedDesktopLauncherOwnership -LauncherPath $launcherPath -LauncherStatePath $launcherStatePath -Expected $expected
        if (-not [bool] $initialLauncherOwnership.Managed) {
            throw "Refusing to overwrite an unmanaged desktop launcher: $launcherPath"
        }
        $InteractiveContext = Assert-CountdownInstallInteractiveContextStable -ExpectedContext $InteractiveContext -Ports $Ports
        $transaction = Start-CountdownInstallTransaction -InstallPath $install -LauncherPath $launcherPath -LegacyLauncherPath $legacyLauncherPath -TaskName $Layout.TaskName -LegacyTaskName $Layout.LegacyTaskName
        $transactionStarted = $true
        $transactionCompleted = $false

        try {
            $installStage = 'configure-network-time'
            $networkTime = & $Ports.EnableNetworkTime

            $installStage = 'quiesce-existing-application'
            [void] (& $Ports.StopApplication $Layout)
            # 旧进程已经静止后才捕获可变 settings/runtime 与桌面文件；任务原状态仍取自 prepared journal。
            $transaction = Capture-CountdownInstallPayloadSnapshot -TransactionPath $transaction.transactionPath @identity
            [void] (& $Ports.RemoveScheduledTask $Layout.LegacyTaskName)
            $install = Assert-CountdownInstallPathOnDDrive -Path $install
            [void] (Assert-CountdownInstallTreeHasNoReparsePoints -Path $install)
            if (-not (Test-Path -LiteralPath $install -PathType Container)) {
                New-Item -ItemType Directory -Path $install -Force | Out-Null
            }
            $runtimeStatusPath = Assert-CountdownInstallPathOnDDrive -Path $Layout.RuntimeStatusPath
            $acceptanceStatusPath = Assert-CountdownInstallPathOnDDrive -Path $Layout.AcceptanceStatusPath
            $startupRequestPath = Assert-CountdownInstallPathOnDDrive -Path $Layout.StartupRequestPath
            Remove-Item -LiteralPath $runtimeStatusPath -Force -ErrorAction SilentlyContinue
            Remove-Item -LiteralPath $acceptanceStatusPath -Force -ErrorAction SilentlyContinue
            $startupRequest = New-CountdownStartupRequest -Path $startupRequestPath -AcceptanceReadOnly

            $installStage = 'copy-payload'
            foreach ($file in @($ReleaseContract.InstalledFiles)) {
                $source = Assert-CountdownInstallPathOnDDrive -Path (Join-Path $StagingRoot $file)
                $destination = Assert-CountdownInstallPathOnDDrive -Path (Join-Path $install $file)
                $destinationParent = Assert-CountdownInstallPathOnDDrive -Path (Split-Path -Parent $destination)
                New-Item -ItemType Directory -Path $destinationParent -Force | Out-Null
                [void] (Assert-CountdownInstallPathOnDDrive -Path $destination)
                Copy-Item -LiteralPath $source -Destination $destination -Force
            }
            foreach ($file in @($ReleaseContract.RetiredRuntimeFiles)) {
                $retiredPath = Assert-CountdownInstallPathOnDDrive -Path (Join-Path $install $file)
                if (Test-Path -LiteralPath $retiredPath -PathType Leaf) {
                    Remove-Item -LiteralPath $retiredPath -Force
                }
            }

            # 候选启动器放在受管事务目录，桌面上的 .new 仅作为用户已有文件的快照目标，绝不作为安装候选。
            $candidateLauncherPath = Assert-CountdownInstallPathOnDDrive -Path (Join-Path $transaction.transactionPath 'candidate-launcher.exe')
            $installedLauncherSourcePath = Assert-CountdownInstallPathOnDDrive -Path $Layout.InstalledLauncherPath
            Copy-Item -LiteralPath $installedLauncherSourcePath -Destination $candidateLauncherPath -Force
            $candidateLauncherHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $candidateLauncherPath).Hash
            if ($candidateLauncherHash -ne $expected['CountdownLauncher.exe']) {
                Remove-Item -LiteralPath $candidateLauncherPath -Force
                throw 'Transaction launcher candidate hash mismatch.'
            }
            # Move 前持久化候选哈希；崩溃恢复只会覆盖候选文件、原备份或缺失路径，绝不静默覆盖用户后续替换的启动器。
            $transaction = Set-CountdownInstallTransactionCandidateLauncherHash -TransactionPath $transaction.transactionPath -CandidateLauncherSha256 $candidateLauncherHash @identity
            # 将所有慢操作放在此前完成；紧贴替换前比较初始哈希，准备期间的任意 launcher 替换都会安全回滚。
            $preparedLauncherOwnership = Get-CountdownManagedDesktopLauncherOwnership -LauncherPath $launcherPath -LauncherStatePath $launcherStatePath -Expected $expected
            if (-not [bool] $preparedLauncherOwnership.Managed -or
                [bool] $preparedLauncherOwnership.Exists -ne [bool] $initialLauncherOwnership.Exists -or
                -not [string]::Equals([string] $preparedLauncherOwnership.Sha256, [string] $initialLauncherOwnership.Sha256, [System.StringComparison]::OrdinalIgnoreCase)) {
                throw "Refusing to overwrite a desktop launcher changed while installation was preparing: $launcherPath"
            }
            $candidateLauncherHashBeforeMove = if (Test-Path -LiteralPath $candidateLauncherPath -PathType Leaf) { (Get-FileHash -Algorithm SHA256 -LiteralPath $candidateLauncherPath).Hash } else { $null }
            if ($candidateLauncherHashBeforeMove -ne $candidateLauncherHash) {
                throw 'Transaction launcher candidate changed before desktop replacement.'
            }
            $candidateLauncherPath = Assert-CountdownInstallPathOnDDrive -Path $candidateLauncherPath
            $launcherPath = Assert-CountdownInstallPathOnDDrive -Path $launcherPath
            $installStage = 'replace-desktop-launcher'
            Move-Item -LiteralPath $candidateLauncherPath -Destination $launcherPath -Force
            if (-not (Test-Path -LiteralPath $launcherPath -PathType Leaf)) {
                throw 'Desktop launcher was not created.'
            }
            $installedLauncherHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $launcherPath).Hash
            if ($installedLauncherHash -ne $candidateLauncherHash) {
                throw 'Desktop launcher hash mismatch after replacement.'
            }
            $launcherState = [ordered]@{ schemaVersion = 1; desktopPath = $launcherPath; desktopSha256 = $candidateLauncherHash } | ConvertTo-Json
            $launcherStatePath = Assert-CountdownInstallPathOnDDrive -Path $launcherStatePath
            $launcherStateTempPath = Assert-CountdownInstallPathOnDDrive -Path "$launcherStatePath.tmp"
            $installStage = 'write-launcher-state'
            [System.IO.File]::WriteAllText($launcherStateTempPath, $launcherState, (New-Object System.Text.UTF8Encoding($false)))
            Move-Item -LiteralPath $launcherStateTempPath -Destination $launcherStatePath -Force
            $legacyLauncherPath = Assert-CountdownInstallPathOnDDrive -Path $legacyLauncherPath
            if (Test-Path -LiteralPath $legacyLauncherPath -PathType Leaf) {
                $legacyExpected = "@echo off`r`nC:\Windows\System32\schtasks.exe /Run /TN `"$($Layout.TaskName)`" >nul 2>&1`r`nexit /b %errorlevel%`r`n"
                $legacyActual = [System.IO.File]::ReadAllText($legacyLauncherPath, [System.Text.Encoding]::ASCII)
                if ($legacyActual -eq $legacyExpected) {
                    Remove-Item -LiteralPath $legacyLauncherPath -Force
                }
            }

            $installStage = 'register-task'
            $registeredTask = & $Ports.RegisterScheduledTask $Layout $PowerShell7Path ([string] $InteractiveContext.User)
            if (-not [string]::Equals(
                    [System.IO.Path]::GetFullPath([string] $registeredTask.Actions.Execute),
                    [System.IO.Path]::GetFullPath($PowerShell7Path),
                    [System.StringComparison]::OrdinalIgnoreCase
                )) {
                throw "计划任务未使用 PowerShell 7：$($registeredTask.Actions.Execute)"
            }
            $expectedScheduledTaskIdentity = [string] (& $Ports.GetScheduledTaskIdentity $registeredTask)

            $installStage = 'wait-window-ready'
            [void] (& $Ports.StartScheduledTask $Layout.TaskName)
            $windowReadyArguments = @{
                Path = $runtimeStatusPath
                LaunchToken = $startupRequest.launchToken
                ExpectedSessionId = [int] $InteractiveContext.SessionId
                ExpectedApplicationPath = $Layout.ApplicationPath
                TimeoutSeconds = 30
                PollMilliseconds = 250
            }
            if ($null -ne $Ports.ProcessProbe) { $windowReadyArguments.ProcessProbe = $Ports.ProcessProbe }
            $runtimeStatus = Wait-CountdownWindowReady @windowReadyArguments
            [void] [System.IO.Directory]::CreateDirectory($InventoryEvidenceRoot)
            $inventoryEvidencePath = Join-Path $InventoryEvidenceRoot ('installed-files-{0}.json' -f [guid]::NewGuid().ToString('N'))
            $installedInventory = @(Get-ChildItem -LiteralPath $install -File -Recurse | Select-Object FullName, Length, @{n='Hash';e={(Get-FileHash -Algorithm SHA256 -LiteralPath $_.FullName).Hash}})
            [System.IO.File]::WriteAllText($inventoryEvidencePath, ($installedInventory | ConvertTo-Json -Depth 4), [System.Text.UTF8Encoding]::new($false))
            $result = [ordered]@{}
            $result.InstalledFileCount = [int] $installedInventory.Count
            $result.InstalledFilesEvidencePath = $inventoryEvidencePath
            $result.Launcher = Get-Item -LiteralPath $launcherPath | Select-Object FullName, Length
            $result.Task = & $Ports.GetScheduledTask $Layout.TaskName | Select-Object TaskName, State, @{n='Execute';e={$_.Actions.Execute}}, @{n='Arguments';e={$_.Actions.Arguments}}, @{n='LogonType';e={$_.Principal.LogonType}}
            $result.TaskInfo = & $Ports.GetScheduledTaskInfo $Layout.TaskName | Select-Object LastRunTime, LastTaskResult, NextRunTime
            $result.RuntimeStatus = $runtimeStatus
            $result.LaunchToken = $startupRequest.launchToken
            $result.InteractiveSessionId = [int] $InteractiveContext.SessionId
            $result.TimeSource = $networkTime.Source
            $result.TimeStatus = $networkTime.Status
            $result.RecoveredTransactions = $recoveredTransactions
            $result.DeferredCommit = $deferCommit
            $result.TransactionPath = if ($deferCommit) { $transaction.transactionPath } else { $null }
            if ($deferCommit) {
                $installStage = 'write-deferred-receipt'
                [void] (Write-CountdownDeferredInstallReceipt -Path $DeferredReceiptPath -TransactionPath $transaction.transactionPath -LaunchToken $startupRequest.launchToken -InteractiveSessionId ([int] $InteractiveContext.SessionId))
                $result.DeferredReceiptPath = [System.IO.Path]::GetFullPath($DeferredReceiptPath)
                return $result
            }

            $installStage = 'transaction-acceptance'
            [void] (Complete-CountdownInstallation `
                    -Layout $Layout `
                    -TransactionPath $transaction.transactionPath `
                    -InteractiveContext $InteractiveContext `
                    -LaunchToken $startupRequest.launchToken `
                    -ExpectedProcessId ([int] $runtimeStatus.processId) `
                    -ExpectedScheduledTaskIdentity $expectedScheduledTaskIdentity `
                    -Ports $Ports)
            $transactionCompleted = $true
            $null = & $Ports.RefreshDesktopIcons
            return $result
        }
        catch {
            $failureRecord = $_
            $failure = $failureRecord.Exception.Message
            $failurePosition = if ($null -eq $failureRecord.InvocationInfo) { '' } else { [string] $failureRecord.InvocationInfo.PositionMessage }
            if ([string]::IsNullOrWhiteSpace($failurePosition)) { $failurePosition = '未提供' }
            $failureStackTrace = [string] $failureRecord.ScriptStackTrace
            if ([string]::IsNullOrWhiteSpace($failureStackTrace)) { $failureStackTrace = '未提供' }
            $failurePosition = $failurePosition -replace '\s+', ' '
            $failureStackTrace = $failureStackTrace -replace '\s+', ' '
            $failureDiagnostic = "安装阶段=$installStage；错误 ID=$($failureRecord.FullyQualifiedErrorId)；错误位置=$failurePosition；ScriptStackTrace=$failureStackTrace"
            $rollbackFailure = $null
            $rollbackAttempted = $false
            $rollbackSucceeded = $false
            $preservedUnmanagedLaunchers = $false
            if (-not $transactionCompleted) {
                $rollbackAttempted = $true
                try {
                    $rollbackResult = Undo-CountdownInstallation -Layout $Layout -TransactionPath $transaction.transactionPath -PowerShell7Path $PowerShell7Path -Ports $Ports
                    $rollbackSucceeded = $true
                    $preservedUnmanagedLaunchers = [bool] $rollbackResult.PreservedUnmanagedLaunchers
                }
                catch {
                    $rollbackFailure = $_
                }
            }
            $failureReceiptWriteFailure = $null
            if ($deferCommit) {
                try {
                    $deferredReceiptDirectory = Split-Path -Parent (Assert-CountdownInstallPathOnDDrive -Path $DeferredReceiptPath)
                    $failureReceiptPath = Join-Path $deferredReceiptDirectory 'deployment-failure.json'
                    [void] (Write-CountdownInstallFailureReceipt `
                            -Path $failureReceiptPath `
                            -Stage $installStage `
                            -ErrorRecord $failureRecord `
                            -TransactionStarted $transactionStarted `
                            -CommitState $(if ($transactionCompleted) { 'committed' } else { 'not-committed' }) `
                            -RollbackAttempted $rollbackAttempted `
                            -RollbackSucceeded $rollbackSucceeded `
                            -PreservedUnmanagedLaunchers $preservedUnmanagedLaunchers)
                }
                catch {
                    $failureReceiptWriteFailure = $_.Exception.Message
                }
            }
            $receiptWriteSuffix = if ([string]::IsNullOrWhiteSpace([string] $failureReceiptWriteFailure)) { '' } else { "；故障回执写入失败：$failureReceiptWriteFailure" }
            if ($null -ne $rollbackFailure) {
                throw "安装失败：$failure；$failureDiagnostic；回滚也失败：$($rollbackFailure.Exception.Message)$receiptWriteSuffix"
            }
            if ($transactionCompleted) {
                throw "安装已提交，但提交后收尾失败，未回滚：$failure；$failureDiagnostic$receiptWriteSuffix"
            }
            if ($preservedUnmanagedLaunchers) {
                throw "安装失败，已恢复应用、任务和时间服务；为保护用户替换的桌面启动器，保留了事务证据：$failure；$failureDiagnostic$receiptWriteSuffix"
            }
            throw "安装失败，已回滚：$failure；$failureDiagnostic$receiptWriteSuffix"
        }
    }
    finally {
        if ($null -ne $installMutex) {
            Exit-CountdownInstallTransactionMutex -Mutex $installMutex
        }
    }
}

Export-ModuleMember -Function Invoke-CountdownInstallation, Complete-CountdownInstallation, Undo-CountdownInstallation, Restore-CountdownInterruptedInstallation, New-CountdownInstallWindowsPorts, Invoke-CountdownTransactionAcceptance
