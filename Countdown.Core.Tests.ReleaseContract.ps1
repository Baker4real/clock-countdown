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
Assert-Equal 448 @($releaseContract.PackageFiles).Count 'Release contract declares exactly the office package: trust roots, install-path modules, launcher, holiday data, root character assets and the published payload'
Assert-Equal 446 $releaseContract.IntegrityHashes.Count 'Release contract pins every non-root office package file'
foreach ($buildOnlyInput in @('src\MinminCountdown\NoteCompletionExit.cs', 'src\MinminCountdown\app.manifest')) {
    Assert-Equal $false ($buildOnlyInput -in @($releaseContract.PackageFiles)) "Build input stays out of the office package: $buildOnlyInput"
    Assert-Equal $false $releaseContract.IntegrityHashes.Contains($buildOnlyInput) "Build input is not statically pinned: $buildOnlyInput"
}
Assert-Equal $false ('assets\floating-plush\SOURCES.md' -in @($releaseContract.PackageFiles)) 'Plush source metadata stays in the repository, outside the office package'
Assert-Equal $false ('assets\floating-plush\SOURCES.md' -in @($releaseContract.InstalledFiles)) 'Plush source metadata is not installed as a runtime file'
Assert-Equal $false ('tools\Countdown.BuildPreflight.psm1' -in @($releaseContract.PackageFiles)) 'PowerShell build preflight helper stays out of the office package'
Assert-Equal $false ('tools\Test-CountdownBuildPreflight.ps1' -in @($releaseContract.PackageFiles)) 'PowerShell build preflight entry point stays out of the office package'
Assert-Equal $false ('DESIGN.md' -in @($releaseContract.PackageFiles)) 'Design contract stays in the repository, outside the office package'
Assert-Equal 14 @($releaseContract.BuildAssets).Count 'Release contract declares fourteen explicit application asset mappings'
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
    'holiday-data\2027.json',
    'holiday-data\README.md',
    'holiday-data\LICENSE.txt'
)
$installedSupportFiles = @($releaseContract.InstalledFiles | Where-Object { $_ -notlike 'app\*' })
Assert-Equal (($expectedInstalledSupportFiles | Sort-Object) -join ',') (($installedSupportFiles | Sort-Object) -join ',') 'Release contract installs exactly the runtime support files and offline holiday data'
$installedApplicationFiles = @($releaseContract.InstalledFiles | Where-Object { $_ -like 'app\*' })
$publishedPayloadManifest = Get-Content -LiteralPath (
    Join-Path $PSScriptRoot 'app\published-payload.json') -Raw -Encoding UTF8 | ConvertFrom-Json
