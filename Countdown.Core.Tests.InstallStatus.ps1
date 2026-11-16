# Countdown.Core.Tests.ps1 的 InstallStatus 分段：入口按开关点源执行，共用入口的参数、断言函数与已建立的状态，不单独运行。

# #276 只读状态命令：在仓库根下的临时安装根上运行 Get-CountdownInstallStatus.ps1，进程与计划任务查询经测试 port 注入，
# 断言输出 JSON 的字段值、每次读取后安装根逐项不变、输出不含设置内容与账号；再按远端执行入口的包装方式在子进程里用真实
# Windows 查询跑一次。最后核对构建在载荷清单里记录的提交标识。
$statusScript = Join-Path $PSScriptRoot 'Get-CountdownInstallStatus.ps1'
$statusTestRoot = Join-Path $PSScriptRoot ('install-status-test-' + [guid]::NewGuid().ToString('N'))
$statusGitFixtureRoot = Join-Path ([System.IO.Path]::GetTempPath()) ('clock-countdown-install-status-test-' + [guid]::NewGuid().ToString('N'))
$statusUtf8 = [System.Text.UTF8Encoding]::new($false)
# 期望值是独立算好的字面量：SHA-256('status exe v1')、SHA-256('status exe tampered')、SHA-256(设置夹具)。
$statusExeSha256 = 'BC3378EBFBF2C5A7EDF92163E6DCA58B2FBD06032E1F47BD5B6CC16B68ADF36A'
$statusTamperedExeSha256 = '9D00DC5A8B26DBBB59AA99CDB406BCB74E4462761BA0A2C904F7F14E69506CF8'
$statusSettingsText = '{"schemaVersion":10,"note":"settings-content-sentinel-276"}'
$statusSettingsSha256 = 'F1DC9C8BF0DCCFF4C088D16AE0EA902131E8DB0490154C9B15B7665C89E2745D'
$statusCommit = '5e1a7b2c9d3f4e6a8b0c1d2e3f405162738495a6'
$statusAggregate = '0F1E2D3C4B5A69788796A5B4C3D2E1F00F1E2D3C4B5A69788796A5B4C3D2E1F0'
$statusSessionSid = 'S-1-5-21-2760000000-2760000001-2760000002-1001'
$statusOtherSid = 'S-1-5-21-2760000000-2760000001-2760000002-2002'
# 设置正文、账号、SID 和进程命令行只出现在夹具里；任何一个出现在输出里都算泄漏。
$statusPrivacySentinels = @('settings-content-sentinel-276', 'status-account-sentinel', 'status-commandline-sentinel', 'MINMIN-OFFICE', $statusSessionSid, $statusOtherSid)
# 每次运行用自己的安装锁名，不读本机真实安装的全机锁。
$statusLockName = 'Local\MinminCountdownStatusTest-' + [guid]::NewGuid().ToString('N')
$statusCase = $null

# 测试 port 只读当前 $statusCase：状态脚本回调时按动态作用域解析到这里的变量。
$statusPort = [pscustomobject]@{
    GetProcesses = {
        param([string] $Name)
        if ($Name -eq 'MinminCountdown.exe') { $statusCase.Processes }
    }
    GetTask = {
        param([string] $Name)
        if ($Name -eq $statusCase.TaskName) { $statusCase.Task }
    }
    GetInteractiveSessions = { $statusCase.Sessions }
}

