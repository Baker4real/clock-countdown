#requires -Version 7.0

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-CountdownRemoteDeploymentProperty {
    param(
        [Parameter(Mandatory)] [object] $InputObject,
        [Parameter(Mandatory)] [string] $Name,
        [switch] $Required
    )

    $property = $InputObject.PSObject.Properties[$Name]
    if ($Required -and $null -eq $property) {
        throw "远端部署结果缺少字段：$Name"
    }
    if ($null -eq $property) { return $null }
    $property.Value
}

function Get-CountdownRemoteDeploymentCommitted {
    param([Parameter(Mandatory)] [object] $Adapter)

    $stateProperty = $Adapter.PSObject.Properties['State']
    if ($null -eq $stateProperty -or $null -eq $stateProperty.Value) {
        throw '远端部署 adapter 缺少 State。'
    }
    $committedProperty = $stateProperty.Value.PSObject.Properties['Committed']
    if ($null -eq $committedProperty -or $committedProperty.Value -isnot [bool]) {
        throw '远端部署 adapter 的 State.Committed 必须是 bool。'
    }
    [bool] $committedProperty.Value
}

function Get-CountdownRemoteDeploymentStage {
    param(
        [Parameter(Mandatory)] [object] $Adapter,
        [Parameter(Mandatory)] [string] $Fallback
    )

    $stateProperty = $Adapter.PSObject.Properties['State']
    if ($null -eq $stateProperty -or $null -eq $stateProperty.Value) {
        return $Fallback
    }
    $stageProperty = $stateProperty.Value.PSObject.Properties['Stage']
    if ($null -eq $stageProperty -or [string]::IsNullOrWhiteSpace([string] $stageProperty.Value)) {
        return $Fallback
    }
    [string] $stageProperty.Value
}

function Get-CountdownRemoteDeploymentFailureDiagnostic {
    param(
        [Parameter(Mandatory)] [string] $Stage,
        [Parameter(Mandatory)] [System.Management.Automation.ErrorRecord] $ErrorRecord
    )

    $exception = $ErrorRecord.Exception
    $exceptionType = if ($null -eq $exception) { '未提供' } else { $exception.GetType().FullName }
    $message = if ($null -eq $exception) { '未提供' } else { [string] $exception.Message }
    $errorId = if ([string]::IsNullOrWhiteSpace([string] $ErrorRecord.FullyQualifiedErrorId)) {
        '未提供'
    }
    else {
        [string] $ErrorRecord.FullyQualifiedErrorId
    }
    $position = if ($null -eq $ErrorRecord.InvocationInfo) { '' } else { [string] $ErrorRecord.InvocationInfo.PositionMessage }
    if ([string]::IsNullOrWhiteSpace($position)) { $position = '未提供' }
    $scriptStackTrace = [string] $ErrorRecord.ScriptStackTrace
    if ([string]::IsNullOrWhiteSpace($scriptStackTrace)) { $scriptStackTrace = '未提供' }
    $position = $position -replace '\s+', ' '
    $scriptStackTrace = $scriptStackTrace -replace '\s+', ' '

    "阶段=$Stage；异常类型=$exceptionType；错误 ID=$errorId；错误位置=$position；ScriptStackTrace=$scriptStackTrace；原因=$message"
}

function Get-CountdownRemoteDeploymentStateBoolean {
    param(
        [Parameter(Mandatory)] [object] $Adapter,
        [Parameter(Mandatory)] [string] $Name
    )

    $stateProperty = $Adapter.PSObject.Properties['State']
    if ($null -eq $stateProperty -or $null -eq $stateProperty.Value) { return $false }
    $valueProperty = $stateProperty.Value.PSObject.Properties[$Name]
    if ($null -eq $valueProperty -or $valueProperty.Value -isnot [bool]) { return $false }
    [bool] $valueProperty.Value
}

function Write-CountdownRemoteDeploymentFailureReceipt {
    param(
        [Parameter(Mandatory)] [object] $Context,
        [Parameter(Mandatory)] [object] $Adapter,
        [Parameter(Mandatory)] [string] $Stage,
        [Parameter(Mandatory)] [System.Management.Automation.ErrorRecord] $ErrorRecord,
        [Parameter(Mandatory)]
        [ValidateSet('committed', 'not-committed', 'unknown')]
        [string] $CommitState,
        [Parameter(Mandatory)] [bool] $RollbackAttempted,
        [Parameter(Mandatory)] [bool] $RollbackSucceeded,
        [Parameter(Mandatory)] [bool] $PreservedUnmanagedLaunchers
    )

    $receiptDirectory = Split-Path -Parent $Context.DeferredReceiptPath
    $receiptPath = Join-Path $receiptDirectory 'deployment-failure.json'
    if (Test-Path -LiteralPath $receiptPath) { return $false }
    try {
        Import-Module (Join-Path $Context.SourceRoot 'Countdown.InstallTransaction.psm1') -Force -DisableNameChecking
        [void] (Write-CountdownInstallFailureReceipt `
                -Path $receiptPath `
                -Stage $Stage `
                -ErrorRecord $ErrorRecord `
                -TransactionStarted (Get-CountdownRemoteDeploymentStateBoolean -Adapter $Adapter -Name 'InstallStarted') `
                -CommitState $CommitState `
                -RollbackAttempted $RollbackAttempted `
                -RollbackSucceeded $RollbackSucceeded `
                -PreservedUnmanagedLaunchers $PreservedUnmanagedLaunchers)
        $true
    }
    catch {
        # 回执诊断不能覆盖原始部署异常或改变回滚/提交边界。
        $false
    }
}


function Get-CountdownPreRollbackValue {
    param([AllowNull()] [object] $Object, [Parameter(Mandatory)] [string] $Name)
    if ($null -eq $Object) { return $null }
    if ($Object -is [System.Collections.IDictionary]) {
        if ($Object.Contains($Name)) { return $Object[$Name] }
        return $null
    }
    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property) { return $null }
    $property.Value
}

function ConvertTo-CountdownPreRollbackText {
    param([AllowNull()] [object] $Value, [ValidateRange(1, 2048)] [int] $MaxLength = 512)
    if ($null -eq $Value) { return $null }
    $text = [string] $Value
    if ($text.Length -le $MaxLength) { return $text }
    $text.Substring(0, $MaxLength)
}

function ConvertTo-CountdownPreRollbackTimestampText {
    param([AllowNull()] [object] $Value)
    if ($null -eq $Value) { return $null }
    if ($Value -is [DateTimeOffset]) { return ([DateTimeOffset] $Value).ToUniversalTime().ToString('o') }
    if ($Value -is [DateTime]) { return ([DateTimeOffset] ([DateTime] $Value)).ToUniversalTime().ToString('o') }
    ConvertTo-CountdownPreRollbackText $Value 128
}

function Get-CountdownPreRollbackHash {
    param([Parameter(Mandatory)] [byte[]] $Bytes)
    $algorithm = [System.Security.Cryptography.SHA256]::Create()
    try { [Convert]::ToHexString($algorithm.ComputeHash($Bytes)) }
    finally { $algorithm.Dispose() }
}

