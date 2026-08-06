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

function Assert-CountdownApplicationValidationReport {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [object] $Report,
        [Parameter(Mandatory)] [bool] $MainManifestRequired,
        [Parameter(Mandatory)] [bool] $FloatingManifestRequired
    )

    $validProperty = $Report.PSObject.Properties['Valid']
    if ($null -eq $validProperty -or
        $validProperty.Value -isnot [bool] -or
        -not [bool] $validProperty.Value) {
        throw '应用 validate-only 报告未通过。'
    }

    $contractProperty = $Report.PSObject.Properties['ValidationReportContractBehavior']
    if ($null -eq $contractProperty -or $contractProperty.Value -isnot [bool]) {
        throw '应用 validate-only 报告缺少布尔字段：ValidationReportContractBehavior'
    }
    if (-not [bool] $contractProperty.Value) {
        throw '应用 validate-only 报告契约未通过：ValidationReportContractBehavior'
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
    foreach ($fieldName in $requiredUiQualityFields) {
        $field = $Report.PSObject.Properties[$fieldName]
        if ($null -eq $field -or $field.Value -isnot [bool] -or -not [bool] $field.Value) {
            throw "应用 validate-only 报告的 UI 质量门禁未通过：$fieldName"
        }
    }

    if ($MainManifestRequired -ne $FloatingManifestRequired) {
        throw '主窗口与悬浮窗视觉 manifest 模式不一致。'
    }

    $baselines = @(
        [pscustomobject]@{ Name = 'Main'; Label = '主窗口'; Required = $MainManifestRequired; CaptureCount = 12 }
        [pscustomobject]@{ Name = 'Floating'; Label = '悬浮窗'; Required = $FloatingManifestRequired; CaptureCount = 6 }
    )
    $baselineStates = [ordered]@{}
    foreach ($baseline in $baselines) {
        $state = [ordered]@{}
        foreach ($stateName in @('Requested', 'ManifestAvailable', 'Validated')) {
            $fieldName = "$($baseline.Name)ReferenceBaseline$stateName"
            $field = $Report.PSObject.Properties[$fieldName]
            if ($null -eq $field -or $field.Value -isnot [bool]) {
                throw "应用 validate-only 的$($baseline.Label)参考基线状态错误：$fieldName"
            }
            $state[$stateName] = [bool] $field.Value
        }
        $baselineStates[$baseline.Name] = [pscustomobject] $state
    }

    if ($baselineStates.Main.ManifestAvailable -ne $baselineStates.Floating.ManifestAvailable) {
        throw '主窗口与悬浮窗视觉 manifest 模式不一致。'
    }

    foreach ($baseline in $baselines) {
        $state = $baselineStates[$baseline.Name]
        if ($state.Requested -ne $baseline.Required -or
            $state.ManifestAvailable -ne $baseline.Required -or
            $state.Validated -ne $baseline.Required) {
            throw "应用 validate-only 的$($baseline.Label)参考基线状态组合无效：Requested=$($state.Requested); ManifestAvailable=$($state.ManifestAvailable); Validated=$($state.Validated)"
        }

        $captureFieldName = "$($baseline.Name)ReferenceBaselineCaptures"
        $captureField = $Report.PSObject.Properties[$captureFieldName]
        if ($null -eq $captureField) {
            throw "应用 validate-only 报告缺少 captures 字段：$captureFieldName"
        }
        $actualCaptureCount = @($captureField.Value).Count
        if ($baseline.Required) {
            if ($actualCaptureCount -ne $baseline.CaptureCount) {
                throw "应用 validate-only 的$($baseline.Label)参考基线必须包含 $($baseline.CaptureCount) 个 captures，实际为 $actualCaptureCount。"
            }
        }
        elseif ($actualCaptureCount -ne 0) {
            throw "不携带参考资产的发布包伪造了$($baseline.Label)参考基线 captures。"
        }
    }

    foreach ($sampleCountFieldName in @(
            'MainDynamicCountdownCardSampleCount',
            'MainDynamicNoteCardSampleCount'
        )) {
        $sampleCountField = $Report.PSObject.Properties[$sampleCountFieldName]
        $isInteger = $null -ne $sampleCountField -and (
            $sampleCountField.Value -is [sbyte] -or
            $sampleCountField.Value -is [byte] -or
            $sampleCountField.Value -is [int16] -or
            $sampleCountField.Value -is [uint16] -or
            $sampleCountField.Value -is [int32] -or
            $sampleCountField.Value -is [uint32] -or
            $sampleCountField.Value -is [int64] -or
            $sampleCountField.Value -is [uint64]
        )
        if (-not $isInteger -or [int64] $sampleCountField.Value -lt 1) {
            throw "应用 validate-only 报告未命中动态卡片样本：$sampleCountFieldName"
        }
    }

    $Report
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

    $floatingBaselineManifest = Join-Path $resolvedRoot 'assets\ui-references\floating-window\520x390\2026-08-06-v6\manifest.json'
    $floatingBaselineRequired = Test-Path -LiteralPath $floatingBaselineManifest -PathType Leaf
    $mainBaselineManifest = Join-Path $resolvedRoot 'assets\ui-references\main-window\1165x1350\2026-08-06-v6\manifest.json'
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
    $null = Assert-CountdownApplicationValidationReport `
        -Report $application.Report `
        -MainManifestRequired $mainBaselineRequired `
        -FloatingManifestRequired $floatingBaselineRequired
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

Export-ModuleMember -Function Assert-CountdownApplicationValidationReport, Invoke-CountdownValidationSuite
