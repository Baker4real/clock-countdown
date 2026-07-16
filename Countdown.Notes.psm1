#requires -Version 7.0

Set-StrictMode -Version Latest

function New-CountdownNoteRecord {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [string] $Id,
        [Parameter(Mandatory)] [string] $Text,
        [bool] $IsCompleted = $false
    )

    $normalizedId = $Id.Trim()
    $normalizedText = $Text.Trim()
    if ([string]::IsNullOrWhiteSpace($normalizedId)) { throw '便签 ID 不能为空。' }
    if ([string]::IsNullOrWhiteSpace($normalizedText)) { throw '便签内容不能为空。' }
    if ($normalizedText.Length -gt 140) { throw '便签内容不能超过 140 个字符。' }

    [pscustomobject]@{
        id = $normalizedId
        text = $normalizedText
        isCompleted = [bool] $IsCompleted
    }
}

function Copy-CountdownNotes {
    param([Parameter(Mandatory)] [System.Collections.IEnumerable] $Notes)

    $copy = [System.Collections.ArrayList]::new()
    foreach ($note in @($Notes)) {
        [void] $copy.Add((New-CountdownNoteRecord -Id ([string] $note.id) -Text ([string] $note.text) -IsCompleted ([bool] $note.isCompleted)))
    }
    return ,$copy
}

function Invoke-NoteMutation {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [System.Collections.IEnumerable] $Notes,
        [Parameter(Mandatory)] [ValidateSet('Add', 'SetCompletion', 'Remove')] [string] $Action,
        [string] $Text,
        [string] $Id,
        [bool] $IsCompleted,
        [Parameter(Mandatory)] [scriptblock] $PersistCandidate
    )

    $candidate = Copy-CountdownNotes -Notes $Notes
    $changed = $false
    $note = $null
    $message = $null

    switch ($Action) {
        'Add' {
            $note = New-CountdownNoteRecord -Id ([guid]::NewGuid().ToString('N')) -Text $Text
            [void] $candidate.Add($note)
            $changed = $true
            $message = '已添加到便签。'
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
