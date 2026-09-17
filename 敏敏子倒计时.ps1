#requires -Version 7.0

[CmdletBinding()]
param(
    [switch] $ValidateOnly,
    [switch] $ExerciseNoteAdd,
    [switch] $ExercisePersistenceFailures,
    [switch] $ExerciseTagInteractions,
    [switch] $ExerciseFatalDiagnostic,
    [switch] $ExitAfterReadyForTest,
    [int] $ExitAfterReadyForTestSeconds,
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
if ($PSBoundParameters.ContainsKey('ExitAfterReadyForTestSeconds')) {
    if (-not $ExitAfterReadyForTest.IsPresent) {
        throw '-ExitAfterReadyForTestSeconds 只能与 -ExitAfterReadyForTest 一起使用。'
    }
    $applicationArguments.Add('--exit-after-ready-seconds')
    $applicationArguments.Add([string] $ExitAfterReadyForTestSeconds)
}
if ($PSBoundParameters.ContainsKey('SettingsFile')) {
    $applicationArguments.Add('--settings-file')
    $applicationArguments.Add($SettingsFile)
}
if ($PSBoundParameters.ContainsKey('DiagnosticLogPath')) {
    $applicationArguments.Add('--diagnostic-log-path')
    $applicationArguments.Add($DiagnosticLogPath)
}

$stayAttached = $ValidateOnly.IsPresent -or
    $ExerciseNoteAdd.IsPresent -or
    $ExercisePersistenceFailures.IsPresent -or
    $ExerciseTagInteractions.IsPresent -or
    $ExerciseFatalDiagnostic.IsPresent -or
    $ExitAfterReadyForTest.IsPresent

function ConvertTo-Win32ProcessCommandLine {
    param([Parameter(Mandatory)] [string] $Value)

    if ($Value -notmatch '[\s"]') {
        return $Value
    }

    '"' + ($Value -replace '\\', '\\' -replace '"', '\"') + '"'
}

$process = $null
try {
    if ($stayAttached) {
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
        $process.StartInfo = $startInfo
        if (-not $process.Start()) {
            throw "无法启动 C# 应用宿主：$applicationPath"
        }

        $process.WaitForExit()
        $exitCode = $process.ExitCode
    }
    else {
        # Win32_Process.Create 不会加入计划任务作业，adapter 退出后任务可回 Ready。
        $commandLine = ConvertTo-Win32ProcessCommandLine -Value $applicationPath
        foreach ($argument in $applicationArguments) {
            $commandLine += ' ' + (ConvertTo-Win32ProcessCommandLine -Value $argument)
        }

        $created = Invoke-CimMethod -ClassName Win32_Process -MethodName Create -Arguments @{
            CommandLine = $commandLine
            CurrentDirectory = $PSScriptRoot
        }
        if ([int] $created.ReturnValue -ne 0 -or [int] $created.ProcessId -lt 1) {
            throw "无法脱离计划任务作业启动 C# 应用宿主：$applicationPath return=$($created.ReturnValue)"
        }

        $process = [System.Diagnostics.Process]::GetProcessById([int] $created.ProcessId)
        Start-Sleep -Milliseconds 200
        $process.Refresh()
        if ($process.HasExited) {
            $exitCode = $process.ExitCode
        }
        else {
            $exitCode = 0
        }
    }
}
finally {
    if ($null -ne $process) {
        $process.Dispose()
    }
}

exit $exitCode
