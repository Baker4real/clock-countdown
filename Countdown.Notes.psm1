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

function New-CountdownNoteRecord {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [string] $Id,
        [Parameter(Mandatory)] [string] $Text,
        [AllowNull()] [object] $Date,
        [bool] $IsCompleted = $false,
        [AllowNull()] [System.Collections.IEnumerable] $TagIds
    )

    $normalizedId = $Id.Trim()
    $normalizedText = $Text.Trim()
    if ([string]::IsNullOrWhiteSpace($normalizedId)) { throw '便签 ID 不能为空。' }
    if ([string]::IsNullOrWhiteSpace($normalizedText)) { throw '便签内容不能为空。' }
    if ($normalizedText.Length -gt 140) { throw '便签内容不能超过 140 个字符。' }
    $normalizedDate = ConvertTo-CountdownNoteDate -Date $Date
    $normalizedTagIds = ConvertTo-CountdownNoteTagIds -TagIds $TagIds

    [pscustomobject]@{
        id = $normalizedId
        text = $normalizedText
        date = $normalizedDate
        isCompleted = [bool] $IsCompleted
        tagIds = $normalizedTagIds
    }
}

function Copy-CountdownNotes {
    param([Parameter(Mandatory)] [System.Collections.IEnumerable] $Notes)

    $copy = [System.Collections.ArrayList]::new()
    foreach ($note in @($Notes)) {
        $date = if ($null -eq $note.PSObject.Properties['date']) { $null } else { $note.date }
        $tagIds = if ($null -eq $note.PSObject.Properties['tagIds']) { @() } else { $note.tagIds }
        [void] $copy.Add((New-CountdownNoteRecord -Id ([string] $note.id) -Text ([string] $note.text) -Date $date -IsCompleted ([bool] $note.isCompleted) -TagIds $tagIds))
    }
    return ,$copy
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
        [Parameter(Mandatory)] [scriptblock] $PersistCandidate
    )

    $candidate = Copy-CountdownNotes -Notes $Notes
    $changed = $false
    $note = $null
    $message = $null

    switch ($Action) {
        'Add' {
            if ([string]::IsNullOrWhiteSpace($Date)) { throw '请选择便签日期。' }
            $note = New-CountdownNoteRecord -Id ([guid]::NewGuid().ToString('N')) -Text $Text -Date $Date -TagIds $TagIds
            [void] $candidate.Add($note)
            $changed = $true
            $message = '已添加到便签。'
        }
        'Update' {
            $note = @($candidate | Where-Object { $_.id -eq $Id })[0]
            if ($null -ne $note) {
                if ([string]::IsNullOrWhiteSpace($Date)) { throw '请选择便签日期。' }
                $updatedNote = New-CountdownNoteRecord -Id $note.id -Text $Text -Date $Date -IsCompleted ([bool] $note.isCompleted) -TagIds $TagIds
                $oldTagIds = [string]::Join([char] 0, [string[]] @(ConvertTo-CountdownNoteTagIds -TagIds $note.tagIds))
                $newTagIds = [string]::Join([char] 0, [string[]] $updatedNote.tagIds)
                if ($note.text -ne $updatedNote.text -or $note.date -ne $updatedNote.date -or $oldTagIds -ne $newTagIds) {
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

Export-ModuleMember -Function New-CountdownNoteRecord, Invoke-NoteMutation
