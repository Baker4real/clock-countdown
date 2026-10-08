#requires -Version 7.0

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Resolve-CountdownBuildDotnet {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$RepositoryRoot,
        [string]$DotnetExe
    )

    $candidates = [System.Collections.Generic.List[object]]::new()
    $pathCommand = Get-Command dotnet -ErrorAction SilentlyContinue
    if (-not [string]::IsNullOrWhiteSpace($DotnetExe)) {
        $candidates.Add([pscustomobject]@{ Path = $DotnetExe; Source = 'parameter' })
    } elseif (-not [string]::IsNullOrWhiteSpace($env:COUNTDOWN_DOTNET_EXE)) {
        $candidates.Add([pscustomobject]@{ Path = $env:COUNTDOWN_DOTNET_EXE; Source = 'COUNTDOWN_DOTNET_EXE' })
    } else {
        $candidates.Add([pscustomobject]@{ Path = 'D:\Codex\tools\dotnet\10.0\dotnet.exe'; Source = 'managed' })
    }
    if ($null -ne $pathCommand) {
        $candidates.Add([pscustomobject]@{ Path = [string]$pathCommand.Source; Source = 'PATH' })
    }

    foreach ($candidate in $candidates) {
        $candidateText = [string]$candidate.Path
        $candidatePath = if ([System.IO.Path]::IsPathRooted($candidateText)) {
            [System.IO.Path]::GetFullPath($candidateText)
        } else {
            [System.IO.Path]::GetFullPath((Join-Path $RepositoryRoot $candidateText))
        }
        if (Test-Path -LiteralPath $candidatePath -PathType Leaf) {
            return [pscustomobject]@{
                Path = $candidatePath
                Source = [string]$candidate.Source
            }
        }
    }
    throw "未找到可用 dotnet：$([string]($candidates | ForEach-Object Path) -join ', ')"
}

function Get-CountdownBuildSdkRequirement {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$RepositoryRoot)

    $globalJson = Join-Path $RepositoryRoot 'global.json'
    if (-not (Test-Path -LiteralPath $globalJson -PathType Leaf)) {
        throw "未找到 global.json：$globalJson"
    }
    $document = Get-Content -Raw -LiteralPath $globalJson | ConvertFrom-Json -Depth 8
    if ($null -eq $document.sdk -or [string]::IsNullOrWhiteSpace([string]$document.sdk.version)) {
        throw "global.json 缺少 sdk.version：$globalJson"
    }
    $version = [System.Version]::Parse([string]$document.sdk.version)
    $rollForward = if ([string]::IsNullOrWhiteSpace([string]$document.sdk.rollForward)) { 'patch' } else { [string]$document.sdk.rollForward }
    $allowPrerelease = if ($null -eq $document.sdk.allowPrerelease) { $false } else { [bool]$document.sdk.allowPrerelease }
    return [pscustomobject]@{
        Version = $version
        VersionText = [string]$document.sdk.version
        RollForward = $rollForward
        AllowPrerelease = $allowPrerelease
        Path = $globalJson
    }
}

function Get-CountdownDotnetSdkVersion {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$DotnetPath,
        [Parameter(Mandatory)][string]$RepositoryRoot
    )

    $previousLocation = Get-Location
    try {
        Set-Location -LiteralPath $RepositoryRoot
        $output = @(& $DotnetPath '--version' 2>&1)
        $exitCode = $LASTEXITCODE
    } finally {
        Set-Location -LiteralPath $previousLocation.Path
    }
    if ($exitCode -ne 0 -or $output.Count -eq 0) {
        throw "dotnet --version 失败：path=$DotnetPath exit=$exitCode output=$($output -join ' ')"
    }
    $versionText = ([string]$output[-1]).Trim()
    try { $version = [System.Version]::Parse($versionText) } catch { throw "dotnet --version 输出非法：$versionText" }
    return [pscustomobject]@{ Version = $version; VersionText = $versionText }
}

function Test-CountdownSdkVersionMatch {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][System.Version]$Actual,
        [Parameter(Mandatory)][System.Version]$Required,
        [Parameter(Mandatory)][string]$RollForward
    )

    $matches = switch ($RollForward) {
        'disable' { $Actual -eq $Required }
        'patch' { $Actual.Major -eq $Required.Major -and $Actual.Minor -eq $Required.Minor -and [math]::Floor($Actual.Build / 100) -eq [math]::Floor($Required.Build / 100) -and $Actual -ge $Required }
        'latestPatch' { $Actual.Major -eq $Required.Major -and $Actual.Minor -eq $Required.Minor -and [math]::Floor($Actual.Build / 100) -eq [math]::Floor($Required.Build / 100) -and $Actual -ge $Required }
        'feature' { $Actual.Major -eq $Required.Major -and $Actual.Minor -eq $Required.Minor -and $Actual.Build -ge $Required.Build }
        'latestFeature' { $Actual.Major -eq $Required.Major -and $Actual.Minor -eq $Required.Minor -and $Actual.Build -ge $Required.Build }
        'minor' { $Actual.Major -eq $Required.Major -and ($Actual.Minor -gt $Required.Minor -or ($Actual.Minor -eq $Required.Minor -and $Actual.Build -ge $Required.Build)) }
        'latestMinor' { $Actual.Major -eq $Required.Major -and ($Actual.Minor -gt $Required.Minor -or ($Actual.Minor -eq $Required.Minor -and $Actual.Build -ge $Required.Build)) }
        'major' { $Actual -ge $Required }
        'latestMajor' { $Actual -ge $Required }
        default { $false }
    }
    return [bool]$matches
}

