#requires -Version 7.0
<#
.SYNOPSIS
Audits final source ownership for the US34/US41 validation extraction.
.DESCRIPTION
This is a representation guard for the explicitly declared extraction boundary. It does not
replace the CSharp behavior suite or the product-assembly audit performed there.
#>
[CmdletBinding()]
param(
    [string] $RepositoryRoot = (Join-Path $PSScriptRoot '..')
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = (Resolve-Path -LiteralPath $RepositoryRoot).Path
$productRoot = Join-Path $root 'src/MinminCountdown'
$runnerRoot = Join-Path $root 'src/MinminCountdown.WindowVerification'
$movedFileNames = @(
    'ApplicationValidation.cs',
    'VisualReferenceBaseline.cs',
    'WindowValidationInput.cs',
    'WindowValidationScenarios.cs',
    'WindowValidationScenarios.Density.cs',
    'MonitorGeometryValidation.cs',
    'DualWindowValidationFixture.cs',
    'KeyboardFocusPolicyFixtures.cs',
    'ValidationExercises.cs',
    'ValidationExercises.CalendarAndStartup.cs',
    'ValidationExercises.ConfirmationAndUndo.cs',
    'ValidationExercises.Density.cs',
    'ValidationExercises.DualWindowFixture.cs',
    'ValidationExercises.LayoutProbes.cs',
    'ValidationExercises.MainWindow.cs',
    'ValidationExercises.Narrator.cs',
    'ValidationExercises.NotesAndLifecycle.cs',
    'ValidationExercises.QualityGates.cs',
    'ValidationExercises.ReminderCenter.cs',
    'ValidationExercises.ReminderRules.cs',
    'ValidationExercises.Reminders.cs',
    'ValidationExercises.Types.cs',
    'ValidationExercises.VisualGates.cs',
    'ValidationReport.cs',
    'ValidationReport.Create.cs',
    'ValidationReport.Types.cs',
    'ValidationReport.ContrastAndControls.cs',
    'ValidationReport.FloatingFitAndTray.cs',
    'ValidationReport.FloatingReference.cs',
    'ValidationReport.FocusEvidence.cs',
    'ValidationReport.Imaging.cs',
    'ValidationReport.KeyboardAndDialogs.cs',
    'ValidationReport.MainReference.cs',
    'ValidationReport.NotesAndTheme.cs',
    'ValidationReport.SettingsAndReminders.cs',
    'ValidationReportContract.cs'
)
$failures = [System.Collections.Generic.List[string]]::new()
foreach ($fileName in $movedFileNames) {
    if (Test-Path -LiteralPath (Join-Path $productRoot $fileName) -PathType Leaf) {
        $failures.Add("Product still owns migrated validation source: $fileName")
    }
    if (-not (Test-Path -LiteralPath (Join-Path $runnerRoot $fileName) -PathType Leaf)) {
        $failures.Add("WindowVerification is missing migrated validation source: $fileName")
    }
}
foreach ($residual in Get-ChildItem -LiteralPath $productRoot -File -Filter '*.cs') {
    if ($residual.Name -match '^(?:ApplicationValidation|ValidationReport.*|ValidationExercises.*|WindowValidationScenarios.*)\.cs$' -and
        $residual.Name -notin $movedFileNames) {
        $failures.Add("Product owns an undeclared residual validation source: $($residual.Name)")
    }
}
$forbiddenProductReferences = @(
    'ApplicationValidation', 'ValidationReport', 'ValidationExercises',
    'WindowValidationScenarios', 'ValidationHostOptions', 'WindowValidationInput',
    'ValidationEntryPoint', '--validate-only', 'ValidateOnly'
)
foreach ($sourceFile in Get-ChildItem -LiteralPath $productRoot -File -Filter '*.cs') {
    $source = [System.IO.File]::ReadAllText($sourceFile.FullName, [System.Text.Encoding]::UTF8)
    foreach ($reference in $forbiddenProductReferences) {
        if ($source.Contains($reference, [System.StringComparison]::Ordinal)) {
            $failures.Add("Product source retains forbidden validation reference: $($sourceFile.Name):$reference")
        }
    }
}
foreach ($requiredRunnerFile in @('ValidationHostOptions.cs', 'ValidationEntryPoint.cs')) {
    if (-not (Test-Path -LiteralPath (Join-Path $runnerRoot $requiredRunnerFile) -PathType Leaf)) {
        $failures.Add("WindowVerification is missing its validation host component: $requiredRunnerFile")
    }
}
foreach ($requiredProductFile in @('SettingsEquivalenceCommand.cs', 'SettingsMigrationCommand.cs', 'RuntimeProtocol.cs')) {
    if (-not (Test-Path -LiteralPath (Join-Path $productRoot $requiredProductFile) -PathType Leaf)) {
        $failures.Add("Product lost required normal-startup/install capability: $requiredProductFile")
    }
}

$removedWindowMembers = [ordered]@{
    'MainWindow.xaml.cs' = @(
        'RunNoteAddExercise', 'RunReminderModeClickExercises', 'ProbeNoteDateBehavior',
        'ProbeWorkspaceDraftRetention', 'ProbeCrossDayReminderBehavior',
        'ProbeReminderSummaryAdvancesAfterFirstDue', 'RunValidationScenarios')
    'FloatingWindow.xaml.cs' = @(
        'RunDragInteractionExercise', 'RunManualPlacementExercise', 'RunCrossDayReminderAddExercise',
        'RunReminderModeClickExercises', 'RunDayThemeExercise', 'RunValidationScenarios')
}
foreach ($entry in $removedWindowMembers.GetEnumerator()) {
    $path = Join-Path $productRoot ([string] $entry.Key)
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        $failures.Add("Product window source is missing: $($entry.Key)")
        continue
    }
    $source = [System.IO.File]::ReadAllText($path, [System.Text.Encoding]::UTF8)
    foreach ($memberName in @($entry.Value)) {
        if ($source.Contains([string] $memberName, [System.StringComparison]::Ordinal)) {
            $failures.Add("Product window still owns validation scenario member: $($entry.Key):$memberName")
        }
    }
}

$result = [ordered]@{
    MovedFileCount = $movedFileNames.Count
    ProductResidualCount = @($failures | Where-Object { $_.StartsWith('Product still owns', [System.StringComparison]::Ordinal) }).Count
    RunnerMissingCount = @($failures | Where-Object { $_.StartsWith('WindowVerification', [System.StringComparison]::Ordinal) }).Count
    FailureCount = $failures.Count
    Failures = $failures.ToArray()
}
$result | ConvertTo-Json -Depth 5 -Compress
if ($failures.Count -gt 0) {
    exit 1
}
