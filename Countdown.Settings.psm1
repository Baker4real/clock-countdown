#requires -Version 7.0

Set-StrictMode -Version Latest

Import-Module (Join-Path $PSScriptRoot 'Countdown.Notes.psm1')
Import-Module (Join-Path $PSScriptRoot 'Countdown.Tags.psm1')

function New-CountdownPreferences {
    [CmdletBinding()]
    param(
        [ValidateSet('Day', 'Hour', 'Minute', 'Second')] [string] $Precision = 'Second',
        [decimal] $MonthlySalary = 0,
        [string] $WorkStartText = '09:00',
        [string] $WorkEndText = '18:00',
        [ValidateSet('Auto', 'Day', 'Night')] [string] $ThemePreference = 'Auto'
    )

    if ($MonthlySalary -lt 0) { throw '月薪不能小于 0。' }
    [TimeSpan] $workStart = [TimeSpan]::Zero
    [TimeSpan] $workEnd = [TimeSpan]::Zero
    if (-not [TimeSpan]::TryParseExact($WorkStartText.Trim(), 'hh\:mm', [System.Globalization.CultureInfo]::InvariantCulture, [ref] $workStart)) {
        throw '上下班时间格式无效，请使用 09:00 这样的 24 小时制时间。'
    }
    if (-not [TimeSpan]::TryParseExact($WorkEndText.Trim(), 'hh\:mm', [System.Globalization.CultureInfo]::InvariantCulture, [ref] $workEnd)) {
        throw '上下班时间格式无效，请使用 09:00 这样的 24 小时制时间。'
    }
    if ($workEnd -le $workStart) { throw '下班时间必须晚于上班时间。' }

    [pscustomobject]@{
        Precision = $Precision
        MonthlySalary = $MonthlySalary
        WorkStart = $workStart
        WorkEnd = $workEnd
        ThemePreference = $ThemePreference
    }
}

function Format-CountdownWorkTime {
    param([Parameter(Mandatory)] [TimeSpan] $Value)

    '{0:D2}:{1:D2}' -f $Value.Hours, $Value.Minutes
}

function ConvertTo-CountdownSettingsTargetUtc {
    param([Parameter(Mandatory)] [object] $Value)

    if ($Value -is [DateTimeOffset]) { return $Value.ToUniversalTime() }
    if ($Value -is [DateTime]) {
        if ($Value.Kind -eq [DateTimeKind]::Utc) { return [DateTimeOffset]::new($Value, [TimeSpan]::Zero) }
        return ([DateTimeOffset] $Value).ToUniversalTime()
    }
    [DateTimeOffset]::Parse(([string] $Value), [System.Globalization.CultureInfo]::InvariantCulture).ToUniversalTime()
}

function ConvertTo-CountdownRelativeReminderOffsetMinutes {
    param([AllowNull()] [object] $Minutes)

    $isInteger =
        $Minutes -is [sbyte] -or
        $Minutes -is [byte] -or
        $Minutes -is [int16] -or
        $Minutes -is [uint16] -or
        $Minutes -is [int32] -or
        $Minutes -is [uint32] -or
        $Minutes -is [int64] -or
        $Minutes -is [uint64]
    if (-not $isInteger -or [decimal] $Minutes -lt 0 -or [decimal] $Minutes -gt 5256000) {
        throw '倒计时相对提醒必须提前 0 到 5256000 分钟。'
    }

    [int] $Minutes
}

function ConvertTo-CountdownStrictInteger {
    param(
        [AllowNull()] [object] $Value,
        [Parameter(Mandatory)] [int] $Minimum,
        [Parameter(Mandatory)] [int] $Maximum,
        [Parameter(Mandatory)] [string] $Subject
    )

    $isInteger =
        $Value -is [sbyte] -or
        $Value -is [byte] -or
        $Value -is [int16] -or
        $Value -is [uint16] -or
        $Value -is [int32] -or
        $Value -is [uint32] -or
        $Value -is [int64] -or
        $Value -is [uint64]
    if (-not $isInteger -or [decimal] $Value -lt $Minimum -or [decimal] $Value -gt $Maximum) {
        throw "$Subject 必须是 $Minimum 到 $Maximum 之间的整数。"
    }

    [int] $Value
}

function ConvertTo-CountdownCalendarDate {
    param([AllowNull()] [object] $Value)

    $parsed = [DateOnly]::MinValue
    if ($Value -isnot [string] -or
        -not [DateOnly]::TryParseExact(
            [string] $Value,
            'yyyy-MM-dd',
            [System.Globalization.CultureInfo]::InvariantCulture,
            [System.Globalization.DateTimeStyles]::None,
            [ref] $parsed
        )) {
        throw '日历提醒开始日期必须是有效的 yyyy-MM-dd。'
    }

    $parsed.ToString('yyyy-MM-dd', [System.Globalization.CultureInfo]::InvariantCulture)
}

function ConvertTo-CountdownCalendarTime {
    param([AllowNull()] [object] $Value)

    $parsed = [TimeOnly]::MinValue
    if ($Value -isnot [string] -or
        -not [TimeOnly]::TryParseExact(
            [string] $Value,
            'HH:mm',
            [System.Globalization.CultureInfo]::InvariantCulture,
            [System.Globalization.DateTimeStyles]::None,
            [ref] $parsed
        )) {
        throw '日历提醒本地时间必须是有效的 HH:mm。'
    }

    $parsed.ToString('HH:mm', [System.Globalization.CultureInfo]::InvariantCulture)
}

function ConvertTo-CountdownCalendarWeekday {
    param([AllowNull()] [object] $Value)

    if ($Value -isnot [string]) { throw '按周日历提醒包含无效的星期。' }
    switch (([string] $Value).Trim().ToLowerInvariant()) {
        'monday' { 'monday'; break }
        'tuesday' { 'tuesday'; break }
        'wednesday' { 'wednesday'; break }
        'thursday' { 'thursday'; break }
        'friday' { 'friday'; break }
        'saturday' { 'saturday'; break }
        'sunday' { 'sunday'; break }
        default { throw '按周日历提醒包含无效的星期。' }
    }
}

function Get-CountdownCalendarWeekdayOrder {
    param([Parameter(Mandatory)] [string] $Value)

    switch ($Value) {
        'monday' { 1 }
        'tuesday' { 2 }
        'wednesday' { 3 }
        'thursday' { 4 }
        'friday' { 5 }
        'saturday' { 6 }
        'sunday' { 7 }
        default { throw '按周日历提醒包含无效的星期。' }
    }
}

function ConvertTo-CountdownCalendarRule {
    param(
        [Parameter(Mandatory)] [object] $Rule,
        [Parameter(Mandatory)] [string] $Id
    )

    $frequency = ([string] $Rule.frequency).Trim().ToLowerInvariant()
    $interval = ConvertTo-CountdownStrictInteger -Value $Rule.interval -Minimum 1 -Maximum 999 -Subject '日历提醒间隔'
    $startDate = ConvertTo-CountdownCalendarDate -Value $Rule.startDate
    $localTime = ConvertTo-CountdownCalendarTime -Value $Rule.localTime
    $normalized = [ordered]@{
        id = $Id
        type = 'calendar'
        frequency = $frequency
        interval = $interval
        startDate = $startDate
        localTime = $localTime
    }
    $selectorKey = ''

    switch ($frequency) {
        'daily' {
            Assert-CountdownNoteReminderProperties -Reminder $Rule -Expected @('id', 'type', 'frequency', 'interval', 'startDate', 'localTime')
        }
        'weekly' {
            Assert-CountdownNoteReminderProperties -Reminder $Rule -Expected @('id', 'type', 'frequency', 'interval', 'startDate', 'localTime', 'weekdays')
            if ($null -eq $Rule.weekdays -or $Rule.weekdays -is [string]) {
                throw '按周日历提醒必须至少包含一个星期。'
            }
            $seen = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
            $weekdays = @(
                foreach ($value in @($Rule.weekdays)) {
                    $weekday = ConvertTo-CountdownCalendarWeekday -Value $value
                    if (-not $seen.Add($weekday)) { throw '按周日历提醒不能包含重复星期。' }
                    $weekday
                }
            )
            if ($weekdays.Count -eq 0) { throw '按周日历提醒必须至少包含一个星期。' }
            $weekdays = @($weekdays | Sort-Object { Get-CountdownCalendarWeekdayOrder -Value $_ })
            $normalized.weekdays = $weekdays
            $selectorKey = [string]::Join(',', $weekdays)
        }
        'monthly' {
            $hasDay = $null -ne $Rule.PSObject.Properties['day']
            $hasLastDay = $null -ne $Rule.PSObject.Properties['lastDay']
            if ($hasDay -eq $hasLastDay) { throw '按月日历提醒必须且只能选择日期或最后一天。' }
            if ($hasDay) {
                Assert-CountdownNoteReminderProperties -Reminder $Rule -Expected @('id', 'type', 'frequency', 'interval', 'startDate', 'localTime', 'day')
                $day = ConvertTo-CountdownStrictInteger -Value $Rule.day -Minimum 1 -Maximum 31 -Subject '按月日历提醒日期'
                $normalized.day = $day
                $selectorKey = "day=$day"
            }
            else {
                Assert-CountdownNoteReminderProperties -Reminder $Rule -Expected @('id', 'type', 'frequency', 'interval', 'startDate', 'localTime', 'lastDay')
                if ($Rule.lastDay -isnot [bool] -or -not [bool] $Rule.lastDay) { throw '按月日历提醒最后一天必须显式为 true。' }
                $normalized.lastDay = $true
                $selectorKey = 'lastDay'
            }
        }
        'yearly' {
            $hasDay = $null -ne $Rule.PSObject.Properties['day']
            $hasLastDay = $null -ne $Rule.PSObject.Properties['lastDay']
            if ($hasDay -eq $hasLastDay) { throw '按年日历提醒必须且只能选择日期或最后一天。' }
            $month = ConvertTo-CountdownStrictInteger -Value $Rule.month -Minimum 1 -Maximum 12 -Subject '按年日历提醒月份'
            $normalized.month = $month
            if ($hasDay) {
                Assert-CountdownNoteReminderProperties -Reminder $Rule -Expected @('id', 'type', 'frequency', 'interval', 'startDate', 'localTime', 'month', 'day')
                $day = ConvertTo-CountdownStrictInteger -Value $Rule.day -Minimum 1 -Maximum 31 -Subject '按年日历提醒日期'
                $normalized.day = $day
                $selectorKey = "month=$month|day=$day"
            }
            else {
                Assert-CountdownNoteReminderProperties -Reminder $Rule -Expected @('id', 'type', 'frequency', 'interval', 'startDate', 'localTime', 'month', 'lastDay')
                if ($Rule.lastDay -isnot [bool] -or -not [bool] $Rule.lastDay) { throw '按年日历提醒最后一天必须显式为 true。' }
                $normalized.lastDay = $true
                $selectorKey = "month=$month|lastDay"
            }
        }
        default { throw '设置中包含不支持的日历提醒频率。' }
    }

    [pscustomobject]@{
        Rule = [pscustomobject] $normalized
        Schedule = "calendar|$frequency|$interval|$startDate|$localTime|$selectorKey"
    }
}

