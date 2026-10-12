#requires -Version 7.0
[CmdletBinding()]
param([switch] $Json, [switch] $FailureProbe)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
Import-Module (Join-Path $repo 'Countdown.DeploymentPreflight.psm1') -Force
$testRoot = Join-Path $repo "logs/deployment-preflight-test-$([guid]::NewGuid().ToString('N'))"
[void] [IO.Directory]::CreateDirectory($testRoot)
$assertions = 0
$inheritedEnvironment = @{}
foreach ($name in @('HTTP_PROXY', 'HTTPS_PROXY', 'ALL_PROXY', 'NO_PROXY', 'NuGetAudit')) {
    $inheritedEnvironment[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
}
function Assert-Preflight($Expected, $Actual, [string] $Name) {
    $script:assertions++
    if ($Expected -cne $Actual) { throw "$Name expected=$Expected actual=$Actual" }
}
$healthyHttp = {
    param($Uri, $TimeoutSeconds)
    if ($TimeoutSeconds -ne 8) { throw 'HTTP probes must use their bounded request timeout.' }
    if ($Uri -eq 'https://api.nuget.org/v3/index.json') {
        return @{ version = '3.0.0'; resources = @(@{ '@type' = 'VulnerabilityInfo/6.7.0'; '@id' = 'https://api.nuget.org/v3/vulnerabilities/index.json' }) }
    }
    return ,@(@{ '@name' = 'base'; '@id' = 'https://api.nuget.org/v3/vulnerabilities/base.json'; '@updated' = '2026-10-12T00:00:00Z' })
}
$healthyCim = {
    param($ClassName, $Filter, $TimeoutSeconds)
    if ($TimeoutSeconds -ne 2) { throw 'CIM must use the same two-second budget as installation.' }
    switch ($ClassName) {
        'Win32_Service' { return [pscustomobject]@{ Name = 'W32Time'; StartMode = 'Manual'; State = 'Stopped' } }
        'Win32_OperatingSystem' { return [pscustomobject]@{ TotalVisibleMemorySize = 8388608; FreePhysicalMemory = 1 } }
        'Win32_Process' {
            if ($Filter -eq "Name='explorer.exe'") { return [pscustomobject]@{ SessionId = 1; ProcessId = 10 } }
            return @()
        }
        default { throw 'Unexpected query.' }
    }
}
$healthyOwner = {
    param($Process, $TimeoutSeconds)
    if ($TimeoutSeconds -ne 2) { throw 'Owner lookup must use the installation timeout.' }
    [pscustomobject]@{ ReturnValue = 0; Domain = 'TEST'; User = 'Fixture' }
}
if ($FailureProbe) {
    Invoke-CountdownDeploymentPreflight -OutputPath (Join-Path $testRoot 'child-failure.json') -HttpGet { throw [Net.Http.HttpRequestException]::new('http://private-user:private-password@proxy.invalid/?secret=fixture') } -CimQuery $healthyCim -CimOwner $healthyOwner
    throw 'Unreachable after failed dependency check.'
}
$outputPath = Join-Path $testRoot 'healthy.json'
$output = @(Invoke-CountdownDeploymentPreflight -OutputPath $outputPath -HttpGet $healthyHttp -CimQuery $healthyCim -CimOwner $healthyOwner)
Assert-Preflight 1 $output.Count 'Preflight returns one result'
Assert-Preflight $true $output[0].Success 'Available dependencies pass even with low diagnostic free memory'
$saved = Get-Content -LiteralPath $outputPath -Raw | ConvertFrom-Json
Assert-Preflight $true $saved.Success 'Success evidence is persisted'
Assert-Preflight 5 @($saved.Checks | Where-Object Status -eq 'Passed').Count 'NuGet audit and actual installation CIM dependencies are checked'
Assert-Preflight 1024 $saved.Diagnostics.Memory.FreePhysicalBytes 'Memory is recorded without an invented rejection threshold'
$badService = {
    param($ClassName, $Filter, $TimeoutSeconds)
    if ($ClassName -eq 'Win32_Service') { throw [TimeoutException]::new('private-proxy-value must never appear') }
    & $healthyCim $ClassName $Filter $TimeoutSeconds
}
$failurePath = Join-Path $testRoot 'cim-timeout.json'
$continued = $false
$failure = $null
try {
    $null = Invoke-CountdownDeploymentPreflight -OutputPath $failurePath -HttpGet $healthyHttp -CimQuery $badService -CimOwner $healthyOwner
    $continued = $true
} catch { $failure = $_ }
Assert-Preflight $false $continued 'Failed preflight prevents continuation'
$failedResult = Get-Content -LiteralPath $failurePath -Raw | ConvertFrom-Json
Assert-Preflight $false $failedResult.Success 'CIM timeout is persisted as failure'
$failedCheck = $failedResult.Checks | Where-Object Id -eq 'CimTimeService'
Assert-Preflight 'TimeoutException' $failedCheck.ErrorType 'Failure preserves the original exception type'
Assert-Preflight ([TimeoutException]::new().HResult) $failedCheck.HResult 'Failure preserves the original numeric error code'
Assert-Preflight $false (($failedResult | ConvertTo-Json -Depth 8).Contains('private-proxy-value')) 'Failure evidence excludes raw sensitive messages'
Assert-Preflight $false $failure.Exception.Data['PreflightResult'].Success 'Caller can inspect the failed result without parsing error text'
$scenarios = @(
    @{ Name = 'network-refused'; Check = 'NuGetServiceIndex'; Http = { throw [Net.Sockets.SocketException]::new(10061) }; Cim = $healthyCim; Owner = $healthyOwner },
    @{ Name = 'missing-audit-resource'; Check = 'NuGetServiceIndex'; Http = { @{ version = '3.0.0'; resources = @() } }; Cim = $healthyCim; Owner = $healthyOwner },
    @{ Name = 'audit-index-unreachable'; Check = 'NuGetVulnerabilityInfo'; Http = {
        param($Uri, $TimeoutSeconds)
        if ($Uri -eq 'https://api.nuget.org/v3/index.json') { & $healthyHttp $Uri $TimeoutSeconds }
        else { throw [TimeoutException]::new('Audit endpoint timeout.') }
    }; Cim = $healthyCim; Owner = $healthyOwner },
    @{ Name = 'audit-index-invalid'; Check = 'NuGetVulnerabilityInfo'; Http = {
        param($Uri, $TimeoutSeconds)
        if ($Uri -eq 'https://api.nuget.org/v3/index.json') { & $healthyHttp $Uri $TimeoutSeconds }
        else { @{ message = 'This is not a vulnerability index.' } }
    }; Cim = $healthyCim; Owner = $healthyOwner },
    @{ Name = 'process-query-unavailable'; Check = 'CimProcesses'; Http = $healthyHttp; Cim = {
        param($ClassName, $Filter, $TimeoutSeconds)
        if ($Filter -like '*MinminCountdown.exe*') { throw [TimeoutException]::new('Process query timeout.') }
        & $healthyCim $ClassName $Filter $TimeoutSeconds
    }; Owner = $healthyOwner },
    @{ Name = 'owner-failed'; Check = 'CimExplorerOwner'; Http = $healthyHttp; Cim = $healthyCim; Owner = { @{ ReturnValue = 2; Domain = ''; User = '' } } },
    @{ Name = 'no-interactive-desktop'; Check = 'CimExplorerOwner'; Http = $healthyHttp; Cim = {
        param($ClassName, $Filter, $TimeoutSeconds)
        if ($Filter -eq "Name='explorer.exe'") { return @() }
        & $healthyCim $ClassName $Filter $TimeoutSeconds
    }; Owner = $healthyOwner },
    @{ Name = 'ambiguous-desktop'; Check = 'CimExplorerOwner'; Http = $healthyHttp; Cim = {
        param($ClassName, $Filter, $TimeoutSeconds)
        if ($Filter -eq "Name='explorer.exe'") { return @([pscustomobject]@{ SessionId = 1; ProcessId = 10 }, [pscustomobject]@{ SessionId = 2; ProcessId = 11 }) }
        & $healthyCim $ClassName $Filter $TimeoutSeconds
    }; Owner = $healthyOwner }
)
foreach ($scenario in $scenarios) {
    $path = Join-Path $testRoot "$($scenario.Name).json"
    $continued = $false
    try {
        $null = Invoke-CountdownDeploymentPreflight -OutputPath $path -HttpGet $scenario.Http -CimQuery $scenario.Cim -CimOwner $scenario.Owner
        $continued = $true
    } catch {
        if ($null -eq $_.Exception.Data['PreflightResult']) { throw }
    }
    Assert-Preflight $false $continued "$($scenario.Name) stops callers"
    $saved = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
    Assert-Preflight 'Failed' ($saved.Checks | Where-Object Id -eq $scenario.Check).Status "$($scenario.Name) identifies its failed dependency"
}
$multipleExplorer = {
    param($ClassName, $Filter, $TimeoutSeconds)
    if ($Filter -eq "Name='explorer.exe'") { return @([pscustomobject]@{ SessionId = 1; ProcessId = 10 }, [pscustomobject]@{ SessionId = 1; ProcessId = 11 }) }
    if ($ClassName -eq 'Win32_OperatingSystem') { throw [TimeoutException]::new('Memory diagnostics unavailable.') }
    & $healthyCim $ClassName $Filter $TimeoutSeconds
}
$sameDesktop = Invoke-CountdownDeploymentPreflight -OutputPath (Join-Path $testRoot 'same-desktop.json') -HttpGet $healthyHttp -CimQuery $multipleExplorer -CimOwner $healthyOwner
Assert-Preflight $true $sameDesktop.Success 'Multiple Explorer processes with the same owner and session are supported'
Assert-Preflight 'Unavailable' $sameDesktop.Diagnostics.Memory.Status 'Diagnostic memory failure does not block deployment'
$child = [Diagnostics.ProcessStartInfo]::new()
$child.FileName = (Get-Process -Id $PID).Path
$child.UseShellExecute = $false
$child.CreateNoWindow = $true
$child.RedirectStandardOutput = $true
$child.RedirectStandardError = $true
foreach ($argument in @('-NoLogo', '-NoProfile', '-File', $PSCommandPath, '-FailureProbe')) { $child.ArgumentList.Add($argument) }
$process = [Diagnostics.Process]::new()
$process.StartInfo = $child
try {
    [void] $process.Start()
    $stdoutTask = $process.StandardOutput.ReadToEndAsync()
    $stderrTask = $process.StandardError.ReadToEndAsync()
    if (-not $process.WaitForExit(15000)) { $process.Kill($true); throw 'Failure fixture did not exit within 15 seconds.' }
    $text = $stdoutTask.GetAwaiter().GetResult() + $stderrTask.GetAwaiter().GetResult()
    Assert-Preflight 1 $process.ExitCode 'Unhandled preflight failure exits PowerShell nonzero'
    Assert-Preflight $false ($text.Contains('private-password') -or $text.Contains('secret=fixture')) 'CLI failure does not disclose credentials or URL query'
} finally { $process.Dispose() }
foreach ($name in $inheritedEnvironment.Keys) {
    Assert-Preflight $true ($inheritedEnvironment[$name] -ceq [Environment]::GetEnvironmentVariable($name, 'Process')) 'Preflight preserves inherited proxy and audit environment values'
}
[pscustomobject]@{ Valid = $true; Assertions = $assertions; EvidenceDirectory = $testRoot } | ConvertTo-Json -Compress
