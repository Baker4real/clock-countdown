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
    param([Parameter(Mandatory)] [string] $Path)

    $request = [ordered]@{
        schemaVersion = 1
        launchToken = [guid]::NewGuid().ToString('N')
        requestedAtUtc = [DateTimeOffset]::UtcNow.ToString('o')
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
        [scriptblock] $ProcessProbe
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
            Get-CimInstance Win32_Process -Filter "ProcessId=$Id" -ErrorAction SilentlyContinue | Select-Object -First 1
        }
    }
    $process = & $ProcessProbe ([int] $processId)
    if ($null -eq $process) { $result.Reason = '运行状态回执对应的进程不存在。'; return [pscustomobject] $result }
    if ((Get-CountdownRuntimeProperty -Object $process -Name 'ProcessId') -ne [int] $processId) { $result.Reason = '进程探针返回的进程号与回执不一致。'; return [pscustomobject] $result }
    if ((Get-CountdownRuntimeProperty -Object $process -Name 'SessionId') -ne $ExpectedSessionId) { $result.Reason = '应用进程不属于交互会话。'; return [pscustomobject] $result }
    $commandLine = [string] (Get-CountdownRuntimeProperty -Object $process -Name 'CommandLine')
    $fileArgument = Get-CountdownRuntimeFileArgument -CommandLine $commandLine
    if ([string]::IsNullOrWhiteSpace($fileArgument) -or -not [string]::Equals($fileArgument, (ConvertTo-CountdownRuntimeFullPath -Path $ExpectedApplicationPath), [System.StringComparison]::OrdinalIgnoreCase)) {
        $result.Reason = '应用进程命令行未指向受安装器管理的脚本。'
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
        [scriptblock] $ProcessProbe
    )

    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)
    $lastReason = '运行状态回执尚未出现。'
    do {
        $receipt = Read-CountdownRuntimeReceipt -Path $Path
        $evaluation = Test-CountdownWindowReadyReceipt -Receipt $receipt -ExpectedLaunchToken $LaunchToken -ExpectedSessionId $ExpectedSessionId -ExpectedApplicationPath $ExpectedApplicationPath -ProcessProbe $ProcessProbe
        if ($evaluation.Ready) { return $evaluation.Receipt }
        $lastReason = $evaluation.Reason
        if ([DateTimeOffset]::UtcNow -ge $deadline) { break }
        Start-Sleep -Milliseconds $PollMilliseconds
    } while ($true)
    throw "启动就绪回执超时（$TimeoutSeconds 秒）：$lastReason"
}

Export-ModuleMember -Function New-CountdownStartupRequest, Get-CountdownStartupLaunchToken, Remove-CountdownStartupRequest, Write-CountdownWindowReadyReceipt, Read-CountdownRuntimeReceipt, Test-CountdownWindowReadyReceipt, Wait-CountdownWindowReady, Write-CountdownDiagnosticEvent
