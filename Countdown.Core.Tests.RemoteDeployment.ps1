# Countdown.Core.Tests.ps1 的 RemoteDeployment 分段：入口按开关点源执行，共用入口的参数、断言函数与已建立的状态，不单独运行。

$remoteDeploymentEvidenceRoot = 'D:\agents\run-evidence\migration-preflight-20260825T013156Z\clock-countdown-remote-deployment-tests'
$remoteDeploymentTestRoot = Join-Path $remoteDeploymentEvidenceRoot ([guid]::NewGuid().ToString('N'))
$remoteDeploymentSource = Join-Path $remoteDeploymentTestRoot 'source'
$remoteDeploymentInstall = Join-Path $remoteDeploymentTestRoot 'install'
$remoteDeploymentReceipt = Join-Path $remoteDeploymentTestRoot 'deferred-install-receipt.json'
$remoteDeploymentPowerShell = (Get-Command pwsh.exe -CommandType Application -ErrorAction Stop |
    Select-Object -First 1 -ExpandProperty Source)
# 部署运行的受锁加载会以 -Force 从同一路径重载 InstallTransaction，并移除测试全局导入的实例；
# 本段夹具与回读经段首解析到的布局命令取安装布局，不受之前部署运行影响。
$installLayoutCommand = Get-Command -Name Get-CountdownInstallLayout -CommandType Function
function New-CountdownRemoteDeploymentFixtureAdapter {
    param([ValidateSet('Success', 'BeginFailure', 'CandidateFailure', 'AcceptanceFailure', 'CommitThenFail', 'RollbackFailure', 'FinalizeFailure', 'DisposeFailure', 'CandidateAndDisposeFailure', 'CommitProbeFailure', 'UnresolvedCommittedTransaction')] [string] $Mode)

    $state = [pscustomobject]@{
        Mode = $Mode
        Begun = $false
        Accepted = $false
        Committed = $false
        RolledBack = $false
        RollbackCount = 0
        DiagnosticsPresentBeforeRollback = $false
        Disposed = $false
        InteractiveContext = [pscustomobject]@{ SessionId = 7 }
        Layout = & $installLayoutCommand -InstallPath $remoteDeploymentInstall -TaskName 'Minmin Countdown Fixture' -LegacyTaskName '敏敏子倒计时 Fixture'
        LaunchToken = 'sensitive-launch-token-sentinel'
        ExpectedProcessId = 0
        ExpectedScheduledTaskIdentity = '<Task><Actions><Exec><Arguments>sensitive-task-argument-sentinel</Arguments></Exec></Actions></Task>'
    }
    $adapter = [pscustomobject]@{
        State = $state
        Begin = {
            param($AdapterState, $Context)
            $AdapterState.Begun = $true
            if ($AdapterState.Mode -eq 'BeginFailure') {
                throw [System.IO.IOException]::new('模拟候选安装异常。')
            }
            if ($AdapterState.Mode -eq 'CommitProbeFailure') {
                $AdapterState.PSObject.Properties.Remove('Committed')
            }
            [pscustomobject]@{
                RuntimeStatus = [pscustomobject]@{
                    processId = 4242
                    # 候选未达到 window-ready 是提交边界前的候选契约失败，触发回滚路径。
                    stage = if ($AdapterState.Mode -in @('CandidateFailure', 'RollbackFailure', 'CandidateAndDisposeFailure', 'CommitProbeFailure', 'UnresolvedCommittedTransaction')) { 'starting' } else { 'window-ready' }
                    calendarState = 'Ready'
                }
            }
        }
        Accept = {
            param($AdapterState, $Context, $Candidate)
            if ($AdapterState.Mode -eq 'AcceptanceFailure') {
                throw '模拟验收失败。'
            }
            $AdapterState.Accepted = $true
            $AdapterState.Committed = $true
            if ($AdapterState.Mode -eq 'CommitThenFail') {
                throw '模拟提交后验收失败。'
            }
            [pscustomobject]@{
                AcceptanceState = 'finalized-ready'
                RuntimeProcessId = 4242
                TaskState = 'Ready'
                LastTaskResult = 0
                SettingsHashBefore = ('A' * 64)
                SettingsHashAfter = ('B' * 64)
                SettingsLengthBefore = 1
                SettingsLengthAfter = 2
                SettingsMtimeUtcBefore = '2026-01-01T00:00:00.0000000Z'
                SettingsMtimeUtcAfter = '2026-01-02T00:00:00.0000000Z'
                SettingsUnchanged = $false
            }
        }
        Rollback = {
            param($AdapterState, $Context, $Candidate)
            $AdapterState.RolledBack = $true
            $AdapterState.RollbackCount++
            $AdapterState.DiagnosticsPresentBeforeRollback = Test-Path -LiteralPath (Join-Path (Split-Path -Parent $Context.DeferredReceiptPath) 'pre-rollback-diagnostics.json') -PathType Leaf
            if ($AdapterState.Mode -eq 'RollbackFailure') {
                throw '模拟回滚失败。'
            }
            [pscustomobject]@{
                PreservedUnmanagedLaunchers = $false
                UnresolvedCommittedTransaction = $AdapterState.Mode -eq 'UnresolvedCommittedTransaction'
            }
        }
        Finalize = {
            param($AdapterState, $Context)
            if ($AdapterState.Mode -eq 'FinalizeFailure') {
                throw '模拟提交后收尾失败。'
            }
        }
        Dispose = {
            param($AdapterState, $Context)
            $AdapterState.Disposed = $true
            if ($AdapterState.Mode -in @('DisposeFailure', 'CandidateAndDisposeFailure')) {
                throw '模拟资源释放失败。'
            }
        }
    }
    [pscustomobject]@{ State = $state; Adapter = $adapter }
}

function New-CountdownRemoteDeploymentWindowsFixtureAdapter {
    param(
        [Parameter(Mandatory)]
        [ValidateSet(
            'AcceptedFinalizationFailure',
            'AcceptedReceiptFailure',
            'AcceptedEnvironmentFailure',
            'CommitPreparationFailure',
            'CommittedCleanupFailure',
            'CommittedFinalizeFailure')]
        [string] $Mode
    )

    $remoteDeploymentModule = Get-Module -Name Countdown.RemoteDeployment | Select-Object -First 1
    $adapter = & $remoteDeploymentModule { New-CountdownRemoteDeploymentWindowsAdapter }
    $transactionPath = Join-Path $remoteDeploymentTestRoot "$Mode-transaction"
    New-Item -ItemType Directory -Path $transactionPath -Force | Out-Null
    $adapter.State | Add-Member -NotePropertyName TestMode -NotePropertyValue $Mode
    $adapter.State.RollbackTransactionPath = $transactionPath
    $adapter.State.InteractiveContext = [pscustomobject]@{
        User = 'MINMIN\RemoteDeploymentFixture'
        SessionId = 7
        Desktop = (Join-Path $remoteDeploymentTestRoot 'desktop')
    }
    # fake 生命周期 module 按 Layout.TaskName 选择 accepted 之后的失败模式。
    $adapter.State.Layout = & $installLayoutCommand `
        -InstallPath $remoteDeploymentInstall `
        -TaskName $Mode `
        -LegacyTaskName '敏敏子倒计时 Fixture' `
        -Desktop (Join-Path $remoteDeploymentTestRoot 'desktop')
    $adapter.State.LaunchToken = 'remote-deployment-fixture-token'
    $adapter.State.ExpectedProcessId = 4242
    $adapter.State.ExpectedScheduledTaskIdentity = '<Task />'
    $adapter.State | Add-Member -NotePropertyName RollbackCount -NotePropertyValue 0
    $adapter.State | Add-Member -NotePropertyName Disposed -NotePropertyValue $false

    $adapter.Begin = {
        param($AdapterState, $Context)
        [pscustomobject]@{
            RuntimeStatus = [pscustomobject]@{
                processId = 4242
                stage = 'window-ready'
                calendarState = 'Ready'
            }
        }
    }
    $adapter.Rollback = {
        param($AdapterState, $Context, $Candidate)
        $AdapterState.RollbackCount++
        [pscustomobject]@{
            PreservedUnmanagedLaunchers = $false
            UnresolvedCommittedTransaction = $false
        }
    }
    $adapter.Finalize = {
        param($AdapterState, $Context)
        if ($AdapterState.TestMode -eq 'CommittedFinalizeFailure') {
            throw '模拟 committed 后 adapter 收尾失败。'
        }
    }
    $adapter.Dispose = {
        param($AdapterState, $Context)
        $AdapterState.Disposed = $true
    }
    [pscustomobject]@{
        Adapter = $adapter
        State = $adapter.State
        TransactionPath = $transactionPath
    }
}

function New-CountdownIssue4DefaultAdapterPackage {
    param(
        [Parameter(Mandatory)] [string] $Name,
        [switch] $IncludeExtraPackageFile,
        [switch] $OmitExtraManifestEntry,
        [switch] $IncludeUnexpectedManifestEntry,
        [switch] $ContractHashConflict,
        [switch] $DriftReleaseContractAfterManifest
    )

    $root = Join-Path $remoteDeploymentTestRoot "issue4-default-$Name"
    $installPath = Join-Path $root 'install'
    New-Item -ItemType Directory -Path $root -Force | Out-Null
    New-Item -ItemType Directory -Path $installPath -Force | Out-Null
    $packageFiles = [System.Collections.Generic.List[string]]::new()
    foreach ($relativePath in @(
            'Countdown.ReleaseContract.psm1',
            'Countdown.InstallTransaction.psm1',
            'Countdown.Runtime.psm1',
            'Countdown.Settings.psm1',
            'Countdown.InstallLifecycle.psm1',
            'Install-Countdown.ps1',
            '敏敏子倒计时.ps1')) {
        $packageFiles.Add($relativePath)
    }
    if ($IncludeExtraPackageFile) {
        $packageFiles.Add('data\issue4-extra.txt')
    }

    foreach ($relativePath in $packageFiles) {
        if ($relativePath -eq 'Countdown.ReleaseContract.psm1') { continue }
        $path = Join-Path $root $relativePath
        New-Item -ItemType Directory -Path (Split-Path -Parent $path) -Force | Out-Null
        $content = switch ($relativePath) {
            'Countdown.InstallTransaction.psm1' {
                "`$global:CountdownIssue4InstallTransactionSentinel = 'executed'`r`n"
            }
            'Install-Countdown.ps1' {
                "`$global:CountdownIssue4InstallerSentinel = 'executed'`r`n"
            }
            default { "Set-StrictMode -Version Latest`r`n" }
        }
        [System.IO.File]::WriteAllText($path, $content, [System.Text.UTF8Encoding]::new($false))
    }

    $contractIntegrityHash = if ($ContractHashConflict) { '0' * 64 } else {
        (Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $root 'Countdown.InstallTransaction.psm1')).Hash
    }
    $packageFilesLiteral = @(
        $packageFiles | ForEach-Object { "            '$($_.Replace("'", "''"))'" }
    ) -join ",`r`n"
    $releaseContractSource = @"
Set-StrictMode -Version Latest

function Get-CountdownReleaseContract {
    [pscustomobject]@{
        PackageFiles = [string[]] @(
$packageFilesLiteral
        )
        IntegrityHashes = [ordered]@{
            'Countdown.InstallTransaction.psm1' = '$contractIntegrityHash'
        }
        InstalledFiles = [string[]] @()
        RetiredRuntimeFiles = [string[]] @()
    }
}

function Assert-CountdownReleasePackage {
    param([string] `$Root)
    [pscustomobject]@{ Valid = `$true }
}

function Get-CountdownInteractiveDesktopContext {
    `$global:CountdownIssue4WindowsActionSentinel = 'executed'
    throw 'Issue #4 fixture crossed into Windows actions.'
}

Export-ModuleMember -Function Get-CountdownReleaseContract, Assert-CountdownReleasePackage, Get-CountdownInteractiveDesktopContext
"@
    $releaseContractPath = Join-Path $root 'Countdown.ReleaseContract.psm1'
    [System.IO.File]::WriteAllText(
        $releaseContractPath,
        $releaseContractSource,
        [System.Text.UTF8Encoding]::new($false))

    $manifest = [ordered]@{}
    foreach ($relativePath in $packageFiles) {
        if ($OmitExtraManifestEntry -and $relativePath -eq 'data\issue4-extra.txt') { continue }
        $manifest[$relativePath] = (Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $root $relativePath)).Hash
    }
    if ($IncludeUnexpectedManifestEntry) {
        $unexpectedPath = Join-Path $root 'issue4-unexpected.txt'
        [System.IO.File]::WriteAllText(
            $unexpectedPath,
            'unexpected',
            [System.Text.UTF8Encoding]::new($false))
        $manifest['issue4-unexpected.txt'] = (Get-FileHash -Algorithm SHA256 -LiteralPath $unexpectedPath).Hash
    }
    if ($DriftReleaseContractAfterManifest) {
        [System.IO.File]::WriteAllText(
            $releaseContractPath,
            "`$global:CountdownIssue4MaliciousReleaseSentinel = 'executed'`r`n$releaseContractSource",
            [System.Text.UTF8Encoding]::new($false))
    }

    $adapter = & $remoteDeploymentModule { New-CountdownRemoteDeploymentWindowsAdapter }
    $context = [pscustomobject]@{
        SourceRoot = $root
        InstallPath = $installPath
        PowerShell7Path = $remoteDeploymentPowerShell
        DeferredReceiptPath = Join-Path $root 'deferred-install-receipt.json'
        TaskName = "Issue4 $Name"
        LegacyTaskName = "Issue4 Legacy $Name"
        DeploymentBindingPath = ''
        DeploymentBindingSha256 = ''
        ExpectedFileSha256 = $manifest
        TrustedFileManifestRequired = $true
        AdapterState = $adapter.State
    }
    [pscustomobject]@{
        Adapter = $adapter
        Context = $context
    }
}

function Invoke-CountdownIssue4DefaultAdapterRejection {
    param([Parameter(Mandatory)] [object] $Fixture)

    foreach ($name in @(
            'CountdownIssue4InstallTransactionSentinel',
            'CountdownIssue4InstallerSentinel',
            'CountdownIssue4WindowsActionSentinel',
            'CountdownIssue4MaliciousReleaseSentinel')) {
        Remove-Variable -Name $name -Scope Global -ErrorAction SilentlyContinue
    }
    $message = ''
    try {
        [void] (& $Fixture.Adapter.Begin $Fixture.Adapter.State $Fixture.Context)
    }
    catch {
        $message = $_.Exception.Message
    }
    finally {
        [void] (& $Fixture.Adapter.Dispose $Fixture.Adapter.State $Fixture.Context)
    }
    $result = [pscustomobject]@{
        Message = $message
        TransactionModuleImported = [bool] $Fixture.Adapter.State.TransactionModuleImported
        InstallStarted = [bool] $Fixture.Adapter.State.InstallStarted
        InstallTransactionSentinel = $null -ne (Get-Variable -Name CountdownIssue4InstallTransactionSentinel -Scope Global -ErrorAction SilentlyContinue)
        InstallerSentinel = $null -ne (Get-Variable -Name CountdownIssue4InstallerSentinel -Scope Global -ErrorAction SilentlyContinue)
        WindowsActionSentinel = $null -ne (Get-Variable -Name CountdownIssue4WindowsActionSentinel -Scope Global -ErrorAction SilentlyContinue)
        MaliciousReleaseSentinel = $null -ne (Get-Variable -Name CountdownIssue4MaliciousReleaseSentinel -Scope Global -ErrorAction SilentlyContinue)
    }
    foreach ($name in @(
            'CountdownIssue4InstallTransactionSentinel',
            'CountdownIssue4InstallerSentinel',
            'CountdownIssue4WindowsActionSentinel',
            'CountdownIssue4MaliciousReleaseSentinel')) {
        Remove-Variable -Name $name -Scope Global -ErrorAction SilentlyContinue
    }
    Import-Module (Join-Path $PSScriptRoot 'Countdown.InstallTransaction.psm1') -Force -DisableNameChecking
    Import-Module (Join-Path $PSScriptRoot 'Countdown.ReleaseContract.psm1') -Force
    $result
}

function Invoke-CountdownIssue4AtomicReplacementProbe {
    param(
        [Parameter(Mandatory)] [string] $DestinationPath,
        [Parameter(Mandatory)] [string] $ReplacementPath,
        [Parameter(Mandatory)] [string] $BackupPath,
        [switch] $Restore
    )

    try {
        [System.IO.File]::Replace($ReplacementPath, $DestinationPath, $BackupPath, $true)
        if ($Restore) {
            [System.IO.File]::Replace($BackupPath, $DestinationPath, $ReplacementPath, $true)
        }
        $true
    }
    catch {
        $false
    }
}

