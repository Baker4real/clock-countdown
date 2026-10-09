#requires -Version 7.0
[CmdletBinding()]
param([switch] $Json)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$testRoot = Join-Path ([System.IO.Path]::GetTempPath()) "clock-countdown-publication-diff-test-$([guid]::NewGuid().ToString('N'))"
[System.IO.Directory]::CreateDirectory($testRoot) | Out-Null
$checks = [System.Collections.Generic.List[string]]::new()
$failures = @()
$script = Join-Path $PSScriptRoot 'Test-CountdownPublicationDiff.ps1'

function Invoke-FixtureGit {
    param([string[]] $Arguments)
    $text = @($null | & git -C $testRoot @Arguments 2>&1) -join [Environment]::NewLine
    if ($LASTEXITCODE -ne 0) { throw "Fixture git failed: $text" }
    $text.Trim()
}

function Test-Fixture {
    param([string] $Name, [string] $Base, [string] $Candidate, [string] $FailedScope = '', [string[]] $EventArguments = @())
    $rangeArguments = if ($EventArguments.Count -gt 0) { $EventArguments } else { @('-BaseCommit', $Base) }
    $output = @(& pwsh -NoLogo -NoProfile -NonInteractive -File $script -RepositoryRoot $testRoot @rangeArguments -CandidateCommit $Candidate -Json 2>&1)
    $code = $LASTEXITCODE
    $record = ($output -join [Environment]::NewLine) | ConvertFrom-Json -Depth 10
    if ($FailedScope -eq '') {
        if ($code -ne 0 -or -not $record.Valid) { throw "$Name should pass: $($record.Failures -join '; ')" }
        $expectedCandidate = Invoke-FixtureGit @('rev-parse', "$Candidate^{commit}")
        if ($record.CandidateCommit -cne $expectedCandidate -or $record.BaseCommit -notmatch '^[0-9a-f]{40,64}$') { throw "$Name did not bind resolved commit IDs" }
        if ($record.Checks.Count -ne 3) { throw "$Name did not check all three diff scopes" }
    } elseif ($FailedScope -eq 'invalid-base') {
        if ($code -eq 0 -or $record.Valid -or $record.Failures.Count -eq 0) { throw "$Name should fail closed" }
    } else {
        $failed = @($record.Checks | Where-Object { $_.Name -eq $FailedScope -and -not $_.Valid })
        if ($code -eq 0 -or $record.Valid -or $failed.Count -ne 1) { throw "$Name did not detect $FailedScope whitespace" }
    }
    $checks.Add($Name)
}

try {
    Invoke-FixtureGit @('init', '--quiet', '--initial-branch=fixture') | Out-Null
    Invoke-FixtureGit @('config', 'user.name', 'Publication diff fixture') | Out-Null
    Invoke-FixtureGit @('config', 'user.email', 'fixture@example.invalid') | Out-Null
    Invoke-FixtureGit @('config', 'core.autocrlf', 'false') | Out-Null
    Invoke-FixtureGit @('config', 'core.whitespace', 'blank-at-eof,blank-at-eol,space-before-tab') | Out-Null
    [System.IO.File]::WriteAllText((Join-Path $testRoot 'sample.txt'), "clean`n")
    Invoke-FixtureGit @('add', 'sample.txt') | Out-Null
    Invoke-FixtureGit @('commit', '--quiet', '-m', 'clean base') | Out-Null
    $base = Invoke-FixtureGit @('rev-parse', 'HEAD')
    Test-Fixture -Name 'clean' -Base $base -Candidate $base
    [System.IO.File]::WriteAllText((Join-Path $testRoot 'sample.txt'), "committed `n")
    Invoke-FixtureGit @('add', 'sample.txt') | Out-Null
    Invoke-FixtureGit @('commit', '--quiet', '-m', 'committed whitespace') | Out-Null
    $bad = Invoke-FixtureGit @('rev-parse', 'HEAD')
    Test-Fixture -Name 'committed-negative-control' -Base $base -Candidate $bad -FailedScope committed
    # Removing bad whitespace from the index must not mask the bad candidate commit.
    [System.IO.File]::WriteAllText((Join-Path $testRoot 'sample.txt'), "clean`n")
    Invoke-FixtureGit @('add', 'sample.txt') | Out-Null
    Test-Fixture -Name 'candidate-not-masked-by-index' -Base $base -Candidate $bad -FailedScope committed
    Invoke-FixtureGit @('commit', '--quiet', '-m', 'clean candidate') | Out-Null
    $candidate = Invoke-FixtureGit @('rev-parse', 'HEAD')
    [System.IO.File]::WriteAllText((Join-Path $testRoot 'sample.txt'), "staged `n")
    Invoke-FixtureGit @('add', 'sample.txt') | Out-Null
    Test-Fixture -Name 'staged-negative-control' -Base $base -Candidate $candidate -FailedScope staged
    [System.IO.File]::WriteAllText((Join-Path $testRoot 'sample.txt'), "clean`n")
    Invoke-FixtureGit @('add', 'sample.txt') | Out-Null
    [System.IO.File]::WriteAllText((Join-Path $testRoot 'sample.txt'), "worktree `n")
    Test-Fixture -Name 'worktree-negative-control' -Base $base -Candidate $candidate -FailedScope worktree
    [System.IO.File]::WriteAllText((Join-Path $testRoot 'sample.txt'), "clean`n")
    Test-Fixture -Name 'missing-base-fails-closed' -Base 'missing-base' -Candidate $candidate -FailedScope invalid-base
    Test-Fixture -Name 'non-ancestor-fails-closed' -Base $candidate -Candidate $base -FailedScope invalid-base
    $emptyTree = Invoke-FixtureGit @('hash-object', '-t', 'tree', '--stdin')
    Test-Fixture -Name 'initial-push-empty-tree' -Base $emptyTree -Candidate $candidate
    Test-Fixture -Name 'manual-full-ref' -Base $base -Candidate 'refs/heads/fixture'
    Test-Fixture -Name 'push-event' -Candidate $candidate -EventArguments @('-EventName', 'push', '-PushBase', $base)
    Test-Fixture -Name 'pull-request-event' -Candidate $candidate -EventArguments @('-EventName', 'pull_request', '-PullRequestBase', $base)
    Test-Fixture -Name 'manual-event' -Candidate $candidate -EventArguments @('-EventName', 'workflow_dispatch', '-ManualBase', $base)
    Test-Fixture -Name 'initial-push-event' -Candidate $candidate -EventArguments @('-EventName', 'push', '-PushBase', ('0' * 40))
    Test-Fixture -Name 'missing-event-base' -Candidate $candidate -FailedScope invalid-base -EventArguments @('-EventName', 'workflow_dispatch')
} catch {
    $failures += $_.Exception.Message
}
$result = [pscustomobject]@{ Valid = $failures.Count -eq 0; Checks = @($checks); Failures = $failures; FixtureRoot = $testRoot }
if ($Json) { $result | ConvertTo-Json -Depth 8 } else { $result }
if (-not $result.Valid) { exit 1 }
exit 0
