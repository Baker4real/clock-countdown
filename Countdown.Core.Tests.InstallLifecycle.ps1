# Countdown.Core.Tests.ps1 的 InstallLifecycle 分段：入口按开关点源执行，共用入口的参数、断言函数与已建立的状态，不单独运行。

# 安装事务生命周期：真实事务原语跑在隔离根下，计划任务、时间服务、进程与桌面图标经记录调用顺序的
# 测试 port 注入；覆盖延迟提交、事务验收、回滚、启动器归属冲突、阶段故障回执与 reparse point 复查。
$lifecycleTestRoot = Join-Path $PSScriptRoot ("install-lifecycle-test-" + [guid]::NewGuid().ToString('N'))
$lifecycleStagingRoot = Join-Path $lifecycleTestRoot 'staging'
$lifecycleJunctionTarget = Join-Path $lifecycleTestRoot 'junction-target'
$lifecycleJunctionPath = $null
$lifecycleSuffix = [guid]::NewGuid().ToString('N')
$lifecyclePowerShell = (Get-Command pwsh.exe -CommandType Application -ErrorAction Stop | Select-Object -First 1 -ExpandProperty Source)
$lifecycleUtf8 = [System.Text.UTF8Encoding]::new($false)
$lifecycleJobs = [System.Collections.ArrayList]::new()
$lifecycleCase = $null

function New-CountdownLifecycleTestCase {
    param([Parameter(Mandatory)] [string] $Name)

    $caseRoot = Join-Path $lifecycleTestRoot $Name
    $desktop = Join-Path $caseRoot 'desktop'
    New-Item -ItemType Directory -Path $desktop -Force | Out-Null
    [pscustomobject]@{
        Root = $caseRoot
        Layout = Get-CountdownInstallLayout `
            -InstallPath (Join-Path $caseRoot 'install') `
            -TaskName "Minmin Countdown Lifecycle $Name $lifecycleSuffix" `
            -LegacyTaskName "敏敏子倒计时 Lifecycle $Name $lifecycleSuffix" `
            -Desktop $desktop
        Context = [pscustomobject]@{ User = 'MINMIN\LifecycleUser'; SessionId = 7; Desktop = $desktop }
        Calls = [System.Collections.Generic.List[string]]::new()
        SettingsFlushedOnStop = $null
        OnRemoveScheduledTask = $null
        RegisterFailure = $null
        RegisteredUser = $null
        Task = $null
        LaunchApplication = $false
        ApplicationJob = $null
    }
}

function Invoke-CountdownLifecycleTestInstallation {
    param([string] $DeferredReceiptPath)

    $arguments = @{
        Layout = $lifecycleCase.Layout
        StagingRoot = $lifecycleStagingRoot
        ReleaseContract = $lifecycleReleaseContract
        PowerShell7Path = $lifecyclePowerShell
        InteractiveContext = $lifecycleCase.Context
        InventoryEvidenceRoot = Join-Path $lifecycleCase.Root 'evidence'
        Ports = $lifecyclePorts
    }
    if (-not [string]::IsNullOrWhiteSpace($DeferredReceiptPath)) { $arguments.DeferredReceiptPath = $DeferredReceiptPath }
    Invoke-CountdownInstallation @arguments
}

