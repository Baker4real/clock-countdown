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

$target = ConvertTo-CountdownTargetUtc -DateText '2026-07-15' -TimeText '18:00:00'
Assert-Equal '2026-07-15T10:00:00.0000000+00:00' $target.ToString('o') 'Beijing to UTC'

$parts = Get-CountdownParts -TargetUtc ([DateTimeOffset]'2026-07-17T11:02:03Z') -NowUtc ([DateTimeOffset]'2026-07-15T10:00:00Z')
Assert-Equal $false $parts.IsElapsed 'Future target state'
Assert-Equal 2 $parts.Days 'Days'
Assert-Equal 1 $parts.Hours 'Hours'
Assert-Equal 2 $parts.Minutes 'Minutes'
Assert-Equal 3 $parts.Seconds 'Seconds'
Assert-Equal '2 天' (Format-CountdownParts -Parts $parts -Precision Day) 'Day precision format'
Assert-Equal '2 天 01 小时' (Format-CountdownParts -Parts $parts -Precision Hour) 'Hour precision format'
Assert-Equal '2 天 01 小时 02 分钟' (Format-CountdownParts -Parts $parts -Precision Minute) 'Minute precision format'
Assert-Equal '2 天 01 小时 02 分钟 03 秒' (Format-CountdownParts -Parts $parts -Precision Second) 'Second precision format'

$elapsed = Get-CountdownParts -TargetUtc ([DateTimeOffset]'2026-07-15T10:00:00Z') -NowUtc ([DateTimeOffset]'2026-07-15T10:00:01Z')
Assert-Equal $true $elapsed.IsElapsed 'Elapsed state'
Assert-Equal '已到期' (Format-CountdownParts -Parts $elapsed -Precision Second) 'Elapsed format'

$workdayMidpoint = Get-WorkdayEarnings -MonthlySalary ([decimal]23000) -WorkStart ([TimeSpan]'09:00') -WorkEnd ([TimeSpan]'18:00') -BeijingNow ([DateTimeOffset]'2026-07-15T13:30:00+08:00')
Assert-Equal ([decimal]500) $workdayMidpoint 'Midpoint workday earnings'
Assert-Equal ([decimal]0) (Get-WorkdayEarnings -MonthlySalary ([decimal]23000) -WorkStart ([TimeSpan]'09:00') -WorkEnd ([TimeSpan]'18:00') -BeijingNow ([DateTimeOffset]'2026-07-15T08:59:59+08:00')) 'Before work earnings'
Assert-Equal ([decimal]0) (Get-WorkdayEarnings -MonthlySalary ([decimal]23000) -WorkStart ([TimeSpan]'09:00') -WorkEnd ([TimeSpan]'18:00') -BeijingNow ([DateTimeOffset]'2026-07-18T13:30:00+08:00')) 'Weekend earnings'
$statutoryCalendar = @{ '2026-01-01' = $false; '2026-01-04' = $true }
Assert-Equal ([decimal]0) (Get-WorkdayEarnings -MonthlySalary ([decimal]22000) -WorkStart ([TimeSpan]'09:00') -WorkEnd ([TimeSpan]'18:00') -BeijingNow ([DateTimeOffset]'2026-01-01T13:30:00+08:00') -WorkdayMap $statutoryCalendar) 'Statutory paid leave earnings'
Assert-Equal ([decimal]500) (Get-WorkdayEarnings -MonthlySalary ([decimal]22000) -WorkStart ([TimeSpan]'09:00') -WorkEnd ([TimeSpan]'18:00') -BeijingNow ([DateTimeOffset]'2026-01-04T13:30:00+08:00') -WorkdayMap $statutoryCalendar) 'Compensatory workday earnings'
$invalidWorkHoursRejected = $false
try { Get-WorkdayEarnings -MonthlySalary ([decimal]1) -WorkStart ([TimeSpan]'18:00') -WorkEnd ([TimeSpan]'09:00') -BeijingNow ([DateTimeOffset]'2026-07-15T13:30:00+08:00') | Out-Null }
catch { $invalidWorkHoursRejected = $true }
Assert-Equal $true $invalidWorkHoursRejected 'Invalid work hours rejected'

$invalidRejected = $false
try { ConvertTo-CountdownTargetUtc -DateText '2026-02-30' -TimeText '18:00:00' | Out-Null }
catch { $invalidRejected = $true }
Assert-Equal $true $invalidRejected 'Invalid date rejected'

# #218 悬浮窗底部状态解析（纯函数 seam）：直接编译现役 src\MinminCountdown\FloatingBottomStatus.cs，
# 覆盖 月薪 >0 / =0 与 日历就绪 / 加载中 / 未就绪 的组合；日历未就绪必须返回说明影响范围与
# 恢复方式的新文案，而不是旧状态文本或鼓励文案。
$holidayUnavailableCopy = '节假日安排未就绪：距离下班和今日已赚暂不可用，联网后自动恢复'
$floatingBottomStatusSource = [System.IO.File]::ReadAllText(
    (Join-Path $PSScriptRoot 'src\MinminCountdown\FloatingBottomStatus.cs'),
    [System.Text.Encoding]::UTF8)
# Apply 的参数是应用内的 AutomationOptionalTextBlock。这里只验证 Resolve，
# 编进仅承载 Apply 所需成员的同名类型，避免把整份 UiControls 拉进这个纯函数编译。
$floatingBottomStatusPeerStub = @"
internal sealed class AutomationOptionalTextBlock : System.Windows.Controls.TextBlock
{
    internal bool IsInvokable { get; set; }

    internal void SetExcludedFromAutomationTree(bool excluded)
    {
    }
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
$diagnosticLogTestPath = Join-Path $runtimeTestRoot 'logs\application.jsonl'
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
    $diagnosticStarted = Write-CountdownDiagnosticEvent -Path $diagnosticLogTestPath -Level Info -Event 'ProcessStarted'
    Assert-Equal $true $diagnosticStarted 'Diagnostic logging reports a successful append'
    $diagnosticStartEntry = Get-Content -LiteralPath $diagnosticLogTestPath -Encoding UTF8 | Select-Object -First 1 | ConvertFrom-Json
    Assert-Equal 1 $diagnosticStartEntry.schemaVersion 'Diagnostic log schema'
    Assert-Equal 'ProcessStarted' $diagnosticStartEntry.event 'Diagnostic log event name'
    Assert-Equal 'Info' $diagnosticStartEntry.level 'Diagnostic log level'
    Assert-Equal $PID $diagnosticStartEntry.processId 'Diagnostic log process identity'
    $diagnosticException = [System.InvalidOperationException]::new('Deferred theme template failed')
    $diagnosticFailed = Write-CountdownDiagnosticEvent -Path $diagnosticLogTestPath -Level Error -Event 'FatalUnhandledException' -Exception $diagnosticException -ScriptStackTrace 'Start-CountdownApplication line 1'
    Assert-Equal $true $diagnosticFailed 'Diagnostic logging records fatal exceptions'
    $diagnosticErrorEntry = Get-Content -LiteralPath $diagnosticLogTestPath -Encoding UTF8 | Select-Object -Last 1 | ConvertFrom-Json
    Assert-Equal 'System.InvalidOperationException' $diagnosticErrorEntry.exceptionType 'Diagnostic log exception type'
    Assert-Equal 'Deferred theme template failed' $diagnosticErrorEntry.exceptionMessage 'Diagnostic log exception message'
    Assert-Equal 'Start-CountdownApplication line 1' $diagnosticErrorEntry.scriptStackTrace 'Diagnostic log PowerShell stack'
    foreach ($index in 1..3) {
        Write-CountdownDiagnosticEvent -Path $diagnosticLogTestPath -Level Info -Event ("RotationProbe$index-" + ('x' * 180)) -MaxBytes 300 -MaxArchives 2 | Out-Null
    }
    Assert-Equal $true (Test-Path -LiteralPath "$diagnosticLogTestPath.1" -PathType Leaf) 'Diagnostic log rotates at the size bound'
    Assert-Equal $true ((Get-Item -LiteralPath $diagnosticLogTestPath).Length -lt 1024) 'Diagnostic current log remains bounded after rotation'

    $utf8NoBomDiagnostic = [System.Text.UTF8Encoding]::new($false)
    $runtimeModulePath = Join-Path $PSScriptRoot 'Countdown.Runtime.psm1'
    $runtimeModuleForDiagnostic = Get-Module -Name 'Countdown.Runtime'
    $diagnosticMutexGuardPath = Join-Path $PSScriptRoot 'diagnostic-mutex-name-guard.jsonl'
    $expectedDiagnosticMutexName = Get-IndependentCountdownDiagnosticMutexName -Path $diagnosticMutexGuardPath
    $actualDiagnosticMutexName = ''
    try {
        $actualDiagnosticMutexName = [string] (& $runtimeModuleForDiagnostic {
                param($Path)
                Get-CountdownDiagnosticMutexName -Path $Path
            } $diagnosticMutexGuardPath)
    }
    catch {
        $actualDiagnosticMutexName = ''
    }
    Assert-Equal $expectedDiagnosticMutexName $actualDiagnosticMutexName 'Diagnostic mutex name follows the C# path-hash rule'
    $mixedCaseDiagnosticMutexName = ''
    $aliasDiagnosticMutexName = ''
    try {
        $mixedCaseDiagnosticMutexName = [string] (& $runtimeModuleForDiagnostic {
                param($Path)
                Get-CountdownDiagnosticMutexName -Path $Path
            } $diagnosticMutexGuardPath.ToLowerInvariant())
        $aliasDiagnosticMutexName = [string] (& $runtimeModuleForDiagnostic {
                param($Path)
                Get-CountdownDiagnosticMutexName -Path $Path
            } (Join-Path $PSScriptRoot 'mutex-alias\..\diagnostic-mutex-name-guard.jsonl'))
    }
    catch {
        $mixedCaseDiagnosticMutexName = ''
        $aliasDiagnosticMutexName = ''
    }
    Assert-Equal $expectedDiagnosticMutexName $mixedCaseDiagnosticMutexName 'Diagnostic mutex name normalizes path case'
    Assert-Equal $expectedDiagnosticMutexName $aliasDiagnosticMutexName 'Diagnostic mutex name uses the resolved full path'

