#requires -Version 7.0

[CmdletBinding()]
param(
    [switch] $AcceptanceOnly,
    [switch] $ReleaseContractOnly,
    [switch] $ValidationOnly,
    [switch] $RemoteDeploymentOnly
)

# CI 环境检测
$isCIEnvironment = $env:GITHUB_ACTIONS -eq 'true' -or $env:CI -eq 'true'

$ErrorActionPreference = 'Stop'
# Native validation children emit UTF-8; decoding with Windows' cached 936
# code page can consume the closing JSON quote after a Chinese path.
[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)

# #126 分段计时插桩：计时行只走 stderr，保持 stdout 单一 JSON 报告契约
# （Countdown.Validation.psm1 的 Invoke-CountdownValidationProcess 严格解析整个 stdout）。
$script:testClock = [System.Diagnostics.Stopwatch]::StartNew()
$script:segmentClock = [System.Diagnostics.Stopwatch]::StartNew()
function Write-TestSegmentTiming {
    param([Parameter(Mandatory)] [string] $Name)
    $script:segmentClock.Stop()
    [Console]::Error.WriteLine(
        ('[test-segment] name={0} seconds={1:F3}' -f $Name, $script:segmentClock.Elapsed.TotalSeconds))
    $script:segmentClock.Restart()
}
function Write-TestSegmentTotal {
    [Console]::Error.WriteLine(
        ('[test-segment-total] seconds={0:F3}' -f $script:testClock.Elapsed.TotalSeconds))
}

