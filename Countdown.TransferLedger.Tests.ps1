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
}
catch {
    [void] $failures.Add($_.Exception.Message)
}
finally {
    foreach ($path in @($ledgerPath, $firstSource, $secondSource)) {
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