# 测试 port 只读写当前 $lifecycleCase：生命周期 module 回调时按动态作用域解析到脚本作用域的变量。
# 只把有副作用的 port 记入 Calls；查询类 port 的调用次数随轮询变化，不作为契约。
$lifecyclePorts = [pscustomobject]@{
    GetInteractiveContext = { $lifecycleCase.Context }
    EnableNetworkTime = {
        $lifecycleCase.Calls.Add('EnableNetworkTime')
        [pscustomobject]@{ Source = 'lifecycle.fixture.time'; Status = 'fixture status' }
    }
    RestoreNetworkTime = {
        param([object] $Snapshot)
        $lifecycleCase.Calls.Add('RestoreNetworkTime')
    }
    StopApplication = {
        param([object] $Layout)
        $lifecycleCase.Calls.Add('StopApplication')
        if ($null -ne $lifecycleCase.SettingsFlushedOnStop) {
            # 旧应用退出时写回设置：事务快照必须捕获静止之后的这份内容。
            [System.IO.File]::WriteAllText($Layout.SettingsPath, $lifecycleCase.SettingsFlushedOnStop, $lifecycleUtf8)
        }
    }
    RemoveScheduledTask = {
        param([string] $TaskName)
        $lifecycleCase.Calls.Add("RemoveScheduledTask:$TaskName")
        if ($null -ne $lifecycleCase.OnRemoveScheduledTask) { & $lifecycleCase.OnRemoveScheduledTask }
    }
    RegisterScheduledTask = {
        param([object] $Layout, [string] $PowerShell7Path, [string] $InteractiveUser)
        $lifecycleCase.Calls.Add('RegisterScheduledTask')
        if ($null -ne $lifecycleCase.RegisterFailure) { throw $lifecycleCase.RegisterFailure }
        $lifecycleCase.RegisteredUser = $InteractiveUser
        $lifecycleCase.Task = [pscustomobject]@{
            TaskName = $Layout.TaskName
            State = 'Ready'
            Identity = 'lifecycle-task-v1'
            Actions = [pscustomobject]@{ Execute = $PowerShell7Path; Arguments = $Layout.TaskArguments }
            Principal = [pscustomobject]@{ LogonType = 'Interactive' }
        }
        $lifecycleCase.Task
    }
    StartScheduledTask = {
        param([string] $TaskName)
        $lifecycleCase.Calls.Add('StartScheduledTask')
        # 任务启动即回报首帧就绪；需要事务验收的用例再起一个按 release/finalize 协议应答的测试应用。
        $request = [System.IO.File]::ReadAllText($lifecycleCase.Layout.StartupRequestPath, [System.Text.Encoding]::UTF8) | ConvertFrom-Json -DateKind String
        [void] (Write-CountdownWindowReadyReceipt -Path $lifecycleCase.Layout.RuntimeStatusPath -LaunchToken $request.launchToken -ProcessId 4242 -SessionId 7 -ApplicationPath $lifecycleCase.Layout.ApplicationPath -CalendarState Ready)
        if ($lifecycleCase.LaunchApplication) {
            $lifecycleCase.ApplicationJob = Start-CountdownAcceptanceTestApplication -StartupRequest $request -Layout $lifecycleCase.Layout
            [void] $lifecycleJobs.Add($lifecycleCase.ApplicationJob)
        }
    }
    GetScheduledTask = {
        param([string] $TaskName)
        $lifecycleCase.Task
    }
    GetScheduledTaskIdentity = {
        param([object] $ScheduledTask)
        [string] $ScheduledTask.Identity
    }
    GetScheduledTaskInfo = {
        param([string] $TaskName)
        [pscustomobject]@{ LastRunTime = $null; LastTaskResult = 0; NextRunTime = $null }
    }
    ProcessProbe = {
        param([int] $ProcessId)
        [pscustomobject]@{ ProcessId = $ProcessId; SessionId = 7; ExecutablePath = $lifecycleCase.Layout.ExecutablePath; CommandLine = '"MinminCountdown.exe"' }
    }
    RefreshDesktopIcons = { $lifecycleCase.Calls.Add('RefreshDesktopIcons') }
}

