# Countdown.Core.Tests.ps1 的 SourceContracts 分段：入口按开关点源执行，共用入口的参数、断言函数与已建立的状态，不单独运行。

$installerSource = [System.IO.File]::ReadAllText((Join-Path $PSScriptRoot 'Install-Countdown.ps1'), [System.Text.Encoding]::UTF8)
$buildSource = [System.IO.File]::ReadAllText((Join-Path $PSScriptRoot 'Build-CountdownApplication.ps1'), [System.Text.Encoding]::UTF8)
$buildPreflightSource = [System.IO.File]::ReadAllText((Join-Path $PSScriptRoot 'tools\Countdown.BuildPreflight.psm1'), [System.Text.Encoding]::UTF8)
$buildPreflightScript = [System.IO.File]::ReadAllText((Join-Path $PSScriptRoot 'tools\Test-CountdownBuildPreflight.ps1'), [System.Text.Encoding]::UTF8)
$deploymentSource = [System.IO.File]::ReadAllText((Join-Path $PSScriptRoot 'Deploy-OfficeCountdown.ps1'), [System.Text.Encoding]::UTF8)
Assert-Equal $true ($buildSource.Contains("tools\Countdown.BuildPreflight.psm1") -and $buildSource.Contains('Resolve-CountdownBuildDotnet') -and $buildSource.Contains('Get-CountdownBuildPaths')) 'Build reuses the shared SDK and clean-path preflight module'
$managedCleanIndex = $buildSource.IndexOf('invoke-safe-tree-delete.cmd')
$releaseContractImportIndex = $buildSource.IndexOf("Import-Module (Join-Path `$PSScriptRoot 'Countdown.ReleaseContract.psm1')")
Assert-Equal $true ($managedCleanIndex -ge 0 -and $releaseContractImportIndex -gt $managedCleanIndex) 'Build -Clean removes app output before the release contract import reads a stale published payload'
Assert-Equal $true ($buildPreflightSource.Contains("`$env:COUNTDOWN_DOTNET_EXE") -and $buildPreflightSource.Contains("D:\Codex\tools\dotnet\10.0\dotnet.exe") -and $buildPreflightSource.Contains('Get-Command dotnet')) 'Shared preflight preserves parameter, environment, managed and PATH SDK resolution order'
Assert-Equal $true ($buildPreflightSource.Contains("Join-Path `$RepositoryRoot 'global.json'") -and $buildPreflightSource.Contains('Test-CountdownSdkVersionMatch')) 'Shared preflight resolves and checks the repository global.json SDK contract'
Assert-Equal $true ($buildPreflightScript.Contains('[switch]$RequireClean') -and $buildPreflightScript.Contains('[switch]$Json') -and $buildPreflightScript.Contains('Failures')) 'Build preflight entry point exposes clean gating and structured failure output'
$installerDotnetPathIndex = $installerSource.IndexOf("`$dotnetPath = 'D:\Codex\tools\dotnet\10.0\dotnet.exe'")
$installerDotnetCheckIndex = $installerSource.IndexOf('if (-not $DeferCommit -and -not (Test-Path -LiteralPath $dotnetPath -PathType Leaf))', $installerDotnetPathIndex)
$installerDotnetErrorIndex = $installerSource.IndexOf('未找到验收套件所需的受管 dotnet', $installerDotnetCheckIndex)
$installerValidationSuiteIndex = $installerSource.IndexOf('$validationSuite = Invoke-CountdownValidationSuite')
$installerDotnetPassIndex = $installerSource.IndexOf('-DotnetPath $dotnetPath', $installerValidationSuiteIndex)
Assert-Equal $true `
    ($installerDotnetPathIndex -ge 0 -and
        $installerDotnetCheckIndex -gt $installerDotnetPathIndex -and
        $installerDotnetErrorIndex -gt $installerDotnetCheckIndex -and
        $installerValidationSuiteIndex -gt $installerDotnetErrorIndex -and
        $installerDotnetPassIndex -gt $installerValidationSuiteIndex) `
    'Installer validates and passes the canonical managed dotnet 10.0 executable before invoking the validation suite'
