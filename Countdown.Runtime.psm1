#requires -Version 7.0

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-CountdownRuntimeProperty {
    param(
        [Parameter(Mandatory)] [object] $Object,
        [Parameter(Mandatory)] [string] $Name
    )

    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property) { return $null }
    $property.Value
}

function ConvertTo-CountdownRuntimeFullPath {
    param([Parameter(Mandatory)] [string] $Path)

    [System.IO.Path]::GetFullPath($Path)
}

function ConvertFrom-CountdownWindowsCommandLine {
    param([AllowEmptyString()] [string] $CommandLine)

    if ([string]::IsNullOrWhiteSpace($CommandLine)) { return @() }
    if ($null -eq ('MinminCountdownCommandLineNative' -as [type])) {
        Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;

public static class MinminCountdownCommandLineNative
{
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr CommandLineToArgvW(string lpCmdLine, out int pNumArgs);

    [DllImport("kernel32.dll")]
    public static extern IntPtr LocalFree(IntPtr hMem);
}
'@
    }
    [int] $argumentCount = 0
    $argvPointer = [MinminCountdownCommandLineNative]::CommandLineToArgvW($CommandLine, [ref] $argumentCount)
    if ($argvPointer -eq [IntPtr]::Zero -or $argumentCount -le 0) { return @() }
    try {
        $arguments = [System.Collections.Generic.List[string]]::new()
        for ($index = 0; $index -lt $argumentCount; $index++) {
            $argumentPointer = [System.Runtime.InteropServices.Marshal]::ReadIntPtr($argvPointer, $index * [IntPtr]::Size)
            [void] $arguments.Add([System.Runtime.InteropServices.Marshal]::PtrToStringUni($argumentPointer))
        }
        @($arguments)
    }
    finally {
        [void] [MinminCountdownCommandLineNative]::LocalFree($argvPointer)
    }
}

function Get-CountdownRuntimeFileArgument {
    param([AllowEmptyString()] [string] $CommandLine)

    $argv = @(ConvertFrom-CountdownWindowsCommandLine -CommandLine $CommandLine)
    if ($argv.Count -lt 3) { return $null }
    $commandModes = @('-command', '-c', '-encodedcommand', '-e', '-ec', '-commandwithargs', '-encodedcommandwithargs')
    $switchesWithoutValue = @('-nologo', '-nol', '-noprofile', '-nop', '-noninteractive', '-noni', '-noexit', '-noe', '-sta', '-mta', '-login')
    $switchesWithValue = @('-executionpolicy', '-ep', '-windowstyle', '-w', '-inputformat', '-if', '-outputformat', '-of', '-configurationname', '-config', '-version', '-v', '-settingsfile', '-settings', '-workingdirectory', '-wd')
    $index = 1
    while ($index -lt $argv.Count) {
        $argument = [string] $argv[$index]
        $normalized = $argument.ToLowerInvariant()
        if ($normalized -in $commandModes) { return $null }
        if ($normalized -in @('-file', '-f')) {
            if (($index + 1) -ge $argv.Count) { return $null }
            $argumentPath = [string] $argv[$index + 1]
            if ([string]::IsNullOrWhiteSpace($argumentPath) -or $argumentPath.StartsWith('-')) { return $null }
            try { return ConvertTo-CountdownRuntimeFullPath -Path $argumentPath } catch { return $null }
        }
        if ($normalized -in $switchesWithoutValue) {
            $index++
            continue
        }
        if ($normalized -in $switchesWithValue) {
            if (($index + 1) -ge $argv.Count) { return $null }
            $index += 2
            continue
        }
        # 任何未知启动开关或裸参数都不属于本项目受控的 -File 启动形态；宁可漏检，不能误杀。
        return $null
    }
    $null
}

