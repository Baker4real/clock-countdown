#requires -Version 7.0
<#
.SYNOPSIS
由真实 Deployer 把一份已构建 app 生成完整安装 ZIP，并解压到本机包根。
.DESCRIPTION
输出目录必须是新路径；同名 package.zip 也不能已经存在。脚本不运行安装器、不注册任务、
不连接办公室。输出包根含两个安装器文件、应用载荷清单和根级 package-manifest.json，
可直接作为 Install-CountdownProgram.ps1 的包根或入口所在目录。
#>
[CmdletBinding()]
param(
    [string] $AppDirectory = (Join-Path $PSScriptRoot '..\app'),
    [Parameter(Mandatory)] [string] $OutputDirectory
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)

$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$appPath = [System.IO.Path]::GetFullPath($AppDirectory)
if (-not (Test-Path -LiteralPath $appPath -PathType Container)) { throw "构建输出目录不存在：$appPath" }
$packageRoot = [System.IO.Path]::TrimEndingDirectorySeparator([System.IO.Path]::GetFullPath($OutputDirectory))
$packagePath = "$packageRoot.zip"
foreach ($path in @($packageRoot, $packagePath)) {
    if (Test-Path -LiteralPath $path) { throw "输出路径已存在，换一个全新路径以免混入旧文件：$path" }
}
[void] [System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName($packagePath))

$workRoot = Join-Path ([System.IO.Path]::GetTempPath()) ('clock-countdown-local-package-' + [guid]::NewGuid().ToString('N'))
$sourceRoot = Join-Path $workRoot 'source'
$sourceApp = Join-Path $sourceRoot 'app'
try {
    [void] [System.IO.Directory]::CreateDirectory($sourceRoot)
    Copy-Item -LiteralPath $appPath -Destination $sourceApp -Recurse
    foreach ($name in @('Install-CountdownProgram.ps1', 'Countdown.Installer.psm1')) {
        [System.IO.File]::Copy((Join-Path $repoRoot $name), (Join-Path $sourceRoot $name))
    }
    $manifest = [System.IO.File]::ReadAllText((Join-Path $sourceApp 'published-payload.json')) | ConvertFrom-Json -DateKind String
    $commit = [string] $manifest.commit
    if ([string]::IsNullOrWhiteSpace($commit)) { throw '应用载荷清单没有源码提交标识。' }

    $deployerModule = Import-Module (Join-Path $repoRoot 'Countdown.Deployer.psm1') -Force -PassThru
    $package = & $deployerModule {
        param($Root, $Commit, $PackagePath)
        $buildOutput = Read-CountdownDeployBuildOutput -RepositoryRoot $Root -Commit $Commit
        New-CountdownDeployPackage -RepositoryRoot $Root -BuildOutput $buildOutput -PackagePath $PackagePath
    } $sourceRoot $commit $packagePath
    [System.IO.Compression.ZipFile]::ExtractToDirectory($packagePath, $packageRoot)

    # 只验证字节和精确闭包，不查询交互会话、不取安装锁、不触碰安装根。
    $installerModule = Import-Module (Join-Path $repoRoot 'Countdown.Installer.psm1') -Force -PassThru
    & $installerModule {
        param($Root)
        $payload = Read-CountdownPayloadManifest -ProgramDirectory (Join-Path $Root 'app')
        $packageManifest = Read-CountdownPackageManifest -PackageRoot $Root
        Assert-CountdownPackageMatchesManifest -PackageRoot $Root -Manifest $packageManifest -PayloadManifest $payload
    } $packageRoot

    [ordered]@{
        packageRoot = $packageRoot
        packageZip = $packagePath
        sha256 = [string] $package.Sha256
        manifestSha256 = [string] $package.ManifestSha256
    } | ConvertTo-Json -Compress
}
finally {
    if (Test-Path -LiteralPath $workRoot -PathType Container) {
        [System.IO.Directory]::Delete($workRoot, $true)
    }
}