Assert-Equal $true ($installerSource -match 'if \(-not \$DeferCommit\) \{\r?\n(?:\s*#[^\r\n]*\r?\n)?\s*Import-Module \(Join-Path \$staging ''Countdown\.Validation\.psm1''\) -Force\r?\n\s*\$validationSuite = Invoke-CountdownValidationSuite') 'Installer loads the validation module and runs the validation suite only outside the office deferred commit'
Assert-Equal 1 ([regex]::Matches($installerSource, 'Countdown\.Validation\.psm1').Count) 'Installer references the validation module only inside the local-install branch'
Assert-Equal 2 ([regex]::Matches($buildSource, '-p:IncludeSourceRevisionInInformationalVersion=false').Count) 'Both self-contained publish commands exclude the Git revision so committed manifest hashes remain reproducible'
Assert-Equal $true ($buildPreflightSource.Contains("Join-Path `$project 'bin'") -and $buildPreflightSource.Contains("Join-Path `$project 'obj'")) 'Shared build path contract includes managed bin and obj directories for each project'
Assert-Equal $true $buildSource.Contains('$buildPaths = Get-CountdownBuildPaths -RepositoryRoot $PSScriptRoot') 'Clean build derives candidates from the shared build path contract'
Assert-Equal $true $buildSource.Contains('$managedCleanCandidates = @($buildPaths.CleanCandidates)') 'Clean build passes the shared candidate list to the managed cleanup gate'
Assert-Equal $true ($buildPreflightSource.Contains("'MinminCountdown.Core'") -and $buildPreflightSource.Contains("'MinminCountdown'") -and $buildPreflightSource.Contains("'MinminCountdown.Verification'")) 'Shared build path contract covers all three C# projects'
Assert-Equal $true $buildSource.Contains('$resolvedCleanPath.StartsWith($repositoryRootPrefix, [System.StringComparison]::OrdinalIgnoreCase)') 'Clean build rejects absolute removal targets outside the repository root'
Assert-Equal $true $buildSource.Contains('$resolvedCleanPath -notin $expectedCleanPaths') 'Clean build validates every absolute removal target against its exact allowlist'
Assert-Equal $true $buildSource.Contains("`$runEvidenceCapabilityRoot = 'D:\agents\run-evidence'") 'Clean build uses the governed run-evidence capability root for isolated release clones'
Assert-Equal $true $buildSource.Contains('$resolvedCleanPath.StartsWith($runEvidenceRootPrefix, [System.StringComparison]::OrdinalIgnoreCase)') 'Clean build detects output paths inside the governed run-evidence tree'
Assert-Equal $true $buildSource.Contains('-CapabilityRoot $cleanupCapabilityRoot') 'Clean build passes the resolved stable capability root to safe-delete'
Assert-Equal $false $buildSource.Contains('New-CountdownStartupRequest') 'Build writes no startup request: the host reads the installer request at the application root, never app\startup-request.json'
$buildPayloadHashSegmentIndex = $buildSource.IndexOf("Write-BuildSegmentTiming -Name 'AssetValidationAndPayloadHash'")
$buildPayloadEnumerationIndex = $buildSource.IndexOf('$payloadFiles = @(')
$buildPayloadLegacyStartupRequestExclusionIndex = $buildSource.IndexOf('$_.FullName -ne $legacyStartupRequestPath')
Assert-Equal $true ($buildPayloadEnumerationIndex -ge 0 -and $buildPayloadLegacyStartupRequestExclusionIndex -gt $buildPayloadEnumerationIndex -and $buildPayloadLegacyStartupRequestExclusionIndex -lt $buildPayloadHashSegmentIndex) 'Payload enumeration skips a stale app\startup-request.json left by older builds so incremental and -Clean manifests match'
$installLifecycleSource = [System.IO.File]::ReadAllText((Join-Path $PSScriptRoot 'Countdown.InstallLifecycle.psm1'), [System.Text.Encoding]::UTF8)
$lifecycleCommittedRecoveryMessageIndex = $installLifecycleSource.IndexOf('检测到已提交但未完成应用解锁确认的事务')
$lifecycleOwnershipRecoveryMessageIndex = $installLifecycleSource.IndexOf('检测到用户替换的桌面启动器：应用、任务和时间服务已恢复')
Assert-Equal $true ($lifecycleCommittedRecoveryMessageIndex -ge 0 -and $lifecycleCommittedRecoveryMessageIndex -lt $lifecycleOwnershipRecoveryMessageIndex) 'Install lifecycle prioritizes committed recovery evidence over a simultaneous launcher conflict'
$timeSourceWaitDefinitionIndex = $installLifecycleSource.IndexOf('function Wait-CountdownNetworkTimeSource')
$timeSourceWaitCallIndex = $installLifecycleSource.IndexOf('$source = Wait-CountdownNetworkTimeSource')
Assert-Equal $true ($timeSourceWaitDefinitionIndex -ge 0 -and $timeSourceWaitCallIndex -gt $timeSourceWaitDefinitionIndex) 'Production Windows ports wait with the bounded network-time source probe'
Assert-Equal $true ($installLifecycleSource -notmatch 'Start-Sleep -Seconds 3') 'Install lifecycle does not rely on a fixed three-second time-service sleep'
Assert-Equal $true ($deploymentSource.Contains('Get-OfficeRemoteDeploymentFailureReceipt') -and $deploymentSource.Contains('remote-deployment-failure.json') -and $deploymentSource.Contains('ConvertFrom-CountdownInstallFailureReceiptBytes') -and $deploymentSource.Contains('ReparsePoint') -and $deploymentSource.Contains('WriteAllText(') -and -not $deploymentSource.Contains('WriteAllBytes($failureReceiptEvidencePath')) 'Office deployment validates a bounded non-reparse remote failure receipt before persisting normalized evidence'
$deploymentCleanMainIndex = $deploymentSource.IndexOf('$initialRepositoryState = Assert-CountdownDeploymentRepositoryState')
$deploymentLocalValidationIndex = $deploymentSource.IndexOf('$localValidation = Find-CountdownLocalValidationRun -Commit $commit -RepositoryRoot $repoRoot')
$deploymentSnapshotIndex = $deploymentSource.IndexOf('Export-CountdownDeploymentCommitSnapshot `')
Assert-Equal $true ($deploymentCleanMainIndex -ge 0 -and $deploymentLocalValidationIndex -gt $deploymentCleanMainIndex -and $deploymentSnapshotIndex -gt $deploymentLocalValidationIndex) 'Office deployment requires same-commit local suite evidence after the clean-main check and before building'
$adapterSource =[System.IO.File]::ReadAllText((Join-Path $PSScriptRoot '敏敏子倒计时.ps1'), [System.Text.Encoding]::UTF8)
$adapterWaitForExitIndex = $adapterSource.IndexOf('$process.WaitForExit()')
$adapterStayAttachedIndex = $adapterSource.IndexOf('$stayAttached = $ValidateOnly.IsPresent')
$adapterAliveProbeIndex = $adapterSource.IndexOf('Start-Sleep -Milliseconds 200')
$adapterHasExitedIndex = $adapterSource.IndexOf('$process.HasExited')
Assert-Equal $true ($adapterStayAttachedIndex -ge 0 -and $adapterWaitForExitIndex -gt $adapterStayAttachedIndex) 'Production adapter waits for exit only in validate/exercise/test modes'
Assert-Equal $true ($adapterAliveProbeIndex -gt $adapterWaitForExitIndex -and $adapterHasExitedIndex -gt $adapterAliveProbeIndex) 'Production adapter detaches after confirming the C# host stayed alive'
Assert-Equal $true ($adapterSource.Contains('Win32_Process') -and $adapterSource.Contains('MethodName Create') -and $adapterSource.Contains('脱离计划任务作业')) 'Production adapter starts the C# host outside the scheduled-task job'
$remoteDeploymentSource = [System.IO.File]::ReadAllText((Join-Path $PSScriptRoot 'Countdown.RemoteDeployment.psm1'), [System.Text.Encoding]::UTF8)
Assert-Equal $true ($remoteDeploymentSource.Contains('ConvertFrom-Json -DateKind String') -and $remoteDeploymentSource.Contains('ConvertTo-CountdownSettingsFingerprintMtimeUtc')) 'Independent readback keeps settings mtime as a string and normalizes ConvertFrom-Json DateTime coercion'
$mtimeSample = '2026-08-17T05:20:04.4405459Z'
$mtimeJson = (@{ ExpectedSettingsMtimeUtc = $mtimeSample } | ConvertTo-Json -Compress)
$coercedMtime = ($mtimeJson | ConvertFrom-Json).ExpectedSettingsMtimeUtc
$keptMtime = ($mtimeJson | ConvertFrom-Json -DateKind String).ExpectedSettingsMtimeUtc
Assert-Equal $true ($coercedMtime -is [datetime] -or [string] $keptMtime -eq $mtimeSample) 'ConvertFrom-Json either keeps the settings mtime string or yields a DateTime the fingerprint helper can normalize'
Assert-Equal $false ('Build-CountdownApplication.ps1' -in @($releaseContract.PackageFiles)) 'Build script stays out of the office package; the deployer runs it from the commit-identity-checked snapshot'
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
Assert-Equal 10 $canonicalLegacyV3Object.schemaVersion 'Canonical settings schema'
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
Assert-Equal $true (Test-CountdownSettingsDeploymentTransition -BeforeHash 'before' -AfterHash 'after' -BeforeJson ($legacyV3Settings | ConvertTo-Json -Depth 4) -AfterJson $canonicalLegacyV3) 'Strict schema v3 to v10 migration is accepted for deployment'
$legacyV1Settings = [pscustomobject]@{ schemaVersion = 1; timeZoneId = 'China Standard Time'; targetUtc = '2099-01-01T00:00:00Z' }
$canonicalLegacyV1 = ConvertTo-CountdownSettingsJson -State (ConvertFrom-CountdownSettingsObject -Settings $legacyV1Settings)
Assert-Equal $false (Test-CountdownSettingsDeploymentTransition -BeforeHash 'before' -AfterHash 'after' -BeforeJson ($legacyV1Settings | ConvertTo-Json -Depth 4) -AfterJson $canonicalLegacyV1) 'Schema v1 migration remains rejected for deployment'
$settingsModuleTestPath = Join-Path $PSScriptRoot 'settings-module-test.json'
try {
    Save-CountdownSettings -Path $settingsModuleTestPath -State $loadedLegacyV3
    $savedSettings = [System.IO.File]::ReadAllText($settingsModuleTestPath, [System.Text.Encoding]::UTF8) | ConvertFrom-Json
    Assert-Equal 10 $savedSettings.schemaVersion 'Settings module writes canonical schema'
    Assert-Equal $false (Test-Path -LiteralPath "$settingsModuleTestPath.tmp") 'Settings module cleans temporary file'
    $canonicalV10Reload = ConvertFrom-CountdownSettingsObject -Settings $savedSettings
    Assert-Equal $false $canonicalV10Reload.Migrated 'Saved canonical v10 is not marked for migration'
    $canonicalV10Item = Get-Item -LiteralPath $settingsModuleTestPath
    $canonicalV10Hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $settingsModuleTestPath).Hash
    $canonicalV10Length = [long] $canonicalV10Item.Length
    $canonicalV10Mtime = $canonicalV10Item.LastWriteTimeUtc.ToUniversalTime().ToString('o')
    Start-Sleep -Milliseconds 20
    Save-CountdownSettings -Path $settingsModuleTestPath -State $canonicalV10Reload
    $resavedV10Item = Get-Item -LiteralPath $settingsModuleTestPath
    Assert-Equal $canonicalV10Hash (Get-FileHash -Algorithm SHA256 -LiteralPath $settingsModuleTestPath).Hash 'Re-saving already-canonical v10 does not change SHA-256'
    Assert-Equal $canonicalV10Length ([long] $resavedV10Item.Length) 'Re-saving already-canonical v10 does not change length'
    Assert-Equal $canonicalV10Mtime $resavedV10Item.LastWriteTimeUtc.ToUniversalTime().ToString('o') 'Re-saving already-canonical v10 does not change LastWriteTimeUtc'
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
Assert-Equal $true (Test-CountdownSettingsDeploymentTransition -BeforeHash 'before' -AfterHash 'after' -BeforeJson ($legacyV4Settings | ConvertTo-Json -Depth 4) -AfterJson $canonicalLegacyV4) 'Strict schema v4 to v10 migration is accepted for deployment'
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
Assert-Equal 10 $canonicalFromV5.schemaVersion 'Schema v5 migrates to canonical schema v10'
Assert-Equal 'Auto' $canonicalFromV5.themePreference 'Schema v5 migration defaults the theme preference to Auto'
Assert-Equal '#6B8E23' $canonicalFromV5.tags[0].color 'Schema v10 serializes tag color'
Assert-Equal $true ($null -eq $canonicalFromV5.tags[1].PSObject.Properties['color']) 'Schema v10 omits an unspecified tag color'
Assert-Equal 'work' $canonicalFromV5.countdowns[0].tagIds[0] 'Schema v10 serializes countdown tag IDs'
Assert-Equal 'home' $canonicalFromV5.notes[0].tagIds[0] 'Schema v10 serializes note tag IDs'
Assert-Equal $null $canonicalFromV5.notes[0].date 'Schema v10 preserves an unknown legacy note date as null'
Assert-Equal 0 @($canonicalFromV5.notes[0].reminderRules).Count 'Schema v10 disables reminders for migrated notes'
$loadedNullDateV10 = ConvertFrom-CountdownSettingsObject -Settings $canonicalFromV5
Assert-Equal $null $loadedNullDateV10.Notes[0].date 'Schema v10 accepts an explicitly unknown note date'
Assert-Equal $true (Test-CountdownSettingsDeploymentTransition -BeforeHash 'before' -AfterHash 'after' -BeforeJson ($v5Settings | ConvertTo-Json -Depth 5) -AfterJson ($canonicalFromV5 | ConvertTo-Json -Depth 8)) 'Strict schema v5 to v10 migration is accepted for deployment'
$invalidV5Transition = $canonicalFromV5 | ConvertTo-Json -Depth 5 | ConvertFrom-Json
$invalidV5Transition.notes[0].date = '2026-07-18'
Assert-Equal $false (Test-CountdownSettingsDeploymentTransition -BeforeHash 'before' -AfterHash 'after' -BeforeJson ($v5Settings | ConvertTo-Json -Depth 5) -AfterJson ($invalidV5Transition | ConvertTo-Json -Depth 5)) 'Schema v5 migration rejects an invented note date'

