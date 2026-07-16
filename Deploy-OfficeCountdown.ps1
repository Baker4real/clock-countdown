#requires -Version 7.0

[CmdletBinding()]
param(
    [switch] $ValidateOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = $PSScriptRoot
$requiredFiles = @(
    'Countdown.App.psm1',
    'Countdown.Core.psm1',
    'Countdown.InstallTransaction.psm1',
    'Countdown.Notes.psm1',
    'Countdown.Runtime.psm1',
    'Countdown.Settings.psm1',
    'Countdown.Core.Tests.ps1',
    'Countdown.Ui.Tests.ps1',
    '敏敏子倒计时.ps1',
    'CountdownLauncher.exe',
    'countdown-icon.ico',
    'Install-Countdown.ps1'
)

function Get-InstallerHashes {
    param([Parameter(Mandatory)] [string] $InstallerPath)

    $hashes = @{}
    $matches = (Get-Content -Raw -LiteralPath $InstallerPath | Select-String -Pattern "'([^']+)'\s*=\s*'([A-F0-9]{64})'" -AllMatches).Matches
    foreach ($match in $matches) { $hashes[$match.Groups[1].Value] = $match.Groups[2].Value }
    if ($hashes.Count -ne 11) { throw "无法从安装器读取完整性清单，实际读取到 $($hashes.Count) 项。" }
    $hashes
}

function Invoke-OfficeEncodedCommand {
    param([Parameter(Mandatory)] [string] $ScriptText)

    $runtimeGuard = "`$ErrorActionPreference = 'Stop'; if (`$PSVersionTable.PSVersion.Major -lt 7 -or `$PSVersionTable.PSEdition -ne 'Core') { throw '办公室设备必须使用 PowerShell 7+（pwsh.exe）。' }"
    $encoded = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes("$runtimeGuard`r`n$ScriptText"))
    & ssh.exe @script:sshOptions $env:REPLICATOR_OFFICE_SSH_DESTINATION "pwsh.exe -NoLogo -NoProfile -EncodedCommand $encoded"
    if ($LASTEXITCODE -ne 0) { throw '办公室设备上的远程命令失败。' }
}

if (@(git -C $repoRoot status --porcelain).Count -ne 0) { throw '工作树不干净，拒绝部署未提交内容。' }
if ((git -C $repoRoot branch --show-current).Trim() -ne 'main') { throw '仅允许从 main 分支部署。' }
git -C $repoRoot fetch origin main
if ($LASTEXITCODE -ne 0) { throw '无法同步 origin/main。' }
$commit = (git -C $repoRoot rev-parse HEAD).Trim()
if ($commit -ne (git -C $repoRoot rev-parse origin/main).Trim()) { throw '本地 HEAD 与 origin/main 不一致，拒绝部署。' }

$installerPath = Join-Path $repoRoot 'Install-Countdown.ps1'
$expectedHashes = Get-InstallerHashes -InstallerPath $installerPath
foreach ($file in $requiredFiles) {
    if (-not (Test-Path -LiteralPath (Join-Path $repoRoot $file) -PathType Leaf)) { throw "缺少部署文件：$file" }
}

$shortCommit = $commit.Substring(0, 7)
$runId = "$(Get-Date -Format 'yyyyMMddHHmmssfff')-$([guid]::NewGuid().ToString('N').Substring(0, 8))"
$evidenceRoot = "D:\Codex\.codex\run-evidence\clock-countdown-deploy\$shortCommit\$runId"
$packageRoot = Join-Path $evidenceRoot 'windows-package'
$extractRoot = Join-Path $evidenceRoot 'package-check'
$archive = Join-Path $evidenceRoot 'clock-countdown-windows.zip'
New-Item -ItemType Directory -Path $packageRoot -Force | Out-Null
foreach ($file in $requiredFiles) { Copy-Item -LiteralPath (Join-Path $repoRoot $file) -Destination (Join-Path $packageRoot $file) -Force }
Compress-Archive -Path (Join-Path $packageRoot '*') -DestinationPath $archive -CompressionLevel Optimal
Expand-Archive -LiteralPath $archive -DestinationPath $extractRoot -Force
foreach ($entry in $expectedHashes.GetEnumerator()) {
    $actual = (Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $extractRoot $entry.Key)).Hash
    if ($actual -ne $entry.Value) { throw "Windows 部署包哈希不匹配：$($entry.Key)" }
}

$archiveHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $archive).Hash
if ($ValidateOnly) {
    [pscustomobject]@{ Valid = $true; Commit = $shortCommit; Archive = $archive; ArchiveSha256 = $archiveHash; VerifiedFiles = $expectedHashes.Count; Transactional = $true } | ConvertTo-Json
    exit 0
}

