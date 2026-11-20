#requires -Version 7.0
[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'Countdown.IdleProcessorMeasurement.psm1') -Force

$failures = [System.Collections.Generic.List[string]]::new()
$checks = [System.Collections.Generic.List[object]]::new()

function Add-MeasurementCheck {
    param(
        [Parameter(Mandatory)] [string] $Name,
        [Parameter(Mandatory)] [bool] $Passed,
        [string] $Detail = ''
    )
    $checks.Add([pscustomobject]@{ Name = $Name; Passed = $Passed; Detail = $Detail })
    if (-not $Passed) { $failures.Add($Name + $(if ($Detail) { ': ' + $Detail } else { '' })) }
}

function New-CounterSet {
    param(
        [Parameter(Mandatory)] [DateTimeOffset] $Timestamp,
        [Parameter(Mandatory)] [int] $ProcessId,
        [Parameter(Mandatory)] [double] $SingleCorePercent,
        [string] $InstanceName = 'minmincountdown'
    )
    [pscustomobject]@{
        Timestamp = $Timestamp.UtcDateTime
        CounterSamples = @(
            [pscustomobject]@{
                Path = "\\measurement\process($InstanceName)\% processor time"
                InstanceName = $InstanceName
                Status = 0
                CookedValue = $SingleCorePercent
            },
            [pscustomobject]@{
                Path = "\\measurement\process($InstanceName)\id process"
                InstanceName = $InstanceName
                Status = 0
                CookedValue = $ProcessId
            }
        )
    }
}

function Format-SummaryDetail {
    param([Parameter(Mandatory)] $Summary)
    $Summary | Select-Object ProcessId, SampleCount, DurationSeconds, MeanSingleCorePercent, MeanSingleCoreRatio,
        MedianSingleCorePercent, P95SingleCorePercent, MinSingleCorePercent, MaxSingleCorePercent |
        ConvertTo-Json -Compress
}

try {
    $start = [DateTimeOffset]::Parse('2026-11-18T00:00:00Z')
    $sets = @(for ($index = 0; $index -lt 63; $index++) {
        $value = if ($index -eq 0) { 100.0 } elseif ($index % 2 -eq 0) { 2.0 } else { 0.0 }
        New-CounterSet -Timestamp $start.AddSeconds($index) -ProcessId 4242 -SingleCorePercent $value
    })
    $summary = ConvertTo-IdleProcessorSampleSummary -CounterSets $sets -ProcessId 4242 -SkipInitialSets 1
    Add-MeasurementCheck -Name 'summary:sample-count' -Passed ($summary.SampleCount -eq 62) -Detail (Format-SummaryDetail $summary)
    Add-MeasurementCheck -Name 'summary:duration' -Passed ($summary.DurationSeconds -eq 61) -Detail (Format-SummaryDetail $summary)
    Add-MeasurementCheck -Name 'summary:single-core-scale' -Passed ([Math]::Abs($summary.MeanSingleCorePercent - 1.0) -lt 0.000001 -and [Math]::Abs($summary.MeanSingleCoreRatio - 0.01) -lt 0.000001) -Detail (Format-SummaryDetail $summary)
    Add-MeasurementCheck -Name 'summary:discard-initial' -Passed ($summary.MaxSingleCorePercent -eq 2.0) -Detail (Format-SummaryDetail $summary)
    Add-MeasurementCheck -Name 'summary:raw-samples' -Passed ($summary.Samples.Count -eq 62 -and $summary.Samples[0].SingleCorePercent -eq 0.0) -Detail (Format-SummaryDetail $summary)
} catch {
    Add-MeasurementCheck -Name 'summary:known-input' -Passed $false -Detail $_.Exception.Message
}

try {
    $cpuOnlySets = @($sets | ForEach-Object {
        [pscustomobject]@{ Timestamp = $_.Timestamp; CounterSamples = @($_.CounterSamples[0]) }
    })
    $prebound = ConvertTo-IdleProcessorSampleSummary -CounterSets $cpuOnlySets -ProcessId 4242 -CounterInstance 'minmincountdown' -SkipInitialSets 1
    Add-MeasurementCheck -Name 'summary:prebound-cpu-only' -Passed ($prebound.SampleCount -eq 62 -and $prebound.MeanSingleCorePercent -eq 1.0) -Detail (Format-SummaryDetail $prebound)
} catch {
    Add-MeasurementCheck -Name 'summary:prebound-cpu-only' -Passed $false -Detail $_.Exception.Message
}