$v6Settings = $canonicalFromV5 | ConvertTo-Json -Depth 8 | ConvertFrom-Json
$v6Settings.schemaVersion = 6
$v6Settings.PSObject.Properties.Remove('themePreference')
$v6Settings.PSObject.Properties.Remove('reminderStates')
foreach ($countdown in @($v6Settings.countdowns)) { $countdown.PSObject.Properties.Remove('reminderRules') }
foreach ($note in @($v6Settings.notes)) { $note.PSObject.Properties.Remove('reminderRules') }
$v6Settings.notes[0].date = '2026-07-18'
$loadedV6 = ConvertFrom-CountdownSettingsObject -Settings $v6Settings
Assert-Equal $true $loadedV6.Migrated 'Schema v6 settings migrate to add reminder fields'
Assert-Equal '2026-07-18' $loadedV6.Notes[0].date 'Schema v6 loads an exact note date'
Assert-Equal $null $loadedV6.Notes[0].reminderIntervalMinutes 'Schema v6 migration disables reminders'
$canonicalV6 = ConvertTo-CountdownSettingsJson -State $loadedV6 | ConvertFrom-Json
Assert-Equal 10 $canonicalV6.schemaVersion 'Schema v6 migrates to canonical schema v10'
Assert-Equal '2026-07-18' $canonicalV6.notes[0].date 'Schema v10 preserves an exact note date'
Assert-Equal 0 @($canonicalV6.notes[0].reminderRules).Count 'Schema v10 persists a disabled reminder explicitly'
Assert-Equal $true (Test-CountdownSettingsDeploymentTransition -BeforeHash 'before' -AfterHash 'after' -BeforeJson ($v6Settings | ConvertTo-Json -Depth 8) -AfterJson ($canonicalV6 | ConvertTo-Json -Depth 8)) 'Strict schema v6 to v10 migration is accepted for deployment'

$v7Settings = $canonicalV6 | ConvertTo-Json -Depth 8 | ConvertFrom-Json
$v7Settings.schemaVersion = 7
$v7Settings.PSObject.Properties.Remove('themePreference')
$v7Settings.PSObject.Properties.Remove('reminderStates')
foreach ($countdown in @($v7Settings.countdowns)) { $countdown.PSObject.Properties.Remove('reminderRules') }
foreach ($note in @($v7Settings.notes)) { $note.PSObject.Properties.Remove('reminderRules') }
$v7Settings.notes[0] | Add-Member -NotePropertyName reminderIntervalMinutes -NotePropertyValue 30 -Force
$v7Settings.notes[0] | Add-Member -NotePropertyName reminderStartUtc -NotePropertyValue '2099-07-19T00:00:00.0000000+00:00' -Force
$loadedV7 = ConvertFrom-CountdownSettingsObject -Settings $v7Settings
Assert-Equal $true $loadedV7.Migrated 'Schema v7 settings migrate to schema v10'
Assert-Equal 30 $loadedV7.Notes[0].reminderIntervalMinutes 'Schema v7 loads a reminder interval'
Assert-Equal ([DateTimeOffset]'2099-07-19T00:00:00Z') $loadedV7.Notes[0].reminderStartUtc 'Schema v7 loads a UTC reminder start'
$canonicalV7 = ConvertTo-CountdownSettingsJson -State $loadedV7 | ConvertFrom-Json
Assert-Equal 10 $canonicalV7.schemaVersion 'Schema v7 writes canonical schema v10'
Assert-Equal 'fixedInterval' $canonicalV7.notes[0].reminderRules[0].type 'Schema v7 migrates to a fixed-interval rule'
Assert-Equal 30 $canonicalV7.notes[0].reminderRules[0].intervalMinutes 'Schema v7 preserves a reminder interval'
Assert-Equal ([DateTimeOffset]'2099-07-19T00:30:00Z') ([DateTimeOffset] $canonicalV7.notes[0].reminderRules[0].firstDueUtc).ToUniversalTime() 'Schema v7 preserves the first due time'
Assert-Equal $true (Test-CountdownSettingsDeploymentTransition -BeforeHash 'before' -AfterHash 'after' -BeforeJson ($v7Settings | ConvertTo-Json -Depth 8) -AfterJson ($canonicalV7 | ConvertTo-Json -Depth 8)) 'Strict schema v7 to v10 migration is accepted for deployment'
$preFreezeV7 = $canonicalV7 | ConvertTo-Json -Depth 5 | ConvertFrom-Json
$preFreezeV7.monthlySalary = 100
$postFreezeV7 = $canonicalV7 | ConvertTo-Json -Depth 5 | ConvertFrom-Json
$postFreezeV7.monthlySalary = 200
Assert-Equal $false (Test-CountdownSettingsDeploymentTransition -BeforeHash 'pre-freeze' -AfterHash 'post-freeze' -BeforeJson ($preFreezeV7 | ConvertTo-Json -Depth 5) -AfterJson ($postFreezeV7 | ConvertTo-Json -Depth 5)) 'Schema v7 changes after the selected deployment baseline are rejected'
Assert-Equal $true (Test-CountdownSettingsDeploymentTransition -BeforeHash 'post-freeze' -AfterHash 'post-freeze' -BeforeJson ($postFreezeV7 | ConvertTo-Json -Depth 5) -AfterJson ($postFreezeV7 | ConvertTo-Json -Depth 5)) 'A pre-freeze save preserved in the transaction snapshot remains deployable'
$reloadedCanonicalV7 = ConvertFrom-CountdownSettingsObject -Settings $canonicalV7
Assert-Equal 2 $reloadedCanonicalV7.Tags.Count 'Schema v10 reloads tags without optional colors'
Assert-Equal $null $reloadedCanonicalV7.Tags[1].color 'Schema v10 reload preserves an unspecified tag color'

$v8OnceSettings = $canonicalV7 | ConvertTo-Json -Depth 8 | ConvertFrom-Json
$v8OnceSettings.schemaVersion = 8
$v8OnceSettings.PSObject.Properties.Remove('themePreference')
$v8OnceSettings.PSObject.Properties.Remove('reminderStates')
foreach ($countdown in @($v8OnceSettings.countdowns)) { $countdown.PSObject.Properties.Remove('reminderRules') }
foreach ($note in @($v8OnceSettings.notes)) { $note.PSObject.Properties.Remove('reminderRules') }
$v8OnceSettings.notes[0] | Add-Member -NotePropertyName reminder -NotePropertyValue ([pscustomobject]@{
        type = 'once'
        firstDueUtc = '2026-07-20T01:15:00.0000000+00:00'
        delivery = 'pending'
    }) -Force
