#requires -Version 7.0
<#
.SYNOPSIS
    Measurement-tool-side managed wrapper. It reports the owner token and real child identity,
    then performs bounded graceful cleanup before identity-verified escalation.
#>
[CmdletBinding()]
param([Parameter(Mandatory)] [string] $ConfigurationPath)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false

function Write-ChildReport {
    param([Parameter(Mandatory)] [string] $Path, [Parameter(Mandatory)] $Report)
    $Report.ReportedAtUtc = [DateTimeOffset]::UtcNow.ToString('o')
    $Report.ReportSequence++
    $json = $Report | ConvertTo-Json -Depth 16
    $temporary = $Path + '.tmp-' + [guid]::NewGuid().ToString('N')
    [IO.File]::WriteAllText($temporary, $json, [Text.UTF8Encoding]::new($false))
    [IO.File]::Move($temporary, $Path, $true)
}
function Get-TextSha256 {
    param([Parameter(Mandatory)] [string] $Value)
    [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($Value)))
}
function Get-ProcessParentId {
    param([Parameter(Mandatory)] [int] $ProcessId)
    [MinminIdleMeasurementChildNative]::GetParentProcessId($ProcessId)
}
function Get-ExactProcessIdentity {
    param([Parameter(Mandatory)] $Process, [Parameter(Mandatory)] [int] $ParentProcessId)
    $processId = $Process.Id
    $startedAt = [DateTimeOffset]::new($Process.StartTime).ToUniversalTime()
    $path = [MinminIdleMeasurementChildNative]::GetProcessImagePath($processId)
    $actualParent = Get-ProcessParentId -ProcessId $processId
    if ($actualParent -ne $ParentProcessId) { throw "identity: PID $processId parent $actualParent does not match $ParentProcessId." }
    [pscustomobject]@{
        ProcessId = $processId
        ParentProcessId = $actualParent
        StartedAtUtc = $startedAt.ToString('o')
        ExecutablePath = $path
        ExecutableSha256 = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
    }
}
function Test-ExactProcessIdentity {
    param([Parameter(Mandatory)] $Process, [Parameter(Mandatory)] $Expected)
    try {
        if ($Process.HasExited) { return $false }
        $actual = Get-ExactProcessIdentity -Process $Process -ParentProcessId ([int] $Expected.ParentProcessId)
        return [int] $actual.ProcessId -eq [int] $Expected.ProcessId -and
            [DateTimeOffset]::Parse($actual.StartedAtUtc).ToUniversalTime() -eq [DateTimeOffset]::Parse($Expected.StartedAtUtc).ToUniversalTime() -and
            [string]::Equals($actual.ExecutablePath, $Expected.ExecutablePath, [StringComparison]::OrdinalIgnoreCase) -and
            [string]::Equals($actual.ExecutableSha256, $Expected.ExecutableSha256, [StringComparison]::OrdinalIgnoreCase)
    } catch {
        return $false
    }
}

