#requires -Version 7.0
[CmdletBinding(DefaultParameterSetName = 'Explicit')]
param(
    [string] $RepositoryRoot = (Join-Path $PSScriptRoot '..'),
    [Parameter(Mandatory, ParameterSetName = 'Explicit')] [string] $BaseCommit,
    [Parameter(Mandatory, ParameterSetName = 'Event')] [ValidateSet('push', 'pull_request', 'workflow_dispatch')] [string] $EventName,
    [Parameter(ParameterSetName = 'Event')] [string] $PushBase,
    [Parameter(ParameterSetName = 'Event')] [string] $PullRequestBase,
    [Parameter(ParameterSetName = 'Event')] [string] $ManualBase,
    [Parameter(Mandatory)] [string] $CandidateCommit,
    [switch] $Json
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$result = [ordered]@{ Valid = $false; BaseCommit = $null; CandidateCommit = $null; BaseKind = 'commit'; Checks = @(); Failures = @() }

function Invoke-RepositoryGit {
    param([string[]] $Arguments)
    $output = @($null | & git -C $RepositoryRoot @Arguments 2>&1)
    $code = $LASTEXITCODE
    [pscustomobject]@{ ExitCode = $code; Text = ($output -join [Environment]::NewLine).Trim() }
}

try {
    $RepositoryRoot = (Resolve-Path -LiteralPath $RepositoryRoot).Path
    if ($PSCmdlet.ParameterSetName -eq 'Event') {
        $BaseCommit = switch ($EventName) {
            'push' { $PushBase }
            'pull_request' { $PullRequestBase }
            'workflow_dispatch' { $ManualBase }
        }
        if ($EventName -eq 'push' -and $BaseCommit -match '^0{40,64}$') {
            $emptyTree = Invoke-RepositoryGit @('hash-object', '-t', 'tree', '--stdin')
            if ($emptyTree.ExitCode -ne 0) { throw 'Cannot resolve the empty tree for an initial push.' }
            $BaseCommit = $emptyTree.Text
        }
    }
    if ([string]::IsNullOrWhiteSpace($BaseCommit)) { throw 'Missing publication base.' }
    $candidate = Invoke-RepositoryGit @('rev-parse', '--verify', '--end-of-options', "$CandidateCommit^{commit}")
    if ($candidate.ExitCode -ne 0) { throw "Invalid candidate commit: $($candidate.Text)" }
    $result.CandidateCommit = $candidate.Text
    $base = Invoke-RepositoryGit @('rev-parse', '--verify', '--end-of-options', "$BaseCommit^{commit}")
    if ($base.ExitCode -ne 0) {
        # A newly created branch has no previous commit; explicitly check its entire tree.
        $emptyTree = Invoke-RepositoryGit @('hash-object', '-t', 'tree', '--stdin')
        if ($emptyTree.ExitCode -ne 0 -or $BaseCommit -cne $emptyTree.Text) { throw "Invalid publication base: $($base.Text)" }
        $result.BaseCommit = $emptyTree.Text
        $result.BaseKind = 'empty-tree'
    } else {
        $result.BaseCommit = $base.Text
        $ancestor = Invoke-RepositoryGit @('merge-base', '--is-ancestor', $base.Text, $candidate.Text)
        if ($ancestor.ExitCode -ne 0) { throw 'Publication base must be an ancestor of the candidate commit.' }
    }
    $ranges = @(
        @{ Name = 'committed'; Arguments = @('diff', '--check', $result.BaseCommit, $result.CandidateCommit, '--') },
        @{ Name = 'staged'; Arguments = @('diff', '--cached', '--check', $result.CandidateCommit, '--') },
        @{ Name = 'worktree'; Arguments = @('diff', '--check', '--') }
    )
    foreach ($range in $ranges) {
        $check = Invoke-RepositoryGit $range.Arguments
        $result.Checks += [pscustomobject]@{ Name = $range.Name; Valid = $check.ExitCode -eq 0; ExitCode = $check.ExitCode; Detail = $check.Text }
        if ($check.ExitCode -ne 0) { $result.Failures += "$($range.Name): $($check.Text)" }
    }
    $result.Valid = $result.Failures.Count -eq 0
} catch {
    $result.Failures += $_.Exception.Message
}
if ($Json) { $result | ConvertTo-Json -Depth 8 } else { [pscustomobject] $result }
if (-not $result.Valid) { exit 1 }
exit 0
