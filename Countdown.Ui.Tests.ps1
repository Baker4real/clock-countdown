#requires -Version 7.0

$ErrorActionPreference = 'Stop'
[Environment]::SetEnvironmentVariable(
    'MINMIN_COUNTDOWN_MAIN_BASELINE_GENERATE',
    'stale-test-request',
    [EnvironmentVariableTarget]::Process
)
$floatingReferenceBaselineManifest = Join-Path $PSScriptRoot 'assets\ui-references\floating-window\520x390\2026-09-07-v21\manifest.json'
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
Import-Module (Join-Path $PSScriptRoot 'Countdown.Settings.psm1') -Force

function Test-ExactSymmetricTabTargets {
    param(
        [Parameter(Mandatory)] [string[]] $ExpectedForward,
        [Parameter(Mandatory)] [string[]] $ActualForward,
        [Parameter(Mandatory)] [string[]] $ActualBackward
    )

    $expectedBackward = [string[]] $ExpectedForward.Clone()
    [array]::Reverse($expectedBackward)
    return (
        ($ActualForward -join ',') -ceq ($ExpectedForward -join ',') -and
        ($ActualBackward -join ',') -ceq ($expectedBackward -join ','))
}

function Test-MainShortcutCommandResults {
    param([Parameter(Mandatory)] [object] $CommandResults)

    $properties = @($CommandResults.PSObject.Properties)
    return (
        $properties.Count -eq 8 -and
        ((@($properties.Name) | Sort-Object) -join ',') -eq
            'CountdownsWorkspace,Filter,Minimize,NotesWorkspace,SaveCountdown,SavePreferences,TagManager,Theme' -and
        @($properties | Where-Object { -not [bool] $_.Value }).Count -eq 0)
}

function Test-MainShortcutCommandDiagnostics {
    param([Parameter(Mandatory)] [object] $CommandDiagnostics)

    $properties = @($CommandDiagnostics.PSObject.Properties)
    return (
        $properties.Count -eq 8 -and
        ((@($properties.Name) | Sort-Object) -join ',') -eq
            'CountdownsWorkspace,Filter,Minimize,NotesWorkspace,SaveCountdown,SavePreferences,TagManager,Theme' -and
        @($properties | Where-Object {
                -not [bool] $_.Value.Handled -or
                -not [bool] $_.Value.EffectObserved
            }).Count -eq 0)
}

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
$validationSettingsPath = Join-Path `
    ([System.IO.Path]::GetTempPath()) `
    ("MinminCountdown-ui-validation-{0}.settings.json" -f [guid]::NewGuid().ToString('N'))
foreach ($diagnosticPath in @($diagnosticLogPath, "$diagnosticLogPath.1", "$diagnosticLogPath.2", "$diagnosticLogPath.3")) {
    Remove-Item -LiteralPath $diagnosticPath -Force -ErrorAction SilentlyContinue
}
try {
    $fatalDiagnosticOutput = & $powerShellEngine `
        -NoLogo -NoProfile -NonInteractive -STA -File (Join-Path $PSScriptRoot '敏敏子倒计时.ps1') `
        -ValidateOnly -ExerciseFatalDiagnostic -SettingsFile $validationSettingsPath `
        -DiagnosticLogPath $diagnosticLogPath 2>&1 | Out-String
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

    $applicationHostPath = Join-Path $PSScriptRoot 'Countdown.App.psm1'
    $applicationHostPathLiteral = $applicationHostPath.Replace("'", "''")
    $validationSettingsPathLiteral = $validationSettingsPath.Replace("'", "''")
    $applicationHostCommand = "Import-Module -Force -Name '$applicationHostPathLiteral'; Start-CountdownApplication -ValidateOnly -SettingsFile '$validationSettingsPathLiteral'"
    if (
        -not $applicationHostCommand.Contains('Start-CountdownApplication') -or
        -not $applicationHostCommand.Contains('-ValidateOnly') -or
        -not $applicationHostCommand.Contains('-SettingsFile') -or
        -not $applicationHostCommand.Contains("'$validationSettingsPathLiteral'")
    ) {
        throw 'Application-host validation command does not use the isolated validation settings file.'
    }
    $applicationHostValidationJson = & $powerShellEngine `
        -NoLogo -NoProfile -NonInteractive -STA -Command $applicationHostCommand | Out-String
    if ($LASTEXITCODE -ne 0) {
        throw "Application-host validation failed: $applicationHostValidationJson"
    }
    $applicationHostValidation = $applicationHostValidationJson | ConvertFrom-Json
    if (-not $applicationHostValidation.Valid) { throw 'Application host did not preserve the ValidateOnly contract.' }

    $validationJson = & $powerShellEngine `
        -NoLogo -NoProfile -NonInteractive -STA -File (Join-Path $PSScriptRoot '敏敏子倒计时.ps1') `
        -ValidateOnly -SettingsFile $validationSettingsPath | Out-String
    if ($LASTEXITCODE -ne 0) {
        throw "Application validation failed: $validationJson"
    }
    $validation = $validationJson | ConvertFrom-Json
}
finally {
    Remove-Item -LiteralPath $validationSettingsPath -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath "$validationSettingsPath.tmp" -Force -ErrorAction SilentlyContinue
}
if (-not $validation.DatePickerTextDefersCalendarUntilPointerRelease) { throw 'Clicking the date text opens the calendar before the pointer is released.' }

$runtimeIntegrationEvidenceRoot = 'D:\agents\run-evidence\migration-preflight-20260825T013156Z\clock-countdown-ui-tests'
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

$popupUiaRoot = Join-Path $runtimeIntegrationEvidenceRoot ("ui-popup-trigger-uia-" + [guid]::NewGuid().ToString('N'))
$popupUiaProcess = $null
$popupUiaSkipEvidence = $null
if ([Environment]::UserInteractive) {
    try {
        New-Item -ItemType Directory -Path $popupUiaRoot -Force | Out-Null
        foreach ($file in @('Countdown.App.psm1', 'Countdown.Core.psm1', 'Countdown.Notes.psm1', 'Countdown.Tags.psm1', 'Countdown.Runtime.psm1', 'Countdown.Settings.psm1', 'countdown-icon.ico', 'sanrio-official-mymelody.png', 'sanrio-official-cinnamoroll.png', 'sanrio-official-kuromi.png')) {
            Copy-Item -LiteralPath (Join-Path $PSScriptRoot $file) -Destination (Join-Path $popupUiaRoot $file) -Force
        }
        $popupUiaFloatingPlushRoot = Join-Path $popupUiaRoot 'assets\floating-plush'
        New-Item -ItemType Directory -Path $popupUiaFloatingPlushRoot -Force | Out-Null
        foreach ($file in $floatingPlushAssetFiles) {
            Copy-Item -LiteralPath (Join-Path $PSScriptRoot "assets\floating-plush\$file") -Destination (Join-Path $popupUiaFloatingPlushRoot $file) -Force
        }
        $popupUiaHostPath = Join-Path $popupUiaRoot 'Countdown.App.psm1'
        $popupUiaEntry = Join-Path $popupUiaRoot 'popup-uia-harness.ps1'
        $popupUiaRequestPath = Join-Path $popupUiaRoot 'startup-request.json'
        $popupUiaStatusPath = Join-Path $popupUiaRoot 'runtime-status.json'
        $popupUiaHostLiteral = $popupUiaHostPath.Replace("'", "''")
        $popupUiaHarness = @"
`$ErrorActionPreference = 'Stop'
trap {
    [Console]::Error.WriteLine("HARNESS_ERROR: `$(`$_.Exception.Message)")
    [Console]::Error.WriteLine(`$_.ScriptStackTrace)
    exit 1
}
Import-Module -Name '$popupUiaHostLiteral' -Force
`$calendarTaskFactory = {
    param([uri] `$Uri)
    [System.Threading.Tasks.Task]::Delay(10000)
}
Start-CountdownApplication -ApplicationPath `$PSCommandPath -HolidayCalendarTaskFactory `$calendarTaskFactory
"@
        [System.IO.File]::WriteAllText($popupUiaEntry, $popupUiaHarness, (New-Object System.Text.UTF8Encoding($false)))
        $popupUiaRequest = New-CountdownStartupRequest -Path $popupUiaRequestPath
        $popupUiaOutputPath = Join-Path $popupUiaRoot 'app.stdout.txt'
        $popupUiaErrorPath = Join-Path $popupUiaRoot 'app.stderr.txt'
        $popupUiaArguments = @('-NoLogo', '-NoProfile', '-NonInteractive', '-STA', '-File', ('"{0}"' -f $popupUiaEntry))
        $popupUiaProcess = Start-Process -FilePath $powerShellEngine -ArgumentList $popupUiaArguments -PassThru -RedirectStandardOutput $popupUiaOutputPath -RedirectStandardError $popupUiaErrorPath
        $popupUiaReceipt = Wait-CountdownWindowReady -Path $popupUiaStatusPath -LaunchToken $popupUiaRequest.launchToken -ExpectedSessionId ([System.Diagnostics.Process]::GetCurrentProcess().SessionId) -ExpectedApplicationPath $popupUiaEntry -TimeoutSeconds 20 -PollMilliseconds 100 -AllowLegacyPowerShellAdapter
        if ($popupUiaReceipt.stage -ne 'window-ready') { throw 'Popup UIA launch did not emit a window-ready receipt.' }

        Add-Type -AssemblyName UIAutomationClient
        Add-Type -AssemblyName UIAutomationTypes
        $uiaProcessId = [int] $popupUiaReceipt.processId
        $uiaProcessCondition = New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::ProcessIdProperty, $uiaProcessId)
        $uiaWindowCondition = New-Object System.Windows.Automation.AndCondition(
            $uiaProcessCondition,
            (New-Object System.Windows.Automation.PropertyCondition(
                [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
                [System.Windows.Automation.ControlType]::Window)))
        $uiaDesktop = [System.Windows.Automation.AutomationElement]::RootElement

        $uiaOpenManagerButton = $null
        $uiaDeadline = [DateTimeOffset]::UtcNow.AddSeconds(10)
        while ($null -eq $uiaOpenManagerButton -and [DateTimeOffset]::UtcNow -lt $uiaDeadline) {
            foreach ($uiaWindow in @($uiaDesktop.FindAll([System.Windows.Automation.TreeScope]::Children, $uiaWindowCondition))) {
                $uiaOpenManagerButton = $uiaWindow.FindFirst(
                    [System.Windows.Automation.TreeScope]::Descendants,
                    (New-Object System.Windows.Automation.PropertyCondition(
                        [System.Windows.Automation.AutomationElement]::NameProperty, '打开管理窗口')))
                if ($null -ne $uiaOpenManagerButton) { break }
            }
            if ($null -eq $uiaOpenManagerButton) { Start-Sleep -Milliseconds 100 }
        }
        if ($null -eq $uiaOpenManagerButton) { throw 'UIA could not find the floating open-manager button in the real app.' }
        $uiaOpenManagerInvoke = $uiaOpenManagerButton.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern) -as [System.Windows.Automation.InvokePattern]
        if ($null -eq $uiaOpenManagerInvoke) { throw 'The floating open-manager button does not expose the UIA invoke pattern.' }
        $uiaOpenManagerInvoke.Invoke()

        $uiaMainWindow = $null
        $uiaDeadline = [DateTimeOffset]::UtcNow.AddSeconds(10)
        while ($null -eq $uiaMainWindow -and [DateTimeOffset]::UtcNow -lt $uiaDeadline) {
            foreach ($uiaWindow in @($uiaDesktop.FindAll([System.Windows.Automation.TreeScope]::Children, $uiaWindowCondition))) {
                if ([string] $uiaWindow.Current.Name -eq '敏敏子倒计时') {
                    $uiaMainWindow = $uiaWindow
                    break
                }
            }
            if ($null -eq $uiaMainWindow) { Start-Sleep -Milliseconds 100 }
        }
        if ($null -eq $uiaMainWindow) { throw 'UIA could not find the real main window after invoking the open-manager button.' }

        $uiaFilterButton = $uiaMainWindow.FindFirst(
            [System.Windows.Automation.TreeScope]::Descendants,
            (New-Object System.Windows.Automation.PropertyCondition(
                [System.Windows.Automation.AutomationElement]::AutomationIdProperty, 'FilterToggleButton')))
        if ($null -eq $uiaFilterButton) { throw 'UIA could not find the main-window filter trigger button in the real app.' }
        $uiaFilterInvoke = $uiaFilterButton.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern) -as [System.Windows.Automation.InvokePattern]
        if ($null -eq $uiaFilterInvoke) { throw 'The main-window filter trigger does not expose the UIA invoke pattern.' }
        $uiaPopupMarkerCondition = New-Object System.Windows.Automation.AndCondition(
            $uiaProcessCondition,
            (New-Object System.Windows.Automation.PropertyCondition(
                [System.Windows.Automation.AutomationElement]::NameProperty, '筛选当前视图')))

        $uiaFilterInvoke.Invoke()
        $uiaPopupOpen = $false
        $uiaDeadline = [DateTimeOffset]::UtcNow.AddSeconds(10)
        while (-not $uiaPopupOpen -and [DateTimeOffset]::UtcNow -lt $uiaDeadline) {
            $uiaPopupOpen = $null -ne $uiaDesktop.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $uiaPopupMarkerCondition)
            if (-not $uiaPopupOpen) { Start-Sleep -Milliseconds 100 }
        }
        if (-not $uiaPopupOpen) { throw 'A UIA invoke on the filter trigger did not open the filter popup in the real app.' }

        $uiaFilterInvoke.Invoke()
        $uiaPopupClosed = $false
        $uiaDeadline = [DateTimeOffset]::UtcNow.AddSeconds(10)
        while (-not $uiaPopupClosed -and [DateTimeOffset]::UtcNow -lt $uiaDeadline) {
            $uiaPopupClosed = $null -eq $uiaDesktop.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $uiaPopupMarkerCondition)
            if (-not $uiaPopupClosed) { Start-Sleep -Milliseconds 100 }
        }
        if (-not $uiaPopupClosed) { throw 'A second UIA invoke on the filter trigger did not close the filter popup in the real app.' }
    }
    finally {
        if ($null -ne $popupUiaProcess -and -not $popupUiaProcess.HasExited) {
            Stop-Process -Id $popupUiaProcess.Id -Force -ErrorAction Stop
            if (-not $popupUiaProcess.WaitForExit(5000)) { throw 'Popup UIA child process did not exit during cleanup.' }
        }
        if (Test-Path -LiteralPath $popupUiaRoot) {
            Remove-Item -LiteralPath $popupUiaRoot -Recurse -Force -ErrorAction Stop
        }
    }
}
else {
    $popupUiaSkipEvidence = 'skipped-non-interactive-session'
    [Console]::Error.WriteLine('UIA-GATE-SKIP: popup-trigger UIA gate skipped because this session is non-interactive ([Environment]::UserInteractive is false, e.g. an office SSH session without an interactive desktop); the gate runs unchanged and fail-closed on interactive desktops.')
}