function Test-CountdownCalendarOccurrenceBoundary {
    param(
        [Parameter(Mandatory)] [object] $Rule,
        [Parameter(Mandatory)] [DateTimeOffset] $ScheduledUtc
    )

    $zone = [TimeZoneInfo]::FindSystemTimeZoneById('China Standard Time')
    $beijing = [TimeZoneInfo]::ConvertTime($ScheduledUtc.ToUniversalTime(), $zone)
    $localDate = [DateOnly]::FromDateTime($beijing.DateTime)
    $localTime = [TimeOnly]::FromDateTime($beijing.DateTime)
    $startDate = [DateOnly]::ParseExact(
        [string] $Rule.startDate,
        'yyyy-MM-dd',
        [System.Globalization.CultureInfo]::InvariantCulture
    )
    $expectedTime = [TimeOnly]::ParseExact(
        [string] $Rule.localTime,
        'HH:mm',
        [System.Globalization.CultureInfo]::InvariantCulture
    )
    if ($localDate -lt $startDate -or $localTime -ne $expectedTime) { return $false }

    $interval = [int] $Rule.interval
    switch ([string] $Rule.frequency) {
        'daily' {
            return (($localDate.DayNumber - $startDate.DayNumber) % $interval) -eq 0
        }
        'weekly' {
            $startWeek = $startDate.AddDays(-(([int] $startDate.DayOfWeek + 6) % 7))
            $localWeek = $localDate.AddDays(-(([int] $localDate.DayOfWeek + 6) % 7))
            $weekOffset = [int] (($localWeek.DayNumber - $startWeek.DayNumber) / 7)
            $weekday = $localDate.DayOfWeek.ToString().ToLowerInvariant()
            return ($weekOffset % $interval) -eq 0 -and $weekday -cin @($Rule.weekdays)
        }
        'monthly' {
            $monthOffset = (($localDate.Year - $startDate.Year) * 12) +
                ($localDate.Month - $startDate.Month)
            if (($monthOffset % $interval) -ne 0) { return $false }
            if ($null -ne $Rule.PSObject.Properties['lastDay']) {
                return $localDate.Day -eq [DateTime]::DaysInMonth($localDate.Year, $localDate.Month)
            }
            return $localDate.Day -eq [int] $Rule.day
        }
        'yearly' {
            if ((($localDate.Year - $startDate.Year) % $interval) -ne 0 -or
                $localDate.Month -ne [int] $Rule.month) {
                return $false
            }
            if ($null -ne $Rule.PSObject.Properties['lastDay']) {
                return $localDate.Day -eq [DateTime]::DaysInMonth($localDate.Year, $localDate.Month)
            }
            return $localDate.Day -eq [int] $Rule.day
        }
        default { return $false }
    }
}

function New-CountdownLegacyReminderRuleId {
    param(
        [Parameter(Mandatory)] [string] $SourceType,
        [Parameter(Mandatory)] [string] $RecordId,
        [Parameter(Mandatory)] [string] $LegacyBranch
    )

    $identity = "{0}`n{1}`n{2}" -f $SourceType.Trim().ToLowerInvariant(), $RecordId.Trim().ToUpperInvariant(), $LegacyBranch.Trim().ToLowerInvariant()
    $sha256 = [System.Security.Cryptography.SHA256]::Create()
    try {
        $hash = $sha256.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($identity))
    }
    finally {
        $sha256.Dispose()
    }
    'legacy-{0}' -f [System.BitConverter]::ToString($hash).Replace('-', '').ToLowerInvariant()
}

function ConvertTo-CountdownReminderRules {
    param(
        [Parameter(Mandatory)] [string] $Owner,
        [Parameter(Mandatory)] [string] $RecordId,
        [AllowNull()] [System.Collections.IEnumerable] $Rules,
        [AllowNull()] [object] $LegacyReminder,
        [switch] $UseRules,
        [switch] $AllowCountdownRelative
    )

    if (-not $UseRules) {
        if ($null -eq $LegacyReminder -or [string] $LegacyReminder.type -eq 'none') { return ,@() }
        $branch = switch ([string] $LegacyReminder.type) {
            'once' { 'once' }
            'repeat' { 'repeat' }
            default { throw '设置中包含无效的便签提醒类型。' }
        }
        $ruleId = New-CountdownLegacyReminderRuleId -SourceType $Owner -RecordId $RecordId -LegacyBranch $branch
        if ($branch -eq 'once') {
            return ,@([pscustomobject][ordered]@{
                id = $ruleId
                type = 'absoluteOnce'
                scheduledUtc = ConvertTo-CountdownNoteReminderUtc -Value $LegacyReminder.firstDueUtc -Subject '绝对一次提醒时间'
            })
        }
        return ,@([pscustomobject][ordered]@{
            id = $ruleId
            type = 'fixedInterval'
            firstDueUtc = ConvertTo-CountdownNoteReminderUtc -Value $LegacyReminder.firstDueUtc -Subject '固定间隔首次提醒时间'
            intervalMinutes = ConvertTo-CountdownNoteReminderIntervalMinutes -Minutes $LegacyReminder.intervalMinutes
        })
    }

    $result = [System.Collections.ArrayList]::new()
    $schedules = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
    foreach ($rule in @($Rules)) {
        if ($null -eq $rule) { throw "$Owner 包含无效的提醒规则。" }
        $id = ([string] $rule.id).Trim()
        if ([string]::IsNullOrWhiteSpace($id)) { throw '提醒规则 ID 不能为空。' }
        $type = [string] $rule.type
        switch ($type) {
            'absoluteOnce' {
                Assert-CountdownNoteReminderProperties -Reminder $rule -Expected @('id', 'type', 'scheduledUtc')
                $scheduledUtc = ConvertTo-CountdownNoteReminderUtc -Value $rule.scheduledUtc -Subject '绝对一次提醒时间'
                $schedule = 'absoluteOnce|{0}' -f $scheduledUtc.ToString('o', [System.Globalization.CultureInfo]::InvariantCulture)
                $normalized = [pscustomobject][ordered]@{ id = $id; type = 'absoluteOnce'; scheduledUtc = $scheduledUtc }
            }
            'fixedInterval' {
                Assert-CountdownNoteReminderProperties -Reminder $rule -Expected @('id', 'type', 'firstDueUtc', 'intervalMinutes')
                $firstDueUtc = ConvertTo-CountdownNoteReminderUtc -Value $rule.firstDueUtc -Subject '固定间隔首次提醒时间'
                $intervalMinutes = ConvertTo-CountdownNoteReminderIntervalMinutes -Minutes $rule.intervalMinutes
                $schedule = 'fixedInterval|{0}|{1}' -f $firstDueUtc.ToString('o', [System.Globalization.CultureInfo]::InvariantCulture), $intervalMinutes
                $normalized = [pscustomobject][ordered]@{ id = $id; type = 'fixedInterval'; firstDueUtc = $firstDueUtc; intervalMinutes = $intervalMinutes }
            }
            'countdownRelative' {
                if (-not $AllowCountdownRelative) {
                    throw '倒计时相对提醒规则只能属于用户倒计时。'
                }
                Assert-CountdownNoteReminderProperties -Reminder $rule -Expected @('id', 'type', 'offsetMinutesBefore')
                $offsetMinutesBefore = ConvertTo-CountdownRelativeReminderOffsetMinutes -Minutes $rule.offsetMinutesBefore
                $schedule = 'countdownRelative|{0}' -f $offsetMinutesBefore
                $normalized = [pscustomobject][ordered]@{ id = $id; type = 'countdownRelative'; offsetMinutesBefore = $offsetMinutesBefore }
            }
            'calendar' {
                $calendar = ConvertTo-CountdownCalendarRule -Rule $rule -Id $id
                $schedule = $calendar.Schedule
                $normalized = $calendar.Rule
            }
            default { throw '设置中包含当前 schema 不支持的提醒规则类型。' }
        }
        if (-not $schedules.Add($schedule)) { throw "$Owner 包含重复的规范提醒日程。" }
        [void] $result.Add($normalized)
    }
    if ($result.Count -gt 8) { throw "$Owner 最多只能包含 8 条提醒规则。" }
    return ,$result
}

