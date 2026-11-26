#requires -Version 7.0
[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$checks = [System.Collections.Generic.List[object]]::new()
function Add-WriteCheck {
    param([Parameter(Mandatory)] [string] $Name, [Parameter(Mandatory)] [bool] $Passed, [string] $Detail = '')
    $checks.Add([pscustomobject]@{ Name = $Name; Passed = $Passed; Detail = $Detail })
}
$wrapperPath = Join-Path $PSScriptRoot 'Invoke-CountdownIdleMeasurementChild.ps1'
$tokens = $null
$parseErrors = $null
$wrapperAst = [Management.Automation.Language.Parser]::ParseFile($wrapperPath, [ref] $tokens, [ref] $parseErrors)
$functionAst = $wrapperAst.Find({ param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'Write-ChildReport' }, $true)
. ([scriptblock]::Create($functionAst.Extent.Text))
$root = Join-Path ([IO.Path]::GetTempPath()) ('minmin-idle-report-write-' + [guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($root)
$path = Join-Path $root 'child-report.json'
$reader = $null
$persistentReader = $null
try {
    $report = [pscustomobject]@{ ReportedAtUtc = ''; ReportSequence = 0; Value = 'updated' }
    [IO.File]::WriteAllText($path, '{"Value":"old"}', [Text.UTF8Encoding]::new($false))
    $reader = [IO.File]::Open($path, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
    $probeCount = 0
    Write-ChildReport -Path $path -Report $report -RetryAttempts 3 -RetryDelayMilliseconds 1 -RetryProbe { $script:probeCount++; $reader.Dispose() }
    $actual = [IO.File]::ReadAllText($path) | ConvertFrom-Json
    Add-WriteCheck -Name 'sharing-violation-retried-after-reader-closes' -Passed ($probeCount -eq 1 -and $actual.Value -eq 'updated' -and $report.ReportSequence -eq 1) -Detail "probeCount=$probeCount; sequence=$($report.ReportSequence)"
    $persistentReader = [IO.File]::Open($path, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
    $persistentProbes = 0
    $persistentError = $null
    try {
        Write-ChildReport -Path $path -Report $report -RetryAttempts 3 -RetryDelayMilliseconds 1 -RetryProbe { $script:persistentProbes++ }
    } catch {
        $persistentError = $_.Exception.Message
    }
    Add-WriteCheck -Name 'persistent-sharing-violation-is-bounded' -Passed ($null -ne $persistentError -and $persistentProbes -eq 2) -Detail "probes=$persistentProbes; error=$persistentError"
} catch {
    Add-WriteCheck -Name 'report-write-fixture' -Passed $false -Detail $_.Exception.Message
} finally {
    if ($null -ne $reader) { $reader.Dispose() }
    if ($null -ne $persistentReader) { $persistentReader.Dispose() }
    if ([IO.Directory]::Exists($root)) { [IO.Directory]::Delete($root, $true) }
}
$report = [pscustomobject]@{ Valid = @($checks | Where-Object { -not $_.Passed }).Count -eq 0; Checks = @($checks) }
[Console]::Out.WriteLine(($report | ConvertTo-Json -Depth 8 -Compress))
exit $(if ($report.Valid) { 0 } else { 1 })
