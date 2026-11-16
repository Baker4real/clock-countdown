#requires -Version 7.0

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# 新部署入口（#259 / #285 / #286）：把已推送的 main 当前提交部署到办公室，送过去的就是本机套件测过的那份构建输出。
# 部署：本机预检 → 打包 → 只读连通性探测（读一次办公室的安装状态）→ 传输到按运行编号命名的暂存目录 → 远端核对包哈希、
# 解包并运行包内安装器 → 只读状态读取（提交、EXE 哈希与运行中的进程都与预期一致才算成功）→ 本地部署记录。
# 只查状态：一次只读状态读取，不做任何改动。
# 部署的结论 outcome 只有五种：deployed；already-running（探测读到当前提交已在运行，不传输、不安装）；rejected（连接远端之前就停下，
# reason 写明缺什么；或办公室上另一个安装、切换或回退正在运行，包内入口什么都没改就停下）；failed（安装失败时 rollback 是安装器
# 报告的回滚结果）；unknown（拿不到安装结果或安装后的状态，既不算成功也不算失败）。办公室现状不确定时 nextStep 提示先查状态。
# 回退成功的结论是 rolled-back。
# 一次性布局切换（#287）：-LayoutSwitch 与部署同一流程，包里另带切换脚本，远端写操作运行它（备份、经包内安装器换布局、移除遗留），
# 探测读到当前提交已在运行也照常切换；-LayoutRollback 一次远端写操作运行办公室备份里的回退脚本，不打包、不传输。
# 远端执行与传输经 port 调用：生产用 New-CountdownDeployerHostPorts 接主机受管的远端执行入口与传输入口，测试注入假 port。
# port 的返回值只取已知字段，结果与记录里没有主机名、地址、账号、凭据或连接参数。

Import-Module (Join-Path $PSScriptRoot 'tools\Countdown.SuiteStages.psm1')

$script:ExecutableManifestPath = 'app\MinminCountdown.exe'
$script:InstallerFiles = @('Install-CountdownProgram.ps1', 'Countdown.Installer.psm1')
$script:LayoutSwitchScript = 'Switch-CountdownInstallLayout.ps1'
$script:StatusScriptName = 'Get-CountdownInstallStatus.ps1'
$script:DeploySteps = @('preflight', 'package', 'probe', 'transfer', 'install', 'status', 'record')
$script:Utf8NoBom = [System.Text.UTF8Encoding]::new($false)
# 安装器报告的回滚结果（#283 的 rollback.outcome）及其在部署结论里的说法。
$script:RollbackOutcomes = [ordered]@{
    'not-needed' = '没有需要回滚的改动。'
    'rolled-back' = '已回滚，办公室回到安装前的版本。'
    'rollback-failed' = '回滚也失败了，办公室可能停在半安装的状态。'
}
# 办公室现状不确定时（结果未知、回滚失败或安装器没报回滚结果）给维护者的下一步。
$script:ReadStatusFirst = '先运行 Deploy-CountdownProgram.ps1 -StatusOnly 读一次办公室的状态：installInProgress 为 true（另一个安装、切换或回退还在运行）时等它结束再读；transactionMarkerPresent 为 true（有未完成的安装）时重新执行部署，安装器会先恢复再安装（恢复不了时安装停在 recovery 阶段，结果的 install.recovery 写明原因）；状态显示本次提交已在运行就不用再做什么。'
# 办公室上另一个安装、切换或回退正在运行（持有全机安装锁）时包内入口报告的原因：安装器的 install-in-progress，切换与回退的
# transaction-in-progress。它们什么都没改就停下，部署的结论是 rejected。
$script:InProgressReasons = @('install-in-progress', 'transaction-in-progress')
$script:InProgressNextStep = '办公室上另一个安装、切换或回退正在运行，这次什么都没改：等它结束，再运行 Deploy-CountdownProgram.ps1 -StatusOnly 读一次状态（installInProgress 为 false 之后再看其余各项），据此决定要不要重新运行；它还在运行时不要再部署、切换或回退。'

# 写操作脚本的第一条语句（param 块之后）必须是对执行目标的断言：主机的远端执行入口缺了它就拒绝执行。
# 包内入口 __ENTRY__ 是安装器，布局切换时是切换脚本（两者参数相同，都输出一份结果 JSON）。
$script:RemoteInstallScript = @'
param([object] $Port, [string] $LockName)
if ($env:CODEX_REMOTE_EXECUTION_TARGET -ne 'office') { throw '安装脚本只能经远端执行入口在办公室上运行。' }
$ErrorActionPreference = 'Stop'
$staging = __STAGING__
$packagePath = Join-Path $staging 'package.zip'
$packageRoot = Join-Path $staging 'package'
# installerStarted：安装器开始运行之后才可能动到安装根；它之前的失败（包哈希、解包）什么都没改。
$envelope = [ordered]@{ packageVerified = $false; installerStarted = $false; installer = $null; stagingRemoved = $false; error = $null }
try {
    if ((Get-FileHash -LiteralPath $packagePath -Algorithm SHA256).Hash -ne __SHA256__) { throw '暂存目录里的包与部署端算出的包哈希不一致。' }
    $envelope.packageVerified = $true
    [System.IO.Compression.ZipFile]::ExtractToDirectory($packagePath, $packageRoot)
    $arguments = @{ PackageRoot = $packageRoot; InstallRoot = __INSTALL_ROOT__; TaskName = __TASK_NAME__ }
    if ($null -ne $Port) { $arguments.Port = $Port }
    if ($LockName) { $arguments.LockName = $LockName }
    $envelope.installerStarted = $true
    $envelope.installer = @(& (Join-Path $packageRoot __ENTRY__) @arguments) -join "`n"
}
catch { $envelope.error = ($_.Exception.Message -replace '\s+', ' ').Trim() }
finally {
    # 包和暂存内容不长期保留：安装器已把程序目录复制到安装根，暂存目录随即删除。
    try { if (Test-Path -LiteralPath $staging) { [System.IO.Directory]::Delete($staging, $true) } } catch { }
    $envelope.stagingRemoved = -not (Test-Path -LiteralPath $staging)
}
$envelope | ConvertTo-Json -Compress -EscapeHandling EscapeNonAscii
'@

# 回退（#287）：切换脚本把自己和新安装器 module 复制进了办公室上的备份 <安装根>.layout-switch-backup\tools\，从那里以 -Rollback 运行。
$script:RemoteRollbackScript = @'
param([object] $Port, [string] $LockName)
if ($env:CODEX_REMOTE_EXECUTION_TARGET -ne 'office') { throw '回退脚本只能经远端执行入口在办公室上运行。' }
$ErrorActionPreference = 'Stop'
$installRoot = [System.IO.Path]::TrimEndingDirectorySeparator([System.IO.Path]::GetFullPath(__INSTALL_ROOT__))
$entry = Join-Path "$installRoot.layout-switch-backup" 'tools\Switch-CountdownInstallLayout.ps1'
# rollbackStarted：回退脚本开始运行之后才可能动到安装；它之前的失败（没有备份）什么都没改。
$envelope = [ordered]@{ rollbackStarted = $false; rollback = $null; error = $null }
try {
    if (-not (Test-Path -LiteralPath $entry -PathType Leaf)) { throw "没有切换前的备份：$entry 不在。" }
    $arguments = @{ Rollback = $true; InstallRoot = $installRoot; TaskName = __TASK_NAME__ }
    if ($null -ne $Port) { $arguments.Port = $Port }
    if ($LockName) { $arguments.LockName = $LockName }
    $envelope.rollbackStarted = $true
    $envelope.rollback = @(& $entry @arguments) -join "`n"
}
catch { $envelope.error = ($_.Exception.Message -replace '\s+', ' ').Trim() }
$envelope | ConvertTo-Json -Compress -EscapeHandling EscapeNonAscii
'@
# 布局切换没有成功时，切换脚本给的下一步之后补这一句：经部署入口怎么接着切换或回退。
$script:LayoutSwitchNextStep = '经部署入口再次切换运行 Deploy-CountdownProgram.ps1 -LayoutSwitch（备份完整时沿用它接着做），回到切换前运行 -LayoutRollback。'
# 回退结果不确定时给维护者的下一步。
$script:RollbackReadStatusFirst = '先运行 Deploy-CountdownProgram.ps1 -StatusOnly 读一次办公室的状态；回退可以重复运行，状态还不是切换前的样子就再运行一次 -LayoutRollback。'

function Get-CountdownDeployProperty {
    param(
        [AllowNull()] [object] $Object,
        [Parameter(Mandatory)] [string] $Name
    )

    if ($null -eq $Object) { return $null }
    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property) { return $null }
    $property.Value
}

function Test-CountdownDeployTrue {
    param([AllowNull()] [object] $Value)
    $Value -is [bool] -and $Value
}

# 带原因类别的失败：原因类别写进结果的 reason，消息写明缺什么或哪里不对。结论默认按是否已经连接远端定为拒绝或失败；
# 不知道办公室上发生了什么时给 -Outcome unknown，办公室上什么都没改就拒绝了（另一个运行在进行）时给 -Outcome rejected。
function New-CountdownDeployFailure {
    param(
        [Parameter(Mandatory)] [string] $Reason,
        [Parameter(Mandatory)] [string] $Message,
        [ValidateSet('unknown', 'rejected')] [string] $Outcome
    )

    $exception = [System.InvalidOperationException]::new($Message)
    $exception.Data['CountdownDeployReason'] = $Reason
    if ($Outcome) { $exception.Data['CountdownDeployOutcome'] = $Outcome }
    $exception
}