foreach ($case in @(
    @{ Name = 'reject-short-duration'; Count = 61; IntervalSeconds = 0.5; Expected = 'duration' },
    @{ Name = 'reject-insufficient-samples'; Count = 59; IntervalSeconds = 1.0; Expected = 'samples' },
    @{ Name = 'reject-wrong-pid'; Count = 63; IntervalSeconds = 1.0; Expected = 'samples'; WrongPid = $true }
)) {
    $caseStart = [DateTimeOffset]::Parse('2026-11-18T00:00:00Z')
    $caseSets = @(for ($index = 0; $index -lt $case.Count; $index++) {
        $pidValue = if ($case.ContainsKey('WrongPid') -and $case.WrongPid) { 9999 } else { 4242 }
        New-CounterSet -Timestamp $caseStart.AddSeconds($index * $case.IntervalSeconds) -ProcessId $pidValue -SingleCorePercent 4
    })
    $threw = $false
    $message = ''
    try {
        ConvertTo-IdleProcessorSampleSummary -CounterSets $caseSets -ProcessId 4242 | Out-Null
    } catch {
        $threw = $true
        $message = $_.Exception.Message
    }
    Add-MeasurementCheck -Name $case.Name -Passed ($threw -and $message.Contains($case.Expected)) -Detail $message
}

try {
    $tiers = @(Get-IdleMeasurementTierDefinition)
    Add-MeasurementCheck -Name 'tiers:five-required' -Passed ($tiers.Count -eq 5) -Detail ($tiers | ConvertTo-Json -Compress)
    $combinations = @($tiers | Where-Object -not CalendarRule | ForEach-Object { "$($_.FloatingVisible)/$($_.MainVisible)" } | Sort-Object)
    Add-MeasurementCheck -Name 'tiers:visibility-matrix' -Passed (($combinations -join ',') -ceq 'False/False,False/True,True/False,True/True') -Detail ($combinations -join ',')
    Add-MeasurementCheck -Name 'tiers:calendar-comparison' -Passed (@($tiers | Where-Object CalendarRule).Count -eq 1 -and [bool]($tiers | Where-Object CalendarRule).FloatingVisible -and -not [bool]($tiers | Where-Object CalendarRule).MainVisible) -Detail ($tiers | ConvertTo-Json -Compress)
} catch {
    Add-MeasurementCheck -Name 'tiers:definitions' -Passed $false -Detail $_.Exception.Message
}

