# Countdown.Core.Tests.ps1 的 DeployEntry 分段：入口按开关点源执行，共用入口的参数、断言函数与已建立的状态，不单独运行。

# 新部署入口的成功路径（#285）与拒绝、异常路径（#286）：主机的远端执行入口与传输入口换成假 port，办公室是一个临时文件夹，
# 不连接任何设备。
# 夹具是一个临时 Git 仓库：main 已推送到本地裸仓库，app\ 与 logs\ 被忽略；app\ 里是一份假的构建输出与载荷清单，
# logs\suite-runs\ 里有同一提交全部必过阶段通过、干净构建回执带聚合哈希的套件记录。假远端执行在本进程里运行部署端送来的
# 脚本，把假办公室的 Windows port（与安装器测试同形：交互会话带 SID，任务按 SID 记用户，任务启动时起一个假进程并写出
# 运行状态回执；桌面是假办公室里的目录，快捷方式经生产 port 读写）绑定到脚本的测试参数 Port 与 LockName；假传输把包
# 复制到暂存目录、核对哈希并写一份账本。断言结果 JSON、标准错误上的进度、假办公室看到的调用顺序与安装根、本地部署记录，
# 以及输出、进度和记录里没有主机名、账号与 SID。
$deployScript = Join-Path $PSScriptRoot 'Deploy-CountdownProgram.ps1'
$deployTestRoot = Join-Path ([System.IO.Path]::GetTempPath()) ('clock-countdown-deploy-test-' + [guid]::NewGuid().ToString('N'))
$deployUtf8 = [System.Text.UTF8Encoding]::new($false)
# 每次运行用自己的安装锁名，不和本机真实安装或别处并行的测试争同一把全机锁。
$deployLockName = 'Local\MinminCountdownDeployTest-' + [guid]::NewGuid().ToString('N')
# 期望值是独立算好的字面量：各文件为 SHA-256(正文)，聚合哈希按构建脚本的算法（清单顺序的 路径\0哈希\n）另行算出。
$deployBuildFiles = @(
    [pscustomobject]@{ Path = 'app\holiday-data\2027.json'; Text = 'deploy holiday 2027'; Sha256 = '320D602817C92672CFB8B8C91F64F668BD8DCA7F705703350E4C74B3B8B7B197' }
    [pscustomobject]@{ Path = 'app\MinminCountdown.dll'; Text = 'deploy dll v1'; Sha256 = '353CAEC3F60575F11DD4730EA5CAE7E83B130670EF2AE34CA12173AE5567ABA8' }
    [pscustomobject]@{ Path = 'app\MinminCountdown.exe'; Text = 'deploy exe v1'; Sha256 = 'A778CC6FB674FC274CFDFE7A14236F5992C235A2296DC5B7444A28B4833804B6' }
)
$deployAggregate = '93D1974BBA1182448C8B8FB941E4743959C6040BD438045B966FD5010EF7763B'
# 主机名与账号只出现在假 port 的返回值与假办公室的交互会话里，每次运行随机生成；出现在输出、进度或记录里就算泄漏。
$deployHostSentinel = 'deploy-host-' + [guid]::NewGuid().ToString('N')
$deployAccountSentinel = 'deploy-account-' + [guid]::NewGuid().ToString('N')
# 交互会话登录用户的 SID（假值）：安装器按 SID 认登录任务的主体与登录触发器（#281）；它和账号一样不得出现在输出与记录里。
$deploySessionSid = 'S-1-5-21-2850000000-2850000001-2850000002-1001'
$deployRepo = $null
$deployCase = $null
Import-Module (Join-Path $PSScriptRoot 'tools\Countdown.SuiteStages.psm1') -Force
# 桌面快捷方式用安装器的生产 port（WScript.Shell）读写，只落在每个用例假办公室里的桌面目录；本分段自己导入安装器模块取 port。
Import-Module (Join-Path $PSScriptRoot 'Countdown.Installer.psm1') -Force
$deployShortcutPorts = New-CountdownInstallerWindowsPorts

function Invoke-CountdownDeployTestGit {
    param(
        [Parameter(Mandatory)] [string] $Root,
        [Parameter(Mandatory)] [string[]] $Arguments
    )

    $output = @(& git -C $Root @Arguments 2>&1 | ForEach-Object { [string] $_ })
    if ($LASTEXITCODE -ne 0) { throw "夹具 git $($Arguments -join ' ') 失败：$($output -join ' ')" }
    $output
}

# 夹具仓库：部署端打包与送去远端运行的三份脚本取自本仓库；构建输出与套件记录在被忽略的 app\ 与 logs\ 里。
function New-CountdownDeployTestRepository {
    $root = Join-Path $deployTestRoot 'repo'
    $origin = Join-Path $deployTestRoot 'origin.git'
    New-Item -ItemType Directory -Path $root, $origin -Force | Out-Null
    [void] (Invoke-CountdownDeployTestGit -Root $origin -Arguments @('init', '--quiet', '--bare'))
    [void] (Invoke-CountdownDeployTestGit -Root $root -Arguments @('init', '--quiet', '--initial-branch=main'))
    foreach ($setting in @(@('user.name', 'Deploy fixture'), @('user.email', 'fixture@example.invalid'), @('core.autocrlf', 'false'))) {
        [void] (Invoke-CountdownDeployTestGit -Root $root -Arguments (@('config') + $setting))
    }
    foreach ($name in @('Install-CountdownProgram.ps1', 'Countdown.Installer.psm1', 'Get-CountdownInstallStatus.ps1', 'Switch-CountdownInstallLayout.ps1')) {
        [System.IO.File]::Copy((Join-Path $PSScriptRoot $name), (Join-Path $root $name))
    }
    [System.IO.File]::WriteAllText((Join-Path $root '.gitignore'), "app/`nlogs/`n", $deployUtf8)
    [void] (Invoke-CountdownDeployTestGit -Root $root -Arguments @('add', '--all'))
    [void] (Invoke-CountdownDeployTestGit -Root $root -Arguments @('commit', '--quiet', '-m', 'deploy fixture'))
    [void] (Invoke-CountdownDeployTestGit -Root $root -Arguments @('remote', 'add', 'origin', $origin))
    [void] (Invoke-CountdownDeployTestGit -Root $root -Arguments @('push', '--quiet', 'origin', 'main'))
    $head = (@(Invoke-CountdownDeployTestGit -Root $root -Arguments @('rev-parse', '--verify', 'HEAD')) -join '').Trim()

    foreach ($file in $deployBuildFiles) {
        $path = Join-Path $root $file.Path
        New-Item -ItemType Directory -Path (Split-Path -Parent $path) -Force | Out-Null
        [System.IO.File]::WriteAllText($path, $file.Text, $deployUtf8)
    }
    $manifest = [ordered]@{
        schemaVersion = 1
        commit = $head
        aggregateSha256 = $deployAggregate
        files = @($deployBuildFiles | ForEach-Object { [ordered]@{ path = $_.Path; sha256 = $_.Sha256 } })
    }
    [System.IO.File]::WriteAllText((Join-Path $root 'app\published-payload.json'), ($manifest | ConvertTo-Json -Depth 4), $deployUtf8)

    $repository = [pscustomobject]@{ Root = $root; Head = $head; SuiteRunId = '20261114T090000000-0a1b2c3d'; SuiteRunPath = $null }
    $repository.SuiteRunPath = Write-CountdownDeployTestSuiteRun -Repository $repository -RunId $repository.SuiteRunId
    $repository
}

# 一份套件记录，与低影响启动器写的 run.json 同形。默认合格：同一提交、本 checkout、开跑与收尾都干净、必过阶段全部通过、
# Build 阶段的回执是带聚合哈希的干净构建（CleanR2R）。参数各改一处：记录的提交、跑了哪些阶段、某阶段的退出码。
# IncrementalBuild 阶段按启动器的做法写增量构建回执（Mode Incremental、Clean false）。
function Write-CountdownDeployTestSuiteRun {
    param(
        [Parameter(Mandatory)] [object] $Repository,
        [Parameter(Mandatory)] [string] $RunId,
        [string] $Commit = $Repository.Head,
        [string[]] $Stages = @(Get-CountdownRequiredSuiteStage),
        [hashtable] $StageExitCodes = @{},
        [string] $ReceiptAggregate = $deployAggregate
    )

    $logDirectory = Join-Path $Repository.Root 'logs\suite-runs'
    New-Item -ItemType Directory -Path $logDirectory -Force | Out-Null
    $stageRecords = @(foreach ($stage in $Stages) {
            $exitCode = if ($StageExitCodes.ContainsKey($stage)) { [int] $StageExitCodes[$stage] } else { 0 }
            [ordered]@{
                Stage = $stage
                ExitCode = $exitCode
                Status = if ($exitCode -eq 0) { 'Completed' } else { 'Failed' }
                ElapsedSeconds = 60
                BuildReceipt = switch ($stage) {
                    'Build' { [ordered]@{ SchemaVersion = 1; Mode = 'CleanR2R'; Clean = $true; ReadyToRun = $true; ApplicationDllSha256 = $deployBuildFiles[1].Sha256; PayloadAggregateSha256 = $ReceiptAggregate } }
                    'IncrementalBuild' { [ordered]@{ SchemaVersion = 1; Mode = 'Incremental'; Clean = $false; ReadyToRun = $true; ApplicationDllSha256 = $deployBuildFiles[1].Sha256; PayloadAggregateSha256 = $ReceiptAggregate } }
                    default { $null }
                }
            }
        })
    $passed = @($stageRecords | Where-Object { $_.ExitCode -ne 0 }).Count -eq 0
    $run = [ordered]@{
        SchemaVersion = 1
        RunId = $RunId
        RepositoryRoot = $Repository.Root
        LogDirectory = $logDirectory
        Status = if ($passed) { 'Completed' } else { 'Failed' }
        ExitCode = if ($passed) { 0 } else { 1 }
        StartedAtUtc = '2026-11-14T09:00:00.0000000Z'
        FinishedAtUtc = '2026-11-14T09:25:00.0000000Z'
        Stages = $stageRecords
        Source = [ordered]@{ Commit = $Commit; CleanAtStart = $true; CommitAtEnd = $Commit; CleanAtEnd = $true }
    }
    $path = Join-Path $logDirectory "$RunId-run.json"
    [System.IO.File]::WriteAllText($path, ($run | ConvertTo-Json -Depth 6), $deployUtf8)
    $path
}

