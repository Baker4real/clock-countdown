#requires -Version 7.0

[CmdletBinding()]
param(
    [string]$RepositoryRoot = (Split-Path -Parent $PSScriptRoot),
    [string]$DotnetExe,
    [switch]$RequireClean,
    [switch]$Json
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$utf8 = [System.Text.UTF8Encoding]::new($false)
[Console]::OutputEncoding = $utf8
[Console]::InputEncoding = $utf8
$OutputEncoding = $utf8

$result = [ordered]@{
    Valid = $false
    RepositoryRoot = $null
    RequireClean = [bool]$RequireClean
    Dotnet = $null
    BuildPaths = $null
    Checks = @()
    Failures = @()
}

try {
    $resolvedRoot = [System.IO.Path]::GetFullPath($RepositoryRoot).TrimEnd('\')
    $result.RepositoryRoot = $resolvedRoot
    $modulePath = Join-Path $resolvedRoot 'tools\Countdown.BuildPreflight.psm1'
    Import-Module $modulePath -Force

    $paths = Get-CountdownBuildPaths -RepositoryRoot $resolvedRoot
    $result.BuildPaths = [ordered]@{
        Candidates = @($paths.CleanCandidates)
        ExistingCandidates = @($paths.ExistingCleanCandidates)
    }
    $result.Checks += [pscustomobject][ordered]@{ Name = 'RepositoryRoot'; Valid = (Test-Path -LiteralPath $resolvedRoot -PathType Container); Details = $resolvedRoot }
    $requirement = Get-CountdownBuildSdkRequirement -RepositoryRoot $resolvedRoot
    $dotnet = Resolve-CountdownBuildDotnet -RepositoryRoot $resolvedRoot -DotnetExe $DotnetExe
    $actual = Get-CountdownDotnetSdkVersion -DotnetPath $dotnet.Path -RepositoryRoot $resolvedRoot
    $sdkMatches = Test-CountdownSdkVersionMatch -Actual $actual.Version -Required $requirement.Version -RollForward $requirement.RollForward
    $result.Dotnet = [ordered]@{
        Path = $dotnet.Path
        Source = $dotnet.Source
        Version = $actual.VersionText
        RequiredVersion = $requirement.VersionText
        RollForward = $requirement.RollForward
        AllowPrerelease = $requirement.AllowPrerelease
        GlobalJson = $requirement.Path
    }
    $result.Checks += [pscustomobject][ordered]@{ Name = 'DotnetSdk'; Valid = $sdkMatches; Details = "actual=$($actual.VersionText); required=$($requirement.VersionText); rollForward=$($requirement.RollForward); source=$($dotnet.Source)" }

    $safeDelete = Get-CountdownSafeDeleteCheck -RepositoryRoot $resolvedRoot -Candidates $paths.CleanCandidates -RequireClean:$RequireClean
    $safeValid = (-not $RequireClean) -or ($safeDelete.Available -and $safeDelete.Failures.Count -eq 0)
    if ($RequireClean -and $safeDelete.Failures.Count -gt 0) { $result.Failures += @($safeDelete.Failures) }
    $result.Checks += [pscustomobject][ordered]@{
        Name = 'SafeTreeDeleteCapability'
        Valid = $safeValid
        Details = [ordered]@{ PolicyPath = $safeDelete.PolicyPath; Available = $safeDelete.Available; Entries = @($safeDelete.Entries); RequireClean = [bool]$RequireClean }
    }

    if (-not $sdkMatches) { $result.Failures += "dotnet SDK 不符合 global.json：actual=$($actual.VersionText), required=$($requirement.VersionText), rollForward=$($requirement.RollForward)" }
    $result.Valid = ($result.Failures.Count -eq 0)
} catch {
    $result.Failures += $_.Exception.Message
    $result.Valid = $false
}

if ($Json) {
    $result | ConvertTo-Json -Depth 12
} else {
    [pscustomobject]$result
}
if (-not $result.Valid) { exit 1 }
exit 0
