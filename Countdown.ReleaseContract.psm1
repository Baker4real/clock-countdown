#requires -Version 7.0

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$script:packageInstallerFiles = @(
    'Install-CountdownProgram.ps1',
    'Countdown.Installer.psm1'
)

$script:buildAssetDefinitions = @(
    [pscustomobject]@{
        SourcePath = 'sanrio-official-mymelody.png'
        PublishedPath = 'app\assets\sanrio-official-mymelody.png'
        Category = 'OfficialCharacter'
    },
    [pscustomobject]@{
        SourcePath = 'sanrio-official-cinnamoroll.png'
        PublishedPath = 'app\assets\sanrio-official-cinnamoroll.png'
        Category = 'OfficialCharacter'
    },
    [pscustomobject]@{
        SourcePath = 'sanrio-official-kuromi.png'
        PublishedPath = 'app\assets\sanrio-official-kuromi.png'
        Category = 'OfficialCharacter'
    },
    [pscustomobject]@{
        SourcePath = 'assets\floating-plush\component-day-outer-shell.png'
        PublishedPath = 'app\assets\floating-plush\component-day-outer-shell.png'
        Category = 'CurrentFloating'
    },
    [pscustomobject]@{
        SourcePath = 'assets\floating-plush\component-day-header-blank.png'
        PublishedPath = 'app\assets\floating-plush\component-day-header-blank.png'
        Category = 'CurrentFloating'
    },
    [pscustomobject]@{
        SourcePath = 'assets\floating-plush\component-day-card-blank.png'
        PublishedPath = 'app\assets\floating-plush\component-day-card-blank.png'
        Category = 'CurrentFloating'
    },
    [pscustomobject]@{
        SourcePath = 'assets\floating-plush\component-day-mascots.png'
        PublishedPath = 'app\assets\floating-plush\component-day-mascots.png'
        Category = 'CurrentFloating'
    },
    [pscustomobject]@{
        SourcePath = 'assets\main-decorations\main-countdown-scene-day.png'
        PublishedPath = 'app\assets\main-decorations\main-countdown-scene-day.png'
        Category = 'MainDecoration'
    },
    [pscustomobject]@{
        SourcePath = 'assets\main-decorations\main-notes-scene-day.png'
        PublishedPath = 'app\assets\main-decorations\main-notes-scene-day.png'
        Category = 'MainDecoration'
    },
    # #275 程序目录自足：随包节假日数据与图标随载荷进 app\，应用从程序目录读取。
    [pscustomobject]@{
        SourcePath = 'holiday-data\2026.json'
        PublishedPath = 'app\holiday-data\2026.json'
        Category = 'OfflineHolidayData'
    },
    [pscustomobject]@{
        SourcePath = 'holiday-data\2027.json'
        PublishedPath = 'app\holiday-data\2027.json'
        Category = 'OfflineHolidayData'
    },
    [pscustomobject]@{
        SourcePath = 'holiday-data\README.md'
        PublishedPath = 'app\holiday-data\README.md'
        Category = 'OfflineHolidayData'
    },
    [pscustomobject]@{
        SourcePath = 'holiday-data\LICENSE.txt'
        PublishedPath = 'app\holiday-data\LICENSE.txt'
        Category = 'OfflineHolidayData'
    },
    [pscustomobject]@{
        SourcePath = 'countdown-icon.ico'
        PublishedPath = 'app\countdown-icon.ico'
        Category = 'ApplicationIcon'
    }
)

$script:floatingPlushAssets = @(
    'assets\floating-plush\floating-plush-mymelody-reference.png',
    'assets\floating-plush\floating-plush-cinnamoroll-reference.png',
    'assets\floating-plush\floating-plush-kuromi-reference.png',
    'assets\floating-plush\component-day-outer-shell.png',
    'assets\floating-plush\component-day-header-blank.png',
    'assets\floating-plush\component-day-card-blank.png',
    'assets\floating-plush\component-day-mascots.png'
)

$script:offlineHolidayDataFiles = @(
    'holiday-data\2026.json',
    'holiday-data\2027.json',
    'holiday-data\README.md',
    'holiday-data\LICENSE.txt'
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
        [Parameter(Mandatory)] [AllowEmptyCollection()] [string[]] $Paths
    )

    $seen = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($path in $Paths) {
        Assert-CountdownReleaseRelativePath -Path $path
        if (-not $seen.Add($path)) {
            throw "发布契约的 $Name 包含重复路径：$path"
        }
    }
}

# #218/#261 节假日年度覆盖守卫：纯函数，注入北京时间与随包覆盖年份集合。北京时间进入 12 月后，
# 随包数据必须已覆盖下一年；打包、真实安装和部署入口显式执行断言，module 导入与定义校验不执行。
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
        Message = "随包节假日数据尚未覆盖 $nextYear 年：当前北京时间已进入 $($BeijingNow.Year) 年 12 月。请按 holiday-data/README.md 的更新流程补齐 $nextYear 年随包数据（固定上游提交、重新生成全年 days 数组并更新来源提交与年份，工单 #234），再重新运行验证。"
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

