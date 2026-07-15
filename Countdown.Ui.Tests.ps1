$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationFramework
Import-Module (Join-Path $PSScriptRoot 'Countdown.Core.psm1') -Force

$powerShellEngine = (Get-Command pwsh.exe -ErrorAction SilentlyContinue).Source
if ([string]::IsNullOrWhiteSpace($powerShellEngine)) {
    $powerShellEngine = 'C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe'
}

$picker = New-Object System.Windows.Controls.DatePicker
$picker.SelectedDate = [DateTime]'2026-08-22'
$actual = ConvertTo-CountdownDateText -SelectedDate $picker.SelectedDate

if ($actual -ne '2026-08-22') {
    throw "DatePicker selection formatting failed. Expected 2026-08-22, got $actual"
}

$validationJson = & $powerShellEngine `
    -NoLogo -NoProfile -NonInteractive -STA -File (Join-Path $PSScriptRoot '敏敏子倒计时.ps1') -ValidateOnly | Out-String
if ($LASTEXITCODE -ne 0) {
    throw "Application validation failed: $validationJson"
}
$validation = $validationJson | ConvertFrom-Json

if (-not $validation.DateInputReadOnly) { throw 'Date selection still accepts free-text input.' }
if (-not $validation.DatePickerButtonOpensCalendar) { throw 'Date picker calendar closes immediately after the drop-down button is clicked.' }
if ($validation.TimeDropDowns -ne 3) { throw "Expected three time drop-downs, got $($validation.TimeDropDowns)." }
if (-not $validation.HasSetNowButton) { throw 'Set-now action is missing.' }
if (-not $validation.HasCountdownList) { throw 'Multiple-countdown list is missing.' }
if (-not $validation.HasNameEditor) { throw 'Countdown name editor is missing.' }
if (-not $validation.FloatingWindowPresent) { throw 'Desktop floating countdown window is missing.' }
if (-not $validation.FloatingManagerButtonPresent) { throw 'Floating countdown manager action is missing.' }
if (-not $validation.HasPreferenceControls) { throw 'Floating precision or income settings controls are missing.' }
if (@($validation.TimeOptionCounts) -join ',' -ne '24,60,60') { throw "Unexpected time option counts: $(@($validation.TimeOptionCounts) -join ',')." }
if (-not $validation.LegacyMigrationSupported) { throw 'Legacy single-countdown settings are not migrated.' }
if (-not $validation.DuplicateSettingsRejected) { throw 'Duplicate persisted countdown names are not rejected.' }

$testSettingsPath = Join-Path $PSScriptRoot 'ui-test.settings.json'
try {
    [System.IO.File]::WriteAllText($testSettingsPath, (@{
        schemaVersion = 3
        timeZoneId = 'China Standard Time'
        displayPrecision = 'Second'
        monthlySalary = 23000
        workStart = '09:00'
        workEnd = '18:00'
        countdowns = @(
            @{ id = 'expired'; name = '已到期项目'; targetUtc = '2000-01-01T00:00:00Z' }
            @{ id = 'future'; name = '未来项目'; targetUtc = '2099-01-01T00:00:00Z' }
        )
    } | ConvertTo-Json -Depth 4), (New-Object System.Text.UTF8Encoding($false)))
    $settingsHashBeforeValidation = (Get-FileHash -Algorithm SHA256 -LiteralPath $testSettingsPath).Hash
    $listValidationJson = & $powerShellEngine `
        -NoLogo -NoProfile -NonInteractive -STA -File (Join-Path $PSScriptRoot '敏敏子倒计时.ps1') `
        -ValidateOnly -SettingsFile $testSettingsPath | Out-String
    if ($LASTEXITCODE -ne 0) { throw "Countdown-list validation failed: $listValidationJson" }
    $listValidation = $listValidationJson | ConvertFrom-Json
    if ($listValidation.LoadedCountdowns -ne 2) { throw "Expected two loaded countdowns, got $($listValidation.LoadedCountdowns)." }
    if (@($listValidation.SortedNames) -join ',' -ne '未来项目,已到期项目') { throw "Unexpected countdown order: $(@($listValidation.SortedNames) -join ',')." }
    if (-not $listValidation.PersistenceRoundTrip) { throw 'Schema v2 persistence round-trip failed.' }
    $settingsHashAfterValidation = (Get-FileHash -Algorithm SHA256 -LiteralPath $testSettingsPath).Hash
    if ($settingsHashAfterValidation -ne $settingsHashBeforeValidation) { throw 'ValidateOnly modified the supplied settings file.' }
    if (-not $listValidation.CardActionsBoundByTag) { throw 'Card edit/delete actions are not bound to their countdown ID.' }
    if ($listValidation.FloatingCountdownCount -ne 1) { throw "Expected one active floating countdown card, got $($listValidation.FloatingCountdownCount)." }
    if ((@($listValidation.FloatingCountdownTexts).Count -ne 1) -or $listValidation.FloatingCountdownTexts[0] -notmatch '天 .*小时 .*分钟 .*秒') { throw "Floating countdowns did not use the unified unit format: $(@($listValidation.FloatingCountdownTexts) -join ',')" }
    if ($listValidation.FloatingIncomeText -notmatch '今日已赚 ￥|节假日日历未就绪') { throw "Floating income text is missing: $($listValidation.FloatingIncomeText)" }
}
finally {
    Remove-Item -LiteralPath $testSettingsPath -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath "$testSettingsPath.tmp" -Force -ErrorAction SilentlyContinue
}

[pscustomobject]@{
    Passed = 18
    Failed = 0
    SelectedDateType = $picker.SelectedDate.GetType().FullName
    ApplicationValidation = $validation.Valid
} | ConvertTo-Json
