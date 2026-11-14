#requires -Version 7.0

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# 新安装器（#259）：只替换程序目录。安装根下只动现役程序目录 app、候选 app.candidate、旧版本 app.previous、
# 事务标记 install-transaction.json 与运行状态回执 runtime-status.json；设置、设置备份、恢复证据、诊断日志和节假日缓存
# 不被移动、复制或改写。计划任务、进程、交互会话与目录改名经 port 调用：生产用 New-CountdownInstallerWindowsPorts，
# 测试注入假 port。本机与远端是同一个安装器；它不运行测试套件，不检查构建工具链，不触碰 Windows 时间服务。
# #280 只走成功路径：设置保护（#282）、任务与快捷方式的完整登记（#281）、失败回滚（#283）与中断恢复（#284）在后续票。

# 与旧安装器（Countdown.InstallTransaction.psm1）同名：新旧安装器在同一台机器上互斥。
$script:InstallLockName = 'Global\MinminCountdownInstallTransaction-v1'
$script:ApplicationProcessName = 'MinminCountdown.exe'
$script:ExecutableManifestPath = 'app\MinminCountdown.exe'
$script:Utf8NoBom = [System.Text.UTF8Encoding]::new($false)
# 设置及其两代备份（docs/设置与持久化.md）：快照与迁移演练的对象，安装器从不写安装根里的这三份文件。
$script:SettingsFamilyNames = @('settings.json', 'settings.json.backup1', 'settings.json.backup2')

function New-CountdownInstallRejection {
    param(
        [Parameter(Mandatory)] [string] $Reason,
        [Parameter(Mandatory)] [string] $Message
    )

    $exception = [System.InvalidOperationException]::new($Message)
    $exception.Data['CountdownInstallReason'] = $Reason
    $exception
}

function Get-CountdownInstallerProperty {
    param(
        [AllowNull()] [object] $Object,
        [Parameter(Mandatory)] [string] $Name
    )

    if ($null -eq $Object) { return $null }
    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property) { return $null }
    $property.Value
}

function Resolve-CountdownInstallerPath {
    param([Parameter(Mandatory)] [string] $Path)

    [System.IO.Path]::TrimEndingDirectorySeparator(
        [System.IO.Path]::GetFullPath($ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Path)))
}

function Test-CountdownReparsePoint {
    param([Parameter(Mandatory)] [string] $Path)

    (Test-Path -LiteralPath $Path) -and
        (([System.IO.File]::GetAttributes($Path) -band [System.IO.FileAttributes]::ReparsePoint) -ne 0)
}

# 安装路径：本机盘符下的完整路径，不是盘符根，不是 reparse point；不存在时父目录必须存在（首次安装只新建安装根这一层）。
function Resolve-CountdownInstallerLayout {
    param([Parameter(Mandatory)] [string] $InstallRoot)

    $invalid = { param([string] $Detail) New-CountdownInstallRejection -Reason 'invalid-install-path' -Message "安装路径不可用（$Detail）：$InstallRoot" }
    try { $root = Resolve-CountdownInstallerPath -Path $InstallRoot }
    catch { throw (& $invalid '无法解析') }
    if ($root -notmatch '^[A-Za-z]:\\') { throw (& $invalid '不是本机盘符下的完整路径') }
    if ([string]::Equals($root, [System.IO.Path]::GetPathRoot($root), [System.StringComparison]::OrdinalIgnoreCase)) { throw (& $invalid '不能是盘符根') }
    if (Test-Path -LiteralPath $root) {
        if (-not (Test-Path -LiteralPath $root -PathType Container)) { throw (& $invalid '同名文件已存在') }
    }
    elseif (-not (Test-Path -LiteralPath ([System.IO.Path]::GetDirectoryName($root)) -PathType Container)) {
        throw (& $invalid '父目录不存在')
    }
    $layout = [pscustomobject]@{
        Root = $root
        ProgramDirectory = Join-Path $root 'app'
        CandidateDirectory = Join-Path $root 'app.candidate'
        PreviousDirectory = Join-Path $root 'app.previous'
        MarkerPath = Join-Path $root 'install-transaction.json'
        TransactionDirectory = Join-Path $root 'install-transaction'
        RuntimeStatusPath = Join-Path $root 'runtime-status.json'
        SettingsPath = Join-Path $root 'settings.json'
        ExecutablePath = Join-Path $root 'app\MinminCountdown.exe'
    }
    foreach ($path in @($layout.Root, $layout.ProgramDirectory)) {
        if (Test-CountdownReparsePoint -Path $path) { throw (& $invalid "reparse point：$path") }
    }
    $layout
}

function Enter-CountdownInstallLock {
    param([Parameter(Mandatory)] [string] $Name)

    $mutex = [System.Threading.Mutex]::new($false, $Name)
    $acquired = $false
    try { $acquired = $mutex.WaitOne(0) }
    catch [System.Threading.AbandonedMutexException] {
        # 上一个安装进程中断时仍取得锁；它留下的状态由恢复处理。
        $acquired = $true
    }
    if (-not $acquired) {
        $mutex.Dispose()
        throw (New-CountdownInstallRejection -Reason 'install-in-progress' -Message '另一个安装正在进行，拒绝本次安装。')
    }
    $mutex
}

function Exit-CountdownInstallLock {
    param([AllowNull()] [System.Threading.Mutex] $Mutex)

    if ($null -eq $Mutex) { return }
    try { $Mutex.ReleaseMutex() } catch [System.ApplicationException] { }
    finally { $Mutex.Dispose() }
}

# 设置指纹与状态命令（#276）同形：长度、修改时间与 SHA-256 取自同一个共享读写删除打开的文件，不含内容。
function Get-CountdownSettingsFingerprint {
    param([Parameter(Mandatory)] [string] $Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        return [ordered]@{ present = $false; length = $null; lastWriteTimeUtc = $null; sha256 = $null }
    }
    $stream = [System.IO.File]::Open($Path, 'Open', 'Read', 'ReadWrite, Delete')
    try {
        [ordered]@{
            present = $true
            length = $stream.Length
            lastWriteTimeUtc = [System.IO.File]::GetLastWriteTimeUtc($stream.SafeFileHandle).ToString('o')
            sha256 = [Convert]::ToHexString([System.Security.Cryptography.SHA256]::HashData($stream))
        }
    }
    finally { $stream.Dispose() }
}

