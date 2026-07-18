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

function New-CountdownNoteRecord {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [string] $Id,
        [Parameter(Mandatory)] [string] $Text,
        [bool] $IsCompleted = $false,
        [AllowNull()] [System.Collections.IEnumerable] $TagIds
    )

    $normalizedId = $Id.Trim()
    $normalizedText = $Text.Trim()
    if ([string]::IsNullOrWhiteSpace($normalizedId)) { throw '便签 ID 不能为空。' }
    if ([string]::IsNullOrWhiteSpace($normalizedText)) { throw '便签内容不能为空。' }
    if ($normalizedText.Length -gt 140) { throw '便签内容不能超过 140 个字符。' }
    $normalizedTagIds = ConvertTo-CountdownNoteTagIds -TagIds $TagIds

    [pscustomobject]@{
        id = $normalizedId
        text = $normalizedText
        isCompleted = [bool] $IsCompleted
        tagIds = $normalizedTagIds
    }
}

function Copy-CountdownNotes {
    param([Parameter(Mandatory)] [System.Collections.IEnumerable] $Notes)

    $copy = [System.Collections.ArrayList]::new()
    foreach ($note in @($Notes)) {
        $tagIds = if ($null -eq $note.PSObject.Properties['tagIds']) { @() } else { $note.tagIds }
        [void] $copy.Add((New-CountdownNoteRecord -Id ([string] $note.id) -Text ([string] $note.text) -IsCompleted ([bool] $note.isCompleted) -TagIds $tagIds))
    }
    return ,$copy
}

function Invoke-NoteMutation {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [System.Collections.IEnumerable] $Notes,
        [Parameter(Mandatory)] [ValidateSet('Add', 'Update', 'SetCompletion', 'Remove')] [string] $Action,
        [string] $Text,
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
            $note = New-CountdownNoteRecord -Id ([guid]::NewGuid().ToString('N')) -Text $Text -TagIds $TagIds
            [void] $candidate.Add($note)
            $changed = $true
            $message = '已添加到便签。'
        }
        'Update' {
            $note = @($candidate | Where-Object { $_.id -eq $Id })[0]
            if ($null -ne $note) {
                $updatedNote = New-CountdownNoteRecord -Id $note.id -Text $Text -IsCompleted ([bool] $note.isCompleted) -TagIds $TagIds
                $oldTagIds = [string]::Join([char] 0, [string[]] @(ConvertTo-CountdownNoteTagIds -TagIds $note.tagIds))
                $newTagIds = [string]::Join([char] 0, [string[]] $updatedNote.tagIds)
                if ($note.text -ne $updatedNote.text -or $oldTagIds -ne $newTagIds) {
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
