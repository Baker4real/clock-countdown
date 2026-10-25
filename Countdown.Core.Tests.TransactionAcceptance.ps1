# Countdown.Core.Tests.ps1 的 TransactionAcceptance 分段：入口按开关点源执行，共用入口的参数、断言函数与已建立的状态，不单独运行。

$acceptanceTestRoot = Join-Path $PSScriptRoot ("acceptance-test-" + [guid]::NewGuid().ToString('N'))
$acceptanceStartupRequestPath = Join-Path $acceptanceTestRoot 'startup-request.json'
$acceptanceRuntimeStatusPath = Join-Path $acceptanceTestRoot 'runtime-status.json'
$acceptanceStatusPath = Join-Path $acceptanceTestRoot 'acceptance-status.json'
$acceptanceSettingsSnapshotPath = Join-Path $acceptanceTestRoot 'settings-snapshot.json'
$acceptanceSettingsPath = Join-Path $acceptanceTestRoot 'settings.json'
$acceptanceApplicationPath = Join-Path $acceptanceTestRoot '敏敏子倒计时.ps1'
$acceptanceHeldRequestPath = Join-Path $acceptanceTestRoot 'held-startup-request.json'
$acceptanceExpectedContext = [pscustomobject]@{
    User = 'MINMIN\AcceptanceUser'
    SessionId = 7
    Desktop = 'D:\AcceptanceDesktop'
}
$acceptanceAdapterState = [pscustomobject]@{ Completed = 0; Removed = 0 }
$acceptanceTaskState = 'Ready'
$acceptanceLastTaskResult = 0
$acceptanceTaskIdentity = 'scheduled-task-v1'
$acceptanceJobs = [System.Collections.ArrayList]::new()
function Reset-CountdownAcceptanceTestCase {
    param([int] $RuntimeProcessId = 4242)

    Remove-Item -LiteralPath $acceptanceStatusPath -Force -ErrorAction SilentlyContinue
    Copy-Item -LiteralPath $acceptanceSettingsSnapshotPath -Destination $acceptanceSettingsPath -Force
    $acceptanceAdapterState.Completed = 0
    $acceptanceAdapterState.Removed = 0
    $script:acceptanceTaskState = 'Ready'
    $script:acceptanceLastTaskResult = 0
    $script:acceptanceTaskIdentity = 'scheduled-task-v1'
    $request = New-CountdownStartupRequest -Path $acceptanceStartupRequestPath -AcceptanceReadOnly
    [void] (Write-CountdownWindowReadyReceipt -Path $acceptanceRuntimeStatusPath -LaunchToken $request.launchToken -ProcessId $RuntimeProcessId -SessionId 7 -ApplicationPath $acceptanceApplicationPath -CalendarState Ready)
    $request
}

function Start-CountdownAcceptanceTestApplication {
    param(
        [Parameter(Mandatory)] [object] $StartupRequest,
        [switch] $ChangeSettings,
        [AllowNull()] [string] $SettingsJsonAfterRelease,
        [switch] $SkipFinalizedReady,
        # 安装生命周期测试传入安装布局；缺省时沿用事务验收夹具的固定路径。
        [object] $Layout
    )

    if ($null -eq $Layout) {
        $Layout = [pscustomobject]@{
            StartupRequestPath = $acceptanceStartupRequestPath
            AcceptanceStatusPath = $acceptanceStatusPath
            ApplicationPath = $acceptanceApplicationPath
            SettingsPath = $acceptanceSettingsPath
        }
    }
    $job = Start-Job -ArgumentList $Layout.StartupRequestPath, $Layout.AcceptanceStatusPath, $StartupRequest.launchToken, $Layout.ApplicationPath, $Layout.SettingsPath, ([bool] $ChangeSettings), $SettingsJsonAfterRelease, ([bool] $SkipFinalizedReady) -ScriptBlock {
        param($StartupRequestPath, $AcceptanceStatusPath, $LaunchToken, $ApplicationPath, $SettingsPath, $ChangeSettings, $SettingsJsonAfterRelease, $SkipFinalizedReady)
        $deadline = [DateTimeOffset]::UtcNow.AddSeconds(10)
        do {
            try {
                $request = [System.IO.File]::ReadAllText($StartupRequestPath, [System.Text.Encoding]::UTF8) | ConvertFrom-Json -DateKind String
                if ([string] $request.acceptanceState -eq 'released') {
                    if ($ChangeSettings) {
                        [System.IO.File]::WriteAllText($SettingsPath, '{"schemaVersion":9,"unexpected":true}', [System.Text.UTF8Encoding]::new($false))
                    }
                    elseif (-not [string]::IsNullOrWhiteSpace($SettingsJsonAfterRelease)) {
                        [System.IO.File]::WriteAllText($SettingsPath, $SettingsJsonAfterRelease, [System.Text.UTF8Encoding]::new($false))
                    }
                    $request.acceptanceState = 'ready'
                    [System.IO.File]::WriteAllText($StartupRequestPath, ($request | ConvertTo-Json -Depth 8), [System.Text.UTF8Encoding]::new($false))
                    break
                }
            }
            catch {}
            Start-Sleep -Milliseconds 20
        } while ([DateTimeOffset]::UtcNow -lt $deadline)
        if ($SkipFinalizedReady) { return }

        do {
            try {
                $request = [System.IO.File]::ReadAllText($StartupRequestPath, [System.Text.Encoding]::UTF8) | ConvertFrom-Json -DateKind String
                if ([string] $request.acceptanceState -eq 'finalized') {
                    $receipt = [ordered]@{
                        schemaVersion = 1
                        stage = 'finalized-ready'
                        launchToken = $LaunchToken
                        processId = 4242
                        sessionId = 7
                        applicationPath = $ApplicationPath
                        readyAtUtc = [DateTimeOffset]::UtcNow.ToString('o')
                    }
                    [System.IO.File]::WriteAllText($AcceptanceStatusPath, ($receipt | ConvertTo-Json -Compress), [System.Text.UTF8Encoding]::new($false))
                    return
                }
            }
            catch {}
            Start-Sleep -Milliseconds 20
        } while ([DateTimeOffset]::UtcNow -lt $deadline)
        throw 'Acceptance test application did not observe the full release/finalize protocol.'
    }
    [void] $acceptanceJobs.Add($job)
    $job
}

