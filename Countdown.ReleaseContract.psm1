#requires -Version 7.0

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$script:packageTrustRoots = @(
    'Deploy-OfficeCountdown.ps1',
    'Install-Countdown.ps1',
    'Countdown.ReleaseContract.psm1'
)

$script:buildAssetDefinitions = @(
    [pscustomobject]@{
        SourcePath = 'sanrio-official-mymelody.png'
        PublishedPath = 'app\assets\sanrio-official-mymelody.png'
        SHA256 = '6C53BD64E8F601C0709C69DAEE863F282EAA763AFFA2F4F96CE77B5AA2B0B41C'
        Category = 'OfficialCharacter'
    },
    [pscustomobject]@{
        SourcePath = 'sanrio-official-cinnamoroll.png'
        PublishedPath = 'app\assets\sanrio-official-cinnamoroll.png'
        SHA256 = '9344D335E7591C8599E7CC32730B315401037D58E15C14DCF23853DE74867520'
        Category = 'OfficialCharacter'
    },
    [pscustomobject]@{
        SourcePath = 'sanrio-official-kuromi.png'
        PublishedPath = 'app\assets\sanrio-official-kuromi.png'
        SHA256 = '3DC52AAC57897374436C3394C434DE26090482A8C11E6BDF4BD7F3DCE581D4D0'
        Category = 'OfficialCharacter'
    },
    [pscustomobject]@{
        SourcePath = 'assets\floating-plush\component-day-outer-shell.png'
        PublishedPath = 'app\assets\floating-plush\component-day-outer-shell.png'
        SHA256 = 'A4F2F78163F8AA5217C4161F0EE2BFB924686E8BED5768C527F3B5C7D6CC4924'
        Category = 'CurrentFloating'
    },
    [pscustomobject]@{
        SourcePath = 'assets\floating-plush\component-day-header-blank.png'
        PublishedPath = 'app\assets\floating-plush\component-day-header-blank.png'
        SHA256 = '857B570F1CD47C29F1C2744251DE1B1C7B4D9D921944BB79E974B0B60B26C1B1'
        Category = 'CurrentFloating'
    },
    [pscustomobject]@{
        SourcePath = 'assets\floating-plush\component-day-card-blank.png'
        PublishedPath = 'app\assets\floating-plush\component-day-card-blank.png'
        SHA256 = '253EF68502B1F798E37DDF02BF17F9BEE08D818B8AEE76EA287A4403226FB1C8'
        Category = 'CurrentFloating'
    },
    [pscustomobject]@{
        SourcePath = 'assets\floating-plush\component-day-mascots.png'
        PublishedPath = 'app\assets\floating-plush\component-day-mascots.png'
        SHA256 = 'D000285BFFC2CBCF3411C11407FF691E531A8602E9D3E8F3FD68EBFC4ADC0726'
        Category = 'CurrentFloating'
    },
    [pscustomobject]@{
        SourcePath = 'assets\main-decorations\main-countdown-scene-day.png'
        PublishedPath = 'app\assets\main-decorations\main-countdown-scene-day.png'
        SHA256 = '5455A25CA3006EB50FBD0C42ECD0AEBCFF41A2464D812FADE09CE6FE671A18B3'
        Category = 'MainDecoration'
    },
    [pscustomobject]@{
        SourcePath = 'assets\main-decorations\main-notes-scene-day.png'
        PublishedPath = 'app\assets\main-decorations\main-notes-scene-day.png'
        SHA256 = '2369048E05CB229115FBA119E4F4DFC833AEFC6D281C66655C8340B644526FDB'
        Category = 'MainDecoration'
    }
)

$script:legacyFloatingRegressionAssets = @(
    'assets\floating-plush\floating-plush-mymelody-front.png',
    'assets\floating-plush\floating-plush-mymelody-pose.png',
    'assets\floating-plush\floating-plush-cinnamoroll-front.png',
    'assets\floating-plush\floating-plush-cinnamoroll-pose.png',
    'assets\floating-plush\floating-plush-kuromi-front.png',
    'assets\floating-plush\floating-plush-kuromi-pose.png'
)