function Get-CountdownOccurrenceKey {
    param(
        [Parameter(Mandatory)] [string] $RuleId,
        [Parameter(Mandatory)] [DateTimeOffset] $ScheduledUtc
    )

    '{0}|{1}' -f $RuleId, $ScheduledUtc.ToUniversalTime().ToString('o', [System.Globalization.CultureInfo]::InvariantCulture)
}

function ConvertFrom-CountdownOccurrenceKey {
    param([Parameter(Mandatory)] [string] $Value)

    $separator = $Value.LastIndexOf('|')
    $scheduledUtc = [DateTimeOffset]::MinValue
    if (
        $separator -le 0 -or
        -not [DateTimeOffset]::TryParseExact(
            $Value.Substring($separator + 1),
            'o',
            [System.Globalization.CultureInfo]::InvariantCulture,
            [System.Globalization.DateTimeStyles]::None,
            [ref] $scheduledUtc
        ) -or
        $scheduledUtc.Offset -ne [TimeSpan]::Zero
    ) {
        throw '提醒 occurrence 与规则游标互相矛盾。'
    }

    [pscustomobject]@{
        RuleId = $Value.Substring(0, $separator)
        ScheduledUtc = $scheduledUtc.ToUniversalTime()
    }
}

function Get-CountdownReminderFirstScheduledUtc {
    param([Parameter(Mandatory)] [object] $Rule)

    switch ([string] $Rule.type) {
        'absoluteOnce' { return ([DateTimeOffset] $Rule.scheduledUtc).ToUniversalTime() }
        'fixedInterval' { return ([DateTimeOffset] $Rule.firstDueUtc).ToUniversalTime() }
        'countdownRelative' { return $null }
        'calendar' { return $null }
        default { throw '设置中包含当前 schema 不支持的提醒规则类型。' }
    }
}

function Test-CountdownOccurrenceMatchesRule {
    param(
        [Parameter(Mandatory)] [object] $Rule,
        [Parameter(Mandatory)] [DateTimeOffset] $ScheduledUtc
    )

    $scheduled = $ScheduledUtc.ToUniversalTime()
    switch ([string] $Rule.type) {
        'absoluteOnce' { return $scheduled -eq ([DateTimeOffset] $Rule.scheduledUtc).ToUniversalTime() }
        'fixedInterval' {
            $firstDueUtc = ([DateTimeOffset] $Rule.firstDueUtc).ToUniversalTime()
            return $scheduled -ge $firstDueUtc -and
                (($scheduled.Ticks - $firstDueUtc.Ticks) % [TimeSpan]::FromMinutes([int] $Rule.intervalMinutes).Ticks) -eq 0
        }
        'countdownRelative' { return $true }
        'calendar' { return Test-CountdownCalendarOccurrenceBoundary -Rule $Rule -ScheduledUtc $scheduled }
        default { return $false }
    }
}

function Get-CountdownLegacyReminderFromRules {
    param(
        [Parameter(Mandatory)] [System.Collections.IEnumerable] $Rules,
        [AllowNull()] [System.Collections.IDictionary] $ReminderStates
    )

    $items = @($Rules)
    if ($items.Count -eq 0) { return [pscustomobject][ordered]@{ type = 'none' } }
    $rule = $items[0]
    if ($rule.type -eq 'absoluteOnce') {
        $delivery = 'pending'
        if ($null -ne $ReminderStates -and $ReminderStates.Contains([string] $rule.id)) {
            $state = $ReminderStates[[string] $rule.id]
            $expectedKey = Get-CountdownOccurrenceKey -RuleId ([string] $rule.id) -ScheduledUtc ([DateTimeOffset] $rule.scheduledUtc)
            if ([string] $state.lastDeliveredOccurrenceKey -ceq $expectedKey) { $delivery = 'delivered' }
        }
        return [pscustomobject][ordered]@{ type = 'once'; firstDueUtc = $rule.scheduledUtc; delivery = $delivery }
    }
    if ($rule.type -eq 'fixedInterval') {
        return [pscustomobject][ordered]@{ type = 'repeat'; firstDueUtc = $rule.firstDueUtc; intervalMinutes = [int] $rule.intervalMinutes }
    }
    if ($rule.type -eq 'calendar') {
        return [pscustomobject][ordered]@{ type = 'none' }
    }
    throw '当前兼容 oracle 不支持该提醒规则。'
}

function ConvertTo-CountdownSerializedReminderRule {
    param([Parameter(Mandatory)] [object] $Rule)

    switch ([string] $Rule.type) {
        'absoluteOnce' {
            return [ordered]@{
                id = [string] $Rule.id
                type = 'absoluteOnce'
                scheduledUtc = ([DateTimeOffset] $Rule.scheduledUtc).ToUniversalTime().ToString('o', [System.Globalization.CultureInfo]::InvariantCulture)
            }
        }
        'fixedInterval' {
            return [ordered]@{
                id = [string] $Rule.id
                type = 'fixedInterval'
                firstDueUtc = ([DateTimeOffset] $Rule.firstDueUtc).ToUniversalTime().ToString('o', [System.Globalization.CultureInfo]::InvariantCulture)
                intervalMinutes = [int] $Rule.intervalMinutes
            }
        }
        'countdownRelative' {
            return [ordered]@{
                id = [string] $Rule.id
                type = 'countdownRelative'
                offsetMinutesBefore = [int] $Rule.offsetMinutesBefore
            }
        }
        'calendar' {
            $serialized = [ordered]@{
                id = [string] $Rule.id
                type = 'calendar'
                frequency = [string] $Rule.frequency
                interval = [int] $Rule.interval
                startDate = [string] $Rule.startDate
                localTime = [string] $Rule.localTime
            }
            switch ([string] $Rule.frequency) {
                'daily' { }
                'weekly' { $serialized.weekdays = @($Rule.weekdays | ForEach-Object { [string] $_ }) }
                'monthly' {
                    if ($null -ne $Rule.PSObject.Properties['lastDay']) { $serialized.lastDay = $true }
                    else { $serialized.day = [int] $Rule.day }
                }
                'yearly' {
                    $serialized.month = [int] $Rule.month
                    if ($null -ne $Rule.PSObject.Properties['lastDay']) { $serialized.lastDay = $true }
                    else { $serialized.day = [int] $Rule.day }
                }
                default { throw '设置中包含不支持的日历提醒频率。' }
            }
            return $serialized
        }
        default { throw '设置中包含当前 schema 不支持的提醒规则类型。' }
    }
}

function New-CountdownRecordFromSettings {
    param(
        [Parameter(Mandatory)] [string] $Id,
        [Parameter(Mandatory)] [string] $Name,
        [Parameter(Mandatory)] [DateTimeOffset] $TargetUtc,
        [AllowNull()] [System.Collections.IEnumerable] $TagIds,
        [AllowNull()] [System.Collections.IEnumerable] $ReminderRules
    )

    $tagIdCopy = [System.Collections.ArrayList]::new()
    foreach ($tagId in $TagIds) {
        [void] $tagIdCopy.Add(([string] $tagId).Trim())
    }

    [pscustomobject]@{
        id = $Id
        name = $Name
        targetUtc = $TargetUtc.ToUniversalTime()
        tagIds = $tagIdCopy
        reminderRules = @($ReminderRules | Where-Object { $null -ne $_ })
    }
}

