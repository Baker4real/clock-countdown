#requires -Version 7.0

Set-StrictMode -Version Latest

Import-Module (Join-Path $PSScriptRoot 'Countdown.Notes.psm1')

function New-CountdownPreferences {
    [CmdletBinding()]
    param(
        [ValidateSet('Day', 'Hour', 'Minute', 'Second')] [string] $Precision = 'Second',
        [decimal] $MonthlySalary = 0,
        [string] $WorkStartText = '09:00',
        [string] $WorkEndText = '18:00'
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
    }
}

function Format-CountdownWorkTime {
    param([Parameter(Mandatory)] [TimeSpan] $Value)

    '{0:D2}:{1:D2}' -f $Value.Hours, $Value.Minutes
}

function New-CountdownRecordFromSettings {
    param(
        [Parameter(Mandatory)] [string] $Id,
        [Parameter(Mandatory)] [string] $Name,
        [Parameter(Mandatory)] [DateTimeOffset] $TargetUtc
    )

    [pscustomobject]@{
        id = $Id
        name = $Name
        targetUtc = $TargetUtc.ToUniversalTime()
    }
}

function ConvertFrom-CountdownSettingsObject {
    [CmdletBinding()]
    param([Parameter(Mandatory)] [object] $Settings)

    $records = [System.Collections.ArrayList]::new()
    $notes = [System.Collections.ArrayList]::new()
    $preferences = New-CountdownPreferences

    if ($Settings.timeZoneId -ne 'China Standard Time') { throw '设置文件时区无效。' }
    if ($Settings.schemaVersion -eq 1) {
        [void] $records.Add((New-CountdownRecordFromSettings -Id ([guid]::NewGuid().ToString('N')) -Name '我的倒计时' -TargetUtc ([DateTimeOffset]::Parse($Settings.targetUtc, [System.Globalization.CultureInfo]::InvariantCulture))))
        return [pscustomobject]@{ Records = $records; Notes = $notes; Preferences = $preferences; Migrated = $true }
    }
    if ($Settings.schemaVersion -notin @(2, 3, 4)) { throw '设置文件版本无效。' }

    $names = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::OrdinalIgnoreCase)
    $ids = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($item in @($Settings.countdowns)) {
        $id = ([string] $item.id).Trim()
        $name = ([string] $item.name).Trim()
        if ([string]::IsNullOrWhiteSpace($name) -or $name.Length -gt 30) { throw '设置中包含无效的倒计时名称。' }
        if ([string]::IsNullOrWhiteSpace($id)) { throw '设置中包含空的倒计时 ID。' }
        if (-not $names.Add($name)) { throw '设置中包含重复的倒计时名称。' }
        if (-not $ids.Add($id)) { throw '设置中包含重复的倒计时 ID。' }
        [void] $records.Add((New-CountdownRecordFromSettings -Id $id -Name $name -TargetUtc ([DateTimeOffset]::Parse($item.targetUtc, [System.Globalization.CultureInfo]::InvariantCulture))))
    }

    if ($Settings.schemaVersion -ge 3) {
        $precision = [string] $Settings.displayPrecision
        if ($precision -notin @('Day', 'Hour', 'Minute', 'Second')) { throw '设置中包含无效的悬浮窗显示精度。' }
        [decimal] $salary = 0
        if (-not [decimal]::TryParse(([string] $Settings.monthlySalary), [System.Globalization.NumberStyles]::Number, [System.Globalization.CultureInfo]::InvariantCulture, [ref] $salary)) {
            throw '设置中包含无效的月薪。'
        }
        $preferences = New-CountdownPreferences -Precision $precision -MonthlySalary $salary -WorkStartText ([string] $Settings.workStart) -WorkEndText ([string] $Settings.workEnd)
    }

    if ($Settings.schemaVersion -eq 4) {
        if ($null -eq $Settings.PSObject.Properties['notes']) { throw '设置中缺少便签列表。' }
        $noteIds = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::OrdinalIgnoreCase)
        foreach ($item in @($Settings.notes)) {
            $id = ([string] $item.id).Trim()
            if ($null -eq $item.PSObject.Properties['isCompleted'] -or $item.isCompleted -isnot [bool]) { throw '设置中包含无效的便签完成状态。' }
            if (-not $noteIds.Add($id)) { throw '设置中包含重复的便签 ID。' }
            [void] $notes.Add((New-CountdownNoteRecord -Id $id -Text ([string] $item.text) -IsCompleted ([bool] $item.isCompleted)))
        }
    }

    [pscustomobject]@{ Records = $records; Notes = $notes; Preferences = $preferences; Migrated = $Settings.schemaVersion -ne 4 }
}

