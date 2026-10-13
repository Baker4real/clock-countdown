#requires -Version 7.0
[CmdletBinding()]
param(
    [string] $EvidenceRoot = 'D:/agents/run-evidence/clock-countdown-deploy',
    [ValidatePattern('^[0-9a-fA-F]{40}$')] [string] $Commit,
    [switch] $IncludeDiagnostics,
    [ValidateRange(0, 50)] [int] $HistoryLimit = 5,
    [ValidateRange(2048, 30000)] [int] $MaxTotalBytes = 6000,
    [string] $ProjectionScriptPath = 'D:/Codex/.codex/scripts/Read-BoundedProjection.ps1'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
$repositoryRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $repositoryRoot 'Deploy-OfficeCountdown.ps1') -LoadFunctionsOnly
Import-Module (Join-Path $repositoryRoot 'Countdown.RemoteDeployment.psm1') -Force -DisableNameChecking

if ([string]::IsNullOrWhiteSpace($Commit)) {
    $gitPath = (Get-Command git -CommandType Application -ErrorAction Stop | Select-Object -First 1).Source
    $Commit = Get-CountdownDeploymentGitScalar -GitPath $gitPath -RepositoryRoot $repositoryRoot `
        -Arguments @('rev-parse', '--verify', 'HEAD')
}
$EvidenceRoot = [IO.Path]::GetFullPath($EvidenceRoot)
# This is the owner used by Deploy-OfficeCountdown.ps1 -StatusOnly. Historical
# scripts remain data; no checkpoint fields or saved commands are evaluated.
$status = Get-OfficeDeploymentStatus -Commit $Commit -EvidenceBase $EvidenceRoot
$latest = @($status.Runs | Select-Object -First 1)
$plainRunDirectory = $latest.Count -gt 0 -and
    (((Get-Item -LiteralPath $latest[0].EvidenceRoot -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) -eq 0)
$current = [ordered]@{
    RunId = if ($latest.Count) { $latest[0].RunId } else { $null }
    State = if ($latest.Count) { $latest[0].State } else { 'NotFound' }
    EvidenceRoot = if ($latest.Count) { $latest[0].EvidenceRoot } else { $EvidenceRoot }
    VerifiedAtUtc = if ($latest.Count) { $latest[0].VerifiedAtUtc } else { $null }
    Outcome = if ($latest.Count) { 'Incomplete' } else { 'NoEvidence' }
    Failure = $null
    FailureReceiptStatus = 'Absent'
    NextAction = if ($latest.Count) { 'ReviewIncompleteEvidence' } else { 'LocateDeploymentEvidence' }
}
if ($current.State -eq 'HistoricallyVerified') {
    $current.Outcome = 'Verified'
    $current.FailureReceiptStatus = 'NotApplicable'
    $current.NextAction = $null
}
elseif ($plainRunDirectory -and (Test-Path -LiteralPath (Join-Path $current.EvidenceRoot 'remote-deployment-failure.json') -PathType Leaf)) {
    try {
        Import-Module (Join-Path $repositoryRoot 'Countdown.InstallTransaction.psm1') -Force -DisableNameChecking
        $failureJson = Read-OfficeEvidenceText $current.EvidenceRoot 'remote-deployment-failure.json' -MaxBytes 24576
        $failure = ConvertFrom-CountdownInstallFailureReceiptBytes -Bytes ([Text.Encoding]::UTF8.GetBytes($failureJson))
        $current.Failure = [ordered]@{
            CommitState = $failure.commitState
            TransactionStarted = $failure.transactionStarted
            RollbackAttempted = $failure.rollbackAttempted
            RollbackSucceeded = $failure.rollbackSucceeded
            WrittenAtUtc = ([DateTimeOffset]::Parse($failure.writtenAtUtc)).ToUniversalTime().ToString('o')
        }
        $current.FailureReceiptStatus = 'Valid'
        $current.Outcome = 'Failed'
        $current.NextAction = 'ReviewFailureEvidence'
    }
    catch {
        # Parser errors can contain raw receipt data; expose the failure class
        # here and leave bounded diagnostic text to the managed projector.
        $current.FailureReceiptStatus = 'Invalid'
    }
}
$verified = if ($null -ne $status.LatestComplete) {
    $status.LatestComplete | Select-Object RunId, EvidenceRoot, State, VerifiedAtUtc
} else { $null }
$history = @($status.Runs | Select-Object -Skip 1 -First $HistoryLimit |
    Select-Object RunId, State, VerifiedAtUtc)
$result = [ordered]@{
    SchemaVersion = 1
    Commit = $status.Commit
    VerificationSource = 'LocalDeploymentEvidence'
    LiveVerified = $false
    ObservedAtUtc = [DateTime]::UtcNow.ToString('o')
    Current = $current
    LatestHistoricallyVerified = $verified
    History = $history
    OmittedHistory = [Math]::Max(0, @($status.Runs).Count - 1 - $history.Count)
    Diagnostics = $null
    MaxTotalBytes = $MaxTotalBytes
}
$exitCode = 0
if ($IncludeDiagnostics) {
    $result.Diagnostics = [ordered]@{ Status='NoSources'; OmittedSources=0; ProjectionExitCode=$null; Projection=$null }
    if (-not (Test-Path -LiteralPath $ProjectionScriptPath -PathType Leaf)) {
        $result.Diagnostics.Status = 'Unavailable'
        $exitCode = 1
    }
    elseif ($plainRunDirectory) {
        # Receipts and stage output are the only diagnostic inputs. Never scan
        # generated scripts, settings, archives, or hand-written checkpoints.
        $diagnosticFiles = @(
            Get-ChildItem -LiteralPath $current.EvidenceRoot -File |
                Where-Object {
                    ($_.Name -eq 'remote-deployment-failure.json' -or $_.Name -match '(^|[.-])(stdout|stderr)\.log$') -and
                    ($_.Attributes -band [IO.FileAttributes]::ReparsePoint) -eq 0
                } |
                Sort-Object @{ Expression={ if ($_.Name -eq 'remote-deployment-failure.json') { 0 } else { 1 } } },
                    @{ Expression='LastWriteTimeUtc'; Descending=$true }
        )
        $paths = @($diagnosticFiles | Select-Object -First 4 -ExpandProperty FullName)
        $result.Diagnostics.OmittedSources = [Math]::Max(0, $diagnosticFiles.Count - $paths.Count)
        if ($paths.Count) {
            $result.Diagnostics.Status = 'Read'
            $result.Diagnostics.ProjectionExitCode = 0
            $baseJson = $result | ConvertTo-Json -Depth 12 -Compress
            # Reserve wrapper space before giving the remaining shared budget
            # to the host projector; each file does not receive a fresh budget.
            while ([Text.Encoding]::UTF8.GetByteCount($baseJson) + 1024 -gt $MaxTotalBytes -and $result.History.Count -gt 0) {
                $result.History = @($result.History | Select-Object -SkipLast 1)
                $result.OmittedHistory++
                $baseJson = $result | ConvertTo-Json -Depth 12 -Compress
            }
            $projectionBudget = $MaxTotalBytes - [Text.Encoding]::UTF8.GetByteCount($baseJson) - 64
            if ($projectionBudget -lt 512) {
                $result.Diagnostics.Status = 'BudgetTooSmall'
                $exitCode = 1
            }
            else {
                try {
                    $global:LASTEXITCODE = 0
                    $projectionText = @(& $ProjectionScriptPath -LiteralPath $paths -MaxRows 12 -MaxValueChars 200 -MaxTotalBytes $projectionBudget)
                    $result.Diagnostics.ProjectionExitCode = $LASTEXITCODE
                    $result.Diagnostics.Projection = ($projectionText -join "`n") | ConvertFrom-Json -DateKind String -ErrorAction Stop
                    if ($LASTEXITCODE -ne 0) {
                        $result.Diagnostics.Status = 'ReadFailed'
                        $exitCode = 1
                    }
                }
                catch {
                    $result.Diagnostics.Status = 'ReadFailed'
                    $result.Diagnostics.Projection = $null
                    $exitCode = 1
                }
            }
        }
    }
}
$json = $result | ConvertTo-Json -Depth 12 -Compress
while ([Text.Encoding]::UTF8.GetByteCount($json) + 2 -gt $MaxTotalBytes -and $result.History.Count -gt 0) {
    $result.History = @($result.History | Select-Object -SkipLast 1)
    $result.OmittedHistory++
    $json = $result | ConvertTo-Json -Depth 12 -Compress
}
if ([Text.Encoding]::UTF8.GetByteCount($json) + 2 -gt $MaxTotalBytes) {
    throw '部署状态元数据超过预算，请使用更短的证据路径或增大 MaxTotalBytes。'
}
Write-Output $json
exit $exitCode
