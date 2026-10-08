#requires -Version 7.0
<#
.SYNOPSIS
    官方验证套件的 Win32 桌面隔离合同：桌面名识别、fail-closed、泄漏匹配、启动器决议。
#>
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$script:AllowUnisolatedEnvName = 'MINMIN_SUITE_ALLOW_UNISOLATED'

function Test-SuiteIsolatedDesktopName {
    param([Parameter(Mandatory)] [AllowEmptyString()] [string] $DesktopName)

    return $DesktopName -cmatch '^MinminSuiteIso(-[A-Za-z][A-Za-z0-9]*)?$'
}

function Get-SuiteThreadDesktopName {
    if (-not ('SuiteThreadDesktopProbe' -as [type])) {
        Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
using System.Text;

public static class SuiteThreadDesktopProbe
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
        if (handle == IntPtr.Zero)
        {
            throw new InvalidOperationException("GetThreadDesktop 失败 error=" + Marshal.GetLastWin32Error());
        }
        var builder = new StringBuilder(256);
        uint lengthNeeded;
        if (!GetUserObjectInformationW(handle, 2, builder, 256, out lengthNeeded))
        {
            throw new InvalidOperationException("GetUserObjectInformationW 失败 error=" + Marshal.GetLastWin32Error());
        }
        return builder.ToString();
    }
}
'@
    }

    return [SuiteThreadDesktopProbe]::GetDesktopName()
}

function Test-SuiteAllowUnisolatedOverride {
    return [string]::Equals(
        [string] [Environment]::GetEnvironmentVariable($script:AllowUnisolatedEnvName),
        '1',
        [StringComparison]::Ordinal)
}

function Assert-SuiteIsolatedDesktop {
    [CmdletBinding()]
    param(
        [string] $DesktopName,
        [switch] $AllowForegroundAuthorized
    )

    if ($AllowForegroundAuthorized -or (Test-SuiteAllowUnisolatedOverride)) {
        return
    }

    $name = if (-not [string]::IsNullOrWhiteSpace($DesktopName)) {
        $DesktopName
    }
    else {
        Get-SuiteThreadDesktopName
    }

    if (-not (Test-SuiteIsolatedDesktopName -DesktopName $name)) {
        throw ("拒绝在用户桌面直跑窗口套件（当前桌面='$name'）。请经 tools/Invoke-CountdownSuiteLowImpact.ps1 运行；用户正在用电脑时禁止 -AllowUnisolatedDesktop。")
    }
}

function Resolve-SuiteLauncherIsolation {
    param(
        [Parameter(Mandatory)] [bool] $IsolatedDesktopBound,
        [Parameter(Mandatory)] [bool] $IsolatedDesktop,
        [Parameter(Mandatory)] [bool] $AllowUnisolatedDesktop
    )

    if ($AllowUnisolatedDesktop) {
        return [pscustomobject] @{
            Isolated = $false
            Reason = 'explicit-opt-out'
        }
    }

    if ($IsolatedDesktopBound -and -not $IsolatedDesktop) {
        throw '拒绝 -IsolatedDesktop:$false。用户正在用电脑时禁止非隔离运行；调试必须显式 -AllowUnisolatedDesktop。'
    }

    return [pscustomobject] @{
        Isolated = $true
        Reason = $(if ($IsolatedDesktopBound) { 'explicit-on' } else { 'default' })
    }
}

function Test-SuiteDesktopLeakWindow {
    param(
        [Parameter(Mandatory)] [int] $ProcessId,
        [AllowEmptyString()] [string] $ProcessName = '',
        [AllowEmptyString()] [string] $ClassName = '',
        [AllowEmptyString()] [string] $Title = '',
        [Parameter(Mandatory)] [bool] $Visible,
        [int[]] $SuiteProcessIds = @()
    )

    if (-not $Visible) {
        return $false
    }

    $titleHit = $Title -match '敏敏子'
    $processHit = $ProcessName -in @('MinminCountdown', 'minmin-countdown-slice')
    $pidHit = @($SuiteProcessIds) -contains $ProcessId
    $classHit = $ClassName -match 'MinminCountdown'
    return [bool] ($titleHit -or $processHit -or $pidHit -or $classHit)
}

function ConvertFrom-SuiteDesktopWindowSnapshotRow {
    param([Parameter(Mandatory)] [string] $Row)

    $parts = $Row -split '\|', 4
    if ($parts.Count -lt 4) {
        return $null
    }

    return [pscustomobject] @{
        ProcessId = [int] $parts[0]
        ClassName = [string] $parts[1]
        Title = [string] $parts[2]
        Visible = [string] $parts[3] -eq '1'
    }
}

Export-ModuleMember -Function @(
    'Test-SuiteIsolatedDesktopName',
    'Get-SuiteThreadDesktopName',
    'Test-SuiteAllowUnisolatedOverride',
    'Assert-SuiteIsolatedDesktop',
    'Resolve-SuiteLauncherIsolation',
    'Test-SuiteDesktopLeakWindow',
    'ConvertFrom-SuiteDesktopWindowSnapshotRow'
)