function ConvertTo-CountdownDeployBoundedText {
    param([AllowNull()] [AllowEmptyString()] [string] $Text)

    $value = ([string] $Text -replace '\s+', ' ').Trim()
    if ($value.Length -gt 400) { $value = $value.Substring(0, 400) + '…' }
    $value
}

# 底层错误后面接部署的说法：没有句末标点时补一个句号。
function ConvertTo-CountdownDeploySentence {
    param([AllowEmptyString()] [string] $Text)

    $value = ([string] $Text).Trim()
    if ($value -and $value[-1] -notin [char[]] '。.!?！？') { $value += '。' }
    $value
}

function ConvertTo-CountdownDeployLiteral {
    param([AllowEmptyString()] [string] $Value)
    "'" + [System.Management.Automation.Language.CodeGeneration]::EscapeSingleQuotedStringContent($Value) + "'"
}

# 远端脚本模板的占位符一次替换成各自值的单引号字面量：替换进去的值里拼着别的占位符字样（暂存根、任务名都由调用方给出）
# 也不会被再替换一次。
function Expand-CountdownDeployTemplate {
    param(
        [Parameter(Mandatory)] [string] $Template,
        [Parameter(Mandatory)] [System.Collections.IDictionary] $Values
    )

    $pattern = (@($Values.Keys) | ForEach-Object { [regex]::Escape([string] $_) }) -join '|'
    $Template -creplace $pattern, { ConvertTo-CountdownDeployLiteral -Value ([string] $Values[$_.Value]) }
}

# 进度只写标准错误：标准输出留给结果 JSON。每步一行“[deploy] 时刻 序号/总数 步骤 说明”。
function Write-CountdownDeployProgress {
    param(
        [Parameter(Mandatory)] [string] $Step,
        [Parameter(Mandatory)] [string] $Message,
        [string[]] $Steps = $script:DeploySteps
    )

    $index = [Array]::IndexOf($Steps, $Step) + 1
    [Console]::Error.WriteLine(('[deploy] {0:HH:mm:ss} {1}/{2} {3} {4}' -f [DateTime]::Now, $index, $Steps.Count, $Step, $Message))
}

# 结论是标准错误的最后一行；部署时带上与结果 JSON 相同的 outcome。
function Write-CountdownDeployConclusion {
    param(
        [Parameter(Mandatory)] [string] $Message,
        [string] $Outcome
    )

    $label = if ($Outcome) { "结果 $Outcome" } else { '结果' }
    [Console]::Error.WriteLine(('[deploy] {0:HH:mm:ss} {1}：{2}' -f [DateTime]::Now, $label, $Message))
}

function Invoke-CountdownDeployGit {
    param(
        [Parameter(Mandatory)] [string] $Root,
        [Parameter(Mandatory)] [string[]] $Arguments
    )

    $PSNativeCommandUseErrorActionPreference = $false
    $records = @(& git -C $Root @Arguments 2>&1)
    $exitCode = $LASTEXITCODE
    $lines = @($records | Where-Object { $_ -isnot [System.Management.Automation.ErrorRecord] } | ForEach-Object { [string] $_ })
    $errors = @($records | Where-Object { $_ -is [System.Management.Automation.ErrorRecord] } | ForEach-Object { [string] $_ })
    [pscustomobject]@{
        ExitCode = $exitCode
        Lines = $lines
        Text = ($lines -join "`n").Trim()
        Error = ConvertTo-CountdownDeployBoundedText -Text ($errors -join ' ')
    }
}

function Get-CountdownDeploySuiteStage {
    param(
        [AllowNull()] [object[]] $Stages,
        [Parameter(Mandatory)] [string] $Name
    )
    @($Stages | Where-Object { [string] (Get-CountdownDeployProperty -Object $_ -Name 'Stage') -ceq $Name })
}

# 一份同一提交套件记录的问题，按先后：必过阶段缺失或没有通过；构建回执不是带聚合哈希的完整干净构建（CleanR2R）；
# 开跑或收尾时工作树不干净、HEAD 变了；整轮没有通过。每项带原因类别，消息写明缺什么；没有问题时什么都不返回。
function Get-CountdownDeploySuiteRunProblem {
    param(
        [Parameter(Mandatory)] [object] $Run,
        [Parameter(Mandatory)] [string] $Commit,
        [Parameter(Mandatory)] [string[]] $RequiredStages
    )

    $stages = @(Get-CountdownDeployProperty -Object $Run -Name 'Stages')
    $build = @(Get-CountdownDeploySuiteStage -Stages $stages -Name 'Build')
    # 这一轮的构建若是增量构建 IncrementalBuild，按“构建不是干净构建”报，不按“没跑 Build”报。
    $incremental = @(Get-CountdownDeploySuiteStage -Stages $stages -Name 'IncrementalBuild')
    $missing = [System.Collections.Generic.List[string]]::new()
    $failed = [System.Collections.Generic.List[string]]::new()
    foreach ($name in $RequiredStages) {
        $records = @(Get-CountdownDeploySuiteStage -Stages $stages -Name $name)
        if ($records.Count -eq 0) {
            if ($name -cne 'Build' -or $incremental.Count -eq 0) { $missing.Add($name) }
            continue
        }
        $status = [string] (Get-CountdownDeployProperty -Object $records[0] -Name 'Status')
        $exitCode = Get-CountdownDeployProperty -Object $records[0] -Name 'ExitCode'
        if ($records.Count -ne 1 -or $status -cne 'Completed' -or $exitCode -ne 0) { $failed.Add("$name（$status，退出码 $exitCode）") }
    }
    $stagesPassed = $missing.Count -eq 0 -and $failed.Count -eq 0
    if (-not $stagesPassed) {
        $parts = @(
            if ($missing.Count -gt 0) { "没跑 $($missing -join '、')" }
            if ($failed.Count -gt 0) { "$($failed -join '、') 没有通过" }
        )
        [pscustomobject]@{ Reason = 'required-stages-incomplete'; Message = "必过阶段不全：$($parts -join '，')" }
    }

    $describeReceipt = {
        param([AllowNull()] [object] $Receipt)
        if ($null -eq $Receipt) { return '没有回执' }
        "回执 Mode=$(Get-CountdownDeployProperty -Object $Receipt -Name 'Mode')、Clean=$(Get-CountdownDeployProperty -Object $Receipt -Name 'Clean')"
    }
    if ($build.Count -eq 0 -and $incremental.Count -gt 0) {
        $receipt = Get-CountdownDeployProperty -Object $incremental[0] -Name 'BuildReceipt'
        [pscustomobject]@{ Reason = 'build-receipt-not-clean'; Message = "构建不是干净构建：这一轮用的是 IncrementalBuild（$(& $describeReceipt $receipt)），部署要求 Build 阶段的 CleanR2R 回执" }
    }
    elseif ($build.Count -eq 1 -and [string] (Get-CountdownDeployProperty -Object $build[0] -Name 'Status') -ceq 'Completed') {
        $receipt = Get-CountdownDeployProperty -Object $build[0] -Name 'BuildReceipt'
        if ((Get-CountdownDeployProperty -Object $receipt -Name 'SchemaVersion') -ne 1 -or
            (Get-CountdownDeployProperty -Object $receipt -Name 'Mode') -cne 'CleanR2R' -or
            -not (Test-CountdownDeployTrue (Get-CountdownDeployProperty -Object $receipt -Name 'Clean')) -or
            -not (Test-CountdownDeployTrue (Get-CountdownDeployProperty -Object $receipt -Name 'ReadyToRun'))) {
            [pscustomobject]@{ Reason = 'build-receipt-not-clean'; Message = "构建回执不是完整的干净构建（$(& $describeReceipt $receipt)），部署要求 CleanR2R" }
        }
        elseif ([string] (Get-CountdownDeployProperty -Object $receipt -Name 'PayloadAggregateSha256') -notmatch '^[0-9A-Fa-f]{64}$') {
            [pscustomobject]@{ Reason = 'build-receipt-not-clean'; Message = '构建回执里没有构建输出的聚合哈希（这一轮的构建脚本还不写它）' }
        }
    }

    $source = Get-CountdownDeployProperty -Object $Run -Name 'Source'
    $unclean = @(
        if (-not (Test-CountdownDeployTrue (Get-CountdownDeployProperty -Object $source -Name 'CleanAtStart'))) { '开跑时工作树不干净' }
        if (-not (Test-CountdownDeployTrue (Get-CountdownDeployProperty -Object $source -Name 'CleanAtEnd'))) { '收尾时工作树不干净' }
        if (-not [string]::Equals([string] (Get-CountdownDeployProperty -Object $source -Name 'CommitAtEnd'), $Commit, [System.StringComparison]::OrdinalIgnoreCase)) { '收尾时 HEAD 已经变了' }
    )
    if ($unclean.Count -gt 0) {
        [pscustomobject]@{ Reason = 'suite-evidence-missing'; Message = "这一轮不能代表这个提交：$($unclean -join '、')" }
    }
    # 必过阶段都过了、整轮却没有通过：别的阶段或收尾检查失败。
    $runStatus = [string] (Get-CountdownDeployProperty -Object $Run -Name 'Status')
    if ($stagesPassed -and ($runStatus -cne 'Completed' -or (Get-CountdownDeployProperty -Object $Run -Name 'ExitCode') -ne 0)) {
        [pscustomobject]@{ Reason = 'suite-evidence-missing'; Message = "这一轮没有整轮通过（$runStatus：$(Get-CountdownDeployProperty -Object $Run -Name 'Failure')）" }
    }
}

