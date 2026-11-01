#requires -Version 7.0
# 后台负载预检读计数器：进程起落时 Get-Counter 先报一条非终止错误，再照样输出整组样本。状态非零的实例要丢掉，
# 不能作废整组；缺了整类样本要隔一秒重取；连取五次仍缺要返回带错误原文的摘要，不抛给启动器。
# 从启动器里取出 Measure-SuiteBackgroundLoad，Get-Counter 与 Start-Sleep 换成假实现：不读真实计数器，不开窗口。
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$tokens = $null
$errors = $null
$launcher = [System.Management.Automation.Language.Parser]::ParseFile(
    (Join-Path $PSScriptRoot 'Invoke-CountdownSuiteLowImpact.ps1'), [ref] $tokens, [ref] $errors)
$definition = $launcher.Find({
        param($node)
        $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'Measure-SuiteBackgroundLoad'
    }, $true)
if ($null -eq $definition) { throw 'suite-load-preflight 失败：启动器里找不到 Measure-SuiteBackgroundLoad' }
. ([scriptblock]::Create($definition.Extent.Text))

# 假 Get-Counter 按调用序号取 $fake.Respond 的结果，报错走 $PSCmdlet，和真 Get-Counter 一样受调用方的
# -ErrorAction / -ErrorVariable 支配。
$fake = @{ Calls = 0; Sleeps = 0; Respond = $null }
function Get-Counter {
    [CmdletBinding()]
    param([string[]] $Counter)
    $fake.Calls++
    $response = & $fake.Respond $fake.Calls
    foreach ($message in $response.Errors) {
        $PSCmdlet.WriteError([System.Management.Automation.ErrorRecord]::new(
                [System.Exception]::new($message), 'CounterApiError', 'InvalidResult', $null))
    }
    if ($response.Terminate) {
        $PSCmdlet.ThrowTerminatingError([System.Management.Automation.ErrorRecord]::new(
                [System.Exception]::new($response.Terminate), 'CounterApiError', 'InvalidResult', $null))
    }
    [pscustomobject]@{ CounterSamples = $response.Samples }
}
function Start-Sleep { param([double] $Seconds) $fake.Sleeps++ }

function New-Response {
    param([object[]] $Samples = @(), [string[]] $Errors = @(), [string] $Terminate = '')
    @{ Samples = $Samples; Errors = $Errors; Terminate = $Terminate }
}

function New-Sample {
    param([string] $Path, [double] $Value, [uint32] $Status = 0)
    [void] ($Path -match '\((.+)\)\\')
    [pscustomobject]@{ Path = $Path; InstanceName = $Matches[1]; CookedValue = $Value; Status = $Status }
}

function Invoke-Preflight {
    param([scriptblock] $Respond)
    $fake.Calls = 0
    $fake.Sleeps = 0
    $fake.Respond = $Respond
    $load = Measure-SuiteBackgroundLoad
    if ($load.Error) { return 'Error={0} calls={1} sleeps={2}' -f $load.Error, $fake.Calls, $fake.Sleeps }
    'CPU={0:N0} CpuTop={1} GPU={2} {3:N0} calls={4} sleeps={5}' -f
        $load.CpuPercent, $load.CpuTop, $load.GpuEngine, $load.GpuPercent, $fake.Calls, $fake.Sleeps
}

$cores = [Environment]::ProcessorCount
$total = New-Sample '\\host\processor(_total)\% processor time' 30
$gpu = @(
    New-Sample '\\host\gpu engine(pid_100_luid_0x0_0x1_phys_0_eng_0_engtype_3d)\utilization percentage' 40
    New-Sample '\\host\gpu engine(pid_200_luid_0x0_0x1_phys_0_eng_0_engtype_3d)\utilization percentage' 20
    New-Sample '\\host\gpu engine(pid_200_luid_0x0_0x1_phys_0_eng_1_engtype_copy)\utilization percentage' 5)
$miner = New-Sample '\\host\process(miner)\% processor time' (25 * $cores)
# 展开后退出的进程：PDH 给 PDH_CSTATUS_NO_INSTANCE。值故意给大，算进去就会排到 CPU 列表第一。
$exited = New-Sample '\\host\process(exited)\% processor time' (90 * $cores) 0x800007D1u

$failures = [System.Collections.Generic.List[string]]::new()
foreach ($case in @(
        @{
            Name = '单个实例无效时只丢这一条'
            Expected = 'CPU=30 CpuTop=miner 25% GPU=3d 60 calls=3 sleeps=0'
            Respond = { New-Response -Samples (@($total) + $gpu + @($miner, $exited)) -Errors 'The data in one of the performance counter samples is not valid.' }
        },
        @{
            # 展开 \Process(*) 时进程数恰好在变，PDH 报 PDH_MORE_DATA（下面是中文系统上的原文）：这一组没有进程样本。
            Name = '缺整类样本时隔一秒重取'
            Expected = 'CPU=30 CpuTop=miner 25% GPU=3d 60 calls=4 sleeps=1'
            Respond = {
                param($call)
                if ($call -eq 1) {
                    return New-Response -Samples (@($total) + $gpu) -Errors '存在其他不符合缓冲区支持的数据返回。请分配较大的缓冲区并重新调用函数。'
                }
                New-Response -Samples (@($total) + $gpu + @($miner))
            }
        },
        @{
            Name = '连取五次仍缺时返回非终止错误的原文'
            Expected = 'Error=连取 5 次都缺GPU 引擎样本（The specified object was not found on the computer.） calls=5 sleeps=4'
            Respond = { New-Response -Samples @($total, $miner) -Errors 'The specified object was not found on the computer.' }
        },
        @{
            Name = '终止错误同样收进摘要，不抛给启动器'
            Expected = 'Error=连取 5 次都缺CPU 总量、GPU 引擎、进程样本（PDH 查询句柄失效） calls=5 sleeps=4'
            Respond = { New-Response -Terminate 'PDH 查询句柄失效' }
        }
    )) {
    try { $actual = Invoke-Preflight -Respond $case.Respond }
    catch { $actual = "抛出：$($_.Exception.Message)" }
    if ($actual -ne $case.Expected) { $failures.Add("$($case.Name)：期望「$($case.Expected)」，实际「$actual」") }
}

if ($failures.Count -gt 0) {
    throw "suite-load-preflight 失败：$($failures -join '；')"
}
Write-Output 'suite-load-preflight: PASS（单个实例无效只丢该条、缺整类样本隔一秒重取、连取五次仍缺返回错误原文、终止错误收进摘要）'