if (-not $validation.DateInputReadOnly) { throw 'Date selection still accepts free-text input.' }
if (-not $validation.DatePickerButtonOpensCalendar) { throw 'Clicking the date picker drop-down button did not open a calendar popup.' }
if (-not $validation.DatePickerTextOpensCalendar) { throw 'Clicking the date text does not open every themed calendar.' }
if (-not $validation.DatePickerTextClicksUseTheme) { throw 'Clicking the date text is not attached to every themed date picker.' }
if (-not $validation.DatePickerTextOpensThemedPopup) { throw 'Clicking the date text did not open a real themed calendar popup.' }
if ($validation.TimeDropDowns -ne 3) { throw "Expected three time drop-downs, got $($validation.TimeDropDowns)." }
if (-not $validation.CompactSelectionFieldLayout) {
    throw "Short selection fields should stay content-sized and use the compact height: height=$($validation.CountdownInputHeight); default(name=$($validation.CountdownNameWidth), date=$($validation.TargetDateWidth), hour=$($validation.TargetHourWidth), minute=$($validation.TargetMinuteWidth), second=$($validation.TargetSecondWidth), precision=$($validation.DisplayPrecisionWidth)); expanded(date=$($validation.ExpandedTargetDateWidth), hour=$($validation.ExpandedTargetHourWidth), minute=$($validation.ExpandedTargetMinuteWidth), second=$($validation.ExpandedTargetSecondWidth), precision=$($validation.ExpandedDisplayPrecisionWidth))."
}
if (-not $validation.CompactPreferenceFieldLayout) {
    throw "Preference fields should stay compact at default and expanded window widths: height=$($validation.CountdownPreferenceInputHeight); default(precision=$($validation.DisplayPrecisionWidth), salary=$($validation.MonthlySalaryWidth), start=$($validation.WorkStartWidth), end=$($validation.WorkEndWidth)); expanded(precision=$($validation.ExpandedDisplayPrecisionWidth), salary=$($validation.ExpandedMonthlySalaryWidth), start=$($validation.ExpandedWorkStartWidth), end=$($validation.ExpandedWorkEndWidth))."
}
if (-not $validation.CountdownInputsUseProjectTheme) { throw 'Countdown inputs are not using the shared day/night Canvas, PeachLine, and Ink theme resources.' }
if (-not $validation.CompactInputHorizontalPaddingAligned) { throw 'Countdown editor and settings inputs do not share the 16px design-coordinate horizontal padding across text, date, and selection controls.' }
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
    $floatingReferenceBaselineDefinition = Get-Content -LiteralPath $floatingReferenceBaselineManifest -Raw |
        ConvertFrom-Json -Depth 100
    if (-not $validation.FloatingReferenceBaselineRequested -or -not $validation.FloatingReferenceBaselineManifestAvailable) { throw 'The versioned floating reference manifest was not loaded from the source tree.' }
    if (-not $validation.FloatingReferenceBaselineValidated) { throw "The current WPF day/night frames failed the versioned floating reference manifest: $(@($validation.FloatingReferenceBaselineDiagnostics) -join ',')." }
    if ([string] $validation.FloatingReferenceBaselineManifestPath -ne 'assets/ui-references/floating-window/520x390/2026-09-07-v21/manifest.json') { throw 'The floating reference manifest path is not portable and repository-relative.' }
    if ([string] $floatingReferenceBaselineDefinition.baselineVersion -ne '2026-09-07-v21') { throw 'The floating reference manifest version does not match the pinned v21 path.' }
    $expectedBaselineCaptureIds = @('floating-countdowns-day','floating-countdowns-night','notes-search-expanded-day','notes-search-expanded-night') + $expectedLongNoteCaptureIds
    if (@($validation.FloatingReferenceBaselineCaptures).Count -ne 8 -or ((@($validation.FloatingReferenceBaselineCaptures.CaptureId) | Sort-Object) -join ',') -ne (($expectedBaselineCaptureIds | Sort-Object) -join ',')) { throw 'The floating reference manifest does not bind the strict eight-frame inventory.' }
    if (@($validation.FloatingReferenceBaselineGates.PSObject.Properties | Where-Object { -not [bool] $_.Value }).Count -ne 0) { throw 'A concrete floating reference manifest gate is red.' }
    if (@($validation.FloatingReferenceFreshnessGates.PSObject.Properties | Where-Object { -not [bool] $_.Value }).Count -ne 0) { throw 'A concrete long-note freshness gate is red.' }
    if (@($validation.FloatingReferenceBaselineCaptures.BaselineSha256 | Sort-Object -Unique).Count -ne 8) { throw 'The eight-frame manifest reuses a PNG hash.' }
    if (@($floatingReferenceBaselineDefinition.captures).Count -ne 8) { throw 'The pinned floating reference manifest does not declare exactly eight captures.' }
    foreach ($expectedCapture in @($floatingReferenceBaselineDefinition.captures)) {
        $reportedCapture = @($validation.FloatingReferenceBaselineCaptures |
            Where-Object CaptureId -eq ([string] $expectedCapture.captureId))
        if ($reportedCapture.Count -ne 1 -or
            [string] $reportedCapture[0].Path -ne [string] $expectedCapture.path -or
            [string] $reportedCapture[0].BaselineSha256 -ne [string] $expectedCapture.pngSha256 -or
            [string] $reportedCapture[0].ActualSha256 -ne [string] $expectedCapture.pngSha256) {
            throw "The floating capture is not bound to its v21 manifest entry: $($expectedCapture.captureId)."
        }
    }
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
if (-not $validation.FloatingNoteSearchScenariosPass) { throw 'The floating note search scenarios did not pass.' }
$floatingNoteSearch = $validation.FloatingNoteSearch
if (-not $floatingNoteSearch.HiddenInCountdowns -or -not $floatingNoteSearch.VisibleInNotes) { throw 'The floating search button is not exclusive to the notes workspace.' }
if (-not $floatingNoteSearch.SearchButtonShowsClosedMagnifier) { throw 'The floating search button does not render a closed magnifier glyph.' }
if (-not $floatingNoteSearch.ExpandsAndFocuses -or -not $floatingNoteSearch.FiltersRenderedIds -or -not $floatingNoteSearch.CollapseKeepsTerm -or -not $floatingNoteSearch.HintReexpands -or -not $floatingNoteSearch.EscapeClears) { throw 'The floating collapsed-search main path (expand, filter, hint, Esc) did not pass.' }
if (-not $floatingNoteSearch.SearchRefreshLeavesPlacementUnchanged) { throw 'Assigning a floating note search term moved Left/Top even though ActualWidth/ActualHeight were unchanged.' }
if (-not $floatingNoteSearch.EmptyState -or -not $floatingNoteSearch.EmptyDoesNotOverlap -or -not $floatingNoteSearch.ClearRestores) { throw 'The floating search empty state, overlap, or clear-restore contract did not pass.' }
if (-not $floatingNoteSearch.AddThenSearchTab -or -not $floatingNoteSearch.SearchBeforeFirstCard) { throw 'The floating search Tab order contract did not pass.' }
if (-not $floatingNoteSearch.CtrlFFromCountdowns) { throw 'Ctrl+F did not switch the floating window to notes search.' }
if (-not $floatingNoteSearch.UsesQuickAddTheme) { throw 'The floating search input does not follow local quick-add theme tokens.' }
if (-not $validation.NoteSearchSyncsFromSharedFilter) { throw 'The main-window search box does not sync from the shared filter.' }
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
if (-not $validation.FloatingTrayFailedRestoreKeepsTrayStateAndAllowsRetry) { throw 'A failed restore determination hides the notification-area icon or blocks retry.' }
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
if (-not $validation.ReminderRulesEditorSharedEntryWorks) { throw 'Main and floating note flows do not expose the shared reminder-rules editor entry.' }
if (-not $validation.FloatingReminderRulesEntryStaysCompact) { throw 'Floating reminder-rules entry expanded the fixed 520 x 390 surface.' }
if (-not $validation.NoteReminderDispatchWorks) { throw 'Sticky-note reminders were not dispatched exactly once per due interval.' }
if (-not $validation.ReminderRequiresContentRenderedConfirmation) { throw 'A reminder was confirmed without a real ContentRendered boundary.' }
if (-not $validation.ReminderPersistenceFailureStaysVisible) { throw 'A reminder did not stay visible with an explicit persistence failure state.' }
if (-not $validation.ReminderPersistenceRetryBackoffWorks) { throw 'Reminder persistence retry did not follow the 5/10/20/30-second bounded backoff.' }
if (-not $validation.ReminderDeliverySingleFlightWorks) { throw 'A reminder occurrence created duplicate windows or persistence retries.' }
if (-not $validation.ReminderIndependentOccurrencesWork) { throw 'One failing reminder occurrence blocked another due reminder from rendering and confirming.' }
if (-not $validation.ReminderPersistenceFailureCloseGuardWorks) { throw 'A persistence-failed reminder allowed a normal close to bypass its retry state.' }
if (-not $validation.RepeatingReminderRenderedLatchWorks) { throw 'A repeating reminder cycle was latched before real rendering or duplicated afterward.' }
if (-not $validation.RepeatingReminderDistinctCyclesCoexist) { throw 'Distinct repeating-reminder cycles did not retain separate windows or duplicated the current cycle.' }
if (-not $validation.ReminderPersistenceFailureDisposeWorks) { throw 'Coordinator disposal did not force-close a persistence-failed reminder without retrying it.' }
if (-not $validation.ReminderDiagnosticsExcludePrivatePayload) { throw 'Reminder failure diagnostics exposed note text or a complete settings payload.' }
if (-not $validation.ReminderCenterIsThemed) { throw 'Reminder center did not use the warm non-blocking application theme.' }
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
if (-not $validation.PopupTriggerSecondClickCloses) { throw 'Clicking a popup trigger a second time reopens the popup instead of closing it.' }
if (-not $validation.PopupEscapeFromTriggerClosesAndKeepsFocus) { throw 'Escape on a popup trigger does not close the popup and keep trigger focus.' }
if (-not $validation.NoteEmptyStatesDistinguished) { throw 'The first-use sticky-note empty state is indistinguishable from filtered no-results.' }
if (-not $validation.NoteNoResultsResetWorks) { throw 'The filtered no-results state cannot reset all sticky-note filters.' }
if (-not $validation.CountdownEmptyStatesDistinguished) { throw 'Countdown first-use and filtered no-results empty states are not distinguished.' }
if (-not $validation.CountdownNoResultsResetWorks -or -not $validation.CountdownEmptyPointerResetRestoresResults) { throw 'The countdown filtered no-results pointer reset does not restore default filters, rows, and summary.' }
if (-not $validation.CountdownEmptyKeyboardResetRestoresResults) { throw 'The focused countdown filtered no-results reset cannot restore results through keyboard activation.' }
if (-not $validation.CountdownEmptyStateTextContrast) { throw 'Countdown empty-state text does not meet 4.5:1 contrast against its actual day/night backdrop.' }
$countdownEmptyContrastMeasurements = @($validation.CountdownEmptyStateContrastMeasurements)
if ($countdownEmptyContrastMeasurements.Count -ne 2 -or ((@($countdownEmptyContrastMeasurements.Theme) | Sort-Object) -join ',') -ne 'Day,Night') { throw 'Countdown empty-state contrast must contain exact day/night measurements.' }
if (@($countdownEmptyContrastMeasurements | Where-Object { [double] $_.ContrastRatio -lt 4.5 -or -not $_.ForegroundUsesMutedBrush -or -not $_.ActualBackgroundMatchesBackdrop -or -not $_.TransparentAncestors -or -not $_.TextWithinBackdrop }).Count -ne 0) { throw 'A countdown empty-state contrast measurement did not use the actual visible backdrop or expected resource bindings.' }
if (-not $validation.CountdownEmptyStateGateIsolationBehavior) { throw 'The countdown empty-state gates cannot fail independently.' }
if (-not $validation.NoteStatusCountsUseMutedToken) { throw 'Note status section counts do not use the MutedBrush auxiliary token in both day and night themes.' }
if (-not $validation.NoteEmptyStateTextContrast) { throw 'Note empty-state text does not meet 4.5:1 contrast against its actual day/night backdrop or lost the MutedBrush token.' }
$noteEmptyContrastMeasurements = @($validation.NoteEmptyStateContrastMeasurements)
if ($noteEmptyContrastMeasurements.Count -ne 2 -or ((@($noteEmptyContrastMeasurements.Theme) | Sort-Object) -join ',') -ne 'Day,Night') { throw 'Note empty-state contrast must contain exact day/night measurements.' }
if (@($noteEmptyContrastMeasurements | Where-Object { [double] $_.ContrastRatio -lt 4.5 -or -not $_.ForegroundUsesMutedBrush -or -not $_.ActualBackgroundMatchesBackdrop -or -not $_.TransparentAncestors -or -not $_.TextWithinBackdrop }).Count -ne 0) { throw 'A note empty-state contrast measurement did not use the actual visible backdrop or the MutedBrush token.' }
if (-not $validation.NoteVisualTokenGateIsolationBehavior) { throw 'The note visual-contract gates cannot fail independently.' }
if (-not $validation.CountdownRemainingLayoutMatches) { throw 'Main-window countdown remaining text does not use the fixed tabular single-line column.' }
if (-not $validation.CountdownIncrementalRefreshIdentityMatches) { throw 'Countdown clock ticks rebuilt row controls instead of updating existing text targets.' }
if (-not $validation.CountdownExpiryVisualsFlipTogether) { throw 'Countdown expiry did not flip both the card background and remaining-text danger color.' }
if (-not $validation.CountdownIncrementalRefreshGateIsolationBehavior) { throw 'Countdown incremental-refresh negative fixtures are not isolated.' }
$countdownIncrementalRefresh = $validation.CountdownIncrementalRefresh
if (-not $countdownIncrementalRefresh.FocusPreserved -or -not $countdownIncrementalRefresh.NonZeroScrollPreserved -or -not $countdownIncrementalRefresh.PointerStatePreserved) { throw 'Countdown ordinary tick did not preserve focus, non-zero scroll, and captured pointer state.' }
if (-not $countdownIncrementalRefresh.TriggerMatrixRebuilt -or -not $countdownIncrementalRefresh.CollectionChangeRebuilt -or -not $countdownIncrementalRefresh.FilterChangeRebuilt -or -not $countdownIncrementalRefresh.PrecisionChangeRebuilt -or -not $countdownIncrementalRefresh.ThemeChangeRebuilt -or -not $countdownIncrementalRefresh.ExpiryChangeRebuilt) { throw 'Countdown collection/filter/precision/theme/expiry trigger matrix did not rebuild correctly.' }
if (-not $countdownIncrementalRefresh.ExpiryBackgroundFlipped -or -not $countdownIncrementalRefresh.ExpiryRemainingTextFlipped) { throw 'Countdown expiry background and remaining-text gates did not both pass independently.' }
if (-not $validation.CountdownRemainingUnitSpacingMatches) { throw 'Main-window countdown remaining text lost FormatRemaining unit spacing or sample coverage.' }
if (-not $validation.CountdownRemainingGateIsolationBehavior) { throw 'Main-window countdown remaining gates cannot fail independently.' }
$mainRemainingSamples = @($validation.CountdownRemainingSamples)
if ($mainRemainingSamples.Count -ne 5) { throw "Expected 5 main-window remaining samples, got $($mainRemainingSamples.Count)." }
if (@($mainRemainingSamples | Where-Object { $_.Precision -notin @('Day','Hour','Minute','Second','SecondLongDays') -or -not $_.IsRightAligned -or -not $_.IsSingleLineAndUntrimmed -or -not $_.IsFullyVisible -or -not $_.UsesTabularNumerals -or -not $_.PreferenceSaved }).Count -ne 0) { throw 'A main-window remaining-time precision sample failed the fixed-column visual contract.' }
if (@($mainRemainingSamples | Where-Object { [Math]::Abs([double] $_.ColumnWidth - 300) -gt 0.01 -or [Math]::Abs([double] $_.ActualColumnWidth - 300) -gt 0.5 }).Count -ne 0) { throw 'A main-window remaining-time sample expanded outside its actual 300px fixed column.' }
if ((@($mainRemainingSamples)[0..3].FontSize -join ';') -ne '20;20;20;20') { throw 'A normal main-window precision unexpectedly reduced the remaining-time font size.' }
if ([double] @($mainRemainingSamples)[4].FontSize -lt 14 -or [double] @($mainRemainingSamples)[4].FontSize -ge 20) { throw 'The long-day main-window sample did not use bounded dynamic font fitting.' }
if (-not $validation.MainNoteCompletionTargetSizeAndBehavior) { throw 'Main-window note completion checkbox does not meet its 24px template and interaction contract.' }
if (-not $validation.MainNoteCompletionOtherUserSizesUnchanged) { throw 'Main-window note completion styling changed TagSelector or FloatingWindow checkbox dimensions.' }
if (-not $validation.MainNoteCompletionGateIsolationBehavior) { throw 'Main-window note completion target and isolation gates cannot fail independently.' }
$mainNoteCompletionMeasurements = @($validation.MainNoteCompletionMeasurements)
if ($mainNoteCompletionMeasurements.Count -ne 3 -or ((@($mainNoteCompletionMeasurements.User) | Sort-Object) -join ',') -ne 'FloatingWindow,MainWindow,TagSelector') { throw 'Main note completion validation must report three real WPF checkbox users.' }
$mainNoteCompletionMeasurement = @($mainNoteCompletionMeasurements | Where-Object User -eq 'MainWindow')[0]
if ([double] $mainNoteCompletionMeasurement.BoxWidth -lt 24 -or [double] $mainNoteCompletionMeasurement.BoxHeight -lt 24 -or [double] $mainNoteCompletionMeasurement.TickWidth -lt 14 -or [double] $mainNoteCompletionMeasurement.TickHeight -lt 10 -or -not $mainNoteCompletionMeasurement.UsesDedicatedStyle -or -not $mainNoteCompletionMeasurement.UsesWarmTokens -or [double] $mainNoteCompletionMeasurement.CornerRadius -lt 6) { throw 'Main-window note completion template is smaller than the accepted box/tick geometry or lost its scoped warm style.' }
if (@($mainNoteCompletionMeasurements | Where-Object { $_.User -ne 'MainWindow' -and ([math]::Abs([double] $_.BoxWidth - 18) -gt 0.5 -or [math]::Abs([double] $_.BoxHeight - 18) -gt 0.5 -or [math]::Abs([double] $_.TickWidth - 10) -gt 0.5 -or [math]::Abs([double] $_.TickHeight - 7) -gt 0.5 -or $_.UsesDedicatedStyle) }).Count -ne 0) { throw 'A non-main-window checkbox no longer uses the unchanged 18px global template.' }
$mainNoteCompletionInteraction = $validation.MainNoteCompletionInteractionBehavior
if (-not $mainNoteCompletionInteraction.Checked -or -not $mainNoteCompletionInteraction.Unchecked -or -not $mainNoteCompletionInteraction.PersistenceFailureRolledBack -or -not $mainNoteCompletionInteraction.AccessibleNamesUpdated -or -not $mainNoteCompletionInteraction.AccessibleNamesHaveNoLineBreaks) { throw 'Main-window note completion check, uncheck, rollback, accessible-name, or newline-injection behavior failed.' }
if (-not $validation.MainNoteCompletionStyleScoped) { throw 'Compatibility note completion style gate must remain true.' }
if (-not $validation.TransientPanelsOnDemand) { throw 'Filters, tag pickers, or tag management do not use the required on-demand interaction flow.' }
if (-not $validation.WorkspaceDraftsRetained) { throw 'Switching workspaces discarded an unsaved countdown or sticky-note draft.' }
if (-not $validation.SettingsDraftsRetainedOnRefresh) { throw 'A background state refresh discarded unsaved settings-area edits (precision, salary, or work hours).' }
if (-not $validation.SettingsThemeSelectionRetainedOnRefresh) { throw 'A background state refresh reset the theme tri-state button selection.' }
if (-not $validation.SettingsSavedValuesReflectedAfterSave) { throw 'The settings area did not reflect persisted values immediately after a successful save.' }
if (-not $validation.NegativeMonthlySalaryRejectedKeepsDraft) { throw 'A negative monthly salary was not rejected with the original preferences and draft text kept.' }
if (-not $validation.ZeroMonthlySalaryStillSavesAsUnset) { throw 'A zero monthly salary no longer saves as the unset state.' }
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
if ($mainReferenceBaselineRequired) {
    if (-not $validation.MainDaySingleBorderGate) { throw 'The main-window control frame is not a single border after excluding declared ornament bands.' }
    if (-not $validation.MainDaySingleBorderGateIsolationBehavior) { throw 'The main-window single-border negative fixtures cannot fail independently.' }
    if ((@($validation.MainDaySingleBorderNegativeFixtures) -join ',') -ne 'unexpected-control-edge,label-intersection,missing-lower-ornament') { throw 'The main-window single-border negative-fixture inventory changed.' }
    $singleBorderMeasurements = @($validation.MainDaySingleBorder.Measurements | Sort-Object StateId)
    if ((@($singleBorderMeasurements.StateId) -join ',') -ne 'Countdown-Day,Countdown-Night,Notes-Day,Notes-Night') { throw 'The main-window single-border measurement matrix is incomplete.' }
    if (@($singleBorderMeasurements | Where-Object { -not $_.ExactControlEdges -or -not $_.LabelsClear -or -not $_.LowerDecorationPresent -or [int] $_.LowerDecorationPixelCount -le 0 }).Count -ne 0) { throw 'A main-window state failed its observed border, label-clearance, or declared lower-ornament measurement.' }
    if ((@($singleBorderMeasurements | ForEach-Object { "$($_.StateId):$(@($_.EdgeRows) -join '/'):$($_.ControlScanTop):$($_.DeclaredBottomBandStart)" }) -join ';') -ne 'Countdown-Day:515/516/570/571:480:1079;Countdown-Night:515/516/570/571:480:1079;Notes-Day:448/449/515/516:416:845;Notes-Night:448/449/515/516:416:845') { throw 'The main-window control-edge rows or shared ornament-band boundaries drifted.' }
    if (-not $validation.MainNightDerivationConsistencyGate -or -not $validation.MainNightDerivationConsistency.ByteIdentical -or -not $validation.MainNightDerivationConsistency.AlphaRangesMatch) { throw 'The checked-in night decoration assets no longer match an isolated derivation.' }
    if ((@($validation.MainNightDerivationConsistency.AlphaRanges | Sort-Object Scene | ForEach-Object { "$($_.Scene):$($_.MinimumY)-$($_.MaximumY):$($_.MatchesExpected)" }) -join ';') -ne 'countdown:260-395:True;notes:264-415:True') { throw 'The night decoration alpha ranges moved.' }
}
if ((@($validation.MainNightReferenceWaveColumnCounts) -join ',') -ne '1094,1080') { throw 'The main-window night wave contour is not continuous across the reference width.' }
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
if (-not $validation.FloatingQuickAddDayPaletteBindingsMatch) { throw 'Floating quick-add day controls do not bind to the dedicated day palette tokens.' }
if (-not $validation.FloatingQuickAddNightPaletteBindingsMatch) { throw 'Floating quick-add night controls do not bind to the dedicated night palette tokens.' }
if (-not $validation.FloatingQuickAddPaletteBindingIsolationBehavior) { throw 'Floating quick-add palette bindings cannot fail one control family at a time.' }
$quickAddPaletteBindings = @($validation.FloatingQuickAddPaletteBindings)
if ($quickAddPaletteBindings.Count -ne 2 -or ((@($quickAddPaletteBindings.Theme) | Sort-Object) -join ',') -ne 'Day,Night') { throw 'Floating quick-add palette report does not contain exact day/night bindings.' }
$expectedQuickAddPaletteKeys = @{
    Day = 'FloatingDayNoteInputBrush,FloatingDayNoteInputTextBrush,FloatingDayErrorBrush,FloatingDayNoteInputTextBrush,FloatingDayErrorBrush'
    Night = 'FloatingNightNoteInputBrush,FloatingNightNoteInputTextBrush,FloatingNightErrorBrush,FloatingNightNoteInputTextBrush,FloatingNightErrorBrush'
}
$quickAddPaletteGateNames = @(
    'InputSurfaceMatches',
    'InputTextMatches',
    'ErrorTextMatches'
)
foreach ($binding in $quickAddPaletteBindings) {
    $expectedKeys = $expectedQuickAddPaletteKeys[[string] $binding.Theme]
    $reportedExpected = @(
        $binding.Expected.InputSurfaceResourceKey,
        $binding.Expected.InputTextResourceKey,
        $binding.Expected.ErrorTextResourceKey,
        $binding.Expected.ReminderTextResourceKey,
        $binding.Expected.ReminderErrorResourceKey
    ) -join ','
    $reportedActual = @(
        $binding.Actual.InputSurfaceResourceKey,
        $binding.Actual.InputTextResourceKey,
        $binding.Actual.ErrorTextResourceKey,
        $binding.Actual.ReminderTextResourceKey,
        $binding.Actual.ReminderErrorResourceKey
    ) -join ','
    if ($reportedExpected -ne $expectedKeys -or $reportedActual -ne $expectedKeys) { throw "Floating quick-add $($binding.Theme) expected/actual resource keys are not fixed to the dedicated palette: expected=[$reportedExpected], actual=[$reportedActual]." }
    if (@($quickAddPaletteGateNames | Where-Object { -not [bool] $binding.Gates.$_ }).Count -ne 0 -or
        -not [bool] $binding.Gates.Passed -or
        @($binding.Gates.FailedChecks).Count -ne 0) {
        throw "Floating quick-add $($binding.Theme) has a failed concrete palette-binding gate."
    }
}
$quickAddPaletteFailedChecksFixture = [pscustomobject]@{
    InputSurfaceMatches = $true
    InputTextMatches = $true
    ErrorTextMatches = $true
    Passed = $true
    FailedChecks = @('InputSurfaceMatches')
}
$quickAddPaletteFailedChecksFixturePassed =
    @($quickAddPaletteGateNames | Where-Object {
            -not [bool] $quickAddPaletteFailedChecksFixture.$_
        }).Count -eq 0 -and
    [bool] $quickAddPaletteFailedChecksFixture.Passed -and
    @($quickAddPaletteFailedChecksFixture.FailedChecks).Count -eq 0
