#requires -Version 7.0

[CmdletBinding()]
param(
    [string] $RecordPath,
    [string] $Repo,
    [switch] $Apply,
    [switch] $CloseSatisfied,
    [switch] $SelfTest,
    [switch] $Json,
    [string] $OutputDirectory,
    [string] $ResumeFrom,
    [ValidateRange(1, 5)] [int] $GetAttempts = 3,
    [ValidateRange(0, 30)] [int] $GetRetryDelaySeconds = 1,
    [int] $ParentIssue,
    [string] $GhPath = 'gh'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$utf8 = [System.Text.UTF8Encoding]::new($false)
[Console]::OutputEncoding = $utf8
[Console]::InputEncoding = $utf8
$OutputEncoding = $utf8

$scriptRoot = Split-Path -Parent $PSCommandPath
$repoRoot = (Resolve-Path (Join-Path $scriptRoot '..')).Path
$schemaPath = Join-Path $repoRoot 'docs/agents/issue-acceptance.json'
$validatorPath = Join-Path $scriptRoot 'Test-IssueAcceptance.ps1'
$script:outputRoot = $null
$script:ledger = $null
$script:resumeRoot = $null

function Write-JsonFile {
    param([Parameter(Mandatory)] [string] $Path, [Parameter(Mandatory)] [object] $Value, [int] $Depth = 30)
    $temporary = "$Path.tmp"
    [System.IO.File]::WriteAllText($temporary, ($Value | ConvertTo-Json -Depth $Depth), $utf8)
    [System.IO.File]::Move($temporary, $Path, $true)
}

function Read-JsonFile {
    param([Parameter(Mandatory)] [string] $Path)
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { throw "记录不存在：$Path" }
    $raw = Get-Content -LiteralPath $Path -Raw -Encoding UTF8
    if ([string]::IsNullOrWhiteSpace($raw)) { throw "JSON 文件为空：$Path" }
    return $raw | ConvertFrom-Json -Depth 40 -ErrorAction Stop
}

function Get-TextSha256 {
    param([Parameter(Mandatory)] [string] $Text)
    $bytes = $utf8.GetBytes($Text.Trim())
    return ([System.Security.Cryptography.SHA256]::HashData($bytes) |
        ForEach-Object { $_.ToString('X2', [Globalization.CultureInfo]::InvariantCulture) }) -join ''
}

function Get-Records {
    param([Parameter(Mandatory)] [object] $Document)
    $records = if ($Document.PSObject.Properties.Name -contains 'issues') { @($Document.issues) } else { @($Document) }
    if ($records.Count -eq 0) { throw '记录没有 issues。' }
    return $records
}

function Resolve-ParentIssueNumber {
    param([Parameter(Mandatory)] [object] $Document)
    if ($ParentIssue -gt 0) { return $ParentIssue }
    if (-not ($Document.PSObject.Properties.Name -contains 'parentIssue') -or $null -eq $Document.parentIssue) { return 0 }
    if ($Document.parentIssue -is [int] -or $Document.parentIssue -is [long]) { return [int] $Document.parentIssue }
    if ($Document.parentIssue.PSObject.Properties.Name -contains 'number') { return [int] $Document.parentIssue.number }
    return [int] $Document.parentIssue
}

function Assert-WritebackRecords {
    param([Parameter(Mandatory)] [object[]] $Records)
    $seen = [System.Collections.Generic.HashSet[int]]::new()
    foreach ($record in $Records) {
        $number = 0
        if (-not [int]::TryParse([string] $record.number, [ref] $number) -or $number -le 0) { throw "issue number 无效：$($record.number)" }
        if (-not $seen.Add($number)) { throw "issue 重复：$number" }
        foreach ($field in @('taskType', 'requiredChecks', 'evidence', 'notApplicable', 'status', 'summary', 'missing', 'next')) {
            if (-not ($record.PSObject.Properties.Name -contains $field)) { throw "issue $number 缺少字段：$field" }
        }
        if ([string] $record.status -notin @('candidate', 'open', 'closed')) { throw "issue $number 使用未知状态：$($record.status)" }
        if ([string]::IsNullOrWhiteSpace([string] $record.summary)) { throw "issue $number 缺少 summary。" }
        if ([string]::IsNullOrWhiteSpace([string] $record.next)) { throw "issue $number 缺少 next。" }
        $required = @($record.requiredChecks)
        foreach ($entry in @($record.notApplicable)) {
            if ([string]::IsNullOrWhiteSpace([string] $entry.check) -or [string] $entry.check -in $required) { throw "issue $number 不能把 required 检查标为 notApplicable：$($entry.check)" }
            if ([string]::IsNullOrWhiteSpace([string] $entry.reason)) { throw "issue $number 的 $($entry.check) 缺少免测理由。" }
        }
        if ([string] $record.status -eq 'closed') {
            if (@($record.missing).Count -gt 0) { throw "issue $number 标为 closed 但仍有 missing。" }
            foreach ($check in $required) {
                $processed = @($record.evidence | Where-Object {
                    [string] $_.check -eq [string] $check -and [string] $_.status -eq 'processed' -and
                    -not [string]::IsNullOrWhiteSpace([string] $_.detail)
                })
                if ($processed.Count -eq 0) { throw "issue $number 标为 closed 但缺少带 detail 的 processed 证据：$check" }
            }
        }
    }
}

function Convert-WritebackComment {
    param([Parameter(Mandatory)] [object] $Record)
    $lines = [System.Collections.Generic.List[string]]::new()
    $lines.Add(('验收回写：issue #{0}（类型 `{1}`，状态 `{2}`）。' -f $Record.number, $Record.taskType, $Record.status))
    $lines.Add('')
    $lines.Add("**摘要**：$($Record.summary)")
    $lines.Add('')
    $lines.Add('**已具备证据**')
    $lines.Add('')
    $evidence = @($Record.evidence | ForEach-Object {
        $status = if ([string]::IsNullOrWhiteSpace([string] $_.status)) { 'unknown' } else { [string] $_.status }
        "- [$status] $($_.check)：$($_.detail)"
    })
    if ($evidence.Count -eq 0) { $lines.Add('- 无。') } else { $evidence | ForEach-Object { $lines.Add($_) } }
    $na = @($Record.notApplicable | ForEach-Object { "- $($_.check)：$($_.reason)" })
    if ($na.Count -gt 0) {
        $lines.Add(''); $lines.Add('**明确不适用**'); $lines.Add(''); $na | ForEach-Object { $lines.Add($_) }
    }
    $lines.Add(''); $lines.Add('**未完成项**'); $lines.Add('')
    $missing = @($Record.missing)
    if ($missing.Count -eq 0) { $lines.Add('- 无。') } else { $missing | ForEach-Object { $lines.Add("- $_") } }
    $lines.Add(''); $lines.Add('**下一步**'); $lines.Add(''); $lines.Add([string] $Record.next); $lines.Add('')
    $lines.Add('_此评论由结构化验收记录生成；candidate、dry-run、deployed 和 live verified 分开记录。_')
    return ($lines -join [Environment]::NewLine)
}

function Convert-ParentSummary {
    param([Parameter(Mandatory)] [int] $Number, [Parameter(Mandatory)] [object[]] $Records, [Parameter(Mandatory)] [string] $RepoName)
    $lines = [System.Collections.Generic.List[string]]::new()
    $lines.Add("验收回写汇总：父 issue #$Number"); $lines.Add('')
    $lines.Add('本次由结构化记录生成，逐票正文与回读账本保存在本次输出目录。'); $lines.Add('')
    $lines.Add('| Issue | 类型 | 状态 | 缺口 | 下一步 |'); $lines.Add('| --- | --- | --- | --- | --- |')
    foreach ($record in $Records) {
        $missing = @($record.missing); $missingText = if ($missing.Count -eq 0) { '无' } else { ($missing -join '<br>') }
        $next = ([string] $record.next) -replace '\|', '\\|'
        $issueRef = if ([string]::IsNullOrWhiteSpace($RepoName)) { "#$($record.number)" } else { "[#$($record.number)](https://github.com/$RepoName/issues/$($record.number))" }
        $lines.Add("| $issueRef | $($record.taskType) | $($record.status) | $missingText | $next |")
    }
    $lines.Add(''); $lines.Add('_父票只有在所有子票写入与独立回读完成后才允许 Apply；任何失败都会停止父票写入。_')
    return ($lines -join [Environment]::NewLine)
}

function Invoke-GhJson {
    param([Parameter(Mandatory)] [string[]] $Arguments)
    $isGet = '--method' -notin $Arguments -or ($Arguments[[Array]::IndexOf($Arguments, '--method') + 1] -eq 'GET')
    $attempts = if ($isGet) { $GetAttempts } else { 1 }
    for ($attempt = 1; $attempt -le $attempts; $attempt++) {
        try { $output = @(& $GhPath @Arguments 2>&1); $exitCode = $LASTEXITCODE }
        catch { $output = @($_.Exception.Message); $exitCode = 1 }
        if ($exitCode -eq 0 -or $attempt -eq $attempts) { break }
        if ($GetRetryDelaySeconds -gt 0) { Start-Sleep -Seconds $GetRetryDelaySeconds }
    }
    $text = ($output -join [Environment]::NewLine).Trim()
    if ($exitCode -ne 0) { throw "gh 失败（$exitCode）：$text" }
    if ([string]::IsNullOrWhiteSpace($text)) { throw "gh 没有返回 JSON：$($Arguments -join ' ')" }
    try { return $text | ConvertFrom-Json -Depth 40 -ErrorAction Stop } catch { throw "gh 返回了无效 JSON：$text" }
}

function Get-IssueState {
    param([Parameter(Mandatory)] [int] $Number, [Parameter(Mandatory)] [string] $RepoName)
    return [string] (Invoke-GhJson -Arguments @('api', "repos/$RepoName/issues/$Number")).state
}

function Get-CommentReadback {
    param([Parameter(Mandatory)] [int] $Number, [Parameter(Mandatory)] [string] $RepoName, [Parameter(Mandatory)] [long] $CommentId, [Parameter(Mandatory)] [string] $ExpectedBody)
    $pages = @(Invoke-GhJson -Arguments @('api', "repos/$RepoName/issues/$Number/comments", '--paginate', '--slurp'))
    $comments = @($pages | ForEach-Object {
        if ($_ -is [System.Array]) { $_ | ForEach-Object { $_ } } else { $_ }
    })
    $comment = $comments | Where-Object { [long] $_.id -eq $CommentId } | Select-Object -First 1
    if ($null -eq $comment) { throw "issue $Number 的评论 $CommentId 独立回读不到。" }
    $actualHash = Get-TextSha256 -Text ([string] $comment.body); $expectedHash = Get-TextSha256 -Text $ExpectedBody
    if ($expectedHash -ne $actualHash) { throw "issue $Number 的评论正文回读 SHA 不符（期望 $expectedHash，实际 $actualHash）。" }
    return [ordered]@{ commentId = $CommentId; commentUrl = [string] $comment.html_url; bodySha256 = $actualHash; bodyMatches = $true }
}

function Write-Ledger {
    if ($null -ne $script:ledger -and $null -ne $script:outputRoot) { Write-JsonFile -Path (Join-Path $script:outputRoot 'ledger.json') -Value $script:ledger }
}

function Write-IssueResult {
    param([Parameter(Mandatory)] [object] $Entry)
    Write-JsonFile -Path (Join-Path $script:outputRoot "issue-$($Entry.number).result.json") -Value $Entry
}

function Invoke-CommentTransaction {
    param([int] $Number, [string] $Body, [string] $BodyPath, [string] $ExpectedState, [string] $Prefix)
    $resultName = "$Prefix-$Number.result.json"
    $resultPath = Join-Path $script:outputRoot $resultName
    $entry = [ordered]@{ number = $Number; status = 'running'; action = 'comment-and-readback'; bodyPath = $BodyPath; expectedBodySha256 = Get-TextSha256 -Text $Body; expectedIssueState = $ExpectedState; phase = 'ready' }
    try {
        if ($null -ne $script:resumeRoot) {
            $previousPath = Join-Path $script:resumeRoot $resultName
            if (Test-Path -LiteralPath $previousPath -PathType Leaf) {
                $previous = Read-JsonFile -Path $previousPath
                if ($previous.PSObject.Properties.Name -contains 'commentId' -and [long] $previous.commentId -gt 0) {
                    if ([string] $previous.expectedBodySha256 -cne $entry.expectedBodySha256 -or [string] $previous.expectedIssueState -cne $ExpectedState) { throw "issue $Number 恢复记录与本次正文或目标状态不符。" }
                    $entry.commentId = [long] $previous.commentId
                    $entry.phase = 'comment-posted'
                }
                elseif ([string] $previous.status -ne 'skipped') {
                    throw "issue $Number 的先前写入结果未知；拒绝自动重复 POST，请先独立核对评论。"
                }
            }
        }
        Write-JsonFile -Path $resultPath -Value $entry
        if (-not $entry.Contains('commentId')) {
            $requestPath = Join-Path $script:outputRoot "$Prefix-$Number.comment-request.json"
            Write-JsonFile -Path $requestPath -Value ([ordered]@{ body = $Body })
            # Persist intent before POST: a lost response must never trigger an automatic duplicate.
            $entry.phase = 'comment-pending'; Write-JsonFile -Path $resultPath -Value $entry
            $comment = Invoke-GhJson -Arguments @('api', '--method', 'POST', "repos/$Repo/issues/$Number/comments", '--input', $requestPath)
            if ([long] $comment.id -le 0) { throw "issue $Number 的 gh comment 响应没有有效 id。" }
            $entry.commentId = [long] $comment.id; $entry.commentUrl = [string] $comment.html_url
            $entry.phase = 'comment-posted'; Write-JsonFile -Path $resultPath -Value $entry
        }
        $entry.commentReadback = Get-CommentReadback -Number $Number -RepoName $Repo -CommentId $entry.commentId -ExpectedBody $Body
        $entry.phase = 'comment-verified'; Write-JsonFile -Path $resultPath -Value $entry
        $state = Get-IssueState -Number $Number -RepoName $Repo
        if ($ExpectedState -eq 'CLOSED' -and $state -ne 'CLOSED') {
            $entry.phase = 'close-pending'; Write-JsonFile -Path $resultPath -Value $entry
            [void] (Invoke-GhJson -Arguments @('api', '--method', 'PATCH', "repos/$Repo/issues/$Number", '-f', 'state=closed'))
        }
        $entry.issueState = Get-IssueState -Number $Number -RepoName $Repo
        if ($entry.issueState -ne $ExpectedState) { throw "issue $Number 状态回读不符（期望 $ExpectedState，实际 $($entry.issueState)）。" }
        $entry.phase = 'verified'; $entry.status = 'processed'
    }
    catch { $entry.status = 'failed'; $entry.error = $_.Exception.Message }
    Write-JsonFile -Path $resultPath -Value $entry
    return $entry
}

function Assert-ResumePlan {
    param([object] $Plan, [string] $ParentBody)
    if ([string]::IsNullOrWhiteSpace($ResumeFrom)) { return }
    if (-not $Apply) { throw '-ResumeFrom 只能与 -Apply 一起使用。' }
    $script:resumeRoot = (Resolve-Path -LiteralPath $ResumeFrom).Path
    $previous = Read-JsonFile -Path (Join-Path $script:resumeRoot 'plan.json')
    if (-not $previous.apply -or [string] $previous.repo -cne $Repo -or [bool] $previous.closeSatisfied -ne [bool] $CloseSatisfied -or [string] $previous.parentIssue -ne [string] $Plan.parentIssue) { throw '恢复 plan 的仓库、Apply/Close 或父票不匹配。' }
    if (@($previous.records).Count -ne @($Plan.records).Count) { throw '恢复 plan 的票数不匹配。' }
    foreach ($record in $Plan.records) {
        $old = @($previous.records | Where-Object number -eq $record.number)
        if ($old.Count -ne 1 -or [string] $old[0].bodySha256 -cne [string] $record.bodySha256 -or [string] $old[0].expectedIssueState -cne [string] $record.expectedIssueState) { throw "issue $($record.number) 恢复 plan 不匹配。" }
    }
    if ($null -ne $Plan.parentIssue) {
        $oldBody = [System.IO.File]::ReadAllText((Join-Path $script:resumeRoot "parent-$($Plan.parentIssue).md"), $utf8)
        if ((Get-TextSha256 -Text $oldBody) -cne (Get-TextSha256 -Text $ParentBody)) { throw '恢复父票正文不匹配。' }
    }
    # Seed the entire resume checkpoint before publishing its plan or making any requests.
    # An interrupted early readback must not lose later tickets' transaction state.
    $names = @($Plan.records | ForEach-Object { "issue-$($_.number).result.json" })
    if ($null -ne $Plan.parentIssue) { $names += "parent-$($Plan.parentIssue).result.json" }
    foreach ($name in $names) {
        $oldPath = Join-Path $script:resumeRoot $name
        if (Test-Path -LiteralPath $oldPath -PathType Leaf) {
            Write-JsonFile -Path (Join-Path $script:outputRoot $name) -Value (Read-JsonFile -Path $oldPath)
        }
    }
}

function Save-DeferredResult {
    param([object] $Skipped, [string] $Prefix)
    $name = "$Prefix-$($Skipped.number).result.json"
    $previousPath = if ($null -ne $script:resumeRoot) { Join-Path $script:resumeRoot $name } else { $null }
    if ($null -ne $previousPath -and (Test-Path -LiteralPath $previousPath -PathType Leaf)) {
        # Carry the full transaction state across failed resumes, including unknown POST outcomes.
        Write-JsonFile -Path (Join-Path $script:outputRoot $name) -Value (Read-JsonFile -Path $previousPath)
        $Skipped.resumeResultPreserved = $true
    }
    else { Write-JsonFile -Path (Join-Path $script:outputRoot $name) -Value $Skipped }
}

function Initialize-Output {
    if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
        $stamp = [DateTimeOffset]::UtcNow.ToString('yyyyMMddTHHmmssfffZ')
        $OutputDirectory = Join-Path $repoRoot "logs/issue-writeback/$stamp-$([guid]::NewGuid().ToString('N').Substring(0, 8))"
    }
    $candidateRoot = [System.IO.Path]::GetFullPath($OutputDirectory)
    if (Test-Path -LiteralPath $candidateRoot) {
        throw "拒绝复用已有输出目录：$candidateRoot；请保留既有证据并使用新的输出目录。"
    }
    [System.IO.Directory]::CreateDirectory($candidateRoot) | Out-Null
    # Ownership is acquired only after creating a new output directory.
    $script:outputRoot = $candidateRoot
    return $script:outputRoot
}

