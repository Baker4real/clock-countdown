# Countdown.Core.Tests.ps1 的 Validation 分段：入口按开关点源执行，共用入口的参数、断言函数与已建立的状态，不单独运行。

# 运行根自封闭（参照 transactionTestRoot 的 $PSScriptRoot + NewGuid 模式）：夹具与证据
# 写读同一次运行的同一根，不再依赖 migration-preflight 时代的固定绝对根（目录已加入
# .gitignore，崩溃残留不污染 git status）。
$validationEvidenceRoot = Join-Path $PSScriptRoot 'validation-suite-test-runs'
$validationTestRoot = Join-Path $validationEvidenceRoot ([guid]::NewGuid().ToString('N'))
$utf8NoBom = [System.Text.UTF8Encoding]::new($false)
$originalFixtureRecord = $env:COUNTDOWN_FAKE_DOTNET_RECORD
$originalValidationOrder = $env:COUNTDOWN_VALIDATION_ORDER_PATH
$originalFixtureFailure = $env:COUNTDOWN_FAKE_DOTNET_FAIL
$originalDotnetCliHome = $env:DOTNET_CLI_HOME
$originalValidationArtifactMode = $env:COUNTDOWN_VALIDATION_ARTIFACT_MODE
$originalValidationArtifactRecord = $env:COUNTDOWN_VALIDATION_ARTIFACT_RECORD
$originalValidationSiblingMarker = $env:COUNTDOWN_VALIDATION_SIBLING_MARKER
# 隔离断言自封闭：快照入口标记（启动器 -AllowUnisolatedDesktop 调试时会带上）并在 finally
# 恢复；验证套件自身从不注入该标记，窗口化子步骤必须进各自的隔离桌面。
$originalSuiteAllowUnisolated = $env:MINMIN_SUITE_ALLOW_UNISOLATED
$env:MINMIN_SUITE_ALLOW_UNISOLATED = $null
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
        FloatingWindowPresent = $true
    }
    foreach ($baselineName in @('Main', 'Floating')) {
        $validationApplicationReport["${baselineName}ReferenceBaselineRequested"] = $false
        $validationApplicationReport["${baselineName}ReferenceBaselineManifestAvailable"] = $false
        $validationApplicationReport["${baselineName}ReferenceBaselineValidated"] = $false
        $validationApplicationReport["${baselineName}ReferenceBaselineCaptures"] = @()
    }
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

    $sourceTreeValidationReport = Copy-CountdownApplicationValidationReportFixture
    foreach ($baselineName in @('Main', 'Floating')) {
        $sourceTreeValidationReport."${baselineName}ReferenceBaselineRequested" = $true
        $sourceTreeValidationReport."${baselineName}ReferenceBaselineManifestAvailable" = $true
        $sourceTreeValidationReport."${baselineName}ReferenceBaselineValidated" = $true
    }
    $sourceTreeValidationReport.MainReferenceBaselineCaptures = @(
        1..9 | ForEach-Object { [pscustomobject]@{ CaptureId = "main-$_" } }
    )
    $sourceTreeValidationReport.FloatingReferenceBaselineCaptures = @(
        1..5 | ForEach-Object { [pscustomobject]@{ CaptureId = "floating-$_" } }
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
        '应用 validate-only 的悬浮窗参考基线必须包含 5 个 captures，实际为 0。' `
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

    $validationApplicationFixture = $validationApplicationJson.Replace("'", "''")
    $validationFixtures = [ordered]@{
        'Countdown.Core.Tests.ps1' = "'{`"Passed`":11,`"Failed`":0}'"
        'Countdown.CSharp.Tests.ps1' = @"
if (-not ('SuiteEvidenceDesktopProbe' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
using System.Text;
public static class SuiteEvidenceDesktopProbe
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetThreadDesktop(uint threadId);
    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetUserObjectInformationW(IntPtr handle, int index, StringBuilder info, int maxLength, out uint lengthNeeded);
    public static string GetDesktopName()
    {
        IntPtr handle = GetThreadDesktop(GetCurrentThreadId());
        if (handle == IntPtr.Zero)
        {
            throw new InvalidOperationException("GetThreadDesktop failed");
        }
        StringBuilder builder = new StringBuilder(256);
        uint lengthNeeded;
        if (!GetUserObjectInformationW(handle, 2, builder, 256, out lengthNeeded))
        {
            throw new InvalidOperationException("GetUserObjectInformationW failed");
        }
        return builder.ToString();
    }
}
'@
}
[System.IO.File]::AppendAllText(`$env:COUNTDOWN_VALIDATION_ORDER_PATH, 'csharp' + [Environment]::NewLine)
[System.IO.File]::WriteAllText(
    (Join-Path '$validationTestRoot' 'csharp-isolation-evidence.txt'),
    ('allowUnisolated=' + [Environment]::GetEnvironmentVariable('MINMIN_SUITE_ALLOW_UNISOLATED') +
        [Environment]::NewLine + 'desktop=' + [SuiteEvidenceDesktopProbe]::GetDesktopName()),
    [System.Text.UTF8Encoding]::new(`$false))
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
        -DotnetPath $fixtureDotnet
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
    $isolationEvidencePath = Join-Path $validationTestRoot 'csharp-isolation-evidence.txt'
    Assert-Equal $true (Test-Path -LiteralPath $isolationEvidencePath -PathType Leaf) 'Validation interface isolation evidence is written into the same validation run root'
    $isolationEvidenceLines = @()
    if (Test-Path -LiteralPath $isolationEvidencePath -PathType Leaf) {
        $isolationEvidenceLines = @((([System.IO.File]::ReadAllText($isolationEvidencePath, [System.Text.Encoding]::UTF8)) -split '\r?\n'))
    }
    # 判别式：隔离路径必把子进程放到 MinminSuiteIso-ValidateCSharp 桌面（与父桌面无关），
    # 且套件不注入隔离 opt-out 标记；直接启动会继承父桌面。
    Assert-Equal $true `
        ($isolationEvidenceLines -contains 'allowUnisolated=' -and
            $isolationEvidenceLines -ccontains 'desktop=MinminSuiteIso-ValidateCSharp') `
        'Validation interface keeps the windowed C# child on its isolated desktop'
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
[System.IO.File]::AppendAllText(`$env:COUNTDOWN_VALIDATION_ORDER_PATH, 'csharp' + [Environment]::NewLine)
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
    $env:MINMIN_SUITE_ALLOW_UNISOLATED = $originalSuiteAllowUnisolated
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
