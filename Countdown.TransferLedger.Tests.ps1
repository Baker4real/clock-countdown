#requires -Version 7.0

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$failures = [System.Collections.Generic.List[string]]::new()
$script:assertions = 0

function Assert-Equal {
    param(
        [AllowNull()] $Expected,
        [AllowNull()] $Actual,
        [Parameter(Mandatory)] [string] $Name
    )

    $script:assertions++
    if ($Expected -ne $Actual) {
        [void] $failures.Add("$Name：期望 [$Expected]，实际 [$Actual]")
    }
}

function New-FakeStopwatch {
    param(
        [Parameter(Mandatory)] [double] $ElapsedSeconds,
        [Parameter(Mandatory)]
        [AllowEmptyCollection()]
        [System.Collections.Generic.List[string]] $Events
    )

    $stopwatch = [pscustomobject]@{
        Elapsed = [TimeSpan]::FromSeconds($ElapsedSeconds)
        Events = $Events
    }
    $stopwatch | Add-Member -MemberType ScriptMethod -Name Start -Value {
        [void] $this.Events.Add('start')
    }
    $stopwatch | Add-Member -MemberType ScriptMethod -Name Stop -Value {
        [void] $this.Events.Add('stop')
    }
    $stopwatch
}

$testRoot = Join-Path ([System.IO.Path]::GetTempPath()) "Countdown-transfer-ledger-$([guid]::NewGuid().ToString('N'))"
$ledgerPath = Join-Path $testRoot 'transfer-attempts.json'
$firstSource = Join-Path $testRoot 'first.bin'
$secondSource = Join-Path $testRoot 'second.bin'
$bindingEvidencePath = Join-Path $testRoot 'behavior-deployment-binding.json'
$readbackEvidencePath = Join-Path $testRoot 'behavior-independent-readback.json'

