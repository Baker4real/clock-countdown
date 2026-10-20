#requires -Version 7.0

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Write-CountdownValidationStage {
    param([string] $Path, [object] $Value)
    $temporary = "$Path.$([guid]::NewGuid().ToString('N')).tmp"
    [IO.File]::WriteAllText($temporary, ($Value | ConvertTo-Json -Depth 8), [Text.UTF8Encoding]::new($false))
    [IO.File]::Move($temporary, $Path, $true)
}

function New-CountdownValidationEvidenceDirectory {
    param([string] $Parent)
    if (-not $Parent) { $Parent = $env:MINMIN_VALIDATION_EVIDENCE_ROOT }
    if (-not $Parent) { $Parent = 'D:/agents/run-evidence/clock-countdown-validation' }
    $path = Join-Path $Parent (([datetime]::UtcNow.ToString('yyyyMMddTHHmmssfff')) + '-' + [guid]::NewGuid().ToString('N'))
    [void][IO.Directory]::CreateDirectory($path)
    return $path
}

function Invoke-CountdownProcess {
    param(
        [Parameter(Mandatory)] [string] $FilePath,
        [Parameter(Mandatory)] [string] $Operation,
        [Parameter(Mandatory)] [string[]] $ArgumentList,
        [hashtable] $EnvironmentVariables = @{},
        [string] $StartFailureMessage = "$Operation 无法启动进程。",
        [string] $IsolatedDesktopName,
        [string] $EvidenceDirectory
    )

    $workRoot = New-CountdownValidationEvidenceDirectory -Parent $EvidenceDirectory
    $stdoutPath = Join-Path $workRoot 'stdout.log'
    $stderrPath = Join-Path $workRoot 'stderr.log'
    $errorPath = Join-Path $workRoot 'error.json'
    $statePath = Join-Path $workRoot 'stage.json'
    $stage = [ordered]@{
        SchemaVersion=1; Operation=$Operation; Status='Running'; StartedAtUtc=[datetime]::UtcNow.ToString('o')
        FinishedAtUtc=$null; ExitCode=$null; OwnerProcessId=$PID
        OwnerStartedAtUtc=[Diagnostics.Process]::GetCurrentProcess().StartTime.ToUniversalTime().ToString('o')
        StdoutPath=$stdoutPath; StderrPath=$stderrPath; ErrorPath=$errorPath
    }
    Write-CountdownValidationStage $statePath $stage
    [Console]::Error.WriteLine("[validation-stage] $($stage.StartedAtUtc) $Operation; evidence=$workRoot")

    # PowerShell -File 的错误跨进程后只剩格式化文本。用同一 pwsh/STA 参数运行
    # 极小入口，捕获原始 ErrorRecord；不改变目标脚本的 PSScriptRoot 或 stdout。
    $fileIndex = [Array]::IndexOf($ArgumentList, '-File')
    if ([IO.Path]::GetFileNameWithoutExtension($FilePath) -eq 'pwsh' -and $fileIndex -ge 0) {
        $quote = { param([string]$Text) "'" + $Text.Replace("'", "''") + "'" }
        $target = $ArgumentList[$fileIndex + 1]
        $tokens = @(& $quote $target)
        for ($i = $fileIndex + 2; $i -lt $ArgumentList.Count; $i++) {
            $argument = $ArgumentList[$i]
            if ($argument -match '^-[A-Za-z][A-Za-z0-9]*$') { $tokens += $argument }
            else { $tokens += (& $quote $argument) }
        }
        $entryPath = Join-Path $workRoot 'entry.ps1'
        $entry = @"
`$ErrorActionPreference = 'Stop'
`$utf8 = [Text.UTF8Encoding]::new(`$false)
[Console]::OutputEncoding = `$utf8
[Console]::InputEncoding = `$utf8
`$OutputEncoding = `$utf8
`$LASTEXITCODE = 0
try {
    & $($tokens -join ' ')
    if (`$?) { exit 0 }
    if (`$LASTEXITCODE -ne 0) { exit `$LASTEXITCODE }
    exit 1
}
catch {
    `$record = [ordered]@{
        exceptionMessage = `$_.Exception.Message
        fullyQualifiedErrorId = `$_.FullyQualifiedErrorId
        positionMessage = `$_.InvocationInfo.PositionMessage
        scriptStackTrace = `$_.ScriptStackTrace
    }
    [IO.File]::WriteAllText($(& $quote $errorPath), (`$record | ConvertTo-Json -Depth 4), `$utf8)
    [Console]::Error.WriteLine(`$_.Exception.Message)
    exit 1
}
"@
        [IO.File]::WriteAllText($entryPath, $entry, [Text.UTF8Encoding]::new($false))
        $ArgumentList = @($ArgumentList[0..$fileIndex]) + @($entryPath)
    }

    $allowUnisolated = [string]::Equals(
        [Environment]::GetEnvironmentVariable('MINMIN_SUITE_ALLOW_UNISOLATED'),
        '1',
        [StringComparison]::Ordinal)
    if (-not $allowUnisolated) {
        # 办公室部署延迟事务上下文只在子进程环境注入同一 opt-out 标记：
        # 生效范围限于该子树，父进程环境与本地交互隔离语义不受影响。
        foreach ($environmentKey in $EnvironmentVariables.Keys) {
            if ([string]::Equals([string] $environmentKey, 'MINMIN_SUITE_ALLOW_UNISOLATED', [StringComparison]::OrdinalIgnoreCase) -and
                [string]::Equals([string] $EnvironmentVariables[$environmentKey], '1', [StringComparison]::Ordinal)) {
                $allowUnisolated = $true
                break
            }
        }
    }
    try {
    if (-not [string]::IsNullOrWhiteSpace($IsolatedDesktopName) -and -not $allowUnisolated) {
        $result = Invoke-CountdownIsolatedDesktopProcess `
            -FilePath $FilePath `
            -Operation $Operation `
            -ArgumentList $ArgumentList `
            -EnvironmentVariables $EnvironmentVariables `
            -StartFailureMessage $StartFailureMessage `
            -DesktopName $IsolatedDesktopName `
            -StdoutPath $stdoutPath -StderrPath $stderrPath
    }
    else {
    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $FilePath
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.StandardOutputEncoding = [Text.UTF8Encoding]::new($false)
    $startInfo.StandardErrorEncoding = [Text.UTF8Encoding]::new($false)
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
    $outputStream = $null
    $errorStream = $null
    try {
        # 1 字节缓冲使小段 stderr 也在子进程运行期间可读。
        $outputStream = [IO.FileStream]::new($stdoutPath, 'Create', 'Write', 'ReadWrite', 1, 'Asynchronous')
        $errorStream = [IO.FileStream]::new($stderrPath, 'Create', 'Write', 'ReadWrite', 1, 'Asynchronous')
        if (-not $process.Start()) {
            throw $StartFailureMessage
        }
        $stdoutTask = $process.StandardOutput.BaseStream.CopyToAsync($outputStream)
        $stderrTask = $process.StandardError.BaseStream.CopyToAsync($errorStream)
        if (-not $process.WaitForExit(1800000)) {
            $process.Kill($true)
            throw "$Operation 超过 1800 秒；已停止本次拥有的子进程。"
        }
        if (-not [Threading.Tasks.Task]::WaitAll([Threading.Tasks.Task[]]@($stdoutTask, $stderrTask), 5000)) {
            throw "$Operation 退出后输出管道未关闭。"
        }
        $outputStream.Dispose(); $outputStream = $null
        $errorStream.Dispose(); $errorStream = $null
        $result = [pscustomobject]@{
            ExitCode = [int] $process.ExitCode
            StandardOutput = [IO.File]::ReadAllText($stdoutPath, [Text.Encoding]::UTF8)
            StandardError = [IO.File]::ReadAllText($stderrPath, [Text.Encoding]::UTF8)
        }
    }
    finally {
        if ($null -ne $outputStream) { $outputStream.Dispose() }
        if ($null -ne $errorStream) { $errorStream.Dispose() }
        $process.Dispose()
    }
    }
    $stage.ExitCode = $result.ExitCode
    if ($result.ExitCode -ne 0) {
        $child = $null
        $detail = (@($result.StandardOutput.Trim(), $result.StandardError.Trim()) | Where-Object { $_ }) -join "`n"
        if (Test-Path -LiteralPath $errorPath -PathType Leaf) {
            $child = [IO.File]::ReadAllText($errorPath, [Text.Encoding]::UTF8) | ConvertFrom-Json
            $detail = "$($child.exceptionMessage)`n$($child.fullyQualifiedErrorId)`n$($child.positionMessage)`n$($child.scriptStackTrace)"
        }
        if ($detail.Length -gt 8000) { $detail = $detail.Substring(0, 8000) + ' [truncated; see evidence]' }
        $exception = [InvalidOperationException]::new("$Operation 失败，退出码：$($result.ExitCode)。日志：$workRoot`n$detail")
        if ($null -ne $child) { $exception.Data['ChildErrorRecord'] = $child }
        throw $exception
    }
    $stage.Status = 'Completed'
    $result | Add-Member -NotePropertyName EvidenceDirectory -NotePropertyValue $workRoot
    $result
    }
    catch {
        $stage.Status = 'Failed'
        $_.Exception.Data['ValidationEvidenceDirectory'] = $workRoot
        throw
    }
    finally {
        $stage.FinishedAtUtc = [datetime]::UtcNow.ToString('o')
        Write-CountdownValidationStage $statePath $stage
        [Console]::Error.WriteLine("[validation-stage] $($stage.FinishedAtUtc) $Operation $($stage.Status)")
    }
}

function Invoke-CountdownIsolatedDesktopProcess {
    param(
        [Parameter(Mandatory)] [string] $FilePath,
        [Parameter(Mandatory)] [string] $Operation,
        [Parameter(Mandatory)] [string[]] $ArgumentList,
        [hashtable] $EnvironmentVariables = @{},
        [string] $StartFailureMessage = "$Operation 无法启动进程。",
        [Parameter(Mandatory)] [string] $DesktopName,
        [Parameter(Mandatory)] [string] $StdoutPath,
        [Parameter(Mandatory)] [string] $StderrPath
    )

    $launcher = Join-Path $PSScriptRoot 'tools\Start-IsolatedSuiteProcess.ps1'
    if (-not (Test-Path -LiteralPath $launcher -PathType Leaf)) {
        throw "$Operation 缺少隔离启动器：$launcher"
    }

    $previousEnv = [ordered]@{}
    foreach ($entry in $EnvironmentVariables.GetEnumerator()) {
        $key = [string] $entry.Key
        $previousEnv[$key] = [Environment]::GetEnvironmentVariable($key)
        if ($null -eq $entry.Value) {
            [Environment]::SetEnvironmentVariable($key, $null)
        }
        else {
            [Environment]::SetEnvironmentVariable($key, [string] $entry.Value)
        }
    }

    try {
        $output = @(
            & $launcher `
                -FilePath $FilePath `
                -ArgumentList $ArgumentList `
                -WorkingDirectory ([System.IO.Directory]::GetCurrentDirectory()) `
                -StdoutPath $stdoutPath `
                -StderrPath $stderrPath `
                -DesktopName $DesktopName
        )
        $result = [pscustomobject]@{
            ExitCode = [int] ($output | Select-Object -Last 1)
            StandardOutput = $(if (Test-Path -LiteralPath $stdoutPath -PathType Leaf) {
                    [System.IO.File]::ReadAllText($stdoutPath)
                }
                else { '' })
            StandardError = $(if (Test-Path -LiteralPath $stderrPath -PathType Leaf) {
                    [System.IO.File]::ReadAllText($stderrPath)
                }
                else { '' })
        }
    }
    finally {
        foreach ($key in $previousEnv.Keys) {
            [Environment]::SetEnvironmentVariable($key, $previousEnv[$key])
        }
    }

    $result
}

function Invoke-CountdownValidationProcess {
    param(
        [Parameter(Mandatory)] [string] $PowerShell7Path,
        [Parameter(Mandatory)] [string] $Operation,
        [Parameter(Mandatory)] [string[]] $ArgumentList,
        [hashtable] $EnvironmentVariables = @{},
        [string] $IsolatedDesktopName,
        [string] $EvidenceDirectory
    )

    $processResult = Invoke-CountdownProcess `
        -FilePath $PowerShell7Path `
        -Operation $Operation `
        -ArgumentList $ArgumentList `
        -EnvironmentVariables $EnvironmentVariables `
        -StartFailureMessage "$Operation 无法启动 PowerShell 7 进程。" `
        -IsolatedDesktopName $IsolatedDesktopName -EvidenceDirectory $EvidenceDirectory
    try {
    $json = ([string] $processResult.StandardOutput).Trim()
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
    catch {
        $path = Join-Path $processResult.EvidenceDirectory 'stage.json'
        $stage = [IO.File]::ReadAllText($path) | ConvertFrom-Json
        $stage.Status = 'Failed'
        Write-CountdownValidationStage $path $stage
        [Console]::Error.WriteLine("[validation-stage] $Operation JSON Failed; evidence=$($processResult.EvidenceDirectory)")
        $_.Exception.Data['ValidationEvidenceDirectory'] = $processResult.EvidenceDirectory
        throw
    }
}

function Remove-CountdownValidationArtifactFiles {
    param([Parameter(Mandatory)] [string[]] $Paths)

    $files = [System.Collections.Generic.List[System.IO.FileInfo]]::new()
    foreach ($path in $Paths) {
        if (-not (Test-Path -LiteralPath $path)) {
            continue
        }

        $item = Get-Item -LiteralPath $path -Force -ErrorAction Stop
        if ($item -isnot [System.IO.FileInfo] -or
            $item.Attributes.HasFlag([System.IO.FileAttributes]::ReparsePoint) -or
            -not [string]::IsNullOrWhiteSpace([string] $item.LinkType)) {
            throw "验证设置清理目标不是普通文件：$path"
        }
        $files.Add($item)
    }

    foreach ($file in $files) {
        Remove-Item -LiteralPath $file.FullName -Force -ErrorAction Stop
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

    foreach ($fieldName in @('CalendarReminderBehavior')) {
        $field = $Report.PSObject.Properties[$fieldName]
        if ($null -eq $field -or $field.Value -isnot [bool] -or -not [bool] $field.Value) {
            throw "应用 validate-only 报告的提醒行为未通过：$fieldName"
        }
    }

    $requiredUiQualityFields = @(
        'MainReferenceDecorationsShareScaleAtMinimumAndWideSizes',
        'MainReferenceDecorationLayerOrderMatches',
        'MainWorkspaceResourcesFollowDayTheme',
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
        'FloatingCountdownSelectionSurvivesRefresh',
        'FloatingStableRefreshScenariosPass',
        'FloatingRenderSignatureGateIsolationBehavior',
        'FloatingQuickAddDayResourcesFollowLocalTheme',
        'FloatingQuickAddIndependentFromApplicationTheme',
        'FloatingQuickAddThemeResourceIsolationBehavior',
        'FloatingQuickAddDayExpandedScrolledCaptureWritten',
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
        'FloatingScrollBarStateCapturesReady',
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
        [pscustomobject]@{ Name = 'Main'; Label = '主窗口'; Required = $MainManifestRequired; CaptureCount = 9 }
        [pscustomobject]@{ Name = 'Floating'; Label = '悬浮窗'; Required = $FloatingManifestRequired; CaptureCount = 5 }
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
        [Parameter(Mandatory)] [string] $DotnetPath,
        [switch] $ParentTransactionMutexHeld,
        [string] $EvidenceDirectory
    )

    $resolvedRoot = [System.IO.Path]::GetFullPath($Root)
    $resolvedPowerShell = [System.IO.Path]::GetFullPath($PowerShell7Path)
    $resolvedDotnet = [System.IO.Path]::GetFullPath($DotnetPath)
    if (-not (Test-Path -LiteralPath $resolvedRoot -PathType Container)) {
        throw "验证根目录不存在：$resolvedRoot"
    }
    if (-not (Test-Path -LiteralPath $resolvedPowerShell -PathType Leaf)) {
        throw "验证所需的 PowerShell 7 不存在：$resolvedPowerShell"
    }
    if (-not (Test-Path -LiteralPath $resolvedDotnet -PathType Leaf)) {
        throw "验证所需的受管 dotnet 不存在：$resolvedDotnet"
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
    # -ParentTransactionMutexHeld 只与安装器 -DeferCommit 同用，即办公室远端部署的 SSH 会话：
    # 该上下文无法在自建隔离桌面初始化 GUI 子进程（0xC0000142），且其窗口对交互用户不可见
    # （docs/办公室部署.md：远端窗口已创建但 IsWindowVisible=false）。经子进程环境注入隔离
    # opt-out 标记让窗口化子步骤直接启动；本地上下文不带标记，隔离 fail-closed 语义不变。
    $windowedChildEnvironment = if ($ParentTransactionMutexHeld) {
        @{ MINMIN_SUITE_ALLOW_UNISOLATED = '1' }
    }
    else {
        @{}
    }
    $core = Invoke-CountdownValidationProcess `
        -PowerShell7Path $resolvedPowerShell `
        -Operation 'Core 回归' `
        -ArgumentList $coreArguments -EvidenceDirectory $EvidenceDirectory
    $corePassed = Assert-CountdownValidationTestReport -Report $core.Report -Label 'Core 回归'

    [void] (Invoke-CountdownProcess `
            -FilePath $resolvedDotnet `
            -Operation '源码应用 Release 构建' `
            -EvidenceDirectory $EvidenceDirectory `
            -ArgumentList @(
                'build',
                (Join-Path $resolvedRoot 'src\MinminCountdown\MinminCountdown.csproj'),
                '--configuration',
                'Release'
            ) `
            -EnvironmentVariables @{
                DOTNET_ROOT = Split-Path -Parent $resolvedDotnet
                DOTNET_CLI_TELEMETRY_OPTOUT = '1'
            })

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
        -ArgumentList $cSharpArguments `
        -EvidenceDirectory $EvidenceDirectory `
        -EnvironmentVariables $windowedChildEnvironment `
        -IsolatedDesktopName 'MinminSuiteIso-ValidateCSharp'
    $cSharpPassed = Assert-CountdownValidationTestReport -Report $cSharp.Report -Label 'C# 回归'

    $ui = Invoke-CountdownValidationProcess `
        -PowerShell7Path $resolvedPowerShell `
        -Operation '兼容 UI 回归' `
        -EvidenceDirectory $EvidenceDirectory `
        -ArgumentList @(
            '-NoLogo',
            '-NoProfile',
            '-NonInteractive',
            '-STA',
            '-File',
            (Join-Path $resolvedRoot 'Countdown.Ui.Tests.ps1')
        ) `
        -EnvironmentVariables $windowedChildEnvironment `
        -IsolatedDesktopName 'MinminSuiteIso-ValidateUi'
    $uiPassed = Assert-CountdownValidationTestReport -Report $ui.Report -Label '兼容 UI 回归'

    $floatingBaselineManifest = Join-Path $resolvedRoot 'assets\ui-references\floating-window\520x390\2026-10-14-v55\manifest.json'
    $floatingBaselineRequired = Test-Path -LiteralPath $floatingBaselineManifest -PathType Leaf
    $mainBaselineManifest = Join-Path $resolvedRoot 'assets\ui-references\main-window\864x1000\2026-10-14-v55\manifest.json'
    $mainBaselineRequired = Test-Path -LiteralPath $mainBaselineManifest -PathType Leaf
    $visualBaselinesRequired = $floatingBaselineRequired -and $mainBaselineRequired
    $applicationEnvironment = @{
        MINMIN_COUNTDOWN_VISUAL_SOURCE_ROOT = if ($visualBaselinesRequired) { $resolvedRoot } else { $null }
        MINMIN_COUNTDOWN_FLOATING_BASELINE_GENERATE = $null
        MINMIN_COUNTDOWN_MAIN_BASELINE_GENERATE = $null
    }
    if ($ParentTransactionMutexHeld) {
        $applicationEnvironment['MINMIN_SUITE_ALLOW_UNISOLATED'] = '1'
    }
    $applicationValidationRoot = 'D:\Codex\.codex\tmp\clock-countdown-validation-suite'
    [System.IO.Directory]::CreateDirectory($applicationValidationRoot) | Out-Null
    $applicationValidationId = [guid]::NewGuid().ToString('N')
    $applicationSettingsPath = Join-Path $applicationValidationRoot "$applicationValidationId.settings.json"
    $applicationDiagnosticPath = Join-Path $applicationValidationRoot "$applicationValidationId.application.jsonl"
    $applicationFailureEvidence = $null
    try {
        $application = Invoke-CountdownValidationProcess `
            -PowerShell7Path $resolvedPowerShell `
            -Operation '应用 validate-only' `
            -EvidenceDirectory $EvidenceDirectory `
            -EnvironmentVariables $applicationEnvironment `
            -ArgumentList @(
                '-NoLogo',
                '-NoProfile',
                '-NonInteractive',
                '-STA',
                '-File',
                (Join-Path $resolvedRoot '敏敏子倒计时.ps1'),
                '-ValidateOnly',
                '-SettingsFile',
                $applicationSettingsPath,
                '-DiagnosticLogPath',
                $applicationDiagnosticPath
            ) `
            -IsolatedDesktopName 'MinminSuiteIso-ValidateApp'
    }
    catch {
        $applicationFailureEvidence = [string] $_.Exception.Data['ValidationEvidenceDirectory']
        throw
    }
    finally {
        # 应用失败时，它写的诊断日志也是证据：清理前先复制进本阶段证据目录。
        # 复制失败只写 stderr，不能盖住应用本身的失败。
        if (-not [string]::IsNullOrWhiteSpace($applicationFailureEvidence)) {
            foreach ($diagnosticFile in @($applicationDiagnosticPath, "$applicationDiagnosticPath.1", "$applicationDiagnosticPath.2", "$applicationDiagnosticPath.3")) {
                try {
                    if ([System.IO.File]::Exists($diagnosticFile)) {
                        [System.IO.File]::Copy($diagnosticFile, (Join-Path $applicationFailureEvidence ([System.IO.Path]::GetFileName($diagnosticFile))), $true)
                    }
                }
                catch {
                    [Console]::Error.WriteLine("[validation-stage] 应用诊断日志未能复制进证据目录：$diagnosticFile；$($_.Exception.Message)")
                }
            }
        }
        Remove-CountdownValidationArtifactFiles -Paths @(
                $applicationSettingsPath,
                "$applicationSettingsPath.tmp",
                "$applicationSettingsPath.backup1",
                "$applicationSettingsPath.backup1.tmp",
                "$applicationSettingsPath.backup1.rollback",
                "$applicationSettingsPath.backup2",
                "$applicationSettingsPath.backup2.tmp",
                "$applicationSettingsPath.backup2.rollback",
                $applicationDiagnosticPath,
                "$applicationDiagnosticPath.1",
                "$applicationDiagnosticPath.2",
                "$applicationDiagnosticPath.3"
            )
    }
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

function Invoke-CountdownCandidatePreflight {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [string] $Root,
        [Parameter(Mandatory)] [string] $PowerShell7Path,
        [Parameter(Mandatory)] [string] $EvidenceDirectory,
        [switch] $OfficeSession
    )
    Import-Module (Join-Path $Root 'Countdown.DeploymentPreflight.psm1') -Force
    [Console]::Error.WriteLine('[validation-stage] 部署依赖预检')
    $null = Invoke-CountdownDeploymentPreflight -OutputPath (Join-Path $EvidenceDirectory 'dependencies.json')
    $app = Join-Path $Root 'app'
    $dllHash = (Get-FileHash -LiteralPath (Join-Path $app 'MinminCountdown.dll') -Algorithm SHA256).Hash
    $environment = if ($OfficeSession) { @{ MINMIN_SUITE_ALLOW_UNISOLATED='1' } } else { @{} }
    $window = Invoke-CountdownValidationProcess -PowerShell7Path $PowerShell7Path -Operation '候选窗口短验证' `
        -ArgumentList @('-NoLogo','-NoProfile','-NonInteractive','-STA','-File',
            (Join-Path $Root 'tools/Test-CountdownWindowPreflight.ps1'),'-AppPath',$app,'-ExpectedDllSha256',$dllHash) `
        -EvidenceDirectory $EvidenceDirectory -EnvironmentVariables $environment -IsolatedDesktopName 'MinminSuiteIso-Preflight'
    $null = Assert-CountdownValidationTestReport -Report $window.Report -Label '候选窗口短验证'
    Write-CountdownValidationStage (Join-Path $EvidenceDirectory 'window.json') $window.Report
}

Export-ModuleMember -Function Assert-CountdownApplicationValidationReport, Invoke-CountdownValidationSuite, Invoke-CountdownCandidatePreflight
