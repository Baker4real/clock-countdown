#requires -Version 7.0

# 桌面启动 T0–T4 分段时延探针。样本只用于归因，不汇入门槛样本池。
# 夹具复刻 -DesktopLauncherOnly：adapter 副本 + app junction + 隔离 settings。
# T1 必须在第一次 window-ready 之后才查询 Win32_Process.CreationDate。

[CmdletBinding()]
param(
    [int] $SampleCount = 1,
    [ValidateSet('Cold', 'Warm', 'Both')]
    [string] $StartKind = 'Both',
    [string] $EvidenceRoot = 'D:\Codex\.codex\run-evidence\clock-countdown-desktop-launcher-tests\probe',
    [string] $RepoRoot = (Split-Path -Parent $PSScriptRoot),
    [switch] $SelfCheck,
    [int] $TimeoutMilliseconds = 10000
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false

if ($PSVersionTable.PSEdition -ne 'Core' -or $PSVersionTable.PSVersion.Major -lt 7) {
    throw "桌面启动时延探针需要 PowerShell 7 Core，当前：$($PSVersionTable.PSEdition) $($PSVersionTable.PSVersion)"
}

if ($SelfCheck) {
    $SampleCount = 1
    $StartKind = 'Both'
}

if ($SampleCount -lt 1) {
    throw 'SampleCount 必须 >= 1'
}

if ($TimeoutMilliseconds -lt 1) {
    throw 'TimeoutMilliseconds 必须 >= 1'
}

$utf8NoBom = [System.Text.UTF8Encoding]::new($false)
$safeTreeDelete = 'D:\Codex\.codex\scripts\invoke-safe-tree-delete.cmd'
$capabilityRoot = [System.IO.Path]::GetFullPath('D:\Codex\.codex\run-evidence')
$productionHost = [System.IO.Path]::GetFullPath('D:\敏敏子倒计时\app\MinminCountdown.exe')
$productionSettingsPath = [System.IO.Path]::GetFullPath('D:\敏敏子倒计时\settings.json')
$isolatedSettingsJson = '{"schemaVersion":10,"timeZoneId":"China Standard Time","displayPrecision":"Minute","monthlySalary":0,"workStart":"09:00","workEnd":"18:00","themePreference":"Day","tags":[],"countdowns":[],"notes":[],"reminderStates":{}}'
$clockAlignment = [ordered]@{
    comparisonDomain = 'DateTimeOffset UTC ticks'
    t0Source = 'probe DateTimeOffset.UtcNow immediately before Start-Process'
    t1Source = 'Win32_Process.CreationDate queried after first window-ready; Unspecified Kind treated as local'
    t2Source = 'host logs/application.jsonl ProcessStarted timestampUtc'
    t3Source = 'host logs/application.jsonl WindowReady timestampUtc'
    t4Source = 'probe DateTimeOffset.UtcNow at first runtime-status stage=window-ready'
    stopwatch = 'probe Stopwatch covers T0 to T4 only; T1-T3 stay on UTC wall clock'
    t1ObserverRule = 'CreationDate is not queried during the window-ready wait loop'
}

if (-not ('MinminCountdownDesktopLauncherLatencyWindowProbe' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
using System.Text;

public static class MinminCountdownDesktopLauncherLatencyWindowProbe
{
    public delegate bool EnumWindowsProc(IntPtr windowHandle, IntPtr state);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool EnumWindows(EnumWindowsProc callback, IntPtr state);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsWindowVisible(IntPtr windowHandle);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint GetWindowThreadProcessId(IntPtr windowHandle, out uint processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern int GetWindowText(IntPtr windowHandle, StringBuilder text, int maxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern int GetClassName(IntPtr windowHandle, StringBuilder className, int maxCount);
}
'@
}

function ConvertTo-ProbeUtcOffset {
    param($Value)

    if ($null -eq $Value) {
        return $null
    }

    if ($Value -is [DateTimeOffset]) {
        return ([DateTimeOffset] $Value).ToUniversalTime()
    }

    if ($Value -is [DateTime]) {
        $dt = [DateTime] $Value
        switch ($dt.Kind) {
            'Utc' { return [DateTimeOffset]::new($dt, [TimeSpan]::Zero) }
            'Local' { return [DateTimeOffset]::new($dt).ToUniversalTime() }
            default {
                return [DateTimeOffset]::new(
                    [DateTime]::SpecifyKind($dt, [DateTimeKind]::Local)
                ).ToUniversalTime()
            }
        }
    }

    $parsed = [datetimeoffset]::MinValue
    if ([DateTimeOffset]::TryParse(
            [string] $Value,
            [System.Globalization.CultureInfo]::InvariantCulture,
            [System.Globalization.DateTimeStyles]::RoundtripKind,
            [ref] $parsed)) {
        return $parsed.ToUniversalTime()
    }

    return $null
}

function Format-ProbeUtc {
    param($Value)

    $utc = ConvertTo-ProbeUtcOffset -Value $Value
    if ($null -eq $utc) {
        return $null
    }

    return $utc.ToUniversalTime().ToString('o')
}

function Get-ProbeUtcMilliseconds {
    param(
        $From,
        $To
    )

    $fromUtc = ConvertTo-ProbeUtcOffset -Value $From
    $toUtc = ConvertTo-ProbeUtcOffset -Value $To
    if ($null -eq $fromUtc -or $null -eq $toUtc) {
        return $null
    }

    return [Math]::Round(($toUtc - $fromUtc).TotalMilliseconds, 3)
}

function Get-ProbeFileSha256 {
    param([AllowNull()] [string] $Path)

    if ([string]::IsNullOrWhiteSpace($Path) -or -not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        return $null
    }

    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash
}

function Get-JsonPropertyValue {
    param(
        $Object,
        [Parameter(Mandatory)] [string] $Name
    )

    if ($null -eq $Object) {
        return $null
    }

    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property) {
        return $null
    }

    return $property.Value
}

function Get-VisibleProcessWindows {
    param([Parameter(Mandatory)] [int] $ProcessId)

    $windows = [System.Collections.Generic.List[object]]::new()
    $callback = [MinminCountdownDesktopLauncherLatencyWindowProbe+EnumWindowsProc] {
        param([IntPtr] $windowHandle, [IntPtr] $state)
        [uint32] $ownerProcessId = 0
        [void] [MinminCountdownDesktopLauncherLatencyWindowProbe]::GetWindowThreadProcessId(
            $windowHandle,
            [ref] $ownerProcessId)
        if ($ownerProcessId -eq $ProcessId -and
            [MinminCountdownDesktopLauncherLatencyWindowProbe]::IsWindowVisible($windowHandle)) {
            $title = [System.Text.StringBuilder]::new(512)
            $className = [System.Text.StringBuilder]::new(256)
            [void] [MinminCountdownDesktopLauncherLatencyWindowProbe]::GetWindowText(
                $windowHandle,
                $title,
                $title.Capacity)
            [void] [MinminCountdownDesktopLauncherLatencyWindowProbe]::GetClassName(
                $windowHandle,
                $className,
                $className.Capacity)
            $windows.Add([pscustomobject]@{
                    Handle = $windowHandle.ToInt64()
                    Title = $title.ToString()
                    ClassName = $className.ToString()
                })
        }
        return $true
    }
    [void] [MinminCountdownDesktopLauncherLatencyWindowProbe]::EnumWindows($callback, [IntPtr]::Zero)
    return @($windows)
}

function Get-ProbeWin32Process {
    param([Parameter(Mandatory)] [int] $ProcessId)

    return Get-CimInstance -ClassName Win32_Process -Filter "ProcessId=$ProcessId" -OperationTimeoutSec 5 -ErrorAction SilentlyContinue |
        Select-Object -First 1
}

function Get-ProbeHostProcessRecords {
    param([Parameter(Mandatory)] [string] $ExecutablePath)

    $normalized = [System.IO.Path]::GetFullPath($ExecutablePath)
    return @(
        Get-CimInstance -ClassName Win32_Process -Filter "Name='MinminCountdown.exe'" -OperationTimeoutSec 5 -ErrorAction Stop |
            Where-Object {
                -not [string]::IsNullOrWhiteSpace([string] $_.ExecutablePath) -and
                [string]::Equals(
                    [System.IO.Path]::GetFullPath([string] $_.ExecutablePath),
                    $normalized,
                    [System.StringComparison]::OrdinalIgnoreCase)
            }
    )
}

function Stop-ProbeOwnedProcess {
    param([AllowNull()] $Process)

    if ($null -eq $Process) {
        return
    }

    try {
        if (-not $Process.HasExited) {
            $Process.Kill($true)
            [void] $Process.WaitForExit(5000)
        }
    }
    catch {
        if ($_.Exception -isnot [System.InvalidOperationException]) {
            throw
        }
    }
    finally {
        $Process.Dispose()
    }
}

function Stop-ProbeProcessId {
    param(
        [Parameter(Mandatory)] [int] $ProcessId,
        [int[]] $ProtectedProcessIds = @()
    )

    if ($ProcessId -le 0 -or $ProtectedProcessIds -contains $ProcessId) {
        return
    }

    try {
        $process = [System.Diagnostics.Process]::GetProcessById($ProcessId)
    }
    catch {
        return
    }

    try {
        if (-not $process.HasExited) {
            $process.Kill($true)
            [void] $process.WaitForExit(5000)
        }
    }
    catch {
        if ($_.Exception -isnot [System.InvalidOperationException]) {
            throw
        }
    }
    finally {
        $process.Dispose()
    }
}

function Stop-ProbeHostProcesses {
    param(
        [Parameter(Mandatory)] [string] $ExecutablePath,
        [int[]] $ProtectedProcessIds = @()
    )

    foreach ($record in @(Get-ProbeHostProcessRecords -ExecutablePath $ExecutablePath)) {
        Stop-ProbeProcessId -ProcessId ([int] $record.ProcessId) -ProtectedProcessIds $ProtectedProcessIds
    }
}

function Wait-ProbeHostGone {
    param(
        [Parameter(Mandatory)] [string] $ExecutablePath,
        [int] $TimeoutMilliseconds = 5000
    )

    $clock = [System.Diagnostics.Stopwatch]::StartNew()
    do {
        if (@(Get-ProbeHostProcessRecords -ExecutablePath $ExecutablePath).Count -eq 0) {
            return $true
        }
        Start-Sleep -Milliseconds 20
    } while ($clock.ElapsedMilliseconds -lt $TimeoutMilliseconds)

    return $false
}

function Copy-ProbeDesktopLauncherHostPayload {
    param(
        [Parameter(Mandatory)] [string] $SourceHost,
        [Parameter(Mandatory)] [string] $DestinationHost
    )

    $sourceApp = [System.IO.Path]::GetFullPath((Split-Path -Parent $SourceHost))
    $destinationApp = [System.IO.Path]::GetFullPath((Split-Path -Parent $DestinationHost))
    if (-not (Test-Path -LiteralPath $sourceApp -PathType Container)) {
        throw "未找到发布宿主目录：$sourceApp"
    }

    Remove-ProbeDesktopLauncherHostPayload -DestinationHost $DestinationHost
    $parent = Split-Path -Parent $destinationApp
    if (-not (Test-Path -LiteralPath $parent -PathType Container)) {
        New-Item -ItemType Directory -Path $parent -Force | Out-Null
    }

    New-Item -ItemType Junction -Path $destinationApp -Target $sourceApp | Out-Null
    if (-not (Test-Path -LiteralPath $DestinationHost -PathType Leaf)) {
        throw "发布宿主未连接到隔离目录：$DestinationHost"
    }
}

function Remove-ProbeDesktopLauncherHostPayload {
    param([Parameter(Mandatory)] [string] $DestinationHost)

    $destinationApp = [System.IO.Path]::GetFullPath((Split-Path -Parent $DestinationHost))
    if (Test-Path -LiteralPath $destinationApp) {
        [System.IO.Directory]::Delete($destinationApp)
    }
}

function Read-ProbeRuntimeReceipt {
    param([Parameter(Mandatory)] [string] $Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        return $null
    }

    try {
        return Get-Content -LiteralPath $Path -Raw -Encoding UTF8 | ConvertFrom-Json
    }
    catch {
        return $null
    }
}

function Read-ProbeDiagnosticEvent {
    param(
        [Parameter(Mandatory)] [string] $Path,
        [Parameter(Mandatory)] [int] $ProcessId,
        [Parameter(Mandatory)] [string] $EventName
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        return $null
    }

    foreach ($line in Get-Content -LiteralPath $Path -Encoding UTF8) {
        if ([string]::IsNullOrWhiteSpace($line)) {
            continue
        }

        try {
            $entry = $line | ConvertFrom-Json
        }
        catch {
            continue
        }

        $eventValue = [string] (Get-JsonPropertyValue -Object $entry -Name 'event')
        $processValue = Get-JsonPropertyValue -Object $entry -Name 'processId'
        if ($eventValue -eq $EventName -and $null -ne $processValue -and [int] $processValue -eq $ProcessId) {
            return $entry
        }
    }

    return $null
}

function Wait-ProbeDiagnosticEvent {
    param(
        [Parameter(Mandatory)] [string] $Path,
        [Parameter(Mandatory)] [int] $ProcessId,
        [Parameter(Mandatory)] [string] $EventName,
        [int] $TimeoutMilliseconds = 2000
    )

    $clock = [System.Diagnostics.Stopwatch]::StartNew()
    do {
        $entry = Read-ProbeDiagnosticEvent -Path $Path -ProcessId $ProcessId -EventName $EventName
        if ($null -ne $entry) {
            return $entry
        }
        if ($clock.ElapsedMilliseconds -ge $TimeoutMilliseconds) {
            return $null
        }
        Start-Sleep -Milliseconds 10
    } while ($true)
}

function Wait-ProbeWindowReadyReceipt {
    param(
        [Parameter(Mandatory)] [string] $Path,
        [Parameter(Mandatory)] [System.Diagnostics.Stopwatch] $Clock,
        [Parameter(Mandatory)] [int] $TimeoutMilliseconds
    )

    $lastStage = 'missing'
    do {
        $receipt = Read-ProbeRuntimeReceipt -Path $Path
        if ($null -ne $receipt) {
            $lastStage = [string] (Get-JsonPropertyValue -Object $receipt -Name 'stage')
            if ($lastStage -eq 'window-ready') {
                $t4 = [DateTimeOffset]::UtcNow
                $processIdValue = Get-JsonPropertyValue -Object $receipt -Name 'processId'
                $visibleWindows = @()
                if ($null -ne $processIdValue) {
                    $visibleWindows = @(Get-VisibleProcessWindows -ProcessId ([int] $processIdValue))
                }
                return [pscustomobject]@{
                    Receipt = $receipt
                    T4 = $t4
                    ElapsedMilliseconds = [int] $Clock.ElapsedMilliseconds
                    VisibleWindows = $visibleWindows
                    TimedOut = $false
                    LastStage = $lastStage
                }
            }
        }

        if ($Clock.ElapsedMilliseconds -ge $TimeoutMilliseconds) {
            return [pscustomobject]@{
                Receipt = $receipt
                T4 = $null
                ElapsedMilliseconds = [int] $Clock.ElapsedMilliseconds
                VisibleWindows = @()
                TimedOut = $true
                LastStage = $lastStage
            }
        }

        Start-Sleep -Milliseconds 10
    } while ($true)
}

function Get-ProbeWindowObservation {
    param([AllowEmptyCollection()] [object[]] $VisibleWindows)

    $nativeWindows = @($VisibleWindows | Where-Object {
            [string] $_.ClassName -eq 'MinminCountdownNativeStartupFrame'
        })
    $wpfFloatingWindows = @($VisibleWindows | Where-Object {
            [string] $_.Title -eq '敏敏子悬浮窗' -and
            ([string] $_.ClassName).StartsWith('HwndWrapper[', [System.StringComparison]::Ordinal)
        })
    $managerWindows = @($VisibleWindows | Where-Object Title -eq '敏敏子倒计时')

    return [pscustomobject]@{
        NativeStartupFrameCount = $nativeWindows.Count
        WpfFloatingCount = $wpfFloatingWindows.Count
        ManagerWindowCount = $managerWindows.Count
        Titles = @($VisibleWindows | ForEach-Object { [string] $_.Title })
        ClassNames = @($VisibleWindows | ForEach-Object { [string] $_.ClassName })
    }
}

function Get-ProbeDiscardReasons {
    param([Parameter(Mandatory)] $Observation)

    $reasons = [System.Collections.Generic.List[string]]::new()
    if ([bool] (Get-JsonPropertyValue -Object $Observation -Name 'TimedOut')) {
        $reasons.Add('window-ready-timeout')
    }
    if ($null -eq (Get-JsonPropertyValue -Object $Observation -Name 'T4')) {
        $reasons.Add('missing-window-ready-receipt')
    }
    if (-not [bool] (Get-JsonPropertyValue -Object $Observation -Name 'ProcessStartedFound')) {
        $reasons.Add('missing-process-started')
    }
    if (-not [bool] (Get-JsonPropertyValue -Object $Observation -Name 'WindowReadyFound')) {
        $reasons.Add('missing-window-ready')
    }
    if ($null -eq (Get-JsonPropertyValue -Object $Observation -Name 'T1')) {
        $reasons.Add('missing-host-creation-date')
    }
    if ($null -eq (Get-JsonPropertyValue -Object $Observation -Name 'ReadyAtUtc')) {
        $reasons.Add('missing-ready-at-utc')
    }

    $t0 = ConvertTo-ProbeUtcOffset (Get-JsonPropertyValue -Object $Observation -Name 'T0')
    $t1 = ConvertTo-ProbeUtcOffset (Get-JsonPropertyValue -Object $Observation -Name 'T1')
    $t2 = ConvertTo-ProbeUtcOffset (Get-JsonPropertyValue -Object $Observation -Name 'T2')
    $t3 = ConvertTo-ProbeUtcOffset (Get-JsonPropertyValue -Object $Observation -Name 'T3')
    $readyAt = ConvertTo-ProbeUtcOffset (Get-JsonPropertyValue -Object $Observation -Name 'ReadyAtUtc')
    if ($null -ne $t0 -and $null -ne $t1 -and $null -ne $t2 -and $null -ne $t3) {
        if ($t0 -gt $t1 -or $t1 -gt $t2 -or $t2 -gt $t3) {
            $reasons.Add('clock-order-violation')
        }
    }
    if ($null -ne $t3 -and $null -ne $readyAt) {
        if ([Math]::Abs(($t3 - $readyAt).TotalMilliseconds) -gt 2000) {
            $reasons.Add('ready-at-utc-cross-check-failed')
        }
    }

    if ([int] (Get-JsonPropertyValue -Object $Observation -Name 'NativeStartupFrameCount') -gt 0) {
        $reasons.Add('native-startup-frame-visible')
    }
    if ([int] (Get-JsonPropertyValue -Object $Observation -Name 'WpfFloatingCount') -ne 1) {
        $reasons.Add('floating-window-invalid')
    }
    if ([int] (Get-JsonPropertyValue -Object $Observation -Name 'ManagerWindowCount') -ne 0) {
        $reasons.Add('manager-window-visible')
    }

    $hostPath = [string] (Get-JsonPropertyValue -Object $Observation -Name 'HostExecutablePath')
    $productionPath = [string] (Get-JsonPropertyValue -Object $Observation -Name 'ProductionHostPath')
    if (-not [string]::IsNullOrWhiteSpace($hostPath) -and -not [string]::IsNullOrWhiteSpace($productionPath) -and
        [string]::Equals($hostPath, $productionPath, [System.StringComparison]::OrdinalIgnoreCase)) {
        $reasons.Add('production-host-started')
    }

    $launcherId = Get-JsonPropertyValue -Object $Observation -Name 'LauncherProcessId'
    $parentId = Get-JsonPropertyValue -Object $Observation -Name 'HostParentProcessId'
    if ($null -ne $launcherId -and $null -ne $parentId -and [int] $launcherId -ne [int] $parentId) {
        $reasons.Add('host-parent-mismatch')
    }

    $hostSession = Get-JsonPropertyValue -Object $Observation -Name 'HostSessionId'
    $currentSession = Get-JsonPropertyValue -Object $Observation -Name 'CurrentSessionId'
    if ($null -ne $hostSession -and $null -ne $currentSession -and [int] $hostSession -ne [int] $currentSession) {
        $reasons.Add('host-session-mismatch')
    }

    if (-not [bool] (Get-JsonPropertyValue -Object $Observation -Name 'LauncherExited')) {
        $reasons.Add('launcher-did-not-exit')
    }
    $launcherExit = Get-JsonPropertyValue -Object $Observation -Name 'LauncherExitCode'
    if ($null -ne $launcherExit -and [int] $launcherExit -ne 0) {
        $reasons.Add('launcher-nonzero-exit')
    }

    $unique = [System.Collections.Generic.List[string]]::new()
    foreach ($reason in @($reasons | Select-Object -Unique)) {
        $unique.Add([string] $reason)
    }
    return , $unique
}

function New-ProbeSyntheticObservation {
    param(
        [Parameter(Mandatory)] $Base,
        [hashtable] $Override = @{}
    )

    foreach ($key in $Override.Keys) {
        $Base[$key] = $Override[$key]
    }
    return [pscustomobject]$Base
}

function Test-ProbeSyntheticDiscard {
    $t0 = [DateTimeOffset]::Parse('2026-08-19T00:00:00.0000000Z').ToUniversalTime()
    $t1 = $t0.AddMilliseconds(8)
    $t2 = $t1.AddMilliseconds(40)
    $t3 = $t2.AddMilliseconds(120)
    $t4 = $t3.AddMilliseconds(5)
    $base = @{
        TimedOut = $false
        T0 = $t0
        T1 = $t1
        T2 = $t2
        T3 = $t3
        T4 = $t4
        ReadyAtUtc = $t3.AddMilliseconds(-2)
        ProcessStartedFound = $true
        WindowReadyFound = $true
        NativeStartupFrameCount = 0
        WpfFloatingCount = 1
        ManagerWindowCount = 0
        HostExecutablePath = 'D:\probe\fixture\app\MinminCountdown.exe'
        ProductionHostPath = $productionHost
        LauncherProcessId = 11
        HostParentProcessId = 11
        HostSessionId = 1
        CurrentSessionId = 1
        LauncherExited = $true
        LauncherExitCode = 0
    }

    $valid = Get-ProbeDiscardReasons -Observation (New-ProbeSyntheticObservation -Base $base.Clone())
    $missingStarted = Get-ProbeDiscardReasons -Observation (New-ProbeSyntheticObservation -Base $base.Clone() -Override @{
            T2 = $null
            ProcessStartedFound = $false
            ReadyAtUtc = $t3
        })
    $missingReady = Get-ProbeDiscardReasons -Observation (New-ProbeSyntheticObservation -Base $base.Clone() -Override @{
            T3 = $null
            WindowReadyFound = $false
            ReadyAtUtc = $t3
        })
    $clockOrder = Get-ProbeDiscardReasons -Observation (New-ProbeSyntheticObservation -Base $base.Clone() -Override @{
            T1 = $t0.AddMilliseconds(-5)
            ReadyAtUtc = $t3
        })

    $failures = [System.Collections.Generic.List[string]]::new()
    if ($valid.Count -ne 0) {
        $failures.Add('valid-sample-was-discarded')
    }
    if ($missingStarted -notcontains 'missing-process-started') {
        $failures.Add('missing-process-started-not-discarded')
    }
    if ($missingReady -notcontains 'missing-window-ready') {
        $failures.Add('missing-window-ready-not-discarded')
    }
    if ($clockOrder -notcontains 'clock-order-violation') {
        $failures.Add('clock-order-violation-not-discarded')
    }

    return [pscustomobject]@{
        Passed = ($failures.Count -eq 0)
        Failures = @($failures)
        MissingProcessStartedDiscarded = ($missingStarted -contains 'missing-process-started')
        MissingWindowReadyDiscarded = ($missingReady -contains 'missing-window-ready')
        ClockOrderDiscarded = ($clockOrder -contains 'clock-order-violation')
    }
}

function Write-ProbeSampleJson {
    param(
        [Parameter(Mandatory)] [string] $Path,
        [Parameter(Mandatory)] $Document
    )

    $parent = Split-Path -Parent $Path
    if (-not (Test-Path -LiteralPath $parent -PathType Container)) {
        New-Item -ItemType Directory -Path $parent -Force | Out-Null
    }

    [System.IO.File]::WriteAllText(
        $Path,
        ($Document | ConvertTo-Json -Depth 8),
        $utf8NoBom)
}

function Invoke-ManagedFixtureCleanup {
    param(
        [Parameter(Mandatory)] [string] $FixtureRoot,
        [Parameter(Mandatory)] [string] $FixtureHost
    )

    if (Test-Path -LiteralPath $FixtureHost) {
        Remove-ProbeDesktopLauncherHostPayload -DestinationHost $FixtureHost
    }

    if (-not (Test-Path -LiteralPath $FixtureRoot -PathType Container)) {
        return
    }

    if (-not (Test-Path -LiteralPath $safeTreeDelete -PathType Leaf)) {
        throw "未找到受管测试目录清理入口：$safeTreeDelete"
    }

    $cleanupJson = & $safeTreeDelete `
        -Mode BatchPurge `
        -TargetPaths $FixtureRoot `
        -CapabilityRoot $capabilityRoot `
        -ConfirmApply `
        -ConfirmPurge `
        -Json | Out-String
    if ($LASTEXITCODE -ne 0) {
        throw "受管探针夹具清理失败：$FixtureRoot exit=$LASTEXITCODE $cleanupJson"
    }
    $cleanup = $cleanupJson | ConvertFrom-Json
    if (-not [bool] $cleanup.completed -or [int] $cleanup.purgedCount -ne 1) {
        throw "受管探针夹具未完成清理：$FixtureRoot"
    }
}

$repoRoot = [System.IO.Path]::GetFullPath($RepoRoot)
$evidenceRoot = [System.IO.Path]::GetFullPath($EvidenceRoot)
$repoLauncher = Join-Path $repoRoot 'CountdownLauncher.exe'
$repoHost = Join-Path $repoRoot 'app\MinminCountdown.exe'
$repoAdapter = Join-Path $repoRoot '敏敏子倒计时.ps1'
$repoSettingsPath = Join-Path $repoRoot 'settings.json'
$runId = [guid]::NewGuid().ToString('N')
$runRoot = Join-Path $evidenceRoot $runId
$fixtureRoot = Join-Path $runRoot 'fixture'
$fixtureLauncher = Join-Path $fixtureRoot 'CountdownLauncher.exe'
$fixtureHostDirectory = Join-Path $fixtureRoot 'app'
$fixtureHost = Join-Path $fixtureHostDirectory 'MinminCountdown.exe'
$fixtureAdapter = Join-Path $fixtureRoot '敏敏子倒计时.ps1'
$isolatedSettingsPath = Join-Path $fixtureRoot 'settings.json'
$runtimeStatusPath = Join-Path $fixtureRoot 'runtime-status.json'
$diagnosticLogPath = Join-Path $fixtureRoot 'logs\application.jsonl'
$currentSessionId = [System.Diagnostics.Process]::GetCurrentProcess().SessionId
$script:activeLauncher = $null
$script:measuredHostPid = 0

if (-not (Test-Path -LiteralPath $repoLauncher -PathType Leaf)) {
    throw "未找到仓库启动器：$repoLauncher"
}
if (-not (Test-Path -LiteralPath $repoHost -PathType Leaf)) {
    throw "未找到发布宿主：$repoHost"
}
if (-not (Test-Path -LiteralPath $repoAdapter -PathType Leaf)) {
    throw "未找到 adapter 脚本：$repoAdapter"
}
if (-not (Test-Path -LiteralPath $safeTreeDelete -PathType Leaf)) {
    throw "未找到受管测试目录清理入口：$safeTreeDelete"
}

$kinds = [System.Collections.Generic.List[string]]::new()
switch ($StartKind) {
    'Cold' { 1..$SampleCount | ForEach-Object { $kinds.Add('cold') } }
    'Warm' { 1..$SampleCount | ForEach-Object { $kinds.Add('warm') } }
    'Both' {
        $kinds.Add('cold')
        1..$SampleCount | ForEach-Object { $kinds.Add('warm') }
    }
}

$repoSettingsHashBefore = Get-ProbeFileSha256 -Path $repoSettingsPath
$productionSettingsHashBefore = Get-ProbeFileSha256 -Path $productionSettingsPath
$productionPidsBefore = @(
    Get-ProbeHostProcessRecords -ExecutablePath $productionHost |
        ForEach-Object { [int] $_.ProcessId }
)

function Invoke-ProbeMeasuredSample {
    param(
        [Parameter(Mandatory)] [string] $Kind,
        [Parameter(Mandatory)] [int] $Index,
        [Parameter(Mandatory)] [string] $OsCacheState
    )

    if (Test-Path -LiteralPath $runtimeStatusPath -PathType Leaf) {
        Remove-Item -LiteralPath $runtimeStatusPath -Force
    }

    $launcher = $null
    $t0 = $null
    $t1 = $null
    $t2 = $null
    $t3 = $null
    $t4 = $null
    $readyAtUtc = $null
    $receipt = $null
    $processStartedEntry = $null
    $windowReadyEntry = $null
    $hostRecord = $null
    $creationRaw = $null
    $creationKind = $null
    $visibleWindows = @()
    $timedOut = $false
    $lastStage = 'missing'
    $probeElapsedMs = $null
    $launcherExited = $false
    $launcherExitCode = $null
    $launcherPid = $null
    $hostPid = 0
    $hostParentPid = $null
    $hostSessionId = $null
    $hostPath = $null
    $hostCommandLine = $null

    try {
        $clock = [System.Diagnostics.Stopwatch]::StartNew()
        $t0 = [DateTimeOffset]::UtcNow
        $launcher = Start-Process -FilePath $fixtureLauncher -WorkingDirectory $fixtureRoot -PassThru -WindowStyle Hidden
        $launcherPid = [int] $launcher.Id
        $script:activeLauncher = $launcher

        $ready = Wait-ProbeWindowReadyReceipt `
            -Path $runtimeStatusPath `
            -Clock $clock `
            -TimeoutMilliseconds $TimeoutMilliseconds
        $timedOut = [bool] $ready.TimedOut
        $lastStage = [string] $ready.LastStage
        $probeElapsedMs = [int] $ready.ElapsedMilliseconds
        $receipt = $ready.Receipt
        $t4 = $ready.T4
        $visibleWindows = @($ready.VisibleWindows)

        if ($null -ne $receipt) {
            $readyAtUtc = ConvertTo-ProbeUtcOffset (Get-JsonPropertyValue -Object $receipt -Name 'readyAtUtc')
            $processIdValue = Get-JsonPropertyValue -Object $receipt -Name 'processId'
            $sessionValue = Get-JsonPropertyValue -Object $receipt -Name 'sessionId'
            if ($null -ne $processIdValue) {
                $hostPid = [int] $processIdValue
                $script:measuredHostPid = $hostPid
            }
            if ($null -ne $sessionValue) {
                $hostSessionId = [int] $sessionValue
            }
        }

        # T1 在第一次 window-ready 之后查询，避免 CIM 观察者效应进入 T0–T4 等待环。
        if ($hostPid -gt 0) {
            $hostRecord = Get-ProbeWin32Process -ProcessId $hostPid
        }
        if ($null -eq $hostRecord -and -not $timedOut) {
            $hostRecord = @(Get-ProbeHostProcessRecords -ExecutablePath $fixtureHost) | Select-Object -First 1
        }
        if ($null -ne $hostRecord) {
            if ($hostPid -le 0) {
                $hostPid = [int] $hostRecord.ProcessId
                $script:measuredHostPid = $hostPid
            }
            $creationRaw = $hostRecord.CreationDate
            if ($creationRaw -is [DateTime]) {
                $creationKind = ([DateTime] $creationRaw).Kind.ToString()
            }
            else {
                $creationKind = $creationRaw.GetType().FullName
            }
            $t1 = ConvertTo-ProbeUtcOffset -Value $creationRaw
            $hostParentPid = [int] $hostRecord.ParentProcessId
            if ($null -eq $hostSessionId) {
                $hostSessionId = [int] $hostRecord.SessionId
            }
            if (-not [string]::IsNullOrWhiteSpace([string] $hostRecord.ExecutablePath)) {
                $hostPath = [System.IO.Path]::GetFullPath([string] $hostRecord.ExecutablePath)
            }
            $hostCommandLine = [string] $hostRecord.CommandLine
        }

        if ($hostPid -gt 0) {
            $processStartedEntry = Wait-ProbeDiagnosticEvent `
                -Path $diagnosticLogPath `
                -ProcessId $hostPid `
                -EventName 'ProcessStarted' `
                -TimeoutMilliseconds 2000
            $windowReadyEntry = Wait-ProbeDiagnosticEvent `
                -Path $diagnosticLogPath `
                -ProcessId $hostPid `
                -EventName 'WindowReady' `
                -TimeoutMilliseconds 2000
            $t2 = ConvertTo-ProbeUtcOffset (Get-JsonPropertyValue -Object $processStartedEntry -Name 'timestampUtc')
            $t3 = ConvertTo-ProbeUtcOffset (Get-JsonPropertyValue -Object $windowReadyEntry -Name 'timestampUtc')
        }

        if ($null -ne $launcher) {
            if (-not $launcher.HasExited) {
                [void] $launcher.WaitForExit($TimeoutMilliseconds)
            }
            $launcherExited = [bool] $launcher.HasExited
            if ($launcherExited) {
                $launcherExitCode = [int] $launcher.ExitCode
            }
        }
    }
    finally {
        $script:activeLauncher = $null
        if ($null -ne $launcher) {
            if (-not $launcher.HasExited) {
                try {
                    $launcher.Kill($true)
                    [void] $launcher.WaitForExit(5000)
                }
                catch {
                    if ($_.Exception -isnot [System.InvalidOperationException]) {
                        throw
                    }
                }
            }
            $launcher.Dispose()
        }
    }

    $windowObservation = Get-ProbeWindowObservation -VisibleWindows $visibleWindows
    $observation = [pscustomobject]@{
        TimedOut = $timedOut
        T0 = $t0
        T1 = $t1
        T2 = $t2
        T3 = $t3
        T4 = $t4
        ReadyAtUtc = $readyAtUtc
        ProcessStartedFound = ($null -ne $processStartedEntry)
        WindowReadyFound = ($null -ne $windowReadyEntry)
        NativeStartupFrameCount = [int] $windowObservation.NativeStartupFrameCount
        WpfFloatingCount = [int] $windowObservation.WpfFloatingCount
        ManagerWindowCount = [int] $windowObservation.ManagerWindowCount
        HostExecutablePath = $hostPath
        ProductionHostPath = $productionHost
        LauncherProcessId = $launcherPid
        HostParentProcessId = $hostParentPid
        HostSessionId = $hostSessionId
        CurrentSessionId = $currentSessionId
        LauncherExited = $launcherExited
        LauncherExitCode = $launcherExitCode
    }
    $discardReasons = Get-ProbeDiscardReasons -Observation $observation
    $valid = ($discardReasons.Count -eq 0)

    $document = [ordered]@{
        schemaVersion = 1
        kind = 'desktop-launcher-latency-probe'
        notThresholdEvidence = $true
        purpose = 'segment-attribution-only'
        runId = $runId
        sampleIndex = $Index
        startKind = $Kind
        osCacheState = $OsCacheState
        valid = $valid
        discardReasons = @($discardReasons)
        clockAlignment = $clockAlignment
        t0Utc = Format-ProbeUtc $t0
        t1Utc = Format-ProbeUtc $t1
        t2Utc = Format-ProbeUtc $t2
        t3Utc = Format-ProbeUtc $t3
        t4Utc = Format-ProbeUtc $t4
        readyAtUtc = Format-ProbeUtc $readyAtUtc
        t1RawKind = $creationKind
        intervalsMilliseconds = [ordered]@{
            t0ToT1 = Get-ProbeUtcMilliseconds -From $t0 -To $t1
            t1ToT2 = Get-ProbeUtcMilliseconds -From $t1 -To $t2
            t2ToT3 = Get-ProbeUtcMilliseconds -From $t2 -To $t3
            t3ToT4 = Get-ProbeUtcMilliseconds -From $t3 -To $t4
            t0ToT4 = Get-ProbeUtcMilliseconds -From $t0 -To $t4
            probeStopwatchT0ToT4 = $probeElapsedMs
        }
        process = [ordered]@{
            launcherPid = $launcherPid
            hostPid = if ($hostPid -gt 0) { $hostPid } else { $null }
            hostParentPid = $hostParentPid
            sessionId = $hostSessionId
            hostExecutablePath = $hostPath
            fixtureHostPath = $fixtureHost
            hostCommandLine = $hostCommandLine
            launcherExited = $launcherExited
            launcherExitCode = $launcherExitCode
        }
        windows = [ordered]@{
            classNames = @($windowObservation.ClassNames)
            titles = @($windowObservation.Titles)
            managerWindowCount = [int] $windowObservation.ManagerWindowCount
            nativeStartupFrameCount = [int] $windowObservation.NativeStartupFrameCount
            wpfFloatingCount = [int] $windowObservation.WpfFloatingCount
        }
        receipt = [ordered]@{
            lastStage = $lastStage
            timedOut = $timedOut
            schemaVersion = Get-JsonPropertyValue -Object $receipt -Name 'schemaVersion'
            calendarState = Get-JsonPropertyValue -Object $receipt -Name 'calendarState'
        }
    }

    $sampleName = 'sample-{0:D2}-{1}.json' -f $Index, $Kind
    $samplePath = Join-Path $runRoot $sampleName
    Write-ProbeSampleJson -Path $samplePath -Document $document

    return [pscustomobject]@{
        Path = $samplePath
        Valid = $valid
        StartKind = $Kind
        DiscardReasons = @($discardReasons)
        Document = $document
        HostPid = $hostPid
    }
}

function Invoke-ProbePrimeStart {
    if (Test-Path -LiteralPath $runtimeStatusPath -PathType Leaf) {
        Remove-Item -LiteralPath $runtimeStatusPath -Force
    }

    $launcher = $null
    try {
        $clock = [System.Diagnostics.Stopwatch]::StartNew()
        $launcher = Start-Process -FilePath $fixtureLauncher -WorkingDirectory $fixtureRoot -PassThru -WindowStyle Hidden
        $script:activeLauncher = $launcher
        $ready = Wait-ProbeWindowReadyReceipt `
            -Path $runtimeStatusPath `
            -Clock $clock `
            -TimeoutMilliseconds $TimeoutMilliseconds
        if ([bool] $ready.TimedOut) {
            throw "暖启动预热未观察到 window-ready：stage=$($ready.LastStage)"
        }
        if (-not $launcher.HasExited) {
            [void] $launcher.WaitForExit($TimeoutMilliseconds)
        }
    }
    finally {
        $script:activeLauncher = $null
        Stop-ProbeOwnedProcess -Process $launcher
        Stop-ProbeHostProcesses -ExecutablePath $fixtureHost -ProtectedProcessIds $productionPidsBefore
        if (-not (Wait-ProbeHostGone -ExecutablePath $fixtureHost)) {
            throw "暖启动预热后隔离宿主仍未退出：$fixtureHost"
        }
        if (Test-Path -LiteralPath $runtimeStatusPath -PathType Leaf) {
            Remove-Item -LiteralPath $runtimeStatusPath -Force
        }
    }
}

$synthetic = $null
$samples = [System.Collections.Generic.List[object]]::new()
$cleanupError = $null
$runError = $null

try {
    if ($SelfCheck) {
        $synthetic = Test-ProbeSyntheticDiscard
        if (-not [bool] $synthetic.Passed) {
            throw "合成弃样检查失败：$($synthetic.Failures -join ', ')"
        }
    }

    New-Item -ItemType Directory -Path $fixtureHostDirectory -Force | Out-Null
    Copy-Item -LiteralPath $repoLauncher -Destination $fixtureLauncher
    Copy-ProbeDesktopLauncherHostPayload -SourceHost $repoHost -DestinationHost $fixtureHost
    Copy-Item -LiteralPath $repoAdapter -Destination $fixtureAdapter
    [System.IO.File]::WriteAllText($isolatedSettingsPath, $isolatedSettingsJson, $utf8NoBom)

    if ([string]::Equals(
            [System.IO.Path]::GetFullPath($fixtureLauncher),
            [System.IO.Path]::GetFullPath($repoLauncher),
            [System.StringComparison]::OrdinalIgnoreCase)) {
        throw '探针不得使用仓库现役 CountdownLauncher.exe 启动'
    }
    if ($fixtureHost.StartsWith('D:\敏敏子倒计时', [System.StringComparison]::OrdinalIgnoreCase)) {
        throw '探针夹具宿主路径落在生产安装目录'
    }

    $needPrime = ($kinds[0] -eq 'warm')
    if ($needPrime) {
        Invoke-ProbePrimeStart
    }

    $index = 1
    foreach ($kind in $kinds) {
        if ($index -gt 1 -or $needPrime) {
            Stop-ProbeHostProcesses -ExecutablePath $fixtureHost -ProtectedProcessIds $productionPidsBefore
            if (-not (Wait-ProbeHostGone -ExecutablePath $fixtureHost)) {
                throw "暖启动前隔离宿主仍未退出：$fixtureHost"
            }
            if (Test-Path -LiteralPath $runtimeStatusPath -PathType Leaf) {
                Remove-Item -LiteralPath $runtimeStatusPath -Force
            }
        }

        $osCacheState = if ($index -eq 1 -and -not $needPrime) {
            'fresh-fixture'
        }
        else {
            'restart-after-kill'
        }
        $sample = Invoke-ProbeMeasuredSample -Kind $kind -Index $index -OsCacheState $osCacheState
        $samples.Add($sample)
        $index++
    }
}
catch {
    $runError = $_
}
finally {
    try {
        Stop-ProbeOwnedProcess -Process $script:activeLauncher
        $script:activeLauncher = $null
        if ($script:measuredHostPid -gt 0) {
            Stop-ProbeProcessId -ProcessId $script:measuredHostPid -ProtectedProcessIds $productionPidsBefore
        }
        Stop-ProbeHostProcesses -ExecutablePath $fixtureHost -ProtectedProcessIds $productionPidsBefore
        [void] (Wait-ProbeHostGone -ExecutablePath $fixtureHost)

        $accidentalProduction = @(
            Get-ProbeHostProcessRecords -ExecutablePath $productionHost |
                Where-Object {
                    $pidValue = [int] $_.ProcessId
                    -not ($productionPidsBefore -contains $pidValue)
                }
        )
        foreach ($record in $accidentalProduction) {
            Stop-ProbeProcessId -ProcessId ([int] $record.ProcessId) -ProtectedProcessIds $productionPidsBefore
        }

        Set-Location -LiteralPath $repoRoot
        Invoke-ManagedFixtureCleanup -FixtureRoot $fixtureRoot -FixtureHost $fixtureHost
    }
    catch {
        $cleanupError = $_
    }
}

$repoSettingsHashAfter = Get-ProbeFileSha256 -Path $repoSettingsPath
$productionSettingsHashAfter = Get-ProbeFileSha256 -Path $productionSettingsPath
$productionPidsAfter = @(
    Get-ProbeHostProcessRecords -ExecutablePath $productionHost |
        ForEach-Object { [int] $_.ProcessId }
)
$newProductionPids = @(
    $productionPidsAfter |
        Where-Object { -not ($productionPidsBefore -contains $_) }
)

$repoSettingsUnchanged = ($repoSettingsHashBefore -eq $repoSettingsHashAfter)
$productionSettingsUnchanged = ($productionSettingsHashBefore -eq $productionSettingsHashAfter)
$productionUntouched = ($newProductionPids.Count -eq 0)
$allValid = ($samples.Count -gt 0 -and (@($samples | Where-Object { -not $_.Valid }).Count -eq 0))

$summary = [ordered]@{
    schemaVersion = 1
    kind = 'desktop-launcher-latency-probe-summary'
    notThresholdEvidence = $true
    selfCheck = [bool] $SelfCheck
    runId = $runId
    evidenceRoot = $runRoot
    startKind = $StartKind
    sampleCountRequested = $SampleCount
    allSamplesValid = $allValid
    repoSettingsHashUnchanged = $repoSettingsUnchanged
    productionSettingsHashUnchanged = $productionSettingsUnchanged
    productionUntouched = $productionUntouched
    syntheticDiscard = $synthetic
    samples = @(
        $samples | ForEach-Object {
            [ordered]@{
                path = $_.Path
                valid = [bool] $_.Valid
                startKind = $_.StartKind
                discardReasons = @($_.DiscardReasons)
                t0Utc = $_.Document.t0Utc
                t1Utc = $_.Document.t1Utc
                t2Utc = $_.Document.t2Utc
                t3Utc = $_.Document.t3Utc
                t4Utc = $_.Document.t4Utc
                intervalsMilliseconds = $_.Document.intervalsMilliseconds
                windows = $_.Document.windows
                process = $_.Document.process
            }
        }
    )
}

$summaryPath = Join-Path $runRoot 'summary.json'
Write-ProbeSampleJson -Path $summaryPath -Document $summary
$summary | ConvertTo-Json -Depth 8

if ($null -ne $runError) {
    throw $runError
}
if ($null -ne $cleanupError) {
    throw $cleanupError
}
if (-not $repoSettingsUnchanged) {
    throw '仓库 settings.json 哈希在探针运行后发生变化'
}
if (-not $productionSettingsUnchanged) {
    throw '生产 settings.json 哈希在探针运行后发生变化'
}
if (-not $productionUntouched) {
    throw "探针触碰了生产宿主进程：$($newProductionPids -join ', ')"
}
if ($SelfCheck -and -not $allValid) {
    throw '自检未得到全部有效 T0-T4 样本'
}

exit 0
