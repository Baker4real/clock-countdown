#requires -Version 7.0
[CmdletBinding()]
param([switch]$Json)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$fixture = Join-Path $repoRoot ('logs/publication-evidence-test-' + [guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($fixture)
$utf8 = [Text.UTF8Encoding]::new($false)
$checks = [Collections.Generic.List[string]]::new()
$cli = Join-Path $PSScriptRoot 'Test-CountdownPublicationEvidence.ps1'
function Write-Fixture([string]$Path, [string]$Text) {
    $target = Join-Path $fixture $Path
    [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($target))
    [IO.File]::WriteAllText($target, $Text, $utf8)
}
function Git([string[]]$Arguments) {
    $text = @(& git.exe -C $fixture @Arguments 2>&1)
    if ($LASTEXITCODE -ne 0) { throw "Fixture git failed: $text" }
}
function Check([bool]$Passed, [string]$Name) {
    if (-not $Passed) { throw "Publication evidence regression: $Name" }
    $checks.Add($Name)
}
function Probe([string]$Source = 'Index', [string]$Commit = 'HEAD') {
    $prior = $PSNativeCommandUseErrorActionPreference
    try {
        $PSNativeCommandUseErrorActionPreference = $false
        $raw = & pwsh -NoLogo -NoProfile -NonInteractive -File $cli -RepositoryRoot $fixture -Source $Source -Commit $Commit -Json
        $code = $LASTEXITCODE
        [pscustomobject]@{ ExitCode = $code; Report = (($raw -join "`n") | ConvertFrom-Json) }
    } finally { $PSNativeCommandUseErrorActionPreference = $prior }
}
$passed = $false
try {
    Git @('init','--quiet')
    Git @('config','user.name','Evidence Fixture')
    Git @('config','user.email','fixture@example.invalid')
    Write-Fixture '.gitignore' "receipt.json`n"
    Write-Fixture 'README.md' "[验收回执](receipt.json)`n"
    Write-Fixture 'receipt.json' "{}`n"
    Git @('add','.gitignore','README.md')
    $missing = Probe
    Check ($missing.ExitCode -eq 1 -and -not $missing.Report.Valid) 'ignored-disk-file-does-not-satisfy-index-link'
    Git @('add','--force','receipt.json')
    $included = Probe
    Check ($included.ExitCode -eq 0 -and $included.Report.Valid) 'staged-link-target-passes'
    Write-Fixture 'docs/evidence/manifest.json' '{"hash_scope":"git-blob","entries":[{"path":"frame.png","sha256":"0000000000000000000000000000000000000000000000000000000000000000"}]}'
    Write-Fixture 'docs/evidence/frame.png' 'fixture pixels'
    Git @('add','docs')
    $mismatch = Probe
    Check ($mismatch.ExitCode -eq 1 -and ($mismatch.Report.Failures -match 'HashMismatch')) 'tampered-png-is-rejected'
    Import-Module (Join-Path $PSScriptRoot 'Countdown.PublicationEvidence.psm1') -Force
    $pngHash = (Get-FileHash -LiteralPath (Join-Path $fixture 'docs/evidence/frame.png') -Algorithm SHA256).Hash
    Write-CountdownEvidenceJson -Path (Join-Path $fixture 'docs/evidence/manifest.json') -Value @{
        hash_scope='git-blob'; entries=@(@{path='frame.png';sha256=$pngHash}); run_artifact_sha256=('F'*64)
    }
    Git @('add','docs')
    Check ((Probe).Report.Valid) 'published-png-and-distinct-run-artifact-hash-pass'
    Git @('commit','--quiet','-m','valid evidence')
    Write-Fixture 'docs/evidence/frame.png' 'changed bytes'
    Git @('add','docs/evidence/frame.png')
    Check ((Probe).ExitCode -eq 1 -and (Probe 'Commit').Report.Valid) 'index-mutation-does-not-mask-committed-evidence'
    Git @('reset','--quiet','HEAD')
    Write-Fixture 'README.md' "[新增文件](not-staged.json)`n"
    $stable = Probe
    Check $stable.Report.Valid 'unstaged-document-does-not-change-index-snapshot'
    Git @('add','README.md')
    Check ((Probe).ExitCode -eq 1) 'new-staged-document-requires-staged-target'
    Git @('reset','--quiet','HEAD')
    Git @('rm','--cached','--quiet','docs/evidence/frame.png')
    Check (@((Probe).Report.Failures -match 'MissingEvidence').Count -gt 0) 'missing-hash-target-rejected-even-when-on-disk'
    Git @('reset','--quiet','HEAD')
    Write-Fixture '.gitattributes' "*.json text eol=lf`n"
    Write-Fixture 'docs/evidence/data.json' "{`r`n  `"sample`": 1`r`n}`r`n"
    $canonicalHash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($utf8.GetBytes("{`n  `"sample`": 1`n}`n")))
    $originalHash = (Get-FileHash -LiteralPath (Join-Path $fixture 'docs/evidence/data.json') -Algorithm SHA256).Hash
    Write-CountdownEvidenceJson -Path (Join-Path $fixture 'docs/evidence/manifest.json') -Value @{
        hash_scope='git-blob'; receipts=@(@{path='data.json';sha256=$canonicalHash;run_artifact_sha256=$originalHash})
    }
    Git @('add','.gitattributes','docs/evidence/data.json','docs/evidence/manifest.json')
    Check ((Probe).Report.Valid -and $canonicalHash -cne $originalHash) 'crlf-run-artifact-and-lf-git-blob-have-distinct-valid-hashes'
    $writerBytes = [IO.File]::ReadAllBytes((Join-Path $fixture 'docs/evidence/manifest.json'))
    Check (-not ($writerBytes -contains 13) -and $writerBytes[0] -eq 123) 'evidence-writer-produces-utf8-no-bom-lf-before-hashing'
    Write-CountdownEvidenceJson -Path (Join-Path $fixture 'docs/evidence/manifest.json') -Value @{
        hash_scope='git-blob'; receipts=@(@{path='data.json';sha256=$originalHash;run_artifact_sha256=$originalHash})
    }
    Git @('add','docs/evidence/manifest.json')
    Check (@((Probe).Report.Failures -match 'HashMismatch').Count -gt 0) 'crlf-hash-cannot-stand-in-for-published-blob'
    Write-Fixture 'docs/agents/publication-evidence.json' '{}'
    Write-CountdownEvidenceJson -Path (Join-Path $fixture 'docs/agents/publication-evidence.json') -Value @{schemaVersion=1;roots=@('docs/evidence/required.json')}
    Git @('add','docs/agents/publication-evidence.json')
    Check (@((Probe).Report.Failures -match 'MissingEvidenceRoot').Count -gt 0) 'registered-evidence-root-cannot-disappear'
    Write-Fixture 'docs/evidence/required.json' '{"entries":[]}'
    Git @('add','docs/evidence/required.json')
    Check (@((Probe).Report.Failures -match 'MissingHashScope').Count -gt 0) 'registered-root-cannot-drop-its-byte-scope'
    $passed = $true
    [pscustomobject]@{Valid=$true;Checks=@($checks)} | ConvertTo-Json -Depth 5 -Compress
}
finally {
    # 仅删除本次创建且已通过的精确夹具根；失败保留现场。
    if ($passed) {
        $resolved = [IO.Path]::GetFullPath($fixture)
        if (-not $resolved.StartsWith((Join-Path $repoRoot 'logs/publication-evidence-test-'),[StringComparison]::OrdinalIgnoreCase)) { throw 'Unexpected fixture cleanup path.' }
        Get-ChildItem -LiteralPath $resolved -File -Recurse -Force | ForEach-Object { $_.IsReadOnly = $false }
        [IO.Directory]::Delete($resolved,$true)
    } else { Write-Warning "Failure fixture retained: $fixture" }
}
exit 0
