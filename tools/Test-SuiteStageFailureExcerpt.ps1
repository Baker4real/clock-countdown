#requires -Version 7.0
# 失败阶段摘要：从 err 日志最后一个 PowerShell 错误块取行，同一文件里 UTF-8 与 936 混写时逐行解码，
# 没有错误块或阶段以 75（参考基线待重拍）退出时取末尾，超长行截断，主日志缺失时退到隔离启动器日志。
# 不开窗口，只在临时目录里读写。
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Import-Module (Join-Path $PSScriptRoot 'Countdown.SuiteRun.psm1') -Force
$workRoot = Join-Path ([System.IO.Path]::GetTempPath()) ('minmin-suite-excerpt-' + [guid]::NewGuid().ToString('N'))
[void] [System.IO.Directory]::CreateDirectory($workRoot)
$utf8 = [System.Text.UTF8Encoding]::new($false)
$gbk = [System.Text.Encoding]::GetEncoding(936)
$failures = [System.Collections.Generic.List[string]]::new()

function Write-LogBytes {
    param([string] $Name, [object[]] $Parts)
    $path = Join-Path $workRoot $Name
    $bytes = [System.Collections.Generic.List[byte]]::new()
    foreach ($part in $Parts) { $bytes.AddRange([byte[]] $part) }
    [System.IO.File]::WriteAllBytes($path, $bytes.ToArray())
    return $path
}

function New-Stage {
    param([string] $Stderr, [string] $LauncherStderr = $null)
    [pscustomobject]@{ Stage = 'Fake'; Stderr = $Stderr; LauncherStderr = $LauncherStderr }
}

try {
    # 1) UTF-8 前文 + 两个 936 错误块：取最后一块，从块头一行起，中文按 936 还原。
    $concise = {
        param([int] $Line, [string] $Message)
        "Exception: D:\repo\Countdown.CSharp.Tests.ps1:$Line`r`nLine |`r`n$Line |          throw `$detail`r`n     |          ~~~~~~~~~~~~~~`r`n     | $Message`r`n"
    }
    $mixed = Write-LogBytes 'mixed.err.log' @(
        $utf8.GetBytes("REMINDER_BATCH_RETRY_COMMITS: 1`n预热完成（UTF-8）`n"),
        $gbk.GetBytes((& $concise 100 '第一处失败')),
        $utf8.GetBytes("中间输出（UTF-8）`n"),
        $gbk.GetBytes((& $concise 4218 '经 PowerShell adapter 运行 C# 应用 --validate-only 失败，退出码：1。')))
    $excerpt = Get-SuiteStageFailureExcerpt -Stage (New-Stage $mixed)
    if ($null -eq $excerpt) { $failures.Add('有错误块的日志应返回摘要') }
    else {
        if ($excerpt.Lines[0] -ne 'Exception: D:\repo\Countdown.CSharp.Tests.ps1:4218') { $failures.Add("摘要应从最后一个错误块的块头开始，实际首行：$($excerpt.Lines[0])") }
        if (-not ($excerpt.Lines -match '经 PowerShell adapter 运行 C# 应用 --validate-only 失败')) { $failures.Add("936 写的中文消息应还原：$($excerpt.Lines -join ' / ')") }
        if ($excerpt.Lines -match '第一处失败') { $failures.Add('摘要不应回到更早的错误块') }
        if ($excerpt.Path -ne $mixed) { $failures.Add("摘要应来自主 err 日志：$($excerpt.Path)") }
    }

    # 2) 没有错误块：取末尾 MaxLines 行；超长行截断。
    $tailLines = @(1..20 | ForEach-Object { "第 $_ 行" }) + ('x' * 500)
    $tail = Write-LogBytes 'tail.err.log' @($utf8.GetBytes(($tailLines -join "`n") + "`n"))
    $excerpt = Get-SuiteStageFailureExcerpt -Stage (New-Stage $tail) -MaxLines 5 -MaxLineLength 300
    if ($null -eq $excerpt -or $excerpt.Lines.Count -ne 5) { $failures.Add('没有错误块时应取末尾 5 行') }
    else {
        if ($excerpt.Lines[0] -ne '第 17 行') { $failures.Add("末尾摘要首行应为第 17 行，实际：$($excerpt.Lines[0])") }
        if ($excerpt.Lines[4].Length -ne 301 -or -not $excerpt.Lines[4].EndsWith('…')) { $failures.Add("超长行应截到 300 字符并加省略号，实际长度 $($excerpt.Lines[4].Length)") }
    }

    # 3) 主日志为空、隔离启动器日志有内容：退到启动器日志；两个都没有时返回空。
    $empty = Write-LogBytes 'empty.err.log' @($utf8.GetBytes("`r`n`r`n"))
    $launcher = Write-LogBytes 'launcher.iso.err.log' @($utf8.GetBytes("Start-IsolatedSuiteProcess: 隔离桌面创建失败`n"))
    $excerpt = Get-SuiteStageFailureExcerpt -Stage (New-Stage $empty $launcher)
    if ($null -eq $excerpt -or $excerpt.Path -ne $launcher -or $excerpt.Lines[0] -ne 'Start-IsolatedSuiteProcess: 隔离桌面创建失败') { $failures.Add('主日志为空时应取隔离启动器日志') }
    if ($null -ne (Get-SuiteStageFailureExcerpt -Stage (New-Stage (Join-Path $workRoot 'missing.err.log')))) { $failures.Add('没有日志时应返回空') }

    # 4) 以 75 退出（参考基线待重拍）：延后清单写在末尾，取末尾，不回到更早的错误块。
    $pending = Write-LogBytes 'pending.err.log' @(
        $gbk.GetBytes((& $concise 512 '子进程预期失败的回显')),
        $utf8.GetBytes("[test-segment] name=Report seconds=1.000`n参考基线待重拍：Ui 其余断言已全部通过；以下 1 项因视觉源码、EXE 或实拍与现役基线不一致而延后：`n  - CrossInterfaceReferenceBaselinesCurrent`n按 README「本机验证」重拍基线后重跑本阶段；延后不改变本阶段和整轮的失败结论。`n"))
    $excerpt = Get-SuiteStageFailureExcerpt -Stage ([pscustomobject]@{ Stage = 'Ui'; ExitCode = 75; Stderr = $pending; LauncherStderr = $null }) -MaxLines 3
    if ($null -eq $excerpt -or $excerpt.Lines.Count -ne 3 -or -not $excerpt.Lines[0].StartsWith('参考基线待重拍：Ui ') -or
        ($excerpt.Lines -match '子进程预期失败')) {
        $failures.Add("退出码 75 的摘要应是末尾的延后清单：$(if ($null -ne $excerpt) { $excerpt.Lines -join ' / ' })")
    }
}
finally {
    foreach ($file in [System.IO.Directory]::GetFiles($workRoot)) { [System.IO.File]::Delete($file) }
    [System.IO.Directory]::Delete($workRoot)
}

if ($failures.Count -gt 0) {
    throw "suite-stage-failure-excerpt 失败：$($failures -join '；')"
}
Write-Output 'suite-stage-failure-excerpt: PASS（混合编码取最后错误块、末尾回退与截断、启动器日志回退、75 取末尾延后清单）'
