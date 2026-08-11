#requires -Version 7.0

[CmdletBinding()]
param(
    [string] $OfficeDeploymentStagingRoot,
    [switch] $InstanceActivationOnly,
    [switch] $InstanceActivationMatrix,
    [switch] $MigrationParityOnly,
    [ValidateSet('FloatingVisible', 'Tray', 'Hidden', 'Minimized', 'MainVisible')]
    [string] $InstanceActivationTestState = 'FloatingVisible',
    [ValidateSet('Normal', 'Timeout', 'ProtocolMismatch', 'Disconnect', 'CallbackFailureOnce')]
    [string] $InstanceActivationListenerBehavior = 'Normal',
    [ValidateSet('Normal', 'AcceptanceReadOnly', 'Finalized', 'ValidateExercise', 'StalledRecovery', 'OptionGuard', 'DiagnosticConcurrency', 'DisposeRace', 'Scope')]
    [string] $InstanceActivationClientMode = 'Normal'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
$script:assertions = 0
$floatingReferenceBaselineManifest = Join-Path $PSScriptRoot 'assets\ui-references\floating-window\520x390\2026-08-06-v6\manifest.json'
$floatingReferenceBaselineRequired = Test-Path -LiteralPath $floatingReferenceBaselineManifest -PathType Leaf
$mainReferenceBaselineManifest = Join-Path $PSScriptRoot 'assets\ui-references\main-window\1165x1350\2026-08-06-v6\manifest.json'
$mainReferenceBaselineRequired = Test-Path -LiteralPath $mainReferenceBaselineManifest -PathType Leaf

function Resolve-ReferenceBaselineMode {
    param(
        [Parameter(Mandatory)] [bool] $MainManifestAvailable,
        [Parameter(Mandatory)] [bool] $FloatingManifestAvailable
    )

    if ($MainManifestAvailable -and $FloatingManifestAvailable) {
        return 'SourceTree'
    }

    if (-not $MainManifestAvailable -and -not $FloatingManifestAvailable) {
        return 'ReleasePackage'
    }

    throw '主窗口与悬浮窗视觉 manifest 必须同时存在或同时缺失；部分基线状态已失败闭锁。'
}

function Invoke-CheckedNativeCommand {
    param(
        [Parameter(Mandatory)] [string] $FilePath,
        [Parameter(Mandatory)] [string[]] $ArgumentList,
        [Parameter(Mandatory)] [string] $Operation
    )

    $output = @(& $FilePath @ArgumentList)
    $exitCode = $LASTEXITCODE
    if ($exitCode -ne 0) {
        $detail = if ($output.Count -gt 0) {
            $output -join [Environment]::NewLine
        }
        else {
            '无标准输出。'
        }
        throw "$Operation 失败，退出码：$exitCode。$([Environment]::NewLine)$detail"
    }

    return $output -join [Environment]::NewLine
}

function Invoke-NativeCommandResult {
    param(
        [Parameter(Mandatory)] [string] $FilePath,
        [Parameter(Mandatory)] [string[]] $ArgumentList
    )

    $output = @(& $FilePath @ArgumentList)
    [pscustomobject]@{
        ExitCode = $LASTEXITCODE
        Output = $output -join [Environment]::NewLine
    }
}

function Assert-Equal {
    param(
        [AllowNull()] $Expected,
        [AllowNull()] $Actual,
        [Parameter(Mandatory)] [string] $Name
    )

    $script:assertions++
    if ($Expected -ne $Actual) {
        throw "$Name 失败：expected=[$Expected] actual=[$Actual]"
    }
}

if (-not ('MinminCountdownActivationWindowProbe' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
using System.Text;

public static class MinminCountdownActivationWindowProbe
{
    public delegate bool EnumWindowsProc(IntPtr windowHandle, IntPtr state);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool EnumWindows(EnumWindowsProc callback, IntPtr state);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsWindowVisible(IntPtr windowHandle);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint GetWindowThreadProcessId(IntPtr windowHandle, out uint processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern int GetWindowText(IntPtr windowHandle, StringBuilder text, int maxCount);

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool PostMessage(IntPtr windowHandle, uint message, IntPtr wParam, IntPtr lParam);
}
'@
}

function Get-VisibleProcessWindows {
    param([Parameter(Mandatory)] [int] $ProcessId)

    $windows = [System.Collections.Generic.List[object]]::new()
    $callback = [MinminCountdownActivationWindowProbe+EnumWindowsProc] {
        param([IntPtr] $windowHandle, [IntPtr] $state)
        [uint32] $ownerProcessId = 0
        [void] [MinminCountdownActivationWindowProbe]::GetWindowThreadProcessId(
            $windowHandle,
            [ref] $ownerProcessId)
        if ($ownerProcessId -eq $ProcessId -and
            [MinminCountdownActivationWindowProbe]::IsWindowVisible($windowHandle)) {
            $title = [System.Text.StringBuilder]::new(512)
            [void] [MinminCountdownActivationWindowProbe]::GetWindowText($windowHandle, $title, $title.Capacity)
            $windows.Add([pscustomobject]@{
                Handle = $windowHandle.ToInt64()
                Title = $title.ToString()
            })
        }
        return $true
    }
    [void] [MinminCountdownActivationWindowProbe]::EnumWindows($callback, [IntPtr]::Zero)
    return @($windows)
}

function Get-InstanceActivationPipeName {
    param(
        [Parameter(Mandatory)] [string] $ApplicationRoot,
        [Parameter(Mandatory)] [string] $SettingsPath
    )

    $utf8NoBom = [System.Text.UTF8Encoding]::new($false)
    $scope = '{0}|{1}' -f (
        [System.IO.Path]::GetFullPath($ApplicationRoot).ToUpperInvariant()
    ), (
        [System.IO.Path]::GetFullPath($SettingsPath).ToUpperInvariant()
    )
    $scopeHash = [Convert]::ToHexString(
        [System.Security.Cryptography.SHA256]::HashData($utf8NoBom.GetBytes($scope)))
    $sid = [System.Security.Principal.WindowsIdentity]::GetCurrent().User.Value
    $mutexName = 'Local\MinminCountdown-{0}-{1}' -f $sid, $scopeHash.Substring(0, 24)
    $activationScope = [Convert]::ToHexString(
        [System.Security.Cryptography.SHA256]::HashData($utf8NoBom.GetBytes($mutexName)))
    return 'MinminCountdown.Activation-{0}' -f $activationScope.Substring(0, 32)
}

function Invoke-InstanceActivationProcessTests {
    $dotnet = 'D:\Codex\tools\dotnet\10.0\dotnet.exe'
    $sourceApplication = Join-Path $PSScriptRoot 'src\MinminCountdown\bin\Release\net10.0-windows\MinminCountdown.dll'
    $safeTreeDelete = 'D:\Codex\.codex\scripts\invoke-safe-tree-delete.cmd'
    $testCapabilityRoot = [System.IO.Path]::GetFullPath('D:\Codex\.codex\run-evidence')
    $testGroupRoot = Join-Path $testCapabilityRoot 'clock-countdown-instance-activation-tests'
    $testRoot = [System.IO.Path]::GetFullPath(
        (Join-Path $testGroupRoot ("run-" + [guid]::NewGuid().ToString('N')))
    )
    $primaryProcess = $null
    $secondaryProcess = $null
    $firstSecondaryProcess = $null
    $stalledPipe = $null
    $parallelProcesses = [System.Collections.Generic.List[System.Diagnostics.Process]]::new()

    if ($InstanceActivationClientMode -ne 'Normal' -and
        $InstanceActivationListenerBehavior -ne 'Normal') {
        throw '客户端抑制与监听器故障场景必须分别运行。'
    }

    if (-not (Test-Path -LiteralPath $dotnet -PathType Leaf)) {
        throw "未找到受管 .NET 运行时：$dotnet"
    }
    if (-not (Test-Path -LiteralPath $sourceApplication -PathType Leaf)) {
        throw "未找到源码构建应用：$sourceApplication。请先构建 MinminCountdown.csproj。"
    }
    if (-not (Test-Path -LiteralPath $safeTreeDelete -PathType Leaf)) {
        throw "未找到受管测试目录清理入口：$safeTreeDelete"
    }

    try {
        New-Item -ItemType Directory -Path $testRoot | Out-Null
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot '敏敏子倒计时.ps1') -Destination $testRoot
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'countdown-icon.ico') -Destination $testRoot

        $settingsPath = Join-Path $testRoot 'settings.json'
        $diagnosticPath = Join-Path $testRoot 'logs\application.jsonl'
        $runtimeStatusPath = Join-Path $testRoot 'runtime-status.json'
        $primaryOutputPath = Join-Path $testRoot 'primary.stdout.txt'
        $primaryErrorPath = Join-Path $testRoot 'primary.stderr.txt'
        $secondaryOutputPath = Join-Path $testRoot 'secondary.stdout.txt'
        $secondaryErrorPath = Join-Path $testRoot 'secondary.stderr.txt'
        $utf8NoBom = [System.Text.UTF8Encoding]::new($false)
        $settingsJson = @'
{
  "schemaVersion": 10,
  "timeZoneId": "China Standard Time",
  "displayPrecision": "Minute",
  "monthlySalary": 0,
  "workStart": "09:00",
  "workEnd": "18:00",
  "themePreference": "Day",
  "tags": [],
  "countdowns": [],
  "notes": [],
  "reminderStates": {}
}
'@
        [System.IO.File]::WriteAllText($settingsPath, $settingsJson, $utf8NoBom)
        $settingsHashBefore = (Get-FileHash -LiteralPath $settingsPath -Algorithm SHA256).Hash

        if ($InstanceActivationClientMode -eq 'OptionGuard') {
            $guardOutputPath = Join-Path $testRoot 'option-guard.stdout.txt'
            $guardErrorPath = Join-Path $testRoot 'option-guard.stderr.txt'
            $secondaryProcess = Start-Process -FilePath $dotnet -ArgumentList @(
                $sourceApplication,
                '--require-startup-request',
                '--exit-after-ready-for-test',
                '--instance-activation-test-state',
                'FloatingVisible',
                '--settings-file',
                $settingsPath,
                '--diagnostic-log-path',
                $diagnosticPath
            ) -WorkingDirectory $testRoot -PassThru -WindowStyle Hidden `
                -RedirectStandardOutput $guardOutputPath -RedirectStandardError $guardErrorPath
            if (-not $secondaryProcess.WaitForExit(5000)) {
                throw 'test-only 与 required 参数组合未在有界期限内退出。'
            }
            $secondaryProcess.WaitForExit()
            Assert-Equal 1 $secondaryProcess.ExitCode 'test-only 与 required 参数组合失败闭锁'
            $guardError = Get-Content -LiteralPath $guardErrorPath -Raw -Encoding UTF8
            Assert-Equal $true $guardError.Contains(
                '实例激活测试参数不能与 --require-startup-request 组合',
                [System.StringComparison]::Ordinal) '参数解析明确拒绝 test-only 与 required 组合'
            Assert-Equal $settingsHashBefore (Get-FileHash -LiteralPath $settingsPath -Algorithm SHA256).Hash `
                '参数失败不写隔离设置'
            [pscustomobject]@{
                Passed = $script:assertions
                Failed = 0
                InstanceActivationOnly = $true
                ProcessScenarios = 1
            } | ConvertTo-Json
            return
        }

        if ($InstanceActivationClientMode -eq 'Finalized') {
            $startupRequestPath = Join-Path $testRoot 'startup-request.json'
            $activeRequest = [ordered]@{
                schemaVersion = 1
                launchToken = [guid]::NewGuid().ToString('N')
                requestedAtUtc = [DateTimeOffset]::UtcNow.ToString('o')
                acceptanceReadOnly = $true
                acceptanceState = 'active'
            } | ConvertTo-Json -Compress
            [System.IO.File]::WriteAllText($startupRequestPath, $activeRequest, $utf8NoBom)
        }

        $primaryArguments = @(
            $sourceApplication,
            '--exit-after-ready-for-test',
            '--instance-activation-test-state',
            $InstanceActivationTestState,
            '--settings-file',
            $settingsPath,
            '--diagnostic-log-path',
            $diagnosticPath
        )
        if ($InstanceActivationListenerBehavior -ne 'Normal') {
            $primaryArguments += @(
                '--instance-activation-listener-behavior',
                $InstanceActivationListenerBehavior
            )
        }
        $primaryProcess = Start-Process -FilePath $dotnet -ArgumentList $primaryArguments `
            -WorkingDirectory $testRoot -PassThru -WindowStyle Hidden `
            -RedirectStandardOutput $primaryOutputPath -RedirectStandardError $primaryErrorPath

        $readyDeadline = [DateTimeOffset]::UtcNow.AddSeconds(15)
        while (
            -not (Test-Path -LiteralPath $runtimeStatusPath -PathType Leaf) -and
            -not $primaryProcess.HasExited -and
            [DateTimeOffset]::UtcNow -lt $readyDeadline
        ) {
            Start-Sleep -Milliseconds 50
        }
        if (-not (Test-Path -LiteralPath $runtimeStatusPath -PathType Leaf)) {
            $primaryError = if (Test-Path -LiteralPath $primaryErrorPath -PathType Leaf) {
                Get-Content -LiteralPath $primaryErrorPath -Raw
            }
            else { '' }
            throw "首个源码应用未进入窗口就绪状态：$primaryError"
        }

        $runtimeStatus = Get-Content -LiteralPath $runtimeStatusPath -Raw -Encoding UTF8 | ConvertFrom-Json
        Assert-Equal $primaryProcess.Id ([int] $runtimeStatus.processId) '激活测试回执绑定首个源码应用进程'
        Assert-Equal $false $primaryProcess.HasExited '第二次启动前主实例仍存活'

        if ($InstanceActivationClientMode -in @('StalledRecovery', 'DisposeRace')) {
            $pipeName = Get-InstanceActivationPipeName -ApplicationRoot $testRoot -SettingsPath $settingsPath
            $stalledPipe = [System.IO.Pipes.NamedPipeClientStream]::new(
                '.',
                $pipeName,
                [System.IO.Pipes.PipeDirection]::InOut,
                [System.IO.Pipes.PipeOptions]::Asynchronous
            )
            $stalledPipe.Connect(2000)
            Assert-Equal $true $stalledPipe.IsConnected '停滞客户端已建立真实命名管道连接'
        }

        $secondaryArguments = @(
            $sourceApplication,
            '--settings-file',
            $settingsPath,
            '--diagnostic-log-path',
            $diagnosticPath
        )
        if ($InstanceActivationClientMode -eq 'AcceptanceReadOnly') {
            $startupRequestPath = Join-Path $testRoot 'startup-request.json'
            $startupRequest = [ordered]@{
                schemaVersion = 1
                launchToken = [guid]::NewGuid().ToString('N')
                requestedAtUtc = [DateTimeOffset]::UtcNow.ToString('o')
                acceptanceReadOnly = $true
                acceptanceState = 'active'
            } | ConvertTo-Json -Compress
            [System.IO.File]::WriteAllText($startupRequestPath, $startupRequest, $utf8NoBom)
            $secondaryArguments += '--require-startup-request'
        }
        elseif ($InstanceActivationClientMode -eq 'Finalized') {
            $finalizedRequest = [ordered]@{
                schemaVersion = 1
                launchToken = [guid]::NewGuid().ToString('N')
                requestedAtUtc = [DateTimeOffset]::UtcNow.ToString('o')
                acceptanceReadOnly = $false
                acceptanceState = 'finalized'
            } | ConvertTo-Json -Compress
            [System.IO.File]::WriteAllText($startupRequestPath, $finalizedRequest, $utf8NoBom)
            $secondaryArguments += '--require-startup-request'
        }
        elseif ($InstanceActivationClientMode -eq 'ValidateExercise') {
            $secondaryArguments = @(
                $sourceApplication,
                '--validate-only',
                '--exercise-fatal-diagnostic',
                '--settings-file',
                $settingsPath,
                '--diagnostic-log-path',
                $diagnosticPath
            )
        }

        if ($InstanceActivationClientMode -eq 'DiagnosticConcurrency') {
            $padding = [ordered]@{
                schemaVersion = 1
                event = 'Padding'
                padding = 'x' * 1048400
            } | ConvertTo-Json -Compress
            [System.IO.File]::WriteAllText(
                $diagnosticPath,
                $padding + [Environment]::NewLine,
                $utf8NoBom)
            $clientCount = 8
            for ($index = 0; $index -lt $clientCount; $index++) {
                $client = Start-Process -FilePath $dotnet -ArgumentList $secondaryArguments `
                    -WorkingDirectory $testRoot -PassThru -WindowStyle Hidden `
                    -RedirectStandardOutput (Join-Path $testRoot "parallel-$index.stdout.txt") `
                    -RedirectStandardError (Join-Path $testRoot "parallel-$index.stderr.txt")
                $parallelProcesses.Add($client)
            }
            foreach ($client in $parallelProcesses) {
                if (-not $client.WaitForExit(10000)) {
                    throw "并发激活客户端未在有界期限内退出：PID=$($client.Id)"
                }
                $client.WaitForExit()
                Assert-Equal 0 $client.ExitCode "并发激活客户端收到匹配 ACK：PID=$($client.Id)"
            }

            $allDiagnosticEntries = [System.Collections.Generic.List[object]]::new()
            foreach ($path in @($diagnosticPath, "$diagnosticPath.1", "$diagnosticPath.2", "$diagnosticPath.3")) {
                if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
                    continue
                }
                foreach ($line in Get-Content -LiteralPath $path -Encoding UTF8) {
                    $allDiagnosticEntries.Add(($line | ConvertFrom-Json))
                }
            }
            $parallelIds = @($parallelProcesses | ForEach-Object Id)
            Assert-Equal $true (Test-Path -LiteralPath "$diagnosticPath.1" -PathType Leaf) `
                '跨进程并发写入触发并保留 rollover'
            Assert-Equal $clientCount @(
                $allDiagnosticEntries |
                    Where-Object {
                        $_.event -eq 'InstanceActivationSucceeded' -and
                        $_.processId -in $parallelIds
                    }
            ).Count '并发客户端激活结果无静默丢失'
            Assert-Equal $clientCount @(
                $allDiagnosticEntries |
                    Where-Object {
                        $_.event -eq 'InstanceActivationHandled' -and
                        $_.processId -eq $primaryProcess.Id
                    }
            ).Count '主实例并发激活处理诊断无静默丢失'

            [System.IO.File]::WriteAllText(
                $diagnosticPath,
                $padding + [Environment]::NewLine,
                $utf8NoBom)
            $loggerProbePath = Join-Path $testRoot 'diagnostic-concurrency-probe.ps1'
            $loggerBarrierPath = Join-Path $testRoot 'diagnostic-concurrency.start'
            $loggerProbe = @'
param(
    [Parameter(Mandatory)] [string] $AssemblyPath,
    [Parameter(Mandatory)] [string] $LogPath,
    [Parameter(Mandatory)] [string] $BarrierPath,
    [Parameter(Mandatory)] [int] $Worker,
    [Parameter(Mandatory)] [int] $WriteCount
)
$assembly = [System.Reflection.Assembly]::LoadFrom($AssemblyPath)
$loggerType = $assembly.GetType('MinminCountdown.DiagnosticLogger', $true)
$logger = [Activator]::CreateInstance($loggerType, [object[]] @($LogPath))
$info = $loggerType.GetMethod('Info')
while (-not (Test-Path -LiteralPath $BarrierPath -PathType Leaf)) {
    Start-Sleep -Milliseconds 5
}
for ($index = 0; $index -lt $WriteCount; $index++) {
    [void] $info.Invoke($logger, [object[]] @("InstanceActivationListenerFailedWorker$Worker-$index"))
}
'@
            [System.IO.File]::WriteAllText($loggerProbePath, $loggerProbe, $utf8NoBom)
            $loggerWorkerCount = 8
            $writesPerWorker = 100
            for ($worker = 0; $worker -lt $loggerWorkerCount; $worker++) {
                $probeLogPath = if ($worker % 2 -eq 0) {
                    $diagnosticPath
                }
                else {
                    Join-Path $testRoot 'logs\..\logs\application.jsonl'
                }
                $probe = Start-Process -FilePath 'D:\Codex\tools\PowerShell\7.6.4\pwsh.exe' -ArgumentList @(
                    '-NoLogo',
                    '-NoProfile',
                    '-File',
                    $loggerProbePath,
                    '-AssemblyPath',
                    $sourceApplication,
                    '-LogPath',
                    $probeLogPath,
                    '-BarrierPath',
                    $loggerBarrierPath,
                    '-Worker',
                    $worker,
                    '-WriteCount',
                    $writesPerWorker
                ) -WorkingDirectory $testRoot -PassThru -WindowStyle Hidden `
                    -RedirectStandardOutput (Join-Path $testRoot "logger-$worker.stdout.txt") `
                    -RedirectStandardError (Join-Path $testRoot "logger-$worker.stderr.txt")
                $parallelProcesses.Add($probe)
            }
            Start-Sleep -Milliseconds 300
            [System.IO.File]::WriteAllText($loggerBarrierPath, 'start', $utf8NoBom)
            foreach ($probe in @($parallelProcesses | Select-Object -Last $loggerWorkerCount)) {
                if (-not $probe.WaitForExit(10000)) {
                    throw "诊断并发 probe 未在有界期限内退出：PID=$($probe.Id)"
                }
                $probe.WaitForExit()
                Assert-Equal 0 $probe.ExitCode "诊断并发 probe 正常退出：PID=$($probe.Id)"
            }
            $probeEntries = [System.Collections.Generic.List[object]]::new()
            foreach ($path in @($diagnosticPath, "$diagnosticPath.1", "$diagnosticPath.2", "$diagnosticPath.3")) {
                if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
                    continue
                }
                foreach ($line in Get-Content -LiteralPath $path -Encoding UTF8) {
                    $probeEntries.Add(($line | ConvertFrom-Json))
                }
            }
            Assert-Equal ($loggerWorkerCount * $writesPerWorker) @(
                $probeEntries |
                    Where-Object { $_.event -like 'InstanceActivationListenerFailedWorker*' }
            ).Count '规范化同路径的跨进程 rotate+append 无静默丢失'
            Assert-Equal $settingsHashBefore (Get-FileHash -LiteralPath $settingsPath -Algorithm SHA256).Hash `
                '诊断并发与 rollover 不写设置'
            [pscustomobject]@{
                Passed = $script:assertions
                Failed = 0
                InstanceActivationOnly = $true
                ProcessScenarios = 1
            } | ConvertTo-Json
            return
        }

        if ($InstanceActivationClientMode -eq 'DisposeRace') {
            $floatingWindows = @(Get-VisibleProcessWindows -ProcessId $primaryProcess.Id |
                Where-Object Title -eq '敏敏子悬浮窗')
            Assert-Equal 1 $floatingWindows.Count 'Dispose 竞态前存在唯一真实悬浮窗'
            Assert-Equal $true ([MinminCountdownActivationWindowProbe]::PostMessage(
                [IntPtr]::new([long] $floatingWindows[0].Handle),
                0x0010,
                [IntPtr]::Zero,
                [IntPtr]::Zero)) '向真实悬浮窗发送 WM_CLOSE'
            if (-not $primaryProcess.WaitForExit(5000)) {
                throw '停滞连接存在时主实例 Dispose 未有界收口。'
            }
            $primaryProcess.WaitForExit()
            Assert-Equal 0 $primaryProcess.ExitCode '停滞连接存在时主实例正常退出'
            $stalledPipe.Dispose()
            $stalledPipe = $null
            $primaryProcess.Dispose()
            $primaryProcess = $null
            [System.IO.File]::Delete($runtimeStatusPath)
            $primaryProcess = Start-Process -FilePath $dotnet -ArgumentList $primaryArguments `
                -WorkingDirectory $testRoot -PassThru -WindowStyle Hidden `
                -RedirectStandardOutput $primaryOutputPath -RedirectStandardError $primaryErrorPath
            $restartDeadline = [DateTimeOffset]::UtcNow.AddSeconds(15)
            while (-not (Test-Path -LiteralPath $runtimeStatusPath -PathType Leaf) -and
                -not $primaryProcess.HasExited -and
                [DateTimeOffset]::UtcNow -lt $restartDeadline) {
                Start-Sleep -Milliseconds 50
            }
            Assert-Equal $true (Test-Path -LiteralPath $runtimeStatusPath -PathType Leaf) `
                'Dispose 后同 scope 可重新取得租约并进入 window-ready'
            Assert-Equal $false $primaryProcess.HasExited 'Dispose 后新主实例保持存活'
            Assert-Equal $settingsHashBefore (Get-FileHash -LiteralPath $settingsPath -Algorithm SHA256).Hash `
                'Dispose 竞态与同 scope 重启不写设置'
            [pscustomobject]@{
                Passed = $script:assertions
                Failed = 0
                InstanceActivationOnly = $true
                ProcessScenarios = 1
            } | ConvertTo-Json
            return
        }

        if ($InstanceActivationClientMode -eq 'Scope') {
            $settingsPathB = Join-Path $testRoot 'settings-b.json'
            $diagnosticPathB = Join-Path $testRoot 'logs\application-b.jsonl'
            [System.IO.File]::WriteAllText($settingsPathB, $settingsJson, $utf8NoBom)
            $settingsHashBeforeB = (Get-FileHash -LiteralPath $settingsPathB -Algorithm SHA256).Hash
            $scopeBPrimary = Start-Process -FilePath $dotnet -ArgumentList @(
                $sourceApplication,
                '--exit-after-ready-for-test',
                '--instance-activation-test-state',
                'FloatingVisible',
                '--settings-file',
                $settingsPathB,
                '--diagnostic-log-path',
                $diagnosticPathB
            ) -WorkingDirectory $testRoot -PassThru -WindowStyle Hidden `
                -RedirectStandardOutput (Join-Path $testRoot 'scope-b-primary.stdout.txt') `
                -RedirectStandardError (Join-Path $testRoot 'scope-b-primary.stderr.txt')
            $parallelProcesses.Add($scopeBPrimary)
            $scopeReadyDeadline = [DateTimeOffset]::UtcNow.AddSeconds(15)
            do {
                $scopeBReady = (Test-Path -LiteralPath $diagnosticPathB -PathType Leaf) -and
                    (Get-Content -LiteralPath $diagnosticPathB -Raw -Encoding UTF8).Contains(
                        'WindowReady',
                        [StringComparison]::Ordinal)
                if ($scopeBReady) { break }
                Start-Sleep -Milliseconds 50
            } while (-not $scopeBPrimary.HasExited -and [DateTimeOffset]::UtcNow -lt $scopeReadyDeadline)
            Assert-Equal $true $scopeBReady '不同设置路径的第二个主实例进入 window-ready'
            Assert-Equal $false $primaryProcess.HasExited 'scope A 主实例保持存活'
            Assert-Equal $false $scopeBPrimary.HasExited 'scope B 主实例保持存活'

            $scopeClients = @(
                @{ Settings = $settingsPath; Diagnostic = $diagnosticPath; ServerPid = $primaryProcess.Id; Name = 'a' },
                @{ Settings = $settingsPathB; Diagnostic = $diagnosticPathB; ServerPid = $scopeBPrimary.Id; Name = 'b' }
            )
            foreach ($scopeClient in $scopeClients) {
                $client = Start-Process -FilePath $dotnet -ArgumentList @(
                    $sourceApplication,
                    '--settings-file',
                    $scopeClient.Settings,
                    '--diagnostic-log-path',
                    $scopeClient.Diagnostic
                ) -WorkingDirectory $testRoot -PassThru -WindowStyle Hidden `
                    -RedirectStandardOutput (Join-Path $testRoot "scope-$($scopeClient.Name)-client.stdout.txt") `
                    -RedirectStandardError (Join-Path $testRoot "scope-$($scopeClient.Name)-client.stderr.txt")
                $parallelProcesses.Add($client)
                if (-not $client.WaitForExit(7000)) {
                    throw "scope $($scopeClient.Name) 客户端未有界退出。"
                }
                $client.WaitForExit()
                Assert-Equal 0 $client.ExitCode "scope $($scopeClient.Name) 客户端收到 ACK"
                $clientEntry = Get-Content -LiteralPath $scopeClient.Diagnostic -Encoding UTF8 |
                    ForEach-Object { $_ | ConvertFrom-Json } |
                    Where-Object {
                        $_.event -eq 'InstanceActivationSucceeded' -and
                        $_.processId -eq $client.Id
                    }
                Assert-Equal 1 @($clientEntry).Count "scope $($scopeClient.Name) 客户端只记录一次成功"
                Assert-Equal ([int] $scopeClient.ServerPid) ([int] $clientEntry.activationServerProcessId `
                    ) "scope $($scopeClient.Name) ACK 绑定自己的 server PID"
            }
            Assert-Equal $settingsHashBefore (Get-FileHash -LiteralPath $settingsPath -Algorithm SHA256).Hash `
                'scope A 激活不写设置'
            Assert-Equal $settingsHashBeforeB (Get-FileHash -LiteralPath $settingsPathB -Algorithm SHA256).Hash `
                'scope B 激活不写设置'
            [pscustomobject]@{
                Passed = $script:assertions
                Failed = 0
                InstanceActivationOnly = $true
                ProcessScenarios = 2
            } | ConvertTo-Json
            return
        }

        if ($InstanceActivationListenerBehavior -eq 'CallbackFailureOnce') {
            $firstSecondaryOutputPath = Join-Path $testRoot 'secondary-first.stdout.txt'
            $firstSecondaryErrorPath = Join-Path $testRoot 'secondary-first.stderr.txt'
            $firstSecondaryProcess = Start-Process -FilePath $dotnet -ArgumentList $secondaryArguments `
                -WorkingDirectory $testRoot -PassThru -WindowStyle Hidden `
                -RedirectStandardOutput $firstSecondaryOutputPath -RedirectStandardError $firstSecondaryErrorPath
            if (-not $firstSecondaryProcess.WaitForExit(7000)) {
                throw '回调故障场景的首次第二实例未在有界期限内退出。'
            }
            $firstSecondaryProcess.WaitForExit()
            Assert-Equal 1 $firstSecondaryProcess.ExitCode '首次回调故障不能冒充激活成功'
        }

        $secondaryProcess = Start-Process -FilePath $dotnet -ArgumentList $secondaryArguments `
            -WorkingDirectory $testRoot -PassThru -WindowStyle Hidden `
            -RedirectStandardOutput $secondaryOutputPath -RedirectStandardError $secondaryErrorPath
        if (-not $secondaryProcess.WaitForExit(7000)) {
            throw '真实第二个普通实例未在有界激活期限内退出。'
        }
        $secondaryProcess.WaitForExit()
        $expectedSecondaryExitCode = if (
            $InstanceActivationListenerBehavior -in @('Normal', 'CallbackFailureOnce') -and
            $InstanceActivationClientMode -in @('Normal', 'Finalized', 'StalledRecovery')
        ) { 0 } else { 1 }
        Assert-Equal $expectedSecondaryExitCode $secondaryProcess.ExitCode '第二实例只在匹配激活 ACK 后成功退出'
        $primaryProcess.Refresh()
        Assert-Equal $false $primaryProcess.HasExited '第二实例请求结束后主实例仍存活'

        $diagnosticEntries = @(
            Get-Content -LiteralPath $diagnosticPath -Encoding UTF8 |
                ForEach-Object { $_ | ConvertFrom-Json }
        )
        $expectedClientEvent = if ($InstanceActivationClientMode -eq 'AcceptanceReadOnly') {
            'InstanceActivationSuppressedAcceptanceReadOnly'
        }
        elseif ($InstanceActivationClientMode -eq 'ValidateExercise') {
            'FatalUnhandledException'
        }
        else {
            switch ($InstanceActivationListenerBehavior) {
                'Timeout' { 'InstanceActivationTimedOut' }
                'ProtocolMismatch' { 'InstanceActivationProtocolMismatch' }
                'Disconnect' { 'InstanceActivationListenerFailed' }
                'CallbackFailureOnce' { 'InstanceActivationSucceeded' }
                default { 'InstanceActivationSucceeded' }
            }
        }
        $activationClientResult = @(
            $diagnosticEntries |
                Where-Object { $_.event -eq $expectedClientEvent }
        )
        $activationHandled = @(
            $diagnosticEntries |
                Where-Object { $_.event -eq 'InstanceActivationHandled' }
        )
        $activationRequestsReceived = @(
            $diagnosticEntries |
                Where-Object { $_.event -eq 'InstanceActivationRequestReceived' }
        )
        Assert-Equal 1 $activationClientResult.Count '第二实例记录一次明确激活结果'
        Assert-Equal 0 @(
            $diagnosticEntries |
                Where-Object {
                    $_.event -eq 'InstanceActivationSucceeded' -and
                    $expectedClientEvent -ne 'InstanceActivationSucceeded'
                }
        ).Count '超时或协议/监听失败不冒充激活成功'
        $expectedRequestCount = if ($InstanceActivationListenerBehavior -eq 'CallbackFailureOnce') {
            2
        }
        elseif ($InstanceActivationClientMode -in @('Normal', 'Finalized', 'StalledRecovery')) {
            1
        }
        else { 0 }
        Assert-Equal $expectedRequestCount $activationRequestsReceived.Count '第二个普通实例只发送一次请求，非交互模式零请求'
        $allowedActivationDiagnosticFields = @(
            'schemaVersion',
            'timestampUtc',
            'level',
            'event',
            'processId',
            'sessionId',
            'runtimeVersion',
            'activationScopeId',
            'activationRequestId',
            'activationServerProcessId',
            'activationForegroundPermissionGranted',
            'activationResult',
            'activationStateBefore',
            'activationStateAfter',
            'interactiveFloatingWindowCount',
            'activationForegroundWindowHandle',
            'activationFloatingWindowIsActive',
            'mainWindowVisibleBefore',
            'mainWindowVisibleAfter',
            'managerStatePreserved'
        )
        $unexpectedActivationDiagnosticFields = @(
            $diagnosticEntries |
                Where-Object { $_.event -like 'InstanceActivation*' } |
                ForEach-Object { $_.PSObject.Properties.Name } |
                Where-Object { $_ -notin $allowedActivationDiagnosticFields }
        )
        Assert-Equal 0 $unexpectedActivationDiagnosticFields.Count '激活诊断只含结果与非内容身份/窗口状态'
        if ($InstanceActivationListenerBehavior -in @('Normal', 'CallbackFailureOnce') -and
            $InstanceActivationClientMode -in @('Normal', 'Finalized', 'StalledRecovery')) {
            Assert-Equal 1 $activationHandled.Count '主实例处理一次激活请求'
            Assert-Equal (
                [string] $activationClientResult[0].activationScopeId
            ) (
                [string] $activationHandled[0].activationScopeId
            ) '激活 ACK 绑定同一非内容 scope identity'
            Assert-Equal (
                [string] $activationClientResult[0].activationRequestId
            ) (
                [string] $activationHandled[0].activationRequestId
            ) '激活 ACK 绑定同一 request identity'
            Assert-Equal $primaryProcess.Id ([int] $activationClientResult[0].activationServerProcessId `
                ) '前台授权只绑定命名管道真实 server PID'
            $expectedStateBefore = switch ($InstanceActivationTestState) {
                'Tray' { 'Tray' }
                'Hidden' { 'Hidden' }
                'Minimized' { 'Minimized' }
                default { 'VisibleNormal' }
            }
            Assert-Equal $expectedStateBefore ([string] $activationHandled[0].activationStateBefore) '悬浮窗激活前状态'
            Assert-Equal 'VisibleNormal' ([string] $activationHandled[0].activationStateAfter) '激活后悬浮窗恢复为可见正常状态'
            Assert-Equal (
                $InstanceActivationTestState -eq 'MainVisible'
            ) (
                [bool] $activationHandled[0].mainWindowVisibleBefore
            ) '激活前管理窗口可见状态保持测试输入'
            Assert-Equal (
                [bool] $activationHandled[0].mainWindowVisibleBefore
            ) (
                [bool] $activationHandled[0].mainWindowVisibleAfter
            ) '激活不显示或隐藏管理窗口'
            Assert-Equal 1 ([int] $activationHandled[0].interactiveFloatingWindowCount) '第二次普通启动后仍只有一个交互悬浮窗'
            Assert-Equal $true ([bool] $activationHandled[0].managerStatePreserved `
                ) '激活保持工作区、筛选与编辑草稿状态'
            $visibleWindows = @(Get-VisibleProcessWindows -ProcessId $primaryProcess.Id)
            $floatingWindows = @($visibleWindows | Where-Object Title -eq '敏敏子悬浮窗')
            Assert-Equal 1 $floatingWindows.Count 'Win32 枚举确认主进程只有一个可见悬浮窗'
            $foregroundMatchesFloating = [long] $floatingWindows[0].Handle -eq
                [long] $activationHandled[0].activationForegroundWindowHandle
            Assert-Equal $true (
                $foregroundMatchesFloating -or
                ([bool] $activationHandled[0].activationFloatingWindowIsActive -and
                 -not [bool] $activationClientResult[0].activationForegroundPermissionGranted)
            ) '匹配 ACK 由真实前台 HWND 或 WPF IsActive 证明'
            Assert-Equal $floatingWindows.Count ([int] $activationHandled[0].interactiveFloatingWindowCount `
                ) 'WPF FloatingWindow 计数与外部可见窗口枚举一致'
            if ($InstanceActivationListenerBehavior -eq 'CallbackFailureOnce') {
                Assert-Equal 1 @(
                    $diagnosticEntries |
                        Where-Object {
                            $_.event -eq 'InstanceActivationListenerFailed' -and
                            $_.processId -eq $primaryProcess.Id
                        }
                ).Count '主实例记录首次回调故障且 listener 继续服务'
            }
            if ($InstanceActivationClientMode -eq 'StalledRecovery') {
                Assert-Equal 1 @(
                    $diagnosticEntries |
                        Where-Object {
                            $_.event -eq 'InstanceActivationListenerFailed' -and
                            $_.processId -eq $primaryProcess.Id
                        }
                ).Count '主实例记录停滞连接超时且 listener 继续服务'
            }
        }
        elseif ($InstanceActivationClientMode -eq 'AcceptanceReadOnly') {
            Assert-Equal 0 $activationHandled.Count 'acceptance-read-only 调用不发送激活请求'
        }
        elseif ($InstanceActivationClientMode -eq 'ValidateExercise') {
            Assert-Equal 0 $activationHandled.Count 'validate-only exercise 探针不发送激活请求'
            Assert-Equal 0 @(
                $diagnosticEntries |
                    Where-Object { $_.event -like 'InstanceActivation*' }
            ).Count 'validate-only exercise 探针不记录交互激活结果'
        }
        Assert-Equal $settingsHashBefore (Get-FileHash -LiteralPath $settingsPath -Algorithm SHA256).Hash '激活不写设置'

        [pscustomobject]@{
            Passed = $script:assertions
            Failed = 0
            InstanceActivationOnly = $true
            ProcessScenarios = 1
        } | ConvertTo-Json
    }
    finally {
        if ($null -ne $stalledPipe) {
            $stalledPipe.Dispose()
        }
        foreach ($process in @($secondaryProcess, $firstSecondaryProcess) + @($parallelProcesses) + @($primaryProcess)) {
            if ($null -eq $process) {
                continue
            }
            try {
                if (-not $process.HasExited) {
                    $process.Kill($true)
                    [void] $process.WaitForExit(5000)
                }
            }
            finally {
                $process.Dispose()
            }
        }

        if (Test-Path -LiteralPath $testRoot -PathType Container) {
            $cleanupJson = & $safeTreeDelete `
                -Mode BatchPurge `
                -TargetPaths $testRoot `
                -CapabilityRoot $testCapabilityRoot `
                -ConfirmApply `
                -ConfirmPurge `
                -Json | Out-String
            if ($LASTEXITCODE -ne 0) {
                throw "受管激活测试目录清理失败：$testRoot"
            }
            $cleanup = $cleanupJson | ConvertFrom-Json
            if (-not $cleanup.completed -or $cleanup.purgedCount -ne 1) {
                throw "受管激活测试目录未完成清理：$testRoot"
            }
        }
    }
}

function Invoke-InstanceActivationMatrixTests {
    $pwsh = 'D:\Codex\tools\PowerShell\7.6.4\pwsh.exe'
    $scenarios = @(
        [pscustomobject]@{ Name = 'State-FloatingVisible'; Arguments = @('-InstanceActivationTestState', 'FloatingVisible') },
        [pscustomobject]@{ Name = 'State-Tray'; Arguments = @('-InstanceActivationTestState', 'Tray') },
        [pscustomobject]@{ Name = 'State-Hidden'; Arguments = @('-InstanceActivationTestState', 'Hidden') },
        [pscustomobject]@{ Name = 'State-Minimized'; Arguments = @('-InstanceActivationTestState', 'Minimized') },
        [pscustomobject]@{ Name = 'State-MainVisible'; Arguments = @('-InstanceActivationTestState', 'MainVisible') },
        [pscustomobject]@{ Name = 'Listener-Timeout'; Arguments = @('-InstanceActivationListenerBehavior', 'Timeout') },
        [pscustomobject]@{ Name = 'Listener-ProtocolMismatch'; Arguments = @('-InstanceActivationListenerBehavior', 'ProtocolMismatch') },
        [pscustomobject]@{ Name = 'Listener-Disconnect'; Arguments = @('-InstanceActivationListenerBehavior', 'Disconnect') },
        [pscustomobject]@{ Name = 'Listener-CallbackFailureOnce'; Arguments = @('-InstanceActivationListenerBehavior', 'CallbackFailureOnce') },
        [pscustomobject]@{ Name = 'Client-AcceptanceReadOnly'; Arguments = @('-InstanceActivationClientMode', 'AcceptanceReadOnly') },
        [pscustomobject]@{ Name = 'Client-Finalized'; Arguments = @('-InstanceActivationClientMode', 'Finalized') },
        [pscustomobject]@{ Name = 'Client-ValidateExercise'; Arguments = @('-InstanceActivationClientMode', 'ValidateExercise') },
        [pscustomobject]@{ Name = 'Client-StalledRecovery'; Arguments = @('-InstanceActivationClientMode', 'StalledRecovery') },
        [pscustomobject]@{ Name = 'Client-OptionGuard'; Arguments = @('-InstanceActivationClientMode', 'OptionGuard') },
        [pscustomobject]@{ Name = 'Client-DiagnosticConcurrency'; Arguments = @('-InstanceActivationClientMode', 'DiagnosticConcurrency') },
        [pscustomobject]@{ Name = 'Client-DisposeRace'; Arguments = @('-InstanceActivationClientMode', 'DisposeRace') },
        [pscustomobject]@{ Name = 'Client-Scope'; Arguments = @('-InstanceActivationClientMode', 'Scope') }
    )
    $totalPassed = 0
    $processScenarios = 0

    foreach ($scenario in $scenarios) {
        Write-Host "[instance-activation-matrix] START $($scenario.Name)"
        $childArguments = @(
            '-NoLogo',
            '-NoProfile',
            '-File',
            $PSCommandPath,
            '-InstanceActivationOnly'
        ) + @($scenario.Arguments)
        $output = & $pwsh @childArguments
        $exitCode = $LASTEXITCODE
        $outputText = @($output) -join [Environment]::NewLine
        if ($exitCode -ne 0) {
            throw "实例激活矩阵场景失败：$($scenario.Name)，exit=$exitCode`n$outputText"
        }

        try {
            $result = $outputText | ConvertFrom-Json
        }
        catch {
            throw "实例激活矩阵场景未返回有效 JSON：$($scenario.Name)`n$outputText"
        }
        if ([int] $result.Failed -ne 0 -or -not [bool] $result.InstanceActivationOnly) {
            throw "实例激活矩阵场景返回失败结果：$($scenario.Name)`n$outputText"
        }

        $totalPassed += [int] $result.Passed
        $processScenarios += [int] $result.ProcessScenarios
        Write-Host "[instance-activation-matrix] PASS $($scenario.Name) assertions=$($result.Passed)"
    }

    [pscustomobject]@{
        Passed = $totalPassed
        Failed = 0
        InstanceActivationMatrix = $true
        MatrixScenarios = $scenarios.Count
        ProcessScenarios = $processScenarios
    } | ConvertTo-Json
}