function ConvertFrom-CountdownSettingsObject {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [object] $Settings,
        [DateTimeOffset] $MigrationUtc = [DateTimeOffset]::UtcNow
    )

    $records = [System.Collections.ArrayList]::new()
    $notes = [System.Collections.ArrayList]::new()
    $tags = [System.Collections.ArrayList]::new()
    $preferences = New-CountdownPreferences
    $reminderStates = [ordered]@{}
    $globalRuleIds = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    $rulesById = [ordered]@{}

    if ($Settings.timeZoneId -ne 'China Standard Time') { throw '设置文件时区无效。' }
    if ($Settings.schemaVersion -eq 1) {
        [void] $records.Add((New-CountdownRecordFromSettings -Id ([guid]::NewGuid().ToString('N')) -Name '我的倒计时' -TargetUtc (ConvertTo-CountdownSettingsTargetUtc -Value $Settings.targetUtc)))
        return [pscustomobject]@{ Records = $records; Notes = $notes; Tags = $tags; Preferences = $preferences; ReminderStates = $reminderStates; Migrated = $true }
    }
    if ($Settings.schemaVersion -notin @(2, 3, 4, 5, 6, 7, 8, 9, 10)) { throw '设置文件版本无效。' }

    if ($Settings.schemaVersion -ge 5) {
        if ($null -eq $Settings.PSObject.Properties['tags'] -or $null -eq $Settings.tags) { throw '设置中缺少标签库。' }
        $tagIds = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
        $tagNames = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
        foreach ($item in @($Settings.tags)) {
            $color = if ($null -eq $item.PSObject.Properties['color']) { $null } else { [string] $item.color }
            $tag = New-CountdownTagRecord -Id ([string] $item.id) -Name ([string] $item.name) -Color $color
            if (-not $tagIds.Add($tag.id)) { throw '设置中包含重复的标签 ID。' }
            if (-not $tagNames.Add($tag.name)) { throw '设置中包含重复的标签名称。' }
            [void] $tags.Add($tag)
        }
    }

    $knownTagIds = @($tags | ForEach-Object { $_.id })

    $names = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::OrdinalIgnoreCase)
    $ids = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($item in @($Settings.countdowns)) {
        $id = ([string] $item.id).Trim()
        $name = ([string] $item.name).Trim()
        if ([string]::IsNullOrWhiteSpace($name) -or $name.Length -gt 30) { throw '设置中包含无效的倒计时名称。' }
        if ([string]::IsNullOrWhiteSpace($id)) { throw '设置中包含空的倒计时 ID。' }
        if (-not $names.Add($name)) { throw '设置中包含重复的倒计时名称。' }
        if (-not $ids.Add($id)) { throw '设置中包含重复的倒计时 ID。' }
        $recordTagIds = @()
        if ($Settings.schemaVersion -ge 5) {
            if ($null -eq $item.PSObject.Properties['tagIds'] -or $null -eq $item.tagIds) { throw '设置中的倒计时缺少标签 ID 列表。' }
            $recordTagIds = ConvertTo-CountdownTagIds -TagIds $item.tagIds -KnownTagIds $knownTagIds
        }
        $recordRules = if ($Settings.schemaVersion -ge 10) {
            if ($null -eq $item.PSObject.Properties['reminderRules']) { throw '设置中的倒计时缺少提醒规则列表。' }
            ConvertTo-CountdownReminderRules -Owner '倒计时' -RecordId $id -Rules $item.reminderRules -UseRules -AllowCountdownRelative
        }
        else { @() }
        foreach ($rule in @($recordRules)) {
            if (-not $globalRuleIds.Add([string] $rule.id)) { throw '设置中包含重复的全局提醒规则 ID。' }
            $rulesById[[string] $rule.id] = $rule
        }
        [void] $records.Add((New-CountdownRecordFromSettings -Id $id -Name $name -TargetUtc (ConvertTo-CountdownSettingsTargetUtc -Value $item.targetUtc) -TagIds $recordTagIds -ReminderRules $recordRules))
    }

    if ($Settings.schemaVersion -ge 3) {
        $precision = [string] $Settings.displayPrecision
        if ($precision -notin @('Day', 'Hour', 'Minute', 'Second')) { throw '设置中包含无效的悬浮窗显示精度。' }
        [decimal] $salary = 0
        if (-not [decimal]::TryParse(([string] $Settings.monthlySalary), [System.Globalization.NumberStyles]::Number, [System.Globalization.CultureInfo]::InvariantCulture, [ref] $salary)) {
            throw '设置中包含无效的月薪。'
        }
        $themePreference = 'Auto'
        if ($Settings.schemaVersion -ge 9) {
            if ($null -eq $Settings.PSObject.Properties['themePreference']) {
                throw '设置中缺少主题偏好。'
            }
            $themePreference = [string] $Settings.themePreference
        }
        $preferences = New-CountdownPreferences -Precision $precision -MonthlySalary $salary -WorkStartText ([string] $Settings.workStart) -WorkEndText ([string] $Settings.workEnd) -ThemePreference $themePreference
    }

    if ($Settings.schemaVersion -ge 4) {
        if ($null -eq $Settings.PSObject.Properties['notes']) { throw '设置中缺少便签列表。' }
        $noteIds = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::OrdinalIgnoreCase)
        foreach ($item in @($Settings.notes)) {
            $id = ([string] $item.id).Trim()
            if ($null -eq $item.PSObject.Properties['isCompleted'] -or $item.isCompleted -isnot [bool]) { throw '设置中包含无效的便签完成状态。' }
            if (-not $noteIds.Add($id)) { throw '设置中包含重复的便签 ID。' }
            $noteTagIds = @()
            if ($Settings.schemaVersion -ge 5) {
                if ($null -eq $item.PSObject.Properties['tagIds'] -or $null -eq $item.tagIds) { throw '设置中的便签缺少标签 ID 列表。' }
                $noteTagIds = ConvertTo-CountdownTagIds -TagIds $item.tagIds -KnownTagIds $knownTagIds
            }
            $noteDate = $null
            if ($Settings.schemaVersion -ge 6) {
                if ($null -eq $item.PSObject.Properties['date']) { throw '设置中的便签缺少日期字段。' }
                $noteDate = $item.date
            }
            if ($Settings.schemaVersion -ge 10) {
                if ($null -eq $item.PSObject.Properties['reminderRules']) { throw '设置中的便签缺少提醒规则列表。' }
                $noteRules = ConvertTo-CountdownReminderRules -Owner '便签' -RecordId $id -Rules $item.reminderRules -UseRules
                foreach ($rule in @($noteRules)) {
                    if (-not $globalRuleIds.Add([string] $rule.id)) { throw '设置中包含重复的全局提醒规则 ID。' }
                    $rulesById[[string] $rule.id] = $rule
                }
                $legacyReminder = Get-CountdownLegacyReminderFromRules -Rules $noteRules -ReminderStates $null
                $note = New-CountdownNoteRecord -Id $id -Text ([string] $item.text) -Date $noteDate -IsCompleted ([bool] $item.isCompleted) -TagIds $noteTagIds -Reminder $legacyReminder
                $note | Add-Member -NotePropertyName reminderRules -NotePropertyValue $noteRules -Force
                [void] $notes.Add($note)
                continue
            }
            if ($Settings.schemaVersion -ge 8) {
                if ($null -eq $item.PSObject.Properties['reminder']) {
                    throw '设置中的便签缺少提醒字段。'
                }
                $note = New-CountdownNoteRecord -Id $id -Text ([string] $item.text) -Date $noteDate -IsCompleted ([bool] $item.isCompleted) -TagIds $noteTagIds -Reminder $item.reminder
                $noteRules = ConvertTo-CountdownReminderRules -Owner 'note' -RecordId $id -LegacyReminder $note.reminder
                foreach ($rule in @($noteRules)) {
                    if (-not $globalRuleIds.Add([string] $rule.id)) { throw '设置中包含重复的全局提醒规则 ID。' }
                    $rulesById[[string] $rule.id] = $rule
                }
                $note | Add-Member -NotePropertyName reminderRules -NotePropertyValue $noteRules -Force
                if ($note.reminder.type -eq 'once' -and $note.reminder.delivery -eq 'delivered') {
                    $rule = @($noteRules)[0]
                    $scheduledUtc = ([DateTimeOffset] $rule.scheduledUtc).ToUniversalTime()
                    $reminderStates[[string] $rule.id] = [pscustomobject][ordered]@{
                        handledThroughUtc = $scheduledUtc
                        lastDeliveredOccurrenceKey = Get-CountdownOccurrenceKey -RuleId ([string] $rule.id) -ScheduledUtc $scheduledUtc
                        lastDispositionOccurrenceKey = $null
                        activeSnooze = $null
                    }
                }
                elseif ($note.reminder.type -eq 'repeat') {
                    $rule = @($noteRules)[0]
                    $firstDueUtc = ([DateTimeOffset] $rule.firstDueUtc).ToUniversalTime()
                    if ($MigrationUtc.ToUniversalTime() -ge $firstDueUtc) {
                        $intervalTicks = [TimeSpan]::FromMinutes([int] $rule.intervalMinutes).Ticks
                        $cycles = [long] [Math]::Floor(($MigrationUtc.ToUniversalTime().Ticks - $firstDueUtc.Ticks) / $intervalTicks)
                        $reminderStates[[string] $rule.id] = [pscustomobject][ordered]@{
                            handledThroughUtc = $firstDueUtc.AddTicks($cycles * $intervalTicks)
                            lastDeliveredOccurrenceKey = $null
                            lastDispositionOccurrenceKey = $null
                            activeSnooze = $null
                        }
                    }
                }
                [void] $notes.Add($note)
                continue
            }

            $reminderIntervalMinutes = $null
            $reminderStartUtc = $null
            if ($Settings.schemaVersion -eq 7) {
                if (
                    $null -eq $item.PSObject.Properties['reminderIntervalMinutes'] -or
                    $null -eq $item.PSObject.Properties['reminderStartUtc']
                ) {
                    throw '设置中的便签缺少提醒字段。'
                }
                $reminderIntervalMinutes = $item.reminderIntervalMinutes
                $reminderStartValue = $item.reminderStartUtc
                if (($null -eq $reminderIntervalMinutes) -ne ($null -eq $reminderStartValue)) {
                    throw '设置中的便签提醒间隔和开始时间必须同时设置或同时关闭。'
                }
                if ($null -ne $reminderIntervalMinutes) {
                    $typeCode = [System.Type]::GetTypeCode($reminderIntervalMinutes.GetType())
                    if ($typeCode -notin @(
                            [System.TypeCode]::SByte,
                            [System.TypeCode]::Byte,
                            [System.TypeCode]::Int16,
                            [System.TypeCode]::UInt16,
                            [System.TypeCode]::Int32,
                            [System.TypeCode]::UInt32,
                            [System.TypeCode]::Int64,
                            [System.TypeCode]::UInt64
                        )) {
                        throw '设置中的便签提醒间隔必须是整数。'
                    }
                    if ([long] $reminderIntervalMinutes -lt 1 -or [long] $reminderIntervalMinutes -gt 1440) {
                        throw '设置中的便签提醒间隔必须在 1 到 1440 分钟之间。'
                    }
                    $parsedReminderStart = [DateTimeOffset]::MinValue
                    if ($reminderStartValue -is [DateTimeOffset]) {
                        $parsedReminderStart = [DateTimeOffset] $reminderStartValue
                    }
                    elseif ($reminderStartValue -is [DateTime] -and ([DateTime] $reminderStartValue).Kind -ne [DateTimeKind]::Unspecified) {
                        $parsedReminderStart = [DateTimeOffset] ([DateTime] $reminderStartValue)
                    }
                    elseif (
                        $reminderStartValue -isnot [string] -or
                        -not [DateTimeOffset]::TryParseExact(
                            [string] $reminderStartValue,
                            'o',
                            [System.Globalization.CultureInfo]::InvariantCulture,
                            [System.Globalization.DateTimeStyles]::None,
                            [ref] $parsedReminderStart
                        )
                    ) {
                        throw '设置中的便签提醒开始时间必须是带时区的有效时间。'
                    }
                    $reminderStartUtc = $parsedReminderStart.ToUniversalTime()
                }
            }
            $note = New-CountdownNoteRecord -Id $id -Text ([string] $item.text) -Date $noteDate -IsCompleted ([bool] $item.isCompleted) -TagIds $noteTagIds -ReminderIntervalMinutes $reminderIntervalMinutes -ReminderStartUtc $reminderStartUtc
            $noteRules = ConvertTo-CountdownReminderRules -Owner 'note' -RecordId $id -LegacyReminder $note.reminder
            foreach ($rule in @($noteRules)) {
                if (-not $globalRuleIds.Add([string] $rule.id)) { throw '设置中包含重复的全局提醒规则 ID。' }
                $rulesById[[string] $rule.id] = $rule
            }
            $note | Add-Member -NotePropertyName reminderRules -NotePropertyValue $noteRules -Force
            if ($note.reminder.type -eq 'repeat') {
                $rule = @($noteRules)[0]
                $firstDueUtc = ([DateTimeOffset] $rule.firstDueUtc).ToUniversalTime()
                if ($MigrationUtc.ToUniversalTime() -ge $firstDueUtc) {
                    $intervalTicks = [TimeSpan]::FromMinutes([int] $rule.intervalMinutes).Ticks
                    $cycles = [long] [Math]::Floor(($MigrationUtc.ToUniversalTime().Ticks - $firstDueUtc.Ticks) / $intervalTicks)
                    $reminderStates[[string] $rule.id] = [pscustomobject][ordered]@{
                        handledThroughUtc = $firstDueUtc.AddTicks($cycles * $intervalTicks)
                        lastDeliveredOccurrenceKey = $null
                        lastDispositionOccurrenceKey = $null
                        activeSnooze = $null
                    }
                }
            }
            [void] $notes.Add($note)
        }
    }

    if ($Settings.schemaVersion -ge 10) {
        if ($null -eq $Settings.PSObject.Properties['reminderStates'] -or $null -eq $Settings.reminderStates) { throw '设置中缺少提醒规则状态。' }
        foreach ($property in @($Settings.reminderStates.PSObject.Properties)) {
            $ruleId = [string] $property.Name
            if (-not $globalRuleIds.Contains($ruleId)) { throw '设置中包含悬空的提醒规则状态。' }
            $hasDisposition = $null -ne $property.Value.PSObject.Properties['lastDispositionOccurrenceKey']
            $hasAcknowledgement = $null -ne $property.Value.PSObject.Properties['lastAcknowledgedOccurrenceKey']
            if ($hasDisposition -eq $hasAcknowledgement) { throw '提醒规则状态必须且只能包含一种处置 occurrence key 字段。' }
            if ($hasDisposition) {
                Assert-CountdownNoteReminderProperties -Reminder $property.Value -Expected @('handledThroughUtc', 'lastDeliveredOccurrenceKey', 'lastDispositionOccurrenceKey', 'activeSnooze')
            }
            else {
                Assert-CountdownNoteReminderProperties -Reminder $property.Value -Expected @('handledThroughUtc', 'lastDeliveredOccurrenceKey', 'lastAcknowledgedOccurrenceKey')
            }
            $handledThroughUtc = if ($null -eq $property.Value.handledThroughUtc) { $null } else { ConvertTo-CountdownNoteReminderUtc -Value $property.Value.handledThroughUtc -Subject '提醒规则游标' }
            $lastDeliveredOccurrenceKey = if ($null -eq $property.Value.lastDeliveredOccurrenceKey) { $null } else { [string] $property.Value.lastDeliveredOccurrenceKey }
            $lastDispositionOccurrenceKey = if ($hasDisposition) {
                if ($null -eq $property.Value.lastDispositionOccurrenceKey) { $null } else { [string] $property.Value.lastDispositionOccurrenceKey }
            }
            else {
                if ($null -eq $property.Value.lastAcknowledgedOccurrenceKey) { $null } else { [string] $property.Value.lastAcknowledgedOccurrenceKey }
            }
            $sourceActiveSnooze = if ($hasDisposition) { $property.Value.activeSnooze } else { $null }
            if ($null -eq $handledThroughUtc -and [string]::IsNullOrWhiteSpace($lastDeliveredOccurrenceKey) -and [string]::IsNullOrWhiteSpace($lastDispositionOccurrenceKey) -and $null -eq $sourceActiveSnooze) { throw '空提醒状态不得持久化。' }
            $rule = $rulesById[$ruleId]
            $firstScheduledUtc = Get-CountdownReminderFirstScheduledUtc -Rule $rule
            if ($null -ne $handledThroughUtc -and $null -ne $firstScheduledUtc -and $handledThroughUtc -lt $firstScheduledUtc) { throw '提醒规则游标不能早于首次计划时刻。' }
            if ($null -ne $handledThroughUtc) {
                $validCursor = Test-CountdownOccurrenceMatchesRule -Rule $rule -ScheduledUtc $handledThroughUtc
                if (-not $validCursor) { throw '提醒规则游标必须落在规则的计划 occurrence 上。' }
            }
            $lastDeliveredScheduledUtc = $null
            if (-not [string]::IsNullOrWhiteSpace($lastDeliveredOccurrenceKey)) {
                $occurrence = ConvertFrom-CountdownOccurrenceKey -Value $lastDeliveredOccurrenceKey
                $occurrenceRuleId = $occurrence.RuleId
                $occurrenceUtc = $occurrence.ScheduledUtc
                if ($occurrenceRuleId -ine $ruleId -or $null -eq $handledThroughUtc -or $occurrenceUtc -gt $handledThroughUtc) { throw '提醒 occurrence 与规则游标互相矛盾。' }
                $validOccurrence = Test-CountdownOccurrenceMatchesRule -Rule $rule -ScheduledUtc $occurrenceUtc
                if (-not $validOccurrence) { throw '提醒 occurrence 与规则游标互相矛盾。' }
                $lastDeliveredScheduledUtc = $occurrenceUtc
                $lastDeliveredOccurrenceKey = Get-CountdownOccurrenceKey -RuleId $ruleId -ScheduledUtc $occurrenceUtc
            }
            $lastDispositionScheduledUtc = $null
            if (-not [string]::IsNullOrWhiteSpace($lastDispositionOccurrenceKey)) {
                if ([string]::IsNullOrWhiteSpace($lastDeliveredOccurrenceKey)) { throw '提醒处置 occurrence 缺少送达事实。' }
                $occurrence = ConvertFrom-CountdownOccurrenceKey -Value $lastDispositionOccurrenceKey
                $occurrenceRuleId = $occurrence.RuleId
                $occurrenceUtc = $occurrence.ScheduledUtc
                if ($occurrenceRuleId -ine $ruleId -or
                    $null -eq $handledThroughUtc -or
                    $occurrenceUtc -gt $handledThroughUtc -or
                    $null -eq $lastDeliveredScheduledUtc -or
                    $occurrenceUtc -gt $lastDeliveredScheduledUtc) { throw '提醒处置 occurrence 与规则游标互相矛盾。' }
                $validOccurrence = Test-CountdownOccurrenceMatchesRule -Rule $rule -ScheduledUtc $occurrenceUtc
                if (-not $validOccurrence) { throw '提醒处置 occurrence 与规则游标互相矛盾。' }
                $lastDispositionScheduledUtc = $occurrenceUtc
                $lastDispositionOccurrenceKey = Get-CountdownOccurrenceKey -RuleId $ruleId -ScheduledUtc $occurrenceUtc
            }
            $activeSnooze = $null
            if ($null -ne $sourceActiveSnooze) {
                Assert-CountdownNoteReminderProperties -Reminder $sourceActiveSnooze -Expected @('occurrenceKey', 'originalScheduledUtc', 'snoozeUntilUtc')
                $snoozeOccurrence = ConvertFrom-CountdownOccurrenceKey -Value ([string] $sourceActiveSnooze.occurrenceKey)
                $originalScheduledUtc = ConvertTo-CountdownNoteReminderUtc -Value $sourceActiveSnooze.originalScheduledUtc -Subject '稍后原计划时刻'
                $snoozeUntilUtc = ConvertTo-CountdownNoteReminderUtc -Value $sourceActiveSnooze.snoozeUntilUtc -Subject '稍后覆盖时刻'
                $canonicalSnoozeKey = Get-CountdownOccurrenceKey -RuleId $ruleId -ScheduledUtc $originalScheduledUtc
                if ($snoozeOccurrence.RuleId -ine $ruleId -or
                    $snoozeOccurrence.ScheduledUtc -ne $originalScheduledUtc -or
                    $null -eq $handledThroughUtc -or
                    $originalScheduledUtc -gt $handledThroughUtc -or
                    $lastDeliveredOccurrenceKey -cne $canonicalSnoozeKey -or
                    $snoozeUntilUtc -le $originalScheduledUtc -or
                    ($null -ne $lastDispositionScheduledUtc -and $lastDispositionScheduledUtc -ge $originalScheduledUtc)) {
                    throw '活动稍后提醒与规则 occurrence 状态互相矛盾。'
                }
                $activeSnooze = [pscustomobject][ordered]@{
                    occurrenceKey = $canonicalSnoozeKey
                    originalScheduledUtc = $originalScheduledUtc
                    snoozeUntilUtc = $snoozeUntilUtc
                }
            }
            $reminderStates[$ruleId] = [pscustomobject][ordered]@{
                handledThroughUtc = $handledThroughUtc
                lastDeliveredOccurrenceKey = $lastDeliveredOccurrenceKey
                lastDispositionOccurrenceKey = $lastDispositionOccurrenceKey
                activeSnooze = $activeSnooze
            }
        }
        foreach ($note in $notes) {
            $note.reminder = Get-CountdownLegacyReminderFromRules -Rules $note.reminderRules -ReminderStates $reminderStates
        }
    }

    [pscustomobject]@{ Records = $records; Notes = $notes; Tags = $tags; Preferences = $preferences; ReminderStates = $reminderStates; Migrated = $Settings.schemaVersion -ne 10 }
}

