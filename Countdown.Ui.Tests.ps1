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

$diagnosticLogPath = Join-Path $PSScriptRoot 'ui-diagnostic-test\application.jsonl'
foreach ($diagnosticPath in @($diagnosticLogPath, "$diagnosticLogPath.1", "$diagnosticLogPath.2", "$diagnosticLogPath.3")) {
    Remove-Item -LiteralPath $diagnosticPath -Force -ErrorAction SilentlyContinue
}
$fatalDiagnosticOutput = & $powerShellEngine `
    -NoLogo -NoProfile -NonInteractive -STA -File (Join-Path $PSScriptRoot '敏敏子倒计时.ps1') `
    -ValidateOnly -ExerciseFatalDiagnostic -DiagnosticLogPath $diagnosticLogPath 2>&1 | Out-String
if ($LASTEXITCODE -eq 0) { throw 'Fatal diagnostic exercise unexpectedly succeeded.' }
if (-not (Test-Path -LiteralPath $diagnosticLogPath -PathType Leaf)) {
    throw "Fatal diagnostic exercise did not create a log: $fatalDiagnosticOutput"
}
$fatalDiagnosticEntries = @(Get-Content -LiteralPath $diagnosticLogPath -Encoding UTF8 | ForEach-Object { $_ | ConvertFrom-Json })
if (@($fatalDiagnosticEntries | Where-Object { $_.event -eq 'ProcessStarted' }).Count -ne 1) { throw 'Diagnostic lifecycle log is missing ProcessStarted.' }
$fatalDiagnosticEntry = @($fatalDiagnosticEntries | Where-Object { $_.event -eq 'FatalUnhandledException' })[-1]
if ($null -eq $fatalDiagnosticEntry -or $fatalDiagnosticEntry.exceptionType -ne 'System.InvalidOperationException') {
    throw "Fatal diagnostic log is missing the unhandled exception: $fatalDiagnosticOutput"
}

