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
        Source = Join-Path $PSScriptRoot 'assets\floating-plush\floating-plush-mymelody-front.png'
        RelativePath = 'floating-plush\floating-plush-mymelody-front.png'
    },
    [pscustomobject]@{
        Source = Join-Path $PSScriptRoot 'assets\floating-plush\floating-plush-mymelody-pose.png'
        RelativePath = 'floating-plush\floating-plush-mymelody-pose.png'
    },
    [pscustomobject]@{
        Source = Join-Path $PSScriptRoot 'assets\floating-plush\floating-plush-cinnamoroll-front.png'
        RelativePath = 'floating-plush\floating-plush-cinnamoroll-front.png'
    },
    [pscustomobject]@{
        Source = Join-Path $PSScriptRoot 'assets\floating-plush\floating-plush-cinnamoroll-pose.png'
        RelativePath = 'floating-plush\floating-plush-cinnamoroll-pose.png'
    },
    [pscustomobject]@{
        Source = Join-Path $PSScriptRoot 'assets\floating-plush\floating-plush-kuromi-front.png'
        RelativePath = 'floating-plush\floating-plush-kuromi-front.png'
    },
    [pscustomobject]@{
        Source = Join-Path $PSScriptRoot 'assets\floating-plush\floating-plush-kuromi-pose.png'
        RelativePath = 'floating-plush\floating-plush-kuromi-pose.png'
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

if ($Clean) {
    foreach ($output in @($applicationOutput, $verificationOutput)) {
        if (-not (Test-Path -LiteralPath $output -PathType Container)) { continue }
        $resolvedOutput = [System.IO.Path]::GetFullPath($output)
        $expectedOutputs = @(
            [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot 'app')),
            [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot 'verification'))
        )
        if ($resolvedOutput -notin $expectedOutputs) {
            throw "拒绝清理非受管发布目录：$resolvedOutput"
        }
        Remove-Item -LiteralPath $resolvedOutput -Recurse -Force
    }
}

& $dotnet publish $applicationProject -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
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