function New-CountdownReleaseContractSnapshot {
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
    $publishedPayloadFiles = @(Get-CountdownPublishedPayload | ForEach-Object { $_.Files })
    $installedFiles = $applicationAssets + @($publishedPayloadFiles | ForEach-Object { $_.Path })
    if ($publishedPayloadFiles.Count -gt 0) {
        $installedFiles += 'app\published-payload.json'
    }
    $uniqueInstalled = [System.Collections.Generic.List[string]]::new()
    $seenInstalled = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($path in $installedFiles) {
        if ($seenInstalled.Add($path)) {
            $uniqueInstalled.Add($path)
        }
    }
    $installedFiles = @($uniqueInstalled)
    # 历史清理元数据只供回查；新安装器只替换程序目录，不执行一次性旧布局清理。
    $retiredRuntimeFiles = @(
        'Countdown.App.psm1',
        'Countdown.Core.psm1',
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
        'assets\floating-plush\component-day-mascots.png',
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
    # PackageFiles 与 InstalledFiles 都由实际载荷和固定文件类别派生；包内哈希由 Deployer
    # 在真实 ZIP 中生成 package-manifest.json，不在本 module 保存静态钉值。
    $packageFiles = @($script:packageInstallerFiles) + $installedFiles + @('package-manifest.json')
    $buildAssetCopies = @(
        foreach ($asset in $script:buildAssetDefinitions) {
            [pscustomobject]@{
                SourcePath = [string] $asset.SourcePath
                PublishedPath = [string] $asset.PublishedPath
                Category = [string] $asset.Category
            }
        }
    )

    [pscustomobject]@{
        ContractVersion = 1
        PackageFiles = [string[]] $packageFiles
        BuildAssets = [object[]] $buildAssetCopies
        InstalledFiles = [string[]] $installedFiles
        RetiredRuntimeFiles = [string[]] $retiredRuntimeFiles
        OfficialCharacterAssets = [string[]] $officialCharacterAssets
        FloatingPlushAssets = [string[]] @($script:floatingPlushAssets)
        CurrentFloatingRuntimeAssets = [string[]] $currentFloatingRuntimeAssets
        OfflineHolidayDataFiles = [string[]] @($script:offlineHolidayDataFiles)
    }
}

function Assert-CountdownReleaseContractDefinition {
    $contract = New-CountdownReleaseContractSnapshot
    Assert-CountdownUniqueReleasePaths -Name 'PackageFiles' -Paths $contract.PackageFiles
    Assert-CountdownUniqueReleasePaths -Name 'InstalledFiles' -Paths $contract.InstalledFiles
    Assert-CountdownUniqueReleasePaths -Name 'RetiredRuntimeFiles' -Paths $contract.RetiredRuntimeFiles
    Assert-CountdownUniqueReleasePaths -Name 'BuildAssets.SourcePath' -Paths @($contract.BuildAssets.SourcePath)
    Assert-CountdownUniqueReleasePaths -Name 'BuildAssets.PublishedPath' -Paths @($contract.BuildAssets.PublishedPath)

    $packageSet = [System.Collections.Generic.HashSet[string]]::new($contract.PackageFiles, [System.StringComparer]::OrdinalIgnoreCase)
    $installedSet = [System.Collections.Generic.HashSet[string]]::new($contract.InstalledFiles, [System.StringComparer]::OrdinalIgnoreCase)
    $retiredSet = [System.Collections.Generic.HashSet[string]]::new($contract.RetiredRuntimeFiles, [System.StringComparer]::OrdinalIgnoreCase)
    foreach ($path in $installedSet) {
        if (-not $packageSet.Contains($path)) { throw "安装运行时文件不在发布包内：$path" }
        if ($retiredSet.Contains($path)) { throw "安装运行时文件同时被标记为已淘汰：$path" }
    }
    foreach ($required in @($script:packageInstallerFiles) + @('package-manifest.json')) {
        if ($contract.InstalledFiles.Count -gt 0 -and -not $packageSet.Contains($required)) {
            throw "发布包缺少固定文件类别：$required"
        }
    }
    foreach ($asset in $contract.BuildAssets) {
        Assert-CountdownReleaseRelativePath -Path $asset.SourcePath
        Assert-CountdownReleaseRelativePath -Path $asset.PublishedPath
        if (-not $installedSet.Contains($asset.PublishedPath)) {
            throw "构建资产目标不属于安装运行时：$($asset.PublishedPath)"
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
}

function Get-CountdownReleaseContract {
    [CmdletBinding()]
    param()

    New-CountdownReleaseContractSnapshot
}

Assert-CountdownReleaseContractDefinition
Export-ModuleMember -Function Get-CountdownReleaseContract, Test-CountdownHolidayYearCoverage, Assert-CountdownHolidayYearCoverage