if (-not ('MinminIdleMeasurementChildNative' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
public static class MinminIdleMeasurementChildNative
{
    public sealed record WindowRecord(long Handle, string Title, bool Visible);
    private delegate bool EnumWindowsProc(IntPtr window, IntPtr state);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr state);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr window, StringBuilder text, int maxCount);
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr window, uint command);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr window, int command);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr OpenProcess(uint access, bool inheritHandle, int processId);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool QueryFullProcessImageNameW(IntPtr process, int flags, StringBuilder path, ref int size);
    [DllImport("ntdll.dll")] private static extern int NtQueryInformationProcess(IntPtr handle, int informationClass, ref ProcessBasicInformation information, int length, ref int returnLength);
    [StructLayout(LayoutKind.Sequential)]
    public struct ProcessBasicInformation
    {
        public uint ExitStatus;
        public IntPtr PebBaseAddress;
        public UIntPtr AffinityMask;
        public int BasePriority;
        public UIntPtr UniqueProcessId;
        public UIntPtr InheritedFromUniqueProcessId;
    }
    public static string GetProcessImagePath(int processId)
    {
        var handle = OpenProcess(0x1000, false, processId);
        if (handle == IntPtr.Zero) throw new InvalidOperationException("OpenProcess failed: " + Marshal.GetLastWin32Error());
        try
        {
            var path = new StringBuilder(32768);
            var size = path.Capacity;
            if (!QueryFullProcessImageNameW(handle, 0, path, ref size))
                throw new InvalidOperationException("QueryFullProcessImageNameW failed: " + Marshal.GetLastWin32Error());
            return path.ToString();
        }
        finally { CloseHandle(handle); }
    }
    public static int GetParentProcessId(int processId)
    {
        var handle = OpenProcess(0x1000, false, processId);
        if (handle == IntPtr.Zero) throw new InvalidOperationException("OpenProcess failed: " + Marshal.GetLastWin32Error());
        try
        {
            var information = new ProcessBasicInformation();
            var returnLength = 0;
            var status = NtQueryInformationProcess(handle, 0, ref information, Marshal.SizeOf(typeof(ProcessBasicInformation)), ref returnLength);
            if (status != 0) throw new InvalidOperationException("NtQueryInformationProcess failed: " + status);
            return checked((int)information.InheritedFromUniqueProcessId.ToUInt64());
        }
        finally { CloseHandle(handle); }
    }
    public static WindowRecord[] GetWindows(int processId)
    {
        var result = new List<WindowRecord>();
        EnumWindows((window, state) =>
        {
            uint owner;
            GetWindowThreadProcessId(window, out owner);
            if (owner == (uint)processId)
            {
                var title = new StringBuilder(512);
                GetWindowText(window, title, title.Capacity);
                result.Add(new WindowRecord(window.ToInt64(), title.ToString(), IsWindowVisible(window)));
            }
            return true;
        }, IntPtr.Zero);
        return result.ToArray();
    }
    public static void Hide(long handle) { ShowWindow(new IntPtr(handle), 0); }
    public static long GetOwner(long handle) { return GetWindow(new IntPtr(handle), 4).ToInt64(); }
    public static bool PostClose(long handle) { return PostMessage(new IntPtr(handle), 0x0010, IntPtr.Zero, IntPtr.Zero); }
}
'@
}

function Request-GracefulStop {
    param(
        [Parameter(Mandatory)] $Config,
        [Parameter(Mandatory)] $Process,
        [Parameter(Mandatory)] $Identity,
        [Parameter(Mandatory)] $Report
    )
    $Report.GracefulStop.Requested = $true
    $Report.GracefulStop.RequestedAtUtc = [DateTimeOffset]::UtcNow.ToString('o')
    $Report.GracefulStop.IdentityVerifiedBeforeRequest = Test-ExactProcessIdentity -Process $Process -Expected $Identity
    Write-ChildReport -Path $Config.ReportPath -Report $Report
    if (-not $Report.GracefulStop.IdentityVerifiedBeforeRequest) {
        $Report.GracefulStop.Error = 'manual-review: identity mismatch before graceful stop; no stop signal sent.'
        return
    }
    $Report.GracefulStop.Attempted = $true
    if ($Config.GracefulStopMode -ceq 'FixtureFile') {
        [IO.File]::WriteAllText($Config.GracefulStopSignalPath, 'graceful', [Text.UTF8Encoding]::new($false))
        $Report.GracefulStop.SignalWritten = $true
    } elseif ($Config.GracefulStopMode -ceq 'WpfExternalClose') {
        $windows = @([MinminIdleMeasurementChildNative]::GetWindows($Process.Id))
        $floating = @($windows | Where-Object Title -ceq '敏敏子悬浮窗')
        $Report.GracefulStop.OwnedWindowCount = $windows.Count
        if ($floating.Count -eq 1) {
            $owner = [MinminIdleMeasurementChildNative]::GetOwner([long] $floating[0].Handle)
            $Report.GracefulStop.OwnerFound = $owner -ne 0
            foreach ($window in $windows) {
                if ($window.Visible) { [MinminIdleMeasurementChildNative]::Hide([long] $window.Handle) }
            }
            if ($owner -ne 0) {
                $Report.GracefulStop.ClosePosted = [MinminIdleMeasurementChildNative]::PostClose($owner)
            }
        } else {
            $Report.GracefulStop.Error = "Expected one owned floating window, found $($floating.Count)."
        }
    } else {
        throw "Unsupported graceful stop mode: $($Config.GracefulStopMode)"
    }
    Write-ChildReport -Path $Config.ReportPath -Report $Report
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds([int] $Config.GracefulStopTimeoutSeconds)
    while (-not $Process.HasExited -and [DateTimeOffset]::UtcNow -lt $deadline) {
        [void] $Process.WaitForExit(100)
    }
    $Report.GracefulStop.Exited = $Process.HasExited
    $Report.GracefulStop.FinishedAtUtc = [DateTimeOffset]::UtcNow.ToString('o')
    Write-ChildReport -Path $Config.ReportPath -Report $Report
}