    $diagnosticTimeoutLogPath = Join-Path $runtimeTestRoot 'logs\timeout.jsonl'
    $expectedTimeoutMutexName = Get-IndependentCountdownDiagnosticMutexName -Path $diagnosticTimeoutLogPath
    $timeoutProbePath = Join-Path $runtimeTestRoot 'diagnostic-timeout-probe.ps1'
    $timeoutStdoutPath = Join-Path $runtimeTestRoot 'diagnostic-timeout.stdout.txt'
    $timeoutStderrPath = Join-Path $runtimeTestRoot 'diagnostic-timeout.stderr.txt'
    $timeoutProbe = @'
param(
    [Parameter(Mandatory)] [string] $RuntimeModulePath,
    [Parameter(Mandatory)] [string] $LogPath
)
$ErrorActionPreference = 'Stop'
Import-Module -Name $RuntimeModulePath -Force
$threw = $false
$result = $null
$started = [DateTimeOffset]::UtcNow
try {
    $result = Write-CountdownDiagnosticEvent -Path $LogPath -Level Info -Event 'DiagnosticMutexTimeoutDrop' -TimeoutSeconds 1
}
catch {
    $threw = $true
}
[pscustomobject]@{
    Threw = $threw
    Result = $result
    ElapsedMilliseconds = [int] ([DateTimeOffset]::UtcNow - $started).TotalMilliseconds
} | ConvertTo-Json -Compress
'@
    [System.IO.File]::WriteAllText($timeoutProbePath, $timeoutProbe, $utf8NoBomDiagnostic)
    $heldDiagnosticMutex = [System.Threading.Mutex]::new($false, $expectedTimeoutMutexName)
    $heldDiagnosticMutexAcquired = $false
    $timeoutProbeProcess = $null
    try {
        $heldDiagnosticMutexAcquired = $heldDiagnosticMutex.WaitOne([TimeSpan]::FromSeconds(5))
        Assert-Equal $true $heldDiagnosticMutexAcquired 'Diagnostic timeout fixture acquired the independently derived mutex'
        $diagnosticPwsh = (Get-Command pwsh.exe -CommandType Application -ErrorAction Stop | Select-Object -First 1 -ExpandProperty Source)
        $timeoutProbeProcess = Start-Process -FilePath $diagnosticPwsh -ArgumentList @(
            '-NoLogo',
            '-NoProfile',
            '-File',
            $timeoutProbePath,
            '-RuntimeModulePath',
            $runtimeModulePath,
            '-LogPath',
            $diagnosticTimeoutLogPath
        ) -WorkingDirectory $runtimeTestRoot -PassThru -WindowStyle Hidden `
            -RedirectStandardOutput $timeoutStdoutPath `
            -RedirectStandardError $timeoutStderrPath
        $timeoutProbeExited = $timeoutProbeProcess.WaitForExit(8000)
        if (-not $timeoutProbeExited) {
            if ($heldDiagnosticMutexAcquired) {
                $heldDiagnosticMutex.ReleaseMutex()
                $heldDiagnosticMutexAcquired = $false
            }
            if (-not $timeoutProbeProcess.WaitForExit(8000)) {
                Stop-Process -Id $timeoutProbeProcess.Id -Force -ErrorAction SilentlyContinue
            }
        }
        else {
            $timeoutProbeProcess.WaitForExit()
        }
        Assert-Equal $true $timeoutProbeExited 'Diagnostic write wait stays within a 1-second bound'
        Assert-Equal 0 $timeoutProbeProcess.ExitCode 'Diagnostic timeout probe exits without throwing'
        $timeoutProbeSummary = Get-Content -LiteralPath $timeoutStdoutPath -Raw -Encoding UTF8 | ConvertFrom-Json
        Assert-Equal $false ([bool] $timeoutProbeSummary.Threw) 'Diagnostic mutex timeout does not throw to the caller'
        Assert-Equal $false ([bool] $timeoutProbeSummary.Result) 'Diagnostic mutex timeout silently drops the line'
        Assert-Equal $true (
            [int] $timeoutProbeSummary.ElapsedMilliseconds -ge 500 -and
            [int] $timeoutProbeSummary.ElapsedMilliseconds -le 4000
        ) 'Diagnostic mutex wait uses the requested 1-second timeout'
        Assert-Equal $false (Test-Path -LiteralPath $diagnosticTimeoutLogPath -PathType Leaf) 'Dropped diagnostic line is not written'
    }
    finally {
        if ($heldDiagnosticMutexAcquired) {
            $heldDiagnosticMutex.ReleaseMutex()
        }
        $heldDiagnosticMutex.Dispose()
        if ($null -ne $timeoutProbeProcess) {
            $timeoutProbeProcess.Dispose()
        }
    }
    $afterTimeoutWrite = $false
    $afterTimeoutThrew = $false
    try {
        $afterTimeoutWrite = Write-CountdownDiagnosticEvent -Path $diagnosticTimeoutLogPath -Level Info -Event 'DiagnosticMutexTimeoutRecovered' -TimeoutSeconds 1
    }
    catch {
        $afterTimeoutThrew = $true
    }
    Assert-Equal $false $afterTimeoutThrew 'Diagnostic write after mutex release does not throw'
    Assert-Equal $true $afterTimeoutWrite 'Diagnostic write succeeds after the mutex is released'

    $diagnosticConcurrentLogPath = Join-Path $runtimeTestRoot 'logs\concurrent.jsonl'
    $diagnosticConcurrentBarrierPath = Join-Path $runtimeTestRoot 'diagnostic-concurrent.start'
    $diagnosticConcurrentProbePath = Join-Path $runtimeTestRoot 'diagnostic-concurrent-probe.ps1'
    $concurrentProbe = @'
param(
    [Parameter(Mandatory)] [string] $RuntimeModulePath,
    [Parameter(Mandatory)] [string] $LogPath,
    [Parameter(Mandatory)] [string] $BarrierPath,
    [Parameter(Mandatory)] [int] $Worker,
    [Parameter(Mandatory)] [int] $WriteCount
)
$ErrorActionPreference = 'Stop'
Import-Module -Name $RuntimeModulePath -Force
while (-not (Test-Path -LiteralPath $BarrierPath -PathType Leaf)) {
    Start-Sleep -Milliseconds 5
}
$failed = 0
for ($index = 0; $index -lt $WriteCount; $index++) {
    $written = Write-CountdownDiagnosticEvent -Path $LogPath -Level Info -Event "DiagnosticMutexWorker$Worker-$index" -MaxBytes 4096 -MaxArchives 3
    if (-not $written) { $failed++ }
}
if ($failed -ne 0) { exit 1 }
'@
    [System.IO.File]::WriteAllText($diagnosticConcurrentProbePath, $concurrentProbe, $utf8NoBomDiagnostic)
    $diagnosticPwsh = (Get-Command pwsh.exe -CommandType Application -ErrorAction Stop | Select-Object -First 1 -ExpandProperty Source)
    $concurrentProcesses = [System.Collections.Generic.List[System.Diagnostics.Process]]::new()
    $concurrentWorkerCount = 2
    $concurrentWritesPerWorker = 30
    try {
        for ($worker = 0; $worker -lt $concurrentWorkerCount; $worker++) {
            $probeLogPath = if ($worker -eq 0) {
                $diagnosticConcurrentLogPath
            }
            else {
                Join-Path $runtimeTestRoot 'logs\..\logs\concurrent.jsonl'
            }
            $probe = Start-Process -FilePath $diagnosticPwsh -ArgumentList @(
                '-NoLogo',
                '-NoProfile',
                '-File',
                $diagnosticConcurrentProbePath,
                '-RuntimeModulePath',
                $runtimeModulePath,
                '-LogPath',
                $probeLogPath,
                '-BarrierPath',
                $diagnosticConcurrentBarrierPath,
                '-Worker',
                $worker,
                '-WriteCount',
                $concurrentWritesPerWorker
            ) -WorkingDirectory $runtimeTestRoot -PassThru -WindowStyle Hidden `
                -RedirectStandardOutput (Join-Path $runtimeTestRoot "diagnostic-concurrent-$worker.stdout.txt") `
                -RedirectStandardError (Join-Path $runtimeTestRoot "diagnostic-concurrent-$worker.stderr.txt")
            [void] $concurrentProcesses.Add($probe)
        }
        Start-Sleep -Milliseconds 300
        [System.IO.File]::WriteAllText($diagnosticConcurrentBarrierPath, 'start', $utf8NoBomDiagnostic)
        foreach ($probe in $concurrentProcesses) {
            $exited = $probe.WaitForExit(30000)
            if (-not $exited) {
                Stop-Process -Id $probe.Id -Force -ErrorAction SilentlyContinue
            }
            else {
                $probe.WaitForExit()
            }
            Assert-Equal $true $exited "Diagnostic concurrent probe exited in time: PID=$($probe.Id)"
            Assert-Equal 0 $probe.ExitCode "Diagnostic concurrent probe wrote every event: PID=$($probe.Id)"
        }
        $concurrentEntries = [System.Collections.Generic.List[object]]::new()
        $concurrentParseFailures = 0
        foreach ($path in @(
                $diagnosticConcurrentLogPath,
                "$diagnosticConcurrentLogPath.1",
                "$diagnosticConcurrentLogPath.2",
                "$diagnosticConcurrentLogPath.3"
            )) {
            if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { continue }
            foreach ($line in Get-Content -LiteralPath $path -Encoding UTF8) {
                if ([string]::IsNullOrWhiteSpace($line)) { continue }
                try {
                    $concurrentEntries.Add(($line | ConvertFrom-Json))
                }
                catch {
                    $concurrentParseFailures++
                }
            }
        }
        Assert-Equal 0 $concurrentParseFailures 'Concurrent diagnostic writes do not interleave or corrupt JSONL'
        $concurrentWorkerEvents = @(
            $concurrentEntries |
                Where-Object { $_.event -like 'DiagnosticMutexWorker*' }
        )
        Assert-Equal ($concurrentWorkerCount * $concurrentWritesPerWorker) $concurrentWorkerEvents.Count 'Concurrent diagnostic writes persist every event'
        Assert-Equal $concurrentWorkerEvents.Count @($concurrentWorkerEvents.event | Sort-Object -Unique).Count 'Concurrent diagnostic writes persist unique events'
    }
    finally {
        foreach ($probe in $concurrentProcesses) {
            if (-not $probe.HasExited) {
                Stop-Process -Id $probe.Id -Force -ErrorAction SilentlyContinue
            }
            $probe.Dispose()
        }
    }
}
finally {
    Remove-Item -LiteralPath $runtimeTestRoot -Recurse -Force -ErrorAction SilentlyContinue
}

$transactionModule = Get-Module -Name 'Countdown.InstallTransaction'
$installPathGuardCommand = Get-Command Assert-CountdownInstallPathOnDDrive -Module Countdown.InstallTransaction -ErrorAction SilentlyContinue
Assert-Equal $true ($null -ne $installPathGuardCommand) 'Installer-facing D-drive path guard is exported by the transaction module'
Assert-Equal 'D:\agents\projects\时钟倒计时\expected-desktop' (Assert-CountdownInstallPathOnDDrive -Path 'D:\agents\projects\时钟倒计时\expected-desktop') 'Installer-facing D-drive path guard normalizes a safe expected desktop path'
$outsideDDriveRejected = $false
try {
    Assert-CountdownInstallPathOnDDrive -Path 'C:\unexpected-desktop' | Out-Null
}
catch {
    $outsideDDriveRejected = $_.Exception.Message -match '只允许操作 D 盘路径'
}
Assert-Equal $true $outsideDDriveRejected 'Installer-facing D-drive path guard rejects a non-D expected desktop path'
$managedFilePath = & $transactionModule { param($CommandLine) Get-CountdownManagedApplicationFileArgument -CommandLine $CommandLine } "pwsh.exe -NoProfile -File `"$runtimeApplicationPath`""
Assert-Equal ([System.IO.Path]::GetFullPath($runtimeApplicationPath)) $managedFilePath 'Managed-process parser resolves exact -File application path'
$managedEmbeddedPath = & $transactionModule { param($CommandLine) Get-CountdownManagedApplicationFileArgument -CommandLine $CommandLine } "pwsh.exe -File `"$(Join-Path $PSScriptRoot 'helper.ps1')`" -ArgumentList `"$runtimeApplicationPath`""
Assert-Equal ([System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot 'helper.ps1'))) $managedEmbeddedPath 'Managed-process parser does not treat an argument substring as the script path'
$managedCommandEmbeddedPath = & $transactionModule { param($CommandLine) Get-CountdownManagedApplicationFileArgument -CommandLine $CommandLine } "pwsh.exe -NoProfile -Command `"Write-Output ' -File $runtimeApplicationPath '`""
Assert-Equal $true ($null -eq $managedCommandEmbeddedPath) 'Managed-process parser rejects -File text inside -Command argv'
$runtimeModule = Get-Module -Name 'Countdown.Runtime'
$runtimeCommandEmbeddedPath = & $runtimeModule { param($CommandLine) Get-CountdownRuntimeFileArgument -CommandLine $CommandLine } "pwsh.exe -NoProfile -Command `"Write-Output ' -File $runtimeApplicationPath '`""
Assert-Equal $true ($null -eq $runtimeCommandEmbeddedPath) 'Runtime-process parser rejects -File text inside -Command argv'
$resolvedInteractiveDesktop = & $transactionModule {
    Resolve-CountdownInteractiveDesktopPath -ProfileImagePath '%SystemDrive%\Profiles\OfficeUser' -DesktopTemplate '%OneDrive%\Desktop' -UserName 'OfficeUser' -EnvironmentVariables @{ SystemDrive = 'D:'; OneDrive = '%USERPROFILE%\OneDrive' }
}
Assert-Equal 'D:\Profiles\OfficeUser\OneDrive\Desktop' $resolvedInteractiveDesktop 'Desktop resolver uses the Explorer SID environment rather than the current pwsh environment'
$unknownDesktopVariableRejected = $false
try {
    & $transactionModule {
        Resolve-CountdownInteractiveDesktopPath -ProfileImagePath '%SystemDrive%\Profiles\OfficeUser' -DesktopTemplate '%UnmappedDesktop%\Desktop' -UserName 'OfficeUser' -EnvironmentVariables @{ SystemDrive = 'D:' }
    } | Out-Null
}
catch {
    $unknownDesktopVariableRejected = $_.Exception.Message -match '无法按交互用户环境解析'
}
Assert-Equal $true $unknownDesktopVariableRejected 'Desktop resolver rejects variables missing from the Explorer SID environment'
$sameSessionExplorerContext = & $transactionModule {
    Get-CountdownSingleInteractiveExplorerContext -ExplorerProcesses @(
        [pscustomobject]@{ ProcessId = 101; SessionId = 7 },
        [pscustomobject]@{ ProcessId = 102; SessionId = 7 }
    ) -OwnerResolver {
        param($process)
        [pscustomobject]@{ ReturnValue = 0; Domain = 'OFFICE'; User = 'Minmin' }
    }
}
Assert-Equal 7 $sameSessionExplorerContext.SessionId 'Multiple Explorer processes in the same interactive session are accepted'
Assert-Equal 'OFFICE\Minmin' $sameSessionExplorerContext.User 'Explorer grouping retains the interactive user identity'
$multipleExplorerContextsRejected = $false
try {
    & $transactionModule {
        Get-CountdownSingleInteractiveExplorerContext -ExplorerProcesses @(
            [pscustomobject]@{ ProcessId = 201; SessionId = 7 },
            [pscustomobject]@{ ProcessId = 202; SessionId = 8 }
        ) -OwnerResolver {
            param($process)
            [pscustomobject]@{ ReturnValue = 0; Domain = 'OFFICE'; User = 'Minmin' }
        } | Out-Null
    }
}
catch {
    $multipleExplorerContextsRejected = $_.Exception.Message -match 'exactly one interactive Explorer session and user'
}
Assert-Equal $true $multipleExplorerContextsRejected 'Multiple interactive Explorer sessions are rejected as ambiguous'