function ConvertTo-CountdownSettingsJson {
    [CmdletBinding()]
    param([Parameter(Mandatory)] [object] $State)

    $themePreference = if ($null -eq $State.Preferences.PSObject.Properties['ThemePreference']) {
        'Auto'
    }
    else {
        [string] $State.Preferences.ThemePreference
    }
    $preferences = New-CountdownPreferences -Precision ([string] $State.Preferences.Precision) -MonthlySalary ([decimal] $State.Preferences.MonthlySalary) -WorkStartText (Format-CountdownWorkTime -Value ([TimeSpan] $State.Preferences.WorkStart)) -WorkEndText (Format-CountdownWorkTime -Value ([TimeSpan] $State.Preferences.WorkEnd)) -ThemePreference $themePreference
    $canonicalMonthlySalary = if (($preferences.MonthlySalary % [decimal] 1) -eq 0) {
        [System.Numerics.BigInteger]::Parse(
            $preferences.MonthlySalary.ToString('0', [System.Globalization.CultureInfo]::InvariantCulture),
            [System.Globalization.CultureInfo]::InvariantCulture)
    }
    else {
        $preferences.MonthlySalary
    }

    if ($null -eq $State.PSObject.Properties['Tags']) { throw '设置状态缺少标签库。' }
    $tagIds = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    $tagNames = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    $validTags = [System.Collections.ArrayList]::new()
    foreach ($tag in @($State.Tags)) {
        $color = if ($null -eq $tag.PSObject.Properties['color']) { $null } else { [string] $tag.color }
        $validTag = New-CountdownTagRecord -Id ([string] $tag.id) -Name ([string] $tag.name) -Color $color
        if (-not $tagIds.Add($validTag.id)) { throw '设置中包含重复的标签 ID。' }
        if (-not $tagNames.Add($validTag.name)) { throw '设置中包含重复的标签名称。' }
        [void] $validTags.Add($validTag)
    }
    $knownTagIds = @($validTags | ForEach-Object { $_.id })
    $tags = @()
    foreach ($tag in $validTags) {
        $serializedTag = [ordered]@{ id = $tag.id; name = $tag.name }
        if ($null -ne $tag.color) { $serializedTag.color = $tag.color }
        $tags += $serializedTag
    }

    $recordIds = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::OrdinalIgnoreCase)
    $recordNames = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::OrdinalIgnoreCase)
    $globalRuleIds = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    $rulesById = [ordered]@{}
    $records = @()
    foreach ($record in @($State.Records)) {
        $id = ([string] $record.id).Trim()
        $name = ([string] $record.name).Trim()
        if ([string]::IsNullOrWhiteSpace($id) -or [string]::IsNullOrWhiteSpace($name) -or $name.Length -gt 30) { throw '设置中包含无效的倒计时记录。' }
        if (-not $recordIds.Add($id) -or -not $recordNames.Add($name)) { throw '设置中包含重复的倒计时记录。' }
        if ($null -eq $record.PSObject.Properties['tagIds'] -or $null -eq $record.tagIds) { throw '设置中的倒计时缺少标签 ID 列表。' }
        $recordTagIds = ConvertTo-CountdownTagIds -TagIds $record.tagIds -KnownTagIds $knownTagIds
        $recordRules = if ($null -eq $record.PSObject.Properties['reminderRules']) { @() } else {
            ConvertTo-CountdownReminderRules -Owner '倒计时' -RecordId $id -Rules $record.reminderRules -UseRules -AllowCountdownRelative
        }
        foreach ($rule in @($recordRules)) {
            if (-not $globalRuleIds.Add([string] $rule.id)) { throw '设置中包含重复的全局提醒规则 ID。' }
            $rulesById[[string] $rule.id] = $rule
        }
        $serializedRules = @(
            foreach ($rule in @($recordRules)) {
                ConvertTo-CountdownSerializedReminderRule -Rule $rule
            }
        )
        $records += [ordered]@{
            id = $id
            name = $name
            targetUtc = ([DateTimeOffset] $record.targetUtc).ToUniversalTime().ToString('o')
            tagIds = @($recordTagIds | ForEach-Object { [string] $_ })
            reminderRules = $serializedRules
        }
    }

    $noteIds = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::OrdinalIgnoreCase)
    $notes = @()
    foreach ($note in @($State.Notes)) {
        if ($null -eq $note.PSObject.Properties['tagIds'] -or $null -eq $note.tagIds) { throw '设置中的便签缺少标签 ID 列表。' }
        $noteTagIds = ConvertTo-CountdownTagIds -TagIds $note.tagIds -KnownTagIds $knownTagIds
        $noteDate = if ($null -eq $note.PSObject.Properties['date']) { $null } else { $note.date }
        $validNote = if ($null -ne $note.PSObject.Properties['reminder']) {
            New-CountdownNoteRecord -Id ([string] $note.id) -Text ([string] $note.text) -Date $noteDate -IsCompleted ([bool] $note.isCompleted) -TagIds $noteTagIds -Reminder $note.reminder
        }
        else {
            $reminderIntervalMinutes = if ($null -eq $note.PSObject.Properties['reminderIntervalMinutes']) { $null } else { $note.reminderIntervalMinutes }
            $reminderStartUtc = if ($null -eq $note.PSObject.Properties['reminderStartUtc']) { $null } else { $note.reminderStartUtc }
            New-CountdownNoteRecord -Id ([string] $note.id) -Text ([string] $note.text) -Date $noteDate -IsCompleted ([bool] $note.isCompleted) -TagIds $noteTagIds -ReminderIntervalMinutes $reminderIntervalMinutes -ReminderStartUtc $reminderStartUtc
        }
        if (-not $noteIds.Add($validNote.id)) { throw '设置中包含重复的便签 ID。' }
        $noteRules = if ($null -ne $note.PSObject.Properties['reminderRules']) {
            ConvertTo-CountdownReminderRules -Owner '便签' -RecordId $validNote.id -Rules $note.reminderRules -UseRules
        }
        else {
            ConvertTo-CountdownReminderRules -Owner 'note' -RecordId $validNote.id -LegacyReminder $validNote.reminder
        }
        foreach ($rule in @($noteRules)) {
            if (-not $globalRuleIds.Add([string] $rule.id)) { throw '设置中包含重复的全局提醒规则 ID。' }
            $rulesById[[string] $rule.id] = $rule
        }
        $serializedRules = @(
            foreach ($rule in @($noteRules)) {
                ConvertTo-CountdownSerializedReminderRule -Rule $rule
            }
        )
        $notes += [ordered]@{
            id = $validNote.id
            text = $validNote.text
            date = $validNote.date
            isCompleted = [bool] $validNote.isCompleted
            tagIds = @($validNote.tagIds | ForEach-Object { [string] $_ })
            reminderRules = $serializedRules
        }
    }

    $serializedReminderStates = [ordered]@{}
    $sourceReminderStates = if ($null -eq $State.PSObject.Properties['ReminderStates']) { [ordered]@{} } else { $State.ReminderStates }
    if ($sourceReminderStates -isnot [System.Collections.IDictionary]) { throw '提醒状态必须是按规则 ID 索引的对象。' }
    foreach ($entry in $sourceReminderStates.GetEnumerator()) {
        $ruleId = ([string] $entry.Key).Trim()
        if (-not $rulesById.Contains($ruleId)) { throw '设置中包含悬空的提醒规则状态。' }
        $state = $entry.Value
        $handledThroughUtc = if ($null -eq $state.handledThroughUtc) { $null } else { ConvertTo-CountdownNoteReminderUtc -Value $state.handledThroughUtc -Subject '提醒规则游标' }
        $lastDeliveredOccurrenceKey = if ($null -eq $state.lastDeliveredOccurrenceKey) { $null } else { [string] $state.lastDeliveredOccurrenceKey }
        $lastDispositionOccurrenceKey = if ($null -ne $state.PSObject.Properties['lastDispositionOccurrenceKey']) {
            if ($null -eq $state.lastDispositionOccurrenceKey) { $null } else { [string] $state.lastDispositionOccurrenceKey }
        }
        elseif ($null -ne $state.PSObject.Properties['lastAcknowledgedOccurrenceKey']) {
            if ($null -eq $state.lastAcknowledgedOccurrenceKey) { $null } else { [string] $state.lastAcknowledgedOccurrenceKey }
        }
        else { $null }
        $sourceActiveSnooze = if ($null -eq $state.PSObject.Properties['activeSnooze']) { $null } else { $state.activeSnooze }
        if ($null -eq $handledThroughUtc -and [string]::IsNullOrWhiteSpace($lastDeliveredOccurrenceKey) -and [string]::IsNullOrWhiteSpace($lastDispositionOccurrenceKey) -and $null -eq $sourceActiveSnooze) { throw '空提醒状态不得持久化。' }
        $rule = $rulesById[$ruleId]
        $firstScheduledUtc = Get-CountdownReminderFirstScheduledUtc -Rule $rule
        if ($null -ne $handledThroughUtc -and $null -ne $firstScheduledUtc -and $handledThroughUtc -lt $firstScheduledUtc) { throw '提醒规则游标不能早于首次计划时刻。' }
        if ($null -ne $handledThroughUtc -and -not (Test-CountdownOccurrenceMatchesRule -Rule $rule -ScheduledUtc $handledThroughUtc)) { throw '提醒规则游标必须落在规则的计划 occurrence 上。' }
        $lastDeliveredScheduledUtc = $null
        if (-not [string]::IsNullOrWhiteSpace($lastDeliveredOccurrenceKey)) {
            $occurrence = ConvertFrom-CountdownOccurrenceKey -Value $lastDeliveredOccurrenceKey
            $occurrenceRuleId = $occurrence.RuleId
            $occurrenceUtc = $occurrence.ScheduledUtc
            if ($occurrenceRuleId -ine $ruleId -or $null -eq $handledThroughUtc -or $occurrenceUtc -gt $handledThroughUtc) { throw '提醒 occurrence 与规则游标互相矛盾。' }
            $validOccurrence = Test-CountdownOccurrenceMatchesRule -Rule $rule -ScheduledUtc $occurrenceUtc
            if (-not $validOccurrence) { throw '提醒 occurrence 与规则游标互相矛盾。' }
            $lastDeliveredScheduledUtc = $occurrenceUtc
            $lastDeliveredOccurrenceKey = Get-CountdownOccurrenceKey -RuleId $ruleId -ScheduledUtc $occurrenceUtc
        }
        $lastDispositionScheduledUtc = $null
        if (-not [string]::IsNullOrWhiteSpace($lastDispositionOccurrenceKey)) {
            if ([string]::IsNullOrWhiteSpace($lastDeliveredOccurrenceKey)) { throw '提醒处置 occurrence 缺少送达事实。' }
            $occurrence = ConvertFrom-CountdownOccurrenceKey -Value $lastDispositionOccurrenceKey
            $occurrenceRuleId = $occurrence.RuleId
            $occurrenceUtc = $occurrence.ScheduledUtc
            if ($occurrenceRuleId -ine $ruleId -or
                $null -eq $handledThroughUtc -or
                $occurrenceUtc -gt $handledThroughUtc -or
                $null -eq $lastDeliveredScheduledUtc -or
                $occurrenceUtc -gt $lastDeliveredScheduledUtc) { throw '提醒处置 occurrence 与规则游标互相矛盾。' }
            $validOccurrence = Test-CountdownOccurrenceMatchesRule -Rule $rule -ScheduledUtc $occurrenceUtc
            if (-not $validOccurrence) { throw '提醒处置 occurrence 与规则游标互相矛盾。' }
            $lastDispositionScheduledUtc = $occurrenceUtc
            $lastDispositionOccurrenceKey = Get-CountdownOccurrenceKey -RuleId $ruleId -ScheduledUtc $occurrenceUtc
        }
        $activeSnooze = $null
        if ($null -ne $sourceActiveSnooze) {
            $snoozeOccurrence = ConvertFrom-CountdownOccurrenceKey -Value ([string] $sourceActiveSnooze.occurrenceKey)
            $originalScheduledUtc = ConvertTo-CountdownNoteReminderUtc -Value $sourceActiveSnooze.originalScheduledUtc -Subject '稍后原计划时刻'
            $snoozeUntilUtc = ConvertTo-CountdownNoteReminderUtc -Value $sourceActiveSnooze.snoozeUntilUtc -Subject '稍后覆盖时刻'
            $canonicalSnoozeKey = Get-CountdownOccurrenceKey -RuleId $ruleId -ScheduledUtc $originalScheduledUtc
            if ($snoozeOccurrence.RuleId -ine $ruleId -or
                $snoozeOccurrence.ScheduledUtc -ne $originalScheduledUtc -or
                $null -eq $handledThroughUtc -or
                $originalScheduledUtc -gt $handledThroughUtc -or
                $lastDeliveredOccurrenceKey -cne $canonicalSnoozeKey -or
                $snoozeUntilUtc -le $originalScheduledUtc -or
                ($null -ne $lastDispositionScheduledUtc -and $lastDispositionScheduledUtc -ge $originalScheduledUtc)) {
                throw '活动稍后提醒与规则 occurrence 状态互相矛盾。'
            }
            $activeSnooze = [ordered]@{
                occurrenceKey = $canonicalSnoozeKey
                originalScheduledUtc = $originalScheduledUtc.ToString('o', [System.Globalization.CultureInfo]::InvariantCulture)
                snoozeUntilUtc = $snoozeUntilUtc.ToString('o', [System.Globalization.CultureInfo]::InvariantCulture)
            }
        }
        $serializedReminderStates[$ruleId] = [ordered]@{
            handledThroughUtc = if ($null -eq $handledThroughUtc) { $null } else { $handledThroughUtc.ToString('o', [System.Globalization.CultureInfo]::InvariantCulture) }
            lastDeliveredOccurrenceKey = $lastDeliveredOccurrenceKey
            lastDispositionOccurrenceKey = $lastDispositionOccurrenceKey
            activeSnooze = $activeSnooze
        }
    }

    [ordered]@{
        schemaVersion = 10
        timeZoneId = 'China Standard Time'
        displayPrecision = $preferences.Precision
        monthlySalary = $canonicalMonthlySalary
        workStart = Format-CountdownWorkTime -Value $preferences.WorkStart
        workEnd = Format-CountdownWorkTime -Value $preferences.WorkEnd
        themePreference = $preferences.ThemePreference
        tags = $tags
        countdowns = $records
        notes = $notes
        reminderStates = $serializedReminderStates
    } | ConvertTo-Json -Depth 8
}

