#requires -Version 7.4
<#
.SYNOPSIS
#288 本机真实安装验收探针：一个临时名称的真实计划任务加一个临时安装根，用新安装器 Install-CountdownProgram.ps1 做首次安装、
成功升级、人为制造的启动失败与自动回滚，经真实任务停止应用，记录程序目录被资源管理器打开时改名的实际表现，最后清理。
.DESCRIPTION
真实任务会在当前用户桌面上显示悬浮窗，资源管理器窗口也会打开一次，所以 -Mode Run 只在用户当场同意之后运行，并要求把
同意的原话传给 -Consent。其余模式不注册任务、不开窗口：
- Prepare：经真实 Deployer 把两份门禁构建生成并解压成完整包 v1（旧）与 v2（新）；在 v2 的程序目录上叠加故障注入构建里变了的文件，
  按构建脚本同一算法重写 v3 的载荷清单，再经同一 Deployer 生成完整 v3 包；用 v2 的无界面迁移命令生成三份互不相同的当前 schema 设置，
  作为安装根里的设置与两代备份。
- DryRun：不需要真实任务的每一步：现场指纹、安装器 -CheckOnly（三个包）、三个候选 EXE 在设置副本上的迁移与等价判定、
  生产 port 的只读一侧（交互会话、桌面目录、读桌面快捷方式）与在运行目录里写、读、删一个快捷方式、进程事实读取、
  资源管理器窗口列表。
- Run：真实验收（R0–R7，见 docs/evidence/clock-countdown-issue-288/README.md）。
- Cleanup：结束运行目录里的残留进程，经生产 RestoreTask port 注销临时任务，关掉位置在运行目录里的资源管理器窗口，记录现场的
  事后指纹。运行目录随后经主机的受管回收移入回收站，不在本脚本里。
原始测量写进 -OutputDirectory（放在运行目录之外，回收之后仍在），提交前另行脱敏汇编。本脚本只结束映像路径在运行目录里的进程
（结束前按 PID 重读映像路径），只关闭位置在运行目录里的资源管理器窗口，只改动名为 MinminCountdown-Check-288-* 的任务；
桌面上同名快捷方式不存在或指向运行目录时 Run 直接拒绝，因为那时安装器会在真实桌面上新建或改写它。
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [ValidateSet('Prepare', 'DryRun', 'Run', 'Cleanup')] [string] $Mode,
    [Parameter(Mandatory)] [string] $RunDirectory,
    [Parameter(Mandatory)] [string] $OutputDirectory,
    [string] $PreviousApp,
    [string] $CurrentApp,
    [string] $FaultInjectedPublish,
    [string] $Consent
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)

$script:RepoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$script:RunDirectory = [System.IO.Path]::TrimEndingDirectorySeparator([System.IO.Path]::GetFullPath($RunDirectory))
$script:OutputDirectory = [System.IO.Path]::GetFullPath($OutputDirectory)
$script:Utf8 = [System.Text.UTF8Encoding]::new($false)
$script:StatePath = Join-Path $script:RunDirectory 'check-state.json'
$script:InstallerEntry = Join-Path $script:RepoRoot 'Install-CountdownProgram.ps1'
$script:StatusEntry = Join-Path $script:RepoRoot 'Get-CountdownInstallStatus.ps1'
$script:ShortcutName = '敏敏子倒计时.lnk'
$script:RealTaskNames = @('Minmin Countdown', '敏敏子倒计时')
$script:SettingsNames = @('settings.json', 'settings.json.backup1', 'settings.json.backup2')
$leaf = [System.IO.Path]::GetFileName($script:RunDirectory)
if ($leaf -notmatch '^minmin-288-([0-9a-f]{8})$') { throw "运行目录的名字必须是 minmin-288-<8 位十六进制>：$leaf" }
$script:RunId = $Matches[1]
$script:TaskName = "MinminCountdown-Check-288-$($script:RunId)"
$script:InstallRoot = Join-Path $script:RunDirectory 'root'
$script:Packages = [ordered]@{
    v1 = Join-Path $script:RunDirectory 'packages\v1'
    v2 = Join-Path $script:RunDirectory 'packages\v2'
    v3 = Join-Path $script:RunDirectory 'packages\v3'
}
Import-Module (Join-Path $script:RepoRoot 'Countdown.Installer.psm1') -Force
$script:Ports = New-CountdownInstallerWindowsPorts

if (-not ('Countdown288Native' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
using System.Text;

public sealed class Countdown288ProcessFacts
{
    public string ImagePath;
    public int SessionId = -1;
    public uint PriorityClass;
    public int IoPriority = -1;
    public int PagePriority = -1;
    public bool? InJob;
    public bool? TokenElevated;
    public int ElevationType = -1;
    public uint IntegrityRid;
    public string Error;
}

// #288: read-only facts about one process of the same user. Nothing here writes to the process.
public static class Countdown288Native
{
    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr OpenProcess(uint access, bool inherit, int processId);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool CloseHandle(IntPtr handle);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern uint GetPriorityClass(IntPtr process);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool IsProcessInJob(IntPtr process, IntPtr job, out bool result);
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)] private static extern bool QueryFullProcessImageNameW(IntPtr process, uint flags, StringBuilder name, ref uint size);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool ProcessIdToSessionId(uint processId, out uint sessionId);
    [DllImport("ntdll.dll")] private static extern int NtQueryInformationProcess(IntPtr process, int infoClass, out int info, int length, out int returnLength);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool GetTokenInformation(IntPtr token, int infoClass, IntPtr info, int length, out int returnLength);
    [DllImport("advapi32.dll")] private static extern IntPtr GetSidSubAuthority(IntPtr sid, uint index);
    [DllImport("advapi32.dll")] private static extern IntPtr GetSidSubAuthorityCount(IntPtr sid);

    public static Countdown288ProcessFacts Query(int processId)
    {
        var facts = new Countdown288ProcessFacts();
        uint session;
        if (ProcessIdToSessionId((uint)processId, out session)) { facts.SessionId = (int)session; }
        var handle = OpenProcess(0x0400 | 0x1000, false, processId);
        if (handle == IntPtr.Zero) { handle = OpenProcess(0x1000, false, processId); }
        if (handle == IntPtr.Zero) { facts.Error = "OpenProcess failed: " + Marshal.GetLastWin32Error(); return facts; }
        try
        {
            var buffer = new StringBuilder(1024);
            uint size = (uint)buffer.Capacity;
            if (QueryFullProcessImageNameW(handle, 0, buffer, ref size)) { facts.ImagePath = buffer.ToString(0, (int)size); }
            facts.PriorityClass = GetPriorityClass(handle);
            bool inJob;
            if (IsProcessInJob(handle, IntPtr.Zero, out inJob)) { facts.InJob = inJob; }
            int value;
            int returned;
            if (NtQueryInformationProcess(handle, 33, out value, 4, out returned) == 0) { facts.IoPriority = value; }
            if (NtQueryInformationProcess(handle, 39, out value, 4, out returned) == 0) { facts.PagePriority = value; }
            IntPtr token;
            if (OpenProcessToken(handle, 0x0008, out token))
            {
                var info = Marshal.AllocHGlobal(256);
                try
                {
                    if (GetTokenInformation(token, 20, info, 4, out returned)) { facts.TokenElevated = Marshal.ReadInt32(info) != 0; }
                    if (GetTokenInformation(token, 18, info, 256, out returned)) { facts.ElevationType = Marshal.ReadInt32(info); }
                    if (GetTokenInformation(token, 25, info, 256, out returned))
                    {
                        var sid = Marshal.ReadIntPtr(info);
                        int count = Marshal.ReadByte(GetSidSubAuthorityCount(sid));
                        facts.IntegrityRid = (uint)Marshal.ReadInt32(GetSidSubAuthority(sid, (uint)(count - 1)));
                    }
                }
                finally { Marshal.FreeHGlobal(info); CloseHandle(token); }
            }
        }
        finally { CloseHandle(handle); }
        return facts;
    }
}
'@
}

