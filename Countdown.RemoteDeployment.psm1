#requires -Version 7.0

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-CountdownRemoteDeploymentProperty {
    param(
        [Parameter(Mandatory)] [object] $InputObject,
        [Parameter(Mandatory)] [string] $Name,
        [switch] $Required
    )

    $property = $InputObject.PSObject.Properties[$Name]
    if ($Required -and $null -eq $property) {
        throw "远端部署结果缺少字段：$Name"
    }
    if ($null -eq $property) { return $null }
    $property.Value
}

function Get-CountdownRemoteDeploymentCommitted {
    param([Parameter(Mandatory)] [object] $Adapter)

    $stateProperty = $Adapter.PSObject.Properties['State']
    if ($null -eq $stateProperty -or $null -eq $stateProperty.Value) {
        throw '远端部署 adapter 缺少 State。'
    }
    $committedProperty = $stateProperty.Value.PSObject.Properties['Committed']
    if ($null -eq $committedProperty -or $committedProperty.Value -isnot [bool]) {
        throw '远端部署 adapter 的 State.Committed 必须是 bool。'
    }
    [bool] $committedProperty.Value
}

function Assert-CountdownRemoteDeploymentAdapter {
    param([Parameter(Mandatory)] [object] $Adapter)

    foreach ($name in @('State', 'Begin', 'Accept', 'Rollback', 'Dispose')) {
        $property = $Adapter.PSObject.Properties[$name]
        if ($null -eq $property) {
            throw "远端部署 adapter 缺少 $name。"
        }
        if ($name -ne 'State' -and $property.Value -isnot [scriptblock]) {
            throw "远端部署 adapter 的 $name 必须是 scriptblock。"
        }
    }
    $finalizeProperty = $Adapter.PSObject.Properties['Finalize']
    if ($null -ne $finalizeProperty -and $finalizeProperty.Value -isnot [scriptblock]) {
        throw '远端部署 adapter 的 Finalize 必须是 scriptblock。'
    }
    if (Get-CountdownRemoteDeploymentCommitted -Adapter $Adapter) {
        throw '远端部署 adapter 的初始提交状态必须为 false。'
    }
    $Adapter
}