function Copy-CountdownSettingsState {
    [CmdletBinding()]
    param([Parameter(Mandatory)] [object] $State)

    $canonicalJson = ConvertTo-CountdownSettingsJson -State $State
    ConvertFrom-CountdownSettingsObject -Settings ($canonicalJson | ConvertFrom-Json)
}

function Get-CountdownSettingsWriteMutexName {
    param([Parameter(Mandatory)] [string] $Path)

    $normalizedPath = [System.IO.Path]::GetFullPath($Path).ToUpperInvariant()
    $sha256 = [System.Security.Cryptography.SHA256]::Create()
    try {
        $pathHash = $sha256.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($normalizedPath))
    }
    finally {
        $sha256.Dispose()
    }
    "Global\MinminCountdownSettings-$([System.BitConverter]::ToString($pathHash).Replace('-', ''))"
}

function Enter-CountdownSettingsWriteMutex {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [string] $Path,
        [ValidateRange(1, 60)] [int] $TimeoutSeconds = 5
    )

    $security = [System.Security.AccessControl.MutexSecurity]::new()
    $authenticatedUsers = [System.Security.Principal.SecurityIdentifier]::new(
        [System.Security.Principal.WellKnownSidType]::AuthenticatedUserSid,
        $null
    )
    $rights = [System.Security.AccessControl.MutexRights]::Synchronize -bor [System.Security.AccessControl.MutexRights]::Modify
    $rule = [System.Security.AccessControl.MutexAccessRule]::new(
        $authenticatedUsers,
        $rights,
        [System.Security.AccessControl.AccessControlType]::Allow
    )
    $security.AddAccessRule($rule)

    $mutexName = Get-CountdownSettingsWriteMutexName -Path $Path
    $mutex = $null
    if (-not [System.Threading.MutexAcl]::TryOpenExisting($mutexName, $rights, [ref] $mutex)) {
        $createdNew = $false
        try {
            $mutex = [System.Threading.MutexAcl]::Create($false, $mutexName, [ref] $createdNew, $security)
        }
        catch {
            $creationError = $_
            try { $mutex = [System.Threading.MutexAcl]::OpenExisting($mutexName, $rights) }
            catch { throw $creationError }
        }
    }
    try {
        $acquired = $false
        try {
            $acquired = $mutex.WaitOne([TimeSpan]::FromSeconds($TimeoutSeconds))
        }
        catch [System.Threading.AbandonedMutexException] {
            $acquired = $true
        }
        if (-not $acquired) {
            throw "等待设置写入锁超时（$TimeoutSeconds 秒）。"
        }
        $mutex
    }
    catch {
        $mutex.Dispose()
        throw
    }
}

