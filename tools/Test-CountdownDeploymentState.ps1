#requires -Version 7.0
[CmdletBinding()]
param(
    [switch] $Json,
    [switch] $SkipDiagnosticsWhenUnavailable,
    [string] $ProjectionScriptPath = 'D:/Codex/.codex/scripts/Read-BoundedProjection.ps1'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
$repo = Split-Path -Parent $PSScriptRoot
$entry = Join-Path $PSScriptRoot 'Get-CountdownDeploymentState.ps1'
$testRoot = Join-Path $repo "logs/deployment-state-test-$([guid]::NewGuid().ToString('N'))"
[void] (New-Item -ItemType Directory -Path $testRoot)
$commit = 'a' * 40
$assertions = 0
$skipped = [Collections.Generic.List[string]]::new()

function Assert-State([bool] $Condition, [string] $Name) {
    $script:assertions++
    if (-not $Condition) { throw $Name }
}

function Read-State(
    [switch] $Diagnostics,
    [int] $HistoryLimit = 5,
    [int] $MaxTotalBytes = 6000,
    [int] $ExpectedExitCode = 0,
    [string] $Projector = $ProjectionScriptPath
) {
    $arguments = @('-NoLogo', '-NoProfile', '-File', $entry, '-EvidenceRoot', $testRoot,
        '-Commit', $commit, '-HistoryLimit', [string] $HistoryLimit, '-MaxTotalBytes', [string] $MaxTotalBytes)
    if ($Diagnostics) { $arguments += @('-IncludeDiagnostics', '-ProjectionScriptPath', $Projector) }
    $process = [Diagnostics.Process]::new()
    $process.StartInfo.FileName = (Get-Command pwsh -CommandType Application | Select-Object -First 1).Source
    $process.StartInfo.UseShellExecute = $false
    $process.StartInfo.CreateNoWindow = $true
    $process.StartInfo.RedirectStandardOutput = $true
    $process.StartInfo.RedirectStandardError = $true
    $process.StartInfo.StandardOutputEncoding = [Text.UTF8Encoding]::new($false)
    $process.StartInfo.StandardErrorEncoding = [Text.UTF8Encoding]::new($false)
    foreach ($argument in $arguments) { [void] $process.StartInfo.ArgumentList.Add($argument) }
    try {
        [void] $process.Start()
        $stdout = $process.StandardOutput.ReadToEndAsync()
        $stderr = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit(30000)) { throw '状态查询超过 30 秒。' }
        $raw = $stdout.GetAwaiter().GetResult()
        $errorText = $stderr.GetAwaiter().GetResult()
        Assert-State ($process.ExitCode -eq $ExpectedExitCode) "状态入口退出码应为 $ExpectedExitCode，实际 $($process.ExitCode)：$errorText"
        Assert-State ([string]::IsNullOrWhiteSpace($errorText)) '状态入口须保持 stderr 干净。'
        Assert-State ([Text.Encoding]::UTF8.GetByteCount($raw) -le $MaxTotalBytes) '状态 JSON 超过总字节预算。'
        return [pscustomobject]@{ Raw=$raw; Value=($raw | ConvertFrom-Json -DateKind String) }
    }
    finally { $process.Dispose() }
}

function Write-FixtureJson([string] $RunRoot, [string] $Name, $Value) {
    [IO.File]::WriteAllText((Join-Path $RunRoot $Name), ($Value | ConvertTo-Json -Depth 10 -Compress), [Text.UTF8Encoding]::new($false))
}

