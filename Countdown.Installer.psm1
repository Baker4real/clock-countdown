#requires -Version 7.0

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# 新安装器（#259）：只替换程序目录。安装根下只动现役程序目录 app、候选 app.candidate、旧版本 app.previous、
# 事务标记 install-transaction.json（及其临时文件 install-transaction.json.tmp）、事务目录 install-transaction\ 与运行状态回执
# runtime-status.json；设置、设置备份、恢复证据、诊断日志和节假日缓存不被移动或改写，设置及其两代备份只被复制进事务目录
# （快照与迁移演练的副本）。
# 安装根之外只登记登录任务与交互用户桌面上的快捷方式。计划任务、进程、交互会话、桌面目录、快捷方式、目录改名与
# 候选 EXE 的无界面命令经 port 调用：生产用 New-CountdownInstallerWindowsPorts，
# 测试注入假 port。本机与远端是同一个安装器；它不运行测试套件，不检查构建工具链，不触碰 Windows 时间服务。
# 已有成功路径（#280）、换程序前的设置保护（#282）、登录任务与桌面快捷方式的登记（#281）、失败回滚（#283）和
# 安装开始时的中断恢复（#284，Invoke-CountdownInstallRecovery）。

# 与旧安装器（Countdown.InstallTransaction.psm1）同名：新旧安装器在同一台机器上互斥。
$script:InstallLockName = 'Global\MinminCountdownInstallTransaction-v1'
$script:ApplicationProcessName = 'MinminCountdown.exe'
$script:ExecutableManifestPath = 'app\MinminCountdown.exe'
# 桌面快捷方式的名字沿用旧安装器放在桌面上的启动器 敏敏子倒计时.exe（#281）。
$script:DesktopShortcutName = '敏敏子倒计时.lnk'
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
        # 事务标记先写进这个临时文件再改名；改名之前被打断时只留下它，标记并没有写下。
        MarkerTemporaryPath = Join-Path $root 'install-transaction.json.tmp'
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

