#requires -Version 7.0

Set-StrictMode -Version Latest

$script:BeijingTimeZone = [System.TimeZoneInfo]::FindSystemTimeZoneById('China Standard Time')

function Get-BeijingNow {
    [CmdletBinding()]
    param()

    [System.TimeZoneInfo]::ConvertTime([DateTimeOffset]::UtcNow, $script:BeijingTimeZone)
}

function ConvertTo-CountdownTargetUtc {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [string] $DateText,
        [Parameter(Mandatory)] [string] $TimeText
    )

    [DateTime] $value = [DateTime]::MinValue
    $styles = [System.Globalization.DateTimeStyles]::None
    $culture = [System.Globalization.CultureInfo]::InvariantCulture
    if (-not [DateTime]::TryParseExact(
            "$DateText $TimeText",
            'yyyy-MM-dd HH:mm:ss',
            $culture,
            $styles,
            [ref] $value
        )) {
        throw '日期时间格式无效，请使用 yyyy-MM-dd HH:mm:ss。'
    }

    $unspecified = [DateTime]::SpecifyKind($value, [DateTimeKind]::Unspecified)
    [DateTimeOffset]::new([System.TimeZoneInfo]::ConvertTimeToUtc($unspecified, $script:BeijingTimeZone))
}

function Get-CountdownParts {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [DateTimeOffset] $TargetUtc,
        [Parameter(Mandatory)] [DateTimeOffset] $NowUtc
    )

    $remaining = $TargetUtc - $NowUtc
    if ($remaining.TotalSeconds -le 0) {
        return [pscustomobject]@{
            IsElapsed = $true
            Days      = 0
            Hours     = 0
            Minutes   = 0
            Seconds   = 0
        }
    }

    [pscustomobject]@{
        IsElapsed = $false
        Days      = [Math]::Floor($remaining.TotalDays)
        Hours     = $remaining.Hours
        Minutes   = $remaining.Minutes
        Seconds   = $remaining.Seconds
    }
}

function Format-CountdownParts {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [object] $Parts,
        [ValidateSet('Day', 'Hour', 'Minute', 'Second')] [string] $Precision = 'Second'
    )

    if ($Parts.IsElapsed) { return '已到期' }
    switch ($Precision) {
        'Day' { return ('{0} 天' -f $Parts.Days) }
        'Hour' { return ('{0} 天 {1:D2} 小时' -f $Parts.Days, $Parts.Hours) }
        'Minute' { return ('{0} 天 {1:D2} 小时 {2:D2} 分钟' -f $Parts.Days, $Parts.Hours, $Parts.Minutes) }
        default { return ('{0} 天 {1:D2} 小时 {2:D2} 分钟 {3:D2} 秒' -f $Parts.Days, $Parts.Hours, $Parts.Minutes, $Parts.Seconds) }
    }
}

function Get-WorkdayEarnings {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [decimal] $MonthlySalary,
        [Parameter(Mandatory)] [TimeSpan] $WorkStart,
        [Parameter(Mandatory)] [TimeSpan] $WorkEnd,
        [Parameter(Mandatory)] [DateTimeOffset] $BeijingNow,
        [hashtable] $WorkdayMap
    )

    if ($MonthlySalary -lt 0) { throw '月薪不能小于 0。' }
    if ($WorkEnd -le $WorkStart) { throw '下班时间必须晚于上班时间。' }
    $workdays = 0
    for ($day = [DateTime]::new($BeijingNow.Year, $BeijingNow.Month, 1); $day.Month -eq $BeijingNow.Month; $day = $day.AddDays(1)) {
        $dateKey = $day.ToString('yyyy-MM-dd', [System.Globalization.CultureInfo]::InvariantCulture)
        $isWorkday = if ($null -ne $WorkdayMap -and $WorkdayMap.ContainsKey($dateKey)) { [bool] $WorkdayMap[$dateKey] } else { $day.DayOfWeek -ne [DayOfWeek]::Saturday -and $day.DayOfWeek -ne [DayOfWeek]::Sunday }
        if ($isWorkday) { $workdays++ }
    }
    if ($workdays -eq 0) { return [decimal]0 }

    $todayKey = $BeijingNow.ToString('yyyy-MM-dd', [System.Globalization.CultureInfo]::InvariantCulture)
    $isTodayWorkday = if ($null -ne $WorkdayMap -and $WorkdayMap.ContainsKey($todayKey)) { [bool] $WorkdayMap[$todayKey] } else { $BeijingNow.DayOfWeek -ne [DayOfWeek]::Saturday -and $BeijingNow.DayOfWeek -ne [DayOfWeek]::Sunday }
    if (-not $isTodayWorkday) { return [decimal]0 }

    $daySalary = $MonthlySalary / $workdays
    $current = $BeijingNow.TimeOfDay
    if ($current -le $WorkStart) { return [decimal]0 }
    if ($current -ge $WorkEnd) { return [Math]::Round($daySalary, 2, [MidpointRounding]::AwayFromZero) }
    $progress = ($current - $WorkStart).TotalSeconds / ($WorkEnd - $WorkStart).TotalSeconds
    [Math]::Round($daySalary * [decimal]$progress, 2, [MidpointRounding]::AwayFromZero)
}

function ConvertTo-CountdownDateText {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [object] $SelectedDate
    )

    ([DateTime] $SelectedDate).ToString('yyyy-MM-dd', [System.Globalization.CultureInfo]::InvariantCulture)
}

function Remove-CountdownSettings {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [string] $SettingsPath
    )

    $removed = $false
    foreach ($path in @("$SettingsPath.tmp", $SettingsPath)) {
        if (Test-Path -LiteralPath $path) {
            Remove-Item -LiteralPath $path -Force
            $removed = $true
        }
    }
    $removed
}

Export-ModuleMember -Function Get-BeijingNow, ConvertTo-CountdownTargetUtc, Get-CountdownParts, Format-CountdownParts, Get-WorkdayEarnings, ConvertTo-CountdownDateText, Remove-CountdownSettings
