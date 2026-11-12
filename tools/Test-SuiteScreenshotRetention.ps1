#requires -Version 7.0
<#
.SYNOPSIS
    在临时日志树验证套件截图保留最近 30 次运行、摘要长期保留及当前运行保护。
#>
[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
Import-Module (Join-Path $PSScriptRoot 'Countdown.SuiteRun.psm1') -Force
$pwsh = (Get-Command pwsh -CommandType Application -ErrorAction Stop | Select-Object -First 1).Source

function Assert-ScreenshotRetention {
    param([bool] $Condition, [string] $Message)
    if (-not $Condition) { throw "Suite screenshot retention 失败：$Message" }
}

function New-ScreenshotRetentionFixture {
    param(
        [Parameter(Mandatory)] [string] $CaseRoot,
        [Parameter(Mandatory)] [int] $HistoricalRunCount
    )

    $logRoot = Join-Path $CaseRoot 'logs/suite-runs'
    $captureRoot = Join-Path $CaseRoot 'logs/ui-captures'
    [void] [IO.Directory]::CreateDirectory($logRoot)
    [void] [IO.Directory]::CreateDirectory($captureRoot)
    $captures = [Collections.Generic.List[string]]::new()
    $summaries = [Collections.Generic.List[string]]::new()
    for ($index = 0; $index -lt $HistoricalRunCount; $index++) {
        $startedAt = [DateTimeOffset]::Parse('2026-01-01T00:00:00Z').AddMinutes($index)
        $runId = $startedAt.ToString('yyyyMMddTHHmmssfff') + '-' + $index.ToString('x8')
        $runPath = Join-Path $logRoot "$runId-run.json"
        $summaryPath = Join-Path $logRoot "$runId-summary.json"
        $run = [ordered]@{
            SchemaVersion = 1; RunId = $runId; RepositoryRoot = $repoRoot; LogDirectory = $logRoot
            StatePath = $runPath; SummaryPath = $summaryPath
            CancellationPath = (Join-Path $logRoot "$runId-cancel.request")
            Owner = [ordered]@{ ProcessId = 1; StartedAtUtc = '2000-01-01T00:00:00.0000000Z' }
            Status = 'Completed'; ExitCode = 0; Failure = $null; Closeout = $null
            StartedAtUtc = $startedAt.ToString('o'); FinishedAtUtc = $startedAt.AddMinutes(1).ToString('o'); UpdatedAtUtc = $null
            Stages = @()
        }
        [IO.File]::WriteAllText($runPath, (ConvertTo-Json -InputObject $run -Depth 8), [Text.UTF8Encoding]::new($false))
        [IO.File]::WriteAllText($summaryPath, ('[{"Stage":"History","Status":"Completed","ExitCode":0,"ElapsedSeconds":' + $index + '}]'), [Text.UTF8Encoding]::new($false))
        $summaries.Add($summaryPath)
        $capturePath = Join-Path $captureRoot ($startedAt.AddSeconds(30).ToString('yyyyMMddTHHmmssfff') + '-validate-only')
        [void] [IO.Directory]::CreateDirectory($capturePath)
        [IO.File]::WriteAllBytes((Join-Path $capturePath 'frame.png'), [byte[]] @(0x89, 0x50, 0x4E, 0x47, $index))
        $captures.Add($capturePath)
    }
    [pscustomobject]@{
        LogRoot = $logRoot
        CaptureRoot = $captureRoot
        Captures = $captures.ToArray()
        Summaries = $summaries.ToArray()
    }
}

function Invoke-ScreenshotRetentionProbe {
    param([Parameter(Mandatory)] [string] $LogRoot, [switch] $IncludeStageFailure)
    $spec = [ordered]@{
        Stage = 'RetentionProbe'; FilePath = $pwsh
        ArgumentList = @('-NoLogo', '-NoProfile', '-NonInteractive', '-Command', 'exit 0')
        Isolated = $false
    }
    $specs = @($spec)
    if ($IncludeStageFailure) {
        $specs += [ordered]@{
            Stage = 'OriginalFailure'; FilePath = $pwsh
            ArgumentList = @('-NoLogo', '-NoProfile', '-NonInteractive', '-Command', 'exit 7')
            Isolated = $false
        }
    }
    $initialize = {
        param($run)
        $captureRoot = Join-Path (Split-Path -Parent $run.LogDirectory) 'ui-captures'
        $activeCapture = Join-Path $captureRoot ([DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfff') + '-validate-only')
        [void] [IO.Directory]::CreateDirectory($activeCapture)
        [IO.File]::WriteAllBytes((Join-Path $activeCapture 'active.png'), [byte[]] @(1, 2, 3))
    }
    Invoke-SuiteRun -StageSpecs $specs -RepositoryRoot $repoRoot -LogDirectory $LogRoot -MaxParallel 1 -StageTimeoutSeconds 10 -Initialize $initialize
}

$testBase = 'D:/Codex/.codex/tmp/clock-countdown-screenshot-retention'
$testRoot = Join-Path $testBase ([guid]::NewGuid().ToString('N'))
[void] [IO.Directory]::CreateDirectory($testRoot)
try {
    $boundary = New-ScreenshotRetentionFixture -CaseRoot (Join-Path $testRoot 'boundary') -HistoricalRunCount 29
    $boundaryResult = Invoke-ScreenshotRetentionProbe -LogRoot $boundary.LogRoot
    Assert-ScreenshotRetention ($boundaryResult.Status -eq 'Completed' -and $boundaryResult.ExitCode -eq 0) '30 次运行边界的阶段应完成'
    Assert-ScreenshotRetention (@($boundary.Captures | Where-Object { -not [IO.Directory]::Exists($_) }).Count -eq 0) '恰好 30 次运行时不应删除历史截图'
    Assert-ScreenshotRetention (@(Get-ChildItem -LiteralPath $boundary.CaptureRoot -Directory).Count -eq 30) '恰好 30 次运行应保留全部截图目录'

    $retention = New-ScreenshotRetentionFixture -CaseRoot (Join-Path $testRoot 'retention') -HistoricalRunCount 30
    $oldestStamp = ([IO.Path]::GetFileName($retention.Captures[0]) -split '-')[0]
    $oldTrayCapture = Join-Path $retention.CaptureRoot "tray-menu-$($oldestStamp)Z-12345678"
    [void] [IO.Directory]::CreateDirectory($oldTrayCapture)
    [IO.File]::WriteAllBytes((Join-Path $oldTrayCapture 'tray.png'), [byte[]] @(4, 5, 6))
    $summaryHashesBefore = @($retention.Summaries | ForEach-Object { (Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash })
    $deploymentReference = Join-Path $testRoot 'deployment/local-validation.json'
    [void] [IO.Directory]::CreateDirectory((Split-Path -Parent $deploymentReference))
    $referencedRunId = [IO.Path]::GetFileName($retention.Summaries[0]).Replace('-summary.json', '')
    [IO.File]::WriteAllText($deploymentReference, (@{ SuiteRunId = $referencedRunId } | ConvertTo-Json), [Text.UTF8Encoding]::new($false))

    $result = Invoke-ScreenshotRetentionProbe -LogRoot $retention.LogRoot
    Assert-ScreenshotRetention ($result.Status -eq 'Completed' -and $result.ExitCode -eq 0 -and $result.Stages[0].Status -eq 'Completed') '超过 30 次运行时阶段结果仍应完成'
    Assert-ScreenshotRetention ($result.ScreenshotRetention.Status -eq 'Completed' -and $result.ScreenshotRetention.DeletedDirectoryCount -eq 2) '最早普通截图与 tray-menu 截图均应经受管回收完成'
    Assert-ScreenshotRetention (-not [IO.Directory]::Exists($retention.Captures[0])) '第 31 次运行结束后应删除最早一次截图'
    Assert-ScreenshotRetention (-not [IO.Directory]::Exists($oldTrayCapture)) '最早一次运行的 tray-menu 截图也应受同一留存边界约束'
    Assert-ScreenshotRetention (@($retention.Captures[1..29] | Where-Object { -not [IO.Directory]::Exists($_) }).Count -eq 0) '最近 30 次运行内的历史截图必须保留'
    Assert-ScreenshotRetention (@(Get-ChildItem -LiteralPath $retention.CaptureRoot -Directory).Count -eq 30) '超过边界后应只保留最近 30 次运行的截图目录'
    Assert-ScreenshotRetention (@(Get-ChildItem -LiteralPath $retention.LogRoot -Filter '*-summary.json' -File).Count -eq 31) '运行摘要必须全部长期保留'
    Assert-ScreenshotRetention ([IO.File]::Exists($retention.Summaries[0])) '部署记录引用的最早运行摘要不得随截图清理'
    $summaryHashesAfter = @($retention.Summaries | ForEach-Object { (Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash })
    Assert-ScreenshotRetention (($summaryHashesBefore -join ',') -eq ($summaryHashesAfter -join ',')) '截图清理不得改写既有运行摘要'

    $failure = New-ScreenshotRetentionFixture -CaseRoot (Join-Path $testRoot 'failure') -HistoricalRunCount 30
    $lockedPath = Join-Path $failure.Captures[0] 'frame.png'
    $lockedStream = [IO.File]::Open($lockedPath, [IO.FileMode]::Open, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
    try {
        $failureResult = Invoke-ScreenshotRetentionProbe -LogRoot $failure.LogRoot -IncludeStageFailure
        Assert-ScreenshotRetention ($failureResult.Status -eq 'Failed' -and $failureResult.ExitCode -eq 1) '清理失败不得覆盖原有整轮失败结果'
        Assert-ScreenshotRetention ($failureResult.Stages[0].Status -eq 'Completed' -and $failureResult.Stages[0].ExitCode -eq 0) '清理失败不得改变已成功阶段'
        Assert-ScreenshotRetention ($failureResult.Stages[1].Status -eq 'Failed' -and $failureResult.Stages[1].ExitCode -eq 7) '清理失败不得改变原失败阶段或退出码'
        Assert-ScreenshotRetention ($failureResult.Failure -eq '阶段 OriginalFailure 未通过。') '清理失败不得覆盖原阶段失败说明'
        Assert-ScreenshotRetention ($null -ne $failureResult.ScreenshotRetention -and $failureResult.ScreenshotRetention.Status -eq 'Failed') '清理失败必须写入运行记录'
        Assert-ScreenshotRetention (-not [string]::IsNullOrWhiteSpace([string] $failureResult.ScreenshotRetention.Error)) '运行记录必须包含清理失败原因'
        Assert-ScreenshotRetention ([IO.Directory]::Exists($failure.Captures[0])) '被占用而删除失败的截图目录必须保留'
        $persistedFailure = Get-Content -LiteralPath $failureResult.StatePath -Raw -Encoding UTF8 | ConvertFrom-Json -DateKind String
        Assert-ScreenshotRetention ($persistedFailure.ScreenshotRetention.Status -eq 'Failed') '清理失败说明必须持久化到 run.json'
    }
    finally {
        $lockedStream.Dispose()
    }

    Write-Output 'PASS: 30-run boundary, older-capture deletion, active/recent captures, all summaries, deployment-reference protection, cleanup-failure isolation'
}
finally {
    if ([IO.Directory]::Exists($testRoot) -and [IO.Path]::GetFullPath($testRoot).StartsWith([IO.Path]::GetFullPath($testBase) + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        [IO.Directory]::Delete($testRoot, $true)
    }
}
