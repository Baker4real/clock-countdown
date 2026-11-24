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
