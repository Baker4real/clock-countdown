#requires -Version 7.0

[CmdletBinding()]
param(
    [switch] $ValidateOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = $PSScriptRoot

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

$buildScript = Join-Path $repoRoot 'Build-CountdownApplication.ps1'
$powerShell7 = (Get-Command pwsh.exe -CommandType Application -ErrorAction Stop | Select-Object -First 1 -ExpandProperty Source)
& $powerShell7 -NoLogo -NoProfile -NonInteractive -File $buildScript -Clean
if ($LASTEXITCODE -ne 0) { throw '无法从当前 main 提交重建 C# 部署产物。' }

Import-Module (Join-Path $repoRoot 'Countdown.ReleaseContract.psm1') -Force
$releaseContract = Get-CountdownReleaseContract
$requiredFiles = @($releaseContract.PackageFiles)
$packageValidation = Assert-CountdownReleasePackage -Root $repoRoot

$shortCommit = $commit.Substring(0, 7)
$runId = "$(Get-Date -Format 'yyyyMMddHHmmssfff')-$([guid]::NewGuid().ToString('N').Substring(0, 8))"
$evidenceRoot = "D:\Codex\.codex\run-evidence\clock-countdown-deploy\$shortCommit\$runId"
$packageRoot = Join-Path $evidenceRoot 'windows-package'
$extractRoot = Join-Path $evidenceRoot 'package-check'
$archive = Join-Path $evidenceRoot 'clock-countdown-windows.zip'
New-Item -ItemType Directory -Path $packageRoot -Force | Out-Null
foreach ($file in $requiredFiles) {
    $destination = Join-Path $packageRoot $file
    New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $repoRoot $file) -Destination $destination -Force
}
Compress-Archive -Path (Join-Path $packageRoot '*') -DestinationPath $archive -CompressionLevel Optimal
Expand-Archive -LiteralPath $archive -DestinationPath $extractRoot -Force
[void] (Assert-CountdownReleasePackage -Root $extractRoot)

$archiveHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $archive).Hash
if ($ValidateOnly) {
    [pscustomobject]@{ Valid = $true; Commit = $shortCommit; Archive = $archive; ArchiveSha256 = $archiveHash; VerifiedFiles = $packageValidation.VerifiedFiles; Transactional = $true } | ConvertTo-Json
    exit 0
}

