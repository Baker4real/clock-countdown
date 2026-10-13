#requires -Version 7.0
[CmdletBinding()]
param([switch] $Json)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
$target = Join-Path $PSScriptRoot 'Test-CountdownWindowPreflight.ps1'
$appPath = Join-Path (Split-Path -Parent $PSScriptRoot) 'app'
$checks = [Collections.Generic.List[object]]::new()

# Invalid candidate input must fail before desktop or WPF initialization. These
# process-contract checks intentionally never authorize a windowed execution.
function Test-RejectedCandidate {
    param([string] $Name, [string] $CandidatePath, [string] $Hash, [string] $ExpectedCode)
    $process = [Diagnostics.Process]::new()
    $process.StartInfo = [Diagnostics.ProcessStartInfo]::new((Get-Command pwsh.exe -CommandType Application | Select-Object -First 1 -ExpandProperty Source))
    $process.StartInfo.UseShellExecute = $false
    $process.StartInfo.CreateNoWindow = $true
    $process.StartInfo.RedirectStandardOutput = $true
    $process.StartInfo.RedirectStandardError = $true
    $process.StartInfo.StandardOutputEncoding = [Text.UTF8Encoding]::new($false)
    $process.StartInfo.StandardErrorEncoding = [Text.UTF8Encoding]::new($false)
    foreach ($argument in @('-NoLogo','-NoProfile','-NonInteractive','-STA','-File',$target,
            '-AppPath',$CandidatePath,'-ExpectedDllSha256',$Hash)) {
        $process.StartInfo.ArgumentList.Add($argument)
    }
    $process.StartInfo.Environment.Remove('MINMIN_SUITE_ALLOW_UNISOLATED') | Out-Null
    try {
        if (-not $process.Start()) { throw 'Unable to start contract child.' }
        $stdout = $process.StandardOutput.ReadToEndAsync()
        $stderr = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit(30000)) {
            $process.Kill($true)
            $process.WaitForExit()
            throw 'Window preflight input rejection exceeded 30 seconds.'
        }
        $text = $stdout.GetAwaiter().GetResult()
        $errorText = $stderr.GetAwaiter().GetResult()
        $report = $text | ConvertFrom-Json -ErrorAction Stop
        if ($process.ExitCode -eq 0 -or $report.Valid -or $report.Failed -ne 1 -or
            $report.Total -ne 1 -or $report.Passed -ne 0 -or
            $report.FailureCode -ne $ExpectedCode -or $report.WindowChecksCompleted -ne 0 -or
            -not [string]::IsNullOrWhiteSpace($errorText)) {
            throw "Unexpected rejection contract for $Name."
        }
        $checks.Add([pscustomobject]@{ Name=$Name; Passed=$true; FailureCode=$report.FailureCode })
    }
    catch { $checks.Add([pscustomobject]@{ Name=$Name; Passed=$false; Error=$_.Exception.Message }) }
    finally { $process.Dispose() }
}

Test-RejectedCandidate -Name 'wrong-dll-hash' -CandidatePath $appPath -Hash ('0' * 64) -ExpectedCode 'CandidateHashMismatch'
Test-RejectedCandidate -Name 'malformed-dll-hash' -CandidatePath $appPath -Hash 'not-a-hash' -ExpectedCode 'InvalidExpectedHash'
Test-RejectedCandidate -Name 'missing-candidate' -CandidatePath (Join-Path $appPath 'missing-window-preflight-candidate') -Hash ('0' * 64) -ExpectedCode 'CandidateMissing'
$failed = @($checks | Where-Object { -not $_.Passed }).Count
[pscustomobject]@{ Valid=($failed -eq 0); Passed=($checks.Count - $failed); Total=$checks.Count; Failed=$failed; Checks=$checks.ToArray() } |
    ConvertTo-Json -Depth 4
if ($failed -ne 0) { exit 1 }
