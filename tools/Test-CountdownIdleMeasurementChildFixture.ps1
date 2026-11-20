#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [ValidateSet('normal', 'graceful', 'ignore')] [string] $Mode,
    [Parameter(Mandatory)] [string] $GracefulSignalPath,
    [ValidateRange(1, 60)] [int] $ExitAfterSeconds
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if ($Mode -eq 'normal') {
    Start-Sleep -Seconds $ExitAfterSeconds
    exit 0
}
$deadline = [DateTimeOffset]::UtcNow.AddSeconds($ExitAfterSeconds)
while ([DateTimeOffset]::UtcNow -lt $deadline) {
    if ($Mode -eq 'graceful' -and [IO.File]::Exists($GracefulSignalPath)) { exit 0 }
    Start-Sleep -Milliseconds 50
}
exit 2
