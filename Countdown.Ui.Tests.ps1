$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationFramework
Import-Module (Join-Path $PSScriptRoot 'Countdown.Core.psm1') -Force

$picker = New-Object System.Windows.Controls.DatePicker
$picker.SelectedDate = [DateTime]'2026-08-22'
$actual = ConvertTo-CountdownDateText -SelectedDate $picker.SelectedDate

if ($actual -ne '2026-08-22') {
    throw "DatePicker selection formatting failed. Expected 2026-08-22, got $actual"
}

[pscustomobject]@{ Passed = 1; Failed = 0; SelectedDateType = $picker.SelectedDate.GetType().FullName } | ConvertTo-Json
