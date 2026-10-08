#requires -Version 7.0

[CmdletBinding()]
param([string]$RepositoryRoot = (Split-Path -Parent $PSScriptRoot))

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'Countdown.BuildPreflight.psm1') -Force

$passed = 0
function Assert-Preflight {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) { throw "ASSERT FAILED: $Message" }
    $script:passed++
    Write-Output "PASS: $Message"
}

$root = [System.IO.Path]::GetFullPath($RepositoryRoot).TrimEnd('\')
$buildSource = Get-Content -Raw -LiteralPath (Join-Path $root 'Build-CountdownApplication.ps1')
Assert-Preflight ($buildSource.Contains("tools\Countdown.BuildPreflight.psm1") -and $buildSource.Contains('Resolve-CountdownBuildDotnet') -and $buildSource.Contains('Get-CountdownBuildPaths')) 'Build reuses the shared preflight resolver and build path contract'

$pathDotnet = (Get-Command dotnet -ErrorAction Stop).Source
$managed = Resolve-CountdownBuildDotnet -RepositoryRoot $root -DotnetExe $pathDotnet
Assert-Preflight ($managed.Source -eq 'parameter' -and $managed.Path -ieq ([System.IO.Path]::GetFullPath($pathDotnet))) 'explicit dotnet path has highest priority'

$oldEnv = $env:COUNTDOWN_DOTNET_EXE
try {
    $env:COUNTDOWN_DOTNET_EXE = $pathDotnet
    $fromEnv = Resolve-CountdownBuildDotnet -RepositoryRoot $root
    Assert-Preflight ($fromEnv.Source -eq 'COUNTDOWN_DOTNET_EXE' -and $fromEnv.Path -ieq ([System.IO.Path]::GetFullPath($pathDotnet))) 'COUNTDOWN_DOTNET_EXE precedes managed default'
    $env:COUNTDOWN_DOTNET_EXE = Join-Path ([System.IO.Path]::GetTempPath()) 'countdown-preflight-missing-dotnet.exe'
    $fallbackPath = Resolve-CountdownBuildDotnet -RepositoryRoot $root
    Assert-Preflight ($fallbackPath.Source -eq 'PATH' -and $fallbackPath.Path -ieq ([System.IO.Path]::GetFullPath($pathDotnet))) 'missing COUNTDOWN_DOTNET_EXE falls back directly to PATH as Build does'
} finally {
    $env:COUNTDOWN_DOTNET_EXE = $oldEnv
}

$required = [System.Version]::new(10, 0, 302)
$newer = [System.Version]::new(10, 0, 302, 1)
$olderMajor = [System.Version]::new(9, 0, 400)
Assert-Preflight (Test-CountdownSdkVersionMatch -Actual $newer -Required $required -RollForward 'latestPatch') 'latestPatch accepts a newer patch in the same feature band'
Assert-Preflight (-not (Test-CountdownSdkVersionMatch -Actual $olderMajor -Required $required -RollForward 'latestPatch')) 'latestPatch rejects a mismatched SDK'

$fixtureRoot = Join-Path ([System.IO.Path]::GetTempPath()) ('countdown-build-preflight-' + [guid]::NewGuid().ToString('N'))
$fixturePolicy = Join-Path $fixtureRoot 'policy.json'
$registeredRoot = Join-Path $fixtureRoot 'registered'
$unregisteredRoot = Join-Path $fixtureRoot 'unregistered'
$fixtureSucceeded = $false
try {
    [void][System.IO.Directory]::CreateDirectory($fixtureRoot)
    [void][System.IO.Directory]::CreateDirectory($registeredRoot)
    [void][System.IO.Directory]::CreateDirectory($unregisteredRoot)
    $policyDocument = [ordered]@{
        artifactType = 'env-governance.codex-safe-tree-delete-policy'
        schemaVersion = 4
        entries = @([ordered]@{
            id = 'fixture-registered-build'
            capabilityRoot = $registeredRoot
            minimumDescendantDepth = 0
            rootDeletionPurpose = 'rebuildable-build-output'
            deniedExactPaths = @()
        })
    }
    [System.IO.File]::WriteAllText($fixturePolicy, ($policyDocument | ConvertTo-Json -Depth 8), [System.Text.UTF8Encoding]::new($false))

    $unregistered = Get-CountdownSafeDeleteCheck -RepositoryRoot $root -Candidates @($unregisteredRoot) -PolicyPath $fixturePolicy -RequireClean
    Assert-Preflight (-not $unregistered.Entries[0].Valid -and $unregistered.Failures.Count -eq 1) 'unregistered fixture build root is refused with one precise failure'

    $registered = Get-CountdownSafeDeleteCheck -RepositoryRoot $root -Candidates @($registeredRoot) -PolicyPath $fixturePolicy -RequireClean
    Assert-Preflight ($registered.Available -and $registered.Entries[0].Valid -and $registered.Failures.Count -eq 0) 'registered fixture build root is accepted'
    $fixtureSucceeded = $true
} finally {
    if ($fixtureSucceeded -and (Test-Path -LiteralPath $fixtureRoot -PathType Container)) {
        [System.IO.Directory]::Delete($fixtureRoot, $true)
    } elseif (Test-Path -LiteralPath $fixtureRoot -PathType Container) {
        Write-Warning "预检契约失败证据保留：$fixtureRoot"
    }
}

$preflight = Join-Path $PSScriptRoot 'Test-CountdownBuildPreflight.ps1'
$json = & pwsh -NoLogo -NoProfile -NonInteractive -File $preflight -RepositoryRoot $root -Json 2>&1 | Out-String
Assert-Preflight ($LASTEXITCODE -eq 0 -and ($json | ConvertFrom-Json).Valid) 'default preflight passes without requiring local cleanup capability'

Write-Output "PREFLIGHT_CONTRACT_PASSED=$passed"
