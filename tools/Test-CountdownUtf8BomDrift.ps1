#requires -Version 7.0
[CmdletBinding()]
param(
    [string] $RepositoryRoot = (Join-Path $PSScriptRoot '..'),
    [string] $BaseCommit,
    [string] $CandidateCommit = 'HEAD',
    [switch] $StagedOnly,
    [switch] $SelfTest,
    [switch] $Json
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$textExtensions = [System.Collections.Generic.HashSet[string]]::new(
    [string[]] @(
        '.ps1', '.psm1', '.psd1', '.cs', '.csproj', '.sln', '.xaml',
        '.json', '.md', '.yml', '.yaml', '.txt', '.props', '.targets',
        '.gitignore', '.gitattributes', '.editorconfig'
    ),
    [System.StringComparer]::OrdinalIgnoreCase
)

function Invoke-GitRaw {
    param(
        [Parameter(Mandatory)] [string] $Root,
        [Parameter(Mandatory)] [string[]] $Arguments
    )

    $startInfo = [System.Diagnostics.ProcessStartInfo]::new('git')
    foreach ($argument in @('-C', $Root) + $Arguments) {
        $startInfo.ArgumentList.Add($argument)
    }
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.StandardErrorEncoding = [System.Text.UTF8Encoding]::new($false)
    $process = [System.Diagnostics.Process]::Start($startInfo)
    try {
        $stderrTask = $process.StandardError.ReadToEndAsync()
        $buffer = [System.IO.MemoryStream]::new()
        try {
            $process.StandardOutput.BaseStream.CopyTo($buffer)
            $process.WaitForExit()
            [pscustomobject]@{
                ExitCode = $process.ExitCode
                Bytes = $buffer.ToArray()
                Error = $stderrTask.GetAwaiter().GetResult().Trim()
            }
        }
        finally {
            $buffer.Dispose()
        }
    }
    finally {
        $process.Dispose()
    }
}

function Test-BytesHaveUtf8Bom {
    param([byte[]] $Bytes)
    $null -ne $Bytes -and $Bytes.Length -ge 3 -and $Bytes[0] -eq 0xEF -and $Bytes[1] -eq 0xBB -and $Bytes[2] -eq 0xBF
}

function Test-TrackedTextPath {
    param([Parameter(Mandatory)] [string] $RelativePath)
    $leaf = Split-Path -Path $RelativePath -Leaf
    if ($textExtensions.Contains($leaf)) { return $true }
    $extension = [System.IO.Path]::GetExtension($leaf)
    return -not [string]::IsNullOrEmpty($extension) -and $textExtensions.Contains($extension)
}

function Get-NameStatusEntries {
    param(
        [Parameter(Mandatory)] [string] $Root,
        [Parameter(Mandatory)] [string[]] $DiffArguments
    )

    $diff = Invoke-GitRaw -Root $Root -Arguments $DiffArguments
    if ($diff.ExitCode -ne 0) {
        throw "git diff 失败（$($DiffArguments -join ' ')）：$($diff.Error)"
    }
    $text = [System.Text.Encoding]::UTF8.GetString($diff.Bytes)
    if ([string]::IsNullOrEmpty($text)) { return @() }
    $tokens = $text.Split([char] 0, [System.StringSplitOptions]::RemoveEmptyEntries)
    $entries = [System.Collections.Generic.List[object]]::new()
    for ($index = 0; $index + 1 -lt $tokens.Count; $index += 2) {
        $status = $tokens[$index]
        $path = $tokens[$index + 1]
        if (Test-TrackedTextPath -RelativePath $path) {
            $entries.Add([pscustomobject]@{ Status = $status; Path = $path })
        }
    }
    return @($entries)
}

function Get-BlobHasBom {
    param(
        [Parameter(Mandatory)] [string] $Root,
        [Parameter(Mandatory)] [string] $Spec
    )

    $blob = Invoke-GitRaw -Root $Root -Arguments @('cat-file', 'blob', $Spec)
    if ($blob.ExitCode -ne 0) {
        throw "git cat-file blob $Spec 失败：$($blob.Error)"
    }
    return Test-BytesHaveUtf8Bom -Bytes $blob.Bytes
}

function Get-ScopeBomViolations {
    param(
        [Parameter(Mandatory)] [string] $Root,
        [Parameter(Mandatory)] [ValidateSet('committed', 'staged', 'worktree')] [string] $Scope,
        [string] $BaseSpec,
        [string] $CandidateSpec
    )

    $violations = [System.Collections.Generic.List[string]]::new()
    if ($Scope -eq 'committed') {
        $entries = @(Get-NameStatusEntries -Root $Root -DiffArguments @('diff', '--no-renames', '--name-status', '-z', '--diff-filter=AM', $BaseSpec, $CandidateSpec, '--'))
        foreach ($entry in $entries) {
            $candidateHasBom = Get-BlobHasBom -Root $Root -Spec "${CandidateSpec}:$($entry.Path)"
            if (-not $candidateHasBom) { continue }
            if ($entry.Status -eq 'A') {
                $violations.Add("committed: 新建文本文件 $($entry.Path) 带有 UTF-8 BOM（应为无 BOM UTF-8）")
            }
            elseif (-not (Get-BlobHasBom -Root $Root -Spec "${BaseSpec}:$($entry.Path)")) {
                $violations.Add("committed: $($entry.Path) 引入了 UTF-8 BOM（基线原文件无 BOM）")
            }
        }
    }
    elseif ($Scope -eq 'staged') {
        $diffArgs = if ([string]::IsNullOrWhiteSpace($BaseSpec)) {
            @('diff', '--cached', '--no-renames', '--name-status', '-z', '--diff-filter=AM', '--')
        }
        else {
            @('diff', '--cached', '--no-renames', '--name-status', '-z', '--diff-filter=AM', $BaseSpec, '--')
        }
        $entries = @(Get-NameStatusEntries -Root $Root -DiffArguments $diffArgs)
        foreach ($entry in $entries) {
            $stagedHasBom = Get-BlobHasBom -Root $Root -Spec ":$($entry.Path)"
            if (-not $stagedHasBom) { continue }
            if ($entry.Status -eq 'A' -or [string]::IsNullOrWhiteSpace($BaseSpec)) {
                $violations.Add("staged: 新建文本文件 $($entry.Path) 带有 UTF-8 BOM（应为无 BOM UTF-8）")
            }
            elseif (-not (Get-BlobHasBom -Root $Root -Spec "${BaseSpec}:$($entry.Path)")) {
                $violations.Add("staged: $($entry.Path) 引入了 UTF-8 BOM（HEAD 原文件无 BOM）")
            }
        }
    }
    else {
        $entries = @(Get-NameStatusEntries -Root $Root -DiffArguments @('diff', '--no-renames', '--name-status', '-z', '--diff-filter=M', '--'))
        foreach ($entry in $entries) {
            $fullPath = Join-Path $Root ($entry.Path -replace '/', '\')
            if (-not (Test-Path -LiteralPath $fullPath -PathType Leaf)) { continue }
            $worktreeHasBom = Test-BytesHaveUtf8Bom -Bytes ([System.IO.File]::ReadAllBytes($fullPath))
            if ($worktreeHasBom -and -not (Get-BlobHasBom -Root $Root -Spec ":$($entry.Path)")) {
                $violations.Add("worktree: $($entry.Path) 引入了 UTF-8 BOM（暂存区原文件无 BOM）")
            }
        }
    }
    return @($violations)
}

function Test-RepositoryBomDrift {
    param(
        [Parameter(Mandatory)] [string] $Root,
        [string] $Base,
        [string] $Candidate = 'HEAD',
        [switch] $OnlyStaged
    )

    $resolvedRoot = (Resolve-Path -LiteralPath $Root).Path
    $checks = [System.Collections.Generic.List[object]]::new()
    $failures = [System.Collections.Generic.List[string]]::new()

    if ($OnlyStaged) {
        $headCheck = Invoke-GitRaw -Root $resolvedRoot -Arguments @('rev-parse', '--verify', '--end-of-options', 'HEAD^{commit}')
        $headSpec = if ($headCheck.ExitCode -eq 0) { [System.Text.Encoding]::UTF8.GetString($headCheck.Bytes).Trim() } else { '' }
        $stagedViolations = @(Get-ScopeBomViolations -Root $resolvedRoot -Scope 'staged' -BaseSpec $headSpec)
        $checks.Add([pscustomobject]@{ Name = 'staged'; Valid = ($stagedViolations.Count -eq 0); Violations = $stagedViolations })
        foreach ($violation in $stagedViolations) { $failures.Add($violation) }
    }
    else {
        if ([string]::IsNullOrWhiteSpace($Base)) { $Base = 'origin/main' }
        $candidateCheck = Invoke-GitRaw -Root $resolvedRoot -Arguments @('rev-parse', '--verify', '--end-of-options', "${Candidate}^{commit}")
        if ($candidateCheck.ExitCode -ne 0) { throw "无法解析候选提交 ${Candidate}：$($candidateCheck.Error)" }
        $candidateHash = [System.Text.Encoding]::UTF8.GetString($candidateCheck.Bytes).Trim()

        $baseCheck = Invoke-GitRaw -Root $resolvedRoot -Arguments @('rev-parse', '--verify', '--end-of-options', "${Base}^{commit}")
        if ($baseCheck.ExitCode -eq 0) {
            $baseHash = [System.Text.Encoding]::UTF8.GetString($baseCheck.Bytes).Trim()
        }
        else {
            $emptyTreeHash = '4b825dc642cb6eb9a060e54bf8d69288fbee4904'
            if ($Base -cne $emptyTreeHash) { throw "无法解析基线提交 ${Base}：$($baseCheck.Error)" }
            $baseHash = $emptyTreeHash
        }

        foreach ($scope in @('committed', 'staged', 'worktree')) {
            $scopeBase = if ($scope -eq 'staged') { $candidateHash } else { $baseHash }
            $scopeViolations = @(Get-ScopeBomViolations -Root $resolvedRoot -Scope $scope -BaseSpec $scopeBase -CandidateSpec $candidateHash)
            $checks.Add([pscustomobject]@{ Name = $scope; Valid = ($scopeViolations.Count -eq 0); Violations = $scopeViolations })
            foreach ($violation in $scopeViolations) { $failures.Add($violation) }
        }
    }

    [pscustomobject]@{
        Valid = ($failures.Count -eq 0)
        Checks = @($checks)
        Failures = @($failures)
    }
}

function Invoke-Utf8BomSelfTest {
    $fixtureRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("clock-countdown-bom-drift-" + [guid]::NewGuid().ToString('N'))
    [void] [System.IO.Directory]::CreateDirectory($fixtureRoot)
    $utf8NoBom = [System.Text.UTF8Encoding]::new($false)
    $utf8Bom = [System.Text.UTF8Encoding]::new($true)
    $cases = [System.Collections.Generic.List[string]]::new()
    $failures = [System.Collections.Generic.List[string]]::new()

    function Assert-Case {
        param([string] $Name, [bool] $Condition, [string] $Message)
        if ($Condition) { $cases.Add($Name) } else { $failures.Add("${Name}: $Message") }
    }

    try {
        foreach ($gitArgs in @(
                @('init', '--quiet', '--initial-branch=fixture'),
                @('config', 'user.name', 'BOM fixture'),
                @('config', 'user.email', 'bom@example.invalid'),
                @('config', 'core.autocrlf', 'false')
            )) {
            $run = Invoke-GitRaw -Root $fixtureRoot -Arguments $gitArgs
            if ($run.ExitCode -ne 0) { throw "fixture git failed: $($run.Error)" }
        }

        [System.IO.File]::WriteAllText((Join-Path $fixtureRoot 'clean.ps1'), "Write-Output 'base'`n", $utf8NoBom)
        [System.IO.File]::WriteAllText((Join-Path $fixtureRoot 'legacy-bom.ps1'), "Write-Output 'legacy'`n", $utf8Bom)
        [void] (Invoke-GitRaw -Root $fixtureRoot -Arguments @('add', 'clean.ps1', 'legacy-bom.ps1'))
        [void] (Invoke-GitRaw -Root $fixtureRoot -Arguments @('commit', '--quiet', '-m', 'base'))
        $baseHash = [System.Text.Encoding]::UTF8.GetString((Invoke-GitRaw -Root $fixtureRoot -Arguments @('rev-parse', 'HEAD')).Bytes).Trim()

        # 1. Clean edit on no-BOM file + keeping BOM on legacy BOM file both pass.
        [System.IO.File]::WriteAllText((Join-Path $fixtureRoot 'clean.ps1'), "Write-Output 'updated'`n", $utf8NoBom)
        [System.IO.File]::WriteAllText((Join-Path $fixtureRoot 'legacy-bom.ps1'), "Write-Output 'legacy-2'`n", $utf8Bom)
        [void] (Invoke-GitRaw -Root $fixtureRoot -Arguments @('add', 'clean.ps1', 'legacy-bom.ps1'))
        $r1 = Test-RepositoryBomDrift -Root $fixtureRoot -Base $baseHash -Candidate 'HEAD'
        $r1Staged = Test-RepositoryBomDrift -Root $fixtureRoot -OnlyStaged
        Assert-Case 'clean-and-legacy-bom-pass' ($r1.Valid -and $r1Staged.Valid) ($r1.Failures -join '; ')

        # 2. Unstaged BOM drift on clean.ps1 fails full check, but -StagedOnly ignores unstaged worktree.
        [System.IO.File]::WriteAllText((Join-Path $fixtureRoot 'clean.ps1'), "Write-Output 'bom-worktree'`n", $utf8Bom)
        $r2 = Test-RepositoryBomDrift -Root $fixtureRoot -Base $baseHash -Candidate 'HEAD'
        $r2Staged = Test-RepositoryBomDrift -Root $fixtureRoot -OnlyStaged
        Assert-Case 'worktree-bom-detected-and-staged-only-ignores-worktree' ((-not $r2.Valid) -and $r2Staged.Valid) "full=$($r2.Valid) staged=$($r2Staged.Valid)"

        # 3. Staging BOM drift fails both full check and -StagedOnly.
        [void] (Invoke-GitRaw -Root $fixtureRoot -Arguments @('add', 'clean.ps1'))
        $r3 = Test-RepositoryBomDrift -Root $fixtureRoot -Base $baseHash -Candidate 'HEAD'
        $r3Staged = Test-RepositoryBomDrift -Root $fixtureRoot -OnlyStaged
        Assert-Case 'staged-bom-drift-detected' ((-not $r3.Valid) -and (-not $r3Staged.Valid)) "full=$($r3.Valid) staged=$($r3Staged.Valid)"

        # 4. Committed BOM drift fails committed check even after reverting index.
        [void] (Invoke-GitRaw -Root $fixtureRoot -Arguments @('commit', '--quiet', '-m', 'bom-drift'))
        $badCommit = [System.Text.Encoding]::UTF8.GetString((Invoke-GitRaw -Root $fixtureRoot -Arguments @('rev-parse', 'HEAD')).Bytes).Trim()
        $r4 = Test-RepositoryBomDrift -Root $fixtureRoot -Base $baseHash -Candidate $badCommit
        Assert-Case 'committed-bom-drift-detected' (-not $r4.Valid -and @($r4.Checks | Where-Object { $_.Name -eq 'committed' -and -not $_.Valid }).Count -eq 1) ($r4.Failures -join '; ')

        # 5. Newly added text file with UTF-8 BOM fails staged check.
        [void] (Invoke-GitRaw -Root $fixtureRoot -Arguments @('reset', '--hard', '--quiet', $baseHash))
        [System.IO.File]::WriteAllText((Join-Path $fixtureRoot 'added-bom.ps1'), "Write-Output 'new'`n", $utf8Bom)
        [void] (Invoke-GitRaw -Root $fixtureRoot -Arguments @('add', 'added-bom.ps1'))
        $r5Staged = Test-RepositoryBomDrift -Root $fixtureRoot -OnlyStaged
        Assert-Case 'added-file-with-bom-detected' (-not $r5Staged.Valid) "expected failure on added BOM file"
    }
    finally {
        if (Test-Path -LiteralPath $fixtureRoot) {
            Remove-Item -LiteralPath $fixtureRoot -Recurse -Force -ErrorAction SilentlyContinue
        }
    }

    [pscustomobject]@{
        Valid = ($failures.Count -eq 0)
        Cases = @($cases)
        Failures = @($failures)
    }
}

try {
    $result = if ($SelfTest) {
        Invoke-Utf8BomSelfTest
    }
    else {
        Test-RepositoryBomDrift -Root $RepositoryRoot -Base $BaseCommit -Candidate $CandidateCommit -OnlyStaged:$StagedOnly
    }
    if ($Json) { $result | ConvertTo-Json -Depth 8 } else { $result }
    if (-not $result.Valid) { exit 1 }
    exit 0
}
catch {
    $errorResult = [pscustomobject]@{ Valid = $false; Failures = @($_.Exception.Message) }
    if ($Json) { $errorResult | ConvertTo-Json -Depth 8 } else { Write-Error $_.Exception.Message }
    exit 1
}