if ($quickAddPaletteFailedChecksFixturePassed) { throw 'A non-empty floating quick-add palette FailedChecks fixture was accepted.' }
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
$sourceLabelContrast = $validation.ReminderCenterSourceLabelContrast
if (-not $sourceLabelContrast.TextContrast -or -not $sourceLabelContrast.UsesInkBrush -or -not $sourceLabelContrast.EmphasisPreserved -or -not $sourceLabelContrast.NoteCardBackground) {
    throw 'Reminder-center source labels do not meet InkBrush, emphasis, or 4.5:1 note-card contrast in day and night.'
}
$sourceLabelMeasurements = @($sourceLabelContrast.Measurements)
if ($sourceLabelMeasurements.Count -ne 4 -or ((@($sourceLabelMeasurements.Theme) | Sort-Object -Unique) -join ',') -ne 'Day,Night') {
    throw 'Reminder-center source-label contrast must contain day/night measurements for both source kinds.'
}
if (@($sourceLabelMeasurements | Where-Object { [double] $_.ContrastRatio -lt 4.5 -or -not $_.UsesInkBrush -or -not $_.EmphasisPreserved -or -not $_.NoteCardBackground }).Count -ne 0) {
    throw 'A reminder-center source-label measurement failed InkBrush, chapter emphasis, or 4.5:1 contrast against the note card.'
}
$dialogContrast = $validation.SystemDialogPrimaryButtonContrast
if (-not $dialogContrast.TextContrast -or -not $dialogContrast.UsesDaytimeMainActionColors) {
    throw 'Settings-restore or activation-failure primary buttons do not use daytime main-action #A24438 with primary button text at 4.5:1.'
}
$dialogMeasurements = @($dialogContrast.Measurements)
if ($dialogMeasurements.Count -ne 2 -or ((@($dialogMeasurements.Window) | Sort-Object) -join ',') -ne 'InstanceActivationFailure,SettingsRecovery') {
    throw 'System-dialog primary-button contrast must measure the settings-restore and activation-failure windows.'
}
if (@($dialogMeasurements | Where-Object { [double] $_.ContrastRatio -lt 4.5 -or [string] $_.Background -ne '#FFA24438' -or [string] $_.Foreground -ne '#FFFFFDFC' }).Count -ne 0) {
    throw 'A system-dialog primary button is not #A24438 on #FFFDFC at 4.5:1.'
}
$nightFocus = $validation.NightKeyboardFocusContrast
if (-not $nightFocus.NightPairsMeetContrast -or -not $nightFocus.RingsUseExpectedTokens -or -not $nightFocus.PinKeepsWarmGold -or -not $nightFocus.HoverTokenDistinctAtNight) {
    throw 'Night keyboard focus rings do not use warm-gold at 3:1, or day/pin/hover semantics drifted.'
}
$nightFocusMeasurements = @($nightFocus.Measurements)
if ($nightFocusMeasurements.Count -ne 3 -or ((@($nightFocusMeasurements.Surface) | Sort-Object) -join ',') -ne 'Canvas,Note,NoteStatusHeader') {
    throw 'Night keyboard-focus contrast must measure canvas, status header, and note-card pairs.'
}
if (@($nightFocusMeasurements | Where-Object { [double] $_.ContrastRatio -lt 3 }).Count -ne 0) {
    throw 'A night keyboard-focus pair is below the 3:1 non-text contrast gate.'
}
$focusRings = @($nightFocus.Rings)
if ($focusRings.Count -ne 2 -or ((@($focusRings.Theme) | Sort-Object) -join ',') -ne 'Day,Night') {
    throw 'Keyboard focus-ring measurements must contain exact day/night samples.'
}
if (@($focusRings | Where-Object { -not $_.UsesExpectedToken -or -not $_.RingVisible }).Count -ne 0) {
    throw 'A keyboard focus ring is missing or not bound to ActiveBrush by day / MainActionFocusBrush by night.'
}
$comboClick = $validation.ComboBoxTextClick
if (-not $comboClick.TextClickOpens -or -not $comboClick.ArrowPressOpens -or -not $comboClick.KeyboardPathsWork -or -not $comboClick.DisabledClickDoesNotOpen -or -not $comboClick.PopupItemClickSelects) {
    throw 'ComboBox text-surface click, arrow Press, keyboard, disabled, or popup item paths failed.'
}
if ([int] $comboClick.ComboCount -lt 14) {
    throw "ComboBox text-click coverage is incomplete: $($comboClick.ComboCount)"
}
if (@($comboClick.Measurements | Where-Object { -not $_.ReleaseOpened -or -not $_.Attached -or -not $_.ArrowColumnUnchanged }).Count -ne 0) {
    throw 'A production ComboBox did not open from its text surface or changed arrow-column geometry.'
}
if (-not $comboClick.ArrowIsNotTabStop -or -not $validation.ComboBoxArrowIsNotTabStop -or @($comboClick.Measurements | Where-Object { -not $_.ArrowIsNotTabStop }).Count -ne 0) {
    throw 'A production ComboBox arrow remains a Tab stop or can take keyboard focus.'
}
$secondaryDrag = $validation.SecondaryWindowHeaderDrag
if (-not $secondaryDrag.HeaderRequestsDrag -or -not $secondaryDrag.ManualPositionPersists -or -not $secondaryDrag.InteractiveControlsDoNotDrag -or -not $secondaryDrag.ReminderCenterFirstAppearanceTopRight -or -not $secondaryDrag.CloseCancelledWithItems -or -not $secondaryDrag.ResizeGripUnchanged) {
    throw 'Reminder-center or rules-editor header drag, first-show placement, close-cancel, or resize-grip contract failed.'
}
if (-not $validation.CalendarNavigationAccessibleNames) {
    throw "Calendar month buttons are not named 上一个月/下一个月 (previous='$($validation.CalendarPreviousMonthAccessibleName)', next='$($validation.CalendarNextMonthAccessibleName)')."
}
if ([string] $validation.DatePickerOpenCalendarAccessibleName -ne '打开日历' -or -not $validation.DatePickerOpenCalendarAccessibleNames) {
    throw "DatePicker open-calendar buttons are unnamed (name='$($validation.DatePickerOpenCalendarAccessibleName)')."
}
if (-not $validation.CountdownCardActionsIncludeRecordName) {
    throw 'Countdown card edit/delete accessible names do not include the record name.'
}
if (-not $validation.SettingsRecoveryFailureExitIsDefault) {
    throw 'Settings-recovery failure Exit is not the Enter default button.'
}
if (-not $validation.RuleEditorInlineDeleteUsesDangerButton) {
    throw 'Rules-editor inline Delete does not use DangerButton.'
}
if (-not $validation.SettingsRecoverySecondaryUsesCanvas) {
    throw 'Settings-recovery secondary buttons are not static warm canvas #FFFBF7.'
}
if (-not $validation.ReminderCenterSnoozeRowAlignsWithActions) {
    throw 'Reminder-center actions are not a single right-aligned row in DESIGN order.'
}
if (-not $validation.PopupAnimationIsInstant) {
    throw 'A production popup still uses system fade animation instead of appearing instantly.'
}
if (-not $validation.ActionForegroundsUseMainActionText) {
    throw 'PrimaryButton, CheckBox tick, or calendar selected foreground does not use MainActionTextBrush.'
}
if (-not $validation.DisabledSelectionControlsUseButtonOpacity) {
    throw 'Disabled CheckBox/ComboBox surface opacity is not 0.45.'
}
if (-not $validation.FloatingTypographyWeightWhitelist) {
    throw 'Floating-window text uses a font weight outside the DESIGN section 3 Regular/SemiBold whitelist.'
}
if (-not $validation.FloatingDateStampTypographyMatches) {
    throw 'Floating date-stamp typography does not match DESIGN section 3 Regular/SemiBold, fallback fonts, tabular numerals, or 18px rule spacing.'
}
if (-not $validation.FloatingDateStampDensityMatches) {
    throw 'Floating date-stamp padding or inter-line gaps remain cramped.'
}
if (-not $validation.NoteCancelFitsEditorPanel) {
    throw 'Note-edit Cancel still overhangs the editor panel or is not aligned with Save.'
}
if (-not $validation.PopupRightInsetIsEightFromCanvas) {
    throw 'Filter, tag-manager, or tag-selector popup is not inset 8px from the content canvas inner edge.'
}
if (-not $validation.FilterPopupScrollsLongTagList) {
    throw 'Filter popup does not cap height and scroll a long tag list.'
}
if (-not $validation.MainCheckboxHitCoversSlot) {
    throw 'Main-window note checkbox hit box does not cover the 30px slot or lost Tag=note.Id.'
}
if (-not $validation.FloatingSearchSlotRightIs396) {
    throw 'Floating search slot right edge is not aligned to x=396.'
}
if (-not $validation.FloatingSearchHintMatchesInputHeight) {
    throw 'Floating search hint row is not the same height as the input row, or expanding search jumps the viewport.'
}
if (-not $validation.FloatingQuickAddAlignsWithNoteCards) {
    throw 'Floating quick-add right edge is not aligned with the measured note-card column.'
}
if (-not $validation.FloatingCheckboxHitCoversSlot) {
    throw 'Floating note checkbox hit box does not cover the 34px slot, lost Tag=note.Id, or covers the date stamp.'
}
if ($validation.NightKeyboardFocusBaselineFramesAffected) {
    throw 'Night keyboard-focus pixel baseline frames were marked affected without regenerating manifests.'
}
if ([string] $validation.NightKeyboardFocusBaselineEvidence -notmatch '2026-09-07-v21') {
    throw 'Night keyboard-focus baseline evidence does not record the regenerated 2026-09-07-v21 frames.'
}
if (-not $validation.AuditContrastGateIsolationBehavior) { throw 'Audit contrast negative fixtures cannot fail independently.' }
if (-not $validation.DangerButtonContrast) { throw 'The destructive confirmation button loses required text contrast when hovered or pressed.' }
if (-not $validation.DestructiveConfirmationIsSafe) { throw 'A destructive confirmation still makes deletion the Enter default action.' }
if (-not $validation.MainInitialFocusIsSafeAndDeterministic) { throw 'The main window has no deterministic safe initial focus contract.' }
if (-not $validation.MainTabSequenceStartsWithWorkspaceControls) { throw 'The main window workspace controls are not keyboard-traversable in order.' }
if (-not $validation.MainKeyboardNavigationGates.InitialFocusSafe) { throw 'The main-window Loaded focus is not the observed safe initial focus.' }
if (-not $validation.MainKeyboardNavigationGates.CompleteTabSequence) { throw 'The main-window Countdowns/Notes Tab paths are not complete, symmetric, and visibly focused.' }
if (-not $validation.MainKeyboardNavigationGateIsolationBehavior) { throw 'The main-window initial-focus and complete-Tab gates cannot fail independently.' }
if (-not $validation.MainKeyboardPathNegativeFixtures) { throw 'The complete Tab path negative fixtures did not reject a missing tag trigger, reordered target, asymmetric reverse path, or invisible focus.' }
if (-not $validation.MainStructuralLiveRegionsUsePoliteSemantics) { throw 'Structural main-window announcements are not carried by polite visible live regions.' }
if (-not $validation.MainShortcutScopeIsSafe) { throw 'Main-window shortcuts do not guard text-entry and popup scopes.' }
if ([string] $validation.MainKeyboardBehavior.InitialFocusedAutomationId -ne 'CountdownTab') { throw 'The main window did not actually focus the countdown workspace after Loaded.' }
if ((@($validation.MainKeyboardBehavior.TabSequence) -join ',') -ne 'CountdownTab,NotesTab,FilterButton,TagManagerButton') { throw 'The main-window real Tab prefix does not follow the visual workspace order.' }
$countdownTabPath = $validation.MainKeyboardBehavior.CountdownTabPath
$expectedCountdownTabPath = 'ThemeModeButton,TopmostCheck,MinimizeButton,CloseButton,CountdownTab,NotesTab,FilterButton,TagManagerButton,CountdownName,CountdownDate,CountdownHour,CountdownMinute,CountdownSecond,SetNowButton,CountdownTags,ManageCountdownReminderRulesButton,SaveCountdownButton,DisplayPrecision,MonthlySalary,WorkStart,WorkEnd,SavePreferencesButton,CountdownListRegion,StatusText'
$expectedCountdownTabPathBackward = [string[]] ($expectedCountdownTabPath -split ',')
[array]::Reverse($expectedCountdownTabPathBackward)
if ((@($countdownTabPath.ExpectedSequence) -join ',') -ne $expectedCountdownTabPath -or (@($countdownTabPath.ForwardSequence) -join ',') -ne $expectedCountdownTabPath -or (@($countdownTabPath.BackwardSequence) -join ',') -ne ($expectedCountdownTabPathBackward -join ',') -or -not $countdownTabPath.FocusVisualsVisible) { throw 'The Countdowns workspace real Tab path is incomplete, asymmetric, or missing a visible focus style.' }
$expectedCountdownFocusedTargets = [string[]] ($expectedCountdownTabPath -split ',')
if (-not (Test-ExactSymmetricTabTargets `
        -ExpectedForward $expectedCountdownFocusedTargets `
        -ActualForward @($countdownTabPath.ForwardFocusedTargets) `
        -ActualBackward @($countdownTabPath.BackwardFocusedTargets))) { throw 'The empty Countdowns workspace focus targets were not traversed symmetrically.' }
$notesTabPath = $validation.MainKeyboardBehavior.NotesTabPath
$expectedNotesTabPath = 'ThemeModeButton,TopmostCheck,MinimizeButton,CloseButton,CountdownTab,NotesTab,FilterButton,TagManagerButton,NoteText,NoteDate,SaveNoteButton,ManageNoteReminderRulesButton,NoteTags,NoteSearch,NotesListRegion,StatusText'
$expectedNotesTabPathBackward = [string[]] ($expectedNotesTabPath -split ',')
[array]::Reverse($expectedNotesTabPathBackward)
if ((@($notesTabPath.ExpectedSequence) -join ',') -ne $expectedNotesTabPath -or (@($notesTabPath.ForwardSequence) -join ',') -ne $expectedNotesTabPath -or (@($notesTabPath.BackwardSequence) -join ',') -ne ($expectedNotesTabPathBackward -join ',') -or -not $notesTabPath.FocusVisualsVisible) { throw 'The Notes workspace real Tab path is incomplete, asymmetric, or missing a visible focus style.' }
$expectedNotesFocusedTargets = [string[]] ($expectedNotesTabPath -split ',')
if (-not (Test-ExactSymmetricTabTargets `
        -ExpectedForward $expectedNotesFocusedTargets `
        -ActualForward @($notesTabPath.ForwardFocusedTargets) `
        -ActualBackward @($notesTabPath.BackwardFocusedTargets))) { throw 'The empty Notes workspace focus targets were not traversed symmetrically.' }