$loadedV8Once = ConvertFrom-CountdownSettingsObject -Settings $v8OnceSettings
Assert-Equal $true $loadedV8Once.Migrated 'Schema v8 settings migrate to schema v10'
Assert-Equal 'Auto' $loadedV8Once.Preferences.ThemePreference 'Schema v8 defaults the theme preference to Auto'
Assert-Equal 'once' $loadedV8Once.Notes[0].reminder.type 'Schema v8 loads an once reminder'
Assert-Equal 'pending' $loadedV8Once.Notes[0].reminder.delivery 'Schema v8 preserves pending delivery state'
$canonicalV8Once = ConvertTo-CountdownSettingsJson -State $loadedV8Once | ConvertFrom-Json
Assert-Equal 'absoluteOnce' $canonicalV8Once.notes[0].reminderRules[0].type 'Schema v8 migrates an once reminder to an absolute rule'
Assert-Equal ([DateTimeOffset]'2026-07-20T01:15:00Z') ([DateTimeOffset] $canonicalV8Once.notes[0].reminderRules[0].scheduledUtc).ToUniversalTime() 'Schema v8 preserves the once due time'
Assert-Equal 0 @($canonicalV8Once.reminderStates.PSObject.Properties).Count 'Schema v8 preserves pending delivery by omitting handled state'
Assert-Equal 10 $canonicalV8Once.schemaVersion 'Schema v8 writes canonical schema v10'
Assert-Equal 'Auto' $canonicalV8Once.themePreference 'Schema v8 writes the default Auto theme preference'
Assert-Equal $true (Test-CountdownSettingsDeploymentTransition -BeforeHash 'before' -AfterHash 'after' -BeforeJson ($v8OnceSettings | ConvertTo-Json -Depth 8) -AfterJson ($canonicalV8Once | ConvertTo-Json -Depth 8)) 'Strict schema v8 to v10 migration is accepted for deployment'
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

$v9NightSettings = $canonicalV8Once | ConvertTo-Json -Depth 8 | ConvertFrom-Json
$v9NightSettings.schemaVersion = 9
$v9NightSettings.themePreference = 'Night'
foreach ($countdown in @($v9NightSettings.countdowns)) { $countdown.PSObject.Properties.Remove('reminderRules') }
foreach ($note in @($v9NightSettings.notes)) {
    $note.PSObject.Properties.Remove('reminderRules')
    $note | Add-Member -NotePropertyName reminder -NotePropertyValue ([pscustomobject]@{
        type = 'once'
        firstDueUtc = '2026-07-20T01:15:00.0000000+00:00'
        delivery = 'pending'
    }) -Force
}
$v9NightSettings.PSObject.Properties.Remove('reminderStates')
$loadedV9Night = ConvertFrom-CountdownSettingsObject -Settings $v9NightSettings
Assert-Equal $true $loadedV9Night.Migrated 'Schema v9 settings migrate to schema v10'
Assert-Equal 'Night' $loadedV9Night.Preferences.ThemePreference 'Schema v9 loads a manual night preference'
$canonicalV9Night = ConvertTo-CountdownSettingsJson -State $loadedV9Night | ConvertFrom-Json
Assert-Equal 10 $canonicalV9Night.schemaVersion 'Schema v9 writes canonical schema v10'
Assert-Equal 'Night' $canonicalV9Night.themePreference 'Schema v9 migration preserves a manual night preference'
Assert-Equal 'absoluteOnce' $canonicalV9Night.notes[0].reminderRules[0].type 'Schema v9 migrates a pending once reminder'
Assert-Equal 0 @($canonicalV9Night.reminderStates.PSObject.Properties).Count 'Schema v9 pending once migration does not invent handled state'
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

$v9PendingRuleIdFirst = [string] (ConvertTo-CountdownSettingsJson -State (
        ConvertFrom-CountdownSettingsObject -Settings $v9NightSettings -MigrationUtc ([DateTimeOffset]'2026-07-20T02:16:00Z')
    ) | ConvertFrom-Json).notes[0].reminderRules[0].id
$v9PendingRuleIdSecond = [string] (ConvertTo-CountdownSettingsJson -State (
        ConvertFrom-CountdownSettingsObject -Settings $v9NightSettings -MigrationUtc ([DateTimeOffset]'2030-01-01T00:00:00Z')
    ) | ConvertFrom-Json).notes[0].reminderRules[0].id
Assert-Equal $v9PendingRuleIdFirst $v9PendingRuleIdSecond 'Schema v9 migration derives a stable reminder rule ID independent of wall-clock time'

$v9DeliveredOnceSettings = $v9NightSettings | ConvertTo-Json -Depth 8 | ConvertFrom-Json
$v9DeliveredOnceSettings.notes[0].reminder.delivery = 'delivered'
$canonicalV9DeliveredOnce = ConvertTo-CountdownSettingsJson -State (
    ConvertFrom-CountdownSettingsObject -Settings $v9DeliveredOnceSettings -MigrationUtc ([DateTimeOffset]'2026-07-20T02:16:00Z')
) | ConvertFrom-Json
$v9DeliveredRuleId = [string] $canonicalV9DeliveredOnce.notes[0].reminderRules[0].id
$v9DeliveredState = $canonicalV9DeliveredOnce.reminderStates.PSObject.Properties[$v9DeliveredRuleId].Value
Assert-Equal ([DateTimeOffset]'2026-07-20T01:15:00Z') ([DateTimeOffset] $v9DeliveredState.handledThroughUtc).ToUniversalTime() 'Schema v9 delivered once migration preserves the handled cursor'
Assert-Equal ("{0}|2026-07-20T01:15:00.0000000+00:00" -f $v9DeliveredRuleId) ([string] $v9DeliveredState.lastDeliveredOccurrenceKey) 'Schema v9 delivered once migration persists the canonical occurrence key'
Assert-Equal $null $v9DeliveredState.lastDispositionOccurrenceKey 'Schema v9 delivered once migration does not invent disposition'
Assert-Equal $null $v9DeliveredState.activeSnooze 'Schema v9 delivered once migration does not invent snooze state'

$v9RepeatSettings = $v9NightSettings | ConvertTo-Json -Depth 8 | ConvertFrom-Json
$v9RepeatSettings.notes[0].reminder = [pscustomobject]@{
    type = 'repeat'
    firstDueUtc = '2026-07-20T01:00:00.0000000+00:00'
    intervalMinutes = 30
}
$v9RepeatMigrationUtc = [DateTimeOffset]'2026-07-20T02:16:00Z'
$loadedV9Repeat = ConvertFrom-CountdownSettingsObject -Settings $v9RepeatSettings -MigrationUtc $v9RepeatMigrationUtc
$canonicalV9Repeat = ConvertTo-CountdownSettingsJson -State $loadedV9Repeat | ConvertFrom-Json
$v9RepeatRuleId = [string] $canonicalV9Repeat.notes[0].reminderRules[0].id
$v9RepeatState = $canonicalV9Repeat.reminderStates.PSObject.Properties[$v9RepeatRuleId].Value
Assert-Equal 'fixedInterval' $canonicalV9Repeat.notes[0].reminderRules[0].type 'Schema v9 repeat migration creates a fixed-interval rule'
Assert-Equal ([DateTimeOffset]'2026-07-20T02:00:00Z') ([DateTimeOffset] $v9RepeatState.handledThroughUtc).ToUniversalTime() 'Schema v9 repeat migration advances the cursor to the latest elapsed occurrence'
Assert-Equal $null $v9RepeatState.lastDeliveredOccurrenceKey 'Schema v9 repeat migration does not claim a rendered delivery'
Assert-Equal $null $v9RepeatState.lastDispositionOccurrenceKey 'Schema v9 repeat migration does not invent disposition'
Assert-Equal $null $v9RepeatState.activeSnooze 'Schema v9 repeat migration does not invent snooze state'
$reloadedV10Repeat = ConvertFrom-CountdownSettingsObject -Settings $canonicalV9Repeat -MigrationUtc ([DateTimeOffset]'2099-01-01T00:00:00Z')
Assert-Equal $false $reloadedV10Repeat.Migrated 'Canonical schema v10 reload does not migrate'
Assert-Equal 'repeat' $reloadedV10Repeat.Notes[0].reminder.type 'Canonical schema v10 projects the first fixed-interval rule for the legacy UI'
Assert-Equal ($canonicalV9Repeat | ConvertTo-Json -Depth 8) ((ConvertTo-CountdownSettingsJson -State $reloadedV10Repeat | ConvertFrom-Json) | ConvertTo-Json -Depth 8) 'Schema v10 reminder rules and cursor round-trip canonically'

