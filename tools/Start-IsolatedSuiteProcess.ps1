#requires -Version 7.0
<#
.SYNOPSIS
    在不可见 Win32 桌面（CreateDesktopW）中启动一个 BelowNormal 优先级进程，
    标准输出/错误重定向到文件，等待退出并以唯一一行整数输出退出码。
    用于官方验证套件的窗口隔离：窗口在专用桌面对象中渲染，
    不出现在用户当前桌面，也不参与前台焦点竞争。
.OUTPUTS
    唯一一行整数：子进程退出码。
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $FilePath,
    [string[]] $ArgumentList = @(),
    [Parameter(Mandatory)] [string] $WorkingDirectory,
    [Parameter(Mandatory)] [string] $StdoutPath,
    [Parameter(Mandatory)] [string] $StderrPath,
    [string] $DesktopName = 'MinminSuiteIso'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$source = @'
using System;
using System.Runtime.InteropServices;
using System.Text;

public static class IsolatedSuiteProcess
{
    private const uint GenericAll = 0x10000000;
    private const uint GenericRead = 0x80000000;
    private const uint GenericWrite = 0x40000000;
    private const uint FileShareRead = 0x00000001;
    private const uint OpenExisting = 3;
    private const uint CreateAlways = 2;
    private const uint FileAttributeNormal = 0x00000080;
    private const uint BelowNormalPriorityClass = 0x00004000;
    private const uint CreateUnicodeEnvironment = 0x00000400;
    private const int StartfUseStdHandles = 0x00000100;
    private const uint Infinite = 0xFFFFFFFF;
    private static readonly IntPtr InvalidHandleValue = new IntPtr(-1);

    [StructLayout(LayoutKind.Sequential)]
    private sealed class SecurityAttributes
    {
        public int Length;
        public IntPtr SecurityDescriptor;
        public bool InheritHandle;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private sealed class StartupInfo
    {
        public int Size;
        public string Reserved;
        public string Desktop;
        public string Title;
        public int X, Y, XSize, YSize, XCountChars, YCountChars, FillAttribute, Flags;
        public short ShowWindow;
        public short Reserved2;
        public IntPtr Reserved2Ptr;
        public IntPtr StdInput;
        public IntPtr StdOutput;
        public IntPtr StdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private sealed class ProcessInformation
    {
        public IntPtr Process;
        public IntPtr Thread;
        public int ProcessId;
        public int ThreadId;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateDesktopW(string desktop, IntPtr device, IntPtr devmode, int flags, uint desiredAccess, IntPtr securityAttributes);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool CloseDesktop(IntPtr desktop);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateFileW(string fileName, uint desiredAccess, uint shareMode, SecurityAttributes securityAttributes, uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CreateProcessW(string applicationName, StringBuilder commandLine, IntPtr processAttributes, IntPtr threadAttributes, bool inheritHandles, uint creationFlags, IntPtr environment, string currentDirectory, StartupInfo startupInfo, ProcessInformation processInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetExitCodeProcess(IntPtr process, out uint exitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

    private static IntPtr OpenInheritableFile(string path, uint access, uint creationDisposition)
    {
        var attributes = new SecurityAttributes
        {
            Length = Marshal.SizeOf(typeof(SecurityAttributes)),
            InheritHandle = true
        };
        IntPtr handle = CreateFileW(path, access, FileShareRead, attributes, creationDisposition, FileAttributeNormal, IntPtr.Zero);
        if (handle == InvalidHandleValue)
        {
            throw new InvalidOperationException("CreateFileW 失败: " + path + " error=" + Marshal.GetLastWin32Error());
        }
        return handle;
    }

    public static int Run(string desktopName, string application, string commandLine, string workingDirectory, string stdoutPath, string stderrPath)
    {
        IntPtr desktop = CreateDesktopW(desktopName, IntPtr.Zero, IntPtr.Zero, 0, GenericAll, IntPtr.Zero);
        if (desktop == IntPtr.Zero)
        {
            throw new InvalidOperationException("CreateDesktopW 失败 error=" + Marshal.GetLastWin32Error());
        }

        IntPtr stdin = IntPtr.Zero;
        IntPtr stdout = IntPtr.Zero;
        IntPtr stderr = IntPtr.Zero;
        ProcessInformation info = null;
        try
        {
            stdin = OpenInheritableFile("NUL", GenericRead, OpenExisting);
            stdout = OpenInheritableFile(stdoutPath, GenericWrite, CreateAlways);
            stderr = OpenInheritableFile(stderrPath, GenericWrite, CreateAlways);

            var startup = new StartupInfo
            {
                Size = Marshal.SizeOf(typeof(StartupInfo)),
                Desktop = "winsta0\\" + desktopName,
                Flags = StartfUseStdHandles,
                StdInput = stdin,
                StdOutput = stdout,
                StdError = stderr
            };
            info = new ProcessInformation();
            var mutableCommandLine = new StringBuilder(commandLine, 32768);
            bool created = CreateProcessW(application, mutableCommandLine, IntPtr.Zero, IntPtr.Zero, true,
                BelowNormalPriorityClass | CreateUnicodeEnvironment, IntPtr.Zero, workingDirectory, startup, info);
            if (!created)
            {
                throw new InvalidOperationException("CreateProcessW 失败: " + application + " error=" + Marshal.GetLastWin32Error());
            }

            WaitForSingleObject(info.Process, Infinite);
            uint code;
            if (!GetExitCodeProcess(info.Process, out code))
            {
                throw new InvalidOperationException("GetExitCodeProcess 失败 error=" + Marshal.GetLastWin32Error());
            }
            return unchecked((int)code);
        }
        finally
        {
            if (info != null)
            {
                if (info.Process != IntPtr.Zero) CloseHandle(info.Process);
                if (info.Thread != IntPtr.Zero) CloseHandle(info.Thread);
            }
            if (stdin != IntPtr.Zero) CloseHandle(stdin);
            if (stdout != IntPtr.Zero) CloseHandle(stdout);
            if (stderr != IntPtr.Zero) CloseHandle(stderr);
            CloseDesktop(desktop);
        }
    }
}
'@

Add-Type -TypeDefinition $source -Language CSharp

$quoted = @([char] 34 + $FilePath + [char] 34)
foreach ($argument in $ArgumentList) {
    $quoted += [char] 34 + ($argument -replace '"', '\"') + [char] 34
}
$commandLine = $quoted -join ' '

$code = [IsolatedSuiteProcess]::Run(
    $DesktopName, $FilePath, $commandLine, $WorkingDirectory, $StdoutPath, $StderrPath)
Write-Output $code