# 上一次安装没走完时留下的东西。恢复表在 #284 实现；之前一律拒绝，不在半成品状态上继续改。
function Assert-CountdownNoUnfinishedInstall {
    param([Parameter(Mandatory)] [object] $Layout)

    $leftovers = @(
        foreach ($path in @($Layout.MarkerPath, $Layout.CandidateDirectory, $Layout.PreviousDirectory, $Layout.TransactionDirectory)) {
            if (Test-Path -LiteralPath $path) { [System.IO.Path]::GetFileName($path) }
        }
    )
    if ($leftovers.Count -gt 0) {
        throw (New-CountdownInstallRejection -Reason 'unfinished-install' -Message "安装根留有上次未完成安装的痕迹（$($leftovers -join '、')），中断恢复尚未实现，拒绝安装。")
    }
}

function Get-CountdownSingleInteractiveSession {
    param([Parameter(Mandatory)] [object] $Ports)

    $sessions = @(& $Ports.GetInteractiveSessions | Where-Object { $null -ne $_ })
    if ($sessions.Count -eq 0) {
        throw (New-CountdownInstallRejection -Reason 'no-interactive-session' -Message '没有交互会话：没有用户登录桌面，无法经登录任务启动并验收。')
    }
    if ($sessions.Count -gt 1) {
        throw (New-CountdownInstallRejection -Reason 'multiple-interactive-sessions' -Message "有 $($sessions.Count) 个交互会话，无法确定在哪个会话里启动并验收。")
    }
    $sessions[0]
}

function Test-CountdownProgramManifestPath {
    param([AllowEmptyString()] [string] $Path)

    if ([string]::IsNullOrWhiteSpace($Path) -or $Path.Contains('/') -or
        -not $Path.StartsWith('app\', [System.StringComparison]::OrdinalIgnoreCase)) {
        return $false
    }
    $invalidCharacters = [System.IO.Path]::GetInvalidFileNameChars()
    foreach ($segment in $Path.Substring(4).Split('\')) {
        if ($segment -in @('', '.', '..') -or $segment.IndexOfAny($invalidCharacters) -ge 0) { return $false }
    }
    $true
}

# 载荷清单（构建写在程序目录里的 published-payload.json）：逐项路径与 SHA-256，聚合哈希按清单顺序的“路径\0哈希\n”重算，
# 与构建脚本同一算法，文件列表被截断或改动时对不上。
function Read-CountdownPayloadManifest {
    param([Parameter(Mandatory)] [string] $ProgramDirectory)

    $manifestPath = Join-Path $ProgramDirectory 'published-payload.json'
    $mismatch = { param([string] $Detail) New-CountdownInstallRejection -Reason 'payload-mismatch' -Message "包内文件与载荷清单不符：$Detail" }
    if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) { throw (& $mismatch "没有载荷清单 $manifestPath") }
    try { $document = [System.IO.File]::ReadAllText($manifestPath) | ConvertFrom-Json }
    catch { throw (& $mismatch '载荷清单不是有效 JSON') }
    if ($null -eq $document -or (Get-CountdownInstallerProperty -Object $document -Name 'schemaVersion') -ne 1) {
        throw (& $mismatch '载荷清单的 schemaVersion 不是 1')
    }
    $entries = [System.Collections.Generic.List[object]]::new()
    $seen = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    $aggregateInput = [System.Text.StringBuilder]::new()
    foreach ($file in @(Get-CountdownInstallerProperty -Object $document -Name 'files')) {
        $path = [string] (Get-CountdownInstallerProperty -Object $file -Name 'path')
        $sha256 = [string] (Get-CountdownInstallerProperty -Object $file -Name 'sha256')
        if (-not (Test-CountdownProgramManifestPath -Path $path) -or $sha256 -notmatch '^[0-9A-Fa-f]{64}$' -or -not $seen.Add($path)) {
            throw (& $mismatch "清单条目无效：$path")
        }
        $entries.Add([pscustomobject]@{ Path = $path; RelativePath = $path.Substring(4); Sha256 = $sha256.ToUpperInvariant() })
        [void] $aggregateInput.Append($path).Append([char] 0).Append($sha256).Append([char] 10)
    }
    if ($entries.Count -eq 0) { throw (& $mismatch '载荷清单没有文件') }
    if (-not $seen.Contains($script:ExecutableManifestPath)) { throw (& $mismatch "载荷清单没有 $($script:ExecutableManifestPath)") }
    $aggregate = [Convert]::ToHexString([System.Security.Cryptography.SHA256]::HashData([System.Text.Encoding]::UTF8.GetBytes($aggregateInput.ToString())))
    if (-not [string]::Equals($aggregate, [string] (Get-CountdownInstallerProperty -Object $document -Name 'aggregateSha256'), [System.StringComparison]::OrdinalIgnoreCase)) {
        throw (& $mismatch '聚合哈希与清单的文件列表不一致')
    }
    $commit = [string] (Get-CountdownInstallerProperty -Object $document -Name 'commit')
    [pscustomobject]@{
        Path = $manifestPath
        Commit = if ([string]::IsNullOrWhiteSpace($commit)) { $null } else { $commit }
        AggregateSha256 = $aggregate
        Files = $entries.ToArray()
        ExecutableSha256 = @($entries | Where-Object { [string]::Equals($_.Path, $script:ExecutableManifestPath, [System.StringComparison]::OrdinalIgnoreCase) })[0].Sha256
    }
}

function Assert-CountdownProgramDirectoryMatchesManifest {
    param(
        [Parameter(Mandatory)] [string] $Directory,
        [Parameter(Mandatory)] [object] $Manifest
    )

    foreach ($entry in $Manifest.Files) {
        $path = Join-Path $Directory $entry.RelativePath
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
            throw (New-CountdownInstallRejection -Reason 'payload-mismatch' -Message "包内文件与载荷清单不符：缺少 $($entry.Path)")
        }
        if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $entry.Sha256) {
            throw (New-CountdownInstallRejection -Reason 'payload-mismatch' -Message "包内文件与载荷清单不符：$($entry.Path) 的 SHA-256 不同")
        }
    }
}