$script:floatingPlushAssets = @(
    'assets\floating-plush\floating-plush-mymelody-front.png',
    'assets\floating-plush\floating-plush-mymelody-pose.png',
    'assets\floating-plush\floating-plush-mymelody-reference.png',
    'assets\floating-plush\floating-plush-cinnamoroll-front.png',
    'assets\floating-plush\floating-plush-cinnamoroll-pose.png',
    'assets\floating-plush\floating-plush-cinnamoroll-reference.png',
    'assets\floating-plush\floating-plush-kuromi-front.png',
    'assets\floating-plush\floating-plush-kuromi-pose.png',
    'assets\floating-plush\floating-plush-kuromi-reference.png',
    'assets\floating-plush\component-day-outer-shell.png',
    'assets\floating-plush\component-day-header-blank.png',
    'assets\floating-plush\component-day-card-blank.png',
    'assets\floating-plush\component-day-mascots.png'
)

$script:offlineHolidayDataFiles = @(
    'holiday-data\2026.json',
    'holiday-data\README.md',
    'holiday-data\LICENSE.txt'
)

$script:sourceBuildNonAssetFiles = @(
    'global.json',
    'src\MinminCountdown.Core\CalendarOccurrenceCalculator.cs',
    'src\MinminCountdown.Core\DesktopWorkspace.cs',
    'src\MinminCountdown.Core\DesktopWorkspace.Transactions.cs',
    'src\MinminCountdown.Core\DomainLogic.cs',
    'src\MinminCountdown.Core\FloatingCountdownProjection.cs',
    'src\MinminCountdown.Core\MinminCountdown.Core.csproj',
    'src\MinminCountdown.Core\ReminderEditing.cs',
    'src\MinminCountdown.Core\SettingsContract.cs',
    'src\MinminCountdown.Core\SettingsFileOperations.cs',
    'src\MinminCountdown.Core\SettingsModels.cs',
    'src\MinminCountdown.Core\DateDisplay.cs',
    'src\MinminCountdown.Core\NoteOrdering.cs',
    'src\MinminCountdown.Core\SettingsRecovery.cs',
    'src\MinminCountdown.Core\SettingsStore.cs',
    'src\MinminCountdown.Core\SettingsSynchronization.cs',
    'src\MinminCountdown\App.xaml',
    'src\MinminCountdown\App.xaml.cs',
    'src\MinminCountdown\ApplicationTheme.cs',
    'src\MinminCountdown\ApplicationValidation.cs',
    'src\MinminCountdown\DeleteUndoOffer.cs',
    'src\MinminCountdown\DiagnosticLogger.cs',
    'src\MinminCountdown\DwmWindowChrome.cs',
    'src\MinminCountdown\FloatingBottomStatus.cs',
    'src\MinminCountdown\FloatingTrayController.cs',
    'src\MinminCountdown\FloatingWindow.Countdowns.cs',
    'src\MinminCountdown\FloatingWindow.Notes.cs',
    'src\MinminCountdown\FloatingWindow.xaml',
    'src\MinminCountdown\FloatingWindow.xaml.cs',
    'src\MinminCountdown\GlobalUsings.cs',
    'src\MinminCountdown\HolidayCalendar.cs',
    'src\MinminCountdown\HostOptions.cs',
    'src\MinminCountdown\InstanceActivation.cs',
    'src\MinminCountdown\MainWindow.xaml',
    'src\MinminCountdown\MainWindow.xaml.cs',
    'src\MinminCountdown\MinminCountdown.csproj',
    'src\MinminCountdown\app.manifest',
    'src\MinminCountdown\MonitorGeometryValidation.cs',
    'src\MinminCountdown\MonitorWorkAreaResolver.cs',
    'src\MinminCountdown\MotionBehavior.cs',
    'src\MinminCountdown\NoteCompletionExit.cs',
    'src\MinminCountdown\ReminderCenterViewModel.cs',
    'src\MinminCountdown\ReminderRulesEditorViewModel.cs',
    'src\MinminCountdown\ReminderRulesEditorWindow.cs',
    'src\MinminCountdown\ReminderWindow.cs',
    'src\MinminCountdown\RuntimeProtocol.cs',
    'src\MinminCountdown\SegmentIndicator.cs',
    'src\MinminCountdown\SettingsRecoveryWindow.cs',
    'src\MinminCountdown\TrayIconHost.cs',
    'src\MinminCountdown\UiControls.cs',
    'src\MinminCountdown\ValidationExercises.cs',
    'src\MinminCountdown\ValidationReport.cs',
    'src\MinminCountdown\ValidationReportContract.cs',
    'src\MinminCountdown\VisualReferenceBaseline.cs',
    'src\MinminCountdown\WindowValidationScenarios.cs',
    'src\MinminCountdown\WindowWorkAreaGeometry.cs'
)

