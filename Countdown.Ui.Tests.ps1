#requires -Version 7.0

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationFramework
Import-Module (Join-Path $PSScriptRoot 'Countdown.Core.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'Countdown.Runtime.psm1') -Force

$powerShellEngine = (Get-Command pwsh.exe -CommandType Application -ErrorAction Stop | Select-Object -First 1).Source
if ([string]::IsNullOrWhiteSpace($powerShellEngine)) {
    throw '未找到 pwsh.exe；本项目要求 PowerShell 7+，不支持 Windows PowerShell 5.1。'
}

$picker = New-Object System.Windows.Controls.DatePicker
$picker.SelectedDate = [DateTime]'2026-08-22'
$actual = ConvertTo-CountdownDateText -SelectedDate $picker.SelectedDate

if ($actual -ne '2026-08-22') {
    throw "DatePicker selection formatting failed. Expected 2026-08-22, got $actual"
}

$validationJson = & $powerShellEngine `
    -NoLogo -NoProfile -NonInteractive -STA -File (Join-Path $PSScriptRoot '敏敏子倒计时.ps1') -ValidateOnly | Out-String
if ($LASTEXITCODE -ne 0) {
    throw "Application validation failed: $validationJson"
}
$validation = $validationJson | ConvertFrom-Json

$applicationHostPath = Join-Path $PSScriptRoot 'Countdown.App.psm1'
$applicationHostCommand = "Import-Module -Force -Name '$($applicationHostPath.Replace("'", "''"))'; Start-CountdownApplication -ValidateOnly"
$applicationHostValidationJson = & $powerShellEngine `
    -NoLogo -NoProfile -NonInteractive -STA -Command $applicationHostCommand | Out-String
if ($LASTEXITCODE -ne 0) {
    throw "Application-host validation failed: $applicationHostValidationJson"
}
$applicationHostValidation = $applicationHostValidationJson | ConvertFrom-Json
if (-not $applicationHostValidation.Valid) { throw 'Application host did not preserve the ValidateOnly contract.' }

$runtimeIntegrationEvidenceRoot = 'D:\Codex\.codex\run-evidence\clock-countdown-ui-tests'
$runtimeIntegrationRoot = Join-Path $runtimeIntegrationEvidenceRoot ("ui-runtime-integration-" + [guid]::NewGuid().ToString('N'))
$runtimeIntegrationProcess = $null
try {
    New-Item -ItemType Directory -Path $runtimeIntegrationRoot -Force | Out-Null
    foreach ($file in @('Countdown.App.psm1', 'Countdown.Core.psm1', 'Countdown.Notes.psm1', 'Countdown.Tags.psm1', 'Countdown.Runtime.psm1', 'Countdown.Settings.psm1', 'countdown-icon.ico')) {
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot $file) -Destination (Join-Path $runtimeIntegrationRoot $file) -Force
    }
    $runtimeIntegrationHostPath = Join-Path $runtimeIntegrationRoot 'Countdown.App.psm1'
    $runtimeIntegrationEntry = Join-Path $runtimeIntegrationRoot 'runtime-harness.ps1'
    $runtimeIntegrationRequestPath = Join-Path $runtimeIntegrationRoot 'startup-request.json'
    $runtimeIntegrationStatusPath = Join-Path $runtimeIntegrationRoot 'runtime-status.json'
    $runtimeIntegrationMarkerPath = Join-Path $runtimeIntegrationRoot 'calendar-bootstrap-started.txt'
    $runtimeIntegrationHostLiteral = $runtimeIntegrationHostPath.Replace("'", "''")
    $runtimeIntegrationMarkerLiteral = $runtimeIntegrationMarkerPath.Replace("'", "''")
    $runtimeIntegrationHarness = @"
`$ErrorActionPreference = 'Stop'
trap {
    [Console]::Error.WriteLine("HARNESS_ERROR: `$(`$_.Exception.Message)")
    [Console]::Error.WriteLine(`$_.ScriptStackTrace)
    exit 1
}
Import-Module -Name '$runtimeIntegrationHostLiteral' -Force
`$calendarTaskFactory = {
    param([uri] `$Uri)
    [System.IO.File]::WriteAllText('$runtimeIntegrationMarkerLiteral', 'started', [System.Text.UTF8Encoding]::new(`$false))
    [System.Threading.Tasks.Task]::Delay(10000)
}
Start-CountdownApplication -ExitAfterReadyForTest -ApplicationPath `$PSCommandPath -HolidayCalendarTaskFactory `$calendarTaskFactory
"@
    [System.IO.File]::WriteAllText($runtimeIntegrationEntry, $runtimeIntegrationHarness, (New-Object System.Text.UTF8Encoding($false)))
    $runtimeIntegrationRequest = New-CountdownStartupRequest -Path $runtimeIntegrationRequestPath
    $runtimeIntegrationOutputPath = Join-Path $runtimeIntegrationRoot 'app.stdout.txt'
    $runtimeIntegrationErrorPath = Join-Path $runtimeIntegrationRoot 'app.stderr.txt'
    $runtimeIntegrationArguments = @('-NoLogo', '-NoProfile', '-NonInteractive', '-STA', '-File', ('"{0}"' -f $runtimeIntegrationEntry))
    $runtimeIntegrationProcess = Start-Process -FilePath $powerShellEngine -ArgumentList $runtimeIntegrationArguments -PassThru -RedirectStandardOutput $runtimeIntegrationOutputPath -RedirectStandardError $runtimeIntegrationErrorPath
    $runtimeIntegrationReceipt = Wait-CountdownWindowReady -Path $runtimeIntegrationStatusPath -LaunchToken $runtimeIntegrationRequest.launchToken -ExpectedSessionId ([System.Diagnostics.Process]::GetCurrentProcess().SessionId) -ExpectedApplicationPath $runtimeIntegrationEntry -TimeoutSeconds 20 -PollMilliseconds 100
    if ($runtimeIntegrationReceipt.stage -ne 'window-ready') { throw 'Real WPF launch did not emit a window-ready receipt.' }
    if ($runtimeIntegrationReceipt.calendarState -ne 'Pending') { throw 'Real WPF launch should become ready before the holiday calendar finishes.' }
    $calendarBootstrapDeadline = [DateTimeOffset]::UtcNow.AddSeconds(5)
    while (-not (Test-Path -LiteralPath $runtimeIntegrationMarkerPath -PathType Leaf) -and [DateTimeOffset]::UtcNow -lt $calendarBootstrapDeadline) {
        Start-Sleep -Milliseconds 50
    }
    if (-not (Test-Path -LiteralPath $runtimeIntegrationMarkerPath -PathType Leaf)) {
        $runtimeError = if (Test-Path -LiteralPath $runtimeIntegrationErrorPath) { Get-Content -LiteralPath $runtimeIntegrationErrorPath -Raw } else { '' }
        throw "Real WPF launch did not start the asynchronous holiday-calendar bootstrap: $runtimeError"
    }
    if (-not $runtimeIntegrationProcess.WaitForExit(35000)) { throw 'Real WPF launch did not close after the receipt integration test.' }
    if ($runtimeIntegrationProcess.ExitCode -ne 0) {
        $runtimeError = if (Test-Path -LiteralPath $runtimeIntegrationErrorPath) { Get-Content -LiteralPath $runtimeIntegrationErrorPath -Raw } else { '' }
        throw "Real WPF launch exited with $($runtimeIntegrationProcess.ExitCode): $runtimeError"
    }
}
finally {
    if ($null -ne $runtimeIntegrationProcess -and -not $runtimeIntegrationProcess.HasExited) {
        Stop-Process -Id $runtimeIntegrationProcess.Id -Force -ErrorAction Stop
        if (-not $runtimeIntegrationProcess.WaitForExit(5000)) { throw 'Runtime integration child process did not exit during cleanup.' }
    }
    if (Test-Path -LiteralPath $runtimeIntegrationRoot) {
        Remove-Item -LiteralPath $runtimeIntegrationRoot -Recurse -Force -ErrorAction Stop
    }
}

if (-not $validation.DateInputReadOnly) { throw 'Date selection still accepts free-text input.' }
if (-not $validation.DatePickerButtonOpensCalendar) { throw 'Date picker calendar closes immediately after the drop-down button is clicked.' }
if ($validation.TimeDropDowns -ne 3) { throw "Expected three time drop-downs, got $($validation.TimeDropDowns)." }
if (-not $validation.HasSetNowButton) { throw 'Set-now action is missing.' }
if (-not $validation.HasCountdownList) { throw 'Multiple-countdown list is missing.' }
if (-not $validation.HasNameEditor) { throw 'Countdown name editor is missing.' }
if (-not $validation.FloatingWindowPresent) { throw 'Desktop floating countdown window is missing.' }
if (-not $validation.FloatingCountdownButtonTextFits) {
    throw "Floating countdown switch label is clipped: available=$($validation.FloatingCountdownButtonTextAvailableWidth), required=$($validation.FloatingCountdownButtonTextRequiredWidth)."
}
if (-not $validation.FloatingNotesButtonTextFits) {
    throw "Floating notes switch label is clipped: available=$($validation.FloatingNotesButtonTextAvailableWidth), required=$($validation.FloatingNotesButtonTextRequiredWidth)."
}
if (-not $validation.FloatingManagerButtonPresent) { throw 'Floating countdown manager action is missing.' }
if (-not $validation.HasPreferenceControls) { throw 'Floating precision or income settings controls are missing.' }
if (-not $validation.HasUnifiedNoteEntry) { throw 'Unified sticky-note entry is missing from the countdown workspace.' }
if (-not $validation.HasNoteControls) { throw 'Sticky-note input or add action is missing.' }
if (-not $validation.HasOpenNotesActions) { throw 'No action opens the unified sticky-note workspace.' }
if (-not $validation.UnifiedWorkspaceSwitches) { throw 'Countdown and sticky-note workspaces do not switch together.' }
if (-not $validation.WorkspaceSwitcherBeforeEditors) { throw 'Countdown and sticky-note entry switcher must appear before the editors.' }
if (@($validation.TimeOptionCounts) -join ',' -ne '24,60,60') { throw "Unexpected time option counts: $(@($validation.TimeOptionCounts) -join ',')." }
if (-not $validation.LegacyMigrationSupported) { throw 'Legacy single-countdown settings are not migrated.' }
if (-not $validation.DuplicateSettingsRejected) { throw 'Duplicate persisted countdown names are not rejected.' }
if (-not $validation.InvalidNotesRejected) { throw 'Invalid persisted sticky notes are not rejected.' }
if (-not $validation.InvalidTagReferencesRejected) { throw 'Persisted dangling tag references are not rejected.' }
if (-not $validation.HasTagControls) { throw 'Shared tag selection, filter, and management controls are missing.' }
if (-not $validation.StaticWorkspaceLayout) { throw 'Countdown editor and settings no longer preserve the established always-visible layout.' }
if (-not $validation.TransientPanelsOnDemand) { throw 'Filters, tag pickers, or tag management do not use the required on-demand interaction flow.' }
if (-not $validation.WorkspaceDraftsRetained) { throw 'Switching workspaces discarded an unsaved countdown or sticky-note draft.' }
if (-not $validation.HiddenCountdownSubmitDisabled) { throw 'A hidden countdown editor still exposes its default submit button.' }
if (-not $validation.WorkspaceTabsUseSelectionSemantics) { throw 'Workspace tabs do not expose an accessible selected state.' }
if (-not $validation.TagUiAccessible) { throw 'Tag search labels or icon-button accessible names are missing.' }
if (-not $validation.FilterOptionsUseSelectionSemantics) { throw 'Filter options do not expose an accessible selected state.' }
if (-not $validation.CompactFilterSummary) { throw 'The filter trigger does not use the compact summary layout.' }
if (-not $validation.TagCreateAppearsFromSearch) { throw 'A new tag cannot be created from the unified tag search field.' }
if (-not $validation.TagCreateStaysCollapsedWithoutQuery) { throw 'The tag-creation action is visible before a new tag name is entered.' }
if (-not $validation.TagFilterSupportsAnyAll) { throw 'Tag filtering does not expose explicit any/all matching.' }
if (-not $validation.TagChipsUseUid) { throw 'Tag chips must not reuse the record-ID Tag property.' }
if (-not $validation.ThemeControlInventoryCovered) { throw 'The theme inventory does not cover every visible control family.' }
if (-not $validation.ThemedControlTemplatesApplied) { throw 'One or more primary or floating controls did not receive the shared theme template.' }
if (-not $validation.ComboBoxesUseThemedDropDowns) { throw 'A ComboBox drop-down, option, or scroll bar is still using an unthemed surface.' }
if (-not $validation.DatePickerCalendarIsThemed) { throw 'The DatePicker text box, trigger, or calendar family is not fully themed.' }
if (-not $validation.PopupSurfacesUseThemeTokens) { throw 'A popup surface is missing the shared themed background, border, or motion treatment.' }
if (-not $validation.NoDefaultSystemSurfaces) { throw 'A system window frame or MessageBox remains in the application flow.' }
if (-not $validation.NoRawFloatingThemeBypass) { throw 'The floating window bypasses shared theme tokens with raw colors.' }
if (-not $validation.ThemedInteractiveStates) { throw 'A themed interactive control is missing visual state feedback.' }
if (-not $validation.ThemeTextContrast) { throw 'The warm theme does not meet the required text-contrast pairs.' }

$testSettingsPath = Join-Path $PSScriptRoot 'ui-test.settings.json'
$noteAddSettingsPath = Join-Path $PSScriptRoot 'ui-note-add-test.settings.json'
$tagInteractionSettingsPath = Join-Path $PSScriptRoot 'ui-tag-interaction.settings.json'
$persistenceFailureSettingsPath = Join-Path $PSScriptRoot 'ui-persistence-failure.settings.json'
$persistenceFailureUnblockedSettingsPath = Join-Path $PSScriptRoot 'ui-persistence-failure-unblocked.settings.json'
$legacyV3SettingsPath = Join-Path $PSScriptRoot 'ui-test-v3.settings.json'
try {
    [System.IO.File]::WriteAllText($testSettingsPath, (@{
        schemaVersion = 6
        timeZoneId = 'China Standard Time'
        displayPrecision = 'Second'
        monthlySalary = 23000
        workStart = '09:00'
        workEnd = '18:00'
        tags = @(
            @{ id = 'seed-tag'; name = '种子标签' }
        )
        countdowns = @(
            @{ id = 'expired'; name = '已到期项目'; targetUtc = '2000-01-01T00:00:00Z'; tagIds = @() }
            @{ id = 'future'; name = '未来项目'; targetUtc = '2099-01-01T00:00:00Z'; tagIds = @('seed-tag') }
        )
        notes = @(
            @{ id = 'note-active'; text = '整理本周安排'; date = '2026-07-18'; isCompleted = $false; tagIds = @('seed-tag') }
            @{ id = 'note-completed'; text = '确认已完成事项'; date = $null; isCompleted = $true; tagIds = @() }
        )
    } | ConvertTo-Json -Depth 5), (New-Object System.Text.UTF8Encoding($false)))
    $settingsHashBeforeValidation = (Get-FileHash -Algorithm SHA256 -LiteralPath $testSettingsPath).Hash
    $listValidationJson = & $powerShellEngine `
        -NoLogo -NoProfile -NonInteractive -STA -File (Join-Path $PSScriptRoot '敏敏子倒计时.ps1') `
        -ValidateOnly -SettingsFile $testSettingsPath | Out-String
    if ($LASTEXITCODE -ne 0) { throw "Countdown-list validation failed: $listValidationJson" }
    $listValidation = $listValidationJson | ConvertFrom-Json
    if ($listValidation.LoadedCountdowns -ne 2) { throw "Expected two loaded countdowns, got $($listValidation.LoadedCountdowns)." }
    if (@($listValidation.SortedNames) -join ',' -ne '未来项目,已到期项目') { throw "Unexpected countdown order: $(@($listValidation.SortedNames) -join ',')." }
    if (-not $listValidation.PersistenceRoundTrip) { throw 'Countdown persistence round-trip failed.' }
    if ($listValidation.LoadedTags -ne 1) { throw "Expected one loaded shared tag, got $($listValidation.LoadedTags)." }
    if (-not $listValidation.TagPersistenceRoundTrip) { throw 'Shared tag persistence round-trip failed.' }
    if ($listValidation.LoadedNotes -ne 2) { throw "Expected two loaded sticky notes, got $($listValidation.LoadedNotes)." }
    if ($listValidation.ActiveNoteCount -ne 1 -or $listValidation.CompletedNoteCount -ne 1) { throw "Unexpected sticky-note grouping: active=$($listValidation.ActiveNoteCount), completed=$($listValidation.CompletedNoteCount)." }
    if (-not $listValidation.NotePersistenceRoundTrip) { throw 'Sticky-note persistence round-trip failed.' }
    if (-not $listValidation.NoteActionsBoundByTag) { throw 'Sticky-note actions are not bound to their note ID.' }
    if (-not $listValidation.NoteStatusLabelsPresent) { throw 'Sticky-note status labels are missing from rendered rows.' }
    if (-not $listValidation.HasNoteDateControls) { throw 'The manager or floating sticky-note date picker is missing.' }
    if (-not $listValidation.NoteDatesRendered) { throw 'Sticky-note calendar dates are missing from rendered rows.' }
    if (-not $listValidation.NoteDateDefaultsTrackBeijingDay) { throw 'Default sticky-note dates did not advance at Beijing midnight.' }
    if (-not $listValidation.NoteDateChoicesSurviveBeijingDayChange) { throw 'A selected sticky-note date or in-progress draft was overwritten at Beijing midnight.' }
    if (-not $listValidation.CompletedNotesCollapsed) { throw 'Completed sticky notes should be collapsed by default.' }
    $settingsHashAfterValidation = (Get-FileHash -Algorithm SHA256 -LiteralPath $testSettingsPath).Hash
    if ($settingsHashAfterValidation -ne $settingsHashBeforeValidation) { throw 'ValidateOnly modified the supplied settings file.' }
    if (-not $listValidation.CardActionsBoundByTag) { throw 'Card edit/delete actions are not bound to their countdown ID.' }
    if ($listValidation.FloatingCountdownCount -ne 1) { throw "Expected one active floating countdown card, got $($listValidation.FloatingCountdownCount)." }
    if ((@($listValidation.FloatingCountdownTexts).Count -ne 1) -or $listValidation.FloatingCountdownTexts[0] -notmatch '天 .*小时 .*分钟 .*秒') { throw "Floating countdowns did not use the unified unit format: $(@($listValidation.FloatingCountdownTexts) -join ',')" }
    if ($listValidation.FloatingIncomeText -notmatch '今日已赚 ￥|节假日日历未就绪') { throw "Floating income text is missing: $($listValidation.FloatingIncomeText)" }

    Copy-Item -LiteralPath $testSettingsPath -Destination $noteAddSettingsPath -Force
    $noteAddValidationJson = & $powerShellEngine `
        -NoLogo -NoProfile -NonInteractive -STA -File (Join-Path $PSScriptRoot '敏敏子倒计时.ps1') `
        -ValidateOnly -ExerciseNoteAdd -SettingsFile $noteAddSettingsPath | Out-String
    if ($LASTEXITCODE -ne 0) { throw "Sticky-note add click validation failed: $noteAddValidationJson" }
    $noteAddValidation = $noteAddValidationJson | ConvertFrom-Json
    if (-not $noteAddValidation.NoteDateRequired) { throw 'The sticky-note add flow accepted a missing calendar date or discarded the draft.' }
    if (-not $noteAddValidation.NoteAddViaClick) { throw 'Clicking Add did not create an active sticky note and retain the application validation flow.' }
    if (-not $noteAddValidation.NoteDateViaPicker) { throw 'Clicking Add did not preserve the selected sticky-note calendar date.' }
    if (-not $noteAddValidation.NoteStatusViaCheckbox) { throw 'Sticky-note completion status did not change through its checkbox.' }
    $noteAddSettings = [System.IO.File]::ReadAllText($noteAddSettingsPath, [System.Text.Encoding]::UTF8) | ConvertFrom-Json
    if (@($noteAddSettings.notes | Where-Object { $_.text -eq '验证点击添加便签' -and $_.date -eq '2031-04-05' -and -not $_.isCompleted }).Count -ne 1) { throw 'Clicking Add did not persist the active sticky note with its selected calendar date.' }

    Copy-Item -LiteralPath $testSettingsPath -Destination $tagInteractionSettingsPath -Force
    $tagInteractionValidationJson = & $powerShellEngine `
        -NoLogo -NoProfile -NonInteractive -STA -File (Join-Path $PSScriptRoot '敏敏子倒计时.ps1') `
        -ValidateOnly -ExerciseTagInteractions -SettingsFile $tagInteractionSettingsPath | Out-String
    if ($LASTEXITCODE -ne 0) { throw "Tag interaction validation failed: $tagInteractionValidationJson" }
    $tagInteractionValidation = $tagInteractionValidationJson | ConvertFrom-Json
if (-not $tagInteractionValidation.TagInteractionsExecuted) { throw 'Tag interaction exercise did not run.' }
if (-not $tagInteractionValidation.TagCreationAndAssociation) { throw 'Creating shared tags and associating countdowns and notes failed.' }
if (-not $tagInteractionValidation.TagAnyAllFilter) { throw 'Any/all tag filtering did not distinguish the expected countdowns.' }
if (-not $tagInteractionValidation.TagEditorSummaryIsCompact) { throw 'Long selected tag names are not condensed into a readable editor summary.' }
if (-not $tagInteractionValidation.TagSearchCreateAvoidsDefaultSubmit) { throw 'Creating a tag from the search field triggered an unrelated default submit.' }
if (-not $tagInteractionValidation.TagUntaggedAndCountdownStatus) { throw 'Untagged or countdown-status filtering failed.' }
    if (-not $tagInteractionValidation.TagNoteStatusSearchAndCompletion) { throw 'Note text search, status filtering, or completion tag retention failed.' }
    if (-not $tagInteractionValidation.TagFloatingNoteInheritsTags) { throw 'Floating note creation did not inherit the current selected tag.' }
    if (-not $tagInteractionValidation.TagFloatingNoteDateViaPicker) { throw 'Floating note creation did not preserve its selected calendar date.' }
    if (-not $tagInteractionValidation.TagFloatingNotePopupOnDemand) { throw 'Floating note tags were not hidden until the compact selector was opened.' }
    if (-not $tagInteractionValidation.TagFloatingNoteCanOverrideTags) { throw 'Floating note creation could not override or clear its inherited tags.' }
    if (-not $tagInteractionValidation.TagNoteEditingPersistsTags) { throw 'Editing a sticky note did not persist its text and selected tags.' }
    if (-not $tagInteractionValidation.TagNoteEditingPersistsDate) { throw 'Editing a sticky note did not persist its selected calendar date.' }
    if (-not $tagInteractionValidation.TagRenameAndDeleteDetachOnly) { throw 'Tag rename or delete-detach behavior failed.' }
    $tagInteractionSettings = [System.IO.File]::ReadAllText($tagInteractionSettingsPath, [System.Text.Encoding]::UTF8) | ConvertFrom-Json
    if ($tagInteractionSettings.schemaVersion -ne 6) { throw 'Tag interaction exercise did not preserve schema v6.' }
    if (@($tagInteractionSettings.tags).Count -lt 2) { throw 'Tag interaction exercise did not preserve the shared tag library after deletion.' }
    if (@($tagInteractionSettings.countdowns).Count -lt 5 -or @($tagInteractionSettings.notes).Count -lt 4) { throw 'Tag interaction exercise did not preserve the records while managing tags.' }

    Copy-Item -LiteralPath $testSettingsPath -Destination $persistenceFailureUnblockedSettingsPath -Force
    $persistenceFailureUnblockedHashBeforeValidation = (Get-FileHash -Algorithm SHA256 -LiteralPath $persistenceFailureUnblockedSettingsPath).Hash
    $persistenceFailureUnblockedValidationOutput = & $powerShellEngine `
        -NoLogo -NoProfile -NonInteractive -STA -File (Join-Path $PSScriptRoot '敏敏子倒计时.ps1') `
        -ValidateOnly -ExercisePersistenceFailures -SettingsFile $persistenceFailureUnblockedSettingsPath 2>&1 | Out-String
    if ($LASTEXITCODE -eq 0) { throw 'Persistence-failure validation must reject an unblocked temporary path.' }
    $persistenceFailureUnblockedHashAfterValidation = (Get-FileHash -Algorithm SHA256 -LiteralPath $persistenceFailureUnblockedSettingsPath).Hash
    if ($persistenceFailureUnblockedHashAfterValidation -ne $persistenceFailureUnblockedHashBeforeValidation) { throw 'Persistence-failure validation changed the supplied settings file without a blocked temporary path.' }

    Copy-Item -LiteralPath $testSettingsPath -Destination $persistenceFailureSettingsPath -Force
    New-Item -ItemType Directory -Path "$persistenceFailureSettingsPath.tmp" -Force | Out-Null
    $persistenceFailureHashBeforeValidation = (Get-FileHash -Algorithm SHA256 -LiteralPath $persistenceFailureSettingsPath).Hash
    $persistenceFailureValidationJson = & $powerShellEngine `
        -NoLogo -NoProfile -NonInteractive -STA -File (Join-Path $PSScriptRoot '敏敏子倒计时.ps1') `
        -ValidateOnly -ExercisePersistenceFailures -SettingsFile $persistenceFailureSettingsPath | Out-String
    if ($LASTEXITCODE -ne 0) { throw "Persistence-failure validation failed: $persistenceFailureValidationJson" }
    $persistenceFailureValidation = $persistenceFailureValidationJson | ConvertFrom-Json
    if (-not $persistenceFailureValidation.CountdownSaveFailurePreservesState) { throw 'Countdown save committed state after persistence failed.' }
    if (-not $persistenceFailureValidation.PreferenceSaveFailurePreservesState) { throw 'Preference save committed state after persistence failed.' }
    if (-not $persistenceFailureValidation.CountdownSaveFailureRetainsDraft) { throw 'Countdown save failure did not retain a retryable draft.' }
    if (-not $persistenceFailureValidation.PreferenceSaveFailureRetainsDraft) { throw 'Preference save failure did not retain a retryable draft.' }
    if (-not $persistenceFailureValidation.TagSaveFailurePreservesState) { throw 'Tag create, rename, or delete committed state after persistence failed.' }
    if (-not $persistenceFailureValidation.TagSaveFailureRetainsDraft) { throw 'Tag save failure did not retain retryable tag-name drafts.' }
    if (-not $persistenceFailureValidation.TagSaveFailureRetainsFilter) { throw 'Tag save failure changed the current filter selection.' }
    if (-not $persistenceFailureValidation.PersistenceFailureTempPathRemainsDirectory) { throw 'Persistence failure fixture did not remain a temporary directory.' }
    if (-not (Test-Path -LiteralPath "$persistenceFailureSettingsPath.tmp" -PathType Container)) { throw 'Persistence failure test did not preserve the blocked temporary path.' }
    $persistenceFailureHashAfterValidation = (Get-FileHash -Algorithm SHA256 -LiteralPath $persistenceFailureSettingsPath).Hash
    if ($persistenceFailureHashAfterValidation -ne $persistenceFailureHashBeforeValidation) { throw 'Persistence failure changed the supplied settings file.' }

    [System.IO.File]::WriteAllText($legacyV3SettingsPath, (@{
        schemaVersion = 3
        timeZoneId = 'China Standard Time'
        displayPrecision = 'Minute'
        monthlySalary = 12345
        workStart = '08:30'
        workEnd = '17:30'
        countdowns = @(
            @{ id = 'legacy-v3'; name = '旧版倒计时'; targetUtc = '2099-01-01T00:00:00Z' }
        )
    } | ConvertTo-Json -Depth 4), (New-Object System.Text.UTF8Encoding($false)))
    $legacyV3HashBeforeValidation = (Get-FileHash -Algorithm SHA256 -LiteralPath $legacyV3SettingsPath).Hash
    $legacyV3ValidationJson = & $powerShellEngine `
        -NoLogo -NoProfile -NonInteractive -STA -File (Join-Path $PSScriptRoot '敏敏子倒计时.ps1') `
        -ValidateOnly -SettingsFile $legacyV3SettingsPath | Out-String
    if ($LASTEXITCODE -ne 0) { throw "Schema-v3 migration validation failed: $legacyV3ValidationJson" }
    $legacyV3Validation = $legacyV3ValidationJson | ConvertFrom-Json
    if (-not $legacyV3Validation.SettingsWereMigrated) { throw 'Schema-v3 settings were not marked for migration.' }
    if ($legacyV3Validation.LoadedCountdowns -ne 1 -or (@($legacyV3Validation.SortedNames) -join ',') -ne '旧版倒计时') { throw 'Schema-v3 countdowns were not preserved.' }
    if ($legacyV3Validation.LoadedNotes -ne 0) { throw 'Schema-v3 migration should initialize an empty sticky-note list.' }
    if (-not $legacyV3Validation.PersistenceRoundTrip -or -not $legacyV3Validation.NotePersistenceRoundTrip -or -not $legacyV3Validation.TagPersistenceRoundTrip) { throw 'Schema-v3 settings did not serialize as schema-v6.' }
    if ($legacyV3Validation.LoadedDisplayPrecision -ne 'Minute' -or ([decimal] $legacyV3Validation.LoadedMonthlySalary) -ne [decimal]12345 -or $legacyV3Validation.LoadedWorkStart -ne '08:30' -or $legacyV3Validation.LoadedWorkEnd -ne '17:30') { throw 'Schema-v3 preferences were not preserved.' }
    $legacyV3HashAfterValidation = (Get-FileHash -Algorithm SHA256 -LiteralPath $legacyV3SettingsPath).Hash
    if ($legacyV3HashAfterValidation -ne $legacyV3HashBeforeValidation) { throw 'ValidateOnly modified the supplied schema-v3 settings file.' }
}
finally {
    Remove-Item -LiteralPath $testSettingsPath -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath "$testSettingsPath.tmp" -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $noteAddSettingsPath -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath "$noteAddSettingsPath.tmp" -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $tagInteractionSettingsPath -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath "$tagInteractionSettingsPath.tmp" -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $persistenceFailureSettingsPath -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath "$persistenceFailureSettingsPath.tmp" -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $persistenceFailureUnblockedSettingsPath -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath "$persistenceFailureUnblockedSettingsPath.tmp" -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $legacyV3SettingsPath -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath "$legacyV3SettingsPath.tmp" -Force -ErrorAction SilentlyContinue
}

[pscustomobject]@{
    Passed = 92
    Failed = 0
    SelectedDateType = $picker.SelectedDate.GetType().FullName
    ApplicationValidation = $validation.Valid
} | ConvertTo-Json
