#requires -Version 7.0

$ErrorActionPreference = 'Stop'

Import-Module -Name (Join-Path $PSScriptRoot 'tools\Countdown.SuiteIsolation.psm1') -Force
Assert-SuiteIsolatedDesktop

# #126 分段计时插桩：计时行只走 stderr，保持 stdout 单一 JSON 报告契约
# （Countdown.Validation.psm1 的 Invoke-CountdownValidationProcess 严格解析整个 stdout）。
$script:testClock = [System.Diagnostics.Stopwatch]::StartNew()
$script:segmentClock = [System.Diagnostics.Stopwatch]::StartNew()
function Write-TestSegmentTiming {
    param([Parameter(Mandatory)] [string] $Name)
    $script:segmentClock.Stop()
    [Console]::Error.WriteLine(
        ('[test-segment] name={0} seconds={1:F3}' -f $Name, $script:segmentClock.Elapsed.TotalSeconds))
    $script:segmentClock.Restart()
}
function Write-TestSegmentTotal {
    [Console]::Error.WriteLine(
        ('[test-segment-total] seconds={0:F3}' -f $script:testClock.Elapsed.TotalSeconds))
}

# #128 断言计数插桩：脚本启动时解析自身 AST，找出全部断言语句（分支体内含 throw 的 if），
# 在其起始行打 line breakpoint 动态计数。任何断言失败都会 throw 中止、走不到报告，
# 因此报告点的命中数就是「实际执行并通过的断言数」（口径与 CSharp 的 Assert-Equal 逐次
# 计数一致；循环体断言按迭代计）。stdout 仍是单一 JSON 报告，计时行走 stderr（#126）。
$script:uiAssertionCount = 0
$uiAssertionTokens = $null
$uiAssertionParseErrors = $null
$uiAssertionAst = [System.Management.Automation.Language.Parser]::ParseFile(
    $PSCommandPath, [ref]$uiAssertionTokens, [ref]$uiAssertionParseErrors)
if ($uiAssertionParseErrors.Count -gt 0) { throw 'Ui 测试脚本 AST 解析失败，无法建立断言计数。' }
$script:uiAssertionSites = @(
    $uiAssertionAst.FindAll(
        { param($node)
            $node -is [System.Management.Automation.Language.IfStatementAst] -and
            @($node.FindAll(
                    { param($inner) $inner -is [System.Management.Automation.Language.ThrowStatementAst] },
                    $true)).Count -gt 0
        },
        $true))
if ($script:uiAssertionSites.Count -eq 0) { throw '未找到任何断言语句，断言计数不可信，拒绝继续。' }
# 前导码自身的守卫 throw 在绑定之前执行，按「Set-PSBreakpoint 绑定行之后」划出计数总体，
# 保证 AssertionSites 与动态命中口径一致（插桩自检不算断言）。
$uiAssertionBindPoint = $uiAssertionAst.FindAll(
    { param($node)
        $node -is [System.Management.Automation.Language.CommandAst] -and
        $node.GetCommandName() -eq 'Set-PSBreakpoint'
    }, $true) | Select-Object -First 1
if ($null -eq $uiAssertionBindPoint) { throw '未找到断言计数绑定点，拒绝继续。' }
$script:uiAssertionSites = @(
    $script:uiAssertionSites | Where-Object {
        $_.Extent.StartLineNumber -gt $uiAssertionBindPoint.Extent.StartLineNumber
    })
