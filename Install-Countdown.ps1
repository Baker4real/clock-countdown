#requires -Version 7.0

[CmdletBinding()]
param(
    [switch] $ValidateOnly,
    [switch] $DeferCommit,
    [string] $DeferredReceiptPath,
    [switch] $ParentTransactionMutexHeld,
    [string] $ExpectedInteractiveUser,
    [int] $ExpectedInteractiveSessionId,
    [string] $ExpectedDesktopPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$staging = $PSScriptRoot
$install = 'D:\敏敏子倒计时'
$taskName = 'Minmin Countdown'
$legacyTaskName = '敏敏子倒计时'
Import-Module (Join-Path $staging 'Countdown.ReleaseContract.psm1') -Force
$releaseContract = Get-CountdownReleaseContract
$expected = $releaseContract.IntegrityHashes
$applicationFiles = @($releaseContract.InstalledFiles)
$officialCharacterAssets = @($releaseContract.OfficialCharacterAssets)
$floatingPlushAssets = @($releaseContract.FloatingPlushAssets)
$currentFloatingRuntimeAssets = @($releaseContract.CurrentFloatingRuntimeAssets)
$legacyFloatingRegressionAssets = @($releaseContract.LegacyFloatingRegressionAssets)
$retiredRuntimeFiles = @($releaseContract.RetiredRuntimeFiles)
$packageValidation = Assert-CountdownReleasePackage -Root $staging

if ($ValidateOnly) {
    [pscustomobject]@{
        Valid = $true
        SourceRoot = $staging
        VerifiedFiles = $packageValidation.VerifiedFiles
        OfficialCharacterAssets = $officialCharacterAssets
        FloatingPlushAssets = $floatingPlushAssets
        CurrentFloatingRuntimeAssets = $currentFloatingRuntimeAssets
        LegacyFloatingRegressionAssets = $legacyFloatingRegressionAssets
        CompatibilityFixtureFiles = @($releaseContract.CompatibilityFixtureFiles)
        RetiredRuntimeFiles = $retiredRuntimeFiles
        Transactional = $true
        DeferredCommitSupported = $true
    } | ConvertTo-Json
    exit 0
}

if ($DeferCommit -and ((-not $ParentTransactionMutexHeld) -or [string]::IsNullOrWhiteSpace($DeferredReceiptPath))) {
    throw '延迟提交只能由已持有父安装事务锁的验收方调用，并且必须提供独立回执路径。'
}
if ($ParentTransactionMutexHeld -and -not $DeferCommit) {
    throw '父安装事务锁上下文只允许与 -DeferCommit 一起使用。'
}

$installResolved = [System.IO.Path]::GetFullPath($install)
if (-not $installResolved.StartsWith('D:\', [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Install path is outside D drive: $installResolved"
}
if ((Test-Path -LiteralPath $installResolved) -and -not (Test-Path -LiteralPath $installResolved -PathType Container)) {
    throw "Install path is not a directory: $installResolved"
}

$powerShellEngine = (Get-Command pwsh.exe -CommandType Application -ErrorAction Stop | Select-Object -First 1).Source
if ([string]::IsNullOrWhiteSpace($powerShellEngine)) {
    throw '未找到 pwsh.exe；本项目要求 PowerShell 7+，不支持 Windows PowerShell 5.1。'
}
$dotnetPath = 'D:\Codex\tools\dotnet\10.0\dotnet.exe'
if (-not (Test-Path -LiteralPath $dotnetPath -PathType Leaf)) {
    throw "未找到验收套件所需的受管 dotnet：$dotnetPath"
}
$powerShellProcessName = [System.IO.Path]::GetFileName($powerShellEngine)
Get-Service -Name W32Time -ErrorAction Stop | Out-Null

Import-Module (Join-Path $staging 'Countdown.InstallTransaction.psm1') -Force -DisableNameChecking
Import-Module (Join-Path $staging 'Countdown.Validation.psm1') -Force
$interactiveContext = Get-CountdownInteractiveDesktopContext
$contextExpectationProvided = $PSBoundParameters.ContainsKey('ExpectedInteractiveUser') -or $PSBoundParameters.ContainsKey('ExpectedInteractiveSessionId') -or $PSBoundParameters.ContainsKey('ExpectedDesktopPath')
if ($contextExpectationProvided) {
    if (-not ($PSBoundParameters.ContainsKey('ExpectedInteractiveUser') -and $PSBoundParameters.ContainsKey('ExpectedInteractiveSessionId') -and $PSBoundParameters.ContainsKey('ExpectedDesktopPath'))) {
        throw '办公室部署传入的交互上下文必须同时包含用户、会话和桌面路径。'
    }
    $expectedDesktop = Assert-CountdownInstallPathOnDDrive -Path $ExpectedDesktopPath
    if (-not [string]::Equals([string] $interactiveContext.User, $ExpectedInteractiveUser, [System.StringComparison]::OrdinalIgnoreCase) -or
        [int] $interactiveContext.SessionId -ne $ExpectedInteractiveSessionId -or
        -not [string]::Equals([string] $interactiveContext.Desktop, $expectedDesktop, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw '办公室部署开始安装前交互用户、会话或桌面路径已变化；未创建安装事务。'
    }
}
$interactiveUser = [string] $interactiveContext.User
$desktop = [string] $interactiveContext.Desktop
$legacyManagedLauncherHash = 'E5E37A9D20669E4A888969F0DB95560D45D581913B68EA7600055DE50E20AC14'
function Assert-CountdownInstallInteractiveContextStable {
    param([Parameter(Mandatory)] [object] $ExpectedContext)

    $actualContext = Get-CountdownInteractiveDesktopContext
    if (-not [string]::Equals([string] $actualContext.User, [string] $ExpectedContext.User, [System.StringComparison]::OrdinalIgnoreCase) -or
        [int] $actualContext.SessionId -ne [int] $ExpectedContext.SessionId -or
        -not [string]::Equals([string] $actualContext.Desktop, [string] $ExpectedContext.Desktop, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw '安装事务开始前交互用户、会话或桌面路径已变化；未创建安装事务。'
    }
    $actualContext
}

function Get-CountdownManagedDesktopLauncherOwnership {
    param(
        [Parameter(Mandatory)] [string] $LauncherPath,
        [Parameter(Mandatory)] [string] $LauncherStatePath,
        [Parameter(Mandatory)] [System.Collections.IDictionary] $Expected,
        [Parameter(Mandatory)] [string] $LegacyManagedLauncherHash
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
        $managed = $currentLauncherHash -eq $LegacyManagedLauncherHash
    }
    [pscustomobject]@{ Exists = $true; Sha256 = $currentLauncherHash; Managed = [bool] $managed }
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

$validationSuite = Invoke-CountdownValidationSuite `
    -Root $staging `
    -PowerShell7Path $powerShellEngine `
    -DotnetPath $dotnetPath `
    -ParentTransactionMutexHeld:$ParentTransactionMutexHeld
$testOutput = [string] $validationSuite.TestOutput
$cSharpTestOutput = [string] $validationSuite.CSharpTestOutput
$uiTestOutput = [string] $validationSuite.UiTestOutput
$uiValidationOutput = [string] $validationSuite.UiValidationOutput

$launcherPath = Join-Path $desktop '敏敏子倒计时.exe'
$launcherStatePath = Join-Path $install 'launcher-state.json'
$legacyLauncherPath = Join-Path $desktop '敏敏子倒计时.cmd'
$launcherTempPath = "$launcherPath.new"

Import-Module (Join-Path $staging 'Countdown.Runtime.psm1') -Force
Import-Module (Join-Path $staging 'Countdown.Settings.psm1') -Force
Import-Module (Join-Path $staging 'Countdown.TransactionAcceptance.psm1') -Force
$installMutex = $null
$installStage = 'prepare-transaction'
$transactionStarted = $false
try {
    # 即使父验收已持锁也再次获取同线程可重入 mutex；公开参数不能跳过真实安装锁。
    $installMutex = Enter-CountdownInstallTransactionMutex -TimeoutSeconds 120
    $interactiveContext = Assert-CountdownInstallInteractiveContextStable -ExpectedContext $interactiveContext
    # 先按 journal 恢复崩溃留下的候选启动器；否则候选 exe 与旧 launcher-state 的哈希不一致，会永久阻断恢复。
    $recoveryResult = Recover-CountdownInstallTransactions -InstallPath $install -PowerShell7Path $powerShellEngine -ExpectedTaskName $taskName -ExpectedLegacyTaskName $legacyTaskName -ExpectedLauncherPath $launcherPath -ExpectedLegacyLauncherPath $legacyLauncherPath -ExpectedLauncherTempPath $launcherTempPath
    $recoveredTransactions = @($recoveryResult.RecoveredTransactionPaths)
    if (@($recoveryResult.UnresolvedCommittedTransactions).Count -gt 0) {
        throw '检测到已提交但未完成应用解锁确认的事务；已保留回滚证据，拒绝继续安装。'
    }
    if (@($recoveryResult.UnresolvedLauncherOwnershipConflicts).Count -gt 0) {
        throw '检测到用户替换的桌面启动器：应用、任务和时间服务已恢复，但事务证据被保留，拒绝继续安装。'
    }
    $initialLauncherOwnership = Get-CountdownManagedDesktopLauncherOwnership -LauncherPath $launcherPath -LauncherStatePath $launcherStatePath -Expected $expected -LegacyManagedLauncherHash $legacyManagedLauncherHash
    if (-not [bool] $initialLauncherOwnership.Managed) {
        throw "Refusing to overwrite an unmanaged desktop launcher: $launcherPath"
    }
    $interactiveContext = Assert-CountdownInstallInteractiveContextStable -ExpectedContext $interactiveContext
    $transaction = Start-CountdownInstallTransaction -InstallPath $install -LauncherPath $launcherPath -LegacyLauncherPath $legacyLauncherPath -TaskName $taskName -LegacyTaskName $legacyTaskName
    $transactionStarted = $true
    $transactionCompleted = $false

    try {
        $installStage = 'configure-network-time'
        Set-Service -Name W32Time -StartupType Automatic
        Start-Service -Name W32Time
        $resync = (w32tm.exe /resync /rediscover | Out-String).Trim()
        if ($LASTEXITCODE -ne 0) {
            throw "Windows Time resync failed: $resync"
        }
        $timeSource = Wait-CountdownNetworkTimeSource -TimeoutSeconds 30 -PollMilliseconds 500
        $timeStatus = (w32tm.exe /query /status | Out-String).Trim()
        if ($LASTEXITCODE -ne 0) {
            throw 'Windows Time status query failed.'
        }

        $installStage = 'quiesce-existing-application'
        Stop-CountdownManagedApplication -InstallPath $install -TaskName $taskName -LegacyTaskName $legacyTaskName -FreezeFutureStarts
        # 旧进程已经静止后才捕获可变 settings/runtime 与桌面文件；任务原状态仍取自 prepared journal。
        $transaction = Capture-CountdownInstallPayloadSnapshot -TransactionPath $transaction.transactionPath -ExpectedInstallPath $install -ExpectedTaskName $taskName -ExpectedLegacyTaskName $legacyTaskName -ExpectedLauncherPath $launcherPath -ExpectedLegacyLauncherPath $legacyLauncherPath -ExpectedLauncherTempPath $launcherTempPath
        [void] (Remove-CountdownScheduledTaskIfPresent -TaskName $legacyTaskName)
        $install = Assert-CountdownInstallPathOnDDrive -Path $install
        [void] (Assert-CountdownInstallTreeHasNoReparsePoints -Path $install)
        if (-not (Test-Path -LiteralPath $install -PathType Container)) {
            New-Item -ItemType Directory -Path $install -Force | Out-Null
        }
        $runtimeStatusPath = Assert-CountdownInstallPathOnDDrive -Path (Join-Path $install 'runtime-status.json')
        $acceptanceStatusPath = Assert-CountdownInstallPathOnDDrive -Path (Join-Path $install 'acceptance-status.json')
        $startupRequestPath = Assert-CountdownInstallPathOnDDrive -Path (Join-Path $install 'startup-request.json')
        Remove-Item -LiteralPath $runtimeStatusPath -Force -ErrorAction SilentlyContinue
        Remove-Item -LiteralPath $acceptanceStatusPath -Force -ErrorAction SilentlyContinue
        $startupRequest = New-CountdownStartupRequest -Path $startupRequestPath -AcceptanceReadOnly

        $installStage = 'copy-payload'
        foreach ($file in $applicationFiles) {
            $source = Assert-CountdownInstallPathOnDDrive -Path (Join-Path $staging $file)
            $destination = Assert-CountdownInstallPathOnDDrive -Path (Join-Path $install $file)
            $destinationParent = Assert-CountdownInstallPathOnDDrive -Path (Split-Path -Parent $destination)
            New-Item -ItemType Directory -Path $destinationParent -Force | Out-Null
            [void] (Assert-CountdownInstallPathOnDDrive -Path $destination)
            Copy-Item -LiteralPath $source -Destination $destination -Force
        }
        foreach ($file in $retiredRuntimeFiles) {
            $retiredPath = Assert-CountdownInstallPathOnDDrive -Path (Join-Path $install $file)
            if (Test-Path -LiteralPath $retiredPath -PathType Leaf) {
                Remove-Item -LiteralPath $retiredPath -Force
            }
        }

        $appPath = Join-Path $install '敏敏子倒计时.ps1'
        $arguments = "-NoLogo -NoProfile -ExecutionPolicy RemoteSigned -WindowStyle Hidden -STA -File `"$appPath`""
        # 候选启动器放在受管事务目录，桌面上的 .new 仅作为用户已有文件的快照目标，绝不作为安装候选。
        $candidateLauncherPath = Assert-CountdownInstallPathOnDDrive -Path (Join-Path $transaction.transactionPath 'candidate-launcher.exe')
        $installedLauncherSourcePath = Assert-CountdownInstallPathOnDDrive -Path (Join-Path $install 'CountdownLauncher.exe')
        Copy-Item -LiteralPath $installedLauncherSourcePath -Destination $candidateLauncherPath -Force
        $candidateLauncherHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $candidateLauncherPath).Hash
        if ($candidateLauncherHash -ne $expected['CountdownLauncher.exe']) {
            Remove-Item -LiteralPath $candidateLauncherPath -Force
            throw 'Transaction launcher candidate hash mismatch.'
        }
        # Move 前持久化候选哈希；崩溃恢复只会覆盖候选文件、原备份或缺失路径，绝不静默覆盖用户后续替换的启动器。
        $transaction = Set-CountdownInstallTransactionCandidateLauncherHash -TransactionPath $transaction.transactionPath -CandidateLauncherSha256 $candidateLauncherHash -ExpectedInstallPath $install -ExpectedTaskName $taskName -ExpectedLegacyTaskName $legacyTaskName -ExpectedLauncherPath $launcherPath -ExpectedLegacyLauncherPath $legacyLauncherPath -ExpectedLauncherTempPath $launcherTempPath
        # 将所有慢操作放在此前完成；紧贴替换前比较初始哈希，准备期间的任意 launcher 替换都会安全回滚。
        $preparedLauncherOwnership = Get-CountdownManagedDesktopLauncherOwnership -LauncherPath $launcherPath -LauncherStatePath $launcherStatePath -Expected $expected -LegacyManagedLauncherHash $legacyManagedLauncherHash
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
            $legacyExpected = "@echo off`r`nC:\Windows\System32\schtasks.exe /Run /TN `"$taskName`" >nul 2>&1`r`nexit /b %errorlevel%`r`n"
            $legacyActual = [System.IO.File]::ReadAllText($legacyLauncherPath, [System.Text.Encoding]::ASCII)
            if ($legacyActual -eq $legacyExpected) {
                Remove-Item -LiteralPath $legacyLauncherPath -Force
            }
        }

        $installStage = 'register-task'
        $action = New-ScheduledTaskAction -Execute $powerShellEngine -Argument $arguments -WorkingDirectory $install
        $trigger = New-ScheduledTaskTrigger -AtLogOn -User $interactiveUser
        $principal = New-ScheduledTaskPrincipal -UserId $interactiveUser -LogonType Interactive -RunLevel Limited
        $settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries `
            -ExecutionTimeLimit ([TimeSpan]::Zero) -MultipleInstances IgnoreNew
        Register-ScheduledTask -TaskName $taskName -Action $action -Trigger $trigger -Principal $principal `
            -Settings $settings -Description '登录后启动敏敏子倒计时，按北京时间计算。' -Force | Out-Null

        $registeredTask = Get-ScheduledTask -TaskName $taskName
        if (-not [string]::Equals(
                [System.IO.Path]::GetFullPath([string] $registeredTask.Actions.Execute),
                [System.IO.Path]::GetFullPath($powerShellEngine),
                [System.StringComparison]::OrdinalIgnoreCase
            )) {
            throw "计划任务未使用 PowerShell 7：$($registeredTask.Actions.Execute)"
        }
        $expectedScheduledTaskIdentity = [string] (Export-ScheduledTask -InputObject $registeredTask -ErrorAction Stop)

        $installStage = 'wait-window-ready'
        Start-ScheduledTask -TaskName $taskName
        $runtimeStatus = Wait-CountdownWindowReady -Path $runtimeStatusPath -LaunchToken $startupRequest.launchToken -ExpectedSessionId ([int] $interactiveContext.SessionId) -ExpectedApplicationPath $appPath -TimeoutSeconds 30 -PollMilliseconds 250
        $inventoryEvidenceRoot = 'D:\agents\run-evidence\countdown\install-inventory'
        [void] [System.IO.Directory]::CreateDirectory($inventoryEvidenceRoot)
        $inventoryEvidencePath = Join-Path $inventoryEvidenceRoot ('installed-files-{0}.json' -f [guid]::NewGuid().ToString('N'))
        $installedInventory = @(Get-ChildItem -LiteralPath $install -File -Recurse | Select-Object FullName, Length, @{n='Hash';e={(Get-FileHash -Algorithm SHA256 -LiteralPath $_.FullName).Hash}})
        [System.IO.File]::WriteAllText($inventoryEvidencePath, ($installedInventory | ConvertTo-Json -Depth 4), [System.Text.UTF8Encoding]::new($false))
        $result = [ordered]@{
            TestOutput = $testOutput.Trim()
            CSharpTestOutput = $cSharpTestOutput.Trim()
            UiTestOutput = $uiTestOutput.Trim()
            UiValidationOutput = $uiValidationOutput.Trim()
            Validation = [ordered]@{
                SchemaVersion = [int] $validationSuite.SchemaVersion
                Valid = [bool] $validationSuite.Valid
                CoreTestsPassed = [int] $validationSuite.CoreTestsPassed
                CSharpTestsPassed = [int] $validationSuite.CSharpTestsPassed
                UiTestsPassed = [int] $validationSuite.UiTestsPassed
                UiValid = [bool] $validationSuite.UiValid
                AdapterUiValidated = [bool] $validationSuite.AdapterUiValidated
            }
            InstalledFileCount = [int] $installedInventory.Count
            InstalledFilesEvidencePath = $inventoryEvidencePath
            Launcher = Get-Item -LiteralPath $launcherPath | Select-Object FullName, Length
            Task = Get-ScheduledTask -TaskName $taskName | Select-Object TaskName, State, @{n='Execute';e={$_.Actions.Execute}}, @{n='Arguments';e={$_.Actions.Arguments}}, @{n='LogonType';e={$_.Principal.LogonType}}
            TaskInfo = Get-ScheduledTaskInfo -TaskName $taskName | Select-Object LastRunTime, LastTaskResult, NextRunTime
            RuntimeStatus = $runtimeStatus
            LaunchToken = $startupRequest.launchToken
            InteractiveSessionId = [int] $interactiveContext.SessionId
            TimeSource = $timeSource
            TimeStatus = $timeStatus
            RecoveredTransactions = $recoveredTransactions
            DeferredCommit = [bool] $DeferCommit
            TransactionPath = if ($DeferCommit) { $transaction.transactionPath } else { $null }
        }
        if ($DeferCommit) {
            $installStage = 'write-deferred-receipt'
            [void] (Write-CountdownDeferredInstallReceipt -Path $DeferredReceiptPath -TransactionPath $transaction.transactionPath -LaunchToken $startupRequest.launchToken -InteractiveSessionId ([int] $interactiveContext.SessionId))
            $result.DeferredReceiptPath = [System.IO.Path]::GetFullPath($DeferredReceiptPath)
            $result | ConvertTo-Json -Depth 8
            return
        }

        $settingsSnapshotPath = Join-Path $transaction.transactionPath 'install-backup\settings.json'
        $settingsPath = Join-Path $install 'settings.json'
        $getAcceptanceInteractiveContext = { Get-CountdownInteractiveDesktopContext }
        $getAcceptanceScheduledTask = {
            param([string] $ExpectedTaskName)
            Get-ScheduledTask -TaskName $ExpectedTaskName -ErrorAction Stop
        }
        $getAcceptanceScheduledTaskIdentity = {
            param([object] $ScheduledTask)
            [string] (Export-ScheduledTask -InputObject $ScheduledTask -ErrorAction Stop)
        }
        $getAcceptanceScheduledTaskInfo = {
            param([string] $ExpectedTaskName)
            Get-ScheduledTaskInfo -TaskName $ExpectedTaskName -ErrorAction Stop
        }
        $completeAcceptanceTransaction = {
            Complete-CountdownInstallTransaction -TransactionPath $transaction.transactionPath -ExpectedInstallPath $install -ExpectedTaskName $taskName -ExpectedLegacyTaskName $legacyTaskName -ExpectedLauncherPath $launcherPath -ExpectedLegacyLauncherPath $legacyLauncherPath -ExpectedLauncherTempPath $launcherTempPath
        }
        $removeAcceptedTransaction = {
            Remove-CountdownCommittedInstallTransaction -TransactionPath $transaction.transactionPath -ExpectedInstallPath $install -ExpectedTaskName $taskName -ExpectedLegacyTaskName $legacyTaskName -ExpectedLauncherPath $launcherPath -ExpectedLegacyLauncherPath $legacyLauncherPath -ExpectedLauncherTempPath $launcherTempPath
        }
        $installStage = 'transaction-acceptance'
        [void] (Invoke-CountdownTransactionAcceptance `
                -StartupRequestPath $startupRequestPath `
                -RuntimeStatusPath $runtimeStatusPath `
                -AcceptanceStatusPath $acceptanceStatusPath `
                -LaunchToken $startupRequest.launchToken `
                -ExpectedInteractiveContext $interactiveContext `
                -ExpectedProcessId ([int] $runtimeStatus.processId) `
                -ExpectedSessionId ([int] $interactiveContext.SessionId) `
                -ExpectedApplicationPath $appPath `
                -TaskName $taskName `
                -ExpectedScheduledTaskIdentity $expectedScheduledTaskIdentity `
                -SettingsSnapshotPath $settingsSnapshotPath `
                -SettingsPath $settingsPath `
                -GetInteractiveContext $getAcceptanceInteractiveContext `
                -GetScheduledTask $getAcceptanceScheduledTask `
                -GetScheduledTaskIdentity $getAcceptanceScheduledTaskIdentity `
                -GetScheduledTaskInfo $getAcceptanceScheduledTaskInfo `
                -CompleteTransaction $completeAcceptanceTransaction `
                -RemoveCommittedTransaction $removeAcceptedTransaction)
        $transactionCompleted = $true
        & "$env:SystemRoot\System32\ie4uinit.exe" -show 2>$null
        $result | ConvertTo-Json -Depth 8
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
        $rollbackResult = $null
        $rollbackFailure = $null
        $rollbackAttempted = $false
        $rollbackSucceeded = $false
        $preservedUnmanagedLaunchers = $false
        if (-not $transactionCompleted) {
            $rollbackAttempted = $true
            try {
                # 任意失败都优先恢复应用、任务与时间服务；若用户在事务窗口替换桌面文件，只保留该文件和可审计 journal。
                $rollbackResult = Undo-CountdownInstallTransaction -TransactionPath $transaction.transactionPath -PowerShell7Path $powerShellEngine -ExpectedInstallPath $install -ExpectedTaskName $taskName -ExpectedLegacyTaskName $legacyTaskName -ExpectedLauncherPath $launcherPath -ExpectedLegacyLauncherPath $legacyLauncherPath -ExpectedLauncherTempPath $launcherTempPath -PreserveUnmanagedLaunchers
                $rollbackSucceeded = $true
            }
            catch {
                $rollbackFailure = $_
            }
            if ($null -ne $rollbackResult -and [bool] $rollbackResult.PreservedUnmanagedLaunchers) {
                $preservedUnmanagedLaunchers = $true
            }
        }
        $failureReceiptWriteFailure = $null
        if ($DeferCommit -and -not [string]::IsNullOrWhiteSpace($DeferredReceiptPath)) {
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
