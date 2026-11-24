#requires -Version 7.0
<#
.SYNOPSIS
由真实 Deployer 生成并解包当前构建输出，再运行包内安装器的 CheckOnly。
.DESCRIPTION
InstallValidate 与 Core 的安装器验证共用这个无窗口包装入口。仓库源码树不是发布包，
不能用来验证全包精确闭包。CheckOnly 按现役契约豁免节假日年度覆盖，因此打包夹具使用
非 December 的守卫输入；正式部署仍由 Deploy-CountdownProgram.ps1 执行真实年度守卫。
#>
[CmdletBinding()]
param(
    [string] $RepositoryRoot = (Join-Path $PSScriptRoot '..'),
    [string] $InstallRoot = 'D:\敏敏子倒计时',
    [string] $TaskName = 'Minmin Countdown',
    [string] $LockName
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)

$RepositoryRoot = (Resolve-Path -LiteralPath $RepositoryRoot).Path
$payloadManifestPath = Join-Path $RepositoryRoot 'app\published-payload.json'
$payloadManifest = [System.IO.File]::ReadAllText($payloadManifestPath) | ConvertFrom-Json -DateKind String
$commit = [string] $payloadManifest.commit
if ([string]::IsNullOrWhiteSpace($commit)) { throw '载荷清单没有源码提交标识。' }

$deployerModule = Import-Module (Join-Path $RepositoryRoot 'Countdown.Deployer.psm1') -Force -PassThru
$workRoot = Join-Path ([System.IO.Path]::GetTempPath()) ('clock-countdown-install-package-' + [guid]::NewGuid().ToString('N'))
$packagePath = Join-Path $workRoot 'package.zip'
$packageRoot = Join-Path $workRoot 'package'
$outputText = ''
$installerExitCode = 1
try {
    [void] [System.IO.Directory]::CreateDirectory($workRoot)
    [void] (& $deployerModule {
            param($Root, $Commit, $PackagePath)
            $buildOutput = Read-CountdownDeployBuildOutput -RepositoryRoot $Root -Commit $Commit
            New-CountdownDeployPackage -RepositoryRoot $Root -BuildOutput $buildOutput -PackagePath $PackagePath `
                -BeijingNow ([DateTimeOffset]'2026-11-30T12:00:00+08:00')
        } $RepositoryRoot $commit $packagePath)
    [System.IO.Compression.ZipFile]::ExtractToDirectory($packagePath, $packageRoot)

    $engine = (Get-Command pwsh.exe -CommandType Application -ErrorAction Stop | Select-Object -First 1).Source
    $arguments = @{
        PackageRoot = $packageRoot
        InstallRoot = $InstallRoot
        TaskName = $TaskName
        CheckOnly = $true
    }
    if (-not [string]::IsNullOrWhiteSpace($LockName)) { $arguments.LockName = $LockName }
    $installerOutput = @(& $engine -NoLogo -NoProfile -NonInteractive `
            -File (Join-Path $packageRoot 'Install-CountdownProgram.ps1') @arguments)
    $installerExitCode = $LASTEXITCODE
    $outputText = ($installerOutput | Out-String).TrimEnd()
}
finally {
    if (Test-Path -LiteralPath $workRoot -PathType Container) {
        [System.IO.Directory]::Delete($workRoot, $true)
    }
}

Write-Output $outputText
exit $installerExitCode