$validationJson = & $powerShellEngine `
    -NoLogo -NoProfile -NonInteractive -STA -File (Join-Path $PSScriptRoot '敏敏子倒计时.ps1') -ValidateOnly | Out-String
if ($LASTEXITCODE -ne 0) {
    throw "Application validation failed: $validationJson"
}
$validation = $validationJson | ConvertFrom-Json
if (-not $validation.DatePickerTextDefersCalendarUntilPointerRelease) { throw 'Clicking the date text opens the calendar before the pointer is released.' }

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
$floatingPlushAssetFiles = @(
    'floating-plush-mymelody-front.png',
    'floating-plush-mymelody-pose.png',
    'floating-plush-cinnamoroll-front.png',
    'floating-plush-cinnamoroll-pose.png',
    'floating-plush-kuromi-front.png',
    'floating-plush-kuromi-pose.png',
    'floating-plush-mymelody-reference.png',
    'floating-plush-cinnamoroll-reference.png',
    'floating-plush-kuromi-reference.png'
)
try {
    New-Item -ItemType Directory -Path $runtimeIntegrationRoot -Force | Out-Null
    foreach ($file in @('Countdown.App.psm1', 'Countdown.Core.psm1', 'Countdown.Notes.psm1', 'Countdown.Tags.psm1', 'Countdown.Runtime.psm1', 'Countdown.Settings.psm1', 'countdown-icon.ico', 'sanrio-official-mymelody.png', 'sanrio-official-cinnamoroll.png', 'sanrio-official-kuromi.png')) {
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot $file) -Destination (Join-Path $runtimeIntegrationRoot $file) -Force
    }
    $runtimeFloatingPlushRoot = Join-Path $runtimeIntegrationRoot 'assets\floating-plush'
    New-Item -ItemType Directory -Path $runtimeFloatingPlushRoot -Force | Out-Null
    foreach ($file in $floatingPlushAssetFiles) {
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot "assets\floating-plush\$file") -Destination (Join-Path $runtimeFloatingPlushRoot $file) -Force
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
    $runtimeIntegrationReceipt = Wait-CountdownWindowReady -Path $runtimeIntegrationStatusPath -LaunchToken $runtimeIntegrationRequest.launchToken -ExpectedSessionId ([System.Diagnostics.Process]::GetCurrentProcess().SessionId) -ExpectedApplicationPath $runtimeIntegrationEntry -TimeoutSeconds 20 -PollMilliseconds 100 -AllowLegacyPowerShellAdapter
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
if (-not $validation.DatePickerButtonOpensCalendar) { throw 'Clicking the date picker drop-down button did not open a calendar popup.' }
if (-not $validation.DatePickerTextOpensCalendar) { throw 'Clicking the date text does not open every themed calendar.' }
if (-not $validation.DatePickerTextClicksUseTheme) { throw 'Clicking the date text is not attached to every themed date picker.' }
if (-not $validation.DatePickerTextOpensThemedPopup) { throw 'Clicking the date text did not open a real themed calendar popup.' }
if ($validation.TimeDropDowns -ne 3) { throw "Expected three time drop-downs, got $($validation.TimeDropDowns)." }
if (-not $validation.CompactSelectionFieldLayout) {
    throw "Short selection fields should stay content-sized at default and expanded window widths: default(name=$($validation.CountdownNameWidth), date=$($validation.TargetDateWidth), hour=$($validation.TargetHourWidth), minute=$($validation.TargetMinuteWidth), second=$($validation.TargetSecondWidth), precision=$($validation.DisplayPrecisionWidth)); expanded(date=$($validation.ExpandedTargetDateWidth), hour=$($validation.ExpandedTargetHourWidth), minute=$($validation.ExpandedTargetMinuteWidth), second=$($validation.ExpandedTargetSecondWidth), precision=$($validation.ExpandedDisplayPrecisionWidth))."
}
if (-not $validation.CompactPreferenceFieldLayout) {
    throw "Preference fields should stay compact at default and expanded window widths: default(precision=$($validation.DisplayPrecisionWidth), salary=$($validation.MonthlySalaryWidth), start=$($validation.WorkStartWidth), end=$($validation.WorkEndWidth)); expanded(precision=$($validation.ExpandedDisplayPrecisionWidth), salary=$($validation.ExpandedMonthlySalaryWidth), start=$($validation.ExpandedWorkStartWidth), end=$($validation.ExpandedWorkEndWidth))."
}
if (-not $validation.HasSetNowButton) { throw 'Set-now action is missing.' }
if (-not $validation.HasCountdownList) { throw 'Multiple-countdown list is missing.' }
if (-not $validation.HasNameEditor) { throw 'Countdown name editor is missing.' }
if (-not $validation.FloatingWindowPresent) { throw 'Desktop floating countdown window is missing.' }
if (-not $validation.FloatingWindowStaysAboveWorkAreaBottom) {
    throw "Floating window overlaps the taskbar work area: countdown bottom margin=$($validation.FloatingCountdownBottomMargin), notes bottom margin=$($validation.FloatingNotesBottomMargin)."
}
if (-not $validation.FloatingWindowBottomAnchored) {
    throw "Floating window does not return to the work-area bottom after content size changes: countdown bottom margin=$($validation.FloatingCountdownBottomMargin), notes bottom margin=$($validation.FloatingNotesBottomMargin)."
}
if (-not $validation.FloatingWindowReanchorsAfterContentChanges) { throw 'Floating window did not stay bottom-anchored while note content was added and removed.' }
if (-not $validation.FloatingWindowHeightConstrainedToWorkArea) { throw 'Floating window height can exceed the desktop work area after content changes.' }
if (-not $validation.FloatingPlushTaskWrapLoaded) { throw 'The three floating plush task-wrap anchors did not load.' }
if (-not $validation.FloatingPlushUsesThreeAnchors) { throw 'Floating plush characters must use three dedicated scene anchors.' }
if ($validation.FloatingPlushCount -ne 3) { throw "Expected three floating plush characters, got $($validation.FloatingPlushCount)." }
if (-not $validation.FloatingPlushDecorationsAreNonInteractive) { throw 'Floating plush characters must remain mouse-transparent decoration.' }
if (-not $validation.FloatingPlushUsesReferencePoses) { throw 'Floating plush characters do not use the three reference pose assets.' }
$expectedFloatingReferenceAssets = @(
    'floating-plush-mymelody-reference.png',
    'floating-plush-cinnamoroll-reference.png',
    'floating-plush-kuromi-reference.png'
)
if ((@($validation.FloatingPlushAssetNames) -join ',') -ne ($expectedFloatingReferenceAssets -join ',')) {
    throw "Floating plush characters did not load the three extracted reference assets: $(@($validation.FloatingPlushAssetNames) -join ',')."
}
if (-not $validation.FloatingPlushAnchorsIntegrateWithPanel) { throw 'Floating plush scene anchors are not layered above the task panel.' }
if (-not $validation.FloatingReferenceAspectMatches) { throw 'The floating reference scene is not rendered at the required 520x390 4:3 format.' }
if (-not $validation.FloatingReferencePanelGeometryMatches) { throw 'The floating frame, content panel, header, or countdown cards do not match the frozen reference geometry.' }
if (-not $validation.FloatingReferenceMascotGeometryMatches) { throw 'The rendered mascot alpha bounds do not match the frozen reference geometry.' }
if (-not $validation.FloatingReferenceContentSafeAreaMatches -or $validation.FloatingReferenceContentOverlapCount -ne 0) { throw "A rendered mascot overlaps protected floating content: $(@($validation.FloatingReferenceContentOverlapTargets) -join ',')." }
if (-not $validation.FloatingReferenceCaptureWritten) { throw 'The floating reference scene did not produce a non-empty RenderTargetBitmap PNG.' }
if (-not $validation.FloatingReferenceScenarioMatches) { throw 'The isolated floating reference scene is missing its deterministic two-card countdown and income state.' }
if (-not $validation.FloatingReferenceViewportStatesMatch) { throw 'The fixed floating viewport does not preserve zero, one, multiple countdown, note reminder, or scrolling states.' }
$expectedDayRasterComponents = @(
    'component-day-outer-shell.png',
    'component-day-header-blank.png',
    'component-day-card-blank.png',
    'component-day-mascots.png'
)
if ((@($validation.FloatingDayRasterComponentAssetNames) -join ',') -ne ($expectedDayRasterComponents -join ',')) {
    throw "The floating window did not load all four day raster components: $(@($validation.FloatingDayRasterComponentAssetNames) -join ',')."
}
if (-not $validation.FloatingDayRasterShellAndHeaderLoaded) { throw 'The day raster shell or blank header component did not load.' }
if (-not $validation.FloatingRasterSurfacesFollowDayNightTheme) { throw 'The floating raster and vector surfaces do not switch at Beijing-time boundaries.' }
if (-not $validation.FloatingDayRasterDecorationsAreNonInteractive) { throw 'A day raster decoration blocks pointer input.' }
if (-not $validation.FloatingDayRasterCardStatesMatch) { throw 'Day raster cards do not follow the real zero, one, two-countdown, and note states.' }
if (-not $validation.FloatingDynamicCardContentRemainsWpf) { throw 'Floating card text or note checkboxes are no longer real WPF controls.' }
if (-not $validation.FloatingDynamicHeaderControlsRemainWpf) { throw 'Floating title, tabs, or command icons are no longer real WPF controls.' }
if (-not $validation.FloatingHeaderIconAccessibility) { throw 'The floating icon buttons lost their tooltips or accessible names.' }
if (-not $validation.FloatingHeaderIconClickCommandsWork) { throw 'The floating open-manager or exit icon button no longer executes its click command.' }
if (-not $validation.FloatingPlushGroupWithinWorkArea) { throw 'The floating plush task-wrap group exceeds the desktop work area.' }
if (-not $validation.FloatingTitleTextFits) {
    throw "Floating sticky-note title is clipped: available=$($validation.FloatingTitleTextAvailableWidth), required=$($validation.FloatingTitleTextRequiredWidth)."
}
if (-not $validation.FloatingTitleTextHasVerticalClearance) {
    throw "Floating sticky-note title lacks vertical clearance: available=$($validation.FloatingTitleTextAvailableHeight), required=$($validation.FloatingTitleTextRequiredHeight)."
}
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
if (-not $validation.HasNoteReminderControls) { throw 'Sticky-note reminder selectors or custom-minute inputs are missing from the manager or floating entry.' }
if (-not $validation.NoteReminderControlsWork) { throw 'Sticky-note reminder selectors do not support presets, custom minutes, and disabling reminders.' }
if (-not $validation.NoteReminderDispatchWorks) { throw 'Sticky-note reminders were not dispatched exactly once per due interval.' }
if (-not $validation.NoteReminderWindowIsThemed) { throw 'Sticky-note reminder window did not use the warm non-blocking application theme.' }
if (-not $validation.OfficialCharacterBackgroundLoaded) { throw 'The official Sanrio character background was not loaded into the main window.' }
if (-not $validation.OfficialCharacterBackgroundCharactersExclusive) { throw 'The official Sanrio background must contain only My Melody, Cinnamoroll, and Kuromi.' }
if (-not $validation.OfficialCharacterBackgroundLowContrast) { throw 'The official Sanrio character background is too strong for readable application content.' }
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
if (-not $validation.ToolTipTemplateMaterializes) { throw 'The themed ToolTip template cannot be instantiated safely.' }
if (-not $validation.ThemedControlTemplatesApplied) { throw 'One or more primary or floating controls did not receive the shared theme template.' }
if (-not $validation.ComboBoxesUseThemedDropDowns) { throw 'A ComboBox drop-down, option, or scroll bar is still using an unthemed surface.' }
if (-not $validation.DatePickerCalendarIsThemed) { throw 'The DatePicker text box, trigger, or calendar family is not fully themed.' }
if (-not $validation.DatePickerTextFits) {
    throw "The complete selected date is clipped: available=$($validation.DatePickerTextAvailableWidth), required=$($validation.DatePickerTextRequiredWidth)."
}
if (-not $validation.LiveDatePickerPopupIsThemed) { throw 'The opened DatePicker popup fell back to system calendar styles.' }
if (-not $validation.LiveDatePickerPopupVisuallyUnified) { throw 'The opened DatePicker popup does not use the shared warm border, header, and selected-date treatment.' }
if (-not $validation.CalendarHeaderTextFits) {
    throw "The calendar month header is clipped: available=$($validation.CalendarHeaderTextAvailableWidth), required=$($validation.CalendarHeaderTextRequiredWidth)."
}
if (-not $validation.PopupSurfacesUseThemeTokens) { throw 'A popup surface is missing the shared themed background, border, or motion treatment.' }
if (-not $validation.NoDefaultSystemSurfaces) { throw 'A system window frame or MessageBox remains in the application flow.' }
if (-not $validation.NoRawFloatingThemeBypass) { throw 'The floating window bypasses shared theme tokens with raw colors.' }
if ((@($validation.MainThemeBoundaryModes) -join ',') -ne 'Night,Day,Day,Night') { throw 'The main window does not switch themes at the expected Beijing-time boundaries.' }
if ((@($validation.MainThemeBackgroundColors) -join ',') -ne '#FFFFF6EF,#FF2B2118') { throw 'The main window day/night background palette is incorrect.' }
if ((@($validation.MainThemeCharacterOpacities) -join ',') -ne '10,16') { throw 'The main-window character layer does not adapt across day and night.' }
if ($validation.MainCharacterPanelDockCount -ne 3) { throw 'The main-window character scene must contain three docks.' }
if (-not $validation.MainCharacterPanelDocksIntegrateWithPanel) { throw 'The main-window character docks are not integrated with the workspace panel.' }
if (-not $validation.MainCharacterPanelDecorationsAreNonInteractive) { throw 'The main-window character scene blocks workspace interaction.' }
if ((@($validation.MainThemeCharacterPanelColors) -join ',') -ne '#FFFFF7E8,#FF3B2B1D') { throw 'The main-window character scene does not switch to the expected day/night palette.' }
if (-not $validation.MainCharacterPanelDocksFollowDayNightTheme) { throw 'The main-window character docks do not follow the day/night theme.' }
if (-not $validation.MainThemeUsesChinaStandardTime) { throw 'The main window theme does not use China Standard Time.' }
if ((@($validation.FloatingThemeBoundaryModes) -join ',') -ne 'Night,Day,Day,Night') { throw 'The floating panel does not switch themes at the expected Beijing-time boundaries.' }
if ((@($validation.FloatingThemePanelColors) -join ',') -ne '#FFFFFDFC,#FF3A2B1E') { throw 'The floating panel day/night palette is incorrect.' }
if ((@($validation.FloatingThemePlushGlowColors) -join ',') -ne '#FFB9794E,#FFE9B36D') { throw 'The floating plush scene glow palette is incorrect.' }
if ((@($validation.FloatingMascotPresentationModes) -join ',') -ne 'NightAnchors,DayRaster,DayRaster,NightAnchors') { throw 'The floating mascot layer does not switch between day raster and night anchors at Beijing-time boundaries.' }
if (-not $validation.FloatingRasterMascotBoundaryStatesMatch) { throw 'The floating mascot day/night boundary states are incomplete.' }
if (-not $validation.FloatingThemeUsesChinaStandardTime) { throw 'The floating panel theme does not use China Standard Time.' }
if (-not $validation.FloatingPlushSceneFollowsDayNightTheme) { throw 'The floating plush scene does not follow the day/night theme transition.' }
if (-not $validation.FloatingThemeTextContrast) { throw 'Floating header and validation text do not maintain sufficient contrast across day and night.' }
if (-not $validation.TagPopupFollowsDayNightTheme) { throw 'An open tag popup does not follow the day/night theme transition.' }
if (-not $validation.MainThemeReminderLabelFollowsTheme) { throw 'The main-window reminder label does not follow the day/night theme.' }
if (-not $validation.WarmYellowPaletteMatchesLegacy) { throw 'The application no longer matches the legacy warm-yellow palette.' }
if (-not $validation.PrimaryButtonHoverTextContrast) { throw 'The primary action loses readable coral-on-white contrast while hovered.' }
if (-not $validation.ToolTipThemeTextContrast) { throw 'The themed tooltip loses readable contrast across the day/night transition.' }
if (-not $validation.TagChipTextContrast) { throw 'Tag chips lose readable warm-ink text contrast across the day/night transition.' }
if (-not $validation.ThemedInteractiveStates) { throw 'A themed interactive control is missing visual state feedback.' }
if (-not $validation.ThemeTextContrast) { throw 'The warm theme does not meet the required text-contrast pairs.' }
if (-not $validation.DangerButtonContrast) { throw 'The destructive confirmation button loses required text contrast when hovered or pressed.' }
if (-not $validation.DestructiveConfirmationIsSafe) { throw 'A destructive confirmation still makes deletion the Enter default action.' }
if (-not $validation.EffectiveThemeTextContrast) { throw 'Muted note or calendar text falls below the required effective contrast.' }
if (-not $validation.FormControlsHaveAccessibleNames) { throw 'A core form control is missing an explicit accessible name.' }
if (-not $validation.StatusMessagesUseLiveRegions) { throw 'Dynamic status messages are not announced through live regions.' }
if (-not $validation.ReminderWindowSupportsKeyboardDismissal) { throw 'The non-activating reminder window cannot be dismissed with Escape.' }
if (-not $validation.CompactActionTargetsAreUsable) { throw 'Compact tag and note actions are smaller than the desktop interaction target.' }

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
            @{ id = 'note-active-old'; text = '未完成旧日期'; date = '2026-07-18'; isCompleted = $false; tagIds = @('seed-tag') }
            @{ id = 'note-active-new-first'; text = '未完成新日期甲'; date = '2026-07-22'; isCompleted = $false; tagIds = @() }
            @{ id = 'note-active-new-second'; text = '未完成新日期乙'; date = '2026-07-22'; isCompleted = $false; tagIds = @() }
            @{ id = 'note-completed-old'; text = '已完成旧日期'; date = '2026-07-18'; isCompleted = $true; tagIds = @() }
            @{ id = 'note-completed-new'; text = '已完成新日期'; date = '2026-07-22'; isCompleted = $true; tagIds = @() }
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
    if ($listValidation.LoadedNotes -ne 5) { throw "Expected five loaded sticky notes, got $($listValidation.LoadedNotes)." }
    if ($listValidation.ActiveNoteCount -ne 3 -or $listValidation.CompletedNoteCount -ne 2) { throw "Unexpected sticky-note grouping: active=$($listValidation.ActiveNoteCount), completed=$($listValidation.CompletedNoteCount)." }
    if (-not $listValidation.NotePersistenceRoundTrip) { throw 'Sticky-note persistence round-trip failed.' }
    if (-not $listValidation.NoteActionsBoundByTag) { throw 'Sticky-note actions are not bound to their note ID.' }
    if (-not $listValidation.NoteStatusLabelsPresent) { throw 'Sticky-note status labels are missing from rendered rows.' }
    if (-not $listValidation.HasNoteDateControls) { throw 'The manager or floating sticky-note date picker is missing.' }
    if (-not $listValidation.NoteDatesRendered) { throw 'Sticky-note calendar dates are missing from rendered rows.' }
    if (-not $listValidation.NotesGroupedByDay) { throw 'Sticky-note rows are not grouped by calendar day.' }
    if (@($listValidation.SortedNoteTexts) -join ',' -ne '未完成新日期乙,未完成新日期甲,未完成旧日期,已完成新日期,已完成旧日期') {
        throw "Sticky-note rows are not sorted by newest date and entry order: $(@($listValidation.SortedNoteTexts) -join ',')."
    }
    if (-not $listValidation.ManagerNoteRowsFollowSortOrder -or
        @($listValidation.RenderedManagerNoteIds) -join ',' -ne 'note-active-new-second,note-active-new-first,note-active-old,note-completed-new,note-completed-old') {
        throw "Rendered manager note order is incorrect: $(@($listValidation.RenderedManagerNoteIds) -join ',')."
    }
    if (-not $listValidation.FloatingNotesKeepExistingSortOrder -or
        @($listValidation.RenderedFloatingNoteIds) -join ',' -ne 'note-active-old,note-active-new-first,note-active-new-second') {
        throw "Floating note order changed unexpectedly: $(@($listValidation.RenderedFloatingNoteIds) -join ',')."
    }
    if (-not $listValidation.NoteDateSectionsCanCollapse) { throw 'Sticky-note date sections cannot be expanded and collapsed independently.' }
    if (-not $listValidation.NoteDateSectionsAreIndependent) { throw 'Active and completed date sections do not retain independent collapse state.' }
    if (-not $listValidation.NoteDateSectionStateSurvivesRefresh) { throw 'Sticky-note date collapse state did not survive a manager refresh.' }
    if (-not $listValidation.NoteStatusSectionsAreIndependent) { throw 'Active and completed status sections are not independent across refresh.' }
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
    $noteExerciseDateBefore = [TimeZoneInfo]::ConvertTimeBySystemTimeZoneId(
        [DateTimeOffset]::UtcNow,
        'China Standard Time'
    ).AddDays(2).ToString('yyyy-MM-dd', [Globalization.CultureInfo]::InvariantCulture)
    $noteAddValidationJson = & $powerShellEngine `
        -NoLogo -NoProfile -NonInteractive -STA -File (Join-Path $PSScriptRoot '敏敏子倒计时.ps1') `
        -ValidateOnly -ExerciseNoteAdd -SettingsFile $noteAddSettingsPath | Out-String
    if ($LASTEXITCODE -ne 0) { throw "Sticky-note add click validation failed: $noteAddValidationJson" }
    $noteAddValidation = $noteAddValidationJson | ConvertFrom-Json
    if (-not $noteAddValidation.NoteDateRequired) { throw 'The sticky-note add flow accepted a missing calendar date or discarded the draft.' }
    if (-not $noteAddValidation.NoteAddViaClick) { throw 'Clicking Add did not create an active sticky note and retain the application validation flow.' }
    if (-not $noteAddValidation.NoteDateViaPicker) { throw 'Clicking Add did not preserve the selected sticky-note calendar date.' }
    if (-not $noteAddValidation.NoteReminderViaSelector) { throw 'Clicking Add did not preserve the selected sticky-note reminder interval and start time.' }
    if (-not $noteAddValidation.NoteStatusViaCheckbox) { throw 'Sticky-note completion status did not change through its checkbox.' }
    $noteExerciseDateAfter = [TimeZoneInfo]::ConvertTimeBySystemTimeZoneId(
        [DateTimeOffset]::UtcNow,
        'China Standard Time'
    ).AddDays(2).ToString('yyyy-MM-dd', [Globalization.CultureInfo]::InvariantCulture)
    $expectedNoteExerciseDates = @($noteExerciseDateBefore, $noteExerciseDateAfter) | Select-Object -Unique
    $noteAddSettings = [System.IO.File]::ReadAllText($noteAddSettingsPath, [System.Text.Encoding]::UTF8) | ConvertFrom-Json
    if (@($noteAddSettings.notes | Where-Object { $_.text -eq '验证点击添加便签' -and $_.date -in $expectedNoteExerciseDates -and -not $_.isCompleted -and $_.reminder.type -eq 'repeat' -and $_.reminder.intervalMinutes -eq 30 -and $null -ne $_.reminder.firstDueUtc }).Count -ne 1) { throw 'Clicking Add did not persist the active sticky note with its selected date and reminder.' }
    if (-not $noteAddValidation.MainNoReminderViaClick -or -not $noteAddValidation.MainOnceReminderViaClick -or -not $noteAddValidation.MainRepeatingReminderViaClick) { throw 'Main window reminder mode click paths did not all pass.' }
    if (-not $noteAddValidation.FloatingNoReminderViaClick -or -not $noteAddValidation.FloatingOnceReminderViaClick -or -not $noteAddValidation.FloatingRepeatingReminderViaClick) { throw 'Floating window reminder mode click paths did not all pass.' }

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
    if (-not $tagInteractionValidation.NoteDateFilterControls) { throw 'The sticky-note filter is missing themed exact-date, undated, or clear controls.' }
    if (-not $tagInteractionValidation.NoteDateFilterBehavior) { throw 'Sticky-note exact-date, undated, or clear filtering did not return the expected records.' }
    if (-not $tagInteractionValidation.TagFloatingNoteInheritsTags) { throw 'Floating note creation did not inherit the current selected tag.' }
    if (-not $tagInteractionValidation.TagFloatingNoteDateViaPicker) { throw 'Floating note creation did not preserve its selected calendar date.' }
    if (-not $tagInteractionValidation.TagFloatingNotePopupOnDemand) { throw 'Floating note tags were not hidden until the compact selector was opened.' }
    if (-not $tagInteractionValidation.TagFloatingNoteCanOverrideTags) { throw 'Floating note creation could not override or clear its inherited tags.' }
    if (-not $tagInteractionValidation.TagNoteEditingPersistsTags) { throw 'Editing a sticky note did not persist its text and selected tags.' }
    if (-not $tagInteractionValidation.TagNoteEditingPersistsDate) { throw 'Editing a sticky note did not persist its selected calendar date.' }
    if (-not $tagInteractionValidation.TagRenameAndDeleteDetachOnly) { throw 'Tag rename or delete-detach behavior failed.' }
    $tagInteractionSettings = [System.IO.File]::ReadAllText($tagInteractionSettingsPath, [System.Text.Encoding]::UTF8) | ConvertFrom-Json
    if ($tagInteractionSettings.schemaVersion -ne 8) { throw 'Tag interaction exercise did not persist schema v8.' }
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
    if (-not $legacyV3Validation.PersistenceRoundTrip -or -not $legacyV3Validation.NotePersistenceRoundTrip -or -not $legacyV3Validation.TagPersistenceRoundTrip) { throw 'Schema-v3 settings did not serialize as schema-v8.' }
    if ($legacyV3Validation.LoadedDisplayPrecision -ne 'Minute' -or ([decimal] $legacyV3Validation.LoadedMonthlySalary) -ne [decimal]12345 -or $legacyV3Validation.LoadedWorkStart -ne '08:30' -or $legacyV3Validation.LoadedWorkEnd -ne '17:30') { throw 'Schema-v3 preferences were not preserved.' }
    $legacyV3HashAfterValidation = (Get-FileHash -Algorithm SHA256 -LiteralPath $legacyV3SettingsPath).Hash
    if ($legacyV3HashAfterValidation -ne $legacyV3HashBeforeValidation) { throw 'ValidateOnly modified the supplied schema-v3 settings file.' }
}
finally {
    foreach ($diagnosticPath in @($diagnosticLogPath, "$diagnosticLogPath.1", "$diagnosticLogPath.2", "$diagnosticLogPath.3")) {
        Remove-Item -LiteralPath $diagnosticPath -Force -ErrorAction SilentlyContinue
    }
    Remove-Item -LiteralPath (Split-Path -Parent $diagnosticLogPath) -Force -ErrorAction SilentlyContinue
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
    Passed = 123
    Failed = 0
    SelectedDateType = $picker.SelectedDate.GetType().FullName
    ApplicationValidation = $validation.Valid
} | ConvertTo-Json