if ($script:uiAssertionSites.Count -eq 0) { throw '未找到任何断言语句，断言计数不可信，拒绝继续。' }
# UIA 门跳过段（[Environment]::UserInteractive 为假时整段跳过，见 UIA-GATE-SKIP）内的
# 断言点数，供报告在非交互会话下按 Skipped 分列，保证与 #129 去重门禁的口径可比。
$script:uiAssertionGateSkipSites = 0
foreach ($uiAssertionNode in $uiAssertionAst.FindAll(
        { param($node) $node -is [System.Management.Automation.Language.IfStatementAst] }, $true) |
        Where-Object { $_.Extent.StartLineNumber -gt $uiAssertionBindPoint.Extent.StartLineNumber }) {
    if ($uiAssertionNode.Clauses.Count -gt 0 -and
        $uiAssertionNode.Clauses[0].Item1.Extent.Text -match 'UserInteractive') {
        $uiAssertionGateExtent = $uiAssertionNode.Clauses[0].Item2.Extent
        $script:uiAssertionGateSkipSites = @(
            $script:uiAssertionSites | Where-Object {
                $_.Extent.StartOffset -ge $uiAssertionGateExtent.StartOffset -and
                $_.Extent.StartOffset -le $uiAssertionGateExtent.EndOffset
            }).Count
        break
    }
}
$null = Set-PSBreakpoint -Script $PSCommandPath -Line @(
    $script:uiAssertionSites | ForEach-Object { $_.Extent.StartLineNumber } | Sort-Object -Unique
) -Action { $script:uiAssertionCount = $script:uiAssertionCount + 1 }

[Environment]::SetEnvironmentVariable(
    'MINMIN_COUNTDOWN_MAIN_BASELINE_GENERATE',
    'stale-test-request',
    [EnvironmentVariableTarget]::Process
)
Import-Module (Join-Path $PSScriptRoot 'Countdown.Validation.psm1') -Force
$visualBaseline = Get-CountdownVisualBaseline -RepositoryRoot $PSScriptRoot
$floatingReferenceBaselineRequired = Test-Path -LiteralPath $visualBaseline.Floating.ManifestPath -PathType Leaf
$mainReferenceBaselineRequired = Test-Path -LiteralPath $visualBaseline.Main.ManifestPath -PathType Leaf
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
        $properties.Count -eq 7 -and
        ((@($properties.Name) | Sort-Object) -join ',') -eq
            'CountdownsWorkspace,Filter,Minimize,NotesWorkspace,SaveCountdown,SavePreferences,TagManager' -and
        @($properties | Where-Object { -not [bool] $_.Value }).Count -eq 0)
}

