#requires -Version 7.0
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$tempDir = Join-Path $env:TEMP 'release-contract-compare'
[void] [System.IO.Directory]::CreateDirectory($tempDir)
$headModule = Join-Path $tempDir 'HeadContract.psm1'
# 原始字节由调用方用 bash 重定向预先导出，避免 PowerShell 管道重编码
if (-not (Test-Path -LiteralPath $headModule)) { throw 'HEAD 契约未导出：' + $headModule }

Import-Module $headModule -Force
$headContract = Get-CountdownReleaseContract
Write-Output ("Head: PackageFiles={0} IntegrityHashes={1}" -f @($headContract.PackageFiles).Count, $headContract.IntegrityHashes.Count)

Import-Module (Join-Path $repoRoot 'Countdown.ReleaseContract.psm1') -Force
$workContract = Get-CountdownReleaseContract
Write-Output ("Work: PackageFiles={0} IntegrityHashes={1}" -f @($workContract.PackageFiles).Count, $workContract.IntegrityHashes.Count)

Write-Output '--- work-only entries ---'
Compare-Object @($headContract.PackageFiles) @($workContract.PackageFiles) |
    Where-Object SideIndicator -eq '=>' | ForEach-Object InputObject
Write-Output '--- head-only entries ---'
Compare-Object @($headContract.PackageFiles) @($workContract.PackageFiles) |
    Where-Object SideIndicator -eq '<=' | ForEach-Object InputObject
