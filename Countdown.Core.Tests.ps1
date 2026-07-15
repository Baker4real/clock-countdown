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
Assert-Equal '2 天' (Format-CountdownParts -Parts $parts -Precision Day) 'Day precision format'
Assert-Equal '2 天 01 小时' (Format-CountdownParts -Parts $parts -Precision Hour) 'Hour precision format'
Assert-Equal '2 天 01 小时 02 分钟' (Format-CountdownParts -Parts $parts -Precision Minute) 'Minute precision format'
Assert-Equal '2 天 01 小时 02 分钟 03 秒' (Format-CountdownParts -Parts $parts -Precision Second) 'Second precision format'

$elapsed = Get-CountdownParts -TargetUtc ([DateTimeOffset]'2026-07-15T10:00:00Z') -NowUtc ([DateTimeOffset]'2026-07-15T10:00:01Z')
Assert-Equal $true $elapsed.IsElapsed 'Elapsed state'
Assert-Equal '已到期' (Format-CountdownParts -Parts $elapsed -Precision Second) 'Elapsed format'

$workdayMidpoint = Get-WorkdayEarnings -MonthlySalary ([decimal]23000) -WorkStart ([TimeSpan]'09:00') -WorkEnd ([TimeSpan]'18:00') -BeijingNow ([DateTimeOffset]'2026-07-15T13:30:00+08:00')
Assert-Equal ([decimal]500) $workdayMidpoint 'Midpoint workday earnings'
Assert-Equal ([decimal]0) (Get-WorkdayEarnings -MonthlySalary ([decimal]23000) -WorkStart ([TimeSpan]'09:00') -WorkEnd ([TimeSpan]'18:00') -BeijingNow ([DateTimeOffset]'2026-07-15T08:59:59+08:00')) 'Before work earnings'
Assert-Equal ([decimal]0) (Get-WorkdayEarnings -MonthlySalary ([decimal]23000) -WorkStart ([TimeSpan]'09:00') -WorkEnd ([TimeSpan]'18:00') -BeijingNow ([DateTimeOffset]'2026-07-18T13:30:00+08:00')) 'Weekend earnings'
$statutoryCalendar = @{ '2026-01-01' = $false; '2026-01-04' = $true }
Assert-Equal ([decimal]0) (Get-WorkdayEarnings -MonthlySalary ([decimal]22000) -WorkStart ([TimeSpan]'09:00') -WorkEnd ([TimeSpan]'18:00') -BeijingNow ([DateTimeOffset]'2026-01-01T13:30:00+08:00') -WorkdayMap $statutoryCalendar) 'Statutory paid leave earnings'
Assert-Equal ([decimal]500) (Get-WorkdayEarnings -MonthlySalary ([decimal]22000) -WorkStart ([TimeSpan]'09:00') -WorkEnd ([TimeSpan]'18:00') -BeijingNow ([DateTimeOffset]'2026-01-04T13:30:00+08:00') -WorkdayMap $statutoryCalendar) 'Compensatory workday earnings'
$invalidWorkHoursRejected = $false
try { Get-WorkdayEarnings -MonthlySalary ([decimal]1) -WorkStart ([TimeSpan]'18:00') -WorkEnd ([TimeSpan]'09:00') -BeijingNow ([DateTimeOffset]'2026-07-15T13:30:00+08:00') | Out-Null }
catch { $invalidWorkHoursRejected = $true }
Assert-Equal $true $invalidWorkHoursRejected 'Invalid work hours rejected'

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

[pscustomobject]@{ Passed = 20; Failed = 0; TimeZone = (Get-BeijingNow).Offset.ToString() } | ConvertTo-Json