# 候选程序目录只含清单列出的文件和清单本身；包里清单之外的文件不装。
function Copy-CountdownProgramCandidate {
    param(
        [Parameter(Mandatory)] [string] $SourceDirectory,
        [Parameter(Mandatory)] [string] $CandidateDirectory,
        [Parameter(Mandatory)] [object] $Manifest
    )

    [void] [System.IO.Directory]::CreateDirectory($CandidateDirectory)
    foreach ($entry in $Manifest.Files) {
        $source = Join-Path $SourceDirectory $entry.RelativePath
        if (-not (Test-Path -LiteralPath $source -PathType Leaf)) {
            throw (New-CountdownInstallRejection -Reason 'payload-mismatch' -Message "包内文件与载荷清单不符：缺少 $($entry.Path)")
        }
        $destination = Join-Path $CandidateDirectory $entry.RelativePath
        [void] [System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName($destination))
        [System.IO.File]::Copy($source, $destination)
    }
    [System.IO.File]::Copy($Manifest.Path, (Join-Path $CandidateDirectory 'published-payload.json'))
}

function Get-CountdownInstalledProcesses {
    param(
        [Parameter(Mandatory)] [object] $Layout,
        [Parameter(Mandatory)] [object] $Ports
    )

    @(& $Ports.GetProcesses $script:ApplicationProcessName | Where-Object {
            $path = [string] (Get-CountdownInstallerProperty -Object $_ -Name 'ExecutablePath')
            -not [string]::IsNullOrWhiteSpace($path) -and
                [string]::Equals([System.IO.Path]::GetFullPath($path), $Layout.ExecutablePath, [System.StringComparison]::OrdinalIgnoreCase)
        })
}

function Test-CountdownTaskRunning {
    param([AllowNull()] [object] $Task)

    $null -ne $Task -and [string] (Get-CountdownInstallerProperty -Object $Task -Name 'State') -eq 'Running'
}

function New-CountdownTaskStillRunningRejection {
    param([Parameter(Mandatory)] [string] $TaskName)

    New-CountdownInstallRejection -Reason 'login-task-still-running' -Message "登录任务 $TaskName 仍有运行中的实例：经它启动会被「已在运行时忽略新实例」悄悄拒绝，等不到新的运行状态回执。"
}

# 结束前按 PID 重新读一次映像路径，仍是已安装 EXE 才结束：枚举之后进程可能已经退出，PID 也可能已被别的进程复用。
function Stop-CountdownInstalledProcess {
    param(
        [Parameter(Mandatory)] [object] $Layout,
        [Parameter(Mandatory)] [object] $Ports,
        [Parameter(Mandatory)] [int] $ProcessId
    )

    if (@(Get-CountdownInstalledProcesses -Layout $Layout -Ports $Ports | Where-Object { [int] $_.ProcessId -eq $ProcessId }).Count -gt 0) {
        [void] (& $Ports.StopProcess $ProcessId)
    }
}

# 停止应用：结束映像路径是已安装 EXE 的进程，直到它们都退出，并且登录任务没有运行中的实例。不经计划任务停止：
# #279 实测停止任务会让应用走崩溃路径（日志留下致命异常，退出码 1），也停不掉在任务之外启动的实例。
# 任务实例还在时经任务启动会被 IgnoreNew 拒绝，所以这里一并等它结束。未保存的编辑草稿会丢失，与现有安装器相同。
# 返回开始时在运行的应用进程数。
function Stop-CountdownInstalledApplication {
    param(
        [Parameter(Mandatory)] [object] $Layout,
        [Parameter(Mandatory)] [string] $TaskName,
        [Parameter(Mandatory)] [object] $Ports,
        [Parameter(Mandatory)] [int] $TimeoutSeconds
    )

    $running = $null
    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    while ($true) {
        $remaining = @(Get-CountdownInstalledProcesses -Layout $Layout -Ports $Ports)
        if ($null -eq $running) { $running = $remaining.Count }
        $taskRunning = Test-CountdownTaskRunning -Task (& $Ports.GetTask $TaskName)
        if ($remaining.Count -eq 0 -and -not $taskRunning) { return $running }
        if ([DateTime]::UtcNow -ge $deadline) {
            if ($remaining.Count -eq 0) { throw (New-CountdownTaskStillRunningRejection -TaskName $TaskName) }
            throw (New-CountdownInstallRejection -Reason 'application-still-running' -Message "$TimeoutSeconds 秒内没能停止应用：仍有 $($remaining.Count) 个已安装 EXE 的进程。")
        }
        foreach ($process in $remaining) {
            Stop-CountdownInstalledProcess -Layout $Layout -Ports $Ports -ProcessId ([int] $process.ProcessId)
        }
        Start-Sleep -Milliseconds 200
    }
}

function Write-CountdownInstallMarker {
    param(
        [Parameter(Mandatory)] [string] $Path,
        [Parameter(Mandatory)] [object] $Manifest
    )

    $marker = [ordered]@{
        schemaVersion = 1
        startedAtUtc = [DateTimeOffset]::UtcNow.ToString('o')
        candidateCommit = $Manifest.Commit
        candidateAggregateSha256 = $Manifest.AggregateSha256
    }
    $temporaryPath = "$Path.tmp"
    [System.IO.File]::WriteAllText($temporaryPath, ($marker | ConvertTo-Json), $script:Utf8NoBom)
    [System.IO.File]::Move($temporaryPath, $Path)
}

