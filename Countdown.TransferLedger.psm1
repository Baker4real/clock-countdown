#requires -Version 7.0

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Read-CountdownTransferLedger {
    param([Parameter(Mandatory)] [string] $Path)

    $attempts = [System.Collections.Generic.List[object]]::new()
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        return [ordered]@{
            transferAttemptsVersion = '1.0'
            generatedAtUtc = [DateTimeOffset]::UtcNow.ToString('o')
            attempts = $attempts
        }
    }

    $json = [System.IO.File]::ReadAllText($Path, [System.Text.UTF8Encoding]::new($false, $true))
    $options = [System.Text.Json.JsonDocumentOptions]::new()
    $options.AllowTrailingCommas = $false
    $options.CommentHandling = [System.Text.Json.JsonCommentHandling]::Disallow
    try {
        $document = [System.Text.Json.JsonDocument]::Parse($json, $options)
    }
    catch {
        throw "SCP 传输账本不是单个完整 JSON object：$($_.Exception.Message)"
    }
    try {
        if ($document.RootElement.ValueKind -ne [System.Text.Json.JsonValueKind]::Object) {
            throw 'SCP 传输账本根节点必须是 object。'
        }
        $rootNames = [string[]] @('transferAttemptsVersion', 'generatedAtUtc', 'attempts')
        $rootSet = [System.Collections.Generic.HashSet[string]]::new($rootNames, [System.StringComparer]::Ordinal)
        $rootActual = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
        $root = [ordered]@{}
        foreach ($property in $document.RootElement.EnumerateObject()) {
            if (-not $rootActual.Add($property.Name)) { throw "SCP 传输账本根对象包含重复字段：$($property.Name)" }
            if (-not $rootSet.Contains($property.Name)) { throw "SCP 传输账本根对象包含额外字段：$($property.Name)" }
            $root[$property.Name] = $property.Value
        }
        foreach ($name in $rootNames) {
            if (-not $rootActual.Contains($name)) { throw "SCP 传输账本根对象缺少字段：$name" }
        }
        if ($root.transferAttemptsVersion.ValueKind -ne [System.Text.Json.JsonValueKind]::String -or
            $root.transferAttemptsVersion.GetString() -ne '1.0') {
            throw 'SCP 传输账本 transferAttemptsVersion 必须是字符串 1.0。'
        }
        if ($root.generatedAtUtc.ValueKind -ne [System.Text.Json.JsonValueKind]::String) {
            throw 'SCP 传输账本 generatedAtUtc 必须是 round-trip UTC 时间字符串。'
        }
        $generatedAt = [DateTimeOffset]::MinValue
        if (-not [DateTimeOffset]::TryParseExact(
                $root.generatedAtUtc.GetString(),
                'o',
                [Globalization.CultureInfo]::InvariantCulture,
                [Globalization.DateTimeStyles]::RoundtripKind,
                [ref] $generatedAt) -or $generatedAt.Offset -ne [TimeSpan]::Zero) {
            throw 'SCP 传输账本 generatedAtUtc 必须是 round-trip UTC 时间字符串。'
        }
        if ($root.attempts.ValueKind -ne [System.Text.Json.JsonValueKind]::Array) {
            throw 'SCP 传输账本 attempts 必须是数组。'
        }

        $attemptNames = [string[]] @('id', 'method', 'status', 'exitCode', 'transferStarted', 'bytesVerified', 'metrics')
        $metricNames = [string[]] @('bytes', 'elapsed_seconds', 'mib_per_second', 'MiBPerSecond', 'mbps')
        foreach ($attemptElement in $root.attempts.EnumerateArray()) {
            if ($attemptElement.ValueKind -ne [System.Text.Json.JsonValueKind]::Object) {
                throw 'SCP 传输账本 attempt 必须是 object。'
            }
            $attemptSet = [System.Collections.Generic.HashSet[string]]::new($attemptNames, [System.StringComparer]::Ordinal)
            $attemptActual = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
            $attemptValues = [ordered]@{}
            foreach ($property in $attemptElement.EnumerateObject()) {
                if (-not $attemptActual.Add($property.Name)) { throw "SCP 传输账本 attempt 包含重复字段：$($property.Name)" }
                if (-not $attemptSet.Contains($property.Name)) { throw "SCP 传输账本 attempt 包含额外字段：$($property.Name)" }
                $attemptValues[$property.Name] = $property.Value
            }
            foreach ($name in $attemptNames) {
                if (-not $attemptActual.Contains($name)) { throw "SCP 传输账本 attempt 缺少字段：$name" }
            }
            foreach ($name in @('id', 'method', 'status')) {
                if ($attemptValues[$name].ValueKind -ne [System.Text.Json.JsonValueKind]::String) {
                    throw "SCP 传输账本 attempt.$name 必须是 string。"
                }
            }
            $id = $attemptValues.id.GetString()
            $method = $attemptValues.method.GetString()
            $status = $attemptValues.status.GetString()
            if ([string]::IsNullOrWhiteSpace($id) -or $id.Length -gt 128) { throw 'SCP 传输账本 attempt.id 无效。' }
            if ($method -notin @('scp', 'ssh')) { throw 'SCP 传输账本 attempt.method 枚举无效。' }
            if ($status -notin @('succeeded', 'failed', 'failed-before-transfer', 'failed-bytes-unverified')) {
                throw 'SCP 传输账本 attempt.status 枚举无效。'
            }
            $exitCode64 = 0L
            if ($attemptValues.exitCode.ValueKind -ne [System.Text.Json.JsonValueKind]::Number -or
                -not $attemptValues.exitCode.TryGetInt64([ref] $exitCode64) -or
                $exitCode64 -lt -1 -or $exitCode64 -gt [int]::MaxValue) {
                throw 'SCP 传输账本 attempt.exitCode 类型或值域无效。'
            }
            $transferStarted = $null
            if ($attemptValues.transferStarted.ValueKind -eq [System.Text.Json.JsonValueKind]::True) { $transferStarted = $true }
            elseif ($attemptValues.transferStarted.ValueKind -eq [System.Text.Json.JsonValueKind]::False) { $transferStarted = $false }
            elseif ($attemptValues.transferStarted.ValueKind -ne [System.Text.Json.JsonValueKind]::Null) {
                throw 'SCP 传输账本 attempt.transferStarted 必须是 bool 或 null。'
            }
            if ($attemptValues.bytesVerified.ValueKind -notin @([System.Text.Json.JsonValueKind]::True, [System.Text.Json.JsonValueKind]::False)) {
                throw 'SCP 传输账本 attempt.bytesVerified 必须是 bool。'
            }
            $bytesVerified = $attemptValues.bytesVerified.ValueKind -eq [System.Text.Json.JsonValueKind]::True

            if ($attemptValues.metrics.ValueKind -ne [System.Text.Json.JsonValueKind]::Object) {
                throw 'SCP 传输账本 attempt.metrics 必须是 object。'
            }
            $metricSet = [System.Collections.Generic.HashSet[string]]::new($metricNames, [System.StringComparer]::Ordinal)
            $metricActual = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
            $metricValues = [ordered]@{}
            foreach ($property in $attemptValues.metrics.EnumerateObject()) {
                if (-not $metricActual.Add($property.Name)) { throw "SCP 传输账本 metrics 包含重复字段：$($property.Name)" }
                if (-not $metricSet.Contains($property.Name)) { throw "SCP 传输账本 metrics 包含额外字段：$($property.Name)" }
                $metricValues[$property.Name] = $property.Value
            }
            foreach ($name in $metricNames) {
                if (-not $metricActual.Contains($name)) { throw "SCP 传输账本 metrics 缺少字段：$name" }
            }
            $bytes = 0L
            if ($metricValues.bytes.ValueKind -ne [System.Text.Json.JsonValueKind]::Number -or
                -not $metricValues.bytes.TryGetInt64([ref] $bytes) -or $bytes -lt 0) {
                throw 'SCP 传输账本 metrics.bytes 类型或值域无效。'
            }
            $metricNumbers = [ordered]@{}
            foreach ($name in @('elapsed_seconds', 'mib_per_second', 'MiBPerSecond', 'mbps')) {
                $number = 0.0
                if ($metricValues[$name].ValueKind -ne [System.Text.Json.JsonValueKind]::Number -or
                    -not $metricValues[$name].TryGetDouble([ref] $number) -or
                    [double]::IsNaN($number) -or [double]::IsInfinity($number) -or $number -lt 0) {
                    throw "SCP 传输账本 metrics.$name 类型或值域无效。"
                }
                $metricNumbers[$name] = $number
            }
            if ($metricNumbers.mib_per_second -ne $metricNumbers.MiBPerSecond -or
                (($bytes -eq 0 -or $metricNumbers.elapsed_seconds -eq 0) -and
                    ($metricNumbers.mib_per_second -ne 0 -or $metricNumbers.mbps -ne 0)) -or
                ($bytes -gt 0 -and $metricNumbers.elapsed_seconds -gt 0 -and
                    ($metricNumbers.mib_per_second -le 0 -or $metricNumbers.mbps -le 0))) {
                throw 'SCP 传输账本 metrics 速率字段彼此或与零字节/零耗时状态不一致。'
            }
            if (($status -eq 'succeeded' -and ($exitCode64 -ne 0 -or -not $bytesVerified -or $transferStarted -ne ($bytes -gt 0))) -or
                ($status -eq 'failed' -and (-not $bytesVerified -or $transferStarted -ne ($bytes -gt 0))) -or
                ($status -eq 'failed-before-transfer' -and ($exitCode64 -eq 0 -or $transferStarted -ne $false -or -not $bytesVerified -or $bytes -ne 0)) -or
                ($status -eq 'failed-bytes-unverified' -and ($null -ne $transferStarted -or $bytesVerified -or $bytes -ne 0))) {
                throw 'SCP 传输账本 attempt 状态与退出码/字节证据不一致。'
            }

            [void] $attempts.Add([pscustomobject][ordered]@{
                    id = $id
                    method = $method
                    status = $status
                    exitCode = [int] $exitCode64
                    transferStarted = $transferStarted
                    bytesVerified = $bytesVerified
                    metrics = [pscustomobject][ordered]@{
                        bytes = $bytes
                        elapsed_seconds = $metricNumbers.elapsed_seconds
                        mib_per_second = $metricNumbers.mib_per_second
                        MiBPerSecond = $metricNumbers.MiBPerSecond
                        mbps = $metricNumbers.mbps
                    }
                })
        }
    }
    finally {
        $document.Dispose()
    }

    [ordered]@{
        transferAttemptsVersion = '1.0'
        generatedAtUtc = [DateTimeOffset]::UtcNow.ToString('o')
        attempts = $attempts
    }
}

