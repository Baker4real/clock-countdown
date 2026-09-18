#requires -Version 7.0

[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release',
    [string] $DotnetPath = 'D:\Codex\tools\dotnet\10.0\dotnet.exe'
)

$ErrorActionPreference = 'Stop'

Import-Module -Name (Join-Path $PSScriptRoot 'tools\Countdown.SuiteIsolation.psm1') -Force
Assert-SuiteIsolatedDesktop

$applicationDll = Join-Path $PSScriptRoot "src\MinminCountdown\bin\$Configuration\net10.0-windows\MinminCountdown.dll"
if (-not (Test-Path -LiteralPath $DotnetPath -PathType Leaf)) {
    throw "未找到受管 dotnet：$DotnetPath"
}
if (-not (Test-Path -LiteralPath $applicationDll -PathType Leaf)) {
    throw "未找到源码应用 DLL：$applicationDll。请先构建 MinminCountdown.csproj。"
}

$temporaryRoot = 'D:\Codex\.codex\tmp\clock-countdown-source-validation'
[System.IO.Directory]::CreateDirectory($temporaryRoot) | Out-Null
$runId = [guid]::NewGuid().ToString('N')
$settingsPath = Join-Path $temporaryRoot "$runId.settings.json"
$diagnosticPath = Join-Path $temporaryRoot "$runId.application.jsonl"
$rootSettingsPath = Join-Path $PSScriptRoot 'settings.json'

function Get-RootSettingsSnapshot {
    if (-not (Test-Path -LiteralPath $rootSettingsPath -PathType Leaf)) {
        return $null
    }

    $item = Get-Item -LiteralPath $rootSettingsPath
    [pscustomobject]@{
        Length = $item.Length
        LastWriteTimeUtc = $item.LastWriteTimeUtc
        Sha256 = (Get-FileHash -LiteralPath $rootSettingsPath -Algorithm SHA256).Hash
    }
}

function Assert-RootSettingsUnchanged {
    param([AllowNull()] [object] $Before)

    $after = Get-RootSettingsSnapshot
    if (($null -eq $Before) -ne ($null -eq $after) -or
        ($null -ne $Before -and (
            $Before.Length -ne $after.Length -or
            $Before.LastWriteTimeUtc -ne $after.LastWriteTimeUtc -or
            $Before.Sha256 -ne $after.Sha256))) {
        throw '源码 validate-only 改变了权威根 settings.json，验证已闭锁。'
    }
}

$rootSettingsBefore = Get-RootSettingsSnapshot
$validationOutput = $null
$validationExitCode = $null
try {
    $validationOutput = & $DotnetPath $applicationDll `
        '--validate-only' `
        '--settings-file' $settingsPath `
        '--diagnostic-log-path' $diagnosticPath | Out-String
    $validationExitCode = $LASTEXITCODE
    if ($validationExitCode -ne 0) {
        throw "源码 validate-only 失败（exit=$validationExitCode）：$validationOutput"
    }

    $report = $validationOutput | ConvertFrom-Json
    if (-not [bool] $report.Valid) {
        throw '源码 validate-only 报告未通过。'
    }
    if ($report.PSObject.Properties['CalendarReminderBehavior']?.Value -isnot [bool] -or
        -not [bool] $report.CalendarReminderBehavior) {
        throw '源码 validate-only 日历提醒行为未通过。'
    }

    $validationOutput.Trim()
}
finally {
    foreach ($path in @(
            $settingsPath,
            "$settingsPath.tmp",
            "$settingsPath.backup1",
            "$settingsPath.backup2",
            "$settingsPath.backup2.tmp",
            "$settingsPath.backup2.rollback",
            $diagnosticPath,
            "$diagnosticPath.1",
            "$diagnosticPath.2",
            "$diagnosticPath.3"
        )) {
        if (Test-Path -LiteralPath $path -PathType Leaf) {
            Remove-Item -LiteralPath $path -Force
        }
    }
    if (Test-Path -LiteralPath "$settingsPath.tmp" -PathType Container) {
        [System.IO.Directory]::Delete("$settingsPath.tmp", $false)
    }
    Assert-RootSettingsUnchanged -Before $rootSettingsBefore
}
