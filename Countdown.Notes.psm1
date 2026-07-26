#requires -Version 7.0

Set-StrictMode -Version Latest

function ConvertTo-CountdownNoteTagIds {
    [CmdletBinding()]
    param([AllowNull()] [System.Collections.IEnumerable] $TagIds)

    $normalizedTagIds = [System.Collections.ArrayList]::new()
    $seenTagIds = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($tagId in $TagIds) {
        $normalizedTagId = ([string] $tagId).Trim()
        if ([string]::IsNullOrWhiteSpace($normalizedTagId)) { throw '便签包含空的标签 ID。' }
        if (-not $seenTagIds.Add($normalizedTagId)) { throw '便签包含重复的标签 ID。' }
        [void] $normalizedTagIds.Add($normalizedTagId)
    }

    return ,$normalizedTagIds
}

function ConvertTo-CountdownNoteDate {
    [CmdletBinding()]
    param([AllowNull()] [object] $Date)

    if ($null -eq $Date) { return $null }
    if ($Date -isnot [string]) { throw '便签日期必须使用 yyyy-MM-dd 格式。' }
    if ([string]::IsNullOrWhiteSpace($Date)) { throw '便签日期必须使用 yyyy-MM-dd 格式。' }
    if ($Date -cnotmatch '^\d{4}-\d{2}-\d{2}$') { throw '便签日期必须使用 yyyy-MM-dd 格式。' }

    $parsed = [DateTime]::MinValue
    if (-not [DateTime]::TryParseExact(
            $Date,
            'yyyy-MM-dd',
            [System.Globalization.CultureInfo]::InvariantCulture,
            [System.Globalization.DateTimeStyles]::None,
            [ref] $parsed
        )) {
        throw '便签日期无效。'
    }

    $parsed.ToString('yyyy-MM-dd', [System.Globalization.CultureInfo]::InvariantCulture)
}

function ConvertTo-CountdownNoteReminderIntervalMinutes {
    [CmdletBinding()]
    param([AllowNull()] [object] $Minutes)

    if ($null -eq $Minutes) { return $null }
    [int] $parsed = 0
    if (
        $Minutes -is [bool] -or
        -not [int]::TryParse(
            ([string] $Minutes),
            [System.Globalization.NumberStyles]::None,
            [System.Globalization.CultureInfo]::InvariantCulture,
            [ref] $parsed
        ) -or
        $parsed -lt 1 -or
        $parsed -gt 1440
    ) {
        throw '便签提醒间隔必须是 1 到 1440 分钟之间的整数。'
    }
    $parsed
}

function ConvertTo-CountdownNoteReminderUtc {
    param(
        [Parameter(Mandatory)] [object] $Value,
        [Parameter(Mandatory)] [string] $Subject
    )

    if ($Value -is [DateTimeOffset]) {
        return ([DateTimeOffset] $Value).ToUniversalTime()
    }
    if ($Value -is [DateTime] -and ([DateTime] $Value).Kind -ne [DateTimeKind]::Unspecified) {
        return ([DateTimeOffset] ([DateTime] $Value)).ToUniversalTime()
    }
    $parsed = [DateTimeOffset]::MinValue
    if (
        $Value -isnot [string] -or
        -not [DateTimeOffset]::TryParseExact(
            [string] $Value,
            'o',
            [System.Globalization.CultureInfo]::InvariantCulture,
            [System.Globalization.DateTimeStyles]::None,
            [ref] $parsed
        )
    ) {
        throw "$Subject 必须是带时区的有效时间。"
    }
    $parsed.ToUniversalTime()
}

function Assert-CountdownNoteReminderProperties {
    param(
        [Parameter(Mandatory)] [object] $Reminder,
        [Parameter(Mandatory)] [string[]] $Expected
    )

    $actual = @($Reminder.PSObject.Properties.Name)
    if ($actual.Count -ne $Expected.Count) { throw '便签提醒包含不适用于当前类型的字段。' }
    foreach ($name in $Expected) {
        if ($name -notin $actual) { throw "便签提醒缺少 $name 字段。" }
    }
}