function New-CompleteRun([string] $RunId) {
    $runRoot = Join-Path $testRoot "$($commit.Substring(0, 7))/$RunId"
    [void] (New-Item -ItemType Directory -Path $runRoot -Force)
    Write-FixtureJson $runRoot 'remote-deployment-success.json' ([ordered]@{
        SchemaVersion=1; Valid=$true; CoreTests=1248; CSharpTests=992; UiTests=94; UiValid=$true; AdapterUiValidated=$true
        TaskState='Ready'; LastTaskResult=0; RuntimeProcessId=123; RuntimeStage='window-ready'; CalendarState='Ready'; AcceptanceState='finalized-ready'
        SettingsHashBefore=('D'*64); SettingsHashAfter=('D'*64); SettingsLengthBefore=100; SettingsLengthAfter=100
        SettingsMtimeUtcBefore='2026-10-09T00:00:00.0000000Z'; SettingsMtimeUtcAfter='2026-10-09T00:00:00.0000000Z'; SettingsUnchanged=$true
    })
    Write-FixtureJson $runRoot 'deployment-binding.json' ([ordered]@{
        SchemaVersion=1; DeploymentBindingSha256=('B'*64); TokenSha256=('A'*64); RuntimeProcessId=123
    })
    Write-FixtureJson $runRoot 'post-deployment-independent-readback.json' ([ordered]@{
        SchemaVersion=1; Valid=$true; HostIdentityMatch=$true; UserIdentityMatch=$true; RemoteMachineNameSha256=('E'*64); RemoteUserNameSha256=('F'*64)
        TaskState='Ready'; LastTaskResult=0; TaskExecuteMatch=$true; TaskArgumentsMatch=$true; TaskWorkingDirectoryMatch=$true
        StartupSchemaVersion=1; StartupAcceptanceState='finalized'; RuntimeSchemaVersion=4; RuntimeStage='window-ready'; AcceptanceSchemaVersion=1; AcceptanceStage='finalized-ready'
        TokenSha256=('A'*64); TokenMatch=$true; RuntimeProcessId=123; AcceptanceProcessId=123; ProcessIdMatch=$true
        RuntimeSessionId=1; AcceptanceSessionId=1; ProcessSessionId=1; SessionIdMatch=$true; ApplicationPathMatch=$true; ProcessExecutablePathMatch=$true
        ExecutableSha256=('C'*64); ExecutableHashMatch=$true; SettingsSha256=('D'*64); SettingsHashMatch=$true
        SettingsLength=100; SettingsMtimeUtc='2026-10-09T00:00:00.0000000Z'; SettingsUnchanged=$true
    })
    $ids = @('deployment-package', 'remote-install-script', 'remote-deployment-binding', 'post-deployment-independent-readback-script', 'post-deployment-independent-readback')
    $methods = @('scp', 'scp', 'ssh', 'scp', 'ssh')
    $attempts = @(for ($i=0; $i -lt $ids.Count; $i++) {
        [ordered]@{ id=$ids[$i]; method=$methods[$i]; status='succeeded'; exitCode=0; transferStarted=$true; bytesVerified=$true
            metrics=@{ bytes=100; elapsed_seconds=1; mib_per_second=0.000095; mbps=0.0008 } }
    })
    Write-FixtureJson $runRoot 'transfer-attempts.json' (@{ transferAttemptsVersion='1.0'; generatedAtUtc='2026-10-10T00:30:01.0000000Z'; attempts=$attempts })
    $manifestBase64 = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes((@{ 'app\MinminCountdown.exe'=('C'*64) } | ConvertTo-Json -Compress)))
    [IO.File]::WriteAllText((Join-Path $runRoot 'remote-install.ps1'), "  DeploymentBindingSha256 = '$('B'*64)'`n    [Convert]::FromBase64String('$manifestBase64'))", [Text.UTF8Encoding]::new($false))
    $zipPath = Join-Path $runRoot 'commit-source.zip'
    $zip = [IO.Compression.ZipFile]::Open($zipPath, [IO.Compression.ZipArchiveMode]::Create)
    $zip.Dispose()
    $bytes = [IO.File]::ReadAllBytes($zipPath)
    $bytes[$bytes.Length-2] = 40
    [IO.File]::WriteAllBytes($zipPath, [byte[]]($bytes + [Text.Encoding]::ASCII.GetBytes($commit)))
    return $runRoot
}

$result = Read-State
Assert-State ($result.Value.Current.State -eq 'NotFound') '没有正式证据不能报告完成。'
Assert-State (-not $result.Value.LiveVerified) '只读本地证据不能报告实时验证。'
Assert-State ($result.Value.VerificationSource -eq 'LocalDeploymentEvidence') '状态来源必须明确。'
Assert-State ($null -eq $result.Value.LatestHistoricallyVerified) '没有证据时不存在历史成功。'

$completeRunId = '20261010003000000-12345678'
$completeRoot = New-CompleteRun $completeRunId
Write-FixtureJson $completeRoot 'deployment-checkpoint.json' (@{
    status='running'; resumeNote='Old runtime still active.'; resumeSuiteSession=43452; deploymentSession=68870
})
$result = Read-State
Assert-State ($result.Value.Current.State -eq 'HistoricallyVerified') '正式完整记录应通过原有交叉校验。'
Assert-State ($null -eq $result.Value.Current.NextAction) '已完整验收的记录不能给出过期续接动作。'
Assert-State (-not $result.Value.LiveVerified) '历史成功不能提升为实时验收。'
Assert-State ($result.Raw -notmatch 'resumeNote|resumeSuiteSession|deploymentSession|Old runtime') '手写 checkpoint 不能进入派生状态。'

$incompleteRunId = '20261010004000000-87654321'
$incompleteRoot = Join-Path $testRoot "$($commit.Substring(0, 7))/$incompleteRunId"
[void] (New-Item -ItemType Directory -Path $incompleteRoot)
$result = Read-State
Assert-State ($result.Value.Current.State -eq 'Incomplete') '较新的未完成尝试必须留在 Current。'
Assert-State ($result.Value.LatestHistoricallyVerified.RunId -eq $completeRunId) '先前成功应单独保留。'
Assert-State ($result.Value.Current.NextAction -eq 'ReviewIncompleteEvidence') '缺少回执不等于进程仍在运行。'