function New-Ledger {
    param([Parameter(Mandatory)] [string] $RepoName, [Parameter(Mandatory)] [bool] $IsApply, [Parameter(Mandatory)] [bool] $IsClose)
    $script:ledger = [ordered]@{
        schemaVersion = 1; status = 'running'; startedAtUtc = [DateTimeOffset]::UtcNow.ToString('o')
        repo = $RepoName; apply = $IsApply; closeSatisfied = $IsClose; outputDirectory = $script:outputRoot
        issues = @(); parent = $null; events = @()
    }
    Write-Ledger
}

function Add-LedgerEvent {
    param([Parameter(Mandatory)] [string] $Kind, [Parameter(Mandatory)] [string] $Detail)
    $script:ledger.events = @($script:ledger.events) + [pscustomobject]@{ atUtc = [DateTimeOffset]::UtcNow.ToString('o'); kind = $Kind; detail = $Detail }
    Write-Ledger
}

function Set-LedgerStatus {
    param([Parameter(Mandatory)] [string] $Status)
    $script:ledger.status = $Status; $script:ledger.finishedAtUtc = [DateTimeOffset]::UtcNow.ToString('o'); Write-Ledger
}

function Invoke-Writeback {
    param([Parameter(Mandatory)] [object] $Document)
    if ($Apply -and [string]::IsNullOrWhiteSpace($Repo)) { throw '-Apply 必须提供 -Repo。' }
    if ($CloseSatisfied -and -not $Apply) { throw '-CloseSatisfied 只能与 -Apply 一起使用。' }
    $records = Get-Records -Document $Document; Assert-WritebackRecords -Records $records
    if ($Apply -and -not $CloseSatisfied -and @($records | Where-Object { [string] $_.status -eq 'closed' }).Count -gt 0) { throw 'Apply 包含 closed issue 时必须同时传 -CloseSatisfied，避免只写评论而没有关闭。' }
    $parent = Resolve-ParentIssueNumber -Document $Document
    if ($parent -gt 0 -and @($records | Where-Object { [int] $_.number -eq $parent }).Count -gt 0) { throw "parent issue #$parent 不能同时作为子票记录。" }
    $root = Initialize-Output; New-Ledger -RepoName $Repo -IsApply ([bool] $Apply) -IsClose ([bool] $CloseSatisfied)
    $planEntries = [System.Collections.Generic.List[object]]::new(); $bodyByNumber = @{}
    foreach ($record in $records) {
        $number = [int] $record.number; $body = Convert-WritebackComment -Record $record; $bodyPath = Join-Path $root "issue-$number.md"
        [System.IO.File]::WriteAllText($bodyPath, $body, $utf8); $bodyByNumber[$number] = $body
        $planEntries.Add([ordered]@{ number = $number; status = [string] $record.status; action = if ($Apply) { 'comment-and-readback' } else { 'dry-run-comment' }; bodyPath = $bodyPath; bodySha256 = Get-TextSha256 -Text $body; expectedIssueState = if ([string] $record.status -eq 'closed' -and $Apply -and $CloseSatisfied) { 'CLOSED' } else { 'OPEN' } })
    }
    $parentBody = $null; $parentBodyPath = $null
    if ($parent -gt 0) { $parentBody = Convert-ParentSummary -Number $parent -Records $records -RepoName $Repo; $parentBodyPath = Join-Path $root "parent-$parent.md"; [System.IO.File]::WriteAllText($parentBodyPath, $parentBody, $utf8) }
    $plan = [ordered]@{ schemaVersion = 1; createdAtUtc = [DateTimeOffset]::UtcNow.ToString('o'); repo = $Repo; apply = [bool] $Apply; closeSatisfied = [bool] $CloseSatisfied; outputDirectory = $root; records = @($planEntries); parentIssue = if ($parent -gt 0) { $parent } else { $null }; parentBodyPath = $parentBodyPath }
    Assert-ResumePlan -Plan $plan -ParentBody $parentBody
    $planPath = Join-Path $root 'plan.json'; Write-JsonFile -Path $planPath -Value $plan; Add-LedgerEvent -Kind 'plan-created' -Detail "已生成 $($records.Count) 条逐票正文。"
    if (-not $Apply) {
        foreach ($entry in $planEntries) { $processed = [ordered]@{ number = $entry.number; status = 'processed'; action = 'dry-run-comment'; bodyPath = $entry.bodyPath; bodySha256 = $entry.bodySha256 }; Write-IssueResult -Entry $processed; $script:ledger.issues = @($script:ledger.issues) + [pscustomobject] $processed }
        if ($parent -gt 0) { $parentEntry = [ordered]@{ number = $parent; status = 'processed'; action = 'dry-run-parent-summary'; bodyPath = $parentBodyPath; bodySha256 = Get-TextSha256 -Text $parentBody }; $script:ledger.parent = $parentEntry; Write-JsonFile -Path (Join-Path $root "parent-$parent.result.json") -Value $parentEntry }
        Set-LedgerStatus -Status 'dry-run-complete'
        $dryResult = [pscustomobject]@{ Valid = $true; Applied = $false; OutputDirectory = $root; PlanPath = $planPath; LedgerPath = Join-Path $root 'ledger.json'; ResultPath = Join-Path $root 'result.json'; ParentSummaryPath = $parentBodyPath; Issues = @($script:ledger.issues); Parent = $script:ledger.parent }
        Write-JsonFile -Path (Join-Path $root 'result.json') -Value $dryResult
        return $dryResult
    }
    $failed = $false
    foreach ($record in $records) {
        $number = [int] $record.number
        $expected = if ([string] $record.status -eq 'closed' -and $CloseSatisfied) { 'CLOSED' } else { 'OPEN' }
        $entry = Invoke-CommentTransaction -Number $number -Body $bodyByNumber[$number] -BodyPath (Join-Path $root "issue-$number.md") -ExpectedState $expected -Prefix 'issue'
        $script:ledger.issues = @($script:ledger.issues) + [pscustomobject] $entry
        if ($entry.status -eq 'failed') {
            Add-LedgerEvent -Kind 'issue-failed' -Detail "issue #$number 失败：$($entry.error)"; $failed = $true; break
        }
        Add-LedgerEvent -Kind 'issue-processed' -Detail "issue #$number 评论、正文和状态均已独立回读。"
    }
    if ($failed) {
        $doneNumbers = @($script:ledger.issues | ForEach-Object { [int] $_.number })
        foreach ($record in $records | Where-Object { [int] $_.number -notin $doneNumbers }) { $skipped = [ordered]@{ number = [int] $record.number; status = 'skipped'; action = 'stopped-after-failure'; reason = '前一条 issue 写入或独立回读失败。' }; Save-DeferredResult -Skipped $skipped -Prefix 'issue'; $script:ledger.issues = @($script:ledger.issues) + [pscustomobject] $skipped }
        if ($parent -gt 0) { $parentSkipped = [ordered]@{ number = $parent; status = 'skipped'; action = 'parent-stopped-after-child-failure'; reason = '子 issue 失败，停止依赖父票写入。' }; Save-DeferredResult -Skipped $parentSkipped -Prefix 'parent'; $script:ledger.parent = $parentSkipped }
        Set-LedgerStatus -Status 'failed'; throw 'issue 回写中止；已完成评论和 ledger 已保留，未重试或写入父票。'
    }
    if ($parent -gt 0) {
        $parentEntry = Invoke-CommentTransaction -Number $parent -Body $parentBody -BodyPath $parentBodyPath -ExpectedState 'OPEN' -Prefix 'parent'
        $script:ledger.parent = $parentEntry; Write-Ledger
        if ($parentEntry.status -eq 'failed') {
            Add-LedgerEvent -Kind 'parent-failed' -Detail "父 issue #$parent 失败：$($parentEntry.error)"; Set-LedgerStatus -Status 'failed'; throw '子 issue 已完成，但父票写入或回读失败；已保留全部 ledger 和评论。'
        }
        Add-LedgerEvent -Kind 'parent-processed' -Detail "父 issue #$parent 汇总评论和状态均已独立回读。"
    }
    Set-LedgerStatus -Status 'complete'
    $successResult = [pscustomobject]@{ Valid = $true; Applied = $true; OutputDirectory = $root; PlanPath = $planPath; LedgerPath = Join-Path $root 'ledger.json'; ResultPath = Join-Path $root 'result.json'; ParentSummaryPath = $parentBodyPath; Issues = @($script:ledger.issues); Parent = $script:ledger.parent }
    Write-JsonFile -Path (Join-Path $root 'result.json') -Value $successResult
    return $successResult
}