function Exit-CountdownSettingsWriteMutex {
    param([AllowNull()] [System.Threading.Mutex] $Mutex)

    if ($null -eq $Mutex) { return }
    try { $Mutex.ReleaseMutex() } catch [System.ApplicationException] { }
    finally { $Mutex.Dispose() }
}

function Save-CountdownSettings {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [string] $Path,
        [Parameter(Mandatory)] [object] $State
    )

    $payload = ConvertTo-CountdownSettingsJson -State $State
    $payloadBytes = [System.Text.UTF8Encoding]::new($false).GetBytes($payload)
    $temporaryPath = "$Path.tmp"
    $settingsMutex = $null
    try {
        $settingsMutex = Enter-CountdownSettingsWriteMutex -Path $Path
        if (Test-Path -LiteralPath $Path -PathType Leaf) {
            $existingBytes = [System.IO.File]::ReadAllBytes($Path)
            if ($existingBytes.Length -eq $payloadBytes.Length -and
                [System.BitConverter]::ToString($existingBytes) -eq [System.BitConverter]::ToString($payloadBytes)) {
                return
            }
        }
        [System.IO.File]::WriteAllBytes($temporaryPath, $payloadBytes)
        Move-Item -LiteralPath $temporaryPath -Destination $Path -Force
    }
    finally {
        Exit-CountdownSettingsWriteMutex -Mutex $settingsMutex
    }
}

