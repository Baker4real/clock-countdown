#requires -Version 7.0
<#
.SYNOPSIS
运行无窗口 Core 分段的红→绿内环；正式验收仍使用完整 Core 阶段。
.DESCRIPTION
只开放使用假端口、临时目录的 Installer、InstallStatus、DeployEntry 段。
复用 Countdown.Core.Tests.ps1 的公开测试辅助函数，不开窗口，不读写真实安装、计划任务或办公室设备。
断言失败或段异常均以非零退出码结束。
.EXAMPLE
pwsh -NoLogo -NoProfile -File ./tools/Invoke-CountdownCoreSegment.ps1 -Segment Installer
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateSet('Installer', 'InstallStatus', 'DeployEntry')]
    [string] $Segment,
    [string] $RepositoryRoot = (Join-Path $PSScriptRoot '..')
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)

$RepositoryRoot = (Resolve-Path -LiteralPath $RepositoryRoot).Path
$corePath = Join-Path $RepositoryRoot 'Countdown.Core.Tests.ps1'
$segmentPath = Join-Path $RepositoryRoot "Countdown.Core.Tests.$Segment.ps1"
foreach ($path in @($corePath, $segmentPath)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "缺少 Core 分段文件：$path" }
}

$failures = [System.Collections.Generic.List[string]]::new()
$script:assertions = 0
$script:segmentClock = [System.Diagnostics.Stopwatch]::StartNew()
$tokens = $null
$errors = $null
$coreAst = [System.Management.Automation.Language.Parser]::ParseFile($corePath, [ref] $tokens, [ref] $errors)
if ($errors.Count -gt 0) { throw "Countdown.Core.Tests.ps1 解析失败：$($errors.Message -join '；')" }
$wanted = @('Assert-Equal', 'Remove-CountdownIsolatedTestTree', 'Receive-CountdownTestThreadJob',
    'Start-CountdownTestLockHolder', 'Stop-CountdownTestLockHolder', 'Test-CountdownTestLockFree', 'Write-TestSegmentTiming')
foreach ($definition in @($coreAst.EndBlock.Statements | Where-Object {
            $_ -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $_.Name -in $wanted
        })) {
    . ([scriptblock]::Create($definition.Extent.Text))
}
try {
    . $segmentPath
    if ($failures.Count -gt 0) {
        foreach ($failure in $failures) { [Console]::Error.WriteLine("[core-segment] failure: $failure") }
        throw "Core 分段 $Segment 有 $($failures.Count) 条断言失败。"
    }
    [pscustomobject]@{
        Segment = $Segment
        Assertions = $script:assertions
        Failed = 0
        JobsLeft = @(Get-Job).Count
        OfficialGateRequired = $true
    } | ConvertTo-Json -Compress
}
catch {
    [Console]::Error.WriteLine("[core-segment] $Segment failed: $($_.Exception.Message)")
    exit 1
}