if ((Test-ExactSymmetricTabTargets `
        -ExpectedForward $expectedCountdownFocusedTargets `
        -ActualForward $expectedCountdownFocusedTargets `
        -ActualBackward $expectedCountdownFocusedTargets) -or
    (Test-ExactSymmetricTabTargets `
        -ExpectedForward $expectedNotesFocusedTargets `
        -ActualForward $expectedNotesFocusedTargets `
        -ActualBackward $expectedNotesFocusedTargets)) {
    throw 'An asymmetric main-window focused-target fixture was accepted.'
}
if (-not (Test-MainShortcutCommandResults -CommandResults $validation.MainKeyboardBehavior.CommandResults)) { throw 'The real main-window shortcut event path does not cover the exact eight commands.' }
if (-not (Test-MainShortcutCommandDiagnostics -CommandDiagnostics $validation.MainKeyboardBehavior.CommandDiagnostics)) { throw 'A main-window shortcut was not both handled and observed through its real effect.' }
if ([string] $validation.MainKeyboardBehavior.CommandDiagnostics.SaveCountdown.StatusText -ne '倒计时已保存。' -or [string] $validation.MainKeyboardBehavior.CommandDiagnostics.SavePreferences.StatusText -ne '偏好已保存。') { throw 'The save shortcuts did not report their real persisted success status.' }
if (-not $validation.MainKeyboardBehavior.CommandsWork -or -not $validation.MainKeyboardBehavior.TextInputPreserved -or -not $validation.MainKeyboardBehavior.PopupGuarded -or -not $validation.MainKeyboardBehavior.ModalOwnerGuarded) { throw 'The real main-window shortcut behavior does not cover commands, text input, popup guards, and disabled modal owners.' }
if (@($validation.MainKeyboardBehavior.InputGuards.PSObject.Properties | Where-Object { -not [bool] $_.Value }).Count -ne 0) { throw 'A TextBox, DatePicker, or ComboBox input guard allowed a main-window shortcut.' }
if (@($validation.MainShortcutGates.FailedChecks).Count -ne 0) { throw 'A main-window shortcut evaluator gate is red.' }
$missingMainShortcutCommandFixture = [pscustomobject][ordered]@{
    CountdownsWorkspace = $true
    NotesWorkspace = $true
    Filter = $true
    TagManager = $true
    Theme = $true
    SaveCountdown = $true
    SavePreferences = $true
}
if (Test-MainShortcutCommandResults -CommandResults $missingMainShortcutCommandFixture) { throw 'A main-window shortcut report with a missing command was accepted.' }
$falseMainShortcutEffectFixture = [pscustomobject][ordered]@{}
foreach ($commandName in @('CountdownsWorkspace', 'NotesWorkspace', 'Filter', 'TagManager', 'Theme', 'Minimize', 'SaveCountdown', 'SavePreferences')) {
    $falseMainShortcutEffectFixture | Add-Member -NotePropertyName $commandName -NotePropertyValue ([pscustomobject]@{
            Handled = $true
            EffectObserved = $commandName -ne 'Theme'
        })
}
if (Test-MainShortcutCommandDiagnostics -CommandDiagnostics $falseMainShortcutEffectFixture) { throw 'A handled main-window shortcut without an observed effect was accepted.' }
$structural = $validation.MainStructuralAnnouncements
if (-not $structural.CountdownWorkspaceAnnounced -or -not $structural.NotesWorkspaceAnnounced) { throw 'A workspace switch is missing its visible polite announcement.' }
if (-not $structural.FilterResultAnnounced) { throw 'A filter result is missing its visible polite announcement.' }
if (-not $structural.CountdownFirstUseEmptyAnnounced -or -not $structural.NotesFirstUseEmptyAnnounced -or -not $structural.CountdownFilterEmptyAnnounced -or -not $structural.NotesFilterEmptyAnnounced) { throw 'A first-use or filtered empty state is missing its distinct visible polite announcement.' }
if (-not $structural.DayThemeAnnounced -or -not $structural.NightThemeAnnounced) { throw 'A day/night theme switch is missing its visible polite announcement.' }
$structuralGates = $validation.MainStructuralAnnouncementGates
$structuralGateNames = @(
    'WorkspaceAnnouncements',
    'FilterResultAnnouncement',
    'EmptyStateAnnouncements',
    'ThemeAnnouncements'
)
if (@($structuralGateNames | Where-Object { -not [bool] $structuralGates.$_ }).Count -ne 0 -or
    -not [bool] $structuralGates.AllPassed -or
    @($structuralGates.FailedChecks).Count -ne 0) {
    throw 'A main structural-announcement evaluator gate is red.'
}
$structuralFailedChecksFixture = [pscustomobject]@{
    WorkspaceAnnouncements = $true
    FilterResultAnnouncement = $true
    EmptyStateAnnouncements = $true
    ThemeAnnouncements = $true
    AllPassed = $true
    FailedChecks = @('WorkspaceAnnouncements')
}
$structuralFailedChecksFixturePassed =
    @($structuralGateNames | Where-Object {
            -not [bool] $structuralFailedChecksFixture.$_
        }).Count -eq 0 -and
    [bool] $structuralFailedChecksFixture.AllPassed -and
    @($structuralFailedChecksFixture.FailedChecks).Count -eq 0
