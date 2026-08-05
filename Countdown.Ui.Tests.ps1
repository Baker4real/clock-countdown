#requires -Version 7.0

$ErrorActionPreference = 'Stop'
[Environment]::SetEnvironmentVariable(
    'MINMIN_COUNTDOWN_MAIN_BASELINE_GENERATE',
    'stale-test-request',
    [EnvironmentVariableTarget]::Process
)
$floatingReferenceBaselineManifest = Join-Path $PSScriptRoot 'assets\ui-references\floating-window\520x390\2026-08-05-v3\manifest.json'
$floatingReferenceBaselineRequired = Test-Path -LiteralPath $floatingReferenceBaselineManifest -PathType Leaf
[Environment]::SetEnvironmentVariable(
    'MINMIN_COUNTDOWN_VISUAL_SOURCE_ROOT',
    $(if ($floatingReferenceBaselineRequired) { $PSScriptRoot } else { $null }),
    [EnvironmentVariableTarget]::Process
)
[Environment]::SetEnvironmentVariable(
    'MINMIN_COUNTDOWN_FLOATING_BASELINE_GENERATE',
    $null,
    [EnvironmentVariableTarget]::Process
)
[Environment]::SetEnvironmentVariable(
    'MINMIN_COUNTDOWN_MAIN_BASELINE_GENERATE',
    $null,
    [EnvironmentVariableTarget]::Process
)
if (-not [string]::IsNullOrWhiteSpace(
        [Environment]::GetEnvironmentVariable('MINMIN_COUNTDOWN_MAIN_BASELINE_GENERATE'))) {
    throw 'UI 测试未清理主窗口视觉基线生成环境变量。'
}
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
if (-not $validation.FloatingReferencePanelGeometryMatches) { throw 'The floating frame, header, or selected warm-paper countdown hierarchy does not match its fixed geometry.' }
if (-not $validation.FloatingReferenceMascotGeometryMatches) { throw 'The rendered mascot alpha bounds do not match the frozen reference geometry.' }
if (-not $validation.FloatingReferenceBaselineGateIsolationBehavior) { throw 'A floating reference manifest negative fixture did not fail closed in isolation.' }
if (@($validation.FloatingReferenceBaselineNegativeFixtures).Count -ne 12) { throw 'The floating reference manifest negative-fixture inventory is incomplete.' }
if (-not $validation.FloatingReferenceFreshnessGateIsolationBehavior) { throw 'A long-note frame freshness negative fixture did not fail closed in isolation.' }
$expectedFreshnessFixtures = @(
    'duplicate-capture-id',
    'duplicate-state-fingerprint',
    'long-note-layout-unreachable',
    'long-note-typography-mismatch',
    'reused-frame',
    'state-fingerprint-mismatch',
    'swapped-top-bottom',
    'top-bottom-pixel-difference-too-small'
)
if (((@($validation.FloatingReferenceFreshnessNegativeFixtures) | Sort-Object) -join ',') -ne ($expectedFreshnessFixtures -join ',')) { throw 'The long-note frame freshness negative-fixture inventory is incomplete.' }
foreach ($fieldName in @(
    'FloatingLongNotesBodyHasNoManualLineBreaks',
    'FloatingLongNotesNaturalWraps',
    'FloatingLongNotesUsesOnlyOuterScroller',
    'FloatingLongNotesOuterScrollable',
    'FloatingLongNotesTopBottomReachable',
    'FloatingLongNotesSafeAreaMatches',
    'FloatingLongNotesTypographyMatches',
    'FloatingLongNotesLayoutValid'
)) {
    if (-not [bool] $validation.$fieldName) { throw "The long-note WPF viewport contract is red: $fieldName." }
}
$expectedLongNoteCaptureIds = @(
    'long-notes-day-bottom',
    'long-notes-day-top',
    'long-notes-night-bottom',
    'long-notes-night-top'
)
$longNoteMeasurements = @($validation.FloatingLongNotesMeasurements)
if ($longNoteMeasurements.Count -ne 4 -or ((@($longNoteMeasurements.CaptureId) | Sort-Object) -join ',') -ne ($expectedLongNoteCaptureIds -join ',')) { throw 'The long-note WPF measurement inventory is not the exact four required states.' }
if (@($longNoteMeasurements | Where-Object { -not $_.BodyHasNoManualLineBreaks -or -not $_.NaturalWraps -or -not $_.UsesOnlyOuterScroller -or -not $_.OuterScrollable -or -not $_.PrimaryAnchorReachable -or -not $_.SafeAreaMatches }).Count -ne 0) { throw 'A long-note WPF state failed wrap, outer-scroll, reachability, or safe-area measurement.' }
if (@($longNoteMeasurements | Where-Object { [double] $_.BodyFontSize -ne 13 -or [double] $_.BodyLineHeight -ne 18 -or [double] $_.ReminderFontSize -ne 12 -or [double] $_.ReminderLineHeight -ne 17 -or -not $_.TypographyMatches }).Count -ne 0) { throw 'A long-note WPF state does not match the explicit 13/18 body and 12/17 reminder typography contract.' }
if (@($longNoteMeasurements | Where-Object { $_.State -eq 'Top' -and [math]::Abs([double] $_.VerticalOffset) -gt 1 }).Count -ne 0) { throw 'A long-note top frame is not captured at offset zero.' }
if (@($longNoteMeasurements | Where-Object { $_.State -eq 'Bottom' -and [math]::Abs([double] $_.VerticalOffset - [double] $_.ScrollableHeight) -gt 1 }).Count -ne 0) { throw 'A long-note bottom frame is not captured at ScrollableHeight.' }
if (@($longNoteMeasurements | Where-Object { ([string] $_.NoteTextSha256).Length -ne 64 -or [string]::IsNullOrWhiteSpace([string] $_.FirstVisibleAnchor) -or [string]::IsNullOrWhiteSpace([string] $_.LastVisibleAnchor) }).Count -ne 0) { throw 'A long-note frame is missing its body hash or visible anchors.' }
$longNoteCaptures = @($validation.FloatingLongNotesCaptures)
if ($longNoteCaptures.Count -ne 4 -or ((@($longNoteCaptures.CaptureId) | Sort-Object) -join ',') -ne ($expectedLongNoteCaptureIds -join ',')) { throw 'The actual WPF long-note capture inventory is incomplete.' }
if (@($longNoteCaptures.PngSha256 | Sort-Object -Unique).Count -ne 4 -or @($longNoteCaptures.StateFingerprint.Sha256 | Sort-Object -Unique).Count -ne 4) { throw 'Long-note frames or state fingerprints were reused across states.' }
if ($floatingReferenceBaselineRequired) {
    if (-not $validation.FloatingReferenceBaselineRequested -or -not $validation.FloatingReferenceBaselineManifestAvailable) { throw 'The versioned floating reference manifest was not loaded from the source tree.' }
    if (-not $validation.FloatingReferenceBaselineValidated) { throw "The current WPF day/night frames failed the versioned floating reference manifest: $(@($validation.FloatingReferenceBaselineDiagnostics) -join ',')." }
    if ([string] $validation.FloatingReferenceBaselineManifestPath -ne 'assets/ui-references/floating-window/520x390/2026-08-05-v3/manifest.json') { throw 'The floating reference manifest path is not portable and repository-relative.' }
    $expectedBaselineCaptureIds = @('floating-countdowns-day','floating-countdowns-night') + $expectedLongNoteCaptureIds
    if (@($validation.FloatingReferenceBaselineCaptures).Count -ne 6 -or ((@($validation.FloatingReferenceBaselineCaptures.CaptureId) | Sort-Object) -join ',') -ne (($expectedBaselineCaptureIds | Sort-Object) -join ',')) { throw 'The floating reference manifest does not bind the strict six-frame inventory.' }
    if (@($validation.FloatingReferenceBaselineGates.PSObject.Properties | Where-Object { -not [bool] $_.Value }).Count -ne 0) { throw 'A concrete floating reference manifest gate is red.' }
    if (@($validation.FloatingReferenceFreshnessGates.PSObject.Properties | Where-Object { -not [bool] $_.Value }).Count -ne 0) { throw 'A concrete long-note freshness gate is red.' }
    if (@($validation.FloatingReferenceBaselineCaptures.BaselineSha256 | Sort-Object -Unique).Count -ne 6) { throw 'The six-frame manifest reuses a PNG hash.' }
    if ([string] ($validation.FloatingReferenceBaselineCaptures | Where-Object CaptureId -eq 'floating-countdowns-day').BaselineSha256 -ne '6BC7D19CD445186E48913DB738907C785497A1B6A19D5A3BD5FEDFBC645D6FE9') { throw 'The current day countdown PNG hash does not match the formal baseline.' }
    if ([string] ($validation.FloatingReferenceBaselineCaptures | Where-Object CaptureId -eq 'floating-countdowns-night').BaselineSha256 -ne '2A3E246125832271AB26AAC6D90FE5B91A58D97049C20197282F2CD1A892E960') { throw 'The current night countdown PNG hash does not match the formal baseline.' }
    if (@($validation.FloatingReferenceBaselineCaptures | Where-Object { [int] $_.Comparison.MaxAbsoluteChannelDelta -gt 8 -or [double] $_.Comparison.MeanAbsoluteError -gt 0.02 -or [double] $_.Comparison.RootMeanSquareError -gt 0.25 -or [int] $_.Comparison.ChangedPixelCount -gt 500 }).Count -ne 0) { throw 'A current floating frame exceeds the fixed pixel comparison thresholds.' }
}
elseif ($validation.FloatingReferenceBaselineRequested -or $validation.FloatingReferenceBaselineManifestAvailable -or $validation.FloatingReferenceBaselineValidated -or @($validation.FloatingReferenceBaselineCaptures).Count -ne 0) {
    throw 'A runtime package without reference assets claimed a floating baseline validation.'
}
if (-not $validation.FloatingNightPlushSourceAlphaBoundsMatch) { throw 'The night mascot source alpha bounds no longer match the frozen reference geometry.' }
if ((@($validation.FloatingNightPlushSourceAlphaBounds) -join ';') -ne '7,5,137,95;371,15,137,77;407,207,113,109') { throw 'The night mascot source alpha bounds moved.' }
if (-not $validation.FloatingNightPlushEdgeBrightnessWithinLimit) { throw 'The night mascot 2-6px outer-edge brightness exceeds its warm-shadow limit.' }
if (-not $validation.FloatingNightPlushEdgeSaturationWithinLimit) { throw 'The night mascot 2-6px outer-edge saturation still forms a vivid outline.' }
if (-not $validation.FloatingNightPlushEdgeGlowGateIsolationBehavior) { throw 'The night mascot brightness and saturation negative fixtures cannot fail independently.' }
$nightPlushEdgeMeasurements = @($validation.FloatingNightPlushEdgeGlowMeasurements)
if ($nightPlushEdgeMeasurements.Count -ne 3) { throw 'The night mascot edge gate did not measure all three roles independently.' }
$maximumNightPlushBrightness = ($nightPlushEdgeMeasurements | Measure-Object -Property BrightnessSignal -Maximum).Maximum
$maximumNightPlushSaturation = ($nightPlushEdgeMeasurements | Measure-Object -Property Saturation -Maximum).Maximum
if ($maximumNightPlushBrightness -gt [double] $validation.FloatingNightPlushEdgeBrightnessLimit) { throw 'The reported night mascot edge brightness exceeds its reported limit.' }
if ($maximumNightPlushSaturation -gt [double] $validation.FloatingNightPlushEdgeSaturationLimit) { throw 'The reported night mascot edge saturation exceeds its reported limit.' }
if (-not ([string] $validation.FloatingNightPlushEdgeRoiAlgorithm).Contains('Chebyshev distance 2-6px', [System.StringComparison]::Ordinal)) { throw 'The night mascot edge report does not define the required 2-6px ROI.' }
if (-not $validation.FloatingReferenceContentSafeAreaMatches -or $validation.FloatingReferenceContentOverlapCount -ne 0) { throw "A rendered mascot overlaps protected floating content: $(@($validation.FloatingReferenceContentOverlapTargets) -join ',')." }
if (-not $validation.FloatingTitleUsesSemiBoldWeight) { throw 'The floating title does not preserve its independent SemiBold weight contract.' }
if (-not $validation.FloatingNoteInputWraps) { throw 'The floating sticky-note input does not preserve independent text wrapping.' }
if (-not $validation.FloatingNoteInputScrolls) { throw 'The floating sticky-note input does not preserve independent vertical scrolling.' }
if (-not $validation.FloatingInitialWorkspaceStateMatches) { throw 'The floating window does not initialize through the synchronized countdown workspace state.' }
if (-not $validation.FloatingWorkspaceSwitchingStaysSynchronized) { throw 'Floating workspace switches do not preserve exactly one selected button, matching chrome, and matching content.' }
if (-not $validation.FloatingCountdownSelectionSurvivesDayNightThemes) { throw 'The initial floating countdown selection is not recognized across day and night themes.' }
if (-not $validation.FloatingReferenceCaptureWritten) { throw 'The floating reference scene did not produce a non-empty RenderTargetBitmap PNG.' }
if (-not $validation.FloatingQuickAddDayExpandedScrolledCaptureWritten) { throw 'The expanded and scrolled floating quick-add day scene did not produce a non-empty offscreen capture.' }
if (-not $validation.FloatingQuickAddNightExpandedScrolledCaptureWritten) { throw 'The expanded and scrolled floating quick-add night scene did not produce a non-empty offscreen capture.' }
if (-not $validation.FloatingWorkdayCountdownUsesPrimaryVisual) { throw 'The workday-end countdown is not the sole primary row with its accessible name, warm-paper surface, accent rail, typography, and emphasized remaining time.' }
if (-not $validation.FloatingReferenceScenarioMatches) { throw 'The isolated floating reference scene does not render the workday-end countdown as the first primary row while preserving its deterministic two-card and income state.' }
if (-not $validation.FloatingPrimaryDeadlineTextFullyVisible) { throw 'The widest primary deadline subtitle is clipped or retains a visible Beijing-time suffix.' }
if (-not $validation.FloatingReferenceViewportStatesMatch) { throw 'The fixed floating viewport does not preserve zero, one, multiple countdown, note reminder, or scrolling states.' }
if (-not $validation.FloatingViewportScrollAndFocusRestored) { throw 'Floating refresh does not restore the current scroll position and sticky-note focus.' }
if (-not $validation.FloatingNoteFailureAppearsInSourceCard) { throw 'A failed floating sticky-note completion does not expose an accessible error in its source card.' }
if (-not $validation.FloatingResolvedNoteCardErrorDoesNotReturn) { throw 'A resolved floating sticky-note card error returns after the note is completed and restored.' }
if (-not $validation.FloatingSecondaryRemainingTextFullyVisible) { throw 'A long secondary countdown is clipped inside the fixed floating viewport.' }
if (-not $validation.FloatingSecondaryRemainingUnitSpacingMatches) { throw 'A secondary countdown no longer preserves FormatRemaining unit spacing.' }
if (-not $validation.FloatingSecondaryRemainingLayoutMatches) { throw 'A secondary countdown no longer fits its 194px right-aligned, single-line tabular column.' }
if (-not $validation.FloatingSecondaryRemainingGateIsolationBehavior) { throw 'The secondary countdown spacing and fixed-column negative fixtures cannot fail independently.' }
$secondaryRemainingSamples = @($validation.FloatingSecondaryRemainingSamples)
if ($secondaryRemainingSamples.Count -ne 5) { throw 'The secondary countdown gate did not cover all four precisions and long days.' }
if (($secondaryRemainingSamples.Precision -join ';') -ne 'Day;Hour;Minute;Second;SecondLongDays') { throw 'The secondary countdown precision coverage is incomplete or unstable.' }
if (($secondaryRemainingSamples.RenderedText -join ';') -ne '4 天;4 天 13 小时;4 天 13 小时 18 分钟;4 天 13 小时 18 分钟 59 秒;2000000 天 00 小时 00 分钟 00 秒') { throw 'The secondary countdown WPF text does not preserve unit spacing for every precision.' }
if (@($secondaryRemainingSamples | Where-Object { [Math]::Abs([double] $_.ColumnWidth - 194) -gt 0.01 }).Count -ne 0) { throw 'A secondary countdown does not declare the fixed 194px column.' }
if (@($secondaryRemainingSamples | Where-Object { [Math]::Abs([double] $_.ActualWidth - 194) -gt 0.5 }).Count -ne 0) { throw 'A secondary countdown actual layout width escaped the 194px rounding tolerance.' }
if (@($secondaryRemainingSamples[0..3] | Where-Object { [Math]::Abs([double] $_.FontSize - 13) -gt 0.01 }).Count -ne 0) { throw 'A regular secondary countdown no longer uses 13px type.' }
if ([double] $secondaryRemainingSamples[4].FontSize -lt 10 -or [double] $secondaryRemainingSamples[4].FontSize -ge 13) { throw 'The long-day secondary countdown did not use its bounded adaptive font size.' }
foreach ($fieldName in @(
    'FloatingScrollBarLocalStyleScoped',
    'FloatingScrollBarHitTargetUsable',
    'FloatingScrollBarVisualWeightProgression',
    'FloatingScrollBarUsesWarmThemeTokens',
    'FloatingScrollBarContentRemainsSafe',
    'FloatingScrollBarInputPathsWork',
    'FloatingScrollBarGateIsolationBehavior',
    'FloatingScrollBarDayNightStateCapturesReady'
)) {
    if (-not [bool] $validation.$fieldName) { throw "Floating scroll-bar gate failed: $fieldName" }
}
$floatingScrollBarSamples = @($validation.FloatingScrollBarStateMeasurements)
if ($floatingScrollBarSamples.Count -ne 6) { throw 'The floating scroll-bar gate did not cover six day/night visual states.' }
if (@($floatingScrollBarSamples | Where-Object { [Math]::Abs([double] $_.HitTargetWidth - 10) -gt 0.01 -or [Math]::Abs([double] $_.ThumbVisualWidth - 4) -gt 0.01 }).Count -ne 0) { throw 'The floating scroll-bar 10px hit / 4px visual geometry is incorrect.' }
if ((@($floatingScrollBarSamples[0..2].ThumbOpacity) -join ',') -ne '0.52,0.78,0.96') { throw 'The floating scroll-bar hover/drag emphasis progression is incorrect.' }
if (@($floatingScrollBarSamples | Where-Object { [double] $_.ThumbVisualRight -gt 396.01 -or [double] $_.MaximumVisibleCardRight -ge [double] $_.ThumbVisualLeft -or -not [bool] $_.DoesNotObscureContent }).Count -ne 0) { throw 'A floating scroll-bar state overlaps a card or escapes x=396.' }
if ([double] $validation.FloatingScrollBarWheelOffsets[1] -le [double] $validation.FloatingScrollBarWheelOffsets[0] -or [double] $validation.FloatingScrollBarDragOffsets[1] -le [double] $validation.FloatingScrollBarDragOffsets[0]) { throw 'A real floating scroll-bar wheel or thumb-drag input path did not move.' }
if (-not $validation.FloatingDatePickerKeyboardLifecycleWorks) { throw 'A floating date picker does not focus its live calendar, close with Escape, and return focus to its trigger button.' }
if (-not $validation.FloatingFieldErrorsAppearAtSource) { throw 'A floating quick-note validation error is not shown beside and associated with its invalid field.' }
if (-not $validation.FloatingCorrectedFieldErrorsClearLiveMessage) { throw 'Correcting a floating quick-note field leaves stale local or live-region errors.' }
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
if (-not $validation.FloatingDayRasterCardStatesMatch) { throw 'Workspace-specific warm-paper rows fell back to the retired shared raster-card surface.' }
if (-not $validation.FloatingDynamicCardContentRemainsWpf) { throw 'Floating card text or note checkboxes are no longer real WPF controls.' }
if (-not $validation.FloatingDynamicHeaderControlsRemainWpf) { throw 'Floating title, tabs, or command icons are no longer real WPF controls.' }
if (-not $validation.FloatingHeaderIconAccessibility) { throw 'The floating icon buttons lost their tooltips or accessible names.' }
if (-not $validation.FloatingHeaderIconClickCommandsWork) { throw 'The floating open-manager or exit icon button no longer executes its click command.' }
if (-not $validation.FloatingDragSurfaceCoversWindowBackground) { throw 'The non-interactive floating-window background does not receive drag gestures.' }
if (-not $validation.FloatingTrayButtonAccessibility) { throw 'The floating tray button lost its tooltip or accessible name.' }
if (-not $validation.FloatingTrayProductionControllerActive) { throw 'The application host and tray validation do not share the production transition controller.' }
if (-not $validation.FloatingTrayMinimizeHidesWindowAndShowsIcon) { throw 'The floating tray button does not hide the window and show the notification-area icon.' }
if (-not $validation.FloatingTrayRestoreShowsWindowAndHidesIcon) { throw 'The notification-area restore command does not show the floating window and hide the icon.' }
if (-not $validation.FloatingTrayExitRequestsApplicationExit) { throw 'The notification-area exit command does not request application shutdown.' }
if (-not $validation.FloatingDragHandleReceivesPointerGestures) { throw 'The floating drag handle does not receive a routed left-button gesture.' }
if (-not $validation.FloatingDragHandleRequestsNativeWindowDrag) { throw 'The floating drag handle does not route input to WPF DragMove.' }
if (-not $validation.FloatingSyntheticDragDoesNotClaimPhysicalCompletion) { throw 'A rejected synthetic DragMove is being reported as a completed physical drag.' }
if (-not $validation.FloatingDragAttemptClampsToWorkArea) { throw 'A floating drag attempt does not immediately clamp to the work area.' }
if (-not $validation.FloatingInteractiveControlsDoNotRequestWindowDrag) { throw 'An interactive floating control incorrectly routes input to WPF DragMove.' }
if (-not $validation.FloatingDragHandleExcludesInteractiveControls) { throw 'The floating drag handle overlaps an interactive control.' }
if (-not $validation.FloatingWindowManualPositionPersistsAndRemainsVisibleAfterRefresh) { throw 'A manual floating-window position does not persist within the work area after content refresh.' }
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
if (-not $validation.MainNoteEditorFieldsDoNotOverlap) { throw 'Main-window repeating-reminder fields overlap the first-reminder schedule or error area.' }
if (-not $validation.MainNoteEditorLabelsVisible) { throw 'A main-window sticky-note editor field is missing its visible label.' }
if (-not $validation.MainMinimumSizeMatchesDefault) { throw 'The main-window default size is smaller than its supported minimum layout.' }
if (-not $validation.PopupKeyboardLifecycleWorks) { throw 'A filter or tag popup cannot close with Escape and return focus to its trigger.' }
if (-not $validation.PopupOutsideFocusPreserved) { throw 'Closing a popup after outside focus steals focus back to its trigger.' }
if (-not $validation.NoteEmptyStatesDistinguished) { throw 'The first-use sticky-note empty state is indistinguishable from filtered no-results.' }
if (-not $validation.NoteNoResultsResetWorks) { throw 'The filtered no-results state cannot reset all sticky-note filters.' }
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
if ((@($validation.MainThemeCharacterOpacities) -join ',') -ne '8,16') { throw 'The main-window character layer does not adapt across day and night.' }
if ($validation.MainCharacterPanelDockCount -ne 3) { throw 'The main-window character scene must contain three docks.' }
if (-not $validation.MainCharacterPanelDocksIntegrateWithPanel) { throw 'The main-window character docks are not integrated with the workspace panel.' }
if (-not $validation.MainCharacterPanelDecorationsAreNonInteractive) { throw 'The main-window character scene blocks workspace interaction.' }
if (-not $validation.MainReferenceDecorationAssetsLoaded) { throw 'The main-window day reference decoration assets did not load.' }
if (-not $validation.MainNightReferenceDecorationAssetsLoaded) { throw 'The main-window night character and wave assets did not load.' }
if (-not $validation.MainReferenceDecorationsAreNonInteractive) { throw 'The main-window day reference decorations block workspace interaction.' }
if (-not $validation.MainReferenceDecorationsFollowDayNightTheme) { throw 'The main-window day reference decorations do not follow the active theme.' }
if (-not $validation.MainReferenceDecorationLayerOrderMatches) { throw 'The main-window reference decoration layer order does not match the workspace contract.' }
if (-not $validation.MainWorkspaceResourcesFollowDayNightTheme) { throw 'The main-window workspace surfaces do not independently follow theme resources.' }
if (-not $validation.MainDynamicCountdownCardsUseThemeResources -or $validation.MainDynamicCountdownCardSampleCount -lt 1) { throw 'No qualifying dynamic countdown card sample uses the required theme resources.' }
if (-not $validation.MainDynamicNoteCardsUseThemeResources -or $validation.MainDynamicNoteCardSampleCount -lt 1) { throw 'No qualifying dynamic sticky-note card sample uses the required theme resources.' }
if (-not $validation.MainNightReferenceGeometryMatchesDay) { throw 'The main-window night character and wave geometry no longer matches the day reference.' }
if ((@($validation.MainNightReferenceWaveColumnCounts) -join ',') -ne '1093,1079') { throw 'The main-window night wave contour is not continuous across the reference width.' }
if (-not $validation.MainReferenceDecorationsShareScaleAtMinimumAndWideSizes) { throw 'The main-window day reference decorations do not share the content scaling coordinate system at minimum and wide sizes.' }
if (-not $validation.UiQualityGateIsolationBehavior) { throw 'A targeted UI quality-gate fixture failed to isolate its intended contract.' }
if ((@($validation.MainThemeCharacterPanelColors) -join ',') -ne '#FFFFF7E8,#FF3B2B1D') { throw 'The main-window character scene does not switch to the expected day/night palette.' }
if (-not $validation.MainCharacterPanelDocksFollowDayNightTheme) { throw 'The main-window character docks do not follow the day/night theme.' }
if (-not $validation.MainThemeUsesChinaStandardTime) { throw 'The main window theme does not use China Standard Time.' }
if ((@($validation.FloatingThemeBoundaryModes) -join ',') -ne 'Night,Day,Day,Night') { throw 'The floating panel does not switch themes at the expected Beijing-time boundaries.' }
if ((@($validation.FloatingThemePanelColors) -join ',') -ne '#FFFFFDFC,#FF3A2B1E') { throw 'The floating panel day/night palette is incorrect.' }
if ((@($validation.FloatingThemePlushGlowColors) -join ',') -ne '#FFB9794E,#FF756A62') { throw 'The floating plush scene glow palette is incorrect.' }
if ((@($validation.FloatingMascotPresentationModes) -join ',') -ne 'NightAnchors,DayRaster,DayRaster,NightAnchors') { throw 'The floating mascot layer does not switch between day raster and night anchors at Beijing-time boundaries.' }
if (-not $validation.FloatingRasterMascotBoundaryStatesMatch) { throw 'The floating mascot day/night boundary states are incomplete.' }
if (-not $validation.FloatingThemeUsesChinaStandardTime) { throw 'The floating panel theme does not use China Standard Time.' }
if (-not $validation.FloatingPlushSceneFollowsDayNightTheme) { throw 'The floating plush scene does not follow the day/night theme transition.' }
if (-not $validation.FloatingThemeTextContrast) { throw 'Floating header and validation text do not maintain sufficient contrast across day and night.' }
if (-not $validation.FloatingPrimaryCountdownTextContrast) { throw 'The primary floating countdown does not maintain 4.5:1 contrast against its actual composited surface.' }
if (-not $validation.FloatingPrimaryCountdownAccentSeparatedFromPlushGlow) { throw 'The primary floating countdown accent still shares the plush glow semantic token.' }
if (-not $validation.FloatingPrimaryCountdownContrastGateIsolationBehavior) { throw 'The primary countdown contrast and semantic-token fixtures do not fail independently.' }
$primaryCountdownContrastMeasurements = @($validation.FloatingPrimaryCountdownContrastMeasurements)
if ($primaryCountdownContrastMeasurements.Count -ne 2 -or ((@($primaryCountdownContrastMeasurements.Theme) | Sort-Object) -join ',') -ne 'Day,Night') { throw 'The primary countdown contrast report does not contain exact day/night WPF measurements.' }
$nightPrimaryCountdownContrast = @($primaryCountdownContrastMeasurements | Where-Object Theme -eq 'Night')[0]
if ([string] $nightPrimaryCountdownContrast.Foreground -ne '#FFE9B36D' -or [string] $nightPrimaryCountdownContrast.CompositeBackground -ne '#FF493525' -or [math]::Abs([double] $nightPrimaryCountdownContrast.ContrastRatio - 6.12) -gt 0.01) { throw 'The night primary countdown does not report the expected warm-gold contrast against #493525.' }
if (@($primaryCountdownContrastMeasurements | Where-Object { [double] $_.FontSize -ne 16 -or -not $_.UsesExpectedSemanticAccent -or -not $_.AccentTrackMatchesText -or -not $_.AccentDiffersFromPlushGlow }).Count -ne 0) { throw 'A primary countdown WPF measurement does not use the independent semantic accent for both rail and 16 DIP text.' }
if (-not $validation.FloatingQuickAddDayResourcesFollowLocalTheme) { throw 'Floating quick-add controls do not use the local warm day theme.' }
if (-not $validation.FloatingQuickAddNightResourcesFollowLocalTheme) { throw 'Floating quick-add controls do not use the local warm night theme.' }
if (-not $validation.FloatingQuickAddIndependentFromApplicationAutoTheme) { throw 'Floating quick-add controls still inherit the opposite application auto theme.' }
if (-not $validation.FloatingQuickAddThemeResourceIsolationBehavior) { throw 'Floating quick-add resource checks cannot fail one control family at a time.' }
if ((@($validation.FloatingQuickAddLocalCanvasColors) -join ',') -ne '#FFFFFDFC,#FF473323') { throw 'Floating quick-add local surface colors are not warm day and night values.' }
if ((@($validation.FloatingQuickAddLocalActiveColors) -join ',') -ne '#FFA24438,#FF6E492E') { throw 'Floating quick-add local active colors are not coral and warm brown.' }
$quickAddThemeChecks = @(
    'TextBox',
    'DatePicker',
    'DatePickerPopup',
    'ComboBox',
    'ComboBoxPopup',
    'TagSelector',
    'TagSelectorPopup',
    'CheckBox',
    'ScrollViewer',
    'ScrollBar'
)
foreach ($checkName in $quickAddThemeChecks) {
    if (-not [bool] $validation.FloatingQuickAddDayResourceChecks.$checkName) {
        throw "Floating quick-add day resource check failed: $checkName"
    }
    if (-not [bool] $validation.FloatingQuickAddNightResourceChecks.$checkName) {
        throw "Floating quick-add night resource check failed: $checkName"
    }
}
if (-not $validation.TagPopupFollowsDayNightTheme) { throw 'An open tag popup does not follow the day/night theme transition.' }
if (-not $validation.MainThemeReminderLabelFollowsTheme) { throw 'The main-window reminder label does not follow the day/night theme.' }
if (-not $validation.WarmYellowPaletteMatchesLegacy) { throw 'The application no longer matches the legacy warm-yellow palette.' }
if (-not $validation.PrimaryButtonHoverTextContrast) { throw 'The primary action loses readable coral-on-white contrast while hovered.' }
if (-not $validation.MainActionTextContrast) { throw 'A main-window action loses readable contrast in a day/night default, hover, or pressed state.' }
if (-not $validation.ToolTipThemeTextContrast) { throw 'The themed tooltip loses readable contrast across the day/night transition.' }
if (-not $validation.TagChipTextContrast) { throw 'Tag chips lose readable warm-ink text contrast across the day/night transition.' }
if (-not $validation.ThemedInteractiveStates) { throw 'A themed interactive control is missing visual state feedback.' }
if (-not $validation.ThemeTextContrast) { throw 'The warm theme does not meet the required text-contrast pairs.' }
if (-not $validation.DangerButtonContrast) { throw 'The destructive confirmation button loses required text contrast when hovered or pressed.' }
if (-not $validation.DestructiveConfirmationIsSafe) { throw 'A destructive confirmation still makes deletion the Enter default action.' }
if (-not $validation.EffectiveThemeTextContrast) { throw 'Muted note or calendar text falls below the required effective contrast.' }
if (-not $validation.FormControlsHaveAccessibleNames) { throw 'A core form control is missing an explicit accessible name.' }
if (-not $validation.StatusMessagesUseLiveRegions) { throw 'Dynamic status messages are not announced through live regions.' }
if (-not $validation.ReminderWindowSupportsKeyboardDismissal) { throw 'The reminder window cannot be dismissed with Escape, Enter, or Space.' }
if (-not $validation.ReminderWindowFocusesDismissAction) { throw 'The reminder window does not focus its dismiss action when shown.' }
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
    if (-not $listValidation.NoteStatusDisclosureVisuals) { throw 'Sticky-note status disclosures are missing fixed labels, counts, or state arrows.' }
    if (-not $listValidation.NoteDateTimelineVisuals) { throw 'Sticky-note date disclosures are missing the independent timeline visual.' }
    if (-not $listValidation.NoteDisclosureAccessibility) { throw 'Sticky-note disclosure controls are missing stable accessible names.' }
    if (-not $listValidation.NoteDisclosureThemeTokens) { throw 'Sticky-note disclosure theme tokens are missing day/night resources.' }
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
    if ($tagInteractionSettings.schemaVersion -ne 9) { throw 'Tag interaction exercise did not persist schema v9.' }
    if ($tagInteractionSettings.themePreference -ne 'Auto') { throw 'Tag interaction exercise did not preserve the Auto theme preference.' }
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
    if (-not $legacyV3Validation.PersistenceRoundTrip -or -not $legacyV3Validation.NotePersistenceRoundTrip -or -not $legacyV3Validation.TagPersistenceRoundTrip) { throw 'Schema-v3 settings did not serialize as schema-v9.' }
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
    Passed = 142
    Failed = 0
    SelectedDateType = $picker.SelectedDate.GetType().FullName
    ApplicationValidation = $validation.Valid
} | ConvertTo-Json
