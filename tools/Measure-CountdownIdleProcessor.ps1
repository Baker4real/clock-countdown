#requires -Version 7.0
<#
.SYNOPSIS
    Runs inside the official low-impact isolated desktop and measures the real interactive app at idle.
    Only temporary settings and evidence under D:\agents\run-evidence are written.
#>
[CmdletBinding()]
param(
    [string] $RepositoryRoot = (Split-Path -Parent $PSScriptRoot),
    [string] $EvidenceRoot = 'D:\agents\run-evidence\clock-countdown-implement-spec\prd-296-20261118\issue-301',
    [ValidateRange(60, 570)] [int] $SecondsPerTier = 60
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
Import-Module (Join-Path $PSScriptRoot 'Countdown.IdleProcessorMeasurement.psm1') -Force

if ($PSVersionTable.PSEdition -ne 'Core' -or $PSVersionTable.PSVersion.Major -lt 7) {
    throw 'PowerShell 7 Core is required.'
}
if ($env:MINMIN_SUITE_ALLOW_UNISOLATED -eq '1') {
    throw 'Idle measurement refuses the unisolated-desktop override.'
}

if (-not ('MinminIdleMeasurementNative' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

public static class MinminIdleMeasurementNative
{
    public sealed record WindowRecord(long Handle, string Title, bool Visible);
    private delegate bool EnumWindowsProc(IntPtr window, IntPtr state);
    private const int UoiName = 2;
    private const int SwHide = 0;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetThreadDesktop(uint threadId);
    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetUserObjectInformationW(IntPtr handle, int index, StringBuilder info, int maxLength, out uint lengthNeeded);
    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr state);
    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetWindowText(IntPtr window, StringBuilder text, int maxCount);
    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr window, int command);

    public static string GetCurrentDesktopName()
    {
        var handle = GetThreadDesktop(GetCurrentThreadId());
        if (handle == IntPtr.Zero) throw new InvalidOperationException("GetThreadDesktop failed: " + Marshal.GetLastWin32Error());
        var name = new StringBuilder(256);
        if (!GetUserObjectInformationW(handle, UoiName, name, name.Capacity, out _))
            throw new InvalidOperationException("GetUserObjectInformationW failed: " + Marshal.GetLastWin32Error());
        return name.ToString();
    }

    public static WindowRecord[] GetOwnedWindows(int processId)
    {
        var windows = new List<WindowRecord>();
        EnumWindows((window, state) =>
        {
            uint owner;
            GetWindowThreadProcessId(window, out owner);
            if (owner == (uint)processId)
            {
                var title = new StringBuilder(512);
                GetWindowText(window, title, title.Capacity);
                windows.Add(new WindowRecord(window.ToInt64(), title.ToString(), IsWindowVisible(window)));
            }
            return true;
        }, IntPtr.Zero);
        return windows.ToArray();
    }

    public static void HideOwnedWindow(long handle)
    {
        ShowWindow(new IntPtr(handle), SwHide);
    }
}
'@
}

function Write-MeasurementJson {
    param(
        [Parameter(Mandatory)] [string] $Path,
        [Parameter(Mandatory)] $Value
    )
    $parent = [IO.Path]::GetDirectoryName($Path)
    if (-not [IO.Directory]::Exists($parent)) { [IO.Directory]::CreateDirectory($parent) | Out-Null }
    [IO.File]::WriteAllText($Path, ($Value | ConvertTo-Json -Depth 14), [Text.UTF8Encoding]::new($false))
}

function Get-StringSha256 {
    param([Parameter(Mandatory)] [AllowEmptyString()] [string] $Value)
    $bytes = [Text.Encoding]::UTF8.GetBytes($Value)
    [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes))
}

function Get-OwnedWindowVisibility {
    param([Parameter(Mandatory)] [int] $OwnedProcessId)
    $windows = @([MinminIdleMeasurementNative]::GetOwnedWindows($OwnedProcessId))
    $floating = @($windows | Where-Object Title -ceq '敏敏子悬浮窗')
    $main = @($windows | Where-Object Title -ceq '敏敏子倒计时')
    if ($floating.Count -gt 1 -or $main.Count -gt 1) {
        throw "identity: expected at most one floating and one manager window for PID $OwnedProcessId."
    }
    [pscustomobject]@{
        FloatingPresent = $floating.Count -eq 1
        FloatingVisible = $floating.Count -eq 1 -and [bool] $floating[0].Visible
        FloatingHandle = if ($floating.Count -eq 1) { [long] $floating[0].Handle } else { $null }
        MainPresent = $main.Count -eq 1
        MainVisible = $main.Count -eq 1 -and [bool] $main[0].Visible
        MainHandle = if ($main.Count -eq 1) { [long] $main[0].Handle } else { $null }
        OwnedTopLevelWindowCount = $windows.Count
    }
}

function Test-ExpectedVisibility {
    param(
        [Parameter(Mandatory)] $Visibility,
        [Parameter(Mandatory)] $Tier
    )
    $Visibility.FloatingPresent -and
    $Visibility.FloatingVisible -eq [bool] $Tier.FloatingVisible -and
    $Visibility.MainVisible -eq [bool] $Tier.MainVisible -and
    (-not $Tier.MainVisible -or $Visibility.MainPresent)
}

function Wait-ExpectedVisibility {
    param(
        [Parameter(Mandatory)] [int] $OwnedProcessId,
        [Parameter(Mandatory)] $Tier,
        [Parameter(Mandatory)] $Process
    )
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds(30)
    do {
        $visibility = Get-OwnedWindowVisibility -OwnedProcessId $OwnedProcessId
        if (Test-ExpectedVisibility -Visibility $visibility -Tier $Tier) { return $visibility }
        if ($Process.HasExited) { throw "readiness: owned app exited with code $($Process.ExitCode) before visibility stabilized." }
        Start-Sleep -Milliseconds 100
    } while ([DateTimeOffset]::UtcNow -lt $deadline)
    throw "readiness: visibility did not stabilize for $($Tier.Name): $($visibility | ConvertTo-Json -Compress)"
}

