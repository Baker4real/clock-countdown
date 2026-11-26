#requires -Version 7.0
[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'Countdown.VisualBaselineDpi.psm1') -Force
$checks = [System.Collections.Generic.List[object]]::new()
function Add-DpiCheck {
    param([Parameter(Mandatory)] [string] $Name, [Parameter(Mandatory)] [bool] $Passed, [string] $Detail = '')
    $checks.Add([pscustomobject]@{ Name = $Name; Passed = $Passed; Detail = $Detail })
}
$rejection = $null
try {
    [void](Assert-CountdownVisualBaselineDpi -ActualDpi 96 -ExpectedDpi 144)
} catch {
    $rejection = $_.Exception.Message
}
Add-DpiCheck -Name 'reject-96-before-run' -Passed ($null -ne $rejection -and $rejection.Contains('DPI=96') -and $rejection.Contains('150%') -and $rejection.Contains('未启动验证 runner')) -Detail $rejection
$accepted = Assert-CountdownVisualBaselineDpi -ActualDpi 144 -ExpectedDpi 144
Add-DpiCheck -Name 'accept-144' -Passed ([bool] $accepted.Valid -and $accepted.ActualDpi -eq 144) -Detail ($accepted | ConvertTo-Json -Compress)
$generatorPath = Join-Path (Split-Path -Parent $PSScriptRoot) 'tools/New-CountdownVisualBaseline.ps1'
$generator = [IO.File]::ReadAllText($generatorPath)
$guardIndex = $generator.IndexOf('Assert-CountdownVisualBaselineDpi', [StringComparison]::Ordinal)
$runDirectoryIndex = $generator.IndexOf('$runDirectory =', [StringComparison]::Ordinal)
Add-DpiCheck -Name 'generator-wired-before-run-directory' -Passed ($guardIndex -ge 0 -and $runDirectoryIndex -gt $guardIndex) -Detail "guardIndex=$guardIndex; runDirectoryIndex=$runDirectoryIndex"
$hostDpi = Get-CountdownVisualBaselineHostDpi
Add-DpiCheck -Name 'host-dpi-readable' -Passed ($hostDpi -gt 0) -Detail "hostDpi=$hostDpi"
$report = [pscustomobject]@{ Valid = @($checks | Where-Object { -not $_.Passed }).Count -eq 0; Checks = @($checks); HostDpi = $hostDpi }
[Console]::Out.WriteLine(($report | ConvertTo-Json -Depth 8 -Compress))
exit $(if ($report.Valid) { 0 } else { 1 })
