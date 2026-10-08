#requires -Version 7.0
[CmdletBinding()]
param(
    [string] $SchemaPath = (Join-Path $PSScriptRoot '..\docs\agents\issue-acceptance.json'),
    [string] $RecordPath,
    [switch] $Json,
    [switch] $SelfTest
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Read-IssueAcceptanceJson {
    param([Parameter(Mandatory)] [string] $Path)
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "文件不存在：$Path"
    }
    $raw = Get-Content -LiteralPath $Path -Raw -Encoding UTF8
    if ([string]::IsNullOrWhiteSpace($raw)) { throw "JSON 文件为空：$Path" }
    return $raw | ConvertFrom-Json -Depth 30 -ErrorAction Stop
}

function Assert-IssueAcceptance {
    param([Parameter(Mandatory)] [object] $Schema)

    $knownChecks = @($Schema.checks.PSObject.Properties.Name)
    $knownTypes = @($Schema.taskTypes.PSObject.Properties.Name)
    $knownStatuses = @($Schema.statuses)
    if ([int] $Schema.schemaVersion -ne 1) { throw '只支持 issue-acceptance schemaVersion=1。' }
    if ($knownChecks.Count -lt 3 -or $knownTypes.Count -lt 3) { throw '验收 schema 的检查或任务类型不足。' }

    foreach ($typeProperty in $Schema.taskTypes.PSObject.Properties) {
        $typeName = [string] $typeProperty.Name
        $type = $typeProperty.Value
        $required = @($type.requiredChecks)
        $optional = @($type.optionalChecks)
        $notApplicable = @($type.'n/aChecks')
        if ($required.Count -eq 0) { throw "任务类型 $typeName 没有 requiredChecks。" }
        foreach ($check in @($required + $optional + $notApplicable)) {
            if ($check -notin $knownChecks) { throw "任务类型 $typeName 引用未知检查：$check" }
        }
        if ([string]::IsNullOrWhiteSpace([string] $type.'n/aRule')) { throw "任务类型 $typeName 缺少 n/aRule。" }
        if (@($required | Where-Object { $_ -in $optional -or $_ -in $notApplicable }).Count -gt 0) {
            throw "任务类型 $typeName 的 required/optional/n/a 检查重叠。"
        }
    }

    if ($SelfTest) {
        return [pscustomobject]@{ Valid = $true; SchemaVersion = 1; TaskTypes = $knownTypes.Count; Checks = $knownChecks.Count }
    }

    if ([string]::IsNullOrWhiteSpace($RecordPath)) {
        return [pscustomobject]@{ Valid = $true; SchemaVersion = 1; TaskTypes = $knownTypes.Count; Checks = $knownChecks.Count }
    }

    $recordDocument = Read-IssueAcceptanceJson -Path $RecordPath
    $records = if ($recordDocument.PSObject.Properties.Name -contains 'issues') { @($recordDocument.issues) } else { @($recordDocument) }
    if ($records.Count -eq 0) { throw '记录没有 issues。' }
    $seen = [System.Collections.Generic.HashSet[int]]::new()
    foreach ($record in $records) {
        foreach ($field in @($Schema.recordFields)) {
            if (-not ($record.PSObject.Properties.Name -contains $field)) { throw "issue $($record.number) 缺少字段：$field" }
        }
        $number = 0
        if (-not [int]::TryParse([string] $record.number, [ref] $number) -or $number -le 0) { throw "issue number 无效：$($record.number)" }
        if (-not $seen.Add($number)) { throw "issue 重复：$number" }
        $typeName = [string] $record.taskType
        if ($typeName -notin $knownTypes) { throw "issue $number 使用未知任务类型：$typeName" }
        if ([string] $record.status -notin $knownStatuses) { throw "issue $number 使用未知状态：$($record.status)" }
        $type = $Schema.taskTypes.PSObject.Properties[$typeName].Value
        $required = @($type.requiredChecks)
        $optional = @($type.optionalChecks)
        $notApplicableChecks = @($type.'n/aChecks')
        $recordRequired = @($record.requiredChecks)
        if (@($required | Where-Object { $_ -notin $recordRequired }).Count -gt 0) { throw "issue $number 丢失类型必需检查。" }
        if (@($recordRequired | Where-Object { $_ -notin $knownChecks }).Count -gt 0) { throw "issue $number 引用未知 required 检查。" }
        if (@($recordRequired | Where-Object { $_ -notin @($required + $optional) }).Count -gt 0) { throw "issue $number 把不属于该类型的检查标为 required。" }
        $na = @($record.notApplicable)
        foreach ($entry in $na) {
            if ($null -eq $entry.check -or $entry.check -notin $knownChecks) { throw "issue $number 的 notApplicable 检查无效。" }
            if ([string]::IsNullOrWhiteSpace([string] $entry.reason)) { throw "issue $number 的 $($entry.check) 缺少免测理由。" }
            if ($entry.check -notin $notApplicableChecks -or $entry.check -in $required -or $entry.check -in $recordRequired) {
                throw "issue $number 的 $($entry.check) 不允许按该类型标为 notApplicable。"
            }
        }
        foreach ($evidence in @($record.evidence)) {
            if ($null -eq $evidence.check -or $evidence.check -notin $knownChecks) { throw "issue $number 的 evidence 检查无效。" }
            if ([string] $evidence.status -notin @('processed', 'failed', 'skipped')) { throw "issue $number 的 evidence 状态无效：$($evidence.status)" }
            if ([string]::IsNullOrWhiteSpace([string] $evidence.detail)) { throw "issue $number 的 $($evidence.check) 缺少 evidence detail。" }
        }
        if ([string] $record.status -eq 'closed') {
            if (@($record.missing).Count -gt 0) { throw "issue $number 标为 closed 但仍有 missing。" }
            foreach ($check in $required) {
                if (@($record.evidence | Where-Object { $_.check -eq $check -and $_.status -eq 'processed' }).Count -eq 0) {
                    throw "issue $number 标为 closed 但缺少已处理证据：$check"
                }
            }
        }
    }
    return [pscustomobject]@{ Valid = $true; Records = $records.Count; SchemaVersion = 1 }
}

try {
    $schema = Read-IssueAcceptanceJson -Path $SchemaPath
    $result = Assert-IssueAcceptance -Schema $schema
    if ($Json) { $result | ConvertTo-Json -Depth 8 } else { "issue-acceptance: PASS ($($result | ConvertTo-Json -Compress))" }
    exit 0
}
catch {
    $result = [pscustomobject]@{ Valid = $false; Error = $_.Exception.Message }
    if ($Json) { $result | ConvertTo-Json -Depth 8 } else { Write-Error "issue-acceptance: $($_.Exception.Message)" }
    exit 1
}