. 'D:\Codex\.codex\credentials\ssh\replicator\office\Use-OfficeSshHelper.ps1' | Out-Null
$officeHost = ($env:REPLICATOR_OFFICE_SSH_DESTINATION -split '@')[-1]
& tailscale.exe ping --timeout 10s $officeHost
if ($LASTEXITCODE -ne 0) { throw "办公室设备的 Tailscale 连通性检查失败：$officeHost" }
$script:sshOptions = @(
    '-i', $env:REPLICATOR_OFFICE_SSH_IDENTITY_FILE,
    '-o', 'BatchMode=yes',
    '-o', 'IdentitiesOnly=yes',
    '-o', "ConnectTimeout=$env:REPLICATOR_OFFICE_SSH_CONNECT_TIMEOUT_SECONDS",
    '-o', "ConnectionAttempts=$env:REPLICATOR_OFFICE_SSH_CONNECTION_ATTEMPTS",
    '-o', 'StrictHostKeyChecking=yes',
    '-o', "UserKnownHostsFile=$env:REPLICATOR_OFFICE_SSH_USER_KNOWN_HOSTS_FILE",
    '-o', "KexAlgorithms=$env:REPLICATOR_OFFICE_SSH_KEX_ALGORITHMS"
)
$remoteRoot = "D:\Codex\deploy\clock-countdown-$shortCommit-$runId"
Invoke-OfficeEncodedCommand -ScriptText "`$ErrorActionPreference='Stop'; if (Test-Path -LiteralPath '$remoteRoot') { throw '远端暂存目录已存在。' }; New-Item -ItemType Directory -Path '$remoteRoot' -Force | Out-Null"
& scp.exe @script:sshOptions $archive "$($env:REPLICATOR_OFFICE_SSH_DESTINATION):D:/Codex/deploy/clock-countdown-$shortCommit-$runId/clock-countdown.zip"
if ($LASTEXITCODE -ne 0) { throw '无法上传办公室部署包。' }

$installedFiles = @('Countdown.App.psm1', 'Countdown.Core.psm1', 'Countdown.InstallTransaction.psm1', 'Countdown.Notes.psm1', 'Countdown.Runtime.psm1', 'Countdown.Settings.psm1', '敏敏子倒计时.ps1', 'CountdownLauncher.exe', 'countdown-icon.ico')
$remoteExpectedHashes = ($installedFiles | ForEach-Object { "  '$($_)' = '$($expectedHashes[$_])'" }) -join "`r`n"