function ConvertTo-CountdownNoteReminder {
    param(
        [AllowNull()] [object] $Reminder,
        [AllowNull()] [object] $ReminderIntervalMinutes,
        [AllowNull()] [object] $ReminderStartUtc,
        [switch] $UseReminder
    )

    if (-not $UseReminder) {
        $interval = ConvertTo-CountdownNoteReminderIntervalMinutes -Minutes $ReminderIntervalMinutes
        if (($null -eq $interval) -ne ($null -eq $ReminderStartUtc)) {
            throw '便签提醒间隔和开始时间必须同时设置或同时关闭。'
        }
        if ($null -eq $interval) {
            return [pscustomobject][ordered]@{ type = 'none' }
        }
        $startUtc = ConvertTo-CountdownNoteReminderUtc -Value $ReminderStartUtc -Subject '便签提醒开始时间'
        return [pscustomobject][ordered]@{
            type = 'repeat'
            firstDueUtc = $startUtc.AddMinutes($interval)
            intervalMinutes = $interval
        }
    }

    if ($null -eq $Reminder -or $null -eq $Reminder.PSObject.Properties['type']) {
        throw '便签缺少提醒对象。'
    }
    $type = [string] $Reminder.type
    switch ($type) {
        'none' {
            Assert-CountdownNoteReminderProperties -Reminder $Reminder -Expected @('type')
            return [pscustomobject][ordered]@{ type = 'none' }
        }
        'once' {
            Assert-CountdownNoteReminderProperties -Reminder $Reminder -Expected @('type', 'firstDueUtc', 'delivery')
            $delivery = [string] $Reminder.delivery
            if ($delivery -notin @('pending', 'delivered')) {
                throw '设置中包含无效的一次性提醒送达状态。'
            }
            return [pscustomobject][ordered]@{
                type = 'once'
                firstDueUtc = ConvertTo-CountdownNoteReminderUtc -Value $Reminder.firstDueUtc -Subject '便签首次提醒时间'
                delivery = $delivery
            }
        }
        'repeat' {
            Assert-CountdownNoteReminderProperties -Reminder $Reminder -Expected @('type', 'firstDueUtc', 'intervalMinutes')
            $interval = ConvertTo-CountdownNoteReminderIntervalMinutes -Minutes $Reminder.intervalMinutes
            return [pscustomobject][ordered]@{
                type = 'repeat'
                firstDueUtc = ConvertTo-CountdownNoteReminderUtc -Value $Reminder.firstDueUtc -Subject '便签首次提醒时间'
                intervalMinutes = $interval
            }
        }
        default { throw '设置中包含无效的便签提醒类型。' }
    }
}

function New-CountdownNoteRecord {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [string] $Id,
        [Parameter(Mandatory)] [string] $Text,
        [AllowNull()] [object] $Date,
        [bool] $IsCompleted = $false,
        [AllowNull()] [System.Collections.IEnumerable] $TagIds,
        [AllowNull()] [object] $ReminderIntervalMinutes,
        [AllowNull()] [object] $ReminderStartUtc,
        [AllowNull()] [object] $Reminder
    )

    $normalizedId = $Id.Trim()
    $normalizedText = $Text.Trim()
    if ([string]::IsNullOrWhiteSpace($normalizedId)) { throw '便签 ID 不能为空。' }
    if ([string]::IsNullOrWhiteSpace($normalizedText)) { throw '便签内容不能为空。' }
    if ($normalizedText.Length -gt 140) { throw '便签内容不能超过 140 个字符。' }
    $normalizedDate = ConvertTo-CountdownNoteDate -Date $Date
    $normalizedTagIds = ConvertTo-CountdownNoteTagIds -TagIds $TagIds
    $normalizedReminder = ConvertTo-CountdownNoteReminder `
        -Reminder $Reminder `
        -ReminderIntervalMinutes $ReminderIntervalMinutes `
        -ReminderStartUtc $ReminderStartUtc `
        -UseReminder:($PSBoundParameters.ContainsKey('Reminder'))
    $normalizedReminderIntervalMinutes = if ($normalizedReminder.type -eq 'repeat') {
        [int] $normalizedReminder.intervalMinutes
    }
    else { $null }
    $normalizedReminderStartUtc = if ($normalizedReminder.type -eq 'repeat') {
        ([DateTimeOffset] $normalizedReminder.firstDueUtc).AddMinutes(-$normalizedReminderIntervalMinutes)
    }
    else { $null }

    [pscustomobject]@{
        id = $normalizedId
        text = $normalizedText
        date = $normalizedDate
        isCompleted = [bool] $IsCompleted
        tagIds = $normalizedTagIds
        reminder = $normalizedReminder
        reminderIntervalMinutes = $normalizedReminderIntervalMinutes
        reminderStartUtc = $normalizedReminderStartUtc
    }
}

