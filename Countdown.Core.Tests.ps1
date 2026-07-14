$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'Countdown.Core.psm1') -Force

$failures = [System.Collections.Generic.List[string]]::new()
function Assert-Equal($Expected, $Actual, [string] $Name) {
    if ($Expected -ne $Actual) {
        $failures.Add("$Name expected=[$Expected] actual=[$Actual]")
    }
}

$target = ConvertTo-CountdownTargetUtc -DateText '2026-07-15' -TimeText '18:00:00'
Assert-Equal '2026-07-15T10:00:00.0000000+00:00' $target.ToString('o') 'Beijing to UTC'

$parts = Get-CountdownParts -TargetUtc ([DateTimeOffset]'2026-07-17T11:02:03Z') -NowUtc ([DateTimeOffset]'2026-07-15T10:00:00Z')
Assert-Equal $false $parts.IsElapsed 'Future target state'
Assert-Equal 2 $parts.Days 'Days'
Assert-Equal 1 $parts.Hours 'Hours'
Assert-Equal 2 $parts.Minutes 'Minutes'
Assert-Equal 3 $parts.Seconds 'Seconds'

$elapsed = Get-CountdownParts -TargetUtc ([DateTimeOffset]'2026-07-15T10:00:00Z') -NowUtc ([DateTimeOffset]'2026-07-15T10:00:01Z')
Assert-Equal $true $elapsed.IsElapsed 'Elapsed state'

$invalidRejected = $false
try { ConvertTo-CountdownTargetUtc -DateText '2026-02-30' -TimeText '18:00:00' | Out-Null }
catch { $invalidRejected = $true }
Assert-Equal $true $invalidRejected 'Invalid date rejected'

$clearTestPath = Join-Path $PSScriptRoot 'clear-test.settings.json'
[System.IO.File]::WriteAllText($clearTestPath, '{}')
[System.IO.File]::WriteAllText("$clearTestPath.tmp", '{}')
$removed = Remove-CountdownSettings -SettingsPath $clearTestPath
Assert-Equal $true $removed 'Settings clear reports removal'
Assert-Equal $false (Test-Path -LiteralPath $clearTestPath) 'Settings file removed'
Assert-Equal $false (Test-Path -LiteralPath "$clearTestPath.tmp") 'Temporary settings file removed'

if ($failures.Count -gt 0) {
    $failures | ForEach-Object { Write-Error $_ }
    exit 1
}

[pscustomobject]@{ Passed = 11; Failed = 0; TimeZone = (Get-BeijingNow).Offset.ToString() } | ConvertTo-Json
