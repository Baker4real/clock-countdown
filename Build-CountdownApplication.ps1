#requires -Version 7.0

[CmdletBinding()]
param(
    [switch] $Clean
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$dotnetRoot = 'D:\Codex\tools\dotnet\10'
$dotnet = Join-Path $dotnetRoot 'dotnet.exe'
if (-not (Test-Path -LiteralPath $dotnet -PathType Leaf)) {
    throw "未找到受管 .NET SDK：$dotnet"
}

$env:DOTNET_ROOT = $dotnetRoot
$env:DOTNET_CLI_HOME = 'D:\Codex\.dotnet-cli'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$coreProject = Join-Path $PSScriptRoot 'src\MinminCountdown.Core\MinminCountdown.Core.csproj'
$applicationProject = Join-Path $PSScriptRoot 'src\MinminCountdown\MinminCountdown.csproj'
$verificationProject = Join-Path $PSScriptRoot 'src\MinminCountdown.Verification\MinminCountdown.Verification.csproj'
$applicationOutput = Join-Path $PSScriptRoot 'app'
$verificationOutput = Join-Path $PSScriptRoot 'verification'
$applicationAssetsOutput = Join-Path $applicationOutput 'assets'
$assetManifest = @(
    [pscustomobject]@{
        Source = Join-Path $PSScriptRoot 'sanrio-official-mymelody.png'
        RelativePath = 'sanrio-official-mymelody.png'
    },
    [pscustomobject]@{
        Source = Join-Path $PSScriptRoot 'sanrio-official-cinnamoroll.png'
        RelativePath = 'sanrio-official-cinnamoroll.png'
    },
    [pscustomobject]@{
        Source = Join-Path $PSScriptRoot 'sanrio-official-kuromi.png'
        RelativePath = 'sanrio-official-kuromi.png'
    },
    [pscustomobject]@{
        Source = Join-Path $PSScriptRoot 'assets\floating-plush\floating-plush-mymelody-reference.png'
        RelativePath = 'floating-plush\floating-plush-mymelody-reference.png'
    },
    [pscustomobject]@{
        Source = Join-Path $PSScriptRoot 'assets\floating-plush\floating-plush-cinnamoroll-reference.png'
        RelativePath = 'floating-plush\floating-plush-cinnamoroll-reference.png'
    },
    [pscustomobject]@{
        Source = Join-Path $PSScriptRoot 'assets\floating-plush\floating-plush-kuromi-reference.png'
        RelativePath = 'floating-plush\floating-plush-kuromi-reference.png'
    },
    [pscustomobject]@{
        Source = Join-Path $PSScriptRoot 'assets\floating-plush\component-day-outer-shell.png'
        RelativePath = 'floating-plush\component-day-outer-shell.png'
    },
    [pscustomobject]@{
        Source = Join-Path $PSScriptRoot 'assets\floating-plush\component-day-header-blank.png'
        RelativePath = 'floating-plush\component-day-header-blank.png'
    },
    [pscustomobject]@{
        Source = Join-Path $PSScriptRoot 'assets\floating-plush\component-day-card-blank.png'
        RelativePath = 'floating-plush\component-day-card-blank.png'
    },
    [pscustomobject]@{
        Source = Join-Path $PSScriptRoot 'assets\floating-plush\component-day-mascots.png'
        RelativePath = 'floating-plush\component-day-mascots.png'
    }
    [pscustomobject]@{
        Source = Join-Path $PSScriptRoot 'assets\main-decorations\main-countdown-scene-day.png'
        RelativePath = 'main-decorations\main-countdown-scene-day.png'
    }
    [pscustomobject]@{
        Source = Join-Path $PSScriptRoot 'assets\main-decorations\main-notes-scene-day.png'
        RelativePath = 'main-decorations\main-notes-scene-day.png'
    }
    [pscustomobject]@{
        Source = Join-Path $PSScriptRoot 'assets\main-decorations\main-countdown-top-night.png'
        RelativePath = 'main-decorations\main-countdown-top-night.png'
    }
    [pscustomobject]@{
        Source = Join-Path $PSScriptRoot 'assets\main-decorations\main-notes-top-night.png'
        RelativePath = 'main-decorations\main-notes-top-night.png'
    }
)

if (-not (Test-Path -LiteralPath $applicationProject -PathType Leaf)) {
    throw "未找到 C# WPF 项目：$applicationProject"
}
if (-not (Test-Path -LiteralPath $verificationProject -PathType Leaf)) {
    throw "未找到 C# 验证项目：$verificationProject"
}
foreach ($asset in $assetManifest) {
    if (-not (Test-Path -LiteralPath $asset.Source -PathType Leaf)) {
        throw "未找到发布资产：$($asset.Source)"
    }
}
$applicationProjectXml = [xml] (Get-Content -Raw -LiteralPath $applicationProject)
$projectFloatingAssetFiles = @(
    $applicationProjectXml.SelectNodes('/Project/ItemGroup/Content') |
        Where-Object { [string] $_.Include -like '*assets\floating-plush*' } |
        ForEach-Object { [System.IO.Path]::GetFileName([string] $_.Include) }
)
$manifestFloatingAssetFiles = @(
    $assetManifest |
        Where-Object { $_.RelativePath -like 'floating-plush\*' } |
        ForEach-Object { Split-Path -Leaf $_.RelativePath }
)
if (($projectFloatingAssetFiles -join ',') -ne ($manifestFloatingAssetFiles -join ',')) {
    throw 'C# 项目必须显式打包七个现役悬浮窗资产，且不得使用通配。'
}

if ($Clean) {
    $safeTreeDelete = 'D:\Codex\.codex\scripts\invoke-safe-tree-delete.cmd'
    if (-not (Test-Path -LiteralPath $safeTreeDelete -PathType Leaf)) {
        throw "未找到受管构建目录清理入口：$safeTreeDelete"
    }
    $expectedCleanPaths = @(
        [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot 'app')),
        [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot 'verification')),
        [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot 'src\MinminCountdown.Core\bin')),
        [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot 'src\MinminCountdown.Core\obj')),
        [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot 'src\MinminCountdown\bin')),
        [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot 'src\MinminCountdown\obj')),
        [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot 'src\MinminCountdown.Verification\bin')),
        [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot 'src\MinminCountdown.Verification\obj'))
    )
    $projectDirectories = @($coreProject, $applicationProject, $verificationProject) |
        ForEach-Object { Split-Path -Parent $_ }
    $managedCleanCandidates = @($applicationOutput, $verificationOutput)
    foreach ($projectDirectory in $projectDirectories) {
        $managedCleanCandidates += Join-Path $projectDirectory 'bin'
        $managedCleanCandidates += Join-Path $projectDirectory 'obj'
    }
    $repositoryRootPrefix = [System.IO.Path]::GetFullPath($PSScriptRoot).TrimEnd('\') + '\'
    foreach ($cleanPath in $managedCleanCandidates) {
        if (-not (Test-Path -LiteralPath $cleanPath -PathType Container)) { continue }
        $resolvedCleanPath = [System.IO.Path]::GetFullPath($cleanPath)
        if (-not $resolvedCleanPath.StartsWith($repositoryRootPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "拒绝清理仓库外构建目录：$resolvedCleanPath"
        }
        if ($resolvedCleanPath -notin $expectedCleanPaths) {
            throw "拒绝清理非受管构建目录：$resolvedCleanPath"
        }
        $cleanupJson = & $safeTreeDelete `
            -Mode BatchPurge `
            -TargetPaths $resolvedCleanPath `
            -CapabilityRoot $resolvedCleanPath `
            -ConfirmApply `
            -ConfirmPurge `
            -Json | Out-String
        if ($LASTEXITCODE -ne 0) {
            throw "受管构建目录清理失败：$resolvedCleanPath"
        }
        $cleanup = $cleanupJson | ConvertFrom-Json
        if (-not $cleanup.completed -or $cleanup.purgedCount -ne 1) {
            throw "受管构建目录未完成清理：$resolvedCleanPath"
        }
    }
}

& $dotnet publish $applicationProject -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:IncludeSourceRevisionInInformationalVersion=false `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -o $applicationOutput
if ($LASTEXITCODE -ne 0) {
    throw "C# WPF 发布失败，退出码：$LASTEXITCODE"
}

$application = Join-Path $applicationOutput 'MinminCountdown.exe'
if (-not (Test-Path -LiteralPath $application -PathType Leaf)) {
    throw "发布未生成应用程序：$application"
}

foreach ($asset in $assetManifest) {
    $target = Join-Path $applicationAssetsOutput $asset.RelativePath
    $targetDirectory = Split-Path -Parent $target
    if (-not (Test-Path -LiteralPath $targetDirectory -PathType Container)) {
        New-Item -ItemType Directory -Path $targetDirectory -Force | Out-Null
    }
    Copy-Item -LiteralPath $asset.Source -Destination $target -Force
}

$expectedAssetPaths = [System.Collections.Generic.HashSet[string]]::new(
    [System.StringComparer]::OrdinalIgnoreCase
)
foreach ($asset in $assetManifest) {
    [void] $expectedAssetPaths.Add($asset.RelativePath)
}

$publishedAssets = @(
    Get-ChildItem -LiteralPath $applicationAssetsOutput -File -Recurse
)
if ($publishedAssets.Count -ne $assetManifest.Count) {
    throw "发布资产数量无效：期望 $($assetManifest.Count)，实际 $($publishedAssets.Count)。"
}

$assetResults = foreach ($asset in $assetManifest) {
    $target = Join-Path $applicationAssetsOutput $asset.RelativePath
    if (-not (Test-Path -LiteralPath $target -PathType Leaf)) {
        throw "发布缺少资产：$target"
    }
    $sourceHash = (Get-FileHash -LiteralPath $asset.Source -Algorithm SHA256).Hash
    $targetHash = (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash
    if ($sourceHash -ne $targetHash) {
        throw "发布资产校验失败：$target"
    }
    $targetItem = Get-Item -LiteralPath $target
    [pscustomobject]@{
        RelativePath = $asset.RelativePath
        Length = $targetItem.Length
        SHA256 = $targetHash
    }
}

foreach ($publishedAsset in $publishedAssets) {
    $relativePath = [System.IO.Path]::GetRelativePath(
        $applicationAssetsOutput,
        $publishedAsset.FullName
    )
    if (-not $expectedAssetPaths.Contains($relativePath)) {
        throw "发布包含未声明资产：$relativePath"
    }
}

$sourcesMetadata = Join-Path $applicationAssetsOutput 'floating-plush\SOURCES.md'
if (Test-Path -LiteralPath $sourcesMetadata) {
    throw "发布不得包含资产来源说明：$sourcesMetadata"
}

& $dotnet publish $verificationProject -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:IncludeSourceRevisionInInformationalVersion=false `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -o $verificationOutput
if ($LASTEXITCODE -ne 0) {
    throw "C# 验证程序发布失败，退出码：$LASTEXITCODE"
}

$verification = Join-Path $verificationOutput 'MinminCountdown.Verification.exe'
if (-not (Test-Path -LiteralPath $verification -PathType Leaf)) {
    throw "发布未生成验证程序：$verification"
}

Get-Item -LiteralPath $application, $verification | Select-Object FullName, Length, LastWriteTime
$assetResults
