#requires -Version 7.0
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# 探针 1：BelowNormal 父进程的子进程是否继承优先级
[System.Diagnostics.Process]::GetCurrentProcess().PriorityClass = 'BelowNormal'
$childOut = Join-Path $env:TEMP 'prio-child.txt'
$child = Start-Process -FilePath pwsh -ArgumentList @(
    '-NoProfile', '-Command',
    '[System.Diagnostics.Process]::GetCurrentProcess().PriorityClass.ToString()'
) -RedirectStandardOutput $childOut -PassThru
$child.WaitForExit()
$childPriority = (Get-Content -LiteralPath $childOut -Raw).Trim()
Write-Output "probe1-parent: BelowNormal"
Write-Output "probe1-child: $childPriority"

# 探针 2：不可见桌面启动子进程，子进程回报自身桌面名与优先级。
# #123 行为不变式：桌面名由调用方传入（缺省保持现名 MinminSuiteIso）、
# 每阶段落在各自唯一桌面、隔离 CreateProcessW 路径 BelowNormal 传播不变。
# 子进程经 GetThreadDesktop/GetUserObjectInformationW 读回真实所属桌面，属外部可观察行为。

$probeTempRoot = Join-Path $env:TEMP 'minmin-suite-iso-probe'
[void] [System.IO.Directory]::CreateDirectory($probeTempRoot)
$childCsPath = Join-Path $probeTempRoot 'ChildDesktopProbe.cs'
$childScriptPath = Join-Path $probeTempRoot 'Invoke-ChildDesktopProbe.ps1'

@'
using System;
using System.Runtime.InteropServices;
using System.Text;

public static class ChildDesktopProbe
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetThreadDesktop(uint threadId);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetUserObjectInformationW(IntPtr handle, int index, StringBuilder info, int maxLength, out uint lengthNeeded);

    public static string GetDesktopName()
    {
        IntPtr handle = GetThreadDesktop(GetCurrentThreadId());
        var builder = new StringBuilder(256);
        uint lengthNeeded;
        if (!GetUserObjectInformationW(handle, 2, builder, 256, out lengthNeeded))
        {
            throw new InvalidOperationException("GetUserObjectInformationW failed error=" + Marshal.GetLastWin32Error());
        }
        return builder.ToString();
    }
}
'@ | Set-Content -LiteralPath $childCsPath -Encoding UTF8