function Get-CountdownPreRollbackFile {
    param([Parameter(Mandatory)] [string] $Path)
    $diagnostic = [ordered]@{ status = 'absent'; length = $null; lastWriteTimeUtc = $null; sha256 = $null; error = $null }
    if (-not [System.IO.File]::Exists($Path)) {
        return [pscustomobject]@{ Diagnostic = [pscustomobject] $diagnostic; Bytes = $null }
    }
    try {
        $item = Get-Item -LiteralPath $Path -Force -ErrorAction Stop
        if ($item -isnot [System.IO.FileInfo] -or ($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
            $diagnostic.status = 'invalid'
            $diagnostic.error = '不是非 reparse point 的普通文件。'
            return [pscustomobject]@{ Diagnostic = [pscustomobject] $diagnostic; Bytes = $null }
        }
        $bytes = [System.IO.File]::ReadAllBytes($item.FullName)
        $diagnostic.status = 'readable'
        $diagnostic.length = [long] $bytes.Length
        $diagnostic.lastWriteTimeUtc = $item.LastWriteTimeUtc.ToString('o')
        $diagnostic.sha256 = Get-CountdownPreRollbackHash -Bytes $bytes
        [pscustomobject]@{ Diagnostic = [pscustomobject] $diagnostic; Bytes = [byte[]] $bytes }
    }
    catch {
        $diagnostic.status = 'io-error'
        $diagnostic.error = ConvertTo-CountdownPreRollbackText $_.Exception.Message
        [pscustomobject]@{ Diagnostic = [pscustomobject] $diagnostic; Bytes = $null }
    }
}

function Get-CountdownPreRollbackReceipt {
    param(
        [Parameter(Mandatory)] [string] $Path,
        [AllowNull()] [string] $ExpectedToken,
        [int] $ExpectedProcessId,
        [int] $ExpectedSessionId,
        [Parameter(Mandatory)] [string] $ExpectedApplicationPath
    )
    $file = Get-CountdownPreRollbackFile -Path $Path
    $core = [ordered]@{
        schemaVersion = $null; stage = $null; acceptanceState = $null; acceptanceReadOnly = $null
        processId = $null; sessionId = $null; applicationPath = $null
        requestedAtUtc = $null; releasedAtUtc = $null; readyAtUtc = $null; finalizedAtUtc = $null
    }
    $match = [ordered]@{
        tokenExpected = -not [string]::IsNullOrWhiteSpace($ExpectedToken); token = $null
        processId = $null; sessionId = $null; applicationPath = $null
    }
    $result = [ordered]@{
        status = $file.Diagnostic.status; length = $file.Diagnostic.length
        lastWriteTimeUtc = $file.Diagnostic.lastWriteTimeUtc; sha256 = $file.Diagnostic.sha256
        core = [pscustomobject] $core; match = [pscustomobject] $match; error = $file.Diagnostic.error
    }
    if ($file.Diagnostic.status -ne 'readable') { return [pscustomobject] $result }
    try {
        $json = [System.Text.UTF8Encoding]::new($false, $true).GetString($file.Bytes)
        $receipt = ConvertFrom-Json $json -AsHashtable -Depth 8 -ErrorAction Stop
        if ($receipt -isnot [System.Collections.IDictionary]) { throw 'JSON 根节点不是 object。' }
        foreach ($name in @('schemaVersion', 'processId', 'sessionId')) {
            $value = Get-CountdownPreRollbackValue $receipt $name
            if ($value -is [int] -or $value -is [long]) { $core[$name] = [long] $value }
        }
        foreach ($name in @('stage', 'acceptanceState', 'applicationPath')) {
            $value = Get-CountdownPreRollbackValue $receipt $name
            if ($value -is [string]) { $core[$name] = ConvertTo-CountdownPreRollbackText $value 1024 }
        }
        foreach ($name in @('requestedAtUtc', 'releasedAtUtc', 'readyAtUtc', 'finalizedAtUtc')) {
            $value = Get-CountdownPreRollbackValue $receipt $name
            if ($null -ne $value) { $core[$name] = ConvertTo-CountdownPreRollbackTimestampText $value }
        }
        $readOnly = Get-CountdownPreRollbackValue $receipt 'acceptanceReadOnly'
        if ($readOnly -is [bool]) { $core.acceptanceReadOnly = [bool] $readOnly }
        $actualToken = Get-CountdownPreRollbackValue $receipt 'launchToken'
        if ($match.tokenExpected -and $actualToken -is [string]) { $match.token = $actualToken -ceq $ExpectedToken }
        if ($ExpectedProcessId -gt 0 -and $null -ne $core.processId) { $match.processId = [long] $core.processId -eq $ExpectedProcessId }
        if ($null -ne $core.sessionId) { $match.sessionId = [long] $core.sessionId -eq $ExpectedSessionId }
        if (-not [string]::IsNullOrWhiteSpace([string] $core.applicationPath)) {
            try { $match.applicationPath = [System.IO.Path]::GetFullPath($core.applicationPath) -ieq [System.IO.Path]::GetFullPath($ExpectedApplicationPath) }
            catch { $match.applicationPath = $false }
        }
    }
    catch {
        $result.status = 'invalid'
        $result.error = ConvertTo-CountdownPreRollbackText $_.Exception.Message
    }
    $result.core = [pscustomobject] $core
    $result.match = [pscustomobject] $match
    [pscustomobject] $result
}

function Get-CountdownPreRollbackProcess {
    param([int] $ProcessId, [int] $SessionId, [Parameter(Mandatory)] [string] $ApplicationPath)
    $result = [ordered]@{
        status = 'not-known'; processId = $ProcessId; sessionId = $null; sessionMatch = $null
        executablePath = $null; executablePathMatch = $null; startTimeUtc = $null
        parentProcessId = $null; responding = $null; error = $null
    }
    if ($ProcessId -le 0) { return [pscustomobject] $result }
    try {
        $process = Get-CimInstance Win32_Process -Filter "ProcessId=$ProcessId" -OperationTimeoutSec 2 -ErrorAction Stop | Select-Object -First 1
    }
    catch {
        $result.status = 'probe-error'
        $result.error = ConvertTo-CountdownPreRollbackText $_.Exception.Message
        return [pscustomobject] $result
    }
    if ($null -eq $process) { $result.status = 'missing'; return [pscustomobject] $result }
    $result.status = 'running'
    $result.sessionId = [int] $process.SessionId
    $result.sessionMatch = [int] $process.SessionId -eq $SessionId
    $result.executablePath = ConvertTo-CountdownPreRollbackText $process.ExecutablePath 1024
    if (-not [string]::IsNullOrWhiteSpace($result.executablePath)) {
        $expectedExe = Join-Path (Split-Path -Parent $ApplicationPath) 'app\MinminCountdown.exe'
        try { $result.executablePathMatch = [System.IO.Path]::GetFullPath($result.executablePath) -ieq [System.IO.Path]::GetFullPath($expectedExe) }
        catch { $result.executablePathMatch = $false }
    }
    if ($process.ParentProcessId -is [int] -or $process.ParentProcessId -is [long]) { $result.parentProcessId = [int] $process.ParentProcessId }
    try { $result.startTimeUtc = ([DateTimeOffset] $process.CreationDate).ToUniversalTime().ToString('o') } catch { }
    try { $result.responding = [bool] (Get-Process -Id $ProcessId -ErrorAction Stop).Responding }
    catch { $result.error = ConvertTo-CountdownPreRollbackText "Responding 探针失败：$($_.Exception.Message)" }
    [pscustomobject] $result
}

function Get-CountdownPreRollbackTask {
    param([Parameter(Mandatory)] [string] $TaskName)
    $result = [ordered]@{
        status = 'missing'; state = $null; lastRunTimeUtc = $null; lastTaskResult = $null
        actionIdentitySha256 = $null; error = $null
    }
    try { $task = Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue | Select-Object -First 1 }
    catch {
        $result.status = 'error'
        $result.error = ConvertTo-CountdownPreRollbackText $_.Exception.Message
        return [pscustomobject] $result
    }
    if ($null -eq $task) { return [pscustomobject] $result }
    $result.status = 'readable'
    $result.state = ConvertTo-CountdownPreRollbackText $task.State 64
    try {
        $identity = @($task.Actions | ForEach-Object { "$($_.Execute)$([char]0)$($_.Arguments)$([char]0)$($_.WorkingDirectory)" }) -join ([char]1)
        $result.actionIdentitySha256 = Get-CountdownPreRollbackHash ([System.Text.UTF8Encoding]::new($false).GetBytes($identity))
        $info = Get-ScheduledTaskInfo -TaskName $TaskName -ErrorAction Stop
        if ($info.LastRunTime -gt [DateTime]::MinValue) { $result.lastRunTimeUtc = $info.LastRunTime.ToUniversalTime().ToString('o') }
        $result.lastTaskResult = [long] $info.LastTaskResult
    }
    catch { $result.error = ConvertTo-CountdownPreRollbackText $_.Exception.Message }
    [pscustomobject] $result
}

function Get-CountdownPreRollbackLog {
    param([Parameter(Mandatory)] [string] $Path, [Parameter(Mandatory)] [string] $Name, [int] $ExpectedProcessId)
    $file = Get-CountdownPreRollbackFile -Path $Path
    $result = [ordered]@{
        name = $Name; status = $file.Diagnostic.status; length = $file.Diagnostic.length
        lastWriteTimeUtc = $file.Diagnostic.lastWriteTimeUtc; sha256 = $file.Diagnostic.sha256
        tail = @(); error = $file.Diagnostic.error
    }
    if ($file.Diagnostic.status -ne 'readable') { return [pscustomobject] $result }
    try { $text = [System.Text.UTF8Encoding]::new($false, $true).GetString($file.Bytes) }
    catch {
        $result.status = 'invalid'
        $result.error = ConvertTo-CountdownPreRollbackText $_.Exception.Message
        return [pscustomobject] $result
    }
    $records = @()
    $invalid = 0
    $lines = @($text -split "\r?\n" | Where-Object { $_ } | Select-Object -Last 100)
    for ($index = 0; $index -lt $lines.Count; $index++) {
        try {
            $entry = ConvertFrom-Json $lines[$index] -AsHashtable -Depth 4 -ErrorAction Stop
            if ($entry -isnot [System.Collections.IDictionary]) { throw '日志行不是 object。' }
            $processId = Get-CountdownPreRollbackValue $entry 'processId'
            $event = ConvertTo-CountdownPreRollbackText (Get-CountdownPreRollbackValue $entry 'event') 256
            $normalized = [pscustomobject][ordered]@{
                timestampUtc = Get-CountdownPreRollbackValue $entry 'timestampUtc'
                level = ConvertTo-CountdownPreRollbackText (Get-CountdownPreRollbackValue $entry 'level') 32
                event = $event
                processId = $(if ($processId -is [int] -or $processId -is [long]) { [long] $processId } else { $null })
                exceptionType = ConvertTo-CountdownPreRollbackText (Get-CountdownPreRollbackValue $entry 'exceptionType') 256
                exceptionMessage = ConvertTo-CountdownPreRollbackText (Get-CountdownPreRollbackValue $entry 'exceptionMessage') 256
            }
            $preferred = $ExpectedProcessId -gt 0 -and $normalized.processId -eq $ExpectedProcessId -and $event -in @('AcceptanceMigrationReady', 'AcceptanceReadOnlyReleased', 'FatalUnhandledException', 'ProcessExited')
            $records += [pscustomobject]@{ Index = $index; Preferred = $preferred; Entry = $normalized }
        }
        catch { $invalid++ }
    }
    if ($records.Count -eq 0 -and $lines.Count -gt 0) { $result.status = 'invalid'; $result.error = '最近日志均不是有效 JSONL。'; return [pscustomobject] $result }
    $result.tail = @($records | Sort-Object @{ Expression = 'Preferred'; Descending = $true }, @{ Expression = 'Index'; Descending = $true } | Select-Object -First 3 | Sort-Object Index | ForEach-Object Entry)
    if ($invalid -gt 0) { $result.error = "最近日志有 $invalid 行无法解析，已跳过。" }
    [pscustomobject] $result
}

function Write-CountdownPreRollbackDiagnostics {
    param([Parameter(Mandatory)] [object] $Context, [Parameter(Mandatory)] [object] $Adapter, [Parameter(Mandatory)] [string] $Stage)
    $path = Join-Path (Split-Path -Parent $Context.DeferredReceiptPath) 'pre-rollback-diagnostics.json'
    $temporaryPath = $null
    try {
        $path = [System.IO.Path]::GetFullPath($path)
        if ([System.IO.Path]::GetPathRoot($path) -ine 'D:\') { throw 'rollback 前诊断只能写入普通 D: 路径。' }
        $directory = Split-Path -Parent $path
        if (-not [System.IO.Directory]::Exists($directory)) { New-Item -ItemType Directory -Path $directory -Force -ErrorAction Stop | Out-Null }
        $directoryItem = Get-Item -LiteralPath $directory -Force -ErrorAction Stop
        if (($directoryItem.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'rollback 前诊断目录不能是 reparse point。' }

        $state = Get-CountdownPreRollbackValue $Adapter 'State'
        $pidValue = Get-CountdownPreRollbackValue $state 'ExpectedProcessId'
        $expectedPid = if (($pidValue -is [int] -or $pidValue -is [long]) -and $pidValue -gt 0) { [int] $pidValue } else { 0 }
        $interactive = Get-CountdownPreRollbackValue $state 'InteractiveContext'
        $sessionValue = Get-CountdownPreRollbackValue $interactive 'SessionId'
        $expectedSession = if ($sessionValue -is [int] -or $sessionValue -is [long]) { [int] $sessionValue } else { 0 }
        $applicationPath = Join-Path $Context.InstallPath '敏敏子倒计时.ps1'
        $token = [string] (Get-CountdownPreRollbackValue $state 'LaunchToken')
        $statusPaths = [ordered]@{}
        foreach ($definition in @(
                @('startup', 'StartupRequestPath', 'startup-request.json'),
                @('runtime', 'RuntimeStatusPath', 'runtime-status.json'),
                @('acceptance', 'AcceptanceStatusPath', 'acceptance-status.json'))) {
            $statusPath = [string] (Get-CountdownPreRollbackValue $state $definition[1])
            if ([string]::IsNullOrWhiteSpace($statusPath)) { $statusPath = Join-Path $Context.InstallPath $definition[2] }
            $statusPaths[$definition[0]] = $statusPath
        }
        $receipts = [ordered]@{}
        foreach ($name in $statusPaths.Keys) {
            $receipts[$name] = Get-CountdownPreRollbackReceipt $statusPaths[$name] $token $expectedPid $expectedSession $applicationPath
        }

        $tempResult = [ordered]@{ status = 'readable'; truncated = $false; files = @(); error = $null }
        try {
            $tempFiles = @(Get-ChildItem -LiteralPath (Split-Path -Parent $statusPaths.acceptance) -File -Force -ErrorAction Stop | Where-Object Name -Like 'acceptance-status.json.*.tmp' | Sort-Object LastWriteTimeUtc -Descending)
            $tempResult.truncated = $tempFiles.Count -gt 16
            $tempResult.files = @($tempFiles | Select-Object -First 16 | ForEach-Object {
                    $snapshot = (Get-CountdownPreRollbackFile $_.FullName).Diagnostic
                    [pscustomobject][ordered]@{ name = $_.Name; status = $snapshot.status; length = $snapshot.length; lastWriteTimeUtc = $snapshot.lastWriteTimeUtc; sha256 = $snapshot.sha256; error = $snapshot.error }
                })
        }
        catch { $tempResult.status = 'io-error'; $tempResult.error = ConvertTo-CountdownPreRollbackText $_.Exception.Message }

        $logs = @('application.jsonl', 'application.jsonl.1', 'application.jsonl.2', 'application.jsonl.3' | ForEach-Object {
                Get-CountdownPreRollbackLog (Join-Path $Context.InstallPath "logs\$_") $_ $expectedPid
            })
        $presence = {
            param([string] $Root)
            [pscustomobject][ordered]@{
                startup = [System.IO.File]::Exists((Join-Path $Root 'startup-request.json'))
                runtime = [System.IO.File]::Exists((Join-Path $Root 'runtime-status.json'))
                acceptance = [System.IO.File]::Exists((Join-Path $Root 'acceptance-status.json'))
            }
        }
        $document = [ordered]@{
            schemaVersion = 1
            captureAtUtc = [DateTimeOffset]::UtcNow.ToString('o')
            deploymentStage = ConvertTo-CountdownPreRollbackText $Stage 256
            expected = [pscustomobject][ordered]@{ processId = $expectedPid; sessionId = $expectedSession; applicationPath = [System.IO.Path]::GetFullPath($applicationPath) }
            receipts = [pscustomobject] $receipts
            acceptanceTempFiles = [pscustomobject] $tempResult
            process = Get-CountdownPreRollbackProcess $expectedPid $expectedSession $applicationPath
            task = Get-CountdownPreRollbackTask $Context.TaskName
            misplacedReceipts = [pscustomobject][ordered]@{ installRoot = & $presence $Context.InstallPath; appSubdirectory = & $presence (Join-Path $Context.InstallPath 'app') }
            logs = @($logs)
        }
        $encoding = [System.Text.UTF8Encoding]::new($false)
        $json = $document | ConvertTo-Json -Depth 12 -Compress
        if ($encoding.GetByteCount($json) -gt 24576) {
            foreach ($log in $logs) { $log.tail = @($log.tail | Select-Object -Last 1) }
            $json = $document | ConvertTo-Json -Depth 12 -Compress
        }
        if ($encoding.GetByteCount($json) -gt 24576) { throw 'rollback 前诊断超过 24 KiB 上限。' }
        $temporaryPath = "$path.$([guid]::NewGuid().ToString('N')).tmp"
        [System.IO.File]::WriteAllText($temporaryPath, $json, $encoding)
        [System.IO.File]::Move($temporaryPath, $path, $true)
        $temporaryPath = $null
        [pscustomobject]@{ Success = $true; FailureMessage = $null }
    }
    catch { [pscustomobject]@{ Success = $false; FailureMessage = ConvertTo-CountdownPreRollbackText $_.Exception.Message 1024 } }
    finally {
        if ($null -ne $temporaryPath -and [System.IO.File]::Exists($temporaryPath)) {
            try { Remove-Item -LiteralPath $temporaryPath -Force -ErrorAction Stop } catch { }
        }
    }
}

function Assert-CountdownPreRollbackShape {
    param([AllowNull()] [object] $Value, [Parameter(Mandatory)] [string[]] $Fields, [Parameter(Mandatory)] [string] $Name)
    if ($Value -isnot [System.Collections.IDictionary]) { throw "$Name 必须是 JSON object。" }
    $allowed = [System.Collections.Generic.HashSet[string]]::new($Fields, [System.StringComparer]::Ordinal)
    if (@($Value.Keys).Count -ne $Fields.Count) { throw "$Name 字段数量无效。" }
    foreach ($key in $Value.Keys) { if (-not $allowed.Contains([string] $key)) { throw "$Name 包含未允许字段：$key" } }
}

function Assert-CountdownPreRollbackMetadata {
    param([System.Collections.IDictionary] $Value, [string] $Name)
    if ($null -ne $Value.length -and (($Value.length -isnot [int] -and $Value.length -isnot [long]) -or $Value.length -lt 0)) { throw "$Name length 无效。" }
    if ($null -ne $Value.sha256 -and ($Value.sha256 -isnot [string] -or $Value.sha256 -notmatch '\A[0-9A-F]{64}\z')) { throw "$Name sha256 无效。" }
    if ($null -ne $Value.error -and $Value.error -isnot [string]) { throw "$Name error 无效。" }
    $metadataPresent = @($Value.length, $Value.lastWriteTimeUtc, $Value.sha256 | Where-Object { $null -ne $_ }).Count
    if ($Value.status -eq 'readable' -and $metadataPresent -ne 3) { throw "$Name readable 状态缺少完整性元数据。" }
    if ($Value.status -eq 'absent' -and $metadataPresent -ne 0) { throw "$Name absent 状态不能包含完整性元数据。" }
    if ($Value.status -in @('invalid', 'io-error') -and $metadataPresent -notin @(0, 3)) { throw "$Name 完整性元数据组合无效。" }
    if ($Value.status -in @('invalid', 'io-error') -and [string]::IsNullOrWhiteSpace([string] $Value.error)) { throw "$Name $($Value.status) 状态缺少错误原因。" }
}

function Assert-CountdownPreRollbackTimestamp {
    param([AllowNull()] [object] $Value, [Parameter(Mandatory)] [string] $Name, [switch] $Required)
    if ($null -eq $Value) {
        if ($Required) { throw "$Name 缺少时间戳。" }
        return
    }
    if ($Value -is [DateTime] -or $Value -is [DateTimeOffset]) { return }
    if ($Value -isnot [string] -or [string]::IsNullOrWhiteSpace($Value)) { throw "$Name 时间戳类型无效。" }
    $parsed = [DateTimeOffset]::MinValue
    if (-not [DateTimeOffset]::TryParse(
            $Value,
            [Globalization.CultureInfo]::InvariantCulture,
            [Globalization.DateTimeStyles]::AllowWhiteSpaces,
            [ref] $parsed)) {
        throw "$Name 时间戳无效。"
    }
}

function Assert-CountdownPreRollbackValueTypes {
    param(
        [System.Collections.IDictionary] $Value,
        [string] $Name,
        [string[]] $IntegerFields = @(),
        [string[]] $BooleanFields = @(),
        [string[]] $StringFields = @(),
        [string[]] $TimestampFields = @()
    )
    foreach ($field in $IntegerFields) {
        if ($null -ne $Value[$field] -and $Value[$field] -isnot [int] -and $Value[$field] -isnot [long]) { throw "$Name.$field 类型无效。" }
    }
    foreach ($field in $BooleanFields) {
        if ($null -ne $Value[$field] -and $Value[$field] -isnot [bool]) { throw "$Name.$field 类型无效。" }
    }
    foreach ($field in $StringFields) {
        if ($null -ne $Value[$field] -and $Value[$field] -isnot [string]) { throw "$Name.$field 类型无效。" }
    }
    foreach ($field in $TimestampFields) {
        Assert-CountdownPreRollbackTimestamp $Value[$field] "$Name.$field"
    }
}

function ConvertFrom-CountdownPreRollbackDiagnosticsBytes {
    param([Parameter(Mandatory)] [byte[]] $Bytes)
    if ($Bytes.Length -gt 24576) { throw "rollback 前诊断超过允许大小：$($Bytes.Length)" }
    if ($Bytes.Length -ge 3 -and $Bytes[0] -eq 0xEF -and $Bytes[1] -eq 0xBB -and $Bytes[2] -eq 0xBF) { throw 'rollback 前诊断不允许 UTF-8 BOM。' }
    try {
        $document = ConvertFrom-Json ([System.Text.UTF8Encoding]::new($false, $true).GetString($Bytes)) -AsHashtable -Depth 12 -ErrorAction Stop
    }
    catch { throw "rollback 前诊断不是有效 UTF-8 JSON：$($_.Exception.Message)" }

    Assert-CountdownPreRollbackShape $document @('schemaVersion', 'captureAtUtc', 'deploymentStage', 'expected', 'receipts', 'acceptanceTempFiles', 'process', 'task', 'misplacedReceipts', 'logs') 'rollback 前诊断'
    if (($document.schemaVersion -isnot [int] -and $document.schemaVersion -isnot [long]) -or $document.schemaVersion -ne 1) { throw 'rollback 前诊断 schemaVersion 无效。' }
    Assert-CountdownPreRollbackTimestamp $document.captureAtUtc 'rollback 前诊断.captureAtUtc' -Required
    if ($document.deploymentStage -isnot [string] -or [string]::IsNullOrWhiteSpace($document.deploymentStage)) { throw 'rollback 前诊断 deploymentStage 无效。' }
    Assert-CountdownPreRollbackShape $document.expected @('processId', 'sessionId', 'applicationPath') 'expected'
    foreach ($name in @('processId', 'sessionId')) {
        if (($document.expected[$name] -isnot [int] -and $document.expected[$name] -isnot [long]) -or $document.expected[$name] -lt 0) { throw "expected.$name 无效。" }
    }
    if ($document.expected.applicationPath -isnot [string]) { throw 'expected.applicationPath 无效。' }

    Assert-CountdownPreRollbackShape $document.receipts @('startup', 'runtime', 'acceptance') 'receipts'
    foreach ($name in @('startup', 'runtime', 'acceptance')) {
        $receipt = $document.receipts[$name]
        Assert-CountdownPreRollbackShape $receipt @('status', 'length', 'lastWriteTimeUtc', 'sha256', 'core', 'match', 'error') "receipts.$name"
        if ($receipt.status -notin @('absent', 'readable', 'invalid', 'io-error')) { throw "receipts.$name.status 无效。" }
        Assert-CountdownPreRollbackMetadata $receipt "receipts.$name"
        Assert-CountdownPreRollbackShape $receipt.core @('schemaVersion', 'stage', 'acceptanceState', 'acceptanceReadOnly', 'processId', 'sessionId', 'applicationPath', 'requestedAtUtc', 'releasedAtUtc', 'readyAtUtc', 'finalizedAtUtc') "receipts.$name.core"
        Assert-CountdownPreRollbackShape $receipt.match @('tokenExpected', 'token', 'processId', 'sessionId', 'applicationPath') "receipts.$name.match"
        Assert-CountdownPreRollbackValueTypes $receipt "receipts.$name" -TimestampFields @('lastWriteTimeUtc')
        Assert-CountdownPreRollbackValueTypes $receipt.core "receipts.$name.core" -IntegerFields @('schemaVersion', 'processId', 'sessionId') -BooleanFields @('acceptanceReadOnly') -StringFields @('stage', 'acceptanceState', 'applicationPath') -TimestampFields @('requestedAtUtc', 'releasedAtUtc', 'readyAtUtc', 'finalizedAtUtc')
        if ($receipt.match.tokenExpected -isnot [bool]) { throw "receipts.$name.match.tokenExpected 无效。" }
        foreach ($field in @('token', 'processId', 'sessionId', 'applicationPath')) {
            if ($null -ne $receipt.match[$field] -and $receipt.match[$field] -isnot [bool]) { throw "receipts.$name.match.$field 无效。" }
        }
    }

    Assert-CountdownPreRollbackShape $document.acceptanceTempFiles @('status', 'truncated', 'files', 'error') 'acceptanceTempFiles'
    if ($document.acceptanceTempFiles.status -notin @('readable', 'io-error') -or $document.acceptanceTempFiles.truncated -isnot [bool] -or $document.acceptanceTempFiles.files -isnot [System.Collections.IList] -or @($document.acceptanceTempFiles.files).Count -gt 16) { throw 'acceptanceTempFiles 无效。' }
    if ($null -ne $document.acceptanceTempFiles.error -and $document.acceptanceTempFiles.error -isnot [string]) { throw 'acceptanceTempFiles.error 类型无效。' }
    if ($document.acceptanceTempFiles.status -eq 'io-error' -and [string]::IsNullOrWhiteSpace([string] $document.acceptanceTempFiles.error)) { throw 'acceptanceTempFiles.io-error 缺少错误原因。' }
    foreach ($file in @($document.acceptanceTempFiles.files)) {
        Assert-CountdownPreRollbackShape $file @('name', 'status', 'length', 'lastWriteTimeUtc', 'sha256', 'error') 'acceptanceTempFiles.files[]'
        if ($file.status -notin @('absent', 'readable', 'invalid', 'io-error')) { throw 'acceptanceTempFiles.files[].status 无效。' }
        if ($file.name -isnot [string] -or $file.name -notmatch '\Aacceptance-status\.json\.[0-9A-Fa-f]{32}\.tmp\z') { throw 'acceptanceTempFiles.files[].name 无效。' }
        Assert-CountdownPreRollbackMetadata $file 'acceptanceTempFiles.files[]'
        Assert-CountdownPreRollbackValueTypes $file 'acceptanceTempFiles.files[]' -StringFields @('name', 'status') -TimestampFields @('lastWriteTimeUtc')
    }

    Assert-CountdownPreRollbackShape $document.process @('status', 'processId', 'sessionId', 'sessionMatch', 'executablePath', 'executablePathMatch', 'startTimeUtc', 'parentProcessId', 'responding', 'error') 'process'
    if ($document.process.status -notin @('not-known', 'running', 'missing', 'probe-error')) { throw 'process.status 无效。' }
    foreach ($field in @('sessionMatch', 'executablePathMatch', 'responding')) {
        if ($null -ne $document.process[$field] -and $document.process[$field] -isnot [bool]) { throw "process.$field 无效。" }
    }
    Assert-CountdownPreRollbackValueTypes $document.process 'process' -IntegerFields @('processId', 'sessionId', 'parentProcessId') -StringFields @('executablePath', 'error') -TimestampFields @('startTimeUtc')
    Assert-CountdownPreRollbackShape $document.task @('status', 'state', 'lastRunTimeUtc', 'lastTaskResult', 'actionIdentitySha256', 'error') 'task'
    if ($document.task.status -notin @('missing', 'readable', 'error')) { throw 'task.status 无效。' }
    if ($null -ne $document.task.actionIdentitySha256 -and $document.task.actionIdentitySha256 -notmatch '\A[0-9A-F]{64}\z') { throw 'task.actionIdentitySha256 无效。' }
    Assert-CountdownPreRollbackValueTypes $document.task 'task' -IntegerFields @('lastTaskResult') -StringFields @('state', 'actionIdentitySha256', 'error') -TimestampFields @('lastRunTimeUtc')

    Assert-CountdownPreRollbackShape $document.misplacedReceipts @('installRoot', 'appSubdirectory') 'misplacedReceipts'
    foreach ($location in @('installRoot', 'appSubdirectory')) {
        Assert-CountdownPreRollbackShape $document.misplacedReceipts[$location] @('startup', 'runtime', 'acceptance') "misplacedReceipts.$location"
        foreach ($field in @('startup', 'runtime', 'acceptance')) { if ($document.misplacedReceipts[$location][$field] -isnot [bool]) { throw "misplacedReceipts.$location.$field 无效。" } }
    }

    if ($document.logs -isnot [System.Collections.IList] -or @($document.logs).Count -ne 4) { throw 'logs 无效。' }
    $expectedLogNames = [System.Collections.Generic.HashSet[string]]::new(
        [string[]] @('application.jsonl', 'application.jsonl.1', 'application.jsonl.2', 'application.jsonl.3'),
        [System.StringComparer]::Ordinal)
    $actualLogNames = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
    foreach ($log in @($document.logs)) {
        Assert-CountdownPreRollbackShape $log @('name', 'status', 'length', 'lastWriteTimeUtc', 'sha256', 'tail', 'error') 'logs[]'
        Assert-CountdownPreRollbackMetadata $log 'logs[]'
        if ($log.name -notin @('application.jsonl', 'application.jsonl.1', 'application.jsonl.2', 'application.jsonl.3') -or $log.status -notin @('absent', 'readable', 'invalid', 'io-error') -or $log.tail -isnot [System.Collections.IList] -or @($log.tail).Count -gt 3) { throw 'logs[] 无效。' }
        if (-not $actualLogNames.Add([string] $log.name)) { throw "logs[] 包含重复名称：$($log.name)" }
        Assert-CountdownPreRollbackValueTypes $log 'logs[]' -StringFields @('name', 'status') -TimestampFields @('lastWriteTimeUtc')
        foreach ($entry in @($log.tail)) {
            Assert-CountdownPreRollbackShape $entry @('timestampUtc', 'level', 'event', 'processId', 'exceptionType', 'exceptionMessage') 'logs[].tail[]'
            Assert-CountdownPreRollbackValueTypes $entry 'logs[].tail[]' -IntegerFields @('processId') -StringFields @('level', 'event', 'exceptionType', 'exceptionMessage') -TimestampFields @('timestampUtc')
        }
    }
    if (-not $actualLogNames.SetEquals($expectedLogNames)) { throw 'logs[] 未包含四个固定日志名称。' }
    [pscustomobject] $document
}

function Assert-CountdownRemoteDeploymentAdapter {
    param([Parameter(Mandatory)] [object] $Adapter)

    foreach ($name in @('State', 'Begin', 'Accept', 'Rollback', 'Dispose')) {
        $property = $Adapter.PSObject.Properties[$name]
        if ($null -eq $property) {
            throw "远端部署 adapter 缺少 $name。"
        }
        if ($name -ne 'State' -and $property.Value -isnot [scriptblock]) {
            throw "远端部署 adapter 的 $name 必须是 scriptblock。"
        }
    }
    $finalizeProperty = $Adapter.PSObject.Properties['Finalize']
    if ($null -ne $finalizeProperty -and $finalizeProperty.Value -isnot [scriptblock]) {
        throw '远端部署 adapter 的 Finalize 必须是 scriptblock。'
    }
    if (Get-CountdownRemoteDeploymentCommitted -Adapter $Adapter) {
        throw '远端部署 adapter 的初始提交状态必须为 false。'
    }
    $Adapter
}

function New-CountdownRemoteDeploymentWindowsAdapter {
    $state = [pscustomobject]@{
        InstallMutex = $null
        TransactionModuleImported = $false
        InstallStarted = $false
        Committed = $false
        Stage = '初始化'
        RollbackTransactionPath = $null
        InteractiveContext = $null
        LauncherPath = $null
        LegacyLauncherPath = $null
        LauncherTempPath = $null
        SettingsSnapshotPath = $null
        SettingsPath = $null
        StartupRequestPath = $null
        RuntimeStatusPath = $null
        AcceptanceStatusPath = $null
        LaunchToken = $null
        ExpectedProcessId = 0
        ExpectedScheduledTaskIdentity = $null
    }

    [pscustomobject]@{
        State = $state
        Begin = {
            param($AdapterState, $Context)

            $AdapterState.Stage = '加载发布包'
            Import-Module (Join-Path $Context.SourceRoot 'Countdown.ReleaseContract.psm1') -Force
            $releaseContract = Get-CountdownReleaseContract
            [void] (Assert-CountdownReleasePackage -Root $Context.SourceRoot)

            Import-Module (Join-Path $Context.SourceRoot 'Countdown.InstallTransaction.psm1') -Force -DisableNameChecking
            $AdapterState.TransactionModuleImported = $true
            $interactiveContext = Get-CountdownInteractiveDesktopContext
            $launcherPath = Join-Path ([string] $interactiveContext.Desktop) '敏敏子倒计时.exe'
            $legacyLauncherPath = Join-Path ([string] $interactiveContext.Desktop) '敏敏子倒计时.cmd'
            $launcherTempPath = "$launcherPath.new"
            $AdapterState.InteractiveContext = $interactiveContext
            $AdapterState.LauncherPath = $launcherPath
            $AdapterState.LegacyLauncherPath = $legacyLauncherPath
            $AdapterState.LauncherTempPath = $launcherTempPath
            $AdapterState.Stage = '取得安装事务锁'
            $AdapterState.InstallMutex = Enter-CountdownInstallTransactionMutex -TimeoutSeconds 120

            $settingsPath = Join-Path $Context.InstallPath 'settings.json'
            $installerArguments = @{
                DeferCommit = $true
                DeferredReceiptPath = $Context.DeferredReceiptPath
                ParentTransactionMutexHeld = $true
                ExpectedInteractiveUser = [string] $interactiveContext.User
                ExpectedInteractiveSessionId = [int] $interactiveContext.SessionId
                ExpectedDesktopPath = [string] $interactiveContext.Desktop
            }
            $AdapterState.InstallStarted = $true
            $AdapterState.Stage = '执行安装器'
            $installerOutput = & (Join-Path $Context.SourceRoot 'Install-Countdown.ps1') @installerArguments | Out-String
            $AdapterState.Stage = '读取延迟提交回执'
            $receipt = Read-CountdownDeferredInstallReceipt -Path $Context.DeferredReceiptPath
            if ($null -eq $receipt) {
                throw '安装器没有写入可用于回滚的延迟提交回执。'
            }
            $AdapterState.RollbackTransactionPath = [string] $receipt.transactionPath
            $AdapterState.Stage = '验证安装器输出'
            try {
                $installer = $installerOutput | ConvertFrom-Json -ErrorAction Stop
            }
            catch {
                throw "安装器返回了无效 JSON：$($_.Exception.Message)"
            }
            if (-not [bool] $installer.DeferredCommit -or
                [string]::IsNullOrWhiteSpace([string] $installer.TransactionPath)) {
                throw '安装器没有保留待验收事务。'
            }
            if (-not [string]::Equals(
                    [System.IO.Path]::GetFullPath([string] $installer.TransactionPath),
                    [System.IO.Path]::GetFullPath([string] $receipt.transactionPath),
                    [System.StringComparison]::OrdinalIgnoreCase)) {
                throw '安装器输出与独立事务回执不一致。'
            }
            if (-not [string]::Equals(
                    [string] $installer.LaunchToken,
                    [string] $receipt.launchToken,
                    [System.StringComparison]::Ordinal)) {
                throw '安装器输出与独立事务回执的启动令牌不一致。'
            }
            if ([int] $installer.InteractiveSessionId -ne [int] $receipt.interactiveSessionId) {
                throw '安装器输出与独立事务回执的交互会话不一致。'
            }

            $validationProperty = $installer.PSObject.Properties['Validation']
            if ($null -eq $validationProperty) {
                throw '安装器没有返回稳定 validation interface。'
            }
            $validation = $validationProperty.Value

            $AdapterState.Stage = '验证安装文件'
            foreach ($file in $releaseContract.InstalledFiles) {
                $expectedHash = [string] $releaseContract.IntegrityHashes[$file]
                $installedPath = Join-Path $Context.InstallPath $file
                if ((Get-FileHash -Algorithm SHA256 -LiteralPath $installedPath).Hash -ne $expectedHash) {
                    throw "安装后哈希不匹配：$file"
                }
            }
            foreach ($file in $releaseContract.RetiredRuntimeFiles) {
                if (Test-Path -LiteralPath (Join-Path $Context.InstallPath $file)) {
                    throw "安装目录仍包含已淘汰悬浮窗文件：$file"
                }
            }

            Import-Module (Join-Path $Context.InstallPath 'Countdown.Settings.psm1') -Force
            Import-Module (Join-Path $Context.SourceRoot 'Countdown.Runtime.psm1') -Force
            Import-Module (Join-Path $Context.SourceRoot 'Countdown.TransactionAcceptance.psm1') -Force

            $applicationPath = Join-Path $Context.InstallPath '敏敏子倒计时.ps1'
            $AdapterState.Stage = '验证已安装应用'
            $installedValidationOutput = & $Context.PowerShell7Path `
                -NoLogo -NoProfile -NonInteractive -STA -File $applicationPath -ValidateOnly | Out-String
            if ($LASTEXITCODE -ne 0) {
                throw "安装后应用 validate-only 失败：$installedValidationOutput"
            }
            try {
                $installedValidation = $installedValidationOutput | ConvertFrom-Json -ErrorAction Stop
            }
            catch {
                throw "安装后应用 validate-only 返回了无效 JSON：$($_.Exception.Message)"
            }
            if (-not [bool] $installedValidation.Valid -or
                -not [bool] $installedValidation.FloatingWindowPresent -or
                -not [bool] $installedValidation.FloatingManagerButtonPresent) {
                throw '安装后悬浮框验证失败。'
            }

            $AdapterState.Stage = '等待窗口就绪回执'
            $task = Get-ScheduledTask -TaskName $Context.TaskName -ErrorAction Stop
            $runtimeStatusPath = Join-Path $Context.InstallPath 'runtime-status.json'
            $runtimeStatus = Wait-CountdownWindowReady `
                -Path $runtimeStatusPath `
                -LaunchToken ([string] $receipt.launchToken) `
                -ExpectedSessionId ([int] $receipt.interactiveSessionId) `
                -ExpectedApplicationPath $applicationPath `
                -TimeoutSeconds 5 `
                -PollMilliseconds 250
            if ([string] $task.State -ne 'Running') {
                throw "计划任务未运行：$($task.State)"
            }
            $AdapterState.Stage = '复核计划任务引擎'
            if (-not [string]::Equals(
                    [System.IO.Path]::GetFullPath([string] $task.Actions.Execute),
                    [System.IO.Path]::GetFullPath($Context.PowerShell7Path),
                    [System.StringComparison]::OrdinalIgnoreCase)) {
                throw '计划任务未使用当前 PowerShell 7。'
            }

            $AdapterState.SettingsSnapshotPath = Join-Path ([string] $receipt.transactionPath) 'install-backup\settings.json'
            $AdapterState.SettingsPath = $settingsPath
            $AdapterState.StartupRequestPath = Join-Path $Context.InstallPath 'startup-request.json'
            $AdapterState.RuntimeStatusPath = $runtimeStatusPath
            $AdapterState.AcceptanceStatusPath = Join-Path $Context.InstallPath 'acceptance-status.json'
            $AdapterState.LaunchToken = [string] $receipt.launchToken
            $AdapterState.ExpectedProcessId = [int] $runtimeStatus.processId
            $AdapterState.ExpectedScheduledTaskIdentity = [string] (Export-ScheduledTask -InputObject $task -ErrorAction Stop)
            $AdapterState.Stage = '候选安装验证完成'

            [pscustomobject]@{
                Validation = $validation
                RuntimeStatus = $runtimeStatus
                InstalledValidation = $installedValidation
            }
        }
        Accept = {
            param($AdapterState, $Context, $Candidate)

            $AdapterState.Stage = '准备事务验收'
            Import-Module (Join-Path $Context.SourceRoot 'Countdown.InstallTransaction.psm1') -Force -DisableNameChecking
            Import-Module (Join-Path $Context.InstallPath 'Countdown.Settings.psm1') -Force
            Import-Module (Join-Path $Context.SourceRoot 'Countdown.Runtime.psm1') -Force
            Import-Module (Join-Path $Context.SourceRoot 'Countdown.TransactionAcceptance.psm1') -Force
            $getInteractiveContext = { Get-CountdownInteractiveDesktopContext }
            $getScheduledTask = {
                param([string] $ExpectedTaskName)
                Get-ScheduledTask -TaskName $ExpectedTaskName -ErrorAction Stop
            }
            $getScheduledTaskIdentity = {
                param([object] $ScheduledTask)
                [string] (Export-ScheduledTask -InputObject $ScheduledTask -ErrorAction Stop)
            }
            $completeTransaction = {
                $AdapterState.Stage = '提交安装事务'
                Complete-CountdownInstallTransaction `
                    -TransactionPath $AdapterState.RollbackTransactionPath `
                    -ExpectedInstallPath $Context.InstallPath `
                    -ExpectedTaskName $Context.TaskName `
                    -ExpectedLegacyTaskName $Context.LegacyTaskName `
                    -ExpectedLauncherPath $AdapterState.LauncherPath `
                    -ExpectedLegacyLauncherPath $AdapterState.LegacyLauncherPath `
                    -ExpectedLauncherTempPath $AdapterState.LauncherTempPath
            }
            $removeCommittedTransaction = {
                $AdapterState.Stage = '清理已提交事务'
                $moveCommittedTransaction = {
                    param([string] $Source, [string] $Destination)
                    # InstallTransaction 只在 journal 写入 committed 后调用此动作；移动失败也已不可回滚。
                    $AdapterState.Committed = $true
                    Move-Item -LiteralPath $Source -Destination $Destination -ErrorAction Stop
                }
                Remove-CountdownCommittedInstallTransaction `
                    -TransactionPath $AdapterState.RollbackTransactionPath `
                    -ExpectedInstallPath $Context.InstallPath `
                    -ExpectedTaskName $Context.TaskName `
                    -ExpectedLegacyTaskName $Context.LegacyTaskName `
                    -ExpectedLauncherPath $AdapterState.LauncherPath `
                    -ExpectedLegacyLauncherPath $AdapterState.LegacyLauncherPath `
                    -ExpectedLauncherTempPath $AdapterState.LauncherTempPath `
                    -MoveAction $moveCommittedTransaction
            }
            $acceptanceArguments = @{
                StartupRequestPath = $AdapterState.StartupRequestPath
                RuntimeStatusPath = $AdapterState.RuntimeStatusPath
                AcceptanceStatusPath = $AdapterState.AcceptanceStatusPath
                LaunchToken = $AdapterState.LaunchToken
                ExpectedInteractiveContext = $AdapterState.InteractiveContext
                ExpectedProcessId = $AdapterState.ExpectedProcessId
                ExpectedSessionId = [int] $AdapterState.InteractiveContext.SessionId
                ExpectedApplicationPath = Join-Path $Context.InstallPath '敏敏子倒计时.ps1'
                TaskName = $Context.TaskName
                ExpectedScheduledTaskIdentity = $AdapterState.ExpectedScheduledTaskIdentity
                SettingsSnapshotPath = $AdapterState.SettingsSnapshotPath
                SettingsPath = $AdapterState.SettingsPath
                GetInteractiveContext = $getInteractiveContext
                GetScheduledTask = $getScheduledTask
                GetScheduledTaskIdentity = $getScheduledTaskIdentity
                CompleteTransaction = $completeTransaction
                RemoveCommittedTransaction = $removeCommittedTransaction
            }
            $AdapterState.Stage = '执行事务验收'
            Invoke-CountdownTransactionAcceptance @acceptanceArguments
        }
        Rollback = {
            param($AdapterState, $Context, $Candidate)

            $AdapterState.Stage = '回滚安装事务'
            if (-not [bool] $AdapterState.TransactionModuleImported -or -not [bool] $AdapterState.InstallStarted) {
                return [pscustomobject]@{
                    PreservedUnmanagedLaunchers = $false
                    UnresolvedCommittedTransaction = $false
                }
            }
            Import-Module (Join-Path $Context.SourceRoot 'Countdown.InstallTransaction.psm1') -Force -DisableNameChecking

            $preservedUnmanagedLaunchers = $false
            $unresolvedCommittedTransaction = $false
            $rollbackResult = $null
            if ([string]::IsNullOrWhiteSpace([string] $AdapterState.RollbackTransactionPath)) {
                $receipt = Read-CountdownDeferredInstallReceipt -Path $Context.DeferredReceiptPath
                if ($null -ne $receipt) {
                    $AdapterState.RollbackTransactionPath = [string] $receipt.transactionPath
                }
            }
            if (-not [string]::IsNullOrWhiteSpace([string] $AdapterState.RollbackTransactionPath)) {
                $journalPath = Join-Path $AdapterState.RollbackTransactionPath 'journal.json'
                if (-not (Test-Path -LiteralPath $journalPath -PathType Leaf)) {
                    throw '无法验证待回滚事务的 journal；拒绝声称已回滚。'
                }
                $rollbackResult = Undo-CountdownInstallTransaction `
                    -TransactionPath $AdapterState.RollbackTransactionPath `
                    -PowerShell7Path $Context.PowerShell7Path `
                    -ExpectedInstallPath $Context.InstallPath `
                    -ExpectedTaskName $Context.TaskName `
                    -ExpectedLegacyTaskName $Context.LegacyTaskName `
                    -ExpectedLauncherPath $AdapterState.LauncherPath `
                    -ExpectedLegacyLauncherPath $AdapterState.LegacyLauncherPath `
                    -ExpectedLauncherTempPath $AdapterState.LauncherTempPath `
                    -PreserveUnmanagedLaunchers
            }
            else {
                $recovery = Recover-CountdownInstallTransactions `
                    -InstallPath $Context.InstallPath `
                    -PowerShell7Path $Context.PowerShell7Path `
                    -ExpectedTaskName $Context.TaskName `
                    -ExpectedLegacyTaskName $Context.LegacyTaskName `
                    -ExpectedLauncherPath $AdapterState.LauncherPath `
                    -ExpectedLegacyLauncherPath $AdapterState.LegacyLauncherPath `
                    -ExpectedLauncherTempPath $AdapterState.LauncherTempPath
                $preservedUnmanagedLaunchers = @($recovery.UnresolvedLauncherOwnershipConflicts).Count -gt 0
                $unresolvedCommittedTransaction = @($recovery.UnresolvedCommittedTransactions).Count -gt 0
            }
            if ($null -ne $rollbackResult -and [bool] $rollbackResult.PreservedUnmanagedLaunchers) {
                $preservedUnmanagedLaunchers = $true
            }
            [pscustomobject]@{
                PreservedUnmanagedLaunchers = $preservedUnmanagedLaunchers
                UnresolvedCommittedTransaction = $unresolvedCommittedTransaction
            }
        }
        Finalize = {
            param($AdapterState, $Context)
            $AdapterState.Stage = '刷新桌面图标'
            $iconRefreshPath = Join-Path $env:SystemRoot 'System32\ie4uinit.exe'
            $null = & $iconRefreshPath -show 2>$null
            if ($LASTEXITCODE -ne 0) {
                throw "桌面图标刷新失败，退出码：$LASTEXITCODE"
            }
        }
        Dispose = {
            param($AdapterState, $Context)
            $AdapterState.Stage = '释放安装事务锁'
            if ($null -ne $AdapterState.InstallMutex) {
                Import-Module (Join-Path $Context.SourceRoot 'Countdown.InstallTransaction.psm1') -Force -DisableNameChecking
                Exit-CountdownInstallTransactionMutex -Mutex $AdapterState.InstallMutex
                $AdapterState.InstallMutex = $null
            }
        }
    }
}

function Invoke-CountdownRemoteDeployment {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [string] $SourceRoot,
        [Parameter(Mandatory)] [string] $InstallPath,
        [Parameter(Mandatory)] [string] $PowerShell7Path,
        [Parameter(Mandatory)] [string] $DeferredReceiptPath,
        [Parameter(Mandatory)] [string] $TaskName,
        [Parameter(Mandatory)] [string] $LegacyTaskName,
        [object] $Adapter
    )

    $context = [pscustomobject]@{
        SourceRoot = [System.IO.Path]::GetFullPath($SourceRoot)
        InstallPath = [System.IO.Path]::GetFullPath($InstallPath)
        PowerShell7Path = [System.IO.Path]::GetFullPath($PowerShell7Path)
        DeferredReceiptPath = [System.IO.Path]::GetFullPath($DeferredReceiptPath)
        TaskName = $TaskName
        LegacyTaskName = $LegacyTaskName
    }
    if (-not (Test-Path -LiteralPath $context.SourceRoot -PathType Container)) {
        throw "远端部署源目录不存在：$($context.SourceRoot)"
    }
    if (-not (Test-Path -LiteralPath $context.PowerShell7Path -PathType Leaf)) {
        throw "远端部署所需的 PowerShell 7 不存在：$($context.PowerShell7Path)"
    }
    if ([string]::IsNullOrWhiteSpace($TaskName) -or [string]::IsNullOrWhiteSpace($LegacyTaskName)) {
        throw '远端部署任务名称不能为空。'
    }

    if ($null -eq $Adapter) {
        $Adapter = New-CountdownRemoteDeploymentWindowsAdapter
    }
    $Adapter = Assert-CountdownRemoteDeploymentAdapter -Adapter $Adapter
    $candidate = $null
    $committed = $false
    $result = $null
    $deploymentFailure = $null
    $stage = '开始候选安装'
    try {
        try {
            $candidate = & $Adapter.Begin $Adapter.State $context
            if ($null -eq $candidate) {
                throw '远端部署 adapter 没有返回候选结果。'
            }
            $stage = '验证候选结果契约'
            $validation = Get-CountdownRemoteDeploymentProperty -InputObject $candidate -Name 'Validation' -Required
            $validationSchemaVersion = [int] (Get-CountdownRemoteDeploymentProperty `
                    -InputObject $validation `
                    -Name 'SchemaVersion' `
                    -Required)
            if ($validationSchemaVersion -ne 1) {
                throw "validation interface schemaVersion 不受支持：$validationSchemaVersion"
            }
            $validationValid = Get-CountdownRemoteDeploymentProperty -InputObject $validation -Name 'Valid' -Required
            if ($validationValid -isnot [bool] -or -not [bool] $validationValid) {
                throw '安装器内置校验未通过。'
            }
            $coreTestsPassed = [int] (Get-CountdownRemoteDeploymentProperty -InputObject $validation -Name 'CoreTestsPassed' -Required)
            $cSharpTestsPassed = [int] (Get-CountdownRemoteDeploymentProperty -InputObject $validation -Name 'CSharpTestsPassed' -Required)
            $uiTestsPassed = [int] (Get-CountdownRemoteDeploymentProperty -InputObject $validation -Name 'UiTestsPassed' -Required)
            $uiValid = Get-CountdownRemoteDeploymentProperty -InputObject $validation -Name 'UiValid' -Required
            if ($uiValid -isnot [bool] -or -not [bool] $uiValid) {
                throw '应用 validate-only 验证未通过。'
            }
            $adapterUiValidated = Get-CountdownRemoteDeploymentProperty -InputObject $validation -Name 'AdapterUiValidated' -Required
            if ($adapterUiValidated -isnot [bool] -or -not [bool] $adapterUiValidated) {
                throw 'adapter UI 验证未通过。'
            }
            $runtimeStatus = Get-CountdownRemoteDeploymentProperty -InputObject $candidate -Name 'RuntimeStatus' -Required
            $runtimeStage = [string] (Get-CountdownRemoteDeploymentProperty -InputObject $runtimeStatus -Name 'stage' -Required)
            $calendarState = [string] (Get-CountdownRemoteDeploymentProperty -InputObject $runtimeStatus -Name 'calendarState' -Required)
            $installedValidation = Get-CountdownRemoteDeploymentProperty -InputObject $candidate -Name 'InstalledValidation'
            if ($null -ne $installedValidation) {
                $installedValidationValid = Get-CountdownRemoteDeploymentProperty -InputObject $installedValidation -Name 'Valid' -Required
                if ($installedValidationValid -isnot [bool] -or -not [bool] $installedValidationValid) {
                    throw '安装后应用 validate-only 验证未通过。'
                }
            }

            $stage = '执行事务验收'
            $acceptance = & $Adapter.Accept $Adapter.State $context $candidate
            $stage = '确认提交边界'
            $committed = Get-CountdownRemoteDeploymentCommitted -Adapter $Adapter
            if (-not $committed) {
                throw '远端部署 adapter 在返回验收成功前未确认提交边界。'
            }
            if ($null -eq $acceptance -or
                -not [string]::Equals(
                    [string] (Get-CountdownRemoteDeploymentProperty -InputObject $acceptance -Name 'AcceptanceState' -Required),
                    'finalized-ready',
                    [System.StringComparison]::Ordinal)) {
                throw '远端部署验收没有达到 finalized-ready。'
            }
            $finalizeProperty = $Adapter.PSObject.Properties['Finalize']
            if ($null -ne $finalizeProperty) {
                $stage = '执行提交后收尾'
                [void] (& $finalizeProperty.Value $Adapter.State $context)
            }

            $stage = '组装部署结果'
            $result = [pscustomobject]@{
                SchemaVersion = 1
                Valid = $true
                CoreTests = $coreTestsPassed
                CSharpTests = $cSharpTestsPassed
                UiTests = $uiTestsPassed
                UiValid = $true
                AdapterUiValidated = $true
                TaskState = [string] (Get-CountdownRemoteDeploymentProperty -InputObject $acceptance -Name 'TaskState' -Required)
                RuntimeProcessId = [int] (Get-CountdownRemoteDeploymentProperty -InputObject $acceptance -Name 'RuntimeProcessId' -Required)
                RuntimeStage = $runtimeStage
                CalendarState = $calendarState
                AcceptanceState = [string] (Get-CountdownRemoteDeploymentProperty -InputObject $acceptance -Name 'AcceptanceState' -Required)
                SettingsHashBefore = [string] (Get-CountdownRemoteDeploymentProperty -InputObject $acceptance -Name 'SettingsHashBefore' -Required)
                SettingsHashAfter = [string] (Get-CountdownRemoteDeploymentProperty -InputObject $acceptance -Name 'SettingsHashAfter' -Required)
            }
        }
        catch {
            $failureRecord = $_
            $failureStage = Get-CountdownRemoteDeploymentStage -Adapter $Adapter -Fallback $stage
            $failure = Get-CountdownRemoteDeploymentFailureDiagnostic `
                -Stage $failureStage `
                -ErrorRecord $failureRecord
            try {
                $committed = Get-CountdownRemoteDeploymentCommitted -Adapter $Adapter
            }
            catch {
                $commitProbeRecord = $_
                [void] (Write-CountdownRemoteDeploymentFailureReceipt `
                        -Context $context `
                        -Adapter $Adapter `
                        -Stage $failureStage `
                        -ErrorRecord $failureRecord `
                        -CommitState 'unknown' `
                        -RollbackAttempted $false `
                        -RollbackSucceeded $false `
                        -PreservedUnmanagedLaunchers $false)
                $commitProbeFailure = Get-CountdownRemoteDeploymentFailureDiagnostic `
                    -Stage '确认提交状态' `
                    -ErrorRecord $commitProbeRecord
                throw "远端部署失败，adapter 提交状态无法验证，未回滚：$failure；提交状态错误：$commitProbeFailure"
            }
            if ($committed) {
                [void] (Write-CountdownRemoteDeploymentFailureReceipt `
                        -Context $context `
                        -Adapter $Adapter `
                        -Stage $failureStage `
                        -ErrorRecord $failureRecord `
                        -CommitState 'committed' `
                        -RollbackAttempted $false `
                        -RollbackSucceeded $false `
                        -PreservedUnmanagedLaunchers $false)
                throw "部署已提交，但提交后收尾失败，未回滚：$failure"
            }

            $preRollbackCaptureFailure = $null
            try {
                $preRollbackCapture = Write-CountdownPreRollbackDiagnostics -Context $context -Adapter $Adapter -Stage $failureStage
                if (-not [bool] $preRollbackCapture.Success) { $preRollbackCaptureFailure = [string] $preRollbackCapture.FailureMessage }
            }
            catch { $preRollbackCaptureFailure = $_.Exception.Message }
            $preRollbackCaptureSuffix = if ([string]::IsNullOrWhiteSpace($preRollbackCaptureFailure)) { '' } else { "；rollback 前诊断捕获失败：$preRollbackCaptureFailure" }

            try {
                $rollback = & $Adapter.Rollback $Adapter.State $context $candidate
            }
            catch {
                [void] (Write-CountdownRemoteDeploymentFailureReceipt `
                        -Context $context `
                        -Adapter $Adapter `
                        -Stage $failureStage `
                        -ErrorRecord $failureRecord `
                        -CommitState 'not-committed' `
                        -RollbackAttempted $true `
                        -RollbackSucceeded $false `
                        -PreservedUnmanagedLaunchers $false)
                throw "远端部署失败：$failure；回滚也失败：$($_.Exception.Message)$preRollbackCaptureSuffix"
            }
            $unresolvedCommitted = $false
            $preservedUnmanaged = $false
            if ($null -ne $rollback) {
                $unresolvedProperty = $rollback.PSObject.Properties['UnresolvedCommittedTransaction']
                $preservedProperty = $rollback.PSObject.Properties['PreservedUnmanagedLaunchers']
                $unresolvedCommitted = $null -ne $unresolvedProperty -and [bool] $unresolvedProperty.Value
                $preservedUnmanaged = $null -ne $preservedProperty -and [bool] $preservedProperty.Value
            }
            $failureReceiptCommitState = if ($unresolvedCommitted) { 'committed' } else { 'not-committed' }
            $failureReceiptRollbackSucceeded = -not $unresolvedCommitted
            [void] (Write-CountdownRemoteDeploymentFailureReceipt `
                    -Context $context `
                    -Adapter $Adapter `
                    -Stage $failureStage `
                    -ErrorRecord $failureRecord `
                    -CommitState $failureReceiptCommitState `
                    -RollbackAttempted $true `
                    -RollbackSucceeded $failureReceiptRollbackSucceeded `
                    -PreservedUnmanagedLaunchers $preservedUnmanaged)
            if ($unresolvedCommitted) {
                throw "远端部署失败；检测到已提交但未完成解锁确认的事务，未声称回滚并保留了证据：$failure$preRollbackCaptureSuffix"
            }
            if ($preservedUnmanaged) {
                throw "远端部署失败，已恢复应用、任务和时间服务；为保护用户替换的桌面启动器，保留了事务证据：$failure$preRollbackCaptureSuffix"
            }
            throw "远端部署失败，已回滚：$failure$preRollbackCaptureSuffix"
        }
    }
    catch {
        $deploymentFailure = $_.Exception.Message
    }

    $disposeFailure = $null
    $disposeFailureRecord = $null
    try {
        [void] (& $Adapter.Dispose $Adapter.State $context)
    }
    catch {
        $disposeFailureRecord = $_
        $disposeFailure = $_.Exception.Message
    }

    if ($null -ne $disposeFailureRecord) {
        $disposeStage = Get-CountdownRemoteDeploymentStage -Adapter $Adapter -Fallback '释放部署资源'
        $disposeCommitState = if ($committed) { 'committed' } else { 'unknown' }
        [void] (Write-CountdownRemoteDeploymentFailureReceipt `
                -Context $context `
                -Adapter $Adapter `
                -Stage $disposeStage `
                -ErrorRecord $disposeFailureRecord `
                -CommitState $disposeCommitState `
                -RollbackAttempted $false `
                -RollbackSucceeded $false `
                -PreservedUnmanagedLaunchers $false)
    }

    if ($null -ne $deploymentFailure) {
        if ($null -ne $disposeFailure) {
            throw "$deploymentFailure；资源释放也失败：$disposeFailure"
        }
        throw $deploymentFailure
    }
    if ($null -ne $disposeFailure) {
        if ($committed) {
            throw "部署已提交，但资源释放失败，未回滚：$disposeFailure"
        }
        throw "远端部署资源释放失败：$disposeFailure"
    }
    $result
}

Export-ModuleMember -Function Invoke-CountdownRemoteDeployment, ConvertFrom-CountdownPreRollbackDiagnosticsBytes
