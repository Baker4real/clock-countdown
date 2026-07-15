[CmdletBinding()]
param(
    [switch] $ValidateOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = $PSScriptRoot
$requiredFiles = @(
    'Countdown.Core.psm1',
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
    if ($hashes.Count -ne 6) { throw "无法从安装器读取完整性清单，实际读取到 $($hashes.Count) 项。" }
    $hashes
}

function Invoke-OfficeEncodedCommand {
    param([Parameter(Mandatory)] [string] $ScriptText)

    $encoded = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($ScriptText))
    & ssh.exe @script:sshOptions $env:REPLICATOR_OFFICE_SSH_DESTINATION "pwsh.exe -NoLogo -NoProfile -EncodedCommand $encoded"
    if ($LASTEXITCODE -ne 0) { throw '办公室设备上的远程命令失败。' }
}

if ((git -C $repoRoot status --porcelain).Count -ne 0) { throw '工作树不干净，拒绝部署未提交内容。' }
if ((git -C $repoRoot branch --show-current).Trim() -ne 'main') { throw '仅允许从 main 分支部署。' }
git -C $repoRoot fetch origin main
if ($LASTEXITCODE -ne 0) { throw '无法同步 origin/main。' }
$commit = (git -C $repoRoot rev-parse HEAD).Trim()
if ($commit -ne (git -C $repoRoot rev-parse origin/main).Trim()) { throw '本地 HEAD 与 origin/main 不一致，拒绝部署。' }

$installerPath = Join-Path $repoRoot 'Install-Countdown.ps1'
$expectedHashes = Get-InstallerHashes -InstallerPath $installerPath
foreach ($file in $requiredFiles) {
    if (-not (Test-Path -LiteralPath (Join-Path $repoRoot $file))) { throw "缺少部署文件：$file" }
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
    [pscustomobject]@{ Valid = $true; Commit = $shortCommit; Archive = $archive; ArchiveSha256 = $archiveHash; VerifiedFiles = $expectedHashes.Count } | ConvertTo-Json
    exit 0
}