function Copy-CountdownSettingsFamily {
    param(
        [Parameter(Mandatory)] [string] $SourceDirectory,
        [Parameter(Mandatory)] [string] $DestinationDirectory
    )

    [void] [System.IO.Directory]::CreateDirectory($DestinationDirectory)
    foreach ($name in $script:SettingsFamilyNames) {
        $source = Join-Path $SourceDirectory $name
        if (Test-Path -LiteralPath $source -PathType Leaf) { [System.IO.File]::Copy($source, (Join-Path $DestinationDirectory $name)) }
    }
}

# 事务目录 install-transaction\ 在写下事务标记之后建立，提交或撤回本次安装时整个删除。settings-snapshot\ 是设置及其两代
# 备份在本次安装前的逐字节快照，不在快照里的文件安装前就不存在；它只供本次安装在新版本达到启动就绪之前恢复设置（#283）。
function Save-CountdownSettingsSnapshot {
    param([Parameter(Mandatory)] [object] $Layout)

    $snapshotDirectory = Join-Path $Layout.TransactionDirectory 'settings-snapshot'
    Copy-CountdownSettingsFamily -SourceDirectory $Layout.Root -DestinationDirectory $snapshotDirectory
    $snapshotDirectory
}

# 经 port 运行候选 EXE 的一个无界面命令；stdout 应是一份 JSON 报告，读不出时 Report 为 $null。
function Invoke-CountdownCandidateCommand {
    param(
        [Parameter(Mandatory)] [object] $Ports,
        [Parameter(Mandatory)] [string] $ExecutablePath,
        [Parameter(Mandatory)] [string] $WorkingDirectory,
        [Parameter(Mandatory)] [string[]] $Arguments
    )

    $run = & $Ports.RunHeadlessCommand $ExecutablePath $Arguments $WorkingDirectory
    $report = $null
    try { $report = [string] (Get-CountdownInstallerProperty -Object $run -Name 'Output') | ConvertFrom-Json -DateKind String }
    catch { $report = $null }
    [pscustomobject]@{ ExitCode = Get-CountdownInstallerProperty -Object $run -Name 'ExitCode'; Report = $report }
}

# 迁移演练：在事务目录 settings-rehearsal\ 里的设置副本上运行候选 EXE 的无界面迁移（#278），再用候选 EXE 的等价判定（#272）
# 核对快照与迁移结果，迁移命令报告的迁移时刻原样交给判定。没有设置文件时跳过（首次安装）。Report 是结果 JSON 里的演练
# 结论，只含原因类别，不含设置内容或命令消息；不等价或演练没能完成时另带 Rejection，调用方据此撤回本次安装。
function Invoke-CountdownSettingsRehearsal {
    param(
        [Parameter(Mandatory)] [object] $Layout,
        [Parameter(Mandatory)] [string] $SnapshotDirectory,
        [Parameter(Mandatory)] [object] $Ports
    )

    $report = [ordered]@{ outcome = 'skipped-no-settings'; migration = $null; equivalence = $null }
    $snapshotSettings = Join-Path $SnapshotDirectory 'settings.json'
    if (-not (Test-Path -LiteralPath $snapshotSettings -PathType Leaf)) { return [pscustomobject]@{ Report = $report; Rejection = $null } }

    $rehearsalDirectory = Join-Path $Layout.TransactionDirectory 'settings-rehearsal'
    $rehearsalSettings = Join-Path $rehearsalDirectory 'settings.json'
    $candidateExecutable = Join-Path $Layout.CandidateDirectory 'MinminCountdown.exe'
    Copy-CountdownSettingsFamily -SourceDirectory $SnapshotDirectory -DestinationDirectory $rehearsalDirectory
    $failed = {
        param([string] $Detail)
        $report.outcome = 'failed'
        [pscustomobject]@{
            Report = $report
            Rejection = New-CountdownInstallRejection -Reason 'settings-rehearsal-failed' -Message "设置迁移演练没能完成（$Detail）：没有替换程序，现役设置未被改动。"
        }
    }
    $category = { param([object] $Run) $value = [string] (Get-CountdownInstallerProperty -Object $Run.Report -Name 'reasonCategory'); if ($value) { $value } else { 'no-report' } }

    try { $migration = Invoke-CountdownCandidateCommand -Ports $Ports -ExecutablePath $candidateExecutable -WorkingDirectory $rehearsalDirectory -Arguments @('--settings-migration', '--settings-file', $rehearsalSettings) }
    catch { return (& $failed "迁移命令：$($_.Exception.GetBaseException().Message)") }
    $report.migration = & $category $migration
    $migrationUtc = [string] (Get-CountdownInstallerProperty -Object $migration.Report -Name 'migrationUtc')
    if ($migration.ExitCode -ne 0 -or (Get-CountdownInstallerProperty -Object $migration.Report -Name 'succeeded') -ne $true -or [string]::IsNullOrWhiteSpace($migrationUtc)) {
        return (& $failed "迁移命令退出码 $($migration.ExitCode)，$($report.migration)")
    }

    try { $equivalence = Invoke-CountdownCandidateCommand -Ports $Ports -ExecutablePath $candidateExecutable -WorkingDirectory $rehearsalDirectory -Arguments @('--settings-equivalence', '--before-settings', $snapshotSettings, '--after-settings', $rehearsalSettings, '--migration-utc', $migrationUtc) }
    catch { return (& $failed "等价判定：$($_.Exception.GetBaseException().Message)") }
    $report.equivalence = & $category $equivalence
    $equivalent = Get-CountdownInstallerProperty -Object $equivalence.Report -Name 'equivalent'
    if ($equivalence.ExitCode -eq 0 -and $equivalent -eq $true) {
        $report.outcome = 'equivalent'
        return [pscustomobject]@{ Report = $report; Rejection = $null }
    }
    if ($equivalence.ExitCode -eq 1 -and $equivalent -eq $false) {
        $report.outcome = 'not-equivalent'
        return [pscustomobject]@{
            Report = $report
            Rejection = New-CountdownInstallRejection -Reason 'settings-not-equivalent' -Message "候选版本对现役设置的迁移不等价（$($report.equivalence)）：没有替换程序，现役设置未被改动。"
        }
    }
    & $failed "等价判定退出码 $($equivalence.ExitCode)，$($report.equivalence)"
}