$remoteInstall = @"
`$ErrorActionPreference = 'Stop'
`$ProgressPreference = 'SilentlyContinue'
[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new(`$false)
`$powerShell7 = Join-Path `$PSHOME 'pwsh.exe'
if (-not (Test-Path -LiteralPath `$powerShell7)) { throw '未找到当前 PowerShell 7 可执行文件。' }
`$archive = '$remoteRoot\clock-countdown.zip'
  `$source = '$remoteRoot\source'
  `$install = 'D:\敏敏子倒计时'
  `$taskName = 'Minmin Countdown'
  `$legacyTaskName = '敏敏子倒计时'
if ((Get-FileHash -Algorithm SHA256 -LiteralPath `$archive).Hash -ne '$archiveHash') { throw '部署包哈希不匹配。' }
`$installer = `$null
`$deferredReceipt = `$null
`$rollbackTransactionPath = `$null
`$rollbackPreservedUnmanagedLaunchers = `$false
`$transactionCommitted = `$false
`$installMutex = `$null
`$transactionModuleImported = `$false
try {
  Expand-Archive -LiteralPath `$archive -DestinationPath `$source -Force
  Import-Module (Join-Path `$source 'Countdown.InstallTransaction.psm1') -Force -DisableNameChecking
  `$transactionModuleImported = `$true
  `$interactiveContext = Get-CountdownInteractiveDesktopContext
  `$launcherPath = Join-Path ([string] `$interactiveContext.Desktop) '敏敏子倒计时.exe'
  `$legacyLauncherPath = Join-Path ([string] `$interactiveContext.Desktop) '敏敏子倒计时.cmd'
  `$launcherTempPath = "`$launcherPath.new"
  `$installMutex = Enter-CountdownInstallTransactionMutex -TimeoutSeconds 120
  `$deferredReceiptPath = Join-Path '$remoteRoot' 'deferred-install-receipt.json'
  `$settingsPath = Join-Path `$install 'settings.json'
  `$settingsJsonBefore = if (Test-Path -LiteralPath `$settingsPath) { Get-Content -LiteralPath `$settingsPath -Raw } else { `$null }
  `$settingsHashBefore = if (Test-Path -LiteralPath `$settingsPath) { (Get-FileHash -Algorithm SHA256 -LiteralPath `$settingsPath).Hash } else { `$null }
  # 与验收阶段共用同一 pwsh 进程和同一把 mutex；SSH/父进程退出不会留下仍在修改文件的脱离子安装器。
  `$installerOutput = & (Join-Path `$source 'Install-Countdown.ps1') -DeferCommit -DeferredReceiptPath `$deferredReceiptPath -ParentTransactionMutexHeld -ExpectedInteractiveUser ([string] `$interactiveContext.User) -ExpectedInteractiveSessionId ([int] `$interactiveContext.SessionId) -ExpectedDesktopPath ([string] `$interactiveContext.Desktop) | Out-String
  `$deferredReceipt = Read-CountdownDeferredInstallReceipt -Path `$deferredReceiptPath
  if (`$null -eq `$deferredReceipt) { throw '安装器没有写入可用于回滚的延迟提交回执。' }
  `$rollbackTransactionPath = [string] `$deferredReceipt.transactionPath
  `$installer = `$installerOutput | ConvertFrom-Json
  if (-not `$installer.DeferredCommit -or [string]::IsNullOrWhiteSpace([string] `$installer.TransactionPath)) { throw '安装器没有保留待验收事务。' }
  if (-not [string]::Equals([System.IO.Path]::GetFullPath([string] `$installer.TransactionPath), [System.IO.Path]::GetFullPath(`$rollbackTransactionPath), [System.StringComparison]::OrdinalIgnoreCase)) { throw '安装器输出与独立事务回执不一致。' }
  if (-not [string]::Equals([string] `$installer.LaunchToken, [string] `$deferredReceipt.launchToken, [System.StringComparison]::Ordinal)) { throw '安装器输出与独立事务回执的启动令牌不一致。' }
  if ([int] `$installer.InteractiveSessionId -ne [int] `$deferredReceipt.interactiveSessionId) { throw '安装器输出与独立事务回执的交互会话不一致。' }
  `$coreTests = `$installer.TestOutput | ConvertFrom-Json
  `$uiTests = `$installer.UiTestOutput | ConvertFrom-Json
  `$uiValidation = `$installer.UiValidationOutput | ConvertFrom-Json
  if (`$coreTests.Failed -ne 0 -or `$uiTests.Failed -ne 0 -or -not `$uiValidation.Valid) { throw '安装器内置校验未通过。' }
  `$expected = @{
$remoteExpectedHashes
  }
  foreach (`$entry in `$expected.GetEnumerator()) {
    if ((Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path `$install `$entry.Key)).Hash -ne `$entry.Value) { throw "安装后哈希不匹配：`$(`$entry.Key)" }
  }
  Import-Module (Join-Path `$install 'Countdown.Settings.psm1') -Force
  Import-Module (Join-Path `$source 'Countdown.Runtime.psm1') -Force
  `$validation = & `$powerShell7 -NoLogo -NoProfile -NonInteractive -STA -File (Join-Path `$install '敏敏子倒计时.ps1') -ValidateOnly | Out-String | ConvertFrom-Json
  `$settingsJsonAfter = if (Test-Path -LiteralPath `$settingsPath) { Get-Content -LiteralPath `$settingsPath -Raw } else { `$null }
  `$settingsHashAfter = if (Test-Path -LiteralPath `$settingsPath) { (Get-FileHash -Algorithm SHA256 -LiteralPath `$settingsPath).Hash } else { `$null }
  `$task = Get-ScheduledTask -TaskName `$taskName
  `$runtimeStatus = Wait-CountdownWindowReady -Path (Join-Path `$install 'runtime-status.json') -LaunchToken ([string] `$deferredReceipt.launchToken) -ExpectedSessionId ([int] `$deferredReceipt.interactiveSessionId) -ExpectedApplicationPath (Join-Path `$install '敏敏子倒计时.ps1') -TimeoutSeconds 5 -PollMilliseconds 250
  if (-not (Test-CountdownSettingsDeploymentTransition -BeforeHash `$settingsHashBefore -AfterHash `$settingsHashAfter -BeforeJson `$settingsJsonBefore -AfterJson `$settingsJsonAfter)) { throw '部署修改了 settings.json（非预期迁移）。' }
  if ([string]`$task.State -ne 'Running') { throw "计划任务未运行：`$(`$task.State)" }
  if (-not [string]::Equals([System.IO.Path]::GetFullPath([string]`$task.Actions.Execute), [System.IO.Path]::GetFullPath(`$powerShell7), [System.StringComparison]::OrdinalIgnoreCase)) { throw '计划任务未使用当前 PowerShell 7。' }
  if (-not (`$validation.FloatingWindowPresent -and `$validation.FloatingManagerButtonPresent)) { throw '悬浮框验证失败。' }
  [void] (Remove-CountdownStartupRequest -Path (Join-Path `$install 'startup-request.json') -LaunchToken ([string] `$deferredReceipt.launchToken))
  `$commitInteractiveContext = Get-CountdownInteractiveDesktopContext
  if (-not [string]::Equals([string] `$commitInteractiveContext.User, [string] `$interactiveContext.User, [System.StringComparison]::OrdinalIgnoreCase) -or
      [int] `$commitInteractiveContext.SessionId -ne [int] `$interactiveContext.SessionId -or
      -not [string]::Equals([string] `$commitInteractiveContext.Desktop, [string] `$interactiveContext.Desktop, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw '办公室验收提交前交互用户、会话或桌面路径已变化；拒绝提交事务。'
  }
  Complete-CountdownInstallTransaction -TransactionPath `$rollbackTransactionPath -ExpectedInstallPath `$install -ExpectedTaskName `$taskName -ExpectedLegacyTaskName `$legacyTaskName -ExpectedLauncherPath `$launcherPath -ExpectedLegacyLauncherPath `$legacyLauncherPath -ExpectedLauncherTempPath `$launcherTempPath
  `$transactionCommitted = `$true
  & "`$env:SystemRoot\System32\ie4uinit.exe" -show 2>`$null
  [pscustomobject]@{ CoreTests = `$coreTests.Passed; UiTests = `$uiTests.Passed; UiValid = `$uiValidation.Valid; TaskState = [string]`$task.State; RuntimeProcessId = `$runtimeStatus.processId; RuntimeStage = `$runtimeStatus.stage; CalendarState = `$runtimeStatus.calendarState } | ConvertTo-Json -Compress
}
catch {
  `$failure = `$_.Exception.Message
  if (`$transactionCommitted) { throw "部署已提交，但提交后收尾失败，未回滚：`$failure" }
  `$rollbackResult = `$null
  try {
    if (-not `$transactionCommitted -and `$transactionModuleImported) {
      if ([string]::IsNullOrWhiteSpace(`$rollbackTransactionPath)) {
        `$deferredReceipt = Read-CountdownDeferredInstallReceipt -Path (Join-Path '$remoteRoot' 'deferred-install-receipt.json')
        if (`$null -ne `$deferredReceipt) { `$rollbackTransactionPath = [string] `$deferredReceipt.transactionPath }
      }
      if (-not [string]::IsNullOrWhiteSpace(`$rollbackTransactionPath) -and (Test-Path -LiteralPath (Join-Path `$rollbackTransactionPath 'journal.json') -PathType Leaf)) {
        `$rollbackResult = Undo-CountdownInstallTransaction -TransactionPath `$rollbackTransactionPath -PowerShell7Path `$powerShell7 -ExpectedInstallPath `$install -ExpectedTaskName `$taskName -ExpectedLegacyTaskName `$legacyTaskName -ExpectedLauncherPath `$launcherPath -ExpectedLegacyLauncherPath `$legacyLauncherPath -ExpectedLauncherTempPath `$launcherTempPath -PreserveUnmanagedLaunchers
      }
      elseif ([string]::IsNullOrWhiteSpace(`$rollbackTransactionPath)) {
        # 子安装器在写出 stdout 前异常中断时，仍在同一把锁下恢复其遗留 journal。
        `$recoveryResult = Recover-CountdownInstallTransactions -InstallPath `$install -PowerShell7Path `$powerShell7 -ExpectedTaskName `$taskName -ExpectedLegacyTaskName `$legacyTaskName -ExpectedLauncherPath `$launcherPath -ExpectedLegacyLauncherPath `$legacyLauncherPath -ExpectedLauncherTempPath `$launcherTempPath
        if (@(`$recoveryResult.UnresolvedLauncherOwnershipConflicts).Count -gt 0) { `$rollbackPreservedUnmanagedLaunchers = `$true }
      }
      else {
        throw '无法验证待回滚事务的 journal；拒绝声称已回滚。'
      }
    }
  }
  catch { throw "部署失败：`$failure；回滚也失败：`$(`$_.Exception.Message)" }
  if (`$rollbackPreservedUnmanagedLaunchers -or (`$null -ne `$rollbackResult -and [bool] `$rollbackResult.PreservedUnmanagedLaunchers)) {
    throw "部署失败，已恢复应用、任务和时间服务；为保护用户替换的桌面启动器，保留了事务证据：`$failure"
  }
  throw "部署失败，已回滚：`$failure"
}
finally {
  if (`$null -ne `$installMutex) { Exit-CountdownInstallTransactionMutex -Mutex `$installMutex }
}
"@
Invoke-OfficeEncodedCommand -ScriptText $remoteInstall