function Write-CountdownRuntimeJsonAtomically {
    param(
        [Parameter(Mandatory)] [string] $Path,
        [Parameter(Mandatory)] [object] $Value
    )

    $resolvedPath = ConvertTo-CountdownRuntimeFullPath -Path $Path
    $directory = Split-Path -Parent $resolvedPath
    if (-not (Test-Path -LiteralPath $directory -PathType Container)) {
        New-Item -ItemType Directory -Path $directory -Force | Out-Null
    }
    $temporaryPath = "$resolvedPath.$([guid]::NewGuid().ToString('N')).tmp"
    try {
        $json = $Value | ConvertTo-Json -Depth 6
        [System.IO.File]::WriteAllText($temporaryPath, $json, (New-Object System.Text.UTF8Encoding($false)))
        Move-Item -LiteralPath $temporaryPath -Destination $resolvedPath -Force
    }
    finally {
        Remove-Item -LiteralPath $temporaryPath -Force -ErrorAction SilentlyContinue
    }
}

function ConvertTo-CountdownDiagnosticText {
    param(
        [AllowNull()] [object] $Value,
        [Parameter(Mandatory)] [ValidateRange(1, 16384)] [int] $MaxLength
    )

    if ($null -eq $Value) { return $null }
    $text = [string] $Value
    if ($text.Length -le $MaxLength) { return $text }
    $text.Substring(0, $MaxLength)
}

function Rotate-CountdownDiagnosticLog {
    param(
        [Parameter(Mandatory)] [string] $Path,
        [Parameter(Mandatory)] [string] $NextLine,
        [Parameter(Mandatory)] [ValidateRange(256, 104857600)] [long] $MaxBytes,
        [Parameter(Mandatory)] [ValidateRange(1, 10)] [int] $MaxArchives
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { return }
    $encoding = [System.Text.UTF8Encoding]::new($false)
    $nextBytes = $encoding.GetByteCount($NextLine + [Environment]::NewLine)
    if ((Get-Item -LiteralPath $Path).Length + $nextBytes -le $MaxBytes) { return }
    for ($archive = $MaxArchives; $archive -ge 1; $archive--) {
        $source = if ($archive -eq 1) { $Path } else { "$Path.$($archive - 1)" }
        if (-not (Test-Path -LiteralPath $source -PathType Leaf)) { continue }
        $destination = "$Path.$archive"
        Remove-Item -LiteralPath $destination -Force -ErrorAction SilentlyContinue
        Move-Item -LiteralPath $source -Destination $destination -Force
    }
}

function Write-CountdownDiagnosticEvent {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [string] $Path,
        [Parameter(Mandatory)] [ValidateSet('Info', 'Warning', 'Error')] [string] $Level,
        [Parameter(Mandatory)] [ValidateNotNullOrEmpty()] [string] $Event,
        [AllowNull()] [System.Exception] $Exception,
        [AllowNull()] [AllowEmptyString()] [string] $ScriptStackTrace,
        [ValidateRange(256, 104857600)] [long] $MaxBytes = 1048576,
        [ValidateRange(1, 10)] [int] $MaxArchives = 3
    )

    try {
        $resolvedPath = ConvertTo-CountdownRuntimeFullPath -Path $Path
        $directory = Split-Path -Parent $resolvedPath
        if (-not (Test-Path -LiteralPath $directory -PathType Container)) {
            New-Item -ItemType Directory -Path $directory -Force | Out-Null
        }
        $entry = [ordered]@{
            schemaVersion = 1
            timestampUtc = [DateTimeOffset]::UtcNow.ToString('o')
            level = $Level
            event = ConvertTo-CountdownDiagnosticText -Value $Event -MaxLength 256
            processId = $PID
            sessionId = [System.Diagnostics.Process]::GetCurrentProcess().SessionId
            powerShellVersion = $PSVersionTable.PSVersion.ToString()
        }
        if ($null -ne $Exception) {
            $entry.exceptionType = $Exception.GetType().FullName
            $entry.exceptionMessage = ConvertTo-CountdownDiagnosticText -Value $Exception.Message -MaxLength 2048
            $entry.hResult = $Exception.HResult
        }
        if (-not [string]::IsNullOrWhiteSpace($ScriptStackTrace)) {
            $entry.scriptStackTrace = ConvertTo-CountdownDiagnosticText -Value $ScriptStackTrace -MaxLength 4096
        }
        $line = $entry | ConvertTo-Json -Depth 4 -Compress
        Rotate-CountdownDiagnosticLog -Path $resolvedPath -NextLine $line -MaxBytes $MaxBytes -MaxArchives $MaxArchives
        [System.IO.File]::AppendAllText($resolvedPath, $line + [Environment]::NewLine, [System.Text.UTF8Encoding]::new($false))
        $true
    }
    catch {
        $false
    }
}