# 换程序之前撤回本次安装（设置快照或迁移演练失败、迁移不等价）：先清除事务标记，再删除候选和事务目录，然后经登录任务重新
# 启动旧版本，等它达到启动就绪。此时程序目录与现役设置都没被动过；清除标记之前中断时，标记、候选和现役的旧版本都还在，
# 按恢复表删除候选即可（#284）。各项清理互不依赖，哪一项失败都照样重新启动旧版本。首次安装没有旧版本或登录任务，不启动。
# 程序目录没被动过，所以就绪核对的 EXE 哈希取现役 EXE 本身。返回重新启动的进程（没有启动时为 $null）与遇到的问题。
function Undo-CountdownInstallBeforeReplace {
    param(
        [Parameter(Mandatory)] [object] $Layout,
        [Parameter(Mandatory)] [string] $TaskName,
        [Parameter(Mandatory)] [object] $Session,
        [Parameter(Mandatory)] [object] $Ports,
        [Parameter(Mandatory)] [int] $ReadyTimeoutSeconds
    )

    $problems = [System.Collections.Generic.List[string]]::new()
    foreach ($path in @($Layout.MarkerPath, $Layout.CandidateDirectory, $Layout.TransactionDirectory)) {
        try {
            if (Test-Path -LiteralPath $path -PathType Container) { [System.IO.Directory]::Delete($path, $true) }
            elseif (Test-Path -LiteralPath $path) { [System.IO.File]::Delete($path) }
        }
        catch { $problems.Add("删除 $([System.IO.Path]::GetFileName($path)) 失败：$($_.Exception.Message)") }
    }

    $restarted = $null
    try {
        if ((Test-Path -LiteralPath $Layout.ExecutablePath -PathType Leaf) -and $null -ne (& $Ports.GetTask $TaskName)) {
            if (Test-CountdownTaskRunning -Task (& $Ports.GetTask $TaskName)) { throw (New-CountdownTaskStillRunningRejection -TaskName $TaskName) }
            if (Test-Path -LiteralPath $Layout.RuntimeStatusPath) { [System.IO.File]::Delete($Layout.RuntimeStatusPath) }
            [void] (& $Ports.StartTask $TaskName)
            $installedSha256 = (Get-FileHash -LiteralPath $Layout.ExecutablePath -Algorithm SHA256).Hash
            $process = Wait-CountdownStartupReady -Layout $Layout -SessionId ([int] $Session.SessionId) -ExpectedExecutableSha256 $installedSha256 -Ports $Ports -TimeoutSeconds $ReadyTimeoutSeconds
            $restarted = [ordered]@{ processId = [int] $process.ProcessId; sessionId = [int] $process.SessionId }
        }
    }
    catch { $problems.Add("重新启动旧版本失败：$($_.Exception.Message)") }
    [pscustomobject]@{ RestartedProcess = $restarted; Problems = $problems.ToArray() }
}

# 登录任务的定义（#279「登录任务的最终定义」），其中状态命令（#276）核对的各项与它的期望一致：登录时触发，交互式、
# 非提升、不限运行时长、已在运行时忽略新实例；动作是已安装 EXE 本身，不带参数，不设工作目录。优先级取 4：#279 实测 4 的
# 优先级类、I/O 与内存优先级都与正常启动一致（5 的内存优先级低一级，默认 7 是 BelowNormal）。电池供电时也启动、拔电不停：
# 计划任务的默认值相反。
function Get-CountdownLoginTaskDefinition {
    param([Parameter(Mandatory)] [string] $ExecutablePath)

    [pscustomobject]@{
        Enabled = $true
        Actions = @([pscustomobject]@{ Type = 'Exec'; Execute = $ExecutablePath; Arguments = $null })
        Triggers = @([pscustomobject]@{ Type = 'Logon'; Enabled = $true })
        LogonType = 'Interactive'
        RunLevel = 'Limited'
        Priority = 4
        ExecutionTimeLimit = 'PT0S'
        MultipleInstances = 'IgnoreNew'
        DisallowStartIfOnBatteries = $false
        StopIfGoingOnBatteries = $false
    }
}

function Read-CountdownRuntimeReceipt {
    param([Parameter(Mandatory)] [string] $Path)

    try {
        # 共享读写删除打开：应用原子替换回执时不被这次读取挡住。不存在、正在替换或半份内容都当作还没有，继续等。
        $stream = [System.IO.File]::Open($Path, 'Open', 'Read', 'ReadWrite, Delete')
        try { $text = [System.IO.StreamReader]::new($stream, $script:Utf8NoBom).ReadToEnd() }
        finally { $stream.Dispose() }
        $text | ConvertFrom-Json
    }
    catch { $null }
}