function Stop-WithVerifiedEscalation {
    param(
        [Parameter(Mandatory)] $Config,
        [Parameter(Mandatory)] $Process,
        [Parameter(Mandatory)] $Identity,
        [Parameter(Mandatory)] $Report
    )
    if ($Process.HasExited) { return }
    $Report.Escalation.Attempted = $true
    $Report.Escalation.AttemptedAtUtc = [DateTimeOffset]::UtcNow.ToString('o')
    $Report.Escalation.IdentityReverified = Test-ExactProcessIdentity -Process $Process -Expected $Identity
    Write-ChildReport -Path $Config.ReportPath -Report $Report
    if (-not $Report.Escalation.IdentityReverified) {
        $Report.Escalation.Error = 'manual-review: identity mismatch after graceful timeout; no kill sent.'
        return
    }
    $Report.Escalation.KillAttempted = $true
    $Process.Kill($true)
    $Report.Escalation.Exited = $Process.WaitForExit(([int] $Config.EscalationExitTimeoutSeconds * 1000))
    $Report.Escalation.FinishedAtUtc = [DateTimeOffset]::UtcNow.ToString('o')
}

$configFullPath = [IO.Path]::GetFullPath($ConfigurationPath)
$config = [IO.File]::ReadAllText($configFullPath) | ConvertFrom-Json -Depth 20 -ErrorAction Stop
foreach ($required in @('OwnerTokenSha256', 'Application', 'WorkingDirectory', 'ReportPath', 'StopRequestPath', 'GracefulStopMode', 'GracefulStopTimeoutSeconds', 'EscalationExitTimeoutSeconds')) {
    if ($config.PSObject.Properties[$required] -isnot [System.Management.Automation.PSPropertyInfo] -or $null -eq $config.$required) {
        throw "config: missing $required"
    }
}
$ownerToken = [string] $env:MINMIN_IDLE_MEASUREMENT_OWNER_TOKEN
if ([string]::IsNullOrWhiteSpace($ownerToken)) { throw 'identity: owner token was not supplied through the environment channel.' }
$ownerTokenHash = Get-TextSha256 $ownerToken
if (-not [string]::Equals($ownerTokenHash, [string] $config.OwnerTokenSha256, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'identity: computed owner token SHA256 does not match launch intent.'
}
$workingDirectory = [IO.Path]::GetFullPath([string] $config.WorkingDirectory)
$reportPath = [IO.Path]::GetFullPath([string] $config.ReportPath)
$stopRequestPath = [IO.Path]::GetFullPath([string] $config.StopRequestPath)
foreach ($path in @($reportPath, $stopRequestPath)) {
    if (-not $path.StartsWith($workingDirectory.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) {
        throw "config: control path escapes working directory: $path"
    }
}
$config | Add-Member -NotePropertyName ReportPath -NotePropertyValue $reportPath -Force
$config | Add-Member -NotePropertyName StopRequestPath -NotePropertyValue $stopRequestPath -Force
$config | Add-Member -NotePropertyName WorkingDirectory -NotePropertyValue $workingDirectory -Force
if ($config.GracefulStopMode -ceq 'FixtureFile') {
    $gracefulPath = [IO.Path]::GetFullPath([string] $config.GracefulStopSignalPath)
    if (-not $gracefulPath.StartsWith($workingDirectory.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) {
        throw 'config: graceful fixture signal escapes working directory.'
    }
    $config | Add-Member -NotePropertyName GracefulStopSignalPath -NotePropertyValue $gracefulPath -Force
}
$wrapperProcess = [Diagnostics.Process]::GetCurrentProcess()
$wrapperIdentity = Get-ExactProcessIdentity -Process $wrapperProcess -ParentProcessId (Get-ProcessParentId -ProcessId $PID)
$wrapperIdentity | Add-Member -NotePropertyName ScriptPath -NotePropertyValue $PSCommandPath
$wrapperIdentity | Add-Member -NotePropertyName ScriptSha256 -NotePropertyValue (Get-FileHash -LiteralPath $PSCommandPath -Algorithm SHA256).Hash
$report = [pscustomobject]@{
    SchemaVersion = 1
    ReportRole = 'managed-child-wrapper'
    ReportSequence = 0
    ReportedAtUtc = $null
    OwnerTokenSha256 = $ownerTokenHash
    Wrapper = $wrapperIdentity
    Application = $null
    GracefulStop = [pscustomobject]@{
        Requested = $false; RequestedAtUtc = $null; IdentityVerifiedBeforeRequest = $false
        Attempted = $false; SignalWritten = $false; OwnedWindowCount = $null; OwnerFound = $false
        ClosePosted = $false; Exited = $false; FinishedAtUtc = $null; Error = $null
    }
    Escalation = [pscustomobject]@{
        Attempted = $false; AttemptedAtUtc = $null; IdentityReverified = $false
        KillAttempted = $false; Exited = $false; FinishedAtUtc = $null; Error = $null
    }
    CleanupMethod = $null
    Error = $null
}
Write-ChildReport -Path $reportPath -Report $report
$applicationProcess = $null
$applicationIdentity = $null
$stdoutTask = $null
$stderrTask = $null
$stopHandled = $false
try {
    $applicationPath = [IO.Path]::GetFullPath([string] $config.Application)
    $startInfo = [Diagnostics.ProcessStartInfo]::new($applicationPath)
    $startInfo.WorkingDirectory = $workingDirectory
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.StandardOutputEncoding = [Text.UTF8Encoding]::new($false)
    $startInfo.StandardErrorEncoding = [Text.UTF8Encoding]::new($false)
    foreach ($argument in @($config.ApplicationArguments)) { $startInfo.ArgumentList.Add([string] $argument) }
    $startInfo.Environment['MINMIN_IDLE_MEASUREMENT_OWNER_TOKEN'] = $ownerToken
    $startInfo.Environment['MINMIN_IDLE_MEASUREMENT_OWNER_TOKEN_SHA256'] = $ownerTokenHash
    if ($config.PSObject.Properties['ProxyAddress'] -and -not [string]::IsNullOrWhiteSpace([string] $config.ProxyAddress)) {
        foreach ($proxyVariable in @('HTTPS_PROXY', 'HTTP_PROXY', 'ALL_PROXY')) {
            $startInfo.Environment[$proxyVariable] = [string] $config.ProxyAddress
        }
        [void] $startInfo.Environment.Remove('NO_PROXY')
    }
    $applicationProcess = [Diagnostics.Process]::Start($startInfo)
    if ($null -eq $applicationProcess) { throw 'launch: managed child start returned null.' }
    $applicationIdentity = Get-ExactProcessIdentity -Process $applicationProcess -ParentProcessId $PID
    $applicationProcess.PriorityClass = [Diagnostics.ProcessPriorityClass]::BelowNormal
    $applicationIdentity | Add-Member -NotePropertyName PriorityClass -NotePropertyValue $applicationProcess.PriorityClass.ToString()
    $applicationIdentity | Add-Member -NotePropertyName ProcessState -NotePropertyValue 'running'
    $applicationIdentity | Add-Member -NotePropertyName ExitCode -NotePropertyValue $null
    $applicationIdentity | Add-Member -NotePropertyName ExitedAtUtc -NotePropertyValue $null
    $report.Application = $applicationIdentity
    $stdoutTask = $applicationProcess.StandardOutput.ReadToEndAsync()
    $stderrTask = $applicationProcess.StandardError.ReadToEndAsync()
    Write-ChildReport -Path $reportPath -Report $report

    $maximumRunSeconds = if ($config.PSObject.Properties['MaximumRunSeconds']) { [int] $config.MaximumRunSeconds } else { 120 }
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($maximumRunSeconds)
    while (-not $applicationProcess.HasExited) {
        if ([IO.File]::Exists($stopRequestPath) -or [DateTimeOffset]::UtcNow -ge $deadline) {
            Request-GracefulStop -Config $config -Process $applicationProcess -Identity $applicationIdentity -Report $report
            $stopHandled = $true
            Stop-WithVerifiedEscalation -Config $config -Process $applicationProcess -Identity $applicationIdentity -Report $report
            break
        }
        [void] $applicationProcess.WaitForExit(100)
    }
    if (-not $stopHandled) { $report.CleanupMethod = 'expected-auto-exit' }
} catch {
    $report.Error = $_.Exception.Message
    if ($null -ne $applicationProcess -and $null -ne $applicationIdentity -and -not $applicationProcess.HasExited) {
        try {
            Request-GracefulStop -Config $config -Process $applicationProcess -Identity $applicationIdentity -Report $report
            $stopHandled = $true
            Stop-WithVerifiedEscalation -Config $config -Process $applicationProcess -Identity $applicationIdentity -Report $report
        } catch {
            $report.Error = @($report.Error, $_.Exception.Message) -join '; '
        }
    }
} finally {
    if ($null -ne $applicationProcess) {
        $applicationProcess.Refresh()
        if ($applicationProcess.HasExited) {
            $applicationProcess.WaitForExit()
            if ($null -ne $applicationIdentity) {
                $applicationIdentity.ExitCode = $applicationProcess.ExitCode
                $applicationIdentity.ProcessState = 'exited'
                $applicationIdentity.ExitedAtUtc = [DateTimeOffset]::UtcNow.ToString('o')
                if ($stopHandled) {
                    if ($report.Escalation.Exited) { $report.CleanupMethod = 'escalated-exact-owned-handle' }
                    elseif ($report.GracefulStop.Exited) { $report.CleanupMethod = 'graceful-' + $config.GracefulStopMode.ToLowerInvariant() }
                }
            }
        } elseif ($null -ne $applicationIdentity) {
            $applicationIdentity.ProcessState = 'manual-review-not-stopped'
        }
        if ($null -ne $stdoutTask -and $applicationProcess.HasExited) {
            $stdout = $stdoutTask.GetAwaiter().GetResult()
            $stderr = $stderrTask.GetAwaiter().GetResult()
            if ($config.PSObject.Properties['StandardOutputPath']) { [IO.File]::WriteAllText([string] $config.StandardOutputPath, $stdout, [Text.UTF8Encoding]::new($false)) }
            if ($config.PSObject.Properties['StandardErrorPath']) { [IO.File]::WriteAllText([string] $config.StandardErrorPath, $stderr, [Text.UTF8Encoding]::new($false)) }
        }
        $applicationProcess.Dispose()
    }
    $wrapperProcess.Dispose()
    Write-ChildReport -Path $reportPath -Report $report
}
if ($null -ne $report.Error -or $null -eq $report.Application -or $report.Application.ProcessState -cne 'exited') { exit 1 }
exit 0
