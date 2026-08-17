#requires -Version 7.0

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Import-Module (Join-Path $PSScriptRoot 'Countdown.Runtime.psm1')
Import-Module (Join-Path $PSScriptRoot 'Countdown.Settings.psm1')

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
    try {
        $settingsMutex = Enter-CountdownSettingsWriteMutex -Path $SettingsPath -TimeoutSeconds 10
        $settingsBefore = Get-CountdownAcceptanceSettingsState -Path $SettingsPath
        if (-not (Test-CountdownSettingsDeploymentTransition -BeforeHash $settingsBaseline.Hash -AfterHash $settingsBefore.Hash -BeforeJson $settingsBaseline.Json -AfterJson $settingsBefore.Json -MigrationUtc $migrationUtc)) {
            throw '验收前 settings.json 超出允许的数据等价范围。'
        }

        [void] (Assert-CountdownAcceptanceEnvironment `
            -Phase '验收提交前' `
            -ExpectedInteractiveContext $ExpectedInteractiveContext `
            -ExpectedProcessId $ExpectedProcessId `
            -RuntimeStatusPath $RuntimeStatusPath `
            -LaunchToken $LaunchToken `
            -ExpectedSessionId $ExpectedSessionId `
            -ExpectedApplicationPath $ExpectedApplicationPath `
            -TaskName $TaskName `
            -ExpectedScheduledTaskIdentity $ExpectedScheduledTaskIdentity `
            -GetInteractiveContext $GetInteractiveContext `
            -GetScheduledTask $GetScheduledTask `
            -GetScheduledTaskIdentity $GetScheduledTaskIdentity `
            -GetScheduledTaskInfo $GetScheduledTaskInfo `
            -ProcessProbe $ProcessProbe)

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

        [void] (Assert-CountdownAcceptanceEnvironment `
            -Phase 'schema 迁移确认后' `
            -ExpectedInteractiveContext $ExpectedInteractiveContext `
            -ExpectedProcessId $ExpectedProcessId `
            -RuntimeStatusPath $RuntimeStatusPath `
            -LaunchToken $LaunchToken `
            -ExpectedSessionId $ExpectedSessionId `
            -ExpectedApplicationPath $ExpectedApplicationPath `
            -TaskName $TaskName `
            -ExpectedScheduledTaskIdentity $ExpectedScheduledTaskIdentity `
            -GetInteractiveContext $GetInteractiveContext `
            -GetScheduledTask $GetScheduledTask `
            -GetScheduledTaskIdentity $GetScheduledTaskIdentity `
            -GetScheduledTaskInfo $GetScheduledTaskInfo `
            -ProcessProbe $ProcessProbe)

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

        $finalEnvironment = Assert-CountdownAcceptanceEnvironment `
            -Phase '应用解锁确认后' `
            -ExpectedInteractiveContext $ExpectedInteractiveContext `
            -ExpectedProcessId $ExpectedProcessId `
            -RuntimeStatusPath $RuntimeStatusPath `
            -LaunchToken $LaunchToken `
            -ExpectedSessionId $ExpectedSessionId `
            -ExpectedApplicationPath $ExpectedApplicationPath `
            -TaskName $TaskName `
            -ExpectedScheduledTaskIdentity $ExpectedScheduledTaskIdentity `
            -GetInteractiveContext $GetInteractiveContext `
            -GetScheduledTask $GetScheduledTask `
            -GetScheduledTaskIdentity $GetScheduledTaskIdentity `
            -GetScheduledTaskInfo $GetScheduledTaskInfo `
            -ProcessProbe $ProcessProbe `
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

Export-ModuleMember -Function Invoke-CountdownTransactionAcceptance
