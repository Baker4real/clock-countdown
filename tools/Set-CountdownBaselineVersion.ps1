#requires -Version 7.0
<#
.SYNOPSIS
把现役视觉基线版本号换成新编号，一次改完源码、测试与验证模块里的全部钉点。

.DESCRIPTION
现役版本以 src/MinminCountdown/VisualReferenceBaseline.cs 的 Version 常量为准（主窗与悬浮窗共用），脚本经
Countdown.Validation.psm1 的 Get-CountdownVisualBaseline 读取它。本工具把受 Git 跟踪的 *.ps1、*.psm1、*.cs（assets 除外）
里的旧版本字符串全部换成新版本，保留原有 BOM 与换行，写回后复核旧版本不再残留；DESIGN.md 等文档的叙述与
聚合哈希仍需人工更新，工具只列出仍提到旧版本的文件。

基线编号跨分支全局分配（见 DESIGN.md）：新编号必须大于本地全部引用里提交过的基线 manifest、各工作树现存的
基线目录和现役版本的编号。作废且没留下目录的编号只记在 DESIGN.md，需人工核对。-WhatIf 只列出将改的文件。

.EXAMPLE
pwsh -NoLogo -NoProfile -File ./tools/Set-CountdownBaselineVersion.ps1 -Version 2026-10-28-v64 -WhatIf

-Version 换成重拍当天的日期和下一个未用编号。与现役版本相同或编号不够大时工具拒绝执行，后者会报出下一个可用编号。
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^[0-9]{4}-[0-9]{2}-[0-9]{2}-v[0-9]+$')]
    [string] $Version,
    [string] $Root = (Split-Path -Parent $PSScriptRoot)
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path -LiteralPath $Root).Path
$strictUtf8 = [System.Text.UTF8Encoding]::new($false, $true)

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
    $startInfo.StandardOutputEncoding = [System.Text.UTF8Encoding]::new($false)
    $startInfo.StandardErrorEncoding = [System.Text.UTF8Encoding]::new($false)
    $process = [System.Diagnostics.Process]::Start($startInfo)
    $stderrTask = $process.StandardError.ReadToEndAsync()
    $output = $process.StandardOutput.ReadToEnd()
    $process.WaitForExit()
    [pscustomobject]@{
        ExitCode = $process.ExitCode
        Lines = @($output.Split([char[]] @([char] 0, "`n"), [System.StringSplitOptions]::RemoveEmptyEntries) |
                ForEach-Object { $_.TrimEnd("`r") } | Where-Object { $_ })
        Error = $stderrTask.Result.Trim()
    }
}

function Read-Utf8File {
    param([Parameter(Mandatory)] [string] $Path)

    $bytes = [System.IO.File]::ReadAllBytes($Path)
    $hasBom = $bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF
    $offset = if ($hasBom) { 3 } else { 0 }
    [pscustomobject]@{
        HasBom = $hasBom
        Text = $strictUtf8.GetString($bytes, $offset, $bytes.Length - $offset)
    }
}

function Get-BaselineNumber {
    param([Parameter(Mandatory)] [string] $Name)

    $match = [regex]::Match($Name, '^[0-9]{4}-[0-9]{2}-[0-9]{2}-v(?<number>[0-9]+)(?:\.|$)')
    if ($match.Success) { [int] $match.Groups['number'].Value }
}

$parsedDate = [datetime]::MinValue
if (-not [datetime]::TryParseExact($Version.Substring(0, 10), 'yyyy-MM-dd', [cultureinfo]::InvariantCulture,
        [System.Globalization.DateTimeStyles]::None, [ref] $parsedDate)) {
    throw "版本日期无效：$($Version.Substring(0, 10))"
}
$newNumber = Get-BaselineNumber -Name $Version

Import-Module (Join-Path (Split-Path -Parent $PSScriptRoot) 'Countdown.Validation.psm1') -Force
$oldVersion = (Get-CountdownVisualBaseline -RepositoryRoot $repoRoot).Version
if ($oldVersion -ceq $Version) {
    throw "现役版本已是 $Version。"
}

$usedNumbers = [System.Collections.Generic.List[object]]::new()
$usedNumbers.Add([pscustomobject]@{ Number = Get-BaselineNumber -Name $oldVersion; Source = "现役版本 $oldVersion" })
$history = Invoke-CountdownGit -Arguments @('log', '--all', '--format=', '--name-only', '-z', '--',
    ':(glob)assets/ui-references/*/*/*/manifest.json')