if ($structuralFailedChecksFixturePassed) { throw 'A non-empty main structural-announcement FailedChecks fixture was accepted.' }
if (-not $validation.MainStructuralAnnouncementGateIsolationBehavior) { throw 'Main structural-announcement gates cannot fail independently.' }
$secondaryTypography = $validation.SecondaryWindowTypography
$secondaryTypographyWindows = @($secondaryTypography.Windows)
if ($secondaryTypographyWindows.Count -ne 3 -or ((@($secondaryTypographyWindows.Window) | Sort-Object) -join ',') -ne 'ConfirmationDialog,ReminderCenter,ReminderRulesEditor') { throw 'Secondary-window typography report does not contain the exact three real windows.' }
foreach ($measurement in $secondaryTypographyWindows) {
    if ([double] $measurement.TitleFontSize -ne 22 -or [double] $measurement.BodyFontSize -ne 16 -or [double] $measurement.ContentRootFontSize -ne 16 -or -not $measurement.ContentRootDeclaresFontSize) { throw "Secondary-window $($measurement.Window) does not render the shared 22/16 scale from a locally declared 16 DIP content root." }
    if ($measurement.Window -ne 'ConfirmationDialog' -and ([double] $measurement.SectionFontSize -ne 18 -or [double] $measurement.MinorFontSize -ne 15)) { throw "Secondary-window $($measurement.Window) does not render the shared 18/15 section and minor scale." }
    if ($measurement.Window -eq 'ConfirmationDialog' -and ($null -ne $measurement.SectionFontSize -or $null -ne $measurement.MinorFontSize)) { throw 'Confirmation dialog unexpectedly reports section or minor typography roles.' }
}
if (-not $secondaryTypography.RenderedScaleMatches -or -not $secondaryTypography.ContentRootsDeclareFontSize -or -not $secondaryTypography.FloatingTypographyUnchanged) { throw 'A concrete secondary-window typography gate is red.' }
if ([double] $secondaryTypography.FloatingBodyFontSize -ne 13 -or [double] $secondaryTypography.FloatingReminderFontSize -ne 12) { throw 'Floating-window body/reminder typography changed from the fixed 13/12 DIP contract.' }
$secondaryTypographyGateNames = @(
    'RenderedScaleMatches',
    'ContentRootsDeclareFontSize',
    'FloatingTypographyUnchanged'
)
if (@($secondaryTypographyGateNames | Where-Object { -not [bool] $secondaryTypography.Gates.$_ }).Count -ne 0 -or
    -not [bool] $secondaryTypography.Gates.AllPassed -or
    @($secondaryTypography.Gates.FailedChecks).Count -ne 0) {
    throw 'A concrete secondary-window typography evaluator gate is red.'
}
$secondaryTypographyFailedChecksFixture = [pscustomobject]@{
    RenderedScaleMatches = $true
    ContentRootsDeclareFontSize = $true
    FloatingTypographyUnchanged = $true
    AllPassed = $true
    FailedChecks = @('RenderedScaleMatches')
}
$secondaryTypographyFailedChecksFixturePassed =
    @($secondaryTypographyGateNames | Where-Object {
            -not [bool] $secondaryTypographyFailedChecksFixture.$_
        }).Count -eq 0 -and
    [bool] $secondaryTypographyFailedChecksFixture.AllPassed -and
    @($secondaryTypographyFailedChecksFixture.FailedChecks).Count -eq 0