function New-CountdownStartupRequest {
    param(
        [Parameter(Mandatory)] [string] $Path,
        [switch] $AcceptanceReadOnly
    )

    $request = [ordered]@{
        schemaVersion = 1
        launchToken = [guid]::NewGuid().ToString('N')
        requestedAtUtc = [DateTimeOffset]::UtcNow.ToString('o')
        acceptanceReadOnly = [bool] $AcceptanceReadOnly
        acceptanceState = if ($AcceptanceReadOnly) { 'active' } else { 'none' }
    }
    Write-CountdownRuntimeJsonAtomically -Path $Path -Value $request
    [pscustomobject] $request
}

function Get-CountdownStartupLaunchToken {
    param([Parameter(Mandatory)] [string] $Path)

    if (Test-Path -LiteralPath $Path -PathType Leaf) {
        try {
            $request = [System.IO.File]::ReadAllText($Path, [System.Text.Encoding]::UTF8) | ConvertFrom-Json
            $token = [string] (Get-CountdownRuntimeProperty -Object $request -Name 'launchToken')
            if (-not [string]::IsNullOrWhiteSpace($token)) { return $token }
        }
        catch {
            # 手工启动遇到损坏的旧请求时生成新令牌；安装器会用自己的令牌而不是走此分支。
        }
    }
    [guid]::NewGuid().ToString('N')
}

function Read-CountdownStartupRequestStrict {
    param([Parameter(Mandatory)] [string] $Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw [System.IO.FileNotFoundException]::new('启动请求不存在。', $Path)
    }
    $request = [System.IO.File]::ReadAllText($Path, [System.Text.Encoding]::UTF8) | ConvertFrom-Json -DateKind String
    $schemaVersion = Get-CountdownRuntimeProperty -Object $request -Name 'schemaVersion'
    $token = [string] (Get-CountdownRuntimeProperty -Object $request -Name 'launchToken')
    $state = [string] (Get-CountdownRuntimeProperty -Object $request -Name 'acceptanceState')
    $requestedAtValue = [string] (Get-CountdownRuntimeProperty -Object $request -Name 'requestedAtUtc')
    if ($null -eq $schemaVersion -or [int] $schemaVersion -ne 1) {
        throw "不支持的启动请求 schemaVersion：$schemaVersion"
    }
    if ([string]::IsNullOrWhiteSpace($token) -or $state -notin @('active', 'released', 'ready', 'finalized', 'none')) {
        throw '启动请求的 token 或 acceptanceState 无效。'
    }
    $requestedAtUtc = [DateTimeOffset]::MinValue
    if (-not [DateTimeOffset]::TryParseExact(
            $requestedAtValue,
            'o',
            [System.Globalization.CultureInfo]::InvariantCulture,
            [System.Globalization.DateTimeStyles]::RoundtripKind,
            [ref] $requestedAtUtc)) {
        throw '启动请求的 requestedAtUtc 无效。'
    }
    $request
}

function Get-CountdownStartupRequestMigrationUtc {
    param(
        [Parameter(Mandatory)] [string] $Path,
        [Parameter(Mandatory)] [string] $LaunchToken
    )

    $request = Read-CountdownStartupRequestStrict -Path $Path
    $requestToken = [string] (Get-CountdownRuntimeProperty -Object $request -Name 'launchToken')
    if (-not [string]::Equals($requestToken, $LaunchToken, [System.StringComparison]::Ordinal)) {
        throw '启动请求 token 已变化，拒绝选择设置迁移时刻。'
    }

    ([DateTimeOffset] $request.requestedAtUtc).ToUniversalTime()
}

