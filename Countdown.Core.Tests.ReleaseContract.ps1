# Countdown.Core.Tests.ps1 的 ReleaseContract 分段：入口按开关点源执行，共用入口的参数、断言函数与已建立的状态，不单独运行。

$releaseContract = Get-CountdownReleaseContract
$expectedBuildAssets = @(
    'sanrio-official-mymelody.png=>app\assets\sanrio-official-mymelody.png',
    'sanrio-official-cinnamoroll.png=>app\assets\sanrio-official-cinnamoroll.png',
    'sanrio-official-kuromi.png=>app\assets\sanrio-official-kuromi.png',
    'assets\floating-plush\component-day-outer-shell.png=>app\assets\floating-plush\component-day-outer-shell.png',
    'assets\floating-plush\component-day-header-blank.png=>app\assets\floating-plush\component-day-header-blank.png',
    'assets\floating-plush\component-day-card-blank.png=>app\assets\floating-plush\component-day-card-blank.png',
    'assets\floating-plush\component-day-mascots.png=>app\assets\floating-plush\component-day-mascots.png',
    'assets\main-decorations\main-countdown-scene-day.png=>app\assets\main-decorations\main-countdown-scene-day.png',
    'assets\main-decorations\main-notes-scene-day.png=>app\assets\main-decorations\main-notes-scene-day.png'
)
$actualBuildAssets = @($releaseContract.BuildAssets | ForEach-Object { "$($_.SourcePath)=>$($_.PublishedPath)" })
$expectedCompatibilityFixtures = @(
    'Countdown.App.psm1',
    'Countdown.Core.psm1',
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
Assert-Equal 445 @($releaseContract.PackageFiles).Count 'Release contract declares exactly the office package: trust roots, install-path modules, launcher, holiday data, root character assets and the published payload'
Assert-Equal 442 $releaseContract.IntegrityHashes.Count 'Release contract pins every non-root office package file'
Assert-Equal $true ('src\MinminCountdown\NoteCompletionExit.cs' -in @($releaseContract.BuildInputFiles)) '#233 source rebuild checks the completion exit animation by commit identity'
Assert-Equal $true ('src\MinminCountdown\app.manifest' -in @($releaseContract.BuildInputFiles)) '#219 source rebuild checks the PerMonitorV2 manifest by commit identity'
foreach ($buildOnlyInput in @('src\MinminCountdown\NoteCompletionExit.cs', 'src\MinminCountdown\app.manifest')) {
    Assert-Equal $false ($buildOnlyInput -in @($releaseContract.PackageFiles)) "Build input stays out of the office package: $buildOnlyInput"
    Assert-Equal $false $releaseContract.IntegrityHashes.Contains($buildOnlyInput) "Build input is not statically pinned: $buildOnlyInput"
}
Assert-Equal $false ('assets\floating-plush\SOURCES.md' -in @($releaseContract.PackageFiles)) 'Plush source metadata stays in the repository, outside the office package'
Assert-Equal $false ('assets\floating-plush\SOURCES.md' -in @($releaseContract.InstalledFiles)) 'Plush source metadata is not installed as a runtime file'
Assert-Equal 89 @($releaseContract.SourceBuildFiles).Count 'Release contract declares the complete source-build closure including icon, manifest and content assets'
Assert-Equal $false ('tools\Countdown.BuildPreflight.psm1' -in @($releaseContract.SourceBuildFiles)) 'PowerShell build preflight is not an MSBuild project input'
Assert-Equal $true ('tools\Countdown.BuildPreflight.psm1' -in @($releaseContract.BuildInputFiles)) 'Deploy checks the PowerShell build preflight helper by commit identity'
Assert-Equal $false ('tools\Countdown.BuildPreflight.psm1' -in @($releaseContract.PackageFiles)) 'PowerShell build preflight helper stays out of the office package'
Assert-Equal $false ('tools\Test-CountdownBuildPreflight.ps1' -in @($releaseContract.PackageFiles)) 'PowerShell build preflight entry point stays out of the office package'
Assert-Equal $false ('DESIGN.md' -in @($releaseContract.PackageFiles)) 'Design contract stays in the repository, outside the office package'
Assert-Equal 9 @($releaseContract.BuildAssets).Count 'Release contract declares nine explicit application asset mappings'
$expectedInstalledSupportFiles = @(
    'Countdown.InstallTransaction.psm1',
    'Countdown.Notes.psm1',
    'Countdown.Runtime.psm1',
    'Countdown.Settings.psm1',
    'Countdown.Tags.psm1',
    '敏敏子倒计时.ps1',
    'CountdownLauncher.exe',
    'countdown-icon.ico',
    'sanrio-official-mymelody.png',
    'sanrio-official-cinnamoroll.png',
    'sanrio-official-kuromi.png',
    'holiday-data\2026.json',
    'holiday-data\README.md',
    'holiday-data\LICENSE.txt'
)
$installedSupportFiles = @($releaseContract.InstalledFiles | Where-Object { $_ -notlike 'app\*' })
Assert-Equal (($expectedInstalledSupportFiles | Sort-Object) -join ',') (($installedSupportFiles | Sort-Object) -join ',') 'Release contract installs exactly the runtime support files and offline holiday data'
$installedApplicationFiles = @($releaseContract.InstalledFiles | Where-Object { $_ -like 'app\*' })
$publishedPayloadManifest = Get-Content -LiteralPath (
    Join-Path $PSScriptRoot 'app\published-payload.json') -Raw -Encoding UTF8 | ConvertFrom-Json
Assert-Equal ((@($publishedPayloadManifest.files.path) | Sort-Object) -join ',') (($installedApplicationFiles | Sort-Object) -join ',') 'Release contract preserves every published application file without extra runtime files'
$expectedOfficePackageSupportFiles = @(
    'Deploy-OfficeCountdown.ps1',
    'Install-Countdown.ps1',
    'Countdown.ReleaseContract.psm1',
    'Countdown.InstallTransaction.psm1',
    'Countdown.RemoteDeployment.psm1',
    'Countdown.InstallLifecycle.psm1',
    'Countdown.TransferLedger.psm1',
    'Countdown.Notes.psm1',
    'Countdown.Runtime.psm1',
    'Countdown.Settings.psm1',
    'Countdown.Tags.psm1',
    '敏敏子倒计时.ps1',
    'CountdownLauncher.exe',
    'countdown-icon.ico',
    'sanrio-official-mymelody.png',
    'sanrio-official-cinnamoroll.png',
    'sanrio-official-kuromi.png',
    'holiday-data\2026.json',
    'holiday-data\README.md',
    'holiday-data\LICENSE.txt'
)
$officePackageSupportFiles = @(
    $releaseContract.PackageFiles |
        Where-Object { $_ -notin @($publishedPayloadManifest.files.path) -and $_ -ne 'app\published-payload.json' }
)
Assert-Equal (($expectedOfficePackageSupportFiles | Sort-Object) -join ',') (($officePackageSupportFiles | Sort-Object) -join ',') 'Office package carries only what the office install loads or installs besides the published payload'
$expectedPublishedLicenseFiles = @(
    'app\licenses\CommunityToolkit.Mvvm\8.4.2\License.md',
    'app\licenses\CommunityToolkit.Mvvm\8.4.2\ThirdPartyNotices.txt',
    'app\licenses\WPF-UI\4.3.0\LICENSE.md',
    'app\licenses\WPF-UI\4.3.0\ThirdPartyNotices.txt',
    'app\licenses\WPF-UI.Abstractions\4.3.0\LICENSE.md',
    'app\licenses\WPF-UI.Abstractions\4.3.0\ThirdPartyNotices.txt',
    'app\licenses\H.NotifyIcon\2.4.1\LICENSE.md'
)
$actualPublishedLicenseFiles = @($publishedPayloadManifest.files.path | Where-Object { $_ -like 'app\licenses\*' })
Assert-Equal (($expectedPublishedLicenseFiles | Sort-Object) -join ',') (($actualPublishedLicenseFiles | Sort-Object) -join ',') 'Published payload retains the complete audited license and notices set'
foreach ($publishedLicenseFile in $expectedPublishedLicenseFiles) {
    Assert-Equal $true ($publishedLicenseFile -in @($releaseContract.InstalledFiles)) "Third-party license is installed: $publishedLicenseFile"
    Assert-Equal $true $releaseContract.IntegrityHashes.Contains($publishedLicenseFile) "Third-party license is integrity protected: $publishedLicenseFile"
}
$notifyIconLicenseSource = 'docs\third-party\H.NotifyIcon\2.4.1\LICENSE.md'
Assert-Equal $true ($notifyIconLicenseSource -in @($releaseContract.BuildInputFiles)) 'Deploy checks the vendored H.NotifyIcon license read by the build script by commit identity'
Assert-Equal $false ($notifyIconLicenseSource -in @($releaseContract.PackageFiles)) 'Office package gets the published H.NotifyIcon license copy instead of the repository source'
Assert-Equal 'B41F5ADB8391601E3C693462925317E4CBE446270BCD646BDB8D02572821FE80' (Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $PSScriptRoot $notifyIconLicenseSource)).Hash 'Vendored H.NotifyIcon license matches the upstream commit bytes'
Assert-Equal $false ($notifyIconLicenseSource -in @($releaseContract.SourceBuildFiles)) 'Build-script license input is not an MSBuild project item'
Assert-Equal $false ($notifyIconLicenseSource -in @($releaseContract.InstalledFiles)) 'Runtime uses the published license copy instead of the repository source path'
foreach ($offlineHolidayFile in @('holiday-data\2026.json', 'holiday-data\README.md', 'holiday-data\LICENSE.txt')) {
    Assert-Equal $true ($offlineHolidayFile -in @($releaseContract.PackageFiles)) "Release contract packages offline holiday data: $offlineHolidayFile"
    Assert-Equal $true $releaseContract.IntegrityHashes.Contains($offlineHolidayFile) "Offline holiday data is integrity protected: $offlineHolidayFile"
    Assert-Equal $true ($offlineHolidayFile -in @($releaseContract.InstalledFiles)) "Offline holiday data is installed: $offlineHolidayFile"
}
Assert-Equal 24 @($releaseContract.RetiredRuntimeFiles).Count 'Release contract preserves the retired runtime cleanup set'
foreach ($verificationOnlyReference in @(
        'assets\floating-plush\floating-plush-mymelody-reference.png',
        'assets\floating-plush\floating-plush-cinnamoroll-reference.png',
        'assets\floating-plush\floating-plush-kuromi-reference.png'
    )) {
    $installedReference = $verificationOnlyReference -replace '^assets\\', 'app\assets\'
    Assert-Equal $false ($verificationOnlyReference -in @($releaseContract.PackageFiles)) "Plush reference stays in the repository, outside the office package: $verificationOnlyReference"
    Assert-Equal $false ($installedReference -in @($releaseContract.PackageFiles)) "Plush reference no longer ships inside the application: $installedReference"
    Assert-Equal $false ($installedReference -in @($releaseContract.InstalledFiles)) "Plush reference is not installed as a runtime file: $installedReference"
    Assert-Equal $true ($installedReference -in @($releaseContract.RetiredRuntimeFiles)) "Plush reference is cleaned from existing installations: $installedReference"
}
Assert-Equal ($expectedBuildAssets -join ',') ($actualBuildAssets -join ',') 'Release contract exposes the exact build asset mappings'
Assert-Equal ($expectedCompatibilityFixtures -join ',') (@($releaseContract.CompatibilityFixtureFiles) -join ',') 'Release contract exposes the exact compatibility fixtures'
Assert-Equal $true ('Countdown.InstallLifecycle.psm1' -in @($releaseContract.PackageFiles)) 'Install lifecycle is packaged'
Assert-Equal $true $releaseContract.IntegrityHashes.Contains('Countdown.InstallLifecycle.psm1') 'Install lifecycle is integrity protected'
Assert-Equal $false ('Countdown.InstallLifecycle.psm1' -in @($releaseContract.InstalledFiles)) 'Install lifecycle is not installed as runtime'
foreach ($deploymentModule in @('Countdown.RemoteDeployment.psm1', 'Countdown.TransferLedger.psm1')) {
    Assert-Equal $true ($deploymentModule -in @($releaseContract.PackageFiles)) "Deployment module is packaged: $deploymentModule"
    Assert-Equal $true $releaseContract.IntegrityHashes.Contains($deploymentModule) "Deployment module is integrity protected: $deploymentModule"
    Assert-Equal $false ($deploymentModule -in @($releaseContract.InstalledFiles)) "Deployment module is not installed as runtime: $deploymentModule"
}
foreach ($repositoryOnlyModule in @('Countdown.Validation.psm1', 'Countdown.Core.psm1')) {
    Assert-Equal $false ($repositoryOnlyModule -in @($releaseContract.PackageFiles)) "Module the office install never loads stays out of the office package: $repositoryOnlyModule"
    Assert-Equal $false $releaseContract.IntegrityHashes.Contains($repositoryOnlyModule) "Module the office install never loads is not statically pinned: $repositoryOnlyModule"
    Assert-Equal $false ($repositoryOnlyModule -in @($releaseContract.InstalledFiles)) "Module the office install never loads is not installed as runtime: $repositoryOnlyModule"
}
Assert-Equal $true ('Countdown.Core.psm1' -in @($releaseContract.RetiredRuntimeFiles)) 'Legacy core module is cleaned from existing installations'
Assert-Equal 0 @($releaseContract.PackageFiles | Where-Object { $_ -match '(^|\\)(bin|obj)(\\|$)' }).Count 'Release package excludes all bin and obj paths'
Assert-Equal 0 @($releaseContract.SourceBuildFiles | Where-Object { $_ -notin @($releaseContract.BuildInputFiles) }).Count 'Deploy checks every source-build input by commit identity'
Assert-Equal 91 @($releaseContract.BuildInputFiles).Count 'Build inputs are the source-build closure plus the preflight helper and vendored license read by the build script'
Assert-Equal 0 @($releaseContract.PackageFiles | Where-Object { $_ -match '\.(?:cs|csproj|xaml)$' -or $_ -eq 'global.json' }).Count 'Office package carries no C# project, source or SDK pin'
Assert-Equal 0 `
    @($releaseContract.SourceBuildFiles | Where-Object { $_ -match '\.(?:cs|csproj|xaml)$' -and $_ -in @($releaseContract.InstalledFiles) }).Count `
    'Project, C# and XAML source-build inputs are not installed as runtime files'