if ($InstanceActivationMatrix) {
    if ($InstanceActivationOnly -or
        $InstanceActivationTestState -ne 'FloatingVisible' -or
        $InstanceActivationListenerBehavior -ne 'Normal' -or
        $InstanceActivationClientMode -ne 'Normal') {
        throw '-InstanceActivationMatrix 不能与单场景参数组合。'
    }
    Invoke-InstanceActivationMatrixTests
    exit 0
}

if ($InstanceActivationOnly) {
    Invoke-InstanceActivationProcessTests
    exit 0
}

$sourceTreeBaselineMode = Resolve-ReferenceBaselineMode `
    -MainManifestAvailable $true `
    -FloatingManifestAvailable $true
$releasePackageBaselineMode = Resolve-ReferenceBaselineMode `
    -MainManifestAvailable $false `
    -FloatingManifestAvailable $false
Assert-Equal 'SourceTree' $sourceTreeBaselineMode '双 manifest 存在时选择源码树视觉验证模式'
Assert-Equal 'ReleasePackage' $releasePackageBaselineMode '双 manifest 缺失时选择发布包验证模式'
foreach ($partialBaseline in @(
    @{ Main = $true; Floating = $false },
    @{ Main = $false; Floating = $true }
)) {
    $partialBaselineRejected = $false
    try {
        $null = Resolve-ReferenceBaselineMode `
            -MainManifestAvailable $partialBaseline.Main `
            -FloatingManifestAvailable $partialBaseline.Floating
    }
    catch {
        $partialBaselineRejected = $true
    }

    Assert-Equal $true $partialBaselineRejected '只存在一套视觉 manifest 时失败闭锁'
}

