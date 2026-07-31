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

function Get-OfficeRemoteFileLength {
    param([Parameter(Mandatory)] [string] $RemotePath)

    $escapedRemotePath = $RemotePath.Replace("'", "''")
    $runtimeGuard = "`$ErrorActionPreference = 'Stop'; if (`$PSVersionTable.PSVersion.Major -lt 7 -or `$PSVersionTable.PSEdition -ne 'Core') { throw '办公室设备必须使用 PowerShell 7+（pwsh.exe）。' }"
    $probeScript = @"
$runtimeGuard
if (-not (Test-Path -LiteralPath '$escapedRemotePath' -PathType Leaf)) {
  [Console]::Out.Write('0')
  exit 0
}
[Console]::Out.Write(([long] (Get-Item -LiteralPath '$escapedRemotePath').Length).ToString([Globalization.CultureInfo]::InvariantCulture))
"@
    $encoded = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($probeScript))
    try {
        $output = & ssh.exe @script:sshOptions $env:REPLICATOR_OFFICE_SSH_DESTINATION "pwsh.exe -NoLogo -NoProfile -EncodedCommand $encoded"
        $exitCode = $LASTEXITCODE
    }
    catch {
        return $null
    }
    if ($exitCode -ne 0) { return $null }

    $text = ($output | Out-String).Trim()
    $bytes = 0L
    if (-not [long]::TryParse($text, [ref] $bytes)) { return $null }
    $bytes
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
$transferLedgerPath = Join-Path $evidenceRoot 'transfer-attempts.json'
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

Import-Module (Join-Path $repoRoot 'Countdown.TransferLedger.psm1') -Force
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
$remoteBytesProbe = {
    param([string] $RemotePath)
    Get-OfficeRemoteFileLength -RemotePath $RemotePath
}.GetNewClosure()
Invoke-OfficeEncodedCommand -ScriptText "`$ErrorActionPreference='Stop'; if (Test-Path -LiteralPath '$remoteRoot') { throw '远端暂存目录已存在。' }; New-Item -ItemType Directory -Path '$remoteRoot' -Force | Out-Null"
$archiveRemotePath = "$remoteRoot\clock-countdown.zip"
Invoke-CountdownRecordedScpTransfer `
    -LedgerPath $transferLedgerPath `
    -Id 'deployment-package' `
    -SourcePath $archive `
    -Destination "$($env:REPLICATOR_OFFICE_SSH_DESTINATION):D:/Codex/deploy/clock-countdown-$shortCommit-$runId/clock-countdown.zip" `
    -RemotePath $archiveRemotePath `
    -ScpOptions $script:sshOptions `
    -RemoteBytesProbe $remoteBytesProbe | Out-Null

$remoteInstall = @"
`$ErrorActionPreference = 'Stop'
`$ProgressPreference = 'SilentlyContinue'
[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new(`$false)
`$powerShell7 = Join-Path `$PSHOME 'pwsh.exe'
if (-not (Test-Path -LiteralPath `$powerShell7 -PathType Leaf)) { throw '未找到当前 PowerShell 7 可执行文件。' }
`$archive = '$remoteRoot\clock-countdown.zip'
`$source = '$remoteRoot\source'
if ((Get-FileHash -Algorithm SHA256 -LiteralPath `$archive).Hash -ne '$archiveHash') { throw '部署包哈希不匹配。' }
Expand-Archive -LiteralPath `$archive -DestinationPath `$source -Force
Import-Module (Join-Path `$source 'Countdown.ReleaseContract.psm1') -Force
[void] (Assert-CountdownReleasePackage -Root `$source)
Import-Module (Join-Path `$source 'Countdown.RemoteDeployment.psm1') -Force
`$deploymentArguments = @{
  SourceRoot = `$source
  InstallPath = 'D:\敏敏子倒计时'
  PowerShell7Path = `$powerShell7
  DeferredReceiptPath = Join-Path '$remoteRoot' 'deferred-install-receipt.json'
  TaskName = 'Minmin Countdown'
  LegacyTaskName = '敏敏子倒计时'
}
Invoke-CountdownRemoteDeployment @deploymentArguments | ConvertTo-Json -Compress
"@
$remoteInstallScript = Join-Path $evidenceRoot 'remote-install.ps1'
[System.IO.File]::WriteAllText(
    $remoteInstallScript,
    $remoteInstall,
    [System.Text.UTF8Encoding]::new($false)
)
$remoteInstallHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $remoteInstallScript).Hash
$remoteInstallDestination = "D:/Codex/deploy/clock-countdown-$shortCommit-$runId/remote-install.ps1"
$remoteInstallPath = "$remoteRoot\remote-install.ps1"
Invoke-CountdownRecordedScpTransfer `
    -LedgerPath $transferLedgerPath `
    -Id 'remote-install-script' `
    -SourcePath $remoteInstallScript `
    -Destination "$($env:REPLICATOR_OFFICE_SSH_DESTINATION):$remoteInstallDestination" `
    -RemotePath $remoteInstallPath `
    -ScpOptions $script:sshOptions `
    -RemoteBytesProbe $remoteBytesProbe | Out-Null
Invoke-OfficeEncodedCommand -ScriptText "if ((Get-FileHash -Algorithm SHA256 -LiteralPath '$remoteInstallPath').Hash -ne '$remoteInstallHash') { throw '远端安装脚本哈希不匹配。' }; & (Join-Path `$PSHOME 'pwsh.exe') -NoLogo -NoProfile -NonInteractive -File '$remoteInstallPath'; if (`$LASTEXITCODE -ne 0) { exit `$LASTEXITCODE }"