$failure = [ordered]@{
    schemaVersion=2; stage='Validation'; exceptionMessage='服务查询超时'; fullyQualifiedErrorId='CimJob_BrokenCimSession'
    positionMessage='At fixture.ps1:12'; scriptStackTrace='at <ScriptBlock>, fixture.ps1: line 12'
    transactionStarted=$true; commitState='not-committed'; rollbackAttempted=$true; rollbackSucceeded=$true
    preservedUnmanagedLaunchers=$true; writtenAtUtc='2026-10-10T00:41:00.0000000Z'
}
Write-FixtureJson $incompleteRoot 'remote-deployment-failure.json' $failure
$result = Read-State
Assert-State ($result.Value.Current.Outcome -eq 'Failed') '正式失败回执应在最新状态中明确报告失败。'
Assert-State ($result.Value.Current.NextAction -eq 'ReviewFailureEvidence') '失败只能指向证据复核，不能复用旧 session 命令。'
Assert-State ($result.Value.Current.Failure.CommitState -eq 'not-committed') '失败回执应保留事务边界。'

$result = Read-State -Diagnostics -Projector (Join-Path $testRoot 'missing-projector.ps1') -ExpectedExitCode 1
Assert-State ($result.Value.Diagnostics.Status -eq 'Unavailable') '缺少受管投影工具必须明确失败。'
Assert-State ($result.Value.Current.Outcome -eq 'Failed') '诊断读取失败时仍保留正式状态。'

if (Test-Path -LiteralPath $ProjectionScriptPath -PathType Leaf) {
    $diagnosticText = @(
        '开始中文验证',
        'token=fixture-secret-do-not-emit',
        'GET https://fixture-user:fixture-password@unit.invalid/path?token=fixture-query-secret&mode=test',
        ('payload=' + ('X' * 75924)),
        'validation-stage-ended'
    ) -join "`n"
    [IO.File]::WriteAllText((Join-Path $incompleteRoot 'deployment.stderr.log'), $diagnosticText, [Text.UTF8Encoding]::new($false))
    [IO.File]::WriteAllText((Join-Path $incompleteRoot 'remote-install.ps1'), ('script-only-secret=' + ('Y' * 75924)), [Text.UTF8Encoding]::new($false))
    $result = Read-State -Diagnostics
    Assert-State ($result.Value.Diagnostics.Status -eq 'Read') '诊断应通过现役有界投影入口读取。'
    Assert-State ($result.Raw -match '开始中文验证') '中文日志必须保留 UTF-8。'
    Assert-State ($result.Raw -notmatch 'fixture-secret-do-not-emit|fixture-user|fixture-password|fixture-query-secret') '已知凭据及 URL 认证必须脱敏。'
    Assert-State ($result.Raw -match '\[redacted\]') '敏感内容应有明确省略标记。'
    Assert-State ($result.Raw -match '\[value clipped\]') '超长单行必须被裁剪。'
    Assert-State ($result.Raw -notmatch 'script-only-secret|remote-install.ps1') '诊断不得扫描生成脚本中的 Base64。'
}
elseif ($SkipDiagnosticsWhenUnavailable) {
    $skipped.Add('受管 Read-BoundedProjection.ps1 不可用；诊断脱敏及大日志回归未运行。')
}
else { throw '缺少受管 Read-BoundedProjection.ps1，无法验证诊断；仅 CI 可显式使用 -SkipDiagnosticsWhenUnavailable。' }

$failure.schemaVersion = 1
$failure.exceptionMessage = 'token=invalid-receipt-secret'
Write-FixtureJson $incompleteRoot 'remote-deployment-failure.json' $failure
$result = Read-State
Assert-State ($result.Value.Current.FailureReceiptStatus -eq 'Invalid') '无效失败回执必须明确标记，不能默认为可用。'
Assert-State ($result.Value.Current.Outcome -eq 'Incomplete' -and $null -eq $result.Value.Current.Failure) '无效回执不能提供事务或完成证明。'
Assert-State ($result.Raw -notmatch 'invalid-receipt-secret') '解析失败不得回显原始错误材料。'

for ($i=1; $i -le 28; $i++) {
    $runId = '20261011{0:000000000}-{1:x8}' -f $i, $i
    [void] (New-Item -ItemType Directory -Path (Join-Path $testRoot "$($commit.Substring(0, 7))/$runId"))
}
$result = Read-State -HistoryLimit 3
Assert-State ($result.Value.History.Count -eq 3 -and $result.Value.OmittedHistory -eq 26) '历史条数限制必须保留准确省略计数。'
$result = Read-State -HistoryLimit 50 -MaxTotalBytes 2048
Assert-State ($result.Value.OmittedHistory -gt 0) '总字节预算应收紧历史输出。'
Assert-State ($result.Value.History.Count + $result.Value.OmittedHistory -eq 29) '裁剪后历史记录仍须完整计数。'
Assert-State ($result.Value.LatestHistoricallyVerified.RunId -eq $completeRunId) '预算裁剪不能丢掉最近历史验收的独立来源。'

[pscustomobject]@{ Valid=$true; Assertions=$assertions; Skipped=@($skipped); EvidenceRoot=$testRoot } | ConvertTo-Json -Compress