. 'D:\Codex\.codex\credentials\ssh\replicator\office\Use-OfficeSshHelper.ps1' | Out-Null
$officeHost = ($env:REPLICATOR_OFFICE_SSH_DESTINATION -split '@')[-1]
& tailscale.exe ping --until-direct=false --c 3 --timeout 10s $officeHost
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
`$rollbackPreservedCommittedTransaction = `$false
`$transactionCommitted = `$false
`$installMutex = `$null
`$transactionModuleImported = `$false
try {
  Expand-Archive -LiteralPath `$archive -DestinationPath `$source -Force
  Import-Module (Join-Path `$source 'Countdown.ReleaseContract.psm1') -Force
  `$remoteReleaseContract = Get-CountdownReleaseContract
  [void] (Assert-CountdownReleasePackage -Root `$source)
  Import-Module (Join-Path `$source 'Countdown.InstallTransaction.psm1') -Force -DisableNameChecking
  `$transactionModuleImported = `$true
  `$interactiveContext = Get-CountdownInteractiveDesktopContext
  `$launcherPath = Join-Path ([string] `$interactiveContext.Desktop) '敏敏子倒计时.exe'
  `$legacyLauncherPath = Join-Path ([string] `$interactiveContext.Desktop) '敏敏子倒计时.cmd'
  `$launcherTempPath = "`$launcherPath.new"
  `$installMutex = Enter-CountdownInstallTransactionMutex -TimeoutSeconds 120
  `$deferredReceiptPath = Join-Path '$remoteRoot' 'deferred-install-receipt.json'
  `$settingsPath = Join-Path `$install 'settings.json'
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
  # 远端测试与校时期间旧应用仍可保存用户操作；设置基线必须取自旧进程静止后的事务快照。
  `$settingsSnapshotPath = Join-Path `$rollbackTransactionPath 'install-backup\settings.json'
  `$coreTests = `$installer.TestOutput | ConvertFrom-Json
  `$cSharpTests = `$installer.CSharpTestOutput | ConvertFrom-Json
  `$uiTests = `$installer.UiTestOutput | ConvertFrom-Json
  `$uiValidation = `$installer.UiValidationOutput | ConvertFrom-Json
  if (`$coreTests.Failed -ne 0 -or `$cSharpTests.Failed -ne 0 -or `$uiTests.Failed -ne 0 -or -not `$uiValidation.Valid) { throw '安装器内置校验未通过。' }
  foreach (`$file in `$remoteReleaseContract.InstalledFiles) {
    `$expectedHash = [string] `$remoteReleaseContract.IntegrityHashes[`$file]
    if ((Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path `$install `$file)).Hash -ne `$expectedHash) { throw "安装后哈希不匹配：`$file" }
  }
  foreach (`$file in `$remoteReleaseContract.RetiredRuntimeFiles) {
    if (Test-Path -LiteralPath (Join-Path `$install `$file)) { throw "安装目录仍包含已淘汰悬浮窗文件：`$file" }
  }
  Import-Module (Join-Path `$install 'Countdown.Settings.psm1') -Force
  Import-Module (Join-Path `$source 'Countdown.Runtime.psm1') -Force
  Import-Module (Join-Path `$source 'Countdown.TransactionAcceptance.psm1') -Force
  `$validation = & `$powerShell7 -NoLogo -NoProfile -NonInteractive -STA -File (Join-Path `$install '敏敏子倒计时.ps1') -ValidateOnly | Out-String | ConvertFrom-Json
  `$task = Get-ScheduledTask -TaskName `$taskName
  `$runtimeStatus = Wait-CountdownWindowReady -Path (Join-Path `$install 'runtime-status.json') -LaunchToken ([string] `$deferredReceipt.launchToken) -ExpectedSessionId ([int] `$deferredReceipt.interactiveSessionId) -ExpectedApplicationPath (Join-Path `$install '敏敏子倒计时.ps1') -TimeoutSeconds 5 -PollMilliseconds 250
  if ([string]`$task.State -ne 'Running') { throw "计划任务未运行：`$(`$task.State)" }
  if (-not [string]::Equals([System.IO.Path]::GetFullPath([string]`$task.Actions.Execute), [System.IO.Path]::GetFullPath(`$powerShell7), [System.StringComparison]::OrdinalIgnoreCase)) { throw '计划任务未使用当前 PowerShell 7。' }
  if (-not (`$validation.FloatingWindowPresent -and `$validation.FloatingManagerButtonPresent)) { throw '悬浮框验证失败。' }
  `$expectedScheduledTaskIdentity = [string] (Export-ScheduledTask -InputObject `$task -ErrorAction Stop)
  `$getAcceptanceInteractiveContext = { Get-CountdownInteractiveDesktopContext }
  `$getAcceptanceScheduledTask = {
    param([string] `$ExpectedTaskName)
    Get-ScheduledTask -TaskName `$ExpectedTaskName -ErrorAction Stop
  }
  `$getAcceptanceScheduledTaskIdentity = {
    param([object] `$ScheduledTask)
    [string] (Export-ScheduledTask -InputObject `$ScheduledTask -ErrorAction Stop)
  }
  `$completeAcceptanceTransaction = {
    Complete-CountdownInstallTransaction -TransactionPath `$rollbackTransactionPath -ExpectedInstallPath `$install -ExpectedTaskName `$taskName -ExpectedLegacyTaskName `$legacyTaskName -ExpectedLauncherPath `$launcherPath -ExpectedLegacyLauncherPath `$legacyLauncherPath -ExpectedLauncherTempPath `$launcherTempPath
  }
  `$removeAcceptedTransaction = {
    Remove-CountdownCommittedInstallTransaction -TransactionPath `$rollbackTransactionPath -ExpectedInstallPath `$install -ExpectedTaskName `$taskName -ExpectedLegacyTaskName `$legacyTaskName -ExpectedLauncherPath `$launcherPath -ExpectedLegacyLauncherPath `$legacyLauncherPath -ExpectedLauncherTempPath `$launcherTempPath
  }
  `$transactionAcceptanceArguments = @{
    StartupRequestPath = Join-Path `$install 'startup-request.json'
    RuntimeStatusPath = Join-Path `$install 'runtime-status.json'
    AcceptanceStatusPath = Join-Path `$install 'acceptance-status.json'
    LaunchToken = [string] `$deferredReceipt.launchToken
    ExpectedInteractiveContext = `$interactiveContext
    ExpectedProcessId = [int] `$runtimeStatus.processId
    ExpectedSessionId = [int] `$deferredReceipt.interactiveSessionId
    ExpectedApplicationPath = Join-Path `$install '敏敏子倒计时.ps1'
    TaskName = `$taskName
    ExpectedScheduledTaskIdentity = `$expectedScheduledTaskIdentity
    SettingsSnapshotPath = `$settingsSnapshotPath
    SettingsPath = `$settingsPath
    GetInteractiveContext = `$getAcceptanceInteractiveContext
    GetScheduledTask = `$getAcceptanceScheduledTask
    GetScheduledTaskIdentity = `$getAcceptanceScheduledTaskIdentity
    CompleteTransaction = `$completeAcceptanceTransaction
    RemoveCommittedTransaction = `$removeAcceptedTransaction
  }
  `$acceptanceResult = Invoke-CountdownTransactionAcceptance @transactionAcceptanceArguments
  `$transactionCommitted = `$true
  & "`$env:SystemRoot\System32\ie4uinit.exe" -show 2>`$null
  [pscustomobject]@{ CoreTests = `$coreTests.Passed; CSharpTests = `$cSharpTests.Passed; UiTests = `$uiTests.Passed; UiValid = `$uiValidation.Valid; AdapterUiValidated = `$cSharpTests.AdapterUiValidated; TaskState = [string]`$acceptanceResult.TaskState; RuntimeProcessId = `$acceptanceResult.RuntimeProcessId; RuntimeStage = `$runtimeStatus.stage; CalendarState = `$runtimeStatus.calendarState; AcceptanceState = `$acceptanceResult.AcceptanceState; SettingsHashBefore = `$acceptanceResult.SettingsHashBefore; SettingsHashAfter = `$acceptanceResult.SettingsHashAfter } | ConvertTo-Json -Compress
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
        if (@(`$recoveryResult.UnresolvedCommittedTransactions).Count -gt 0) { `$rollbackPreservedCommittedTransaction = `$true }
      }
      else {
        throw '无法验证待回滚事务的 journal；拒绝声称已回滚。'
      }
    }
  }
  catch { throw "部署失败：`$failure；回滚也失败：`$(`$_.Exception.Message)" }
  if (`$rollbackPreservedCommittedTransaction) {
    throw "部署失败；检测到已提交但未完成解锁确认的事务，未声称回滚并保留了证据：`$failure"
  }
  if (`$rollbackPreservedUnmanagedLaunchers -or (`$null -ne `$rollbackResult -and [bool] `$rollbackResult.PreservedUnmanagedLaunchers)) {
    throw "部署失败，已恢复应用、任务和时间服务；为保护用户替换的桌面启动器，保留了事务证据：`$failure"
  }
  throw "部署失败，已回滚：`$failure"
}
finally {
  if (`$null -ne `$installMutex) { Exit-CountdownInstallTransactionMutex -Mutex `$installMutex }
}
"@
$remoteInstallScript = Join-Path $evidenceRoot 'remote-install.ps1'
[System.IO.File]::WriteAllText(
    $remoteInstallScript,
    $remoteInstall,
    [System.Text.UTF8Encoding]::new($false)
)
$remoteInstallHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $remoteInstallScript).Hash
$remoteInstallDestination = "D:/Codex/deploy/clock-countdown-$shortCommit-$runId/remote-install.ps1"
& scp.exe @script:sshOptions $remoteInstallScript "$($env:REPLICATOR_OFFICE_SSH_DESTINATION):$remoteInstallDestination"
if ($LASTEXITCODE -ne 0) { throw '无法上传办公室远端安装脚本。' }
$remoteInstallPath = "$remoteRoot\remote-install.ps1"
Invoke-OfficeEncodedCommand -ScriptText "if ((Get-FileHash -Algorithm SHA256 -LiteralPath '$remoteInstallPath').Hash -ne '$remoteInstallHash') { throw '远端安装脚本哈希不匹配。' }; & (Join-Path `$PSHOME 'pwsh.exe') -NoLogo -NoProfile -NonInteractive -File '$remoteInstallPath'; if (`$LASTEXITCODE -ne 0) { exit `$LASTEXITCODE }"
