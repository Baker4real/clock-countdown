#requires -Version 7.0

[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
$script:assertions = 0

function Invoke-CheckedNativeCommand {
    param(
        [Parameter(Mandatory)] [string] $FilePath,
        [Parameter(Mandatory)] [string[]] $ArgumentList,
        [Parameter(Mandatory)] [string] $Operation
    )

    $output = @(& $FilePath @ArgumentList)
    $exitCode = $LASTEXITCODE
    if ($exitCode -ne 0) {
        $detail = if ($output.Count -gt 0) {
            $output -join [Environment]::NewLine
        }
        else {
            '无标准输出。'
        }
        throw "$Operation 失败，退出码：$exitCode。$([Environment]::NewLine)$detail"
    }

    return $output -join [Environment]::NewLine
}

function Invoke-NativeCommandResult {
    param(
        [Parameter(Mandatory)] [string] $FilePath,
        [Parameter(Mandatory)] [string[]] $ArgumentList
    )

    $output = @(& $FilePath @ArgumentList)
    [pscustomobject]@{
        ExitCode = $LASTEXITCODE
        Output = $output -join [Environment]::NewLine
    }
}

function Assert-Equal {
    param(
        [AllowNull()] $Expected,
        [AllowNull()] $Actual,
        [Parameter(Mandatory)] [string] $Name
    )

    $script:assertions++
    if ($Expected -ne $Actual) {
        throw "$Name 失败：expected=[$Expected] actual=[$Actual]"
    }
}

$verificationRunner = Join-Path $PSScriptRoot 'verification\MinminCountdown.Verification.exe'
$application = Join-Path $PSScriptRoot 'app\MinminCountdown.exe'
$adapter = Join-Path $PSScriptRoot '敏敏子倒计时.ps1'
$settingsModule = Join-Path $PSScriptRoot 'Countdown.Settings.psm1'
$runtimeModule = Join-Path $PSScriptRoot 'Countdown.Runtime.psm1'
$pwsh = (Get-Process -Id $PID).Path

if (-not (Test-Path -LiteralPath $verificationRunner -PathType Leaf)) {
    throw "未找到自包含 C# 验证程序：$verificationRunner。请先运行 Build-CountdownApplication.ps1。"
}
if (-not (Test-Path -LiteralPath $application -PathType Leaf)) {
    throw "未找到待验证的 C# 应用宿主：$application。请先运行 Build-CountdownApplication.ps1。"
}
if (-not (Test-Path -LiteralPath $adapter -PathType Leaf)) {
    throw "未找到 PowerShell 薄 adapter：$adapter"
}
if (-not (Test-Path -LiteralPath $settingsModule -PathType Leaf)) {
    throw "未找到 PowerShell 设置契约 module：$settingsModule"
}
if (-not (Test-Path -LiteralPath $runtimeModule -PathType Leaf)) {
    throw "未找到 PowerShell 运行协议 module：$runtimeModule"
}
if (-not [string]::Equals(
        [System.IO.Path]::GetFileName($pwsh),
        'pwsh.exe',
        [System.StringComparison]::OrdinalIgnoreCase
    )) {
    throw "C# 验证必须由 pwsh.exe 执行，当前宿主：$pwsh"
}

$systemTemp = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath())
$systemTempPrefix = $systemTemp.TrimEnd(
    [System.IO.Path]::DirectorySeparatorChar,
    [System.IO.Path]::AltDirectorySeparatorChar
) + [System.IO.Path]::DirectorySeparatorChar
$testRoot = [System.IO.Path]::GetFullPath(
    (Join-Path $systemTemp ("MinminCountdown-CSharpTests-" + [guid]::NewGuid().ToString('N')))
)
if (-not $testRoot.StartsWith($systemTempPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "拒绝在系统临时目录外创建 C# 测试数据：$testRoot"
}

$fixturePath = Join-Path $testRoot 'settings-input.json'
$powerShellCanonicalPath = Join-Path $testRoot 'settings-powershell-canonical.json'
$roundTripPath = Join-Path $testRoot 'settings-round-trip.json'
$mutexSettingsPath = Join-Path $testRoot 'mutex.settings.json'
$mutexReadyPath = Join-Path $testRoot 'mutex-ready.json'
$mutexHolderOutputPath = Join-Path $testRoot 'mutex-holder.stdout.txt'
$mutexHolderErrorPath = Join-Path $testRoot 'mutex-holder.stderr.txt'
$directDiagnosticPath = Join-Path $testRoot 'direct-application.jsonl'
$adapterDiagnosticPath = Join-Path $testRoot 'adapter-application.jsonl'
$noteExercisePath = Join-Path $testRoot 'note-exercise.settings.json'
$noteExerciseDiagnosticPath = Join-Path $testRoot 'note-exercise-application.jsonl'
$interactiveRoot = Join-Path $testRoot 'interactive-application-root'
$interactiveApplicationDirectory = Join-Path $interactiveRoot 'app'
$interactiveApplicationPath = Join-Path $interactiveApplicationDirectory 'MinminCountdown.exe'
$interactiveAdapterPath = Join-Path $interactiveRoot '敏敏子倒计时.ps1'
$interactiveSettingsPath = Join-Path $interactiveRoot 'settings.json'
$interactiveStartupRequestPath = Join-Path $interactiveRoot 'startup-request.json'
$interactiveRuntimeStatusPath = Join-Path $interactiveRoot 'runtime-status.json'
$interactiveAcceptanceStatusPath = Join-Path $interactiveRoot 'acceptance-status.json'
$interactiveDiagnosticPath = Join-Path $interactiveRoot 'logs\application.jsonl'
$interactiveAdapterOutputPath = Join-Path $testRoot 'interactive-adapter.stdout.txt'
$interactiveAdapterErrorPath = Join-Path $testRoot 'interactive-adapter.stderr.txt'
$duplicateApplicationOutputPath = Join-Path $testRoot 'duplicate-application.stdout.txt'
$duplicateApplicationErrorPath = Join-Path $testRoot 'duplicate-application.stderr.txt'
$requiredRequestOutputPath = Join-Path $testRoot 'required-request.stdout.txt'
$requiredRequestErrorPath = Join-Path $testRoot 'required-request.stderr.txt'
$unknownSchemaOutputPath = Join-Path $testRoot 'unknown-schema.stdout.txt'
$unknownSchemaErrorPath = Join-Path $testRoot 'unknown-schema.stderr.txt'
$utf8NoBom = [System.Text.UTF8Encoding]::new($false)
$mutexHolderProcess = $null
$interactiveAdapterProcess = $null
$interactiveApplicationProcess = $null
$duplicateApplicationProcess = $null
$fixture = @'
{
  "schemaVersion": 7,
  "timeZoneId": "China Standard Time",
  "displayPrecision": "Second",
  "monthlySalary": 23000,
  "workStart": "09:00",
  "workEnd": "18:00",
  "tags": [
    {
      "id": "work",
      "name": "Work",
      "color": "#F4B183"
    }
  ],
  "countdowns": [
    {
      "id": "migration-test",
      "name": "Migration verification",
      "targetUtc": "2026-12-31T10:00:00.0000000+00:00",
      "tagIds": [
        "work"
      ]
    }
  ],
  "notes": [
    {
      "id": "migration-note",
      "text": "Round-trip verification",
      "date": "2026-12-31",
      "isCompleted": false,
      "tagIds": [
        "work"
      ],
      "reminderIntervalMinutes": null,
      "reminderStartUtc": null
    }
  ]
}
'@

try {
    New-Item -ItemType Directory -Path $testRoot | Out-Null
    [System.IO.File]::WriteAllText($fixturePath, $fixture, $utf8NoBom)
    $fixtureHash = (Get-FileHash -LiteralPath $fixturePath -Algorithm SHA256).Hash
    Import-Module -Name $settingsModule -Force
    Import-Module -Name $runtimeModule -Force
    $powerShellState = ConvertFrom-CountdownSettingsObject -Settings (
        Get-Content -LiteralPath $fixturePath -Raw -Encoding UTF8 | ConvertFrom-Json
    )
    $powerShellCanonical = ConvertTo-CountdownSettingsJson -State $powerShellState
    [System.IO.File]::WriteAllText($powerShellCanonicalPath, $powerShellCanonical, $utf8NoBom)
    $powerShellCanonicalHash = (Get-FileHash -LiteralPath $powerShellCanonicalPath -Algorithm SHA256).Hash

    $verifyOutput = Invoke-CheckedNativeCommand -FilePath $verificationRunner -Operation '运行 C# 内建验证' -ArgumentList @(
        'verify',
        '--json',
        '--settings-file',
        $fixturePath
    )
    $verifyOutput | ConvertFrom-Json | Out-Null
    Assert-Equal $fixtureHash (Get-FileHash -LiteralPath $fixturePath -Algorithm SHA256).Hash 'C# 内建验证保持输入设置不变'

    $roundTripOutput = Invoke-CheckedNativeCommand -FilePath $verificationRunner -Operation '运行 C# 设置 round-trip' -ArgumentList @(
        'round-trip',
        '--input',
        $powerShellCanonicalPath,
        '--output',
        $roundTripPath,
        '--json'
    )
    $roundTripOutput | ConvertFrom-Json | Out-Null
    Assert-Equal $powerShellCanonicalHash (Get-FileHash -LiteralPath $powerShellCanonicalPath -Algorithm SHA256).Hash 'C# round-trip 保持 PowerShell 输入设置不变'
    Assert-Equal $fixtureHash (Get-FileHash -LiteralPath $fixturePath -Algorithm SHA256).Hash '跨实现 round-trip 保持来源 fixture 不变'
    if (-not (Test-Path -LiteralPath $roundTripPath -PathType Leaf)) {
        throw "C# round-trip 未生成显式输出：$roundTripPath"
    }
    $canonicalSettings = Get-Content -LiteralPath $roundTripPath -Raw -Encoding UTF8 | ConvertFrom-Json
    Assert-Equal 8 $canonicalSettings.schemaVersion 'C# round-trip 输出 schemaVersion'
    Assert-Equal 'China Standard Time' $canonicalSettings.timeZoneId 'C# round-trip 输出时区'
    $powerShellReloadedState = ConvertFrom-CountdownSettingsObject -Settings $canonicalSettings
    $powerShellReloadedCanonical = ConvertTo-CountdownSettingsJson -State $powerShellReloadedState
    Assert-Equal $powerShellCanonical $powerShellReloadedCanonical 'PowerShell/C# 双向领域值等价'

    $powerShellMutex = Enter-CountdownSettingsWriteMutex -Path $mutexSettingsPath -TimeoutSeconds 1
    try {
        $blockedCSharpProbe = Invoke-NativeCommandResult -FilePath $verificationRunner -ArgumentList @(
            'mutex-probe',
            '--settings-path',
            $mutexSettingsPath,
            '--timeout-seconds',
            '1',
            '--json'
        )
        Assert-Equal 1 $blockedCSharpProbe.ExitCode 'PowerShell 持锁时 C# probe 超时退出码'
        $blockedCSharpProbeJson = $blockedCSharpProbe.Output | ConvertFrom-Json
        Assert-Equal $false ([bool] $blockedCSharpProbeJson.acquired) 'PowerShell 持锁时 C# 不得获取 mutex'
    }
    finally {
        Exit-CountdownSettingsWriteMutex -Mutex $powerShellMutex
    }

    $releasedCSharpProbe = Invoke-CheckedNativeCommand -FilePath $verificationRunner -Operation '验证 PowerShell 释放后 C# 获取 mutex' -ArgumentList @(
        'mutex-probe',
        '--settings-path',
        $mutexSettingsPath,
        '--timeout-seconds',
        '1',
        '--json'
    ) | ConvertFrom-Json
    Assert-Equal $true ([bool] $releasedCSharpProbe.acquired) 'PowerShell 释放后 C# 可获取 mutex'

    $mutexHolderArguments = @(
        'hold-mutex',
        '--settings-path',
        ('"{0}"' -f $mutexSettingsPath),
        '--ready-file',
        ('"{0}"' -f $mutexReadyPath),
        '--milliseconds',
        '3000',
        '--json'
    )
    $mutexHolderProcess = Start-Process -FilePath $verificationRunner -ArgumentList $mutexHolderArguments -PassThru -WindowStyle Hidden `
        -RedirectStandardOutput $mutexHolderOutputPath -RedirectStandardError $mutexHolderErrorPath
    $mutexReadyDeadline = [DateTimeOffset]::UtcNow.AddSeconds(5)
    while (-not (Test-Path -LiteralPath $mutexReadyPath -PathType Leaf) -and
        -not $mutexHolderProcess.HasExited -and
        [DateTimeOffset]::UtcNow -lt $mutexReadyDeadline) {
        Start-Sleep -Milliseconds 50
    }
    if (-not (Test-Path -LiteralPath $mutexReadyPath -PathType Leaf)) {
        $holderError = if (Test-Path -LiteralPath $mutexHolderErrorPath) {
            Get-Content -LiteralPath $mutexHolderErrorPath -Raw
        }
        else {
            ''
        }
        throw "C# mutex holder 未进入持锁状态：$holderError"
    }

    $powerShellTimedOut = $false
    $unexpectedPowerShellMutex = $null
    try {
        $unexpectedPowerShellMutex = Enter-CountdownSettingsWriteMutex -Path $mutexSettingsPath -TimeoutSeconds 1
    }
    catch {
        if ($_.Exception.Message -notlike '等待设置写入锁超时*') {
            throw
        }
        $powerShellTimedOut = $true
    }
    finally {
        if ($null -ne $unexpectedPowerShellMutex) {
            Exit-CountdownSettingsWriteMutex -Mutex $unexpectedPowerShellMutex
        }
    }
    Assert-Equal $true $powerShellTimedOut 'C# 持锁时 PowerShell 超时'
    if (-not $mutexHolderProcess.WaitForExit(5000)) {
        throw 'C# mutex holder 未在预期时间内释放。'
    }
    Assert-Equal 0 $mutexHolderProcess.ExitCode 'C# mutex holder 退出码'
    $mutexHolderProcess = $null
    $releasedPowerShellMutex = Enter-CountdownSettingsWriteMutex -Path $mutexSettingsPath -TimeoutSeconds 1
    try {
        Assert-Equal $true ($null -ne $releasedPowerShellMutex) 'C# 释放后 PowerShell 可获取 mutex'
    }
    finally {
        Exit-CountdownSettingsWriteMutex -Mutex $releasedPowerShellMutex
    }

    $canonicalHash = (Get-FileHash -LiteralPath $roundTripPath -Algorithm SHA256).Hash
    Invoke-CheckedNativeCommand -FilePath $application -Operation '运行 C# 应用 --validate-only' -ArgumentList @(
        '--validate-only',
        '--settings-file',
        $roundTripPath,
        '--diagnostic-log-path',
        $directDiagnosticPath
    ) | Out-Null
    Assert-Equal $canonicalHash (Get-FileHash -LiteralPath $roundTripPath -Algorithm SHA256).Hash '应用 validate-only 保持设置不变'

    $adapterValidationOutput = Invoke-CheckedNativeCommand -FilePath $pwsh -Operation '经 PowerShell adapter 运行 C# 应用 --validate-only' -ArgumentList @(
        '-NoLogo',
        '-NoProfile',
        '-STA',
        '-File',
        $adapter,
        '-ValidateOnly',
        '-SettingsFile',
        $roundTripPath,
        '-DiagnosticLogPath',
        $adapterDiagnosticPath
    )
    $adapterValidation = $adapterValidationOutput | ConvertFrom-Json
    Assert-Equal $true ([bool] $adapterValidation.Valid) 'adapter validate-only 返回有效 C# 验证报告'
    Assert-Equal $true ([bool] $adapterValidation.DesktopWorkspaceModuleDeep) '桌面工作区 deep module 验收'
    Assert-Equal $true ([bool] $adapterValidation.StartupAcceptanceProtocolModuleDeep) '启动验收 protocol deep module 验收'
    Assert-Equal $true ([bool] $adapterValidation.WorkdayCalendarModuleDeep) '工作日历 deep module 验收'
    Assert-Equal $true ([bool] $adapterValidation.ValidationReportModuleDeep) '验证报告 deep module 验收'
    Assert-Equal $true ([bool] $adapterValidation.ThemeControlInventoryCovered) 'adapter 验证主题控件资产清单'
    Assert-Equal $true ([bool] $adapterValidation.ThemedControlTemplatesApplied) 'adapter 验证真实 WPF 主题模板'
    Assert-Equal $true ([bool] $adapterValidation.LiveDatePickerPopupIsThemed) 'adapter 验证真实 DatePicker Popup 主题'
    Assert-Equal $true ([bool] $adapterValidation.OfficialCharacterBackgroundLoaded) 'adapter 验证角色图片资产已加载'
    Assert-Equal $true ([bool] $adapterValidation.SavePreferencesButtonTextFits) '设置保存按钮文字完整显示'
    Assert-Equal $true ([bool] $adapterValidation.SavePreferencesButtonIsFullyVisible) '设置保存按钮完整位于主窗口内'
    Assert-Equal $true ([bool] $adapterValidation.SavePreferencesButtonIsFullyVisibleAtMinimumWidth) '最小主窗口宽度下设置保存按钮仍完整可见'
    Assert-Equal $true ([bool] $adapterValidation.FloatingPlushTouchesTaskCard) '悬浮窗三个角色都贴合卡片边缘'
    Assert-Equal $true ([bool] $adapterValidation.FloatingPlushDoesNotObscureTaskCardContent) '悬浮窗角色不遮挡卡片内容'
    Assert-Equal $true ([bool] $adapterValidation.FloatingPlushVisibleEdgesMeetCard) '悬浮窗角色可见像素贴合卡片边缘'
    Assert-Equal $true ([bool] $adapterValidation.ActiveNotesGrouped) '未完成便签收拢在独立分组中'
    Assert-Equal $true ([bool] $adapterValidation.ActiveNotesExpandedByDefault) '未完成便签分组默认展开'
    Assert-Equal $true ([bool] $adapterValidation.ActiveNotesKeepDateOrder) '未完成便签组内保持现有日期顺序'
    Assert-Equal '24,60,24,60' ([string]::Join(',', @($adapterValidation.NoteReminderTimeOptionCounts))) '主窗口和悬浮窗的便签提醒时间选项完整'
    Assert-Equal $true ([bool] $adapterValidation.CrossDayReminderSelectionWorks) '便签提醒支持跨天选择并按北京时间触发'
    Assert-Equal $true ([bool] $adapterValidation.ReminderSummaryAdvancesAfterFirstDue) '首次提醒到期后主窗口摘要显示下一循环时间'
    Assert-Equal $true ([bool] $adapterValidation.OneTimeReminderLifecycleWorks) '一次性提醒支持离线补发并在送达后跨重启去重'
    Assert-Equal $canonicalHash (Get-FileHash -LiteralPath $roundTripPath -Algorithm SHA256).Hash 'adapter validate-only 保持设置不变'

    [System.IO.File]::Copy($roundTripPath, $noteExercisePath)
    $noteExerciseOutput = Invoke-CheckedNativeCommand -FilePath $pwsh -Operation '经真实 UI 点击验证跨天便签提醒' -ArgumentList @(
        '-NoLogo',
        '-NoProfile',
        '-STA',
        '-File',
        $adapter,
        '-ValidateOnly',
        '-ExerciseNoteAdd',
        '-SettingsFile',
        $noteExercisePath,
        '-DiagnosticLogPath',
        $noteExerciseDiagnosticPath
    )
    $noteExercise = $noteExerciseOutput | ConvertFrom-Json
    Assert-Equal $true ([bool] $noteExercise.NoteAddViaClick) '主窗口真实点击添加便签'
    Assert-Equal $true ([bool] $noteExercise.NoteReminderViaSelector) '主窗口真实点击保存跨天提醒时间'
    Assert-Equal $true ([bool] $noteExercise.FloatingCrossDayReminderViaClick) '悬浮窗真实点击保存跨天提醒时间'
    Assert-Equal $true ([bool] $noteExercise.MainNoReminderViaClick) '主窗口真实点击保存不提醒'
    Assert-Equal $true ([bool] $noteExercise.MainOnceReminderViaClick) '主窗口真实点击保存一次性提醒'
    Assert-Equal $true ([bool] $noteExercise.MainRepeatingReminderViaClick) '主窗口真实点击保存重复提醒'
    Assert-Equal $true ([bool] $noteExercise.FloatingNoReminderViaClick) '悬浮窗真实点击保存不提醒'
    Assert-Equal $true ([bool] $noteExercise.FloatingOnceReminderViaClick) '悬浮窗真实点击保存一次性提醒'
    Assert-Equal $true ([bool] $noteExercise.FloatingRepeatingReminderViaClick) '悬浮窗真实点击保存重复提醒'
    $noteExerciseSettings = Get-Content -LiteralPath $noteExercisePath -Raw -Encoding UTF8 | ConvertFrom-Json
    Assert-Equal $true (@($noteExerciseSettings.notes | Where-Object text -eq '验证快速跨天提醒').Count -eq 1) '悬浮跨天提醒真实写入隔离设置'

    New-Item -ItemType Directory -Path $interactiveApplicationDirectory | Out-Null
    Copy-Item -LiteralPath $application -Destination $interactiveApplicationPath
    Copy-Item -LiteralPath $adapter -Destination $interactiveAdapterPath
    $requiredRequestProcess = Start-Process -FilePath $interactiveApplicationPath -ArgumentList @('--require-startup-request') `
        -PassThru -Wait -WindowStyle Hidden `
        -RedirectStandardOutput $requiredRequestOutputPath `
        -RedirectStandardError $requiredRequestErrorPath
    try {
        Assert-Equal 1 $requiredRequestProcess.ExitCode '生产模式缺失启动请求时 C# 宿主 fail-closed'
        $requiredRequestError = Get-Content -LiteralPath $requiredRequestErrorPath -Raw -Encoding UTF8
        Assert-Equal $true $requiredRequestError.Contains('启动请求存在但无法验证', [System.StringComparison]::Ordinal) '生产模式报告缺失启动请求而不进入可编辑窗口'
    }
    finally {
        $requiredRequestProcess.Dispose()
    }
    [System.IO.File]::WriteAllText(
        $interactiveStartupRequestPath,
        '{"schemaVersion":999,"launchToken":"unknown-schema","acceptanceState":"active"}',
        $utf8NoBom
    )
    $unknownSchemaProcess = Start-Process -FilePath $interactiveApplicationPath -ArgumentList @('--require-startup-request') `
        -PassThru -Wait -WindowStyle Hidden `
        -RedirectStandardOutput $unknownSchemaOutputPath `
        -RedirectStandardError $unknownSchemaErrorPath
    try {
        Assert-Equal 1 $unknownSchemaProcess.ExitCode '生产模式拒绝未知启动请求 schema'
        $unknownSchemaError = Get-Content -LiteralPath $unknownSchemaErrorPath -Raw -Encoding UTF8
        Assert-Equal $true $unknownSchemaError.Contains('启动请求存在但无法验证', [System.StringComparison]::Ordinal) 'C# 对未知启动请求 schema 保持 fail-closed'
    }
    finally {
        $unknownSchemaProcess.Dispose()
    }
    $interactiveLegacySettings = $fixture | ConvertFrom-Json
    $interactiveLegacySettings.schemaVersion = 6
    foreach ($note in @($interactiveLegacySettings.notes)) {
        $note.PSObject.Properties.Remove('reminderIntervalMinutes')
        $note.PSObject.Properties.Remove('reminderStartUtc')
    }
    $interactiveLegacyJson = $interactiveLegacySettings | ConvertTo-Json -Depth 20
    [System.IO.File]::WriteAllText($interactiveSettingsPath, $interactiveLegacyJson, $utf8NoBom)
    $interactiveLegacyHash = (Get-FileHash -LiteralPath $interactiveSettingsPath -Algorithm SHA256).Hash
    $interactiveLaunchToken = [guid]::NewGuid().ToString('N')
    $interactiveStartupRequest = [ordered]@{
        schemaVersion = 1
        launchToken = $interactiveLaunchToken
        requestedAtUtc = [DateTimeOffset]::UtcNow.ToString('o')
        acceptanceReadOnly = $true
        acceptanceState = 'active'
    } | ConvertTo-Json -Compress
    [System.IO.File]::WriteAllText(
        $interactiveStartupRequestPath,
        $interactiveStartupRequest,
        $utf8NoBom
    )

    $interactiveAdapterArguments = @(
        '-NoLogo',
        '-NoProfile',
        '-STA',
        '-File',
        ('"{0}"' -f $interactiveAdapterPath),
        '-ExitAfterReadyForTest',
        '-SettingsFile',
        ('"{0}"' -f $interactiveSettingsPath),
        '-DiagnosticLogPath',
        ('"{0}"' -f $interactiveDiagnosticPath)
    )
    $interactiveAdapterProcess = Start-Process -FilePath $pwsh -ArgumentList $interactiveAdapterArguments `
        -PassThru -WindowStyle Hidden `
        -RedirectStandardOutput $interactiveAdapterOutputPath `
        -RedirectStandardError $interactiveAdapterErrorPath
    $runtimeReceiptDeadline = [DateTimeOffset]::UtcNow.AddSeconds(15)
    while (
        -not (Test-Path -LiteralPath $interactiveRuntimeStatusPath -PathType Leaf) -and
        -not $interactiveAdapterProcess.HasExited -and
        [DateTimeOffset]::UtcNow -lt $runtimeReceiptDeadline
    ) {
        Start-Sleep -Milliseconds 50
    }
    if (-not (Test-Path -LiteralPath $interactiveRuntimeStatusPath -PathType Leaf)) {
        $interactiveAdapterError = if (Test-Path -LiteralPath $interactiveAdapterErrorPath -PathType Leaf) {
            Get-Content -LiteralPath $interactiveAdapterErrorPath -Raw
        }
        else {
            ''
        }
        throw "交互启动未生成 runtime-status.json：$interactiveAdapterError"
    }

    $runtimeReceipt = Get-Content -LiteralPath $interactiveRuntimeStatusPath -Raw -Encoding UTF8 |
        ConvertFrom-Json
    Assert-Equal 4 ([int] $runtimeReceipt.schemaVersion) '交互启动回执 schemaVersion'
    Assert-Equal 'window-ready' ([string] $runtimeReceipt.stage) '交互启动回执阶段'
    Assert-Equal $interactiveLaunchToken ([string] $runtimeReceipt.launchToken) '交互启动回执令牌'
    Assert-Equal 'China Standard Time' ([string] $runtimeReceipt.timeZoneId) '交互启动回执时区'
    Assert-Equal (
        [System.IO.Path]::GetFullPath($interactiveAdapterPath)
    ) (
        [System.IO.Path]::GetFullPath([string] $runtimeReceipt.applicationPath)
    ) '交互启动回执保持 adapter 路径身份'
    Assert-Equal (
        [System.Diagnostics.Process]::GetCurrentProcess().SessionId
    ) (
        [int] $runtimeReceipt.sessionId
    ) '交互启动回执属于当前交互会话'

    $interactiveApplicationProcess = [System.Diagnostics.Process]::GetProcessById(
        [int] $runtimeReceipt.processId
    )
    Assert-Equal (
        [System.IO.Path]::GetFullPath($interactiveApplicationPath)
    ) (
        [System.IO.Path]::GetFullPath($interactiveApplicationProcess.Path)
    ) '交互启动 PID 对应临时应用根内 C# EXE'
    Assert-Equal $false $interactiveAdapterProcess.HasExited 'window-ready 时 adapter 仍在等待 C# EXE'
    $acceptanceDiagnostic = Get-Content -LiteralPath $interactiveDiagnosticPath -Raw -Encoding UTF8
    Assert-Equal $true $acceptanceDiagnostic.Contains('AcceptanceReadOnlyEnabled', [System.StringComparison]::Ordinal) '延期验收启动时 C# 窗口进入只读状态'

    $duplicateApplicationArguments = @(
        '--settings-file',
        ('"{0}"' -f $interactiveSettingsPath),
        '--diagnostic-log-path',
        ('"{0}"' -f $interactiveDiagnosticPath)
    )
    $duplicateApplicationProcess = Start-Process -FilePath $interactiveApplicationPath -ArgumentList $duplicateApplicationArguments `
        -PassThru -WindowStyle Hidden `
        -RedirectStandardOutput $duplicateApplicationOutputPath `
        -RedirectStandardError $duplicateApplicationErrorPath
    if (-not $duplicateApplicationProcess.WaitForExit(5000)) {
        $duplicateError = if (Test-Path -LiteralPath $duplicateApplicationErrorPath -PathType Leaf) {
            Get-Content -LiteralPath $duplicateApplicationErrorPath -Raw
        }
        else { '' }
        $duplicateLog = if (Test-Path -LiteralPath $interactiveDiagnosticPath -PathType Leaf) {
            Get-Content -LiteralPath $interactiveDiagnosticPath -Raw -Encoding UTF8
        }
        else { '' }
        $interactiveApplicationProcess.Refresh()
        throw "真实第二个 C# EXE 未在单实例门禁期限内退出；首实例已退出=$($interactiveApplicationProcess.HasExited)；stderr=$duplicateError；diagnostic=$duplicateLog"
    }
    $duplicateApplicationProcess.WaitForExit()
    Assert-Equal 0 $duplicateApplicationProcess.ExitCode '真实第二个 C# EXE 被单实例门禁正常拒绝'
    $interactiveApplicationProcess.Refresh()
    Assert-Equal $false $interactiveApplicationProcess.HasExited '拒绝第二实例后首个 C# EXE 仍存活'
    $duplicateDiagnostic = Get-Content -LiteralPath $interactiveDiagnosticPath -Raw -Encoding UTF8
    Assert-Equal $true $duplicateDiagnostic.Contains('DuplicateInstanceRejected', [System.StringComparison]::Ordinal) '真实第二个 C# EXE 写入拒绝诊断'

    Assert-Equal $interactiveLegacyHash (Get-FileHash -LiteralPath $interactiveSettingsPath -Algorithm SHA256).Hash 'active 验收期间 settings 文件保持只读'
    Assert-Equal $true (Complete-CountdownStartupRequest -Path $interactiveStartupRequestPath -LaunchToken $interactiveLaunchToken) '同 token 原子授权 C# 持久化 schema 迁移'
    [void] (Wait-CountdownStartupRequestState -Path $interactiveStartupRequestPath -LaunchToken $interactiveLaunchToken -ExpectedState ready -TimeoutSeconds 5 -PollMilliseconds 50)
    $acceptanceReadyDiagnostic = Get-Content -LiteralPath $interactiveDiagnosticPath -Raw -Encoding UTF8
    Assert-Equal $true $acceptanceReadyDiagnostic.Contains('AcceptanceMigrationReady', [System.StringComparison]::Ordinal) 'C# 持久化迁移后确认 ready 且窗口继续只读'
    $interactiveMigratedJson = Get-Content -LiteralPath $interactiveSettingsPath -Raw -Encoding UTF8
    $interactiveMigratedHash = (Get-FileHash -LiteralPath $interactiveSettingsPath -Algorithm SHA256).Hash
    $interactiveMigratedSettings = $interactiveMigratedJson | ConvertFrom-Json
    Assert-Equal 8 ([int] $interactiveMigratedSettings.schemaVersion) 'released 阶段把 schema v6 写回 canonical v8'
    Assert-Equal $false ($interactiveLegacyHash -eq $interactiveMigratedHash) 'released 阶段实际写回迁移后的设置'
    Assert-Equal $true (
        Test-CountdownSettingsDeploymentTransition `
            -BeforeHash $interactiveLegacyHash `
            -AfterHash $interactiveMigratedHash `
            -BeforeJson $interactiveLegacyJson `
            -AfterJson $interactiveMigratedJson
    ) 'released 阶段迁移保持 schema v6 数据语义'
    Assert-Equal $true (Set-CountdownStartupRequestFinalized -Path $interactiveStartupRequestPath -LaunchToken $interactiveLaunchToken) '事务提交标记只把 ready 请求推进为 finalized'
    $acceptanceReleaseDeadline = [DateTimeOffset]::UtcNow.AddSeconds(5)
    do {
        $acceptanceReleaseDiagnostic = Get-Content -LiteralPath $interactiveDiagnosticPath -Raw -Encoding UTF8
        if ($acceptanceReleaseDiagnostic.Contains('AcceptanceReadOnlyReleased', [System.StringComparison]::Ordinal)) { break }
        Start-Sleep -Milliseconds 50
    } while ([DateTimeOffset]::UtcNow -lt $acceptanceReleaseDeadline)
    Assert-Equal $true $acceptanceReleaseDiagnostic.Contains('AcceptanceReadOnlyReleased', [System.StringComparison]::Ordinal) 'finalized 后 C# 窗口解除只读状态'
    $acceptanceStatus = Get-Content -LiteralPath $interactiveAcceptanceStatusPath -Raw -Encoding UTF8 | ConvertFrom-Json
    Assert-Equal 1 ([int] $acceptanceStatus.schemaVersion) '应用解锁确认回执使用 schema v1'
    Assert-Equal 'finalized-ready' ([string] $acceptanceStatus.stage) '应用真实观察 finalized 后写出确认回执'
    Assert-Equal $interactiveLaunchToken ([string] $acceptanceStatus.launchToken) '应用解锁确认回执绑定同一 token'
    Assert-Equal ([int] $runtimeReceipt.processId) ([int] $acceptanceStatus.processId) '应用解锁确认回执绑定同一 C# PID'

    if (-not $interactiveAdapterProcess.WaitForExit(45000)) {
        throw '交互启动未在测试退出期限内结束。'
    }
    $interactiveAdapterProcess.WaitForExit()
    Assert-Equal 0 $interactiveAdapterProcess.ExitCode '交互启动 adapter 透传 C# 正常退出码'
    $interactiveApplicationProcess.Refresh()
    Assert-Equal $true $interactiveApplicationProcess.HasExited '测试参数触发 C# EXE 退出'
    [pscustomobject]@{
        Passed = $script:assertions
        Failed = 0
        SchemaVersion = $canonicalSettings.schemaVersion
        TimeZone = $canonicalSettings.timeZoneId
        TempIsolation = $true
        RuntimeSchemaVersion = $runtimeReceipt.schemaVersion
        AdapterUiValidated = $true
    } | ConvertTo-Json
}
finally {
    if ($null -ne $duplicateApplicationProcess) {
        try {
            if (-not $duplicateApplicationProcess.HasExited) {
                $duplicateApplicationProcess.Kill()
                [void] $duplicateApplicationProcess.WaitForExit(5000)
            }
        }
        finally {
            $duplicateApplicationProcess.Dispose()
        }
    }
    if ($null -ne $interactiveApplicationProcess) {
        try {
            if (-not $interactiveApplicationProcess.HasExited) {
                $interactiveApplicationProcess.Kill()
                [void] $interactiveApplicationProcess.WaitForExit(5000)
            }
        }
        finally {
            $interactiveApplicationProcess.Dispose()
        }
    }
    if ($null -ne $interactiveAdapterProcess) {
        try {
            if (-not $interactiveAdapterProcess.HasExited) {
                $interactiveAdapterProcess.Kill($true)
                [void] $interactiveAdapterProcess.WaitForExit(5000)
            }
        }
        finally {
            $interactiveAdapterProcess.Dispose()
        }
    }
    if ($null -ne $mutexHolderProcess -and -not $mutexHolderProcess.HasExited) {
        Stop-Process -Id $mutexHolderProcess.Id -Force -ErrorAction SilentlyContinue
        [void] $mutexHolderProcess.WaitForExit(5000)
    }
    if (
        (Test-Path -LiteralPath $testRoot -PathType Container) -and
        $testRoot.StartsWith($systemTempPrefix, [System.StringComparison]::OrdinalIgnoreCase)
    ) {
        Remove-Item -LiteralPath $testRoot -Recurse -Force
    }
}
