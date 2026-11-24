# Countdown.Core.Tests.ps1 的 ReleaseContract 分段：入口按开关点源执行，共用入口的参数、断言函数与已建立的状态，不单独运行。

$releaseContract = Get-CountdownReleaseContract
$publishedPayloadManifest = Get-Content -LiteralPath (
    Join-Path $PSScriptRoot 'app\published-payload.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$expectedBuildAssets = @(
    'sanrio-official-mymelody.png=>app\assets\sanrio-official-mymelody.png',
    'sanrio-official-cinnamoroll.png=>app\assets\sanrio-official-cinnamoroll.png',
    'sanrio-official-kuromi.png=>app\assets\sanrio-official-kuromi.png',
    'assets\floating-plush\component-day-outer-shell.png=>app\assets\floating-plush\component-day-outer-shell.png',
    'assets\floating-plush\component-day-header-blank.png=>app\assets\floating-plush\component-day-header-blank.png',
    'assets\floating-plush\component-day-card-blank.png=>app\assets\floating-plush\component-day-card-blank.png',
    'assets\floating-plush\component-day-mascots.png=>app\assets\floating-plush\component-day-mascots.png',
    'assets\main-decorations\main-countdown-scene-day.png=>app\assets\main-decorations\main-countdown-scene-day.png',
    'assets\main-decorations\main-notes-scene-day.png=>app\assets\main-decorations\main-notes-scene-day.png',
    'holiday-data\2026.json=>app\holiday-data\2026.json',
    'holiday-data\2027.json=>app\holiday-data\2027.json',
    'holiday-data\README.md=>app\holiday-data\README.md',
    'holiday-data\LICENSE.txt=>app\holiday-data\LICENSE.txt',
    'countdown-icon.ico=>app\countdown-icon.ico'
)
$actualBuildAssets = @($releaseContract.BuildAssets | ForEach-Object { "$($_.SourcePath)=>$($_.PublishedPath)" })
$expectedFloatingPlushAssets = @(
    'assets\floating-plush\floating-plush-mymelody-reference.png',
    'assets\floating-plush\floating-plush-cinnamoroll-reference.png',
    'assets\floating-plush\floating-plush-kuromi-reference.png',
    'assets\floating-plush\component-day-outer-shell.png',
    'assets\floating-plush\component-day-header-blank.png',
    'assets\floating-plush\component-day-card-blank.png',
    'assets\floating-plush\component-day-mascots.png'
)

Assert-Equal 1 $releaseContract.ContractVersion 'Release contract exposes version one'
$expectedPackageCount = @($publishedPayloadManifest.files).Count + 4
Assert-Equal $expectedPackageCount @($releaseContract.PackageFiles).Count 'Release contract declares the payload, two manifests and two installer files'
foreach ($buildOnlyInput in @('src\MinminCountdown\NoteCompletionExit.cs', 'src\MinminCountdown\app.manifest')) {
    Assert-Equal $false ($buildOnlyInput -in @($releaseContract.PackageFiles)) "Build input stays out of the office package: $buildOnlyInput"
}
Assert-Equal $false ('assets\floating-plush\SOURCES.md' -in @($releaseContract.PackageFiles)) 'Plush source metadata stays in the repository, outside the office package'
Assert-Equal $false ('assets\floating-plush\SOURCES.md' -in @($releaseContract.InstalledFiles)) 'Plush source metadata is not installed as a runtime file'
Assert-Equal $false ('tools\Countdown.BuildPreflight.psm1' -in @($releaseContract.PackageFiles)) 'PowerShell build preflight helper stays out of the office package'
Assert-Equal $false ('tools\Test-CountdownBuildPreflight.ps1' -in @($releaseContract.PackageFiles)) 'PowerShell build preflight entry point stays out of the office package'
Assert-Equal $false ('DESIGN.md' -in @($releaseContract.PackageFiles)) 'Design contract stays in the repository, outside the office package'
Assert-Equal 14 @($releaseContract.BuildAssets).Count 'Release contract declares fourteen explicit application asset mappings'
Assert-Equal 0 @($releaseContract.BuildAssets | Where-Object { $_.PSObject.Properties.Name -contains 'SHA256' }).Count 'Build asset mappings carry no static SHA-256 pins'
foreach ($removedProperty in @('IntegrityHashes', 'PackageTrustRoots', 'BuildInputFiles', 'SourceBuildFiles')) {
    Assert-Equal $false ($releaseContract.PSObject.Properties.Name -contains $removedProperty) "Release contract exposes no retired manual list: $removedProperty"
}
$installedSupportFiles = @($releaseContract.InstalledFiles | Where-Object { $_ -notlike 'app\*' })
Assert-Equal 0 $installedSupportFiles.Count 'Release contract installs no legacy support files outside the program directory'
$installedApplicationFiles = @($releaseContract.InstalledFiles | Where-Object { $_ -like 'app\*' })
Assert-Equal ((@($publishedPayloadManifest.files.path) + @('app\published-payload.json') | Sort-Object) -join ',') (($installedApplicationFiles | Sort-Object) -join ',') 'Release contract installs exactly the published program directory and its manifest'
Assert-Equal 0 @($publishedPayloadManifest.files.path | Where-Object { $_ -match 'CsWin32|Win32Metadata|Win32Docs' }).Count '#333 build-time CsWin32 packages add zero runtime files to the published payload'
$expectedOfficePackageSupportFiles = @(
    'Install-CountdownProgram.ps1',
    'Countdown.Installer.psm1',
    'package-manifest.json'
)
$officePackageSupportFiles = @(
    $releaseContract.PackageFiles |
        Where-Object { $_ -notin @($publishedPayloadManifest.files.path) -and $_ -ne 'app\published-payload.json' }
)
Assert-Equal (($expectedOfficePackageSupportFiles | Sort-Object) -join ',') (($officePackageSupportFiles | Sort-Object) -join ',') 'Office package carries only what the office install loads or installs besides the published payload'