# 视觉基线版本只在 VisualReferenceBaseline.cs 声明一次；脚本经 Get-CountdownVisualBaseline 取版本与两块 manifest 的位置。
$visualBaselineFixtureRoot = Join-Path ([System.IO.Path]::GetTempPath()) "countdown-visual-baseline-$([guid]::NewGuid().ToString('N'))"
$visualBaselineFixtureSource = Join-Path $visualBaselineFixtureRoot 'src\MinminCountdown\VisualReferenceBaseline.cs'
New-Item -ItemType Directory -Path (Split-Path -Parent $visualBaselineFixtureSource) -Force | Out-Null
try {
    [System.IO.File]::WriteAllText(
        $visualBaselineFixtureSource,
        "internal static class VisualReferenceBaseline`n{`n    public const string Version = `"2031-01-02-v77`";`n}`n")
    $fixtureVisualBaseline = Get-CountdownVisualBaseline -RepositoryRoot $visualBaselineFixtureRoot
    Assert-Equal '2031-01-02-v77' $fixtureVisualBaseline.Version 'Visual baseline lookup reads the single C# version constant'
    Assert-Equal 'assets/ui-references/main-window/864x1000/2031-01-02-v77' $fixtureVisualBaseline.Main.RelativeDirectory `
        'Main-window baseline directory follows the version constant'
    Assert-Equal (Join-Path $visualBaselineFixtureRoot 'assets\ui-references\floating-window\520x390\2031-01-02-v77\manifest.json') `
        $fixtureVisualBaseline.Floating.ManifestPath 'Floating manifest path follows the version constant'
    [System.IO.File]::WriteAllText($visualBaselineFixtureSource, "internal static class VisualReferenceBaseline { }`n")
    $missingVisualBaselineVersionMessage = $null
    try { $null = Get-CountdownVisualBaseline -RepositoryRoot $visualBaselineFixtureRoot }
    catch { $missingVisualBaselineVersionMessage = $_.Exception.Message }
    Assert-Equal "找不到视觉基线版本常量：$visualBaselineFixtureSource" $missingVisualBaselineVersionMessage `
        'Visual baseline lookup fails closed without the version constant'
}
finally {
    Remove-Item -LiteralPath $visualBaselineFixtureRoot -Recurse -Force -ErrorAction SilentlyContinue
}
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
$visualBaseline = Get-CountdownVisualBaseline -RepositoryRoot $PSScriptRoot
$visualBaselinesAvailable = (Test-Path -LiteralPath $visualBaseline.Floating.ManifestPath -PathType Leaf) -and `
                            (Test-Path -LiteralPath $visualBaseline.Main.ManifestPath -PathType Leaf)

if ($visualBaselinesAvailable) {
$visualBaselineDefinitions = @(
    [pscustomobject]@{
        Name = 'Floating'
        ManifestPath = $visualBaseline.Floating.ManifestPath
        ExpectedVersion = $visualBaseline.Version
        ExpectedCaptureIds = @(
            'floating-countdowns-day',
            'floating-workday-complete-day',
            'long-notes-day-bottom',
            'long-notes-day-top',
            'notes-search-expanded-day'
        )
        ExpectedPathPrefix = "$($visualBaseline.Floating.RelativeDirectory)/"
    },
    [pscustomobject]@{
        Name = 'Main'
        ManifestPath = $visualBaseline.Main.ManifestPath
        ExpectedVersion = $visualBaseline.Version
        ExpectedCaptureIds = @(
            'main-day-countdowns-empty',
            'main-day-countdowns-representative',
            'main-day-countdowns-stress',
            'main-day-notes-edit',
            'main-day-notes-empty',
            'main-day-notes-filter-open',
            'main-day-notes-representative',
            'main-day-notes-selected',
            'main-day-notes-stress'
        )
        ExpectedPathPrefix = "$($visualBaseline.Main.RelativeDirectory)/"
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
$deployControlIdentityIndex = $deploySource.IndexOf("-Stage '控制文件提交身份校验'")
$deployBuildInputIdentityIndex = $deploySource.IndexOf("-Stage '构建输入提交身份校验'")
$credentialImportIndex = if ($deployBuildInputIdentityIndex -ge 0) {
    $deploySource.IndexOf(". 'D:\Codex\.codex\credentials\ssh\replicator\office\Use-OfficeSshHelper.ps1'", $deployBuildInputIdentityIndex)
}
else { -1 }
Assert-Equal $true (
    $deployValidationExitIndex -ge 0 -and
    $deployCandidateValidationIndex -gt $deployValidationExitIndex -and
    $deployCommitGateIndex -gt $deployCandidateValidationIndex -and
    $deploySnapshotExportIndex -gt $deployCommitGateIndex -and
    $deployControlIdentityIndex -gt $deploySnapshotExportIndex -and
    $deployBuildInputIdentityIndex -gt $deployControlIdentityIndex -and
    $credentialImportIndex -gt $deployBuildInputIdentityIndex -and
    $deployPackageAssertionIndex -gt $credentialImportIndex -and
    $transferLedgerImportIndex -gt $deployPackageAssertionIndex
) 'Office deploy validates the candidate before Git gates and verifies control files and build inputs by commit identity before loading SSH credentials'
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
$postPackageManifestStageBinding = [regex]::Match(
    $deploySource,
    '(?s)Assert-CountdownDeploymentRepositoryAndManifest.{0,500}-Stage ''打包后''.{0,500}-ManifestRoot \$sourceRoot.{0,200}-Manifest \$packageManifest.{0,100}-ManifestFirst'
).Success
$remoteOutputManifestStageBinding = [regex]::Match(
    $deploySource,
    '(?s)Assert-CountdownDeploymentRepositoryAndManifest.{0,500}-Stage ''远程输出前''.{0,500}-ManifestRoot \$extractRoot.{0,200}-Manifest \$packageManifest'
).Success
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
    $postPackageManifestStageBinding -and
    $remoteOutputManifestStageBinding
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
    'Assert-CountdownDeploymentRepositoryAndManifest',
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

    $missingBuildInputRejected = $false
    try {
        [void] (New-CountdownDeploymentCommitFileManifest `
            -GitPath $fixtureGitPath `
            -RepositoryRoot $trustRootRepository `
            -Root $trustedSnapshotRoot `
            -IdentityManifest ([ordered]@{ 'src\MinminCountdown\Missing.cs' = $trustedBlobIdentity }) `
            -Stage '构建输入夹具' `
            -Adapter $trustRootGitAdapter)
    }
    catch {
        $missingBuildInputRejected = $_.FullyQualifiedErrorId -like 'PathNotFound,*'
    }
    Assert-Equal $true $missingBuildInputRejected 'Missing build input fails the commit identity check before the build'

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
$identityResolveIndex = if ($credentialImportIndex -ge 0) { $deploySource.IndexOf('Resolve-OfficeSshClientIdentity', $credentialImportIndex) } else { -1 }
$echoProbeIndex = if ($credentialImportIndex -ge 0) { $deploySource.IndexOf('Invoke-OfficeSshEchoBehaviorProbe', $credentialImportIndex) } else { -1 }
Assert-Equal $true (
    $credentialImportIndex -ge 0 -and
    $tailscalePingIndex -gt $credentialImportIndex -and
    $identityResolveIndex -gt $tailscalePingIndex -and
    $echoProbeIndex -gt $identityResolveIndex -and
    $buildInvocationIndex -gt $echoProbeIndex -and
    $firstRecordedScpIndex -gt $echoProbeIndex
) 'Formal deploy resolves SSH identity and runs the echo probe after Tailscale, before the build and first SCP transfer'
$adapterCalls = @(
    $deploymentAst.FindAll({
            param($node)
            $node -is [System.Management.Automation.Language.CommandAst] -and
            $node.GetCommandName() -eq 'New-CountdownRemoteDeploymentReadbackSshAdapter'
        }, $true)
)
Assert-Equal 2 $adapterCalls.Count 'Office deploy and read-only recheck each construct an independent readback SSH adapter'
$adapterParameterNames = @(
    $adapterCalls[-1].CommandElements |
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
$mutableReleaseContract.IntegrityHashes['Countdown.Notes.psm1'] = ('0' * 64)
$freshReleaseContract = Get-CountdownReleaseContract
Assert-Equal $false ('mutated-package-file' -in @($freshReleaseContract.PackageFiles)) 'Release contract returns a fresh package list'
Assert-Equal $false ('mutated-build-source' -in @($freshReleaseContract.BuildAssets.SourcePath)) 'Release contract returns fresh build asset records'
Assert-Equal $false (($freshReleaseContract.IntegrityHashes['Countdown.Notes.psm1']) -eq ('0' * 64)) 'Release contract returns a fresh integrity map'

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
    # 构建输入不随办公室包下发：在仓库根评估 MSBuild 项目闭包，部署器构建前按提交 blob 身份逐项核对同一份清单。
    $sourceClosureRoot = [System.IO.Path]::GetFullPath($PSScriptRoot).TrimEnd('\')
    $applicationProjectPath = Join-Path $sourceClosureRoot 'src\MinminCountdown\MinminCountdown.csproj'
    $evaluatedSourceBuildFiles = [System.Collections.Generic.List[string]]::new()
    [void] $evaluatedSourceBuildFiles.Add('global.json')
    $projectQueue = [System.Collections.Generic.Queue[string]]::new()
    $projectQueue.Enqueue($applicationProjectPath)
    $evaluatedProjects = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    while ($projectQueue.Count -gt 0) {
        $projectPath = [System.IO.Path]::GetFullPath($projectQueue.Dequeue())
        if (-not $evaluatedProjects.Add($projectPath)) {
            continue
        }
        [void] $evaluatedSourceBuildFiles.Add($projectPath.Substring($sourceClosureRoot.Length + 1))
        $evaluatedJson = & $managedDotnet msbuild $projectPath '-getItem:Compile,ApplicationDefinition,Page,Content,ProjectReference' '-getProperty:ApplicationIcon,ApplicationManifest' | Out-String
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
                if ($fullPath.StartsWith($sourceClosureRoot + '\', [System.StringComparison]::OrdinalIgnoreCase) -and
                    $fullPath -notmatch '[\\/](bin|obj)[\\/]') {
                    [void] $evaluatedSourceBuildFiles.Add($fullPath.Substring($sourceClosureRoot.Length + 1))
                }
            }
        }
        $projectReferenceProperty = $evaluatedProject.Items.PSObject.Properties['ProjectReference']
        foreach ($projectReference in @(
                if ($null -ne $projectReferenceProperty) { $projectReferenceProperty.Value }
            )) {
            $referencedProjectPath = [System.IO.Path]::GetFullPath([string] $projectReference.FullPath)
            if ($referencedProjectPath.StartsWith($sourceClosureRoot + '\', [System.StringComparison]::OrdinalIgnoreCase)) {
                $projectQueue.Enqueue($referencedProjectPath)
            }
        }
        $applicationIcon = [string] $evaluatedProject.Properties.ApplicationIcon
        if (-not [string]::IsNullOrWhiteSpace($applicationIcon)) {
            $applicationIconPath = [System.IO.Path]::GetFullPath((Join-Path (Split-Path -Parent $projectPath) $applicationIcon))
            if ($applicationIconPath.StartsWith($sourceClosureRoot + '\', [System.StringComparison]::OrdinalIgnoreCase)) {
                [void] $evaluatedSourceBuildFiles.Add($applicationIconPath.Substring($sourceClosureRoot.Length + 1))
            }
        }
        $applicationManifest = [string] $evaluatedProject.Properties.ApplicationManifest
        if (-not [string]::IsNullOrWhiteSpace($applicationManifest)) {
            $manifestPath = [System.IO.Path]::GetFullPath((Join-Path (Split-Path -Parent $projectPath) $applicationManifest))
            if ($manifestPath.StartsWith($sourceClosureRoot + '\', [System.StringComparison]::OrdinalIgnoreCase)) {
                [void] $evaluatedSourceBuildFiles.Add($manifestPath.Substring($sourceClosureRoot.Length + 1))
            }
        }
    }
    Assert-Equal `
        ((@($releaseContract.SourceBuildFiles) | Sort-Object) -join ',') `
        ((@($evaluatedSourceBuildFiles) | Sort-Object -Unique) -join ',') `
        'Release contract matches the MSBuild-evaluated project, Compile, WPF XAML, icon, manifest and Content closure'

    # 办公室包不含源码：补进声明的构建输入，证明它们足以从零重建应用，再在隔离桌面跑应用 validate-only。
    foreach ($relativePath in $releaseContract.BuildInputFiles) {
        $testPath = Join-Path $releaseContractTestRoot $relativePath
        $testParent = Split-Path -Parent $testPath
        if (-not (Test-Path -LiteralPath $testParent -PathType Container)) {
            New-Item -ItemType Directory -Path $testParent -Force | Out-Null
        }
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot $relativePath) -Destination $testPath -Force
    }
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
        'Declared build inputs rebuild the DLL required by C# regression'

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
