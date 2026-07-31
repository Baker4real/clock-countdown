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

    $existing = [System.IO.File]::ReadAllText($Path, [System.Text.Encoding]::UTF8) |
        ConvertFrom-Json -ErrorAction Stop
    if ($null -eq $existing -or
        [string] $existing.transferAttemptsVersion -ne '1.0' -or
        $null -eq $existing.PSObject.Properties['attempts']) {
        throw "SCP 传输账本 schema 无效：$Path"
    }
    foreach ($attempt in @($existing.attempts)) {
        [void] $attempts.Add($attempt)
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
        [Parameter(Mandatory)] [string] $Destination
    )

    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = 'scp.exe'
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    foreach ($argument in @($Options) + @($SourcePath, $Destination)) {
        [void] $startInfo.ArgumentList.Add($argument)
    }

    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    try {
        if (-not $process.Start()) {
            throw '无法启动 scp.exe。'
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
        [scriptblock] $StopwatchFactory
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
                -Destination $RemoteDestination
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

Export-ModuleMember -Function Invoke-CountdownRecordedScpTransfer
