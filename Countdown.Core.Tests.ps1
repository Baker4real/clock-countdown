#requires -Version 7.0

[CmdletBinding()]
param(
    [switch] $ReleaseContractOnly,
    [switch] $ValidationOnly
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

Import-Module (Join-Path $PSScriptRoot 'Countdown.ReleaseContract.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'Countdown.Validation.psm1') -Force

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

# 等线程作业结束、取回输出再移除。不用 Receive-Job -Wait -AutoRemoveJob：作业恰在它等待时结束，它会移除两次，第二次报
# “作业不存在”（本机 pwsh 7.6.9 实测 200 次里 4 次）；先 Wait-Job 再取回、移除没有这个竞态。
function Receive-CountdownTestThreadJob {
    param([Parameter(Mandatory)] [System.Management.Automation.Job] $Job)

    [void] (Wait-Job -Job $Job)
    try { Receive-Job -Job $Job }
    finally { Remove-Job -Job $Job -Force }
}

# 另一个运行持有全机安装锁：另一个线程取得测试自己的锁名并一直持有，直到 Stop-CountdownTestLockHolder 放开。
# 安装锁按线程归属，同一线程再取会重入成功，所以持有者与探测都在别的线程里。
function Start-CountdownTestLockHolder {
    param([Parameter(Mandatory)] [string] $Name)

    $held = [System.Threading.ManualResetEventSlim]::new($false)
    $release = [System.Threading.ManualResetEventSlim]::new($false)
    $job = Start-ThreadJob -ArgumentList $Name, $held, $release -ScriptBlock {
        param($Name, $Held, $Release)
        $mutex = [System.Threading.Mutex]::new($false, $Name)
        try {
            [void] $mutex.WaitOne()
            $Held.Set()
            [void] $Release.Wait(120000)
            $mutex.ReleaseMutex()
        }
        finally { $mutex.Dispose() }
    }
    if (-not $held.Wait(30000)) { throw "另一个线程 30 秒内没能取得锁 $Name。" }
    [pscustomobject]@{ Job = $job; Release = $release }
}

function Stop-CountdownTestLockHolder {
    param([Parameter(Mandatory)] [object] $Holder)

    $Holder.Release.Set()
    [void] (Receive-CountdownTestThreadJob -Job $Holder.Job)
}

# 从另一个线程试取一次锁（不等待），取到就马上放开：返回锁此刻是否空闲。
function Test-CountdownTestLockFree {
    param([Parameter(Mandatory)] [string] $Name)

    $job = Start-ThreadJob -ArgumentList $Name -ScriptBlock {
        param($Name)
        $mutex = [System.Threading.Mutex]::new($false, $Name)
        try {
            $acquired = try { $mutex.WaitOne(0) } catch [System.Threading.AbandonedMutexException] { $true }
            if ($acquired) { $mutex.ReleaseMutex() }
            $acquired
        }
        finally { $mutex.Dispose() }
    }
    [bool] (Receive-CountdownTestThreadJob -Job $job)
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

Write-TestSegmentTiming -Name 'Setup'
if ($ValidationOnly -or -not $ReleaseContractOnly) {
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

. (Join-Path $PSScriptRoot 'Countdown.Core.Tests.MainDomain.ps1')

Write-TestSegmentTiming -Name 'MainDomain'

. (Join-Path $PSScriptRoot 'Countdown.Core.Tests.InstallStatus.ps1')
Write-TestSegmentTiming -Name 'InstallStatus'
. (Join-Path $PSScriptRoot 'Countdown.Core.Tests.Installer.ps1')
Write-TestSegmentTiming -Name 'Installer'
. (Join-Path $PSScriptRoot 'Countdown.Core.Tests.InstallerValidation.ps1')
Write-TestSegmentTiming -Name 'InstallerValidation'
. (Join-Path $PSScriptRoot 'Countdown.Core.Tests.SourceContracts.ps1')

Write-TestSegmentTiming -Name 'SourceContracts'
# 放在最后：假办公室在本进程里运行包内安装器，它从暂存目录的副本重新导入 Countdown.Installer.psm1。
. (Join-Path $PSScriptRoot 'Countdown.Core.Tests.DeployEntry.ps1')
Write-TestSegmentTiming -Name 'DeployEntry'
Write-TestSegmentTotal
if ($failures.Count -gt 0) {
    $failures | ForEach-Object { Write-Error $_ -ErrorAction Continue }
    exit 1
}

$beijingNow = [System.TimeZoneInfo]::ConvertTimeBySystemTimeZoneId([DateTimeOffset]::UtcNow, 'China Standard Time')
[pscustomobject]@{ Passed = $script:assertions; Failed = 0; TimeZone = $beijingNow.Offset.ToString() } | ConvertTo-Json
