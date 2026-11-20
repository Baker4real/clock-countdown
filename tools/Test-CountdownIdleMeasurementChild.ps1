#requires -Version 7.0
[CmdletBinding()]
param(
    [string] $ScratchRoot = [IO.Path]::GetTempPath()
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'Countdown.IdleProcessorMeasurement.psm1') -Force

$checks = [System.Collections.Generic.List[object]]::new()
$failures = [System.Collections.Generic.List[string]]::new()
function Add-ChildCheck {
    param([Parameter(Mandatory)] [string] $Name, [Parameter(Mandatory)] [bool] $Passed, [string] $Detail = '')
    $checks.Add([pscustomobject]@{ Name = $Name; Passed = $Passed; Detail = $Detail })
    if (-not $Passed) { $failures.Add($Name + $(if ($Detail) { ': ' + $Detail } else { '' })) }
}
function Get-TextSha256 {
    param([Parameter(Mandatory)] [string] $Value)
    [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($Value)))
}
function Read-ChildReport {
    param([Parameter(Mandatory)] [string] $Path)
    [IO.File]::ReadAllText($Path) | ConvertFrom-Json -Depth 20 -ErrorAction Stop
}
function Wait-ChildReport {
    param([Parameter(Mandatory)] [string] $Path, [Parameter(Mandatory)] $Wrapper)
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds(60)
    while (-not $Wrapper.HasExited -and [DateTimeOffset]::UtcNow -lt $deadline) {
        if ([IO.File]::Exists($Path)) {
            try {
                $candidate = Read-ChildReport -Path $Path
                if ($null -ne $candidate.Application) { return $candidate }
            } catch { }
        }
        Start-Sleep -Milliseconds 50
    }
    throw 'Wrapper did not write its application launch report within 60 seconds.'
}
function Invoke-WrapperFixture {
    param(
        [Parameter(Mandatory)] [string] $Mode,
        [Parameter(Mandatory)] [string] $Root,
        [Parameter(Mandatory)] [string] $WrapperPath,
        [Parameter(Mandatory)] [string] $FixturePath,
        [Parameter(Mandatory)] [string] $PwshPath,
        [Parameter(Mandatory)] [string] $PwshSha256
    )
    $caseRoot = Join-Path $Root $Mode
    [IO.Directory]::CreateDirectory($caseRoot) | Out-Null
    $reportPath = Join-Path $caseRoot 'child-report.json'
    $stopRequestPath = Join-Path $caseRoot 'stop.request'
    $gracefulSignalPath = Join-Path $caseRoot 'graceful.request'
    $ownerToken = [guid]::NewGuid().ToString('N') + [guid]::NewGuid().ToString('N')
    $ownerTokenHash = Get-TextSha256 $ownerToken
    $exitAfter = if ($Mode -eq 'normal') { 10 } else { 30 }
    $config = [pscustomobject]@{
        OwnerTokenSha256 = $ownerTokenHash
        Application = $PwshPath
        ApplicationArguments = @('-NoLogo', '-NoProfile', '-NonInteractive', '-File', $FixturePath, '-Mode', $Mode, '-GracefulSignalPath', $gracefulSignalPath, '-ExitAfterSeconds', ([string] $exitAfter))
        WorkingDirectory = $caseRoot
        ReportPath = $reportPath
        StopRequestPath = $stopRequestPath
        GracefulStopMode = 'FixtureFile'
        GracefulStopSignalPath = $gracefulSignalPath
        GracefulStopTimeoutSeconds = if ($Mode -eq 'graceful') { 5 } else { 1 }
        EscalationExitTimeoutSeconds = 5
        ExpectedApplicationExitCode = 0
    }
    $configPath = Join-Path $caseRoot 'config.json'
    [IO.File]::WriteAllText($configPath, ($config | ConvertTo-Json -Depth 10), [Text.UTF8Encoding]::new($false))
    $startInfo = [Diagnostics.ProcessStartInfo]::new($PwshPath)
    $startInfo.WorkingDirectory = $caseRoot
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.StandardOutputEncoding = [Text.UTF8Encoding]::new($false)
    $startInfo.StandardErrorEncoding = [Text.UTF8Encoding]::new($false)
    foreach ($argument in @('-NoLogo', '-NoProfile', '-NonInteractive', '-File', $WrapperPath, '-ConfigurationPath', $configPath)) {
        $startInfo.ArgumentList.Add($argument)
    }
    $startInfo.Environment['MINMIN_IDLE_MEASUREMENT_OWNER_TOKEN'] = $ownerToken
    $wrapper = [Diagnostics.Process]::Start($startInfo)
    $stdoutTask = $wrapper.StandardOutput.ReadToEndAsync()
    $stderrTask = $wrapper.StandardError.ReadToEndAsync()
    try {
        $wrapperStarted = [DateTimeOffset]::new($wrapper.StartTime).ToUniversalTime()
        $launchReport = Wait-ChildReport -Path $reportPath -Wrapper $wrapper
        $application = [Diagnostics.Process]::GetProcessById([int] $launchReport.Application.ProcessId)
        try {
            $applicationStarted = [DateTimeOffset]::new($application.StartTime).ToUniversalTime()
            $applicationPath = Get-IdleMeasurementProcessImagePath -ProcessId $application.Id
            $wrapperImagePath = Get-IdleMeasurementProcessImagePath -ProcessId $wrapper.Id
            $binding = @{
                ExpectedOwnerTokenSha256 = $ownerTokenHash
                CoordinatorProcessId = $PID
                ExpectedWrapperProcessId = $wrapper.Id
                ExpectedWrapperStartedAtUtc = $wrapperStarted
                ExpectedWrapperExecutablePath = $wrapperImagePath
                ExpectedWrapperExecutableSha256 = $PwshSha256
                ExpectedApplicationProcessId = $application.Id
                ExpectedApplicationStartedAtUtc = $applicationStarted
                ExpectedApplicationExecutablePath = $applicationPath
                ExpectedApplicationExecutableSha256 = $PwshSha256
            }
            Add-ChildCheck -Name "$Mode`:actual-os-token-and-identity-report" -Passed (Test-IdleMeasurementChildReport -Report $launchReport @binding) -Detail ($launchReport | ConvertTo-Json -Compress -Depth 8)
        } finally {
            $application.Dispose()
        }
        if ($Mode -ne 'normal') {
            [IO.File]::WriteAllText($stopRequestPath, 'stop', [Text.UTF8Encoding]::new($false))
        }
        if (-not $wrapper.WaitForExit(30000)) {
            $wrapper.Kill($true)
            $wrapper.WaitForExit(2000) | Out-Null
            throw 'Wrapper did not finish within 30 seconds; exact owned wrapper tree was stopped by the test harness.'
        }
        $wrapper.WaitForExit()
        $finalReport = Read-ChildReport -Path $reportPath
        $stderr = $stderrTask.GetAwaiter().GetResult()
        Add-ChildCheck -Name "$Mode`:wrapper-exit" -Passed ($wrapper.ExitCode -eq 0) -Detail $stderr
        return $finalReport
    } finally {
        if (-not $wrapper.HasExited) {
            $wrapper.Kill($true)
            $wrapper.WaitForExit(2000) | Out-Null
        }
        if ($wrapper.HasExited) {
            $wrapper.WaitForExit()
            [IO.File]::WriteAllText((Join-Path $caseRoot 'wrapper.stdout.log'), $stdoutTask.GetAwaiter().GetResult(), [Text.UTF8Encoding]::new($false))
            [IO.File]::WriteAllText((Join-Path $caseRoot 'wrapper.stderr.log'), $stderrTask.GetAwaiter().GetResult(), [Text.UTF8Encoding]::new($false))
        }
        $wrapper.Dispose()
    }
}