if ($secondaryTypographyFailedChecksFixturePassed) { throw 'A non-empty secondary-window typography FailedChecks fixture was accepted.' }
if (-not $validation.SecondaryWindowTypographyGateIsolationBehavior) { throw 'Secondary-window typography gates cannot fail independently.' }
if (-not $validation.MainShortcutGateIsolationBehavior) { throw 'Main-window shortcut negative fixtures are not isolated.' }
if (-not $validation.ConfirmationDialogLongestButtonsVisible) { throw 'The longest confirmation dialog content pushes a destructive-dialog button outside the visible window.' }
if (-not $validation.ConfirmationDialogLongestBodyReachable) { throw 'The longest confirmation dialog body is not wrapped or reachable through its scroller.' }
if (-not $validation.ConfirmationDialogSafeDefaultActions) { throw 'The confirmation dialog does not keep cancel as the safe default action.' }
if (-not $validation.ConfirmationDialogGateIsolationBehavior) { throw 'Confirmation dialog negative fixtures are not isolated.' }
$confirmation = $validation.ConfirmationDialog
$confirmationMeasurements = @($confirmation.Measurements)
if ($confirmationMeasurements.Count -ne 4) { throw 'Confirmation dialog validation did not measure the exact 2 content kinds x 2 themes matrix.' }
$expectedConfirmationStates = @('Countdown-Day', 'Countdown-Night', 'Note-Day', 'Note-Night')
if ((@($confirmationMeasurements.StateId | Sort-Object) -join ',') -ne ($expectedConfirmationStates -join ',')) { throw 'Confirmation dialog validation matrix state IDs are not exact.' }
foreach ($measurement in $confirmationMeasurements) {
    $expectedSubjectLength = if ($measurement.ContentKind -eq 'Note') { 140 } elseif ($measurement.ContentKind -eq 'Countdown') { 30 } else { -1 }
    if ([int] $measurement.SubjectLength -ne $expectedSubjectLength -or
        [int] $measurement.MessageLength -ne ($expectedSubjectLength + 8) -or
        -not [bool] $measurement.ProductionMessageExact) { throw "Confirmation dialog $($measurement.StateId) did not use the exact production prompt at its input limit." }
    if (-not [bool] $measurement.ButtonsVisible -or -not [bool] $measurement.BodyReachable -or
        -not [bool] $measurement.BodyEndReached -or -not [bool] $measurement.OwnerMatches -or
        [string] $measurement.StartupLocation -ne 'CenterOwner' -or -not [bool] $measurement.CenteredOnOwner) { throw "Confirmation dialog $($measurement.StateId) did not preserve visible controls, reachable content, and real owner centering." }
    if (-not [bool] $measurement.EnterDidNotConfirm -or -not [bool] $measurement.EscapeDidNotConfirm -or
        -not [bool] $measurement.CancelDidNotConfirm) { throw "Confirmation dialog $($measurement.StateId) allowed a safe keyboard/cancel action to confirm deletion." }
}
$confirmationDialogGateNames = @(
    'ButtonsVisible',
    'BodyReachable',
    'CancelIsSafeDefault',
    'ProductionMatrixComplete',
    'BodyEndReachable',
    'CenteredOnOwner',
    'EnterIsSafe',
    'CancelAndEscapeAreSafe'
)
if (@($confirmationDialogGateNames | Where-Object { -not [bool] $confirmation.Gates.$_ }).Count -ne 0 -or
    -not [bool] $confirmation.Gates.AllPassed -or
    @($confirmation.Gates.FailedChecks).Count -ne 0) {
    throw 'A concrete confirmation-dialog evaluator gate is red.'
}
$confirmationDialogFailedChecksFixture = [pscustomobject]@{
    ButtonsVisible = $true
    BodyReachable = $true
    CancelIsSafeDefault = $true
    ProductionMatrixComplete = $true
    BodyEndReachable = $true
    CenteredOnOwner = $true
    EnterIsSafe = $true
    CancelAndEscapeAreSafe = $true
    AllPassed = $true
    FailedChecks = @('ButtonsVisible')
}
$confirmationDialogFailedChecksFixturePassed =
    @($confirmationDialogGateNames | Where-Object {
            -not [bool] $confirmationDialogFailedChecksFixture.$_
        }).Count -eq 0 -and
    [bool] $confirmationDialogFailedChecksFixture.AllPassed -and
    @($confirmationDialogFailedChecksFixture.FailedChecks).Count -eq 0