try {
    New-Item -ItemType Directory -Path $remoteDeploymentSource -Force | Out-Null
    New-Item -ItemType Directory -Path $remoteDeploymentInstall -Force | Out-Null
    [System.IO.File]::WriteAllText(
        (Join-Path $remoteDeploymentSource 'Countdown.InstallTransaction.psm1'),
        "Set-StrictMode -Version Latest`r`n",
        [System.Text.UTF8Encoding]::new($false))
    [System.IO.File]::WriteAllText(
        (Join-Path $remoteDeploymentSource 'Countdown.Runtime.psm1'),
        "Set-StrictMode -Version Latest`r`n",
        [System.Text.UTF8Encoding]::new($false))
    [System.IO.File]::WriteAllText(
        (Join-Path $remoteDeploymentInstall 'Countdown.Settings.psm1'),
        "Set-StrictMode -Version Latest`r`n",
        [System.Text.UTF8Encoding]::new($false))
    # fake 生命周期按真实 module 的回调契约推进：accepted 之前只报 Stage，journal 写入 committed 后才回调 OnCommitted。
    [System.IO.File]::WriteAllText(
        (Join-Path $remoteDeploymentSource 'Countdown.InstallLifecycle.psm1'),
        @'
Set-StrictMode -Version Latest

function Complete-CountdownInstallation {
    param(
        [object] $Layout,
        [string] $TransactionPath,
        [object] $InteractiveContext,
        [string] $LaunchToken,
        [int] $ExpectedProcessId,
        [string] $ExpectedScheduledTaskIdentity,
        [scriptblock] $OnStage,
        [scriptblock] $OnCommitted
    )

    & $OnStage '提交安装事务'
    [System.IO.File]::WriteAllText(
        (Join-Path $TransactionPath 'accepted.marker'),
        'accepted',
        [System.Text.UTF8Encoding]::new($false))
    switch ($Layout.TaskName) {
        'AcceptedFinalizationFailure' { throw '模拟 accepted 后 finalized 写入失败。' }
        'AcceptedReceiptFailure' { throw '模拟 accepted 后 finalized-ready 回执失败。' }
        'AcceptedEnvironmentFailure' { throw '模拟 accepted 后环境复验失败。' }
    }

    & $OnStage '清理已提交事务'
    if ($TransactionPath -match 'CommitPreparationFailure') {
        throw '模拟 committed 点前的清理准备失败。'
    }
    & $OnCommitted
    Move-Item -LiteralPath $TransactionPath -Destination "$TransactionPath.committed-fixture" -ErrorAction Stop
    if ($TransactionPath -match 'CommittedCleanupFailure') {
        throw '模拟 committed 点后的事务清理失败。'
    }
    [pscustomobject]@{
        AcceptanceState = 'finalized-ready'
        RuntimeProcessId = 4242
        TaskState = 'Ready'
        LastTaskResult = 0
        SettingsHashBefore = ('A' * 64)
        SettingsHashAfter = ('B' * 64)
        SettingsLengthBefore = 1
        SettingsLengthAfter = 2
        SettingsMtimeUtcBefore = '2026-01-01T00:00:00.0000000Z'
        SettingsMtimeUtcAfter = '2026-01-02T00:00:00.0000000Z'
        SettingsUnchanged = $false
    }
}

Export-ModuleMember -Function Complete-CountdownInstallation
'@,
        [System.Text.UTF8Encoding]::new($false))
    [System.IO.File]::WriteAllText(
        (Join-Path $remoteDeploymentInstall 'settings.json'),
        '{"sensitive-settings-sentinel":true}',
        [System.Text.UTF8Encoding]::new($false))

    $installedApplicationFixture = Join-Path $remoteDeploymentInstall '敏敏子倒计时.ps1'
    if (-not (Test-Path -LiteralPath $installedApplicationFixture -PathType Leaf)) {
        [System.IO.File]::WriteAllText(
            $installedApplicationFixture,
            "param()`r`n",
            [System.Text.UTF8Encoding]::new($false))
    }

    # 办公室端只安装、不跑测试：验收在发起端本机完成，远端 adapter 不得调用任何验证入口、
    # 应用 validate-only 或隔离 opt-out 标记。
    $remoteDeploymentAdapterSource = [System.IO.File]::ReadAllText(
        (Join-Path $PSScriptRoot 'Countdown.RemoteDeployment.psm1'),
        [System.Text.Encoding]::UTF8)
    $remoteTestEntryPoints = @(
        @('Invoke-CountdownValidationSuite', 'Invoke-CountdownCandidatePreflight', '-ValidateOnly', 'MINMIN_SUITE_ALLOW_UNISOLATED') |
            Where-Object { $remoteDeploymentAdapterSource.Contains($_) })
    Assert-Equal '' ($remoteTestEntryPoints -join ',') 'Remote deployment adapter runs no tests or validate-only on the office device'

    $trustedLoaderRoot = Join-Path $remoteDeploymentTestRoot 'trusted-source-loader'
    $trustedLoaderModulePath = Join-Path $trustedLoaderRoot 'Issue4.SourceLoader.Fixture.psm1'
    $trustedLoaderMaliciousPath = Join-Path $remoteDeploymentTestRoot 'Issue4.SourceLoader.Malicious.psm1'
    $trustedLoaderBackupPath = Join-Path $remoteDeploymentTestRoot 'Issue4.SourceLoader.Backup.psm1'
    $trustedLoaderSource = @'
function Get-CountdownIssue4SourceLoaderValue { 'benign' }
Export-ModuleMember -Function Get-CountdownIssue4SourceLoaderValue
'@
    $maliciousLoaderSource = @'
Set-Variable -Scope Global -Name CountdownIssue4RemoteLoaderSentinel -Value 'executed'
function Get-CountdownIssue4SourceLoaderValue { 'malicious' }
Export-ModuleMember -Function Get-CountdownIssue4SourceLoaderValue
'@
    New-Item -ItemType Directory -Path $trustedLoaderRoot -Force | Out-Null
    [System.IO.File]::WriteAllText(
        $trustedLoaderModulePath,
        $trustedLoaderSource,
        [System.Text.UTF8Encoding]::new($false))
    [System.IO.File]::WriteAllText(
        $trustedLoaderMaliciousPath,
        $maliciousLoaderSource,
        [System.Text.UTF8Encoding]::new($false))
    $trustedLoaderHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $trustedLoaderModulePath).Hash
    $trustedLoaderManifest = [ordered]@{
        'Issue4.SourceLoader.Fixture.psm1' = $trustedLoaderHash
    }
    $trustedLoaderState = [pscustomobject]@{
        SourceFileLocks = $null
        InstalledFileLocks = $null
    }
    $trustedLoaderContext = [pscustomobject]@{
        SourceRoot = $trustedLoaderRoot
        InstallPath = $trustedLoaderRoot
        ExpectedFileSha256 = $trustedLoaderManifest
        TrustedFileManifestRequired = $true
        AdapterState = $trustedLoaderState
    }
    $remoteDeploymentModule = Get-Module -Name Countdown.RemoteDeployment | Select-Object -First 1
    $trustedLoaderState.SourceFileLocks = & $remoteDeploymentModule {
        param($root, $manifest)
        Enter-CountdownRemoteDeploymentFileLocks `
            -Root $root `
            -ExpectedFileSha256 $manifest `
            -RelativePaths @('Issue4.SourceLoader.Fixture.psm1') `
            -Stage '远端 source-loader 夹具'
    } $trustedLoaderRoot $trustedLoaderManifest
    $trustedLoaderSwapState = [pscustomobject]@{
        Rejected = $false
        Succeeded = $false
        Restored = $false
        ImportedValue = ''
    }
    Remove-Variable -Name CountdownIssue4RemoteLoaderSentinel -Scope Global -ErrorAction SilentlyContinue
    $trustedLoaderAction = {
        param($lockedPath)
        try {
            [System.IO.File]::Replace(
                $trustedLoaderMaliciousPath,
                $lockedPath,
                $trustedLoaderBackupPath,
                $true)
            $trustedLoaderSwapState.Succeeded = $true
            try {
                $fixtureModule = Import-Module $lockedPath -Force -PassThru
                $trustedLoaderSwapState.ImportedValue = & $fixtureModule { Get-CountdownIssue4SourceLoaderValue }
            }
            finally {
                [System.IO.File]::Replace(
                    $trustedLoaderBackupPath,
                    $lockedPath,
                    $trustedLoaderMaliciousPath,
                    $true)
                $trustedLoaderSwapState.Restored = $true
            }
        }
        catch {
            if ($trustedLoaderSwapState.Succeeded) { throw }
            $trustedLoaderSwapState.Rejected = $true
            $fixtureModule = Import-Module $lockedPath -Force -PassThru
            $trustedLoaderSwapState.ImportedValue = & $fixtureModule { Get-CountdownIssue4SourceLoaderValue }
        }
    }.GetNewClosure()
    try {
        & $remoteDeploymentModule {
            param($context, $action)
            Invoke-CountdownRemoteDeploymentTrustedFile `
                -Context $context `
                -Location Source `
                -RelativePath 'Issue4.SourceLoader.Fixture.psm1' `
                -Stage '远端 source-loader 导入窗口夹具' `
                -Action $action
        } $trustedLoaderContext $trustedLoaderAction
    }
    finally {
        & $remoteDeploymentModule {
            param($locks)
            Exit-CountdownRemoteDeploymentFileLocks -Locks $locks
        } $trustedLoaderState.SourceFileLocks
        $trustedLoaderState.SourceFileLocks = $null
    }
    Assert-Equal $true $trustedLoaderSwapState.Rejected 'Default remote source-loader rejects an atomic malicious replacement'
    Assert-Equal $false $trustedLoaderSwapState.Succeeded 'Malicious remote module cannot enter the verified loader window'
    Assert-Equal $false $trustedLoaderSwapState.Restored 'Rejected remote replacement never opens a restore window'
    Assert-Equal 'benign' $trustedLoaderSwapState.ImportedValue 'Default remote source-loader imports the same benign bytes that passed SHA-256 verification'
    Assert-Equal $false ($null -ne (Get-Variable -Name CountdownIssue4RemoteLoaderSentinel -Scope Global -ErrorAction SilentlyContinue)) 'Default remote source-loader never executes the malicious sentinel'
    Remove-Module Issue4.SourceLoader.Fixture -Force -ErrorAction SilentlyContinue

    $missingDefaultManifestRejected = $false
    try {
        Invoke-CountdownRemoteDeployment `
            -SourceRoot $remoteDeploymentSource `
            -InstallPath $remoteDeploymentInstall `
            -PowerShell7Path $remoteDeploymentPowerShell `
            -DeferredReceiptPath $remoteDeploymentReceipt `
            -TaskName 'Minmin Countdown Manifest Fixture' `
            -LegacyTaskName '敏敏子倒计时 Manifest Fixture' | Out-Null
    }
    catch {
        $missingDefaultManifestRejected = $_.Exception.Message -match '默认部署 adapter 缺少受信文件 identity'
    }
    Assert-Equal $true $missingDefaultManifestRejected 'Default remote adapter fails closed before Windows actions when the trusted file manifest is absent'

    $driftedHashResult = Invoke-CountdownIssue4DefaultAdapterRejection -Fixture (
        New-CountdownIssue4DefaultAdapterPackage `
            -Name 'hash-drift' `
            -DriftReleaseContractAfterManifest)
    $missingPackageMemberResult = Invoke-CountdownIssue4DefaultAdapterRejection -Fixture (
        New-CountdownIssue4DefaultAdapterPackage `
            -Name 'missing-package-member' `
            -IncludeExtraPackageFile `
            -OmitExtraManifestEntry)
    $extraManifestMemberResult = Invoke-CountdownIssue4DefaultAdapterRejection -Fixture (
        New-CountdownIssue4DefaultAdapterPackage `
            -Name 'extra-manifest-member' `
            -IncludeUnexpectedManifestEntry)
    $contractHashConflictResult = Invoke-CountdownIssue4DefaultAdapterRejection -Fixture (
        New-CountdownIssue4DefaultAdapterPackage `
            -Name 'contract-hash-conflict' `
            -ContractHashConflict)

    foreach ($case in @(
            [pscustomobject]@{
                Name = 'Hash drift before source locking'
                Result = $driftedHashResult
                Pattern = '锁定远端部署源文件：受信文件 SHA-256 不匹配：Countdown\.ReleaseContract\.psm1'
            },
            [pscustomobject]@{
                Name = 'Missing non-entry package member'
                Result = $missingPackageMemberResult
                Pattern = '远端部署预期文件 manifest 与发布契约文件数量不一致'
            },
            [pscustomobject]@{
                Name = 'Extra manifest member and count drift'
                Result = $extraManifestMemberResult
                Pattern = '远端部署预期文件 manifest 与发布契约文件数量不一致'
            },
            [pscustomobject]@{
                Name = 'Manifest and release contract hash conflict'
                Result = $contractHashConflictResult
                Pattern = '远端部署预期文件 manifest 与发布契约哈希不一致：Countdown\.InstallTransaction\.psm1'
            })) {
        Assert-Equal $true ($case.Result.Message -match $case.Pattern) "$($case.Name) fails closed with the expected reason"
        Assert-Equal $false ([bool] $case.Result.TransactionModuleImported) "$($case.Name) fails before the install transaction module is imported"
        Assert-Equal $false ([bool] $case.Result.InstallStarted) "$($case.Name) fails before the installer starts"
        Assert-Equal $false ([bool] $case.Result.InstallTransactionSentinel) "$($case.Name) never executes the install transaction sentinel"
        Assert-Equal $false ([bool] $case.Result.InstallerSentinel) "$($case.Name) never executes the installer sentinel"
        Assert-Equal $false ([bool] $case.Result.WindowsActionSentinel) "$($case.Name) fails before Windows actions"
        Assert-Equal $false ([bool] $case.Result.MaliciousReleaseSentinel) "$($case.Name) never executes drifted release-contract bytes"
    }

    $acceptLockRoot = Join-Path $remoteDeploymentTestRoot 'issue4-accept-locks'
    $acceptSourceRoot = Join-Path $acceptLockRoot 'source'
    $acceptInstallRoot = Join-Path $acceptLockRoot 'install'
    New-Item -ItemType Directory -Path $acceptSourceRoot -Force | Out-Null
    New-Item -ItemType Directory -Path $acceptInstallRoot -Force | Out-Null
    foreach ($file in @(
            'Countdown.InstallTransaction.psm1',
            'Countdown.Runtime.psm1')) {
        Copy-Item `
            -LiteralPath (Join-Path $remoteDeploymentSource $file) `
            -Destination (Join-Path $acceptSourceRoot $file) `
            -Force
    }
    [System.IO.File]::WriteAllText(
        (Join-Path $acceptSourceRoot 'Countdown.InstallLifecycle.psm1'),
        @'
function Test-CountdownIssue4AcceptReplacementRejected {
    param(
        [string] $DestinationPath,
        [string] $ReplacementPath,
        [string] $BackupPath
    )

    try {
        [System.IO.File]::Replace($ReplacementPath, $DestinationPath, $BackupPath, $true)
    }
    catch {
        return $true
    }
    [System.IO.File]::Replace($BackupPath, $DestinationPath, $ReplacementPath, $true)
    $false
}

function Complete-CountdownInstallation {
    param(
        [object] $Layout,
        [string] $TransactionPath,
        [object] $InteractiveContext,
        [string] $LaunchToken,
        [int] $ExpectedProcessId,
        [string] $ExpectedScheduledTaskIdentity,
        [scriptblock] $OnStage,
        [scriptblock] $OnCommitted
    )

    $global:CountdownIssue4AcceptDuringSourceReplacementRejected =
        Test-CountdownIssue4AcceptReplacementRejected `
            -DestinationPath $global:CountdownIssue4AcceptSourceProbePath `
            -ReplacementPath $global:CountdownIssue4AcceptSourceReplacementPath `
            -BackupPath $global:CountdownIssue4AcceptDuringSourceBackupPath
    $global:CountdownIssue4AcceptDuringInstalledReplacementRejected =
        Test-CountdownIssue4AcceptReplacementRejected `
            -DestinationPath $global:CountdownIssue4AcceptInstalledProbePath `
            -ReplacementPath $global:CountdownIssue4AcceptInstalledReplacementPath `
            -BackupPath $global:CountdownIssue4AcceptDuringInstalledBackupPath

    & $OnStage '提交安装事务'
    & $OnStage '清理已提交事务'
    & $OnCommitted
    [pscustomobject]@{
        AcceptanceState = 'finalized-ready'
        RuntimeProcessId = 4242
        TaskState = 'Ready'
        LastTaskResult = 0
        SettingsHashBefore = ('A' * 64)
        SettingsHashAfter = ('B' * 64)
        SettingsLengthBefore = 1
        SettingsLengthAfter = 2
        SettingsMtimeUtcBefore = '2026-01-01T00:00:00.0000000Z'
        SettingsMtimeUtcAfter = '2026-01-02T00:00:00.0000000Z'
        SettingsUnchanged = $false
    }
}

Export-ModuleMember -Function Complete-CountdownInstallation
'@,
        [System.Text.UTF8Encoding]::new($false))
    Copy-Item `
        -LiteralPath (Join-Path $remoteDeploymentInstall 'Countdown.Settings.psm1') `
        -Destination (Join-Path $acceptInstallRoot 'Countdown.Settings.psm1') `
        -Force
    $acceptSourceProbePath = Join-Path $acceptSourceRoot 'issue4-source-lock.txt'
    $acceptSourceReplacementPath = Join-Path $acceptSourceRoot 'issue4-source-replacement.txt'
    $acceptInstalledProbePath = Join-Path $acceptInstallRoot 'issue4-installed-lock.txt'
    $acceptInstalledReplacementPath = Join-Path $acceptInstallRoot 'issue4-installed-replacement.txt'
    [System.IO.File]::WriteAllText($acceptSourceProbePath, 'source-benign', [System.Text.UTF8Encoding]::new($false))
    [System.IO.File]::WriteAllText($acceptSourceReplacementPath, 'source-malicious', [System.Text.UTF8Encoding]::new($false))
    [System.IO.File]::WriteAllText($acceptInstalledProbePath, 'installed-benign', [System.Text.UTF8Encoding]::new($false))
    [System.IO.File]::WriteAllText($acceptInstalledReplacementPath, 'installed-malicious', [System.Text.UTF8Encoding]::new($false))
    $acceptManifest = [ordered]@{}
    foreach ($file in @(
            'Countdown.InstallTransaction.psm1',
            'Countdown.Runtime.psm1',
            'Countdown.InstallLifecycle.psm1',
            'issue4-source-lock.txt')) {
        $acceptManifest[$file] = (Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $acceptSourceRoot $file)).Hash
    }
    foreach ($file in @('Countdown.Settings.psm1', 'issue4-installed-lock.txt')) {
        $acceptManifest[$file] = (Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $acceptInstallRoot $file)).Hash
    }
    $acceptAdapter = & $remoteDeploymentModule { New-CountdownRemoteDeploymentWindowsAdapter }
    $acceptTransactionPath = Join-Path $acceptLockRoot 'transaction'
    New-Item -ItemType Directory -Path $acceptTransactionPath -Force | Out-Null
    $acceptAdapter.State.RollbackTransactionPath = $acceptTransactionPath
    $acceptAdapter.State.InteractiveContext = [pscustomobject]@{
        User = 'MINMIN\Issue4AcceptLocks'
        SessionId = 7
        Desktop = Join-Path $acceptLockRoot 'desktop'
    }
    $acceptAdapter.State.Layout = & $installLayoutCommand `
        -InstallPath $acceptInstallRoot `
        -TaskName 'Issue4 Accept Locks' `
        -LegacyTaskName 'Issue4 Accept Locks Legacy' `
        -Desktop (Join-Path $acceptLockRoot 'desktop')
    $acceptAdapter.State.LaunchToken = 'issue4-accept-lock-token'
    $acceptAdapter.State.ExpectedProcessId = 4242
    $acceptAdapter.State.ExpectedScheduledTaskIdentity = '<Task />'
    $acceptContext = [pscustomobject]@{
        SourceRoot = $acceptSourceRoot
        InstallPath = $acceptInstallRoot
        PowerShell7Path = $remoteDeploymentPowerShell
        DeferredReceiptPath = Join-Path $acceptLockRoot 'deferred-install-receipt.json'
        TaskName = 'Issue4 Accept Locks'
        LegacyTaskName = 'Issue4 Accept Locks Legacy'
        ExpectedFileSha256 = $acceptManifest
        TrustedFileManifestRequired = $true
        AdapterState = $acceptAdapter.State
    }
    $acceptAdapter.State.SourceFileLocks = & $remoteDeploymentModule {
        param($root, $manifest)
        Enter-CountdownRemoteDeploymentFileLocks `
            -Root $root `
            -ExpectedFileSha256 $manifest `
            -RelativePaths ([string[]] @(
                    'Countdown.InstallTransaction.psm1',
                    'Countdown.Runtime.psm1',
                    'Countdown.InstallLifecycle.psm1',
                    'issue4-source-lock.txt')) `
            -Stage 'Issue #4 Accept source locks'
    } $acceptSourceRoot $acceptManifest
    $acceptAdapter.State.InstalledFileLocks = & $remoteDeploymentModule {
        param($root, $manifest)
        Enter-CountdownRemoteDeploymentFileLocks `
            -Root $root `
            -ExpectedFileSha256 $manifest `
            -RelativePaths ([string[]] @('Countdown.Settings.psm1', 'issue4-installed-lock.txt')) `
            -Stage 'Issue #4 Accept installed locks'
    } $acceptInstallRoot $acceptManifest
    $acceptSourceReplacementRejected = -not (Invoke-CountdownIssue4AtomicReplacementProbe `
            -DestinationPath $acceptSourceProbePath `
            -ReplacementPath $acceptSourceReplacementPath `
            -BackupPath (Join-Path $acceptSourceRoot 'issue4-source-backup.txt'))
    $acceptInstalledReplacementRejected = -not (Invoke-CountdownIssue4AtomicReplacementProbe `
            -DestinationPath $acceptInstalledProbePath `
            -ReplacementPath $acceptInstalledReplacementPath `
            -BackupPath (Join-Path $acceptInstallRoot 'issue4-installed-backup.txt'))
    $global:CountdownIssue4AcceptSourceProbePath = $acceptSourceProbePath
    $global:CountdownIssue4AcceptSourceReplacementPath = $acceptSourceReplacementPath
    $global:CountdownIssue4AcceptInstalledProbePath = $acceptInstalledProbePath
    $global:CountdownIssue4AcceptInstalledReplacementPath = $acceptInstalledReplacementPath
    $global:CountdownIssue4AcceptDuringSourceBackupPath = Join-Path $acceptSourceRoot 'issue4-source-backup-during-accept.txt'
    $global:CountdownIssue4AcceptDuringInstalledBackupPath = Join-Path $acceptInstallRoot 'issue4-installed-backup-during-accept.txt'
    $global:CountdownIssue4AcceptDuringSourceReplacementRejected = $false
    $global:CountdownIssue4AcceptDuringInstalledReplacementRejected = $false
    $acceptResult = $null
    $acceptDuringSourceReplacementRejected = $false
    $acceptDuringInstalledReplacementRejected = $false
    $acceptAfterSourceReplacementRejected = $false
    $acceptAfterInstalledReplacementRejected = $false
    try {
        $acceptResult = & $acceptAdapter.Accept $acceptAdapter.State $acceptContext ([pscustomobject]@{})
        $acceptDuringSourceReplacementRejected = [bool] $global:CountdownIssue4AcceptDuringSourceReplacementRejected
        $acceptDuringInstalledReplacementRejected = [bool] $global:CountdownIssue4AcceptDuringInstalledReplacementRejected
        $acceptAfterSourceReplacementRejected = -not (Invoke-CountdownIssue4AtomicReplacementProbe `
                -DestinationPath $acceptSourceProbePath `
                -ReplacementPath $acceptSourceReplacementPath `
                -BackupPath (Join-Path $acceptSourceRoot 'issue4-source-backup-after-accept.txt'))
        $acceptAfterInstalledReplacementRejected = -not (Invoke-CountdownIssue4AtomicReplacementProbe `
                -DestinationPath $acceptInstalledProbePath `
                -ReplacementPath $acceptInstalledReplacementPath `
                -BackupPath (Join-Path $acceptInstallRoot 'issue4-installed-backup-after-accept.txt'))
    }
    finally {
        [void] (& $acceptAdapter.Dispose $acceptAdapter.State $acceptContext)
        foreach ($name in @(
                'CountdownIssue4AcceptSourceProbePath',
                'CountdownIssue4AcceptSourceReplacementPath',
                'CountdownIssue4AcceptInstalledProbePath',
                'CountdownIssue4AcceptInstalledReplacementPath',
                'CountdownIssue4AcceptDuringSourceBackupPath',
                'CountdownIssue4AcceptDuringInstalledBackupPath',
                'CountdownIssue4AcceptDuringSourceReplacementRejected',
                'CountdownIssue4AcceptDuringInstalledReplacementRejected')) {
            Remove-Variable -Name $name -Scope Global -ErrorAction SilentlyContinue
        }
        Import-Module (Join-Path $PSScriptRoot 'Countdown.InstallTransaction.psm1') -Force -DisableNameChecking
        Import-Module (Join-Path $PSScriptRoot 'Countdown.Runtime.psm1') -Force
        Import-Module (Join-Path $PSScriptRoot 'Countdown.Settings.psm1') -Force
        Import-Module (Join-Path $PSScriptRoot 'Countdown.InstallLifecycle.psm1') -Force
    }
    Assert-Equal $true $acceptSourceReplacementRejected 'Default adapter keeps source locks through the point immediately before Accept'
    Assert-Equal $true $acceptInstalledReplacementRejected 'Default adapter keeps installed locks through the point immediately before Accept'
    Assert-Equal $true $acceptDuringSourceReplacementRejected 'Default adapter rejects source replacement inside the Accept behavior chain'
    Assert-Equal $true $acceptDuringInstalledReplacementRejected 'Default adapter rejects installed-file replacement inside the Accept behavior chain'
    Assert-Equal 'finalized-ready' ([string] $acceptResult.AcceptanceState) 'Default adapter Accept completes while source and installed files remain locked'
    Assert-Equal $true $acceptAfterSourceReplacementRejected 'Default adapter keeps source locks after Accept returns and before Dispose'
    Assert-Equal $true $acceptAfterInstalledReplacementRejected 'Default adapter keeps installed locks after Accept returns and before Dispose'
    Assert-Equal $null $acceptAdapter.State.SourceFileLocks 'Default adapter Dispose clears source lock state'
    Assert-Equal $null $acceptAdapter.State.InstalledFileLocks 'Default adapter Dispose clears installed lock state'
    Assert-Equal $true (Invoke-CountdownIssue4AtomicReplacementProbe `
            -DestinationPath $acceptSourceProbePath `
            -ReplacementPath $acceptSourceReplacementPath `
            -BackupPath (Join-Path $acceptSourceRoot 'issue4-source-backup-after-dispose.txt') `
            -Restore) 'Default adapter Dispose releases source locks'
    Assert-Equal $true (Invoke-CountdownIssue4AtomicReplacementProbe `
            -DestinationPath $acceptInstalledProbePath `
            -ReplacementPath $acceptInstalledReplacementPath `
            -BackupPath (Join-Path $acceptInstallRoot 'issue4-installed-backup-after-dispose.txt') `
            -Restore) 'Default adapter Dispose releases installed locks'

    $rollbackLockRoot = Join-Path $remoteDeploymentTestRoot 'issue4-rollback-locks'
    $rollbackSourceRoot = Join-Path $rollbackLockRoot 'source'
    $rollbackInstallRoot = Join-Path $rollbackLockRoot 'install'
    $rollbackTransactionPath = Join-Path $rollbackLockRoot 'transaction'
    New-Item -ItemType Directory -Path $rollbackSourceRoot -Force | Out-Null
    New-Item -ItemType Directory -Path $rollbackInstallRoot -Force | Out-Null
    New-Item -ItemType Directory -Path $rollbackTransactionPath -Force | Out-Null
    [System.IO.File]::WriteAllText(
        (Join-Path $rollbackTransactionPath 'journal.json'),
        '{}',
        [System.Text.UTF8Encoding]::new($false))
    $rollbackTransactionModulePath = Join-Path $rollbackSourceRoot 'Countdown.InstallTransaction.psm1'
    [System.IO.File]::WriteAllText(
        $rollbackTransactionModulePath,
        "Set-StrictMode -Version Latest`r`n",
        [System.Text.UTF8Encoding]::new($false))
    $rollbackLifecycleModulePath = Join-Path $rollbackSourceRoot 'Countdown.InstallLifecycle.psm1'
    [System.IO.File]::WriteAllText(
        $rollbackLifecycleModulePath,
        @'
function Undo-CountdownInstallation {
    param(
        [object] $Layout,
        [string] $TransactionPath,
        [string] $PowerShell7Path
    )

    try {
        [System.IO.File]::Replace(
            $global:CountdownIssue4RollbackInstalledReplacementPath,
            $global:CountdownIssue4RollbackInstalledProbePath,
            $global:CountdownIssue4RollbackInstalledBackupPath,
            $true)
        $global:CountdownIssue4RollbackInstalledReplacementSucceeded = $true
        [System.IO.File]::Replace(
            $global:CountdownIssue4RollbackInstalledBackupPath,
            $global:CountdownIssue4RollbackInstalledProbePath,
            $global:CountdownIssue4RollbackInstalledReplacementPath,
            $true)
    }
    catch {
        $global:CountdownIssue4RollbackInstalledReplacementSucceeded = $false
    }
    try {
        [System.IO.File]::Replace(
            $global:CountdownIssue4RollbackSourceReplacementPath,
            $global:CountdownIssue4RollbackSourceProbePath,
            $global:CountdownIssue4RollbackSourceBackupPath,
            $true)
        $global:CountdownIssue4RollbackSourceReplacementRejected = $false
    }
    catch {
        $global:CountdownIssue4RollbackSourceReplacementRejected = $true
    }
    [pscustomobject]@{ PreservedUnmanagedLaunchers = $false }
}

Export-ModuleMember -Function Undo-CountdownInstallation
'@,
        [System.Text.UTF8Encoding]::new($false))
    $rollbackSourceProbePath = Join-Path $rollbackSourceRoot 'issue4-source-lock.txt'
    $rollbackSourceReplacementPath = Join-Path $rollbackSourceRoot 'issue4-source-replacement.txt'
    $rollbackInstalledProbePath = Join-Path $rollbackInstallRoot 'issue4-installed-lock.txt'
    $rollbackInstalledReplacementPath = Join-Path $rollbackInstallRoot 'issue4-installed-replacement.txt'
    [System.IO.File]::WriteAllText($rollbackSourceProbePath, 'source-benign', [System.Text.UTF8Encoding]::new($false))
    [System.IO.File]::WriteAllText($rollbackSourceReplacementPath, 'source-malicious', [System.Text.UTF8Encoding]::new($false))
    [System.IO.File]::WriteAllText($rollbackInstalledProbePath, 'installed-benign', [System.Text.UTF8Encoding]::new($false))
    [System.IO.File]::WriteAllText($rollbackInstalledReplacementPath, 'installed-malicious', [System.Text.UTF8Encoding]::new($false))
    $global:CountdownIssue4RollbackInstalledProbePath = $rollbackInstalledProbePath
    $global:CountdownIssue4RollbackInstalledReplacementPath = $rollbackInstalledReplacementPath
    $global:CountdownIssue4RollbackInstalledBackupPath = Join-Path $rollbackInstallRoot 'issue4-installed-backup.txt'
    $global:CountdownIssue4RollbackSourceProbePath = $rollbackSourceProbePath
    $global:CountdownIssue4RollbackSourceReplacementPath = $rollbackSourceReplacementPath
    $global:CountdownIssue4RollbackSourceBackupPath = Join-Path $rollbackSourceRoot 'issue4-source-backup.txt'
    $global:CountdownIssue4RollbackInstalledReplacementSucceeded = $false
    $global:CountdownIssue4RollbackSourceReplacementRejected = $false
    $rollbackManifest = [ordered]@{
        'Countdown.InstallTransaction.psm1' = (Get-FileHash -Algorithm SHA256 -LiteralPath $rollbackTransactionModulePath).Hash
        'Countdown.InstallLifecycle.psm1' = (Get-FileHash -Algorithm SHA256 -LiteralPath $rollbackLifecycleModulePath).Hash
        'issue4-source-lock.txt' = (Get-FileHash -Algorithm SHA256 -LiteralPath $rollbackSourceProbePath).Hash
        'issue4-installed-lock.txt' = (Get-FileHash -Algorithm SHA256 -LiteralPath $rollbackInstalledProbePath).Hash
    }
    $rollbackAdapter = & $remoteDeploymentModule { New-CountdownRemoteDeploymentWindowsAdapter }
    $rollbackAdapter.State.TransactionModuleImported = $true
    $rollbackAdapter.State.InstallStarted = $true
    $rollbackAdapter.State.RollbackTransactionPath = $rollbackTransactionPath
    $rollbackAdapter.State.Layout = & $installLayoutCommand `
        -InstallPath $rollbackInstallRoot `
        -TaskName 'Issue4 Rollback Locks' `
        -LegacyTaskName 'Issue4 Rollback Locks Legacy' `
        -Desktop (Join-Path $rollbackLockRoot 'desktop')
    $rollbackContext = [pscustomobject]@{
        SourceRoot = $rollbackSourceRoot
        InstallPath = $rollbackInstallRoot
        PowerShell7Path = $remoteDeploymentPowerShell
        DeferredReceiptPath = Join-Path $rollbackLockRoot 'deferred-install-receipt.json'
        TaskName = 'Issue4 Rollback Locks'
        LegacyTaskName = 'Issue4 Rollback Locks Legacy'
        ExpectedFileSha256 = $rollbackManifest
        TrustedFileManifestRequired = $true
        AdapterState = $rollbackAdapter.State
    }
    $rollbackAdapter.State.SourceFileLocks = & $remoteDeploymentModule {
        param($root, $manifest)
        Enter-CountdownRemoteDeploymentFileLocks `
            -Root $root `
            -ExpectedFileSha256 $manifest `
            -RelativePaths ([string[]] @('Countdown.InstallTransaction.psm1', 'Countdown.InstallLifecycle.psm1', 'issue4-source-lock.txt')) `
            -Stage 'Issue #4 Rollback source locks'
    } $rollbackSourceRoot $rollbackManifest
    $rollbackAdapter.State.InstalledFileLocks = & $remoteDeploymentModule {
        param($root, $manifest)
        Enter-CountdownRemoteDeploymentFileLocks `
            -Root $root `
            -ExpectedFileSha256 $manifest `
            -RelativePaths ([string[]] @('issue4-installed-lock.txt')) `
            -Stage 'Issue #4 Rollback installed locks'
    } $rollbackInstallRoot $rollbackManifest
    try {
        [void] (& $rollbackAdapter.Rollback $rollbackAdapter.State $rollbackContext ([pscustomobject]@{}))
        Assert-Equal $true ([bool] $global:CountdownIssue4RollbackInstalledReplacementSucceeded) 'Default adapter Rollback releases installed locks before restoring the install directory'
        Assert-Equal $true ([bool] $global:CountdownIssue4RollbackSourceReplacementRejected) 'Default adapter Rollback retains source locks while restoring the install directory'
        Assert-Equal $null $rollbackAdapter.State.InstalledFileLocks 'Default adapter Rollback clears installed lock state before restoration'
        Assert-Equal $true ($null -ne $rollbackAdapter.State.SourceFileLocks) 'Default adapter Rollback keeps source lock state until Dispose'
    }
    finally {
        [void] (& $rollbackAdapter.Dispose $rollbackAdapter.State $rollbackContext)
        foreach ($name in @(
                'CountdownIssue4RollbackInstalledProbePath',
                'CountdownIssue4RollbackInstalledReplacementPath',
                'CountdownIssue4RollbackInstalledBackupPath',
                'CountdownIssue4RollbackSourceProbePath',
                'CountdownIssue4RollbackSourceReplacementPath',
                'CountdownIssue4RollbackSourceBackupPath',
                'CountdownIssue4RollbackInstalledReplacementSucceeded',
                'CountdownIssue4RollbackSourceReplacementRejected')) {
            Remove-Variable -Name $name -Scope Global -ErrorAction SilentlyContinue
        }
        Import-Module (Join-Path $PSScriptRoot 'Countdown.InstallTransaction.psm1') -Force -DisableNameChecking
    }
    Assert-Equal $null $rollbackAdapter.State.SourceFileLocks 'Default adapter Dispose clears the source locks retained by Rollback'
    Assert-Equal $true (Invoke-CountdownIssue4AtomicReplacementProbe `
            -DestinationPath $rollbackSourceProbePath `
            -ReplacementPath $rollbackSourceReplacementPath `
            -BackupPath (Join-Path $rollbackSourceRoot 'issue4-source-backup-after-dispose.txt') `
            -Restore) 'Default adapter Dispose finally releases Rollback source locks'

    $missingAcceptancePath = Join-Path $remoteDeploymentTestRoot 'missing-acceptance-status.json'
    $expectedAcceptanceApplicationPath = Join-Path $remoteDeploymentInstall '敏敏子倒计时.ps1'
    $expectedAcceptanceExecutablePath = Join-Path $remoteDeploymentInstall 'app\MinminCountdown.exe'
    $aliveMissingAcceptanceMessage = $null
    try {
        Wait-CountdownAcceptanceReadyReceipt `
            -Path $missingAcceptancePath `
            -LaunchToken 'missing-acceptance-token' `
            -ExpectedProcessId 4242 `
            -ExpectedSessionId 7 `
            -ExpectedApplicationPath $expectedAcceptanceApplicationPath `
            -TimeoutSeconds 0 `
            -PollMilliseconds 1 `
            -ProcessProbe {
                param([int] $ProcessId)
                [pscustomobject]@{
                    ProcessId = $ProcessId
                    SessionId = 7
                    ExecutablePath = $expectedAcceptanceExecutablePath
                }
            } | Out-Null
    }
    catch {
        $aliveMissingAcceptanceMessage = $_.Exception.Message
    }
    Assert-Equal $true ($aliveMissingAcceptanceMessage -match '回执不存在.*期望进程 4242 仍在运行.*会话和可执行路径匹配') 'Acceptance wait distinguishes a living candidate process when the receipt is absent'

    $deadMissingAcceptanceMessage = $null
    try {
        Wait-CountdownAcceptanceReadyReceipt `
            -Path $missingAcceptancePath `
            -LaunchToken 'missing-acceptance-token' `
            -ExpectedProcessId 4242 `
            -ExpectedSessionId 7 `
            -ExpectedApplicationPath $expectedAcceptanceApplicationPath `
            -TimeoutSeconds 0 `
            -PollMilliseconds 1 `
            -ProcessProbe { param([int] $ProcessId) $null } | Out-Null
    }
    catch {
        $deadMissingAcceptanceMessage = $_.Exception.Message
    }
    Assert-Equal $true ($deadMissingAcceptanceMessage -match '回执不存在.*期望进程 4242 不存在') 'Acceptance wait distinguishes a missing candidate process when the receipt is absent'

    $successFixture = New-CountdownRemoteDeploymentFixtureAdapter -Mode Success
    $successResult = Invoke-CountdownRemoteDeployment `
        -SourceRoot $remoteDeploymentSource `
        -InstallPath $remoteDeploymentInstall `
        -PowerShell7Path $remoteDeploymentPowerShell `
        -DeferredReceiptPath $remoteDeploymentReceipt `
        -TaskName 'Minmin Countdown Fixture' `
        -LegacyTaskName '敏敏子倒计时 Fixture' `
        -Adapter $successFixture.Adapter
    Assert-Equal $true ([bool] $successResult.Valid) 'Remote deployment interface reports success only after acceptance'
    Assert-Equal 'finalized-ready' ([string] $successResult.AcceptanceState) 'Remote deployment interface exposes finalized-ready'
    Assert-Equal 2 ([int] $successResult.SchemaVersion) 'Remote deployment interface reports the install-only success schema'
    Assert-Equal '' (@(@('CoreTests', 'CSharpTests', 'UiTests', 'UiValid', 'AdapterUiValidated') | Where-Object { $successResult.PSObject.Properties.Name -contains $_ }) -join ',') 'Remote deployment success carries no remote test results'
    Assert-Equal $true ([bool] $successFixture.State.Accepted) 'Remote deployment success reaches acceptance'
    Assert-Equal $false ([bool] $successFixture.State.RolledBack) 'Remote deployment success does not roll back'
    Assert-Equal $true ([bool] $successFixture.State.Disposed) 'Remote deployment success releases its host adapter'

    $expectedDeploymentBindingHash = '9' * 64
    $bindingTestRoot = Join-Path $remoteDeploymentTestRoot 'binding-success'
    New-Item -ItemType Directory -Path $bindingTestRoot -Force | Out-Null
    $remoteBindingPath = Join-Path $bindingTestRoot 'deployment-binding.json'
    $bindingFixture = New-CountdownRemoteDeploymentFixtureAdapter -Mode Success
    $bindingResult = Invoke-CountdownRemoteDeployment `
        -SourceRoot $remoteDeploymentSource `
        -InstallPath $remoteDeploymentInstall `
        -PowerShell7Path $remoteDeploymentPowerShell `
        -DeferredReceiptPath (Join-Path $bindingTestRoot 'deferred-install-receipt.json') `
        -TaskName 'Minmin Countdown Fixture' `
        -LegacyTaskName '敏敏子倒计时 Fixture' `
        -DeploymentBindingPath $remoteBindingPath `
        -DeploymentBindingSha256 $expectedDeploymentBindingHash `
        -Adapter $bindingFixture.Adapter
    $bindingBytes = [System.IO.File]::ReadAllBytes($remoteBindingPath)
    $bindingDocument = ConvertFrom-CountdownRemoteDeploymentBindingJson `
        -Json ([System.Text.UTF8Encoding]::new($false, $true).GetString($bindingBytes)) `
        -ExpectedDeploymentBindingSha256 $expectedDeploymentBindingHash `
        -ExpectedProcessId 4242
    $expectedFixtureTokenHash = [Convert]::ToHexString(
        [System.Security.Cryptography.SHA256]::HashData(
            [System.Text.UTF8Encoding]::new($false).GetBytes('sensitive-launch-token-sentinel')))
    Assert-Equal 15 @($bindingResult.PSObject.Properties).Count 'Deployment binding does not change the strict success receipt schema'
    Assert-Equal 4 @($bindingDocument.PSObject.Properties).Count 'Deployment binding sidecar uses the exact independent schema'
    Assert-Equal $expectedDeploymentBindingHash ([string] $bindingDocument.DeploymentBindingSha256) 'Deployment binding sidecar preserves the locally generated nonce hash'
    Assert-Equal $expectedFixtureTokenHash ([string] $bindingDocument.TokenSha256) 'Deployment binding sidecar binds the committed launch token hash'
    Assert-Equal $false ([bool] $bindingFixture.State.RolledBack) 'Successful binding publication never rolls back a committed deployment'

    $bindingFailureTestRoot = Join-Path $remoteDeploymentTestRoot 'binding-failure'
    New-Item -ItemType Directory -Path $bindingFailureTestRoot -Force | Out-Null
    $bindingFailurePath = Join-Path $bindingFailureTestRoot 'deployment-binding-existing.json'
    [System.IO.File]::WriteAllText($bindingFailurePath, 'existing', [System.Text.UTF8Encoding]::new($false))
    $bindingFailureFixture = New-CountdownRemoteDeploymentFixtureAdapter -Mode Success
    $bindingFailureMessage = $null
    try {
        Invoke-CountdownRemoteDeployment `
            -SourceRoot $remoteDeploymentSource `
            -InstallPath $remoteDeploymentInstall `
            -PowerShell7Path $remoteDeploymentPowerShell `
            -DeferredReceiptPath (Join-Path $bindingFailureTestRoot 'deferred-install-receipt.json') `
            -TaskName 'Minmin Countdown Fixture' `
            -LegacyTaskName '敏敏子倒计时 Fixture' `
            -DeploymentBindingPath $bindingFailurePath `
            -DeploymentBindingSha256 $expectedDeploymentBindingHash `
            -Adapter $bindingFailureFixture.Adapter | Out-Null
    }
    catch { $bindingFailureMessage = $_.Exception.Message }
    Assert-Equal $true ($bindingFailureMessage -match '部署已提交.*未回滚.*binding') 'Binding publication failure reports a committed deployment with incomplete verification'
    Assert-Equal 0 ([int] $bindingFailureFixture.State.RollbackCount) 'Binding publication failure never invokes the rollback seam after commit'
    Assert-Equal 'existing' ([System.IO.File]::ReadAllText($bindingFailurePath, [System.Text.Encoding]::UTF8)) 'Binding publication failure preserves existing bytes'

    $successReceiptJson = $successResult | ConvertTo-Json -Compress -Depth 4
    $normalizedSuccessReceipt = ConvertFrom-CountdownRemoteDeploymentSuccessReceiptJson -Json $successReceiptJson
    Assert-Equal 15 @($normalizedSuccessReceipt.PSObject.Properties).Count 'Success receipt parser returns the exact schema field set'
    Assert-Equal 'Ready' ([string] $normalizedSuccessReceipt.TaskState) 'Success receipt parser requires the ready task state'
    Assert-Equal 0 ([int] $normalizedSuccessReceipt.LastTaskResult) 'Success receipt parser requires lastTaskResult 0'
    Assert-Equal 'window-ready' ([string] $normalizedSuccessReceipt.RuntimeStage) 'Success receipt parser requires window-ready runtime state'
    Assert-Equal $false ($normalizedSuccessReceipt.PSObject.Properties.Name -contains 'LaunchToken') 'Success receipt schema excludes the raw launch token'
    $unchangedTrueReceipt = $successReceiptJson | ConvertFrom-Json -AsHashtable
    $unchangedTrueReceipt.SettingsHashAfter = $unchangedTrueReceipt.SettingsHashBefore
    $unchangedTrueReceipt.SettingsLengthAfter = $unchangedTrueReceipt.SettingsLengthBefore
    $unchangedTrueReceipt.SettingsMtimeUtcAfter = $unchangedTrueReceipt.SettingsMtimeUtcBefore
    $unchangedTrueReceipt.SettingsUnchanged = $true
    $acceptedUnchangedReceipt = ConvertFrom-CountdownRemoteDeploymentSuccessReceiptJson `
        -Json ($unchangedTrueReceipt | ConvertTo-Json -Compress)
    Assert-Equal $true ([bool] $acceptedUnchangedReceipt.SettingsUnchanged) 'Success receipt parser accepts SettingsUnchanged computed from identical fingerprints'
    # 历史 v1 回执带远端三组测试计数；只为识别既有部署证据而继续可读。
    $historicalReceipt = [ordered]@{ SchemaVersion = 1; Valid = $true; CoreTests = 11; CSharpTests = 13; UiTests = 17; UiValid = $true; AdapterUiValidated = $true }
    foreach ($entry in ($successReceiptJson | ConvertFrom-Json -AsHashtable).GetEnumerator()) {
        if ($entry.Key -notin @('SchemaVersion', 'Valid')) { $historicalReceipt[$entry.Key] = $entry.Value }
    }
    $historicalReceiptJson = $historicalReceipt | ConvertTo-Json -Compress
    $acceptedHistoricalReceipt = ConvertFrom-CountdownRemoteDeploymentSuccessReceiptJson -Json $historicalReceiptJson
    Assert-Equal $true (
            [int] $acceptedHistoricalReceipt.SchemaVersion -eq 1 -and
            [int] $acceptedHistoricalReceipt.CoreTests -eq 11 -and
            @($acceptedHistoricalReceipt.PSObject.Properties).Count -eq 20) 'Success receipt parser still reads historical v1 receipts with remote test counts'

    $invalidSuccessReceiptCases = [ordered]@{}
    $missingFieldReceipt = $successReceiptJson | ConvertFrom-Json -AsHashtable
    [void] $missingFieldReceipt.Remove('CalendarState')
    $invalidSuccessReceiptCases['missing field'] = $missingFieldReceipt | ConvertTo-Json -Compress
    $remoteTestFieldReceipt = $successReceiptJson | ConvertFrom-Json -AsHashtable
    $remoteTestFieldReceipt['CoreTests'] = 11
    $invalidSuccessReceiptCases['v2 with remote test field'] = $remoteTestFieldReceipt | ConvertTo-Json -Compress
    $historicalMissingTestReceipt = $historicalReceiptJson | ConvertFrom-Json -AsHashtable
    [void] $historicalMissingTestReceipt.Remove('UiTests')
    $invalidSuccessReceiptCases['v1 missing remote test field'] = $historicalMissingTestReceipt | ConvertTo-Json -Compress
    $extraFieldReceipt = $successReceiptJson | ConvertFrom-Json -AsHashtable
    $extraFieldReceipt['Credential'] = 'must-not-be-persisted'
    $invalidSuccessReceiptCases['extra field'] = $extraFieldReceipt | ConvertTo-Json -Compress
    $invalidSuccessReceiptCases['truncated JSON'] = $successReceiptJson.Substring(0, $successReceiptJson.Length - 1)
    $invalidSuccessReceiptCases['multiple objects'] = "$successReceiptJson`n$successReceiptJson"
    $invalidSuccessReceiptCases['noise output'] = "unexpected output`n$successReceiptJson"
    $invalidSuccessReceiptCases['duplicate field'] = $successReceiptJson.Insert(1, '"Valid":true,')
    $unsupportedSchemaReceipt = $successReceiptJson | ConvertFrom-Json -AsHashtable
    $unsupportedSchemaReceipt.SchemaVersion = 3
    $invalidSuccessReceiptCases['unsupported schema'] = $unsupportedSchemaReceipt | ConvertTo-Json -Compress
    $failedStatusReceipt = $successReceiptJson | ConvertFrom-Json -AsHashtable
    $failedStatusReceipt.Valid = $false
    $invalidSuccessReceiptCases['failed status'] = $failedStatusReceipt | ConvertTo-Json -Compress
    $wrongTypeReceipt = $successReceiptJson | ConvertFrom-Json -AsHashtable
    $wrongTypeReceipt.RuntimeProcessId = '4242'
    $invalidSuccessReceiptCases['wrong type'] = $wrongTypeReceipt | ConvertTo-Json -Compress
    $invalidRangeReceipt = $successReceiptJson | ConvertFrom-Json -AsHashtable
    $invalidRangeReceipt.RuntimeProcessId = 0
    $invalidSuccessReceiptCases['invalid range'] = $invalidRangeReceipt | ConvertTo-Json -Compress
    $invalidStateReceipt = $successReceiptJson | ConvertFrom-Json -AsHashtable
    $invalidStateReceipt.AcceptanceState = 'accepted'
    $invalidSuccessReceiptCases['invalid state'] = $invalidStateReceipt | ConvertTo-Json -Compress
    $runningTaskReceipt = $successReceiptJson | ConvertFrom-Json -AsHashtable
    $runningTaskReceipt.TaskState = 'Running'
    $invalidSuccessReceiptCases['task state running'] = $runningTaskReceipt | ConvertTo-Json -Compress
    $nonzeroLastTaskReceipt = $successReceiptJson | ConvertFrom-Json -AsHashtable
    $nonzeroLastTaskReceipt.LastTaskResult = 1
    $invalidSuccessReceiptCases['nonzero lastTaskResult'] = $nonzeroLastTaskReceipt | ConvertTo-Json -Compress
    $missingLastTaskReceipt = $successReceiptJson | ConvertFrom-Json -AsHashtable
    [void] $missingLastTaskReceipt.Remove('LastTaskResult')
    $invalidSuccessReceiptCases['missing lastTaskResult'] = $missingLastTaskReceipt | ConvertTo-Json -Compress
    $missingSettingsLengthReceipt = $successReceiptJson | ConvertFrom-Json -AsHashtable
    [void] $missingSettingsLengthReceipt.Remove('SettingsLengthBefore')
    $invalidSuccessReceiptCases['missing settings length'] = $missingSettingsLengthReceipt | ConvertTo-Json -Compress
    $missingSettingsMtimeReceipt = $successReceiptJson | ConvertFrom-Json -AsHashtable
    [void] $missingSettingsMtimeReceipt.Remove('SettingsMtimeUtcBefore')
    $invalidSuccessReceiptCases['missing settings mtime'] = $missingSettingsMtimeReceipt | ConvertTo-Json -Compress
    $missingSettingsUnchangedReceipt = $successReceiptJson | ConvertFrom-Json -AsHashtable
    [void] $missingSettingsUnchangedReceipt.Remove('SettingsUnchanged')
    $invalidSuccessReceiptCases['missing settings unchanged'] = $missingSettingsUnchangedReceipt | ConvertTo-Json -Compress
    $unchangedMismatchReceipt = $successReceiptJson | ConvertFrom-Json -AsHashtable
    $unchangedMismatchReceipt.SettingsUnchanged = $true
    $invalidSuccessReceiptCases['settings unchanged mismatch'] = $unchangedMismatchReceipt | ConvertTo-Json -Compress
    $unchangedFalseOnIdenticalReceipt = $successReceiptJson | ConvertFrom-Json -AsHashtable
    $unchangedFalseOnIdenticalReceipt.SettingsHashAfter = $unchangedFalseOnIdenticalReceipt.SettingsHashBefore
    $unchangedFalseOnIdenticalReceipt.SettingsLengthAfter = $unchangedFalseOnIdenticalReceipt.SettingsLengthBefore
    $unchangedFalseOnIdenticalReceipt.SettingsMtimeUtcAfter = $unchangedFalseOnIdenticalReceipt.SettingsMtimeUtcBefore
    $unchangedFalseOnIdenticalReceipt.SettingsUnchanged = $false
    $invalidSuccessReceiptCases['settings unchanged false on identical fingerprint'] = $unchangedFalseOnIdenticalReceipt | ConvertTo-Json -Compress
    foreach ($case in $invalidSuccessReceiptCases.GetEnumerator()) {
        $rejected = $false
        try {
            ConvertFrom-CountdownRemoteDeploymentSuccessReceiptJson -Json ([string] $case.Value) | Out-Null
        }
        catch {
            $rejected = $true
        }
        Assert-Equal $true $rejected "Success receipt parser rejects $($case.Key)"
    }

    $successReceiptEvidencePath = Join-Path $remoteDeploymentTestRoot 'remote-deployment-success.json'
    $persistedSuccessReceipt = Write-CountdownRemoteDeploymentSuccessReceipt `
        -Path $successReceiptEvidencePath `
        -Json $successReceiptJson
    $persistedSuccessBytes = [System.IO.File]::ReadAllBytes($successReceiptEvidencePath)
    $persistedSuccessText = [System.Text.UTF8Encoding]::new($false, $true).GetString($persistedSuccessBytes)
    Assert-Equal $true (Test-Path -LiteralPath $successReceiptEvidencePath -PathType Leaf) 'Success receipt writer atomically publishes one evidence file'
    Assert-Equal $false ($persistedSuccessBytes.Length -ge 3 -and $persistedSuccessBytes[0] -eq 0xEF -and $persistedSuccessBytes[1] -eq 0xBB -and $persistedSuccessBytes[2] -eq 0xBF) 'Success receipt evidence is UTF-8 without BOM'
    Assert-Equal $false ($persistedSuccessText -match 'Credential|LaunchToken|sensitive-launch-token-sentinel') 'Success receipt evidence excludes credentials and raw launch tokens'
    Assert-Equal 15 @($persistedSuccessReceipt.PSObject.Properties).Count 'Success receipt writer returns the normalized exact schema'
    [void] (ConvertFrom-CountdownRemoteDeploymentSuccessReceiptJson -Json $persistedSuccessText)
    $persistedSuccessHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $successReceiptEvidencePath).Hash

    $existingEvidenceRejected = $false
    try {
        Write-CountdownRemoteDeploymentSuccessReceipt -Path $successReceiptEvidencePath -Json $successReceiptJson | Out-Null
    }
    catch {
        $existingEvidenceRejected = $_.Exception.Message -match '部署已提交但本地证据不完整.*未回滚'
    }
    Assert-Equal $true $existingEvidenceRejected 'Success receipt persistence failure reports committed deployment with incomplete local evidence and no rollback claim'
    Assert-Equal $persistedSuccessHash (Get-FileHash -Algorithm SHA256 -LiteralPath $successReceiptEvidencePath).Hash 'Failed success receipt publication preserves the existing evidence bytes'
    Assert-Equal 0 @(Get-ChildItem -LiteralPath $remoteDeploymentTestRoot -File -Filter 'remote-deployment-success.json.*.tmp').Count 'Success receipt writer leaves no temporary file after publication failure'

    $invalidEvidencePath = Join-Path $remoteDeploymentTestRoot 'invalid-success.json'
    $invalidEvidenceRejected = $false
    try {
        Write-CountdownRemoteDeploymentSuccessReceipt `
            -Path $invalidEvidencePath `
            -Json $invalidSuccessReceiptCases['truncated JSON'] | Out-Null
    }
    catch {
        $invalidEvidenceRejected = $_.Exception.Message -match '部署已提交但本地证据不完整.*未回滚'
    }
    Assert-Equal $true $invalidEvidenceRejected 'Invalid remote success output reports committed deployment with incomplete local evidence'
    Assert-Equal $false (Test-Path -LiteralPath $invalidEvidencePath) 'Invalid success output never creates local success evidence'

    $expectedReadbackExecutableHash = 'C' * 64
    $expectedReadbackSettingsHash = 'D' * 64
    $validReadback = [ordered]@{
        SchemaVersion = 1
        Valid = $true
        HostIdentityMatch = $true
        UserIdentityMatch = $true
        RemoteMachineNameSha256 = 'A' * 64
        RemoteUserNameSha256 = 'B' * 64
        TaskState = 'Ready'
        LastTaskResult = 0
        TaskExecuteMatch = $true
        TaskArgumentsMatch = $true
        TaskWorkingDirectoryMatch = $true
        StartupSchemaVersion = 1
        StartupAcceptanceState = 'finalized'
        RuntimeSchemaVersion = 4
        RuntimeStage = 'window-ready'
        AcceptanceSchemaVersion = 1
        AcceptanceStage = 'finalized-ready'
        TokenSha256 = 'E' * 64
        TokenMatch = $true
        RuntimeProcessId = 4242
        AcceptanceProcessId = 4242
        ProcessIdMatch = $true
        RuntimeSessionId = 7
        AcceptanceSessionId = 7
        ProcessSessionId = 7
        SessionIdMatch = $true
        ApplicationPathMatch = $true
        ProcessExecutablePathMatch = $true
        ExecutableSha256 = $expectedReadbackExecutableHash
        ExecutableHashMatch = $true
        SettingsSha256 = $expectedReadbackSettingsHash
        SettingsHashMatch = $true
        SettingsLength = 2
        SettingsMtimeUtc = '2026-01-02T00:00:00.0000000Z'
        SettingsUnchanged = $false
    }
    $validReadbackJson = $validReadback | ConvertTo-Json -Compress
    $normalizedReadback = ConvertFrom-CountdownRemoteDeploymentReadbackJson `
        -Json $validReadbackJson `
        -ExpectedProcessId 4242 `
        -ExpectedExecutableSha256 $expectedReadbackExecutableHash `
        -ExpectedTokenSha256 ('E' * 64) `
        -ExpectedSettingsSha256 $expectedReadbackSettingsHash
    Assert-Equal 35 @($normalizedReadback.PSObject.Properties).Count 'Independent readback parser returns the exact schema field set'
    Assert-Equal $true ([bool] $normalizedReadback.Valid) 'Independent readback parser accepts a fully cross-bound receipt'
    Assert-Equal $false ($normalizedReadback.PSObject.Properties.Name -contains 'LaunchToken') 'Independent readback schema excludes the raw launch token'

    $invalidReadbackCases = [ordered]@{}
    $readbackMissingField = $validReadbackJson | ConvertFrom-Json -AsHashtable
    [void] $readbackMissingField.Remove('TokenMatch')
    $invalidReadbackCases['missing field'] = $readbackMissingField | ConvertTo-Json -Compress
    $readbackExtraField = $validReadbackJson | ConvertFrom-Json -AsHashtable
    $readbackExtraField.LaunchToken = 'sensitive-launch-token-sentinel'
    $invalidReadbackCases['raw token field'] = $readbackExtraField | ConvertTo-Json -Compress
    $invalidReadbackCases['duplicate field'] = $validReadbackJson.Insert(1, '"Valid":true,')
    $readbackWrongSchema = $validReadbackJson | ConvertFrom-Json -AsHashtable
    $readbackWrongSchema.SchemaVersion = 2
    $invalidReadbackCases['output schema'] = $readbackWrongSchema | ConvertTo-Json -Compress
    foreach ($field in @('HostIdentityMatch', 'UserIdentityMatch')) {
        $caseValue = $validReadbackJson | ConvertFrom-Json -AsHashtable
        $caseValue[$field] = $false
        $invalidReadbackCases["identity $field"] = $caseValue | ConvertTo-Json -Compress
    }
    $readbackTaskState = $validReadbackJson | ConvertFrom-Json -AsHashtable
    $readbackTaskState.TaskState = 'Running'
    $invalidReadbackCases['task state'] = $readbackTaskState | ConvertTo-Json -Compress
    $readbackLastTaskResult = $validReadbackJson | ConvertFrom-Json -AsHashtable
    $readbackLastTaskResult.LastTaskResult = 1
    $invalidReadbackCases['nonzero lastTaskResult'] = $readbackLastTaskResult | ConvertTo-Json -Compress
    $readbackMissingLastTaskResult = $validReadbackJson | ConvertFrom-Json -AsHashtable
    [void] $readbackMissingLastTaskResult.Remove('LastTaskResult')
    $invalidReadbackCases['missing lastTaskResult'] = $readbackMissingLastTaskResult | ConvertTo-Json -Compress
    $readbackMissingSettingsLength = $validReadbackJson | ConvertFrom-Json -AsHashtable
    [void] $readbackMissingSettingsLength.Remove('SettingsLength')
    $invalidReadbackCases['missing settings length'] = $readbackMissingSettingsLength | ConvertTo-Json -Compress
    $readbackMissingSettingsMtime = $validReadbackJson | ConvertFrom-Json -AsHashtable
    [void] $readbackMissingSettingsMtime.Remove('SettingsMtimeUtc')
    $invalidReadbackCases['missing settings mtime'] = $readbackMissingSettingsMtime | ConvertTo-Json -Compress
    $readbackMissingSettingsUnchanged = $validReadbackJson | ConvertFrom-Json -AsHashtable
    [void] $readbackMissingSettingsUnchanged.Remove('SettingsUnchanged')
    $invalidReadbackCases['missing settings unchanged'] = $readbackMissingSettingsUnchanged | ConvertTo-Json -Compress
    foreach ($field in @('TaskExecuteMatch', 'TaskArgumentsMatch', 'TaskWorkingDirectoryMatch')) {
        $caseValue = $validReadbackJson | ConvertFrom-Json -AsHashtable
        $caseValue[$field] = $false
        $invalidReadbackCases["task action $field"] = $caseValue | ConvertTo-Json -Compress
    }
    foreach ($definition in @(
            @('StartupAcceptanceState', 'ready'),
            @('RuntimeStage', 'starting'),
            @('AcceptanceStage', 'accepted'))) {
        $caseValue = $validReadbackJson | ConvertFrom-Json -AsHashtable
        $caseValue[[string] $definition[0]] = [string] $definition[1]
        $invalidReadbackCases["receipt $($definition[0])"] = $caseValue | ConvertTo-Json -Compress
    }
    $readbackTokenMismatch = $validReadbackJson | ConvertFrom-Json -AsHashtable
    $readbackTokenMismatch.TokenMatch = $false
    $invalidReadbackCases['token mismatch'] = $readbackTokenMismatch | ConvertTo-Json -Compress
    $readbackPidMismatch = $validReadbackJson | ConvertFrom-Json -AsHashtable
    $readbackPidMismatch.RuntimeProcessId = 4243
    $invalidReadbackCases['success receipt PID mismatch'] = $readbackPidMismatch | ConvertTo-Json -Compress
    $readbackProcessMismatch = $validReadbackJson | ConvertFrom-Json -AsHashtable
    $readbackProcessMismatch.ProcessIdMatch = $false
    $invalidReadbackCases['process identity mismatch'] = $readbackProcessMismatch | ConvertTo-Json -Compress
    $readbackSessionMismatch = $validReadbackJson | ConvertFrom-Json -AsHashtable
    $readbackSessionMismatch.AcceptanceSessionId = 8
    $invalidReadbackCases['receipt session mismatch'] = $readbackSessionMismatch | ConvertTo-Json -Compress
    $readbackSessionIdentityMismatch = $validReadbackJson | ConvertFrom-Json -AsHashtable
    $readbackSessionIdentityMismatch.SessionIdMatch = $false
    $invalidReadbackCases['process session mismatch'] = $readbackSessionIdentityMismatch | ConvertTo-Json -Compress
    foreach ($field in @('ApplicationPathMatch', 'ProcessExecutablePathMatch')) {
        $caseValue = $validReadbackJson | ConvertFrom-Json -AsHashtable
        $caseValue[$field] = $false
        $invalidReadbackCases["path $field"] = $caseValue | ConvertTo-Json -Compress
    }
    $readbackExecutableHash = $validReadbackJson | ConvertFrom-Json -AsHashtable
    $readbackExecutableHash.ExecutableSha256 = 'F' * 64
    $invalidReadbackCases['executable release hash'] = $readbackExecutableHash | ConvertTo-Json -Compress
    $readbackExecutableMatch = $validReadbackJson | ConvertFrom-Json -AsHashtable
    $readbackExecutableMatch.ExecutableHashMatch = $false
    $invalidReadbackCases['executable hash match'] = $readbackExecutableMatch | ConvertTo-Json -Compress
    $readbackSettingsHash = $validReadbackJson | ConvertFrom-Json -AsHashtable
    $readbackSettingsHash.SettingsSha256 = 'F' * 64
    $invalidReadbackCases['settings success receipt hash'] = $readbackSettingsHash | ConvertTo-Json -Compress
    $readbackSettingsMatch = $validReadbackJson | ConvertFrom-Json -AsHashtable
    $readbackSettingsMatch.SettingsHashMatch = $false
    $invalidReadbackCases['settings hash match'] = $readbackSettingsMatch | ConvertTo-Json -Compress
    foreach ($case in $invalidReadbackCases.GetEnumerator()) {
        $rejected = $false
        try {
            ConvertFrom-CountdownRemoteDeploymentReadbackJson `
                -Json ([string] $case.Value) `
                -ExpectedProcessId 4242 `
                -ExpectedExecutableSha256 $expectedReadbackExecutableHash `
                -ExpectedTokenSha256 ('E' * 64) `
                -ExpectedSettingsSha256 $expectedReadbackSettingsHash | Out-Null
        }
        catch {
            $rejected = $true
        }
        Assert-Equal $true $rejected "Independent readback parser rejects $($case.Key)"
    }

    $readbackEvidencePath = Join-Path $remoteDeploymentTestRoot 'post-deployment-independent-readback.json'
    $persistedReadback = Write-CountdownRemoteDeploymentReadbackReceipt `
        -Path $readbackEvidencePath `
        -Bytes ([System.Text.UTF8Encoding]::new($false).GetBytes($validReadbackJson)) `
        -ExpectedProcessId 4242 `
        -ExpectedExecutableSha256 $expectedReadbackExecutableHash `
        -ExpectedTokenSha256 ('E' * 64) `
        -ExpectedSettingsSha256 $expectedReadbackSettingsHash
    $persistedReadbackBytes = [System.IO.File]::ReadAllBytes($readbackEvidencePath)
    $persistedReadbackText = [System.Text.UTF8Encoding]::new($false, $true).GetString($persistedReadbackBytes)
    Assert-Equal $true (Test-Path -LiteralPath $readbackEvidencePath -PathType Leaf) 'Independent readback writer atomically publishes one evidence file'
    Assert-Equal $false ($persistedReadbackText -match 'LaunchToken|sensitive-launch-token-sentinel') 'Independent readback evidence never persists the raw token'
    Assert-Equal 35 @($persistedReadback.PSObject.Properties).Count 'Independent readback writer returns the normalized exact schema'
    $readbackEvidenceHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $readbackEvidencePath).Hash
    $existingReadbackRejected = $false
    try {
        Write-CountdownRemoteDeploymentReadbackReceipt `
            -Path $readbackEvidencePath `
            -Bytes ([System.Text.UTF8Encoding]::new($false).GetBytes($validReadbackJson)) `
            -ExpectedProcessId 4242 `
            -ExpectedExecutableSha256 $expectedReadbackExecutableHash `
            -ExpectedTokenSha256 ('E' * 64) `
            -ExpectedSettingsSha256 $expectedReadbackSettingsHash | Out-Null
    }
    catch {
        $existingReadbackRejected = $_.Exception.Message -match '部署已提交但独立验证不完整.*未回滚'
    }
    Assert-Equal $true $existingReadbackRejected 'Independent readback publication failure reports committed deployment without a rollback claim'
    Assert-Equal $readbackEvidenceHash (Get-FileHash -Algorithm SHA256 -LiteralPath $readbackEvidencePath).Hash 'Failed readback publication preserves existing evidence bytes'
    Assert-Equal 0 @(Get-ChildItem -LiteralPath $remoteDeploymentTestRoot -File -Filter 'post-deployment-independent-readback.json.*.tmp').Count 'Independent readback writer leaves no temporary file after publication failure'

    $beginFailureFixture = New-CountdownRemoteDeploymentFixtureAdapter -Mode BeginFailure
    $beginFailureReceiptPath = Join-Path $remoteDeploymentTestRoot 'deployment-failure.json'
    $beginFailureMessage = $null
    try {
        Invoke-CountdownRemoteDeployment `
            -SourceRoot $PSScriptRoot `
            -InstallPath $remoteDeploymentInstall `
            -PowerShell7Path $remoteDeploymentPowerShell `
            -DeferredReceiptPath $remoteDeploymentReceipt `
            -TaskName 'Minmin Countdown Fixture' `
            -LegacyTaskName '敏敏子倒计时 Fixture' `
            -Adapter $beginFailureFixture.Adapter | Out-Null
    }
    catch {
        $beginFailureMessage = $_.Exception.Message
    }
    Assert-Equal $true ($beginFailureMessage -match '远端部署失败，已回滚' -and $beginFailureMessage -match '阶段=开始候选安装' -and $beginFailureMessage -match '异常类型=System.IO.IOException' -and $beginFailureMessage -match '错误位置=' -and $beginFailureMessage -match 'ScriptStackTrace=' -and $beginFailureMessage -match '模拟候选安装异常') 'Remote deployment preserves stage, exception type, position and script stack for a pre-commit failure'
    Assert-Equal $true ([bool] $beginFailureFixture.State.RolledBack) 'Begin failure still rolls back the candidate'
    Assert-Equal $true ([bool] $beginFailureFixture.State.Disposed) 'Begin failure still releases its host adapter'
    Assert-Equal $true ([bool] $beginFailureFixture.State.DiagnosticsPresentBeforeRollback) 'Remote deployment captures candidate diagnostics before rollback begins'
    $preRollbackDiagnosticsPath = Join-Path $remoteDeploymentTestRoot 'pre-rollback-diagnostics.json'
    Assert-Equal $true (Test-Path -LiteralPath $preRollbackDiagnosticsPath -PathType Leaf) 'Pre-rollback diagnostics use the fixed sidecar name beside the deferred receipt'
    if (Test-Path -LiteralPath $preRollbackDiagnosticsPath -PathType Leaf) {
        $preRollbackDiagnosticsText = [System.IO.File]::ReadAllText($preRollbackDiagnosticsPath, [System.Text.Encoding]::UTF8)
        $preRollbackDiagnosticsDocument = $preRollbackDiagnosticsText | ConvertFrom-Json
        Assert-Equal 1 ([int] $preRollbackDiagnosticsDocument.schemaVersion) 'Pre-rollback diagnostics use schema v1'
        Assert-Equal $true ($preRollbackDiagnosticsText -notmatch 'sensitive-launch-token-sentinel|sensitive-task-argument-sentinel|sensitive-settings-sentinel') 'Pre-rollback diagnostics omit tokens, task XML and settings contents'
    }
    $preRollbackParserCommand = Get-Command ConvertFrom-CountdownPreRollbackDiagnosticsBytes -Module Countdown.RemoteDeployment -ErrorAction SilentlyContinue
    Assert-Equal $true ($null -ne $preRollbackParserCommand) 'Remote deployment exports the strict pre-rollback diagnostics parser'
    if ($null -ne $preRollbackParserCommand -and (Test-Path -LiteralPath $preRollbackDiagnosticsPath -PathType Leaf)) {
        $preRollbackDiagnosticsBytes = [System.IO.File]::ReadAllBytes($preRollbackDiagnosticsPath)
        $validatedPreRollbackDiagnostics = ConvertFrom-CountdownPreRollbackDiagnosticsBytes -Bytes $preRollbackDiagnosticsBytes
        Assert-Equal 1 ([int] $validatedPreRollbackDiagnostics.schemaVersion) 'Pre-rollback diagnostics parser accepts writer output'

        $unexpectedPreRollbackDocument = $preRollbackDiagnosticsText | ConvertFrom-Json -AsHashtable
        $unexpectedPreRollbackDocument['unexpected'] = 'reject-me'
        $unexpectedPreRollbackRejected = $false
        try {
            ConvertFrom-CountdownPreRollbackDiagnosticsBytes -Bytes ([System.Text.UTF8Encoding]::new($false).GetBytes(($unexpectedPreRollbackDocument | ConvertTo-Json -Depth 12 -Compress))) | Out-Null
        }
        catch {
            $unexpectedPreRollbackRejected = $_.Exception.Message -match '字段数量无效|未允许字段'
        }
        Assert-Equal $true $unexpectedPreRollbackRejected 'Pre-rollback diagnostics parser rejects additional fields'

        $nestedUnexpectedPreRollbackDocument = $preRollbackDiagnosticsText | ConvertFrom-Json -AsHashtable
        $nestedUnexpectedPreRollbackDocument.receipts.runtime.core['unexpected'] = 'reject-me'
        $nestedUnexpectedPreRollbackRejected = $false
        try {
            ConvertFrom-CountdownPreRollbackDiagnosticsBytes -Bytes ([System.Text.UTF8Encoding]::new($false).GetBytes(($nestedUnexpectedPreRollbackDocument | ConvertTo-Json -Depth 12 -Compress))) | Out-Null
        }
        catch { $nestedUnexpectedPreRollbackRejected = $_.Exception.Message -match '字段数量无效|未允许字段' }
        Assert-Equal $true $nestedUnexpectedPreRollbackRejected 'Pre-rollback diagnostics parser rejects nested additional fields'

        $invalidTypePreRollbackDocument = $preRollbackDiagnosticsText | ConvertFrom-Json -AsHashtable
        $invalidTypePreRollbackDocument.receipts.runtime.core.processId = '4242'
        $invalidTypePreRollbackRejected = $false
        try {
            ConvertFrom-CountdownPreRollbackDiagnosticsBytes -Bytes ([System.Text.UTF8Encoding]::new($false).GetBytes(($invalidTypePreRollbackDocument | ConvertTo-Json -Depth 12 -Compress))) | Out-Null
        }
        catch { $invalidTypePreRollbackRejected = $_.Exception.Message -match 'processId.*类型无效' }
        Assert-Equal $true $invalidTypePreRollbackRejected 'Pre-rollback diagnostics parser rejects invalid nested field types'

        $invalidTimestampPreRollbackDocument = $preRollbackDiagnosticsText | ConvertFrom-Json -AsHashtable
        $invalidTimestampPreRollbackDocument.captureAtUtc = 'not-a-time'
        $invalidTimestampPreRollbackRejected = $false
        try {
            ConvertFrom-CountdownPreRollbackDiagnosticsBytes -Bytes ([System.Text.UTF8Encoding]::new($false).GetBytes(($invalidTimestampPreRollbackDocument | ConvertTo-Json -Depth 12 -Compress))) | Out-Null
        }
        catch { $invalidTimestampPreRollbackRejected = $_.Exception.Message -match 'captureAtUtc.*时间戳无效' }
        Assert-Equal $true $invalidTimestampPreRollbackRejected 'Pre-rollback diagnostics parser rejects invalid timestamps'

        $missingIntegrityPreRollbackDocument = $preRollbackDiagnosticsText | ConvertFrom-Json -AsHashtable
        $missingIntegrityPreRollbackDocument.receipts.runtime.status = 'readable'
        $missingIntegrityPreRollbackDocument.receipts.runtime.length = $null
        $missingIntegrityPreRollbackDocument.receipts.runtime.lastWriteTimeUtc = $null
        $missingIntegrityPreRollbackDocument.receipts.runtime.sha256 = $null
        $missingIntegrityPreRollbackRejected = $false
        try {
            ConvertFrom-CountdownPreRollbackDiagnosticsBytes -Bytes ([System.Text.UTF8Encoding]::new($false).GetBytes(($missingIntegrityPreRollbackDocument | ConvertTo-Json -Depth 12 -Compress))) | Out-Null
        }
        catch { $missingIntegrityPreRollbackRejected = $_.Exception.Message -match 'readable 状态缺少完整性元数据' }
        Assert-Equal $true $missingIntegrityPreRollbackRejected 'Pre-rollback diagnostics parser rejects readable files without integrity metadata'

        $duplicateLogsPreRollbackDocument = $preRollbackDiagnosticsText | ConvertFrom-Json -AsHashtable
        $duplicateLogsPreRollbackDocument.logs = @(
            $duplicateLogsPreRollbackDocument.logs[0],
            $duplicateLogsPreRollbackDocument.logs[0],
            $duplicateLogsPreRollbackDocument.logs[0],
            $duplicateLogsPreRollbackDocument.logs[0])
        $duplicateLogsPreRollbackRejected = $false
        try {
            ConvertFrom-CountdownPreRollbackDiagnosticsBytes -Bytes ([System.Text.UTF8Encoding]::new($false).GetBytes(($duplicateLogsPreRollbackDocument | ConvertTo-Json -Depth 12 -Compress))) | Out-Null
        }
        catch { $duplicateLogsPreRollbackRejected = $_.Exception.Message -match '重复名称|固定日志名称' }
        Assert-Equal $true $duplicateLogsPreRollbackRejected 'Pre-rollback diagnostics parser rejects duplicate log names'

        $invalidTempStatusPreRollbackDocument = $preRollbackDiagnosticsText | ConvertFrom-Json -AsHashtable
        $invalidTempStatusPreRollbackDocument.acceptanceTempFiles.files = @([ordered]@{
                name = "acceptance-status.json.$('a' * 32).tmp"
                status = 'unexpected'
                length = 0
                lastWriteTimeUtc = $invalidTempStatusPreRollbackDocument.captureAtUtc
                sha256 = '0' * 64
                error = $null
            })
        $invalidTempStatusPreRollbackRejected = $false
        try {
            ConvertFrom-CountdownPreRollbackDiagnosticsBytes -Bytes ([System.Text.UTF8Encoding]::new($false).GetBytes(($invalidTempStatusPreRollbackDocument | ConvertTo-Json -Depth 12 -Compress))) | Out-Null
        }
        catch { $invalidTempStatusPreRollbackRejected = $_.Exception.Message -match 'acceptanceTempFiles.*status 无效' }
        Assert-Equal $true $invalidTempStatusPreRollbackRejected 'Pre-rollback diagnostics parser rejects invalid acceptance temporary file status'

        $invalidTempNamePreRollbackDocument = $preRollbackDiagnosticsText | ConvertFrom-Json -AsHashtable
        $invalidTempNamePreRollbackDocument.acceptanceTempFiles.files = @([ordered]@{
                name = 'acceptance-status.json.sensitive-launch-token-sentinel.tmp'
                status = 'readable'
                length = 0
                lastWriteTimeUtc = $invalidTempNamePreRollbackDocument.captureAtUtc
                sha256 = '0' * 64
                error = $null
            })
        $invalidTempNamePreRollbackRejected = $false
        try {
            ConvertFrom-CountdownPreRollbackDiagnosticsBytes -Bytes ([System.Text.UTF8Encoding]::new($false).GetBytes(($invalidTempNamePreRollbackDocument | ConvertTo-Json -Depth 12 -Compress))) | Out-Null
        }
        catch { $invalidTempNamePreRollbackRejected = $_.Exception.Message -match 'acceptanceTempFiles.*name 无效' }
        Assert-Equal $true $invalidTempNamePreRollbackRejected 'Pre-rollback diagnostics parser rejects unsafe acceptance temporary file names'

        $invalidTempErrorPreRollbackDocument = $preRollbackDiagnosticsText | ConvertFrom-Json -AsHashtable
        $invalidTempErrorPreRollbackDocument.acceptanceTempFiles.error = 42
        $invalidTempErrorPreRollbackRejected = $false
        try {
            ConvertFrom-CountdownPreRollbackDiagnosticsBytes -Bytes ([System.Text.UTF8Encoding]::new($false).GetBytes(($invalidTempErrorPreRollbackDocument | ConvertTo-Json -Depth 12 -Compress))) | Out-Null
        }
        catch { $invalidTempErrorPreRollbackRejected = $_.Exception.Message -match 'acceptanceTempFiles\.error 类型无效' }
        Assert-Equal $true $invalidTempErrorPreRollbackRejected 'Pre-rollback diagnostics parser rejects invalid acceptance temporary file error types'

        $missingTempErrorPreRollbackDocument = $preRollbackDiagnosticsText | ConvertFrom-Json -AsHashtable
        $missingTempErrorPreRollbackDocument.acceptanceTempFiles.status = 'io-error'
        $missingTempErrorPreRollbackDocument.acceptanceTempFiles.error = $null
        $missingTempErrorPreRollbackRejected = $false
        try {
            ConvertFrom-CountdownPreRollbackDiagnosticsBytes -Bytes ([System.Text.UTF8Encoding]::new($false).GetBytes(($missingTempErrorPreRollbackDocument | ConvertTo-Json -Depth 12 -Compress))) | Out-Null
        }
        catch { $missingTempErrorPreRollbackRejected = $_.Exception.Message -match 'acceptanceTempFiles\.io-error 缺少错误原因' }
        Assert-Equal $true $missingTempErrorPreRollbackRejected 'Pre-rollback diagnostics parser rejects io-error without an error reason'

        $invalidUtf8PreRollbackRejected = $false
        try { ConvertFrom-CountdownPreRollbackDiagnosticsBytes -Bytes ([byte[]]@(0x7B, 0x22, 0xFF, 0x22, 0x7D)) | Out-Null }
        catch { $invalidUtf8PreRollbackRejected = $_.Exception.Message -match 'UTF-8' }
        Assert-Equal $true $invalidUtf8PreRollbackRejected 'Pre-rollback diagnostics parser rejects invalid UTF-8'

        $bomPreRollbackRejected = $false
        try { ConvertFrom-CountdownPreRollbackDiagnosticsBytes -Bytes ([byte[]]@(0xEF, 0xBB, 0xBF, 0x7B, 0x7D)) | Out-Null }
        catch { $bomPreRollbackRejected = $_.Exception.Message -match 'BOM' }
        Assert-Equal $true $bomPreRollbackRejected 'Pre-rollback diagnostics parser rejects a UTF-8 BOM'

        $oversizedPreRollbackRejected = $false
        try { ConvertFrom-CountdownPreRollbackDiagnosticsBytes -Bytes ([byte[]]::new(24577)) | Out-Null }
        catch { $oversizedPreRollbackRejected = $_.Exception.Message -match '超过允许大小' }
        Assert-Equal $true $oversizedPreRollbackRejected 'Pre-rollback diagnostics parser rejects payloads above 24 KiB'
    }
    Assert-Equal $true (Test-Path -LiteralPath $beginFailureReceiptPath -PathType Leaf) 'Remote deployment writes a controller fallback receipt after a pre-commit failure'
    $beginFailureReceipt = [System.IO.File]::ReadAllText($beginFailureReceiptPath, [System.Text.Encoding]::UTF8) | ConvertFrom-Json
    Assert-Equal '开始候选安装' ([string] $beginFailureReceipt.stage) 'Controller fallback receipt retains the active deployment stage'
    Assert-Equal '模拟候选安装异常。' ([string] $beginFailureReceipt.exceptionMessage) 'Controller fallback receipt retains the original exception message'
    Assert-Equal $true (-not [bool] $beginFailureReceipt.transactionStarted -and [string] $beginFailureReceipt.commitState -eq 'not-committed' -and [bool] $beginFailureReceipt.rollbackAttempted -and [bool] $beginFailureReceipt.rollbackSucceeded) 'Controller fallback receipt retains the uncommitted rollback outcome'

    $blockedDiagnosticsParent = Join-Path $remoteDeploymentTestRoot 'blocked-diagnostics-parent'
    [System.IO.File]::WriteAllText($blockedDiagnosticsParent, 'not-a-directory', [System.Text.UTF8Encoding]::new($false))
    $captureFailureFixture = New-CountdownRemoteDeploymentFixtureAdapter -Mode BeginFailure
    $captureFailureMessage = $null
    try {
        Invoke-CountdownRemoteDeployment `
            -SourceRoot $PSScriptRoot `
            -InstallPath $remoteDeploymentInstall `
            -PowerShell7Path $remoteDeploymentPowerShell `
            -DeferredReceiptPath (Join-Path $blockedDiagnosticsParent 'deferred-install-receipt.json') `
            -TaskName 'Minmin Countdown Fixture' `
            -LegacyTaskName '敏敏子倒计时 Fixture' `
            -Adapter $captureFailureFixture.Adapter | Out-Null
    }
    catch {
        $captureFailureMessage = $_.Exception.Message
    }
    Assert-Equal 1 ([int] $captureFailureFixture.State.RollbackCount) 'Pre-rollback diagnostics capture failure still invokes rollback exactly once'
    Assert-Equal $true ($captureFailureMessage -match '模拟候选安装异常.*rollback 前诊断捕获失败') 'Pre-rollback diagnostics capture failure preserves the deployment failure and adds a capture summary'

    $commitProbeReceipt = Join-Path $remoteDeploymentTestRoot 'commit-probe\deferred-install-receipt.json'
    New-Item -ItemType Directory -Path (Split-Path -Parent $commitProbeReceipt) -Force | Out-Null
    $commitProbeFailureFixture = New-CountdownRemoteDeploymentFixtureAdapter -Mode CommitProbeFailure
    $commitProbeFailureMessage = $null
    try {
        Invoke-CountdownRemoteDeployment `
            -SourceRoot $PSScriptRoot `
            -InstallPath $remoteDeploymentInstall `
            -PowerShell7Path $remoteDeploymentPowerShell `
            -DeferredReceiptPath $commitProbeReceipt `
            -TaskName 'Minmin Countdown Fixture' `
            -LegacyTaskName '敏敏子倒计时 Fixture' `
            -Adapter $commitProbeFailureFixture.Adapter | Out-Null
    }
    catch {
        $commitProbeFailureMessage = $_.Exception.Message
    }
    $commitProbeFailureReceiptPath = Join-Path (Split-Path -Parent $commitProbeReceipt) 'deployment-failure.json'
    Assert-Equal $true ($commitProbeFailureMessage -match '提交状态无法验证.*未回滚') 'Remote deployment fails closed when commit state cannot be probed'
    Assert-Equal 0 ([int] $commitProbeFailureFixture.State.RollbackCount) 'Commit state probe failure does not speculate with a rollback'
    Assert-Equal $true (Test-Path -LiteralPath $commitProbeFailureReceiptPath -PathType Leaf) 'Commit state probe failure writes an auditable receipt'
    $commitProbeFailureReceipt = [System.IO.File]::ReadAllText($commitProbeFailureReceiptPath, [System.Text.Encoding]::UTF8) | ConvertFrom-Json
    Assert-Equal $true ([string] $commitProbeFailureReceipt.commitState -eq 'unknown' -and -not [bool] $commitProbeFailureReceipt.rollbackAttempted -and -not [bool] $commitProbeFailureReceipt.rollbackSucceeded) 'Commit state probe receipt records the unknown boundary without a rollback claim'

    $unresolvedReceipt = Join-Path $remoteDeploymentTestRoot 'unresolved-committed\deferred-install-receipt.json'
    New-Item -ItemType Directory -Path (Split-Path -Parent $unresolvedReceipt) -Force | Out-Null
    $unresolvedCommittedFixture = New-CountdownRemoteDeploymentFixtureAdapter -Mode UnresolvedCommittedTransaction
    $unresolvedCommittedMessage = $null
    try {
        Invoke-CountdownRemoteDeployment `
            -SourceRoot $PSScriptRoot `
            -InstallPath $remoteDeploymentInstall `
            -PowerShell7Path $remoteDeploymentPowerShell `
            -DeferredReceiptPath $unresolvedReceipt `
            -TaskName 'Minmin Countdown Fixture' `
            -LegacyTaskName '敏敏子倒计时 Fixture' `
            -Adapter $unresolvedCommittedFixture.Adapter | Out-Null
    }
    catch {
        $unresolvedCommittedMessage = $_.Exception.Message
    }
    $unresolvedCommittedReceiptPath = Join-Path (Split-Path -Parent $unresolvedReceipt) 'deployment-failure.json'
    Assert-Equal $true ($unresolvedCommittedMessage -match '已提交但未完成解锁确认.*未声称回滚') 'Remote deployment does not falsely claim rollback for an unresolved committed transaction'
    Assert-Equal 1 ([int] $unresolvedCommittedFixture.State.RollbackCount) 'Unresolved committed transaction records the attempted rollback boundary'
    $unresolvedCommittedReceipt = [System.IO.File]::ReadAllText($unresolvedCommittedReceiptPath, [System.Text.Encoding]::UTF8) | ConvertFrom-Json
    Assert-Equal $true ([string] $unresolvedCommittedReceipt.commitState -eq 'committed' -and [bool] $unresolvedCommittedReceipt.rollbackAttempted -and -not [bool] $unresolvedCommittedReceipt.rollbackSucceeded) 'Unresolved committed receipt preserves the no-rollback-success fact'

    $candidateFailureFixture = New-CountdownRemoteDeploymentFixtureAdapter -Mode CandidateFailure
    $candidateFailureMessage = $null
    try {
        Invoke-CountdownRemoteDeployment `
            -SourceRoot $PSScriptRoot `
            -InstallPath $remoteDeploymentInstall `
            -PowerShell7Path $remoteDeploymentPowerShell `
            -DeferredReceiptPath $remoteDeploymentReceipt `
            -TaskName 'Minmin Countdown Fixture' `
            -LegacyTaskName '敏敏子倒计时 Fixture' `
            -Adapter $candidateFailureFixture.Adapter | Out-Null
    }
    catch {
        $candidateFailureMessage = $_.Exception.Message
    }
    Assert-Equal $true ($candidateFailureMessage -match '候选运行态未达到 window-ready') 'Remote deployment rejects a candidate that never reached window-ready'
    Assert-Equal $false ([bool] $candidateFailureFixture.State.Accepted) 'Candidate contract failure does not enter acceptance'
    Assert-Equal $true ([bool] $candidateFailureFixture.State.RolledBack) 'Candidate contract failure rolls back the candidate'
    Assert-Equal $true ([bool] $candidateFailureFixture.State.Disposed) 'Candidate contract failure releases its host adapter'

    $richDiagnosticsDirectory = Join-Path $remoteDeploymentTestRoot 'rich-diagnostics'
    New-Item -ItemType Directory -Path $richDiagnosticsDirectory -Force | Out-Null
    $richDiagnosticsReceipt = Join-Path $richDiagnosticsDirectory 'deferred-install-receipt.json'
    $richDiagnosticsToken = 'rich-diagnostics-token'
    $richDiagnosticsSessionId = [System.Diagnostics.Process]::GetCurrentProcess().SessionId
    $richDiagnosticsApplicationPath = Join-Path $remoteDeploymentInstall '敏敏子倒计时.ps1'
    $richRequestedAtUtc = [DateTimeOffset]::Parse('2036-01-02T03:04:05.0000000+00:00')
    $richReleasedAtUtc = $richRequestedAtUtc.AddSeconds(1)
    $richReadyAtUtc = $richRequestedAtUtc.AddSeconds(2)
    $richFinalizedAtUtc = $richRequestedAtUtc.AddSeconds(3)
    $richDiagnosticsStatus = [ordered]@{
        schemaVersion = 4
        stage = 'window-ready'
        launchToken = $richDiagnosticsToken
        processId = $PID
        sessionId = $richDiagnosticsSessionId
        applicationPath = $richDiagnosticsApplicationPath
        readyAtUtc = $richReadyAtUtc.ToString('o')
    }
    [System.IO.File]::WriteAllText((Join-Path $remoteDeploymentInstall 'startup-request.json'), (@{
                schemaVersion = 1
                launchToken = $richDiagnosticsToken
                requestedAtUtc = $richRequestedAtUtc.ToString('o')
                releasedAtUtc = $richReleasedAtUtc.ToString('o')
                readyAtUtc = $richReadyAtUtc.ToString('o')
                finalizedAtUtc = $richFinalizedAtUtc.ToString('o')
                acceptanceState = 'finalized'
                acceptanceReadOnly = $false
            } | ConvertTo-Json -Compress), [System.Text.UTF8Encoding]::new($false))
    [System.IO.File]::WriteAllText((Join-Path $remoteDeploymentInstall 'runtime-status.json'), ($richDiagnosticsStatus | ConvertTo-Json -Compress), [System.Text.UTF8Encoding]::new($false))
    $richDiagnosticsStatus.schemaVersion = 1
    $richDiagnosticsStatus.stage = 'finalized-ready'
    $richDiagnosticsStatus.readyAtUtc = $richFinalizedAtUtc.ToString('o')
    [System.IO.File]::WriteAllText((Join-Path $remoteDeploymentInstall 'acceptance-status.json'), ($richDiagnosticsStatus | ConvertTo-Json -Compress), [System.Text.UTF8Encoding]::new($false))
    [System.IO.File]::WriteAllText((Join-Path $remoteDeploymentInstall "acceptance-status.json.$([guid]::NewGuid().ToString('N')).tmp"), '{}', [System.Text.UTF8Encoding]::new($false))
    $richDiagnosticsLogDirectory = Join-Path $remoteDeploymentInstall 'logs'
    New-Item -ItemType Directory -Path $richDiagnosticsLogDirectory -Force | Out-Null
    $richDiagnosticsLog = @(
        @{ schemaVersion = 1; timestampUtc = [DateTimeOffset]::UtcNow.AddSeconds(-1).ToString('o'); level = 'Info'; event = 'AcceptanceMigrationReady'; processId = $PID },
        @{ schemaVersion = 1; timestampUtc = [DateTimeOffset]::UtcNow.ToString('o'); level = 'Info'; event = 'UnrelatedNewestEvent'; processId = 1 }
    ) | ForEach-Object { $_ | ConvertTo-Json -Compress }
    [System.IO.File]::WriteAllText((Join-Path $richDiagnosticsLogDirectory 'application.jsonl'), ($richDiagnosticsLog -join [Environment]::NewLine), [System.Text.UTF8Encoding]::new($false))

    $acceptanceFailureFixture = New-CountdownRemoteDeploymentFixtureAdapter -Mode AcceptanceFailure
    $acceptanceFailureFixture.State.LaunchToken = $richDiagnosticsToken
    $acceptanceFailureFixture.State.ExpectedProcessId = $PID
    $acceptanceFailureFixture.State.InteractiveContext.SessionId = $richDiagnosticsSessionId
    $acceptanceFailureMessage = $null
    try {
        Invoke-CountdownRemoteDeployment `
            -SourceRoot $remoteDeploymentSource `
            -InstallPath $remoteDeploymentInstall `
            -PowerShell7Path $remoteDeploymentPowerShell `
            -DeferredReceiptPath $richDiagnosticsReceipt `
            -TaskName 'Minmin Countdown Fixture' `
            -LegacyTaskName '敏敏子倒计时 Fixture' `
            -Adapter $acceptanceFailureFixture.Adapter | Out-Null
    }
    catch {
        $acceptanceFailureMessage = $_.Exception.Message
    }
    Assert-Equal $true ($acceptanceFailureMessage -match '模拟验收失败') 'Remote deployment surfaces acceptance failure'
    Assert-Equal $true ([bool] $acceptanceFailureFixture.State.RolledBack) 'Acceptance failure rolls back the candidate'
    Assert-Equal $true ([bool] $acceptanceFailureFixture.State.Disposed) 'Acceptance failure releases its host adapter'
    $richDiagnostics = ConvertFrom-CountdownPreRollbackDiagnosticsBytes -Bytes ([System.IO.File]::ReadAllBytes((Join-Path $richDiagnosticsDirectory 'pre-rollback-diagnostics.json')))
    Assert-Equal 'readable' ([string] $richDiagnostics.receipts.runtime.status) 'Pre-rollback diagnostics records readable runtime metadata'
    Assert-Equal $true ([bool] $richDiagnostics.receipts.runtime.match.token) 'Pre-rollback diagnostics matches the runtime token without persisting it'
    Assert-Equal $true ([bool] $richDiagnostics.receipts.runtime.match.processId) 'Pre-rollback diagnostics matches the runtime PID'
    Assert-Equal $true ([bool] $richDiagnostics.receipts.runtime.match.sessionId) 'Pre-rollback diagnostics matches the runtime session'
    Assert-Equal $true ([bool] $richDiagnostics.receipts.runtime.match.applicationPath) 'Pre-rollback diagnostics matches the runtime application path'
    Assert-Equal $true ([string] $richDiagnostics.receipts.acceptance.status -eq 'readable' -and [bool] $richDiagnostics.receipts.acceptance.match.token -and [bool] $richDiagnostics.receipts.acceptance.match.processId) 'Pre-rollback diagnostics records the candidate acceptance receipt before rollback'
    Assert-Equal $richRequestedAtUtc.ToString('o') ([DateTimeOffset] $richDiagnostics.receipts.startup.core.requestedAtUtc).ToUniversalTime().ToString('o') 'Pre-rollback diagnostics records the startup request time'
    Assert-Equal $richReleasedAtUtc.ToString('o') ([DateTimeOffset] $richDiagnostics.receipts.startup.core.releasedAtUtc).ToUniversalTime().ToString('o') 'Pre-rollback diagnostics records the startup release time'
    Assert-Equal $richReadyAtUtc.ToString('o') ([DateTimeOffset] $richDiagnostics.receipts.startup.core.readyAtUtc).ToUniversalTime().ToString('o') 'Pre-rollback diagnostics records the startup ready time'
    Assert-Equal $richFinalizedAtUtc.ToString('o') ([DateTimeOffset] $richDiagnostics.receipts.startup.core.finalizedAtUtc).ToUniversalTime().ToString('o') 'Pre-rollback diagnostics records the startup finalized time'
    Assert-Equal $richReadyAtUtc.ToString('o') ([DateTimeOffset] $richDiagnostics.receipts.runtime.core.readyAtUtc).ToUniversalTime().ToString('o') 'Pre-rollback diagnostics records the window-ready receipt time'
    Assert-Equal $richFinalizedAtUtc.ToString('o') ([DateTimeOffset] $richDiagnostics.receipts.acceptance.core.readyAtUtc).ToUniversalTime().ToString('o') 'Pre-rollback diagnostics records the finalized-ready receipt time'
    Assert-Equal $true ([string] $richDiagnostics.process.status -eq 'running' -and [bool] $richDiagnostics.process.sessionMatch) 'Pre-rollback diagnostics records that the expected candidate process is still running in the expected session'
    Assert-Equal 1 @($richDiagnostics.acceptanceTempFiles.files).Count 'Pre-rollback diagnostics enumerates atomic acceptance temporary files'
    Assert-Equal $true (@($richDiagnostics.logs[0].tail.event) -contains 'AcceptanceMigrationReady') 'Pre-rollback diagnostics prioritizes the expected PID acceptance event in the bounded log tail'

    $commitThenFailFixture = New-CountdownRemoteDeploymentFixtureAdapter -Mode CommitThenFail
    $commitThenFailMessage = $null
    try {
        Invoke-CountdownRemoteDeployment `
            -SourceRoot $remoteDeploymentSource `
            -InstallPath $remoteDeploymentInstall `
            -PowerShell7Path $remoteDeploymentPowerShell `
            -DeferredReceiptPath $remoteDeploymentReceipt `
            -TaskName 'Minmin Countdown Fixture' `
            -LegacyTaskName '敏敏子倒计时 Fixture' `
            -Adapter $commitThenFailFixture.Adapter | Out-Null
    }
    catch {
        $commitThenFailMessage = $_.Exception.Message
    }
    Assert-Equal $true ($commitThenFailMessage -match '部署已提交.*未回滚.*模拟提交后验收失败') 'Remote deployment reports an acceptance failure after the explicit commit boundary without claiming rollback'
    Assert-Equal 0 ([int] $commitThenFailFixture.State.RollbackCount) 'Remote deployment never invokes rollback after the adapter crosses its explicit commit boundary'
    Assert-Equal $true ([bool] $commitThenFailFixture.State.Disposed) 'Committed acceptance failure still releases its host adapter'

    $acceptedFailureCases = @(
        [pscustomobject]@{ Mode = 'AcceptedFinalizationFailure'; Failure = '模拟 accepted 后 finalized 写入失败' },
        [pscustomobject]@{ Mode = 'AcceptedReceiptFailure'; Failure = '模拟 accepted 后 finalized-ready 回执失败' },
        [pscustomobject]@{ Mode = 'AcceptedEnvironmentFailure'; Failure = '模拟 accepted 后环境复验失败' }
    )
    foreach ($acceptedFailureCase in $acceptedFailureCases) {
        $acceptedFailureFixture = New-CountdownRemoteDeploymentWindowsFixtureAdapter -Mode $acceptedFailureCase.Mode
        $acceptedFailureMessage = $null
        try {
            Invoke-CountdownRemoteDeployment `
                -SourceRoot $remoteDeploymentSource `
                -InstallPath $remoteDeploymentInstall `
                -PowerShell7Path $remoteDeploymentPowerShell `
                -DeferredReceiptPath $remoteDeploymentReceipt `
                -TaskName $acceptedFailureCase.Mode `
                -LegacyTaskName '敏敏子倒计时 Fixture' `
                -Adapter $acceptedFailureFixture.Adapter | Out-Null
        }
        catch {
            $acceptedFailureMessage = $_.Exception.Message
        }
        Assert-Equal $true (Test-Path -LiteralPath (Join-Path $acceptedFailureFixture.TransactionPath 'accepted.marker') -PathType Leaf) "Remote deployment fixture reaches accepted before $($acceptedFailureCase.Mode)"
        Assert-Equal $true ($acceptedFailureMessage -match "已回滚.*$([regex]::Escape($acceptedFailureCase.Failure))") "Remote deployment rolls back $($acceptedFailureCase.Mode)"
        Assert-Equal 1 ([int] $acceptedFailureFixture.State.RollbackCount) "Remote deployment invokes rollback exactly once for $($acceptedFailureCase.Mode)"
        Assert-Equal $false ([bool] $acceptedFailureFixture.State.Committed) "Accepted is not the committed boundary for $($acceptedFailureCase.Mode)"
    }

    $commitPreparationFailureFixture = New-CountdownRemoteDeploymentWindowsFixtureAdapter -Mode CommitPreparationFailure
    $commitPreparationFailureMessage = $null
    try {
        Invoke-CountdownRemoteDeployment `
            -SourceRoot $remoteDeploymentSource `
            -InstallPath $remoteDeploymentInstall `
            -PowerShell7Path $remoteDeploymentPowerShell `
            -DeferredReceiptPath $remoteDeploymentReceipt `
            -TaskName 'CommitPreparationFailure' `
            -LegacyTaskName '敏敏子倒计时 Fixture' `
            -Adapter $commitPreparationFailureFixture.Adapter | Out-Null
    }
    catch {
        $commitPreparationFailureMessage = $_.Exception.Message
    }
    Assert-Equal $true ($commitPreparationFailureMessage -match '已回滚.*模拟 committed 点前的清理准备失败') 'Remote deployment rolls back when committed cleanup fails before crossing the committed point'
    Assert-Equal 1 ([int] $commitPreparationFailureFixture.State.RollbackCount) 'Remote deployment invokes rollback exactly once before the committed point'
    Assert-Equal $false ([bool] $commitPreparationFailureFixture.State.Committed) 'Remote deployment keeps State.Committed false before the committed point'

    $committedCleanupFailureFixture = New-CountdownRemoteDeploymentWindowsFixtureAdapter -Mode CommittedCleanupFailure
    $committedCleanupFailureMessage = $null
    try {
        Invoke-CountdownRemoteDeployment `
            -SourceRoot $remoteDeploymentSource `
            -InstallPath $remoteDeploymentInstall `
            -PowerShell7Path $remoteDeploymentPowerShell `
            -DeferredReceiptPath $remoteDeploymentReceipt `
            -TaskName 'CommittedCleanupFailure' `
            -LegacyTaskName '敏敏子倒计时 Fixture' `
            -Adapter $committedCleanupFailureFixture.Adapter | Out-Null
    }
    catch {
        $committedCleanupFailureMessage = $_.Exception.Message
    }
    Assert-Equal $true ($committedCleanupFailureMessage -match '部署已提交.*未回滚.*模拟 committed 点后的事务清理失败') 'Remote deployment reports cleanup failure after crossing the committed point without rollback'
    Assert-Equal 0 ([int] $committedCleanupFailureFixture.State.RollbackCount) 'Remote deployment never rolls back when committed cleanup throws after the committed point'
    Assert-Equal $true ([bool] $committedCleanupFailureFixture.State.Committed) 'Remote deployment observes the committed point even when committed cleanup later throws'

    $committedFinalizeFailureFixture = New-CountdownRemoteDeploymentWindowsFixtureAdapter -Mode CommittedFinalizeFailure
    $committedFinalizeFailureMessage = $null
    try {
        Invoke-CountdownRemoteDeployment `
            -SourceRoot $remoteDeploymentSource `
            -InstallPath $remoteDeploymentInstall `
            -PowerShell7Path $remoteDeploymentPowerShell `
            -DeferredReceiptPath $remoteDeploymentReceipt `
            -TaskName 'CommittedFinalizeFailure' `
            -LegacyTaskName '敏敏子倒计时 Fixture' `
            -Adapter $committedFinalizeFailureFixture.Adapter | Out-Null
    }
    catch {
        $committedFinalizeFailureMessage = $_.Exception.Message
    }
    Assert-Equal $true ($committedFinalizeFailureMessage -match '部署已提交.*未回滚.*模拟 committed 后 adapter 收尾失败') 'Remote deployment reports adapter finalization failure after commit without rollback'
    Assert-Equal 0 ([int] $committedFinalizeFailureFixture.State.RollbackCount) 'Remote deployment never rolls back an adapter finalization failure after commit'
    Assert-Equal $true ([bool] $committedFinalizeFailureFixture.State.Committed) 'Remote deployment keeps committed state through adapter finalization failure'

    $rollbackFailureFixture = New-CountdownRemoteDeploymentFixtureAdapter -Mode RollbackFailure
    $rollbackFailureMessage = $null
    try {
        Invoke-CountdownRemoteDeployment `
            -SourceRoot $remoteDeploymentSource `
            -InstallPath $remoteDeploymentInstall `
            -PowerShell7Path $remoteDeploymentPowerShell `
            -DeferredReceiptPath $remoteDeploymentReceipt `
            -TaskName 'Minmin Countdown Fixture' `
            -LegacyTaskName '敏敏子倒计时 Fixture' `
            -Adapter $rollbackFailureFixture.Adapter | Out-Null
    }
    catch {
        $rollbackFailureMessage = $_.Exception.Message
    }
    Assert-Equal $true ($rollbackFailureMessage -match '候选运行态未达到 window-ready.*回滚也失败.*模拟回滚失败') 'Remote deployment preserves both primary and rollback failures'
    Assert-Equal $true ([bool] $rollbackFailureFixture.State.RolledBack) 'Rollback failure records that rollback was attempted'
    Assert-Equal $true ([bool] $rollbackFailureFixture.State.Disposed) 'Rollback failure still releases its host adapter'

    $finalizeFailureFixture = New-CountdownRemoteDeploymentFixtureAdapter -Mode FinalizeFailure
    $finalizeFailureMessage = $null
    try {
        Invoke-CountdownRemoteDeployment `
            -SourceRoot $remoteDeploymentSource `
            -InstallPath $remoteDeploymentInstall `
            -PowerShell7Path $remoteDeploymentPowerShell `
            -DeferredReceiptPath $remoteDeploymentReceipt `
            -TaskName 'Minmin Countdown Fixture' `
            -LegacyTaskName '敏敏子倒计时 Fixture' `
            -Adapter $finalizeFailureFixture.Adapter | Out-Null
    }
    catch {
        $finalizeFailureMessage = $_.Exception.Message
    }
    Assert-Equal $true ($finalizeFailureMessage -match '部署已提交.*未回滚.*模拟提交后收尾失败') 'Remote deployment does not claim rollback after committed cleanup failure'
    Assert-Equal $false ([bool] $finalizeFailureFixture.State.RolledBack) 'Committed cleanup failure does not roll back'
    Assert-Equal $true ([bool] $finalizeFailureFixture.State.Disposed) 'Committed cleanup failure still releases its host adapter'

    $candidateAndDisposeFailureFixture = New-CountdownRemoteDeploymentFixtureAdapter -Mode CandidateAndDisposeFailure
    $candidateAndDisposeFailureMessage = $null
    try {
        Invoke-CountdownRemoteDeployment `
            -SourceRoot $remoteDeploymentSource `
            -InstallPath $remoteDeploymentInstall `
            -PowerShell7Path $remoteDeploymentPowerShell `
            -DeferredReceiptPath $remoteDeploymentReceipt `
            -TaskName 'Minmin Countdown Fixture' `
            -LegacyTaskName '敏敏子倒计时 Fixture' `
            -Adapter $candidateAndDisposeFailureFixture.Adapter | Out-Null
    }
    catch {
        $candidateAndDisposeFailureMessage = $_.Exception.Message
    }
    Assert-Equal $true ($candidateAndDisposeFailureMessage -match '已回滚.*候选运行态未达到 window-ready.*资源释放也失败.*模拟资源释放失败') 'Remote deployment preserves its primary failure when adapter disposal also fails'
    Assert-Equal $true ([bool] $candidateAndDisposeFailureFixture.State.RolledBack) 'Candidate and disposal failure still rolls back the candidate'
    Assert-Equal $true ([bool] $candidateAndDisposeFailureFixture.State.Disposed) 'Candidate and disposal failure attempts adapter disposal'

    $disposeFailureReceipt = Join-Path $remoteDeploymentTestRoot 'dispose-failure\deferred-install-receipt.json'
    New-Item -ItemType Directory -Path (Split-Path -Parent $disposeFailureReceipt) -Force | Out-Null
    $disposeFailureFixture = New-CountdownRemoteDeploymentFixtureAdapter -Mode DisposeFailure
    $disposeFailureMessage = $null
    try {
        Invoke-CountdownRemoteDeployment `
            -SourceRoot $PSScriptRoot `
            -InstallPath $remoteDeploymentInstall `
            -PowerShell7Path $remoteDeploymentPowerShell `
            -DeferredReceiptPath $disposeFailureReceipt `
            -TaskName 'Minmin Countdown Fixture' `
            -LegacyTaskName '敏敏子倒计时 Fixture' `
            -Adapter $disposeFailureFixture.Adapter | Out-Null
    }
    catch {
        $disposeFailureMessage = $_.Exception.Message
    }
    Assert-Equal $true ($disposeFailureMessage -match '部署已提交.*资源释放失败.*未回滚.*模拟资源释放失败') 'Remote deployment reports committed disposal failure without claiming rollback'
    Assert-Equal $false ([bool] $disposeFailureFixture.State.RolledBack) 'Committed disposal failure does not roll back'
    Assert-Equal $true ([bool] $disposeFailureFixture.State.Disposed) 'Committed disposal failure records the disposal attempt'
    $disposeFailureReceiptPath = Join-Path (Split-Path -Parent $disposeFailureReceipt) 'deployment-failure.json'
    Assert-Equal $true (Test-Path -LiteralPath $disposeFailureReceiptPath -PathType Leaf) 'Committed disposal failure writes an auditable receipt'
    $disposeFailureReceiptDocument = [System.IO.File]::ReadAllText($disposeFailureReceiptPath, [System.Text.Encoding]::UTF8) | ConvertFrom-Json
    Assert-Equal $true ([string] $disposeFailureReceiptDocument.commitState -eq 'committed' -and -not [bool] $disposeFailureReceiptDocument.rollbackAttempted -and -not [bool] $disposeFailureReceiptDocument.rollbackSucceeded) 'Committed disposal receipt never claims a rollback'

    $knownTransactionPath = Join-Path $remoteDeploymentTestRoot 'known-transaction-without-journal'
    New-Item -ItemType Directory -Path $knownTransactionPath -Force | Out-Null
    $remoteDeploymentModule = Get-Module -Name Countdown.RemoteDeployment | Select-Object -First 1
    $windowsAdapter = & $remoteDeploymentModule { New-CountdownRemoteDeploymentWindowsAdapter }
    $windowsAdapter.State.TransactionModuleImported = $true
    $windowsAdapter.State.InstallStarted = $true
    $windowsAdapter.State.RollbackTransactionPath = $knownTransactionPath
    $windowsAdapter.State.Layout = & $installLayoutCommand `
        -InstallPath $remoteDeploymentInstall `
        -TaskName 'Minmin Countdown Fixture' `
        -LegacyTaskName '敏敏子倒计时 Fixture' `
        -Desktop (Join-Path $remoteDeploymentTestRoot 'desktop')
    $windowsAdapterContext = [pscustomobject]@{
        SourceRoot = $remoteDeploymentSource
        InstallPath = $remoteDeploymentInstall
        PowerShell7Path = $remoteDeploymentPowerShell
        DeferredReceiptPath = $remoteDeploymentReceipt
        TaskName = 'Minmin Countdown Fixture'
        LegacyTaskName = '敏敏子倒计时 Fixture'
    }
    $missingJournalMessage = $null
    try {
        & $windowsAdapter.Rollback $windowsAdapter.State $windowsAdapterContext $null | Out-Null
    }
    catch {
        $missingJournalMessage = $_.Exception.Message
    }
    Assert-Equal $true ($missingJournalMessage -match '无法验证待回滚事务的 journal；拒绝声称已回滚') 'Known rollback transaction without a journal fails closed'

    $remoteDeploymentControllerSource = [System.IO.File]::ReadAllText((Join-Path $PSScriptRoot 'Countdown.RemoteDeployment.psm1'), [System.Text.Encoding]::UTF8)
    $deploymentOrchestratorSource = [System.IO.File]::ReadAllText((Join-Path $PSScriptRoot 'Deploy-OfficeCountdown.ps1'), [System.Text.Encoding]::UTF8)
    $acceptanceSource = [System.IO.File]::ReadAllText((Join-Path $PSScriptRoot 'Countdown.InstallLifecycle.psm1'), [System.Text.Encoding]::UTF8)
    Assert-Equal 1 ([regex]::Matches($deploymentOrchestratorSource, '(?m)^function Invoke-OfficeRemoteBoundedBase64FileRead\b').Count) 'Deployment orchestrator has one bounded Base64 reader helper'
    Assert-Equal 2 ([regex]::Matches($deploymentOrchestratorSource, '(?m)^function Get-OfficeRemote(?:DeploymentFailureReceipt|PreRollbackDiagnostics)\b').Count) 'Deployment orchestrator keeps both receipt reader entry points'
    Assert-Equal 4 ([regex]::Matches($deploymentOrchestratorSource, '(?m)^Assert-CountdownDeploymentRepositoryAndManifest\b').Count) 'Deployment orchestrator uses the repository and manifest stage helper at all four stages'
    Assert-Equal 1 ([regex]::Matches($acceptanceSource, '(?m)^function Invoke-CountdownAcceptanceEnvironmentAssertion\b').Count) 'Transaction acceptance has one shared environment assertion helper'
    Assert-Equal 3 ([regex]::Matches($acceptanceSource, '(?m)^\s*\[void\] \(Invoke-CountdownAcceptanceEnvironmentAssertion|^\s*\$finalEnvironment = Invoke-CountdownAcceptanceEnvironmentAssertion').Count) 'Transaction acceptance preserves three environment assertion timings'
    $preRollbackCaptureCallCount = [regex]::Matches($remoteDeploymentControllerSource, '(?m)^\s*\$preRollbackCapture = Write-CountdownPreRollbackDiagnostics\b').Count
    $preRollbackCaptureIndex = $remoteDeploymentControllerSource.IndexOf('$preRollbackCapture = Write-CountdownPreRollbackDiagnostics')
    $rollbackInvocationIndex = $remoteDeploymentControllerSource.IndexOf('$rollback = & $Adapter.Rollback', $preRollbackCaptureIndex)
    Assert-Equal 1 $preRollbackCaptureCallCount 'Remote deployment invokes pre-rollback diagnostics capture exactly once'
    Assert-Equal $true ($preRollbackCaptureIndex -ge 0 -and $rollbackInvocationIndex -gt $preRollbackCaptureIndex) 'Remote deployment captures diagnostics before invoking rollback'

    $readbackAdapterCommand = Get-Command `
        New-CountdownRemoteDeploymentReadbackSshAdapter `
        -Module Countdown.RemoteDeployment `
        -ErrorAction SilentlyContinue
    $independentReadbackCommand = Get-Command `
        Invoke-CountdownRemoteDeploymentIndependentReadback `
        -Module Countdown.RemoteDeployment `
        -ErrorAction SilentlyContinue
    Assert-Equal $true ($null -ne $readbackAdapterCommand) 'Remote deployment exports the independent readback SSH adapter interface'
    Assert-Equal $true ($null -ne $independentReadbackCommand) 'Remote deployment exports the independent readback orchestration interface'

    $publicReadbackBindingHash = 'F' * 64
    $publicReadbackBindingJson = [ordered]@{
        SchemaVersion = 1
        DeploymentBindingSha256 = $publicReadbackBindingHash
        TokenSha256 = 'E' * 64
        RuntimeProcessId = 4242
    } | ConvertTo-Json -Compress
    $publicReadbackBindingBytes = [System.Text.UTF8Encoding]::new($false).GetBytes(
        $publicReadbackBindingJson)
    $publicReadbackBytes = [System.Text.UTF8Encoding]::new($false).GetBytes(
        $validReadbackJson)
    $publicReadbackOptions = @(
        '-i', 'D:\fixture\id_ed25519',
        '-o', 'BatchMode=yes',
        '-o', 'IdentitiesOnly=yes',
        '-o', 'ConnectTimeout=10',
        '-o', 'ConnectionAttempts=1',
        '-o', 'StrictHostKeyChecking=yes',
        '-o', 'UserKnownHostsFile=D:\fixture\known_hosts',
        '-o', 'KexAlgorithms=curve25519-sha256'
    )
    $publicReadbackRunnerState = [pscustomobject]@{ Calls = 0; ReadbackRemoteCommand = $null }
    $publicReadbackRunner = {
        param($FileName, $Arguments)
        $publicReadbackRunnerState.Calls++
        $payload = if ($publicReadbackRunnerState.Calls -eq 1) {
            $publicReadbackBindingBytes
        }
        else {
            $publicReadbackRunnerState.ReadbackRemoteCommand = [string] $Arguments[-1]
            $publicReadbackBytes
        }
        [pscustomobject]@{
            ExitCode = 0
            StandardOutput = [Convert]::ToBase64String([byte[]] $payload)
            StandardError = ''
            ProcessId = $PID + 1
        }
    }.GetNewClosure()
    $publicReadbackScpRunnerState = [pscustomobject]@{ Calls = 0; FileName = $null; SourceArgument = $null; LastArgument = $null }
    $publicReadbackScpRunner = {
        param($FileName, $Arguments)
        $publicReadbackScpRunnerState.Calls++
        $publicReadbackScpRunnerState.FileName = $FileName
        $publicReadbackScpRunnerState.SourceArgument = [string] $Arguments[-2]
        $publicReadbackScpRunnerState.LastArgument = [string] $Arguments[-1]
        0
    }.GetNewClosure()
    $publicReadbackAdapter = New-CountdownRemoteDeploymentReadbackSshAdapter `
        -Options $publicReadbackOptions `
        -Destination 'replicator@fixture-host' `
        -RemoteRoot 'D:\fixture\readback-root' `
        -ScpExecutablePath 'D:\fixture\scp.exe' `
        -ScpOptions $publicReadbackOptions `
        -ScpCommandRunner $publicReadbackScpRunner `
        -CommandRunner $publicReadbackRunner
    Assert-Equal $false ($null -ne $publicReadbackAdapter.PSObject.Properties['Rollback']) 'Independent readback adapter does not expose Rollback capability'
    Assert-Equal $false ($null -ne $publicReadbackAdapter.PSObject.Properties['RollbackAction']) 'Independent readback adapter does not expose RollbackAction capability'

    $publicReadbackLedger = Join-Path $remoteDeploymentTestRoot 'public-readback-transfer-attempts.json'
    $publicReadbackBindingEvidence = Join-Path $remoteDeploymentTestRoot 'public-readback-binding.json'
    $publicReadbackEvidence = Join-Path $remoteDeploymentTestRoot 'public-independent-readback.json'
    Invoke-CountdownRemoteDeploymentIndependentReadback `
        -LedgerPath $publicReadbackLedger `
        -DeploymentBindingEvidencePath $publicReadbackBindingEvidence `
        -IndependentReadbackEvidencePath $publicReadbackEvidence `
        -RemoteDeploymentBindingPath 'D:\fixture\deployment-binding.json' `
        -DeploymentBindingSha256 $publicReadbackBindingHash `
        -SuccessReceipt ([pscustomobject]@{
                RuntimeProcessId = 4242
                SettingsHashAfter = $expectedReadbackSettingsHash
                SettingsLengthAfter = 2
                SettingsMtimeUtcAfter = '2026-01-02T00:00:00.0000000Z'
                SettingsUnchanged = $false
            }) `
        -ExpectedExecutableSha256 $expectedReadbackExecutableHash `
        -ExpectedRemoteHost 'fixture-host' `
        -ExpectedRemoteUser 'fixture-user' `
        -InstallLayout (& $installLayoutCommand) `
        -Adapter $publicReadbackAdapter
    $publicReadbackLedgerDocument = Get-Content -LiteralPath $publicReadbackLedger -Raw | ConvertFrom-Json
    Assert-Equal 2 ([int] $publicReadbackRunnerState.Calls) 'Independent readback public adapter executes the binding and readback ssh reads once each'
    Assert-Equal 1 ([int] $publicReadbackScpRunnerState.Calls) 'Independent readback public adapter uploads the reader script exactly once'
    Assert-Equal 'D:\fixture\scp.exe' ([string] $publicReadbackScpRunnerState.FileName) 'Independent readback scp upload starts the injected executable path'
    Assert-Equal 'replicator@fixture-host:D:/fixture/readback-root/post-deployment-independent-readback.ps1' ([string] $publicReadbackScpRunnerState.LastArgument) 'Independent readback scp upload targets the fixed remote script name'
    Assert-Equal 'remote-deployment-binding,post-deployment-independent-readback-script,post-deployment-independent-readback' ((@($publicReadbackLedgerDocument.attempts).id) -join ',') 'Independent readback public interface preserves ledger IDs and order including the scp script entry'
    Assert-Equal 'ssh,scp,ssh' ((@($publicReadbackLedgerDocument.attempts).method) -join ',') 'Independent readback public interface records the scp script channel between both ssh reads'
    Assert-Equal 'succeeded,succeeded,succeeded' ((@($publicReadbackLedgerDocument.attempts).status) -join ',') 'Independent readback public interface records all three attempts successful'
    Assert-Equal $true (Test-Path -LiteralPath $publicReadbackBindingEvidence -PathType Leaf) 'Independent readback public interface publishes binding evidence'
    Assert-Equal $true (Test-Path -LiteralPath $publicReadbackEvidence -PathType Leaf) 'Independent readback public interface publishes readback evidence'
    $publicReadbackScriptEvidence = Join-Path $remoteDeploymentTestRoot 'post-deployment-independent-readback.ps1'
    Assert-Equal $true (Test-Path -LiteralPath $publicReadbackScriptEvidence -PathType Leaf) 'Independent readback public interface persists the uploaded reader script to run evidence'
    Assert-Equal $publicReadbackScriptEvidence ([string] $publicReadbackScpRunnerState.SourceArgument) 'Independent readback scp upload sources the persisted run-evidence reader script'
    $publicReadbackScriptSha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $publicReadbackScriptEvidence).Hash
    Assert-Equal $true ((([string] $publicReadbackRunnerState.ReadbackRemoteCommand).Length -gt 0) -and (([string] $publicReadbackRunnerState.ReadbackRemoteCommand).Length -lt 8000)) 'Independent readback ssh remote command length stays bounded regardless of reader script size'
    $publicReadbackEncodedCommand = [regex]::Match(
        [string] $publicReadbackRunnerState.ReadbackRemoteCommand,
        '-EncodedCommand\s+(\S+)').Groups[1].Value
    $publicReadbackWrapper = [System.Text.Encoding]::Unicode.GetString(
        [Convert]::FromBase64String($publicReadbackEncodedCommand))
    Assert-Equal $true ($publicReadbackWrapper.Contains($publicReadbackScriptSha256)) 'Independent readback short command pins the uploaded script SHA-256'
    Assert-Equal $true ($publicReadbackWrapper.Contains('D:\fixture\readback-root\post-deployment-independent-readback.ps1')) 'Independent readback short command pins the remote script path'
    Assert-Equal $false ($publicReadbackWrapper.Contains('Invoke-CountdownRemoteDeploymentReadbackProbe')) 'Independent readback short command does not inline the reader definitions'

    $wrapperExecutionRoot = Join-Path $remoteDeploymentTestRoot 'wrapper-execution'
    New-Item -ItemType Directory -Path $wrapperExecutionRoot -Force | Out-Null
    $uploadedWrapperChildPath = Join-Path $wrapperExecutionRoot 'post-deployment-independent-readback.ps1'
    [System.IO.File]::WriteAllText(
        $uploadedWrapperChildPath,
        "[Console]::Out.Write('wrapper-child-marker')",
        [System.Text.UTF8Encoding]::new($false))
    $uploadedWrapperChildSha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $uploadedWrapperChildPath).Hash
    $wrapperCapture = [pscustomobject]@{ RemoteCommand = $null }
    $wrapperCaptureRunner = {
        param($FileName, $Arguments)
        $wrapperCapture.RemoteCommand = [string] $Arguments[-1]
        [pscustomobject]@{
            ExitCode = 0
            StandardOutput = ''
            StandardError = ''
            ProcessId = $PID + 1
        }
    }.GetNewClosure()
    $wrapperParameters = @{
        Options = $publicReadbackOptions
        Destination = 'replicator@fixture-host'
        RemoteScriptPath = $uploadedWrapperChildPath
        ExpectedScriptSha256 = ('0' * 64)
        ExpectedRemoteHost = 'fixture-host'
        ExpectedRemoteUser = 'fixture-user'
        ExpectedProcessId = 4242
        ExpectedExecutableSha256 = $expectedReadbackExecutableHash
        ExpectedTokenSha256 = 'E' * 64
        ExpectedDeploymentBindingSha256 = $publicReadbackBindingHash
        DeploymentBindingPath = 'D:\fixture\deployment-binding.json'
        CommandRunner = $wrapperCaptureRunner
    }
    $wrapperEngine = (Get-Command pwsh.exe -CommandType Application -ErrorAction Stop | Select-Object -First 1 -ExpandProperty Source)
    $wrapperFilePath = Join-Path $wrapperExecutionRoot 'executed-wrapper.ps1'
    $wrapperStdoutPath = Join-Path $wrapperExecutionRoot 'executed-wrapper.stdout.txt'
    $wrapperStderrPath = Join-Path $wrapperExecutionRoot 'executed-wrapper.stderr.txt'
    foreach ($wrapperCase in @(
            @{ Name = 'hash-mismatch'; ExpectedScriptSha256 = ('0' * 64); UploadedText = "[Console]::Out.Write('wrapper-child-marker')"; ExpectedExitCode = -1 },
            @{ Name = 'case-insensitive-hash'; ExpectedScriptSha256 = $uploadedWrapperChildSha256.ToLowerInvariant(); UploadedText = "[Console]::Out.Write('wrapper-child-marker')"; ExpectedExitCode = 0 },
            @{ Name = 'child-exit-passthrough'; ExpectedScriptSha256 = $null; UploadedText = 'exit 5'; ExpectedExitCode = 5 }
        )) {
        if ($null -ne $wrapperCase.ExpectedScriptSha256) {
            $wrapperParameters.ExpectedScriptSha256 = [string] $wrapperCase.ExpectedScriptSha256
        }
        else {
            [System.IO.File]::WriteAllText(
                $uploadedWrapperChildPath,
                [string] $wrapperCase.UploadedText,
                [System.Text.UTF8Encoding]::new($false))
            $wrapperParameters.ExpectedScriptSha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $uploadedWrapperChildPath).Hash
        }
        $wrapperCapture.RemoteCommand = $null
        [void] (& $remoteDeploymentModule { Invoke-CountdownRemoteDeploymentIndependentRead @wrapperParameters })
        $wrapperEncoded = [regex]::Match(
            [string] $wrapperCapture.RemoteCommand,
            '-EncodedCommand\s+(\S+)').Groups[1].Value
        $wrapperScriptText = [System.Text.Encoding]::Unicode.GetString(
            [Convert]::FromBase64String($wrapperEncoded))
        [System.IO.File]::WriteAllText(
            $wrapperFilePath,
            $wrapperScriptText,
            [System.Text.UTF8Encoding]::new($false))
        $wrapperProcess = Start-Process -FilePath $wrapperEngine -ArgumentList @(
            '-NoLogo', '-NoProfile', '-File', $wrapperFilePath
        ) -WorkingDirectory $wrapperExecutionRoot -PassThru -WindowStyle Hidden `
            -RedirectStandardOutput $wrapperStdoutPath `
            -RedirectStandardError $wrapperStderrPath
        $wrapperProcess.WaitForExit()
        $wrapperStdout = [System.IO.File]::ReadAllText($wrapperStdoutPath, [System.Text.Encoding]::UTF8)
        $wrapperStderr = [System.Text.Encoding]::UTF8.GetString(
            [System.IO.File]::ReadAllBytes($wrapperStderrPath)) -replace "`e\[[0-9;]*m", ''
        if ($wrapperCase.Name -eq 'hash-mismatch') {
            Assert-Equal $true ($wrapperProcess.ExitCode -ne 0) 'Wrapper aborts with a nonzero exit code on an uploaded script hash mismatch'
            Assert-Equal $true ($wrapperStderr -match '独立回读脚本哈希不匹配') 'Wrapper reports the hash mismatch reason on stderr'
            Assert-Equal $false ($wrapperStdout.Contains('wrapper-child-marker')) 'Wrapper never executes the uploaded script after a hash mismatch'
        }
        elseif ($wrapperCase.Name -eq 'case-insensitive-hash') {
            Assert-Equal 0 ([int] $wrapperProcess.ExitCode) 'Wrapper accepts a lowercase hex script hash'
            Assert-Equal $true ($wrapperStdout.Contains('wrapper-child-marker')) 'Wrapper executes the uploaded script when the hash matches case-insensitively'
        }
        else {
            Assert-Equal 5 ([int] $wrapperProcess.ExitCode) 'Wrapper propagates the reader child exit code'
        }
    }

    $failedPublicReadback = $validReadbackJson | ConvertFrom-Json -AsHashtable
    $failedPublicReadback.RuntimeStage = 'starting'
    $failedPublicReadbackBytes = [System.Text.UTF8Encoding]::new($false).GetBytes(
        ($failedPublicReadback | ConvertTo-Json -Compress))
    $failedPublicReadbackRunnerState = [pscustomobject]@{ Calls = 0 }
    $failedPublicReadbackRunner = {
        param($FileName, $Arguments)
        $failedPublicReadbackRunnerState.Calls++
        $payload = if ($failedPublicReadbackRunnerState.Calls -eq 1) {
            $publicReadbackBindingBytes
        }
        else {
            $failedPublicReadbackBytes
        }
        [pscustomobject]@{
            ExitCode = 0
            StandardOutput = [Convert]::ToBase64String([byte[]] $payload)
            StandardError = ''
            ProcessId = $PID + 1
        }
    }.GetNewClosure()
    $failedPublicReadbackAdapter = New-CountdownRemoteDeploymentReadbackSshAdapter `
        -Options $publicReadbackOptions `
        -Destination 'replicator@fixture-host' `
        -RemoteRoot 'D:\fixture\readback-root' `
        -ScpExecutablePath 'D:\fixture\scp.exe' `
        -ScpOptions $publicReadbackOptions `
        -ScpCommandRunner { param($FileName, $Arguments) 0 } `
        -CommandRunner $failedPublicReadbackRunner
    $failedPublicReadbackMessage = $null
    $failedPublicReadbackEvidence = Join-Path $remoteDeploymentTestRoot 'failed-public-independent-readback.json'
    try {
        Invoke-CountdownRemoteDeploymentIndependentReadback `
            -LedgerPath (Join-Path $remoteDeploymentTestRoot 'failed-public-readback-transfer-attempts.json') `
            -DeploymentBindingEvidencePath (Join-Path $remoteDeploymentTestRoot 'failed-public-readback-binding.json') `
            -IndependentReadbackEvidencePath $failedPublicReadbackEvidence `
            -RemoteDeploymentBindingPath 'D:\fixture\deployment-binding.json' `
            -DeploymentBindingSha256 $publicReadbackBindingHash `
            -SuccessReceipt ([pscustomobject]@{
                    RuntimeProcessId = 4242
                    SettingsHashAfter = $expectedReadbackSettingsHash
                    SettingsLengthAfter = 2
                    SettingsMtimeUtcAfter = '2026-01-02T00:00:00.0000000Z'
                    SettingsUnchanged = $false
                }) `
            -ExpectedExecutableSha256 $expectedReadbackExecutableHash `
            -ExpectedRemoteHost 'fixture-host' `
            -ExpectedRemoteUser 'fixture-user' `
            -InstallLayout (& $installLayoutCommand) `
            -Adapter $failedPublicReadbackAdapter | Out-Null
    }
    catch {
        $failedPublicReadbackMessage = $_.Exception.Message
    }
    Assert-Equal $false ($null -ne $failedPublicReadbackAdapter.PSObject.Properties['RollbackAction']) 'Failed independent readback adapter still exposes no RollbackAction capability'
    Assert-Equal $true ($failedPublicReadbackMessage -match '部署已提交但独立验证不完整，未回滚.*runtime 未达到 window-ready') 'Independent readback public interface reports post-commit failure without a rollback claim'
    Assert-Equal $false (Test-Path -LiteralPath $failedPublicReadbackEvidence) 'Failed independent readback never publishes readback evidence'
    $failedPublicReadbackLedgerDocument = Get-Content -LiteralPath (Join-Path $remoteDeploymentTestRoot 'failed-public-readback-transfer-attempts.json') -Raw | ConvertFrom-Json
    Assert-Equal 'remote-deployment-binding,post-deployment-independent-readback-script,post-deployment-independent-readback' ((@($failedPublicReadbackLedgerDocument.attempts).id) -join ',') 'Contract-drift independent readback still records the three-step ledger sequence'
    Assert-Equal 'succeeded,succeeded,failed' ((@($failedPublicReadbackLedgerDocument.attempts).status) -join ',') 'Contract-drift independent readback marks only the final ssh read failed'

    $remoteHashMismatchRunnerState = [pscustomobject]@{ Calls = 0 }
    $remoteHashMismatchRunner = {
        param($FileName, $Arguments)
        $remoteHashMismatchRunnerState.Calls++
        if ($remoteHashMismatchRunnerState.Calls -eq 1) {
            [pscustomobject]@{
                ExitCode = 0
                StandardOutput = [Convert]::ToBase64String([byte[]] $publicReadbackBindingBytes)
                StandardError = ''
                ProcessId = $PID + 1
            }
        }
        else {
            [pscustomobject]@{
                ExitCode = 1
                StandardOutput = ''
                StandardError = '独立回读脚本哈希不匹配。'
                ProcessId = $PID + 1
            }
        }
    }.GetNewClosure()
    $remoteHashMismatchScpRunnerState = [pscustomobject]@{ Calls = 0 }
    $remoteHashMismatchScpRunner = {
        param($FileName, $Arguments)
        $remoteHashMismatchScpRunnerState.Calls++
        0
    }.GetNewClosure()
    $remoteHashMismatchAdapter = New-CountdownRemoteDeploymentReadbackSshAdapter `
        -Options $publicReadbackOptions `
        -Destination 'replicator@fixture-host' `
        -RemoteRoot 'D:\fixture\readback-root' `
        -ScpExecutablePath 'D:\fixture\scp.exe' `
        -ScpOptions $publicReadbackOptions `
        -ScpCommandRunner $remoteHashMismatchScpRunner `
        -CommandRunner $remoteHashMismatchRunner
    $remoteHashMismatchMessage = $null
    $remoteHashMismatchEvidence = Join-Path $remoteDeploymentTestRoot 'remote-hash-mismatch-readback.json'
    try {
        Invoke-CountdownRemoteDeploymentIndependentReadback `
            -LedgerPath (Join-Path $remoteDeploymentTestRoot 'remote-hash-mismatch-transfer-attempts.json') `
            -DeploymentBindingEvidencePath (Join-Path $remoteDeploymentTestRoot 'remote-hash-mismatch-binding.json') `
            -IndependentReadbackEvidencePath $remoteHashMismatchEvidence `
            -RemoteDeploymentBindingPath 'D:\fixture\deployment-binding.json' `
            -DeploymentBindingSha256 $publicReadbackBindingHash `
            -SuccessReceipt ([pscustomobject]@{
                    RuntimeProcessId = 4242
                    SettingsHashAfter = $expectedReadbackSettingsHash
                    SettingsLengthAfter = 2
                    SettingsMtimeUtcAfter = '2026-01-02T00:00:00.0000000Z'
                    SettingsUnchanged = $false
                }) `
            -ExpectedExecutableSha256 $expectedReadbackExecutableHash `
            -ExpectedRemoteHost 'fixture-host' `
            -ExpectedRemoteUser 'fixture-user' `
            -InstallLayout (& $installLayoutCommand) `
            -Adapter $remoteHashMismatchAdapter | Out-Null
    }
    catch {
        $remoteHashMismatchMessage = $_.Exception.Message
    }
    Assert-Equal 1 ([int] $remoteHashMismatchScpRunnerState.Calls) 'Remote hash-mismatch fixture still uploads the reader script first'
    Assert-Equal $true ($remoteHashMismatchMessage -match '部署已提交但独立验证不完整，未回滚.*独立回读脚本哈希不匹配') 'Remote script hash mismatch aborts before execution and reports post-commit verification incomplete'
    Assert-Equal $false (Test-Path -LiteralPath $remoteHashMismatchEvidence) 'Remote hash mismatch never publishes readback evidence'
    $remoteHashMismatchLedgerDocument = Get-Content -LiteralPath (Join-Path $remoteDeploymentTestRoot 'remote-hash-mismatch-transfer-attempts.json') -Raw | ConvertFrom-Json
    Assert-Equal 'remote-deployment-binding,post-deployment-independent-readback-script,post-deployment-independent-readback' ((@($remoteHashMismatchLedgerDocument.attempts).id) -join ',') 'Remote hash mismatch records the full three-step ledger sequence'
    Assert-Equal 'succeeded,succeeded,failed-bytes-unverified' ((@($remoteHashMismatchLedgerDocument.attempts).status) -join ',') 'Remote hash mismatch maps to scp succeeded plus ssh failed-bytes-unverified'
    Assert-Equal 1 ([int] @($remoteHashMismatchLedgerDocument.attempts)[-1].exitCode) 'Remote hash mismatch propagates the nonzero remote exit code'

    $uploadFailureRunnerState = [pscustomobject]@{ Calls = 0 }
    $uploadFailureRunner = {
        param($FileName, $Arguments)
        $uploadFailureRunnerState.Calls++
        [pscustomobject]@{
            ExitCode = 0
            StandardOutput = [Convert]::ToBase64String([byte[]] $publicReadbackBindingBytes)
            StandardError = ''
            ProcessId = $PID + 1
        }
    }.GetNewClosure()
    $uploadFailureAdapter = New-CountdownRemoteDeploymentReadbackSshAdapter `
        -Options $publicReadbackOptions `
        -Destination 'replicator@fixture-host' `
        -RemoteRoot 'D:\fixture\readback-root' `
        -ScpExecutablePath 'D:\fixture\scp.exe' `
        -ScpOptions $publicReadbackOptions `
        -ScpCommandRunner { param($FileName, $Arguments) 7 } `
        -CommandRunner $uploadFailureRunner
    $uploadFailureMessage = $null
    $uploadFailureEvidence = Join-Path $remoteDeploymentTestRoot 'upload-failure-readback.json'
    try {
        Invoke-CountdownRemoteDeploymentIndependentReadback `
            -LedgerPath (Join-Path $remoteDeploymentTestRoot 'upload-failure-transfer-attempts.json') `
            -DeploymentBindingEvidencePath (Join-Path $remoteDeploymentTestRoot 'upload-failure-binding.json') `
            -IndependentReadbackEvidencePath $uploadFailureEvidence `
            -RemoteDeploymentBindingPath 'D:\fixture\deployment-binding.json' `
            -DeploymentBindingSha256 $publicReadbackBindingHash `
            -SuccessReceipt ([pscustomobject]@{
                    RuntimeProcessId = 4242
                    SettingsHashAfter = $expectedReadbackSettingsHash
                    SettingsLengthAfter = 2
                    SettingsMtimeUtcAfter = '2026-01-02T00:00:00.0000000Z'
                    SettingsUnchanged = $false
                }) `
            -ExpectedExecutableSha256 $expectedReadbackExecutableHash `
            -ExpectedRemoteHost 'fixture-host' `
            -ExpectedRemoteUser 'fixture-user' `
            -InstallLayout (& $installLayoutCommand) `
            -Adapter $uploadFailureAdapter | Out-Null
    }
    catch {
        $uploadFailureMessage = $_.Exception.Message
    }
    Assert-Equal 1 ([int] $uploadFailureRunnerState.Calls) 'Upload failure runs only the binding ssh read before failing'
    Assert-Equal $true ($uploadFailureMessage -match '部署已提交但独立验证不完整，未回滚.*SCP 传输失败，退出码：7') 'Upload failure reports post-commit verification incomplete with the scp exit code'
    Assert-Equal $false (Test-Path -LiteralPath $uploadFailureEvidence) 'Upload failure never publishes readback evidence'
    $uploadFailureLedgerDocument = Get-Content -LiteralPath (Join-Path $remoteDeploymentTestRoot 'upload-failure-transfer-attempts.json') -Raw | ConvertFrom-Json
    Assert-Equal 'remote-deployment-binding,post-deployment-independent-readback-script' ((@($uploadFailureLedgerDocument.attempts).id) -join ',') 'Upload failure records binding plus the scp attempt without any ssh readback entry'
    Assert-Equal 'succeeded,failed-bytes-unverified' ((@($uploadFailureLedgerDocument.attempts).status) -join ',') 'Upload failure maps the scp attempt to the failed-bytes-unverified ledger state'
    Assert-Equal 7 ([int] @($uploadFailureLedgerDocument.attempts)[-1].exitCode) 'Upload failure preserves the scp exit code in the ledger'

    $sshStartFailureRunnerState = [pscustomobject]@{ Calls = 0 }
    $sshStartFailureRunner = {
        param($FileName, $Arguments)
        $sshStartFailureRunnerState.Calls++
        if ($sshStartFailureRunnerState.Calls -eq 1) {
            [pscustomobject]@{
                ExitCode = 0
                StandardOutput = [Convert]::ToBase64String([byte[]] $publicReadbackBindingBytes)
                StandardError = ''
                ProcessId = $PID + 1
            }
        }
        else {
            throw '模拟 ssh.exe 无法启动。'
        }
    }.GetNewClosure()
    $sshStartFailureAdapter = New-CountdownRemoteDeploymentReadbackSshAdapter `
        -Options $publicReadbackOptions `
        -Destination 'replicator@fixture-host' `
        -RemoteRoot 'D:\fixture\readback-root' `
        -ScpExecutablePath 'D:\fixture\scp.exe' `
        -ScpOptions $publicReadbackOptions `
        -ScpCommandRunner { param($FileName, $Arguments) 0 } `
        -CommandRunner $sshStartFailureRunner
    $sshStartFailureMessage = $null
    $sshStartFailureEvidence = Join-Path $remoteDeploymentTestRoot 'ssh-start-failure-readback.json'
    try {
        Invoke-CountdownRemoteDeploymentIndependentReadback `
            -LedgerPath (Join-Path $remoteDeploymentTestRoot 'ssh-start-failure-transfer-attempts.json') `
            -DeploymentBindingEvidencePath (Join-Path $remoteDeploymentTestRoot 'ssh-start-failure-binding.json') `
            -IndependentReadbackEvidencePath $sshStartFailureEvidence `
            -RemoteDeploymentBindingPath 'D:\fixture\deployment-binding.json' `
            -DeploymentBindingSha256 $publicReadbackBindingHash `
            -SuccessReceipt ([pscustomobject]@{
                    RuntimeProcessId = 4242
                    SettingsHashAfter = $expectedReadbackSettingsHash
                    SettingsLengthAfter = 2
                    SettingsMtimeUtcAfter = '2026-01-02T00:00:00.0000000Z'
                    SettingsUnchanged = $false
                }) `
            -ExpectedExecutableSha256 $expectedReadbackExecutableHash `
            -ExpectedRemoteHost 'fixture-host' `
            -ExpectedRemoteUser 'fixture-user' `
            -InstallLayout (& $installLayoutCommand) `
            -Adapter $sshStartFailureAdapter | Out-Null
    }
    catch {
        $sshStartFailureMessage = $_.Exception.Message
    }
    Assert-Equal 2 ([int] $sshStartFailureRunnerState.Calls) 'ssh start failure attempts both ssh reads before failing'
    Assert-Equal $true ($sshStartFailureMessage -match '部署已提交但独立验证不完整，未回滚.*退出码：-1.*无法启动 部署后独立回读') 'ssh start failure reports post-commit verification incomplete with exit code -1'
    Assert-Equal $false (Test-Path -LiteralPath $sshStartFailureEvidence) 'ssh start failure never publishes readback evidence'
    $sshStartFailureLedgerDocument = Get-Content -LiteralPath (Join-Path $remoteDeploymentTestRoot 'ssh-start-failure-transfer-attempts.json') -Raw | ConvertFrom-Json
    Assert-Equal 'remote-deployment-binding,post-deployment-independent-readback-script,post-deployment-independent-readback' ((@($sshStartFailureLedgerDocument.attempts).id) -join ',') 'ssh start failure records the full three-step ledger sequence'
    Assert-Equal 'succeeded,succeeded,failed-bytes-unverified' ((@($sshStartFailureLedgerDocument.attempts).status) -join ',') 'ssh start failure maps to scp succeeded plus ssh failed-bytes-unverified'
    Assert-Equal -1 ([int] @($sshStartFailureLedgerDocument.attempts)[-1].exitCode) 'ssh start failure records exit code -1 in the ssh ledger entry'
}
finally {
    Import-Module (Join-Path $PSScriptRoot 'Countdown.Settings.psm1') -Force
    Import-Module (Join-Path $PSScriptRoot 'Countdown.Runtime.psm1') -Force
    Import-Module (Join-Path $PSScriptRoot 'Countdown.InstallTransaction.psm1') -Force -DisableNameChecking
    Import-Module (Join-Path $PSScriptRoot 'Countdown.InstallLifecycle.psm1') -Force
    Remove-CountdownIsolatedTestTree -Path $remoteDeploymentTestRoot -AllowedRoot $remoteDeploymentEvidenceRoot
}
