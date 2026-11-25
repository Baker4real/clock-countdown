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
    [string] $RepositoryRoot = (Join-Path $PSScriptRoot '..'),
    [switch] $SelfTest
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

if ($SelfTest) {
    $selfTestRoot = Join-Path $PSScriptRoot ('../logs/validation-extraction-contract-test-' + [guid]::NewGuid().ToString('N'))
    $selfTestRoot = [IO.Path]::GetFullPath($selfTestRoot)
    $selfTestPwsh = (Get-Command pwsh.exe -CommandType Application | Select-Object -First 1).Source
    $selfTestScript = $PSCommandPath
    $utf8 = [Text.UTF8Encoding]::new($false)
    function New-ExtractionAuditFixture([string]$FixtureRoot) {
        $fixtureProduct = Join-Path $FixtureRoot 'src/MinminCountdown'
        $fixtureRunner = Join-Path $FixtureRoot 'src/MinminCountdown.WindowVerification'
        [void][IO.Directory]::CreateDirectory($fixtureProduct)
        [void][IO.Directory]::CreateDirectory($fixtureRunner)
        foreach ($fileName in @($movedFileNames) + @('ValidationHostOptions.cs', 'ValidationEntryPoint.cs')) {
            [IO.File]::WriteAllText((Join-Path $fixtureRunner $fileName), '// runner fixture', $utf8)
        }
        foreach ($fileName in @(
                'SettingsEquivalenceCommand.cs', 'SettingsMigrationCommand.cs', 'RuntimeProtocol.cs',
                'MainWindow.xaml.cs', 'FloatingWindow.xaml.cs')) {
            [IO.File]::WriteAllText((Join-Path $fixtureProduct $fileName), '// product fixture', $utf8)
        }
        foreach ($directoryName in @('bin', 'obj')) {
            $ignoredRoot = Join-Path $fixtureProduct $directoryName
            [void][IO.Directory]::CreateDirectory($ignoredRoot)
            [IO.File]::WriteAllText(
                (Join-Path $ignoredRoot 'ValidationEntryPoint.cs'),
                'internal sealed class ValidationEntryPoint { }',
                $utf8)
        }
    }
    function Invoke-ExtractionAuditFixture([string]$FixtureRoot) {
        $output = & $selfTestPwsh -NoLogo -NoProfile -NonInteractive -File $selfTestScript `
            -RepositoryRoot $FixtureRoot 2>&1 | Out-String
        [pscustomobject]@{ ExitCode = $LASTEXITCODE; Output = $output }
    }

    $validRoot = Join-Path $selfTestRoot 'valid'
    $nestedRoot = Join-Path $selfTestRoot 'nested'
    New-ExtractionAuditFixture $validRoot
    New-ExtractionAuditFixture $nestedRoot
    $nestedSource = Join-Path $nestedRoot 'src/MinminCountdown/Nested/State/MainWindowValidationScenariosRunner.cs'
    [void][IO.Directory]::CreateDirectory((Split-Path -Parent $nestedSource))
    [IO.File]::WriteAllText($nestedSource, 'internal sealed class MainWindowValidationScenariosRunner { }', $utf8)
    $validResult = Invoke-ExtractionAuditFixture $validRoot
    $nestedResult = Invoke-ExtractionAuditFixture $nestedRoot
    $checks = @(
        [pscustomobject]@{
            Name = 'valid-fixture-ignores-bin-obj'
            Passed = $validResult.ExitCode -eq 0
            Detail = $validResult.Output.Trim()
        },
        [pscustomobject]@{
            Name = 'nested-runner-type-is-rejected'
            Passed = $nestedResult.ExitCode -ne 0 -and
                $nestedResult.Output.Contains('ValidationScenariosRunner', [StringComparison]::Ordinal)
            Detail = $nestedResult.Output.Trim()
        }
    )
    $valid = @($checks | Where-Object { -not $_.Passed }).Count -eq 0
    [pscustomobject]@{ Valid = $valid; Checks = $checks; TestRoot = $selfTestRoot } |
        ConvertTo-Json -Depth 6
    if ($valid) {
        [IO.Directory]::Delete($selfTestRoot, $true)
        return
    }
    throw "Extraction audit self-test failed; fixture retained at $selfTestRoot"
}

$failures = [System.Collections.Generic.List[string]]::new()
$productSourceFiles = @(
    Get-ChildItem -LiteralPath $productRoot -File -Filter '*.cs' -Recurse |
        Where-Object {
            $relative = [IO.Path]::GetRelativePath($productRoot, $_.FullName).Replace('\', '/')
            $relative -notmatch '^(?:bin|obj)/'
        }
)
foreach ($fileName in $movedFileNames) {
    if (Test-Path -LiteralPath (Join-Path $productRoot $fileName) -PathType Leaf) {
        $failures.Add("Product still owns migrated validation source: $fileName")
    }
    if (-not (Test-Path -LiteralPath (Join-Path $runnerRoot $fileName) -PathType Leaf)) {
        $failures.Add("WindowVerification is missing migrated validation source: $fileName")
    }
}
foreach ($residual in $productSourceFiles) {
    if ($residual.Name -match '^(?:ApplicationValidation|ValidationReport.*|ValidationExercises.*|WindowValidationScenarios.*)\.cs$' -and
        $residual.Name -notin $movedFileNames) {
        $failures.Add("Product owns an undeclared residual validation source: $($residual.Name)")
    }
}
$forbiddenProductReferences = @(
    'ApplicationValidation', 'ValidationReport', 'ValidationExercises',
    'WindowValidationScenarios', 'ValidationScenariosRunner', 'ValidationHostOptions', 'WindowValidationInput',
    'ValidationEntryPoint', '--validate-only', 'ValidateOnly'
)
foreach ($sourceFile in $productSourceFiles) {
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