@'
param(
    [Parameter(Mandatory)] [string] $ResultPath,
    [Parameter(Mandatory)] [string] $CsPath
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Add-Type -TypeDefinition ([System.IO.File]::ReadAllText($CsPath))
$desktopName = [ChildDesktopProbe]::GetDesktopName()
$priority = [System.Diagnostics.Process]::GetCurrentProcess().PriorityClass.ToString()
[System.IO.File]::WriteAllText($ResultPath, "desktop=$desktopName|priority=$priority")
'@ | Set-Content -LiteralPath $childScriptPath -Encoding UTF8

# 首个用例锁缺省合同：不传 -DesktopName 时保持现名；其余按套件六阶段逐一断言唯一桌面。
$stageNames = @('Build', 'Core', 'CSharp', 'Ui', 'SourceValidation', 'InstallValidate')
$cases = @(@{ Case = 'default'; ExpectedDesktop = 'MinminSuiteIso'; PassDesktop = $null })
$cases += @($stageNames | ForEach-Object {
    @{ Case = $_; ExpectedDesktop = "MinminSuiteIso-$_"; PassDesktop = "MinminSuiteIso-$_" }
})

$observedDesktops = @()
foreach ($case in $cases) {
    $caseStdout = Join-Path $probeTempRoot "$($case.Case).out.log"
    $caseStderr = Join-Path $probeTempRoot "$($case.Case).err.log"
    $resultPath = Join-Path $probeTempRoot "$($case.Case).result.txt"
    if (Test-Path -LiteralPath $resultPath) { Remove-Item -LiteralPath $resultPath -Force }

    $launcherArgs = @{
        FilePath         = (Get-Command pwsh).Source
        ArgumentList     = @('-NoProfile', '-NonInteractive', '-File', $childScriptPath, '-ResultPath', $resultPath, '-CsPath', $childCsPath)
        WorkingDirectory = $PSScriptRoot
        StdoutPath       = $caseStdout
        StderrPath       = $caseStderr
    }
    if ($null -ne $case.PassDesktop) { $launcherArgs.DesktopName = $case.PassDesktop }

    $isoExit = [int] (& (Join-Path $PSScriptRoot 'Start-IsolatedSuiteProcess.ps1') @launcherArgs | Select-Object -Last 1)
    $observed = if (Test-Path -LiteralPath $resultPath) { (Get-Content -LiteralPath $resultPath -Raw).Trim() } else { '<missing>' }
    Write-Output "probe2-[$($case.Case)]-exit: $isoExit"
    Write-Output "probe2-[$($case.Case)]-result: $observed"

    if ($isoExit -ne 0) { throw "probe2 [$($case.Case)]: 隔离桌面子进程退出码 $isoExit" }
    if ($observed -eq '<missing>') { throw "probe2 [$($case.Case)]: 子进程未写出结果文件" }
    $parts = $observed -split '\|'
    $observedDesktop = $parts[0] -replace '^desktop=', ''
    $observedPriority = $parts[1] -replace '^priority=', ''
    if ($observedDesktop -cne $case.ExpectedDesktop) {
        throw "probe2 [$($case.Case)]: 期望桌面 '$($case.ExpectedDesktop)'，实际 '$observedDesktop'"
    }
    if ($observedPriority -ne 'BelowNormal') {
        throw "probe2 [$($case.Case)]: 期望 BelowNormal，实际 '$observedPriority'"
    }
    $observedDesktops += $observedDesktop
}

if (($observedDesktops | Sort-Object -Unique).Count -ne $observedDesktops.Count) {
    throw "probe2: 观察到重复桌面名：$($observedDesktops -join ', ')"
}
Write-Output "probe2-assert: PASS (unique-desktops=$(($observedDesktops | Sort-Object -Unique).Count)/$($observedDesktops.Count), priority=BelowNormal, default-preserved)"

# 探针 3（#127）：并行调度原语——两个隔离阶段包装进程（bootstrap）同时启动。
# 与启动器并行路径同形：bootstrap 在包装进程内以真实数组绑定调用 Start-IsolatedSuiteProcess.ps1，
# 包装进程退出码即测试子进程退出码；断言并发下各自落在唯一桌面、BelowNormal 经包装进程传播、
# 退出码中继无损。子进程复用探针 2 的 ChildDesktopProbe。
$probe3Cases = @(
    @{ Case = 'Core'; DesktopName = 'MinminSuiteIso-Core' },
    @{ Case = 'CSharp'; DesktopName = 'MinminSuiteIso-CSharp' }
)
$probe3Runs = @()
foreach ($case in $probe3Cases) {
    $caseResultPath = Join-Path $probeTempRoot "parallel-$($case.Case).result.txt"
    $caseStdout = Join-Path $probeTempRoot "parallel-$($case.Case).out.log"
    $caseStderr = Join-Path $probeTempRoot "parallel-$($case.Case).err.log"
    $bootstrapPath = Join-Path $probeTempRoot "parallel-$($case.Case).iso.ps1"
    if (Test-Path -LiteralPath $caseResultPath) { Remove-Item -LiteralPath $caseResultPath -Force }

    $quote = { param($v) "'" + ($v -replace "'", "''") + "'" }
    $invokeLine =
        "& $(& $quote (Join-Path $PSScriptRoot 'Start-IsolatedSuiteProcess.ps1'))" +
        " -FilePath $(& $quote (Get-Command pwsh).Source)" +
        " -ArgumentList @('-NoProfile', '-NonInteractive', '-File', $(& $quote $childScriptPath), '-ResultPath', $(& $quote $caseResultPath), '-CsPath', $(& $quote $childCsPath))" +
        " -WorkingDirectory $(& $quote $PSScriptRoot)" +
        " -StdoutPath $(& $quote $caseStdout)" +
        " -StderrPath $(& $quote $caseStderr)" +
        " -DesktopName $(& $quote $case.DesktopName)"
    @"
`$ErrorActionPreference = 'Stop'
try {
    `$output = @($invokeLine)
    `$exitCode = [int] (`$output | Select-Object -Last 1)
}
catch {
    `$exitCode = 1
}
exit `$exitCode
"@ | Set-Content -LiteralPath $bootstrapPath -Encoding UTF8

    $bootstrapProcess = Start-Process -FilePath (Get-Command pwsh).Source `
        -ArgumentList @('-NoLogo', '-NoProfile', '-NonInteractive', '-File', $bootstrapPath) `
        -WorkingDirectory $PSScriptRoot `
        -RedirectStandardOutput (Join-Path $probeTempRoot "parallel-$($case.Case).iso.out.log") `
        -RedirectStandardError (Join-Path $probeTempRoot "parallel-$($case.Case).iso.err.log") `
        -WindowStyle Hidden -PassThru
    $bootstrapProcess.PriorityClass = 'BelowNormal'
    $probe3Runs += @{
        Case = $case.Case
        ExpectedDesktop = $case.DesktopName
        Process = $bootstrapProcess
        ResultPath = $caseResultPath
    }
}

$probe3Desktops = @()
foreach ($run in $probe3Runs) {
    $run.Process.WaitForExit()
    $observed = if (Test-Path -LiteralPath $run.ResultPath) { (Get-Content -LiteralPath $run.ResultPath -Raw).Trim() } else { '<missing>' }
    Write-Output "probe3-[$($run.Case)]-exit: $($run.Process.ExitCode)"
    Write-Output "probe3-[$($run.Case)]-result: $observed"

    if ($run.Process.ExitCode -ne 0) { throw "probe3 [$($run.Case)]: 包装进程退出码 $($run.Process.ExitCode)" }
    if ($observed -eq '<missing>') { throw "probe3 [$($run.Case)]: 子进程未写出结果文件" }
    $parts = $observed -split '\|'
    $probe3ObservedDesktop = $parts[0] -replace '^desktop=', ''
    $probe3ObservedPriority = $parts[1] -replace '^priority=', ''
    if ($probe3ObservedDesktop -cne $run.ExpectedDesktop) {
        throw "probe3 [$($run.Case)]: 期望桌面 '$($run.ExpectedDesktop)'，实际 '$probe3ObservedDesktop'"
    }
    if ($probe3ObservedPriority -ne 'BelowNormal') {
        throw "probe3 [$($run.Case)]: 期望 BelowNormal，实际 '$probe3ObservedPriority'"
    }
    $probe3Desktops += $probe3ObservedDesktop
}

if (($probe3Desktops | Sort-Object -Unique).Count -ne $probe3Desktops.Count) {
    throw "probe3: 并发下出现重复桌面名：$($probe3Desktops -join ', ')"
}
Write-Output "probe3-assert: PASS (concurrent-isolated-launches=$($probe3Desktops.Count), priority=BelowNormal, exit-code-relay=ok)"
