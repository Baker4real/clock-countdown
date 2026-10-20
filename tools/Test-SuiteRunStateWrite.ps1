#requires -Version 7.0
# 套件运行状态的原子写：目标被别的句柄短暂打开（杀毒扫描、读取方）时替换会报 Access denied，
# Write-SuiteRunJson 应重试后写入成功；一直被占时约 2 秒后抛出、写明目标并清掉临时文件，不无限重试。
# 不开窗口，只在临时目录里读写。
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$module = Import-Module (Join-Path $PSScriptRoot 'Countdown.SuiteRun.psm1') -Force -PassThru
$workRoot = Join-Path ([System.IO.Path]::GetTempPath()) 'minmin-suite-run-state-write'
[void] [System.IO.Directory]::CreateDirectory($workRoot)
$statePath = Join-Path $workRoot 'summary.json'
foreach ($leftover in [System.IO.Directory]::GetFiles($workRoot, 'summary.json.*.tmp')) { [System.IO.File]::Delete($leftover) }
[System.IO.File]::WriteAllText($statePath, '{"Status":"Running"}')

function Invoke-StateWrite {
    param([Parameter(Mandatory)] [string] $Status)
    & $module { param($Path, $Status) Write-SuiteRunJson -Path $Path -Value @{ Status = $Status } } $statePath $Status
}

function Get-LeftoverCount {
    @([System.IO.Directory]::GetFiles($workRoot, 'summary.json.*.tmp')).Count
}

$failures = [System.Collections.Generic.List[string]]::new()
$watch = [System.Diagnostics.Stopwatch]::new()

# 1) 目标被占 300 ms：第一次替换必然被拒，重试后写入成功、不留临时文件。
$hold = [System.IO.File]::Open($statePath, 'Open', 'Read', 'ReadWrite, Delete')
$release = Start-ThreadJob -ScriptBlock { Start-Sleep -Milliseconds 300; ($using:hold).Dispose() }
$watch.Restart()
try {
    Invoke-StateWrite -Status 'Completed'
}
catch {
    $failures.Add("短暂占用时写入失败：$($_.Exception.Message)")
}
$transientSeconds = $watch.Elapsed.TotalSeconds
Receive-Job -Job $release -Wait -AutoRemoveJob | Out-Null
$hold.Dispose()
if ($transientSeconds -lt 0.25) { $failures.Add(('短暂占用应先被拒再重试成功，实际 {0:N2} 秒即返回' -f $transientSeconds)) }
if ((Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json).Status -ne 'Completed') { $failures.Add('短暂占用后状态未写入') }
if ((Get-LeftoverCount) -ne 0) { $failures.Add('短暂占用后留下了临时文件') }

# 2) 目标一直被占：约 2 秒后放弃，抛出写明目标的错误，清掉临时文件，原文件不变。
$before = [System.IO.File]::ReadAllText($statePath)
$hold = [System.IO.File]::Open($statePath, 'Open', 'Read', 'ReadWrite, Delete')
$persistentError = $null
$watch.Restart()
try {
    Invoke-StateWrite -Status 'Failed'
}
catch {
    $persistentError = $_.Exception.Message
}
finally {
    $hold.Dispose()
}
$persistentSeconds = $watch.Elapsed.TotalSeconds
if ($null -eq $persistentError) {
    $failures.Add('一直被占时应抛出错误')
}
elseif ($persistentError -notmatch 'summary\.json') {
    $failures.Add("失败消息应写明目标文件：$persistentError")
}
if ($persistentSeconds -lt 1.5 -or $persistentSeconds -gt 10) { $failures.Add(('一直被占时应约 2 秒后放弃，实际 {0:N1} 秒' -f $persistentSeconds)) }
if ((Get-LeftoverCount) -ne 0) { $failures.Add('放弃后留下了临时文件') }
if ([System.IO.File]::ReadAllText($statePath) -ne $before) { $failures.Add('失败的写入改动了原文件') }

if ($failures.Count -gt 0) {
    throw "suite-run-state-write 失败：$($failures -join '；')"
}
Write-Output ('suite-run-state-write: PASS（短暂占用 {0:N2} 秒后写入；一直被占 {1:N1} 秒后放弃）' -f $transientSeconds, $persistentSeconds)