# 真实 ZIP 精确闭包：部署打包器的 entries 与 ReleaseContract 必须逐项相同，不多也不少。
$releaseDeployerModule = Import-Module (Join-Path $PSScriptRoot 'Countdown.Deployer.psm1') -Force -PassThru
$releaseBuildOutput = & $releaseDeployerModule {
    param($Root, $Commit)
    Read-CountdownDeployBuildOutput -RepositoryRoot $Root -Commit $Commit
} $PSScriptRoot ([string] $publishedPayloadManifest.commit)
$releaseZipPath = Join-Path ([System.IO.Path]::GetTempPath()) ('clock-countdown-release-' + [guid]::NewGuid().ToString('N') + '.zip')
try {
    $null = & $releaseDeployerModule {
        param($Root, $BuildOutput, $PackagePath)
        New-CountdownDeployPackage -RepositoryRoot $Root -BuildOutput $BuildOutput -PackagePath $PackagePath -BeijingNow ([DateTimeOffset]'2026-11-30T12:00:00+08:00')
    } $PSScriptRoot $releaseBuildOutput $releaseZipPath
    $releaseArchive = [System.IO.Compression.ZipFile]::OpenRead($releaseZipPath)
    try {
        $releaseZipFiles = @($releaseArchive.Entries | ForEach-Object { $_.FullName.Replace('/', '\') })
    }
    finally { $releaseArchive.Dispose() }
    Assert-Equal ((@($releaseContract.PackageFiles) | Sort-Object) -join ',') (($releaseZipFiles | Sort-Object) -join ',') 'Deployment ZIP entries exactly equal the release contract package set'
    Assert-Equal $expectedPackageCount $releaseZipFiles.Count 'Deployment ZIP has the expected package count'
}
finally {
    if (Test-Path -LiteralPath $releaseZipPath -PathType Leaf) { [System.IO.File]::Delete($releaseZipPath) }
}

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
}
$notifyIconLicenseSource = 'docs\third-party\H.NotifyIcon\2.4.1\LICENSE.md'
Assert-Equal $false ($notifyIconLicenseSource -in @($releaseContract.PackageFiles)) 'Office package gets the published H.NotifyIcon license copy instead of the repository source'
Assert-Equal 'B41F5ADB8391601E3C693462925317E4CBE446270BCD646BDB8D02572821FE80' (Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $PSScriptRoot $notifyIconLicenseSource)).Hash 'Vendored H.NotifyIcon license matches the upstream commit bytes'
Assert-Equal $false ($notifyIconLicenseSource -in @($releaseContract.InstalledFiles)) 'Runtime uses the published license copy instead of the repository source path'
foreach ($offlineHolidayFile in @('holiday-data\2026.json', 'holiday-data\2027.json', 'holiday-data\README.md', 'holiday-data\LICENSE.txt')) {
    Assert-Equal $false ($offlineHolidayFile -in @($releaseContract.PackageFiles)) "Repository-root holiday input stays out of the ZIP: $offlineHolidayFile"
    Assert-Equal $false ($offlineHolidayFile -in @($releaseContract.InstalledFiles)) "Repository-root holiday input is not installed outside the program directory: $offlineHolidayFile"
}
# #275 程序目录自足：随包节假日数据与图标随构建输出进 app\，载荷清单按仓库原字节覆盖它们，
# 根级生成包清单与安装精确闭包继续保护这些字节，不再复制到安装根。
$expectedProgramDirectoryResources = [ordered]@{
    'app\holiday-data\2026.json' = 'holiday-data\2026.json'
    'app\holiday-data\2027.json' = 'holiday-data\2027.json'
    'app\holiday-data\README.md' = 'holiday-data\README.md'
    'app\holiday-data\LICENSE.txt' = 'holiday-data\LICENSE.txt'
    'app\countdown-icon.ico' = 'countdown-icon.ico'
}
foreach ($programResource in $expectedProgramDirectoryResources.GetEnumerator()) {
    $publishedPath = [string] $programResource.Key
    $repositoryHash = (Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $PSScriptRoot ([string] $programResource.Value))).Hash
    $manifestEntries = @($publishedPayloadManifest.files | Where-Object { [string] $_.path -eq $publishedPath })
    Assert-Equal 1 $manifestEntries.Count "#275 payload manifest covers the program-directory resource: $publishedPath"
    $manifestHash = if ($manifestEntries.Count -eq 1) { [string] $manifestEntries[0].sha256 } else { '' }
    Assert-Equal $repositoryHash $manifestHash "#275 payload manifest pins the repository bytes: $publishedPath"
    $builtResource = Join-Path $PSScriptRoot $publishedPath
    Assert-Equal $true (Test-Path -LiteralPath $builtResource -PathType Leaf) "#275 build output carries the program-directory resource: $publishedPath"
    if (Test-Path -LiteralPath $builtResource -PathType Leaf) {
        Assert-Equal $repositoryHash (Get-FileHash -Algorithm SHA256 -LiteralPath $builtResource).Hash "#275 build output copies the repository bytes: $publishedPath"
    }
    Assert-Equal $true ($publishedPath -in @($releaseContract.InstalledFiles)) "#275 install copies the program-directory resource: $publishedPath"
}

