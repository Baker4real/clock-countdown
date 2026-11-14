# Countdown.Core.Tests.ps1 的 DeployEntry 分段：入口按开关点源执行，共用入口的参数、断言函数与已建立的状态，不单独运行。

# #285 新部署入口的成功路径：主机的远端执行入口与传输入口换成假 port，办公室是一个临时文件夹，不连接任何设备。
# 夹具是一个临时 Git 仓库：main 已推送到本地裸仓库，app\ 与 logs\ 被忽略；app\ 里是一份假的构建输出与载荷清单，
# logs\suite-runs\ 里有同一提交全部必过阶段通过、干净构建回执带聚合哈希的套件记录。假远端执行在本进程里运行部署端送来的
# 脚本，把假办公室的 Windows port（与安装器测试同形：任务启动时起一个假进程并写出运行状态回执）绑定到脚本的测试参数
# Port 与 LockName；假传输把包复制到暂存目录、核对哈希并写一份账本。断言结果 JSON、标准错误上的进度、假办公室看到的
# 调用顺序与安装根、本地部署记录，以及输出、进度和记录里没有主机名与账号。
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
$deployRepo = $null
$deployCase = $null
Import-Module (Join-Path $PSScriptRoot 'tools\Countdown.SuiteStages.psm1') -Force

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
    foreach ($name in @('Install-CountdownProgram.ps1', 'Countdown.Installer.psm1', 'Get-CountdownInstallStatus.ps1')) {
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

    $suiteRunId = '20261114T090000000-0a1b2c3d'
    $logDirectory = Join-Path $root 'logs\suite-runs'
    New-Item -ItemType Directory -Path $logDirectory -Force | Out-Null
    $run = [ordered]@{
        SchemaVersion = 1
        RunId = $suiteRunId
        RepositoryRoot = $root
        LogDirectory = $logDirectory
        Status = 'Completed'
        ExitCode = 0
        StartedAtUtc = '2026-11-14T09:00:00.0000000Z'
        FinishedAtUtc = '2026-11-14T09:25:00.0000000Z'
        Stages = @(foreach ($stage in @(Get-CountdownRequiredSuiteStage)) {
                [ordered]@{
                    Stage = $stage
                    ExitCode = 0
                    Status = 'Completed'
                    ElapsedSeconds = 60
                    BuildReceipt = if ($stage -eq 'Build') {
                        [ordered]@{ SchemaVersion = 1; Mode = 'CleanR2R'; Clean = $true; ReadyToRun = $true; ApplicationDllSha256 = $deployBuildFiles[1].Sha256; PayloadAggregateSha256 = $deployAggregate }
                    } else { $null }
                }
            })
        Source = [ordered]@{ Commit = $head; CleanAtStart = $true; CommitAtEnd = $head; CleanAtEnd = $true }
    }
    [System.IO.File]::WriteAllText((Join-Path $logDirectory "$suiteRunId-run.json"), ($run | ConvertTo-Json -Depth 6), $deployUtf8)
    [pscustomobject]@{ Root = $root; Head = $head; SuiteRunId = $suiteRunId }
}

