# Countdown.Core.Tests.ps1 的 MainDomain 分段：入口按开关点源执行，共用入口的参数、断言函数与已建立的状态，不单独运行。

$firstTag = New-CountdownTagRecord -Id ' work ' -Name ' 工作 ' -Color ' #6B8E23 '
Assert-Equal 'work' $firstTag.id 'Tag record normalizes ID'
Assert-Equal '工作' $firstTag.name 'Tag record normalizes name'
Assert-Equal '#6B8E23' $firstTag.color 'Tag record preserves optional color'
$invalidTagColorRejected = $false
try {
    New-CountdownTagRecord -Id 'invalid-color' -Name '无效颜色' -Color 'red' | Out-Null
}
catch {
    $invalidTagColorRejected = $true
}
Assert-Equal $true $invalidTagColorRejected 'Invalid tag color is rejected'
$tagLibrary = [System.Collections.ArrayList]::new()
[void] $tagLibrary.Add($firstTag)
$tagLibraryCopy = Copy-CountdownTags -Tags $tagLibrary
$tagLibraryCopy[0].name = '已改名'
Assert-Equal '工作' $tagLibrary[0].name 'Tag library copy does not mutate source tag'
$normalizedTagIds = ConvertTo-CountdownTagIds -TagIds @(' work ', 'home') -KnownTagIds @('work', 'home')
Assert-Equal 2 $normalizedTagIds.Count 'Tag IDs retain unique values'
Assert-Equal 'work' $normalizedTagIds[0] 'Tag IDs preserve first-known canonical value'
Assert-Equal 'home' $normalizedTagIds[1] 'Tag IDs retain additional values'
$emptyTagIds = ConvertTo-CountdownTagIds -TagIds $null -KnownTagIds @('work')
Assert-Equal 0 $emptyTagIds.Count 'Empty tag IDs are valid for untagged records'
$duplicateTagIdRejected = $false
try {
    ConvertTo-CountdownTagIds -TagIds @('work', 'work') -KnownTagIds @('work') | Out-Null
}
catch {
    $duplicateTagIdRejected = $true
}
Assert-Equal $true $duplicateTagIdRejected 'Duplicate tag ID is rejected'
$danglingTagRejected = $false
try {
    ConvertTo-CountdownTagIds -TagIds @('missing') -KnownTagIds @('work') | Out-Null
}
catch {
    $danglingTagRejected = $true
}
Assert-Equal $true $danglingTagRejected 'Unknown tag ID is rejected'
Assert-Equal $true (Test-CountdownTagFilter -TagIds @('work') -SelectedTagIds @() -MatchMode Any) 'Empty tag selection includes tagged records'
Assert-Equal $true (Test-CountdownTagFilter -TagIds @('work') -SelectedTagIds @('work') -MatchMode Any) 'Any tag filter matches selected tag'
Assert-Equal $false (Test-CountdownTagFilter -TagIds @('home') -SelectedTagIds @('work') -MatchMode Any) 'Any tag filter excludes unselected tag'
Assert-Equal $true (Test-CountdownTagFilter -TagIds @('work', 'home') -SelectedTagIds @('home') -MatchMode Any) 'Any tag filter evaluates a non-first selected tag'
Assert-Equal $true (Test-CountdownTagFilter -TagIds @('work', 'home') -SelectedTagIds @('work', 'home') -MatchMode All) 'All tag filter matches every selected tag'
Assert-Equal $false (Test-CountdownTagFilter -TagIds @('work') -SelectedTagIds @('work', 'home') -MatchMode All) 'All tag filter requires every selected tag'
Assert-Equal $false (Test-CountdownTagFilter -TagIds @('work', 'home') -SelectedTagIds @('home', 'office') -MatchMode All) 'All tag filter excludes a missing selected tag'
Assert-Equal $true (Test-CountdownTagFilter -TagIds @() -SelectedTagIds @() -UntaggedOnly) 'Untagged filter includes untagged record'
Assert-Equal $false (Test-CountdownTagFilter -TagIds @('work') -SelectedTagIds @() -UntaggedOnly) 'Untagged filter excludes tagged record'

