#requires -Version 7.0

[CmdletBinding()]
param(
    [switch] $ParentTransactionMutexHeld,
    [switch] $AcceptanceOnly,
    [switch] $ReleaseContractOnly,
    [switch] $ValidationOnly,
    [switch] $RemoteDeploymentOnly
)

# CI 环境检测
$isCIEnvironment = $env:GITHUB_ACTIONS -eq 'true' -or $env:CI -eq 'true'

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'Countdown.Core.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'Countdown.Notes.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'Countdown.Tags.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'Countdown.Settings.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'Countdown.Runtime.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'Countdown.InstallTransaction.psm1') -Force -DisableNameChecking
Import-Module (Join-Path $PSScriptRoot 'Countdown.TransactionAcceptance.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'Countdown.ReleaseContract.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'Countdown.Validation.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'Countdown.RemoteDeployment.psm1') -Force

$failures = [System.Collections.Generic.List[string]]::new()
$script:assertions = 0
function Assert-Equal($Expected, $Actual, [string] $Name) {
    $script:assertions++
    if ($Expected -ne $Actual) {
        $failures.Add("$Name expected=[$Expected] actual=[$Actual]")
    }
}

function Remove-CountdownReleaseContractTestTree {
    param([Parameter(Mandatory)] [string] $Path)

    $resolvedPath = [System.IO.Path]::GetFullPath($Path)
    $expectedParent = [System.IO.Path]::GetFullPath($PSScriptRoot).TrimEnd('\')
    if (-not [string]::Equals(
            [System.IO.Path]::GetDirectoryName($resolvedPath),
            $expectedParent,
            [System.StringComparison]::OrdinalIgnoreCase) -or
        -not [System.IO.Path]::GetFileName($resolvedPath).StartsWith(
            'release-contract-test-',
            [System.StringComparison]::Ordinal)) {
        throw "拒绝清理未受管的发布契约测试目录：$resolvedPath"
    }
    if (-not (Test-Path -LiteralPath $resolvedPath -PathType Container)) {
        return
    }

    foreach ($file in Get-ChildItem -LiteralPath $resolvedPath -File -Recurse) {
        Remove-Item -LiteralPath $file.FullName -Force
    }
    foreach ($directory in @(
            Get-ChildItem -LiteralPath $resolvedPath -Directory -Recurse |
                Sort-Object { $_.FullName.Length } -Descending
        )) {
        Remove-Item -LiteralPath $directory.FullName -Force
    }
    Remove-Item -LiteralPath $resolvedPath -Force
}

function Remove-CountdownIsolatedTestTree {
    param(
        [Parameter(Mandatory)] [string] $Path,
        [Parameter(Mandatory)] [string] $AllowedRoot
    )

    $resolvedPath = [System.IO.Path]::GetFullPath($Path)
    $resolvedRoot = [System.IO.Path]::GetFullPath($AllowedRoot).TrimEnd('\') + '\'
    if (-not $resolvedPath.StartsWith($resolvedRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "拒绝清理隔离测试根目录之外的路径：$resolvedPath"
    }
    if (-not (Test-Path -LiteralPath $resolvedPath -PathType Container)) {
        return
    }

    foreach ($file in Get-ChildItem -LiteralPath $resolvedPath -File -Recurse) {
        Remove-Item -LiteralPath $file.FullName -Force
    }
    foreach ($directory in @(
            Get-ChildItem -LiteralPath $resolvedPath -Directory -Recurse |
                Sort-Object { $_.FullName.Length } -Descending
        )) {
        Remove-Item -LiteralPath $directory.FullName -Force
    }
    Remove-Item -LiteralPath $resolvedPath -Force
}

function Get-CountdownValidationSettingsArtifactPaths {
    param([Parameter(Mandatory)] [string] $SettingsPath)

    @(
        $SettingsPath,
        "$SettingsPath.tmp",
        "$SettingsPath.backup1",
        "$SettingsPath.backup1.tmp",
        "$SettingsPath.backup1.rollback",
        "$SettingsPath.backup2",
        "$SettingsPath.backup2.tmp",
        "$SettingsPath.backup2.rollback"
    )
}

function Remove-CountdownValidationSettingsArtifactFixture {
    param([Parameter(Mandatory)] [string] $SettingsPath)

    foreach ($path in Get-CountdownValidationSettingsArtifactPaths -SettingsPath $SettingsPath) {
        if (Test-Path -LiteralPath $path -PathType Leaf) {
            Remove-Item -LiteralPath $path -Force
        }
    }
    foreach ($path in Get-CountdownValidationSettingsArtifactPaths -SettingsPath $SettingsPath) {
        if (Test-Path -LiteralPath $path -PathType Container) {
            Remove-Item -LiteralPath $path -Force
        }
    }
}

function Get-IndependentCountdownDiagnosticMutexName {
    param([Parameter(Mandatory)] [string] $Path)

    $normalizedPath = [System.IO.Path]::GetFullPath($Path).ToUpperInvariant()
    $hex = [Convert]::ToHexString(
        [System.Security.Cryptography.SHA256]::HashData(
            [System.Text.UTF8Encoding]::new($false).GetBytes($normalizedPath)))
    "Local\MinminCountdown.Diagnostic-$($hex.Substring(0, 32))"
}

if ($ValidationOnly -or (-not $AcceptanceOnly -and -not $ReleaseContractOnly -and -not $RemoteDeploymentOnly)) {
    $validationEvidenceRoot = 'D:\agents\run-evidence\migration-preflight-20260825T013156Z\clock-countdown-validation-tests'
    $validationTestRoot = Join-Path $validationEvidenceRoot ([guid]::NewGuid().ToString('N'))
    $utf8NoBom = [System.Text.UTF8Encoding]::new($false)
    $originalFixtureRecord = $env:COUNTDOWN_FAKE_DOTNET_RECORD
    $originalValidationOrder = $env:COUNTDOWN_VALIDATION_ORDER_PATH
    $originalFixtureFailure = $env:COUNTDOWN_FAKE_DOTNET_FAIL
    $originalDotnetCliHome = $env:DOTNET_CLI_HOME
    $originalValidationArtifactMode = $env:COUNTDOWN_VALIDATION_ARTIFACT_MODE
    $originalValidationArtifactRecord = $env:COUNTDOWN_VALIDATION_ARTIFACT_RECORD
    $originalValidationSiblingMarker = $env:COUNTDOWN_VALIDATION_SIBLING_MARKER
    $validationArtifactRecord = Join-Path $validationTestRoot 'application-settings-path.txt'
    $validationArtifactRoot = 'D:\Codex\.codex\tmp\clock-countdown-validation-suite'
    $validationSiblingMarker = Join-Path $validationArtifactRoot (
        'unrelated-sibling-' + [guid]::NewGuid().ToString('N') + '.marker')
    $validationSettingsArtifactFixture = @'
param(
    [switch] $ValidateOnly,
    [string] $SettingsFile,
    [string] $DiagnosticLogPath
)
if (-not [string]::IsNullOrWhiteSpace($env:COUNTDOWN_VALIDATION_ARTIFACT_MODE)) {
    [System.IO.File]::WriteAllText(
        $env:COUNTDOWN_VALIDATION_ARTIFACT_RECORD,
        $SettingsFile,
        [System.Text.UTF8Encoding]::new($false))
    $artifactPaths = @(
        $SettingsFile,
        "$SettingsFile.tmp",
        "$SettingsFile.backup1",
        "$SettingsFile.backup1.tmp",
        "$SettingsFile.backup1.rollback",
        "$SettingsFile.backup2",
        "$SettingsFile.backup2.tmp",
        "$SettingsFile.backup2.rollback"
    )
    foreach ($artifactPath in $artifactPaths) {
        if ($env:COUNTDOWN_VALIDATION_ARTIFACT_MODE -eq 'directory-backup1' -and
            $artifactPath -eq "$SettingsFile.backup1") {
            [void] [System.IO.Directory]::CreateDirectory($artifactPath)
        }
        else {
            [System.IO.File]::WriteAllText(
                $artifactPath,
                'validation artifact',
                [System.Text.UTF8Encoding]::new($false))
        }
    }
    [System.IO.File]::WriteAllText(
        $env:COUNTDOWN_VALIDATION_SIBLING_MARKER,
        'preserve sibling',
        [System.Text.UTF8Encoding]::new($false))
}
'@
    try {
        New-Item -ItemType Directory -Path $validationTestRoot -Force | Out-Null
        $validationApplicationReport = [ordered]@{
            Valid = $true
            ValidationReportContractBehavior = $true
            CalendarReminderBehavior = $true
            FloatingWindowPresent = $true
        }
        foreach ($fieldName in @(
                'MainReferenceDecorationsShareScaleAtMinimumAndWideSizes',
                'MainReferenceDecorationLayerOrderMatches',
                'MainWorkspaceResourcesFollowDayNightTheme',
                'MainDynamicCountdownCardsUseThemeResources',
                'MainDynamicNoteCardsUseThemeResources',
                'FloatingReferenceContentSafeAreaMatches',
                'FloatingReferenceBaselineGateIsolationBehavior',
                'FloatingReferenceFreshnessGateIsolationBehavior',
                'FloatingLongNotesBodyHasNoManualLineBreaks',
                'FloatingLongNotesNaturalWraps',
                'FloatingLongNotesUsesOnlyOuterScroller',
                'FloatingLongNotesOuterScrollable',
                'FloatingLongNotesTopBottomReachable',
                'FloatingLongNotesSafeAreaMatches',
                'FloatingLongNotesLayoutValid',
                'FloatingTitleUsesSemiBoldWeight',
                'FloatingNoteInputWraps',
                'FloatingNoteInputScrolls',
                'FloatingInitialWorkspaceStateMatches',
                'FloatingWorkspaceSwitchingStaysSynchronized',
                'FloatingCountdownSelectionSurvivesDayNightThemes',
                'FloatingNoteSearchScenariosPass',
                'NoteSearchSyncsFromSharedFilter',
                'FloatingQuickAddDayResourcesFollowLocalTheme',
                'FloatingQuickAddNightResourcesFollowLocalTheme',
                'FloatingQuickAddIndependentFromApplicationAutoTheme',
                'FloatingQuickAddThemeResourceIsolationBehavior',
                'FloatingQuickAddDayExpandedScrolledCaptureWritten',
                'FloatingQuickAddNightExpandedScrolledCaptureWritten',
                'FloatingNightPlushSourceAlphaBoundsMatch',
                'FloatingNightPlushEdgeBrightnessWithinLimit',
                'FloatingNightPlushEdgeSaturationWithinLimit',
                'FloatingNightPlushEdgeGlowGateIsolationBehavior',
                'FloatingSecondaryRemainingUnitSpacingMatches',
                'FloatingSecondaryRemainingLayoutMatches',
                'FloatingSecondaryRemainingGateIsolationBehavior',
                'FloatingScrollBarLocalStyleScoped',
                'FloatingScrollBarHitTargetUsable',
                'FloatingScrollBarVisualWeightProgression',
                'FloatingScrollBarUsesWarmThemeTokens',
                'FloatingScrollBarContentRemainsSafe',
                'FloatingScrollBarInputPathsWork',
                'FloatingScrollBarGateIsolationBehavior',
                'FloatingScrollBarDayNightStateCapturesReady',
                'UiQualityGateIsolationBehavior'
            )) {
            $validationApplicationReport[$fieldName] = $true
        }
        foreach ($baselineName in @('Main', 'Floating')) {
            $validationApplicationReport["${baselineName}ReferenceBaselineRequested"] = $false
            $validationApplicationReport["${baselineName}ReferenceBaselineManifestAvailable"] = $false
            $validationApplicationReport["${baselineName}ReferenceBaselineValidated"] = $false
            $validationApplicationReport["${baselineName}ReferenceBaselineCaptures"] = @()
        }
        $validationApplicationReport['MainDynamicCountdownCardSampleCount'] = 1
        $validationApplicationReport['MainDynamicNoteCardSampleCount'] = 1
        $validationApplicationReport['UnknownFutureField'] = [ordered]@{
            Preserved = 'future-compatible'
        }
        $validationApplicationJson = $validationApplicationReport | ConvertTo-Json -Compress

        function Copy-CountdownApplicationValidationReportFixture {
            $validationApplicationJson | ConvertFrom-Json
        }

        function Get-CountdownApplicationValidationReportFailure {
            param(
                [Parameter(Mandatory)] [object] $Report,
                [Parameter(Mandatory)] [bool] $MainManifestRequired,
                [Parameter(Mandatory)] [bool] $FloatingManifestRequired
            )

            try {
                Assert-CountdownApplicationValidationReport `
                    -Report $Report `
                    -MainManifestRequired $MainManifestRequired `
                    -FloatingManifestRequired $FloatingManifestRequired | Out-Null
                return $null
            }
            catch {
                return $_.Exception.Message
            }
        }

        $packageValidationReport = Copy-CountdownApplicationValidationReportFixture
        $assertedPackageValidationReport = Assert-CountdownApplicationValidationReport `
            -Report $packageValidationReport `
            -MainManifestRequired $false `
            -FloatingManifestRequired $false
        Assert-Equal `
            'future-compatible' `
            ([string] $assertedPackageValidationReport.UnknownFutureField.Preserved) `
            'Application validation report interface accepts and preserves unknown fields'

        $missingContractReport = Copy-CountdownApplicationValidationReportFixture
        $missingContractReport.PSObject.Properties.Remove('ValidationReportContractBehavior')
        Assert-Equal `
            '应用 validate-only 报告缺少布尔字段：ValidationReportContractBehavior' `
            (Get-CountdownApplicationValidationReportFailure `
                -Report $missingContractReport `
                -MainManifestRequired $false `
                -FloatingManifestRequired $false) `
            'Application validation report interface rejects a missing contract behavior field'

        $stringUiBooleanReport = Copy-CountdownApplicationValidationReportFixture
        $stringUiBooleanReport.MainReferenceDecorationsShareScaleAtMinimumAndWideSizes = 'true'
        Assert-Equal `
            '应用 validate-only 报告的 UI 质量门禁未通过：MainReferenceDecorationsShareScaleAtMinimumAndWideSizes' `
            (Get-CountdownApplicationValidationReportFailure `
                -Report $stringUiBooleanReport `
                -MainManifestRequired $false `
                -FloatingManifestRequired $false) `
            'Application validation report interface rejects a string UI boolean'

        $sourceTreeValidationReport = Copy-CountdownApplicationValidationReportFixture
        foreach ($baselineName in @('Main', 'Floating')) {
            $sourceTreeValidationReport."${baselineName}ReferenceBaselineRequested" = $true
            $sourceTreeValidationReport."${baselineName}ReferenceBaselineManifestAvailable" = $true
            $sourceTreeValidationReport."${baselineName}ReferenceBaselineValidated" = $true
        }
        $sourceTreeValidationReport.MainReferenceBaselineCaptures = @(
            1..12 | ForEach-Object { [pscustomobject]@{ CaptureId = "main-$_" } }
        )
        $sourceTreeValidationReport.FloatingReferenceBaselineCaptures = @(
            1..6 | ForEach-Object { [pscustomobject]@{ CaptureId = "floating-$_" } }
        )
        Assert-Equal `
            $true `
            ($null -ne (Assert-CountdownApplicationValidationReport `
                    -Report $sourceTreeValidationReport `
                    -MainManifestRequired $true `
                    -FloatingManifestRequired $true)) `
            'Application validation report interface accepts complete source-tree baselines'

        $partialManifestReport = $sourceTreeValidationReport | ConvertTo-Json -Depth 20 | ConvertFrom-Json
        $partialManifestReport.FloatingReferenceBaselineManifestAvailable = $false
        Assert-Equal `
            '主窗口与悬浮窗视觉 manifest 模式不一致。' `
            (Get-CountdownApplicationValidationReportFailure `
                -Report $partialManifestReport `
                -MainManifestRequired $true `
                -FloatingManifestRequired $true) `
            'Application validation report interface rejects a single available manifest'

        $unrequestedValidatedReport = $sourceTreeValidationReport | ConvertTo-Json -Depth 20 | ConvertFrom-Json
        $unrequestedValidatedReport.MainReferenceBaselineRequested = $false
        Assert-Equal `
            '应用 validate-only 的主窗口参考基线状态组合无效：Requested=False; ManifestAvailable=True; Validated=True' `
            (Get-CountdownApplicationValidationReportFailure `
                -Report $unrequestedValidatedReport `
                -MainManifestRequired $true `
                -FloatingManifestRequired $true) `
            'Application validation report interface rejects validated output that was not requested'

        $missingSourceCaptureReport = $sourceTreeValidationReport | ConvertTo-Json -Depth 20 | ConvertFrom-Json
        $missingSourceCaptureReport.FloatingReferenceBaselineCaptures = @()
        Assert-Equal `
            '应用 validate-only 的悬浮窗参考基线必须包含 6 个 captures，实际为 0。' `
            (Get-CountdownApplicationValidationReportFailure `
                -Report $missingSourceCaptureReport `
                -MainManifestRequired $true `
                -FloatingManifestRequired $true) `
            'Application validation report interface requires source-tree captures'

        $fabricatedPackageCaptureReport = Copy-CountdownApplicationValidationReportFixture
        $fabricatedPackageCaptureReport.FloatingReferenceBaselineCaptures = @(
            [pscustomobject]@{ CaptureId = 'fabricated' }
        )
        Assert-Equal `
            '不携带参考资产的发布包伪造了悬浮窗参考基线 captures。' `
            (Get-CountdownApplicationValidationReportFailure `
                -Report $fabricatedPackageCaptureReport `
                -MainManifestRequired $false `
                -FloatingManifestRequired $false) `
            'Application validation report interface rejects package captures'

        $missingDynamicSampleReport = Copy-CountdownApplicationValidationReportFixture
        $missingDynamicSampleReport.MainDynamicCountdownCardSampleCount = 0
        Assert-Equal `
            '应用 validate-only 报告未命中动态卡片样本：MainDynamicCountdownCardSampleCount' `
            (Get-CountdownApplicationValidationReportFailure `
                -Report $missingDynamicSampleReport `
                -MainManifestRequired $false `
                -FloatingManifestRequired $false) `
            'Application validation report interface requires dynamic card samples'

        $validationApplicationFixture = $validationApplicationJson.Replace("'", "''")
        $validationFixtures = [ordered]@{
            'Countdown.Core.Tests.ps1' = "param([switch] `$ParentTransactionMutexHeld)`r`n'{`"Passed`":11,`"Failed`":0}'"
            'Countdown.CSharp.Tests.ps1' = @"
param([string] `$OfficeDeploymentStagingRoot)
[System.IO.File]::AppendAllText(`$env:COUNTDOWN_VALIDATION_ORDER_PATH, 'csharp' + [Environment]::NewLine)
if (`$PSBoundParameters.ContainsKey('OfficeDeploymentStagingRoot')) {
    [System.IO.File]::WriteAllText(
        (Join-Path `$PSScriptRoot 'office-staging-argument.txt'),
        `$OfficeDeploymentStagingRoot,
        [System.Text.UTF8Encoding]::new(`$false))
}
'{`"Passed`":13,`"Failed`":0,`"AdapterUiValidated`":true}'
"@
            'Countdown.Ui.Tests.ps1' = "'{`"Passed`":17,`"Failed`":0}'"
            '敏敏子倒计时.ps1' = $validationSettingsArtifactFixture + "`r`n'$validationApplicationFixture'"
        }
        foreach ($fixture in $validationFixtures.GetEnumerator()) {
            [System.IO.File]::WriteAllText(
                (Join-Path $validationTestRoot ([string] $fixture.Key)),
                [string] $fixture.Value,
                $utf8NoBom)
        }
        $validationApplicationProject = Join-Path $validationTestRoot 'src\MinminCountdown\MinminCountdown.csproj'
        New-Item -ItemType Directory -Path (Split-Path -Parent $validationApplicationProject) -Force | Out-Null
        [System.IO.File]::WriteAllText(
            $validationApplicationProject,
            '<Project Sdk="Microsoft.NET.Sdk"></Project>',
            $utf8NoBom)

        $validationPowerShell = (Get-Command pwsh.exe -CommandType Application -ErrorAction Stop |
            Select-Object -First 1 -ExpandProperty Source)
        $fixtureDotnetSource = Join-Path $validationTestRoot 'fake-dotnet-source'
        $fixtureDotnetPublish = Join-Path $validationTestRoot 'fake-dotnet-publish'
        New-Item -ItemType Directory -Path $fixtureDotnetSource -Force | Out-Null
        [System.IO.File]::WriteAllText(
            (Join-Path $fixtureDotnetSource 'FakeDotnet.csproj'),
            @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <RuntimeIdentifier>win-x64</RuntimeIdentifier>
    <UseAppHost>true</UseAppHost>
  </PropertyGroup>
</Project>
"@,
            $utf8NoBom)
        [System.IO.File]::WriteAllText(
            (Join-Path $fixtureDotnetSource 'Program.cs'),
            @"
using System;
using System.IO;
using System.Text.Json;

var recordPath = Environment.GetEnvironmentVariable("COUNTDOWN_FAKE_DOTNET_RECORD")
    ?? throw new InvalidOperationException("COUNTDOWN_FAKE_DOTNET_RECORD is required.");
var orderPath = Environment.GetEnvironmentVariable("COUNTDOWN_VALIDATION_ORDER_PATH")
    ?? throw new InvalidOperationException("COUNTDOWN_VALIDATION_ORDER_PATH is required.");
if (args.Length < 2 || args[0] != "build" || !File.Exists(args[1]))
{
    throw new InvalidOperationException("The validation build project must exist.");
}
var record = new
{
    ProcessPath = Environment.ProcessPath,
    Arguments = args,
    DotnetRoot = Environment.GetEnvironmentVariable("DOTNET_ROOT"),
    TelemetryOptOut = Environment.GetEnvironmentVariable("DOTNET_CLI_TELEMETRY_OPTOUT"),
    CliHome = Environment.GetEnvironmentVariable("DOTNET_CLI_HOME")
};
File.AppendAllText(recordPath, JsonSerializer.Serialize(record) + Environment.NewLine);
File.AppendAllText(orderPath, "build" + Environment.NewLine);
return Environment.GetEnvironmentVariable("COUNTDOWN_FAKE_DOTNET_FAIL") == "1" ? 73 : 0;
"@,
            $utf8NoBom)
        $fixtureCompiler = if ($isCIEnvironment) {
            (Get-Command dotnet -ErrorAction Stop).Path
        } else {
            'D:\Codex\tools\dotnet\10.0\dotnet.exe'
        }
        $fixtureCompilerOutput = & $fixtureCompiler publish (Join-Path $fixtureDotnetSource 'FakeDotnet.csproj') `
            -c Release `
            -r win-x64 `
            --self-contained true `
            --nologo `
            -o $fixtureDotnetPublish 2>&1 | Out-String
        if ($LASTEXITCODE -ne 0) {
            throw "Fake dotnet fixture compilation failed: exit=$LASTEXITCODE$([Environment]::NewLine)$fixtureCompilerOutput"
        }
        $fixtureDotnet = Join-Path $fixtureDotnetPublish 'FakeDotnet.exe'
        $fixtureDotnetRecord = Join-Path $validationTestRoot 'fake-dotnet-record.jsonl'
        $validationOrderPath = Join-Path $validationTestRoot 'validation-order.txt'
        $env:COUNTDOWN_FAKE_DOTNET_RECORD = $fixtureDotnetRecord
        $env:COUNTDOWN_VALIDATION_ORDER_PATH = $validationOrderPath
        $env:COUNTDOWN_FAKE_DOTNET_FAIL = $null
        $env:DOTNET_CLI_HOME = 'validation-parent-cli-home-sentinel'
        $env:COUNTDOWN_VALIDATION_ARTIFACT_MODE = 'files'
        $env:COUNTDOWN_VALIDATION_ARTIFACT_RECORD = $validationArtifactRecord
        $env:COUNTDOWN_VALIDATION_SIBLING_MARKER = $validationSiblingMarker
        $validationResult = Invoke-CountdownValidationSuite `
            -Root $validationTestRoot `
            -PowerShell7Path $validationPowerShell `
            -DotnetPath $fixtureDotnet `
            -ParentTransactionMutexHeld
        $successfulValidationSettingsPath = [System.IO.File]::ReadAllText(
            $validationArtifactRecord,
            [System.Text.Encoding]::UTF8)
        foreach ($artifactPath in Get-CountdownValidationSettingsArtifactPaths -SettingsPath $successfulValidationSettingsPath) {
            Assert-Equal $false (Test-Path -LiteralPath $artifactPath) "Successful validation cleans exact settings artifact: $artifactPath"
        }
        Assert-Equal $true (Test-Path -LiteralPath $validationSiblingMarker -PathType Leaf) 'Successful validation preserves an unrelated sibling marker'
        Remove-CountdownValidationSettingsArtifactFixture -SettingsPath $successfulValidationSettingsPath
        Remove-Item -LiteralPath $validationSiblingMarker -Force -ErrorAction SilentlyContinue
        $env:COUNTDOWN_VALIDATION_ARTIFACT_MODE = $null
        Assert-Equal $true ([bool] $validationResult.Valid) 'Validation interface reports one stable success state'
        Assert-Equal 11 ([int] $validationResult.CoreTestsPassed) 'Validation interface exposes Core assertion count'
        Assert-Equal 13 ([int] $validationResult.CSharpTestsPassed) 'Validation interface exposes C# assertion count'
        Assert-Equal 17 ([int] $validationResult.UiTestsPassed) 'Validation interface exposes compatibility UI assertion count'
        Assert-Equal $true ([bool] $validationResult.UiValid) 'Validation interface exposes application validate-only state'
        Assert-Equal $true ([bool] $validationResult.AdapterUiValidated) 'Validation interface preserves adapter UI evidence'
        Assert-Equal '{"Passed":11,"Failed":0}' ([string] $validationResult.TestOutput) 'Validation interface preserves Core JSON compatibility'
        Assert-Equal '{"Passed":13,"Failed":0,"AdapterUiValidated":true}' ([string] $validationResult.CSharpTestOutput) 'Validation interface preserves C# JSON compatibility'
        Assert-Equal '{"Passed":17,"Failed":0}' ([string] $validationResult.UiTestOutput) 'Validation interface preserves UI JSON compatibility'
        Assert-Equal $validationApplicationJson ([string] $validationResult.UiValidationOutput) 'Validation interface preserves application JSON compatibility'
        Assert-Equal `
            ([System.IO.Path]::GetFullPath((Split-Path -Parent $validationTestRoot))) `
            ([System.IO.File]::ReadAllText((Join-Path $validationTestRoot 'office-staging-argument.txt'), [System.Text.Encoding]::UTF8)) `
            'Validation interface passes the current staging root to C# tests while the parent transaction mutex is held'
        $fixtureInvocation = ([System.IO.File]::ReadAllLines($fixtureDotnetRecord, [System.Text.Encoding]::UTF8)[0] | ConvertFrom-Json)
        $dotnetParameter = (Get-Command Invoke-CountdownValidationSuite).Parameters['DotnetPath']
        $expectedApplicationProject = [System.IO.Path]::GetFullPath((Join-Path $validationTestRoot 'src\MinminCountdown\MinminCountdown.csproj'))
        $buildArguments = @($fixtureInvocation.Arguments)
        Assert-Equal $true `
            ([bool] $dotnetParameter.Attributes.Mandatory -and
                [string]::Equals([string] $fixtureInvocation.ProcessPath, $fixtureDotnet, [System.StringComparison]::OrdinalIgnoreCase) -and
                $buildArguments.Count -eq 4 -and
                $buildArguments[0] -eq 'build' -and
                [string]::Equals([string] $buildArguments[1], $expectedApplicationProject, [System.StringComparison]::OrdinalIgnoreCase) -and
                $buildArguments[2] -eq '--configuration' -and
                $buildArguments[3] -eq 'Release' -and
                '-NoRestore' -notin $buildArguments -and
                '--no-restore' -notin $buildArguments) `
            'Validation interface requires and invokes the supplied dotnet path for a restoring Release application build'
        Assert-Equal `
            'build,csharp' `
            (([System.IO.File]::ReadAllLines($validationOrderPath, [System.Text.Encoding]::UTF8)) -join ',') `
            'Validation interface builds the source application before C# regression'
        Assert-Equal $true `
            ([string]::Equals([string] $fixtureInvocation.DotnetRoot, (Split-Path -Parent $fixtureDotnet), [System.StringComparison]::OrdinalIgnoreCase) -and
                [string] $fixtureInvocation.TelemetryOptOut -eq '1' -and
                [string] $fixtureInvocation.CliHome -eq 'validation-parent-cli-home-sentinel') `
            'Validation build sets DOTNET_ROOT and telemetry opt-out without setting DOTNET_CLI_HOME'

        [System.IO.File]::WriteAllText($validationOrderPath, '', $utf8NoBom)
        $env:COUNTDOWN_FAKE_DOTNET_FAIL = '1'
        $buildFailureIsolated = $false
        try {
            Invoke-CountdownValidationSuite `
                -Root $validationTestRoot `
                -PowerShell7Path $validationPowerShell `
                -DotnetPath $fixtureDotnet | Out-Null
        }
        catch {
            $buildFailureIsolated =
                $_.Exception.Message -match '源码应用 Release 构建 失败，退出码：73' -and
                (([System.IO.File]::ReadAllLines($validationOrderPath, [System.Text.Encoding]::UTF8)) -join ',') -eq 'build'
        }
        finally {
            $env:COUNTDOWN_FAKE_DOTNET_FAIL = $null
        }
        Assert-Equal $true $buildFailureIsolated 'Validation build failure propagates distinctly and prevents C# regression'

        [System.IO.File]::WriteAllText(
            (Join-Path $validationTestRoot 'Countdown.CSharp.Tests.ps1'),
            @"
param([string] `$OfficeDeploymentStagingRoot)
[System.IO.File]::AppendAllText(`$env:COUNTDOWN_VALIDATION_ORDER_PATH, 'csharp' + [Environment]::NewLine)
[System.IO.File]::WriteAllText(
    (Join-Path `$PSScriptRoot 'normal-office-staging-argument-present.txt'),
    [string] `$PSBoundParameters.ContainsKey('OfficeDeploymentStagingRoot'),
    [System.Text.UTF8Encoding]::new(`$false))
[Console]::Error.WriteLine('expected diagnostic')
'{`"Passed`":13,`"Failed`":0,`"AdapterUiValidated`":true}'
"@,
            $utf8NoBom)
        $successfulDiagnosticPreserved = $false
        try {
            $diagnosticValidationResult = Invoke-CountdownValidationSuite `
                -Root $validationTestRoot `
                -PowerShell7Path $validationPowerShell `
                -DotnetPath $fixtureDotnet
            $successfulDiagnosticPreserved =
                [bool] $diagnosticValidationResult.Valid -and
                [string] $diagnosticValidationResult.CSharpTestOutput -eq '{"Passed":13,"Failed":0,"AdapterUiValidated":true}'
        }
        catch {
            $successfulDiagnosticPreserved = $false
        }
        Assert-Equal $true $successfulDiagnosticPreserved 'Validation interface parses stdout JSON without merging successful stderr diagnostics'
        Assert-Equal `
            'False' `
            ([System.IO.File]::ReadAllText((Join-Path $validationTestRoot 'normal-office-staging-argument-present.txt'), [System.Text.Encoding]::UTF8)) `
            'Validation interface does not pass the office staging parameter outside the parent transaction context'

        $cSharpTestScript = Join-Path $PSScriptRoot 'Countdown.CSharp.Tests.ps1'
        $invalidOfficeStagingRoot = Join-Path 'D:\Codex\deploy' ("invalid-csharp-staging-" + [guid]::NewGuid().ToString('N'))
        $invalidSourceOutput = @(& $validationPowerShell `
                -NoLogo `
                -NoProfile `
                -NonInteractive `
                -STA `
                -File $cSharpTestScript `
                -OfficeDeploymentStagingRoot $invalidOfficeStagingRoot 2>&1)
        $invalidSourceExitCode = $LASTEXITCODE
        # 跨进程捕获的中文错误文本会被 SSH/console 编码边界破坏，断言只匹配 ASCII 标记。
        Assert-Equal $true ($invalidSourceExitCode -ne 0 -and ($invalidSourceOutput -join "`n") -match 'office-staging-source-mismatch') 'C# tests reject an office staging that does not own the script source before creating test data'
        Assert-Equal $false (Test-Path -LiteralPath $invalidOfficeStagingRoot) 'Rejected office staging validation creates no directory'

        $nestedOfficeStagingRoot = Join-Path $invalidOfficeStagingRoot 'nested'
        $nestedStagingOutput = @(& $validationPowerShell `
                -NoLogo `
                -NoProfile `
                -NonInteractive `
                -STA `
                -File $cSharpTestScript `
                -OfficeDeploymentStagingRoot $nestedOfficeStagingRoot 2>&1)
        $nestedStagingExitCode = $LASTEXITCODE
        Assert-Equal $true ($nestedStagingExitCode -ne 0 -and ($nestedStagingOutput -join "`n") -match 'office-staging-root-invalid') 'C# tests reject a nested office staging before creating test data'
        Assert-Equal $false (Test-Path -LiteralPath $invalidOfficeStagingRoot) 'Rejected nested staging validation creates no parent directory'

        [System.IO.File]::WriteAllText(
            (Join-Path $validationTestRoot 'Countdown.Ui.Tests.ps1'),
            "'{`"Passed`":16,`"Failed`":1}'",
            $utf8NoBom)
        $reportedFailureRejected = $false
        try {
            Invoke-CountdownValidationSuite `
                -Root $validationTestRoot `
                -PowerShell7Path $validationPowerShell `
                -DotnetPath $fixtureDotnet | Out-Null
        }
        catch {
            $reportedFailureRejected = $_.Exception.Message -match '兼容 UI 回归报告包含 1 个失败'
        }
        Assert-Equal $true $reportedFailureRejected 'Validation interface rejects a failed report even when the process exits zero'

        [System.IO.File]::WriteAllText(
            (Join-Path $validationTestRoot 'Countdown.Ui.Tests.ps1'),
            "'{`"Passed`":17,`"Failed`":0}'",
            $utf8NoBom)
        [System.IO.File]::WriteAllText(
            (Join-Path $validationTestRoot 'Countdown.CSharp.Tests.ps1'),
            "'{`"Passed`":13,`"Failed`":0,`"AdapterUiValidated`":false}'",
            $utf8NoBom)
        $adapterUiFailureRejected = $false
        try {
            Invoke-CountdownValidationSuite `
                -Root $validationTestRoot `
                -PowerShell7Path $validationPowerShell `
                -DotnetPath $fixtureDotnet | Out-Null
        }
        catch {
            $adapterUiFailureRejected = $_.Exception.Message -match 'C# 回归未确认 adapter UI 验证'
        }
        Assert-Equal $true $adapterUiFailureRejected 'Validation interface requires adapter UI evidence before reporting success'

        [System.IO.File]::WriteAllText(
            (Join-Path $validationTestRoot 'Countdown.CSharp.Tests.ps1'),
            "'{`"Passed`":13,`"Failed`":0,`"AdapterUiValidated`":true}'",
            $utf8NoBom)
        [System.IO.File]::WriteAllText(
            (Join-Path $validationTestRoot '敏敏子倒计时.ps1'),
            ($validationSettingsArtifactFixture + "`r`n'{`"Valid`":`"false`",`"FloatingWindowPresent`":true}'"),
            $utf8NoBom)
        $env:COUNTDOWN_VALIDATION_ARTIFACT_MODE = 'files'
        $stringBooleanRejected = $false
        try {
            Invoke-CountdownValidationSuite `
                -Root $validationTestRoot `
                -PowerShell7Path $validationPowerShell `
                -DotnetPath $fixtureDotnet | Out-Null
        }
        catch {
            $stringBooleanRejected = $_.Exception.Message -match '应用 validate-only 报告未通过'
        }
        $failedValidationSettingsPath = [System.IO.File]::ReadAllText(
            $validationArtifactRecord,
            [System.Text.Encoding]::UTF8)
        foreach ($artifactPath in Get-CountdownValidationSettingsArtifactPaths -SettingsPath $failedValidationSettingsPath) {
            Assert-Equal $false (Test-Path -LiteralPath $artifactPath) "Failed validation cleans exact settings artifact: $artifactPath"
        }
        Assert-Equal $true (Test-Path -LiteralPath $validationSiblingMarker -PathType Leaf) 'Failed validation preserves an unrelated sibling marker'
        Remove-CountdownValidationSettingsArtifactFixture -SettingsPath $failedValidationSettingsPath
        Remove-Item -LiteralPath $validationSiblingMarker -Force -ErrorAction SilentlyContinue
        $env:COUNTDOWN_VALIDATION_ARTIFACT_MODE = $null
        Assert-Equal $true $stringBooleanRejected 'Validation interface rejects string values that only coerce to true'

        [System.IO.File]::WriteAllText(
            (Join-Path $validationTestRoot '敏敏子倒计时.ps1'),
            ($validationSettingsArtifactFixture + "`r`n'$validationApplicationFixture'"),
            $utf8NoBom)
        $env:COUNTDOWN_VALIDATION_ARTIFACT_MODE = 'directory-backup1'
        $nonFileArtifactRejected = $false
        try {
            Invoke-CountdownValidationSuite `
                -Root $validationTestRoot `
                -PowerShell7Path $validationPowerShell `
                -DotnetPath $fixtureDotnet | Out-Null
        }
        catch {
            $nonFileArtifactRejected = $_.Exception.Message -match '验证设置清理目标不是普通文件'
        }
        $nonFileValidationSettingsPath = [System.IO.File]::ReadAllText(
            $validationArtifactRecord,
            [System.Text.Encoding]::UTF8)
        Assert-Equal $true $nonFileArtifactRejected 'Validation settings cleanup fails closed for a directory at an exact backup path'
        foreach ($artifactPath in Get-CountdownValidationSettingsArtifactPaths -SettingsPath $nonFileValidationSettingsPath) {
            Assert-Equal $true (Test-Path -LiteralPath $artifactPath) "Rejected cleanup preserves every exact settings artifact: $artifactPath"
        }
        Assert-Equal $true (Test-Path -LiteralPath "$nonFileValidationSettingsPath.backup1" -PathType Container) 'Rejected backup directory retains its original type as evidence'
        Assert-Equal $true (Test-Path -LiteralPath $validationSiblingMarker -PathType Leaf) 'Rejected cleanup preserves an unrelated sibling marker'
        Remove-CountdownValidationSettingsArtifactFixture -SettingsPath $nonFileValidationSettingsPath
        Remove-Item -LiteralPath $validationSiblingMarker -Force -ErrorAction SilentlyContinue
        $env:COUNTDOWN_VALIDATION_ARTIFACT_MODE = $null
    }
    finally {
        $env:COUNTDOWN_FAKE_DOTNET_RECORD = $originalFixtureRecord
        $env:COUNTDOWN_VALIDATION_ORDER_PATH = $originalValidationOrder
        $env:COUNTDOWN_FAKE_DOTNET_FAIL = $originalFixtureFailure
        $env:DOTNET_CLI_HOME = $originalDotnetCliHome
        $env:COUNTDOWN_VALIDATION_ARTIFACT_MODE = $originalValidationArtifactMode
        $env:COUNTDOWN_VALIDATION_ARTIFACT_RECORD = $originalValidationArtifactRecord
        $env:COUNTDOWN_VALIDATION_SIBLING_MARKER = $originalValidationSiblingMarker
        if (Test-Path -LiteralPath $validationSiblingMarker -PathType Leaf) {
            Remove-Item -LiteralPath $validationSiblingMarker -Force
        }
        if (Test-Path -LiteralPath $validationArtifactRecord -PathType Leaf) {
            $recordedSettingsPath = [System.IO.File]::ReadAllText(
                $validationArtifactRecord,
                [System.Text.Encoding]::UTF8)
            if (-not [string]::IsNullOrWhiteSpace($recordedSettingsPath)) {
                Remove-CountdownValidationSettingsArtifactFixture -SettingsPath $recordedSettingsPath
            }
        }
        Remove-CountdownIsolatedTestTree -Path $validationTestRoot -AllowedRoot $validationEvidenceRoot
    }

    if ($ValidationOnly) {
        if ($failures.Count -gt 0) {
            $failures | ForEach-Object { Write-Error $_ }
            exit 1
        }
        [pscustomobject]@{ Passed = $script:assertions; Failed = 0; Scope = 'Validation' } | ConvertTo-Json
        exit 0
    }
}

if ($RemoteDeploymentOnly -or (-not $AcceptanceOnly -and -not $ReleaseContractOnly -and -not $ValidationOnly)) {
    $remoteDeploymentEvidenceRoot = 'D:\agents\run-evidence\migration-preflight-20260825T013156Z\clock-countdown-remote-deployment-tests'
    $remoteDeploymentTestRoot = Join-Path $remoteDeploymentEvidenceRoot ([guid]::NewGuid().ToString('N'))
    $remoteDeploymentSource = Join-Path $remoteDeploymentTestRoot 'source'
    $remoteDeploymentInstall = Join-Path $remoteDeploymentTestRoot 'install'
    $remoteDeploymentReceipt = Join-Path $remoteDeploymentTestRoot 'deferred-install-receipt.json'
    $remoteDeploymentPowerShell = (Get-Command pwsh.exe -CommandType Application -ErrorAction Stop |
        Select-Object -First 1 -ExpandProperty Source)
    function New-CountdownRemoteDeploymentFixtureAdapter {
        param([ValidateSet('Success', 'BeginFailure', 'ValidationFailure', 'AdapterUiValidationFailure', 'ValidationSchemaFailure', 'AcceptanceFailure', 'CommitThenFail', 'RollbackFailure', 'FinalizeFailure', 'DisposeFailure', 'ValidationAndDisposeFailure', 'CommitProbeFailure', 'UnresolvedCommittedTransaction')] [string] $Mode)

        $state = [pscustomobject]@{
            Mode = $Mode
            Begun = $false
            Accepted = $false
            Committed = $false
            RolledBack = $false
            RollbackCount = 0
            DiagnosticsPresentBeforeRollback = $false
            Disposed = $false
            InteractiveContext = [pscustomobject]@{ SessionId = 7 }
            StartupRequestPath = Join-Path $remoteDeploymentInstall 'startup-request.json'
            RuntimeStatusPath = Join-Path $remoteDeploymentInstall 'runtime-status.json'
            AcceptanceStatusPath = Join-Path $remoteDeploymentInstall 'acceptance-status.json'
            LaunchToken = 'sensitive-launch-token-sentinel'
            ExpectedProcessId = 0
            ExpectedScheduledTaskIdentity = '<Task><Actions><Exec><Arguments>sensitive-task-argument-sentinel</Arguments></Exec></Actions></Task>'
            SettingsPath = Join-Path $remoteDeploymentInstall 'settings.json'
        }
        $adapter = [pscustomobject]@{
            State = $state
            Begin = {
                param($AdapterState, $Context)
                $AdapterState.Begun = $true
                if ($AdapterState.Mode -eq 'BeginFailure') {
                    throw [System.IO.IOException]::new('模拟候选安装异常。')
                }
                if ($AdapterState.Mode -eq 'CommitProbeFailure') {
                    $AdapterState.PSObject.Properties.Remove('Committed')
                }
                [pscustomobject]@{
                    Validation = [pscustomobject]@{
                        SchemaVersion = if ($AdapterState.Mode -eq 'ValidationSchemaFailure') { 999 } else { 1 }
                        Valid = $AdapterState.Mode -notin @('ValidationFailure', 'RollbackFailure', 'ValidationAndDisposeFailure', 'CommitProbeFailure', 'UnresolvedCommittedTransaction')
                        CoreTestsPassed = 11
                        CSharpTestsPassed = 13
                        UiTestsPassed = 17
                        UiValid = $true
                        AdapterUiValidated = $AdapterState.Mode -ne 'AdapterUiValidationFailure'
                    }
                    RuntimeStatus = [pscustomobject]@{
                        processId = 4242
                        stage = 'window-ready'
                        calendarState = 'Ready'
                    }
                }
            }
            Accept = {
                param($AdapterState, $Context, $Candidate)
                if ($AdapterState.Mode -eq 'AcceptanceFailure') {
                    throw '模拟验收失败。'
                }
                $AdapterState.Accepted = $true
                $AdapterState.Committed = $true
                if ($AdapterState.Mode -eq 'CommitThenFail') {
                    throw '模拟提交后验收失败。'
                }
                [pscustomobject]@{
                    AcceptanceState = 'finalized-ready'
                    RuntimeProcessId = 4242
                    TaskState = 'Ready'
                    LastTaskResult = 0
                    SettingsHashBefore = ('A' * 64)
                    SettingsHashAfter = ('B' * 64)
                    SettingsLengthBefore = 1
                    SettingsLengthAfter = 2
                    SettingsMtimeUtcBefore = '2026-01-01T00:00:00.0000000Z'
                    SettingsMtimeUtcAfter = '2026-01-02T00:00:00.0000000Z'
                    SettingsUnchanged = $false
                }
            }
            Rollback = {
                param($AdapterState, $Context, $Candidate)
                $AdapterState.RolledBack = $true
                $AdapterState.RollbackCount++
                $AdapterState.DiagnosticsPresentBeforeRollback = Test-Path -LiteralPath (Join-Path (Split-Path -Parent $Context.DeferredReceiptPath) 'pre-rollback-diagnostics.json') -PathType Leaf
                if ($AdapterState.Mode -eq 'RollbackFailure') {
                    throw '模拟回滚失败。'
                }
                [pscustomobject]@{
                    PreservedUnmanagedLaunchers = $false
                    UnresolvedCommittedTransaction = $AdapterState.Mode -eq 'UnresolvedCommittedTransaction'
                }
            }
            Finalize = {
                param($AdapterState, $Context)
                if ($AdapterState.Mode -eq 'FinalizeFailure') {
                    throw '模拟提交后收尾失败。'
                }
            }
            Dispose = {
                param($AdapterState, $Context)
                $AdapterState.Disposed = $true
                if ($AdapterState.Mode -in @('DisposeFailure', 'ValidationAndDisposeFailure')) {
                    throw '模拟资源释放失败。'
                }
            }
        }
        [pscustomobject]@{ State = $state; Adapter = $adapter }
    }

    function New-CountdownRemoteDeploymentWindowsFixtureAdapter {
        param(
            [Parameter(Mandatory)]
            [ValidateSet(
                'AcceptedFinalizationFailure',
                'AcceptedReceiptFailure',
                'AcceptedEnvironmentFailure',
                'CommitPreparationFailure',
                'CommittedCleanupFailure',
                'CommittedFinalizeFailure')]
            [string] $Mode
        )

        $remoteDeploymentModule = Get-Module -Name Countdown.RemoteDeployment | Select-Object -First 1
        $adapter = & $remoteDeploymentModule { New-CountdownRemoteDeploymentWindowsAdapter }
        $transactionPath = Join-Path $remoteDeploymentTestRoot "$Mode-transaction"
        New-Item -ItemType Directory -Path $transactionPath -Force | Out-Null
        $adapter.State | Add-Member -NotePropertyName TestMode -NotePropertyValue $Mode
        $adapter.State.RollbackTransactionPath = $transactionPath
        $adapter.State.InteractiveContext = [pscustomobject]@{
            User = 'MINMIN\RemoteDeploymentFixture'
            SessionId = 7
            Desktop = (Join-Path $remoteDeploymentTestRoot 'desktop')
        }
        $adapter.State.LauncherPath = Join-Path $remoteDeploymentTestRoot 'desktop\敏敏子倒计时.exe'
        $adapter.State.LegacyLauncherPath = Join-Path $remoteDeploymentTestRoot 'desktop\敏敏子倒计时.cmd'
        $adapter.State.LauncherTempPath = "$($adapter.State.LauncherPath).new"
        $adapter.State.SettingsSnapshotPath = Join-Path $transactionPath 'install-backup\settings.json'
        $adapter.State.SettingsPath = Join-Path $remoteDeploymentInstall 'settings.json'
        $adapter.State.StartupRequestPath = Join-Path $remoteDeploymentInstall 'startup-request.json'
        $adapter.State.RuntimeStatusPath = Join-Path $remoteDeploymentInstall 'runtime-status.json'
        $adapter.State.AcceptanceStatusPath = Join-Path $remoteDeploymentInstall 'acceptance-status.json'
        $adapter.State.LaunchToken = 'remote-deployment-fixture-token'
        $adapter.State.ExpectedProcessId = 4242
        $adapter.State.ExpectedScheduledTaskIdentity = '<Task />'
        $adapter.State | Add-Member -NotePropertyName RollbackCount -NotePropertyValue 0
        $adapter.State | Add-Member -NotePropertyName Disposed -NotePropertyValue $false

        $adapter.Begin = {
            param($AdapterState, $Context)
            [pscustomobject]@{
                Validation = [pscustomobject]@{
                    SchemaVersion = 1
                    Valid = $true
                    CoreTestsPassed = 11
                    CSharpTestsPassed = 13
                    UiTestsPassed = 17
                    UiValid = $true
                    AdapterUiValidated = $true
                }
                RuntimeStatus = [pscustomobject]@{
                    processId = 4242
                    stage = 'window-ready'
                    calendarState = 'Ready'
                }
            }
        }
        $adapter.Rollback = {
            param($AdapterState, $Context, $Candidate)
            $AdapterState.RollbackCount++
            [pscustomobject]@{
                PreservedUnmanagedLaunchers = $false
                UnresolvedCommittedTransaction = $false
            }
        }
        $adapter.Finalize = {
            param($AdapterState, $Context)
            if ($AdapterState.TestMode -eq 'CommittedFinalizeFailure') {
                throw '模拟 committed 后 adapter 收尾失败。'
            }
        }
        $adapter.Dispose = {
            param($AdapterState, $Context)
            $AdapterState.Disposed = $true
        }
        [pscustomobject]@{
            Adapter = $adapter
            State = $adapter.State
            TransactionPath = $transactionPath
        }
    }

    function New-CountdownIssue4DefaultAdapterPackage {
        param(
            [Parameter(Mandatory)] [string] $Name,
            [switch] $IncludeExtraPackageFile,
            [switch] $OmitExtraManifestEntry,
            [switch] $IncludeUnexpectedManifestEntry,
            [switch] $ContractHashConflict,
            [switch] $DriftReleaseContractAfterManifest
        )

        $root = Join-Path $remoteDeploymentTestRoot "issue4-default-$Name"
        $installPath = Join-Path $root 'install'
        New-Item -ItemType Directory -Path $root -Force | Out-Null
        New-Item -ItemType Directory -Path $installPath -Force | Out-Null
        $packageFiles = [System.Collections.Generic.List[string]]::new()
        foreach ($relativePath in @(
                'Countdown.ReleaseContract.psm1',
                'Countdown.InstallTransaction.psm1',
                'Countdown.Validation.psm1',
                'Countdown.Runtime.psm1',
                'Countdown.Settings.psm1',
                'Countdown.TransactionAcceptance.psm1',
                'Install-Countdown.ps1',
                '敏敏子倒计时.ps1')) {
            $packageFiles.Add($relativePath)
        }
        if ($IncludeExtraPackageFile) {
            $packageFiles.Add('data\issue4-extra.txt')
        }

        foreach ($relativePath in $packageFiles) {
            if ($relativePath -eq 'Countdown.ReleaseContract.psm1') { continue }
            $path = Join-Path $root $relativePath
            New-Item -ItemType Directory -Path (Split-Path -Parent $path) -Force | Out-Null
            $content = switch ($relativePath) {
                'Countdown.InstallTransaction.psm1' {
                    "`$global:CountdownIssue4InstallTransactionSentinel = 'executed'`r`n"
                }
                'Install-Countdown.ps1' {
                    "`$global:CountdownIssue4InstallerSentinel = 'executed'`r`n"
                }
                default { "Set-StrictMode -Version Latest`r`n" }
            }
            [System.IO.File]::WriteAllText($path, $content, [System.Text.UTF8Encoding]::new($false))
        }

        $contractIntegrityHash = if ($ContractHashConflict) { '0' * 64 } else {
            (Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $root 'Countdown.InstallTransaction.psm1')).Hash
        }
        $packageFilesLiteral = @(
            $packageFiles | ForEach-Object { "            '$($_.Replace("'", "''"))'" }
        ) -join ",`r`n"
        $releaseContractSource = @"
Set-StrictMode -Version Latest

function Get-CountdownReleaseContract {
    [pscustomobject]@{
        PackageFiles = [string[]] @(
$packageFilesLiteral
        )
        IntegrityHashes = [ordered]@{
            'Countdown.InstallTransaction.psm1' = '$contractIntegrityHash'
        }
        InstalledFiles = [string[]] @()
        RetiredRuntimeFiles = [string[]] @()
    }
}

function Assert-CountdownReleasePackage {
    param([string] `$Root)
    [pscustomobject]@{ Valid = `$true }
}

function Get-CountdownInteractiveDesktopContext {
    `$global:CountdownIssue4WindowsActionSentinel = 'executed'
    throw 'Issue #4 fixture crossed into Windows actions.'
}

Export-ModuleMember -Function Get-CountdownReleaseContract, Assert-CountdownReleasePackage, Get-CountdownInteractiveDesktopContext
"@
        $releaseContractPath = Join-Path $root 'Countdown.ReleaseContract.psm1'
        [System.IO.File]::WriteAllText(
            $releaseContractPath,
            $releaseContractSource,
            [System.Text.UTF8Encoding]::new($false))

        $manifest = [ordered]@{}
        foreach ($relativePath in $packageFiles) {
            if ($OmitExtraManifestEntry -and $relativePath -eq 'data\issue4-extra.txt') { continue }
            $manifest[$relativePath] = (Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $root $relativePath)).Hash
        }
        if ($IncludeUnexpectedManifestEntry) {
            $unexpectedPath = Join-Path $root 'issue4-unexpected.txt'
            [System.IO.File]::WriteAllText(
                $unexpectedPath,
                'unexpected',
                [System.Text.UTF8Encoding]::new($false))
            $manifest['issue4-unexpected.txt'] = (Get-FileHash -Algorithm SHA256 -LiteralPath $unexpectedPath).Hash
        }
        if ($DriftReleaseContractAfterManifest) {
            [System.IO.File]::WriteAllText(
                $releaseContractPath,
                "`$global:CountdownIssue4MaliciousReleaseSentinel = 'executed'`r`n$releaseContractSource",
                [System.Text.UTF8Encoding]::new($false))
        }

        $adapter = & $remoteDeploymentModule { New-CountdownRemoteDeploymentWindowsAdapter }
        $context = [pscustomobject]@{
            SourceRoot = $root
            InstallPath = $installPath
            PowerShell7Path = $remoteDeploymentPowerShell
            DeferredReceiptPath = Join-Path $root 'deferred-install-receipt.json'
            TaskName = "Issue4 $Name"
            LegacyTaskName = "Issue4 Legacy $Name"
            DeploymentBindingPath = ''
            DeploymentBindingSha256 = ''
            ExpectedFileSha256 = $manifest
            TrustedFileManifestRequired = $true
            AdapterState = $adapter.State
        }
        [pscustomobject]@{
            Adapter = $adapter
            Context = $context
        }
    }

    function Invoke-CountdownIssue4DefaultAdapterRejection {
        param([Parameter(Mandatory)] [object] $Fixture)

        foreach ($name in @(
                'CountdownIssue4InstallTransactionSentinel',
                'CountdownIssue4InstallerSentinel',
                'CountdownIssue4WindowsActionSentinel',
                'CountdownIssue4MaliciousReleaseSentinel')) {
            Remove-Variable -Name $name -Scope Global -ErrorAction SilentlyContinue
        }
        $message = ''
        try {
            [void] (& $Fixture.Adapter.Begin $Fixture.Adapter.State $Fixture.Context)
        }
        catch {
            $message = $_.Exception.Message
        }
        finally {
            [void] (& $Fixture.Adapter.Dispose $Fixture.Adapter.State $Fixture.Context)
        }
        $result = [pscustomobject]@{
            Message = $message
            TransactionModuleImported = [bool] $Fixture.Adapter.State.TransactionModuleImported
            InstallStarted = [bool] $Fixture.Adapter.State.InstallStarted
            InstallTransactionSentinel = $null -ne (Get-Variable -Name CountdownIssue4InstallTransactionSentinel -Scope Global -ErrorAction SilentlyContinue)
            InstallerSentinel = $null -ne (Get-Variable -Name CountdownIssue4InstallerSentinel -Scope Global -ErrorAction SilentlyContinue)
            WindowsActionSentinel = $null -ne (Get-Variable -Name CountdownIssue4WindowsActionSentinel -Scope Global -ErrorAction SilentlyContinue)
            MaliciousReleaseSentinel = $null -ne (Get-Variable -Name CountdownIssue4MaliciousReleaseSentinel -Scope Global -ErrorAction SilentlyContinue)
        }
        foreach ($name in @(
                'CountdownIssue4InstallTransactionSentinel',
                'CountdownIssue4InstallerSentinel',
                'CountdownIssue4WindowsActionSentinel',
                'CountdownIssue4MaliciousReleaseSentinel')) {
            Remove-Variable -Name $name -Scope Global -ErrorAction SilentlyContinue
        }
        Import-Module (Join-Path $PSScriptRoot 'Countdown.InstallTransaction.psm1') -Force -DisableNameChecking
        Import-Module (Join-Path $PSScriptRoot 'Countdown.ReleaseContract.psm1') -Force
        $result
    }

    function Invoke-CountdownIssue4AtomicReplacementProbe {
        param(
            [Parameter(Mandatory)] [string] $DestinationPath,
            [Parameter(Mandatory)] [string] $ReplacementPath,
            [Parameter(Mandatory)] [string] $BackupPath,
            [switch] $Restore
        )

        try {
            [System.IO.File]::Replace($ReplacementPath, $DestinationPath, $BackupPath, $true)
            if ($Restore) {
                [System.IO.File]::Replace($BackupPath, $DestinationPath, $ReplacementPath, $true)
            }
            $true
        }
        catch {
            $false
        }
    }

    try {
        New-Item -ItemType Directory -Path $remoteDeploymentSource -Force | Out-Null
        New-Item -ItemType Directory -Path $remoteDeploymentInstall -Force | Out-Null
        [System.IO.File]::WriteAllText(
            (Join-Path $remoteDeploymentSource 'Countdown.InstallTransaction.psm1'),
            @'
Set-StrictMode -Version Latest

function Complete-CountdownInstallTransaction {
    param(
        [string] $TransactionPath,
        [string] $ExpectedInstallPath,
        [string] $ExpectedTaskName,
        [string] $ExpectedLegacyTaskName,
        [string] $ExpectedLauncherPath,
        [string] $ExpectedLegacyLauncherPath,
        [string] $ExpectedLauncherTempPath
    )
    [System.IO.File]::WriteAllText(
        (Join-Path $TransactionPath 'accepted.marker'),
        'accepted',
        [System.Text.UTF8Encoding]::new($false))
}

function Remove-CountdownCommittedInstallTransaction {
    param(
        [string] $TransactionPath,
        [string] $ExpectedInstallPath,
        [string] $ExpectedTaskName,
        [string] $ExpectedLegacyTaskName,
        [string] $ExpectedLauncherPath,
        [string] $ExpectedLegacyLauncherPath,
        [string] $ExpectedLauncherTempPath,
        [scriptblock] $MoveAction
    )
    if ($TransactionPath -match 'CommitPreparationFailure') {
        throw '模拟 committed 点前的清理准备失败。'
    }

    $destination = "$TransactionPath.committed-fixture"
    if ($null -ne $MoveAction) {
        & $MoveAction $TransactionPath $destination
    }
    else {
        Move-Item -LiteralPath $TransactionPath -Destination $destination -ErrorAction Stop
    }
    if ($TransactionPath -match 'CommittedCleanupFailure') {
        throw '模拟 committed 点后的事务清理失败。'
    }
}

Export-ModuleMember -Function Complete-CountdownInstallTransaction, Remove-CountdownCommittedInstallTransaction
'@,
            [System.Text.UTF8Encoding]::new($false))
        [System.IO.File]::WriteAllText(
            (Join-Path $remoteDeploymentSource 'Countdown.Runtime.psm1'),
            "Set-StrictMode -Version Latest`r`n",
            [System.Text.UTF8Encoding]::new($false))
        [System.IO.File]::WriteAllText(
            (Join-Path $remoteDeploymentInstall 'Countdown.Settings.psm1'),
            "Set-StrictMode -Version Latest`r`n",
            [System.Text.UTF8Encoding]::new($false))
        [System.IO.File]::WriteAllText(
            (Join-Path $remoteDeploymentSource 'Countdown.TransactionAcceptance.psm1'),
            @'
Set-StrictMode -Version Latest

function Invoke-CountdownTransactionAcceptance {
    param(
        [string] $StartupRequestPath,
        [string] $RuntimeStatusPath,
        [string] $AcceptanceStatusPath,
        [string] $LaunchToken,
        [object] $ExpectedInteractiveContext,
        [int] $ExpectedProcessId,
        [int] $ExpectedSessionId,
        [string] $ExpectedApplicationPath,
        [string] $TaskName,
        [string] $ExpectedScheduledTaskIdentity,
        [string] $SettingsSnapshotPath,
        [string] $SettingsPath,
        [scriptblock] $GetInteractiveContext,
        [scriptblock] $GetScheduledTask,
        [scriptblock] $GetScheduledTaskIdentity,
        [scriptblock] $CompleteTransaction,
        [scriptblock] $RemoveCommittedTransaction
    )

    [void] (& $CompleteTransaction)
    switch ($TaskName) {
        'AcceptedFinalizationFailure' { throw '模拟 accepted 后 finalized 写入失败。' }
        'AcceptedReceiptFailure' { throw '模拟 accepted 后 finalized-ready 回执失败。' }
        'AcceptedEnvironmentFailure' { throw '模拟 accepted 后环境复验失败。' }
    }

    [void] (& $RemoveCommittedTransaction)
    [pscustomobject]@{
        AcceptanceState = 'finalized-ready'
        RuntimeProcessId = 4242
        TaskState = 'Ready'
        LastTaskResult = 0
        SettingsHashBefore = ('A' * 64)
        SettingsHashAfter = ('B' * 64)
        SettingsLengthBefore = 1
        SettingsLengthAfter = 2
        SettingsMtimeUtcBefore = '2026-01-01T00:00:00.0000000Z'
        SettingsMtimeUtcAfter = '2026-01-02T00:00:00.0000000Z'
        SettingsUnchanged = $false
    }
}

Export-ModuleMember -Function Invoke-CountdownTransactionAcceptance
'@,
            [System.Text.UTF8Encoding]::new($false))
        [System.IO.File]::WriteAllText(
            (Join-Path $remoteDeploymentInstall 'settings.json'),
            '{"sensitive-settings-sentinel":true}',
            [System.Text.UTF8Encoding]::new($false))

        $installedValidationTransaction = Join-Path $remoteDeploymentTestRoot 'installed-validation-transaction'
        New-Item -ItemType Directory -Path $installedValidationTransaction -Force | Out-Null
        $installedValidationApplication = Join-Path $remoteDeploymentInstall '敏敏子倒计时.ps1'
        if (-not (Test-Path -LiteralPath $installedValidationApplication -PathType Leaf)) {
            [System.IO.File]::WriteAllText(
                $installedValidationApplication,
                "param()`r`n",
                [System.Text.UTF8Encoding]::new($false))
        }
        $installedValidationActiveSettings = Join-Path $remoteDeploymentInstall 'settings.json'
        $installedValidationActiveBefore = Get-Item -LiteralPath $installedValidationActiveSettings
        $installedValidationActiveHashBefore = (Get-FileHash -Algorithm SHA256 -LiteralPath $installedValidationActiveSettings).Hash
        $installedValidationInvocation = [pscustomobject]@{
            Executable = ''
            Arguments = @()
            SettingsFile = ''
            ActiveSettingsArgumentObserved = $false
        }
        $installedValidationNativeInvocation = {
            param([string] $Executable, [string[]] $Arguments)

            $installedValidationInvocation.Executable = $Executable
            $installedValidationInvocation.Arguments = [string[]] @($Arguments)
            $settingsIndex = [Array]::IndexOf($Arguments, '-SettingsFile')
            if ($settingsIndex -lt 0 -or $settingsIndex + 1 -ge $Arguments.Count) {
                throw 'Native validation fixture did not receive -SettingsFile.'
            }
            $settingsFile = [string] $Arguments[$settingsIndex + 1]
            $installedValidationInvocation.SettingsFile = $settingsFile
            $installedValidationInvocation.ActiveSettingsArgumentObserved = @($Arguments) -contains $installedValidationActiveSettings
            foreach ($path in @(
                    $settingsFile,
                    "$settingsFile.tmp",
                    "$settingsFile.backup1",
                    "$settingsFile.backup2",
                    "$settingsFile.backup2.tmp",
                    "$settingsFile.backup2.rollback"
                )) {
                [System.IO.File]::WriteAllText($path, 'fixture', [System.Text.UTF8Encoding]::new($false))
            }
            [pscustomobject]@{
                ExitCode = 0
                Output = [pscustomobject]@{
                    Valid = $true
                    FloatingWindowPresent = $true
                    FloatingManagerButtonPresent = $true
                } | ConvertTo-Json -Compress
            }
        }.GetNewClosure()
        $installedValidationResult = Invoke-CountdownInstalledApplicationValidation `
            -ApplicationPath $installedValidationApplication `
            -PowerShell7Path $remoteDeploymentPowerShell `
            -TransactionPath $installedValidationTransaction `
            -ActiveSettingsPath $installedValidationActiveSettings `
            -NativeInvocation $installedValidationNativeInvocation
        $installedValidationActiveAfter = Get-Item -LiteralPath $installedValidationActiveSettings
        Assert-Equal $true ([bool] $installedValidationResult.Valid) 'Remote installed application validation returns the native validation result'
        Assert-Equal $true (@($installedValidationInvocation.Arguments) -contains '-ValidateOnly') 'Remote installed application validation invokes the installed app in validate-only mode'
        Assert-Equal $false $installedValidationInvocation.ActiveSettingsArgumentObserved 'Remote installed application validation never passes the active settings path to the child process'
        Assert-Equal $true ([System.IO.Path]::GetFullPath($installedValidationInvocation.SettingsFile).StartsWith([System.IO.Path]::GetFullPath($installedValidationTransaction).TrimEnd('\') + '\', [System.StringComparison]::OrdinalIgnoreCase)) 'Remote installed application validation uses a transaction-local settings path'
        Assert-Equal 0 @(
            Get-ChildItem -LiteralPath $installedValidationTransaction -Force |
                Where-Object { $_.Name -like 'installed-validation-*' }
        ).Count 'Remote installed application validation cleans settings and companion files in finally'
        Assert-Equal $installedValidationActiveBefore.Length $installedValidationActiveAfter.Length 'Remote installed application validation preserves active settings length'
        Assert-Equal $installedValidationActiveBefore.LastWriteTimeUtc $installedValidationActiveAfter.LastWriteTimeUtc 'Remote installed application validation preserves active settings mtime'
        Assert-Equal $installedValidationActiveHashBefore (Get-FileHash -Algorithm SHA256 -LiteralPath $installedValidationActiveSettings).Hash 'Remote installed application validation preserves active settings SHA-256'

        $removedActiveSettingsPath = Join-Path $remoteDeploymentInstall 'removed-active.settings.json'
        [System.IO.File]::WriteAllText($removedActiveSettingsPath, '{"active-settings":true}', [System.Text.UTF8Encoding]::new($false))
        $activeSettingsRemovalInvocation = {
            param([string] $Executable, [string[]] $Arguments)

            [System.IO.File]::Delete($removedActiveSettingsPath)
            & $installedValidationNativeInvocation $Executable $Arguments
        }.GetNewClosure()
        $activeSettingsRemovalRejected = $false
        try {
            Invoke-CountdownInstalledApplicationValidation `
                -ApplicationPath $installedValidationApplication `
                -PowerShell7Path $remoteDeploymentPowerShell `
                -TransactionPath $installedValidationTransaction `
                -ActiveSettingsPath $removedActiveSettingsPath `
                -NativeInvocation $activeSettingsRemovalInvocation | Out-Null
        }
        catch {
            $activeSettingsRemovalRejected = $_.Exception.Message -match '改变了现役 settings.json'
        }
        Assert-Equal $true $activeSettingsRemovalRejected 'Installed validation rejects present active settings removed by the child process'

        $replacedActiveSettingsPath = Join-Path $remoteDeploymentInstall 'replaced-active.settings.json'
        $replacementCandidatePath = "$replacedActiveSettingsPath.replacement"
        [System.IO.File]::WriteAllText($replacedActiveSettingsPath, '{"active-settings":true}', [System.Text.UTF8Encoding]::new($false))
        $replacedActiveSettingsBytes = [System.IO.File]::ReadAllBytes($replacedActiveSettingsPath)
        $replacedActiveSettingsMtime = (Get-Item -LiteralPath $replacedActiveSettingsPath).LastWriteTimeUtc
        $activeSettingsReplacementInvocation = {
            param([string] $Executable, [string[]] $Arguments)

            [System.IO.File]::WriteAllBytes($replacementCandidatePath, $replacedActiveSettingsBytes)
            [System.IO.File]::SetLastWriteTimeUtc($replacementCandidatePath, $replacedActiveSettingsMtime)
            [System.IO.File]::Move($replacementCandidatePath, $replacedActiveSettingsPath, $true)
            [System.IO.File]::SetLastWriteTimeUtc($replacedActiveSettingsPath, $replacedActiveSettingsMtime)
            & $installedValidationNativeInvocation $Executable $Arguments
        }.GetNewClosure()
        $activeSettingsReplacementRejected = $false
        try {
            Invoke-CountdownInstalledApplicationValidation `
                -ApplicationPath $installedValidationApplication `
                -PowerShell7Path $remoteDeploymentPowerShell `
                -TransactionPath $installedValidationTransaction `
                -ActiveSettingsPath $replacedActiveSettingsPath `
                -NativeInvocation $activeSettingsReplacementInvocation | Out-Null
        }
        catch {
            $activeSettingsReplacementRejected = $_.Exception.Message -match '改变了现役 settings.json'
        }
        finally {
            Remove-Item -LiteralPath $replacementCandidatePath -Force -ErrorAction SilentlyContinue
        }
        Assert-Equal $true $activeSettingsReplacementRejected 'Installed validation rejects same-bytes active settings object replacement'

        $freshInstallSettingsPath = Join-Path $remoteDeploymentInstall 'fresh-install.settings.json'
        Assert-Equal $false (Test-Path -LiteralPath $freshInstallSettingsPath) 'Fresh-install validation starts without an active settings file'
        $freshInstallValidationResult = Invoke-CountdownInstalledApplicationValidation `
            -ApplicationPath $installedValidationApplication `
            -PowerShell7Path $remoteDeploymentPowerShell `
            -TransactionPath $installedValidationTransaction `
            -ActiveSettingsPath $freshInstallSettingsPath `
            -NativeInvocation $installedValidationNativeInvocation
        Assert-Equal $true ([bool] $freshInstallValidationResult.Valid) 'Fresh-install validation accepts absent active settings that remain absent'
        Assert-Equal $false (Test-Path -LiteralPath $freshInstallSettingsPath) 'Fresh-install validation does not create active settings'
        $freshInstallCreationInvocation = {
            param([string] $Executable, [string[]] $Arguments)

            [System.IO.File]::WriteAllText(
                $freshInstallSettingsPath,
                '{"unexpected-active-settings":true}',
                [System.Text.UTF8Encoding]::new($false))
            & $installedValidationNativeInvocation $Executable $Arguments
        }.GetNewClosure()
        $freshInstallCreationRejected = $false
        try {
            Invoke-CountdownInstalledApplicationValidation `
                -ApplicationPath $installedValidationApplication `
                -PowerShell7Path $remoteDeploymentPowerShell `
                -TransactionPath $installedValidationTransaction `
                -ActiveSettingsPath $freshInstallSettingsPath `
                -NativeInvocation $freshInstallCreationInvocation | Out-Null
        }
        catch {
            $freshInstallCreationRejected = $_.Exception.Message -match '改变了现役 settings.json'
        }
        finally {
            Remove-Item -LiteralPath $freshInstallSettingsPath -Force -ErrorAction SilentlyContinue
        }
        Assert-Equal $true $freshInstallCreationRejected 'Fresh-install validation rejects active settings created by the child process'

        $trustedLoaderRoot = Join-Path $remoteDeploymentTestRoot 'trusted-source-loader'
        $trustedLoaderModulePath = Join-Path $trustedLoaderRoot 'Issue4.SourceLoader.Fixture.psm1'
        $trustedLoaderMaliciousPath = Join-Path $remoteDeploymentTestRoot 'Issue4.SourceLoader.Malicious.psm1'
        $trustedLoaderBackupPath = Join-Path $remoteDeploymentTestRoot 'Issue4.SourceLoader.Backup.psm1'
        $trustedLoaderSource = @'
function Get-CountdownIssue4SourceLoaderValue { 'benign' }
Export-ModuleMember -Function Get-CountdownIssue4SourceLoaderValue
'@
        $maliciousLoaderSource = @'
Set-Variable -Scope Global -Name CountdownIssue4RemoteLoaderSentinel -Value 'executed'
function Get-CountdownIssue4SourceLoaderValue { 'malicious' }
Export-ModuleMember -Function Get-CountdownIssue4SourceLoaderValue
'@
        New-Item -ItemType Directory -Path $trustedLoaderRoot -Force | Out-Null
        [System.IO.File]::WriteAllText(
            $trustedLoaderModulePath,
            $trustedLoaderSource,
            [System.Text.UTF8Encoding]::new($false))
        [System.IO.File]::WriteAllText(
            $trustedLoaderMaliciousPath,
            $maliciousLoaderSource,
            [System.Text.UTF8Encoding]::new($false))
        $trustedLoaderHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $trustedLoaderModulePath).Hash
        $trustedLoaderManifest = [ordered]@{
            'Issue4.SourceLoader.Fixture.psm1' = $trustedLoaderHash
        }
        $trustedLoaderState = [pscustomobject]@{
            SourceFileLocks = $null
            InstalledFileLocks = $null
        }
        $trustedLoaderContext = [pscustomobject]@{
            SourceRoot = $trustedLoaderRoot
            InstallPath = $trustedLoaderRoot
            ExpectedFileSha256 = $trustedLoaderManifest
            TrustedFileManifestRequired = $true
            AdapterState = $trustedLoaderState
        }
        $remoteDeploymentModule = Get-Module -Name Countdown.RemoteDeployment | Select-Object -First 1
        $trustedLoaderState.SourceFileLocks = & $remoteDeploymentModule {
            param($root, $manifest)
            Enter-CountdownRemoteDeploymentFileLocks `
                -Root $root `
                -ExpectedFileSha256 $manifest `
                -RelativePaths @('Issue4.SourceLoader.Fixture.psm1') `
                -Stage '远端 source-loader 夹具'
        } $trustedLoaderRoot $trustedLoaderManifest
        $trustedLoaderSwapState = [pscustomobject]@{
            Rejected = $false
            Succeeded = $false
            Restored = $false
            ImportedValue = ''
        }
        Remove-Variable -Name CountdownIssue4RemoteLoaderSentinel -Scope Global -ErrorAction SilentlyContinue
        $trustedLoaderAction = {
            param($lockedPath)
            try {
                [System.IO.File]::Replace(
                    $trustedLoaderMaliciousPath,
                    $lockedPath,
                    $trustedLoaderBackupPath,
                    $true)
                $trustedLoaderSwapState.Succeeded = $true
                try {
                    $fixtureModule = Import-Module $lockedPath -Force -PassThru
                    $trustedLoaderSwapState.ImportedValue = & $fixtureModule { Get-CountdownIssue4SourceLoaderValue }
                }
                finally {
                    [System.IO.File]::Replace(
                        $trustedLoaderBackupPath,
                        $lockedPath,
                        $trustedLoaderMaliciousPath,
                        $true)
                    $trustedLoaderSwapState.Restored = $true
                }
            }
            catch {
                if ($trustedLoaderSwapState.Succeeded) { throw }
                $trustedLoaderSwapState.Rejected = $true
                $fixtureModule = Import-Module $lockedPath -Force -PassThru
                $trustedLoaderSwapState.ImportedValue = & $fixtureModule { Get-CountdownIssue4SourceLoaderValue }
            }
        }.GetNewClosure()
        try {
            & $remoteDeploymentModule {
                param($context, $action)
                Invoke-CountdownRemoteDeploymentTrustedFile `
                    -Context $context `
                    -Location Source `
                    -RelativePath 'Issue4.SourceLoader.Fixture.psm1' `
                    -Stage '远端 source-loader 导入窗口夹具' `
                    -Action $action
            } $trustedLoaderContext $trustedLoaderAction
        }
        finally {
            & $remoteDeploymentModule {
                param($locks)
                Exit-CountdownRemoteDeploymentFileLocks -Locks $locks
            } $trustedLoaderState.SourceFileLocks
            $trustedLoaderState.SourceFileLocks = $null
        }
        Assert-Equal $true $trustedLoaderSwapState.Rejected 'Default remote source-loader rejects an atomic malicious replacement'
        Assert-Equal $false $trustedLoaderSwapState.Succeeded 'Malicious remote module cannot enter the verified loader window'
        Assert-Equal $false $trustedLoaderSwapState.Restored 'Rejected remote replacement never opens a restore window'
        Assert-Equal 'benign' $trustedLoaderSwapState.ImportedValue 'Default remote source-loader imports the same benign bytes that passed SHA-256 verification'
        Assert-Equal $false ($null -ne (Get-Variable -Name CountdownIssue4RemoteLoaderSentinel -Scope Global -ErrorAction SilentlyContinue)) 'Default remote source-loader never executes the malicious sentinel'
        Remove-Module Issue4.SourceLoader.Fixture -Force -ErrorAction SilentlyContinue

        $missingDefaultManifestRejected = $false
        try {
            Invoke-CountdownRemoteDeployment `
                -SourceRoot $remoteDeploymentSource `
                -InstallPath $remoteDeploymentInstall `
                -PowerShell7Path $remoteDeploymentPowerShell `
                -DeferredReceiptPath $remoteDeploymentReceipt `
                -TaskName 'Minmin Countdown Manifest Fixture' `
                -LegacyTaskName '敏敏子倒计时 Manifest Fixture' | Out-Null
        }
        catch {
            $missingDefaultManifestRejected = $_.Exception.Message -match '默认部署 adapter 缺少受信文件 identity'
        }
        Assert-Equal $true $missingDefaultManifestRejected 'Default remote adapter fails closed before Windows actions when the trusted file manifest is absent'

        $driftedHashResult = Invoke-CountdownIssue4DefaultAdapterRejection -Fixture (
            New-CountdownIssue4DefaultAdapterPackage `
                -Name 'hash-drift' `
                -DriftReleaseContractAfterManifest)
        $missingPackageMemberResult = Invoke-CountdownIssue4DefaultAdapterRejection -Fixture (
            New-CountdownIssue4DefaultAdapterPackage `
                -Name 'missing-package-member' `
                -IncludeExtraPackageFile `
                -OmitExtraManifestEntry)
        $extraManifestMemberResult = Invoke-CountdownIssue4DefaultAdapterRejection -Fixture (
            New-CountdownIssue4DefaultAdapterPackage `
                -Name 'extra-manifest-member' `
                -IncludeUnexpectedManifestEntry)
        $contractHashConflictResult = Invoke-CountdownIssue4DefaultAdapterRejection -Fixture (
            New-CountdownIssue4DefaultAdapterPackage `
                -Name 'contract-hash-conflict' `
                -ContractHashConflict)

        foreach ($case in @(
                [pscustomobject]@{
                    Name = 'Hash drift before source locking'
                    Result = $driftedHashResult
                    Pattern = '锁定远端部署源文件：受信文件 SHA-256 不匹配：Countdown\.ReleaseContract\.psm1'
                },
                [pscustomobject]@{
                    Name = 'Missing non-entry package member'
                    Result = $missingPackageMemberResult
                    Pattern = '远端部署预期文件 manifest 与发布契约文件数量不一致'
                },
                [pscustomobject]@{
                    Name = 'Extra manifest member and count drift'
                    Result = $extraManifestMemberResult
                    Pattern = '远端部署预期文件 manifest 与发布契约文件数量不一致'
                },
                [pscustomobject]@{
                    Name = 'Manifest and release contract hash conflict'
                    Result = $contractHashConflictResult
                    Pattern = '远端部署预期文件 manifest 与发布契约哈希不一致：Countdown\.InstallTransaction\.psm1'
                })) {
            Assert-Equal $true ($case.Result.Message -match $case.Pattern) "$($case.Name) fails closed with the expected reason"
            Assert-Equal $false ([bool] $case.Result.TransactionModuleImported) "$($case.Name) fails before the install transaction module is imported"
            Assert-Equal $false ([bool] $case.Result.InstallStarted) "$($case.Name) fails before the installer starts"
            Assert-Equal $false ([bool] $case.Result.InstallTransactionSentinel) "$($case.Name) never executes the install transaction sentinel"
            Assert-Equal $false ([bool] $case.Result.InstallerSentinel) "$($case.Name) never executes the installer sentinel"
            Assert-Equal $false ([bool] $case.Result.WindowsActionSentinel) "$($case.Name) fails before Windows actions"
            Assert-Equal $false ([bool] $case.Result.MaliciousReleaseSentinel) "$($case.Name) never executes drifted release-contract bytes"
        }

        $acceptLockRoot = Join-Path $remoteDeploymentTestRoot 'issue4-accept-locks'
        $acceptSourceRoot = Join-Path $acceptLockRoot 'source'
        $acceptInstallRoot = Join-Path $acceptLockRoot 'install'
        New-Item -ItemType Directory -Path $acceptSourceRoot -Force | Out-Null
        New-Item -ItemType Directory -Path $acceptInstallRoot -Force | Out-Null
        foreach ($file in @(
                'Countdown.InstallTransaction.psm1',
                'Countdown.Runtime.psm1',
                'Countdown.TransactionAcceptance.psm1')) {
            Copy-Item `
                -LiteralPath (Join-Path $remoteDeploymentSource $file) `
                -Destination (Join-Path $acceptSourceRoot $file) `
                -Force
        }
        [System.IO.File]::WriteAllText(
            (Join-Path $acceptSourceRoot 'Countdown.TransactionAcceptance.psm1'),
            @'
function Test-CountdownIssue4AcceptReplacementRejected {
    param(
        [string] $DestinationPath,
        [string] $ReplacementPath,
        [string] $BackupPath
    )

    try {
        [System.IO.File]::Replace($ReplacementPath, $DestinationPath, $BackupPath, $true)
    }
    catch {
        return $true
    }
    [System.IO.File]::Replace($BackupPath, $DestinationPath, $ReplacementPath, $true)
    $false
}

function Invoke-CountdownTransactionAcceptance {
    param(
        [string] $StartupRequestPath,
        [string] $RuntimeStatusPath,
        [string] $AcceptanceStatusPath,
        [string] $LaunchToken,
        [object] $ExpectedInteractiveContext,
        [int] $ExpectedProcessId,
        [int] $ExpectedSessionId,
        [string] $ExpectedApplicationPath,
        [string] $TaskName,
        [string] $ExpectedScheduledTaskIdentity,
        [string] $SettingsSnapshotPath,
        [string] $SettingsPath,
        [scriptblock] $GetInteractiveContext,
        [scriptblock] $GetScheduledTask,
        [scriptblock] $GetScheduledTaskIdentity,
        [scriptblock] $CompleteTransaction,
        [scriptblock] $RemoveCommittedTransaction
    )

    $global:CountdownIssue4AcceptDuringSourceReplacementRejected =
        Test-CountdownIssue4AcceptReplacementRejected `
            -DestinationPath $global:CountdownIssue4AcceptSourceProbePath `
            -ReplacementPath $global:CountdownIssue4AcceptSourceReplacementPath `
            -BackupPath $global:CountdownIssue4AcceptDuringSourceBackupPath
    $global:CountdownIssue4AcceptDuringInstalledReplacementRejected =
        Test-CountdownIssue4AcceptReplacementRejected `
            -DestinationPath $global:CountdownIssue4AcceptInstalledProbePath `
            -ReplacementPath $global:CountdownIssue4AcceptInstalledReplacementPath `
            -BackupPath $global:CountdownIssue4AcceptDuringInstalledBackupPath

    [void] (& $CompleteTransaction)
    [void] (& $RemoveCommittedTransaction)
    [pscustomobject]@{
        AcceptanceState = 'finalized-ready'
        RuntimeProcessId = 4242
        TaskState = 'Ready'
        LastTaskResult = 0
        SettingsHashBefore = ('A' * 64)
        SettingsHashAfter = ('B' * 64)
        SettingsLengthBefore = 1
        SettingsLengthAfter = 2
        SettingsMtimeUtcBefore = '2026-01-01T00:00:00.0000000Z'
        SettingsMtimeUtcAfter = '2026-01-02T00:00:00.0000000Z'
        SettingsUnchanged = $false
    }
}

Export-ModuleMember -Function Invoke-CountdownTransactionAcceptance
'@,
            [System.Text.UTF8Encoding]::new($false))
        Copy-Item `
            -LiteralPath (Join-Path $remoteDeploymentInstall 'Countdown.Settings.psm1') `
            -Destination (Join-Path $acceptInstallRoot 'Countdown.Settings.psm1') `
            -Force
        $acceptSourceProbePath = Join-Path $acceptSourceRoot 'issue4-source-lock.txt'
        $acceptSourceReplacementPath = Join-Path $acceptSourceRoot 'issue4-source-replacement.txt'
        $acceptInstalledProbePath = Join-Path $acceptInstallRoot 'issue4-installed-lock.txt'
        $acceptInstalledReplacementPath = Join-Path $acceptInstallRoot 'issue4-installed-replacement.txt'
        [System.IO.File]::WriteAllText($acceptSourceProbePath, 'source-benign', [System.Text.UTF8Encoding]::new($false))
        [System.IO.File]::WriteAllText($acceptSourceReplacementPath, 'source-malicious', [System.Text.UTF8Encoding]::new($false))
        [System.IO.File]::WriteAllText($acceptInstalledProbePath, 'installed-benign', [System.Text.UTF8Encoding]::new($false))
        [System.IO.File]::WriteAllText($acceptInstalledReplacementPath, 'installed-malicious', [System.Text.UTF8Encoding]::new($false))
        $acceptManifest = [ordered]@{}
        foreach ($file in @(
                'Countdown.InstallTransaction.psm1',
                'Countdown.Runtime.psm1',
                'Countdown.TransactionAcceptance.psm1',
                'issue4-source-lock.txt')) {
            $acceptManifest[$file] = (Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $acceptSourceRoot $file)).Hash
        }
        foreach ($file in @('Countdown.Settings.psm1', 'issue4-installed-lock.txt')) {
            $acceptManifest[$file] = (Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $acceptInstallRoot $file)).Hash
        }
        $acceptAdapter = & $remoteDeploymentModule { New-CountdownRemoteDeploymentWindowsAdapter }
        $acceptTransactionPath = Join-Path $acceptLockRoot 'transaction'
        New-Item -ItemType Directory -Path $acceptTransactionPath -Force | Out-Null
        $acceptAdapter.State.RollbackTransactionPath = $acceptTransactionPath
        $acceptAdapter.State.InteractiveContext = [pscustomobject]@{
            User = 'MINMIN\Issue4AcceptLocks'
            SessionId = 7
            Desktop = Join-Path $acceptLockRoot 'desktop'
        }
        $acceptAdapter.State.LauncherPath = Join-Path $acceptLockRoot 'desktop\敏敏子倒计时.exe'
        $acceptAdapter.State.LegacyLauncherPath = Join-Path $acceptLockRoot 'desktop\敏敏子倒计时.cmd'
        $acceptAdapter.State.LauncherTempPath = "$($acceptAdapter.State.LauncherPath).new"
        $acceptAdapter.State.SettingsSnapshotPath = Join-Path $acceptTransactionPath 'install-backup\settings.json'
        $acceptAdapter.State.SettingsPath = Join-Path $acceptInstallRoot 'settings.json'
        $acceptAdapter.State.StartupRequestPath = Join-Path $acceptInstallRoot 'startup-request.json'
        $acceptAdapter.State.RuntimeStatusPath = Join-Path $acceptInstallRoot 'runtime-status.json'
        $acceptAdapter.State.AcceptanceStatusPath = Join-Path $acceptInstallRoot 'acceptance-status.json'
        $acceptAdapter.State.LaunchToken = 'issue4-accept-lock-token'
        $acceptAdapter.State.ExpectedProcessId = 4242
        $acceptAdapter.State.ExpectedScheduledTaskIdentity = '<Task />'
        $acceptContext = [pscustomobject]@{
            SourceRoot = $acceptSourceRoot
            InstallPath = $acceptInstallRoot
            PowerShell7Path = $remoteDeploymentPowerShell
            DeferredReceiptPath = Join-Path $acceptLockRoot 'deferred-install-receipt.json'
            TaskName = 'Issue4 Accept Locks'
            LegacyTaskName = 'Issue4 Accept Locks Legacy'
            ExpectedFileSha256 = $acceptManifest
            TrustedFileManifestRequired = $true
            AdapterState = $acceptAdapter.State
        }
        $acceptAdapter.State.SourceFileLocks = & $remoteDeploymentModule {
            param($root, $manifest)
            Enter-CountdownRemoteDeploymentFileLocks `
                -Root $root `
                -ExpectedFileSha256 $manifest `
                -RelativePaths ([string[]] @(
                        'Countdown.InstallTransaction.psm1',
                        'Countdown.Runtime.psm1',
                        'Countdown.TransactionAcceptance.psm1',
                        'issue4-source-lock.txt')) `
                -Stage 'Issue #4 Accept source locks'
        } $acceptSourceRoot $acceptManifest
        $acceptAdapter.State.InstalledFileLocks = & $remoteDeploymentModule {
            param($root, $manifest)
            Enter-CountdownRemoteDeploymentFileLocks `
                -Root $root `
                -ExpectedFileSha256 $manifest `
                -RelativePaths ([string[]] @('Countdown.Settings.psm1', 'issue4-installed-lock.txt')) `
                -Stage 'Issue #4 Accept installed locks'
        } $acceptInstallRoot $acceptManifest
        $acceptSourceReplacementRejected = -not (Invoke-CountdownIssue4AtomicReplacementProbe `
                -DestinationPath $acceptSourceProbePath `
                -ReplacementPath $acceptSourceReplacementPath `
                -BackupPath (Join-Path $acceptSourceRoot 'issue4-source-backup.txt'))
        $acceptInstalledReplacementRejected = -not (Invoke-CountdownIssue4AtomicReplacementProbe `
                -DestinationPath $acceptInstalledProbePath `
                -ReplacementPath $acceptInstalledReplacementPath `
                -BackupPath (Join-Path $acceptInstallRoot 'issue4-installed-backup.txt'))
        $global:CountdownIssue4AcceptSourceProbePath = $acceptSourceProbePath
        $global:CountdownIssue4AcceptSourceReplacementPath = $acceptSourceReplacementPath
        $global:CountdownIssue4AcceptInstalledProbePath = $acceptInstalledProbePath
        $global:CountdownIssue4AcceptInstalledReplacementPath = $acceptInstalledReplacementPath
        $global:CountdownIssue4AcceptDuringSourceBackupPath = Join-Path $acceptSourceRoot 'issue4-source-backup-during-accept.txt'
        $global:CountdownIssue4AcceptDuringInstalledBackupPath = Join-Path $acceptInstallRoot 'issue4-installed-backup-during-accept.txt'
        $global:CountdownIssue4AcceptDuringSourceReplacementRejected = $false
        $global:CountdownIssue4AcceptDuringInstalledReplacementRejected = $false
        $acceptResult = $null
        $acceptDuringSourceReplacementRejected = $false
        $acceptDuringInstalledReplacementRejected = $false
        $acceptAfterSourceReplacementRejected = $false
        $acceptAfterInstalledReplacementRejected = $false
        try {
            $acceptResult = & $acceptAdapter.Accept $acceptAdapter.State $acceptContext ([pscustomobject]@{})
            $acceptDuringSourceReplacementRejected = [bool] $global:CountdownIssue4AcceptDuringSourceReplacementRejected
            $acceptDuringInstalledReplacementRejected = [bool] $global:CountdownIssue4AcceptDuringInstalledReplacementRejected
            $acceptAfterSourceReplacementRejected = -not (Invoke-CountdownIssue4AtomicReplacementProbe `
                    -DestinationPath $acceptSourceProbePath `
                    -ReplacementPath $acceptSourceReplacementPath `
                    -BackupPath (Join-Path $acceptSourceRoot 'issue4-source-backup-after-accept.txt'))
            $acceptAfterInstalledReplacementRejected = -not (Invoke-CountdownIssue4AtomicReplacementProbe `
                    -DestinationPath $acceptInstalledProbePath `
                    -ReplacementPath $acceptInstalledReplacementPath `
                    -BackupPath (Join-Path $acceptInstallRoot 'issue4-installed-backup-after-accept.txt'))
        }
        finally {
            [void] (& $acceptAdapter.Dispose $acceptAdapter.State $acceptContext)
            foreach ($name in @(
                    'CountdownIssue4AcceptSourceProbePath',
                    'CountdownIssue4AcceptSourceReplacementPath',
                    'CountdownIssue4AcceptInstalledProbePath',
                    'CountdownIssue4AcceptInstalledReplacementPath',
                    'CountdownIssue4AcceptDuringSourceBackupPath',
                    'CountdownIssue4AcceptDuringInstalledBackupPath',
                    'CountdownIssue4AcceptDuringSourceReplacementRejected',
                    'CountdownIssue4AcceptDuringInstalledReplacementRejected')) {
                Remove-Variable -Name $name -Scope Global -ErrorAction SilentlyContinue
            }
            Import-Module (Join-Path $PSScriptRoot 'Countdown.InstallTransaction.psm1') -Force -DisableNameChecking
            Import-Module (Join-Path $PSScriptRoot 'Countdown.Runtime.psm1') -Force
            Import-Module (Join-Path $PSScriptRoot 'Countdown.Settings.psm1') -Force
            Import-Module (Join-Path $PSScriptRoot 'Countdown.TransactionAcceptance.psm1') -Force
        }
        Assert-Equal $true $acceptSourceReplacementRejected 'Default adapter keeps source locks through the point immediately before Accept'
        Assert-Equal $true $acceptInstalledReplacementRejected 'Default adapter keeps installed locks through the point immediately before Accept'
        Assert-Equal $true $acceptDuringSourceReplacementRejected 'Default adapter rejects source replacement inside the Accept behavior chain'
        Assert-Equal $true $acceptDuringInstalledReplacementRejected 'Default adapter rejects installed-file replacement inside the Accept behavior chain'
        Assert-Equal 'finalized-ready' ([string] $acceptResult.AcceptanceState) 'Default adapter Accept completes while source and installed files remain locked'
        Assert-Equal $true $acceptAfterSourceReplacementRejected 'Default adapter keeps source locks after Accept returns and before Dispose'
        Assert-Equal $true $acceptAfterInstalledReplacementRejected 'Default adapter keeps installed locks after Accept returns and before Dispose'
        Assert-Equal $null $acceptAdapter.State.SourceFileLocks 'Default adapter Dispose clears source lock state'
        Assert-Equal $null $acceptAdapter.State.InstalledFileLocks 'Default adapter Dispose clears installed lock state'
        Assert-Equal $true (Invoke-CountdownIssue4AtomicReplacementProbe `
                -DestinationPath $acceptSourceProbePath `
                -ReplacementPath $acceptSourceReplacementPath `
                -BackupPath (Join-Path $acceptSourceRoot 'issue4-source-backup-after-dispose.txt') `
                -Restore) 'Default adapter Dispose releases source locks'
        Assert-Equal $true (Invoke-CountdownIssue4AtomicReplacementProbe `
                -DestinationPath $acceptInstalledProbePath `
                -ReplacementPath $acceptInstalledReplacementPath `
                -BackupPath (Join-Path $acceptInstallRoot 'issue4-installed-backup-after-dispose.txt') `
                -Restore) 'Default adapter Dispose releases installed locks'

        $rollbackLockRoot = Join-Path $remoteDeploymentTestRoot 'issue4-rollback-locks'
        $rollbackSourceRoot = Join-Path $rollbackLockRoot 'source'
        $rollbackInstallRoot = Join-Path $rollbackLockRoot 'install'
        $rollbackTransactionPath = Join-Path $rollbackLockRoot 'transaction'
        New-Item -ItemType Directory -Path $rollbackSourceRoot -Force | Out-Null
        New-Item -ItemType Directory -Path $rollbackInstallRoot -Force | Out-Null
        New-Item -ItemType Directory -Path $rollbackTransactionPath -Force | Out-Null
        [System.IO.File]::WriteAllText(
            (Join-Path $rollbackTransactionPath 'journal.json'),
            '{}',
            [System.Text.UTF8Encoding]::new($false))
        $rollbackTransactionModulePath = Join-Path $rollbackSourceRoot 'Countdown.InstallTransaction.psm1'
        [System.IO.File]::WriteAllText(
            $rollbackTransactionModulePath,
            @'
function Undo-CountdownInstallTransaction {
    param(
        [string] $TransactionPath,
        [string] $PowerShell7Path,
        [string] $ExpectedInstallPath,
        [string] $ExpectedTaskName,
        [string] $ExpectedLegacyTaskName,
        [string] $ExpectedLauncherPath,
        [string] $ExpectedLegacyLauncherPath,
        [string] $ExpectedLauncherTempPath,
        [switch] $PreserveUnmanagedLaunchers
    )

    try {
        [System.IO.File]::Replace(
            $global:CountdownIssue4RollbackInstalledReplacementPath,
            $global:CountdownIssue4RollbackInstalledProbePath,
            $global:CountdownIssue4RollbackInstalledBackupPath,
            $true)
        $global:CountdownIssue4RollbackInstalledReplacementSucceeded = $true
        [System.IO.File]::Replace(
            $global:CountdownIssue4RollbackInstalledBackupPath,
            $global:CountdownIssue4RollbackInstalledProbePath,
            $global:CountdownIssue4RollbackInstalledReplacementPath,
            $true)
    }
    catch {
        $global:CountdownIssue4RollbackInstalledReplacementSucceeded = $false
    }
    try {
        [System.IO.File]::Replace(
            $global:CountdownIssue4RollbackSourceReplacementPath,
            $global:CountdownIssue4RollbackSourceProbePath,
            $global:CountdownIssue4RollbackSourceBackupPath,
            $true)
        $global:CountdownIssue4RollbackSourceReplacementRejected = $false
    }
    catch {
        $global:CountdownIssue4RollbackSourceReplacementRejected = $true
    }
    [pscustomobject]@{ PreservedUnmanagedLaunchers = $false }
}

Export-ModuleMember -Function Undo-CountdownInstallTransaction
'@,
            [System.Text.UTF8Encoding]::new($false))
        $rollbackSourceProbePath = Join-Path $rollbackSourceRoot 'issue4-source-lock.txt'
        $rollbackSourceReplacementPath = Join-Path $rollbackSourceRoot 'issue4-source-replacement.txt'
        $rollbackInstalledProbePath = Join-Path $rollbackInstallRoot 'issue4-installed-lock.txt'
        $rollbackInstalledReplacementPath = Join-Path $rollbackInstallRoot 'issue4-installed-replacement.txt'
        [System.IO.File]::WriteAllText($rollbackSourceProbePath, 'source-benign', [System.Text.UTF8Encoding]::new($false))
        [System.IO.File]::WriteAllText($rollbackSourceReplacementPath, 'source-malicious', [System.Text.UTF8Encoding]::new($false))
        [System.IO.File]::WriteAllText($rollbackInstalledProbePath, 'installed-benign', [System.Text.UTF8Encoding]::new($false))
        [System.IO.File]::WriteAllText($rollbackInstalledReplacementPath, 'installed-malicious', [System.Text.UTF8Encoding]::new($false))
        $global:CountdownIssue4RollbackInstalledProbePath = $rollbackInstalledProbePath
        $global:CountdownIssue4RollbackInstalledReplacementPath = $rollbackInstalledReplacementPath
        $global:CountdownIssue4RollbackInstalledBackupPath = Join-Path $rollbackInstallRoot 'issue4-installed-backup.txt'
        $global:CountdownIssue4RollbackSourceProbePath = $rollbackSourceProbePath
        $global:CountdownIssue4RollbackSourceReplacementPath = $rollbackSourceReplacementPath
        $global:CountdownIssue4RollbackSourceBackupPath = Join-Path $rollbackSourceRoot 'issue4-source-backup.txt'
        $global:CountdownIssue4RollbackInstalledReplacementSucceeded = $false
        $global:CountdownIssue4RollbackSourceReplacementRejected = $false
        $rollbackManifest = [ordered]@{
            'Countdown.InstallTransaction.psm1' = (Get-FileHash -Algorithm SHA256 -LiteralPath $rollbackTransactionModulePath).Hash
            'issue4-source-lock.txt' = (Get-FileHash -Algorithm SHA256 -LiteralPath $rollbackSourceProbePath).Hash
            'issue4-installed-lock.txt' = (Get-FileHash -Algorithm SHA256 -LiteralPath $rollbackInstalledProbePath).Hash
        }
        $rollbackAdapter = & $remoteDeploymentModule { New-CountdownRemoteDeploymentWindowsAdapter }
        $rollbackAdapter.State.TransactionModuleImported = $true
        $rollbackAdapter.State.InstallStarted = $true
        $rollbackAdapter.State.RollbackTransactionPath = $rollbackTransactionPath
        $rollbackAdapter.State.LauncherPath = Join-Path $rollbackLockRoot 'desktop\敏敏子倒计时.exe'
        $rollbackAdapter.State.LegacyLauncherPath = Join-Path $rollbackLockRoot 'desktop\敏敏子倒计时.cmd'
        $rollbackAdapter.State.LauncherTempPath = "$($rollbackAdapter.State.LauncherPath).new"
        $rollbackContext = [pscustomobject]@{
            SourceRoot = $rollbackSourceRoot
            InstallPath = $rollbackInstallRoot
            PowerShell7Path = $remoteDeploymentPowerShell
            DeferredReceiptPath = Join-Path $rollbackLockRoot 'deferred-install-receipt.json'
            TaskName = 'Issue4 Rollback Locks'
            LegacyTaskName = 'Issue4 Rollback Locks Legacy'
            ExpectedFileSha256 = $rollbackManifest
            TrustedFileManifestRequired = $true
            AdapterState = $rollbackAdapter.State
        }
        $rollbackAdapter.State.SourceFileLocks = & $remoteDeploymentModule {
            param($root, $manifest)
            Enter-CountdownRemoteDeploymentFileLocks `
                -Root $root `
                -ExpectedFileSha256 $manifest `
                -RelativePaths ([string[]] @('Countdown.InstallTransaction.psm1', 'issue4-source-lock.txt')) `
                -Stage 'Issue #4 Rollback source locks'
        } $rollbackSourceRoot $rollbackManifest
        $rollbackAdapter.State.InstalledFileLocks = & $remoteDeploymentModule {
            param($root, $manifest)
            Enter-CountdownRemoteDeploymentFileLocks `
                -Root $root `
                -ExpectedFileSha256 $manifest `
                -RelativePaths ([string[]] @('issue4-installed-lock.txt')) `
                -Stage 'Issue #4 Rollback installed locks'
        } $rollbackInstallRoot $rollbackManifest
        try {
            [void] (& $rollbackAdapter.Rollback $rollbackAdapter.State $rollbackContext ([pscustomobject]@{}))
            Assert-Equal $true ([bool] $global:CountdownIssue4RollbackInstalledReplacementSucceeded) 'Default adapter Rollback releases installed locks before restoring the install directory'
            Assert-Equal $true ([bool] $global:CountdownIssue4RollbackSourceReplacementRejected) 'Default adapter Rollback retains source locks while restoring the install directory'
            Assert-Equal $null $rollbackAdapter.State.InstalledFileLocks 'Default adapter Rollback clears installed lock state before restoration'
            Assert-Equal $true ($null -ne $rollbackAdapter.State.SourceFileLocks) 'Default adapter Rollback keeps source lock state until Dispose'
        }
        finally {
            [void] (& $rollbackAdapter.Dispose $rollbackAdapter.State $rollbackContext)
            foreach ($name in @(
                    'CountdownIssue4RollbackInstalledProbePath',
                    'CountdownIssue4RollbackInstalledReplacementPath',
                    'CountdownIssue4RollbackInstalledBackupPath',
                    'CountdownIssue4RollbackSourceProbePath',
                    'CountdownIssue4RollbackSourceReplacementPath',
                    'CountdownIssue4RollbackSourceBackupPath',
                    'CountdownIssue4RollbackInstalledReplacementSucceeded',
                    'CountdownIssue4RollbackSourceReplacementRejected')) {
                Remove-Variable -Name $name -Scope Global -ErrorAction SilentlyContinue
            }
            Import-Module (Join-Path $PSScriptRoot 'Countdown.InstallTransaction.psm1') -Force -DisableNameChecking
        }
        Assert-Equal $null $rollbackAdapter.State.SourceFileLocks 'Default adapter Dispose clears the source locks retained by Rollback'
        Assert-Equal $true (Invoke-CountdownIssue4AtomicReplacementProbe `
                -DestinationPath $rollbackSourceProbePath `
                -ReplacementPath $rollbackSourceReplacementPath `
                -BackupPath (Join-Path $rollbackSourceRoot 'issue4-source-backup-after-dispose.txt') `
                -Restore) 'Default adapter Dispose finally releases Rollback source locks'

        $missingAcceptancePath = Join-Path $remoteDeploymentTestRoot 'missing-acceptance-status.json'
        $expectedAcceptanceApplicationPath = Join-Path $remoteDeploymentInstall '敏敏子倒计时.ps1'
        $expectedAcceptanceExecutablePath = Join-Path $remoteDeploymentInstall 'app\MinminCountdown.exe'
        $aliveMissingAcceptanceMessage = $null
        try {
            Wait-CountdownAcceptanceReadyReceipt `
                -Path $missingAcceptancePath `
                -LaunchToken 'missing-acceptance-token' `
                -ExpectedProcessId 4242 `
                -ExpectedSessionId 7 `
                -ExpectedApplicationPath $expectedAcceptanceApplicationPath `
                -TimeoutSeconds 0 `
                -PollMilliseconds 1 `
                -ProcessProbe {
                    param([int] $ProcessId)
                    [pscustomobject]@{
                        ProcessId = $ProcessId
                        SessionId = 7
                        ExecutablePath = $expectedAcceptanceExecutablePath
                    }
                } | Out-Null
        }
        catch {
            $aliveMissingAcceptanceMessage = $_.Exception.Message
        }
        Assert-Equal $true ($aliveMissingAcceptanceMessage -match '回执不存在.*期望进程 4242 仍在运行.*会话和可执行路径匹配') 'Acceptance wait distinguishes a living candidate process when the receipt is absent'

        $deadMissingAcceptanceMessage = $null
        try {
            Wait-CountdownAcceptanceReadyReceipt `
                -Path $missingAcceptancePath `
                -LaunchToken 'missing-acceptance-token' `
                -ExpectedProcessId 4242 `
                -ExpectedSessionId 7 `
                -ExpectedApplicationPath $expectedAcceptanceApplicationPath `
                -TimeoutSeconds 0 `
                -PollMilliseconds 1 `
                -ProcessProbe { param([int] $ProcessId) $null } | Out-Null
        }
        catch {
            $deadMissingAcceptanceMessage = $_.Exception.Message
        }
        Assert-Equal $true ($deadMissingAcceptanceMessage -match '回执不存在.*期望进程 4242 不存在') 'Acceptance wait distinguishes a missing candidate process when the receipt is absent'

        $successFixture = New-CountdownRemoteDeploymentFixtureAdapter -Mode Success
        $successResult = Invoke-CountdownRemoteDeployment `
            -SourceRoot $remoteDeploymentSource `
            -InstallPath $remoteDeploymentInstall `
            -PowerShell7Path $remoteDeploymentPowerShell `
            -DeferredReceiptPath $remoteDeploymentReceipt `
            -TaskName 'Minmin Countdown Fixture' `
            -LegacyTaskName '敏敏子倒计时 Fixture' `
            -Adapter $successFixture.Adapter
        Assert-Equal $true ([bool] $successResult.Valid) 'Remote deployment interface reports success only after acceptance'
        Assert-Equal 'finalized-ready' ([string] $successResult.AcceptanceState) 'Remote deployment interface exposes finalized-ready'
        Assert-Equal 11 ([int] $successResult.CoreTests) 'Remote deployment interface exposes stable Core evidence'
        Assert-Equal 13 ([int] $successResult.CSharpTests) 'Remote deployment interface exposes stable C# evidence'
        Assert-Equal 17 ([int] $successResult.UiTests) 'Remote deployment interface exposes stable compatibility UI evidence'
        Assert-Equal $true ([bool] $successFixture.State.Accepted) 'Remote deployment success reaches acceptance'
        Assert-Equal $false ([bool] $successFixture.State.RolledBack) 'Remote deployment success does not roll back'
        Assert-Equal $true ([bool] $successFixture.State.Disposed) 'Remote deployment success releases its host adapter'

        $expectedDeploymentBindingHash = '9' * 64
        $bindingTestRoot = Join-Path $remoteDeploymentTestRoot 'binding-success'
        New-Item -ItemType Directory -Path $bindingTestRoot -Force | Out-Null
        $remoteBindingPath = Join-Path $bindingTestRoot 'deployment-binding.json'
        $bindingFixture = New-CountdownRemoteDeploymentFixtureAdapter -Mode Success
        $bindingResult = Invoke-CountdownRemoteDeployment `
            -SourceRoot $remoteDeploymentSource `
            -InstallPath $remoteDeploymentInstall `
            -PowerShell7Path $remoteDeploymentPowerShell `
            -DeferredReceiptPath (Join-Path $bindingTestRoot 'deferred-install-receipt.json') `
            -TaskName 'Minmin Countdown Fixture' `
            -LegacyTaskName '敏敏子倒计时 Fixture' `
            -DeploymentBindingPath $remoteBindingPath `
            -DeploymentBindingSha256 $expectedDeploymentBindingHash `
            -Adapter $bindingFixture.Adapter
        $bindingBytes = [System.IO.File]::ReadAllBytes($remoteBindingPath)
        $bindingDocument = ConvertFrom-CountdownRemoteDeploymentBindingJson `
            -Json ([System.Text.UTF8Encoding]::new($false, $true).GetString($bindingBytes)) `
            -ExpectedDeploymentBindingSha256 $expectedDeploymentBindingHash `
            -ExpectedProcessId 4242
        $expectedFixtureTokenHash = [Convert]::ToHexString(
            [System.Security.Cryptography.SHA256]::HashData(
                [System.Text.UTF8Encoding]::new($false).GetBytes('sensitive-launch-token-sentinel')))
        Assert-Equal 20 @($bindingResult.PSObject.Properties).Count 'Deployment binding does not change the strict success receipt schema'
        Assert-Equal 4 @($bindingDocument.PSObject.Properties).Count 'Deployment binding sidecar uses the exact independent schema'
        Assert-Equal $expectedDeploymentBindingHash ([string] $bindingDocument.DeploymentBindingSha256) 'Deployment binding sidecar preserves the locally generated nonce hash'
        Assert-Equal $expectedFixtureTokenHash ([string] $bindingDocument.TokenSha256) 'Deployment binding sidecar binds the committed launch token hash'
        Assert-Equal $false ([bool] $bindingFixture.State.RolledBack) 'Successful binding publication never rolls back a committed deployment'

        $bindingFailureTestRoot = Join-Path $remoteDeploymentTestRoot 'binding-failure'
        New-Item -ItemType Directory -Path $bindingFailureTestRoot -Force | Out-Null
        $bindingFailurePath = Join-Path $bindingFailureTestRoot 'deployment-binding-existing.json'
        [System.IO.File]::WriteAllText($bindingFailurePath, 'existing', [System.Text.UTF8Encoding]::new($false))
        $bindingFailureFixture = New-CountdownRemoteDeploymentFixtureAdapter -Mode Success
        $bindingFailureMessage = $null
        try {
            Invoke-CountdownRemoteDeployment `
                -SourceRoot $remoteDeploymentSource `
                -InstallPath $remoteDeploymentInstall `
                -PowerShell7Path $remoteDeploymentPowerShell `
                -DeferredReceiptPath (Join-Path $bindingFailureTestRoot 'deferred-install-receipt.json') `
                -TaskName 'Minmin Countdown Fixture' `
                -LegacyTaskName '敏敏子倒计时 Fixture' `
                -DeploymentBindingPath $bindingFailurePath `
                -DeploymentBindingSha256 $expectedDeploymentBindingHash `
                -Adapter $bindingFailureFixture.Adapter | Out-Null
        }
        catch { $bindingFailureMessage = $_.Exception.Message }
        Assert-Equal $true ($bindingFailureMessage -match '部署已提交.*未回滚.*binding') 'Binding publication failure reports a committed deployment with incomplete verification'
        Assert-Equal 0 ([int] $bindingFailureFixture.State.RollbackCount) 'Binding publication failure never invokes the rollback seam after commit'
        Assert-Equal 'existing' ([System.IO.File]::ReadAllText($bindingFailurePath, [System.Text.Encoding]::UTF8)) 'Binding publication failure preserves existing bytes'

        $successReceiptJson = $successResult | ConvertTo-Json -Compress -Depth 4
        $normalizedSuccessReceipt = ConvertFrom-CountdownRemoteDeploymentSuccessReceiptJson -Json $successReceiptJson
        Assert-Equal 20 @($normalizedSuccessReceipt.PSObject.Properties).Count 'Success receipt parser returns the exact schema field set'
        Assert-Equal 'Ready' ([string] $normalizedSuccessReceipt.TaskState) 'Success receipt parser requires the ready task state'
        Assert-Equal 0 ([int] $normalizedSuccessReceipt.LastTaskResult) 'Success receipt parser requires lastTaskResult 0'
        Assert-Equal 'window-ready' ([string] $normalizedSuccessReceipt.RuntimeStage) 'Success receipt parser requires window-ready runtime state'
        Assert-Equal $false ($normalizedSuccessReceipt.PSObject.Properties.Name -contains 'LaunchToken') 'Success receipt schema excludes the raw launch token'
        $unchangedTrueReceipt = $successReceiptJson | ConvertFrom-Json -AsHashtable
        $unchangedTrueReceipt.SettingsHashAfter = $unchangedTrueReceipt.SettingsHashBefore
        $unchangedTrueReceipt.SettingsLengthAfter = $unchangedTrueReceipt.SettingsLengthBefore
        $unchangedTrueReceipt.SettingsMtimeUtcAfter = $unchangedTrueReceipt.SettingsMtimeUtcBefore
        $unchangedTrueReceipt.SettingsUnchanged = $true
        $acceptedUnchangedReceipt = ConvertFrom-CountdownRemoteDeploymentSuccessReceiptJson `
            -Json ($unchangedTrueReceipt | ConvertTo-Json -Compress)
        Assert-Equal $true ([bool] $acceptedUnchangedReceipt.SettingsUnchanged) 'Success receipt parser accepts SettingsUnchanged computed from identical fingerprints'

        $invalidSuccessReceiptCases = [ordered]@{}
        $missingFieldReceipt = $successReceiptJson | ConvertFrom-Json -AsHashtable
        [void] $missingFieldReceipt.Remove('UiTests')
        $invalidSuccessReceiptCases['missing field'] = $missingFieldReceipt | ConvertTo-Json -Compress
        $extraFieldReceipt = $successReceiptJson | ConvertFrom-Json -AsHashtable
        $extraFieldReceipt['Credential'] = 'must-not-be-persisted'
        $invalidSuccessReceiptCases['extra field'] = $extraFieldReceipt | ConvertTo-Json -Compress
        $invalidSuccessReceiptCases['truncated JSON'] = $successReceiptJson.Substring(0, $successReceiptJson.Length - 1)
        $invalidSuccessReceiptCases['multiple objects'] = "$successReceiptJson`n$successReceiptJson"
        $invalidSuccessReceiptCases['noise output'] = "unexpected output`n$successReceiptJson"
        $invalidSuccessReceiptCases['duplicate field'] = $successReceiptJson.Insert(1, '"Valid":true,')
        $unsupportedSchemaReceipt = $successReceiptJson | ConvertFrom-Json -AsHashtable
        $unsupportedSchemaReceipt.SchemaVersion = 2
        $invalidSuccessReceiptCases['unsupported schema'] = $unsupportedSchemaReceipt | ConvertTo-Json -Compress
        $failedStatusReceipt = $successReceiptJson | ConvertFrom-Json -AsHashtable
        $failedStatusReceipt.Valid = $false
        $invalidSuccessReceiptCases['failed status'] = $failedStatusReceipt | ConvertTo-Json -Compress
        $wrongTypeReceipt = $successReceiptJson | ConvertFrom-Json -AsHashtable
        $wrongTypeReceipt.CoreTests = '11'
        $invalidSuccessReceiptCases['wrong type'] = $wrongTypeReceipt | ConvertTo-Json -Compress
        $invalidRangeReceipt = $successReceiptJson | ConvertFrom-Json -AsHashtable
        $invalidRangeReceipt.RuntimeProcessId = 0
        $invalidSuccessReceiptCases['invalid range'] = $invalidRangeReceipt | ConvertTo-Json -Compress
        $invalidStateReceipt = $successReceiptJson | ConvertFrom-Json -AsHashtable
        $invalidStateReceipt.AcceptanceState = 'accepted'
        $invalidSuccessReceiptCases['invalid state'] = $invalidStateReceipt | ConvertTo-Json -Compress
        $runningTaskReceipt = $successReceiptJson | ConvertFrom-Json -AsHashtable
        $runningTaskReceipt.TaskState = 'Running'
        $invalidSuccessReceiptCases['task state running'] = $runningTaskReceipt | ConvertTo-Json -Compress
        $nonzeroLastTaskReceipt = $successReceiptJson | ConvertFrom-Json -AsHashtable
        $nonzeroLastTaskReceipt.LastTaskResult = 1
        $invalidSuccessReceiptCases['nonzero lastTaskResult'] = $nonzeroLastTaskReceipt | ConvertTo-Json -Compress
        $missingLastTaskReceipt = $successReceiptJson | ConvertFrom-Json -AsHashtable
        [void] $missingLastTaskReceipt.Remove('LastTaskResult')
        $invalidSuccessReceiptCases['missing lastTaskResult'] = $missingLastTaskReceipt | ConvertTo-Json -Compress
        $missingSettingsLengthReceipt = $successReceiptJson | ConvertFrom-Json -AsHashtable
        [void] $missingSettingsLengthReceipt.Remove('SettingsLengthBefore')
        $invalidSuccessReceiptCases['missing settings length'] = $missingSettingsLengthReceipt | ConvertTo-Json -Compress
        $missingSettingsMtimeReceipt = $successReceiptJson | ConvertFrom-Json -AsHashtable
        [void] $missingSettingsMtimeReceipt.Remove('SettingsMtimeUtcBefore')
        $invalidSuccessReceiptCases['missing settings mtime'] = $missingSettingsMtimeReceipt | ConvertTo-Json -Compress
        $missingSettingsUnchangedReceipt = $successReceiptJson | ConvertFrom-Json -AsHashtable
        [void] $missingSettingsUnchangedReceipt.Remove('SettingsUnchanged')
        $invalidSuccessReceiptCases['missing settings unchanged'] = $missingSettingsUnchangedReceipt | ConvertTo-Json -Compress
        $unchangedMismatchReceipt = $successReceiptJson | ConvertFrom-Json -AsHashtable
        $unchangedMismatchReceipt.SettingsUnchanged = $true
        $invalidSuccessReceiptCases['settings unchanged mismatch'] = $unchangedMismatchReceipt | ConvertTo-Json -Compress
        $unchangedFalseOnIdenticalReceipt = $successReceiptJson | ConvertFrom-Json -AsHashtable
        $unchangedFalseOnIdenticalReceipt.SettingsHashAfter = $unchangedFalseOnIdenticalReceipt.SettingsHashBefore
        $unchangedFalseOnIdenticalReceipt.SettingsLengthAfter = $unchangedFalseOnIdenticalReceipt.SettingsLengthBefore
        $unchangedFalseOnIdenticalReceipt.SettingsMtimeUtcAfter = $unchangedFalseOnIdenticalReceipt.SettingsMtimeUtcBefore
        $unchangedFalseOnIdenticalReceipt.SettingsUnchanged = $false
        $invalidSuccessReceiptCases['settings unchanged false on identical fingerprint'] = $unchangedFalseOnIdenticalReceipt | ConvertTo-Json -Compress
        foreach ($case in $invalidSuccessReceiptCases.GetEnumerator()) {
            $rejected = $false
            try {
                ConvertFrom-CountdownRemoteDeploymentSuccessReceiptJson -Json ([string] $case.Value) | Out-Null
            }
            catch {
                $rejected = $true
            }
            Assert-Equal $true $rejected "Success receipt parser rejects $($case.Key)"
        }

        $successReceiptEvidencePath = Join-Path $remoteDeploymentTestRoot 'remote-deployment-success.json'
        $persistedSuccessReceipt = Write-CountdownRemoteDeploymentSuccessReceipt `
            -Path $successReceiptEvidencePath `
            -Json $successReceiptJson
        $persistedSuccessBytes = [System.IO.File]::ReadAllBytes($successReceiptEvidencePath)
        $persistedSuccessText = [System.Text.UTF8Encoding]::new($false, $true).GetString($persistedSuccessBytes)
        Assert-Equal $true (Test-Path -LiteralPath $successReceiptEvidencePath -PathType Leaf) 'Success receipt writer atomically publishes one evidence file'
        Assert-Equal $false ($persistedSuccessBytes.Length -ge 3 -and $persistedSuccessBytes[0] -eq 0xEF -and $persistedSuccessBytes[1] -eq 0xBB -and $persistedSuccessBytes[2] -eq 0xBF) 'Success receipt evidence is UTF-8 without BOM'
        Assert-Equal $false ($persistedSuccessText -match 'Credential|LaunchToken|sensitive-launch-token-sentinel') 'Success receipt evidence excludes credentials and raw launch tokens'
        Assert-Equal 20 @($persistedSuccessReceipt.PSObject.Properties).Count 'Success receipt writer returns the normalized exact schema'
        [void] (ConvertFrom-CountdownRemoteDeploymentSuccessReceiptJson -Json $persistedSuccessText)
        $persistedSuccessHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $successReceiptEvidencePath).Hash

        $existingEvidenceRejected = $false
        try {
            Write-CountdownRemoteDeploymentSuccessReceipt -Path $successReceiptEvidencePath -Json $successReceiptJson | Out-Null
        }
        catch {
            $existingEvidenceRejected = $_.Exception.Message -match '部署已提交但本地证据不完整.*未回滚'
        }
        Assert-Equal $true $existingEvidenceRejected 'Success receipt persistence failure reports committed deployment with incomplete local evidence and no rollback claim'
        Assert-Equal $persistedSuccessHash (Get-FileHash -Algorithm SHA256 -LiteralPath $successReceiptEvidencePath).Hash 'Failed success receipt publication preserves the existing evidence bytes'
        Assert-Equal 0 @(Get-ChildItem -LiteralPath $remoteDeploymentTestRoot -File -Filter 'remote-deployment-success.json.*.tmp').Count 'Success receipt writer leaves no temporary file after publication failure'

        $invalidEvidencePath = Join-Path $remoteDeploymentTestRoot 'invalid-success.json'
        $invalidEvidenceRejected = $false
        try {
            Write-CountdownRemoteDeploymentSuccessReceipt `
                -Path $invalidEvidencePath `
                -Json $invalidSuccessReceiptCases['truncated JSON'] | Out-Null
        }
        catch {
            $invalidEvidenceRejected = $_.Exception.Message -match '部署已提交但本地证据不完整.*未回滚'
        }
        Assert-Equal $true $invalidEvidenceRejected 'Invalid remote success output reports committed deployment with incomplete local evidence'
        Assert-Equal $false (Test-Path -LiteralPath $invalidEvidencePath) 'Invalid success output never creates local success evidence'

        $expectedReadbackExecutableHash = 'C' * 64
        $expectedReadbackSettingsHash = 'D' * 64
        $validReadback = [ordered]@{
            SchemaVersion = 1
            Valid = $true
            HostIdentityMatch = $true
            UserIdentityMatch = $true
            RemoteMachineNameSha256 = 'A' * 64
            RemoteUserNameSha256 = 'B' * 64
            TaskState = 'Ready'
            LastTaskResult = 0
            TaskExecuteMatch = $true
            TaskArgumentsMatch = $true
            TaskWorkingDirectoryMatch = $true
            StartupSchemaVersion = 1
            StartupAcceptanceState = 'finalized'
            RuntimeSchemaVersion = 4
            RuntimeStage = 'window-ready'
            AcceptanceSchemaVersion = 1
            AcceptanceStage = 'finalized-ready'
            TokenSha256 = 'E' * 64
            TokenMatch = $true
            RuntimeProcessId = 4242
            AcceptanceProcessId = 4242
            ProcessIdMatch = $true
            RuntimeSessionId = 7
            AcceptanceSessionId = 7
            ProcessSessionId = 7
            SessionIdMatch = $true
            ApplicationPathMatch = $true
            ProcessExecutablePathMatch = $true
            ExecutableSha256 = $expectedReadbackExecutableHash
            ExecutableHashMatch = $true
            SettingsSha256 = $expectedReadbackSettingsHash
            SettingsHashMatch = $true
            SettingsLength = 2
            SettingsMtimeUtc = '2026-01-02T00:00:00.0000000Z'
            SettingsUnchanged = $false
        }
        $validReadbackJson = $validReadback | ConvertTo-Json -Compress
        $normalizedReadback = ConvertFrom-CountdownRemoteDeploymentReadbackJson `
            -Json $validReadbackJson `
            -ExpectedProcessId 4242 `
            -ExpectedExecutableSha256 $expectedReadbackExecutableHash `
            -ExpectedTokenSha256 ('E' * 64) `
            -ExpectedSettingsSha256 $expectedReadbackSettingsHash
        Assert-Equal 35 @($normalizedReadback.PSObject.Properties).Count 'Independent readback parser returns the exact schema field set'
        Assert-Equal $true ([bool] $normalizedReadback.Valid) 'Independent readback parser accepts a fully cross-bound receipt'
        Assert-Equal $false ($normalizedReadback.PSObject.Properties.Name -contains 'LaunchToken') 'Independent readback schema excludes the raw launch token'

        $invalidReadbackCases = [ordered]@{}
        $readbackMissingField = $validReadbackJson | ConvertFrom-Json -AsHashtable
        [void] $readbackMissingField.Remove('TokenMatch')
        $invalidReadbackCases['missing field'] = $readbackMissingField | ConvertTo-Json -Compress
        $readbackExtraField = $validReadbackJson | ConvertFrom-Json -AsHashtable
        $readbackExtraField.LaunchToken = 'sensitive-launch-token-sentinel'
        $invalidReadbackCases['raw token field'] = $readbackExtraField | ConvertTo-Json -Compress
        $invalidReadbackCases['duplicate field'] = $validReadbackJson.Insert(1, '"Valid":true,')
        $readbackWrongSchema = $validReadbackJson | ConvertFrom-Json -AsHashtable
        $readbackWrongSchema.SchemaVersion = 2
        $invalidReadbackCases['output schema'] = $readbackWrongSchema | ConvertTo-Json -Compress
        foreach ($field in @('HostIdentityMatch', 'UserIdentityMatch')) {
            $caseValue = $validReadbackJson | ConvertFrom-Json -AsHashtable
            $caseValue[$field] = $false
            $invalidReadbackCases["identity $field"] = $caseValue | ConvertTo-Json -Compress
        }
        $readbackTaskState = $validReadbackJson | ConvertFrom-Json -AsHashtable
        $readbackTaskState.TaskState = 'Running'
        $invalidReadbackCases['task state'] = $readbackTaskState | ConvertTo-Json -Compress
        $readbackLastTaskResult = $validReadbackJson | ConvertFrom-Json -AsHashtable
        $readbackLastTaskResult.LastTaskResult = 1
        $invalidReadbackCases['nonzero lastTaskResult'] = $readbackLastTaskResult | ConvertTo-Json -Compress
        $readbackMissingLastTaskResult = $validReadbackJson | ConvertFrom-Json -AsHashtable
        [void] $readbackMissingLastTaskResult.Remove('LastTaskResult')
        $invalidReadbackCases['missing lastTaskResult'] = $readbackMissingLastTaskResult | ConvertTo-Json -Compress
        $readbackMissingSettingsLength = $validReadbackJson | ConvertFrom-Json -AsHashtable
        [void] $readbackMissingSettingsLength.Remove('SettingsLength')
        $invalidReadbackCases['missing settings length'] = $readbackMissingSettingsLength | ConvertTo-Json -Compress
        $readbackMissingSettingsMtime = $validReadbackJson | ConvertFrom-Json -AsHashtable
        [void] $readbackMissingSettingsMtime.Remove('SettingsMtimeUtc')
        $invalidReadbackCases['missing settings mtime'] = $readbackMissingSettingsMtime | ConvertTo-Json -Compress
        $readbackMissingSettingsUnchanged = $validReadbackJson | ConvertFrom-Json -AsHashtable
        [void] $readbackMissingSettingsUnchanged.Remove('SettingsUnchanged')
        $invalidReadbackCases['missing settings unchanged'] = $readbackMissingSettingsUnchanged | ConvertTo-Json -Compress
        foreach ($field in @('TaskExecuteMatch', 'TaskArgumentsMatch', 'TaskWorkingDirectoryMatch')) {
            $caseValue = $validReadbackJson | ConvertFrom-Json -AsHashtable
            $caseValue[$field] = $false
            $invalidReadbackCases["task action $field"] = $caseValue | ConvertTo-Json -Compress
        }
        foreach ($definition in @(
                @('StartupAcceptanceState', 'ready'),
                @('RuntimeStage', 'starting'),
                @('AcceptanceStage', 'accepted'))) {
            $caseValue = $validReadbackJson | ConvertFrom-Json -AsHashtable
            $caseValue[[string] $definition[0]] = [string] $definition[1]
            $invalidReadbackCases["receipt $($definition[0])"] = $caseValue | ConvertTo-Json -Compress
        }
        $readbackTokenMismatch = $validReadbackJson | ConvertFrom-Json -AsHashtable
        $readbackTokenMismatch.TokenMatch = $false
        $invalidReadbackCases['token mismatch'] = $readbackTokenMismatch | ConvertTo-Json -Compress
        $readbackPidMismatch = $validReadbackJson | ConvertFrom-Json -AsHashtable
        $readbackPidMismatch.RuntimeProcessId = 4243
        $invalidReadbackCases['success receipt PID mismatch'] = $readbackPidMismatch | ConvertTo-Json -Compress
        $readbackProcessMismatch = $validReadbackJson | ConvertFrom-Json -AsHashtable
        $readbackProcessMismatch.ProcessIdMatch = $false
        $invalidReadbackCases['process identity mismatch'] = $readbackProcessMismatch | ConvertTo-Json -Compress
        $readbackSessionMismatch = $validReadbackJson | ConvertFrom-Json -AsHashtable
        $readbackSessionMismatch.AcceptanceSessionId = 8
        $invalidReadbackCases['receipt session mismatch'] = $readbackSessionMismatch | ConvertTo-Json -Compress
        $readbackSessionIdentityMismatch = $validReadbackJson | ConvertFrom-Json -AsHashtable
        $readbackSessionIdentityMismatch.SessionIdMatch = $false
        $invalidReadbackCases['process session mismatch'] = $readbackSessionIdentityMismatch | ConvertTo-Json -Compress
        foreach ($field in @('ApplicationPathMatch', 'ProcessExecutablePathMatch')) {
            $caseValue = $validReadbackJson | ConvertFrom-Json -AsHashtable
            $caseValue[$field] = $false
            $invalidReadbackCases["path $field"] = $caseValue | ConvertTo-Json -Compress
        }
        $readbackExecutableHash = $validReadbackJson | ConvertFrom-Json -AsHashtable
        $readbackExecutableHash.ExecutableSha256 = 'F' * 64
        $invalidReadbackCases['executable release hash'] = $readbackExecutableHash | ConvertTo-Json -Compress
        $readbackExecutableMatch = $validReadbackJson | ConvertFrom-Json -AsHashtable
        $readbackExecutableMatch.ExecutableHashMatch = $false
        $invalidReadbackCases['executable hash match'] = $readbackExecutableMatch | ConvertTo-Json -Compress
        $readbackSettingsHash = $validReadbackJson | ConvertFrom-Json -AsHashtable
        $readbackSettingsHash.SettingsSha256 = 'F' * 64
        $invalidReadbackCases['settings success receipt hash'] = $readbackSettingsHash | ConvertTo-Json -Compress
        $readbackSettingsMatch = $validReadbackJson | ConvertFrom-Json -AsHashtable
        $readbackSettingsMatch.SettingsHashMatch = $false
        $invalidReadbackCases['settings hash match'] = $readbackSettingsMatch | ConvertTo-Json -Compress
        foreach ($case in $invalidReadbackCases.GetEnumerator()) {
            $rejected = $false
            try {
                ConvertFrom-CountdownRemoteDeploymentReadbackJson `
                    -Json ([string] $case.Value) `
                    -ExpectedProcessId 4242 `
                    -ExpectedExecutableSha256 $expectedReadbackExecutableHash `
                    -ExpectedTokenSha256 ('E' * 64) `
                    -ExpectedSettingsSha256 $expectedReadbackSettingsHash | Out-Null
            }
            catch {
                $rejected = $true
            }
            Assert-Equal $true $rejected "Independent readback parser rejects $($case.Key)"
        }

        $readbackEvidencePath = Join-Path $remoteDeploymentTestRoot 'post-deployment-independent-readback.json'
        $persistedReadback = Write-CountdownRemoteDeploymentReadbackReceipt `
            -Path $readbackEvidencePath `
            -Bytes ([System.Text.UTF8Encoding]::new($false).GetBytes($validReadbackJson)) `
            -ExpectedProcessId 4242 `
            -ExpectedExecutableSha256 $expectedReadbackExecutableHash `
            -ExpectedTokenSha256 ('E' * 64) `
            -ExpectedSettingsSha256 $expectedReadbackSettingsHash
        $persistedReadbackBytes = [System.IO.File]::ReadAllBytes($readbackEvidencePath)
        $persistedReadbackText = [System.Text.UTF8Encoding]::new($false, $true).GetString($persistedReadbackBytes)
        Assert-Equal $true (Test-Path -LiteralPath $readbackEvidencePath -PathType Leaf) 'Independent readback writer atomically publishes one evidence file'
        Assert-Equal $false ($persistedReadbackText -match 'LaunchToken|sensitive-launch-token-sentinel') 'Independent readback evidence never persists the raw token'
        Assert-Equal 35 @($persistedReadback.PSObject.Properties).Count 'Independent readback writer returns the normalized exact schema'
        $readbackEvidenceHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $readbackEvidencePath).Hash
        $existingReadbackRejected = $false
        try {
            Write-CountdownRemoteDeploymentReadbackReceipt `
                -Path $readbackEvidencePath `
                -Bytes ([System.Text.UTF8Encoding]::new($false).GetBytes($validReadbackJson)) `
                -ExpectedProcessId 4242 `
                -ExpectedExecutableSha256 $expectedReadbackExecutableHash `
                -ExpectedTokenSha256 ('E' * 64) `
                -ExpectedSettingsSha256 $expectedReadbackSettingsHash | Out-Null
        }
        catch {
            $existingReadbackRejected = $_.Exception.Message -match '部署已提交但独立验证不完整.*未回滚'
        }
        Assert-Equal $true $existingReadbackRejected 'Independent readback publication failure reports committed deployment without a rollback claim'
        Assert-Equal $readbackEvidenceHash (Get-FileHash -Algorithm SHA256 -LiteralPath $readbackEvidencePath).Hash 'Failed readback publication preserves existing evidence bytes'
        Assert-Equal 0 @(Get-ChildItem -LiteralPath $remoteDeploymentTestRoot -File -Filter 'post-deployment-independent-readback.json.*.tmp').Count 'Independent readback writer leaves no temporary file after publication failure'

        $beginFailureFixture = New-CountdownRemoteDeploymentFixtureAdapter -Mode BeginFailure
        $beginFailureReceiptPath = Join-Path $remoteDeploymentTestRoot 'deployment-failure.json'
        $beginFailureMessage = $null
        try {
            Invoke-CountdownRemoteDeployment `
                -SourceRoot $PSScriptRoot `
                -InstallPath $remoteDeploymentInstall `
                -PowerShell7Path $remoteDeploymentPowerShell `
                -DeferredReceiptPath $remoteDeploymentReceipt `
                -TaskName 'Minmin Countdown Fixture' `
                -LegacyTaskName '敏敏子倒计时 Fixture' `
                -Adapter $beginFailureFixture.Adapter | Out-Null
        }
        catch {
            $beginFailureMessage = $_.Exception.Message
        }
        Assert-Equal $true ($beginFailureMessage -match '远端部署失败，已回滚' -and $beginFailureMessage -match '阶段=开始候选安装' -and $beginFailureMessage -match '异常类型=System.IO.IOException' -and $beginFailureMessage -match '错误位置=' -and $beginFailureMessage -match 'ScriptStackTrace=' -and $beginFailureMessage -match '模拟候选安装异常') 'Remote deployment preserves stage, exception type, position and script stack for a pre-commit failure'
        Assert-Equal $true ([bool] $beginFailureFixture.State.RolledBack) 'Begin failure still rolls back the candidate'
        Assert-Equal $true ([bool] $beginFailureFixture.State.Disposed) 'Begin failure still releases its host adapter'
        Assert-Equal $true ([bool] $beginFailureFixture.State.DiagnosticsPresentBeforeRollback) 'Remote deployment captures candidate diagnostics before rollback begins'
        $preRollbackDiagnosticsPath = Join-Path $remoteDeploymentTestRoot 'pre-rollback-diagnostics.json'
        Assert-Equal $true (Test-Path -LiteralPath $preRollbackDiagnosticsPath -PathType Leaf) 'Pre-rollback diagnostics use the fixed sidecar name beside the deferred receipt'
        if (Test-Path -LiteralPath $preRollbackDiagnosticsPath -PathType Leaf) {
            $preRollbackDiagnosticsText = [System.IO.File]::ReadAllText($preRollbackDiagnosticsPath, [System.Text.Encoding]::UTF8)
            $preRollbackDiagnosticsDocument = $preRollbackDiagnosticsText | ConvertFrom-Json
            Assert-Equal 1 ([int] $preRollbackDiagnosticsDocument.schemaVersion) 'Pre-rollback diagnostics use schema v1'
            Assert-Equal $true ($preRollbackDiagnosticsText -notmatch 'sensitive-launch-token-sentinel|sensitive-task-argument-sentinel|sensitive-settings-sentinel') 'Pre-rollback diagnostics omit tokens, task XML and settings contents'
        }
        $preRollbackParserCommand = Get-Command ConvertFrom-CountdownPreRollbackDiagnosticsBytes -Module Countdown.RemoteDeployment -ErrorAction SilentlyContinue
        Assert-Equal $true ($null -ne $preRollbackParserCommand) 'Remote deployment exports the strict pre-rollback diagnostics parser'
        if ($null -ne $preRollbackParserCommand -and (Test-Path -LiteralPath $preRollbackDiagnosticsPath -PathType Leaf)) {
            $preRollbackDiagnosticsBytes = [System.IO.File]::ReadAllBytes($preRollbackDiagnosticsPath)
            $validatedPreRollbackDiagnostics = ConvertFrom-CountdownPreRollbackDiagnosticsBytes -Bytes $preRollbackDiagnosticsBytes
            Assert-Equal 1 ([int] $validatedPreRollbackDiagnostics.schemaVersion) 'Pre-rollback diagnostics parser accepts writer output'

            $unexpectedPreRollbackDocument = $preRollbackDiagnosticsText | ConvertFrom-Json -AsHashtable
            $unexpectedPreRollbackDocument['unexpected'] = 'reject-me'
            $unexpectedPreRollbackRejected = $false
            try {
                ConvertFrom-CountdownPreRollbackDiagnosticsBytes -Bytes ([System.Text.UTF8Encoding]::new($false).GetBytes(($unexpectedPreRollbackDocument | ConvertTo-Json -Depth 12 -Compress))) | Out-Null
            }
            catch {
                $unexpectedPreRollbackRejected = $_.Exception.Message -match '字段数量无效|未允许字段'
            }
            Assert-Equal $true $unexpectedPreRollbackRejected 'Pre-rollback diagnostics parser rejects additional fields'

            $nestedUnexpectedPreRollbackDocument = $preRollbackDiagnosticsText | ConvertFrom-Json -AsHashtable
            $nestedUnexpectedPreRollbackDocument.receipts.runtime.core['unexpected'] = 'reject-me'
            $nestedUnexpectedPreRollbackRejected = $false
            try {
                ConvertFrom-CountdownPreRollbackDiagnosticsBytes -Bytes ([System.Text.UTF8Encoding]::new($false).GetBytes(($nestedUnexpectedPreRollbackDocument | ConvertTo-Json -Depth 12 -Compress))) | Out-Null
            }
            catch { $nestedUnexpectedPreRollbackRejected = $_.Exception.Message -match '字段数量无效|未允许字段' }
            Assert-Equal $true $nestedUnexpectedPreRollbackRejected 'Pre-rollback diagnostics parser rejects nested additional fields'

            $invalidTypePreRollbackDocument = $preRollbackDiagnosticsText | ConvertFrom-Json -AsHashtable
            $invalidTypePreRollbackDocument.receipts.runtime.core.processId = '4242'
            $invalidTypePreRollbackRejected = $false
            try {
                ConvertFrom-CountdownPreRollbackDiagnosticsBytes -Bytes ([System.Text.UTF8Encoding]::new($false).GetBytes(($invalidTypePreRollbackDocument | ConvertTo-Json -Depth 12 -Compress))) | Out-Null
            }
            catch { $invalidTypePreRollbackRejected = $_.Exception.Message -match 'processId.*类型无效' }
            Assert-Equal $true $invalidTypePreRollbackRejected 'Pre-rollback diagnostics parser rejects invalid nested field types'

            $invalidTimestampPreRollbackDocument = $preRollbackDiagnosticsText | ConvertFrom-Json -AsHashtable
            $invalidTimestampPreRollbackDocument.captureAtUtc = 'not-a-time'
            $invalidTimestampPreRollbackRejected = $false
            try {
                ConvertFrom-CountdownPreRollbackDiagnosticsBytes -Bytes ([System.Text.UTF8Encoding]::new($false).GetBytes(($invalidTimestampPreRollbackDocument | ConvertTo-Json -Depth 12 -Compress))) | Out-Null
            }
            catch { $invalidTimestampPreRollbackRejected = $_.Exception.Message -match 'captureAtUtc.*时间戳无效' }
            Assert-Equal $true $invalidTimestampPreRollbackRejected 'Pre-rollback diagnostics parser rejects invalid timestamps'

            $missingIntegrityPreRollbackDocument = $preRollbackDiagnosticsText | ConvertFrom-Json -AsHashtable
            $missingIntegrityPreRollbackDocument.receipts.runtime.status = 'readable'
            $missingIntegrityPreRollbackDocument.receipts.runtime.length = $null
            $missingIntegrityPreRollbackDocument.receipts.runtime.lastWriteTimeUtc = $null
            $missingIntegrityPreRollbackDocument.receipts.runtime.sha256 = $null
            $missingIntegrityPreRollbackRejected = $false
            try {
                ConvertFrom-CountdownPreRollbackDiagnosticsBytes -Bytes ([System.Text.UTF8Encoding]::new($false).GetBytes(($missingIntegrityPreRollbackDocument | ConvertTo-Json -Depth 12 -Compress))) | Out-Null
            }
            catch { $missingIntegrityPreRollbackRejected = $_.Exception.Message -match 'readable 状态缺少完整性元数据' }
            Assert-Equal $true $missingIntegrityPreRollbackRejected 'Pre-rollback diagnostics parser rejects readable files without integrity metadata'

            $duplicateLogsPreRollbackDocument = $preRollbackDiagnosticsText | ConvertFrom-Json -AsHashtable
            $duplicateLogsPreRollbackDocument.logs = @(
                $duplicateLogsPreRollbackDocument.logs[0],
                $duplicateLogsPreRollbackDocument.logs[0],
                $duplicateLogsPreRollbackDocument.logs[0],
                $duplicateLogsPreRollbackDocument.logs[0])
            $duplicateLogsPreRollbackRejected = $false
            try {
                ConvertFrom-CountdownPreRollbackDiagnosticsBytes -Bytes ([System.Text.UTF8Encoding]::new($false).GetBytes(($duplicateLogsPreRollbackDocument | ConvertTo-Json -Depth 12 -Compress))) | Out-Null
            }
            catch { $duplicateLogsPreRollbackRejected = $_.Exception.Message -match '重复名称|固定日志名称' }
            Assert-Equal $true $duplicateLogsPreRollbackRejected 'Pre-rollback diagnostics parser rejects duplicate log names'

            $invalidTempStatusPreRollbackDocument = $preRollbackDiagnosticsText | ConvertFrom-Json -AsHashtable
            $invalidTempStatusPreRollbackDocument.acceptanceTempFiles.files = @([ordered]@{
                    name = "acceptance-status.json.$('a' * 32).tmp"
                    status = 'unexpected'
                    length = 0
                    lastWriteTimeUtc = $invalidTempStatusPreRollbackDocument.captureAtUtc
                    sha256 = '0' * 64
                    error = $null
                })
            $invalidTempStatusPreRollbackRejected = $false
            try {
                ConvertFrom-CountdownPreRollbackDiagnosticsBytes -Bytes ([System.Text.UTF8Encoding]::new($false).GetBytes(($invalidTempStatusPreRollbackDocument | ConvertTo-Json -Depth 12 -Compress))) | Out-Null
            }
            catch { $invalidTempStatusPreRollbackRejected = $_.Exception.Message -match 'acceptanceTempFiles.*status 无效' }
            Assert-Equal $true $invalidTempStatusPreRollbackRejected 'Pre-rollback diagnostics parser rejects invalid acceptance temporary file status'

            $invalidTempNamePreRollbackDocument = $preRollbackDiagnosticsText | ConvertFrom-Json -AsHashtable
            $invalidTempNamePreRollbackDocument.acceptanceTempFiles.files = @([ordered]@{
                    name = 'acceptance-status.json.sensitive-launch-token-sentinel.tmp'
                    status = 'readable'
                    length = 0
                    lastWriteTimeUtc = $invalidTempNamePreRollbackDocument.captureAtUtc
                    sha256 = '0' * 64
                    error = $null
                })
            $invalidTempNamePreRollbackRejected = $false
            try {
                ConvertFrom-CountdownPreRollbackDiagnosticsBytes -Bytes ([System.Text.UTF8Encoding]::new($false).GetBytes(($invalidTempNamePreRollbackDocument | ConvertTo-Json -Depth 12 -Compress))) | Out-Null
            }
            catch { $invalidTempNamePreRollbackRejected = $_.Exception.Message -match 'acceptanceTempFiles.*name 无效' }
            Assert-Equal $true $invalidTempNamePreRollbackRejected 'Pre-rollback diagnostics parser rejects unsafe acceptance temporary file names'

            $invalidTempErrorPreRollbackDocument = $preRollbackDiagnosticsText | ConvertFrom-Json -AsHashtable
            $invalidTempErrorPreRollbackDocument.acceptanceTempFiles.error = 42
            $invalidTempErrorPreRollbackRejected = $false
            try {
                ConvertFrom-CountdownPreRollbackDiagnosticsBytes -Bytes ([System.Text.UTF8Encoding]::new($false).GetBytes(($invalidTempErrorPreRollbackDocument | ConvertTo-Json -Depth 12 -Compress))) | Out-Null
            }
            catch { $invalidTempErrorPreRollbackRejected = $_.Exception.Message -match 'acceptanceTempFiles\.error 类型无效' }
            Assert-Equal $true $invalidTempErrorPreRollbackRejected 'Pre-rollback diagnostics parser rejects invalid acceptance temporary file error types'

            $missingTempErrorPreRollbackDocument = $preRollbackDiagnosticsText | ConvertFrom-Json -AsHashtable
            $missingTempErrorPreRollbackDocument.acceptanceTempFiles.status = 'io-error'
            $missingTempErrorPreRollbackDocument.acceptanceTempFiles.error = $null
            $missingTempErrorPreRollbackRejected = $false
            try {
                ConvertFrom-CountdownPreRollbackDiagnosticsBytes -Bytes ([System.Text.UTF8Encoding]::new($false).GetBytes(($missingTempErrorPreRollbackDocument | ConvertTo-Json -Depth 12 -Compress))) | Out-Null
            }
            catch { $missingTempErrorPreRollbackRejected = $_.Exception.Message -match 'acceptanceTempFiles\.io-error 缺少错误原因' }
            Assert-Equal $true $missingTempErrorPreRollbackRejected 'Pre-rollback diagnostics parser rejects io-error without an error reason'

            $invalidUtf8PreRollbackRejected = $false
            try { ConvertFrom-CountdownPreRollbackDiagnosticsBytes -Bytes ([byte[]]@(0x7B, 0x22, 0xFF, 0x22, 0x7D)) | Out-Null }
            catch { $invalidUtf8PreRollbackRejected = $_.Exception.Message -match 'UTF-8' }
            Assert-Equal $true $invalidUtf8PreRollbackRejected 'Pre-rollback diagnostics parser rejects invalid UTF-8'

            $bomPreRollbackRejected = $false
            try { ConvertFrom-CountdownPreRollbackDiagnosticsBytes -Bytes ([byte[]]@(0xEF, 0xBB, 0xBF, 0x7B, 0x7D)) | Out-Null }
            catch { $bomPreRollbackRejected = $_.Exception.Message -match 'BOM' }
            Assert-Equal $true $bomPreRollbackRejected 'Pre-rollback diagnostics parser rejects a UTF-8 BOM'

            $oversizedPreRollbackRejected = $false
            try { ConvertFrom-CountdownPreRollbackDiagnosticsBytes -Bytes ([byte[]]::new(24577)) | Out-Null }
            catch { $oversizedPreRollbackRejected = $_.Exception.Message -match '超过允许大小' }
            Assert-Equal $true $oversizedPreRollbackRejected 'Pre-rollback diagnostics parser rejects payloads above 24 KiB'
        }
        Assert-Equal $true (Test-Path -LiteralPath $beginFailureReceiptPath -PathType Leaf) 'Remote deployment writes a controller fallback receipt after a pre-commit failure'
        $beginFailureReceipt = [System.IO.File]::ReadAllText($beginFailureReceiptPath, [System.Text.Encoding]::UTF8) | ConvertFrom-Json
        Assert-Equal '开始候选安装' ([string] $beginFailureReceipt.stage) 'Controller fallback receipt retains the active deployment stage'
        Assert-Equal '模拟候选安装异常。' ([string] $beginFailureReceipt.exceptionMessage) 'Controller fallback receipt retains the original exception message'
        Assert-Equal $true (-not [bool] $beginFailureReceipt.transactionStarted -and [string] $beginFailureReceipt.commitState -eq 'not-committed' -and [bool] $beginFailureReceipt.rollbackAttempted -and [bool] $beginFailureReceipt.rollbackSucceeded) 'Controller fallback receipt retains the uncommitted rollback outcome'

        $blockedDiagnosticsParent = Join-Path $remoteDeploymentTestRoot 'blocked-diagnostics-parent'
        [System.IO.File]::WriteAllText($blockedDiagnosticsParent, 'not-a-directory', [System.Text.UTF8Encoding]::new($false))
        $captureFailureFixture = New-CountdownRemoteDeploymentFixtureAdapter -Mode BeginFailure
        $captureFailureMessage = $null
        try {
            Invoke-CountdownRemoteDeployment `
                -SourceRoot $PSScriptRoot `
                -InstallPath $remoteDeploymentInstall `
                -PowerShell7Path $remoteDeploymentPowerShell `
                -DeferredReceiptPath (Join-Path $blockedDiagnosticsParent 'deferred-install-receipt.json') `
                -TaskName 'Minmin Countdown Fixture' `
                -LegacyTaskName '敏敏子倒计时 Fixture' `
                -Adapter $captureFailureFixture.Adapter | Out-Null
        }
        catch {
            $captureFailureMessage = $_.Exception.Message
        }
        Assert-Equal 1 ([int] $captureFailureFixture.State.RollbackCount) 'Pre-rollback diagnostics capture failure still invokes rollback exactly once'
        Assert-Equal $true ($captureFailureMessage -match '模拟候选安装异常.*rollback 前诊断捕获失败') 'Pre-rollback diagnostics capture failure preserves the deployment failure and adds a capture summary'

        $commitProbeReceipt = Join-Path $remoteDeploymentTestRoot 'commit-probe\deferred-install-receipt.json'
        New-Item -ItemType Directory -Path (Split-Path -Parent $commitProbeReceipt) -Force | Out-Null
        $commitProbeFailureFixture = New-CountdownRemoteDeploymentFixtureAdapter -Mode CommitProbeFailure
        $commitProbeFailureMessage = $null
        try {
            Invoke-CountdownRemoteDeployment `
                -SourceRoot $PSScriptRoot `
                -InstallPath $remoteDeploymentInstall `
                -PowerShell7Path $remoteDeploymentPowerShell `
                -DeferredReceiptPath $commitProbeReceipt `
                -TaskName 'Minmin Countdown Fixture' `
                -LegacyTaskName '敏敏子倒计时 Fixture' `
                -Adapter $commitProbeFailureFixture.Adapter | Out-Null
        }
        catch {
            $commitProbeFailureMessage = $_.Exception.Message
        }
        $commitProbeFailureReceiptPath = Join-Path (Split-Path -Parent $commitProbeReceipt) 'deployment-failure.json'
        Assert-Equal $true ($commitProbeFailureMessage -match '提交状态无法验证.*未回滚') 'Remote deployment fails closed when commit state cannot be probed'
        Assert-Equal 0 ([int] $commitProbeFailureFixture.State.RollbackCount) 'Commit state probe failure does not speculate with a rollback'
        Assert-Equal $true (Test-Path -LiteralPath $commitProbeFailureReceiptPath -PathType Leaf) 'Commit state probe failure writes an auditable receipt'
        $commitProbeFailureReceipt = [System.IO.File]::ReadAllText($commitProbeFailureReceiptPath, [System.Text.Encoding]::UTF8) | ConvertFrom-Json
        Assert-Equal $true ([string] $commitProbeFailureReceipt.commitState -eq 'unknown' -and -not [bool] $commitProbeFailureReceipt.rollbackAttempted -and -not [bool] $commitProbeFailureReceipt.rollbackSucceeded) 'Commit state probe receipt records the unknown boundary without a rollback claim'

        $unresolvedReceipt = Join-Path $remoteDeploymentTestRoot 'unresolved-committed\deferred-install-receipt.json'
        New-Item -ItemType Directory -Path (Split-Path -Parent $unresolvedReceipt) -Force | Out-Null
        $unresolvedCommittedFixture = New-CountdownRemoteDeploymentFixtureAdapter -Mode UnresolvedCommittedTransaction
        $unresolvedCommittedMessage = $null
        try {
            Invoke-CountdownRemoteDeployment `
                -SourceRoot $PSScriptRoot `
                -InstallPath $remoteDeploymentInstall `
                -PowerShell7Path $remoteDeploymentPowerShell `
                -DeferredReceiptPath $unresolvedReceipt `
                -TaskName 'Minmin Countdown Fixture' `
                -LegacyTaskName '敏敏子倒计时 Fixture' `
                -Adapter $unresolvedCommittedFixture.Adapter | Out-Null
        }
        catch {
            $unresolvedCommittedMessage = $_.Exception.Message
        }
        $unresolvedCommittedReceiptPath = Join-Path (Split-Path -Parent $unresolvedReceipt) 'deployment-failure.json'
        Assert-Equal $true ($unresolvedCommittedMessage -match '已提交但未完成解锁确认.*未声称回滚') 'Remote deployment does not falsely claim rollback for an unresolved committed transaction'
        Assert-Equal 1 ([int] $unresolvedCommittedFixture.State.RollbackCount) 'Unresolved committed transaction records the attempted rollback boundary'
        $unresolvedCommittedReceipt = [System.IO.File]::ReadAllText($unresolvedCommittedReceiptPath, [System.Text.Encoding]::UTF8) | ConvertFrom-Json
        Assert-Equal $true ([string] $unresolvedCommittedReceipt.commitState -eq 'committed' -and [bool] $unresolvedCommittedReceipt.rollbackAttempted -and -not [bool] $unresolvedCommittedReceipt.rollbackSucceeded) 'Unresolved committed receipt preserves the no-rollback-success fact'

        $validationFailureFixture = New-CountdownRemoteDeploymentFixtureAdapter -Mode ValidationFailure
        $validationFailureMessage = $null
        try {
            Invoke-CountdownRemoteDeployment `
                -SourceRoot $PSScriptRoot `
                -InstallPath $remoteDeploymentInstall `
                -PowerShell7Path $remoteDeploymentPowerShell `
                -DeferredReceiptPath $remoteDeploymentReceipt `
                -TaskName 'Minmin Countdown Fixture' `
                -LegacyTaskName '敏敏子倒计时 Fixture' `
                -Adapter $validationFailureFixture.Adapter | Out-Null
        }
        catch {
            $validationFailureMessage = $_.Exception.Message
        }
        Assert-Equal $true ($validationFailureMessage -match '安装器内置校验未通过') 'Remote deployment rejects a failed stable validation result'
        Assert-Equal $false ([bool] $validationFailureFixture.State.Accepted) 'Validation failure does not enter acceptance'
        Assert-Equal $true ([bool] $validationFailureFixture.State.RolledBack) 'Validation failure rolls back the candidate'
        Assert-Equal $true ([bool] $validationFailureFixture.State.Disposed) 'Validation failure releases its host adapter'

        $adapterUiValidationFailureFixture = New-CountdownRemoteDeploymentFixtureAdapter -Mode AdapterUiValidationFailure
        $adapterUiValidationFailureMessage = $null
        try {
            Invoke-CountdownRemoteDeployment `
                -SourceRoot $remoteDeploymentSource `
                -InstallPath $remoteDeploymentInstall `
                -PowerShell7Path $remoteDeploymentPowerShell `
                -DeferredReceiptPath $remoteDeploymentReceipt `
                -TaskName 'Minmin Countdown Fixture' `
                -LegacyTaskName '敏敏子倒计时 Fixture' `
                -Adapter $adapterUiValidationFailureFixture.Adapter | Out-Null
        }
        catch {
            $adapterUiValidationFailureMessage = $_.Exception.Message
        }
        Assert-Equal $true ($adapterUiValidationFailureMessage -match 'adapter UI 验证未通过') 'Remote deployment rejects missing adapter UI evidence before acceptance'
        Assert-Equal $false ([bool] $adapterUiValidationFailureFixture.State.Accepted) 'Adapter UI validation failure does not enter acceptance'
        Assert-Equal $true ([bool] $adapterUiValidationFailureFixture.State.RolledBack) 'Adapter UI validation failure rolls back the candidate'

        $validationSchemaFailureFixture = New-CountdownRemoteDeploymentFixtureAdapter -Mode ValidationSchemaFailure
        $validationSchemaFailureMessage = $null
        try {
            Invoke-CountdownRemoteDeployment `
                -SourceRoot $remoteDeploymentSource `
                -InstallPath $remoteDeploymentInstall `
                -PowerShell7Path $remoteDeploymentPowerShell `
                -DeferredReceiptPath $remoteDeploymentReceipt `
                -TaskName 'Minmin Countdown Fixture' `
                -LegacyTaskName '敏敏子倒计时 Fixture' `
                -Adapter $validationSchemaFailureFixture.Adapter | Out-Null
        }
        catch {
            $validationSchemaFailureMessage = $_.Exception.Message
        }
        Assert-Equal $true ($validationSchemaFailureMessage -match 'validation interface schemaVersion 不受支持') 'Remote deployment rejects an unknown validation interface version'
        Assert-Equal $false ([bool] $validationSchemaFailureFixture.State.Accepted) 'Validation schema failure does not enter acceptance'
        Assert-Equal $true ([bool] $validationSchemaFailureFixture.State.RolledBack) 'Validation schema failure rolls back the candidate'

        $richDiagnosticsDirectory = Join-Path $remoteDeploymentTestRoot 'rich-diagnostics'
        New-Item -ItemType Directory -Path $richDiagnosticsDirectory -Force | Out-Null
        $richDiagnosticsReceipt = Join-Path $richDiagnosticsDirectory 'deferred-install-receipt.json'
        $richDiagnosticsToken = 'rich-diagnostics-token'
        $richDiagnosticsSessionId = [System.Diagnostics.Process]::GetCurrentProcess().SessionId
        $richDiagnosticsApplicationPath = Join-Path $remoteDeploymentInstall '敏敏子倒计时.ps1'
        $richRequestedAtUtc = [DateTimeOffset]::Parse('2036-01-02T03:04:05.0000000+00:00')
        $richReleasedAtUtc = $richRequestedAtUtc.AddSeconds(1)
        $richReadyAtUtc = $richRequestedAtUtc.AddSeconds(2)
        $richFinalizedAtUtc = $richRequestedAtUtc.AddSeconds(3)
        $richDiagnosticsStatus = [ordered]@{
            schemaVersion = 4
            stage = 'window-ready'
            launchToken = $richDiagnosticsToken
            processId = $PID
            sessionId = $richDiagnosticsSessionId
            applicationPath = $richDiagnosticsApplicationPath
            readyAtUtc = $richReadyAtUtc.ToString('o')
        }
        [System.IO.File]::WriteAllText((Join-Path $remoteDeploymentInstall 'startup-request.json'), (@{
                    schemaVersion = 1
                    launchToken = $richDiagnosticsToken
                    requestedAtUtc = $richRequestedAtUtc.ToString('o')
                    releasedAtUtc = $richReleasedAtUtc.ToString('o')
                    readyAtUtc = $richReadyAtUtc.ToString('o')
                    finalizedAtUtc = $richFinalizedAtUtc.ToString('o')
                    acceptanceState = 'finalized'
                    acceptanceReadOnly = $false
                } | ConvertTo-Json -Compress), [System.Text.UTF8Encoding]::new($false))
        [System.IO.File]::WriteAllText((Join-Path $remoteDeploymentInstall 'runtime-status.json'), ($richDiagnosticsStatus | ConvertTo-Json -Compress), [System.Text.UTF8Encoding]::new($false))
        $richDiagnosticsStatus.schemaVersion = 1
        $richDiagnosticsStatus.stage = 'finalized-ready'
        $richDiagnosticsStatus.readyAtUtc = $richFinalizedAtUtc.ToString('o')
        [System.IO.File]::WriteAllText((Join-Path $remoteDeploymentInstall 'acceptance-status.json'), ($richDiagnosticsStatus | ConvertTo-Json -Compress), [System.Text.UTF8Encoding]::new($false))
        [System.IO.File]::WriteAllText((Join-Path $remoteDeploymentInstall "acceptance-status.json.$([guid]::NewGuid().ToString('N')).tmp"), '{}', [System.Text.UTF8Encoding]::new($false))
        $richDiagnosticsLogDirectory = Join-Path $remoteDeploymentInstall 'logs'
        New-Item -ItemType Directory -Path $richDiagnosticsLogDirectory -Force | Out-Null
        $richDiagnosticsLog = @(
            @{ schemaVersion = 1; timestampUtc = [DateTimeOffset]::UtcNow.AddSeconds(-1).ToString('o'); level = 'Info'; event = 'AcceptanceMigrationReady'; processId = $PID },
            @{ schemaVersion = 1; timestampUtc = [DateTimeOffset]::UtcNow.ToString('o'); level = 'Info'; event = 'UnrelatedNewestEvent'; processId = 1 }
        ) | ForEach-Object { $_ | ConvertTo-Json -Compress }
        [System.IO.File]::WriteAllText((Join-Path $richDiagnosticsLogDirectory 'application.jsonl'), ($richDiagnosticsLog -join [Environment]::NewLine), [System.Text.UTF8Encoding]::new($false))

        $acceptanceFailureFixture = New-CountdownRemoteDeploymentFixtureAdapter -Mode AcceptanceFailure
        $acceptanceFailureFixture.State.LaunchToken = $richDiagnosticsToken
        $acceptanceFailureFixture.State.ExpectedProcessId = $PID
        $acceptanceFailureFixture.State.InteractiveContext.SessionId = $richDiagnosticsSessionId
        $acceptanceFailureMessage = $null
        try {
            Invoke-CountdownRemoteDeployment `
                -SourceRoot $remoteDeploymentSource `
                -InstallPath $remoteDeploymentInstall `
                -PowerShell7Path $remoteDeploymentPowerShell `
                -DeferredReceiptPath $richDiagnosticsReceipt `
                -TaskName 'Minmin Countdown Fixture' `
                -LegacyTaskName '敏敏子倒计时 Fixture' `
                -Adapter $acceptanceFailureFixture.Adapter | Out-Null
        }
        catch {
            $acceptanceFailureMessage = $_.Exception.Message
        }
        Assert-Equal $true ($acceptanceFailureMessage -match '模拟验收失败') 'Remote deployment surfaces acceptance failure'
        Assert-Equal $true ([bool] $acceptanceFailureFixture.State.RolledBack) 'Acceptance failure rolls back the candidate'
        Assert-Equal $true ([bool] $acceptanceFailureFixture.State.Disposed) 'Acceptance failure releases its host adapter'
        $richDiagnostics = ConvertFrom-CountdownPreRollbackDiagnosticsBytes -Bytes ([System.IO.File]::ReadAllBytes((Join-Path $richDiagnosticsDirectory 'pre-rollback-diagnostics.json')))
        Assert-Equal 'readable' ([string] $richDiagnostics.receipts.runtime.status) 'Pre-rollback diagnostics records readable runtime metadata'
        Assert-Equal $true ([bool] $richDiagnostics.receipts.runtime.match.token) 'Pre-rollback diagnostics matches the runtime token without persisting it'
        Assert-Equal $true ([bool] $richDiagnostics.receipts.runtime.match.processId) 'Pre-rollback diagnostics matches the runtime PID'
        Assert-Equal $true ([bool] $richDiagnostics.receipts.runtime.match.sessionId) 'Pre-rollback diagnostics matches the runtime session'
        Assert-Equal $true ([bool] $richDiagnostics.receipts.runtime.match.applicationPath) 'Pre-rollback diagnostics matches the runtime application path'
        Assert-Equal $true ([string] $richDiagnostics.receipts.acceptance.status -eq 'readable' -and [bool] $richDiagnostics.receipts.acceptance.match.token -and [bool] $richDiagnostics.receipts.acceptance.match.processId) 'Pre-rollback diagnostics records the candidate acceptance receipt before rollback'
        Assert-Equal $richRequestedAtUtc.ToString('o') ([DateTimeOffset] $richDiagnostics.receipts.startup.core.requestedAtUtc).ToUniversalTime().ToString('o') 'Pre-rollback diagnostics records the startup request time'
        Assert-Equal $richReleasedAtUtc.ToString('o') ([DateTimeOffset] $richDiagnostics.receipts.startup.core.releasedAtUtc).ToUniversalTime().ToString('o') 'Pre-rollback diagnostics records the startup release time'
        Assert-Equal $richReadyAtUtc.ToString('o') ([DateTimeOffset] $richDiagnostics.receipts.startup.core.readyAtUtc).ToUniversalTime().ToString('o') 'Pre-rollback diagnostics records the startup ready time'
        Assert-Equal $richFinalizedAtUtc.ToString('o') ([DateTimeOffset] $richDiagnostics.receipts.startup.core.finalizedAtUtc).ToUniversalTime().ToString('o') 'Pre-rollback diagnostics records the startup finalized time'
        Assert-Equal $richReadyAtUtc.ToString('o') ([DateTimeOffset] $richDiagnostics.receipts.runtime.core.readyAtUtc).ToUniversalTime().ToString('o') 'Pre-rollback diagnostics records the window-ready receipt time'
        Assert-Equal $richFinalizedAtUtc.ToString('o') ([DateTimeOffset] $richDiagnostics.receipts.acceptance.core.readyAtUtc).ToUniversalTime().ToString('o') 'Pre-rollback diagnostics records the finalized-ready receipt time'
        Assert-Equal $true ([string] $richDiagnostics.process.status -eq 'running' -and [bool] $richDiagnostics.process.sessionMatch) 'Pre-rollback diagnostics records that the expected candidate process is still running in the expected session'
        Assert-Equal 1 @($richDiagnostics.acceptanceTempFiles.files).Count 'Pre-rollback diagnostics enumerates atomic acceptance temporary files'
        Assert-Equal $true (@($richDiagnostics.logs[0].tail.event) -contains 'AcceptanceMigrationReady') 'Pre-rollback diagnostics prioritizes the expected PID acceptance event in the bounded log tail'

        $commitThenFailFixture = New-CountdownRemoteDeploymentFixtureAdapter -Mode CommitThenFail
        $commitThenFailMessage = $null
        try {
            Invoke-CountdownRemoteDeployment `
                -SourceRoot $remoteDeploymentSource `
                -InstallPath $remoteDeploymentInstall `
                -PowerShell7Path $remoteDeploymentPowerShell `
                -DeferredReceiptPath $remoteDeploymentReceipt `
                -TaskName 'Minmin Countdown Fixture' `
                -LegacyTaskName '敏敏子倒计时 Fixture' `
                -Adapter $commitThenFailFixture.Adapter | Out-Null
        }
        catch {
            $commitThenFailMessage = $_.Exception.Message
        }
        Assert-Equal $true ($commitThenFailMessage -match '部署已提交.*未回滚.*模拟提交后验收失败') 'Remote deployment reports an acceptance failure after the explicit commit boundary without claiming rollback'
        Assert-Equal 0 ([int] $commitThenFailFixture.State.RollbackCount) 'Remote deployment never invokes rollback after the adapter crosses its explicit commit boundary'
        Assert-Equal $true ([bool] $commitThenFailFixture.State.Disposed) 'Committed acceptance failure still releases its host adapter'

        $acceptedFailureCases = @(
            [pscustomobject]@{ Mode = 'AcceptedFinalizationFailure'; Failure = '模拟 accepted 后 finalized 写入失败' },
            [pscustomobject]@{ Mode = 'AcceptedReceiptFailure'; Failure = '模拟 accepted 后 finalized-ready 回执失败' },
            [pscustomobject]@{ Mode = 'AcceptedEnvironmentFailure'; Failure = '模拟 accepted 后环境复验失败' }
        )
        foreach ($acceptedFailureCase in $acceptedFailureCases) {
            $acceptedFailureFixture = New-CountdownRemoteDeploymentWindowsFixtureAdapter -Mode $acceptedFailureCase.Mode
            $acceptedFailureMessage = $null
            try {
                Invoke-CountdownRemoteDeployment `
                    -SourceRoot $remoteDeploymentSource `
                    -InstallPath $remoteDeploymentInstall `
                    -PowerShell7Path $remoteDeploymentPowerShell `
                    -DeferredReceiptPath $remoteDeploymentReceipt `
                    -TaskName $acceptedFailureCase.Mode `
                    -LegacyTaskName '敏敏子倒计时 Fixture' `
                    -Adapter $acceptedFailureFixture.Adapter | Out-Null
            }
            catch {
                $acceptedFailureMessage = $_.Exception.Message
            }
            Assert-Equal $true (Test-Path -LiteralPath (Join-Path $acceptedFailureFixture.TransactionPath 'accepted.marker') -PathType Leaf) "Remote deployment fixture reaches accepted before $($acceptedFailureCase.Mode)"
            Assert-Equal $true ($acceptedFailureMessage -match "已回滚.*$([regex]::Escape($acceptedFailureCase.Failure))") "Remote deployment rolls back $($acceptedFailureCase.Mode)"
            Assert-Equal 1 ([int] $acceptedFailureFixture.State.RollbackCount) "Remote deployment invokes rollback exactly once for $($acceptedFailureCase.Mode)"
            Assert-Equal $false ([bool] $acceptedFailureFixture.State.Committed) "Accepted is not the committed boundary for $($acceptedFailureCase.Mode)"
        }

        $commitPreparationFailureFixture = New-CountdownRemoteDeploymentWindowsFixtureAdapter -Mode CommitPreparationFailure
        $commitPreparationFailureMessage = $null
        try {
            Invoke-CountdownRemoteDeployment `
                -SourceRoot $remoteDeploymentSource `
                -InstallPath $remoteDeploymentInstall `
                -PowerShell7Path $remoteDeploymentPowerShell `
                -DeferredReceiptPath $remoteDeploymentReceipt `
                -TaskName 'CommitPreparationFailure' `
                -LegacyTaskName '敏敏子倒计时 Fixture' `
                -Adapter $commitPreparationFailureFixture.Adapter | Out-Null
        }
        catch {
            $commitPreparationFailureMessage = $_.Exception.Message
        }
        Assert-Equal $true ($commitPreparationFailureMessage -match '已回滚.*模拟 committed 点前的清理准备失败') 'Remote deployment rolls back when committed cleanup fails before crossing the committed point'
        Assert-Equal 1 ([int] $commitPreparationFailureFixture.State.RollbackCount) 'Remote deployment invokes rollback exactly once before the committed point'
        Assert-Equal $false ([bool] $commitPreparationFailureFixture.State.Committed) 'Remote deployment keeps State.Committed false before the committed point'

        $committedCleanupFailureFixture = New-CountdownRemoteDeploymentWindowsFixtureAdapter -Mode CommittedCleanupFailure
        $committedCleanupFailureMessage = $null
        try {
            Invoke-CountdownRemoteDeployment `
                -SourceRoot $remoteDeploymentSource `
                -InstallPath $remoteDeploymentInstall `
                -PowerShell7Path $remoteDeploymentPowerShell `
                -DeferredReceiptPath $remoteDeploymentReceipt `
                -TaskName 'CommittedCleanupFailure' `
                -LegacyTaskName '敏敏子倒计时 Fixture' `
                -Adapter $committedCleanupFailureFixture.Adapter | Out-Null
        }
        catch {
            $committedCleanupFailureMessage = $_.Exception.Message
        }
        Assert-Equal $true ($committedCleanupFailureMessage -match '部署已提交.*未回滚.*模拟 committed 点后的事务清理失败') 'Remote deployment reports cleanup failure after crossing the committed point without rollback'
        Assert-Equal 0 ([int] $committedCleanupFailureFixture.State.RollbackCount) 'Remote deployment never rolls back when committed cleanup throws after the committed point'
        Assert-Equal $true ([bool] $committedCleanupFailureFixture.State.Committed) 'Remote deployment observes the committed point even when committed cleanup later throws'

        $committedFinalizeFailureFixture = New-CountdownRemoteDeploymentWindowsFixtureAdapter -Mode CommittedFinalizeFailure
        $committedFinalizeFailureMessage = $null
        try {
            Invoke-CountdownRemoteDeployment `
                -SourceRoot $remoteDeploymentSource `
                -InstallPath $remoteDeploymentInstall `
                -PowerShell7Path $remoteDeploymentPowerShell `
                -DeferredReceiptPath $remoteDeploymentReceipt `
                -TaskName 'CommittedFinalizeFailure' `
                -LegacyTaskName '敏敏子倒计时 Fixture' `
                -Adapter $committedFinalizeFailureFixture.Adapter | Out-Null
        }
        catch {
            $committedFinalizeFailureMessage = $_.Exception.Message
        }
        Assert-Equal $true ($committedFinalizeFailureMessage -match '部署已提交.*未回滚.*模拟 committed 后 adapter 收尾失败') 'Remote deployment reports adapter finalization failure after commit without rollback'
        Assert-Equal 0 ([int] $committedFinalizeFailureFixture.State.RollbackCount) 'Remote deployment never rolls back an adapter finalization failure after commit'
        Assert-Equal $true ([bool] $committedFinalizeFailureFixture.State.Committed) 'Remote deployment keeps committed state through adapter finalization failure'

        $rollbackFailureFixture = New-CountdownRemoteDeploymentFixtureAdapter -Mode RollbackFailure
        $rollbackFailureMessage = $null
        try {
            Invoke-CountdownRemoteDeployment `
                -SourceRoot $remoteDeploymentSource `
                -InstallPath $remoteDeploymentInstall `
                -PowerShell7Path $remoteDeploymentPowerShell `
                -DeferredReceiptPath $remoteDeploymentReceipt `
                -TaskName 'Minmin Countdown Fixture' `
                -LegacyTaskName '敏敏子倒计时 Fixture' `
                -Adapter $rollbackFailureFixture.Adapter | Out-Null
        }
        catch {
            $rollbackFailureMessage = $_.Exception.Message
        }
        Assert-Equal $true ($rollbackFailureMessage -match '安装器内置校验未通过.*回滚也失败.*模拟回滚失败') 'Remote deployment preserves both primary and rollback failures'
        Assert-Equal $true ([bool] $rollbackFailureFixture.State.RolledBack) 'Rollback failure records that rollback was attempted'
        Assert-Equal $true ([bool] $rollbackFailureFixture.State.Disposed) 'Rollback failure still releases its host adapter'

        $finalizeFailureFixture = New-CountdownRemoteDeploymentFixtureAdapter -Mode FinalizeFailure
        $finalizeFailureMessage = $null
        try {
            Invoke-CountdownRemoteDeployment `
                -SourceRoot $remoteDeploymentSource `
                -InstallPath $remoteDeploymentInstall `
                -PowerShell7Path $remoteDeploymentPowerShell `
                -DeferredReceiptPath $remoteDeploymentReceipt `
                -TaskName 'Minmin Countdown Fixture' `
                -LegacyTaskName '敏敏子倒计时 Fixture' `
                -Adapter $finalizeFailureFixture.Adapter | Out-Null
        }
        catch {
            $finalizeFailureMessage = $_.Exception.Message
        }
        Assert-Equal $true ($finalizeFailureMessage -match '部署已提交.*未回滚.*模拟提交后收尾失败') 'Remote deployment does not claim rollback after committed cleanup failure'
        Assert-Equal $false ([bool] $finalizeFailureFixture.State.RolledBack) 'Committed cleanup failure does not roll back'
        Assert-Equal $true ([bool] $finalizeFailureFixture.State.Disposed) 'Committed cleanup failure still releases its host adapter'

        $validationAndDisposeFailureFixture = New-CountdownRemoteDeploymentFixtureAdapter -Mode ValidationAndDisposeFailure
        $validationAndDisposeFailureMessage = $null
        try {
            Invoke-CountdownRemoteDeployment `
                -SourceRoot $remoteDeploymentSource `
                -InstallPath $remoteDeploymentInstall `
                -PowerShell7Path $remoteDeploymentPowerShell `
                -DeferredReceiptPath $remoteDeploymentReceipt `
                -TaskName 'Minmin Countdown Fixture' `
                -LegacyTaskName '敏敏子倒计时 Fixture' `
                -Adapter $validationAndDisposeFailureFixture.Adapter | Out-Null
        }
        catch {
            $validationAndDisposeFailureMessage = $_.Exception.Message
        }
        Assert-Equal $true ($validationAndDisposeFailureMessage -match '已回滚.*安装器内置校验未通过.*资源释放也失败.*模拟资源释放失败') 'Remote deployment preserves its primary failure when adapter disposal also fails'
        Assert-Equal $true ([bool] $validationAndDisposeFailureFixture.State.RolledBack) 'Validation and disposal failure still rolls back the candidate'
        Assert-Equal $true ([bool] $validationAndDisposeFailureFixture.State.Disposed) 'Validation and disposal failure attempts adapter disposal'

        $disposeFailureReceipt = Join-Path $remoteDeploymentTestRoot 'dispose-failure\deferred-install-receipt.json'
        New-Item -ItemType Directory -Path (Split-Path -Parent $disposeFailureReceipt) -Force | Out-Null
        $disposeFailureFixture = New-CountdownRemoteDeploymentFixtureAdapter -Mode DisposeFailure
        $disposeFailureMessage = $null
        try {
            Invoke-CountdownRemoteDeployment `
                -SourceRoot $PSScriptRoot `
                -InstallPath $remoteDeploymentInstall `
                -PowerShell7Path $remoteDeploymentPowerShell `
                -DeferredReceiptPath $disposeFailureReceipt `
                -TaskName 'Minmin Countdown Fixture' `
                -LegacyTaskName '敏敏子倒计时 Fixture' `
                -Adapter $disposeFailureFixture.Adapter | Out-Null
        }
        catch {
            $disposeFailureMessage = $_.Exception.Message
        }
        Assert-Equal $true ($disposeFailureMessage -match '部署已提交.*资源释放失败.*未回滚.*模拟资源释放失败') 'Remote deployment reports committed disposal failure without claiming rollback'
        Assert-Equal $false ([bool] $disposeFailureFixture.State.RolledBack) 'Committed disposal failure does not roll back'
        Assert-Equal $true ([bool] $disposeFailureFixture.State.Disposed) 'Committed disposal failure records the disposal attempt'
        $disposeFailureReceiptPath = Join-Path (Split-Path -Parent $disposeFailureReceipt) 'deployment-failure.json'
        Assert-Equal $true (Test-Path -LiteralPath $disposeFailureReceiptPath -PathType Leaf) 'Committed disposal failure writes an auditable receipt'
        $disposeFailureReceiptDocument = [System.IO.File]::ReadAllText($disposeFailureReceiptPath, [System.Text.Encoding]::UTF8) | ConvertFrom-Json
        Assert-Equal $true ([string] $disposeFailureReceiptDocument.commitState -eq 'committed' -and -not [bool] $disposeFailureReceiptDocument.rollbackAttempted -and -not [bool] $disposeFailureReceiptDocument.rollbackSucceeded) 'Committed disposal receipt never claims a rollback'

        $knownTransactionPath = Join-Path $remoteDeploymentTestRoot 'known-transaction-without-journal'
        New-Item -ItemType Directory -Path $knownTransactionPath -Force | Out-Null
        $remoteDeploymentModule = Get-Module -Name Countdown.RemoteDeployment | Select-Object -First 1
        $windowsAdapter = & $remoteDeploymentModule { New-CountdownRemoteDeploymentWindowsAdapter }
        $windowsAdapter.State.TransactionModuleImported = $true
        $windowsAdapter.State.InstallStarted = $true
        $windowsAdapter.State.RollbackTransactionPath = $knownTransactionPath
        $windowsAdapter.State.LauncherPath = Join-Path $remoteDeploymentTestRoot 'desktop-launcher.exe'
        $windowsAdapter.State.LegacyLauncherPath = Join-Path $remoteDeploymentTestRoot 'legacy-launcher.cmd'
        $windowsAdapter.State.LauncherTempPath = Join-Path $remoteDeploymentTestRoot 'desktop-launcher.exe.new'
        $windowsAdapterContext = [pscustomobject]@{
            SourceRoot = $remoteDeploymentSource
            InstallPath = $remoteDeploymentInstall
            PowerShell7Path = $remoteDeploymentPowerShell
            DeferredReceiptPath = $remoteDeploymentReceipt
            TaskName = 'Minmin Countdown Fixture'
            LegacyTaskName = '敏敏子倒计时 Fixture'
        }
        $missingJournalMessage = $null
        try {
            & $windowsAdapter.Rollback $windowsAdapter.State $windowsAdapterContext $null | Out-Null
        }
        catch {
            $missingJournalMessage = $_.Exception.Message
        }
        Assert-Equal $true ($missingJournalMessage -match '无法验证待回滚事务的 journal；拒绝声称已回滚') 'Known rollback transaction without a journal fails closed'

        $remoteDeploymentControllerSource = [System.IO.File]::ReadAllText((Join-Path $PSScriptRoot 'Countdown.RemoteDeployment.psm1'), [System.Text.Encoding]::UTF8)
        $preRollbackCaptureCallCount = [regex]::Matches($remoteDeploymentControllerSource, '(?m)^\s*\$preRollbackCapture = Write-CountdownPreRollbackDiagnostics\b').Count
        $preRollbackCaptureIndex = $remoteDeploymentControllerSource.IndexOf('$preRollbackCapture = Write-CountdownPreRollbackDiagnostics')
        $rollbackInvocationIndex = $remoteDeploymentControllerSource.IndexOf('$rollback = & $Adapter.Rollback', $preRollbackCaptureIndex)
        Assert-Equal 1 $preRollbackCaptureCallCount 'Remote deployment invokes pre-rollback diagnostics capture exactly once'
        Assert-Equal $true ($preRollbackCaptureIndex -ge 0 -and $rollbackInvocationIndex -gt $preRollbackCaptureIndex) 'Remote deployment captures diagnostics before invoking rollback'

        $readbackAdapterCommand = Get-Command `
            New-CountdownRemoteDeploymentReadbackSshAdapter `
            -Module Countdown.RemoteDeployment `
            -ErrorAction SilentlyContinue
        $independentReadbackCommand = Get-Command `
            Invoke-CountdownRemoteDeploymentIndependentReadback `
            -Module Countdown.RemoteDeployment `
            -ErrorAction SilentlyContinue
        Assert-Equal $true ($null -ne $readbackAdapterCommand) 'Remote deployment exports the independent readback SSH adapter interface'
        Assert-Equal $true ($null -ne $independentReadbackCommand) 'Remote deployment exports the independent readback orchestration interface'

        $publicReadbackBindingHash = 'F' * 64
        $publicReadbackBindingJson = [ordered]@{
            SchemaVersion = 1
            DeploymentBindingSha256 = $publicReadbackBindingHash
            TokenSha256 = 'E' * 64
            RuntimeProcessId = 4242
        } | ConvertTo-Json -Compress
        $publicReadbackBindingBytes = [System.Text.UTF8Encoding]::new($false).GetBytes(
            $publicReadbackBindingJson)
        $publicReadbackBytes = [System.Text.UTF8Encoding]::new($false).GetBytes(
            $validReadbackJson)
        $publicReadbackOptions = @(
            '-i', 'D:\fixture\id_ed25519',
            '-o', 'BatchMode=yes',
            '-o', 'IdentitiesOnly=yes',
            '-o', 'ConnectTimeout=10',
            '-o', 'ConnectionAttempts=1',
            '-o', 'StrictHostKeyChecking=yes',
            '-o', 'UserKnownHostsFile=D:\fixture\known_hosts',
            '-o', 'KexAlgorithms=curve25519-sha256'
        )
        $publicReadbackRunnerState = [pscustomobject]@{ Calls = 0; ReadbackRemoteCommand = $null }
        $publicReadbackRunner = {
            param($FileName, $Arguments)
            $publicReadbackRunnerState.Calls++
            $payload = if ($publicReadbackRunnerState.Calls -eq 1) {
                $publicReadbackBindingBytes
            }
            else {
                $publicReadbackRunnerState.ReadbackRemoteCommand = [string] $Arguments[-1]
                $publicReadbackBytes
            }
            [pscustomobject]@{
                ExitCode = 0
                StandardOutput = [Convert]::ToBase64String([byte[]] $payload)
                StandardError = ''
                ProcessId = $PID + 1
            }
        }.GetNewClosure()
        $publicReadbackScpRunnerState = [pscustomobject]@{ Calls = 0; FileName = $null; SourceArgument = $null; LastArgument = $null }
        $publicReadbackScpRunner = {
            param($FileName, $Arguments)
            $publicReadbackScpRunnerState.Calls++
            $publicReadbackScpRunnerState.FileName = $FileName
            $publicReadbackScpRunnerState.SourceArgument = [string] $Arguments[-2]
            $publicReadbackScpRunnerState.LastArgument = [string] $Arguments[-1]
            0
        }.GetNewClosure()
        $publicReadbackAdapter = New-CountdownRemoteDeploymentReadbackSshAdapter `
            -Options $publicReadbackOptions `
            -Destination 'replicator@fixture-host' `
            -RemoteRoot 'D:\fixture\readback-root' `
            -ScpExecutablePath 'D:\fixture\scp.exe' `
            -ScpOptions $publicReadbackOptions `
            -ScpCommandRunner $publicReadbackScpRunner `
            -CommandRunner $publicReadbackRunner
        Assert-Equal $false ($null -ne $publicReadbackAdapter.PSObject.Properties['Rollback']) 'Independent readback adapter does not expose Rollback capability'
        Assert-Equal $false ($null -ne $publicReadbackAdapter.PSObject.Properties['RollbackAction']) 'Independent readback adapter does not expose RollbackAction capability'

        $publicReadbackLedger = Join-Path $remoteDeploymentTestRoot 'public-readback-transfer-attempts.json'
        $publicReadbackBindingEvidence = Join-Path $remoteDeploymentTestRoot 'public-readback-binding.json'
        $publicReadbackEvidence = Join-Path $remoteDeploymentTestRoot 'public-independent-readback.json'
        Invoke-CountdownRemoteDeploymentIndependentReadback `
            -LedgerPath $publicReadbackLedger `
            -DeploymentBindingEvidencePath $publicReadbackBindingEvidence `
            -IndependentReadbackEvidencePath $publicReadbackEvidence `
            -RemoteDeploymentBindingPath 'D:\fixture\deployment-binding.json' `
            -DeploymentBindingSha256 $publicReadbackBindingHash `
            -SuccessReceipt ([pscustomobject]@{
                    RuntimeProcessId = 4242
                    SettingsHashAfter = $expectedReadbackSettingsHash
                    SettingsLengthAfter = 2
                    SettingsMtimeUtcAfter = '2026-01-02T00:00:00.0000000Z'
                    SettingsUnchanged = $false
                }) `
            -ExpectedExecutableSha256 $expectedReadbackExecutableHash `
            -ExpectedRemoteHost 'fixture-host' `
            -ExpectedRemoteUser 'fixture-user' `
            -Adapter $publicReadbackAdapter
        $publicReadbackLedgerDocument = Get-Content -LiteralPath $publicReadbackLedger -Raw | ConvertFrom-Json
        Assert-Equal 2 ([int] $publicReadbackRunnerState.Calls) 'Independent readback public adapter executes the binding and readback ssh reads once each'
        Assert-Equal 1 ([int] $publicReadbackScpRunnerState.Calls) 'Independent readback public adapter uploads the reader script exactly once'
        Assert-Equal 'D:\fixture\scp.exe' ([string] $publicReadbackScpRunnerState.FileName) 'Independent readback scp upload starts the injected executable path'
        Assert-Equal 'replicator@fixture-host:D:/fixture/readback-root/post-deployment-independent-readback.ps1' ([string] $publicReadbackScpRunnerState.LastArgument) 'Independent readback scp upload targets the fixed remote script name'
        Assert-Equal 'remote-deployment-binding,post-deployment-independent-readback-script,post-deployment-independent-readback' ((@($publicReadbackLedgerDocument.attempts).id) -join ',') 'Independent readback public interface preserves ledger IDs and order including the scp script entry'
        Assert-Equal 'ssh,scp,ssh' ((@($publicReadbackLedgerDocument.attempts).method) -join ',') 'Independent readback public interface records the scp script channel between both ssh reads'
        Assert-Equal 'succeeded,succeeded,succeeded' ((@($publicReadbackLedgerDocument.attempts).status) -join ',') 'Independent readback public interface records all three attempts successful'
        Assert-Equal $true (Test-Path -LiteralPath $publicReadbackBindingEvidence -PathType Leaf) 'Independent readback public interface publishes binding evidence'
        Assert-Equal $true (Test-Path -LiteralPath $publicReadbackEvidence -PathType Leaf) 'Independent readback public interface publishes readback evidence'
        $publicReadbackScriptEvidence = Join-Path $remoteDeploymentTestRoot 'post-deployment-independent-readback.ps1'
        Assert-Equal $true (Test-Path -LiteralPath $publicReadbackScriptEvidence -PathType Leaf) 'Independent readback public interface persists the uploaded reader script to run evidence'
        Assert-Equal $publicReadbackScriptEvidence ([string] $publicReadbackScpRunnerState.SourceArgument) 'Independent readback scp upload sources the persisted run-evidence reader script'
        $publicReadbackScriptSha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $publicReadbackScriptEvidence).Hash
        Assert-Equal $true ((([string] $publicReadbackRunnerState.ReadbackRemoteCommand).Length -gt 0) -and (([string] $publicReadbackRunnerState.ReadbackRemoteCommand).Length -lt 8000)) 'Independent readback ssh remote command length stays bounded regardless of reader script size'
        $publicReadbackEncodedCommand = [regex]::Match(
            [string] $publicReadbackRunnerState.ReadbackRemoteCommand,
            '-EncodedCommand\s+(\S+)').Groups[1].Value
        $publicReadbackWrapper = [System.Text.Encoding]::Unicode.GetString(
            [Convert]::FromBase64String($publicReadbackEncodedCommand))
        Assert-Equal $true ($publicReadbackWrapper.Contains($publicReadbackScriptSha256)) 'Independent readback short command pins the uploaded script SHA-256'
        Assert-Equal $true ($publicReadbackWrapper.Contains('D:\fixture\readback-root\post-deployment-independent-readback.ps1')) 'Independent readback short command pins the remote script path'
        Assert-Equal $false ($publicReadbackWrapper.Contains('Invoke-CountdownRemoteDeploymentReadbackProbe')) 'Independent readback short command does not inline the reader definitions'

        $wrapperExecutionRoot = Join-Path $remoteDeploymentTestRoot 'wrapper-execution'
        New-Item -ItemType Directory -Path $wrapperExecutionRoot -Force | Out-Null
        $uploadedWrapperChildPath = Join-Path $wrapperExecutionRoot 'post-deployment-independent-readback.ps1'
        [System.IO.File]::WriteAllText(
            $uploadedWrapperChildPath,
            "[Console]::Out.Write('wrapper-child-marker')",
            [System.Text.UTF8Encoding]::new($false))
        $uploadedWrapperChildSha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $uploadedWrapperChildPath).Hash
        $wrapperCapture = [pscustomobject]@{ RemoteCommand = $null }
        $wrapperCaptureRunner = {
            param($FileName, $Arguments)
            $wrapperCapture.RemoteCommand = [string] $Arguments[-1]
            [pscustomobject]@{
                ExitCode = 0
                StandardOutput = ''
                StandardError = ''
                ProcessId = $PID + 1
            }
        }.GetNewClosure()
        $wrapperParameters = @{
            Options = $publicReadbackOptions
            Destination = 'replicator@fixture-host'
            RemoteScriptPath = $uploadedWrapperChildPath
            ExpectedScriptSha256 = ('0' * 64)
            ExpectedRemoteHost = 'fixture-host'
            ExpectedRemoteUser = 'fixture-user'
            ExpectedProcessId = 4242
            ExpectedExecutableSha256 = $expectedReadbackExecutableHash
            ExpectedTokenSha256 = 'E' * 64
            ExpectedDeploymentBindingSha256 = $publicReadbackBindingHash
            DeploymentBindingPath = 'D:\fixture\deployment-binding.json'
            CommandRunner = $wrapperCaptureRunner
        }
        $wrapperEngine = (Get-Command pwsh.exe -CommandType Application -ErrorAction Stop | Select-Object -First 1 -ExpandProperty Source)
        $wrapperFilePath = Join-Path $wrapperExecutionRoot 'executed-wrapper.ps1'
        $wrapperStdoutPath = Join-Path $wrapperExecutionRoot 'executed-wrapper.stdout.txt'
        $wrapperStderrPath = Join-Path $wrapperExecutionRoot 'executed-wrapper.stderr.txt'
        foreach ($wrapperCase in @(
                @{ Name = 'hash-mismatch'; ExpectedScriptSha256 = ('0' * 64); UploadedText = "[Console]::Out.Write('wrapper-child-marker')"; ExpectedExitCode = -1 },
                @{ Name = 'case-insensitive-hash'; ExpectedScriptSha256 = $uploadedWrapperChildSha256.ToLowerInvariant(); UploadedText = "[Console]::Out.Write('wrapper-child-marker')"; ExpectedExitCode = 0 },
                @{ Name = 'child-exit-passthrough'; ExpectedScriptSha256 = $null; UploadedText = 'exit 5'; ExpectedExitCode = 5 }
            )) {
            if ($null -ne $wrapperCase.ExpectedScriptSha256) {
                $wrapperParameters.ExpectedScriptSha256 = [string] $wrapperCase.ExpectedScriptSha256
            }
            else {
                [System.IO.File]::WriteAllText(
                    $uploadedWrapperChildPath,
                    [string] $wrapperCase.UploadedText,
                    [System.Text.UTF8Encoding]::new($false))
                $wrapperParameters.ExpectedScriptSha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $uploadedWrapperChildPath).Hash
            }
            $wrapperCapture.RemoteCommand = $null
            [void] (& $remoteDeploymentModule { Invoke-CountdownRemoteDeploymentIndependentRead @wrapperParameters })
            $wrapperEncoded = [regex]::Match(
                [string] $wrapperCapture.RemoteCommand,
                '-EncodedCommand\s+(\S+)').Groups[1].Value
            $wrapperScriptText = [System.Text.Encoding]::Unicode.GetString(
                [Convert]::FromBase64String($wrapperEncoded))
            [System.IO.File]::WriteAllText(
                $wrapperFilePath,
                $wrapperScriptText,
                [System.Text.UTF8Encoding]::new($false))
            $wrapperProcess = Start-Process -FilePath $wrapperEngine -ArgumentList @(
                '-NoLogo', '-NoProfile', '-File', $wrapperFilePath
            ) -WorkingDirectory $wrapperExecutionRoot -PassThru -WindowStyle Hidden `
                -RedirectStandardOutput $wrapperStdoutPath `
                -RedirectStandardError $wrapperStderrPath
            $wrapperProcess.WaitForExit()
            $wrapperStdout = [System.IO.File]::ReadAllText($wrapperStdoutPath, [System.Text.Encoding]::UTF8)
            $wrapperStderr = [System.Text.Encoding]::UTF8.GetString(
                [System.IO.File]::ReadAllBytes($wrapperStderrPath)) -replace "`e\[[0-9;]*m", ''
            if ($wrapperCase.Name -eq 'hash-mismatch') {
                Assert-Equal $true ($wrapperProcess.ExitCode -ne 0) 'Wrapper aborts with a nonzero exit code on an uploaded script hash mismatch'
                Assert-Equal $true ($wrapperStderr -match '独立回读脚本哈希不匹配') 'Wrapper reports the hash mismatch reason on stderr'
                Assert-Equal $false ($wrapperStdout.Contains('wrapper-child-marker')) 'Wrapper never executes the uploaded script after a hash mismatch'
            }
            elseif ($wrapperCase.Name -eq 'case-insensitive-hash') {
                Assert-Equal 0 ([int] $wrapperProcess.ExitCode) 'Wrapper accepts a lowercase hex script hash'
                Assert-Equal $true ($wrapperStdout.Contains('wrapper-child-marker')) 'Wrapper executes the uploaded script when the hash matches case-insensitively'
            }
            else {
                Assert-Equal 5 ([int] $wrapperProcess.ExitCode) 'Wrapper propagates the reader child exit code'
            }
        }

        $failedPublicReadback = $validReadbackJson | ConvertFrom-Json -AsHashtable
        $failedPublicReadback.RuntimeStage = 'starting'
        $failedPublicReadbackBytes = [System.Text.UTF8Encoding]::new($false).GetBytes(
            ($failedPublicReadback | ConvertTo-Json -Compress))
        $failedPublicReadbackRunnerState = [pscustomobject]@{ Calls = 0 }
        $failedPublicReadbackRunner = {
            param($FileName, $Arguments)
            $failedPublicReadbackRunnerState.Calls++
            $payload = if ($failedPublicReadbackRunnerState.Calls -eq 1) {
                $publicReadbackBindingBytes
            }
            else {
                $failedPublicReadbackBytes
            }
            [pscustomobject]@{
                ExitCode = 0
                StandardOutput = [Convert]::ToBase64String([byte[]] $payload)
                StandardError = ''
                ProcessId = $PID + 1
            }
        }.GetNewClosure()
        $failedPublicReadbackAdapter = New-CountdownRemoteDeploymentReadbackSshAdapter `
            -Options $publicReadbackOptions `
            -Destination 'replicator@fixture-host' `
            -RemoteRoot 'D:\fixture\readback-root' `
            -ScpExecutablePath 'D:\fixture\scp.exe' `
            -ScpOptions $publicReadbackOptions `
            -ScpCommandRunner { param($FileName, $Arguments) 0 } `
            -CommandRunner $failedPublicReadbackRunner
        $failedPublicReadbackMessage = $null
        $failedPublicReadbackEvidence = Join-Path $remoteDeploymentTestRoot 'failed-public-independent-readback.json'
        try {
            Invoke-CountdownRemoteDeploymentIndependentReadback `
                -LedgerPath (Join-Path $remoteDeploymentTestRoot 'failed-public-readback-transfer-attempts.json') `
                -DeploymentBindingEvidencePath (Join-Path $remoteDeploymentTestRoot 'failed-public-readback-binding.json') `
                -IndependentReadbackEvidencePath $failedPublicReadbackEvidence `
                -RemoteDeploymentBindingPath 'D:\fixture\deployment-binding.json' `
                -DeploymentBindingSha256 $publicReadbackBindingHash `
                -SuccessReceipt ([pscustomobject]@{
                        RuntimeProcessId = 4242
                        SettingsHashAfter = $expectedReadbackSettingsHash
                        SettingsLengthAfter = 2
                        SettingsMtimeUtcAfter = '2026-01-02T00:00:00.0000000Z'
                        SettingsUnchanged = $false
                    }) `
                -ExpectedExecutableSha256 $expectedReadbackExecutableHash `
                -ExpectedRemoteHost 'fixture-host' `
                -ExpectedRemoteUser 'fixture-user' `
                -Adapter $failedPublicReadbackAdapter | Out-Null
        }
        catch {
            $failedPublicReadbackMessage = $_.Exception.Message
        }
        Assert-Equal $false ($null -ne $failedPublicReadbackAdapter.PSObject.Properties['RollbackAction']) 'Failed independent readback adapter still exposes no RollbackAction capability'
        Assert-Equal $true ($failedPublicReadbackMessage -match '部署已提交但独立验证不完整，未回滚.*runtime 未达到 window-ready') 'Independent readback public interface reports post-commit failure without a rollback claim'
        Assert-Equal $false (Test-Path -LiteralPath $failedPublicReadbackEvidence) 'Failed independent readback never publishes readback evidence'
        $failedPublicReadbackLedgerDocument = Get-Content -LiteralPath (Join-Path $remoteDeploymentTestRoot 'failed-public-readback-transfer-attempts.json') -Raw | ConvertFrom-Json
        Assert-Equal 'remote-deployment-binding,post-deployment-independent-readback-script,post-deployment-independent-readback' ((@($failedPublicReadbackLedgerDocument.attempts).id) -join ',') 'Contract-drift independent readback still records the three-step ledger sequence'
        Assert-Equal 'succeeded,succeeded,failed' ((@($failedPublicReadbackLedgerDocument.attempts).status) -join ',') 'Contract-drift independent readback marks only the final ssh read failed'

        $remoteHashMismatchRunnerState = [pscustomobject]@{ Calls = 0 }
        $remoteHashMismatchRunner = {
            param($FileName, $Arguments)
            $remoteHashMismatchRunnerState.Calls++
            if ($remoteHashMismatchRunnerState.Calls -eq 1) {
                [pscustomobject]@{
                    ExitCode = 0
                    StandardOutput = [Convert]::ToBase64String([byte[]] $publicReadbackBindingBytes)
                    StandardError = ''
                    ProcessId = $PID + 1
                }
            }
            else {
                [pscustomobject]@{
                    ExitCode = 1
                    StandardOutput = ''
                    StandardError = '独立回读脚本哈希不匹配。'
                    ProcessId = $PID + 1
                }
            }
        }.GetNewClosure()
        $remoteHashMismatchScpRunnerState = [pscustomobject]@{ Calls = 0 }
        $remoteHashMismatchScpRunner = {
            param($FileName, $Arguments)
            $remoteHashMismatchScpRunnerState.Calls++
            0
        }.GetNewClosure()
        $remoteHashMismatchAdapter = New-CountdownRemoteDeploymentReadbackSshAdapter `
            -Options $publicReadbackOptions `
            -Destination 'replicator@fixture-host' `
            -RemoteRoot 'D:\fixture\readback-root' `
            -ScpExecutablePath 'D:\fixture\scp.exe' `
            -ScpOptions $publicReadbackOptions `
            -ScpCommandRunner $remoteHashMismatchScpRunner `
            -CommandRunner $remoteHashMismatchRunner
        $remoteHashMismatchMessage = $null
        $remoteHashMismatchEvidence = Join-Path $remoteDeploymentTestRoot 'remote-hash-mismatch-readback.json'
        try {
            Invoke-CountdownRemoteDeploymentIndependentReadback `
                -LedgerPath (Join-Path $remoteDeploymentTestRoot 'remote-hash-mismatch-transfer-attempts.json') `
                -DeploymentBindingEvidencePath (Join-Path $remoteDeploymentTestRoot 'remote-hash-mismatch-binding.json') `
                -IndependentReadbackEvidencePath $remoteHashMismatchEvidence `
                -RemoteDeploymentBindingPath 'D:\fixture\deployment-binding.json' `
                -DeploymentBindingSha256 $publicReadbackBindingHash `
                -SuccessReceipt ([pscustomobject]@{
                        RuntimeProcessId = 4242
                        SettingsHashAfter = $expectedReadbackSettingsHash
                        SettingsLengthAfter = 2
                        SettingsMtimeUtcAfter = '2026-01-02T00:00:00.0000000Z'
                        SettingsUnchanged = $false
                    }) `
                -ExpectedExecutableSha256 $expectedReadbackExecutableHash `
                -ExpectedRemoteHost 'fixture-host' `
                -ExpectedRemoteUser 'fixture-user' `
                -Adapter $remoteHashMismatchAdapter | Out-Null
        }
        catch {
            $remoteHashMismatchMessage = $_.Exception.Message
        }
        Assert-Equal 1 ([int] $remoteHashMismatchScpRunnerState.Calls) 'Remote hash-mismatch fixture still uploads the reader script first'
        Assert-Equal $true ($remoteHashMismatchMessage -match '部署已提交但独立验证不完整，未回滚.*独立回读脚本哈希不匹配') 'Remote script hash mismatch aborts before execution and reports post-commit verification incomplete'
        Assert-Equal $false (Test-Path -LiteralPath $remoteHashMismatchEvidence) 'Remote hash mismatch never publishes readback evidence'
        $remoteHashMismatchLedgerDocument = Get-Content -LiteralPath (Join-Path $remoteDeploymentTestRoot 'remote-hash-mismatch-transfer-attempts.json') -Raw | ConvertFrom-Json
        Assert-Equal 'remote-deployment-binding,post-deployment-independent-readback-script,post-deployment-independent-readback' ((@($remoteHashMismatchLedgerDocument.attempts).id) -join ',') 'Remote hash mismatch records the full three-step ledger sequence'
        Assert-Equal 'succeeded,succeeded,failed-bytes-unverified' ((@($remoteHashMismatchLedgerDocument.attempts).status) -join ',') 'Remote hash mismatch maps to scp succeeded plus ssh failed-bytes-unverified'
        Assert-Equal 1 ([int] @($remoteHashMismatchLedgerDocument.attempts)[-1].exitCode) 'Remote hash mismatch propagates the nonzero remote exit code'

        $uploadFailureRunnerState = [pscustomobject]@{ Calls = 0 }
        $uploadFailureRunner = {
            param($FileName, $Arguments)
            $uploadFailureRunnerState.Calls++
            [pscustomobject]@{
                ExitCode = 0
                StandardOutput = [Convert]::ToBase64String([byte[]] $publicReadbackBindingBytes)
                StandardError = ''
                ProcessId = $PID + 1
            }
        }.GetNewClosure()
        $uploadFailureAdapter = New-CountdownRemoteDeploymentReadbackSshAdapter `
            -Options $publicReadbackOptions `
            -Destination 'replicator@fixture-host' `
            -RemoteRoot 'D:\fixture\readback-root' `
            -ScpExecutablePath 'D:\fixture\scp.exe' `
            -ScpOptions $publicReadbackOptions `
            -ScpCommandRunner { param($FileName, $Arguments) 7 } `
            -CommandRunner $uploadFailureRunner
        $uploadFailureMessage = $null
        $uploadFailureEvidence = Join-Path $remoteDeploymentTestRoot 'upload-failure-readback.json'
        try {
            Invoke-CountdownRemoteDeploymentIndependentReadback `
                -LedgerPath (Join-Path $remoteDeploymentTestRoot 'upload-failure-transfer-attempts.json') `
                -DeploymentBindingEvidencePath (Join-Path $remoteDeploymentTestRoot 'upload-failure-binding.json') `
                -IndependentReadbackEvidencePath $uploadFailureEvidence `
                -RemoteDeploymentBindingPath 'D:\fixture\deployment-binding.json' `
                -DeploymentBindingSha256 $publicReadbackBindingHash `
                -SuccessReceipt ([pscustomobject]@{
                        RuntimeProcessId = 4242
                        SettingsHashAfter = $expectedReadbackSettingsHash
                        SettingsLengthAfter = 2
                        SettingsMtimeUtcAfter = '2026-01-02T00:00:00.0000000Z'
                        SettingsUnchanged = $false
                    }) `
                -ExpectedExecutableSha256 $expectedReadbackExecutableHash `
                -ExpectedRemoteHost 'fixture-host' `
                -ExpectedRemoteUser 'fixture-user' `
                -Adapter $uploadFailureAdapter | Out-Null
        }
        catch {
            $uploadFailureMessage = $_.Exception.Message
        }
        Assert-Equal 1 ([int] $uploadFailureRunnerState.Calls) 'Upload failure runs only the binding ssh read before failing'
        Assert-Equal $true ($uploadFailureMessage -match '部署已提交但独立验证不完整，未回滚.*SCP 传输失败，退出码：7') 'Upload failure reports post-commit verification incomplete with the scp exit code'
        Assert-Equal $false (Test-Path -LiteralPath $uploadFailureEvidence) 'Upload failure never publishes readback evidence'
        $uploadFailureLedgerDocument = Get-Content -LiteralPath (Join-Path $remoteDeploymentTestRoot 'upload-failure-transfer-attempts.json') -Raw | ConvertFrom-Json
        Assert-Equal 'remote-deployment-binding,post-deployment-independent-readback-script' ((@($uploadFailureLedgerDocument.attempts).id) -join ',') 'Upload failure records binding plus the scp attempt without any ssh readback entry'
        Assert-Equal 'succeeded,failed-bytes-unverified' ((@($uploadFailureLedgerDocument.attempts).status) -join ',') 'Upload failure maps the scp attempt to the failed-bytes-unverified ledger state'
        Assert-Equal 7 ([int] @($uploadFailureLedgerDocument.attempts)[-1].exitCode) 'Upload failure preserves the scp exit code in the ledger'

        $sshStartFailureRunnerState = [pscustomobject]@{ Calls = 0 }
        $sshStartFailureRunner = {
            param($FileName, $Arguments)
            $sshStartFailureRunnerState.Calls++
            if ($sshStartFailureRunnerState.Calls -eq 1) {
                [pscustomobject]@{
                    ExitCode = 0
                    StandardOutput = [Convert]::ToBase64String([byte[]] $publicReadbackBindingBytes)
                    StandardError = ''
                    ProcessId = $PID + 1
                }
            }
            else {
                throw '模拟 ssh.exe 无法启动。'
            }
        }.GetNewClosure()
        $sshStartFailureAdapter = New-CountdownRemoteDeploymentReadbackSshAdapter `
            -Options $publicReadbackOptions `
            -Destination 'replicator@fixture-host' `
            -RemoteRoot 'D:\fixture\readback-root' `
            -ScpExecutablePath 'D:\fixture\scp.exe' `
            -ScpOptions $publicReadbackOptions `
            -ScpCommandRunner { param($FileName, $Arguments) 0 } `
            -CommandRunner $sshStartFailureRunner
        $sshStartFailureMessage = $null
        $sshStartFailureEvidence = Join-Path $remoteDeploymentTestRoot 'ssh-start-failure-readback.json'
        try {
            Invoke-CountdownRemoteDeploymentIndependentReadback `
                -LedgerPath (Join-Path $remoteDeploymentTestRoot 'ssh-start-failure-transfer-attempts.json') `
                -DeploymentBindingEvidencePath (Join-Path $remoteDeploymentTestRoot 'ssh-start-failure-binding.json') `
                -IndependentReadbackEvidencePath $sshStartFailureEvidence `
                -RemoteDeploymentBindingPath 'D:\fixture\deployment-binding.json' `
                -DeploymentBindingSha256 $publicReadbackBindingHash `
                -SuccessReceipt ([pscustomobject]@{
                        RuntimeProcessId = 4242
                        SettingsHashAfter = $expectedReadbackSettingsHash
                        SettingsLengthAfter = 2
                        SettingsMtimeUtcAfter = '2026-01-02T00:00:00.0000000Z'
                        SettingsUnchanged = $false
                    }) `
                -ExpectedExecutableSha256 $expectedReadbackExecutableHash `
                -ExpectedRemoteHost 'fixture-host' `
                -ExpectedRemoteUser 'fixture-user' `
                -Adapter $sshStartFailureAdapter | Out-Null
        }
        catch {
            $sshStartFailureMessage = $_.Exception.Message
        }
        Assert-Equal 2 ([int] $sshStartFailureRunnerState.Calls) 'ssh start failure attempts both ssh reads before failing'
        Assert-Equal $true ($sshStartFailureMessage -match '部署已提交但独立验证不完整，未回滚.*退出码：-1.*无法启动 部署后独立回读') 'ssh start failure reports post-commit verification incomplete with exit code -1'
        Assert-Equal $false (Test-Path -LiteralPath $sshStartFailureEvidence) 'ssh start failure never publishes readback evidence'
        $sshStartFailureLedgerDocument = Get-Content -LiteralPath (Join-Path $remoteDeploymentTestRoot 'ssh-start-failure-transfer-attempts.json') -Raw | ConvertFrom-Json
        Assert-Equal 'remote-deployment-binding,post-deployment-independent-readback-script,post-deployment-independent-readback' ((@($sshStartFailureLedgerDocument.attempts).id) -join ',') 'ssh start failure records the full three-step ledger sequence'
        Assert-Equal 'succeeded,succeeded,failed-bytes-unverified' ((@($sshStartFailureLedgerDocument.attempts).status) -join ',') 'ssh start failure maps to scp succeeded plus ssh failed-bytes-unverified'
        Assert-Equal -1 ([int] @($sshStartFailureLedgerDocument.attempts)[-1].exitCode) 'ssh start failure records exit code -1 in the ssh ledger entry'
    }
    finally {
        Import-Module (Join-Path $PSScriptRoot 'Countdown.Settings.psm1') -Force
        Import-Module (Join-Path $PSScriptRoot 'Countdown.Runtime.psm1') -Force
        Import-Module (Join-Path $PSScriptRoot 'Countdown.InstallTransaction.psm1') -Force -DisableNameChecking
        Import-Module (Join-Path $PSScriptRoot 'Countdown.TransactionAcceptance.psm1') -Force
        Remove-CountdownIsolatedTestTree -Path $remoteDeploymentTestRoot -AllowedRoot $remoteDeploymentEvidenceRoot
    }

    if ($RemoteDeploymentOnly) {
        if ($failures.Count -gt 0) {
            $failures | ForEach-Object { Write-Error $_ }
            exit 1
        }
        [pscustomobject]@{ Passed = $script:assertions; Failed = 0; Scope = 'RemoteDeployment' } | ConvertTo-Json
        exit 0
    }
}

if (-not $AcceptanceOnly) {
$releaseContract = Get-CountdownReleaseContract
$expectedBuildAssets = @(
    'sanrio-official-mymelody.png=>app\assets\sanrio-official-mymelody.png',
    'sanrio-official-cinnamoroll.png=>app\assets\sanrio-official-cinnamoroll.png',
    'sanrio-official-kuromi.png=>app\assets\sanrio-official-kuromi.png',
    'assets\floating-plush\floating-plush-mymelody-reference.png=>app\assets\floating-plush\floating-plush-mymelody-reference.png',
    'assets\floating-plush\floating-plush-cinnamoroll-reference.png=>app\assets\floating-plush\floating-plush-cinnamoroll-reference.png',
    'assets\floating-plush\floating-plush-kuromi-reference.png=>app\assets\floating-plush\floating-plush-kuromi-reference.png',
    'assets\floating-plush\component-day-outer-shell.png=>app\assets\floating-plush\component-day-outer-shell.png',
    'assets\floating-plush\component-day-header-blank.png=>app\assets\floating-plush\component-day-header-blank.png',
    'assets\floating-plush\component-day-card-blank.png=>app\assets\floating-plush\component-day-card-blank.png',
    'assets\floating-plush\component-day-mascots.png=>app\assets\floating-plush\component-day-mascots.png',
    'assets\main-decorations\main-countdown-scene-day.png=>app\assets\main-decorations\main-countdown-scene-day.png',
    'assets\main-decorations\main-notes-scene-day.png=>app\assets\main-decorations\main-notes-scene-day.png',
    'assets\main-decorations\main-countdown-top-night.png=>app\assets\main-decorations\main-countdown-top-night.png',
    'assets\main-decorations\main-notes-top-night.png=>app\assets\main-decorations\main-notes-top-night.png'
)
$actualBuildAssets = @($releaseContract.BuildAssets | ForEach-Object { "$($_.SourcePath)=>$($_.PublishedPath)" })
$expectedCompatibilityFixtures = @(
    'Countdown.App.psm1',
    'sanrio-official-mymelody.png',
    'sanrio-official-cinnamoroll.png',
    'sanrio-official-kuromi.png',
    'assets\floating-plush\floating-plush-mymelody-front.png',
    'assets\floating-plush\floating-plush-mymelody-pose.png',
    'assets\floating-plush\floating-plush-cinnamoroll-front.png',
    'assets\floating-plush\floating-plush-cinnamoroll-pose.png',
    'assets\floating-plush\floating-plush-kuromi-front.png',
    'assets\floating-plush\floating-plush-kuromi-pose.png'
)

Assert-Equal 1 $releaseContract.ContractVersion 'Release contract exposes version one'
Assert-Equal 578 @($releaseContract.PackageFiles).Count 'Release contract declares every package file including source-build inputs'
Assert-Equal 574 $releaseContract.IntegrityHashes.Count 'Release contract protects every non-root package file including source-build inputs'
Assert-Equal $true $releaseContract.IntegrityHashes.Contains('assets\floating-plush\SOURCES.md') 'Release contract packages the plush source metadata used by Core tests'
Assert-Equal $false ('assets\floating-plush\SOURCES.md' -in @($releaseContract.InstalledFiles)) 'Plush source metadata is not installed as a runtime file'
Assert-Equal 56 @($releaseContract.SourceBuildFiles).Count 'Release contract declares the complete source-build closure including icon and content assets'
Assert-Equal 14 @($releaseContract.BuildAssets).Count 'Release contract declares fourteen explicit application asset mappings'
Assert-Equal 501 @($releaseContract.InstalledFiles).Count 'Release contract preserves the installed runtime payload'
Assert-Equal 20 @($releaseContract.RetiredRuntimeFiles).Count 'Release contract preserves the retired runtime cleanup set'
Assert-Equal ($expectedBuildAssets -join ',') ($actualBuildAssets -join ',') 'Release contract exposes the exact build asset mappings'
Assert-Equal ($expectedCompatibilityFixtures -join ',') (@($releaseContract.CompatibilityFixtureFiles) -join ',') 'Release contract exposes the exact compatibility fixtures'
Assert-Equal $true ('Countdown.TransactionAcceptance.psm1' -in @($releaseContract.PackageFiles)) 'Transaction acceptance is packaged'
Assert-Equal $true $releaseContract.IntegrityHashes.Contains('Countdown.TransactionAcceptance.psm1') 'Transaction acceptance is integrity protected'
Assert-Equal $false ('Countdown.TransactionAcceptance.psm1' -in @($releaseContract.InstalledFiles)) 'Transaction acceptance is not installed as runtime'
foreach ($deploymentModule in @('Countdown.Validation.psm1', 'Countdown.RemoteDeployment.psm1', 'Countdown.TransferLedger.psm1')) {
    Assert-Equal $true ($deploymentModule -in @($releaseContract.PackageFiles)) "Deployment module is packaged: $deploymentModule"
    Assert-Equal $true $releaseContract.IntegrityHashes.Contains($deploymentModule) "Deployment module is integrity protected: $deploymentModule"
    Assert-Equal $false ($deploymentModule -in @($releaseContract.InstalledFiles)) "Deployment module is not installed as runtime: $deploymentModule"
}
Assert-Equal 0 @($releaseContract.PackageFiles | Where-Object { $_ -match '(^|\\)(bin|obj)(\\|$)' }).Count 'Release package excludes all bin and obj paths'
foreach ($sourceBuildFile in $releaseContract.SourceBuildFiles) {
    Assert-Equal $true ($sourceBuildFile -in @($releaseContract.PackageFiles)) "Source-build input is packaged: $sourceBuildFile"
    Assert-Equal $true $releaseContract.IntegrityHashes.Contains($sourceBuildFile) "Source-build input is integrity protected: $sourceBuildFile"
}
Assert-Equal 0 `
    @($releaseContract.SourceBuildFiles | Where-Object { $_ -match '\.(?:cs|csproj|xaml)$' -and $_ -in @($releaseContract.InstalledFiles) }).Count `
    'Project, C# and XAML source-build inputs are not installed as runtime files'
$validationModuleSource = [System.IO.File]::ReadAllText(
    (Join-Path $PSScriptRoot 'Countdown.Validation.psm1'))
Assert-Equal 1 ([regex]::Matches(
        $validationModuleSource,
        'assets\\ui-references\\floating-window\\520x390\\2026-09-07-v21\\manifest\.json').Count) `
    'Validation suite pins the floating visual manifest to the current v21 baseline'
Assert-Equal $false ($validationModuleSource.Contains('2026-08-25-v17')) `
    'Validation suite no longer references the retired floating v17 baseline'
Assert-Equal 1 ([regex]::Matches(
        $validationModuleSource,
        'assets\\ui-references\\main-window\\1165x1350\\2026-09-07-v21\\manifest\.json').Count) `
    'Validation suite pins the main-window visual manifest to the current v21 baseline'
Assert-Equal $false ($validationModuleSource.Contains(
        'assets\ui-references\main-window\1165x1350\2026-08-23-v17\manifest.json')) `
    'Validation suite no longer references the retired main-window v17 baseline'
Assert-Equal $false ($validationModuleSource.Contains(
        'assets\ui-references\main-window\1165x1350\2026-08-19-v16\manifest.json')) `
    'Validation suite no longer references the retired main-window v16 baseline'
Assert-Equal $false ($validationModuleSource.Contains('2026-08-13-v11')) `
    'Validation suite no longer references the retired v11 visual baseline'
Assert-Equal $false ($validationModuleSource.Contains('2026-08-06-v6')) `
    'Validation suite no longer references the retired v6 visual baseline'
$floatingPlushSources = [System.IO.File]::ReadAllText(
    (Join-Path $PSScriptRoot 'assets\floating-plush\SOURCES.md'))
Assert-Equal $true ($floatingPlushSources.Contains('DESIGN.md') -and $floatingPlushSources.Contains('现役 manifest')) `
    '玩偶素材来源文档指向 DESIGN.md 绑定的现役 manifest'
foreach ($retiredBaseline in @(
        '2026-08-13-v12',
        '2026-08-13-v11',
        '2026-08-06-v6',
        'v12 manifest'
    )) {
    Assert-Equal $false ($floatingPlushSources.Contains($retiredBaseline)) `
        "玩偶素材来源文档不再写死退役基线：$retiredBaseline"
}

# 条件化现役 visual baseline 验证
$floatingVisualManifest = Join-Path $PSScriptRoot 'assets\ui-references\floating-window\520x390\2026-09-07-v21\manifest.json'
$mainVisualManifest = Join-Path $PSScriptRoot 'assets\ui-references\main-window\1165x1350\2026-09-07-v21\manifest.json'
$visualBaselinesAvailable = (Test-Path -LiteralPath $floatingVisualManifest -PathType Leaf) -and `
                            (Test-Path -LiteralPath $mainVisualManifest -PathType Leaf)

if ($visualBaselinesAvailable) {
$visualBaselineDefinitions = @(
    [pscustomobject]@{
        Name = 'Floating'
        ManifestPath = $floatingVisualManifest
        ExpectedVersion = '2026-09-07-v21'
        ExpectedCaptureIds = @(
            'floating-countdowns-day',
            'floating-countdowns-night',
            'long-notes-day-bottom',
            'long-notes-day-top',
            'long-notes-night-bottom',
            'long-notes-night-top',
            'notes-search-expanded-day',
            'notes-search-expanded-night'
        )
        ExpectedPathPrefix = 'assets/ui-references/floating-window/520x390/2026-09-07-v21/'
    },
    [pscustomobject]@{
        Name = 'Main'
        ManifestPath = $mainVisualManifest
        ExpectedVersion = '2026-09-07-v21'
        ExpectedCaptureIds = @(
            'main-day-countdowns-empty',
            'main-day-countdowns-representative',
            'main-day-countdowns-stress',
            'main-day-notes-edit',
            'main-day-notes-empty',
            'main-day-notes-filter-open',
            'main-day-notes-representative',
            'main-day-notes-stress',
            'main-night-countdowns-empty',
            'main-night-countdowns-representative',
            'main-night-countdowns-stress',
            'main-night-notes-edit',
            'main-night-notes-empty',
            'main-night-notes-filter-open',
            'main-night-notes-representative',
            'main-night-notes-stress'
        )
        ExpectedPathPrefix = 'assets/ui-references/main-window/1165x1350/2026-09-07-v21/'
    }
)
foreach ($visualBaselineDefinition in $visualBaselineDefinitions) {
    $visualManifest = Get-Content -LiteralPath $visualBaselineDefinition.ManifestPath -Raw |
        ConvertFrom-Json -Depth 100
    $visualCaptures = @($visualManifest.captures)
    Assert-Equal ([string] $visualBaselineDefinition.ExpectedVersion) ([string] $visualManifest.baselineVersion) `
        "$($visualBaselineDefinition.Name) visual manifest declares the current baseline version"
    Assert-Equal ($visualBaselineDefinition.ExpectedCaptureIds -join ',') `
        ((@($visualCaptures.captureId) | Sort-Object) -join ',') `
        "$($visualBaselineDefinition.Name) visual manifest declares the exact capture inventory"
    Assert-Equal $visualCaptures.Count @($visualCaptures.path | Sort-Object -Unique).Count `
        "$($visualBaselineDefinition.Name) visual manifest capture paths are unique"
    Assert-Equal $visualCaptures.Count @($visualCaptures.pngSha256 | Sort-Object -Unique).Count `
        "$($visualBaselineDefinition.Name) visual manifest capture hashes are unique"
    foreach ($visualCapture in $visualCaptures) {
        $visualCapturePath = [string] $visualCapture.path
        $visualCaptureFile = Join-Path $PSScriptRoot ($visualCapturePath -replace '/', '\')
        Assert-Equal $true $visualCapturePath.StartsWith(
            $visualBaselineDefinition.ExpectedPathPrefix,
            [System.StringComparison]::Ordinal) `
            "$($visualBaselineDefinition.Name) capture path stays inside the pinned baseline: $($visualCapture.captureId)"
        Assert-Equal ([string] $visualCapture.pngSha256) `
            (Get-FileHash -LiteralPath $visualCaptureFile -Algorithm SHA256).Hash `
            "$($visualBaselineDefinition.Name) capture SHA matches the formal PNG: $($visualCapture.captureId)"
    }
}
} # End of if ($visualBaselinesAvailable)

foreach ($visualTool in @(
        'tools\Build-MainDayOrnamentScene.ps1',
        'tools\MainDecorationSceneBands.psd1',
        'tools\Test-MainDayOrnamentScene.ps1')) {
    Assert-Equal $true ($visualTool -in @($releaseContract.PackageFiles)) "Visual generator is packaged: $visualTool"
    Assert-Equal $true $releaseContract.IntegrityHashes.Contains($visualTool) "Visual generator is integrity protected: $visualTool"
    Assert-Equal $false ($visualTool -in @($releaseContract.InstalledFiles)) "Visual generator is not installed as runtime: $visualTool"
}
foreach ($trustRoot in @('Deploy-OfficeCountdown.ps1', 'Install-Countdown.ps1', 'Countdown.ReleaseContract.psm1')) {
    Assert-Equal $true ($trustRoot -in @($releaseContract.PackageTrustRoots)) "Release contract declares trust root $trustRoot"
    Assert-Equal $false $releaseContract.IntegrityHashes.Contains($trustRoot) "Release contract does not self-hash trust root $trustRoot"
}
$deploySource = [System.IO.File]::ReadAllText(
    (Join-Path $PSScriptRoot 'Deploy-OfficeCountdown.ps1'),
    [System.Text.Encoding]::UTF8)
$deployValidationExitIndex = $deploySource.IndexOf('if ($ValidateOnly) {')
$deployCandidateValidationIndex = if ($deployValidationExitIndex -ge 0) {
    $deploySource.IndexOf('$candidateArchiveValidation = Invoke-CountdownWorkingTreeArchiveValidation', $deployValidationExitIndex)
}
else { -1 }
$deployCommitGateIndex = $deploySource.IndexOf("-Stage '构建前'")
$deploySnapshotExportIndex = if ($deployCommitGateIndex -ge 0) {
    $deploySource.IndexOf('Export-CountdownDeploymentCommitSnapshot', $deployCommitGateIndex)
}
else { -1 }
$deployPackageAssertionIndex = $deploySource.IndexOf('$packageValidation = Assert-CountdownReleasePackage -Root $sourceRoot')
$transferLedgerImportIndex = $deploySource.IndexOf("-Stage '传输账本模块导入'")
$credentialImportIndex = $deploySource.IndexOf(". 'D:\Codex\.codex\credentials\ssh\replicator\office\Use-OfficeSshHelper.ps1'")
Assert-Equal $true (
    $deployValidationExitIndex -ge 0 -and
    $deployCandidateValidationIndex -gt $deployValidationExitIndex -and
    $deployCommitGateIndex -gt $deployCandidateValidationIndex -and
    $deploySnapshotExportIndex -gt $deployCommitGateIndex -and
    $deployPackageAssertionIndex -gt $deployCommitGateIndex -and
    $transferLedgerImportIndex -gt $deployPackageAssertionIndex -and
    $credentialImportIndex -gt $transferLedgerImportIndex
) 'Office deploy validates a working-tree candidate before every Git gate while formal deployment snapshots the verified commit before remote imports'
$deploymentTokens = $null
$deploymentParseErrors = $null
$deploymentAst = [System.Management.Automation.Language.Parser]::ParseInput(
    $deploySource,
    [ref] $deploymentTokens,
    [ref] $deploymentParseErrors)
Assert-Equal 0 $deploymentParseErrors.Count 'Office deployment script parses before gate fixtures are extracted'
$directGitCommands = @(
    $deploymentAst.FindAll({
            param($node)
            if ($node -isnot [System.Management.Automation.Language.CommandAst]) { return $false }
            $commandName = $node.GetCommandName()
            $commandName -in @('git', 'git.exe')
        }, $true)
)
Assert-Equal 0 $directGitCommands.Count 'Office deployment has no Git command outside the checked wrapper invocation'
$deploymentGitWrappers = @(
    $deploymentAst.FindAll({
            param($node)
            $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and
            $node.Name -eq 'Invoke-CountdownDeploymentGit'
        }, $true)
)
$deploymentGitProcessInvocations = @(
    $deploymentAst.FindAll({
            param($node)
            $node -is [System.Management.Automation.Language.CommandAst] -and
            $node.InvocationOperator -eq [System.Management.Automation.Language.TokenKind]::Ampersand -and
            $node.CommandElements.Count -gt 0 -and
            $node.CommandElements[0] -is [System.Management.Automation.Language.VariableExpressionAst] -and
            $node.CommandElements[0].VariablePath.UserPath -eq 'GitPath'
        }, $true)
)
Assert-Equal 1 $deploymentGitWrappers.Count 'Office deployment defines one checked Git wrapper'
Assert-Equal 1 $deploymentGitProcessInvocations.Count 'Office deployment starts Git only once in the checked wrapper'
$deploymentGitWrapper = if ($deploymentGitWrappers.Count -eq 1) { $deploymentGitWrappers[0] } else { $null }
$deploymentGitProcessInvocation = if ($deploymentGitProcessInvocations.Count -eq 1) { $deploymentGitProcessInvocations[0] } else { $null }
$deploymentGitInvocationOwnedByWrapper = $null -ne $deploymentGitWrapper -and
    $null -ne $deploymentGitProcessInvocation -and
    $deploymentGitProcessInvocation.Extent.StartOffset -ge $deploymentGitWrapper.Extent.StartOffset -and
    $deploymentGitProcessInvocation.Extent.EndOffset -le $deploymentGitWrapper.Extent.EndOffset
Assert-Equal $true $deploymentGitInvocationOwnedByWrapper 'The checked wrapper owns the only real Git process invocation'
$deploymentGitInvocationAssignment = if ($null -ne $deploymentGitProcessInvocation) {
    $candidate = $deploymentGitProcessInvocation
    while ($null -ne $candidate -and
        $candidate -isnot [System.Management.Automation.Language.AssignmentStatementAst]) {
        $candidate = $candidate.Parent
    }
    $candidate
}
else { $null }
$deploymentGitInvocationBlock = if ($null -ne $deploymentGitInvocationAssignment -and
    $deploymentGitInvocationAssignment.Parent -is [System.Management.Automation.Language.StatementBlockAst]) {
    $deploymentGitInvocationAssignment.Parent
}
else { $null }
$deploymentGitInvocationStatements = if ($null -ne $deploymentGitInvocationBlock) {
    @($deploymentGitInvocationBlock.Statements)
}
else { @() }
$deploymentGitInvocationStatementIndex = if ($null -ne $deploymentGitInvocationAssignment) {
    [Array]::IndexOf($deploymentGitInvocationStatements, $deploymentGitInvocationAssignment)
}
else { -1 }
$deploymentGitExitCodeStatement = if ($deploymentGitInvocationStatementIndex -ge 0 -and
    $deploymentGitInvocationStatementIndex + 1 -lt $deploymentGitInvocationStatements.Count) {
    $deploymentGitInvocationStatements[$deploymentGitInvocationStatementIndex + 1]
}
else { $null }
$deploymentGitExitCodeText = if ($null -ne $deploymentGitExitCodeStatement) {
    $deploymentGitExitCodeStatement.Extent.Text.Trim()
}
else { '' }
Assert-Equal '$exitCode = $LASTEXITCODE' $deploymentGitExitCodeText 'Office deployment captures the real Git exit code in the immediately following statement'
Assert-Equal $true (
    $deploySource.Contains("@('status', '--porcelain=v1', '--untracked-files=all')") -and
    $deploySource.Contains("@('branch', '--show-current')") -and
    $deploySource.Contains("@('rev-parse', '--verify', 'HEAD')") -and
    $deploySource.Contains("@('rev-parse', '--verify', 'refs/remotes/origin/main')")
) 'Office deployment repository gate checks clean tree, branch, HEAD, and fetched origin/main'
foreach ($stage in @('构建前', '构建后', '打包前', '打包后', '远程输出前')) {
    Assert-Equal $true $deploySource.Contains("-Stage '$stage'") "Office deployment runs repository gate at $stage"
}
Assert-Equal $true (
    $deploySource.Contains("@('archive', '--format=zip', `"--output=`$DestinationPath`", `$Commit)") -and
    $deploySource.Contains('Copy-Item -LiteralPath (Join-Path $sourceRoot $file)') -and
    -not $deploySource.Contains('Copy-Item -LiteralPath (Join-Path $repoRoot $file)') -and
    $deploySource.Contains("Assert-CountdownDeploymentFileManifest -Root `$sourceRoot -Manifest `$packageManifest -Stage '打包后'") -and
    $deploySource.Contains("Assert-CountdownDeploymentFileManifest -Root `$extractRoot -Manifest `$packageManifest -Stage '远程输出前'")
) 'Office deployment builds package bytes only from the verified commit snapshot'
$buildInvocationIndex = $deploySource.IndexOf('[void] (Invoke-CountdownDeploymentLockedCommitFile', $deploySource.IndexOf('$powerShell7 ='))
$buildScriptPathIndex = $deploySource.IndexOf("-RelativePath 'Build-CountdownApplication.ps1'", $buildInvocationIndex)
$transferLedgerHashAssignmentIndex = $deploySource.IndexOf('$transferLedgerSha256 = Invoke-CountdownDeploymentLockedCommitFile', $buildScriptPathIndex)
$transferLedgerPathIndex = $deploySource.IndexOf("-RelativePath 'Countdown.TransferLedger.psm1'", $transferLedgerHashAssignmentIndex)
Assert-Equal $true (
    $buildInvocationIndex -ge 0 -and
    $buildScriptPathIndex -gt $buildInvocationIndex -and
    $transferLedgerHashAssignmentIndex -gt $buildScriptPathIndex -and
    $transferLedgerPathIndex -gt $transferLedgerHashAssignmentIndex
) 'Office deployment discards build stdout before assigning the locked TransferLedger SHA-256'

$deploymentGateFunctionNames = @(
    'Invoke-CountdownDeploymentGit',
    'Get-CountdownDeploymentGitScalar',
    'Assert-CountdownDeploymentRepositoryState',
    'Export-CountdownDeploymentCommitSnapshot',
    'Resolve-CountdownDeploymentSnapshotPath',
    'Get-CountdownDeploymentCommitFileIdentity',
    'New-CountdownDeploymentCommitIdentityManifest',
    'Invoke-CountdownDeploymentLockedCommitFile',
    'New-CountdownDeploymentCommitFileManifest',
    'New-CountdownDeploymentFileManifest',
    'Assert-CountdownDeploymentFileManifest',
    'Assert-CountdownDeploymentFileSet',
    'Invoke-CountdownWorkingTreeArchiveValidation',
    'Assert-CountdownRemoteInstallScript'
)
$deploymentGateFunctions = @(
    $deploymentAst.FindAll({
            param($node)
            $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and
            $node.Name -in $deploymentGateFunctionNames
        }, $true)
)
Assert-Equal $deploymentGateFunctionNames.Count $deploymentGateFunctions.Count 'Office deployment exposes every gate function required by isolated fixtures'
$deploymentGateEvidenceRoot = 'D:\agents\run-evidence\migration-preflight-20260825T013156Z\clock-countdown-deployment-gate-tests'
$deploymentGateTestRoot = Join-Path $deploymentGateEvidenceRoot ([guid]::NewGuid().ToString('N'))
$deploymentGateModulePath = Join-Path $deploymentGateTestRoot 'Countdown.DeploymentGate.Fixture.psm1'
try {
    New-Item -ItemType Directory -Path $deploymentGateTestRoot -Force | Out-Null
    $deploymentGateModuleSource = "Set-StrictMode -Version Latest`r`n`$ErrorActionPreference = 'Stop'`r`n" +
        (($deploymentGateFunctions | ForEach-Object { $_.Extent.Text }) -join "`r`n`r`n")
    [System.IO.File]::WriteAllText(
        $deploymentGateModulePath,
        $deploymentGateModuleSource,
        [System.Text.UTF8Encoding]::new($false))
    Import-Module $deploymentGateModulePath -Force

    $workingTreeCandidateRoot = Join-Path $deploymentGateTestRoot 'working-tree-candidate'
    New-Item -ItemType Directory -Path $workingTreeCandidateRoot -Force | Out-Null
    $workingTreeCandidatePath = Join-Path $workingTreeCandidateRoot 'candidate.txt'
    $workingTreeCandidateBytes = [System.Text.UTF8Encoding]::new($false).GetBytes('working-tree-bytes')
    [System.IO.File]::WriteAllBytes($workingTreeCandidatePath, $workingTreeCandidateBytes)
    $oldHeadHash = [Convert]::ToHexString(
        [System.Security.Cryptography.SHA256]::HashData(
            [System.Text.UTF8Encoding]::new($false).GetBytes('old-head-bytes')))
    $workingTreeHash = [Convert]::ToHexString(
        [System.Security.Cryptography.SHA256]::HashData($workingTreeCandidateBytes))
    $successfulCandidateArchive = Join-Path $deploymentGateTestRoot 'candidate-success.zip'
    $candidateArchiveResult = Invoke-CountdownWorkingTreeArchiveValidation `
        -RepositoryRoot $workingTreeCandidateRoot `
        -RelativePaths @('candidate.txt') `
        -TemporaryArchivePath $successfulCandidateArchive
    Assert-Equal $workingTreeHash $candidateArchiveResult.SourceManifest['candidate.txt'] 'Working-tree validation archives current candidate bytes'
    Assert-Equal $false ([string]::Equals($oldHeadHash, $candidateArchiveResult.SourceManifest['candidate.txt'], [System.StringComparison]::OrdinalIgnoreCase)) 'Working-tree validation does not archive stale HEAD bytes'
    Assert-Equal 1 $candidateArchiveResult.ArchiveFiles 'Working-tree validation verifies the exact archive entry allowlist'
    Assert-Equal $false (Test-Path -LiteralPath $successfulCandidateArchive) 'Working-tree validation removes its temporary ZIP after success'

    $failedCandidateArchive = Join-Path $deploymentGateTestRoot 'candidate-failure.zip'
    $candidateArchiveFailureRejected = $false
    try {
        Invoke-CountdownWorkingTreeArchiveValidation `
            -RepositoryRoot $workingTreeCandidateRoot `
            -RelativePaths @('candidate.txt', 'missing.txt') `
            -TemporaryArchivePath $failedCandidateArchive | Out-Null
    }
    catch {
        $candidateArchiveFailureRejected = $_.Exception.Message -match 'missing.txt'
    }
    Assert-Equal $true $candidateArchiveFailureRejected 'Working-tree validation rejects a missing allowlisted file after opening the archive'
    Assert-Equal $false (Test-Path -LiteralPath $failedCandidateArchive) 'Working-tree validation removes its temporary ZIP after failure'

    $validRemoteInstallScript = @(
        "Invoke-LockedDeploymentFile -Path 'archive.zip' -ExpectedSha256 '$('A' * 64)' -Action { param(`$lockedPath) `$lockedPath }",
        "Invoke-LockedDeploymentFile -Path 'release.psm1' -ExpectedSha256 '$('B' * 64)' -Action { param(`$lockedPath) `$lockedPath }",
        "Invoke-LockedDeploymentFile -Path 'Countdown.TransferLedger.psm1' -ExpectedSha256 '$('C' * 64)' -Action { param(`$lockedPath) `$lockedPath }",
        "Invoke-LockedDeploymentFile -Path 'Countdown.RemoteDeployment.psm1' -ExpectedSha256 '$('D' * 64)' -Action { param(`$lockedPath) `$lockedPath }"
    ) -join "`r`n"
    $validRemoteInstallResult = Assert-CountdownRemoteInstallScript -ScriptText $validRemoteInstallScript
    Assert-Equal 4 $validRemoteInstallResult.InvocationCount 'Generated remote install script locks TransferLedger before RemoteDeployment and keeps every parameter on its invocation'

    $invalidHashRemoteInstallScript = $validRemoteInstallScript.Replace(
        "-ExpectedSha256 '$('C' * 64)'",
        "-ExpectedSha256 'build output'")
    $invalidHashRejected = $false
    try {
        Assert-CountdownRemoteInstallScript -ScriptText $invalidHashRemoteInstallScript | Out-Null
    }
    catch {
        $invalidHashRejected = $_.Exception.Message -match 'ExpectedSha256 必须是 64 位十六进制字面量'
    }
    Assert-Equal $true $invalidHashRejected 'Generated remote install script rejects build output in an expected SHA-256 before upload'

    $brokenRemoteInstallScript = @(
        "Invoke-LockedDeploymentFile -Path 'archive.zip' -ExpectedSha256 '$('A' * 64)' -Action { param(`$lockedPath) `$lockedPath }",
        'Invoke-LockedDeploymentFile',
        "    -Path 'release.psm1' -ExpectedSha256 '$('B' * 64)' -Action { param(`$lockedPath) `$lockedPath }",
        "Invoke-LockedDeploymentFile -Path 'Countdown.TransferLedger.psm1' -ExpectedSha256 '$('C' * 64)' -Action { param(`$lockedPath) `$lockedPath }",
        "Invoke-LockedDeploymentFile -Path 'Countdown.RemoteDeployment.psm1' -ExpectedSha256 '$('D' * 64)' -Action { param(`$lockedPath) `$lockedPath }"
    ) -join "`r`n"
    $brokenRemoteInstallRejected = $false
    $brokenRemoteInstallMessage = ''
    try {
        Assert-CountdownRemoteInstallScript -ScriptText $brokenRemoteInstallScript | Out-Null
    }
    catch {
        $brokenRemoteInstallRejected = $true
        $brokenRemoteInstallMessage = $_.Exception.Message
    }
    Assert-Equal $true $brokenRemoteInstallRejected 'Generated remote install script rejects a bare locked file command before upload'
    Assert-Equal $true $brokenRemoteInstallMessage.Contains('缺少参数') 'Generated remote install script reports the missing mandatory parameter set'

    foreach ($missingValueParameter in @('Path', 'ExpectedSha256', 'Action')) {
        $missingValueInvocation = switch ($missingValueParameter) {
            'Path' { "Invoke-LockedDeploymentFile -Path -ExpectedSha256 '$('B' * 64)' -Action { param(`$lockedPath) `$lockedPath }" }
            'ExpectedSha256' { "Invoke-LockedDeploymentFile -Path 'release.psm1' -ExpectedSha256 -Action { param(`$lockedPath) `$lockedPath }" }
            'Action' { "Invoke-LockedDeploymentFile -Path 'release.psm1' -ExpectedSha256 '$('B' * 64)' -Action" }
        }
        $missingValueScript = @(
            "Invoke-LockedDeploymentFile -Path 'archive.zip' -ExpectedSha256 '$('A' * 64)' -Action { param(`$lockedPath) `$lockedPath }",
            $missingValueInvocation,
            "Invoke-LockedDeploymentFile -Path 'Countdown.TransferLedger.psm1' -ExpectedSha256 '$('C' * 64)' -Action { param(`$lockedPath) `$lockedPath }",
            "Invoke-LockedDeploymentFile -Path 'Countdown.RemoteDeployment.psm1' -ExpectedSha256 '$('D' * 64)' -Action { param(`$lockedPath) `$lockedPath }"
        ) -join "`r`n"
        $missingValueRejected = $false
        try {
            Assert-CountdownRemoteInstallScript -ScriptText $missingValueScript | Out-Null
        }
        catch {
            $missingValueRejected = $_.Exception.Message -match "参数 $missingValueParameter 缺少值"
        }
        Assert-Equal $true $missingValueRejected "Generated remote install script rejects a $missingValueParameter parameter without a value"
    }

    $fixtureCommit = '1111111111111111111111111111111111111111'
    $deploymentGitFixture = [pscustomobject]@{
        FailCommand = ''
        FailExitCode = 0
        Dirty = $false
        Branch = 'main'
        Head = $fixtureCommit
        Remote = $fixtureCommit
    }
    $deploymentGitAdapter = {
        param($RepositoryRoot, $Arguments)

        $command = [string] $Arguments[0]
        if ($deploymentGitFixture.FailCommand -eq $command) {
            return [pscustomobject]@{ ExitCode = $deploymentGitFixture.FailExitCode; Output = @() }
        }
        $output = switch ($command) {
            'fetch' { @() }
            'status' { if ($deploymentGitFixture.Dirty) { @(' M changed-during-build.txt') } else { @() } }
            'branch' { @($deploymentGitFixture.Branch) }
            'rev-parse' {
                if ($Arguments[-1] -eq 'HEAD') { @($deploymentGitFixture.Head) }
                else { @($deploymentGitFixture.Remote) }
            }
            default { throw "Unexpected fixture Git command: $command" }
        }
        [pscustomobject]@{ ExitCode = 0; Output = @($output) }
    }.GetNewClosure()

    $deploymentGitFixture.FailCommand = 'status'
    $deploymentGitFixture.FailExitCode = 19
    $emptyGitFailureRejected = $false
    try {
        Assert-CountdownDeploymentRepositoryState -GitPath 'fixture-git.exe' -RepositoryRoot $deploymentGateTestRoot -Stage '失败夹具' -Adapter $deploymentGitAdapter | Out-Null
    }
    catch {
        $emptyGitFailureRejected = $_.Exception.Message -match '退出码 19' -and $_.Exception.Message -match '<no output>'
    }
    Assert-Equal $true $emptyGitFailureRejected 'Repository gate rejects a nonzero Git command with empty output'

    $deploymentGitFixture.FailCommand = ''
    $deploymentGitFixture.FailExitCode = 0
    $deploymentGitFixture.Dirty = $true
    $formalDirtyRejected = $false
    try {
        Assert-CountdownDeploymentRepositoryState -GitPath 'fixture-git.exe' -RepositoryRoot $deploymentGateTestRoot -Stage '构建前夹具' -Adapter $deploymentGitAdapter | Out-Null
    }
    catch {
        $formalDirtyRejected = $_.Exception.Message -match '工作树不干净'
    }
    Assert-Equal $true $formalDirtyRejected 'Formal deployment rejects a dirty worktree at its initial gate before remote actions'

    $deploymentGitFixture.Dirty = $false
    $initialGate = Assert-CountdownDeploymentRepositoryState -GitPath 'fixture-git.exe' -RepositoryRoot $deploymentGateTestRoot -Stage '构建前夹具' -Adapter $deploymentGitAdapter
    Assert-Equal $fixtureCommit $initialGate.Head 'Repository gate accepts one clean pushed commit'

    $deploymentGitFixture.Remote = '2222222222222222222222222222222222222222'
    $remoteAdvanceRejected = $false
    try {
        Assert-CountdownDeploymentRepositoryState -GitPath 'fixture-git.exe' -RepositoryRoot $deploymentGateTestRoot -Stage '构建后夹具' -ExpectedCommit $fixtureCommit -Adapter $deploymentGitAdapter | Out-Null
    }
    catch {
        $remoteAdvanceRejected = $_.Exception.Message -match 'HEAD 与 origin/main 不一致'
    }
    Assert-Equal $true $remoteAdvanceRejected 'Repository gate rejects origin/main advancing after the initial gate'

    $deploymentGitFixture.Remote = $fixtureCommit
    $deploymentGitFixture.Dirty = $true
    $buildDirtyRejected = $false
    try {
        Assert-CountdownDeploymentRepositoryState -GitPath 'fixture-git.exe' -RepositoryRoot $deploymentGateTestRoot -Stage '构建后夹具' -ExpectedCommit $fixtureCommit -Adapter $deploymentGitAdapter | Out-Null
    }
    catch {
        $buildDirtyRejected = $_.Exception.Message -match '工作树不干净'
    }
    Assert-Equal $true $buildDirtyRejected 'Repository gate rejects a worktree dirtied during the build'

    $trustRootRepository = Join-Path $deploymentGateTestRoot 'commit-trust-root-repository'
    $trustedSnapshotRoot = Join-Path $deploymentGateTestRoot 'trusted-snapshot'
    $replacedSnapshotRoot = Join-Path $deploymentGateTestRoot 'replaced-snapshot'
    $trustRootArchive = Join-Path $deploymentGateTestRoot 'commit-trust-root.zip'
    $trustRootModulePath = Join-Path $trustRootRepository 'Countdown.ReleaseContract.psm1'
    $trustedModuleSource = @'
function Get-CountdownIssue4TrustRootFixtureValue { 'benign' }
Export-ModuleMember -Function Get-CountdownIssue4TrustRootFixtureValue
'@
    $maliciousModuleSource = @'
Set-Variable -Scope Global -Name CountdownIssue4MaliciousSentinel -Value 'executed'
function Get-CountdownIssue4TrustRootFixtureValue { 'malicious' }
Export-ModuleMember -Function Get-CountdownIssue4TrustRootFixtureValue
'@
    New-Item -ItemType Directory -Path $trustRootRepository -Force | Out-Null
    [System.IO.File]::WriteAllText($trustRootModulePath, $trustedModuleSource, [System.Text.UTF8Encoding]::new($false))
    $fixtureGitPath = 'fixture-git.exe'
    $trustRootCommit = '3333333333333333333333333333333333333333'
    $trustedBlobIdentity = '4444444444444444444444444444444444444444'
    $maliciousBlobIdentity = '5555555555555555555555555555555555555555'
    $trustRootGitAdapter = {
        param($RepositoryRoot, $Arguments)

        $output = switch ([string] $Arguments[0]) {
            'rev-parse' { @($trustedBlobIdentity) }
            'hash-object' {
                $content = [System.IO.File]::ReadAllText([string] $Arguments[-1], [System.Text.Encoding]::UTF8)
                if ([string]::Equals($content, $trustedModuleSource, [System.StringComparison]::Ordinal)) {
                    @($trustedBlobIdentity)
                }
                else {
                    @($maliciousBlobIdentity)
                }
            }
            'archive' {
                $outputArgument = @($Arguments | Where-Object { ([string] $_).StartsWith('--output=', [System.StringComparison]::Ordinal) })
                if ($outputArgument.Count -ne 1) { throw 'Archive fixture did not receive one output path.' }
                $destinationPath = ([string] $outputArgument[0]).Substring('--output='.Length)
                Compress-Archive -LiteralPath $trustRootModulePath -DestinationPath $destinationPath -Force
                @()
            }
            default { throw "Unexpected trust-root fixture Git command: $($Arguments[0])" }
        }
        [pscustomobject]@{ ExitCode = 0; Output = @($output) }
    }.GetNewClosure()
    $trustRootIdentityManifest = New-CountdownDeploymentCommitIdentityManifest `
        -GitPath $fixtureGitPath `
        -RepositoryRoot $trustRootRepository `
        -Commit $trustRootCommit `
        -RelativePaths @('Countdown.ReleaseContract.psm1') `
        -Adapter $trustRootGitAdapter
    Export-CountdownDeploymentCommitSnapshot `
        -GitPath $fixtureGitPath `
        -RepositoryRoot $trustRootRepository `
        -Commit $trustRootCommit `
        -DestinationPath $trustRootArchive `
        -Adapter $trustRootGitAdapter
    Expand-Archive -LiteralPath $trustRootArchive -DestinationPath $trustedSnapshotRoot

    [System.IO.File]::WriteAllText($trustRootModulePath, $maliciousModuleSource, [System.Text.UTF8Encoding]::new($false))
    Compress-Archive -LiteralPath $trustRootModulePath -DestinationPath $trustRootArchive -Force
    Expand-Archive -LiteralPath $trustRootArchive -DestinationPath $replacedSnapshotRoot
    Remove-Variable -Name CountdownIssue4MaliciousSentinel -Scope Global -ErrorAction SilentlyContinue
    $replacedArchiveRejected = $false
    try {
        Invoke-CountdownDeploymentLockedCommitFile `
            -GitPath $fixtureGitPath `
            -RepositoryRoot $trustRootRepository `
            -Root $replacedSnapshotRoot `
            -RelativePath 'Countdown.ReleaseContract.psm1' `
            -ExpectedIdentity $trustRootIdentityManifest['Countdown.ReleaseContract.psm1'] `
            -Stage '首次发布契约夹具' `
            -Adapter $trustRootGitAdapter `
            -Action { param($lockedPath, $sha256) Import-Module $lockedPath -Force | Out-Null }
    }
    catch {
        $replacedArchiveRejected = $_.Exception.Message -match 'blob identity 不一致'
    }
    Assert-Equal $true $replacedArchiveRejected 'Commit snapshot replacement is rejected before the first release-contract code executes'
    Assert-Equal $false ($null -ne (Get-Variable -Name CountdownIssue4MaliciousSentinel -Scope Global -ErrorAction SilentlyContinue)) 'Rejected first trust root never executes its malicious sentinel'

    $maliciousSwapCandidate = Join-Path $deploymentGateTestRoot 'malicious-release-contract.psm1'
    $trustedSwapBackup = Join-Path $deploymentGateTestRoot 'trusted-release-contract.backup.psm1'
    [System.IO.File]::WriteAllText($maliciousSwapCandidate, $maliciousModuleSource, [System.Text.UTF8Encoding]::new($false))
    $swapState = [pscustomobject]@{
        Rejected = $false
        Succeeded = $false
        Restored = $false
        ImportedValue = ''
    }
    $lockedImportAction = {
        param($lockedPath, $sha256)

        try {
            [System.IO.File]::Replace($maliciousSwapCandidate, $lockedPath, $trustedSwapBackup, $true)
            $swapState.Succeeded = $true
            try {
                $fixtureModule = Import-Module $lockedPath -Force -PassThru
                $swapState.ImportedValue = & $fixtureModule { Get-CountdownIssue4TrustRootFixtureValue }
            }
            finally {
                [System.IO.File]::Replace($trustedSwapBackup, $lockedPath, $maliciousSwapCandidate, $true)
                $swapState.Restored = $true
            }
        }
        catch {
            if ($swapState.Succeeded) { throw }
            $swapState.Rejected = $true
            $fixtureModule = Import-Module $lockedPath -Force -PassThru
            $swapState.ImportedValue = & $fixtureModule { Get-CountdownIssue4TrustRootFixtureValue }
        }
    }.GetNewClosure()
    Invoke-CountdownDeploymentLockedCommitFile `
        -GitPath $fixtureGitPath `
        -RepositoryRoot $trustRootRepository `
        -Root $trustedSnapshotRoot `
        -RelativePath 'Countdown.ReleaseContract.psm1' `
        -ExpectedIdentity $trustRootIdentityManifest['Countdown.ReleaseContract.psm1'] `
        -Stage '发布契约导入窗口夹具' `
        -Adapter $trustRootGitAdapter `
        -Action $lockedImportAction
    Assert-Equal $true $swapState.Rejected 'Locked release-contract import rejects an atomic malicious replacement'
    Assert-Equal $false $swapState.Succeeded 'Malicious module cannot enter the verified import window'
    Assert-Equal $false $swapState.Restored 'Rejected replacement never needs a restore window'
    Assert-Equal 'benign' $swapState.ImportedValue 'Locked import executes the same benign bytes that passed Git identity verification'
    Assert-Equal $false ($null -ne (Get-Variable -Name CountdownIssue4MaliciousSentinel -Scope Global -ErrorAction SilentlyContinue)) 'Import-window replacement cannot execute its malicious sentinel'
    Remove-Module Countdown.ReleaseContract -Force -ErrorAction SilentlyContinue
    Import-Module (Join-Path $PSScriptRoot 'Countdown.ReleaseContract.psm1') -Force

    $controlFile = Join-Path $deploymentGateTestRoot 'Deploy-Control.ps1'
    [System.IO.File]::WriteAllText($controlFile, 'committed control', [System.Text.UTF8Encoding]::new($false))
    $controlManifestFixture = New-CountdownDeploymentFileManifest -Root $deploymentGateTestRoot -RelativePaths @('Deploy-Control.ps1')
    [System.IO.File]::WriteAllText($controlFile, 'changed control', [System.Text.UTF8Encoding]::new($false))
    $controlChangeRejected = $false
    try {
        Assert-CountdownDeploymentFileManifest -Root $deploymentGateTestRoot -Manifest $controlManifestFixture -Stage '打包前夹具'
    }
    catch {
        $controlChangeRejected = $_.Exception.Message -match '部署文件已变化'
    }
    Assert-Equal $true $controlChangeRejected 'Deployment gate rejects a changed control file'

    $snapshotRoot = Join-Path $deploymentGateTestRoot 'commit-snapshot'
    $activeRoot = Join-Path $deploymentGateTestRoot 'active-worktree'
    $fixturePackageRoot = Join-Path $deploymentGateTestRoot 'package'
    $fixtureExtractRoot = Join-Path $deploymentGateTestRoot 'package-check'
    New-Item -ItemType Directory -Path $snapshotRoot, $activeRoot, $fixturePackageRoot -Force | Out-Null
    [System.IO.File]::WriteAllText((Join-Path $snapshotRoot 'payload.txt'), 'committed payload', [System.Text.UTF8Encoding]::new($false))
    [System.IO.File]::WriteAllText((Join-Path $activeRoot 'payload.txt'), 'committed payload', [System.Text.UTF8Encoding]::new($false))
    $snapshotManifestFixture = New-CountdownDeploymentFileManifest -Root $snapshotRoot -RelativePaths @('payload.txt')
    [System.IO.File]::WriteAllText((Join-Path $activeRoot 'payload.txt'), 'concurrent worktree change', [System.Text.UTF8Encoding]::new($false))
    Copy-Item -LiteralPath (Join-Path $snapshotRoot 'payload.txt') -Destination (Join-Path $fixturePackageRoot 'payload.txt')
    $fixtureArchive = Join-Path $deploymentGateTestRoot 'fixture-package.zip'
    Compress-Archive -Path (Join-Path $fixturePackageRoot '*') -DestinationPath $fixtureArchive
    Expand-Archive -LiteralPath $fixtureArchive -DestinationPath $fixtureExtractRoot
    Assert-CountdownDeploymentFileSet -Root $fixtureExtractRoot -ExpectedFiles @('payload.txt') -Stage 'ZIP 夹具'
    Assert-CountdownDeploymentFileManifest -Root $fixtureExtractRoot -Manifest $snapshotManifestFixture -Stage 'ZIP 夹具'
    Assert-Equal 'committed payload' ([System.IO.File]::ReadAllText((Join-Path $fixtureExtractRoot 'payload.txt'), [System.Text.Encoding]::UTF8)) 'Concurrent worktree changes cannot enter the snapshot-backed ZIP'

    [System.IO.File]::WriteAllText((Join-Path $fixtureExtractRoot 'payload.txt'), 'changed ZIP payload', [System.Text.UTF8Encoding]::new($false))
    $zipPayloadChangeRejected = $false
    try {
        Assert-CountdownDeploymentFileManifest -Root $fixtureExtractRoot -Manifest $snapshotManifestFixture -Stage 'ZIP 哈希夹具'
    }
    catch {
        $zipPayloadChangeRejected = $_.Exception.Message -match '部署文件已变化'
    }
    Assert-Equal $true $zipPayloadChangeRejected 'Deployment gate rejects a changed file extracted from the ZIP'

    [System.IO.File]::WriteAllText((Join-Path $fixtureExtractRoot 'unexpected.txt'), 'unexpected ZIP file', [System.Text.UTF8Encoding]::new($false))
    $zipFileSetChangeRejected = $false
    try {
        Assert-CountdownDeploymentFileSet -Root $fixtureExtractRoot -ExpectedFiles @('payload.txt') -Stage 'ZIP 文件集夹具'
    }
    catch {
        $zipFileSetChangeRejected = $_.Exception.Message -match '发布包文件集与已验证清单不一致'
    }
    Assert-Equal $true $zipFileSetChangeRejected 'Deployment gate rejects an unexpected file extracted from the ZIP'
}
finally {
    Remove-Module 'Countdown.DeploymentGate.Fixture' -Force -ErrorAction SilentlyContinue
    Remove-CountdownIsolatedTestTree -Path $deploymentGateTestRoot -AllowedRoot $deploymentGateEvidenceRoot
}
$recordedTransferCallCount = [regex]::Matches(
    $deploySource,
    '(?m)^\s*Invoke-CountdownRecordedScpTransfer\b').Count
Assert-Equal 2 $recordedTransferCallCount 'Office deploy records both SCP commands through the transfer ledger interface'
$recordedScpCalls = @(
    $deploymentAst.FindAll({
            param($node)
            $node -is [System.Management.Automation.Language.CommandAst] -and
            $node.GetCommandName() -eq 'Invoke-CountdownRecordedScpTransfer'
        }, $true)
)
Assert-Equal 2 $recordedScpCalls.Count 'Office deploy AST finds both recorded SCP transfer calls'
foreach ($recordedScpCall in $recordedScpCalls) {
    $scpParameterNames = @(
        $recordedScpCall.CommandElements |
            Where-Object { $_ -is [System.Management.Automation.Language.CommandParameterAst] } |
            ForEach-Object { [string] $_.ParameterName }
    )
    Assert-Equal $true ($scpParameterNames -contains 'ScpExecutablePath') 'Office deploy passes the resolved SCP executable path into the transfer ledger'
    Assert-Equal $true ($scpParameterNames -contains 'ScpOptions') 'Office deploy keeps strict SCP options on the transfer ledger call'
}
$recordedSshReadCallCount = [regex]::Matches(
    $deploySource,
    '\bInvoke-CountdownRecordedSshRead\b').Count
Assert-Equal 2 $recordedSshReadCallCount 'Office deploy directly records both failure evidence reads; committed binding/readback are owned by the RemoteDeployment interface'
Assert-Equal $false $deploySource.Contains('& scp.exe') 'Office deploy has no direct SCP call that bypasses the transfer ledger'
$directSshScpCommands = @(
    $deploymentAst.FindAll({
            param($node)
            if ($node -isnot [System.Management.Automation.Language.CommandAst]) { return $false }
            $commandName = $node.GetCommandName()
            $commandName -in @('ssh', 'ssh.exe', 'scp', 'scp.exe')
        }, $true)
)
Assert-Equal 0 $directSshScpCommands.Count 'Office deploy has no bare ssh/scp command names; resolved absolute paths own every process start'
$validateOnlyBlockEnd = if ($deployValidationExitIndex -ge 0) {
    $deploySource.IndexOf('exit 0', $deployValidationExitIndex)
}
else { -1 }
$validateOnlyBlock = if ($deployValidationExitIndex -ge 0 -and $validateOnlyBlockEnd -gt $deployValidationExitIndex) {
    $deploySource.Substring($deployValidationExitIndex, $validateOnlyBlockEnd - $deployValidationExitIndex)
}
else { '' }
Assert-Equal $false $validateOnlyBlock.Contains('Resolve-OfficeSshClientIdentity') 'ValidateOnly does not resolve SSH client identity'
Assert-Equal $false $validateOnlyBlock.Contains('Invoke-OfficeSshEchoBehaviorProbe') 'ValidateOnly does not run the SSH echo probe'
Assert-Equal $false $validateOnlyBlock.Contains('Use-OfficeSshHelper.ps1') 'ValidateOnly does not load the managed SSH helper'
Assert-Equal $false $validateOnlyBlock.Contains('tailscale') 'ValidateOnly does not touch Tailscale'
$tailscalePingIndex = $deploySource.IndexOf('& tailscale.exe ping')
$firstRecordedScpIndex = $deploySource.IndexOf('Invoke-CountdownRecordedScpTransfer')
$identityResolveIndex = $deploySource.IndexOf('Resolve-OfficeSshClientIdentity', $credentialImportIndex)
$echoProbeIndex = $deploySource.IndexOf('Invoke-OfficeSshEchoBehaviorProbe', $credentialImportIndex)
Assert-Equal $true (
    $tailscalePingIndex -gt $credentialImportIndex -and
    $identityResolveIndex -gt $tailscalePingIndex -and
    $echoProbeIndex -gt $identityResolveIndex -and
    $firstRecordedScpIndex -gt $echoProbeIndex
) 'Formal deploy resolves SSH identity and runs the echo probe after Tailscale and before the first SCP transfer'
$adapterCalls = @(
    $deploymentAst.FindAll({
            param($node)
            $node -is [System.Management.Automation.Language.CommandAst] -and
            $node.GetCommandName() -eq 'New-CountdownRemoteDeploymentReadbackSshAdapter'
        }, $true)
)
Assert-Equal 1 $adapterCalls.Count 'Office deploy constructs one independent readback SSH adapter'
$adapterParameterNames = @(
    $adapterCalls[0].CommandElements |
        Where-Object { $_ -is [System.Management.Automation.Language.CommandParameterAst] } |
        ForEach-Object { [string] $_.ParameterName }
)
Assert-Equal $true ($adapterParameterNames -contains 'CommandRunner') 'Independent readback adapter uses the resolved SSH executable through CommandRunner'
Assert-Equal $true ($adapterParameterNames -contains 'RemoteRoot') 'Independent readback adapter takes the remote staging root for the uploaded script'
Assert-Equal $true ($adapterParameterNames -contains 'ScpExecutablePath') 'Independent readback adapter takes the resolved SCP executable path'
Assert-Equal $true ($adapterParameterNames -contains 'ScpOptions') 'Independent readback adapter keeps strict SCP options'
Assert-Equal $true ($adapterParameterNames -contains 'RemoteBytesProbe') 'Independent readback adapter probes remote bytes on failed uploads'

$remoteDeploymentModuleSource = [System.IO.File]::ReadAllText(
    (Join-Path $PSScriptRoot 'Countdown.RemoteDeployment.psm1'),
    [System.Text.Encoding]::UTF8)
$remoteDeploymentModuleAst = [System.Management.Automation.Language.Parser]::ParseInput(
    $remoteDeploymentModuleSource,
    [ref] $null,
    [ref] $null)
$moduleRecordedScpInvocations = @(
    $remoteDeploymentModuleAst.FindAll({
            param($node)
            if ($node -isnot [System.Management.Automation.Language.CommandAst]) { return $false }
            if ($node.CommandElements.Count -lt 1 -or
                $node.CommandElements[0] -isnot [System.Management.Automation.Language.VariableExpressionAst] -or
                [string] $node.CommandElements[0].VariablePath.UserPath -ne 'recordedScpTransferCommand') {
                return $false
            }
            $true
        }, $true)
)
Assert-Equal 1 $moduleRecordedScpInvocations.Count 'Remote deployment module uploads the readback script through the trusted recorded SCP transfer binding'
foreach ($moduleScpInvocation in $moduleRecordedScpInvocations) {
    $moduleScpInvocationParameterNames = @(
        $moduleScpInvocation.CommandElements |
            Where-Object { $_ -is [System.Management.Automation.Language.CommandParameterAst] } |
            ForEach-Object { [string] $_.ParameterName }
    )
    Assert-Equal $true ($moduleScpInvocationParameterNames -contains 'ScpExecutablePath') 'Remote deployment module passes the resolved SCP executable path explicitly'
    Assert-Equal $true ($moduleScpInvocationParameterNames -contains 'ScpOptions') 'Remote deployment module passes strict SCP options explicitly'
}
$moduleDirectSshScpCommands = @(
    $remoteDeploymentModuleAst.FindAll({
            param($node)
            if ($node -isnot [System.Management.Automation.Language.CommandAst]) { return $false }
            $commandName = $node.GetCommandName()
            $commandName -in @('ssh', 'ssh.exe', 'scp', 'scp.exe')
        }, $true)
)
Assert-Equal 0 $moduleDirectSshScpCommands.Count 'Remote deployment module has no bare ssh/scp command names'
$wrapperTemplateStartIndex = $remoteDeploymentModuleSource.IndexOf('$wrapperScript = @"')
$wrapperTemplateEndIndex = if ($wrapperTemplateStartIndex -ge 0) {
    $remoteDeploymentModuleSource.IndexOf('"@', $wrapperTemplateStartIndex + '$wrapperScript = @"'.Length)
}
else { -1 }
Assert-Equal $true ($wrapperTemplateStartIndex -ge 0 -and $wrapperTemplateEndIndex -gt $wrapperTemplateStartIndex) 'Remote deployment module keeps exactly one independent readback wrapper template'
$wrapperTemplateSource = if ($wrapperTemplateEndIndex -gt $wrapperTemplateStartIndex) {
    $remoteDeploymentModuleSource.Substring($wrapperTemplateStartIndex, $wrapperTemplateEndIndex - $wrapperTemplateStartIndex)
}
else { '' }
Assert-Equal $true ($wrapperTemplateSource.Contains('[System.IO.FileAttributes]::ReparsePoint') -and
    $wrapperTemplateSource.Contains('[System.IO.FileShare]::Read')) 'Independent readback wrapper opens the uploaded script as a non-reparse ordinary file with FileShare.Read before hashing'
Assert-Equal $true $wrapperTemplateSource.Contains('[System.StringComparison]::OrdinalIgnoreCase') 'Independent readback wrapper compares the script hash case-insensitively'
Assert-Equal $true $wrapperTemplateSource.Contains('if (`$LASTEXITCODE -ne 0) { exit `$LASTEXITCODE }') 'Independent readback wrapper propagates the reader child exit code'
$wrapperHashGateIndex = $wrapperTemplateSource.IndexOf('独立回读脚本哈希不匹配')
$wrapperExecuteIndex = $wrapperTemplateSource.IndexOf('& (Join-Path `$PSHOME ''pwsh.exe'')')
Assert-Equal $true ($wrapperHashGateIndex -ge 0 -and $wrapperExecuteIndex -gt $wrapperHashGateIndex) 'Independent readback wrapper rechecks the hash before executing the uploaded script'

$officeSshProbeOptions = @(
    '-i', 'D:\fixture\id_ed25519',
    '-o', 'BatchMode=yes',
    '-o', 'IdentitiesOnly=yes',
    '-o', 'ConnectTimeout=10',
    '-o', 'ConnectionAttempts=1',
    '-o', 'StrictHostKeyChecking=yes',
    '-o', 'UserKnownHostsFile=D:\fixture\known_hosts',
    '-o', 'KexAlgorithms=curve25519-sha256'
)
. (Join-Path $PSScriptRoot 'Deploy-OfficeCountdown.ps1') -LoadFunctionsOnly
$truncatedEcho = 'truncated-echo'
$officeSshProbeTruncationLocked = $false
$officeSshProbeTruncationMessage = 'truncated SSH echo probe was accepted'
try {
    $truncationRunner = {
        param($FileName, $Arguments)
        [pscustomobject]@{
            ExitCode = 0
            StandardOutput = $truncatedEcho
            StandardError = ''
            ProcessId = 4242
        }
    }.GetNewClosure()
    Invoke-OfficeSshEchoBehaviorProbe `
        -SshExecutablePath 'D:\OpenSSH\ssh.exe' `
        -SshVersion 'OpenSSH_for_Windows_9.5p2, LibreSSL 3.8.2' `
        -Options $officeSshProbeOptions `
        -Destination 'replicator@office-host' `
        -ProbeCharacterCount 22644 `
        -CommandRunner $truncationRunner
}
catch {
    $officeSshProbeTruncationLocked = $true
    $officeSshProbeTruncationMessage = $_.Exception.Message
}
Assert-Equal $true $officeSshProbeTruncationLocked 'Truncated SSH echo probe fails closed'
Assert-Equal $true ($officeSshProbeTruncationMessage -match [regex]::Escape('D:\OpenSSH\ssh.exe')) 'Truncated SSH echo probe error includes resolved binary path'
Assert-Equal $true ($officeSshProbeTruncationMessage -match [regex]::Escape('OpenSSH_for_Windows_9.5p2, LibreSSL 3.8.2')) 'Truncated SSH echo probe error includes SSH version'
Assert-Equal $true ($officeSshProbeTruncationMessage -match '实测截断长度：14') 'Truncated SSH echo probe error includes measured truncated length'

$officeSshProbeCompleteCapture = [pscustomobject]@{ FileName = $null; RemoteCommandLength = 0 }
$officeSshProbeSpec = New-OfficeSshEchoProbeCommand -ProbeCharacterCount 22644
$officeSshProbeCompleteMessage = $null
try {
    $completeRunner = {
        param($FileName, $Arguments)
        $officeSshProbeCompleteCapture.FileName = $FileName
        $officeSshProbeCompleteCapture.RemoteCommandLength = [int] $Arguments[-1].Length
        [pscustomobject]@{
            ExitCode = 0
            StandardOutput = [string] $officeSshProbeSpec.ExpectedOutput
            StandardError = ''
            ProcessId = 4242
        }
    }.GetNewClosure()
    Invoke-OfficeSshEchoBehaviorProbe `
        -SshExecutablePath 'D:\OpenSSH\ssh.exe' `
        -SshVersion 'OpenSSH_for_Windows_9.5p2, LibreSSL 3.8.2' `
        -Options $officeSshProbeOptions `
        -Destination 'replicator@office-host' `
        -ProbeCharacterCount 22644 `
        -CommandRunner $completeRunner
}
catch {
    $officeSshProbeCompleteMessage = $_.Exception.Message
}
Assert-Equal $null $officeSshProbeCompleteMessage 'Complete SSH echo probe round-trip is accepted'
Assert-Equal 'D:\OpenSSH\ssh.exe' ([string] $officeSshProbeCompleteCapture.FileName) 'SSH echo probe starts the resolved ssh executable'
Assert-Equal 22644 ([int] $officeSshProbeCompleteCapture.RemoteCommandLength) 'SSH echo probe remote command covers the 22644-character readback floor'

$officeSshNoCandidateMessage = $null
try {
    Resolve-OfficeSshClientIdentity `
        -SystemRoot (Join-Path ([System.IO.Path]::GetTempPath()) 'countdown-missing-openssh') `
        -ApplicationCommandLookup { param($Name) @() } `
        -VersionReader { param($SshPath) '<none>' } | Out-Null
}
catch {
    $officeSshNoCandidateMessage = $_.Exception.Message
}
Assert-Equal $true ($officeSshNoCandidateMessage -match '未找到可配对的 ssh/scp 候选') 'Missing ssh/scp pair fails closed'
Assert-Equal $true ($officeSshNoCandidateMessage -match '二进制路径：<none>') 'Missing ssh/scp pair error includes binary path field'
Assert-Equal $true ($officeSshNoCandidateMessage -match '版本：<none>') 'Missing ssh/scp pair error includes version field'
Assert-Equal $true ($officeSshNoCandidateMessage -match '实测截断长度：n/a') 'Missing ssh/scp pair error includes measured truncation field'

$officeSshIdentityFixtureRoot = Join-Path ([System.IO.Path]::GetTempPath()) "countdown-ssh-identity-$([guid]::NewGuid().ToString('N'))"
$officeSshOpenSshDirectory = Join-Path $officeSshIdentityFixtureRoot 'System32\OpenSSH'
New-Item -ItemType Directory -Path $officeSshOpenSshDirectory -Force | Out-Null
[System.IO.File]::WriteAllText((Join-Path $officeSshOpenSshDirectory 'ssh.exe'), 'ssh-fixture')
[System.IO.File]::WriteAllText((Join-Path $officeSshOpenSshDirectory 'scp.exe'), 'scp-fixture')
$pathSsh = Join-Path $officeSshIdentityFixtureRoot 'path-ssh.exe'
$pathScp = Join-Path $officeSshIdentityFixtureRoot 'path-scp.exe'
[System.IO.File]::WriteAllText($pathSsh, 'path-ssh')
[System.IO.File]::WriteAllText($pathScp, 'path-scp')
$officeSshIdentity = Resolve-OfficeSshClientIdentity `
    -SystemRoot $officeSshIdentityFixtureRoot `
    -ApplicationCommandLookup {
        param($Name)
        if ($Name -eq 'ssh.exe') {
            [pscustomobject]@{ Source = $pathSsh }
        }
        elseif ($Name -eq 'scp.exe') {
            [pscustomobject]@{ Source = $pathScp }
        }
        else { @() }
    } `
    -VersionReader { param($SshPath) 'OpenSSH_fixture' }
Assert-Equal ([System.IO.Path]::GetFullPath((Join-Path $officeSshOpenSshDirectory 'ssh.exe'))) ([string] $officeSshIdentity.SshPath) 'Windows OpenSSH directory pair is preferred over PATH'
Assert-Equal ([System.IO.Path]::GetFullPath((Join-Path $officeSshOpenSshDirectory 'scp.exe'))) ([string] $officeSshIdentity.ScpPath) 'Windows OpenSSH directory keeps ssh/scp paired'
Assert-Equal 'windows-openssh' ([string] $officeSshIdentity.Source) 'Windows OpenSSH candidate records its source'
Assert-Equal 'OpenSSH_fixture' ([string] $officeSshIdentity.Version) 'SSH identity uses the injected version reader'
Remove-Item -LiteralPath (Join-Path $officeSshOpenSshDirectory 'ssh.exe') -Force
Remove-Item -LiteralPath (Join-Path $officeSshOpenSshDirectory 'scp.exe') -Force
Remove-Item -LiteralPath $officeSshOpenSshDirectory -Force
Remove-Item -LiteralPath (Join-Path $officeSshIdentityFixtureRoot 'System32') -Force
$officeSshPathIdentity = Resolve-OfficeSshClientIdentity `
    -SystemRoot $officeSshIdentityFixtureRoot `
    -ApplicationCommandLookup {
        param($Name)
        if ($Name -eq 'ssh.exe') {
            [pscustomobject]@{ Source = $pathSsh }
        }
        elseif ($Name -eq 'scp.exe') {
            [pscustomobject]@{ Source = $pathScp }
        }
        else { @() }
    } `
    -VersionReader { param($SshPath) 'PATH_fixture' }
Assert-Equal ([System.IO.Path]::GetFullPath($pathSsh)) ([string] $officeSshPathIdentity.SshPath) 'PATH ssh/scp pair is used when OpenSSH is incomplete'
Assert-Equal ([System.IO.Path]::GetFullPath($pathScp)) ([string] $officeSshPathIdentity.ScpPath) 'PATH candidate keeps ssh/scp paired'
Assert-Equal 'path' ([string] $officeSshPathIdentity.Source) 'PATH candidate records its source'
Remove-Item -LiteralPath $pathSsh -Force
Remove-Item -LiteralPath $pathScp -Force
Remove-Item -LiteralPath $officeSshIdentityFixtureRoot -Force

$officeSshStartabilityDestinations = [System.Collections.Generic.List[string]]::new()
$officeDestinationFixture = 'replicator@office-host'
$officeSshProbeSize = Resolve-OfficeSshEchoProbeCharacterCount `
    -SshExecutablePath 'D:\OpenSSH\ssh.exe' `
    -SshVersion 'OpenSSH_fixture' `
    -Options $officeSshProbeOptions `
    -DestinationLength $officeDestinationFixture.Length `
    -StartabilityProbe {
        param($FileName, $Arguments)
        $destinationArgument = [string] $Arguments[-2]
        [void] $officeSshStartabilityDestinations.Add($destinationArgument)
        ([string] $Arguments[-1]).Length -le 30000
    }
Assert-Equal $true ($officeSshProbeSize -ge 22644) 'Probe size never silently shrinks below the 22644-character readback floor'
Assert-Equal $true ($officeSshProbeSize -le 30000) 'Probe size uses the largest ArgumentList that can start'
Assert-Equal $true ($officeSshStartabilityDestinations.Count -gt 0) 'Size discovery StartabilityProbe actually runs'
Assert-Equal 0 @($officeSshStartabilityDestinations | Where-Object { $_ -eq $officeDestinationFixture }).Count 'Size discovery StartabilityProbe does not receive the office destination'
Assert-Equal $officeDestinationFixture.Length $officeSshStartabilityDestinations[0].Length 'Size discovery dummy destination preserves office destination length'
$resolveProbeCalls = @(
    $deploymentAst.FindAll({
            param($node)
            $node -is [System.Management.Automation.Language.CommandAst] -and
            $node.GetCommandName() -eq 'Resolve-OfficeSshEchoProbeCharacterCount'
        }, $true)
)
Assert-Equal 1 $resolveProbeCalls.Count 'Office deploy resolves echo probe size once'
$resolveProbeParameterNames = @(
    $resolveProbeCalls[0].CommandElements |
        Where-Object { $_ -is [System.Management.Automation.Language.CommandParameterAst] } |
        ForEach-Object { [string] $_.ParameterName }
)
Assert-Equal $true ($resolveProbeParameterNames -contains 'DestinationLength') 'Probe size discovery takes destination length, not the office destination string'
Assert-Equal $false ($resolveProbeParameterNames -contains 'Destination') 'Probe size discovery does not take the office destination for Start'
Assert-Equal $true $deploySource.Contains('ssh-client-identity.json') 'Resolved SSH identity is written to a run-evidence sidecar'
Set-StrictMode -Off
Assert-Equal 0 @($releaseContract.PackageFiles | Where-Object { $_ -like 'assets\ui-references\*' }).Count 'UI references stay outside the release package'
Assert-Equal 0 @($releaseContract.InstalledFiles | Where-Object { $_ -like 'assets\ui-references\*' }).Count 'UI references stay outside installed runtime'
Assert-Equal 0 @($releaseContract.BuildAssets | Where-Object { $_.PublishedPath -like '*ui-references*' }).Count 'UI references stay outside build targets'

$mutableReleaseContract = Get-CountdownReleaseContract
$mutableReleaseContract.PackageFiles[0] = 'mutated-package-file'
$mutableReleaseContract.BuildAssets[0].SourcePath = 'mutated-build-source'
$mutableReleaseContract.IntegrityHashes['Build-CountdownApplication.ps1'] = ('0' * 64)
$freshReleaseContract = Get-CountdownReleaseContract
Assert-Equal $false ('mutated-package-file' -in @($freshReleaseContract.PackageFiles)) 'Release contract returns a fresh package list'
Assert-Equal $false ('mutated-build-source' -in @($freshReleaseContract.BuildAssets.SourcePath)) 'Release contract returns fresh build asset records'
Assert-Equal $false (($freshReleaseContract.IntegrityHashes['Build-CountdownApplication.ps1']) -eq ('0' * 64)) 'Release contract returns a fresh integrity map'

$releaseContractTestRoot = Join-Path $PSScriptRoot ("release-contract-test-" + [guid]::NewGuid().ToString('N'))
try {
    New-Item -ItemType Directory -Path $releaseContractTestRoot -Force | Out-Null
    $missingPackageRejected = $false
    try {
        Assert-CountdownReleasePackage -Root $releaseContractTestRoot | Out-Null
    }
    catch {
        $missingPackageRejected = $_.Exception.Message -match '缺少发布包文件'
    }
    Assert-Equal $true $missingPackageRejected 'Release package assertion rejects missing files'

    foreach ($relativePath in $releaseContract.PackageFiles) {
        $sourcePath = Join-Path $PSScriptRoot $relativePath
        $testPath = Join-Path $releaseContractTestRoot $relativePath
        $testParent = Split-Path -Parent $testPath
        if (-not (Test-Path -LiteralPath $testParent -PathType Container)) {
            New-Item -ItemType Directory -Path $testParent -Force | Out-Null
        }
        Copy-Item -LiteralPath $sourcePath -Destination $testPath -Force
    }
    $isolatedPackageFiles = @(
        Get-ChildItem -LiteralPath $releaseContractTestRoot -File -Recurse |
            ForEach-Object { $_.FullName.Substring($releaseContractTestRoot.Length + 1) }
    )
    Assert-Equal 0 @($isolatedPackageFiles | Where-Object { $_ -match '(^|\\)(bin|obj)(\\|$)' }).Count 'Isolated package starts without bin or obj outputs'
    Assert-Equal `
        ((@($releaseContract.PackageFiles) | Sort-Object) -join ',') `
        (($isolatedPackageFiles | Sort-Object) -join ',') `
        'Isolated package contains exactly the deployment ZIP allowlist'
    $isolatedPackageValidation = Assert-CountdownReleasePackage -Root $releaseContractTestRoot
    Assert-Equal $true ([bool] $isolatedPackageValidation.Valid) 'Isolated deployment package passes the release assertion'

    $managedDotnet = if ($isCIEnvironment) {
        (Get-Command dotnet -ErrorAction Stop).Path
    } else {
        'D:\Codex\tools\dotnet\10.0\dotnet.exe'
    }
    $isolatedApplicationProject = Join-Path $releaseContractTestRoot 'src\MinminCountdown\MinminCountdown.csproj'
    $evaluatedSourceBuildFiles = [System.Collections.Generic.List[string]]::new()
    [void] $evaluatedSourceBuildFiles.Add('global.json')
    $projectQueue = [System.Collections.Generic.Queue[string]]::new()
    $projectQueue.Enqueue($isolatedApplicationProject)
    $evaluatedProjects = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    while ($projectQueue.Count -gt 0) {
        $projectPath = [System.IO.Path]::GetFullPath($projectQueue.Dequeue())
        if (-not $evaluatedProjects.Add($projectPath)) {
            continue
        }
        [void] $evaluatedSourceBuildFiles.Add($projectPath.Substring($releaseContractTestRoot.Length + 1))
        $evaluatedJson = & $managedDotnet msbuild $projectPath '-getItem:Compile,ApplicationDefinition,Page,Content,ProjectReference' '-getProperty:ApplicationIcon' | Out-String
        if ($LASTEXITCODE -ne 0) {
            throw "MSBuild source-closure evaluation failed: $projectPath"
        }
        $evaluatedProject = $evaluatedJson | ConvertFrom-Json
        foreach ($itemType in @('Compile', 'ApplicationDefinition', 'Page', 'Content')) {
            $itemProperty = $evaluatedProject.Items.PSObject.Properties[$itemType]
            if ($null -eq $itemProperty) {
                continue
            }
            foreach ($item in @($itemProperty.Value)) {
                $fullPath = [System.IO.Path]::GetFullPath([string] $item.FullPath)
                if ($fullPath.StartsWith($releaseContractTestRoot + '\', [System.StringComparison]::OrdinalIgnoreCase) -and
                    $fullPath -notmatch '[\\/](bin|obj)[\\/]') {
                    [void] $evaluatedSourceBuildFiles.Add($fullPath.Substring($releaseContractTestRoot.Length + 1))
                }
            }
        }
        $projectReferenceProperty = $evaluatedProject.Items.PSObject.Properties['ProjectReference']
        foreach ($projectReference in @(
                if ($null -ne $projectReferenceProperty) { $projectReferenceProperty.Value }
            )) {
            $referencedProjectPath = [System.IO.Path]::GetFullPath([string] $projectReference.FullPath)
            if ($referencedProjectPath.StartsWith($releaseContractTestRoot + '\', [System.StringComparison]::OrdinalIgnoreCase)) {
                $projectQueue.Enqueue($referencedProjectPath)
            }
        }
        $applicationIcon = [string] $evaluatedProject.Properties.ApplicationIcon
        if (-not [string]::IsNullOrWhiteSpace($applicationIcon)) {
            $applicationIconPath = [System.IO.Path]::GetFullPath((Join-Path (Split-Path -Parent $projectPath) $applicationIcon))
            if ($applicationIconPath.StartsWith($releaseContractTestRoot + '\', [System.StringComparison]::OrdinalIgnoreCase)) {
                [void] $evaluatedSourceBuildFiles.Add($applicationIconPath.Substring($releaseContractTestRoot.Length + 1))
            }
        }
    }
    Assert-Equal `
        ((@($releaseContract.SourceBuildFiles) | Sort-Object) -join ',') `
        ((@($evaluatedSourceBuildFiles) | Sort-Object -Unique) -join ',') `
        'Release contract matches the MSBuild-evaluated project, Compile, WPF XAML, icon and Content closure'

    $isolatedSourceDll = Join-Path $releaseContractTestRoot 'src\MinminCountdown\bin\Release\net10.0-windows\MinminCountdown.dll'
    [System.IO.File]::WriteAllText(
        (Join-Path $releaseContractTestRoot 'Countdown.Core.Tests.ps1'),
        "if (Test-Path -LiteralPath '$($isolatedSourceDll.Replace("'", "''"))' -PathType Leaf) { throw 'Source DLL existed before the validation build.' }`r`n'{`"Passed`":1,`"Failed`":0}'",
        [System.Text.UTF8Encoding]::new($false))
    [System.IO.File]::WriteAllText(
        (Join-Path $releaseContractTestRoot 'Countdown.CSharp.Tests.ps1'),
        "if (-not (Test-Path -LiteralPath '$($isolatedSourceDll.Replace("'", "''"))' -PathType Leaf)) { throw 'Source DLL was not built before C# regression.' }`r`n'{`"Passed`":1,`"Failed`":0,`"AdapterUiValidated`":true}'",
        [System.Text.UTF8Encoding]::new($false))
    [System.IO.File]::WriteAllText(
        (Join-Path $releaseContractTestRoot 'Countdown.Ui.Tests.ps1'),
        "'{`"Passed`":1,`"Failed`":0}'",
        [System.Text.UTF8Encoding]::new($false))
    $isolatedValidation = Invoke-CountdownValidationSuite `
        -Root $releaseContractTestRoot `
        -PowerShell7Path (Get-Command pwsh.exe -CommandType Application -ErrorAction Stop | Select-Object -First 1 -ExpandProperty Source) `
        -DotnetPath $managedDotnet
    Assert-Equal 1 ([int] $isolatedValidation.CoreTestsPassed) 'Isolated validation runs the pre-build Core fixture'
    Assert-Equal 1 ([int] $isolatedValidation.CSharpTestsPassed) 'Isolated validation runs C# regression after the real source build'
    Assert-Equal 1 ([int] $isolatedValidation.UiTestsPassed) 'Isolated validation runs the narrow UI fixture'
    Assert-Equal $true ([bool] $isolatedValidation.AdapterUiValidated) 'Isolated validation preserves adapter UI evidence'
    Assert-Equal $true `
        (Test-Path -LiteralPath $isolatedSourceDll -PathType Leaf) `
        'Isolated source build produces the DLL required by C# regression'

    foreach ($fixturePath in @('Countdown.Core.Tests.ps1', 'Countdown.CSharp.Tests.ps1', 'Countdown.Ui.Tests.ps1')) {
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot $fixturePath) -Destination (Join-Path $releaseContractTestRoot $fixturePath) -Force
    }
    $isolatedCoreProject = Join-Path $releaseContractTestRoot 'src\MinminCountdown.Core\MinminCountdown.Core.csproj'
    Remove-Item -LiteralPath $isolatedCoreProject -Force
    $missingSourceBuildInputRejected = $false
    try {
        Assert-CountdownReleasePackage -Root $releaseContractTestRoot | Out-Null
    }
    catch {
        $missingSourceBuildInputRejected = $_.Exception.Message -match '缺少发布包文件：src\\MinminCountdown\.Core\\MinminCountdown\.Core\.csproj'
    }
    Assert-Equal $true $missingSourceBuildInputRejected 'Release assertion fails closed when a required project-graph input is missing'
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'src\MinminCountdown.Core\MinminCountdown.Core.csproj') -Destination $isolatedCoreProject -Force

    foreach ($relativePath in $releaseContract.PackageFiles) {
        $testPath = Join-Path $releaseContractTestRoot $relativePath
        $testParent = Split-Path -Parent $testPath
        if (-not (Test-Path -LiteralPath $testParent -PathType Container)) {
            New-Item -ItemType Directory -Path $testParent -Force | Out-Null
        }
        [System.IO.File]::WriteAllText($testPath, 'invalid release content', [System.Text.UTF8Encoding]::new($false))
    }
    $invalidHashRejected = $false
    try {
        Assert-CountdownReleasePackage -Root $releaseContractTestRoot | Out-Null
    }
    catch {
        $invalidHashRejected = $_.Exception.Message -match '发布包哈希不匹配'
    }
    Assert-Equal $true $invalidHashRejected 'Release package assertion rejects invalid content'
}
finally {
    Remove-CountdownReleaseContractTestTree -Path $releaseContractTestRoot
}

if ($ReleaseContractOnly) {
    if ($failures.Count -gt 0) {
        $failures | ForEach-Object { Write-Error $_ }
        exit 1
    }
    [pscustomobject]@{ Passed = $script:assertions; Failed = 0; Scope = 'ReleaseContract' } | ConvertTo-Json
    exit 0
}
}

$acceptanceTestRoot = Join-Path $PSScriptRoot ("acceptance-test-" + [guid]::NewGuid().ToString('N'))
$acceptanceStartupRequestPath = Join-Path $acceptanceTestRoot 'startup-request.json'
$acceptanceRuntimeStatusPath = Join-Path $acceptanceTestRoot 'runtime-status.json'
$acceptanceStatusPath = Join-Path $acceptanceTestRoot 'acceptance-status.json'
$acceptanceSettingsSnapshotPath = Join-Path $acceptanceTestRoot 'settings-snapshot.json'
$acceptanceSettingsPath = Join-Path $acceptanceTestRoot 'settings.json'
$acceptanceApplicationPath = Join-Path $acceptanceTestRoot '敏敏子倒计时.ps1'
$acceptanceExpectedContext = [pscustomobject]@{
    User = 'MINMIN\AcceptanceUser'
    SessionId = 7
    Desktop = 'D:\AcceptanceDesktop'
}
$acceptanceAdapterState = [pscustomobject]@{ Completed = 0; Removed = 0 }
$acceptanceTaskState = 'Ready'
$acceptanceLastTaskResult = 0
$acceptanceTaskIdentity = 'scheduled-task-v1'
$acceptanceJobs = [System.Collections.ArrayList]::new()
function Reset-CountdownAcceptanceTestCase {
    param([int] $RuntimeProcessId = 4242)

    Remove-Item -LiteralPath $acceptanceStatusPath -Force -ErrorAction SilentlyContinue
    Copy-Item -LiteralPath $acceptanceSettingsSnapshotPath -Destination $acceptanceSettingsPath -Force
    $acceptanceAdapterState.Completed = 0
    $acceptanceAdapterState.Removed = 0
    $script:acceptanceTaskState = 'Ready'
    $script:acceptanceLastTaskResult = 0
    $script:acceptanceTaskIdentity = 'scheduled-task-v1'
    $request = New-CountdownStartupRequest -Path $acceptanceStartupRequestPath -AcceptanceReadOnly
    [void] (Write-CountdownWindowReadyReceipt -Path $acceptanceRuntimeStatusPath -LaunchToken $request.launchToken -ProcessId $RuntimeProcessId -SessionId 7 -ApplicationPath $acceptanceApplicationPath -CalendarState Ready)
    $request
}

function Start-CountdownAcceptanceTestApplication {
    param(
        [Parameter(Mandatory)] [object] $StartupRequest,
        [switch] $ChangeSettings,
        [AllowNull()] [string] $SettingsJsonAfterRelease,
        [switch] $SkipFinalizedReady
    )

    $job = Start-Job -ArgumentList $acceptanceStartupRequestPath, $acceptanceStatusPath, $StartupRequest.launchToken, $acceptanceApplicationPath, $acceptanceSettingsPath, ([bool] $ChangeSettings), $SettingsJsonAfterRelease, ([bool] $SkipFinalizedReady) -ScriptBlock {
        param($StartupRequestPath, $AcceptanceStatusPath, $LaunchToken, $ApplicationPath, $SettingsPath, $ChangeSettings, $SettingsJsonAfterRelease, $SkipFinalizedReady)
        $deadline = [DateTimeOffset]::UtcNow.AddSeconds(10)
        do {
            try {
                $request = [System.IO.File]::ReadAllText($StartupRequestPath, [System.Text.Encoding]::UTF8) | ConvertFrom-Json -DateKind String
                if ([string] $request.acceptanceState -eq 'released') {
                    if ($ChangeSettings) {
                        [System.IO.File]::WriteAllText($SettingsPath, '{"schemaVersion":9,"unexpected":true}', [System.Text.UTF8Encoding]::new($false))
                    }
                    elseif (-not [string]::IsNullOrWhiteSpace($SettingsJsonAfterRelease)) {
                        [System.IO.File]::WriteAllText($SettingsPath, $SettingsJsonAfterRelease, [System.Text.UTF8Encoding]::new($false))
                    }
                    $request.acceptanceState = 'ready'
                    [System.IO.File]::WriteAllText($StartupRequestPath, ($request | ConvertTo-Json -Depth 8), [System.Text.UTF8Encoding]::new($false))
                    break
                }
            }
            catch {}
            Start-Sleep -Milliseconds 20
        } while ([DateTimeOffset]::UtcNow -lt $deadline)
        if ($SkipFinalizedReady) { return }

        do {
            try {
                $request = [System.IO.File]::ReadAllText($StartupRequestPath, [System.Text.Encoding]::UTF8) | ConvertFrom-Json -DateKind String
                if ([string] $request.acceptanceState -eq 'finalized') {
                    $receipt = [ordered]@{
                        schemaVersion = 1
                        stage = 'finalized-ready'
                        launchToken = $LaunchToken
                        processId = 4242
                        sessionId = 7
                        applicationPath = $ApplicationPath
                        readyAtUtc = [DateTimeOffset]::UtcNow.ToString('o')
                    }
                    [System.IO.File]::WriteAllText($AcceptanceStatusPath, ($receipt | ConvertTo-Json -Compress), [System.Text.UTF8Encoding]::new($false))
                    return
                }
            }
            catch {}
            Start-Sleep -Milliseconds 20
        } while ([DateTimeOffset]::UtcNow -lt $deadline)
        throw 'Acceptance test application did not observe the full release/finalize protocol.'
    }
    [void] $acceptanceJobs.Add($job)
    $job
}

function Invoke-CountdownAcceptanceTestCase {
    param(
        [Parameter(Mandatory)] [object] $StartupRequest,
        [int] $ExpectedProcessId = 4242,
        [int] $ReadyTimeoutSeconds = 5
    )

    Invoke-CountdownTransactionAcceptance `
        -StartupRequestPath $acceptanceStartupRequestPath `
        -RuntimeStatusPath $acceptanceRuntimeStatusPath `
        -AcceptanceStatusPath $acceptanceStatusPath `
        -LaunchToken $StartupRequest.launchToken `
        -ExpectedInteractiveContext $acceptanceExpectedContext `
        -ExpectedProcessId $ExpectedProcessId `
        -ExpectedSessionId 7 `
        -ExpectedApplicationPath $acceptanceApplicationPath `
        -TaskName 'Minmin Countdown Acceptance Test' `
        -ExpectedScheduledTaskIdentity 'scheduled-task-v1' `
        -SettingsSnapshotPath $acceptanceSettingsSnapshotPath `
        -SettingsPath $acceptanceSettingsPath `
        -GetInteractiveContext { $acceptanceExpectedContext } `
        -GetScheduledTask {
            param([string] $TaskName)
            $reportedTaskState = $acceptanceTaskState
            if ($reportedTaskState -eq 'StopAfterReady') {
                $startupState = [string] ((Get-Content -LiteralPath $acceptanceStartupRequestPath -Raw -Encoding UTF8 | ConvertFrom-Json).acceptanceState)
                $reportedTaskState = if ($startupState -in @('ready', 'finalized')) { 'Disabled' } else { 'Ready' }
            }
            $reportedTaskIdentity = $acceptanceTaskIdentity
            if ($reportedTaskIdentity -eq 'DriftAfterReady') {
                $startupState = [string] ((Get-Content -LiteralPath $acceptanceStartupRequestPath -Raw -Encoding UTF8 | ConvertFrom-Json).acceptanceState)
                $reportedTaskIdentity = if ($startupState -in @('ready', 'finalized')) { 'scheduled-task-v2' } else { 'scheduled-task-v1' }
            }
            [pscustomobject]@{ TaskName = $TaskName; State = $reportedTaskState; Identity = $reportedTaskIdentity }
        } `
        -GetScheduledTaskIdentity {
            param([object] $ScheduledTask)
            [string] $ScheduledTask.Identity
        } `
        -GetScheduledTaskInfo {
            param([string] $TaskName)
            [pscustomobject]@{ LastTaskResult = $acceptanceLastTaskResult }
        } `
        -CompleteTransaction {
            $acceptanceAdapterState.Completed++
            [pscustomobject]@{ Internal = 'accepted-journal' }
        } `
        -RemoveCommittedTransaction {
            $acceptanceAdapterState.Removed++
            [pscustomobject]@{ Internal = 'cleanup-result' }
        } `
        -ProcessProbe {
            param([int] $ProcessId)
            [pscustomobject]@{
                ProcessId = $ProcessId
                SessionId = 7
                ExecutablePath = Join-Path $acceptanceTestRoot 'app\MinminCountdown.exe'
                CommandLine = '"MinminCountdown.exe"'
            }
        } `
        -ReadyTimeoutSeconds $ReadyTimeoutSeconds
}

try {
    New-Item -ItemType Directory -Path $acceptanceTestRoot -Force | Out-Null
    [System.IO.File]::WriteAllText($acceptanceApplicationPath, '# acceptance application', [System.Text.UTF8Encoding]::new($false))
    [System.IO.File]::WriteAllText($acceptanceSettingsSnapshotPath, '{"schemaVersion":9}', [System.Text.UTF8Encoding]::new($false))
    $acceptanceStartupRequest = Reset-CountdownAcceptanceTestCase
    $acceptanceReadyJob = Start-CountdownAcceptanceTestApplication -StartupRequest $acceptanceStartupRequest
    $acceptanceResult = @(Invoke-CountdownAcceptanceTestCase -StartupRequest $acceptanceStartupRequest)

    Assert-Equal 1 $acceptanceResult.Count 'Transaction acceptance returns one stable result object'
    Assert-Equal 'finalized-ready' ([string] $acceptanceResult[0].AcceptanceState) 'Transaction acceptance returns finalized-ready only after application unlock confirmation'
    Assert-Equal 4242 ([int] $acceptanceResult[0].RuntimeProcessId) 'Transaction acceptance retains the original managed process identity'
    Assert-Equal 'Ready' ([string] $acceptanceResult[0].TaskState) 'Transaction acceptance reports Ready from the scheduled task'
    Assert-Equal 0 ([int] $acceptanceResult[0].LastTaskResult) 'Transaction acceptance reports LastTaskResult from GetScheduledTaskInfo'
    Assert-Equal $true ([bool] $acceptanceResult[0].SettingsUnchanged) 'Identical live settings fingerprints compute SettingsUnchanged=true'
    Assert-Equal ([string] $acceptanceResult[0].SettingsHashBefore) ([string] $acceptanceResult[0].SettingsHashAfter) 'Unchanged settings keep the same SHA-256'
    Assert-Equal ([long] $acceptanceResult[0].SettingsLengthBefore) ([long] $acceptanceResult[0].SettingsLengthAfter) 'Unchanged settings keep the same length'
    Assert-Equal ([string] $acceptanceResult[0].SettingsMtimeUtcBefore) ([string] $acceptanceResult[0].SettingsMtimeUtcAfter) 'Unchanged settings keep the same LastWriteTimeUtc'
    Assert-Equal 1 $acceptanceAdapterState.Completed 'Transaction acceptance commits the transaction once'
    Assert-Equal 1 $acceptanceAdapterState.Removed 'Transaction acceptance removes rollback evidence only after finalized-ready'
    Assert-Equal 'finalized' ([string] ((Get-Content -LiteralPath $acceptanceStartupRequestPath -Raw -Encoding UTF8 | ConvertFrom-Json).acceptanceState)) 'Transaction acceptance leaves the startup request finalized'
    Wait-Job -Job $acceptanceReadyJob -Timeout 5 | Out-Null
    Assert-Equal 'Completed' ([string] $acceptanceReadyJob.State) 'Acceptance test application observed release before finalize'

    $acceptanceMigrationUtc = [DateTimeOffset] '2026-07-20T02:16:00Z'
    $acceptanceLegacyRepeat = [pscustomobject]@{
        schemaVersion = 9
        timeZoneId = 'China Standard Time'
        displayPrecision = 'Minute'
        monthlySalary = 0
        workStart = '09:00'
        workEnd = '18:00'
        themePreference = 'Auto'
        tags = @()
        countdowns = @()
        notes = @([pscustomobject]@{
                id = 'acceptance-repeat-note'
                text = '验收重复提醒'
                date = $null
                isCompleted = $false
                tagIds = @()
                reminder = [pscustomobject]@{
                    type = 'repeat'
                    firstDueUtc = '2026-07-20T01:00:00.0000000+00:00'
                    intervalMinutes = 1
                }
            })
    }
    $acceptanceLegacyJson = $acceptanceLegacyRepeat | ConvertTo-Json -Depth 8
    $acceptanceMigratedJson = ConvertTo-CountdownSettingsJson -State (
        ConvertFrom-CountdownSettingsObject -Settings $acceptanceLegacyRepeat -MigrationUtc $acceptanceMigrationUtc)
    $acceptanceMigrated = $acceptanceMigratedJson | ConvertFrom-Json
    $acceptanceRuleId = [string] $acceptanceMigrated.notes[0].reminderRules[0].id
    $acceptanceOccurrenceKey = "{0}|{1}" -f $acceptanceRuleId, $acceptanceMigrationUtc.ToString('o')
    $cursorBeyondFrozen = $acceptanceMigratedJson | ConvertFrom-Json
    $cursorBeyondFrozen.reminderStates.PSObject.Properties[$acceptanceRuleId].Value.handledThroughUtc = $acceptanceMigrationUtc.AddMinutes(1).ToString('o')
    Assert-Equal $false (Test-CountdownSettingsDeploymentTransition `
            -BeforeHash 'legacy-repeat' `
            -AfterHash 'cursor-beyond-frozen' `
            -BeforeJson $acceptanceLegacyJson `
            -AfterJson ($cursorBeyondFrozen | ConvertTo-Json -Depth 12) `
            -MigrationUtc $acceptanceMigrationUtc) 'Deployment transition rejects a reminder cursor beyond the frozen migration occurrence'
    $forgedDelivery = $acceptanceMigratedJson | ConvertFrom-Json
    $forgedDelivery.reminderStates.PSObject.Properties[$acceptanceRuleId].Value.lastDeliveredOccurrenceKey = $acceptanceOccurrenceKey
    Assert-Equal $false (Test-CountdownSettingsDeploymentTransition `
            -BeforeHash 'legacy-repeat' `
            -AfterHash 'forged-delivery' `
            -BeforeJson $acceptanceLegacyJson `
            -AfterJson ($forgedDelivery | ConvertTo-Json -Depth 12) `
            -MigrationUtc $acceptanceMigrationUtc) 'Deployment transition rejects a forged reminder delivery'
    $forgedDisposition = $acceptanceMigratedJson | ConvertFrom-Json
    $forgedDispositionState = $forgedDisposition.reminderStates.PSObject.Properties[$acceptanceRuleId].Value
    $forgedDispositionState.lastDeliveredOccurrenceKey = $acceptanceOccurrenceKey
    $forgedDispositionState.lastDispositionOccurrenceKey = $acceptanceOccurrenceKey
    Assert-Equal $false (Test-CountdownSettingsDeploymentTransition `
            -BeforeHash 'legacy-repeat' `
            -AfterHash 'forged-disposition' `
            -BeforeJson $acceptanceLegacyJson `
            -AfterJson ($forgedDisposition | ConvertTo-Json -Depth 12) `
            -MigrationUtc $acceptanceMigrationUtc) 'Deployment transition rejects a forged reminder disposition'
    [System.IO.File]::WriteAllText($acceptanceSettingsSnapshotPath, $acceptanceLegacyJson, [System.Text.UTF8Encoding]::new($false))
    $frozenMigrationRequest = Reset-CountdownAcceptanceTestCase
    $frozenMigrationRequest.requestedAtUtc = $acceptanceMigrationUtc.ToString('o')
    [System.IO.File]::WriteAllText(
        $acceptanceStartupRequestPath,
        ($frozenMigrationRequest | ConvertTo-Json -Compress),
        [System.Text.UTF8Encoding]::new($false))
    $frozenMigrationJob = Start-CountdownAcceptanceTestApplication `
        -StartupRequest $frozenMigrationRequest `
        -SettingsJsonAfterRelease $acceptanceMigratedJson
    $frozenMigrationResult = @(Invoke-CountdownAcceptanceTestCase -StartupRequest $frozenMigrationRequest)
    Assert-Equal 'finalized-ready' ([string] $frozenMigrationResult[0].AcceptanceState) 'Transaction acceptance uses one frozen migration instant across the C# handoff'
    Assert-Equal $false ([bool] $frozenMigrationResult[0].SettingsUnchanged) 'Allowed schema migration records SettingsUnchanged=false'
    Assert-Equal $true ([string] $frozenMigrationResult[0].SettingsHashBefore -match '\A[0-9A-F]{64}\z') 'Allowed migration keeps the before SHA-256 fingerprint'
    Assert-Equal $true ([string] $frozenMigrationResult[0].SettingsHashAfter -match '\A[0-9A-F]{64}\z') 'Allowed migration keeps the after SHA-256 fingerprint'
    Assert-Equal $false ([string] $frozenMigrationResult[0].SettingsHashBefore -eq [string] $frozenMigrationResult[0].SettingsHashAfter) 'Allowed migration changes the live settings SHA-256'
    Assert-Equal $true ([long] $frozenMigrationResult[0].SettingsLengthBefore -ge 0) 'Allowed migration keeps the before length fingerprint'
    Assert-Equal $true ([long] $frozenMigrationResult[0].SettingsLengthAfter -ge 0) 'Allowed migration keeps the after length fingerprint'
    Assert-Equal $false ([string]::IsNullOrWhiteSpace([string] $frozenMigrationResult[0].SettingsMtimeUtcBefore)) 'Allowed migration keeps the before LastWriteTimeUtc fingerprint'
    Assert-Equal $false ([string]::IsNullOrWhiteSpace([string] $frozenMigrationResult[0].SettingsMtimeUtcAfter)) 'Allowed migration keeps the after LastWriteTimeUtc fingerprint'
    Wait-Job -Job $frozenMigrationJob -Timeout 5 | Out-Null
    Assert-Equal 'Completed' ([string] $frozenMigrationJob.State) 'Frozen migration acceptance completes the release protocol'

    $canonicalV10AcceptanceSettings = ConvertTo-CountdownSettingsJson -State (
        ConvertFrom-CountdownSettingsObject -Settings ([pscustomobject]@{
                schemaVersion = 10
                timeZoneId = 'China Standard Time'
                displayPrecision = 'Second'
                monthlySalary = 0
                workStart = '09:00'
                workEnd = '18:00'
                themePreference = 'Auto'
                tags = @()
                countdowns = @()
                notes = @()
                reminderStates = [pscustomobject]@{}
            }))
    Assert-Equal $false ((ConvertFrom-CountdownSettingsObject -Settings ($canonicalV10AcceptanceSettings | ConvertFrom-Json)).Migrated) 'Canonical v10 acceptance fixture is not a migration'
    [System.IO.File]::WriteAllText($acceptanceSettingsSnapshotPath, $canonicalV10AcceptanceSettings, [System.Text.UTF8Encoding]::new($false))
    $canonicalV10Request = Reset-CountdownAcceptanceTestCase
    $canonicalV10BeforeItem = Get-Item -LiteralPath $acceptanceSettingsPath
    $canonicalV10BeforeHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $acceptanceSettingsPath).Hash
    $canonicalV10BeforeLength = [long] $canonicalV10BeforeItem.Length
    $canonicalV10BeforeMtime = $canonicalV10BeforeItem.LastWriteTimeUtc.ToUniversalTime().ToString('o')
    $canonicalV10Job = Start-CountdownAcceptanceTestApplication -StartupRequest $canonicalV10Request
    $canonicalV10Result = @(Invoke-CountdownAcceptanceTestCase -StartupRequest $canonicalV10Request)
    $canonicalV10AfterItem = Get-Item -LiteralPath $acceptanceSettingsPath
    Assert-Equal $true ([bool] $canonicalV10Result[0].SettingsUnchanged) 'Already-canonical v10 keeps SettingsUnchanged=true'
    Assert-Equal $canonicalV10BeforeHash ([string] $canonicalV10Result[0].SettingsHashBefore) 'Already-canonical v10 before hash is the live file SHA-256'
    Assert-Equal $canonicalV10BeforeHash ([string] $canonicalV10Result[0].SettingsHashAfter) 'Already-canonical v10 after hash stays the live file SHA-256'
    Assert-Equal $canonicalV10BeforeLength ([long] $canonicalV10Result[0].SettingsLengthBefore) 'Already-canonical v10 before length is the live file length'
    Assert-Equal $canonicalV10BeforeLength ([long] $canonicalV10Result[0].SettingsLengthAfter) 'Already-canonical v10 after length stays the live file length'
    Assert-Equal $canonicalV10BeforeMtime ([string] $canonicalV10Result[0].SettingsMtimeUtcBefore) 'Already-canonical v10 before mtime is the live file LastWriteTimeUtc'
    Assert-Equal $canonicalV10BeforeMtime ([string] $canonicalV10Result[0].SettingsMtimeUtcAfter) 'Already-canonical v10 after mtime stays the live file LastWriteTimeUtc'
    Assert-Equal $canonicalV10BeforeHash (Get-FileHash -Algorithm SHA256 -LiteralPath $acceptanceSettingsPath).Hash 'Already-canonical v10 deploy path does not rewrite settings.json bytes'
    Assert-Equal $canonicalV10BeforeLength ([long] $canonicalV10AfterItem.Length) 'Already-canonical v10 deploy path does not change settings.json length'
    Assert-Equal $canonicalV10BeforeMtime $canonicalV10AfterItem.LastWriteTimeUtc.ToUniversalTime().ToString('o') 'Already-canonical v10 deploy path does not change settings.json LastWriteTimeUtc'
    Wait-Job -Job $canonicalV10Job -Timeout 5 | Out-Null
    Assert-Equal 'Completed' ([string] $canonicalV10Job.State) 'Already-canonical v10 acceptance completes the release protocol'

    $pidDriftRequest = Reset-CountdownAcceptanceTestCase -RuntimeProcessId 5252
    $pidDriftRejected = $false
    $pidDriftMessage = $null
    try {
        Invoke-CountdownAcceptanceTestCase -StartupRequest $pidDriftRequest -ExpectedProcessId 4242 | Out-Null
    }
    catch {
        $pidDriftMessage = $_.Exception.Message
        $pidDriftRejected = $pidDriftMessage -match '验收提交前受管应用进程已变化'
    }
    Assert-Equal $true $pidDriftRejected "Managed PID drift blocks transaction acceptance before release: $pidDriftMessage"
    Assert-Equal 0 $acceptanceAdapterState.Completed 'PID drift does not commit the transaction'
    Assert-Equal 0 $acceptanceAdapterState.Removed 'PID drift preserves rollback evidence'
    Assert-Equal 'active' ([string] ((Get-Content -LiteralPath $acceptanceStartupRequestPath -Raw -Encoding UTF8 | ConvertFrom-Json).acceptanceState)) 'PID drift leaves the candidate application read-only and unreleased'

    $taskStoppedRequest = Reset-CountdownAcceptanceTestCase
    $acceptanceTaskState = 'Disabled'
    $taskStoppedRejected = $false
    try {
        Invoke-CountdownAcceptanceTestCase -StartupRequest $taskStoppedRequest | Out-Null
    }
    catch {
        $taskStoppedRejected = $_.Exception.Message -match '验收提交前计划任务未回到 Ready 且 lastTaskResult=0：state=Disabled'
    }
    Assert-Equal $true $taskStoppedRejected 'A non-ready scheduled task blocks transaction acceptance before release'

    $taskRunningRequest = Reset-CountdownAcceptanceTestCase
    $acceptanceTaskState = 'Running'
    $taskRunningRejected = $false
    try {
        Invoke-CountdownAcceptanceTestCase -StartupRequest $taskRunningRequest | Out-Null
    }
    catch {
        $taskRunningRejected = $_.Exception.Message -match '验收提交前计划任务未回到 Ready 且 lastTaskResult=0：state=Running lastTaskResult=0'
    }
    Assert-Equal $true $taskRunningRejected 'A still-running scheduled task cannot impersonate Ready'
    Assert-Equal 0 $acceptanceAdapterState.Completed 'A still-running scheduled task does not commit the transaction'
    Assert-Equal 0 $acceptanceAdapterState.Removed 'A still-running scheduled task preserves rollback evidence'

    $nonzeroTaskResultRequest = Reset-CountdownAcceptanceTestCase
    $acceptanceLastTaskResult = 267
    $nonzeroTaskResultRejected = $false
    try {
        Invoke-CountdownAcceptanceTestCase -StartupRequest $nonzeroTaskResultRequest | Out-Null
    }
    catch {
        $nonzeroTaskResultRejected = $_.Exception.Message -match '验收提交前计划任务未回到 Ready 且 lastTaskResult=0：state=Ready lastTaskResult=267'
    }
    Assert-Equal $true $nonzeroTaskResultRejected 'A non-zero LastTaskResult from GetScheduledTaskInfo blocks transaction acceptance'
    Assert-Equal 0 $acceptanceAdapterState.Completed 'A non-zero LastTaskResult does not commit the transaction'
    Assert-Equal 0 $acceptanceAdapterState.Removed 'A non-zero LastTaskResult preserves rollback evidence'
    Assert-Equal 0 $acceptanceAdapterState.Completed 'A non-running scheduled task does not commit the transaction'
    Assert-Equal 0 $acceptanceAdapterState.Removed 'A non-running scheduled task preserves rollback evidence'
    Assert-Equal 'active' ([string] ((Get-Content -LiteralPath $acceptanceStartupRequestPath -Raw -Encoding UTF8 | ConvertFrom-Json).acceptanceState)) 'A non-running scheduled task leaves the candidate application read-only and unreleased'

    $taskDriftRequest = Reset-CountdownAcceptanceTestCase
    $acceptanceTaskState = 'StopAfterReady'
    $taskDriftJob = Start-CountdownAcceptanceTestApplication -StartupRequest $taskDriftRequest -SkipFinalizedReady
    $taskDriftRejected = $false
    try {
        Invoke-CountdownAcceptanceTestCase -StartupRequest $taskDriftRequest | Out-Null
    }
    catch {
        $taskDriftRejected = $_.Exception.Message -match 'schema 迁移确认后计划任务未回到 Ready 且 lastTaskResult=0：state=Disabled'
    }
    Wait-Job -Job $taskDriftJob -Timeout 5 | Out-Null
    Assert-Equal $true $taskDriftRejected 'Scheduled task drift after release blocks transaction acceptance'
    Assert-Equal 0 $acceptanceAdapterState.Completed 'Scheduled task drift after release does not commit the transaction'
    Assert-Equal 0 $acceptanceAdapterState.Removed 'Scheduled task drift after release preserves rollback evidence'
    $taskDriftStartupState = Get-Content -LiteralPath $acceptanceStartupRequestPath -Raw -Encoding UTF8 | ConvertFrom-Json
    Assert-Equal 'ready' ([string] $taskDriftStartupState.acceptanceState) 'Scheduled task drift does not finalize the startup request'
    Assert-Equal $true ([bool] $taskDriftStartupState.acceptanceReadOnly) 'Scheduled task drift leaves the candidate application read-only'

    $taskIdentityDriftRequest = Reset-CountdownAcceptanceTestCase
    $acceptanceTaskIdentity = 'DriftAfterReady'
    $taskIdentityDriftJob = Start-CountdownAcceptanceTestApplication -StartupRequest $taskIdentityDriftRequest -SkipFinalizedReady
    $taskIdentityDriftRejected = $false
    try {
        Invoke-CountdownAcceptanceTestCase -StartupRequest $taskIdentityDriftRequest | Out-Null
    }
    catch {
        $taskIdentityDriftRejected = $_.Exception.Message -match 'schema 迁移确认后计划任务定义已变化'
    }
    Wait-Job -Job $taskIdentityDriftJob -Timeout 5 | Out-Null
    Assert-Equal $true $taskIdentityDriftRejected 'Scheduled task identity drift after release blocks transaction acceptance'
    Assert-Equal 0 $acceptanceAdapterState.Completed 'Scheduled task identity drift does not commit the transaction'
    Assert-Equal 0 $acceptanceAdapterState.Removed 'Scheduled task identity drift preserves rollback evidence'
    $taskIdentityDriftStartupState = Get-Content -LiteralPath $acceptanceStartupRequestPath -Raw -Encoding UTF8 | ConvertFrom-Json
    Assert-Equal 'ready' ([string] $taskIdentityDriftStartupState.acceptanceState) 'Scheduled task identity drift does not finalize the startup request'
    Assert-Equal $true ([bool] $taskIdentityDriftStartupState.acceptanceReadOnly) 'Scheduled task identity drift leaves the candidate application read-only'

    $settingsDriftRequest = Reset-CountdownAcceptanceTestCase
    $settingsDriftJob = Start-CountdownAcceptanceTestApplication -StartupRequest $settingsDriftRequest -ChangeSettings -SkipFinalizedReady
    $settingsDriftRejected = $false
    try {
        Invoke-CountdownAcceptanceTestCase -StartupRequest $settingsDriftRequest | Out-Null
    }
    catch {
        $settingsDriftRejected = $_.Exception.Message -match 'C# schema 迁移超出允许的数据等价范围'
    }
    Wait-Job -Job $settingsDriftJob -Timeout 5 | Out-Null
    Assert-Equal $true $settingsDriftRejected 'Non-equivalent settings after release block transaction acceptance'
    Assert-Equal 0 $acceptanceAdapterState.Completed 'Non-equivalent settings do not commit the transaction'
    Assert-Equal 0 $acceptanceAdapterState.Removed 'Non-equivalent settings preserve rollback evidence'
    $settingsDriftStartupState = Get-Content -LiteralPath $acceptanceStartupRequestPath -Raw -Encoding UTF8 | ConvertFrom-Json
    Assert-Equal 'ready' ([string] $settingsDriftStartupState.acceptanceState) 'Settings rejection does not finalize the startup request'
    Assert-Equal $true ([bool] $settingsDriftStartupState.acceptanceReadOnly) 'Settings rejection leaves the candidate application read-only'

    $finalTimeoutRequest = Reset-CountdownAcceptanceTestCase
    $finalTimeoutJob = Start-CountdownAcceptanceTestApplication -StartupRequest $finalTimeoutRequest -SkipFinalizedReady
    $finalTimeoutRejected = $false
    try {
        Invoke-CountdownAcceptanceTestCase -StartupRequest $finalTimeoutRequest -ReadyTimeoutSeconds 1 | Out-Null
    }
    catch {
        $finalTimeoutRejected = $_.Exception.Message -match '应用未确认 finalized-ready'
    }
    Wait-Job -Job $finalTimeoutJob -Timeout 5 | Out-Null
    Assert-Equal $true $finalTimeoutRejected 'Finalized-ready timeout remains observable after transaction acceptance'
    Assert-Equal 1 $acceptanceAdapterState.Completed 'Finalized-ready timeout occurs after transaction acceptance'
    Assert-Equal 0 $acceptanceAdapterState.Removed 'Finalized-ready timeout preserves rollback evidence'
    Assert-Equal 'finalized' ([string] ((Get-Content -LiteralPath $acceptanceStartupRequestPath -Raw -Encoding UTF8 | ConvertFrom-Json).acceptanceState)) 'Finalized-ready timeout preserves the finalized startup state for recovery'
}
finally {
    foreach ($job in $acceptanceJobs) {
        Stop-Job -Job $job -ErrorAction SilentlyContinue
        Remove-Job -Job $job -Force -ErrorAction SilentlyContinue
    }
    foreach ($path in @($acceptanceStartupRequestPath, $acceptanceRuntimeStatusPath, $acceptanceStatusPath, $acceptanceSettingsSnapshotPath, $acceptanceSettingsPath, $acceptanceApplicationPath)) {
        Remove-Item -LiteralPath $path -Force -ErrorAction SilentlyContinue
    }
    Remove-Item -LiteralPath $acceptanceTestRoot -Force -ErrorAction SilentlyContinue
}

if ($AcceptanceOnly) {
    if ($failures.Count -gt 0) {
        $failures | ForEach-Object { Write-Error $_ }
        exit 1
    }
    [pscustomobject]@{ Passed = $script:assertions; Failed = 0; Scope = 'TransactionAcceptance' } | ConvertTo-Json
    exit 0
}

$firstTag = New-CountdownTagRecord -Id ' work ' -Name ' 工作 ' -Color ' #6B8E23 '
Assert-Equal 'work' $firstTag.id 'Tag record normalizes ID'
Assert-Equal '工作' $firstTag.name 'Tag record normalizes name'
Assert-Equal '#6B8E23' $firstTag.color 'Tag record preserves optional color'
$invalidTagColorRejected = $false
try {
    New-CountdownTagRecord -Id 'invalid-color' -Name '无效颜色' -Color 'red' | Out-Null
}
catch {
    $invalidTagColorRejected = $true
}
Assert-Equal $true $invalidTagColorRejected 'Invalid tag color is rejected'
$tagLibrary = [System.Collections.ArrayList]::new()
[void] $tagLibrary.Add($firstTag)
$tagLibraryCopy = Copy-CountdownTags -Tags $tagLibrary
$tagLibraryCopy[0].name = '已改名'
Assert-Equal '工作' $tagLibrary[0].name 'Tag library copy does not mutate source tag'
$normalizedTagIds = ConvertTo-CountdownTagIds -TagIds @(' work ', 'home') -KnownTagIds @('work', 'home')
Assert-Equal 2 $normalizedTagIds.Count 'Tag IDs retain unique values'
Assert-Equal 'work' $normalizedTagIds[0] 'Tag IDs preserve first-known canonical value'
Assert-Equal 'home' $normalizedTagIds[1] 'Tag IDs retain additional values'
$emptyTagIds = ConvertTo-CountdownTagIds -TagIds $null -KnownTagIds @('work')
Assert-Equal 0 $emptyTagIds.Count 'Empty tag IDs are valid for untagged records'
$duplicateTagIdRejected = $false
try {
    ConvertTo-CountdownTagIds -TagIds @('work', 'work') -KnownTagIds @('work') | Out-Null
}
catch {
    $duplicateTagIdRejected = $true
}
Assert-Equal $true $duplicateTagIdRejected 'Duplicate tag ID is rejected'
$danglingTagRejected = $false
try {
    ConvertTo-CountdownTagIds -TagIds @('missing') -KnownTagIds @('work') | Out-Null
}
catch {
    $danglingTagRejected = $true
}
Assert-Equal $true $danglingTagRejected 'Unknown tag ID is rejected'
Assert-Equal $true (Test-CountdownTagFilter -TagIds @('work') -SelectedTagIds @() -MatchMode Any) 'Empty tag selection includes tagged records'
Assert-Equal $true (Test-CountdownTagFilter -TagIds @('work') -SelectedTagIds @('work') -MatchMode Any) 'Any tag filter matches selected tag'
Assert-Equal $false (Test-CountdownTagFilter -TagIds @('home') -SelectedTagIds @('work') -MatchMode Any) 'Any tag filter excludes unselected tag'
Assert-Equal $true (Test-CountdownTagFilter -TagIds @('work', 'home') -SelectedTagIds @('home') -MatchMode Any) 'Any tag filter evaluates a non-first selected tag'
Assert-Equal $true (Test-CountdownTagFilter -TagIds @('work', 'home') -SelectedTagIds @('work', 'home') -MatchMode All) 'All tag filter matches every selected tag'
Assert-Equal $false (Test-CountdownTagFilter -TagIds @('work') -SelectedTagIds @('work', 'home') -MatchMode All) 'All tag filter requires every selected tag'
Assert-Equal $false (Test-CountdownTagFilter -TagIds @('work', 'home') -SelectedTagIds @('home', 'office') -MatchMode All) 'All tag filter excludes a missing selected tag'
Assert-Equal $true (Test-CountdownTagFilter -TagIds @() -SelectedTagIds @() -UntaggedOnly) 'Untagged filter includes untagged record'
Assert-Equal $false (Test-CountdownTagFilter -TagIds @('work') -SelectedTagIds @() -UntaggedOnly) 'Untagged filter excludes tagged record'

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

$runtimeTestRoot = Join-Path $PSScriptRoot ("runtime-test-" + [guid]::NewGuid().ToString('N'))
$runtimeStatusTestPath = Join-Path $runtimeTestRoot 'runtime-status.json'
$acceptanceStatusTestPath = Join-Path $runtimeTestRoot 'acceptance-status.json'
$startupRequestTestPath = Join-Path $runtimeTestRoot 'startup-request.json'
$runtimeApplicationPath = Join-Path $runtimeTestRoot '敏敏子倒计时.ps1'
$runtimeExecutablePath = Join-Path $runtimeTestRoot 'app\MinminCountdown.exe'
$diagnosticLogTestPath = Join-Path $runtimeTestRoot 'logs\application.jsonl'
try {
    New-Item -ItemType Directory -Path $runtimeTestRoot -Force | Out-Null
    [System.IO.File]::WriteAllText($runtimeApplicationPath, '# test application', (New-Object System.Text.UTF8Encoding($false)))
    $startupRequest = New-CountdownStartupRequest -Path $startupRequestTestPath -AcceptanceReadOnly
    Assert-Equal 1 $startupRequest.schemaVersion 'Startup request schema'
    Assert-Equal $true (-not [string]::IsNullOrWhiteSpace([string] $startupRequest.launchToken)) 'Startup request has token'
    Assert-Equal $true ([bool] $startupRequest.acceptanceReadOnly) 'Startup request can hold the candidate application in acceptance read-only mode'
    Assert-Equal 'active' ([string] $startupRequest.acceptanceState) 'Startup request begins in active acceptance state'
    $parsedRequestedAtUtc = [DateTimeOffset]::MinValue
    Assert-Equal $true ([DateTimeOffset]::TryParseExact(
            [string] $startupRequest.requestedAtUtc,
            'o',
            [System.Globalization.CultureInfo]::InvariantCulture,
            [System.Globalization.DateTimeStyles]::RoundtripKind,
            [ref] $parsedRequestedAtUtc)) 'Startup request uses the exact round-trip timestamp format'
    Assert-Equal $false (Complete-CountdownStartupRequest -Path $startupRequestTestPath -LaunchToken 'wrong-token') 'Startup request rejects release with a different token'
    Assert-Equal $true (Complete-CountdownStartupRequest -Path $startupRequestTestPath -LaunchToken $startupRequest.launchToken) 'Startup request releases acceptance mode with the same token'
    $releasedStartupRequest = Get-Content -LiteralPath $startupRequestTestPath -Raw -Encoding UTF8 | ConvertFrom-Json -DateKind String
    Assert-Equal 'released' ([string] $releasedStartupRequest.acceptanceState) 'Startup request persists explicit released acceptance state'
    Assert-Equal $true ([bool] $releasedStartupRequest.acceptanceReadOnly) 'Released startup request keeps the UI read-only until C# confirms migration'
    Assert-Equal 'released' ([string] (Wait-CountdownStartupRequestState -Path $startupRequestTestPath -LaunchToken $startupRequest.launchToken -ExpectedState released -TimeoutSeconds 0).acceptanceState) 'Startup request state wait validates token and state'
    $releasedStartupRequest.acceptanceState = 'ready'
    [System.IO.File]::WriteAllText($startupRequestTestPath, ($releasedStartupRequest | ConvertTo-Json -Depth 8), [System.Text.UTF8Encoding]::new($false))
    Assert-Equal $true (Set-CountdownStartupRequestFinalized -Path $startupRequestTestPath -LaunchToken $startupRequest.launchToken) 'Startup request finalizes only after C# reports ready'
    $finalizedStartupRequest = Get-Content -LiteralPath $startupRequestTestPath -Raw -Encoding UTF8 | ConvertFrom-Json
    Assert-Equal 'finalized' ([string] $finalizedStartupRequest.acceptanceState) 'Startup request persists explicit finalized state'
    Assert-Equal $false ([bool] $finalizedStartupRequest.acceptanceReadOnly) 'Finalized startup request permits UI editing'
    [System.IO.File]::WriteAllText($startupRequestTestPath, '{broken-json', [System.Text.UTF8Encoding]::new($false))
    $corruptReleaseRejected = $false
    try { Complete-CountdownStartupRequest -Path $startupRequestTestPath -LaunchToken $startupRequest.launchToken | Out-Null }
    catch { $corruptReleaseRejected = $true }
    Assert-Equal $true $corruptReleaseRejected 'Startup request release preserves JSON and IO failures as exceptions'
    [System.IO.File]::WriteAllText($startupRequestTestPath, '{"schemaVersion":999,"launchToken":"unknown","acceptanceState":"active"}', [System.Text.UTF8Encoding]::new($false))
    $unknownSchemaRejected = $false
    try { Complete-CountdownStartupRequest -Path $startupRequestTestPath -LaunchToken 'unknown' | Out-Null }
    catch { $unknownSchemaRejected = $_.Exception.Message -match 'schemaVersion' }
    Assert-Equal $true $unknownSchemaRejected 'Startup request rejects unknown schema versions'
    $nonRoundTripStartupRequest = [ordered]@{
        schemaVersion = 1
        launchToken = 'non-round-trip-token'
        requestedAtUtc = '2026-08-13T12:34:56Z'
        acceptanceReadOnly = $true
        acceptanceState = 'active'
    }
    [System.IO.File]::WriteAllText($startupRequestTestPath, ($nonRoundTripStartupRequest | ConvertTo-Json -Compress), [System.Text.UTF8Encoding]::new($false))
    $nonRoundTripTimestampRejected = $false
    try { Complete-CountdownStartupRequest -Path $startupRequestTestPath -LaunchToken $nonRoundTripStartupRequest.launchToken | Out-Null }
    catch { $nonRoundTripTimestampRejected = $_.Exception.Message -match 'requestedAtUtc' }
    Assert-Equal $true $nonRoundTripTimestampRejected 'Startup request rejects valid ISO timestamps outside the exact round-trip format'
    $startupRequest = New-CountdownStartupRequest -Path $startupRequestTestPath -AcceptanceReadOnly
    Assert-Equal $false (Test-Path -LiteralPath "$startupRequestTestPath.tmp") 'Startup request leaves no fixed temporary file'
    $receipt = Write-CountdownWindowReadyReceipt -Path $runtimeStatusTestPath -LaunchToken $startupRequest.launchToken -ProcessId 4242 -SessionId 7 -ApplicationPath $runtimeApplicationPath -CalendarState Pending
    $processProbe = {
        param([int] $ProcessId)
        if ($ProcessId -eq 4242) {
            [pscustomobject]@{ ProcessId = 4242; SessionId = 7; CommandLine = "pwsh.exe -STA -File `"$runtimeApplicationPath`"" }
        }
    }
    $defaultAdapterEvaluation = Test-CountdownWindowReadyReceipt -Receipt $receipt -ExpectedLaunchToken $startupRequest.launchToken -ExpectedSessionId 7 -ExpectedApplicationPath $runtimeApplicationPath -ProcessProbe $processProbe
    Assert-Equal $false $defaultAdapterEvaluation.Ready 'Window-ready receipt rejects the legacy PowerShell adapter by default'
    $legacyAdapterEvaluation = Test-CountdownWindowReadyReceipt -Receipt $receipt -ExpectedLaunchToken $startupRequest.launchToken -ExpectedSessionId 7 -ExpectedApplicationPath $runtimeApplicationPath -ProcessProbe $processProbe -AllowLegacyPowerShellAdapter
    Assert-Equal $true $legacyAdapterEvaluation.Ready 'Window-ready receipt accepts the legacy PowerShell adapter only in explicit compatibility mode'
    Assert-Equal 'Pending' $legacyAdapterEvaluation.Receipt.calendarState 'Window-ready does not wait for calendar'
    $managedExecutableProbe = {
        param([int] $ProcessId)
        if ($ProcessId -eq 4242) {
            [pscustomobject]@{ ProcessId = 4242; SessionId = 7; ExecutablePath = $runtimeExecutablePath; CommandLine = "`"$runtimeExecutablePath`"" }
        }
    }
    Assert-Equal $true (Test-CountdownWindowReadyReceipt -Receipt $receipt -ExpectedLaunchToken $startupRequest.launchToken -ExpectedSessionId 7 -ExpectedApplicationPath $runtimeApplicationPath -ProcessProbe $managedExecutableProbe).Ready 'Window-ready receipt accepts the managed C# application host'
    $acceptanceReceipt = [ordered]@{
        schemaVersion = 1
        stage = 'finalized-ready'
        launchToken = $startupRequest.launchToken
        processId = 4242
        sessionId = 7
        applicationPath = $runtimeApplicationPath
        readyAtUtc = [DateTimeOffset]::UtcNow.ToString('o')
    }
    [System.IO.File]::WriteAllText($acceptanceStatusTestPath, ($acceptanceReceipt | ConvertTo-Json -Compress), [System.Text.UTF8Encoding]::new($false))
    Assert-Equal 'finalized-ready' ([string] (Wait-CountdownAcceptanceReadyReceipt -Path $acceptanceStatusTestPath -LaunchToken $startupRequest.launchToken -ExpectedProcessId 4242 -ExpectedSessionId 7 -ExpectedApplicationPath $runtimeApplicationPath -TimeoutSeconds 0 -ProcessProbe $managedExecutableProbe).stage) 'Acceptance-ready receipt validates token, C# PID, session and adapter path'
    $unmanagedExecutableProbe = {
        param([int] $ProcessId)
        if ($ProcessId -eq 4242) {
            [pscustomobject]@{ ProcessId = 4242; SessionId = 7; ExecutablePath = (Join-Path $runtimeTestRoot 'other\MinminCountdown.exe'); CommandLine = '"MinminCountdown.exe"' }
        }
    }
    Assert-Equal $false (Test-CountdownWindowReadyReceipt -Receipt $receipt -ExpectedLaunchToken $startupRequest.launchToken -ExpectedSessionId 7 -ExpectedApplicationPath $runtimeApplicationPath -ProcessProbe $unmanagedExecutableProbe).Ready 'Window-ready receipt rejects an unmanaged executable with the expected process name'
    Assert-Equal $false (Test-CountdownWindowReadyReceipt -Receipt $receipt -ExpectedLaunchToken 'old-token' -ExpectedSessionId 7 -ExpectedApplicationPath $runtimeApplicationPath -ProcessProbe $processProbe).Ready 'Old startup token is rejected'
    Assert-Equal $false (Test-CountdownWindowReadyReceipt -Receipt $receipt -ExpectedLaunchToken $startupRequest.launchToken -ExpectedSessionId 8 -ExpectedApplicationPath $runtimeApplicationPath -ProcessProbe $processProbe).Ready 'Wrong interactive session is rejected'
    Assert-Equal $false (Test-CountdownWindowReadyReceipt -Receipt $receipt -ExpectedLaunchToken $startupRequest.launchToken -ExpectedSessionId 7 -ExpectedApplicationPath (Join-Path $runtimeTestRoot 'other.ps1') -ProcessProbe $processProbe).Ready 'Wrong application path is rejected'
    $embeddedPathProbe = {
        param([int] $ProcessId)
        if ($ProcessId -eq 4242) {
            [pscustomobject]@{ ProcessId = 4242; SessionId = 7; CommandLine = "pwsh.exe -File `"$(Join-Path $runtimeTestRoot 'helper.ps1')`" -ArgumentList `"$runtimeApplicationPath`"" }
        }
    }
    Assert-Equal $false (Test-CountdownWindowReadyReceipt -Receipt $receipt -ExpectedLaunchToken $startupRequest.launchToken -ExpectedSessionId 7 -ExpectedApplicationPath $runtimeApplicationPath -ProcessProbe $embeddedPathProbe).Ready 'Command-line path substring outside -File is rejected'
    $commandModeProbe = {
        param([int] $ProcessId)
        if ($ProcessId -eq 4242) {
            [pscustomobject]@{ ProcessId = 4242; SessionId = 7; CommandLine = "pwsh.exe -NoProfile -Command `"Write-Output ' -File $runtimeApplicationPath '`"" }
        }
    }
    Assert-Equal $false (Test-CountdownWindowReadyReceipt -Receipt $receipt -ExpectedLaunchToken $startupRequest.launchToken -ExpectedSessionId 7 -ExpectedApplicationPath $runtimeApplicationPath -ProcessProbe $commandModeProbe).Ready 'Command-mode text containing -File is not accepted as the application process'
    Assert-Equal $false (Test-CountdownWindowReadyReceipt -Receipt $receipt -ExpectedLaunchToken $startupRequest.launchToken -ExpectedSessionId 7 -ExpectedApplicationPath $runtimeApplicationPath -ProcessProbe { param([int] $ProcessId) $null }).Ready 'Missing process is rejected'
    Assert-Equal $false (Test-CountdownWindowReadyReceipt -Receipt $receipt -ExpectedLaunchToken $startupRequest.launchToken -ExpectedSessionId 7 -ExpectedApplicationPath $runtimeApplicationPath -ProcessProbe { param([int] $ProcessId) [pscustomobject]@{ ProcessId = 4243; SessionId = 7; CommandLine = "pwsh.exe -File `"$runtimeApplicationPath`"" } }).Ready 'Mismatched process identity is rejected'
    [System.IO.File]::WriteAllText($runtimeStatusTestPath, '{not-valid-json', (New-Object System.Text.UTF8Encoding($false)))
    Assert-Equal $true ($null -eq (Read-CountdownRuntimeReceipt -Path $runtimeStatusTestPath)) 'Malformed receipt is not accepted'
    $timeoutObserved = $false
    try {
        Wait-CountdownWindowReady -Path $runtimeStatusTestPath -LaunchToken $startupRequest.launchToken -ExpectedSessionId 7 -ExpectedApplicationPath $runtimeApplicationPath -TimeoutSeconds 0 -PollMilliseconds 1 -ProcessProbe { param([int] $ProcessId) $null } | Out-Null
    }
    catch { $timeoutObserved = $_.Exception.Message -match '启动就绪回执超时' }
    Assert-Equal $true $timeoutObserved 'Window-ready wait has a bounded timeout'
    $diagnosticStarted = Write-CountdownDiagnosticEvent -Path $diagnosticLogTestPath -Level Info -Event 'ProcessStarted'
    Assert-Equal $true $diagnosticStarted 'Diagnostic logging reports a successful append'
    $diagnosticStartEntry = Get-Content -LiteralPath $diagnosticLogTestPath -Encoding UTF8 | Select-Object -First 1 | ConvertFrom-Json
    Assert-Equal 1 $diagnosticStartEntry.schemaVersion 'Diagnostic log schema'
    Assert-Equal 'ProcessStarted' $diagnosticStartEntry.event 'Diagnostic log event name'
    Assert-Equal 'Info' $diagnosticStartEntry.level 'Diagnostic log level'
    Assert-Equal $PID $diagnosticStartEntry.processId 'Diagnostic log process identity'
    $diagnosticException = [System.InvalidOperationException]::new('Deferred theme template failed')
    $diagnosticFailed = Write-CountdownDiagnosticEvent -Path $diagnosticLogTestPath -Level Error -Event 'FatalUnhandledException' -Exception $diagnosticException -ScriptStackTrace 'Start-CountdownApplication line 1'
    Assert-Equal $true $diagnosticFailed 'Diagnostic logging records fatal exceptions'
    $diagnosticErrorEntry = Get-Content -LiteralPath $diagnosticLogTestPath -Encoding UTF8 | Select-Object -Last 1 | ConvertFrom-Json
    Assert-Equal 'System.InvalidOperationException' $diagnosticErrorEntry.exceptionType 'Diagnostic log exception type'
    Assert-Equal 'Deferred theme template failed' $diagnosticErrorEntry.exceptionMessage 'Diagnostic log exception message'
    Assert-Equal 'Start-CountdownApplication line 1' $diagnosticErrorEntry.scriptStackTrace 'Diagnostic log PowerShell stack'
    foreach ($index in 1..3) {
        Write-CountdownDiagnosticEvent -Path $diagnosticLogTestPath -Level Info -Event ("RotationProbe$index-" + ('x' * 180)) -MaxBytes 300 -MaxArchives 2 | Out-Null
    }
    Assert-Equal $true (Test-Path -LiteralPath "$diagnosticLogTestPath.1" -PathType Leaf) 'Diagnostic log rotates at the size bound'
    Assert-Equal $true ((Get-Item -LiteralPath $diagnosticLogTestPath).Length -lt 1024) 'Diagnostic current log remains bounded after rotation'

    $utf8NoBomDiagnostic = [System.Text.UTF8Encoding]::new($false)
    $runtimeModulePath = Join-Path $PSScriptRoot 'Countdown.Runtime.psm1'
    $runtimeModuleForDiagnostic = Get-Module -Name 'Countdown.Runtime'
    $diagnosticMutexGuardPath = Join-Path $PSScriptRoot 'diagnostic-mutex-name-guard.jsonl'
    $expectedDiagnosticMutexName = Get-IndependentCountdownDiagnosticMutexName -Path $diagnosticMutexGuardPath
    $actualDiagnosticMutexName = ''
    try {
        $actualDiagnosticMutexName = [string] (& $runtimeModuleForDiagnostic {
                param($Path)
                Get-CountdownDiagnosticMutexName -Path $Path
            } $diagnosticMutexGuardPath)
    }
    catch {
        $actualDiagnosticMutexName = ''
    }
    Assert-Equal $expectedDiagnosticMutexName $actualDiagnosticMutexName 'Diagnostic mutex name follows the C# path-hash rule'
    $mixedCaseDiagnosticMutexName = ''
    $aliasDiagnosticMutexName = ''
    try {
        $mixedCaseDiagnosticMutexName = [string] (& $runtimeModuleForDiagnostic {
                param($Path)
                Get-CountdownDiagnosticMutexName -Path $Path
            } $diagnosticMutexGuardPath.ToLowerInvariant())
        $aliasDiagnosticMutexName = [string] (& $runtimeModuleForDiagnostic {
                param($Path)
                Get-CountdownDiagnosticMutexName -Path $Path
            } (Join-Path $PSScriptRoot 'mutex-alias\..\diagnostic-mutex-name-guard.jsonl'))
    }
    catch {
        $mixedCaseDiagnosticMutexName = ''
        $aliasDiagnosticMutexName = ''
    }
    Assert-Equal $expectedDiagnosticMutexName $mixedCaseDiagnosticMutexName 'Diagnostic mutex name normalizes path case'
    Assert-Equal $expectedDiagnosticMutexName $aliasDiagnosticMutexName 'Diagnostic mutex name uses the resolved full path'

    $diagnosticTimeoutLogPath = Join-Path $runtimeTestRoot 'logs\timeout.jsonl'
    $expectedTimeoutMutexName = Get-IndependentCountdownDiagnosticMutexName -Path $diagnosticTimeoutLogPath
    $timeoutProbePath = Join-Path $runtimeTestRoot 'diagnostic-timeout-probe.ps1'
    $timeoutStdoutPath = Join-Path $runtimeTestRoot 'diagnostic-timeout.stdout.txt'
    $timeoutStderrPath = Join-Path $runtimeTestRoot 'diagnostic-timeout.stderr.txt'
    $timeoutProbe = @'
param(
    [Parameter(Mandatory)] [string] $RuntimeModulePath,
    [Parameter(Mandatory)] [string] $LogPath
)
$ErrorActionPreference = 'Stop'
Import-Module -Name $RuntimeModulePath -Force
$threw = $false
$result = $null
$started = [DateTimeOffset]::UtcNow
try {
    $result = Write-CountdownDiagnosticEvent -Path $LogPath -Level Info -Event 'DiagnosticMutexTimeoutDrop' -TimeoutSeconds 1
}
catch {
    $threw = $true
}
[pscustomobject]@{
    Threw = $threw
    Result = $result
    ElapsedMilliseconds = [int] ([DateTimeOffset]::UtcNow - $started).TotalMilliseconds
} | ConvertTo-Json -Compress
'@
    [System.IO.File]::WriteAllText($timeoutProbePath, $timeoutProbe, $utf8NoBomDiagnostic)
    $heldDiagnosticMutex = [System.Threading.Mutex]::new($false, $expectedTimeoutMutexName)
    $heldDiagnosticMutexAcquired = $false
    $timeoutProbeProcess = $null
    try {
        $heldDiagnosticMutexAcquired = $heldDiagnosticMutex.WaitOne([TimeSpan]::FromSeconds(5))
        Assert-Equal $true $heldDiagnosticMutexAcquired 'Diagnostic timeout fixture acquired the independently derived mutex'
        $diagnosticPwsh = (Get-Command pwsh.exe -CommandType Application -ErrorAction Stop | Select-Object -First 1 -ExpandProperty Source)
        $timeoutProbeProcess = Start-Process -FilePath $diagnosticPwsh -ArgumentList @(
            '-NoLogo',
            '-NoProfile',
            '-File',
            $timeoutProbePath,
            '-RuntimeModulePath',
            $runtimeModulePath,
            '-LogPath',
            $diagnosticTimeoutLogPath
        ) -WorkingDirectory $runtimeTestRoot -PassThru -WindowStyle Hidden `
            -RedirectStandardOutput $timeoutStdoutPath `
            -RedirectStandardError $timeoutStderrPath
        $timeoutProbeExited = $timeoutProbeProcess.WaitForExit(8000)
        if (-not $timeoutProbeExited) {
            if ($heldDiagnosticMutexAcquired) {
                $heldDiagnosticMutex.ReleaseMutex()
                $heldDiagnosticMutexAcquired = $false
            }
            if (-not $timeoutProbeProcess.WaitForExit(8000)) {
                Stop-Process -Id $timeoutProbeProcess.Id -Force -ErrorAction SilentlyContinue
            }
        }
        else {
            $timeoutProbeProcess.WaitForExit()
        }
        Assert-Equal $true $timeoutProbeExited 'Diagnostic write wait stays within a 1-second bound'
        Assert-Equal 0 $timeoutProbeProcess.ExitCode 'Diagnostic timeout probe exits without throwing'
        $timeoutProbeSummary = Get-Content -LiteralPath $timeoutStdoutPath -Raw -Encoding UTF8 | ConvertFrom-Json
        Assert-Equal $false ([bool] $timeoutProbeSummary.Threw) 'Diagnostic mutex timeout does not throw to the caller'
        Assert-Equal $false ([bool] $timeoutProbeSummary.Result) 'Diagnostic mutex timeout silently drops the line'
        Assert-Equal $true (
            [int] $timeoutProbeSummary.ElapsedMilliseconds -ge 500 -and
            [int] $timeoutProbeSummary.ElapsedMilliseconds -le 4000
        ) 'Diagnostic mutex wait uses the requested 1-second timeout'
        Assert-Equal $false (Test-Path -LiteralPath $diagnosticTimeoutLogPath -PathType Leaf) 'Dropped diagnostic line is not written'
    }
    finally {
        if ($heldDiagnosticMutexAcquired) {
            $heldDiagnosticMutex.ReleaseMutex()
        }
        $heldDiagnosticMutex.Dispose()
        if ($null -ne $timeoutProbeProcess) {
            $timeoutProbeProcess.Dispose()
        }
    }
    $afterTimeoutWrite = $false
    $afterTimeoutThrew = $false
    try {
        $afterTimeoutWrite = Write-CountdownDiagnosticEvent -Path $diagnosticTimeoutLogPath -Level Info -Event 'DiagnosticMutexTimeoutRecovered' -TimeoutSeconds 1
    }
    catch {
        $afterTimeoutThrew = $true
    }
    Assert-Equal $false $afterTimeoutThrew 'Diagnostic write after mutex release does not throw'
    Assert-Equal $true $afterTimeoutWrite 'Diagnostic write succeeds after the mutex is released'

    $diagnosticConcurrentLogPath = Join-Path $runtimeTestRoot 'logs\concurrent.jsonl'
    $diagnosticConcurrentBarrierPath = Join-Path $runtimeTestRoot 'diagnostic-concurrent.start'
    $diagnosticConcurrentProbePath = Join-Path $runtimeTestRoot 'diagnostic-concurrent-probe.ps1'
    $concurrentProbe = @'
param(
    [Parameter(Mandatory)] [string] $RuntimeModulePath,
    [Parameter(Mandatory)] [string] $LogPath,
    [Parameter(Mandatory)] [string] $BarrierPath,
    [Parameter(Mandatory)] [int] $Worker,
    [Parameter(Mandatory)] [int] $WriteCount
)
$ErrorActionPreference = 'Stop'
Import-Module -Name $RuntimeModulePath -Force
while (-not (Test-Path -LiteralPath $BarrierPath -PathType Leaf)) {
    Start-Sleep -Milliseconds 5
}
$failed = 0
for ($index = 0; $index -lt $WriteCount; $index++) {
    $written = Write-CountdownDiagnosticEvent -Path $LogPath -Level Info -Event "DiagnosticMutexWorker$Worker-$index" -MaxBytes 4096 -MaxArchives 3
    if (-not $written) { $failed++ }
}
if ($failed -ne 0) { exit 1 }
'@
    [System.IO.File]::WriteAllText($diagnosticConcurrentProbePath, $concurrentProbe, $utf8NoBomDiagnostic)
    $diagnosticPwsh = (Get-Command pwsh.exe -CommandType Application -ErrorAction Stop | Select-Object -First 1 -ExpandProperty Source)
    $concurrentProcesses = [System.Collections.Generic.List[System.Diagnostics.Process]]::new()
    $concurrentWorkerCount = 2
    $concurrentWritesPerWorker = 30
    try {
        for ($worker = 0; $worker -lt $concurrentWorkerCount; $worker++) {
            $probeLogPath = if ($worker -eq 0) {
                $diagnosticConcurrentLogPath
            }
            else {
                Join-Path $runtimeTestRoot 'logs\..\logs\concurrent.jsonl'
            }
            $probe = Start-Process -FilePath $diagnosticPwsh -ArgumentList @(
                '-NoLogo',
                '-NoProfile',
                '-File',
                $diagnosticConcurrentProbePath,
                '-RuntimeModulePath',
                $runtimeModulePath,
                '-LogPath',
                $probeLogPath,
                '-BarrierPath',
                $diagnosticConcurrentBarrierPath,
                '-Worker',
                $worker,
                '-WriteCount',
                $concurrentWritesPerWorker
            ) -WorkingDirectory $runtimeTestRoot -PassThru -WindowStyle Hidden `
                -RedirectStandardOutput (Join-Path $runtimeTestRoot "diagnostic-concurrent-$worker.stdout.txt") `
                -RedirectStandardError (Join-Path $runtimeTestRoot "diagnostic-concurrent-$worker.stderr.txt")
            [void] $concurrentProcesses.Add($probe)
        }
        Start-Sleep -Milliseconds 300
        [System.IO.File]::WriteAllText($diagnosticConcurrentBarrierPath, 'start', $utf8NoBomDiagnostic)
        foreach ($probe in $concurrentProcesses) {
            $exited = $probe.WaitForExit(30000)
            if (-not $exited) {
                Stop-Process -Id $probe.Id -Force -ErrorAction SilentlyContinue
            }
            else {
                $probe.WaitForExit()
            }
            Assert-Equal $true $exited "Diagnostic concurrent probe exited in time: PID=$($probe.Id)"
            Assert-Equal 0 $probe.ExitCode "Diagnostic concurrent probe wrote every event: PID=$($probe.Id)"
        }
        $concurrentEntries = [System.Collections.Generic.List[object]]::new()
        $concurrentParseFailures = 0
        foreach ($path in @(
                $diagnosticConcurrentLogPath,
                "$diagnosticConcurrentLogPath.1",
                "$diagnosticConcurrentLogPath.2",
                "$diagnosticConcurrentLogPath.3"
            )) {
            if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { continue }
            foreach ($line in Get-Content -LiteralPath $path -Encoding UTF8) {
                if ([string]::IsNullOrWhiteSpace($line)) { continue }
                try {
                    $concurrentEntries.Add(($line | ConvertFrom-Json))
                }
                catch {
                    $concurrentParseFailures++
                }
            }
        }
        Assert-Equal 0 $concurrentParseFailures 'Concurrent diagnostic writes do not interleave or corrupt JSONL'
        $concurrentWorkerEvents = @(
            $concurrentEntries |
                Where-Object { $_.event -like 'DiagnosticMutexWorker*' }
        )
        Assert-Equal ($concurrentWorkerCount * $concurrentWritesPerWorker) $concurrentWorkerEvents.Count 'Concurrent diagnostic writes persist every event'
        Assert-Equal $concurrentWorkerEvents.Count @($concurrentWorkerEvents.event | Sort-Object -Unique).Count 'Concurrent diagnostic writes persist unique events'
    }
    finally {
        foreach ($probe in $concurrentProcesses) {
            if (-not $probe.HasExited) {
                Stop-Process -Id $probe.Id -Force -ErrorAction SilentlyContinue
            }
            $probe.Dispose()
        }
    }
}
finally {
    Remove-Item -LiteralPath $runtimeTestRoot -Recurse -Force -ErrorAction SilentlyContinue
}

$transactionModule = Get-Module -Name 'Countdown.InstallTransaction'
$installPathGuardCommand = Get-Command Assert-CountdownInstallPathOnDDrive -Module Countdown.InstallTransaction -ErrorAction SilentlyContinue
Assert-Equal $true ($null -ne $installPathGuardCommand) 'Installer-facing D-drive path guard is exported by the transaction module'
Assert-Equal 'D:\agents\projects\时钟倒计时\expected-desktop' (Assert-CountdownInstallPathOnDDrive -Path 'D:\agents\projects\时钟倒计时\expected-desktop') 'Installer-facing D-drive path guard normalizes a safe expected desktop path'
$outsideDDriveRejected = $false
try {
    Assert-CountdownInstallPathOnDDrive -Path 'C:\unexpected-desktop' | Out-Null
}
catch {
    $outsideDDriveRejected = $_.Exception.Message -match '只允许操作 D 盘路径'
}
Assert-Equal $true $outsideDDriveRejected 'Installer-facing D-drive path guard rejects a non-D expected desktop path'
$managedFilePath = & $transactionModule { param($CommandLine) Get-CountdownManagedApplicationFileArgument -CommandLine $CommandLine } "pwsh.exe -NoProfile -File `"$runtimeApplicationPath`""
Assert-Equal ([System.IO.Path]::GetFullPath($runtimeApplicationPath)) $managedFilePath 'Managed-process parser resolves exact -File application path'
$managedEmbeddedPath = & $transactionModule { param($CommandLine) Get-CountdownManagedApplicationFileArgument -CommandLine $CommandLine } "pwsh.exe -File `"$(Join-Path $PSScriptRoot 'helper.ps1')`" -ArgumentList `"$runtimeApplicationPath`""
Assert-Equal ([System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot 'helper.ps1'))) $managedEmbeddedPath 'Managed-process parser does not treat an argument substring as the script path'
$managedCommandEmbeddedPath = & $transactionModule { param($CommandLine) Get-CountdownManagedApplicationFileArgument -CommandLine $CommandLine } "pwsh.exe -NoProfile -Command `"Write-Output ' -File $runtimeApplicationPath '`""
Assert-Equal $true ($null -eq $managedCommandEmbeddedPath) 'Managed-process parser rejects -File text inside -Command argv'
$runtimeModule = Get-Module -Name 'Countdown.Runtime'
$runtimeCommandEmbeddedPath = & $runtimeModule { param($CommandLine) Get-CountdownRuntimeFileArgument -CommandLine $CommandLine } "pwsh.exe -NoProfile -Command `"Write-Output ' -File $runtimeApplicationPath '`""
Assert-Equal $true ($null -eq $runtimeCommandEmbeddedPath) 'Runtime-process parser rejects -File text inside -Command argv'
$resolvedInteractiveDesktop = & $transactionModule {
    Resolve-CountdownInteractiveDesktopPath -ProfileImagePath '%SystemDrive%\Profiles\OfficeUser' -DesktopTemplate '%OneDrive%\Desktop' -UserName 'OfficeUser' -EnvironmentVariables @{ SystemDrive = 'D:'; OneDrive = '%USERPROFILE%\OneDrive' }
}
Assert-Equal 'D:\Profiles\OfficeUser\OneDrive\Desktop' $resolvedInteractiveDesktop 'Desktop resolver uses the Explorer SID environment rather than the current pwsh environment'
$unknownDesktopVariableRejected = $false
try {
    & $transactionModule {
        Resolve-CountdownInteractiveDesktopPath -ProfileImagePath '%SystemDrive%\Profiles\OfficeUser' -DesktopTemplate '%UnmappedDesktop%\Desktop' -UserName 'OfficeUser' -EnvironmentVariables @{ SystemDrive = 'D:' }
    } | Out-Null
}
catch {
    $unknownDesktopVariableRejected = $_.Exception.Message -match '无法按交互用户环境解析'
}
Assert-Equal $true $unknownDesktopVariableRejected 'Desktop resolver rejects variables missing from the Explorer SID environment'
$sameSessionExplorerContext = & $transactionModule {
    Get-CountdownSingleInteractiveExplorerContext -ExplorerProcesses @(
        [pscustomobject]@{ ProcessId = 101; SessionId = 7 },
        [pscustomobject]@{ ProcessId = 102; SessionId = 7 }
    ) -OwnerResolver {
        param($process)
        [pscustomobject]@{ ReturnValue = 0; Domain = 'OFFICE'; User = 'Minmin' }
    }
}
Assert-Equal 7 $sameSessionExplorerContext.SessionId 'Multiple Explorer processes in the same interactive session are accepted'
Assert-Equal 'OFFICE\Minmin' $sameSessionExplorerContext.User 'Explorer grouping retains the interactive user identity'
$multipleExplorerContextsRejected = $false
try {
    & $transactionModule {
        Get-CountdownSingleInteractiveExplorerContext -ExplorerProcesses @(
            [pscustomobject]@{ ProcessId = 201; SessionId = 7 },
            [pscustomobject]@{ ProcessId = 202; SessionId = 8 }
        ) -OwnerResolver {
            param($process)
            [pscustomobject]@{ ReturnValue = 0; Domain = 'OFFICE'; User = 'Minmin' }
        } | Out-Null
    }
}
catch {
    $multipleExplorerContextsRejected = $_.Exception.Message -match 'exactly one interactive Explorer session and user'
}
Assert-Equal $true $multipleExplorerContextsRejected 'Multiple interactive Explorer sessions are rejected as ambiguous'

$stoppedWithoutProcesses = Test-CountdownManagedApplicationStopped -ProcessIds @() -TaskStates @{ 'Minmin Countdown' = 'Ready'; '敏敏子倒计时' = 'Missing' }
Assert-Equal $true $stoppedWithoutProcesses.Ready 'Managed application stop accepts non-running tasks with no process'
$taskStillRunning = Test-CountdownManagedApplicationStopped -ProcessIds @() -TaskStates @{ 'Minmin Countdown' = 'Running'; '敏敏子倒计时' = 'Ready' }
Assert-Equal $false $taskStillRunning.Ready 'Managed application stop waits for a running scheduled task even without a process'
Assert-Equal 'Minmin Countdown' (@($taskStillRunning.RunningTasks) -join ',') 'Managed application stop reports the running task'
$taskStillQueued = Test-CountdownManagedApplicationStopped -ProcessIds @() -TaskStates @{ 'Minmin Countdown' = 'Queued'; '敏敏子倒计时' = 'Ready' }
Assert-Equal $false $taskStillQueued.Ready 'Managed application stop waits for a queued scheduled task even without a process'
$taskStateUnknown = Test-CountdownManagedApplicationStopped -ProcessIds @() -TaskStates @{ 'Minmin Countdown' = 'Unknown'; '敏敏子倒计时' = 'Ready' }
Assert-Equal $false $taskStateUnknown.Ready 'Managed application stop treats an unknown scheduled-task state as still active'
$processQueryFailureRejected = $false
try {
    & $transactionModule {
        param($ApplicationPath)
        Get-CountdownManagedApplicationProcesses -ApplicationPath $ApplicationPath -ProcessQueryAction { throw 'injected process query failure' }
    } $runtimeApplicationPath | Out-Null
}
catch {
    $processQueryFailureRejected = $_.Exception.Message -match 'injected process query failure'
}
Assert-Equal $true $processQueryFailureRejected 'Managed process probe fails closed when CIM enumeration fails'
$taskQueryFailureRejected = $false
try {
    & $transactionModule {
        Get-CountdownManagedTaskStates -TaskName 'Minmin Countdown' -LegacyTaskName '敏敏子倒计时' -TaskQueryAction { throw 'injected task query failure' }
    } | Out-Null
}
catch {
    $taskQueryFailureRejected = $_.Exception.Message -match 'injected task query failure'
}
Assert-Equal $true $taskQueryFailureRejected 'Managed task probe fails closed when scheduled-task enumeration fails'
$legacyRemovalQueryFailureRejected = $false
try {
    Remove-CountdownScheduledTaskIfPresent -TaskName '敏敏子倒计时' -TaskQueryAction { throw 'injected legacy task query failure' } | Out-Null
}
catch {
    $legacyRemovalQueryFailureRejected = $_.Exception.Message -match 'injected legacy task query failure'
}
Assert-Equal $true $legacyRemovalQueryFailureRejected 'Legacy task removal fails closed when scheduled-task enumeration fails'
$exactTaskStates = & $transactionModule {
    Get-CountdownManagedTaskStates -TaskName 'Minmin Countdown' -LegacyTaskName '敏敏子倒计时' -TaskQueryAction {
        [pscustomobject]@{ TaskName = 'Minmin Countdown Extra'; State = 'Running' }
    }
}
Assert-Equal 'Missing' $exactTaskStates['Minmin Countdown'] 'Managed task probe requires an exact task-name match'
$stopCommand = Get-Command Stop-CountdownManagedApplication -Module Countdown.InstallTransaction
Assert-Equal $true $stopCommand.Parameters.ContainsKey('FreezeFutureStarts') 'Managed application stop can freeze future scheduled-task starts'
$mutexProbeModulePath = (Join-Path $PSScriptRoot 'Countdown.InstallTransaction.psm1').Replace("'", "''")
$mutexProbeCommand = "Import-Module -Name '$mutexProbeModulePath' -Force -DisableNameChecking; try { `$childMutex = Enter-CountdownInstallTransactionMutex -TimeoutSeconds 1; Exit-CountdownInstallTransactionMutex -Mutex `$childMutex; 'unexpected acquisition' } catch { `$_.Exception.Message }"
$mutexProbeEngine = (Get-Command pwsh.exe -CommandType Application -ErrorAction Stop | Select-Object -First 1 -ExpandProperty Source)
if ($ParentTransactionMutexHeld) {
    # 办公室验收已由同一远端 pwsh 持锁：仅验证子进程确实被该锁排除，避免测试进程自锁。
    $mutexProbeOutput = & $mutexProbeEngine -NoLogo -NoProfile -NonInteractive -Command $mutexProbeCommand | Out-String
    Assert-Equal $true ($mutexProbeOutput -match '超时') 'Parent install transaction mutex excludes a child PowerShell process'
}
else {
    $transactionMutex = Enter-CountdownInstallTransactionMutex -TimeoutSeconds 1
    try {
        Assert-Equal $true ($null -ne $transactionMutex) 'Install transaction mutex can be acquired'
        $mutexProbeOutput = & $mutexProbeEngine -NoLogo -NoProfile -NonInteractive -Command $mutexProbeCommand | Out-String
        Assert-Equal $true ($mutexProbeOutput -match '超时') 'Install transaction mutex excludes a second PowerShell process'
    }
    finally {
        Exit-CountdownInstallTransactionMutex -Mutex $transactionMutex
    }
}
$settingsMutexEnterCommand = Get-Command Enter-CountdownSettingsWriteMutex -Module Countdown.Settings -ErrorAction SilentlyContinue
$settingsMutexExitCommand = Get-Command Exit-CountdownSettingsWriteMutex -Module Countdown.Settings -ErrorAction SilentlyContinue
Assert-Equal $true ($null -ne $settingsMutexEnterCommand -and $null -ne $settingsMutexExitCommand) 'Settings module exposes a cross-process write mutex'
$settingsModuleSource = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'Countdown.Settings.psm1') -Raw
Assert-Equal $false ($settingsModuleSource.Contains('[System.Security.Cryptography.SHA256]::HashData') -or $settingsModuleSource.Contains('[Convert]::ToHexString')) 'Settings mutex hashing remains compatible with PowerShell 7.0 and .NET Core 3.1'
if ($null -ne $settingsMutexEnterCommand -and $null -ne $settingsMutexExitCommand) {
    $settingsMutexProbePath = Join-Path $PSScriptRoot ("settings-mutex-probe-" + [guid]::NewGuid().ToString('N') + '.json')
    $settingsMutex = Enter-CountdownSettingsWriteMutex -Path $settingsMutexProbePath -TimeoutSeconds 1
    try {
        $settingsMutexModulePath = (Join-Path $PSScriptRoot 'Countdown.Settings.psm1').Replace("'", "''")
        $escapedSettingsMutexProbePath = $settingsMutexProbePath.Replace("'", "''")
        $settingsMutexProbeCommand = "Import-Module -Name '$settingsMutexModulePath' -Force; try { `$childMutex = Enter-CountdownSettingsWriteMutex -Path '$escapedSettingsMutexProbePath' -TimeoutSeconds 1; Exit-CountdownSettingsWriteMutex -Mutex `$childMutex; 'unexpected acquisition' } catch { `$_.Exception.Message }"
        $settingsMutexProbeOutput = & $mutexProbeEngine -NoLogo -NoProfile -NonInteractive -Command $settingsMutexProbeCommand | Out-String
        Assert-Equal $true ($settingsMutexProbeOutput -match '超时') 'Settings write mutex excludes a second PowerShell process'
    }
    finally {
        Exit-CountdownSettingsWriteMutex -Mutex $settingsMutex
    }
}

$transactionTestRoot = Join-Path $PSScriptRoot ("transaction-test-" + [guid]::NewGuid().ToString('N'))
$recoveryConflictRoot = Join-Path $PSScriptRoot ("recovery-conflict-" + [guid]::NewGuid().ToString('N'))
$committedCleanupRoot = Join-Path $PSScriptRoot ("committed-cleanup-" + [guid]::NewGuid().ToString('N'))
$reparseSafetyRoot = Join-Path $PSScriptRoot ("reparse-safety-" + [guid]::NewGuid().ToString('N'))
$reparseDirectLinkPath = $null
$reparseNestedLinkPath = $null
$transactionInstallPath = Join-Path $transactionTestRoot 'install'
$transactionDesktopPath = Join-Path $transactionTestRoot 'desktop'
$transactionLauncherPath = Join-Path $transactionDesktopPath '敏敏子倒计时.exe'
$transactionLauncherTempPath = "$transactionLauncherPath.new"
$transactionLegacyLauncherPath = Join-Path $transactionDesktopPath '敏敏子倒计时.cmd'
$transactionSuffix = [guid]::NewGuid().ToString('N')
try {
    New-Item -ItemType Directory -Path $transactionInstallPath, $transactionDesktopPath -Force | Out-Null
    $reparseTargetPath = Join-Path $reparseSafetyRoot 'protected-target'
    $reparseDirectLinkPath = Join-Path $reparseSafetyRoot 'direct-link'
    $reparseTreePath = Join-Path $reparseSafetyRoot 'transaction-tree'
    $reparseNestedLinkPath = Join-Path $reparseTreePath 'nested-link'
    New-Item -ItemType Directory -Path $reparseTargetPath, $reparseTreePath -Force | Out-Null
    [System.IO.File]::WriteAllText((Join-Path $reparseTargetPath 'protected.txt'), 'must survive', (New-Object System.Text.UTF8Encoding($false)))
    New-Item -ItemType Junction -Path $reparseDirectLinkPath -Target $reparseTargetPath | Out-Null
    New-Item -ItemType Junction -Path $reparseNestedLinkPath -Target $reparseTargetPath | Out-Null
    $directReparseRejected = $false
    try { [void] (Assert-CountdownInstallPathOnDDrive -Path (Join-Path $reparseDirectLinkPath 'protected.txt')) }
    catch { $directReparseRejected = $_.Exception.Message -match 'reparse point' }
    Assert-Equal $true $directReparseRejected 'Install path validation rejects an ancestor junction before file operations'
    $nestedReparseRejected = $false
    try { & (Get-Module Countdown.InstallTransaction) { param($path) [void] (Assert-CountdownInstallTreeHasNoReparsePoints -Path $path) } $reparseTreePath }
    catch { $nestedReparseRejected = $_.Exception.Message -match 'reparse point' }
    Assert-Equal $true $nestedReparseRejected 'Recursive transaction operations reject nested junctions before deletion or copy'
    Assert-Equal 'must survive' ([System.IO.File]::ReadAllText((Join-Path $reparseTargetPath 'protected.txt'), [System.Text.Encoding]::UTF8)) 'Reparse point rejection leaves the external target untouched'
    [System.IO.File]::WriteAllText((Join-Path $transactionInstallPath 'settings.json'), '{"before":true}', (New-Object System.Text.UTF8Encoding($false)))
    [System.IO.File]::WriteAllText((Join-Path $transactionInstallPath '敏敏子倒计时.ps1'), '# old app', (New-Object System.Text.UTF8Encoding($false)))
    [System.IO.File]::WriteAllText($transactionLauncherPath, 'old launcher', (New-Object System.Text.UTF8Encoding($false)))
    [System.IO.File]::WriteAllText($transactionLauncherTempPath, 'old staged launcher', (New-Object System.Text.UTF8Encoding($false)))
    [System.IO.File]::WriteAllText($transactionLegacyLauncherPath, 'old legacy launcher', (New-Object System.Text.UTF8Encoding($false)))
    $transactionPowerShell = (Get-Command pwsh.exe -CommandType Application -ErrorAction Stop | Select-Object -First 1 -ExpandProperty Source)
    $preparedTransaction = Start-CountdownInstallTransaction -InstallPath $transactionInstallPath -LauncherPath $transactionLauncherPath -LegacyLauncherPath $transactionLegacyLauncherPath -TaskName "Minmin Countdown Prepared $transactionSuffix" -LegacyTaskName "敏敏子倒计时 Prepared $transactionSuffix"
    Assert-Equal $false ([bool] $preparedTransaction.payloadSnapshotReady) 'Prepared transaction does not snapshot mutable files before the old application is quiesced'
    [System.IO.File]::WriteAllText((Join-Path $transactionInstallPath 'settings.json'), '{"post-prepared":true}', (New-Object System.Text.UTF8Encoding($false)))
    Undo-CountdownInstallTransaction -TransactionPath $preparedTransaction.transactionPath -PowerShell7Path $transactionPowerShell -ExpectedInstallPath $transactionInstallPath -ExpectedTaskName "Minmin Countdown Prepared $transactionSuffix" -ExpectedLegacyTaskName "敏敏子倒计时 Prepared $transactionSuffix" -ExpectedLauncherPath $transactionLauncherPath -ExpectedLegacyLauncherPath $transactionLegacyLauncherPath -ExpectedLauncherTempPath $transactionLauncherTempPath -RestoreWindowsTimeAction { param($snapshot) }
    Assert-Equal '{"post-prepared":true}' ([System.IO.File]::ReadAllText((Join-Path $transactionInstallPath 'settings.json'), [System.Text.Encoding]::UTF8)) 'Prepared rollback preserves files not yet snapshotted after quiescence'

    [System.IO.File]::WriteAllText((Join-Path $transactionInstallPath 'settings.json'), '{"quiesced":true}', (New-Object System.Text.UTF8Encoding($false)))
    $rollbackTransaction = Start-CountdownInstallTransaction -InstallPath $transactionInstallPath -LauncherPath $transactionLauncherPath -LegacyLauncherPath $transactionLegacyLauncherPath -TaskName "Minmin Countdown Test $transactionSuffix" -LegacyTaskName "敏敏子倒计时 Test $transactionSuffix"
    $rollbackTransaction = Capture-CountdownInstallPayloadSnapshot -TransactionPath $rollbackTransaction.transactionPath -ExpectedInstallPath $transactionInstallPath -ExpectedTaskName "Minmin Countdown Test $transactionSuffix" -ExpectedLegacyTaskName "敏敏子倒计时 Test $transactionSuffix" -ExpectedLauncherPath $transactionLauncherPath -ExpectedLegacyLauncherPath $transactionLegacyLauncherPath -ExpectedLauncherTempPath $transactionLauncherTempPath
    Assert-Equal $true ([bool] $rollbackTransaction.payloadSnapshotReady) 'Transaction snapshots mutable files only after the caller has quiesced the old application'
    Assert-Equal $true (Test-Path -LiteralPath $rollbackTransaction.transactionPath) 'Transaction retains backup until external acceptance or rollback'
    $deferredReceiptPath = Join-Path $transactionTestRoot 'deferred-install-receipt.json'
    $deferredReceipt = Write-CountdownDeferredInstallReceipt -Path $deferredReceiptPath -TransactionPath $rollbackTransaction.transactionPath -LaunchToken ([guid]::NewGuid().ToString('N')) -InteractiveSessionId 1
    $readDeferredReceipt = Read-CountdownDeferredInstallReceipt -Path $deferredReceiptPath
    Assert-Equal $rollbackTransaction.transactionPath $readDeferredReceipt.transactionPath 'Deferred install receipt preserves rollback transaction path'
    Assert-Equal $deferredReceipt.launchToken $readDeferredReceipt.launchToken 'Deferred install receipt preserves startup token'
    [System.IO.File]::WriteAllText($deferredReceiptPath, '{bad-json', (New-Object System.Text.UTF8Encoding($false)))
    Assert-Equal $true ($null -eq (Read-CountdownDeferredInstallReceipt -Path $deferredReceiptPath)) 'Malformed deferred install receipt is rejected'
    $failureReceiptPath = Join-Path $transactionTestRoot 'deployment-failure.json'
    $failureReceiptRecord = $null
    $failureReceiptException = [System.InvalidOperationException]::new('failure receipt sentinel')
    $failureReceiptException.Data['do-not-serialize'] = 'sentinel-secret'
    try {
        throw $failureReceiptException
    }
    catch {
        $failureReceiptRecord = $_
    }
    $failureReceipt = Write-CountdownInstallFailureReceipt `
        -Path $failureReceiptPath `
        -Stage 'replace-desktop-launcher' `
        -ErrorRecord $failureReceiptRecord `
        -TransactionStarted $true `
        -CommitState 'not-committed' `
        -RollbackAttempted $true `
        -RollbackSucceeded $true `
        -PreservedUnmanagedLaunchers $false
    $failureReceiptText = [System.IO.File]::ReadAllText($failureReceiptPath, [System.Text.Encoding]::UTF8)
    $readFailureReceipt = $failureReceiptText | ConvertFrom-Json
    Assert-Equal 2 ([int] $readFailureReceipt.schemaVersion) 'Failure receipt uses a stable schema version'
    Assert-Equal 'replace-desktop-launcher' ([string] $readFailureReceipt.stage) 'Failure receipt records the precise installer stage'
    Assert-Equal 'failure receipt sentinel' ([string] $readFailureReceipt.exceptionMessage) 'Failure receipt preserves the original exception message'
    Assert-Equal $true (-not [string]::IsNullOrWhiteSpace([string] $readFailureReceipt.fullyQualifiedErrorId)) 'Failure receipt preserves the fully qualified error ID'
    Assert-Equal $true (-not [string]::IsNullOrWhiteSpace([string] $readFailureReceipt.positionMessage)) 'Failure receipt preserves the original error position'
    Assert-Equal $true (-not [string]::IsNullOrWhiteSpace([string] $readFailureReceipt.scriptStackTrace)) 'Failure receipt preserves the original script stack'
    Assert-Equal $true ([bool] $readFailureReceipt.transactionStarted -and [string] $readFailureReceipt.commitState -eq 'not-committed' -and [bool] $readFailureReceipt.rollbackAttempted -and [bool] $readFailureReceipt.rollbackSucceeded) 'Failure receipt records transactional rollback state'
    Assert-Equal $false ([bool] $readFailureReceipt.preservedUnmanagedLaunchers) 'Failure receipt records launcher preservation state'
    Assert-Equal $true ($failureReceiptText -notmatch 'sentinel-secret') 'Failure receipt serializes selected diagnostics rather than exception data'
    Assert-Equal 'replace-desktop-launcher' ([string] $failureReceipt.stage) 'Failure receipt writer returns the persisted receipt'
    Assert-Equal $true ([System.Text.UTF8Encoding]::new($false).GetByteCount($failureReceiptText) -le 24576) 'Failure receipt remains below the remote retrieval ceiling'
    $validatedFailureReceipt = ConvertFrom-CountdownInstallFailureReceiptBytes -Bytes ([System.Text.UTF8Encoding]::new($false).GetBytes($failureReceiptText))
    Assert-Equal 'not-committed' ([string] $validatedFailureReceipt.commitState) 'Failure receipt parser accepts the writer output'
    $untrustedFailureReceipt = [ordered]@{}
    foreach ($property in $readFailureReceipt.PSObject.Properties) {
        $untrustedFailureReceipt[$property.Name] = $property.Value
    }
    $untrustedFailureReceipt['unexpected'] = 'do-not-persist'
    $untrustedFailureReceiptRejected = $false
    try {
        ConvertFrom-CountdownInstallFailureReceiptBytes -Bytes ([System.Text.UTF8Encoding]::new($false).GetBytes(($untrustedFailureReceipt | ConvertTo-Json -Depth 4))) | Out-Null
    }
    catch {
        $untrustedFailureReceiptRejected = $_.Exception.Message -match '字段数量无效|未允许字段'
    }
    Assert-Equal $true $untrustedFailureReceiptRejected 'Failure receipt parser rejects non-whitelisted fields'
    $invalidStateReceipt = [ordered]@{}
    foreach ($property in $readFailureReceipt.PSObject.Properties) {
        $invalidStateReceipt[$property.Name] = $property.Value
    }
    $invalidStateReceipt.commitState = 'unknown'
    $invalidStateReceipt.rollbackSucceeded = $true
    $invalidStateReceiptRejected = $false
    try {
        ConvertFrom-CountdownInstallFailureReceiptBytes -Bytes ([System.Text.UTF8Encoding]::new($false).GetBytes(($invalidStateReceipt | ConvertTo-Json -Depth 4))) | Out-Null
    }
    catch {
        $invalidStateReceiptRejected = $_.Exception.Message -match '不能声称回滚成功'
    }
    Assert-Equal $true $invalidStateReceiptRejected 'Failure receipt parser rejects an unknown commit state that claims rollback success'
    $invalidUtf8ReceiptRejected = $false
    try {
        ConvertFrom-CountdownInstallFailureReceiptBytes -Bytes ([byte[]] @(0xC3, 0x28)) | Out-Null
    }
    catch {
        $invalidUtf8ReceiptRejected = $_.Exception.Message -match '有效 UTF-8 JSON'
    }
    Assert-Equal $true $invalidUtf8ReceiptRejected 'Failure receipt parser rejects invalid UTF-8'
    $longFailureReceiptRecord = $null
    try {
        throw [System.InvalidOperationException]::new((('诊断' * 8000) -join ''))
    }
    catch {
        $longFailureReceiptRecord = $_
    }
    $longFailureReceiptPath = Join-Path $transactionTestRoot 'deployment-failure-long.json'
    $longFailureReceipt = Write-CountdownInstallFailureReceipt `
        -Path $longFailureReceiptPath `
        -Stage (('阶段' * 100) -join '') `
        -ErrorRecord $longFailureReceiptRecord `
        -TransactionStarted $true `
        -CommitState 'not-committed' `
        -RollbackAttempted $true `
        -RollbackSucceeded $true `
        -PreservedUnmanagedLaunchers $false
    $longFailureReceiptBytes = [System.IO.File]::ReadAllBytes($longFailureReceiptPath)
    Assert-Equal $true ($longFailureReceiptBytes.Length -le 24576) 'Oversized diagnostics remain within the 24 KiB receipt contract'
    Assert-Equal $true (([System.Text.UTF8Encoding]::new($false)).GetByteCount([string] $longFailureReceipt.stage) -le 128) 'Failure receipt truncates stage by UTF-8 bytes'
    Assert-Equal $true (([System.Text.UTF8Encoding]::new($false)).GetByteCount([string] $longFailureReceipt.exceptionMessage) -le 1024) 'Failure receipt truncates exception messages by UTF-8 bytes'
    $candidateLauncherSourcePath = Join-Path $transactionTestRoot 'candidate-launcher.exe'
    [System.IO.File]::WriteAllText($candidateLauncherSourcePath, 'new launcher', (New-Object System.Text.UTF8Encoding($false)))
    $candidateLauncherHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $candidateLauncherSourcePath).Hash
    $rollbackTransaction = Set-CountdownInstallTransactionCandidateLauncherHash -TransactionPath $rollbackTransaction.transactionPath -CandidateLauncherSha256 $candidateLauncherHash -ExpectedInstallPath $transactionInstallPath -ExpectedTaskName "Minmin Countdown Test $transactionSuffix" -ExpectedLegacyTaskName "敏敏子倒计时 Test $transactionSuffix" -ExpectedLauncherPath $transactionLauncherPath -ExpectedLegacyLauncherPath $transactionLegacyLauncherPath -ExpectedLauncherTempPath $transactionLauncherTempPath
    Assert-Equal 'launcher-candidate-staged' $rollbackTransaction.phase 'Transaction persists candidate launcher identity before replacement'
    [System.IO.File]::WriteAllText((Join-Path $transactionInstallPath 'settings.json'), '{"after":true}', (New-Object System.Text.UTF8Encoding($false)))
    Remove-Item -LiteralPath (Join-Path $transactionInstallPath '敏敏子倒计时.ps1') -Force
    [System.IO.File]::WriteAllText((Join-Path $transactionInstallPath 'new-file.txt'), 'new payload', (New-Object System.Text.UTF8Encoding($false)))
    [System.IO.File]::WriteAllText($transactionLauncherPath, 'new launcher', (New-Object System.Text.UTF8Encoding($false)))
    Remove-Item -LiteralPath $transactionLauncherTempPath -Force
    Remove-Item -LiteralPath $transactionLegacyLauncherPath -Force
    Undo-CountdownInstallTransaction -TransactionPath $rollbackTransaction.transactionPath -PowerShell7Path $transactionPowerShell -ExpectedInstallPath $transactionInstallPath -ExpectedTaskName "Minmin Countdown Test $transactionSuffix" -ExpectedLegacyTaskName "敏敏子倒计时 Test $transactionSuffix" -ExpectedLauncherPath $transactionLauncherPath -ExpectedLegacyLauncherPath $transactionLegacyLauncherPath -ExpectedLauncherTempPath $transactionLauncherTempPath -RestoreWindowsTimeAction { param($snapshot) }
    Assert-Equal '{"quiesced":true}' ([System.IO.File]::ReadAllText((Join-Path $transactionInstallPath 'settings.json'), [System.Text.Encoding]::UTF8)) 'Transaction rollback restores the stable post-quiescence install tree'
    Assert-Equal $true (Test-Path -LiteralPath (Join-Path $transactionInstallPath '敏敏子倒计时.ps1')) 'Transaction rollback restores application entry point'
    Assert-Equal $false (Test-Path -LiteralPath (Join-Path $transactionInstallPath 'new-file.txt')) 'Transaction rollback removes new install files'
    Assert-Equal 'old launcher' ([System.IO.File]::ReadAllText($transactionLauncherPath, [System.Text.Encoding]::UTF8)) 'Transaction rollback restores desktop launcher'
    Assert-Equal 'old staged launcher' ([System.IO.File]::ReadAllText($transactionLauncherTempPath, [System.Text.Encoding]::UTF8)) 'Transaction rollback restores desktop launcher staging file'
    Assert-Equal 'old legacy launcher' ([System.IO.File]::ReadAllText($transactionLegacyLauncherPath, [System.Text.Encoding]::UTF8)) 'Transaction rollback restores legacy launcher'
    Assert-Equal $false (Test-Path -LiteralPath $rollbackTransaction.transactionPath) 'Transaction rollback clears its journal only after restoration'
    $ownershipTransaction = Start-CountdownInstallTransaction -InstallPath $transactionInstallPath -LauncherPath $transactionLauncherPath -LegacyLauncherPath $transactionLegacyLauncherPath -TaskName "Minmin Countdown Ownership $transactionSuffix" -LegacyTaskName "敏敏子倒计时 Ownership $transactionSuffix"
    $ownershipTransaction = Capture-CountdownInstallPayloadSnapshot -TransactionPath $ownershipTransaction.transactionPath -ExpectedInstallPath $transactionInstallPath -ExpectedTaskName "Minmin Countdown Ownership $transactionSuffix" -ExpectedLegacyTaskName "敏敏子倒计时 Ownership $transactionSuffix" -ExpectedLauncherPath $transactionLauncherPath -ExpectedLegacyLauncherPath $transactionLegacyLauncherPath -ExpectedLauncherTempPath $transactionLauncherTempPath
    $ownershipTransaction = Set-CountdownInstallTransactionCandidateLauncherHash -TransactionPath $ownershipTransaction.transactionPath -CandidateLauncherSha256 $candidateLauncherHash -ExpectedInstallPath $transactionInstallPath -ExpectedTaskName "Minmin Countdown Ownership $transactionSuffix" -ExpectedLegacyTaskName "敏敏子倒计时 Ownership $transactionSuffix" -ExpectedLauncherPath $transactionLauncherPath -ExpectedLegacyLauncherPath $transactionLegacyLauncherPath -ExpectedLauncherTempPath $transactionLauncherTempPath
    [System.IO.File]::WriteAllText($transactionLauncherPath, 'user replacement', (New-Object System.Text.UTF8Encoding($false)))
    $ownershipRejected = $false
    try {
        Undo-CountdownInstallTransaction -TransactionPath $ownershipTransaction.transactionPath -PowerShell7Path $transactionPowerShell -ExpectedInstallPath $transactionInstallPath -ExpectedTaskName "Minmin Countdown Ownership $transactionSuffix" -ExpectedLegacyTaskName "敏敏子倒计时 Ownership $transactionSuffix" -ExpectedLauncherPath $transactionLauncherPath -ExpectedLegacyLauncherPath $transactionLegacyLauncherPath -ExpectedLauncherTempPath $transactionLauncherTempPath -RestoreWindowsTimeAction { param($snapshot) }
    }
    catch {
        $ownershipRejected = $_.Exception.Message -match '拒绝恢复 桌面启动器'
    }
    Assert-Equal $true $ownershipRejected 'Rollback refuses to overwrite a user-replaced desktop launcher after a crash'
    Assert-Equal 'user replacement' ([System.IO.File]::ReadAllText($transactionLauncherPath, [System.Text.Encoding]::UTF8)) 'Rejected launcher recovery preserves the user replacement'
    Assert-Equal $true (Test-Path -LiteralPath $ownershipTransaction.transactionPath) 'Rejected launcher recovery retains journal evidence'
    [System.IO.File]::WriteAllText((Join-Path $transactionInstallPath 'settings.json'), '{"partial-new":true}', (New-Object System.Text.UTF8Encoding($false)))
    Remove-Item -LiteralPath (Join-Path $transactionInstallPath '敏敏子倒计时.ps1') -Force
    $partialRollback = Undo-CountdownInstallTransaction -TransactionPath $ownershipTransaction.transactionPath -PowerShell7Path $transactionPowerShell -ExpectedInstallPath $transactionInstallPath -ExpectedTaskName "Minmin Countdown Ownership $transactionSuffix" -ExpectedLegacyTaskName "敏敏子倒计时 Ownership $transactionSuffix" -ExpectedLauncherPath $transactionLauncherPath -ExpectedLegacyLauncherPath $transactionLegacyLauncherPath -ExpectedLauncherTempPath $transactionLauncherTempPath -PreserveUnmanagedLaunchers -RestoreWindowsTimeAction { param($snapshot) }
    Assert-Equal $true ([bool] $partialRollback.PreservedUnmanagedLaunchers) 'Conflict rollback restores non-launcher state without overwriting a user launcher'
    Assert-Equal '{"quiesced":true}' ([System.IO.File]::ReadAllText((Join-Path $transactionInstallPath 'settings.json'), [System.Text.Encoding]::UTF8)) 'Conflict rollback restores the stable install tree'
    Assert-Equal $true (Test-Path -LiteralPath (Join-Path $transactionInstallPath '敏敏子倒计时.ps1')) 'Conflict rollback restores the application entry point'
    Assert-Equal 'user replacement' ([System.IO.File]::ReadAllText($transactionLauncherPath, [System.Text.Encoding]::UTF8)) 'Conflict rollback continues to preserve the user launcher'
    $partialRollbackJournal = [System.IO.File]::ReadAllText((Join-Path $ownershipTransaction.transactionPath 'journal.json'), [System.Text.Encoding]::UTF8) | ConvertFrom-Json
    Assert-Equal 'launcher-ownership-conflict' $partialRollbackJournal.phase 'Conflict rollback retains a dedicated journal phase for safe later recovery'
    Copy-Item -LiteralPath $candidateLauncherSourcePath -Destination $transactionLauncherPath -Force
    Undo-CountdownInstallTransaction -TransactionPath $ownershipTransaction.transactionPath -PowerShell7Path $transactionPowerShell -ExpectedInstallPath $transactionInstallPath -ExpectedTaskName "Minmin Countdown Ownership $transactionSuffix" -ExpectedLegacyTaskName "敏敏子倒计时 Ownership $transactionSuffix" -ExpectedLauncherPath $transactionLauncherPath -ExpectedLegacyLauncherPath $transactionLegacyLauncherPath -ExpectedLauncherTempPath $transactionLauncherTempPath -RestoreWindowsTimeAction { param($snapshot) }
    $recoveryConflictInstallPath = Join-Path $recoveryConflictRoot 'install'
    $recoveryConflictDesktopPath = Join-Path $recoveryConflictRoot 'desktop'
    $recoveryConflictLauncherPath = Join-Path $recoveryConflictDesktopPath '敏敏子倒计时.exe'
    $recoveryConflictLauncherTempPath = "$recoveryConflictLauncherPath.new"
    $recoveryConflictLegacyLauncherPath = Join-Path $recoveryConflictDesktopPath '敏敏子倒计时.cmd'
    New-Item -ItemType Directory -Path $recoveryConflictInstallPath, $recoveryConflictDesktopPath -Force | Out-Null
    [System.IO.File]::WriteAllText((Join-Path $recoveryConflictInstallPath 'settings.json'), '{"isolated-before":true}', (New-Object System.Text.UTF8Encoding($false)))
    [System.IO.File]::WriteAllText((Join-Path $recoveryConflictInstallPath '敏敏子倒计时.ps1'), '# isolated old app', (New-Object System.Text.UTF8Encoding($false)))
    [System.IO.File]::WriteAllText($recoveryConflictLauncherPath, 'isolated old launcher', (New-Object System.Text.UTF8Encoding($false)))
    [System.IO.File]::WriteAllText($recoveryConflictLauncherTempPath, 'isolated old staged launcher', (New-Object System.Text.UTF8Encoding($false)))
    [System.IO.File]::WriteAllText($recoveryConflictLegacyLauncherPath, 'isolated old legacy launcher', (New-Object System.Text.UTF8Encoding($false)))
    $recoveryConflictTransaction = Start-CountdownInstallTransaction -InstallPath $recoveryConflictInstallPath -LauncherPath $recoveryConflictLauncherPath -LegacyLauncherPath $recoveryConflictLegacyLauncherPath -TaskName "Minmin Countdown Recovery Conflict $transactionSuffix" -LegacyTaskName "敏敏子倒计时 Recovery Conflict $transactionSuffix"
    $recoveryConflictTransaction = Capture-CountdownInstallPayloadSnapshot -TransactionPath $recoveryConflictTransaction.transactionPath -ExpectedInstallPath $recoveryConflictInstallPath -ExpectedTaskName "Minmin Countdown Recovery Conflict $transactionSuffix" -ExpectedLegacyTaskName "敏敏子倒计时 Recovery Conflict $transactionSuffix" -ExpectedLauncherPath $recoveryConflictLauncherPath -ExpectedLegacyLauncherPath $recoveryConflictLegacyLauncherPath -ExpectedLauncherTempPath $recoveryConflictLauncherTempPath
    $recoveryConflictTransaction = Set-CountdownInstallTransactionCandidateLauncherHash -TransactionPath $recoveryConflictTransaction.transactionPath -CandidateLauncherSha256 $candidateLauncherHash -ExpectedInstallPath $recoveryConflictInstallPath -ExpectedTaskName "Minmin Countdown Recovery Conflict $transactionSuffix" -ExpectedLegacyTaskName "敏敏子倒计时 Recovery Conflict $transactionSuffix" -ExpectedLauncherPath $recoveryConflictLauncherPath -ExpectedLegacyLauncherPath $recoveryConflictLegacyLauncherPath -ExpectedLauncherTempPath $recoveryConflictLauncherTempPath
    [System.IO.File]::WriteAllText((Join-Path $recoveryConflictInstallPath 'settings.json'), '{"isolated-new":true}', (New-Object System.Text.UTF8Encoding($false)))
    [System.IO.File]::WriteAllText($recoveryConflictLauncherPath, 'isolated user replacement', (New-Object System.Text.UTF8Encoding($false)))
    $recoveryConflictPartialRollback = Undo-CountdownInstallTransaction -TransactionPath $recoveryConflictTransaction.transactionPath -PowerShell7Path $transactionPowerShell -ExpectedInstallPath $recoveryConflictInstallPath -ExpectedTaskName "Minmin Countdown Recovery Conflict $transactionSuffix" -ExpectedLegacyTaskName "敏敏子倒计时 Recovery Conflict $transactionSuffix" -ExpectedLauncherPath $recoveryConflictLauncherPath -ExpectedLegacyLauncherPath $recoveryConflictLegacyLauncherPath -ExpectedLauncherTempPath $recoveryConflictLauncherTempPath -PreserveUnmanagedLaunchers -RestoreWindowsTimeAction { param($snapshot) }
    Assert-Equal $true ([bool] $recoveryConflictPartialRollback.PreservedUnmanagedLaunchers) 'Isolated conflict setup preserves its user launcher during partial rollback'
    if ($ParentTransactionMutexHeld) {
        # 办公室验收的父进程已持有全局 mutex；子进程只能验证它被排除，不能调用 Recover 并等待同一把锁超时。
        Assert-Equal $true $true 'Parent-held transaction mutex skips recovery execution that requires the parent lock'
    }
    else {
        $recoveryConflictStatus = Recover-CountdownInstallTransactions -InstallPath $recoveryConflictInstallPath -PowerShell7Path $transactionPowerShell -ExpectedTaskName "Minmin Countdown Recovery Conflict $transactionSuffix" -ExpectedLegacyTaskName "敏敏子倒计时 Recovery Conflict $transactionSuffix" -ExpectedLauncherPath $recoveryConflictLauncherPath -ExpectedLegacyLauncherPath $recoveryConflictLegacyLauncherPath -ExpectedLauncherTempPath $recoveryConflictLauncherTempPath
        Assert-Equal 0 @($recoveryConflictStatus.RecoveredTransactionPaths).Count 'Recovery does not falsely report a user-launcher conflict as complete'
        Assert-Equal 1 @($recoveryConflictStatus.UnresolvedLauncherOwnershipConflicts).Count 'Recovery returns a structured unresolved launcher conflict'
        Assert-Equal 'isolated user replacement' ([System.IO.File]::ReadAllText($recoveryConflictLauncherPath, [System.Text.Encoding]::UTF8)) 'Recovery status probing does not overwrite the isolated user launcher'
    }
    $strictBooleanTransaction = Start-CountdownInstallTransaction -InstallPath $transactionInstallPath -LauncherPath $transactionLauncherPath -LegacyLauncherPath $transactionLegacyLauncherPath -TaskName "Minmin Countdown Strict Boolean $transactionSuffix" -LegacyTaskName "敏敏子倒计时 Strict Boolean $transactionSuffix"
    $strictBooleanJournalPath = Join-Path $strictBooleanTransaction.transactionPath 'journal.json'
    $strictBooleanJournal = [System.IO.File]::ReadAllText($strictBooleanJournalPath, [System.Text.Encoding]::UTF8) | ConvertFrom-Json
    $strictBooleanJournal.payloadSnapshotReady = 'false'
    [System.IO.File]::WriteAllText($strictBooleanJournalPath, ($strictBooleanJournal | ConvertTo-Json -Depth 8), (New-Object System.Text.UTF8Encoding($false)))
    $strictBooleanRejected = $false
    try {
        Undo-CountdownInstallTransaction -TransactionPath $strictBooleanTransaction.transactionPath -PowerShell7Path $transactionPowerShell -ExpectedInstallPath $transactionInstallPath -ExpectedTaskName "Minmin Countdown Strict Boolean $transactionSuffix" -ExpectedLegacyTaskName "敏敏子倒计时 Strict Boolean $transactionSuffix" -ExpectedLauncherPath $transactionLauncherPath -ExpectedLegacyLauncherPath $transactionLegacyLauncherPath -ExpectedLauncherTempPath $transactionLauncherTempPath -RestoreWindowsTimeAction { param($snapshot) }
    }
    catch {
        $strictBooleanRejected = $_.Exception.Message -match '严格布尔字段 payloadSnapshotReady'
    }
    Assert-Equal $true $strictBooleanRejected 'Rollback rejects a string payloadSnapshotReady before deleting any files'
    Assert-Equal '{"quiesced":true}' ([System.IO.File]::ReadAllText((Join-Path $transactionInstallPath 'settings.json'), [System.Text.Encoding]::UTF8)) 'Malformed prepared journal leaves current install tree intact'
    $missingBackupTransaction = Start-CountdownInstallTransaction -InstallPath $transactionInstallPath -LauncherPath $transactionLauncherPath -LegacyLauncherPath $transactionLegacyLauncherPath -TaskName "Minmin Countdown Missing Backup $transactionSuffix" -LegacyTaskName "敏敏子倒计时 Missing Backup $transactionSuffix"
    $missingBackupTransaction = Capture-CountdownInstallPayloadSnapshot -TransactionPath $missingBackupTransaction.transactionPath -ExpectedInstallPath $transactionInstallPath -ExpectedTaskName "Minmin Countdown Missing Backup $transactionSuffix" -ExpectedLegacyTaskName "敏敏子倒计时 Missing Backup $transactionSuffix" -ExpectedLauncherPath $transactionLauncherPath -ExpectedLegacyLauncherPath $transactionLegacyLauncherPath -ExpectedLauncherTempPath $transactionLauncherTempPath
    [System.IO.File]::WriteAllText((Join-Path $transactionInstallPath 'settings.json'), '{"must-not-delete":true}', (New-Object System.Text.UTF8Encoding($false)))
    Remove-Item -LiteralPath $missingBackupTransaction.backupInstallPath -Recurse -Force
    $missingBackupRejected = $false
    try {
        Undo-CountdownInstallTransaction -TransactionPath $missingBackupTransaction.transactionPath -PowerShell7Path $transactionPowerShell -ExpectedInstallPath $transactionInstallPath -ExpectedTaskName "Minmin Countdown Missing Backup $transactionSuffix" -ExpectedLegacyTaskName "敏敏子倒计时 Missing Backup $transactionSuffix" -ExpectedLauncherPath $transactionLauncherPath -ExpectedLegacyLauncherPath $transactionLegacyLauncherPath -ExpectedLauncherTempPath $transactionLauncherTempPath -RestoreWindowsTimeAction { param($snapshot) }
    }
    catch {
        $missingBackupRejected = $_.Exception.Message -match '缺少安装目录备份'
    }
    Assert-Equal $true $missingBackupRejected 'Rollback verifies install backup before deleting the current install tree'
    Assert-Equal '{"must-not-delete":true}' ([System.IO.File]::ReadAllText((Join-Path $transactionInstallPath 'settings.json'), [System.Text.Encoding]::UTF8)) 'Missing backup leaves current install tree intact'
    $invalidTaskXmlTransaction = Start-CountdownInstallTransaction -InstallPath $transactionInstallPath -LauncherPath $transactionLauncherPath -LegacyLauncherPath $transactionLegacyLauncherPath -TaskName "Minmin Countdown Invalid XML $transactionSuffix" -LegacyTaskName "敏敏子倒计时 Invalid XML $transactionSuffix"
    $invalidTaskXmlJournalPath = Join-Path $invalidTaskXmlTransaction.transactionPath 'journal.json'
    $invalidTaskXmlJournal = [System.IO.File]::ReadAllText($invalidTaskXmlJournalPath, [System.Text.Encoding]::UTF8) | ConvertFrom-Json
    $invalidTaskXmlJournal.currentTask.Exists = $true
    $invalidTaskXmlJournal.currentTask.State = 'Ready'
    [System.IO.File]::WriteAllText([string] $invalidTaskXmlJournal.currentTask.XmlPath, '<Task>', (New-Object System.Text.UTF8Encoding($false)))
    [System.IO.File]::WriteAllText($invalidTaskXmlJournalPath, ($invalidTaskXmlJournal | ConvertTo-Json -Depth 8), (New-Object System.Text.UTF8Encoding($false)))
    [System.IO.File]::WriteAllText((Join-Path $transactionInstallPath 'settings.json'), '{"task-xml-must-not-delete":true}', (New-Object System.Text.UTF8Encoding($false)))
    $invalidTaskXmlRejected = $false
    try {
        Undo-CountdownInstallTransaction -TransactionPath $invalidTaskXmlTransaction.transactionPath -PowerShell7Path $transactionPowerShell -ExpectedInstallPath $transactionInstallPath -ExpectedTaskName "Minmin Countdown Invalid XML $transactionSuffix" -ExpectedLegacyTaskName "敏敏子倒计时 Invalid XML $transactionSuffix" -ExpectedLauncherPath $transactionLauncherPath -ExpectedLegacyLauncherPath $transactionLegacyLauncherPath -ExpectedLauncherTempPath $transactionLauncherTempPath -RestoreWindowsTimeAction { param($snapshot) }
    }
    catch {
        $invalidTaskXmlRejected = $_.Exception.Message -match 'currentTask XML 无法解析'
    }
    Assert-Equal $true $invalidTaskXmlRejected 'Rollback rejects a truncated task XML before any destructive action'
    Assert-Equal '{"task-xml-must-not-delete":true}' ([System.IO.File]::ReadAllText((Join-Path $transactionInstallPath 'settings.json'), [System.Text.Encoding]::UTF8)) 'Invalid task XML leaves the current install tree intact'
    Assert-Equal $true (Test-Path -LiteralPath $invalidTaskXmlTransaction.transactionPath) 'Invalid task XML keeps journal evidence for manual repair'
    $identityTransaction = Start-CountdownInstallTransaction -InstallPath $transactionInstallPath -LauncherPath $transactionLauncherPath -LegacyLauncherPath $transactionLegacyLauncherPath -TaskName "Minmin Countdown Identity $transactionSuffix" -LegacyTaskName "敏敏子倒计时 Identity $transactionSuffix"
    $identityRejected = $false
    try {
        Undo-CountdownInstallTransaction -TransactionPath $identityTransaction.transactionPath -PowerShell7Path $transactionPowerShell -ExpectedInstallPath $transactionInstallPath -ExpectedTaskName 'unexpected task' -ExpectedLegacyTaskName "敏敏子倒计时 Identity $transactionSuffix" -ExpectedLauncherPath $transactionLauncherPath -ExpectedLegacyLauncherPath $transactionLegacyLauncherPath -ExpectedLauncherTempPath $transactionLauncherTempPath -RestoreWindowsTimeAction { param($snapshot) }
    }
    catch {
        $identityRejected = $_.Exception.Message -match 'taskName 与当前安装任务不一致'
    }
    Assert-Equal $true $identityRejected 'Transaction rollback binds its journal to the expected task identities'
    $launcherIdentityRejected = $false
    try {
        Undo-CountdownInstallTransaction -TransactionPath $identityTransaction.transactionPath -PowerShell7Path $transactionPowerShell -ExpectedInstallPath $transactionInstallPath -ExpectedTaskName "Minmin Countdown Identity $transactionSuffix" -ExpectedLegacyTaskName "敏敏子倒计时 Identity $transactionSuffix" -ExpectedLauncherPath (Join-Path $transactionDesktopPath 'other-launcher.exe') -ExpectedLegacyLauncherPath $transactionLegacyLauncherPath -ExpectedLauncherTempPath $transactionLauncherTempPath -RestoreWindowsTimeAction { param($snapshot) }
    }
    catch {
        $launcherIdentityRejected = $_.Exception.Message -match 'launcherPath 与当前安装目标不一致'
    }
    Assert-Equal $true $launcherIdentityRejected 'Transaction rollback binds its journal to the expected launcher paths'
    Undo-CountdownInstallTransaction -TransactionPath $identityTransaction.transactionPath -PowerShell7Path $transactionPowerShell -ExpectedInstallPath $transactionInstallPath -ExpectedTaskName "Minmin Countdown Identity $transactionSuffix" -ExpectedLegacyTaskName "敏敏子倒计时 Identity $transactionSuffix" -ExpectedLauncherPath $transactionLauncherPath -ExpectedLegacyLauncherPath $transactionLegacyLauncherPath -ExpectedLauncherTempPath $transactionLauncherTempPath -RestoreWindowsTimeAction { param($snapshot) }
    $commitTransaction = Start-CountdownInstallTransaction -InstallPath $transactionInstallPath -LauncherPath $transactionLauncherPath -LegacyLauncherPath $transactionLegacyLauncherPath -TaskName "Minmin Countdown Commit $transactionSuffix" -LegacyTaskName "敏敏子倒计时 Commit $transactionSuffix"
    $commitTransaction = Capture-CountdownInstallPayloadSnapshot -TransactionPath $commitTransaction.transactionPath -ExpectedInstallPath $transactionInstallPath -ExpectedTaskName "Minmin Countdown Commit $transactionSuffix" -ExpectedLegacyTaskName "敏敏子倒计时 Commit $transactionSuffix" -ExpectedLauncherPath $transactionLauncherPath -ExpectedLegacyLauncherPath $transactionLegacyLauncherPath -ExpectedLauncherTempPath $transactionLauncherTempPath
    $commitTransaction = Set-CountdownInstallTransactionCandidateLauncherHash -TransactionPath $commitTransaction.transactionPath -CandidateLauncherSha256 $candidateLauncherHash -ExpectedInstallPath $transactionInstallPath -ExpectedTaskName "Minmin Countdown Commit $transactionSuffix" -ExpectedLegacyTaskName "敏敏子倒计时 Commit $transactionSuffix" -ExpectedLauncherPath $transactionLauncherPath -ExpectedLegacyLauncherPath $transactionLegacyLauncherPath -ExpectedLauncherTempPath $transactionLauncherTempPath
    Copy-Item -LiteralPath $candidateLauncherSourcePath -Destination $transactionLauncherPath -Force
    Complete-CountdownInstallTransaction -TransactionPath $commitTransaction.transactionPath -ExpectedInstallPath $transactionInstallPath -ExpectedTaskName "Minmin Countdown Commit $transactionSuffix" -ExpectedLegacyTaskName "敏敏子倒计时 Commit $transactionSuffix" -ExpectedLauncherPath $transactionLauncherPath -ExpectedLegacyLauncherPath $transactionLegacyLauncherPath -ExpectedLauncherTempPath $transactionLauncherTempPath
    Assert-Equal $true (Test-Path -LiteralPath $commitTransaction.transactionPath) 'Transaction commit retains rollback evidence until application acceptance acknowledgement'
    $committedJournal = Get-Content -LiteralPath (Join-Path $commitTransaction.transactionPath 'journal.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    Assert-Equal 'accepted' ([string] $committedJournal.phase) 'Transaction remains rollbackable while waiting for application acknowledgement'
    Remove-CountdownCommittedInstallTransaction -TransactionPath $commitTransaction.transactionPath -ExpectedInstallPath $transactionInstallPath -ExpectedTaskName "Minmin Countdown Commit $transactionSuffix" -ExpectedLegacyTaskName "敏敏子倒计时 Commit $transactionSuffix" -ExpectedLauncherPath $transactionLauncherPath -ExpectedLegacyLauncherPath $transactionLegacyLauncherPath -ExpectedLauncherTempPath $transactionLauncherTempPath
    Assert-Equal $false (Test-Path -LiteralPath $commitTransaction.transactionPath) 'Committed transaction cleanup removes backup only after acknowledgement'
    $committedCleanupInstallPath = Join-Path $committedCleanupRoot 'install'
    $committedCleanupDesktopPath = Join-Path $committedCleanupRoot 'desktop'
    $committedCleanupLauncherPath = Join-Path $committedCleanupDesktopPath '敏敏子倒计时.exe'
    $committedCleanupLauncherTempPath = "$committedCleanupLauncherPath.new"
    $committedCleanupLegacyLauncherPath = Join-Path $committedCleanupDesktopPath '敏敏子倒计时.cmd'
    New-Item -ItemType Directory -Path $committedCleanupInstallPath, $committedCleanupDesktopPath -Force | Out-Null
    [System.IO.File]::WriteAllText((Join-Path $committedCleanupInstallPath 'settings.json'), '{"committed-before":true}', (New-Object System.Text.UTF8Encoding($false)))
    [System.IO.File]::WriteAllText($committedCleanupLauncherPath, 'committed old launcher', (New-Object System.Text.UTF8Encoding($false)))
    $committedCleanupTransaction = Start-CountdownInstallTransaction -InstallPath $committedCleanupInstallPath -LauncherPath $committedCleanupLauncherPath -LegacyLauncherPath $committedCleanupLegacyLauncherPath -TaskName "Minmin Countdown Committed Cleanup $transactionSuffix" -LegacyTaskName "敏敏子倒计时 Committed Cleanup $transactionSuffix"
    $committedCleanupTransaction = Capture-CountdownInstallPayloadSnapshot -TransactionPath $committedCleanupTransaction.transactionPath -ExpectedInstallPath $committedCleanupInstallPath -ExpectedTaskName "Minmin Countdown Committed Cleanup $transactionSuffix" -ExpectedLegacyTaskName "敏敏子倒计时 Committed Cleanup $transactionSuffix" -ExpectedLauncherPath $committedCleanupLauncherPath -ExpectedLegacyLauncherPath $committedCleanupLegacyLauncherPath -ExpectedLauncherTempPath $committedCleanupLauncherTempPath
    $committedCleanupTransaction = Set-CountdownInstallTransactionCandidateLauncherHash -TransactionPath $committedCleanupTransaction.transactionPath -CandidateLauncherSha256 $candidateLauncherHash -ExpectedInstallPath $committedCleanupInstallPath -ExpectedTaskName "Minmin Countdown Committed Cleanup $transactionSuffix" -ExpectedLegacyTaskName "敏敏子倒计时 Committed Cleanup $transactionSuffix" -ExpectedLauncherPath $committedCleanupLauncherPath -ExpectedLegacyLauncherPath $committedCleanupLegacyLauncherPath -ExpectedLauncherTempPath $committedCleanupLauncherTempPath
    Copy-Item -LiteralPath $candidateLauncherSourcePath -Destination $committedCleanupLauncherPath -Force
    [System.IO.File]::WriteAllText((Join-Path $committedCleanupInstallPath 'settings.json'), '{"committed-new":true}', (New-Object System.Text.UTF8Encoding($false)))
    Complete-CountdownInstallTransaction -TransactionPath $committedCleanupTransaction.transactionPath -ExpectedInstallPath $committedCleanupInstallPath -ExpectedTaskName "Minmin Countdown Committed Cleanup $transactionSuffix" -ExpectedLegacyTaskName "敏敏子倒计时 Committed Cleanup $transactionSuffix" -ExpectedLauncherPath $committedCleanupLauncherPath -ExpectedLegacyLauncherPath $committedCleanupLegacyLauncherPath -ExpectedLauncherTempPath $committedCleanupLauncherTempPath
    $committedTransactionLeaf = Split-Path -Leaf $committedCleanupTransaction.transactionPath
    $committedTransactionParent = Split-Path -Parent $committedCleanupTransaction.transactionPath
    Remove-CountdownCommittedInstallTransaction -TransactionPath $committedCleanupTransaction.transactionPath -ExpectedInstallPath $committedCleanupInstallPath -ExpectedTaskName "Minmin Countdown Committed Cleanup $transactionSuffix" -ExpectedLegacyTaskName "敏敏子倒计时 Committed Cleanup $transactionSuffix" -ExpectedLauncherPath $committedCleanupLauncherPath -ExpectedLegacyLauncherPath $committedCleanupLegacyLauncherPath -ExpectedLauncherTempPath $committedCleanupLauncherTempPath -CleanupAction { param($path) Remove-Item -LiteralPath (Join-Path $path 'journal.json') -Force; throw "模拟 journal 已删除后的 committed tombstone 清理失败：$path" }
    Assert-Equal $false (Test-Path -LiteralPath $committedCleanupTransaction.transactionPath) 'Committed cleanup atomically renames the transaction before recursive deletion'
    $committedTombstones = @(Get-ChildItem -LiteralPath $committedTransactionParent -Directory -Force | Where-Object { $_.Name -like "$committedTransactionLeaf.committed-*" })
    Assert-Equal 1 $committedTombstones.Count 'Committed cleanup failure retains one tombstone directory'
    Assert-Equal $false (Test-Path -LiteralPath (Join-Path $committedTombstones[0].FullName 'journal.json')) 'Committed cleanup test reproduces a partial deletion that already removed journal.json'
    $committedMarkers = @(Get-ChildItem -LiteralPath $committedTransactionParent -File -Filter "$committedTransactionLeaf.committed-*.marker.json" -Force)
    Assert-Equal 1 $committedMarkers.Count 'External committed cleanup marker survives partial tombstone deletion'
    if (-not $ParentTransactionMutexHeld) {
        $committedRecoveryStatus = Recover-CountdownInstallTransactions -InstallPath $committedCleanupInstallPath -PowerShell7Path $transactionPowerShell -ExpectedTaskName "Minmin Countdown Committed Cleanup $transactionSuffix" -ExpectedLegacyTaskName "敏敏子倒计时 Committed Cleanup $transactionSuffix" -ExpectedLauncherPath $committedCleanupLauncherPath -ExpectedLegacyLauncherPath $committedCleanupLegacyLauncherPath -ExpectedLauncherTempPath $committedCleanupLauncherTempPath -CommittedCleanupAction { param($path) throw "模拟 committed tombstone 重试仍失败：$path" }
        Assert-Equal 0 @($committedRecoveryStatus.RecoveredTransactionPaths).Count 'Recovery never rolls back a committed tombstone'
        Assert-Equal 1 @($committedRecoveryStatus.UnresolvedCommittedTransactions).Count 'Recovery reports a committed tombstone while cleanup remains blocked'
        Assert-Equal '{"committed-new":true}' ([System.IO.File]::ReadAllText((Join-Path $committedCleanupInstallPath 'settings.json'), [System.Text.Encoding]::UTF8)) 'Recovery preserves the accepted install after committed cleanup failure'
        $committedRetryStatus = Recover-CountdownInstallTransactions -InstallPath $committedCleanupInstallPath -PowerShell7Path $transactionPowerShell -ExpectedTaskName "Minmin Countdown Committed Cleanup $transactionSuffix" -ExpectedLegacyTaskName "敏敏子倒计时 Committed Cleanup $transactionSuffix" -ExpectedLauncherPath $committedCleanupLauncherPath -ExpectedLegacyLauncherPath $committedCleanupLegacyLauncherPath -ExpectedLauncherTempPath $committedCleanupLauncherTempPath
        Assert-Equal 0 @($committedRetryStatus.UnresolvedCommittedTransactions).Count 'Recovery automatically clears a committed tombstone after the transient cleanup failure ends'
        Assert-Equal $false (Test-Path -LiteralPath $committedTombstones[0].FullName) 'Committed tombstone retry removes stale transaction evidence without rollback'
        Assert-Equal $false (Test-Path -LiteralPath $committedMarkers[0].FullName) 'Committed tombstone retry removes the external marker only after cleanup succeeds'

        $moveFailureTransaction = Start-CountdownInstallTransaction -InstallPath $committedCleanupInstallPath -LauncherPath $committedCleanupLauncherPath -LegacyLauncherPath $committedCleanupLegacyLauncherPath -TaskName "Minmin Countdown Move Failure $transactionSuffix" -LegacyTaskName "敏敏子倒计时 Move Failure $transactionSuffix"
        $moveFailureTransaction = Capture-CountdownInstallPayloadSnapshot -TransactionPath $moveFailureTransaction.transactionPath -ExpectedInstallPath $committedCleanupInstallPath -ExpectedTaskName "Minmin Countdown Move Failure $transactionSuffix" -ExpectedLegacyTaskName "敏敏子倒计时 Move Failure $transactionSuffix" -ExpectedLauncherPath $committedCleanupLauncherPath -ExpectedLegacyLauncherPath $committedCleanupLegacyLauncherPath -ExpectedLauncherTempPath $committedCleanupLauncherTempPath
        $moveFailureTransaction = Set-CountdownInstallTransactionCandidateLauncherHash -TransactionPath $moveFailureTransaction.transactionPath -CandidateLauncherSha256 $candidateLauncherHash -ExpectedInstallPath $committedCleanupInstallPath -ExpectedTaskName "Minmin Countdown Move Failure $transactionSuffix" -ExpectedLegacyTaskName "敏敏子倒计时 Move Failure $transactionSuffix" -ExpectedLauncherPath $committedCleanupLauncherPath -ExpectedLegacyLauncherPath $committedCleanupLegacyLauncherPath -ExpectedLauncherTempPath $committedCleanupLauncherTempPath
        Copy-Item -LiteralPath $candidateLauncherSourcePath -Destination $committedCleanupLauncherPath -Force
        Complete-CountdownInstallTransaction -TransactionPath $moveFailureTransaction.transactionPath -ExpectedInstallPath $committedCleanupInstallPath -ExpectedTaskName "Minmin Countdown Move Failure $transactionSuffix" -ExpectedLegacyTaskName "敏敏子倒计时 Move Failure $transactionSuffix" -ExpectedLauncherPath $committedCleanupLauncherPath -ExpectedLegacyLauncherPath $committedCleanupLegacyLauncherPath -ExpectedLauncherTempPath $committedCleanupLauncherTempPath
        Remove-CountdownCommittedInstallTransaction -TransactionPath $moveFailureTransaction.transactionPath -ExpectedInstallPath $committedCleanupInstallPath -ExpectedTaskName "Minmin Countdown Move Failure $transactionSuffix" -ExpectedLegacyTaskName "敏敏子倒计时 Move Failure $transactionSuffix" -ExpectedLauncherPath $committedCleanupLauncherPath -ExpectedLegacyLauncherPath $committedCleanupLegacyLauncherPath -ExpectedLauncherTempPath $committedCleanupLauncherTempPath -MoveAction { param($source, $destination) throw "模拟 committed 后目录重命名失败：$source -> $destination" }
        $moveFailureJournal = [System.IO.File]::ReadAllText((Join-Path $moveFailureTransaction.transactionPath 'journal.json'), [System.Text.Encoding]::UTF8) | ConvertFrom-Json
        Assert-Equal 'committed' ([string] $moveFailureJournal.phase) 'Move failure after commit leaves the original transaction permanently committed'
        $committedUndoRejected = $false
        try { Undo-CountdownInstallTransaction -TransactionPath $moveFailureTransaction.transactionPath -PowerShell7Path $transactionPowerShell -ExpectedInstallPath $committedCleanupInstallPath -ExpectedTaskName "Minmin Countdown Move Failure $transactionSuffix" -ExpectedLegacyTaskName "敏敏子倒计时 Move Failure $transactionSuffix" -ExpectedLauncherPath $committedCleanupLauncherPath -ExpectedLegacyLauncherPath $committedCleanupLegacyLauncherPath -ExpectedLauncherTempPath $committedCleanupLauncherTempPath -RestoreWindowsTimeAction { param($snapshot) } }
        catch { $committedUndoRejected = $_.Exception.Message -match '禁止重新进入回滚' }
        Assert-Equal $true $committedUndoRejected 'Undo rejects a committed transaction even when tombstone rename failed'
        $moveFailureCleanupAttempts = [System.Collections.Generic.List[string]]::new()
        $moveFailureRecovery = Recover-CountdownInstallTransactions -InstallPath $committedCleanupInstallPath -PowerShell7Path $transactionPowerShell -ExpectedTaskName "Minmin Countdown Move Failure $transactionSuffix" -ExpectedLegacyTaskName "敏敏子倒计时 Move Failure $transactionSuffix" -ExpectedLauncherPath $committedCleanupLauncherPath -ExpectedLegacyLauncherPath $committedCleanupLegacyLauncherPath -ExpectedLauncherTempPath $committedCleanupLauncherTempPath -CommittedCleanupAction {
            param($path)
            [void] $moveFailureCleanupAttempts.Add($path)
            if ($moveFailureCleanupAttempts.Count -eq 1) { throw "模拟同一次 recovery 的首次清理失败：$path" }
            Remove-Item -LiteralPath $path -Recurse -Force -ErrorAction Stop
        }
        Assert-Equal 2 $moveFailureCleanupAttempts.Count 'Recovery retries the same committed transaction through its sidecar after a transient first failure'
        Assert-Equal 0 @($moveFailureRecovery.UnresolvedCommittedTransactions).Count 'Recovery cleans a committed transaction left at its original path after move failure'
        Assert-Equal $false (Test-Path -LiteralPath $moveFailureTransaction.transactionPath) 'Move-failed committed transaction is cleanup-only and never rolled back'
    }
    $commitReplacementTransaction = Start-CountdownInstallTransaction -InstallPath $transactionInstallPath -LauncherPath $transactionLauncherPath -LegacyLauncherPath $transactionLegacyLauncherPath -TaskName "Minmin Countdown Commit Replacement $transactionSuffix" -LegacyTaskName "敏敏子倒计时 Commit Replacement $transactionSuffix"
    $commitReplacementTransaction = Capture-CountdownInstallPayloadSnapshot -TransactionPath $commitReplacementTransaction.transactionPath -ExpectedInstallPath $transactionInstallPath -ExpectedTaskName "Minmin Countdown Commit Replacement $transactionSuffix" -ExpectedLegacyTaskName "敏敏子倒计时 Commit Replacement $transactionSuffix" -ExpectedLauncherPath $transactionLauncherPath -ExpectedLegacyLauncherPath $transactionLegacyLauncherPath -ExpectedLauncherTempPath $transactionLauncherTempPath
    $commitReplacementTransaction = Set-CountdownInstallTransactionCandidateLauncherHash -TransactionPath $commitReplacementTransaction.transactionPath -CandidateLauncherSha256 $candidateLauncherHash -ExpectedInstallPath $transactionInstallPath -ExpectedTaskName "Minmin Countdown Commit Replacement $transactionSuffix" -ExpectedLegacyTaskName "敏敏子倒计时 Commit Replacement $transactionSuffix" -ExpectedLauncherPath $transactionLauncherPath -ExpectedLegacyLauncherPath $transactionLegacyLauncherPath -ExpectedLauncherTempPath $transactionLauncherTempPath
    [System.IO.File]::WriteAllText($transactionLauncherPath, 'user replacement before commit', (New-Object System.Text.UTF8Encoding($false)))
    $commitReplacementRejected = $false
    try {
        Complete-CountdownInstallTransaction -TransactionPath $commitReplacementTransaction.transactionPath -ExpectedInstallPath $transactionInstallPath -ExpectedTaskName "Minmin Countdown Commit Replacement $transactionSuffix" -ExpectedLegacyTaskName "敏敏子倒计时 Commit Replacement $transactionSuffix" -ExpectedLauncherPath $transactionLauncherPath -ExpectedLegacyLauncherPath $transactionLegacyLauncherPath -ExpectedLauncherTempPath $transactionLauncherTempPath
    }
    catch {
        $commitReplacementRejected = $_.Exception.Message -match '候选桌面启动器在提交前已被替换'
    }
    Assert-Equal $true $commitReplacementRejected 'Transaction commit rejects a launcher replaced after acceptance'
    Assert-Equal $true (Test-Path -LiteralPath $commitReplacementTransaction.transactionPath) 'Rejected transaction commit retains rollback evidence'
    $commitReplacementRollback = Undo-CountdownInstallTransaction -TransactionPath $commitReplacementTransaction.transactionPath -PowerShell7Path $transactionPowerShell -ExpectedInstallPath $transactionInstallPath -ExpectedTaskName "Minmin Countdown Commit Replacement $transactionSuffix" -ExpectedLegacyTaskName "敏敏子倒计时 Commit Replacement $transactionSuffix" -ExpectedLauncherPath $transactionLauncherPath -ExpectedLegacyLauncherPath $transactionLegacyLauncherPath -ExpectedLauncherTempPath $transactionLauncherTempPath -PreserveUnmanagedLaunchers -RestoreWindowsTimeAction { param($snapshot) }
    Assert-Equal $true ([bool] $commitReplacementRollback.PreservedUnmanagedLaunchers) 'Rejected transaction commit can preserve the user-replaced launcher during rollback'
    Assert-Equal 'user replacement before commit' ([System.IO.File]::ReadAllText($transactionLauncherPath, [System.Text.Encoding]::UTF8)) 'Rejected transaction commit does not overwrite the user-replaced launcher'
    $tamperedTransaction = Start-CountdownInstallTransaction -InstallPath $transactionInstallPath -LauncherPath $transactionLauncherPath -LegacyLauncherPath $transactionLegacyLauncherPath -TaskName "Minmin Countdown Tamper $transactionSuffix" -LegacyTaskName "敏敏子倒计时 Tamper $transactionSuffix"
    $tamperedJournalPath = Join-Path $tamperedTransaction.transactionPath 'journal.json'
    $tamperedJournal = [System.IO.File]::ReadAllText($tamperedJournalPath, [System.Text.Encoding]::UTF8) | ConvertFrom-Json
    $tamperedJournal.backupLauncherPath = 'D:\outside-transaction-backup.exe'
    [System.IO.File]::WriteAllText($tamperedJournalPath, ($tamperedJournal | ConvertTo-Json -Depth 8), (New-Object System.Text.UTF8Encoding($false)))
    $tamperedJournalRejected = $false
    try {
        Undo-CountdownInstallTransaction -TransactionPath $tamperedTransaction.transactionPath -PowerShell7Path (Get-Command pwsh.exe -CommandType Application -ErrorAction Stop | Select-Object -First 1 -ExpandProperty Source) -RestoreWindowsTimeAction { param($snapshot) }
    }
    catch {
        $tamperedJournalRejected = $_.Exception.Message -match '不在事务目录内'
    }
    Assert-Equal $true $tamperedJournalRejected 'Transaction rollback rejects backup paths outside its journal directory'
}
finally {
    foreach ($junctionPath in @($reparseDirectLinkPath, $reparseNestedLinkPath)) {
        if (-not [string]::IsNullOrWhiteSpace([string] $junctionPath) -and (Test-Path -LiteralPath $junctionPath)) {
            Remove-Item -LiteralPath $junctionPath -Force -ErrorAction SilentlyContinue
        }
    }
    Remove-Item -LiteralPath $transactionTestRoot -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $recoveryConflictRoot -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $committedCleanupRoot -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $reparseSafetyRoot -Recurse -Force -ErrorAction SilentlyContinue
}

$installerValidationEngine = (Get-Command pwsh.exe -CommandType Application -ErrorAction Stop | Select-Object -First 1).Source
$installerValidationJson = & $installerValidationEngine -NoLogo -NoProfile -NonInteractive -File (Join-Path $PSScriptRoot 'Install-Countdown.ps1') -ValidateOnly | Out-String
if ($LASTEXITCODE -ne 0) { throw "Installer validation failed: $installerValidationJson" }
$installerValidation = $installerValidationJson | ConvertFrom-Json
Assert-Equal $true $installerValidation.Valid 'Installer package validation succeeds'
Assert-Equal $releaseContract.IntegrityHashes.Count $installerValidation.VerifiedFiles 'Installer reports the canonical integrity count'
Assert-Equal (@($releaseContract.OfficialCharacterAssets) -join ',') (@($installerValidation.OfficialCharacterAssets) -join ',') 'Installer exposes canonical official character assets'
Assert-Equal (@($releaseContract.FloatingPlushAssets) -join ',') (@($installerValidation.FloatingPlushAssets) -join ',') 'Installer exposes canonical floating plush assets'
Assert-Equal (@($releaseContract.CurrentFloatingRuntimeAssets) -join ',') (@($installerValidation.CurrentFloatingRuntimeAssets) -join ',') 'Installer exposes canonical current floating runtime assets'
Assert-Equal (@($releaseContract.LegacyFloatingRegressionAssets) -join ',') (@($installerValidation.LegacyFloatingRegressionAssets) -join ',') 'Installer exposes canonical floating regression fixtures'
Assert-Equal (@($releaseContract.CompatibilityFixtureFiles) -join ',') (@($installerValidation.CompatibilityFixtureFiles) -join ',') 'Installer exposes canonical compatibility fixtures'
Assert-Equal (@($releaseContract.RetiredRuntimeFiles) -join ',') (@($installerValidation.RetiredRuntimeFiles) -join ',') 'Installer exposes canonical retired runtime files'
Assert-Equal $true $installerValidation.Transactional 'Installer exposes transactional install boundary'
Assert-Equal $true $installerValidation.DeferredCommitSupported 'Installer exposes deferred commit for office acceptance'
$installerCommand = Get-Command (Join-Path $PSScriptRoot 'Install-Countdown.ps1')
Assert-Equal $false $installerCommand.Parameters.ContainsKey('TransactionMutexHeld') 'Installer does not expose a lock-bypass switch'
Assert-Equal $true $installerCommand.Parameters.ContainsKey('ParentTransactionMutexHeld') 'Installer exposes a parent-lock test context only for deferred office acceptance'
Assert-Equal $true $installerCommand.Parameters.ContainsKey('ExpectedInteractiveUser') 'Office installer validates the expected interactive user'
Assert-Equal $true $installerCommand.Parameters.ContainsKey('ExpectedInteractiveSessionId') 'Office installer validates the expected interactive session'
Assert-Equal $true $installerCommand.Parameters.ContainsKey('ExpectedDesktopPath') 'Office installer validates the expected desktop path'
$recoveryCommand = Get-Command Recover-CountdownInstallTransactions -Module Countdown.InstallTransaction
Assert-Equal $false $recoveryCommand.Parameters.ContainsKey('TransactionMutexHeld') 'Recovery does not expose a lock-bypass switch'
$candidateCommand = Get-Command Set-CountdownInstallTransactionCandidateLauncherHash -Module Countdown.InstallTransaction
Assert-Equal $true $candidateCommand.Parameters.ContainsKey('CandidateLauncherSha256') 'Transaction persists a candidate launcher hash before replacement'
$failureReceiptCommand = Get-Command Write-CountdownInstallFailureReceipt -Module Countdown.InstallTransaction
Assert-Equal $true ($failureReceiptCommand.Parameters.ContainsKey('ErrorRecord') -and $failureReceiptCommand.Parameters.ContainsKey('RollbackSucceeded')) 'Transaction module exposes a selected-field deployment failure receipt writer'
$failureReceiptParserCommand = Get-Command ConvertFrom-CountdownInstallFailureReceiptBytes -Module Countdown.InstallTransaction
Assert-Equal $true ($failureReceiptCommand.Parameters.ContainsKey('CommitState') -and $failureReceiptParserCommand.Parameters.ContainsKey('Bytes')) 'Transaction module exposes a strict deployment failure receipt contract'
$installerSource = [System.IO.File]::ReadAllText((Join-Path $PSScriptRoot 'Install-Countdown.ps1'), [System.Text.Encoding]::UTF8)
$buildSource = [System.IO.File]::ReadAllText((Join-Path $PSScriptRoot 'Build-CountdownApplication.ps1'), [System.Text.Encoding]::UTF8)
$deploymentSource = [System.IO.File]::ReadAllText((Join-Path $PSScriptRoot 'Deploy-OfficeCountdown.ps1'), [System.Text.Encoding]::UTF8)
Assert-Equal $true $buildSource.Contains("`$dotnetRoot = 'D:\Codex\tools\dotnet\10.0'") 'Build uses the canonical managed dotnet 10.0 root'
Assert-Equal $false ([regex]::IsMatch($buildSource, 'dotnet\\10(?:\\|[''\"]|\s*$)')) 'Build contains no literal or dynamically joined dotnet 10 root without the minor version'
$installerDotnetPathIndex = $installerSource.IndexOf("`$dotnetPath = 'D:\Codex\tools\dotnet\10.0\dotnet.exe'")
$installerDotnetCheckIndex = $installerSource.IndexOf('if (-not (Test-Path -LiteralPath $dotnetPath -PathType Leaf))', $installerDotnetPathIndex)
$installerDotnetErrorIndex = $installerSource.IndexOf('未找到验收套件所需的受管 dotnet', $installerDotnetCheckIndex)
$installerValidationSuiteIndex = $installerSource.IndexOf('$validationSuite = Invoke-CountdownValidationSuite')
$installerDotnetPassIndex = $installerSource.IndexOf('-DotnetPath $dotnetPath', $installerValidationSuiteIndex)
Assert-Equal $true `
    ($installerDotnetPathIndex -ge 0 -and
        $installerDotnetCheckIndex -gt $installerDotnetPathIndex -and
        $installerDotnetErrorIndex -gt $installerDotnetCheckIndex -and
        $installerValidationSuiteIndex -gt $installerDotnetErrorIndex -and
        $installerDotnetPassIndex -gt $installerValidationSuiteIndex) `
    'Installer validates and passes the canonical managed dotnet 10.0 executable before invoking the validation suite'
Assert-Equal 2 ([regex]::Matches($buildSource, '-p:IncludeSourceRevisionInInformationalVersion=false').Count) 'Both self-contained publish commands exclude the Git revision so committed manifest hashes remain reproducible'
$managedBuildIntermediates = @(
    'src\MinminCountdown.Core\bin',
    'src\MinminCountdown.Core\obj',
    'src\MinminCountdown\bin',
    'src\MinminCountdown\obj',
    'src\MinminCountdown.Verification\bin',
    'src\MinminCountdown.Verification\obj'
)
foreach ($relativePath in $managedBuildIntermediates) {
    Assert-Equal $true $buildSource.Contains("'$relativePath'") "Clean build removes managed intermediate directory $relativePath"
}
Assert-Equal $true $buildSource.Contains('$managedCleanCandidates = @($applicationOutput, $verificationOutput)') 'Clean build derives removal candidates from actual output paths independently of the allowlist'
Assert-Equal $true $buildSource.Contains('$projectDirectories = @($coreProject, $applicationProject, $verificationProject)') 'Clean build derives intermediate candidates from all actual C# project paths'
Assert-Equal $true $buildSource.Contains('$resolvedCleanPath.StartsWith($repositoryRootPrefix, [System.StringComparison]::OrdinalIgnoreCase)') 'Clean build rejects absolute removal targets outside the repository root'
Assert-Equal $true $buildSource.Contains('$resolvedCleanPath -notin $expectedCleanPaths') 'Clean build validates every absolute removal target against its exact allowlist'
Assert-Equal $true $buildSource.Contains("`$runEvidenceCapabilityRoot = 'D:\agents\run-evidence'") 'Clean build uses the governed run-evidence capability root for isolated release clones'
Assert-Equal $true $buildSource.Contains('$resolvedCleanPath.StartsWith($runEvidenceRootPrefix, [System.StringComparison]::OrdinalIgnoreCase)') 'Clean build detects output paths inside the governed run-evidence tree'
Assert-Equal $true $buildSource.Contains('-CapabilityRoot $cleanupCapabilityRoot') 'Clean build passes the resolved stable capability root to safe-delete'
$installerCommittedRecoveryMessageIndex = $installerSource.IndexOf('检测到已提交但未完成应用解锁确认的事务')
$installerOwnershipRecoveryMessageIndex = $installerSource.IndexOf('检测到用户替换的桌面启动器：应用、任务和时间服务已恢复')
Assert-Equal $true ($installerCommittedRecoveryMessageIndex -ge 0 -and $installerCommittedRecoveryMessageIndex -lt $installerOwnershipRecoveryMessageIndex) 'Installer prioritizes committed recovery evidence over a simultaneous launcher conflict'
$timeSourceWaitDefinitionIndex = $installerSource.IndexOf('function Wait-CountdownNetworkTimeSource')
$timeSourceWaitCallIndex = $installerSource.IndexOf('$timeSource = Wait-CountdownNetworkTimeSource')
Assert-Equal $true ($timeSourceWaitDefinitionIndex -ge 0 -and $timeSourceWaitCallIndex -gt $timeSourceWaitDefinitionIndex) 'Installer waits with a bounded network-time source probe'
Assert-Equal $true ($installerSource -notmatch 'Start-Sleep -Seconds 3') 'Installer does not rely on a fixed three-second time-service sleep'
$payloadCaptureIndex = $installerSource.IndexOf('$transaction = Capture-CountdownInstallPayloadSnapshot')
$candidatePathIndex = $installerSource.IndexOf("`$candidateLauncherPath = Assert-CountdownInstallPathOnDDrive -Path (Join-Path `$transaction.transactionPath 'candidate-launcher.exe')")
$candidatePersistIndex = $installerSource.IndexOf('$transaction = Set-CountdownInstallTransactionCandidateLauncherHash')
$preparedLauncherGuardIndex = $installerSource.IndexOf('changed while installation was preparing')
$candidateHashCheckIndex = $installerSource.IndexOf('Transaction launcher candidate changed before desktop replacement.')
$launcherMoveIndex = $installerSource.IndexOf('Move-Item -LiteralPath $candidateLauncherPath -Destination $launcherPath -Force')
$installedLauncherHashCheckIndex = $installerSource.IndexOf('Desktop launcher hash mismatch after replacement.')
Assert-Equal $true ($payloadCaptureIndex -ge 0 -and $candidatePathIndex -gt $payloadCaptureIndex -and $candidatePersistIndex -gt $candidatePathIndex -and $preparedLauncherGuardIndex -gt $candidatePersistIndex -and $candidateHashCheckIndex -gt $preparedLauncherGuardIndex -and $launcherMoveIndex -gt $candidateHashCheckIndex -and $installedLauncherHashCheckIndex -gt $launcherMoveIndex) 'Installer keeps candidate under the transaction and verifies both candidate and replaced launcher immediately around the move'
$failureRecordIndex = $installerSource.IndexOf('$failureRecord = $_')
$failureStringIndex = $installerSource.IndexOf('$failure = $failureRecord.Exception.Message')
$launcherReplacementStageIndex = $installerSource.IndexOf("`$installStage = 'replace-desktop-launcher'")
$launcherStateStageIndex = $installerSource.IndexOf("`$installStage = 'write-launcher-state'")
$launcherStateMoveIndex = $installerSource.IndexOf('Move-Item -LiteralPath $launcherStateTempPath -Destination $launcherStatePath -Force')
Assert-Equal $true ($failureRecordIndex -ge 0 -and $failureStringIndex -gt $failureRecordIndex -and $launcherReplacementStageIndex -ge 0 -and $launcherReplacementStageIndex -lt $launcherMoveIndex -and $launcherStateStageIndex -gt $launcherMoveIndex -and $launcherStateStageIndex -lt $launcherStateMoveIndex) 'Installer preserves the original error record and marks both desktop replacement stages before they execute'
Assert-Equal $true ($deploymentSource.Contains('Get-OfficeRemoteDeploymentFailureReceipt') -and $deploymentSource.Contains('remote-deployment-failure.json') -and $deploymentSource.Contains('ConvertFrom-CountdownInstallFailureReceiptBytes') -and $deploymentSource.Contains('ReparsePoint') -and $deploymentSource.Contains('WriteAllText(') -and -not $deploymentSource.Contains('WriteAllBytes($failureReceiptEvidencePath')) 'Office deployment validates a bounded non-reparse remote failure receipt before persisting normalized evidence'
$installTreeGuardIndex = $installerSource.IndexOf('Assert-CountdownInstallTreeHasNoReparsePoints -Path $install', $payloadCaptureIndex)
$payloadCopyIndex = $installerSource.IndexOf('Copy-Item -LiteralPath $source -Destination $destination -Force', $installTreeGuardIndex)
Assert-Equal $true ($installTreeGuardIndex -gt $payloadCaptureIndex -and $payloadCopyIndex -gt $installTreeGuardIndex) 'Installer rechecks the install tree after the stable snapshot and before writing payload files'
$adapterSource = [System.IO.File]::ReadAllText((Join-Path $PSScriptRoot '敏敏子倒计时.ps1'), [System.Text.Encoding]::UTF8)
$adapterWaitForExitIndex = $adapterSource.IndexOf('$process.WaitForExit()')
$adapterStayAttachedIndex = $adapterSource.IndexOf('$stayAttached = $ValidateOnly.IsPresent')
$adapterAliveProbeIndex = $adapterSource.IndexOf('Start-Sleep -Milliseconds 200')
$adapterHasExitedIndex = $adapterSource.IndexOf('$process.HasExited')
Assert-Equal $true ($adapterStayAttachedIndex -ge 0 -and $adapterWaitForExitIndex -gt $adapterStayAttachedIndex) 'Production adapter waits for exit only in validate/exercise/test modes'
Assert-Equal $true ($adapterAliveProbeIndex -gt $adapterWaitForExitIndex -and $adapterHasExitedIndex -gt $adapterAliveProbeIndex) 'Production adapter detaches after confirming the C# host stayed alive'
Assert-Equal $true ($adapterSource.Contains('Win32_Process') -and $adapterSource.Contains('MethodName Create') -and $adapterSource.Contains('脱离计划任务作业')) 'Production adapter starts the C# host outside the scheduled-task job'
$remoteDeploymentSource = [System.IO.File]::ReadAllText((Join-Path $PSScriptRoot 'Countdown.RemoteDeployment.psm1'), [System.Text.Encoding]::UTF8)
Assert-Equal $true ($remoteDeploymentSource.Contains('ConvertFrom-Json -DateKind String') -and $remoteDeploymentSource.Contains('ConvertTo-CountdownSettingsFingerprintMtimeUtc')) 'Independent readback keeps settings mtime as a string and normalizes ConvertFrom-Json DateTime coercion'
$mtimeSample = '2026-08-17T05:20:04.4405459Z'
$mtimeJson = (@{ ExpectedSettingsMtimeUtc = $mtimeSample } | ConvertTo-Json -Compress)
$coercedMtime = ($mtimeJson | ConvertFrom-Json).ExpectedSettingsMtimeUtc
$keptMtime = ($mtimeJson | ConvertFrom-Json -DateKind String).ExpectedSettingsMtimeUtc
Assert-Equal $true ($coercedMtime -is [datetime] -or [string] $keptMtime -eq $mtimeSample) 'ConvertFrom-Json either keeps the settings mtime string or yields a DateTime the fingerprint helper can normalize'
Assert-Equal $true $releaseContract.IntegrityHashes.Contains('Build-CountdownApplication.ps1') 'Release contract protects the build script required by remote core tests'
Assert-Equal $true ('Build-CountdownApplication.ps1' -in @($releaseContract.PackageFiles)) 'Release contract packages the build script required by remote core tests'
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
Assert-Equal 0 $loadedLegacyV3.Tags.Count 'Schema v3 creates an empty tag library'
Assert-Equal 0 @($loadedLegacyV3.Records[0].tagIds).Count 'Schema v3 creates untagged countdowns'
Assert-Equal 'Minute' $loadedLegacyV3.Preferences.Precision 'Schema v3 preserves precision'
$canonicalLegacyV3 = ConvertTo-CountdownSettingsJson -State $loadedLegacyV3
$canonicalLegacyV3Object = $canonicalLegacyV3 | ConvertFrom-Json
Assert-Equal 10 $canonicalLegacyV3Object.schemaVersion 'Canonical settings schema'
Assert-Equal 'Auto' $loadedLegacyV3.Preferences.ThemePreference 'Schema v3 defaults the theme preference to Auto'
Assert-Equal 'Auto' $canonicalLegacyV3Object.themePreference 'Canonical settings persist the Auto theme preference'
Assert-Equal 0 @($canonicalLegacyV3Object.tags).Count 'Canonical settings tags'
Assert-Equal 0 @($canonicalLegacyV3Object.notes).Count 'Canonical settings notes'
Assert-Equal 0 @($canonicalLegacyV3Object.countdowns[0].tagIds).Count 'Canonical settings countdown tag IDs'
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
Assert-Equal $true (Test-CountdownSettingsDeploymentTransition -BeforeHash 'before' -AfterHash 'after' -BeforeJson ($legacyV3Settings | ConvertTo-Json -Depth 4) -AfterJson $canonicalLegacyV3) 'Strict schema v3 to v10 migration is accepted for deployment'
$legacyV1Settings = [pscustomobject]@{ schemaVersion = 1; timeZoneId = 'China Standard Time'; targetUtc = '2099-01-01T00:00:00Z' }
$canonicalLegacyV1 = ConvertTo-CountdownSettingsJson -State (ConvertFrom-CountdownSettingsObject -Settings $legacyV1Settings)
Assert-Equal $false (Test-CountdownSettingsDeploymentTransition -BeforeHash 'before' -AfterHash 'after' -BeforeJson ($legacyV1Settings | ConvertTo-Json -Depth 4) -AfterJson $canonicalLegacyV1) 'Schema v1 migration remains rejected for deployment'
$settingsModuleTestPath = Join-Path $PSScriptRoot 'settings-module-test.json'
try {
    Save-CountdownSettings -Path $settingsModuleTestPath -State $loadedLegacyV3
    $savedSettings = [System.IO.File]::ReadAllText($settingsModuleTestPath, [System.Text.Encoding]::UTF8) | ConvertFrom-Json
    Assert-Equal 10 $savedSettings.schemaVersion 'Settings module writes canonical schema'
    Assert-Equal $false (Test-Path -LiteralPath "$settingsModuleTestPath.tmp") 'Settings module cleans temporary file'
    $canonicalV10Reload = ConvertFrom-CountdownSettingsObject -Settings $savedSettings
    Assert-Equal $false $canonicalV10Reload.Migrated 'Saved canonical v10 is not marked for migration'
    $canonicalV10Item = Get-Item -LiteralPath $settingsModuleTestPath
    $canonicalV10Hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $settingsModuleTestPath).Hash
    $canonicalV10Length = [long] $canonicalV10Item.Length
    $canonicalV10Mtime = $canonicalV10Item.LastWriteTimeUtc.ToUniversalTime().ToString('o')
    Start-Sleep -Milliseconds 20
    Save-CountdownSettings -Path $settingsModuleTestPath -State $canonicalV10Reload
    $resavedV10Item = Get-Item -LiteralPath $settingsModuleTestPath
    Assert-Equal $canonicalV10Hash (Get-FileHash -Algorithm SHA256 -LiteralPath $settingsModuleTestPath).Hash 'Re-saving already-canonical v10 does not change SHA-256'
    Assert-Equal $canonicalV10Length ([long] $resavedV10Item.Length) 'Re-saving already-canonical v10 does not change length'
    Assert-Equal $canonicalV10Mtime $resavedV10Item.LastWriteTimeUtc.ToUniversalTime().ToString('o') 'Re-saving already-canonical v10 does not change LastWriteTimeUtc'
}
finally {
    Remove-Item -LiteralPath $settingsModuleTestPath -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath "$settingsModuleTestPath.tmp" -Force -ErrorAction SilentlyContinue
}

$legacyV4Settings = [pscustomobject]@{
    schemaVersion = 4
    timeZoneId = 'China Standard Time'
    displayPrecision = 'Second'
    monthlySalary = 23000
    workStart = '09:00'
    workEnd = '18:00'
    countdowns = @([pscustomobject]@{ id = 'legacy-v4-countdown'; name = '旧版带便签倒计时'; targetUtc = '2099-02-01T00:00:00Z' })
    notes = @([pscustomobject]@{ id = 'legacy-v4-note'; text = '保留的旧版便签'; isCompleted = $true })
}
$loadedLegacyV4 = ConvertFrom-CountdownSettingsObject -Settings $legacyV4Settings
Assert-Equal $true $loadedLegacyV4.Migrated 'Schema v4 settings migrate'
Assert-Equal 0 $loadedLegacyV4.Tags.Count 'Schema v4 creates an empty tag library'
Assert-Equal 0 @($loadedLegacyV4.Records[0].tagIds).Count 'Schema v4 creates untagged countdowns'
Assert-Equal 0 @($loadedLegacyV4.Notes[0].tagIds).Count 'Schema v4 creates untagged notes'
Assert-Equal '保留的旧版便签' $loadedLegacyV4.Notes[0].text 'Schema v4 preserves notes'
Assert-Equal $null $loadedLegacyV4.Notes[0].date 'Schema v4 does not invent a note date'
$canonicalLegacyV4 = ConvertTo-CountdownSettingsJson -State $loadedLegacyV4
Assert-Equal $true (Test-CountdownSettingsDeploymentTransition -BeforeHash 'before' -AfterHash 'after' -BeforeJson ($legacyV4Settings | ConvertTo-Json -Depth 4) -AfterJson $canonicalLegacyV4) 'Strict schema v4 to v10 migration is accepted for deployment'
$invalidV4Transition = $canonicalLegacyV4 | ConvertFrom-Json
$invalidV4Transition.notes[0].text = '被篡改的便签'
Assert-Equal $false (Test-CountdownSettingsDeploymentTransition -BeforeHash 'before' -AfterHash 'after' -BeforeJson ($legacyV4Settings | ConvertTo-Json -Depth 4) -AfterJson ($invalidV4Transition | ConvertTo-Json -Depth 5)) 'Schema v4 migration rejects changed notes'

$v5Settings = [pscustomobject]@{
    schemaVersion = 5
    timeZoneId = 'China Standard Time'
    displayPrecision = 'Hour'
    monthlySalary = 8000
    workStart = '08:00'
    workEnd = '17:00'
    tags = @(
        [pscustomobject]@{ id = 'work'; name = '工作'; color = '#6B8E23' },
        [pscustomobject]@{ id = 'home'; name = '家庭'; color = $null }
    )
    countdowns = @([pscustomobject]@{ id = 'v5-countdown'; name = '带标签倒计时'; targetUtc = '2099-03-01T00:00:00Z'; tagIds = @(' WORK ') })
    notes = @([pscustomobject]@{ id = 'v5-note'; text = '带标签便签'; isCompleted = $false; tagIds = @('home') })
}
$loadedV5 = ConvertFrom-CountdownSettingsObject -Settings $v5Settings
Assert-Equal $true $loadedV5.Migrated 'Schema v5 settings migrate to add note dates'
Assert-Equal 2 $loadedV5.Tags.Count 'Schema v5 loads tags'
Assert-Equal 'work' $loadedV5.Records[0].tagIds[0] 'Schema v5 canonicalizes countdown tag IDs'
Assert-Equal 'home' $loadedV5.Notes[0].tagIds[0] 'Schema v5 preserves note tag IDs'
Assert-Equal $null $loadedV5.Notes[0].date 'Schema v5 migration does not invent a note date'
$v5Copy = Copy-CountdownSettingsState -State $loadedV5
$v5Copy.Tags[0].name = '已改名'
$v5Copy.Records[0].tagIds[0] = 'home'
$v5Copy.Notes[0].tagIds[0] = 'work'
Assert-Equal '工作' $loadedV5.Tags[0].name 'Settings state copy does not mutate source tags'
Assert-Equal 'work' $loadedV5.Records[0].tagIds[0] 'Settings state copy does not mutate source countdown tags'
Assert-Equal 'home' $loadedV5.Notes[0].tagIds[0] 'Settings state copy does not mutate source note tags'
$canonicalFromV5 = ConvertTo-CountdownSettingsJson -State $loadedV5 | ConvertFrom-Json
Assert-Equal 10 $canonicalFromV5.schemaVersion 'Schema v5 migrates to canonical schema v10'
Assert-Equal 'Auto' $canonicalFromV5.themePreference 'Schema v5 migration defaults the theme preference to Auto'
Assert-Equal '#6B8E23' $canonicalFromV5.tags[0].color 'Schema v10 serializes tag color'
Assert-Equal $true ($null -eq $canonicalFromV5.tags[1].PSObject.Properties['color']) 'Schema v10 omits an unspecified tag color'
Assert-Equal 'work' $canonicalFromV5.countdowns[0].tagIds[0] 'Schema v10 serializes countdown tag IDs'
Assert-Equal 'home' $canonicalFromV5.notes[0].tagIds[0] 'Schema v10 serializes note tag IDs'
Assert-Equal $null $canonicalFromV5.notes[0].date 'Schema v10 preserves an unknown legacy note date as null'
Assert-Equal 0 @($canonicalFromV5.notes[0].reminderRules).Count 'Schema v10 disables reminders for migrated notes'
$loadedNullDateV10 = ConvertFrom-CountdownSettingsObject -Settings $canonicalFromV5
Assert-Equal $null $loadedNullDateV10.Notes[0].date 'Schema v10 accepts an explicitly unknown note date'
Assert-Equal $true (Test-CountdownSettingsDeploymentTransition -BeforeHash 'before' -AfterHash 'after' -BeforeJson ($v5Settings | ConvertTo-Json -Depth 5) -AfterJson ($canonicalFromV5 | ConvertTo-Json -Depth 8)) 'Strict schema v5 to v10 migration is accepted for deployment'
$invalidV5Transition = $canonicalFromV5 | ConvertTo-Json -Depth 5 | ConvertFrom-Json
$invalidV5Transition.notes[0].date = '2026-07-18'
Assert-Equal $false (Test-CountdownSettingsDeploymentTransition -BeforeHash 'before' -AfterHash 'after' -BeforeJson ($v5Settings | ConvertTo-Json -Depth 5) -AfterJson ($invalidV5Transition | ConvertTo-Json -Depth 5)) 'Schema v5 migration rejects an invented note date'

$v6Settings = $canonicalFromV5 | ConvertTo-Json -Depth 8 | ConvertFrom-Json
$v6Settings.schemaVersion = 6
$v6Settings.PSObject.Properties.Remove('themePreference')
$v6Settings.PSObject.Properties.Remove('reminderStates')
foreach ($countdown in @($v6Settings.countdowns)) { $countdown.PSObject.Properties.Remove('reminderRules') }
foreach ($note in @($v6Settings.notes)) { $note.PSObject.Properties.Remove('reminderRules') }
$v6Settings.notes[0].date = '2026-07-18'
$loadedV6 = ConvertFrom-CountdownSettingsObject -Settings $v6Settings
Assert-Equal $true $loadedV6.Migrated 'Schema v6 settings migrate to add reminder fields'
Assert-Equal '2026-07-18' $loadedV6.Notes[0].date 'Schema v6 loads an exact note date'
Assert-Equal $null $loadedV6.Notes[0].reminderIntervalMinutes 'Schema v6 migration disables reminders'
$canonicalV6 = ConvertTo-CountdownSettingsJson -State $loadedV6 | ConvertFrom-Json
Assert-Equal 10 $canonicalV6.schemaVersion 'Schema v6 migrates to canonical schema v10'
Assert-Equal '2026-07-18' $canonicalV6.notes[0].date 'Schema v10 preserves an exact note date'
Assert-Equal 0 @($canonicalV6.notes[0].reminderRules).Count 'Schema v10 persists a disabled reminder explicitly'
Assert-Equal $true (Test-CountdownSettingsDeploymentTransition -BeforeHash 'before' -AfterHash 'after' -BeforeJson ($v6Settings | ConvertTo-Json -Depth 8) -AfterJson ($canonicalV6 | ConvertTo-Json -Depth 8)) 'Strict schema v6 to v10 migration is accepted for deployment'

$v7Settings = $canonicalV6 | ConvertTo-Json -Depth 8 | ConvertFrom-Json
$v7Settings.schemaVersion = 7
$v7Settings.PSObject.Properties.Remove('themePreference')
$v7Settings.PSObject.Properties.Remove('reminderStates')
foreach ($countdown in @($v7Settings.countdowns)) { $countdown.PSObject.Properties.Remove('reminderRules') }
foreach ($note in @($v7Settings.notes)) { $note.PSObject.Properties.Remove('reminderRules') }
$v7Settings.notes[0] | Add-Member -NotePropertyName reminderIntervalMinutes -NotePropertyValue 30 -Force
$v7Settings.notes[0] | Add-Member -NotePropertyName reminderStartUtc -NotePropertyValue '2099-07-19T00:00:00.0000000+00:00' -Force
$loadedV7 = ConvertFrom-CountdownSettingsObject -Settings $v7Settings
Assert-Equal $true $loadedV7.Migrated 'Schema v7 settings migrate to schema v10'
Assert-Equal 30 $loadedV7.Notes[0].reminderIntervalMinutes 'Schema v7 loads a reminder interval'
Assert-Equal ([DateTimeOffset]'2099-07-19T00:00:00Z') $loadedV7.Notes[0].reminderStartUtc 'Schema v7 loads a UTC reminder start'
$canonicalV7 = ConvertTo-CountdownSettingsJson -State $loadedV7 | ConvertFrom-Json
Assert-Equal 10 $canonicalV7.schemaVersion 'Schema v7 writes canonical schema v10'
Assert-Equal 'fixedInterval' $canonicalV7.notes[0].reminderRules[0].type 'Schema v7 migrates to a fixed-interval rule'
Assert-Equal 30 $canonicalV7.notes[0].reminderRules[0].intervalMinutes 'Schema v7 preserves a reminder interval'
Assert-Equal ([DateTimeOffset]'2099-07-19T00:30:00Z') ([DateTimeOffset] $canonicalV7.notes[0].reminderRules[0].firstDueUtc).ToUniversalTime() 'Schema v7 preserves the first due time'
Assert-Equal $true (Test-CountdownSettingsDeploymentTransition -BeforeHash 'before' -AfterHash 'after' -BeforeJson ($v7Settings | ConvertTo-Json -Depth 8) -AfterJson ($canonicalV7 | ConvertTo-Json -Depth 8)) 'Strict schema v7 to v10 migration is accepted for deployment'
$preFreezeV7 = $canonicalV7 | ConvertTo-Json -Depth 5 | ConvertFrom-Json
$preFreezeV7.monthlySalary = 100
$postFreezeV7 = $canonicalV7 | ConvertTo-Json -Depth 5 | ConvertFrom-Json
$postFreezeV7.monthlySalary = 200
Assert-Equal $false (Test-CountdownSettingsDeploymentTransition -BeforeHash 'pre-freeze' -AfterHash 'post-freeze' -BeforeJson ($preFreezeV7 | ConvertTo-Json -Depth 5) -AfterJson ($postFreezeV7 | ConvertTo-Json -Depth 5)) 'Schema v7 changes after the selected deployment baseline are rejected'
Assert-Equal $true (Test-CountdownSettingsDeploymentTransition -BeforeHash 'post-freeze' -AfterHash 'post-freeze' -BeforeJson ($postFreezeV7 | ConvertTo-Json -Depth 5) -AfterJson ($postFreezeV7 | ConvertTo-Json -Depth 5)) 'A pre-freeze save preserved in the transaction snapshot remains deployable'
$reloadedCanonicalV7 = ConvertFrom-CountdownSettingsObject -Settings $canonicalV7
Assert-Equal 2 $reloadedCanonicalV7.Tags.Count 'Schema v10 reloads tags without optional colors'
Assert-Equal $null $reloadedCanonicalV7.Tags[1].color 'Schema v10 reload preserves an unspecified tag color'

$v8OnceSettings = $canonicalV7 | ConvertTo-Json -Depth 8 | ConvertFrom-Json
$v8OnceSettings.schemaVersion = 8
$v8OnceSettings.PSObject.Properties.Remove('themePreference')
$v8OnceSettings.PSObject.Properties.Remove('reminderStates')
foreach ($countdown in @($v8OnceSettings.countdowns)) { $countdown.PSObject.Properties.Remove('reminderRules') }
foreach ($note in @($v8OnceSettings.notes)) { $note.PSObject.Properties.Remove('reminderRules') }
$v8OnceSettings.notes[0] | Add-Member -NotePropertyName reminder -NotePropertyValue ([pscustomobject]@{
        type = 'once'
        firstDueUtc = '2026-07-20T01:15:00.0000000+00:00'
        delivery = 'pending'
    }) -Force
$loadedV8Once = ConvertFrom-CountdownSettingsObject -Settings $v8OnceSettings
Assert-Equal $true $loadedV8Once.Migrated 'Schema v8 settings migrate to schema v10'
Assert-Equal 'Auto' $loadedV8Once.Preferences.ThemePreference 'Schema v8 defaults the theme preference to Auto'
Assert-Equal 'once' $loadedV8Once.Notes[0].reminder.type 'Schema v8 loads an once reminder'
Assert-Equal 'pending' $loadedV8Once.Notes[0].reminder.delivery 'Schema v8 preserves pending delivery state'
$canonicalV8Once = ConvertTo-CountdownSettingsJson -State $loadedV8Once | ConvertFrom-Json
Assert-Equal 'absoluteOnce' $canonicalV8Once.notes[0].reminderRules[0].type 'Schema v8 migrates an once reminder to an absolute rule'
Assert-Equal ([DateTimeOffset]'2026-07-20T01:15:00Z') ([DateTimeOffset] $canonicalV8Once.notes[0].reminderRules[0].scheduledUtc).ToUniversalTime() 'Schema v8 preserves the once due time'
Assert-Equal 0 @($canonicalV8Once.reminderStates.PSObject.Properties).Count 'Schema v8 preserves pending delivery by omitting handled state'
Assert-Equal 10 $canonicalV8Once.schemaVersion 'Schema v8 writes canonical schema v10'
Assert-Equal 'Auto' $canonicalV8Once.themePreference 'Schema v8 writes the default Auto theme preference'
Assert-Equal $true (Test-CountdownSettingsDeploymentTransition -BeforeHash 'before' -AfterHash 'after' -BeforeJson ($v8OnceSettings | ConvertTo-Json -Depth 8) -AfterJson ($canonicalV8Once | ConvertTo-Json -Depth 8)) 'Strict schema v8 to v10 migration is accepted for deployment'
$invalidV8ThemeTransition = $canonicalV8Once | ConvertTo-Json -Depth 6 | ConvertFrom-Json
$invalidV8ThemeTransition.themePreference = 'Night'
Assert-Equal $false (Test-CountdownSettingsDeploymentTransition -BeforeHash 'before' -AfterHash 'after' -BeforeJson ($v8OnceSettings | ConvertTo-Json -Depth 6) -AfterJson ($invalidV8ThemeTransition | ConvertTo-Json -Depth 6)) 'Schema v8 migration rejects an invented manual theme preference'
$invalidV8OnceBranchRejected = $false
try {
    $invalidV8OnceBranch = $v8OnceSettings | ConvertTo-Json -Depth 6 | ConvertFrom-Json
    $invalidV8OnceBranch.notes[0].reminder | Add-Member -NotePropertyName intervalMinutes -NotePropertyValue 30
    ConvertFrom-CountdownSettingsObject -Settings $invalidV8OnceBranch | Out-Null
}
catch {
    $invalidV8OnceBranchRejected = $true
}
Assert-Equal $true $invalidV8OnceBranchRejected 'Schema v8 rejects repeat fields on an once reminder'

$v9NightSettings = $canonicalV8Once | ConvertTo-Json -Depth 8 | ConvertFrom-Json
$v9NightSettings.schemaVersion = 9
$v9NightSettings.themePreference = 'Night'
foreach ($countdown in @($v9NightSettings.countdowns)) { $countdown.PSObject.Properties.Remove('reminderRules') }
foreach ($note in @($v9NightSettings.notes)) {
    $note.PSObject.Properties.Remove('reminderRules')
    $note | Add-Member -NotePropertyName reminder -NotePropertyValue ([pscustomobject]@{
        type = 'once'
        firstDueUtc = '2026-07-20T01:15:00.0000000+00:00'
        delivery = 'pending'
    }) -Force
}
$v9NightSettings.PSObject.Properties.Remove('reminderStates')
$loadedV9Night = ConvertFrom-CountdownSettingsObject -Settings $v9NightSettings
Assert-Equal $true $loadedV9Night.Migrated 'Schema v9 settings migrate to schema v10'
Assert-Equal 'Night' $loadedV9Night.Preferences.ThemePreference 'Schema v9 loads a manual night preference'
$canonicalV9Night = ConvertTo-CountdownSettingsJson -State $loadedV9Night | ConvertFrom-Json
Assert-Equal 10 $canonicalV9Night.schemaVersion 'Schema v9 writes canonical schema v10'
Assert-Equal 'Night' $canonicalV9Night.themePreference 'Schema v9 migration preserves a manual night preference'
Assert-Equal 'absoluteOnce' $canonicalV9Night.notes[0].reminderRules[0].type 'Schema v9 migrates a pending once reminder'
Assert-Equal 0 @($canonicalV9Night.reminderStates.PSObject.Properties).Count 'Schema v9 pending once migration does not invent handled state'
$missingV9ThemeRejected = $false
try {
    $missingV9Theme = $v9NightSettings | ConvertTo-Json -Depth 6 | ConvertFrom-Json
    $missingV9Theme.PSObject.Properties.Remove('themePreference')
    ConvertFrom-CountdownSettingsObject -Settings $missingV9Theme | Out-Null
}
catch {
    $missingV9ThemeRejected = $true
}
Assert-Equal $true $missingV9ThemeRejected 'Schema v9 rejects a missing theme preference'

$v9PendingRuleIdFirst = [string] (ConvertTo-CountdownSettingsJson -State (
        ConvertFrom-CountdownSettingsObject -Settings $v9NightSettings -MigrationUtc ([DateTimeOffset]'2026-07-20T02:16:00Z')
    ) | ConvertFrom-Json).notes[0].reminderRules[0].id
$v9PendingRuleIdSecond = [string] (ConvertTo-CountdownSettingsJson -State (
        ConvertFrom-CountdownSettingsObject -Settings $v9NightSettings -MigrationUtc ([DateTimeOffset]'2030-01-01T00:00:00Z')
    ) | ConvertFrom-Json).notes[0].reminderRules[0].id
Assert-Equal $v9PendingRuleIdFirst $v9PendingRuleIdSecond 'Schema v9 migration derives a stable reminder rule ID independent of wall-clock time'

$v9DeliveredOnceSettings = $v9NightSettings | ConvertTo-Json -Depth 8 | ConvertFrom-Json
$v9DeliveredOnceSettings.notes[0].reminder.delivery = 'delivered'
$canonicalV9DeliveredOnce = ConvertTo-CountdownSettingsJson -State (
    ConvertFrom-CountdownSettingsObject -Settings $v9DeliveredOnceSettings -MigrationUtc ([DateTimeOffset]'2026-07-20T02:16:00Z')
) | ConvertFrom-Json
$v9DeliveredRuleId = [string] $canonicalV9DeliveredOnce.notes[0].reminderRules[0].id
$v9DeliveredState = $canonicalV9DeliveredOnce.reminderStates.PSObject.Properties[$v9DeliveredRuleId].Value
Assert-Equal ([DateTimeOffset]'2026-07-20T01:15:00Z') ([DateTimeOffset] $v9DeliveredState.handledThroughUtc).ToUniversalTime() 'Schema v9 delivered once migration preserves the handled cursor'
Assert-Equal ("{0}|2026-07-20T01:15:00.0000000+00:00" -f $v9DeliveredRuleId) ([string] $v9DeliveredState.lastDeliveredOccurrenceKey) 'Schema v9 delivered once migration persists the canonical occurrence key'
Assert-Equal $null $v9DeliveredState.lastDispositionOccurrenceKey 'Schema v9 delivered once migration does not invent disposition'
Assert-Equal $null $v9DeliveredState.activeSnooze 'Schema v9 delivered once migration does not invent snooze state'

$v9RepeatSettings = $v9NightSettings | ConvertTo-Json -Depth 8 | ConvertFrom-Json
$v9RepeatSettings.notes[0].reminder = [pscustomobject]@{
    type = 'repeat'
    firstDueUtc = '2026-07-20T01:00:00.0000000+00:00'
    intervalMinutes = 30
}
$v9RepeatMigrationUtc = [DateTimeOffset]'2026-07-20T02:16:00Z'
$loadedV9Repeat = ConvertFrom-CountdownSettingsObject -Settings $v9RepeatSettings -MigrationUtc $v9RepeatMigrationUtc
$canonicalV9Repeat = ConvertTo-CountdownSettingsJson -State $loadedV9Repeat | ConvertFrom-Json
$v9RepeatRuleId = [string] $canonicalV9Repeat.notes[0].reminderRules[0].id
$v9RepeatState = $canonicalV9Repeat.reminderStates.PSObject.Properties[$v9RepeatRuleId].Value
Assert-Equal 'fixedInterval' $canonicalV9Repeat.notes[0].reminderRules[0].type 'Schema v9 repeat migration creates a fixed-interval rule'
Assert-Equal ([DateTimeOffset]'2026-07-20T02:00:00Z') ([DateTimeOffset] $v9RepeatState.handledThroughUtc).ToUniversalTime() 'Schema v9 repeat migration advances the cursor to the latest elapsed occurrence'
Assert-Equal $null $v9RepeatState.lastDeliveredOccurrenceKey 'Schema v9 repeat migration does not claim a rendered delivery'
Assert-Equal $null $v9RepeatState.lastDispositionOccurrenceKey 'Schema v9 repeat migration does not invent disposition'
Assert-Equal $null $v9RepeatState.activeSnooze 'Schema v9 repeat migration does not invent snooze state'
$reloadedV10Repeat = ConvertFrom-CountdownSettingsObject -Settings $canonicalV9Repeat -MigrationUtc ([DateTimeOffset]'2099-01-01T00:00:00Z')
Assert-Equal $false $reloadedV10Repeat.Migrated 'Canonical schema v10 reload does not migrate'
Assert-Equal 'repeat' $reloadedV10Repeat.Notes[0].reminder.type 'Canonical schema v10 projects the first fixed-interval rule for the legacy UI'
Assert-Equal ($canonicalV9Repeat | ConvertTo-Json -Depth 8) ((ConvertTo-CountdownSettingsJson -State $reloadedV10Repeat | ConvertFrom-Json) | ConvertTo-Json -Depth 8) 'Schema v10 reminder rules and cursor round-trip canonically'

$multiRuleV10 = $canonicalV9Night | ConvertTo-Json -Depth 8 | ConvertFrom-Json
$multiRuleV10.notes[0].reminderRules = @(
    [pscustomobject]@{ id = 'multi-absolute'; type = 'absoluteOnce'; scheduledUtc = '2099-01-01T00:00:00.0000000+00:00' },
    [pscustomobject]@{ id = 'multi-fixed'; type = 'fixedInterval'; firstDueUtc = '2099-01-02T00:00:00.0000000+00:00'; intervalMinutes = 45 }
)
$multiRuleV10.reminderStates = [pscustomobject]@{}
$multiRuleState = ConvertFrom-CountdownSettingsObject -Settings $multiRuleV10
$multiRuleCanonical = ConvertTo-CountdownSettingsJson -State $multiRuleState | ConvertFrom-Json
Assert-Equal 2 @($multiRuleCanonical.notes[0].reminderRules).Count 'Schema v10 round-trips multiple reminder rules on one record'
Assert-Equal 'multi-absolute' $multiRuleCanonical.notes[0].reminderRules[0].id 'Schema v10 preserves reminder rule order and identity'
Assert-Equal 'multi-fixed' $multiRuleCanonical.notes[0].reminderRules[1].id 'Schema v10 preserves a second reminder rule identity'

$relativeV10 = $multiRuleV10 | ConvertTo-Json -Depth 8 | ConvertFrom-Json
$relativeV10.countdowns[0].targetUtc = '2099-01-01T00:10:00.0000000+00:00'
$relativeV10.countdowns[0].reminderRules = @(
    [pscustomobject]@{ id = 'relative-zero'; type = 'countdownRelative'; offsetMinutesBefore = 0 },
    [pscustomobject]@{ id = 'relative-max'; type = 'countdownRelative'; offsetMinutesBefore = 5256000 }
)
$relativeV10.reminderStates = [pscustomobject]@{}
$relativeV10.reminderStates | Add-Member -NotePropertyName 'relative-max' -NotePropertyValue ([pscustomobject]@{
        handledThroughUtc = '2088-12-31T23:50:00.0000000+00:00'
        lastDeliveredOccurrenceKey = 'relative-max|2088-12-31T23:50:00.0000000+00:00'
        lastDispositionOccurrenceKey = $null
        activeSnooze = $null
    })
$relativeState = ConvertFrom-CountdownSettingsObject -Settings $relativeV10
$relativeCanonical = ConvertTo-CountdownSettingsJson -State $relativeState | ConvertFrom-Json
Assert-Equal 2 @($relativeCanonical.countdowns[0].reminderRules).Count 'Schema v10 round-trips countdown-relative reminder rules'
Assert-Equal 0 $relativeCanonical.countdowns[0].reminderRules[0].offsetMinutesBefore 'Schema v10 accepts a zero countdown-relative offset'
Assert-Equal 5256000 $relativeCanonical.countdowns[0].reminderRules[1].offsetMinutesBefore 'Schema v10 accepts the maximum countdown-relative offset'
Assert-Equal 'relative-max|2088-12-31T23:50:00.0000000+00:00' $relativeCanonical.reminderStates.'relative-max'.lastDeliveredOccurrenceKey 'Schema v10 preserves a historical delivered relative occurrence after target movement'
$relativeReloaded = ConvertFrom-CountdownSettingsObject -Settings $relativeCanonical
Assert-Equal ($relativeCanonical | ConvertTo-Json -Depth 8) ((ConvertTo-CountdownSettingsJson -State $relativeReloaded | ConvertFrom-Json) | ConvertTo-Json -Depth 8) 'Schema v10 countdown-relative rules round-trip canonically'

$calendarV10 = $multiRuleV10 | ConvertTo-Json -Depth 12 | ConvertFrom-Json
$calendarV10.notes[0].reminderRules = @(
    [pscustomobject]@{ id = 'calendar-daily'; type = 'calendar'; frequency = 'daily'; interval = 2; startDate = '2032-01-01'; localTime = '09:00' },
    [pscustomobject]@{ id = 'calendar-weekly'; type = 'calendar'; frequency = 'weekly'; interval = 3; startDate = '2032-01-05'; localTime = '10:30'; weekdays = @('friday', 'monday') },
    [pscustomobject]@{ id = 'calendar-monthly-day'; type = 'calendar'; frequency = 'monthly'; interval = 1; startDate = '2032-01-01'; localTime = '11:00'; day = 31 },
    [pscustomobject]@{ id = 'calendar-yearly-last'; type = 'calendar'; frequency = 'yearly'; interval = 4; startDate = '2032-01-01'; localTime = '12:00'; month = 2; lastDay = $true }
)
$calendarV10.countdowns[0].reminderRules = @(
    [pscustomobject]@{ id = 'calendar-countdown-last'; type = 'calendar'; frequency = 'monthly'; interval = 1; startDate = '2032-01-01'; localTime = '09:00'; lastDay = $true }
)
$calendarV10.reminderStates = [pscustomobject]@{}
$calendarV10.reminderStates | Add-Member -NotePropertyName 'calendar-countdown-last' -NotePropertyValue ([pscustomobject]@{
        handledThroughUtc = '2032-02-29T01:00:00.0000000+00:00'
        lastDeliveredOccurrenceKey = 'calendar-countdown-last|2032-02-29T01:00:00.0000000+00:00'
        lastDispositionOccurrenceKey = $null
        activeSnooze = $null
    })
$calendarState = ConvertFrom-CountdownSettingsObject -Settings $calendarV10
$calendarCanonical = ConvertTo-CountdownSettingsJson -State $calendarState | ConvertFrom-Json
Assert-Equal 4 @($calendarCanonical.notes[0].reminderRules).Count 'Schema v10 round-trips all four calendar frequencies'
Assert-Equal 'monday' $calendarCanonical.notes[0].reminderRules[1].weekdays[0] 'Schema v10 canonicalizes weekly selectors to Monday-first order'
Assert-Equal 'friday' $calendarCanonical.notes[0].reminderRules[1].weekdays[1] 'Schema v10 preserves all typed weekly selector values'
Assert-Equal 31 $calendarCanonical.notes[0].reminderRules[2].day 'Schema v10 preserves an explicit monthly day without clamping'
Assert-Equal $true $calendarCanonical.notes[0].reminderRules[3].lastDay 'Schema v10 preserves an explicit yearly lastDay selector'
Assert-Equal $true $calendarCanonical.countdowns[0].reminderRules[0].lastDay 'Schema v10 supports calendar selectors on countdowns'
Assert-Equal 'none' $calendarState.Notes[0].reminder.type 'Schema v10 keeps calendar rules out of the legacy reminder projection'
Assert-Equal ($calendarCanonical | ConvertTo-Json -Depth 12) ((ConvertTo-CountdownSettingsJson -State (ConvertFrom-CountdownSettingsObject -Settings $calendarCanonical) | ConvertFrom-Json) | ConvertTo-Json -Depth 12) 'Schema v10 calendar rules and states round-trip canonically'

$invalidCalendarCursorRejected = $false
try {
    $invalid = $calendarV10 | ConvertTo-Json -Depth 12 | ConvertFrom-Json
    $invalid.reminderStates.'calendar-countdown-last'.handledThroughUtc = '2032-02-28T01:00:00.0000000+00:00'
    $invalid.reminderStates.'calendar-countdown-last'.lastDeliveredOccurrenceKey = 'calendar-countdown-last|2032-02-28T01:00:00.0000000+00:00'
    ConvertFrom-CountdownSettingsObject -Settings $invalid | Out-Null
}
catch { $invalidCalendarCursorRejected = $true }
Assert-Equal $true $invalidCalendarCursorRejected 'Schema v10 rejects a calendar cursor that is not a typed occurrence'

$duplicateCalendarScheduleRejected = $false
try {
    $invalid = $calendarV10 | ConvertTo-Json -Depth 12 | ConvertFrom-Json
    $invalid.notes[0].reminderRules = @(
        [pscustomobject]@{ id = 'calendar-duplicate-a'; type = 'calendar'; frequency = 'weekly'; interval = 2; startDate = '2032-01-05'; localTime = '09:00'; weekdays = @('friday', 'monday') },
        [pscustomobject]@{ id = 'calendar-duplicate-b'; type = 'calendar'; frequency = 'WEEKLY'; interval = 2; startDate = '2032-01-05'; localTime = '09:00'; weekdays = @('Monday', 'Friday') }
    )
    ConvertFrom-CountdownSettingsObject -Settings $invalid | Out-Null
}
catch { $duplicateCalendarScheduleRejected = $true }
Assert-Equal $true $duplicateCalendarScheduleRejected 'Schema v10 rejects duplicate canonical calendar schedules independent of weekday order and case'

foreach ($mutation in @(
        @{ Name = 'interval-zero'; Apply = { param($rule) $rule.interval = 0 } },
        @{ Name = 'interval-too-large'; Apply = { param($rule) $rule.interval = 1000 } },
        @{ Name = 'interval-decimal'; Apply = { param($rule) $rule.interval = 1.0 } },
        @{ Name = 'interval-string'; Apply = { param($rule) $rule.interval = '1' } },
        @{ Name = 'weekly-empty'; Apply = { param($rule) $rule.frequency = 'weekly'; $rule | Add-Member -NotePropertyName weekdays -NotePropertyValue @() } },
        @{ Name = 'monthly-both-selectors'; Apply = { param($rule) $rule.frequency = 'monthly'; $rule | Add-Member -NotePropertyName day -NotePropertyValue 31; $rule | Add-Member -NotePropertyName lastDay -NotePropertyValue $true } },
        @{ Name = 'monthly-false-last-day'; Apply = { param($rule) $rule.frequency = 'monthly'; $rule | Add-Member -NotePropertyName lastDay -NotePropertyValue $false } },
        @{ Name = 'yearly-invalid-month'; Apply = { param($rule) $rule.frequency = 'yearly'; $rule | Add-Member -NotePropertyName month -NotePropertyValue 13; $rule | Add-Member -NotePropertyName day -NotePropertyValue 1 } },
        @{ Name = 'unsupported-frequency'; Apply = { param($rule) $rule.frequency = 'hourly' } },
        @{ Name = 'free-form-rrule'; Apply = { param($rule) $rule | Add-Member -NotePropertyName rrule -NotePropertyValue 'FREQ=DAILY;COUNT=3' } }
    )) {
    $calendarMutationRejected = $false
    try {
        $invalid = $multiRuleV10 | ConvertTo-Json -Depth 12 | ConvertFrom-Json
        $rule = [pscustomobject]@{ id = 'calendar-invalid'; type = 'calendar'; frequency = 'daily'; interval = 1; startDate = '2032-01-01'; localTime = '09:00' }
        & $mutation.Apply $rule
        $invalid.notes[0].reminderRules = @($rule)
        $invalid.reminderStates = [pscustomobject]@{}
        ConvertFrom-CountdownSettingsObject -Settings $invalid | Out-Null
    }
    catch { $calendarMutationRejected = $true }
    Assert-Equal $true $calendarMutationRejected "Schema v10 rejects invalid calendar rule $($mutation.Name)"
}

$noteRelativeV10Rejected = $false
try {
    $invalid = $multiRuleV10 | ConvertTo-Json -Depth 8 | ConvertFrom-Json
    $invalid.notes[0].reminderRules = @(
        [pscustomobject]@{ id = 'note-relative'; type = 'countdownRelative'; offsetMinutesBefore = 0 }
    )
    ConvertFrom-CountdownSettingsObject -Settings $invalid | Out-Null
}
catch { $noteRelativeV10Rejected = $true }
Assert-Equal $true $noteRelativeV10Rejected 'Schema v10 rejects countdown-relative rules on notes'

foreach ($invalidOffset in @(-1, 5256001, 1.0, '1')) {
    $invalidRelativeOffsetRejected = $false
    try {
        $invalid = $relativeV10 | ConvertTo-Json -Depth 8 | ConvertFrom-Json
        $invalid.countdowns[0].reminderRules[0].offsetMinutesBefore = $invalidOffset
        ConvertFrom-CountdownSettingsObject -Settings $invalid | Out-Null
    }
    catch { $invalidRelativeOffsetRejected = $true }
    Assert-Equal $true $invalidRelativeOffsetRejected "Schema v10 rejects invalid countdown-relative offset $invalidOffset"
}

$relativeForeignFieldRejected = $false
try {
    $invalid = $relativeV10 | ConvertTo-Json -Depth 8 | ConvertFrom-Json
    $invalid.countdowns[0].reminderRules[0] | Add-Member -NotePropertyName scheduledUtc -NotePropertyValue '2099-01-01T00:10:00.0000000+00:00'
    ConvertFrom-CountdownSettingsObject -Settings $invalid | Out-Null
}
catch { $relativeForeignFieldRejected = $true }
Assert-Equal $true $relativeForeignFieldRejected 'Schema v10 rejects foreign fields on countdown-relative rules'

$unknownV10RuleRejected = $false
try {
    $invalid = $multiRuleV10 | ConvertTo-Json -Depth 8 | ConvertFrom-Json
    $invalid.notes[0].reminderRules[0].type = 'rfc5545'
    ConvertFrom-CountdownSettingsObject -Settings $invalid | Out-Null
}
catch { $unknownV10RuleRejected = $true }
Assert-Equal $true $unknownV10RuleRejected 'Schema v10 rejects unknown reminder rule types'

$duplicateV10ScheduleRejected = $false
try {
    $invalid = $multiRuleV10 | ConvertTo-Json -Depth 8 | ConvertFrom-Json
    $invalid.notes[0].reminderRules = @(
        [pscustomobject]@{ id = 'duplicate-a'; type = 'absoluteOnce'; scheduledUtc = '2099-01-01T00:00:00.0000000+00:00' },
        [pscustomobject]@{ id = 'duplicate-b'; type = 'absoluteOnce'; scheduledUtc = '2099-01-01T00:00:00.0000000+00:00' }
    )
    ConvertFrom-CountdownSettingsObject -Settings $invalid | Out-Null
}
catch { $duplicateV10ScheduleRejected = $true }
Assert-Equal $true $duplicateV10ScheduleRejected 'Schema v10 rejects duplicate canonical schedules on one record'

$tooManyV10RulesRejected = $false
try {
    $invalid = $multiRuleV10 | ConvertTo-Json -Depth 8 | ConvertFrom-Json
    $invalid.notes[0].reminderRules = @(0..8 | ForEach-Object {
            [pscustomobject]@{ id = "rule-$_"; type = 'absoluteOnce'; scheduledUtc = ([DateTimeOffset]'2099-01-01T00:00:00Z').AddMinutes($_).ToString('o') }
        })
    ConvertFrom-CountdownSettingsObject -Settings $invalid | Out-Null
}
catch { $tooManyV10RulesRejected = $true }
Assert-Equal $true $tooManyV10RulesRejected 'Schema v10 rejects more than eight reminder rules on one record'

$duplicateGlobalV10RuleIdRejected = $false
try {
    $invalid = $multiRuleV10 | ConvertTo-Json -Depth 8 | ConvertFrom-Json
    $invalid.countdowns[0].reminderRules = @([pscustomobject]@{ id = 'multi-absolute'; type = 'absoluteOnce'; scheduledUtc = '2099-02-01T00:00:00.0000000+00:00' })
    ConvertFrom-CountdownSettingsObject -Settings $invalid | Out-Null
}
catch { $duplicateGlobalV10RuleIdRejected = $true }
Assert-Equal $true $duplicateGlobalV10RuleIdRejected 'Schema v10 rejects duplicate global reminder rule IDs'

$danglingV10StateRejected = $false
try {
    $invalid = $multiRuleV10 | ConvertTo-Json -Depth 8 | ConvertFrom-Json
    $invalid.reminderStates | Add-Member -NotePropertyName 'missing-rule' -NotePropertyValue ([pscustomobject]@{ handledThroughUtc = '2099-01-01T00:00:00.0000000+00:00'; lastDeliveredOccurrenceKey = $null; lastDispositionOccurrenceKey = $null; activeSnooze = $null })
    ConvertFrom-CountdownSettingsObject -Settings $invalid | Out-Null
}
catch { $danglingV10StateRejected = $true }
Assert-Equal $true $danglingV10StateRejected 'Schema v10 rejects dangling reminder state'

$emptyV10StateRejected = $false
try {
    $invalid = $multiRuleV10 | ConvertTo-Json -Depth 8 | ConvertFrom-Json
    $invalid.reminderStates | Add-Member -NotePropertyName 'multi-absolute' -NotePropertyValue ([pscustomobject]@{ handledThroughUtc = $null; lastDeliveredOccurrenceKey = $null; lastDispositionOccurrenceKey = $null; activeSnooze = $null })
    ConvertFrom-CountdownSettingsObject -Settings $invalid | Out-Null
}
catch { $emptyV10StateRejected = $true }
Assert-Equal $true $emptyV10StateRejected 'Schema v10 rejects empty reminder state'

$earlyV10CursorRejected = $false
try {
    $invalid = $multiRuleV10 | ConvertTo-Json -Depth 8 | ConvertFrom-Json
    $invalid.reminderStates | Add-Member -NotePropertyName 'multi-absolute' -NotePropertyValue ([pscustomobject]@{ handledThroughUtc = '2098-12-31T23:59:59.0000000+00:00'; lastDeliveredOccurrenceKey = $null; lastDispositionOccurrenceKey = $null; activeSnooze = $null })
    ConvertFrom-CountdownSettingsObject -Settings $invalid | Out-Null
}
catch { $earlyV10CursorRejected = $true }
Assert-Equal $true $earlyV10CursorRejected 'Schema v10 rejects a cursor before the first scheduled occurrence'

foreach ($misalignedCursor in @(
        [pscustomobject]@{ RuleId = 'multi-absolute'; HandledThroughUtc = '2099-01-01T00:01:00.0000000+00:00'; Name = 'absolute once cursor after its unique occurrence' },
        [pscustomobject]@{ RuleId = 'multi-fixed'; HandledThroughUtc = '2099-01-02T00:01:00.0000000+00:00'; Name = 'fixed interval cursor between occurrences' }
    )) {
    $misalignedV10CursorRejected = $false
    try {
        $invalid = $multiRuleV10 | ConvertTo-Json -Depth 8 | ConvertFrom-Json
        $invalid.reminderStates | Add-Member -NotePropertyName $misalignedCursor.RuleId -NotePropertyValue ([pscustomobject]@{
                handledThroughUtc = $misalignedCursor.HandledThroughUtc
                lastDeliveredOccurrenceKey = $null
                lastDispositionOccurrenceKey = $null
                activeSnooze = $null
            })
        $invalidRawJson = $invalid | ConvertTo-Json -Depth 8
        ConvertFrom-CountdownSettingsObject -Settings ($invalidRawJson | ConvertFrom-Json) | Out-Null
    }
    catch { $misalignedV10CursorRejected = $true }
    Assert-Equal $true $misalignedV10CursorRejected "Schema v10 rejects $($misalignedCursor.Name)"
}

$alignedV10Cursors = $multiRuleV10 | ConvertTo-Json -Depth 8 | ConvertFrom-Json
$alignedV10Cursors.reminderStates | Add-Member -NotePropertyName 'multi-absolute' -NotePropertyValue ([pscustomobject]@{
        handledThroughUtc = '2099-01-01T00:00:00.0000000+00:00'
        lastDeliveredOccurrenceKey = $null
        lastDispositionOccurrenceKey = $null
        activeSnooze = $null
    })
$alignedV10Cursors.reminderStates | Add-Member -NotePropertyName 'multi-fixed' -NotePropertyValue ([pscustomobject]@{
        handledThroughUtc = '2099-01-02T01:30:00.0000000+00:00'
        lastDeliveredOccurrenceKey = $null
        lastDispositionOccurrenceKey = $null
        activeSnooze = $null
    })
$loadedAlignedV10Cursors = ConvertFrom-CountdownSettingsObject -Settings (($alignedV10Cursors | ConvertTo-Json -Depth 8) | ConvertFrom-Json)
Assert-Equal ([DateTimeOffset]'2099-01-01T00:00:00Z') ([DateTimeOffset] $loadedAlignedV10Cursors.ReminderStates['multi-absolute'].handledThroughUtc).ToUniversalTime() 'Schema v10 accepts an aligned absolute once cursor'
Assert-Equal ([DateTimeOffset]'2099-01-02T01:30:00Z') ([DateTimeOffset] $loadedAlignedV10Cursors.ReminderStates['multi-fixed'].handledThroughUtc).ToUniversalTime() 'Schema v10 accepts an aligned fixed interval cursor'

$disposedV10 = $multiRuleV10 | ConvertTo-Json -Depth 8 | ConvertFrom-Json
$disposedV10.reminderStates | Add-Member -NotePropertyName 'multi-absolute' -NotePropertyValue ([pscustomobject]@{
        handledThroughUtc = '2099-01-01T00:00:00.0000000+00:00'
        lastDeliveredOccurrenceKey = 'multi-absolute|2099-01-01T00:00:00.0000000+00:00'
        lastDispositionOccurrenceKey = 'multi-absolute|2099-01-01T00:00:00.0000000+00:00'
        activeSnooze = $null
    })
$loadedDisposedV10 = ConvertFrom-CountdownSettingsObject -Settings $disposedV10
$canonicalDisposedV10 = ConvertTo-CountdownSettingsJson -State $loadedDisposedV10 | ConvertFrom-Json
Assert-Equal 'multi-absolute|2099-01-01T00:00:00.0000000+00:00' ([string] $canonicalDisposedV10.reminderStates.'multi-absolute'.lastDispositionOccurrenceKey) 'Schema v10 round-trips disposition separately from delivery'
Assert-Equal $null $canonicalDisposedV10.reminderStates.'multi-absolute'.activeSnooze 'Schema v10 writes an explicit empty active snooze'

$legacyAcknowledgedV10 = $disposedV10 | ConvertTo-Json -Depth 8 | ConvertFrom-Json
$legacyDisposition = [string] $legacyAcknowledgedV10.reminderStates.'multi-absolute'.lastDispositionOccurrenceKey
$legacyAcknowledgedV10.reminderStates.'multi-absolute'.PSObject.Properties.Remove('lastDispositionOccurrenceKey')
$legacyAcknowledgedV10.reminderStates.'multi-absolute'.PSObject.Properties.Remove('activeSnooze')
$legacyAcknowledgedV10.reminderStates.'multi-absolute' | Add-Member -NotePropertyName 'lastAcknowledgedOccurrenceKey' -NotePropertyValue $legacyDisposition
$legacyAcknowledgedCanonical = ConvertTo-CountdownSettingsJson -State (
    ConvertFrom-CountdownSettingsObject -Settings $legacyAcknowledgedV10
) | ConvertFrom-Json
Assert-Equal $legacyDisposition ([string] $legacyAcknowledgedCanonical.reminderStates.'multi-absolute'.lastDispositionOccurrenceKey) 'Schema v10 accepts provisional acknowledgement input and writes canonical disposition'
Assert-Equal $null $legacyAcknowledgedCanonical.reminderStates.'multi-absolute'.activeSnooze 'Schema v10 canonicalizes provisional acknowledgement input with empty snooze state'
Assert-Equal $null $legacyAcknowledgedCanonical.reminderStates.'multi-absolute'.PSObject.Properties['lastAcknowledgedOccurrenceKey'] 'Schema v10 canonical output omits the provisional acknowledgement field'

$missingDispositionFieldRejected = $false
try {
    $invalid = $disposedV10 | ConvertTo-Json -Depth 8 | ConvertFrom-Json
    $invalid.reminderStates.'multi-absolute'.PSObject.Properties.Remove('lastDispositionOccurrenceKey')
    ConvertFrom-CountdownSettingsObject -Settings $invalid | Out-Null
}
catch { $missingDispositionFieldRejected = $true }
Assert-Equal $true $missingDispositionFieldRejected 'Schema v10 strictly requires the canonical disposition field'

foreach ($invalidDisposition in @(
        [pscustomobject]@{ Value = 'multi-fixed|2099-01-01T00:00:00.0000000+00:00'; Name = 'a different rule disposition' },
        [pscustomobject]@{ Value = 'multi-absolute|2099-01-01T08:00:00.0000000+08:00'; Name = 'a non-UTC disposition' }
    )) {
    $invalidDispositionRejected = $false
    try {
        $invalid = $disposedV10 | ConvertTo-Json -Depth 8 | ConvertFrom-Json
        $invalid.reminderStates.'multi-absolute'.lastDispositionOccurrenceKey = $invalidDisposition.Value
        ConvertFrom-CountdownSettingsObject -Settings $invalid | Out-Null
    }
    catch { $invalidDispositionRejected = $true }
    Assert-Equal $true $invalidDispositionRejected "Schema v10 rejects $($invalidDisposition.Name)"
}

$dispositionWithoutDeliveryRejected = $false
try {
    $invalid = $disposedV10 | ConvertTo-Json -Depth 8 | ConvertFrom-Json
    $invalid.reminderStates.'multi-absolute'.lastDeliveredOccurrenceKey = $null
    ConvertFrom-CountdownSettingsObject -Settings $invalid | Out-Null
}
catch { $dispositionWithoutDeliveryRejected = $true }
Assert-Equal $true $dispositionWithoutDeliveryRejected 'Schema v10 rejects disposition without a delivery fact'

$dispositionAfterDeliveryRejected = $false
try {
    $invalid = $multiRuleV10 | ConvertTo-Json -Depth 8 | ConvertFrom-Json
    $invalid.reminderStates | Add-Member -NotePropertyName 'multi-fixed' -NotePropertyValue ([pscustomobject]@{
            handledThroughUtc = '2099-01-02T01:30:00.0000000+00:00'
            lastDeliveredOccurrenceKey = 'multi-fixed|2099-01-02T01:00:00.0000000+00:00'
            lastDispositionOccurrenceKey = 'multi-fixed|2099-01-02T01:30:00.0000000+00:00'
            activeSnooze = $null
        })
    ConvertFrom-CountdownSettingsObject -Settings $invalid | Out-Null
}
catch { $dispositionAfterDeliveryRejected = $true }
Assert-Equal $true $dispositionAfterDeliveryRejected 'Schema v10 rejects disposition after the last delivered occurrence'

$snoozedV10 = $multiRuleV10 | ConvertTo-Json -Depth 8 | ConvertFrom-Json
$snoozedV10.reminderStates | Add-Member -NotePropertyName 'multi-fixed' -NotePropertyValue ([pscustomobject]@{
        handledThroughUtc = '2099-01-02T01:30:00.0000000+00:00'
        lastDeliveredOccurrenceKey = 'multi-fixed|2099-01-02T01:30:00.0000000+00:00'
        lastDispositionOccurrenceKey = 'multi-fixed|2099-01-02T00:45:00.0000000+00:00'
        activeSnooze = [pscustomobject]@{
            occurrenceKey = 'multi-fixed|2099-01-02T01:30:00.0000000+00:00'
            originalScheduledUtc = '2099-01-02T01:30:00.0000000+00:00'
            snoozeUntilUtc = '2099-01-02T02:00:00.0000000+00:00'
        }
    })
$canonicalSnoozedV10 = ConvertTo-CountdownSettingsJson -State (
    ConvertFrom-CountdownSettingsObject -Settings $snoozedV10
) | ConvertFrom-Json
Assert-Equal 'multi-fixed|2099-01-02T01:30:00.0000000+00:00' ([string] $canonicalSnoozedV10.reminderStates.'multi-fixed'.activeSnooze.occurrenceKey) 'Schema v10 round-trips an active snooze occurrence key'
Assert-Equal ([DateTimeOffset]'2099-01-02T01:30:00Z') ([DateTimeOffset] $canonicalSnoozedV10.reminderStates.'multi-fixed'.activeSnooze.originalScheduledUtc).ToUniversalTime() 'Schema v10 round-trips an active snooze original UTC'
Assert-Equal ([DateTimeOffset]'2099-01-02T02:00:00Z') ([DateTimeOffset] $canonicalSnoozedV10.reminderStates.'multi-fixed'.activeSnooze.snoozeUntilUtc).ToUniversalTime() 'Schema v10 round-trips an active snooze override UTC'

foreach ($invalidSnooze in @(
        [pscustomobject]@{ Name = 'a snooze key for a different rule'; Mutate = { param($state) $state.activeSnooze.occurrenceKey = 'multi-absolute|2099-01-02T01:30:00.0000000+00:00' } },
        [pscustomobject]@{ Name = 'a snooze key that differs from original schedule'; Mutate = { param($state) $state.activeSnooze.originalScheduledUtc = '2099-01-02T00:45:00.0000000+00:00' } },
        [pscustomobject]@{ Name = 'a snooze override not later than the original schedule'; Mutate = { param($state) $state.activeSnooze.snoozeUntilUtc = '2099-01-02T01:30:00.0000000+00:00' } },
        [pscustomobject]@{ Name = 'a snooze occurrence already disposed'; Mutate = { param($state) $state.lastDispositionOccurrenceKey = 'multi-fixed|2099-01-02T01:30:00.0000000+00:00' } },
        [pscustomobject]@{ Name = 'a snooze occurrence without matching delivery'; Mutate = { param($state) $state.lastDeliveredOccurrenceKey = 'multi-fixed|2099-01-02T00:45:00.0000000+00:00' } }
    )) {
    $invalidSnoozeRejected = $false
    try {
        $invalid = $snoozedV10 | ConvertTo-Json -Depth 8 | ConvertFrom-Json
        & $invalidSnooze.Mutate $invalid.reminderStates.'multi-fixed'
        ConvertFrom-CountdownSettingsObject -Settings $invalid | Out-Null
    }
    catch { $invalidSnoozeRejected = $true }
    Assert-Equal $true $invalidSnoozeRejected "Schema v10 rejects $($invalidSnooze.Name)"
}

$mismatchedV10OccurrenceRejected = $false
try {
    $invalid = $multiRuleV10 | ConvertTo-Json -Depth 8 | ConvertFrom-Json
    $invalid.reminderStates | Add-Member -NotePropertyName 'multi-absolute' -NotePropertyValue ([pscustomobject]@{
            handledThroughUtc = '2099-01-01T00:00:00.0000000+00:00'
            lastDeliveredOccurrenceKey = 'multi-fixed|2099-01-01T00:00:00.0000000+00:00'
            lastDispositionOccurrenceKey = $null
            activeSnooze = $null
        })
    ConvertFrom-CountdownSettingsObject -Settings $invalid | Out-Null
}
catch { $mismatchedV10OccurrenceRejected = $true }
Assert-Equal $true $mismatchedV10OccurrenceRejected 'Schema v10 rejects an occurrence key for a different reminder rule'

$nonUtcV10OccurrenceRejected = $false
try {
    $invalid = $multiRuleV10 | ConvertTo-Json -Depth 8 | ConvertFrom-Json
    $invalid.reminderStates | Add-Member -NotePropertyName 'multi-absolute' -NotePropertyValue ([pscustomobject]@{
            handledThroughUtc = '2099-01-01T00:00:00.0000000+00:00'
            lastDeliveredOccurrenceKey = 'multi-absolute|2099-01-01T08:00:00.0000000+08:00'
            lastDispositionOccurrenceKey = $null
            activeSnooze = $null
        })
    ConvertFrom-CountdownSettingsObject -Settings $invalid | Out-Null
}
catch { $nonUtcV10OccurrenceRejected = $true }
Assert-Equal $true $nonUtcV10OccurrenceRejected 'Schema v10 rejects a non-UTC occurrence key even when it names the same instant'

foreach ($nonIntegerJson in @('1.0', '1e0')) {
    $nonIntegerV10IntervalRejected = $false
    try {
        $invalidJson = ($multiRuleV10 | ConvertTo-Json -Depth 8).Replace('"intervalMinutes": 45', "`"intervalMinutes`": $nonIntegerJson")
        ConvertFrom-CountdownSettingsObject -Settings ($invalidJson | ConvertFrom-Json) | Out-Null
    }
    catch { $nonIntegerV10IntervalRejected = $true }
    Assert-Equal $true $nonIntegerV10IntervalRejected "Schema v10 rejects non-integer JSON intervalMinutes $nonIntegerJson"
}

foreach ($integerInterval in @([int] 45, [long] 45)) {
    $validIntegerInterval = $multiRuleV10 | ConvertTo-Json -Depth 8 | ConvertFrom-Json
    $validIntegerInterval.notes[0].reminderRules[1].intervalMinutes = $integerInterval
    $loadedIntegerInterval = ConvertFrom-CountdownSettingsObject -Settings $validIntegerInterval
    Assert-Equal 45 $loadedIntegerInterval.Notes[0].reminderRules[1].intervalMinutes "Schema v10 accepts $($integerInterval.GetType().Name) intervalMinutes"
}

$missingV6NoteDateRejected = $false
try {
    $missingV6NoteDate = $v6Settings | ConvertTo-Json -Depth 5 | ConvertFrom-Json
    $missingV6NoteDate.notes[0].PSObject.Properties.Remove('date')
    ConvertFrom-CountdownSettingsObject -Settings $missingV6NoteDate | Out-Null
}
catch {
    $missingV6NoteDateRejected = $true
}
Assert-Equal $true $missingV6NoteDateRejected 'Schema v6 requires a note date field'
$invalidV6NoteDateRejected = $false
try {
    $invalidV6NoteDate = $v6Settings | ConvertTo-Json -Depth 5 | ConvertFrom-Json
    $invalidV6NoteDate.notes[0].date = '2026-7-18'
    ConvertFrom-CountdownSettingsObject -Settings $invalidV6NoteDate | Out-Null
}
catch {
    $invalidV6NoteDateRejected = $true
}
Assert-Equal $true $invalidV6NoteDateRejected 'Schema v6 rejects a non-canonical note date'
$blankV6NoteDateRejected = $false
try {
    $blankV6NoteDate = $v6Settings | ConvertTo-Json -Depth 5 | ConvertFrom-Json
    $blankV6NoteDate.notes[0].date = '   '
    ConvertFrom-CountdownSettingsObject -Settings $blankV6NoteDate | Out-Null
}
catch {
    $blankV6NoteDateRejected = $true
}
Assert-Equal $true $blankV6NoteDateRejected 'Schema v6 rejects a blank note date'

$missingV7ReminderFieldsRejected = $false
try {
    $missingV7ReminderFields = $v7Settings | ConvertTo-Json -Depth 5 | ConvertFrom-Json
    $missingV7ReminderFields.notes[0].PSObject.Properties.Remove('reminderIntervalMinutes')
    ConvertFrom-CountdownSettingsObject -Settings $missingV7ReminderFields | Out-Null
}
catch {
    $missingV7ReminderFieldsRejected = $true
}
Assert-Equal $true $missingV7ReminderFieldsRejected 'Schema v7 requires reminder fields'
$invalidV7ReminderTypeRejected = $false
try {
    $invalidV7ReminderType = $v7Settings | ConvertTo-Json -Depth 5 | ConvertFrom-Json
    $invalidV7ReminderType.notes[0].reminderIntervalMinutes = '30'
    ConvertFrom-CountdownSettingsObject -Settings $invalidV7ReminderType | Out-Null
}
catch {
    $invalidV7ReminderTypeRejected = $true
}
Assert-Equal $true $invalidV7ReminderTypeRejected 'Schema v7 rejects a string reminder interval'

$missingV5TagsRejected = $false
try {
    $missingV5Tags = $v5Settings | Select-Object * -ExcludeProperty tags
    ConvertFrom-CountdownSettingsObject -Settings $missingV5Tags | Out-Null
}
catch {
    $missingV5TagsRejected = $true
}
Assert-Equal $true $missingV5TagsRejected 'Schema v5 requires a tag library'
$duplicateTagIdRejected = $false
try {
    $duplicateTagIdSettings = $v5Settings | ConvertTo-Json -Depth 5 | ConvertFrom-Json
    $duplicateTagIdSettings.tags += [pscustomobject]@{ id = 'WORK'; name = '第二个工作'; color = $null }
    ConvertFrom-CountdownSettingsObject -Settings $duplicateTagIdSettings | Out-Null
}
catch {
    $duplicateTagIdRejected = $true
}
Assert-Equal $true $duplicateTagIdRejected 'Schema v5 rejects duplicate tag IDs without case sensitivity'
$duplicateTagNameRejected = $false
try {
    $duplicateTagNameSettings = $v5Settings | ConvertTo-Json -Depth 5 | ConvertFrom-Json
    $duplicateTagNameSettings.tags += [pscustomobject]@{ id = 'office'; name = '工作'; color = $null }
    ConvertFrom-CountdownSettingsObject -Settings $duplicateTagNameSettings | Out-Null
}
catch {
    $duplicateTagNameRejected = $true
}
Assert-Equal $true $duplicateTagNameRejected 'Schema v5 rejects duplicate tag names without case sensitivity'
$danglingCountdownTagRejected = $false
try {
    $danglingCountdownTagSettings = $v5Settings | ConvertTo-Json -Depth 5 | ConvertFrom-Json
    $danglingCountdownTagSettings.countdowns[0].tagIds = @('missing')
    ConvertFrom-CountdownSettingsObject -Settings $danglingCountdownTagSettings | Out-Null
}
catch {
    $danglingCountdownTagRejected = $true
}
Assert-Equal $true $danglingCountdownTagRejected 'Schema v5 rejects dangling countdown tag IDs'
$duplicateNoteTagRejected = $false
try {
    $duplicateNoteTagSettings = $v5Settings | ConvertTo-Json -Depth 5 | ConvertFrom-Json
    $duplicateNoteTagSettings.notes[0].tagIds = @('home', 'HOME')
    ConvertFrom-CountdownSettingsObject -Settings $duplicateNoteTagSettings | Out-Null
}
catch {
    $duplicateNoteTagRejected = $true
}
Assert-Equal $true $duplicateNoteTagRejected 'Schema v5 rejects duplicate note tag IDs without case sensitivity'
$invalidTagColorSettingsRejected = $false
try {
    $invalidTagColorSettings = $v5Settings | ConvertTo-Json -Depth 5 | ConvertFrom-Json
    $invalidTagColorSettings.tags[0].color = 'blue'
    ConvertFrom-CountdownSettingsObject -Settings $invalidTagColorSettings | Out-Null
}
catch {
    $invalidTagColorSettingsRejected = $true
}
Assert-Equal $true $invalidTagColorSettingsRejected 'Schema v5 validates tag colors through tag records'

$datedNote = New-CountdownNoteRecord -Id 'dated-note' -Text '带日期便签' -Date '2026-07-18' -TagIds @('work')
Assert-Equal '2026-07-18' $datedNote.date 'Note records preserve an exact calendar date'
$reminderStart = [DateTimeOffset]'2026-07-19T08:00:00+08:00'
$reminderNote = New-CountdownNoteRecord -Id 'reminder-note' -Text '每半小时站起来活动' -Date '2026-07-19' -TagIds @() -ReminderIntervalMinutes 30 -ReminderStartUtc $reminderStart
Assert-Equal 30 $reminderNote.reminderIntervalMinutes 'Note records preserve a reminder interval'
Assert-Equal ([DateTimeOffset]'2026-07-19T00:00:00Z') $reminderNote.reminderStartUtc 'Note records normalize reminder start to UTC'
$dueBeforeInterval = @(Get-DueCountdownNoteReminders -Notes @($reminderNote) -BeijingTime ([DateTimeOffset]'2026-07-19T08:29:59+08:00') -LastShownCycles @{})
Assert-Equal 0 $dueBeforeInterval.Count 'Note reminder does not fire before its interval'
$dueAtInterval = @(Get-DueCountdownNoteReminders -Notes @($reminderNote) -BeijingTime ([DateTimeOffset]'2026-07-19T08:30:00+08:00') -LastShownCycles @{})
Assert-Equal 1 $dueAtInterval.Count 'Note reminder fires when its interval elapses'
Assert-Equal 1 $dueAtInterval[0].Cycle 'First note reminder reports cycle one'
$dueAlreadyShown = @(Get-DueCountdownNoteReminders -Notes @($reminderNote) -BeijingTime ([DateTimeOffset]'2026-07-19T08:30:00+08:00') -LastShownCycles @{ 'reminder-note' = 1 })
Assert-Equal 0 $dueAlreadyShown.Count 'Note reminder does not repeat within the same cycle'
$dueNextCycle = @(Get-DueCountdownNoteReminders -Notes @($reminderNote) -BeijingTime ([DateTimeOffset]'2026-07-19T09:00:00+08:00') -LastShownCycles @{ 'reminder-note' = 1 })
Assert-Equal 1 $dueNextCycle.Count 'Note reminder fires again at the next interval'
$completedReminderNote = New-CountdownNoteRecord -Id 'completed-reminder-note' -Text '已经完成' -Date '2026-07-19' -IsCompleted $true -TagIds @() -ReminderIntervalMinutes 30 -ReminderStartUtc $reminderStart
Assert-Equal 0 @(Get-DueCountdownNoteReminders -Notes @($completedReminderNote) -BeijingTime ([DateTimeOffset]'2026-07-19T09:00:00+08:00') -LastShownCycles @{}).Count 'Completed notes do not produce reminders'
$invalidReminderIntervalRejected = $false
try {
    New-CountdownNoteRecord -Id 'invalid-reminder-note' -Text '无效提醒' -Date '2026-07-19' -TagIds @() -ReminderIntervalMinutes 0 -ReminderStartUtc $reminderStart | Out-Null
}
catch {
    $invalidReminderIntervalRejected = $true
}
Assert-Equal $true $invalidReminderIntervalRejected 'Note records reject reminder intervals below one minute'
$leapDayNote = New-CountdownNoteRecord -Id 'leap-day-note' -Text '闰日便签' -Date '2028-02-29' -TagIds @()
Assert-Equal '2028-02-29' $leapDayNote.date 'Note records accept a valid leap day'
$invalidNoteDateRejected = $false
try {
    New-CountdownNoteRecord -Id 'invalid-date-note' -Text '无效日期便签' -Date '2026-7-18' -TagIds @() | Out-Null
}
catch {
    $invalidNoteDateRejected = $true
}
Assert-Equal $true $invalidNoteDateRejected 'Note records reject non-canonical calendar dates'
$invalidCalendarDateRejected = $false
try {
    New-CountdownNoteRecord -Id 'invalid-calendar-note' -Text '不存在日期便签' -Date '2026-02-29' -TagIds @() | Out-Null
}
catch {
    $invalidCalendarDateRejected = $true
}
Assert-Equal $true $invalidCalendarDateRejected 'Note records reject an impossible calendar date'

$originalNotes = [System.Collections.ArrayList]::new()
[void] $originalNotes.Add([pscustomobject]@{ id = 'existing'; text = '已有便签'; date = '2026-07-17'; isCompleted = $false; tagIds = @('work') })
$missingAddDateRejected = $false
try {
    Invoke-NoteMutation -Notes $originalNotes -Action Add -Text '缺少日期' -TagIds @() -PersistCandidate { param($candidate) } | Out-Null
}
catch {
    $missingAddDateRejected = $true
}
Assert-Equal $true $missingAddDateRejected 'Note add requires a calendar date'
$persistedCandidates = [System.Collections.ArrayList]::new()
$addResult = Invoke-NoteMutation -Notes $originalNotes -Action Add -Text '新增便签' -Date '2026-07-18' -TagIds @('home') -PersistCandidate {
    param($candidate)
    [void] $persistedCandidates.Add($candidate)
}
Assert-Equal 1 $originalNotes.Count 'Note add does not mutate source before commit'
Assert-Equal 2 $addResult.Notes.Count 'Note add returns candidate notes'
Assert-Equal $true $addResult.Changed 'Note add reports change'
Assert-Equal 1 $persistedCandidates.Count 'Note add persists one candidate'
Assert-Equal 'home' $addResult.Notes[1].tagIds[0] 'Note add preserves selected tag IDs'
Assert-Equal '2026-07-18' $addResult.Notes[1].date 'Note add preserves the selected calendar date'
$updateResult = Invoke-NoteMutation -Notes $addResult.Notes -Action Update -Id 'existing' -Text '已有便签' -Date '2026-07-19' -TagIds @('work') -PersistCandidate {
    param($candidate)
    [void] $persistedCandidates.Add($candidate)
}
Assert-Equal '2026-07-17' $addResult.Notes[0].date 'Note update does not mutate the source date before commit'
Assert-Equal '2026-07-19' $updateResult.Notes[0].date 'Note update preserves the selected calendar date'
$completionResult = Invoke-NoteMutation -Notes $updateResult.Notes -Action SetCompletion -Id 'existing' -IsCompleted $true -PersistCandidate {
    param($candidate)
    [void] $persistedCandidates.Add($candidate)
}
Assert-Equal $false ([bool] $updateResult.Notes[0].isCompleted) 'Note completion does not mutate source before commit'
Assert-Equal $true ([bool] $completionResult.Notes[0].isCompleted) 'Note completion returns changed candidate'
Assert-Equal 'work' $completionResult.Notes[0].tagIds[0] 'Note completion retains existing tag IDs'
Assert-Equal '2026-07-19' $completionResult.Notes[0].date 'Note completion retains the existing calendar date'
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
Assert-Equal '2026-07-19' $completionResult.Notes[0].date 'Note persistence failure preserves the source calendar date'
Assert-Equal 1 $failedPersistCalls.Count 'Note persistence failure invokes persistence once'

if ($failures.Count -gt 0) {
    $failures | ForEach-Object { Write-Error $_ }
    exit 1
}

[pscustomobject]@{ Passed = $script:assertions; Failed = 0; TimeZone = (Get-BeijingNow).Offset.ToString() } | ConvertTo-Json