# 一台假办公室（暂存目录与安装根）和这次部署的本地记录根。
function New-CountdownDeployTestCase {
    param([Parameter(Mandatory)] [string] $Name)

    $officeRoot = Join-Path $deployTestRoot "office-$Name"
    New-Item -ItemType Directory -Path $officeRoot -Force | Out-Null
    [pscustomobject]@{
        Name = $Name
        OfficeRoot = $officeRoot
        StagingRoot = Join-Path $officeRoot 'deploy'
        InstallRoot = Join-Path $officeRoot 'install'
        TaskName = "Minmin Countdown Deploy $Name"
        RecordRoot = Join-Path $deployTestRoot "records-$Name"
        LedgerRoot = Join-Path $deployTestRoot "ledgers-$Name"
        Sessions = @([pscustomobject]@{ SessionId = 3; User = "$deployHostSentinel\$deployAccountSentinel" })
        Processes = @()
        Tasks = @{}
        NextProcessId = 7001
        Calls = [System.Collections.Generic.List[string]]::new()
        ReadOnlyChanges = [System.Collections.Generic.List[string]]::new()
        MaxEncodedLength = 0
        BeforeRead = $null
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
        foreach ($process in @($deployCase.Processes | Where-Object { $_.ProcessId -eq $ProcessId -and $_.StartedByTask })) {
            $deployCase.Tasks[$process.StartedByTask].State = 'Ready'
        }
        $deployCase.Processes = @($deployCase.Processes | Where-Object { $_.ProcessId -ne $ProcessId })
    }
    GetTask = {
        param([string] $Name)
        if ($deployCase.Tasks.ContainsKey($Name)) { $deployCase.Tasks[$Name] }
    }
    RegisterTask = {
        param([string] $Name, [object] $Definition, [object] $Session)
        $deployCase.Tasks[$Name] = [pscustomobject]@{
            State = 'Ready'
            Enabled = [bool] $Definition.Enabled
            Actions = @($Definition.Actions | ForEach-Object { [pscustomobject]@{ Type = $_.Type; Execute = $_.Execute; Arguments = $_.Arguments } })
            Triggers = @($Definition.Triggers | ForEach-Object { [pscustomobject]@{ Type = $_.Type; Enabled = $_.Enabled } })
            LogonType = $Definition.LogonType
            RunLevel = $Definition.RunLevel
            Priority = $Definition.Priority
            ExecutionTimeLimit = $Definition.ExecutionTimeLimit
            MultipleInstances = $Definition.MultipleInstances
            UserId = $Session.User
        }
    }
    # 任务按动作启动 EXE：起一个假进程，像应用渲染首帧后那样在应用根（app\ 的上一级）写出运行状态回执。
    StartTask = {
        param([string] $Name)
        $task = $deployCase.Tasks[$Name]
        $task.State = 'Running'
        $exePath = ([string] $task.Actions[0].Execute).Trim('"')
        $processId = $deployCase.NextProcessId
        $deployCase.NextProcessId = $processId + 1
        $deployCase.Processes = @($deployCase.Processes) + @([pscustomobject]@{
                ProcessId = $processId
                Name = 'MinminCountdown.exe'
                ExecutablePath = $exePath
                SessionId = 3
                CreationDate = [DateTimeOffset]::Parse('2026-11-14T09:40:00Z').LocalDateTime
                StartedByTask = $Name
            })
        $receipt = [ordered]@{ schemaVersion = 4; stage = 'window-ready'; processId = $processId; sessionId = 3; readyAtUtc = '2026-11-14T09:40:02.0000000+00:00' }
        [System.IO.File]::WriteAllText((Join-Path (Split-Path -Parent (Split-Path -Parent $exePath)) 'runtime-status.json'), ($receipt | ConvertTo-Json), $deployUtf8)
    }
    MoveDirectory = {
        param([string] $Source, [string] $Destination)
        [System.IO.Directory]::Move($Source, $Destination)
    }
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
$deployHostPorts = [pscustomobject]@{
    InvokeRemoteScript = {
        param([string] $ScriptPath, [bool] $WriteOperation)
        $deployCase.Calls.Add($(if ($WriteOperation) { 'remote-write' } else { 'remote-read' }))
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
        if (-not $WriteOperation -and $null -ne $deployCase.BeforeRead) { & $deployCase.BeforeRead }
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
    SendFile = {
        param([string] $SourcePath, [string] $RemotePath, [string] $ExpectedSha256, [string] $AttemptId)
        $deployCase.Calls.Add('transfer')
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
        [switch] $StatusOnly
    )

    $errorWriter = [System.IO.StringWriter]::new()
    $standardError = [Console]::Error
    [Console]::SetError($errorWriter)
    try {
        $output = @(& $deployScript -StatusOnly:$StatusOnly -RepositoryRoot $deployRepo.Root -RecordRoot $deployCase.RecordRoot `
                -RemoteInstallRoot $deployCase.InstallRoot -TaskName $deployCase.TaskName -RemoteStagingRoot $deployCase.StagingRoot -Port $deployHostPorts *>&1)
    }
    finally { [Console]::SetError($standardError) }
    $text = @($output | ForEach-Object { [string] $_ })
    Assert-Equal 1 $text.Count "Deploy writes exactly one item to standard output: $Label"
    $json = $text -join "`n"
    [pscustomobject]@{
        Text = $json
        Stderr = $errorWriter.ToString()
        Result = try { $json | ConvertFrom-Json -DateKind String } catch { $null }
    }
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
    $leaks = @(@($deployHostSentinel, $deployAccountSentinel) | Where-Object { $Text.Contains($_) })
    Assert-Equal 0 $leaks.Count "$Name carries no host name or account"
}

try {
    New-Item -ItemType Directory -Path $deployTestRoot -Force | Out-Null
    $deployRepo = New-CountdownDeployTestRepository
    $deployAppTree = Get-CountdownDeployTestTree -Root (Join-Path $deployRepo.Root 'app')

    # 曳光弹：把已推送的 main 部署到一台还没装过的办公室。
    $deployCase = New-CountdownDeployTestCase -Name 'first'
    $deployed = Invoke-CountdownDeployTest -Label 'first deploy'
    $result = $deployed.Result
    Assert-Equal 'deploy' ([string] $result.mode) 'Deploy reports the deploy mode'
    Assert-Equal $true ([bool] $result.succeeded) "Deploy succeeds: $($result.failedStep) $($result.error)"
    Assert-Equal $true ($null -eq $result.failedStep) 'A successful deploy names no failed step'
    Assert-Equal $deployRepo.Head ([string] $result.commit) 'Deploy reports the pushed main commit it deployed'
    Assert-Equal $deployRepo.SuiteRunId ([string] $result.suiteRun.runId) 'Deploy names the suite run that tested the shipped build output'
    Assert-Equal $deployAggregate ([string] $result.buildOutput.payloadAggregateSha256) 'Deploy reports the aggregate hash of the build output it shipped'

    # 各步骤按顺序发生：办公室依次看到只读探测、传输、写操作安装、只读状态读取；进度在标准错误上逐步报告。
    Assert-Equal 'remote-read,transfer,remote-write,remote-read' ($deployCase.Calls -join ',') 'The office sees the read-only probe, the transfer, the install as a write and then a read-only status read'
    Assert-Equal 0 $deployCase.ReadOnlyChanges.Count 'Read-only remote calls change nothing on the office'
    Assert-Equal $true ($deployCase.MaxEncodedLength -gt 0 -and $deployCase.MaxEncodedLength -le 29000) "Every remote script fits the remote entry's encoded command limit: $($deployCase.MaxEncodedLength)"
    $progressSteps = @([regex]::Matches($deployed.Stderr, '(?m)^\[deploy\] \d\d:\d\d:\d\d \d+/\d+ (?<step>[a-z]+) ') | ForEach-Object { $_.Groups['step'].Value })
    Assert-Equal 'preflight,package,probe,transfer,install,status,record' ($progressSteps -join ',') 'Deploy reports each step on standard error in order'

    # 打包内容来自套件测过的构建输出：办公室程序目录里的文件与载荷清单和本机构建输出逐字节相同；
    # 本机构建输出与工作树都没被动过，没有重新导出或重新构建。
    foreach ($file in $deployBuildFiles) {
        Assert-Equal $file.Sha256 (Get-CountdownDeployTestFileHash -Path (Join-Path $deployCase.InstallRoot $file.Path)) "The office runs the suite-tested build output file: $($file.Path)"
    }
    Assert-Equal (Get-CountdownDeployTestFileHash -Path (Join-Path $deployRepo.Root 'app\published-payload.json')) (Get-CountdownDeployTestFileHash -Path (Join-Path $deployCase.InstallRoot 'app\published-payload.json')) 'The office keeps the payload manifest the suite-tested build wrote'
    Assert-Equal $deployAppTree (Get-CountdownDeployTestTree -Root (Join-Path $deployRepo.Root 'app')) 'Deploy leaves the local build output untouched: nothing re-exported or rebuilt'
    Assert-Equal '' (@(Invoke-CountdownDeployTestGit -Root $deployRepo.Root -Arguments @('status', '--porcelain=v1', '--untracked-files=all')) -join ',') 'Deploy leaves the work tree clean'

    # 成功判定来自安装后的状态读取：提交、聚合哈希、EXE 哈希与运行中的进程。
    Assert-Equal $true ([bool] $result.install.succeeded) 'The package installer reports success'
    Assert-Equal 7001 ([int] $result.install.readyProcess.processId) 'The install result names the process that reached startup readiness'
    Assert-Equal $deployRepo.Head ([string] $result.status.commit) 'The status read after the install reports the deployed commit'
    Assert-Equal $deployAggregate ([string] $result.status.aggregateSha256) 'The status read after the install reports the shipped aggregate hash'
    Assert-Equal $deployBuildFiles[2].Sha256 ([string] $result.status.exe.sha256) 'The status read after the install reports the shipped EXE hash'
    Assert-Equal $true ([bool] $result.status.exe.matchesManifest) 'The status read after the install reports the EXE matching the manifest'
    Assert-Equal $true ([bool] $result.status.running) 'The status read after the install reports the application running'
    Assert-Equal '7001' (@($result.status.processes | ForEach-Object { [int] $_.processId }) -join ',') 'The status read lists the process that reached startup readiness'
    Assert-Equal $false ([bool] $result.status.transactionMarkerPresent) 'The status read reports no unfinished install'
    Assert-Equal 0 @($result.statusMismatches).Count 'The status read matches every expectation'

    # 包和暂存内容不长期保留。
    Assert-Equal $true ([string] $result.package.sha256 -match '^[0-9A-F]{64}$') 'Deploy reports the package SHA-256'
    Assert-Equal ([string] $result.package.sha256) ([string] $result.transfer.remoteSha256) 'The transfer verified the package hash on the office'
    Assert-Equal $false (Test-Path -LiteralPath ([string] $result.remoteStagingDirectory)) 'The office staging directory is removed after the install'
    Assert-Equal $true ([bool] $result.install.stagingRemoved) 'The remote install reports the staging directory removed'
    Assert-Equal $false ([bool] $result.package.retained) 'Deploy reports the local package not retained'
    Assert-Equal 0 @(Get-ChildItem -LiteralPath $deployCase.RecordRoot -Recurse -Force -File | Where-Object { $_.Extension -eq '.zip' }).Count 'No package is kept under the local record root'

    # 本地部署记录：提交、包哈希、安装结果、状态快照与传输账本引用。
    $recordText = [System.IO.File]::ReadAllText([string] $result.recordPath)
    $record = $recordText | ConvertFrom-Json -DateKind String
    Assert-Equal $deployRepo.Head ([string] $record.commit) 'The deployment record names the commit'
    Assert-Equal ([string] $result.package.sha256) ([string] $record.package.sha256) 'The deployment record names the package hash'
    Assert-Equal $true ([bool] $record.install.succeeded) 'The deployment record carries the install result'
    Assert-Equal $deployRepo.Head ([string] $record.status.commit) 'The deployment record carries the status snapshot'
    Assert-Equal $true ([bool] $record.succeeded) 'The deployment record carries the verdict'
    Assert-Equal $true (Test-Path -LiteralPath ([string] $record.transfer.ledgerPath) -PathType Leaf) 'The deployment record references the transfer ledger'
    $ledger = [System.IO.File]::ReadAllText([string] $record.transfer.ledgerPath) | ConvertFrom-Json
    Assert-Equal ([string] $record.transfer.attemptId) ([string] $ledger.attempts[0].id) 'The ledger reference names the transfer attempt'

    # 标准输出、进度与记录都不含主机名或账号。
    Assert-CountdownDeployNoLeak -Text $deployed.Text -Name 'Deploy standard output'
    Assert-CountdownDeployNoLeak -Text $deployed.Stderr -Name 'Deploy progress'
    Assert-CountdownDeployNoLeak -Text $recordText -Name 'Deployment record'

    # 只查状态：一次只读的状态读取，办公室与本地记录都不变。
    $officeBefore = Get-CountdownDeployTestOfficeState
    $recordsBefore = Get-CountdownDeployTestTree -Root $deployCase.RecordRoot
    $deployCase.Calls.Clear()
    $statusOnly = Invoke-CountdownDeployTest -Label 'status only' -StatusOnly
    Assert-Equal 'status' ([string] $statusOnly.Result.mode) 'Status-only mode reports the status mode'
    Assert-Equal $true ([bool] $statusOnly.Result.succeeded) "Status-only mode reads the status: $($statusOnly.Result.error)"
    Assert-Equal 'remote-read' ($deployCase.Calls -join ',') 'Status-only mode makes one read-only remote call and no transfer'
    Assert-Equal $officeBefore (Get-CountdownDeployTestOfficeState) 'Status-only mode changes nothing on the office'
    Assert-Equal $recordsBefore (Get-CountdownDeployTestTree -Root $deployCase.RecordRoot) 'Status-only mode writes nothing locally'
    Assert-Equal $deployRepo.Head ([string] $statusOnly.Result.status.commit) 'Status-only mode reports the installed commit'
    Assert-Equal $true ([bool] $statusOnly.Result.status.running) 'Status-only mode reports the application running'
    Assert-CountdownDeployNoLeak -Text $statusOnly.Text -Name 'Status-only standard output'
    Assert-CountdownDeployNoLeak -Text $statusOnly.Stderr -Name 'Status-only progress'

    # 成功只看安装后的状态读取：安装器报告成功，但读状态时应用已经退出，部署不报告成功。
    $deployCase = New-CountdownDeployTestCase -Name 'exited'
    $deployCase.BeforeRead = { if ($deployCase.Calls.Contains('remote-write')) { $deployCase.Processes = @() } }
    $exited = Invoke-CountdownDeployTest -Label 'application exited before the status read'
    Assert-Equal $true ([bool] $exited.Result.install.succeeded) 'The installer reports success before the application exits'
    Assert-Equal $false ([bool] $exited.Result.succeeded) 'Deploy does not report success when the status read finds the application not running'
    Assert-Equal 'status' ([string] $exited.Result.failedStep) 'Deploy names the status read as the failed step'
    Assert-Equal 'process' (@($exited.Result.statusMismatches) -join ',') 'Deploy names the missing process as the status mismatch'

    # 生产 port 接主机入口：入口换成临时目录里的假脚本（形参、写操作断言与摘要格式同主机入口），不碰真实入口、不连任何设备。
    # 入口的杂项输出与摘要里的主机名不外漏，port 只回已知字段。
    Import-Module (Join-Path $PSScriptRoot 'Countdown.Deployer.psm1') -Force
    $hostEntryRoot = Join-Path $deployTestRoot 'host-entries'
    New-Item -ItemType Directory -Path $hostEntryRoot -Force | Out-Null
    $hostStepCalls = Join-Path $hostEntryRoot 'step-calls.jsonl'
    $hostTransferCalls = Join-Path $hostEntryRoot 'transfer-calls.jsonl'
    $hostStepEntry = Join-Path $hostEntryRoot 'Invoke-RemoteStep.ps1'
    $hostTransferEntry = Join-Path $hostEntryRoot 'Invoke-OfficeScpTransfer.ps1'
    $hostCallsLiteral = "'" + $hostStepCalls.Replace("'", "''") + "'"
    [System.IO.File]::WriteAllText($hostStepEntry, @"
param([string] `$Target, [string] `$ScriptPath, [string] `$OutputPath, [string] `$StdinScript, [int] `$TimeoutSeconds = 300, [switch] `$WriteOperation, [switch] `$IncludeOutput, [int] `$MaxOutputChars = 65536)
`$tokens = `$null
`$errors = `$null
`$ast = [System.Management.Automation.Language.Parser]::ParseInput([System.IO.File]::ReadAllText(`$ScriptPath), [ref] `$tokens, [ref] `$errors)
`$first = @(`$ast.EndBlock.Statements | Select-Object -First 1)
`$refused = `$WriteOperation -and (`$first.Count -ne 1 -or -not `$first[0].Extent.Text.StartsWith("if (```$env:CODEX_REMOTE_EXECUTION_TARGET -ne '`$Target')"))
Add-Content -LiteralPath $hostCallsLiteral -Value (@{ target = `$Target; write = [bool] `$WriteOperation; includeOutput = [bool] `$IncludeOutput; refused = `$refused } | ConvertTo-Json -Compress)
Write-Host 'ssh $deployAccountSentinel@$deployHostSentinel'
Write-Warning 'connected to $deployHostSentinel'
if (`$refused) { [ordered]@{ status = 'refused'; target = `$Target; exitCode = 2; helperExitCode = 2; outputPath = ''; reason = 'remote-context-assertion-missing'; output = `$null; outputTruncated = `$false } | ConvertTo-Json -Compress; exit 1 }
[ordered]@{ status = 'ok'; target = `$Target; exitCode = 0; helperExitCode = 0; outputPath = 'out.txt'; output = '{"probe":"ok"}'; outputTruncated = `$false } | ConvertTo-Json -Compress
"@, $deployUtf8)
    [System.IO.File]::WriteAllText($hostTransferEntry, @"
param([Parameter(Mandatory)] [string] `$SourcePath, [Parameter(Mandatory)] [string] `$RemotePath, [Parameter(Mandatory)] [string] `$AttemptId, [Parameter(Mandatory)] [string] `$ExpectedHash, [Parameter(Mandatory)] [string] `$LedgerPath)
Add-Content -LiteralPath '$($hostTransferCalls.Replace("'", "''"))' -Value (@{ remotePath = `$RemotePath; expectedHash = `$ExpectedHash; attemptId = `$AttemptId } | ConvertTo-Json -Compress)
Write-Host 'scp to $deployHostSentinel'
[System.IO.File]::WriteAllText(`$LedgerPath, '{"attempts":[{"id":"' + `$AttemptId + '"}]}')
[pscustomobject]@{ status = 'succeeded'; attemptId = `$AttemptId; bytes = 5; elapsedSeconds = 0.1; ledger = `$LedgerPath; sha256 = `$ExpectedHash; hostname = '$deployHostSentinel' } | ConvertTo-Json -Compress
"@, $deployUtf8)
    $hostLedgerRoot = Join-Path $hostEntryRoot 'ledgers'
    $hostPorts = New-CountdownDeployerHostPorts -RemoteStepEntry $hostStepEntry -TransferEntry $hostTransferEntry -LedgerRoot $hostLedgerRoot
    $hostScript = Join-Path $hostEntryRoot 'read.ps1'
    [System.IO.File]::WriteAllText($hostScript, "'{}'", $deployUtf8)
    $hostRead = @(& $hostPorts.InvokeRemoteScript $hostScript $false *>&1)
    Assert-Equal 1 $hostRead.Count 'The host remote port returns one result and lets none of the entry output through'
    Assert-Equal '0|{"probe":"ok"}' ("$($hostRead[0].ExitCode)|$($hostRead[0].Output)") 'The host remote port returns the exit code and the remote output'
    $hostRefused = @(& $hostPorts.InvokeRemoteScript $hostScript $true *>&1)
    Assert-Equal $true ([int] $hostRefused[0].ExitCode -ne 0) 'The host remote port reports a write payload the entry refused as failed'
    $hostPackage = Join-Path $hostEntryRoot 'package.zip'
    [System.IO.File]::WriteAllText($hostPackage, 'bytes', $deployUtf8)
    $hostPackageSha256 = (Get-FileHash -LiteralPath $hostPackage -Algorithm SHA256).Hash
    $hostSent = @(& $hostPorts.SendFile $hostPackage 'D:\deploy-staging\clock-countdown-run-1\package.zip' $hostPackageSha256 'run-1' *>&1)
    Assert-Equal 1 $hostSent.Count 'The host transfer port returns one result and lets none of the entry output through'
    Assert-Equal "succeeded|run-1|$hostPackageSha256" ("$($hostSent[0].Status)|$($hostSent[0].AttemptId)|$(([string] $hostSent[0].Sha256).ToUpperInvariant())") 'The host transfer port returns the status, attempt and remote hash'
    Assert-Equal $true ([string] $hostSent[0].LedgerPath).StartsWith($hostLedgerRoot + '\') 'The host transfer port keeps the ledger under its ledger root'
    Assert-Equal $true (Test-Path -LiteralPath ([string] $hostSent[0].LedgerPath) -PathType Leaf) 'The host transfer port returns the ledger the entry wrote'
    Assert-CountdownDeployNoLeak -Text ($hostSent[0] | ConvertTo-Json -Compress) -Name 'Host transfer port result'
    $hostStepLog = @([System.IO.File]::ReadAllLines($hostStepCalls) | ForEach-Object { $_ | ConvertFrom-Json })
    Assert-Equal 'office:False:True:False,office:True:True:True,office:True:True:False' (@($hostStepLog | ForEach-Object { "$($_.target):$($_.write):$($_.includeOutput):$($_.refused)" }) -join ',') 'The host ports run reads and writes on the office with output, and create the staging directory with an accepted write payload'
    $hostTransferLog = [System.IO.File]::ReadAllText($hostTransferCalls) | ConvertFrom-Json
    Assert-Equal "D:/deploy-staging/clock-countdown-run-1/package.zip|$($hostPackageSha256.ToLowerInvariant())" "$($hostTransferLog.remotePath)|$($hostTransferLog.expectedHash)" 'The host transfer port passes a forward-slash remote path and a lower-case hash'
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