if ($confirmationDialogFailedChecksFixturePassed) { throw 'A non-empty confirmation-dialog FailedChecks fixture was accepted.' }
if (-not $validation.EffectiveThemeTextContrast) { throw 'Muted note or calendar text falls below the required effective contrast.' }
$completedAuxiliaryContrast = $validation.NoteCompletedAuxiliaryContrast
if (-not $completedAuxiliaryContrast.TextContrast -or
    -not $completedAuxiliaryContrast.UsesDedicatedToken -or
    -not $completedAuxiliaryContrast.ActualBackgroundMatchesHeader -or
    -not $completedAuxiliaryContrast.MutedBrushUnchanged -or
    -not $completedAuxiliaryContrast.CompletedHeaderColorUnchanged) {
    throw 'Completed-note auxiliary text does not meet its actual day/night header-background contrast contract.'
}
if (-not $validation.NoteCompletedAuxiliaryContrastGateIsolationBehavior) { throw 'Completed-note auxiliary contrast and semantic-token negative fixtures cannot fail independently.' }
$completedAuxiliaryMeasurements = @($completedAuxiliaryContrast.Measurements)
if ($completedAuxiliaryMeasurements.Count -ne 2 -or ((@($completedAuxiliaryMeasurements.Theme) | Sort-Object) -join ',') -ne 'Day,Night') { throw 'Completed-note auxiliary contrast report must contain exact day/night measurements.' }
if (@($completedAuxiliaryMeasurements | Where-Object { [double] $_.ContrastRatio -lt 4.5 -or -not $_.UsesDedicatedToken -or -not $_.ActualBackgroundMatchesHeader -or -not $_.MutedBrushUnchanged -or -not $_.CompletedHeaderColorUnchanged }).Count -ne 0) { throw 'A completed-note auxiliary measurement failed the dedicated-token, actual-background, or unchanged-token contract.' }
if (-not $validation.FormControlsHaveAccessibleNames) { throw 'A core form control is missing an explicit accessible name.' }
if (-not $validation.StatusMessagesUseLiveRegions) { throw 'Dynamic status messages are not announced through live regions.' }
if (-not $validation.ReminderCenterSupportsKeyboardNavigation) { throw 'The reminder center does not support complete keyboard traversal.' }
if (-not $validation.ReminderCenterInitialFocusAndAccessibleNameWork) { throw 'The reminder center lacks its initial focus or accessible name.' }
if (-not $validation.ReminderCenterKeyboardGateIsolationBehavior) { throw 'Reminder-center tab traversal, close-guard, and initial-focus/accessible-name gates cannot fail independently.' }
if (-not $validation.ReminderCenterCustomSnoozeControlsVisibility) { throw 'Custom snooze date/hour/minute controls do not expand and collapse with the custom option.' }
if (-not $validation.ReminderCenterCustomSnoozeCalendarBehavior) { throw 'Custom snooze calendar lost the unified read-only, Escape-close, and focus behavior.' }
if (-not $validation.ReminderCenterShowFailureRollbackSurvivesEntryFailure) { throw 'Reminder-center show-failure rollback no longer cleans coordinator state when entry removal fails.' }
if (-not $validation.CompactActionTargetsAreUsable) { throw 'Compact tag and note actions are smaller than the desktop interaction target.' }

$uiSettingsRoot = Join-Path `
    'D:\Codex\.codex\tmp\clock-countdown-ui-tests' `
    ([guid]::NewGuid().ToString('N'))
