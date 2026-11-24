#requires -Version 7.0
<#
.SYNOPSIS
    轻量 pre-commit 守卫：仅检查暂存区（staged index），不读取或修改未暂存工作树文件。
    1. git diff --cached --check（空白/冲突标记）
    2. UTF-8 BOM 漂移检查（禁止给原无 BOM 文件或新增文本文件写入 UTF-8 BOM）
#>
[CmdletBinding()]
param(
    [string] $RepositoryRoot
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) {
    $top = @(& git rev-parse --show-toplevel 2>$null)
    if ($LASTEXITCODE -eq 0 -and $top.Count -gt 0 -and -not [string]::IsNullOrWhiteSpace($top[0])) {
        $RepositoryRoot = $top[0].Trim()
    }
    else {
        $RepositoryRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
    }
}
$RepositoryRoot = (Resolve-Path -LiteralPath $RepositoryRoot).Path

# 1. 暂存区空白与冲突标记
$diffCheck = @(& git -C $RepositoryRoot diff --cached --check -- 2>&1 | ForEach-Object { "$_" })
if ($LASTEXITCODE -ne 0) {
    [Console]::Error.WriteLine('[pre-commit] git diff --cached --check 失败：')
    foreach ($line in $diffCheck) { [Console]::Error.WriteLine("  $line") }
    exit 1
}

# 2. 暂存区 UTF-8 BOM 漂移检查
$bomScript = Join-Path $RepositoryRoot 'tools\Test-CountdownUtf8BomDrift.ps1'
if (Test-Path -LiteralPath $bomScript -PathType Leaf) {
    $bomRaw = @(& pwsh -NoLogo -NoProfile -NonInteractive -File $bomScript -RepositoryRoot $RepositoryRoot -StagedOnly -Json)
    $bomExit = $LASTEXITCODE
    $bomReport = try { ($bomRaw -join "`n") | ConvertFrom-Json -Depth 8 -ErrorAction Stop } catch { $null }
    if ($bomExit -ne 0 -or $null -eq $bomReport -or -not [bool] $bomReport.Valid) {
        [Console]::Error.WriteLine('[pre-commit] 检测到暂存区 UTF-8 BOM 漂移（常见原因：编辑工具给无 BOM 文件补写了 BOM）：')
        if ($null -ne $bomReport -and $bomReport.Failures) {
            foreach ($failure in @($bomReport.Failures)) { [Console]::Error.WriteLine("  $failure") }
        }
        else {
            [Console]::Error.WriteLine("  $($bomRaw -join ' ')")
        }
        exit 1
    }
}

$global:LASTEXITCODE = 0
exit 0
