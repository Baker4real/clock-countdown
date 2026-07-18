#requires -Version 7.0

Set-StrictMode -Version Latest

function New-CountdownTagRecord {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [string] $Id,
        [Parameter(Mandatory)] [string] $Name,
        [string] $Color
    )

    $normalizedId = $Id.Trim()
    $normalizedName = $Name.Trim()
    $normalizedColor = $Color.Trim()
    if ([string]::IsNullOrWhiteSpace($normalizedId)) { throw '标签 ID 不能为空。' }
    if ([string]::IsNullOrWhiteSpace($normalizedName)) { throw '标签名称不能为空。' }
    if ($normalizedName.Length -gt 30) { throw '标签名称不能超过 30 个字符。' }
    if (-not [string]::IsNullOrWhiteSpace($normalizedColor) -and $normalizedColor -notmatch '^#[0-9A-Fa-f]{6}([0-9A-Fa-f]{2})?$') { throw '标签颜色必须是 #RRGGBB 或 #AARRGGBB。' }

    [pscustomobject]@{
        id = $normalizedId
        name = $normalizedName
        color = if ([string]::IsNullOrWhiteSpace($normalizedColor)) { $null } else { $normalizedColor }
    }
}

function Copy-CountdownTags {
    [CmdletBinding()]
    param([Parameter(Mandatory)] [System.Collections.IEnumerable] $Tags)

    $copy = [System.Collections.ArrayList]::new()
    foreach ($tag in @($Tags)) {
        [void] $copy.Add((New-CountdownTagRecord -Id ([string] $tag.id) -Name ([string] $tag.name) -Color ([string] $tag.color)))
    }
    return ,$copy
}

function ConvertTo-CountdownTagIds {
    [CmdletBinding()]
    param(
        [AllowNull()] [System.Collections.IEnumerable] $TagIds,
        [Parameter(Mandatory)] [AllowEmptyCollection()] [System.Collections.IEnumerable] $KnownTagIds
    )

    $knownById = [System.Collections.Generic.Dictionary[string, string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($knownTagId in $KnownTagIds) {
        $normalizedKnownTagId = ([string] $knownTagId).Trim()
        if ([string]::IsNullOrWhiteSpace($normalizedKnownTagId)) { throw '标签库包含空 ID。' }
        if (-not $knownById.TryAdd($normalizedKnownTagId, $normalizedKnownTagId)) { throw '标签库包含重复 ID。' }
    }

    $result = [System.Collections.ArrayList]::new()
    $seen = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    if ($null -ne $TagIds) {
        foreach ($tagId in $TagIds) {
            $normalizedTagId = ([string] $tagId).Trim()
            if ([string]::IsNullOrWhiteSpace($normalizedTagId)) { throw '记录包含空的标签 ID。' }
            [string] $canonicalTagId = ''
            if (-not $knownById.TryGetValue($normalizedTagId, [ref] $canonicalTagId)) { throw '记录包含不存在的标签 ID。' }
            if (-not $seen.Add($canonicalTagId)) { throw '记录包含重复的标签 ID。' }
            [void] $result.Add($canonicalTagId)
        }
    }

    return ,$result
}

function Test-CountdownTagFilter {
    [CmdletBinding()]
    param(
        [AllowNull()] [System.Collections.IEnumerable] $TagIds,
        [AllowNull()] [System.Collections.IEnumerable] $SelectedTagIds,
        [ValidateSet('Any', 'All')] [string] $MatchMode = 'Any',
        [switch] $UntaggedOnly
    )

    $recordTagIds = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($tagId in $TagIds) {
        $normalizedTagId = ([string] $tagId).Trim()
        if (-not [string]::IsNullOrWhiteSpace($normalizedTagId)) { [void] $recordTagIds.Add($normalizedTagId) }
    }

    if ($UntaggedOnly) { return $recordTagIds.Count -eq 0 }

    $selectedIdsSet = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($selectedTagId in $SelectedTagIds) {
        $normalizedSelectedTagId = ([string] $selectedTagId).Trim()
        if (-not [string]::IsNullOrWhiteSpace($normalizedSelectedTagId)) { [void] $selectedIdsSet.Add($normalizedSelectedTagId) }
    }

    if ($selectedIdsSet.Count -eq 0) { return $true }
    if ($MatchMode -eq 'Any') {
        foreach ($selectedTagId in $selectedIdsSet) {
            if ($recordTagIds.Contains($selectedTagId)) { return $true }
        }
        return $false
    }

    foreach ($selectedTagId in $selectedIdsSet) {
        if (-not $recordTagIds.Contains($selectedTagId)) { return $false }
    }
    $true
}

Export-ModuleMember -Function New-CountdownTagRecord, Copy-CountdownTags, ConvertTo-CountdownTagIds, Test-CountdownTagFilter
