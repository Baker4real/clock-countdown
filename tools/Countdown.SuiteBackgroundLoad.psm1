#requires -Version 7.0

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# 后台负载采样：套件启动器开跑窗口阶段前调用，读 CPU 总量、GPU 引擎与进程计数器，得出 CPU 总占用、最忙的 GPU 引擎类型和
# 各自最忙的进程。哪些阶段要采样、多高算忙、读不全时怎么报，由启动器决定。读计数器、等待和按 PID 取进程名经 port 调用：
# 生产用 New-SuiteBackgroundLoadWindowsPorts，测试注入假 port（tools/Test-SuiteLoadPreflight.ps1）。

# GetCounter 与 Sleep 就是 Get-Counter、Start-Sleep 本身：采样按它们的参数和错误语义调用（-Counter 加 -ErrorAction /
# -ErrorVariable；-Seconds），假 port 照同样的语义实现。GetProcessName 按 PID 取进程名，进程已退出时不输出。
function New-SuiteBackgroundLoadWindowsPorts {
    [pscustomobject]@{
        GetCounter = Get-Command -Name 'Get-Counter' -CommandType Cmdlet
        Sleep = Get-Command -Name 'Start-Sleep' -CommandType Cmdlet
        GetProcessName = {
            param([int] $ProcessId)
            $process = Get-Process -Id $ProcessId -ErrorAction Ignore
            if ($process) { $process.ProcessName }
        }
    }
}

# 采样三次取平均：CPU 总占用，GPU 取最忙的引擎类型（同类引擎跨进程求和），并记下各自最忙的进程。
# 进程不停起落时（上一轮启动器的子进程刚退出就是这样），Get-Counter 先报一条非终止的 CounterApiError，再照样输出整组
# 样本：展开通配符后退出的进程，实例状态非零，丢掉这几条即可；展开时实例数恰好在变，会缺掉整类样本，隔一秒重取。
# 同一次采样连取五次仍缺，就返回只有 Error（带错误原文）的结果，不抛给调用方。
# 读全时 Error 为空串；CpuPercent、GpuPercent 是平均占用（%）；GpuEngine 是最忙的引擎类型；CpuTop、GpuTop 列出占用
# 不低于 1% 的前三个进程（「名称 N%」以顿号相连，GPU 进程查不到名字时写「PID n」）。
function Measure-SuiteBackgroundLoad {
    [CmdletBinding()]
    param([object] $Ports = (New-SuiteBackgroundLoadWindowsPorts))

    $sampleCount = 3
    $maxAttempts = 5
    $gpuEngineInstance = '^pid_(\d+)_.*_engtype_(.+)$'
    $cpuPercent = 0.0
    $engineTotals = @{}
    $engineByPid = @{}
    $cpuByProcess = @{}
    for ($sample = 0; $sample -lt $sampleCount; $sample++) {
        for ($attempt = 1; ; $attempt++) {
            $counterErrors = @()
            $samples = @()
            try {
                $samples = @(& $Ports.GetCounter -ErrorAction SilentlyContinue -ErrorVariable counterErrors -Counter @(
                        '\Processor(_Total)\% Processor Time', '\GPU Engine(*)\Utilization Percentage', '\Process(*)\% Processor Time') |
                    ForEach-Object CounterSamples | Where-Object Status -eq 0)
            }
            catch { $counterErrors = @($_) }
            $missing = @(
                if ($samples.Where({ $_.Path -match '\\processor\(_total\)\\' }, 'First').Count -eq 0) { 'CPU 总量' }
                if ($samples.Where({ $_.InstanceName -match $gpuEngineInstance }, 'First').Count -eq 0) { 'GPU 引擎' }
                if ($samples.Where({ $_.Path -match '\\process\(' }, 'First').Count -eq 0) { '进程' })
            if ($missing.Count -eq 0) { break }
            if ($attempt -ge $maxAttempts) {
                $reasons = @($counterErrors | ForEach-Object { $_.Exception.Message } | Select-Object -Unique)
                return [pscustomobject]@{
                    Error = '连取 {0} 次都缺{1}样本（{2}）' -f $maxAttempts, ($missing -join '、'),
                        $(if ($reasons.Count -gt 0) { $reasons -join '；' } else { '没有错误输出' })
                }
            }
            & $Ports.Sleep -Seconds 1
        }
        foreach ($counter in $samples) {
            if ($counter.Path -match '\\processor\(_total\)\\') {
                $cpuPercent += $counter.CookedValue / $sampleCount
            }
            elseif ($counter.InstanceName -match $gpuEngineInstance) {
                $engine = $Matches[2]
                $engineTotals[$engine] += $counter.CookedValue / $sampleCount
                if (-not $engineByPid.ContainsKey($engine)) { $engineByPid[$engine] = @{} }
                $engineByPid[$engine][[int] $Matches[1]] += $counter.CookedValue / $sampleCount
            }
            elseif ($counter.Path -match '\\process\(' -and $counter.InstanceName -notin @('_total', 'idle')) {
                $cpuByProcess[$counter.InstanceName] += $counter.CookedValue / $sampleCount / [Environment]::ProcessorCount
            }
        }
    }
    $busiest = $engineTotals.GetEnumerator() | Sort-Object Value -Descending | Select-Object -First 1
    $formatTop = {
        param([hashtable] $byOwner, [scriptblock] $name)
        @($byOwner.GetEnumerator() | Sort-Object Value -Descending | Select-Object -First 3 |
            Where-Object Value -ge 1 | ForEach-Object { '{0} {1:N0}%' -f (& $name $_.Key), $_.Value }) -join '、'
    }
    [pscustomobject]@{
        Error = ''
        CpuPercent = $cpuPercent
        CpuTop = & $formatTop $cpuByProcess { param($key) $key }
        GpuEngine = $busiest.Key
        GpuPercent = $busiest.Value
        GpuTop = & $formatTop $engineByPid[$busiest.Key] {
            param($key) $processName = & $Ports.GetProcessName $key; if ($processName) { $processName } else { "PID $key" }
        }
    }
}

Export-ModuleMember -Function Measure-SuiteBackgroundLoad