function Get-CheckUtc { [DateTimeOffset]::UtcNow.ToString('o') }

function Get-CheckTextSha256 {
    param([AllowEmptyString()] [string] $Text)
    [Convert]::ToHexString([System.Security.Cryptography.SHA256]::HashData($script:Utf8.GetBytes($Text)))
}

function Save-CheckJson {
    param([Parameter(Mandatory)] [string] $Name, [Parameter(Mandatory)] [object] $Value)
    [void] [System.IO.Directory]::CreateDirectory($script:OutputDirectory)
    $path = Join-Path $script:OutputDirectory $Name
    [System.IO.File]::WriteAllText($path, ($Value | ConvertTo-Json -Depth 16), $script:Utf8)
    $path
}

function Test-CheckPathInRun {
    param([AllowNull()] [string] $Path)
    if ([string]::IsNullOrWhiteSpace($Path)) { return $false }
    ([System.IO.Path]::GetFullPath($Path)).StartsWith($script:RunDirectory + '\', [System.StringComparison]::OrdinalIgnoreCase)
}

function Get-CheckAppProcesses {
    @(Get-CimInstance Win32_Process -Filter "Name='MinminCountdown.exe'" -OperationTimeoutSec 10 | ForEach-Object {
            [pscustomobject]@{ ProcessId = [int] $_.ProcessId; Path = [string] $_.ExecutablePath; SessionId = [int] $_.SessionId; InRun = (Test-CheckPathInRun -Path ([string] $_.ExecutablePath)) }
        })
}

# 本探针结束进程的唯一入口：按 PID 重读映像路径，不在运行目录里就拒绝。
function Stop-CheckRunProcess {
    param([Parameter(Mandatory)] [int] $Id)
    $process = Get-CimInstance Win32_Process -Filter "ProcessId=$Id" -OperationTimeoutSec 10
    if ($null -eq $process) { return 'already-exited' }
    if ([string] $process.Name -ne 'MinminCountdown.exe' -or -not (Test-CheckPathInRun -Path ([string] $process.ExecutablePath))) {
        throw "拒绝结束 PID ${Id}：映像不是运行目录里的 MinminCountdown.exe。"
    }
    Stop-Process -Id $Id -Force
    'stopped'
}

function Get-CheckProcessFacts {
    param([Parameter(Mandatory)] [int] $Id)
    $native = [Countdown288Native]::Query($Id)
    $cim = Get-CimInstance Win32_Process -Filter "ProcessId=$Id" -OperationTimeoutSec 10
    $parent = if ($null -ne $cim) { Get-CimInstance Win32_Process -Filter "ProcessId=$([int] $cim.ParentProcessId)" -OperationTimeoutSec 10 } else { $null }
    $classNames = @{ 0x40 = 'Idle'; 0x4000 = 'BelowNormal'; 0x20 = 'Normal'; 0x8000 = 'AboveNormal'; 0x80 = 'High'; 0x100 = 'RealTime' }
    $ioNames = @{ 0 = 'VeryLow'; 1 = 'Low'; 2 = 'Normal'; 3 = 'High'; 4 = 'Critical' }
    $elevationNames = @{ 1 = 'Default'; 2 = 'Full'; 3 = 'Limited' }
    [ordered]@{
        atUtc = Get-CheckUtc
        processId = $Id
        imagePath = $native.ImagePath
        sessionId = $native.SessionId
        priorityClass = if ($classNames.ContainsKey([int] $native.PriorityClass)) { $classNames[[int] $native.PriorityClass] } else { '0x{0:X}' -f $native.PriorityClass }
        ioPriority = if ($native.IoPriority -ge 0) { $ioNames[$native.IoPriority] } else { $null }
        memoryPriority = if ($native.PagePriority -ge 0) { $native.PagePriority } else { $null }
        inJob = $native.InJob
        tokenElevated = $native.TokenElevated
        elevationType = if ($elevationNames.ContainsKey($native.ElevationType)) { $elevationNames[$native.ElevationType] } else { [string] $native.ElevationType }
        integrityRid = '0x{0:X}' -f $native.IntegrityRid
        parentProcessName = if ($null -ne $parent) { [string] $parent.Name } elseif ($null -ne $cim) { 'exited' } else { $null }
        startTimeUtc = if ($null -ne $cim -and $cim.CreationDate) { ([datetime] $cim.CreationDate).ToUniversalTime().ToString('o') } else { $null }
        imageSha256 = if ($native.ImagePath) { (Get-FileHash -LiteralPath $native.ImagePath -Algorithm SHA256).Hash } else { $null }
        nativeError = $native.Error
    }
}

function Get-CheckTaskFacts {
    param([Parameter(Mandatory)] [string] $Name)
    try { $task = Get-ScheduledTask -TaskPath '\' -TaskName $Name -ErrorAction Stop }
    catch { if ($_.CategoryInfo.Category -eq 'ObjectNotFound') { return [ordered]@{ atUtc = Get-CheckUtc; present = $false } }; throw }
    $info = Get-ScheduledTaskInfo -TaskPath '\' -TaskName $Name -ErrorAction Stop
    $xml = [string] (Export-ScheduledTask -TaskPath '\' -TaskName $Name -ErrorAction Stop)
    $service = New-Object -ComObject 'Schedule.Service'
    $service.Connect()
    $instances = @(foreach ($instance in $service.GetFolder('\').GetTask($Name).GetInstances(0)) { [int] $instance.EnginePID })
    $lastResult = ([long] $info.LastTaskResult) -band 0xFFFFFFFFL
    [ordered]@{
        atUtc = Get-CheckUtc
        present = $true
        state = [string] $task.State
        lastRunTimeUtc = if ($info.LastRunTime) { ([datetime] $info.LastRunTime).ToUniversalTime().ToString('o') } else { $null }
        lastTaskResultHex = '0x{0:X8}' -f [uint32] $lastResult
        runningEnginePids = $instances
        priority = [int] $task.Settings.Priority
        triggers = @($task.Triggers | Where-Object { $_ } | ForEach-Object { ([string] $_.CimClass.CimClassName) -replace '^MSFT_Task' })
        logonType = [string] $task.Principal.LogonType
        runLevel = [string] $task.Principal.RunLevel
        actions = @($task.Actions | Where-Object { $_ } | ForEach-Object { [ordered]@{ execute = [string] $_.Execute; arguments = [string] $_.Arguments; workingDirectory = [string] $_.WorkingDirectory } })
        executionTimeLimit = [string] $task.Settings.ExecutionTimeLimit
        multipleInstances = [string] $task.Settings.MultipleInstances
        disallowStartIfOnBatteries = [bool] $task.Settings.DisallowStartIfOnBatteries
        stopIfGoingOnBatteries = [bool] $task.Settings.StopIfGoingOnBatteries
        xmlSha256 = Get-CheckTextSha256 $xml
        xml = $xml
    }
}

# 把临时任务改得与期望定义不一致（只改优先级），让安装器走导出、更新与回滚恢复；改不了时记下原因，验收照常继续。
function Set-CheckTaskPriority {
    param([Parameter(Mandatory)] [int] $Priority)
    if (-not $script:TaskName.StartsWith('MinminCountdown-Check-288-', [System.StringComparison]::Ordinal)) { throw '只改临时任务。' }
    try {
        $task = Get-ScheduledTask -TaskPath '\' -TaskName $script:TaskName -ErrorAction Stop
        $task.Settings.Priority = $Priority
        Set-ScheduledTask -InputObject $task -ErrorAction Stop | Out-Null
        [ordered]@{ requested = $Priority; actual = [int] (Get-ScheduledTask -TaskPath '\' -TaskName $script:TaskName -ErrorAction Stop).Settings.Priority; error = $null }
    }
    catch { [ordered]@{ requested = $Priority; actual = $null; error = [string] $_.Exception.Message } }
}

function Get-CheckFileFingerprint {
    param([Parameter(Mandatory)] [string] $Path)
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { return [ordered]@{ present = $false } }
    $item = Get-Item -LiteralPath $Path -Force
    [ordered]@{
        present = $true
        length = $item.Length
        sha256 = (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash
        creationTimeUtc = $item.CreationTimeUtc.ToString('o')
        lastWriteTimeUtc = $item.LastWriteTimeUtc.ToString('o')
        lastAccessTimeUtc = $item.LastAccessTimeUtc.ToString('o')
        attributes = [string] $item.Attributes
    }
}

function Get-CheckSettingsFamily {
    $family = [ordered]@{ atUtc = Get-CheckUtc }
    foreach ($name in $script:SettingsNames) {
        $path = Join-Path $script:InstallRoot $name
        $family[$name] = if (Test-Path -LiteralPath $path -PathType Leaf) {
            [ordered]@{ present = $true; length = (Get-Item -LiteralPath $path).Length; sha256 = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash; lastWriteTimeUtc = (Get-Item -LiteralPath $path).LastWriteTimeUtc.ToString('o') }
        }
        else { [ordered]@{ present = $false } }
    }
    $family
}

function Compare-CheckSettingsWithSeed {
    $seed = Join-Path $script:RunDirectory 'seed'
    $rows = foreach ($name in $script:SettingsNames) {
        $live = Join-Path $script:InstallRoot $name
        $saved = Join-Path $seed $name
        $same = (Test-Path -LiteralPath $live -PathType Leaf) -and
            [System.Linq.Enumerable]::SequenceEqual([byte[]] [System.IO.File]::ReadAllBytes($live), [byte[]] [System.IO.File]::ReadAllBytes($saved))
        [ordered]@{ name = $name; byteIdenticalToSeed = $same }
    }
    [ordered]@{ allByteIdentical = (@($rows | Where-Object { -not $_.byteIdenticalToSeed }).Count -eq 0); files = @($rows) }
}

function Get-CheckRootListing {
    if (-not (Test-Path -LiteralPath $script:InstallRoot)) { return @() }
    @(Get-ChildItem -LiteralPath $script:InstallRoot -Force | Sort-Object Name | ForEach-Object { if ($_.PSIsContainer) { $_.Name + '\' } else { $_.Name } })
}

function Get-CheckManifestFacts {
    param([Parameter(Mandatory)] [string] $AppDirectory)
    $path = Join-Path $AppDirectory 'published-payload.json'
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { return $null }
    $document = [System.IO.File]::ReadAllText($path) | ConvertFrom-Json
    $exe = @($document.files | Where-Object { [string] $_.path -eq 'app\MinminCountdown.exe' })
    [ordered]@{ commit = [string] $document.commit; aggregateSha256 = [string] $document.aggregateSha256; fileCount = @($document.files).Count; exeSha256 = if ($exe.Count -eq 1) { [string] $exe[0].sha256 } else { $null } }
}

# 桌面快捷方式按安装器同一个 port 找：交互会话登录用户的桌面目录。
function Get-CheckDesktopShortcut {
    $session = & $script:Ports.GetInteractiveSessions
    $sessions = @($session | Where-Object { $null -ne $_ })
    if ($sessions.Count -ne 1) { throw "交互会话不是恰好一个（$($sessions.Count)）。" }
    $desktop = & $script:Ports.GetDesktopDirectory $sessions[0]
    $path = Join-Path $desktop $script:ShortcutName
    $read = & $script:Ports.ReadShortcut $path
    [ordered]@{
        sessionId = [int] $sessions[0].SessionId
        path = $path
        fingerprint = Get-CheckFileFingerprint -Path $path
        isShellLink = if ($null -ne $read) { [bool] $read.IsShellLink } else { $null }
        targetInRunDirectory = if ($null -ne $read -and [bool] $read.IsShellLink) { Test-CheckPathInRun -Path ([string] $read.TargetPath) } else { $null }
        targetIsThisInstall = if ($null -ne $read -and [bool] $read.IsShellLink) { [string]::Equals(([string] $read.TargetPath).Trim('"'), (Join-Path $script:InstallRoot 'app\MinminCountdown.exe'), [System.StringComparison]::OrdinalIgnoreCase) } else { $null }
    }
}

function Get-CheckRealWorld {
    $named = @(foreach ($name in $script:RealTaskNames) {
            try { [ordered]@{ name = $name; present = $true; xmlSha256 = Get-CheckTextSha256 ([string] (Export-ScheduledTask -TaskPath '\' -TaskName $name -ErrorAction Stop)) } }
            catch { [ordered]@{ name = $name; present = $false; error = [string] $_.CategoryInfo.Category } }
        })
    $rows = @(foreach ($task in @(Get-ScheduledTask -TaskPath '\' -ErrorAction Stop | Where-Object { $_.TaskName -ne $script:TaskName } | Sort-Object TaskName)) {
            $hash = $null
            try { $hash = Get-CheckTextSha256 ([string] (Export-ScheduledTask -TaskPath '\' -TaskName $task.TaskName -ErrorAction Stop)) } catch { $hash = 'unexportable' }
            '{0}|{1}' -f $task.TaskName, $hash
        })
    [ordered]@{
        atUtc = Get-CheckUtc
        realTasks = $named
        rootFolderTaskCount = $rows.Count
        rootFolderAggregateSha256 = Get-CheckTextSha256 ($rows -join "`n")
        realInstallRootPresent = Test-Path -LiteralPath 'D:\敏敏子倒计时'
        desktopShortcut = Get-CheckDesktopShortcut
        appProcessesOutsideRun = @(Get-CheckAppProcesses | Where-Object { -not $_.InRun }).Count
        appProcessesInRun = @(Get-CheckAppProcesses | Where-Object { $_.InRun } | ForEach-Object { $_.ProcessId })
    }
}

function Get-CheckShellWindows {
    $shell = New-Object -ComObject Shell.Application
    @(foreach ($window in @($shell.Windows())) {
            $path = $null
            try { $path = [string] $window.Document.Folder.Self.Path } catch { $path = $null }
            [pscustomobject]@{ Hwnd = [long] $window.HWND; Path = $path; InRun = (Test-CheckPathInRun -Path $path) -or [string]::Equals($path, $script:RunDirectory, [System.StringComparison]::OrdinalIgnoreCase) }
        })
}

function Close-CheckRunShellWindows {
    $shell = New-Object -ComObject Shell.Application
    $closed = 0
    foreach ($window in @($shell.Windows())) {
        $path = $null
        try { $path = [string] $window.Document.Folder.Self.Path } catch { $path = $null }
        if ((Test-CheckPathInRun -Path $path) -or [string]::Equals($path, $script:RunDirectory, [System.StringComparison]::OrdinalIgnoreCase)) {
            $window.Quit()
            $closed++
        }
    }
    $closed
}

function Read-CheckDiagnostics {
    param([int[]] $ProcessIds = @())
    $log = Join-Path $script:InstallRoot 'logs\application.jsonl'
    if (-not (Test-Path -LiteralPath $log -PathType Leaf)) { return @() }
    @(Get-Content -LiteralPath $log -Encoding utf8 | Where-Object { $_ } | ForEach-Object { $_ | ConvertFrom-Json -DateKind String } |
            Where-Object { $ProcessIds.Count -eq 0 -or $ProcessIds -contains [int] $_.processId } |
            ForEach-Object {
                $entry = $_
                $row = [ordered]@{ timestampUtc = [string] $entry.timestampUtc; level = [string] $entry.level; event = [string] $entry.event; processId = [int] $entry.processId; sessionId = [int] $entry.sessionId }
                foreach ($name in @('exceptionType', 'exceptionMessage', 'hResult')) {
                    $property = $entry.PSObject.Properties[$name]
                    if ($property) { $row[$name] = $property.Value }
                }
                $row
            })
}

function Invoke-CheckStatus {
    $output = @(& pwsh -NoLogo -NoProfile -NonInteractive -File $script:StatusEntry -InstallRoot $script:InstallRoot -TaskName $script:TaskName)
    [ordered]@{ exitCode = $LASTEXITCODE; json = (($output -join "`n") | ConvertFrom-Json -DateKind String) }
}

# 安装器在子进程里运行，与生产用法相同；旁边一个线程每 50 毫秒记一次安装根的目录状态、设置文件与运行目录里的应用进程，
# 并替每个新出现的进程保留句柄，事后读它的退出码。
function Invoke-CheckInstaller {
    param([Parameter(Mandatory)] [string] $Package, [switch] $CheckOnly, [string] $InstallRoot = $script:InstallRoot)
    $sync = [hashtable]::Synchronized(@{ Stop = $false })
    $sampler = $null
    if (-not $CheckOnly) {
        $sampler = Start-ThreadJob -ArgumentList $sync, $InstallRoot, $script:RunDirectory -ScriptBlock {
            param($Sync, [string] $Root, [string] $Run)
            $clock = [Diagnostics.Stopwatch]::StartNew()
            $events = [System.Collections.Generic.List[object]]::new()
            $handles = @{}
            $last = ''
            $names = @('app', 'app.candidate', 'app.previous', 'install-transaction.json', 'install-transaction', 'runtime-status.json')
            $settings = @('settings.json', 'settings.json.backup1', 'settings.json.backup2')
            while (-not $Sync.Stop) {
                $present = @($names | Where-Object { Test-Path -LiteralPath (Join-Path $Root $_) })
                $stamps = @(foreach ($name in $settings) {
                        $path = Join-Path $Root $name
                        if (Test-Path -LiteralPath $path -PathType Leaf) { '{0}:{1}' -f $name, [System.IO.File]::GetLastWriteTimeUtc($path).Ticks } else { "${name}:absent" }
                    })
                $processes = @(Get-Process -Name 'MinminCountdown' -ErrorAction SilentlyContinue | Where-Object {
                        $path = $null
                        try { $path = $_.Path } catch { $path = $null }
                        $path -and $path.StartsWith($Run + '\', [StringComparison]::OrdinalIgnoreCase)
                    })
                foreach ($process in $processes) {
                    if (-not $handles.ContainsKey($process.Id)) {
                        try { $null = $process.Handle; $handles[$process.Id] = [ordered]@{ process = $process; firstSeenMs = $clock.ElapsedMilliseconds; path = $process.Path } } catch { }
                    }
                }
                $state = ($present -join ',') + '|' + ($stamps -join ',') + '|' + (@($processes | ForEach-Object { $_.Id } | Sort-Object) -join ',')
                if ($state -ne $last) {
                    $events.Add([ordered]@{ ms = $clock.ElapsedMilliseconds; present = $present; settingsStamps = $stamps; processIds = @($processes | ForEach-Object { $_.Id } | Sort-Object) })
                    $last = $state
                }
                Start-Sleep -Milliseconds 50
            }
            $exits = @(foreach ($id in $handles.Keys) {
                    $entry = $handles[$id]
                    $process = $entry.process
                    $exited = $process.WaitForExit(0)
                    [ordered]@{
                        processId = [int] $id
                        firstSeenMs = $entry.firstSeenMs
                        imageInRoot = ([string] $entry.path).StartsWith($Root + '\', [StringComparison]::OrdinalIgnoreCase)
                        exited = $exited
                        exitCodeHex = if ($exited) { '0x{0:X8}' -f [uint32] ($process.ExitCode -band 0xFFFFFFFFL) } else { $null }
                        exitTimeUtc = if ($exited) { $process.ExitTime.ToUniversalTime().ToString('o') } else { $null }
                    }
                })
            [pscustomobject]@{ Timeline = $events.ToArray(); Processes = $exits }
        }
    }
    $arguments = @('-NoLogo', '-NoProfile', '-NonInteractive', '-File', $script:InstallerEntry, '-PackageRoot', $Package, '-InstallRoot', $InstallRoot, '-TaskName', $script:TaskName)
    if ($CheckOnly) { $arguments += '-CheckOnly' }
    $started = Get-CheckUtc
    $clock = [System.Diagnostics.Stopwatch]::StartNew()
    $output = @(& pwsh @arguments 2>&1)
    $exitCode = $LASTEXITCODE
    $elapsed = $clock.ElapsedMilliseconds
    $sampled = $null
    if ($null -ne $sampler) {
        Start-Sleep -Milliseconds 300
        $sync.Stop = $true
        $sampled = Receive-Job -Job $sampler -Wait -AutoRemoveJob
    }
    $text = @($output | Where-Object { $_ -isnot [System.Management.Automation.ErrorRecord] } | ForEach-Object { "$_" }) -join "`n"
    $errorText = @($output | Where-Object { $_ -is [System.Management.Automation.ErrorRecord] } | ForEach-Object { "$_" }) -join "`n"
    $result = $null
    try { $result = $text | ConvertFrom-Json -DateKind String } catch { $result = $null }
    [ordered]@{
        package = Split-Path -Leaf $Package
        checkOnly = [bool] $CheckOnly
        startedUtc = $started
        elapsedMs = $elapsed
        exitCode = $exitCode
        result = $result
        rawOutput = if ($null -eq $result) { $text } else { $null }
        stderr = if ($errorText) { $errorText } else { $null }
        timeline = if ($null -ne $sampled) { $sampled.Timeline } else { $null }
        processesSeen = if ($null -ne $sampled) { $sampled.Processes } else { $null }
    }
}

function Get-CheckState {
    if (-not (Test-Path -LiteralPath $script:StatePath -PathType Leaf)) { throw "没有 check-state.json，先跑 -Mode Prepare：$script:StatePath" }
    [System.IO.File]::ReadAllText($script:StatePath) | ConvertFrom-Json -DateKind String
}

function Invoke-CheckPrepare {
    foreach ($required in @($PreviousApp, $CurrentApp, $FaultInjectedPublish)) {
        if ([string]::IsNullOrWhiteSpace($required) -or -not (Test-Path -LiteralPath $required -PathType Container)) { throw "Prepare 需要 -PreviousApp、-CurrentApp、-FaultInjectedPublish 三个目录：$required" }
    }
    if (Test-Path -LiteralPath $script:StatePath) { throw 'Prepare 只跑一次：check-state.json 已存在。' }
    $localPackageEntry = Join-Path $script:RepoRoot 'tools\New-CountdownLocalInstallPackage.ps1'
    [void] [System.IO.Directory]::CreateDirectory((Join-Path $script:RunDirectory 'packages'))
    foreach ($pair in @(@('v1', $PreviousApp), @('v2', $CurrentApp))) {
        & $localPackageEntry -AppDirectory $pair[1] -OutputDirectory $script:Packages[$pair[0]] | Out-Null
    }
    # v3：先构造故障注入 app 并重写载荷清单，再由同一真实 Deployer 生成完整包。
    $v2App = Join-Path $script:Packages.v2 'app'
    $v3WorkRoot = Join-Path $script:RunDirectory 'prepare-v3-source'
    $v3SourceApp = Join-Path $v3WorkRoot 'app'
    [void] [System.IO.Directory]::CreateDirectory($v3WorkRoot)
    Copy-Item -LiteralPath $v2App -Destination $v3SourceApp -Recurse
    $v2Manifest = [System.IO.File]::ReadAllText((Join-Path $v2App 'published-payload.json')) | ConvertFrom-Json
    $overlaid = [System.Collections.Generic.List[string]]::new()
    $entries = [System.Collections.Generic.List[object]]::new()
    $aggregateInput = [System.Text.StringBuilder]::new()
    foreach ($entry in @($v2Manifest.files)) {
        $relative = ([string] $entry.path).Substring(4)
        $injected = Join-Path $FaultInjectedPublish $relative
        if ((Test-Path -LiteralPath $injected -PathType Leaf) -and (Get-FileHash -LiteralPath $injected -Algorithm SHA256).Hash -ne [string] $entry.sha256) {
            Copy-Item -LiteralPath $injected -Destination (Join-Path $v3SourceApp $relative) -Force
            $overlaid.Add($relative)
        }
        $hash = (Get-FileHash -LiteralPath (Join-Path $v3SourceApp $relative) -Algorithm SHA256).Hash
        $entries.Add([ordered]@{ path = [string] $entry.path; sha256 = $hash })
        [void] $aggregateInput.Append([string] $entry.path).Append([char] 0).Append($hash).Append([char] 10)
    }
    if ($overlaid.Count -eq 0) { throw '故障注入构建与 v2 没有任何不同的文件。' }
    $v3Manifest = [ordered]@{
        schemaVersion = 1
        commit = "$($v2Manifest.commit)-fault-injected-288"
        aggregateSha256 = [Convert]::ToHexString([System.Security.Cryptography.SHA256]::HashData([System.Text.Encoding]::UTF8.GetBytes($aggregateInput.ToString())))
        files = $entries.ToArray()
    }
    [System.IO.File]::WriteAllText((Join-Path $v3SourceApp 'published-payload.json'), ($v3Manifest | ConvertTo-Json -Depth 6), $script:Utf8)
    & $localPackageEntry -AppDirectory $v3SourceApp -OutputDirectory $script:Packages.v3 | Out-Null
    $v3App = Join-Path $script:Packages.v3 'app'
    [System.IO.Directory]::Delete($v3WorkRoot, $true)

    # 三份互不相同的当前 schema 设置：v7 夹具与它的两个改了一条便签文字的变体，各自在隔离目录里经 v2 的无界面迁移命令迁移。
    $fixture = Join-Path $script:RepoRoot 'src\MinminCountdown.Verification\Fixtures\powershell-v7.settings.json'
    $fixtureText = [System.IO.File]::ReadAllText($fixture)
    $seed = Join-Path $script:RunDirectory 'seed'
    [void] [System.IO.Directory]::CreateDirectory($seed)
    $generated = [ordered]@{}
    $variants = [ordered]@{
        'settings.json' = $fixtureText
        'settings.json.backup1' = $fixtureText.Replace('保留历史便签', '保留历史便签（第一代备份）')
        'settings.json.backup2' = $fixtureText.Replace('保留历史便签', '保留历史便签（第二代备份）')
    }
    foreach ($name in $variants.Keys) {
        $work = Join-Path $seed ('gen-' + $name)
        [void] [System.IO.Directory]::CreateDirectory($work)
        $file = Join-Path $work 'settings.json'
        [System.IO.File]::WriteAllText($file, $variants[$name], $script:Utf8)
        $run = & $script:Ports.RunHeadlessCommand (Join-Path $v2App 'MinminCountdown.exe') @('--settings-migration', '--settings-file', $file) $work
        $report = ([string] $run.Output) | ConvertFrom-Json
        if ([int] $run.ExitCode -ne 0 -or $report.succeeded -ne $true -or $report.migrated -ne $true) { throw "生成 $name 失败：退出码 $($run.ExitCode)" }
        Copy-Item -LiteralPath $file -Destination (Join-Path $seed $name)
        $generated[$name] = [ordered]@{ exitCode = [int] $run.ExitCode; migrated = $report.migrated; sha256 = (Get-FileHash -LiteralPath (Join-Path $seed $name) -Algorithm SHA256).Hash }
    }
    if (@($generated.Values | ForEach-Object { $_.sha256 } | Sort-Object -Unique).Count -ne 3) { throw '三份设置不是互不相同。' }
    [void] [System.IO.Directory]::CreateDirectory($script:InstallRoot)
    foreach ($name in $script:SettingsNames) { Copy-Item -LiteralPath (Join-Path $seed $name) -Destination (Join-Path $script:InstallRoot $name) }

    $state = [ordered]@{
        runId = $script:RunId
        taskName = $script:TaskName
        installRoot = $script:InstallRoot
        preparedUtc = Get-CheckUtc
        packages = [ordered]@{
            v1 = Get-CheckManifestFacts -AppDirectory (Join-Path $script:Packages.v1 'app')
            v2 = Get-CheckManifestFacts -AppDirectory $v2App
            v3 = Get-CheckManifestFacts -AppDirectory $v3App
        }
        v3OverlaidFiles = $overlaid.ToArray()
        seed = $generated
    }
    [System.IO.File]::WriteAllText($script:StatePath, ($state | ConvertTo-Json -Depth 8), $script:Utf8)
    Save-CheckJson -Name 'prepare.json' -Value $state | Out-Null
    $state
}

function Invoke-CheckDryRun {
    $state = Get-CheckState
    $identity = [System.Security.Principal.WindowsIdentity]::GetCurrent()
    $result = [ordered]@{
        mode = 'DryRun'
        startedUtc = Get-CheckUtc
        environment = [ordered]@{
            osBuild = [string] (Get-CimInstance Win32_OperatingSystem).BuildNumber
            pwsh = $PSVersionTable.PSVersion.ToString()
            shellElevated = ([System.Security.Principal.WindowsPrincipal]::new($identity)).IsInRole([System.Security.Principal.WindowsBuiltInRole]::Administrator)
            apartment = [string] [System.Threading.Thread]::CurrentThread.GetApartmentState()
            kryptexProcesses = @(Get-Process -Name 'kryptex_*' -ErrorAction SilentlyContinue).Count
            tempTaskPresent = (Get-CheckTaskFacts -Name $script:TaskName).present
        }
        realWorld = Get-CheckRealWorld
        packages = $state.packages
    }
    $result.checkOnly = [ordered]@{}
    foreach ($name in @('v1', 'v2', 'v3')) { $result.checkOnly[$name] = Invoke-CheckInstaller -Package $script:Packages[$name] -CheckOnly }
    # 安装器演练迁移的同一组命令，在设置副本上逐个候选跑一遍（与 Invoke-CountdownSettingsRehearsal 同参数）。
    $result.rehearsal = [ordered]@{}
    foreach ($name in @('v1', 'v2', 'v3')) {
        $work = Join-Path $script:RunDirectory "dryrun-rehearsal-$name"
        [void] [System.IO.Directory]::CreateDirectory($work)
        foreach ($settingsName in $script:SettingsNames) { Copy-Item -LiteralPath (Join-Path $script:InstallRoot $settingsName) -Destination (Join-Path $work $settingsName) -Force }
        $exe = Join-Path $script:Packages[$name] 'app\MinminCountdown.exe'
        $migration = & $script:Ports.RunHeadlessCommand $exe @('--settings-migration', '--settings-file', (Join-Path $work 'settings.json')) $work
        $migrationReport = ([string] $migration.Output) | ConvertFrom-Json -DateKind String
        $equivalence = & $script:Ports.RunHeadlessCommand $exe @('--settings-equivalence', '--before-settings', (Join-Path $script:InstallRoot 'settings.json'), '--after-settings', (Join-Path $work 'settings.json'), '--migration-utc', [string] $migrationReport.migrationUtc) $work
        $equivalenceReport = ([string] $equivalence.Output) | ConvertFrom-Json -DateKind String
        $result.rehearsal[$name] = [ordered]@{
            migrationExitCode = [int] $migration.ExitCode; migrated = $migrationReport.migrated; migrationCategory = [string] $migrationReport.reasonCategory
            equivalenceExitCode = [int] $equivalence.ExitCode; equivalent = $equivalenceReport.equivalent; equivalenceCategory = [string] $equivalenceReport.reasonCategory
        }
    }
    $result.settingsUnchangedByDryRun = Compare-CheckSettingsWithSeed
    # #283 RemoveShortcut 等生产 port：在运行目录里写、读、删一个真实的 Shell Link，桌面不动。
    $lnk = Join-Path $script:RunDirectory 'dryrun-port-check.lnk'
    $definition = [pscustomobject]@{ TargetPath = (Join-Path $script:Packages.v2 'app\MinminCountdown.exe'); Arguments = ''; WorkingDirectory = (Join-Path $script:Packages.v2 'app'); IconLocation = ((Join-Path $script:Packages.v2 'app\MinminCountdown.exe') + ',0') }
    & $script:Ports.WriteShortcut $lnk $definition
    $read = & $script:Ports.ReadShortcut $lnk
    & $script:Ports.RemoveShortcut $lnk
    $result.shortcutPorts = [ordered]@{ written = $true; readIsShellLink = [bool] $read.IsShellLink; readTargetMatches = [string]::Equals([string] $read.TargetPath, $definition.TargetPath, [System.StringComparison]::OrdinalIgnoreCase); removed = -not (Test-Path -LiteralPath $lnk) }
    $result.processFactsSelfCheck = Get-CheckProcessFacts -Id $PID
    $result.shellWindowsInRun = @(Get-CheckShellWindows | Where-Object { $_.InRun }).Count
    $result.shellWindowsTotal = @(Get-CheckShellWindows).Count
    $result.finishedUtc = Get-CheckUtc
    Save-CheckJson -Name 'dryrun.json' -Value $result | Out-Null
    $result
}

function Stop-CheckThroughTask {
    param([Parameter(Mandatory)] [int] $ProcessId)
    $process = [System.Diagnostics.Process]::GetProcessById($ProcessId)
    $null = $process.Handle
    $before = Get-CheckTaskFacts -Name $script:TaskName
    $called = Get-CheckUtc
    $clock = [System.Diagnostics.Stopwatch]::StartNew()
    Stop-ScheduledTask -TaskPath '\' -TaskName $script:TaskName -ErrorAction Stop
    $callMs = $clock.ElapsedMilliseconds
    $exited = $process.WaitForExit(30000)
    $exitMs = $clock.ElapsedMilliseconds
    Start-Sleep -Milliseconds 1500
    $diagnostics = @(Read-CheckDiagnostics -ProcessIds @($ProcessId))
    [ordered]@{
        processId = $ProcessId
        calledUtc = $called
        stopCallMs = $callMs
        exited = $exited
        msToExit = if ($exited) { $exitMs } else { $null }
        exitCode = if ($exited) { $process.ExitCode } else { $null }
        taskBefore = $before
        taskAfter = Get-CheckTaskFacts -Name $script:TaskName
        diagnostics = $diagnostics
        exitConfirmationSkippedExternalClose = @($diagnostics | Where-Object { $_.event -eq 'ExitConfirmationSkippedExternalClose' }).Count
        processExitedLogged = @($diagnostics | Where-Object { $_.event -eq 'ProcessExited' }).Count
        fatalUnhandledException = @($diagnostics | Where-Object { $_.event -eq 'FatalUnhandledException' }).Count
        appProcessesInRunAfter = @(Get-CheckAppProcesses | Where-Object { $_.InRun } | ForEach-Object { $_.ProcessId })
    }
}

function Complete-CheckInstallStep {
    param([Parameter(Mandatory)] [string] $Name, [Parameter(Mandatory)] [object] $Install)
    $step = [ordered]@{ name = $Name; install = $Install }
    $result = $Install.result
    $ids = [System.Collections.Generic.List[int]]::new()
    foreach ($field in @('readyProcess', 'restartedProcess')) {
        $value = if ($null -ne $result) { $result.PSObject.Properties[$field] } else { $null }
        if ($null -ne $value -and $null -ne $value.Value) {
            $id = [int] $value.Value.processId
            $ids.Add($id)
            $step[$field + 'Facts'] = Get-CheckProcessFacts -Id $id
        }
    }
    foreach ($seen in @($Install.processesSeen | Where-Object { $null -ne $_ })) { if (-not $ids.Contains([int] $seen.processId)) { $ids.Add([int] $seen.processId) } }
    $step.task = Get-CheckTaskFacts -Name $script:TaskName
    $step.settings = Get-CheckSettingsFamily
    $step.settingsVersusSeed = Compare-CheckSettingsWithSeed
    $step.rootListing = Get-CheckRootListing
    $step.programManifest = Get-CheckManifestFacts -AppDirectory (Join-Path $script:InstallRoot 'app')
    $step.status = Invoke-CheckStatus
    $step.diagnostics = @(Read-CheckDiagnostics -ProcessIds $ids.ToArray())
    $step.appProcessesInRun = @(Get-CheckAppProcesses | Where-Object { $_.InRun } | ForEach-Object { [ordered]@{ processId = $_.ProcessId; sessionId = $_.SessionId } })
    $step.desktopShortcut = (Get-CheckDesktopShortcut).fingerprint
    $step
}

function Assert-CheckDesktopShortcutForeign {
    $shortcut = Get-CheckDesktopShortcut
    if (-not $shortcut.fingerprint.present -or $shortcut.isShellLink -ne $true -or $shortcut.targetInRunDirectory -ne $false) {
        throw '桌面上的同名快捷方式不存在、不是 Shell Link，或指向运行目录：安装器会在真实桌面上新建或改写它，拒绝继续。'
    }
    $shortcut
}

function Invoke-CheckRun {
    if ([string]::IsNullOrWhiteSpace($Consent)) { throw 'Run 需要用户当场同意的原话（-Consent）。' }
    $state = Get-CheckState
    $run = [ordered]@{ mode = 'Run'; consent = $Consent; startedUtc = Get-CheckUtc; steps = [ordered]@{} }
    $save = { Save-CheckJson -Name 'run.json' -Value $run | Out-Null }
    try {
        # R0：现场指纹与护栏。
        $run.before = Get-CheckRealWorld
        if (@($run.before.appProcessesInRun).Count -gt 0) { throw '运行目录里已有应用进程。' }
        if ((Get-CheckTaskFacts -Name $script:TaskName).present) { throw '临时任务已存在。' }
        $run.shortcutBefore = Assert-CheckDesktopShortcutForeign
        $run.settingsSeed = Get-CheckSettingsFamily
        & $save

        # R1：首次安装 v1（安装根里已有设置与两代备份；登录任务由安装器在非提升的 shell 里创建）。
        $run.steps.r1FirstInstall = Complete-CheckInstallStep -Name 'r1-first-install-v1' -Install (Invoke-CheckInstaller -Package $script:Packages.v1)
        & $save
        if (-not $run.steps.r1FirstInstall.install.result.succeeded) { throw 'R1 首次安装没有成功，停止。' }

        # R2：成功升级 v1 -> v2。先把临时任务的优先级改成 5，让安装器走“与期望不一致时更新”（RegisterTask -Force）。
        $run.steps.r2PriorityBefore = Set-CheckTaskPriority -Priority 5
        Assert-CheckDesktopShortcutForeign | Out-Null
        $run.steps.r2Upgrade = Complete-CheckInstallStep -Name 'r2-upgrade-v2' -Install (Invoke-CheckInstaller -Package $script:Packages.v2)
        & $save
        if (-not $run.steps.r2Upgrade.install.result.succeeded) { throw 'R2 升级没有成功，停止。' }

        # R3：经真实任务停止应用（#295 复核）。
        $run.steps.r3StopThroughTask = Stop-CheckThroughTask -ProcessId ([int] $run.steps.r2Upgrade.install.result.readyProcess.processId)
        $run.steps.r3SettingsVersusSeed = Compare-CheckSettingsWithSeed
        & $save

        # R4：人为制造的启动失败：v3 在启动时改写并保存设置（轮转两代备份）后、首帧之前抛出。任务优先级先改回 5，
        # 让安装器导出、更新任务，回滚时按导出的 XML 恢复（#283 ExportTask/RestoreTask）。
        $run.steps.r4PriorityBefore = Set-CheckTaskPriority -Priority 5
        $run.steps.r4TaskBefore = Get-CheckTaskFacts -Name $script:TaskName
        $run.steps.r4SettingsBefore = Get-CheckSettingsFamily
        Assert-CheckDesktopShortcutForeign | Out-Null
        $run.steps.r4InducedFailure = Complete-CheckInstallStep -Name 'r4-induced-startup-failure-v3' -Install (Invoke-CheckInstaller -Package $script:Packages.v3)
        $run.steps.r4TaskXmlRestoredExactly = [string]::Equals([string] $run.steps.r4TaskBefore.xml, [string] $run.steps.r4InducedFailure.task.xml, [System.StringComparison]::Ordinal)
        & $save

        # R5：资源管理器打开现役程序目录时安装（v2 重装），记录改名的实际表现。
        $programDirectory = Join-Path $script:InstallRoot 'app'
        $shell = New-Object -ComObject Shell.Application
        $shell.Open($programDirectory)
        $window = $null
        $clock = [System.Diagnostics.Stopwatch]::StartNew()
        while ($clock.Elapsed.TotalSeconds -lt 15 -and $null -eq $window) {
            $window = @(Get-CheckShellWindows | Where-Object { [string]::Equals($_.Path, $programDirectory, [System.StringComparison]::OrdinalIgnoreCase) }) | Select-Object -First 1
            if ($null -eq $window) { Start-Sleep -Milliseconds 200 }
        }
        if ($null -eq $window) { throw '资源管理器窗口 15 秒内没有出现。' }
        Start-Sleep -Seconds 3
        $explorer = [ordered]@{ openedAfterMs = $clock.ElapsedMilliseconds; hwnd = $window.Hwnd; pathBefore = $window.Path }
        Assert-CheckDesktopShortcutForeign | Out-Null
        $run.steps.r5ExplorerOpen = Complete-CheckInstallStep -Name 'r5-reinstall-v2-explorer-open' -Install (Invoke-CheckInstaller -Package $script:Packages.v2)
        $after = @(Get-CheckShellWindows | Where-Object { $_.Hwnd -eq $explorer.hwnd }) | Select-Object -First 1
        $explorer.windowStillOpen = $null -ne $after
        $explorer.pathAfter = if ($null -ne $after) { $after.Path } else { $null }
        $explorer.closedWindows = Close-CheckRunShellWindows
        Start-Sleep -Milliseconds 800
        $explorer.windowsInRunAfterClose = @(Get-CheckShellWindows | Where-Object { $_.InRun }).Count
        $run.steps.r5Explorer = $explorer
        & $save

        # R6：中断恢复（#284）在真实安装根上：R5 留下残留就让下一次安装收拾；否则造一个“两次改名之间中断”的状态
        # （应用先经任务停止，app 改名为 app.previous，写下事务标记），再安装 v2。
        $r6 = [ordered]@{}
        $marker = Join-Path $script:InstallRoot 'install-transaction.json'
        $leftovers = @('app.candidate', 'app.previous', 'install-transaction', 'install-transaction.json', 'install-transaction.json.tmp' | Where-Object { Test-Path -LiteralPath (Join-Path $script:InstallRoot $_) })
        $r6.leftoversAfterR5 = $leftovers
        if ($leftovers.Count -eq 0) {
            $running = @(Get-CheckAppProcesses | Where-Object { $_.InRun })
            if ($running.Count -eq 1) { $r6.stopThroughTask = Stop-CheckThroughTask -ProcessId $running[0].ProcessId }
            # 刚退出的映像可能还被短暂占用（杀毒扫描），改名按安装器同一类占用有界重试 10 秒。
            $moveClock = [System.Diagnostics.Stopwatch]::StartNew()
            while ($true) {
                try { [System.IO.Directory]::Move($programDirectory, (Join-Path $script:InstallRoot 'app.previous')); break }
                catch { if ($moveClock.Elapsed.TotalSeconds -ge 10 -or $_.Exception.GetBaseException() -isnot [System.IO.IOException] -and $_.Exception.GetBaseException() -isnot [System.UnauthorizedAccessException]) { throw }; Start-Sleep -Milliseconds 200 }
            }
            $r6.emulatedRenameMs = $moveClock.ElapsedMilliseconds
            $markerDocument = [ordered]@{ schemaVersion = 1; startedAtUtc = Get-CheckUtc; candidateCommit = [string] $state.packages.v2.commit; candidateAggregateSha256 = [string] $state.packages.v2.aggregateSha256 }
            [System.IO.File]::WriteAllText($marker, ($markerDocument | ConvertTo-Json), $script:Utf8)
            $r6.emulated = 'mid-replace'
            $r6.rootListingBefore = Get-CheckRootListing
        }
        Assert-CheckDesktopShortcutForeign | Out-Null
        $r6.install = Complete-CheckInstallStep -Name 'r6-recovery-then-install-v2' -Install (Invoke-CheckInstaller -Package $script:Packages.v2)
        $run.steps.r6Recovery = $r6
        & $save

        # R7：再经真实任务停止一次（由安装器经任务启动的那个实例）。
        $running = @(Get-CheckAppProcesses | Where-Object { $_.InRun })
        if ($running.Count -eq 1) { $run.steps.r7StopThroughTask = Stop-CheckThroughTask -ProcessId $running[0].ProcessId }
        $run.steps.r7SettingsVersusSeed = Compare-CheckSettingsWithSeed
    }
    catch {
        $run.error = [ordered]@{ message = [string] $_.Exception.Message; at = [string] $_.InvocationInfo.PositionMessage }
    }
    finally {
        $run.finishedUtc = Get-CheckUtc
        & $save
    }
    $run
}

function Invoke-CheckCleanup {
    $result = [ordered]@{ mode = 'Cleanup'; startedUtc = Get-CheckUtc }
    $result.stoppedLeftovers = @(foreach ($process in @(Get-CheckAppProcesses | Where-Object { $_.InRun })) { [ordered]@{ processId = $process.ProcessId; result = Stop-CheckRunProcess -Id $process.ProcessId } })
    $task = Get-CheckTaskFacts -Name $script:TaskName
    $result.taskBefore = if ($task.present) { [ordered]@{ state = $task.state; priority = $task.priority; xmlSha256 = $task.xmlSha256 } } else { $null }
    if ($task.present) {
        if ($task.state -eq 'Running') { Stop-ScheduledTask -TaskPath '\' -TaskName $script:TaskName }
        # 生产 RestoreTask port 的“安装前没有这个任务”一支（#283）：定义为空时注销。
        & $script:Ports.RestoreTask $script:TaskName $null
        $result.unregisteredUtc = Get-CheckUtc
    }
    $result.taskGone = -not (Get-CheckTaskFacts -Name $script:TaskName).present
    $result.taskGoneByAnyPath = @(Get-ScheduledTask -ErrorAction Stop | Where-Object { $_.TaskName -like 'MinminCountdown-Check-288-*' }).Count -eq 0
    $result.closedShellWindows = Close-CheckRunShellWindows
    Start-Sleep -Milliseconds 800
    $result.shellWindowsInRunAfter = @(Get-CheckShellWindows | Where-Object { $_.InRun }).Count
    $result.after = Get-CheckRealWorld
    $result.finishedUtc = Get-CheckUtc
    Save-CheckJson -Name 'cleanup.json' -Value $result | Out-Null
    $result
}

$outcome = switch ($Mode) {
    'Prepare' { Invoke-CheckPrepare }
    'DryRun' { Invoke-CheckDryRun }
    'Run' { Invoke-CheckRun }
    'Cleanup' { Invoke-CheckCleanup }
}
$failure = if ($outcome -is [System.Collections.IDictionary] -and $outcome.Contains('error')) { $outcome['error'] } else { $null }
"$Mode finished; measurements under $($script:OutputDirectory)"
if ($null -ne $failure) { 'ERROR: ' + ($failure | ConvertTo-Json -Compress) }