$script:sourceBuildFiles = @($script:sourceBuildNonAssetFiles) + @('countdown-icon.ico') + @(
    $script:buildAssetDefinitions | ForEach-Object { [string] $_.SourcePath }
)

# 发布包只装办公室安装路径会加载或安装的文件，静态钉值也只覆盖这些文件。C# 源码、测试、构建与验证工具、
# DESIGN.md 留在仓库：正式部署在构建前按提交 blob 身份核对构建输入（BuildInputFiles），不在这里钉死。
$script:nonAssetHashes = [ordered]@{
    'Countdown.InstallTransaction.psm1' = 'B89E634CE9A67183084931C3F9864E27E03C2C6D4CF9BABC3EA5B924262FA08F'
    'Countdown.InstallLifecycle.psm1' = '87304F33DF58FBD358362DDFC9711756041C606271BC665B0ACA1A80B37212BD'
    'Countdown.RemoteDeployment.psm1' = 'F11A927F40AFFCF933670C609223EE6B2B4C56B2EC576732690F7D39703AF30B'
    'Countdown.TransferLedger.psm1' = 'B3051D0F5AF1F7E0351A9637B076E0402DCC9500A21CC41C02594EC9012D88B8'
    'Countdown.Notes.psm1' = 'CF41E5A29B5FCFB0807E1EA1E6F52AC7FADE4478A18990698F2BA6F725E08CA0'
    'Countdown.Runtime.psm1' = '29733FCD6CF36E797800EC7678AF1685E97A308CC67FEE38975FE6CC54709856'
    'Countdown.Settings.psm1' = '507BB437A209CC6F340B00D7089171C6D78EFF6C6EF597A87F49E45BF40E95DC'
    'Countdown.Tags.psm1' = '57AE8BBE689F3838E3B9EE8B56F96D661C8A46EAD825103307482B00C472FAAC'
    '敏敏子倒计时.ps1' = '3A4C018DEE2C1453D01BA4C837EBB4E207C1422D0301CE2973757CD4670B3106'
    'CountdownLauncher.exe' = '4B4C66274278049FF185C7824685D1B5A85F5DA10557F70A1D7E458778066440'
    'countdown-icon.ico' = 'C5B7CB0A09524AEFE4488291078F61B0AF54958582DA98B99B25D0F4DC6976C5'
    'holiday-data\2026.json' = '5B9887FB139FA190150BF0FC3E8EDB32A06647E884F1447633B305783ADACEBB'
    'holiday-data\README.md' = 'E177C43E1EA189D8C191A058DFC46E0E524ED4DA78881DD1E95CD600E508D53F'
    'holiday-data\LICENSE.txt' = 'C1614A521EFA0BD3FC0D271D01E38E9450C8BA3A13825044C325E433B4870142'
}

# 构建脚本自身读取、但不在 MSBuild 项目闭包里的输入：构建前导入的预检模块，以及复制进载荷的许可证原文。
$script:buildScriptInputFiles = @(
    'tools\Countdown.BuildPreflight.psm1',
    'docs\third-party\H.NotifyIcon\2.4.1\LICENSE.md'
)

