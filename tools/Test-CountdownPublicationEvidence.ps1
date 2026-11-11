#requires -Version 7.0
[CmdletBinding()]
param([string]$RepositoryRoot = (Join-Path $PSScriptRoot '..'),
    [ValidateSet('Index','Commit')][string]$Source = 'Index', [string]$Commit = 'HEAD', [switch]$Json)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'Countdown.PublicationEvidence.psm1') -Force
$result = Test-CountdownPublicationEvidence -RepositoryRoot $RepositoryRoot -Source $Source -Commit $Commit
if ($Json) { $result | ConvertTo-Json -Depth 8 } else { $result }
if (-not $result.Valid) { exit 1 }
exit 0