if ([string]::IsNullOrWhiteSpace($ScratchRoot)) {
    throw 'ScratchRoot must not be empty.'
}
$scratchBase = [IO.Path]::GetFullPath($ScratchRoot)
$rootPrefix = [IO.Path]::GetFullPath((Join-Path $scratchBase 'minmin-idle-child-selftest-'))
$root = [IO.Path]::GetFullPath($rootPrefix + [guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($root) | Out-Null
$pwshPath = (Get-Command pwsh -CommandType Application -ErrorAction Stop | Select-Object -First 1).Source
$pwshHash = (Get-FileHash -LiteralPath $pwshPath -Algorithm SHA256).Hash
$wrapperPath = Join-Path $PSScriptRoot 'Invoke-CountdownIdleMeasurementChild.ps1'
$fixturePath = Join-Path $PSScriptRoot 'Test-CountdownIdleMeasurementChildFixture.ps1'
$cleanedUp = $false
try {
    $normal = Invoke-WrapperFixture -Mode normal -Root $root -WrapperPath $wrapperPath -FixturePath $fixturePath -PwshPath $pwshPath -PwshSha256 $pwshHash
    Add-ChildCheck -Name 'normal:auto-exit' -Passed ($normal.Application.ExitCode -eq 0 -and $normal.CleanupMethod -ceq 'expected-auto-exit' -and $normal.Application.ProcessState -ceq 'exited') -Detail ($normal | ConvertTo-Json -Compress -Depth 8)
    $graceful = Invoke-WrapperFixture -Mode graceful -Root $root -WrapperPath $wrapperPath -FixturePath $fixturePath -PwshPath $pwshPath -PwshSha256 $pwshHash
    Add-ChildCheck -Name 'graceful:bounded-before-escalation' -Passed ($graceful.GracefulStop.Requested -and $graceful.GracefulStop.IdentityVerifiedBeforeRequest -and $graceful.GracefulStop.Exited -and -not $graceful.Escalation.Attempted -and $graceful.Application.ExitCode -eq 0) -Detail ($graceful | ConvertTo-Json -Compress -Depth 8)
    $escalated = Invoke-WrapperFixture -Mode ignore -Root $root -WrapperPath $wrapperPath -FixturePath $fixturePath -PwshPath $pwshPath -PwshSha256 $pwshHash
    Add-ChildCheck -Name 'escalation:reverify-after-graceful-timeout' -Passed (-not $escalated.GracefulStop.Exited -and $escalated.Escalation.Attempted -and $escalated.Escalation.IdentityReverified -and $escalated.Escalation.KillAttempted -and $escalated.Escalation.Exited -and $escalated.Application.ProcessState -ceq 'exited') -Detail ($escalated | ConvertTo-Json -Compress -Depth 8)
} catch {
    Add-ChildCheck -Name 'wrapper:integration' -Passed $false -Detail $_.Exception.Message
} finally {
    if ($failures.Count -eq 0 -and [IO.Directory]::Exists($root)) {
        if (-not $root.StartsWith($rootPrefix, [StringComparison]::OrdinalIgnoreCase)) {
            Add-ChildCheck -Name 'scratch:cleanup' -Passed $false -Detail "Unexpected cleanup path: $root"
        } else {
            $cleanupError = $null
            for ($attempt = 1; $attempt -le 5; $attempt++) {
                try {
                    [IO.Directory]::Delete($root, $true)
                    $cleanedUp = -not [IO.Directory]::Exists($root)
                    if ($cleanedUp) { $cleanupError = $null; break }
                } catch {
                    $cleanupError = $_.Exception.Message
                    if ($attempt -lt 5) { Start-Sleep -Milliseconds 100 }
                }
            }
            if (-not $cleanedUp) {
                Add-ChildCheck -Name 'scratch:cleanup' -Passed $false -Detail $(if ($cleanupError) { $cleanupError } else { "Directory still exists: $root" })
            }
        }
    }
}
$report = [pscustomobject]@{ Valid = $failures.Count -eq 0; Root = $root; CleanedUp = $cleanedUp; Checks = @($checks); Failures = @($failures) }
[Console]::Out.WriteLine(($report | ConvertTo-Json -Depth 8 -Compress))
exit $(if ($report.Valid) { 0 } else { 1 })