function Assert-CountdownReleaseRelativePath {
    param([Parameter(Mandatory)] [string] $Path)

    if ([string]::IsNullOrWhiteSpace($Path) -or
        [System.IO.Path]::IsPathRooted($Path) -or
        [System.Management.Automation.WildcardPattern]::ContainsWildcardCharacters($Path) -or
        $Path.Contains('/') -or
        $Path.IndexOfAny([System.IO.Path]::GetInvalidPathChars()) -ge 0) {
        throw "发布契约包含无效相对路径：$Path"
    }

    $segments = $Path.Split('\')
    $invalidFileNameChars = [System.IO.Path]::GetInvalidFileNameChars()
    if ($segments.Count -eq 0 -or @(
            $segments | Where-Object {
                $_ -in @('', '.', '..') -or
                $_.IndexOfAny($invalidFileNameChars) -ge 0 -or
                $_.EndsWith(' ') -or
                $_.EndsWith('.')
            }
        ).Count -ne 0) {
        throw "发布契约包含不安全路径片段：$Path"
    }
}

function Assert-CountdownUniqueReleasePaths {
    param(
        [Parameter(Mandatory)] [string] $Name,
        [Parameter(Mandatory)] [string[]] $Paths
    )

    $seen = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($path in $Paths) {
        Assert-CountdownReleaseRelativePath -Path $path
        if (-not $seen.Add($path)) {
            throw "发布契约的 $Name 包含重复路径：$path"
        }
    }
}

# #218 节假日年度覆盖守卫：纯函数，注入北京时间与随包覆盖年份集合。北京时间进入 12 月后，
# 随包数据必须已覆盖下一年；否则验证明确失败并指明缺失年份与补数据流程。
function Test-CountdownHolidayYearCoverage {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [DateTimeOffset] $BeijingNow,
        [Parameter(Mandatory)] [AllowEmptyCollection()] [int[]] $PackagedYears
    )

    $nextYear = $BeijingNow.Year + 1
    if ($BeijingNow.Month -lt 12 -or $PackagedYears.Contains($nextYear)) {
        return [pscustomobject]@{
            Covered = $true
            MissingYear = $null
            Message = ''
        }
    }

    [pscustomobject]@{
        Covered = $false
        MissingYear = $nextYear
        Message = "随包节假日数据尚未覆盖 $nextYear 年：当前北京时间已进入 $($BeijingNow.Year) 年 12 月。请按 holiday-data/README.md 的更新流程补齐 $nextYear 年随包数据（固定上游提交、重新生成全年 days 数组、更新来源提交、年份与 IntegrityHashes，工单 #234），再重新运行验证。"
    }
}

function Assert-CountdownHolidayYearCoverage {
    $packagedYears = @(
        foreach ($file in $script:offlineHolidayDataFiles) {
            if ($file -match '^holiday-data\\(\d{4})\.json$') {
                [int] $Matches[1]
            }
        }
    )
    $beijingNow = [System.TimeZoneInfo]::ConvertTimeBySystemTimeZoneId(
        [DateTimeOffset]::UtcNow, 'China Standard Time')
    $coverage = Test-CountdownHolidayYearCoverage -BeijingNow $beijingNow -PackagedYears $packagedYears
    if (-not $coverage.Covered) {
        throw $coverage.Message
    }
}

function Get-CountdownPublishedPayload {
    $manifestPath = Join-Path $PSScriptRoot 'app\published-payload.json'
    if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
        return [pscustomobject]@{
            AggregateSha256 = ''
            Files = @()
        }
    }

    $manifest = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
    $files = @(
        foreach ($file in @($manifest.files)) {
            [pscustomobject]@{
                Path = [string] $file.path
                SHA256 = [string] $file.sha256
            }
        }
    )
    if ($files.Count -lt 2) {
        throw "发布载荷清单文件数无效：$($files.Count)"
    }

    [pscustomobject]@{
        AggregateSha256 = [string] $manifest.aggregateSha256
        Files = $files
    }
}

function Get-CountdownReleaseHashByPath {
    param([Parameter(Mandatory)] [string] $Path)

    if ($script:nonAssetHashes.Contains($Path)) {
        return [string] $script:nonAssetHashes[$Path]
    }

    foreach ($asset in $script:buildAssetDefinitions) {
        if ([string]::Equals($Path, [string] $asset.SourcePath, [System.StringComparison]::OrdinalIgnoreCase) -or
            [string]::Equals($Path, [string] $asset.PublishedPath, [System.StringComparison]::OrdinalIgnoreCase)) {
            return [string] $asset.SHA256
        }
    }

    foreach ($payloadFile in @(Get-CountdownPublishedPayload).Files) {
        if ([string]::Equals($Path, [string] $payloadFile.Path, [System.StringComparison]::OrdinalIgnoreCase)) {
            return [string] $payloadFile.SHA256
        }
    }

    if ([string]::Equals($Path, 'app\published-payload.json', [System.StringComparison]::OrdinalIgnoreCase)) {
        $manifestPath = Join-Path $PSScriptRoot 'app\published-payload.json'
        if (Test-Path -LiteralPath $manifestPath -PathType Leaf) {
            return [string] (Get-FileHash -Algorithm SHA256 -LiteralPath $manifestPath).Hash
        }
    }

    throw "发布契约缺少静态 SHA-256：$Path"
}