function Start-CountdownAcceptanceHeldReader {
    param(
        [Parameter(Mandatory)] [string] $Path,
        [Parameter(Mandatory)] [int] $HoldMilliseconds
    )

    # 模拟应用读启动请求：像 File.ReadAllText 一样只共享读地打开目标，打开后才建标记文件；
    # 持有到 HoldMilliseconds 到期或调用方删掉标记为止，删标记就是让读方立即关闭句柄。
    $markerPath = "$Path.$([guid]::NewGuid().ToString('N')).holding"
    $job = Start-Job -ArgumentList $Path, $markerPath, $HoldMilliseconds -ScriptBlock {
        param($Path, $MarkerPath, $HoldMilliseconds)
        $stream = [System.IO.File]::Open($Path, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::Read)
        try {
            [System.IO.File]::WriteAllText($MarkerPath, 'holding')
            $deadline = [DateTimeOffset]::UtcNow.AddMilliseconds($HoldMilliseconds)
            while ([DateTimeOffset]::UtcNow -lt $deadline -and (Test-Path -LiteralPath $MarkerPath -PathType Leaf)) {
                Start-Sleep -Milliseconds 10
            }
        }
        finally {
            $stream.Dispose()
        }
    }
    [void] $acceptanceJobs.Add($job)
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds(15)
    while (-not (Test-Path -LiteralPath $markerPath -PathType Leaf)) {
        if ([DateTimeOffset]::UtcNow -ge $deadline -or $job.State -in @('Completed', 'Failed', 'Stopped')) {
            throw '读句柄夹具未能占住启动请求。'
        }
        Start-Sleep -Milliseconds 10
    }
    [pscustomobject]@{ Job = $job; MarkerPath = $markerPath }
}