$multiRuleV10 = $canonicalV9Night | ConvertTo-Json -Depth 8 | ConvertFrom-Json
$multiRuleV10.notes[0].reminderRules = @(
    [pscustomobject]@{ id = 'multi-absolute'; type = 'absoluteOnce'; scheduledUtc = '2099-01-01T00:00:00.0000000+00:00' },
    [pscustomobject]@{ id = 'multi-fixed'; type = 'fixedInterval'; firstDueUtc = '2099-01-02T00:00:00.0000000+00:00'; intervalMinutes = 45 }
)
$multiRuleV10.reminderStates = [pscustomobject]@{}
$multiRuleState = ConvertFrom-CountdownSettingsObject -Settings $multiRuleV10
$multiRuleCanonical = ConvertTo-CountdownSettingsJson -State $multiRuleState | ConvertFrom-Json
Assert-Equal 2 @($multiRuleCanonical.notes[0].reminderRules).Count 'Schema v10 round-trips multiple reminder rules on one record'
Assert-Equal 'multi-absolute' $multiRuleCanonical.notes[0].reminderRules[0].id 'Schema v10 preserves reminder rule order and identity'
Assert-Equal 'multi-fixed' $multiRuleCanonical.notes[0].reminderRules[1].id 'Schema v10 preserves a second reminder rule identity'

$relativeV10 = $multiRuleV10 | ConvertTo-Json -Depth 8 | ConvertFrom-Json
$relativeV10.countdowns[0].targetUtc = '2099-01-01T00:10:00.0000000+00:00'
$relativeV10.countdowns[0].reminderRules = @(
    [pscustomobject]@{ id = 'relative-zero'; type = 'countdownRelative'; offsetMinutesBefore = 0 },
    [pscustomobject]@{ id = 'relative-max'; type = 'countdownRelative'; offsetMinutesBefore = 5256000 }
)
$relativeV10.reminderStates = [pscustomobject]@{}
$relativeV10.reminderStates | Add-Member -NotePropertyName 'relative-max' -NotePropertyValue ([pscustomobject]@{
        handledThroughUtc = '2088-12-31T23:50:00.0000000+00:00'
        lastDeliveredOccurrenceKey = 'relative-max|2088-12-31T23:50:00.0000000+00:00'
        lastDispositionOccurrenceKey = $null
        activeSnooze = $null
    })
$relativeState = ConvertFrom-CountdownSettingsObject -Settings $relativeV10
$relativeCanonical = ConvertTo-CountdownSettingsJson -State $relativeState | ConvertFrom-Json
Assert-Equal 2 @($relativeCanonical.countdowns[0].reminderRules).Count 'Schema v10 round-trips countdown-relative reminder rules'
Assert-Equal 0 $relativeCanonical.countdowns[0].reminderRules[0].offsetMinutesBefore 'Schema v10 accepts a zero countdown-relative offset'
Assert-Equal 5256000 $relativeCanonical.countdowns[0].reminderRules[1].offsetMinutesBefore 'Schema v10 accepts the maximum countdown-relative offset'
Assert-Equal 'relative-max|2088-12-31T23:50:00.0000000+00:00' $relativeCanonical.reminderStates.'relative-max'.lastDeliveredOccurrenceKey 'Schema v10 preserves a historical delivered relative occurrence after target movement'
$relativeReloaded = ConvertFrom-CountdownSettingsObject -Settings $relativeCanonical
Assert-Equal ($relativeCanonical | ConvertTo-Json -Depth 8) ((ConvertTo-CountdownSettingsJson -State $relativeReloaded | ConvertFrom-Json) | ConvertTo-Json -Depth 8) 'Schema v10 countdown-relative rules round-trip canonically'

$calendarV10 = $multiRuleV10 | ConvertTo-Json -Depth 12 | ConvertFrom-Json
$calendarV10.notes[0].reminderRules = @(
    [pscustomobject]@{ id = 'calendar-daily'; type = 'calendar'; frequency = 'daily'; interval = 2; startDate = '2032-01-01'; localTime = '09:00' },
    [pscustomobject]@{ id = 'calendar-weekly'; type = 'calendar'; frequency = 'weekly'; interval = 3; startDate = '2032-01-05'; localTime = '10:30'; weekdays = @('friday', 'monday') },
    [pscustomobject]@{ id = 'calendar-monthly-day'; type = 'calendar'; frequency = 'monthly'; interval = 1; startDate = '2032-01-01'; localTime = '11:00'; day = 31 },
    [pscustomobject]@{ id = 'calendar-yearly-last'; type = 'calendar'; frequency = 'yearly'; interval = 4; startDate = '2032-01-01'; localTime = '12:00'; month = 2; lastDay = $true }
)
$calendarV10.countdowns[0].reminderRules = @(
    [pscustomobject]@{ id = 'calendar-countdown-last'; type = 'calendar'; frequency = 'monthly'; interval = 1; startDate = '2032-01-01'; localTime = '09:00'; lastDay = $true }
)
$calendarV10.reminderStates = [pscustomobject]@{}
$calendarV10.reminderStates | Add-Member -NotePropertyName 'calendar-countdown-last' -NotePropertyValue ([pscustomobject]@{
        handledThroughUtc = '2032-02-29T01:00:00.0000000+00:00'
        lastDeliveredOccurrenceKey = 'calendar-countdown-last|2032-02-29T01:00:00.0000000+00:00'
        lastDispositionOccurrenceKey = $null
        activeSnooze = $null
    })
$calendarState = ConvertFrom-CountdownSettingsObject -Settings $calendarV10
$calendarCanonical = ConvertTo-CountdownSettingsJson -State $calendarState | ConvertFrom-Json
Assert-Equal 4 @($calendarCanonical.notes[0].reminderRules).Count 'Schema v10 round-trips all four calendar frequencies'
Assert-Equal 'monday' $calendarCanonical.notes[0].reminderRules[1].weekdays[0] 'Schema v10 canonicalizes weekly selectors to Monday-first order'
Assert-Equal 'friday' $calendarCanonical.notes[0].reminderRules[1].weekdays[1] 'Schema v10 preserves all typed weekly selector values'
Assert-Equal 31 $calendarCanonical.notes[0].reminderRules[2].day 'Schema v10 preserves an explicit monthly day without clamping'
Assert-Equal $true $calendarCanonical.notes[0].reminderRules[3].lastDay 'Schema v10 preserves an explicit yearly lastDay selector'
Assert-Equal $true $calendarCanonical.countdowns[0].reminderRules[0].lastDay 'Schema v10 supports calendar selectors on countdowns'
Assert-Equal 'none' $calendarState.Notes[0].reminder.type 'Schema v10 keeps calendar rules out of the legacy reminder projection'
Assert-Equal ($calendarCanonical | ConvertTo-Json -Depth 12) ((ConvertTo-CountdownSettingsJson -State (ConvertFrom-CountdownSettingsObject -Settings $calendarCanonical) | ConvertFrom-Json) | ConvertTo-Json -Depth 12) 'Schema v10 calendar rules and states round-trip canonically'

$invalidCalendarCursorRejected = $false
try {
    $invalid = $calendarV10 | ConvertTo-Json -Depth 12 | ConvertFrom-Json
    $invalid.reminderStates.'calendar-countdown-last'.handledThroughUtc = '2032-02-28T01:00:00.0000000+00:00'
    $invalid.reminderStates.'calendar-countdown-last'.lastDeliveredOccurrenceKey = 'calendar-countdown-last|2032-02-28T01:00:00.0000000+00:00'
    ConvertFrom-CountdownSettingsObject -Settings $invalid | Out-Null
}
catch { $invalidCalendarCursorRejected = $true }
Assert-Equal $true $invalidCalendarCursorRejected 'Schema v10 rejects a calendar cursor that is not a typed occurrence'