[System.IO.Directory]::CreateDirectory($uiSettingsRoot) | Out-Null
$testSettingsPath = Join-Path $uiSettingsRoot 'ui-test.settings.json'
$noteAddSettingsPath = Join-Path $uiSettingsRoot 'ui-note-add-test.settings.json'
$tagInteractionSettingsPath = Join-Path $uiSettingsRoot 'ui-tag-interaction.settings.json'
$persistenceFailureSettingsPath = Join-Path $uiSettingsRoot 'ui-persistence-failure.settings.json'
$persistenceFailureUnblockedSettingsPath = Join-Path $uiSettingsRoot 'ui-persistence-failure-unblocked.settings.json'
$legacyV3SourcePath = Join-Path $uiSettingsRoot 'ui-test-v3.source.json'
$legacyV3SettingsPath = Join-Path $uiSettingsRoot 'ui-test-v3.settings.json'
try {
    $testSettingsSeedJson = @{
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
    } | ConvertTo-Json -Depth 5
    $testSettingsJson = ConvertTo-CountdownSettingsJson -State (
        ConvertFrom-CountdownSettingsObject -Settings (
            $testSettingsSeedJson | ConvertFrom-Json))
    $testSettingsDocument = $testSettingsJson | ConvertFrom-Json
    if ([int] $testSettingsDocument.schemaVersion -ne 10) { throw 'The UI fixture was not generated from the canonical schema-v10 settings contract.' }
    [System.IO.File]::WriteAllText(
        $testSettingsPath,
        $testSettingsJson,
        (New-Object System.Text.UTF8Encoding($false)))
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
    $dynamicCountdownFocusedTargets = [string[]] ('ThemeModeButton,TopmostCheck,MinimizeButton,CloseButton,CountdownTab,NotesTab,FilterButton,TagManagerButton,CountdownName,CountdownDate,CountdownHour,CountdownMinute,CountdownSecond,SetNowButton,CountdownTags,ManageCountdownReminderRulesButton,SaveCountdownButton,DisplayPrecision,MonthlySalary,WorkStart,WorkEnd,SavePreferencesButton,CountdownListRegion,CountdownListRegion.Edit,CountdownListRegion.Delete,CountdownListRegion.Edit,CountdownListRegion.Delete,StatusText' -split ',')
    $dynamicCountdownTabPath = $listValidation.MainKeyboardBehavior.CountdownTabPath
    if (-not $dynamicCountdownTabPath.FocusVisualsVisible -or
        -not (Test-ExactSymmetricTabTargets `
            -ExpectedForward $dynamicCountdownFocusedTargets `
            -ActualForward @($dynamicCountdownTabPath.ForwardFocusedTargets) `
            -ActualBackward @($dynamicCountdownTabPath.BackwardFocusedTargets))) {
        throw 'The populated Countdowns workspace did not traverse both Edit/Delete controls for every row symmetrically with visible focus.'
    }
    $dynamicNotesFocusedTargets = [string[]] ('ThemeModeButton,TopmostCheck,MinimizeButton,CloseButton,CountdownTab,NotesTab,FilterButton,TagManagerButton,NoteText,NoteDate,SaveNoteButton,ManageNoteReminderRulesButton,NoteTags,NoteSearch,NotesListRegion,NotesListRegion.ActiveSection,NotesListRegion.DateSection,NotesListRegion.Completion,NotesListRegion.Edit,NotesListRegion.Delete,NotesListRegion.Completion,NotesListRegion.Edit,NotesListRegion.Delete,NotesListRegion.DateSection,NotesListRegion.Completion,NotesListRegion.Edit,NotesListRegion.Delete,NotesListRegion.CompletedSection,NotesListRegion.RepeatButton,StatusText' -split ',')
    $dynamicNotesTabPath = $listValidation.MainKeyboardBehavior.NotesTabPath
    if (-not $dynamicNotesTabPath.FocusVisualsVisible -or
        -not (Test-ExactSymmetricTabTargets `
            -ExpectedForward $dynamicNotesFocusedTargets `
            -ActualForward @($dynamicNotesTabPath.ForwardFocusedTargets) `
            -ActualBackward @($dynamicNotesTabPath.BackwardFocusedTargets))) {
        throw 'The populated Notes workspace did not traverse every section, date, completion, Edit, and Delete target symmetrically with visible focus.'
    }
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
    if ($listValidation.SettingsWereMigrated) { throw 'The canonical UI fixture was unexpectedly treated as a legacy settings version.' }
    if (-not $listValidation.CardActionsBoundByTag) { throw 'Card edit/delete actions are not bound to their countdown ID.' }
    if ($listValidation.FloatingCountdownCount -ne 1) { throw "Expected one active floating countdown card, got $($listValidation.FloatingCountdownCount)." }
    if ((@($listValidation.FloatingCountdownTexts).Count -ne 1) -or $listValidation.FloatingCountdownTexts[0] -notmatch '天 .*小时 .*分钟 .*秒') { throw "Floating countdowns did not use the unified unit format: $(@($listValidation.FloatingCountdownTexts) -join ',')" }
    if ($listValidation.FloatingIncomeText -notmatch '今日已赚 ￥|节假日日历未就绪') { throw "Floating income text is missing: $($listValidation.FloatingIncomeText)" }
    if (-not $listValidation.FloatingNoteSearch.HintNamesDescribeAction) { throw 'Floating search hint accessible name lost the expand action semantics.' }
    if (-not $listValidation.FloatingNoteSearch.LongHintTermTrimsWithinSlot) { throw 'A 140-character search term did not trim inside the floating search hint slot.' }
    if (-not $listValidation.FloatingNoteSearch.NoteFilterEmptyStateDistinguished) { throw 'Floating notes workspace did not distinguish filter-no-match from no-data empty state.' }
    if (-not $listValidation.FloatingNoteSearch.CountdownFilterEmptyStateDistinguished) { throw 'Floating countdowns workspace did not distinguish filter-no-match from no-data empty state.' }
    if (-not $listValidation.FloatingNoteSearch.SearchRefreshLeavesPlacementUnchanged) { throw 'Floating note search refresh moved Left/Top even though ActualWidth/ActualHeight were unchanged.' }
    if (-not $listValidation.FloatingEmptyStateTextIsolationBehavior) { throw 'Floating empty-state text mapping isolation behavior failed.' }
    if (-not $listValidation.FloatingIncomeHiddenWithoutSalary) { throw 'Floating income row still renders when monthly salary is unset.' }
    if (-not $listValidation.FloatingIncomeRestoredWithSalary) { throw 'Floating income row did not restore after restoring a positive monthly salary.' }

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
    if (-not $noteAddValidation.NewNoteRejectsEmptyDate) { throw 'The new-note path accepted an empty date or discarded the draft.' }
    if (-not $noteAddValidation.UndatedNoteEditSavesWithoutDate) { throw 'Editing an undated sticky note without assigning a date failed to save or fabricated a date.' }
    if (-not $noteAddValidation.DatedNoteEditRejectsClearedDate) { throw 'Editing a dated sticky note accepted clearing the date or discarded the draft.' }
    if (-not $noteAddValidation.NoteDateValidationMessagesDistinguished) { throw 'New-note and edit-path date validation messages are not distinguishable.' }
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
    if (@($noteAddSettings.notes | Where-Object {
                $rules = @($_.reminderRules)
                $_.text -eq '验证点击添加便签' -and
                $_.date -in $expectedNoteExerciseDates -and
                -not $_.isCompleted -and
                $rules.Count -eq 1 -and
                $rules[0].type -eq 'fixedInterval' -and
                $rules[0].intervalMinutes -eq 30 -and
                $null -ne $rules[0].firstDueUtc
            }).Count -ne 1) { throw 'Clicking Add did not persist the active sticky note with its selected date and reminder rule.' }
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
    if ($tagInteractionSettings.schemaVersion -ne 10) { throw 'Tag interaction exercise did not persist schema v10.' }
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

    [System.IO.File]::WriteAllText($legacyV3SourcePath, (@{
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
    $legacyV3HashBeforeValidation = (Get-FileHash -Algorithm SHA256 -LiteralPath $legacyV3SourcePath).Hash
    Copy-Item -LiteralPath $legacyV3SourcePath -Destination $legacyV3SettingsPath
    $legacyV3ValidationJson = & $powerShellEngine `
        -NoLogo -NoProfile -NonInteractive -STA -File (Join-Path $PSScriptRoot '敏敏子倒计时.ps1') `
        -ValidateOnly -SettingsFile $legacyV3SettingsPath | Out-String
    if ($LASTEXITCODE -ne 0) { throw "Schema-v3 migration validation failed: $legacyV3ValidationJson" }
    $legacyV3Validation = $legacyV3ValidationJson | ConvertFrom-Json
    if (-not $legacyV3Validation.SettingsWereMigrated) { throw 'Schema-v3 settings were not marked for migration.' }
    if ($legacyV3Validation.LoadedCountdowns -ne 1 -or (@($legacyV3Validation.SortedNames) -join ',') -ne '旧版倒计时') { throw 'Schema-v3 countdowns were not preserved.' }
    if ($legacyV3Validation.LoadedNotes -ne 0) { throw 'Schema-v3 migration should initialize an empty sticky-note list.' }
    if (-not $legacyV3Validation.PersistenceRoundTrip -or -not $legacyV3Validation.NotePersistenceRoundTrip -or -not $legacyV3Validation.TagPersistenceRoundTrip) { throw 'Schema-v3 settings did not serialize as schema-v10.' }
    if ($legacyV3Validation.LoadedDisplayPrecision -ne 'Minute' -or ([decimal] $legacyV3Validation.LoadedMonthlySalary) -ne [decimal]12345 -or $legacyV3Validation.LoadedWorkStart -ne '08:30' -or $legacyV3Validation.LoadedWorkEnd -ne '17:30') { throw 'Schema-v3 preferences were not preserved.' }
    $legacyV3MigratedSettings = [System.IO.File]::ReadAllText($legacyV3SettingsPath, [System.Text.Encoding]::UTF8) | ConvertFrom-Json
    if ([int] $legacyV3MigratedSettings.schemaVersion -ne 10) { throw 'Schema-v3 validation did not write its execution copy through the canonical schema-v10 contract.' }
    $legacyV3HashAfterValidation = (Get-FileHash -Algorithm SHA256 -LiteralPath $legacyV3SourcePath).Hash
    if ($legacyV3HashAfterValidation -ne $legacyV3HashBeforeValidation) { throw 'ValidateOnly modified the schema-v3 source fixture instead of its isolated execution copy.' }
}
finally {
    foreach ($diagnosticPath in @($diagnosticLogPath, "$diagnosticLogPath.1", "$diagnosticLogPath.2", "$diagnosticLogPath.3")) {
        Remove-Item -LiteralPath $diagnosticPath -Force -ErrorAction SilentlyContinue
    }
    Remove-Item -LiteralPath (Split-Path -Parent $diagnosticLogPath) -Force -ErrorAction SilentlyContinue
    foreach ($settingsPath in @(
            $testSettingsPath,
            $noteAddSettingsPath,
            $tagInteractionSettingsPath,
            $persistenceFailureSettingsPath,
            $persistenceFailureUnblockedSettingsPath,
            $legacyV3SourcePath,
            $legacyV3SettingsPath
        )) {
        foreach ($suffix in @('', '.tmp', '.backup1', '.backup2', '.backup2.tmp', '.backup2.rollback')) {
            $companionPath = "$settingsPath$suffix"
            if (Test-Path -LiteralPath $companionPath -PathType Leaf) {
                Remove-Item -LiteralPath $companionPath -Force
            }
        }
    }
    if (Test-Path -LiteralPath "$persistenceFailureSettingsPath.tmp" -PathType Container) {
        [System.IO.Directory]::Delete("$persistenceFailureSettingsPath.tmp", $false)
    }
    if (Test-Path -LiteralPath "$persistenceFailureUnblockedSettingsPath.tmp" -PathType Container) {
        [System.IO.Directory]::Delete("$persistenceFailureUnblockedSettingsPath.tmp", $false)
    }
    [System.IO.Directory]::Delete($uiSettingsRoot, $false)
}

$report = [pscustomobject]@{
    Passed = 152
    Failed = 0
    SelectedDateType = $picker.SelectedDate.GetType().FullName
    ApplicationValidation = $validation.Valid
}
if ($null -ne $popupUiaSkipEvidence) {
    Add-Member -InputObject $report -NotePropertyName PopupUiaGate -NotePropertyValue $popupUiaSkipEvidence
}
$report | ConvertTo-Json