# 换上一组套件记录跑一次部署：合格记录先挪出 logs\，跑完把这组记录也挪出去，合格记录放回原处。
function Invoke-CountdownDeployTestWithSuiteRuns {
    param(
        [Parameter(Mandatory)] [string] $Label,
        [Parameter(Mandatory)] [AllowEmptyCollection()] [hashtable[]] $Runs
    )

    $setAside = Join-Path $deployTestRoot ('suite-runs-' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $setAside -Force | Out-Null
    $keptPath = Join-Path $setAside 'kept-run.json'
    [System.IO.File]::Move($deployRepo.SuiteRunPath, $keptPath)
    $written = [System.Collections.Generic.List[string]]::new()
    try {
        foreach ($run in $Runs) { $written.Add((Write-CountdownDeployTestSuiteRun -Repository $deployRepo @run)) }
        Invoke-CountdownDeployTest -Label $Label
    }
    finally {
        foreach ($path in $written) { [System.IO.File]::Move($path, (Join-Path $setAside ([System.IO.Path]::GetFileName($path)))) }
        [System.IO.File]::Move($keptPath, $deployRepo.SuiteRunPath)
    }
}

# 一台假办公室（暂存目录与安装根）和这次部署的本地记录根。
function New-CountdownDeployTestCase {
    param([Parameter(Mandatory)] [string] $Name)

    $officeRoot = Join-Path $deployTestRoot "office-$Name"
    # 交互用户的桌面也在假办公室里：只读调用前后的办公室现状连桌面一起比较。
    $desktop = Join-Path $officeRoot 'desktop'
    New-Item -ItemType Directory -Path $officeRoot, $desktop -Force | Out-Null
    [pscustomobject]@{
        Name = $Name
        OfficeRoot = $officeRoot
        StagingRoot = Join-Path $officeRoot 'deploy'
        InstallRoot = Join-Path $officeRoot 'install'
        Desktop = $desktop
        TaskName = "Minmin Countdown Deploy $Name"
        RecordRoot = Join-Path $deployTestRoot "records-$Name"
        LedgerRoot = Join-Path $deployTestRoot "ledgers-$Name"
        Sessions = @([pscustomobject]@{ SessionId = 3; User = "$deployHostSentinel\$deployAccountSentinel"; Sid = $deploySessionSid })
        Processes = @()
        Tasks = @{}
        NextProcessId = 7001
        Calls = [System.Collections.Generic.List[string]]::new()
        ReadOnlyChanges = [System.Collections.Generic.List[string]]::new()
        MaxEncodedLength = 0
        # 假远端的应答钩子，按“读/写/传输 + 第几次”登记（read1 是第一次只读调用，write1 是第一次写操作，transfer1 是第一次传输）。
        Responses = @{}
    }
}

# 假办公室的 Windows port 只读写当前 $deployCase：安装器与状态命令回调时按动态作用域解析到这里的变量。
$deployWindowsPorts = [pscustomobject]@{
    GetInteractiveSessions = { $deployCase.Sessions }
    GetProcesses = {
        param([string] $Name)
        @($deployCase.Processes | Where-Object { $_.Name -eq $Name })
    }
    StopProcess = {
        param([int] $ProcessId)
        # 任务已被移除（回退先让启动路径失效）时进程照样能结束。
        foreach ($process in @($deployCase.Processes | Where-Object { $_.ProcessId -eq $ProcessId -and $_.StartedByTask -and $deployCase.Tasks.ContainsKey($_.StartedByTask) })) {
            $deployCase.Tasks[$process.StartedByTask].State = 'Ready'
        }
        $deployCase.Processes = @($deployCase.Processes | Where-Object { $_.ProcessId -ne $ProcessId })
    }
    GetTask = {
        param([string] $Name)
        if ($deployCase.Tasks.ContainsKey($Name)) { $deployCase.Tasks[$Name] }
    }
    # 注册即整份替换；主体与登录触发器都记交互会话登录用户的 SID（#281），UserId 记注册时的显示名。
    RegisterTask = {
        param([string] $Name, [object] $Definition, [object] $Session)
        $deployCase.Tasks[$Name] = [pscustomobject]@{
            State = 'Ready'
            Enabled = [bool] $Definition.Enabled
            Actions = @($Definition.Actions | ForEach-Object { [pscustomobject]@{ Type = $_.Type; Execute = $_.Execute; Arguments = $_.Arguments; WorkingDirectory = $_.WorkingDirectory } })
            Triggers = @($Definition.Triggers | ForEach-Object { [pscustomobject]@{ Type = $_.Type; Enabled = $_.Enabled; UserSid = $Session.Sid } })
            UserSid = $Session.Sid
            LogonType = $Definition.LogonType
            RunLevel = $Definition.RunLevel
            Priority = $Definition.Priority
            ExecutionTimeLimit = $Definition.ExecutionTimeLimit
            MultipleInstances = $Definition.MultipleInstances
            DisallowStartIfOnBatteries = $Definition.DisallowStartIfOnBatteries
            StopIfGoingOnBatteries = $Definition.StopIfGoingOnBatteries
            AllowDemandStart = $Definition.AllowDemandStart
            AllowHardTerminate = $Definition.AllowHardTerminate
            UserId = $Session.User
        }
    }
    # 任务按动作启动 EXE：起一个假进程，像应用渲染首帧后那样在应用根（app\ 的上一级）写出运行状态回执。
    # 与任务计划一样，停用的任务和不允许按需启动的任务不能按需启动。现有布局的任务（#287）经 pwsh 运行 adapter：adapter 启动它
    # 所在安装根里的 app\MinminCountdown.exe 后退出，任务实例随之结束；adapter 不在时什么都起不来。
    StartTask = {
        param([string] $Name)
        $task = $deployCase.Tasks[$Name]
        if (-not $task.Enabled -or -not $task.AllowDemandStart) { throw "任务 $Name 不能按需启动。" }
        $exePath = ([string] $task.Actions[0].Execute).Trim('"')
        $startedByTask = $Name
        if ([System.IO.Path]::GetFileName($exePath) -eq 'pwsh.exe') {
            if ([string] $task.Actions[0].Arguments -notmatch '-File "([^"]+)"' -or -not (Test-Path -LiteralPath $Matches[1] -PathType Leaf)) { return }
            $exePath = Join-Path (Split-Path -Parent $Matches[1]) 'app\MinminCountdown.exe'
            $startedByTask = $null
        }
        else { $task.State = 'Running' }
        $processId = $deployCase.NextProcessId
        $deployCase.NextProcessId = $processId + 1
        $deployCase.Processes = @($deployCase.Processes) + @([pscustomobject]@{
                ProcessId = $processId
                Name = 'MinminCountdown.exe'
                ExecutablePath = $exePath
                SessionId = 3
                CreationDate = [DateTimeOffset]::Parse('2026-11-14T09:40:00Z').LocalDateTime
                StartedByTask = $startedByTask
            })
        $receipt = [ordered]@{ schemaVersion = 4; stage = 'window-ready'; processId = $processId; sessionId = 3; readyAtUtc = '2026-11-14T09:40:02.0000000+00:00' }
        [System.IO.File]::WriteAllText((Join-Path (Split-Path -Parent (Split-Path -Parent $exePath)) 'runtime-status.json'), ($receipt | ConvertTo-Json), $deployUtf8)
    }
    MoveDirectory = {
        param([string] $Source, [string] $Destination)
        [System.IO.Directory]::Move($Source, $Destination)
    }
    # 交互用户的桌面目录是假办公室里的目录；快捷方式经生产 port 读写，安装器按 Shell Link 文件头认出自己的快捷方式（#281）。
    GetDesktopDirectory = {
        param([object] $Session)
        $deployCase.Desktop
    }
    ReadShortcut = $deployShortcutPorts.ReadShortcut
    WriteShortcut = $deployShortcutPorts.WriteShortcut
    RemoveShortcut = $deployShortcutPorts.RemoveShortcut
    # 任务定义的导出与恢复（切换的备份与回退、安装器更新任务前的导出）：导出是不含运行状态的整份定义，恢复按导出整份替换，
    # 定义为空即删除。
    ExportTask = {
        param([string] $Name)
        if ($deployCase.Tasks.ContainsKey($Name)) { $deployCase.Tasks[$Name] | Select-Object -Property * -ExcludeProperty State | ConvertTo-Json -Depth 4 -Compress }
    }
    RestoreTask = {
        param([string] $Name, [object] $Definition)
        if ($null -eq $Definition) {
            [void] $deployCase.Tasks.Remove($Name)
            return
        }
        $restored = [string] $Definition | ConvertFrom-Json
        $restored | Add-Member -NotePropertyName State -NotePropertyValue 'Ready'
        $deployCase.Tasks[$Name] = $restored
    }
    # 回退恢复任务前读导出定义里的主体、运行级别与动作；切换的备份目录经受保护目录 port 建出（这里只建目录）。
    ReadTaskDefinition = {
        param([string] $Definition)
        $task = $Definition | ConvertFrom-Json
        [pscustomobject]@{ UserSid = $task.UserSid; RunLevel = $task.RunLevel; Actions = @($task.Actions) }
    }
    CreateProtectedDirectory = {
        param([string] $Path, [string] $ReaderSid)
        if (Test-Path -LiteralPath $Path) { throw "目录已存在：$Path" }
        [void] [System.IO.Directory]::CreateDirectory($Path)
    }
    # 切换前置检查读的令牌与任务 DACL（#288）：远端步骤提权运行，任务都是提权注册的（只有 SYSTEM 与 Administrators 能改）。
    GetProcessIdentity = { [pscustomobject]@{ Elevated = $true; Sids = @('S-1-5-32-544', 'S-1-5-32-545', 'S-1-5-11') } }
    GetTaskSecurity = { param([string] $Name) 'D:AI(A;ID;0x1f019f;;;BA)(A;ID;0x1f019f;;;SY)(A;ID;FA;;;BA)' }
}

# 假办公室的现状：文件树（长度、修改时间与 SHA-256）、计划任务与进程。只读调用前后必须相同。
function Get-CountdownDeployTestOfficeState {
    $tree = @(Get-ChildItem -LiteralPath $deployCase.OfficeRoot -Recurse -Force | Sort-Object FullName | ForEach-Object {
            $relative = [System.IO.Path]::GetRelativePath($deployCase.OfficeRoot, $_.FullName)
            if ($_.PSIsContainer) { "dir:$relative" }
            else { "file:$relative|$($_.Length)|$($_.LastWriteTimeUtc.Ticks)|$((Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash)" }
        }) -join "`n"
    $tasks = @($deployCase.Tasks.Keys | Sort-Object | ForEach-Object { "$_=$($deployCase.Tasks[$_].State)" }) -join ','
    $processes = @($deployCase.Processes | ForEach-Object { [int] $_.ProcessId } | Sort-Object) -join ','
    "$tree`ntasks:$tasks`nprocesses:$processes"
}

# 假主机入口。远端执行与主机入口同一约定：写操作脚本的第一条语句（param 块之后）不是执行目标断言就拒绝、不执行；
# 远端前缀注入执行目标标记；办公室上没有脚本文件，脚本包进脚本块运行，远端的其余输出流不进部署端；返回值另带主机名与账号，
# 部署端只能取 ExitCode 与 Output。传输：建出远端目录，送到办公室上按运行编号命名的暂存目录，核对远端 SHA-256，
# 在主机的账本目录记账；摘要另带远端主机名，与主机的传输入口一样。
# 用例登记了应答钩子时，钩子拿到“在假办公室上照常运行”的脚本块，自己决定运不运行、回什么：不运行而直接回一份应答，
# 是罐装的远端结果（办公室上什么都没发生）；运行之后丢掉应答、回连接断开，是连接在途中断开。
$deployHostPorts = [pscustomobject]@{
    InvokeRemoteScript = {
        param([string] $ScriptPath, [bool] $WriteOperation)
        $deployCase.Calls.Add($(if ($WriteOperation) { 'remote-write' } else { 'remote-read' }))
        $runOnOffice = {
            $source = [System.IO.File]::ReadAllText($ScriptPath)
            # 远端执行入口把整条命令限制在 30000 字符内（UTF-16 的 Base64 加上约 1000 字符的前缀）。
            $encodedLength = [Convert]::ToBase64String([System.Text.Encoding]::Unicode.GetBytes('& {' + $source.TrimEnd() + "`n}")).Length
            $deployCase.MaxEncodedLength = [Math]::Max($deployCase.MaxEncodedLength, $encodedLength)
            $tokens = $null
            $errors = $null
            $ast = [System.Management.Automation.Language.Parser]::ParseInput($source, [ref] $tokens, [ref] $errors)
            $first = @($ast.EndBlock.Statements | Select-Object -First 1)
            if ($errors.Count -gt 0 -or ($WriteOperation -and ($first.Count -ne 1 -or $first[0] -isnot [System.Management.Automation.Language.IfStatementAst] -or
                            -not $first[0].Extent.Text.StartsWith("if (`$env:CODEX_REMOTE_EXECUTION_TARGET -ne 'office')")))) {
                return [pscustomobject]@{ ExitCode = 2; Output = ''; Host = $deployHostSentinel; User = $deployAccountSentinel }
            }
            $before = Get-CountdownDeployTestOfficeState
            $exitCode = 0
            $previousTarget = $env:CODEX_REMOTE_EXECUTION_TARGET
            $env:CODEX_REMOTE_EXECUTION_TARGET = 'office'
            try { $output = @(& ([scriptblock]::Create($source)) -Port $deployWindowsPorts -LockName $deployLockName 2>$null 3>$null 4>$null 5>$null 6>$null) }
            catch { $output = @(); $exitCode = 1 }
            finally { $env:CODEX_REMOTE_EXECUTION_TARGET = $previousTarget }
            if (-not $WriteOperation -and $before -ne (Get-CountdownDeployTestOfficeState)) { $deployCase.ReadOnlyChanges.Add($ScriptPath) }
            [pscustomobject]@{ ExitCode = $exitCode; Output = (@($output | ForEach-Object { [string] $_ }) -join "`n"); Host = $deployHostSentinel; User = $deployAccountSentinel }
        }
        $kind = if ($WriteOperation) { 'write' } else { 'read' }
        $hook = $deployCase.Responses["$kind$(@($deployCase.Calls | Where-Object { $_ -eq "remote-$kind" }).Count)"]
        if ($null -ne $hook) { return & $hook $runOnOffice }
        & $runOnOffice
    }
    SendFile = {
        param([string] $SourcePath, [string] $RemotePath, [string] $ExpectedSha256, [string] $AttemptId)
        $deployCase.Calls.Add('transfer')
        $hook = $deployCase.Responses["transfer$(@($deployCase.Calls | Where-Object { $_ -eq 'transfer' }).Count)"]
        if ($null -ne $hook) { return & $hook }
        New-Item -ItemType Directory -Path (Split-Path -Parent $RemotePath) -Force | Out-Null
        [System.IO.File]::Copy($SourcePath, $RemotePath)
        $remoteSha256 = (Get-FileHash -LiteralPath $RemotePath -Algorithm SHA256).Hash
        if ($remoteSha256 -ne $ExpectedSha256) { throw '假传输：远端哈希与期望不一致。' }
        $bytes = (Get-Item -LiteralPath $RemotePath).Length
        $ledgerPath = Join-Path $deployCase.LedgerRoot "$AttemptId\transfer-attempts.json"
        New-Item -ItemType Directory -Path (Split-Path -Parent $ledgerPath) -Force | Out-Null
        $ledger = [ordered]@{ transferAttemptsVersion = '1.0'; attempts = @([ordered]@{ id = $AttemptId; method = 'fake'; status = 'succeeded'; bytes = $bytes }) }
        [System.IO.File]::WriteAllText($ledgerPath, ($ledger | ConvertTo-Json -Depth 4), $deployUtf8)
        [pscustomobject]@{ Status = 'succeeded'; AttemptId = $AttemptId; Sha256 = $remoteSha256.ToLowerInvariant(); Bytes = $bytes; LedgerPath = $ledgerPath; Hostname = $deployHostSentinel }
    }
}

# 在本进程里运行部署入口：标准错误换成内存写入器收集进度，其余所有输出流并进标准输出，必须只有一份 JSON。
function Invoke-CountdownDeployTest {
    param(
        [Parameter(Mandatory)] [string] $Label,
        [switch] $StatusOnly,
        [switch] $LayoutSwitch,
        [switch] $LayoutRollback
    )

    $errorWriter = [System.IO.StringWriter]::new()
    $standardError = [Console]::Error
    [Console]::SetError($errorWriter)
    try {
        $output = @(& $deployScript -StatusOnly:$StatusOnly -LayoutSwitch:$LayoutSwitch -LayoutRollback:$LayoutRollback -RepositoryRoot $deployRepo.Root -RecordRoot $deployCase.RecordRoot `
                -RemoteInstallRoot $deployCase.InstallRoot -TaskName $deployCase.TaskName -RemoteStagingRoot $deployCase.StagingRoot -Port $deployHostPorts *>&1)
    }
    finally { [Console]::SetError($standardError) }
    $text = @($output | ForEach-Object { [string] $_ })
    Assert-Equal 1 $text.Count "Deploy writes exactly one item to standard output: $Label"
    $json = $text -join "`n"
    $run = [pscustomobject]@{
        Text = $json
        Stderr = $errorWriter.ToString()
        Result = try { $json | ConvertFrom-Json -DateKind String } catch { $null }
    }
    # 标准错误的最后一行是结论：与结果 JSON 同一个结论，有下一步提示时一并写出，人看到的和程序读到的是同一件事。
    if (-not $StatusOnly) {
        $conclusion = [regex]::Match($run.Stderr, '(?m)^\[deploy\] \d\d:\d\d:\d\d 结果 (?<outcome>[a-z-]+)(?<rest>.*)$')
        Assert-Equal ([string] $run.Result.outcome) $conclusion.Groups['outcome'].Value "The conclusion on standard error states the result's outcome: $Label"
        if ($run.Result.nextStep) {
            Assert-Equal $true $conclusion.Groups['rest'].Value.Contains([string] $run.Result.nextStep) "The conclusion on standard error repeats the next step: $Label"
        }
    }
    $run
}

# 远端安装脚本的罐装应答：包内安装器报告启动就绪超时，带 #283 的回滚结果 rollback.outcome（不给就是还没有这个字段的安装器）。
# 给了 StartupExitCode 时改为新版本在启动就绪之前退出（#288）：原因 startup-exited，结果带退出码。
# 假远端直接回它，不在假办公室上运行安装器。
function New-CountdownDeployTestInstallFailure {
    param(
        [AllowNull()] [string] $RollbackOutcome,
        [Nullable[long]] $StartupExitCode
    )

    $installer = [ordered]@{
        schemaVersion = 1
        succeeded = $false
        checkOnly = $false
        failedStage = 'start-and-wait-ready'
        reason = 'startup-not-ready'
        error = '30 秒内没有达到启动就绪：运行状态回执尚未出现。'
    }
    if ($null -ne $StartupExitCode) {
        $installer.reason = 'startup-exited'
        $installer.error = "经登录任务启动的进程在启动就绪之前退出，退出码 $StartupExitCode（0x$('{0:X8}' -f $StartupExitCode)），原因见应用的诊断日志。"
        $installer.startupExitCode = $StartupExitCode
    }
    if ($RollbackOutcome) { $installer.rollback = [ordered]@{ outcome = $RollbackOutcome } }
    $envelope = [ordered]@{
        packageVerified = $true
        installerStarted = $true
        installer = $installer | ConvertTo-Json -Depth 4 -EscapeHandling EscapeNonAscii
        stagingRemoved = $true
        error = $null
    }
    [pscustomobject]@{ ExitCode = 0; Output = ($envelope | ConvertTo-Json -Compress -EscapeHandling EscapeNonAscii); Host = $deployHostSentinel; User = $deployAccountSentinel }
}

function Get-CountdownDeployTestTree {
    param([Parameter(Mandatory)] [string] $Root)

    if (-not (Test-Path -LiteralPath $Root)) { return '<missing>' }
    @(Get-ChildItem -LiteralPath $Root -Recurse -Force | Sort-Object FullName | ForEach-Object {
            $relative = [System.IO.Path]::GetRelativePath($Root, $_.FullName)
            if ($_.PSIsContainer) { "dir:$relative" }
            else { "file:$relative|$($_.Length)|$($_.LastWriteTimeUtc.Ticks)|$((Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash)" }
        }) -join "`n"
}

function Get-CountdownDeployTestFileHash {
    param([Parameter(Mandatory)] [string] $Path)
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { return 'missing' }
    (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash
}

function Assert-CountdownDeployNoLeak {
    param(
        [Parameter(Mandatory)] [AllowEmptyString()] [string] $Text,
        [Parameter(Mandatory)] [string] $Name
    )
    $leaks = @(@($deployHostSentinel, $deployAccountSentinel, $deploySessionSid) | Where-Object { $Text.Contains($_) })
    Assert-Equal 0 $leaks.Count "$Name carries no host name, account or SID"
}

# 连接远端之前的拒绝（#286）：结果是 rejected、不是成功，原因类别与期望一致，错误写明缺的每一项；办公室没有收到任何调用。
# 本机预检拒绝时不写本地记录（打包时的拒绝在预检通过之后，照常写记录）。
function Assert-CountdownDeployRejected {
    param(
        [Parameter(Mandatory)] [object] $Deploy,
        [Parameter(Mandatory)] [string] $Reason,
        [Parameter(Mandatory)] [string[]] $Names,
        [Parameter(Mandatory)] [string] $Label,
        [string] $FailedStep = 'preflight'
    )

    $result = $Deploy.Result
    Assert-Equal "rejected|False|$FailedStep|$Reason" "$($result.outcome)|$($result.succeeded)|$($result.failedStep)|$($result.reason)" "$Label is rejected with its reason: $($result.error)"
    foreach ($name in $Names) {
        Assert-Equal $true ([string] $result.error).Contains($name) "$Label names what is missing: $name"
    }
    Assert-Equal '' ($deployCase.Calls -join ',') "$Label is rejected before any remote call or transfer"
    if ($FailedStep -eq 'preflight') {
        Assert-Equal $false (Test-Path -LiteralPath $deployCase.RecordRoot) "$Label writes no local record"
    }
    Assert-CountdownDeployNoLeak -Text $Deploy.Text -Name "$Label standard output"
}

try {
    New-Item -ItemType Directory -Path $deployTestRoot -Force | Out-Null
    $deployRepo = New-CountdownDeployTestRepository
    $deployAppTree = Get-CountdownDeployTestTree -Root (Join-Path $deployRepo.Root 'app')

    # 曳光弹：把已推送的 main 部署到一台还没装过的办公室。
    $deployCase = New-CountdownDeployTestCase -Name 'first'
    $deployed = Invoke-CountdownDeployTest -Label 'first deploy'
    $deployResult = $deployed.Result
    Assert-Equal 'deploy' ([string] $deployResult.mode) 'Deploy reports the deploy mode'
    Assert-Equal $true ([bool] $deployResult.succeeded) "Deploy succeeds: $($deployResult.failedStep) $($deployResult.error)"
    Assert-Equal 'deployed' ([string] $deployResult.outcome) 'A successful deploy reports the deployed outcome'
    Assert-Equal $true ($null -eq $deployResult.failedStep -and $null -eq $deployResult.reason) 'A successful deploy names no failed step and no reason'
    Assert-Equal $deployRepo.Head ([string] $deployResult.commit) 'Deploy reports the pushed main commit it deployed'
    Assert-Equal $deployRepo.SuiteRunId ([string] $deployResult.suiteRun.runId) 'Deploy names the suite run that tested the shipped build output'
    Assert-Equal $deployAggregate ([string] $deployResult.buildOutput.payloadAggregateSha256) 'Deploy reports the aggregate hash of the build output it shipped'

    # 各步骤按顺序发生：办公室依次看到只读探测、传输、写操作安装、只读状态读取；进度在标准错误上逐步报告。
    Assert-Equal 'remote-read,transfer,remote-write,remote-read' ($deployCase.Calls -join ',') 'The office sees the read-only probe, the transfer, the install as a write and then a read-only status read'
    Assert-Equal 0 $deployCase.ReadOnlyChanges.Count 'Read-only remote calls change nothing on the office'
    Assert-Equal 'missing|False' "$($deployResult.statusBefore.manifest)|$($deployResult.statusBefore.running)" 'The probe reads the office status before installing: nothing installed yet'
    Assert-Equal $true ($deployCase.MaxEncodedLength -gt 0 -and $deployCase.MaxEncodedLength -le 29000) "Every remote script fits the remote entry's encoded command limit: $($deployCase.MaxEncodedLength)"
    $deployProgressSteps = @([regex]::Matches($deployed.Stderr, '(?m)^\[deploy\] \d\d:\d\d:\d\d \d+/\d+ (?<step>[a-z]+) ') | ForEach-Object { $_.Groups['step'].Value })
    Assert-Equal 'preflight,package,probe,transfer,install,status,record' ($deployProgressSteps -join ',') 'Deploy reports each step on standard error in order'

    # 打包内容来自套件测过的构建输出：办公室程序目录里的文件与载荷清单和本机构建输出逐字节相同；
    # 本机构建输出与工作树都没被动过，没有重新导出或重新构建。
    foreach ($deployFile in $deployBuildFiles) {
        Assert-Equal $deployFile.Sha256 (Get-CountdownDeployTestFileHash -Path (Join-Path $deployCase.InstallRoot $deployFile.Path)) "The office runs the suite-tested build output file: $($deployFile.Path)"
    }
    Assert-Equal (Get-CountdownDeployTestFileHash -Path (Join-Path $deployRepo.Root 'app\published-payload.json')) (Get-CountdownDeployTestFileHash -Path (Join-Path $deployCase.InstallRoot 'app\published-payload.json')) 'The office keeps the payload manifest the suite-tested build wrote'
    Assert-Equal $deployAppTree (Get-CountdownDeployTestTree -Root (Join-Path $deployRepo.Root 'app')) 'Deploy leaves the local build output untouched: nothing re-exported or rebuilt'
    Assert-Equal '' (@(Invoke-CountdownDeployTestGit -Root $deployRepo.Root -Arguments @('status', '--porcelain=v1', '--untracked-files=all')) -join ',') 'Deploy leaves the work tree clean'

    # 成功判定来自安装后的状态读取：提交、聚合哈希、EXE 哈希与运行中的进程。
    Assert-Equal $true ([bool] $deployResult.install.succeeded) 'The package installer reports success'
    Assert-Equal 7001 ([int] $deployResult.install.readyProcess.processId) 'The install result names the process that reached startup readiness'
    Assert-Equal 'created|created' "$($deployResult.install.loginTask.action)|$($deployResult.install.desktopShortcut.action)" 'The package installer creates the login task and the desktop shortcut on the office'
    Assert-Equal $deployRepo.Head ([string] $deployResult.status.commit) 'The status read after the install reports the deployed commit'
    Assert-Equal $deployAggregate ([string] $deployResult.status.aggregateSha256) 'The status read after the install reports the shipped aggregate hash'
    Assert-Equal $deployBuildFiles[2].Sha256 ([string] $deployResult.status.exe.sha256) 'The status read after the install reports the shipped EXE hash'
    Assert-Equal $true ([bool] $deployResult.status.exe.matchesManifest) 'The status read after the install reports the EXE matching the manifest'
    Assert-Equal $true ([bool] $deployResult.status.running) 'The status read after the install reports the application running'
    Assert-Equal '7001' (@($deployResult.status.processes | ForEach-Object { [int] $_.processId }) -join ',') 'The status read lists the process that reached startup readiness'
    Assert-Equal $false ([bool] $deployResult.status.transactionMarkerPresent) 'The status read reports no unfinished install'
    Assert-Equal 0 @($deployResult.statusMismatches).Count 'The status read matches every expectation'

    # 包和暂存内容不长期保留。
    Assert-Equal $true ([string] $deployResult.package.sha256 -match '^[0-9A-F]{64}$') 'Deploy reports the package SHA-256'
    Assert-Equal ([string] $deployResult.package.sha256) ([string] $deployResult.transfer.remoteSha256) 'The transfer verified the package hash on the office'
    Assert-Equal $false (Test-Path -LiteralPath ([string] $deployResult.remoteStagingDirectory)) 'The office staging directory is removed after the install'
    Assert-Equal $true ([bool] $deployResult.install.stagingRemoved) 'The remote install reports the staging directory removed'
    Assert-Equal $false ([bool] $deployResult.package.retained) 'Deploy reports the local package not retained'
    Assert-Equal 0 @(Get-ChildItem -LiteralPath $deployCase.RecordRoot -Recurse -Force -File | Where-Object { $_.Extension -eq '.zip' }).Count 'No package is kept under the local record root'

    # 本地部署记录：提交、包哈希、安装结果、状态快照与传输账本引用。
    $deployRecordText = [System.IO.File]::ReadAllText([string] $deployResult.recordPath)
    $deployRecord = $deployRecordText | ConvertFrom-Json -DateKind String
    Assert-Equal $deployRepo.Head ([string] $deployRecord.commit) 'The deployment record names the commit'
    Assert-Equal ([string] $deployResult.package.sha256) ([string] $deployRecord.package.sha256) 'The deployment record names the package hash'
    Assert-Equal $true ([bool] $deployRecord.install.succeeded) 'The deployment record carries the install result'
    Assert-Equal $deployRepo.Head ([string] $deployRecord.status.commit) 'The deployment record carries the status snapshot'
    Assert-Equal $true ([bool] $deployRecord.succeeded) 'The deployment record carries the verdict'
    Assert-Equal $true (Test-Path -LiteralPath ([string] $deployRecord.transfer.ledgerPath) -PathType Leaf) 'The deployment record references the transfer ledger'
    $deployLedger = [System.IO.File]::ReadAllText([string] $deployRecord.transfer.ledgerPath) | ConvertFrom-Json
    Assert-Equal ([string] $deployRecord.transfer.attemptId) ([string] $deployLedger.attempts[0].id) 'The ledger reference names the transfer attempt'

    # 标准输出、进度与记录都不含主机名或账号。
    Assert-CountdownDeployNoLeak -Text $deployed.Text -Name 'Deploy standard output'
    Assert-CountdownDeployNoLeak -Text $deployed.Stderr -Name 'Deploy progress'
    Assert-CountdownDeployNoLeak -Text $deployRecordText -Name 'Deployment record'

    # 只查状态：一次只读的状态读取，办公室与本地记录都不变。
    $deployOfficeBefore = Get-CountdownDeployTestOfficeState
    $deployRecordsBefore = Get-CountdownDeployTestTree -Root $deployCase.RecordRoot
    $deployCase.Calls.Clear()
    $deployStatusOnly = Invoke-CountdownDeployTest -Label 'status only' -StatusOnly
    Assert-Equal 'status' ([string] $deployStatusOnly.Result.mode) 'Status-only mode reports the status mode'
    Assert-Equal $true ([bool] $deployStatusOnly.Result.succeeded) "Status-only mode reads the status: $($deployStatusOnly.Result.error)"
    Assert-Equal 'remote-read' ($deployCase.Calls -join ',') 'Status-only mode makes one read-only remote call and no transfer'
    Assert-Equal $deployOfficeBefore (Get-CountdownDeployTestOfficeState) 'Status-only mode changes nothing on the office'
    Assert-Equal $deployRecordsBefore (Get-CountdownDeployTestTree -Root $deployCase.RecordRoot) 'Status-only mode writes nothing locally'
    Assert-Equal $deployRepo.Head ([string] $deployStatusOnly.Result.status.commit) 'Status-only mode reports the installed commit'
    Assert-Equal $true ([bool] $deployStatusOnly.Result.status.running) 'Status-only mode reports the application running'
    Assert-CountdownDeployNoLeak -Text $deployStatusOnly.Text -Name 'Status-only standard output'
    Assert-CountdownDeployNoLeak -Text $deployStatusOnly.Stderr -Name 'Status-only progress'

    # 当前提交已在运行（#286）：再部署一次，探测时读到的状态显示 HEAD 已在运行，直接报告并退出：不传输、不重复安装。
    $deployOfficeBefore = Get-CountdownDeployTestOfficeState
    $deployCase.Calls.Clear()
    $deployAgain = Invoke-CountdownDeployTest -Label 'commit already running'
    Assert-Equal 'already-running|True' "$($deployAgain.Result.outcome)|$($deployAgain.Result.succeeded)" "Deploying the commit already running reports it as such: $($deployAgain.Result.failedStep) $($deployAgain.Result.error)"
    Assert-Equal $true ($null -eq $deployAgain.Result.failedStep -and $null -eq $deployAgain.Result.transfer -and $null -eq $deployAgain.Result.install) 'Deploying the commit already running neither transfers nor installs'
    Assert-Equal 'remote-read' ($deployCase.Calls -join ',') 'Deploying the commit already running makes one read-only remote call'
    Assert-Equal $deployOfficeBefore (Get-CountdownDeployTestOfficeState) 'Deploying the commit already running changes nothing on the office'
    Assert-Equal "$($deployRepo.Head)|True|7001" "$($deployAgain.Result.statusBefore.commit)|$($deployAgain.Result.statusBefore.running)|$(@($deployAgain.Result.statusBefore.processes | ForEach-Object { [int] $_.processId }) -join ',')" 'The report carries the status read that shows the commit running'
    Assert-Equal 'already-running' ([string] ([System.IO.File]::ReadAllText([string] $deployAgain.Result.recordPath) | ConvertFrom-Json).outcome) 'The deployment record says the commit was already running'

    # 办公室上运行的是同一提交的另一份构建输出（#259 评审）：载荷清单记的聚合哈希不是包的那份，探测不报已在运行，照常传输并安装。
    $deployOfficeManifestPath = Join-Path $deployCase.InstallRoot 'app\published-payload.json'
    $deployOfficeManifest = [System.IO.File]::ReadAllText($deployOfficeManifestPath) | ConvertFrom-Json
    $deployOfficeManifest.aggregateSha256 = 'ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789'
    [System.IO.File]::WriteAllText($deployOfficeManifestPath, ($deployOfficeManifest | ConvertTo-Json -Depth 4), $deployUtf8)
    $deployCase.Calls.Clear()
    $deployOtherBuild = Invoke-CountdownDeployTest -Label 'same commit, another build output running'
    Assert-Equal 'deployed|True' "$($deployOtherBuild.Result.outcome)|$($deployOtherBuild.Result.succeeded)" "Deploying over another build output of the same commit installs the suite-tested one: $($deployOtherBuild.Result.failedStep) $($deployOtherBuild.Result.error)"
    Assert-Equal 'remote-read,transfer,remote-write,remote-read' ($deployCase.Calls -join ',') 'Another build output of the same commit gets the transfer, the install and the status read'
    Assert-Equal (Get-CountdownDeployTestFileHash -Path (Join-Path $deployRepo.Root 'app\published-payload.json')) (Get-CountdownDeployTestFileHash -Path $deployOfficeManifestPath) 'The office gets the payload manifest of the suite-tested build output'

    # 远端脚本的占位符一次替换（#259 评审）：暂存根与任务名里拼着别的占位符字样和单引号，远端脚本照原样用它们。
    $deployCase = New-CountdownDeployTestCase -Name 'placeholders'
    $deployCase.StagingRoot = Join-Path $deployCase.OfficeRoot 'deploy __TASK_NAME__ __SHA256__'
    $deployCase.TaskName = "Minmin Countdown Deploy it's __STAGING__ __ENTRY__"
    $deployPlaceholders = Invoke-CountdownDeployTest -Label 'staging root and task name that spell placeholders'
    Assert-Equal 'deployed|True' "$($deployPlaceholders.Result.outcome)|$($deployPlaceholders.Result.succeeded)" "A staging root and a task name that spell other placeholders deploy as given: $($deployPlaceholders.Result.failedStep) $($deployPlaceholders.Result.error)"
    Assert-Equal $true ([string] $deployPlaceholders.Result.remoteStagingDirectory).StartsWith("$($deployCase.StagingRoot)\") 'The package goes to the staging root as given'
    Assert-Equal "$($deployCase.TaskName)=Running" (@($deployCase.Tasks.Keys | ForEach-Object { "$_=$($deployCase.Tasks[$_].State)" }) -join ',') 'The login task is registered under the task name as given'

    # 成功只看安装后的状态读取：安装器报告成功，但读状态时应用已经退出，部署不报告成功。
    $deployCase = New-CountdownDeployTestCase -Name 'exited'
    $deployCase.Responses['read2'] = { param($Run) $deployCase.Processes = @(); & $Run }
    $deployExited = Invoke-CountdownDeployTest -Label 'application exited before the status read'
    Assert-Equal $true ([bool] $deployExited.Result.install.succeeded) 'The installer reports success before the application exits'
    Assert-Equal $false ([bool] $deployExited.Result.succeeded) 'Deploy does not report success when the status read finds the application not running'
    Assert-Equal 'status' ([string] $deployExited.Result.failedStep) 'Deploy names the status read as the failed step'
    Assert-Equal 'process' (@($deployExited.Result.statusMismatches) -join ',') 'Deploy names the missing process as the status mismatch'
    Assert-Equal 'failed|status-mismatch' "$($deployExited.Result.outcome)|$($deployExited.Result.reason)" 'A status mismatch is a failure'

    # 安装器报告成功，但状态读取时办公室上的提交不是 HEAD，或已安装 EXE 的哈希不对：部署都不报告成功，并写明哪一项不一致。
    $deployStatusMismatches = [ordered]@{
        commit = {
            $manifestPath = Join-Path $deployCase.InstallRoot 'app\published-payload.json'
            $manifest = [System.IO.File]::ReadAllText($manifestPath) | ConvertFrom-Json
            $manifest.commit = '0123456789abcdef0123456789abcdef01234567'
            [System.IO.File]::WriteAllText($manifestPath, ($manifest | ConvertTo-Json -Depth 4), $deployUtf8)
        }
        exe = { [System.IO.File]::WriteAllText((Join-Path $deployCase.InstallRoot 'app\MinminCountdown.exe'), 'deploy exe replaced', $deployUtf8) }
    }
    foreach ($deployMismatch in $deployStatusMismatches.Keys) {
        $deployCase = New-CountdownDeployTestCase -Name "mismatch-$deployMismatch"
        $deployCase.Responses['read2'] = { param($Run) & $deployStatusMismatches[$deployMismatch]; & $Run }
        $deployMismatched = Invoke-CountdownDeployTest -Label "status $deployMismatch mismatch"
        Assert-Equal $true ([bool] $deployMismatched.Result.install.succeeded) "The installer reports success before the $deployMismatch changes"
        Assert-Equal "failed|False|status|status-mismatch|$deployMismatch" "$($deployMismatched.Result.outcome)|$($deployMismatched.Result.succeeded)|$($deployMismatched.Result.failedStep)|$($deployMismatched.Result.reason)|$(@($deployMismatched.Result.statusMismatches) -join ',')" "Deploy does not report success when the status read disagrees on the $deployMismatch"
    }

    # 安装器报告成功，但安装后的状态读取没有读到（连接断开）：确认不了办公室在运行什么，结果是未知，提示先查状态。
    $deployCase = New-CountdownDeployTestCase -Name 'status-unreadable'
    $deployCase.Responses['read2'] = { param($Run) [pscustomobject]@{ ExitCode = 255; Output = ''; Host = $deployHostSentinel; User = $deployAccountSentinel } }
    $deployStatusLost = Invoke-CountdownDeployTest -Label 'status read lost after the install'
    Assert-Equal 'unknown|status|status-unreadable|True' "$($deployStatusLost.Result.outcome)|$($deployStatusLost.Result.failedStep)|$($deployStatusLost.Result.reason)|$($null -eq $deployStatusLost.Result.succeeded)" "A status read lost after a reported install leaves the result unknown: $($deployStatusLost.Result.error)"
    Assert-Equal $true ([string] $deployStatusLost.Result.nextStep).Contains('-StatusOnly') 'An unreadable status after the install tells to read the status first'

    # 本机预检的拒绝（#286）：每种情况都在连接远端之前拒绝并写明缺什么。夹具每次只改一处，用完放回原样。
    # 工作树不干净：有一个未跟踪的文件。
    $deployCase = New-CountdownDeployTestCase -Name 'dirty'
    $deployDirtyPath = Join-Path $deployRepo.Root 'deploy-dirty-note.txt'
    [System.IO.File]::WriteAllText($deployDirtyPath, 'uncommitted', $deployUtf8)
    try { $deployDirty = Invoke-CountdownDeployTest -Label 'dirty work tree' }
    finally { [System.IO.File]::Move($deployDirtyPath, (Join-Path $deployTestRoot 'deploy-dirty-note.txt')) }
    Assert-CountdownDeployRejected -Deploy $deployDirty -Reason 'dirty-work-tree' -Names @('deploy-dirty-note.txt') -Label 'A dirty work tree'

    # 提交未推送：main 上多了一个只在本地的提交。
    $deployCase = New-CountdownDeployTestCase -Name 'unpushed'
    [void] (Invoke-CountdownDeployTestGit -Root $deployRepo.Root -Arguments @('commit', '--quiet', '--allow-empty', '-m', 'local only'))
    try {
        $deployLocalHead = (@(Invoke-CountdownDeployTestGit -Root $deployRepo.Root -Arguments @('rev-parse', '--verify', 'HEAD')) -join '').Trim()
        $deployUnpushed = Invoke-CountdownDeployTest -Label 'unpushed commit'
    }
    finally { [void] (Invoke-CountdownDeployTestGit -Root $deployRepo.Root -Arguments @('reset', '--quiet', '--soft', 'HEAD~1')) }
    Assert-CountdownDeployRejected -Deploy $deployUnpushed -Reason 'not-pushed' -Names @($deployLocalHead.Substring(0, 7), $deployRepo.Head.Substring(0, 7)) -Label 'An unpushed commit'

    # 缺少套件证据：只有别的提交的套件记录，HEAD 没有。
    $deployCase = New-CountdownDeployTestCase -Name 'no-suite-run'
    $deployNoSuiteRun = Invoke-CountdownDeployTestWithSuiteRuns -Label 'missing suite evidence' -Runs @(
        @{ RunId = '20261114T080000000-00000000'; Commit = '0123456789abcdef0123456789abcdef01234567' })
    Assert-CountdownDeployRejected -Deploy $deployNoSuiteRun -Reason 'suite-evidence-missing' -Names @($deployRepo.Head.Substring(0, 7), 'Invoke-CountdownSuiteLowImpact.ps1') -Label 'Missing suite evidence'

    # 必过阶段不全：这一轮没跑 SourceValidation，CSharp 以 75（参考基线待重拍）退出；拒绝时两项都写明，不只报第一项。
    $deployCase = New-CountdownDeployTestCase -Name 'stages'
    $deployStagesRunId = '20261114T100000000-11111111'
    $deployStages = Invoke-CountdownDeployTestWithSuiteRuns -Label 'incomplete required stages' -Runs @(
        @{ RunId = $deployStagesRunId; Stages = @('Build', 'Core', 'CSharp', 'InstallValidate'); StageExitCodes = @{ CSharp = 75 } })
    Assert-CountdownDeployRejected -Deploy $deployStages -Reason 'required-stages-incomplete' -Names @($deployStagesRunId, 'SourceValidation', 'CSharp', '75') -Label 'Incomplete required stages'

    # 构建回执不是干净构建：这一轮用增量构建（IncrementalBuild，回执 Mode Incremental）代替了干净构建 Build，其余必过阶段都通过。
    $deployCase = New-CountdownDeployTestCase -Name 'incremental'
    $deployIncremental = Invoke-CountdownDeployTestWithSuiteRuns -Label 'incremental build receipt' -Runs @(
        @{ RunId = '20261114T110000000-22222222'; Stages = @('IncrementalBuild', 'Core', 'CSharp', 'SourceValidation', 'InstallValidate') })
    Assert-CountdownDeployRejected -Deploy $deployIncremental -Reason 'build-receipt-not-clean' -Names @('IncrementalBuild', 'Incremental', 'CleanR2R') -Label 'A build receipt that is not a clean build'

    # 聚合哈希与回执不一致：套件测过的是另一份构建输出（回执记的聚合哈希不同），程序目录不是那一份。
    $deployCase = New-CountdownDeployTestCase -Name 'aggregate'
    $deployOtherAggregate = 'AB' * 32
    $deployAggregateRunId = '20261114T120000000-33333333'
    $deployAggregateMismatch = Invoke-CountdownDeployTestWithSuiteRuns -Label 'aggregate mismatch' -Runs @(
        @{ RunId = $deployAggregateRunId; ReceiptAggregate = $deployOtherAggregate })
    Assert-CountdownDeployRejected -Deploy $deployAggregateMismatch -Reason 'aggregate-mismatch' -Names @($deployAggregateRunId, $deployAggregate.Substring(0, 12), $deployOtherAggregate.Substring(0, 12)) -Label 'An aggregate hash that differs from the receipt'

    # 构建输出被改动：载荷清单和聚合哈希都还对，但程序目录里的一个文件在套件之后变了；打包时边写边核对，仍在连接远端之前拒绝。
    $deployCase = New-CountdownDeployTestCase -Name 'modified'
    $deployModifiedPath = Join-Path $deployRepo.Root $deployBuildFiles[1].Path
    [System.IO.File]::WriteAllText($deployModifiedPath, 'deploy dll tampered', $deployUtf8)
    try { $deployModified = Invoke-CountdownDeployTest -Label 'modified build output' }
    finally { [System.IO.File]::WriteAllText($deployModifiedPath, $deployBuildFiles[1].Text, $deployUtf8) }
    Assert-CountdownDeployRejected -Deploy $deployModified -Reason 'build-output-modified' -Names @($deployBuildFiles[1].Path) -Label 'A build output file changed after the suite' -FailedStep 'package'

    # 其余预检条件同样在连接远端之前拒绝并写明缺什么：不在 main 上；本机没有构建输出；构建输出不是 HEAD 构建的。
    $deployCase = New-CountdownDeployTestCase -Name 'side-branch'
    [void] (Invoke-CountdownDeployTestGit -Root $deployRepo.Root -Arguments @('switch', '--quiet', '-c', 'deploy-side'))
    try { $deploySideBranch = Invoke-CountdownDeployTest -Label 'not on main' }
    finally { [void] (Invoke-CountdownDeployTestGit -Root $deployRepo.Root -Arguments @('switch', '--quiet', 'main')) }
    Assert-CountdownDeployRejected -Deploy $deploySideBranch -Reason 'not-main' -Names @('deploy-side') -Label 'A branch other than main'

    $deployManifestPath = Join-Path $deployRepo.Root 'app\published-payload.json'
    $deployManifestBytes = [System.IO.File]::ReadAllBytes($deployManifestPath)
    $deployCase = New-CountdownDeployTestCase -Name 'no-build-output'
    [System.IO.File]::Move($deployManifestPath, (Join-Path $deployTestRoot 'published-payload.json.kept'))
    try { $deployNoBuildOutput = Invoke-CountdownDeployTest -Label 'no build output' }
    finally { [System.IO.File]::Move((Join-Path $deployTestRoot 'published-payload.json.kept'), $deployManifestPath) }
    Assert-CountdownDeployRejected -Deploy $deployNoBuildOutput -Reason 'build-output-missing' -Names @('published-payload.json') -Label 'Missing local build output'

    $deployCase = New-CountdownDeployTestCase -Name 'other-build'
    $deployOtherBuild = [System.IO.File]::ReadAllText($deployManifestPath) | ConvertFrom-Json
    $deployOtherBuild.commit = "$($deployRepo.Head)-dirty"
    [System.IO.File]::WriteAllText($deployManifestPath, ($deployOtherBuild | ConvertTo-Json -Depth 4), $deployUtf8)
    try { $deployOtherBuildRun = Invoke-CountdownDeployTest -Label 'build output from a dirty tree' }
    finally { [System.IO.File]::WriteAllBytes($deployManifestPath, $deployManifestBytes) }
    Assert-CountdownDeployRejected -Deploy $deployOtherBuildRun -Reason 'build-output-invalid' -Names @("$($deployRepo.Head)-dirty") -Label 'Build output built from another state than HEAD'

    # 连通性探测失败：远端执行入口连不上办公室（退出码 255、没有输出），部署在传输之前停下，办公室没有收到包。
    $deployCase = New-CountdownDeployTestCase -Name 'probe'
    $deployCase.Responses['read1'] = { param($Run) [pscustomobject]@{ ExitCode = 255; Output = ''; Host = $deployHostSentinel; User = $deployAccountSentinel } }
    $deployProbe = Invoke-CountdownDeployTest -Label 'probe failure'
    Assert-Equal 'failed|False|probe|probe-failed' "$($deployProbe.Result.outcome)|$($deployProbe.Result.succeeded)|$($deployProbe.Result.failedStep)|$($deployProbe.Result.reason)" "A failed probe fails the deploy at the probe: $($deployProbe.Result.error)"
    Assert-Equal 'remote-read' ($deployCase.Calls -join ',') 'A failed probe stops the deploy before any transfer'
    Assert-Equal $false (Test-Path -LiteralPath $deployCase.StagingRoot) 'A failed probe leaves no staging directory on the office'
    Assert-Equal 'probe' ([string] ([System.IO.File]::ReadAllText([string] $deployProbe.Result.recordPath) | ConvertFrom-Json).failedStep) 'The deployment record names the failed probe'
    Assert-CountdownDeployNoLeak -Text $deployProbe.Text -Name 'Probe failure standard output'

    # 传输失败：传输入口报告失败，部署停在传输，不运行安装。
    $deployCase = New-CountdownDeployTestCase -Name 'transfer'
    $deployCase.Responses['transfer1'] = { [pscustomobject]@{ Status = 'failed'; AttemptId = 'attempt'; Sha256 = ''; Bytes = 0; LedgerPath = ''; Hostname = $deployHostSentinel } }
    $deployTransfer = Invoke-CountdownDeployTest -Label 'transfer failure'
    Assert-Equal 'failed|False|transfer|transfer-failed' "$($deployTransfer.Result.outcome)|$($deployTransfer.Result.succeeded)|$($deployTransfer.Result.failedStep)|$($deployTransfer.Result.reason)" "A failed transfer fails the deploy at the transfer: $($deployTransfer.Result.error)"
    Assert-Equal 'remote-read,transfer' ($deployCase.Calls -join ',') 'A failed transfer stops the deploy before the install'
    Assert-CountdownDeployNoLeak -Text $deployTransfer.Text -Name 'Transfer failure standard output'

    # 远端安装失败：结果写明安装器的失败阶段与回滚结果。回滚成功、回滚失败、安装器没报回滚结果是三种各自明确的结果，
    # 都不报告成功；办公室现状不确定时（回滚失败或没报）提示先查状态。安装失败后不再做状态读取。
    foreach ($deployRollback in @('rolled-back', 'rollback-failed', '')) {
        $deployCase = New-CountdownDeployTestCase -Name "install-$(if ($deployRollback) { $deployRollback } else { 'no-rollback' })"
        $deployCase.Responses['write1'] = { param($Run) New-CountdownDeployTestInstallFailure -RollbackOutcome $deployRollback }
        $deployInstallFailed = Invoke-CountdownDeployTest -Label "install failed, rollback '$deployRollback'"
        $deployInstallResult = $deployInstallFailed.Result
        $deployLabel = "An install that failed with rollback '$deployRollback'"
        Assert-Equal "failed|False|install|install-failed|$deployRollback" "$($deployInstallResult.outcome)|$($deployInstallResult.succeeded)|$($deployInstallResult.failedStep)|$($deployInstallResult.reason)|$($deployInstallResult.rollback)" "$deployLabel is a failure that reports its rollback result: $($deployInstallResult.error)"
        Assert-Equal 'start-and-wait-ready' ([string] $deployInstallResult.install.failedStage) "$deployLabel reports the installer's failed stage"
        Assert-Equal $true ([string] $deployInstallResult.error).Contains('start-and-wait-ready') "$deployLabel names the failed stage in the error"
        Assert-Equal ($deployRollback -cne 'rolled-back') ([string] $deployInstallResult.nextStep).Contains('-StatusOnly') "$deployLabel tells to read the status first only when the office state is uncertain"
        Assert-Equal 'remote-read,transfer,remote-write' ($deployCase.Calls -join ',') "$deployLabel makes no status read after the install"
        Assert-Equal "$deployRollback" ([string] ([System.IO.File]::ReadAllText([string] $deployInstallResult.recordPath) | ConvertFrom-Json).rollback) "$deployLabel keeps the rollback result in the deployment record"
        Assert-CountdownDeployNoLeak -Text $deployInstallFailed.Text -Name "$deployLabel standard output"
    }

    # 新版本在启动就绪之前就退出（#288）：部署照样报告安装失败与回滚结果，错误里有安装器的原因 startup-exited 与退出码，
    # 安装结果与部署记录都保留退出码。
    $deployCase = New-CountdownDeployTestCase -Name 'install-startup-exited'
    $deployCase.Responses['write1'] = { param($Run) New-CountdownDeployTestInstallFailure -RollbackOutcome 'rolled-back' -StartupExitCode 1 }
    $deployExitedRun = Invoke-CountdownDeployTest -Label 'install failed, new version exited before startup readiness'
    $deployExitedResult = $deployExitedRun.Result
    Assert-Equal 'failed|install|install-failed|rolled-back|startup-exited|1' "$($deployExitedResult.outcome)|$($deployExitedResult.failedStep)|$($deployExitedResult.reason)|$($deployExitedResult.rollback)|$($deployExitedResult.install.reason)|$($deployExitedResult.install.startupExitCode)" "An install whose new version exited is a failure that keeps the installer's reason and exit code: $($deployExitedResult.error)"
    Assert-Equal $true (([string] $deployExitedResult.error).Contains('startup-exited') -and ([string] $deployExitedResult.error).Contains('0x00000001')) 'The deploy error names the early exit and the exit code'
    Assert-Equal 1 ([int] ([System.IO.File]::ReadAllText([string] $deployExitedResult.recordPath) | ConvertFrom-Json).install.startupExitCode) 'The deployment record keeps the exit code'
    Assert-CountdownDeployNoLeak -Text $deployExitedRun.Text -Name 'Early-exit install failure standard output'

    # 包没能交给安装器（办公室上核对包哈希失败）：安装器没有运行，办公室的安装没有改动；这是明确的失败，没有需要回滚的改动。
    $deployCase = New-CountdownDeployTestCase -Name 'install-not-started'
    $deployCase.Responses['write1'] = {
        param($Run)
        $envelope = [ordered]@{ packageVerified = $false; installerStarted = $false; installer = $null; stagingRemoved = $true; error = 'package hash mismatch on the office' }
        [pscustomobject]@{ ExitCode = 0; Output = ($envelope | ConvertTo-Json -Compress); Host = $deployHostSentinel; User = $deployAccountSentinel }
    }
    $deployNotStarted = Invoke-CountdownDeployTest -Label 'installer never started'
    Assert-Equal 'failed|False|install|install-failed|not-needed' "$($deployNotStarted.Result.outcome)|$($deployNotStarted.Result.succeeded)|$($deployNotStarted.Result.failedStep)|$($deployNotStarted.Result.reason)|$($deployNotStarted.Result.rollback)" "A package the installer never received is a failure with nothing to roll back: $($deployNotStarted.Result.error)"
    Assert-Equal $true ([string] $deployNotStarted.Result.error).Contains('package hash mismatch on the office') 'The failure carries the remote error'

    # 连接在安装中途断开：安装在办公室上照常跑完，应答却丢了（远端执行入口退出码 255、没有输出）。结果是“未知”：既不报告成功
    # 也不报告失败，提示先查状态。随后只查状态读到这次提交在运行、没有事务标记，重新部署就直接报告已在运行，不再安装。
    $deployCase = New-CountdownDeployTestCase -Name 'dropped'
    $deployCase.Responses['write1'] = { param($Run) $null = & $Run; [pscustomobject]@{ ExitCode = 255; Output = ''; Host = $deployHostSentinel; User = $deployAccountSentinel } }
    $deployDropped = Invoke-CountdownDeployTest -Label 'connection dropped during the install'
    $deployDroppedResult = $deployDropped.Result
    Assert-Equal 'unknown|install|install-result-unknown' "$($deployDroppedResult.outcome)|$($deployDroppedResult.failedStep)|$($deployDroppedResult.reason)" "A connection drop during the install leaves the result unknown: $($deployDroppedResult.error)"
    Assert-Equal $true ($null -eq $deployDroppedResult.succeeded) 'An unknown result reports neither success nor failure'
    Assert-Equal $true (([string] $deployDroppedResult.nextStep).Contains('-StatusOnly') -and ([string] $deployDroppedResult.nextStep).Contains('transactionMarkerPresent')) 'An unknown result tells to read the status first and to deploy again when it shows a transaction marker'
    Assert-Equal 'remote-read,transfer,remote-write' ($deployCase.Calls -join ',') 'After the connection drop the deploy makes no further remote call'
    Assert-Equal 'unknown' ([string] ([System.IO.File]::ReadAllText([string] $deployDroppedResult.recordPath) | ConvertFrom-Json).outcome) 'The deployment record keeps the unknown result'
    Assert-CountdownDeployNoLeak -Text $deployDropped.Text -Name 'Unknown result standard output'
    $deployCase.Calls.Clear()
    $deployDroppedStatus = Invoke-CountdownDeployTest -Label 'status read after the drop' -StatusOnly
    Assert-Equal "$($deployRepo.Head)|True|False" "$($deployDroppedStatus.Result.status.commit)|$($deployDroppedStatus.Result.status.running)|$($deployDroppedStatus.Result.status.transactionMarkerPresent)" 'The status read after the drop shows the commit running and no unfinished install'
    $deployDroppedAgain = Invoke-CountdownDeployTest -Label 'deploy again after the drop'
    Assert-Equal 'already-running' ([string] $deployDroppedAgain.Result.outcome) "Deploying again after the drop reports the commit already running: $($deployDroppedAgain.Result.error)"
    Assert-Equal 'remote-read,remote-read' ($deployCase.Calls -join ',') 'Neither the status read nor the second deploy transfers or installs'
    # 状态显示有未完成的安装（事务标记）时，这次提交在运行也不算已在运行：重新部署照常传输并交给安装器，由安装器先恢复（#284）
    # 再安装。这里的标记读不出是哪一版、也没有候选或旧版本目录，恢复清除标记后照常装上；部署结果里带着安装器的恢复报告。
    [System.IO.File]::WriteAllText((Join-Path $deployCase.InstallRoot 'install-transaction.json'), '{}', $deployUtf8)
    $deployCase.Calls.Clear()
    $deployCase.Responses.Clear()
    $deployMarker = Invoke-CountdownDeployTest -Label 'deploy again over an unfinished install'
    Assert-Equal $true ([bool] $deployMarker.Result.statusBefore.transactionMarkerPresent) 'The probe reads the unfinished install marker'
    Assert-Equal $true ($deployMarker.Result.outcome -cne 'already-running' -and ($deployCase.Calls -join ',').StartsWith('remote-read,transfer,remote-write')) "A deploy over an unfinished install hands the package to the installer instead of reporting already running: $($deployMarker.Result.outcome) $($deployMarker.Result.error)"
    Assert-Equal 'deployed|before-replace|discarded-candidate|recovered|False' "$($deployMarker.Result.outcome)|$($deployMarker.Result.install.recovery.state)|$($deployMarker.Result.install.recovery.action)|$($deployMarker.Result.install.recovery.outcome)|$($deployMarker.Result.status.transactionMarkerPresent)" "Deploying again recovers the unfinished install and installs: $($deployMarker.Result.error)"

    # 同样是结果未知：远端执行入口在安装这一步自己失败（port 抛错）；安装器开始运行了却没给出结果。
    $deployUnknownResponses = [ordered]@{
        'entry-failed' = { param($Run) throw 'remote entry failed: IOException' }
        'no-installer-result' = {
            param($Run)
            $envelope = [ordered]@{ packageVerified = $true; installerStarted = $true; installer = $null; stagingRemoved = $false; error = 'installer terminated' }
            [pscustomobject]@{ ExitCode = 0; Output = ($envelope | ConvertTo-Json -Compress); Host = $deployHostSentinel; User = $deployAccountSentinel }
        }
    }
    foreach ($deployUnknownName in $deployUnknownResponses.Keys) {
        $deployCase = New-CountdownDeployTestCase -Name $deployUnknownName
        $deployCase.Responses['write1'] = $deployUnknownResponses[$deployUnknownName]
        $deployUnknown = Invoke-CountdownDeployTest -Label $deployUnknownName
        Assert-Equal 'unknown|install|install-result-unknown|True' "$($deployUnknown.Result.outcome)|$($deployUnknown.Result.failedStep)|$($deployUnknown.Result.reason)|$($null -eq $deployUnknown.Result.succeeded)" "An install step without an installer result is unknown ($deployUnknownName): $($deployUnknown.Result.error)"
        Assert-Equal $true ([string] $deployUnknown.Result.nextStep).Contains('-StatusOnly') "An unknown result tells to read the status first ($deployUnknownName)"
    }

    # 本地记录写不进去（记录根被一个文件占着）时，未知的结果仍是未知，错误里补上记录没写成。
    $deployCase = New-CountdownDeployTestCase -Name 'record-blocked'
    $deployCase.Responses['write1'] = $deployUnknownResponses['entry-failed']
    [System.IO.File]::WriteAllText($deployCase.RecordRoot, 'not a directory', $deployUtf8)
    $deployRecordBlocked = Invoke-CountdownDeployTest -Label 'unknown result with an unwritable record root'
    Assert-Equal 'unknown|install|True' "$($deployRecordBlocked.Result.outcome)|$($deployRecordBlocked.Result.failedStep)|$($null -eq $deployRecordBlocked.Result.recordPath)" "A record that cannot be written keeps the unknown result: $($deployRecordBlocked.Result.error)"
    Assert-Equal $true ([string] $deployRecordBlocked.Result.error).Contains('deployment-record.json') 'The error says the deployment record was not written'

    # 办公室还是现有布局：登录任务经 pwsh 运行 adapter，桌面上是启动器 EXE；程序目录里已经是 HEAD 这一版（经现有部署路径送达），
    # 旧版本在运行。
    function Initialize-CountdownDeployTestOldLayout {
        $oldRoot = $deployCase.InstallRoot
        foreach ($file in $deployBuildFiles) {
            $path = Join-Path $oldRoot $file.Path
            New-Item -ItemType Directory -Path (Split-Path -Parent $path) -Force | Out-Null
            [System.IO.File]::WriteAllText($path, $file.Text, $deployUtf8)
        }
        [System.IO.File]::Copy((Join-Path $deployRepo.Root 'app\published-payload.json'), (Join-Path $oldRoot 'app\published-payload.json'))
        foreach ($name in @('敏敏子倒计时.ps1', 'CountdownLauncher.exe')) { [System.IO.File]::WriteAllText((Join-Path $oldRoot $name), "deploy old layout $name", $deployUtf8) }
        [System.IO.File]::WriteAllText((Join-Path $oldRoot 'runtime-status.json'), '{"schemaVersion":4,"stage":"window-ready","processId":4101,"sessionId":3}', $deployUtf8)
        [System.IO.File]::Copy((Join-Path $oldRoot 'CountdownLauncher.exe'), (Join-Path $deployCase.Desktop '敏敏子倒计时.exe'))
        $deployCase.Tasks[$deployCase.TaskName] = [pscustomobject]@{
            State = 'Ready'
            Enabled = $true
            Actions = @([pscustomobject]@{ Type = 'Exec'; Execute = 'C:\Program Files\PowerShell\7\pwsh.exe'; Arguments = "-NoLogo -NoProfile -ExecutionPolicy RemoteSigned -WindowStyle Hidden -STA -File `"$(Join-Path $oldRoot '敏敏子倒计时.ps1')`""; WorkingDirectory = $oldRoot })
            Triggers = @([pscustomobject]@{ Type = 'Logon'; Enabled = $true; UserSid = $deploySessionSid })
            UserSid = $deploySessionSid
            LogonType = 'Interactive'
            RunLevel = 'Limited'
            Priority = 7
            ExecutionTimeLimit = 'PT0S'
            MultipleInstances = 'IgnoreNew'
            DisallowStartIfOnBatteries = $false
            StopIfGoingOnBatteries = $false
            AllowDemandStart = $true
            AllowHardTerminate = $true
            UserId = 'old-installer-task-owner'
        }
        $deployCase.Processes = @([pscustomobject]@{ ProcessId = 4101; Name = 'MinminCountdown.exe'; ExecutablePath = (Join-Path $oldRoot 'app\MinminCountdown.exe'); SessionId = 3; CreationDate = [DateTimeOffset]::Parse('2026-11-14T09:00:00Z').LocalDateTime; StartedByTask = $null })
    }

    # 布局切换经新部署入口（#287）：办公室还是现有布局，程序目录里的 HEAD 这一版不让切换模式报告已在运行。包里带上切换脚本，
    # 远端写操作在办公室上运行它：备份、经新安装器换布局、移除遗留；再读一次状态，登录任务已是新定义。
    $deployCase = New-CountdownDeployTestCase -Name 'layout-switch'
    $deployOldRoot = $deployCase.InstallRoot
    Initialize-CountdownDeployTestOldLayout
    $deployLayoutState = {
        $tree = @((Get-CountdownDeployTestTree -Root $deployOldRoot) -split "`n" | Where-Object { $_ -notlike 'file:runtime-status.json|*' }) -join "`n"
        $task = $deployCase.Tasks[$deployCase.TaskName] | Select-Object -Property * -ExcludeProperty State | ConvertTo-Json -Depth 4 -Compress
        "$tree`n$(Get-CountdownDeployTestTree -Root $deployCase.Desktop)`n$task"
    }
    $deployLayoutBefore = & $deployLayoutState
    $deploySwitched = Invoke-CountdownDeployTest -Label 'layout switch' -LayoutSwitch
    $deploySwitch = $deploySwitched.Result
    Assert-Equal 'deployed|layout-switch|switch|created' "$($deploySwitch.outcome)|$($deploySwitch.mode)|$($deploySwitch.install.mode)|$($deploySwitch.install.backup.action)" "The layout switch runs through the deploy entry: $($deploySwitch.failedStep) $($deploySwitch.error)"
    Assert-Equal "$($deployRepo.Head)|True" "$($deploySwitch.statusBefore.commit)|$($deploySwitch.statusBefore.running)" 'The probe reads the commit already running in the old layout, and the switch goes on'
    Assert-Equal 'remote-read,transfer,remote-write,remote-read' ($deployCase.Calls -join ',') 'The switch travels in the package and runs as one remote write between the probe and the status read'
    Assert-Equal "$($deployRepo.Head)|True|True|7001" "$($deploySwitch.status.commit)|$($deploySwitch.status.running)|$($deploySwitch.status.loginTask.matchesExpected)|$($deploySwitch.install.readyProcess.processId)" 'The status read after the switch shows the new layout running'
    Assert-Equal 'False|False|敏敏子倒计时.lnk' "$(Test-Path -LiteralPath (Join-Path $deployOldRoot '敏敏子倒计时.ps1'))|$(Test-Path -LiteralPath (Join-Path $deployOldRoot 'CountdownLauncher.exe'))|$(@(Get-ChildItem -LiteralPath $deployCase.Desktop -Force | ForEach-Object { $_.Name }) -join ',')" 'The switch through the deploy entry removes the old files and leaves the shortcut on the desktop'
    Assert-Equal $true ($deployCase.MaxEncodedLength -gt 0 -and $deployCase.MaxEncodedLength -le 29000) "The switch's remote scripts fit the remote entry's encoded command limit: $($deployCase.MaxEncodedLength)"
    Assert-CountdownDeployNoLeak -Text $deploySwitched.Text -Name 'Layout switch standard output'

    # 另一个运行持有全机安装锁时，回退在办公室上被拒绝，什么都没改：结论是 rejected，下一步是等它结束、先读状态。
    $deploySwitchedState = Get-CountdownDeployTestOfficeState
    $deployCase.Calls.Clear()
    $deployLockHolder = Start-CountdownTestLockHolder -Name $deployLockName
    try { $deployRollbackLocked = Invoke-CountdownDeployTest -Label 'layout rollback while another run holds the install lock' -LayoutRollback }
    finally { Stop-CountdownTestLockHolder -Holder $deployLockHolder }
    Assert-Equal 'rejected|False|rollback|transaction-in-progress|transfer,remote-write' "$($deployRollbackLocked.Result.outcome)|$($deployRollbackLocked.Result.succeeded)|$($deployRollbackLocked.Result.failedStep)|$($deployRollbackLocked.Result.reason)|$($deployCase.Calls -join ',')" "A rollback refused on the office for a run in progress is rejected: $($deployRollbackLocked.Result.error)"
    Assert-Equal $true ([string] $deployRollbackLocked.Result.nextStep).Contains('-StatusOnly') "A rollback refused for a run in progress tells to wait and read the status first: $($deployRollbackLocked.Result.nextStep)"
    Assert-Equal $deploySwitchedState (Get-CountdownDeployTestOfficeState) 'A rollback refused for a run in progress changes nothing on the office'

    # 回退经新部署入口：把仓库里的切换脚本与新安装器 module 打成包送到暂存目录，一次写操作核对包哈希后以 -Rollback 运行包里的
    # 切换脚本（#259 评审），不运行办公室备份里的副本：备份 tools\ 里的切换脚本被换成一个只留下记号的脚本（backup.json 里的哈希也
    # 跟着改了），回退照常走完，记号没有出现。安装根、登录任务与桌面启动器回到切换前，旧版本经恢复的任务重新启动；本地记录写下回退结果。
    $deployBackupRoot = "$($deployCase.InstallRoot).layout-switch-backup"
    $deployForgedTool = Join-Path $deployBackupRoot 'tools\Switch-CountdownInstallLayout.ps1'
    $deployForgedMarker = Join-Path $deployCase.OfficeRoot 'forged-tool-ran.txt'
    [System.IO.File]::WriteAllText($deployForgedTool, "[System.IO.File]::WriteAllText('$($deployForgedMarker.Replace("'", "''"))', 'ran')", $deployUtf8)
    $deployBackupManifest = [System.IO.File]::ReadAllText((Join-Path $deployBackupRoot 'backup.json')) | ConvertFrom-Json -DateKind String
    @($deployBackupManifest.tools | Where-Object { $_.name -eq 'Switch-CountdownInstallLayout.ps1' })[0].sha256 = (Get-FileHash -LiteralPath $deployForgedTool -Algorithm SHA256).Hash
    [System.IO.File]::WriteAllText((Join-Path $deployBackupRoot 'backup.json'), ($deployBackupManifest | ConvertTo-Json -Depth 6), $deployUtf8)
    $deployCase.Calls.Clear()
    $deployRolledBack = Invoke-CountdownDeployTest -Label 'layout rollback' -LayoutRollback
    $deployRollback = $deployRolledBack.Result
    Assert-Equal 'rolled-back|True|layout-rollback|transfer,remote-write' "$($deployRollback.outcome)|$($deployRollback.succeeded)|$($deployRollback.mode)|$($deployCase.Calls -join ',')" "The rollback runs through the deploy entry as a transfer and one remote write: $($deployRollback.failedStep) $($deployRollback.error)"
    Assert-Equal 'True|True|False' "$($deployRollback.rollback.packageVerified)|$($deployRollback.rollback.stagingRemoved)|$(Test-Path -LiteralPath $deployForgedMarker)" 'The rollback runs the switch script of the hash-checked package, not the copy in the office backup, and removes the staging directory'
    Assert-Equal ([string] $deployRollback.package.sha256) ([string] $deployRollback.transfer.remoteSha256) 'The rollback transfer verified the package hash on the office'
    Assert-Equal $deployLayoutBefore (& $deployLayoutState) 'After the rollback through the deploy entry the install root, the login task and the desktop launcher are as before the switch'
    Assert-Equal '7002|7002' "$($deployRollback.rollback.restartedProcess.processId)|$(@($deployCase.Processes | ForEach-Object { $_.ProcessId }) -join ',')" 'After the rollback the old version runs again'
    Assert-Equal 'layout-rollback|rolled-back' "$(([System.IO.File]::ReadAllText([string] $deployRollback.recordPath) | ConvertFrom-Json).mode)|$(([System.IO.File]::ReadAllText([string] $deployRollback.recordPath) | ConvertFrom-Json).outcome)" 'The local record keeps the rollback result'
    Assert-CountdownDeployNoLeak -Text $deployRolledBack.Text -Name 'Layout rollback standard output'
    Assert-CountdownDeployNoLeak -Text $deployRolledBack.Stderr -Name 'Layout rollback progress'

    # 办公室桌面上已有一个同名的 敏敏子倒计时.lnk，却不是安装器的（#259 评审）：切换经部署入口照常完成，旧启动器留在桌面上；
    # 结论之后的下一步带上切换脚本给的那句（移走那个文件、再次切换），再说经部署入口怎么再次切换。
    $deployCase = New-CountdownDeployTestCase -Name 'layout-switch-foreign-shortcut'
    Initialize-CountdownDeployTestOldLayout
    [System.IO.File]::WriteAllText((Join-Path $deployCase.Desktop '敏敏子倒计时.lnk'), 'my own notes that happen to share the shortcut name', $deployUtf8)
    $deployForeignShortcut = Invoke-CountdownDeployTest -Label 'layout switch with a foreign file holding the shortcut name' -LayoutSwitch
    Assert-Equal 'deployed|True|kept-foreign|敏敏子倒计时.exe=kept-no-shortcut' "$($deployForeignShortcut.Result.outcome)|$($deployForeignShortcut.Result.succeeded)|$($deployForeignShortcut.Result.install.installer.desktopShortcut.action)|$(@($deployForeignShortcut.Result.install.legacy.desktop | Where-Object { $_.name -eq '敏敏子倒计时.exe' } | ForEach-Object { "$($_.name)=$($_.action)" }) -join ',')" "A switch that has to keep the old launchers still completes through the deploy entry: $($deployForeignShortcut.Result.failedStep) $($deployForeignShortcut.Result.error)"
    Assert-Equal 'True|True' "$(([string] $deployForeignShortcut.Result.nextStep).StartsWith([string] $deployForeignShortcut.Result.install.nextStep))|$(([string] $deployForeignShortcut.Result.nextStep).Contains('-LayoutSwitch'))" "The next step carries the switch's own next step and how to run the switch again through the deploy entry: $($deployForeignShortcut.Result.nextStep)"
    Assert-Equal $true ([string] $deployForeignShortcut.Result.install.nextStep).Contains('敏敏子倒计时.lnk') "The switch's next step names the foreign file: $($deployForeignShortcut.Result.install.nextStep)"

    # 没有备份时回退什么都不做，明确报告失败；三种模式只能选一个，同时给出时在连接办公室之前拒绝。
    $deployCase = New-CountdownDeployTestCase -Name 'rollback-without-backup'
    $deployNoBackup = Invoke-CountdownDeployTest -Label 'rollback without a backup' -LayoutRollback
    Assert-Equal 'failed|rollback|rollback-failed|transfer,remote-write' "$($deployNoBackup.Result.outcome)|$($deployNoBackup.Result.failedStep)|$($deployNoBackup.Result.reason)|$($deployCase.Calls -join ',')" "A rollback without a backup fails and says so: $($deployNoBackup.Result.error)"
    $deployCase.Calls.Clear()
    $deployBothModes = Invoke-CountdownDeployTest -Label 'switch and rollback together' -LayoutSwitch -LayoutRollback
    Assert-Equal 'rejected|conflicting-modes|' "$($deployBothModes.Result.outcome)|$($deployBothModes.Result.reason)|$($deployCase.Calls -join ',')" 'Asking for two modes at once is rejected before contacting the office'

    # 办公室上另一个安装、切换或回退正在运行（持有全机安装锁）：只查状态读到有运行在进行；部署与布局切换都在办公室上被拒绝，
    # 什么都没改。结论是 rejected（不是失败），下一步是等它结束、先读状态。
    $deployCase = New-CountdownDeployTestCase -Name 'locked'
    New-Item -ItemType Directory -Path $deployCase.StagingRoot -Force | Out-Null
    $deployLockedBefore = Get-CountdownDeployTestOfficeState
    $deployLockedRuns = [ordered]@{}
    $deployLockHolder = Start-CountdownTestLockHolder -Name $deployLockName
    try {
        $deployLockedStatus = Invoke-CountdownDeployTest -Label 'status read while another run holds the install lock' -StatusOnly
        foreach ($deployLockedMode in @('deploy', 'layout-switch')) {
            $deployCase.Calls.Clear()
            $deployLockedRun = Invoke-CountdownDeployTest -Label "$deployLockedMode while another run holds the install lock" -LayoutSwitch:($deployLockedMode -eq 'layout-switch')
            $deployLockedRuns[$deployLockedMode] = "$($deployLockedRun.Result.outcome)|$($deployLockedRun.Result.succeeded)|$($deployLockedRun.Result.failedStep)|$($deployLockedRun.Result.reason)|$($deployCase.Calls -join ',')|$(([string] $deployLockedRun.Result.nextStep).Contains('-StatusOnly'))"
        }
    }
    finally { Stop-CountdownTestLockHolder -Holder $deployLockHolder }
    Assert-Equal $true $deployLockedStatus.Result.status.installInProgress 'Status-only mode reads a run in progress while another run holds the install lock'
    Assert-Equal 'rejected|False|install|install-in-progress|remote-read,transfer,remote-write|True' $deployLockedRuns['deploy'] 'A deploy refused on the office for a run in progress is rejected and tells to wait and read the status first'
    Assert-Equal 'rejected|False|install|transaction-in-progress|remote-read,transfer,remote-write|True' $deployLockedRuns['layout-switch'] 'A layout switch refused on the office for a run in progress is rejected and tells to wait and read the status first'
    Assert-Equal $deployLockedBefore (Get-CountdownDeployTestOfficeState) 'A deploy and a layout switch refused for a run in progress change nothing on the office'

    # 切换中途失败（罐装的切换结果：新安装器注册登录任务失败并已回滚，备份完整保留）：部署入口报告失败，下一步带上切换脚本
    # 给的那句，再说明经部署入口怎么接着切换或回退。
    $deployCase = New-CountdownDeployTestCase -Name 'layout-switch-failed'
    $deployCase.Responses['write1'] = {
        param([scriptblock] $RunOnOffice)
        $switchFailure = [ordered]@{
            schemaVersion = 1
            mode = 'switch'
            succeeded = $false
            failedStage = 'install'
            reason = 'register-login-task'
            error = '注册登录任务被拒绝。'
            backup = [ordered]@{ action = 'created'; complete = $true }
            rollback = [ordered]@{ outcome = 'rolled-back' }
            nextStep = '切换没有走完，切换前的备份完整保留：排除原因后再次运行切换。'
        }
        $envelope = [ordered]@{
            packageVerified = $true
            installerStarted = $true
            installer = $switchFailure | ConvertTo-Json -Depth 4 -EscapeHandling EscapeNonAscii
            stagingRemoved = $true
            error = $null
        }
        [pscustomobject]@{ ExitCode = 0; Output = ($envelope | ConvertTo-Json -Compress -EscapeHandling EscapeNonAscii); Host = $deployHostSentinel; User = $deployAccountSentinel }
    }
    $deploySwitchFailed = Invoke-CountdownDeployTest -Label 'layout switch fails mid-way' -LayoutSwitch
    Assert-Equal 'failed|install|install-failed|rolled-back|remote-read,transfer,remote-write' "$($deploySwitchFailed.Result.outcome)|$($deploySwitchFailed.Result.failedStep)|$($deploySwitchFailed.Result.reason)|$($deploySwitchFailed.Result.rollback)|$($deployCase.Calls -join ',')" "A switch that fails mid-way is reported as failed: $($deploySwitchFailed.Result.error)"
    Assert-Equal $true ([string] $deploySwitchFailed.Result.nextStep).Contains('切换没有走完，切换前的备份完整保留：排除原因后再次运行切换。') "The next step carries the switch's own next step: $($deploySwitchFailed.Result.nextStep)"
    Assert-Equal $true (([string] $deploySwitchFailed.Result.nextStep).Contains('-LayoutSwitch') -and ([string] $deploySwitchFailed.Result.nextStep).Contains('-LayoutRollback')) "The next step says how to resume or roll back through the deploy entry: $($deploySwitchFailed.Result.nextStep)"

    # 生产 port 接主机入口：入口换成临时目录里的假脚本（形参、写操作断言与摘要格式同主机入口），不碰真实入口、不连任何设备。
    # 入口的杂项输出与摘要里的主机名不外漏，port 只回已知字段。
    Import-Module (Join-Path $PSScriptRoot 'Countdown.Deployer.psm1') -Force
    $deployEntryRoot = Join-Path $deployTestRoot 'host-entries'
    New-Item -ItemType Directory -Path $deployEntryRoot -Force | Out-Null
    $deployEntryStepCalls = Join-Path $deployEntryRoot 'step-calls.jsonl'
    $deployEntryTransferCalls = Join-Path $deployEntryRoot 'transfer-calls.jsonl'
    $deployEntryStep = Join-Path $deployEntryRoot 'Invoke-RemoteStep.ps1'
    $deployEntryTransfer = Join-Path $deployEntryRoot 'Invoke-OfficeScpTransfer.ps1'
    $deployEntryCallsLiteral = "'" + $deployEntryStepCalls.Replace("'", "''") + "'"
    [System.IO.File]::WriteAllText($deployEntryStep, @"
param([string] `$Target, [string] `$ScriptPath, [string] `$OutputPath, [string] `$StdinScript, [int] `$TimeoutSeconds = 300, [switch] `$WriteOperation, [switch] `$IncludeOutput, [int] `$MaxOutputChars = 65536)
`$tokens = `$null
`$errors = `$null
`$ast = [System.Management.Automation.Language.Parser]::ParseInput([System.IO.File]::ReadAllText(`$ScriptPath), [ref] `$tokens, [ref] `$errors)
`$first = @(`$ast.EndBlock.Statements | Select-Object -First 1)
`$refused = `$WriteOperation -and (`$first.Count -ne 1 -or -not `$first[0].Extent.Text.StartsWith("if (```$env:CODEX_REMOTE_EXECUTION_TARGET -ne '`$Target')"))
Add-Content -LiteralPath $deployEntryCallsLiteral -Value (@{ target = `$Target; write = [bool] `$WriteOperation; includeOutput = [bool] `$IncludeOutput; refused = `$refused } | ConvertTo-Json -Compress)
Write-Host 'ssh $deployAccountSentinel@$deployHostSentinel'
Write-Warning 'connected to $deployHostSentinel'
if (`$refused) { [ordered]@{ status = 'refused'; target = `$Target; exitCode = 2; helperExitCode = 2; outputPath = ''; reason = 'remote-context-assertion-missing'; output = `$null; outputTruncated = `$false } | ConvertTo-Json -Compress; exit 1 }
[ordered]@{ status = 'ok'; target = `$Target; exitCode = 0; helperExitCode = 0; outputPath = 'out.txt'; output = '{"probe":"ok"}'; outputTruncated = `$false } | ConvertTo-Json -Compress
"@, $deployUtf8)
    [System.IO.File]::WriteAllText($deployEntryTransfer, @"
param([Parameter(Mandatory)] [string] `$SourcePath, [Parameter(Mandatory)] [string] `$RemotePath, [Parameter(Mandatory)] [string] `$AttemptId, [Parameter(Mandatory)] [string] `$ExpectedHash, [Parameter(Mandatory)] [string] `$LedgerPath)
Add-Content -LiteralPath '$($deployEntryTransferCalls.Replace("'", "''"))' -Value (@{ remotePath = `$RemotePath; expectedHash = `$ExpectedHash; attemptId = `$AttemptId } | ConvertTo-Json -Compress)
Write-Host 'scp to $deployHostSentinel'
[System.IO.File]::WriteAllText(`$LedgerPath, '{"attempts":[{"id":"' + `$AttemptId + '"}]}')
[pscustomobject]@{ status = 'succeeded'; attemptId = `$AttemptId; bytes = 5; elapsedSeconds = 0.1; ledger = `$LedgerPath; sha256 = `$ExpectedHash; hostname = '$deployHostSentinel' } | ConvertTo-Json -Compress
"@, $deployUtf8)
    $deployEntryLedgerRoot = Join-Path $deployEntryRoot 'ledgers'
    $deployProductionPorts = New-CountdownDeployerHostPorts -RemoteStepEntry $deployEntryStep -TransferEntry $deployEntryTransfer -LedgerRoot $deployEntryLedgerRoot
    $deployEntryScript = Join-Path $deployEntryRoot 'read.ps1'
    [System.IO.File]::WriteAllText($deployEntryScript, "'{}'", $deployUtf8)
    $deployEntryRead = @(& $deployProductionPorts.InvokeRemoteScript $deployEntryScript $false *>&1)
    Assert-Equal 1 $deployEntryRead.Count 'The host remote port returns one result and lets none of the entry output through'
    Assert-Equal '0|{"probe":"ok"}' ("$($deployEntryRead[0].ExitCode)|$($deployEntryRead[0].Output)") 'The host remote port returns the exit code and the remote output'
    $deployEntryRefused = @(& $deployProductionPorts.InvokeRemoteScript $deployEntryScript $true *>&1)
    Assert-Equal $true ([int] $deployEntryRefused[0].ExitCode -ne 0) 'The host remote port reports a write payload the entry refused as failed'
    $deployEntryPackage = Join-Path $deployEntryRoot 'package.zip'
    [System.IO.File]::WriteAllText($deployEntryPackage, 'bytes', $deployUtf8)
    $deployEntryPackageSha256 = (Get-FileHash -LiteralPath $deployEntryPackage -Algorithm SHA256).Hash
    $deployEntryStaging = Join-Path $deployEntryRoot 'office-deploy\clock-countdown-run-1'
    $deployEntrySent = @(& $deployProductionPorts.SendFile $deployEntryPackage (Join-Path $deployEntryStaging 'package.zip') $deployEntryPackageSha256 'run-1' *>&1)
    Assert-Equal 1 $deployEntrySent.Count 'The host transfer port returns one result and lets none of the entry output through'
    Assert-Equal "succeeded|run-1|$deployEntryPackageSha256" ("$($deployEntrySent[0].Status)|$($deployEntrySent[0].AttemptId)|$(([string] $deployEntrySent[0].Sha256).ToUpperInvariant())") 'The host transfer port returns the status, attempt and remote hash'
    Assert-Equal $true ([string] $deployEntrySent[0].LedgerPath).StartsWith($deployEntryLedgerRoot + '\') 'The host transfer port keeps the ledger under its ledger root'
    Assert-Equal $true (Test-Path -LiteralPath ([string] $deployEntrySent[0].LedgerPath) -PathType Leaf) 'The host transfer port returns the ledger the entry wrote'
    Assert-CountdownDeployNoLeak -Text ($deployEntrySent[0] | ConvertTo-Json -Compress) -Name 'Host transfer port result'
    $deployEntryStepLog = @([System.IO.File]::ReadAllLines($deployEntryStepCalls) | ForEach-Object { $_ | ConvertFrom-Json })
    Assert-Equal 'office:False:True:False,office:True:True:True,office:True:True:False' (@($deployEntryStepLog | ForEach-Object { "$($_.target):$($_.write):$($_.includeOutput):$($_.refused)" }) -join ',') 'The host ports run reads and writes on the office with output, and create the staging directory with an accepted write payload'
    $deployEntryTransferLog = [System.IO.File]::ReadAllText($deployEntryTransferCalls) | ConvertFrom-Json
    Assert-Equal "$($deployEntryStaging.Replace('\', '/'))/package.zip|$($deployEntryPackageSha256.ToLowerInvariant())" "$($deployEntryTransferLog.remotePath)|$($deployEntryTransferLog.expectedHash)" 'The host transfer port passes a forward-slash remote path and a lower-case hash'

    # 暂存目录（#259 评审）：在本机按办公室的样子运行建暂存目录的远端脚本（目标指向本测试的临时目录）。目录不继承上级、只有
    # SYSTEM 与 Administrators 完全控制，远端步骤没提权时另给运行者自己完全控制（传输才写得进去）；目录已在就拒绝。
    $deployEntryCreate = Join-Path $deployEntryLedgerRoot 'run-1\create-staging.ps1'
    $deployEntryPreviousTarget = $env:CODEX_REMOTE_EXECUTION_TARGET
    $env:CODEX_REMOTE_EXECUTION_TARGET = 'office'
    try {
        $deployEntryCreated = (@(& $deployEntryCreate) | ForEach-Object { [string] $_ }) -join ''
        $deployEntryCreatedAgain = try { [void] (& $deployEntryCreate); 'created' } catch { 'refused' }
    }
    finally { $env:CODEX_REMOTE_EXECUTION_TARGET = $deployEntryPreviousTarget }
    $deployEntryIdentity = [System.Security.Principal.WindowsIdentity]::GetCurrent()
    $deployEntryElevated = ([System.Security.Principal.WindowsPrincipal] $deployEntryIdentity).IsInRole([System.Security.Principal.WindowsBuiltInRole]::Administrator)
    $deployEntryExpectedRules = @(@('S-1-5-18', 'S-1-5-32-544') + @(if (-not $deployEntryElevated) { $deployEntryIdentity.User.Value }) |
            ForEach-Object { "$_=FullControl/ContainerInherit, ObjectInherit/False" } | Sort-Object)
    $deployEntryAcl = Get-Acl -LiteralPath $deployEntryStaging
    $deployEntryRules = @($deployEntryAcl.GetAccessRules($true, $true, [System.Security.Principal.SecurityIdentifier]) |
            ForEach-Object { "$($_.IdentityReference.Value)=$($_.FileSystemRights)/$($_.InheritanceFlags)/$($_.IsInherited)" } | Sort-Object)
    Assert-Equal "{`"created`":true}|True|$($deployEntryExpectedRules -join ',')" "$deployEntryCreated|$($deployEntryAcl.AreAccessRulesProtected)|$($deployEntryRules -join ',')" 'The staging directory script creates a directory that inherits nothing and only SYSTEM, Administrators and an unelevated runner may write'
    Assert-Equal 'refused' $deployEntryCreatedAgain 'The staging directory script refuses a directory that already exists'
}
finally {
    # Windows 上 git init 把 .git 设为隐藏，入口的清理函数不枚举隐藏项，所以先去掉隐藏属性。
    $deployGitDirectory = Join-Path $deployTestRoot 'repo\.git'
    if (Test-Path -LiteralPath $deployGitDirectory -PathType Container) {
        $deployGitDirectoryItem = Get-Item -LiteralPath $deployGitDirectory -Force
        $deployGitDirectoryItem.Attributes = $deployGitDirectoryItem.Attributes -band -bnot [System.IO.FileAttributes]::Hidden
    }
    Remove-CountdownIsolatedTestTree -Path $deployTestRoot -AllowedRoot ([System.IO.Path]::GetTempPath())
}