function New-CountdownStatusTestProcess {
    param([int] $ProcessId, [AllowNull()] [string] $ExecutablePath)
    [pscustomobject]@{
        ProcessId = $ProcessId
        Name = 'MinminCountdown.exe'
        ExecutablePath = $ExecutablePath
        CommandLine = "`"$ExecutablePath`" status-commandline-sentinel"
        SessionId = 3
        CreationDate = [DateTimeOffset]::Parse('2026-11-14T09:15:30Z').LocalDateTime
    }
}

function New-CountdownStatusTestCase {
    param([Parameter(Mandatory)] [string] $Name)

    $root = Join-Path $statusTestRoot $Name
    $exePath = Join-Path $root 'app\MinminCountdown.exe'
    New-Item -ItemType Directory -Path (Join-Path $root 'app') -Force | Out-Null
    [System.IO.File]::WriteAllText($exePath, 'status exe v1', $statusUtf8)
    $manifest = [ordered]@{
        schemaVersion = 1
        commit = $statusCommit
        aggregateSha256 = $statusAggregate
        files = @([ordered]@{ path = 'app\MinminCountdown.exe'; sha256 = $statusExeSha256 })
    }
    [System.IO.File]::WriteAllText((Join-Path $root 'app\published-payload.json'), ($manifest | ConvertTo-Json -Depth 4), $statusUtf8)
    $settingsPath = Join-Path $root 'settings.json'
    [System.IO.File]::WriteAllText($settingsPath, $statusSettingsText, $statusUtf8)
    [System.IO.File]::SetLastWriteTimeUtc($settingsPath, [datetime]::SpecifyKind([datetime] '2026-11-01 08:30:00', 'Utc'))
    [pscustomobject]@{
        Root = $root
        ExePath = $exePath
        TaskName = "Minmin Countdown Status $Name"
        Sessions = @([pscustomobject]@{ SessionId = 3; User = 'MINMIN-OFFICE\status-account-sentinel'; Sid = $statusSessionSid })
        # 第一项用测试进程自己的 PID：状态命令若结束列出的进程，整个测试进程会随之退出。另外两项是别处的同名
        # 程序和读不到映像路径的同名进程，都不属于这个安装。
        Processes = @(
            New-CountdownStatusTestProcess -ProcessId $PID -ExecutablePath $exePath
            New-CountdownStatusTestProcess -ProcessId 4243 -ExecutablePath 'D:\dev-checkout\app\MinminCountdown.exe'
            New-CountdownStatusTestProcess -ProcessId 4244 -ExecutablePath $null
        )
        Task = [pscustomobject]@{
            State = 'Running'
            Enabled = $true
            Actions = @([pscustomobject]@{ Type = 'Exec'; Execute = "`"$exePath`""; Arguments = $null; WorkingDirectory = $null })
            Triggers = @([pscustomobject]@{ Type = 'Logon'; Enabled = $true; UserSid = $statusSessionSid })
            UserSid = $statusSessionSid
            LogonType = 'Interactive'
            RunLevel = 'Limited'
            Priority = 4
            ExecutionTimeLimit = 'PT0S'
            MultipleInstances = 'IgnoreNew'
            DisallowStartIfOnBatteries = $false
            StopIfGoingOnBatteries = $false
            AllowDemandStart = $true
            AllowHardTerminate = $true
            UserId = 'MINMIN-OFFICE\status-account-sentinel'
        }
    }
}

function Get-CountdownStatusTestTreeState {
    param([Parameter(Mandatory)] [string] $Root)

    @(Get-ChildItem -LiteralPath $Root -Recurse -Force | Sort-Object FullName | ForEach-Object {
            $relative = [System.IO.Path]::GetRelativePath($Root, $_.FullName)
            if ($_.PSIsContainer) { "dir:$relative" }
            else { "file:$relative|$($_.Length)|$($_.LastWriteTimeUtc.Ticks)|$((Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash)" }
        }) -join "`n"
}

function Invoke-CountdownStatusTestRead {
    param([Parameter(Mandatory)] [string] $Label)

    $before = Get-CountdownStatusTestTreeState -Root $statusCase.Root
    $output = @(& $statusScript -InstallRoot $statusCase.Root -TaskName $statusCase.TaskName -Port $statusPort -LockName $statusLockName)
    Assert-Equal $before (Get-CountdownStatusTestTreeState -Root $statusCase.Root) "Status read leaves every file and directory in the install root unchanged: $Label"
    Assert-Equal 1 $output.Count "Status writes exactly one JSON document: $Label"
    $text = [string] $output[0]
    Assert-Equal '' (@($statusPrivacySentinels | Where-Object { $text.Contains($_) }) -join ',') "Status output carries no settings content, account or command line: $Label"
    $text | ConvertFrom-Json -DateKind String
}