$stoppedWithoutProcesses = Test-CountdownManagedApplicationStopped -ProcessIds @() -TaskStates @{ 'Minmin Countdown' = 'Ready'; '敏敏子倒计时' = 'Missing' }
Assert-Equal $true $stoppedWithoutProcesses.Ready 'Managed application stop accepts non-running tasks with no process'
$taskStillRunning = Test-CountdownManagedApplicationStopped -ProcessIds @() -TaskStates @{ 'Minmin Countdown' = 'Running'; '敏敏子倒计时' = 'Ready' }
Assert-Equal $false $taskStillRunning.Ready 'Managed application stop waits for a running scheduled task even without a process'
Assert-Equal 'Minmin Countdown' (@($taskStillRunning.RunningTasks) -join ',') 'Managed application stop reports the running task'
$taskStillQueued = Test-CountdownManagedApplicationStopped -ProcessIds @() -TaskStates @{ 'Minmin Countdown' = 'Queued'; '敏敏子倒计时' = 'Ready' }
Assert-Equal $false $taskStillQueued.Ready 'Managed application stop waits for a queued scheduled task even without a process'
$taskStateUnknown = Test-CountdownManagedApplicationStopped -ProcessIds @() -TaskStates @{ 'Minmin Countdown' = 'Unknown'; '敏敏子倒计时' = 'Ready' }
Assert-Equal $false $taskStateUnknown.Ready 'Managed application stop treats an unknown scheduled-task state as still active'
$processQueryFailureRejected = $false
try {
    & $transactionModule {
        param($ApplicationPath)
        Get-CountdownManagedApplicationProcesses -ApplicationPath $ApplicationPath -ProcessQueryAction { throw 'injected process query failure' }
    } $runtimeApplicationPath | Out-Null
}
catch {
    $processQueryFailureRejected = $_.Exception.Message -match 'injected process query failure'
}
Assert-Equal $true $processQueryFailureRejected 'Managed process probe fails closed when CIM enumeration fails'
$taskQueryFailureRejected = $false
try {
    & $transactionModule {
        Get-CountdownManagedTaskStates -TaskName 'Minmin Countdown' -LegacyTaskName '敏敏子倒计时' -TaskQueryAction { throw 'injected task query failure' }
    } | Out-Null
}
catch {
    $taskQueryFailureRejected = $_.Exception.Message -match 'injected task query failure'
}
Assert-Equal $true $taskQueryFailureRejected 'Managed task probe fails closed when scheduled-task enumeration fails'
$legacyRemovalQueryFailureRejected = $false
try {
    Remove-CountdownScheduledTaskIfPresent -TaskName '敏敏子倒计时' -TaskQueryAction { throw 'injected legacy task query failure' } | Out-Null
}
catch {
    $legacyRemovalQueryFailureRejected = $_.Exception.Message -match 'injected legacy task query failure'
}
Assert-Equal $true $legacyRemovalQueryFailureRejected 'Legacy task removal fails closed when scheduled-task enumeration fails'
$exactTaskStates = & $transactionModule {
    Get-CountdownManagedTaskStates -TaskName 'Minmin Countdown' -LegacyTaskName '敏敏子倒计时' -TaskQueryAction {
        [pscustomobject]@{ TaskName = 'Minmin Countdown Extra'; State = 'Running' }
    }
}
Assert-Equal 'Missing' $exactTaskStates['Minmin Countdown'] 'Managed task probe requires an exact task-name match'
$stopCommand = Get-Command Stop-CountdownManagedApplication -Module Countdown.InstallTransaction
Assert-Equal $true $stopCommand.Parameters.ContainsKey('FreezeFutureStarts') 'Managed application stop can freeze future scheduled-task starts'
$mutexProbeModulePath = (Join-Path $PSScriptRoot 'Countdown.InstallTransaction.psm1').Replace("'", "''")
$mutexProbeCommand = "Import-Module -Name '$mutexProbeModulePath' -Force -DisableNameChecking; try { `$childMutex = Enter-CountdownInstallTransactionMutex -TimeoutSeconds 1; Exit-CountdownInstallTransactionMutex -Mutex `$childMutex; 'unexpected acquisition' } catch { `$_.Exception.Message }"
$mutexProbeEngine = (Get-Command pwsh.exe -CommandType Application -ErrorAction Stop | Select-Object -First 1 -ExpandProperty Source)
$transactionMutex = Enter-CountdownInstallTransactionMutex -TimeoutSeconds 1
try {
    Assert-Equal $true ($null -ne $transactionMutex) 'Install transaction mutex can be acquired'
    $mutexProbeOutput = & $mutexProbeEngine -NoLogo -NoProfile -NonInteractive -Command $mutexProbeCommand | Out-String
    Assert-Equal $true ($mutexProbeOutput -match '超时') 'Install transaction mutex excludes a second PowerShell process'
}
finally {
    Exit-CountdownInstallTransactionMutex -Mutex $transactionMutex
}
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
        $settingsMutexProbeOutput = & $mutexProbeEngine -NoLogo -NoProfile -NonInteractive -Command $settingsMutexProbeCommand | Out-String
        Assert-Equal $true ($settingsMutexProbeOutput -match '超时') 'Settings write mutex excludes a second PowerShell process'
    }
    finally {
        Exit-CountdownSettingsWriteMutex -Mutex $settingsMutex
    }
}