function Remove-CountdownStartupRequest {
    param(
        [Parameter(Mandatory)] [string] $Path,
        [Parameter(Mandatory)] [string] $LaunchToken
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { return $false }
    try {
        $request = [System.IO.File]::ReadAllText($Path, [System.Text.Encoding]::UTF8) | ConvertFrom-Json
        $requestToken = [string] (Get-CountdownRuntimeProperty -Object $request -Name 'launchToken')
        if (-not [string]::Equals($requestToken, $LaunchToken, [System.StringComparison]::Ordinal)) { return $false }
        Remove-Item -LiteralPath $Path -Force
        $true
    }
    catch {
        $false
    }
}

function Complete-CountdownStartupRequest {
    param(
        [Parameter(Mandatory)] [string] $Path,
        [Parameter(Mandatory)] [string] $LaunchToken
    )

    $request = Read-CountdownStartupRequestStrict -Path $Path
    $requestToken = [string] (Get-CountdownRuntimeProperty -Object $request -Name 'launchToken')
    $acceptanceState = [string] (Get-CountdownRuntimeProperty -Object $request -Name 'acceptanceState')
    if (-not [string]::Equals($requestToken, $LaunchToken, [System.StringComparison]::Ordinal) -or $acceptanceState -ne 'active') {
        return $false
    }
    $request.acceptanceReadOnly = $true
    $request.acceptanceState = 'released'
    $request | Add-Member -NotePropertyName releasedAtUtc -NotePropertyValue ([DateTimeOffset]::UtcNow.ToString('o')) -Force
    Write-CountdownRuntimeJsonAtomically -Path $Path -Value $request
    $true
}

function Wait-CountdownStartupRequestState {
    param(
        [Parameter(Mandatory)] [string] $Path,
        [Parameter(Mandatory)] [string] $LaunchToken,
        [Parameter(Mandatory)] [ValidateSet('released', 'ready', 'finalized')] [string] $ExpectedState,
        [ValidateRange(0, 300)] [int] $TimeoutSeconds = 30,
        [ValidateRange(1, 5000)] [int] $PollMilliseconds = 100
    )

    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)
    $lastReason = '尚未读取到有效启动请求。'
    do {
        try {
            $request = Read-CountdownStartupRequestStrict -Path $Path
            $requestToken = [string] (Get-CountdownRuntimeProperty -Object $request -Name 'launchToken')
            $state = [string] (Get-CountdownRuntimeProperty -Object $request -Name 'acceptanceState')
            if (-not [string]::Equals($requestToken, $LaunchToken, [System.StringComparison]::Ordinal)) {
                $lastReason = '启动请求 token 已变化。'
            }
            elseif ($state -eq $ExpectedState) {
                return $request
            }
            else {
                $lastReason = "当前状态为 $state。"
            }
        }
        catch {
            $lastReason = $_.Exception.Message
        }
        if ([DateTimeOffset]::UtcNow -ge $deadline) { break }
        Start-Sleep -Milliseconds $PollMilliseconds
    } while ($true)
    throw "启动请求未进入 $ExpectedState 状态（$TimeoutSeconds 秒）：$lastReason"
}

function Set-CountdownStartupRequestFinalized {
    param(
        [Parameter(Mandatory)] [string] $Path,
        [Parameter(Mandatory)] [string] $LaunchToken
    )

    $request = Read-CountdownStartupRequestStrict -Path $Path
    $requestToken = [string] (Get-CountdownRuntimeProperty -Object $request -Name 'launchToken')
    $acceptanceState = [string] (Get-CountdownRuntimeProperty -Object $request -Name 'acceptanceState')
    if (-not [string]::Equals($requestToken, $LaunchToken, [System.StringComparison]::Ordinal) -or $acceptanceState -ne 'ready') {
        return $false
    }
    $request.acceptanceReadOnly = $false
    $request.acceptanceState = 'finalized'
    $request | Add-Member -NotePropertyName finalizedAtUtc -NotePropertyValue ([DateTimeOffset]::UtcNow.ToString('o')) -Force
    Write-CountdownRuntimeJsonAtomically -Path $Path -Value $request
    $true
}

