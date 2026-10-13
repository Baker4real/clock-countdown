#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $AppPath,
    [Parameter(Mandatory)] [string] $ExpectedDllSha256
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
$watch = [Diagnostics.Stopwatch]::StartNew()
$report = [ordered]@{
    SchemaVersion=1; Valid=$false; Passed=0; Total=1; Failed=1
    FailureCode='InvalidExpectedHash'; Failures=@(); WindowChecksCompleted=0
    SessionId=[Diagnostics.Process]::GetCurrentProcess().SessionId
    DesktopName=$null; NativeWorkArea=$null; DllSha256=$null; AppPath=$AppPath
    FixtureDirectory=$null; Checks=@(); ElapsedMilliseconds=0
}
$previousTemp = $env:TEMP
$previousTmp = $env:TMP
try {
    if ($ExpectedDllSha256 -notmatch '^[0-9a-fA-F]{64}$') { throw 'ExpectedDllSha256 must contain 64 hexadecimal characters.' }
    $report.FailureCode = 'CandidateMissing'
    $dll = Join-Path $AppPath 'MinminCountdown.dll'
    if (-not (Test-Path -LiteralPath $dll -PathType Leaf)) { throw 'Candidate MinminCountdown.dll was not found.' }
    $dll = (Resolve-Path -LiteralPath $dll).Path
    $report.AppPath = Split-Path -Parent $dll
    $report.DllSha256 = (Get-FileHash -LiteralPath $dll -Algorithm SHA256).Hash
    $report.FailureCode = 'CandidateHashMismatch'
    if ($report.DllSha256 -cne $ExpectedDllSha256.ToUpperInvariant()) { throw 'Candidate DLL does not match the expected SHA-256.' }

    $report.FailureCode = 'DesktopNotAuthorized'
    Import-Module (Join-Path $PSScriptRoot 'Countdown.SuiteIsolation.psm1') -Force
    $report.DesktopName = Get-SuiteThreadDesktopName
    Assert-SuiteIsolatedDesktop -DesktopName $report.DesktopName
    $report.FailureCode = 'ApartmentNotSTA'
    if ([Threading.Thread]::CurrentThread.ApartmentState -ne 'STA') { throw 'Window preflight requires an STA process.' }

    $report.FailureCode = 'NativeEnvironmentUnavailable'
    Add-Type -TypeDefinition @'
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
public static class CountdownWindowPreflightNative
{
    [StructLayout(LayoutKind.Sequential)]
    public struct Rect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfoW(uint action, uint parameter, out Rect value, uint flags);
    public static Rect WorkArea()
    {
        if (!SystemParametersInfoW(0x0030, 0, out var area, 0))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        if (area.Right <= area.Left || area.Bottom <= area.Top)
            throw new InvalidOperationException("The native work area is empty.");
        return area;
    }
}
'@
    $area = [CountdownWindowPreflightNative]::WorkArea()
    $report.NativeWorkArea = [ordered]@{ Left=$area.Left; Top=$area.Top; Right=$area.Right; Bottom=$area.Bottom }

    $report.FailureCode = 'FixtureInitializationFailed'
    if (-not $report.AppPath.StartsWith('D:\', [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Candidate application must be hosted on D:.'
    }
    $fixtureDirectory = Join-Path (Split-Path -Parent $report.AppPath) ('logs/window-preflight/' + [guid]::NewGuid().ToString('N'))
    [void][IO.Directory]::CreateDirectory($fixtureDirectory)
    $report.FixtureDirectory = $fixtureDirectory
    $env:TEMP = $fixtureDirectory
    $env:TMP = $fixtureDirectory
    Add-Type -AssemblyName PresentationFramework, PresentationCore, WindowsBase, System.Xaml
    foreach ($name in @('Wpf.Ui.Abstractions.dll','Wpf.Ui.dll')) {
        [void][Reflection.Assembly]::LoadFrom((Join-Path $report.AppPath $name))
    }
    $assembly = [Reflection.Assembly]::LoadFrom($dll)
    if (-not [string]::Equals($assembly.Location, $dll, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'The loaded assembly is not the candidate DLL.'
    }
    $flags = [Reflection.BindingFlags]'Public,NonPublic,Static'
    $entry = $assembly.GetType('MinminCountdown.ValidationReport', $true).GetMethod('RunWindowPreflight', $flags)
    $report.FailureCode = 'ValidationEntryMissing'
    if ($null -eq $entry) { throw 'The candidate does not expose the window preflight validation entry.' }

    $report.FailureCode = 'FixtureInitializationFailed'
    if ($null -ne [Windows.Application]::Current) { throw 'The preflight requires its own WPF Application.' }
    $appType = $assembly.GetType('MinminCountdown.App', $true)
    $startup = $appType.GetMethod('OnStartup', [Reflection.BindingFlags]'NonPublic,Instance,DeclaredOnly',
        $null, [type[]]@([object],[Windows.StartupEventArgs]), $null)
    if ($null -eq $startup) { throw 'Cannot detach the production startup handler.' }
    $application = [Activator]::CreateInstance($appType)
    $handler = [Delegate]::CreateDelegate([Windows.StartupEventHandler], $application, $startup)
    try { $appType.GetMethod('InitializeComponent').Invoke($application, @()) | Out-Null }
    finally { $application.remove_Startup($handler) }
    $application.ShutdownMode = [Windows.ShutdownMode]::OnExplicitShutdown

    $report.FailureCode = 'WindowBehaviorFailed'
    $result = $entry.Invoke($null, @($application.Resources, [string]$report.AppPath))
    $report.Passed = [int]$result.Passed
    $report.Total = [int]$result.Total
    $report.Failed = [int]$result.Failed
    $report.WindowChecksCompleted = [int]$result.Total
    $report.Checks = @($result.Checks)
    $report.Failures = @($result.Failures)
    $report.Valid = $result.Passed -eq 2 -and $result.Total -eq 2 -and $result.Failed -eq 0
    if ($report.Valid) { $report.FailureCode = $null }
}
catch {
    $errorRecord = $_
    $exception = $errorRecord.Exception
    while ($null -ne $exception.InnerException) { $exception = $exception.InnerException }
    $report.Failures = @($exception.Message)
}
finally {
    $env:TEMP = $previousTemp
    $env:TMP = $previousTmp
    $watch.Stop()
    $report.ElapsedMilliseconds = $watch.ElapsedMilliseconds
}
[pscustomobject]$report | ConvertTo-Json -Depth 8
if (-not $report.Valid) { exit 1 }
exit 0
