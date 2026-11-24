#requires -Version 7.0

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$script:packageTrustRoots = @()

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
    },
    # #275 程序目录自足：随包节假日数据与图标随载荷进 app\，应用从程序目录读取。
    [pscustomobject]@{
        SourcePath = 'holiday-data\2026.json'
        PublishedPath = 'app\holiday-data\2026.json'
        SHA256 = '5B9887FB139FA190150BF0FC3E8EDB32A06647E884F1447633B305783ADACEBB'
        Category = 'OfflineHolidayData'
    },
    [pscustomobject]@{
        SourcePath = 'holiday-data\2027.json'
        PublishedPath = 'app\holiday-data\2027.json'
        SHA256 = '618646318BA54CACE19C20BB9DEB8BF8FF6179ABF963628A608D47AEFDCCF082'
        Category = 'OfflineHolidayData'
    },
    [pscustomobject]@{
        SourcePath = 'holiday-data\README.md'
        PublishedPath = 'app\holiday-data\README.md'
        SHA256 = '2775B80BC9DA69F281FED7A89404F18CA592D5299B1E6788CEF9C425CBD61B35'
        Category = 'OfflineHolidayData'
    },
    [pscustomobject]@{
        SourcePath = 'holiday-data\LICENSE.txt'
        PublishedPath = 'app\holiday-data\LICENSE.txt'
        SHA256 = 'C1614A521EFA0BD3FC0D271D01E38E9450C8BA3A13825044C325E433B4870142'
        Category = 'OfflineHolidayData'
    },
    [pscustomobject]@{
        SourcePath = 'countdown-icon.ico'
        PublishedPath = 'app\countdown-icon.ico'
        SHA256 = 'C5B7CB0A09524AEFE4488291078F61B0AF54958582DA98B99B25D0F4DC6976C5'
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

# 发布包只装办公室安装路径会加载或安装的文件，静态钉值也只覆盖这些文件。C# 源码、测试、构建与验证工具、
# DESIGN.md 留在仓库，不在这里钉死。
$script:nonAssetHashes = [ordered]@{
    'Install-CountdownProgram.ps1' = 'AD6384EAE3B754D75EC45C674C8A0921585E2851307B38D00E4D669A275ABE7A'
    'Countdown.Installer.psm1' = 'B599C4C49445D76C83DCE4B8F82A6697EF7A17D4304E4FDA0B96CD7B1AE1D11B'
}

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
        RetiredRuntimeFiles = [string[]] $retiredRuntimeFiles
        OfficialCharacterAssets = [string[]] $officialCharacterAssets
        FloatingPlushAssets = [string[]] @($script:floatingPlushAssets)
        CurrentFloatingRuntimeAssets = [string[]] $currentFloatingRuntimeAssets
        OfflineHolidayDataFiles = [string[]] @($script:offlineHolidayDataFiles)
    }
}

function Assert-CountdownReleaseContractDefinition {
    $contract = New-CountdownReleaseContractSnapshot
    Assert-CountdownUniqueReleasePaths -Name 'PackageTrustRoots' -Paths $contract.PackageTrustRoots
    Assert-CountdownUniqueReleasePaths -Name 'PackageFiles' -Paths $contract.PackageFiles
    Assert-CountdownUniqueReleasePaths -Name 'InstalledFiles' -Paths $contract.InstalledFiles
    Assert-CountdownUniqueReleasePaths -Name 'RetiredRuntimeFiles' -Paths $contract.RetiredRuntimeFiles
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
Export-ModuleMember -Function Get-CountdownReleaseContract, Assert-CountdownReleasePackage, Test-CountdownHolidayYearCoverage, Assert-CountdownHolidayYearCoverage
