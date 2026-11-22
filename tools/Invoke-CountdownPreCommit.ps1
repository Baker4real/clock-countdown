#requires -Version 7.0
<#
.SYNOPSIS
    轻量 pre-commit 守卫：仅检查暂存区（staged index），不读取或修改未暂存工作树文件。
    1. git diff --cached --check（空白/冲突标记）
    2. UTF-8 BOM 漂移检查（禁止给原无 BOM 文件或新增文本文件写入 UTF-8 BOM）
    3. 若暂存区触及 Countdown.ReleaseContract.psm1 或其 IntegrityHashes 受钉文件，运行静态哈希校验
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

# 3. 仅当暂存区触及发布契约或任一受钉文件时校验静态 SHA-256，避免阻断脏工作树里的无关提交
$contractPath = Join-Path $RepositoryRoot 'Countdown.ReleaseContract.psm1'
$hashScript = Join-Path $RepositoryRoot 'tools\Update-CountdownIntegrityHashes.ps1'
if ((Test-Path -LiteralPath $contractPath -PathType Leaf) -and (Test-Path -LiteralPath $hashScript -PathType Leaf)) {
    $stagedRaw = @(& git -C $RepositoryRoot diff --cached --name-only --)
    if ($LASTEXITCODE -eq 0 -and $stagedRaw.Count -gt 0) {
        $contractText = [System.IO.File]::ReadAllText($contractPath)
        $pinnedSet = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
        [void] $pinnedSet.Add('Countdown.ReleaseContract.psm1')
        foreach ($pattern in @(
                "(?m)^[ \t]*'(?<path>[^'\r\n]+)'[ \t]*=[ \t]*'[0-9A-Fa-f]{64}'",
                "SourcePath[ \t]*=[ \t]*'(?<path>[^'\r\n]+)'(?:(?!SourcePath)[\s\S])*?SHA256[ \t]*=[ \t]*'[0-9A-Fa-f]{64}'"
            )) {
            foreach ($match in [regex]::Matches($contractText, $pattern)) {
                [void] $pinnedSet.Add($match.Groups['path'].Value.Replace('\', '/'))
            }
        }
        $touchedPinned = @($stagedRaw | ForEach-Object { $_.Trim().Replace('\', '/') } | Where-Object { $pinnedSet.Contains($_) })
        if ($touchedPinned.Count -gt 0) {
            $hashOutput = @(& pwsh -NoLogo -NoProfile -NonInteractive -File $hashScript -Root $RepositoryRoot -Check 2>&1 | ForEach-Object { "$_" })
            if ($LASTEXITCODE -ne 0) {
                [Console]::Error.WriteLine('[pre-commit] 暂存区包含发布契约受钉文件，但 IntegrityHashes 校验未通过（先运行 tools/Update-CountdownIntegrityHashes.ps1 并 git add Countdown.ReleaseContract.psm1）：')
                foreach ($line in $hashOutput) { [Console]::Error.WriteLine("  $line") }
                exit 1
            }
        }
    }
}

$global:LASTEXITCODE = 0
exit 0