Assert-Equal ((@($publishedPayloadManifest.files.path) | Sort-Object) -join ',') (($installedApplicationFiles | Sort-Object) -join ',') 'Release contract preserves every published application file without extra runtime files'
Assert-Equal 0 @($publishedPayloadManifest.files.path | Where-Object { $_ -match 'CsWin32|Win32Metadata|Win32Docs' }).Count '#333 build-time CsWin32 packages add zero runtime files to the published payload'
$expectedOfficePackageSupportFiles = @(
    'Install-Countdown.ps1',
    'Countdown.ReleaseContract.psm1',
    'Countdown.InstallTransaction.psm1',
    'Countdown.InstallLifecycle.psm1',
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
    'holiday-data\2027.json',
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
Assert-Equal $false ($notifyIconLicenseSource -in @($releaseContract.PackageFiles)) 'Office package gets the published H.NotifyIcon license copy instead of the repository source'
Assert-Equal 'B41F5ADB8391601E3C693462925317E4CBE446270BCD646BDB8D02572821FE80' (Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $PSScriptRoot $notifyIconLicenseSource)).Hash 'Vendored H.NotifyIcon license matches the upstream commit bytes'
Assert-Equal $false ($notifyIconLicenseSource -in @($releaseContract.InstalledFiles)) 'Runtime uses the published license copy instead of the repository source path'
foreach ($offlineHolidayFile in @('holiday-data\2026.json', 'holiday-data\2027.json', 'holiday-data\README.md', 'holiday-data\LICENSE.txt')) {
    Assert-Equal $true ($offlineHolidayFile -in @($releaseContract.PackageFiles)) "Release contract packages offline holiday data: $offlineHolidayFile"
    Assert-Equal $true $releaseContract.IntegrityHashes.Contains($offlineHolidayFile) "Offline holiday data is integrity protected: $offlineHolidayFile"
    Assert-Equal $true ($offlineHolidayFile -in @($releaseContract.InstalledFiles)) "Offline holiday data is installed: $offlineHolidayFile"
}
# #275 程序目录自足：随包节假日数据与图标随构建输出进 app\，载荷清单按仓库原字节覆盖它们，
# 安装与完整性钉值一并包含。安装根的同名副本暂留（只增不删），由上面的断言继续守住。
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
    Assert-Equal $repositoryHash ([string] $releaseContract.IntegrityHashes[$publishedPath]) "#275 release integrity pins the program-directory resource: $publishedPath"
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
    Assert-Equal $true ($installedReference -in @($releaseContract.RetiredRuntimeFiles)) "Plush reference is cleaned from existing installations: $installedReference"
}
Assert-Equal ($expectedBuildAssets -join ',') ($actualBuildAssets -join ',') 'Release contract exposes the exact build asset mappings'
Assert-Equal ($expectedFloatingPlushAssets -join ',') (@($releaseContract.FloatingPlushAssets) -join ',') 'Release contract retains only current and reference floating assets in the repository asset list'
Assert-Equal $false ($releaseContract.PSObject.Properties.Name -contains 'CompatibilityFixtureFiles') 'Release contract no longer exposes a legacy-host compatibility fixture list'
Assert-Equal $false ($releaseContract.PSObject.Properties.Name -contains 'LegacyFloatingRegressionAssets') 'Release contract no longer exposes a legacy floating regression asset list'
Assert-Equal $true ('Countdown.InstallLifecycle.psm1' -in @($releaseContract.PackageFiles)) 'Install lifecycle is packaged'
Assert-Equal $true $releaseContract.IntegrityHashes.Contains('Countdown.InstallLifecycle.psm1') 'Install lifecycle is integrity protected'
Assert-Equal $false ('Countdown.InstallLifecycle.psm1' -in @($releaseContract.InstalledFiles)) 'Install lifecycle is not installed as runtime'
foreach ($repositoryOnlyModule in @('Countdown.Validation.psm1')) {
    Assert-Equal $false ($repositoryOnlyModule -in @($releaseContract.PackageFiles)) "Module the office install never loads stays out of the office package: $repositoryOnlyModule"
    Assert-Equal $false $releaseContract.IntegrityHashes.Contains($repositoryOnlyModule) "Module the office install never loads is not statically pinned: $repositoryOnlyModule"
    Assert-Equal $false ($repositoryOnlyModule -in @($releaseContract.InstalledFiles)) "Module the office install never loads is not installed as runtime: $repositoryOnlyModule"
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

foreach ($trustRoot in @('Install-Countdown.ps1', 'Countdown.ReleaseContract.psm1')) {
    Assert-Equal $true ($trustRoot -in @($releaseContract.PackageTrustRoots)) "Release contract declares trust root $trustRoot"
    Assert-Equal $false $releaseContract.IntegrityHashes.Contains($trustRoot) "Release contract does not self-hash trust root $trustRoot"
}
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

    # 新 Windows 克隆即使 core.autocrlf=true，受 .gitattributes 约束的许可证仍须保持发布契约钉住的字节。
    # 这是真实 Git checkout 行为，不读取或断言任何部署脚本源码。
    $autocrlfSource = Join-Path $releaseContractTestRoot 'autocrlf-source'
    $autocrlfHoliday = New-Item -ItemType Directory -Path (Join-Path $autocrlfSource 'holiday-data') -Force
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot '.gitattributes') -Destination (Join-Path $autocrlfSource '.gitattributes')
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'holiday-data\LICENSE.txt') -Destination (Join-Path $autocrlfHoliday.FullName 'LICENSE.txt')
    & git init --quiet $autocrlfSource
    Assert-Equal 0 $LASTEXITCODE 'autocrlf fixture repository initializes'
    & git -C $autocrlfSource -c core.autocrlf=true add .
    Assert-Equal 0 $LASTEXITCODE 'autocrlf fixture adds locked text bytes'
    & git -C $autocrlfSource -c user.name=Test -c user.email=test@example.invalid -c core.hooksPath=NUL commit --quiet -m fixture
    Assert-Equal 0 $LASTEXITCODE 'autocrlf fixture commits locked text bytes'
    $autocrlfClone = Join-Path $releaseContractTestRoot 'autocrlf-checkout'
    & git -c core.autocrlf=true clone --quiet --no-local $autocrlfSource $autocrlfClone
    Assert-Equal 0 $LASTEXITCODE 'autocrlf fixture clones into a fresh checkout'
    Assert-Equal 'C1614A521EFA0BD3FC0D271D01E38E9450C8BA3A13825044C325E433B4870142' `
        (Get-FileHash -LiteralPath (Join-Path $autocrlfClone 'holiday-data\LICENSE.txt') -Algorithm SHA256).Hash `
        'Fresh autocrlf checkout preserves the release license hash'
    Remove-Item -LiteralPath $autocrlfSource, $autocrlfClone -Recurse -Force

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

    $holidayGuardEntryRoot = Join-Path $releaseContractTestRoot 'holiday-guard-entries'
    $holidayGuardEntryTools = Join-Path $holidayGuardEntryRoot 'tools'
    New-Item -ItemType Directory -Path $holidayGuardEntryTools -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Install-Countdown.ps1') -Destination $holidayGuardEntryRoot
    [System.IO.File]::WriteAllText(
        (Join-Path $holidayGuardEntryTools 'Countdown.SuiteStages.psm1'),
        '',
        [System.Text.UTF8Encoding]::new($false))
    $holidayGuardEntryModulePath = Join-Path $holidayGuardEntryRoot 'Countdown.ReleaseContract.psm1'
    $holidayGuardEntryEngine = (Get-Command pwsh.exe -CommandType Application -ErrorAction Stop | Select-Object -First 1).Source
    $PSNativeCommandUseErrorActionPreference = $false

    # 安装验证不是发布动作：即使注入缺数据结果，也只运行原有包校验夹具并成功返回。
    $holidayGuardValidationStubs = @'
