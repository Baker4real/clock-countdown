#requires -Version 7.0

[CmdletBinding()]
param(
    [switch] $Clean
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$dotnetRoot = 'D:\Codex\tools\dotnet\10.0'
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
Import-Module (Join-Path $PSScriptRoot 'Countdown.ReleaseContract.psm1') -Force
$releaseContract = Get-CountdownReleaseContract
$assetManifest = @(
    foreach ($asset in $releaseContract.BuildAssets) {
        $source = Join-Path $PSScriptRoot $asset.SourcePath
        $target = Join-Path $PSScriptRoot $asset.PublishedPath
        [pscustomobject]@{
            Source = $source
            Target = $target
            RelativePath = [System.IO.Path]::GetRelativePath($applicationAssetsOutput, $target)
            SHA256 = [string] $asset.SHA256
        }
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
$applicationProjectDirectory = Split-Path -Parent $applicationProject
$projectAssetMappings = @(
    $applicationProjectXml.SelectNodes('/Project/ItemGroup/Content') |
        ForEach-Object {
            $sourcePath = [System.IO.Path]::GetFullPath((Join-Path $applicationProjectDirectory ([string] $_.Include)))
            $sourceRelativePath = [System.IO.Path]::GetRelativePath($PSScriptRoot, $sourcePath)
            $publishedRelativePath = Join-Path 'app' ([string] $_.Link)
            "$sourceRelativePath=>$publishedRelativePath"
        } |
        Sort-Object
)
$contractAssetMappings = @(
    $releaseContract.BuildAssets |
        ForEach-Object { "$($_.SourcePath)=>$($_.PublishedPath)" } |
        Sort-Object
)
if (($projectAssetMappings -join ',') -ne ($contractAssetMappings -join ',')) {
    throw 'C# 项目必须显式打包发布契约声明的十四个应用资产，且不得使用通配。'
}

if ($Clean) {
    $safeTreeDelete = 'D:\Codex\.codex\scripts\invoke-safe-tree-delete.cmd'
    $runEvidenceCapabilityRoot = 'D:\Codex\.codex\run-evidence'
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
    $runEvidenceRootPrefix = [System.IO.Path]::GetFullPath($runEvidenceCapabilityRoot).TrimEnd('\') + '\'
    foreach ($cleanPath in $managedCleanCandidates) {
        if (-not (Test-Path -LiteralPath $cleanPath -PathType Container)) { continue }
        $resolvedCleanPath = [System.IO.Path]::GetFullPath($cleanPath)
        if (-not $resolvedCleanPath.StartsWith($repositoryRootPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "拒绝清理仓库外构建目录：$resolvedCleanPath"
        }
        if ($resolvedCleanPath -notin $expectedCleanPaths) {
            throw "拒绝清理非受管构建目录：$resolvedCleanPath"
        }
        $cleanupCapabilityRoot = if ($resolvedCleanPath.StartsWith($runEvidenceRootPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
            $runEvidenceCapabilityRoot
        }
        else {
            $resolvedCleanPath
        }
        $cleanupJson = & $safeTreeDelete `
            -Mode BatchPurge `
            -TargetPaths $resolvedCleanPath `
            -CapabilityRoot $cleanupCapabilityRoot `
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

# Keep a framework-dependent source DLL beside the project output for the
# source validate-only contract. The self-contained publish remains the
# installable application artifact above.
& $dotnet build $applicationProject -c Release --no-restore
if ($LASTEXITCODE -ne 0) {
    throw "源码 DLL 构建失败，退出码：$LASTEXITCODE"
}
$sourceApplicationDll = Join-Path $PSScriptRoot 'src\MinminCountdown\bin\Release\net10.0-windows\MinminCountdown.dll'
if (-not (Test-Path -LiteralPath $sourceApplicationDll -PathType Leaf)) {
    throw "源码构建未生成 validate-only DLL：$sourceApplicationDll"
}

foreach ($asset in $assetManifest) {
    $target = $asset.Target
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
    $target = $asset.Target
    if (-not (Test-Path -LiteralPath $target -PathType Leaf)) {
        throw "发布缺少资产：$target"
    }
    $sourceHash = (Get-FileHash -LiteralPath $asset.Source -Algorithm SHA256).Hash
    $targetHash = (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash
    if ($sourceHash -ne $asset.SHA256) {
        throw "发布源资产不符合发布契约：$($asset.Source)"
    }
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
