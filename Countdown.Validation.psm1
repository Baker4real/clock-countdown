#requires -Version 7.0

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Invoke-CountdownValidationProcess {
    param(
        [Parameter(Mandatory)] [string] $PowerShell7Path,
        [Parameter(Mandatory)] [string] $Operation,
        [Parameter(Mandatory)] [string[]] $ArgumentList,
        [hashtable] $EnvironmentVariables = @{}
    )

    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $PowerShell7Path
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    foreach ($argument in $ArgumentList) {
        [void] $startInfo.ArgumentList.Add($argument)
    }
    foreach ($entry in $EnvironmentVariables.GetEnumerator()) {
        if ($null -eq $entry.Value) {
            [void] $startInfo.Environment.Remove([string] $entry.Key)
        }
        else {
            $startInfo.Environment[[string] $entry.Key] = [string] $entry.Value
        }
    }

    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    try {
        if (-not $process.Start()) {
            throw "$Operation 无法启动 PowerShell 7 进程。"
        }
        $stdoutTask = $process.StandardOutput.ReadToEndAsync()
        $stderrTask = $process.StandardError.ReadToEndAsync()
        $process.WaitForExit()
        $output = $stdoutTask.GetAwaiter().GetResult()
        $diagnostics = $stderrTask.GetAwaiter().GetResult()
        $exitCode = $process.ExitCode
    }
    finally {
        $process.Dispose()
    }

    $json = $output.Trim()
    if ($exitCode -ne 0) {
        $diagnosticText = $diagnostics.Trim()
        $failureOutput = if ([string]::IsNullOrWhiteSpace($json)) {
            $diagnosticText
        }
        elseif ([string]::IsNullOrWhiteSpace($diagnosticText)) {
            $json
        }
        else {
            "$json$([Environment]::NewLine)$diagnosticText"
        }
        throw "$Operation 失败，退出码：$exitCode。$([Environment]::NewLine)$failureOutput"
    }
    if ([string]::IsNullOrWhiteSpace($json)) {
        throw "$Operation 未返回 JSON。"
    }

    try {
        $report = $json | ConvertFrom-Json -ErrorAction Stop
    }
    catch {
        throw "$Operation 返回了无效 JSON：$($_.Exception.Message)"
    }

    [pscustomobject]@{
        Json = $json
        Report = $report
    }
}

function Assert-CountdownValidationTestReport {
    param(
        [Parameter(Mandatory)] [object] $Report,
        [Parameter(Mandatory)] [string] $Label
    )

    $failedProperty = $Report.PSObject.Properties['Failed']
    $passedProperty = $Report.PSObject.Properties['Passed']
    if ($null -eq $failedProperty -or $null -eq $passedProperty) {
        throw "${Label}缺少 Passed/Failed 字段。"
    }

    $failed = [int] $failedProperty.Value
    if ($failed -ne 0) {
        throw "${Label}报告包含 $failed 个失败。"
    }
    [int] $passedProperty.Value
}