try {
    New-Item -ItemType Directory -Path (Join-Path $lifecycleStagingRoot 'app'), $lifecycleJunctionTarget -Force | Out-Null
    [System.IO.File]::WriteAllText((Join-Path $lifecycleStagingRoot 'CountdownLauncher.exe'), 'lifecycle launcher v2', $lifecycleUtf8)
    [System.IO.File]::WriteAllText((Join-Path $lifecycleStagingRoot '敏敏子倒计时.ps1'), '# lifecycle adapter v2', $lifecycleUtf8)
    [System.IO.File]::WriteAllText((Join-Path $lifecycleStagingRoot 'app\MinminCountdown.exe'), 'lifecycle host v2', $lifecycleUtf8)
    $lifecycleLauncherHash = (Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $lifecycleStagingRoot 'CountdownLauncher.exe')).Hash
    $lifecycleReleaseContract = [pscustomobject]@{
        IntegrityHashes = @{ 'CountdownLauncher.exe' = $lifecycleLauncherHash }
        InstalledFiles = @('CountdownLauncher.exe', '敏敏子倒计时.ps1', 'app\MinminCountdown.exe')
        RetiredRuntimeFiles = @('retired-runtime.txt')
    }
    $lifecycleModule = (Get-Command Invoke-CountdownInstallation).Module
    Assert-Equal (Join-Path $PSScriptRoot 'Countdown.InstallLifecycle.psm1') ([string] $lifecycleModule.Path) 'Install lifecycle tests run against the repository module'

    # 网络时间源等待：跳过本地时钟与失败查询，首个网络时间源即返回；超时报出最后一次探测。
    $lifecycleTimeProbes = [System.Collections.Generic.Queue[object]]::new()
    $lifecycleTimeProbes.Enqueue([pscustomobject]@{ ExitCode = 0; Source = 'Local CMOS Clock' })
    $lifecycleTimeProbes.Enqueue([pscustomobject]@{ ExitCode = 1; Source = '' })
    $lifecycleTimeProbes.Enqueue([pscustomobject]@{ ExitCode = 0; Source = 'time.windows.com,0x9' })
    $lifecycleNetworkSource = & $lifecycleModule {
        param([scriptblock] $Query)
        Wait-CountdownNetworkTimeSource -TimeoutSeconds 5 -PollMilliseconds 50 -QueryTimeSourceAction $Query
    } { $lifecycleTimeProbes.Dequeue() }
    Assert-Equal 'time.windows.com,0x9' $lifecycleNetworkSource 'Network time wait skips the local clock and a failed query until a network source appears'
    Assert-Equal 0 $lifecycleTimeProbes.Count 'Network time wait returns on the first network source'
    $lifecycleFreeRunningMessage = $null
    try {
        & $lifecycleModule {
            param([scriptblock] $Query)
            Wait-CountdownNetworkTimeSource -TimeoutSeconds 1 -PollMilliseconds 50 -QueryTimeSourceAction $Query
        } { [pscustomobject]@{ ExitCode = 0; Source = 'Free-running System Clock' } } | Out-Null
    }
    catch {
        $lifecycleFreeRunningMessage = $_.Exception.Message
    }
    Assert-Equal 'Windows Time did not report a network source within 1 seconds: exit=0; source=Free-running System Clock' $lifecycleFreeRunningMessage 'Network time wait times out on a free-running clock and reports the last probe'

    # 延迟提交：装到 window-ready 后保留待验收事务与回执、不刷新桌面图标；随后 Complete 完成事务验收。
    $lifecycleCase = New-CountdownLifecycleTestCase -Name 'deferred'
    $deferredLayout = $lifecycleCase.Layout
    $lifecycleCase.LaunchApplication = $true
    $deferredReceiptPath = Join-Path $lifecycleCase.Root 'receipt\deferred-install-receipt.json'
    $deferredInstall = Invoke-CountdownLifecycleTestInstallation -DeferredReceiptPath $deferredReceiptPath
    Assert-Equal "EnableNetworkTime|StopApplication|RemoveScheduledTask:$($deferredLayout.LegacyTaskName)|RegisterScheduledTask|StartScheduledTask" ($lifecycleCase.Calls -join '|') 'Deferred install configures time, quiesces, removes the legacy task, then registers and starts the task without refreshing icons'
    Assert-Equal $true ([bool] $deferredInstall.DeferredCommit) 'Deferred install reports a deferred commit'
    Assert-Equal 'lifecycle.fixture.time' ([string] $deferredInstall.TimeSource) 'Deferred install reports the time source from the network time port'
    Assert-Equal $true (Test-Path -LiteralPath $deferredInstall.TransactionPath -PathType Container) 'Deferred install keeps the pending transaction for office acceptance'
    $deferredReceipt = Read-CountdownDeferredInstallReceipt -Path $deferredReceiptPath
    Assert-Equal ([string] $deferredInstall.TransactionPath) ([string] $deferredReceipt.transactionPath) 'Deferred receipt names the pending transaction'
    Assert-Equal ([string] $deferredInstall.LaunchToken) ([string] $deferredReceipt.launchToken) 'Deferred receipt carries the candidate launch token'
    Assert-Equal 7 ([int] $deferredReceipt.interactiveSessionId) 'Deferred receipt records the interactive session'
    Assert-Equal 'lifecycle host v2' ([System.IO.File]::ReadAllText($deferredLayout.ExecutablePath, [System.Text.Encoding]::UTF8)) 'Deferred install copies the release payload'
    Assert-Equal (@(Get-ChildItem -LiteralPath $deferredLayout.InstallPath -File -Recurse).Count) ([int] $deferredInstall.InstalledFileCount) 'Deferred install inventories every installed file'
    Assert-Equal $lifecycleLauncherHash (Get-FileHash -Algorithm SHA256 -LiteralPath $deferredLayout.LauncherPath).Hash 'Deferred install places the verified launcher on the desktop'
    Assert-Equal $false (Test-Path -LiteralPath $deferredLayout.LauncherTempPath) 'Deferred install never stages the launcher candidate beside the desktop launcher'
    Assert-Equal $false (Test-Path -LiteralPath (Join-Path $deferredInstall.TransactionPath 'candidate-launcher.exe')) 'Deferred install moves the transaction-held candidate onto the desktop'
    $deferredLauncherState = [System.IO.File]::ReadAllText($deferredLayout.LauncherStatePath, [System.Text.Encoding]::UTF8) | ConvertFrom-Json
    Assert-Equal $deferredLayout.LauncherPath ([string] $deferredLauncherState.desktopPath) 'Launcher state records the managed desktop path'
    Assert-Equal $lifecycleLauncherHash ([string] $deferredLauncherState.desktopSha256) 'Launcher state records the managed launcher hash'
    $deferredJournal = [System.IO.File]::ReadAllText((Join-Path $deferredInstall.TransactionPath 'journal.json'), [System.Text.Encoding]::UTF8) | ConvertFrom-Json
    Assert-Equal $lifecycleLauncherHash ([string] $deferredJournal.candidateLauncherSha256) 'Pending transaction journal records the candidate launcher hash'
    Assert-Equal 'active' ([string] (([System.IO.File]::ReadAllText($deferredLayout.StartupRequestPath, [System.Text.Encoding]::UTF8) | ConvertFrom-Json).acceptanceState)) 'Deferred install keeps the candidate application read-only until acceptance'
    $lifecycleStages = [System.Collections.Generic.List[string]]::new()
    $lifecycleCommittedPhases = [System.Collections.Generic.List[string]]::new()
    $deferredAcceptance = Complete-CountdownInstallation `
        -Layout $deferredLayout `
        -TransactionPath $deferredInstall.TransactionPath `
        -InteractiveContext $lifecycleCase.Context `
        -LaunchToken $deferredInstall.LaunchToken `
        -ExpectedProcessId ([int] $deferredInstall.RuntimeStatus.processId) `
        -ExpectedScheduledTaskIdentity 'lifecycle-task-v1' `
        -OnStage { param([string] $Stage) $lifecycleStages.Add($Stage) } `
        -OnCommitted { $lifecycleCommittedPhases.Add([string] (([System.IO.File]::ReadAllText((Join-Path $deferredInstall.TransactionPath 'journal.json'), [System.Text.Encoding]::UTF8) | ConvertFrom-Json).phase)) } `
        -Ports $lifecyclePorts
    Assert-Equal 'finalized-ready' ([string] $deferredAcceptance.AcceptanceState) 'Completing a deferred install returns finalized-ready'
    Assert-Equal '提交安装事务|清理已提交事务' ($lifecycleStages -join '|') 'Completing a deferred install reports the commit stage before the cleanup stage'
    Assert-Equal 'committed' ($lifecycleCommittedPhases -join '|') 'OnCommitted runs once, only after the journal records committed'
    Assert-Equal $false (Test-Path -LiteralPath $deferredInstall.TransactionPath) 'Completing a deferred install removes the committed transaction'
    Assert-Equal $false (Test-Path -LiteralPath (Join-Path $lifecycleCase.Root '.minmin-countdown-transactions')) 'Completing a deferred install leaves no transaction base behind'
    Assert-Equal 'finalized' ([string] (([System.IO.File]::ReadAllText($deferredLayout.StartupRequestPath, [System.Text.Encoding]::UTF8) | ConvertFrom-Json).acceptanceState)) 'Completing a deferred install finalizes the startup request'
    Assert-Equal 5 $lifecycleCase.Calls.Count 'Completing a deferred install does not refresh desktop icons'
    Wait-Job -Job $lifecycleCase.ApplicationJob -Timeout 5 | Out-Null
    Assert-Equal 'Completed' ([string] $lifecycleCase.ApplicationJob.State) 'Deferred acceptance drives the application through release and finalize'

    # 回滚：恢复静止后捕获的安装目录、受管桌面启动器、启动器状态与退役文件，并经 port 恢复时间服务。
    $lifecycleCase = New-CountdownLifecycleTestCase -Name 'undo'
    $undoLayout = $lifecycleCase.Layout
    New-Item -ItemType Directory -Path $undoLayout.InstallPath -Force | Out-Null
    [System.IO.File]::WriteAllText($undoLayout.SettingsPath, '{"before":true}', $lifecycleUtf8)
    [System.IO.File]::WriteAllText($undoLayout.ApplicationPath, '# old adapter', $lifecycleUtf8)
    [System.IO.File]::WriteAllText((Join-Path $undoLayout.InstallPath 'retired-runtime.txt'), 'retired runtime', $lifecycleUtf8)
    [System.IO.File]::WriteAllText($undoLayout.LauncherPath, 'old managed launcher', $lifecycleUtf8)
    $undoOldLauncherHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $undoLayout.LauncherPath).Hash
    [System.IO.File]::WriteAllText($undoLayout.LauncherStatePath, ([ordered]@{ schemaVersion = 1; desktopPath = $undoLayout.LauncherPath; desktopSha256 = $undoOldLauncherHash } | ConvertTo-Json), $lifecycleUtf8)
    $lifecycleCase.SettingsFlushedOnStop = '{"flushed-on-exit":true}'
    $undoInstall = Invoke-CountdownLifecycleTestInstallation -DeferredReceiptPath (Join-Path $lifecycleCase.Root 'receipt\deferred-install-receipt.json')
    Assert-Equal $false (Test-Path -LiteralPath (Join-Path $undoLayout.InstallPath 'retired-runtime.txt')) 'Install removes retired runtime files'
    Assert-Equal $lifecycleLauncherHash (Get-FileHash -Algorithm SHA256 -LiteralPath $undoLayout.LauncherPath).Hash 'Install replaces a managed desktop launcher'
    [System.IO.File]::WriteAllText($undoLayout.SettingsPath, '{"candidate":true}', $lifecycleUtf8)
    $undoResult = Undo-CountdownInstallation -Layout $undoLayout -TransactionPath $undoInstall.TransactionPath -PowerShell7Path $lifecyclePowerShell -Ports $lifecyclePorts
    Assert-Equal $false ([bool] $undoResult.PreservedUnmanagedLaunchers) 'Rollback of a managed install preserves no unmanaged launcher'
    Assert-Equal '{"flushed-on-exit":true}' ([System.IO.File]::ReadAllText($undoLayout.SettingsPath, [System.Text.Encoding]::UTF8)) 'Rollback restores settings captured after the old application quiesced'
    Assert-Equal '# old adapter' ([System.IO.File]::ReadAllText($undoLayout.ApplicationPath, [System.Text.Encoding]::UTF8)) 'Rollback restores the previous application files'
    Assert-Equal $false (Test-Path -LiteralPath $undoLayout.ExecutablePath) 'Rollback removes payload files absent from the previous install'
    Assert-Equal 'retired runtime' ([System.IO.File]::ReadAllText((Join-Path $undoLayout.InstallPath 'retired-runtime.txt'), [System.Text.Encoding]::UTF8)) 'Rollback restores retired runtime files'
    Assert-Equal $undoOldLauncherHash (Get-FileHash -Algorithm SHA256 -LiteralPath $undoLayout.LauncherPath).Hash 'Rollback restores the previous managed desktop launcher'
    Assert-Equal $undoOldLauncherHash ([string] (([System.IO.File]::ReadAllText($undoLayout.LauncherStatePath, [System.Text.Encoding]::UTF8) | ConvertFrom-Json).desktopSha256)) 'Rollback restores the previous launcher state'
    Assert-Equal "EnableNetworkTime|StopApplication|RemoveScheduledTask:$($undoLayout.LegacyTaskName)|RegisterScheduledTask|StartScheduledTask|RestoreNetworkTime" ($lifecycleCase.Calls -join '|') 'Rollback restores network time through the time port'
    Assert-Equal $false (Test-Path -LiteralPath (Join-Path $lifecycleCase.Root '.minmin-countdown-transactions')) 'Rollback removes the transaction'

    # 准备期间用户替换桌面启动器：拒绝覆盖，恢复应用、任务与时间服务，保留用户文件与事务证据；再次安装在恢复阶段拒绝。
    $lifecycleCase = New-CountdownLifecycleTestCase -Name 'conflict'
    $conflictLayout = $lifecycleCase.Layout
    $lifecycleCase.OnRemoveScheduledTask = { [System.IO.File]::WriteAllText($lifecycleCase.Layout.LauncherPath, 'user replacement launcher', $lifecycleUtf8) }
    $conflictMessage = $null
    try { Invoke-CountdownLifecycleTestInstallation | Out-Null }
    catch { $conflictMessage = $_.Exception.Message }
    Assert-Equal $true (([string] $conflictMessage).StartsWith("安装失败，已恢复应用、任务和时间服务；为保护用户替换的桌面启动器，保留了事务证据：Refusing to overwrite a desktop launcher changed while installation was preparing: $($conflictLayout.LauncherPath)；")) "A launcher replaced while preparing aborts the install: $conflictMessage"
    Assert-Equal 'user replacement launcher' ([System.IO.File]::ReadAllText($conflictLayout.LauncherPath, [System.Text.Encoding]::UTF8)) 'Launcher ownership conflict keeps the user replacement on the desktop'
    Assert-Equal $false (Test-Path -LiteralPath $conflictLayout.InstallPath) 'Launcher ownership conflict still rolls back the install directory'
    Assert-Equal "EnableNetworkTime|StopApplication|RemoveScheduledTask:$($conflictLayout.LegacyTaskName)|RestoreNetworkTime" ($lifecycleCase.Calls -join '|') 'Launcher ownership conflict restores network time without registering the task'
    $conflictTransactions = @(Get-ChildItem -LiteralPath (Join-Path $lifecycleCase.Root '.minmin-countdown-transactions') -Directory)
    Assert-Equal 1 $conflictTransactions.Count 'Launcher ownership conflict keeps one transaction as evidence'
    Assert-Equal 'launcher-ownership-conflict' ([string] (([System.IO.File]::ReadAllText((Join-Path $conflictTransactions[0].FullName 'journal.json'), [System.Text.Encoding]::UTF8) | ConvertFrom-Json).phase)) 'Preserved transaction records the launcher ownership conflict'
    $lifecycleCase.Calls.Clear()
    $conflictRetryMessage = $null
    try { Invoke-CountdownLifecycleTestInstallation | Out-Null }
    catch { $conflictRetryMessage = $_.Exception.Message }
    Assert-Equal '检测到用户替换的桌面启动器：应用、任务和时间服务已恢复，但事务证据被保留，拒绝继续安装。' $conflictRetryMessage 'Reinstall refuses while the user replacement launcher is unresolved'
    Assert-Equal 0 $lifecycleCase.Calls.Count 'Refused reinstall starts no transaction work'

    # 阶段失败：带延迟回执时写出部署器可严格解析的故障回执，并完整回滚。
    $lifecycleCase = New-CountdownLifecycleTestCase -Name 'register'
    $registerLayout = $lifecycleCase.Layout
    $lifecycleCase.RegisterFailure = 'register-task fixture failure'
    $registerReceiptPath = Join-Path $lifecycleCase.Root 'receipt\deferred-install-receipt.json'
    $registerMessage = $null
    try { Invoke-CountdownLifecycleTestInstallation -DeferredReceiptPath $registerReceiptPath | Out-Null }
    catch { $registerMessage = $_.Exception.Message }
    Assert-Equal $true (([string] $registerMessage).StartsWith('安装失败，已回滚：register-task fixture failure；安装阶段=register-task；')) "A failed task registration rolls back and names the failing stage: $registerMessage"
    Assert-Equal $false (Test-Path -LiteralPath $registerReceiptPath) 'A failed deferred install writes no success receipt'
    $registerFailureReceipt = ConvertFrom-CountdownInstallFailureReceiptBytes -Bytes ([System.IO.File]::ReadAllBytes((Join-Path $lifecycleCase.Root 'receipt\deployment-failure.json')))
    Assert-Equal 'register-task' ([string] $registerFailureReceipt.stage) 'Failure receipt names the failing stage'
    Assert-Equal 'register-task fixture failure' ([string] $registerFailureReceipt.exceptionMessage) 'Failure receipt keeps the original error message'
    Assert-Equal 'not-committed' ([string] $registerFailureReceipt.commitState) 'Failure receipt reports the uncommitted transaction'
    Assert-Equal $true ([bool] $registerFailureReceipt.transactionStarted -and [bool] $registerFailureReceipt.rollbackAttempted -and [bool] $registerFailureReceipt.rollbackSucceeded) 'Failure receipt reports a started transaction and a successful rollback'
    Assert-Equal $false (Test-Path -LiteralPath $registerLayout.LauncherPath) 'Failed install removes the newly placed desktop launcher'
    Assert-Equal $false (Test-Path -LiteralPath $registerLayout.InstallPath) 'Failed install removes a fresh install directory'
    Assert-Equal "EnableNetworkTime|StopApplication|RemoveScheduledTask:$($registerLayout.LegacyTaskName)|RegisterScheduledTask|RestoreNetworkTime" ($lifecycleCase.Calls -join '|') 'Failed install restores network time and never starts the task'

    # 快照之后出现在安装目录里的 junction：复查在写入任何 payload 之前拒绝，回滚也拒绝穿过它删除。
    $lifecycleCase = New-CountdownLifecycleTestCase -Name 'reparse'
    $lifecycleJunctionPath = Join-Path $lifecycleCase.Layout.InstallPath 'app'
    $lifecycleCase.OnRemoveScheduledTask = {
        New-Item -ItemType Directory -Path $lifecycleCase.Layout.InstallPath -Force | Out-Null
        New-Item -ItemType Junction -Path $lifecycleJunctionPath -Target $lifecycleJunctionTarget | Out-Null
    }
    $reparseMessage = $null
    try { Invoke-CountdownLifecycleTestInstallation | Out-Null }
    catch { $reparseMessage = $_.Exception.Message }
    Assert-Equal $true (([string] $reparseMessage).StartsWith("安装失败：安装事务目录树包含 reparse point：$lifecycleJunctionPath；安装阶段=quiesce-existing-application；")) "A junction planted after the snapshot stops the install before payload copy: $reparseMessage"
    Assert-Equal $true (([string] $reparseMessage).Contains("回滚也失败：安装事务目录树包含 reparse point：$lifecycleJunctionPath")) 'Rollback refuses to delete through the planted junction'
    Assert-Equal 0 (@(Get-ChildItem -LiteralPath $lifecycleJunctionTarget -Force).Count) 'No payload file is written through a junction planted after the snapshot'
    Assert-Equal $false ($lifecycleCase.Calls -contains 'RegisterScheduledTask') 'Install with a planted junction never registers the task'

    # 本机安装：就地完成事务验收后才刷新桌面图标，事务随提交清理。
    $lifecycleCase = New-CountdownLifecycleTestCase -Name 'local'
    $localLayout = $lifecycleCase.Layout
    $lifecycleCase.LaunchApplication = $true
    $localInstall = Invoke-CountdownLifecycleTestInstallation
    Assert-Equal "EnableNetworkTime|StopApplication|RemoveScheduledTask:$($localLayout.LegacyTaskName)|RegisterScheduledTask|StartScheduledTask|RefreshDesktopIcons" ($lifecycleCase.Calls -join '|') 'Local install refreshes desktop icons only after transaction acceptance'
    Assert-Equal $false ([bool] $localInstall.DeferredCommit) 'Local install commits in place'
    Assert-Equal $true ($null -eq $localInstall.TransactionPath) 'Local install reports no pending transaction'
    Assert-Equal 'MINMIN\LifecycleUser' ([string] $lifecycleCase.RegisteredUser) 'Local install registers the task for the interactive user'
    Assert-Equal $false (Test-Path -LiteralPath (Join-Path $lifecycleCase.Root '.minmin-countdown-transactions')) 'Local install removes the committed transaction'
    Assert-Equal 'finalized' ([string] (([System.IO.File]::ReadAllText($localLayout.StartupRequestPath, [System.Text.Encoding]::UTF8) | ConvertFrom-Json).acceptanceState)) 'Local install finalizes the startup request'
    Wait-Job -Job $lifecycleCase.ApplicationJob -Timeout 5 | Out-Null
    Assert-Equal 'Completed' ([string] $lifecycleCase.ApplicationJob.State) 'Local install drives the application through release and finalize'
}
finally {
    foreach ($job in $lifecycleJobs) {
        Stop-Job -Job $job -ErrorAction SilentlyContinue
        Remove-Job -Job $job -Force -ErrorAction SilentlyContinue
    }
    if (-not [string]::IsNullOrWhiteSpace([string] $lifecycleJunctionPath) -and (Test-Path -LiteralPath $lifecycleJunctionPath)) {
        Remove-Item -LiteralPath $lifecycleJunctionPath -Force -ErrorAction SilentlyContinue
    }
    Remove-Item -LiteralPath $lifecycleTestRoot -Recurse -Force -ErrorAction SilentlyContinue
}