try {
    if ($SelfTest) {
        $self = [pscustomobject]@{ Valid = (Test-Path -LiteralPath $schemaPath -PathType Leaf) -and (Test-Path -LiteralPath $validatorPath -PathType Leaf); SelfTest = $true; NetworkWrites = $false }
        if (-not $self.Valid) { throw 'issue acceptance schema 或 validator 不存在。' }
        if ($Json) { $self | ConvertTo-Json -Depth 5 } else { 'issue-writeback: PASS (self-test)' }
        exit 0
    }
    if ([string]::IsNullOrWhiteSpace($RecordPath)) { throw '必须提供 -RecordPath，或使用 -SelfTest。' }
    $document = Read-JsonFile -Path $RecordPath; $records = Get-Records -Document $document; Assert-WritebackRecords -Records $records
    $validationOutput = @(& $validatorPath -SchemaPath $schemaPath -RecordPath $RecordPath -Json 2>&1)
    if ($LASTEXITCODE -ne 0) { throw "验收记录未通过 Test-IssueAcceptance：$($validationOutput -join [Environment]::NewLine)" }
    $validation = ($validationOutput -join [Environment]::NewLine) | ConvertFrom-Json -Depth 20 -ErrorAction Stop
    if (-not $validation.Valid) { throw "验收记录未通过 Test-IssueAcceptance：$($validation.Error)" }
    $result = Invoke-Writeback -Document $document
    $result | ConvertTo-Json -Depth 30
    exit 0
}
catch {
    if ($null -ne $script:ledger -and [string] $script:ledger.status -eq 'running') { $script:ledger.status = 'failed'; $script:ledger.finishedAtUtc = [DateTimeOffset]::UtcNow.ToString('o'); Write-Ledger }
    $failedResult = [pscustomobject]@{ Valid = $false; Error = $_.Exception.Message; OutputDirectory = $script:outputRoot; LedgerPath = if ($null -ne $script:outputRoot) { Join-Path $script:outputRoot 'ledger.json' } else { $null } }
    if ($null -ne $script:outputRoot) { Write-JsonFile -Path (Join-Path $script:outputRoot 'result.json') -Value $failedResult }
    if ($Json) { $failedResult | ConvertTo-Json -Depth 10 } else { Write-Error "issue-writeback: $($_.Exception.Message)" }
    exit 1
}
