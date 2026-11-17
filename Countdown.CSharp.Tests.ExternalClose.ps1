#requires -Version 7.0
# #295 外部关闭：完整 CSharp 与 ExternalCloseOnly/CI 共用同一组真实 EXE 场景和断言。
function Invoke-ExternalCloseProgramLaunch {
    param(
        [Parameter(Mandatory)] [string] $Application,
        [Parameter(Mandatory)] [string] $InstallRoot,
        [switch] $CancelOnManager
    )

    $refusingProxy = [System.Net.Sockets.Socket]::new(
        [System.Net.Sockets.AddressFamily]::InterNetwork,
        [System.Net.Sockets.SocketType]::Stream,
        [System.Net.Sockets.ProtocolType]::Tcp)
    $process = $null
    $secondProcess = $null
    try {
        $refusingProxy.Bind([System.Net.IPEndPoint]::new([System.Net.IPAddress]::Loopback, 0))
        $startInfo = [System.Diagnostics.ProcessStartInfo]::new($Application)
        $startInfo.WorkingDirectory = $InstallRoot
        $startInfo.UseShellExecute = $false
        $startInfo.CreateNoWindow = $true
        $startInfo.RedirectStandardOutput = $true
        $startInfo.RedirectStandardError = $true
        $startInfo.StandardOutputEncoding = [System.Text.UTF8Encoding]::new($false)
        $startInfo.StandardErrorEncoding = [System.Text.UTF8Encoding]::new($false)
        foreach ($argument in @('--exit-after-ready-for-test', '--exit-after-ready-seconds', '300')) {
            $startInfo.ArgumentList.Add($argument)
        }
        if ($CancelOnManager) {
            foreach ($argument in @(
                    '--instance-activation-test-state', 'MainVisible',
                    '--settings-file', (Join-Path $InstallRoot 'settings.json'),
                    '--diagnostic-log-path', (Join-Path $InstallRoot 'logs\application.jsonl'))) {
                $startInfo.ArgumentList.Add($argument)
            }
        }
        foreach ($proxyVariable in @('HTTPS_PROXY', 'HTTP_PROXY', 'ALL_PROXY')) {
            $startInfo.Environment[$proxyVariable] = "http://127.0.0.1:$($refusingProxy.LocalEndPoint.Port)"
        }
        [void] $startInfo.Environment.Remove('NO_PROXY')
        $process = [System.Diagnostics.Process]::Start($startInfo)
        $stdoutTask = $process.StandardOutput.ReadToEndAsync()
        $stderrTask = $process.StandardError.ReadToEndAsync()

        # 首帧就绪后按标题找悬浮窗；隔离桌面上不要求 Win32 可见枚举。
        $receiptPath = Join-Path $InstallRoot 'runtime-status.json'
        $readyDeadline = [DateTimeOffset]::UtcNow.AddSeconds(90)
        $floating = $null
        while ($null -eq $floating -and -not $process.HasExited -and [DateTimeOffset]::UtcNow -lt $readyDeadline) {
            $receipt = Read-DesktopLauncherRuntimeReceipt -Path $receiptPath
            if ($null -ne $receipt -and [string] $receipt.stage -eq 'window-ready' -and [int] $receipt.processId -eq $process.Id) {
                $floating = @(
                    Get-ProcessTopLevelWindows -ProcessId $process.Id |
                        Where-Object Title -eq '敏敏子悬浮窗'
                ) | Select-Object -First 1
            }
            if ($null -eq $floating) { Start-Sleep -Milliseconds 50 }
        }

        $owner = $null
        $ownerVisible = $null
        $posted = $false
        if ($null -ne $floating) {
            $ownerHandle = [MinminCountdownActivationWindowProbe]::GetWindow([IntPtr]::new([long] $floating.Handle), 4)
            $owner = @(
                Get-ProcessTopLevelWindows -ProcessId $process.Id |
                    Where-Object Handle -eq $ownerHandle.ToInt64()
            ) | Select-Object -First 1
            if ($null -ne $owner) {
                $ownerVisible = [MinminCountdownActivationWindowProbe]::IsWindowVisible($ownerHandle)
                $posted = [MinminCountdownActivationWindowProbe]::PostMessage($ownerHandle, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero)
            }
        }

        $confirmation = $null
        $cancelPosted = $false
        $replacement = $null
        $secondInstanceExited = $false
        $secondInstanceExitCode = $null
        $primaryAliveAfterActivation = $false
        if ($posted -and $CancelOnManager) {
            $confirmationDeadline = [DateTimeOffset]::UtcNow.AddSeconds(20)
            while ($null -eq $confirmation -and -not $process.HasExited -and [DateTimeOffset]::UtcNow -lt $confirmationDeadline) {
                $confirmation = @(
                    Get-ProcessTopLevelWindows -ProcessId $process.Id |
                        Where-Object Title -eq '退出敏敏子倒计时'
                ) | Select-Object -First 1
                if ($null -eq $confirmation) { Start-Sleep -Milliseconds 50 }
            }
            if ($null -ne $confirmation) {
                $cancelPosted = [MinminCountdownActivationWindowProbe]::PostMessage(
                    [IntPtr]::new([long] $confirmation.Handle), 0x0010, [IntPtr]::Zero, [IntPtr]::Zero)
            }

            if ($cancelPosted) {
                $replacementDeadline = [DateTimeOffset]::UtcNow.AddSeconds(20)
                while ($null -eq $replacement -and -not $process.HasExited -and [DateTimeOffset]::UtcNow -lt $replacementDeadline) {
                    $replacement = @(
                        Get-ProcessTopLevelWindows -ProcessId $process.Id |
                            Where-Object { $_.Title -eq '敏敏子悬浮窗' -and $_.Handle -ne $floating.Handle }
                    ) | Select-Object -First 1
                    if ($null -eq $replacement) { Start-Sleep -Milliseconds 50 }
                }

                # 不带参数的第二实例与快捷方式走同一条单实例激活路径。
                $secondStart = [System.Diagnostics.ProcessStartInfo]::new($Application)
                $secondStart.WorkingDirectory = $InstallRoot
                $secondStart.UseShellExecute = $false
                $secondStart.CreateNoWindow = $true
                foreach ($proxyVariable in @('HTTPS_PROXY', 'HTTP_PROXY', 'ALL_PROXY')) {
                    $secondStart.Environment[$proxyVariable] = "http://127.0.0.1:$($refusingProxy.LocalEndPoint.Port)"
                }
                [void] $secondStart.Environment.Remove('NO_PROXY')
                $secondProcess = [System.Diagnostics.Process]::Start($secondStart)
                $secondInstanceExited = $secondProcess.WaitForExit(30000)
                if ($secondInstanceExited) { $secondInstanceExitCode = $secondProcess.ExitCode }
                $process.Refresh()
                $primaryAliveAfterActivation = -not $process.HasExited
            }
        }
        elseif ($posted) {
            [void] $process.WaitForExit(30000)
        }
        $exitedOnItsOwn = $process.HasExited
        if (-not $exitedOnItsOwn) {
            $process.Kill()
            [void] $process.WaitForExit(5000)
        }
        [void] $stdoutTask.GetAwaiter().GetResult()
        [pscustomobject]@{
            ProcessId = $process.Id
            FloatingFound = $null -ne $floating
            OwnerFound = $null -ne $owner
            OwnerTitle = if ($null -ne $owner) { [string] $owner.Title } else { $null }
            OwnerVisible = $ownerVisible
            Posted = $posted
            ExitedAfterClose = $posted -and -not $CancelOnManager -and $exitedOnItsOwn
            ExitCode = if ($exitedOnItsOwn) { $process.ExitCode } else { $null }
            ConfirmationFound = $null -ne $confirmation
            CancelPosted = $cancelPosted
            ReplacementFound = $null -ne $replacement
            SecondInstanceProcessId = if ($null -ne $secondProcess) { $secondProcess.Id } else { $null }
            SecondInstanceExited = $secondInstanceExited
            SecondInstanceExitCode = $secondInstanceExitCode
            PrimaryAliveAfterActivation = $primaryAliveAfterActivation
            Stderr = $stderrTask.GetAwaiter().GetResult()
        }
    }
    finally {
        foreach ($externalProcess in @($secondProcess, $process)) {
            if ($null -ne $externalProcess) {
                if (-not $externalProcess.HasExited) {
                    $externalProcess.Kill()
                    [void] $externalProcess.WaitForExit(5000)
                }
                $externalProcess.Dispose()
            }
        }
        $refusingProxy.Dispose()
    }
}