try {
    $syntheticFixtureRoot = Join-Path ([IO.Path]::GetTempPath()) 'minmin-idle-measurement-unit'
    $syntheticWrapperPath = Join-Path $syntheticFixtureRoot 'pwsh.exe'
    $syntheticApplicationPath = Join-Path $syntheticFixtureRoot 'app.exe'
    $childReport = [pscustomobject]@{
        ReportRole = 'managed-child-wrapper'
        OwnerTokenSha256 = 'AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA'
        Wrapper = [pscustomobject]@{
            ProcessId = 100
            ParentProcessId = 50
            StartedAtUtc = '2026-11-19T00:00:00Z'
            ExecutablePath = $syntheticWrapperPath
            ExecutableSha256 = 'BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB'
        }
        Application = [pscustomobject]@{
            ProcessId = 200
            ParentProcessId = 100
            StartedAtUtc = '2026-11-19T00:00:01Z'
            ExecutablePath = $syntheticApplicationPath
            ExecutableSha256 = 'CCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCC'
        }
    }
    $reportBinding = @{
        ExpectedOwnerTokenSha256 = 'AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA'
        CoordinatorProcessId = 50
        ExpectedWrapperProcessId = 100
        ExpectedWrapperStartedAtUtc = [DateTimeOffset]::Parse('2026-11-19T00:00:00Z')
        ExpectedWrapperExecutablePath = $syntheticWrapperPath
        ExpectedWrapperExecutableSha256 = 'BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB'
        ExpectedApplicationProcessId = 200
        ExpectedApplicationStartedAtUtc = [DateTimeOffset]::Parse('2026-11-19T00:00:01Z')
        ExpectedApplicationExecutablePath = $syntheticApplicationPath
        ExpectedApplicationExecutableSha256 = 'CCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCC'
    }
    Add-MeasurementCheck -Name 'child-report:valid-binding' -Passed (Test-IdleMeasurementChildReport -Report $childReport @reportBinding)
    $wrongToken = $childReport | ConvertTo-Json -Depth 8 | ConvertFrom-Json
    $wrongToken.OwnerTokenSha256 = 'DDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDD'
    Add-MeasurementCheck -Name 'child-report:reject-token-mismatch' -Passed (-not (Test-IdleMeasurementChildReport -Report $wrongToken @reportBinding))
    $wrongParent = $childReport | ConvertTo-Json -Depth 8 | ConvertFrom-Json
    $wrongParent.Application.ParentProcessId = 999
    Add-MeasurementCheck -Name 'child-report:reject-parent-mismatch' -Passed (-not (Test-IdleMeasurementChildReport -Report $wrongParent @reportBinding))
    $missingHash = $childReport | ConvertTo-Json -Depth 8 | ConvertFrom-Json
    $missingHash.Application.PSObject.Properties.Remove('ExecutableSha256')
    Add-MeasurementCheck -Name 'child-report:reject-missing-hash' -Passed (-not (Test-IdleMeasurementChildReport -Report $missingHash @reportBinding))
} catch {
    Add-MeasurementCheck -Name 'child-report:binding-contract' -Passed $false -Detail $_.Exception.Message
}

$childIntegrationOutput = @(& pwsh -NoLogo -NoProfile -NonInteractive -File (Join-Path $PSScriptRoot 'Test-CountdownIdleMeasurementChild.ps1')) -join [Environment]::NewLine
$childIntegrationExit = $LASTEXITCODE
try {
    $childIntegration = $childIntegrationOutput | ConvertFrom-Json -Depth 8 -ErrorAction Stop
    Add-MeasurementCheck -Name 'child-report:managed-wrapper-integration' -Passed ($childIntegrationExit -eq 0 -and [bool] $childIntegration.Valid) -Detail "checks=$($childIntegration.Checks.Count); root=$($childIntegration.Root); failures=$($childIntegration.Failures -join ';')"
    $expectedTempPrefix = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
    $childRootFull = if ([string]::IsNullOrWhiteSpace([string] $childIntegration.Root)) { '' } else { [IO.Path]::GetFullPath([string] $childIntegration.Root) }
    $underTemp = -not [string]::IsNullOrWhiteSpace($childRootFull) -and $childRootFull.StartsWith($expectedTempPrefix, [StringComparison]::OrdinalIgnoreCase)
    Add-MeasurementCheck -Name 'child-report:scratch-under-temp' -Passed $underTemp -Detail "root=$childRootFull; expectedPrefix=$expectedTempPrefix"
    $reportedCleanedUp = $null -ne $childIntegration.PSObject.Properties['CleanedUp'] -and [bool] $childIntegration.CleanedUp
    $removedFromDisk = -not [string]::IsNullOrWhiteSpace($childRootFull) -and -not [IO.Directory]::Exists($childRootFull)
    Add-MeasurementCheck -Name 'child-report:scratch-cleaned-on-pass' -Passed ($childIntegrationExit -eq 0 -and [bool] $childIntegration.Valid -and $reportedCleanedUp -and $removedFromDisk) -Detail "cleanedUp=$reportedCleanedUp; exists=$([IO.Directory]::Exists($childRootFull)); root=$childRootFull"
} catch {
    Add-MeasurementCheck -Name 'child-report:managed-wrapper-integration' -Passed $false -Detail $_.Exception.Message
}

$report = [pscustomobject]@{
    Valid = $failures.Count -eq 0
    Checks = @($checks)
    Failures = @($failures)
}
[Console]::Out.WriteLine(($report | ConvertTo-Json -Depth 8 -Compress))
exit $(if ($report.Valid) { 0 } else { 1 })
