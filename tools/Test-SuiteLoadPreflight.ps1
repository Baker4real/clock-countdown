#requires -Version 7.0
# 后台负载采样 module 的行为：正常导入 tools/Countdown.SuiteBackgroundLoad.psm1，经 Measure-SuiteBackgroundLoad 调用真实实现，
# 读计数器、等待和按 PID 取进程名都换成假 port：不读真实计数器、不真的等待、不查真实进程，也不开窗口。
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Import-Module (Join-Path $PSScriptRoot 'Countdown.SuiteBackgroundLoad.psm1') -Force

# 假 Get-Counter 第 n 次调用返回 Responses 第 n 项，用完后重复最后一项。报错走 $PSCmdlet，和真 Get-Counter 一样受
# 调用方的 -ErrorAction / -ErrorVariable 支配。
$fake = @{
    Calls = 0
    Responses = @()
    Requested = [System.Collections.Generic.HashSet[string]]::new()
    Slept = [System.Collections.Generic.List[double]]::new()
}
$processNames = @{ 100 = 'game'; 300 = 'miner' }
$ports = [pscustomobject]@{
    GetCounter = {
        [CmdletBinding()]
        param([string[]] $Counter)
        $fake.Calls++
        [void] $fake.Requested.Add($Counter -join ' | ')
        $response = $fake.Responses[[Math]::Min($fake.Calls, $fake.Responses.Count) - 1]
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
    Sleep = { param([double] $Seconds) $fake.Slept.Add($Seconds) }
    GetProcessName = { param([int] $ProcessId) $processNames[$ProcessId] }
}

function New-Response {
    param([object[]] $Samples = @(), [string[]] $Errors = @(), [string] $Terminate = '')
    @{ Samples = $Samples; Errors = $Errors; Terminate = $Terminate }
}

function New-Sample {
    param([string] $Path, [double] $Value, [uint32] $Status = 0)
    [void] ($Path -match '\((.+)\)\\')
    [pscustomobject]@{ Path = $Path; InstanceName = $Matches[1]; CookedValue = $Value; Status = $Status }
}

$cores = [Environment]::ProcessorCount
function New-CpuTotal { param([double] $Percent) New-Sample '\\host\processor(_total)\% processor time' $Percent }
function New-GpuEngine {
    param([int] $ProcessId, [string] $Engine, [double] $Percent, [uint32] $Status = 0)
    New-Sample "\\host\gpu engine(pid_${ProcessId}_luid_0x0_0x1_phys_0_eng_0_engtype_$Engine)\utilization percentage" $Percent $Status
}
# 进程计数器按单核计：占满两个核是 200%。
function New-ProcessCpu {
    param([string] $Name, [double] $Percent, [uint32] $Status = 0)
    New-Sample "\\host\process($Name)\% processor time" ($Percent * $cores) $Status
}

# 第 Index 次采样的完整样本。三次之间 CPU 总量、pid 100 的 3d 引擎和 miner 进程各不相同，平均后是整数：
# CPU (10+20+60)/3=30；3d 引擎 pid 100 (30+45+45)/3=40，加上 pid 300、500、600 共 65，高过单个进程最忙的 videodecode 50；
# miner (15+30+30)/3=25%。_total 与 idle 不是进程；不足 1% 的 tiny 不列；GPU 只列前三个进程，查不到名字的写 PID。
function New-SampleSet {
    param([Parameter(Mandatory)] [ValidateRange(1, 3)] [int] $Index)
    @(
        New-CpuTotal (@(10, 20, 60)[$Index - 1])
        New-GpuEngine 100 '3d' (@(30, 45, 45)[$Index - 1])
        New-GpuEngine 300 '3d' 20
        New-GpuEngine 500 '3d' 3
        New-GpuEngine 600 '3d' 2
        New-GpuEngine 200 'videodecode' 50
        New-GpuEngine 300 'copy' 5
        New-ProcessCpu 'miner' (@(15, 30, 30)[$Index - 1])
        New-ProcessCpu 'game' 4
        New-ProcessCpu 'tiny' 0.5
        New-ProcessCpu '_total' 100
        New-ProcessCpu 'idle' 70
    )
}
$complete = 'CPU=30（miner 25%、game 4%） GPU=3d 65（game 40%、miner 20%、PID 500 3%）'

# 缺一类样本的那次读取整组作废，不算进平均：留下的样本故意给大值，算进去结果就变。
function New-PartialSampleSet {
    param([Parameter(Mandatory)] [ValidateSet('CPU 总量', 'GPU 引擎', '进程')] [string] $Without)
    @(
        if ($Without -ne 'CPU 总量') { New-CpuTotal 99 }
        if ($Without -ne 'GPU 引擎') { New-GpuEngine 400 '3d' 99 }
        if ($Without -ne '进程') { New-ProcessCpu 'partial' 99 }
    )
}

function Invoke-Sampling {
    param([Parameter(Mandatory)] [object[]] $Responses)
    $fake.Calls = 0
    $fake.Responses = $Responses
    $fake.Slept.Clear()
    $load = Measure-SuiteBackgroundLoad -Ports $ports
    $tail = 'calls={0} sleeps=[{1}]' -f $fake.Calls, ($fake.Slept -join ',')
    if ($load.Error) { return "Error=$($load.Error) $tail" }
    'CPU={0:N0}（{1}） GPU={2} {3:N0}（{4}） {5}' -f $load.CpuPercent, $load.CpuTop, $load.GpuEngine, $load.GpuPercent, $load.GpuTop, $tail
}

$failures = [System.Collections.Generic.List[string]]::new()
foreach ($case in @(
        @{
            Name = '完整样本取三次平均，列出最忙的进程'
            Expected = "$complete calls=3 sleeps=[]"
            Responses = @(1..3 | ForEach-Object { New-Response -Samples (New-SampleSet $_) })
        },
        @{
            # 展开通配符后退出的进程：PDH 给 PDH_CSTATUS_NO_INSTANCE（0x800007D1）或 PDH_INVALID_DATA（0xC0000BC6），
            # 同时报一条非终止错误。值故意给大，算进去就排到第一。
            Name = '单个实例无效时只丢这几条'
            Expected = "$complete calls=3 sleeps=[]"
            Responses = @(1..3 | ForEach-Object {
                    New-Response -Samples (@(New-SampleSet $_) + @(New-ProcessCpu 'exited' 90 0x800007D1u; New-GpuEngine 700 '3d' 90 0xC0000BC6u)) `
                        -Errors 'The data in one of the performance counter samples is not valid.'
                })
        },
        @{
            # 展开 \Process(*) 时进程数恰好在变，PDH 报 PDH_MORE_DATA（下面是中文系统上的原文），这一组没有进程样本。
            # 第一次采样连缺四次、第五次才读全，第三次采样又缺一次：每次采样各有五次机会，每次重取前等一秒。
            Name = '缺整类样本时隔一秒重取'
            Expected = "$complete calls=8 sleeps=[1,1,1,1,1]"
            Responses = @(
                New-Response -Samples (New-PartialSampleSet -Without '进程') -Errors '存在其他不符合缓冲区支持的数据返回。请分配较大的缓冲区并重新调用函数。'
                New-Response -Samples (New-PartialSampleSet -Without 'CPU 总量')
                New-Response -Samples (New-PartialSampleSet -Without 'GPU 引擎') -Errors 'The specified object was not found on the computer.'
                New-Response -Samples (New-PartialSampleSet -Without '进程')
                New-Response -Samples (New-SampleSet 1)
                New-Response -Samples (New-SampleSet 2)
                New-Response -Samples (New-PartialSampleSet -Without 'GPU 引擎')
                New-Response -Samples (New-SampleSet 3))
        },
        @{
            # 第一次采样读全，第二次采样连取五次仍缺：前面读全的样本也不交出半截结果。同一条原文只留一次，多条以「；」相连。
            Name = '同一次采样连取五次仍缺时返回非终止错误的原文'
            Expected = 'Error=连取 5 次都缺GPU 引擎样本（The specified object was not found on the computer.；The specified instance is not present.） calls=6 sleeps=[1,1,1,1]'
            Responses = @(
                New-Response -Samples (New-SampleSet 1)
                New-Response -Samples (New-PartialSampleSet -Without 'GPU 引擎') -Errors @(
                    'The specified object was not found on the computer.', 'The specified object was not found on the computer.',
                    'The specified instance is not present.'))
        },
        @{
            Name = '仍缺却没有错误输出时如实写明'
            Expected = 'Error=连取 5 次都缺CPU 总量样本（没有错误输出） calls=5 sleeps=[1,1,1,1]'
            Responses = @(New-Response -Samples (New-PartialSampleSet -Without 'CPU 总量'))
        },
        @{
            Name = '终止错误同样收进摘要，不抛给启动器'
            Expected = 'Error=连取 5 次都缺CPU 总量、GPU 引擎、进程样本（PDH 查询句柄失效） calls=5 sleeps=[1,1,1,1]'
            Responses = @(New-Response -Terminate 'PDH 查询句柄失效')
        }
    )) {
    try { $actual = Invoke-Sampling -Responses $case.Responses }
    catch { $actual = "抛出：$($_.Exception.Message)" }
    if ($actual -cne $case.Expected) { $failures.Add("$($case.Name)：期望「$($case.Expected)」，实际「$actual」") }
}

# 读哪几组计数器也是行为：写错一个路径，真实采样就永远缺那一类，预检每次都跳过。
$expectedCounters = '\Processor(_Total)\% Processor Time | \GPU Engine(*)\Utilization Percentage | \Process(*)\% Processor Time'
$requested = @($fake.Requested)
if ($requested.Count -ne 1 -or $requested[0] -cne $expectedCounters) {
    $failures.Add("每次都应读 CPU 总量、GPU 引擎与进程三组计数器：期望「$expectedCounters」，实际「$($requested -join '」「')」")
}

if ($failures.Count -gt 0) {
    throw "suite-load-preflight 失败：$($failures -join '；')"
}
Write-Output ('suite-load-preflight: PASS（经 module 用假计数器与假等待：完整样本取三次平均与进程排名、单个实例无效只丢该条、' +
    '缺整类样本隔一秒重取且每次采样各五次、连取五次仍缺返回错误原文、没有错误输出时写明、终止错误收进摘要）')
