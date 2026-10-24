#requires -Version 7.0

[CmdletBinding()]
param(
    [switch] $ValidateOnly,
    [switch] $DeferCommit,
    [string] $DeferredReceiptPath,
    [switch] $ParentTransactionMutexHeld,
    [string] $ExpectedInteractiveUser,
    [int] $ExpectedInteractiveSessionId,
    [string] $ExpectedDesktopPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$staging = $PSScriptRoot
Import-Module (Join-Path $staging 'Countdown.ReleaseContract.psm1') -Force
$releaseContract = Get-CountdownReleaseContract
$officialCharacterAssets = @($releaseContract.OfficialCharacterAssets)
$floatingPlushAssets = @($releaseContract.FloatingPlushAssets)
$currentFloatingRuntimeAssets = @($releaseContract.CurrentFloatingRuntimeAssets)
$legacyFloatingRegressionAssets = @($releaseContract.LegacyFloatingRegressionAssets)
$retiredRuntimeFiles = @($releaseContract.RetiredRuntimeFiles)
$packageValidation = Assert-CountdownReleasePackage -Root $staging

if ($ValidateOnly) {
    [pscustomobject]@{
        Valid = $true
        SourceRoot = $staging
        VerifiedFiles = $packageValidation.VerifiedFiles
        OfficialCharacterAssets = $officialCharacterAssets
        FloatingPlushAssets = $floatingPlushAssets
        CurrentFloatingRuntimeAssets = $currentFloatingRuntimeAssets
        LegacyFloatingRegressionAssets = $legacyFloatingRegressionAssets
        CompatibilityFixtureFiles = @($releaseContract.CompatibilityFixtureFiles)
        RetiredRuntimeFiles = $retiredRuntimeFiles
        Transactional = $true
        DeferredCommitSupported = $true
    } | ConvertTo-Json
    exit 0
}

if ($DeferCommit -and ((-not $ParentTransactionMutexHeld) -or [string]::IsNullOrWhiteSpace($DeferredReceiptPath))) {
    throw '延迟提交只能由已持有父安装事务锁的验收方调用，并且必须提供独立回执路径。'
}
if ($ParentTransactionMutexHeld -and -not $DeferCommit) {
    throw '父安装事务锁上下文只允许与 -DeferCommit 一起使用。'
}

Import-Module (Join-Path $staging 'Countdown.InstallTransaction.psm1') -Force -DisableNameChecking
$installResolved = (Get-CountdownInstallLayout).InstallPath
if (-not $installResolved.StartsWith('D:\', [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Install path is outside D drive: $installResolved"
}
if ((Test-Path -LiteralPath $installResolved) -and -not (Test-Path -LiteralPath $installResolved -PathType Container)) {
    throw "Install path is not a directory: $installResolved"
}

$powerShellEngine = (Get-Command pwsh.exe -CommandType Application -ErrorAction Stop | Select-Object -First 1).Source
if ([string]::IsNullOrWhiteSpace($powerShellEngine)) {
    throw '未找到 pwsh.exe；本项目要求 PowerShell 7+，不支持 Windows PowerShell 5.1。'
}
# 办公室延迟提交（-DeferCommit）只安装：验收套件已在发起端本机跑完，部署器上传前核对同一提交的
# 本机套件证据，远端不运行任何测试，也就不需要受管 dotnet。本机直接安装仍先跑完整验收套件。
$dotnetPath = 'D:\Codex\tools\dotnet\10.0\dotnet.exe'
if (-not $DeferCommit -and -not (Test-Path -LiteralPath $dotnetPath -PathType Leaf)) {
    throw "未找到验收套件所需的受管 dotnet：$dotnetPath"
}
$powerShellProcessName = [System.IO.Path]::GetFileName($powerShellEngine)
Get-Service -Name W32Time -ErrorAction Stop | Out-Null

$interactiveContext = Get-CountdownInteractiveDesktopContext
$contextExpectationProvided = $PSBoundParameters.ContainsKey('ExpectedInteractiveUser') -or $PSBoundParameters.ContainsKey('ExpectedInteractiveSessionId') -or $PSBoundParameters.ContainsKey('ExpectedDesktopPath')
if ($contextExpectationProvided) {
    if (-not ($PSBoundParameters.ContainsKey('ExpectedInteractiveUser') -and $PSBoundParameters.ContainsKey('ExpectedInteractiveSessionId') -and $PSBoundParameters.ContainsKey('ExpectedDesktopPath'))) {
        throw '办公室部署传入的交互上下文必须同时包含用户、会话和桌面路径。'
    }
    $expectedDesktop = Assert-CountdownInstallPathOnDDrive -Path $ExpectedDesktopPath
    if (-not [string]::Equals([string] $interactiveContext.User, $ExpectedInteractiveUser, [System.StringComparison]::OrdinalIgnoreCase) -or
        [int] $interactiveContext.SessionId -ne $ExpectedInteractiveSessionId -or
        -not [string]::Equals([string] $interactiveContext.Desktop, $expectedDesktop, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw '办公室部署开始安装前交互用户、会话或桌面路径已变化；未创建安装事务。'
    }
}

$validationSuite = $null
if (-not $DeferCommit) {
    # 验证 module 只在本机直接安装时从仓库加载，不随办公室发布包分发。
    Import-Module (Join-Path $staging 'Countdown.Validation.psm1') -Force
    $validationSuite = Invoke-CountdownValidationSuite `
        -Root $staging `
        -PowerShell7Path $powerShellEngine `
        -DotnetPath $dotnetPath
}

# Begin、Accept 与 Rollback 都在安装事务生命周期 module 内；本脚本只做预检与输出。
Import-Module (Join-Path $staging 'Countdown.InstallLifecycle.psm1') -Force
$installationArguments = @{
    Layout = Get-CountdownInstallLayout -Desktop ([string] $interactiveContext.Desktop)
    StagingRoot = $staging
    ReleaseContract = $releaseContract
    PowerShell7Path = $powerShellEngine
    InteractiveContext = $interactiveContext
    InventoryEvidenceRoot = 'D:\agents\run-evidence\countdown\install-inventory'
}
if ($DeferCommit) {
    $installationArguments.DeferredReceiptPath = $DeferredReceiptPath
}
$installation = Invoke-CountdownInstallation @installationArguments

$result = [ordered]@{}
if ($null -ne $validationSuite) {
    $result.TestOutput = ([string] $validationSuite.TestOutput).Trim()
    $result.CSharpTestOutput = ([string] $validationSuite.CSharpTestOutput).Trim()
    $result.UiTestOutput = ([string] $validationSuite.UiTestOutput).Trim()
    $result.UiValidationOutput = ([string] $validationSuite.UiValidationOutput).Trim()
    $result.Validation = [ordered]@{
        SchemaVersion = [int] $validationSuite.SchemaVersion
        Valid = [bool] $validationSuite.Valid
        CoreTestsPassed = [int] $validationSuite.CoreTestsPassed
        CSharpTestsPassed = [int] $validationSuite.CSharpTestsPassed
        UiTestsPassed = [int] $validationSuite.UiTestsPassed
        UiValid = [bool] $validationSuite.UiValid
        AdapterUiValidated = [bool] $validationSuite.AdapterUiValidated
    }
}
foreach ($field in $installation.GetEnumerator()) {
    $result[$field.Key] = $field.Value
}
$result | ConvertTo-Json -Depth 8