function Invoke-CountdownValidationSuite {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [string] $Root,
        [Parameter(Mandatory)] [string] $PowerShell7Path,
        [switch] $ParentTransactionMutexHeld
    )

    $resolvedRoot = [System.IO.Path]::GetFullPath($Root)
    $resolvedPowerShell = [System.IO.Path]::GetFullPath($PowerShell7Path)
    if (-not (Test-Path -LiteralPath $resolvedRoot -PathType Container)) {
        throw "验证根目录不存在：$resolvedRoot"
    }
    if (-not (Test-Path -LiteralPath $resolvedPowerShell -PathType Leaf)) {
        throw "验证所需的 PowerShell 7 不存在：$resolvedPowerShell"
    }

    $coreArguments = @(
        '-NoLogo',
        '-NoProfile',
        '-NonInteractive',
        '-File',
        (Join-Path $resolvedRoot 'Countdown.Core.Tests.ps1')
    )
    if ($ParentTransactionMutexHeld) {
        $coreArguments += '-ParentTransactionMutexHeld'
    }
    $core = Invoke-CountdownValidationProcess `
        -PowerShell7Path $resolvedPowerShell `
        -Operation 'Core 回归' `
        -ArgumentList $coreArguments
    $corePassed = Assert-CountdownValidationTestReport -Report $core.Report -Label 'Core 回归'

    $cSharpArguments = @(
        '-NoLogo',
        '-NoProfile',
        '-NonInteractive',
        '-STA',
        '-File',
        (Join-Path $resolvedRoot 'Countdown.CSharp.Tests.ps1')
    )
    if ($ParentTransactionMutexHeld) {
        $cSharpArguments += @(
            '-OfficeDeploymentStagingRoot',
            (Split-Path -Parent $resolvedRoot)
        )
    }
    $cSharp = Invoke-CountdownValidationProcess `
        -PowerShell7Path $resolvedPowerShell `
        -Operation 'C# 回归' `
        -ArgumentList $cSharpArguments
    $cSharpPassed = Assert-CountdownValidationTestReport -Report $cSharp.Report -Label 'C# 回归'

    $ui = Invoke-CountdownValidationProcess `
        -PowerShell7Path $resolvedPowerShell `
        -Operation '兼容 UI 回归' `
        -ArgumentList @(
            '-NoLogo',
            '-NoProfile',
            '-NonInteractive',
            '-STA',
            '-File',
            (Join-Path $resolvedRoot 'Countdown.Ui.Tests.ps1')
        )
    $uiPassed = Assert-CountdownValidationTestReport -Report $ui.Report -Label '兼容 UI 回归'

    $floatingBaselineManifest = Join-Path $resolvedRoot 'assets\ui-references\floating-window\520x390\2026-08-05-v3\manifest.json'
    $floatingBaselineRequired = Test-Path -LiteralPath $floatingBaselineManifest -PathType Leaf
    $mainBaselineManifest = Join-Path $resolvedRoot 'assets\ui-references\main-window\1165x1350\2026-08-05-v3\manifest.json'
    $mainBaselineRequired = Test-Path -LiteralPath $mainBaselineManifest -PathType Leaf
    $visualBaselinesRequired = $floatingBaselineRequired -and $mainBaselineRequired
    $applicationEnvironment = @{
        MINMIN_COUNTDOWN_VISUAL_SOURCE_ROOT = if ($visualBaselinesRequired) { $resolvedRoot } else { $null }
        MINMIN_COUNTDOWN_FLOATING_BASELINE_GENERATE = $null
        MINMIN_COUNTDOWN_MAIN_BASELINE_GENERATE = $null
    }
    $application = Invoke-CountdownValidationProcess `
        -PowerShell7Path $resolvedPowerShell `
        -Operation '应用 validate-only' `
        -EnvironmentVariables $applicationEnvironment `
        -ArgumentList @(
            '-NoLogo',
            '-NoProfile',
            '-NonInteractive',
            '-STA',
            '-File',
            (Join-Path $resolvedRoot '敏敏子倒计时.ps1'),
            '-ValidateOnly'
        )
    $validProperty = $application.Report.PSObject.Properties['Valid']
    if ($null -eq $validProperty -or
        $validProperty.Value -isnot [bool] -or
        -not [bool] $validProperty.Value) {
        throw '应用 validate-only 报告未通过。'
    }
    $requiredUiQualityFields = @(
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
    )
    if ($floatingBaselineRequired) {
        $requiredUiQualityFields += 'FloatingReferenceBaselineValidated'
    }
    foreach ($fieldName in $requiredUiQualityFields) {
        $field = $application.Report.PSObject.Properties[$fieldName]
        if ($null -eq $field -or $field.Value -isnot [bool] -or -not [bool] $field.Value) {
            throw "应用 validate-only 报告的 UI 质量门禁未通过：$fieldName"
        }
    }
    foreach ($stateField in @(
        'FloatingReferenceBaselineRequested',
        'FloatingReferenceBaselineManifestAvailable'
    )) {
        $field = $application.Report.PSObject.Properties[$stateField]
        if ($null -eq $field -or $field.Value -isnot [bool] -or [bool] $field.Value -ne $floatingBaselineRequired) {
            throw "应用 validate-only 的悬浮窗参考基线状态错误：$stateField"
        }
    }
    if (-not $floatingBaselineRequired -and [bool] $application.Report.FloatingReferenceBaselineValidated) {
        throw '不携带参考资产的发布包伪造了悬浮窗参考基线验收。'
    }
    foreach ($sampleCountFieldName in @(
        'MainDynamicCountdownCardSampleCount',
        'MainDynamicNoteCardSampleCount'
    )) {
        $sampleCountField = $application.Report.PSObject.Properties[$sampleCountFieldName]
        if ($null -eq $sampleCountField -or [int] $sampleCountField.Value -lt 1) {
            throw "应用 validate-only 报告未命中动态卡片样本：$sampleCountFieldName"
        }
    }
    $adapterUiProperty = $cSharp.Report.PSObject.Properties['AdapterUiValidated']
    if ($null -eq $adapterUiProperty -or
        $adapterUiProperty.Value -isnot [bool] -or
        -not [bool] $adapterUiProperty.Value) {
        throw 'C# 回归未确认 adapter UI 验证。'
    }

    [pscustomobject]@{
        SchemaVersion = 1
        Valid = $true
        CoreTestsPassed = $corePassed
        CSharpTestsPassed = $cSharpPassed
        UiTestsPassed = $uiPassed
        UiValid = $true
        AdapterUiValidated = $true
        TestOutput = $core.Json
        CSharpTestOutput = $cSharp.Json
        UiTestOutput = $ui.Json
        UiValidationOutput = $application.Json
    }
}

Export-ModuleMember -Function Invoke-CountdownValidationSuite
