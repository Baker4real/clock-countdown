#requires -Version 7.0

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$script:CountdownInstallMutexName = 'Global\MinminCountdownInstallTransaction-v1'

function ConvertTo-CountdownInstallFullPath {
    param([Parameter(Mandatory)] [string] $Path)

    [System.IO.Path]::GetFullPath($Path)
}

function Assert-CountdownInstallPathOnDDrive {
    param([Parameter(Mandatory)] [string] $Path)

    $resolved = ConvertTo-CountdownInstallFullPath -Path $Path
    if (-not $resolved.StartsWith('D:\', [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "安装事务只允许操作 D 盘路径：$resolved"
    }
    $resolved
}

function Get-CountdownRegistryEnvironmentValues {
    param([Parameter(Mandatory)] [string[]] $RegistryPaths)

    $values = @{}
    foreach ($registryPath in $RegistryPaths) {
        try {
            $item = Get-ItemProperty -LiteralPath $registryPath -ErrorAction Stop
        }
        catch {
            continue
        }
        foreach ($property in $item.PSObject.Properties) {
            if ($property.Name -like 'PS*' -or $null -eq $property.Value) { continue }
            $values[$property.Name] = [string] $property.Value
        }
    }
    $values
}

function Resolve-CountdownSidEnvironmentTemplate {
    param(
        [Parameter(Mandatory)] [string] $Template,
        [Parameter(Mandatory)] [hashtable] $Variables,
        [Parameter(Mandatory)] [string] $Label
    )

    $resolved = $Template
    for ($iteration = 0; $iteration -lt 16; $iteration++) {
        if ($resolved -notmatch '%[^%]+%') { return $resolved }
        $next = [regex]::Replace($resolved, '%(?<name>[^%]+)%', [System.Text.RegularExpressions.MatchEvaluator] {
                param($match)
                $name = $match.Groups['name'].Value
                if ($Variables.ContainsKey($name)) { return [string] $Variables[$name] }
                $match.Value
            })
        if ([string]::Equals($next, $resolved, [System.StringComparison]::Ordinal)) { break }
        $resolved = $next
    }
    if ($resolved -match '%[^%]+%') {
        throw "无法按交互用户环境解析 $Label：$Template"
    }
    $resolved
}

function Resolve-CountdownInteractiveDesktopPath {
    param(
        [Parameter(Mandatory)] [string] $ProfileImagePath,
        [Parameter(Mandatory)] [string] $DesktopTemplate,
        [Parameter(Mandatory)] [string] $UserName,
        [Parameter(Mandatory)] [hashtable] $EnvironmentVariables
    )

    # 绝不回落到 SSH/pwsh 进程的环境：User Shell Folders 必须只按 Explorer 所属 SID 的注册表环境展开。
    $knownVariables = @{}
    foreach ($entry in $EnvironmentVariables.GetEnumerator()) {
        if (-not [string]::IsNullOrWhiteSpace([string] $entry.Key) -and $null -ne $entry.Value) {
            $knownVariables[[string] $entry.Key] = [string] $entry.Value
        }
    }
    $profilePath = Resolve-CountdownSidEnvironmentTemplate -Template $ProfileImagePath -Variables $knownVariables -Label 'ProfileImagePath'
    if ([string]::IsNullOrWhiteSpace($profilePath) -or -not [System.IO.Path]::IsPathRooted($profilePath)) {
        throw "交互用户的 ProfileImagePath 无效：$ProfileImagePath"
    }
    $profilePath = [System.IO.Path]::GetFullPath($profilePath)
    $profileRoot = [System.IO.Path]::GetPathRoot($profilePath)
    if ([string]::IsNullOrWhiteSpace($profileRoot)) {
        throw "交互用户的 ProfileImagePath 无效：$profilePath"
    }
    $homeDrive = $profileRoot.TrimEnd('\\')
    $knownVariables['USERPROFILE'] = $profilePath
    $knownVariables['HOMEDRIVE'] = $homeDrive
    $knownVariables['HOMEPATH'] = $profilePath.Substring($homeDrive.Length)
    $knownVariables['USERNAME'] = $UserName
    $desktopPath = Resolve-CountdownSidEnvironmentTemplate -Template $DesktopTemplate -Variables $knownVariables -Label 'Desktop'
    if ([string]::IsNullOrWhiteSpace($desktopPath) -or -not [System.IO.Path]::IsPathRooted($desktopPath)) {
        throw "无法解析交互用户的桌面路径：$DesktopTemplate"
    }
    [System.IO.Path]::GetFullPath($desktopPath)
}

function Get-CountdownSingleInteractiveExplorerContext {
    param(
        [Parameter(Mandatory)] [object[]] $ExplorerProcesses,
        [Parameter(Mandatory)] [scriptblock] $OwnerResolver
    )

    # Explorer 可以在同一桌面会话中运行多个进程（例如单独进程打开文件夹窗口）。
    # 只有存在多个不同的会话或用户时，才无法确定安装应落到哪个桌面。
    $contexts = foreach ($process in @($ExplorerProcesses | Where-Object { [int] $_.SessionId -gt 0 })) {
        $owner = & $OwnerResolver $process
        if ($null -eq $owner -or $owner.ReturnValue -ne 0) {
            $returnValue = if ($null -eq $owner) { 'null' } else { [string] $owner.ReturnValue }
            throw "Could not resolve Explorer owner, return value $returnValue."
        }
        if ([string]::IsNullOrWhiteSpace([string] $owner.Domain) -or [string]::IsNullOrWhiteSpace([string] $owner.User)) {
            throw 'Could not resolve a complete Explorer owner identity.'
        }
        [pscustomobject]@{
            Process = $process
            Owner = $owner
            SessionId = [int] $process.SessionId
            User = "$($owner.Domain)\$($owner.User)"
        }
    }
    $groups = @($contexts | Group-Object -Property { "$($_.SessionId)|$($_.User)" })
    if ($groups.Count -ne 1) {
        throw "Expected exactly one interactive Explorer session and user, found $($groups.Count)."
    }
    $groups[0].Group[0]
}

function Get-CountdownInteractiveDesktopContext {
    $explorerContext = Get-CountdownSingleInteractiveExplorerContext -ExplorerProcesses @(Get-CimInstance Win32_Process -Filter "Name='explorer.exe'") -OwnerResolver {
        param($process)
        Invoke-CimMethod -InputObject $process -MethodName GetOwner
    }
    $explorer = $explorerContext.Process
    $owner = $explorerContext.Owner
    $interactiveUser = [string] $explorerContext.User
    $sid = (New-Object System.Security.Principal.NTAccount($interactiveUser)).Translate([System.Security.Principal.SecurityIdentifier]).Value
    $profile = Get-ItemProperty -LiteralPath "Registry::HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList\$sid" -ErrorAction Stop
    $userShellFolders = Get-ItemProperty -LiteralPath "Registry::HKEY_USERS\$sid\Software\Microsoft\Windows\CurrentVersion\Explorer\User Shell Folders" -ErrorAction Stop
    $environmentValues = Get-CountdownRegistryEnvironmentValues -RegistryPaths @(
        'Registry::HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\Session Manager\Environment',
        "Registry::HKEY_USERS\$sid\Environment",
        "Registry::HKEY_USERS\$sid\Volatile Environment"
    )
    # SystemDrive/SystemRoot 是进程环境常见但不一定落在 Session Manager\\Environment 的值；
    # 从系统注册表本身补齐，仍不读取 SSH 身份的环境。
    $windowsCurrentVersion = Get-ItemProperty -LiteralPath 'Registry::HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion' -ErrorAction Stop
    $systemRoot = [string] $windowsCurrentVersion.SystemRoot
    if (-not [string]::IsNullOrWhiteSpace($systemRoot) -and [System.IO.Path]::IsPathRooted($systemRoot)) {
        $environmentValues['SystemRoot'] = $systemRoot
        $environmentValues['SystemDrive'] = ([System.IO.Path]::GetPathRoot($systemRoot)).TrimEnd('\')
    }
    $desktop = Resolve-CountdownInteractiveDesktopPath -ProfileImagePath ([string] $profile.ProfileImagePath) -DesktopTemplate ([string] $userShellFolders.Desktop) -UserName ([string] $owner.User) -EnvironmentVariables $environmentValues
    $desktop = Assert-CountdownInstallPathOnDDrive -Path $desktop
    if (-not (Test-Path -LiteralPath $desktop -PathType Container)) {
        throw "Interactive user's desktop does not exist: $desktop"
    }
    [pscustomobject]@{
        User = $interactiveUser
        Sid = $sid
        SessionId = [int] $explorer[0].SessionId
        Desktop = $desktop
    }
}

function Test-CountdownInstallChildPath {
    param(
        [Parameter(Mandatory)] [string] $Path,
        [Parameter(Mandatory)] [string] $ParentPath
    )

    $child = ConvertTo-CountdownInstallFullPath -Path $Path
    $parent = (ConvertTo-CountdownInstallFullPath -Path $ParentPath).TrimEnd('\') + '\'
    $child.StartsWith($parent, [System.StringComparison]::OrdinalIgnoreCase)
}

function Get-CountdownTransactionBasePath {
    param([Parameter(Mandatory)] [string] $InstallPath)

    $install = Assert-CountdownInstallPathOnDDrive -Path $InstallPath
    Join-Path (Split-Path -Parent $install) '.minmin-countdown-transactions'
}

function Enter-CountdownInstallTransactionMutex {
    param([ValidateRange(1, 600)] [int] $TimeoutSeconds = 120)

    $mutex = [System.Threading.Mutex]::new($false, $script:CountdownInstallMutexName)
    try {
        $acquired = $false
        try {
            $acquired = $mutex.WaitOne([TimeSpan]::FromSeconds($TimeoutSeconds))
        }
        catch [System.Threading.AbandonedMutexException] {
            # 前一个安装进程异常退出时仍取得所有权，并由当前调用方恢复遗留事务。
            $acquired = $true
        }
        if (-not $acquired) {
            throw "等待倒计时安装事务锁超时（$TimeoutSeconds 秒）。"
        }
        $mutex
    }
    catch {
        $mutex.Dispose()
        throw
    }
}

function Exit-CountdownInstallTransactionMutex {
    param([AllowNull()] [System.Threading.Mutex] $Mutex)

    if ($null -eq $Mutex) { return }
    try { $Mutex.ReleaseMutex() } catch [System.ApplicationException] { }
    finally { $Mutex.Dispose() }
}

function Remove-CountdownEmptyTransactionBase {
    param([Parameter(Mandatory)] [string] $BasePath)

    # 事务目录已删除后，清理空父目录只是卫生动作，绝不能把已提交事务误报为失败。
    try {
        if ((Test-Path -LiteralPath $BasePath -PathType Container) -and @((Get-ChildItem -LiteralPath $BasePath -Force -ErrorAction Stop)).Count -eq 0) {
            Remove-Item -LiteralPath $BasePath -Force -ErrorAction Stop
        }
    }
    catch {
        # 下次事务会再次尝试清理；这里不影响提交或回滚的原子边界。
    }
}

function Write-CountdownInstallJsonAtomically {
    param(
        [Parameter(Mandatory)] [string] $Path,
        [Parameter(Mandatory)] [object] $Value
    )

    $resolvedPath = Assert-CountdownInstallPathOnDDrive -Path $Path
    $directory = Split-Path -Parent $resolvedPath
    New-Item -ItemType Directory -Path $directory -Force | Out-Null
    $temporaryPath = "$resolvedPath.$([guid]::NewGuid().ToString('N')).tmp"
    try {
        [System.IO.File]::WriteAllText($temporaryPath, ($Value | ConvertTo-Json -Depth 8), (New-Object System.Text.UTF8Encoding($false)))
        Move-Item -LiteralPath $temporaryPath -Destination $resolvedPath -Force
    }
    finally {
        Remove-Item -LiteralPath $temporaryPath -Force -ErrorAction SilentlyContinue
    }
}

function Write-CountdownInstallTransactionJournal {
    param(
        [Parameter(Mandatory)] [string] $TransactionPath,
        [Parameter(Mandatory)] [object] $Journal
    )

    $transaction = ConvertTo-CountdownInstallFullPath -Path $TransactionPath
    Write-CountdownInstallJsonAtomically -Path (Join-Path $transaction 'journal.json') -Value $Journal
}

function Get-CountdownJournalProperty {
    param(
        [Parameter(Mandatory)] [object] $Journal,
        [Parameter(Mandatory)] [string] $Name
    )

    $property = $Journal.PSObject.Properties[$Name]
    if ($null -eq $property) { return $null }
    $property.Value
}

function Assert-CountdownJournalBackupPaths {
    param(
        [Parameter(Mandatory)] [object] $Journal,
        [Parameter(Mandatory)] [string] $TransactionPath
    )

    $paths = [ordered]@{
        backupInstallPath = [string] (Get-CountdownJournalProperty -Journal $Journal -Name 'backupInstallPath')
        backupLauncherPath = [string] (Get-CountdownJournalProperty -Journal $Journal -Name 'backupLauncherPath')
        backupLegacyLauncherPath = [string] (Get-CountdownJournalProperty -Journal $Journal -Name 'backupLegacyLauncherPath')
        backupLauncherTempPath = [string] (Get-CountdownJournalProperty -Journal $Journal -Name 'backupLauncherTempPath')
        currentTaskXmlPath = [string] (Get-CountdownJournalProperty -Journal (Get-CountdownJournalProperty -Journal $Journal -Name 'currentTask') -Name 'XmlPath')
        legacyTaskXmlPath = [string] (Get-CountdownJournalProperty -Journal (Get-CountdownJournalProperty -Journal $Journal -Name 'legacyTask') -Name 'XmlPath')
    }
    foreach ($entry in $paths.GetEnumerator()) {
        if ([string]::IsNullOrWhiteSpace($entry.Value) -or -not (Test-CountdownInstallChildPath -Path $entry.Value -ParentPath $TransactionPath)) {
            throw "安装事务 journal 的 $($entry.Key) 不在事务目录内：$TransactionPath"
        }
    }
}

function Get-CountdownStrictBooleanProperty {
    param(
        [Parameter(Mandatory)] [object] $Object,
        [Parameter(Mandatory)] [string] $Name,
        [Parameter(Mandatory)] [string] $Context
    )

    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property -or $property.Value -isnot [bool]) {
        throw "$Context 缺少严格布尔字段 $Name。"
    }
    [bool] $property.Value
}

function Get-CountdownRequiredStringProperty {
    param(
        [Parameter(Mandatory)] [object] $Object,
        [Parameter(Mandatory)] [string] $Name,
        [Parameter(Mandatory)] [string] $Context
    )

    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property -or $property.Value -isnot [string] -or [string]::IsNullOrWhiteSpace([string] $property.Value)) {
        throw "$Context 缺少非空字符串字段 $Name。"
    }
    [string] $property.Value
}

function Get-CountdownValidatedScheduledTaskXml {
    param(
        [Parameter(Mandatory)] [object] $Snapshot,
        [Parameter(Mandatory)] [string] $SnapshotName
    )

    if (-not [bool] $Snapshot.Exists) { return $null }
    $xmlPath = Get-CountdownRequiredStringProperty -Object $Snapshot -Name 'XmlPath' -Context "安装事务 journal 的 $SnapshotName"
    try {
        $taskXml = [System.Xml.XmlDocument]::new()
        $taskXml.PreserveWhitespace = $true
        $taskXml.LoadXml([System.IO.File]::ReadAllText($xmlPath, [System.Text.Encoding]::UTF8))
    }
    catch {
        throw "安装事务 journal 的 $SnapshotName XML 无法解析：$xmlPath"
    }
    if ($null -eq $taskXml.DocumentElement -or $taskXml.DocumentElement.LocalName -ne 'Task') {
        throw "安装事务 journal 的 $SnapshotName XML 缺少 Task 根节点：$xmlPath"
    }
    $actionsNode = $taskXml.SelectSingleNode('/*[local-name()="Task"]/*[local-name()="Actions"]')
    $execNode = $taskXml.SelectSingleNode('/*[local-name()="Task"]/*[local-name()="Actions"]/*[local-name()="Exec"]')
    $commandNode = $taskXml.SelectSingleNode('/*[local-name()="Task"]/*[local-name()="Actions"]/*[local-name()="Exec"]/*[local-name()="Command"]')
    if ($null -eq $actionsNode -or $null -eq $execNode -or $null -eq $commandNode -or [string]::IsNullOrWhiteSpace([string] $commandNode.InnerText)) {
        throw "安装事务 journal 的 $SnapshotName XML 缺少 Actions/Exec/Command：$xmlPath"
    }
    $taskXml
}

function Assert-CountdownJournalTaskSnapshot {
    param(
        [Parameter(Mandatory)] [object] $Journal,
        [Parameter(Mandatory)] [string] $Name,
        [Parameter(Mandatory)] [string] $TransactionPath
    )

    $snapshot = Get-CountdownJournalProperty -Journal $Journal -Name $Name
    if ($null -eq $snapshot) { throw "安装事务 journal 缺少 $Name：$TransactionPath" }
    $exists = Get-CountdownStrictBooleanProperty -Object $snapshot -Name 'Exists' -Context "安装事务 journal 的 $Name"
    $xmlPath = Get-CountdownRequiredStringProperty -Object $snapshot -Name 'XmlPath' -Context "安装事务 journal 的 $Name"
    if (-not (Test-CountdownInstallChildPath -Path $xmlPath -ParentPath $TransactionPath)) {
        throw "安装事务 journal 的 $Name.XmlPath 不在事务目录内：$TransactionPath"
    }
    $stateProperty = $snapshot.PSObject.Properties['State']
    if ($exists) {
        if ($null -eq $stateProperty -or $stateProperty.Value -isnot [string] -or [string] $stateProperty.Value -notin @('Unknown', 'Disabled', 'Queued', 'Ready', 'Running')) {
            throw "安装事务 journal 的 $Name.State 非法：$TransactionPath"
        }
        if (-not (Test-Path -LiteralPath $xmlPath -PathType Leaf)) {
            throw "安装事务 journal 缺少已存在计划任务的 XML 备份：$xmlPath"
        }
        # 在任何 Stop/Unregister 之前解析并校验备份；回滚绝不能先删除当前任务再发现 XML 已损坏。
        return (Get-CountdownValidatedScheduledTaskXml -Snapshot $snapshot -SnapshotName $Name)
    }
    elseif ($null -ne $stateProperty -and $null -ne $stateProperty.Value) {
        throw "安装事务 journal 的不存在任务不应携带 State：$TransactionPath"
    }
    $null
}

function Assert-CountdownJournalPayloadBackupsAvailable {
    param(
        [Parameter(Mandatory)] [object] $Journal,
        [Parameter(Mandatory)] [string] $TransactionPath
    )

    if (-not [bool] $Journal.payloadSnapshotReady) { return }
    if ([bool] $Journal.installExisted -and -not (Test-Path -LiteralPath ([string] $Journal.backupInstallPath) -PathType Container)) {
        throw "安装事务缺少安装目录备份：$($Journal.backupInstallPath)"
    }
    foreach ($entry in @(
            @{ Name = 'launcher'; Existed = [bool] $Journal.launcherExisted; BackupPath = [string] $Journal.backupLauncherPath },
            @{ Name = 'launcherTemp'; Existed = [bool] $Journal.launcherTempExisted; BackupPath = [string] $Journal.backupLauncherTempPath },
            @{ Name = 'legacyLauncher'; Existed = [bool] $Journal.legacyLauncherExisted; BackupPath = [string] $Journal.backupLegacyLauncherPath }
        )) {
        if ($entry.Existed -and -not (Test-Path -LiteralPath $entry.BackupPath -PathType Leaf)) {
            throw "安装事务缺少 $($entry.Name) 备份：$($entry.BackupPath)"
        }
    }
}

function Get-CountdownInstallTransactionJournal {
    param(
        [Parameter(Mandatory)] [string] $TransactionPath,
        [hashtable] $ValidatedTaskXml
    )

    $transaction = ConvertTo-CountdownInstallFullPath -Path $TransactionPath
    $journalPath = Join-Path $transaction 'journal.json'
    if (-not (Test-Path -LiteralPath $journalPath -PathType Leaf)) { throw "安装事务缺少 journal：$transaction" }
    $journal = [System.IO.File]::ReadAllText($journalPath, [System.Text.Encoding]::UTF8) | ConvertFrom-Json
    if ($null -eq $journal) { throw "安装事务 journal 为空：$transaction" }
    $schemaValue = Get-CountdownJournalProperty -Journal $journal -Name 'schemaVersion'
    if (($schemaValue -isnot [int] -and $schemaValue -isnot [long]) -or [int] $schemaValue -notin @(1, 2, 3, 4)) {
        throw "安装事务 journal schemaVersion 不受支持：$transaction"
    }
    $schemaVersion = [int] $schemaValue
    $installPath = [string] (Get-CountdownJournalProperty -Journal $journal -Name 'installPath')
    $journalTransactionPath = [string] (Get-CountdownJournalProperty -Journal $journal -Name 'transactionPath')
    if ([string]::IsNullOrWhiteSpace($installPath) -or [string]::IsNullOrWhiteSpace($journalTransactionPath)) {
        throw "安装事务 journal 不完整：$transaction"
    }
    $install = Assert-CountdownInstallPathOnDDrive -Path $installPath
    $basePath = Get-CountdownTransactionBasePath -InstallPath $install
    if (-not (Test-CountdownInstallChildPath -Path $transaction -ParentPath $basePath)) {
        throw "拒绝读取不受管的事务目录：$transaction"
    }
    if (-not [string]::Equals((ConvertTo-CountdownInstallFullPath -Path $journalTransactionPath), $transaction, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "安装事务 journal 路径不匹配：$transaction"
    }
    foreach ($name in @('launcherPath', 'legacyLauncherPath', 'taskName', 'legacyTaskName', 'windowsTime', 'currentTask', 'legacyTask', 'phase', 'installExisted', 'launcherExisted', 'legacyLauncherExisted')) {
        if ($null -eq (Get-CountdownJournalProperty -Journal $journal -Name $name)) {
            throw "安装事务 journal 缺少 $name：$transaction"
        }
    }

    # 兼容已落盘的 schema v1 journal：旧版没有 .new 启动器快照，回滚时按“原本不存在”处理。
    if ($null -eq $journal.PSObject.Properties['launcherTempPath']) {
        $journal | Add-Member -NotePropertyName launcherTempPath -NotePropertyValue ("$([string] $journal.launcherPath).new")
    }
    if ($null -eq $journal.PSObject.Properties['launcherTempExisted']) {
        $journal | Add-Member -NotePropertyName launcherTempExisted -NotePropertyValue $false
    }
    if ($null -eq $journal.PSObject.Properties['backupLauncherTempPath']) {
        $journal | Add-Member -NotePropertyName backupLauncherTempPath -NotePropertyValue (Join-Path $transaction 'launcher-temp-backup.new')
    }
    # schema v1/v2 在创建 journal 时已完成可变文件备份；它们按完整快照恢复。
    if ($null -eq $journal.PSObject.Properties['payloadSnapshotReady']) {
        if ($schemaVersion -ge 3) { throw "安装事务 journal 缺少 payloadSnapshotReady：$transaction" }
        $journal | Add-Member -NotePropertyName payloadSnapshotReady -NotePropertyValue $true
    }
    if ($null -eq $journal.PSObject.Properties['candidateLauncherSha256']) {
        $journal | Add-Member -NotePropertyName candidateLauncherSha256 -NotePropertyValue $null
    }
    foreach ($name in @('installExisted', 'launcherExisted', 'legacyLauncherExisted', 'launcherTempExisted', 'payloadSnapshotReady')) {
        [void] (Get-CountdownStrictBooleanProperty -Object $journal -Name $name -Context \"安装事务 journal：$transaction\")
    }
    $phase = Get-CountdownRequiredStringProperty -Object $journal -Name 'phase' -Context \"安装事务 journal：$transaction\"
    if ($phase -notin @('prepared', 'payload-snapshot-failed', 'payload-snapshotted', 'launcher-candidate-staged', 'rolling-back', 'rollback-failed', 'launcher-ownership-conflict', 'committed', 'rolled-back')) {
        throw \"安装事务 journal phase 不受支持：$phase\"
    }
    $candidateLauncherHash = Get-CountdownJournalProperty -Journal $journal -Name 'candidateLauncherSha256'
    if ($null -ne $candidateLauncherHash -and ($candidateLauncherHash -isnot [string] -or $candidateLauncherHash -notmatch '^[A-Fa-f0-9]{64}$')) {
        throw \"安装事务 journal 的 candidateLauncherSha256 非法：$transaction\"
    }
    $currentTaskXml = Assert-CountdownJournalTaskSnapshot -Journal $journal -Name 'currentTask' -TransactionPath $transaction
    $legacyTaskXml = Assert-CountdownJournalTaskSnapshot -Journal $journal -Name 'legacyTask' -TransactionPath $transaction
    if ($null -ne $ValidatedTaskXml) {
        $ValidatedTaskXml['currentTask'] = $currentTaskXml
        $ValidatedTaskXml['legacyTask'] = $legacyTaskXml
    }
    $windowsTime = Get-CountdownJournalProperty -Journal $journal -Name 'windowsTime'
    if ($null -eq $windowsTime -or (Get-CountdownRequiredStringProperty -Object $windowsTime -Name 'StartMode' -Context \"安装事务 journal：$transaction\") -notin @('Auto', 'Manual', 'Disabled') -or (Get-CountdownRequiredStringProperty -Object $windowsTime -Name 'State' -Context \"安装事务 journal：$transaction\") -notin @('Running', 'Stopped')) {
        throw \"安装事务 journal 的 Windows 时间服务快照非法：$transaction\"
    }
    foreach ($path in @([string] $journal.launcherPath, [string] $journal.legacyLauncherPath, [string] $journal.launcherTempPath)) {
        [void] (Assert-CountdownInstallPathOnDDrive -Path $path)
    }
    Assert-CountdownJournalBackupPaths -Journal $journal -TransactionPath $transaction
    Assert-CountdownJournalPayloadBackupsAvailable -Journal $journal -TransactionPath $transaction
    $journal
}

function Assert-CountdownInstallTransactionIdentity {
    param(
        [Parameter(Mandatory)] [object] $Journal,
        [string] $ExpectedInstallPath,
        [string] $ExpectedTaskName,
        [string] $ExpectedLegacyTaskName,
        [string] $ExpectedLauncherPath,
        [string] $ExpectedLegacyLauncherPath,
        [string] $ExpectedLauncherTempPath
    )

    if (-not [string]::IsNullOrWhiteSpace($ExpectedInstallPath)) {
        $expectedInstall = Assert-CountdownInstallPathOnDDrive -Path $ExpectedInstallPath
        $journalInstall = Assert-CountdownInstallPathOnDDrive -Path ([string] $Journal.installPath)
        if (-not [string]::Equals($journalInstall, $expectedInstall, [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "安装事务 installPath 与当前安装目标不一致：$journalInstall"
        }
    }
    if (-not [string]::IsNullOrWhiteSpace($ExpectedTaskName) -and -not [string]::Equals([string] $Journal.taskName, $ExpectedTaskName, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "安装事务 taskName 与当前安装任务不一致：$($Journal.taskName)"
    }
    if (-not [string]::IsNullOrWhiteSpace($ExpectedLegacyTaskName) -and -not [string]::Equals([string] $Journal.legacyTaskName, $ExpectedLegacyTaskName, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "安装事务 legacyTaskName 与当前安装任务不一致：$($Journal.legacyTaskName)"
    }
    foreach ($entry in @(
            @{ Name = 'launcherPath'; Actual = [string] $Journal.launcherPath; Expected = $ExpectedLauncherPath },
            @{ Name = 'legacyLauncherPath'; Actual = [string] $Journal.legacyLauncherPath; Expected = $ExpectedLegacyLauncherPath },
            @{ Name = 'launcherTempPath'; Actual = [string] $Journal.launcherTempPath; Expected = $ExpectedLauncherTempPath }
        )) {
        if ([string]::IsNullOrWhiteSpace([string] $entry.Expected)) { continue }
        $expectedPath = Assert-CountdownInstallPathOnDDrive -Path ([string] $entry.Expected)
        $journalPath = Assert-CountdownInstallPathOnDDrive -Path ([string] $entry.Actual)
        if (-not [string]::Equals($journalPath, $expectedPath, [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "安装事务 $($entry.Name) 与当前安装目标不一致：$journalPath"
        }
    }
}

function Set-CountdownInstallTransactionPhase {
    param(
        [Parameter(Mandatory)] [string] $TransactionPath,
        [Parameter(Mandatory)] [string] $Phase
    )

    $journal = Get-CountdownInstallTransactionJournal -TransactionPath $TransactionPath
    $journal.phase = $Phase
    $journal.updatedAtUtc = [DateTimeOffset]::UtcNow.ToString('o')
    Write-CountdownInstallTransactionJournal -TransactionPath $TransactionPath -Journal $journal
    $journal
}

function Write-CountdownDeferredInstallReceipt {
    param(
        [Parameter(Mandatory)] [string] $Path,
        [Parameter(Mandatory)] [string] $TransactionPath,
        [Parameter(Mandatory)] [string] $LaunchToken,
        [Parameter(Mandatory)] [ValidateRange(0, 2147483647)] [int] $InteractiveSessionId
    )

    if ([string]::IsNullOrWhiteSpace($LaunchToken)) { throw '延迟提交回执缺少启动令牌。' }
    $receipt = [ordered]@{
        schemaVersion = 1
        transactionPath = Assert-CountdownInstallPathOnDDrive -Path $TransactionPath
        launchToken = $LaunchToken
        interactiveSessionId = $InteractiveSessionId
        writtenAtUtc = [DateTimeOffset]::UtcNow.ToString('o')
    }
    Write-CountdownInstallJsonAtomically -Path $Path -Value $receipt
    [pscustomobject] $receipt
}

function Read-CountdownDeferredInstallReceipt {
    param([Parameter(Mandatory)] [string] $Path)

    try {
        $resolvedPath = Assert-CountdownInstallPathOnDDrive -Path $Path
        if (-not (Test-Path -LiteralPath $resolvedPath -PathType Leaf)) { return $null }
        $receipt = [System.IO.File]::ReadAllText($resolvedPath, [System.Text.Encoding]::UTF8) | ConvertFrom-Json
        if ((Get-CountdownJournalProperty -Journal $receipt -Name 'schemaVersion') -ne 1) { return $null }
        $transactionPath = [string] (Get-CountdownJournalProperty -Journal $receipt -Name 'transactionPath')
        $launchToken = [string] (Get-CountdownJournalProperty -Journal $receipt -Name 'launchToken')
        $sessionId = Get-CountdownJournalProperty -Journal $receipt -Name 'interactiveSessionId'
        if ([string]::IsNullOrWhiteSpace($transactionPath) -or [string]::IsNullOrWhiteSpace($launchToken) -or $null -eq $sessionId -or [int] $sessionId -lt 0) { return $null }
        $receipt.transactionPath = Assert-CountdownInstallPathOnDDrive -Path $transactionPath
        $receipt
    }
    catch {
        # 调用方可安全地把损坏或缺失回执视为“无法定位待回滚事务”。
        $null
    }
}

function Copy-CountdownInstallDirectoryContents {
    param(
        [Parameter(Mandatory)] [string] $Source,
        [Parameter(Mandatory)] [string] $Destination
    )

    if (-not (Test-Path -LiteralPath $Source -PathType Container)) { return }
    New-Item -ItemType Directory -Path $Destination -Force | Out-Null
    foreach ($item in @(Get-ChildItem -LiteralPath $Source -Force)) {
        Copy-Item -LiteralPath $item.FullName -Destination $Destination -Recurse -Force
    }
}

function Save-CountdownInstallFileSnapshot {
    param(
        [Parameter(Mandatory)] [string] $Path,
        [Parameter(Mandatory)] [string] $BackupPath
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { return $false }
    $parent = Split-Path -Parent $BackupPath
    New-Item -ItemType Directory -Path $parent -Force | Out-Null
    Copy-Item -LiteralPath $Path -Destination $BackupPath -Force
    $true
}

function Get-CountdownScheduledTaskSnapshot {
    param(
        [Parameter(Mandatory)] [string] $TaskName,
        [Parameter(Mandatory)] [string] $XmlPath
    )

    $task = Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
    if ($null -eq $task) {
        return [pscustomobject]@{ Exists = $false; State = $null; XmlPath = $XmlPath }
    }
    [System.IO.File]::WriteAllText($XmlPath, [string] (Export-ScheduledTask -TaskName $TaskName), (New-Object System.Text.UTF8Encoding($false)))
    [pscustomobject]@{ Exists = $true; State = [string] $task.State; XmlPath = $XmlPath }
}

function Get-CountdownWindowsTimeSnapshot {
    $service = Get-CimInstance Win32_Service -Filter "Name='W32Time'" -ErrorAction Stop
    [pscustomobject]@{ StartMode = [string] $service.StartMode; State = [string] $service.State }
}

function Restore-CountdownWindowsTimeSnapshot {
    param([Parameter(Mandatory)] [object] $Snapshot)

    $startupType = switch ([string] $Snapshot.StartMode) {
        'Auto' { 'Automatic' }
        'Manual' { 'Manual' }
        'Disabled' { 'Disabled' }
        default { throw "未知的 W32Time 启动类型：$($Snapshot.StartMode)" }
    }
    Set-Service -Name W32Time -StartupType $startupType
    $current = Get-Service -Name W32Time -ErrorAction Stop
    if ([string] $Snapshot.State -eq 'Running') {
        if ($current.Status -ne 'Running') { Start-Service -Name W32Time }
    }
    elseif ($current.Status -eq 'Running') {
        Stop-Service -Name W32Time -Force
    }
}

function ConvertFrom-CountdownWindowsCommandLine {
    param([AllowEmptyString()] [string] $CommandLine)

    if ([string]::IsNullOrWhiteSpace($CommandLine)) { return @() }
    if ($null -eq ('MinminCountdownCommandLineNative' -as [type])) {
        Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;

public static class MinminCountdownCommandLineNative
{
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr CommandLineToArgvW(string lpCmdLine, out int pNumArgs);

    [DllImport("kernel32.dll")]
    public static extern IntPtr LocalFree(IntPtr hMem);
}
'@
    }
    [int] $argumentCount = 0
    $argvPointer = [MinminCountdownCommandLineNative]::CommandLineToArgvW($CommandLine, [ref] $argumentCount)
    if ($argvPointer -eq [IntPtr]::Zero -or $argumentCount -le 0) { return @() }
    try {
        $arguments = [System.Collections.Generic.List[string]]::new()
        for ($index = 0; $index -lt $argumentCount; $index++) {
            $argumentPointer = [System.Runtime.InteropServices.Marshal]::ReadIntPtr($argvPointer, $index * [IntPtr]::Size)
            [void] $arguments.Add([System.Runtime.InteropServices.Marshal]::PtrToStringUni($argumentPointer))
        }
        @($arguments)
    }
    finally {
        [void] [MinminCountdownCommandLineNative]::LocalFree($argvPointer)
    }
}

function Get-CountdownManagedApplicationFileArgument {
    param([AllowEmptyString()] [string] $CommandLine)

    $argv = @(ConvertFrom-CountdownWindowsCommandLine -CommandLine $CommandLine)
    if ($argv.Count -lt 3) { return $null }
    $commandModes = @('-command', '-c', '-encodedcommand', '-e', '-ec', '-commandwithargs', '-encodedcommandwithargs')
    $switchesWithoutValue = @('-nologo', '-nol', '-noprofile', '-nop', '-noninteractive', '-noni', '-noexit', '-noe', '-sta', '-mta', '-login')
    $switchesWithValue = @('-executionpolicy', '-ep', '-windowstyle', '-w', '-inputformat', '-if', '-outputformat', '-of', '-configurationname', '-config', '-version', '-v', '-settingsfile', '-settings', '-workingdirectory', '-wd')
    $index = 1
    while ($index -lt $argv.Count) {
        $argument = [string] $argv[$index]
        $normalized = $argument.ToLowerInvariant()
        if ($normalized -in $commandModes) { return $null }
        if ($normalized -in @('-file', '-f')) {
            if (($index + 1) -ge $argv.Count) { return $null }
            $path = [string] $argv[$index + 1]
            if ([string]::IsNullOrWhiteSpace($path) -or $path.StartsWith('-')) { return $null }
            try { return ConvertTo-CountdownInstallFullPath -Path $path } catch { return $null }
        }
        if ($normalized -in $switchesWithoutValue) {
            $index++
            continue
        }
        if ($normalized -in $switchesWithValue) {
            if (($index + 1) -ge $argv.Count) { return $null }
            $index += 2
            continue
        }
        return $null
    }
    $null
}

function Get-CountdownManagedApplicationProcesses {
    param([Parameter(Mandatory)] [string] $ApplicationPath)

    $resolvedPath = ConvertTo-CountdownInstallFullPath -Path $ApplicationPath
    @(
        Get-CimInstance Win32_Process -Filter "Name='pwsh.exe' OR Name='powershell.exe'" -ErrorAction SilentlyContinue |
            Where-Object {
                $fileArgument = Get-CountdownManagedApplicationFileArgument -CommandLine ([string] $_.CommandLine)
                -not [string]::IsNullOrWhiteSpace($fileArgument) -and [string]::Equals($fileArgument, $resolvedPath, [System.StringComparison]::OrdinalIgnoreCase)
            }
    )
}

function Get-CountdownManagedTaskStates {
    param(
        [Parameter(Mandatory)] [string] $TaskName,
        [Parameter(Mandatory)] [string] $LegacyTaskName
    )

    $states = @{}
    foreach ($name in @($TaskName, $LegacyTaskName)) {
        $task = Get-ScheduledTask -TaskName $name -ErrorAction SilentlyContinue
        $states[$name] = if ($null -eq $task) { 'Missing' } else { [string] $task.State }
    }
    $states
}

function Test-CountdownManagedApplicationStopped {
    param(
        [int[]] $ProcessIds = @(),
        [Parameter(Mandatory)] [hashtable] $TaskStates
    )

    # 只有已知静止态才允许继续替换；Queued、Unknown 等任何其他状态都可能继续启动旧应用。
    $runningTasks = @($TaskStates.GetEnumerator() | Where-Object { [string] $_.Value -notin @('Ready', 'Disabled', 'Missing') } | ForEach-Object { [string] $_.Key })
    [pscustomobject]@{
        Ready = (@($ProcessIds).Count -eq 0 -and $runningTasks.Count -eq 0)
        ProcessIds = @($ProcessIds)
        RunningTasks = $runningTasks
    }
}

function Stop-CountdownManagedApplication {
    param(
        [Parameter(Mandatory)] [string] $InstallPath,
        [Parameter(Mandatory)] [string] $TaskName,
        [Parameter(Mandatory)] [string] $LegacyTaskName,
        [switch] $FreezeFutureStarts,
        [ValidateRange(1, 60)] [int] $TimeoutSeconds = 10
    )

    $install = Assert-CountdownInstallPathOnDDrive -Path $InstallPath
    $applicationPath = Join-Path $install '敏敏子倒计时.ps1'
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)
    do {
        foreach ($name in @($TaskName, $LegacyTaskName)) {
            $task = Get-ScheduledTask -TaskName $name -ErrorAction SilentlyContinue
            if ($null -ne $task) {
                if ($FreezeFutureStarts) {
                    # journal 已在调用前捕获；禁用任务阻断桌面启动器/手工 schtasks 在静止窗口内重新拉起旧版本。
                    Disable-ScheduledTask -TaskName $name -ErrorAction Stop | Out-Null
                }
                # 即使刚被禁用仍显为 Queued/Running，也明确取消已有实例。
                Stop-ScheduledTask -TaskName $name -ErrorAction SilentlyContinue
            }
        }
        foreach ($process in @(Get-CountdownManagedApplicationProcesses -ApplicationPath $applicationPath)) {
            Stop-Process -Id $process.ProcessId -Force -ErrorAction SilentlyContinue
        }

        $remaining = @(Get-CountdownManagedApplicationProcesses -ApplicationPath $applicationPath)
        $states = Get-CountdownManagedTaskStates -TaskName $TaskName -LegacyTaskName $LegacyTaskName
        $stopped = Test-CountdownManagedApplicationStopped -ProcessIds @($remaining | ForEach-Object { [int] $_.ProcessId }) -TaskStates $states
        if ($stopped.Ready) { return }
        if ([DateTimeOffset]::UtcNow -ge $deadline) { break }
        Start-Sleep -Milliseconds 250
    } while ($true)

    $remaining = @(Get-CountdownManagedApplicationProcesses -ApplicationPath $applicationPath)
    $states = Get-CountdownManagedTaskStates -TaskName $TaskName -LegacyTaskName $LegacyTaskName
    $stopped = Test-CountdownManagedApplicationStopped -ProcessIds @($remaining | ForEach-Object { [int] $_.ProcessId }) -TaskStates $states
    $details = @()
    if ($stopped.ProcessIds.Count -gt 0) { $details += "进程=$($stopped.ProcessIds -join ',')" }
    if ($stopped.RunningTasks.Count -gt 0) { $details += "计划任务=$($stopped.RunningTasks -join ',')" }
    throw "旧版倒计时未在 $TimeoutSeconds 秒内退出：$($details -join '；')"
}

function Restore-CountdownScheduledTaskSnapshot {
    param(
        [Parameter(Mandatory)] [string] $TaskName,
        [Parameter(Mandatory)] [object] $Snapshot,
        [Parameter(Mandatory)] [string] $PowerShell7Path,
        [AllowNull()] [System.Xml.XmlDocument] $ValidatedTaskXml
    )

    if ([bool] $Snapshot.Exists -and $null -eq $ValidatedTaskXml) {
        throw "安装事务缺少已预检的计划任务 XML：$TaskName"
    }
    $existing = Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
    if ($null -ne $existing -and [string] $existing.State -notin @('Ready', 'Disabled', 'Missing')) {
        Stop-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
    }
    if ($null -ne $existing) {
        Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false -ErrorAction Stop
    }
    if (-not [bool] $Snapshot.Exists) { return }
    $taskXml = $ValidatedTaskXml
    $commandNode = $taskXml.SelectSingleNode('/*[local-name()="Task"]/*[local-name()="Actions"]/*[local-name()="Exec"]/*[local-name()="Command"]')
    if ($null -eq $commandNode -or [string]::IsNullOrWhiteSpace([string] $commandNode.InnerText)) { throw "无法在备份任务中定位命令：$TaskName" }
    if ([System.IO.Path]::GetFileName([string] $commandNode.InnerText) -ieq 'powershell.exe') {
        $commandNode.InnerText = $PowerShell7Path
    }
    Register-ScheduledTask -TaskName $TaskName -Xml $taskXml.OuterXml -Force | Out-Null
    # Queued 也表示原任务已有待执行实例；回滚后重新触发，避免把用户原本已排队的启动静默丢失。
    if ([string] $Snapshot.State -in @('Running', 'Queued')) {
        Start-ScheduledTask -TaskName $TaskName
    }
}

function Start-CountdownInstallTransaction {
    param(
        [Parameter(Mandatory)] [string] $InstallPath,
        [Parameter(Mandatory)] [string] $LauncherPath,
        [Parameter(Mandatory)] [string] $LegacyLauncherPath,
        [Parameter(Mandatory)] [string] $TaskName,
        [Parameter(Mandatory)] [string] $LegacyTaskName
    )

    $install = Assert-CountdownInstallPathOnDDrive -Path $InstallPath
    $launcher = Assert-CountdownInstallPathOnDDrive -Path $LauncherPath
    $legacyLauncher = Assert-CountdownInstallPathOnDDrive -Path $LegacyLauncherPath
    $launcherTemp = Assert-CountdownInstallPathOnDDrive -Path "$launcher.new"
    $basePath = Get-CountdownTransactionBasePath -InstallPath $install
    New-Item -ItemType Directory -Path $basePath -Force | Out-Null
    $transactionPath = Join-Path $basePath ([guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $transactionPath -Force | Out-Null
    try {
        $backupInstallPath = Join-Path $transactionPath 'install-backup'
        $backupLauncherPath = Join-Path $transactionPath 'launcher-backup.exe'
        $backupLegacyLauncherPath = Join-Path $transactionPath 'legacy-launcher-backup.cmd'
        $backupLauncherTempPath = Join-Path $transactionPath 'launcher-temp-backup.new'
        $currentTaskSnapshot = Get-CountdownScheduledTaskSnapshot -TaskName $TaskName -XmlPath (Join-Path $transactionPath 'current-task.xml')
        $legacyTaskSnapshot = Get-CountdownScheduledTaskSnapshot -TaskName $LegacyTaskName -XmlPath (Join-Path $transactionPath 'legacy-task.xml')
        $journal = [ordered]@{
            schemaVersion = 4
            phase = 'prepared'
            transactionPath = $transactionPath
            installPath = $install
            # 任务/W32Time 在停止旧应用前写入；可变的安装树与桌面启动器必须等静止后再捕获。
            payloadSnapshotReady = $false
            installExisted = $false
            backupInstallPath = $backupInstallPath
            launcherPath = $launcher
            launcherExisted = $false
            backupLauncherPath = $backupLauncherPath
            candidateLauncherSha256 = $null
            launcherTempPath = $launcherTemp
            launcherTempExisted = $false
            backupLauncherTempPath = $backupLauncherTempPath
            legacyLauncherPath = $legacyLauncher
            legacyLauncherExisted = $false
            backupLegacyLauncherPath = $backupLegacyLauncherPath
            taskName = $TaskName
            legacyTaskName = $LegacyTaskName
            currentTask = $currentTaskSnapshot
            legacyTask = $legacyTaskSnapshot
            windowsTime = Get-CountdownWindowsTimeSnapshot
            createdAtUtc = [DateTimeOffset]::UtcNow.ToString('o')
            updatedAtUtc = [DateTimeOffset]::UtcNow.ToString('o')
        }
        Write-CountdownInstallTransactionJournal -TransactionPath $transactionPath -Journal $journal
        [pscustomobject] $journal
    }
    catch {
        Remove-Item -LiteralPath $transactionPath -Recurse -Force -ErrorAction SilentlyContinue
        throw
    }
}

function Capture-CountdownInstallPayloadSnapshot {
    param(
        [Parameter(Mandatory)] [string] $TransactionPath,
        [string] $ExpectedInstallPath,
        [string] $ExpectedTaskName,
        [string] $ExpectedLegacyTaskName,
        [string] $ExpectedLauncherPath,
        [string] $ExpectedLegacyLauncherPath,
        [string] $ExpectedLauncherTempPath
    )

    $journal = Get-CountdownInstallTransactionJournal -TransactionPath $TransactionPath
    Assert-CountdownInstallTransactionIdentity -Journal $journal -ExpectedInstallPath $ExpectedInstallPath -ExpectedTaskName $ExpectedTaskName -ExpectedLegacyTaskName $ExpectedLegacyTaskName -ExpectedLauncherPath $ExpectedLauncherPath -ExpectedLegacyLauncherPath $ExpectedLegacyLauncherPath -ExpectedLauncherTempPath $ExpectedLauncherTempPath
    if ([bool] $journal.payloadSnapshotReady) { return $journal }
    if ([string] $journal.phase -notin @('prepared', 'payload-snapshot-failed')) {
        throw "安装事务不处于可捕获状态：$($journal.phase)"
    }
    try {
        $install = Assert-CountdownInstallPathOnDDrive -Path ([string] $journal.installPath)
        $journal.installExisted = [bool] (Test-Path -LiteralPath $install -PathType Container)
        if ([bool] $journal.installExisted) {
            Copy-CountdownInstallDirectoryContents -Source $install -Destination ([string] $journal.backupInstallPath)
        }
        $journal.launcherExisted = [bool] (Save-CountdownInstallFileSnapshot -Path ([string] $journal.launcherPath) -BackupPath ([string] $journal.backupLauncherPath))
        $journal.legacyLauncherExisted = [bool] (Save-CountdownInstallFileSnapshot -Path ([string] $journal.legacyLauncherPath) -BackupPath ([string] $journal.backupLegacyLauncherPath))
        $journal.launcherTempExisted = [bool] (Save-CountdownInstallFileSnapshot -Path ([string] $journal.launcherTempPath) -BackupPath ([string] $journal.backupLauncherTempPath))
        $journal.payloadSnapshotReady = $true
        $journal.phase = 'payload-snapshotted'
        $journal.updatedAtUtc = [DateTimeOffset]::UtcNow.ToString('o')
        Write-CountdownInstallTransactionJournal -TransactionPath $TransactionPath -Journal $journal
        [pscustomobject] $journal
    }
    catch {
        try {
            $journal.phase = 'payload-snapshot-failed'
            $journal.updatedAtUtc = [DateTimeOffset]::UtcNow.ToString('o')
            Write-CountdownInstallTransactionJournal -TransactionPath $TransactionPath -Journal $journal
        }
        catch { }
        throw
    }
}

function Set-CountdownInstallTransactionCandidateLauncherHash {
    param(
        [Parameter(Mandatory)] [string] $TransactionPath,
        [Parameter(Mandatory)] [ValidatePattern('^[A-Fa-f0-9]{64}$')] [string] $CandidateLauncherSha256,
        [string] $ExpectedInstallPath,
        [string] $ExpectedTaskName,
        [string] $ExpectedLegacyTaskName,
        [string] $ExpectedLauncherPath,
        [string] $ExpectedLegacyLauncherPath,
        [string] $ExpectedLauncherTempPath
    )

    $journal = Get-CountdownInstallTransactionJournal -TransactionPath $TransactionPath
    Assert-CountdownInstallTransactionIdentity -Journal $journal -ExpectedInstallPath $ExpectedInstallPath -ExpectedTaskName $ExpectedTaskName -ExpectedLegacyTaskName $ExpectedLegacyTaskName -ExpectedLauncherPath $ExpectedLauncherPath -ExpectedLegacyLauncherPath $ExpectedLegacyLauncherPath -ExpectedLauncherTempPath $ExpectedLauncherTempPath
    if (-not [bool] $journal.payloadSnapshotReady -or [string] $journal.phase -notin @('payload-snapshotted', 'launcher-candidate-staged')) {
        throw "安装事务尚未准备好记录候选启动器：$($journal.phase)"
    }
    $journal.candidateLauncherSha256 = $CandidateLauncherSha256.ToUpperInvariant()
    $journal.phase = 'launcher-candidate-staged'
    $journal.updatedAtUtc = [DateTimeOffset]::UtcNow.ToString('o')
    Write-CountdownInstallTransactionJournal -TransactionPath $TransactionPath -Journal $journal
    [pscustomobject] $journal
}

function Test-CountdownInstallFileRestoreOwnership {
    param(
        [Parameter(Mandatory)] [string] $Path,
        [Parameter(Mandatory)] [bool] $Existed,
        [Parameter(Mandatory)] [string] $BackupPath,
        [string] $CandidateSha256,
        [Parameter(Mandatory)] [string] $Label
    )

    $resolved = Assert-CountdownInstallPathOnDDrive -Path $Path
    if ($Existed -and -not (Test-Path -LiteralPath $BackupPath -PathType Leaf)) {
        throw "安装事务备份文件缺失：$BackupPath"
    }
    if (-not (Test-Path -LiteralPath $resolved -PathType Leaf)) {
        return [pscustomobject]@{ Label = $Label; Path = $resolved; Owned = $true }
    }
    $allowedHashes = [System.Collections.Generic.List[string]]::new()
    if ($Existed) {
        [void] $allowedHashes.Add((Get-FileHash -Algorithm SHA256 -LiteralPath $BackupPath).Hash)
    }
    if (-not [string]::IsNullOrWhiteSpace($CandidateSha256)) {
        [void] $allowedHashes.Add($CandidateSha256)
    }
    $currentHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $resolved).Hash
    [pscustomobject]@{ Label = $Label; Path = $resolved; Owned = [bool] $allowedHashes.Contains($currentHash) }
}

function Assert-CountdownInstallFileRestoreOwnership {
    param(
        [Parameter(Mandatory)] [string] $Path,
        [Parameter(Mandatory)] [bool] $Existed,
        [Parameter(Mandatory)] [string] $BackupPath,
        [string] $CandidateSha256,
        [Parameter(Mandatory)] [string] $Label
    )

    $ownership = Test-CountdownInstallFileRestoreOwnership -Path $Path -Existed $Existed -BackupPath $BackupPath -CandidateSha256 $CandidateSha256 -Label $Label
    if (-not [bool] $ownership.Owned) {
        throw "拒绝恢复 $Label：当前文件不属于事务候选、原始备份或缺失状态：$($ownership.Path)"
    }
}

function Get-CountdownInstallPayloadRestoreOwnershipConflicts {
    param([Parameter(Mandatory)] [object] $Journal)

    if (-not [bool] $Journal.payloadSnapshotReady) { return @() }
    $candidateHash = [string] $Journal.candidateLauncherSha256
    $specifications = @(
        [pscustomobject]@{ Key = 'launcher'; Path = [string] $Journal.launcherPath; Existed = [bool] $Journal.launcherExisted; BackupPath = [string] $Journal.backupLauncherPath; CandidateSha256 = $candidateHash; Label = '桌面启动器' },
        [pscustomobject]@{ Key = 'launcherTemp'; Path = [string] $Journal.launcherTempPath; Existed = [bool] $Journal.launcherTempExisted; BackupPath = [string] $Journal.backupLauncherTempPath; CandidateSha256 = $candidateHash; Label = '桌面启动器暂存文件' },
        [pscustomobject]@{ Key = 'legacyLauncher'; Path = [string] $Journal.legacyLauncherPath; Existed = [bool] $Journal.legacyLauncherExisted; BackupPath = [string] $Journal.backupLegacyLauncherPath; CandidateSha256 = $null; Label = '遗留桌面启动器' }
    )
    $conflicts = [System.Collections.Generic.List[object]]::new()
    foreach ($specification in $specifications) {
        $ownership = Test-CountdownInstallFileRestoreOwnership -Path $specification.Path -Existed $specification.Existed -BackupPath $specification.BackupPath -CandidateSha256 $specification.CandidateSha256 -Label $specification.Label
        if (-not [bool] $ownership.Owned) {
            [void] $conflicts.Add([pscustomobject]@{ Key = $specification.Key; Label = $specification.Label; Path = $ownership.Path })
        }
    }
    $conflicts.ToArray()
}

function Assert-CountdownInstallPayloadRestoreOwnership {
    param([Parameter(Mandatory)] [object] $Journal)

    if (-not [bool] $Journal.payloadSnapshotReady) { return }
    $candidateHash = [string] $Journal.candidateLauncherSha256
    Assert-CountdownInstallFileRestoreOwnership -Path ([string] $Journal.launcherPath) -Existed ([bool] $Journal.launcherExisted) -BackupPath ([string] $Journal.backupLauncherPath) -CandidateSha256 $candidateHash -Label '桌面启动器'
    Assert-CountdownInstallFileRestoreOwnership -Path ([string] $Journal.launcherTempPath) -Existed ([bool] $Journal.launcherTempExisted) -BackupPath ([string] $Journal.backupLauncherTempPath) -CandidateSha256 $candidateHash -Label '桌面启动器暂存文件'
    Assert-CountdownInstallFileRestoreOwnership -Path ([string] $Journal.legacyLauncherPath) -Existed ([bool] $Journal.legacyLauncherExisted) -BackupPath ([string] $Journal.backupLegacyLauncherPath) -Label '遗留桌面启动器'
}

function Restore-CountdownInstallTree {
    param([Parameter(Mandatory)] [object] $Journal)

    $install = Assert-CountdownInstallPathOnDDrive -Path ([string] $Journal.installPath)
    if ([bool] $Journal.installExisted) {
        if (-not (Test-Path -LiteralPath ([string] $Journal.backupInstallPath) -PathType Container)) {
            throw "安装事务缺少安装目录备份：$($Journal.backupInstallPath)"
        }
    }
    if (Test-Path -LiteralPath $install) { Remove-Item -LiteralPath $install -Recurse -Force }
    if ([bool] $Journal.installExisted) {
        New-Item -ItemType Directory -Path $install -Force | Out-Null
        Copy-CountdownInstallDirectoryContents -Source ([string] $Journal.backupInstallPath) -Destination $install
    }
}

function Restore-CountdownInstallFile {
    param(
        [Parameter(Mandatory)] [string] $Path,
        [Parameter(Mandatory)] [bool] $Existed,
        [Parameter(Mandatory)] [string] $BackupPath
    )

    $resolved = Assert-CountdownInstallPathOnDDrive -Path $Path
    if ($Existed) {
        if (-not (Test-Path -LiteralPath $BackupPath -PathType Leaf)) { throw "安装事务备份文件缺失：$BackupPath" }
    }
    if (Test-Path -LiteralPath $resolved) { Remove-Item -LiteralPath $resolved -Force }
    if ($Existed) {
        Copy-Item -LiteralPath $BackupPath -Destination $resolved -Force
    }
}

function Undo-CountdownInstallTransaction {
    param(
        [Parameter(Mandatory)] [string] $TransactionPath,
        [Parameter(Mandatory)] [string] $PowerShell7Path,
        [scriptblock] $RestoreWindowsTimeAction,
        [string] $ExpectedInstallPath,
        [string] $ExpectedTaskName,
        [string] $ExpectedLegacyTaskName,
        [string] $ExpectedLauncherPath,
        [string] $ExpectedLegacyLauncherPath,
        [string] $ExpectedLauncherTempPath,
        [switch] $PreserveUnmanagedLaunchers
    )

    # 先把已存在任务的 XML 解析进内存；之后的 Stop/Unregister 绝不再读取可被截断的备份文件。
    $validatedTaskXml = @{}
    $journal = Get-CountdownInstallTransactionJournal -TransactionPath $TransactionPath -ValidatedTaskXml $validatedTaskXml
    Assert-CountdownInstallTransactionIdentity -Journal $journal -ExpectedInstallPath $ExpectedInstallPath -ExpectedTaskName $ExpectedTaskName -ExpectedLegacyTaskName $ExpectedLegacyTaskName -ExpectedLauncherPath $ExpectedLauncherPath -ExpectedLegacyLauncherPath $ExpectedLegacyLauncherPath -ExpectedLauncherTempPath $ExpectedLauncherTempPath
    $basePath = Get-CountdownTransactionBasePath -InstallPath ([string] $journal.installPath)
    $ownershipConflicts = @(Get-CountdownInstallPayloadRestoreOwnershipConflicts -Journal $journal)
    if ($ownershipConflicts.Count -gt 0 -and -not $PreserveUnmanagedLaunchers) {
        $firstConflict = $ownershipConflicts[0]
        throw "拒绝恢复 $($firstConflict.Label)：当前文件不属于事务候选、原始备份或缺失状态：$($firstConflict.Path)"
    }
    $conflictKeys = @($ownershipConflicts | ForEach-Object { [string] $_.Key })
    Set-CountdownInstallTransactionPhase -TransactionPath $TransactionPath -Phase 'rolling-back' | Out-Null
    try {
        Stop-CountdownManagedApplication -InstallPath ([string] $journal.installPath) -TaskName ([string] $journal.taskName) -LegacyTaskName ([string] $journal.legacyTaskName) -FreezeFutureStarts
        if ([bool] $journal.payloadSnapshotReady) {
            Restore-CountdownInstallTree -Journal $journal
            if (-not ($conflictKeys -contains 'launcher')) {
                Restore-CountdownInstallFile -Path ([string] $journal.launcherPath) -Existed ([bool] $journal.launcherExisted) -BackupPath ([string] $journal.backupLauncherPath)
            }
            if (-not ($conflictKeys -contains 'launcherTemp')) {
                Restore-CountdownInstallFile -Path ([string] $journal.launcherTempPath) -Existed ([bool] $journal.launcherTempExisted) -BackupPath ([string] $journal.backupLauncherTempPath)
            }
            if (-not ($conflictKeys -contains 'legacyLauncher')) {
                Restore-CountdownInstallFile -Path ([string] $journal.legacyLauncherPath) -Existed ([bool] $journal.legacyLauncherExisted) -BackupPath ([string] $journal.backupLegacyLauncherPath)
            }
        }
        Restore-CountdownScheduledTaskSnapshot -TaskName ([string] $journal.taskName) -Snapshot $journal.currentTask -PowerShell7Path $PowerShell7Path -ValidatedTaskXml $validatedTaskXml['currentTask']
        Restore-CountdownScheduledTaskSnapshot -TaskName ([string] $journal.legacyTaskName) -Snapshot $journal.legacyTask -PowerShell7Path $PowerShell7Path -ValidatedTaskXml $validatedTaskXml['legacyTask']
        if ($null -eq $RestoreWindowsTimeAction) {
            Restore-CountdownWindowsTimeSnapshot -Snapshot $journal.windowsTime
        }
        else {
            & $RestoreWindowsTimeAction $journal.windowsTime
        }
        if ($ownershipConflicts.Count -gt 0) {
            # 应用、任务和时间服务已恢复；保留被用户替换的桌面文件和 journal，等待人工确认后再完成清理。
            Set-CountdownInstallTransactionPhase -TransactionPath $TransactionPath -Phase 'launcher-ownership-conflict' | Out-Null
            return [pscustomobject]@{ PreservedUnmanagedLaunchers = $true; Conflicts = $ownershipConflicts }
        }
        Set-CountdownInstallTransactionPhase -TransactionPath $TransactionPath -Phase 'rolled-back' | Out-Null
        # 回滚已经完成后，删除备份只是卫生动作；保留 rolled-back journal 供下次安全清理，不能把成功回滚误报为失败。
        try { Remove-Item -LiteralPath $TransactionPath -Recurse -Force -ErrorAction Stop } catch { }
        Remove-CountdownEmptyTransactionBase -BasePath $basePath
    }
    catch {
        try { Set-CountdownInstallTransactionPhase -TransactionPath $TransactionPath -Phase 'rollback-failed' | Out-Null } catch { }
        throw
    }
}

function Complete-CountdownInstallTransaction {
    param(
        [Parameter(Mandatory)] [string] $TransactionPath,
        [string] $ExpectedInstallPath,
        [string] $ExpectedTaskName,
        [string] $ExpectedLegacyTaskName,
        [string] $ExpectedLauncherPath,
        [string] $ExpectedLegacyLauncherPath,
        [string] $ExpectedLauncherTempPath
    )

    $journal = Get-CountdownInstallTransactionJournal -TransactionPath $TransactionPath
    Assert-CountdownInstallTransactionIdentity -Journal $journal -ExpectedInstallPath $ExpectedInstallPath -ExpectedTaskName $ExpectedTaskName -ExpectedLegacyTaskName $ExpectedLegacyTaskName -ExpectedLauncherPath $ExpectedLauncherPath -ExpectedLegacyLauncherPath $ExpectedLegacyLauncherPath -ExpectedLauncherTempPath $ExpectedLauncherTempPath
    if (-not [bool] $journal.payloadSnapshotReady) { throw '安装事务尚未捕获静止后的安装文件，拒绝提交。' }
    $candidateLauncherHash = [string] $journal.candidateLauncherSha256
    if ([string]::IsNullOrWhiteSpace($candidateLauncherHash)) {
        throw '安装事务尚未记录候选桌面启动器哈希，拒绝提交。'
    }
    $launcherPath = Assert-CountdownInstallPathOnDDrive -Path ([string] $journal.launcherPath)
    if (-not (Test-Path -LiteralPath $launcherPath -PathType Leaf)) {
        throw '候选桌面启动器在提交前不存在，拒绝提交。'
    }
    $actualLauncherHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $launcherPath).Hash
    if (-not [string]::Equals($actualLauncherHash, $candidateLauncherHash, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw '候选桌面启动器在提交前已被替换，拒绝提交。'
    }
    $basePath = Get-CountdownTransactionBasePath -InstallPath ([string] $journal.installPath)
    Set-CountdownInstallTransactionPhase -TransactionPath $TransactionPath -Phase 'committed' | Out-Null
    # committed 是不可逆状态；清理失败必须留给后续恢复清扫，不能重新进入回滚路径。
    try { Remove-Item -LiteralPath $TransactionPath -Recurse -Force -ErrorAction Stop } catch { }
    Remove-CountdownEmptyTransactionBase -BasePath $basePath
}

function Recover-CountdownInstallTransactions {
    param(
        [Parameter(Mandatory)] [string] $InstallPath,
        [Parameter(Mandatory)] [string] $PowerShell7Path,
        [string] $ExpectedTaskName,
        [string] $ExpectedLegacyTaskName,
        [string] $ExpectedLauncherPath,
        [string] $ExpectedLegacyLauncherPath,
        [string] $ExpectedLauncherTempPath
    )

    $recoveryMutex = $null
    try {
        # 同线程重入由命名 mutex 自身保证；不要暴露任何可伪造的“已持锁”开关。
        $recoveryMutex = Enter-CountdownInstallTransactionMutex -TimeoutSeconds 120
        $install = Assert-CountdownInstallPathOnDDrive -Path $InstallPath
        $basePath = Get-CountdownTransactionBasePath -InstallPath $install
        $recovered = [System.Collections.Generic.List[string]]::new()
        $unresolvedLauncherOwnershipConflicts = [System.Collections.Generic.List[object]]::new()
        if (-not (Test-Path -LiteralPath $basePath -PathType Container)) {
            return [pscustomobject]@{
                RecoveredTransactionPaths = @()
                UnresolvedLauncherOwnershipConflicts = @()
            }
        }
        foreach ($directory in @(Get-ChildItem -LiteralPath $basePath -Directory -Force)) {
            $journalPath = Join-Path $directory.FullName 'journal.json'
            if (-not (Test-Path -LiteralPath $journalPath -PathType Leaf)) { continue }
            $journal = Get-CountdownInstallTransactionJournal -TransactionPath $directory.FullName
            if (-not [string]::Equals((ConvertTo-CountdownInstallFullPath -Path ([string] $journal.installPath)), $install, [System.StringComparison]::OrdinalIgnoreCase)) { continue }
            if ([string] $journal.phase -in @('committed', 'rolled-back')) {
                try { Remove-Item -LiteralPath $directory.FullName -Recurse -Force -ErrorAction Stop } catch { }
                continue
            }
            if ([string] $journal.phase -eq 'launcher-ownership-conflict') {
                # 用户替换仍在时不得再次冻结任务或重写其桌面文件；待其恢复受管版本后再完成普通回滚。
                $ownershipConflicts = @(Get-CountdownInstallPayloadRestoreOwnershipConflicts -Journal $journal)
                if ($ownershipConflicts.Count -gt 0) {
                    [void] $unresolvedLauncherOwnershipConflicts.Add([pscustomobject]@{
                            TransactionPath = $directory.FullName
                            Conflicts = $ownershipConflicts
                        })
                    continue
                }
            }
            Undo-CountdownInstallTransaction -TransactionPath $directory.FullName -PowerShell7Path $PowerShell7Path -ExpectedInstallPath $install -ExpectedTaskName $ExpectedTaskName -ExpectedLegacyTaskName $ExpectedLegacyTaskName -ExpectedLauncherPath $ExpectedLauncherPath -ExpectedLegacyLauncherPath $ExpectedLegacyLauncherPath -ExpectedLauncherTempPath $ExpectedLauncherTempPath
            [void] $recovered.Add($directory.FullName)
        }
        Remove-CountdownEmptyTransactionBase -BasePath $basePath
        [pscustomobject]@{
            RecoveredTransactionPaths = $recovered.ToArray()
            UnresolvedLauncherOwnershipConflicts = $unresolvedLauncherOwnershipConflicts.ToArray()
        }
    }
    finally {
        if ($null -ne $recoveryMutex) {
            Exit-CountdownInstallTransactionMutex -Mutex $recoveryMutex
        }
    }
}

Export-ModuleMember -Function Enter-CountdownInstallTransactionMutex, Exit-CountdownInstallTransactionMutex, Get-CountdownInteractiveDesktopContext, Write-CountdownDeferredInstallReceipt, Read-CountdownDeferredInstallReceipt, Start-CountdownInstallTransaction, Capture-CountdownInstallPayloadSnapshot, Set-CountdownInstallTransactionCandidateLauncherHash, Complete-CountdownInstallTransaction, Undo-CountdownInstallTransaction, Recover-CountdownInstallTransactions, Stop-CountdownManagedApplication, Test-CountdownManagedApplicationStopped
