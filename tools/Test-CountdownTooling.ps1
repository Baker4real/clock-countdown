#requires -Version 7.0
[CmdletBinding()]
param(
    [switch] $Json,
    [string] $BaseCommit = $env:COUNTDOWN_DIFF_BASE,
    [string] $CandidateCommit = $env:COUNTDOWN_DIFF_CANDIDATE
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$failures = [System.Collections.Generic.List[string]]::new()
$checks = [System.Collections.Generic.List[object]]::new()

function Add-ToolingCheck {
    param([Parameter(Mandatory)] [string] $Name, [Parameter(Mandatory)] [bool] $Passed, [string] $Detail = '')
    $checks.Add([pscustomobject]@{ Name = $Name; Status = if ($Passed) { 'processed' } else { 'failed' }; Detail = $Detail })
    if (-not $Passed) { $failures.Add("$Name：$Detail") }
}

function Test-RequiredPath {
    param([Parameter(Mandatory)] [string] $RelativePath)
    $path = Join-Path $repoRoot $RelativePath
    $exists = Test-Path -LiteralPath $path -PathType Leaf
    $detail = if ($exists) { 'exists' } else { 'missing' }
    Add-ToolingCheck -Name "path:$RelativePath" -Passed $exists -Detail $detail
}

foreach ($requiredPath in @(
        'global.json',
        'README.md',
        'docs/agents/issue-acceptance.json',
        'docs/agents/issue-acceptance.md',
        'tools/Test-IssueAcceptance.ps1',
        'tools/Invoke-IssueWriteback.ps1',
        'tools/Test-IssueWriteback.ps1',
        'tools/Test-CountdownPublicationDiff.ps1',
        'tools/Test-CountdownPublicationDiff.Tests.ps1',
        'tools/Invoke-CountdownSuiteLowImpact.ps1'
    )) { Test-RequiredPath -RelativePath $requiredPath }

$schemaTest = & (Join-Path $PSScriptRoot 'Test-IssueAcceptance.ps1') -SelfTest -Json | ConvertFrom-Json -Depth 10
Add-ToolingCheck -Name 'issue-acceptance:self-test' -Passed ($LASTEXITCODE -eq 0 -and [bool] $schemaTest.Valid) -Detail ($schemaTest | ConvertTo-Json -Compress)
$writebackTest = & (Join-Path $PSScriptRoot 'Invoke-IssueWriteback.ps1') -SelfTest -Json | ConvertFrom-Json -Depth 10
Add-ToolingCheck -Name 'issue-writeback:self-test' -Passed ($LASTEXITCODE -eq 0 -and [bool] $writebackTest.Valid) -Detail ($writebackTest | ConvertTo-Json -Compress)
$writebackRegression = & pwsh -NoLogo -NoProfile -NonInteractive -File (Join-Path $PSScriptRoot 'Test-IssueWriteback.ps1') -Json | ConvertFrom-Json -Depth 20
Add-ToolingCheck -Name 'issue-writeback:regression' -Passed ($LASTEXITCODE -eq 0 -and [bool] $writebackRegression.Valid) -Detail ($writebackRegression | ConvertTo-Json -Compress -Depth 20)
$diffRegression = & pwsh -NoLogo -NoProfile -NonInteractive -File (Join-Path $PSScriptRoot 'Test-CountdownPublicationDiff.Tests.ps1') -Json | ConvertFrom-Json -Depth 10
Add-ToolingCheck -Name 'publication-diff:regression' -Passed ($LASTEXITCODE -eq 0 -and [bool] $diffRegression.Valid) -Detail ($diffRegression | ConvertTo-Json -Compress -Depth 10)

$parseErrors = [System.Collections.Generic.List[string]]::new()
$scriptPaths = @(
    Get-ChildItem -LiteralPath $repoRoot -Filter '*.ps1' -File
    Get-ChildItem -LiteralPath (Join-Path $repoRoot 'tools') -Filter '*.ps1' -File
) | Sort-Object FullName -Unique
foreach ($scriptPath in $scriptPaths) {
    $tokens = $null
    $errors = $null
    [System.Management.Automation.Language.Parser]::ParseFile($scriptPath.FullName, [ref] $tokens, [ref] $errors) | Out-Null
    if ($errors.Count -gt 0) {
        $messages = @($errors | ForEach-Object { $_.Message }) -join '; '
        $parseErrors.Add("$($scriptPath.FullName): $messages")
    }
}
$parseDetail = if ($parseErrors.Count -eq 0) { "$($scriptPaths.Count) scripts" } else { $parseErrors -join ' | ' }
Add-ToolingCheck -Name 'powershell:parse' -Passed ($parseErrors.Count -eq 0) -Detail $parseDetail

$docLinkFailures = [System.Collections.Generic.List[string]]::new()
$markdownFiles = Get-ChildItem -LiteralPath $repoRoot -Filter '*.md' -File -Recurse |
    Where-Object {
        $_.FullName -notmatch '\\(?:\.git|bin|obj|app|verification|logs)\\' -and
        $_.FullName -notmatch '\\release-contract-test-[^\\]+\\'
    }
$linkPattern = '\[[^\]]+\]\((?<target>[^)\s]+)\)'
foreach ($markdownFile in $markdownFiles) {
    $markdown = [System.IO.File]::ReadAllText($markdownFile.FullName)
    foreach ($match in [regex]::Matches($markdown, $linkPattern)) {
        $target = [string] $match.Groups['target'].Value
        if ($target -match '^(?:[a-z][a-z0-9+.-]*:|//)' -or $target.StartsWith('#')) { continue }
        $target = $target.Split('#')[0]
        if ([string]::IsNullOrWhiteSpace($target)) { continue }
        $target = [System.Uri]::UnescapeDataString($target).Replace('/', [System.IO.Path]::DirectorySeparatorChar)
        $resolved = [System.IO.Path]::GetFullPath((Join-Path $markdownFile.DirectoryName $target))
        if (-not (Test-Path -LiteralPath $resolved)) {
            $docLinkFailures.Add("$($markdownFile.FullName): $target")
        }
    }
}
$docLinkDetail = if ($docLinkFailures.Count -eq 0) { "$($markdownFiles.Count) Markdown files" } else { $docLinkFailures -join ' | ' }
Add-ToolingCheck -Name 'docs:local-links' -Passed ($docLinkFailures.Count -eq 0) -Detail $docLinkDetail

if ([string]::IsNullOrWhiteSpace($BaseCommit)) { $BaseCommit = 'origin/main' }
if ([string]::IsNullOrWhiteSpace($CandidateCommit)) { $CandidateCommit = 'HEAD' }
$diffCheck = & pwsh -NoLogo -NoProfile -NonInteractive -File (Join-Path $PSScriptRoot 'Test-CountdownPublicationDiff.ps1') -RepositoryRoot $repoRoot -BaseCommit $BaseCommit -CandidateCommit $CandidateCommit -Json | ConvertFrom-Json -Depth 10
Add-ToolingCheck -Name 'git:publication-diff-check' -Passed ($LASTEXITCODE -eq 0 -and [bool] $diffCheck.Valid) -Detail ($diffCheck | ConvertTo-Json -Compress -Depth 10)

$result = [pscustomobject]@{
    Valid = $failures.Count -eq 0
    Checks = @($checks)
    Failures = @($failures)
}
if ($Json) { $result | ConvertTo-Json -Depth 10 } else {
    $result | ConvertTo-Json -Depth 10
}
if ($failures.Count -gt 0) { exit 1 }
exit 0
