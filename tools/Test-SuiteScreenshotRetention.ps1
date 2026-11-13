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
    param(
        [Parameter(Mandatory)] [string] $LogRoot,
        [switch] $IncludeStageFailure,
        [scriptblock] $ConfirmScreenshotRetention,
        [string] $ManagedRecyclePath = $script:fixtureManagedRecyclePath
    )
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
    $confirmationArgument = @{}
    if ($null -ne $ConfirmScreenshotRetention) { $confirmationArgument.ConfirmScreenshotRetention = $ConfirmScreenshotRetention }
    if (-not [string]::IsNullOrWhiteSpace($ManagedRecyclePath)) { $confirmationArgument.ManagedRecyclePath = $ManagedRecyclePath }
    Invoke-SuiteRun -StageSpecs $specs -RepositoryRoot $repoRoot -LogDirectory $LogRoot -MaxParallel 1 -StageTimeoutSeconds 10 -Initialize $initialize @confirmationArgument
}

$testBase = 'D:/Codex/.codex/tmp/clock-countdown-screenshot-retention'
$testRoot = Join-Path $testBase ([guid]::NewGuid().ToString('N'))
[void] [IO.Directory]::CreateDirectory($testRoot)
$script:fixtureManagedRecyclePath = Join-Path $testRoot 'Invoke-ManagedRecycle.Fixture.ps1'
$fixtureManagedRecycleSource = @'
#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [ValidateSet('Check', 'Plan', 'Apply')] [string] $Mode,
    [string] $TargetPath,
    [string] $Purpose,
    [string] $Authorization,
    [string] $PlanPath,
    [string] $ApprovedPlanSha256,
    [switch] $ConfirmRecycle
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$fixtureRoot = [IO.Path]::GetFullPath($PSScriptRoot).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar

function Assert-FixtureTarget {
    param([string] $Path)
    if ([string]::IsNullOrWhiteSpace($Path)) { throw 'Fixture recycle requires a target.' }
    $fullPath = [IO.Path]::GetFullPath($Path)
    if (-not $fullPath.StartsWith($fixtureRoot, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Fixture recycle target escapes the fixture root: $fullPath"
    }
    if (-not [IO.Directory]::Exists($fullPath)) { throw "Fixture recycle target is missing: $fullPath" }
    return $fullPath
}

if ($Mode -eq 'Check') {
    $target = Assert-FixtureTarget -Path $TargetPath
    foreach ($file in [IO.Directory]::EnumerateFiles($target, '*', [IO.SearchOption]::AllDirectories)) {
        $stream = [IO.File]::Open($file, [IO.FileMode]::Open, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
        $stream.Dispose()
    }
    [pscustomobject]@{ status = 'checked' } | ConvertTo-Json -Compress
    exit 0
}

if ($Mode -eq 'Plan') {
    $target = Assert-FixtureTarget -Path $TargetPath
    $planDirectory = Join-Path $PSScriptRoot 'fixture-recycle-plans'
    [void] [IO.Directory]::CreateDirectory($planDirectory)
    $newPlanPath = Join-Path $planDirectory ([guid]::NewGuid().ToString('N') + '.json')
    [IO.File]::WriteAllText($newPlanPath, (@{ TargetPath = $target } | ConvertTo-Json -Compress), [Text.UTF8Encoding]::new($false))
    $planSha256 = (Get-FileHash -LiteralPath $newPlanPath -Algorithm SHA256).Hash
    [pscustomobject]@{ status = 'planned'; planPath = $newPlanPath; planSha256 = $planSha256 } | ConvertTo-Json -Compress
    exit 0
}

if (-not $ConfirmRecycle) { throw 'Fixture recycle Apply requires ConfirmRecycle.' }
if (-not [IO.File]::Exists($PlanPath)) { throw "Fixture recycle plan is missing: $PlanPath" }
$actualPlanSha256 = (Get-FileHash -LiteralPath $PlanPath -Algorithm SHA256).Hash
if ($actualPlanSha256 -cne $ApprovedPlanSha256) { throw 'Fixture recycle plan hash differs from approval.' }
$plan = Get-Content -LiteralPath $PlanPath -Raw -Encoding UTF8 | ConvertFrom-Json
$target = Assert-FixtureTarget -Path ([string] $plan.TargetPath)
$recycleRoot = Join-Path $PSScriptRoot 'fixture-recycle'
[void] [IO.Directory]::CreateDirectory($recycleRoot)
$destination = Join-Path $recycleRoot ([guid]::NewGuid().ToString('N') + '-' + [IO.Path]::GetFileName($target))
[IO.Directory]::Move($target, $destination)
[pscustomobject]@{ status = 'recycled'; targetStillExists = [IO.Directory]::Exists($target) } | ConvertTo-Json -Compress
exit 0
'@
[IO.File]::WriteAllText($script:fixtureManagedRecyclePath, $fixtureManagedRecycleSource, [Text.UTF8Encoding]::new($false))
try {
    $boundary = New-ScreenshotRetentionFixture -CaseRoot (Join-Path $testRoot 'boundary') -HistoricalRunCount 29
    $boundaryResult = Invoke-ScreenshotRetentionProbe -LogRoot $boundary.LogRoot
    Assert-ScreenshotRetention ($boundaryResult.Status -eq 'Completed' -and $boundaryResult.ExitCode -eq 0) '30 次运行边界的阶段应完成'
    Assert-ScreenshotRetention (@($boundary.Captures | Where-Object { -not [IO.Directory]::Exists($_) }).Count -eq 0) '恰好 30 次运行时不应删除历史截图'
    Assert-ScreenshotRetention (@(Get-ChildItem -LiteralPath $boundary.CaptureRoot -Directory).Count -eq 30) '恰好 30 次运行应保留全部截图目录'

    $unconfirmed = New-ScreenshotRetentionFixture -CaseRoot (Join-Path $testRoot 'unconfirmed') -HistoricalRunCount 30
    $unconfirmedResult = Invoke-ScreenshotRetentionProbe -LogRoot $unconfirmed.LogRoot
    Assert-ScreenshotRetention ($unconfirmedResult.Status -eq 'Completed' -and $unconfirmedResult.ExitCode -eq 0) '首次自动清理未确认不得改变阶段结果'
    Assert-ScreenshotRetention ($unconfirmedResult.ScreenshotRetention.Status -eq 'ConfirmationRequired') '首次自动清理未确认必须闭锁'
    Assert-ScreenshotRetention ($unconfirmedResult.ScreenshotRetention.DeletedDirectoryCount -eq 0) '首次自动清理未确认不得报告已删除'
    Assert-ScreenshotRetention ([IO.Directory]::Exists($unconfirmed.Captures[0])) '首次自动清理未确认必须保留原目标'
    Assert-ScreenshotRetention (@(Get-ChildItem -LiteralPath $unconfirmed.CaptureRoot -Directory).Count -eq 31) '首次自动清理未确认不得删除任何截图目录'
    Assert-ScreenshotRetention ($unconfirmedResult.ScreenshotRetention.ConfirmationPlan.Targets.Count -eq 1) '首次自动清理计划必须列出精确目标'
    Assert-ScreenshotRetention ($unconfirmedResult.ScreenshotRetention.ConfirmationPlan.Targets[0].Path -eq $unconfirmed.Captures[0]) '首次自动清理计划路径必须匹配实际旧截图'
    Assert-ScreenshotRetention ($unconfirmedResult.ScreenshotRetention.ConfirmationPlan.Targets[0].Bytes -eq 5 -and $unconfirmedResult.ScreenshotRetention.ConfirmationPlan.TotalBytes -eq 5) '首次自动清理计划必须绑定精确字节数'

    $missingHost = New-ScreenshotRetentionFixture -CaseRoot (Join-Path $testRoot 'missing-host') -HistoricalRunCount 30
    $missingHostPath = Join-Path $testRoot 'absent-host/Invoke-ManagedRecycle.ps1'
    $missingHostResult = Invoke-ScreenshotRetentionProbe -LogRoot $missingHost.LogRoot -ManagedRecyclePath $missingHostPath -ConfirmScreenshotRetention { param($plan) $plan.PlanSha256 }
    Assert-ScreenshotRetention ($missingHostResult.Status -eq 'Completed' -and $missingHostResult.Stages[0].Status -eq 'Completed') '宿主回收入口缺失不得改变已完成阶段'
    Assert-ScreenshotRetention ($missingHostResult.ScreenshotRetention.Status -eq 'Failed') '宿主回收入口缺失必须失败闭锁'
    Assert-ScreenshotRetention ([string] $missingHostResult.ScreenshotRetention.Error -like '*受管回收入口不存在*') '宿主回收入口缺失必须保留明确错误'
    Assert-ScreenshotRetention ([IO.Directory]::Exists($missingHost.Captures[0])) '宿主回收入口缺失不得移动原目标'
    $persistedMissingHost = Get-Content -LiteralPath $missingHostResult.StatePath -Raw -Encoding UTF8 | ConvertFrom-Json -DateKind String
    Assert-ScreenshotRetention ($persistedMissingHost.ScreenshotRetention.Status -eq 'Failed' -and [string] $persistedMissingHost.ScreenshotRetention.Error -like '*受管回收入口不存在*') '宿主回收入口缺失错误必须写入运行记录'

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

    $expectedPlanPaths = @($retention.Captures[0], $oldTrayCapture) | Sort-Object
    $confirmRetention = {
        param($plan)
        $actualPlanPaths = @($plan.Targets.Path | Sort-Object)
        Assert-ScreenshotRetention (($actualPlanPaths -join '|') -eq ($expectedPlanPaths -join '|')) '当场确认必须绑定精确目标列表'
        Assert-ScreenshotRetention ($plan.Targets.Count -eq 2 -and $plan.TotalBytes -eq 8) '当场确认必须绑定两个目标的精确总字节数'
        Assert-ScreenshotRetention (@($plan.Targets | Where-Object Path -EQ $retention.Captures[0])[0].Bytes -eq 5) '普通截图计划必须绑定 5 字节'
        Assert-ScreenshotRetention (@($plan.Targets | Where-Object Path -EQ $oldTrayCapture)[0].Bytes -eq 3) 'tray-menu 截图计划必须绑定 3 字节'
        return $plan.PlanSha256
    }.GetNewClosure()
    $result = Invoke-ScreenshotRetentionProbe -LogRoot $retention.LogRoot -ConfirmScreenshotRetention $confirmRetention
    Assert-ScreenshotRetention ($result.Status -eq 'Completed' -and $result.ExitCode -eq 0 -and $result.Stages[0].Status -eq 'Completed') '超过 30 次运行时阶段结果仍应完成'
    Assert-ScreenshotRetention ($result.ScreenshotRetention.Status -eq 'Completed' -and $result.ScreenshotRetention.DeletedDirectoryCount -eq 2) "最早普通截图与 tray-menu 截图均应经受管回收完成：$($result.ScreenshotRetention.Error)"
    Assert-ScreenshotRetention (@(Get-ChildItem -LiteralPath (Join-Path $testRoot 'fixture-recycle') -File -Recurse).Count -eq 2) 'fixture 必须把两个目标移动到临时回收目录而非永久删除'
    Assert-ScreenshotRetention (-not [IO.Directory]::Exists($retention.Captures[0])) '第 31 次运行结束后应删除最早一次截图'
    Assert-ScreenshotRetention (-not [IO.Directory]::Exists($oldTrayCapture)) '最早一次运行的 tray-menu 截图也应受同一留存边界约束'
    Assert-ScreenshotRetention (@($retention.Captures[1..29] | Where-Object { -not [IO.Directory]::Exists($_) }).Count -eq 0) '最近 30 次运行内的历史截图必须保留'
    Assert-ScreenshotRetention (@(Get-ChildItem -LiteralPath $retention.CaptureRoot -Directory).Count -eq 30) '超过边界后应只保留最近 30 次运行的截图目录'
    Assert-ScreenshotRetention (@(Get-ChildItem -LiteralPath $retention.LogRoot -Filter '*-summary.json' -File).Count -eq 31) '运行摘要必须全部长期保留'
    Assert-ScreenshotRetention ([IO.File]::Exists($retention.Summaries[0])) '部署记录引用的最早运行摘要不得随截图清理'
    $summaryHashesAfter = @($retention.Summaries | ForEach-Object { (Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash })
    Assert-ScreenshotRetention (($summaryHashesBefore -join ',') -eq ($summaryHashesAfter -join ',')) '截图清理不得改写既有运行摘要'
    Assert-ScreenshotRetention ($result.ScreenshotRetention.FirstRunConfirmation.PlanSha256 -eq $result.ScreenshotRetention.ConfirmationPlan.PlanSha256) '确认后的运行必须记录精确计划回执'

    $regularResult = Invoke-ScreenshotRetentionProbe -LogRoot $retention.LogRoot
    Assert-ScreenshotRetention ($regularResult.ScreenshotRetention.Status -eq 'Completed' -and $regularResult.ScreenshotRetention.DeletedDirectoryCount -eq 1) '首次确认后应按常规留存自动清理'
    Assert-ScreenshotRetention (@(Get-ChildItem -LiteralPath (Join-Path $testRoot 'fixture-recycle') -File -Recurse).Count -eq 3) '首次确认后的常规清理也必须移动到 fixture 回收目录'
    Assert-ScreenshotRetention (-not [IO.Directory]::Exists($retention.Captures[1])) '首次确认后的下一轮应清理新的越界截图'
    Assert-ScreenshotRetention (@($retention.Captures[2..29] | Where-Object { -not [IO.Directory]::Exists($_) }).Count -eq 0) '首次确认后的常规清理仍须保留最近 30 次运行'
    Assert-ScreenshotRetention ([IO.File]::Exists($result.StatePath)) '后续常规清理不得删除首次确认运行记录'
    $persistedFirstConfirmation = Get-Content -LiteralPath $result.StatePath -Raw -Encoding UTF8 | ConvertFrom-Json -DateKind String
    Assert-ScreenshotRetention ($persistedFirstConfirmation.ScreenshotRetention.FirstRunConfirmation.PlanSha256 -eq $result.ScreenshotRetention.FirstRunConfirmation.PlanSha256) '后续常规清理不得改写首次确认历史'

    $changedPlan = New-ScreenshotRetentionFixture -CaseRoot (Join-Path $testRoot 'changed-plan') -HistoricalRunCount 30
    $changedTarget = $changedPlan.Captures[0]
    $changeAfterConfirmation = {
        param($plan)
        [IO.File]::AppendAllBytes((Join-Path $changedTarget 'frame.png'), [byte[]] @(9))
        return $plan.PlanSha256
    }.GetNewClosure()
    $changedPlanResult = Invoke-ScreenshotRetentionProbe -LogRoot $changedPlan.LogRoot -ConfirmScreenshotRetention $changeAfterConfirmation
    Assert-ScreenshotRetention ($changedPlanResult.Status -eq 'Completed' -and $changedPlanResult.Stages[0].Status -eq 'Completed') '确认后字节变化不得改变阶段结果'
    Assert-ScreenshotRetention ($changedPlanResult.ScreenshotRetention.Status -eq 'Failed') '确认后目标字节变化必须让清理闭锁'
    Assert-ScreenshotRetention ([string] $changedPlanResult.ScreenshotRetention.Error -like '*精确目标或字节数*') '字节绑定失败必须明确报告计划变化'
    Assert-ScreenshotRetention ([IO.Directory]::Exists($changedTarget)) '确认后字节变化不得继续 Apply'

    $failure = New-ScreenshotRetentionFixture -CaseRoot (Join-Path $testRoot 'failure') -HistoricalRunCount 30
    $lockedPath = Join-Path $failure.Captures[0] 'frame.png'
    $lockedStream = [IO.File]::Open($lockedPath, [IO.FileMode]::Open, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
    try {
        $failureResult = Invoke-ScreenshotRetentionProbe -LogRoot $failure.LogRoot -IncludeStageFailure -ConfirmScreenshotRetention { param($plan) $plan.PlanSha256 }
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

    Write-Output 'PASS: isolated fixture recycle, missing-host failure, 30-run boundary, first-apply confirmation lock, exact path/byte binding, confirmed and subsequent regular retention, active/recent captures, all summaries, deployment-reference protection, cleanup-failure isolation'
}
finally {
    if ([IO.Directory]::Exists($testRoot) -and [IO.Path]::GetFullPath($testRoot).StartsWith([IO.Path]::GetFullPath($testBase) + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        [IO.Directory]::Delete($testRoot, $true)
    }
}