# #260 独立期望直接转录自国务院办公厅国办发明电〔2026〕10号，不读取上游数据或转换代码：
# https://www.gov.cn/zhengce/content/202611/content_7085101.htm
$expected2027PublicHolidays = @(
    '2027-01-01', '2027-01-02', '2027-01-03',
    '2027-02-05', '2027-02-06', '2027-02-07', '2027-02-08', '2027-02-09',
    '2027-02-10', '2027-02-11', '2027-02-12', '2027-02-13',
    '2027-04-03', '2027-04-04', '2027-04-05',
    '2027-05-01', '2027-05-02', '2027-05-03', '2027-05-04', '2027-05-05',
    '2027-06-09',
    '2027-09-15',
    '2027-10-01', '2027-10-02', '2027-10-03', '2027-10-04', '2027-10-05', '2027-10-06', '2027-10-07'
)
$expected2027TransferWorkdays = @(
    '2027-01-31', '2027-02-14', '2027-05-08', '2027-09-26', '2027-10-09'
)
Assert-Equal 29 $expected2027PublicHolidays.Count 'State Council notice yields 29 public holidays in 2027'
Assert-Equal 5 $expected2027TransferWorkdays.Count 'State Council notice yields five transfer workdays in 2027'
$expected2027Days = [ordered]@{}
for ($date = [datetime]'2027-01-01'; $date -le [datetime]'2027-12-31'; $date = $date.AddDays(1)) {
    $dateText = $date.ToString('yyyy-MM-dd')
    $expected2027Days[$dateText] = if ($dateText -in $expected2027PublicHolidays) {
        $false
    } elseif ($dateText -in $expected2027TransferWorkdays) {
        $true
    } else {
        $date.DayOfWeek -notin @([DayOfWeek]::Saturday, [DayOfWeek]::Sunday)
    }
}
Assert-Equal 365 $expected2027Days.Count 'Independent 2027 expectation covers the full year'
Assert-Equal 248 @($expected2027Days.Values | Where-Object { $_ }).Count 'Independent 2027 expectation has 248 workdays'
Assert-Equal 117 @($expected2027Days.Values | Where-Object { -not $_ }).Count 'Independent 2027 expectation has 117 non-workdays'
$packaged2027Path = Join-Path $PSScriptRoot 'holiday-data\2027.json'
Assert-Equal $true (Test-Path -LiteralPath $packaged2027Path -PathType Leaf) 'Packaged 2027 holiday data exists'
if (Test-Path -LiteralPath $packaged2027Path -PathType Leaf) {
    $packaged2027 = Get-Content -LiteralPath $packaged2027Path -Raw -Encoding UTF8 | ConvertFrom-Json
    Assert-Equal 1 $packaged2027.schemaVersion 'Packaged 2027 data keeps schema version one'
    Assert-Equal 2027 $packaged2027.year 'Packaged 2027 data declares its year'
    Assert-Equal 'CN' $packaged2027.region 'Packaged 2027 data declares the CN region'
    Assert-Equal 365 @($packaged2027.days).Count 'Packaged 2027 data contains 365 day records'
    Assert-Equal 365 @($packaged2027.days.date | Sort-Object -Unique).Count 'Packaged 2027 dates are unique'
    Assert-Equal (($expected2027Days.Keys | Sort-Object) -join ',') (($packaged2027.days.date | Sort-Object) -join ',') 'Packaged 2027 data covers every date exactly once'
    foreach ($day in $packaged2027.days) {
        Assert-Equal $expected2027Days[$day.date] $day.is_workday "Packaged 2027 workday matches the State Council notice: $($day.date)"
    }
}
$packagedHolidayYears = @($releaseContract.OfflineHolidayDataFiles | ForEach-Object {
        if ($_ -match '^holiday-data\\(\d{4})\.json$') { [int] $Matches[1] }
    })