# 启动就绪：运行状态回执由一个存活进程写出，其映像路径是已安装 EXE，会话是交互会话，EXE 哈希与载荷清单一致。
# 启动前已删除旧回执，回执里的进程身份证明它来自这一次启动；不使用启动令牌，不比较回执的 applicationPath（#279：
# 它仍指向 adapter），也不看任务状态或上次结果。
function Wait-CountdownStartupReady {
    param(
        [Parameter(Mandatory)] [object] $Layout,
        [Parameter(Mandatory)] [int] $SessionId,
        [Parameter(Mandatory)] [string] $ExpectedExecutableSha256,
        [Parameter(Mandatory)] [object] $Ports,
        [Parameter(Mandatory)] [int] $TimeoutSeconds
    )

    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    $reason = '运行状态回执尚未出现。'
    while ($true) {
        $receipt = Read-CountdownRuntimeReceipt -Path $Layout.RuntimeStatusPath
        $processId = Get-CountdownInstallerProperty -Object $receipt -Name 'processId'
        if ($null -eq $receipt) { $reason = '运行状态回执尚未出现。' }
        elseif ([string] (Get-CountdownInstallerProperty -Object $receipt -Name 'stage') -ne 'window-ready' -or $processId -isnot [long] -or $processId -le 0) {
            $reason = '运行状态回执尚未写明首帧已渲染的进程。'
        }
        else {
            $process = @(Get-CountdownInstalledProcesses -Layout $Layout -Ports $Ports | Where-Object { [long] $_.ProcessId -eq $processId })
            if ($process.Count -eq 0) { $reason = "写回执的进程 $processId 不在运行，或映像路径不是已安装 EXE。" }
            elseif ([int] (Get-CountdownInstallerProperty -Object $process[0] -Name 'SessionId') -ne $SessionId) { $reason = "写回执的进程 $processId 不在交互会话里。" }
            elseif ((Get-FileHash -LiteralPath $Layout.ExecutablePath -Algorithm SHA256).Hash -ne $ExpectedExecutableSha256) { $reason = '已安装 EXE 的哈希与载荷清单不一致。' }
            else { return $process[0] }
        }
        if ([DateTime]::UtcNow -ge $deadline) { break }
        Start-Sleep -Milliseconds 250
    }
    throw (New-CountdownInstallRejection -Reason 'startup-not-ready' -Message "$TimeoutSeconds 秒内没有达到启动就绪：$reason")
}

function ConvertTo-CountdownBoundedError {
    param([AllowEmptyString()] [string] $Message)

    $text = ($Message -replace '\s+', ' ').Trim()
    if ($text.Length -gt 400) { $text = $text.Substring(0, 400) + '…' }
    $text
}