function Read-RuntimeReceipt {
    param([Parameter(Mandatory)] [string] $Path)
    if (-not [IO.File]::Exists($Path)) { return $null }
    try { return Get-Content -LiteralPath $Path -Raw -Encoding UTF8 -ErrorAction Stop | ConvertFrom-Json -ErrorAction Stop }
    catch [System.Text.Json.JsonException] { return $null }
    catch [System.Management.Automation.RuntimeException] { return $null }
}

function Wait-OwnedRuntimeReady {
    param(
        [Parameter(Mandatory)] [string] $Path,
        [Parameter(Mandatory)] $Process
    )
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds(60)
    do {
        $receipt = Read-RuntimeReceipt -Path $Path
        if ($null -ne $receipt -and [string] $receipt.stage -ceq 'window-ready' -and [int] $receipt.processId -eq $Process.Id) {
            return $receipt
        }
        if ($Process.HasExited) { throw "readiness: owned app exited with code $($Process.ExitCode) before window-ready." }
        Start-Sleep -Milliseconds 100
    } while ([DateTimeOffset]::UtcNow -lt $deadline)
    throw "readiness: window-ready receipt not observed for PID $($Process.Id) within 60 seconds."
}

function Read-ManagedChildReport {
    param([Parameter(Mandatory)] [string] $Path)
    if (-not [IO.File]::Exists($Path)) { return $null }
    try { return [IO.File]::ReadAllText($Path) | ConvertFrom-Json -Depth 20 -ErrorAction Stop }
    catch [System.Text.Json.JsonException] { return $null }
}

function Wait-ManagedChildReport {
    param(
        [Parameter(Mandatory)] [string] $Path,
        [Parameter(Mandatory)] $WrapperProcess
    )
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds(30)
    while ([DateTimeOffset]::UtcNow -lt $deadline) {
        $childReport = Read-ManagedChildReport -Path $Path
        if ($null -ne $childReport -and $null -ne $childReport.Application) { return $childReport }
        if ($WrapperProcess.HasExited) { throw "identity: managed wrapper exited with code $($WrapperProcess.ExitCode) before reporting an application." }
        Start-Sleep -Milliseconds 100
    }
    throw 'identity: managed wrapper did not report its application within 30 seconds.'
}

function Remove-MeasurementJunction {
    param(
        [Parameter(Mandatory)] [string] $Path,
        [Parameter(Mandatory)] [string] $ExpectedParent
    )
    if (-not [IO.Directory]::Exists($Path)) { return }
    $fullPath = [IO.Path]::GetFullPath($Path)
    $fullParent = [IO.Path]::GetFullPath($ExpectedParent)
    if (-not [string]::Equals([IO.Path]::GetDirectoryName($fullPath), $fullParent, [StringComparison]::OrdinalIgnoreCase)) {
        throw "cleanup: junction path escaped its tier root: $fullPath"
    }
    $item = Get-Item -LiteralPath $fullPath -Force -ErrorAction Stop
    if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -eq 0 -or $item.LinkType -ne 'Junction') {
        throw "cleanup: expected an app junction, not a real directory: $fullPath"
    }
    [IO.Directory]::Delete($fullPath)
}

function Read-OwnedDiagnosticEvents {
    param(
        [Parameter(Mandatory)] [string] $Path,
        [Parameter(Mandatory)] [int] $OwnedProcessId
    )
    if (-not [IO.File]::Exists($Path)) { throw "identity: missing token-bound diagnostic log: $Path" }
    $events = [System.Collections.Generic.List[string]]::new()
    foreach ($line in [IO.File]::ReadAllLines($Path)) {
        if ([string]::IsNullOrWhiteSpace($line)) { continue }
        $entry = $line | ConvertFrom-Json -ErrorAction Stop
        if ([int] $entry.processId -eq $OwnedProcessId) { $events.Add([string] $entry.event) }
    }
    if (-not $events.Contains('ProcessStarted') -or -not $events.Contains('WindowReady')) {
        throw "identity: token-bound child diagnostic report lacks ProcessStarted or WindowReady for PID $OwnedProcessId."
    }
    @($events)
}

function Resolve-IdleCounterInstance {
    param(
        [Parameter(Mandatory)] $Process,
        [Parameter(Mandatory)] [int] $ExpectedProcessId
    )
    $errors = [System.Collections.Generic.List[string]]::new()
    for ($attempt = 1; $attempt -le 5; $attempt++) {
        $counterErrors = @()
        $sets = @(Get-Counter -Counter '\Process(*)\ID Process' -ErrorAction SilentlyContinue -ErrorVariable counterErrors)
        foreach ($error in $counterErrors) { $errors.Add($error.Exception.Message) }
        $matches = @($sets | ForEach-Object CounterSamples | Where-Object {
            $_.Status -eq 0 -and [string] $_.Path -match '\\id process$' -and
            [Math]::Abs([double] $_.CookedValue - $ExpectedProcessId) -lt 0.5
        })
        if ($matches.Count -gt 1) { throw "identity: multiple counter instances match PID $ExpectedProcessId." }
        if ($matches.Count -eq 1) {
            if ($Process.HasExited) { throw "identity: PID $ExpectedProcessId exited during counter-instance resolution." }
            return [pscustomobject]@{
                InstanceName = [string] $matches[0].InstanceName
                Attempts = $attempt
                Errors = @($errors | Select-Object -Unique)
            }
        }
        if ($Process.HasExited) { throw "identity: PID $ExpectedProcessId exited during counter-instance resolution." }
        if ($attempt -lt 5) { Start-Sleep -Seconds 1 }
    }
    throw "identity: no valid performance-counter instance for PID $ExpectedProcessId after 5 attempts; errors=$($errors -join ';')"
}