$referenceBaselineMode = Resolve-ReferenceBaselineMode `
    -MainManifestAvailable $mainReferenceBaselineRequired `
    -FloatingManifestAvailable $floatingReferenceBaselineRequired
[Environment]::SetEnvironmentVariable(
    'MINMIN_COUNTDOWN_VISUAL_SOURCE_ROOT',
    $(if ($referenceBaselineMode -eq 'SourceTree') { $PSScriptRoot } else { $null }),
    [EnvironmentVariableTarget]::Process
)
[Environment]::SetEnvironmentVariable(
    'MINMIN_COUNTDOWN_FLOATING_BASELINE_GENERATE',
    $null,
    [EnvironmentVariableTarget]::Process
)
[Environment]::SetEnvironmentVariable(
    'MINMIN_COUNTDOWN_MAIN_BASELINE_GENERATE',
    $null,
    [EnvironmentVariableTarget]::Process
)

if ($MigrationParityOnly) {
    $migrationParityRoot = Join-Path 'D:\Codex\.codex\run-evidence\clock-countdown-migration-parity' ([guid]::NewGuid().ToString('N'))
    $migrationSettingsModule = Join-Path $PSScriptRoot 'Countdown.Settings.psm1'
    $migrationDotnet = 'D:\Codex\tools\dotnet\10.0\dotnet.exe'
    $migrationVerificationDll = Join-Path $PSScriptRoot 'src\MinminCountdown.Verification\bin\Release\net10.0-windows\MinminCountdown.Verification.dll'
    $migrationUtc = [DateTimeOffset] '2031-09-10T02:16:00Z'
    $migrationFiles = [System.Collections.Generic.List[string]]::new()
    $migrationFixtures = @(
        [pscustomobject]@{ Version = 3; Json = '{"schemaVersion":3,"timeZoneId":"China Standard Time","displayPrecision":"Minute","monthlySalary":12345.67,"workStart":"08:30","workEnd":"17:45","countdowns":[{"id":"countdown-v3","name":"版本三倒计时","targetUtc":"2030-03-04T05:06:07.0000000+00:00"}]}' },
        [pscustomobject]@{ Version = 4; Json = '{"schemaVersion":4,"timeZoneId":"China Standard Time","displayPrecision":"Hour","monthlySalary":8800,"workStart":"09:00","workEnd":"18:00","countdowns":[{"id":"countdown-v4","name":"版本四倒计时","targetUtc":"2030-04-05T06:07:08.0000000+00:00"}],"notes":[{"id":"note-v4","text":"版本四便签","isCompleted":true}]}' },
        [pscustomobject]@{ Version = 5; Json = '{"schemaVersion":5,"timeZoneId":"China Standard Time","displayPrecision":"Second","monthlySalary":10000,"workStart":"09:15","workEnd":"18:15","tags":[{"id":"tag-work","name":"工作","color":"#FF8A70"}],"countdowns":[{"id":"countdown-v5","name":"版本五倒计时","targetUtc":"2030-05-06T07:08:09.0000000+00:00","tagIds":["tag-work"]}],"notes":[{"id":"note-v5","text":"版本五便签","isCompleted":false,"tagIds":["tag-work"]}]}' },
        [pscustomobject]@{ Version = 6; Json = '{"schemaVersion":6,"timeZoneId":"China Standard Time","displayPrecision":"Day","monthlySalary":15000,"workStart":"08:00","workEnd":"17:00","tags":[],"countdowns":[{"id":"countdown-v6","name":"版本六倒计时","targetUtc":"2030-06-07T08:09:10.0000000+00:00","tagIds":[]}],"notes":[{"id":"note-v6","text":"版本六便签","date":"2030-06-07","isCompleted":false,"tagIds":[]}]}' },
        [pscustomobject]@{ Version = 7; Json = '{"schemaVersion":7,"timeZoneId":"China Standard Time","displayPrecision":"Second","monthlySalary":20000.5,"workStart":"09:00","workEnd":"18:30","tags":[],"countdowns":[{"id":"countdown-v7","name":"版本七倒计时","targetUtc":"2031-09-10T11:12:13.0000000+00:00","tagIds":[]}],"notes":[{"id":"note-v7","text":"版本七循环提醒","date":"2031-09-10","isCompleted":false,"tagIds":[],"reminderIntervalMinutes":30,"reminderStartUtc":"2031-09-10T01:00:00.0000000+00:00"}]}' },
        [pscustomobject]@{ Version = 8; Json = '{"schemaVersion":8,"timeZoneId":"China Standard Time","displayPrecision":"Second","monthlySalary":20000.5,"workStart":"09:00","workEnd":"18:30","tags":[],"countdowns":[{"id":"countdown-v8","name":"版本八倒计时","targetUtc":"2031-09-10T11:12:13.0000000+00:00","tagIds":[]}],"notes":[{"id":"note-v8","text":"版本八已送达提醒","date":"2031-09-10","isCompleted":false,"tagIds":[],"reminder":{"type":"once","firstDueUtc":"2031-09-10T01:30:00.0000000+00:00","delivery":"delivered"}}]}' },
        [pscustomobject]@{ Version = 9; Json = '{"schemaVersion":9,"timeZoneId":"China Standard Time","displayPrecision":"Second","monthlySalary":20000.5,"workStart":"09:00","workEnd":"18:30","themePreference":"Night","tags":[],"countdowns":[{"id":"countdown-v9","name":"版本九倒计时","targetUtc":"2031-09-10T11:12:13.0000000+00:00","tagIds":[]}],"notes":[{"id":"note-v9-repeat","text":"已开始循环提醒","date":"2031-09-10","isCompleted":false,"tagIds":[],"reminder":{"type":"repeat","firstDueUtc":"2031-09-10T01:00:00.0000000+00:00","intervalMinutes":30}}]}' }
    )
    try {
        foreach ($requiredPath in @($migrationSettingsModule, $migrationDotnet, $migrationVerificationDll)) {
            if (-not (Test-Path -LiteralPath $requiredPath -PathType Leaf)) {
                throw "迁移 parity 缺少受管依赖：$requiredPath"
            }
        }
        New-Item -ItemType Directory -Path $migrationParityRoot -Force | Out-Null
        Import-Module $migrationSettingsModule -Force
        foreach ($fixture in $migrationFixtures) {
            $migrationFixturePath = Join-Path $migrationParityRoot "schema-v$($fixture.Version).settings.json"
            $migrationCSharpPath = Join-Path $migrationParityRoot "schema-v$($fixture.Version).csharp.json"
            foreach ($path in @(
                    $migrationFixturePath,
                    $migrationCSharpPath,
                    "$migrationCSharpPath.tmp",
                    "$migrationCSharpPath.backup1",
                    "$migrationCSharpPath.backup2",
                    "$migrationCSharpPath.backup2.tmp",
                    "$migrationCSharpPath.backup2.rollback"
                )) {
                [void] $migrationFiles.Add($path)
            }
            [System.IO.File]::WriteAllText($migrationFixturePath, $fixture.Json, [System.Text.UTF8Encoding]::new($false))
            $migrationFixtureHash = (Get-FileHash -LiteralPath $migrationFixturePath -Algorithm SHA256).Hash
            $powerShellCanonical = ConvertTo-CountdownSettingsJson -State (
                ConvertFrom-CountdownSettingsObject `
                    -Settings ($fixture.Json | ConvertFrom-Json -DateKind String) `
                    -MigrationUtc $migrationUtc)
            $migrationOutput = Invoke-CheckedNativeCommand `
                -FilePath $migrationDotnet `
                -Operation "运行 schema v$($fixture.Version) C#/PowerShell migration parity" `
                -ArgumentList @(
                    $migrationVerificationDll,
                    'round-trip',
                    '--input', $migrationFixturePath,
                    '--output', $migrationCSharpPath,
                    '--migration-utc', $migrationUtc.ToString('o'),
                    '--json')
            $migrationOutput | ConvertFrom-Json | Out-Null
            $csharpCanonical = [System.IO.File]::ReadAllText($migrationCSharpPath, [System.Text.Encoding]::UTF8)
            Assert-Equal $powerShellCanonical $csharpCanonical "schema v$($fixture.Version) 使用同一迁移时刻得到相同 canonical v10"
            Assert-Equal $migrationFixtureHash (Get-FileHash -LiteralPath $migrationFixturePath -Algorithm SHA256).Hash "schema v$($fixture.Version) parity 保持共享输入字节不变"

            $canonicalObject = $csharpCanonical | ConvertFrom-Json -DateKind String
            Assert-Equal 10 ([int] $canonicalObject.schemaVersion) "schema v$($fixture.Version) 迁移到 canonical v10"
            if ($fixture.Version -ge 7) {
                $rule = $canonicalObject.notes[0].reminderRules[0]
                $ruleId = [string] $rule.id
                $state = $canonicalObject.reminderStates.PSObject.Properties[$ruleId].Value
                Assert-Equal $null $state.lastDispositionOccurrenceKey "schema v$($fixture.Version) 不伪造 disposition"
                Assert-Equal $null $state.activeSnooze "schema v$($fixture.Version) 不伪造 snooze"
                if ($fixture.Version -eq 8) {
                    Assert-Equal '2031-09-10T01:30:00.0000000+00:00' ([string] $rule.scheduledUtc) 'schema v8 保留一次提醒时刻'
                    Assert-Equal ("{0}|2031-09-10T01:30:00.0000000+00:00" -f $ruleId) ([string] $state.lastDeliveredOccurrenceKey) 'schema v8 保留 delivery occurrence'
                }
                else {
                    $expectedFirstDueUtc = if ($fixture.Version -eq 7) {
                        '2031-09-10T01:30:00.0000000+00:00'
                    }
                    else {
                        '2031-09-10T01:00:00.0000000+00:00'
                    }
                    Assert-Equal $expectedFirstDueUtc ([string] $rule.firstDueUtc) "schema v$($fixture.Version) 保留重复提醒起点"
                    Assert-Equal '2031-09-10T02:00:00.0000000+00:00' ([string] $state.handledThroughUtc) "schema v$($fixture.Version) cursor 以固定迁移时刻计算"
                    Assert-Equal $null $state.lastDeliveredOccurrenceKey "schema v$($fixture.Version) 不伪造 delivery"
                }
                if ($fixture.Version -eq 9) {
                    Assert-Equal 'legacy-d71c35d11bb6b27f66e7c582ef71e036a4f36314a86250d45318b5d32763ca75' $ruleId 'schema v9 repeat 保持稳定规则 ID'
                }
            }
        }
        [pscustomobject]@{ Passed = $script:assertions; Failed = 0; Scope = 'MigrationParity' } | ConvertTo-Json
        exit 0
    }
    finally {
        foreach ($path in $migrationFiles) {
            if (Test-Path -LiteralPath $path -PathType Leaf) {
                [System.IO.File]::Delete($path)
            }
        }
        if (Test-Path -LiteralPath $migrationParityRoot -PathType Container) {
            [System.IO.Directory]::Delete($migrationParityRoot, $false)
        }
    }
}

if (-not ('MinminCountdown.Tests.BundleAssetProbe' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.IO;

namespace MinminCountdown.Tests;

public static class BundleAssetProbe
{
    public static string[] FindExactPayloads(string bundlePath, string[] payloadPaths)
    {
        var bundle = File.ReadAllBytes(bundlePath);
        var matches = new List<string>();
        foreach (var payloadPath in payloadPaths)
        {
            var payload = File.ReadAllBytes(payloadPath);
            if (bundle.AsSpan().IndexOf(payload) >= 0)
            {
                matches.Add(Path.GetFileName(payloadPath));
            }
        }

        return matches.ToArray();
    }
}
'@
}

$verificationRunner = Join-Path $PSScriptRoot 'verification\MinminCountdown.Verification.exe'
$application = Join-Path $PSScriptRoot 'app\MinminCountdown.exe'
$adapter = Join-Path $PSScriptRoot '敏敏子倒计时.ps1'
$settingsModule = Join-Path $PSScriptRoot 'Countdown.Settings.psm1'
$runtimeModule = Join-Path $PSScriptRoot 'Countdown.Runtime.psm1'
$pwsh = (Get-Process -Id $PID).Path

if (-not (Test-Path -LiteralPath $verificationRunner -PathType Leaf)) {
    throw "未找到自包含 C# 验证程序：$verificationRunner。请先运行 Build-CountdownApplication.ps1。"
}
if (-not (Test-Path -LiteralPath $application -PathType Leaf)) {
    throw "未找到待验证的 C# 应用宿主：$application。请先运行 Build-CountdownApplication.ps1。"
}
$dayRasterComponentFiles = @(
    'component-day-outer-shell.png',
    'component-day-header-blank.png',
    'component-day-card-blank.png',
    'component-day-mascots.png'
)
$currentFloatingRuntimeAssetFiles = @(
    'floating-plush-mymelody-reference.png',
    'floating-plush-cinnamoroll-reference.png',
    'floating-plush-kuromi-reference.png'
) + $dayRasterComponentFiles
foreach ($file in $currentFloatingRuntimeAssetFiles) {
    $publishedComponent = Join-Path $PSScriptRoot "app\assets\floating-plush\$file"
    if (-not (Test-Path -LiteralPath $publishedComponent -PathType Leaf)) {
        throw "C# 发布缺少复刻版悬浮窗资产：$publishedComponent"
    }
}
Import-Module (Join-Path $PSScriptRoot 'Countdown.ReleaseContract.psm1') -Force
$releaseContract = Get-CountdownReleaseContract
$releaseReferenceLeaks = @(
    @($releaseContract.PackageFiles) +
    @($releaseContract.InstalledFiles) +
    @($releaseContract.BuildAssets.SourcePath) +
    @($releaseContract.BuildAssets.PublishedPath) |
        Where-Object {
            [string] $_ -like 'assets\ui-references\*' -or
            [string] $_ -like 'app\assets\ui-references\*'
        }
)
Assert-Equal 0 $releaseReferenceLeaks.Count '520×390 参考 PNG 与 manifest 不进入发布文件清单'
$retiredFloatingRuntimeAssetFiles = @(
    'floating-plush-mymelody-front.png',
    'floating-plush-mymelody-pose.png',
    'floating-plush-cinnamoroll-front.png',
    'floating-plush-cinnamoroll-pose.png',
    'floating-plush-kuromi-front.png',
    'floating-plush-kuromi-pose.png'
)
foreach ($file in $retiredFloatingRuntimeAssetFiles) {
    $retiredPublishedAsset = Join-Path $PSScriptRoot "app\assets\floating-plush\$file"
    if (Test-Path -LiteralPath $retiredPublishedAsset -PathType Leaf) {
        throw "C# 发布仍包含已淘汰的旧悬浮窗资产：$retiredPublishedAsset"
    }
}
$retiredSourceAssetPaths = @(
    $retiredFloatingRuntimeAssetFiles | ForEach-Object {
        Join-Path $PSScriptRoot "assets\floating-plush\$_"
    }
)
$embeddedRetiredAssets = [MinminCountdown.Tests.BundleAssetProbe]::FindExactPayloads(
    $application,
    [string[]] $retiredSourceAssetPaths
)
Assert-Equal '' ($embeddedRetiredAssets -join ',') 'C# 单文件 EXE 不嵌入已淘汰的旧悬浮窗 PNG'
if (-not (Test-Path -LiteralPath $adapter -PathType Leaf)) {
    throw "未找到 PowerShell 薄 adapter：$adapter"
}
if (-not (Test-Path -LiteralPath $settingsModule -PathType Leaf)) {
    throw "未找到 PowerShell 设置契约 module：$settingsModule"
}
if (-not (Test-Path -LiteralPath $runtimeModule -PathType Leaf)) {
    throw "未找到 PowerShell 运行协议 module：$runtimeModule"
}
if (-not [string]::Equals(
        [System.IO.Path]::GetFileName($pwsh),
        'pwsh.exe',
        [System.StringComparison]::OrdinalIgnoreCase
    )) {
    throw "C# 验证必须由 pwsh.exe 执行，当前宿主：$pwsh"
}

$safeTreeDelete = 'D:\Codex\.codex\scripts\invoke-safe-tree-delete.cmd'
$defaultTestCapabilityRoot = [System.IO.Path]::GetFullPath('D:\Codex\.codex\run-evidence')
$testCapabilityRoot = $defaultTestCapabilityRoot
$testGroupRoot = Join-Path $testCapabilityRoot 'clock-countdown-csharp-tests'
if (-not [string]::IsNullOrWhiteSpace($OfficeDeploymentStagingRoot)) {
    $officeDeploymentRoot = [System.IO.Path]::GetFullPath('D:\Codex\deploy')
    $resolvedStagingRoot = [System.IO.Path]::GetFullPath($OfficeDeploymentStagingRoot)
    $resolvedSourceRoot = [System.IO.Path]::GetFullPath($PSScriptRoot)
    $expectedSourceRoot = [System.IO.Path]::GetFullPath((Join-Path $resolvedStagingRoot 'source'))
    if (-not [string]::Equals(
            [System.IO.Path]::GetDirectoryName($resolvedStagingRoot),
            $officeDeploymentRoot,
            [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "办公室部署 staging 必须是受管根的直接子目录：$resolvedStagingRoot"
    }
    if (-not [string]::Equals(
            $resolvedSourceRoot,
            $expectedSourceRoot,
            [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "C# 测试脚本不属于指定的办公室部署 staging：$resolvedSourceRoot"
    }
}
$testCapabilityRootPrefix = $testCapabilityRoot.TrimEnd(
    [System.IO.Path]::DirectorySeparatorChar,
    [System.IO.Path]::AltDirectorySeparatorChar
) + [System.IO.Path]::DirectorySeparatorChar
$testRoot = [System.IO.Path]::GetFullPath(
    (Join-Path $testGroupRoot ("run-" + [guid]::NewGuid().ToString('N')))
)
if (-not (Test-Path -LiteralPath $safeTreeDelete -PathType Leaf)) {
    throw "未找到受管 C# 测试目录清理入口：$safeTreeDelete"
}
if (-not $testRoot.StartsWith($testCapabilityRootPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "拒绝在受管测试证据根外创建 C# 测试数据：$testRoot"
}

$fixturePath = Join-Path $testRoot 'settings-input.json'
$powerShellCanonicalPath = Join-Path $testRoot 'settings-powershell-canonical.json'
$roundTripPath = Join-Path $testRoot 'settings-round-trip.json'
$mutexSettingsPath = Join-Path $testRoot 'mutex.settings.json'
$mutexReadyPath = Join-Path $testRoot 'mutex-ready.json'
$mutexHolderOutputPath = Join-Path $testRoot 'mutex-holder.stdout.txt'
$mutexHolderErrorPath = Join-Path $testRoot 'mutex-holder.stderr.txt'
$directDiagnosticPath = Join-Path $testRoot 'direct-application.jsonl'
$adapterDiagnosticPath = Join-Path $testRoot 'adapter-application.jsonl'
$failedValidationDiagnosticPath = Join-Path $testRoot 'failed-validation-application.jsonl'
$noteExercisePath = Join-Path $testRoot 'note-exercise.settings.json'
$noteExerciseDiagnosticPath = Join-Path $testRoot 'note-exercise-application.jsonl'
$interactiveRoot = Join-Path $testRoot 'interactive-application-root'
$interactiveApplicationDirectory = Join-Path $interactiveRoot 'app'
$interactiveApplicationPath = Join-Path $interactiveApplicationDirectory 'MinminCountdown.exe'
$interactiveAdapterPath = Join-Path $interactiveRoot '敏敏子倒计时.ps1'
$interactiveSettingsPath = Join-Path $interactiveRoot 'settings.json'
$interactiveStartupRequestPath = Join-Path $interactiveRoot 'startup-request.json'
$interactiveRuntimeStatusPath = Join-Path $interactiveRoot 'runtime-status.json'
$interactiveAcceptanceStatusPath = Join-Path $interactiveRoot 'acceptance-status.json'
$interactiveDiagnosticPath = Join-Path $interactiveRoot 'logs\application.jsonl'
$interactiveAdapterOutputPath = Join-Path $testRoot 'interactive-adapter.stdout.txt'
$interactiveAdapterErrorPath = Join-Path $testRoot 'interactive-adapter.stderr.txt'
$duplicateApplicationOutputPath = Join-Path $testRoot 'duplicate-application.stdout.txt'
$duplicateApplicationErrorPath = Join-Path $testRoot 'duplicate-application.stderr.txt'
$requiredRequestOutputPath = Join-Path $testRoot 'required-request.stdout.txt'
$requiredRequestErrorPath = Join-Path $testRoot 'required-request.stderr.txt'
$unknownSchemaOutputPath = Join-Path $testRoot 'unknown-schema.stdout.txt'
$unknownSchemaErrorPath = Join-Path $testRoot 'unknown-schema.stderr.txt'
$utf8NoBom = [System.Text.UTF8Encoding]::new($false)
$mutexHolderProcess = $null
$interactiveAdapterProcess = $null
$interactiveApplicationProcess = $null
$duplicateApplicationProcess = $null
$fixture = @'
{
  "schemaVersion": 7,
  "timeZoneId": "China Standard Time",
  "displayPrecision": "Second",
  "monthlySalary": 23000,
  "workStart": "09:00",
  "workEnd": "18:00",
  "tags": [
    {
      "id": "work",
      "name": "Work",
      "color": "#F4B183"
    }
  ],
  "countdowns": [
    {
      "id": "migration-test",
      "name": "Migration verification",
      "targetUtc": "2026-12-31T10:00:00.0000000+00:00",
      "tagIds": [
        "work"
      ]
    }
  ],
  "notes": [
    {
      "id": "migration-note",
      "text": "Round-trip verification",
      "date": "2026-12-31",
      "isCompleted": false,
      "tagIds": [
        "work"
      ],
      "reminderIntervalMinutes": null,
      "reminderStartUtc": null
    }
  ]
}
'@

try {
    New-Item -ItemType Directory -Path $testRoot | Out-Null
    [System.IO.File]::WriteAllText($fixturePath, $fixture, $utf8NoBom)
    $fixtureHash = (Get-FileHash -LiteralPath $fixturePath -Algorithm SHA256).Hash
    Import-Module -Name $settingsModule -Force
    Import-Module -Name $runtimeModule -Force
    $powerShellState = ConvertFrom-CountdownSettingsObject -Settings (
        Get-Content -LiteralPath $fixturePath -Raw -Encoding UTF8 | ConvertFrom-Json
    )
    $powerShellCanonical = ConvertTo-CountdownSettingsJson -State $powerShellState
    [System.IO.File]::WriteAllText($powerShellCanonicalPath, $powerShellCanonical, $utf8NoBom)
    $powerShellCanonicalHash = (Get-FileHash -LiteralPath $powerShellCanonicalPath -Algorithm SHA256).Hash

    $verifyOutput = Invoke-CheckedNativeCommand -FilePath $verificationRunner -Operation '运行 C# 内建验证' -ArgumentList @(
        'verify',
        '--json',
        '--settings-file',
        $fixturePath
    )
    $verifyOutput | ConvertFrom-Json | Out-Null
    Assert-Equal $fixtureHash (Get-FileHash -LiteralPath $fixturePath -Algorithm SHA256).Hash 'C# 内建验证保持输入设置不变'

    $roundTripOutput = Invoke-CheckedNativeCommand -FilePath $verificationRunner -Operation '运行 C# 设置 round-trip' -ArgumentList @(
        'round-trip',
        '--input',
        $powerShellCanonicalPath,
        '--output',
        $roundTripPath,
        '--json'
    )
    $roundTripOutput | ConvertFrom-Json | Out-Null
    Assert-Equal $powerShellCanonicalHash (Get-FileHash -LiteralPath $powerShellCanonicalPath -Algorithm SHA256).Hash 'C# round-trip 保持 PowerShell 输入设置不变'
    Assert-Equal $fixtureHash (Get-FileHash -LiteralPath $fixturePath -Algorithm SHA256).Hash '跨实现 round-trip 保持来源 fixture 不变'
    if (-not (Test-Path -LiteralPath $roundTripPath -PathType Leaf)) {
        throw "C# round-trip 未生成显式输出：$roundTripPath"
    }
    $canonicalSettings = Get-Content -LiteralPath $roundTripPath -Raw -Encoding UTF8 | ConvertFrom-Json
    Assert-Equal 10 $canonicalSettings.schemaVersion 'C# round-trip 输出 schemaVersion'
    Assert-Equal 'China Standard Time' $canonicalSettings.timeZoneId 'C# round-trip 输出时区'
    Assert-Equal 'Auto' $canonicalSettings.themePreference 'C# round-trip 为旧设置补齐自动主题偏好'
    $powerShellReloadedState = ConvertFrom-CountdownSettingsObject -Settings $canonicalSettings
    $powerShellReloadedCanonical = ConvertTo-CountdownSettingsJson -State $powerShellReloadedState
    Assert-Equal $powerShellCanonical $powerShellReloadedCanonical 'PowerShell/C# 双向领域值等价'

    $powerShellMutex = Enter-CountdownSettingsWriteMutex -Path $mutexSettingsPath -TimeoutSeconds 1
    try {
        $blockedCSharpProbe = Invoke-NativeCommandResult -FilePath $verificationRunner -ArgumentList @(
            'mutex-probe',
            '--settings-path',
            $mutexSettingsPath,
            '--timeout-seconds',
            '1',
            '--json'
        )
        Assert-Equal 1 $blockedCSharpProbe.ExitCode 'PowerShell 持锁时 C# probe 超时退出码'
        $blockedCSharpProbeJson = $blockedCSharpProbe.Output | ConvertFrom-Json
        Assert-Equal $false ([bool] $blockedCSharpProbeJson.acquired) 'PowerShell 持锁时 C# 不得获取 mutex'
    }
    finally {
        Exit-CountdownSettingsWriteMutex -Mutex $powerShellMutex
    }

    $releasedCSharpProbe = Invoke-CheckedNativeCommand -FilePath $verificationRunner -Operation '验证 PowerShell 释放后 C# 获取 mutex' -ArgumentList @(
        'mutex-probe',
        '--settings-path',
        $mutexSettingsPath,
        '--timeout-seconds',
        '1',
        '--json'
    ) | ConvertFrom-Json
    Assert-Equal $true ([bool] $releasedCSharpProbe.acquired) 'PowerShell 释放后 C# 可获取 mutex'

    $mutexHolderArguments = @(
        'hold-mutex',
        '--settings-path',
        ('"{0}"' -f $mutexSettingsPath),
        '--ready-file',
        ('"{0}"' -f $mutexReadyPath),
        '--milliseconds',
        '3000',
        '--json'
    )
    $mutexHolderProcess = Start-Process -FilePath $verificationRunner -ArgumentList $mutexHolderArguments -PassThru -WindowStyle Hidden `
        -RedirectStandardOutput $mutexHolderOutputPath -RedirectStandardError $mutexHolderErrorPath
    $mutexReadyDeadline = [DateTimeOffset]::UtcNow.AddSeconds(5)
    while (-not (Test-Path -LiteralPath $mutexReadyPath -PathType Leaf) -and
        -not $mutexHolderProcess.HasExited -and
        [DateTimeOffset]::UtcNow -lt $mutexReadyDeadline) {
        Start-Sleep -Milliseconds 50
    }
    if (-not (Test-Path -LiteralPath $mutexReadyPath -PathType Leaf)) {
        $holderError = if (Test-Path -LiteralPath $mutexHolderErrorPath) {
            Get-Content -LiteralPath $mutexHolderErrorPath -Raw
        }
        else {
            ''
        }
        throw "C# mutex holder 未进入持锁状态：$holderError"
    }

    $powerShellTimedOut = $false
    $unexpectedPowerShellMutex = $null
    try {
        $unexpectedPowerShellMutex = Enter-CountdownSettingsWriteMutex -Path $mutexSettingsPath -TimeoutSeconds 1
    }
    catch {
        if ($_.Exception.Message -notlike '等待设置写入锁超时*') {
            throw
        }
        $powerShellTimedOut = $true
    }
    finally {
        if ($null -ne $unexpectedPowerShellMutex) {
            Exit-CountdownSettingsWriteMutex -Mutex $unexpectedPowerShellMutex
        }
    }
    Assert-Equal $true $powerShellTimedOut 'C# 持锁时 PowerShell 超时'
    if (-not $mutexHolderProcess.WaitForExit(5000)) {
        throw 'C# mutex holder 未在预期时间内释放。'
    }
    Assert-Equal 0 $mutexHolderProcess.ExitCode 'C# mutex holder 退出码'
    $mutexHolderProcess = $null
    $releasedPowerShellMutex = Enter-CountdownSettingsWriteMutex -Path $mutexSettingsPath -TimeoutSeconds 1
    try {
        Assert-Equal $true ($null -ne $releasedPowerShellMutex) 'C# 释放后 PowerShell 可获取 mutex'
    }
    finally {
        Exit-CountdownSettingsWriteMutex -Mutex $releasedPowerShellMutex
    }

    $canonicalHash = (Get-FileHash -LiteralPath $roundTripPath -Algorithm SHA256).Hash
    Invoke-CheckedNativeCommand -FilePath $application -Operation '运行 C# 应用 --validate-only' -ArgumentList @(
        '--validate-only',
        '--settings-file',
        $roundTripPath,
        '--diagnostic-log-path',
        $directDiagnosticPath
    ) | Out-Null
    Assert-Equal $canonicalHash (Get-FileHash -LiteralPath $roundTripPath -Algorithm SHA256).Hash '应用 validate-only 保持设置不变'

    $adapterValidationOutput = Invoke-CheckedNativeCommand -FilePath $pwsh -Operation '经 PowerShell adapter 运行 C# 应用 --validate-only' -ArgumentList @(
        '-NoLogo',
        '-NoProfile',
        '-STA',
        '-File',
        $adapter,
        '-ValidateOnly',
        '-ExerciseThemePreference',
        '-SettingsFile',
        $roundTripPath,
        '-DiagnosticLogPath',
        $adapterDiagnosticPath
    )
    $adapterValidationDocument = [System.Text.Json.JsonDocument]::Parse($adapterValidationOutput)
    try {
        $adapterValidationJsonKinds = [System.Collections.Generic.Dictionary[string, string]]::new(
            [System.StringComparer]::Ordinal)
        $adapterValidationPropertyNames = @(
            foreach ($property in $adapterValidationDocument.RootElement.EnumerateObject()) {
                $jsonKind = switch ($property.Value.ValueKind.ToString()) {
                    'Null' { 'Null' }
                    'True' { 'Boolean' }
                    'False' { 'Boolean' }
                    'String' { 'String' }
                    'Object' { 'Object' }
                    'Array' { 'Array' }
                    'Number' { 'Number' }
                    default { throw "未知的 validate-only JSON 值类型：$($property.Value.ValueKind)" }
                }
                $adapterValidationJsonKinds.Add($property.Name, $jsonKind)
                $property.Name
            }
        )
    }
    finally {
        $adapterValidationDocument.Dispose()
    }
    $adapterValidation = $adapterValidationOutput | ConvertFrom-Json
    $adapterValidationShapeNames = [string[]] @(
        $adapterValidation.PSObject.Properties.Name
    )
    [Array]::Sort(
        $adapterValidationShapeNames,
        [System.StringComparer]::Ordinal
    )
    $adapterValidationShapeLines = [string[]] @(
        foreach ($propertyName in $adapterValidationShapeNames) {
            '{0}:{1}' -f $propertyName, $adapterValidationJsonKinds[$propertyName]
        }
    )
    $adapterValidationShape = [string]::Join(
        "`n",
        $adapterValidationShapeLines
    )
    $adapterValidationShapeSha256 = [System.Convert]::ToHexString(
        [System.Security.Cryptography.SHA256]::HashData(
            [System.Text.UTF8Encoding]::new($false).GetBytes($adapterValidationShape)
        )
    )
    $adapterDiagnosticEvents = @(
        Get-Content -LiteralPath $adapterDiagnosticPath |
            ForEach-Object { ($_ | ConvertFrom-Json).event }
    )
    $syntheticTrayLogCount = @(
        $adapterDiagnosticEvents |
            Where-Object { $_ -in @('FloatingWindowMinimizedToTray', 'FloatingWindowRestoredFromTray') }
    ).Count
    $validationResourcesReleasedIndex = [Array]::IndexOf(
        [object[]] $adapterDiagnosticEvents,
        'ValidationResourcesReleased'
    )
    $validationCompletedIndex = [Array]::IndexOf(
        [object[]] $adapterDiagnosticEvents,
        'ValidationCompleted'
    )
    Assert-Equal 0 $syntheticTrayLogCount 'validate-only 托盘演练不伪造用户操作日志'
    Assert-Equal $true (
        $validationResourcesReleasedIndex -ge 0 -and
        $validationCompletedIndex -gt $validationResourcesReleasedIndex
    ) 'validate-only 在报告完成前释放真实 WPF 场景资源'
    $blockedTemporaryPath = $roundTripPath + '.tmp'
    New-Item -ItemType Directory -Path $blockedTemporaryPath | Out-Null
    try {
        $failedValidationResult = Invoke-NativeCommandResult -FilePath $pwsh -ArgumentList @(
            '-NoLogo',
            '-NoProfile',
            '-STA',
            '-File',
            $adapter,
            '-ValidateOnly',
            '-ExercisePersistenceFailures',
            '-SettingsFile',
            $roundTripPath,
            '-DiagnosticLogPath',
            $failedValidationDiagnosticPath
        )
    }
    finally {
        Remove-Item -LiteralPath $blockedTemporaryPath -Force
    }
    Assert-Equal 0 $failedValidationResult.ExitCode 'validate-only 受控持久化失败演练退出码'
    $persistenceFailureValidation = $failedValidationResult.Output | ConvertFrom-Json
    Assert-Equal $true ([bool] $persistenceFailureValidation.Valid) 'validate-only 受控持久化失败演练报告有效'
    foreach ($fieldName in @(
        'CountdownSaveFailurePreservesState',
        'PreferenceSaveFailurePreservesState',
        'CountdownSaveFailureRetainsDraft',
        'PreferenceSaveFailureRetainsDraft',
        'TagSaveFailurePreservesState',
        'TagSaveFailureRetainsDraft',
        'TagSaveFailureRetainsFilter',
        'PersistenceFailureTempPathRemainsDirectory'
    )) {
        Assert-Equal $true ([bool] $persistenceFailureValidation.$fieldName) "受控持久化失败演练通过：$fieldName"
    }
    $failedValidationEvents = @(
        Get-Content -LiteralPath $failedValidationDiagnosticPath |
            ForEach-Object { ($_ | ConvertFrom-Json).event }
    )
    $failedResourcesReleasedIndex = [Array]::IndexOf(
        [object[]] $failedValidationEvents,
        'ValidationResourcesReleased'
    )
    $fatalValidationIndex = [Array]::IndexOf(
        [object[]] $failedValidationEvents,
        'FatalUnhandledException'
    )
    $failedValidationCompletedIndex = [Array]::IndexOf(
        [object[]] $failedValidationEvents,
        'ValidationCompleted'
    )
    Assert-Equal -1 $fatalValidationIndex 'validate-only 受控持久化失败演练不触发致命异常'
    Assert-Equal $true (
        $failedResourcesReleasedIndex -ge 0 -and
        $failedValidationCompletedIndex -gt $failedResourcesReleasedIndex
    ) 'validate-only 受控持久化失败演练在报告完成前释放真实 WPF 资源'
    Assert-Equal $canonicalHash (Get-FileHash -LiteralPath $roundTripPath -Algorithm SHA256).Hash '主题点击验收循环回 Auto 且不改变等价设置'
    Assert-Equal $true ([bool] $adapterValidation.Valid) 'adapter validate-only 返回有效 C# 验证报告'
    Assert-Equal $true ([bool] $adapterValidation.DesktopWorkspaceInterfaceBehavior) '桌面工作区 interface 统一持久化、状态变更与筛选行为'
    Assert-Equal $true ([bool] $adapterValidation.ReminderEditingInterfaceBehavior) '提醒编辑 interface 统一模式、时间、间隔与既有提醒保留行为'
    Assert-Equal $true ([bool] $adapterValidation.CalendarReminderBehavior) '日历提醒覆盖类型化规则、北京时间 occurrence、错过合并与 Ical.Net 失败闭锁'
    Assert-Equal $true ([bool] $adapterValidation.ReminderRulesEditorSharedEntryWorks) '主窗口与悬浮窗复用共享提醒规则编辑器入口并隐藏旧复杂控件'
    Assert-Equal $true ([bool] $adapterValidation.FloatingReminderRulesEntryStaysCompact) '悬浮窗提醒规则入口保持 520 x 390 紧凑表面'
    Assert-Equal $true ([bool] $adapterValidation.StartupAcceptanceProtocolInterfaceBehavior) '启动验收 protocol interface 覆盖请求、回执、状态推进与拒绝路径'
    Assert-Equal $true ([bool] $adapterValidation.HolidayCalendarInterfaceBehavior) '工作日历 interface 覆盖缓存初始化、派生结果与失败状态'
    Assert-Equal $true ([bool] $adapterValidation.ValidationReportContractBehavior) 'validate-only 报告可序列化且行为字段完整'
    Assert-Equal $true ([bool] $adapterValidation.ValidateOnlySettingsIsolationBehavior) 'validate-only 强制使用显式非根 settings 路径且不改变生产默认路径'
    Assert-Equal $true ([bool] $adapterValidation.UiQualityGateIsolationBehavior) 'UI 质量门禁负向夹具逐项失败且不误伤无关契约'
    Assert-Equal $adapterValidationPropertyNames.Count @($adapterValidationPropertyNames | Sort-Object -Unique).Count 'validate-only JSON 顶层键唯一'
    Assert-Equal 482 $adapterValidationShapeNames.Count 'validate-only JSON 顶层字段数量保持兼容'
    Assert-Equal 'FloatingTitleTextRequiredWidth:Number' @($adapterValidationShapeLines | Where-Object { $_ -like 'FloatingTitleTextRequiredWidth:*' })[0] 'validate-only JSON 数值形状不受整数或小数布局度量影响'
    Assert-Equal 'CFF850180996B27BC8EFAB2FE99B3A007F1BA59C1EFE67D9864D79A69B0B993C' $adapterValidationShapeSha256 'validate-only JSON 顶层 name:type 形状保持兼容'
    Assert-Equal $true ([bool] $adapterValidation.NoteStatusDisclosureVisuals) 'validate-only 验证便签状态披露视觉'
    Assert-Equal $true ([bool] $adapterValidation.NoteDateTimelineVisuals) 'validate-only 验证便签日期时间轴视觉'
    Assert-Equal $true ([bool] $adapterValidation.NoteDisclosureAccessibility) 'validate-only 验证便签披露控件无障碍契约'
    Assert-Equal $true ([bool] $adapterValidation.NoteDisclosureThemeTokens) 'validate-only 验证便签披露日夜主题 token'
    Assert-Equal 0 @($adapterValidationPropertyNames | Where-Object { $_ -in @('DeepModuleCount', 'DeepModuleInterfaces') }).Count 'validate-only 不输出类型清单或固定 deep module 计数'
    Assert-Equal $true ([bool] $adapterValidation.DesktopWorkspaceModuleDeep) '兼容字段由桌面工作区真实 interface 行为计算'
    Assert-Equal $true ([bool] $adapterValidation.StartupAcceptanceProtocolModuleDeep) '兼容字段由启动验收真实 interface 行为计算'
    Assert-Equal $true ([bool] $adapterValidation.WorkdayCalendarModuleDeep) '兼容字段由工作日历真实 interface 行为计算'
    Assert-Equal $true ([bool] $adapterValidation.ValidationReportModuleDeep) '兼容字段由验证报告真实 JSON 契约计算'
    Assert-Equal $true ([bool] $adapterValidation.ThemeControlInventoryCovered) 'adapter 验证主题控件资产清单'
    Assert-Equal $true ([bool] $adapterValidation.ThemePreferenceButtonCyclesViaClick) '主题按钮通过真实 WPF 点击按自动、日间、夜间循环'
    Assert-Equal $true ([bool] $adapterValidation.ThemePreferenceButtonPersistsEachMode) '主题按钮每次选择都写入设置'
    Assert-Equal $true ([bool] $adapterValidation.ThemePreferenceButtonIsAccessible) '主题按钮每个状态都有 ToolTip 和可访问名称'
    Assert-Equal $true ([bool] $adapterValidation.ThemePreferenceButtonUsesProjectTheme) '主题按钮保持紧凑并使用项目主题模板'
    Assert-Equal $true ([bool] $adapterValidation.ThemePreferenceWindowsStaySynchronized) '主题按钮切换后主窗口与悬浮窗保持同一生效主题'
    Assert-Equal $true ([bool] $adapterValidation.ThemedControlTemplatesApplied) 'adapter 验证真实 WPF 主题模板'
    Assert-Equal $true ([bool] $adapterValidation.LiveDatePickerPopupIsThemed) 'adapter 验证真实 DatePicker Popup 主题'
    Assert-Equal $true ([bool] $adapterValidation.MainNoteEditorFieldsDoNotOverlap) '主窗口重复提醒字段、首次提醒与错误区互不重叠'
    Assert-Equal $true ([bool] $adapterValidation.MainNoteEditorLabelsVisible) '主窗口便签编辑器保留可见字段标签'
    Assert-Equal $true ([bool] $adapterValidation.MainMinimumSizeMatchesDefault) '主窗口默认尺寸与最小可用尺寸一致'
    Assert-Equal $mainReferenceBaselineRequired ([bool] $adapterValidation.MainReferenceBaselineRequested) '主窗口参考基线只在源码树存在双 manifest 时请求'
    Assert-Equal $mainReferenceBaselineRequired ([bool] $adapterValidation.MainReferenceBaselineManifestAvailable) '主窗口参考 manifest 可用状态匹配源码树或发布包模式'
    Assert-Equal $true ([bool] $adapterValidation.MainReferenceBaselineGateIsolationBehavior) '主窗口十六个 manifest 门禁负向夹具与视觉基线 module interface 均通过'
    Assert-Equal 'alpha-metrics-mismatch,content-roi-mismatch,duplicate-capture-id,duplicate-path,duplicate-scenario,duplicate-state-fingerprint,executable-hash-mismatch,malformed-manifest,missing-png,orphan-png,pixel-metrics-mismatch,png-hash-mismatch,reused-frame,source-hash-mismatch,state-fingerprint-mismatch,wrong-dimensions' ((@($adapterValidation.MainReferenceBaselineNegativeFixtures) | Sort-Object) -join ',') '主窗口正式视觉负向夹具集合完整'
    Assert-Equal $true ([bool] $adapterValidation.MainStressCountdownTitleGateIsolationBehavior) '主窗口 Stress 标题缺失和截断或重叠负夹具逐项失败闭锁'
    if ($mainReferenceBaselineRequired) {
    Assert-Equal $true ([bool] $adapterValidation.MainReferenceBaselineValidated) '主窗口十二场景真实 WPF 帧通过正式 manifest'
    Assert-Equal 'assets/ui-references/main-window/1165x1350/2026-08-06-v6/manifest.json' ([string] $adapterValidation.MainReferenceBaselineManifestPath) '主窗口 manifest 报告路径保持仓库相对格式'
    Assert-Equal 64 ([string] $adapterValidation.MainReferenceBaselineSourceAggregateSha256).Length '主窗口视觉源码聚合使用 SHA-256'
    Assert-Equal 64 ([string] $adapterValidation.MainReferenceBaselineExecutableSha256).Length '主窗口正式参考帧绑定最终 EXE SHA-256'
    Assert-Equal 16 @($adapterValidation.MainReferenceBaselineGates.PSObject.Properties | Where-Object { [bool] $_.Value }).Count '主窗口正式 manifest 十六项门禁全部通过'
    $mainReferenceCaptures = @($adapterValidation.MainReferenceBaselineCaptures)
    $expectedMainReferenceCaptureIds = @(
        'main-day-countdowns-empty',
        'main-day-countdowns-representative',
        'main-day-countdowns-stress',
        'main-day-notes-empty',
        'main-day-notes-representative',
        'main-day-notes-stress',
        'main-night-countdowns-empty',
        'main-night-countdowns-representative',
        'main-night-countdowns-stress',
        'main-night-notes-empty',
        'main-night-notes-representative',
        'main-night-notes-stress'
    )
    Assert-Equal 12 $mainReferenceCaptures.Count '主窗口正式 manifest 严格绑定十二帧'
    Assert-Equal ($expectedMainReferenceCaptureIds -join ',') ((@($mainReferenceCaptures.CaptureId) | Sort-Object) -join ',') '主窗口十二场景 capture ID 固定'
    Assert-Equal 12 @($mainReferenceCaptures | ForEach-Object { "$($_.Theme)|$($_.Workspace)|$($_.State)" } | Sort-Object -Unique).Count '主窗口 Day/Night × Countdowns/Notes × 三状态组合唯一'
    Assert-Equal 12 @($mainReferenceCaptures.BaselineSha256 | Sort-Object -Unique).Count '主窗口十二帧 PNG hash 全部唯一'
    Assert-Equal 12 @($mainReferenceCaptures.StateFingerprint.Sha256 | Sort-Object -Unique).Count '主窗口十二帧状态指纹全部唯一'
    Assert-Equal 0 @($mainReferenceCaptures | Where-Object { $_.Path -notlike 'assets/ui-references/main-window/1165x1350/2026-08-06-v6/*.png' -or $_.BaselineSha256 -ne $_.ActualSha256 }).Count '主窗口十二帧路径可移植且基线与实拍 SHA 一致'
    Assert-Equal 0 @($mainReferenceCaptures | Where-Object { $_.Alpha.Minimum -ne 255 -or $_.Alpha.Maximum -ne 255 -or $_.Alpha.NonOpaquePixelCount -ne 0 -or $_.Alpha.OpaquePixelCount -ne 1572750 }).Count '主窗口十二帧 1165×1350 画布全部不透明'
    Assert-Equal 0 @($mainReferenceCaptures | Where-Object { $_.ContentRoi.Algorithm -ne 'fixed full-canvas ROI for opaque 1165x1350 main-window surface' -or $_.ContentRoi.X -ne 0 -or $_.ContentRoi.Y -ne 0 -or $_.ContentRoi.Width -ne 1165 -or $_.ContentRoi.Height -ne 1350 -or $_.ContentRoi.PixelCount -ne 1572750 }).Count '主窗口 opaque surface 使用固定全画布 ROI 而非透明 alpha ROI'
    Assert-Equal 0 @($mainReferenceCaptures | Where-Object { [int] $_.Comparison.MaxAbsoluteChannelDelta -gt 8 -or [double] $_.Comparison.MeanAbsoluteError -gt 0.02 -or [double] $_.Comparison.RootMeanSquareError -gt 0.25 -or [int] $_.Comparison.ChangedPixelCount -gt 500 }).Count '主窗口十二帧像素指标未超过 manifest 固定阈值'
    Assert-Equal 4 @($mainReferenceCaptures | Where-Object { $_.StateFingerprint.CountdownCount -eq 0 -and $_.StateFingerprint.NoteCount -eq 0 }).Count '主窗口 Empty 四态确实绑定零数据设置'
    Assert-Equal 4 @($mainReferenceCaptures | Where-Object { $_.State -eq 'Representative' -and $_.StateFingerprint.CountdownCount -eq 3 -and $_.StateFingerprint.NoteCount -eq 3 -and $_.StateFingerprint.ActiveNoteCount -eq 3 }).Count '主窗口 Representative 四态绑定少量倒计时、未完成便签与提醒'
    Assert-Equal 4 @($mainReferenceCaptures | Where-Object { $_.State -eq 'Stress' -and $_.StateFingerprint.CountdownCount -eq 9 -and $_.StateFingerprint.NoteCount -eq 8 -and $_.StateFingerprint.ActiveNoteCount -eq 4 -and $_.StateFingerprint.CompletedNoteCount -eq 4 }).Count '主窗口 Stress 四态绑定极长标题、长便签、提醒、完成状态与多日期组'
    Assert-Equal $true ([bool] $adapterValidation.MainStressCountdownTitleVisuals) '主窗口 Stress 日夜 30 字符标题通过真实 WPF 可见性与操作区隔离门禁'
    $stressCountdownTitleMeasurements = @($adapterValidation.MainStressCountdownTitleMeasurements)
    Assert-Equal 2 $stressCountdownTitleMeasurements.Count '主窗口 Stress 标题精确测量 Day/Night 两态'
    Assert-Equal 'main-day-countdowns-stress,main-night-countdowns-stress' ((@($stressCountdownTitleMeasurements.CaptureId) | Sort-Object) -join ',') '主窗口 Stress 标题测量 capture ID 固定'
    Assert-Equal 0 @($stressCountdownTitleMeasurements | Where-Object {
        $_.RenderedText -ne '极长倒计时标题用于验证主窗口列表不会挤压操作区或破坏暖纸层级' -or
        -not $_.ExactTitleRendered -or
        $_.TextWrapping -ne 'NoWrap' -or
        $_.TextTrimming -ne 'None' -or
        -not $_.TextFits -or
        -not $_.FullyVisibleInViewport -or
        -not $_.SeparatedFromActions
    }).Count '主窗口 Stress 标题日夜两态均完整单行显示且不与操作按钮相交'
    Assert-Equal 0 @($adapterValidation.MainReferenceBaselineDiagnostics).Count '主窗口正式 manifest 验证没有残留诊断'
    }
    else {
        Assert-Equal $false ([bool] $adapterValidation.MainReferenceBaselineValidated) '发布包不伪造未携带主窗口参考资产时的视觉验收'
        Assert-Equal 0 @($adapterValidation.MainReferenceBaselineCaptures).Count '发布包不伪造主窗口参考帧度量'
        Assert-Equal $false ([bool] $adapterValidation.MainStressCountdownTitleVisuals) '发布包不伪造未执行的 Stress 标题真实 WPF 验收'
        Assert-Equal 0 @($adapterValidation.MainStressCountdownTitleMeasurements).Count '发布包不伪造 Stress 标题测量'
    }
    Assert-Equal $true ([bool] $adapterValidation.PopupKeyboardLifecycleWorks) '筛选、标签管理与标签选择弹层支持 Esc 关闭并返焦'
    Assert-Equal $true ([bool] $adapterValidation.PopupOutsideFocusPreserved) '鼠标或外部焦点关闭弹层时不会抢回触发器焦点'
    Assert-Equal $true ([bool] $adapterValidation.NoteEmptyStatesDistinguished) '首次空态与筛选无结果状态具有不同反馈'
    Assert-Equal $true ([bool] $adapterValidation.NoteNoResultsResetWorks) '筛选无结果状态可就地重置完整筛选条件'
    Assert-Equal $true ([bool] $adapterValidation.ReminderCenterInitialFocusAndAccessibleNameWork) '提醒中心打开后建立初始焦点与可访问名称'
    Assert-Equal $true ([bool] $adapterValidation.OfficialCharacterBackgroundLoaded) 'adapter 验证角色图片资产已加载'
    Assert-Equal 3 ([int] $adapterValidation.MainCharacterPanelDockCount) '主窗口角色场景栏保留三个玩偶底座'
    Assert-Equal $true ([bool] $adapterValidation.MainCharacterPanelDocksIntegrateWithPanel) '主窗口三个玩偶底座与工作区面板形成一体'
    Assert-Equal $true ([bool] $adapterValidation.MainCharacterPanelDecorationsAreNonInteractive) '主窗口角色场景栏不拦截工作区操作'
    Assert-Equal $true ([bool] $adapterValidation.SavePreferencesButtonTextFits) '设置保存按钮文字完整显示'
    Assert-Equal $true ([bool] $adapterValidation.SavePreferencesButtonIsFullyVisible) '设置保存按钮完整位于主窗口内'
    Assert-Equal $true ([bool] $adapterValidation.SavePreferencesButtonIsFullyVisibleAtMinimumWidth) '最小主窗口宽度下设置保存按钮仍完整可见'
    Assert-Equal $true ([bool] $adapterValidation.FloatingPlushUsesReferencePoses) '悬浮窗使用原始三位角色的姿势素材'
    Assert-Equal $true ([bool] $adapterValidation.FloatingPlushAnchorsIntegrateWithPanel) '三个玩偶锚点在面板上方形成不拦截交互的场景层'
    Assert-Equal $true ([bool] $adapterValidation.FloatingReferenceAspectMatches) '悬浮窗参考场景保持 520×390 的 4:3 比例'
    Assert-Equal $true ([bool] $adapterValidation.FloatingReferencePanelGeometryMatches) '悬浮窗外框、内容面板与标题栏匹配参考图构图'
    Assert-Equal $true ([bool] $adapterValidation.FloatingReferenceMascotGeometryMatches) '悬浮窗三角色可见像素匹配参考图锚点和裁切'
    Assert-Equal $floatingReferenceBaselineRequired ([bool] $adapterValidation.FloatingReferenceBaselineRequested) '悬浮窗参考基线只在源码树存在 manifest 时请求'
    Assert-Equal $floatingReferenceBaselineRequired ([bool] $adapterValidation.FloatingReferenceBaselineManifestAvailable) '悬浮窗参考 manifest 可用状态匹配源码树'
    Assert-Equal $true ([bool] $adapterValidation.FloatingReferenceBaselineGateIsolationBehavior) '悬浮窗参考 manifest 十二个负向夹具与视觉基线 module interface 均通过'
    Assert-Equal 'alpha-metrics-mismatch,content-roi-mismatch,duplicate-path,duplicate-scenario-theme,executable-hash-mismatch,malformed-manifest,missing-png,orphan-png,pixel-metrics-mismatch,png-hash-mismatch,source-hash-mismatch,wrong-dimensions' ((@($adapterValidation.FloatingReferenceBaselineNegativeFixtures) | Sort-Object) -join ',') '悬浮窗参考 manifest 负向夹具集合完整'
    Assert-Equal $true ([bool] $adapterValidation.FloatingReferenceFreshnessGateIsolationBehavior) '长便签帧新鲜度与排版八个负向夹具逐项失败闭锁'
    Assert-Equal 'duplicate-capture-id,duplicate-state-fingerprint,long-note-layout-unreachable,long-note-typography-mismatch,reused-frame,state-fingerprint-mismatch,swapped-top-bottom,top-bottom-pixel-difference-too-small' ((@($adapterValidation.FloatingReferenceFreshnessNegativeFixtures) | Sort-Object) -join ',') '长便签帧新鲜度与排版负向夹具集合完整'
    foreach ($fieldName in @(
        'FloatingLongNotesBodyHasNoManualLineBreaks',
        'FloatingLongNotesNaturalWraps',
        'FloatingLongNotesUsesOnlyOuterScroller',
        'FloatingLongNotesOuterScrollable',
        'FloatingLongNotesTopBottomReachable',
        'FloatingLongNotesSafeAreaMatches',
        'FloatingLongNotesTypographyMatches',
        'FloatingLongNotesLayoutValid'
    )) {
        Assert-Equal $true ([bool] $adapterValidation.$fieldName) "长便签真实 WPF 门禁通过：$fieldName"
    }
    $expectedLongNoteCaptureIds = 'long-notes-day-bottom,long-notes-day-top,long-notes-night-bottom,long-notes-night-top'
    $longNoteMeasurements = @($adapterValidation.FloatingLongNotesMeasurements)
    Assert-Equal 4 $longNoteMeasurements.Count '长便签真实 WPF 精确测量日夜 top/bottom 四态'
    Assert-Equal $expectedLongNoteCaptureIds ((@($longNoteMeasurements.CaptureId) | Sort-Object) -join ',') '长便签测量 capture ID 固定'
    Assert-Equal 0 @($longNoteMeasurements | Where-Object { -not $_.BodyHasNoManualLineBreaks -or -not $_.NaturalWraps -or -not $_.UsesOnlyOuterScroller -or -not $_.OuterScrollable -or -not $_.PrimaryAnchorReachable -or -not $_.SafeAreaMatches }).Count '长便签每态均证明无手工换行、自然折行、仅外层滚动、锚点可达和安全区'
    Assert-Equal 0 @($longNoteMeasurements | Where-Object { [double] $_.BodyFontSize -ne 13 -or [double] $_.BodyLineHeight -ne 18 -or [double] $_.ReminderFontSize -ne 12 -or [double] $_.ReminderLineHeight -ne 17 -or -not $_.TypographyMatches }).Count '长便签日夜 top/bottom 四态均满足正文 13/18 与提醒 12/17 显式排版'
    Assert-Equal 0 @($longNoteMeasurements | Where-Object { $_.State -eq 'Top' -and [math]::Abs([double] $_.VerticalOffset) -gt 1 }).Count '长便签 top 四舍五入 offset 约等于零'
    Assert-Equal 0 @($longNoteMeasurements | Where-Object { $_.State -eq 'Bottom' -and [math]::Abs([double] $_.VerticalOffset - [double] $_.ScrollableHeight) -gt 1 }).Count '长便签 bottom offset 约等于 ScrollableHeight'
    Assert-Equal 0 @($longNoteMeasurements | Where-Object { ([string] $_.NoteTextSha256).Length -ne 64 -or [string]::IsNullOrWhiteSpace([string] $_.FirstVisibleAnchor) -or [string]::IsNullOrWhiteSpace([string] $_.LastVisibleAnchor) }).Count '长便签每态绑定正文 SHA-256 与可见首尾锚点'
    $longNoteCaptures = @($adapterValidation.FloatingLongNotesCaptures)
    Assert-Equal 4 $longNoteCaptures.Count '长便签真实 WPF 写出四态帧'
    Assert-Equal $expectedLongNoteCaptureIds ((@($longNoteCaptures.CaptureId) | Sort-Object) -join ',') '长便签四态 capture ID 固定'
    Assert-Equal 4 @($longNoteCaptures.PngSha256 | Sort-Object -Unique).Count '长便签四态 PNG hash 全部唯一'
    Assert-Equal 4 @($longNoteCaptures.StateFingerprint.Sha256 | Sort-Object -Unique).Count '长便签四态 fingerprint 全部唯一'
    if ($floatingReferenceBaselineRequired) {
        Assert-Equal $true ([bool] $adapterValidation.FloatingReferenceBaselineValidated) '当前真实 WPF 日夜截图通过版本化 manifest'
        Assert-Equal 'assets/ui-references/floating-window/520x390/2026-08-06-v6/manifest.json' ([string] $adapterValidation.FloatingReferenceBaselineManifestPath) 'manifest 报告路径保持仓库相对可移植格式'
        Assert-Equal 64 ([string] $adapterValidation.FloatingReferenceBaselineSourceAggregateSha256).Length '悬浮窗视觉源码聚合使用 SHA-256'
        Assert-Equal 64 ([string] $adapterValidation.FloatingReferenceBaselineExecutableSha256).Length '悬浮窗参考帧绑定生成 EXE SHA-256'
        Assert-Equal 12 @($adapterValidation.FloatingReferenceBaselineGates.PSObject.Properties | Where-Object { [bool] $_.Value }).Count '悬浮窗参考 manifest 十二个真实门禁全部通过'
        Assert-Equal 6 @($adapterValidation.FloatingReferenceBaselineCaptures).Count '悬浮窗参考 manifest 严格绑定六帧'
        Assert-Equal 'floating-countdowns-day,floating-countdowns-night,long-notes-day-bottom,long-notes-day-top,long-notes-night-bottom,long-notes-night-top' ((@($adapterValidation.FloatingReferenceBaselineCaptures.CaptureId) | Sort-Object) -join ',') '悬浮窗参考 manifest capture ID 固定'
        Assert-Equal 8 @($adapterValidation.FloatingReferenceFreshnessGates.PSObject.Properties | Where-Object { [bool] $_.Value }).Count '长便签八个真实新鲜度与排版门禁全部通过'
        Assert-Equal 6 @($adapterValidation.FloatingReferenceBaselineCaptures.BaselineSha256 | Sort-Object -Unique).Count '六帧 PNG hash 全部唯一'
        Assert-Equal '6BC7D19CD445186E48913DB738907C785497A1B6A19D5A3BD5FEDFBC645D6FE9' ([string] (@($adapterValidation.FloatingReferenceBaselineCaptures | Where-Object CaptureId -eq 'floating-countdowns-day')[0].BaselineSha256)) '当前日间倒计时 PNG hash 与正式基线一致'
        Assert-Equal '2A3E246125832271AB26AAC6D90FE5B91A58D97049C20197282F2CD1A892E960' ([string] (@($adapterValidation.FloatingReferenceBaselineCaptures | Where-Object CaptureId -eq 'floating-countdowns-night')[0].BaselineSha256)) '当前夜间倒计时 PNG hash 与正式基线一致'
        Assert-Equal 0 @($adapterValidation.FloatingReferenceBaselineCaptures | Where-Object { [int] $_.Comparison.MaxAbsoluteChannelDelta -gt 8 -or [double] $_.Comparison.MeanAbsoluteError -gt 0.02 -or [double] $_.Comparison.RootMeanSquareError -gt 0.25 -or [int] $_.Comparison.ChangedPixelCount -gt 500 }).Count '当前真实帧像素指标未超过 manifest 固定阈值'
        Assert-Equal 0 @($adapterValidation.FloatingReferenceBaselineDiagnostics).Count '悬浮窗参考 manifest 验证没有残留诊断'
    }
    else {
        Assert-Equal $false ([bool] $adapterValidation.FloatingReferenceBaselineValidated) '发布包不伪造未携带参考资产时的视觉验收'
        Assert-Equal 0 @($adapterValidation.FloatingReferenceBaselineCaptures).Count '发布包不伪造参考帧度量'
    }
    Assert-Equal $true ([bool] $adapterValidation.FloatingNightPlushSourceAlphaBoundsMatch) '夜间悬浮窗三角色源 alpha bounds 保持冻结几何'
    Assert-Equal '7,5,137,95;371,15,137,77;407,207,113,109' ((@($adapterValidation.FloatingNightPlushSourceAlphaBounds) -join ';')) '夜间悬浮窗三角色源 alpha bounds 未移动'
    Assert-Equal $true ([bool] $adapterValidation.FloatingNightPlushEdgeBrightnessWithinLimit) '夜间悬浮窗角色 2-6px 外缘亮度受控'
    Assert-Equal $true ([bool] $adapterValidation.FloatingNightPlushEdgeSaturationWithinLimit) '夜间悬浮窗角色 2-6px 外缘饱和度受控'
    Assert-Equal $true ([bool] $adapterValidation.FloatingNightPlushEdgeGlowGateIsolationBehavior) '夜间悬浮窗角色外缘亮度与饱和度负向夹具可分别单独变红'
    Assert-Equal 3 @($adapterValidation.FloatingNightPlushEdgeGlowMeasurements).Count '夜间悬浮窗角色外缘分别测量三只角色'
    Assert-Equal $true ([bool] ((@($adapterValidation.FloatingNightPlushEdgeGlowMeasurements) | Measure-Object -Property BrightnessSignal -Maximum).Maximum -le [double] $adapterValidation.FloatingNightPlushEdgeBrightnessLimit)) '夜间悬浮窗角色外缘实测亮度峰值不超过门限'
    Assert-Equal $true ([bool] ((@($adapterValidation.FloatingNightPlushEdgeGlowMeasurements) | Measure-Object -Property Saturation -Maximum).Maximum -le [double] $adapterValidation.FloatingNightPlushEdgeSaturationLimit)) '夜间悬浮窗角色外缘实测饱和度峰值不超过门限'
    Assert-Equal $true ([bool] ([string] $adapterValidation.FloatingNightPlushEdgeRoiAlgorithm).Contains('Chebyshev distance 2-6px', [System.StringComparison]::Ordinal)) '夜间悬浮窗角色外缘报告明确 2-6px ROI 算法'
    Assert-Equal $true ([bool] $adapterValidation.FloatingReferenceContentSafeAreaMatches) '悬浮窗角色 alpha 不遮挡标题、图标、卡片文字或收入'
    Assert-Equal $true ([bool] $adapterValidation.FloatingTitleUsesSemiBoldWeight) '悬浮窗标题独立保持 SemiBold 字重'
    Assert-Equal $true ([bool] $adapterValidation.FloatingNoteInputWraps) '悬浮窗便签输入独立保持自动换行'
    Assert-Equal $true ([bool] $adapterValidation.FloatingNoteInputScrolls) '悬浮窗便签输入独立保持垂直滚动'
    Assert-Equal $true ([bool] $adapterValidation.FloatingQuickAddDayExpandedScrolledCaptureWritten) '悬浮窗快速新增日间展开滚动场景可离屏捕获'
    Assert-Equal $true ([bool] $adapterValidation.FloatingQuickAddNightExpandedScrolledCaptureWritten) '悬浮窗快速新增夜间展开滚动场景可离屏捕获'
    Assert-Equal $true ([bool] $adapterValidation.FloatingInitialWorkspaceStateMatches) '悬浮窗首次显示立即同步倒计时选中态、标题、输入区和内容'
    Assert-Equal $true ([bool] $adapterValidation.FloatingWorkspaceSwitchingStaysSynchronized) '悬浮窗往返切换后始终恰有一个工作区并同步表面'
    Assert-Equal $true ([bool] $adapterValidation.FloatingCountdownSelectionSurvivesDayNightThemes) '悬浮窗倒计时首次选中态跨日夜边界保持'
    Assert-Equal 0 ([int] $adapterValidation.FloatingReferenceContentOverlapCount) '悬浮窗参考场景没有角色与内容墨迹重叠'
    Assert-Equal $true ([bool] $adapterValidation.FloatingReferenceCaptureWritten) '悬浮窗参考场景可由 RenderTargetBitmap 写出非空 PNG'
    Assert-Equal $true ([bool] $adapterValidation.FloatingWorkdayCountdownUsesPrimaryVisual) '距离下班在首行使用唯一主项的 Automation 语义、暖纸底、珊瑚轨、字体和强调色'
    Assert-Equal $true ([bool] $adapterValidation.FloatingReferenceScenarioMatches) '悬浮窗参考场景将距离下班作为首行主项，并保持两张卡片、收入和主项单行剩余时间'
    Assert-Equal $true ([bool] $adapterValidation.FloatingPrimaryDeadlineTextFullyVisible) '悬浮窗主项的最宽日期在有无滚动条时均完整可见'
    Assert-Equal $true ([bool] $adapterValidation.FloatingReferenceViewportStatesMatch) '悬浮窗固定视口覆盖零、一、多卡片及便签提醒滚动状态'
    Assert-Equal $true ([bool] $adapterValidation.FloatingViewportScrollAndFocusRestored) '悬浮窗刷新后恢复当前滚动位置与便签焦点'
    Assert-Equal $true ([bool] $adapterValidation.FloatingNoteFailureAppearsInSourceCard) '悬浮便签完成失败时在来源卡片显示可访问错误'
    Assert-Equal $true ([bool] $adapterValidation.FloatingResolvedNoteCardErrorDoesNotReturn) '便签完成并恢复后不会重新显示旧卡片错误'
    Assert-Equal $true ([bool] $adapterValidation.FloatingSecondaryRemainingTextFullyVisible) '超长次级倒计时在固定视口内单行完整显示'
    Assert-Equal $true ([bool] $adapterValidation.FloatingSecondaryRemainingUnitSpacingMatches) '次级倒计时直接保留 FormatRemaining 的单位分隔空格'
    Assert-Equal $true ([bool] $adapterValidation.FloatingSecondaryRemainingLayoutMatches) '次级倒计时保持 194px 右对齐、单行、完整显示及表格数字'
    Assert-Equal $true ([bool] $adapterValidation.FloatingSecondaryRemainingGateIsolationBehavior) '次级倒计时去空格与破坏固定列负向夹具可分别单独变红'
    Assert-Equal 5 @($adapterValidation.FloatingSecondaryRemainingSamples).Count '次级倒计时门禁覆盖四种精度及长天数'
    Assert-Equal 'Day;Hour;Minute;Second;SecondLongDays' ((@($adapterValidation.FloatingSecondaryRemainingSamples) | ForEach-Object { $_.Precision }) -join ';') '次级倒计时门禁覆盖精度顺序稳定'
    Assert-Equal '4 天;4 天 13 小时;4 天 13 小时 18 分钟;4 天 13 小时 18 分钟 59 秒;2000000 天 00 小时 00 分钟 00 秒' ((@($adapterValidation.FloatingSecondaryRemainingSamples) | ForEach-Object { $_.RenderedText }) -join ';') '次级倒计时 WPF 文本逐项保留单位分隔空格'
    Assert-Equal $true ([bool] (@($adapterValidation.FloatingSecondaryRemainingSamples) | ForEach-Object { [Math]::Abs([double] $_.ColumnWidth - 194) -le 0.01 } | Where-Object { -not $_ } | Measure-Object).Count -eq 0) '次级倒计时所有样本声明 194px 固定列'
    Assert-Equal $true ([bool] (@($adapterValidation.FloatingSecondaryRemainingSamples) | ForEach-Object { [Math]::Abs([double] $_.ActualWidth - 194) -le 0.5 } | Where-Object { -not $_ } | Measure-Object).Count -eq 0) '次级倒计时所有样本实际布局宽度保持在 194px 舍入容差内'
    Assert-Equal '13;13;13;13' ((@($adapterValidation.FloatingSecondaryRemainingSamples)[0..3] | ForEach-Object { $_.FontSize }) -join ';') '次级倒计时四种常规精度保持 13px'
    Assert-Equal $true ([bool] ([double] @($adapterValidation.FloatingSecondaryRemainingSamples)[4].FontSize -ge 10 -and [double] @($adapterValidation.FloatingSecondaryRemainingSamples)[4].FontSize -lt 13)) '仅超长次级倒计时自适应缩小字号'
    foreach ($fieldName in @(
        'FloatingScrollBarLocalStyleScoped',
        'FloatingScrollBarHitTargetUsable',
        'FloatingScrollBarVisualWeightProgression',
        'FloatingScrollBarUsesWarmThemeTokens',
        'FloatingScrollBarContentRemainsSafe',
        'FloatingScrollBarInputPathsWork',
        'FloatingScrollBarGateIsolationBehavior',
        'FloatingScrollBarDayNightStateCapturesReady'
    )) {
        Assert-Equal $true ([bool] $adapterValidation.$fieldName) "悬浮窗滚动条门禁通过：$fieldName"
    }
    $floatingScrollBarSamples = @($adapterValidation.FloatingScrollBarStateMeasurements)
    Assert-Equal 6 $floatingScrollBarSamples.Count '悬浮窗滚动条覆盖日夜 normal/hover/drag 六态'
    Assert-Equal 'Day;Day;Day;Night;Night;Night' (($floatingScrollBarSamples | ForEach-Object { $_.Theme }) -join ';') '悬浮窗滚动条日夜状态顺序稳定'
    Assert-Equal 'normal;hover;drag;normal;hover;drag' (($floatingScrollBarSamples | ForEach-Object { $_.State }) -join ';') '悬浮窗滚动条交互状态顺序稳定'
    Assert-Equal $true ([bool] (@($floatingScrollBarSamples | Where-Object { [Math]::Abs([double] $_.HitTargetWidth - 10) -gt 0.01 }).Count -eq 0)) '悬浮窗滚动条六态保留 10px 命中区'
    Assert-Equal $true ([bool] (@($floatingScrollBarSamples | Where-Object { [Math]::Abs([double] $_.ThumbVisualWidth - 4) -gt 0.01 }).Count -eq 0)) '悬浮窗滚动条六态只显示 4px thumb'
    Assert-Equal '0.52;0.78;0.96' (($floatingScrollBarSamples[0..2] | ForEach-Object { $_.ThumbOpacity }) -join ';') '悬浮窗滚动条 hover/drag 只增强可见度'
    Assert-Equal $true ([bool] (@($floatingScrollBarSamples | Where-Object { [double] $_.ThumbVisualRight -gt 396.01 -or [double] $_.MaximumVisibleCardRight -ge [double] $_.ThumbVisualLeft -or -not [bool] $_.DoesNotObscureContent }).Count -eq 0)) '悬浮窗滚动条六态不遮挡卡片且不越过 x=396'
    Assert-Equal $true ([bool] ([double] @($adapterValidation.FloatingScrollBarWheelOffsets)[1] -gt [double] @($adapterValidation.FloatingScrollBarWheelOffsets)[0])) '真实滚轮输入改变悬浮窗 ScrollViewer 偏移'
    Assert-Equal $true ([bool] ([double] @($adapterValidation.FloatingScrollBarDragOffsets)[1] -gt [double] @($adapterValidation.FloatingScrollBarDragOffsets)[0])) '真实 Thumb 拖动输入改变悬浮窗 ScrollViewer 偏移'
    Assert-Equal 'day-normal;day-hover;day-drag;night-normal;night-hover;night-drag' ((@($adapterValidation.FloatingScrollBarCaptureNames)) -join ';') '悬浮窗滚动条日夜六态截图清单完整'
    Assert-Equal $true ([bool] $adapterValidation.FloatingDatePickerKeyboardLifecycleWorks) '悬浮窗两个日期选择器支持日历聚焦、Esc 关闭与按钮返焦'
    Assert-Equal $true ([bool] $adapterValidation.FloatingFieldErrorsAppearAtSource) '悬浮快速便签错误显示在无效字段附近并关联输入控件'
    Assert-Equal $true ([bool] $adapterValidation.FloatingCorrectedFieldErrorsClearLiveMessage) '悬浮快速便签字段修正后清除局部与全局错误'
    Assert-Equal (
        $dayRasterComponentFiles -join ','
    ) (
        [string]::Join(',', @($adapterValidation.FloatingDayRasterComponentAssetNames))
    ) '悬浮窗加载四个日间无文字 raster 组件'
    Assert-Equal $true ([bool] $adapterValidation.FloatingDayRasterShellAndHeaderLoaded) '悬浮窗日间 shell 与无文字标题栏组件已加载'
    Assert-Equal $true ([bool] $adapterValidation.FloatingRasterSurfacesFollowDayNightTheme) '悬浮窗 raster 表面与夜间矢量表面按北京时间边界切换'
    Assert-Equal $true ([bool] $adapterValidation.FloatingDayRasterDecorationsAreNonInteractive) '悬浮窗日间 raster 装饰不拦截鼠标命中'
    Assert-Equal $true ([bool] $adapterValidation.FloatingDayRasterCardStatesMatch) '悬浮窗日间 0/1/2 张倒计时及便签均按真实记录复用无文字卡片底板'
    Assert-Equal $true ([bool] $adapterValidation.FloatingDynamicCardContentRemainsWpf) '悬浮窗卡片文字与便签勾选框仍由真实 WPF 控件渲染'
    Assert-Equal $true ([bool] $adapterValidation.FloatingDynamicHeaderControlsRemainWpf) '悬浮窗标题、页签与命令图标仍由真实 WPF 控件渲染'
    Assert-Equal $true ([bool] $adapterValidation.FloatingHeaderIconAccessibility) '悬浮窗图标按钮保留 ToolTip 与可访问名称'
    Assert-Equal $true ([bool] $adapterValidation.FloatingHeaderIconClickCommandsWork) '悬浮窗打开管理与退出图标保留真实点击命令'
    Assert-Equal $true ([bool] $adapterValidation.FloatingDragSurfaceCoversWindowBackground) '悬浮窗非交互背景均可接收拖动手势'
    Assert-Equal $true ([bool] $adapterValidation.FloatingTrayButtonAccessibility) '悬浮窗缩小按钮提供明确 ToolTip 与可访问名称'
    Assert-Equal $true ([bool] $adapterValidation.FloatingTrayProductionControllerActive) '应用宿主与托盘验收共用生产切换控制器'
    Assert-Equal $true ([bool] $adapterValidation.FloatingTrayMinimizeHidesWindowAndShowsIcon) '悬浮窗缩小按钮隐藏窗口并显示系统托盘图标'
    Assert-Equal $true ([bool] $adapterValidation.FloatingTrayRestoreShowsWindowAndHidesIcon) '系统托盘打开命令恢复悬浮窗并隐藏托盘图标'
    Assert-Equal $true ([bool] $adapterValidation.FloatingTrayExitRequestsApplicationExit) '系统托盘退出命令触发应用退出请求'
    Assert-Equal $true ([bool] $adapterValidation.FloatingDragHandleReceivesPointerGestures) '悬浮窗标题拖动区接收真实 WPF 左键手势'
    Assert-Equal $true ([bool] $adapterValidation.FloatingDragHandleRequestsNativeWindowDrag) '悬浮窗标题拖动区经 WPF 左键路由请求原生窗口拖动'
    Assert-Equal $true ([bool] $adapterValidation.FloatingSyntheticDragDoesNotClaimPhysicalCompletion) '悬浮窗合成拖动验收不会把被 WPF 拒绝的 DragMove 误报为物理拖动成功'
    Assert-Equal $true ([bool] $adapterValidation.FloatingDragAttemptClampsToWorkArea) '悬浮窗拖动尝试结束后立即收紧到工作区内'
    Assert-Equal $true ([bool] $adapterValidation.FloatingInteractiveControlsDoNotRequestWindowDrag) '悬浮窗按钮、输入和滚动控件不请求窗口拖动'
    Assert-Equal $true ([bool] $adapterValidation.FloatingDragHandleExcludesInteractiveControls) '悬浮窗标题拖动区不覆盖按钮或输入控件'
    Assert-Equal $true ([bool] $adapterValidation.FloatingWindowManualPositionPersistsAndRemainsVisibleAfterRefresh) '悬浮窗手动移动后内容刷新会保留位置并限制在工作区内'
    Assert-Equal $true ([bool] $adapterValidation.FloatingTitleTextFits) '悬浮窗今日便签标题在实际父布局槽内完整可见'
    Assert-Equal $true ([bool] $adapterValidation.FloatingTitleTextHasVerticalClearance) '悬浮窗今日便签标题保留完整字形所需的垂直余量'
    Assert-Equal 'Night,Day,Day,Night' ([string]::Join(',', @($adapterValidation.FloatingThemeBoundaryModes))) '悬浮窗按北京时间在日夜边界自动切换主题'
    Assert-Equal '#FFFFFDFC,#FF3A2B1E' ([string]::Join(',', @($adapterValidation.FloatingThemePanelColors))) '悬浮窗日夜面板使用预期的暖白与深琥珀配色'
    Assert-Equal '#FFB9794E,#FF756A62' ([string]::Join(',', @($adapterValidation.FloatingThemePlushGlowColors))) '悬浮窗角色场景日间保持原暖色、夜间使用低饱和暖棕阴影'
    Assert-Equal 'NightAnchors,DayRaster,DayRaster,NightAnchors' ([string]::Join(',', @($adapterValidation.FloatingMascotPresentationModes))) '悬浮窗按北京时间切换日间单一角色层与夜间三锚点'
    Assert-Equal $true ([bool] $adapterValidation.FloatingRasterMascotBoundaryStatesMatch) '悬浮窗日间 raster 角色与夜间旧三角色边界状态完整'
    Assert-Equal $true ([bool] $adapterValidation.FloatingPlushSceneFollowsDayNightTheme) '悬浮窗角色场景在日夜边界同步切换暖色光晕'
    Assert-Equal $true ([bool] $adapterValidation.FloatingThemeTextContrast) '悬浮窗标题与便签错误提示在日夜主题下保持可读对比度'
    Assert-Equal $true ([bool] $adapterValidation.FloatingPrimaryCountdownTextContrast) '悬浮窗主倒计时相对真实合成纸面达到 4.5:1 对比度'
    Assert-Equal $true ([bool] $adapterValidation.FloatingPrimaryCountdownAccentSeparatedFromPlushGlow) '悬浮窗主倒计时强调与玩偶阴影使用独立语义 token'
    Assert-Equal $true ([bool] $adapterValidation.FloatingPrimaryCountdownContrastGateIsolationBehavior) '旧夜间强调色与共享 token 负向夹具分别单项失败'
    $primaryContrastMeasurements = @($adapterValidation.FloatingPrimaryCountdownContrastMeasurements)
    Assert-Equal 2 $primaryContrastMeasurements.Count '悬浮窗主倒计时报告日夜两份真实 WPF 对比度测量'
    Assert-Equal 'Day,Night' ((@($primaryContrastMeasurements.Theme) | Sort-Object) -join ',') '悬浮窗主倒计时测量覆盖日夜主题'
    $nightPrimaryContrast = @($primaryContrastMeasurements | Where-Object Theme -eq 'Night')[0]
    Assert-Equal '#FFE9B36D' ([string] $nightPrimaryContrast.Foreground) '夜间主倒计时使用独立暖金强调'
    Assert-Equal '#FF493525' ([string] $nightPrimaryContrast.CompositeBackground) '夜间主倒计时报告真实合成背景'
    Assert-Equal $true ([math]::Abs([double] $nightPrimaryContrast.ContrastRatio - 6.12) -le 0.01) '夜间主倒计时实际对比度约为 6.12:1'
    Assert-Equal 0 @($primaryContrastMeasurements | Where-Object { [double] $_.FontSize -ne 16 -or -not $_.UsesExpectedSemanticAccent -or -not $_.AccentTrackMatchesText -or -not $_.AccentDiffersFromPlushGlow }).Count '日夜主项轨和 16 DIP 数字均使用独立强调 token'
    Assert-Equal $true ([bool] $adapterValidation.FloatingQuickAddDayResourcesFollowLocalTheme) '悬浮窗快速新增真实控件日间使用局部暖色主题'
    Assert-Equal $true ([bool] $adapterValidation.FloatingQuickAddNightResourcesFollowLocalTheme) '悬浮窗快速新增真实控件夜间使用局部暖棕主题'
    Assert-Equal $true ([bool] $adapterValidation.FloatingQuickAddIndependentFromApplicationAutoTheme) '悬浮窗快速新增局部主题不受应用自动主题反向切换污染'
    Assert-Equal $true ([bool] $adapterValidation.FloatingQuickAddThemeResourceIsolationBehavior) '悬浮窗快速新增资源枚举负向夹具可逐控件单项失败'
    Assert-Equal 'TextBox,DatePicker,DatePickerPopup,ComboBox,ComboBoxPopup,TagSelector,TagSelectorPopup,CheckBox,ScrollViewer,ScrollBar' ([string]::Join(',', @($adapterValidation.FloatingQuickAddDayResourceChecks.PSObject.Properties.Name))) '悬浮窗快速新增日间资源枚举覆盖全部真实控件族'
    Assert-Equal 0 @($adapterValidation.FloatingQuickAddDayResourceChecks.PSObject.Properties | Where-Object { -not [bool] $_.Value }).Count '悬浮窗快速新增日间资源枚举没有失败项'
    Assert-Equal 0 @($adapterValidation.FloatingQuickAddNightResourceChecks.PSObject.Properties | Where-Object { -not [bool] $_.Value }).Count '悬浮窗快速新增夜间资源枚举没有失败项'
    Assert-Equal '#FFFFFDFC,#FF473323' ([string]::Join(',', @($adapterValidation.FloatingQuickAddLocalCanvasColors))) '悬浮窗快速新增局部表面使用暖白和深暖棕'
    Assert-Equal '#FFA24438,#FF6E492E' ([string]::Join(',', @($adapterValidation.FloatingQuickAddLocalActiveColors))) '悬浮窗快速新增局部交互强调使用珊瑚和深暖棕'
    Assert-Equal $true ([bool] $adapterValidation.TagPopupFollowsDayNightTheme) '打开的标签弹层随日夜主题自动换色'
    Assert-Equal 'Night,Day,Day,Night' ([string]::Join(',', @($adapterValidation.MainThemeBoundaryModes))) '主窗口按北京时间在日夜边界自动切换主题'
    Assert-Equal '#FFFFF6EF,#FF2B2118' ([string]::Join(',', @($adapterValidation.MainThemeBackgroundColors))) '主窗口日夜背景使用预期的暖白与深琥珀配色'
    Assert-Equal '8,16' ([string]::Join(',', @($adapterValidation.MainThemeCharacterOpacities))) '主窗口角色装饰在日夜主题下保持低对比度'
    Assert-Equal '#FFFFF7E8,#FF3B2B1D' ([string]::Join(',', @($adapterValidation.MainThemeCharacterPanelColors))) '主窗口角色场景栏在日夜主题间切换暖白与深琥珀配色'
    Assert-Equal $true ([bool] $adapterValidation.MainCharacterPanelDocksFollowDayNightTheme) '主窗口三个玩偶底座随日夜主题切换配色'
    Assert-Equal $true ([bool] $adapterValidation.MainReferenceDecorationAssetsLoaded) '主窗口日间参考装饰资产已加载'
    Assert-Equal $true ([bool] $adapterValidation.MainNightReferenceDecorationAssetsLoaded) '主窗口夜间角色与波浪装饰资产已加载'
    Assert-Equal $true ([bool] $adapterValidation.MainReferenceDecorationsAreNonInteractive) '主窗口日间参考装饰不会阻断真实控件输入'
    Assert-Equal $true ([bool] $adapterValidation.MainReferenceDecorationsFollowDayNightTheme) '主窗口日夜参考装饰按主题与工作区切换'
    Assert-Equal $true ([bool] $adapterValidation.MainReferenceDecorationLayerOrderMatches) '主窗口装饰层级顺序独立通过'
    Assert-Equal $true ([bool] $adapterValidation.MainWorkspaceResourcesFollowDayNightTheme) '主窗口工作区静态表面独立使用主题资源'
    Assert-Equal $true ([bool] $adapterValidation.MainDynamicCountdownCardsUseThemeResources) '主窗口动态倒计时卡片命中样本并使用主题资源'
    Assert-Equal $true ([int] $adapterValidation.MainDynamicCountdownCardSampleCount -gt 0) '主窗口动态倒计时卡片至少命中一个样本'
    Assert-Equal $true ([bool] $adapterValidation.MainDynamicNoteCardsUseThemeResources) '主窗口动态便签卡片命中样本并使用主题资源'
    Assert-Equal $true ([int] $adapterValidation.MainDynamicNoteCardSampleCount -gt 0) '主窗口动态便签卡片至少命中一个样本'
    Assert-Equal $true ([bool] $adapterValidation.MainNightReferenceGeometryMatchesDay) '主窗口夜间三角色姿态、尺寸、位置、露出高度与波浪嵌入几何沿用日间参考'
    Assert-Equal '1093,1079' ([string]::Join(',', @($adapterValidation.MainNightReferenceWaveColumnCounts))) '主窗口夜间波浪轮廓保持跨窗口连续覆盖'
    Assert-Equal $true ([bool] $adapterValidation.MainReferenceDecorationsShareScaleAtMinimumAndWideSizes) '主窗口日间参考装饰在最小和放大尺寸下与真实控件共用缩放坐标系'
    Assert-Equal $true ([bool] $adapterValidation.MainThemeUsesChinaStandardTime) '主窗口日夜主题以北京时间为准'
    Assert-Equal $true ([bool] $adapterValidation.MainThemeReminderLabelFollowsTheme) '主窗口提醒标签随日夜主题切换前景色'
    Assert-Equal $true ([bool] $adapterValidation.MainThemeSegmentSelectedTextContrast) '主窗口工作区选中态在日夜主题下保持可读对比度'
    Assert-Equal $true ([bool] $adapterValidation.WarmYellowPaletteMatchesLegacy) '全局主题恢复旧版暖黄日间色并保持深琥珀夜间色'
    Assert-Equal $true ([bool] $adapterValidation.PrimaryButtonHoverTextContrast) '主操作按钮悬停时保持珊瑚底白字的可读对比度'
    Assert-Equal $true ([bool] $adapterValidation.MainActionTextContrast) '主窗口核心操作在日夜默认、悬停与按下状态均保持可读对比度'
    Assert-Equal $true ([bool] $adapterValidation.ToolTipThemeTextContrast) '提示气泡在日夜主题下保持可读对比度'
    Assert-Equal $true ([bool] $adapterValidation.TagChipTextContrast) '标签芯片在日夜主题下保持暖墨文字的可读对比度'
    Assert-Equal $true ([bool] $adapterValidation.ActiveNotesGrouped) '未完成便签收拢在独立分组中'
    Assert-Equal $true ([bool] $adapterValidation.ActiveNotesExpandedByDefault) '未完成便签分组默认展开'
    Assert-Equal $true ([bool] $adapterValidation.ActiveNotesKeepDateOrder) '未完成便签组内按较新日期优先显示'
    Assert-Equal $true ([bool] $adapterValidation.ManagerNoteRowsFollowSortOrder) '完整管理窗口的实际便签行遵循管理排序'
    Assert-Equal $true ([bool] $adapterValidation.NoteDateSectionsCanCollapse) '未完成与已完成便签支持独立的日期级展开和收起'
    Assert-Equal $true ([bool] $adapterValidation.NoteDateSectionsAreIndependent) '同一日期的未完成与已完成折叠状态互不影响'
    Assert-Equal $true ([bool] $adapterValidation.NoteDateSectionStateSurvivesRefresh) '日期折叠状态在管理窗口刷新后保持'
    Assert-Equal $true ([bool] $adapterValidation.NoteStatusSectionsAreIndependent) '未完成与已完成状态分组独立折叠且刷新后保持'
    Assert-Equal $true ([bool] $adapterValidation.FloatingNotesKeepExistingSortOrder) '悬浮窗继续使用既有便签排序'
    Assert-Equal '24,60,24,60' ([string]::Join(',', @($adapterValidation.NoteReminderTimeOptionCounts))) '主窗口和悬浮窗的便签提醒时间选项完整'
    Assert-Equal $true ([bool] $adapterValidation.CrossDayReminderSelectionWorks) '便签提醒支持跨天选择并按北京时间触发'
    Assert-Equal $true ([bool] $adapterValidation.ReminderSummaryAdvancesAfterFirstDue) '首次提醒到期后主窗口摘要显示下一循环时间'
    Assert-Equal $true ([bool] $adapterValidation.OneTimeReminderLifecycleWorks) '一次性提醒支持离线补发并在送达后跨重启去重'
    Assert-Equal $true ([bool] $adapterValidation.ReminderRequiresContentRenderedConfirmation) '提醒只在真实 ContentRendered 后确认送达'
    Assert-Equal $true ([bool] $adapterValidation.ReminderPersistenceFailureStaysVisible) '一次提醒持久化失败时保持可见并阻止提前关闭'
    Assert-Equal $true ([bool] $adapterValidation.ReminderPersistenceRetryBackoffWorks) '一次提醒按 5/10/20/30 秒上限持续重试'
    Assert-Equal $true ([bool] $adapterValidation.ReminderDeliverySingleFlightWorks) '同一提醒 occurrence 在中心内只有一个条目和一个持久化重试'
    Assert-Equal $true ([bool] $adapterValidation.ReminderIndependentOccurrencesWork) '不同到期 occurrence 在同一中心独立显示且失败项不阻塞其他项确认'
    Assert-Equal $true ([bool] $adapterValidation.ReminderPersistenceFailureCloseGuardWorks) '持久化失败态拒绝普通 Close 且保持同一退避状态'
    Assert-Equal $true ([bool] $adapterValidation.RepeatingReminderRenderedLatchWorks) '重复提醒只在真实渲染后锁存当前周期'
    Assert-Equal $true ([bool] $adapterValidation.RepeatingReminderDistinctCyclesCoexist) '重复提醒相邻周期聚合到同一中心且同周期不重复加入'
    Assert-Equal $true ([bool] $adapterValidation.ReminderPersistenceFailureDisposeWorks) '应用退出可强制关闭失败态窗口且不触发提前重试'
    Assert-Equal $true ([bool] $adapterValidation.ReminderDiagnosticsExcludePrivatePayload) '提醒失败诊断不包含便签正文或完整设置载荷'
    foreach ($field in @(
            'ReminderCenterSingleWindow',
            'ReminderCenterSameTickBatch',
            'ReminderCenterAppendsAfterOpen',
            'ReminderCenterStableOrdering',
            'ReminderCenterEntriesRendered',
            'ReminderAcknowledgementDistinctFromDelivery',
            'ReminderAcknowledgementFailureRetainsEntry',
            'ReminderAcknowledgementFailureIsAtomic',
            'ReminderAcknowledgementMonotonicAcrossOccurrences',
            'ReminderCenterSingleItemDisposition',
            'ReminderCenterOpenSourcePreservesState',
            'ReminderCenterOpenSourceFindsExactNote',
            'ReminderCenterOpenSourceRestoresMinimizedWindow',
            'ReminderCenterPersistenceFailureRetainsCurrentSource',
            'ReminderCenterRemovesDeletedSource',
            'ReminderCenterCancelsDeletedSourceAckRetry',
            'ReminderCenterRemovesDisabledSource',
            'ReminderCenterRemovesCompletedSource',
            'ReminderCenterReopensAfterSourceRemoval',
            'ReminderCenterClosesWhenEmpty',
            'ReminderCenterReopens',
            'ReminderCenterScrollsLongContent',
            'ReminderCenterFollowsDayNightTheme',
            'ReminderCenterHasOnlySafeActions',
            'ReminderCenterDisplaysSourceContentAndTime',
            'ReminderCenterAccessible',
            'ReminderCenterIsThemed',
            'ReminderCenterSupportsKeyboardNavigation',
            'ReminderCenterInitialFocusAndAccessibleNameWork'
        )) {
        Assert-Equal $true ([bool] $adapterValidation.$field) "提醒中心验证字段通过：$field"
    }
    Assert-Equal $canonicalHash (Get-FileHash -LiteralPath $roundTripPath -Algorithm SHA256).Hash 'adapter validate-only 保持设置不变'

    [System.IO.File]::Copy($roundTripPath, $noteExercisePath)
    $noteExerciseOutput = Invoke-CheckedNativeCommand -FilePath $pwsh -Operation '经真实 UI 点击验证跨天便签提醒' -ArgumentList @(
        '-NoLogo',
        '-NoProfile',
        '-STA',
        '-File',
        $adapter,
        '-ValidateOnly',
        '-ExerciseNoteAdd',
        '-SettingsFile',
        $noteExercisePath,
        '-DiagnosticLogPath',
        $noteExerciseDiagnosticPath
    )
    $noteExercise = $noteExerciseOutput | ConvertFrom-Json
    Assert-Equal $true ([bool] $noteExercise.NoteAddViaClick) '主窗口真实点击添加便签'
    Assert-Equal $true ([bool] $noteExercise.NoteReminderViaSelector) '主窗口真实点击保存跨天提醒时间'
    Assert-Equal $true ([bool] $noteExercise.FloatingCrossDayReminderViaClick) '悬浮窗真实点击保存跨天提醒时间'
    Assert-Equal $true ([bool] $noteExercise.MainNoReminderViaClick) '主窗口真实点击保存不提醒'
    Assert-Equal $true ([bool] $noteExercise.MainOnceReminderViaClick) '主窗口真实点击保存一次性提醒'
    Assert-Equal $true ([bool] $noteExercise.MainRepeatingReminderViaClick) '主窗口真实点击保存重复提醒'
    Assert-Equal $true ([bool] $noteExercise.FloatingNoReminderViaClick) '悬浮窗真实点击保存不提醒'
    Assert-Equal $true ([bool] $noteExercise.FloatingOnceReminderViaClick) '悬浮窗真实点击保存一次性提醒'
    Assert-Equal $true ([bool] $noteExercise.FloatingRepeatingReminderViaClick) '悬浮窗真实点击保存重复提醒'
    $noteExerciseSettings = Get-Content -LiteralPath $noteExercisePath -Raw -Encoding UTF8 | ConvertFrom-Json
    Assert-Equal $true (@($noteExerciseSettings.notes | Where-Object text -eq '验证快速跨天提醒').Count -eq 1) '悬浮跨天提醒真实写入隔离设置'

    New-Item -ItemType Directory -Path $interactiveApplicationDirectory | Out-Null
    Copy-Item -LiteralPath $application -Destination $interactiveApplicationPath
    Copy-Item -LiteralPath $adapter -Destination $interactiveAdapterPath
    $requiredRequestProcess = Start-Process -FilePath $interactiveApplicationPath -ArgumentList @('--require-startup-request') `
        -PassThru -Wait -WindowStyle Hidden `
        -RedirectStandardOutput $requiredRequestOutputPath `
        -RedirectStandardError $requiredRequestErrorPath
    try {
        Assert-Equal 1 $requiredRequestProcess.ExitCode '生产模式缺失启动请求时 C# 宿主 fail-closed'
        $requiredRequestError = Get-Content -LiteralPath $requiredRequestErrorPath -Raw -Encoding UTF8
        Assert-Equal $true $requiredRequestError.Contains('启动请求存在但无法验证', [System.StringComparison]::Ordinal) '生产模式报告缺失启动请求而不进入可编辑窗口'
    }
    finally {
        $requiredRequestProcess.Dispose()
    }
    [System.IO.File]::WriteAllText(
        $interactiveStartupRequestPath,
        '{"schemaVersion":999,"launchToken":"unknown-schema","acceptanceState":"active"}',
        $utf8NoBom
    )
    $unknownSchemaProcess = Start-Process -FilePath $interactiveApplicationPath -ArgumentList @('--require-startup-request') `
        -PassThru -Wait -WindowStyle Hidden `
        -RedirectStandardOutput $unknownSchemaOutputPath `
        -RedirectStandardError $unknownSchemaErrorPath
    try {
        Assert-Equal 1 $unknownSchemaProcess.ExitCode '生产模式拒绝未知启动请求 schema'
        $unknownSchemaError = Get-Content -LiteralPath $unknownSchemaErrorPath -Raw -Encoding UTF8
        Assert-Equal $true $unknownSchemaError.Contains('启动请求存在但无法验证', [System.StringComparison]::Ordinal) 'C# 对未知启动请求 schema 保持 fail-closed'
    }
    finally {
        $unknownSchemaProcess.Dispose()
    }
    $interactiveLegacySettings = $fixture | ConvertFrom-Json
    $interactiveLegacySettings.schemaVersion = 9
    $interactiveLegacySettings | Add-Member -NotePropertyName themePreference -NotePropertyValue 'Auto' -Force
    foreach ($note in @($interactiveLegacySettings.notes)) {
        $note.PSObject.Properties.Remove('reminderIntervalMinutes')
        $note.PSObject.Properties.Remove('reminderStartUtc')
        $note | Add-Member -NotePropertyName reminder -NotePropertyValue ([pscustomobject]@{
                type = 'repeat'
                firstDueUtc = '2026-07-20T01:00:00.0000000+00:00'
                intervalMinutes = 1
            }) -Force
    }
    $interactiveMigrationUtc = [DateTimeOffset] '2026-07-20T02:16:00Z'
    $interactiveLegacyJson = $interactiveLegacySettings | ConvertTo-Json -Depth 20
    [System.IO.File]::WriteAllText($interactiveSettingsPath, $interactiveLegacyJson, $utf8NoBom)
    $interactiveLegacyHash = (Get-FileHash -LiteralPath $interactiveSettingsPath -Algorithm SHA256).Hash
    $interactiveLaunchToken = [guid]::NewGuid().ToString('N')
    $interactiveStartupRequest = [ordered]@{
        schemaVersion = 1
        launchToken = $interactiveLaunchToken
        requestedAtUtc = $interactiveMigrationUtc.ToString('o')
        acceptanceReadOnly = $true
        acceptanceState = 'active'
    } | ConvertTo-Json -Compress
    [System.IO.File]::WriteAllText(
        $interactiveStartupRequestPath,
        $interactiveStartupRequest,
        $utf8NoBom
    )

    $interactiveAdapterArguments = @(
        '-NoLogo',
        '-NoProfile',
        '-STA',
        '-File',
        ('"{0}"' -f $interactiveAdapterPath),
        '-ExitAfterReadyForTest',
        '-SettingsFile',
        ('"{0}"' -f $interactiveSettingsPath),
        '-DiagnosticLogPath',
        ('"{0}"' -f $interactiveDiagnosticPath)
    )
    $interactiveAdapterProcess = Start-Process -FilePath $pwsh -ArgumentList $interactiveAdapterArguments `
        -PassThru -WindowStyle Hidden `
        -RedirectStandardOutput $interactiveAdapterOutputPath `
        -RedirectStandardError $interactiveAdapterErrorPath
    $runtimeReceiptDeadline = [DateTimeOffset]::UtcNow.AddSeconds(15)
    while (
        -not (Test-Path -LiteralPath $interactiveRuntimeStatusPath -PathType Leaf) -and
        -not $interactiveAdapterProcess.HasExited -and
        [DateTimeOffset]::UtcNow -lt $runtimeReceiptDeadline
    ) {
        Start-Sleep -Milliseconds 50
    }
    if (-not (Test-Path -LiteralPath $interactiveRuntimeStatusPath -PathType Leaf)) {
        $interactiveAdapterError = if (Test-Path -LiteralPath $interactiveAdapterErrorPath -PathType Leaf) {
            Get-Content -LiteralPath $interactiveAdapterErrorPath -Raw
        }
        else {
            ''
        }
        throw "交互启动未生成 runtime-status.json：$interactiveAdapterError"
    }

    $runtimeReceipt = Get-Content -LiteralPath $interactiveRuntimeStatusPath -Raw -Encoding UTF8 |
        ConvertFrom-Json
    Assert-Equal 4 ([int] $runtimeReceipt.schemaVersion) '交互启动回执 schemaVersion'
    Assert-Equal 'window-ready' ([string] $runtimeReceipt.stage) '交互启动回执阶段'
    Assert-Equal $interactiveLaunchToken ([string] $runtimeReceipt.launchToken) '交互启动回执令牌'
    Assert-Equal 'China Standard Time' ([string] $runtimeReceipt.timeZoneId) '交互启动回执时区'
    Assert-Equal (
        [System.IO.Path]::GetFullPath($interactiveAdapterPath)
    ) (
        [System.IO.Path]::GetFullPath([string] $runtimeReceipt.applicationPath)
    ) '交互启动回执保持 adapter 路径身份'
    Assert-Equal (
        [System.Diagnostics.Process]::GetCurrentProcess().SessionId
    ) (
        [int] $runtimeReceipt.sessionId
    ) '交互启动回执属于当前交互会话'

    $interactiveApplicationProcess = [System.Diagnostics.Process]::GetProcessById(
        [int] $runtimeReceipt.processId
    )
    Assert-Equal (
        [System.IO.Path]::GetFullPath($interactiveApplicationPath)
    ) (
        [System.IO.Path]::GetFullPath($interactiveApplicationProcess.Path)
    ) '交互启动 PID 对应临时应用根内 C# EXE'
    Assert-Equal $false $interactiveAdapterProcess.HasExited 'window-ready 时 adapter 仍在等待 C# EXE'
    $acceptanceDiagnostic = Get-Content -LiteralPath $interactiveDiagnosticPath -Raw -Encoding UTF8
    Assert-Equal $true $acceptanceDiagnostic.Contains('AcceptanceReadOnlyEnabled', [System.StringComparison]::Ordinal) '延期验收启动时 C# 窗口进入只读状态'

    $duplicateApplicationArguments = @(
        '--settings-file',
        ('"{0}"' -f $interactiveSettingsPath),
        '--diagnostic-log-path',
        ('"{0}"' -f $interactiveDiagnosticPath)
    )
    $duplicateApplicationProcess = Start-Process -FilePath $interactiveApplicationPath -ArgumentList $duplicateApplicationArguments `
        -PassThru -WindowStyle Hidden `
        -RedirectStandardOutput $duplicateApplicationOutputPath `
        -RedirectStandardError $duplicateApplicationErrorPath
    if (-not $duplicateApplicationProcess.WaitForExit(7000)) {
        $duplicateError = if (Test-Path -LiteralPath $duplicateApplicationErrorPath -PathType Leaf) {
            Get-Content -LiteralPath $duplicateApplicationErrorPath -Raw
        }
        else { '' }
        $duplicateLog = if (Test-Path -LiteralPath $interactiveDiagnosticPath -PathType Leaf) {
            Get-Content -LiteralPath $interactiveDiagnosticPath -Raw -Encoding UTF8
        }
        else { '' }
        $interactiveApplicationProcess.Refresh()
        throw "真实第二个 C# EXE 未在单实例门禁期限内退出；首实例已退出=$($interactiveApplicationProcess.HasExited)；stderr=$duplicateError；diagnostic=$duplicateLog"
    }
    $duplicateApplicationProcess.WaitForExit()
    Assert-Equal 1 $duplicateApplicationProcess.ExitCode '验收只读主实例不提供交互激活 ACK'
    $interactiveApplicationProcess.Refresh()
    Assert-Equal $false $interactiveApplicationProcess.HasExited '第二实例激活超时后首个 C# EXE 仍存活'
    $duplicateDiagnostic = Get-Content -LiteralPath $interactiveDiagnosticPath -Raw -Encoding UTF8
    Assert-Equal $true $duplicateDiagnostic.Contains('InstanceActivationTimedOut', [System.StringComparison]::Ordinal) '验收只读主实例未监听时第二实例记录超时诊断'
    Assert-Equal $false $duplicateDiagnostic.Contains('InstanceActivationSucceeded', [System.StringComparison]::Ordinal) '验收只读阶段不冒充恢复成功'
    $duplicateApplicationProcess.Dispose()
    $duplicateApplicationProcess = $null

    Assert-Equal $interactiveLegacyHash (Get-FileHash -LiteralPath $interactiveSettingsPath -Algorithm SHA256).Hash 'active 验收期间 settings 文件保持只读'
    Assert-Equal $true (Complete-CountdownStartupRequest -Path $interactiveStartupRequestPath -LaunchToken $interactiveLaunchToken) '同 token 原子授权 C# 持久化 schema 迁移'
    [void] (Wait-CountdownStartupRequestState -Path $interactiveStartupRequestPath -LaunchToken $interactiveLaunchToken -ExpectedState ready -TimeoutSeconds 5 -PollMilliseconds 50)
    $acceptanceReadyDeadline = [DateTimeOffset]::UtcNow.AddSeconds(5)
    do {
        $acceptanceReadyDiagnostic = Get-Content -LiteralPath $interactiveDiagnosticPath -Raw -Encoding UTF8
        if ($acceptanceReadyDiagnostic.Contains('AcceptanceMigrationReady', [System.StringComparison]::Ordinal)) { break }
        Start-Sleep -Milliseconds 50
    } while ([DateTimeOffset]::UtcNow -lt $acceptanceReadyDeadline)
    Assert-Equal $true $acceptanceReadyDiagnostic.Contains('AcceptanceMigrationReady', [System.StringComparison]::Ordinal) 'C# 持久化迁移后确认 ready 且窗口继续只读'
    $interactiveMigratedJson = Get-Content -LiteralPath $interactiveSettingsPath -Raw -Encoding UTF8
    $interactiveMigratedHash = (Get-FileHash -LiteralPath $interactiveSettingsPath -Algorithm SHA256).Hash
    $interactiveMigratedSettings = $interactiveMigratedJson | ConvertFrom-Json
    Assert-Equal 10 ([int] $interactiveMigratedSettings.schemaVersion) 'released 阶段把 schema v9 写回 canonical v10'
    Assert-Equal 'Auto' ([string] $interactiveMigratedSettings.themePreference) 'released 阶段保留 schema v9 自动主题偏好'
    Assert-Equal $false ($interactiveLegacyHash -eq $interactiveMigratedHash) 'released 阶段实际写回迁移后的设置'
    $interactiveRuleId = [string] $interactiveMigratedSettings.notes[0].reminderRules[0].id
    $interactiveReminderState = $interactiveMigratedSettings.reminderStates.PSObject.Properties[$interactiveRuleId].Value
    Assert-Equal $interactiveMigrationUtc ([DateTimeOffset] $interactiveReminderState.handledThroughUtc).ToUniversalTime() 'C# 初次迁移使用启动请求冻结时刻而非后续墙钟'
    Assert-Equal $true (
        Test-CountdownSettingsDeploymentTransition `
            -BeforeHash $interactiveLegacyHash `
            -AfterHash $interactiveMigratedHash `
            -BeforeJson $interactiveLegacyJson `
            -AfterJson $interactiveMigratedJson `
            -MigrationUtc $interactiveMigrationUtc
    ) 'released 阶段迁移保持 schema v9 数据语义'
    Assert-Equal $true (Set-CountdownStartupRequestFinalized -Path $interactiveStartupRequestPath -LaunchToken $interactiveLaunchToken) '事务提交标记只把 ready 请求推进为 finalized'
    $acceptanceReleaseDeadline = [DateTimeOffset]::UtcNow.AddSeconds(5)
    do {
        $acceptanceReleaseDiagnostic = Get-Content -LiteralPath $interactiveDiagnosticPath -Raw -Encoding UTF8
        if ($acceptanceReleaseDiagnostic.Contains('AcceptanceReadOnlyReleased', [System.StringComparison]::Ordinal)) { break }
        Start-Sleep -Milliseconds 50
    } while ([DateTimeOffset]::UtcNow -lt $acceptanceReleaseDeadline)
    Assert-Equal $true $acceptanceReleaseDiagnostic.Contains('AcceptanceReadOnlyReleased', [System.StringComparison]::Ordinal) 'finalized 后 C# 窗口解除只读状态'
    $acceptanceStatus = Get-Content -LiteralPath $interactiveAcceptanceStatusPath -Raw -Encoding UTF8 | ConvertFrom-Json
    Assert-Equal 1 ([int] $acceptanceStatus.schemaVersion) '应用解锁确认回执使用 schema v1'
    Assert-Equal 'finalized-ready' ([string] $acceptanceStatus.stage) '应用真实观察 finalized 后写出确认回执'
    Assert-Equal $interactiveLaunchToken ([string] $acceptanceStatus.launchToken) '应用解锁确认回执绑定同一 token'
    Assert-Equal ([int] $runtimeReceipt.processId) ([int] $acceptanceStatus.processId) '应用解锁确认回执绑定同一 C# PID'

    $finalizedActivationOutput = Invoke-CheckedNativeCommand `
        -FilePath $pwsh `
        -Operation '使用独立 canonical v10 设置验证 finalized 后实例激活' `
        -ArgumentList @(
            '-NoLogo',
            '-NoProfile',
            '-File',
            $PSCommandPath,
            '-InstanceActivationOnly',
            '-InstanceActivationClientMode',
            'Finalized'
        )
    $finalizedActivation = $finalizedActivationOutput | ConvertFrom-Json
    Assert-Equal 0 ([int] $finalizedActivation.Failed) '独立 finalized 激活场景全部通过'
    Assert-Equal $true ([bool] $finalizedActivation.InstanceActivationOnly) '独立 finalized 激活使用真实进程 seam'
    Assert-Equal 1 ([int] $finalizedActivation.ProcessScenarios) '独立 finalized 激活只运行一个进程场景'

    if (-not $interactiveAdapterProcess.WaitForExit(45000)) {
        throw '交互启动未在测试退出期限内结束。'
    }
    $interactiveAdapterProcess.WaitForExit()
    Assert-Equal 0 $interactiveAdapterProcess.ExitCode '交互启动 adapter 透传 C# 正常退出码'
    $interactiveApplicationProcess.Refresh()
    Assert-Equal $true $interactiveApplicationProcess.HasExited '测试参数触发 C# EXE 退出'
    [pscustomobject]@{
        Passed = $script:assertions
        Failed = 0
        SchemaVersion = $canonicalSettings.schemaVersion
        TimeZone = $canonicalSettings.timeZoneId
        TempIsolation = $true
        RuntimeSchemaVersion = $runtimeReceipt.schemaVersion
        AdapterUiValidated = $true
    } | ConvertTo-Json
}
finally {
    if ($null -ne $duplicateApplicationProcess) {
        try {
            if (-not $duplicateApplicationProcess.HasExited) {
                $duplicateApplicationProcess.Kill()
                [void] $duplicateApplicationProcess.WaitForExit(5000)
            }
        }
        finally {
            $duplicateApplicationProcess.Dispose()
        }
    }
    if ($null -ne $interactiveApplicationProcess) {
        try {
            if (-not $interactiveApplicationProcess.HasExited) {
                $interactiveApplicationProcess.Kill()
                [void] $interactiveApplicationProcess.WaitForExit(5000)
            }
        }
        finally {
            $interactiveApplicationProcess.Dispose()
        }
    }
    if ($null -ne $interactiveAdapterProcess) {
        try {
            if (-not $interactiveAdapterProcess.HasExited) {
                $interactiveAdapterProcess.Kill($true)
                [void] $interactiveAdapterProcess.WaitForExit(5000)
            }
        }
        finally {
            $interactiveAdapterProcess.Dispose()
        }
    }
    if ($null -ne $mutexHolderProcess -and -not $mutexHolderProcess.HasExited) {
        Stop-Process -Id $mutexHolderProcess.Id -Force -ErrorAction SilentlyContinue
        [void] $mutexHolderProcess.WaitForExit(5000)
    }
    if (Test-Path -LiteralPath $testRoot -PathType Container) {
        if (-not $testRoot.StartsWith($testCapabilityRootPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "拒绝清理受管测试证据根外的 C# 测试数据：$testRoot"
        }
        $cleanupJson = & $safeTreeDelete `
            -Mode BatchPurge `
            -TargetPaths $testRoot `
            -CapabilityRoot $testCapabilityRoot `
            -ConfirmApply `
            -ConfirmPurge `
            -Json | Out-String
        if ($LASTEXITCODE -ne 0) {
            throw "受管 C# 测试目录清理失败：$testRoot"
        }
        $cleanup = $cleanupJson | ConvertFrom-Json
        if (-not $cleanup.completed -or $cleanup.purgedCount -ne 1) {
            throw "受管 C# 测试目录未完成清理：$testRoot"
        }
    }
}