# #218 悬浮窗底部状态解析（纯函数 seam）：直接编译现役 src\MinminCountdown\FloatingBottomStatus.cs，
# 覆盖 月薪 >0 / =0 与 日历就绪 / 加载中 / 未就绪 的组合；日历未就绪必须返回说明影响范围与
# 恢复方式的新文案，而不是旧状态文本或鼓励文案。
$holidayUnavailableCopy = '节假日安排未就绪：距离下班和今日已赚暂不可用，联网后自动恢复'
$floatingBottomStatusSource = [System.IO.File]::ReadAllText(
    (Join-Path $PSScriptRoot 'src\MinminCountdown\FloatingBottomStatus.cs'),
    [System.Text.Encoding]::UTF8)
# Apply 的参数是应用内的 InteractiveStatusTextBlock 与 SilentTextBlock。这里只验证 Resolve，
# 编进仅承载 Apply 所需成员的同名类型，避免把整份 UiControls 拉进这个纯函数编译。
$floatingBottomStatusPeerStub = @"
internal sealed class InteractiveStatusTextBlock : System.Windows.Controls.TextBlock
{
    internal bool IsInvokable { get; set; }
}

internal sealed class SilentTextBlock : System.Windows.Controls.TextBlock
{
}

"@
$floatingBottomStatusCompileSource = [regex]::Replace(
    $floatingBottomStatusSource,
    'namespace MinminCountdown;\r?\n',
    { param($match) $match.Value + $floatingBottomStatusPeerStub },
    1)
$floatingBottomStatusDotnet = if ($isCIEnvironment) {
    (Get-Command dotnet -ErrorAction Stop).Path
} else {
    'D:\Codex\tools\dotnet\10.0\dotnet.exe'
}
$floatingBottomStatusDotnetRoot = Split-Path -Parent $floatingBottomStatusDotnet
$netCoreRefPackRoot = Join-Path $floatingBottomStatusDotnetRoot 'packs\Microsoft.NETCore.App.Ref'
$windowsDesktopRefPackRoot = Join-Path $floatingBottomStatusDotnetRoot 'packs\Microsoft.WindowsDesktop.App.Ref'
$netCoreRefPack = Join-Path $netCoreRefPackRoot (
    Get-ChildItem -LiteralPath $netCoreRefPackRoot -Directory |
        Sort-Object { [version] $_.Name } -Descending |
        Select-Object -First 1 -ExpandProperty Name)
$windowsDesktopRefPack = Join-Path $windowsDesktopRefPackRoot (
    Get-ChildItem -LiteralPath $windowsDesktopRefPackRoot -Directory |
        Sort-Object { [version] $_.Name } -Descending |
        Select-Object -First 1 -ExpandProperty Name)
