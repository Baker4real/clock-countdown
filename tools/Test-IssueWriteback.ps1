#requires -Version 7.0

[CmdletBinding()]
param([switch] $Json)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$utf8 = [System.Text.UTF8Encoding]::new($false)
[Console]::OutputEncoding = $utf8
$OutputEncoding = $utf8

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$invokePath = Join-Path $PSScriptRoot 'Invoke-IssueWriteback.ps1'
$testRoot = Join-Path ([System.IO.Path]::GetTempPath()) "clock-countdown-issue-writeback-test-$([guid]::NewGuid().ToString('N'))"
[System.IO.Directory]::CreateDirectory($testRoot) | Out-Null
$fakeGhPath = Join-Path $testRoot 'fake-gh.ps1'
$statePath = Join-Path $testRoot 'fake-gh-state.json'
$recordPath = Join-Path $testRoot 'records.json'

function Assert-True {
    param([Parameter(Mandatory)] [bool] $Condition, [Parameter(Mandatory)] [string] $Message)
    if (-not $Condition) { throw $Message }
}

function Invoke-WritebackTool {
    param([Parameter(Mandatory)] [string[]] $Arguments, [hashtable] $Environment = @{})
    $old = @{}
    foreach ($key in $Environment.Keys) {
        $old[$key] = [Environment]::GetEnvironmentVariable($key)
        [Environment]::SetEnvironmentVariable($key, [string] $Environment[$key])
    }
    try {
        $output = @(& pwsh -NoLogo -NoProfile -File $invokePath @Arguments 2>&1)
        $exitCode = $LASTEXITCODE
        $text = ($output -join [Environment]::NewLine).Trim()
        $parsed = $null
        if (-not [string]::IsNullOrWhiteSpace($text)) {
            try { $parsed = $text | ConvertFrom-Json -Depth 30 -ErrorAction Stop } catch { throw "工具输出不是 JSON：$text" }
        }
        return [pscustomobject]@{ ExitCode = $exitCode; Text = $text; Json = $parsed }
    }
    finally {
        foreach ($key in $Environment.Keys) { [Environment]::SetEnvironmentVariable($key, $old[$key]) }
    }
}

