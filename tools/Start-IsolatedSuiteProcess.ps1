#requires -Version 7.0
<#
.SYNOPSIS
    在不可见 Win32 桌面（CreateDesktopW）中启动一个 BelowNormal 优先级进程，
    标准输出/错误重定向到文件，等待退出并以唯一一行整数输出退出码。
    用于官方验证套件的窗口隔离：窗口在专用桌面对象中渲染，
    不出现在用户当前桌面，也不参与前台焦点竞争。
    隔离桌面无人可点：本 job 内进程弹出崩溃对话框（标题含 has stopped working / 已停止工作）时，
    以退出码 126 结束该进程，并把对话框标题与正文写进 StderrPath，不让已崩溃的进程挂到超时。
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
    [string] $DesktopName = 'MinminSuiteIso',
    [ValidateRange(1, 86400)] [int] $TimeoutSeconds = 1800,
    [string] $ProcessIdentityPath,
    [int] $OwnerProcessId = 0,
    [string] $OwnerStartedAtUtc
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$source = @'
using System;
using System.Collections.Generic;
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
    private const uint CreateSuspended = 0x00000004;
    private const int StartfUseStdHandles = 0x00000100;
    private const uint Infinite = 0xFFFFFFFF;
    private const uint WaitTimeout = 0x00000102;
    private const uint CrashDialogExitCode = 126;
    private const uint ProcessTerminate = 0x0001;
    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const uint Synchronize = 0x00100000;
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
    private static extern bool TerminateProcess(IntPtr process, uint exitCode);

    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters
    {
        public ulong ReadOperationCount, WriteOperationCount, OtherOperationCount;
        public ulong ReadTransferCount, WriteTransferCount, OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BasicLimitInformation
    {
        public long PerProcessUserTimeLimit, PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize, MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass, SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ExtendedLimitInformation
    {
        public BasicLimitInformation BasicLimitInformation;
        public IoCounters IoInfo;
        public UIntPtr ProcessMemoryLimit, JobMemoryLimit, PeakProcessMemoryUsed, PeakJobMemoryUsed;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateJobObjectW(IntPtr jobAttributes, string name);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetInformationJobObject(IntPtr job, int infoClass, ref ExtendedLimitInformation info, uint length);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint ResumeThread(IntPtr thread);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

    private delegate bool EnumWindowsProc(IntPtr window, IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern bool EnumDesktopWindows(IntPtr desktop, EnumWindowsProc callback, IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern bool EnumChildWindows(IntPtr parent, EnumWindowsProc callback, IntPtr parameter);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassNameW(IntPtr window, StringBuilder className, int maxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextW(IntPtr window, StringBuilder text, int maxCount);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr window);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint desiredAccess, bool inheritHandle, uint processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool IsProcessInJob(IntPtr process, IntPtr job, out bool result);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool WriteFile(IntPtr file, byte[] buffer, uint bytesToWrite, out uint bytesWritten, IntPtr overlapped);

    private static string ReadClassName(IntPtr window)
    {
        var buffer = new StringBuilder(256);
        GetClassNameW(window, buffer, buffer.Capacity);
        return buffer.ToString();
    }

    // 跨进程读标题与静态文本不向目标线程发消息，崩溃进程不响应也不会卡住这里。
    private static string ReadWindowText(IntPtr window)
    {
        var buffer = new StringBuilder(1024);
        GetWindowTextW(window, buffer, buffer.Capacity);
        return buffer.ToString();
    }

    // 崩溃对话框在隔离桌面上永远等不到点击，进程已崩溃却会挂到阶段超时。来源不止 WER：
    // 输入法等注入的第三方崩溃处理器（如 CrashRpt 在 CrashSender.exe 缺失时）也弹同类 MessageBox，
    // 错误模式与 WER 标志都管不到。只处理本 job 内进程，以免同名桌面上别的运行被误杀。
    private static void TerminateCrashDialogOwners(IntPtr desktop, IntPtr job, IntPtr diagnostics)
    {
        var dialogs = new List<KeyValuePair<uint, string>>();
        EnumDesktopWindows(desktop, (window, unused) =>
        {
            if (!IsWindowVisible(window) || ReadClassName(window) != "#32770") return true;
            var title = ReadWindowText(window);
            if (!title.EndsWith(" has stopped working", StringComparison.Ordinal) &&
                title.IndexOf("已停止工作", StringComparison.Ordinal) < 0) return true;
            uint processId;
            GetWindowThreadProcessId(window, out processId);
            var body = new List<string>();
            EnumChildWindows(window, (child, unusedChild) =>
            {
                var text = ReadWindowText(child);
                if (text.Length > 0 && ReadClassName(child) == "Static") body.Add(text);
                return true;
            }, IntPtr.Zero);
            dialogs.Add(new KeyValuePair<uint, string>(processId, "标题=\"" + title + "\" 正文=\"" + String.Join(" | ", body) + "\""));
            return true;
        }, IntPtr.Zero);

        foreach (var dialog in dialogs)
        {
            IntPtr process = OpenProcess(ProcessTerminate | ProcessQueryLimitedInformation | Synchronize, false, dialog.Key);
            if (process == IntPtr.Zero) continue;
            try
            {
                bool inJob;
                if (!IsProcessInJob(process, job, out inJob) || !inJob) continue;
                if (!TerminateProcess(process, CrashDialogExitCode)) continue;
                WaitForSingleObject(process, 5000);
                var line = "[Start-IsolatedSuiteProcess] 隔离桌面出现崩溃对话框，已以退出码 " + CrashDialogExitCode +
                    " 结束 pid=" + dialog.Key + "：" + dialog.Value + "\r\n";
                var bytes = new UTF8Encoding(false).GetBytes(line);
                uint written;
                WriteFile(diagnostics, bytes, (uint)bytes.Length, out written, IntPtr.Zero);
            }
            finally
            {
                CloseHandle(process);
            }
        }
    }

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

    private static bool IsOwnerAlive(int ownerProcessId, string ownerStartedAtUtc)
    {
        if (ownerProcessId <= 0 || String.IsNullOrWhiteSpace(ownerStartedAtUtc)) return true;
        try
        {
            using (var owner = System.Diagnostics.Process.GetProcessById(ownerProcessId))
            {
                if (owner.HasExited) return false;
                var expected = DateTime.Parse(ownerStartedAtUtc, null, System.Globalization.DateTimeStyles.RoundtripKind).ToUniversalTime();
                return owner.StartTime.ToUniversalTime().Ticks == expected.Ticks;
            }
        }
        catch (ArgumentException) { return false; }
        catch (InvalidOperationException) { return false; }
        catch (System.ComponentModel.Win32Exception) { return false; }
    }

    public static int Run(string desktopName, string application, string commandLine, string workingDirectory, string stdoutPath, string stderrPath, int timeoutSeconds, string identityPath, int ownerProcessId, string ownerStartedAtUtc)
    {
        IntPtr desktop = CreateDesktopW(desktopName, IntPtr.Zero, IntPtr.Zero, 0, GenericAll, IntPtr.Zero);
        if (desktop == IntPtr.Zero)
        {
            throw new InvalidOperationException("CreateDesktopW 失败 error=" + Marshal.GetLastWin32Error());
        }

        IntPtr stdin = IntPtr.Zero;
        IntPtr stdout = IntPtr.Zero;
        IntPtr stderr = IntPtr.Zero;
        IntPtr job = IntPtr.Zero;
        bool assignedJob = false;
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
            job = CreateJobObjectW(IntPtr.Zero, null);
            if (job == IntPtr.Zero)
            {
                throw new InvalidOperationException("CreateJobObjectW 失败 error=" + Marshal.GetLastWin32Error());
            }
            var limits = new ExtendedLimitInformation();
            limits.BasicLimitInformation.LimitFlags = 0x00002000; // JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
            if (!SetInformationJobObject(job, 9, ref limits, (uint)Marshal.SizeOf(typeof(ExtendedLimitInformation))))
            {
                throw new InvalidOperationException("SetInformationJobObject 失败 error=" + Marshal.GetLastWin32Error());
            }
            var mutableCommandLine = new StringBuilder(commandLine, 32768);
            bool created = CreateProcessW(application, mutableCommandLine, IntPtr.Zero, IntPtr.Zero, true,
                BelowNormalPriorityClass | CreateUnicodeEnvironment | CreateSuspended, IntPtr.Zero, workingDirectory, startup, info);
            if (!created)
            {
                throw new InvalidOperationException("CreateProcessW 失败: " + application + " error=" + Marshal.GetLastWin32Error());
            }
            if (!AssignProcessToJobObject(job, info.Process))
            {
                throw new InvalidOperationException("AssignProcessToJobObject 失败 error=" + Marshal.GetLastWin32Error());
            }
            assignedJob = true;
            if (!String.IsNullOrWhiteSpace(identityPath))
            {
                var started = System.Diagnostics.Process.GetProcessById(info.ProcessId).StartTime.ToUniversalTime();
                var identityJson = "{\"ProcessId\":" + info.ProcessId + ",\"StartedAtUtc\":\"" + started.ToString("o") + "\"}";
                var identityTempPath = identityPath + ".tmp-" + Guid.NewGuid().ToString("N");
                System.IO.File.WriteAllText(identityTempPath, identityJson, Encoding.UTF8);
                System.IO.File.Move(identityTempPath, identityPath, true);
            }
            if (ResumeThread(info.Thread) == 0xFFFFFFFF)
            {
                throw new InvalidOperationException("ResumeThread 失败 error=" + Marshal.GetLastWin32Error());
            }
            var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
            var nextCrashDialogScan = DateTime.UtcNow.AddSeconds(1);
            while (true)
            {
                if (!IsOwnerAlive(ownerProcessId, ownerStartedAtUtc))
                {
                    TerminateProcess(info.Process, 125);
                    WaitForSingleObject(info.Process, 5000);
                    return 125;
                }
                var remaining = deadline - DateTime.UtcNow;
                if (remaining <= TimeSpan.Zero)
                {
                    TerminateProcess(info.Process, 124);
                    WaitForSingleObject(info.Process, 5000);
                    return 124;
                }
                var waitResult = WaitForSingleObject(info.Process, (uint)Math.Min(250, Math.Max(1, (int)remaining.TotalMilliseconds)));
                if (waitResult == 0) break;
                if (waitResult != WaitTimeout)
                {
                    throw new InvalidOperationException("WaitForSingleObject 失败 error=" + Marshal.GetLastWin32Error());
                }
                if (DateTime.UtcNow >= nextCrashDialogScan)
                {
                    TerminateCrashDialogOwners(desktop, job, stderr);
                    nextCrashDialogScan = DateTime.UtcNow.AddSeconds(1);
                }
            }
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
                if (!assignedJob && info.Process != IntPtr.Zero)
                {
                    TerminateProcess(info.Process, 1);
                    WaitForSingleObject(info.Process, 5000);
                }
                if (info.Process != IntPtr.Zero) CloseHandle(info.Process);
                if (info.Thread != IntPtr.Zero) CloseHandle(info.Thread);
            }
            if (job != IntPtr.Zero) CloseHandle(job);
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
    $DesktopName, $FilePath, $commandLine, $WorkingDirectory, $StdoutPath, $StderrPath, $TimeoutSeconds, $ProcessIdentityPath, $OwnerProcessId, $OwnerStartedAtUtc)
Write-Output $code
