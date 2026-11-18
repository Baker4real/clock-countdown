#requires -Version 7.0
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-IdleMeasurementTierDefinition {
    [CmdletBinding()]
    param()

    @(
        [pscustomobject]@{ Name = 'both-hidden'; FloatingVisible = $false; MainVisible = $false; CalendarRule = $false; ActivationState = 'Hidden' }
        [pscustomobject]@{ Name = 'floating-only'; FloatingVisible = $true; MainVisible = $false; CalendarRule = $false; ActivationState = 'FloatingVisible' }
        [pscustomobject]@{ Name = 'main-only'; FloatingVisible = $false; MainVisible = $true; CalendarRule = $false; ActivationState = 'MainVisible' }
        [pscustomobject]@{ Name = 'both-visible'; FloatingVisible = $true; MainVisible = $true; CalendarRule = $false; ActivationState = 'MainVisible' }
        [pscustomobject]@{ Name = 'calendar-rule'; FloatingVisible = $true; MainVisible = $false; CalendarRule = $true; ActivationState = 'FloatingVisible' }
    )
}

function ConvertTo-IdleMeasurementUtc {
    param([Parameter(Mandatory)] $Value)

    if ($Value -is [DateTimeOffset]) {
        return ([DateTimeOffset] $Value).ToUniversalTime()
    }
    if ($Value -is [DateTime]) {
        $timestamp = [DateTime] $Value
        if ($timestamp.Kind -eq [DateTimeKind]::Unspecified) {
            $timestamp = [DateTime]::SpecifyKind($timestamp, [DateTimeKind]::Local)
        }
        return ([DateTimeOffset]::new($timestamp)).ToUniversalTime()
    }
    return ([DateTimeOffset]::Parse(
            [string] $Value,
            [Globalization.CultureInfo]::InvariantCulture,
            [Globalization.DateTimeStyles]::RoundtripKind)).ToUniversalTime()
}

function ConvertTo-IdleProcessorSampleSummary {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [AllowEmptyCollection()] [object[]] $CounterSets,
        [Parameter(Mandatory)] [int] $ProcessId,
        [ValidateRange(0, 1000)] [int] $SkipInitialSets = 0,
        [ValidateRange(1, 86400)] [int] $MinimumSeconds = 60,
        [ValidateRange(1, 86400)] [int] $MinimumSamples = 60
    )

    if ($ProcessId -le 0) { throw 'identity: ProcessId must be positive.' }
    $samples = [System.Collections.Generic.List[object]]::new()
    $selectedSets = @($CounterSets | Select-Object -Skip $SkipInitialSets)
    foreach ($set in $selectedSets) {
        $counterSamples = @($set.CounterSamples | Where-Object { $_.Status -eq 0 })
        $idSamples = @($counterSamples | Where-Object {
            [string] $_.Path -match '\\id process$' -and
            [Math]::Abs([double] $_.CookedValue - $ProcessId) -lt 0.5
        })
        if ($idSamples.Count -gt 1) {
            throw "identity: multiple process counter instances match PID $ProcessId."
        }
        if ($idSamples.Count -eq 0) { continue }
        $instanceName = [string] $idSamples[0].InstanceName
        $cpuSamples = @($counterSamples | Where-Object {
            [string] $_.Path -match '\\% processor time$' -and
            [string] $_.InstanceName -ceq $instanceName
        })
        if ($cpuSamples.Count -ne 1) { continue }
        $percent = [double] $cpuSamples[0].CookedValue
        $samples.Add([pscustomobject]@{
            TimestampUtc = (ConvertTo-IdleMeasurementUtc $set.Timestamp).ToString('o')
            SingleCorePercent = [Math]::Round($percent, 6)
            SingleCoreRatio = [Math]::Round($percent / 100.0, 8)
            CounterInstance = $instanceName
        })
    }

    $orderedSamples = @($samples | Sort-Object TimestampUtc)
    if ($orderedSamples.Count -lt $MinimumSamples) {
        throw "samples: only $($orderedSamples.Count) valid PID-bound samples; required $MinimumSamples."
    }
    $first = [DateTimeOffset]::Parse($orderedSamples[0].TimestampUtc)
    $last = [DateTimeOffset]::Parse($orderedSamples[-1].TimestampUtc)
    $duration = ($last - $first).TotalSeconds
    if ($duration -lt $MinimumSeconds) {
        throw "duration: measured $duration seconds; required $MinimumSeconds."
    }

    $values = [double[]] @($orderedSamples | ForEach-Object { [double] $_.SingleCorePercent })
    $statistics = $values | Measure-Object -Average -Maximum -Minimum
    $sortedValues = @($values | Sort-Object)
    $medianIndex = [int][Math]::Floor(($sortedValues.Count - 1) / 2.0)
    $p95Index = [Math]::Max(0, [int][Math]::Ceiling($sortedValues.Count * 0.95) - 1)
    $mean = [double] $statistics.Average
    [pscustomobject]@{
        ProcessId = $ProcessId
        SampleCount = $orderedSamples.Count
        CounterSetCount = $selectedSets.Count
        DiscardedInitialSetCount = [Math]::Min($SkipInitialSets, $CounterSets.Count)
        FirstSampleUtc = $first.ToUniversalTime().ToString('o')
        LastSampleUtc = $last.ToUniversalTime().ToString('o')
        DurationSeconds = [Math]::Round($duration, 3)
        MeanSingleCorePercent = [Math]::Round($mean, 6)
        MeanSingleCoreRatio = [Math]::Round($mean / 100.0, 8)
        MedianSingleCorePercent = [Math]::Round($sortedValues[$medianIndex], 6)
        P95SingleCorePercent = [Math]::Round($sortedValues[$p95Index], 6)
        MinSingleCorePercent = [Math]::Round([double] $statistics.Minimum, 6)
        MaxSingleCorePercent = [Math]::Round([double] $statistics.Maximum, 6)
        Samples = $orderedSamples
    }
}

Export-ModuleMember -Function Get-IdleMeasurementTierDefinition, ConvertTo-IdleProcessorSampleSummary