# 全机安装锁按线程归属：持有它的线程再取会重入成功（切换在同一线程里调用安装器，#289），各自放开时只减去自己那一层。
function Enter-CountdownInstallLock {
    param([ValidateNotNullOrEmpty()] [string] $Name = $script:InstallLockName)

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

# 短暂占用：目录树里只要有一个文件开着（杀毒软件扫描刚复制或刚运行过的文件、映像还没释放），目录改名就报拒绝访问，即使那个
# 句柄允许删除也一样（.NET 对目录给的是 HResult 为 E_ACCESSDENIED 的 IOException）；文件删除与替换报拒绝访问、共享冲突或锁冲突，
# 删除目录树时里面的文件还被允许删除的句柄开着，目录报不是空的。只认这几种；路径不存在、目标已存在等确定性失败不重试。
function Test-CountdownTransientFileLock {
    param([Parameter(Mandatory)] [System.Exception] $Exception)

    for ($current = $Exception; $null -ne $current; $current = $current.InnerException) {
        if ($current -is [System.UnauthorizedAccessException]) { return $true }
        if ($current -is [System.IO.IOException] -and ($current.HResult -band 0xFFFF) -in @(5, 32, 33, 145)) { return $true }
    }
    $false
}

# 遇到短暂占用时有界重试一个文件系统动作：间隔从 100 毫秒起翻倍、最长 1 秒，过了时限仍被占用就抛出最后一次的错误。
# 动作按动态作用域读调用方的变量，所以这里的局部变量都带 retry 前缀。
function Invoke-CountdownWithFileLockRetry {
    param(
        [Parameter(Mandatory)] [scriptblock] $Action,
        [Parameter(Mandatory)] [int] $RetrySeconds
    )

    $retryDeadline = [DateTime]::UtcNow.AddSeconds($RetrySeconds)
    $retryDelay = 100
    while ($true) {
        try { return (& $Action) }
        catch {
            if ([DateTime]::UtcNow -ge $retryDeadline -or -not (Test-CountdownTransientFileLock -Exception $_.Exception)) { throw }
        }
        Start-Sleep -Milliseconds $retryDelay
        $retryDelay = [Math]::Min($retryDelay * 2, 1000)
    }
}

# 删除安装器自己的一个文件或目录树（不在时什么都不做），被短暂占用时有界重试。
function Remove-CountdownInstallEntry {
    param(
        [Parameter(Mandatory)] [string] $Path,
        [Parameter(Mandatory)] [int] $RetrySeconds
    )

    Invoke-CountdownWithFileLockRetry -RetrySeconds $RetrySeconds -Action {
        if (Test-Path -LiteralPath $Path -PathType Container) { [System.IO.Directory]::Delete($Path, $true) }
        elseif (Test-Path -LiteralPath $Path) { [System.IO.File]::Delete($Path) }
    }
}

# 经 port 改名安装根里的一个目录（同卷改名，要么完成要么没发生），被短暂占用时有界重试。
function Move-CountdownInstallDirectory {
    param(
        [Parameter(Mandatory)] [object] $Ports,
        [Parameter(Mandatory)] [string] $Source,
        [Parameter(Mandatory)] [string] $Destination,
        [Parameter(Mandatory)] [int] $RetrySeconds
    )

    Invoke-CountdownWithFileLockRetry -RetrySeconds $RetrySeconds -Action { [void] (& $Ports.MoveDirectory $Source $Destination) }
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

# 经登录任务启动之前记下任务的上次运行时间与上次结果（#288），等待启动就绪时凭它们认出这次启动的那次运行。只记值、
# 不留任务对象：port 可能每次返回同一个会变的对象。
function Get-CountdownTaskRunMark {
    param(
        [Parameter(Mandatory)] [string] $TaskName,
        [AllowNull()] [object] $Task
    )

    [pscustomobject]@{
        TaskName = $TaskName
        LastRunTime = Get-CountdownInstallerProperty -Object $Task -Name 'LastRunTime'
        LastTaskResult = Get-CountdownInstallerProperty -Object $Task -Name 'LastTaskResult'
    }
}

# 这次启动的那次运行已经结束时返回它的上次结果，否则为空（#288）。直接运行 EXE 的登录任务，实例与进程同生同灭，上次结果
# 就是进程的退出码（#279 实测）。启动后上次运行时间或上次结果与启动前不同，或这次等待里见过任务在运行，说明那次运行已经
# 开始；任务不在运行，上次结果也不是 0x41301（运行中）、0x41303（尚未运行）或 0x41325（已排队）时，它已经结束。
# 读不到上次结果（port 不提供）时从不算结束。
function Get-CountdownStartedRunResult {
    param(
        [Parameter(Mandatory)] [object] $Mark,
        [AllowNull()] [object] $Task,
        [Parameter(Mandatory)] [bool] $SawRunning
    )

    $lastResult = Get-CountdownInstallerProperty -Object $Task -Name 'LastTaskResult'
    if ($null -eq $lastResult -or [string] (Get-CountdownInstallerProperty -Object $Task -Name 'State') -in @('Running', 'Queued')) { return $null }
    if ([long] $lastResult -in @(0x41301, 0x41303, 0x41325)) { return $null }
    $started = $SawRunning -or
        -not [object]::Equals($Mark.LastRunTime, (Get-CountdownInstallerProperty -Object $Task -Name 'LastRunTime')) -or
        $null -eq $Mark.LastTaskResult -or [long] $Mark.LastTaskResult -ne [long] $lastResult
    if ($started) { [long] $lastResult } else { $null }
}

# 启动的进程在启动就绪之前就退出了（#288）：不再等满就绪时限。退出码另记在异常上，安装结果写进 startupExitCode。
function New-CountdownStartupExitedRejection {
    param([Parameter(Mandatory)] [long] $ExitCode)

    $rejection = New-CountdownInstallRejection -Reason 'startup-exited' -Message ('经登录任务启动的进程在启动就绪之前退出，退出码 {0}（0x{1:X8}），原因见应用的诊断日志。' -f $ExitCode, ($ExitCode -band 0xFFFFFFFFL))
    $rejection.Data['CountdownStartupExitCode'] = $ExitCode
    $rejection
}

# 结束前按 PID 重新读一次进程，映像路径仍是已安装 EXE、启动时间仍是枚举时那个才结束：枚举之后进程可能已经退出，PID 也可能
# 已被别的进程复用。返回是否结束了它。
function Stop-CountdownInstalledProcess {
    param(
        [Parameter(Mandatory)] [object] $Layout,
        [Parameter(Mandatory)] [object] $Ports,
        [Parameter(Mandatory)] [object] $Process
    )

    $processId = [int] $Process.ProcessId
    $startTime = Get-CountdownInstallerProperty -Object $Process -Name 'CreationDate'
    $same = @(Get-CountdownInstalledProcesses -Layout $Layout -Ports $Ports | Where-Object {
            [int] $_.ProcessId -eq $processId -and [object]::Equals((Get-CountdownInstallerProperty -Object $_ -Name 'CreationDate'), $startTime)
        })
    if ($same.Count -eq 0) { return $false }
    [void] (& $Ports.StopProcess $processId)
    $true
}

# 停止应用（#259 第 3 步）：登录任务有运行中的实例时先结束任务实例，给映像路径是已安装 EXE 的进程一段有界正常退出时间。
# 停止任务停不掉在任务之外启动的实例（#279 实测），所以任务结束或宽限期到达后仍存活的进程才按 PID 复核身份并强制结束。
# #279 见到的停止任务走崩溃路径已由 #295 修复，#288 复核为正常退出（退出码 0，不留致命异常）；被强杀的进程不写 ProcessExited。
# 任务实例还在时经任务启动会被 IgnoreNew 拒绝，所以返回前一并确认它已结束。未保存的编辑草稿会丢失，与现有安装器相同。
# 返回开始时的进程数、其中正常退出数与本次强杀数。
function Stop-CountdownInstalledApplication {
    param(
        [Parameter(Mandatory)] [object] $Layout,
        [Parameter(Mandatory)] [string] $TaskName,
        [Parameter(Mandatory)] [object] $Ports,
        [Parameter(Mandatory)] [int] $TimeoutSeconds
    )

    $initial = @(Get-CountdownInstalledProcesses -Layout $Layout -Ports $Ports)
    $killed = [System.Collections.Generic.HashSet[int]]::new()
    $taskRunning = Test-CountdownTaskRunning -Task (& $Ports.GetTask $TaskName)
    if ($taskRunning) { [void] (& $Ports.EndTask $TaskName) }
    $force = -not $taskRunning
    $graceDeadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    $forceDeadline = if ($force) { [DateTime]::UtcNow.AddSeconds($TimeoutSeconds) } else { $null }
    while ($true) {
        $remaining = @(Get-CountdownInstalledProcesses -Layout $Layout -Ports $Ports)
        $taskRunning = Test-CountdownTaskRunning -Task (& $Ports.GetTask $TaskName)
        if ($remaining.Count -eq 0 -and -not $taskRunning) {
            return [pscustomobject]@{
                ProcessCount = $initial.Count
                ExitedProcessCount = [Math]::Max(0, $initial.Count - $killed.Count)
                KilledProcessCount = $killed.Count
            }
        }

        $now = [DateTime]::UtcNow
        if (-not $force -and (-not $taskRunning -or $now -ge $graceDeadline)) {
            $force = $true
            $forceDeadline = $now.AddSeconds($TimeoutSeconds)
        }
        if ($force) {
            foreach ($process in $remaining) {
                if (Stop-CountdownInstalledProcess -Layout $Layout -Ports $Ports -Process $process) { [void] $killed.Add([int] $process.ProcessId) }
            }
            if ($null -ne $forceDeadline -and $now -ge $forceDeadline) {
                if ($remaining.Count -eq 0) { throw (New-CountdownTaskStillRunningRejection -TaskName $TaskName) }
                throw (New-CountdownInstallRejection -Reason 'application-still-running' -Message "$TimeoutSeconds 秒宽限期及其后的强制结束等待内没能停止应用：仍有 $($remaining.Count) 个已安装 EXE 的进程。")
            }
        }
        Start-Sleep -Milliseconds 200
    }
}

# 事务标记记下候选那一版（提交与载荷清单的聚合哈希）：中断恢复（#284）凭它认出现役 app 是不是这次要装的新版本。
function Write-CountdownInstallMarker {
    param(
        [Parameter(Mandatory)] [object] $Layout,
        [Parameter(Mandatory)] [object] $Manifest
    )

    $marker = [ordered]@{
        schemaVersion = 1
        startedAtUtc = [DateTimeOffset]::UtcNow.ToString('o')
        candidateCommit = $Manifest.Commit
        candidateAggregateSha256 = $Manifest.AggregateSha256
    }
    [System.IO.File]::WriteAllText($Layout.MarkerTemporaryPath, ($marker | ConvertTo-Json), $script:Utf8NoBom)
    [System.IO.File]::Move($Layout.MarkerTemporaryPath, $Layout.MarkerPath)
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

# 一次安装从写下事务标记起要回滚时用到的事实（#283），只在这一次安装运行的内存里：快照在哪、程序目录是否已换成新版本、
# 登录任务改之前的样子（只在本次安装要改它时记下）、桌面快捷方式是不是本次创建的、新版本是否已达到启动就绪。
function New-CountdownInstallTransactionState {
    param([Parameter(Mandatory)] [bool] $FirstInstall)

    [pscustomobject]@{
        FirstInstall = $FirstInstall
        SnapshotDirectory = $null
        ProgramReplaced = $false
        LoginTaskChangeStarted = $false
        LoginTaskBefore = $null
        LoginTaskBeforeDefinition = $null
        ShortcutCreated = $false
        NewVersionReady = $false
    }
}

# 替换程序目录：现役 app 改名为 app.previous（首次安装没有这一步），候选 app.candidate 改名为 app。被短暂占用时各自有界重试
# （#281 曾见迁移演练刚运行过候选 EXE 时第二次改名报拒绝访问）；占用超过时限时原因为 program-directory-in-use。第二次改名
# 失败时留下“现役目录不存在、旧版本目录存在”，由回滚改名换回。
function Switch-CountdownProgramDirectory {
    param(
        [Parameter(Mandatory)] [object] $Layout,
        [Parameter(Mandatory)] [object] $Ports,
        [Parameter(Mandatory)] [int] $RetrySeconds
    )

    try {
        if (Test-Path -LiteralPath $Layout.ProgramDirectory) {
            Move-CountdownInstallDirectory -Ports $Ports -Source $Layout.ProgramDirectory -Destination $Layout.PreviousDirectory -RetrySeconds $RetrySeconds
        }
        Move-CountdownInstallDirectory -Ports $Ports -Source $Layout.CandidateDirectory -Destination $Layout.ProgramDirectory -RetrySeconds $RetrySeconds
    }
    catch {
        if (-not (Test-CountdownTransientFileLock -Exception $_.Exception)) { throw }
        throw (New-CountdownInstallRejection -Reason 'program-directory-in-use' -Message "程序目录被占用，$RetrySeconds 秒内没能改名：$($_.Exception.GetBaseException().Message)")
    }
}

# 回滚时把程序目录放回安装前的样子。按目录现状判断替换走到了哪一步，与中断恢复（#284）的恢复表同一套判断：
# app.previous 在时，app（新版本）也在就先改回 app.candidate，再把 app.previous 改回 app；两次改名之间失败时 app 不在，直接改回。
# 首次安装已换上新版本时（只有 app），把它改回 app.candidate，安装根回到没有程序目录的样子。app.candidate 还在时还没换程序
# （或第一次改名就失败了），不用改名。改名被短暂占用时有界重试。
function Restore-CountdownProgramDirectory {
    param(
        [Parameter(Mandatory)] [object] $Layout,
        [Parameter(Mandatory)] [object] $Ports,
        [Parameter(Mandatory)] [bool] $FirstInstall,
        [Parameter(Mandatory)] [int] $RetrySeconds
    )

    $program = Test-Path -LiteralPath $Layout.ProgramDirectory
    $candidate = Test-Path -LiteralPath $Layout.CandidateDirectory
    if (Test-Path -LiteralPath $Layout.PreviousDirectory) {
        if ($program -and $candidate) { throw '安装根同时有 app、app.candidate 与 app.previous，分不清哪个是旧版本。' }
        if ($program) { Move-CountdownInstallDirectory -Ports $Ports -Source $Layout.ProgramDirectory -Destination $Layout.CandidateDirectory -RetrySeconds $RetrySeconds }
        Move-CountdownInstallDirectory -Ports $Ports -Source $Layout.PreviousDirectory -Destination $Layout.ProgramDirectory -RetrySeconds $RetrySeconds
    }
    elseif ($program -and -not $candidate) {
        if (-not $FirstInstall) { throw '升级时安装根只有 app、没有 app.previous，旧版本不在了。' }
        Move-CountdownInstallDirectory -Ports $Ports -Source $Layout.ProgramDirectory -Destination $Layout.CandidateDirectory -RetrySeconds $RetrySeconds
    }
}

function Test-CountdownSameFileBytes {
    param(
        [Parameter(Mandatory)] [string] $Left,
        [Parameter(Mandatory)] [string] $Right
    )

    (Get-FileHash -LiteralPath $Left -Algorithm SHA256).Hash -eq (Get-FileHash -LiteralPath $Right -Algorithm SHA256).Hash
}

# 用本次安装拍下的快照恢复设置及其两代备份（#283）。只在同一次安装运行里、安装器确认新版本达到启动就绪之前调用：在那之前
# 新版本至多运行了等待就绪的时限，通常还没渲染悬浮窗；就绪之后用户可能已经在编辑，不再覆盖。逐个比较，与快照逐字节相同的
# 不动；不同的先复制到事务目录里的临时文件，再整份替换现役文件（同卷改名，替换要么完成要么没发生）；快照里没有的（安装前
# 就不存在，例如新版本迁移时轮转出的 backup 2）删除。主文件最先恢复：中途失败时旧版本至少读到安装前的主文件。
# RA4：现役文件与快照不同、即将被覆盖或删除时，先把它的逐字节副本留在同目录的 .before-rollback-<UTC> 文件中；返回 restored
# 或 unchanged，以及这些副本的文件名（不含正文）。副本不随事务目录删除。
function Restore-CountdownSettingsSnapshot {
    param(
        [Parameter(Mandatory)] [object] $Layout,
        [Parameter(Mandatory)] [string] $SnapshotDirectory,
        [Parameter(Mandatory)] [int] $RetrySeconds
    )

    $stagingDirectory = Join-Path $Layout.TransactionDirectory 'settings-restore'
    $outcome = 'unchanged'
    $liveCopies = [System.Collections.Generic.List[string]]::new()
    $suffix = [DateTimeOffset]::UtcNow.ToString('yyyyMMddTHHmmssfffffffZ')
    foreach ($name in $script:SettingsFamilyNames) {
        $live = Join-Path $Layout.Root $name
        $saved = Join-Path $SnapshotDirectory $name
        $liveExists = Test-Path -LiteralPath $live -PathType Leaf
        $savedExists = Test-Path -LiteralPath $saved -PathType Leaf
        if ($liveExists -and $savedExists -and (Test-CountdownSameFileBytes -Left $live -Right $saved)) { continue }
        if (-not $liveExists -and -not $savedExists) { continue }

        if ($liveExists) {
            $copyName = "$name.before-rollback-$suffix"
            [System.IO.File]::Copy($live, (Join-Path $Layout.Root $copyName))
            $liveCopies.Add($copyName)
        }
        if ($savedExists) {
            $staged = Join-Path $stagingDirectory $name
            [void] [System.IO.Directory]::CreateDirectory($stagingDirectory)
            [System.IO.File]::Copy($saved, $staged, $true)
            Invoke-CountdownWithFileLockRetry -RetrySeconds $RetrySeconds -Action { [System.IO.File]::Move($staged, $live, $true) }
        }
        else {
            Invoke-CountdownWithFileLockRetry -RetrySeconds $RetrySeconds -Action { [System.IO.File]::Delete($live) }
        }
        $outcome = 'restored'
    }
    [pscustomobject]@{ Outcome = $outcome; LiveCopies = $liveCopies.ToArray() }
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

# 回滚时登录任务回到安装前（#283）。本次安装没动过它时什么都不做（返回空）。安装前的任务被本次更新过时，按改之前导出的
# 整份定义恢复（restored）；任务是本次创建的：首次安装删除它（removed），没有程序可启动；升级时保留（kept），它与期望定义
# 一致、指向同一个 EXE 路径，旧版本要经它重新启动。注册其实没生效（任务与改之前逐项相同、或创建没发生）时也什么都不做。
function Restore-CountdownLoginTask {
    param(
        [Parameter(Mandatory)] [string] $TaskName,
        [Parameter(Mandatory)] [object] $Session,
        [Parameter(Mandatory)] [object] $Transaction,
        [Parameter(Mandatory)] [object] $Ports
    )

    if (-not $Transaction.LoginTaskChangeStarted) { return $null }
    $current = & $Ports.GetTask $TaskName
    if ($null -eq $Transaction.LoginTaskBefore) {
        if ($null -eq $current) { return $null }
        if (-not $Transaction.FirstInstall) { return 'kept' }
        [void] (& $Ports.RestoreTask $TaskName $null)
        return 'removed'
    }
    if ($null -ne $current) {
        $before = Get-CountdownLoginTaskItems -Task $Transaction.LoginTaskBefore -Session $Session
        $now = Get-CountdownLoginTaskItems -Task $current -Session $Session
        if (@($before.Keys | Where-Object { $before[$_] -ne $now[$_] }).Count -eq 0) { return $null }
    }
    [void] (& $Ports.RestoreTask $TaskName $Transaction.LoginTaskBeforeDefinition)
    'restored'
}

# 首次安装回滚时删除本次创建的桌面快捷方式：程序目录已移走，它会指向一个不存在的 EXE。删之前重读一次，仍是指向本安装根
# 已安装 EXE 的 Shell Link 才删。升级回滚不动快捷方式：新旧版本是同一个 EXE 路径，快捷方式的定义与版本无关。
function Remove-CountdownCreatedDesktopShortcut {
    param(
        [Parameter(Mandatory)] [string] $DesktopDirectory,
        [Parameter(Mandatory)] [string] $ExecutablePath,
        [Parameter(Mandatory)] [object] $Ports
    )

    $path = Join-Path $DesktopDirectory $script:DesktopShortcutName
    $existing = & $Ports.ReadShortcut $path
    if ($null -eq $existing -or -not [bool] (Get-CountdownInstallerProperty -Object $existing -Name 'IsShellLink')) { return $null }
    $target = ([string] (Get-CountdownInstallerProperty -Object $existing -Name 'TargetPath')).Trim('"')
    if (-not [string]::Equals($target, $ExecutablePath, [System.StringComparison]::OrdinalIgnoreCase)) { return $null }
    [void] (& $Ports.RemoveShortcut $path)
    'removed'
}

# 经登录任务重新启动旧版本并等到启动就绪。程序目录此时是旧版本，就绪核对的 EXE 哈希在启动之前取它本身。没有登录任务、
# 任务仍有运行中的实例（经它启动会被 IgnoreNew 悄悄拒绝）、启动不了或等不到启动就绪时抛出。
function Start-CountdownPreviousVersion {
    param(
        [Parameter(Mandatory)] [object] $Layout,
        [Parameter(Mandatory)] [string] $TaskName,
        [Parameter(Mandatory)] [object] $Session,
        [Parameter(Mandatory)] [object] $Ports,
        [Parameter(Mandatory)] [int] $ReadyTimeoutSeconds
    )

    $task = & $Ports.GetTask $TaskName
    if ($null -eq $task) { throw "没有登录任务 $TaskName，无法经它重新启动旧版本。" }
    if (Test-CountdownTaskRunning -Task $task) { throw (New-CountdownTaskStillRunningRejection -TaskName $TaskName) }
    $startedRun = Get-CountdownTaskRunMark -TaskName $TaskName -Task $task
    $installedSha256 = (Get-FileHash -LiteralPath $Layout.ExecutablePath -Algorithm SHA256).Hash
    if (Test-Path -LiteralPath $Layout.RuntimeStatusPath) { [System.IO.File]::Delete($Layout.RuntimeStatusPath) }
    [void] (& $Ports.StartTask $TaskName)
    $process = Wait-CountdownStartupReady -Layout $Layout -SessionId ([int] $Session.SessionId) -ExpectedExecutableSha256 $installedSha256 -Ports $Ports -TimeoutSeconds $ReadyTimeoutSeconds -StartedRun $startedRun
    [ordered]@{ processId = [int] $process.ProcessId; sessionId = [int] $process.SessionId }
}

# 失败回滚（#283）：写下事务标记之后、新版本达到启动就绪之前任何一步失败都走这里，换程序之前的撤回（#282）是它的特例。
# 按顺序：停止已安装 EXE 的进程（换程序之后就是新版本，它占着程序目录里的文件）；按目录现状把程序目录放回；程序换过时用本次
# 安装的快照恢复设置及两代备份；恢复本次改过的登录任务；首次安装删除本次创建的桌面快捷方式；有旧版本时经登录任务重新启动它并
# 等到启动就绪。这些都做到了才清除事务标记（回滚完成的分界），再删除候选与事务目录，结论为 rolled-back；后两者删不掉只记下
# 问题，残留留给下一次安装的中断恢复清掉（#284）。任何一步没做到，结论为 rollback-failed：事务标记、事务目录（含快照）与候选原样留下，由中断恢复
# （#284）接手；停不掉新版本或换不回程序目录时不再往下做，其余各步失败照样继续。问题逐条截短，不含设置内容。
function Undo-CountdownInstall {
    param(
        [Parameter(Mandatory)] [object] $Layout,
        [Parameter(Mandatory)] [string] $TaskName,
        [Parameter(Mandatory)] [object] $Session,
        [AllowEmptyString()] [string] $DesktopDirectory,
        [Parameter(Mandatory)] [object] $Transaction,
        [Parameter(Mandatory)] [object] $Ports,
        [Parameter(Mandatory)] [int] $ReadyTimeoutSeconds,
        [Parameter(Mandatory)] [int] $StopTimeoutSeconds,
        [Parameter(Mandatory)] [int] $RetrySeconds
    )

    $report = [ordered]@{ outcome = 'rollback-failed'; settings = $null; settingsLiveCopies = @(); loginTask = $null; desktopShortcut = $null; problems = @() }
    $problems = [System.Collections.Generic.List[string]]::new()
    $note = { param([string] $What, [object] $ErrorRecord) $problems.Add((ConvertTo-CountdownBoundedError -Message "$What：$($ErrorRecord.Exception.Message)")) }
    $restarted = $null

    $stopped = $true
    try { [void] (Stop-CountdownInstalledApplication -Layout $Layout -TaskName $TaskName -Ports $Ports -TimeoutSeconds $StopTimeoutSeconds) }
    catch {
        # 进程都已退出、只剩任务实例时照样可以换回程序目录；重新启动旧版本之前会再核对任务。
        if (@(Get-CountdownInstalledProcesses -Layout $Layout -Ports $Ports).Count -gt 0) {
            $stopped = $false
            & $note '停止新版本失败' $_
        }
    }
    $programBack = $false
    if ($stopped) {
        try {
            Restore-CountdownProgramDirectory -Layout $Layout -Ports $Ports -FirstInstall $Transaction.FirstInstall -RetrySeconds $RetrySeconds
            $programBack = $true
        }
        catch { & $note '换回程序目录失败' $_ }
    }
    if ($programBack) {
        if ($Transaction.ProgramReplaced) {
            try {
                $restoredSettings = Restore-CountdownSettingsSnapshot -Layout $Layout -SnapshotDirectory $Transaction.SnapshotDirectory -RetrySeconds $RetrySeconds
                $report.settings = $restoredSettings.Outcome
                $report.settingsLiveCopies = $restoredSettings.LiveCopies
            }
            catch { & $note '恢复设置失败' $_ }
        }
        try { $report.loginTask = Restore-CountdownLoginTask -TaskName $TaskName -Session $Session -Transaction $Transaction -Ports $Ports }
        catch { & $note '恢复登录任务失败' $_ }
        if ($Transaction.FirstInstall -and $Transaction.ShortcutCreated) {
            try { $report.desktopShortcut = Remove-CountdownCreatedDesktopShortcut -DesktopDirectory $DesktopDirectory -ExecutablePath $Layout.ExecutablePath -Ports $Ports }
            catch { & $note '删除本次创建的桌面快捷方式失败' $_ }
        }
        if (Test-Path -LiteralPath $Layout.ExecutablePath -PathType Leaf) {
            try { $restarted = Start-CountdownPreviousVersion -Layout $Layout -TaskName $TaskName -Session $Session -Ports $Ports -ReadyTimeoutSeconds $ReadyTimeoutSeconds }
            catch { & $note '重新启动旧版本失败' $_ }
        }
    }
    if ($problems.Count -eq 0) {
        try {
            Remove-CountdownInstallEntry -Path $Layout.MarkerPath -RetrySeconds $RetrySeconds
            $report.outcome = 'rolled-back'
        }
        catch { & $note '清除事务标记失败' $_ }
    }
    if ($report.outcome -eq 'rolled-back') {
        foreach ($path in @($Layout.CandidateDirectory, $Layout.TransactionDirectory)) {
            try { Remove-CountdownInstallEntry -Path $path -RetrySeconds $RetrySeconds }
            catch { & $note "删除 $([System.IO.Path]::GetFileName($path)) 失败（留给下一次安装的中断恢复清掉）" $_ }
        }
    }
    $report.problems = $problems.ToArray()
    [pscustomobject]@{ Report = $report; RestartedProcess = $restarted }
}

# 事务标记记下的候选聚合哈希；标记读不出（例如断电后内容没落盘）或没有这一项时为空，即不知道候选是哪一版。
function Get-CountdownMarkerCandidateAggregate {
    param([Parameter(Mandatory)] [string] $Path)

    try { $marker = [System.IO.File]::ReadAllText($Path) | ConvertFrom-Json }
    catch { return $null }
    $aggregate = [string] (Get-CountdownInstallerProperty -Object $marker -Name 'candidateAggregateSha256')
    if ($aggregate -match '^[0-9A-Fa-f]{64}$') { $aggregate } else { $null }
}

# 现役 app 是不是事务标记记下的候选那一版：比较 app 的载荷清单与标记的聚合哈希。有一边不知道（标记读不出、app 的载荷清单
# 缺失或无效）时为空。
function Test-CountdownProgramIsCandidate {
    param([Parameter(Mandatory)] [object] $Layout)

    $candidateAggregate = Get-CountdownMarkerCandidateAggregate -Path $Layout.MarkerPath
    if ($null -eq $candidateAggregate) { return $null }
    try { $manifest = Read-CountdownPayloadManifest -ProgramDirectory $Layout.ProgramDirectory }
    catch { return $null }
    [string]::Equals($manifest.AggregateSha256, $candidateAggregate, [System.StringComparison]::OrdinalIgnoreCase)
}

# 中断恢复（#284）看到的目录状态，只读。有事务标记时按三个程序目录各在不在、现役 app 是不是候选那一版，判断换程序走到了哪一步：
# before-replace（候选还在，或只剩事务标记：现役是旧版本，首次安装时还没有现役）、mid-replace（现役不在、旧版本在：两次改名之间）、
# after-replace（现役是新版本：旧版本还在，首次安装时没有旧版本）。现役与旧版本都在时按目录就是换过了，只有清单证明现役不是
# 候选那一版才算分不清。没有事务标记时只看有没有残留：leftovers 或 clean。三个程序目录都在，或没有标记、没有现役却有旧版本
# （提交不会走到这一步）时为 unrecognized。
function Get-CountdownInstallRecoveryState {
    param([Parameter(Mandatory)] [object] $Layout)

    $program = Test-Path -LiteralPath $Layout.ProgramDirectory
    $candidate = Test-Path -LiteralPath $Layout.CandidateDirectory
    $previous = Test-Path -LiteralPath $Layout.PreviousDirectory
    if (-not (Test-Path -LiteralPath $Layout.MarkerPath)) {
        if ($previous -and -not $program) { return 'unrecognized' }
        foreach ($path in @($Layout.CandidateDirectory, $Layout.PreviousDirectory, $Layout.TransactionDirectory, $Layout.MarkerTemporaryPath)) {
            if (Test-Path -LiteralPath $path) { return 'leftovers' }
        }
        return 'clean'
    }
    if ($program -and $candidate -and $previous) { return 'unrecognized' }
    if ($previous -and -not $program) { return 'mid-replace' }
    if ($candidate) { return 'before-replace' }
    $isCandidate = if ($program) { Test-CountdownProgramIsCandidate -Layout $Layout } else { $false }
    if ($previous) { return $(if ($isCandidate -eq $false) { 'unrecognized' } else { 'after-replace' }) }
    if ($isCandidate -eq $true) { 'after-replace' } else { 'before-replace' }
}

# 中断恢复补做的启动验收（#284），现役 app 是新版本。已安装 EXE 已有进程在运行时不再经登录任务启动（任务已在运行时会悄悄忽略
# 新实例，#279），直接按它写下的运行状态回执核对启动就绪；没有在运行时与安装一样：确认任务没有运行中的实例，删除旧回执，经登录
# 任务启动，等到启动就绪。就绪核对的 EXE 哈希取 app 自己的载荷清单。达不到时抛出。
function Confirm-CountdownInstalledVersionReady {
    param(
        [Parameter(Mandatory)] [object] $Layout,
        [Parameter(Mandatory)] [string] $TaskName,
        [Parameter(Mandatory)] [object] $Session,
        [Parameter(Mandatory)] [object] $Ports,
        [Parameter(Mandatory)] [int] $ReadyTimeoutSeconds
    )

    $manifest = Read-CountdownPayloadManifest -ProgramDirectory $Layout.ProgramDirectory
    $startedRun = $null
    if (@(Get-CountdownInstalledProcesses -Layout $Layout -Ports $Ports).Count -eq 0) {
        $task = & $Ports.GetTask $TaskName
        if ($null -eq $task) { throw "没有登录任务 $TaskName，无法经它启动新版本。" }
        if (Test-CountdownTaskRunning -Task $task) { throw (New-CountdownTaskStillRunningRejection -TaskName $TaskName) }
        $startedRun = Get-CountdownTaskRunMark -TaskName $TaskName -Task $task
        if (Test-Path -LiteralPath $Layout.RuntimeStatusPath) { [System.IO.File]::Delete($Layout.RuntimeStatusPath) }
        [void] (& $Ports.StartTask $TaskName)
    }
    Wait-CountdownStartupReady -Layout $Layout -SessionId ([int] $Session.SessionId) -ExpectedExecutableSha256 $manifest.ExecutableSha256 -Ports $Ports -TimeoutSeconds $ReadyTimeoutSeconds -StartedRun $startedRun
}

# 现役设置及其两代备份与事务目录里的快照逐字节相同：快照里有的现役也有且字节相同，快照里没有的现役也没有。只读。
function Test-CountdownSettingsMatchSnapshot {
    param([Parameter(Mandatory)] [object] $Layout)

    $snapshotDirectory = Join-Path $Layout.TransactionDirectory 'settings-snapshot'
    foreach ($name in $script:SettingsFamilyNames) {
        $live = Join-Path $Layout.Root $name
        $saved = Join-Path $snapshotDirectory $name
        $liveExists = Test-Path -LiteralPath $live -PathType Leaf
        if ($liveExists -ne (Test-Path -LiteralPath $saved -PathType Leaf)) { return $false }
        if ($liveExists -and -not (Test-CountdownSameFileBytes -Left $live -Right $saved)) { return $false }
    }
    $true
}

# 清除事务标记（恢复完成的分界），再删除其余残留：候选、旧版本目录、事务目录（含设置快照）与没改成名的标记临时文件。调用方已经
# 把现役 app 放成要留下的那一版，之后再被打断，下一次恢复看到的只是没有标记的残留。被短暂占用时有界重试。
function Remove-CountdownUnfinishedInstallTraces {
    param(
        [Parameter(Mandatory)] [object] $Layout,
        [Parameter(Mandatory)] [int] $RetrySeconds
    )

    foreach ($path in @($Layout.MarkerPath, $Layout.CandidateDirectory, $Layout.PreviousDirectory, $Layout.TransactionDirectory, $Layout.MarkerTemporaryPath)) {
        Remove-CountdownInstallEntry -Path $path -RetrySeconds $RetrySeconds
    }
}

# 中断恢复（#284）：安装的第一步，在取得安装锁之后、前置检查之前，按事务标记与目录状态（Get-CountdownInstallRecoveryState）
# 把上一次没走完的安装收拾好，不设多阶段的事务日志。恢复表：
# - clean：没有痕迹，什么都不做。
# - leftovers：没有事务标记，视为已提交（或回滚已完成），删掉残留，现役 app 与在运行的应用不动。
# - before-replace：旧版本和设置都没被动过，清除事务标记，删除候选与事务目录。
# - mid-replace：把旧版本改名换回 app，再清除标记、删除候选与事务目录。
# - after-replace：补做启动验收（Confirm-CountdownInstalledVersionReady），新版本已在运行或达到启动就绪就提交：清除标记，删除旧版本
#   目录与事务目录。达不到时，设置及两代备份与快照逐字节相同就停下新版本、换回旧版本（新版本改回候选后删除；首次安装没有旧版本，
#   回到没有程序目录），清除标记，删除事务目录；旧版本不重新启动，随后继续的安装会启动它要装的版本。停下新版本之后再比一次设置：
#   它可能正好在这时保存了。设置已经变化时什么都不改（needs-maintainer，settings-changed）。补做验收要恰好一个交互会话，没有或
#   有多个时什么都不改（left-as-is，原因同前置检查）。
# - unrecognized：程序目录的组合不是任何一步安装能留下的，什么都不改（needs-maintainer，unrecognized-state）。
# 只有 after-replace 要交互会话：其余各状态不启动任何东西，没人登录时也照样收拾（两次改名之间停下时不收拾，下次登录旧版本起不来）。
# 每个状态都先把现役 app 放成要留下的那一版，再清除事务标记，最后删除其余残留：做到一半被打断或失败（failed，recovery-failed），
# 留下的仍是表里的某个状态，下一次安装从那里接着恢复。
# 登录任务与桌面快捷方式不动：新旧版本的 EXE 在同一路径，两者都只指向它；被打断的安装改登录任务之前导出的定义（#283）只在那次
# 安装的进程里、没有落盘，恢复无从还原。随后继续的安装按期望定义登记两者，结果照常写明改了什么。
# Report 是结果 JSON 的 recovery：state 是看到的目录状态；action 是做了什么（removed-leftovers、discarded-candidate、renamed-back、
# committed、swapped-back，没做时为空）；outcome 为 not-needed、recovered、planned（-CheckOnly 只报告看到的状态，什么都不改）、
# left-as-is、needs-maintainer 或 failed，后三种另带 Rejection（原因同 reason），调用方据此停下、不继续安装。恢复从不用快照写
# 设置：快照只供拍下它的那次安装在新版本达到启动就绪之前回滚（#283）。
function Invoke-CountdownInstallRecovery {
    param(
        [Parameter(Mandatory)] [object] $Layout,
        [Parameter(Mandatory)] [string] $TaskName,
        [Parameter(Mandatory)] [object] $Ports,
        [Parameter(Mandatory)] [int] $ReadyTimeoutSeconds,
        [Parameter(Mandatory)] [int] $StopTimeoutSeconds,
        [Parameter(Mandatory)] [int] $RetrySeconds,
        [switch] $CheckOnly
    )

    $report = [ordered]@{ state = Get-CountdownInstallRecoveryState -Layout $Layout; action = $null; outcome = 'not-needed'; reason = $null }
    # 收拾不了：什么都不再改，安装停在恢复阶段。
    $stop = {
        param([string] $Outcome, [string] $Reason, [string] $Message)
        $report.outcome = $Outcome
        $report.reason = $Reason
        [pscustomobject]@{ Report = $report; Rejection = New-CountdownInstallRejection -Reason $Reason -Message $Message }
    }
    if ($report.state -eq 'clean') { return [pscustomobject]@{ Report = $report; Rejection = $null } }
    if ($report.state -eq 'unrecognized') {
        return (& $stop 'needs-maintainer' 'unrecognized-state' '安装根里程序目录的组合不是任何一步安装能留下的（三个程序目录都在，或现役不是事务标记记下的候选却有旧版本目录，或没有现役与事务标记却有旧版本目录）：分不清哪个是旧版本，什么都没改，需要维护者决定。')
    }
    if ($CheckOnly) {
        $report.outcome = 'planned'
        return [pscustomobject]@{ Report = $report; Rejection = $null }
    }
    try {
        switch ($report.state) {
            'leftovers' {
                Remove-CountdownUnfinishedInstallTraces -Layout $Layout -RetrySeconds $RetrySeconds
                $report.action = 'removed-leftovers'
            }
            'before-replace' {
                Remove-CountdownUnfinishedInstallTraces -Layout $Layout -RetrySeconds $RetrySeconds
                $report.action = 'discarded-candidate'
            }
            'mid-replace' {
                Restore-CountdownProgramDirectory -Layout $Layout -Ports $Ports -FirstInstall $false -RetrySeconds $RetrySeconds
                Remove-CountdownUnfinishedInstallTraces -Layout $Layout -RetrySeconds $RetrySeconds
                $report.action = 'renamed-back'
            }
            'after-replace' {
                try { $session = Get-CountdownSingleInteractiveSession -Ports $Ports }
                catch {
                    $reason = $_.Exception.Data['CountdownInstallReason']
                    if ($null -eq $reason) { throw }
                    return (& $stop 'left-as-is' $reason "上一次没走完的安装已经换上新版本、还没确认启动就绪，补做启动验收要恰好一个交互会话：$($_.Exception.Message)什么都没改，等恰好一个用户登录后再安装。")
                }
                $notReady = $null
                try { [void] (Confirm-CountdownInstalledVersionReady -Layout $Layout -TaskName $TaskName -Session $session -Ports $Ports -ReadyTimeoutSeconds $ReadyTimeoutSeconds) }
                catch { $notReady = $_.Exception.Message }
                if ($null -eq $notReady) {
                    Remove-CountdownUnfinishedInstallTraces -Layout $Layout -RetrySeconds $RetrySeconds
                    $report.action = 'committed'
                }
                else {
                    $settingsUnchanged = Test-CountdownSettingsMatchSnapshot -Layout $Layout
                    if ($settingsUnchanged) {
                        [void] (Stop-CountdownInstalledApplication -Layout $Layout -TaskName $TaskName -Ports $Ports -TimeoutSeconds $StopTimeoutSeconds)
                        $settingsUnchanged = Test-CountdownSettingsMatchSnapshot -Layout $Layout
                    }
                    if (-not $settingsUnchanged) {
                        return (& $stop 'needs-maintainer' 'settings-changed' "上一次没走完的安装换上的新版本没有达到启动就绪（$notReady），设置却已经与安装前的快照不同：不换回旧版本，也不用快照覆盖设置，需要维护者决定。")
                    }
                    Restore-CountdownProgramDirectory -Layout $Layout -Ports $Ports -FirstInstall (-not (Test-Path -LiteralPath $Layout.PreviousDirectory)) -RetrySeconds $RetrySeconds
                    Remove-CountdownUnfinishedInstallTraces -Layout $Layout -RetrySeconds $RetrySeconds
                    $report.action = 'swapped-back'
                }
            }
        }
    }
    catch {
        # 每一步都先让安装根更接近收拾好的样子，做到一半停下时留下的仍是恢复表里的某个状态，下一次安装从那里接着恢复。
        return (& $stop 'failed' 'recovery-failed' "中断恢复没能做完（$($report.state)）：$($_.Exception.Message) 安装停在恢复阶段，下一次安装从留下的状态接着恢复。")
    }
    $report.outcome = 'recovered'
    [pscustomobject]@{ Report = $report; Rejection = $null }
}

# 登录任务的定义（#279「登录任务的最终定义」），其中状态命令（#276）核对的各项与它的期望一致：登录时触发，交互式、
# 非提升、不限运行时长、已在运行时忽略新实例；动作是已安装 EXE 本身，不带参数，不设工作目录。优先级取 4：#279 实测 4 的
# 优先级类、I/O 与内存优先级都与正常启动一致（5 的内存优先级低一级，默认 7 是 BelowNormal）。电池供电时也启动、拔电不停：
# 计划任务的默认值相反。允许按需启动（安装器经它启动新版本）与强制结束取计划任务的默认值。登录触发器与主体都是交互会话的
# 登录用户，注册时由 port 按会话填写。
function Get-CountdownLoginTaskDefinition {
    param([Parameter(Mandatory)] [string] $ExecutablePath)

    [pscustomobject]@{
        Enabled = $true
        Actions = @([pscustomobject]@{ Type = 'Exec'; Execute = $ExecutablePath; Arguments = $null; WorkingDirectory = $null })
        Triggers = @([pscustomobject]@{ Type = 'Logon'; Enabled = $true })
        LogonType = 'Interactive'
        RunLevel = 'Limited'
        Priority = 4
        ExecutionTimeLimit = 'PT0S'
        MultipleInstances = 'IgnoreNew'
        DisallowStartIfOnBatteries = $false
        StopIfGoingOnBatteries = $false
        AllowDemandStart = $true
        AllowHardTerminate = $true
    }
}

# 任务里的用户只按 SID 认：交互会话的登录用户、别的用户，或不限用户（没写用户）。结果里只写这三类，不写账号或 SID。
function Get-CountdownTaskUserCategory {
    param(
        [AllowNull()] [object] $Sid,
        [Parameter(Mandatory)] [object] $Session
    )

    if ([string]::IsNullOrWhiteSpace([string] $Sid)) { return 'any user' }
    $sessionSid = [string] (Get-CountdownInstallerProperty -Object $Session -Name 'Sid')
    if ([string]::Equals([string] $Sid, $sessionSid, [System.StringComparison]::OrdinalIgnoreCase)) { return 'interactive user' }
    'other user'
}

# 把一个任务读成逐项可比、可写进结果的值（#279「登录任务的最终定义」的各项）。动作与触发器各写成一行文字，用户只写类别。
function Get-CountdownLoginTaskItems {
    param(
        [Parameter(Mandatory)] [object] $Task,
        [Parameter(Mandatory)] [object] $Session
    )

    $get = { param($Object, [string] $Name) Get-CountdownInstallerProperty -Object $Object -Name $Name }
    $actions = @(& $get $Task 'Actions' | Where-Object { $null -ne $_ })
    $triggers = @(& $get $Task 'Triggers' | Where-Object { $null -ne $_ })
    $actionText = @(foreach ($action in $actions) {
            $text = "$(& $get $action 'Type') $(([string] (& $get $action 'Execute')).Trim('"'))"
            $arguments = [string] (& $get $action 'Arguments')
            $workingDirectory = [string] (& $get $action 'WorkingDirectory')
            if (-not [string]::IsNullOrWhiteSpace($arguments)) { $text += " $arguments" }
            if (-not [string]::IsNullOrWhiteSpace($workingDirectory)) { $text += " (in $workingDirectory)" }
            $text
        })
    $triggerText = @(foreach ($trigger in $triggers) {
            $type = [string] (& $get $trigger 'Type')
            $text = $type
            if (-not [bool] (& $get $trigger 'Enabled')) { $text += ' disabled' }
            if ($type -eq 'Logon') { $text += " for $(Get-CountdownTaskUserCategory -Sid (& $get $trigger 'UserSid') -Session $Session)" }
            $text
        })
    $priority = & $get $Task 'Priority'
    [ordered]@{
        enabled = [bool] (& $get $Task 'Enabled')
        action = if ($actionText.Count -eq 0) { '(none)' } else { $actionText -join '; ' }
        trigger = if ($triggerText.Count -eq 0) { '(none)' } else { $triggerText -join '; ' }
        principal = Get-CountdownTaskUserCategory -Sid (& $get $Task 'UserSid') -Session $Session
        logonType = [string] (& $get $Task 'LogonType')
        runLevel = [string] (& $get $Task 'RunLevel')
        priority = if ($null -eq $priority) { $null } else { [int] $priority }
        executionTimeLimit = [string] (& $get $Task 'ExecutionTimeLimit')
        multipleInstances = [string] (& $get $Task 'MultipleInstances')
        disallowStartIfOnBatteries = [bool] (& $get $Task 'DisallowStartIfOnBatteries')
        stopIfGoingOnBatteries = [bool] (& $get $Task 'StopIfGoingOnBatteries')
        allowDemandStart = [bool] (& $get $Task 'AllowDemandStart')
        allowHardTerminate = [bool] (& $get $Task 'AllowHardTerminate')
    }
}

# 已有登录任务与期望定义逐项比较，返回不一致的项及其改前改后的值；一致时返回空。运行时限按时长比较（PT0S 与 P0D 相同），
# 文字项不分大小写。写进结果的值截到 1000 字符。
function Compare-CountdownLoginTask {
    param(
        [Parameter(Mandatory)] [object] $Task,
        [Parameter(Mandatory)] [string] $ExecutablePath,
        [Parameter(Mandatory)] [object] $Session
    )

    $definition = Get-CountdownLoginTaskDefinition -ExecutablePath $ExecutablePath
    $sessionSid = Get-CountdownInstallerProperty -Object $Session -Name 'Sid'
    $expectedTask = $definition | Select-Object -Property *, @{ Name = 'UserSid'; Expression = { $sessionSid } }
    $expectedTask.Triggers = @($definition.Triggers | Select-Object -Property *, @{ Name = 'UserSid'; Expression = { $sessionSid } })
    $actual = Get-CountdownLoginTaskItems -Task $Task -Session $Session
    $expected = Get-CountdownLoginTaskItems -Task $expectedTask -Session $Session
    foreach ($item in $expected.Keys) {
        $same = $actual[$item] -eq $expected[$item]
        if ($item -eq 'executionTimeLimit') {
            try { $same = [System.Xml.XmlConvert]::ToTimeSpan([string] $actual[$item]) -eq [System.Xml.XmlConvert]::ToTimeSpan([string] $expected[$item]) }
            catch { $same = $false }
        }
        if (-not $same) { New-CountdownInstallChange -Item $item -Before $actual[$item] -After $expected[$item] }
    }
}

# 结果里一项改动：改前改后的值，文字截到 1000 字符。
function New-CountdownInstallChange {
    param(
        [Parameter(Mandatory)] [string] $Item,
        [AllowNull()] [object] $Before,
        [AllowNull()] [object] $After
    )

    $bound = {
        param($Value)
        if ($Value -isnot [string]) { return $Value }
        $publicValue = ConvertTo-CountdownInstallPublicText -Text $Value
        if ($publicValue.Length -gt 1000) { $publicValue.Substring(0, 1000) + '…' } else { $publicValue }
    }
    [ordered]@{ item = $Item; before = & $bound $Before; after = & $bound $After }
}

# “只检查、不改动”计划里登录任务的一步：不存在时创建，与期望定义不一致时更新，一致时没有这一步。
function Get-CountdownLoginTaskPlanStep {
    param(
        [Parameter(Mandatory)] [string] $TaskName,
        [Parameter(Mandatory)] [string] $ExecutablePath,
        [Parameter(Mandatory)] [object] $Session,
        [Parameter(Mandatory)] [object] $Ports
    )

    $task = & $Ports.GetTask $TaskName
    if ($null -eq $task) { return 'create-login-task' }
    if (@(Compare-CountdownLoginTask -Task $task -ExecutablePath $ExecutablePath -Session $Session).Count -gt 0) { return 'update-login-task' }
    $null
}

# 登录任务的登记（#281）：不存在时按期望定义创建；存在时逐项比较，一致时不动，不一致时按期望定义整份重新注册，
# 结果写明改了哪些项。常规升级里任务定义与期望一致，所以不改动计划任务。动任务之前把它原来的样子记进事务状态，供回滚
# 恢复（#283）：要更新时先经 port 导出整份定义（逐项比较的读法不含任务的全部设置），导不出就不改。
function Register-CountdownLoginTask {
    param(
        [Parameter(Mandatory)] [string] $TaskName,
        [Parameter(Mandatory)] [string] $ExecutablePath,
        [Parameter(Mandatory)] [object] $Session,
        [Parameter(Mandatory)] [object] $Ports,
        [Parameter(Mandatory)] [object] $Transaction
    )

    $definition = Get-CountdownLoginTaskDefinition -ExecutablePath $ExecutablePath
    $task = & $Ports.GetTask $TaskName
    if ($null -eq $task) {
        $Transaction.LoginTaskChangeStarted = $true
        [void] (& $Ports.RegisterTask $TaskName $definition $Session)
        return [ordered]@{ action = 'created'; changes = @() }
    }
    $changes = @(Compare-CountdownLoginTask -Task $task -ExecutablePath $ExecutablePath -Session $Session)
    if ($changes.Count -eq 0) { return [ordered]@{ action = 'unchanged'; changes = @() } }
    $exported = & $Ports.ExportTask $TaskName
    if ($null -eq $exported) { throw "导出不了登录任务 $TaskName 改之前的定义，不改动它。" }
    $Transaction.LoginTaskBefore = $task
    $Transaction.LoginTaskBeforeDefinition = $exported
    $Transaction.LoginTaskChangeStarted = $true
    [void] (& $Ports.RegisterTask $TaskName $definition $Session)
    [ordered]@{ action = 'updated'; changes = $changes }
}

# 桌面快捷方式的期望定义（#281）：目标是已安装 EXE，不带参数，从程序目录启动，图标取 EXE 里的第一个图标。
function Get-CountdownDesktopShortcutDefinition {
    param([Parameter(Mandatory)] [string] $ExecutablePath)

    [pscustomobject]@{
        TargetPath = $ExecutablePath
        Arguments = ''
        WorkingDirectory = [System.IO.Path]::GetDirectoryName($ExecutablePath)
        IconLocation = "$ExecutablePath,0"
    }
}

# 交互用户的桌面目录：由 port 按交互会话读出，必须是完整路径；读不到时在前置检查拒绝。只解析位置，不访问桌面上的东西，
# 所以只检查时也解析它。
function Resolve-CountdownDesktopDirectory {
    param(
        [Parameter(Mandatory)] [object] $Session,
        [Parameter(Mandatory)] [object] $Ports
    )

    try { $directory = [string] (& $Ports.GetDesktopDirectory $Session) }
    catch { throw (New-CountdownInstallRejection -Reason 'desktop-unavailable' -Message "读不到交互用户的桌面目录：$($_.Exception.Message)") }
    if ([string]::IsNullOrWhiteSpace($directory) -or -not [System.IO.Path]::IsPathFullyQualified($directory)) {
        throw (New-CountdownInstallRejection -Reason 'desktop-unavailable' -Message '交互用户的桌面目录不是完整路径。')
    }
    [System.IO.Path]::GetFullPath($directory)
}

# 桌面快捷方式的登记（#281）。“属于安装器”的判定：桌面上同名的项是一个 Shell Link（文件头是 Shell Link 的头长与 CLSID），
# 且目标是本安装根的已安装 EXE（路径不分大小写）。缺失时创建；属于安装器时逐项比较参数、工作目录与图标，一致时不动，
# 不一致时改回期望定义，结果写明改了什么；不属于安装器（目标指向别处的快捷方式、碰巧同名的普通文件或目录）时原样保留，
# 结果写明原因，不写它指向哪里。
function Register-CountdownDesktopShortcut {
    param(
        [Parameter(Mandatory)] [string] $DesktopDirectory,
        [Parameter(Mandatory)] [string] $ExecutablePath,
        [Parameter(Mandatory)] [object] $Ports
    )

    $name = $script:DesktopShortcutName
    $path = Join-Path $DesktopDirectory $name
    $definition = Get-CountdownDesktopShortcutDefinition -ExecutablePath $ExecutablePath
    $existing = & $Ports.ReadShortcut $path
    if ($null -eq $existing) {
        [void] (& $Ports.WriteShortcut $path $definition)
        return [ordered]@{ name = $name; action = 'created'; reason = $null; changes = @() }
    }
    $target = ([string] (Get-CountdownInstallerProperty -Object $existing -Name 'TargetPath')).Trim('"')
    $foreignReason = if (-not [bool] (Get-CountdownInstallerProperty -Object $existing -Name 'IsShellLink')) { 'not-a-shell-link' }
    elseif (-not [string]::Equals($target, $definition.TargetPath, [System.StringComparison]::OrdinalIgnoreCase)) { 'other-target' }
    if ($null -ne $foreignReason) { return [ordered]@{ name = $name; action = 'kept-foreign'; reason = $foreignReason; changes = @() } }
    $changes = @(foreach ($item in @('Arguments', 'WorkingDirectory', 'IconLocation')) {
            $before = [string] (Get-CountdownInstallerProperty -Object $existing -Name $item)
            if (-not [string]::Equals($before, $definition.$item, [System.StringComparison]::OrdinalIgnoreCase)) {
                New-CountdownInstallChange -Item ($item.Substring(0, 1).ToLowerInvariant() + $item.Substring(1)) -Before $before -After $definition.$item
            }
        })
    if ($changes.Count -eq 0) { return [ordered]@{ name = $name; action = 'unchanged'; reason = $null; changes = @() } }
    [void] (& $Ports.WriteShortcut $path $definition)
    [ordered]@{ name = $name; action = 'updated'; reason = $null; changes = $changes }
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
# 它仍指向 adapter）。就绪与否不看任务状态或上次结果；经登录任务启动时（-StartedRun 是启动之前记下的任务），还没就绪就
# 再看那次运行结束了没有：结束了、已安装 EXE 也没有进程在运行，就不再等满时限，按 startup-exited 报告退出码（#288）。
function Wait-CountdownStartupReady {
    param(
        [Parameter(Mandatory)] [object] $Layout,
        [Parameter(Mandatory)] [int] $SessionId,
        [Parameter(Mandatory)] [string] $ExpectedExecutableSha256,
        [Parameter(Mandatory)] [object] $Ports,
        [Parameter(Mandatory)] [int] $TimeoutSeconds,
        [AllowNull()] [object] $StartedRun
    )

    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    $reason = '运行状态回执尚未出现。'
    $sawRunning = $false
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
        if ($null -ne $StartedRun) {
            $task = & $Ports.GetTask $StartedRun.TaskName
            if ([string] (Get-CountdownInstallerProperty -Object $task -Name 'State') -eq 'Running') { $sawRunning = $true }
            $exitCode = Get-CountdownStartedRunResult -Mark $StartedRun -Task $task -SawRunning $sawRunning
            if ($null -ne $exitCode -and @(Get-CountdownInstalledProcesses -Layout $Layout -Ports $Ports).Count -eq 0) {
                throw (New-CountdownStartupExitedRejection -ExitCode $exitCode)
            }
        }
        if ([DateTime]::UtcNow -ge $deadline) { break }
        Start-Sleep -Milliseconds 250
    }
    throw (New-CountdownInstallRejection -Reason 'startup-not-ready' -Message "$TimeoutSeconds 秒内没有达到启动就绪：$reason")
}

# 公开结果保留本机 Users 路径的形状，但把账户一段换成 <user>；替换是幂等的。
function ConvertTo-CountdownInstallPublicText {
    param([AllowNull()] [AllowEmptyString()] [string] $Text)

    [regex]::Replace([string] $Text, '(?i)([\\/]+Users[\\/]+)[^\\/:*?"<>|\r\n]+', {
            param($Match)
            $Match.Groups[1].Value + '<user>'
        })
}

function ConvertTo-CountdownBoundedError {
    param([AllowEmptyString()] [string] $Message)

    $text = ((ConvertTo-CountdownInstallPublicText -Text $Message) -replace '\s+', ' ').Trim()
    if ($text.Length -gt 400) { $text = $text.Substring(0, 400) + '…' }
    $text
}

function Install-CountdownProgram {
    <#
    .SYNOPSIS
    把包里的程序目录装到安装根：首次安装与升级同一流程，等到启动就绪后提交，返回结果（由入口脚本输出为 JSON）。
    .DESCRIPTION
    步骤：检查安装路径并记下设置指纹（只读，被安装锁拒绝时结果里也有指纹）；取得全机安装锁；中断恢复（#284，见
    Invoke-CountdownInstallRecovery）：安装根留有上次没走完的安装时先按恢复表收拾好，收拾不了时停在恢复阶段、不继续安装；
    前置检查（唯一的交互会话及其桌面目录、把候选程序目录放到安装根下并按载荷清单逐项核对）；
    停止应用（先结束运行中的登录任务实例并给进程有界正常退出时间，再按 PID 复核并结束剩余进程；结果分别记开始时、正常退出与强杀的进程数）；写下事务标记；把设置及其两代备份快照到事务目录；
    有设置文件时用候选 EXE 在事务目录里的副本上演练迁移，并用产品的等价判定核对快照与迁移结果；现役程序目录改名为
    旧版本目录、候选改名为现役；登录任务不存在时创建、与期望定义不一致时更新；登记桌面快捷方式（缺失时创建，属于安装器时
    改回期望定义，不属于时保留）；确认任务仍没有运行中的实例，删除旧回执，经登录任务启动，等到启动就绪（启动的进程在
    启动就绪之前就退出时不等满时限，原因 startup-exited，结果带它的退出码，#288）；
    先清除事务标记，再删除旧版本目录与事务目录。前置检查任何一项失败时只删除候选（及为它新建的安装根），
    其余什么都不改。写下事务标记之后、新版本达到启动就绪之前任何一步失败都回滚（#283，见 Undo-CountdownInstall）：
    停止新版本，把程序目录换回，用本次安装的快照恢复设置及两代备份，恢复本次改过的登录任务，经登录任务重新启动旧版本；
    快照或演练失败、迁移不等价时程序目录与现役设置都没被动过，只需重新启动旧版本。新版本达到启动就绪之后不再回滚，也不再
    用快照覆盖设置：提交没走完时事务标记留给下一次安装的中断恢复。目录改名与安装器自己的删除被短暂占用时有界重试
    （-FileLockRetrySeconds）。-CheckOnly 执行到前置检查为止，只核对包本身、不放候选、不读桌面上的快捷方式，也不做中断
    恢复（只报告看到的状态，恢复排在将要执行的步骤最前面），报告将要执行的步骤。
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
        [ValidateRange(1, 300)] [int] $StopTimeoutSeconds = 10,
        [ValidateRange(0, 300)] [int] $FileLockRetrySeconds = 15
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
        # 中断恢复（#284）：安装停在安装锁之前时为空。
        recovery = $null
        package = $null
        firstInstall = $null
        plannedSteps = @()
        loginTask = $null
        desktopShortcut = $null
        stoppedProcessCount = 0
        exitedProcessCount = 0
        killedProcessCount = 0
        readyProcess = $null
        # 新版本在启动就绪之前就退出时（startup-exited，#288）它的退出码，取自登录任务的上次结果；其余情况为空。
        startupExitCode = $null
        settings = [ordered]@{ before = $null; after = $null }
        settingsRehearsal = $null
        restartedProcess = $null
        # 回滚（#283）：outcome 为 not-needed（没有要撤回的改动，或新版本已达到启动就绪）、rolled-back 或 rollback-failed。
        rollback = [ordered]@{ outcome = 'not-needed'; settings = $null; settingsLiveCopies = @(); loginTask = $null; desktopShortcut = $null; problems = @() }
        transactionMarkerPresent = $false
        diagnosticLogPath = $null
    }
    $stage = 'preflight'
    $lock = $null
    $layout = $null
    $rootCreated = $false
    $candidateOwned = $false
    $transaction = $null
    try {
        # 安装路径检查与设置指纹只读、不需要锁：被安装锁拒绝的结果里也有设置指纹。
        $layout = Resolve-CountdownInstallerLayout -InstallRoot $InstallRoot
        $result.installRoot = $layout.Root
        # 应用的诊断日志（新版本起不来的原因在这里），在安装根里，安装与回滚都不动它。
        $result.diagnosticLogPath = Join-Path $layout.Root 'logs\application.jsonl'
        $result.settings.before = Get-CountdownSettingsFingerprint -Path $layout.SettingsPath

        $stage = 'lock'
        $lock = Enter-CountdownInstallLock -Name $LockName

        $stage = 'recovery'
        $recovery = Invoke-CountdownInstallRecovery -Layout $layout -TaskName $TaskName -Ports $Ports -ReadyTimeoutSeconds $ReadyTimeoutSeconds `
            -StopTimeoutSeconds $StopTimeoutSeconds -RetrySeconds $FileLockRetrySeconds -CheckOnly:$CheckOnly
        $result.recovery = $recovery.Report
        if ($null -ne $recovery.Rejection) { throw $recovery.Rejection }

        $stage = 'preflight'
        $session = Get-CountdownSingleInteractiveSession -Ports $Ports
        $desktopDirectory = Resolve-CountdownDesktopDirectory -Session $session -Ports $Ports
        $sourceDirectory = Join-Path (Resolve-CountdownInstallerPath -Path $PackageRoot) 'app'
        $manifest = Read-CountdownPayloadManifest -ProgramDirectory $sourceDirectory
        $result.package = [ordered]@{ commit = $manifest.Commit; aggregateSha256 = $manifest.AggregateSha256 }
        # 只检查时没做的恢复会把旧版本目录换回 app，因此旧版本目录在也不算首次安装；恢复做完之后不会留下旧版本目录。
        $result.firstInstall = -not (Test-Path -LiteralPath $layout.ProgramDirectory -PathType Container) -and -not (Test-Path -LiteralPath $layout.PreviousDirectory)
        $plan = [System.Collections.Generic.List[string]]::new()
        if ($result.recovery.outcome -eq 'planned') { $plan.Add('recover-unfinished-install') }
        $plan.AddRange([string[]] @('place-candidate', 'stop-application', 'write-transaction-marker', 'snapshot-settings'))
        if (Test-Path -LiteralPath $layout.SettingsPath -PathType Leaf) { $plan.Add('settings-rehearsal') }
        $plan.Add('replace-program-directory')
        $loginTaskStep = Get-CountdownLoginTaskPlanStep -TaskName $TaskName -ExecutablePath $layout.ExecutablePath -Session $session -Ports $Ports
        if ($null -ne $loginTaskStep) { $plan.Add($loginTaskStep) }
        $plan.AddRange([string[]] @('register-desktop-shortcut', 'start-and-wait-ready', 'commit'))

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
            $stopped = Stop-CountdownInstalledApplication -Layout $layout -TaskName $TaskName -Ports $Ports -TimeoutSeconds $StopTimeoutSeconds
            $result.stoppedProcessCount = $stopped.ProcessCount
            $result.exitedProcessCount = $stopped.ExitedProcessCount
            $result.killedProcessCount = $stopped.KilledProcessCount

            # 从这里起失败都由回滚接手（#283），候选随回滚处理。
            $stage = 'write-transaction-marker'
            $transaction = New-CountdownInstallTransactionState -FirstInstall $result.firstInstall
            $candidateOwned = $false
            Write-CountdownInstallMarker -Layout $layout -Manifest $manifest

            # 换程序之前的设置保护（#282）：这一段失败时程序目录与现役设置都没被动过，回滚只需重新启动旧版本。
            $stage = 'snapshot-settings'
            $transaction.SnapshotDirectory = Save-CountdownSettingsSnapshot -Layout $layout
            $stage = 'settings-rehearsal'
            $rehearsal = Invoke-CountdownSettingsRehearsal -Layout $layout -SnapshotDirectory $transaction.SnapshotDirectory -Ports $Ports
            $result.settingsRehearsal = $rehearsal.Report
            if ($null -ne $rehearsal.Rejection) { throw $rehearsal.Rejection }

            $stage = 'replace-program-directory'
            Switch-CountdownProgramDirectory -Layout $layout -Ports $Ports -RetrySeconds $FileLockRetrySeconds
            $transaction.ProgramReplaced = $true

            $stage = 'register-login-task'
            $result.loginTask = Register-CountdownLoginTask -TaskName $TaskName -ExecutablePath $layout.ExecutablePath -Session $session -Ports $Ports -Transaction $transaction

            $stage = 'register-desktop-shortcut'
            $result.desktopShortcut = Register-CountdownDesktopShortcut -DesktopDirectory $desktopDirectory -ExecutablePath $layout.ExecutablePath -Ports $Ports
            $transaction.ShortcutCreated = $result.desktopShortcut.action -eq 'created'

            $stage = 'start-and-wait-ready'
            $taskBeforeStart = & $Ports.GetTask $TaskName
            if (Test-CountdownTaskRunning -Task $taskBeforeStart) { throw (New-CountdownTaskStillRunningRejection -TaskName $TaskName) }
            $startedRun = Get-CountdownTaskRunMark -TaskName $TaskName -Task $taskBeforeStart
            if (Test-Path -LiteralPath $layout.RuntimeStatusPath) { [System.IO.File]::Delete($layout.RuntimeStatusPath) }
            [void] (& $Ports.StartTask $TaskName)
            $ready = Wait-CountdownStartupReady -Layout $layout -SessionId ([int] $session.SessionId) -ExpectedExecutableSha256 $manifest.ExecutableSha256 -Ports $Ports -TimeoutSeconds $ReadyTimeoutSeconds -StartedRun $startedRun
            # 新版本达到启动就绪：从此不再回滚，也不再用快照覆盖设置（用户可能已经在编辑）。
            $transaction.NewVersionReady = $true
            $result.readyProcess = [ordered]@{ processId = [int] $ready.ProcessId; sessionId = [int] $ready.SessionId }

            $stage = 'commit'
            Remove-CountdownInstallEntry -Path $layout.MarkerPath -RetrySeconds $FileLockRetrySeconds
            Remove-CountdownInstallEntry -Path $layout.PreviousDirectory -RetrySeconds $FileLockRetrySeconds
            Remove-CountdownInstallEntry -Path $layout.TransactionDirectory -RetrySeconds $FileLockRetrySeconds
            $result.succeeded = $true
        }
    }
    catch {
        $result.failedStage = $stage
        $reason = $_.Exception.Data['CountdownInstallReason']
        $result.reason = if ($null -ne $reason) { [string] $reason } else { 'unexpected-error' }
        $result.error = ConvertTo-CountdownBoundedError -Message $_.Exception.Message
        $startupExitCode = $_.Exception.Data['CountdownStartupExitCode']
        if ($null -ne $startupExitCode) { $result.startupExitCode = [long] $startupExitCode }
        if ($candidateOwned) {
            # 事务标记写下之前失败：删除候选（以及为它新建、此刻已空的安装根）；停止阶段在旧版本进程已退出后失败时，另按 RA5 重启旧版本。
            try {
                if (Test-Path -LiteralPath $layout.CandidateDirectory) { [System.IO.Directory]::Delete($layout.CandidateDirectory, $true) }
                if ($rootCreated -and @(Get-ChildItem -LiteralPath $layout.Root -Force).Count -eq 0) { [System.IO.Directory]::Delete($layout.Root) }
            }
            catch { $result.error = ConvertTo-CountdownBoundedError -Message "$($result.error)；删除候选失败：$($_.Exception.Message)" }
        }
        if ($stage -eq 'stop-application' -and $result.firstInstall -eq $false) {
            # RA5：停止动作已经结束了旧版本、随后才报错时，不把用户留在无应用状态；安装仍按原错误失败。
            try {
                $remaining = @(Get-CountdownInstalledProcesses -Layout $layout -Ports $Ports)
                $task = & $Ports.GetTask $TaskName
                if ($remaining.Count -eq 0 -and -not (Test-CountdownTaskRunning -Task $task)) {
                    $result.restartedProcess = Start-CountdownPreviousVersion -Layout $layout -TaskName $TaskName -Session $session -Ports $Ports -ReadyTimeoutSeconds $ReadyTimeoutSeconds
                }
            }
            catch { $result.error = ConvertTo-CountdownBoundedError -Message "$($result.error)；重新启动旧版本失败：$($_.Exception.Message)" }
        }
        if ($null -ne $transaction -and -not $transaction.NewVersionReady) {
            $undo = Undo-CountdownInstall -Layout $layout -TaskName $TaskName -Session $session -DesktopDirectory $desktopDirectory -Transaction $transaction -Ports $Ports `
                -ReadyTimeoutSeconds $ReadyTimeoutSeconds -StopTimeoutSeconds $StopTimeoutSeconds -RetrySeconds $FileLockRetrySeconds
            $result.rollback = $undo.Report
            $result.restartedProcess = $undo.RestartedProcess
        }
    }
    finally {
        Exit-CountdownInstallLock -Mutex $lock
        if ($null -ne $layout) {
            try { $result.settings.after = Get-CountdownSettingsFingerprint -Path $layout.SettingsPath }
            catch { $result.settings.after = $null }
            $result.transactionMarkerPresent = Test-Path -LiteralPath $layout.MarkerPath
        }
    }
    $result
}

function New-CountdownInstallerWindowsPorts {
    [pscustomobject]@{
        # 交互会话按 explorer.exe 所在的会话认：同一会话里的多个 explorer 进程算一个会话。只有一个会话时才读它的登录用户
        # 与 SID（登记登录任务、找桌面目录要用）；多个会话反正会被拒绝，读别的用户的进程属主可能没有权限，不去读。
        GetInteractiveSessions = {
            $sessions = @(Get-CimInstance Win32_Process -Filter "Name='explorer.exe'" -OperationTimeoutSec 10 -ErrorAction Stop |
                    Where-Object { [int] $_.SessionId -gt 0 } | Group-Object -Property SessionId)
            if ($sessions.Count -ne 1) {
                return @($sessions | ForEach-Object { [pscustomobject]@{ SessionId = [int] $_.Name; User = $null; Sid = $null } })
            }
            $owner = Invoke-CimMethod -InputObject $sessions[0].Group[0] -MethodName GetOwner -OperationTimeoutSec 10 -ErrorAction Stop
            $ownerSid = Invoke-CimMethod -InputObject $sessions[0].Group[0] -MethodName GetOwnerSid -OperationTimeoutSec 10 -ErrorAction Stop
            if ([int] $owner.ReturnValue -ne 0 -or [string]::IsNullOrWhiteSpace([string] $owner.User) -or
                [int] $ownerSid.ReturnValue -ne 0 -or [string]::IsNullOrWhiteSpace([string] $ownerSid.Sid)) {
                throw "无法读取交互会话 $($sessions[0].Name) 的登录用户。"
            }
            [pscustomobject]@{
                SessionId = [int] $sessions[0].Name
                User = if ([string]::IsNullOrWhiteSpace([string] $owner.Domain)) { [string] $owner.User } else { "$($owner.Domain)\$($owner.User)" }
                Sid = [string] $ownerSid.Sid
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
        # 先给任务启动的应用一次正常退出机会；任务外启动的实例由调用方在有界等待后按 PID 复核并结束。
        EndTask = {
            param([string] $Name)
            Stop-ScheduledTask -TaskPath '\' -TaskName $Name -ErrorAction Stop
        }
        # 状态命令（#276）读出的各项之外，再读登记登录任务要逐项比较的工作目录、电池、按需启动、强制结束与用户（#281），
        # 以及上次运行时间与上次结果（#288：等待启动就绪时凭它们认出启动的那次运行已经结束，上次结果就是进程的退出码）。
        # 任务计划给主体写的是账号名、给登录触发器写的是“域\账号”，都换成 SID；换不出时原样返回，比较时算作别的用户。
        # port 在别的作用域里被调用，用到的辅助块都定义在块内。
        GetTask = {
            param([string] $Name)
            $field = { param($Object, [string] $Property) $value = $Object.PSObject.Properties[$Property]; if ($value) { $value.Value } }
            try { $task = Get-ScheduledTask -TaskPath '\' -TaskName $Name -ErrorAction Stop }
            catch { if ($_.CategoryInfo.Category -eq 'ObjectNotFound') { return $null }; throw }
            $info = Get-ScheduledTaskInfo -InputObject $task -ErrorAction Stop
            $kind = { param($o) $o.CimClass.CimClassName -replace '^MSFT_Task|(Action|Trigger)$' }
            $sid = {
                param([string] $Account)
                if ([string]::IsNullOrWhiteSpace($Account)) { return $null }
                try { ([System.Security.Principal.NTAccount] $Account).Translate([System.Security.Principal.SecurityIdentifier]).Value }
                catch { $Account }
            }
            [pscustomobject]@{
                State = [string] $task.State
                Enabled = [bool] $task.Settings.Enabled
                Actions = @($task.Actions | Where-Object { $_ } | ForEach-Object { [pscustomobject]@{ Type = & $kind $_; Execute = & $field $_ 'Execute'; Arguments = & $field $_ 'Arguments'; WorkingDirectory = & $field $_ 'WorkingDirectory' } })
                Triggers = @($task.Triggers | Where-Object { $_ } | ForEach-Object { [pscustomobject]@{ Type = & $kind $_; Enabled = [bool] $_.Enabled; UserSid = & $sid ([string] (& $field $_ 'UserId')) } })
                UserSid = & $sid ([string] $task.Principal.UserId)
                LogonType = [string] $task.Principal.LogonType
                RunLevel = [string] $task.Principal.RunLevel
                Priority = [int] $task.Settings.Priority
                ExecutionTimeLimit = [string] $task.Settings.ExecutionTimeLimit
                MultipleInstances = [string] $task.Settings.MultipleInstances
                DisallowStartIfOnBatteries = [bool] $task.Settings.DisallowStartIfOnBatteries
                StopIfGoingOnBatteries = [bool] $task.Settings.StopIfGoingOnBatteries
                AllowDemandStart = [bool] $task.Settings.AllowDemandStart
                AllowHardTerminate = [bool] $task.Settings.AllowHardTerminate
                LastRunTime = $info.LastRunTime
                LastTaskResult = if ($null -ne $info.LastTaskResult) { [long] $info.LastTaskResult } else { $null }
            }
        }
        # 按 #279「登录任务的最终定义」注册；同名任务已存在时整份替换（与期望定义不一致时的更新）。
        # #288 在真实任务上实测：非提升（UAC 筛选后的管理员令牌）时能注册带登录触发器的任务，也能整份替换非提升时注册的
        # 同名任务。能否替换提升时注册的任务（旧安装器在管理员 PowerShell 里注册）、标准用户能否注册仍未实测。
        RegisterTask = {
            param([string] $Name, [object] $Definition, [object] $Session)
            $action = New-ScheduledTaskAction -Execute ([string] $Definition.Actions[0].Execute)
            $trigger = New-ScheduledTaskTrigger -AtLogOn -User ([string] $Session.User)
            $principal = New-ScheduledTaskPrincipal -UserId ([string] $Session.User) -LogonType ([string] $Definition.LogonType) -RunLevel ([string] $Definition.RunLevel)
            $settings = New-ScheduledTaskSettingsSet -Priority ([int] $Definition.Priority) `
                -ExecutionTimeLimit ([System.Xml.XmlConvert]::ToTimeSpan([string] $Definition.ExecutionTimeLimit)) `
                -MultipleInstances ([string] $Definition.MultipleInstances) `
                -AllowStartIfOnBatteries:(-not [bool] $Definition.DisallowStartIfOnBatteries) `
                -DontStopIfGoingOnBatteries:(-not [bool] $Definition.StopIfGoingOnBatteries) `
                -DisallowDemandStart:(-not [bool] $Definition.AllowDemandStart) `
                -DisallowHardTerminate:(-not [bool] $Definition.AllowHardTerminate) `
                -Disable:(-not [bool] $Definition.Enabled)
            Register-ScheduledTask -TaskPath '\' -TaskName $Name -Action $action -Trigger $trigger -Principal $principal -Settings $settings `
                -Description '登录后直接启动敏敏子倒计时。' -Force -ErrorAction Stop | Out-Null
        }
        StartTask = {
            param([string] $Name)
            Start-ScheduledTask -TaskPath '\' -TaskName $Name -ErrorAction Stop
        }
        # 登录任务的整份定义（#283）：任务计划导出的 XML，含逐项比较不读的设置；任务不存在时返回空。只在要更新任务之前调用。
        ExportTask = {
            param([string] $Name)
            try { [void] (Get-ScheduledTask -TaskPath '\' -TaskName $Name -ErrorAction Stop) }
            catch { if ($_.CategoryInfo.Category -eq 'ObjectNotFound') { return $null }; throw }
            [string] (Export-ScheduledTask -TaskPath '\' -TaskName $Name -ErrorAction Stop)
        }
        # 回滚时把登录任务恢复成导出的定义：按 XML 整份重新注册（同名任务整份替换）；定义为空表示安装前没有这个任务，删除它。
        # #288 实测非提升时按导出的 XML 恢复（恢复后再导出与原导出逐字节相同）和删除都可行；与 RegisterTask 一样，
        # 提升时注册的任务仍未实测。
        RestoreTask = {
            param([string] $Name, [object] $Definition)
            if ($null -eq $Definition) {
                Unregister-ScheduledTask -TaskPath '\' -TaskName $Name -Confirm:$false -ErrorAction Stop
                return
            }
            Register-ScheduledTask -TaskPath '\' -TaskName $Name -Xml ([string] $Definition) -Force -ErrorAction Stop | Out-Null
        }
        # 切换脚本（#259 评审）：导出的定义（ExportTask 的 XML）里回退恢复之前要核对的几项。唯一主体的用户 SID（账号名换成 SID，
        # 换不出时原样返回；没有或不止一个主体时为空）、运行级别（HighestAvailable 为 Highest，其余为 Limited）与各个动作
        # （类型、命令、参数、工作目录）。
        ReadTaskDefinition = {
            param([string] $Definition)
            $document = [System.Xml.XmlDocument]::new()
            $document.LoadXml($Definition)
            $namespaces = [System.Xml.XmlNamespaceManager]::new($document.NameTable)
            $namespaces.AddNamespace('t', 'http://schemas.microsoft.com/windows/2004/02/mit/task')
            $value = { param([System.Xml.XmlNode] $Node, [string] $Name) $found = $Node.SelectSingleNode("t:$Name", $namespaces); if ($null -ne $found) { $found.InnerText } }
            $principals = @($document.SelectNodes('/t:Task/t:Principals/t:Principal', $namespaces))
            $userId = if ($principals.Count -eq 1) { [string] (& $value $principals[0] 'UserId') }
            $userSid = if ([string]::IsNullOrWhiteSpace($userId) -or $userId.StartsWith('S-1-')) { $userId }
            else {
                try { ([System.Security.Principal.NTAccount] $userId).Translate([System.Security.Principal.SecurityIdentifier]).Value }
                catch { $userId }
            }
            [pscustomobject]@{
                UserSid = $userSid
                RunLevel = if ($principals.Count -eq 1 -and (& $value $principals[0] 'RunLevel') -ceq 'HighestAvailable') { 'Highest' } else { 'Limited' }
                Actions = @(foreach ($action in @($document.SelectNodes('/t:Task/t:Actions/*', $namespaces))) {
                        [pscustomobject]@{ Type = $action.LocalName; Execute = & $value $action 'Command'; Arguments = & $value $action 'Arguments'; WorkingDirectory = & $value $action 'WorkingDirectory' }
                    })
            }
        }
        # 切换脚本（#259 评审）：只给 SYSTEM 与 Administrators 写的目录，提权运行的切换往里写备份、回退从里面读。不继承上级的
        # 权限，SYSTEM 与 Administrators 完全控制，读者（交互用户）只能读与执行，子目录与文件都继承这三条。目录已在时拒绝，
        # 不改别人建的目录的权限；不在的上级目录照常建出（按它自己的上级继承）。
        CreateProtectedDirectory = {
            param([string] $Path, [string] $ReaderSid)
            if (Test-Path -LiteralPath $Path) { throw "目录已存在：$Path" }
            [void] [System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName($Path))
            $security = [System.Security.AccessControl.DirectorySecurity]::new()
            $security.SetAccessRuleProtection($true, $false)
            foreach ($rule in @(@('S-1-5-18', 'FullControl'), @('S-1-5-32-544', 'FullControl'), @($ReaderSid, 'ReadAndExecute'))) {
                $security.AddAccessRule([System.Security.AccessControl.FileSystemAccessRule]::new(
                        [System.Security.Principal.SecurityIdentifier]::new([string] $rule[0]), [System.Security.AccessControl.FileSystemRights] $rule[1],
                        [System.Security.AccessControl.InheritanceFlags] 'ContainerInherit, ObjectInherit', [System.Security.AccessControl.PropagationFlags]::None,
                        [System.Security.AccessControl.AccessControlType]::Allow))
            }
            [System.IO.FileSystemAclExtensions]::Create([System.IO.DirectoryInfo]::new($Path), $security)
        }
        # 交互用户的桌面目录（#281）：取该用户注册表 User Shell Folders 里 Desktop 的原值，只按该用户的注册表环境展开
        # （机器环境、该用户的 Environment 与 Volatile Environment，USERPROFILE 取 ProfileList 里的配置文件目录），不用当前进程的
        # 环境：经 SSH 运行时那不是桌面会话的环境（docs/办公室部署.md）。只读注册表，不访问桌面目录本身。
        GetDesktopDirectory = {
            param([object] $Session)
            $sid = [string] $Session.Sid
            if ([string]::IsNullOrWhiteSpace($sid)) { throw '不知道交互会话登录用户的 SID。' }
            $readValues = {
                param([Microsoft.Win32.RegistryKey] $Hive, [string] $Path)
                $values = @{}
                $key = $Hive.OpenSubKey($Path)
                if ($null -ne $key) {
                    try { foreach ($name in $key.GetValueNames()) { $values[$name] = [string] $key.GetValue($name, $null, 'DoNotExpandEnvironmentNames') } }
                    finally { $key.Dispose() }
                }
                $values
            }
            $users = [Microsoft.Win32.Registry]::Users
            $machine = [Microsoft.Win32.Registry]::LocalMachine
            $template = (& $readValues $users "$sid\Software\Microsoft\Windows\CurrentVersion\Explorer\User Shell Folders")['Desktop']
            $profileImagePath = (& $readValues $machine "SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList\$sid")['ProfileImagePath']
            if ([string]::IsNullOrWhiteSpace($template) -or [string]::IsNullOrWhiteSpace($profileImagePath)) { throw '注册表里没有交互用户的桌面位置或配置文件目录。' }
            $variables = @{}
            $systemRoot = (& $readValues $machine 'SOFTWARE\Microsoft\Windows NT\CurrentVersion')['SystemRoot']
            if ([System.IO.Path]::IsPathFullyQualified([string] $systemRoot)) {
                $variables['SystemRoot'] = $systemRoot
                $variables['SystemDrive'] = [System.IO.Path]::GetPathRoot($systemRoot).TrimEnd('\')
            }
            foreach ($source in @(
                    (& $readValues $machine 'SYSTEM\CurrentControlSet\Control\Session Manager\Environment'),
                    (& $readValues $users "$sid\Environment"),
                    (& $readValues $users "$sid\Volatile Environment"))) {
                foreach ($name in $source.Keys) { $variables[$name] = $source[$name] }
            }
            $expand = {
                param([string] $Text)
                for ($round = 0; $round -lt 8 -and $Text -match '%[^%]+%'; $round++) {
                    $Text = [regex]::Replace($Text, '%([^%]+)%', [System.Text.RegularExpressions.MatchEvaluator] {
                            param($Match)
                            if ($variables.ContainsKey($Match.Groups[1].Value)) { $variables[$Match.Groups[1].Value] } else { $Match.Value }
                        })
                }
                if ($Text -match '%([^%]+)%') { throw "桌面位置里的 %$($Matches[1])% 按交互用户的环境展开不了。" }
                $Text
            }
            $variables['USERPROFILE'] = & $expand $profileImagePath
            & $expand $template
        }
        # 桌面上同名的项（#281）：不存在时返回空。文件头是 Shell Link 的头长 0x4C 与 CLSID 00021401-0000-0000-C000-000000000046 时
        # 经 WScript.Shell 读出目标、参数、工作目录与图标；同名的目录或别的文件只报告不是 Shell Link，不解析它的内容。
        ReadShortcut = {
            param([string] $Path)
            if (-not (Test-Path -LiteralPath $Path)) { return $null }
            $isShellLink = $false
            if (Test-Path -LiteralPath $Path -PathType Leaf) {
                $stream = [System.IO.File]::Open($Path, 'Open', 'Read', 'ReadWrite, Delete')
                try { $header = [System.IO.BinaryReader]::new($stream).ReadBytes(20) }
                finally { $stream.Dispose() }
                $isShellLink = [Convert]::ToHexString($header) -eq '4C0000000114020000000000C000000000000046'
            }
            if (-not $isShellLink) { return [pscustomobject]@{ IsShellLink = $false } }
            $link = (New-Object -ComObject WScript.Shell).CreateShortcut($Path)
            [pscustomobject]@{
                IsShellLink = $true
                TargetPath = [string] $link.TargetPath
                Arguments = [string] $link.Arguments
                WorkingDirectory = [string] $link.WorkingDirectory
                IconLocation = [string] $link.IconLocation
            }
        }
        # 按期望定义写快捷方式：不存在时新建；已有的（属于安装器的）只改这四项，其余属性（如快捷键）照旧。
        WriteShortcut = {
            param([string] $Path, [object] $Definition)
            $link = (New-Object -ComObject WScript.Shell).CreateShortcut($Path)
            $link.TargetPath = [string] $Definition.TargetPath
            $link.Arguments = [string] $Definition.Arguments
            $link.WorkingDirectory = [string] $Definition.WorkingDirectory
            $link.IconLocation = [string] $Definition.IconLocation
            $link.Save()
        }
        # 删除安装器自己的快捷方式（#283：首次安装回滚时删掉本次创建的那个；调用方删之前已重读确认它属于安装器）。
        RemoveShortcut = {
            param([string] $Path)
            [System.IO.File]::Delete($Path)
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

# 另供切换脚本（#287）：整个切换与回退期间持有的安装锁，以及它与安装器共用的安装布局、交互会话、按身份结束进程、
# 目录改名与经登录任务启动并等到启动就绪。
Export-ModuleMember -Function Install-CountdownProgram, New-CountdownInstallerWindowsPorts, Enter-CountdownInstallLock, Exit-CountdownInstallLock,
    Resolve-CountdownInstallerLayout, Get-CountdownSingleInteractiveSession, Get-CountdownInstalledProcesses, Stop-CountdownInstalledProcess,
    Move-CountdownInstallDirectory, Start-CountdownPreviousVersion