function Write-CountdownTransferLedger {
    param(
        [Parameter(Mandatory)] [string] $Path,
        [Parameter(Mandatory)] [System.Collections.IDictionary] $Ledger
    )

    $directory = Split-Path -Parent $Path
    if (-not (Test-Path -LiteralPath $directory -PathType Container)) {
        New-Item -ItemType Directory -Path $directory -Force | Out-Null
    }

    $temporaryPath = "$Path.tmp-$([guid]::NewGuid().ToString('N'))"
    try {
        $json = $Ledger | ConvertTo-Json -Depth 6
        [System.IO.File]::WriteAllText(
            $temporaryPath,
            $json,
            [System.Text.UTF8Encoding]::new($false)
        )
        [System.IO.File]::Move($temporaryPath, $Path, $true)
    }
    finally {
        if (Test-Path -LiteralPath $temporaryPath -PathType Leaf) {
            [System.IO.File]::Delete($temporaryPath)
        }
    }
}

function Invoke-CountdownScpProcess {
    param(
        [Parameter(Mandatory)] [string[]] $Options,
        [Parameter(Mandatory)] [string] $SourcePath,
        [Parameter(Mandatory)] [string] $Destination,
        [string] $ExecutablePath,
        [scriptblock] $CommandRunner
    )

    $fileName = if ([string]::IsNullOrWhiteSpace($ExecutablePath)) {
        'scp.exe'
    }
    else {
        $ExecutablePath
    }
    $arguments = [string[]] (@($Options) + @($SourcePath, $Destination))
    if ($null -ne $CommandRunner) {
        return [int] (& $CommandRunner $fileName $arguments)
    }

    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $fileName
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    foreach ($argument in $arguments) {
        [void] $startInfo.ArgumentList.Add($argument)
    }

    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    try {
        if (-not $process.Start()) {
            throw "无法启动 scp 进程：$fileName"
        }
        $process.WaitForExit()
        $process.ExitCode
    }
    finally {
        $process.Dispose()
    }
}