try {
    # 曳光弹：应用在运行、EXE 与清单一致、任务定义符合期望、有设置文件、没有事务标记。
    $statusCase = New-CountdownStatusTestCase -Name 'running'
    $running = Invoke-CountdownStatusTestRead -Label 'running'
    Assert-Equal 1 ([int] $running.schemaVersion) 'Status reports schema version one'
    Assert-Equal $statusCase.Root ([string] $running.installRoot) 'Status names the install root it read'
    Assert-Equal 'present' ([string] $running.manifest) 'Status finds the payload manifest in the program directory'
    Assert-Equal $statusCommit ([string] $running.commit) 'Status reports the commit recorded in the payload manifest'
    Assert-Equal $statusAggregate ([string] $running.aggregateSha256) 'Status reports the aggregate hash recorded in the payload manifest'
    Assert-Equal $true ([bool] $running.exe.present) 'Status finds the installed EXE'
    Assert-Equal $statusExeSha256 ([string] $running.exe.sha256) 'Status reports the installed EXE SHA-256'
    Assert-Equal $true ([bool] $running.exe.matchesManifest) 'Status reports the installed EXE matches the manifest'
    Assert-Equal $true ([bool] $running.running) 'Status reports the application running'
    Assert-Equal 1 @($running.processes).Count 'Status lists only the process whose image is the installed EXE'
    Assert-Equal $PID ([int] $running.processes[0].processId) 'Status reports the process id'
    Assert-Equal $statusCase.ExePath ([string] $running.processes[0].path) 'Status reports the process image path'
    Assert-Equal 3 ([int] $running.processes[0].sessionId) 'Status reports the process session'
    Assert-Equal '2026-11-14T09:15:30.0000000Z' ([string] $running.processes[0].startTimeUtc) 'Status reports the process start time in UTC'
    Assert-Equal $statusCase.TaskName ([string] $running.loginTask.name) 'Status names the login task it read'
    Assert-Equal $true ([bool] $running.loginTask.present) 'Status finds the login task'
    Assert-Equal 'Running' ([string] $running.loginTask.state) 'Status reports the login task state'
    Assert-Equal $true ([bool] $running.loginTask.matchesExpected) 'Status reports the login task matches the expected definition'
    Assert-Equal 0 @($running.loginTask.mismatches).Count 'Status reports no login task mismatches'
    Assert-Equal $true ([bool] $running.settings.present) 'Status finds the settings file'
    Assert-Equal 59 ([long] $running.settings.length) 'Status reports the settings length'
    Assert-Equal '2026-11-01T08:30:00.0000000Z' ([string] $running.settings.lastWriteTimeUtc) 'Status reports the settings modification time in UTC'
    Assert-Equal $statusSettingsSha256 ([string] $running.settings.sha256) 'Status reports the settings SHA-256'
    Assert-Equal $false ([bool] $running.transactionMarkerPresent) 'Status reports no unfinished install transaction'
    Assert-Equal $false $running.installInProgress 'Status reports no install, switch or rollback in progress while nobody holds the install lock'

    # 另一个运行持有全机安装锁（安装、切换或回退正在进行）：状态命令只看锁在不在，读到有运行在进行；它不取锁、不等待。
    $statusLockHolder = Start-CountdownTestLockHolder -Name $statusLockName
    try { $statusInProgress = Invoke-CountdownStatusTestRead -Label 'install lock held' }
    finally { Stop-CountdownTestLockHolder -Holder $statusLockHolder }
    Assert-Equal $true $statusInProgress.installInProgress 'Status reports a run in progress while another process holds the install lock'
    Assert-Equal $false (Invoke-CountdownStatusTestRead -Label 'install lock released').installInProgress 'Status reports no run in progress after the install lock is released'

    # 全机安装锁的名字有三份：新安装器、旧安装器（Countdown.InstallTransaction.psm1）与自成一体的状态命令的默认值。
    # 三份相同，新旧安装器与切换才互斥，状态命令也才读得到正在进行的运行。
    $statusLockParameter = @([System.Management.Automation.Language.Parser]::ParseFile($statusScript, [ref] $null, [ref] $null).ParamBlock.Parameters |
            Where-Object { $_.Name.VariablePath.UserPath -eq 'LockName' })
    $statusDefaultLockName = if ($statusLockParameter.Count -eq 1 -and $null -ne $statusLockParameter[0].DefaultValue) { [string] $statusLockParameter[0].DefaultValue.SafeGetValue() }
    $installerLockNameValue = & (Import-Module (Join-Path $PSScriptRoot 'Countdown.Installer.psm1') -Force -PassThru) { $script:InstallLockName }
    $oldInstallerLockName = & (Get-Module Countdown.InstallTransaction) { $script:CountdownInstallMutexName }
    Assert-Equal "Global\MinminCountdownInstallTransaction-v1|$oldInstallerLockName|$oldInstallerLockName" "$oldInstallerLockName|$installerLockNameValue|$statusDefaultLockName" 'The new installer, the old installer and the status command name the same machine-wide install lock'

    # 应用未运行：只有别处的同名进程。
    $statusCase.Processes = @(New-CountdownStatusTestProcess -ProcessId 4243 -ExecutablePath 'D:\dev-checkout\app\MinminCountdown.exe')
    $stopped = Invoke-CountdownStatusTestRead -Label 'not running'
    Assert-Equal $false ([bool] $stopped.running) 'Status reports the application not running when no process runs the installed EXE'
    Assert-Equal 0 @($stopped.processes).Count 'Status lists no process when the application is not running'

    # 事务标记存在：上一次安装没有完成。
    [System.IO.File]::WriteAllText((Join-Path $statusCase.Root 'install-transaction.json'), '{}', $statusUtf8)
    $pending = Invoke-CountdownStatusTestRead -Label 'transaction marker'
    Assert-Equal $true ([bool] $pending.transactionMarkerPresent) 'Status reports the unfinished install transaction marker'

    # EXE 被改动：哈希与清单不一致。
    [System.IO.File]::WriteAllText($statusCase.ExePath, 'status exe tampered', $statusUtf8)
    $tampered = Invoke-CountdownStatusTestRead -Label 'tampered exe'
    Assert-Equal $statusTamperedExeSha256 ([string] $tampered.exe.sha256) 'Status reports the SHA-256 of the EXE actually installed'
    Assert-Equal $false ([bool] $tampered.exe.matchesManifest) 'Status reports an EXE that differs from the manifest'

    # 现有布局：载荷清单没记提交标识时报告未知，其余照常。
    $statusCase = New-CountdownStatusTestCase -Name 'manifest-without-commit'
    [System.IO.File]::WriteAllText(
        (Join-Path $statusCase.Root 'app\published-payload.json'),
        ([ordered]@{ schemaVersion = 1; aggregateSha256 = $statusAggregate; files = @([ordered]@{ path = 'app\MinminCountdown.exe'; sha256 = $statusExeSha256 }) } | ConvertTo-Json -Depth 4),
        $statusUtf8)
    $withoutCommit = Invoke-CountdownStatusTestRead -Label 'manifest without commit'
    Assert-Equal 'present' ([string] $withoutCommit.manifest) 'Status reads a manifest that predates the commit field'
    Assert-Equal $true ($null -eq $withoutCommit.commit) 'Status reports the commit as unknown when the manifest has none'
    Assert-Equal $statusAggregate ([string] $withoutCommit.aggregateSha256) 'Status still reports the aggregate hash of a manifest without commit'
    Assert-Equal $true ([bool] $withoutCommit.exe.matchesManifest) 'Status still checks the EXE against a manifest without commit'

    # 清单缺失（现有布局的安装器不复制清单）：提交、聚合哈希与 EXE 一致性都未知，EXE 哈希照常报告。
    Remove-Item -LiteralPath (Join-Path $statusCase.Root 'app\published-payload.json') -Force
    $withoutManifest = Invoke-CountdownStatusTestRead -Label 'manifest missing'
    Assert-Equal 'missing' ([string] $withoutManifest.manifest) 'Status reports the payload manifest missing'
    Assert-Equal $true ($null -eq $withoutManifest.commit) 'Status reports the commit as unknown without a manifest'
    Assert-Equal $true ($null -eq $withoutManifest.aggregateSha256) 'Status reports the aggregate hash as unknown without a manifest'
    Assert-Equal $true ($null -eq $withoutManifest.exe.matchesManifest) 'Status reports EXE consistency as unknown without a manifest'
    Assert-Equal $statusExeSha256 ([string] $withoutManifest.exe.sha256) 'Status reports the installed EXE SHA-256 without a manifest'

    # 清单损坏：同样未知，不报错。
    [System.IO.File]::WriteAllText((Join-Path $statusCase.Root 'app\published-payload.json'), '{ "schemaVersion": 1, "files": [', $statusUtf8)
    $invalidManifest = Invoke-CountdownStatusTestRead -Label 'manifest invalid'
    Assert-Equal 'invalid' ([string] $invalidManifest.manifest) 'Status reports an unreadable payload manifest as invalid'
    Assert-Equal $true ($null -eq $invalidManifest.commit -and $null -eq $invalidManifest.exe.matchesManifest) 'Status reports commit and EXE consistency as unknown for an invalid manifest'

    # 程序目录、设置都不在，登录任务也不存在。
    $statusCase = New-CountdownStatusTestCase -Name 'empty-root'
    Remove-Item -LiteralPath $statusCase.ExePath -Force
    Remove-Item -LiteralPath (Join-Path $statusCase.Root 'app\published-payload.json') -Force
    Remove-Item -LiteralPath (Join-Path $statusCase.Root 'settings.json') -Force
    $statusCase.Processes = @()
    $statusCase.TaskName = 'Minmin Countdown Status absent task'
    $statusCase.Task = $null
    $empty = Invoke-CountdownStatusTestRead -Label 'empty root'
    Assert-Equal $false ([bool] $empty.exe.present) 'Status reports the EXE missing'
    Assert-Equal $true ($null -eq $empty.exe.sha256) 'Status reports no EXE hash when the EXE is missing'
    Assert-Equal $false ([bool] $empty.settings.present) 'Status reports the settings file missing'
    Assert-Equal $true ($null -eq $empty.settings.length -and $null -eq $empty.settings.lastWriteTimeUtc -and $null -eq $empty.settings.sha256) 'Status reports no settings fingerprint when the file is missing'
    Assert-Equal $false ([bool] $empty.loginTask.present) 'Status reports the login task missing'
    Assert-Equal $false ([bool] $empty.loginTask.matchesExpected) 'A missing login task does not match the expected definition'
    Assert-Equal $true ($null -eq $empty.loginTask.state) 'Status reports no state for a missing login task'

    # 现有布局的登录任务：经 pwsh 运行 adapter、默认（低于正常）优先级。
    $statusCase = New-CountdownStatusTestCase -Name 'legacy-task'
    $statusCase.Task.State = 'Ready'
    $statusCase.Task.Actions = @([pscustomobject]@{ Type = 'Exec'; Execute = 'C:\Program Files\PowerShell\7\pwsh.exe'; Arguments = "-NoLogo -NoProfile -ExecutionPolicy RemoteSigned -WindowStyle Hidden -STA -File `"$(Join-Path $statusCase.Root '敏敏子倒计时.ps1')`"" })
    $statusCase.Task.Priority = 7
    $legacyTask = Invoke-CountdownStatusTestRead -Label 'legacy task'
    Assert-Equal $true ([bool] $legacyTask.loginTask.present) 'Status finds the existing-layout login task'
    Assert-Equal $false ([bool] $legacyTask.loginTask.matchesExpected) 'The existing-layout login task does not match the direct-start definition'
    Assert-Equal 'action,priority' (@($legacyTask.loginTask.mismatches) -join ',') 'Status names the action and the below-normal priority as the existing-layout mismatches'

    # 多项同时不符：带参数与固定工作目录的动作、开机触发、另一个主体、密码登录、提升、72 小时时限、允许并行、
    # 电池供电不启动且拔电即停、不能按需启动或强制结束、已禁用。
    $statusCase.Task = [pscustomobject]@{
        State = 'Disabled'
        Enabled = $false
        Actions = @([pscustomobject]@{ Type = 'Exec'; Execute = $statusCase.ExePath; Arguments = '--require-startup-request'; WorkingDirectory = $statusCase.Root })
        Triggers = @([pscustomobject]@{ Type = 'Boot'; Enabled = $true; UserSid = $statusOtherSid })
        UserSid = $statusOtherSid
        LogonType = 'Password'
        RunLevel = 'Highest'
        Priority = 4
        ExecutionTimeLimit = 'PT72H'
        MultipleInstances = 'Parallel'
        DisallowStartIfOnBatteries = $true
        StopIfGoingOnBatteries = $true
        AllowDemandStart = $false
        AllowHardTerminate = $false
        UserId = 'MINMIN-OFFICE\status-account-sentinel'
    }
    $driftedTask = Invoke-CountdownStatusTestRead -Label 'drifted task'
    Assert-Equal 'Disabled' ([string] $driftedTask.loginTask.state) 'Status reports the disabled task state'
    Assert-Equal 'action,trigger,principal,logonType,runLevel,executionTimeLimit,multipleInstances,disallowStartIfOnBatteries,stopIfGoingOnBatteries,allowDemandStart,allowHardTerminate,enabled' (@($driftedTask.loginTask.mismatches) -join ',') 'Status names every login task property that differs from the expected definition'

    # 与安装器的期望定义逐项一致（#259 评审）：每个字段单独偏离都不能被状态命令接受。安装器分段另在首次安装与任务更新后
    # 读回它真实注册的假任务，证明 matchesExpected=true 的正向路径也经过同一命令。
    $statusSingleFieldDrifts = @(
        [pscustomobject]@{ Name = 'priority-five'; Mismatch = 'priority'; Apply = { param($Task) $Task.Priority = 5 } }
        [pscustomobject]@{ Name = 'battery-start-refused'; Mismatch = 'disallowStartIfOnBatteries'; Apply = { param($Task) $Task.DisallowStartIfOnBatteries = $true } }
        [pscustomobject]@{ Name = 'battery-stop'; Mismatch = 'stopIfGoingOnBatteries'; Apply = { param($Task) $Task.StopIfGoingOnBatteries = $true } }
        [pscustomobject]@{ Name = 'demand-start-refused'; Mismatch = 'allowDemandStart'; Apply = { param($Task) $Task.AllowDemandStart = $false } }
        [pscustomobject]@{ Name = 'hard-terminate-refused'; Mismatch = 'allowHardTerminate'; Apply = { param($Task) $Task.AllowHardTerminate = $false } }
        [pscustomobject]@{ Name = 'other-principal'; Mismatch = 'principal'; Apply = { param($Task) $Task.UserSid = $statusOtherSid } }
        [pscustomobject]@{ Name = 'other-logon-user'; Mismatch = 'trigger'; Apply = { param($Task) $Task.Triggers[0].UserSid = $statusOtherSid } }
        [pscustomobject]@{ Name = 'action-working-directory'; Mismatch = 'action'; Apply = { param($Task) $Task.Actions[0].WorkingDirectory = 'D:\elsewhere' } }
    )
    foreach ($statusDrift in $statusSingleFieldDrifts) {
        $statusCase = New-CountdownStatusTestCase -Name $statusDrift.Name
        & $statusDrift.Apply $statusCase.Task
        $statusDriftResult = Invoke-CountdownStatusTestRead -Label $statusDrift.Name
        Assert-Equal $false ([bool] $statusDriftResult.loginTask.matchesExpected) "A single-field deviation is rejected: $($statusDrift.Name)"
        Assert-Equal $statusDrift.Mismatch (@($statusDriftResult.loginTask.mismatches) -join ',') "Status names only the field that deviated: $($statusDrift.Name)"
    }

    # 真实 Windows 查询：按远端执行入口的方式把脚本包进脚本块、经 -EncodedCommand 在子进程运行（没有脚本文件），
    # 对一个空安装根和一个不存在的任务名只读。
    $realRoot = Join-Path $statusTestRoot 'real-ports'
    New-Item -ItemType Directory -Path $realRoot -Force | Out-Null
    $realTaskName = 'Minmin Countdown Status Probe ' + [guid]::NewGuid().ToString('N')
    $statusSource = [System.IO.File]::ReadAllText($statusScript)
    # 远端执行入口把整条命令限制在 30000 字符内（UTF-16 的 Base64 加上约 1000 字符的前缀）。
    $statusEncodedLength = [Convert]::ToBase64String([System.Text.Encoding]::Unicode.GetBytes('& {' + $statusSource.TrimEnd() + "`n}")).Length
    Assert-Equal $true ($statusEncodedLength -le 29000) "Status command fits the remote entry's encoded command limit: $statusEncodedLength"
    $realCommand = '& {' + $statusSource.TrimEnd() + "`n} -InstallRoot '" + $realRoot.Replace("'", "''") + "' -TaskName '" + $realTaskName + "' -LockName '" + $statusLockName + "'"
    $realOutput = @(& pwsh -NoLogo -NoProfile -NonInteractive -EncodedCommand ([Convert]::ToBase64String([System.Text.Encoding]::Unicode.GetBytes($realCommand))))
    Assert-Equal 0 $LASTEXITCODE 'Status command runs as a script block with real Windows queries'
    Assert-Equal '' (Get-CountdownStatusTestTreeState -Root $realRoot) 'Status command with real Windows queries creates nothing in the install root'
    $realText = $realOutput -join "`n"
    $realStatus = $realText | ConvertFrom-Json -DateKind String
    Assert-Equal $realRoot ([string] $realStatus.installRoot) 'Real status names the install root it read'
    Assert-Equal 'missing' ([string] $realStatus.manifest) 'Real status reports the payload manifest missing'
    Assert-Equal $false ([bool] $realStatus.exe.present) 'Real status reports the EXE missing'
    Assert-Equal $false ([bool] $realStatus.running) 'Real status finds no process running an EXE that does not exist'
    Assert-Equal 0 @($realStatus.processes).Count 'Real status lists no process for the empty install root'
    Assert-Equal $realTaskName ([string] $realStatus.loginTask.name) 'Real status names the login task it read'
    Assert-Equal $false ([bool] $realStatus.loginTask.present) 'Real status reports a task that does not exist as missing'
    Assert-Equal $false ([bool] $realStatus.settings.present) 'Real status reports the settings file missing'
    Assert-Equal $false ([bool] $realStatus.transactionMarkerPresent) 'Real status reports no transaction marker'
    Assert-Equal $false $realStatus.installInProgress 'Real status reports no run in progress for an install lock nobody holds'
    $identityLeaks = @(@($env:COMPUTERNAME, $env:USERNAME) | Where-Object { [string] $_ -and ([string] $_).Length -ge 4 -and $realText.IndexOf([string] $_, [System.StringComparison]::OrdinalIgnoreCase) -ge 0 })
    Assert-Equal 0 $identityLeaks.Count 'Real status output carries no host name or account'

    # 构建在载荷清单里记录提交标识：构建根是 Git 工作树顶层时取 HEAD，有未提交改动时带 -dirty；不在 Git 工作树里
    # （例如旧部署路径从 git archive 导出的源码）时为 null，状态命令据此报告未知。
    Import-Module (Join-Path $PSScriptRoot 'tools\Countdown.BuildPreflight.psm1') -Force
    $statusHead = (@(& git -C $PSScriptRoot rev-parse --verify HEAD) -join '').Trim()
    Assert-Equal $true ((Get-CountdownBuildSourceCommit -RepositoryRoot $PSScriptRoot) -match "^$statusHead(-dirty)?$") 'Build source commit of the checkout is its HEAD'
    Assert-Equal $true ($null -eq (Get-CountdownBuildSourceCommit -RepositoryRoot (Join-Path $PSScriptRoot 'src'))) 'A build root below the work tree top records no commit'
    New-Item -ItemType Directory -Path $statusGitFixtureRoot -Force | Out-Null
    Assert-Equal $true ($null -eq (Get-CountdownBuildSourceCommit -RepositoryRoot $statusGitFixtureRoot)) 'A build root outside any Git work tree records no commit'
    foreach ($gitArguments in @(
            @('init', '--quiet'),
            @('config', 'user.name', 'Install status fixture'),
            @('config', 'user.email', 'fixture@example.invalid'),
            @('config', 'core.autocrlf', 'false'))) {
        & git -C $statusGitFixtureRoot @gitArguments | Out-Null
    }
    [System.IO.File]::WriteAllText((Join-Path $statusGitFixtureRoot 'source.txt'), "built`n", $statusUtf8)
    & git -C $statusGitFixtureRoot add source.txt | Out-Null
    & git -C $statusGitFixtureRoot commit --quiet -m 'fixture build source' | Out-Null
    $fixtureHead = (@(& git -C $statusGitFixtureRoot rev-parse --verify HEAD) -join '').Trim()
    Assert-Equal $fixtureHead (Get-CountdownBuildSourceCommit -RepositoryRoot $statusGitFixtureRoot) 'A clean work tree records the bare commit'
    [System.IO.File]::WriteAllText((Join-Path $statusGitFixtureRoot 'untracked.txt'), "not committed`n", $statusUtf8)
    Assert-Equal "$fixtureHead-dirty" (Get-CountdownBuildSourceCommit -RepositoryRoot $statusGitFixtureRoot) 'Uncommitted files mark the recorded commit dirty'
    # 本次套件的构建输出（Build 或 IncrementalBuild 阶段）把提交标识写进了载荷清单。
    $builtManifest = [System.IO.File]::ReadAllText((Join-Path $PSScriptRoot 'app\published-payload.json')) | ConvertFrom-Json
    Assert-Equal $true ([string] $builtManifest.commit -match "^$statusHead(-dirty)?$") 'Build output payload manifest records the commit it was built from'
}
finally {
    Remove-CountdownIsolatedTestTree -Path $statusTestRoot -AllowedRoot $PSScriptRoot
    # Windows 上 git init 把 .git 设为隐藏，入口的清理函数不枚举隐藏项，所以先去掉隐藏属性。
    $statusGitDirectory = Join-Path $statusGitFixtureRoot '.git'
    if (Test-Path -LiteralPath $statusGitDirectory -PathType Container) {
        $statusGitDirectoryItem = Get-Item -LiteralPath $statusGitDirectory -Force
        $statusGitDirectoryItem.Attributes = $statusGitDirectoryItem.Attributes -band -bnot [System.IO.FileAttributes]::Hidden
    }
    Remove-CountdownIsolatedTestTree -Path $statusGitFixtureRoot -AllowedRoot ([System.IO.Path]::GetTempPath())
}