function Invoke-ExternalCloseProcessTests {
    param(
        [Parameter(Mandatory)] [string] $Application,
        [Parameter(Mandatory)] [string] $TestRoot
    )

    $externalCloseInstallRoot = Join-Path $TestRoot 'external-close\install-root'
    $externalCloseProgramDirectory = Join-Path $externalCloseInstallRoot 'app'
    New-Item -ItemType Directory -Path $externalCloseInstallRoot | Out-Null
    Copy-Item -LiteralPath (Split-Path -Parent $Application) -Destination $externalCloseProgramDirectory -Recurse
    $externalClose = Invoke-ExternalCloseProgramLaunch `
        -Application (Join-Path $externalCloseProgramDirectory 'MinminCountdown.exe') `
        -InstallRoot $externalCloseInstallRoot
    $externalCloseEvents = @(
        (Read-SharedTestEvidenceText -Path (Join-Path $externalCloseInstallRoot 'logs\application.jsonl')) -split "`r?`n" |
            Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
            ForEach-Object { $_ | ConvertFrom-Json } |
            Where-Object { [int] $_.processId -eq $externalClose.ProcessId } |
            ForEach-Object { [string] $_.event })
    $externalCloseDetail = "exit=$($externalClose.ExitCode)；events=$($externalCloseEvents -join ',')；owner=$($externalClose.OwnerTitle) visible=$($externalClose.OwnerVisible)；stderr=$($externalClose.Stderr)"
    Assert-Equal $true $externalClose.FloatingFound "外部关闭：悬浮窗首帧就绪；$externalCloseDetail"
    Assert-Equal $true $externalClose.OwnerFound "外部关闭：悬浮窗归本进程的一个顶层所有者窗口；$externalCloseDetail"
    Assert-Equal $false ([bool] $externalClose.OwnerVisible) "外部关闭：悬浮窗的所有者是 ShowInTaskbar=False 带来的不可见窗口；$externalCloseDetail"
    Assert-Equal $true $externalClose.Posted "外部关闭：向悬浮窗的所有者投递 WM_CLOSE；$externalCloseDetail"
    Assert-Equal $true $externalClose.ExitedAfterClose "外部关闭：所有者收到 WM_CLOSE 后应用在期限内自行结束；$externalCloseDetail"
    Assert-Equal 0 $externalClose.ExitCode "外部关闭：应用正常结束，退出码 0；$externalCloseDetail"
    Assert-Equal 0 @($externalCloseEvents | Where-Object { $_ -eq 'FatalUnhandledException' }).Count "外部关闭：不记 FatalUnhandledException；$externalCloseDetail"
    Assert-Equal 0 @($externalCloseEvents | Where-Object { $_ -eq 'ExitConfirmationOffered' }).Count "外部关闭：不弹退出确认；$externalCloseDetail"
    Assert-Equal 1 @($externalCloseEvents | Where-Object { $_ -eq 'ExitConfirmationSkippedExternalClose' }).Count "外部关闭：诊断日志记一条外部关闭、跳过确认事件；$externalCloseDetail"
    Assert-Equal 1 @($externalCloseEvents | Where-Object { $_ -eq 'ProcessExited' }).Count "外部关闭：走完正常关停；$externalCloseDetail"

    $externalCancelInstallRoot = Join-Path $TestRoot 'external-close-cancel\install-root'
    $externalCancelProgramDirectory = Join-Path $externalCancelInstallRoot 'app'
    New-Item -ItemType Directory -Path $externalCancelInstallRoot | Out-Null
    Copy-Item -LiteralPath (Split-Path -Parent $Application) -Destination $externalCancelProgramDirectory -Recurse
    $externalCancel = Invoke-ExternalCloseProgramLaunch `
        -Application (Join-Path $externalCancelProgramDirectory 'MinminCountdown.exe') `
        -InstallRoot $externalCancelInstallRoot `
        -CancelOnManager
    $externalCancelEntries = @(
        (Read-SharedTestEvidenceText -Path (Join-Path $externalCancelInstallRoot 'logs\application.jsonl')) -split "`r?`n" |
            Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
            ForEach-Object { $_ | ConvertFrom-Json } |
            Where-Object { [int] $_.processId -eq $externalCancel.ProcessId })
    $externalCancelEvents = @($externalCancelEntries | ForEach-Object { [string] $_.event })
    $externalCancelDetail = "secondExit=$($externalCancel.SecondInstanceExitCode)；events=$($externalCancelEvents -join ',')；stderr=$($externalCancel.Stderr)"
    Assert-Equal $true $externalCancel.FloatingFound "外部关闭后取消：悬浮窗首帧就绪；$externalCancelDetail"
    Assert-Equal $true $externalCancel.Posted "外部关闭后取消：向悬浮窗所有者投递 WM_CLOSE；$externalCancelDetail"
    Assert-Equal $true $externalCancel.ConfirmationFound "外部关闭后取消：管理窗口上弹出退出确认；$externalCancelDetail"
    Assert-Equal $true $externalCancel.CancelPosted "外部关闭后取消：WM_CLOSE 取消退出确认；$externalCancelDetail"
    Assert-Equal $true $externalCancel.ReplacementFound "外部关闭后取消：应用创建新的悬浮窗，而不是留下已关闭实例；$externalCancelDetail"
    Assert-Equal $true $externalCancel.SecondInstanceExited "外部关闭后取消：第二个实例在期限内结束；$externalCancelDetail"
    Assert-Equal 0 $externalCancel.SecondInstanceExitCode "外部关闭后取消：第二个实例收到激活确认后正常退出；$externalCancelDetail"
    Assert-Equal $true $externalCancel.PrimaryAliveAfterActivation "外部关闭后取消：激活之后主实例仍运行；$externalCancelDetail"
    Assert-Equal 1 @($externalCancelEvents | Where-Object { $_ -eq 'ExitConfirmationOffered' }).Count "外部关闭后取消：记录一次退出确认；$externalCancelDetail"
    Assert-Equal 1 @($externalCancelEvents | Where-Object { $_ -eq 'ExitConfirmationCancelled' }).Count "外部关闭后取消：记录取消；$externalCancelDetail"
    Assert-Equal 1 @($externalCancelEvents | Where-Object { $_ -eq 'FloatingWindowRecreatedAfterExternalClose' }).Count "外部关闭后取消：记录悬浮窗重建；$externalCancelDetail"
    Assert-Equal 0 @($externalCancelEvents | Where-Object { $_ -eq 'ExitConfirmationSkippedExternalClose' }).Count "外部关闭后取消：管理窗口可用时不走无窗口外部关闭；$externalCancelDetail"
    Assert-Equal 0 @($externalCancelEvents | Where-Object { $_ -eq 'FatalUnhandledException' }).Count "外部关闭后取消与激活：不记 FatalUnhandledException；$externalCancelDetail"
    $externalCancelActivation = @($externalCancelEntries | Where-Object { $_.event -eq 'InstanceActivationHandled' })
    Assert-Equal 1 $externalCancelActivation.Count "外部关闭后取消：主实例处理一次激活；$externalCancelDetail"
    Assert-Equal 'Activated|VisibleNormal|1|True|True' `
        "$($externalCancelActivation[0].activationResult)|$($externalCancelActivation[0].activationStateAfter)|$($externalCancelActivation[0].interactiveFloatingWindowCount)|$($externalCancelActivation[0].mainWindowVisibleAfter)|$($externalCancelActivation[0].managerStatePreserved)" `
        "外部关闭后取消：激活恢复唯一悬浮窗并保留可见管理窗口的状态；$externalCancelDetail"
    Write-TestSegmentTiming -Name 'ExternalClose'
}

function Invoke-ExternalCloseOnlyTests {
    param([Parameter(Mandatory)] [string] $RepositoryRoot)

    $application = Join-Path $RepositoryRoot 'app\MinminCountdown.exe'
    if (-not (Test-Path -LiteralPath $application -PathType Leaf)) { throw "未找到待验证的 C# 应用宿主：$application" }
    $ephemeralCi = $env:GITHUB_ACTIONS -eq 'true'
    $safeTreeDelete = 'D:\Codex\.codex\scripts\invoke-safe-tree-delete.cmd'
    if (-not $ephemeralCi -and -not (Test-Path -LiteralPath $safeTreeDelete -PathType Leaf)) { throw "未找到受管 C# 测试目录清理入口：$safeTreeDelete" }
    $capabilityRoot = [System.IO.Path]::GetFullPath('D:\agents\run-evidence')
    $testRoot = [System.IO.Path]::GetFullPath((Join-Path $capabilityRoot ('clock-countdown-external-close\' + [guid]::NewGuid().ToString('N'))))
    if (-not $testRoot.StartsWith($capabilityRoot.TrimEnd('\') + '\', [System.StringComparison]::OrdinalIgnoreCase)) { throw '拒绝在受管测试证据根外创建 ExternalClose 数据。' }
    New-Item -ItemType Directory -Path $testRoot | Out-Null
    $originalError = $null
    try {
        Invoke-ExternalCloseProcessTests -Application $application -TestRoot $testRoot
        [pscustomobject]@{ Passed = $script:assertions; Failed = 0; ExternalCloseOnly = $true; ProcessScenarios = 2 }
    }
    catch { $originalError = $_; throw }
    finally {
        # GitHub runner 是一次性 VM，测试根位于仓库外的 D:\agents\run-evidence；
        # 本机仍必须走受管清理，不能用 CI 跳过反过来说明本机已清理。
        if (-not $ephemeralCi) {
            try {
                Invoke-ManagedTestRootPurge -SafeTreeDelete $safeTreeDelete -TestRoot $testRoot -CapabilityRoot $capabilityRoot `
                    -FailedMessage '受管 ExternalClose 测试目录清理失败' -IncompleteMessage '受管 ExternalClose 测试目录未完成清理'
            }
            catch {
                if ($null -eq $originalError) { throw }
                [Console]::Error.WriteLine("WARNING: 测试已失败，清理失败不覆盖原始错误：$($_.Exception.Message)")
            }
        }
    }
}