$actualDecemberCoverage = Test-CountdownHolidayYearCoverage -BeijingNow ([DateTimeOffset]'2026-12-01T00:00:00+08:00') -PackagedYears $packagedHolidayYears
Assert-Equal $true ([bool] $actualDecemberCoverage.Covered) 'Actual packaged years pass the December 2026 coverage guard'
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
    Assert-Equal $true ($installedReference -in @($releaseContract.RetiredRuntimeFiles)) "Plush reference remains in the historical retired-file metadata: $installedReference"
}
Assert-Equal ($expectedBuildAssets -join ',') ($actualBuildAssets -join ',') 'Release contract exposes the exact build asset mappings'
Assert-Equal ($expectedFloatingPlushAssets -join ',') (@($releaseContract.FloatingPlushAssets) -join ',') 'Release contract retains only current and reference floating assets in the repository asset list'
Assert-Equal $false ($releaseContract.PSObject.Properties.Name -contains 'CompatibilityFixtureFiles') 'Release contract no longer exposes a legacy-host compatibility fixture list'
Assert-Equal $false ($releaseContract.PSObject.Properties.Name -contains 'LegacyFloatingRegressionAssets') 'Release contract no longer exposes a legacy floating regression asset list'
foreach ($installerFile in @('Install-CountdownProgram.ps1', 'Countdown.Installer.psm1')) {
    Assert-Equal $true ($installerFile -in @($releaseContract.PackageFiles)) "New installer is packaged: $installerFile"
    Assert-Equal $false ($installerFile -in @($releaseContract.InstalledFiles)) "Package installer is not copied into the installed program directory: $installerFile"
}
foreach ($repositoryOnlyModule in @('Countdown.Validation.psm1')) {
    Assert-Equal $false ($repositoryOnlyModule -in @($releaseContract.PackageFiles)) "Module the office install never loads stays out of the office package: $repositoryOnlyModule"
    Assert-Equal $false ($repositoryOnlyModule -in @($releaseContract.InstalledFiles)) "Module the office install never loads is not installed as runtime: $repositoryOnlyModule"
}
foreach ($retiredSettingsModule in @('Countdown.Notes.psm1', 'Countdown.Tags.psm1', 'Countdown.Settings.psm1')) {
    Assert-Equal $false ($retiredSettingsModule -in @($releaseContract.PackageFiles)) "Retired PowerShell settings implementation stays out of the office package: $retiredSettingsModule"
    Assert-Equal $false ($retiredSettingsModule -in @($releaseContract.InstalledFiles)) "Retired PowerShell settings implementation is not installed as runtime: $retiredSettingsModule"
}
foreach ($legacyHostModule in @('Countdown.App.psm1', 'Countdown.Core.psm1')) {
    Assert-Equal $false ($legacyHostModule -in @($releaseContract.PackageFiles)) "Deleted legacy host module stays out of the office package: $legacyHostModule"
    Assert-Equal $false ($legacyHostModule -in @($releaseContract.InstalledFiles)) "Deleted legacy host module is not installed as runtime: $legacyHostModule"
    Assert-Equal $true ($legacyHostModule -in @($releaseContract.RetiredRuntimeFiles)) "Deleted legacy host module remains in the cleanup set: $legacyHostModule"
}
Assert-Equal 0 @($releaseContract.PackageFiles | Where-Object { $_ -match '(^|\\)(bin|obj)(\\|$)' }).Count 'Release package excludes all bin and obj paths'
Assert-Equal 0 @($releaseContract.PackageFiles | Where-Object { $_ -match '\.(?:cs|csproj|xaml)$' -or $_ -eq 'global.json' }).Count 'Office package carries no C# project, source or SDK pin'
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