function Write-CountdownWindowReadyReceipt {
    param(
        [Parameter(Mandatory)] [string] $Path,
        [Parameter(Mandatory)] [string] $LaunchToken,
        [Parameter(Mandatory)] [ValidateRange(1, 2147483647)] [int] $ProcessId,
        [Parameter(Mandatory)] [ValidateRange(0, 2147483647)] [int] $SessionId,
        [Parameter(Mandatory)] [string] $ApplicationPath,
        [ValidateSet('Pending', 'Ready', 'Unavailable')] [string] $CalendarState = 'Pending'
    )

    if ([string]::IsNullOrWhiteSpace($LaunchToken)) { throw '启动回执缺少 launchToken。' }
    $receipt = [ordered]@{
        schemaVersion = 4
        stage = 'window-ready'
        launchToken = $LaunchToken
        processId = $ProcessId
        sessionId = $SessionId
        applicationPath = ConvertTo-CountdownRuntimeFullPath -Path $ApplicationPath
        readyAtUtc = [DateTimeOffset]::UtcNow.ToString('o')
        calendarState = $CalendarState
        timeZoneId = 'China Standard Time'
    }
    Write-CountdownRuntimeJsonAtomically -Path $Path -Value $receipt
    [pscustomobject] $receipt
}

function Read-CountdownRuntimeReceipt {
    param([Parameter(Mandatory)] [string] $Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { return $null }
    try {
        [System.IO.File]::ReadAllText($Path, [System.Text.Encoding]::UTF8) | ConvertFrom-Json
    }
    catch {
        # 读取者在原子替换的短暂可见窗口及人工损坏时继续等待，而不是接受半份状态。
        $null
    }
}

function Test-CountdownWindowReadyReceipt {
    param(
        [AllowNull()] [object] $Receipt,
        [Parameter(Mandatory)] [string] $ExpectedLaunchToken,
        [Parameter(Mandatory)] [ValidateRange(0, 2147483647)] [int] $ExpectedSessionId,
        [Parameter(Mandatory)] [string] $ExpectedApplicationPath,
        [scriptblock] $ProcessProbe,
        [switch] $AllowLegacyPowerShellAdapter
    )

    $result = [ordered]@{ Ready = $false; Reason = ''; Receipt = $Receipt }
    if ($null -eq $Receipt) { $result.Reason = '运行状态回执不存在或不是有效 JSON。'; return [pscustomobject] $result }
    if ((Get-CountdownRuntimeProperty -Object $Receipt -Name 'schemaVersion') -ne 4) { $result.Reason = '运行状态回执 schema 不受支持。'; return [pscustomobject] $result }
    if (-not [string]::Equals([string] (Get-CountdownRuntimeProperty -Object $Receipt -Name 'stage'), 'window-ready', [System.StringComparison]::Ordinal)) { $result.Reason = '运行状态尚未到达 window-ready。'; return [pscustomobject] $result }
    if (-not [string]::Equals([string] (Get-CountdownRuntimeProperty -Object $Receipt -Name 'launchToken'), $ExpectedLaunchToken, [System.StringComparison]::Ordinal)) { $result.Reason = '运行状态回执属于其他启动令牌。'; return [pscustomobject] $result }
    if ((Get-CountdownRuntimeProperty -Object $Receipt -Name 'sessionId') -ne $ExpectedSessionId) { $result.Reason = '运行状态回执不属于交互会话。'; return [pscustomobject] $result }
    $receiptApplicationPath = Get-CountdownRuntimeProperty -Object $Receipt -Name 'applicationPath'
    if ([string]::IsNullOrWhiteSpace([string] $receiptApplicationPath) -or -not [string]::Equals((ConvertTo-CountdownRuntimeFullPath -Path ([string] $receiptApplicationPath)), (ConvertTo-CountdownRuntimeFullPath -Path $ExpectedApplicationPath), [System.StringComparison]::OrdinalIgnoreCase)) {
        $result.Reason = '运行状态回执的应用路径不匹配。'
        return [pscustomobject] $result
    }
    $processId = Get-CountdownRuntimeProperty -Object $Receipt -Name 'processId'
    if ($null -eq $processId -or [int] $processId -le 0) { $result.Reason = '运行状态回执缺少有效进程号。'; return [pscustomobject] $result }
    if ($null -eq $ProcessProbe) {
        $ProcessProbe = {
            param([int] $Id)
            Get-CimInstance Win32_Process -Filter "ProcessId=$Id" -OperationTimeoutSec 2 -ErrorAction SilentlyContinue | Select-Object -First 1
        }
    }
    $process = & $ProcessProbe ([int] $processId)
    if ($null -eq $process) { $result.Reason = '运行状态回执对应的进程不存在。'; return [pscustomobject] $result }
    if ((Get-CountdownRuntimeProperty -Object $process -Name 'ProcessId') -ne [int] $processId) { $result.Reason = '进程探针返回的进程号与回执不一致。'; return [pscustomobject] $result }
    if ((Get-CountdownRuntimeProperty -Object $process -Name 'SessionId') -ne $ExpectedSessionId) { $result.Reason = '应用进程不属于交互会话。'; return [pscustomobject] $result }
    $expectedAdapterPath = ConvertTo-CountdownRuntimeFullPath -Path $ExpectedApplicationPath
    $expectedExecutablePath = ConvertTo-CountdownRuntimeFullPath -Path (Join-Path (Split-Path -Parent $expectedAdapterPath) 'app\MinminCountdown.exe')
    $processExecutablePath = [string] (Get-CountdownRuntimeProperty -Object $process -Name 'ExecutablePath')
    $isManagedExecutable =
        -not [string]::IsNullOrWhiteSpace($processExecutablePath) -and
        [string]::Equals(
            (ConvertTo-CountdownRuntimeFullPath -Path $processExecutablePath),
            $expectedExecutablePath,
            [System.StringComparison]::OrdinalIgnoreCase
        )
    $isManagedAdapter = $false
    if ($AllowLegacyPowerShellAdapter) {
        $commandLine = [string] (Get-CountdownRuntimeProperty -Object $process -Name 'CommandLine')
        $fileArgument = Get-CountdownRuntimeFileArgument -CommandLine $commandLine
        $isManagedAdapter =
            -not [string]::IsNullOrWhiteSpace($fileArgument) -and
            [string]::Equals($fileArgument, $expectedAdapterPath, [System.StringComparison]::OrdinalIgnoreCase)
    }
    if (-not $isManagedExecutable -and -not $isManagedAdapter) {
        $result.Reason = if ($AllowLegacyPowerShellAdapter) {
            '应用进程既不是受安装器管理的 C# EXE，也未指向受管 PowerShell 适配脚本。'
        }
        else {
            '应用进程不是受安装器管理的 C# EXE。'
        }
        return [pscustomobject] $result
    }
    $calendarState = [string] (Get-CountdownRuntimeProperty -Object $Receipt -Name 'calendarState')
    if ($calendarState -notin @('Pending', 'Ready', 'Unavailable')) { $result.Reason = '运行状态回执缺少可识别的日历状态。'; return [pscustomobject] $result }
    $result.Ready = $true
    $result.Reason = '窗口已首次渲染。'
    [pscustomobject] $result
}

function Wait-CountdownWindowReady {
    param(
        [Parameter(Mandatory)] [string] $Path,
        [Parameter(Mandatory)] [string] $LaunchToken,
        [Parameter(Mandatory)] [ValidateRange(0, 2147483647)] [int] $ExpectedSessionId,
        [Parameter(Mandatory)] [string] $ExpectedApplicationPath,
        [ValidateRange(0, 300)] [int] $TimeoutSeconds = 30,
        [ValidateRange(1, 5000)] [int] $PollMilliseconds = 250,
        [scriptblock] $ProcessProbe,
        [switch] $AllowLegacyPowerShellAdapter
    )

    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)
    $lastReason = '运行状态回执尚未出现。'
    do {
        $receipt = Read-CountdownRuntimeReceipt -Path $Path
        $evaluation = Test-CountdownWindowReadyReceipt -Receipt $receipt -ExpectedLaunchToken $LaunchToken -ExpectedSessionId $ExpectedSessionId -ExpectedApplicationPath $ExpectedApplicationPath -ProcessProbe $ProcessProbe -AllowLegacyPowerShellAdapter:$AllowLegacyPowerShellAdapter
        if ($evaluation.Ready) { return $evaluation.Receipt }
        $lastReason = $evaluation.Reason
        if ([DateTimeOffset]::UtcNow -ge $deadline) { break }
        Start-Sleep -Milliseconds $PollMilliseconds
    } while ($true)
    throw "启动就绪回执超时（$TimeoutSeconds 秒）：$lastReason"
}