try {
    Import-Module (Join-Path $PSScriptRoot 'Countdown.TransferLedger.psm1') -Force
    New-Item -ItemType Directory -Path $testRoot -Force | Out-Null
    [System.IO.File]::WriteAllBytes($firstSource, [byte[]]::new(1MB))
    [System.IO.File]::WriteAllBytes($secondSource, [byte[]]::new(2MB))

    $firstEvents = [System.Collections.Generic.List[string]]::new()
    $firstInvoker = {
        param($Options, $SourcePath, $Destination)
        [void] $firstEvents.Add('scp')
        0
    }.GetNewClosure()
    $firstStopwatchFactory = {
        New-FakeStopwatch -ElapsedSeconds 2 -Events $firstEvents
    }.GetNewClosure()
    Invoke-CountdownRecordedScpTransfer `
        -LedgerPath $ledgerPath `
        -Id 'deployment-package' `
        -SourcePath $firstSource `
        -Destination 'replicator@office:D:/deploy/package.zip' `
        -RemotePath 'D:\deploy\package.zip' `
        -ScpOptions @('-i', 'D:\secrets\super-secret-key', '-o', 'StrictHostKeyChecking=yes') `
        -ScpInvoker $firstInvoker `
        -StopwatchFactory $firstStopwatchFactory | Out-Null
    $afterFirstTransfer = [System.IO.File]::ReadAllText(
        $ledgerPath,
        [System.Text.Encoding]::UTF8
    ) | ConvertFrom-Json -ErrorAction Stop
    Assert-Equal 1 @($afterFirstTransfer.attempts).Count '第一次 SCP 返回前已经持久化'

    $secondEvents = [System.Collections.Generic.List[string]]::new()
    $secondInvoker = {
        param($Options, $SourcePath, $Destination)
        [void] $secondEvents.Add('scp')
        0
    }.GetNewClosure()
    $secondStopwatchFactory = {
        New-FakeStopwatch -ElapsedSeconds 4 -Events $secondEvents
    }.GetNewClosure()
    Invoke-CountdownRecordedScpTransfer `
        -LedgerPath $ledgerPath `
        -Id 'remote-install-script' `
        -SourcePath $secondSource `
        -Destination 'replicator@office:D:/deploy/remote-install.ps1' `
        -RemotePath 'D:\deploy\remote-install.ps1' `
        -ScpOptions @('-i', 'D:\secrets\super-secret-key', '-o', 'UserKnownHostsFile=D:\secrets\known-hosts') `
        -ScpInvoker $secondInvoker `
        -StopwatchFactory $secondStopwatchFactory | Out-Null

    $ledgerJson = [System.IO.File]::ReadAllText($ledgerPath, [System.Text.Encoding]::UTF8)
    $ledger = $ledgerJson | ConvertFrom-Json -ErrorAction Stop
    Assert-Equal '1.0' ([string] $ledger.transferAttemptsVersion) '账本 schema 版本'
    Assert-Equal 2 @($ledger.attempts).Count '两次成功 SCP 均立即持久化'
    Assert-Equal 'deployment-package' ([string] $ledger.attempts[0].id) '第一条尝试 ID'
    Assert-Equal 'remote-install-script' ([string] $ledger.attempts[1].id) '第二条尝试 ID'
    Assert-Equal 'succeeded' ([string] $ledger.attempts[0].status) '成功状态'
    Assert-Equal 0 ([int] $ledger.attempts[0].exitCode) '成功退出码'
    Assert-Equal 1048576 ([long] $ledger.attempts[0].metrics.bytes) '成功 bytes 等于本地文件长度'
    Assert-Equal 2.0 ([double] $ledger.attempts[0].metrics.elapsed_seconds) '完整命令墙钟耗时'
    Assert-Equal 0.5 ([double] $ledger.attempts[0].metrics.mib_per_second) 'MiB/s 使用独立算例'
    Assert-Equal 0.5 ([double] $ledger.attempts[0].metrics.MiBPerSecond) 'MiBPerSecond 兼容字段'
    Assert-Equal 4.194304 ([double] $ledger.attempts[0].metrics.mbps) 'Mbps 使用十进制 bit 速率'
    Assert-Equal 'start,scp,stop' ($firstEvents -join ',') 'Stopwatch 完整包裹第一次 SCP'
    Assert-Equal 'start,scp,stop' ($secondEvents -join ',') 'Stopwatch 完整包裹第二次 SCP'
    Assert-Equal $false $ledgerJson.Contains('super-secret-key') '私钥路径不进入账本'
    Assert-Equal $false $ledgerJson.Contains('UserKnownHostsFile') 'SSH 参数不进入账本'

    $failedEvents = [System.Collections.Generic.List[string]]::new()
    $failedInvoker = {
        param($Options, $SourcePath, $Destination)
        [void] $failedEvents.Add('scp')
        23
    }.GetNewClosure()
    $failedStopwatchFactory = {
        New-FakeStopwatch -ElapsedSeconds 3 -Events $failedEvents
    }.GetNewClosure()
    $failedProbe = { 0L }
    $failedMessage = $null
    try {
        Invoke-CountdownRecordedScpTransfer `
            -LedgerPath $ledgerPath `
            -Id 'package-failed-before-transfer' `
            -SourcePath $firstSource `
            -Destination 'replicator@office:D:/deploy/package-failed.zip' `
            -RemotePath 'D:\deploy\package-failed.zip' `
            -ScpOptions @('-o', 'BatchMode=yes') `
            -ScpInvoker $failedInvoker `
            -RemoteBytesProbe $failedProbe `
            -StopwatchFactory $failedStopwatchFactory | Out-Null
    }
    catch {
        $failedMessage = $_.Exception.Message
    }

    $ledger = [System.IO.File]::ReadAllText($ledgerPath, [System.Text.Encoding]::UTF8) |
        ConvertFrom-Json -ErrorAction Stop
    Assert-Equal $true ($failedMessage -match '退出码：23') '非零 SCP 仍向部署调用方报告失败'
    Assert-Equal 3 @($ledger.attempts).Count '非零 SCP 在抛错前持久化'
    Assert-Equal 'start,scp,stop' ($failedEvents -join ',') '失败命令仍被完整计时'
    if (@($ledger.attempts).Count -ge 3) {
        $failedAttempt = $ledger.attempts[2]
        Assert-Equal 'failed-before-transfer' ([string] $failedAttempt.status) '未发现部分文件时的失败状态'
        Assert-Equal 23 ([int] $failedAttempt.exitCode) '失败退出码进入账本'
        Assert-Equal $false ([bool] $failedAttempt.transferStarted) '失败前未开始传输'
        Assert-Equal 0 ([long] $failedAttempt.metrics.bytes) '失败前 bytes 为零'
        Assert-Equal 3.0 ([double] $failedAttempt.metrics.elapsed_seconds) '失败仍记录完整命令耗时'
        Assert-Equal 0.0 ([double] $failedAttempt.metrics.mib_per_second) '失败前 MiB/s 为零'
        Assert-Equal 0.0 ([double] $failedAttempt.metrics.mbps) '失败前 Mbps 为零'
    }

    $partialEvents = [System.Collections.Generic.List[string]]::new()
    $partialInvoker = {
        param($Options, $SourcePath, $Destination)
        [void] $partialEvents.Add('scp')
        1
    }.GetNewClosure()
    $partialProbe = {
        param($RemotePath)
        [void] $partialEvents.Add('probe')
        if ($RemotePath -ne 'D:\deploy\partial-package.zip') {
            throw "probe 收到错误远端路径：$RemotePath"
        }
        524288L
    }.GetNewClosure()
    $partialStopwatchFactory = {
        New-FakeStopwatch -ElapsedSeconds 2 -Events $partialEvents
    }.GetNewClosure()
    $partialMessage = $null
    try {
        Invoke-CountdownRecordedScpTransfer `
            -LedgerPath $ledgerPath `
            -Id 'package-partial-failure' `
            -SourcePath $firstSource `
            -Destination 'replicator@office:D:/deploy/partial-package.zip' `
            -RemotePath 'D:\deploy\partial-package.zip' `
            -ScpOptions @('-o', 'StrictHostKeyChecking=yes') `
            -ScpInvoker $partialInvoker `
            -RemoteBytesProbe $partialProbe `
            -StopwatchFactory $partialStopwatchFactory | Out-Null
    }
    catch {
        $partialMessage = $_.Exception.Message
    }

    $ledger = [System.IO.File]::ReadAllText($ledgerPath, [System.Text.Encoding]::UTF8) |
        ConvertFrom-Json -ErrorAction Stop
    Assert-Equal $true ($partialMessage -match '退出码：1') '部分传输失败仍向部署调用方报告失败'
    Assert-Equal 4 @($ledger.attempts).Count '部分传输失败在抛错前持久化'
    Assert-Equal 'start,scp,stop,probe' ($partialEvents -join ',') '远端 probe 不混入 SCP 命令耗时'
    if (@($ledger.attempts).Count -ge 4) {
        $partialAttempt = $ledger.attempts[3]
        Assert-Equal 'failed' ([string] $partialAttempt.status) '有部分字节时保持失败状态'
        Assert-Equal 1 ([int] $partialAttempt.exitCode) '部分传输失败退出码'
        Assert-Equal $true ([bool] $partialAttempt.transferStarted) '部分字节证明传输已开始'
        Assert-Equal 524288 ([long] $partialAttempt.metrics.bytes) '严格 SSH probe 的部分 bytes'
        Assert-Equal 2.0 ([double] $partialAttempt.metrics.elapsed_seconds) '部分失败记录完整 SCP 耗时'
        Assert-Equal 0.25 ([double] $partialAttempt.metrics.mib_per_second) '部分失败 MiB/s'
        Assert-Equal 2.097152 ([double] $partialAttempt.metrics.mbps) '部分失败 Mbps'
    }

    $startFailureEvents = [System.Collections.Generic.List[string]]::new()
    $startFailureInvoker = {
        param($Options, $SourcePath, $Destination)
        [void] $startFailureEvents.Add('scp')
        throw '模拟 scp.exe 无法启动。'
    }.GetNewClosure()
    $startFailureStopwatchFactory = {
        New-FakeStopwatch -ElapsedSeconds 0.5 -Events $startFailureEvents
    }.GetNewClosure()
    $startFailureProbe = { 0L }
    $startFailureMessage = $null
    try {
        Invoke-CountdownRecordedScpTransfer `
            -LedgerPath $ledgerPath `
            -Id 'scp-process-start-failure' `
            -SourcePath $firstSource `
            -Destination 'replicator@office:D:/deploy/not-started.zip' `
            -RemotePath 'D:\deploy\not-started.zip' `
            -ScpInvoker $startFailureInvoker `
            -RemoteBytesProbe $startFailureProbe `
            -StopwatchFactory $startFailureStopwatchFactory | Out-Null
    }
    catch {
        $startFailureMessage = $_.Exception.Message
    }

    $ledger = [System.IO.File]::ReadAllText($ledgerPath, [System.Text.Encoding]::UTF8) |
        ConvertFrom-Json -ErrorAction Stop
    Assert-Equal $true ($startFailureMessage -match '模拟 scp.exe 无法启动') '进程启动异常保留原始原因'
    Assert-Equal 5 @($ledger.attempts).Count '进程启动异常在抛错前持久化'
    Assert-Equal 'start,scp,stop' ($startFailureEvents -join ',') '进程启动异常仍停止 Stopwatch'
    if (@($ledger.attempts).Count -ge 5) {
        $startFailureAttempt = $ledger.attempts[4]
        Assert-Equal 'failed-before-transfer' ([string] $startFailureAttempt.status) '进程启动异常状态'
        Assert-Equal -1 ([int] $startFailureAttempt.exitCode) '进程启动异常退出码'
        Assert-Equal 0 ([long] $startFailureAttempt.metrics.bytes) '进程启动异常 bytes'
        Assert-Equal 0.0 ([double] $startFailureAttempt.metrics.mib_per_second) '进程启动异常 MiB/s'
        Assert-Equal 0.0 ([double] $startFailureAttempt.metrics.mbps) '进程启动异常 Mbps'
    }

    $unknownInvoker = { 1 }
    $unknownProbe = { throw '模拟远端字节 probe 失败。' }
    $unknownStopwatchFactory = {
        New-FakeStopwatch `
            -ElapsedSeconds 1 `
            -Events ([System.Collections.Generic.List[string]]::new())
    }
    $unknownMessage = $null
    try {
        Invoke-CountdownRecordedScpTransfer `
            -LedgerPath $ledgerPath `
            -Id 'scp-failed-bytes-unverified' `
            -SourcePath $firstSource `
            -Destination 'replicator@office:D:/deploy/unknown.bin' `
            -RemotePath 'D:\deploy\unknown.bin' `
            -ScpInvoker $unknownInvoker `
            -RemoteBytesProbe $unknownProbe `
            -StopwatchFactory $unknownStopwatchFactory | Out-Null
    }
    catch {
        $unknownMessage = $_.Exception.Message
    }

    $ledger = [System.IO.File]::ReadAllText($ledgerPath, [System.Text.Encoding]::UTF8) |
        ConvertFrom-Json -ErrorAction Stop
    Assert-Equal $true ($unknownMessage -match '退出码：1') '远端字节未知仍向部署调用方报告失败'
    Assert-Equal 6 @($ledger.attempts).Count '远端 probe 失败的尝试先持久化'
    if (@($ledger.attempts).Count -ge 6) {
        $unknownAttempt = $ledger.attempts[5]
        Assert-Equal 'failed-bytes-unverified' ([string] $unknownAttempt.status) '未知字节不冒充传输前失败'
        Assert-Equal $null $unknownAttempt.transferStarted '未知字节不伪造传输是否开始'
        Assert-Equal $false ([bool] $unknownAttempt.bytesVerified) '未知字节明确标记为未验证'
        Assert-Equal 0 ([long] $unknownAttempt.metrics.bytes) '未知字节保留数值字段但不声称已验证'
    }

    $exceptionUnknownInvoker = { throw '模拟 SCP 启动后调用异常。' }
    $exceptionUnknownProbe = { throw '模拟异常后的远端字节 probe 失败。' }
    $exceptionUnknownStopwatchFactory = {
        New-FakeStopwatch `
            -ElapsedSeconds 1.5 `
            -Events ([System.Collections.Generic.List[string]]::new())
    }
    $exceptionUnknownMessage = $null
    try {
        Invoke-CountdownRecordedScpTransfer `
            -LedgerPath $ledgerPath `
            -Id 'scp-exception-bytes-unverified' `
            -SourcePath $firstSource `
            -Destination 'replicator@office:D:/deploy/exception-unknown.bin' `
            -RemotePath 'D:\deploy\exception-unknown.bin' `
            -ScpInvoker $exceptionUnknownInvoker `
            -RemoteBytesProbe $exceptionUnknownProbe `
            -StopwatchFactory $exceptionUnknownStopwatchFactory | Out-Null
    }
    catch {
        $exceptionUnknownMessage = $_.Exception.Message
    }

    $ledger = [System.IO.File]::ReadAllText($ledgerPath, [System.Text.Encoding]::UTF8) |
        ConvertFrom-Json -ErrorAction Stop
    Assert-Equal $true ($exceptionUnknownMessage -match '模拟 SCP 启动后调用异常') '调用异常保留原始原因'
    Assert-Equal 7 @($ledger.attempts).Count '调用异常与 probe 失败的尝试先持久化'
    if (@($ledger.attempts).Count -ge 7) {
        $exceptionUnknownAttempt = $ledger.attempts[6]
        Assert-Equal 'failed-bytes-unverified' ([string] $exceptionUnknownAttempt.status) '调用异常不能冒充传输前失败'
        Assert-Equal $null $exceptionUnknownAttempt.transferStarted '调用异常且 probe 失败时不伪造传输是否开始'
        Assert-Equal $false ([bool] $exceptionUnknownAttempt.bytesVerified) '调用异常且 probe 失败时明确标记字节未验证'
        Assert-Equal 0 ([long] $exceptionUnknownAttempt.metrics.bytes) '调用异常且 probe 失败时保留零值数值字段'
    }

    $sshReadEvents = [System.Collections.Generic.List[string]]::new()
    $sshReadReader = {
        [void] $sshReadEvents.Add('ssh')
        [pscustomobject]@{
            Success = $true
            ExitCode = 0
            Bytes = [byte[]]::new(1024)
            FailureMessage = $null
        }
    }.GetNewClosure()
    $sshReadStopwatchFactory = {
        New-FakeStopwatch -ElapsedSeconds 2 -Events $sshReadEvents
    }.GetNewClosure()
    [byte[]] $sshReadBytes = Invoke-CountdownRecordedSshRead `
        -LedgerPath $ledgerPath `
        -Id 'post-deployment-independent-readback' `
        -Reader $sshReadReader `
        -StopwatchFactory $sshReadStopwatchFactory
    $ledger = [System.IO.File]::ReadAllText($ledgerPath, [System.Text.Encoding]::UTF8) |
        ConvertFrom-Json -ErrorAction Stop
    Assert-Equal 1024 $sshReadBytes.Length '成功 SSH 回读返回精确 byte[]'
    Assert-Equal 8 @($ledger.attempts).Count '成功 SSH 回读在返回前持久化'
    Assert-Equal 'start,ssh,stop' ($sshReadEvents -join ',') 'Stopwatch 完整包裹 SSH 回读'
    if (@($ledger.attempts).Count -ge 8) {
        $sshReadAttempt = $ledger.attempts[7]
        Assert-Equal 'post-deployment-independent-readback' ([string] $sshReadAttempt.id) '成功独立回读使用稳定账本 ID'
        Assert-Equal 'ssh' ([string] $sshReadAttempt.method) '回执读取使用 SSH 账本类型'
        Assert-Equal 'succeeded' ([string] $sshReadAttempt.status) '成功 SSH 回读状态'
        Assert-Equal 0 ([int] $sshReadAttempt.exitCode) '成功 SSH 回读退出码'
        Assert-Equal $true ([bool] $sshReadAttempt.transferStarted) '成功 SSH 回读记录传输开始'
        Assert-Equal $true ([bool] $sshReadAttempt.bytesVerified) '成功 SSH 回读记录字节已验证'
        Assert-Equal 1024 ([long] $sshReadAttempt.metrics.bytes) '成功 SSH 回读记录 payload bytes'
        Assert-Equal 2.0 ([double] $sshReadAttempt.metrics.elapsed_seconds) '成功 SSH 回读记录完整命令耗时'
        Assert-Equal 0.000488 ([double] $sshReadAttempt.metrics.mib_per_second) '成功 SSH 回读记录 MiB/s'
        Assert-Equal 0.004096 ([double] $sshReadAttempt.metrics.mbps) '成功 SSH 回读记录 Mbps'
    }

    $sshFailureReader = {
        [pscustomobject]@{
            Success = $false
            ExitCode = 255
            Bytes = $null
            FailureMessage = '模拟 SSH 回读失败。'
        }
    }
    $sshFailureStopwatchFactory = {
        New-FakeStopwatch `
            -ElapsedSeconds 1 `
            -Events ([System.Collections.Generic.List[string]]::new())
    }
    $sshFailureMessage = $null
    try {
        Invoke-CountdownRecordedSshRead `
            -LedgerPath $ledgerPath `
            -Id 'post-deployment-independent-readback' `
            -Reader $sshFailureReader `
            -StopwatchFactory $sshFailureStopwatchFactory | Out-Null
    }
    catch {
        $sshFailureMessage = $_.Exception.Message
    }
    $ledger = [System.IO.File]::ReadAllText($ledgerPath, [System.Text.Encoding]::UTF8) |
        ConvertFrom-Json -ErrorAction Stop
    Assert-Equal $true ($sshFailureMessage -match '退出码：255.*模拟 SSH 回读失败') '失败 SSH 回读保留退出码和原因'
    Assert-Equal 9 @($ledger.attempts).Count '失败 SSH 回读在抛错前持久化'
    if (@($ledger.attempts).Count -ge 9) {
        $sshFailureAttempt = $ledger.attempts[8]
        Assert-Equal 'post-deployment-independent-readback' ([string] $sshFailureAttempt.id) '失败独立回读保留同一稳定账本 ID'
        Assert-Equal 'failed-bytes-unverified' ([string] $sshFailureAttempt.status) '失败 SSH 回读不伪造传输状态'
        Assert-Equal 255 ([int] $sshFailureAttempt.exitCode) '失败 SSH 回读退出码进入账本'
        Assert-Equal $null $sshFailureAttempt.transferStarted '失败 SSH 回读不猜测传输是否开始'
        Assert-Equal $false ([bool] $sshFailureAttempt.bytesVerified) '失败 SSH 回读明确字节未验证'
        Assert-Equal 0 ([long] $sshFailureAttempt.metrics.bytes) '失败 SSH 回读保留零 bytes'
        Assert-Equal 0.0 ([double] $sshFailureAttempt.metrics.mib_per_second) '失败 SSH 回读保留零 MiB/s'
        Assert-Equal 0.0 ([double] $sshFailureAttempt.metrics.mbps) '失败 SSH 回读保留零 Mbps'
    }

    $invalidReadbackBytes = [System.Text.UTF8Encoding]::new($false).GetBytes('{"Valid":false}')
    $sshContractFailureReader = {
        [pscustomobject]@{
            Success = $false
            ExitCode = 0
            Bytes = [byte[]] $invalidReadbackBytes
            FailureMessage = '模拟独立回读 schema 失败。'
        }
    }.GetNewClosure()
    $sshContractFailureStopwatchFactory = {
        New-FakeStopwatch `
            -ElapsedSeconds 0.5 `
            -Events ([System.Collections.Generic.List[string]]::new())
    }
    $sshContractFailureMessage = $null
    try {
        Invoke-CountdownRecordedSshRead `
            -LedgerPath $ledgerPath `
            -Id 'post-deployment-independent-readback' `
            -Reader $sshContractFailureReader `
            -StopwatchFactory $sshContractFailureStopwatchFactory | Out-Null
    }
    catch {
        $sshContractFailureMessage = $_.Exception.Message
    }
    $ledger = [System.IO.File]::ReadAllText($ledgerPath, [System.Text.Encoding]::UTF8) |
        ConvertFrom-Json -ErrorAction Stop
    Assert-Equal $true ($sshContractFailureMessage -match '退出码：0.*schema 失败') '独立回读 schema 失败保留验证原因'
    Assert-Equal 10 @($ledger.attempts).Count '独立回读 schema 失败在抛错前持久化'
    if (@($ledger.attempts).Count -ge 10) {
        $sshContractFailureAttempt = $ledger.attempts[9]
        Assert-Equal 'post-deployment-independent-readback' ([string] $sshContractFailureAttempt.id) 'schema 失败使用独立回读稳定账本 ID'
        Assert-Equal 'failed' ([string] $sshContractFailureAttempt.status) 'schema 失败且 payload 已返回时记录失败而不是字节未知'
        Assert-Equal 0 ([int] $sshContractFailureAttempt.exitCode) 'schema 失败保留 SSH 成功退出码'
        Assert-Equal $true ([bool] $sshContractFailureAttempt.transferStarted) 'schema 失败保留已发生的数据传输'
        Assert-Equal $true ([bool] $sshContractFailureAttempt.bytesVerified) 'schema 失败保留可验证 payload 字节数'
        Assert-Equal $invalidReadbackBytes.Length ([long] $sshContractFailureAttempt.metrics.bytes) 'schema 失败记录实际 payload bytes'
        Assert-Equal ([Math]::Round((($invalidReadbackBytes.Length / 1MB) / 0.5), 6)) ([double] $sshContractFailureAttempt.metrics.mib_per_second) 'schema 失败记录实际 MiB/s'
        Assert-Equal ([Math]::Round((($invalidReadbackBytes.Length * 8.0 / 1000000.0) / 0.5), 6)) ([double] $sshContractFailureAttempt.metrics.mbps) 'schema 失败记录实际 Mbps'
    }

    $strictOptions = @(
        '-i', 'D:\Codex\.codex\credentials\ssh\replicator\office\id_ed25519',
        '-o', 'BatchMode=yes',
        '-o', 'IdentitiesOnly=yes',
        '-o', 'ConnectTimeout=10',
        '-o', 'ConnectionAttempts=1',
        '-o', 'StrictHostKeyChecking=yes',
        '-o', 'UserKnownHostsFile=D:\Codex\.codex\credentials\ssh\replicator\office\known_hosts',
        '-o', 'KexAlgorithms=curve25519-sha256')
    $commandCapture = [pscustomobject]@{ FileName = $null; Arguments = $null }
    $commandPayload = [System.Text.UTF8Encoding]::new($false).GetBytes('{"binding":"captured"}')
    $commandRunner = {
        param($FileName, $Arguments)
        $commandCapture.FileName = $FileName
        $commandCapture.Arguments = [string[]] $Arguments
        [pscustomobject]@{
            ExitCode = 0
            StandardOutput = [Convert]::ToBase64String($commandPayload)
            StandardError = ''
            ProcessId = $PID + 1
        }
    }.GetNewClosure()
    $commandResult = Invoke-CountdownSshBase64CommandRead `
        -Options $strictOptions `
        -Destination 'replicator@office-host' `
        -RemoteCommand 'pwsh.exe -NoLogo -NoProfile -EncodedCommand fixture' `
        -MaximumDecodedBytes 4096 `
        -Description '严格 SSH 行为测试' `
        -CommandRunner $commandRunner
    Assert-Equal $true ([bool] $commandResult.Success) '严格 SSH command seam 接受受管参数并返回 payload'
    Assert-Equal 'ssh.exe' ([string] $commandCapture.FileName) '严格 SSH command seam 只启动 ssh.exe'
    Assert-Equal (($strictOptions + @('replicator@office-host', 'pwsh.exe -NoLogo -NoProfile -EncodedCommand fixture')) -join "`n") (@($commandCapture.Arguments) -join "`n") '严格 SSH command seam 保留完整且有序的参数边界'
    Assert-Equal ([Convert]::ToHexString($commandPayload)) ([Convert]::ToHexString([byte[]] $commandResult.Bytes)) '严格 SSH command seam 返回解码后的精确 bytes'

    $pwshPath = (Get-Command pwsh.exe -CommandType Application -ErrorAction Stop | Select-Object -First 1 -ExpandProperty Source)
    $childProcess = Invoke-CountdownExternalCommandProcess `
        -FileName $pwshPath `
        -Arguments @('-NoLogo', '-NoProfile', '-NonInteractive', '-Command', '[Console]::Out.Write([Environment]::ProcessId)')
    Assert-Equal 0 ([int] $childProcess.ExitCode) '受管 command runner 启动真实新进程成功'
    Assert-Equal $true ([int] $childProcess.ProcessId -ne $PID) '受管 command runner 没有在当前测试进程内执行命令'
    Assert-Equal ([int] $childProcess.ProcessId) ([int] ([string] $childProcess.StandardOutput).Trim()) '受管 command runner 捕获新进程的精确 stdout'

    Import-Module (Join-Path $PSScriptRoot 'Countdown.RemoteDeployment.psm1') -Force
    $expectedBindingHash = 'A' * 64
    $mismatchedBindingJson = [pscustomobject][ordered]@{
        SchemaVersion = 1
        DeploymentBindingSha256 = 'B' * 64
        TokenSha256 = 'C' * 64
        RuntimeProcessId = 4242
    } | ConvertTo-Json -Compress
    $mismatchedBindingBytes = [System.Text.UTF8Encoding]::new($false).GetBytes($mismatchedBindingJson)
    $mismatchRunner = {
        param($FileName, $Arguments)
        [pscustomobject]@{
            ExitCode = 0
            StandardOutput = [Convert]::ToBase64String($mismatchedBindingBytes)
            StandardError = ''
            ProcessId = $PID + 1
        }
    }.GetNewClosure()
    $mismatchReader = {
        $read = Invoke-CountdownSshBase64CommandRead `
            -Options $strictOptions `
            -Destination 'replicator@office-host' `
            -RemoteCommand 'pwsh.exe -NoLogo -NoProfile -EncodedCommand binding-fixture' `
            -MaximumDecodedBytes 4096 `
            -Description '部署 binding 独立回读' `
            -CommandRunner $mismatchRunner
        if ($read.Success) {
            try {
                ConvertFrom-CountdownRemoteDeploymentBindingJson `
                    -Json ([System.Text.UTF8Encoding]::new($false, $true).GetString([byte[]] $read.Bytes)) `
                    -ExpectedDeploymentBindingSha256 $expectedBindingHash `
                    -ExpectedProcessId 4242 | Out-Null
            }
            catch {
                $read.Success = $false
                $read.FailureMessage = "nonce/token mismatch：$($_.Exception.Message)"
            }
        }
        $read
    }.GetNewClosure()
    $mismatchMessage = $null
    try {
        Invoke-CountdownRecordedSshRead `
            -LedgerPath $ledgerPath `
            -Id 'remote-deployment-binding' `
            -Reader $mismatchReader `
            -StopwatchFactory {
                New-FakeStopwatch -ElapsedSeconds 0.25 -Events ([System.Collections.Generic.List[string]]::new())
            } | Out-Null
    }
    catch { $mismatchMessage = $_.Exception.Message }
    $ledger = [System.IO.File]::ReadAllText($ledgerPath, [System.Text.Encoding]::UTF8) | ConvertFrom-Json
    $bindingMismatchAttempt = @($ledger.attempts)[-1]
    Assert-Equal $true ($mismatchMessage -match 'nonce/token mismatch.*本次本地部署 nonce 不一致') 'nonce mismatch 通过生产 SSH command/contract/ledger 调用链报告失败'
    Assert-Equal 'remote-deployment-binding' ([string] $bindingMismatchAttempt.id) 'nonce mismatch 使用 binding 稳定账本 ID'
    Assert-Equal 'failed' ([string] $bindingMismatchAttempt.status) 'nonce mismatch 已返回 payload 时作为失败 attempt 入账'
    Assert-Equal $mismatchedBindingBytes.Length ([long] $bindingMismatchAttempt.metrics.bytes) 'nonce mismatch 失败 attempt 记录实际输出 bytes'

    $expectedReadbackExecutableHash = 'D' * 64
    $expectedReadbackSettingsHash = 'E' * 64
    $readbackWithUnexpectedToken = [pscustomobject][ordered]@{
        SchemaVersion = 1; Valid = $true
        HostIdentityMatch = $true; UserIdentityMatch = $true
        RemoteMachineNameSha256 = '1' * 64; RemoteUserNameSha256 = '2' * 64
        TaskState = 'Running'; TaskExecuteMatch = $true; TaskArgumentsMatch = $true
        TaskWorkingDirectoryMatch = $true; StartupSchemaVersion = 1
        StartupAcceptanceState = 'finalized'; RuntimeSchemaVersion = 4
        RuntimeStage = 'window-ready'; AcceptanceSchemaVersion = 1
        AcceptanceStage = 'finalized-ready'; TokenSha256 = 'F' * 64; TokenMatch = $true
        RuntimeProcessId = 4242; AcceptanceProcessId = 4242; ProcessIdMatch = $true
        RuntimeSessionId = 7; AcceptanceSessionId = 7; ProcessSessionId = 7
        SessionIdMatch = $true; ApplicationPathMatch = $true
        ProcessExecutablePathMatch = $true; ExecutableSha256 = $expectedReadbackExecutableHash
        ExecutableHashMatch = $true; SettingsSha256 = $expectedReadbackSettingsHash
        SettingsHashMatch = $true
    } | ConvertTo-Json -Compress
    $unexpectedTokenBytes = [System.Text.UTF8Encoding]::new($false).GetBytes($readbackWithUnexpectedToken)
    $unexpectedTokenRunner = {
        param($FileName, $Arguments)
        [pscustomobject]@{
            ExitCode = 0
            StandardOutput = [Convert]::ToBase64String($unexpectedTokenBytes)
            StandardError = ''
            ProcessId = $PID + 1
        }
    }.GetNewClosure()
    $unexpectedTokenReader = {
        $read = Invoke-CountdownSshBase64CommandRead `
            -Options $strictOptions `
            -Destination 'replicator@office-host' `
            -RemoteCommand 'pwsh.exe -NoLogo -NoProfile -EncodedCommand readback-fixture' `
            -MaximumDecodedBytes 32768 `
            -Description '部署后独立回读' `
            -CommandRunner $unexpectedTokenRunner
        if ($read.Success) {
            try {
                ConvertFrom-CountdownRemoteDeploymentReadbackJson `
                    -Json ([System.Text.UTF8Encoding]::new($false, $true).GetString([byte[]] $read.Bytes)) `
                    -ExpectedProcessId 4242 `
                    -ExpectedExecutableSha256 $expectedReadbackExecutableHash `
                    -ExpectedTokenSha256 ('0' * 64) `
                    -ExpectedSettingsSha256 $expectedReadbackSettingsHash | Out-Null
            }
            catch {
                $read.Success = $false
                $read.FailureMessage = "nonce/token mismatch：$($_.Exception.Message)"
            }
        }
        $read
    }.GetNewClosure()
    $unexpectedTokenMessage = $null
    try {
        Invoke-CountdownRecordedSshRead `
            -LedgerPath $ledgerPath `
            -Id 'post-deployment-independent-readback' `
            -Reader $unexpectedTokenReader `
            -StopwatchFactory {
                New-FakeStopwatch -ElapsedSeconds 0.5 -Events ([System.Collections.Generic.List[string]]::new())
            } | Out-Null
    }
    catch { $unexpectedTokenMessage = $_.Exception.Message }
    $ledger = [System.IO.File]::ReadAllText($ledgerPath, [System.Text.Encoding]::UTF8) | ConvertFrom-Json
    $tokenMismatchAttempt = @($ledger.attempts)[-1]
    Assert-Equal $true ($unexpectedTokenMessage -match 'nonce/token mismatch.*token 哈希与本次部署 binding 不一致') 'token mismatch 通过生产 SSH command/contract/ledger 调用链报告失败'
    Assert-Equal 'post-deployment-independent-readback' ([string] $tokenMismatchAttempt.id) 'token mismatch 使用独立回读稳定账本 ID'
    Assert-Equal 'failed' ([string] $tokenMismatchAttempt.status) 'token mismatch 已返回 payload 时作为失败 attempt 入账'
    Assert-Equal $unexpectedTokenBytes.Length ([long] $tokenMismatchAttempt.metrics.bytes) 'token mismatch 失败 attempt 记录实际输出 bytes'

    . (Join-Path $PSScriptRoot 'Deploy-OfficeCountdown.ps1') -LoadFunctionsOnly

    $isoTimestamp = '2026-08-04T11:33:21.1234567+00:00'
    $flatReadback = ConvertFrom-CountdownReadbackFlatJsonObject `
        -Json ('{"readyAtUtc":"' + $isoTimestamp + '","stage":"window-ready","processId":17388,"valid":true,"optional":null}') `
        -SourceName 'readback fixture'
    Assert-Equal 'System.String' $flatReadback.readyAtUtc.GetType().FullName 'Readback flat JSON converter preserves ISO timestamps as strings'
    Assert-Equal $isoTimestamp ([string] $flatReadback.readyAtUtc) 'Readback flat JSON converter preserves ISO timestamp text exactly'
    Assert-Equal 'System.Int64' $flatReadback.processId.GetType().FullName 'Readback flat JSON converter preserves integer tokens'
    Assert-Equal 'System.Boolean' $flatReadback.valid.GetType().FullName 'Readback flat JSON converter preserves bool tokens'
    Assert-Equal $null $flatReadback.optional 'Readback flat JSON converter preserves null tokens'

    foreach ($invalidFlatJson in @(
            '{"readyAtUtc":1.5}',
            '{"readyAtUtc":{}}',
            '{"readyAtUtc":[]}',
            '{"readyAtUtc":"first","readyAtUtc":"second"}',
            '{"readyAtUtc":"value",}',
            '{/*comment*/"readyAtUtc":"value"}')) {
        $invalidFlatJsonRejected = $false
        try {
            ConvertFrom-CountdownReadbackFlatJsonObject -Json $invalidFlatJson -SourceName 'invalid readback fixture' | Out-Null
        }
        catch {
            $invalidFlatJsonRejected = $true
        }
        Assert-Equal $true $invalidFlatJsonRejected "Readback flat JSON converter rejects invalid token shape: $invalidFlatJson"
    }
    $behaviorBindingHash = '1' * 64
    $behaviorTokenHash = '2' * 64
    $behaviorExecutableHash = '3' * 64
    $behaviorSettingsHash = '4' * 64
    $behaviorBindingJson = [pscustomobject][ordered]@{
        SchemaVersion = 1
        DeploymentBindingSha256 = $behaviorBindingHash
        TokenSha256 = $behaviorTokenHash
        RuntimeProcessId = 4242
    } | ConvertTo-Json -Compress
    $behaviorBindingBytes = [System.Text.UTF8Encoding]::new($false).GetBytes($behaviorBindingJson)
    $behaviorReadback = [pscustomobject][ordered]@{
        SchemaVersion = 1; Valid = $true
        HostIdentityMatch = $true; UserIdentityMatch = $true
        RemoteMachineNameSha256 = '5' * 64; RemoteUserNameSha256 = '6' * 64
        TaskState = 'Running'; TaskExecuteMatch = $true; TaskArgumentsMatch = $true
        TaskWorkingDirectoryMatch = $true; StartupSchemaVersion = 1
        StartupAcceptanceState = 'finalized'; RuntimeSchemaVersion = 4
        RuntimeStage = 'window-ready'; AcceptanceSchemaVersion = 1
        AcceptanceStage = 'finalized-ready'; TokenSha256 = $behaviorTokenHash; TokenMatch = $true
        RuntimeProcessId = 4242; AcceptanceProcessId = 4242; ProcessIdMatch = $true
        RuntimeSessionId = 7; AcceptanceSessionId = 7; ProcessSessionId = 7
        SessionIdMatch = $true; ApplicationPathMatch = $true
        ProcessExecutablePathMatch = $true; ExecutableSha256 = $behaviorExecutableHash
        ExecutableHashMatch = $true; SettingsSha256 = $behaviorSettingsHash
        SettingsHashMatch = $true; UnexpectedField = 'schema-drift'
    }
    $behaviorReadbackJson = $behaviorReadback | ConvertTo-Json -Compress
    $behaviorReadbackBytes = [System.Text.UTF8Encoding]::new($false).GetBytes($behaviorReadbackJson)
    $behaviorRunnerState = [pscustomobject]@{
        Calls = 0
        RemoteCommands = [System.Collections.Generic.List[string]]::new()
    }
    $behaviorRunner = {
        param($FileName, $Arguments)
        $behaviorRunnerState.Calls++
        $behaviorRunnerState.RemoteCommands.Add([string] $Arguments[-1])
        $payload = if ($behaviorRunnerState.Calls -eq 1) {
            $behaviorBindingBytes
        }
        else {
            $behaviorReadbackBytes
        }
        [pscustomobject]@{
            ExitCode = 0
            StandardOutput = [Convert]::ToBase64String([byte[]] $payload)
            StandardError = ''
            ProcessId = $PID + 1
        }
    }.GetNewClosure()
    $behaviorStopwatchFactory = {
        New-FakeStopwatch `
            -ElapsedSeconds 0.5 `
            -Events ([System.Collections.Generic.List[string]]::new())
    }
    $rollbackState = [pscustomobject]@{ Calls = 0 }
    $rollbackSpy = { $rollbackState.Calls++ }.GetNewClosure()
    $script:sshOptions = [string[]] $strictOptions
    $env:REPLICATOR_OFFICE_SSH_DESTINATION = 'replicator@office-host'
    $behaviorMessage = $null
    try {
        Invoke-OfficePostDeploymentIndependentVerification `
            -LedgerPath $ledgerPath `
            -DeploymentBindingEvidencePath $bindingEvidencePath `
            -IndependentReadbackEvidencePath $readbackEvidencePath `
            -RemoteDeploymentBindingPath 'D:\Codex\deploy\fixture\deployment-binding.json' `
            -DeploymentBindingSha256 $behaviorBindingHash `
            -SuccessReceipt ([pscustomobject]@{ RuntimeProcessId = 4242; SettingsHashAfter = $behaviorSettingsHash }) `
            -ExpectedExecutableSha256 $behaviorExecutableHash `
            -ExpectedRemoteHost 'office-host' `
            -ExpectedRemoteUser 'replicator' `
            -CommandRunner $behaviorRunner `
            -StopwatchFactory $behaviorStopwatchFactory `
            -RollbackAction $rollbackSpy | Out-Null
    }
    catch {
        $behaviorMessage = $_.Exception.Message
    }
    $behaviorLedger = [System.IO.File]::ReadAllText($ledgerPath, [System.Text.Encoding]::UTF8) | ConvertFrom-Json
    $behaviorAttempts = @($behaviorLedger.attempts)[-2..-1]
    Assert-Equal 2 ([int] $behaviorRunnerState.Calls) '提交后验证直接调用 binding 与独立回读两个生产 reader'
    Assert-Equal 'succeeded' ([string] $behaviorAttempts[0].status) '合法 binding 先通过生产 parser 与 ledger'
    Assert-Equal $behaviorBindingBytes.Length ([long] $behaviorAttempts[0].metrics.bytes) 'binding attempt 记录实际 UTF-8 payload bytes'
    Assert-Equal 'post-deployment-independent-readback' ([string] $behaviorAttempts[1].id) '字段漂移使用独立回读稳定账本 ID'
    Assert-Equal 'failed' ([string] $behaviorAttempts[1].status) '字段漂移在生产 parser 失败后记录 failed attempt'
    Assert-Equal $true ([bool] $behaviorAttempts[1].bytesVerified) '字段漂移保留已验证 payload bytes 状态'
    Assert-Equal $behaviorReadbackBytes.Length ([long] $behaviorAttempts[1].metrics.bytes) '字段漂移记录实际 UTF-8 payload bytes'
    Assert-Equal $true ($behaviorMessage -match '部署已提交但独立验证不完整，未回滚.*额外字段.*UnexpectedField') '提交后字段漂移返回验证不完整且未回滚的最终错误'
    Assert-Equal 0 ([int] $rollbackState.Calls) '提交后独立验证失败不会调用 rollback'
    Assert-Equal $true (Test-Path -LiteralPath $bindingEvidencePath -PathType Leaf) '提交后链路持久化已验证 binding evidence'
    Assert-Equal $false (Test-Path -LiteralPath $readbackEvidencePath) '失败独立回读不会发布成功 evidence'

    $readbackRemoteCommand = [string] $behaviorRunnerState.RemoteCommands[1]
    $encodedCommandPrefix = '-EncodedCommand '
    $encodedCommandIndex = $readbackRemoteCommand.LastIndexOf($encodedCommandPrefix, [System.StringComparison]::Ordinal)
    $bootstrapBase64 = $readbackRemoteCommand.Substring($encodedCommandIndex + $encodedCommandPrefix.Length)
    $bootstrapText = [System.Text.Encoding]::Unicode.GetString([Convert]::FromBase64String($bootstrapBase64))
    $compressedMatch = [regex]::Match(
        $bootstrapText,
        '(?m)^\$compressed = \[Convert\]::FromBase64String\(''(?<payload>[A-Za-z0-9+/=]+)''\)$')
    $compressedReaderBytes = [Convert]::FromBase64String($compressedMatch.Groups['payload'].Value)
    $compressedReaderStream = [System.IO.MemoryStream]::new($compressedReaderBytes, $false)
    try {
        $readerGzipStream = [System.IO.Compression.GZipStream]::new(
            $compressedReaderStream,
            [System.IO.Compression.CompressionMode]::Decompress)
        try {
            $readerOutputStream = [System.IO.MemoryStream]::new()
            try {
                $readerGzipStream.CopyTo($readerOutputStream)
                $embeddedReaderScript = [System.Text.UTF8Encoding]::new($false, $true).GetString($readerOutputStream.ToArray())
            }
            finally { $readerOutputStream.Dispose() }
        }
        finally { $readerGzipStream.Dispose() }
    }
    finally { $compressedReaderStream.Dispose() }
    $canonicalConverterDefinition = "function ConvertFrom-CountdownReadbackFlatJsonObject {`n$(${function:ConvertFrom-CountdownReadbackFlatJsonObject}.ToString())`n}"
    Assert-Equal $true $compressedMatch.Success 'Production readback command contains one decodable gzip reader payload'
    Assert-Equal $true $embeddedReaderScript.Contains($canonicalConverterDefinition) 'Production SSH reader embeds the canonical flat JSON converter definition'

    $validLedgerBytes = [System.IO.File]::ReadAllBytes($ledgerPath)
    $validLedgerText = [System.Text.UTF8Encoding]::new($false, $true).GetString($validLedgerBytes)
    $validLedgerObject = $validLedgerText | ConvertFrom-Json -AsHashtable
    $driftCases = [ordered]@{}
    $rootExtra = $validLedgerText | ConvertFrom-Json -AsHashtable
    $rootExtra.unexpected = $true
    $driftCases['root extra field'] = $rootExtra | ConvertTo-Json -Depth 8
    $rootWrongType = $validLedgerText | ConvertFrom-Json -AsHashtable
    $rootWrongType.attempts = 'not-an-array'
    $driftCases['attempts wrong type'] = $rootWrongType | ConvertTo-Json -Depth 8
    $attemptExtra = $validLedgerText | ConvertFrom-Json -AsHashtable
    $attemptExtra.attempts[0].credential = 'must-not-be-accepted'
    $driftCases['attempt extra field'] = $attemptExtra | ConvertTo-Json -Depth 8
    $metricMissing = $validLedgerText | ConvertFrom-Json -AsHashtable
    [void] $metricMissing.attempts[0].metrics.Remove('mbps')
    $driftCases['metrics missing field'] = $metricMissing | ConvertTo-Json -Depth 8
    $metricWrongType = $validLedgerText | ConvertFrom-Json -AsHashtable
    $metricWrongType.attempts[0].metrics.bytes = '1048576'
    $driftCases['metrics wrong type'] = $metricWrongType | ConvertTo-Json -Depth 8
    $invalidStatus = $validLedgerText | ConvertFrom-Json -AsHashtable
    $invalidStatus.attempts[0].status = 'complete'
    $driftCases['status invalid enum'] = $invalidStatus | ConvertTo-Json -Depth 8
    $driftCases['duplicate root field'] = $validLedgerText.Insert(1, '"attempts":[],')
    foreach ($case in $driftCases.GetEnumerator()) {
        [System.IO.File]::WriteAllText($ledgerPath, [string] $case.Value, [System.Text.UTF8Encoding]::new($false))
        $beforeDriftBytes = [System.IO.File]::ReadAllBytes($ledgerPath)
        $readerState = [pscustomobject]@{ Called = $false }
        $reader = {
            $readerState.Called = $true
            throw 'ledger drift 后不得执行 Reader。'
        }.GetNewClosure()
        $driftMessage = $null
        try {
            Invoke-CountdownRecordedSshRead `
                -LedgerPath $ledgerPath `
                -Id 'must-not-run' `
                -Reader $reader | Out-Null
        }
        catch { $driftMessage = $_.Exception.Message }
        $afterDriftBytes = [System.IO.File]::ReadAllBytes($ledgerPath)
        Assert-Equal $true ($driftMessage -match 'SCP 传输账本') "Ledger rejects $($case.Key)"
        Assert-Equal $false ([bool] $readerState.Called) "Ledger drift blocks Reader for $($case.Key)"
        Assert-Equal ([Convert]::ToHexString($beforeDriftBytes)) ([Convert]::ToHexString($afterDriftBytes)) "Ledger drift preserves original bytes for $($case.Key)"
        [System.IO.File]::WriteAllBytes($ledgerPath, $validLedgerBytes)
    }
}
catch {
    [void] $failures.Add($_.Exception.Message)
}
finally {
    foreach ($path in @($ledgerPath, $firstSource, $secondSource, $bindingEvidencePath, $readbackEvidencePath)) {
        if (Test-Path -LiteralPath $path -PathType Leaf) {
            Remove-Item -LiteralPath $path -Force
        }
    }
    if (Test-Path -LiteralPath $testRoot -PathType Container) {
        Remove-Item -LiteralPath $testRoot -Force
    }
}

$result = [pscustomobject]@{
    Passed = [Math]::Max(0, $script:assertions - $failures.Count)
    Failed = $failures.Count
    Failures = @($failures)
}
$result | ConvertTo-Json -Depth 4
if ($failures.Count -gt 0) { exit 1 }