function Get-CountdownBuildPaths {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$RepositoryRoot)

    $root = [System.IO.Path]::GetFullPath($RepositoryRoot).TrimEnd('\')
    $projects = @('MinminCountdown.Core', 'MinminCountdown', 'MinminCountdown.Verification') | ForEach-Object { Join-Path $root "src\$_" }
    $paths = [System.Collections.Generic.List[string]]::new()
    $paths.Add((Join-Path $root 'app'))
    $paths.Add((Join-Path $root 'verification'))
    foreach ($project in $projects) {
        $paths.Add((Join-Path $project 'bin'))
        $paths.Add((Join-Path $project 'obj'))
    }
    return [pscustomobject]@{
        RepositoryRoot = $root
        Projects = @($projects)
        CleanCandidates = @($paths)
        ExistingCleanCandidates = @($paths | Where-Object { Test-Path -LiteralPath $_ -PathType Container })
    }
}

function Read-CountdownSafeDeletePolicy {
    [CmdletBinding()]
    param([string]$PolicyPath = 'D:\agents\projects\统一环境\managed\safe-tree-delete\policy.json')

    if (-not (Test-Path -LiteralPath $PolicyPath -PathType Leaf)) {
        throw "未找到 safe-tree-delete policy：$PolicyPath"
    }
    $policy = Get-Content -Raw -LiteralPath $PolicyPath | ConvertFrom-Json -Depth 32
    if ([string]$policy.artifactType -cne 'env-governance.codex-safe-tree-delete-policy') { throw 'safe-delete policy artifact 非法。' }
    if ($policy.schemaVersion -notin @(3, 4, 5) -or $policy.entries -isnot [array] -or @($policy.entries).Count -eq 0) {
        throw 'safe-delete policy schema 或 entries 非法。'
    }
    return $policy
}

function Get-CountdownSafeDeleteCheck {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$RepositoryRoot,
        [Parameter(Mandatory)][string[]]$Candidates,
        [string]$PolicyPath = 'D:\agents\projects\统一环境\managed\safe-tree-delete\policy.json',
        [switch]$RequireClean
    )

    $result = [ordered]@{ PolicyPath = $policyPath; Available = $false; Entries = @(); Failures = @() }
    try { $policy = Read-CountdownSafeDeletePolicy -PolicyPath $policyPath; $result.Available = $true }
    catch { $result.Failures += $_.Exception.Message; return [pscustomobject]$result }

    foreach ($candidate in $Candidates) {
        $full = [System.IO.Path]::GetFullPath($candidate)
        $entry = @($policy.entries | Where-Object { [System.IO.Path]::GetFullPath([string]$_.capabilityRoot).TrimEnd('\') -ieq $full.TrimEnd('\') })
        $exists = Test-Path -LiteralPath $full -PathType Container
        $registered = $entry.Count -eq 1
        $item = [ordered]@{ Path = $full; Exists = $exists; Registered = $registered; Depth = $null; MinimumDepth = $null; Purpose = $null; EntryId = $null; Valid = $true }
        if ($registered) {
            $item.EntryId = [string]$entry[0].id; $item.MinimumDepth = [int]$entry[0].minimumDescendantDepth; $item.Purpose = [string]$entry[0].rootDeletionPurpose; $item.Depth = 0
            if ($exists -and ($item.MinimumDepth -ne 0 -or $item.Purpose -cne 'rebuildable-build-output')) {
                $item.Valid = $false
                if ($RequireClean) { $result.Failures += "构建目录 capability root 的策略用途/深度不适用于清理：$full" }
            }
            if ($exists -and @($entry[0].deniedExactPaths | Where-Object { [System.IO.Path]::GetFullPath([string]$_).TrimEnd('\') -ieq $full.TrimEnd('\') }).Count -gt 0) {
                $item.Valid = $false
                if ($RequireClean) { $result.Failures += "构建目录命中 safe-delete denylist：$full" }
            }
        }
        if ($RequireClean -and $exists -and -not $registered) { $item.Valid = $false; $result.Failures += "构建目录未登记 capability root：$full" }
        $result.Entries += [pscustomobject]$item
    }
    return [pscustomobject]$result
}

Export-ModuleMember -Function Resolve-CountdownBuildDotnet,Get-CountdownBuildSdkRequirement,Get-CountdownDotnetSdkVersion,Test-CountdownSdkVersionMatch,Get-CountdownBuildPaths,Read-CountdownSafeDeletePolicy,Get-CountdownSafeDeleteCheck