function Test-MainShortcutCommandDiagnostics {
    param([Parameter(Mandatory)] [object] $CommandDiagnostics)

    $properties = @($CommandDiagnostics.PSObject.Properties)
    return (
        $properties.Count -eq 7 -and
        ((@($properties.Name) | Sort-Object) -join ',') -eq
            'CountdownsWorkspace,Filter,Minimize,NotesWorkspace,SaveCountdown,SavePreferences,TagManager' -and
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
Write-TestSegmentTiming -Name 'Setup'
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
    Write-TestSegmentTiming -Name 'FatalDiagnosticExercise'

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
    Write-TestSegmentTiming -Name 'ApplicationHostValidation'

}
finally {
    Remove-Item -LiteralPath $validationSettingsPath -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath "$validationSettingsPath.tmp" -Force -ErrorAction SilentlyContinue
}

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
Start-CountdownApplication -ExitAfterReadyForTest -ExitAfterReadyForTestSeconds 3 -ApplicationPath `$PSCommandPath -HolidayCalendarTaskFactory `$calendarTaskFactory
"@
    [System.IO.File]::WriteAllText($runtimeIntegrationEntry, $runtimeIntegrationHarness, (New-Object System.Text.UTF8Encoding($false)))
    $runtimeIntegrationRequest = New-CountdownStartupRequest -Path $runtimeIntegrationRequestPath
    $runtimeIntegrationOutputPath = Join-Path $runtimeIntegrationRoot 'app.stdout.txt'
    $runtimeIntegrationErrorPath = Join-Path $runtimeIntegrationRoot 'app.stderr.txt'
    $runtimeIntegrationArguments = @('-NoLogo', '-NoProfile', '-NonInteractive', '-STA', '-File', ('"{0}"' -f $runtimeIntegrationEntry))
    $runtimeIntegrationProcess = Start-Process -FilePath $powerShellEngine -ArgumentList $runtimeIntegrationArguments -NoNewWindow -PassThru -RedirectStandardOutput $runtimeIntegrationOutputPath -RedirectStandardError $runtimeIntegrationErrorPath
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
    $runtimeIntegrationSelfExitWait = [System.Diagnostics.Stopwatch]::StartNew()
    if (-not $runtimeIntegrationProcess.WaitForExit(35000)) { throw 'Real WPF launch did not close after the receipt integration test.' }
    $runtimeIntegrationSelfExitWait.Stop()
    if ($runtimeIntegrationProcess.ExitCode -ne 0) {
        $runtimeError = if (Test-Path -LiteralPath $runtimeIntegrationErrorPath) { Get-Content -LiteralPath $runtimeIntegrationErrorPath -Raw } else { '' }
        throw "Real WPF launch exited with $($runtimeIntegrationProcess.ExitCode): $runtimeError"
    }
    if ($runtimeIntegrationSelfExitWait.Elapsed.TotalSeconds -ge 10) {
        throw ("Real WPF launch self-exit wait regressed to {0:F1}s; expected about 3s via -ExitAfterReadyForTestSeconds 3." -f $runtimeIntegrationSelfExitWait.Elapsed.TotalSeconds)
    }
    [Console]::Error.WriteLine(("UI_RUNTIME_INTEGRATION_SELF_EXIT_WAIT_SECONDS={0:F1}" -f $runtimeIntegrationSelfExitWait.Elapsed.TotalSeconds))
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

Write-TestSegmentTiming -Name 'RuntimeIntegration'
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
        $popupUiaProcess = Start-Process -FilePath $powerShellEngine -ArgumentList $popupUiaArguments -NoNewWindow -PassThru -RedirectStandardOutput $popupUiaOutputPath -RedirectStandardError $popupUiaErrorPath
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
Write-TestSegmentTiming -Name 'PopupUiaGate'

$uiSettingsRoot = Join-Path `
    'D:\Codex\.codex\tmp\clock-countdown-ui-tests' `
    ([guid]::NewGuid().ToString('N'))
[System.IO.Directory]::CreateDirectory($uiSettingsRoot) | Out-Null
$testSettingsPath = Join-Path $uiSettingsRoot 'ui-test.settings.json'
$tagInteractionSettingsPath = Join-Path $uiSettingsRoot 'ui-tag-interaction.settings.json'
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
    $dynamicCountdownFocusedTargets = [string[]] ('TopmostCheck,MinimizeButton,CloseButton,CountdownTab,NotesTab,PreferencesButton,FilterButton,TagManagerButton,CountdownName,CountdownDate,SaveCountdownButton,CountdownEditorExpandButton,CountdownListRegion,CountdownListRegion.Edit,CountdownListRegion.Delete,CountdownListRegion.Edit,CountdownListRegion.Delete,StatusText' -split ',')
    $dynamicCountdownTabPath = $listValidation.MainKeyboardBehavior.CountdownTabPath
    if (-not $dynamicCountdownTabPath.FocusVisualsVisible -or
        -not (Test-ExactSymmetricTabTargets `
            -ExpectedForward $dynamicCountdownFocusedTargets `
            -ActualForward @($dynamicCountdownTabPath.ForwardFocusedTargets) `
            -ActualBackward @($dynamicCountdownTabPath.BackwardFocusedTargets))) {
        throw 'The populated Countdowns workspace did not traverse both Edit/Delete controls for every row symmetrically with visible focus.'
    }
    $dynamicNotesFocusedTargets = [string[]] ('TopmostCheck,MinimizeButton,CloseButton,CountdownTab,NotesTab,PreferencesButton,FilterButton,TagManagerButton,NoteText,NoteDate,SaveNoteButton,ManageNoteReminderRulesButton,NoteTags,NoteSearch,NotesListRegion,NotesListRegion.ActiveSegment,NotesListRegion.CompletedSegment,NotesListRegion.DateSection,NotesListRegion.NoteSelectionCard,NotesListRegion.Completion,NotesListRegion.Edit,NotesListRegion.Delete,NotesListRegion.NoteSelectionCard,NotesListRegion.Completion,NotesListRegion.Edit,NotesListRegion.Delete,NotesListRegion.DateSection,NotesListRegion.NoteSelectionCard,NotesListRegion.Completion,NotesListRegion.Edit,NotesListRegion.Delete,StatusText' -split ',')
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
    if (-not $listValidation.NoteStatusSegmentSwitch) { throw 'Sticky-note status segment switch is missing fixed labels, badge counts, or exclusive selection.' }
    if (-not $listValidation.NoteDateTimelineVisuals) { throw 'Sticky-note date disclosures are missing the independent timeline visual.' }
    if (-not $listValidation.NoteDisclosureAccessibility) { throw 'Sticky-note disclosure controls are missing stable accessible names.' }
    if (-not $listValidation.NoteDisclosureThemeTokens) { throw 'Sticky-note disclosure theme tokens are missing day resources.' }
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
        @($listValidation.RenderedFloatingNoteIds) -join ',' -ne 'note-active-new-second,note-active-new-first,note-active-old') {
        throw "Floating notes must use shared newest-date and newest-entry order (#226): $(@($listValidation.RenderedFloatingNoteIds) -join ',')."
    }
    if (-not $listValidation.NoteDateSectionsCanCollapse) { throw 'Sticky-note date sections cannot be expanded and collapsed independently.' }
    if (-not $listValidation.NoteDateSectionsAreIndependent) { throw 'Active and completed date sections do not retain independent collapse state.' }
    if (-not $listValidation.NoteDateSectionStateSurvivesRefresh) { throw 'Sticky-note date collapse state did not survive a manager refresh.' }
    if (-not $listValidation.NoteStatusSegmentsSwitchExclusively) { throw 'Active and completed status segments do not switch exclusively across refresh.' }
    if (-not $listValidation.NoteDateDefaultsTrackBeijingDay) { throw 'Default sticky-note dates did not advance at Beijing midnight.' }
    if (-not $listValidation.NoteDateChoicesSurviveBeijingDayChange) { throw 'A selected sticky-note date or in-progress draft was overwritten at Beijing midnight.' }
    if (-not $listValidation.CompletedNotesCollapsed) { throw 'Completed sticky notes should be collapsed by default.' }
    $settingsHashAfterValidation = (Get-FileHash -Algorithm SHA256 -LiteralPath $testSettingsPath).Hash
    if ($settingsHashAfterValidation -ne $settingsHashBeforeValidation) { throw 'ValidateOnly modified the supplied settings file.' }
    if ($listValidation.SettingsWereMigrated) { throw 'The canonical UI fixture was unexpectedly treated as a legacy settings version.' }
    if (-not $listValidation.CardActionsBoundByTag) { throw 'Card edit/delete actions are not bound to their countdown ID.' }
    if ($listValidation.FloatingCountdownCount -ne 1) { throw "Expected one active floating countdown card, got $($listValidation.FloatingCountdownCount)." }
    if ((@($listValidation.FloatingCountdownTexts).Count -ne 1) -or $listValidation.FloatingCountdownTexts[0] -notmatch '天 .*小时 .*分钟 .*秒') { throw "Floating countdowns did not use the unified unit format: $(@($listValidation.FloatingCountdownTexts) -join ',')" }
    if ($listValidation.FloatingIncomeText -notmatch '今日已赚 ￥|节假日安排未就绪：距离下班和今日已赚暂不可用，联网后自动恢复|节假日日历正在加载|加油！美好的事情正在靠近') { throw "Floating income text is missing: $($listValidation.FloatingIncomeText)" }
    if (-not $listValidation.FloatingNoteSearch.HintNamesDescribeAction) { throw 'Floating search hint accessible name lost the expand action semantics.' }
    if (-not $listValidation.FloatingNoteSearch.LongHintTermTrimsWithinSlot) { throw 'A 140-character search term did not trim inside the floating search hint slot.' }
    if (-not $listValidation.FloatingNoteSearch.NoteFilterEmptyStateDistinguished) { throw 'Floating notes workspace did not distinguish filter-no-match from no-data empty state.' }
    if (-not $listValidation.FloatingNoteSearch.CountdownFilterEmptyStateDistinguished) { throw 'Floating countdowns workspace did not distinguish filter-no-match from no-data empty state.' }
    if (-not $listValidation.FloatingNoteSearch.SearchRefreshLeavesPlacementUnchanged) { throw 'Floating note search refresh moved Left/Top even though ActualWidth/ActualHeight were unchanged.' }
    if (-not $listValidation.FloatingEmptyStateTextIsolationBehavior) { throw 'Floating empty-state text mapping isolation behavior failed.' }
    if (-not $listValidation.FloatingIncomeHiddenWithoutSalary) { throw 'Floating income row renders an earnings amount when monthly salary is unset instead of the encouragement copy or calendar status.' }
    if (-not $listValidation.FloatingIncomeRestoredWithSalary) { throw 'Floating income row did not restore after restoring a positive monthly salary.' }
    Write-TestSegmentTiming -Name 'ListValidation'

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
    Write-TestSegmentTiming -Name 'TagInteractionExercise'

    Copy-Item -LiteralPath $testSettingsPath -Destination $persistenceFailureUnblockedSettingsPath -Force
    $persistenceFailureUnblockedHashBeforeValidation = (Get-FileHash -Algorithm SHA256 -LiteralPath $persistenceFailureUnblockedSettingsPath).Hash
    $persistenceFailureUnblockedValidationOutput = & $powerShellEngine `
        -NoLogo -NoProfile -NonInteractive -STA -File (Join-Path $PSScriptRoot '敏敏子倒计时.ps1') `
        -ValidateOnly -ExercisePersistenceFailures -SettingsFile $persistenceFailureUnblockedSettingsPath 2>&1 | Out-String
    if ($LASTEXITCODE -eq 0) { throw 'Persistence-failure validation must reject an unblocked temporary path.' }
    $persistenceFailureUnblockedHashAfterValidation = (Get-FileHash -Algorithm SHA256 -LiteralPath $persistenceFailureUnblockedSettingsPath).Hash
    if ($persistenceFailureUnblockedHashAfterValidation -ne $persistenceFailureUnblockedHashBeforeValidation) { throw 'Persistence-failure validation changed the supplied settings file without a blocked temporary path.' }

    Write-TestSegmentTiming -Name 'PersistenceFailureExercise'

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
    Write-TestSegmentTiming -Name 'LegacyMigrationExercise'
}
finally {
    foreach ($diagnosticPath in @($diagnosticLogPath, "$diagnosticLogPath.1", "$diagnosticLogPath.2", "$diagnosticLogPath.3")) {
        Remove-Item -LiteralPath $diagnosticPath -Force -ErrorAction SilentlyContinue
    }
    Remove-Item -LiteralPath (Split-Path -Parent $diagnosticLogPath) -Force -ErrorAction SilentlyContinue
    foreach ($settingsPath in @(
            $testSettingsPath,
            $tagInteractionSettingsPath,
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
    if (Test-Path -LiteralPath "$persistenceFailureUnblockedSettingsPath.tmp" -PathType Container) {
        [System.IO.Directory]::Delete("$persistenceFailureUnblockedSettingsPath.tmp", $false)
    }
    [System.IO.Directory]::Delete($uiSettingsRoot, $false)
}

Write-TestSegmentTotal
# #128 计数口径：Passed = 实际执行并通过的断言数（动态命中）；Skipped = 跳过段内断言点数
# （仅 UIA-GATE-SKIP 时非零）；AssertionSites = 静态断言点总数。循环体断言按迭代计入 Passed，
# 故 Passed 可能大于 AssertionSites；跳过段未执行，不计入 Passed。
$script:uiAssertionSkipped = 0
if ($null -ne $popupUiaSkipEvidence) { $script:uiAssertionSkipped = $script:uiAssertionGateSkipSites }
$report = [pscustomobject]@{
    Passed = $script:uiAssertionCount
    Failed = 0
    Skipped = $script:uiAssertionSkipped
    AssertionSites = $script:uiAssertionSites.Count
    SelectedDateType = $picker.SelectedDate.GetType().FullName
}
if ($null -ne $popupUiaSkipEvidence) {
    Add-Member -InputObject $report -NotePropertyName PopupUiaGate -NotePropertyValue $popupUiaSkipEvidence
}
$report | ConvertTo-Json