$duplicateCalendarScheduleRejected = $false
try {
    $invalid = $calendarV10 | ConvertTo-Json -Depth 12 | ConvertFrom-Json
    $invalid.notes[0].reminderRules = @(
        [pscustomobject]@{ id = 'calendar-duplicate-a'; type = 'calendar'; frequency = 'weekly'; interval = 2; startDate = '2032-01-05'; localTime = '09:00'; weekdays = @('friday', 'monday') },
        [pscustomobject]@{ id = 'calendar-duplicate-b'; type = 'calendar'; frequency = 'WEEKLY'; interval = 2; startDate = '2032-01-05'; localTime = '09:00'; weekdays = @('Monday', 'Friday') }
    )
    ConvertFrom-CountdownSettingsObject -Settings $invalid | Out-Null
}
catch { $duplicateCalendarScheduleRejected = $true }
Assert-Equal $true $duplicateCalendarScheduleRejected 'Schema v10 rejects duplicate canonical calendar schedules independent of weekday order and case'

foreach ($mutation in @(
        @{ Name = 'interval-zero'; Apply = { param($rule) $rule.interval = 0 } },
        @{ Name = 'interval-too-large'; Apply = { param($rule) $rule.interval = 1000 } },
        @{ Name = 'interval-decimal'; Apply = { param($rule) $rule.interval = 1.0 } },
        @{ Name = 'interval-string'; Apply = { param($rule) $rule.interval = '1' } },
        @{ Name = 'weekly-empty'; Apply = { param($rule) $rule.frequency = 'weekly'; $rule | Add-Member -NotePropertyName weekdays -NotePropertyValue @() } },
        @{ Name = 'monthly-both-selectors'; Apply = { param($rule) $rule.frequency = 'monthly'; $rule | Add-Member -NotePropertyName day -NotePropertyValue 31; $rule | Add-Member -NotePropertyName lastDay -NotePropertyValue $true } },
        @{ Name = 'monthly-false-last-day'; Apply = { param($rule) $rule.frequency = 'monthly'; $rule | Add-Member -NotePropertyName lastDay -NotePropertyValue $false } },
        @{ Name = 'yearly-invalid-month'; Apply = { param($rule) $rule.frequency = 'yearly'; $rule | Add-Member -NotePropertyName month -NotePropertyValue 13; $rule | Add-Member -NotePropertyName day -NotePropertyValue 1 } },
        @{ Name = 'unsupported-frequency'; Apply = { param($rule) $rule.frequency = 'hourly' } },
        @{ Name = 'free-form-rrule'; Apply = { param($rule) $rule | Add-Member -NotePropertyName rrule -NotePropertyValue 'FREQ=DAILY;COUNT=3' } }
    )) {
    $calendarMutationRejected = $false
    try {
        $invalid = $multiRuleV10 | ConvertTo-Json -Depth 12 | ConvertFrom-Json
        $rule = [pscustomobject]@{ id = 'calendar-invalid'; type = 'calendar'; frequency = 'daily'; interval = 1; startDate = '2032-01-01'; localTime = '09:00' }
        & $mutation.Apply $rule
        $invalid.notes[0].reminderRules = @($rule)
        $invalid.reminderStates = [pscustomobject]@{}
        ConvertFrom-CountdownSettingsObject -Settings $invalid | Out-Null
    }
    catch { $calendarMutationRejected = $true }
    Assert-Equal $true $calendarMutationRejected "Schema v10 rejects invalid calendar rule $($mutation.Name)"
}

$noteRelativeV10Rejected = $false
try {
    $invalid = $multiRuleV10 | ConvertTo-Json -Depth 8 | ConvertFrom-Json
    $invalid.notes[0].reminderRules = @(
        [pscustomobject]@{ id = 'note-relative'; type = 'countdownRelative'; offsetMinutesBefore = 0 }
    )
    ConvertFrom-CountdownSettingsObject -Settings $invalid | Out-Null
}
catch { $noteRelativeV10Rejected = $true }
Assert-Equal $true $noteRelativeV10Rejected 'Schema v10 rejects countdown-relative rules on notes'

foreach ($invalidOffset in @(-1, 5256001, 1.0, '1')) {
    $invalidRelativeOffsetRejected = $false
    try {
        $invalid = $relativeV10 | ConvertTo-Json -Depth 8 | ConvertFrom-Json
        $invalid.countdowns[0].reminderRules[0].offsetMinutesBefore = $invalidOffset
        ConvertFrom-CountdownSettingsObject -Settings $invalid | Out-Null
    }
    catch { $invalidRelativeOffsetRejected = $true }
    Assert-Equal $true $invalidRelativeOffsetRejected "Schema v10 rejects invalid countdown-relative offset $invalidOffset"
}

$relativeForeignFieldRejected = $false
try {
    $invalid = $relativeV10 | ConvertTo-Json -Depth 8 | ConvertFrom-Json
    $invalid.countdowns[0].reminderRules[0] | Add-Member -NotePropertyName scheduledUtc -NotePropertyValue '2099-01-01T00:10:00.0000000+00:00'
    ConvertFrom-CountdownSettingsObject -Settings $invalid | Out-Null
}
catch { $relativeForeignFieldRejected = $true }
Assert-Equal $true $relativeForeignFieldRejected 'Schema v10 rejects foreign fields on countdown-relative rules'

$unknownV10RuleRejected = $false
try {
    $invalid = $multiRuleV10 | ConvertTo-Json -Depth 8 | ConvertFrom-Json
    $invalid.notes[0].reminderRules[0].type = 'rfc5545'
    ConvertFrom-CountdownSettingsObject -Settings $invalid | Out-Null
}
catch { $unknownV10RuleRejected = $true }
Assert-Equal $true $unknownV10RuleRejected 'Schema v10 rejects unknown reminder rule types'

$duplicateV10ScheduleRejected = $false
try {
    $invalid = $multiRuleV10 | ConvertTo-Json -Depth 8 | ConvertFrom-Json
    $invalid.notes[0].reminderRules = @(
        [pscustomobject]@{ id = 'duplicate-a'; type = 'absoluteOnce'; scheduledUtc = '2099-01-01T00:00:00.0000000+00:00' },
        [pscustomobject]@{ id = 'duplicate-b'; type = 'absoluteOnce'; scheduledUtc = '2099-01-01T00:00:00.0000000+00:00' }
    )
    ConvertFrom-CountdownSettingsObject -Settings $invalid | Out-Null
}
catch { $duplicateV10ScheduleRejected = $true }
Assert-Equal $true $duplicateV10ScheduleRejected 'Schema v10 rejects duplicate canonical schedules on one record'

$tooManyV10RulesRejected = $false
try {
    $invalid = $multiRuleV10 | ConvertTo-Json -Depth 8 | ConvertFrom-Json
    $invalid.notes[0].reminderRules = @(0..8 | ForEach-Object {
            [pscustomobject]@{ id = "rule-$_"; type = 'absoluteOnce'; scheduledUtc = ([DateTimeOffset]'2099-01-01T00:00:00Z').AddMinutes($_).ToString('o') }
        })
    ConvertFrom-CountdownSettingsObject -Settings $invalid | Out-Null
}
catch { $tooManyV10RulesRejected = $true }
Assert-Equal $true $tooManyV10RulesRejected 'Schema v10 rejects more than eight reminder rules on one record'

$duplicateGlobalV10RuleIdRejected = $false
try {
    $invalid = $multiRuleV10 | ConvertTo-Json -Depth 8 | ConvertFrom-Json
    $invalid.countdowns[0].reminderRules = @([pscustomobject]@{ id = 'multi-absolute'; type = 'absoluteOnce'; scheduledUtc = '2099-02-01T00:00:00.0000000+00:00' })
    ConvertFrom-CountdownSettingsObject -Settings $invalid | Out-Null
}
catch { $duplicateGlobalV10RuleIdRejected = $true }
Assert-Equal $true $duplicateGlobalV10RuleIdRejected 'Schema v10 rejects duplicate global reminder rule IDs'

$danglingV10StateRejected = $false
try {
    $invalid = $multiRuleV10 | ConvertTo-Json -Depth 8 | ConvertFrom-Json
    $invalid.reminderStates | Add-Member -NotePropertyName 'missing-rule' -NotePropertyValue ([pscustomobject]@{ handledThroughUtc = '2099-01-01T00:00:00.0000000+00:00'; lastDeliveredOccurrenceKey = $null; lastDispositionOccurrenceKey = $null; activeSnooze = $null })
    ConvertFrom-CountdownSettingsObject -Settings $invalid | Out-Null
}
catch { $danglingV10StateRejected = $true }
Assert-Equal $true $danglingV10StateRejected 'Schema v10 rejects dangling reminder state'