function New-CountdownRemoteDeploymentWindowsAdapter {
    $state = [pscustomobject]@{
        InstallMutex = $null
        TransactionModuleImported = $false
        InstallStarted = $false
        Committed = $false
        RollbackTransactionPath = $null
        InteractiveContext = $null
        LauncherPath = $null
        LegacyLauncherPath = $null
        LauncherTempPath = $null
        SettingsSnapshotPath = $null
        SettingsPath = $null
        StartupRequestPath = $null
        RuntimeStatusPath = $null
        AcceptanceStatusPath = $null
        LaunchToken = $null
        ExpectedProcessId = 0
        ExpectedScheduledTaskIdentity = $null
    }

    [pscustomobject]@{
        State = $state
        Begin = {
            param($AdapterState, $Context)

            Import-Module (Join-Path $Context.SourceRoot 'Countdown.ReleaseContract.psm1') -Force
            $releaseContract = Get-CountdownReleaseContract
            [void] (Assert-CountdownReleasePackage -Root $Context.SourceRoot)

            Import-Module (Join-Path $Context.SourceRoot 'Countdown.InstallTransaction.psm1') -Force -DisableNameChecking
            $AdapterState.TransactionModuleImported = $true
            $interactiveContext = Get-CountdownInteractiveDesktopContext
            $launcherPath = Join-Path ([string] $interactiveContext.Desktop) '敏敏子倒计时.exe'
            $legacyLauncherPath = Join-Path ([string] $interactiveContext.Desktop) '敏敏子倒计时.cmd'
            $launcherTempPath = "$launcherPath.new"
            $AdapterState.InteractiveContext = $interactiveContext
            $AdapterState.LauncherPath = $launcherPath
            $AdapterState.LegacyLauncherPath = $legacyLauncherPath
            $AdapterState.LauncherTempPath = $launcherTempPath
            $AdapterState.InstallMutex = Enter-CountdownInstallTransactionMutex -TimeoutSeconds 120

            $settingsPath = Join-Path $Context.InstallPath 'settings.json'
            $installerArguments = @{
                DeferCommit = $true
                DeferredReceiptPath = $Context.DeferredReceiptPath
                ParentTransactionMutexHeld = $true
                ExpectedInteractiveUser = [string] $interactiveContext.User
                ExpectedInteractiveSessionId = [int] $interactiveContext.SessionId
                ExpectedDesktopPath = [string] $interactiveContext.Desktop
            }
            $AdapterState.InstallStarted = $true
            $installerOutput = & (Join-Path $Context.SourceRoot 'Install-Countdown.ps1') @installerArguments | Out-String
            $receipt = Read-CountdownDeferredInstallReceipt -Path $Context.DeferredReceiptPath
            if ($null -eq $receipt) {
                throw '安装器没有写入可用于回滚的延迟提交回执。'
            }
            $AdapterState.RollbackTransactionPath = [string] $receipt.transactionPath
            try {
                $installer = $installerOutput | ConvertFrom-Json -ErrorAction Stop
            }
            catch {
                throw "安装器返回了无效 JSON：$($_.Exception.Message)"
            }
            if (-not [bool] $installer.DeferredCommit -or
                [string]::IsNullOrWhiteSpace([string] $installer.TransactionPath)) {
                throw '安装器没有保留待验收事务。'
            }
            if (-not [string]::Equals(
                    [System.IO.Path]::GetFullPath([string] $installer.TransactionPath),
                    [System.IO.Path]::GetFullPath([string] $receipt.transactionPath),
                    [System.StringComparison]::OrdinalIgnoreCase)) {
                throw '安装器输出与独立事务回执不一致。'
            }
            if (-not [string]::Equals(
                    [string] $installer.LaunchToken,
                    [string] $receipt.launchToken,
                    [System.StringComparison]::Ordinal)) {
                throw '安装器输出与独立事务回执的启动令牌不一致。'
            }
            if ([int] $installer.InteractiveSessionId -ne [int] $receipt.interactiveSessionId) {
                throw '安装器输出与独立事务回执的交互会话不一致。'
            }

            $validationProperty = $installer.PSObject.Properties['Validation']
            if ($null -eq $validationProperty) {
                throw '安装器没有返回稳定 validation interface。'
            }
            $validation = $validationProperty.Value

            foreach ($file in $releaseContract.InstalledFiles) {
                $expectedHash = [string] $releaseContract.IntegrityHashes[$file]
                $installedPath = Join-Path $Context.InstallPath $file
                if ((Get-FileHash -Algorithm SHA256 -LiteralPath $installedPath).Hash -ne $expectedHash) {
                    throw "安装后哈希不匹配：$file"
                }
            }
            foreach ($file in $releaseContract.RetiredRuntimeFiles) {
                if (Test-Path -LiteralPath (Join-Path $Context.InstallPath $file)) {
                    throw "安装目录仍包含已淘汰悬浮窗文件：$file"
                }
            }

            Import-Module (Join-Path $Context.InstallPath 'Countdown.Settings.psm1') -Force
            Import-Module (Join-Path $Context.SourceRoot 'Countdown.Runtime.psm1') -Force
            Import-Module (Join-Path $Context.SourceRoot 'Countdown.TransactionAcceptance.psm1') -Force

            $applicationPath = Join-Path $Context.InstallPath '敏敏子倒计时.ps1'
            $installedValidationOutput = & $Context.PowerShell7Path `
                -NoLogo -NoProfile -NonInteractive -STA -File $applicationPath -ValidateOnly | Out-String
            if ($LASTEXITCODE -ne 0) {
                throw "安装后应用 validate-only 失败：$installedValidationOutput"
            }
            try {
                $installedValidation = $installedValidationOutput | ConvertFrom-Json -ErrorAction Stop
            }
            catch {
                throw "安装后应用 validate-only 返回了无效 JSON：$($_.Exception.Message)"
            }
            if (-not [bool] $installedValidation.Valid -or
                -not [bool] $installedValidation.FloatingWindowPresent -or
                -not [bool] $installedValidation.FloatingManagerButtonPresent) {
                throw '安装后悬浮框验证失败。'
            }

            $task = Get-ScheduledTask -TaskName $Context.TaskName -ErrorAction Stop
            $runtimeStatusPath = Join-Path $Context.InstallPath 'runtime-status.json'
            $runtimeStatus = Wait-CountdownWindowReady `
                -Path $runtimeStatusPath `
                -LaunchToken ([string] $receipt.launchToken) `
                -ExpectedSessionId ([int] $receipt.interactiveSessionId) `
                -ExpectedApplicationPath $applicationPath `
                -TimeoutSeconds 5 `
                -PollMilliseconds 250
            if ([string] $task.State -ne 'Running') {
                throw "计划任务未运行：$($task.State)"
            }
            if (-not [string]::Equals(
                    [System.IO.Path]::GetFullPath([string] $task.Actions.Execute),
                    [System.IO.Path]::GetFullPath($Context.PowerShell7Path),
                    [System.StringComparison]::OrdinalIgnoreCase)) {
                throw '计划任务未使用当前 PowerShell 7。'
            }

            $AdapterState.SettingsSnapshotPath = Join-Path ([string] $receipt.transactionPath) 'install-backup\settings.json'
            $AdapterState.SettingsPath = $settingsPath
            $AdapterState.StartupRequestPath = Join-Path $Context.InstallPath 'startup-request.json'
            $AdapterState.RuntimeStatusPath = $runtimeStatusPath
            $AdapterState.AcceptanceStatusPath = Join-Path $Context.InstallPath 'acceptance-status.json'
            $AdapterState.LaunchToken = [string] $receipt.launchToken
            $AdapterState.ExpectedProcessId = [int] $runtimeStatus.processId
            $AdapterState.ExpectedScheduledTaskIdentity = [string] (Export-ScheduledTask -InputObject $task -ErrorAction Stop)

            [pscustomobject]@{
                Validation = $validation
                RuntimeStatus = $runtimeStatus
                InstalledValidation = $installedValidation
            }
        }
        Accept = {
            param($AdapterState, $Context, $Candidate)

            Import-Module (Join-Path $Context.SourceRoot 'Countdown.InstallTransaction.psm1') -Force -DisableNameChecking
            Import-Module (Join-Path $Context.InstallPath 'Countdown.Settings.psm1') -Force
            Import-Module (Join-Path $Context.SourceRoot 'Countdown.Runtime.psm1') -Force
            Import-Module (Join-Path $Context.SourceRoot 'Countdown.TransactionAcceptance.psm1') -Force
            $getInteractiveContext = { Get-CountdownInteractiveDesktopContext }
            $getScheduledTask = {
                param([string] $ExpectedTaskName)
                Get-ScheduledTask -TaskName $ExpectedTaskName -ErrorAction Stop
            }
            $getScheduledTaskIdentity = {
                param([object] $ScheduledTask)
                [string] (Export-ScheduledTask -InputObject $ScheduledTask -ErrorAction Stop)
            }
            $completeTransaction = {
                Complete-CountdownInstallTransaction `
                    -TransactionPath $AdapterState.RollbackTransactionPath `
                    -ExpectedInstallPath $Context.InstallPath `
                    -ExpectedTaskName $Context.TaskName `
                    -ExpectedLegacyTaskName $Context.LegacyTaskName `
                    -ExpectedLauncherPath $AdapterState.LauncherPath `
                    -ExpectedLegacyLauncherPath $AdapterState.LegacyLauncherPath `
                    -ExpectedLauncherTempPath $AdapterState.LauncherTempPath
            }
            $removeCommittedTransaction = {
                $moveCommittedTransaction = {
                    param([string] $Source, [string] $Destination)
                    # InstallTransaction 只在 journal 写入 committed 后调用此动作；移动失败也已不可回滚。
                    $AdapterState.Committed = $true
                    Move-Item -LiteralPath $Source -Destination $Destination -ErrorAction Stop
                }
                Remove-CountdownCommittedInstallTransaction `
                    -TransactionPath $AdapterState.RollbackTransactionPath `
                    -ExpectedInstallPath $Context.InstallPath `
                    -ExpectedTaskName $Context.TaskName `
                    -ExpectedLegacyTaskName $Context.LegacyTaskName `
                    -ExpectedLauncherPath $AdapterState.LauncherPath `
                    -ExpectedLegacyLauncherPath $AdapterState.LegacyLauncherPath `
                    -ExpectedLauncherTempPath $AdapterState.LauncherTempPath `
                    -MoveAction $moveCommittedTransaction
            }
            $acceptanceArguments = @{
                StartupRequestPath = $AdapterState.StartupRequestPath
                RuntimeStatusPath = $AdapterState.RuntimeStatusPath
                AcceptanceStatusPath = $AdapterState.AcceptanceStatusPath
                LaunchToken = $AdapterState.LaunchToken
                ExpectedInteractiveContext = $AdapterState.InteractiveContext
                ExpectedProcessId = $AdapterState.ExpectedProcessId
                ExpectedSessionId = [int] $AdapterState.InteractiveContext.SessionId
                ExpectedApplicationPath = Join-Path $Context.InstallPath '敏敏子倒计时.ps1'
                TaskName = $Context.TaskName
                ExpectedScheduledTaskIdentity = $AdapterState.ExpectedScheduledTaskIdentity
                SettingsSnapshotPath = $AdapterState.SettingsSnapshotPath
                SettingsPath = $AdapterState.SettingsPath
                GetInteractiveContext = $getInteractiveContext
                GetScheduledTask = $getScheduledTask
                GetScheduledTaskIdentity = $getScheduledTaskIdentity
                CompleteTransaction = $completeTransaction
                RemoveCommittedTransaction = $removeCommittedTransaction
            }
            Invoke-CountdownTransactionAcceptance @acceptanceArguments
        }
        Rollback = {
            param($AdapterState, $Context, $Candidate)

            if (-not [bool] $AdapterState.TransactionModuleImported -or -not [bool] $AdapterState.InstallStarted) {
                return [pscustomobject]@{
                    PreservedUnmanagedLaunchers = $false
                    UnresolvedCommittedTransaction = $false
                }
            }
            Import-Module (Join-Path $Context.SourceRoot 'Countdown.InstallTransaction.psm1') -Force -DisableNameChecking

            $preservedUnmanagedLaunchers = $false
            $unresolvedCommittedTransaction = $false
            $rollbackResult = $null
            if ([string]::IsNullOrWhiteSpace([string] $AdapterState.RollbackTransactionPath)) {
                $receipt = Read-CountdownDeferredInstallReceipt -Path $Context.DeferredReceiptPath
                if ($null -ne $receipt) {
                    $AdapterState.RollbackTransactionPath = [string] $receipt.transactionPath
                }
            }
            if (-not [string]::IsNullOrWhiteSpace([string] $AdapterState.RollbackTransactionPath)) {
                $journalPath = Join-Path $AdapterState.RollbackTransactionPath 'journal.json'
                if (-not (Test-Path -LiteralPath $journalPath -PathType Leaf)) {
                    throw '无法验证待回滚事务的 journal；拒绝声称已回滚。'
                }
                $rollbackResult = Undo-CountdownInstallTransaction `
                    -TransactionPath $AdapterState.RollbackTransactionPath `
                    -PowerShell7Path $Context.PowerShell7Path `
                    -ExpectedInstallPath $Context.InstallPath `
                    -ExpectedTaskName $Context.TaskName `
                    -ExpectedLegacyTaskName $Context.LegacyTaskName `
                    -ExpectedLauncherPath $AdapterState.LauncherPath `
                    -ExpectedLegacyLauncherPath $AdapterState.LegacyLauncherPath `
                    -ExpectedLauncherTempPath $AdapterState.LauncherTempPath `
                    -PreserveUnmanagedLaunchers
            }
            else {
                $recovery = Recover-CountdownInstallTransactions `
                    -InstallPath $Context.InstallPath `
                    -PowerShell7Path $Context.PowerShell7Path `
                    -ExpectedTaskName $Context.TaskName `
                    -ExpectedLegacyTaskName $Context.LegacyTaskName `
                    -ExpectedLauncherPath $AdapterState.LauncherPath `
                    -ExpectedLegacyLauncherPath $AdapterState.LegacyLauncherPath `
                    -ExpectedLauncherTempPath $AdapterState.LauncherTempPath
                $preservedUnmanagedLaunchers = @($recovery.UnresolvedLauncherOwnershipConflicts).Count -gt 0
                $unresolvedCommittedTransaction = @($recovery.UnresolvedCommittedTransactions).Count -gt 0
            }
            if ($null -ne $rollbackResult -and [bool] $rollbackResult.PreservedUnmanagedLaunchers) {
                $preservedUnmanagedLaunchers = $true
            }
            [pscustomobject]@{
                PreservedUnmanagedLaunchers = $preservedUnmanagedLaunchers
                UnresolvedCommittedTransaction = $unresolvedCommittedTransaction
            }
        }
        Finalize = {
            param($AdapterState, $Context)
            $iconRefreshPath = Join-Path $env:SystemRoot 'System32\ie4uinit.exe'
            $null = & $iconRefreshPath -show 2>$null
            if ($LASTEXITCODE -ne 0) {
                throw "桌面图标刷新失败，退出码：$LASTEXITCODE"
            }
        }
        Dispose = {
            param($AdapterState, $Context)
            if ($null -ne $AdapterState.InstallMutex) {
                Import-Module (Join-Path $Context.SourceRoot 'Countdown.InstallTransaction.psm1') -Force -DisableNameChecking
                Exit-CountdownInstallTransactionMutex -Mutex $AdapterState.InstallMutex
                $AdapterState.InstallMutex = $null
            }
        }
    }
}