try {
    @'
#requires -Version 7.0
[CmdletBinding()]
param([Parameter(ValueFromRemainingArguments = $true)] [string[]] $Arguments)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$statePath = [Environment]::GetEnvironmentVariable('ISSUE_WRITEBACK_FAKE_STATE')
$mode = [Environment]::GetEnvironmentVariable('ISSUE_WRITEBACK_FAKE_MODE')
$state = if (Test-Path -LiteralPath $statePath -PathType Leaf) {
    Get-Content -LiteralPath $statePath -Raw -Encoding UTF8 | ConvertFrom-Json -Depth 20
} else {
    [pscustomobject]@{ nextId = 7000; comments = @(); closed = @() }
}
$method = 'GET'
for ($i = 0; $i -lt $Arguments.Count - 1; $i++) {
    if ($Arguments[$i] -eq '--method') { $method = $Arguments[$i + 1] }
}
$endpoint = @($Arguments | Where-Object { $_ -match '^repos/.+/issues/\d+' }) | Select-Object -First 1
$issueNumber = [int] ([regex]::Match($endpoint, '/issues/(\d+)').Groups[1].Value)
$inputIndex = [Array]::IndexOf($Arguments, '--input')
$inputPath = if ($inputIndex -ge 0) { $Arguments[$inputIndex + 1] } else { $null }
if ($endpoint -match '/comments$' -and $method -eq 'POST') {
    if ($mode -eq 'mid-failure' -and $issueNumber -eq 102) { Write-Error 'fake mid-flight failure'; exit 17 }
    $body = (Get-Content -LiteralPath $inputPath -Raw -Encoding UTF8 | ConvertFrom-Json).body
    $id = [long] $state.nextId
    $state.nextId = $id + 1
    $state.comments = @($state.comments) + [pscustomobject]@{ id = $id; issue = $issueNumber; body = $body; html_url = "https://fake.invalid/comment/$id" }
    $state | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $statePath -Encoding UTF8
    [pscustomobject]@{ id = $id; html_url = "https://fake.invalid/comment/$id"; body = $body } | ConvertTo-Json -Compress
    exit 0
}
if ($endpoint -match '/comments$') {
    $comments = @($state.comments | Where-Object issue -eq $issueNumber)
    if ($mode -eq 'body-mismatch' -and $comments.Count -gt 0) { $comments[0].body = "$($comments[0].body)\nFAKE-MISMATCH" }
    $comments | ConvertTo-Json -Depth 20 -Compress
    exit 0
}
if ($method -eq 'PATCH') {
    $state.closed = @($state.closed) + $issueNumber
    $state | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $statePath -Encoding UTF8
    [pscustomobject]@{ number = $issueNumber; state = 'closed' } | ConvertTo-Json -Compress
    exit 0
}
$stateValue = if ($mode -eq 'state-mismatch' -and $issueNumber -eq 101) { 'open' } elseif ($issueNumber -in @($state.closed)) { 'closed' } else { 'open' }
[pscustomobject]@{ number = $issueNumber; state = $stateValue } | ConvertTo-Json -Compress
'@ | Set-Content -LiteralPath $fakeGhPath -Encoding UTF8

    $records = [ordered]@{
        parentIssue = 900
        issues = @(
            [ordered]@{
                number = 101; taskType = 'docs'; requiredChecks = @('diff-check', 'docs-links')
                evidence = @(
                    [ordered]@{ check = 'diff-check'; status = 'processed'; detail = 'fake diff clean' }
                    [ordered]@{ check = 'docs-links'; status = 'processed'; detail = 'fake links resolved' }
                )
                notApplicable = @([ordered]@{ check = 'runtime-behavior'; reason = '文档 issue 未触达运行时表面。' })
                status = 'closed'; summary = '文档证据齐全'; missing = @(); next = '无。'
            }
            [ordered]@{
                number = 102; taskType = 'docs'; requiredChecks = @('diff-check', 'docs-links')
                evidence = @(
                    [ordered]@{ check = 'diff-check'; status = 'processed'; detail = 'fake diff clean' }
                    [ordered]@{ check = 'docs-links'; status = 'processed'; detail = 'fake links resolved' }
                )
                notApplicable = @([ordered]@{ check = 'runtime-behavior'; reason = '文档 issue 未触达运行时表面。' })
                status = 'open'; summary = '仍需后续验收'; missing = @('待补充发布证据'); next = '补充发布证据后重跑。'
            }
        )
    }
    [System.IO.File]::WriteAllText($recordPath, ($records | ConvertTo-Json -Depth 20), $utf8)

    $results = [System.Collections.Generic.List[object]]::new()
    $dryDir = Join-Path $testRoot 'dry-run'
    $dry = Invoke-WritebackTool -Arguments @('-RecordPath', $recordPath, '-Repo', 'fake/example', '-GhPath', $fakeGhPath, '-ParentIssue', '900', '-OutputDirectory', $dryDir, '-Json') -Environment @{
        ISSUE_WRITEBACK_FAKE_STATE = $statePath; ISSUE_WRITEBACK_FAKE_MODE = 'success'
    }
    Assert-True ($dry.ExitCode -eq 0 -and $dry.Json.Valid) 'dry-run 未通过。'
    Assert-True ((Test-Path -LiteralPath (Join-Path $dryDir 'issue-101.md')) -and (Test-Path -LiteralPath (Join-Path $dryDir 'parent-900.md'))) 'dry-run 没有持久化逐票正文和父票汇总。'
    $parentText = Get-Content -LiteralPath (Join-Path $dryDir 'parent-900.md') -Raw -Encoding UTF8
    Assert-True ($parentText -match '#101' -and $parentText -match '#102' -and $parentText -match 'closed' -and $parentText -match 'open') '父票汇总缺少逐票状态。'
    Assert-True (-not (Test-Path -LiteralPath $statePath -PathType Leaf)) 'dry-run 意外访问 fake gh。'
    $results.Add([pscustomobject]@{ Name = 'dry-run-persistent-output'; Passed = $true })

    $successDir = Join-Path $testRoot 'success'
    $success = Invoke-WritebackTool -Arguments @('-RecordPath', $recordPath, '-Repo', 'fake/example', '-GhPath', $fakeGhPath, '-Apply', '-CloseSatisfied', '-OutputDirectory', $successDir, '-Json') -Environment @{
        ISSUE_WRITEBACK_FAKE_STATE = $statePath; ISSUE_WRITEBACK_FAKE_MODE = 'success'
    }
    Assert-True ($success.ExitCode -eq 0 -and $success.Json.Valid) '成功 Apply 未通过。'
    $successLedger = Get-Content -LiteralPath (Join-Path $successDir 'ledger.json') -Raw | ConvertFrom-Json -Depth 30
    Assert-True (@($successLedger.issues | Where-Object status -ne 'processed').Count -eq 0 -and $successLedger.parent.status -eq 'processed') '成功 ledger 未记录逐票和父票 processed。'
    Assert-True (@($successLedger.issues | Where-Object { $_.commentId -le 0 -or -not $_.commentReadback.bodyMatches }).Count -eq 0) '成功 ledger 缺少评论 ID 或正文回读。'
    $results.Add([pscustomobject]@{ Name = 'success-independent-readback'; Passed = $true })

    $reuseFailures = [Collections.Generic.List[string]]::new()
    foreach ($reuseApply in @($false, $true)) {
        $caseName = if ($reuseApply) { 'reuse-apply-output' } else { 'reuse-dry-run-output' }
        $reuseDir = Join-Path $testRoot $caseName
        Copy-Item -LiteralPath $successDir -Destination $reuseDir -Recurse
        $snapshot = {
            @(Get-ChildItem -LiteralPath $reuseDir -File -Recurse | Sort-Object FullName | ForEach-Object {
                [pscustomobject]@{ Path = $_.FullName; Length = $_.Length; LastWriteTimeUtc = $_.LastWriteTimeUtc.ToString('o'); SHA256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }
            }) | ConvertTo-Json -Depth 4 -Compress
        }
        $before = & $snapshot
        $fakeBefore = (Get-FileHash -LiteralPath $statePath -Algorithm SHA256).Hash
        $reuseArguments = @('-RecordPath', $recordPath, '-Repo', 'fake/example', '-GhPath', $fakeGhPath, '-OutputDirectory', $reuseDir, '-Json')
        if ($reuseApply) { $reuseArguments += @('-Apply', '-CloseSatisfied') }
        $reuse = Invoke-WritebackTool -Arguments $reuseArguments -Environment @{
            ISSUE_WRITEBACK_FAKE_STATE = $statePath; ISSUE_WRITEBACK_FAKE_MODE = 'success'
        }
        $preserved = $before -ceq (& $snapshot)
        $fakeUnchanged = $fakeBefore -ceq (Get-FileHash -LiteralPath $statePath -Algorithm SHA256).Hash
        $rejected = $reuse.ExitCode -ne 0 -and -not $reuse.Json.Valid
        $passed = $rejected -and $preserved -and $fakeUnchanged -and $null -eq $reuse.Json.OutputDirectory
        $results.Add([pscustomobject]@{ Name = $caseName; Passed = $passed; Rejected = $rejected; BytesAndTimesPreserved = $preserved; FakeGhUnchanged = $fakeUnchanged })
        if (-not $passed) { $reuseFailures.Add("${caseName}: rejected=$rejected, bytes-and-mtime-preserved=$preserved, fake-gh-unchanged=$fakeUnchanged") }
    }
    Assert-True ($reuseFailures.Count -eq 0) ("复用输出目录未保护既有证据：" + ($reuseFailures -join '; '))

    foreach ($case in @(
        [pscustomobject]@{ Name = 'body-mismatch'; Mode = 'body-mismatch'; Message = '正文 SHA 不符' }
        [pscustomobject]@{ Name = 'state-mismatch'; Mode = 'state-mismatch'; Message = '状态回读不符' }
        [pscustomobject]@{ Name = 'mid-failure'; Mode = 'mid-failure'; Message = '中途失败' }
    )) {
        if (Test-Path -LiteralPath $statePath) { Remove-Item -LiteralPath $statePath -Force }
        $outDir = Join-Path $testRoot $case.Name
        $attempt = Invoke-WritebackTool -Arguments @('-RecordPath', $recordPath, '-Repo', 'fake/example', '-GhPath', $fakeGhPath, '-Apply', '-CloseSatisfied', '-OutputDirectory', $outDir, '-Json') -Environment @{
            ISSUE_WRITEBACK_FAKE_STATE = $statePath; ISSUE_WRITEBACK_FAKE_MODE = $case.Mode
        }
        Assert-True ($attempt.ExitCode -ne 0 -and -not $attempt.Json.Valid) "$($case.Name) 未失败闭锁。"
        Assert-True (Test-Path -LiteralPath (Join-Path $outDir 'ledger.json')) "$($case.Name) 未保留 ledger。"
        $ledger = Get-Content -LiteralPath (Join-Path $outDir 'ledger.json') -Raw | ConvertFrom-Json -Depth 30
        if ($case.Name -eq 'mid-failure') {
            Assert-True ($ledger.issues[0].status -eq 'processed' -and $ledger.issues[1].status -eq 'failed' -and $ledger.parent.status -eq 'skipped') '中途失败没有保留已完成项或停止父票。'
        }
        else {
            Assert-True (@($ledger.issues | Where-Object status -eq 'failed').Count -ge 1) "$($case.Name) 未记录 failed issue。"
        }
        $results.Add([pscustomobject]@{ Name = $case.Name; Passed = $true })
    }

    $result = [pscustomobject]@{ Valid = $true; Tests = @($results); FakeGhUsed = $true; NetworkWrites = $false; Root = $testRoot }
    if ($Json) { $result | ConvertTo-Json -Depth 10 } else { $result | ConvertTo-Json -Depth 10 }
    exit 0
}
catch {
    $result = [pscustomobject]@{ Valid = $false; Error = $_.Exception.Message; Root = $testRoot }
    if ($Json) { $result | ConvertTo-Json -Depth 10 } else { Write-Error "issue-writeback-test: $($_.Exception.Message)" }
    exit 1
}
