#requires -Version 7.0
# 监测调用线程所在桌面（用户 Default 桌面）在观测窗口内新出现的顶层窗口。
# 输出：每行一条 JSON：时间、PID、进程名、窗口类、标题。
[CmdletBinding()]
param([int] $Seconds = 120, [string] $OutputPath)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

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

$seen = @{}
$initial = [DesktopWindowWatcher]::Snapshot()
foreach ($row in $initial) { $seen[$row] = $true }

$deadline = [DateTimeOffset]::Now.AddSeconds($Seconds)
$findings = [System.Collections.Generic.List[string]]::new()
while ([DateTimeOffset]::Now -lt $deadline) {
    foreach ($row in [DesktopWindowWatcher]::Snapshot()) {
        if (-not $seen.ContainsKey($row)) {
            $seen[$row] = $true
            $parts = $row -split '\|', 4
            $procName = ''
            try { $procName = (Get-Process -Id ([int] $parts[0]) -ErrorAction Stop).ProcessName } catch {}
            $findings.Add((Get-Date -Format 'HH:mm:ss.fff') + ' pid=' + $parts[0] +
                ' proc=' + $procName + ' class=' + $parts[1] +
                ' visible=' + $parts[3] + ' title=' + $parts[2])
        }
    }
    Start-Sleep -Milliseconds 100
}

if ($OutputPath) {
    Set-Content -LiteralPath $OutputPath -Value $findings -Encoding UTF8
}
$findings | ForEach-Object { Write-Output $_ }
Write-Output ("watcher-done total-new-windows=" + $findings.Count)