function Get-CountdownUtcTicks {
    param([Parameter(Mandatory)] [object] $Value)

    if ($Value -is [DateTimeOffset]) { return $Value.ToUniversalTime().Ticks }
    if ($Value -is [DateTime]) { return $Value.ToUniversalTime().Ticks }
    [DateTimeOffset]::Parse(([string] $Value), [System.Globalization.CultureInfo]::InvariantCulture).ToUniversalTime().Ticks
}

function Test-CountdownSettingsJsonEquivalent {
    param(
        [AllowNull()] [object] $Expected,
        [AllowNull()] [object] $Actual,
        [string] $PropertyName = ''
    )

    if ($null -eq $Expected -or $null -eq $Actual) { return $null -eq $Expected -and $null -eq $Actual }

    $expectedIsObject = $Expected -is [System.Management.Automation.PSCustomObject]
    $actualIsObject = $Actual -is [System.Management.Automation.PSCustomObject]
    if ($expectedIsObject -or $actualIsObject) {
        if (-not $expectedIsObject -or -not $actualIsObject) { return $false }
        $expectedProperties = @($Expected.PSObject.Properties | Where-Object { $_.MemberType -eq 'NoteProperty' })
        $actualProperties = @($Actual.PSObject.Properties | Where-Object { $_.MemberType -eq 'NoteProperty' })
        if ($expectedProperties.Count -ne $actualProperties.Count) { return $false }
        foreach ($expectedProperty in $expectedProperties) {
            $matchingProperties = @($actualProperties | Where-Object { $_.Name -ceq $expectedProperty.Name })
            if ($matchingProperties.Count -ne 1) { return $false }
            if (-not (Test-CountdownSettingsJsonEquivalent -Expected $expectedProperty.Value -Actual $matchingProperties[0].Value -PropertyName $expectedProperty.Name)) { return $false }
        }
        return $true
    }

    $expectedIsCollection = $Expected -is [System.Collections.IEnumerable] -and $Expected -isnot [string]
    $actualIsCollection = $Actual -is [System.Collections.IEnumerable] -and $Actual -isnot [string]
    if ($expectedIsCollection -or $actualIsCollection) {
        if (-not $expectedIsCollection -or -not $actualIsCollection) { return $false }
        $expectedItems = [System.Collections.ArrayList]::new()
        $actualItems = [System.Collections.ArrayList]::new()
        foreach ($item in $Expected) { [void] $expectedItems.Add($item) }
        foreach ($item in $Actual) { [void] $actualItems.Add($item) }
        if ($expectedItems.Count -ne $actualItems.Count) { return $false }
        for ($index = 0; $index -lt $expectedItems.Count; $index++) {
            if (-not (Test-CountdownSettingsJsonEquivalent -Expected $expectedItems[$index] -Actual $actualItems[$index] -PropertyName $PropertyName)) { return $false }
        }
        return $true
    }

    if ($PropertyName -eq 'targetUtc') { return (Get-CountdownUtcTicks -Value $Expected) -eq (Get-CountdownUtcTicks -Value $Actual) }
    $Expected -ceq $Actual
}

function Test-CountdownSettingsDeploymentTransition {
    [CmdletBinding()]
    param(
        [AllowNull()] [string] $BeforeHash,
        [AllowNull()] [string] $AfterHash,
        [AllowNull()] [string] $BeforeJson,
        [AllowNull()] [string] $AfterJson,
        [DateTimeOffset] $MigrationUtc = [DateTimeOffset]::UtcNow
    )

    if ($BeforeHash -eq $AfterHash) { return $true }
    if ([string]::IsNullOrWhiteSpace($BeforeHash) -or [string]::IsNullOrWhiteSpace($AfterHash) -or [string]::IsNullOrWhiteSpace($BeforeJson) -or [string]::IsNullOrWhiteSpace($AfterJson)) { return $false }

    try {
        $before = $BeforeJson | ConvertFrom-Json
        $after = $AfterJson | ConvertFrom-Json
        if ($before.schemaVersion -notin @(3, 4, 5, 6, 7, 8, 9) -or $after.schemaVersion -ne 10) { return $false }
        $expectedMigration = ConvertTo-CountdownSettingsJson -State (ConvertFrom-CountdownSettingsObject -Settings $before -MigrationUtc $MigrationUtc) | ConvertFrom-Json
        $null = ConvertFrom-CountdownSettingsObject -Settings $after -MigrationUtc $MigrationUtc
        if (-not (Test-CountdownSettingsJsonEquivalent -Expected $expectedMigration -Actual $after)) { return $false }
    }
    catch {
        return $false
    }

    $true
}

Export-ModuleMember -Function New-CountdownPreferences, ConvertFrom-CountdownSettingsObject, ConvertTo-CountdownSettingsJson, Copy-CountdownSettingsState, Enter-CountdownSettingsWriteMutex, Exit-CountdownSettingsWriteMutex, Save-CountdownSettings, Test-CountdownSettingsDeploymentTransition