function Get-CountdownExpectedProcessProbeReason {
    param(
        [Parameter(Mandatory)] [ValidateRange(1, 2147483647)] [int] $ExpectedProcessId,
        [Parameter(Mandatory)] [ValidateRange(0, 2147483647)] [int] $ExpectedSessionId,
        [Parameter(Mandatory)] [string] $ExpectedApplicationPath,
        [scriptblock] $ProcessProbe
    )

    if ($null -eq $ProcessProbe) {
        $ProcessProbe = {
            param([int] $Id)
            Get-CimInstance Win32_Process -Filter "ProcessId=$Id" -OperationTimeoutSec 2 -ErrorAction Stop |
                Select-Object -First 1
        }
    }

    try {
        $process = & $ProcessProbe $ExpectedProcessId
    }
    catch {
        return "期望进程 $ExpectedProcessId 探针失败：$($_.Exception.Message)"
    }
    if ($null -eq $process) {
        return "期望进程 $ExpectedProcessId 不存在。"
    }
    if ((Get-CountdownRuntimeProperty -Object $process -Name 'ProcessId') -ne $ExpectedProcessId) {
        return "期望进程 $ExpectedProcessId 的探针返回了其他进程号。"
    }
    if ((Get-CountdownRuntimeProperty -Object $process -Name 'SessionId') -ne $ExpectedSessionId) {
        return "期望进程 $ExpectedProcessId 仍在运行，但会话不匹配。"
    }

    $expectedAdapterPath = ConvertTo-CountdownRuntimeFullPath -Path $ExpectedApplicationPath
    $expectedExecutablePath = ConvertTo-CountdownRuntimeFullPath -Path (Join-Path (Split-Path -Parent $expectedAdapterPath) 'app\MinminCountdown.exe')
    $processExecutablePath = [string] (Get-CountdownRuntimeProperty -Object $process -Name 'ExecutablePath')
    if ([string]::IsNullOrWhiteSpace($processExecutablePath) -or
        -not [string]::Equals(
            (ConvertTo-CountdownRuntimeFullPath -Path $processExecutablePath),
            $expectedExecutablePath,
            [System.StringComparison]::OrdinalIgnoreCase)) {
        return "期望进程 $ExpectedProcessId 仍在运行，但可执行路径不匹配。"
    }

    "期望进程 $ExpectedProcessId 仍在运行，且会话和可执行路径匹配。"
}

