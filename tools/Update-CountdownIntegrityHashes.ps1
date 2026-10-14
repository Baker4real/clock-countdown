#requires -Version 7.0
<#
.SYNOPSIS
按暂存区 blob 重算 Countdown.ReleaseContract.psm1 里的 IntegrityHashes 静态 SHA-256。

.DESCRIPTION
发布契约的静态钉值按提交 blob 的字节计算。本工具对每个钉值读取暂存区 blob（git cat-file blob :<path>），
所以先 git add 改过的受钉文件再运行；受钉文件有未暂存改动时直接失败。覆盖两种钉值：
$script:nonAssetHashes 的 '<path>' = '<sha256>' 行，以及 $script:buildAssetDefinitions 每项按 SourcePath
计算的 SHA256。发布载荷清单 app\published-payload.json 及其文件是构建产物，不在本工具范围内。

默认把不一致的钉值写回模块（保留原有编码和换行），之后再 git add Countdown.ReleaseContract.psm1。
-Check 只列出全部不一致项、不改文件，有不一致时退出码为 1；Assert-CountdownReleasePackage 遇到第一个不一致就停。

.EXAMPLE
pwsh -NoLogo -NoProfile -File ./tools/Update-CountdownIntegrityHashes.ps1 -Check
#>
[CmdletBinding()]
param(
    [switch] $Check,
    [string] $Root = (Split-Path -Parent $PSScriptRoot)
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path -LiteralPath $Root).Path
$contractPath = Join-Path $repoRoot 'Countdown.ReleaseContract.psm1'
if (-not (Test-Path -LiteralPath $contractPath -PathType Leaf)) {
    throw "找不到发布契约：$contractPath"
}

function Invoke-CountdownGit {
    param([Parameter(Mandatory)] [string[]] $Arguments)

    $startInfo = [System.Diagnostics.ProcessStartInfo]::new('git')
    foreach ($argument in @('-C', $repoRoot) + $Arguments) {
        $startInfo.ArgumentList.Add($argument)
    }
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $process = [System.Diagnostics.Process]::Start($startInfo)
    $stderrTask = $process.StandardError.ReadToEndAsync()
    $buffer = [System.IO.MemoryStream]::new()
    $process.StandardOutput.BaseStream.CopyTo($buffer)
    $process.WaitForExit()
    [pscustomobject]@{
        ExitCode = $process.ExitCode
        Bytes = $buffer.ToArray()
        Error = $stderrTask.Result.Trim()
    }
}

$contractBytes = [System.IO.File]::ReadAllBytes($contractPath)
$hasBom = $contractBytes.Length -ge 3 -and $contractBytes[0] -eq 0xEF -and $contractBytes[1] -eq 0xBB -and $contractBytes[2] -eq 0xBF
$textOffset = if ($hasBom) { 3 } else { 0 }
$contractText = [System.Text.UTF8Encoding]::new($false, $true).GetString($contractBytes, $textOffset, $contractBytes.Length - $textOffset)

$pinPatterns = [ordered]@{
    file = "(?m)^[ \t]*'(?<path>[^'\r\n]+)'[ \t]*=[ \t]*'(?<hash>[0-9A-Fa-f]{64})'"
    asset = "SourcePath[ \t]*=[ \t]*'(?<path>[^'\r\n]+)'(?:(?!SourcePath)[\s\S])*?SHA256[ \t]*=[ \t]*'(?<hash>[0-9A-Fa-f]{64})'"
}
$pins = [System.Collections.Generic.List[object]]::new()
foreach ($kind in $pinPatterns.Keys) {
    $found = [regex]::Matches($contractText, $pinPatterns[$kind])
    if ($found.Count -eq 0) {
        throw "发布契约里找不到 $kind 钉值，契约格式可能已变，先更新本工具。"
    }
    foreach ($match in $found) {
        $pins.Add([pscustomobject]@{
                Kind = $kind
                Path = $match.Groups['path'].Value
                Pinned = $match.Groups['hash'].Value
                HashIndex = $match.Groups['hash'].Index
                Actual = $null
            })
    }
}

$gitPaths = @($pins | ForEach-Object { $_.Path.Replace('\', '/') })
$diff = Invoke-CountdownGit -Arguments (@('diff', '--name-only', '-z', '--') + $gitPaths)
if ($diff.ExitCode -ne 0) {
    throw "git diff 失败：$($diff.Error)"
}
$unstaged = @([System.Text.Encoding]::UTF8.GetString($diff.Bytes).Split([char] 0, [System.StringSplitOptions]::RemoveEmptyEntries))
if ($unstaged.Count -gt 0) {
    '受钉文件有未暂存改动；钉值按暂存区 blob 计算，先 git add 再运行：'
    $unstaged | ForEach-Object { "  $_" }
    exit 1
}

foreach ($pin in $pins) {
    $blob = Invoke-CountdownGit -Arguments @('cat-file', 'blob', (':' + $pin.Path.Replace('\', '/')))
    if ($blob.ExitCode -eq 0) {
        $pin.Actual = [Convert]::ToHexString([System.Security.Cryptography.SHA256]::HashData($blob.Bytes))
    }
}
$missing = @($pins | Where-Object { $null -eq $_.Actual })
$mismatched = @($pins | Where-Object {
        $null -ne $_.Actual -and -not [string]::Equals($_.Actual, $_.Pinned, [System.StringComparison]::OrdinalIgnoreCase)
    })

'钉值 {0} 个（文件 {1}、资产 {2}）：不一致 {3}，暂存区缺失 {4}' -f $pins.Count,
    @($pins | Where-Object Kind -eq 'file').Count, @($pins | Where-Object Kind -eq 'asset').Count,
    $mismatched.Count, $missing.Count
foreach ($pin in $mismatched) {
    '  不一致 [{0}] {1}  {2} -> {3}' -f $pin.Kind, $pin.Path, $pin.Pinned.Substring(0, 12), $pin.Actual.Substring(0, 12)
}
foreach ($pin in $missing) {
    '  暂存区缺失 [{0}] {1}' -f $pin.Kind, $pin.Path
}

if ($missing.Count -gt 0 -or ($Check -and $mismatched.Count -gt 0)) {
    exit 1
}
if ($Check) {
    exit 0
}
if ($mismatched.Count -eq 0) {
    '钉值全部一致，未改文件。'
    exit 0
}

$builder = [System.Text.StringBuilder]::new($contractText)
foreach ($pin in @($mismatched | Sort-Object HashIndex -Descending)) {
    [void] $builder.Remove($pin.HashIndex, 64).Insert($pin.HashIndex, $pin.Actual)
}
[System.IO.File]::WriteAllText($contractPath, $builder.ToString(), [System.Text.UTF8Encoding]::new($hasBom))
'已更新 {0} 个钉值；git add Countdown.ReleaseContract.psm1 后再提交。' -f $mismatched.Count
