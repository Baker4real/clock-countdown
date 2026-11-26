Set-StrictMode -Version Latest

function Get-CountdownVisualBaselineHostDpi {
    [CmdletBinding()]
    [OutputType([int])]
    param()
    if (-not ('MinminVisualBaselineDpiNative' -as [type])) {
        Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class MinminVisualBaselineDpiNative
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetProcessDpiAwarenessContext(IntPtr value);
    [DllImport("user32.dll")]
    private static extern uint GetDpiForSystem();
    public static int ReadHostDpi()
    {
        SetProcessDpiAwarenessContext(new IntPtr(-4));
        return checked((int)GetDpiForSystem());
    }
}
'@
    }
    $dpi = [MinminVisualBaselineDpiNative]::ReadHostDpi()
    if ($dpi -le 0) { throw '无法读取当前系统 DPI，拒绝生成视觉基线。' }
    $dpi
}

function Assert-CountdownVisualBaselineDpi {
    [CmdletBinding()]
    param(
        [ValidateRange(0, 768)] [int] $ActualDpi = 0,
        [ValidateRange(1, 768)] [int] $ExpectedDpi = 144
    )
    if ($ActualDpi -eq 0) { $ActualDpi = Get-CountdownVisualBaselineHostDpi }
    if ($ActualDpi -ne $ExpectedDpi) {
        $expectedPercent = [Math]::Round($ExpectedDpi * 100.0 / 96.0)
        throw "当前系统 DPI=$ActualDpi，现役视觉基线要求 DPI=$ExpectedDpi（$expectedPercent%）；已拒绝生成，未启动验证 runner。请先调整 Windows 显示缩放后重跑 BaselineGenerate。"
    }
    [pscustomobject]@{ Valid = $true; ExpectedDpi = $ExpectedDpi; ActualDpi = $ActualDpi }
}

Export-ModuleMember -Function Get-CountdownVisualBaselineHostDpi, Assert-CountdownVisualBaselineDpi