function New-CountdownReleaseIntegrityMap {
    $integrityPaths = @($script:nonAssetHashes.Keys)
    $integrityPaths += @(
        $script:buildAssetDefinitions |
            Where-Object { $_.Category -eq 'OfficialCharacter' } |
            ForEach-Object { $_.SourcePath }
    )
    $integrityPaths += @($script:buildAssetDefinitions | ForEach-Object { $_.PublishedPath })
    $publishedPayloadFiles = @(Get-CountdownPublishedPayload | ForEach-Object { $_.Files })
    $integrityPaths += @($publishedPayloadFiles | ForEach-Object { $_.Path })
    if ($publishedPayloadFiles.Count -gt 0) {
        # 载荷清单文件本身必须随包传输并受哈希保护，远端解包后才能重建同一份含载荷的契约。
        $integrityPaths += 'app\published-payload.json'
    }
    $uniqueIntegrity = [System.Collections.Generic.List[string]]::new()
    $seenIntegrity = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($path in $integrityPaths) {
        if ($seenIntegrity.Add($path)) {
            $uniqueIntegrity.Add($path)
        }
    }
    $integrityPaths = @($uniqueIntegrity)
    Assert-CountdownUniqueReleasePaths -Name 'IntegrityHashes' -Paths $integrityPaths

    $hashes = [ordered]@{}
    foreach ($path in $integrityPaths) {
        $hash = Get-CountdownReleaseHashByPath -Path $path
        if ($hash -notmatch '^[A-F0-9]{64}$') {
            throw "发布契约包含无效 SHA-256：$path"
        }
        $hashes[$path] = $hash
    }
    $hashes
}