$emptyV10StateRejected = $false
try {
    $invalid = $multiRuleV10 | ConvertTo-Json -Depth 8 | ConvertFrom-Json
    $invalid.reminderStates | Add-Member -NotePropertyName 'multi-absolute' -NotePropertyValue ([pscustomobject]@{ handledThroughUtc = $null; lastDeliveredOccurrenceKey = $null; lastDispositionOccurrenceKey = $null; activeSnooze = $null })
    ConvertFrom-CountdownSettingsObject -Settings $invalid | Out-Null
}
catch { $emptyV10StateRejected = $true }
Assert-Equal $true $emptyV10StateRejected 'Schema v10 rejects empty reminder state'

$earlyV10CursorRejected = $false
try {
    $invalid = $multiRuleV10 | ConvertTo-Json -Depth 8 | ConvertFrom-Json
    $invalid.reminderStates | Add-Member -NotePropertyName 'multi-absolute' -NotePropertyValue ([pscustomobject]@{ handledThroughUtc = '2098-12-31T23:59:59.0000000+00:00'; lastDeliveredOccurrenceKey = $null; lastDispositionOccurrenceKey = $null; activeSnooze = $null })
    ConvertFrom-CountdownSettingsObject -Settings $invalid | Out-Null
}
catch { $earlyV10CursorRejected = $true }
Assert-Equal $true $earlyV10CursorRejected 'Schema v10 rejects a cursor before the first scheduled occurrence'

foreach ($misalignedCursor in @(
        [pscustomobject]@{ RuleId = 'multi-absolute'; HandledThroughUtc = '2099-01-01T00:01:00.0000000+00:00'; Name = 'absolute once cursor after its unique occurrence' },
        [pscustomobject]@{ RuleId = 'multi-fixed'; HandledThroughUtc = '2099-01-02T00:01:00.0000000+00:00'; Name = 'fixed interval cursor between occurrences' }
    )) {
    $misalignedV10CursorRejected = $false
    try {
        $invalid = $multiRuleV10 | ConvertTo-Json -Depth 8 | ConvertFrom-Json
        $invalid.reminderStates | Add-Member -NotePropertyName $misalignedCursor.RuleId -NotePropertyValue ([pscustomobject]@{
                handledThroughUtc = $misalignedCursor.HandledThroughUtc
                lastDeliveredOccurrenceKey = $null
                lastDispositionOccurrenceKey = $null
                activeSnooze = $null
            })
        $invalidRawJson = $invalid | ConvertTo-Json -Depth 8
        ConvertFrom-CountdownSettingsObject -Settings ($invalidRawJson | ConvertFrom-Json) | Out-Null
    }
    catch { $misalignedV10CursorRejected = $true }
    Assert-Equal $true $misalignedV10CursorRejected "Schema v10 rejects $($misalignedCursor.Name)"
}

$alignedV10Cursors = $multiRuleV10 | ConvertTo-Json -Depth 8 | ConvertFrom-Json
$alignedV10Cursors.reminderStates | Add-Member -NotePropertyName 'multi-absolute' -NotePropertyValue ([pscustomobject]@{
        handledThroughUtc = '2099-01-01T00:00:00.0000000+00:00'
        lastDeliveredOccurrenceKey = $null
        lastDispositionOccurrenceKey = $null
        activeSnooze = $null
    })
$alignedV10Cursors.reminderStates | Add-Member -NotePropertyName 'multi-fixed' -NotePropertyValue ([pscustomobject]@{
        handledThroughUtc = '2099-01-02T01:30:00.0000000+00:00'
        lastDeliveredOccurrenceKey = $null
        lastDispositionOccurrenceKey = $null
        activeSnooze = $null
    })
$loadedAlignedV10Cursors = ConvertFrom-CountdownSettingsObject -Settings (($alignedV10Cursors | ConvertTo-Json -Depth 8) | ConvertFrom-Json)
Assert-Equal ([DateTimeOffset]'2099-01-01T00:00:00Z') ([DateTimeOffset] $loadedAlignedV10Cursors.ReminderStates['multi-absolute'].handledThroughUtc).ToUniversalTime() 'Schema v10 accepts an aligned absolute once cursor'
Assert-Equal ([DateTimeOffset]'2099-01-02T01:30:00Z') ([DateTimeOffset] $loadedAlignedV10Cursors.ReminderStates['multi-fixed'].handledThroughUtc).ToUniversalTime() 'Schema v10 accepts an aligned fixed interval cursor'

$disposedV10 = $multiRuleV10 | ConvertTo-Json -Depth 8 | ConvertFrom-Json
$disposedV10.reminderStates | Add-Member -NotePropertyName 'multi-absolute' -NotePropertyValue ([pscustomobject]@{
        handledThroughUtc = '2099-01-01T00:00:00.0000000+00:00'
        lastDeliveredOccurrenceKey = 'multi-absolute|2099-01-01T00:00:00.0000000+00:00'
        lastDispositionOccurrenceKey = 'multi-absolute|2099-01-01T00:00:00.0000000+00:00'
        activeSnooze = $null
    })
$loadedDisposedV10 = ConvertFrom-CountdownSettingsObject -Settings $disposedV10
$canonicalDisposedV10 = ConvertTo-CountdownSettingsJson -State $loadedDisposedV10 | ConvertFrom-Json
Assert-Equal 'multi-absolute|2099-01-01T00:00:00.0000000+00:00' ([string] $canonicalDisposedV10.reminderStates.'multi-absolute'.lastDispositionOccurrenceKey) 'Schema v10 round-trips disposition separately from delivery'
Assert-Equal $null $canonicalDisposedV10.reminderStates.'multi-absolute'.activeSnooze 'Schema v10 writes an explicit empty active snooze'

$legacyAcknowledgedV10 = $disposedV10 | ConvertTo-Json -Depth 8 | ConvertFrom-Json
$legacyDisposition = [string] $legacyAcknowledgedV10.reminderStates.'multi-absolute'.lastDispositionOccurrenceKey
$legacyAcknowledgedV10.reminderStates.'multi-absolute'.PSObject.Properties.Remove('lastDispositionOccurrenceKey')
$legacyAcknowledgedV10.reminderStates.'multi-absolute'.PSObject.Properties.Remove('activeSnooze')
$legacyAcknowledgedV10.reminderStates.'multi-absolute' | Add-Member -NotePropertyName 'lastAcknowledgedOccurrenceKey' -NotePropertyValue $legacyDisposition
$legacyAcknowledgedCanonical = ConvertTo-CountdownSettingsJson -State (
    ConvertFrom-CountdownSettingsObject -Settings $legacyAcknowledgedV10
) | ConvertFrom-Json
Assert-Equal $legacyDisposition ([string] $legacyAcknowledgedCanonical.reminderStates.'multi-absolute'.lastDispositionOccurrenceKey) 'Schema v10 accepts provisional acknowledgement input and writes canonical disposition'
Assert-Equal $null $legacyAcknowledgedCanonical.reminderStates.'multi-absolute'.activeSnooze 'Schema v10 canonicalizes provisional acknowledgement input with empty snooze state'
Assert-Equal $null $legacyAcknowledgedCanonical.reminderStates.'multi-absolute'.PSObject.Properties['lastAcknowledgedOccurrenceKey'] 'Schema v10 canonical output omits the provisional acknowledgement field'

$missingDispositionFieldRejected = $false
try {
    $invalid = $disposedV10 | ConvertTo-Json -Depth 8 | ConvertFrom-Json
    $invalid.reminderStates.'multi-absolute'.PSObject.Properties.Remove('lastDispositionOccurrenceKey')
    ConvertFrom-CountdownSettingsObject -Settings $invalid | Out-Null
}
catch { $missingDispositionFieldRejected = $true }
Assert-Equal $true $missingDispositionFieldRejected 'Schema v10 strictly requires the canonical disposition field'

foreach ($invalidDisposition in @(
        [pscustomobject]@{ Value = 'multi-fixed|2099-01-01T00:00:00.0000000+00:00'; Name = 'a different rule disposition' },
        [pscustomobject]@{ Value = 'multi-absolute|2099-01-01T08:00:00.0000000+08:00'; Name = 'a non-UTC disposition' }
    )) {
    $invalidDispositionRejected = $false
    try {
        $invalid = $disposedV10 | ConvertTo-Json -Depth 8 | ConvertFrom-Json
        $invalid.reminderStates.'multi-absolute'.lastDispositionOccurrenceKey = $invalidDisposition.Value
        ConvertFrom-CountdownSettingsObject -Settings $invalid | Out-Null
    }
    catch { $invalidDispositionRejected = $true }
    Assert-Equal $true $invalidDispositionRejected "Schema v10 rejects $($invalidDisposition.Name)"
}