function ConvertTo-CountdownSettingsJson {
    [CmdletBinding()]
    param([Parameter(Mandatory)] [object] $State)

    $preferences = New-CountdownPreferences -Precision ([string] $State.Preferences.Precision) -MonthlySalary ([decimal] $State.Preferences.MonthlySalary) -WorkStartText (Format-CountdownWorkTime -Value ([TimeSpan] $State.Preferences.WorkStart)) -WorkEndText (Format-CountdownWorkTime -Value ([TimeSpan] $State.Preferences.WorkEnd))

    $recordIds = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::OrdinalIgnoreCase)
    $recordNames = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::OrdinalIgnoreCase)
    $records = @()
    foreach ($record in @($State.Records)) {
        $id = ([string] $record.id).Trim()
        $name = ([string] $record.name).Trim()
        if ([string]::IsNullOrWhiteSpace($id) -or [string]::IsNullOrWhiteSpace($name) -or $name.Length -gt 30) { throw '设置中包含无效的倒计时记录。' }
        if (-not $recordIds.Add($id) -or -not $recordNames.Add($name)) { throw '设置中包含重复的倒计时记录。' }
        $records += [ordered]@{ id = $id; name = $name; targetUtc = ([DateTimeOffset] $record.targetUtc).ToUniversalTime().ToString('o') }
    }

    $noteIds = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::OrdinalIgnoreCase)
    $notes = @()
    foreach ($note in @($State.Notes)) {
        $validNote = New-CountdownNoteRecord -Id ([string] $note.id) -Text ([string] $note.text) -IsCompleted ([bool] $note.isCompleted)
        if (-not $noteIds.Add($validNote.id)) { throw '设置中包含重复的便签 ID。' }
        $notes += [ordered]@{ id = $validNote.id; text = $validNote.text; isCompleted = [bool] $validNote.isCompleted }
    }

    [ordered]@{
        schemaVersion = 4
        timeZoneId = 'China Standard Time'
        displayPrecision = $preferences.Precision
        monthlySalary = $preferences.MonthlySalary
        workStart = Format-CountdownWorkTime -Value $preferences.WorkStart
        workEnd = Format-CountdownWorkTime -Value $preferences.WorkEnd
        countdowns = $records
        notes = $notes
    } | ConvertTo-Json -Depth 4
}

function Copy-CountdownSettingsState {
    [CmdletBinding()]
    param([Parameter(Mandatory)] [object] $State)

    $canonicalJson = ConvertTo-CountdownSettingsJson -State $State
    ConvertFrom-CountdownSettingsObject -Settings ($canonicalJson | ConvertFrom-Json)
}

function Save-CountdownSettings {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [string] $Path,
        [Parameter(Mandatory)] [object] $State
    )

    $payload = ConvertTo-CountdownSettingsJson -State $State
    $temporaryPath = "$Path.tmp"
    [System.IO.File]::WriteAllText($temporaryPath, $payload, (New-Object System.Text.UTF8Encoding($false)))
    Move-Item -LiteralPath $temporaryPath -Destination $Path -Force
}

function Get-CountdownUtcTicks {
    param([Parameter(Mandatory)] [object] $Value)

    if ($Value -is [DateTimeOffset]) { return $Value.ToUniversalTime().Ticks }
    if ($Value -is [DateTime]) { return $Value.ToUniversalTime().Ticks }
    [DateTimeOffset]::Parse(([string] $Value), [System.Globalization.CultureInfo]::InvariantCulture).ToUniversalTime().Ticks
}

function Test-CountdownSettingsDeploymentTransition {
    [CmdletBinding()]
    param(
        [AllowNull()] [string] $BeforeHash,
        [AllowNull()] [string] $AfterHash,
        [AllowNull()] [string] $BeforeJson,
        [AllowNull()] [string] $AfterJson
    )

    if ($BeforeHash -eq $AfterHash) { return $true }
    if ([string]::IsNullOrWhiteSpace($BeforeHash) -or [string]::IsNullOrWhiteSpace($AfterHash) -or [string]::IsNullOrWhiteSpace($BeforeJson) -or [string]::IsNullOrWhiteSpace($AfterJson)) { return $false }

    try {
        $before = $BeforeJson | ConvertFrom-Json
        $after = $AfterJson | ConvertFrom-Json
        if ($before.schemaVersion -ne 3 -or $after.schemaVersion -ne 4) { return $false }
        if ($null -eq $after.PSObject.Properties['notes'] -or @($after.notes).Count -ne 0) { return $false }
        if (([string] $before.timeZoneId) -cne ([string] $after.timeZoneId)) { return $false }
        if (([string] $before.displayPrecision) -cne ([string] $after.displayPrecision)) { return $false }
        if (([decimal] $before.monthlySalary) -ne ([decimal] $after.monthlySalary)) { return $false }
        if (([TimeSpan]::Parse([string] $before.workStart)) -ne ([TimeSpan]::Parse([string] $after.workStart))) { return $false }
        if (([TimeSpan]::Parse([string] $before.workEnd)) -ne ([TimeSpan]::Parse([string] $after.workEnd))) { return $false }

        $previousCountdowns = @($before.countdowns)
        $migratedCountdowns = @($after.countdowns)
        if ($previousCountdowns.Count -ne $migratedCountdowns.Count) { return $false }
        foreach ($previous in $previousCountdowns) {
            $matching = @($migratedCountdowns | Where-Object { $_.id -eq $previous.id })
            if ($matching.Count -ne 1) { return $false }
            if (([string] $matching[0].name) -cne ([string] $previous.name)) { return $false }
            if ((Get-CountdownUtcTicks -Value $matching[0].targetUtc) -ne (Get-CountdownUtcTicks -Value $previous.targetUtc)) { return $false }
        }
    }
    catch {
        return $false
    }

    $true
}

Export-ModuleMember -Function New-CountdownPreferences, ConvertFrom-CountdownSettingsObject, ConvertTo-CountdownSettingsJson, Copy-CountdownSettingsState, Save-CountdownSettings, Test-CountdownSettingsDeploymentTransition