# 套件记录：同一提交、本 checkout 的记录里取最新的合格记录（没有问题的记录）。没有合格记录时按最新一份同一提交记录的
# 第一个问题拒绝，消息写明这份记录的每个问题；连同一提交的记录都没有就是缺少套件证据。
function Find-CountdownDeploySuiteRun {
    param(
        [Parameter(Mandatory)] [string] $RepositoryRoot,
        [Parameter(Mandatory)] [string] $Commit
    )

    $directory = Join-Path $RepositoryRoot 'logs\suite-runs'
    $repositoryPath = [System.IO.Path]::GetFullPath($RepositoryRoot).TrimEnd('\')
    $requiredStages = [string[]] @(Get-CountdownRequiredSuiteStage)
    $runFiles = @()
    if (Test-Path -LiteralPath $directory -PathType Container) {
        $runFiles = @(Get-ChildItem -LiteralPath $directory -Filter '*-run.json' -File | Sort-Object Name -Descending)
    }
    $newest = $null
    foreach ($runFile in $runFiles) {
        # 读不出、别的提交、别的 checkout 或认不出的格式：都不是这个提交在本 checkout 的证据。
        try {
            $run = [System.IO.File]::ReadAllText($runFile.FullName) | ConvertFrom-Json -DateKind String
            $runRoot = [string] (Get-CountdownDeployProperty -Object $run -Name 'RepositoryRoot')
            $evidence = (Get-CountdownDeployProperty -Object $run -Name 'SchemaVersion') -eq 1 -and
                [string]::Equals([string] (Get-CountdownDeployProperty -Object (Get-CountdownDeployProperty -Object $run -Name 'Source') -Name 'Commit'), $Commit, [System.StringComparison]::OrdinalIgnoreCase) -and
                -not [string]::IsNullOrWhiteSpace($runRoot) -and
                [string]::Equals([System.IO.Path]::GetFullPath($runRoot).TrimEnd('\'), $repositoryPath, [System.StringComparison]::OrdinalIgnoreCase)
        }
        catch { $evidence = $false }
        if (-not $evidence) { continue }
        $runId = [string] (Get-CountdownDeployProperty -Object $run -Name 'RunId')
        $problems = @(Get-CountdownDeploySuiteRunProblem -Run $run -Commit $Commit -RequiredStages $requiredStages)
        if ($problems.Count -eq 0) {
            $build = @(Get-CountdownDeploySuiteStage -Stages @(Get-CountdownDeployProperty -Object $run -Name 'Stages') -Name 'Build')[0]
            return [pscustomobject]@{
                RunId = $runId
                Path = $runFile.FullName
                PayloadAggregateSha256 = ([string] (Get-CountdownDeployProperty -Object (Get-CountdownDeployProperty -Object $build -Name 'BuildReceipt') -Name 'PayloadAggregateSha256')).ToUpperInvariant()
            }
        }
        if ($null -eq $newest) { $newest = [pscustomobject]@{ RunId = $runId; Problems = $problems } }
    }
    $shortCommit = $Commit.Substring(0, 7)
    $rerun = "先在干净的 main 上运行 pwsh -NoLogo -NoProfile -File ./tools/Invoke-CountdownSuiteLowImpact.ps1 -Stage $($requiredStages -join ',')，通过后再部署。"
    if ($null -eq $newest) {
        throw (New-CountdownDeployFailure -Reason 'suite-evidence-missing' -Message "缺少套件证据：logs\suite-runs 里没有提交 $shortCommit 在本 checkout 的套件记录。$rerun")
    }
    throw (New-CountdownDeployFailure -Reason $newest.Problems[0].Reason -Message "提交 $shortCommit 没有合格的套件记录，最新一份 $($newest.RunId)：$(@($newest.Problems | ForEach-Object { $_.Message }) -join '；')。$rerun")
}

function Test-CountdownDeployManifestPath {
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

# 本机构建输出：程序目录 app\ 与构建写在里面的载荷清单。清单记的提交要是 HEAD，聚合哈希按清单顺序的“路径\0哈希\n”重算，
# 与构建脚本和安装器同一算法。逐个文件的哈希在打包时边写边核对。
function Read-CountdownDeployBuildOutput {
    param(
        [Parameter(Mandatory)] [string] $RepositoryRoot,
        [Parameter(Mandatory)] [string] $Commit
    )

    $manifestPath = Join-Path $RepositoryRoot 'app\published-payload.json'
    $rebuild = '重新跑一轮套件，部署它测过的构建输出。'
    $invalid = { param([string] $Detail) New-CountdownDeployFailure -Reason 'build-output-invalid' -Message "$Detail。$rebuild" }
    if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
        throw (New-CountdownDeployFailure -Reason 'build-output-missing' -Message "本机没有构建输出：缺少载荷清单 app\published-payload.json。$rebuild")
    }
    try { $document = [System.IO.File]::ReadAllText($manifestPath) | ConvertFrom-Json }
    catch { throw (& $invalid '本机构建输出的载荷清单不是有效 JSON') }
    if ((Get-CountdownDeployProperty -Object $document -Name 'schemaVersion') -ne 1) { throw (& $invalid '本机构建输出的载荷清单 schemaVersion 不是 1') }
    $manifestCommit = [string] (Get-CountdownDeployProperty -Object $document -Name 'commit')
    if (-not [string]::Equals($manifestCommit, $Commit, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw (& $invalid "本机构建输出不是提交 $($Commit.Substring(0, 7)) 构建的（载荷清单记的是 $manifestCommit）")
    }
    $entries = [System.Collections.Generic.List[object]]::new()
    $seen = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    $aggregateInput = [System.Text.StringBuilder]::new()
    foreach ($file in @(Get-CountdownDeployProperty -Object $document -Name 'files')) {
        $path = [string] (Get-CountdownDeployProperty -Object $file -Name 'path')
        $sha256 = [string] (Get-CountdownDeployProperty -Object $file -Name 'sha256')
        if (-not (Test-CountdownDeployManifestPath -Path $path) -or $sha256 -notmatch '^[0-9A-Fa-f]{64}$' -or -not $seen.Add($path)) {
            throw (& $invalid "本机构建输出的载荷清单条目无效：$path")
        }
        $entries.Add([pscustomobject]@{ Path = $path; Sha256 = $sha256.ToUpperInvariant() })
        [void] $aggregateInput.Append($path).Append([char] 0).Append($sha256).Append([char] 10)
    }
    if ($entries.Count -eq 0) { throw (& $invalid '本机构建输出的载荷清单没有文件') }
    if (-not $seen.Contains($script:ExecutableManifestPath)) { throw (& $invalid "本机构建输出的载荷清单没有 $($script:ExecutableManifestPath)") }
    $aggregate = [Convert]::ToHexString([System.Security.Cryptography.SHA256]::HashData([System.Text.Encoding]::UTF8.GetBytes($aggregateInput.ToString())))
    if (-not [string]::Equals($aggregate, [string] (Get-CountdownDeployProperty -Object $document -Name 'aggregateSha256'), [System.StringComparison]::OrdinalIgnoreCase)) {
        throw (& $invalid '本机构建输出的载荷清单聚合哈希与文件列表不一致')
    }
    [pscustomobject]@{
        ManifestPath = $manifestPath
        AggregateSha256 = $aggregate
        Files = $entries.ToArray()
        ExecutableSha256 = @($entries | Where-Object { [string]::Equals($_.Path, $script:ExecutableManifestPath, [System.StringComparison]::OrdinalIgnoreCase) })[0].Sha256
    }
}

# 本机预检（不联网）：main、工作树干净、提交已推送（与 origin/main 一致）；该提交有合格的套件记录；
# 本机构建输出的聚合哈希等于那次套件运行的构建回执。
function Get-CountdownDeployPreflight {
    param([Parameter(Mandatory)] [string] $RepositoryRoot)

    $branch = Invoke-CountdownDeployGit -Root $RepositoryRoot -Arguments @('symbolic-ref', '--quiet', '--short', 'HEAD')
    if ($branch.ExitCode -ne 0 -or $branch.Text -cne 'main') {
        $current = if ($branch.ExitCode -eq 0 -and $branch.Text) { $branch.Text } else { '分离的 HEAD' }
        throw (New-CountdownDeployFailure -Reason 'not-main' -Message "只部署 main：当前在 $current 上。先切回 main。")
    }
    $status = Invoke-CountdownDeployGit -Root $RepositoryRoot -Arguments @('--no-optional-locks', 'status', '--porcelain=v1', '--untracked-files=all')
    if ($status.ExitCode -ne 0) { throw "读不出工作树状态：$($status.Error)" }
    if ($status.Lines.Count -gt 0) {
        # porcelain v1 每行是两位状态码、一个空格和路径。
        $paths = @($status.Lines | Select-Object -First 5 | ForEach-Object { $_.Substring([Math]::Min(3, $_.Length)) })
        $more = if ($status.Lines.Count -gt $paths.Count) { " 等 $($status.Lines.Count) 项" } else { '' }
        throw (New-CountdownDeployFailure -Reason 'dirty-work-tree' -Message "工作树不干净，有未提交的改动：$($paths -join '、')$more。先提交或撤回再部署。")
    }
    $head = Invoke-CountdownDeployGit -Root $RepositoryRoot -Arguments @('rev-parse', '--verify', 'HEAD')
    if ($head.ExitCode -ne 0 -or $head.Text -notmatch '^[0-9a-f]{40}$') { throw "读不出 HEAD：$($head.Error)" }
    $commit = $head.Text
    $pushed = Invoke-CountdownDeployGit -Root $RepositoryRoot -Arguments @('rev-parse', '--verify', '--quiet', 'refs/remotes/origin/main')
    if ($pushed.ExitCode -ne 0 -or $pushed.Text -ne $commit) {
        $remoteHead = if ($pushed.Text) { $pushed.Text.Substring(0, [Math]::Min(7, $pushed.Text.Length)) } else { '（不存在）' }
        throw (New-CountdownDeployFailure -Reason 'not-pushed' -Message "提交 $($commit.Substring(0, 7)) 尚未推送：origin/main 是 $remoteHead。只部署与 origin/main 一致的 HEAD。")
    }
    $suiteRun = Find-CountdownDeploySuiteRun -RepositoryRoot $RepositoryRoot -Commit $commit
    $buildOutput = Read-CountdownDeployBuildOutput -RepositoryRoot $RepositoryRoot -Commit $commit
    if ($buildOutput.AggregateSha256 -ne $suiteRun.PayloadAggregateSha256) {
        throw (New-CountdownDeployFailure -Reason 'aggregate-mismatch' -Message ("本机构建输出的聚合哈希 $($buildOutput.AggregateSha256.Substring(0, 12))… 与套件运行 $($suiteRun.RunId) " +
                "构建回执里的 $($suiteRun.PayloadAggregateSha256.Substring(0, 12))… 不一致：程序目录不是套件测过的那份构建输出。重新跑一轮套件，部署它测过的构建输出。"))
    }
    [pscustomobject]@{ Commit = $commit; SuiteRun = $suiteRun; BuildOutput = $buildOutput }
}

function Add-CountdownDeployPackageEntry {
    param(
        [Parameter(Mandatory)] [System.IO.Compression.ZipArchive] $Archive,
        [Parameter(Mandatory)] [string] $SourcePath,
        [Parameter(Mandatory)] [string] $EntryName
    )

    $entry = $Archive.CreateEntry($EntryName.Replace('\', '/'), [System.IO.Compression.CompressionLevel]::Optimal)
    $hash = [System.Security.Cryptography.IncrementalHash]::CreateHash([System.Security.Cryptography.HashAlgorithmName]::SHA256)
    try {
        $source = [System.IO.File]::OpenRead($SourcePath)
        try {
            $target = $entry.Open()
            try {
                $buffer = [byte[]]::new(81920)
                while (($read = $source.Read($buffer, 0, $buffer.Length)) -gt 0) {
                    $hash.AppendData($buffer, 0, $read)
                    $target.Write($buffer, 0, $read)
                }
            }
            finally { $target.Dispose() }
        }
        finally { $source.Dispose() }
        [Convert]::ToHexString($hash.GetHashAndReset())
    }
    finally { $hash.Dispose() }
}

# 包：程序目录里载荷清单列出的文件、载荷清单和安装器（布局切换时另带切换脚本）。程序目录的文件取自本机构建输出，
# 不重新导出、不重新构建；边写进包边算 SHA-256，与清单不一致就停。
function New-CountdownDeployPackage {
    param(
        [Parameter(Mandatory)] [string] $RepositoryRoot,
        [Parameter(Mandatory)] [object] $BuildOutput,
        [Parameter(Mandatory)] [string] $PackagePath,
        [string[]] $Scripts = $script:InstallerFiles
    )

    $archive = [System.IO.Compression.ZipFile]::Open($PackagePath, [System.IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($file in $BuildOutput.Files) {
            $sha256 = Add-CountdownDeployPackageEntry -Archive $archive -SourcePath (Join-Path $RepositoryRoot $file.Path) -EntryName $file.Path
            if ($sha256 -ne $file.Sha256) {
                throw (New-CountdownDeployFailure -Reason 'build-output-modified' -Message "构建输出在套件之后被改动：$($file.Path) 的 SHA-256 与载荷清单不一致，程序目录不是套件测过的那份。重新跑一轮套件，部署它测过的构建输出。")
            }
        }
        [void] (Add-CountdownDeployPackageEntry -Archive $archive -SourcePath $BuildOutput.ManifestPath -EntryName 'app\published-payload.json')
        foreach ($name in $Scripts) {
            [void] (Add-CountdownDeployPackageEntry -Archive $archive -SourcePath (Join-Path $RepositoryRoot $name) -EntryName $name)
        }
    }
    finally { $archive.Dispose() }
    [pscustomobject]@{
        Path = $PackagePath
        Sha256 = (Get-FileHash -LiteralPath $PackagePath -Algorithm SHA256).Hash
        Bytes = (Get-Item -LiteralPath $PackagePath).Length
    }
}

function Write-CountdownDeployScript {
    param(
        [Parameter(Mandatory)] [string] $Path,
        [Parameter(Mandatory)] [string] $Text
    )

    [System.IO.File]::WriteAllText($Path, $Text, $script:Utf8NoBom)
    $Path
}

# 状态读取：仓库里的只读状态命令包进脚本块，带上安装根与任务名，整段交给远端执行入口；办公室上不需要任何项目脚本。
function Write-CountdownDeployStatusScript {
    param(
        [Parameter(Mandatory)] [string] $RepositoryRoot,
        [Parameter(Mandatory)] [string] $Directory,
        [Parameter(Mandatory)] [string] $InstallRoot,
        [Parameter(Mandatory)] [string] $TaskName
    )

    $statusSource = [System.IO.File]::ReadAllText((Join-Path $RepositoryRoot $script:StatusScriptName))
    $text = @(
        'param([object] $Port, [string] $LockName)'
        "`$arguments = @{ InstallRoot = $(ConvertTo-CountdownDeployLiteral -Value $InstallRoot); TaskName = $(ConvertTo-CountdownDeployLiteral -Value $TaskName) }"
        'if ($null -ne $Port) { $arguments.Port = $Port }'
        'if ($LockName) { $arguments.LockName = $LockName }'
        '& {'
        $statusSource.TrimEnd()
        '} @arguments'
    ) -join "`n"
    Write-CountdownDeployScript -Path (Join-Path $Directory 'status.ps1') -Text $text
}

# 经远端执行 port 运行一段脚本，取回它输出的一份 JSON。port 返回 ExitCode 与 Output，其余字段一概不取。
function Invoke-CountdownDeployRemoteScript {
    param(
        [Parameter(Mandatory)] [object] $Port,
        [Parameter(Mandatory)] [string] $ScriptPath,
        [Parameter(Mandatory)] [bool] $WriteOperation,
        [Parameter(Mandatory)] [string] $Name
    )

    $responses = @(& $Port.InvokeRemoteScript $ScriptPath $WriteOperation)
    if ($responses.Count -ne 1) { throw "$Name：远端执行 port 应返回一份结果，实际 $($responses.Count) 份。" }
    $exitCode = Get-CountdownDeployProperty -Object $responses[0] -Name 'ExitCode'
    if ($exitCode -ne 0) { throw "$Name：远端脚本没有成功执行（退出码 $exitCode）。" }
    $output = [string] (Get-CountdownDeployProperty -Object $responses[0] -Name 'Output')
    if ([string]::IsNullOrWhiteSpace($output)) { throw "$Name：远端没有输出。" }
    try { $output | ConvertFrom-Json -DateKind String }
    catch { throw "$Name：远端输出不是一份 JSON。" }
}

# 安装后的状态与预期逐项比对：提交、已安装 EXE 的哈希、经登录任务启动并达到启动就绪的那个进程仍在运行。
function Get-CountdownDeployStatusMismatch {
    param(
        [Parameter(Mandatory)] [object] $Status,
        [Parameter(Mandatory)] [string] $Commit,
        [Parameter(Mandatory)] [string] $ExecutableSha256,
        [AllowNull()] [object] $ReadyProcessId
    )

    $exe = Get-CountdownDeployProperty -Object $Status -Name 'exe'
    $processIds = @(@(Get-CountdownDeployProperty -Object $Status -Name 'processes') | ForEach-Object { [long] (Get-CountdownDeployProperty -Object $_ -Name 'processId') })
    @(
        if (-not [string]::Equals([string] (Get-CountdownDeployProperty -Object $Status -Name 'commit'), $Commit, [System.StringComparison]::OrdinalIgnoreCase)) { 'commit' }
        if (-not [string]::Equals([string] (Get-CountdownDeployProperty -Object $exe -Name 'sha256'), $ExecutableSha256, [System.StringComparison]::OrdinalIgnoreCase) -or
            -not (Test-CountdownDeployTrue (Get-CountdownDeployProperty -Object $exe -Name 'matchesManifest'))) { 'exe' }
        if (-not (Test-CountdownDeployTrue (Get-CountdownDeployProperty -Object $Status -Name 'running')) -or
            $null -eq $ReadyProcessId -or [long] $ReadyProcessId -notin $processIds) { 'process' }
    )
}

# 当前提交已在运行：状态里的提交就是 HEAD，已安装 EXE 与办公室上的载荷清单一致，应用在运行，没有未完成的安装
# （有事务标记时照常部署，安装器先恢复再安装）。提交相同就算，同一提交的另一份构建输出也不重复安装。
function Test-CountdownDeployAlreadyRunning {
    param(
        [Parameter(Mandatory)] [object] $Status,
        [Parameter(Mandatory)] [string] $Commit
    )

    $marker = Get-CountdownDeployProperty -Object $Status -Name 'transactionMarkerPresent'
    [string]::Equals([string] (Get-CountdownDeployProperty -Object $Status -Name 'commit'), $Commit, [System.StringComparison]::OrdinalIgnoreCase) -and
        (Test-CountdownDeployTrue (Get-CountdownDeployProperty -Object (Get-CountdownDeployProperty -Object $Status -Name 'exe') -Name 'matchesManifest')) -and
        (Test-CountdownDeployTrue (Get-CountdownDeployProperty -Object $Status -Name 'running')) -and
        $marker -is [bool] -and -not $marker
}

# 远端安装脚本的信封 → 安装结果：包内安装器的结果 JSON，加上包哈希是否核对通过、安装器是否开始运行、暂存目录是否删掉。
# 安装器没有给出结果时 succeeded 为 null（不知道安装进行到了哪一步），error 是远端脚本的错误。
function ConvertFrom-CountdownDeployInstallEnvelope {
    param([Parameter(Mandatory)] [object] $Envelope)

    $installerText = [string] (Get-CountdownDeployProperty -Object $Envelope -Name 'installer')
    $install = $null
    if (-not [string]::IsNullOrWhiteSpace($installerText)) {
        try { $install = $installerText | ConvertFrom-Json -DateKind String }
        catch { $install = $null }
    }
    if ((Get-CountdownDeployProperty -Object $install -Name 'succeeded') -isnot [bool]) {
        $install = [pscustomobject][ordered]@{ succeeded = $null; failedStage = $null; reason = $null; error = $null }
    }
    $envelopeError = Get-CountdownDeployProperty -Object $Envelope -Name 'error'
    if ($null -ne $envelopeError) {
        $install | Add-Member -NotePropertyName error -NotePropertyValue (ConvertTo-CountdownDeployBoundedText -Text ([string] $envelopeError)) -Force
    }
    foreach ($name in @('packageVerified', 'installerStarted', 'stagingRemoved')) {
        $install | Add-Member -NotePropertyName $name -NotePropertyValue (Test-CountdownDeployTrue (Get-CountdownDeployProperty -Object $Envelope -Name $name)) -Force
    }
    $install
}

function Remove-CountdownDeployWorkDirectory {
    param([Parameter(Mandatory)] [string] $Path)

    try { if (Test-Path -LiteralPath $Path) { [System.IO.Directory]::Delete($Path, $true) } }
    catch { [Console]::Error.WriteLine("[deploy] 本机临时目录没删掉：$Path（$($_.Exception.Message)）") }
}

function Write-CountdownDeployRecord {
    param(
        [Parameter(Mandatory)] [string] $Path,
        [Parameter(Mandatory)] [System.Collections.IDictionary] $Record
    )

    [void] [System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName($Path))
    $temporaryPath = "$Path.tmp"
    [System.IO.File]::WriteAllText($temporaryPath, ($Record | ConvertTo-Json -Depth 12 -EscapeHandling EscapeNonAscii), $script:Utf8NoBom)
    [System.IO.File]::Move($temporaryPath, $Path)
}

# 写本地记录 <记录根>\<运行编号>\deployment-record.json。写不成时：办公室上的结果已经确认、却没留下记录的成功结论改报失败
# （failedStep record、原因 record-write-failed）；没有成功的结论（包括未知）原样保留，只在错误里补一句。
function Complete-CountdownDeployRecord {
    param(
        [Parameter(Mandatory)] [System.Collections.IDictionary] $Result,
        [Parameter(Mandatory)] [string] $RecordRoot
    )

    $Result.finishedAtUtc = [DateTimeOffset]::UtcNow.ToString('o')
    $recordPath = [System.IO.Path]::Join($RecordRoot, $Result.runId, 'deployment-record.json')
    $Result.recordPath = $recordPath
    try { Write-CountdownDeployRecord -Path $recordPath -Record $Result }
    catch {
        $Result.recordPath = $null
        $recordError = "本地部署记录 deployment-record.json 没写成：$($_.Exception.Message)"
        if ($Result.succeeded -eq $true) {
            $Result.outcome = 'failed'
            $Result.succeeded = $false
            $Result.failedStep = 'record'
            $Result.reason = 'record-write-failed'
            $Result.error = ConvertTo-CountdownDeployBoundedText -Text $recordError
        }
        else { $Result.error = ConvertTo-CountdownDeployBoundedText -Text "$($Result.error) $recordError" }
    }
}

function New-CountdownDeployRunId {
    [DateTime]::Now.ToString('yyyyMMddTHHmmssfff') + '-' + [guid]::NewGuid().ToString('N').Substring(0, 8)
}

# 按切换前的备份回退（#287）：一次远端写操作运行办公室备份里的回退脚本，取回它的结果 JSON（结果的 rollback）；本机不需要
# 构建输出，不打包、不传输。结论 outcome：rolled-back（succeeded 为 true）；rejected（另一个安装、切换或回退正在运行，回退什么都
# 没改，nextStep 是等它结束、先读状态）；failed（没有备份，或回退报告失败，nextStep 是回退给的下一步）；unknown（succeeded 为
# null：连接在回退途中断开，或回退开始运行却没给结果）。写一份本地记录。
function Invoke-CountdownDeployLayoutRollback {
    param(
        [Parameter(Mandatory)] [string] $RecordRoot,
        [Parameter(Mandatory)] [string] $RemoteInstallRoot,
        [Parameter(Mandatory)] [string] $TaskName,
        [Parameter(Mandatory)] [object] $Port
    )

    $steps = @('rollback', 'record')
    $runId = New-CountdownDeployRunId
    $result = [ordered]@{
        schemaVersion = 1
        mode = 'layout-rollback'
        outcome = $null
        succeeded = $false
        failedStep = $null
        reason = $null
        error = $null
        nextStep = $null
        runId = $runId
        startedAtUtc = [DateTimeOffset]::UtcNow.ToString('o')
        finishedAtUtc = $null
        rollback = $null
        recordPath = $null
    }
    $workDirectory = [System.IO.Path]::Join([System.IO.Path]::GetTempPath(), "clock-countdown-rollback-$runId")
    try {
        Write-CountdownDeployProgress -Step 'rollback' -Steps $steps -Message '经远端执行入口运行办公室备份里的回退脚本'
        [void] [System.IO.Directory]::CreateDirectory($workDirectory)
        $text = Expand-CountdownDeployTemplate -Template $script:RemoteRollbackScript -Values ([ordered]@{ '__INSTALL_ROOT__' = $RemoteInstallRoot; '__TASK_NAME__' = $TaskName })
        $scriptPath = Write-CountdownDeployScript -Path (Join-Path $workDirectory 'rollback.ps1') -Text $text
        try { $envelope = Invoke-CountdownDeployRemoteScript -Port $Port -ScriptPath $scriptPath -WriteOperation $true -Name '远端回退' }
        catch {
            throw (New-CountdownDeployFailure -Reason 'rollback-result-unknown' -Outcome 'unknown' -Message "$(ConvertTo-CountdownDeploySentence $_.Exception.Message)连接可能在回退途中断开，不知道回退进行到了哪一步。")
        }
        $rollbackText = [string] (Get-CountdownDeployProperty -Object $envelope -Name 'rollback')
        $rollback = $null
        if (-not [string]::IsNullOrWhiteSpace($rollbackText)) {
            try { $rollback = $rollbackText | ConvertFrom-Json -DateKind String }
            catch { $rollback = $null }
        }
        $envelopeError = ConvertTo-CountdownDeployBoundedText -Text ([string] (Get-CountdownDeployProperty -Object $envelope -Name 'error'))
        if ((Get-CountdownDeployProperty -Object $rollback -Name 'succeeded') -isnot [bool]) {
            if (Test-CountdownDeployTrue (Get-CountdownDeployProperty -Object $envelope -Name 'rollbackStarted')) {
                throw (New-CountdownDeployFailure -Reason 'rollback-result-unknown' -Outcome 'unknown' -Message "回退脚本开始运行了，却没有给出结果（$envelopeError），不知道回退进行到了哪一步。")
            }
            throw (New-CountdownDeployFailure -Reason 'rollback-failed' -Message "回退没有开始，办公室没有改动：$envelopeError")
        }
        $result.rollback = $rollback
        if (-not $rollback.succeeded -and [string] (Get-CountdownDeployProperty -Object $rollback -Name 'reason') -cin $script:InProgressReasons) {
            throw (New-CountdownDeployFailure -Reason ([string] $rollback.reason) -Outcome 'rejected' -Message "办公室上另一个安装、切换或回退正在运行，回退没有开始：$(Get-CountdownDeployProperty -Object $rollback -Name 'error')")
        }
        if (-not $rollback.succeeded) {
            $result.nextStep = [string] (Get-CountdownDeployProperty -Object $rollback -Name 'nextStep')
            throw (New-CountdownDeployFailure -Reason 'rollback-failed' -Message ("回退报告失败（阶段 $(Get-CountdownDeployProperty -Object $rollback -Name 'failedStage')，" +
                    "原因 $(Get-CountdownDeployProperty -Object $rollback -Name 'reason')）：$(Get-CountdownDeployProperty -Object $rollback -Name 'error')"))
        }
        $result.succeeded = $true
        $result.outcome = 'rolled-back'
    }
    catch {
        $result.failedStep = 'rollback'
        $reason = $_.Exception.Data['CountdownDeployReason']
        $result.reason = if ($null -ne $reason) { [string] $reason } else { 'unexpected-error' }
        $outcome = $_.Exception.Data['CountdownDeployOutcome']
        $result.outcome = if ($null -ne $outcome) { [string] $outcome } else { 'failed' }
        $result.succeeded = if ($result.outcome -ceq 'unknown') { $null } else { $false }
        $result.error = ConvertTo-CountdownDeployBoundedText -Text $_.Exception.Message
        if ($result.outcome -ceq 'unknown') { $result.nextStep = $script:RollbackReadStatusFirst }
        if ($result.outcome -ceq 'rejected') { $result.nextStep = $script:InProgressNextStep }
    }
    finally { Remove-CountdownDeployWorkDirectory -Path $workDirectory }

    Write-CountdownDeployProgress -Step 'record' -Steps $steps -Message '写本地记录'
    Complete-CountdownDeployRecord -Result $result -RecordRoot $RecordRoot
    $conclusion = switch ($result.outcome) {
        'rolled-back' { '已按切换前的备份回退，旧版本在办公室运行。' }
        'rejected' { "停在 $($result.failedStep)，办公室没有改动：$($result.error)" }
        'unknown' { "停在 $($result.failedStep)，不知道办公室上的结果：$($result.error)" }
        default { "停在 $($result.failedStep)：$($result.error)" }
    }
    Write-CountdownDeployConclusion -Outcome $result.outcome -Message "$conclusion$($result.nextStep)"
    $result
}

function Invoke-CountdownDeployStatusRead {
    param(
        [Parameter(Mandatory)] [string] $RepositoryRoot,
        [Parameter(Mandatory)] [string] $RemoteInstallRoot,
        [Parameter(Mandatory)] [string] $TaskName,
        [Parameter(Mandatory)] [object] $Port
    )

    $result = [ordered]@{ schemaVersion = 1; mode = 'status'; succeeded = $false; failedStep = $null; error = $null; status = $null }
    $workDirectory = [System.IO.Path]::Join([System.IO.Path]::GetTempPath(), 'clock-countdown-status-' + [guid]::NewGuid().ToString('N'))
    try {
        Write-CountdownDeployProgress -Step 'status' -Steps @('status') -Message '只读状态读取'
        [void] [System.IO.Directory]::CreateDirectory($workDirectory)
        $scriptPath = Write-CountdownDeployStatusScript -RepositoryRoot $RepositoryRoot -Directory $workDirectory -InstallRoot $RemoteInstallRoot -TaskName $TaskName
        $result.status = Invoke-CountdownDeployRemoteScript -Port $Port -ScriptPath $scriptPath -WriteOperation $false -Name '状态读取'
        $result.succeeded = $true
    }
    catch {
        $result.failedStep = 'status'
        $result.error = ConvertTo-CountdownDeployBoundedText -Text $_.Exception.Message
    }
    finally { Remove-CountdownDeployWorkDirectory -Path $workDirectory }
    Write-CountdownDeployConclusion -Message $(if ($result.succeeded) { '状态读取完成。' } else { "状态读取失败：$($result.error)" })
    $result
}

function Invoke-CountdownDeployment {
    <#
    .SYNOPSIS
    部署入口的编排：部署、只查状态，或一次性的布局切换与它的回退（#287）。返回结果（由入口脚本输出为 JSON）；进度写到标准错误。
    .DESCRIPTION
    部署成功只看安装后的只读状态读取：提交是 HEAD、已安装 EXE 的哈希等于本机构建输出、达到启动就绪的进程仍在运行。
    连通性探测本身是一次只读状态读取（结果的 statusBefore）；它显示当前提交已在运行、没有未完成的安装时，结论是
    already-running，不传输、不安装。结论 outcome：deployed、already-running（succeeded 为 true）；rejected（连接远端之前停下，
    或办公室上另一个安装、切换或回退正在运行、包内入口什么都没改，后者 nextStep 是等它结束、先读状态）、failed（succeeded 为 false）；unknown（succeeded 为 null：拿不到安装结果或安装后的状态，不知道办公室上的结果）。
    failedStep 是停下的步骤，reason 是原因类别，error 写明缺什么或哪里不对；安装失败时 rollback 是安装器报告的回滚结果
    （not-needed、rolled-back、rollback-failed，没报时为 null）；办公室现状不确定时 nextStep 提示先查状态。
    预检通过后无论成败都写一份本地部署记录（<RecordRoot>\<运行编号>\deployment-record.json）：提交、包哈希、安装结果、
    状态快照与传输账本引用。本机的包和办公室的暂存目录都不保留。只查状态时不写任何东西。
    -LayoutSwitch 走同一流程（mode 为 layout-switch），包里另带切换脚本，安装一步运行它，结果的 install 是切换脚本的结果；
    探测读到当前提交已在运行也照常切换；切换失败时 nextStep 带上切换脚本给的下一步。-LayoutRollback 见
    Invoke-CountdownDeployLayoutRollback。三种模式只能选一种，同时给出时拒绝（reason 为 conflicting-modes），不连接办公室。
    -Port 需要 InvokeRemoteScript（脚本路径、是否写操作 → ExitCode、Output）与 SendFile（本地文件、远端路径、
    期望 SHA-256、尝试编号 → Status、AttemptId、Sha256、Bytes、LedgerPath；远端目录由它建出，账本位置由它决定）。
    #>
    [CmdletBinding()]
    param(
        [switch] $StatusOnly,
        [switch] $LayoutSwitch,
        [switch] $LayoutRollback,
        [Parameter(Mandatory)] [string] $RepositoryRoot,
        [Parameter(Mandatory)] [string] $RecordRoot,
        [Parameter(Mandatory)] [string] $RemoteInstallRoot,
        [Parameter(Mandatory)] [string] $TaskName,
        [Parameter(Mandatory)] [string] $RemoteStagingRoot,
        [Parameter(Mandatory)] [object] $Port
    )

    $modes = @(@{ '-StatusOnly' = $StatusOnly; '-LayoutSwitch' = $LayoutSwitch; '-LayoutRollback' = $LayoutRollback }.GetEnumerator() |
            Where-Object { $_.Value } | ForEach-Object { $_.Key } | Sort-Object)
    if ($modes.Count -gt 1) {
        $rejected = [ordered]@{
            schemaVersion = 1
            mode = $null
            outcome = 'rejected'
            succeeded = $false
            failedStep = 'preflight'
            reason = 'conflicting-modes'
            error = "一次只能选一种模式，却同时给了 $($modes -join '、')。"
            nextStep = $null
        }
        Write-CountdownDeployConclusion -Outcome $rejected.outcome -Message "停在 preflight，没有连接办公室：$($rejected.error)"
        return $rejected
    }
    if ($StatusOnly) {
        return Invoke-CountdownDeployStatusRead -RepositoryRoot $RepositoryRoot -RemoteInstallRoot $RemoteInstallRoot -TaskName $TaskName -Port $Port
    }
    if ($LayoutRollback) {
        return Invoke-CountdownDeployLayoutRollback -RecordRoot $RecordRoot -RemoteInstallRoot $RemoteInstallRoot -TaskName $TaskName -Port $Port
    }

    # 包内入口：安装器，布局切换时是切换脚本。
    $entryName = if ($LayoutSwitch) { $script:LayoutSwitchScript } else { $script:InstallerFiles[0] }
    $entryLabel = if ($LayoutSwitch) { '切换脚本' } else { '安装器' }
    $runId = New-CountdownDeployRunId
    $result = [ordered]@{
        schemaVersion = 1
        mode = if ($LayoutSwitch) { 'layout-switch' } else { 'deploy' }
        outcome = $null
        succeeded = $false
        failedStep = $null
        reason = $null
        error = $null
        rollback = $null
        nextStep = $null
        runId = $runId
        startedAtUtc = [DateTimeOffset]::UtcNow.ToString('o')
        finishedAtUtc = $null
        commit = $null
        suiteRun = $null
        buildOutput = $null
        package = $null
        remoteStagingDirectory = $null
        statusBefore = $null
        transfer = $null
        install = $null
        status = $null
        statusMismatches = @()
        recordPath = $null
    }
    $step = 'preflight'
    $workDirectory = [System.IO.Path]::Join([System.IO.Path]::GetTempPath(), "clock-countdown-deploy-$runId")
    $packagePath = [System.IO.Path]::Join($workDirectory, 'package.zip')
    $preflightPassed = $false
    $remoteContacted = $false
    try {
        Write-CountdownDeployProgress -Step 'preflight' -Message '本机预检：main、工作树干净、已推送、套件记录与构建回执'
        $preflight = Get-CountdownDeployPreflight -RepositoryRoot $RepositoryRoot
        $result.commit = $preflight.Commit
        $result.suiteRun = [ordered]@{ runId = $preflight.SuiteRun.RunId; path = $preflight.SuiteRun.Path }
        $result.buildOutput = [ordered]@{
            payloadAggregateSha256 = $preflight.BuildOutput.AggregateSha256
            executableSha256 = $preflight.BuildOutput.ExecutableSha256
            fileCount = @($preflight.BuildOutput.Files).Count
        }
        $preflightPassed = $true

        $step = 'package'
        $packageScripts = @($script:InstallerFiles) + @(if ($LayoutSwitch) { $script:LayoutSwitchScript })
        Write-CountdownDeployProgress -Step 'package' -Message "把程序目录、载荷清单和$(if ($LayoutSwitch) { '安装器、切换脚本' } else { '安装器' })打成一个包"
        [void] [System.IO.Directory]::CreateDirectory($workDirectory)
        $package = New-CountdownDeployPackage -RepositoryRoot $RepositoryRoot -BuildOutput $preflight.BuildOutput -PackagePath $packagePath -Scripts $packageScripts
        $result.package = [ordered]@{ sha256 = $package.Sha256; bytes = $package.Bytes; retained = $true }

        # 连通性探测就是一次只读状态读取：连得上，也知道办公室现在运行的是什么。安装后的状态读取用同一份脚本。
        $step = 'probe'
        Write-CountdownDeployProgress -Step 'probe' -Message '经远端执行入口做只读连通性探测：读一次办公室的安装状态'
        $statusScript = Write-CountdownDeployStatusScript -RepositoryRoot $RepositoryRoot -Directory $workDirectory -InstallRoot $RemoteInstallRoot -TaskName $TaskName
        $remoteContacted = $true
        try {
            $result.statusBefore = Invoke-CountdownDeployRemoteScript -Port $Port -ScriptPath $statusScript -WriteOperation $false -Name '连通性探测'
            if ((Get-CountdownDeployProperty -Object $result.statusBefore -Name 'schemaVersion') -ne 1) { throw '连通性探测没有读到办公室的安装状态。' }
        }
        catch { throw (New-CountdownDeployFailure -Reason 'probe-failed' -Message "$(ConvertTo-CountdownDeploySentence $_.Exception.Message)部署停在传输之前，办公室没有改动。") }

        # 布局切换不看这一条：现有布局里运行的可能正是当前提交，切换照常进行。
        if (-not $LayoutSwitch -and (Test-CountdownDeployAlreadyRunning -Status $result.statusBefore -Commit $preflight.Commit)) {
            $result.succeeded = $true
            $result.outcome = 'already-running'
        }
        else {
            $step = 'transfer'
            $staging = [System.IO.Path]::Join($RemoteStagingRoot, "clock-countdown-$runId")
            $result.remoteStagingDirectory = $staging
            Write-CountdownDeployProgress -Step 'transfer' -Message "经传输入口送到暂存目录 clock-countdown-$runId 并核对远端哈希"
            # 传输失败时安装还没开始：办公室的安装没有改动，暂存目录里可能留下不完整的包。
            try {
                $sent = @(& $Port.SendFile $packagePath ([System.IO.Path]::Join($staging, 'package.zip')) $package.Sha256 $runId)
                if ($sent.Count -ne 1) { throw "传输 port 应返回一份结果，实际 $($sent.Count) 份。" }
                $remoteSha256 = [string] (Get-CountdownDeployProperty -Object $sent[0] -Name 'Sha256')
                $result.transfer = [ordered]@{
                    attemptId = [string] (Get-CountdownDeployProperty -Object $sent[0] -Name 'AttemptId')
                    status = [string] (Get-CountdownDeployProperty -Object $sent[0] -Name 'Status')
                    remoteSha256 = $remoteSha256.ToUpperInvariant()
                    bytes = Get-CountdownDeployProperty -Object $sent[0] -Name 'Bytes'
                    ledgerPath = [string] (Get-CountdownDeployProperty -Object $sent[0] -Name 'LedgerPath')
                }
                if ($result.transfer.status -cne 'succeeded') { throw "传输没有成功：$($result.transfer.status)。" }
                if (-not [string]::Equals($remoteSha256, $package.Sha256, [System.StringComparison]::OrdinalIgnoreCase)) { throw '远端文件的哈希与包哈希不一致。' }
            }
            catch { throw (New-CountdownDeployFailure -Reason 'transfer-failed' -Message "$(ConvertTo-CountdownDeploySentence $_.Exception.Message)没有运行安装，办公室的安装没有改动。") }

            $step = 'install'
            Write-CountdownDeployProgress -Step 'install' -Message "经远端执行入口核对包哈希、解包并运行包内$(if ($LayoutSwitch) { '切换脚本：备份、经新安装器换布局、移除遗留' } else { '安装器' })"
            $installText = Expand-CountdownDeployTemplate -Template $script:RemoteInstallScript -Values ([ordered]@{
                    '__STAGING__' = $staging; '__SHA256__' = $package.Sha256; '__ENTRY__' = $entryName
                    '__INSTALL_ROOT__' = $RemoteInstallRoot; '__TASK_NAME__' = $TaskName
                })
            $installScript = Write-CountdownDeployScript -Path (Join-Path $workDirectory 'install.ps1') -Text $installText
            # 远端安装脚本总会输出一份信封；拿不到它（连接断开、入口失败、输出不全）就不知道安装进行到了哪一步。
            try { $envelope = Invoke-CountdownDeployRemoteScript -Port $Port -ScriptPath $installScript -WriteOperation $true -Name '远端安装' }
            catch {
                throw (New-CountdownDeployFailure -Reason 'install-result-unknown' -Outcome 'unknown' -Message "$(ConvertTo-CountdownDeploySentence $_.Exception.Message)连接可能在安装途中断开，不知道安装进行到了哪一步。")
            }
            $result.install = ConvertFrom-CountdownDeployInstallEnvelope -Envelope $envelope
            if ($null -eq $result.install.succeeded) {
                if ($result.install.installerStarted) {
                    throw (New-CountdownDeployFailure -Reason 'install-result-unknown' -Outcome 'unknown' -Message "$($entryLabel)开始运行了，却没有给出结果（$($result.install.error)），不知道安装进行到了哪一步。")
                }
                $result.rollback = 'not-needed'
                throw (New-CountdownDeployFailure -Reason 'install-failed' -Message "包没能交给$($entryLabel)（$($result.install.error)）：$($entryLabel)没有运行，办公室的安装没有改动。")
            }
            # 办公室上另一个安装、切换或回退正在运行：包内入口在全机安装锁处什么都没改就停下。
            if (-not $result.install.succeeded -and [string] (Get-CountdownDeployProperty -Object $result.install -Name 'reason') -cin $script:InProgressReasons) {
                $result.rollback = 'not-needed'
                throw (New-CountdownDeployFailure -Reason ([string] $result.install.reason) -Outcome 'rejected' -Message "办公室上另一个安装、切换或回退正在运行，包内$($entryLabel)什么都没改：$(Get-CountdownDeployProperty -Object $result.install -Name 'error')")
            }
            if (-not $result.install.succeeded) {
                $rollback = [string] (Get-CountdownDeployProperty -Object (Get-CountdownDeployProperty -Object $result.install -Name 'rollback') -Name 'outcome')
                $result.rollback = if ($rollback -cin @($script:RollbackOutcomes.Keys)) { $rollback } else { $null }
                $rollbackText = if ($null -ne $result.rollback) { $script:RollbackOutcomes[$result.rollback] } else { "$($entryLabel)没有报告回滚结果。" }
                # 回滚结果放在安装器的错误前面：错误很长、整句被截断时也不丢。
                throw (New-CountdownDeployFailure -Reason 'install-failed' -Message ("包内$($entryLabel)报告失败（阶段 $(Get-CountdownDeployProperty -Object $result.install -Name 'failedStage')，" +
                        "原因 $(Get-CountdownDeployProperty -Object $result.install -Name 'reason')）。$($rollbackText)$($entryLabel)的错误：$(Get-CountdownDeployProperty -Object $result.install -Name 'error')"))
            }

            $step = 'status'
            Write-CountdownDeployProgress -Step 'status' -Message '只读状态读取：提交、EXE 哈希与运行中的进程'
            # 成功只看这次状态读取：读不到就确认不了办公室在运行什么，结果未知。
            try { $result.status = Invoke-CountdownDeployRemoteScript -Port $Port -ScriptPath $statusScript -WriteOperation $false -Name '安装后的状态读取' }
            catch {
                throw (New-CountdownDeployFailure -Reason 'status-unreadable' -Outcome 'unknown' -Message "$($entryLabel)报告成功，但$(ConvertTo-CountdownDeploySentence $_.Exception.Message)确认不了办公室现在运行的是什么。")
            }
            $readyProcess = Get-CountdownDeployProperty -Object $result.install -Name 'readyProcess'
            $result.statusMismatches = @(Get-CountdownDeployStatusMismatch -Status $result.status -Commit $preflight.Commit -ExecutableSha256 $preflight.BuildOutput.ExecutableSha256 `
                    -ReadyProcessId (Get-CountdownDeployProperty -Object $readyProcess -Name 'processId'))
            if ($result.statusMismatches.Count -gt 0) {
                throw (New-CountdownDeployFailure -Reason 'status-mismatch' -Message "$($entryLabel)报告成功，但安装后的状态与预期不一致：$($result.statusMismatches -join '、')。")
            }
            $result.succeeded = $true
            $result.outcome = 'deployed'
        }
    }
    catch {
        $result.failedStep = $step
        $reason = $_.Exception.Data['CountdownDeployReason']
        $result.reason = if ($null -ne $reason) { [string] $reason } else { 'unexpected-error' }
        # 连接远端之前停下的是拒绝：办公室什么都没收到。不知道办公室上发生了什么时是未知：既不算成功，也不算失败。
        $outcome = $_.Exception.Data['CountdownDeployOutcome']
        $result.outcome = if ($null -ne $outcome) { [string] $outcome } elseif ($remoteContacted) { 'failed' } else { 'rejected' }
        $result.succeeded = if ($result.outcome -ceq 'unknown') { $null } else { $false }
        $result.error = ConvertTo-CountdownDeployBoundedText -Text $_.Exception.Message
    }
    finally {
        Remove-CountdownDeployWorkDirectory -Path $workDirectory
        if ($null -ne $result.package) { $result.package.retained = Test-Path -LiteralPath $packagePath }
    }
    # 办公室现状不确定时提示先查状态：结果未知，或安装失败而回滚失败、安装器没有报告回滚结果。
    if ($result.outcome -ceq 'unknown' -or ($result.failedStep -ceq 'install' -and $result.rollback -cnotin @('rolled-back', 'not-needed'))) {
        $result.nextStep = $script:ReadStatusFirst
    }
    $inProgress = $remoteContacted -and $result.outcome -ceq 'rejected'
    if ($inProgress) { $result.nextStep = $script:InProgressNextStep }
    # 切换脚本可能运行过、切换却没有成功时：先是切换脚本自己给的下一步（备份在不在、能不能接着做），再说经部署入口怎么做。
    # 停在传输之前（探测、传输失败）时切换没有开始，没有这一句；另一个运行在进行时只说等它结束、先读状态。
    if ($LayoutSwitch -and $result.failedStep -cin @('install', 'status') -and -not $inProgress) {
        $switchNextStep = [string] (Get-CountdownDeployProperty -Object $result.install -Name 'nextStep')
        $result.nextStep = (@($switchNextStep, $result.nextStep, $script:LayoutSwitchNextStep) | Where-Object { $_ }) -join ''
    }

    if ($preflightPassed) {
        Write-CountdownDeployProgress -Step 'record' -Message '写本地部署记录'
        Complete-CountdownDeployRecord -Result $result -RecordRoot $RecordRoot
    }
    else { $result.finishedAtUtc = [DateTimeOffset]::UtcNow.ToString('o') }
    $conclusion = switch ($result.outcome) {
        { $_ -ceq 'deployed' -and $LayoutSwitch } { "布局切换完成，提交 $($result.commit.Substring(0, 7)) 以新布局在办公室运行；切换前的备份留在办公室上。"; break }
        'deployed' { "部署成功，提交 $($result.commit.Substring(0, 7)) 正在办公室运行。" }
        'already-running' { "提交 $($result.commit.Substring(0, 7)) 已在办公室运行，没有重复安装。" }
        'rejected' { if ($inProgress) { "停在 $($result.failedStep)，办公室没有改动：$($result.error)" } else { "停在 $($result.failedStep)，没有连接办公室：$($result.error)" } }
        'unknown' { "停在 $($result.failedStep)，不知道办公室上的结果：$($result.error)" }
        default { "停在 $($result.failedStep)：$($result.error)" }
    }
    Write-CountdownDeployConclusion -Outcome $result.outcome -Message "$conclusion$($result.nextStep)"
    $result
}

function New-CountdownDeployerHostPorts {
    <#
    .SYNOPSIS
    生产 port：接主机受管的远端执行入口（Invoke-RemoteStep.ps1）与传输入口（Invoke-OfficeScpTransfer.ps1）。
    .DESCRIPTION
    连接参数与凭据由两个入口自己加载，项目里不出现主机名、地址、账号或凭据。入口的全部输出流都收进来，只解析最后一行
    JSON 摘要并只取已知字段；入口失败时只报异常类型：原始输出与异常文本可能带出主机名。传输账本按尝试编号放在 -LedgerRoot 下，
    属于主机的传输记账，不放进项目的部署记录目录（传输入口的远端哈希探针输出里有远端计算机名）。scp 不建远端目录，
    送包前先以写操作建出暂存目录（已存在就拒绝）。
    #>
    [CmdletBinding()]
    param(
        [string] $RemoteStepEntry = 'D:\Codex\.codex\scripts\Invoke-RemoteStep.ps1',
        [string] $TransferEntry = 'D:\Codex\.codex\skills\ssh-transfer-ledger\scripts\Invoke-OfficeScpTransfer.ps1',
        [string] $LedgerRoot = 'D:\agents\run-evidence\clock-countdown-transfers',
        [ValidateRange(1, 86400)] [int] $TimeoutSeconds = 900
    )

    $field = { param($Object, [string] $Name) if ($null -ne $Object) { $property = $Object.PSObject.Properties[$Name]; if ($property) { $property.Value } } }
    $readSummary = {
        param([object[]] $Lines, [string] $Name)
        $last = @($Lines | ForEach-Object { ([string] $_).Trim() } | Where-Object { $_.StartsWith('{') }) | Select-Object -Last 1
        if ($null -eq $last) { throw "$Name 没有输出 JSON 摘要。" }
        try { $last | ConvertFrom-Json -DateKind String }
        catch { throw "$Name 的摘要不是 JSON。" }
    }
    $invokeRemote = {
        param([string] $ScriptPath, [bool] $WriteOperation)
        $ErrorActionPreference = 'Stop'
        $arguments = @{ Target = 'office'; ScriptPath = $ScriptPath; TimeoutSeconds = $TimeoutSeconds; IncludeOutput = $true }
        if ($WriteOperation) { $arguments.WriteOperation = $true }
        try { $lines = @(& $RemoteStepEntry @arguments *>&1) }
        catch { throw "远端执行入口失败：$($_.Exception.GetType().Name)" }
        $summary = & $readSummary $lines '远端执行入口'
        $exitCode = & $field $summary 'exitCode'
        $ok = [string] (& $field $summary 'status') -ceq 'ok' -and $exitCode -eq 0 -and -not [bool] (& $field $summary 'outputTruncated')
        [pscustomobject]@{
            ExitCode = if ($ok) { 0 } elseif ($null -ne $exitCode -and $exitCode -ne 0) { [int] $exitCode } else { 1 }
            Output = [string] (& $field $summary 'output')
        }
    }.GetNewClosure()
    $sendFile = {
        param([string] $SourcePath, [string] $RemotePath, [string] $ExpectedSha256, [string] $AttemptId)
        $ErrorActionPreference = 'Stop'
        $ledgerDirectory = [System.IO.Path]::Join($LedgerRoot, $AttemptId)
        [void] [System.IO.Directory]::CreateDirectory($ledgerDirectory)
        $remoteDirectory = "'" + [System.Management.Automation.Language.CodeGeneration]::EscapeSingleQuotedStringContent([System.IO.Path]::GetDirectoryName($RemotePath)) + "'"
        $createScript = [System.IO.Path]::Join($ledgerDirectory, 'create-staging.ps1')
        [System.IO.File]::WriteAllText($createScript, (@(
                    "if (`$env:CODEX_REMOTE_EXECUTION_TARGET -ne 'office') { throw '只能经远端执行入口在办公室上运行。' }"
                    "`$path = $remoteDirectory"
                    "if (Test-Path -LiteralPath `$path) { throw '远端暂存目录已存在。' }"
                    '[void] [System.IO.Directory]::CreateDirectory($path)'
                    "'{""created"":true}'"
                ) -join "`n"), [System.Text.UTF8Encoding]::new($false))
        $created = & $invokeRemote $createScript $true
        if ($created.ExitCode -ne 0) { throw "建远端暂存目录失败（退出码 $($created.ExitCode)）。" }
        $arguments = @{
            SourcePath = $SourcePath
            RemotePath = $RemotePath.Replace('\', '/')
            AttemptId = $AttemptId
            ExpectedHash = $ExpectedSha256.ToLowerInvariant()
            LedgerPath = [System.IO.Path]::Join($ledgerDirectory, 'transfer-attempts.json')
        }
        try { $lines = @(& $TransferEntry @arguments *>&1) }
        catch { throw "传输入口失败：$($_.Exception.GetType().Name)" }
        $summary = & $readSummary $lines '传输入口'
        [pscustomobject]@{
            Status = [string] (& $field $summary 'status')
            AttemptId = [string] (& $field $summary 'attemptId')
            Sha256 = [string] (& $field $summary 'sha256')
            Bytes = & $field $summary 'bytes'
            LedgerPath = [string] (& $field $summary 'ledger')
        }
    }.GetNewClosure()
    [pscustomobject]@{ InvokeRemoteScript = $invokeRemote; SendFile = $sendFile }
}

Export-ModuleMember -Function Invoke-CountdownDeployment, New-CountdownDeployerHostPorts
