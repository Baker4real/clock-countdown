#requires -Version 7.0

[CmdletBinding()]
param(
    [switch] $ParentTransactionMutexHeld,
    [switch] $AcceptanceOnly,
    [switch] $ReleaseContractOnly,
    [switch] $ValidationOnly,
    [switch] $RemoteDeploymentOnly
)

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'Countdown.Core.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'Countdown.Notes.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'Countdown.Tags.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'Countdown.Settings.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'Countdown.Runtime.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'Countdown.InstallTransaction.psm1') -Force -DisableNameChecking
Import-Module (Join-Path $PSScriptRoot 'Countdown.TransactionAcceptance.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'Countdown.ReleaseContract.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'Countdown.Validation.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'Countdown.RemoteDeployment.psm1') -Force

$failures = [System.Collections.Generic.List[string]]::new()
$script:assertions = 0
function Assert-Equal($Expected, $Actual, [string] $Name) {
    $script:assertions++
    if ($Expected -ne $Actual) {
        $failures.Add("$Name expected=[$Expected] actual=[$Actual]")
    }
}

function Remove-CountdownReleaseContractTestTree {
    param([Parameter(Mandatory)] [string] $Path)

    $resolvedPath = [System.IO.Path]::GetFullPath($Path)
    $expectedParent = [System.IO.Path]::GetFullPath($PSScriptRoot).TrimEnd('\')
    if (-not [string]::Equals(
            [System.IO.Path]::GetDirectoryName($resolvedPath),
            $expectedParent,
            [System.StringComparison]::OrdinalIgnoreCase) -or
        -not [System.IO.Path]::GetFileName($resolvedPath).StartsWith(
            'release-contract-test-',
            [System.StringComparison]::Ordinal)) {
        throw "拒绝清理未受管的发布契约测试目录：$resolvedPath"
    }
    if (-not (Test-Path -LiteralPath $resolvedPath -PathType Container)) {
        return
    }

    foreach ($file in Get-ChildItem -LiteralPath $resolvedPath -File -Recurse) {
        Remove-Item -LiteralPath $file.FullName -Force
    }
    foreach ($directory in @(
            Get-ChildItem -LiteralPath $resolvedPath -Directory -Recurse |
                Sort-Object { $_.FullName.Length } -Descending
        )) {
        Remove-Item -LiteralPath $directory.FullName -Force
    }
    Remove-Item -LiteralPath $resolvedPath -Force
}

function Remove-CountdownIsolatedTestTree {
    param(
        [Parameter(Mandatory)] [string] $Path,
        [Parameter(Mandatory)] [string] $AllowedRoot
    )

    $resolvedPath = [System.IO.Path]::GetFullPath($Path)
    $resolvedRoot = [System.IO.Path]::GetFullPath($AllowedRoot).TrimEnd('\') + '\'
    if (-not $resolvedPath.StartsWith($resolvedRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "拒绝清理隔离测试根目录之外的路径：$resolvedPath"
    }
    if (-not (Test-Path -LiteralPath $resolvedPath -PathType Container)) {
        return
    }

    foreach ($file in Get-ChildItem -LiteralPath $resolvedPath -File -Recurse) {
        Remove-Item -LiteralPath $file.FullName -Force
    }
    foreach ($directory in @(
            Get-ChildItem -LiteralPath $resolvedPath -Directory -Recurse |
                Sort-Object { $_.FullName.Length } -Descending
        )) {
        Remove-Item -LiteralPath $directory.FullName -Force
    }
    Remove-Item -LiteralPath $resolvedPath -Force
}

if ($ValidationOnly -or (-not $AcceptanceOnly -and -not $ReleaseContractOnly -and -not $RemoteDeploymentOnly)) {
    $validationEvidenceRoot = 'D:\Codex\.codex\run-evidence\clock-countdown-validation-tests'
    $validationTestRoot = Join-Path $validationEvidenceRoot ([guid]::NewGuid().ToString('N'))
    $utf8NoBom = [System.Text.UTF8Encoding]::new($false)
    try {
        New-Item -ItemType Directory -Path $validationTestRoot -Force | Out-Null
        $validationApplicationReport = [ordered]@{
            Valid = $true
            FloatingWindowPresent = $true
        }
        foreach ($fieldName in @(
                'MainReferenceDecorationsShareScaleAtMinimumAndWideSizes',
                'MainReferenceDecorationLayerOrderMatches',
                'MainWorkspaceResourcesFollowDayNightTheme',
                'MainDynamicCountdownCardsUseThemeResources',
                'MainDynamicNoteCardsUseThemeResources',
                'FloatingReferenceContentSafeAreaMatches',
                'FloatingReferenceBaselineGateIsolationBehavior',
                'FloatingReferenceFreshnessGateIsolationBehavior',
                'FloatingLongNotesBodyHasNoManualLineBreaks',
                'FloatingLongNotesNaturalWraps',
                'FloatingLongNotesUsesOnlyOuterScroller',
                'FloatingLongNotesOuterScrollable',
                'FloatingLongNotesTopBottomReachable',
                'FloatingLongNotesSafeAreaMatches',
                'FloatingLongNotesLayoutValid',
                'FloatingTitleUsesSemiBoldWeight',
                'FloatingNoteInputWraps',
                'FloatingNoteInputScrolls',
                'FloatingInitialWorkspaceStateMatches',
                'FloatingWorkspaceSwitchingStaysSynchronized',
                'FloatingCountdownSelectionSurvivesDayNightThemes',
                'FloatingQuickAddDayResourcesFollowLocalTheme',
                'FloatingQuickAddNightResourcesFollowLocalTheme',
                'FloatingQuickAddIndependentFromApplicationAutoTheme',
                'FloatingQuickAddThemeResourceIsolationBehavior',
                'FloatingQuickAddDayExpandedScrolledCaptureWritten',
                'FloatingQuickAddNightExpandedScrolledCaptureWritten',
                'FloatingNightPlushSourceAlphaBoundsMatch',
                'FloatingNightPlushEdgeBrightnessWithinLimit',
                'FloatingNightPlushEdgeSaturationWithinLimit',
                'FloatingNightPlushEdgeGlowGateIsolationBehavior',
                'FloatingSecondaryRemainingUnitSpacingMatches',
                'FloatingSecondaryRemainingLayoutMatches',
                'FloatingSecondaryRemainingGateIsolationBehavior',
                'FloatingScrollBarLocalStyleScoped',
                'FloatingScrollBarHitTargetUsable',
                'FloatingScrollBarVisualWeightProgression',
                'FloatingScrollBarUsesWarmThemeTokens',
                'FloatingScrollBarContentRemainsSafe',
                'FloatingScrollBarInputPathsWork',
                'FloatingScrollBarGateIsolationBehavior',
                'FloatingScrollBarDayNightStateCapturesReady',
                'UiQualityGateIsolationBehavior'
            )) {
            $validationApplicationReport[$fieldName] = $true
        }
        $validationApplicationReport['FloatingReferenceBaselineRequested'] = $false
        $validationApplicationReport['FloatingReferenceBaselineManifestAvailable'] = $false
        $validationApplicationReport['FloatingReferenceBaselineValidated'] = $false
        $validationApplicationReport['MainDynamicCountdownCardSampleCount'] = 1
        $validationApplicationReport['MainDynamicNoteCardSampleCount'] = 1
        $validationApplicationJson = $validationApplicationReport | ConvertTo-Json -Compress
        $validationApplicationFixture = $validationApplicationJson.Replace("'", "''")
        $validationFixtures = [ordered]@{
            'Countdown.Core.Tests.ps1' = "param([switch] `$ParentTransactionMutexHeld)`r`n'{`"Passed`":11,`"Failed`":0}'"
            'Countdown.CSharp.Tests.ps1' = @"
param([string] `$OfficeDeploymentStagingRoot)
if (`$PSBoundParameters.ContainsKey('OfficeDeploymentStagingRoot')) {
    [System.IO.File]::WriteAllText(
        (Join-Path `$PSScriptRoot 'office-staging-argument.txt'),
        `$OfficeDeploymentStagingRoot,
        [System.Text.UTF8Encoding]::new(`$false))
}
'{`"Passed`":13,`"Failed`":0,`"AdapterUiValidated`":true}'
"@
            'Countdown.Ui.Tests.ps1' = "'{`"Passed`":17,`"Failed`":0}'"
            '敏敏子倒计时.ps1' = "param([switch] `$ValidateOnly)`r`n'$validationApplicationFixture'"
        }
        foreach ($fixture in $validationFixtures.GetEnumerator()) {
            [System.IO.File]::WriteAllText(
                (Join-Path $validationTestRoot ([string] $fixture.Key)),
                [string] $fixture.Value,
                $utf8NoBom)
        }

        $validationPowerShell = (Get-Command pwsh.exe -CommandType Application -ErrorAction Stop |
            Select-Object -First 1 -ExpandProperty Source)
        $validationResult = Invoke-CountdownValidationSuite `
            -Root $validationTestRoot `
            -PowerShell7Path $validationPowerShell `
            -ParentTransactionMutexHeld
        Assert-Equal $true ([bool] $validationResult.Valid) 'Validation interface reports one stable success state'
        Assert-Equal 11 ([int] $validationResult.CoreTestsPassed) 'Validation interface exposes Core assertion count'
        Assert-Equal 13 ([int] $validationResult.CSharpTestsPassed) 'Validation interface exposes C# assertion count'
        Assert-Equal 17 ([int] $validationResult.UiTestsPassed) 'Validation interface exposes compatibility UI assertion count'
        Assert-Equal $true ([bool] $validationResult.UiValid) 'Validation interface exposes application validate-only state'
        Assert-Equal $true ([bool] $validationResult.AdapterUiValidated) 'Validation interface preserves adapter UI evidence'
        Assert-Equal '{"Passed":11,"Failed":0}' ([string] $validationResult.TestOutput) 'Validation interface preserves Core JSON compatibility'
        Assert-Equal '{"Passed":13,"Failed":0,"AdapterUiValidated":true}' ([string] $validationResult.CSharpTestOutput) 'Validation interface preserves C# JSON compatibility'
        Assert-Equal '{"Passed":17,"Failed":0}' ([string] $validationResult.UiTestOutput) 'Validation interface preserves UI JSON compatibility'
        Assert-Equal $validationApplicationJson ([string] $validationResult.UiValidationOutput) 'Validation interface preserves application JSON compatibility'
        Assert-Equal `
            ([System.IO.Path]::GetFullPath((Split-Path -Parent $validationTestRoot))) `
            ([System.IO.File]::ReadAllText((Join-Path $validationTestRoot 'office-staging-argument.txt'), [System.Text.Encoding]::UTF8)) `
            'Validation interface passes the current staging root to C# tests while the parent transaction mutex is held'

        [System.IO.File]::WriteAllText(
            (Join-Path $validationTestRoot 'Countdown.CSharp.Tests.ps1'),
            @"
param([string] `$OfficeDeploymentStagingRoot)
[System.IO.File]::WriteAllText(
    (Join-Path `$PSScriptRoot 'normal-office-staging-argument-present.txt'),
    [string] `$PSBoundParameters.ContainsKey('OfficeDeploymentStagingRoot'),
    [System.Text.UTF8Encoding]::new(`$false))
[Console]::Error.WriteLine('expected diagnostic')
'{`"Passed`":13,`"Failed`":0,`"AdapterUiValidated`":true}'
"@,
            $utf8NoBom)
        $successfulDiagnosticPreserved = $false
        try {
            $diagnosticValidationResult = Invoke-CountdownValidationSuite `
                -Root $validationTestRoot `
                -PowerShell7Path $validationPowerShell
            $successfulDiagnosticPreserved =
                [bool] $diagnosticValidationResult.Valid -and
                [string] $diagnosticValidationResult.CSharpTestOutput -eq '{"Passed":13,"Failed":0,"AdapterUiValidated":true}'
        }
        catch {
            $successfulDiagnosticPreserved = $false
        }
        Assert-Equal $true $successfulDiagnosticPreserved 'Validation interface parses stdout JSON without merging successful stderr diagnostics'
        Assert-Equal `
            'False' `
            ([System.IO.File]::ReadAllText((Join-Path $validationTestRoot 'normal-office-staging-argument-present.txt'), [System.Text.Encoding]::UTF8)) `
            'Validation interface does not pass the office staging parameter outside the parent transaction context'

        $cSharpTestScript = Join-Path $PSScriptRoot 'Countdown.CSharp.Tests.ps1'
        $invalidOfficeStagingRoot = Join-Path 'D:\Codex\deploy' ("invalid-csharp-staging-" + [guid]::NewGuid().ToString('N'))
        $invalidSourceOutput = @(& $validationPowerShell `
                -NoLogo `
                -NoProfile `
                -NonInteractive `
                -STA `
                -File $cSharpTestScript `
                -OfficeDeploymentStagingRoot $invalidOfficeStagingRoot 2>&1)
        $invalidSourceExitCode = $LASTEXITCODE
        Assert-Equal $true ($invalidSourceExitCode -ne 0 -and ($invalidSourceOutput -join "`n") -match '不属于指定的办公室部署 staging') 'C# tests reject an office staging that does not own the script source before creating test data'
        Assert-Equal $false (Test-Path -LiteralPath $invalidOfficeStagingRoot) 'Rejected office staging validation creates no directory'

        $nestedOfficeStagingRoot = Join-Path $invalidOfficeStagingRoot 'nested'
        $nestedStagingOutput = @(& $validationPowerShell `
                -NoLogo `
                -NoProfile `
                -NonInteractive `
                -STA `
                -File $cSharpTestScript `
                -OfficeDeploymentStagingRoot $nestedOfficeStagingRoot 2>&1)
        $nestedStagingExitCode = $LASTEXITCODE
        Assert-Equal $true ($nestedStagingExitCode -ne 0 -and ($nestedStagingOutput -join "`n") -match '必须是受管根的直接子目录') 'C# tests reject a nested office staging before creating test data'
        Assert-Equal $false (Test-Path -LiteralPath $invalidOfficeStagingRoot) 'Rejected nested staging validation creates no parent directory'

        [System.IO.File]::WriteAllText(
            (Join-Path $validationTestRoot 'Countdown.Ui.Tests.ps1'),
            "'{`"Passed`":16,`"Failed`":1}'",
            $utf8NoBom)
        $reportedFailureRejected = $false
        try {
            Invoke-CountdownValidationSuite `
                -Root $validationTestRoot `
                -PowerShell7Path $validationPowerShell | Out-Null
        }
        catch {
            $reportedFailureRejected = $_.Exception.Message -match '兼容 UI 回归报告包含 1 个失败'
        }
        Assert-Equal $true $reportedFailureRejected 'Validation interface rejects a failed report even when the process exits zero'

        [System.IO.File]::WriteAllText(
            (Join-Path $validationTestRoot 'Countdown.Ui.Tests.ps1'),
            "'{`"Passed`":17,`"Failed`":0}'",
            $utf8NoBom)
        [System.IO.File]::WriteAllText(
            (Join-Path $validationTestRoot 'Countdown.CSharp.Tests.ps1'),
            "'{`"Passed`":13,`"Failed`":0,`"AdapterUiValidated`":false}'",
            $utf8NoBom)
        $adapterUiFailureRejected = $false
        try {
            Invoke-CountdownValidationSuite `
                -Root $validationTestRoot `
                -PowerShell7Path $validationPowerShell | Out-Null
        }
        catch {
            $adapterUiFailureRejected = $_.Exception.Message -match 'C# 回归未确认 adapter UI 验证'
        }
        Assert-Equal $true $adapterUiFailureRejected 'Validation interface requires adapter UI evidence before reporting success'

        [System.IO.File]::WriteAllText(
            (Join-Path $validationTestRoot 'Countdown.CSharp.Tests.ps1'),
            "'{`"Passed`":13,`"Failed`":0,`"AdapterUiValidated`":true}'",
            $utf8NoBom)
        [System.IO.File]::WriteAllText(
            (Join-Path $validationTestRoot '敏敏子倒计时.ps1'),
            "param([switch] `$ValidateOnly)`r`n'{`"Valid`":`"false`",`"FloatingWindowPresent`":true}'",
            $utf8NoBom)
        $stringBooleanRejected = $false
        try {
            Invoke-CountdownValidationSuite `
                -Root $validationTestRoot `
                -PowerShell7Path $validationPowerShell | Out-Null
        }
        catch {
            $stringBooleanRejected = $_.Exception.Message -match '应用 validate-only 报告未通过'
        }
        Assert-Equal $true $stringBooleanRejected 'Validation interface rejects string values that only coerce to true'
    }
    finally {
        Remove-CountdownIsolatedTestTree -Path $validationTestRoot -AllowedRoot $validationEvidenceRoot
    }

    if ($ValidationOnly) {
        if ($failures.Count -gt 0) {
            $failures | ForEach-Object { Write-Error $_ }
            exit 1
        }
        [pscustomobject]@{ Passed = $script:assertions; Failed = 0; Scope = 'Validation' } | ConvertTo-Json
        exit 0
    }
}

if ($RemoteDeploymentOnly -or (-not $AcceptanceOnly -and -not $ReleaseContractOnly -and -not $ValidationOnly)) {
    $remoteDeploymentEvidenceRoot = 'D:\Codex\.codex\run-evidence\clock-countdown-remote-deployment-tests'
    $remoteDeploymentTestRoot = Join-Path $remoteDeploymentEvidenceRoot ([guid]::NewGuid().ToString('N'))
    $remoteDeploymentSource = Join-Path $remoteDeploymentTestRoot 'source'
    $remoteDeploymentInstall = Join-Path $remoteDeploymentTestRoot 'install'
    $remoteDeploymentReceipt = Join-Path $remoteDeploymentTestRoot 'deferred-install-receipt.json'
    $remoteDeploymentPowerShell = (Get-Command pwsh.exe -CommandType Application -ErrorAction Stop |
        Select-Object -First 1 -ExpandProperty Source)

    function New-CountdownRemoteDeploymentFixtureAdapter {
        param([ValidateSet('Success', 'BeginFailure', 'ValidationFailure', 'AdapterUiValidationFailure', 'ValidationSchemaFailure', 'AcceptanceFailure', 'CommitThenFail', 'RollbackFailure', 'FinalizeFailure', 'DisposeFailure', 'ValidationAndDisposeFailure', 'CommitProbeFailure', 'UnresolvedCommittedTransaction')] [string] $Mode)

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
            StartupRequestPath = Join-Path $remoteDeploymentInstall 'startup-request.json'
            RuntimeStatusPath = Join-Path $remoteDeploymentInstall 'runtime-status.json'
            AcceptanceStatusPath = Join-Path $remoteDeploymentInstall 'acceptance-status.json'
            LaunchToken = 'sensitive-launch-token-sentinel'
            ExpectedProcessId = 0
            ExpectedScheduledTaskIdentity = '<Task><Actions><Exec><Arguments>sensitive-task-argument-sentinel</Arguments></Exec></Actions></Task>'
            SettingsPath = Join-Path $remoteDeploymentInstall 'settings.json'
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
                    Validation = [pscustomobject]@{
                        SchemaVersion = if ($AdapterState.Mode -eq 'ValidationSchemaFailure') { 999 } else { 1 }
                        Valid = $AdapterState.Mode -notin @('ValidationFailure', 'RollbackFailure', 'ValidationAndDisposeFailure', 'CommitProbeFailure', 'UnresolvedCommittedTransaction')
                        CoreTestsPassed = 11
                        CSharpTestsPassed = 13
                        UiTestsPassed = 17
                        UiValid = $true
                        AdapterUiValidated = $AdapterState.Mode -ne 'AdapterUiValidationFailure'
                    }
                    RuntimeStatus = [pscustomobject]@{
                        processId = 4242
                        stage = 'window-ready'
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
                    TaskState = 'Running'
                    SettingsHashBefore = ('A' * 64)
                    SettingsHashAfter = ('B' * 64)
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
                if ($AdapterState.Mode -in @('DisposeFailure', 'ValidationAndDisposeFailure')) {
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
        $adapter.State.LauncherPath = Join-Path $remoteDeploymentTestRoot 'desktop\敏敏子倒计时.exe'
        $adapter.State.LegacyLauncherPath = Join-Path $remoteDeploymentTestRoot 'desktop\敏敏子倒计时.cmd'
        $adapter.State.LauncherTempPath = "$($adapter.State.LauncherPath).new"
        $adapter.State.SettingsSnapshotPath = Join-Path $transactionPath 'install-backup\settings.json'
        $adapter.State.SettingsPath = Join-Path $remoteDeploymentInstall 'settings.json'
        $adapter.State.StartupRequestPath = Join-Path $remoteDeploymentInstall 'startup-request.json'
        $adapter.State.RuntimeStatusPath = Join-Path $remoteDeploymentInstall 'runtime-status.json'
        $adapter.State.AcceptanceStatusPath = Join-Path $remoteDeploymentInstall 'acceptance-status.json'
        $adapter.State.LaunchToken = 'remote-deployment-fixture-token'
        $adapter.State.ExpectedProcessId = 4242
        $adapter.State.ExpectedScheduledTaskIdentity = '<Task />'
        $adapter.State | Add-Member -NotePropertyName RollbackCount -NotePropertyValue 0
        $adapter.State | Add-Member -NotePropertyName Disposed -NotePropertyValue $false

        $adapter.Begin = {
            param($AdapterState, $Context)
            [pscustomobject]@{
                Validation = [pscustomobject]@{
                    SchemaVersion = 1
                    Valid = $true
                    CoreTestsPassed = 11
                    CSharpTestsPassed = 13
                    UiTestsPassed = 17
                    UiValid = $true
                    AdapterUiValidated = $true
                }
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
                'Countdown.Validation.psm1',
                'Countdown.Runtime.psm1',
                'Countdown.Settings.psm1',
                'Countdown.TransactionAcceptance.psm1',
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
            @'
Set-StrictMode -Version Latest

function Complete-CountdownInstallTransaction {
    param(
        [string] $TransactionPath,
        [string] $ExpectedInstallPath,
        [string] $ExpectedTaskName,
        [string] $ExpectedLegacyTaskName,
        [string] $ExpectedLauncherPath,
        [string] $ExpectedLegacyLauncherPath,
        [string] $ExpectedLauncherTempPath
    )
    [System.IO.File]::WriteAllText(
        (Join-Path $TransactionPath 'accepted.marker'),
        'accepted',
        [System.Text.UTF8Encoding]::new($false))
}

function Remove-CountdownCommittedInstallTransaction {
    param(
        [string] $TransactionPath,
        [string] $ExpectedInstallPath,
        [string] $ExpectedTaskName,
        [string] $ExpectedLegacyTaskName,
        [string] $ExpectedLauncherPath,
        [string] $ExpectedLegacyLauncherPath,
        [string] $ExpectedLauncherTempPath,
        [scriptblock] $MoveAction
    )
    if ($TransactionPath -match 'CommitPreparationFailure') {
        throw '模拟 committed 点前的清理准备失败。'
    }

    $destination = "$TransactionPath.committed-fixture"
    if ($null -ne $MoveAction) {
        & $MoveAction $TransactionPath $destination
    }
    else {
        Move-Item -LiteralPath $TransactionPath -Destination $destination -ErrorAction Stop
    }
    if ($TransactionPath -match 'CommittedCleanupFailure') {
        throw '模拟 committed 点后的事务清理失败。'
    }
}

Export-ModuleMember -Function Complete-CountdownInstallTransaction, Remove-CountdownCommittedInstallTransaction
'@,
            [System.Text.UTF8Encoding]::new($false))
        [System.IO.File]::WriteAllText(
            (Join-Path $remoteDeploymentSource 'Countdown.Runtime.psm1'),
            "Set-StrictMode -Version Latest`r`n",
            [System.Text.UTF8Encoding]::new($false))
        [System.IO.File]::WriteAllText(
            (Join-Path $remoteDeploymentInstall 'Countdown.Settings.psm1'),
            "Set-StrictMode -Version Latest`r`n",
            [System.Text.UTF8Encoding]::new($false))
        [System.IO.File]::WriteAllText(
            (Join-Path $remoteDeploymentSource 'Countdown.TransactionAcceptance.psm1'),
            @'
Set-StrictMode -Version Latest

function Invoke-CountdownTransactionAcceptance {
    param(
        [string] $StartupRequestPath,
        [string] $RuntimeStatusPath,
        [string] $AcceptanceStatusPath,
        [string] $LaunchToken,
        [object] $ExpectedInteractiveContext,
        [int] $ExpectedProcessId,
        [int] $ExpectedSessionId,
        [string] $ExpectedApplicationPath,
        [string] $TaskName,
        [string] $ExpectedScheduledTaskIdentity,
        [string] $SettingsSnapshotPath,
        [string] $SettingsPath,
        [scriptblock] $GetInteractiveContext,
        [scriptblock] $GetScheduledTask,
        [scriptblock] $GetScheduledTaskIdentity,
        [scriptblock] $CompleteTransaction,
        [scriptblock] $RemoveCommittedTransaction
    )

    [void] (& $CompleteTransaction)
    switch ($TaskName) {
        'AcceptedFinalizationFailure' { throw '模拟 accepted 后 finalized 写入失败。' }
        'AcceptedReceiptFailure' { throw '模拟 accepted 后 finalized-ready 回执失败。' }
        'AcceptedEnvironmentFailure' { throw '模拟 accepted 后环境复验失败。' }
    }

    [void] (& $RemoveCommittedTransaction)
    [pscustomobject]@{
        AcceptanceState = 'finalized-ready'
        RuntimeProcessId = 4242
        TaskState = 'Running'
        SettingsHashBefore = ('A' * 64)
        SettingsHashAfter = ('B' * 64)
    }
}

Export-ModuleMember -Function Invoke-CountdownTransactionAcceptance
'@,
            [System.Text.UTF8Encoding]::new($false))
        [System.IO.File]::WriteAllText(
            (Join-Path $remoteDeploymentInstall 'settings.json'),
            '{"sensitive-settings-sentinel":true}',
            [System.Text.UTF8Encoding]::new($false))

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
                'Countdown.Runtime.psm1',
                'Countdown.TransactionAcceptance.psm1')) {
            Copy-Item `
                -LiteralPath (Join-Path $remoteDeploymentSource $file) `
                -Destination (Join-Path $acceptSourceRoot $file) `
                -Force
        }
        [System.IO.File]::WriteAllText(
            (Join-Path $acceptSourceRoot 'Countdown.TransactionAcceptance.psm1'),
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

function Invoke-CountdownTransactionAcceptance {
    param(
        [string] $StartupRequestPath,
        [string] $RuntimeStatusPath,
        [string] $AcceptanceStatusPath,
        [string] $LaunchToken,
        [object] $ExpectedInteractiveContext,
        [int] $ExpectedProcessId,
        [int] $ExpectedSessionId,
        [string] $ExpectedApplicationPath,
        [string] $TaskName,
        [string] $ExpectedScheduledTaskIdentity,
        [string] $SettingsSnapshotPath,
        [string] $SettingsPath,
        [scriptblock] $GetInteractiveContext,
        [scriptblock] $GetScheduledTask,
        [scriptblock] $GetScheduledTaskIdentity,
        [scriptblock] $CompleteTransaction,
        [scriptblock] $RemoveCommittedTransaction
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

    [void] (& $CompleteTransaction)
    [void] (& $RemoveCommittedTransaction)
    [pscustomobject]@{
        AcceptanceState = 'finalized-ready'
        RuntimeProcessId = 4242
        TaskState = 'Running'
        SettingsHashBefore = ('A' * 64)
        SettingsHashAfter = ('B' * 64)
    }
}

Export-ModuleMember -Function Invoke-CountdownTransactionAcceptance
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
                'Countdown.TransactionAcceptance.psm1',
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
        $acceptAdapter.State.LauncherPath = Join-Path $acceptLockRoot 'desktop\敏敏子倒计时.exe'
        $acceptAdapter.State.LegacyLauncherPath = Join-Path $acceptLockRoot 'desktop\敏敏子倒计时.cmd'
        $acceptAdapter.State.LauncherTempPath = "$($acceptAdapter.State.LauncherPath).new"
        $acceptAdapter.State.SettingsSnapshotPath = Join-Path $acceptTransactionPath 'install-backup\settings.json'
        $acceptAdapter.State.SettingsPath = Join-Path $acceptInstallRoot 'settings.json'
        $acceptAdapter.State.StartupRequestPath = Join-Path $acceptInstallRoot 'startup-request.json'
        $acceptAdapter.State.RuntimeStatusPath = Join-Path $acceptInstallRoot 'runtime-status.json'
        $acceptAdapter.State.AcceptanceStatusPath = Join-Path $acceptInstallRoot 'acceptance-status.json'
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
                        'Countdown.TransactionAcceptance.psm1',
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
            Import-Module (Join-Path $PSScriptRoot 'Countdown.TransactionAcceptance.psm1') -Force
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
            @'
function Undo-CountdownInstallTransaction {
    param(
        [string] $TransactionPath,
        [string] $PowerShell7Path,
        [string] $ExpectedInstallPath,
        [string] $ExpectedTaskName,
        [string] $ExpectedLegacyTaskName,
        [string] $ExpectedLauncherPath,
        [string] $ExpectedLegacyLauncherPath,
        [string] $ExpectedLauncherTempPath,
        [switch] $PreserveUnmanagedLaunchers
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

Export-ModuleMember -Function Undo-CountdownInstallTransaction
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
            'issue4-source-lock.txt' = (Get-FileHash -Algorithm SHA256 -LiteralPath $rollbackSourceProbePath).Hash
            'issue4-installed-lock.txt' = (Get-FileHash -Algorithm SHA256 -LiteralPath $rollbackInstalledProbePath).Hash
        }
        $rollbackAdapter = & $remoteDeploymentModule { New-CountdownRemoteDeploymentWindowsAdapter }
        $rollbackAdapter.State.TransactionModuleImported = $true
        $rollbackAdapter.State.InstallStarted = $true
        $rollbackAdapter.State.RollbackTransactionPath = $rollbackTransactionPath
        $rollbackAdapter.State.LauncherPath = Join-Path $rollbackLockRoot 'desktop\敏敏子倒计时.exe'
        $rollbackAdapter.State.LegacyLauncherPath = Join-Path $rollbackLockRoot 'desktop\敏敏子倒计时.cmd'
        $rollbackAdapter.State.LauncherTempPath = "$($rollbackAdapter.State.LauncherPath).new"
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
                -RelativePaths ([string[]] @('Countdown.InstallTransaction.psm1', 'issue4-source-lock.txt')) `
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
        Assert-Equal 11 ([int] $successResult.CoreTests) 'Remote deployment interface exposes stable Core evidence'
        Assert-Equal 13 ([int] $successResult.CSharpTests) 'Remote deployment interface exposes stable C# evidence'
        Assert-Equal 17 ([int] $successResult.UiTests) 'Remote deployment interface exposes stable compatibility UI evidence'
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
        Assert-Equal 14 @($bindingResult.PSObject.Properties).Count 'Deployment binding does not change the strict success receipt schema'
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
        Assert-Equal 14 @($normalizedSuccessReceipt.PSObject.Properties).Count 'Success receipt parser returns the exact schema field set'
        Assert-Equal 'Running' ([string] $normalizedSuccessReceipt.TaskState) 'Success receipt parser requires the running task state'
        Assert-Equal 'window-ready' ([string] $normalizedSuccessReceipt.RuntimeStage) 'Success receipt parser requires window-ready runtime state'
        Assert-Equal $false ($normalizedSuccessReceipt.PSObject.Properties.Name -contains 'LaunchToken') 'Success receipt schema excludes the raw launch token'

        $invalidSuccessReceiptCases = [ordered]@{}
        $missingFieldReceipt = $successReceiptJson | ConvertFrom-Json -AsHashtable
        [void] $missingFieldReceipt.Remove('UiTests')
        $invalidSuccessReceiptCases['missing field'] = $missingFieldReceipt | ConvertTo-Json -Compress
        $extraFieldReceipt = $successReceiptJson | ConvertFrom-Json -AsHashtable
        $extraFieldReceipt['Credential'] = 'must-not-be-persisted'
        $invalidSuccessReceiptCases['extra field'] = $extraFieldReceipt | ConvertTo-Json -Compress
        $invalidSuccessReceiptCases['truncated JSON'] = $successReceiptJson.Substring(0, $successReceiptJson.Length - 1)
        $invalidSuccessReceiptCases['multiple objects'] = "$successReceiptJson`n$successReceiptJson"
        $invalidSuccessReceiptCases['noise output'] = "unexpected output`n$successReceiptJson"
        $invalidSuccessReceiptCases['duplicate field'] = $successReceiptJson.Insert(1, '"Valid":true,')
        $unsupportedSchemaReceipt = $successReceiptJson | ConvertFrom-Json -AsHashtable
        $unsupportedSchemaReceipt.SchemaVersion = 2
        $invalidSuccessReceiptCases['unsupported schema'] = $unsupportedSchemaReceipt | ConvertTo-Json -Compress
        $failedStatusReceipt = $successReceiptJson | ConvertFrom-Json -AsHashtable
        $failedStatusReceipt.Valid = $false
        $invalidSuccessReceiptCases['failed status'] = $failedStatusReceipt | ConvertTo-Json -Compress
        $wrongTypeReceipt = $successReceiptJson | ConvertFrom-Json -AsHashtable
        $wrongTypeReceipt.CoreTests = '11'
        $invalidSuccessReceiptCases['wrong type'] = $wrongTypeReceipt | ConvertTo-Json -Compress
        $invalidRangeReceipt = $successReceiptJson | ConvertFrom-Json -AsHashtable
        $invalidRangeReceipt.RuntimeProcessId = 0
        $invalidSuccessReceiptCases['invalid range'] = $invalidRangeReceipt | ConvertTo-Json -Compress
        $invalidStateReceipt = $successReceiptJson | ConvertFrom-Json -AsHashtable
        $invalidStateReceipt.AcceptanceState = 'accepted'
        $invalidSuccessReceiptCases['invalid state'] = $invalidStateReceipt | ConvertTo-Json -Compress
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
        Assert-Equal 14 @($persistedSuccessReceipt.PSObject.Properties).Count 'Success receipt writer returns the normalized exact schema'
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
            TaskState = 'Running'
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
        }
        $validReadbackJson = $validReadback | ConvertTo-Json -Compress
        $normalizedReadback = ConvertFrom-CountdownRemoteDeploymentReadbackJson `
            -Json $validReadbackJson `
            -ExpectedProcessId 4242 `
            -ExpectedExecutableSha256 $expectedReadbackExecutableHash `
            -ExpectedTokenSha256 ('E' * 64) `
            -ExpectedSettingsSha256 $expectedReadbackSettingsHash
        Assert-Equal 31 @($normalizedReadback.PSObject.Properties).Count 'Independent readback parser returns the exact schema field set'
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
        $readbackTaskState.TaskState = 'Ready'
        $invalidReadbackCases['task state'] = $readbackTaskState | ConvertTo-Json -Compress
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
        Assert-Equal 31 @($persistedReadback.PSObject.Properties).Count 'Independent readback writer returns the normalized exact schema'
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

        $validationFailureFixture = New-CountdownRemoteDeploymentFixtureAdapter -Mode ValidationFailure
        $validationFailureMessage = $null
        try {
            Invoke-CountdownRemoteDeployment `
                -SourceRoot $PSScriptRoot `
                -InstallPath $remoteDeploymentInstall `
                -PowerShell7Path $remoteDeploymentPowerShell `
                -DeferredReceiptPath $remoteDeploymentReceipt `
                -TaskName 'Minmin Countdown Fixture' `
                -LegacyTaskName '敏敏子倒计时 Fixture' `
                -Adapter $validationFailureFixture.Adapter | Out-Null
        }
        catch {
            $validationFailureMessage = $_.Exception.Message
        }
        Assert-Equal $true ($validationFailureMessage -match '安装器内置校验未通过') 'Remote deployment rejects a failed stable validation result'
        Assert-Equal $false ([bool] $validationFailureFixture.State.Accepted) 'Validation failure does not enter acceptance'
        Assert-Equal $true ([bool] $validationFailureFixture.State.RolledBack) 'Validation failure rolls back the candidate'
        Assert-Equal $true ([bool] $validationFailureFixture.State.Disposed) 'Validation failure releases its host adapter'

        $adapterUiValidationFailureFixture = New-CountdownRemoteDeploymentFixtureAdapter -Mode AdapterUiValidationFailure
        $adapterUiValidationFailureMessage = $null
        try {
            Invoke-CountdownRemoteDeployment `
                -SourceRoot $remoteDeploymentSource `
                -InstallPath $remoteDeploymentInstall `
                -PowerShell7Path $remoteDeploymentPowerShell `
                -DeferredReceiptPath $remoteDeploymentReceipt `
                -TaskName 'Minmin Countdown Fixture' `
                -LegacyTaskName '敏敏子倒计时 Fixture' `
                -Adapter $adapterUiValidationFailureFixture.Adapter | Out-Null
        }
        catch {
            $adapterUiValidationFailureMessage = $_.Exception.Message
        }
        Assert-Equal $true ($adapterUiValidationFailureMessage -match 'adapter UI 验证未通过') 'Remote deployment rejects missing adapter UI evidence before acceptance'
        Assert-Equal $false ([bool] $adapterUiValidationFailureFixture.State.Accepted) 'Adapter UI validation failure does not enter acceptance'
        Assert-Equal $true ([bool] $adapterUiValidationFailureFixture.State.RolledBack) 'Adapter UI validation failure rolls back the candidate'

        $validationSchemaFailureFixture = New-CountdownRemoteDeploymentFixtureAdapter -Mode ValidationSchemaFailure
        $validationSchemaFailureMessage = $null
        try {
            Invoke-CountdownRemoteDeployment `
                -SourceRoot $remoteDeploymentSource `
                -InstallPath $remoteDeploymentInstall `
                -PowerShell7Path $remoteDeploymentPowerShell `
                -DeferredReceiptPath $remoteDeploymentReceipt `
                -TaskName 'Minmin Countdown Fixture' `
                -LegacyTaskName '敏敏子倒计时 Fixture' `
                -Adapter $validationSchemaFailureFixture.Adapter | Out-Null
        }
        catch {
            $validationSchemaFailureMessage = $_.Exception.Message
        }
        Assert-Equal $true ($validationSchemaFailureMessage -match 'validation interface schemaVersion 不受支持') 'Remote deployment rejects an unknown validation interface version'
        Assert-Equal $false ([bool] $validationSchemaFailureFixture.State.Accepted) 'Validation schema failure does not enter acceptance'
        Assert-Equal $true ([bool] $validationSchemaFailureFixture.State.RolledBack) 'Validation schema failure rolls back the candidate'

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
        Assert-Equal $true ($rollbackFailureMessage -match '安装器内置校验未通过.*回滚也失败.*模拟回滚失败') 'Remote deployment preserves both primary and rollback failures'
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

        $validationAndDisposeFailureFixture = New-CountdownRemoteDeploymentFixtureAdapter -Mode ValidationAndDisposeFailure
        $validationAndDisposeFailureMessage = $null
        try {
            Invoke-CountdownRemoteDeployment `
                -SourceRoot $remoteDeploymentSource `
                -InstallPath $remoteDeploymentInstall `
                -PowerShell7Path $remoteDeploymentPowerShell `
                -DeferredReceiptPath $remoteDeploymentReceipt `
                -TaskName 'Minmin Countdown Fixture' `
                -LegacyTaskName '敏敏子倒计时 Fixture' `
                -Adapter $validationAndDisposeFailureFixture.Adapter | Out-Null
        }
        catch {
            $validationAndDisposeFailureMessage = $_.Exception.Message
        }
        Assert-Equal $true ($validationAndDisposeFailureMessage -match '已回滚.*安装器内置校验未通过.*资源释放也失败.*模拟资源释放失败') 'Remote deployment preserves its primary failure when adapter disposal also fails'
        Assert-Equal $true ([bool] $validationAndDisposeFailureFixture.State.RolledBack) 'Validation and disposal failure still rolls back the candidate'
        Assert-Equal $true ([bool] $validationAndDisposeFailureFixture.State.Disposed) 'Validation and disposal failure attempts adapter disposal'

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
        $windowsAdapter.State.LauncherPath = Join-Path $remoteDeploymentTestRoot 'desktop-launcher.exe'
        $windowsAdapter.State.LegacyLauncherPath = Join-Path $remoteDeploymentTestRoot 'legacy-launcher.cmd'
        $windowsAdapter.State.LauncherTempPath = Join-Path $remoteDeploymentTestRoot 'desktop-launcher.exe.new'
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
        $preRollbackCaptureCallCount = [regex]::Matches($remoteDeploymentControllerSource, '(?m)^\s*\$preRollbackCapture = Write-CountdownPreRollbackDiagnostics\b').Count
        $preRollbackCaptureIndex = $remoteDeploymentControllerSource.IndexOf('$preRollbackCapture = Write-CountdownPreRollbackDiagnostics')
        $rollbackInvocationIndex = $remoteDeploymentControllerSource.IndexOf('$rollback = & $Adapter.Rollback', $preRollbackCaptureIndex)
        Assert-Equal 1 $preRollbackCaptureCallCount 'Remote deployment invokes pre-rollback diagnostics capture exactly once'
        Assert-Equal $true ($preRollbackCaptureIndex -ge 0 -and $rollbackInvocationIndex -gt $preRollbackCaptureIndex) 'Remote deployment captures diagnostics before invoking rollback'

        $officeDeploymentSource = [System.IO.File]::ReadAllText((Join-Path $PSScriptRoot 'Deploy-OfficeCountdown.ps1'), [System.Text.Encoding]::UTF8)
        Assert-Equal 4 ([regex]::Matches($officeDeploymentSource, '\bInvoke-CountdownRecordedSshRead\b').Count) 'Office deployment records both failure reads, the deployment binding, and the post-deployment independent readback'
        Assert-Equal $true ($officeDeploymentSource.Contains("-Id 'pre-rollback-diagnostics'") -and $officeDeploymentSource.Contains('pre-rollback-diagnostics.json')) 'Office deployment gives the sidecar a stable ledger and evidence identity'
        $preRollbackRemoteReaderIndex = $officeDeploymentSource.IndexOf('function Get-OfficeRemotePreRollbackDiagnostics')
        $preRollbackRemoteReaderEnd = if ($preRollbackRemoteReaderIndex -ge 0) { $officeDeploymentSource.IndexOf('function Get-OfficePostDeploymentIndependentReadback', $preRollbackRemoteReaderIndex) } else { -1 }
        $preRollbackRemoteReaderSource = if ($preRollbackRemoteReaderIndex -ge 0 -and $preRollbackRemoteReaderEnd -gt $preRollbackRemoteReaderIndex) {
            $officeDeploymentSource.Substring($preRollbackRemoteReaderIndex, $preRollbackRemoteReaderEnd - $preRollbackRemoteReaderIndex)
        }
        else { '' }
        Assert-Equal $true ($preRollbackRemoteReaderSource.Contains('ReparsePoint') -and $preRollbackRemoteReaderSource.Contains('32768') -and $preRollbackRemoteReaderSource.Contains('ConvertFrom-CountdownPreRollbackDiagnosticsBytes')) 'Office sidecar reader enforces non-reparse, 32 KiB remote and strict local parser gates'
        $independentReadbackReaderIndex = $officeDeploymentSource.IndexOf('function Get-OfficePostDeploymentIndependentReadback')
        $independentReadbackReaderEnd = if ($independentReadbackReaderIndex -ge 0) { $officeDeploymentSource.IndexOf('function Invoke-CountdownDeploymentGit', $independentReadbackReaderIndex) } else { -1 }
        $independentReadbackReaderSource = if ($independentReadbackReaderIndex -ge 0 -and $independentReadbackReaderEnd -gt $independentReadbackReaderIndex) {
            $officeDeploymentSource.Substring($independentReadbackReaderIndex, $independentReadbackReaderEnd - $independentReadbackReaderIndex)
        }
        else { '' }
        Assert-Equal $true (
            $independentReadbackReaderSource.Contains('$env:SSH_CONNECTION') -and
            $independentReadbackReaderSource.Contains('[System.Security.Principal.WindowsIdentity]::GetCurrent().Name') -and
            $independentReadbackReaderSource.Contains("Get-ScheduledTask -TaskName ([string] `$expected.TaskName)")
        ) 'Independent readback binds the remote host, Windows user, and exact scheduled task'
        Assert-Equal $true (
            $independentReadbackReaderSource.Contains("TaskName = 'Minmin Countdown'") -and
            $independentReadbackReaderSource.Contains('TaskArguments = ''-NoLogo -NoProfile -ExecutionPolicy RemoteSigned -WindowStyle Hidden -STA -File') -and
            $independentReadbackReaderSource.Contains("Join-Path `$PSHOME 'pwsh.exe'") -and
            $independentReadbackReaderSource.Contains('TaskWorkingDirectoryMatch')
        ) 'Independent readback requires the managed PowerShell 7 STA thin-adapter task action'
        Assert-Equal $true (
            $independentReadbackReaderSource.Contains("'startup-request.json'") -and
            $independentReadbackReaderSource.Contains("'runtime-status.json'") -and
            $independentReadbackReaderSource.Contains("'acceptance-status.json'") -and
            $independentReadbackReaderSource.Contains('$expected.DeploymentBindingPath') -and
            $independentReadbackReaderSource.Contains("'finalized-ready'") -and
            $independentReadbackReaderSource.Contains('Get-ReadbackSha256Text -Text $tokens[0]') -and
            $independentReadbackReaderSource.Contains('$binding.TokenSha256') -and
            $independentReadbackReaderSource.Contains('$expected.ExpectedTokenSha256') -and
            -not $independentReadbackReaderSource.Contains('LaunchToken = $tokens')
        ) 'Independent readback cross-checks all three receipts and the deployment binding while persisting only the token hash and match result'
        Assert-Equal $true (
            $independentReadbackReaderSource.Contains('Get-Process -Id $runtimeProcessId') -and
            $independentReadbackReaderSource.Contains("ExecutablePath = 'D:\敏敏子倒计时\app\MinminCountdown.exe'") -and
            $independentReadbackReaderSource.Contains("Join-Path ([string] `$expected.InstallPath) 'settings.json'") -and
            $independentReadbackReaderSource.Contains('ConvertFrom-CountdownRemoteDeploymentReadbackJson')
        ) 'Independent readback verifies the live PID, interactive session, process path, EXE hash, settings hash, and local schema before ledger success'
        Assert-Equal $true (
            $independentReadbackReaderSource.Contains('[System.IO.Compression.GZipStream]') -and
            $independentReadbackReaderSource.Contains('$remoteCommand.Length -gt 30000')
        ) 'Independent readback compresses the in-memory SSH command and fails closed before the Windows command-line limit'
        Assert-Equal $true (
            $officeDeploymentSource.Contains("'-o', 'BatchMode=yes'") -and
            $officeDeploymentSource.Contains("'-o', 'IdentitiesOnly=yes'") -and
            $officeDeploymentSource.Contains("'-o', 'StrictHostKeyChecking=yes'") -and
            $officeDeploymentSource.Contains('UserKnownHostsFile=$env:REPLICATOR_OFFICE_SSH_USER_KNOWN_HOSTS_FILE')
        ) 'Independent readback reuses the strict managed office SSH options'

        $callbackScopeLedger = Join-Path $remoteDeploymentTestRoot 'callback-scope-transfer-attempts.json'
        $callbackScopeResult = & {
            . (Join-Path $PSScriptRoot 'Deploy-OfficeCountdown.ps1') -LoadFunctionsOnly
            Import-Module (Join-Path $PSScriptRoot 'Countdown.TransferLedger.psm1') -Force

            function Get-OfficeRemoteDeploymentBinding {
                [pscustomobject]@{
                    Success = $true
                    ExitCode = 0
                    Bytes = [Text.UTF8Encoding]::new($false).GetBytes('binding')
                    FailureMessage = $null
                }
            }
            function Get-OfficePostDeploymentIndependentReadback {
                [pscustomobject]@{
                    Success = $true
                    ExitCode = 0
                    Bytes = [Text.UTF8Encoding]::new($false).GetBytes('readback')
                    FailureMessage = $null
                }
            }
            function Write-CountdownRemoteDeploymentBindingReceipt {
                param($Path, $Bytes, $ExpectedDeploymentBindingSha256, $ExpectedProcessId)
                [pscustomobject]@{ TokenSha256 = ('A' * 64) }
            }
            function Write-CountdownRemoteDeploymentReadbackReceipt {
                param(
                    $Path,
                    $Bytes,
                    $ExpectedProcessId,
                    $ExpectedExecutableSha256,
                    $ExpectedTokenSha256,
                    $ExpectedSettingsSha256
                )
                [pscustomobject]@{ Valid = $true }
            }

            Invoke-OfficePostDeploymentIndependentVerification `
                -LedgerPath $callbackScopeLedger `
                -DeploymentBindingEvidencePath (Join-Path $remoteDeploymentTestRoot 'binding.json') `
                -IndependentReadbackEvidencePath (Join-Path $remoteDeploymentTestRoot 'readback.json') `
                -RemoteDeploymentBindingPath 'D:\fixture\deployment-binding.json' `
                -DeploymentBindingSha256 ('B' * 64) `
                -SuccessReceipt ([pscustomobject]@{
                        RuntimeProcessId = 1234
                        SettingsHashAfter = ('C' * 64)
                    }) `
                -ExpectedExecutableSha256 ('D' * 64) `
                -ExpectedRemoteHost 'fixture-host' `
                -ExpectedRemoteUser 'fixture-user' `
                -RollbackAction { throw 'Committed readback must never roll back.' }

            $ledger = Get-Content -LiteralPath $callbackScopeLedger -Raw | ConvertFrom-Json
            [pscustomobject]@{
                Attempts = @($ledger.attempts).Count
                AllSucceeded = @($ledger.attempts | Where-Object status -ne 'succeeded').Count -eq 0
            }
        }
        Assert-Equal 2 $callbackScopeResult.Attempts 'Committed independent readback executes both readers across the transfer module scope'
        Assert-Equal $true $callbackScopeResult.AllSucceeded 'Committed independent readback records both module-scope callbacks as succeeded'
    }
    finally {
        Import-Module (Join-Path $PSScriptRoot 'Countdown.Settings.psm1') -Force
        Import-Module (Join-Path $PSScriptRoot 'Countdown.Runtime.psm1') -Force
        Import-Module (Join-Path $PSScriptRoot 'Countdown.InstallTransaction.psm1') -Force -DisableNameChecking
        Import-Module (Join-Path $PSScriptRoot 'Countdown.TransactionAcceptance.psm1') -Force
        Remove-CountdownIsolatedTestTree -Path $remoteDeploymentTestRoot -AllowedRoot $remoteDeploymentEvidenceRoot
    }

    if ($RemoteDeploymentOnly) {
        if ($failures.Count -gt 0) {
            $failures | ForEach-Object { Write-Error $_ }
            exit 1
        }
        [pscustomobject]@{ Passed = $script:assertions; Failed = 0; Scope = 'RemoteDeployment' } | ConvertTo-Json
        exit 0
    }
}

if (-not $AcceptanceOnly) {
$releaseContract = Get-CountdownReleaseContract
$expectedBuildAssets = @(
    'sanrio-official-mymelody.png=>app\assets\sanrio-official-mymelody.png',
    'sanrio-official-cinnamoroll.png=>app\assets\sanrio-official-cinnamoroll.png',
    'sanrio-official-kuromi.png=>app\assets\sanrio-official-kuromi.png',
    'assets\floating-plush\floating-plush-mymelody-reference.png=>app\assets\floating-plush\floating-plush-mymelody-reference.png',
    'assets\floating-plush\floating-plush-cinnamoroll-reference.png=>app\assets\floating-plush\floating-plush-cinnamoroll-reference.png',
    'assets\floating-plush\floating-plush-kuromi-reference.png=>app\assets\floating-plush\floating-plush-kuromi-reference.png',
    'assets\floating-plush\component-day-outer-shell.png=>app\assets\floating-plush\component-day-outer-shell.png',
    'assets\floating-plush\component-day-header-blank.png=>app\assets\floating-plush\component-day-header-blank.png',
    'assets\floating-plush\component-day-card-blank.png=>app\assets\floating-plush\component-day-card-blank.png',
    'assets\floating-plush\component-day-mascots.png=>app\assets\floating-plush\component-day-mascots.png',
    'assets\main-decorations\main-countdown-scene-day.png=>app\assets\main-decorations\main-countdown-scene-day.png',
    'assets\main-decorations\main-notes-scene-day.png=>app\assets\main-decorations\main-notes-scene-day.png',
    'assets\main-decorations\main-countdown-top-night.png=>app\assets\main-decorations\main-countdown-top-night.png',
    'assets\main-decorations\main-notes-top-night.png=>app\assets\main-decorations\main-notes-top-night.png'
)
$actualBuildAssets = @($releaseContract.BuildAssets | ForEach-Object { "$($_.SourcePath)=>$($_.PublishedPath)" })
$expectedCompatibilityFixtures = @(
    'Countdown.App.psm1',
    'sanrio-official-mymelody.png',
    'sanrio-official-cinnamoroll.png',
    'sanrio-official-kuromi.png',
    'assets\floating-plush\floating-plush-mymelody-front.png',
    'assets\floating-plush\floating-plush-mymelody-pose.png',
    'assets\floating-plush\floating-plush-cinnamoroll-front.png',
    'assets\floating-plush\floating-plush-cinnamoroll-pose.png',
    'assets\floating-plush\floating-plush-kuromi-front.png',
    'assets\floating-plush\floating-plush-kuromi-pose.png'
)

Assert-Equal 1 $releaseContract.ContractVersion 'Release contract exposes version one'
Assert-Equal 53 @($releaseContract.PackageFiles).Count 'Release contract declares every package file including its trust roots'
Assert-Equal 50 $releaseContract.IntegrityHashes.Count 'Release contract protects every non-root package file including deployment modules'
Assert-Equal 14 @($releaseContract.BuildAssets).Count 'Release contract declares fourteen explicit application asset mappings'
Assert-Equal 27 @($releaseContract.InstalledFiles).Count 'Release contract preserves the installed runtime payload'
Assert-Equal 20 @($releaseContract.RetiredRuntimeFiles).Count 'Release contract preserves the retired runtime cleanup set'
Assert-Equal ($expectedBuildAssets -join ',') ($actualBuildAssets -join ',') 'Release contract exposes the exact build asset mappings'
Assert-Equal ($expectedCompatibilityFixtures -join ',') (@($releaseContract.CompatibilityFixtureFiles) -join ',') 'Release contract exposes the exact compatibility fixtures'
Assert-Equal $true ('Countdown.TransactionAcceptance.psm1' -in @($releaseContract.PackageFiles)) 'Transaction acceptance is packaged'
Assert-Equal $true $releaseContract.IntegrityHashes.Contains('Countdown.TransactionAcceptance.psm1') 'Transaction acceptance is integrity protected'
Assert-Equal $false ('Countdown.TransactionAcceptance.psm1' -in @($releaseContract.InstalledFiles)) 'Transaction acceptance is not installed as runtime'
foreach ($deploymentModule in @('Countdown.Validation.psm1', 'Countdown.RemoteDeployment.psm1', 'Countdown.TransferLedger.psm1')) {
    Assert-Equal $true ($deploymentModule -in @($releaseContract.PackageFiles)) "Deployment module is packaged: $deploymentModule"
    Assert-Equal $true $releaseContract.IntegrityHashes.Contains($deploymentModule) "Deployment module is integrity protected: $deploymentModule"
    Assert-Equal $false ($deploymentModule -in @($releaseContract.InstalledFiles)) "Deployment module is not installed as runtime: $deploymentModule"
}
foreach ($trustRoot in @('Deploy-OfficeCountdown.ps1', 'Install-Countdown.ps1', 'Countdown.ReleaseContract.psm1')) {
    Assert-Equal $true ($trustRoot -in @($releaseContract.PackageTrustRoots)) "Release contract declares trust root $trustRoot"
    Assert-Equal $false $releaseContract.IntegrityHashes.Contains($trustRoot) "Release contract does not self-hash trust root $trustRoot"
}
$deploySource = [System.IO.File]::ReadAllText(
    (Join-Path $PSScriptRoot 'Deploy-OfficeCountdown.ps1'),
    [System.Text.Encoding]::UTF8)
$deployCommitGateIndex = $deploySource.IndexOf("-Stage '构建前'")
$deploySnapshotExportIndex = if ($deployCommitGateIndex -ge 0) {
    $deploySource.IndexOf('Export-CountdownDeploymentCommitSnapshot', $deployCommitGateIndex)
}
else { -1 }
$deployPackageAssertionIndex = $deploySource.IndexOf('$packageValidation = Assert-CountdownReleasePackage -Root $sourceRoot')
$deployValidationExitIndex = $deploySource.IndexOf('if ($ValidateOnly) {')
$transferLedgerImportIndex = $deploySource.IndexOf("-Stage '传输账本模块导入'")
$credentialImportIndex = $deploySource.IndexOf(". 'D:\Codex\.codex\credentials\ssh\replicator\office\Use-OfficeSshHelper.ps1'")
Assert-Equal $true (
    $deployCommitGateIndex -ge 0 -and
    $deploySnapshotExportIndex -gt $deployCommitGateIndex -and
    $deployPackageAssertionIndex -gt $deployCommitGateIndex -and
    $deployValidationExitIndex -gt $deployPackageAssertionIndex -and
    $transferLedgerImportIndex -gt $deployValidationExitIndex -and
    $credentialImportIndex -gt $transferLedgerImportIndex
) 'Office deploy snapshots the verified commit and imports transfer code only after Git, package hash, and validate-only gates'
$deploymentTokens = $null
$deploymentParseErrors = $null
$deploymentAst = [System.Management.Automation.Language.Parser]::ParseInput(
    $deploySource,
    [ref] $deploymentTokens,
    [ref] $deploymentParseErrors)
Assert-Equal 0 $deploymentParseErrors.Count 'Office deployment script parses before gate fixtures are extracted'
$directGitCommands = @(
    $deploymentAst.FindAll({
            param($node)
            if ($node -isnot [System.Management.Automation.Language.CommandAst]) { return $false }
            $commandName = $node.GetCommandName()
            $commandName -in @('git', 'git.exe')
        }, $true)
)
Assert-Equal 0 $directGitCommands.Count 'Office deployment has no Git command outside the checked wrapper invocation'
$deploymentGitWrappers = @(
    $deploymentAst.FindAll({
            param($node)
            $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and
            $node.Name -eq 'Invoke-CountdownDeploymentGit'
        }, $true)
)
$deploymentGitProcessInvocations = @(
    $deploymentAst.FindAll({
            param($node)
            $node -is [System.Management.Automation.Language.CommandAst] -and
            $node.InvocationOperator -eq [System.Management.Automation.Language.TokenKind]::Ampersand -and
            $node.CommandElements.Count -gt 0 -and
            $node.CommandElements[0] -is [System.Management.Automation.Language.VariableExpressionAst] -and
            $node.CommandElements[0].VariablePath.UserPath -eq 'GitPath'
        }, $true)
)
Assert-Equal 1 $deploymentGitWrappers.Count 'Office deployment defines one checked Git wrapper'
Assert-Equal 1 $deploymentGitProcessInvocations.Count 'Office deployment starts Git only once in the checked wrapper'
$deploymentGitWrapper = if ($deploymentGitWrappers.Count -eq 1) { $deploymentGitWrappers[0] } else { $null }
$deploymentGitProcessInvocation = if ($deploymentGitProcessInvocations.Count -eq 1) { $deploymentGitProcessInvocations[0] } else { $null }
$deploymentGitInvocationOwnedByWrapper = $null -ne $deploymentGitWrapper -and
    $null -ne $deploymentGitProcessInvocation -and
    $deploymentGitProcessInvocation.Extent.StartOffset -ge $deploymentGitWrapper.Extent.StartOffset -and
    $deploymentGitProcessInvocation.Extent.EndOffset -le $deploymentGitWrapper.Extent.EndOffset
Assert-Equal $true $deploymentGitInvocationOwnedByWrapper 'The checked wrapper owns the only real Git process invocation'
$deploymentGitInvocationAssignment = if ($null -ne $deploymentGitProcessInvocation) {
    $candidate = $deploymentGitProcessInvocation
    while ($null -ne $candidate -and
        $candidate -isnot [System.Management.Automation.Language.AssignmentStatementAst]) {
        $candidate = $candidate.Parent
    }
    $candidate
}
else { $null }
$deploymentGitInvocationBlock = if ($null -ne $deploymentGitInvocationAssignment -and
    $deploymentGitInvocationAssignment.Parent -is [System.Management.Automation.Language.StatementBlockAst]) {
    $deploymentGitInvocationAssignment.Parent
}
else { $null }
$deploymentGitInvocationStatements = if ($null -ne $deploymentGitInvocationBlock) {
    @($deploymentGitInvocationBlock.Statements)
}
else { @() }
$deploymentGitInvocationStatementIndex = if ($null -ne $deploymentGitInvocationAssignment) {
    [Array]::IndexOf($deploymentGitInvocationStatements, $deploymentGitInvocationAssignment)
}
else { -1 }
$deploymentGitExitCodeStatement = if ($deploymentGitInvocationStatementIndex -ge 0 -and
    $deploymentGitInvocationStatementIndex + 1 -lt $deploymentGitInvocationStatements.Count) {
    $deploymentGitInvocationStatements[$deploymentGitInvocationStatementIndex + 1]
}
else { $null }
$deploymentGitExitCodeText = if ($null -ne $deploymentGitExitCodeStatement) {
    $deploymentGitExitCodeStatement.Extent.Text.Trim()
}
else { '' }
Assert-Equal '$exitCode = $LASTEXITCODE' $deploymentGitExitCodeText 'Office deployment captures the real Git exit code in the immediately following statement'
Assert-Equal $true (
    $deploySource.Contains("@('status', '--porcelain=v1', '--untracked-files=all')") -and
    $deploySource.Contains("@('branch', '--show-current')") -and
    $deploySource.Contains("@('rev-parse', '--verify', 'HEAD')") -and
    $deploySource.Contains("@('rev-parse', '--verify', 'refs/remotes/origin/main')")
) 'Office deployment repository gate checks clean tree, branch, HEAD, and fetched origin/main'
foreach ($stage in @('构建前', '构建后', '打包前', '打包后', '远程输出前')) {
    Assert-Equal $true $deploySource.Contains("-Stage '$stage'") "Office deployment runs repository gate at $stage"
}
Assert-Equal $true (
    $deploySource.Contains("@('archive', '--format=zip', `"--output=`$DestinationPath`", `$Commit)") -and
    $deploySource.Contains('Copy-Item -LiteralPath (Join-Path $sourceRoot $file)') -and
    -not $deploySource.Contains('Copy-Item -LiteralPath (Join-Path $repoRoot $file)') -and
    $deploySource.Contains("Assert-CountdownDeploymentFileManifest -Root `$sourceRoot -Manifest `$packageManifest -Stage '打包后'") -and
    $deploySource.Contains("Assert-CountdownDeploymentFileManifest -Root `$extractRoot -Manifest `$packageManifest -Stage '远程输出前'")
) 'Office deployment builds package bytes only from the verified commit snapshot'

$deploymentGateFunctionNames = @(
    'Invoke-CountdownDeploymentGit',
    'Get-CountdownDeploymentGitScalar',
    'Assert-CountdownDeploymentRepositoryState',
    'Export-CountdownDeploymentCommitSnapshot',
    'Resolve-CountdownDeploymentSnapshotPath',
    'Get-CountdownDeploymentCommitFileIdentity',
    'New-CountdownDeploymentCommitIdentityManifest',
    'Invoke-CountdownDeploymentLockedCommitFile',
    'New-CountdownDeploymentCommitFileManifest',
    'New-CountdownDeploymentFileManifest',
    'Assert-CountdownDeploymentFileManifest',
    'Assert-CountdownDeploymentFileSet',
    'Assert-CountdownRemoteInstallScript'
)
$deploymentGateFunctions = @(
    $deploymentAst.FindAll({
            param($node)
            $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and
            $node.Name -in $deploymentGateFunctionNames
        }, $true)
)
Assert-Equal $deploymentGateFunctionNames.Count $deploymentGateFunctions.Count 'Office deployment exposes every gate function required by isolated fixtures'
$deploymentGateEvidenceRoot = 'D:\Codex\.codex\run-evidence\clock-countdown-deployment-gate-tests'
$deploymentGateTestRoot = Join-Path $deploymentGateEvidenceRoot ([guid]::NewGuid().ToString('N'))
$deploymentGateModulePath = Join-Path $deploymentGateTestRoot 'Countdown.DeploymentGate.Fixture.psm1'
try {
    New-Item -ItemType Directory -Path $deploymentGateTestRoot -Force | Out-Null
    $deploymentGateModuleSource = "Set-StrictMode -Version Latest`r`n`$ErrorActionPreference = 'Stop'`r`n" +
        (($deploymentGateFunctions | ForEach-Object { $_.Extent.Text }) -join "`r`n`r`n")
    [System.IO.File]::WriteAllText(
        $deploymentGateModulePath,
        $deploymentGateModuleSource,
        [System.Text.UTF8Encoding]::new($false))
    Import-Module $deploymentGateModulePath -Force

    $validRemoteInstallScript = @(
        "Invoke-LockedDeploymentFile -Path 'archive.zip' -ExpectedSha256 '$('A' * 64)' -Action { param(`$lockedPath) `$lockedPath }",
        "Invoke-LockedDeploymentFile -Path 'release.psm1' -ExpectedSha256 '$('B' * 64)' -Action { param(`$lockedPath) `$lockedPath }",
        "Invoke-LockedDeploymentFile -Path 'remote.psm1' -ExpectedSha256 '$('C' * 64)' -Action { param(`$lockedPath) `$lockedPath }"
    ) -join "`r`n"
    $validRemoteInstallResult = Assert-CountdownRemoteInstallScript -ScriptText $validRemoteInstallScript
    Assert-Equal 3 $validRemoteInstallResult.InvocationCount 'Generated remote install script keeps every locked file parameter on its command invocation'

    $brokenRemoteInstallScript = @(
        "Invoke-LockedDeploymentFile -Path 'archive.zip' -ExpectedSha256 '$('A' * 64)' -Action { param(`$lockedPath) `$lockedPath }",
        'Invoke-LockedDeploymentFile',
        "    -Path 'release.psm1' -ExpectedSha256 '$('B' * 64)' -Action { param(`$lockedPath) `$lockedPath }",
        'Invoke-LockedDeploymentFile',
        "    -Path 'remote.psm1' -ExpectedSha256 '$('C' * 64)' -Action { param(`$lockedPath) `$lockedPath }"
    ) -join "`r`n"
    $brokenRemoteInstallRejected = $false
    $brokenRemoteInstallMessage = ''
    try {
        Assert-CountdownRemoteInstallScript -ScriptText $brokenRemoteInstallScript | Out-Null
    }
    catch {
        $brokenRemoteInstallRejected = $true
        $brokenRemoteInstallMessage = $_.Exception.Message
    }
    Assert-Equal $true $brokenRemoteInstallRejected 'Generated remote install script rejects a bare locked file command before upload'
    Assert-Equal $true $brokenRemoteInstallMessage.Contains('缺少参数') 'Generated remote install script reports the missing mandatory parameter set'

    foreach ($missingValueParameter in @('Path', 'ExpectedSha256', 'Action')) {
        $missingValueInvocation = switch ($missingValueParameter) {
            'Path' { "Invoke-LockedDeploymentFile -Path -ExpectedSha256 '$('B' * 64)' -Action { param(`$lockedPath) `$lockedPath }" }
            'ExpectedSha256' { "Invoke-LockedDeploymentFile -Path 'release.psm1' -ExpectedSha256 -Action { param(`$lockedPath) `$lockedPath }" }
            'Action' { "Invoke-LockedDeploymentFile -Path 'release.psm1' -ExpectedSha256 '$('B' * 64)' -Action" }
        }
        $missingValueScript = @(
            "Invoke-LockedDeploymentFile -Path 'archive.zip' -ExpectedSha256 '$('A' * 64)' -Action { param(`$lockedPath) `$lockedPath }",
            $missingValueInvocation,
            "Invoke-LockedDeploymentFile -Path 'remote.psm1' -ExpectedSha256 '$('C' * 64)' -Action { param(`$lockedPath) `$lockedPath }"
        ) -join "`r`n"
        $missingValueRejected = $false
        try {
            Assert-CountdownRemoteInstallScript -ScriptText $missingValueScript | Out-Null
        }
        catch {
            $missingValueRejected = $_.Exception.Message -match "参数 $missingValueParameter 缺少值"
        }
        Assert-Equal $true $missingValueRejected "Generated remote install script rejects a $missingValueParameter parameter without a value"
    }

    $fixtureCommit = '1111111111111111111111111111111111111111'
    $deploymentGitFixture = [pscustomobject]@{
        FailCommand = ''
        FailExitCode = 0
        Dirty = $false
        Branch = 'main'
        Head = $fixtureCommit
        Remote = $fixtureCommit
    }
    $deploymentGitAdapter = {
        param($RepositoryRoot, $Arguments)

        $command = [string] $Arguments[0]
        if ($deploymentGitFixture.FailCommand -eq $command) {
            return [pscustomobject]@{ ExitCode = $deploymentGitFixture.FailExitCode; Output = @() }
        }
        $output = switch ($command) {
            'fetch' { @() }
            'status' { if ($deploymentGitFixture.Dirty) { @(' M changed-during-build.txt') } else { @() } }
            'branch' { @($deploymentGitFixture.Branch) }
            'rev-parse' {
                if ($Arguments[-1] -eq 'HEAD') { @($deploymentGitFixture.Head) }
                else { @($deploymentGitFixture.Remote) }
            }
            default { throw "Unexpected fixture Git command: $command" }
        }
        [pscustomobject]@{ ExitCode = 0; Output = @($output) }
    }.GetNewClosure()

    $deploymentGitFixture.FailCommand = 'status'
    $deploymentGitFixture.FailExitCode = 19
    $emptyGitFailureRejected = $false
    try {
        Assert-CountdownDeploymentRepositoryState -GitPath 'fixture-git.exe' -RepositoryRoot $deploymentGateTestRoot -Stage '失败夹具' -Adapter $deploymentGitAdapter | Out-Null
    }
    catch {
        $emptyGitFailureRejected = $_.Exception.Message -match '退出码 19' -and $_.Exception.Message -match '<no output>'
    }
    Assert-Equal $true $emptyGitFailureRejected 'Repository gate rejects a nonzero Git command with empty output'

    $deploymentGitFixture.FailCommand = ''
    $deploymentGitFixture.FailExitCode = 0
    $initialGate = Assert-CountdownDeploymentRepositoryState -GitPath 'fixture-git.exe' -RepositoryRoot $deploymentGateTestRoot -Stage '构建前夹具' -Adapter $deploymentGitAdapter
    Assert-Equal $fixtureCommit $initialGate.Head 'Repository gate accepts one clean pushed commit'

    $deploymentGitFixture.Remote = '2222222222222222222222222222222222222222'
    $remoteAdvanceRejected = $false
    try {
        Assert-CountdownDeploymentRepositoryState -GitPath 'fixture-git.exe' -RepositoryRoot $deploymentGateTestRoot -Stage '构建后夹具' -ExpectedCommit $fixtureCommit -Adapter $deploymentGitAdapter | Out-Null
    }
    catch {
        $remoteAdvanceRejected = $_.Exception.Message -match 'HEAD 与 origin/main 不一致'
    }
    Assert-Equal $true $remoteAdvanceRejected 'Repository gate rejects origin/main advancing after the initial gate'

    $deploymentGitFixture.Remote = $fixtureCommit
    $deploymentGitFixture.Dirty = $true
    $buildDirtyRejected = $false
    try {
        Assert-CountdownDeploymentRepositoryState -GitPath 'fixture-git.exe' -RepositoryRoot $deploymentGateTestRoot -Stage '构建后夹具' -ExpectedCommit $fixtureCommit -Adapter $deploymentGitAdapter | Out-Null
    }
    catch {
        $buildDirtyRejected = $_.Exception.Message -match '工作树不干净'
    }
    Assert-Equal $true $buildDirtyRejected 'Repository gate rejects a worktree dirtied during the build'

    $trustRootRepository = Join-Path $deploymentGateTestRoot 'commit-trust-root-repository'
    $trustedSnapshotRoot = Join-Path $deploymentGateTestRoot 'trusted-snapshot'
    $replacedSnapshotRoot = Join-Path $deploymentGateTestRoot 'replaced-snapshot'
    $trustRootArchive = Join-Path $deploymentGateTestRoot 'commit-trust-root.zip'
    $trustRootModulePath = Join-Path $trustRootRepository 'Countdown.ReleaseContract.psm1'
    $trustedModuleSource = @'
function Get-CountdownIssue4TrustRootFixtureValue { 'benign' }
Export-ModuleMember -Function Get-CountdownIssue4TrustRootFixtureValue
'@
    $maliciousModuleSource = @'
Set-Variable -Scope Global -Name CountdownIssue4MaliciousSentinel -Value 'executed'
function Get-CountdownIssue4TrustRootFixtureValue { 'malicious' }
Export-ModuleMember -Function Get-CountdownIssue4TrustRootFixtureValue
'@
    New-Item -ItemType Directory -Path $trustRootRepository -Force | Out-Null
    [System.IO.File]::WriteAllText($trustRootModulePath, $trustedModuleSource, [System.Text.UTF8Encoding]::new($false))
    $fixtureGitPath = 'fixture-git.exe'
    $trustRootCommit = '3333333333333333333333333333333333333333'
    $trustedBlobIdentity = '4444444444444444444444444444444444444444'
    $maliciousBlobIdentity = '5555555555555555555555555555555555555555'
    $trustRootGitAdapter = {
        param($RepositoryRoot, $Arguments)

        $output = switch ([string] $Arguments[0]) {
            'rev-parse' { @($trustedBlobIdentity) }
            'hash-object' {
                $content = [System.IO.File]::ReadAllText([string] $Arguments[-1], [System.Text.Encoding]::UTF8)
                if ([string]::Equals($content, $trustedModuleSource, [System.StringComparison]::Ordinal)) {
                    @($trustedBlobIdentity)
                }
                else {
                    @($maliciousBlobIdentity)
                }
            }
            'archive' {
                $outputArgument = @($Arguments | Where-Object { ([string] $_).StartsWith('--output=', [System.StringComparison]::Ordinal) })
                if ($outputArgument.Count -ne 1) { throw 'Archive fixture did not receive one output path.' }
                $destinationPath = ([string] $outputArgument[0]).Substring('--output='.Length)
                Compress-Archive -LiteralPath $trustRootModulePath -DestinationPath $destinationPath -Force
                @()
            }
            default { throw "Unexpected trust-root fixture Git command: $($Arguments[0])" }
        }
        [pscustomobject]@{ ExitCode = 0; Output = @($output) }
    }.GetNewClosure()
    $trustRootIdentityManifest = New-CountdownDeploymentCommitIdentityManifest `
        -GitPath $fixtureGitPath `
        -RepositoryRoot $trustRootRepository `
        -Commit $trustRootCommit `
        -RelativePaths @('Countdown.ReleaseContract.psm1') `
        -Adapter $trustRootGitAdapter
    Export-CountdownDeploymentCommitSnapshot `
        -GitPath $fixtureGitPath `
        -RepositoryRoot $trustRootRepository `
        -Commit $trustRootCommit `
        -DestinationPath $trustRootArchive `
        -Adapter $trustRootGitAdapter
    Expand-Archive -LiteralPath $trustRootArchive -DestinationPath $trustedSnapshotRoot

    [System.IO.File]::WriteAllText($trustRootModulePath, $maliciousModuleSource, [System.Text.UTF8Encoding]::new($false))
    Compress-Archive -LiteralPath $trustRootModulePath -DestinationPath $trustRootArchive -Force
    Expand-Archive -LiteralPath $trustRootArchive -DestinationPath $replacedSnapshotRoot
    Remove-Variable -Name CountdownIssue4MaliciousSentinel -Scope Global -ErrorAction SilentlyContinue
    $replacedArchiveRejected = $false
    try {
        Invoke-CountdownDeploymentLockedCommitFile `
            -GitPath $fixtureGitPath `
            -RepositoryRoot $trustRootRepository `
            -Root $replacedSnapshotRoot `
            -RelativePath 'Countdown.ReleaseContract.psm1' `
            -ExpectedIdentity $trustRootIdentityManifest['Countdown.ReleaseContract.psm1'] `
            -Stage '首次发布契约夹具' `
            -Adapter $trustRootGitAdapter `
            -Action { param($lockedPath, $sha256) Import-Module $lockedPath -Force | Out-Null }
    }
    catch {
        $replacedArchiveRejected = $_.Exception.Message -match 'blob identity 不一致'
    }
    Assert-Equal $true $replacedArchiveRejected 'Commit snapshot replacement is rejected before the first release-contract code executes'
    Assert-Equal $false ($null -ne (Get-Variable -Name CountdownIssue4MaliciousSentinel -Scope Global -ErrorAction SilentlyContinue)) 'Rejected first trust root never executes its malicious sentinel'

    $maliciousSwapCandidate = Join-Path $deploymentGateTestRoot 'malicious-release-contract.psm1'
    $trustedSwapBackup = Join-Path $deploymentGateTestRoot 'trusted-release-contract.backup.psm1'
    [System.IO.File]::WriteAllText($maliciousSwapCandidate, $maliciousModuleSource, [System.Text.UTF8Encoding]::new($false))
    $swapState = [pscustomobject]@{
        Rejected = $false
        Succeeded = $false
        Restored = $false
        ImportedValue = ''
    }
    $lockedImportAction = {
        param($lockedPath, $sha256)

        try {
            [System.IO.File]::Replace($maliciousSwapCandidate, $lockedPath, $trustedSwapBackup, $true)
            $swapState.Succeeded = $true
            try {
                $fixtureModule = Import-Module $lockedPath -Force -PassThru
                $swapState.ImportedValue = & $fixtureModule { Get-CountdownIssue4TrustRootFixtureValue }
            }
            finally {
                [System.IO.File]::Replace($trustedSwapBackup, $lockedPath, $maliciousSwapCandidate, $true)
                $swapState.Restored = $true
            }
        }
        catch {
            if ($swapState.Succeeded) { throw }
            $swapState.Rejected = $true
            $fixtureModule = Import-Module $lockedPath -Force -PassThru
            $swapState.ImportedValue = & $fixtureModule { Get-CountdownIssue4TrustRootFixtureValue }
        }
    }.GetNewClosure()
    Invoke-CountdownDeploymentLockedCommitFile `
        -GitPath $fixtureGitPath `
        -RepositoryRoot $trustRootRepository `
        -Root $trustedSnapshotRoot `
        -RelativePath 'Countdown.ReleaseContract.psm1' `
        -ExpectedIdentity $trustRootIdentityManifest['Countdown.ReleaseContract.psm1'] `
        -Stage '发布契约导入窗口夹具' `
        -Adapter $trustRootGitAdapter `
        -Action $lockedImportAction
    Assert-Equal $true $swapState.Rejected 'Locked release-contract import rejects an atomic malicious replacement'
    Assert-Equal $false $swapState.Succeeded 'Malicious module cannot enter the verified import window'
    Assert-Equal $false $swapState.Restored 'Rejected replacement never needs a restore window'
    Assert-Equal 'benign' $swapState.ImportedValue 'Locked import executes the same benign bytes that passed Git identity verification'
    Assert-Equal $false ($null -ne (Get-Variable -Name CountdownIssue4MaliciousSentinel -Scope Global -ErrorAction SilentlyContinue)) 'Import-window replacement cannot execute its malicious sentinel'
    Remove-Module Countdown.ReleaseContract -Force -ErrorAction SilentlyContinue
    Import-Module (Join-Path $PSScriptRoot 'Countdown.ReleaseContract.psm1') -Force

    $controlFile = Join-Path $deploymentGateTestRoot 'Deploy-Control.ps1'
    [System.IO.File]::WriteAllText($controlFile, 'committed control', [System.Text.UTF8Encoding]::new($false))
    $controlManifestFixture = New-CountdownDeploymentFileManifest -Root $deploymentGateTestRoot -RelativePaths @('Deploy-Control.ps1')
    [System.IO.File]::WriteAllText($controlFile, 'changed control', [System.Text.UTF8Encoding]::new($false))
    $controlChangeRejected = $false
    try {
        Assert-CountdownDeploymentFileManifest -Root $deploymentGateTestRoot -Manifest $controlManifestFixture -Stage '打包前夹具'
    }
    catch {
        $controlChangeRejected = $_.Exception.Message -match '部署文件已变化'
    }
    Assert-Equal $true $controlChangeRejected 'Deployment gate rejects a changed control file'

    $snapshotRoot = Join-Path $deploymentGateTestRoot 'commit-snapshot'
    $activeRoot = Join-Path $deploymentGateTestRoot 'active-worktree'
    $fixturePackageRoot = Join-Path $deploymentGateTestRoot 'package'
    $fixtureExtractRoot = Join-Path $deploymentGateTestRoot 'package-check'
    New-Item -ItemType Directory -Path $snapshotRoot, $activeRoot, $fixturePackageRoot -Force | Out-Null
    [System.IO.File]::WriteAllText((Join-Path $snapshotRoot 'payload.txt'), 'committed payload', [System.Text.UTF8Encoding]::new($false))
    [System.IO.File]::WriteAllText((Join-Path $activeRoot 'payload.txt'), 'committed payload', [System.Text.UTF8Encoding]::new($false))
    $snapshotManifestFixture = New-CountdownDeploymentFileManifest -Root $snapshotRoot -RelativePaths @('payload.txt')
    [System.IO.File]::WriteAllText((Join-Path $activeRoot 'payload.txt'), 'concurrent worktree change', [System.Text.UTF8Encoding]::new($false))
    Copy-Item -LiteralPath (Join-Path $snapshotRoot 'payload.txt') -Destination (Join-Path $fixturePackageRoot 'payload.txt')
    $fixtureArchive = Join-Path $deploymentGateTestRoot 'fixture-package.zip'
    Compress-Archive -Path (Join-Path $fixturePackageRoot '*') -DestinationPath $fixtureArchive
    Expand-Archive -LiteralPath $fixtureArchive -DestinationPath $fixtureExtractRoot
    Assert-CountdownDeploymentFileSet -Root $fixtureExtractRoot -ExpectedFiles @('payload.txt') -Stage 'ZIP 夹具'
    Assert-CountdownDeploymentFileManifest -Root $fixtureExtractRoot -Manifest $snapshotManifestFixture -Stage 'ZIP 夹具'
    Assert-Equal 'committed payload' ([System.IO.File]::ReadAllText((Join-Path $fixtureExtractRoot 'payload.txt'), [System.Text.Encoding]::UTF8)) 'Concurrent worktree changes cannot enter the snapshot-backed ZIP'

    [System.IO.File]::WriteAllText((Join-Path $fixtureExtractRoot 'payload.txt'), 'changed ZIP payload', [System.Text.UTF8Encoding]::new($false))
    $zipPayloadChangeRejected = $false
    try {
        Assert-CountdownDeploymentFileManifest -Root $fixtureExtractRoot -Manifest $snapshotManifestFixture -Stage 'ZIP 哈希夹具'
    }
    catch {
        $zipPayloadChangeRejected = $_.Exception.Message -match '部署文件已变化'
    }
    Assert-Equal $true $zipPayloadChangeRejected 'Deployment gate rejects a changed file extracted from the ZIP'

    [System.IO.File]::WriteAllText((Join-Path $fixtureExtractRoot 'unexpected.txt'), 'unexpected ZIP file', [System.Text.UTF8Encoding]::new($false))
    $zipFileSetChangeRejected = $false
    try {
        Assert-CountdownDeploymentFileSet -Root $fixtureExtractRoot -ExpectedFiles @('payload.txt') -Stage 'ZIP 文件集夹具'
    }
    catch {
        $zipFileSetChangeRejected = $_.Exception.Message -match '发布包文件集与已验证清单不一致'
    }
    Assert-Equal $true $zipFileSetChangeRejected 'Deployment gate rejects an unexpected file extracted from the ZIP'
}
finally {
    Remove-Module 'Countdown.DeploymentGate.Fixture' -Force -ErrorAction SilentlyContinue
    Remove-CountdownIsolatedTestTree -Path $deploymentGateTestRoot -AllowedRoot $deploymentGateEvidenceRoot
}
$recordedTransferCallCount = [regex]::Matches(
    $deploySource,
    '(?m)^\s*Invoke-CountdownRecordedScpTransfer\b').Count
Assert-Equal 2 $recordedTransferCallCount 'Office deploy records both SCP commands through the transfer ledger interface'
$recordedSshReadCallCount = [regex]::Matches(
    $deploySource,
    '\bInvoke-CountdownRecordedSshRead\b').Count
Assert-Equal 4 $recordedSshReadCallCount 'Office deploy records both failure evidence reads, the deployment binding, and the post-deployment independent readback through the transfer ledger interface'
Assert-Equal $false $deploySource.Contains('& scp.exe') 'Office deploy has no direct SCP call that bypasses the transfer ledger'
Assert-Equal 0 @($releaseContract.PackageFiles | Where-Object { $_ -like 'assets\ui-references\*' }).Count 'UI references stay outside the release package'
Assert-Equal 0 @($releaseContract.InstalledFiles | Where-Object { $_ -like 'assets\ui-references\*' }).Count 'UI references stay outside installed runtime'
Assert-Equal 0 @($releaseContract.BuildAssets | Where-Object { $_.PublishedPath -like '*ui-references*' }).Count 'UI references stay outside build targets'

$mutableReleaseContract = Get-CountdownReleaseContract
$mutableReleaseContract.PackageFiles[0] = 'mutated-package-file'
$mutableReleaseContract.BuildAssets[0].SourcePath = 'mutated-build-source'
$mutableReleaseContract.IntegrityHashes['Build-CountdownApplication.ps1'] = ('0' * 64)
$freshReleaseContract = Get-CountdownReleaseContract
Assert-Equal $false ('mutated-package-file' -in @($freshReleaseContract.PackageFiles)) 'Release contract returns a fresh package list'
Assert-Equal $false ('mutated-build-source' -in @($freshReleaseContract.BuildAssets.SourcePath)) 'Release contract returns fresh build asset records'
Assert-Equal $false (($freshReleaseContract.IntegrityHashes['Build-CountdownApplication.ps1']) -eq ('0' * 64)) 'Release contract returns a fresh integrity map'

$releaseContractTestRoot = Join-Path $PSScriptRoot ("release-contract-test-" + [guid]::NewGuid().ToString('N'))
try {
    New-Item -ItemType Directory -Path $releaseContractTestRoot -Force | Out-Null
    $missingPackageRejected = $false
    try {
        Assert-CountdownReleasePackage -Root $releaseContractTestRoot | Out-Null
    }
    catch {
        $missingPackageRejected = $_.Exception.Message -match '缺少发布包文件'
    }
    Assert-Equal $true $missingPackageRejected 'Release package assertion rejects missing files'

    foreach ($relativePath in $releaseContract.PackageFiles) {
        $testPath = Join-Path $releaseContractTestRoot $relativePath
        $testParent = Split-Path -Parent $testPath
        if (-not (Test-Path -LiteralPath $testParent -PathType Container)) {
            New-Item -ItemType Directory -Path $testParent -Force | Out-Null
        }
        [System.IO.File]::WriteAllText($testPath, 'invalid release content', [System.Text.UTF8Encoding]::new($false))
    }
    $invalidHashRejected = $false
    try {
        Assert-CountdownReleasePackage -Root $releaseContractTestRoot | Out-Null
    }
    catch {
        $invalidHashRejected = $_.Exception.Message -match '发布包哈希不匹配'
    }
    Assert-Equal $true $invalidHashRejected 'Release package assertion rejects invalid content'
}
finally {
    Remove-CountdownReleaseContractTestTree -Path $releaseContractTestRoot
}

if ($ReleaseContractOnly) {
    if ($failures.Count -gt 0) {
        $failures | ForEach-Object { Write-Error $_ }
        exit 1
    }
    [pscustomobject]@{ Passed = $script:assertions; Failed = 0; Scope = 'ReleaseContract' } | ConvertTo-Json
    exit 0
}
}

$acceptanceTestRoot = Join-Path $PSScriptRoot ("acceptance-test-" + [guid]::NewGuid().ToString('N'))
$acceptanceStartupRequestPath = Join-Path $acceptanceTestRoot 'startup-request.json'
$acceptanceRuntimeStatusPath = Join-Path $acceptanceTestRoot 'runtime-status.json'
$acceptanceStatusPath = Join-Path $acceptanceTestRoot 'acceptance-status.json'
$acceptanceSettingsSnapshotPath = Join-Path $acceptanceTestRoot 'settings-snapshot.json'
$acceptanceSettingsPath = Join-Path $acceptanceTestRoot 'settings.json'
$acceptanceApplicationPath = Join-Path $acceptanceTestRoot '敏敏子倒计时.ps1'
$acceptanceExpectedContext = [pscustomobject]@{
    User = 'MINMIN\AcceptanceUser'
    SessionId = 7
    Desktop = 'D:\AcceptanceDesktop'
}
$acceptanceAdapterState = [pscustomobject]@{ Completed = 0; Removed = 0 }
$acceptanceTaskState = 'Running'
$acceptanceTaskIdentity = 'scheduled-task-v1'
$acceptanceJobs = [System.Collections.ArrayList]::new()
function Reset-CountdownAcceptanceTestCase {
    param([int] $RuntimeProcessId = 4242)

    Remove-Item -LiteralPath $acceptanceStatusPath -Force -ErrorAction SilentlyContinue
    Copy-Item -LiteralPath $acceptanceSettingsSnapshotPath -Destination $acceptanceSettingsPath -Force
    $acceptanceAdapterState.Completed = 0
    $acceptanceAdapterState.Removed = 0
    $script:acceptanceTaskState = 'Running'
    $script:acceptanceTaskIdentity = 'scheduled-task-v1'
    $request = New-CountdownStartupRequest -Path $acceptanceStartupRequestPath -AcceptanceReadOnly
    [void] (Write-CountdownWindowReadyReceipt -Path $acceptanceRuntimeStatusPath -LaunchToken $request.launchToken -ProcessId $RuntimeProcessId -SessionId 7 -ApplicationPath $acceptanceApplicationPath -CalendarState Ready)
    $request
}

function Start-CountdownAcceptanceTestApplication {
    param(
        [Parameter(Mandatory)] [object] $StartupRequest,
        [switch] $ChangeSettings,
        [switch] $SkipFinalizedReady
    )

    $job = Start-Job -ArgumentList $acceptanceStartupRequestPath, $acceptanceStatusPath, $StartupRequest.launchToken, $acceptanceApplicationPath, $acceptanceSettingsPath, ([bool] $ChangeSettings), ([bool] $SkipFinalizedReady) -ScriptBlock {
        param($StartupRequestPath, $AcceptanceStatusPath, $LaunchToken, $ApplicationPath, $SettingsPath, $ChangeSettings, $SkipFinalizedReady)
        $deadline = [DateTimeOffset]::UtcNow.AddSeconds(10)
        do {
            try {
                $request = [System.IO.File]::ReadAllText($StartupRequestPath, [System.Text.Encoding]::UTF8) | ConvertFrom-Json
                if ([string] $request.acceptanceState -eq 'released') {
                    if ($ChangeSettings) {
                        [System.IO.File]::WriteAllText($SettingsPath, '{"schemaVersion":9,"unexpected":true}', [System.Text.UTF8Encoding]::new($false))
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
                $request = [System.IO.File]::ReadAllText($StartupRequestPath, [System.Text.Encoding]::UTF8) | ConvertFrom-Json
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
                $reportedTaskState = if ($startupState -in @('ready', 'finalized')) { 'Ready' } else { 'Running' }
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
    Assert-Equal 1 $acceptanceAdapterState.Completed 'Transaction acceptance commits the transaction once'
    Assert-Equal 1 $acceptanceAdapterState.Removed 'Transaction acceptance removes rollback evidence only after finalized-ready'
    Assert-Equal 'finalized' ([string] ((Get-Content -LiteralPath $acceptanceStartupRequestPath -Raw -Encoding UTF8 | ConvertFrom-Json).acceptanceState)) 'Transaction acceptance leaves the startup request finalized'
    Wait-Job -Job $acceptanceReadyJob -Timeout 5 | Out-Null
    Assert-Equal 'Completed' ([string] $acceptanceReadyJob.State) 'Acceptance test application observed release before finalize'

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
    $acceptanceTaskState = 'Ready'
    $taskStoppedRejected = $false
    try {
        Invoke-CountdownAcceptanceTestCase -StartupRequest $taskStoppedRequest | Out-Null
    }
    catch {
        $taskStoppedRejected = $_.Exception.Message -match '验收提交前计划任务未运行：Ready'
    }
    Assert-Equal $true $taskStoppedRejected 'A non-running scheduled task blocks transaction acceptance before release'
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
        $taskDriftRejected = $_.Exception.Message -match 'schema 迁移确认后计划任务未运行：Ready'
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
}
finally {
    foreach ($job in $acceptanceJobs) {
        Stop-Job -Job $job -ErrorAction SilentlyContinue
        Remove-Job -Job $job -Force -ErrorAction SilentlyContinue
    }
    foreach ($path in @($acceptanceStartupRequestPath, $acceptanceRuntimeStatusPath, $acceptanceStatusPath, $acceptanceSettingsSnapshotPath, $acceptanceSettingsPath, $acceptanceApplicationPath)) {
        Remove-Item -LiteralPath $path -Force -ErrorAction SilentlyContinue
    }
    Remove-Item -LiteralPath $acceptanceTestRoot -Force -ErrorAction SilentlyContinue
}

if ($AcceptanceOnly) {
    if ($failures.Count -gt 0) {
        $failures | ForEach-Object { Write-Error $_ }
        exit 1
    }
    [pscustomobject]@{ Passed = $script:assertions; Failed = 0; Scope = 'TransactionAcceptance' } | ConvertTo-Json
    exit 0
}

$firstTag = New-CountdownTagRecord -Id ' work ' -Name ' 工作 ' -Color ' #6B8E23 '
Assert-Equal 'work' $firstTag.id 'Tag record normalizes ID'
Assert-Equal '工作' $firstTag.name 'Tag record normalizes name'
Assert-Equal '#6B8E23' $firstTag.color 'Tag record preserves optional color'
$invalidTagColorRejected = $false
try {
    New-CountdownTagRecord -Id 'invalid-color' -Name '无效颜色' -Color 'red' | Out-Null
}
catch {
    $invalidTagColorRejected = $true
}
Assert-Equal $true $invalidTagColorRejected 'Invalid tag color is rejected'
$tagLibrary = [System.Collections.ArrayList]::new()
[void] $tagLibrary.Add($firstTag)
$tagLibraryCopy = Copy-CountdownTags -Tags $tagLibrary
$tagLibraryCopy[0].name = '已改名'
Assert-Equal '工作' $tagLibrary[0].name 'Tag library copy does not mutate source tag'
$normalizedTagIds = ConvertTo-CountdownTagIds -TagIds @(' work ', 'home') -KnownTagIds @('work', 'home')
Assert-Equal 2 $normalizedTagIds.Count 'Tag IDs retain unique values'
Assert-Equal 'work' $normalizedTagIds[0] 'Tag IDs preserve first-known canonical value'
Assert-Equal 'home' $normalizedTagIds[1] 'Tag IDs retain additional values'
$emptyTagIds = ConvertTo-CountdownTagIds -TagIds $null -KnownTagIds @('work')
Assert-Equal 0 $emptyTagIds.Count 'Empty tag IDs are valid for untagged records'
$duplicateTagIdRejected = $false
try {
    ConvertTo-CountdownTagIds -TagIds @('work', 'work') -KnownTagIds @('work') | Out-Null
}
catch {
    $duplicateTagIdRejected = $true
}
Assert-Equal $true $duplicateTagIdRejected 'Duplicate tag ID is rejected'
$danglingTagRejected = $false
try {
    ConvertTo-CountdownTagIds -TagIds @('missing') -KnownTagIds @('work') | Out-Null
}
catch {
    $danglingTagRejected = $true
}
Assert-Equal $true $danglingTagRejected 'Unknown tag ID is rejected'
Assert-Equal $true (Test-CountdownTagFilter -TagIds @('work') -SelectedTagIds @() -MatchMode Any) 'Empty tag selection includes tagged records'
Assert-Equal $true (Test-CountdownTagFilter -TagIds @('work') -SelectedTagIds @('work') -MatchMode Any) 'Any tag filter matches selected tag'
Assert-Equal $false (Test-CountdownTagFilter -TagIds @('home') -SelectedTagIds @('work') -MatchMode Any) 'Any tag filter excludes unselected tag'
Assert-Equal $true (Test-CountdownTagFilter -TagIds @('work', 'home') -SelectedTagIds @('home') -MatchMode Any) 'Any tag filter evaluates a non-first selected tag'
Assert-Equal $true (Test-CountdownTagFilter -TagIds @('work', 'home') -SelectedTagIds @('work', 'home') -MatchMode All) 'All tag filter matches every selected tag'
Assert-Equal $false (Test-CountdownTagFilter -TagIds @('work') -SelectedTagIds @('work', 'home') -MatchMode All) 'All tag filter requires every selected tag'
Assert-Equal $false (Test-CountdownTagFilter -TagIds @('work', 'home') -SelectedTagIds @('home', 'office') -MatchMode All) 'All tag filter excludes a missing selected tag'
Assert-Equal $true (Test-CountdownTagFilter -TagIds @() -SelectedTagIds @() -UntaggedOnly) 'Untagged filter includes untagged record'
Assert-Equal $false (Test-CountdownTagFilter -TagIds @('work') -SelectedTagIds @() -UntaggedOnly) 'Untagged filter excludes tagged record'

$target = ConvertTo-CountdownTargetUtc -DateText '2026-07-15' -TimeText '18:00:00'
Assert-Equal '2026-07-15T10:00:00.0000000+00:00' $target.ToString('o') 'Beijing to UTC'

$parts = Get-CountdownParts -TargetUtc ([DateTimeOffset]'2026-07-17T11:02:03Z') -NowUtc ([DateTimeOffset]'2026-07-15T10:00:00Z')
Assert-Equal $false $parts.IsElapsed 'Future target state'
Assert-Equal 2 $parts.Days 'Days'
Assert-Equal 1 $parts.Hours 'Hours'
Assert-Equal 2 $parts.Minutes 'Minutes'
Assert-Equal 3 $parts.Seconds 'Seconds'
Assert-Equal '2 天' (Format-CountdownParts -Parts $parts -Precision Day) 'Day precision format'
Assert-Equal '2 天 01 小时' (Format-CountdownParts -Parts $parts -Precision Hour) 'Hour precision format'
Assert-Equal '2 天 01 小时 02 分钟' (Format-CountdownParts -Parts $parts -Precision Minute) 'Minute precision format'
Assert-Equal '2 天 01 小时 02 分钟 03 秒' (Format-CountdownParts -Parts $parts -Precision Second) 'Second precision format'

$elapsed = Get-CountdownParts -TargetUtc ([DateTimeOffset]'2026-07-15T10:00:00Z') -NowUtc ([DateTimeOffset]'2026-07-15T10:00:01Z')
Assert-Equal $true $elapsed.IsElapsed 'Elapsed state'
Assert-Equal '已到期' (Format-CountdownParts -Parts $elapsed -Precision Second) 'Elapsed format'

$workdayMidpoint = Get-WorkdayEarnings -MonthlySalary ([decimal]23000) -WorkStart ([TimeSpan]'09:00') -WorkEnd ([TimeSpan]'18:00') -BeijingNow ([DateTimeOffset]'2026-07-15T13:30:00+08:00')
Assert-Equal ([decimal]500) $workdayMidpoint 'Midpoint workday earnings'
Assert-Equal ([decimal]0) (Get-WorkdayEarnings -MonthlySalary ([decimal]23000) -WorkStart ([TimeSpan]'09:00') -WorkEnd ([TimeSpan]'18:00') -BeijingNow ([DateTimeOffset]'2026-07-15T08:59:59+08:00')) 'Before work earnings'
Assert-Equal ([decimal]0) (Get-WorkdayEarnings -MonthlySalary ([decimal]23000) -WorkStart ([TimeSpan]'09:00') -WorkEnd ([TimeSpan]'18:00') -BeijingNow ([DateTimeOffset]'2026-07-18T13:30:00+08:00')) 'Weekend earnings'
$statutoryCalendar = @{ '2026-01-01' = $false; '2026-01-04' = $true }
Assert-Equal ([decimal]0) (Get-WorkdayEarnings -MonthlySalary ([decimal]22000) -WorkStart ([TimeSpan]'09:00') -WorkEnd ([TimeSpan]'18:00') -BeijingNow ([DateTimeOffset]'2026-01-01T13:30:00+08:00') -WorkdayMap $statutoryCalendar) 'Statutory paid leave earnings'
Assert-Equal ([decimal]500) (Get-WorkdayEarnings -MonthlySalary ([decimal]22000) -WorkStart ([TimeSpan]'09:00') -WorkEnd ([TimeSpan]'18:00') -BeijingNow ([DateTimeOffset]'2026-01-04T13:30:00+08:00') -WorkdayMap $statutoryCalendar) 'Compensatory workday earnings'
$invalidWorkHoursRejected = $false
try { Get-WorkdayEarnings -MonthlySalary ([decimal]1) -WorkStart ([TimeSpan]'18:00') -WorkEnd ([TimeSpan]'09:00') -BeijingNow ([DateTimeOffset]'2026-07-15T13:30:00+08:00') | Out-Null }
catch { $invalidWorkHoursRejected = $true }
Assert-Equal $true $invalidWorkHoursRejected 'Invalid work hours rejected'

$invalidRejected = $false
try { ConvertTo-CountdownTargetUtc -DateText '2026-02-30' -TimeText '18:00:00' | Out-Null }
catch { $invalidRejected = $true }
Assert-Equal $true $invalidRejected 'Invalid date rejected'

$runtimeTestRoot = Join-Path $PSScriptRoot ("runtime-test-" + [guid]::NewGuid().ToString('N'))
$runtimeStatusTestPath = Join-Path $runtimeTestRoot 'runtime-status.json'
$acceptanceStatusTestPath = Join-Path $runtimeTestRoot 'acceptance-status.json'
$startupRequestTestPath = Join-Path $runtimeTestRoot 'startup-request.json'
$runtimeApplicationPath = Join-Path $runtimeTestRoot '敏敏子倒计时.ps1'
$runtimeExecutablePath = Join-Path $runtimeTestRoot 'app\MinminCountdown.exe'
$diagnosticLogTestPath = Join-Path $runtimeTestRoot 'logs\application.jsonl'
try {
    New-Item -ItemType Directory -Path $runtimeTestRoot -Force | Out-Null
    [System.IO.File]::WriteAllText($runtimeApplicationPath, '# test application', (New-Object System.Text.UTF8Encoding($false)))
    $startupRequest = New-CountdownStartupRequest -Path $startupRequestTestPath -AcceptanceReadOnly
    Assert-Equal 1 $startupRequest.schemaVersion 'Startup request schema'
    Assert-Equal $true (-not [string]::IsNullOrWhiteSpace([string] $startupRequest.launchToken)) 'Startup request has token'
    Assert-Equal $true ([bool] $startupRequest.acceptanceReadOnly) 'Startup request can hold the candidate application in acceptance read-only mode'
    Assert-Equal 'active' ([string] $startupRequest.acceptanceState) 'Startup request begins in active acceptance state'
    Assert-Equal $false (Complete-CountdownStartupRequest -Path $startupRequestTestPath -LaunchToken 'wrong-token') 'Startup request rejects release with a different token'
    Assert-Equal $true (Complete-CountdownStartupRequest -Path $startupRequestTestPath -LaunchToken $startupRequest.launchToken) 'Startup request releases acceptance mode with the same token'
    $releasedStartupRequest = Get-Content -LiteralPath $startupRequestTestPath -Raw -Encoding UTF8 | ConvertFrom-Json
    Assert-Equal 'released' ([string] $releasedStartupRequest.acceptanceState) 'Startup request persists explicit released acceptance state'
    Assert-Equal $true ([bool] $releasedStartupRequest.acceptanceReadOnly) 'Released startup request keeps the UI read-only until C# confirms migration'
    Assert-Equal 'released' ([string] (Wait-CountdownStartupRequestState -Path $startupRequestTestPath -LaunchToken $startupRequest.launchToken -ExpectedState released -TimeoutSeconds 0).acceptanceState) 'Startup request state wait validates token and state'
    $releasedStartupRequest.acceptanceState = 'ready'
    [System.IO.File]::WriteAllText($startupRequestTestPath, ($releasedStartupRequest | ConvertTo-Json -Depth 8), [System.Text.UTF8Encoding]::new($false))
    Assert-Equal $true (Set-CountdownStartupRequestFinalized -Path $startupRequestTestPath -LaunchToken $startupRequest.launchToken) 'Startup request finalizes only after C# reports ready'
    $finalizedStartupRequest = Get-Content -LiteralPath $startupRequestTestPath -Raw -Encoding UTF8 | ConvertFrom-Json
    Assert-Equal 'finalized' ([string] $finalizedStartupRequest.acceptanceState) 'Startup request persists explicit finalized state'
    Assert-Equal $false ([bool] $finalizedStartupRequest.acceptanceReadOnly) 'Finalized startup request permits UI editing'
    [System.IO.File]::WriteAllText($startupRequestTestPath, '{broken-json', [System.Text.UTF8Encoding]::new($false))
    $corruptReleaseRejected = $false
    try { Complete-CountdownStartupRequest -Path $startupRequestTestPath -LaunchToken $startupRequest.launchToken | Out-Null }
    catch { $corruptReleaseRejected = $true }
    Assert-Equal $true $corruptReleaseRejected 'Startup request release preserves JSON and IO failures as exceptions'
    [System.IO.File]::WriteAllText($startupRequestTestPath, '{"schemaVersion":999,"launchToken":"unknown","acceptanceState":"active"}', [System.Text.UTF8Encoding]::new($false))
    $unknownSchemaRejected = $false
    try { Complete-CountdownStartupRequest -Path $startupRequestTestPath -LaunchToken 'unknown' | Out-Null }
    catch { $unknownSchemaRejected = $_.Exception.Message -match 'schemaVersion' }
    Assert-Equal $true $unknownSchemaRejected 'Startup request rejects unknown schema versions'
    $startupRequest = New-CountdownStartupRequest -Path $startupRequestTestPath -AcceptanceReadOnly
    Assert-Equal $false (Test-Path -LiteralPath "$startupRequestTestPath.tmp") 'Startup request leaves no fixed temporary file'
    $receipt = Write-CountdownWindowReadyReceipt -Path $runtimeStatusTestPath -LaunchToken $startupRequest.launchToken -ProcessId 4242 -SessionId 7 -ApplicationPath $runtimeApplicationPath -CalendarState Pending
    $processProbe = {
        param([int] $ProcessId)
        if ($ProcessId -eq 4242) {
            [pscustomobject]@{ ProcessId = 4242; SessionId = 7; CommandLine = "pwsh.exe -STA -File `"$runtimeApplicationPath`"" }
        }
    }
    $defaultAdapterEvaluation = Test-CountdownWindowReadyReceipt -Receipt $receipt -ExpectedLaunchToken $startupRequest.launchToken -ExpectedSessionId 7 -ExpectedApplicationPath $runtimeApplicationPath -ProcessProbe $processProbe
    Assert-Equal $false $defaultAdapterEvaluation.Ready 'Window-ready receipt rejects the legacy PowerShell adapter by default'
    $legacyAdapterEvaluation = Test-CountdownWindowReadyReceipt -Receipt $receipt -ExpectedLaunchToken $startupRequest.launchToken -ExpectedSessionId 7 -ExpectedApplicationPath $runtimeApplicationPath -ProcessProbe $processProbe -AllowLegacyPowerShellAdapter
    Assert-Equal $true $legacyAdapterEvaluation.Ready 'Window-ready receipt accepts the legacy PowerShell adapter only in explicit compatibility mode'
    Assert-Equal 'Pending' $legacyAdapterEvaluation.Receipt.calendarState 'Window-ready does not wait for calendar'
    $managedExecutableProbe = {
        param([int] $ProcessId)
        if ($ProcessId -eq 4242) {
            [pscustomobject]@{ ProcessId = 4242; SessionId = 7; ExecutablePath = $runtimeExecutablePath; CommandLine = "`"$runtimeExecutablePath`"" }
        }
    }
    Assert-Equal $true (Test-CountdownWindowReadyReceipt -Receipt $receipt -ExpectedLaunchToken $startupRequest.launchToken -ExpectedSessionId 7 -ExpectedApplicationPath $runtimeApplicationPath -ProcessProbe $managedExecutableProbe).Ready 'Window-ready receipt accepts the managed C# application host'
    $acceptanceReceipt = [ordered]@{
        schemaVersion = 1
        stage = 'finalized-ready'
        launchToken = $startupRequest.launchToken
        processId = 4242
        sessionId = 7
        applicationPath = $runtimeApplicationPath
        readyAtUtc = [DateTimeOffset]::UtcNow.ToString('o')
    }
    [System.IO.File]::WriteAllText($acceptanceStatusTestPath, ($acceptanceReceipt | ConvertTo-Json -Compress), [System.Text.UTF8Encoding]::new($false))
    Assert-Equal 'finalized-ready' ([string] (Wait-CountdownAcceptanceReadyReceipt -Path $acceptanceStatusTestPath -LaunchToken $startupRequest.launchToken -ExpectedProcessId 4242 -ExpectedSessionId 7 -ExpectedApplicationPath $runtimeApplicationPath -TimeoutSeconds 0 -ProcessProbe $managedExecutableProbe).stage) 'Acceptance-ready receipt validates token, C# PID, session and adapter path'
    $unmanagedExecutableProbe = {
        param([int] $ProcessId)
        if ($ProcessId -eq 4242) {
            [pscustomobject]@{ ProcessId = 4242; SessionId = 7; ExecutablePath = (Join-Path $runtimeTestRoot 'other\MinminCountdown.exe'); CommandLine = '"MinminCountdown.exe"' }
        }
    }
    Assert-Equal $false (Test-CountdownWindowReadyReceipt -Receipt $receipt -ExpectedLaunchToken $startupRequest.launchToken -ExpectedSessionId 7 -ExpectedApplicationPath $runtimeApplicationPath -ProcessProbe $unmanagedExecutableProbe).Ready 'Window-ready receipt rejects an unmanaged executable with the expected process name'
    Assert-Equal $false (Test-CountdownWindowReadyReceipt -Receipt $receipt -ExpectedLaunchToken 'old-token' -ExpectedSessionId 7 -ExpectedApplicationPath $runtimeApplicationPath -ProcessProbe $processProbe).Ready 'Old startup token is rejected'
    Assert-Equal $false (Test-CountdownWindowReadyReceipt -Receipt $receipt -ExpectedLaunchToken $startupRequest.launchToken -ExpectedSessionId 8 -ExpectedApplicationPath $runtimeApplicationPath -ProcessProbe $processProbe).Ready 'Wrong interactive session is rejected'
    Assert-Equal $false (Test-CountdownWindowReadyReceipt -Receipt $receipt -ExpectedLaunchToken $startupRequest.launchToken -ExpectedSessionId 7 -ExpectedApplicationPath (Join-Path $runtimeTestRoot 'other.ps1') -ProcessProbe $processProbe).Ready 'Wrong application path is rejected'
    $embeddedPathProbe = {
        param([int] $ProcessId)
        if ($ProcessId -eq 4242) {
            [pscustomobject]@{ ProcessId = 4242; SessionId = 7; CommandLine = "pwsh.exe -File `"$(Join-Path $runtimeTestRoot 'helper.ps1')`" -ArgumentList `"$runtimeApplicationPath`"" }
        }
    }
    Assert-Equal $false (Test-CountdownWindowReadyReceipt -Receipt $receipt -ExpectedLaunchToken $startupRequest.launchToken -ExpectedSessionId 7 -ExpectedApplicationPath $runtimeApplicationPath -ProcessProbe $embeddedPathProbe).Ready 'Command-line path substring outside -File is rejected'
    $commandModeProbe = {
        param([int] $ProcessId)
        if ($ProcessId -eq 4242) {
            [pscustomobject]@{ ProcessId = 4242; SessionId = 7; CommandLine = "pwsh.exe -NoProfile -Command `"Write-Output ' -File $runtimeApplicationPath '`"" }
        }
    }
    Assert-Equal $false (Test-CountdownWindowReadyReceipt -Receipt $receipt -ExpectedLaunchToken $startupRequest.launchToken -ExpectedSessionId 7 -ExpectedApplicationPath $runtimeApplicationPath -ProcessProbe $commandModeProbe).Ready 'Command-mode text containing -File is not accepted as the application process'
    Assert-Equal $false (Test-CountdownWindowReadyReceipt -Receipt $receipt -ExpectedLaunchToken $startupRequest.launchToken -ExpectedSessionId 7 -ExpectedApplicationPath $runtimeApplicationPath -ProcessProbe { param([int] $ProcessId) $null }).Ready 'Missing process is rejected'
    Assert-Equal $false (Test-CountdownWindowReadyReceipt -Receipt $receipt -ExpectedLaunchToken $startupRequest.launchToken -ExpectedSessionId 7 -ExpectedApplicationPath $runtimeApplicationPath -ProcessProbe { param([int] $ProcessId) [pscustomobject]@{ ProcessId = 4243; SessionId = 7; CommandLine = "pwsh.exe -File `"$runtimeApplicationPath`"" } }).Ready 'Mismatched process identity is rejected'
    [System.IO.File]::WriteAllText($runtimeStatusTestPath, '{not-valid-json', (New-Object System.Text.UTF8Encoding($false)))
    Assert-Equal $true ($null -eq (Read-CountdownRuntimeReceipt -Path $runtimeStatusTestPath)) 'Malformed receipt is not accepted'
    $timeoutObserved = $false
    try {
        Wait-CountdownWindowReady -Path $runtimeStatusTestPath -LaunchToken $startupRequest.launchToken -ExpectedSessionId 7 -ExpectedApplicationPath $runtimeApplicationPath -TimeoutSeconds 0 -PollMilliseconds 1 -ProcessProbe { param([int] $ProcessId) $null } | Out-Null
    }
    catch { $timeoutObserved = $_.Exception.Message -match '启动就绪回执超时' }
    Assert-Equal $true $timeoutObserved 'Window-ready wait has a bounded timeout'
    $diagnosticStarted = Write-CountdownDiagnosticEvent -Path $diagnosticLogTestPath -Level Info -Event 'ProcessStarted'
    Assert-Equal $true $diagnosticStarted 'Diagnostic logging reports a successful append'
    $diagnosticStartEntry = Get-Content -LiteralPath $diagnosticLogTestPath -Encoding UTF8 | Select-Object -First 1 | ConvertFrom-Json
    Assert-Equal 1 $diagnosticStartEntry.schemaVersion 'Diagnostic log schema'
    Assert-Equal 'ProcessStarted' $diagnosticStartEntry.event 'Diagnostic log event name'
    Assert-Equal 'Info' $diagnosticStartEntry.level 'Diagnostic log level'
    Assert-Equal $PID $diagnosticStartEntry.processId 'Diagnostic log process identity'
    $diagnosticException = [System.InvalidOperationException]::new('Deferred theme template failed')
    $diagnosticFailed = Write-CountdownDiagnosticEvent -Path $diagnosticLogTestPath -Level Error -Event 'FatalUnhandledException' -Exception $diagnosticException -ScriptStackTrace 'Start-CountdownApplication line 1'
    Assert-Equal $true $diagnosticFailed 'Diagnostic logging records fatal exceptions'
    $diagnosticErrorEntry = Get-Content -LiteralPath $diagnosticLogTestPath -Encoding UTF8 | Select-Object -Last 1 | ConvertFrom-Json
    Assert-Equal 'System.InvalidOperationException' $diagnosticErrorEntry.exceptionType 'Diagnostic log exception type'
    Assert-Equal 'Deferred theme template failed' $diagnosticErrorEntry.exceptionMessage 'Diagnostic log exception message'
    Assert-Equal 'Start-CountdownApplication line 1' $diagnosticErrorEntry.scriptStackTrace 'Diagnostic log PowerShell stack'
    foreach ($index in 1..3) {
        Write-CountdownDiagnosticEvent -Path $diagnosticLogTestPath -Level Info -Event ("RotationProbe$index-" + ('x' * 180)) -MaxBytes 300 -MaxArchives 2 | Out-Null
    }
    Assert-Equal $true (Test-Path -LiteralPath "$diagnosticLogTestPath.1" -PathType Leaf) 'Diagnostic log rotates at the size bound'
    Assert-Equal $true ((Get-Item -LiteralPath $diagnosticLogTestPath).Length -lt 1024) 'Diagnostic current log remains bounded after rotation'
}
finally {
    Remove-Item -LiteralPath $runtimeTestRoot -Recurse -Force -ErrorAction SilentlyContinue
}

$transactionModule = Get-Module -Name 'Countdown.InstallTransaction'
$installPathGuardCommand = Get-Command Assert-CountdownInstallPathOnDDrive -Module Countdown.InstallTransaction -ErrorAction SilentlyContinue
Assert-Equal $true ($null -ne $installPathGuardCommand) 'Installer-facing D-drive path guard is exported by the transaction module'
Assert-Equal 'D:\Codex\projects\时钟倒计时\expected-desktop' (Assert-CountdownInstallPathOnDDrive -Path 'D:\Codex\projects\时钟倒计时\expected-desktop') 'Installer-facing D-drive path guard normalizes a safe expected desktop path'
$outsideDDriveRejected = $false
try {
    Assert-CountdownInstallPathOnDDrive -Path 'C:\unexpected-desktop' | Out-Null
}
catch {
    $outsideDDriveRejected = $_.Exception.Message -match '只允许操作 D 盘路径'
}
Assert-Equal $true $outsideDDriveRejected 'Installer-facing D-drive path guard rejects a non-D expected desktop path'
$managedFilePath = & $transactionModule { param($CommandLine) Get-CountdownManagedApplicationFileArgument -CommandLine $CommandLine } "pwsh.exe -NoProfile -File `"$runtimeApplicationPath`""
Assert-Equal ([System.IO.Path]::GetFullPath($runtimeApplicationPath)) $managedFilePath 'Managed-process parser resolves exact -File application path'
$managedEmbeddedPath = & $transactionModule { param($CommandLine) Get-CountdownManagedApplicationFileArgument -CommandLine $CommandLine } "pwsh.exe -File `"$(Join-Path $PSScriptRoot 'helper.ps1')`" -ArgumentList `"$runtimeApplicationPath`""
Assert-Equal ([System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot 'helper.ps1'))) $managedEmbeddedPath 'Managed-process parser does not treat an argument substring as the script path'
$managedCommandEmbeddedPath = & $transactionModule { param($CommandLine) Get-CountdownManagedApplicationFileArgument -CommandLine $CommandLine } "pwsh.exe -NoProfile -Command `"Write-Output ' -File $runtimeApplicationPath '`""
Assert-Equal $true ($null -eq $managedCommandEmbeddedPath) 'Managed-process parser rejects -File text inside -Command argv'
$runtimeModule = Get-Module -Name 'Countdown.Runtime'
$runtimeCommandEmbeddedPath = & $runtimeModule { param($CommandLine) Get-CountdownRuntimeFileArgument -CommandLine $CommandLine } "pwsh.exe -NoProfile -Command `"Write-Output ' -File $runtimeApplicationPath '`""
Assert-Equal $true ($null -eq $runtimeCommandEmbeddedPath) 'Runtime-process parser rejects -File text inside -Command argv'
$resolvedInteractiveDesktop = & $transactionModule {
    Resolve-CountdownInteractiveDesktopPath -ProfileImagePath '%SystemDrive%\Profiles\OfficeUser' -DesktopTemplate '%OneDrive%\Desktop' -UserName 'OfficeUser' -EnvironmentVariables @{ SystemDrive = 'D:'; OneDrive = '%USERPROFILE%\OneDrive' }
}
Assert-Equal 'D:\Profiles\OfficeUser\OneDrive\Desktop' $resolvedInteractiveDesktop 'Desktop resolver uses the Explorer SID environment rather than the current pwsh environment'
$unknownDesktopVariableRejected = $false
try {
    & $transactionModule {
        Resolve-CountdownInteractiveDesktopPath -ProfileImagePath '%SystemDrive%\Profiles\OfficeUser' -DesktopTemplate '%UnmappedDesktop%\Desktop' -UserName 'OfficeUser' -EnvironmentVariables @{ SystemDrive = 'D:' }
    } | Out-Null
}
catch {
    $unknownDesktopVariableRejected = $_.Exception.Message -match '无法按交互用户环境解析'
}
Assert-Equal $true $unknownDesktopVariableRejected 'Desktop resolver rejects variables missing from the Explorer SID environment'
$sameSessionExplorerContext = & $transactionModule {
    Get-CountdownSingleInteractiveExplorerContext -ExplorerProcesses @(
        [pscustomobject]@{ ProcessId = 101; SessionId = 7 },
        [pscustomobject]@{ ProcessId = 102; SessionId = 7 }
    ) -OwnerResolver {
        param($process)
        [pscustomobject]@{ ReturnValue = 0; Domain = 'OFFICE'; User = 'Minmin' }
    }
}
Assert-Equal 7 $sameSessionExplorerContext.SessionId 'Multiple Explorer processes in the same interactive session are accepted'
Assert-Equal 'OFFICE\Minmin' $sameSessionExplorerContext.User 'Explorer grouping retains the interactive user identity'
$multipleExplorerContextsRejected = $false
try {
    & $transactionModule {
        Get-CountdownSingleInteractiveExplorerContext -ExplorerProcesses @(
            [pscustomobject]@{ ProcessId = 201; SessionId = 7 },
            [pscustomobject]@{ ProcessId = 202; SessionId = 8 }
        ) -OwnerResolver {
            param($process)
            [pscustomobject]@{ ReturnValue = 0; Domain = 'OFFICE'; User = 'Minmin' }
        } | Out-Null
    }
}
catch {
    $multipleExplorerContextsRejected = $_.Exception.Message -match 'exactly one interactive Explorer session and user'
}
Assert-Equal $true $multipleExplorerContextsRejected 'Multiple interactive Explorer sessions are rejected as ambiguous'

$stoppedWithoutProcesses = Test-CountdownManagedApplicationStopped -ProcessIds @() -TaskStates @{ 'Minmin Countdown' = 'Ready'; '敏敏子倒计时' = 'Missing' }
Assert-Equal $true $stoppedWithoutProcesses.Ready 'Managed application stop accepts non-running tasks with no process'
$taskStillRunning = Test-CountdownManagedApplicationStopped -ProcessIds @() -TaskStates @{ 'Minmin Countdown' = 'Running'; '敏敏子倒计时' = 'Ready' }
Assert-Equal $false $taskStillRunning.Ready 'Managed application stop waits for a running scheduled task even without a process'
Assert-Equal 'Minmin Countdown' (@($taskStillRunning.RunningTasks) -join ',') 'Managed application stop reports the running task'
$taskStillQueued = Test-CountdownManagedApplicationStopped -ProcessIds @() -TaskStates @{ 'Minmin Countdown' = 'Queued'; '敏敏子倒计时' = 'Ready' }
Assert-Equal $false $taskStillQueued.Ready 'Managed application stop waits for a queued scheduled task even without a process'
$taskStateUnknown = Test-CountdownManagedApplicationStopped -ProcessIds @() -TaskStates @{ 'Minmin Countdown' = 'Unknown'; '敏敏子倒计时' = 'Ready' }
Assert-Equal $false $taskStateUnknown.Ready 'Managed application stop treats an unknown scheduled-task state as still active'
$processQueryFailureRejected = $false
try {
    & $transactionModule {
        param($ApplicationPath)
        Get-CountdownManagedApplicationProcesses -ApplicationPath $ApplicationPath -ProcessQueryAction { throw 'injected process query failure' }
    } $runtimeApplicationPath | Out-Null
}
catch {
    $processQueryFailureRejected = $_.Exception.Message -match 'injected process query failure'
}
Assert-Equal $true $processQueryFailureRejected 'Managed process probe fails closed when CIM enumeration fails'
$taskQueryFailureRejected = $false
try {
    & $transactionModule {
        Get-CountdownManagedTaskStates -TaskName 'Minmin Countdown' -LegacyTaskName '敏敏子倒计时' -TaskQueryAction { throw 'injected task query failure' }
    } | Out-Null
}
catch {
    $taskQueryFailureRejected = $_.Exception.Message -match 'injected task query failure'
}
Assert-Equal $true $taskQueryFailureRejected 'Managed task probe fails closed when scheduled-task enumeration fails'
$legacyRemovalQueryFailureRejected = $false
try {
    Remove-CountdownScheduledTaskIfPresent -TaskName '敏敏子倒计时' -TaskQueryAction { throw 'injected legacy task query failure' } | Out-Null
}
catch {
    $legacyRemovalQueryFailureRejected = $_.Exception.Message -match 'injected legacy task query failure'
}
Assert-Equal $true $legacyRemovalQueryFailureRejected 'Legacy task removal fails closed when scheduled-task enumeration fails'
$exactTaskStates = & $transactionModule {
    Get-CountdownManagedTaskStates -TaskName 'Minmin Countdown' -LegacyTaskName '敏敏子倒计时' -TaskQueryAction {
        [pscustomobject]@{ TaskName = 'Minmin Countdown Extra'; State = 'Running' }
    }
}
Assert-Equal 'Missing' $exactTaskStates['Minmin Countdown'] 'Managed task probe requires an exact task-name match'
$stopCommand = Get-Command Stop-CountdownManagedApplication -Module Countdown.InstallTransaction
Assert-Equal $true $stopCommand.Parameters.ContainsKey('FreezeFutureStarts') 'Managed application stop can freeze future scheduled-task starts'
$mutexProbeModulePath = (Join-Path $PSScriptRoot 'Countdown.InstallTransaction.psm1').Replace("'", "''")
$mutexProbeCommand = "Import-Module -Name '$mutexProbeModulePath' -Force -DisableNameChecking; try { `$childMutex = Enter-CountdownInstallTransactionMutex -TimeoutSeconds 1; Exit-CountdownInstallTransactionMutex -Mutex `$childMutex; 'unexpected acquisition' } catch { `$_.Exception.Message }"
$mutexProbeEngine = (Get-Command pwsh.exe -CommandType Application -ErrorAction Stop | Select-Object -First 1 -ExpandProperty Source)
if ($ParentTransactionMutexHeld) {
    # 办公室验收已由同一远端 pwsh 持锁：仅验证子进程确实被该锁排除，避免测试进程自锁。
    $mutexProbeOutput = & $mutexProbeEngine -NoLogo -NoProfile -NonInteractive -Command $mutexProbeCommand | Out-String
    Assert-Equal $true ($mutexProbeOutput -match '超时') 'Parent install transaction mutex excludes a child PowerShell process'
}
else {
    $transactionMutex = Enter-CountdownInstallTransactionMutex -TimeoutSeconds 1
    try {
        Assert-Equal $true ($null -ne $transactionMutex) 'Install transaction mutex can be acquired'
        $mutexProbeOutput = & $mutexProbeEngine -NoLogo -NoProfile -NonInteractive -Command $mutexProbeCommand | Out-String
        Assert-Equal $true ($mutexProbeOutput -match '超时') 'Install transaction mutex excludes a second PowerShell process'
    }
    finally {
        Exit-CountdownInstallTransactionMutex -Mutex $transactionMutex
    }
}
$settingsMutexEnterCommand = Get-Command Enter-CountdownSettingsWriteMutex -Module Countdown.Settings -ErrorAction SilentlyContinue
$settingsMutexExitCommand = Get-Command Exit-CountdownSettingsWriteMutex -Module Countdown.Settings -ErrorAction SilentlyContinue
Assert-Equal $true ($null -ne $settingsMutexEnterCommand -and $null -ne $settingsMutexExitCommand) 'Settings module exposes a cross-process write mutex'
$settingsModuleSource = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'Countdown.Settings.psm1') -Raw
Assert-Equal $false ($settingsModuleSource.Contains('[System.Security.Cryptography.SHA256]::HashData') -or $settingsModuleSource.Contains('[Convert]::ToHexString')) 'Settings mutex hashing remains compatible with PowerShell 7.0 and .NET Core 3.1'
if ($null -ne $settingsMutexEnterCommand -and $null -ne $settingsMutexExitCommand) {
    $settingsMutexProbePath = Join-Path $PSScriptRoot ("settings-mutex-probe-" + [guid]::NewGuid().ToString('N') + '.json')
    $settingsMutex = Enter-CountdownSettingsWriteMutex -Path $settingsMutexProbePath -TimeoutSeconds 1
    try {
        $settingsMutexModulePath = (Join-Path $PSScriptRoot 'Countdown.Settings.psm1').Replace("'", "''")
        $escapedSettingsMutexProbePath = $settingsMutexProbePath.Replace("'", "''")
        $settingsMutexProbeCommand = "Import-Module -Name '$settingsMutexModulePath' -Force; try { `$childMutex = Enter-CountdownSettingsWriteMutex -Path '$escapedSettingsMutexProbePath' -TimeoutSeconds 1; Exit-CountdownSettingsWriteMutex -Mutex `$childMutex; 'unexpected acquisition' } catch { `$_.Exception.Message }"
        $settingsMutexProbeOutput = & $mutexProbeEngine -NoLogo -NoProfile -NonInteractive -Command $settingsMutexProbeCommand | Out-String
        Assert-Equal $true ($settingsMutexProbeOutput -match '超时') 'Settings write mutex excludes a second PowerShell process'
    }
    finally {
        Exit-CountdownSettingsWriteMutex -Mutex $settingsMutex
    }
}

$transactionTestRoot = Join-Path $PSScriptRoot ("transaction-test-" + [guid]::NewGuid().ToString('N'))
$recoveryConflictRoot = Join-Path $PSScriptRoot ("recovery-conflict-" + [guid]::NewGuid().ToString('N'))
$committedCleanupRoot = Join-Path $PSScriptRoot ("committed-cleanup-" + [guid]::NewGuid().ToString('N'))
$reparseSafetyRoot = Join-Path $PSScriptRoot ("reparse-safety-" + [guid]::NewGuid().ToString('N'))
$reparseDirectLinkPath = $null
$reparseNestedLinkPath = $null
$transactionInstallPath = Join-Path $transactionTestRoot 'install'
$transactionDesktopPath = Join-Path $transactionTestRoot 'desktop'
$transactionLauncherPath = Join-Path $transactionDesktopPath '敏敏子倒计时.exe'
$transactionLauncherTempPath = "$transactionLauncherPath.new"
$transactionLegacyLauncherPath = Join-Path $transactionDesktopPath '敏敏子倒计时.cmd'
$transactionSuffix = [guid]::NewGuid().ToString('N')
try {
    New-Item -ItemType Directory -Path $transactionInstallPath, $transactionDesktopPath -Force | Out-Null
    $reparseTargetPath = Join-Path $reparseSafetyRoot 'protected-target'
    $reparseDirectLinkPath = Join-Path $reparseSafetyRoot 'direct-link'
    $reparseTreePath = Join-Path $reparseSafetyRoot 'transaction-tree'
    $reparseNestedLinkPath = Join-Path $reparseTreePath 'nested-link'
    New-Item -ItemType Directory -Path $reparseTargetPath, $reparseTreePath -Force | Out-Null
    [System.IO.File]::WriteAllText((Join-Path $reparseTargetPath 'protected.txt'), 'must survive', (New-Object System.Text.UTF8Encoding($false)))
    New-Item -ItemType Junction -Path $reparseDirectLinkPath -Target $reparseTargetPath | Out-Null
    New-Item -ItemType Junction -Path $reparseNestedLinkPath -Target $reparseTargetPath | Out-Null
    $directReparseRejected = $false
    try { [void] (Assert-CountdownInstallPathOnDDrive -Path (Join-Path $reparseDirectLinkPath 'protected.txt')) }
    catch { $directReparseRejected = $_.Exception.Message -match 'reparse point' }
    Assert-Equal $true $directReparseRejected 'Install path validation rejects an ancestor junction before file operations'
    $nestedReparseRejected = $false
    try { & (Get-Module Countdown.InstallTransaction) { param($path) [void] (Assert-CountdownInstallTreeHasNoReparsePoints -Path $path) } $reparseTreePath }
    catch { $nestedReparseRejected = $_.Exception.Message -match 'reparse point' }
    Assert-Equal $true $nestedReparseRejected 'Recursive transaction operations reject nested junctions before deletion or copy'
    Assert-Equal 'must survive' ([System.IO.File]::ReadAllText((Join-Path $reparseTargetPath 'protected.txt'), [System.Text.Encoding]::UTF8)) 'Reparse point rejection leaves the external target untouched'
    [System.IO.File]::WriteAllText((Join-Path $transactionInstallPath 'settings.json'), '{"before":true}', (New-Object System.Text.UTF8Encoding($false)))
    [System.IO.File]::WriteAllText((Join-Path $transactionInstallPath '敏敏子倒计时.ps1'), '# old app', (New-Object System.Text.UTF8Encoding($false)))
    [System.IO.File]::WriteAllText($transactionLauncherPath, 'old launcher', (New-Object System.Text.UTF8Encoding($false)))
    [System.IO.File]::WriteAllText($transactionLauncherTempPath, 'old staged launcher', (New-Object System.Text.UTF8Encoding($false)))
    [System.IO.File]::WriteAllText($transactionLegacyLauncherPath, 'old legacy launcher', (New-Object System.Text.UTF8Encoding($false)))
    $transactionPowerShell = (Get-Command pwsh.exe -CommandType Application -ErrorAction Stop | Select-Object -First 1 -ExpandProperty Source)
    $preparedTransaction = Start-CountdownInstallTransaction -InstallPath $transactionInstallPath -LauncherPath $transactionLauncherPath -LegacyLauncherPath $transactionLegacyLauncherPath -TaskName "Minmin Countdown Prepared $transactionSuffix" -LegacyTaskName "敏敏子倒计时 Prepared $transactionSuffix"
    Assert-Equal $false ([bool] $preparedTransaction.payloadSnapshotReady) 'Prepared transaction does not snapshot mutable files before the old application is quiesced'
    [System.IO.File]::WriteAllText((Join-Path $transactionInstallPath 'settings.json'), '{"post-prepared":true}', (New-Object System.Text.UTF8Encoding($false)))
    Undo-CountdownInstallTransaction -TransactionPath $preparedTransaction.transactionPath -PowerShell7Path $transactionPowerShell -ExpectedInstallPath $transactionInstallPath -ExpectedTaskName "Minmin Countdown Prepared $transactionSuffix" -ExpectedLegacyTaskName "敏敏子倒计时 Prepared $transactionSuffix" -ExpectedLauncherPath $transactionLauncherPath -ExpectedLegacyLauncherPath $transactionLegacyLauncherPath -ExpectedLauncherTempPath $transactionLauncherTempPath -RestoreWindowsTimeAction { param($snapshot) }
    Assert-Equal '{"post-prepared":true}' ([System.IO.File]::ReadAllText((Join-Path $transactionInstallPath 'settings.json'), [System.Text.Encoding]::UTF8)) 'Prepared rollback preserves files not yet snapshotted after quiescence'

    [System.IO.File]::WriteAllText((Join-Path $transactionInstallPath 'settings.json'), '{"quiesced":true}', (New-Object System.Text.UTF8Encoding($false)))
    $rollbackTransaction = Start-CountdownInstallTransaction -InstallPath $transactionInstallPath -LauncherPath $transactionLauncherPath -LegacyLauncherPath $transactionLegacyLauncherPath -TaskName "Minmin Countdown Test $transactionSuffix" -LegacyTaskName "敏敏子倒计时 Test $transactionSuffix"
    $rollbackTransaction = Capture-CountdownInstallPayloadSnapshot -TransactionPath $rollbackTransaction.transactionPath -ExpectedInstallPath $transactionInstallPath -ExpectedTaskName "Minmin Countdown Test $transactionSuffix" -ExpectedLegacyTaskName "敏敏子倒计时 Test $transactionSuffix" -ExpectedLauncherPath $transactionLauncherPath -ExpectedLegacyLauncherPath $transactionLegacyLauncherPath -ExpectedLauncherTempPath $transactionLauncherTempPath
    Assert-Equal $true ([bool] $rollbackTransaction.payloadSnapshotReady) 'Transaction snapshots mutable files only after the caller has quiesced the old application'
    Assert-Equal $true (Test-Path -LiteralPath $rollbackTransaction.transactionPath) 'Transaction retains backup until external acceptance or rollback'
    $deferredReceiptPath = Join-Path $transactionTestRoot 'deferred-install-receipt.json'
    $deferredReceipt = Write-CountdownDeferredInstallReceipt -Path $deferredReceiptPath -TransactionPath $rollbackTransaction.transactionPath -LaunchToken ([guid]::NewGuid().ToString('N')) -InteractiveSessionId 1
    $readDeferredReceipt = Read-CountdownDeferredInstallReceipt -Path $deferredReceiptPath
    Assert-Equal $rollbackTransaction.transactionPath $readDeferredReceipt.transactionPath 'Deferred install receipt preserves rollback transaction path'
    Assert-Equal $deferredReceipt.launchToken $readDeferredReceipt.launchToken 'Deferred install receipt preserves startup token'
    [System.IO.File]::WriteAllText($deferredReceiptPath, '{bad-json', (New-Object System.Text.UTF8Encoding($false)))
    Assert-Equal $true ($null -eq (Read-CountdownDeferredInstallReceipt -Path $deferredReceiptPath)) 'Malformed deferred install receipt is rejected'
    $failureReceiptPath = Join-Path $transactionTestRoot 'deployment-failure.json'
    $failureReceiptRecord = $null
    $failureReceiptException = [System.InvalidOperationException]::new('failure receipt sentinel')
    $failureReceiptException.Data['do-not-serialize'] = 'sentinel-secret'
    try {
        throw $failureReceiptException
    }
    catch {
        $failureReceiptRecord = $_
    }
    $failureReceipt = Write-CountdownInstallFailureReceipt `
        -Path $failureReceiptPath `
        -Stage 'replace-desktop-launcher' `
        -ErrorRecord $failureReceiptRecord `
        -TransactionStarted $true `
        -CommitState 'not-committed' `
        -RollbackAttempted $true `
        -RollbackSucceeded $true `
        -PreservedUnmanagedLaunchers $false
    $failureReceiptText = [System.IO.File]::ReadAllText($failureReceiptPath, [System.Text.Encoding]::UTF8)
    $readFailureReceipt = $failureReceiptText | ConvertFrom-Json
    Assert-Equal 2 ([int] $readFailureReceipt.schemaVersion) 'Failure receipt uses a stable schema version'
    Assert-Equal 'replace-desktop-launcher' ([string] $readFailureReceipt.stage) 'Failure receipt records the precise installer stage'
    Assert-Equal 'failure receipt sentinel' ([string] $readFailureReceipt.exceptionMessage) 'Failure receipt preserves the original exception message'
    Assert-Equal $true (-not [string]::IsNullOrWhiteSpace([string] $readFailureReceipt.fullyQualifiedErrorId)) 'Failure receipt preserves the fully qualified error ID'
    Assert-Equal $true (-not [string]::IsNullOrWhiteSpace([string] $readFailureReceipt.positionMessage)) 'Failure receipt preserves the original error position'
    Assert-Equal $true (-not [string]::IsNullOrWhiteSpace([string] $readFailureReceipt.scriptStackTrace)) 'Failure receipt preserves the original script stack'
    Assert-Equal $true ([bool] $readFailureReceipt.transactionStarted -and [string] $readFailureReceipt.commitState -eq 'not-committed' -and [bool] $readFailureReceipt.rollbackAttempted -and [bool] $readFailureReceipt.rollbackSucceeded) 'Failure receipt records transactional rollback state'
    Assert-Equal $false ([bool] $readFailureReceipt.preservedUnmanagedLaunchers) 'Failure receipt records launcher preservation state'
    Assert-Equal $true ($failureReceiptText -notmatch 'sentinel-secret') 'Failure receipt serializes selected diagnostics rather than exception data'
    Assert-Equal 'replace-desktop-launcher' ([string] $failureReceipt.stage) 'Failure receipt writer returns the persisted receipt'
    Assert-Equal $true ([System.Text.UTF8Encoding]::new($false).GetByteCount($failureReceiptText) -le 24576) 'Failure receipt remains below the remote retrieval ceiling'
    $validatedFailureReceipt = ConvertFrom-CountdownInstallFailureReceiptBytes -Bytes ([System.Text.UTF8Encoding]::new($false).GetBytes($failureReceiptText))
    Assert-Equal 'not-committed' ([string] $validatedFailureReceipt.commitState) 'Failure receipt parser accepts the writer output'
    $untrustedFailureReceipt = [ordered]@{}
    foreach ($property in $readFailureReceipt.PSObject.Properties) {
        $untrustedFailureReceipt[$property.Name] = $property.Value
    }
    $untrustedFailureReceipt['unexpected'] = 'do-not-persist'
    $untrustedFailureReceiptRejected = $false
    try {
        ConvertFrom-CountdownInstallFailureReceiptBytes -Bytes ([System.Text.UTF8Encoding]::new($false).GetBytes(($untrustedFailureReceipt | ConvertTo-Json -Depth 4))) | Out-Null
    }
    catch {
        $untrustedFailureReceiptRejected = $_.Exception.Message -match '字段数量无效|未允许字段'
    }
    Assert-Equal $true $untrustedFailureReceiptRejected 'Failure receipt parser rejects non-whitelisted fields'
    $invalidStateReceipt = [ordered]@{}
    foreach ($property in $readFailureReceipt.PSObject.Properties) {
        $invalidStateReceipt[$property.Name] = $property.Value
    }
    $invalidStateReceipt.commitState = 'unknown'
    $invalidStateReceipt.rollbackSucceeded = $true
    $invalidStateReceiptRejected = $false
    try {
        ConvertFrom-CountdownInstallFailureReceiptBytes -Bytes ([System.Text.UTF8Encoding]::new($false).GetBytes(($invalidStateReceipt | ConvertTo-Json -Depth 4))) | Out-Null
    }
    catch {
        $invalidStateReceiptRejected = $_.Exception.Message -match '不能声称回滚成功'
    }
    Assert-Equal $true $invalidStateReceiptRejected 'Failure receipt parser rejects an unknown commit state that claims rollback success'
    $invalidUtf8ReceiptRejected = $false
    try {
        ConvertFrom-CountdownInstallFailureReceiptBytes -Bytes ([byte[]] @(0xC3, 0x28)) | Out-Null
    }
    catch {
        $invalidUtf8ReceiptRejected = $_.Exception.Message -match '有效 UTF-8 JSON'
    }
    Assert-Equal $true $invalidUtf8ReceiptRejected 'Failure receipt parser rejects invalid UTF-8'
    $longFailureReceiptRecord = $null
    try {
        throw [System.InvalidOperationException]::new((('诊断' * 8000) -join ''))
    }
    catch {
        $longFailureReceiptRecord = $_
    }
    $longFailureReceiptPath = Join-Path $transactionTestRoot 'deployment-failure-long.json'
    $longFailureReceipt = Write-CountdownInstallFailureReceipt `
        -Path $longFailureReceiptPath `
        -Stage (('阶段' * 100) -join '') `
        -ErrorRecord $longFailureReceiptRecord `
        -TransactionStarted $true `
        -CommitState 'not-committed' `
        -RollbackAttempted $true `
        -RollbackSucceeded $true `
        -PreservedUnmanagedLaunchers $false
    $longFailureReceiptBytes = [System.IO.File]::ReadAllBytes($longFailureReceiptPath)
    Assert-Equal $true ($longFailureReceiptBytes.Length -le 24576) 'Oversized diagnostics remain within the 24 KiB receipt contract'
    Assert-Equal $true (([System.Text.UTF8Encoding]::new($false)).GetByteCount([string] $longFailureReceipt.stage) -le 128) 'Failure receipt truncates stage by UTF-8 bytes'
    Assert-Equal $true (([System.Text.UTF8Encoding]::new($false)).GetByteCount([string] $longFailureReceipt.exceptionMessage) -le 1024) 'Failure receipt truncates exception messages by UTF-8 bytes'
    $candidateLauncherSourcePath = Join-Path $transactionTestRoot 'candidate-launcher.exe'
    [System.IO.File]::WriteAllText($candidateLauncherSourcePath, 'new launcher', (New-Object System.Text.UTF8Encoding($false)))
    $candidateLauncherHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $candidateLauncherSourcePath).Hash
    $rollbackTransaction = Set-CountdownInstallTransactionCandidateLauncherHash -TransactionPath $rollbackTransaction.transactionPath -CandidateLauncherSha256 $candidateLauncherHash -ExpectedInstallPath $transactionInstallPath -ExpectedTaskName "Minmin Countdown Test $transactionSuffix" -ExpectedLegacyTaskName "敏敏子倒计时 Test $transactionSuffix" -ExpectedLauncherPath $transactionLauncherPath -ExpectedLegacyLauncherPath $transactionLegacyLauncherPath -ExpectedLauncherTempPath $transactionLauncherTempPath
    Assert-Equal 'launcher-candidate-staged' $rollbackTransaction.phase 'Transaction persists candidate launcher identity before replacement'
    [System.IO.File]::WriteAllText((Join-Path $transactionInstallPath 'settings.json'), '{"after":true}', (New-Object System.Text.UTF8Encoding($false)))
    Remove-Item -LiteralPath (Join-Path $transactionInstallPath '敏敏子倒计时.ps1') -Force
    [System.IO.File]::WriteAllText((Join-Path $transactionInstallPath 'new-file.txt'), 'new payload', (New-Object System.Text.UTF8Encoding($false)))
    [System.IO.File]::WriteAllText($transactionLauncherPath, 'new launcher', (New-Object System.Text.UTF8Encoding($false)))
    Remove-Item -LiteralPath $transactionLauncherTempPath -Force
    Remove-Item -LiteralPath $transactionLegacyLauncherPath -Force
    Undo-CountdownInstallTransaction -TransactionPath $rollbackTransaction.transactionPath -PowerShell7Path $transactionPowerShell -ExpectedInstallPath $transactionInstallPath -ExpectedTaskName "Minmin Countdown Test $transactionSuffix" -ExpectedLegacyTaskName "敏敏子倒计时 Test $transactionSuffix" -ExpectedLauncherPath $transactionLauncherPath -ExpectedLegacyLauncherPath $transactionLegacyLauncherPath -ExpectedLauncherTempPath $transactionLauncherTempPath -RestoreWindowsTimeAction { param($snapshot) }
    Assert-Equal '{"quiesced":true}' ([System.IO.File]::ReadAllText((Join-Path $transactionInstallPath 'settings.json'), [System.Text.Encoding]::UTF8)) 'Transaction rollback restores the stable post-quiescence install tree'
    Assert-Equal $true (Test-Path -LiteralPath (Join-Path $transactionInstallPath '敏敏子倒计时.ps1')) 'Transaction rollback restores application entry point'
    Assert-Equal $false (Test-Path -LiteralPath (Join-Path $transactionInstallPath 'new-file.txt')) 'Transaction rollback removes new install files'
    Assert-Equal 'old launcher' ([System.IO.File]::ReadAllText($transactionLauncherPath, [System.Text.Encoding]::UTF8)) 'Transaction rollback restores desktop launcher'
    Assert-Equal 'old staged launcher' ([System.IO.File]::ReadAllText($transactionLauncherTempPath, [System.Text.Encoding]::UTF8)) 'Transaction rollback restores desktop launcher staging file'
    Assert-Equal 'old legacy launcher' ([System.IO.File]::ReadAllText($transactionLegacyLauncherPath, [System.Text.Encoding]::UTF8)) 'Transaction rollback restores legacy launcher'
    Assert-Equal $false (Test-Path -LiteralPath $rollbackTransaction.transactionPath) 'Transaction rollback clears its journal only after restoration'
    $ownershipTransaction = Start-CountdownInstallTransaction -InstallPath $transactionInstallPath -LauncherPath $transactionLauncherPath -LegacyLauncherPath $transactionLegacyLauncherPath -TaskName "Minmin Countdown Ownership $transactionSuffix" -LegacyTaskName "敏敏子倒计时 Ownership $transactionSuffix"
    $ownershipTransaction = Capture-CountdownInstallPayloadSnapshot -TransactionPath $ownershipTransaction.transactionPath -ExpectedInstallPath $transactionInstallPath -ExpectedTaskName "Minmin Countdown Ownership $transactionSuffix" -ExpectedLegacyTaskName "敏敏子倒计时 Ownership $transactionSuffix" -ExpectedLauncherPath $transactionLauncherPath -ExpectedLegacyLauncherPath $transactionLegacyLauncherPath -ExpectedLauncherTempPath $transactionLauncherTempPath
    $ownershipTransaction = Set-CountdownInstallTransactionCandidateLauncherHash -TransactionPath $ownershipTransaction.transactionPath -CandidateLauncherSha256 $candidateLauncherHash -ExpectedInstallPath $transactionInstallPath -ExpectedTaskName "Minmin Countdown Ownership $transactionSuffix" -ExpectedLegacyTaskName "敏敏子倒计时 Ownership $transactionSuffix" -ExpectedLauncherPath $transactionLauncherPath -ExpectedLegacyLauncherPath $transactionLegacyLauncherPath -ExpectedLauncherTempPath $transactionLauncherTempPath
    [System.IO.File]::WriteAllText($transactionLauncherPath, 'user replacement', (New-Object System.Text.UTF8Encoding($false)))
    $ownershipRejected = $false
    try {
        Undo-CountdownInstallTransaction -TransactionPath $ownershipTransaction.transactionPath -PowerShell7Path $transactionPowerShell -ExpectedInstallPath $transactionInstallPath -ExpectedTaskName "Minmin Countdown Ownership $transactionSuffix" -ExpectedLegacyTaskName "敏敏子倒计时 Ownership $transactionSuffix" -ExpectedLauncherPath $transactionLauncherPath -ExpectedLegacyLauncherPath $transactionLegacyLauncherPath -ExpectedLauncherTempPath $transactionLauncherTempPath -RestoreWindowsTimeAction { param($snapshot) }
    }
    catch {
        $ownershipRejected = $_.Exception.Message -match '拒绝恢复 桌面启动器'
    }
    Assert-Equal $true $ownershipRejected 'Rollback refuses to overwrite a user-replaced desktop launcher after a crash'
    Assert-Equal 'user replacement' ([System.IO.File]::ReadAllText($transactionLauncherPath, [System.Text.Encoding]::UTF8)) 'Rejected launcher recovery preserves the user replacement'
    Assert-Equal $true (Test-Path -LiteralPath $ownershipTransaction.transactionPath) 'Rejected launcher recovery retains journal evidence'
    [System.IO.File]::WriteAllText((Join-Path $transactionInstallPath 'settings.json'), '{"partial-new":true}', (New-Object System.Text.UTF8Encoding($false)))
    Remove-Item -LiteralPath (Join-Path $transactionInstallPath '敏敏子倒计时.ps1') -Force
    $partialRollback = Undo-CountdownInstallTransaction -TransactionPath $ownershipTransaction.transactionPath -PowerShell7Path $transactionPowerShell -ExpectedInstallPath $transactionInstallPath -ExpectedTaskName "Minmin Countdown Ownership $transactionSuffix" -ExpectedLegacyTaskName "敏敏子倒计时 Ownership $transactionSuffix" -ExpectedLauncherPath $transactionLauncherPath -ExpectedLegacyLauncherPath $transactionLegacyLauncherPath -ExpectedLauncherTempPath $transactionLauncherTempPath -PreserveUnmanagedLaunchers -RestoreWindowsTimeAction { param($snapshot) }
    Assert-Equal $true ([bool] $partialRollback.PreservedUnmanagedLaunchers) 'Conflict rollback restores non-launcher state without overwriting a user launcher'
    Assert-Equal '{"quiesced":true}' ([System.IO.File]::ReadAllText((Join-Path $transactionInstallPath 'settings.json'), [System.Text.Encoding]::UTF8)) 'Conflict rollback restores the stable install tree'
    Assert-Equal $true (Test-Path -LiteralPath (Join-Path $transactionInstallPath '敏敏子倒计时.ps1')) 'Conflict rollback restores the application entry point'
    Assert-Equal 'user replacement' ([System.IO.File]::ReadAllText($transactionLauncherPath, [System.Text.Encoding]::UTF8)) 'Conflict rollback continues to preserve the user launcher'
    $partialRollbackJournal = [System.IO.File]::ReadAllText((Join-Path $ownershipTransaction.transactionPath 'journal.json'), [System.Text.Encoding]::UTF8) | ConvertFrom-Json
    Assert-Equal 'launcher-ownership-conflict' $partialRollbackJournal.phase 'Conflict rollback retains a dedicated journal phase for safe later recovery'
    Copy-Item -LiteralPath $candidateLauncherSourcePath -Destination $transactionLauncherPath -Force
    Undo-CountdownInstallTransaction -TransactionPath $ownershipTransaction.transactionPath -PowerShell7Path $transactionPowerShell -ExpectedInstallPath $transactionInstallPath -ExpectedTaskName "Minmin Countdown Ownership $transactionSuffix" -ExpectedLegacyTaskName "敏敏子倒计时 Ownership $transactionSuffix" -ExpectedLauncherPath $transactionLauncherPath -ExpectedLegacyLauncherPath $transactionLegacyLauncherPath -ExpectedLauncherTempPath $transactionLauncherTempPath -RestoreWindowsTimeAction { param($snapshot) }
    $recoveryConflictInstallPath = Join-Path $recoveryConflictRoot 'install'
    $recoveryConflictDesktopPath = Join-Path $recoveryConflictRoot 'desktop'
    $recoveryConflictLauncherPath = Join-Path $recoveryConflictDesktopPath '敏敏子倒计时.exe'
    $recoveryConflictLauncherTempPath = "$recoveryConflictLauncherPath.new"
    $recoveryConflictLegacyLauncherPath = Join-Path $recoveryConflictDesktopPath '敏敏子倒计时.cmd'
    New-Item -ItemType Directory -Path $recoveryConflictInstallPath, $recoveryConflictDesktopPath -Force | Out-Null
    [System.IO.File]::WriteAllText((Join-Path $recoveryConflictInstallPath 'settings.json'), '{"isolated-before":true}', (New-Object System.Text.UTF8Encoding($false)))
    [System.IO.File]::WriteAllText((Join-Path $recoveryConflictInstallPath '敏敏子倒计时.ps1'), '# isolated old app', (New-Object System.Text.UTF8Encoding($false)))
    [System.IO.File]::WriteAllText($recoveryConflictLauncherPath, 'isolated old launcher', (New-Object System.Text.UTF8Encoding($false)))
    [System.IO.File]::WriteAllText($recoveryConflictLauncherTempPath, 'isolated old staged launcher', (New-Object System.Text.UTF8Encoding($false)))
    [System.IO.File]::WriteAllText($recoveryConflictLegacyLauncherPath, 'isolated old legacy launcher', (New-Object System.Text.UTF8Encoding($false)))
    $recoveryConflictTransaction = Start-CountdownInstallTransaction -InstallPath $recoveryConflictInstallPath -LauncherPath $recoveryConflictLauncherPath -LegacyLauncherPath $recoveryConflictLegacyLauncherPath -TaskName "Minmin Countdown Recovery Conflict $transactionSuffix" -LegacyTaskName "敏敏子倒计时 Recovery Conflict $transactionSuffix"
    $recoveryConflictTransaction = Capture-CountdownInstallPayloadSnapshot -TransactionPath $recoveryConflictTransaction.transactionPath -ExpectedInstallPath $recoveryConflictInstallPath -ExpectedTaskName "Minmin Countdown Recovery Conflict $transactionSuffix" -ExpectedLegacyTaskName "敏敏子倒计时 Recovery Conflict $transactionSuffix" -ExpectedLauncherPath $recoveryConflictLauncherPath -ExpectedLegacyLauncherPath $recoveryConflictLegacyLauncherPath -ExpectedLauncherTempPath $recoveryConflictLauncherTempPath
    $recoveryConflictTransaction = Set-CountdownInstallTransactionCandidateLauncherHash -TransactionPath $recoveryConflictTransaction.transactionPath -CandidateLauncherSha256 $candidateLauncherHash -ExpectedInstallPath $recoveryConflictInstallPath -ExpectedTaskName "Minmin Countdown Recovery Conflict $transactionSuffix" -ExpectedLegacyTaskName "敏敏子倒计时 Recovery Conflict $transactionSuffix" -ExpectedLauncherPath $recoveryConflictLauncherPath -ExpectedLegacyLauncherPath $recoveryConflictLegacyLauncherPath -ExpectedLauncherTempPath $recoveryConflictLauncherTempPath
    [System.IO.File]::WriteAllText((Join-Path $recoveryConflictInstallPath 'settings.json'), '{"isolated-new":true}', (New-Object System.Text.UTF8Encoding($false)))
    [System.IO.File]::WriteAllText($recoveryConflictLauncherPath, 'isolated user replacement', (New-Object System.Text.UTF8Encoding($false)))
    $recoveryConflictPartialRollback = Undo-CountdownInstallTransaction -TransactionPath $recoveryConflictTransaction.transactionPath -PowerShell7Path $transactionPowerShell -ExpectedInstallPath $recoveryConflictInstallPath -ExpectedTaskName "Minmin Countdown Recovery Conflict $transactionSuffix" -ExpectedLegacyTaskName "敏敏子倒计时 Recovery Conflict $transactionSuffix" -ExpectedLauncherPath $recoveryConflictLauncherPath -ExpectedLegacyLauncherPath $recoveryConflictLegacyLauncherPath -ExpectedLauncherTempPath $recoveryConflictLauncherTempPath -PreserveUnmanagedLaunchers -RestoreWindowsTimeAction { param($snapshot) }
    Assert-Equal $true ([bool] $recoveryConflictPartialRollback.PreservedUnmanagedLaunchers) 'Isolated conflict setup preserves its user launcher during partial rollback'
    if ($ParentTransactionMutexHeld) {
        # 办公室验收的父进程已持有全局 mutex；子进程只能验证它被排除，不能调用 Recover 并等待同一把锁超时。
        Assert-Equal $true $true 'Parent-held transaction mutex skips recovery execution that requires the parent lock'
    }
    else {
        $recoveryConflictStatus = Recover-CountdownInstallTransactions -InstallPath $recoveryConflictInstallPath -PowerShell7Path $transactionPowerShell -ExpectedTaskName "Minmin Countdown Recovery Conflict $transactionSuffix" -ExpectedLegacyTaskName "敏敏子倒计时 Recovery Conflict $transactionSuffix" -ExpectedLauncherPath $recoveryConflictLauncherPath -ExpectedLegacyLauncherPath $recoveryConflictLegacyLauncherPath -ExpectedLauncherTempPath $recoveryConflictLauncherTempPath
        Assert-Equal 0 @($recoveryConflictStatus.RecoveredTransactionPaths).Count 'Recovery does not falsely report a user-launcher conflict as complete'
        Assert-Equal 1 @($recoveryConflictStatus.UnresolvedLauncherOwnershipConflicts).Count 'Recovery returns a structured unresolved launcher conflict'
        Assert-Equal 'isolated user replacement' ([System.IO.File]::ReadAllText($recoveryConflictLauncherPath, [System.Text.Encoding]::UTF8)) 'Recovery status probing does not overwrite the isolated user launcher'
    }
    $strictBooleanTransaction = Start-CountdownInstallTransaction -InstallPath $transactionInstallPath -LauncherPath $transactionLauncherPath -LegacyLauncherPath $transactionLegacyLauncherPath -TaskName "Minmin Countdown Strict Boolean $transactionSuffix" -LegacyTaskName "敏敏子倒计时 Strict Boolean $transactionSuffix"
    $strictBooleanJournalPath = Join-Path $strictBooleanTransaction.transactionPath 'journal.json'
    $strictBooleanJournal = [System.IO.File]::ReadAllText($strictBooleanJournalPath, [System.Text.Encoding]::UTF8) | ConvertFrom-Json
    $strictBooleanJournal.payloadSnapshotReady = 'false'
    [System.IO.File]::WriteAllText($strictBooleanJournalPath, ($strictBooleanJournal | ConvertTo-Json -Depth 8), (New-Object System.Text.UTF8Encoding($false)))
    $strictBooleanRejected = $false
    try {
        Undo-CountdownInstallTransaction -TransactionPath $strictBooleanTransaction.transactionPath -PowerShell7Path $transactionPowerShell -ExpectedInstallPath $transactionInstallPath -ExpectedTaskName "Minmin Countdown Strict Boolean $transactionSuffix" -ExpectedLegacyTaskName "敏敏子倒计时 Strict Boolean $transactionSuffix" -ExpectedLauncherPath $transactionLauncherPath -ExpectedLegacyLauncherPath $transactionLegacyLauncherPath -ExpectedLauncherTempPath $transactionLauncherTempPath -RestoreWindowsTimeAction { param($snapshot) }
    }
    catch {
        $strictBooleanRejected = $_.Exception.Message -match '严格布尔字段 payloadSnapshotReady'
    }
    Assert-Equal $true $strictBooleanRejected 'Rollback rejects a string payloadSnapshotReady before deleting any files'
    Assert-Equal '{"quiesced":true}' ([System.IO.File]::ReadAllText((Join-Path $transactionInstallPath 'settings.json'), [System.Text.Encoding]::UTF8)) 'Malformed prepared journal leaves current install tree intact'
    $missingBackupTransaction = Start-CountdownInstallTransaction -InstallPath $transactionInstallPath -LauncherPath $transactionLauncherPath -LegacyLauncherPath $transactionLegacyLauncherPath -TaskName "Minmin Countdown Missing Backup $transactionSuffix" -LegacyTaskName "敏敏子倒计时 Missing Backup $transactionSuffix"
    $missingBackupTransaction = Capture-CountdownInstallPayloadSnapshot -TransactionPath $missingBackupTransaction.transactionPath -ExpectedInstallPath $transactionInstallPath -ExpectedTaskName "Minmin Countdown Missing Backup $transactionSuffix" -ExpectedLegacyTaskName "敏敏子倒计时 Missing Backup $transactionSuffix" -ExpectedLauncherPath $transactionLauncherPath -ExpectedLegacyLauncherPath $transactionLegacyLauncherPath -ExpectedLauncherTempPath $transactionLauncherTempPath
    [System.IO.File]::WriteAllText((Join-Path $transactionInstallPath 'settings.json'), '{"must-not-delete":true}', (New-Object System.Text.UTF8Encoding($false)))
    Remove-Item -LiteralPath $missingBackupTransaction.backupInstallPath -Recurse -Force
    $missingBackupRejected = $false
    try {
        Undo-CountdownInstallTransaction -TransactionPath $missingBackupTransaction.transactionPath -PowerShell7Path $transactionPowerShell -ExpectedInstallPath $transactionInstallPath -ExpectedTaskName "Minmin Countdown Missing Backup $transactionSuffix" -ExpectedLegacyTaskName "敏敏子倒计时 Missing Backup $transactionSuffix" -ExpectedLauncherPath $transactionLauncherPath -ExpectedLegacyLauncherPath $transactionLegacyLauncherPath -ExpectedLauncherTempPath $transactionLauncherTempPath -RestoreWindowsTimeAction { param($snapshot) }
    }
    catch {
        $missingBackupRejected = $_.Exception.Message -match '缺少安装目录备份'
    }
    Assert-Equal $true $missingBackupRejected 'Rollback verifies install backup before deleting the current install tree'
    Assert-Equal '{"must-not-delete":true}' ([System.IO.File]::ReadAllText((Join-Path $transactionInstallPath 'settings.json'), [System.Text.Encoding]::UTF8)) 'Missing backup leaves current install tree intact'
    $invalidTaskXmlTransaction = Start-CountdownInstallTransaction -InstallPath $transactionInstallPath -LauncherPath $transactionLauncherPath -LegacyLauncherPath $transactionLegacyLauncherPath -TaskName "Minmin Countdown Invalid XML $transactionSuffix" -LegacyTaskName "敏敏子倒计时 Invalid XML $transactionSuffix"
    $invalidTaskXmlJournalPath = Join-Path $invalidTaskXmlTransaction.transactionPath 'journal.json'
    $invalidTaskXmlJournal = [System.IO.File]::ReadAllText($invalidTaskXmlJournalPath, [System.Text.Encoding]::UTF8) | ConvertFrom-Json
    $invalidTaskXmlJournal.currentTask.Exists = $true
    $invalidTaskXmlJournal.currentTask.State = 'Ready'
    [System.IO.File]::WriteAllText([string] $invalidTaskXmlJournal.currentTask.XmlPath, '<Task>', (New-Object System.Text.UTF8Encoding($false)))
    [System.IO.File]::WriteAllText($invalidTaskXmlJournalPath, ($invalidTaskXmlJournal | ConvertTo-Json -Depth 8), (New-Object System.Text.UTF8Encoding($false)))
    [System.IO.File]::WriteAllText((Join-Path $transactionInstallPath 'settings.json'), '{"task-xml-must-not-delete":true}', (New-Object System.Text.UTF8Encoding($false)))
    $invalidTaskXmlRejected = $false
    try {
        Undo-CountdownInstallTransaction -TransactionPath $invalidTaskXmlTransaction.transactionPath -PowerShell7Path $transactionPowerShell -ExpectedInstallPath $transactionInstallPath -ExpectedTaskName "Minmin Countdown Invalid XML $transactionSuffix" -ExpectedLegacyTaskName "敏敏子倒计时 Invalid XML $transactionSuffix" -ExpectedLauncherPath $transactionLauncherPath -ExpectedLegacyLauncherPath $transactionLegacyLauncherPath -ExpectedLauncherTempPath $transactionLauncherTempPath -RestoreWindowsTimeAction { param($snapshot) }
    }
    catch {
        $invalidTaskXmlRejected = $_.Exception.Message -match 'currentTask XML 无法解析'
    }
    Assert-Equal $true $invalidTaskXmlRejected 'Rollback rejects a truncated task XML before any destructive action'
    Assert-Equal '{"task-xml-must-not-delete":true}' ([System.IO.File]::ReadAllText((Join-Path $transactionInstallPath 'settings.json'), [System.Text.Encoding]::UTF8)) 'Invalid task XML leaves the current install tree intact'
    Assert-Equal $true (Test-Path -LiteralPath $invalidTaskXmlTransaction.transactionPath) 'Invalid task XML keeps journal evidence for manual repair'
    $identityTransaction = Start-CountdownInstallTransaction -InstallPath $transactionInstallPath -LauncherPath $transactionLauncherPath -LegacyLauncherPath $transactionLegacyLauncherPath -TaskName "Minmin Countdown Identity $transactionSuffix" -LegacyTaskName "敏敏子倒计时 Identity $transactionSuffix"
    $identityRejected = $false
    try {
        Undo-CountdownInstallTransaction -TransactionPath $identityTransaction.transactionPath -PowerShell7Path $transactionPowerShell -ExpectedInstallPath $transactionInstallPath -ExpectedTaskName 'unexpected task' -ExpectedLegacyTaskName "敏敏子倒计时 Identity $transactionSuffix" -ExpectedLauncherPath $transactionLauncherPath -ExpectedLegacyLauncherPath $transactionLegacyLauncherPath -ExpectedLauncherTempPath $transactionLauncherTempPath -RestoreWindowsTimeAction { param($snapshot) }
    }
    catch {
        $identityRejected = $_.Exception.Message -match 'taskName 与当前安装任务不一致'
    }
    Assert-Equal $true $identityRejected 'Transaction rollback binds its journal to the expected task identities'
    $launcherIdentityRejected = $false
    try {
        Undo-CountdownInstallTransaction -TransactionPath $identityTransaction.transactionPath -PowerShell7Path $transactionPowerShell -ExpectedInstallPath $transactionInstallPath -ExpectedTaskName "Minmin Countdown Identity $transactionSuffix" -ExpectedLegacyTaskName "敏敏子倒计时 Identity $transactionSuffix" -ExpectedLauncherPath (Join-Path $transactionDesktopPath 'other-launcher.exe') -ExpectedLegacyLauncherPath $transactionLegacyLauncherPath -ExpectedLauncherTempPath $transactionLauncherTempPath -RestoreWindowsTimeAction { param($snapshot) }
    }
    catch {
        $launcherIdentityRejected = $_.Exception.Message -match 'launcherPath 与当前安装目标不一致'
    }
    Assert-Equal $true $launcherIdentityRejected 'Transaction rollback binds its journal to the expected launcher paths'
    Undo-CountdownInstallTransaction -TransactionPath $identityTransaction.transactionPath -PowerShell7Path $transactionPowerShell -ExpectedInstallPath $transactionInstallPath -ExpectedTaskName "Minmin Countdown Identity $transactionSuffix" -ExpectedLegacyTaskName "敏敏子倒计时 Identity $transactionSuffix" -ExpectedLauncherPath $transactionLauncherPath -ExpectedLegacyLauncherPath $transactionLegacyLauncherPath -ExpectedLauncherTempPath $transactionLauncherTempPath -RestoreWindowsTimeAction { param($snapshot) }
    $commitTransaction = Start-CountdownInstallTransaction -InstallPath $transactionInstallPath -LauncherPath $transactionLauncherPath -LegacyLauncherPath $transactionLegacyLauncherPath -TaskName "Minmin Countdown Commit $transactionSuffix" -LegacyTaskName "敏敏子倒计时 Commit $transactionSuffix"
    $commitTransaction = Capture-CountdownInstallPayloadSnapshot -TransactionPath $commitTransaction.transactionPath -ExpectedInstallPath $transactionInstallPath -ExpectedTaskName "Minmin Countdown Commit $transactionSuffix" -ExpectedLegacyTaskName "敏敏子倒计时 Commit $transactionSuffix" -ExpectedLauncherPath $transactionLauncherPath -ExpectedLegacyLauncherPath $transactionLegacyLauncherPath -ExpectedLauncherTempPath $transactionLauncherTempPath
    $commitTransaction = Set-CountdownInstallTransactionCandidateLauncherHash -TransactionPath $commitTransaction.transactionPath -CandidateLauncherSha256 $candidateLauncherHash -ExpectedInstallPath $transactionInstallPath -ExpectedTaskName "Minmin Countdown Commit $transactionSuffix" -ExpectedLegacyTaskName "敏敏子倒计时 Commit $transactionSuffix" -ExpectedLauncherPath $transactionLauncherPath -ExpectedLegacyLauncherPath $transactionLegacyLauncherPath -ExpectedLauncherTempPath $transactionLauncherTempPath
    Copy-Item -LiteralPath $candidateLauncherSourcePath -Destination $transactionLauncherPath -Force
    Complete-CountdownInstallTransaction -TransactionPath $commitTransaction.transactionPath -ExpectedInstallPath $transactionInstallPath -ExpectedTaskName "Minmin Countdown Commit $transactionSuffix" -ExpectedLegacyTaskName "敏敏子倒计时 Commit $transactionSuffix" -ExpectedLauncherPath $transactionLauncherPath -ExpectedLegacyLauncherPath $transactionLegacyLauncherPath -ExpectedLauncherTempPath $transactionLauncherTempPath
    Assert-Equal $true (Test-Path -LiteralPath $commitTransaction.transactionPath) 'Transaction commit retains rollback evidence until application acceptance acknowledgement'
    $committedJournal = Get-Content -LiteralPath (Join-Path $commitTransaction.transactionPath 'journal.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    Assert-Equal 'accepted' ([string] $committedJournal.phase) 'Transaction remains rollbackable while waiting for application acknowledgement'
    Remove-CountdownCommittedInstallTransaction -TransactionPath $commitTransaction.transactionPath -ExpectedInstallPath $transactionInstallPath -ExpectedTaskName "Minmin Countdown Commit $transactionSuffix" -ExpectedLegacyTaskName "敏敏子倒计时 Commit $transactionSuffix" -ExpectedLauncherPath $transactionLauncherPath -ExpectedLegacyLauncherPath $transactionLegacyLauncherPath -ExpectedLauncherTempPath $transactionLauncherTempPath
    Assert-Equal $false (Test-Path -LiteralPath $commitTransaction.transactionPath) 'Committed transaction cleanup removes backup only after acknowledgement'
    $committedCleanupInstallPath = Join-Path $committedCleanupRoot 'install'
    $committedCleanupDesktopPath = Join-Path $committedCleanupRoot 'desktop'
    $committedCleanupLauncherPath = Join-Path $committedCleanupDesktopPath '敏敏子倒计时.exe'
    $committedCleanupLauncherTempPath = "$committedCleanupLauncherPath.new"
    $committedCleanupLegacyLauncherPath = Join-Path $committedCleanupDesktopPath '敏敏子倒计时.cmd'
    New-Item -ItemType Directory -Path $committedCleanupInstallPath, $committedCleanupDesktopPath -Force | Out-Null
    [System.IO.File]::WriteAllText((Join-Path $committedCleanupInstallPath 'settings.json'), '{"committed-before":true}', (New-Object System.Text.UTF8Encoding($false)))
    [System.IO.File]::WriteAllText($committedCleanupLauncherPath, 'committed old launcher', (New-Object System.Text.UTF8Encoding($false)))
    $committedCleanupTransaction = Start-CountdownInstallTransaction -InstallPath $committedCleanupInstallPath -LauncherPath $committedCleanupLauncherPath -LegacyLauncherPath $committedCleanupLegacyLauncherPath -TaskName "Minmin Countdown Committed Cleanup $transactionSuffix" -LegacyTaskName "敏敏子倒计时 Committed Cleanup $transactionSuffix"
    $committedCleanupTransaction = Capture-CountdownInstallPayloadSnapshot -TransactionPath $committedCleanupTransaction.transactionPath -ExpectedInstallPath $committedCleanupInstallPath -ExpectedTaskName "Minmin Countdown Committed Cleanup $transactionSuffix" -ExpectedLegacyTaskName "敏敏子倒计时 Committed Cleanup $transactionSuffix" -ExpectedLauncherPath $committedCleanupLauncherPath -ExpectedLegacyLauncherPath $committedCleanupLegacyLauncherPath -ExpectedLauncherTempPath $committedCleanupLauncherTempPath
    $committedCleanupTransaction = Set-CountdownInstallTransactionCandidateLauncherHash -TransactionPath $committedCleanupTransaction.transactionPath -CandidateLauncherSha256 $candidateLauncherHash -ExpectedInstallPath $committedCleanupInstallPath -ExpectedTaskName "Minmin Countdown Committed Cleanup $transactionSuffix" -ExpectedLegacyTaskName "敏敏子倒计时 Committed Cleanup $transactionSuffix" -ExpectedLauncherPath $committedCleanupLauncherPath -ExpectedLegacyLauncherPath $committedCleanupLegacyLauncherPath -ExpectedLauncherTempPath $committedCleanupLauncherTempPath
    Copy-Item -LiteralPath $candidateLauncherSourcePath -Destination $committedCleanupLauncherPath -Force
    [System.IO.File]::WriteAllText((Join-Path $committedCleanupInstallPath 'settings.json'), '{"committed-new":true}', (New-Object System.Text.UTF8Encoding($false)))
    Complete-CountdownInstallTransaction -TransactionPath $committedCleanupTransaction.transactionPath -ExpectedInstallPath $committedCleanupInstallPath -ExpectedTaskName "Minmin Countdown Committed Cleanup $transactionSuffix" -ExpectedLegacyTaskName "敏敏子倒计时 Committed Cleanup $transactionSuffix" -ExpectedLauncherPath $committedCleanupLauncherPath -ExpectedLegacyLauncherPath $committedCleanupLegacyLauncherPath -ExpectedLauncherTempPath $committedCleanupLauncherTempPath
    $committedTransactionLeaf = Split-Path -Leaf $committedCleanupTransaction.transactionPath
    $committedTransactionParent = Split-Path -Parent $committedCleanupTransaction.transactionPath
    Remove-CountdownCommittedInstallTransaction -TransactionPath $committedCleanupTransaction.transactionPath -ExpectedInstallPath $committedCleanupInstallPath -ExpectedTaskName "Minmin Countdown Committed Cleanup $transactionSuffix" -ExpectedLegacyTaskName "敏敏子倒计时 Committed Cleanup $transactionSuffix" -ExpectedLauncherPath $committedCleanupLauncherPath -ExpectedLegacyLauncherPath $committedCleanupLegacyLauncherPath -ExpectedLauncherTempPath $committedCleanupLauncherTempPath -CleanupAction { param($path) Remove-Item -LiteralPath (Join-Path $path 'journal.json') -Force; throw "模拟 journal 已删除后的 committed tombstone 清理失败：$path" }
    Assert-Equal $false (Test-Path -LiteralPath $committedCleanupTransaction.transactionPath) 'Committed cleanup atomically renames the transaction before recursive deletion'
    $committedTombstones = @(Get-ChildItem -LiteralPath $committedTransactionParent -Directory -Force | Where-Object { $_.Name -like "$committedTransactionLeaf.committed-*" })
    Assert-Equal 1 $committedTombstones.Count 'Committed cleanup failure retains one tombstone directory'
    Assert-Equal $false (Test-Path -LiteralPath (Join-Path $committedTombstones[0].FullName 'journal.json')) 'Committed cleanup test reproduces a partial deletion that already removed journal.json'
    $committedMarkers = @(Get-ChildItem -LiteralPath $committedTransactionParent -File -Filter "$committedTransactionLeaf.committed-*.marker.json" -Force)
    Assert-Equal 1 $committedMarkers.Count 'External committed cleanup marker survives partial tombstone deletion'
    if (-not $ParentTransactionMutexHeld) {
        $committedRecoveryStatus = Recover-CountdownInstallTransactions -InstallPath $committedCleanupInstallPath -PowerShell7Path $transactionPowerShell -ExpectedTaskName "Minmin Countdown Committed Cleanup $transactionSuffix" -ExpectedLegacyTaskName "敏敏子倒计时 Committed Cleanup $transactionSuffix" -ExpectedLauncherPath $committedCleanupLauncherPath -ExpectedLegacyLauncherPath $committedCleanupLegacyLauncherPath -ExpectedLauncherTempPath $committedCleanupLauncherTempPath -CommittedCleanupAction { param($path) throw "模拟 committed tombstone 重试仍失败：$path" }
        Assert-Equal 0 @($committedRecoveryStatus.RecoveredTransactionPaths).Count 'Recovery never rolls back a committed tombstone'
        Assert-Equal 1 @($committedRecoveryStatus.UnresolvedCommittedTransactions).Count 'Recovery reports a committed tombstone while cleanup remains blocked'
        Assert-Equal '{"committed-new":true}' ([System.IO.File]::ReadAllText((Join-Path $committedCleanupInstallPath 'settings.json'), [System.Text.Encoding]::UTF8)) 'Recovery preserves the accepted install after committed cleanup failure'
        $committedRetryStatus = Recover-CountdownInstallTransactions -InstallPath $committedCleanupInstallPath -PowerShell7Path $transactionPowerShell -ExpectedTaskName "Minmin Countdown Committed Cleanup $transactionSuffix" -ExpectedLegacyTaskName "敏敏子倒计时 Committed Cleanup $transactionSuffix" -ExpectedLauncherPath $committedCleanupLauncherPath -ExpectedLegacyLauncherPath $committedCleanupLegacyLauncherPath -ExpectedLauncherTempPath $committedCleanupLauncherTempPath
        Assert-Equal 0 @($committedRetryStatus.UnresolvedCommittedTransactions).Count 'Recovery automatically clears a committed tombstone after the transient cleanup failure ends'
        Assert-Equal $false (Test-Path -LiteralPath $committedTombstones[0].FullName) 'Committed tombstone retry removes stale transaction evidence without rollback'
        Assert-Equal $false (Test-Path -LiteralPath $committedMarkers[0].FullName) 'Committed tombstone retry removes the external marker only after cleanup succeeds'

        $moveFailureTransaction = Start-CountdownInstallTransaction -InstallPath $committedCleanupInstallPath -LauncherPath $committedCleanupLauncherPath -LegacyLauncherPath $committedCleanupLegacyLauncherPath -TaskName "Minmin Countdown Move Failure $transactionSuffix" -LegacyTaskName "敏敏子倒计时 Move Failure $transactionSuffix"
        $moveFailureTransaction = Capture-CountdownInstallPayloadSnapshot -TransactionPath $moveFailureTransaction.transactionPath -ExpectedInstallPath $committedCleanupInstallPath -ExpectedTaskName "Minmin Countdown Move Failure $transactionSuffix" -ExpectedLegacyTaskName "敏敏子倒计时 Move Failure $transactionSuffix" -ExpectedLauncherPath $committedCleanupLauncherPath -ExpectedLegacyLauncherPath $committedCleanupLegacyLauncherPath -ExpectedLauncherTempPath $committedCleanupLauncherTempPath
        $moveFailureTransaction = Set-CountdownInstallTransactionCandidateLauncherHash -TransactionPath $moveFailureTransaction.transactionPath -CandidateLauncherSha256 $candidateLauncherHash -ExpectedInstallPath $committedCleanupInstallPath -ExpectedTaskName "Minmin Countdown Move Failure $transactionSuffix" -ExpectedLegacyTaskName "敏敏子倒计时 Move Failure $transactionSuffix" -ExpectedLauncherPath $committedCleanupLauncherPath -ExpectedLegacyLauncherPath $committedCleanupLegacyLauncherPath -ExpectedLauncherTempPath $committedCleanupLauncherTempPath
        Copy-Item -LiteralPath $candidateLauncherSourcePath -Destination $committedCleanupLauncherPath -Force
        Complete-CountdownInstallTransaction -TransactionPath $moveFailureTransaction.transactionPath -ExpectedInstallPath $committedCleanupInstallPath -ExpectedTaskName "Minmin Countdown Move Failure $transactionSuffix" -ExpectedLegacyTaskName "敏敏子倒计时 Move Failure $transactionSuffix" -ExpectedLauncherPath $committedCleanupLauncherPath -ExpectedLegacyLauncherPath $committedCleanupLegacyLauncherPath -ExpectedLauncherTempPath $committedCleanupLauncherTempPath
        Remove-CountdownCommittedInstallTransaction -TransactionPath $moveFailureTransaction.transactionPath -ExpectedInstallPath $committedCleanupInstallPath -ExpectedTaskName "Minmin Countdown Move Failure $transactionSuffix" -ExpectedLegacyTaskName "敏敏子倒计时 Move Failure $transactionSuffix" -ExpectedLauncherPath $committedCleanupLauncherPath -ExpectedLegacyLauncherPath $committedCleanupLegacyLauncherPath -ExpectedLauncherTempPath $committedCleanupLauncherTempPath -MoveAction { param($source, $destination) throw "模拟 committed 后目录重命名失败：$source -> $destination" }
        $moveFailureJournal = [System.IO.File]::ReadAllText((Join-Path $moveFailureTransaction.transactionPath 'journal.json'), [System.Text.Encoding]::UTF8) | ConvertFrom-Json
        Assert-Equal 'committed' ([string] $moveFailureJournal.phase) 'Move failure after commit leaves the original transaction permanently committed'
        $committedUndoRejected = $false
        try { Undo-CountdownInstallTransaction -TransactionPath $moveFailureTransaction.transactionPath -PowerShell7Path $transactionPowerShell -ExpectedInstallPath $committedCleanupInstallPath -ExpectedTaskName "Minmin Countdown Move Failure $transactionSuffix" -ExpectedLegacyTaskName "敏敏子倒计时 Move Failure $transactionSuffix" -ExpectedLauncherPath $committedCleanupLauncherPath -ExpectedLegacyLauncherPath $committedCleanupLegacyLauncherPath -ExpectedLauncherTempPath $committedCleanupLauncherTempPath -RestoreWindowsTimeAction { param($snapshot) } }
        catch { $committedUndoRejected = $_.Exception.Message -match '禁止重新进入回滚' }
        Assert-Equal $true $committedUndoRejected 'Undo rejects a committed transaction even when tombstone rename failed'
        $moveFailureCleanupAttempts = [System.Collections.Generic.List[string]]::new()
        $moveFailureRecovery = Recover-CountdownInstallTransactions -InstallPath $committedCleanupInstallPath -PowerShell7Path $transactionPowerShell -ExpectedTaskName "Minmin Countdown Move Failure $transactionSuffix" -ExpectedLegacyTaskName "敏敏子倒计时 Move Failure $transactionSuffix" -ExpectedLauncherPath $committedCleanupLauncherPath -ExpectedLegacyLauncherPath $committedCleanupLegacyLauncherPath -ExpectedLauncherTempPath $committedCleanupLauncherTempPath -CommittedCleanupAction {
            param($path)
            [void] $moveFailureCleanupAttempts.Add($path)
            if ($moveFailureCleanupAttempts.Count -eq 1) { throw "模拟同一次 recovery 的首次清理失败：$path" }
            Remove-Item -LiteralPath $path -Recurse -Force -ErrorAction Stop
        }
        Assert-Equal 2 $moveFailureCleanupAttempts.Count 'Recovery retries the same committed transaction through its sidecar after a transient first failure'
        Assert-Equal 0 @($moveFailureRecovery.UnresolvedCommittedTransactions).Count 'Recovery cleans a committed transaction left at its original path after move failure'
        Assert-Equal $false (Test-Path -LiteralPath $moveFailureTransaction.transactionPath) 'Move-failed committed transaction is cleanup-only and never rolled back'
    }
    $commitReplacementTransaction = Start-CountdownInstallTransaction -InstallPath $transactionInstallPath -LauncherPath $transactionLauncherPath -LegacyLauncherPath $transactionLegacyLauncherPath -TaskName "Minmin Countdown Commit Replacement $transactionSuffix" -LegacyTaskName "敏敏子倒计时 Commit Replacement $transactionSuffix"
    $commitReplacementTransaction = Capture-CountdownInstallPayloadSnapshot -TransactionPath $commitReplacementTransaction.transactionPath -ExpectedInstallPath $transactionInstallPath -ExpectedTaskName "Minmin Countdown Commit Replacement $transactionSuffix" -ExpectedLegacyTaskName "敏敏子倒计时 Commit Replacement $transactionSuffix" -ExpectedLauncherPath $transactionLauncherPath -ExpectedLegacyLauncherPath $transactionLegacyLauncherPath -ExpectedLauncherTempPath $transactionLauncherTempPath
    $commitReplacementTransaction = Set-CountdownInstallTransactionCandidateLauncherHash -TransactionPath $commitReplacementTransaction.transactionPath -CandidateLauncherSha256 $candidateLauncherHash -ExpectedInstallPath $transactionInstallPath -ExpectedTaskName "Minmin Countdown Commit Replacement $transactionSuffix" -ExpectedLegacyTaskName "敏敏子倒计时 Commit Replacement $transactionSuffix" -ExpectedLauncherPath $transactionLauncherPath -ExpectedLegacyLauncherPath $transactionLegacyLauncherPath -ExpectedLauncherTempPath $transactionLauncherTempPath
    [System.IO.File]::WriteAllText($transactionLauncherPath, 'user replacement before commit', (New-Object System.Text.UTF8Encoding($false)))
    $commitReplacementRejected = $false
    try {
        Complete-CountdownInstallTransaction -TransactionPath $commitReplacementTransaction.transactionPath -ExpectedInstallPath $transactionInstallPath -ExpectedTaskName "Minmin Countdown Commit Replacement $transactionSuffix" -ExpectedLegacyTaskName "敏敏子倒计时 Commit Replacement $transactionSuffix" -ExpectedLauncherPath $transactionLauncherPath -ExpectedLegacyLauncherPath $transactionLegacyLauncherPath -ExpectedLauncherTempPath $transactionLauncherTempPath
    }
    catch {
        $commitReplacementRejected = $_.Exception.Message -match '候选桌面启动器在提交前已被替换'
    }
    Assert-Equal $true $commitReplacementRejected 'Transaction commit rejects a launcher replaced after acceptance'
    Assert-Equal $true (Test-Path -LiteralPath $commitReplacementTransaction.transactionPath) 'Rejected transaction commit retains rollback evidence'
    $commitReplacementRollback = Undo-CountdownInstallTransaction -TransactionPath $commitReplacementTransaction.transactionPath -PowerShell7Path $transactionPowerShell -ExpectedInstallPath $transactionInstallPath -ExpectedTaskName "Minmin Countdown Commit Replacement $transactionSuffix" -ExpectedLegacyTaskName "敏敏子倒计时 Commit Replacement $transactionSuffix" -ExpectedLauncherPath $transactionLauncherPath -ExpectedLegacyLauncherPath $transactionLegacyLauncherPath -ExpectedLauncherTempPath $transactionLauncherTempPath -PreserveUnmanagedLaunchers -RestoreWindowsTimeAction { param($snapshot) }
    Assert-Equal $true ([bool] $commitReplacementRollback.PreservedUnmanagedLaunchers) 'Rejected transaction commit can preserve the user-replaced launcher during rollback'
    Assert-Equal 'user replacement before commit' ([System.IO.File]::ReadAllText($transactionLauncherPath, [System.Text.Encoding]::UTF8)) 'Rejected transaction commit does not overwrite the user-replaced launcher'
    $tamperedTransaction = Start-CountdownInstallTransaction -InstallPath $transactionInstallPath -LauncherPath $transactionLauncherPath -LegacyLauncherPath $transactionLegacyLauncherPath -TaskName "Minmin Countdown Tamper $transactionSuffix" -LegacyTaskName "敏敏子倒计时 Tamper $transactionSuffix"
    $tamperedJournalPath = Join-Path $tamperedTransaction.transactionPath 'journal.json'
    $tamperedJournal = [System.IO.File]::ReadAllText($tamperedJournalPath, [System.Text.Encoding]::UTF8) | ConvertFrom-Json
    $tamperedJournal.backupLauncherPath = 'D:\outside-transaction-backup.exe'
    [System.IO.File]::WriteAllText($tamperedJournalPath, ($tamperedJournal | ConvertTo-Json -Depth 8), (New-Object System.Text.UTF8Encoding($false)))
    $tamperedJournalRejected = $false
    try {
        Undo-CountdownInstallTransaction -TransactionPath $tamperedTransaction.transactionPath -PowerShell7Path (Get-Command pwsh.exe -CommandType Application -ErrorAction Stop | Select-Object -First 1 -ExpandProperty Source) -RestoreWindowsTimeAction { param($snapshot) }
    }
    catch {
        $tamperedJournalRejected = $_.Exception.Message -match '不在事务目录内'
    }
    Assert-Equal $true $tamperedJournalRejected 'Transaction rollback rejects backup paths outside its journal directory'
}
finally {
    foreach ($junctionPath in @($reparseDirectLinkPath, $reparseNestedLinkPath)) {
        if (-not [string]::IsNullOrWhiteSpace([string] $junctionPath) -and (Test-Path -LiteralPath $junctionPath)) {
            Remove-Item -LiteralPath $junctionPath -Force -ErrorAction SilentlyContinue
        }
    }
    Remove-Item -LiteralPath $transactionTestRoot -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $recoveryConflictRoot -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $committedCleanupRoot -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $reparseSafetyRoot -Recurse -Force -ErrorAction SilentlyContinue
}

$installerValidationEngine = (Get-Command pwsh.exe -CommandType Application -ErrorAction Stop | Select-Object -First 1).Source
$installerValidationJson = & $installerValidationEngine -NoLogo -NoProfile -NonInteractive -File (Join-Path $PSScriptRoot 'Install-Countdown.ps1') -ValidateOnly | Out-String
if ($LASTEXITCODE -ne 0) { throw "Installer validation failed: $installerValidationJson" }
$installerValidation = $installerValidationJson | ConvertFrom-Json
Assert-Equal $true $installerValidation.Valid 'Installer package validation succeeds'
Assert-Equal $releaseContract.IntegrityHashes.Count $installerValidation.VerifiedFiles 'Installer reports the canonical integrity count'
Assert-Equal (@($releaseContract.OfficialCharacterAssets) -join ',') (@($installerValidation.OfficialCharacterAssets) -join ',') 'Installer exposes canonical official character assets'
Assert-Equal (@($releaseContract.FloatingPlushAssets) -join ',') (@($installerValidation.FloatingPlushAssets) -join ',') 'Installer exposes canonical floating plush assets'
Assert-Equal (@($releaseContract.CurrentFloatingRuntimeAssets) -join ',') (@($installerValidation.CurrentFloatingRuntimeAssets) -join ',') 'Installer exposes canonical current floating runtime assets'
Assert-Equal (@($releaseContract.LegacyFloatingRegressionAssets) -join ',') (@($installerValidation.LegacyFloatingRegressionAssets) -join ',') 'Installer exposes canonical floating regression fixtures'
Assert-Equal (@($releaseContract.CompatibilityFixtureFiles) -join ',') (@($installerValidation.CompatibilityFixtureFiles) -join ',') 'Installer exposes canonical compatibility fixtures'
Assert-Equal (@($releaseContract.RetiredRuntimeFiles) -join ',') (@($installerValidation.RetiredRuntimeFiles) -join ',') 'Installer exposes canonical retired runtime files'
Assert-Equal $true $installerValidation.Transactional 'Installer exposes transactional install boundary'
Assert-Equal $true $installerValidation.DeferredCommitSupported 'Installer exposes deferred commit for office acceptance'
$installerCommand = Get-Command (Join-Path $PSScriptRoot 'Install-Countdown.ps1')
Assert-Equal $false $installerCommand.Parameters.ContainsKey('TransactionMutexHeld') 'Installer does not expose a lock-bypass switch'
Assert-Equal $true $installerCommand.Parameters.ContainsKey('ParentTransactionMutexHeld') 'Installer exposes a parent-lock test context only for deferred office acceptance'
Assert-Equal $true $installerCommand.Parameters.ContainsKey('ExpectedInteractiveUser') 'Office installer validates the expected interactive user'
Assert-Equal $true $installerCommand.Parameters.ContainsKey('ExpectedInteractiveSessionId') 'Office installer validates the expected interactive session'
Assert-Equal $true $installerCommand.Parameters.ContainsKey('ExpectedDesktopPath') 'Office installer validates the expected desktop path'
$recoveryCommand = Get-Command Recover-CountdownInstallTransactions -Module Countdown.InstallTransaction
Assert-Equal $false $recoveryCommand.Parameters.ContainsKey('TransactionMutexHeld') 'Recovery does not expose a lock-bypass switch'
$candidateCommand = Get-Command Set-CountdownInstallTransactionCandidateLauncherHash -Module Countdown.InstallTransaction
Assert-Equal $true $candidateCommand.Parameters.ContainsKey('CandidateLauncherSha256') 'Transaction persists a candidate launcher hash before replacement'
$failureReceiptCommand = Get-Command Write-CountdownInstallFailureReceipt -Module Countdown.InstallTransaction
Assert-Equal $true ($failureReceiptCommand.Parameters.ContainsKey('ErrorRecord') -and $failureReceiptCommand.Parameters.ContainsKey('RollbackSucceeded')) 'Transaction module exposes a selected-field deployment failure receipt writer'
$failureReceiptParserCommand = Get-Command ConvertFrom-CountdownInstallFailureReceiptBytes -Module Countdown.InstallTransaction
Assert-Equal $true ($failureReceiptCommand.Parameters.ContainsKey('CommitState') -and $failureReceiptParserCommand.Parameters.ContainsKey('Bytes')) 'Transaction module exposes a strict deployment failure receipt contract'
$installerSource = [System.IO.File]::ReadAllText((Join-Path $PSScriptRoot 'Install-Countdown.ps1'), [System.Text.Encoding]::UTF8)
$buildSource = [System.IO.File]::ReadAllText((Join-Path $PSScriptRoot 'Build-CountdownApplication.ps1'), [System.Text.Encoding]::UTF8)
$deploymentSource = [System.IO.File]::ReadAllText((Join-Path $PSScriptRoot 'Deploy-OfficeCountdown.ps1'), [System.Text.Encoding]::UTF8)
Assert-Equal 2 ([regex]::Matches($buildSource, '-p:IncludeSourceRevisionInInformationalVersion=false').Count) 'Both self-contained publish commands exclude the Git revision so committed manifest hashes remain reproducible'
$managedBuildIntermediates = @(
    'src\MinminCountdown.Core\bin',
    'src\MinminCountdown.Core\obj',
    'src\MinminCountdown\bin',
    'src\MinminCountdown\obj',
    'src\MinminCountdown.Verification\bin',
    'src\MinminCountdown.Verification\obj'
)
foreach ($relativePath in $managedBuildIntermediates) {
    Assert-Equal $true $buildSource.Contains("'$relativePath'") "Clean build removes managed intermediate directory $relativePath"
}
Assert-Equal $true $buildSource.Contains('$managedCleanCandidates = @($applicationOutput, $verificationOutput)') 'Clean build derives removal candidates from actual output paths independently of the allowlist'
Assert-Equal $true $buildSource.Contains('$projectDirectories = @($coreProject, $applicationProject, $verificationProject)') 'Clean build derives intermediate candidates from all actual C# project paths'
Assert-Equal $true $buildSource.Contains('$resolvedCleanPath.StartsWith($repositoryRootPrefix, [System.StringComparison]::OrdinalIgnoreCase)') 'Clean build rejects absolute removal targets outside the repository root'
Assert-Equal $true $buildSource.Contains('$resolvedCleanPath -notin $expectedCleanPaths') 'Clean build validates every absolute removal target against its exact allowlist'
Assert-Equal $true $buildSource.Contains("`$runEvidenceCapabilityRoot = 'D:\Codex\.codex\run-evidence'") 'Clean build uses the governed run-evidence capability root for isolated release clones'
Assert-Equal $true $buildSource.Contains('$resolvedCleanPath.StartsWith($runEvidenceRootPrefix, [System.StringComparison]::OrdinalIgnoreCase)') 'Clean build detects output paths inside the governed run-evidence tree'
Assert-Equal $true $buildSource.Contains('-CapabilityRoot $cleanupCapabilityRoot') 'Clean build passes the resolved stable capability root to safe-delete'
$installerCommittedRecoveryMessageIndex = $installerSource.IndexOf('检测到已提交但未完成应用解锁确认的事务')
$installerOwnershipRecoveryMessageIndex = $installerSource.IndexOf('检测到用户替换的桌面启动器：应用、任务和时间服务已恢复')
Assert-Equal $true ($installerCommittedRecoveryMessageIndex -ge 0 -and $installerCommittedRecoveryMessageIndex -lt $installerOwnershipRecoveryMessageIndex) 'Installer prioritizes committed recovery evidence over a simultaneous launcher conflict'
$timeSourceWaitDefinitionIndex = $installerSource.IndexOf('function Wait-CountdownNetworkTimeSource')
$timeSourceWaitCallIndex = $installerSource.IndexOf('$timeSource = Wait-CountdownNetworkTimeSource')
Assert-Equal $true ($timeSourceWaitDefinitionIndex -ge 0 -and $timeSourceWaitCallIndex -gt $timeSourceWaitDefinitionIndex) 'Installer waits with a bounded network-time source probe'
Assert-Equal $true ($installerSource -notmatch 'Start-Sleep -Seconds 3') 'Installer does not rely on a fixed three-second time-service sleep'
$payloadCaptureIndex = $installerSource.IndexOf('$transaction = Capture-CountdownInstallPayloadSnapshot')
$candidatePathIndex = $installerSource.IndexOf("`$candidateLauncherPath = Assert-CountdownInstallPathOnDDrive -Path (Join-Path `$transaction.transactionPath 'candidate-launcher.exe')")
$candidatePersistIndex = $installerSource.IndexOf('$transaction = Set-CountdownInstallTransactionCandidateLauncherHash')
$preparedLauncherGuardIndex = $installerSource.IndexOf('changed while installation was preparing')
$candidateHashCheckIndex = $installerSource.IndexOf('Transaction launcher candidate changed before desktop replacement.')
$launcherMoveIndex = $installerSource.IndexOf('Move-Item -LiteralPath $candidateLauncherPath -Destination $launcherPath -Force')
$installedLauncherHashCheckIndex = $installerSource.IndexOf('Desktop launcher hash mismatch after replacement.')
Assert-Equal $true ($payloadCaptureIndex -ge 0 -and $candidatePathIndex -gt $payloadCaptureIndex -and $candidatePersistIndex -gt $candidatePathIndex -and $preparedLauncherGuardIndex -gt $candidatePersistIndex -and $candidateHashCheckIndex -gt $preparedLauncherGuardIndex -and $launcherMoveIndex -gt $candidateHashCheckIndex -and $installedLauncherHashCheckIndex -gt $launcherMoveIndex) 'Installer keeps candidate under the transaction and verifies both candidate and replaced launcher immediately around the move'
$failureRecordIndex = $installerSource.IndexOf('$failureRecord = $_')
$failureStringIndex = $installerSource.IndexOf('$failure = $failureRecord.Exception.Message')
$launcherReplacementStageIndex = $installerSource.IndexOf("`$installStage = 'replace-desktop-launcher'")
$launcherStateStageIndex = $installerSource.IndexOf("`$installStage = 'write-launcher-state'")
$launcherStateMoveIndex = $installerSource.IndexOf('Move-Item -LiteralPath $launcherStateTempPath -Destination $launcherStatePath -Force')
Assert-Equal $true ($failureRecordIndex -ge 0 -and $failureStringIndex -gt $failureRecordIndex -and $launcherReplacementStageIndex -ge 0 -and $launcherReplacementStageIndex -lt $launcherMoveIndex -and $launcherStateStageIndex -gt $launcherMoveIndex -and $launcherStateStageIndex -lt $launcherStateMoveIndex) 'Installer preserves the original error record and marks both desktop replacement stages before they execute'
Assert-Equal $true ($deploymentSource.Contains('Get-OfficeRemoteDeploymentFailureReceipt') -and $deploymentSource.Contains('remote-deployment-failure.json') -and $deploymentSource.Contains('ConvertFrom-CountdownInstallFailureReceiptBytes') -and $deploymentSource.Contains('ReparsePoint') -and $deploymentSource.Contains('WriteAllText(') -and -not $deploymentSource.Contains('WriteAllBytes($failureReceiptEvidencePath')) 'Office deployment validates a bounded non-reparse remote failure receipt before persisting normalized evidence'
$installTreeGuardIndex = $installerSource.IndexOf('Assert-CountdownInstallTreeHasNoReparsePoints -Path $install', $payloadCaptureIndex)
$payloadCopyIndex = $installerSource.IndexOf('Copy-Item -LiteralPath $source -Destination $destination -Force', $installTreeGuardIndex)
Assert-Equal $true ($installTreeGuardIndex -gt $payloadCaptureIndex -and $payloadCopyIndex -gt $installTreeGuardIndex) 'Installer rechecks the install tree after the stable snapshot and before writing payload files'
Assert-Equal $true $releaseContract.IntegrityHashes.Contains('Build-CountdownApplication.ps1') 'Release contract protects the build script required by remote core tests'
Assert-Equal $true ('Build-CountdownApplication.ps1' -in @($releaseContract.PackageFiles)) 'Release contract packages the build script required by remote core tests'
$clearTestPath = Join-Path $PSScriptRoot 'clear-test.settings.json'
[System.IO.File]::WriteAllText($clearTestPath, '{}')
[System.IO.File]::WriteAllText("$clearTestPath.tmp", '{}')
$removed = Remove-CountdownSettings -SettingsPath $clearTestPath
Assert-Equal $true $removed 'Settings clear reports removal'
Assert-Equal $false (Test-Path -LiteralPath $clearTestPath) 'Settings file removed'
Assert-Equal $false (Test-Path -LiteralPath "$clearTestPath.tmp") 'Temporary settings file removed'

$legacyV3Settings = [pscustomobject]@{
    schemaVersion = 3
    timeZoneId = 'China Standard Time'
    displayPrecision = 'Minute'
    monthlySalary = 12345
    workStart = '08:30'
    workEnd = '17:30'
    countdowns = @([pscustomobject]@{ id = 'legacy'; name = '旧版倒计时'; targetUtc = '2099-01-01T00:00:00Z' })
}
$loadedLegacyV3 = ConvertFrom-CountdownSettingsObject -Settings $legacyV3Settings
Assert-Equal $true $loadedLegacyV3.Migrated 'Schema v3 settings migrate'
Assert-Equal 1 $loadedLegacyV3.Records.Count 'Schema v3 record count'
Assert-Equal 0 $loadedLegacyV3.Notes.Count 'Schema v3 creates empty notes'
Assert-Equal 0 $loadedLegacyV3.Tags.Count 'Schema v3 creates an empty tag library'
Assert-Equal 0 @($loadedLegacyV3.Records[0].tagIds).Count 'Schema v3 creates untagged countdowns'
Assert-Equal 'Minute' $loadedLegacyV3.Preferences.Precision 'Schema v3 preserves precision'
$canonicalLegacyV3 = ConvertTo-CountdownSettingsJson -State $loadedLegacyV3
$canonicalLegacyV3Object = $canonicalLegacyV3 | ConvertFrom-Json
Assert-Equal 9 $canonicalLegacyV3Object.schemaVersion 'Canonical settings schema'
Assert-Equal 'Auto' $loadedLegacyV3.Preferences.ThemePreference 'Schema v3 defaults the theme preference to Auto'
Assert-Equal 'Auto' $canonicalLegacyV3Object.themePreference 'Canonical settings persist the Auto theme preference'
Assert-Equal 0 @($canonicalLegacyV3Object.tags).Count 'Canonical settings tags'
Assert-Equal 0 @($canonicalLegacyV3Object.notes).Count 'Canonical settings notes'
Assert-Equal 0 @($canonicalLegacyV3Object.countdowns[0].tagIds).Count 'Canonical settings countdown tag IDs'
$settingsCandidate = Copy-CountdownSettingsState -State $loadedLegacyV3
$settingsCandidate.Records[0].name = '候选倒计时'
$settingsCandidate.Preferences.MonthlySalary = 1
Assert-Equal '旧版倒计时' $loadedLegacyV3.Records[0].name 'Settings candidate does not mutate source record'
Assert-Equal 12345 $loadedLegacyV3.Preferences.MonthlySalary 'Settings candidate does not mutate source preferences'
$candidateSaveFailureObserved = $false
try {
    $unwritableCandidatePath = Join-Path (Join-Path $PSScriptRoot ([guid]::NewGuid().ToString('N'))) 'settings.json'
    Save-CountdownSettings -Path $unwritableCandidatePath -State $settingsCandidate
}
catch {
    $candidateSaveFailureObserved = $true
}
Assert-Equal $true $candidateSaveFailureObserved 'Settings candidate persistence failure is observable'
Assert-Equal '旧版倒计时' $loadedLegacyV3.Records[0].name 'Settings candidate persistence failure preserves source record'
Assert-Equal $true (Test-CountdownSettingsDeploymentTransition -BeforeHash 'same' -AfterHash 'same' -BeforeJson $null -AfterJson $null) 'Unchanged settings are accepted for deployment'
Assert-Equal $true (Test-CountdownSettingsDeploymentTransition -BeforeHash 'before' -AfterHash 'after' -BeforeJson ($legacyV3Settings | ConvertTo-Json -Depth 4) -AfterJson $canonicalLegacyV3) 'Strict schema v3 to v9 migration is accepted for deployment'
$legacyV1Settings = [pscustomobject]@{ schemaVersion = 1; timeZoneId = 'China Standard Time'; targetUtc = '2099-01-01T00:00:00Z' }
$canonicalLegacyV1 = ConvertTo-CountdownSettingsJson -State (ConvertFrom-CountdownSettingsObject -Settings $legacyV1Settings)
Assert-Equal $false (Test-CountdownSettingsDeploymentTransition -BeforeHash 'before' -AfterHash 'after' -BeforeJson ($legacyV1Settings | ConvertTo-Json -Depth 4) -AfterJson $canonicalLegacyV1) 'Schema v1 migration remains rejected for deployment'
$settingsModuleTestPath = Join-Path $PSScriptRoot 'settings-module-test.json'
try {
    Save-CountdownSettings -Path $settingsModuleTestPath -State $loadedLegacyV3
    $savedSettings = [System.IO.File]::ReadAllText($settingsModuleTestPath, [System.Text.Encoding]::UTF8) | ConvertFrom-Json
    Assert-Equal 9 $savedSettings.schemaVersion 'Settings module writes canonical schema'
    Assert-Equal $false (Test-Path -LiteralPath "$settingsModuleTestPath.tmp") 'Settings module cleans temporary file'
}
finally {
    Remove-Item -LiteralPath $settingsModuleTestPath -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath "$settingsModuleTestPath.tmp" -Force -ErrorAction SilentlyContinue
}

$legacyV4Settings = [pscustomobject]@{
    schemaVersion = 4
    timeZoneId = 'China Standard Time'
    displayPrecision = 'Second'
    monthlySalary = 23000
    workStart = '09:00'
    workEnd = '18:00'
    countdowns = @([pscustomobject]@{ id = 'legacy-v4-countdown'; name = '旧版带便签倒计时'; targetUtc = '2099-02-01T00:00:00Z' })
    notes = @([pscustomobject]@{ id = 'legacy-v4-note'; text = '保留的旧版便签'; isCompleted = $true })
}
$loadedLegacyV4 = ConvertFrom-CountdownSettingsObject -Settings $legacyV4Settings
Assert-Equal $true $loadedLegacyV4.Migrated 'Schema v4 settings migrate'
Assert-Equal 0 $loadedLegacyV4.Tags.Count 'Schema v4 creates an empty tag library'
Assert-Equal 0 @($loadedLegacyV4.Records[0].tagIds).Count 'Schema v4 creates untagged countdowns'
Assert-Equal 0 @($loadedLegacyV4.Notes[0].tagIds).Count 'Schema v4 creates untagged notes'
Assert-Equal '保留的旧版便签' $loadedLegacyV4.Notes[0].text 'Schema v4 preserves notes'
Assert-Equal $null $loadedLegacyV4.Notes[0].date 'Schema v4 does not invent a note date'
$canonicalLegacyV4 = ConvertTo-CountdownSettingsJson -State $loadedLegacyV4
Assert-Equal $true (Test-CountdownSettingsDeploymentTransition -BeforeHash 'before' -AfterHash 'after' -BeforeJson ($legacyV4Settings | ConvertTo-Json -Depth 4) -AfterJson $canonicalLegacyV4) 'Strict schema v4 to v9 migration is accepted for deployment'
$invalidV4Transition = $canonicalLegacyV4 | ConvertFrom-Json
$invalidV4Transition.notes[0].text = '被篡改的便签'
Assert-Equal $false (Test-CountdownSettingsDeploymentTransition -BeforeHash 'before' -AfterHash 'after' -BeforeJson ($legacyV4Settings | ConvertTo-Json -Depth 4) -AfterJson ($invalidV4Transition | ConvertTo-Json -Depth 5)) 'Schema v4 migration rejects changed notes'

$v5Settings = [pscustomobject]@{
    schemaVersion = 5
    timeZoneId = 'China Standard Time'
    displayPrecision = 'Hour'
    monthlySalary = 8000
    workStart = '08:00'
    workEnd = '17:00'
    tags = @(
        [pscustomobject]@{ id = 'work'; name = '工作'; color = '#6B8E23' },
        [pscustomobject]@{ id = 'home'; name = '家庭'; color = $null }
    )
    countdowns = @([pscustomobject]@{ id = 'v5-countdown'; name = '带标签倒计时'; targetUtc = '2099-03-01T00:00:00Z'; tagIds = @(' WORK ') })
    notes = @([pscustomobject]@{ id = 'v5-note'; text = '带标签便签'; isCompleted = $false; tagIds = @('home') })
}
$loadedV5 = ConvertFrom-CountdownSettingsObject -Settings $v5Settings
Assert-Equal $true $loadedV5.Migrated 'Schema v5 settings migrate to add note dates'
Assert-Equal 2 $loadedV5.Tags.Count 'Schema v5 loads tags'
Assert-Equal 'work' $loadedV5.Records[0].tagIds[0] 'Schema v5 canonicalizes countdown tag IDs'
Assert-Equal 'home' $loadedV5.Notes[0].tagIds[0] 'Schema v5 preserves note tag IDs'
Assert-Equal $null $loadedV5.Notes[0].date 'Schema v5 migration does not invent a note date'
$v5Copy = Copy-CountdownSettingsState -State $loadedV5
$v5Copy.Tags[0].name = '已改名'
$v5Copy.Records[0].tagIds[0] = 'home'
$v5Copy.Notes[0].tagIds[0] = 'work'
Assert-Equal '工作' $loadedV5.Tags[0].name 'Settings state copy does not mutate source tags'
Assert-Equal 'work' $loadedV5.Records[0].tagIds[0] 'Settings state copy does not mutate source countdown tags'
Assert-Equal 'home' $loadedV5.Notes[0].tagIds[0] 'Settings state copy does not mutate source note tags'
$canonicalFromV5 = ConvertTo-CountdownSettingsJson -State $loadedV5 | ConvertFrom-Json
Assert-Equal 9 $canonicalFromV5.schemaVersion 'Schema v5 migrates to canonical schema v9'
Assert-Equal 'Auto' $canonicalFromV5.themePreference 'Schema v5 migration defaults the theme preference to Auto'
Assert-Equal '#6B8E23' $canonicalFromV5.tags[0].color 'Schema v9 serializes tag color'
Assert-Equal $true ($null -eq $canonicalFromV5.tags[1].PSObject.Properties['color']) 'Schema v9 omits an unspecified tag color'
Assert-Equal 'work' $canonicalFromV5.countdowns[0].tagIds[0] 'Schema v9 serializes countdown tag IDs'
Assert-Equal 'home' $canonicalFromV5.notes[0].tagIds[0] 'Schema v9 serializes note tag IDs'
Assert-Equal $null $canonicalFromV5.notes[0].date 'Schema v9 preserves an unknown legacy note date as null'
Assert-Equal 'none' $canonicalFromV5.notes[0].reminder.type 'Schema v9 disables reminders for migrated notes'
$loadedNullDateV7 = ConvertFrom-CountdownSettingsObject -Settings $canonicalFromV5
Assert-Equal $null $loadedNullDateV7.Notes[0].date 'Schema v7 accepts an explicitly unknown note date'
Assert-Equal $true (Test-CountdownSettingsDeploymentTransition -BeforeHash 'before' -AfterHash 'after' -BeforeJson ($v5Settings | ConvertTo-Json -Depth 5) -AfterJson ($canonicalFromV5 | ConvertTo-Json -Depth 5)) 'Strict schema v5 to v9 migration is accepted for deployment'
$invalidV5Transition = $canonicalFromV5 | ConvertTo-Json -Depth 5 | ConvertFrom-Json
$invalidV5Transition.notes[0].date = '2026-07-18'
Assert-Equal $false (Test-CountdownSettingsDeploymentTransition -BeforeHash 'before' -AfterHash 'after' -BeforeJson ($v5Settings | ConvertTo-Json -Depth 5) -AfterJson ($invalidV5Transition | ConvertTo-Json -Depth 5)) 'Schema v5 migration rejects an invented note date'

$v6Settings = $canonicalFromV5 | ConvertTo-Json -Depth 5 | ConvertFrom-Json
$v6Settings.schemaVersion = 6
$v6Settings.PSObject.Properties.Remove('themePreference')
$v6Settings.notes[0].PSObject.Properties.Remove('reminder')
$v6Settings.notes[0].date = '2026-07-18'
$loadedV6 = ConvertFrom-CountdownSettingsObject -Settings $v6Settings
Assert-Equal $true $loadedV6.Migrated 'Schema v6 settings migrate to add reminder fields'
Assert-Equal '2026-07-18' $loadedV6.Notes[0].date 'Schema v6 loads an exact note date'
Assert-Equal $null $loadedV6.Notes[0].reminderIntervalMinutes 'Schema v6 migration disables reminders'
$canonicalV6 = ConvertTo-CountdownSettingsJson -State $loadedV6 | ConvertFrom-Json
Assert-Equal 9 $canonicalV6.schemaVersion 'Schema v6 migrates to canonical schema v9'
Assert-Equal '2026-07-18' $canonicalV6.notes[0].date 'Schema v9 preserves an exact note date'
Assert-Equal 'none' $canonicalV6.notes[0].reminder.type 'Schema v9 persists a disabled reminder explicitly'
Assert-Equal $true (Test-CountdownSettingsDeploymentTransition -BeforeHash 'before' -AfterHash 'after' -BeforeJson ($v6Settings | ConvertTo-Json -Depth 5) -AfterJson ($canonicalV6 | ConvertTo-Json -Depth 5)) 'Strict schema v6 to v9 migration is accepted for deployment'

$v7Settings = $canonicalV6 | ConvertTo-Json -Depth 5 | ConvertFrom-Json
$v7Settings.schemaVersion = 7
$v7Settings.PSObject.Properties.Remove('themePreference')
$v7Settings.notes[0].PSObject.Properties.Remove('reminder')
$v7Settings.notes[0] | Add-Member -NotePropertyName reminderIntervalMinutes -NotePropertyValue 30 -Force
$v7Settings.notes[0] | Add-Member -NotePropertyName reminderStartUtc -NotePropertyValue '2026-07-19T00:00:00.0000000+00:00' -Force
$loadedV7 = ConvertFrom-CountdownSettingsObject -Settings $v7Settings
Assert-Equal $true $loadedV7.Migrated 'Schema v7 settings migrate to schema v9'
Assert-Equal 30 $loadedV7.Notes[0].reminderIntervalMinutes 'Schema v7 loads a reminder interval'
Assert-Equal ([DateTimeOffset]'2026-07-19T00:00:00Z') $loadedV7.Notes[0].reminderStartUtc 'Schema v7 loads a UTC reminder start'
$canonicalV7 = ConvertTo-CountdownSettingsJson -State $loadedV7 | ConvertFrom-Json
Assert-Equal 9 $canonicalV7.schemaVersion 'Schema v7 writes canonical schema v9'
Assert-Equal 'repeat' $canonicalV7.notes[0].reminder.type 'Schema v7 migrates to a repeat reminder'
Assert-Equal 30 $canonicalV7.notes[0].reminder.intervalMinutes 'Schema v7 preserves a reminder interval'
Assert-Equal ([DateTimeOffset]'2026-07-19T00:30:00Z') ([DateTimeOffset] $canonicalV7.notes[0].reminder.firstDueUtc).ToUniversalTime() 'Schema v7 preserves the first due time'
Assert-Equal $true (Test-CountdownSettingsDeploymentTransition -BeforeHash 'before' -AfterHash 'after' -BeforeJson ($v7Settings | ConvertTo-Json -Depth 5) -AfterJson ($canonicalV7 | ConvertTo-Json -Depth 5)) 'Strict schema v7 to v9 migration is accepted for deployment'
$preFreezeV7 = $canonicalV7 | ConvertTo-Json -Depth 5 | ConvertFrom-Json
$preFreezeV7.monthlySalary = 100
$postFreezeV7 = $canonicalV7 | ConvertTo-Json -Depth 5 | ConvertFrom-Json
$postFreezeV7.monthlySalary = 200
Assert-Equal $false (Test-CountdownSettingsDeploymentTransition -BeforeHash 'pre-freeze' -AfterHash 'post-freeze' -BeforeJson ($preFreezeV7 | ConvertTo-Json -Depth 5) -AfterJson ($postFreezeV7 | ConvertTo-Json -Depth 5)) 'Schema v7 changes after the selected deployment baseline are rejected'
Assert-Equal $true (Test-CountdownSettingsDeploymentTransition -BeforeHash 'post-freeze' -AfterHash 'post-freeze' -BeforeJson ($postFreezeV7 | ConvertTo-Json -Depth 5) -AfterJson ($postFreezeV7 | ConvertTo-Json -Depth 5)) 'A pre-freeze save preserved in the transaction snapshot remains deployable'
$reloadedCanonicalV7 = ConvertFrom-CountdownSettingsObject -Settings $canonicalV7
Assert-Equal 2 $reloadedCanonicalV7.Tags.Count 'Schema v9 reloads tags without optional colors'
Assert-Equal $null $reloadedCanonicalV7.Tags[1].color 'Schema v9 reload preserves an unspecified tag color'

$v8OnceSettings = $canonicalV7 | ConvertTo-Json -Depth 6 | ConvertFrom-Json
$v8OnceSettings.schemaVersion = 8
$v8OnceSettings.PSObject.Properties.Remove('themePreference')
$v8OnceSettings.notes[0].reminder = [pscustomobject]@{
    type = 'once'
    firstDueUtc = '2026-07-20T01:15:00.0000000+00:00'
    delivery = 'pending'
}
$loadedV8Once = ConvertFrom-CountdownSettingsObject -Settings $v8OnceSettings
Assert-Equal $true $loadedV8Once.Migrated 'Schema v8 settings migrate to schema v9'
Assert-Equal 'Auto' $loadedV8Once.Preferences.ThemePreference 'Schema v8 defaults the theme preference to Auto'
Assert-Equal 'once' $loadedV8Once.Notes[0].reminder.type 'Schema v8 loads an once reminder'
Assert-Equal 'pending' $loadedV8Once.Notes[0].reminder.delivery 'Schema v8 preserves pending delivery state'
$canonicalV8Once = ConvertTo-CountdownSettingsJson -State $loadedV8Once | ConvertFrom-Json
Assert-Equal 'once' $canonicalV8Once.notes[0].reminder.type 'Schema v8 round-trips an once reminder'
Assert-Equal ([DateTimeOffset]'2026-07-20T01:15:00Z') ([DateTimeOffset] $canonicalV8Once.notes[0].reminder.firstDueUtc).ToUniversalTime() 'Schema v8 round-trips the once due time'
Assert-Equal 'pending' $canonicalV8Once.notes[0].reminder.delivery 'Schema v8 round-trips pending delivery state'
Assert-Equal 9 $canonicalV8Once.schemaVersion 'Schema v8 writes canonical schema v9'
Assert-Equal 'Auto' $canonicalV8Once.themePreference 'Schema v8 writes the default Auto theme preference'
Assert-Equal $true (Test-CountdownSettingsDeploymentTransition -BeforeHash 'before' -AfterHash 'after' -BeforeJson ($v8OnceSettings | ConvertTo-Json -Depth 6) -AfterJson ($canonicalV8Once | ConvertTo-Json -Depth 6)) 'Strict schema v8 to v9 migration is accepted for deployment'
$invalidV8ThemeTransition = $canonicalV8Once | ConvertTo-Json -Depth 6 | ConvertFrom-Json
$invalidV8ThemeTransition.themePreference = 'Night'
Assert-Equal $false (Test-CountdownSettingsDeploymentTransition -BeforeHash 'before' -AfterHash 'after' -BeforeJson ($v8OnceSettings | ConvertTo-Json -Depth 6) -AfterJson ($invalidV8ThemeTransition | ConvertTo-Json -Depth 6)) 'Schema v8 migration rejects an invented manual theme preference'
$invalidV8OnceBranchRejected = $false
try {
    $invalidV8OnceBranch = $v8OnceSettings | ConvertTo-Json -Depth 6 | ConvertFrom-Json
    $invalidV8OnceBranch.notes[0].reminder | Add-Member -NotePropertyName intervalMinutes -NotePropertyValue 30
    ConvertFrom-CountdownSettingsObject -Settings $invalidV8OnceBranch | Out-Null
}
catch {
    $invalidV8OnceBranchRejected = $true
}
Assert-Equal $true $invalidV8OnceBranchRejected 'Schema v8 rejects repeat fields on an once reminder'

$v9NightSettings = $canonicalV8Once | ConvertTo-Json -Depth 6 | ConvertFrom-Json
$v9NightSettings.themePreference = 'Night'
$loadedV9Night = ConvertFrom-CountdownSettingsObject -Settings $v9NightSettings
Assert-Equal $false $loadedV9Night.Migrated 'Schema v9 settings do not migrate'
Assert-Equal 'Night' $loadedV9Night.Preferences.ThemePreference 'Schema v9 loads a manual night preference'
$canonicalV9Night = ConvertTo-CountdownSettingsJson -State $loadedV9Night | ConvertFrom-Json
Assert-Equal 'Night' $canonicalV9Night.themePreference 'Schema v9 round-trips a manual night preference'
$missingV9ThemeRejected = $false
try {
    $missingV9Theme = $v9NightSettings | ConvertTo-Json -Depth 6 | ConvertFrom-Json
    $missingV9Theme.PSObject.Properties.Remove('themePreference')
    ConvertFrom-CountdownSettingsObject -Settings $missingV9Theme | Out-Null
}
catch {
    $missingV9ThemeRejected = $true
}
Assert-Equal $true $missingV9ThemeRejected 'Schema v9 rejects a missing theme preference'

$missingV6NoteDateRejected = $false
try {
    $missingV6NoteDate = $v6Settings | ConvertTo-Json -Depth 5 | ConvertFrom-Json
    $missingV6NoteDate.notes[0].PSObject.Properties.Remove('date')
    ConvertFrom-CountdownSettingsObject -Settings $missingV6NoteDate | Out-Null
}
catch {
    $missingV6NoteDateRejected = $true
}
Assert-Equal $true $missingV6NoteDateRejected 'Schema v6 requires a note date field'
$invalidV6NoteDateRejected = $false
try {
    $invalidV6NoteDate = $v6Settings | ConvertTo-Json -Depth 5 | ConvertFrom-Json
    $invalidV6NoteDate.notes[0].date = '2026-7-18'
    ConvertFrom-CountdownSettingsObject -Settings $invalidV6NoteDate | Out-Null
}
catch {
    $invalidV6NoteDateRejected = $true
}
Assert-Equal $true $invalidV6NoteDateRejected 'Schema v6 rejects a non-canonical note date'
$blankV6NoteDateRejected = $false
try {
    $blankV6NoteDate = $v6Settings | ConvertTo-Json -Depth 5 | ConvertFrom-Json
    $blankV6NoteDate.notes[0].date = '   '
    ConvertFrom-CountdownSettingsObject -Settings $blankV6NoteDate | Out-Null
}
catch {
    $blankV6NoteDateRejected = $true
}
Assert-Equal $true $blankV6NoteDateRejected 'Schema v6 rejects a blank note date'

$missingV7ReminderFieldsRejected = $false
try {
    $missingV7ReminderFields = $v7Settings | ConvertTo-Json -Depth 5 | ConvertFrom-Json
    $missingV7ReminderFields.notes[0].PSObject.Properties.Remove('reminderIntervalMinutes')
    ConvertFrom-CountdownSettingsObject -Settings $missingV7ReminderFields | Out-Null
}
catch {
    $missingV7ReminderFieldsRejected = $true
}
Assert-Equal $true $missingV7ReminderFieldsRejected 'Schema v7 requires reminder fields'
$invalidV7ReminderTypeRejected = $false
try {
    $invalidV7ReminderType = $v7Settings | ConvertTo-Json -Depth 5 | ConvertFrom-Json
    $invalidV7ReminderType.notes[0].reminderIntervalMinutes = '30'
    ConvertFrom-CountdownSettingsObject -Settings $invalidV7ReminderType | Out-Null
}
catch {
    $invalidV7ReminderTypeRejected = $true
}
Assert-Equal $true $invalidV7ReminderTypeRejected 'Schema v7 rejects a string reminder interval'

$missingV5TagsRejected = $false
try {
    $missingV5Tags = $v5Settings | Select-Object * -ExcludeProperty tags
    ConvertFrom-CountdownSettingsObject -Settings $missingV5Tags | Out-Null
}
catch {
    $missingV5TagsRejected = $true
}
Assert-Equal $true $missingV5TagsRejected 'Schema v5 requires a tag library'
$duplicateTagIdRejected = $false
try {
    $duplicateTagIdSettings = $v5Settings | ConvertTo-Json -Depth 5 | ConvertFrom-Json
    $duplicateTagIdSettings.tags += [pscustomobject]@{ id = 'WORK'; name = '第二个工作'; color = $null }
    ConvertFrom-CountdownSettingsObject -Settings $duplicateTagIdSettings | Out-Null
}
catch {
    $duplicateTagIdRejected = $true
}
Assert-Equal $true $duplicateTagIdRejected 'Schema v5 rejects duplicate tag IDs without case sensitivity'
$duplicateTagNameRejected = $false
try {
    $duplicateTagNameSettings = $v5Settings | ConvertTo-Json -Depth 5 | ConvertFrom-Json
    $duplicateTagNameSettings.tags += [pscustomobject]@{ id = 'office'; name = '工作'; color = $null }
    ConvertFrom-CountdownSettingsObject -Settings $duplicateTagNameSettings | Out-Null
}
catch {
    $duplicateTagNameRejected = $true
}
Assert-Equal $true $duplicateTagNameRejected 'Schema v5 rejects duplicate tag names without case sensitivity'
$danglingCountdownTagRejected = $false
try {
    $danglingCountdownTagSettings = $v5Settings | ConvertTo-Json -Depth 5 | ConvertFrom-Json
    $danglingCountdownTagSettings.countdowns[0].tagIds = @('missing')
    ConvertFrom-CountdownSettingsObject -Settings $danglingCountdownTagSettings | Out-Null
}
catch {
    $danglingCountdownTagRejected = $true
}
Assert-Equal $true $danglingCountdownTagRejected 'Schema v5 rejects dangling countdown tag IDs'
$duplicateNoteTagRejected = $false
try {
    $duplicateNoteTagSettings = $v5Settings | ConvertTo-Json -Depth 5 | ConvertFrom-Json
    $duplicateNoteTagSettings.notes[0].tagIds = @('home', 'HOME')
    ConvertFrom-CountdownSettingsObject -Settings $duplicateNoteTagSettings | Out-Null
}
catch {
    $duplicateNoteTagRejected = $true
}
Assert-Equal $true $duplicateNoteTagRejected 'Schema v5 rejects duplicate note tag IDs without case sensitivity'
$invalidTagColorSettingsRejected = $false
try {
    $invalidTagColorSettings = $v5Settings | ConvertTo-Json -Depth 5 | ConvertFrom-Json
    $invalidTagColorSettings.tags[0].color = 'blue'
    ConvertFrom-CountdownSettingsObject -Settings $invalidTagColorSettings | Out-Null
}
catch {
    $invalidTagColorSettingsRejected = $true
}
Assert-Equal $true $invalidTagColorSettingsRejected 'Schema v5 validates tag colors through tag records'

$datedNote = New-CountdownNoteRecord -Id 'dated-note' -Text '带日期便签' -Date '2026-07-18' -TagIds @('work')
Assert-Equal '2026-07-18' $datedNote.date 'Note records preserve an exact calendar date'
$reminderStart = [DateTimeOffset]'2026-07-19T08:00:00+08:00'
$reminderNote = New-CountdownNoteRecord -Id 'reminder-note' -Text '每半小时站起来活动' -Date '2026-07-19' -TagIds @() -ReminderIntervalMinutes 30 -ReminderStartUtc $reminderStart
Assert-Equal 30 $reminderNote.reminderIntervalMinutes 'Note records preserve a reminder interval'
Assert-Equal ([DateTimeOffset]'2026-07-19T00:00:00Z') $reminderNote.reminderStartUtc 'Note records normalize reminder start to UTC'
$dueBeforeInterval = @(Get-DueCountdownNoteReminders -Notes @($reminderNote) -BeijingTime ([DateTimeOffset]'2026-07-19T08:29:59+08:00') -LastShownCycles @{})
Assert-Equal 0 $dueBeforeInterval.Count 'Note reminder does not fire before its interval'
$dueAtInterval = @(Get-DueCountdownNoteReminders -Notes @($reminderNote) -BeijingTime ([DateTimeOffset]'2026-07-19T08:30:00+08:00') -LastShownCycles @{})
Assert-Equal 1 $dueAtInterval.Count 'Note reminder fires when its interval elapses'
Assert-Equal 1 $dueAtInterval[0].Cycle 'First note reminder reports cycle one'
$dueAlreadyShown = @(Get-DueCountdownNoteReminders -Notes @($reminderNote) -BeijingTime ([DateTimeOffset]'2026-07-19T08:30:00+08:00') -LastShownCycles @{ 'reminder-note' = 1 })
Assert-Equal 0 $dueAlreadyShown.Count 'Note reminder does not repeat within the same cycle'
$dueNextCycle = @(Get-DueCountdownNoteReminders -Notes @($reminderNote) -BeijingTime ([DateTimeOffset]'2026-07-19T09:00:00+08:00') -LastShownCycles @{ 'reminder-note' = 1 })
Assert-Equal 1 $dueNextCycle.Count 'Note reminder fires again at the next interval'
$completedReminderNote = New-CountdownNoteRecord -Id 'completed-reminder-note' -Text '已经完成' -Date '2026-07-19' -IsCompleted $true -TagIds @() -ReminderIntervalMinutes 30 -ReminderStartUtc $reminderStart
Assert-Equal 0 @(Get-DueCountdownNoteReminders -Notes @($completedReminderNote) -BeijingTime ([DateTimeOffset]'2026-07-19T09:00:00+08:00') -LastShownCycles @{}).Count 'Completed notes do not produce reminders'
$invalidReminderIntervalRejected = $false
try {
    New-CountdownNoteRecord -Id 'invalid-reminder-note' -Text '无效提醒' -Date '2026-07-19' -TagIds @() -ReminderIntervalMinutes 0 -ReminderStartUtc $reminderStart | Out-Null
}
catch {
    $invalidReminderIntervalRejected = $true
}
Assert-Equal $true $invalidReminderIntervalRejected 'Note records reject reminder intervals below one minute'
$leapDayNote = New-CountdownNoteRecord -Id 'leap-day-note' -Text '闰日便签' -Date '2028-02-29' -TagIds @()
Assert-Equal '2028-02-29' $leapDayNote.date 'Note records accept a valid leap day'
$invalidNoteDateRejected = $false
try {
    New-CountdownNoteRecord -Id 'invalid-date-note' -Text '无效日期便签' -Date '2026-7-18' -TagIds @() | Out-Null
}
catch {
    $invalidNoteDateRejected = $true
}
Assert-Equal $true $invalidNoteDateRejected 'Note records reject non-canonical calendar dates'
$invalidCalendarDateRejected = $false
try {
    New-CountdownNoteRecord -Id 'invalid-calendar-note' -Text '不存在日期便签' -Date '2026-02-29' -TagIds @() | Out-Null
}
catch {
    $invalidCalendarDateRejected = $true
}
Assert-Equal $true $invalidCalendarDateRejected 'Note records reject an impossible calendar date'

$originalNotes = [System.Collections.ArrayList]::new()
[void] $originalNotes.Add([pscustomobject]@{ id = 'existing'; text = '已有便签'; date = '2026-07-17'; isCompleted = $false; tagIds = @('work') })
$missingAddDateRejected = $false
try {
    Invoke-NoteMutation -Notes $originalNotes -Action Add -Text '缺少日期' -TagIds @() -PersistCandidate { param($candidate) } | Out-Null
}
catch {
    $missingAddDateRejected = $true
}
Assert-Equal $true $missingAddDateRejected 'Note add requires a calendar date'
$persistedCandidates = [System.Collections.ArrayList]::new()
$addResult = Invoke-NoteMutation -Notes $originalNotes -Action Add -Text '新增便签' -Date '2026-07-18' -TagIds @('home') -PersistCandidate {
    param($candidate)
    [void] $persistedCandidates.Add($candidate)
}
Assert-Equal 1 $originalNotes.Count 'Note add does not mutate source before commit'
Assert-Equal 2 $addResult.Notes.Count 'Note add returns candidate notes'
Assert-Equal $true $addResult.Changed 'Note add reports change'
Assert-Equal 1 $persistedCandidates.Count 'Note add persists one candidate'
Assert-Equal 'home' $addResult.Notes[1].tagIds[0] 'Note add preserves selected tag IDs'
Assert-Equal '2026-07-18' $addResult.Notes[1].date 'Note add preserves the selected calendar date'
$updateResult = Invoke-NoteMutation -Notes $addResult.Notes -Action Update -Id 'existing' -Text '已有便签' -Date '2026-07-19' -TagIds @('work') -PersistCandidate {
    param($candidate)
    [void] $persistedCandidates.Add($candidate)
}
Assert-Equal '2026-07-17' $addResult.Notes[0].date 'Note update does not mutate the source date before commit'
Assert-Equal '2026-07-19' $updateResult.Notes[0].date 'Note update preserves the selected calendar date'
$completionResult = Invoke-NoteMutation -Notes $updateResult.Notes -Action SetCompletion -Id 'existing' -IsCompleted $true -PersistCandidate {
    param($candidate)
    [void] $persistedCandidates.Add($candidate)
}
Assert-Equal $false ([bool] $updateResult.Notes[0].isCompleted) 'Note completion does not mutate source before commit'
Assert-Equal $true ([bool] $completionResult.Notes[0].isCompleted) 'Note completion returns changed candidate'
Assert-Equal 'work' $completionResult.Notes[0].tagIds[0] 'Note completion retains existing tag IDs'
Assert-Equal '2026-07-19' $completionResult.Notes[0].date 'Note completion retains the existing calendar date'
$noOpPersistCalls = [pscustomobject]@{ Count = 0 }
$noOpResult = Invoke-NoteMutation -Notes $completionResult.Notes -Action SetCompletion -Id 'existing' -IsCompleted $true -PersistCandidate {
    param($candidate)
    $noOpPersistCalls.Count++
}
Assert-Equal $false $noOpResult.Changed 'Repeated note completion is a no-op'
Assert-Equal 0 $noOpPersistCalls.Count 'Repeated note completion does not persist'
$failedPersistCalls = [pscustomobject]@{ Count = 0 }
$persistFailureObserved = $false
try {
    Invoke-NoteMutation -Notes $completionResult.Notes -Action Remove -Id 'existing' -PersistCandidate {
        param($candidate)
        $failedPersistCalls.Count++
        throw 'Simulated settings write failure.'
    } | Out-Null
}
catch {
    $persistFailureObserved = $true
}
Assert-Equal $true $persistFailureObserved 'Note persistence failure is observable'
Assert-Equal 2 $completionResult.Notes.Count 'Note persistence failure preserves source notes'
Assert-Equal $true ([bool] $completionResult.Notes[0].isCompleted) 'Note persistence failure preserves source record state'
Assert-Equal '2026-07-19' $completionResult.Notes[0].date 'Note persistence failure preserves the source calendar date'
Assert-Equal 1 $failedPersistCalls.Count 'Note persistence failure invokes persistence once'

if ($failures.Count -gt 0) {
    $failures | ForEach-Object { Write-Error $_ }
    exit 1
}

[pscustomobject]@{ Passed = $script:assertions; Failed = 0; TimeZone = (Get-BeijingNow).Offset.ToString() } | ConvertTo-Json
