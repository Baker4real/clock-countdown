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

function ConvertTo-CountdownSettingsTargetUtc {
    param([Parameter(Mandatory)] [object] $Value)

    if ($Value -is [DateTimeOffset]) { return $Value.ToUniversalTime() }
    if ($Value -is [DateTime]) {
        if ($Value.Kind -eq [DateTimeKind]::Utc) { return [DateTimeOffset]::new($Value, [TimeSpan]::Zero) }
        return ([DateTimeOffset] $Value).ToUniversalTime()
    }
    [DateTimeOffset]::Parse(([string] $Value), [System.Globalization.CultureInfo]::InvariantCulture).ToUniversalTime()
}

function New-CountdownRecordFromSettings {
    param(
        [Parameter(Mandatory)] [string] $Id,
        [Parameter(Mandatory)] [string] $Name,
        [Parameter(Mandatory)] [DateTimeOffset] $TargetUtc,
        [AllowNull()] [System.Collections.IEnumerable] $TagIds
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
    }
}

function ConvertFrom-CountdownSettingsObject {
    [CmdletBinding()]
    param([Parameter(Mandatory)] [object] $Settings)

    $records = [System.Collections.ArrayList]::new()
    $notes = [System.Collections.ArrayList]::new()
    $tags = [System.Collections.ArrayList]::new()
    $preferences = New-CountdownPreferences

    if ($Settings.timeZoneId -ne 'China Standard Time') { throw '设置文件时区无效。' }
    if ($Settings.schemaVersion -eq 1) {
        [void] $records.Add((New-CountdownRecordFromSettings -Id ([guid]::NewGuid().ToString('N')) -Name '我的倒计时' -TargetUtc (ConvertTo-CountdownSettingsTargetUtc -Value $Settings.targetUtc)))
        return [pscustomobject]@{ Records = $records; Notes = $notes; Tags = $tags; Preferences = $preferences; Migrated = $true }
    }
    if ($Settings.schemaVersion -notin @(2, 3, 4, 5, 6)) { throw '设置文件版本无效。' }

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
        [void] $records.Add((New-CountdownRecordFromSettings -Id $id -Name $name -TargetUtc (ConvertTo-CountdownSettingsTargetUtc -Value $item.targetUtc) -TagIds $recordTagIds))
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
            if ($Settings.schemaVersion -eq 6) {
                if ($null -eq $item.PSObject.Properties['date']) { throw '设置中的便签缺少日期字段。' }
                $noteDate = $item.date
            }
            [void] $notes.Add((New-CountdownNoteRecord -Id $id -Text ([string] $item.text) -Date $noteDate -IsCompleted ([bool] $item.isCompleted) -TagIds $noteTagIds))
        }
    }

    [pscustomobject]@{ Records = $records; Notes = $notes; Tags = $tags; Preferences = $preferences; Migrated = $Settings.schemaVersion -ne 6 }
}

function ConvertTo-CountdownSettingsJson {
    [CmdletBinding()]
    param([Parameter(Mandatory)] [object] $State)

    $preferences = New-CountdownPreferences -Precision ([string] $State.Preferences.Precision) -MonthlySalary ([decimal] $State.Preferences.MonthlySalary) -WorkStartText (Format-CountdownWorkTime -Value ([TimeSpan] $State.Preferences.WorkStart)) -WorkEndText (Format-CountdownWorkTime -Value ([TimeSpan] $State.Preferences.WorkEnd))

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
    $records = @()
    foreach ($record in @($State.Records)) {
        $id = ([string] $record.id).Trim()
        $name = ([string] $record.name).Trim()
        if ([string]::IsNullOrWhiteSpace($id) -or [string]::IsNullOrWhiteSpace($name) -or $name.Length -gt 30) { throw '设置中包含无效的倒计时记录。' }
        if (-not $recordIds.Add($id) -or -not $recordNames.Add($name)) { throw '设置中包含重复的倒计时记录。' }
        if ($null -eq $record.PSObject.Properties['tagIds'] -or $null -eq $record.tagIds) { throw '设置中的倒计时缺少标签 ID 列表。' }
        $recordTagIds = ConvertTo-CountdownTagIds -TagIds $record.tagIds -KnownTagIds $knownTagIds
        $records += [ordered]@{
            id = $id
            name = $name
            targetUtc = ([DateTimeOffset] $record.targetUtc).ToUniversalTime().ToString('o')
            tagIds = @($recordTagIds | ForEach-Object { [string] $_ })
        }
    }

    $noteIds = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::OrdinalIgnoreCase)
    $notes = @()
    foreach ($note in @($State.Notes)) {
        if ($null -eq $note.PSObject.Properties['tagIds'] -or $null -eq $note.tagIds) { throw '设置中的便签缺少标签 ID 列表。' }
        $noteTagIds = ConvertTo-CountdownTagIds -TagIds $note.tagIds -KnownTagIds $knownTagIds
        $noteDate = if ($null -eq $note.PSObject.Properties['date']) { $null } else { $note.date }
        $validNote = New-CountdownNoteRecord -Id ([string] $note.id) -Text ([string] $note.text) -Date $noteDate -IsCompleted ([bool] $note.isCompleted) -TagIds $noteTagIds
        if (-not $noteIds.Add($validNote.id)) { throw '设置中包含重复的便签 ID。' }
        $notes += [ordered]@{
            id = $validNote.id
            text = $validNote.text
            date = $validNote.date
            isCompleted = [bool] $validNote.isCompleted
            tagIds = @($validNote.tagIds | ForEach-Object { [string] $_ })
        }
    }

    [ordered]@{
        schemaVersion = 6
        timeZoneId = 'China Standard Time'
        displayPrecision = $preferences.Precision
        monthlySalary = $preferences.MonthlySalary
        workStart = Format-CountdownWorkTime -Value $preferences.WorkStart
        workEnd = Format-CountdownWorkTime -Value $preferences.WorkEnd
        tags = $tags
        countdowns = $records
        notes = $notes
    } | ConvertTo-Json -Depth 5
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
        [AllowNull()] [string] $AfterJson
    )

    if ($BeforeHash -eq $AfterHash) { return $true }
    if ([string]::IsNullOrWhiteSpace($BeforeHash) -or [string]::IsNullOrWhiteSpace($AfterHash) -or [string]::IsNullOrWhiteSpace($BeforeJson) -or [string]::IsNullOrWhiteSpace($AfterJson)) { return $false }

    try {
        $before = $BeforeJson | ConvertFrom-Json
        $after = $AfterJson | ConvertFrom-Json
        if ($before.schemaVersion -notin @(3, 4, 5) -or $after.schemaVersion -ne 6) { return $false }
        $expectedMigration = ConvertTo-CountdownSettingsJson -State (ConvertFrom-CountdownSettingsObject -Settings $before) | ConvertFrom-Json
        $null = ConvertFrom-CountdownSettingsObject -Settings $after
        if (-not (Test-CountdownSettingsJsonEquivalent -Expected $expectedMigration -Actual $after)) { return $false }
    }
    catch {
        return $false
    }

    $true
}

Export-ModuleMember -Function New-CountdownPreferences, ConvertFrom-CountdownSettingsObject, ConvertTo-CountdownSettingsJson, Copy-CountdownSettingsState, Save-CountdownSettings, Test-CountdownSettingsDeploymentTransition