function New-CountdownReleaseContractSnapshot {
    $integrityHashes = New-CountdownReleaseIntegrityMap
    $officialCharacterAssets = @(
        $script:buildAssetDefinitions |
            Where-Object { $_.Category -eq 'OfficialCharacter' } |
            ForEach-Object { [string] $_.SourcePath }
    )
    $currentFloatingRuntimeAssets = @(
        $script:buildAssetDefinitions |
            Where-Object { $_.Category -eq 'CurrentFloating' } |
            ForEach-Object { [string] $_.PublishedPath }
    )
    $applicationAssets = @($script:buildAssetDefinitions | ForEach-Object { [string] $_.PublishedPath })
    $installedFiles = @(
        $script:offlineHolidayDataFiles
    ) + @(
        'Countdown.InstallTransaction.psm1',
        'Countdown.Notes.psm1',
        'Countdown.Runtime.psm1',
        'Countdown.Settings.psm1',
        'Countdown.Tags.psm1',
        '敏敏子倒计时.ps1'
    ) + @(
        'CountdownLauncher.exe',
        'countdown-icon.ico'
    ) + $officialCharacterAssets + $applicationAssets + @(
        Get-CountdownPublishedPayload | ForEach-Object { $_.Files } | ForEach-Object { $_.Path }
    )
    $uniqueInstalled = [System.Collections.Generic.List[string]]::new()
    $seenInstalled = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($path in $installedFiles) {
        if ($seenInstalled.Add($path)) {
            $uniqueInstalled.Add($path)
        }
    }
    $installedFiles = @($uniqueInstalled)
    # 旧 PowerShell WPF 宿主及其独占的核心 module 只留在仓库作兼容回归夹具，安装时从运行目录移除残留。
    $retiredRuntimeFiles = @('Countdown.App.psm1', 'Countdown.Core.psm1') + $script:floatingPlushAssets + @(
        'app\assets\floating-plush\floating-plush-mymelody-front.png',
        'app\assets\floating-plush\floating-plush-mymelody-pose.png',
        'app\assets\floating-plush\floating-plush-cinnamoroll-front.png',
        'app\assets\floating-plush\floating-plush-cinnamoroll-pose.png',
        'app\assets\floating-plush\floating-plush-kuromi-front.png',
        'app\assets\floating-plush\floating-plush-kuromi-pose.png',
        # #231：参考图自本版起不再随应用安装，清理既有安装的残留副本。
        'app\assets\floating-plush\floating-plush-mymelody-reference.png',
        'app\assets\floating-plush\floating-plush-cinnamoroll-reference.png',
        'app\assets\floating-plush\floating-plush-kuromi-reference.png'
    )
    $compatibilityFixtures = @('Countdown.App.psm1', 'Countdown.Core.psm1') + $officialCharacterAssets + $script:legacyFloatingRegressionAssets
    $packageFiles = @($script:packageTrustRoots) + @($integrityHashes.Keys)

    $hashCopy = [ordered]@{}
    foreach ($entry in $integrityHashes.GetEnumerator()) {
        $hashCopy[[string] $entry.Key] = [string] $entry.Value
    }
    $buildAssetCopies = @(
        foreach ($asset in $script:buildAssetDefinitions) {
            [pscustomobject]@{
                SourcePath = [string] $asset.SourcePath
                PublishedPath = [string] $asset.PublishedPath
                SHA256 = [string] $asset.SHA256
                Category = [string] $asset.Category
            }
        }
    )

    [pscustomobject]@{
        ContractVersion = 1
        PackageTrustRoots = [string[]] @($script:packageTrustRoots)
        PackageFiles = [string[]] $packageFiles
        IntegrityHashes = $hashCopy
        BuildAssets = [object[]] $buildAssetCopies
        InstalledFiles = [string[]] $installedFiles
        CompatibilityFixtureFiles = [string[]] $compatibilityFixtures
        RetiredRuntimeFiles = [string[]] $retiredRuntimeFiles
        OfficialCharacterAssets = [string[]] $officialCharacterAssets
        FloatingPlushAssets = [string[]] @($script:floatingPlushAssets)
        CurrentFloatingRuntimeAssets = [string[]] $currentFloatingRuntimeAssets
        LegacyFloatingRegressionAssets = [string[]] @($script:legacyFloatingRegressionAssets)
        SourceBuildFiles = [string[]] @($script:sourceBuildFiles)
        BuildInputFiles = [string[]] (@($script:sourceBuildFiles) + @($script:buildScriptInputFiles))
        OfflineHolidayDataFiles = [string[]] @($script:offlineHolidayDataFiles)
    }
}

