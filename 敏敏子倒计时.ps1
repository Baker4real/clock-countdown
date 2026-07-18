#requires -Version 7.0

[CmdletBinding()]
param(
    [switch] $ValidateOnly,
    [switch] $ExerciseNoteAdd,
    [switch] $ExercisePersistenceFailures,
    [switch] $ExerciseTagInteractions,
    [switch] $ExerciseFatalDiagnostic,
    [switch] $ExitAfterReadyForTest,
    [string] $SettingsFile,
    [string] $DiagnosticLogPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$resolvedDiagnosticLogPath = if ([string]::IsNullOrWhiteSpace($DiagnosticLogPath)) {
    Join-Path $PSScriptRoot 'logs\application.jsonl'
}
else {
    [System.IO.Path]::GetFullPath($DiagnosticLogPath)
}
$diagnosticsEnabled = -not $ValidateOnly -or $PSBoundParameters.ContainsKey('DiagnosticLogPath')

Import-Module (Join-Path $PSScriptRoot 'Countdown.Runtime.psm1') -Force
function Write-CountdownEntryDiagnostic {
    param(
        [Parameter(Mandatory)] [ValidateSet('Info', 'Warning', 'Error')] [string] $Level,
        [Parameter(Mandatory)] [string] $Event,
        [AllowNull()] [System.Exception] $Exception,
        [AllowNull()] [AllowEmptyString()] [string] $ScriptStackTrace
    )

    try {
        Countdown.Runtime\Write-CountdownDiagnosticEvent -Path $resolvedDiagnosticLogPath -Level $Level -Event $Event -Exception $Exception -ScriptStackTrace $ScriptStackTrace | Out-Null
    }
    catch {
        # 诊断写入失败不能覆盖应用原始异常或改变正常退出语义。
    }
}
if ($diagnosticsEnabled) {
    Write-CountdownEntryDiagnostic -Level Info -Event 'ProcessStarted'
}
try {
    Import-Module (Join-Path $PSScriptRoot 'Countdown.App.psm1') -Force
    Start-CountdownApplication @PSBoundParameters -ApplicationPath $PSCommandPath
    if ($diagnosticsEnabled) {
        Write-CountdownEntryDiagnostic -Level Info -Event 'ProcessExited'
    }
}
catch {
    if ($diagnosticsEnabled) {
        Write-CountdownEntryDiagnostic -Level Error -Event 'FatalUnhandledException' -Exception $_.Exception -ScriptStackTrace $_.ScriptStackTrace
    }
    throw
}