Assert-Equal $false ('Countdown.ReleaseContract.psm1' -in @($releaseContract.PackageFiles)) 'Release contract module itself stays out of the office ZIP'
Assert-Equal 0 @($releaseContract.PackageFiles | Where-Object { $_ -like 'assets\ui-references\*' }).Count 'UI references stay outside the release package'
Assert-Equal 0 @($releaseContract.InstalledFiles | Where-Object { $_ -like 'assets\ui-references\*' }).Count 'UI references stay outside installed runtime'
Assert-Equal 0 @($releaseContract.BuildAssets | Where-Object { $_.PublishedPath -like '*ui-references*' }).Count 'UI references stay outside build targets'

$mutableReleaseContract = Get-CountdownReleaseContract
$mutableReleaseContract.PackageFiles[0] = 'mutated-package-file'
$mutableReleaseContract.BuildAssets[0].SourcePath = 'mutated-build-source'
$freshReleaseContract = Get-CountdownReleaseContract
Assert-Equal $false ('mutated-package-file' -in @($freshReleaseContract.PackageFiles)) 'Release contract returns a fresh package list'
Assert-Equal $false ('mutated-build-source' -in @($freshReleaseContract.BuildAssets.SourcePath)) 'Release contract returns fresh build asset records'

$releaseContractTestRoot = Join-Path $PSScriptRoot ("release-contract-test-" + [guid]::NewGuid().ToString('N'))
try {
    New-Item -ItemType Directory -Path $releaseContractTestRoot -Force | Out-Null

    # #261：在隔离 module 副本里注入“12 月且缺下一年数据”的判定结果。导入仍须成功，
    # 只有发布入口显式调用守卫时才失败；纯函数本身的日期与年份边界由 MainDomain 分段覆盖。
    $holidayGuardModuleSource = [System.IO.File]::ReadAllText(
        (Join-Path $PSScriptRoot 'Countdown.ReleaseContract.psm1'),
        [System.Text.Encoding]::UTF8)
    $holidayGuardImportIndex = $holidayGuardModuleSource.LastIndexOf(
        'Assert-CountdownReleaseContractDefinition',
        [System.StringComparison]::Ordinal)
    if ($holidayGuardImportIndex -lt 0) {
        throw '年度覆盖守卫夹具找不到发布契约导入断言。'
    }
    $holidayGuardOverride = @'
function Test-CountdownHolidayYearCoverage {
    param($BeijingNow, $PackagedYears)
    [pscustomobject]@{
        Covered = $false
        MissingYear = 2027
        Message = '随包节假日数据尚未覆盖 2027 年；请按 holiday-data/README.md 的更新流程补齐。'
    }
}

'@
    $holidayGuardModulePath = Join-Path $releaseContractTestRoot 'Countdown.ReleaseContract.HolidayGuard.psm1'
    [System.IO.File]::WriteAllText(
        $holidayGuardModulePath,
        $holidayGuardModuleSource.Insert($holidayGuardImportIndex, $holidayGuardOverride),
        [System.Text.UTF8Encoding]::new($false))
    $holidayGuardImportError = $null
    $holidayGuardModule = $null
    try {
        $holidayGuardModule = Import-Module $holidayGuardModulePath -Force -PassThru
    }
    catch {
        $holidayGuardImportError = $_.Exception.Message
    }
    Assert-Equal $null $holidayGuardImportError 'Release contract import succeeds when December holiday coverage is missing'
    if ($null -ne $holidayGuardModule) {
        $holidayGuardExplicitError = & $holidayGuardModule {
            try {
                Assert-CountdownHolidayYearCoverage
                $null
            }
            catch {
                $_.Exception.Message
            }
        }
        Assert-Equal $true ([string] $holidayGuardExplicitError).Contains('2027') 'Explicit holiday coverage assertion names the missing year'
        Assert-Equal $true ([string] $holidayGuardExplicitError).Contains('holiday-data/README.md') 'Explicit holiday coverage assertion names the refresh runbook'
        Remove-Module -ModuleInfo $holidayGuardModule -Force
        Import-Module (Join-Path $PSScriptRoot 'Countdown.ReleaseContract.psm1') -Force
    }
    Remove-Item -LiteralPath $holidayGuardModulePath -Force

}
finally {
    Remove-CountdownReleaseContractTestTree -Path $releaseContractTestRoot
}