if ($history.ExitCode -ne 0) {
    throw "git log 失败：$($history.Error)"
}
foreach ($path in $history.Lines) {
    $number = Get-BaselineNumber -Name $path.Split('/')[4]
    if ($null -ne $number) {
        $usedNumbers.Add([pscustomobject]@{ Number = $number; Source = "已提交的 $path" })
    }
}
$worktrees = Invoke-CountdownGit -Arguments @('worktree', 'list', '--porcelain', '-z')
if ($worktrees.ExitCode -ne 0) {
    throw "git worktree list 失败：$($worktrees.Error)"
}
foreach ($line in $worktrees.Lines) {
    if (-not $line.StartsWith('worktree ', [System.StringComparison]::Ordinal)) {
        continue
    }
    $referenceRoot = Join-Path $line.Substring(9) 'assets/ui-references'
    if (-not (Test-Path -LiteralPath $referenceRoot -PathType Container)) {
        continue
    }
    foreach ($directory in @(Get-ChildItem -LiteralPath $referenceRoot -Directory | Get-ChildItem -Directory | Get-ChildItem -Directory)) {
        $number = Get-BaselineNumber -Name $directory.Name
        if ($null -ne $number) {
            $usedNumbers.Add([pscustomobject]@{ Number = $number; Source = "工作树目录 $($directory.FullName)" })
        }
    }
}
$highest = $usedNumbers | Sort-Object -Property Number -Descending | Select-Object -First 1
if ($newNumber -le $highest.Number) {
    throw ('v{0} 不大于已用的最大编号 v{1}（{2}）；基线编号跨分支全局分配，下一个可用编号是 v{3}。' -f
        $newNumber, $highest.Number, $highest.Source, ($highest.Number + 1))
}

# 旧版本后紧跟数字时不算命中，避免 v5 误改 v54。
$oldPattern = [regex]::new([regex]::Escape($oldVersion) + '(?![0-9])')
$grep = Invoke-CountdownGit -Arguments @('grep', '-l', '-z', '-F', '-e', $oldVersion, '--',
    '*.ps1', '*.psm1', '*.cs', ':(exclude)assets')
if ($grep.ExitCode -gt 1) {
    throw "git grep 失败：$($grep.Error)"
}
$changes = @(foreach ($relativePath in $grep.Lines) {
        $fullPath = Join-Path $repoRoot $relativePath
        $file = Read-Utf8File -Path $fullPath
        $count = $oldPattern.Matches($file.Text).Count
        if ($count -gt 0) {
            [pscustomobject]@{ Path = $relativePath; FullPath = $fullPath; HasBom = $file.HasBom; Text = $file.Text; Count = $count }
        }
    })
if ($changes.Count -eq 0) {
    throw "代码里找不到 $oldVersion，现役版本钉点可能已变，先更新本工具。"
}

'现役基线 {0} -> {1}（已用最大编号 v{2}，来自{3}）' -f $oldVersion, $Version, $highest.Number, $highest.Source
foreach ($change in $changes) {
    '  {0}  {1} 处' -f $change.Path, $change.Count
}

$written = [System.Collections.Generic.List[object]]::new()
foreach ($change in $changes) {
    if ($PSCmdlet.ShouldProcess($change.Path, "替换 $($change.Count) 处 $oldVersion")) {
        [System.IO.File]::WriteAllText($change.FullPath, $oldPattern.Replace($change.Text, $Version),
            [System.Text.UTF8Encoding]::new($change.HasBom))
        $written.Add($change)
    }
}
if ($written.Count -gt 0) {
    $leftovers = @($written | Where-Object { $oldPattern.IsMatch((Read-Utf8File -Path $_.FullPath).Text) })
    if ($leftovers.Count -gt 0) {
        throw "写回后仍有 ${oldVersion}：$(@($leftovers.Path) -join ', ')"
    }
    '已改 {0} 个文件共 {1} 处，代码里不再有 {2}。' -f $written.Count, ($written | Measure-Object -Property Count -Sum).Sum, $oldVersion
}
else {
    '未改文件。'
}

$documents = Invoke-CountdownGit -Arguments @('grep', '-l', '-z', '-F', '-e', $oldVersion, '--', '.',
    ':(exclude)assets', ':(exclude)*.ps1', ':(exclude)*.psm1', ':(exclude)*.cs')
if ($documents.ExitCode -gt 1) {
    throw "git grep 失败：$($documents.Error)"
}
if ($documents.Lines.Count -gt 0) {
    "仍提到 $oldVersion 的其他文件（叙述与聚合哈希需人工更新）："
    $documents.Lines | ForEach-Object { "  $_" }
}
'下一步：'
'  1. git add 上面的代码文件，运行 tools/Update-CountdownIntegrityHashes.ps1，再 git add Countdown.ReleaseContract.psm1。'
'  2. 按 README「本机验证」经启动器跑 Build，再单独跑 BaselineGenerate 生成 {0} 的双 manifest。' -f $Version
'  3. 经启动器跑 CSharp / Ui 复验，并在 DESIGN.md 记录新基线与聚合哈希。'