Import-Module (Join-Path $PSScriptRoot 'Countdown.Core.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'Countdown.Notes.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'Countdown.Tags.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'Countdown.Settings.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'Countdown.Runtime.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'Countdown.InstallTransaction.psm1') -Force -DisableNameChecking
Import-Module (Join-Path $PSScriptRoot 'Countdown.InstallLifecycle.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'Countdown.ReleaseContract.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'Countdown.Validation.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'Countdown.RemoteDeployment.psm1') -Force

$failures = [System.Collections.Generic.List[string]]::new()
$script:assertions = 0
function Assert-Equal($Expected, $Actual, [string] $Name) {
    $script:assertions++
    if ($Expected -ne $Actual) {
        $failures.Add("$Name expected=[$Expected] actual=[$Actual]")
    }
}

function Remove-CountdownReleaseContractTestTree {
    param([Parameter(Mandatory)] [string] $Path)

    $resolvedPath = [System.IO.Path]::GetFullPath($Path)
    $expectedParent = [System.IO.Path]::GetFullPath($PSScriptRoot).TrimEnd('\')
    if (-not [string]::Equals(
            [System.IO.Path]::GetDirectoryName($resolvedPath),
            $expectedParent,
            [System.StringComparison]::OrdinalIgnoreCase) -or
        -not [System.IO.Path]::GetFileName($resolvedPath).StartsWith(
            'release-contract-test-',
            [System.StringComparison]::Ordinal)) {
        throw "拒绝清理未受管的发布契约测试目录：$resolvedPath"
    }
    if (-not (Test-Path -LiteralPath $resolvedPath -PathType Container)) {
        return
    }

    foreach ($file in Get-ChildItem -LiteralPath $resolvedPath -File -Recurse) {
        Remove-Item -LiteralPath $file.FullName -Force
    }
    foreach ($directory in @(
            Get-ChildItem -LiteralPath $resolvedPath -Directory -Recurse |
                Sort-Object { $_.FullName.Length } -Descending
        )) {
        Remove-Item -LiteralPath $directory.FullName -Force
    }
    Remove-Item -LiteralPath $resolvedPath -Force
}

function Remove-CountdownIsolatedTestTree {
    param(
        [Parameter(Mandatory)] [string] $Path,
        [Parameter(Mandatory)] [string] $AllowedRoot
    )

    $resolvedPath = [System.IO.Path]::GetFullPath($Path)
    $resolvedRoot = [System.IO.Path]::GetFullPath($AllowedRoot).TrimEnd('\') + '\'
    if (-not $resolvedPath.StartsWith($resolvedRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "拒绝清理隔离测试根目录之外的路径：$resolvedPath"
    }
    if (-not (Test-Path -LiteralPath $resolvedPath -PathType Container)) {
        return
    }

    foreach ($file in Get-ChildItem -LiteralPath $resolvedPath -File -Recurse) {
        Remove-Item -LiteralPath $file.FullName -Force
    }
    foreach ($directory in @(
            Get-ChildItem -LiteralPath $resolvedPath -Directory -Recurse |
                Sort-Object { $_.FullName.Length } -Descending
        )) {
        Remove-Item -LiteralPath $directory.FullName -Force
    }
    Remove-Item -LiteralPath $resolvedPath -Force
}

function Get-CountdownValidationSettingsArtifactPaths {
    param([Parameter(Mandatory)] [string] $SettingsPath)

    @(
        $SettingsPath,
        "$SettingsPath.tmp",
        "$SettingsPath.backup1",
        "$SettingsPath.backup1.tmp",
        "$SettingsPath.backup1.rollback",
        "$SettingsPath.backup2",
        "$SettingsPath.backup2.tmp",
        "$SettingsPath.backup2.rollback"
    )
}

function Remove-CountdownValidationSettingsArtifactFixture {
    param([Parameter(Mandatory)] [string] $SettingsPath)

    foreach ($path in Get-CountdownValidationSettingsArtifactPaths -SettingsPath $SettingsPath) {
        if (Test-Path -LiteralPath $path -PathType Leaf) {
            Remove-Item -LiteralPath $path -Force
        }
    }
    foreach ($path in Get-CountdownValidationSettingsArtifactPaths -SettingsPath $SettingsPath) {
        if (Test-Path -LiteralPath $path -PathType Container) {
            Remove-Item -LiteralPath $path -Force
        }
    }
}

function Get-IndependentCountdownDiagnosticMutexName {
    param([Parameter(Mandatory)] [string] $Path)

    $normalizedPath = [System.IO.Path]::GetFullPath($Path).ToUpperInvariant()
    $hex = [Convert]::ToHexString(
        [System.Security.Cryptography.SHA256]::HashData(
            [System.Text.UTF8Encoding]::new($false).GetBytes($normalizedPath)))
    "Local\MinminCountdown.Diagnostic-$($hex.Substring(0, 32))"
}

Write-TestSegmentTiming -Name 'Setup'
if ($ValidationOnly -or (-not $AcceptanceOnly -and -not $ReleaseContractOnly -and -not $RemoteDeploymentOnly)) {
    . (Join-Path $PSScriptRoot 'Countdown.Core.Tests.Validation.ps1')

    if ($ValidationOnly) {
        if ($failures.Count -gt 0) {
            $failures | ForEach-Object { Write-Error $_ }
            exit 1
        }
        Write-TestSegmentTiming -Name 'Validation'
        Write-TestSegmentTotal
        [pscustomobject]@{ Passed = $script:assertions; Failed = 0; Scope = 'Validation' } | ConvertTo-Json
        exit 0
    }
}

Write-TestSegmentTiming -Name 'Validation'
if ($RemoteDeploymentOnly -or (-not $AcceptanceOnly -and -not $ReleaseContractOnly -and -not $ValidationOnly)) {
    . (Join-Path $PSScriptRoot 'Countdown.Core.Tests.RemoteDeployment.ps1')

    if ($RemoteDeploymentOnly) {
        if ($failures.Count -gt 0) {
            $failures | ForEach-Object { Write-Error $_ }
            exit 1
        }
        Write-TestSegmentTiming -Name 'RemoteDeployment'
        Write-TestSegmentTotal
        [pscustomobject]@{ Passed = $script:assertions; Failed = 0; Scope = 'RemoteDeployment' } | ConvertTo-Json
        exit 0
    }
}

Write-TestSegmentTiming -Name 'RemoteDeployment'
if (-not $AcceptanceOnly) {
    . (Join-Path $PSScriptRoot 'Countdown.Core.Tests.ReleaseContract.ps1')

    Write-TestSegmentTiming -Name 'ReleaseContract'
    if ($ReleaseContractOnly) {
        if ($failures.Count -gt 0) {
            $failures | ForEach-Object { Write-Error $_ }
            exit 1
        }
        Write-TestSegmentTotal
        [pscustomobject]@{ Passed = $script:assertions; Failed = 0; Scope = 'ReleaseContract' } | ConvertTo-Json
        exit 0
    }
}

. (Join-Path $PSScriptRoot 'Countdown.Core.Tests.TransactionAcceptance.ps1')

Write-TestSegmentTiming -Name 'TransactionAcceptance'
if ($AcceptanceOnly) {
    if ($failures.Count -gt 0) {
        $failures | ForEach-Object { Write-Error $_ }
        exit 1
    }
    Write-TestSegmentTotal
    [pscustomobject]@{ Passed = $script:assertions; Failed = 0; Scope = 'TransactionAcceptance' } | ConvertTo-Json
    exit 0
}

. (Join-Path $PSScriptRoot 'Countdown.Core.Tests.MainDomain.ps1')

Write-TestSegmentTiming -Name 'MainDomain'

. (Join-Path $PSScriptRoot 'Countdown.Core.Tests.InstallLifecycle.ps1')

Write-TestSegmentTiming -Name 'InstallLifecycle'
. (Join-Path $PSScriptRoot 'Countdown.Core.Tests.InstallerValidation.ps1')
Write-TestSegmentTiming -Name 'InstallerValidation'
. (Join-Path $PSScriptRoot 'Countdown.Core.Tests.SourceContracts.ps1')

Write-TestSegmentTiming -Name 'SourceContracts'
Write-TestSegmentTotal
if ($failures.Count -gt 0) {
    $failures | ForEach-Object { Write-Error $_ -ErrorAction Continue }
    exit 1
}

[pscustomobject]@{ Passed = $script:assertions; Failed = 0; TimeZone = (Get-BeijingNow).Offset.ToString() } | ConvertTo-Json