$transactionTestRoot = Join-Path $PSScriptRoot ("transaction-test-" + [guid]::NewGuid().ToString('N'))
$recoveryConflictRoot = Join-Path $PSScriptRoot ("recovery-conflict-" + [guid]::NewGuid().ToString('N'))
$committedCleanupRoot = Join-Path $PSScriptRoot ("committed-cleanup-" + [guid]::NewGuid().ToString('N'))
$reparseSafetyRoot = Join-Path $PSScriptRoot ("reparse-safety-" + [guid]::NewGuid().ToString('N'))
$reparseDirectLinkPath = $null
$reparseNestedLinkPath = $null
$transactionInstallPath = Join-Path $transactionTestRoot 'install'
$transactionDesktopPath = Join-Path $transactionTestRoot 'desktop'
$transactionLauncherPath = Join-Path $transactionDesktopPath '敏敏子倒计时.exe'
$transactionLauncherTempPath = "$transactionLauncherPath.new"
$transactionLegacyLauncherPath = Join-Path $transactionDesktopPath '敏敏子倒计时.cmd'
$transactionSuffix = [guid]::NewGuid().ToString('N')
try {
    New-Item -ItemType Directory -Path $transactionInstallPath, $transactionDesktopPath -Force | Out-Null
    $reparseTargetPath = Join-Path $reparseSafetyRoot 'protected-target'
    $reparseDirectLinkPath = Join-Path $reparseSafetyRoot 'direct-link'
    $reparseTreePath = Join-Path $reparseSafetyRoot 'transaction-tree'
    $reparseNestedLinkPath = Join-Path $reparseTreePath 'nested-link'
    New-Item -ItemType Directory -Path $reparseTargetPath, $reparseTreePath -Force | Out-Null
    [System.IO.File]::WriteAllText((Join-Path $reparseTargetPath 'protected.txt'), 'must survive', (New-Object System.Text.UTF8Encoding($false)))
    New-Item -ItemType Junction -Path $reparseDirectLinkPath -Target $reparseTargetPath | Out-Null
    New-Item -ItemType Junction -Path $reparseNestedLinkPath -Target $reparseTargetPath | Out-Null
    $directReparseRejected = $false
    try { [void] (Assert-CountdownInstallPathOnDDrive -Path (Join-Path $reparseDirectLinkPath 'protected.txt')) }
    catch { $directReparseRejected = $_.Exception.Message -match 'reparse point' }
    Assert-Equal $true $directReparseRejected 'Install path validation rejects an ancestor junction before file operations'
    $nestedReparseRejected = $false
    try { & (Get-Module Countdown.InstallTransaction) { param($path) [void] (Assert-CountdownInstallTreeHasNoReparsePoints -Path $path) } $reparseTreePath }
    catch { $nestedReparseRejected = $_.Exception.Message -match 'reparse point' }
    Assert-Equal $true $nestedReparseRejected 'Recursive transaction operations reject nested junctions before deletion or copy'
    Assert-Equal 'must survive' ([System.IO.File]::ReadAllText((Join-Path $reparseTargetPath 'protected.txt'), [System.Text.Encoding]::UTF8)) 'Reparse point rejection leaves the external target untouched'
    [System.IO.File]::WriteAllText((Join-Path $transactionInstallPath 'settings.json'), '{"before":true}', (New-Object System.Text.UTF8Encoding($false)))
    [System.IO.File]::WriteAllText((Join-Path $transactionInstallPath '敏敏子倒计时.ps1'), '# old app', (New-Object System.Text.UTF8Encoding($false)))
    [System.IO.File]::WriteAllText($transactionLauncherPath, 'old launcher', (New-Object System.Text.UTF8Encoding($false)))
    [System.IO.File]::WriteAllText($transactionLauncherTempPath, 'old staged launcher', (New-Object System.Text.UTF8Encoding($false)))
    [System.IO.File]::WriteAllText($transactionLegacyLauncherPath, 'old legacy launcher', (New-Object System.Text.UTF8Encoding($false)))
    $transactionPowerShell = (Get-Command pwsh.exe -CommandType Application -ErrorAction Stop | Select-Object -First 1 -ExpandProperty Source)
    $preparedTransaction = Start-CountdownInstallTransaction -InstallPath $transactionInstallPath -LauncherPath $transactionLauncherPath -LegacyLauncherPath $transactionLegacyLauncherPath -TaskName "Minmin Countdown Prepared $transactionSuffix" -LegacyTaskName "敏敏子倒计时 Prepared $transactionSuffix"
    Assert-Equal $false ([bool] $preparedTransaction.payloadSnapshotReady) 'Prepared transaction does not snapshot mutable files before the old application is quiesced'
    [System.IO.File]::WriteAllText((Join-Path $transactionInstallPath 'settings.json'), '{"post-prepared":true}', (New-Object System.Text.UTF8Encoding($false)))
    Undo-CountdownInstallTransaction -TransactionPath $preparedTransaction.transactionPath -PowerShell7Path $transactionPowerShell -ExpectedInstallPath $transactionInstallPath -ExpectedTaskName "Minmin Countdown Prepared $transactionSuffix" -ExpectedLegacyTaskName "敏敏子倒计时 Prepared $transactionSuffix" -ExpectedLauncherPath $transactionLauncherPath -ExpectedLegacyLauncherPath $transactionLegacyLauncherPath -ExpectedLauncherTempPath $transactionLauncherTempPath -RestoreWindowsTimeAction { param($snapshot) }
    Assert-Equal '{"post-prepared":true}' ([System.IO.File]::ReadAllText((Join-Path $transactionInstallPath 'settings.json'), [System.Text.Encoding]::UTF8)) 'Prepared rollback preserves files not yet snapshotted after quiescence'

    [System.IO.File]::WriteAllText((Join-Path $transactionInstallPath 'settings.json'), '{"quiesced":true}', (New-Object System.Text.UTF8Encoding($false)))
    $rollbackTransaction = Start-CountdownInstallTransaction -InstallPath $transactionInstallPath -LauncherPath $transactionLauncherPath -LegacyLauncherPath $transactionLegacyLauncherPath -TaskName "Minmin Countdown Test $transactionSuffix" -LegacyTaskName "敏敏子倒计时 Test $transactionSuffix"
    $rollbackTransaction = Capture-CountdownInstallPayloadSnapshot -TransactionPath $rollbackTransaction.transactionPath -ExpectedInstallPath $transactionInstallPath -ExpectedTaskName "Minmin Countdown Test $transactionSuffix" -ExpectedLegacyTaskName "敏敏子倒计时 Test $transactionSuffix" -ExpectedLauncherPath $transactionLauncherPath -ExpectedLegacyLauncherPath $transactionLegacyLauncherPath -ExpectedLauncherTempPath $transactionLauncherTempPath
    Assert-Equal $true ([bool] $rollbackTransaction.payloadSnapshotReady) 'Transaction snapshots mutable files only after the caller has quiesced the old application'
    Assert-Equal $true (Test-Path -LiteralPath $rollbackTransaction.transactionPath) 'Transaction retains backup until external acceptance or rollback'
    $deferredReceiptPath = Join-Path $transactionTestRoot 'deferred-install-receipt.json'
    $deferredReceipt = Write-CountdownDeferredInstallReceipt -Path $deferredReceiptPath -TransactionPath $rollbackTransaction.transactionPath -LaunchToken ([guid]::NewGuid().ToString('N')) -InteractiveSessionId 1
    $readDeferredReceipt = Read-CountdownDeferredInstallReceipt -Path $deferredReceiptPath
    Assert-Equal $rollbackTransaction.transactionPath $readDeferredReceipt.transactionPath 'Deferred install receipt preserves rollback transaction path'
    Assert-Equal $deferredReceipt.launchToken $readDeferredReceipt.launchToken 'Deferred install receipt preserves startup token'
    [System.IO.File]::WriteAllText($deferredReceiptPath, '{bad-json', (New-Object System.Text.UTF8Encoding($false)))
    Assert-Equal $true ($null -eq (Read-CountdownDeferredInstallReceipt -Path $deferredReceiptPath)) 'Malformed deferred install receipt is rejected'
    $failureReceiptPath = Join-Path $transactionTestRoot 'deployment-failure.json'
    $failureReceiptRecord = $null
    $failureReceiptException = [System.InvalidOperationException]::new('failure receipt sentinel')
    $failureReceiptException.Data['do-not-serialize'] = 'sentinel-secret'
    try {
        throw $failureReceiptException
    }
    catch {
        $failureReceiptRecord = $_
    }
    $failureReceipt = Write-CountdownInstallFailureReceipt `
        -Path $failureReceiptPath `
        -Stage 'replace-desktop-launcher' `
        -ErrorRecord $failureReceiptRecord `
        -TransactionStarted $true `
        -CommitState 'not-committed' `
        -RollbackAttempted $true `
        -RollbackSucceeded $true `
        -PreservedUnmanagedLaunchers $false
    $failureReceiptText = [System.IO.File]::ReadAllText($failureReceiptPath, [System.Text.Encoding]::UTF8)
    $readFailureReceipt = $failureReceiptText | ConvertFrom-Json
    Assert-Equal 2 ([int] $readFailureReceipt.schemaVersion) 'Failure receipt uses a stable schema version'
    Assert-Equal 'replace-desktop-launcher' ([string] $readFailureReceipt.stage) 'Failure receipt records the precise installer stage'
    Assert-Equal 'failure receipt sentinel' ([string] $readFailureReceipt.exceptionMessage) 'Failure receipt preserves the original exception message'
    Assert-Equal $true (-not [string]::IsNullOrWhiteSpace([string] $readFailureReceipt.fullyQualifiedErrorId)) 'Failure receipt preserves the fully qualified error ID'
    Assert-Equal $true (-not [string]::IsNullOrWhiteSpace([string] $readFailureReceipt.positionMessage)) 'Failure receipt preserves the original error position'
    Assert-Equal $true (-not [string]::IsNullOrWhiteSpace([string] $readFailureReceipt.scriptStackTrace)) 'Failure receipt preserves the original script stack'
    Assert-Equal $true ([bool] $readFailureReceipt.transactionStarted -and [string] $readFailureReceipt.commitState -eq 'not-committed' -and [bool] $readFailureReceipt.rollbackAttempted -and [bool] $readFailureReceipt.rollbackSucceeded) 'Failure receipt records transactional rollback state'
    Assert-Equal $false ([bool] $readFailureReceipt.preservedUnmanagedLaunchers) 'Failure receipt records launcher preservation state'
    Assert-Equal $true ($failureReceiptText -notmatch 'sentinel-secret') 'Failure receipt serializes selected diagnostics rather than exception data'
    Assert-Equal 'replace-desktop-launcher' ([string] $failureReceipt.stage) 'Failure receipt writer returns the persisted receipt'
    Assert-Equal $true ([System.Text.UTF8Encoding]::new($false).GetByteCount($failureReceiptText) -le 24576) 'Failure receipt remains below the remote retrieval ceiling'
    $validatedFailureReceipt = ConvertFrom-CountdownInstallFailureReceiptBytes -Bytes ([System.Text.UTF8Encoding]::new($false).GetBytes($failureReceiptText))
    Assert-Equal 'not-committed' ([string] $validatedFailureReceipt.commitState) 'Failure receipt parser accepts the writer output'
    $untrustedFailureReceipt = [ordered]@{}
    foreach ($property in $readFailureReceipt.PSObject.Properties) {
        $untrustedFailureReceipt[$property.Name] = $property.Value
    }
    $untrustedFailureReceipt['unexpected'] = 'do-not-persist'
    $untrustedFailureReceiptRejected = $false
    try {
        ConvertFrom-CountdownInstallFailureReceiptBytes -Bytes ([System.Text.UTF8Encoding]::new($false).GetBytes(($untrustedFailureReceipt | ConvertTo-Json -Depth 4))) | Out-Null
    }
    catch {
        $untrustedFailureReceiptRejected = $_.Exception.Message -match '字段数量无效|未允许字段'
    }
    Assert-Equal $true $untrustedFailureReceiptRejected 'Failure receipt parser rejects non-whitelisted fields'
    $invalidStateReceipt = [ordered]@{}
    foreach ($property in $readFailureReceipt.PSObject.Properties) {
        $invalidStateReceipt[$property.Name] = $property.Value
    }
    $invalidStateReceipt.commitState = 'unknown'
    $invalidStateReceipt.rollbackSucceeded = $true
    $invalidStateReceiptRejected = $false
    try {
        ConvertFrom-CountdownInstallFailureReceiptBytes -Bytes ([System.Text.UTF8Encoding]::new($false).GetBytes(($invalidStateReceipt | ConvertTo-Json -Depth 4))) | Out-Null
    }
    catch {
        $invalidStateReceiptRejected = $_.Exception.Message -match '不能声称回滚成功'
    }
    Assert-Equal $true $invalidStateReceiptRejected 'Failure receipt parser rejects an unknown commit state that claims rollback success'
    $invalidUtf8ReceiptRejected = $false
    try {
        ConvertFrom-CountdownInstallFailureReceiptBytes -Bytes ([byte[]] @(0xC3, 0x28)) | Out-Null
    }
    catch {
        $invalidUtf8ReceiptRejected = $_.Exception.Message -match '有效 UTF-8 JSON'
    }
    Assert-Equal $true $invalidUtf8ReceiptRejected 'Failure receipt parser rejects invalid UTF-8'
    $longFailureReceiptRecord = $null
    try {
        throw [System.InvalidOperationException]::new((('诊断' * 8000) -join ''))
    }
    catch {
        $longFailureReceiptRecord = $_
    }
    $longFailureReceiptPath = Join-Path $transactionTestRoot 'deployment-failure-long.json'
    $longFailureReceipt = Write-CountdownInstallFailureReceipt `
        -Path $longFailureReceiptPath `
        -Stage (('阶段' * 100) -join '') `
        -ErrorRecord $longFailureReceiptRecord `
        -TransactionStarted $true `
        -CommitState 'not-committed' `
        -RollbackAttempted $true `
        -RollbackSucceeded $true `
        -PreservedUnmanagedLaunchers $false
    $longFailureReceiptBytes = [System.IO.File]::ReadAllBytes($longFailureReceiptPath)
    Assert-Equal $true ($longFailureReceiptBytes.Length -le 24576) 'Oversized diagnostics remain within the 24 KiB receipt contract'
    Assert-Equal $true (([System.Text.UTF8Encoding]::new($false)).GetByteCount([string] $longFailureReceipt.stage) -le 128) 'Failure receipt truncates stage by UTF-8 bytes'
    Assert-Equal $true (([System.Text.UTF8Encoding]::new($false)).GetByteCount([string] $longFailureReceipt.exceptionMessage) -le 1024) 'Failure receipt truncates exception messages by UTF-8 bytes'
    $candidateLauncherSourcePath = Join-Path $transactionTestRoot 'candidate-launcher.exe'
    [System.IO.File]::WriteAllText($candidateLauncherSourcePath, 'new launcher', (New-Object System.Text.UTF8Encoding($false)))
    $candidateLauncherHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $candidateLauncherSourcePath).Hash

    # journal 更新的改名一步失败时，旧 journal 必须原样保留：恢复会跳过没有 journal.json 的事务目录。
    # 故障注入：只共享读地持有事务目录句柄，同目录的新建与删除照常，改名进该目录在释放前报共享冲突。
    if (-not ('CountdownTestDirectoryHold' -as [type])) {
        Add-Type -TypeDefinition @'
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;

public static class CountdownTestDirectoryHold
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(
        string fileName, uint desiredAccess, uint shareMode, IntPtr securityAttributes,
        uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);

    public static SafeFileHandle Open(string path)
    {
        const uint GenericRead = 0x80000000;
        const uint ShareRead = 0x00000001;
        const uint OpenExisting = 3;
        const uint BackupSemantics = 0x02000000;
        var handle = CreateFileW(path, GenericRead, ShareRead, IntPtr.Zero, OpenExisting, BackupSemantics, IntPtr.Zero);
        if (handle.IsInvalid)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        return handle;
    }

    public static Task ReleaseAfter(SafeFileHandle handle, int milliseconds)
    {
        return Task.Delay(milliseconds).ContinueWith(_ => handle.Dispose(), TaskScheduler.Default);
    }
}
'@
    }
    $rollbackJournalPath = Join-Path $rollbackTransaction.transactionPath 'journal.json'
    $journalBeforeStuckUpdate = [System.IO.File]::ReadAllText($rollbackJournalPath, [System.Text.Encoding]::UTF8)
    $stuckJournalError = $null
    $stuckJournalHold = [CountdownTestDirectoryHold]::Open($rollbackTransaction.transactionPath)
    try {
        Set-CountdownInstallTransactionCandidateLauncherHash -TransactionPath $rollbackTransaction.transactionPath -CandidateLauncherSha256 $candidateLauncherHash -ExpectedInstallPath $transactionInstallPath -ExpectedTaskName "Minmin Countdown Test $transactionSuffix" -ExpectedLegacyTaskName "敏敏子倒计时 Test $transactionSuffix" -ExpectedLauncherPath $transactionLauncherPath -ExpectedLegacyLauncherPath $transactionLegacyLauncherPath -ExpectedLauncherTempPath $transactionLauncherTempPath | Out-Null
    }
    catch { $stuckJournalError = $_.Exception.Message }
    finally { $stuckJournalHold.Dispose() }
    $journalAfterStuckUpdate = if (Test-Path -LiteralPath $rollbackJournalPath -PathType Leaf) { [System.IO.File]::ReadAllText($rollbackJournalPath, [System.Text.Encoding]::UTF8) } else { $null }
    Assert-Equal $true ([string] $stuckJournalError).Contains($rollbackJournalPath) 'Journal update that cannot be renamed fails with the journal path'
    Assert-Equal $journalBeforeStuckUpdate $journalAfterStuckUpdate 'Journal update that cannot be renamed keeps the previous journal for recovery'
    Assert-Equal 0 @(Get-ChildItem -LiteralPath $rollbackTransaction.transactionPath -Filter 'journal.json.*.tmp').Count 'Journal update leaves no temporary file after giving up'

    $briefJournalError = $null
    $briefJournalRelease = [CountdownTestDirectoryHold]::ReleaseAfter([CountdownTestDirectoryHold]::Open($rollbackTransaction.transactionPath), 500)
    try {
        Set-CountdownInstallTransactionCandidateLauncherHash -TransactionPath $rollbackTransaction.transactionPath -CandidateLauncherSha256 $candidateLauncherHash -ExpectedInstallPath $transactionInstallPath -ExpectedTaskName "Minmin Countdown Test $transactionSuffix" -ExpectedLegacyTaskName "敏敏子倒计时 Test $transactionSuffix" -ExpectedLauncherPath $transactionLauncherPath -ExpectedLegacyLauncherPath $transactionLegacyLauncherPath -ExpectedLauncherTempPath $transactionLauncherTempPath | Out-Null
    }
    catch { $briefJournalError = $_.Exception.Message }
    finally { $briefJournalRelease.Wait() }
    Assert-Equal $null $briefJournalError 'Journal update waits out a transient rename hold instead of failing'
    Assert-Equal 'launcher-candidate-staged' ([string] ((Get-Content -LiteralPath $rollbackJournalPath -Raw -Encoding UTF8 | ConvertFrom-Json).phase)) 'Journal update persists after a transient rename hold'

    $rollbackTransaction = Set-CountdownInstallTransactionCandidateLauncherHash -TransactionPath $rollbackTransaction.transactionPath -CandidateLauncherSha256 $candidateLauncherHash -ExpectedInstallPath $transactionInstallPath -ExpectedTaskName "Minmin Countdown Test $transactionSuffix" -ExpectedLegacyTaskName "敏敏子倒计时 Test $transactionSuffix" -ExpectedLauncherPath $transactionLauncherPath -ExpectedLegacyLauncherPath $transactionLegacyLauncherPath -ExpectedLauncherTempPath $transactionLauncherTempPath
    Assert-Equal 'launcher-candidate-staged' $rollbackTransaction.phase 'Transaction persists candidate launcher identity before replacement'
    [System.IO.File]::WriteAllText((Join-Path $transactionInstallPath 'settings.json'), '{"after":true}', (New-Object System.Text.UTF8Encoding($false)))
    Remove-Item -LiteralPath (Join-Path $transactionInstallPath '敏敏子倒计时.ps1') -Force
    [System.IO.File]::WriteAllText((Join-Path $transactionInstallPath 'new-file.txt'), 'new payload', (New-Object System.Text.UTF8Encoding($false)))
    [System.IO.File]::WriteAllText($transactionLauncherPath, 'new launcher', (New-Object System.Text.UTF8Encoding($false)))
    Remove-Item -LiteralPath $transactionLauncherTempPath -Force
    Remove-Item -LiteralPath $transactionLegacyLauncherPath -Force
    Undo-CountdownInstallTransaction -TransactionPath $rollbackTransaction.transactionPath -PowerShell7Path $transactionPowerShell -ExpectedInstallPath $transactionInstallPath -ExpectedTaskName "Minmin Countdown Test $transactionSuffix" -ExpectedLegacyTaskName "敏敏子倒计时 Test $transactionSuffix" -ExpectedLauncherPath $transactionLauncherPath -ExpectedLegacyLauncherPath $transactionLegacyLauncherPath -ExpectedLauncherTempPath $transactionLauncherTempPath -RestoreWindowsTimeAction { param($snapshot) }
    Assert-Equal '{"quiesced":true}' ([System.IO.File]::ReadAllText((Join-Path $transactionInstallPath 'settings.json'), [System.Text.Encoding]::UTF8)) 'Transaction rollback restores the stable post-quiescence install tree'
    Assert-Equal $true (Test-Path -LiteralPath (Join-Path $transactionInstallPath '敏敏子倒计时.ps1')) 'Transaction rollback restores application entry point'
    Assert-Equal $false (Test-Path -LiteralPath (Join-Path $transactionInstallPath 'new-file.txt')) 'Transaction rollback removes new install files'
    Assert-Equal 'old launcher' ([System.IO.File]::ReadAllText($transactionLauncherPath, [System.Text.Encoding]::UTF8)) 'Transaction rollback restores desktop launcher'
    Assert-Equal 'old staged launcher' ([System.IO.File]::ReadAllText($transactionLauncherTempPath, [System.Text.Encoding]::UTF8)) 'Transaction rollback restores desktop launcher staging file'
    Assert-Equal 'old legacy launcher' ([System.IO.File]::ReadAllText($transactionLegacyLauncherPath, [System.Text.Encoding]::UTF8)) 'Transaction rollback restores legacy launcher'
    Assert-Equal $false (Test-Path -LiteralPath $rollbackTransaction.transactionPath) 'Transaction rollback clears its journal only after restoration'
    $ownershipTransaction = Start-CountdownInstallTransaction -InstallPath $transactionInstallPath -LauncherPath $transactionLauncherPath -LegacyLauncherPath $transactionLegacyLauncherPath -TaskName "Minmin Countdown Ownership $transactionSuffix" -LegacyTaskName "敏敏子倒计时 Ownership $transactionSuffix"
    $ownershipTransaction = Capture-CountdownInstallPayloadSnapshot -TransactionPath $ownershipTransaction.transactionPath -ExpectedInstallPath $transactionInstallPath -ExpectedTaskName "Minmin Countdown Ownership $transactionSuffix" -ExpectedLegacyTaskName "敏敏子倒计时 Ownership $transactionSuffix" -ExpectedLauncherPath $transactionLauncherPath -ExpectedLegacyLauncherPath $transactionLegacyLauncherPath -ExpectedLauncherTempPath $transactionLauncherTempPath
    $ownershipTransaction = Set-CountdownInstallTransactionCandidateLauncherHash -TransactionPath $ownershipTransaction.transactionPath -CandidateLauncherSha256 $candidateLauncherHash -ExpectedInstallPath $transactionInstallPath -ExpectedTaskName "Minmin Countdown Ownership $transactionSuffix" -ExpectedLegacyTaskName "敏敏子倒计时 Ownership $transactionSuffix" -ExpectedLauncherPath $transactionLauncherPath -ExpectedLegacyLauncherPath $transactionLegacyLauncherPath -ExpectedLauncherTempPath $transactionLauncherTempPath
    [System.IO.File]::WriteAllText($transactionLauncherPath, 'user replacement', (New-Object System.Text.UTF8Encoding($false)))
    $ownershipRejected = $false
    try {
        Undo-CountdownInstallTransaction -TransactionPath $ownershipTransaction.transactionPath -PowerShell7Path $transactionPowerShell -ExpectedInstallPath $transactionInstallPath -ExpectedTaskName "Minmin Countdown Ownership $transactionSuffix" -ExpectedLegacyTaskName "敏敏子倒计时 Ownership $transactionSuffix" -ExpectedLauncherPath $transactionLauncherPath -ExpectedLegacyLauncherPath $transactionLegacyLauncherPath -ExpectedLauncherTempPath $transactionLauncherTempPath -RestoreWindowsTimeAction { param($snapshot) }
    }
    catch {
        $ownershipRejected = $_.Exception.Message -match '拒绝恢复 桌面启动器'
    }
    Assert-Equal $true $ownershipRejected 'Rollback refuses to overwrite a user-replaced desktop launcher after a crash'
    Assert-Equal 'user replacement' ([System.IO.File]::ReadAllText($transactionLauncherPath, [System.Text.Encoding]::UTF8)) 'Rejected launcher recovery preserves the user replacement'
    Assert-Equal $true (Test-Path -LiteralPath $ownershipTransaction.transactionPath) 'Rejected launcher recovery retains journal evidence'
    [System.IO.File]::WriteAllText((Join-Path $transactionInstallPath 'settings.json'), '{"partial-new":true}', (New-Object System.Text.UTF8Encoding($false)))
    Remove-Item -LiteralPath (Join-Path $transactionInstallPath '敏敏子倒计时.ps1') -Force
    $partialRollback = Undo-CountdownInstallTransaction -TransactionPath $ownershipTransaction.transactionPath -PowerShell7Path $transactionPowerShell -ExpectedInstallPath $transactionInstallPath -ExpectedTaskName "Minmin Countdown Ownership $transactionSuffix" -ExpectedLegacyTaskName "敏敏子倒计时 Ownership $transactionSuffix" -ExpectedLauncherPath $transactionLauncherPath -ExpectedLegacyLauncherPath $transactionLegacyLauncherPath -ExpectedLauncherTempPath $transactionLauncherTempPath -PreserveUnmanagedLaunchers -RestoreWindowsTimeAction { param($snapshot) }
    Assert-Equal $true ([bool] $partialRollback.PreservedUnmanagedLaunchers) 'Conflict rollback restores non-launcher state without overwriting a user launcher'
    Assert-Equal '{"quiesced":true}' ([System.IO.File]::ReadAllText((Join-Path $transactionInstallPath 'settings.json'), [System.Text.Encoding]::UTF8)) 'Conflict rollback restores the stable install tree'
    Assert-Equal $true (Test-Path -LiteralPath (Join-Path $transactionInstallPath '敏敏子倒计时.ps1')) 'Conflict rollback restores the application entry point'
    Assert-Equal 'user replacement' ([System.IO.File]::ReadAllText($transactionLauncherPath, [System.Text.Encoding]::UTF8)) 'Conflict rollback continues to preserve the user launcher'
    $partialRollbackJournal = [System.IO.File]::ReadAllText((Join-Path $ownershipTransaction.transactionPath 'journal.json'), [System.Text.Encoding]::UTF8) | ConvertFrom-Json
    Assert-Equal 'launcher-ownership-conflict' $partialRollbackJournal.phase 'Conflict rollback retains a dedicated journal phase for safe later recovery'
    Copy-Item -LiteralPath $candidateLauncherSourcePath -Destination $transactionLauncherPath -Force
    Undo-CountdownInstallTransaction -TransactionPath $ownershipTransaction.transactionPath -PowerShell7Path $transactionPowerShell -ExpectedInstallPath $transactionInstallPath -ExpectedTaskName "Minmin Countdown Ownership $transactionSuffix" -ExpectedLegacyTaskName "敏敏子倒计时 Ownership $transactionSuffix" -ExpectedLauncherPath $transactionLauncherPath -ExpectedLegacyLauncherPath $transactionLegacyLauncherPath -ExpectedLauncherTempPath $transactionLauncherTempPath -RestoreWindowsTimeAction { param($snapshot) }
    $recoveryConflictInstallPath = Join-Path $recoveryConflictRoot 'install'
    $recoveryConflictDesktopPath = Join-Path $recoveryConflictRoot 'desktop'
    $recoveryConflictLauncherPath = Join-Path $recoveryConflictDesktopPath '敏敏子倒计时.exe'
    $recoveryConflictLauncherTempPath = "$recoveryConflictLauncherPath.new"
    $recoveryConflictLegacyLauncherPath = Join-Path $recoveryConflictDesktopPath '敏敏子倒计时.cmd'
    New-Item -ItemType Directory -Path $recoveryConflictInstallPath, $recoveryConflictDesktopPath -Force | Out-Null
    [System.IO.File]::WriteAllText((Join-Path $recoveryConflictInstallPath 'settings.json'), '{"isolated-before":true}', (New-Object System.Text.UTF8Encoding($false)))
    [System.IO.File]::WriteAllText((Join-Path $recoveryConflictInstallPath '敏敏子倒计时.ps1'), '# isolated old app', (New-Object System.Text.UTF8Encoding($false)))
    [System.IO.File]::WriteAllText($recoveryConflictLauncherPath, 'isolated old launcher', (New-Object System.Text.UTF8Encoding($false)))
    [System.IO.File]::WriteAllText($recoveryConflictLauncherTempPath, 'isolated old staged launcher', (New-Object System.Text.UTF8Encoding($false)))
    [System.IO.File]::WriteAllText($recoveryConflictLegacyLauncherPath, 'isolated old legacy launcher', (New-Object System.Text.UTF8Encoding($false)))
    $recoveryConflictTransaction = Start-CountdownInstallTransaction -InstallPath $recoveryConflictInstallPath -LauncherPath $recoveryConflictLauncherPath -LegacyLauncherPath $recoveryConflictLegacyLauncherPath -TaskName "Minmin Countdown Recovery Conflict $transactionSuffix" -LegacyTaskName "敏敏子倒计时 Recovery Conflict $transactionSuffix"
    $recoveryConflictTransaction = Capture-CountdownInstallPayloadSnapshot -TransactionPath $recoveryConflictTransaction.transactionPath -ExpectedInstallPath $recoveryConflictInstallPath -ExpectedTaskName "Minmin Countdown Recovery Conflict $transactionSuffix" -ExpectedLegacyTaskName "敏敏子倒计时 Recovery Conflict $transactionSuffix" -ExpectedLauncherPath $recoveryConflictLauncherPath -ExpectedLegacyLauncherPath $recoveryConflictLegacyLauncherPath -ExpectedLauncherTempPath $recoveryConflictLauncherTempPath
    $recoveryConflictTransaction = Set-CountdownInstallTransactionCandidateLauncherHash -TransactionPath $recoveryConflictTransaction.transactionPath -CandidateLauncherSha256 $candidateLauncherHash -ExpectedInstallPath $recoveryConflictInstallPath -ExpectedTaskName "Minmin Countdown Recovery Conflict $transactionSuffix" -ExpectedLegacyTaskName "敏敏子倒计时 Recovery Conflict $transactionSuffix" -ExpectedLauncherPath $recoveryConflictLauncherPath -ExpectedLegacyLauncherPath $recoveryConflictLegacyLauncherPath -ExpectedLauncherTempPath $recoveryConflictLauncherTempPath
    [System.IO.File]::WriteAllText((Join-Path $recoveryConflictInstallPath 'settings.json'), '{"isolated-new":true}', (New-Object System.Text.UTF8Encoding($false)))
    [System.IO.File]::WriteAllText($recoveryConflictLauncherPath, 'isolated user replacement', (New-Object System.Text.UTF8Encoding($false)))
    $recoveryConflictPartialRollback = Undo-CountdownInstallTransaction -TransactionPath $recoveryConflictTransaction.transactionPath -PowerShell7Path $transactionPowerShell -ExpectedInstallPath $recoveryConflictInstallPath -ExpectedTaskName "Minmin Countdown Recovery Conflict $transactionSuffix" -ExpectedLegacyTaskName "敏敏子倒计时 Recovery Conflict $transactionSuffix" -ExpectedLauncherPath $recoveryConflictLauncherPath -ExpectedLegacyLauncherPath $recoveryConflictLegacyLauncherPath -ExpectedLauncherTempPath $recoveryConflictLauncherTempPath -PreserveUnmanagedLaunchers -RestoreWindowsTimeAction { param($snapshot) }
    Assert-Equal $true ([bool] $recoveryConflictPartialRollback.PreservedUnmanagedLaunchers) 'Isolated conflict setup preserves its user launcher during partial rollback'
    $recoveryConflictStatus = Recover-CountdownInstallTransactions -InstallPath $recoveryConflictInstallPath -PowerShell7Path $transactionPowerShell -ExpectedTaskName "Minmin Countdown Recovery Conflict $transactionSuffix" -ExpectedLegacyTaskName "敏敏子倒计时 Recovery Conflict $transactionSuffix" -ExpectedLauncherPath $recoveryConflictLauncherPath -ExpectedLegacyLauncherPath $recoveryConflictLegacyLauncherPath -ExpectedLauncherTempPath $recoveryConflictLauncherTempPath
    Assert-Equal 0 @($recoveryConflictStatus.RecoveredTransactionPaths).Count 'Recovery does not falsely report a user-launcher conflict as complete'
    Assert-Equal 1 @($recoveryConflictStatus.UnresolvedLauncherOwnershipConflicts).Count 'Recovery returns a structured unresolved launcher conflict'
    Assert-Equal 'isolated user replacement' ([System.IO.File]::ReadAllText($recoveryConflictLauncherPath, [System.Text.Encoding]::UTF8)) 'Recovery status probing does not overwrite the isolated user launcher'
    $strictBooleanTransaction = Start-CountdownInstallTransaction -InstallPath $transactionInstallPath -LauncherPath $transactionLauncherPath -LegacyLauncherPath $transactionLegacyLauncherPath -TaskName "Minmin Countdown Strict Boolean $transactionSuffix" -LegacyTaskName "敏敏子倒计时 Strict Boolean $transactionSuffix"
    $strictBooleanJournalPath = Join-Path $strictBooleanTransaction.transactionPath 'journal.json'
    $strictBooleanJournal = [System.IO.File]::ReadAllText($strictBooleanJournalPath, [System.Text.Encoding]::UTF8) | ConvertFrom-Json
    $strictBooleanJournal.payloadSnapshotReady = 'false'
    [System.IO.File]::WriteAllText($strictBooleanJournalPath, ($strictBooleanJournal | ConvertTo-Json -Depth 8), (New-Object System.Text.UTF8Encoding($false)))
    $strictBooleanRejected = $false
    try {
        Undo-CountdownInstallTransaction -TransactionPath $strictBooleanTransaction.transactionPath -PowerShell7Path $transactionPowerShell -ExpectedInstallPath $transactionInstallPath -ExpectedTaskName "Minmin Countdown Strict Boolean $transactionSuffix" -ExpectedLegacyTaskName "敏敏子倒计时 Strict Boolean $transactionSuffix" -ExpectedLauncherPath $transactionLauncherPath -ExpectedLegacyLauncherPath $transactionLegacyLauncherPath -ExpectedLauncherTempPath $transactionLauncherTempPath -RestoreWindowsTimeAction { param($snapshot) }
    }
    catch {
        $strictBooleanRejected = $_.Exception.Message -match '严格布尔字段 payloadSnapshotReady'
    }
    Assert-Equal $true $strictBooleanRejected 'Rollback rejects a string payloadSnapshotReady before deleting any files'
    Assert-Equal '{"quiesced":true}' ([System.IO.File]::ReadAllText((Join-Path $transactionInstallPath 'settings.json'), [System.Text.Encoding]::UTF8)) 'Malformed prepared journal leaves current install tree intact'
    $missingBackupTransaction = Start-CountdownInstallTransaction -InstallPath $transactionInstallPath -LauncherPath $transactionLauncherPath -LegacyLauncherPath $transactionLegacyLauncherPath -TaskName "Minmin Countdown Missing Backup $transactionSuffix" -LegacyTaskName "敏敏子倒计时 Missing Backup $transactionSuffix"
    $missingBackupTransaction = Capture-CountdownInstallPayloadSnapshot -TransactionPath $missingBackupTransaction.transactionPath -ExpectedInstallPath $transactionInstallPath -ExpectedTaskName "Minmin Countdown Missing Backup $transactionSuffix" -ExpectedLegacyTaskName "敏敏子倒计时 Missing Backup $transactionSuffix" -ExpectedLauncherPath $transactionLauncherPath -ExpectedLegacyLauncherPath $transactionLegacyLauncherPath -ExpectedLauncherTempPath $transactionLauncherTempPath
    [System.IO.File]::WriteAllText((Join-Path $transactionInstallPath 'settings.json'), '{"must-not-delete":true}', (New-Object System.Text.UTF8Encoding($false)))
    Remove-Item -LiteralPath $missingBackupTransaction.backupInstallPath -Recurse -Force
    $missingBackupRejected = $false
    try {
        Undo-CountdownInstallTransaction -TransactionPath $missingBackupTransaction.transactionPath -PowerShell7Path $transactionPowerShell -ExpectedInstallPath $transactionInstallPath -ExpectedTaskName "Minmin Countdown Missing Backup $transactionSuffix" -ExpectedLegacyTaskName "敏敏子倒计时 Missing Backup $transactionSuffix" -ExpectedLauncherPath $transactionLauncherPath -ExpectedLegacyLauncherPath $transactionLegacyLauncherPath -ExpectedLauncherTempPath $transactionLauncherTempPath -RestoreWindowsTimeAction { param($snapshot) }
    }
    catch {
        $missingBackupRejected = $_.Exception.Message -match '缺少安装目录备份'
    }
    Assert-Equal $true $missingBackupRejected 'Rollback verifies install backup before deleting the current install tree'
    Assert-Equal '{"must-not-delete":true}' ([System.IO.File]::ReadAllText((Join-Path $transactionInstallPath 'settings.json'), [System.Text.Encoding]::UTF8)) 'Missing backup leaves current install tree intact'
    $invalidTaskXmlTransaction = Start-CountdownInstallTransaction -InstallPath $transactionInstallPath -LauncherPath $transactionLauncherPath -LegacyLauncherPath $transactionLegacyLauncherPath -TaskName "Minmin Countdown Invalid XML $transactionSuffix" -LegacyTaskName "敏敏子倒计时 Invalid XML $transactionSuffix"
    $invalidTaskXmlJournalPath = Join-Path $invalidTaskXmlTransaction.transactionPath 'journal.json'
    $invalidTaskXmlJournal = [System.IO.File]::ReadAllText($invalidTaskXmlJournalPath, [System.Text.Encoding]::UTF8) | ConvertFrom-Json
    $invalidTaskXmlJournal.currentTask.Exists = $true
    $invalidTaskXmlJournal.currentTask.State = 'Ready'
    [System.IO.File]::WriteAllText([string] $invalidTaskXmlJournal.currentTask.XmlPath, '<Task>', (New-Object System.Text.UTF8Encoding($false)))
    [System.IO.File]::WriteAllText($invalidTaskXmlJournalPath, ($invalidTaskXmlJournal | ConvertTo-Json -Depth 8), (New-Object System.Text.UTF8Encoding($false)))
    [System.IO.File]::WriteAllText((Join-Path $transactionInstallPath 'settings.json'), '{"task-xml-must-not-delete":true}', (New-Object System.Text.UTF8Encoding($false)))
    $invalidTaskXmlRejected = $false
    try {
        Undo-CountdownInstallTransaction -TransactionPath $invalidTaskXmlTransaction.transactionPath -PowerShell7Path $transactionPowerShell -ExpectedInstallPath $transactionInstallPath -ExpectedTaskName "Minmin Countdown Invalid XML $transactionSuffix" -ExpectedLegacyTaskName "敏敏子倒计时 Invalid XML $transactionSuffix" -ExpectedLauncherPath $transactionLauncherPath -ExpectedLegacyLauncherPath $transactionLegacyLauncherPath -ExpectedLauncherTempPath $transactionLauncherTempPath -RestoreWindowsTimeAction { param($snapshot) }
    }
    catch {
        $invalidTaskXmlRejected = $_.Exception.Message -match 'currentTask XML 无法解析'
    }
    Assert-Equal $true $invalidTaskXmlRejected 'Rollback rejects a truncated task XML before any destructive action'
    Assert-Equal '{"task-xml-must-not-delete":true}' ([System.IO.File]::ReadAllText((Join-Path $transactionInstallPath 'settings.json'), [System.Text.Encoding]::UTF8)) 'Invalid task XML leaves the current install tree intact'
    Assert-Equal $true (Test-Path -LiteralPath $invalidTaskXmlTransaction.transactionPath) 'Invalid task XML keeps journal evidence for manual repair'
    $identityTransaction = Start-CountdownInstallTransaction -InstallPath $transactionInstallPath -LauncherPath $transactionLauncherPath -LegacyLauncherPath $transactionLegacyLauncherPath -TaskName "Minmin Countdown Identity $transactionSuffix" -LegacyTaskName "敏敏子倒计时 Identity $transactionSuffix"
    $identityRejected = $false
    try {
        Undo-CountdownInstallTransaction -TransactionPath $identityTransaction.transactionPath -PowerShell7Path $transactionPowerShell -ExpectedInstallPath $transactionInstallPath -ExpectedTaskName 'unexpected task' -ExpectedLegacyTaskName "敏敏子倒计时 Identity $transactionSuffix" -ExpectedLauncherPath $transactionLauncherPath -ExpectedLegacyLauncherPath $transactionLegacyLauncherPath -ExpectedLauncherTempPath $transactionLauncherTempPath -RestoreWindowsTimeAction { param($snapshot) }
    }
    catch {
        $identityRejected = $_.Exception.Message -match 'taskName 与当前安装任务不一致'
    }
    Assert-Equal $true $identityRejected 'Transaction rollback binds its journal to the expected task identities'
    $launcherIdentityRejected = $false
    try {
        Undo-CountdownInstallTransaction -TransactionPath $identityTransaction.transactionPath -PowerShell7Path $transactionPowerShell -ExpectedInstallPath $transactionInstallPath -ExpectedTaskName "Minmin Countdown Identity $transactionSuffix" -ExpectedLegacyTaskName "敏敏子倒计时 Identity $transactionSuffix" -ExpectedLauncherPath (Join-Path $transactionDesktopPath 'other-launcher.exe') -ExpectedLegacyLauncherPath $transactionLegacyLauncherPath -ExpectedLauncherTempPath $transactionLauncherTempPath -RestoreWindowsTimeAction { param($snapshot) }
    }
    catch {
        $launcherIdentityRejected = $_.Exception.Message -match 'launcherPath 与当前安装目标不一致'
    }
    Assert-Equal $true $launcherIdentityRejected 'Transaction rollback binds its journal to the expected launcher paths'
    Undo-CountdownInstallTransaction -TransactionPath $identityTransaction.transactionPath -PowerShell7Path $transactionPowerShell -ExpectedInstallPath $transactionInstallPath -ExpectedTaskName "Minmin Countdown Identity $transactionSuffix" -ExpectedLegacyTaskName "敏敏子倒计时 Identity $transactionSuffix" -ExpectedLauncherPath $transactionLauncherPath -ExpectedLegacyLauncherPath $transactionLegacyLauncherPath -ExpectedLauncherTempPath $transactionLauncherTempPath -RestoreWindowsTimeAction { param($snapshot) }
    $commitTransaction = Start-CountdownInstallTransaction -InstallPath $transactionInstallPath -LauncherPath $transactionLauncherPath -LegacyLauncherPath $transactionLegacyLauncherPath -TaskName "Minmin Countdown Commit $transactionSuffix" -LegacyTaskName "敏敏子倒计时 Commit $transactionSuffix"
    $commitTransaction = Capture-CountdownInstallPayloadSnapshot -TransactionPath $commitTransaction.transactionPath -ExpectedInstallPath $transactionInstallPath -ExpectedTaskName "Minmin Countdown Commit $transactionSuffix" -ExpectedLegacyTaskName "敏敏子倒计时 Commit $transactionSuffix" -ExpectedLauncherPath $transactionLauncherPath -ExpectedLegacyLauncherPath $transactionLegacyLauncherPath -ExpectedLauncherTempPath $transactionLauncherTempPath
    $commitTransaction = Set-CountdownInstallTransactionCandidateLauncherHash -TransactionPath $commitTransaction.transactionPath -CandidateLauncherSha256 $candidateLauncherHash -ExpectedInstallPath $transactionInstallPath -ExpectedTaskName "Minmin Countdown Commit $transactionSuffix" -ExpectedLegacyTaskName "敏敏子倒计时 Commit $transactionSuffix" -ExpectedLauncherPath $transactionLauncherPath -ExpectedLegacyLauncherPath $transactionLegacyLauncherPath -ExpectedLauncherTempPath $transactionLauncherTempPath
    Copy-Item -LiteralPath $candidateLauncherSourcePath -Destination $transactionLauncherPath -Force
    Complete-CountdownInstallTransaction -TransactionPath $commitTransaction.transactionPath -ExpectedInstallPath $transactionInstallPath -ExpectedTaskName "Minmin Countdown Commit $transactionSuffix" -ExpectedLegacyTaskName "敏敏子倒计时 Commit $transactionSuffix" -ExpectedLauncherPath $transactionLauncherPath -ExpectedLegacyLauncherPath $transactionLegacyLauncherPath -ExpectedLauncherTempPath $transactionLauncherTempPath
    Assert-Equal $true (Test-Path -LiteralPath $commitTransaction.transactionPath) 'Transaction commit retains rollback evidence until application acceptance acknowledgement'
    $committedJournal = Get-Content -LiteralPath (Join-Path $commitTransaction.transactionPath 'journal.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    Assert-Equal 'accepted' ([string] $committedJournal.phase) 'Transaction remains rollbackable while waiting for application acknowledgement'
    Remove-CountdownCommittedInstallTransaction -TransactionPath $commitTransaction.transactionPath -ExpectedInstallPath $transactionInstallPath -ExpectedTaskName "Minmin Countdown Commit $transactionSuffix" -ExpectedLegacyTaskName "敏敏子倒计时 Commit $transactionSuffix" -ExpectedLauncherPath $transactionLauncherPath -ExpectedLegacyLauncherPath $transactionLegacyLauncherPath -ExpectedLauncherTempPath $transactionLauncherTempPath
    Assert-Equal $false (Test-Path -LiteralPath $commitTransaction.transactionPath) 'Committed transaction cleanup removes backup only after acknowledgement'
    $committedCleanupInstallPath = Join-Path $committedCleanupRoot 'install'
    $committedCleanupDesktopPath = Join-Path $committedCleanupRoot 'desktop'
    $committedCleanupLauncherPath = Join-Path $committedCleanupDesktopPath '敏敏子倒计时.exe'
    $committedCleanupLauncherTempPath = "$committedCleanupLauncherPath.new"
    $committedCleanupLegacyLauncherPath = Join-Path $committedCleanupDesktopPath '敏敏子倒计时.cmd'
    New-Item -ItemType Directory -Path $committedCleanupInstallPath, $committedCleanupDesktopPath -Force | Out-Null
    [System.IO.File]::WriteAllText((Join-Path $committedCleanupInstallPath 'settings.json'), '{"committed-before":true}', (New-Object System.Text.UTF8Encoding($false)))
    [System.IO.File]::WriteAllText($committedCleanupLauncherPath, 'committed old launcher', (New-Object System.Text.UTF8Encoding($false)))
    $committedCleanupTransaction = Start-CountdownInstallTransaction -InstallPath $committedCleanupInstallPath -LauncherPath $committedCleanupLauncherPath -LegacyLauncherPath $committedCleanupLegacyLauncherPath -TaskName "Minmin Countdown Committed Cleanup $transactionSuffix" -LegacyTaskName "敏敏子倒计时 Committed Cleanup $transactionSuffix"
    $committedCleanupTransaction = Capture-CountdownInstallPayloadSnapshot -TransactionPath $committedCleanupTransaction.transactionPath -ExpectedInstallPath $committedCleanupInstallPath -ExpectedTaskName "Minmin Countdown Committed Cleanup $transactionSuffix" -ExpectedLegacyTaskName "敏敏子倒计时 Committed Cleanup $transactionSuffix" -ExpectedLauncherPath $committedCleanupLauncherPath -ExpectedLegacyLauncherPath $committedCleanupLegacyLauncherPath -ExpectedLauncherTempPath $committedCleanupLauncherTempPath
    $committedCleanupTransaction = Set-CountdownInstallTransactionCandidateLauncherHash -TransactionPath $committedCleanupTransaction.transactionPath -CandidateLauncherSha256 $candidateLauncherHash -ExpectedInstallPath $committedCleanupInstallPath -ExpectedTaskName "Minmin Countdown Committed Cleanup $transactionSuffix" -ExpectedLegacyTaskName "敏敏子倒计时 Committed Cleanup $transactionSuffix" -ExpectedLauncherPath $committedCleanupLauncherPath -ExpectedLegacyLauncherPath $committedCleanupLegacyLauncherPath -ExpectedLauncherTempPath $committedCleanupLauncherTempPath
    Copy-Item -LiteralPath $candidateLauncherSourcePath -Destination $committedCleanupLauncherPath -Force
    [System.IO.File]::WriteAllText((Join-Path $committedCleanupInstallPath 'settings.json'), '{"committed-new":true}', (New-Object System.Text.UTF8Encoding($false)))
    Complete-CountdownInstallTransaction -TransactionPath $committedCleanupTransaction.transactionPath -ExpectedInstallPath $committedCleanupInstallPath -ExpectedTaskName "Minmin Countdown Committed Cleanup $transactionSuffix" -ExpectedLegacyTaskName "敏敏子倒计时 Committed Cleanup $transactionSuffix" -ExpectedLauncherPath $committedCleanupLauncherPath -ExpectedLegacyLauncherPath $committedCleanupLegacyLauncherPath -ExpectedLauncherTempPath $committedCleanupLauncherTempPath
    $committedTransactionLeaf = Split-Path -Leaf $committedCleanupTransaction.transactionPath
    $committedTransactionParent = Split-Path -Parent $committedCleanupTransaction.transactionPath
    Remove-CountdownCommittedInstallTransaction -TransactionPath $committedCleanupTransaction.transactionPath -ExpectedInstallPath $committedCleanupInstallPath -ExpectedTaskName "Minmin Countdown Committed Cleanup $transactionSuffix" -ExpectedLegacyTaskName "敏敏子倒计时 Committed Cleanup $transactionSuffix" -ExpectedLauncherPath $committedCleanupLauncherPath -ExpectedLegacyLauncherPath $committedCleanupLegacyLauncherPath -ExpectedLauncherTempPath $committedCleanupLauncherTempPath -CleanupAction { param($path) Remove-Item -LiteralPath (Join-Path $path 'journal.json') -Force; throw "模拟 journal 已删除后的 committed tombstone 清理失败：$path" }
    Assert-Equal $false (Test-Path -LiteralPath $committedCleanupTransaction.transactionPath) 'Committed cleanup atomically renames the transaction before recursive deletion'
    $committedTombstones = @(Get-ChildItem -LiteralPath $committedTransactionParent -Directory -Force | Where-Object { $_.Name -like "$committedTransactionLeaf.committed-*" })
    Assert-Equal 1 $committedTombstones.Count 'Committed cleanup failure retains one tombstone directory'
    Assert-Equal $false (Test-Path -LiteralPath (Join-Path $committedTombstones[0].FullName 'journal.json')) 'Committed cleanup test reproduces a partial deletion that already removed journal.json'
    $committedMarkers = @(Get-ChildItem -LiteralPath $committedTransactionParent -File -Filter "$committedTransactionLeaf.committed-*.marker.json" -Force)
    Assert-Equal 1 $committedMarkers.Count 'External committed cleanup marker survives partial tombstone deletion'
    $committedRecoveryStatus = Recover-CountdownInstallTransactions -InstallPath $committedCleanupInstallPath -PowerShell7Path $transactionPowerShell -ExpectedTaskName "Minmin Countdown Committed Cleanup $transactionSuffix" -ExpectedLegacyTaskName "敏敏子倒计时 Committed Cleanup $transactionSuffix" -ExpectedLauncherPath $committedCleanupLauncherPath -ExpectedLegacyLauncherPath $committedCleanupLegacyLauncherPath -ExpectedLauncherTempPath $committedCleanupLauncherTempPath -CommittedCleanupAction { param($path) throw "模拟 committed tombstone 重试仍失败：$path" }
    Assert-Equal 0 @($committedRecoveryStatus.RecoveredTransactionPaths).Count 'Recovery never rolls back a committed tombstone'
    Assert-Equal 1 @($committedRecoveryStatus.UnresolvedCommittedTransactions).Count 'Recovery reports a committed tombstone while cleanup remains blocked'
    Assert-Equal '{"committed-new":true}' ([System.IO.File]::ReadAllText((Join-Path $committedCleanupInstallPath 'settings.json'), [System.Text.Encoding]::UTF8)) 'Recovery preserves the accepted install after committed cleanup failure'
    $committedRetryStatus = Recover-CountdownInstallTransactions -InstallPath $committedCleanupInstallPath -PowerShell7Path $transactionPowerShell -ExpectedTaskName "Minmin Countdown Committed Cleanup $transactionSuffix" -ExpectedLegacyTaskName "敏敏子倒计时 Committed Cleanup $transactionSuffix" -ExpectedLauncherPath $committedCleanupLauncherPath -ExpectedLegacyLauncherPath $committedCleanupLegacyLauncherPath -ExpectedLauncherTempPath $committedCleanupLauncherTempPath
    Assert-Equal 0 @($committedRetryStatus.UnresolvedCommittedTransactions).Count 'Recovery automatically clears a committed tombstone after the transient cleanup failure ends'
    Assert-Equal $false (Test-Path -LiteralPath $committedTombstones[0].FullName) 'Committed tombstone retry removes stale transaction evidence without rollback'
    Assert-Equal $false (Test-Path -LiteralPath $committedMarkers[0].FullName) 'Committed tombstone retry removes the external marker only after cleanup succeeds'

    $moveFailureTransaction = Start-CountdownInstallTransaction -InstallPath $committedCleanupInstallPath -LauncherPath $committedCleanupLauncherPath -LegacyLauncherPath $committedCleanupLegacyLauncherPath -TaskName "Minmin Countdown Move Failure $transactionSuffix" -LegacyTaskName "敏敏子倒计时 Move Failure $transactionSuffix"
    $moveFailureTransaction = Capture-CountdownInstallPayloadSnapshot -TransactionPath $moveFailureTransaction.transactionPath -ExpectedInstallPath $committedCleanupInstallPath -ExpectedTaskName "Minmin Countdown Move Failure $transactionSuffix" -ExpectedLegacyTaskName "敏敏子倒计时 Move Failure $transactionSuffix" -ExpectedLauncherPath $committedCleanupLauncherPath -ExpectedLegacyLauncherPath $committedCleanupLegacyLauncherPath -ExpectedLauncherTempPath $committedCleanupLauncherTempPath
    $moveFailureTransaction = Set-CountdownInstallTransactionCandidateLauncherHash -TransactionPath $moveFailureTransaction.transactionPath -CandidateLauncherSha256 $candidateLauncherHash -ExpectedInstallPath $committedCleanupInstallPath -ExpectedTaskName "Minmin Countdown Move Failure $transactionSuffix" -ExpectedLegacyTaskName "敏敏子倒计时 Move Failure $transactionSuffix" -ExpectedLauncherPath $committedCleanupLauncherPath -ExpectedLegacyLauncherPath $committedCleanupLegacyLauncherPath -ExpectedLauncherTempPath $committedCleanupLauncherTempPath
    Copy-Item -LiteralPath $candidateLauncherSourcePath -Destination $committedCleanupLauncherPath -Force
    Complete-CountdownInstallTransaction -TransactionPath $moveFailureTransaction.transactionPath -ExpectedInstallPath $committedCleanupInstallPath -ExpectedTaskName "Minmin Countdown Move Failure $transactionSuffix" -ExpectedLegacyTaskName "敏敏子倒计时 Move Failure $transactionSuffix" -ExpectedLauncherPath $committedCleanupLauncherPath -ExpectedLegacyLauncherPath $committedCleanupLegacyLauncherPath -ExpectedLauncherTempPath $committedCleanupLauncherTempPath
    Remove-CountdownCommittedInstallTransaction -TransactionPath $moveFailureTransaction.transactionPath -ExpectedInstallPath $committedCleanupInstallPath -ExpectedTaskName "Minmin Countdown Move Failure $transactionSuffix" -ExpectedLegacyTaskName "敏敏子倒计时 Move Failure $transactionSuffix" -ExpectedLauncherPath $committedCleanupLauncherPath -ExpectedLegacyLauncherPath $committedCleanupLegacyLauncherPath -ExpectedLauncherTempPath $committedCleanupLauncherTempPath -MoveAction { param($source, $destination) throw "模拟 committed 后目录重命名失败：$source -> $destination" }
    $moveFailureJournal = [System.IO.File]::ReadAllText((Join-Path $moveFailureTransaction.transactionPath 'journal.json'), [System.Text.Encoding]::UTF8) | ConvertFrom-Json
    Assert-Equal 'committed' ([string] $moveFailureJournal.phase) 'Move failure after commit leaves the original transaction permanently committed'
    $committedUndoRejected = $false
    try { Undo-CountdownInstallTransaction -TransactionPath $moveFailureTransaction.transactionPath -PowerShell7Path $transactionPowerShell -ExpectedInstallPath $committedCleanupInstallPath -ExpectedTaskName "Minmin Countdown Move Failure $transactionSuffix" -ExpectedLegacyTaskName "敏敏子倒计时 Move Failure $transactionSuffix" -ExpectedLauncherPath $committedCleanupLauncherPath -ExpectedLegacyLauncherPath $committedCleanupLegacyLauncherPath -ExpectedLauncherTempPath $committedCleanupLauncherTempPath -RestoreWindowsTimeAction { param($snapshot) } }
    catch { $committedUndoRejected = $_.Exception.Message -match '禁止重新进入回滚' }
    Assert-Equal $true $committedUndoRejected 'Undo rejects a committed transaction even when tombstone rename failed'
    $moveFailureCleanupAttempts = [System.Collections.Generic.List[string]]::new()
    $moveFailureRecovery = Recover-CountdownInstallTransactions -InstallPath $committedCleanupInstallPath -PowerShell7Path $transactionPowerShell -ExpectedTaskName "Minmin Countdown Move Failure $transactionSuffix" -ExpectedLegacyTaskName "敏敏子倒计时 Move Failure $transactionSuffix" -ExpectedLauncherPath $committedCleanupLauncherPath -ExpectedLegacyLauncherPath $committedCleanupLegacyLauncherPath -ExpectedLauncherTempPath $committedCleanupLauncherTempPath -CommittedCleanupAction {
        param($path)
        [void] $moveFailureCleanupAttempts.Add($path)
        if ($moveFailureCleanupAttempts.Count -eq 1) { throw "模拟同一次 recovery 的首次清理失败：$path" }
        Remove-Item -LiteralPath $path -Recurse -Force -ErrorAction Stop
    }
    Assert-Equal 2 $moveFailureCleanupAttempts.Count 'Recovery retries the same committed transaction through its sidecar after a transient first failure'
    Assert-Equal 0 @($moveFailureRecovery.UnresolvedCommittedTransactions).Count 'Recovery cleans a committed transaction left at its original path after move failure'
    Assert-Equal $false (Test-Path -LiteralPath $moveFailureTransaction.transactionPath) 'Move-failed committed transaction is cleanup-only and never rolled back'
    $commitReplacementTransaction = Start-CountdownInstallTransaction -InstallPath $transactionInstallPath -LauncherPath $transactionLauncherPath -LegacyLauncherPath $transactionLegacyLauncherPath -TaskName "Minmin Countdown Commit Replacement $transactionSuffix" -LegacyTaskName "敏敏子倒计时 Commit Replacement $transactionSuffix"
    $commitReplacementTransaction = Capture-CountdownInstallPayloadSnapshot -TransactionPath $commitReplacementTransaction.transactionPath -ExpectedInstallPath $transactionInstallPath -ExpectedTaskName "Minmin Countdown Commit Replacement $transactionSuffix" -ExpectedLegacyTaskName "敏敏子倒计时 Commit Replacement $transactionSuffix" -ExpectedLauncherPath $transactionLauncherPath -ExpectedLegacyLauncherPath $transactionLegacyLauncherPath -ExpectedLauncherTempPath $transactionLauncherTempPath
    $commitReplacementTransaction = Set-CountdownInstallTransactionCandidateLauncherHash -TransactionPath $commitReplacementTransaction.transactionPath -CandidateLauncherSha256 $candidateLauncherHash -ExpectedInstallPath $transactionInstallPath -ExpectedTaskName "Minmin Countdown Commit Replacement $transactionSuffix" -ExpectedLegacyTaskName "敏敏子倒计时 Commit Replacement $transactionSuffix" -ExpectedLauncherPath $transactionLauncherPath -ExpectedLegacyLauncherPath $transactionLegacyLauncherPath -ExpectedLauncherTempPath $transactionLauncherTempPath
    [System.IO.File]::WriteAllText($transactionLauncherPath, 'user replacement before commit', (New-Object System.Text.UTF8Encoding($false)))
    $commitReplacementRejected = $false
    try {
        Complete-CountdownInstallTransaction -TransactionPath $commitReplacementTransaction.transactionPath -ExpectedInstallPath $transactionInstallPath -ExpectedTaskName "Minmin Countdown Commit Replacement $transactionSuffix" -ExpectedLegacyTaskName "敏敏子倒计时 Commit Replacement $transactionSuffix" -ExpectedLauncherPath $transactionLauncherPath -ExpectedLegacyLauncherPath $transactionLegacyLauncherPath -ExpectedLauncherTempPath $transactionLauncherTempPath
    }
    catch {
        $commitReplacementRejected = $_.Exception.Message -match '候选桌面启动器在提交前已被替换'
    }
    Assert-Equal $true $commitReplacementRejected 'Transaction commit rejects a launcher replaced after acceptance'
    Assert-Equal $true (Test-Path -LiteralPath $commitReplacementTransaction.transactionPath) 'Rejected transaction commit retains rollback evidence'
    $commitReplacementRollback = Undo-CountdownInstallTransaction -TransactionPath $commitReplacementTransaction.transactionPath -PowerShell7Path $transactionPowerShell -ExpectedInstallPath $transactionInstallPath -ExpectedTaskName "Minmin Countdown Commit Replacement $transactionSuffix" -ExpectedLegacyTaskName "敏敏子倒计时 Commit Replacement $transactionSuffix" -ExpectedLauncherPath $transactionLauncherPath -ExpectedLegacyLauncherPath $transactionLegacyLauncherPath -ExpectedLauncherTempPath $transactionLauncherTempPath -PreserveUnmanagedLaunchers -RestoreWindowsTimeAction { param($snapshot) }
    Assert-Equal $true ([bool] $commitReplacementRollback.PreservedUnmanagedLaunchers) 'Rejected transaction commit can preserve the user-replaced launcher during rollback'
    Assert-Equal 'user replacement before commit' ([System.IO.File]::ReadAllText($transactionLauncherPath, [System.Text.Encoding]::UTF8)) 'Rejected transaction commit does not overwrite the user-replaced launcher'
    $tamperedTransaction = Start-CountdownInstallTransaction -InstallPath $transactionInstallPath -LauncherPath $transactionLauncherPath -LegacyLauncherPath $transactionLegacyLauncherPath -TaskName "Minmin Countdown Tamper $transactionSuffix" -LegacyTaskName "敏敏子倒计时 Tamper $transactionSuffix"
    $tamperedJournalPath = Join-Path $tamperedTransaction.transactionPath 'journal.json'
    $tamperedJournal = [System.IO.File]::ReadAllText($tamperedJournalPath, [System.Text.Encoding]::UTF8) | ConvertFrom-Json
    $tamperedJournal.backupLauncherPath = 'D:\outside-transaction-backup.exe'
    [System.IO.File]::WriteAllText($tamperedJournalPath, ($tamperedJournal | ConvertTo-Json -Depth 8), (New-Object System.Text.UTF8Encoding($false)))
    $tamperedJournalRejected = $false
    try {
        Undo-CountdownInstallTransaction -TransactionPath $tamperedTransaction.transactionPath -PowerShell7Path (Get-Command pwsh.exe -CommandType Application -ErrorAction Stop | Select-Object -First 1 -ExpandProperty Source) -RestoreWindowsTimeAction { param($snapshot) }
    }
    catch {
        $tamperedJournalRejected = $_.Exception.Message -match '不在事务目录内'
    }
    Assert-Equal $true $tamperedJournalRejected 'Transaction rollback rejects backup paths outside its journal directory'
}
finally {
    foreach ($junctionPath in @($reparseDirectLinkPath, $reparseNestedLinkPath)) {
        if (-not [string]::IsNullOrWhiteSpace([string] $junctionPath) -and (Test-Path -LiteralPath $junctionPath)) {
            Remove-Item -LiteralPath $junctionPath -Force -ErrorAction SilentlyContinue
        }
    }
    Remove-Item -LiteralPath $transactionTestRoot -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $recoveryConflictRoot -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $committedCleanupRoot -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $reparseSafetyRoot -Recurse -Force -ErrorAction SilentlyContinue
}