function Wait-CountdownAcceptanceReadyReceipt {
    param(
        [Parameter(Mandatory)] [string] $Path,
        [Parameter(Mandatory)] [string] $LaunchToken,
        [Parameter(Mandatory)] [ValidateRange(1, 2147483647)] [int] $ExpectedProcessId,
        [Parameter(Mandatory)] [ValidateRange(0, 2147483647)] [int] $ExpectedSessionId,
        [Parameter(Mandatory)] [string] $ExpectedApplicationPath,
        [ValidateRange(0, 300)] [int] $TimeoutSeconds = 30,
        [ValidateRange(1, 5000)] [int] $PollMilliseconds = 100,
        [scriptblock] $ProcessProbe
    )

    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)
    $lastReason = '尚未读取到 finalized-ready 回执。'
    do {
        $receipt = Read-CountdownRuntimeReceipt -Path $Path
        if ($null -eq $receipt) {
            $processReason = Get-CountdownExpectedProcessProbeReason -ExpectedProcessId $ExpectedProcessId -ExpectedSessionId $ExpectedSessionId -ExpectedApplicationPath $ExpectedApplicationPath -ProcessProbe $ProcessProbe
            $lastReason = "回执不存在、损坏或不可读；$processReason"
        }
        elseif ([int] (Get-CountdownRuntimeProperty -Object $receipt -Name 'schemaVersion') -ne 1 -or
            [string] (Get-CountdownRuntimeProperty -Object $receipt -Name 'stage') -ne 'finalized-ready') {
            $processReason = Get-CountdownExpectedProcessProbeReason -ExpectedProcessId $ExpectedProcessId -ExpectedSessionId $ExpectedSessionId -ExpectedApplicationPath $ExpectedApplicationPath -ProcessProbe $ProcessProbe
            $lastReason = "回执 schema 或 stage 无效；$processReason"
        }
        elseif ([int] (Get-CountdownRuntimeProperty -Object $receipt -Name 'processId') -ne $ExpectedProcessId) {
            $processReason = Get-CountdownExpectedProcessProbeReason -ExpectedProcessId $ExpectedProcessId -ExpectedSessionId $ExpectedSessionId -ExpectedApplicationPath $ExpectedApplicationPath -ProcessProbe $ProcessProbe
            $lastReason = "回执进程号与期望进程 $ExpectedProcessId 不一致；$processReason"
        }
        else {
            $shadowReceipt = [pscustomobject]@{
                schemaVersion = 4
                stage = 'window-ready'
                launchToken = Get-CountdownRuntimeProperty -Object $receipt -Name 'launchToken'
                processId = Get-CountdownRuntimeProperty -Object $receipt -Name 'processId'
                sessionId = Get-CountdownRuntimeProperty -Object $receipt -Name 'sessionId'
                applicationPath = Get-CountdownRuntimeProperty -Object $receipt -Name 'applicationPath'
                readyAtUtc = Get-CountdownRuntimeProperty -Object $receipt -Name 'readyAtUtc'
                calendarState = 'Pending'
                timeZoneId = 'China Standard Time'
            }
            $evaluation = Test-CountdownWindowReadyReceipt -Receipt $shadowReceipt -ExpectedLaunchToken $LaunchToken -ExpectedSessionId $ExpectedSessionId -ExpectedApplicationPath $ExpectedApplicationPath -ProcessProbe $ProcessProbe
            if ($evaluation.Ready) { return $receipt }
            $lastReason = $evaluation.Reason
        }
        if ([DateTimeOffset]::UtcNow -ge $deadline) { break }
        Start-Sleep -Milliseconds $PollMilliseconds
    } while ($true)
    throw "应用未确认 finalized-ready（$TimeoutSeconds 秒）：$lastReason"
}

Export-ModuleMember -Function New-CountdownStartupRequest, Get-CountdownStartupLaunchToken, Get-CountdownStartupRequestMigrationUtc, Remove-CountdownStartupRequest, Complete-CountdownStartupRequest, Wait-CountdownStartupRequestState, Set-CountdownStartupRequestFinalized, Write-CountdownWindowReadyReceipt, Read-CountdownRuntimeReceipt, Test-CountdownWindowReadyReceipt, Wait-CountdownWindowReady, Wait-CountdownAcceptanceReadyReceipt, Write-CountdownDiagnosticEvent
