#requires -Version 7.0

[CmdletBinding()]
param(
    [switch] $ValidateOnly,
    [switch] $ExerciseNoteAdd,
    [switch] $ExercisePersistenceFailures,
    [switch] $ExerciseTagInteractions,
    [switch] $ExitAfterReadyForTest,
    [string] $SettingsFile
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Import-Module (Join-Path $PSScriptRoot 'Countdown.App.psm1') -Force
Start-CountdownApplication @PSBoundParameters -ApplicationPath $PSCommandPath