$floatingBottomStatusRefs = @(
    Get-ChildItem -LiteralPath (Join-Path $netCoreRefPack 'ref\net10.0') -Filter '*.dll' -File |
        ForEach-Object { $_.FullName }
) + @(
    'PresentationFramework.dll', 'PresentationCore.dll', 'WindowsBase.dll', 'System.Xaml.dll' |
        ForEach-Object { Join-Path $windowsDesktopRefPack "ref\net10.0\$_" }
)
$compiledBottomStatusTypes = Add-Type -TypeDefinition ("using System;`n" + $floatingBottomStatusCompileSource) `
    -ReferencedAssemblies $floatingBottomStatusRefs -IgnoreWarnings -PassThru
$floatingBottomStatusType = @($compiledBottomStatusTypes |
    Where-Object { $_.FullName -eq 'MinminCountdown.FloatingBottomStatus' })[0]
Assert-Equal $true ($null -ne $floatingBottomStatusType) 'Floating bottom status resolver compiles from current source'
$encouragementSentinel = 'ENCOURAGEMENT-SENTINEL'
$bottomEarningsReady = $floatingBottomStatusType::Resolve([decimal]41.13, [decimal]6200, 'Ready', 'STATUS-SENTINEL', $encouragementSentinel)
Assert-Equal 'Earnings' ([string] $bottomEarningsReady.Kind) 'Bottom status shows earnings for positive salary and ready calendar'
Assert-Equal '今日已赚 ￥41.13' ([string] $bottomEarningsReady.Text) 'Bottom status formats earnings amount'
$bottomEarningsHidden = $floatingBottomStatusType::Resolve([decimal]41.13, [decimal]6200, 'Ready', 'STATUS-SENTINEL', $encouragementSentinel, $true)
Assert-Equal 'Earnings' ([string] $bottomEarningsHidden.Kind) 'Hidden earnings retain their earnings presentation kind'
Assert-Equal '今日已赚 ¥••••' ([string] $bottomEarningsHidden.Text) 'Hidden earnings presentation contains no amount'
$bottomLoadingPositiveSalary = $floatingBottomStatusType::Resolve($null, [decimal]6200, 'Pending', '节假日日历正在加载', $encouragementSentinel)
Assert-Equal 'Status' ([string] $bottomLoadingPositiveSalary.Kind) 'Bottom status keeps calendar status while loading with positive salary'
Assert-Equal '节假日日历正在加载' ([string] $bottomLoadingPositiveSalary.Text) 'Bottom status passes through the loading text with positive salary'
$bottomUnavailablePositiveSalary = $floatingBottomStatusType::Resolve($null, [decimal]6200, 'Unavailable', '节假日日历未就绪', $encouragementSentinel)
Assert-Equal 'Status' ([string] $bottomUnavailablePositiveSalary.Kind) 'Bottom status stays a calendar status when unavailable with positive salary'
Assert-Equal $holidayUnavailableCopy ([string] $bottomUnavailablePositiveSalary.Text) 'Bottom status explains the unavailable calendar with positive salary'
$bottomEncouragementReady = $floatingBottomStatusType::Resolve($null, [decimal]0, 'Ready', 'STATUS-SENTINEL', $encouragementSentinel)
Assert-Equal 'Encouragement' ([string] $bottomEncouragementReady.Kind) 'Bottom status shows encouragement for zero salary and ready calendar'
Assert-Equal $encouragementSentinel ([string] $bottomEncouragementReady.Text) 'Bottom status uses the workspace encouragement text for zero salary'
$bottomLoadingZeroSalary = $floatingBottomStatusType::Resolve($null, [decimal]0, 'Pending', '节假日日历正在加载', $encouragementSentinel)
Assert-Equal 'Status' ([string] $bottomLoadingZeroSalary.Kind) 'Bottom status keeps calendar status while loading with zero salary'
Assert-Equal '节假日日历正在加载' ([string] $bottomLoadingZeroSalary.Text) 'Bottom status passes through the loading text with zero salary'
$bottomUnavailableZeroSalary = $floatingBottomStatusType::Resolve($null, [decimal]0, 'Unavailable', '节假日日历未就绪', $encouragementSentinel)
Assert-Equal 'Status' ([string] $bottomUnavailableZeroSalary.Kind) 'Bottom status does not show encouragement when the calendar is unavailable'
Assert-Equal $holidayUnavailableCopy ([string] $bottomUnavailableZeroSalary.Text) 'Bottom status explains the unavailable calendar with zero salary'

# #218 节假日年度覆盖守卫（纯函数）：注入北京时间与随包覆盖年份集合；北京时间进入 12 月且
# 缺少下一年随包数据时守卫必须失败，失败信息指明缺失年份与补数据流程。
$holidayGuardNovember = Test-CountdownHolidayYearCoverage -BeijingNow ([DateTimeOffset]'2026-11-30T23:59:00+08:00') -PackagedYears @(2026)
Assert-Equal $true ([bool] $holidayGuardNovember.Covered) 'Holiday year guard passes before December'
$holidayGuardDecemberReady = Test-CountdownHolidayYearCoverage -BeijingNow ([DateTimeOffset]'2026-12-01T00:00:00+08:00') -PackagedYears @(2026, 2027)
Assert-Equal $true ([bool] $holidayGuardDecemberReady.Covered) 'Holiday year guard passes when the next year is packaged'
$holidayGuardDecemberMissing = Test-CountdownHolidayYearCoverage -BeijingNow ([DateTimeOffset]'2026-12-01T00:00:00+08:00') -PackagedYears @(2026)
Assert-Equal $false ([bool] $holidayGuardDecemberMissing.Covered) 'Holiday year guard fails in December without the next year'
Assert-Equal 2027 ([int] $holidayGuardDecemberMissing.MissingYear) 'Holiday year guard names the missing year'
Assert-Equal $true ([string] $holidayGuardDecemberMissing.Message.Contains('2027')) 'Holiday year guard message names the missing year'
Assert-Equal $true ([string] $holidayGuardDecemberMissing.Message.Contains('holiday-data/README.md')) 'Holiday year guard message points to the data refresh runbook'
$holidayGuardLateDecember = Test-CountdownHolidayYearCoverage -BeijingNow ([DateTimeOffset]'2026-12-31T23:00:00+08:00') -PackagedYears @(2026)
Assert-Equal $false ([bool] $holidayGuardLateDecember.Covered) 'Holiday year guard still fails at the end of December without the next year'
Assert-Equal 2027 ([int] $holidayGuardLateDecember.MissingYear) 'Holiday year guard names the missing year at the end of December'

$runtimeTestRoot = Join-Path $PSScriptRoot ("runtime-test-" + [guid]::NewGuid().ToString('N'))
$runtimeStatusTestPath = Join-Path $runtimeTestRoot 'runtime-status.json'
$acceptanceStatusTestPath = Join-Path $runtimeTestRoot 'acceptance-status.json'
$startupRequestTestPath = Join-Path $runtimeTestRoot 'startup-request.json'
$runtimeApplicationPath = Join-Path $runtimeTestRoot '敏敏子倒计时.ps1'
$runtimeExecutablePath = Join-Path $runtimeTestRoot 'app\MinminCountdown.exe'
try {
    New-Item -ItemType Directory -Path $runtimeTestRoot -Force | Out-Null
    [System.IO.File]::WriteAllText($runtimeApplicationPath, '# test application', (New-Object System.Text.UTF8Encoding($false)))
    $startupRequest = New-CountdownStartupRequest -Path $startupRequestTestPath -AcceptanceReadOnly
    Assert-Equal 1 $startupRequest.schemaVersion 'Startup request schema'
    Assert-Equal $true (-not [string]::IsNullOrWhiteSpace([string] $startupRequest.launchToken)) 'Startup request has token'
    Assert-Equal $true ([bool] $startupRequest.acceptanceReadOnly) 'Startup request can hold the candidate application in acceptance read-only mode'
    Assert-Equal 'active' ([string] $startupRequest.acceptanceState) 'Startup request begins in active acceptance state'
    $parsedRequestedAtUtc = [DateTimeOffset]::MinValue
    Assert-Equal $true ([DateTimeOffset]::TryParseExact(
            [string] $startupRequest.requestedAtUtc,
            'o',
            [System.Globalization.CultureInfo]::InvariantCulture,
            [System.Globalization.DateTimeStyles]::RoundtripKind,
            [ref] $parsedRequestedAtUtc)) 'Startup request uses the exact round-trip timestamp format'
    Assert-Equal $false (Complete-CountdownStartupRequest -Path $startupRequestTestPath -LaunchToken 'wrong-token') 'Startup request rejects release with a different token'
    Assert-Equal $true (Complete-CountdownStartupRequest -Path $startupRequestTestPath -LaunchToken $startupRequest.launchToken) 'Startup request releases acceptance mode with the same token'
    $releasedStartupRequest = Get-Content -LiteralPath $startupRequestTestPath -Raw -Encoding UTF8 | ConvertFrom-Json -DateKind String
    Assert-Equal 'released' ([string] $releasedStartupRequest.acceptanceState) 'Startup request persists explicit released acceptance state'
    Assert-Equal $true ([bool] $releasedStartupRequest.acceptanceReadOnly) 'Released startup request keeps the UI read-only until C# confirms migration'
    Assert-Equal 'released' ([string] (Wait-CountdownStartupRequestState -Path $startupRequestTestPath -LaunchToken $startupRequest.launchToken -ExpectedState released -TimeoutSeconds 0).acceptanceState) 'Startup request state wait validates token and state'
    $releasedStartupRequest.acceptanceState = 'ready'
    [System.IO.File]::WriteAllText($startupRequestTestPath, ($releasedStartupRequest | ConvertTo-Json -Depth 8), [System.Text.UTF8Encoding]::new($false))
    Assert-Equal $true (Set-CountdownStartupRequestFinalized -Path $startupRequestTestPath -LaunchToken $startupRequest.launchToken) 'Startup request finalizes only after C# reports ready'
    $finalizedStartupRequest = Get-Content -LiteralPath $startupRequestTestPath -Raw -Encoding UTF8 | ConvertFrom-Json
    Assert-Equal 'finalized' ([string] $finalizedStartupRequest.acceptanceState) 'Startup request persists explicit finalized state'
    Assert-Equal $false ([bool] $finalizedStartupRequest.acceptanceReadOnly) 'Finalized startup request permits UI editing'
    [System.IO.File]::WriteAllText($startupRequestTestPath, '{broken-json', [System.Text.UTF8Encoding]::new($false))
    $corruptReleaseRejected = $false
    try { Complete-CountdownStartupRequest -Path $startupRequestTestPath -LaunchToken $startupRequest.launchToken | Out-Null }
    catch { $corruptReleaseRejected = $true }
    Assert-Equal $true $corruptReleaseRejected 'Startup request release preserves JSON and IO failures as exceptions'
    [System.IO.File]::WriteAllText($startupRequestTestPath, '{"schemaVersion":999,"launchToken":"unknown","acceptanceState":"active"}', [System.Text.UTF8Encoding]::new($false))
    $unknownSchemaRejected = $false
    try { Complete-CountdownStartupRequest -Path $startupRequestTestPath -LaunchToken 'unknown' | Out-Null }
    catch { $unknownSchemaRejected = $_.Exception.Message -match 'schemaVersion' }
    Assert-Equal $true $unknownSchemaRejected 'Startup request rejects unknown schema versions'
    $nonRoundTripStartupRequest = [ordered]@{
        schemaVersion = 1
        launchToken = 'non-round-trip-token'
        requestedAtUtc = '2026-08-13T12:34:56Z'
        acceptanceReadOnly = $true
        acceptanceState = 'active'
    }
    [System.IO.File]::WriteAllText($startupRequestTestPath, ($nonRoundTripStartupRequest | ConvertTo-Json -Compress), [System.Text.UTF8Encoding]::new($false))
    $nonRoundTripTimestampRejected = $false
    try { Complete-CountdownStartupRequest -Path $startupRequestTestPath -LaunchToken $nonRoundTripStartupRequest.launchToken | Out-Null }
    catch { $nonRoundTripTimestampRejected = $_.Exception.Message -match 'requestedAtUtc' }
    Assert-Equal $true $nonRoundTripTimestampRejected 'Startup request rejects valid ISO timestamps outside the exact round-trip format'
    $startupRequest = New-CountdownStartupRequest -Path $startupRequestTestPath -AcceptanceReadOnly
    Assert-Equal $false (Test-Path -LiteralPath "$startupRequestTestPath.tmp") 'Startup request leaves no fixed temporary file'
    $receipt = Write-CountdownWindowReadyReceipt -Path $runtimeStatusTestPath -LaunchToken $startupRequest.launchToken -ProcessId 4242 -SessionId 7 -ApplicationPath $runtimeApplicationPath -CalendarState Pending
    $processProbe = {
        param([int] $ProcessId)
        if ($ProcessId -eq 4242) {
            [pscustomobject]@{ ProcessId = 4242; SessionId = 7; CommandLine = "pwsh.exe -STA -File `"$runtimeApplicationPath`"" }
        }
    }
    $defaultAdapterEvaluation = Test-CountdownWindowReadyReceipt -Receipt $receipt -ExpectedLaunchToken $startupRequest.launchToken -ExpectedSessionId 7 -ExpectedApplicationPath $runtimeApplicationPath -ProcessProbe $processProbe
    Assert-Equal $false $defaultAdapterEvaluation.Ready 'Window-ready receipt rejects the legacy PowerShell adapter by default'
    $legacyAdapterEvaluation = Test-CountdownWindowReadyReceipt -Receipt $receipt -ExpectedLaunchToken $startupRequest.launchToken -ExpectedSessionId 7 -ExpectedApplicationPath $runtimeApplicationPath -ProcessProbe $processProbe -AllowLegacyPowerShellAdapter
    Assert-Equal $true $legacyAdapterEvaluation.Ready 'Window-ready receipt accepts the legacy PowerShell adapter only in explicit compatibility mode'
    Assert-Equal 'Pending' $legacyAdapterEvaluation.Receipt.calendarState 'Window-ready does not wait for calendar'
    $managedExecutableProbe = {
        param([int] $ProcessId)
        if ($ProcessId -eq 4242) {
            [pscustomobject]@{ ProcessId = 4242; SessionId = 7; ExecutablePath = $runtimeExecutablePath; CommandLine = "`"$runtimeExecutablePath`"" }
        }
    }
    Assert-Equal $true (Test-CountdownWindowReadyReceipt -Receipt $receipt -ExpectedLaunchToken $startupRequest.launchToken -ExpectedSessionId 7 -ExpectedApplicationPath $runtimeApplicationPath -ProcessProbe $managedExecutableProbe).Ready 'Window-ready receipt accepts the managed C# application host'
    $acceptanceReceipt = [ordered]@{
        schemaVersion = 1
        stage = 'finalized-ready'
        launchToken = $startupRequest.launchToken
        processId = 4242
        sessionId = 7
        applicationPath = $runtimeApplicationPath
        readyAtUtc = [DateTimeOffset]::UtcNow.ToString('o')
    }
    [System.IO.File]::WriteAllText($acceptanceStatusTestPath, ($acceptanceReceipt | ConvertTo-Json -Compress), [System.Text.UTF8Encoding]::new($false))
    Assert-Equal 'finalized-ready' ([string] (Wait-CountdownAcceptanceReadyReceipt -Path $acceptanceStatusTestPath -LaunchToken $startupRequest.launchToken -ExpectedProcessId 4242 -ExpectedSessionId 7 -ExpectedApplicationPath $runtimeApplicationPath -TimeoutSeconds 0 -ProcessProbe $managedExecutableProbe).stage) 'Acceptance-ready receipt validates token, C# PID, session and adapter path'
    $unmanagedExecutableProbe = {
        param([int] $ProcessId)
        if ($ProcessId -eq 4242) {
            [pscustomobject]@{ ProcessId = 4242; SessionId = 7; ExecutablePath = (Join-Path $runtimeTestRoot 'other\MinminCountdown.exe'); CommandLine = '"MinminCountdown.exe"' }
        }
    }
    Assert-Equal $false (Test-CountdownWindowReadyReceipt -Receipt $receipt -ExpectedLaunchToken $startupRequest.launchToken -ExpectedSessionId 7 -ExpectedApplicationPath $runtimeApplicationPath -ProcessProbe $unmanagedExecutableProbe).Ready 'Window-ready receipt rejects an unmanaged executable with the expected process name'
    Assert-Equal $false (Test-CountdownWindowReadyReceipt -Receipt $receipt -ExpectedLaunchToken 'old-token' -ExpectedSessionId 7 -ExpectedApplicationPath $runtimeApplicationPath -ProcessProbe $processProbe).Ready 'Old startup token is rejected'
    Assert-Equal $false (Test-CountdownWindowReadyReceipt -Receipt $receipt -ExpectedLaunchToken $startupRequest.launchToken -ExpectedSessionId 8 -ExpectedApplicationPath $runtimeApplicationPath -ProcessProbe $processProbe).Ready 'Wrong interactive session is rejected'
    Assert-Equal $false (Test-CountdownWindowReadyReceipt -Receipt $receipt -ExpectedLaunchToken $startupRequest.launchToken -ExpectedSessionId 7 -ExpectedApplicationPath (Join-Path $runtimeTestRoot 'other.ps1') -ProcessProbe $processProbe).Ready 'Wrong application path is rejected'
    $embeddedPathProbe = {
        param([int] $ProcessId)
        if ($ProcessId -eq 4242) {
            [pscustomobject]@{ ProcessId = 4242; SessionId = 7; CommandLine = "pwsh.exe -File `"$(Join-Path $runtimeTestRoot 'helper.ps1')`" -ArgumentList `"$runtimeApplicationPath`"" }
        }
    }
    Assert-Equal $false (Test-CountdownWindowReadyReceipt -Receipt $receipt -ExpectedLaunchToken $startupRequest.launchToken -ExpectedSessionId 7 -ExpectedApplicationPath $runtimeApplicationPath -ProcessProbe $embeddedPathProbe).Ready 'Command-line path substring outside -File is rejected'
    $commandModeProbe = {
        param([int] $ProcessId)
        if ($ProcessId -eq 4242) {
            [pscustomobject]@{ ProcessId = 4242; SessionId = 7; CommandLine = "pwsh.exe -NoProfile -Command `"Write-Output ' -File $runtimeApplicationPath '`"" }
        }
    }
    Assert-Equal $false (Test-CountdownWindowReadyReceipt -Receipt $receipt -ExpectedLaunchToken $startupRequest.launchToken -ExpectedSessionId 7 -ExpectedApplicationPath $runtimeApplicationPath -ProcessProbe $commandModeProbe).Ready 'Command-mode text containing -File is not accepted as the application process'
    Assert-Equal $false (Test-CountdownWindowReadyReceipt -Receipt $receipt -ExpectedLaunchToken $startupRequest.launchToken -ExpectedSessionId 7 -ExpectedApplicationPath $runtimeApplicationPath -ProcessProbe { param([int] $ProcessId) $null }).Ready 'Missing process is rejected'
    Assert-Equal $false (Test-CountdownWindowReadyReceipt -Receipt $receipt -ExpectedLaunchToken $startupRequest.launchToken -ExpectedSessionId 7 -ExpectedApplicationPath $runtimeApplicationPath -ProcessProbe { param([int] $ProcessId) [pscustomobject]@{ ProcessId = 4243; SessionId = 7; CommandLine = "pwsh.exe -File `"$runtimeApplicationPath`"" } }).Ready 'Mismatched process identity is rejected'
    [System.IO.File]::WriteAllText($runtimeStatusTestPath, '{not-valid-json', (New-Object System.Text.UTF8Encoding($false)))
    Assert-Equal $true ($null -eq (Read-CountdownRuntimeReceipt -Path $runtimeStatusTestPath)) 'Malformed receipt is not accepted'
    $timeoutObserved = $false
    try {
        Wait-CountdownWindowReady -Path $runtimeStatusTestPath -LaunchToken $startupRequest.launchToken -ExpectedSessionId 7 -ExpectedApplicationPath $runtimeApplicationPath -TimeoutSeconds 0 -PollMilliseconds 1 -ProcessProbe { param([int] $ProcessId) $null } | Out-Null
    }
    catch { $timeoutObserved = $_.Exception.Message -match '启动就绪回执超时' }
    Assert-Equal $true $timeoutObserved 'Window-ready wait has a bounded timeout'
}
finally {
    Remove-Item -LiteralPath $runtimeTestRoot -Recurse -Force -ErrorAction SilentlyContinue
}

$settingsMutexProbeEngine = (Get-Command pwsh.exe -CommandType Application -ErrorAction Stop | Select-Object -First 1 -ExpandProperty Source)
$settingsMutexEnterCommand = Get-Command Enter-CountdownSettingsWriteMutex -Module Countdown.Settings -ErrorAction SilentlyContinue
$settingsMutexExitCommand = Get-Command Exit-CountdownSettingsWriteMutex -Module Countdown.Settings -ErrorAction SilentlyContinue
Assert-Equal $true ($null -ne $settingsMutexEnterCommand -and $null -ne $settingsMutexExitCommand) 'Settings module exposes a cross-process write mutex'
$settingsModuleSource = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'Countdown.Settings.psm1') -Raw
Assert-Equal $false ($settingsModuleSource.Contains('[System.Security.Cryptography.SHA256]::HashData') -or $settingsModuleSource.Contains('[Convert]::ToHexString')) 'Settings mutex hashing remains compatible with PowerShell 7.0 and .NET Core 3.1'
if ($null -ne $settingsMutexEnterCommand -and $null -ne $settingsMutexExitCommand) {
    $settingsMutexProbePath = Join-Path $PSScriptRoot ("settings-mutex-probe-" + [guid]::NewGuid().ToString('N') + '.json')
    $settingsMutex = Enter-CountdownSettingsWriteMutex -Path $settingsMutexProbePath -TimeoutSeconds 1
    try {
        $settingsMutexModulePath = (Join-Path $PSScriptRoot 'Countdown.Settings.psm1').Replace("'", "''")
        $escapedSettingsMutexProbePath = $settingsMutexProbePath.Replace("'", "''")
        $settingsMutexProbeCommand = "Import-Module -Name '$settingsMutexModulePath' -Force; try { `$childMutex = Enter-CountdownSettingsWriteMutex -Path '$escapedSettingsMutexProbePath' -TimeoutSeconds 1; Exit-CountdownSettingsWriteMutex -Mutex `$childMutex; 'unexpected acquisition' } catch { `$_.Exception.Message }"
        $settingsMutexProbeOutput = & $settingsMutexProbeEngine -NoLogo -NoProfile -NonInteractive -Command $settingsMutexProbeCommand | Out-String
        Assert-Equal $true ($settingsMutexProbeOutput -match '超时') 'Settings write mutex excludes a second PowerShell process'
    }
    finally {
        Exit-CountdownSettingsWriteMutex -Mutex $settingsMutex
    }
}
