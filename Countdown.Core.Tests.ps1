#requires -Version 7.0

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'Countdown.Core.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'Countdown.Notes.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'Countdown.Settings.psm1') -Force

$failures = [System.Collections.Generic.List[string]]::new()
$script:assertions = 0
function Assert-Equal($Expected, $Actual, [string] $Name) {
    $script:assertions++
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

$legacyV3Settings = [pscustomobject]@{
    schemaVersion = 3
    timeZoneId = 'China Standard Time'
    displayPrecision = 'Minute'
    monthlySalary = 12345
    workStart = '08:30'
    workEnd = '17:30'
    countdowns = @([pscustomobject]@{ id = 'legacy'; name = '旧版倒计时'; targetUtc = '2099-01-01T00:00:00Z' })
}
$loadedLegacyV3 = ConvertFrom-CountdownSettingsObject -Settings $legacyV3Settings
Assert-Equal $true $loadedLegacyV3.Migrated 'Schema v3 settings migrate'
Assert-Equal 1 $loadedLegacyV3.Records.Count 'Schema v3 record count'
Assert-Equal 0 $loadedLegacyV3.Notes.Count 'Schema v3 creates empty notes'
Assert-Equal 'Minute' $loadedLegacyV3.Preferences.Precision 'Schema v3 preserves precision'
$canonicalLegacyV3 = ConvertTo-CountdownSettingsJson -State $loadedLegacyV3
$canonicalLegacyV3Object = $canonicalLegacyV3 | ConvertFrom-Json
Assert-Equal 4 $canonicalLegacyV3Object.schemaVersion 'Canonical settings schema'
Assert-Equal 0 @($canonicalLegacyV3Object.notes).Count 'Canonical settings notes'
$settingsCandidate = Copy-CountdownSettingsState -State $loadedLegacyV3
$settingsCandidate.Records[0].name = '候选倒计时'
$settingsCandidate.Preferences.MonthlySalary = 1
Assert-Equal '旧版倒计时' $loadedLegacyV3.Records[0].name 'Settings candidate does not mutate source record'
Assert-Equal 12345 $loadedLegacyV3.Preferences.MonthlySalary 'Settings candidate does not mutate source preferences'
$candidateSaveFailureObserved = $false
try {
    $unwritableCandidatePath = Join-Path (Join-Path $PSScriptRoot ([guid]::NewGuid().ToString('N'))) 'settings.json'
    Save-CountdownSettings -Path $unwritableCandidatePath -State $settingsCandidate
}
catch {
    $candidateSaveFailureObserved = $true
}
Assert-Equal $true $candidateSaveFailureObserved 'Settings candidate persistence failure is observable'
Assert-Equal '旧版倒计时' $loadedLegacyV3.Records[0].name 'Settings candidate persistence failure preserves source record'
Assert-Equal $true (Test-CountdownSettingsDeploymentTransition -BeforeHash 'same' -AfterHash 'same' -BeforeJson $null -AfterJson $null) 'Unchanged settings are accepted for deployment'
Assert-Equal $true (Test-CountdownSettingsDeploymentTransition -BeforeHash 'before' -AfterHash 'after' -BeforeJson ($legacyV3Settings | ConvertTo-Json -Depth 4) -AfterJson $canonicalLegacyV3) 'Strict schema v3 to v4 migration is accepted for deployment'
$legacyV1Settings = [pscustomobject]@{ schemaVersion = 1; timeZoneId = 'China Standard Time'; targetUtc = '2099-01-01T00:00:00Z' }
$canonicalLegacyV1 = ConvertTo-CountdownSettingsJson -State (ConvertFrom-CountdownSettingsObject -Settings $legacyV1Settings)
Assert-Equal $false (Test-CountdownSettingsDeploymentTransition -BeforeHash 'before' -AfterHash 'after' -BeforeJson ($legacyV1Settings | ConvertTo-Json -Depth 4) -AfterJson $canonicalLegacyV1) 'Schema v1 migration remains rejected for deployment'
$settingsModuleTestPath = Join-Path $PSScriptRoot 'settings-module-test.json'
try {
    Save-CountdownSettings -Path $settingsModuleTestPath -State $loadedLegacyV3
    $savedSettings = [System.IO.File]::ReadAllText($settingsModuleTestPath, [System.Text.Encoding]::UTF8) | ConvertFrom-Json
    Assert-Equal 4 $savedSettings.schemaVersion 'Settings module writes canonical schema'
    Assert-Equal $false (Test-Path -LiteralPath "$settingsModuleTestPath.tmp") 'Settings module cleans temporary file'
}
finally {
    Remove-Item -LiteralPath $settingsModuleTestPath -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath "$settingsModuleTestPath.tmp" -Force -ErrorAction SilentlyContinue
}

$originalNotes = [System.Collections.ArrayList]::new()
[void] $originalNotes.Add([pscustomobject]@{ id = 'existing'; text = '已有便签'; isCompleted = $false })
$persistedCandidates = [System.Collections.ArrayList]::new()
$addResult = Invoke-NoteMutation -Notes $originalNotes -Action Add -Text '新增便签' -PersistCandidate {
    param($candidate)
    [void] $persistedCandidates.Add($candidate)
}
Assert-Equal 1 $originalNotes.Count 'Note add does not mutate source before commit'
Assert-Equal 2 $addResult.Notes.Count 'Note add returns candidate notes'
Assert-Equal $true $addResult.Changed 'Note add reports change'
Assert-Equal 1 $persistedCandidates.Count 'Note add persists one candidate'
$completionResult = Invoke-NoteMutation -Notes $addResult.Notes -Action SetCompletion -Id 'existing' -IsCompleted $true -PersistCandidate {
    param($candidate)
    [void] $persistedCandidates.Add($candidate)
}
Assert-Equal $false ([bool] $addResult.Notes[0].isCompleted) 'Note completion does not mutate source before commit'
Assert-Equal $true ([bool] $completionResult.Notes[0].isCompleted) 'Note completion returns changed candidate'
$noOpPersistCalls = [pscustomobject]@{ Count = 0 }
$noOpResult = Invoke-NoteMutation -Notes $completionResult.Notes -Action SetCompletion -Id 'existing' -IsCompleted $true -PersistCandidate {
    param($candidate)
    $noOpPersistCalls.Count++
}
Assert-Equal $false $noOpResult.Changed 'Repeated note completion is a no-op'
Assert-Equal 0 $noOpPersistCalls.Count 'Repeated note completion does not persist'
$failedPersistCalls = [pscustomobject]@{ Count = 0 }
$persistFailureObserved = $false
try {
    Invoke-NoteMutation -Notes $completionResult.Notes -Action Remove -Id 'existing' -PersistCandidate {
        param($candidate)
        $failedPersistCalls.Count++
        throw 'Simulated settings write failure.'
    } | Out-Null
}
catch {
    $persistFailureObserved = $true
}
Assert-Equal $true $persistFailureObserved 'Note persistence failure is observable'
Assert-Equal 2 $completionResult.Notes.Count 'Note persistence failure preserves source notes'
Assert-Equal $true ([bool] $completionResult.Notes[0].isCompleted) 'Note persistence failure preserves source record state'
Assert-Equal 1 $failedPersistCalls.Count 'Note persistence failure invokes persistence once'

if ($failures.Count -gt 0) {
    $failures | ForEach-Object { Write-Error $_ }
    exit 1
}

[pscustomobject]@{ Passed = $script:assertions; Failed = 0; TimeZone = (Get-BeijingNow).Offset.ToString() } | ConvertTo-Json