function Invoke-CountdownRecordedScpTransfer {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [string] $LedgerPath,
        [Parameter(Mandatory)] [ValidateNotNullOrEmpty()] [string] $Id,
        [Parameter(Mandatory)] [string] $SourcePath,
        [Parameter(Mandatory)] [ValidateNotNullOrEmpty()] [string] $Destination,
        [Parameter(Mandatory)] [ValidateNotNullOrEmpty()] [string] $RemotePath,
        [string[]] $ScpOptions = @(),
        [scriptblock] $ScpInvoker,
        [scriptblock] $RemoteBytesProbe,
        [scriptblock] $StopwatchFactory,
        [string] $ScpExecutablePath,
        [scriptblock] $ScpCommandRunner
    )

    $resolvedLedgerPath = [System.IO.Path]::GetFullPath($LedgerPath)
    $resolvedSourcePath = [System.IO.Path]::GetFullPath($SourcePath)
    if (-not (Test-Path -LiteralPath $resolvedSourcePath -PathType Leaf)) {
        throw "SCP 本地源文件不存在：$resolvedSourcePath"
    }

    $ledger = Read-CountdownTransferLedger -Path $resolvedLedgerPath
    $sourceLength = (Get-Item -LiteralPath $resolvedSourcePath).Length
    if ($null -eq $ScpInvoker) {
        $ScpInvoker = {
            param($Options, $LocalSourcePath, $RemoteDestination)
            Invoke-CountdownScpProcess `
                -Options $Options `
                -SourcePath $LocalSourcePath `
                -Destination $RemoteDestination `
                -ExecutablePath $ScpExecutablePath `
                -CommandRunner $ScpCommandRunner
        }
    }
    $stopwatch = if ($null -eq $StopwatchFactory) {
        [System.Diagnostics.Stopwatch]::new()
    }
    else {
        & $StopwatchFactory
    }
    if ($null -eq $stopwatch) {
        throw 'SCP 计时 adapter 没有返回 Stopwatch。'
    }

    $exitCode = -1
    $invocationException = $null
    $stopwatch.Start()
    try {
        $exitCode = [int] (& $ScpInvoker $ScpOptions $resolvedSourcePath $Destination)
    }
    catch {
        $invocationException = $_.Exception
    }
    finally {
        $stopwatch.Stop()
    }

    $elapsedSeconds = [double] $stopwatch.Elapsed.TotalSeconds
    $transferredBytes = if ($exitCode -eq 0) {
        [long] $sourceLength
    }
    else {
        0L
    }
    $remoteBytesVerified = $exitCode -eq 0
    if ($exitCode -ne 0 -and $null -ne $RemoteBytesProbe) {
        try {
            $remoteBytes = & $RemoteBytesProbe $RemotePath
            if ($null -ne $remoteBytes) {
                $probedBytes = [long] $remoteBytes
                if ($probedBytes -ge 0) {
                    $remoteBytesVerified = $true
                    $transferredBytes = [Math]::Min([long] $sourceLength, $probedBytes)
                }
            }
        }
        catch {
            $transferredBytes = 0L
        }
    }
    $mibPerSecond = if ($elapsedSeconds -gt 0) {
        ($transferredBytes / 1MB) / $elapsedSeconds
    }
    else {
        0.0
    }
    $mbps = if ($elapsedSeconds -gt 0) {
        ($transferredBytes * 8.0 / 1000000.0) / $elapsedSeconds
    }
    else {
        0.0
    }
    $status = if ($exitCode -eq 0) {
        'succeeded'
    }
    elseif ($transferredBytes -gt 0) {
        'failed'
    }
    elseif ($remoteBytesVerified) {
        'failed-before-transfer'
    }
    else {
        'failed-bytes-unverified'
    }
    $attempt = [pscustomobject][ordered]@{
        id = $Id
        method = 'scp'
        status = $status
        exitCode = $exitCode
        transferStarted = if ($status -eq 'failed-bytes-unverified') {
            $null
        }
        else {
            $exitCode -eq 0 -or $transferredBytes -gt 0
        }
        bytesVerified = $remoteBytesVerified
        metrics = [pscustomobject][ordered]@{
            bytes = $transferredBytes
            elapsed_seconds = [Math]::Round($elapsedSeconds, 6)
            mib_per_second = [Math]::Round($mibPerSecond, 6)
            MiBPerSecond = [Math]::Round($mibPerSecond, 6)
            mbps = [Math]::Round($mbps, 6)
        }
    }
    [void] $ledger.attempts.Add($attempt)
    $ledger.generatedAtUtc = [DateTimeOffset]::UtcNow.ToString('o')
    Write-CountdownTransferLedger -Path $resolvedLedgerPath -Ledger $ledger
    if ($null -ne $invocationException) {
        throw "SCP 传输无法执行：$($invocationException.Message)；尝试已写入：$resolvedLedgerPath"
    }
    if ($exitCode -ne 0) {
        throw "SCP 传输失败，退出码：$exitCode；尝试已写入：$resolvedLedgerPath"
    }
    $attempt
}

function Get-CountdownSshReadResultProperty {
    param(
        [Parameter(Mandatory)] [object] $Result,
        [Parameter(Mandatory)] [string] $Name
    )

    $property = $Result.PSObject.Properties[$Name]
    if ($null -eq $property) {
        throw "SSH 回读 adapter 缺少字段：$Name"
    }
    $property.Value
}

function Invoke-CountdownExternalCommandProcess {
    param(
        [Parameter(Mandatory)] [ValidateNotNullOrEmpty()] [string] $FileName,
        [Parameter(Mandatory)] [AllowEmptyCollection()] [string[]] $Arguments
    )

    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $FileName
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.StandardOutputEncoding = [System.Text.UTF8Encoding]::new($false)
    $startInfo.StandardErrorEncoding = [System.Text.UTF8Encoding]::new($false)
    foreach ($argument in $Arguments) { [void] $startInfo.ArgumentList.Add($argument) }

    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    try {
        if (-not $process.Start()) { throw "无法启动命令：$FileName" }
        $standardOutput = $process.StandardOutput.ReadToEndAsync()
        $standardError = $process.StandardError.ReadToEndAsync()
        $process.WaitForExit()
        [pscustomobject]@{
            ExitCode = [int] $process.ExitCode
            StandardOutput = [string] $standardOutput.GetAwaiter().GetResult()
            StandardError = [string] $standardError.GetAwaiter().GetResult()
            ProcessId = [int] $process.Id
        }
    }
    finally { $process.Dispose() }
}

function Invoke-CountdownSshBase64CommandRead {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [string[]] $Options,
        [Parameter(Mandatory)] [ValidateNotNullOrEmpty()] [string] $Destination,
        [Parameter(Mandatory)] [ValidateNotNullOrEmpty()] [string] $RemoteCommand,
        [Parameter(Mandatory)] [ValidateRange(1, 32768)] [int] $MaximumDecodedBytes,
        [Parameter(Mandatory)] [ValidateNotNullOrEmpty()] [string] $Description,
        [scriptblock] $CommandRunner,
        [string] $SshExecutablePath
    )

    if ($Destination -notmatch '\A[^@\s]+@[^@\s]+\z') {
        throw '严格 SSH destination 必须是 user@host。'
    }
    if ($RemoteCommand.Length -gt 30000) {
        return [pscustomobject]@{
            Success = $false; ExitCode = -1; Bytes = $null
            FailureMessage = "$Description 命令超过安全长度：$($RemoteCommand.Length)"
        }
    }
    if ($Options.Count -ne 16 -or $Options[0] -ne '-i' -or [string]::IsNullOrWhiteSpace($Options[1])) {
        throw '严格 SSH 参数必须包含且只包含一个非空 identity file 和七个受管选项。'
    }
    $expectedOptionNames = [string[]] @(
        'BatchMode', 'IdentitiesOnly', 'ConnectTimeout', 'ConnectionAttempts',
        'StrictHostKeyChecking', 'UserKnownHostsFile', 'KexAlgorithms')
    $optionValues = [ordered]@{}
    for ($index = 2; $index -lt $Options.Count; $index += 2) {
        if ($Options[$index] -ne '-o' -or $Options[$index + 1] -notmatch '\A([^=]+)=(.+)\z') {
            throw '严格 SSH 参数必须使用独立的 -o Name=Value 参数。'
        }
        $name = $Matches[1]
        $value = $Matches[2]
        if ($name -notin $expectedOptionNames -or $optionValues.Contains($name)) {
            throw "严格 SSH 参数包含额外或重复选项：$name"
        }
        $optionValues[$name] = $value
    }
    foreach ($name in $expectedOptionNames) {
        if (-not $optionValues.Contains($name)) { throw "严格 SSH 参数缺少选项：$name" }
    }
    if ($optionValues.BatchMode -ne 'yes' -or
        $optionValues.IdentitiesOnly -ne 'yes' -or
        $optionValues.StrictHostKeyChecking -ne 'yes' -or
        [string]::IsNullOrWhiteSpace([string] $optionValues.UserKnownHostsFile)) {
        throw '严格 SSH 参数未启用 BatchMode、IdentitiesOnly、StrictHostKeyChecking 或 known-hosts。'
    }

    if ($null -eq $CommandRunner) {
        $CommandRunner = {
            param($FileName, $Arguments)
            Invoke-CountdownExternalCommandProcess -FileName $FileName -Arguments $Arguments
        }
    }
    $arguments = [string[]] (@($Options) + @($Destination, $RemoteCommand))
    $sshFileName = if ([string]::IsNullOrWhiteSpace($SshExecutablePath)) {
        'ssh.exe'
    }
    else {
        $SshExecutablePath
    }
    try {
        $runnerOutput = @(& $CommandRunner $sshFileName $arguments)
        if ($runnerOutput.Count -ne 1) { throw '严格 SSH command runner 必须返回一个结果对象。' }
        $runnerResult = $runnerOutput[0]
        foreach ($name in @('ExitCode', 'StandardOutput', 'StandardError', 'ProcessId')) {
            if ($null -eq $runnerResult.PSObject.Properties[$name]) {
                throw "严格 SSH command runner 缺少字段：$name"
            }
        }
        if (($runnerResult.ExitCode -isnot [int] -and $runnerResult.ExitCode -isnot [long]) -or
            ($runnerResult.ProcessId -isnot [int] -and $runnerResult.ProcessId -isnot [long]) -or
            $runnerResult.StandardOutput -isnot [string] -or $runnerResult.StandardError -isnot [string]) {
            throw '严格 SSH command runner 返回字段类型无效。'
        }
        $exitCode = [int] $runnerResult.ExitCode
        if ($exitCode -ne 0) {
            $diagnostic = ([string] $runnerResult.StandardError).Trim()
            if ([string]::IsNullOrWhiteSpace($diagnostic)) { $diagnostic = '<no stderr>' }
            return [pscustomobject]@{
                Success = $false; ExitCode = $exitCode; Bytes = $null
                FailureMessage = "$Description 失败：$diagnostic"
            }
        }
        $text = ([string] $runnerResult.StandardOutput).Trim()
        try { $bytes = [Convert]::FromBase64String($text) }
        catch {
            $wireBytes = [System.Text.UTF8Encoding]::new($false).GetBytes([string] $runnerResult.StandardOutput)
            return [pscustomobject]@{
                Success = $false; ExitCode = 0; Bytes = [byte[]] $wireBytes
                FailureMessage = "$Description 返回值不是有效 Base64：$($_.Exception.Message)"
            }
        }
        if ($bytes.Length -lt 1 -or $bytes.Length -gt $MaximumDecodedBytes) {
            return [pscustomobject]@{
                Success = $false; ExitCode = 0; Bytes = [byte[]] $bytes
                FailureMessage = "$Description 返回 payload 大小无效：$($bytes.Length)"
            }
        }
        [pscustomobject]@{
            Success = $true; ExitCode = 0; Bytes = [byte[]] $bytes; FailureMessage = $null
        }
    }
    catch {
        [pscustomobject]@{
            Success = $false; ExitCode = -1; Bytes = $null
            FailureMessage = "无法启动 $Description：$($_.Exception.Message)"
        }
    }
}

function Invoke-CountdownRecordedSshRead {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [string] $LedgerPath,
        [Parameter(Mandatory)] [ValidateNotNullOrEmpty()] [string] $Id,
        [Parameter(Mandatory)] [scriptblock] $Reader,
        [scriptblock] $StopwatchFactory
    )

    $resolvedLedgerPath = [System.IO.Path]::GetFullPath($LedgerPath)
    $ledger = Read-CountdownTransferLedger -Path $resolvedLedgerPath
    $stopwatch = if ($null -eq $StopwatchFactory) {
        [System.Diagnostics.Stopwatch]::new()
    }
    else {
        & $StopwatchFactory
    }
    if ($null -eq $stopwatch) {
        throw 'SSH 回读计时 adapter 没有返回 Stopwatch。'
    }

    $readerResult = $null
    $invocationException = $null
    $stopwatch.Start()
    try {
        $readerOutput = @(& $Reader)
        if ($readerOutput.Count -ne 1) {
            throw 'SSH 回读 adapter 必须返回一个结果对象。'
        }
        $readerResult = $readerOutput[0]
    }
    catch {
        $invocationException = $_.Exception
    }
    finally {
        $stopwatch.Stop()
    }

    $exitCode = -1
    $succeeded = $false
    $payload = $null
    $failureMessage = $null
    if ($null -eq $invocationException) {
        try {
            $successValue = Get-CountdownSshReadResultProperty -Result $readerResult -Name 'Success'
            if ($successValue -isnot [bool]) {
                throw 'SSH 回读 adapter 的 Success 必须是 bool。'
            }
            $succeeded = [bool] $successValue

            $exitCodeValue = Get-CountdownSshReadResultProperty -Result $readerResult -Name 'ExitCode'
            if ($exitCodeValue -isnot [int] -and $exitCodeValue -isnot [long]) {
                throw 'SSH 回读 adapter 的 ExitCode 必须是整数。'
            }
            if ([long] $exitCodeValue -lt [int]::MinValue -or [long] $exitCodeValue -gt [int]::MaxValue) {
                throw 'SSH 回读 adapter 的 ExitCode 超出范围。'
            }
            $exitCode = [int] $exitCodeValue

            $payloadProperty = $readerResult.PSObject.Properties['Bytes']
            if ($null -eq $payloadProperty) {
                throw 'SSH 回读 adapter 缺少字段：Bytes'
            }
            $payloadValue = $payloadProperty.Value
            if ($null -ne $payloadValue -and $payloadValue -isnot [byte[]]) {
                throw 'SSH 回读 adapter 的 Bytes 必须是 byte[] 或 null。'
            }
            if ($null -ne $payloadValue) {
                $payload = [byte[]] $payloadValue
            }

            $failureValue = Get-CountdownSshReadResultProperty -Result $readerResult -Name 'FailureMessage'
            if ($null -ne $failureValue -and $failureValue -isnot [string]) {
                throw 'SSH 回读 adapter 的 FailureMessage 必须是字符串或 null。'
            }
            $failureMessage = [string] $failureValue
            if ($succeeded -and $exitCode -ne 0) {
                throw 'SSH 回读 adapter 成功时 ExitCode 必须为 0。'
            }
            if (-not $succeeded -and [string]::IsNullOrWhiteSpace($failureMessage)) {
                throw 'SSH 回读 adapter 失败时必须提供原因。'
            }
        }
        catch {
            $invocationException = $_.Exception
        }
    }

    $elapsedSeconds = [double] $stopwatch.Elapsed.TotalSeconds
    $payloadBytesVerified = $null -eq $invocationException -and $null -ne $payload
    $transferredBytes = if ($payloadBytesVerified) {
        [long] $payload.Length
    }
    else {
        0L
    }
    $mibPerSecond = if ($elapsedSeconds -gt 0) {
        ($transferredBytes / 1MB) / $elapsedSeconds
    }
    else {
        0.0
    }
    $mbps = if ($elapsedSeconds -gt 0) {
        ($transferredBytes * 8.0 / 1000000.0) / $elapsedSeconds
    }
    else {
        0.0
    }
    $readSucceeded = $null -eq $invocationException -and $succeeded
    $readFailedWithVerifiedBytes = $null -eq $invocationException -and -not $succeeded -and $payloadBytesVerified
    $attempt = [pscustomobject][ordered]@{
        id = $Id
        method = 'ssh'
        status = if ($readSucceeded) { 'succeeded' } elseif ($readFailedWithVerifiedBytes) { 'failed' } else { 'failed-bytes-unverified' }
        exitCode = $exitCode
        transferStarted = if ($readSucceeded -or $readFailedWithVerifiedBytes) { $transferredBytes -gt 0 } else { $null }
        bytesVerified = $readSucceeded -or $readFailedWithVerifiedBytes
        metrics = [pscustomobject][ordered]@{
            bytes = $transferredBytes
            elapsed_seconds = [Math]::Round($elapsedSeconds, 6)
            mib_per_second = [Math]::Round($mibPerSecond, 6)
            MiBPerSecond = [Math]::Round($mibPerSecond, 6)
            mbps = [Math]::Round($mbps, 6)
        }
    }
    [void] $ledger.attempts.Add($attempt)
    $ledger.generatedAtUtc = [DateTimeOffset]::UtcNow.ToString('o')
    Write-CountdownTransferLedger -Path $resolvedLedgerPath -Ledger $ledger

    if ($null -ne $invocationException) {
        throw "SSH 回读无法执行：$($invocationException.Message)；尝试已写入：$resolvedLedgerPath"
    }
    if (-not $succeeded) {
        throw "SSH 回读失败，退出码：$exitCode；$failureMessage；尝试已写入：$resolvedLedgerPath"
    }
    if ($null -ne $payload) {
        Write-Output -NoEnumerate $payload
    }
}

Export-ModuleMember -Function `
    Invoke-CountdownRecordedScpTransfer, `
    Invoke-CountdownRecordedSshRead, `
    Invoke-CountdownExternalCommandProcess, `
    Invoke-CountdownSshBase64CommandRead