. 'D:\Codex\.codex\credentials\ssh\replicator\office\Use-OfficeSshHelper.ps1' | Out-Null
$script:sshOptions = @(
    '-i', $env:REPLICATOR_OFFICE_SSH_IDENTITY_FILE,
    '-o', 'BatchMode=yes',
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

$installedFiles = @('Countdown.Core.psm1', '敏敏子倒计时.ps1', 'CountdownLauncher.exe', 'countdown-icon.ico')
$remoteExpectedHashes = ($installedFiles | ForEach-Object { "  '$($_)' = '$($expectedHashes[$_])'" }) -join "`r`n"

$remoteInstall = @"
`$ErrorActionPreference = 'Stop'
`$ProgressPreference = 'SilentlyContinue'
[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new(`$false)
`$archive = '$remoteRoot\clock-countdown.zip'
`$source = '$remoteRoot\source'
`$install = 'D:\敏敏子倒计时'
`$backupInstall = '$remoteRoot\backup-install'
`$backupTaskXml = '$remoteRoot\minmin-countdown-task.xml'
`$priorTask = Get-ScheduledTask -TaskName 'Minmin Countdown' -ErrorAction SilentlyContinue
`$priorTaskState = if (`$null -ne `$priorTask) { [string]`$priorTask.State } else { `$null }
`$hadInstall = Test-Path -LiteralPath `$install
if (`$hadInstall) {
  New-Item -ItemType Directory -Path `$backupInstall -Force | Out-Null
  Copy-Item -Path (Join-Path `$install '*') -Destination `$backupInstall -Recurse -Force
}
if (`$null -ne `$priorTask) { [System.IO.File]::WriteAllText(`$backupTaskXml, (Export-ScheduledTask -TaskName 'Minmin Countdown'), [System.Text.UTF8Encoding]::new(`$false)) }
if ((Get-FileHash -Algorithm SHA256 -LiteralPath `$archive).Hash -ne '$archiveHash') { throw '部署包哈希不匹配。' }
try {
  Expand-Archive -LiteralPath `$archive -DestinationPath `$source -Force
  `$settingsPath = Join-Path `$install 'settings.json'
  `$settingsHashBefore = if (Test-Path -LiteralPath `$settingsPath) { (Get-FileHash -Algorithm SHA256 -LiteralPath `$settingsPath).Hash } else { `$null }
  `$installer = & (Join-Path `$source 'Install-Countdown.ps1') | Out-String | ConvertFrom-Json
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
  `$validation = & pwsh.exe -NoLogo -NoProfile -NonInteractive -STA -File (Join-Path `$install '敏敏子倒计时.ps1') -ValidateOnly | Out-String | ConvertFrom-Json
  `$settingsHashAfter = if (Test-Path -LiteralPath `$settingsPath) { (Get-FileHash -Algorithm SHA256 -LiteralPath `$settingsPath).Hash } else { `$null }
  `$task = Get-ScheduledTask -TaskName 'Minmin Countdown'
  `$status = Get-Content -LiteralPath (Join-Path `$install 'runtime-status.json') -Raw | ConvertFrom-Json
  `$processes = @(Get-CimInstance Win32_Process -Filter "Name='pwsh.exe'" | Where-Object { `$_.CommandLine -like '*敏敏子倒计时.ps1*' })
  if (`$settingsHashBefore -ne `$settingsHashAfter) { throw '部署改动了 settings.json。' }
  if ([string]`$task.State -ne 'Running') { throw "计划任务未运行：`$(`$task.State)" }
  if (`$status.processId -notin @(`$processes.ProcessId)) { throw 'runtime-status.json 未匹配正在运行的应用进程。' }
  if (-not (`$validation.FloatingWindowPresent -and `$validation.FloatingManagerButtonPresent)) { throw '悬浮框验证失败。' }
  [pscustomobject]@{ CoreTests = `$coreTests.Passed; UiTests = `$uiTests.Passed; UiValid = `$uiValidation.Valid; TaskState = [string]`$task.State; RuntimeProcessId = `$status.processId } | ConvertTo-Json -Compress
}
catch {
  `$failure = `$_.Exception.Message
  try {
    `$currentTask = Get-ScheduledTask -TaskName 'Minmin Countdown' -ErrorAction SilentlyContinue
    if (`$null -ne `$currentTask -and `$currentTask.State -eq 'Running') { Stop-ScheduledTask -TaskName 'Minmin Countdown' }
    if (`$hadInstall) {
      if (Test-Path -LiteralPath `$install) { Remove-Item -LiteralPath `$install -Recurse -Force }
      New-Item -ItemType Directory -Path `$install -Force | Out-Null
      Copy-Item -Path (Join-Path `$backupInstall '*') -Destination `$install -Recurse -Force
    }
    elseif (Test-Path -LiteralPath `$install) { Remove-Item -LiteralPath `$install -Recurse -Force }
    if (Test-Path -LiteralPath `$backupTaskXml) {
      Unregister-ScheduledTask -TaskName 'Minmin Countdown' -Confirm:`$false -ErrorAction SilentlyContinue
      Register-ScheduledTask -TaskName 'Minmin Countdown' -Xml (Get-Content -LiteralPath `$backupTaskXml -Raw) -Force | Out-Null
      if (`$priorTaskState -eq 'Running') { Start-ScheduledTask -TaskName 'Minmin Countdown' }
    }
    elseif (`$null -eq `$priorTask) { Unregister-ScheduledTask -TaskName 'Minmin Countdown' -Confirm:`$false -ErrorAction SilentlyContinue }
  }
  catch { throw "部署失败：`$failure；回滚也失败：`$(`$_.Exception.Message)" }
  throw "部署失败，已回滚：`$failure"
}
"@
Invoke-OfficeEncodedCommand -ScriptText $remoteInstall