function Invoke-CountdownAcceptanceTestCase {
    param(
        [Parameter(Mandatory)] [object] $StartupRequest,
        [int] $ExpectedProcessId = 4242,
        [int] $ReadyTimeoutSeconds = 5
    )

    Invoke-CountdownTransactionAcceptance `
        -StartupRequestPath $acceptanceStartupRequestPath `
        -RuntimeStatusPath $acceptanceRuntimeStatusPath `
        -AcceptanceStatusPath $acceptanceStatusPath `
        -LaunchToken $StartupRequest.launchToken `
        -ExpectedInteractiveContext $acceptanceExpectedContext `
        -ExpectedProcessId $ExpectedProcessId `
        -ExpectedSessionId 7 `
        -ExpectedApplicationPath $acceptanceApplicationPath `
        -TaskName 'Minmin Countdown Acceptance Test' `
        -ExpectedScheduledTaskIdentity 'scheduled-task-v1' `
        -SettingsSnapshotPath $acceptanceSettingsSnapshotPath `
        -SettingsPath $acceptanceSettingsPath `
        -GetInteractiveContext { $acceptanceExpectedContext } `
        -GetScheduledTask {
            param([string] $TaskName)
            $reportedTaskState = $acceptanceTaskState
            if ($reportedTaskState -eq 'StopAfterReady') {
                $startupState = [string] ((Get-Content -LiteralPath $acceptanceStartupRequestPath -Raw -Encoding UTF8 | ConvertFrom-Json).acceptanceState)
                $reportedTaskState = if ($startupState -in @('ready', 'finalized')) { 'Disabled' } else { 'Ready' }
            }
            $reportedTaskIdentity = $acceptanceTaskIdentity
            if ($reportedTaskIdentity -eq 'DriftAfterReady') {
                $startupState = [string] ((Get-Content -LiteralPath $acceptanceStartupRequestPath -Raw -Encoding UTF8 | ConvertFrom-Json).acceptanceState)
                $reportedTaskIdentity = if ($startupState -in @('ready', 'finalized')) { 'scheduled-task-v2' } else { 'scheduled-task-v1' }
            }
            [pscustomobject]@{ TaskName = $TaskName; State = $reportedTaskState; Identity = $reportedTaskIdentity }
        } `
        -GetScheduledTaskIdentity {
            param([object] $ScheduledTask)
            [string] $ScheduledTask.Identity
        } `
        -GetScheduledTaskInfo {
            param([string] $TaskName)
            [pscustomobject]@{ LastTaskResult = $acceptanceLastTaskResult }
        } `
        -CompleteTransaction {
            $acceptanceAdapterState.Completed++
            [pscustomobject]@{ Internal = 'accepted-journal' }
        } `
        -RemoveCommittedTransaction {
            $acceptanceAdapterState.Removed++
            [pscustomobject]@{ Internal = 'cleanup-result' }
        } `
        -ProcessProbe {
            param([int] $ProcessId)
            [pscustomobject]@{
                ProcessId = $ProcessId
                SessionId = 7
                ExecutablePath = Join-Path $acceptanceTestRoot 'app\MinminCountdown.exe'
                CommandLine = '"MinminCountdown.exe"'
            }
        } `
        -ReadyTimeoutSeconds $ReadyTimeoutSeconds
}

