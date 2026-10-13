#requires -Version 7.0
Set-StrictMode -Version Latest

function Invoke-CountdownDeploymentPreflight {
    <#
    .SYNOPSIS
    只读检查部署实际依赖；失败先保存脱敏结果，再抛出终止异常。
    .DESCRIPTION
    HTTP 使用当前进程继承的默认代理，不改环境或 NuGetAudit。三个可选
    scriptblock 是 HTTP/CIM 系统边界的测试 seam，不用于跳过生产检查。
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [string] $OutputPath,
        [scriptblock] $HttpGet,
        [scriptblock] $CimQuery,
        [scriptblock] $CimOwner
    )
    $ErrorActionPreference = 'Stop'
    if ($null -eq $HttpGet) {
        $HttpGet = {
            param([uri] $Uri, [int] $TimeoutSeconds)
            $handler = [Net.Http.HttpClientHandler]::new()
            # NuGet 漏洞索引可能返回 gzip；保持默认代理，只启用 HTTP 内容解压。
            $handler.AutomaticDecompression = [Net.DecompressionMethods]::All
            $client = [Net.Http.HttpClient]::new($handler)
            try {
                $client.Timeout = [TimeSpan]::FromSeconds($TimeoutSeconds)
                $client.MaxResponseContentBufferSize = 1048576
                $body = $client.GetStringAsync($Uri).GetAwaiter().GetResult()
                ConvertFrom-Json -InputObject $body -AsHashtable -NoEnumerate -ErrorAction Stop
            } finally { $client.Dispose(); $handler.Dispose() }
        }
    }
    if ($null -eq $CimQuery) {
        $CimQuery = {
            param($ClassName, $Filter, $TimeoutSeconds)
            $arguments = @{ ClassName = $ClassName; OperationTimeoutSec = $TimeoutSeconds; ErrorAction = 'Stop' }
            if (-not [string]::IsNullOrEmpty($Filter)) { $arguments.Filter = $Filter }
            Get-CimInstance @arguments
        }
    }
    if ($null -eq $CimOwner) {
        $CimOwner = {
            param($Process, $TimeoutSeconds)
            Invoke-CimMethod -InputObject $Process -MethodName GetOwner -OperationTimeoutSec $TimeoutSeconds -ErrorAction Stop
        }
    }
    $started = [DateTime]::UtcNow
    $checks = [Collections.Generic.List[object]]::new()
    $state = @{ VulnerabilityUri = $null }
    function Invoke-DependencyCheck([string] $Id, [string] $CallSite, [string] $FailureMessage, [scriptblock] $Action) {
        $watch = [Diagnostics.Stopwatch]::StartNew()
        try {
            & $Action
            $checks.Add([pscustomobject]@{ Id = $Id; Status = 'Passed'; CallSite = $CallSite; ElapsedMs = $watch.ElapsedMilliseconds; ErrorType = $null; HResult = $null; Message = $null })
        } catch {
            # 异常消息可能含代理凭据或带 query 的 URL，仅保存异常类型和固定说明。
            $cause = $_.Exception.GetBaseException()
            $checks.Add([pscustomobject]@{ Id = $Id; Status = 'Failed'; CallSite = $CallSite; ElapsedMs = $watch.ElapsedMilliseconds; ErrorType = $cause.GetType().Name; HResult = $cause.HResult; Message = $FailureMessage })
        }
    }
    Invoke-DependencyCheck 'NuGetServiceIndex' 'NuGet restore / service index' '当前继承网络路径无法取得有效 NuGet 官方服务索引或 VulnerabilityInfo 地址。' {
        $index = & $HttpGet ([uri] 'https://api.nuget.org/v3/index.json') 8
        if ($index.version -ne '3.0.0') { throw [IO.InvalidDataException]::new('Invalid service index version.') }
        $resource = @($index.resources | Where-Object { @($_.'@type') -contains 'VulnerabilityInfo/6.7.0' } | Select-Object -First 1)
        if ($resource.Count -ne 1) { throw [IO.InvalidDataException]::new('Missing VulnerabilityInfo resource.') }
        $uri = [uri] $resource[0].'@id'
        if (-not $uri.IsAbsoluteUri -or $uri.Scheme -ne 'https' -or $uri.UserInfo -or $uri.Query -or $uri.Fragment) {
            throw [IO.InvalidDataException]::new('Invalid VulnerabilityInfo address.')
        }
        $state.VulnerabilityUri = $uri
    }
    if ($null -eq $state.VulnerabilityUri) {
        $checks.Add([pscustomobject]@{ Id = 'NuGetVulnerabilityInfo'; Status = 'Skipped'; CallSite = 'NuGetAudit / VulnerabilityInfo'; ElapsedMs = 0; ErrorType = $null; HResult = $null; Message = '服务索引检查失败，未取得可验证的漏洞资源地址。' })
    } else {
        Invoke-DependencyCheck 'NuGetVulnerabilityInfo' 'NuGetAudit / VulnerabilityInfo' '当前继承网络路径无法取得有效 NuGet 漏洞资源索引；保持 NuGetAudit 启用。' {
            $vulnerabilityIndex = & $HttpGet $state.VulnerabilityUri 8
            if ($vulnerabilityIndex -isnot [array] -or $vulnerabilityIndex.Count -eq 0) { throw [IO.InvalidDataException]::new('Invalid vulnerability index.') }
            foreach ($entry in $vulnerabilityIndex) {
                $uri = [uri] $entry.'@id'
                if (-not $uri.IsAbsoluteUri -or $uri.Scheme -ne 'https' -or [string]::IsNullOrWhiteSpace([string] $entry.'@name')) {
                    throw [IO.InvalidDataException]::new('Invalid vulnerability page reference.')
                }
            }
        }
    }
    Invoke-DependencyCheck 'CimTimeService' 'Countdown.InstallTransaction.psm1 / Get-CountdownWindowsTimeSnapshot' 'W32Time 的 Win32_Service 查询失败或结果缺失（OperationTimeoutSec=2）。' {
        $services = @(& $CimQuery 'Win32_Service' "Name='W32Time'" 2)
        if ($services.Count -ne 1 -or $services[0].Name -ne 'W32Time') { throw [IO.InvalidDataException]::new('W32Time missing.') }
    }
    Invoke-DependencyCheck 'CimProcesses' 'Countdown.InstallTransaction.psm1 / Get-CountdownManagedApplicationProcesses' '部署进程的 Win32_Process 查询失败（OperationTimeoutSec=2）。' {
        $null = @(& $CimQuery 'Win32_Process' "Name='pwsh.exe' OR Name='powershell.exe' OR Name='MinminCountdown.exe'" 2)
    }
    Invoke-DependencyCheck 'CimExplorerOwner' 'Countdown.InstallTransaction.psm1 / Get-CountdownInteractiveDesktopContext' 'Explorer 查询或 GetOwner 失败，或未找到唯一交互会话与用户（OperationTimeoutSec=2）。' {
        $processes = @(& $CimQuery 'Win32_Process' "Name='explorer.exe'" 2)
        $identities = foreach ($process in $processes) {
            if ([int] $process.SessionId -le 0) { continue }
            $owner = & $CimOwner $process 2
            if ($null -eq $owner -or $owner.ReturnValue -ne 0 -or [string]::IsNullOrWhiteSpace([string] $owner.Domain) -or [string]::IsNullOrWhiteSpace([string] $owner.User)) {
                throw [IO.InvalidDataException]::new('Explorer owner unavailable.')
            }
            "$($process.SessionId)|$($owner.Domain)\$($owner.User)"
        }
        if (@($identities | Sort-Object -Unique).Count -ne 1) { throw [IO.InvalidDataException]::new('Ambiguous interactive desktop.') }
    }
    $memory = [ordered]@{ Status = 'Unavailable'; TotalPhysicalBytes = $null; FreePhysicalBytes = $null; ErrorType = $null }
    try {
        $os = @(& $CimQuery 'Win32_OperatingSystem' $null 2)
        if ($os.Count -ne 1) { throw [IO.InvalidDataException]::new('Memory diagnostics unavailable.') }
        $memory.TotalPhysicalBytes = [long] $os[0].TotalVisibleMemorySize * 1024
        $memory.FreePhysicalBytes = [long] $os[0].FreePhysicalMemory * 1024
        $memory.Status = 'Available'
    } catch { $memory.ErrorType = $_.Exception.GetBaseException().GetType().Name }
    $failed = @($checks | Where-Object Status -eq 'Failed')
    $result = [pscustomobject]@{
        SchemaVersion = 1
        Success = $failed.Count -eq 0
        StartedAtUtc = $started.ToString('o')
        CompletedAtUtc = [DateTime]::UtcNow.ToString('o')
        Checks = $checks.ToArray()
        Diagnostics = [pscustomobject]@{ ProxyMode = 'Inherited'; NuGetAudit = 'Unchanged'; Memory = [pscustomobject] $memory }
    }
    $fullPath = [IO.Path]::GetFullPath($OutputPath)
    [void] [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($fullPath))
    [IO.File]::WriteAllText($fullPath, ($result | ConvertTo-Json -Depth 8), [Text.UTF8Encoding]::new($false))
    if (-not $result.Success) {
        $exception = [InvalidOperationException]::new("部署依赖预检失败：$($failed.Id -join ', ')。已保存脱敏证据；停止后续完整回归。")
        $exception.Data['PreflightResult'] = $result
        throw $exception
    }
    $result
}

Export-ModuleMember -Function Invoke-CountdownDeploymentPreflight
