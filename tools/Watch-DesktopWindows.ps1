#requires -Version 7.0
# 监测调用线程所在桌面（用户 Default 桌面）在观测窗口内新出现的顶层窗口。
# 默认输出每条新窗 JSON 行。-SuiteLeakOnly 只记录套件泄漏（敏敏子标题 / MinminCountdown / 套件 PID），
# 发现即立刻追加 OutputPath，供启动器红即停。本脚本不创建 WPF 或 Win32 业务窗。
[CmdletBinding()]
param(
    [int] $Seconds = 120,
    [string] $OutputPath,
    [switch] $SuiteLeakOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Import-Module -Name (Join-Path $PSScriptRoot 'Countdown.SuiteIsolation.psm1') -Force

$source = @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

public static class DesktopWindowWatcher
{
    public delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern bool EnumWindows(EnumWindowsProc callback, IntPtr extra);

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetClassNameW(IntPtr hwnd, StringBuilder className, int maxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetWindowTextW(IntPtr hwnd, StringBuilder text, int maxCount);

    [DllImport("user32.dll")]
    public static extern bool IsWindowVisible(IntPtr hwnd);

    public static List<string> Snapshot()
    {
        var rows = new List<string>();
        EnumWindows((hwnd, _) =>
        {
            uint pid;
            GetWindowThreadProcessId(hwnd, out pid);
            var cls = new StringBuilder(256);
            GetClassNameW(hwnd, cls, cls.Capacity);
            var title = new StringBuilder(512);
            GetWindowTextW(hwnd, title, title.Capacity);
            bool visible = IsWindowVisible(hwnd);
            rows.Add(pid.ToString() + "|" + cls + "|" + title + "|" + (visible ? "1" : "0"));
            return true;
        }, IntPtr.Zero);
        return rows;
    }
}
'@

Add-Type -TypeDefinition $source -Language CSharp

function Write-SuiteWatcherFinding {
    param([Parameter(Mandatory)] [string] $Line)

    if ($OutputPath) {
        Add-Content -LiteralPath $OutputPath -Value $Line -Encoding UTF8
    }
    Write-Output $Line
}

$seen = @{}
$initial = [DesktopWindowWatcher]::Snapshot()
foreach ($row in $initial) { $seen[$row] = $true }

$deadline = [DateTimeOffset]::Now.AddSeconds($Seconds)
$findings = [System.Collections.Generic.List[string]]::new()
while ([DateTimeOffset]::Now -lt $deadline) {
    foreach ($row in [DesktopWindowWatcher]::Snapshot()) {
        if ($seen.ContainsKey($row)) {
            continue
        }
        $seen[$row] = $true
        $parsed = ConvertFrom-SuiteDesktopWindowSnapshotRow -Row $row
        if ($null -eq $parsed) {
            continue
        }
        $procName = ''
        try { $procName = (Get-Process -Id $parsed.ProcessId -ErrorAction Stop).ProcessName } catch {}
        if ($SuiteLeakOnly -and -not (Test-SuiteDesktopLeakWindow `
                    -ProcessId $parsed.ProcessId `
                    -ProcessName $procName `
                    -ClassName $parsed.ClassName `
                    -Title $parsed.Title `
                    -Visible $parsed.Visible `
                    -SuiteProcessIds @())) {
            continue
        }

        $line = (Get-Date -Format 'HH:mm:ss.fff') + ' pid=' + $parsed.ProcessId +
            ' proc=' + $procName + ' class=' + $parsed.ClassName +
            ' visible=' + $(if ($parsed.Visible) { '1' } else { '0' }) +
            ' title=' + $parsed.Title
        $findings.Add($line)
        Write-SuiteWatcherFinding -Line $line
    }
    Start-Sleep -Milliseconds 100
}

$done = 'watcher-done total-new-windows=' + $findings.Count
Write-SuiteWatcherFinding -Line $done