function Copy-CountdownNotes {
    param([Parameter(Mandatory)] [System.Collections.IEnumerable] $Notes)

    $copy = [System.Collections.ArrayList]::new()
    foreach ($note in @($Notes)) {
        $date = if ($null -eq $note.PSObject.Properties['date']) { $null } else { $note.date }
        $tagIds = if ($null -eq $note.PSObject.Properties['tagIds']) { @() } else { $note.tagIds }
        if ($null -ne $note.PSObject.Properties['reminder']) {
            [void] $copy.Add((New-CountdownNoteRecord -Id ([string] $note.id) -Text ([string] $note.text) -Date $date -IsCompleted ([bool] $note.isCompleted) -TagIds $tagIds -Reminder $note.reminder))
        }
        else {
            $reminderIntervalMinutes = if ($null -eq $note.PSObject.Properties['reminderIntervalMinutes']) { $null } else { $note.reminderIntervalMinutes }
            $reminderStartUtc = if ($null -eq $note.PSObject.Properties['reminderStartUtc']) { $null } else { $note.reminderStartUtc }
            [void] $copy.Add((New-CountdownNoteRecord -Id ([string] $note.id) -Text ([string] $note.text) -Date $date -IsCompleted ([bool] $note.isCompleted) -TagIds $tagIds -ReminderIntervalMinutes $reminderIntervalMinutes -ReminderStartUtc $reminderStartUtc))
        }
    }
    return ,$copy
}

function Get-DueCountdownNoteReminders {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [System.Collections.IEnumerable] $Notes,
        [Parameter(Mandatory)] [DateTimeOffset] $BeijingTime,
        [Parameter(Mandatory)] [System.Collections.IDictionary] $LastShownCycles
    )

    $nowUtc = $BeijingTime.ToUniversalTime()
    foreach ($note in @($Notes)) {
        if ([bool] $note.isCompleted) { continue }
        $reminder = if ($null -eq $note.PSObject.Properties['reminder']) {
            ConvertTo-CountdownNoteReminder -ReminderIntervalMinutes $note.reminderIntervalMinutes -ReminderStartUtc $note.reminderStartUtc
        }
        else { $note.reminder }
        if ($reminder.type -eq 'none' -or ($reminder.type -eq 'once' -and $reminder.delivery -eq 'delivered')) { continue }

        $noteId = [string] $note.id
        if ($reminder.type -eq 'once') {
            if ($nowUtc -lt ([DateTimeOffset] $reminder.firstDueUtc).ToUniversalTime()) { continue }
            $lastShownCycle = if ($LastShownCycles.Contains($noteId)) { [long] $LastShownCycles[$noteId] } else { 0L }
            if ($lastShownCycle -ge 1) { continue }
            [pscustomobject]@{
                Note = $note
                NoteId = $noteId
                Cycle = 1L
                IntervalMinutes = $null
            }
            continue
        }

        $intervalMinutes = [int] $reminder.intervalMinutes
        $firstDueUtc = ([DateTimeOffset] $reminder.firstDueUtc).ToUniversalTime()
        $intervalTicks = [TimeSpan]::FromMinutes([int] $intervalMinutes).Ticks
        $elapsedTicks = $nowUtc.Ticks - $firstDueUtc.Ticks
        if ($elapsedTicks -lt 0) { continue }
        $cycle = 1L + [long] [Math]::Floor($elapsedTicks / $intervalTicks)
        $lastShownCycle = if ($LastShownCycles.Contains($noteId)) { [long] $LastShownCycles[$noteId] } else { 0L }
        if ($cycle -le $lastShownCycle) { continue }

        [pscustomobject]@{
            Note = $note
            NoteId = $noteId
            Cycle = $cycle
            IntervalMinutes = [int] $intervalMinutes
        }
    }
}