function Invoke-CountdownRemoteDeployment {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [string] $SourceRoot,
        [Parameter(Mandatory)] [string] $InstallPath,
        [Parameter(Mandatory)] [string] $PowerShell7Path,
        [Parameter(Mandatory)] [string] $DeferredReceiptPath,
        [Parameter(Mandatory)] [string] $TaskName,
        [Parameter(Mandatory)] [string] $LegacyTaskName,
        [object] $Adapter
    )

    $context = [pscustomobject]@{
        SourceRoot = [System.IO.Path]::GetFullPath($SourceRoot)
        InstallPath = [System.IO.Path]::GetFullPath($InstallPath)
        PowerShell7Path = [System.IO.Path]::GetFullPath($PowerShell7Path)
        DeferredReceiptPath = [System.IO.Path]::GetFullPath($DeferredReceiptPath)
        TaskName = $TaskName
        LegacyTaskName = $LegacyTaskName
    }
    if (-not (Test-Path -LiteralPath $context.SourceRoot -PathType Container)) {
        throw "远端部署源目录不存在：$($context.SourceRoot)"
    }
    if (-not (Test-Path -LiteralPath $context.PowerShell7Path -PathType Leaf)) {
        throw "远端部署所需的 PowerShell 7 不存在：$($context.PowerShell7Path)"
    }
    if ([string]::IsNullOrWhiteSpace($TaskName) -or [string]::IsNullOrWhiteSpace($LegacyTaskName)) {
        throw '远端部署任务名称不能为空。'
    }

    if ($null -eq $Adapter) {
        $Adapter = New-CountdownRemoteDeploymentWindowsAdapter
    }
    $Adapter = Assert-CountdownRemoteDeploymentAdapter -Adapter $Adapter
    $candidate = $null
    $committed = $false
    $result = $null
    $deploymentFailure = $null
    try {
        try {
            $candidate = & $Adapter.Begin $Adapter.State $context
            if ($null -eq $candidate) {
                throw '远端部署 adapter 没有返回候选结果。'
            }
            $validation = Get-CountdownRemoteDeploymentProperty -InputObject $candidate -Name 'Validation' -Required
            $validationSchemaVersion = [int] (Get-CountdownRemoteDeploymentProperty `
                    -InputObject $validation `
                    -Name 'SchemaVersion' `
                    -Required)
            if ($validationSchemaVersion -ne 1) {
                throw "validation interface schemaVersion 不受支持：$validationSchemaVersion"
            }
            $validationValid = Get-CountdownRemoteDeploymentProperty -InputObject $validation -Name 'Valid' -Required
            if ($validationValid -isnot [bool] -or -not [bool] $validationValid) {
                throw '安装器内置校验未通过。'
            }
            $coreTestsPassed = [int] (Get-CountdownRemoteDeploymentProperty -InputObject $validation -Name 'CoreTestsPassed' -Required)
            $cSharpTestsPassed = [int] (Get-CountdownRemoteDeploymentProperty -InputObject $validation -Name 'CSharpTestsPassed' -Required)
            $uiTestsPassed = [int] (Get-CountdownRemoteDeploymentProperty -InputObject $validation -Name 'UiTestsPassed' -Required)
            $uiValid = Get-CountdownRemoteDeploymentProperty -InputObject $validation -Name 'UiValid' -Required
            if ($uiValid -isnot [bool] -or -not [bool] $uiValid) {
                throw '应用 validate-only 验证未通过。'
            }
            $adapterUiValidated = Get-CountdownRemoteDeploymentProperty -InputObject $validation -Name 'AdapterUiValidated' -Required
            if ($adapterUiValidated -isnot [bool] -or -not [bool] $adapterUiValidated) {
                throw 'adapter UI 验证未通过。'
            }
            $runtimeStatus = Get-CountdownRemoteDeploymentProperty -InputObject $candidate -Name 'RuntimeStatus' -Required
            $runtimeStage = [string] (Get-CountdownRemoteDeploymentProperty -InputObject $runtimeStatus -Name 'stage' -Required)
            $calendarState = [string] (Get-CountdownRemoteDeploymentProperty -InputObject $runtimeStatus -Name 'calendarState' -Required)
            $installedValidation = Get-CountdownRemoteDeploymentProperty -InputObject $candidate -Name 'InstalledValidation'
            if ($null -ne $installedValidation) {
                $installedValidationValid = Get-CountdownRemoteDeploymentProperty -InputObject $installedValidation -Name 'Valid' -Required
                if ($installedValidationValid -isnot [bool] -or -not [bool] $installedValidationValid) {
                    throw '安装后应用 validate-only 验证未通过。'
                }
            }

            $acceptance = & $Adapter.Accept $Adapter.State $context $candidate
            $committed = Get-CountdownRemoteDeploymentCommitted -Adapter $Adapter
            if (-not $committed) {
                throw '远端部署 adapter 在返回验收成功前未确认提交边界。'
            }
            if ($null -eq $acceptance -or
                -not [string]::Equals(
                    [string] (Get-CountdownRemoteDeploymentProperty -InputObject $acceptance -Name 'AcceptanceState' -Required),
                    'finalized-ready',
                    [System.StringComparison]::Ordinal)) {
                throw '远端部署验收没有达到 finalized-ready。'
            }
            $finalizeProperty = $Adapter.PSObject.Properties['Finalize']
            if ($null -ne $finalizeProperty) {
                [void] (& $finalizeProperty.Value $Adapter.State $context)
            }

            $result = [pscustomobject]@{
                SchemaVersion = 1
                Valid = $true
                CoreTests = $coreTestsPassed
                CSharpTests = $cSharpTestsPassed
                UiTests = $uiTestsPassed
                UiValid = $true
                AdapterUiValidated = $true
                TaskState = [string] (Get-CountdownRemoteDeploymentProperty -InputObject $acceptance -Name 'TaskState' -Required)
                RuntimeProcessId = [int] (Get-CountdownRemoteDeploymentProperty -InputObject $acceptance -Name 'RuntimeProcessId' -Required)
                RuntimeStage = $runtimeStage
                CalendarState = $calendarState
                AcceptanceState = [string] (Get-CountdownRemoteDeploymentProperty -InputObject $acceptance -Name 'AcceptanceState' -Required)
                SettingsHashBefore = [string] (Get-CountdownRemoteDeploymentProperty -InputObject $acceptance -Name 'SettingsHashBefore' -Required)
                SettingsHashAfter = [string] (Get-CountdownRemoteDeploymentProperty -InputObject $acceptance -Name 'SettingsHashAfter' -Required)
            }
        }
        catch {
            $failure = $_.Exception.Message
            try {
                $committed = Get-CountdownRemoteDeploymentCommitted -Adapter $Adapter
            }
            catch {
                throw "远端部署失败，adapter 提交状态无法验证，未回滚：$failure；提交状态错误：$($_.Exception.Message)"
            }
            if ($committed) {
                throw "部署已提交，但提交后收尾失败，未回滚：$failure"
            }

            try {
                $rollback = & $Adapter.Rollback $Adapter.State $context $candidate
            }
            catch {
                throw "远端部署失败：$failure；回滚也失败：$($_.Exception.Message)"
            }
            $unresolvedCommitted = $false
            $preservedUnmanaged = $false
            if ($null -ne $rollback) {
                $unresolvedProperty = $rollback.PSObject.Properties['UnresolvedCommittedTransaction']
                $preservedProperty = $rollback.PSObject.Properties['PreservedUnmanagedLaunchers']
                $unresolvedCommitted = $null -ne $unresolvedProperty -and [bool] $unresolvedProperty.Value
                $preservedUnmanaged = $null -ne $preservedProperty -and [bool] $preservedProperty.Value
            }
            if ($unresolvedCommitted) {
                throw "远端部署失败；检测到已提交但未完成解锁确认的事务，未声称回滚并保留了证据：$failure"
            }
            if ($preservedUnmanaged) {
                throw "远端部署失败，已恢复应用、任务和时间服务；为保护用户替换的桌面启动器，保留了事务证据：$failure"
            }
            throw "远端部署失败，已回滚：$failure"
        }
    }
    catch {
        $deploymentFailure = $_.Exception.Message
    }

    $disposeFailure = $null
    try {
        [void] (& $Adapter.Dispose $Adapter.State $context)
    }
    catch {
        $disposeFailure = $_.Exception.Message
    }

    if ($null -ne $deploymentFailure) {
        if ($null -ne $disposeFailure) {
            throw "$deploymentFailure；资源释放也失败：$disposeFailure"
        }
        throw $deploymentFailure
    }
    if ($null -ne $disposeFailure) {
        if ($committed) {
            throw "部署已提交，但资源释放失败，未回滚：$disposeFailure"
        }
        throw "远端部署资源释放失败：$disposeFailure"
    }
    $result
}

Export-ModuleMember -Function Invoke-CountdownRemoteDeployment