function Write-IdleCounterDiagnostics {
    param(
        [Parameter(Mandatory)] [object[]] $CounterSets,
        [Parameter(Mandatory)] [int] $ExpectedProcessId,
        [Parameter(Mandatory)] [string] $Path,
        [Parameter(Mandatory)] [AllowEmptyCollection()] [object[]] $Errors,
        [string] $ExpectedInstanceName
    )
    $rows = @(foreach ($set in $CounterSets) {
        $all = @($set.CounterSamples)
        $ids = @($all | Where-Object { [string] $_.Path -match '\\id process$' })
        $validIds = @($ids | Where-Object Status -eq 0)
        $targetIds = @($ids | Where-Object { [Math]::Abs([double] $_.CookedValue - $ExpectedProcessId) -lt 0.5 })
        $targetInstances = @($targetIds | Where-Object Status -eq 0 | ForEach-Object { [string] $_.InstanceName })
        if (-not [string]::IsNullOrWhiteSpace($ExpectedInstanceName)) { $targetInstances = @($ExpectedInstanceName) }
        $targetCpu = @($all | Where-Object {
            $_.Status -eq 0 -and [string] $_.Path -match '\\% processor time$' -and [string] $_.InstanceName -in $targetInstances
        })
        [pscustomobject]@{
            Timestamp = $set.Timestamp
            IdCounterCount = $ids.Count
            ValidIdCounterCount = $validIds.Count
            TargetIdMatchCount = $targetIds.Count
            TargetIdStatuses = @($targetIds | ForEach-Object { [int] $_.Status })
            TargetInstanceNames = $targetInstances
            ValidTargetCpuCount = $targetCpu.Count
            CpuCounterCount = @($all | Where-Object { [string] $_.Path -match '\\% processor time$' }).Count
        }
    })
    $diagnostics = [pscustomobject]@{
        ExpectedProcessId = $ExpectedProcessId
        BindingMode = if ([string]::IsNullOrWhiteSpace($ExpectedInstanceName)) { 'pid-per-set' } else { 'pid-prebound-instance' }
        ExpectedInstanceName = $ExpectedInstanceName
        CounterSetCount = $CounterSets.Count
        CounterErrors = @($Errors | ForEach-Object { $_.Exception.Message } | Select-Object -Unique)
        Sets = $rows
    }
    Write-MeasurementJson -Path $Path -Value $diagnostics
}

function Get-BackgroundTotalCpu {
    $errors = @()
    $sets = @(Get-Counter -Counter '\Processor(_Total)\% Processor Time' -SampleInterval 1 -MaxSamples 3 -ErrorAction SilentlyContinue -ErrorVariable errors)
    $values = @($sets | ForEach-Object CounterSamples | Where-Object Status -eq 0 | ForEach-Object { [double] $_.CookedValue })
    if ($values.Count -eq 0) {
        return [pscustomobject]@{ Available = $false; MeanTotalProcessorPercent = $null; Error = @($errors | ForEach-Object { $_.Exception.Message }) -join '; ' }
    }
    [pscustomobject]@{
        Available = $true
        MeanTotalProcessorPercent = [Math]::Round([double] ($values | Measure-Object -Average).Average, 3)
        Error = @($errors | ForEach-Object { $_.Exception.Message }) -join '; '
    }
}

$desktopName = [MinminIdleMeasurementNative]::GetCurrentDesktopName()
$validDesktop = $desktopName -ceq 'MinminSuiteIso' -or
    ($desktopName.StartsWith('MinminSuiteIso-', [StringComparison]::Ordinal) -and $desktopName.Length -gt 'MinminSuiteIso-'.Length)
if (-not $validDesktop) {
    throw "isolation: current desktop is '$desktopName'; use Invoke-CountdownSuiteLowImpact.ps1 -Stage IdleProcessorMeasurement."
}

