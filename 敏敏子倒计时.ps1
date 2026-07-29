#requires -Version 7.0

[CmdletBinding()]
param(
    [switch] $ValidateOnly,
    [switch] $ExerciseNoteAdd,
    [switch] $ExerciseThemePreference,
    [switch] $ExercisePersistenceFailures,
    [switch] $ExerciseTagInteractions,
    [switch] $ExerciseFatalDiagnostic,
    [switch] $ExitAfterReadyForTest,
    [string] $SettingsFile,
    [string] $DiagnosticLogPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false

$applicationPath = [System.IO.Path]::GetFullPath(
    (Join-Path $PSScriptRoot 'app\MinminCountdown.exe')
)
if (-not (Test-Path -LiteralPath $applicationPath -PathType Leaf)) {
    throw [System.IO.FileNotFoundException]::new(
        "未找到 C# 应用宿主：$applicationPath。入口不允许回退到旧 PowerShell WPF。",
        $applicationPath
    )
}

$applicationArguments = [System.Collections.Generic.List[string]]::new()
if ($ValidateOnly.IsPresent) {
    $applicationArguments.Add('--validate-only')
}
else {
    # 生产 adapter 启动必须绑定安装器写出的请求；缺失或损坏时由 C# 宿主 fail-closed。
    $applicationArguments.Add('--require-startup-request')
}
if ($ExerciseNoteAdd.IsPresent) {
    $applicationArguments.Add('--exercise-note-add')
}
if ($ExerciseThemePreference.IsPresent) {
    $applicationArguments.Add('--exercise-theme-preference')
}
if ($ExercisePersistenceFailures.IsPresent) {
    $applicationArguments.Add('--exercise-persistence-failures')
}
if ($ExerciseTagInteractions.IsPresent) {
    $applicationArguments.Add('--exercise-tag-interactions')
}
if ($ExerciseFatalDiagnostic.IsPresent) {
    $applicationArguments.Add('--exercise-fatal-diagnostic')
}
if ($ExitAfterReadyForTest.IsPresent) {
    $applicationArguments.Add('--exit-after-ready-for-test')
}
if ($PSBoundParameters.ContainsKey('SettingsFile')) {
    $applicationArguments.Add('--settings-file')
    $applicationArguments.Add($SettingsFile)
}
if ($PSBoundParameters.ContainsKey('DiagnosticLogPath')) {
    $applicationArguments.Add('--diagnostic-log-path')
    $applicationArguments.Add($DiagnosticLogPath)
}

$startInfo = [System.Diagnostics.ProcessStartInfo]::new()
$startInfo.FileName = $applicationPath
$startInfo.UseShellExecute = $false
$startInfo.WorkingDirectory = $PSScriptRoot
$startInfo.RedirectStandardOutput = $false
$startInfo.RedirectStandardError = $false
foreach ($argument in $applicationArguments) {
    $startInfo.ArgumentList.Add($argument)
}

$process = [System.Diagnostics.Process]::new()
try {
    $process.StartInfo = $startInfo
    if (-not $process.Start()) {
        throw "无法启动 C# 应用宿主：$applicationPath"
    }

    $process.WaitForExit()
    $exitCode = $process.ExitCode
}
finally {
    $process.Dispose()
}

exit $exitCode