try {
    New-Item -ItemType Directory -Path $acceptanceTestRoot -Force | Out-Null
    [System.IO.File]::WriteAllText($acceptanceApplicationPath, '# acceptance application', [System.Text.UTF8Encoding]::new($false))
    [System.IO.File]::WriteAllText($acceptanceSettingsSnapshotPath, '{"schemaVersion":9}', [System.Text.UTF8Encoding]::new($false))
    $acceptanceStartupRequest = Reset-CountdownAcceptanceTestCase
    $acceptanceReadyJob = Start-CountdownAcceptanceTestApplication -StartupRequest $acceptanceStartupRequest
    $acceptanceResult = @(Invoke-CountdownAcceptanceTestCase -StartupRequest $acceptanceStartupRequest)

    Assert-Equal 1 $acceptanceResult.Count 'Transaction acceptance returns one stable result object'
    Assert-Equal 'finalized-ready' ([string] $acceptanceResult[0].AcceptanceState) 'Transaction acceptance returns finalized-ready only after application unlock confirmation'
    Assert-Equal 4242 ([int] $acceptanceResult[0].RuntimeProcessId) 'Transaction acceptance retains the original managed process identity'
    Assert-Equal 'Ready' ([string] $acceptanceResult[0].TaskState) 'Transaction acceptance reports Ready from the scheduled task'
    Assert-Equal 0 ([int] $acceptanceResult[0].LastTaskResult) 'Transaction acceptance reports LastTaskResult from GetScheduledTaskInfo'
    Assert-Equal $true ([bool] $acceptanceResult[0].SettingsUnchanged) 'Identical live settings fingerprints compute SettingsUnchanged=true'
    Assert-Equal ([string] $acceptanceResult[0].SettingsHashBefore) ([string] $acceptanceResult[0].SettingsHashAfter) 'Unchanged settings keep the same SHA-256'
    Assert-Equal ([long] $acceptanceResult[0].SettingsLengthBefore) ([long] $acceptanceResult[0].SettingsLengthAfter) 'Unchanged settings keep the same length'
    Assert-Equal ([string] $acceptanceResult[0].SettingsMtimeUtcBefore) ([string] $acceptanceResult[0].SettingsMtimeUtcAfter) 'Unchanged settings keep the same LastWriteTimeUtc'
    Assert-Equal 1 $acceptanceAdapterState.Completed 'Transaction acceptance commits the transaction once'
    Assert-Equal 1 $acceptanceAdapterState.Removed 'Transaction acceptance removes rollback evidence only after finalized-ready'
    Assert-Equal 'finalized' ([string] ((Get-Content -LiteralPath $acceptanceStartupRequestPath -Raw -Encoding UTF8 | ConvertFrom-Json).acceptanceState)) 'Transaction acceptance leaves the startup request finalized'
    Wait-Job -Job $acceptanceReadyJob -Timeout 5 | Out-Null
    Assert-Equal 'Completed' ([string] $acceptanceReadyJob.State) 'Acceptance test application observed release before finalize'

    $acceptanceMigrationUtc = [DateTimeOffset] '2026-07-20T02:16:00Z'
    $acceptanceLegacyRepeat = [pscustomobject]@{
        schemaVersion = 9
        timeZoneId = 'China Standard Time'
        displayPrecision = 'Minute'
        monthlySalary = 0
        workStart = '09:00'
        workEnd = '18:00'
        themePreference = 'Auto'
        tags = @()
        countdowns = @()
        notes = @([pscustomobject]@{
                id = 'acceptance-repeat-note'
                text = '验收重复提醒'
                date = $null
                isCompleted = $false
                tagIds = @()
                reminder = [pscustomobject]@{
                    type = 'repeat'
                    firstDueUtc = '2026-07-20T01:00:00.0000000+00:00'
                    intervalMinutes = 1
                }
            })
    }
    $acceptanceLegacyJson = $acceptanceLegacyRepeat | ConvertTo-Json -Depth 8
    $acceptanceMigratedJson = ConvertTo-CountdownSettingsJson -State (
        ConvertFrom-CountdownSettingsObject -Settings $acceptanceLegacyRepeat -MigrationUtc $acceptanceMigrationUtc)
    $acceptanceMigrated = $acceptanceMigratedJson | ConvertFrom-Json
    $acceptanceRuleId = [string] $acceptanceMigrated.notes[0].reminderRules[0].id
    $acceptanceOccurrenceKey = "{0}|{1}" -f $acceptanceRuleId, $acceptanceMigrationUtc.ToString('o')
    $cursorBeyondFrozen = $acceptanceMigratedJson | ConvertFrom-Json
    $cursorBeyondFrozen.reminderStates.PSObject.Properties[$acceptanceRuleId].Value.handledThroughUtc = $acceptanceMigrationUtc.AddMinutes(1).ToString('o')
    Assert-Equal $false (Test-CountdownSettingsDeploymentTransition `
            -BeforeHash 'legacy-repeat' `
            -AfterHash 'cursor-beyond-frozen' `
            -BeforeJson $acceptanceLegacyJson `
            -AfterJson ($cursorBeyondFrozen | ConvertTo-Json -Depth 12) `
            -MigrationUtc $acceptanceMigrationUtc) 'Deployment transition rejects a reminder cursor beyond the frozen migration occurrence'
    $forgedDelivery = $acceptanceMigratedJson | ConvertFrom-Json
    $forgedDelivery.reminderStates.PSObject.Properties[$acceptanceRuleId].Value.lastDeliveredOccurrenceKey = $acceptanceOccurrenceKey
    Assert-Equal $false (Test-CountdownSettingsDeploymentTransition `
            -BeforeHash 'legacy-repeat' `
            -AfterHash 'forged-delivery' `
            -BeforeJson $acceptanceLegacyJson `
            -AfterJson ($forgedDelivery | ConvertTo-Json -Depth 12) `
            -MigrationUtc $acceptanceMigrationUtc) 'Deployment transition rejects a forged reminder delivery'
    $forgedDisposition = $acceptanceMigratedJson | ConvertFrom-Json
    $forgedDispositionState = $forgedDisposition.reminderStates.PSObject.Properties[$acceptanceRuleId].Value
    $forgedDispositionState.lastDeliveredOccurrenceKey = $acceptanceOccurrenceKey
    $forgedDispositionState.lastDispositionOccurrenceKey = $acceptanceOccurrenceKey
    Assert-Equal $false (Test-CountdownSettingsDeploymentTransition `
            -BeforeHash 'legacy-repeat' `
            -AfterHash 'forged-disposition' `
            -BeforeJson $acceptanceLegacyJson `
            -AfterJson ($forgedDisposition | ConvertTo-Json -Depth 12) `
            -MigrationUtc $acceptanceMigrationUtc) 'Deployment transition rejects a forged reminder disposition'
    [System.IO.File]::WriteAllText($acceptanceSettingsSnapshotPath, $acceptanceLegacyJson, [System.Text.UTF8Encoding]::new($false))
    $frozenMigrationRequest = Reset-CountdownAcceptanceTestCase
    $frozenMigrationRequest.requestedAtUtc = $acceptanceMigrationUtc.ToString('o')
    [System.IO.File]::WriteAllText(
        $acceptanceStartupRequestPath,
        ($frozenMigrationRequest | ConvertTo-Json -Compress),
        [System.Text.UTF8Encoding]::new($false))
    $frozenMigrationJob = Start-CountdownAcceptanceTestApplication `
        -StartupRequest $frozenMigrationRequest `
        -SettingsJsonAfterRelease $acceptanceMigratedJson
    $frozenMigrationResult = @(Invoke-CountdownAcceptanceTestCase -StartupRequest $frozenMigrationRequest)
    Assert-Equal 'finalized-ready' ([string] $frozenMigrationResult[0].AcceptanceState) 'Transaction acceptance uses one frozen migration instant across the C# handoff'
    Assert-Equal $false ([bool] $frozenMigrationResult[0].SettingsUnchanged) 'Allowed schema migration records SettingsUnchanged=false'
    Assert-Equal $true ([string] $frozenMigrationResult[0].SettingsHashBefore -match '\A[0-9A-F]{64}\z') 'Allowed migration keeps the before SHA-256 fingerprint'
    Assert-Equal $true ([string] $frozenMigrationResult[0].SettingsHashAfter -match '\A[0-9A-F]{64}\z') 'Allowed migration keeps the after SHA-256 fingerprint'
    Assert-Equal $false ([string] $frozenMigrationResult[0].SettingsHashBefore -eq [string] $frozenMigrationResult[0].SettingsHashAfter) 'Allowed migration changes the live settings SHA-256'
    Assert-Equal $true ([long] $frozenMigrationResult[0].SettingsLengthBefore -ge 0) 'Allowed migration keeps the before length fingerprint'
    Assert-Equal $true ([long] $frozenMigrationResult[0].SettingsLengthAfter -ge 0) 'Allowed migration keeps the after length fingerprint'
    Assert-Equal $false ([string]::IsNullOrWhiteSpace([string] $frozenMigrationResult[0].SettingsMtimeUtcBefore)) 'Allowed migration keeps the before LastWriteTimeUtc fingerprint'
    Assert-Equal $false ([string]::IsNullOrWhiteSpace([string] $frozenMigrationResult[0].SettingsMtimeUtcAfter)) 'Allowed migration keeps the after LastWriteTimeUtc fingerprint'
    Wait-Job -Job $frozenMigrationJob -Timeout 5 | Out-Null
    Assert-Equal 'Completed' ([string] $frozenMigrationJob.State) 'Frozen migration acceptance completes the release protocol'

    $canonicalV10AcceptanceSettings = ConvertTo-CountdownSettingsJson -State (
        ConvertFrom-CountdownSettingsObject -Settings ([pscustomobject]@{
                schemaVersion = 10
                timeZoneId = 'China Standard Time'
                displayPrecision = 'Second'
                monthlySalary = 0
                workStart = '09:00'
                workEnd = '18:00'
                themePreference = 'Auto'
                tags = @()
                countdowns = @()
                notes = @()
                reminderStates = [pscustomobject]@{}
            }))
    Assert-Equal $false ((ConvertFrom-CountdownSettingsObject -Settings ($canonicalV10AcceptanceSettings | ConvertFrom-Json)).Migrated) 'Canonical v10 acceptance fixture is not a migration'
    [System.IO.File]::WriteAllText($acceptanceSettingsSnapshotPath, $canonicalV10AcceptanceSettings, [System.Text.UTF8Encoding]::new($false))
    $canonicalV10Request = Reset-CountdownAcceptanceTestCase
    $canonicalV10BeforeItem = Get-Item -LiteralPath $acceptanceSettingsPath
    $canonicalV10BeforeHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $acceptanceSettingsPath).Hash
    $canonicalV10BeforeLength = [long] $canonicalV10BeforeItem.Length
    $canonicalV10BeforeMtime = $canonicalV10BeforeItem.LastWriteTimeUtc.ToUniversalTime().ToString('o')
    $canonicalV10Job = Start-CountdownAcceptanceTestApplication -StartupRequest $canonicalV10Request
    $canonicalV10Result = @(Invoke-CountdownAcceptanceTestCase -StartupRequest $canonicalV10Request)
    $canonicalV10AfterItem = Get-Item -LiteralPath $acceptanceSettingsPath
    Assert-Equal $true ([bool] $canonicalV10Result[0].SettingsUnchanged) 'Already-canonical v10 keeps SettingsUnchanged=true'
    Assert-Equal $canonicalV10BeforeHash ([string] $canonicalV10Result[0].SettingsHashBefore) 'Already-canonical v10 before hash is the live file SHA-256'
    Assert-Equal $canonicalV10BeforeHash ([string] $canonicalV10Result[0].SettingsHashAfter) 'Already-canonical v10 after hash stays the live file SHA-256'
    Assert-Equal $canonicalV10BeforeLength ([long] $canonicalV10Result[0].SettingsLengthBefore) 'Already-canonical v10 before length is the live file length'
    Assert-Equal $canonicalV10BeforeLength ([long] $canonicalV10Result[0].SettingsLengthAfter) 'Already-canonical v10 after length stays the live file length'
    Assert-Equal $canonicalV10BeforeMtime ([string] $canonicalV10Result[0].SettingsMtimeUtcBefore) 'Already-canonical v10 before mtime is the live file LastWriteTimeUtc'
    Assert-Equal $canonicalV10BeforeMtime ([string] $canonicalV10Result[0].SettingsMtimeUtcAfter) 'Already-canonical v10 after mtime stays the live file LastWriteTimeUtc'
    Assert-Equal $canonicalV10BeforeHash (Get-FileHash -Algorithm SHA256 -LiteralPath $acceptanceSettingsPath).Hash 'Already-canonical v10 deploy path does not rewrite settings.json bytes'
    Assert-Equal $canonicalV10BeforeLength ([long] $canonicalV10AfterItem.Length) 'Already-canonical v10 deploy path does not change settings.json length'
    Assert-Equal $canonicalV10BeforeMtime $canonicalV10AfterItem.LastWriteTimeUtc.ToUniversalTime().ToString('o') 'Already-canonical v10 deploy path does not change settings.json LastWriteTimeUtc'
    Wait-Job -Job $canonicalV10Job -Timeout 5 | Out-Null
    Assert-Equal 'Completed' ([string] $canonicalV10Job.State) 'Already-canonical v10 acceptance completes the release protocol'

    $pidDriftRequest = Reset-CountdownAcceptanceTestCase -RuntimeProcessId 5252
    $pidDriftRejected = $false
    $pidDriftMessage = $null
    try {
        Invoke-CountdownAcceptanceTestCase -StartupRequest $pidDriftRequest -ExpectedProcessId 4242 | Out-Null
    }
    catch {
        $pidDriftMessage = $_.Exception.Message
        $pidDriftRejected = $pidDriftMessage -match '验收提交前受管应用进程已变化'
    }
    Assert-Equal $true $pidDriftRejected "Managed PID drift blocks transaction acceptance before release: $pidDriftMessage"
    Assert-Equal 0 $acceptanceAdapterState.Completed 'PID drift does not commit the transaction'
    Assert-Equal 0 $acceptanceAdapterState.Removed 'PID drift preserves rollback evidence'
    Assert-Equal 'active' ([string] ((Get-Content -LiteralPath $acceptanceStartupRequestPath -Raw -Encoding UTF8 | ConvertFrom-Json).acceptanceState)) 'PID drift leaves the candidate application read-only and unreleased'

    $taskStoppedRequest = Reset-CountdownAcceptanceTestCase
    $acceptanceTaskState = 'Disabled'
    $taskStoppedRejected = $false
    try {
        Invoke-CountdownAcceptanceTestCase -StartupRequest $taskStoppedRequest | Out-Null
    }
    catch {
        $taskStoppedRejected = $_.Exception.Message -match '验收提交前计划任务未回到 Ready 且 lastTaskResult=0：state=Disabled'
    }
    Assert-Equal $true $taskStoppedRejected 'A non-ready scheduled task blocks transaction acceptance before release'

    $taskRunningRequest = Reset-CountdownAcceptanceTestCase
    $acceptanceTaskState = 'Running'
    $taskRunningRejected = $false
    try {
        Invoke-CountdownAcceptanceTestCase -StartupRequest $taskRunningRequest | Out-Null
    }
    catch {
        $taskRunningRejected = $_.Exception.Message -match '验收提交前计划任务未回到 Ready 且 lastTaskResult=0：state=Running lastTaskResult=0'
    }
    Assert-Equal $true $taskRunningRejected 'A still-running scheduled task cannot impersonate Ready'
    Assert-Equal 0 $acceptanceAdapterState.Completed 'A still-running scheduled task does not commit the transaction'
    Assert-Equal 0 $acceptanceAdapterState.Removed 'A still-running scheduled task preserves rollback evidence'

    $nonzeroTaskResultRequest = Reset-CountdownAcceptanceTestCase
    $acceptanceLastTaskResult = 267
    $nonzeroTaskResultRejected = $false
    try {
        Invoke-CountdownAcceptanceTestCase -StartupRequest $nonzeroTaskResultRequest | Out-Null
    }
    catch {
        $nonzeroTaskResultRejected = $_.Exception.Message -match '验收提交前计划任务未回到 Ready 且 lastTaskResult=0：state=Ready lastTaskResult=267'
    }
    Assert-Equal $true $nonzeroTaskResultRejected 'A non-zero LastTaskResult from GetScheduledTaskInfo blocks transaction acceptance'
    Assert-Equal 0 $acceptanceAdapterState.Completed 'A non-zero LastTaskResult does not commit the transaction'
    Assert-Equal 0 $acceptanceAdapterState.Removed 'A non-zero LastTaskResult preserves rollback evidence'
    Assert-Equal 0 $acceptanceAdapterState.Completed 'A non-running scheduled task does not commit the transaction'
    Assert-Equal 0 $acceptanceAdapterState.Removed 'A non-running scheduled task preserves rollback evidence'
    Assert-Equal 'active' ([string] ((Get-Content -LiteralPath $acceptanceStartupRequestPath -Raw -Encoding UTF8 | ConvertFrom-Json).acceptanceState)) 'A non-running scheduled task leaves the candidate application read-only and unreleased'

    $taskDriftRequest = Reset-CountdownAcceptanceTestCase
    $acceptanceTaskState = 'StopAfterReady'
    $taskDriftJob = Start-CountdownAcceptanceTestApplication -StartupRequest $taskDriftRequest -SkipFinalizedReady
    $taskDriftRejected = $false
    try {
        Invoke-CountdownAcceptanceTestCase -StartupRequest $taskDriftRequest | Out-Null
    }
    catch {
        $taskDriftRejected = $_.Exception.Message -match 'schema 迁移确认后计划任务未回到 Ready 且 lastTaskResult=0：state=Disabled'
    }
    Wait-Job -Job $taskDriftJob -Timeout 5 | Out-Null
    Assert-Equal $true $taskDriftRejected 'Scheduled task drift after release blocks transaction acceptance'
    Assert-Equal 0 $acceptanceAdapterState.Completed 'Scheduled task drift after release does not commit the transaction'
    Assert-Equal 0 $acceptanceAdapterState.Removed 'Scheduled task drift after release preserves rollback evidence'
    $taskDriftStartupState = Get-Content -LiteralPath $acceptanceStartupRequestPath -Raw -Encoding UTF8 | ConvertFrom-Json
    Assert-Equal 'ready' ([string] $taskDriftStartupState.acceptanceState) 'Scheduled task drift does not finalize the startup request'
    Assert-Equal $true ([bool] $taskDriftStartupState.acceptanceReadOnly) 'Scheduled task drift leaves the candidate application read-only'

    $taskIdentityDriftRequest = Reset-CountdownAcceptanceTestCase
    $acceptanceTaskIdentity = 'DriftAfterReady'
    $taskIdentityDriftJob = Start-CountdownAcceptanceTestApplication -StartupRequest $taskIdentityDriftRequest -SkipFinalizedReady
    $taskIdentityDriftRejected = $false
    try {
        Invoke-CountdownAcceptanceTestCase -StartupRequest $taskIdentityDriftRequest | Out-Null
    }
    catch {
        $taskIdentityDriftRejected = $_.Exception.Message -match 'schema 迁移确认后计划任务定义已变化'
    }
    Wait-Job -Job $taskIdentityDriftJob -Timeout 5 | Out-Null
    Assert-Equal $true $taskIdentityDriftRejected 'Scheduled task identity drift after release blocks transaction acceptance'
    Assert-Equal 0 $acceptanceAdapterState.Completed 'Scheduled task identity drift does not commit the transaction'
    Assert-Equal 0 $acceptanceAdapterState.Removed 'Scheduled task identity drift preserves rollback evidence'
    $taskIdentityDriftStartupState = Get-Content -LiteralPath $acceptanceStartupRequestPath -Raw -Encoding UTF8 | ConvertFrom-Json
    Assert-Equal 'ready' ([string] $taskIdentityDriftStartupState.acceptanceState) 'Scheduled task identity drift does not finalize the startup request'
    Assert-Equal $true ([bool] $taskIdentityDriftStartupState.acceptanceReadOnly) 'Scheduled task identity drift leaves the candidate application read-only'

    $settingsDriftRequest = Reset-CountdownAcceptanceTestCase
    $settingsDriftJob = Start-CountdownAcceptanceTestApplication -StartupRequest $settingsDriftRequest -ChangeSettings -SkipFinalizedReady
    $settingsDriftRejected = $false
    try {
        Invoke-CountdownAcceptanceTestCase -StartupRequest $settingsDriftRequest | Out-Null
    }
    catch {
        $settingsDriftRejected = $_.Exception.Message -match 'C# schema 迁移超出允许的数据等价范围'
    }
    Wait-Job -Job $settingsDriftJob -Timeout 5 | Out-Null
    Assert-Equal $true $settingsDriftRejected 'Non-equivalent settings after release block transaction acceptance'
    Assert-Equal 0 $acceptanceAdapterState.Completed 'Non-equivalent settings do not commit the transaction'
    Assert-Equal 0 $acceptanceAdapterState.Removed 'Non-equivalent settings preserve rollback evidence'
    $settingsDriftStartupState = Get-Content -LiteralPath $acceptanceStartupRequestPath -Raw -Encoding UTF8 | ConvertFrom-Json
    Assert-Equal 'ready' ([string] $settingsDriftStartupState.acceptanceState) 'Settings rejection does not finalize the startup request'
    Assert-Equal $true ([bool] $settingsDriftStartupState.acceptanceReadOnly) 'Settings rejection leaves the candidate application read-only'

    $finalTimeoutRequest = Reset-CountdownAcceptanceTestCase
    $finalTimeoutJob = Start-CountdownAcceptanceTestApplication -StartupRequest $finalTimeoutRequest -SkipFinalizedReady
    $finalTimeoutRejected = $false
    try {
        Invoke-CountdownAcceptanceTestCase -StartupRequest $finalTimeoutRequest -ReadyTimeoutSeconds 1 | Out-Null
    }
    catch {
        $finalTimeoutRejected = $_.Exception.Message -match '应用未确认 finalized-ready'
    }
    Wait-Job -Job $finalTimeoutJob -Timeout 5 | Out-Null
    Assert-Equal $true $finalTimeoutRejected 'Finalized-ready timeout remains observable after transaction acceptance'
    Assert-Equal 1 $acceptanceAdapterState.Completed 'Finalized-ready timeout occurs after transaction acceptance'
    Assert-Equal 0 $acceptanceAdapterState.Removed 'Finalized-ready timeout preserves rollback evidence'
    Assert-Equal 'finalized' ([string] ((Get-Content -LiteralPath $acceptanceStartupRequestPath -Raw -Encoding UTF8 | ConvertFrom-Json).acceptanceState)) 'Finalized-ready timeout preserves the finalized startup state for recovery'

    # 应用在验收只读期每个节拍都读启动请求；安装器推进 released / finalized 时撞上这个读句柄，
    # 要等句柄释放后完成替换，而不是报错让验收失败。
    $heldRequest = New-CountdownStartupRequest -Path $acceptanceHeldRequestPath -AcceptanceReadOnly
    $heldReader = Start-CountdownAcceptanceHeldReader -Path $acceptanceHeldRequestPath -HoldMilliseconds 500
    $heldReleaseError = $null
    try { [void] (Complete-CountdownStartupRequest -Path $acceptanceHeldRequestPath -LaunchToken $heldRequest.launchToken) }
    catch { $heldReleaseError = $_.Exception.Message }
    finally {
        Remove-Item -LiteralPath $heldReader.MarkerPath -Force -ErrorAction SilentlyContinue
        Wait-Job -Job $heldReader.Job -Timeout 10 | Out-Null
    }
    Assert-Equal $null $heldReleaseError 'Startup request release waits out an application read handle instead of failing'
    Assert-Equal 'released' ([string] ((Get-Content -LiteralPath $acceptanceHeldRequestPath -Raw -Encoding UTF8 | ConvertFrom-Json).acceptanceState)) 'Startup request release completes after the read handle closes'

    # 读句柄超过约 2 秒退避预算时明确失败：报出被占用的路径，启动请求保持原状态，不留临时文件。
    # 读方一直持有到调用返回后才删标记释放，所以这一段也证明夹具确实挡住了替换。
    $stuckRequest = New-CountdownStartupRequest -Path $acceptanceHeldRequestPath -AcceptanceReadOnly
    $stuckReader = Start-CountdownAcceptanceHeldReader -Path $acceptanceHeldRequestPath -HoldMilliseconds 30000
    $stuckReleaseError = $null
    try { [void] (Complete-CountdownStartupRequest -Path $acceptanceHeldRequestPath -LaunchToken $stuckRequest.launchToken) }
    catch { $stuckReleaseError = $_.Exception.Message }
    finally {
        Remove-Item -LiteralPath $stuckReader.MarkerPath -Force -ErrorAction SilentlyContinue
        Wait-Job -Job $stuckReader.Job -Timeout 10 | Out-Null
    }
    Assert-Equal $true ([string] $stuckReleaseError).Contains($acceptanceHeldRequestPath) 'Startup request release names the held path after the retry budget'
    Assert-Equal 'active' ([string] ((Get-Content -LiteralPath $acceptanceHeldRequestPath -Raw -Encoding UTF8 | ConvertFrom-Json).acceptanceState)) 'Startup request keeps its previous state when the release gives up'
    Assert-Equal 0 @(Get-ChildItem -LiteralPath $acceptanceTestRoot -Filter 'held-startup-request.json.*.tmp').Count 'Startup request release leaves no temporary file after giving up'
}
finally {
    foreach ($job in $acceptanceJobs) {
        Stop-Job -Job $job -ErrorAction SilentlyContinue
        Remove-Job -Job $job -Force -ErrorAction SilentlyContinue
    }
    foreach ($path in @($acceptanceStartupRequestPath, $acceptanceRuntimeStatusPath, $acceptanceStatusPath, $acceptanceSettingsSnapshotPath, $acceptanceSettingsPath, $acceptanceApplicationPath, $acceptanceHeldRequestPath)) {
        Remove-Item -LiteralPath $path -Force -ErrorAction SilentlyContinue
    }
    Remove-Item -LiteralPath $acceptanceTestRoot -Force -ErrorAction SilentlyContinue
}