function Install-CountdownProgram {
    <#
    .SYNOPSIS
    把包里的程序目录装到安装根：首次安装与升级同一流程，等到启动就绪后提交，返回结果（由入口脚本输出为 JSON）。
    .DESCRIPTION
    步骤：检查安装路径并记下设置指纹（只读，被安装锁拒绝时结果里也有指纹）；取得全机安装锁；安装根留有上次未完成安装的
    痕迹时拒绝（恢复在 #284）；前置检查（唯一的交互会话、把候选程序目录放到安装根下并按载荷清单逐项核对）；停止应用
    （结束已安装 EXE 的进程，等登录任务没有运行中的实例）；写下事务标记；现役程序目录改名为旧版本目录、候选改名为现役；
    登录任务不存在时创建；确认任务仍没有运行中的实例，删除旧回执，经登录任务启动，等到启动就绪；先清除事务标记，
    再删除旧版本目录。前置检查任何一项失败时只删除候选（及为它新建的安装根），
    其余什么都不改。-CheckOnly 执行到前置检查为止，只核对包本身、不放候选，报告将要执行的步骤。
    写下事务标记之后的失败只报告、不回滚，事务标记留在原处（回滚在 #283）。
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [string] $PackageRoot,
        [Parameter(Mandatory)] [string] $InstallRoot,
        [Parameter(Mandatory)] [string] $TaskName,
        [switch] $CheckOnly,
        [object] $Ports,
        [ValidateNotNullOrEmpty()] [string] $LockName = $script:InstallLockName,
        [ValidateRange(1, 300)] [int] $ReadyTimeoutSeconds = 30,
        [ValidateRange(1, 300)] [int] $StopTimeoutSeconds = 10
    )

    if ($null -eq $Ports) { $Ports = New-CountdownInstallerWindowsPorts }
    $result = [ordered]@{
        schemaVersion = 1
        succeeded = $false
        checkOnly = [bool] $CheckOnly
        failedStage = $null
        reason = $null
        error = $null
        installRoot = $InstallRoot
        taskName = $TaskName
        package = $null
        firstInstall = $null
        plannedSteps = @()
        loginTaskCreated = $false
        stoppedProcessCount = 0
        readyProcess = $null
        settings = [ordered]@{ before = $null; after = $null }
        settingsRehearsal = $null
        restartedProcess = $null
    }
    $stage = 'preflight'
    $lock = $null
    $layout = $null
    $rootCreated = $false
    $candidateOwned = $false
    $transactionUndoable = $false
    try {
        # 安装路径检查与设置指纹只读、不需要锁：被安装锁拒绝的结果里也有设置指纹。
        $layout = Resolve-CountdownInstallerLayout -InstallRoot $InstallRoot
        $result.installRoot = $layout.Root
        $result.settings.before = Get-CountdownSettingsFingerprint -Path $layout.SettingsPath

        $stage = 'lock'
        $lock = Enter-CountdownInstallLock -Name $LockName

        $stage = 'recovery'
        Assert-CountdownNoUnfinishedInstall -Layout $layout

        $stage = 'preflight'
        $session = Get-CountdownSingleInteractiveSession -Ports $Ports
        $sourceDirectory = Join-Path (Resolve-CountdownInstallerPath -Path $PackageRoot) 'app'
        $manifest = Read-CountdownPayloadManifest -ProgramDirectory $sourceDirectory
        $result.package = [ordered]@{ commit = $manifest.Commit; aggregateSha256 = $manifest.AggregateSha256 }
        $result.firstInstall = -not (Test-Path -LiteralPath $layout.ProgramDirectory -PathType Container)
        $plan = [System.Collections.Generic.List[string]]::new()
        $plan.AddRange([string[]] @('place-candidate', 'stop-application', 'write-transaction-marker', 'snapshot-settings'))
        if (Test-Path -LiteralPath $layout.SettingsPath -PathType Leaf) { $plan.Add('settings-rehearsal') }
        $plan.Add('replace-program-directory')
        if ($null -eq (& $Ports.GetTask $TaskName)) { $plan.Add('create-login-task') }
        $plan.AddRange([string[]] @('start-and-wait-ready', 'commit'))

        if ($CheckOnly) {
            Assert-CountdownProgramDirectoryMatchesManifest -Directory $sourceDirectory -Manifest $manifest
            $result.plannedSteps = $plan.ToArray()
            $result.succeeded = $true
        }
        else {
            if (-not (Test-Path -LiteralPath $layout.Root)) {
                [void] [System.IO.Directory]::CreateDirectory($layout.Root)
                $rootCreated = $true
            }
            $candidateOwned = $true
            Copy-CountdownProgramCandidate -SourceDirectory $sourceDirectory -CandidateDirectory $layout.CandidateDirectory -Manifest $manifest
            Assert-CountdownProgramDirectoryMatchesManifest -Directory $layout.CandidateDirectory -Manifest $manifest
            $result.plannedSteps = $plan.ToArray()

            $stage = 'stop-application'
            $result.stoppedProcessCount = Stop-CountdownInstalledApplication -Layout $layout -TaskName $TaskName -Ports $Ports -TimeoutSeconds $StopTimeoutSeconds

            $stage = 'write-transaction-marker'
            Write-CountdownInstallMarker -Path $layout.MarkerPath -Manifest $manifest
            $candidateOwned = $false

            # 换程序之前的设置保护（#282）：这一段失败时撤回本次安装并重新启动旧版本，程序目录与现役设置都没被动过。
            $transactionUndoable = $true
            $stage = 'snapshot-settings'
            $snapshotDirectory = Save-CountdownSettingsSnapshot -Layout $layout
            $stage = 'settings-rehearsal'
            $rehearsal = Invoke-CountdownSettingsRehearsal -Layout $layout -SnapshotDirectory $snapshotDirectory -Ports $Ports
            $result.settingsRehearsal = $rehearsal.Report
            if ($null -ne $rehearsal.Rejection) { throw $rehearsal.Rejection }
            $transactionUndoable = $false

            $stage = 'replace-program-directory'
            if (Test-Path -LiteralPath $layout.ProgramDirectory) {
                [void] (& $Ports.MoveDirectory $layout.ProgramDirectory $layout.PreviousDirectory)
            }
            [void] (& $Ports.MoveDirectory $layout.CandidateDirectory $layout.ProgramDirectory)

            $stage = 'create-login-task'
            if ($null -eq (& $Ports.GetTask $TaskName)) {
                [void] (& $Ports.RegisterTask $TaskName (Get-CountdownLoginTaskDefinition -ExecutablePath $layout.ExecutablePath) $session)
                $result.loginTaskCreated = $true
            }

            $stage = 'start-and-wait-ready'
            if (Test-CountdownTaskRunning -Task (& $Ports.GetTask $TaskName)) { throw (New-CountdownTaskStillRunningRejection -TaskName $TaskName) }
            if (Test-Path -LiteralPath $layout.RuntimeStatusPath) { [System.IO.File]::Delete($layout.RuntimeStatusPath) }
            [void] (& $Ports.StartTask $TaskName)
            $ready = Wait-CountdownStartupReady -Layout $layout -SessionId ([int] $session.SessionId) -ExpectedExecutableSha256 $manifest.ExecutableSha256 -Ports $Ports -TimeoutSeconds $ReadyTimeoutSeconds
            $result.readyProcess = [ordered]@{ processId = [int] $ready.ProcessId; sessionId = [int] $ready.SessionId }

            $stage = 'commit'
            [System.IO.File]::Delete($layout.MarkerPath)
            if (Test-Path -LiteralPath $layout.PreviousDirectory) { [System.IO.Directory]::Delete($layout.PreviousDirectory, $true) }
            if (Test-Path -LiteralPath $layout.TransactionDirectory) { [System.IO.Directory]::Delete($layout.TransactionDirectory, $true) }
            $result.succeeded = $true
        }
    }
    catch {
        $result.failedStage = $stage
        $reason = $_.Exception.Data['CountdownInstallReason']
        $result.reason = if ($null -ne $reason) { [string] $reason } else { 'unexpected-error' }
        $result.error = ConvertTo-CountdownBoundedError -Message $_.Exception.Message
        if ($candidateOwned) {
            # 事务标记写下之前失败：只删除候选（以及为它新建、此刻已空的安装根），其余什么都不改。
            try {
                if (Test-Path -LiteralPath $layout.CandidateDirectory) { [System.IO.Directory]::Delete($layout.CandidateDirectory, $true) }
                if ($rootCreated -and @(Get-ChildItem -LiteralPath $layout.Root -Force).Count -eq 0) { [System.IO.Directory]::Delete($layout.Root) }
            }
            catch { $result.error = ConvertTo-CountdownBoundedError -Message "$($result.error)；删除候选失败：$($_.Exception.Message)" }
        }
        if ($transactionUndoable) {
            $undo = Undo-CountdownInstallBeforeReplace -Layout $layout -TaskName $TaskName -Session $session -Ports $Ports -ReadyTimeoutSeconds $ReadyTimeoutSeconds
            $result.restartedProcess = $undo.RestartedProcess
            if ($undo.Problems.Count -gt 0) { $result.error = ConvertTo-CountdownBoundedError -Message "$($result.error)；$($undo.Problems -join '；')" }
        }
    }
    finally {
        Exit-CountdownInstallLock -Mutex $lock
        if ($null -ne $layout) {
            try { $result.settings.after = Get-CountdownSettingsFingerprint -Path $layout.SettingsPath }
            catch { $result.settings.after = $null }
        }
    }
    $result
}

