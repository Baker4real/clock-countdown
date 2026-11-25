#requires -Version 7.0
[CmdletBinding()]
param(
    [string] $RunnerPath = (Join-Path (Split-Path -Parent $PSScriptRoot) 'window-verification/MinminCountdown.WindowVerification.exe')
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
Import-Module (Join-Path $PSScriptRoot 'Countdown.SuiteIsolation.psm1') -Force
Assert-SuiteIsolatedDesktop
$resolvedRunner = (Resolve-Path -LiteralPath $RunnerPath).Path
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$testRoot = Join-Path $repositoryRoot ('logs/validation-entrypoint-test-' + [guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($testRoot)
$utf8 = [Text.UTF8Encoding]::new($false)
$checks = [Collections.Generic.List[object]]::new()
$settingsJson = @'
{
  "schemaVersion": 10,
  "timeZoneId": "China Standard Time",
  "displayPrecision": "Minute",
  "monthlySalary": 0,
  "workStart": "09:00",
  "workEnd": "18:00",
  "themePreference": "Day",
  "tags": [],
  "countdowns": [],
  "notes": [],
  "reminderStates": {}
}
'@

function Invoke-RunnerProcess {
    param([string[]]$Arguments, [int]$TimeoutMilliseconds = 60000)
    $startInfo = [Diagnostics.ProcessStartInfo]::new($resolvedRunner)
    $startInfo.WorkingDirectory = $repositoryRoot
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.StandardOutputEncoding = $utf8
    $startInfo.StandardErrorEncoding = $utf8
    foreach ($argument in $Arguments) { $startInfo.ArgumentList.Add($argument) }
    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    try {
        if (-not $process.Start()) { throw '无法启动窗口验证 runner。' }
        $stdoutTask = $process.StandardOutput.ReadToEndAsync()
        $stderrTask = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit($TimeoutMilliseconds)) {
            $process.Kill($true)
            $process.WaitForExit()
            throw "窗口验证 runner 超过 $($TimeoutMilliseconds)ms 未退出。"
        }
        [pscustomobject]@{
            ExitCode = $process.ExitCode
            Stdout = $stdoutTask.GetAwaiter().GetResult()
            Stderr = $stderrTask.GetAwaiter().GetResult()
        }
    }
    finally { $process.Dispose() }
}

function Read-DiagnosticEntries([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { return @() }
    return @(Get-Content -LiteralPath $Path -Encoding UTF8 | ForEach-Object { $_ | ConvertFrom-Json })
}

function Add-Check([string]$Name, [bool]$Passed, [string]$Detail) {
    $checks.Add([pscustomobject]@{ Name = $Name; Passed = $Passed; Detail = $Detail })
}

try {
    $snapshotRoot = Join-Path $testRoot 'snapshot-read-failure'
    [void][IO.Directory]::CreateDirectory($snapshotRoot)
    $snapshotSettings = Join-Path $snapshotRoot 'settings.json'
    $snapshotDiagnostic = Join-Path $snapshotRoot 'diagnostic.jsonl'
    [IO.File]::WriteAllText($snapshotSettings, $settingsJson, $utf8)
    $snapshotHash = (Get-FileHash -LiteralPath $snapshotSettings -Algorithm SHA256).Hash
    $snapshotLock = [IO.File]::Open($snapshotSettings, [IO.FileMode]::Open, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
    try {
        $snapshotResult = Invoke-RunnerProcess -Arguments @(
            '--validate-only', '--settings-file', $snapshotSettings,
            '--diagnostic-log-path', $snapshotDiagnostic)
    }
    finally { $snapshotLock.Dispose() }
    $snapshotEntries = Read-DiagnosticEntries $snapshotDiagnostic
    $snapshotFatal = @($snapshotEntries | Where-Object event -eq 'FatalUnhandledException' | Select-Object -Last 1)
    $snapshotPassed =
        $snapshotResult.ExitCode -ne 0 -and
        $snapshotFatal.Count -eq 1 -and
        $snapshotFatal[0].exceptionType -eq 'System.IO.IOException' -and
        $snapshotResult.Stderr.Contains('System.IO.IOException', [StringComparison]::Ordinal) -and
        [string]::IsNullOrWhiteSpace($snapshotResult.Stdout) -and
        $snapshotHash -eq (Get-FileHash -LiteralPath $snapshotSettings -Algorithm SHA256).Hash
    Add-Check 'snapshot-read-failure-is-fatal-and-preserves-settings' $snapshotPassed (
        "exit=$($snapshotResult.ExitCode); fatal=$($snapshotFatal.Count); stderr=$($snapshotResult.Stderr.Trim())")

    $restoreRoot = Join-Path $testRoot 'restore-write-failure'
    [void][IO.Directory]::CreateDirectory($restoreRoot)
    $restoreSettings = Join-Path $restoreRoot 'settings.json'
    $restoreDiagnostic = Join-Path $restoreRoot 'diagnostic.jsonl'
    [IO.File]::WriteAllText($restoreSettings, $settingsJson, $utf8)
    $restoreResult = Invoke-RunnerProcess -Arguments @(
        '--validate-only', '--exercise-restore-write-failure',
        '--settings-file', $restoreSettings, '--diagnostic-log-path', $restoreDiagnostic)
    $restoreEntries = Read-DiagnosticEntries $restoreDiagnostic
    $restoreEvents = @($restoreEntries | ForEach-Object event)
    $restoreFatalIndex = [Array]::LastIndexOf($restoreEvents, 'FatalUnhandledException')
    $resourcesReleasedIndex = [Array]::LastIndexOf($restoreEvents, 'ValidationResourcesReleased')
    $restoreFatal = @($restoreEntries | Where-Object event -eq 'FatalUnhandledException' | Select-Object -Last 1)
    $restorePassed =
        $restoreResult.ExitCode -ne 0 -and
        $restoreFatal.Count -eq 1 -and
        $restoreFatal[0].exceptionType -eq 'System.IO.IOException' -and
        $restoreResult.Stderr.Contains('System.IO.IOException', [StringComparison]::Ordinal) -and
        $resourcesReleasedIndex -ge 0 -and
        $restoreFatalIndex -gt $resourcesReleasedIndex
    Add-Check 'restore-write-failure-is-caught-after-resource-cleanup' $restorePassed (
        "exit=$($restoreResult.ExitCode); events=$($restoreEvents -join ','); stderr=$($restoreResult.Stderr.Trim())")

    $dispatcherRoot = Join-Path $testRoot 'dispatcher-fatal'
    [void][IO.Directory]::CreateDirectory($dispatcherRoot)
    $dispatcherSettings = Join-Path $dispatcherRoot 'settings.json'
    $dispatcherDiagnostic = Join-Path $dispatcherRoot 'diagnostic.jsonl'
    [IO.File]::WriteAllText($dispatcherSettings, $settingsJson, $utf8)
    $dispatcherHash = (Get-FileHash -LiteralPath $dispatcherSettings -Algorithm SHA256).Hash
    $dispatcherResult = Invoke-RunnerProcess -Arguments @(
        '--validate-only', '--exercise-dispatcher-fatal-diagnostic',
        '--settings-file', $dispatcherSettings, '--diagnostic-log-path', $dispatcherDiagnostic)
    $dispatcherEntries = Read-DiagnosticEntries $dispatcherDiagnostic
    $dispatcherEvents = @($dispatcherEntries | ForEach-Object event)
    $dispatcherFatalIndex = [Array]::LastIndexOf($dispatcherEvents, 'FatalUnhandledException')
    $dispatcherReleaseIndex = [Array]::LastIndexOf($dispatcherEvents, 'ValidationResourcesReleased')
    $dispatcherFatal = @($dispatcherEntries | Where-Object event -eq 'FatalUnhandledException')
    $dispatcherPassed =
        $dispatcherResult.ExitCode -ne 0 -and
        $dispatcherFatal.Count -eq 1 -and
        $dispatcherFatal[0].exceptionType -eq 'System.InvalidOperationException' -and
        $dispatcherResult.Stderr.Contains('Synthetic dispatcher fatal diagnostic probe.', [StringComparison]::Ordinal) -and
        $dispatcherFatalIndex -ge 0 -and
        $dispatcherReleaseIndex -gt $dispatcherFatalIndex -and
        $dispatcherHash -eq (Get-FileHash -LiteralPath $dispatcherSettings -Algorithm SHA256).Hash -and
        [string]::IsNullOrWhiteSpace($dispatcherResult.Stdout)
    Add-Check 'dispatcher-fatal-is-handled-before-resource-cleanup' $dispatcherPassed (
        "exit=$($dispatcherResult.ExitCode); events=$($dispatcherEvents -join ','); stderr=$($dispatcherResult.Stderr.Trim())")

    $noteRoot = Join-Path $testRoot 'field-error-clear'
    [void][IO.Directory]::CreateDirectory($noteRoot)
    $noteSettings = Join-Path $noteRoot 'settings.json'
    $noteDiagnostic = Join-Path $noteRoot 'diagnostic.jsonl'
    [IO.File]::WriteAllText($noteSettings, $settingsJson, $utf8)
    $noteResult = Invoke-RunnerProcess -Arguments @(
        '--validate-only', '--exercise-note-add', '--settings-file', $noteSettings,
        '--diagnostic-log-path', $noteDiagnostic) -TimeoutMilliseconds 120000
    $noteReport = $null
    if (-not [string]::IsNullOrWhiteSpace($noteResult.Stdout)) {
        try { $noteReport = $noteResult.Stdout | ConvertFrom-Json }
        catch { $noteReport = $null }
    }
    $noteFact = $false
    if ($null -ne $noteReport -and $null -ne $noteReport.PSObject.Properties['NoteAddViaClick']) {
        $noteFact = [bool] $noteReport.NoteAddViaClick
    }
    $noteEntries = Read-DiagnosticEntries $noteDiagnostic
    $notePassed =
        $noteResult.ExitCode -eq 0 -and
        $noteFact -and
        @($noteEntries | Where-Object event -eq 'ValidationResourcesReleased').Count -eq 1
    Add-Check 'field-error-clear-collapses-rows-and-restores-layout' $notePassed (
        "exit=$($noteResult.ExitCode); fact=$noteFact; stderr=$($noteResult.Stderr.Trim())")
}
catch {
    Add-Check 'validation-entrypoint-probe-executed' $false $_.Exception.Message
}

$failed = @($checks | Where-Object { -not $_.Passed }).Count
$valid = $failed -eq 0 -and $checks.Count -eq 4
[pscustomobject]@{
    Valid = $valid
    Passed = @($checks | Where-Object Passed).Count
    Total = $checks.Count
    Failed = $failed + $(if ($checks.Count -ne 4) { 1 } else { 0 })
    TestRoot = $testRoot
    Checks = $checks.ToArray()
} | ConvertTo-Json -Depth 6
if ($valid) {
    [IO.Directory]::Delete($testRoot, $true)
    return
}
$failedDetails = @($checks | Where-Object { -not $_.Passed } | ForEach-Object { "$($_.Name): $($_.Detail)" }) -join ' | '
throw "Validation entry point probes failed; evidence retained at $testRoot; $failedDetails"