$dispositionWithoutDeliveryRejected = $false
try {
    $invalid = $disposedV10 | ConvertTo-Json -Depth 8 | ConvertFrom-Json
    $invalid.reminderStates.'multi-absolute'.lastDeliveredOccurrenceKey = $null
    ConvertFrom-CountdownSettingsObject -Settings $invalid | Out-Null
}
catch { $dispositionWithoutDeliveryRejected = $true }
Assert-Equal $true $dispositionWithoutDeliveryRejected 'Schema v10 rejects disposition without a delivery fact'

$dispositionAfterDeliveryRejected = $false
try {
    $invalid = $multiRuleV10 | ConvertTo-Json -Depth 8 | ConvertFrom-Json
    $invalid.reminderStates | Add-Member -NotePropertyName 'multi-fixed' -NotePropertyValue ([pscustomobject]@{
            handledThroughUtc = '2099-01-02T01:30:00.0000000+00:00'
            lastDeliveredOccurrenceKey = 'multi-fixed|2099-01-02T01:00:00.0000000+00:00'
            lastDispositionOccurrenceKey = 'multi-fixed|2099-01-02T01:30:00.0000000+00:00'
            activeSnooze = $null
        })
    ConvertFrom-CountdownSettingsObject -Settings $invalid | Out-Null
}
catch { $dispositionAfterDeliveryRejected = $true }
Assert-Equal $true $dispositionAfterDeliveryRejected 'Schema v10 rejects disposition after the last delivered occurrence'

$snoozedV10 = $multiRuleV10 | ConvertTo-Json -Depth 8 | ConvertFrom-Json
$snoozedV10.reminderStates | Add-Member -NotePropertyName 'multi-fixed' -NotePropertyValue ([pscustomobject]@{
        handledThroughUtc = '2099-01-02T01:30:00.0000000+00:00'
        lastDeliveredOccurrenceKey = 'multi-fixed|2099-01-02T01:30:00.0000000+00:00'
        lastDispositionOccurrenceKey = 'multi-fixed|2099-01-02T00:45:00.0000000+00:00'
        activeSnooze = [pscustomobject]@{
            occurrenceKey = 'multi-fixed|2099-01-02T01:30:00.0000000+00:00'
            originalScheduledUtc = '2099-01-02T01:30:00.0000000+00:00'
            snoozeUntilUtc = '2099-01-02T02:00:00.0000000+00:00'
        }
    })
$canonicalSnoozedV10 = ConvertTo-CountdownSettingsJson -State (
    ConvertFrom-CountdownSettingsObject -Settings $snoozedV10
) | ConvertFrom-Json
Assert-Equal 'multi-fixed|2099-01-02T01:30:00.0000000+00:00' ([string] $canonicalSnoozedV10.reminderStates.'multi-fixed'.activeSnooze.occurrenceKey) 'Schema v10 round-trips an active snooze occurrence key'
Assert-Equal ([DateTimeOffset]'2099-01-02T01:30:00Z') ([DateTimeOffset] $canonicalSnoozedV10.reminderStates.'multi-fixed'.activeSnooze.originalScheduledUtc).ToUniversalTime() 'Schema v10 round-trips an active snooze original UTC'
Assert-Equal ([DateTimeOffset]'2099-01-02T02:00:00Z') ([DateTimeOffset] $canonicalSnoozedV10.reminderStates.'multi-fixed'.activeSnooze.snoozeUntilUtc).ToUniversalTime() 'Schema v10 round-trips an active snooze override UTC'

foreach ($invalidSnooze in @(
        [pscustomobject]@{ Name = 'a snooze key for a different rule'; Mutate = { param($state) $state.activeSnooze.occurrenceKey = 'multi-absolute|2099-01-02T01:30:00.0000000+00:00' } },
        [pscustomobject]@{ Name = 'a snooze key that differs from original schedule'; Mutate = { param($state) $state.activeSnooze.originalScheduledUtc = '2099-01-02T00:45:00.0000000+00:00' } },
        [pscustomobject]@{ Name = 'a snooze override not later than the original schedule'; Mutate = { param($state) $state.activeSnooze.snoozeUntilUtc = '2099-01-02T01:30:00.0000000+00:00' } },
        [pscustomobject]@{ Name = 'a snooze occurrence already disposed'; Mutate = { param($state) $state.lastDispositionOccurrenceKey = 'multi-fixed|2099-01-02T01:30:00.0000000+00:00' } },
        [pscustomobject]@{ Name = 'a snooze occurrence without matching delivery'; Mutate = { param($state) $state.lastDeliveredOccurrenceKey = 'multi-fixed|2099-01-02T00:45:00.0000000+00:00' } }
    )) {
    $invalidSnoozeRejected = $false
    try {
        $invalid = $snoozedV10 | ConvertTo-Json -Depth 8 | ConvertFrom-Json
        & $invalidSnooze.Mutate $invalid.reminderStates.'multi-fixed'
        ConvertFrom-CountdownSettingsObject -Settings $invalid | Out-Null
    }
    catch { $invalidSnoozeRejected = $true }
    Assert-Equal $true $invalidSnoozeRejected "Schema v10 rejects $($invalidSnooze.Name)"
}

$mismatchedV10OccurrenceRejected = $false
try {
    $invalid = $multiRuleV10 | ConvertTo-Json -Depth 8 | ConvertFrom-Json
    $invalid.reminderStates | Add-Member -NotePropertyName 'multi-absolute' -NotePropertyValue ([pscustomobject]@{
            handledThroughUtc = '2099-01-01T00:00:00.0000000+00:00'
            lastDeliveredOccurrenceKey = 'multi-fixed|2099-01-01T00:00:00.0000000+00:00'
            lastDispositionOccurrenceKey = $null
            activeSnooze = $null
        })
    ConvertFrom-CountdownSettingsObject -Settings $invalid | Out-Null
}
catch { $mismatchedV10OccurrenceRejected = $true }
Assert-Equal $true $mismatchedV10OccurrenceRejected 'Schema v10 rejects an occurrence key for a different reminder rule'

$nonUtcV10OccurrenceRejected = $false
try {
    $invalid = $multiRuleV10 | ConvertTo-Json -Depth 8 | ConvertFrom-Json
    $invalid.reminderStates | Add-Member -NotePropertyName 'multi-absolute' -NotePropertyValue ([pscustomobject]@{
            handledThroughUtc = '2099-01-01T00:00:00.0000000+00:00'
            lastDeliveredOccurrenceKey = 'multi-absolute|2099-01-01T08:00:00.0000000+08:00'
            lastDispositionOccurrenceKey = $null
            activeSnooze = $null
        })
    ConvertFrom-CountdownSettingsObject -Settings $invalid | Out-Null
}
catch { $nonUtcV10OccurrenceRejected = $true }
Assert-Equal $true $nonUtcV10OccurrenceRejected 'Schema v10 rejects a non-UTC occurrence key even when it names the same instant'

foreach ($nonIntegerJson in @('1.0', '1e0')) {
    $nonIntegerV10IntervalRejected = $false
    try {
        $invalidJson = ($multiRuleV10 | ConvertTo-Json -Depth 8).Replace('"intervalMinutes": 45', "`"intervalMinutes`": $nonIntegerJson")
        ConvertFrom-CountdownSettingsObject -Settings ($invalidJson | ConvertFrom-Json) | Out-Null
    }
    catch { $nonIntegerV10IntervalRejected = $true }
    Assert-Equal $true $nonIntegerV10IntervalRejected "Schema v10 rejects non-integer JSON intervalMinutes $nonIntegerJson"
}

foreach ($integerInterval in @([int] 45, [long] 45)) {
    $validIntegerInterval = $multiRuleV10 | ConvertTo-Json -Depth 8 | ConvertFrom-Json
    $validIntegerInterval.notes[0].reminderRules[1].intervalMinutes = $integerInterval
    $loadedIntegerInterval = ConvertFrom-CountdownSettingsObject -Settings $validIntegerInterval
    Assert-Equal 45 $loadedIntegerInterval.Notes[0].reminderRules[1].intervalMinutes "Schema v10 accepts $($integerInterval.GetType().Name) intervalMinutes"
}

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