$repo = [IO.Path]::GetFullPath($RepositoryRoot)
$evidenceBase = [IO.Path]::GetFullPath($EvidenceRoot)
$allowedEvidenceRoot = [IO.Path]::GetFullPath('D:\agents\run-evidence')
if (-not $evidenceBase.StartsWith($allowedEvidenceRoot.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) {
    throw "evidence: EvidenceRoot must be below $allowedEvidenceRoot."
}
$application = [IO.Path]::GetFullPath((Join-Path $repo 'app\MinminCountdown.exe'))
$applicationDirectory = [IO.Path]::GetDirectoryName($application)
$applicationDll = Join-Path $applicationDirectory 'MinminCountdown.dll'
if (-not [IO.File]::Exists($application) -or -not [IO.File]::Exists($applicationDll)) {
    throw 'build: app\MinminCountdown.exe and MinminCountdown.dll are required; run the official IncrementalBuild stage first.'
}
$wrapperPath = Join-Path $PSScriptRoot 'Invoke-CountdownIdleMeasurementChild.ps1'
$pwshPath = (Get-Command pwsh -CommandType Application -ErrorAction Stop | Select-Object -First 1).Source
$pwshHash = (Get-FileHash -LiteralPath $pwshPath -Algorithm SHA256).Hash
if (-not [IO.File]::Exists($wrapperPath)) { throw "measurement: missing managed wrapper $wrapperPath" }

$runStamp = [DateTimeOffset]::UtcNow.ToString('yyyyMMddTHHmmssfffZ')
$runSuffix = [guid]::NewGuid().ToString('N').Substring(0, 8)
$runRoot = Join-Path $evidenceBase "run-$runStamp-$runSuffix"
[IO.Directory]::CreateDirectory($runRoot) | Out-Null
$runRoot = [IO.Path]::GetFullPath($runRoot)
$ownerToken = [guid]::NewGuid().ToString('N') + [guid]::NewGuid().ToString('N')
$ownerTokenHash = Get-StringSha256 $ownerToken
$utf8NoBom = [Text.UTF8Encoding]::new($false)

$headLines = @(& git -C $repo rev-parse --verify HEAD)
$headExit = $LASTEXITCODE
$statusLines = @(& git -C $repo --no-optional-locks status --porcelain=v1 --untracked-files=all)
$statusExit = $LASTEXITCODE
if ($headExit -ne 0 -or $headLines.Count -ne 1 -or $statusExit -ne 0) { throw 'source: unable to bind measurement to Git HEAD and worktree state.' }
$gitHead = ([string] $headLines[0]).Trim()
$gitStatusText = $statusLines -join "`n"
$payloadPath = Join-Path $applicationDirectory 'published-payload.json'
$payloadCommit = $null
$payloadAggregate = $null
if ([IO.File]::Exists($payloadPath)) {
    $payload = [IO.File]::ReadAllText($payloadPath) | ConvertFrom-Json -ErrorAction Stop
    $payloadCommit = [string] $payload.commit
    $payloadAggregate = [string] $payload.aggregateSha256
}
if ([string]::IsNullOrWhiteSpace($payloadCommit)) { throw 'source: published-payload.json with a source commit is required.' }
$payloadSourceCommit = $payloadCommit -replace '-dirty$', ''
if (-not [string]::Equals($payloadSourceCommit, $gitHead, [StringComparison]::OrdinalIgnoreCase)) {
    throw "source: stale build '$payloadCommit' does not match HEAD '$gitHead'; rerun the official IncrementalBuild stage."
}
$processor = Get-CimInstance Win32_Processor -ErrorAction Stop | Select-Object -First 1
$operatingSystem = Get-CimInstance Win32_OperatingSystem -ErrorAction Stop
$powerLines = @(& powercfg /getactivescheme 2>$null)
$powerExit = $LASTEXITCODE
$powerScheme = if ($powerExit -eq 0 -and $powerLines.Count -gt 0) { ($powerLines -join ' ').Trim() } else { 'unavailable' }
$backgroundLoad = Get-BackgroundTotalCpu

$fixtureBaseline = Join-Path $repo 'docs\evidence\clock-countdown-issue-301\fixtures\baseline.settings.json'
$fixtureCalendar = Join-Path $repo 'docs\evidence\clock-countdown-issue-301\fixtures\calendar.settings.json'
foreach ($fixture in @($fixtureBaseline, $fixtureCalendar)) {
    if (-not [IO.File]::Exists($fixture)) { throw "fixture: missing $fixture" }
}
$sourceBinding = [pscustomobject]@{
    GitHead = $gitHead
    GitDirty = $statusLines.Count -gt 0
    GitStatusPorcelainSha256 = Get-StringSha256 $gitStatusText
    ApplicationPath = $application
    ApplicationExeSha256 = (Get-FileHash -LiteralPath $application -Algorithm SHA256).Hash
    ApplicationDllSha256 = (Get-FileHash -LiteralPath $applicationDll -Algorithm SHA256).Hash
    PayloadManifestCommit = $payloadCommit
    PayloadAggregateSha256 = $payloadAggregate
    MeasurementScriptSha256 = (Get-FileHash -LiteralPath $PSCommandPath -Algorithm SHA256).Hash
    MeasurementModuleSha256 = (Get-FileHash -LiteralPath (Join-Path $PSScriptRoot 'Countdown.IdleProcessorMeasurement.psm1') -Algorithm SHA256).Hash
    MeasurementWrapperSha256 = (Get-FileHash -LiteralPath $wrapperPath -Algorithm SHA256).Hash
    BaselineFixtureSha256 = (Get-FileHash -LiteralPath $fixtureBaseline -Algorithm SHA256).Hash
    CalendarFixtureSha256 = (Get-FileHash -LiteralPath $fixtureCalendar -Algorithm SHA256).Hash
}
$environment = [pscustomobject]@{
    ObservedAtUtc = [DateTimeOffset]::UtcNow.ToString('o')
    OSDescription = [Runtime.InteropServices.RuntimeInformation]::OSDescription
    OSVersion = [string] $operatingSystem.Version
    OSBuild = [string] $operatingSystem.BuildNumber
    OSArchitecture = [string] [Runtime.InteropServices.RuntimeInformation]::OSArchitecture
    ProcessorName = ([string] $processor.Name).Trim()
    LogicalProcessorCount = [Environment]::ProcessorCount
    DotNetRuntime = [Runtime.InteropServices.RuntimeInformation]::FrameworkDescription
    PowerShellVersion = $PSVersionTable.PSVersion.ToString()
    ActivePowerScheme = $powerScheme
    DesktopName = $desktopName
    IsolatedDesktop = $true
    OwnedProcessPriority = 'BelowNormal'
    NetworkMode = 'loopback-bound-refusing-proxy'
    BackgroundLoad = $backgroundLoad
}
$counterContract = [pscustomobject]@{
    Provider = 'Windows Get-Counter PDH'
    CounterPaths = @('\Process(<PID-bound-instance>)\% Processor Time', '\Process(*)\ID Process (pre-sample resolution only)')
    SampleIntervalSeconds = 1
    RequestedSetCount = $SecondsPerTier + 3
    DiscardedInitialSetCount = 1
    MinimumValidSamples = 60
    MinimumObservedDurationSeconds = 60
    WarmupSeconds = 5
    SingleCoreDefinition = 'Process % Processor Time cooked value; 100 means one fully used logical processor. No division by logical processor count.'
    PidBinding = 'Before sampling, resolve exactly one process counter instance from Process(*)\ID Process. Sample only that instance; any mid-window app exit fails the tier, preventing reuse from being accepted.'
}
$launchIntent = [pscustomobject]@{
    SchemaVersion = 1
    Issue = 301
    OwnershipMode = 'task-scoped'
    OwnerTokenSha256 = $ownerTokenHash
    OwnerTokenIssuedAtUtc = [DateTimeOffset]::UtcNow.ToString('o')
    Application = $application
    WorkingDirectoryScope = "$runRoot\<tier>"
    ArgumentTemplate = 'managed wrapper config: --exit-after-ready-for-test --exit-after-ready-seconds <bounded> --instance-activation-test-state <tier> --settings-file <tier settings> --diagnostic-log-path <tier log>'
    ManagedWrapper = $wrapperPath
    TargetTiers = @(Get-IdleMeasurementTierDefinition)
    Readiness = 'runtime-status window-ready PID match and expected owned-window visibility'
    ReadinessTimeoutSeconds = 60
    ExpectedExitCode = 0
    ExpectedResult = 'at least 60 PID-bound samples spanning at least 60 seconds, stable visibility, unchanged temporary settings, automatic test exit'
    Lifetime = 'task-scoped'
    OfficialIsolation = 'Invoke-CountdownSuiteLowImpact.ps1 -> Start-IsolatedSuiteProcess.ps1 -> owned Job with kill-on-close'
    AllowedCleanup = 'wait for test auto-exit; on failure or timeout the managed wrapper verifies identity, attempts bounded graceful stop, reverifies full identity, then may escalate; coordinator never kills by name or PID alone'
}
Write-MeasurementJson -Path (Join-Path $runRoot 'launch-intent.json') -Value $launchIntent

$report = [ordered]@{
    SchemaVersion = 1
    Issue = 301
    Status = 'running'
    RunId = [IO.Path]::GetFileName($runRoot)
    StartedAtUtc = [DateTimeOffset]::UtcNow.ToString('o')
    LastObservedAtUtc = [DateTimeOffset]::UtcNow.ToString('o')
    NextAction = 'measure five tiers sequentially'
    EvidenceRoot = $runRoot
    LaunchIntentPath = Join-Path $runRoot 'launch-intent.json'
    OwnerTokenSha256 = $ownerTokenHash
    Source = $sourceBinding
    Environment = $environment
    CounterContract = $counterContract
    RequestedSecondsPerTier = $SecondsPerTier
    Tiers = @()
    Error = $null
}
$reportPath = Join-Path $runRoot 'measurement.json'
Write-MeasurementJson -Path $reportPath -Value $report
$completedTiers = [System.Collections.Generic.List[object]]::new()
$refusingProxy = [Net.Sockets.Socket]::new([Net.Sockets.AddressFamily]::InterNetwork, [Net.Sockets.SocketType]::Stream, [Net.Sockets.ProtocolType]::Tcp)

try {
    $refusingProxy.Bind([Net.IPEndPoint]::new([Net.IPAddress]::Loopback, 0))
    $proxyAddress = "http://127.0.0.1:$($refusingProxy.LocalEndPoint.Port)"
    foreach ($tier in @(Get-IdleMeasurementTierDefinition)) {
        $tierRoot = Join-Path $runRoot $tier.Name
        $tierLogs = Join-Path $tierRoot 'logs'
        [IO.Directory]::CreateDirectory($tierLogs) | Out-Null
        $junction = Join-Path $tierRoot 'app'
        New-Item -ItemType Junction -Path $junction -Target $applicationDirectory -ErrorAction Stop | Out-Null
        $tierApplication = Join-Path $junction 'MinminCountdown.exe'
        $fixture = if ($tier.CalendarRule) { $fixtureCalendar } else { $fixtureBaseline }
        $settingsPath = Join-Path $tierRoot 'settings.json'
        [IO.File]::WriteAllBytes($settingsPath, [IO.File]::ReadAllBytes($fixture))
        $settingsHashBefore = (Get-FileHash -LiteralPath $settingsPath -Algorithm SHA256).Hash
        $diagnosticPath = Join-Path $tierLogs "application-$ownerTokenHash.jsonl"
        $runtimeStatusPath = Join-Path $tierRoot 'runtime-status.json'
        $processRecordPath = Join-Path $tierRoot 'process.json'
        $autoExitSeconds = $SecondsPerTier + 25
        $wrapperProcess = $null
        $applicationProcess = $null
        $wrapperStdoutTask = $null
        $wrapperStderrTask = $null
        $wrapperStartedAtUtc = $null
        $wrapperExecutablePath = $null
        $childReportPath = Join-Path $tierRoot 'child-report.json'
        $stopRequestPath = Join-Path $tierRoot 'stop.request'
        $tierError = $null
        $processRecord = [ordered]@{
            SchemaVersion = 2
            Tier = $tier.Name
            OwnershipMode = 'task-scoped'
            OwnerTokenSha256 = $ownerTokenHash
            OwnerTokenReport = 'managed wrapper child-report.json'
            OwnerTokenLaunchReportVerified = $false
            OwnerTokenReportVerified = $false
            OwnerTokenReportPath = $childReportPath
            OwnerTokenReportedByPid = $null
            OwnerTokenReportSha256 = $null
            ProcessId = $null
            ParentProcessId = $null
            CoordinatorProcessId = $PID
            StartedAtUtc = $null
            ExecutablePath = $null
            ApplicationExeSha256 = $sourceBinding.ApplicationExeSha256
            PriorityClass = $null
            WrapperProcessId = $null
            WrapperParentProcessId = $PID
            WrapperStartedAtUtc = $null
            WrapperExecutablePath = $null
            WrapperExecutableSha256 = $pwshHash
            WrapperExitCode = $null
            Readiness = 'pending'
            ProcessState = 'launching'
            ExpectedExitCode = 0
            ExitCode = $null
            ExitedAtUtc = $null
            CleanupMethod = $null
            CoordinatorStopRequestedAtUtc = $null
            GracefulStop = $null
            Escalation = $null
            TemporarySettingsPath = $settingsPath
            TemporarySettingsSha256Before = $settingsHashBefore
            TemporarySettingsSha256After = $null
            TemporarySettingsUnchanged = $false
            DiagnosticLogPath = $diagnosticPath
            DiagnosticEvents = @()
            Error = $null
        }
        Write-MeasurementJson -Path $processRecordPath -Value $processRecord
        try {
            $wrapperConfig = [pscustomobject]@{
                OwnerTokenSha256 = $ownerTokenHash
                Application = $tierApplication
                ApplicationArguments = @(
                    '--exit-after-ready-for-test',
                    '--exit-after-ready-seconds', ([string] $autoExitSeconds),
                    '--instance-activation-test-state', ([string] $tier.ActivationState),
                    '--settings-file', $settingsPath,
                    '--diagnostic-log-path', $diagnosticPath
                )
                WorkingDirectory = $tierRoot
                ReportPath = $childReportPath
                StopRequestPath = $stopRequestPath
                GracefulStopMode = 'WpfExternalClose'
                GracefulStopTimeoutSeconds = 10
                EscalationExitTimeoutSeconds = 5
                ExpectedApplicationExitCode = 0
                MaximumRunSeconds = $autoExitSeconds + 30
                ProxyAddress = $proxyAddress
                StandardOutputPath = Join-Path $tierRoot 'app.stdout.log'
                StandardErrorPath = Join-Path $tierRoot 'app.stderr.log'
            }
            $wrapperConfigPath = Join-Path $tierRoot 'wrapper-config.json'
            Write-MeasurementJson -Path $wrapperConfigPath -Value $wrapperConfig
            $startInfo = [Diagnostics.ProcessStartInfo]::new($pwshPath)
            $startInfo.WorkingDirectory = $tierRoot
            $startInfo.UseShellExecute = $false
            $startInfo.CreateNoWindow = $true
            $startInfo.RedirectStandardOutput = $true
            $startInfo.RedirectStandardError = $true
            $startInfo.StandardOutputEncoding = $utf8NoBom
            $startInfo.StandardErrorEncoding = $utf8NoBom
            foreach ($argument in @('-NoLogo', '-NoProfile', '-NonInteractive', '-File', $wrapperPath, '-ConfigurationPath', $wrapperConfigPath)) {
                $startInfo.ArgumentList.Add($argument)
            }
            $startInfo.Environment['MINMIN_IDLE_MEASUREMENT_OWNER_TOKEN'] = $ownerToken
            $wrapperProcess = [Diagnostics.Process]::Start($startInfo)
            if ($null -eq $wrapperProcess) { throw 'launch: managed wrapper start returned null.' }
            $wrapperStartedAtUtc = [DateTimeOffset]::new($wrapperProcess.StartTime).ToUniversalTime()
            $wrapperExecutablePath = Get-IdleMeasurementProcessImagePath -ProcessId $wrapperProcess.Id
            $wrapperStdoutTask = $wrapperProcess.StandardOutput.ReadToEndAsync()
            $wrapperStderrTask = $wrapperProcess.StandardError.ReadToEndAsync()
            $wrapperProcess.PriorityClass = [Diagnostics.ProcessPriorityClass]::BelowNormal
            $processRecord.WrapperProcessId = $wrapperProcess.Id
            $processRecord.WrapperStartedAtUtc = $wrapperStartedAtUtc.ToString('o')
            $processRecord.WrapperExecutablePath = $wrapperExecutablePath
            $processRecord.ProcessState = 'wrapper-running'
            Write-MeasurementJson -Path $processRecordPath -Value $processRecord

            $launchReport = Wait-ManagedChildReport -Path $childReportPath -WrapperProcess $wrapperProcess
            $applicationProcess = [Diagnostics.Process]::GetProcessById([int] $launchReport.Application.ProcessId)
            $applicationStartedAtUtc = [DateTimeOffset]::new($applicationProcess.StartTime).ToUniversalTime()
            $applicationExecutablePath = Get-IdleMeasurementProcessImagePath -ProcessId $applicationProcess.Id
            $reportBinding = @{
                ExpectedOwnerTokenSha256 = $ownerTokenHash
                CoordinatorProcessId = $PID
                ExpectedWrapperProcessId = $wrapperProcess.Id
                ExpectedWrapperStartedAtUtc = $wrapperStartedAtUtc
                ExpectedWrapperExecutablePath = $wrapperExecutablePath
                ExpectedWrapperExecutableSha256 = $pwshHash
                ExpectedApplicationProcessId = $applicationProcess.Id
                ExpectedApplicationStartedAtUtc = $applicationStartedAtUtc
                ExpectedApplicationExecutablePath = $applicationExecutablePath
                ExpectedApplicationExecutableSha256 = $sourceBinding.ApplicationExeSha256
            }
            if (-not (Test-IdleMeasurementChildReport -Report $launchReport @reportBinding)) {
                throw 'identity: managed wrapper launch report failed token, PID, start-time, executable hash or parent-chain verification.'
            }
            $processRecord.OwnerTokenLaunchReportVerified = $true
            $processRecord.ProcessId = $applicationProcess.Id
            $processRecord.ParentProcessId = $wrapperProcess.Id
            $processRecord.StartedAtUtc = $applicationStartedAtUtc.ToString('o')
            $processRecord.ExecutablePath = $applicationExecutablePath
            $processRecord.PriorityClass = $applicationProcess.PriorityClass.ToString()
            $processRecord.ProcessState = 'running'
            Write-MeasurementJson -Path $processRecordPath -Value $processRecord

            $receipt = Wait-OwnedRuntimeReady -Path $runtimeStatusPath -Process $applicationProcess
            $processRecord.Readiness = 'window-ready'
            if ($tier.Name -ceq 'main-only') {
                $bothVisible = Wait-ExpectedVisibility -OwnedProcessId $applicationProcess.Id -Tier ([pscustomobject]@{ FloatingVisible = $true; MainVisible = $true }) -Process $applicationProcess
                [MinminIdleMeasurementNative]::HideOwnedWindow([long] $bothVisible.FloatingHandle)
                $processRecord.VisibilityAdjustment = 'ShowWindow(SW_HIDE) on the exact owned floating HWND after MainVisible readiness'
            }
            $readyVisibility = Wait-ExpectedVisibility -OwnedProcessId $applicationProcess.Id -Tier $tier -Process $applicationProcess
            $processRecord.Readiness = 'window-ready-and-visibility-verified'
            $processRecord.VisibilityAtReady = $readyVisibility
            Write-MeasurementJson -Path $processRecordPath -Value $processRecord

            Start-Sleep -Seconds 5
            $counterBinding = Resolve-IdleCounterInstance -Process $applicationProcess -ExpectedProcessId $applicationProcess.Id
            $processRecord.CounterInstance = $counterBinding.InstanceName
            $processRecord.CounterInstanceResolutionAttempts = $counterBinding.Attempts
            $processRecord.CounterInstanceResolutionErrors = $counterBinding.Errors
            $cpuCounterPath = '\Process(' + $counterBinding.InstanceName + ')\% Processor Time'
            $visibilityAtStart = Wait-ExpectedVisibility -OwnedProcessId $applicationProcess.Id -Tier $tier -Process $applicationProcess
            $processorTimeBefore = $applicationProcess.TotalProcessorTime
            $wallBefore = [DateTimeOffset]::UtcNow
            $counterErrors = @()
            $counterSets = @(Get-Counter -Counter $cpuCounterPath -SampleInterval 1 -MaxSamples ($SecondsPerTier + 3) -ErrorAction SilentlyContinue -ErrorVariable counterErrors)
            $wallAfter = [DateTimeOffset]::UtcNow
            $processorTimeAfter = $applicationProcess.TotalProcessorTime
            $counterDiagnosticsPath = Join-Path $tierRoot 'counter-diagnostics.json'
            Write-IdleCounterDiagnostics -CounterSets $counterSets -ExpectedProcessId $applicationProcess.Id -Path $counterDiagnosticsPath -Errors $counterErrors -ExpectedInstanceName $counterBinding.InstanceName
            $processRecord.CounterDiagnosticsPath = $counterDiagnosticsPath
            $processRecord.CounterErrors = @($counterErrors | ForEach-Object { $_.Exception.Message } | Select-Object -Unique)
            if ($applicationProcess.HasExited) { throw "measurement: app exited during counter collection with code $($applicationProcess.ExitCode)." }
            $summary = ConvertTo-IdleProcessorSampleSummary -CounterSets $counterSets -ProcessId $applicationProcess.Id -CounterInstance $counterBinding.InstanceName -SkipInitialSets 1 -MinimumSeconds 60 -MinimumSamples 60
            $visibilityAtEnd = Wait-ExpectedVisibility -OwnedProcessId $applicationProcess.Id -Tier $tier -Process $applicationProcess
            $samplesPath = Join-Path $tierRoot 'samples.jsonl'
            $sampleLines = @($summary.Samples | ForEach-Object { $_ | ConvertTo-Json -Compress })
            [IO.File]::WriteAllLines($samplesPath, $sampleLines, $utf8NoBom)
            $summaryWithoutSamples = $summary | Select-Object -Property * -ExcludeProperty Samples
            $elapsedWallSeconds = ($wallAfter - $wallBefore).TotalSeconds
            $processorDeltaSeconds = ($processorTimeAfter - $processorTimeBefore).TotalSeconds
            $crossCheck = [pscustomobject]@{
                WallSeconds = [Math]::Round($elapsedWallSeconds, 3)
                TotalProcessorTimeDeltaSeconds = [Math]::Round($processorDeltaSeconds, 6)
                SingleCorePercent = [Math]::Round($processorDeltaSeconds / $elapsedWallSeconds * 100.0, 6)
            }
            $tierResult = [pscustomobject]@{
                Name = $tier.Name
                FloatingVisible = [bool] $tier.FloatingVisible
                MainVisible = [bool] $tier.MainVisible
                CalendarRule = [bool] $tier.CalendarRule
                FixtureSha256 = $settingsHashBefore
                CounterBinding = $counterBinding
                CounterErrors = @($counterErrors | ForEach-Object { $_.Exception.Message } | Select-Object -Unique)
                VisibilityAtStart = $visibilityAtStart
                VisibilityAtEnd = $visibilityAtEnd
                Summary = $summaryWithoutSamples
                TotalProcessorTimeCrossCheck = $crossCheck
                SamplesPath = $samplesPath
                SamplesSha256 = (Get-FileHash -LiteralPath $samplesPath -Algorithm SHA256).Hash
            }
            $processRecord.Summary = $summaryWithoutSamples
        } catch {
            $tierError = $_
            $processRecord.Error = $_.Exception.Message
        } finally {
            if ($null -ne $wrapperProcess) {
                try {
                    if ($null -eq $tierError -and -not $wrapperProcess.HasExited) {
                        [void] $wrapperProcess.WaitForExit(30000)
                    }
                    if (-not $wrapperProcess.HasExited) {
                        [IO.File]::WriteAllText($stopRequestPath, 'stop', $utf8NoBom)
                        $processRecord.CoordinatorStopRequestedAtUtc = [DateTimeOffset]::UtcNow.ToString('o')
                        [void] $wrapperProcess.WaitForExit(20000)
                    }
                    if (-not $wrapperProcess.HasExited) {
                        throw 'manual-review: managed wrapper did not finish bounded graceful stop and escalation; coordinator sent no kill and the official owned Job remains the safety boundary.'
                    }
                    $wrapperProcess.WaitForExit()
                    $processRecord.WrapperExitCode = $wrapperProcess.ExitCode
                    $wrapperStdout = if ($null -ne $wrapperStdoutTask) { $wrapperStdoutTask.GetAwaiter().GetResult() } else { '' }
                    $wrapperStderr = if ($null -ne $wrapperStderrTask) { $wrapperStderrTask.GetAwaiter().GetResult() } else { '' }
                    [IO.File]::WriteAllText((Join-Path $tierRoot 'wrapper.stdout.log'), $wrapperStdout, $utf8NoBom)
                    [IO.File]::WriteAllText((Join-Path $tierRoot 'wrapper.stderr.log'), $wrapperStderr, $utf8NoBom)
                    $finalChildReport = Read-ManagedChildReport -Path $childReportPath
                    if ($null -eq $finalChildReport) { throw 'identity: managed wrapper final report is missing or invalid.' }
                    $processRecord.OwnerTokenReportedByPid = $finalChildReport.Wrapper.ProcessId
                    $processRecord.OwnerTokenReportSha256 = (Get-FileHash -LiteralPath $childReportPath -Algorithm SHA256).Hash
                    $processRecord.GracefulStop = $finalChildReport.GracefulStop
                    $processRecord.Escalation = $finalChildReport.Escalation
                    if ($null -ne $applicationProcess -and $null -ne $finalChildReport.Application) {
                        $finalReportVerified = Test-IdleMeasurementChildReport -Report $finalChildReport @reportBinding
                        $processRecord.OwnerTokenReportVerified = $finalReportVerified -and $finalChildReport.Application.ProcessState -ceq 'exited'
                        $processRecord.ExitCode = $finalChildReport.Application.ExitCode
                        $processRecord.ProcessState = $finalChildReport.Application.ProcessState
                        $processRecord.ExitedAtUtc = $finalChildReport.Application.ExitedAtUtc
                        $processRecord.CleanupMethod = $finalChildReport.CleanupMethod
                        $processRecord.DiagnosticEvents = @(Read-OwnedDiagnosticEvents -Path $diagnosticPath -OwnedProcessId $applicationProcess.Id)
                        $processRecord.DiagnosticLogSha256 = (Get-FileHash -LiteralPath $diagnosticPath -Algorithm SHA256).Hash
                    } else {
                        $processRecord.ProcessState = 'manual-review-no-verified-application-report'
                    }
                    $processRecord.TemporarySettingsSha256After = (Get-FileHash -LiteralPath $settingsPath -Algorithm SHA256).Hash
                    $processRecord.TemporarySettingsUnchanged = $processRecord.TemporarySettingsSha256After -ceq $settingsHashBefore
                    if ($null -eq $tierError) {
                        $cleanupAccepted = $processRecord.CleanupMethod -in @('expected-auto-exit', 'graceful-wpfexternalclose')
                        if (-not $processRecord.OwnerTokenReportVerified -or -not $processRecord.OwnerTokenLaunchReportVerified -or
                            $wrapperProcess.ExitCode -ne 0 -or $processRecord.ProcessState -cne 'exited' -or
                            $processRecord.ExitCode -ne 0 -or -not $cleanupAccepted) {
                            $tierError = [System.Management.Automation.ErrorRecord]::new(
                                [InvalidOperationException]::new("cleanup: invalid managed completion; wrapper=$($wrapperProcess.ExitCode) app=$($processRecord.ExitCode) state=$($processRecord.ProcessState) method=$($processRecord.CleanupMethod) tokenVerified=$($processRecord.OwnerTokenReportVerified)"),
                                'IdleMeasurementCleanup', [System.Management.Automation.ErrorCategory]::OperationStopped, $null)
                            $processRecord.Error = $tierError.Exception.Message
                        }
                        if (-not $processRecord.TemporarySettingsUnchanged) {
                            $tierError = [System.Management.Automation.ErrorRecord]::new(
                                [InvalidOperationException]::new('settings: temporary settings bytes changed during idle measurement'),
                                'IdleMeasurementSettingsChanged', [System.Management.Automation.ErrorCategory]::OperationStopped, $null)
                            $processRecord.Error = $tierError.Exception.Message
                        }
                    }
                } catch {
                    if ($null -eq $tierError) { $tierError = $_ }
                    $processRecord.Error = @($processRecord.Error, $_.Exception.Message) | Where-Object { $_ } | Select-Object -Unique
                } finally {
                    if ($null -ne $applicationProcess) { $applicationProcess.Dispose() }
                    $wrapperProcess.Dispose()
                }
            }
            Remove-MeasurementJunction -Path $junction -ExpectedParent $tierRoot
            Write-MeasurementJson -Path $processRecordPath -Value $processRecord
        }
        if ($null -ne $tierError) { throw $tierError }
        $tierResult | Add-Member -NotePropertyName Process -NotePropertyValue ([pscustomobject] $processRecord)
        $completedTiers.Add($tierResult)
        $report.Tiers = @($completedTiers)
        $report.LastObservedAtUtc = [DateTimeOffset]::UtcNow.ToString('o')
        $report.NextAction = 'continue remaining tiers'
        Write-MeasurementJson -Path $reportPath -Value $report
    }
    if ($completedTiers.Count -ne 5) { throw "coverage: expected five tiers, got $($completedTiers.Count)." }
    $report.Status = 'complete'
    $report.NextAction = 'orchestrator writes the issue comment and runs the full five-stage gate'
    $report.FinishedAtUtc = [DateTimeOffset]::UtcNow.ToString('o')
    $report.LastObservedAtUtc = $report.FinishedAtUtc
    Write-MeasurementJson -Path $reportPath -Value $report
    $publicResult = [pscustomobject]@{
        Status = 'complete'
        MeasurementPath = $reportPath
        EvidenceRoot = $runRoot
        Tiers = @($completedTiers | ForEach-Object {
            [pscustomobject]@{
                Name = $_.Name
                MeanSingleCorePercent = $_.Summary.MeanSingleCorePercent
                MeanSingleCoreRatio = $_.Summary.MeanSingleCoreRatio
                Samples = $_.Summary.SampleCount
                DurationSeconds = $_.Summary.DurationSeconds
                P95SingleCorePercent = $_.Summary.P95SingleCorePercent
            }
        })
    }
    [Console]::Out.WriteLine(($publicResult | ConvertTo-Json -Depth 6 -Compress))
} catch {
    $report.Status = 'failed'
    $report.NextAction = 'inspect the failed tier process.json and stderr evidence; no process name based cleanup is allowed'
    $report.Error = $_.Exception.Message
    $report.FinishedAtUtc = [DateTimeOffset]::UtcNow.ToString('o')
    $report.LastObservedAtUtc = $report.FinishedAtUtc
    $report.Tiers = @($completedTiers)
    Write-MeasurementJson -Path $reportPath -Value $report
    [Console]::Error.WriteLine("Idle measurement failed: $($_.Exception.Message)")
    [Console]::Error.WriteLine("Evidence: $reportPath")
    exit 1
} finally {
    $refusingProxy.Dispose()
}