function New-CountdownInstallerWindowsPorts {
    $field = { param($Object, [string] $Name) $property = $Object.PSObject.Properties[$Name]; if ($property) { $property.Value } }
    [pscustomobject]@{
        # 交互会话按 explorer.exe 所在的会话认：同一会话里的多个 explorer 进程算一个会话。只有一个会话时才读它的登录用户
        # （登记登录任务要用）；多个会话反正会被拒绝，读别的用户的进程属主可能没有权限，不去读。
        GetInteractiveSessions = {
            $sessions = @(Get-CimInstance Win32_Process -Filter "Name='explorer.exe'" -OperationTimeoutSec 10 -ErrorAction Stop |
                    Where-Object { [int] $_.SessionId -gt 0 } | Group-Object -Property SessionId)
            if ($sessions.Count -ne 1) {
                return @($sessions | ForEach-Object { [pscustomobject]@{ SessionId = [int] $_.Name; User = $null } })
            }
            $owner = Invoke-CimMethod -InputObject $sessions[0].Group[0] -MethodName GetOwner -OperationTimeoutSec 10 -ErrorAction Stop
            if ([int] $owner.ReturnValue -ne 0 -or [string]::IsNullOrWhiteSpace([string] $owner.User)) {
                throw "无法读取交互会话 $($sessions[0].Name) 的登录用户。"
            }
            [pscustomobject]@{
                SessionId = [int] $sessions[0].Name
                User = if ([string]::IsNullOrWhiteSpace([string] $owner.Domain)) { [string] $owner.User } else { "$($owner.Domain)\$($owner.User)" }
            }
        }
        GetProcesses = {
            param([string] $Name)
            @(Get-CimInstance Win32_Process -Filter "Name='$Name'" -OperationTimeoutSec 10 -ErrorAction Stop)
        }
        # 进程已经退出时不算错；没结束掉的由调用方的回读发现。
        StopProcess = {
            param([int] $ProcessId)
            Stop-Process -Id $ProcessId -Force -ErrorAction SilentlyContinue
        }
        # 与状态命令（#276）读出的任务同形。
        GetTask = {
            param([string] $Name)
            try { $task = Get-ScheduledTask -TaskPath '\' -TaskName $Name -ErrorAction Stop }
            catch { if ($_.CategoryInfo.Category -eq 'ObjectNotFound') { return $null }; throw }
            $kind = { param($o) $o.CimClass.CimClassName -replace '^MSFT_Task|(Action|Trigger)$' }
            [pscustomobject]@{
                State = [string] $task.State
                Enabled = [bool] $task.Settings.Enabled
                Actions = @($task.Actions | Where-Object { $_ } | ForEach-Object { [pscustomobject]@{ Type = & $kind $_; Execute = & $field $_ 'Execute'; Arguments = & $field $_ 'Arguments' } })
                Triggers = @($task.Triggers | Where-Object { $_ } | ForEach-Object { [pscustomobject]@{ Type = & $kind $_; Enabled = [bool] $_.Enabled } })
                LogonType = [string] $task.Principal.LogonType
                RunLevel = [string] $task.Principal.RunLevel
                Priority = [int] $task.Settings.Priority
                ExecutionTimeLimit = [string] $task.Settings.ExecutionTimeLimit
                MultipleInstances = [string] $task.Settings.MultipleInstances
            }
        }
        # 按 #279「登录任务的最终定义」注册。非提升时能否注册登录触发器未实测（#279 第 7 项），留给 #288 在真实任务上确认。
        RegisterTask = {
            param([string] $Name, [object] $Definition, [object] $Session)
            $action = New-ScheduledTaskAction -Execute ([string] $Definition.Actions[0].Execute)
            $trigger = New-ScheduledTaskTrigger -AtLogOn -User ([string] $Session.User)
            $principal = New-ScheduledTaskPrincipal -UserId ([string] $Session.User) -LogonType ([string] $Definition.LogonType) -RunLevel ([string] $Definition.RunLevel)
            $settings = New-ScheduledTaskSettingsSet -Priority ([int] $Definition.Priority) `
                -ExecutionTimeLimit ([System.Xml.XmlConvert]::ToTimeSpan([string] $Definition.ExecutionTimeLimit)) `
                -MultipleInstances ([string] $Definition.MultipleInstances) `
                -AllowStartIfOnBatteries:(-not [bool] $Definition.DisallowStartIfOnBatteries) `
                -DontStopIfGoingOnBatteries:(-not [bool] $Definition.StopIfGoingOnBatteries)
            Register-ScheduledTask -TaskPath '\' -TaskName $Name -Action $action -Trigger $trigger -Principal $principal -Settings $settings `
                -Description '登录后直接启动敏敏子倒计时。' -ErrorAction Stop | Out-Null
        }
        StartTask = {
            param([string] $Name)
            Start-ScheduledTask -TaskPath '\' -TaskName $Name -ErrorAction Stop
        }
        MoveDirectory = {
            param([string] $Source, [string] $Destination)
            [System.IO.Directory]::Move($Source, $Destination)
        }
        # 候选 EXE 的无界面命令（#278 设置迁移、#272 设置等价判定）：命令在创建窗口之前返回，stdout 是一份 UTF-8 JSON。
        # 不经 shell、不开控制台窗口；最多等 60 秒，超时结束整个进程树并报错。命令只读写参数给出的路径。
        RunHeadlessCommand = {
            param([string] $ExecutablePath, [string[]] $Arguments, [string] $WorkingDirectory)
            $startInfo = [System.Diagnostics.ProcessStartInfo]::new($ExecutablePath)
            foreach ($argument in $Arguments) { $startInfo.ArgumentList.Add($argument) }
            $startInfo.WorkingDirectory = $WorkingDirectory
            $startInfo.UseShellExecute = $false
            $startInfo.CreateNoWindow = $true
            $startInfo.RedirectStandardOutput = $true
            $startInfo.RedirectStandardError = $true
            $startInfo.StandardOutputEncoding = [System.Text.UTF8Encoding]::new($false)
            $startInfo.StandardErrorEncoding = [System.Text.UTF8Encoding]::new($false)
            $process = [System.Diagnostics.Process]::Start($startInfo)
            try {
                $stdout = $process.StandardOutput.ReadToEndAsync()
                $stderr = $process.StandardError.ReadToEndAsync()
                if (-not $process.WaitForExit(60000)) {
                    $process.Kill($true)
                    throw "候选 EXE 的无界面命令 60 秒内没有结束，已结束它：$($Arguments[0])"
                }
                $process.WaitForExit()
                [void] $stderr.GetAwaiter().GetResult()
                [pscustomobject]@{ ExitCode = $process.ExitCode; Output = $stdout.GetAwaiter().GetResult() }
            }
            finally { $process.Dispose() }
        }
    }
}

Export-ModuleMember -Function Install-CountdownProgram, New-CountdownInstallerWindowsPorts