function Assert-CountdownReleaseContractDefinition {
    $contract = New-CountdownReleaseContractSnapshot
    Assert-CountdownUniqueReleasePaths -Name 'PackageTrustRoots' -Paths $contract.PackageTrustRoots
    Assert-CountdownUniqueReleasePaths -Name 'PackageFiles' -Paths $contract.PackageFiles
    Assert-CountdownUniqueReleasePaths -Name 'InstalledFiles' -Paths $contract.InstalledFiles
    Assert-CountdownUniqueReleasePaths -Name 'RetiredRuntimeFiles' -Paths $contract.RetiredRuntimeFiles
    Assert-CountdownUniqueReleasePaths -Name 'CompatibilityFixtureFiles' -Paths $contract.CompatibilityFixtureFiles
    Assert-CountdownUniqueReleasePaths -Name 'SourceBuildFiles' -Paths $contract.SourceBuildFiles
    Assert-CountdownUniqueReleasePaths -Name 'BuildInputFiles' -Paths $contract.BuildInputFiles
    Assert-CountdownUniqueReleasePaths -Name 'BuildAssets.SourcePath' -Paths @($contract.BuildAssets.SourcePath)
    Assert-CountdownUniqueReleasePaths -Name 'BuildAssets.PublishedPath' -Paths @($contract.BuildAssets.PublishedPath)

    $packageSet = [System.Collections.Generic.HashSet[string]]::new($contract.PackageFiles, [System.StringComparer]::OrdinalIgnoreCase)
    $integritySet = [System.Collections.Generic.HashSet[string]]::new([string[]] @($contract.IntegrityHashes.Keys), [System.StringComparer]::OrdinalIgnoreCase)
    $installedSet = [System.Collections.Generic.HashSet[string]]::new($contract.InstalledFiles, [System.StringComparer]::OrdinalIgnoreCase)
    $retiredSet = [System.Collections.Generic.HashSet[string]]::new($contract.RetiredRuntimeFiles, [System.StringComparer]::OrdinalIgnoreCase)
    foreach ($path in $integritySet) {
        if (-not $packageSet.Contains($path)) { throw "受哈希文件不在发布包内：$path" }
    }
    foreach ($path in $installedSet) {
        if (-not $integritySet.Contains($path)) { throw "安装运行时文件未受哈希保护：$path" }
        if ($retiredSet.Contains($path)) { throw "安装运行时文件同时被标记为已淘汰：$path" }
    }
    foreach ($asset in $contract.BuildAssets) {
        Assert-CountdownReleaseRelativePath -Path $asset.SourcePath
        Assert-CountdownReleaseRelativePath -Path $asset.PublishedPath
        if (-not $installedSet.Contains($asset.PublishedPath) -or -not $integritySet.Contains($asset.PublishedPath)) {
            throw "构建资产目标不属于受保护运行时：$($asset.PublishedPath)"
        }
    }
    foreach ($path in @($contract.PackageFiles) + @($contract.InstalledFiles) + @($contract.BuildAssets.PublishedPath)) {
        if ($path -match '(^|\\)(bin|obj)(\\|$)') {
            throw "发布契约不得包含构建输出目录：$path"
        }
        if ($path -like 'assets\ui-references\*' -or $path -like 'app\assets\ui-references\*') {
            throw "UI 参考图不得进入发布运行时：$path"
        }
    }

    # #218 年度覆盖守卫：北京时间进入 12 月后随包数据必须已覆盖下一年。
    Assert-CountdownHolidayYearCoverage
}

function Resolve-CountdownReleasePackagePath {
    param(
        [Parameter(Mandatory)] [string] $Root,
        [Parameter(Mandatory)] [string] $RelativePath
    )

    Assert-CountdownReleaseRelativePath -Path $RelativePath
    $rootPath = [System.IO.Path]::GetFullPath($Root)
    $resolved = [System.IO.Path]::GetFullPath((Join-Path $rootPath $RelativePath))
    $rootPrefix = if ($rootPath.EndsWith('\')) { $rootPath } else { $rootPath + '\' }
    if (-not $resolved.StartsWith($rootPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "发布包路径逃出根目录：$RelativePath"
    }
    $resolved
}

function Get-CountdownReleaseContract {
    [CmdletBinding()]
    param()

    New-CountdownReleaseContractSnapshot
}

function Assert-CountdownReleasePackage {
    [CmdletBinding()]
    param([Parameter(Mandatory)] [string] $Root)

    $rootPath = [System.IO.Path]::GetFullPath($Root)
    if (-not (Test-Path -LiteralPath $rootPath -PathType Container)) {
        throw "发布包根目录不存在：$rootPath"
    }

    $contract = Get-CountdownReleaseContract
    foreach ($relativePath in $contract.PackageFiles) {
        $path = Resolve-CountdownReleasePackagePath -Root $rootPath -RelativePath $relativePath
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
            throw "缺少发布包文件：$relativePath"
        }
    }
    foreach ($entry in $contract.IntegrityHashes.GetEnumerator()) {
        $path = Resolve-CountdownReleasePackagePath -Root $rootPath -RelativePath ([string] $entry.Key)
        $actual = (Get-FileHash -Algorithm SHA256 -LiteralPath $path).Hash
        if (-not [string]::Equals($actual, [string] $entry.Value, [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "发布包哈希不匹配：$($entry.Key)"
        }
    }

    [pscustomobject]@{
        Valid = $true
        VerifiedFiles = $contract.IntegrityHashes.Count
    }
}

Assert-CountdownReleaseContractDefinition
Export-ModuleMember -Function Get-CountdownReleaseContract, Assert-CountdownReleasePackage, Test-CountdownHolidayYearCoverage