function Get-CountdownReleaseContract {
    [pscustomobject]@{
        OfficialCharacterAssets = @()
        FloatingPlushAssets = @()
        CurrentFloatingRuntimeAssets = @()
        RetiredRuntimeFiles = @()
    }
}
function Assert-CountdownReleasePackage {
    param($Root)
    [pscustomobject]@{ Valid = $true; VerifiedFiles = 0 }
}
'@
    [System.IO.File]::WriteAllText(
        $holidayGuardEntryModulePath,
        $holidayGuardModuleSource + $holidayGuardOverride + $holidayGuardValidationStubs,
        [System.Text.UTF8Encoding]::new($false))
    $holidayGuardInstallValidationJson = & $holidayGuardEntryEngine `
        -NoLogo -NoProfile -NonInteractive `
        -File (Join-Path $holidayGuardEntryRoot 'Install-Countdown.ps1') `
        -ValidateOnly | Out-String
    $holidayGuardInstallValidationExit = $LASTEXITCODE
    Assert-Equal 0 $holidayGuardInstallValidationExit 'Installer ValidateOnly remains available when December holiday coverage is missing'
    if ($holidayGuardInstallValidationExit -eq 0) {
        $holidayGuardInstallValidation = $holidayGuardInstallValidationJson | ConvertFrom-Json
        Assert-Equal $true ([bool] $holidayGuardInstallValidation.Valid) 'Installer ValidateOnly returns its normal success contract under the coverage fixture'
    }

    # 真实安装夹具在任何后续发布契约读取处放置安全哨兵；只有入口先调用守卫才会得到年份错误。
    $holidayGuardInstallSentinel = @'
function Get-CountdownReleaseContract { throw 'REAL_INSTALL_CONTINUED_WITHOUT_COVERAGE_GUARD' }
'@
    [System.IO.File]::WriteAllText(
        $holidayGuardEntryModulePath,
        $holidayGuardModuleSource + $holidayGuardOverride + $holidayGuardInstallSentinel,
        [System.Text.UTF8Encoding]::new($false))
    $holidayGuardRealInstallOutput = & $holidayGuardEntryEngine `
        -NoLogo -NoProfile -NonInteractive `
        -File (Join-Path $holidayGuardEntryRoot 'Install-Countdown.ps1') 2>&1 | Out-String
    $holidayGuardRealInstallExit = $LASTEXITCODE
    Assert-Equal $true ($holidayGuardRealInstallExit -ne 0) 'Real install entry fails closed when December holiday coverage is missing'
    Assert-Equal $true ($holidayGuardRealInstallOutput.Contains('2027') -and $holidayGuardRealInstallOutput.Contains('holiday-data/README.md')) 'Real install guard names the missing year and refresh runbook'
    Assert-Equal $false $holidayGuardRealInstallOutput.Contains('REAL_INSTALL_CONTINUED_WITHOUT_COVERAGE_GUARD') 'Real install guard runs before any installation continuation'

    $global:LASTEXITCODE = 0
    Remove-Item -LiteralPath $holidayGuardModulePath -Force
    Remove-Item -LiteralPath $holidayGuardEntryRoot -Recurse -Force

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
    foreach ($retiredLegacyPackageFile in @(
            'Countdown.App.psm1',
            'Countdown.Core.psm1',
            'assets\floating-plush\floating-plush-mymelody-front.png',
            'assets\floating-plush\floating-plush-mymelody-pose.png',
            'assets\floating-plush\floating-plush-cinnamoroll-front.png',
            'assets\floating-plush\floating-plush-cinnamoroll-pose.png',
            'assets\floating-plush\floating-plush-kuromi-front.png',
            'assets\floating-plush\floating-plush-kuromi-pose.png'
        )) {
        $retiredLegacyPackagePath = Join-Path $releaseContractTestRoot $retiredLegacyPackageFile
        $retiredLegacyPackageParent = Split-Path -Parent $retiredLegacyPackagePath
        if (-not (Test-Path -LiteralPath $retiredLegacyPackageParent -PathType Container)) {
            New-Item -ItemType Directory -Path $retiredLegacyPackageParent -Force | Out-Null
        }
        [System.IO.File]::WriteAllText($retiredLegacyPackagePath, 'retired legacy host fixture', [System.Text.UTF8Encoding]::new($false))
        $actualPackageWithRetired = @(
            Get-ChildItem -LiteralPath $releaseContractTestRoot -File -Recurse |
                ForEach-Object { $_.FullName.Substring($releaseContractTestRoot.Length + 1) })
        $expectedPackageWithRetired = @($releaseContract.PackageFiles) + @($retiredLegacyPackageFile)
        Assert-Equal `
            (($expectedPackageWithRetired | Sort-Object) -join ',') `
            (($actualPackageWithRetired | Sort-Object) -join ',') `
            "Package inventory identifies exactly one deleted legacy-host addition: $retiredLegacyPackageFile"
        $unexpectedPackageFiles = @($actualPackageWithRetired | Where-Object { $_ -notin @($releaseContract.PackageFiles) })
        Assert-Equal $retiredLegacyPackageFile (@($unexpectedPackageFiles) -join ',') "Deleted legacy-host content is outside the package allowlist: $retiredLegacyPackageFile"
        Remove-Item -LiteralPath $retiredLegacyPackagePath -Force
    }

    function Write-InvalidReleaseContentWithRetry {
        param([Parameter(Mandatory)] [string] $Path)

        $deadline = [DateTime]::UtcNow.AddSeconds(10)
        while ($true) {
            try {
                [System.IO.File]::WriteAllText($Path, 'invalid release content', [System.Text.UTF8Encoding]::new($false))
                return
            }
            catch [System.IO.IOException] {
                if ([DateTime]::UtcNow -ge $deadline) { throw }
                [System.Threading.Thread]::Sleep(100)
            }
        }
    }

    $lockRetryFixturePath = Join-Path $releaseContractTestRoot 'invalid-content-lock-retry.txt'
    [System.IO.File]::WriteAllText($lockRetryFixturePath, 'original', [System.Text.UTF8Encoding]::new($false))
    $lockRetryStream = [System.IO.File]::Open(
        $lockRetryFixturePath,
        [System.IO.FileMode]::Open,
        [System.IO.FileAccess]::Write,
        [System.IO.FileShare]::None)
    $lockRetryReleaseJob = Start-ThreadJob -ScriptBlock {
        Start-Sleep -Milliseconds 500
        ($using:lockRetryStream).Dispose()
    }
    try {
        Write-InvalidReleaseContentWithRetry -Path $lockRetryFixturePath
    }
    finally {
        [void] (Receive-CountdownTestThreadJob -Job $lockRetryReleaseJob)
        $lockRetryStream.Dispose()
    }
    Assert-Equal 'invalid release content' `
        ([System.IO.File]::ReadAllText($lockRetryFixturePath)) `
        'Invalid-content overwrite waits for a FileShare.None lock to be released'

    foreach ($relativePath in $releaseContract.PackageFiles) {
        $testPath = Join-Path $releaseContractTestRoot $relativePath
        $testParent = Split-Path -Parent $testPath
        if (-not (Test-Path -LiteralPath $testParent -PathType Container)) {
            New-Item -ItemType Directory -Path $testParent -Force | Out-Null
        }
        Write-InvalidReleaseContentWithRetry -Path $testPath
    }
    Assert-Equal 'invalid release content' `
        ([System.IO.File]::ReadAllText((Join-Path $releaseContractTestRoot 'app\MinminCountdown.exe'))) `
        'Invalid-content retry still overwrites the application payload'
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