function Invoke-NoteMutation {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [System.Collections.IEnumerable] $Notes,
        [Parameter(Mandatory)] [ValidateSet('Add', 'Update', 'SetCompletion', 'Remove')] [string] $Action,
        [string] $Text,
        [AllowNull()] [AllowEmptyString()] [string] $Date,
        [string] $Id,
        [bool] $IsCompleted,
        [AllowNull()] [System.Collections.IEnumerable] $TagIds,
        [AllowNull()] [object] $ReminderIntervalMinutes,
        [AllowNull()] [object] $ReminderStartUtc,
        [Parameter(Mandatory)] [scriptblock] $PersistCandidate
    )

    $candidate = Copy-CountdownNotes -Notes $Notes
    $changed = $false
    $note = $null
    $message = $null

    switch ($Action) {
        'Add' {
            if ([string]::IsNullOrWhiteSpace($Date)) { throw '请选择便签日期。' }
            $note = New-CountdownNoteRecord -Id ([guid]::NewGuid().ToString('N')) -Text $Text -Date $Date -TagIds $TagIds -ReminderIntervalMinutes $ReminderIntervalMinutes -ReminderStartUtc $ReminderStartUtc
            [void] $candidate.Add($note)
            $changed = $true
            $message = '已添加到便签。'
        }
        'Update' {
            $note = @($candidate | Where-Object { $_.id -eq $Id })[0]
            if ($null -ne $note) {
                if ([string]::IsNullOrWhiteSpace($Date)) { throw '请选择便签日期。' }
                $updatedNote = New-CountdownNoteRecord -Id $note.id -Text $Text -Date $Date -IsCompleted ([bool] $note.isCompleted) -TagIds $TagIds -ReminderIntervalMinutes $ReminderIntervalMinutes -ReminderStartUtc $ReminderStartUtc
                $oldTagIds = [string]::Join([char] 0, [string[]] @(ConvertTo-CountdownNoteTagIds -TagIds $note.tagIds))
                $newTagIds = [string]::Join([char] 0, [string[]] $updatedNote.tagIds)
                if (
                    $note.text -ne $updatedNote.text -or
                    $note.date -ne $updatedNote.date -or
                    $oldTagIds -ne $newTagIds -or
                    $note.reminderIntervalMinutes -ne $updatedNote.reminderIntervalMinutes -or
                    $note.reminderStartUtc -ne $updatedNote.reminderStartUtc
                ) {
                    $candidate[$candidate.IndexOf($note)] = $updatedNote
                    $note = $updatedNote
                    $changed = $true
                    $message = '已保存便签。'
                }
            }
        }
        'SetCompletion' {
            $note = @($candidate | Where-Object { $_.id -eq $Id })[0]
            if ($null -ne $note -and [bool] $note.isCompleted -ne $IsCompleted) {
                $note.isCompleted = $IsCompleted
                $changed = $true
                $message = if ($IsCompleted) { '已标记为已完成。' } else { '已恢复为未完成。' }
            }
        }
        'Remove' {
            $note = @($candidate | Where-Object { $_.id -eq $Id })[0]
            if ($null -ne $note) {
                $message = ('已删除“{0}”。' -f $note.text)
                [void] $candidate.Remove($note)
                $changed = $true
            }
        }
    }

    if (-not $changed) {
        return [pscustomobject]@{ Notes = $candidate; Action = $Action; Note = $null; Changed = $false; Message = $null }
    }

    & $PersistCandidate $candidate | Out-Null
    [pscustomobject]@{ Notes = $candidate; Action = $Action; Note = $note; Changed = $true; Message = $message }
}

Export-ModuleMember -Function New-CountdownNoteRecord, Get-DueCountdownNoteReminders, Invoke-NoteMutation
