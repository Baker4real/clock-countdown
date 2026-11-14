#requires -Version 7.0

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# 新部署入口（#259 / #285）：把已推送的 main 当前提交部署到办公室，送过去的就是本机套件测过的那份构建输出。
# 部署：本机预检 → 打包 → 只读连通性探测 → 传输到按运行编号命名的暂存目录 → 远端核对包哈希、解包并运行包内安装器 →
# 只读状态读取（提交、EXE 哈希与运行中的进程都与预期一致才算成功）→ 本地部署记录。只查状态：一次只读状态读取，不做任何改动。
# 远端执行与传输经 port 调用：生产用 New-CountdownDeployerHostPorts 接主机受管的远端执行入口与传输入口，测试注入假 port。
# port 的返回值只取已知字段，结果与记录里没有主机名、地址、账号、凭据或连接参数。
# #285 只走成功路径；预检拒绝、探测失败、安装失败与回滚、结果未知、当前提交已在运行等在 #286。

Import-Module (Join-Path $PSScriptRoot 'tools\Countdown.SuiteStages.psm1')

$script:ExecutableManifestPath = 'app\MinminCountdown.exe'
$script:InstallerFiles = @('Install-CountdownProgram.ps1', 'Countdown.Installer.psm1')
$script:StatusScriptName = 'Get-CountdownInstallStatus.ps1'
$script:DeploySteps = @('preflight', 'package', 'probe', 'transfer', 'install', 'status', 'record')
$script:Utf8NoBom = [System.Text.UTF8Encoding]::new($false)

# 写操作脚本的第一条语句（param 块之后）必须是对执行目标的断言：主机的远端执行入口缺了它就拒绝执行。
$script:RemoteInstallScript = @'
param([object] $Port, [string] $LockName)
if ($env:CODEX_REMOTE_EXECUTION_TARGET -ne 'office') { throw '安装脚本只能经远端执行入口在办公室上运行。' }
$ErrorActionPreference = 'Stop'
$staging = __STAGING__
$packagePath = Join-Path $staging 'package.zip'
$packageRoot = Join-Path $staging 'package'
$envelope = [ordered]@{ packageVerified = $false; installer = $null; stagingRemoved = $false; error = $null }
try {
    if ((Get-FileHash -LiteralPath $packagePath -Algorithm SHA256).Hash -ne __SHA256__) { throw '暂存目录里的包与部署端算出的包哈希不一致。' }
    $envelope.packageVerified = $true
    [System.IO.Compression.ZipFile]::ExtractToDirectory($packagePath, $packageRoot)
    $arguments = @{ PackageRoot = $packageRoot; InstallRoot = __INSTALL_ROOT__; TaskName = __TASK_NAME__ }
    if ($null -ne $Port) { $arguments.Port = $Port }
    if ($LockName) { $arguments.LockName = $LockName }
    $envelope.installer = @(& (Join-Path $packageRoot 'Install-CountdownProgram.ps1') @arguments) -join "`n"
}
catch { $envelope.error = ($_.Exception.Message -replace '\s+', ' ').Trim() }
finally {
    # 包和暂存内容不长期保留：安装器已把程序目录复制到安装根，暂存目录随即删除。
    try { if (Test-Path -LiteralPath $staging) { [System.IO.Directory]::Delete($staging, $true) } } catch { }
    $envelope.stagingRemoved = -not (Test-Path -LiteralPath $staging)
}
$envelope | ConvertTo-Json -Compress -EscapeHandling EscapeNonAscii
'@

$script:RemoteProbeScript = @'
# 部署入口的连通性探测：只读，不读写任何文件。
[ordered]@{ probe = 'ok'; powershell = $PSVersionTable.PSVersion.ToString() } | ConvertTo-Json -Compress
'@

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

function ConvertTo-CountdownDeployBoundedText {
    param([AllowNull()] [AllowEmptyString()] [string] $Text)

    $value = ([string] $Text -replace '\s+', ' ').Trim()
    if ($value.Length -gt 400) { $value = $value.Substring(0, 400) + '…' }
    $value
}

function ConvertTo-CountdownDeployLiteral {
    param([AllowEmptyString()] [string] $Value)
    "'" + [System.Management.Automation.Language.CodeGeneration]::EscapeSingleQuotedStringContent($Value) + "'"
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

function Write-CountdownDeployConclusion {
    param([Parameter(Mandatory)] [string] $Message)
    [Console]::Error.WriteLine(('[deploy] {0:HH:mm:ss} 结果：{1}' -f [DateTime]::Now, $Message))
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

# 套件记录：同一提交、本 checkout、整轮通过，开跑与收尾时工作树都干净且 HEAD 不变，启动器的必过阶段全部通过，
# 干净构建（CleanR2R）的回执带构建输出的聚合哈希。取最新的合格记录。
function Find-CountdownDeploySuiteRun {
    param(
        [Parameter(Mandatory)] [string] $RepositoryRoot,
        [Parameter(Mandatory)] [string] $Commit
    )

    $directory = Join-Path $RepositoryRoot 'logs\suite-runs'
    $repositoryPath = [System.IO.Path]::GetFullPath($RepositoryRoot).TrimEnd('\')
    $requiredStages = [string[]] @(Get-CountdownRequiredSuiteStage)
    $rejections = [System.Collections.Generic.List[string]]::new()
    $runFiles = @()
    if (Test-Path -LiteralPath $directory -PathType Container) {
        $runFiles = @(Get-ChildItem -LiteralPath $directory -Filter '*-run.json' -File | Sort-Object Name -Descending)
    }
    foreach ($runFile in $runFiles) {
        try { $run = [System.IO.File]::ReadAllText($runFile.FullName) | ConvertFrom-Json -DateKind String }
        catch { continue }
        $source = Get-CountdownDeployProperty -Object $run -Name 'Source'
        if (-not [string]::Equals([string] (Get-CountdownDeployProperty -Object $source -Name 'Commit'), $Commit, [System.StringComparison]::OrdinalIgnoreCase)) { continue }
        try {
            if ((Get-CountdownDeployProperty -Object $run -Name 'SchemaVersion') -ne 1) { throw '记录格式不受支持。' }
            $runRoot = [string] (Get-CountdownDeployProperty -Object $run -Name 'RepositoryRoot')
            if ([string]::IsNullOrWhiteSpace($runRoot) -or
                -not [string]::Equals([System.IO.Path]::GetFullPath($runRoot).TrimEnd('\'), $repositoryPath, [System.StringComparison]::OrdinalIgnoreCase)) {
                throw '记录不属于这个 checkout。'
            }
            if ((Get-CountdownDeployProperty -Object $run -Name 'Status') -cne 'Completed' -or (Get-CountdownDeployProperty -Object $run -Name 'ExitCode') -ne 0) {
                throw "套件没有通过（$(Get-CountdownDeployProperty -Object $run -Name 'Status')）。"
            }
            if (-not (Test-CountdownDeployTrue (Get-CountdownDeployProperty -Object $source -Name 'CleanAtStart')) -or
                -not (Test-CountdownDeployTrue (Get-CountdownDeployProperty -Object $source -Name 'CleanAtEnd')) -or
                -not [string]::Equals([string] (Get-CountdownDeployProperty -Object $source -Name 'CommitAtEnd'), $Commit, [System.StringComparison]::OrdinalIgnoreCase)) {
                throw '套件开跑或收尾时工作树不干净，或 HEAD 已变化。'
            }
            $stages = @(Get-CountdownDeployProperty -Object $run -Name 'Stages')
            foreach ($name in @($requiredStages) + @('Build')) {
                $records = @($stages | Where-Object { [string] (Get-CountdownDeployProperty -Object $_ -Name 'Stage') -ceq $name })
                if ($records.Count -ne 1 -or (Get-CountdownDeployProperty -Object $records[0] -Name 'Status') -cne 'Completed' -or
                    (Get-CountdownDeployProperty -Object $records[0] -Name 'ExitCode') -ne 0) {
                    throw "必过阶段 $name 缺失或没有通过。"
                }
            }
            $build = @($stages | Where-Object { [string] (Get-CountdownDeployProperty -Object $_ -Name 'Stage') -ceq 'Build' })[0]
            $receipt = Get-CountdownDeployProperty -Object $build -Name 'BuildReceipt'
            if ((Get-CountdownDeployProperty -Object $receipt -Name 'SchemaVersion') -ne 1 -or
                (Get-CountdownDeployProperty -Object $receipt -Name 'Mode') -cne 'CleanR2R' -or
                -not (Test-CountdownDeployTrue (Get-CountdownDeployProperty -Object $receipt -Name 'Clean')) -or
                -not (Test-CountdownDeployTrue (Get-CountdownDeployProperty -Object $receipt -Name 'ReadyToRun'))) {
                throw '构建回执不是完整的干净构建（CleanR2R）。'
            }
            $aggregate = [string] (Get-CountdownDeployProperty -Object $receipt -Name 'PayloadAggregateSha256')
            if ($aggregate -notmatch '^[0-9A-Fa-f]{64}$') { throw '构建回执里没有构建输出的聚合哈希：用当前的构建脚本重跑套件。' }
            return [pscustomobject]@{
                RunId = [string] (Get-CountdownDeployProperty -Object $run -Name 'RunId')
                Path = $runFile.FullName
                PayloadAggregateSha256 = $aggregate.ToUpperInvariant()
            }
        }
        catch { $rejections.Add("$($runFile.Name)：$($_.Exception.Message)") }
    }
    $detail = if ($rejections.Count -gt 0) { "同一提交的记录都不合格：$(@($rejections | Select-Object -First 3) -join '；')" } else { '没有同一提交的套件记录。' }
    throw "提交 $($Commit.Substring(0, 7)) 没有可用的套件记录（$detail）先在干净的 main 上运行 pwsh -NoLogo -NoProfile -File ./tools/Invoke-CountdownSuiteLowImpact.ps1 -Stage $($requiredStages -join ',')。"
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
    if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) { throw "没有本机构建输出的载荷清单：$manifestPath" }
    try { $document = [System.IO.File]::ReadAllText($manifestPath) | ConvertFrom-Json }
    catch { throw '本机构建输出的载荷清单不是有效 JSON。' }
    if ((Get-CountdownDeployProperty -Object $document -Name 'schemaVersion') -ne 1) { throw '本机构建输出的载荷清单 schemaVersion 不是 1。' }
    $manifestCommit = [string] (Get-CountdownDeployProperty -Object $document -Name 'commit')
    if (-not [string]::Equals($manifestCommit, $Commit, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "本机构建输出不是提交 $($Commit.Substring(0, 7)) 构建的（载荷清单记的是 $manifestCommit）。"
    }
    $entries = [System.Collections.Generic.List[object]]::new()
    $seen = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    $aggregateInput = [System.Text.StringBuilder]::new()
    foreach ($file in @(Get-CountdownDeployProperty -Object $document -Name 'files')) {
        $path = [string] (Get-CountdownDeployProperty -Object $file -Name 'path')
        $sha256 = [string] (Get-CountdownDeployProperty -Object $file -Name 'sha256')
        if (-not (Test-CountdownDeployManifestPath -Path $path) -or $sha256 -notmatch '^[0-9A-Fa-f]{64}$' -or -not $seen.Add($path)) {
            throw "本机构建输出的载荷清单条目无效：$path"
        }
        $entries.Add([pscustomobject]@{ Path = $path; Sha256 = $sha256.ToUpperInvariant() })
        [void] $aggregateInput.Append($path).Append([char] 0).Append($sha256).Append([char] 10)
    }
    if ($entries.Count -eq 0) { throw '本机构建输出的载荷清单没有文件。' }
    if (-not $seen.Contains($script:ExecutableManifestPath)) { throw "本机构建输出的载荷清单没有 $($script:ExecutableManifestPath)。" }
    $aggregate = [Convert]::ToHexString([System.Security.Cryptography.SHA256]::HashData([System.Text.Encoding]::UTF8.GetBytes($aggregateInput.ToString())))
    if (-not [string]::Equals($aggregate, [string] (Get-CountdownDeployProperty -Object $document -Name 'aggregateSha256'), [System.StringComparison]::OrdinalIgnoreCase)) {
        throw '本机构建输出的载荷清单聚合哈希与文件列表不一致。'
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
    if ($branch.ExitCode -ne 0 -or $branch.Text -cne 'main') { throw "只部署 main：当前不在 main 分支（$($branch.Text)）。" }
    $status = Invoke-CountdownDeployGit -Root $RepositoryRoot -Arguments @('--no-optional-locks', 'status', '--porcelain=v1', '--untracked-files=all')
    if ($status.ExitCode -ne 0) { throw "读不出工作树状态：$($status.Error)" }
    if ($status.Lines.Count -gt 0) { throw "工作树不干净：有 $($status.Lines.Count) 项未提交的改动。" }
    $head = Invoke-CountdownDeployGit -Root $RepositoryRoot -Arguments @('rev-parse', '--verify', 'HEAD')
    if ($head.ExitCode -ne 0 -or $head.Text -notmatch '^[0-9a-f]{40}$') { throw "读不出 HEAD：$($head.Error)" }
    $commit = $head.Text
    $pushed = Invoke-CountdownDeployGit -Root $RepositoryRoot -Arguments @('rev-parse', '--verify', '--quiet', 'refs/remotes/origin/main')
    if ($pushed.ExitCode -ne 0 -or $pushed.Text -ne $commit) {
        throw "提交 $($commit.Substring(0, 7)) 尚未推送：origin/main 是 $(if ($pushed.Text) { $pushed.Text.Substring(0, [Math]::Min(7, $pushed.Text.Length)) } else { '（不存在）' })。"
    }
    $suiteRun = Find-CountdownDeploySuiteRun -RepositoryRoot $RepositoryRoot -Commit $commit
    $buildOutput = Read-CountdownDeployBuildOutput -RepositoryRoot $RepositoryRoot -Commit $commit
    if ($buildOutput.AggregateSha256 -ne $suiteRun.PayloadAggregateSha256) {
        throw "本机构建输出的聚合哈希与套件运行 $($suiteRun.RunId) 的构建回执不一致：程序目录不是套件测过的那份构建输出。"
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

# 包：程序目录里载荷清单列出的文件、载荷清单和安装器。程序目录的文件取自本机构建输出，不重新导出、不重新构建；
# 边写进包边算 SHA-256，与清单不一致就停。
function New-CountdownDeployPackage {
    param(
        [Parameter(Mandatory)] [string] $RepositoryRoot,
        [Parameter(Mandatory)] [object] $BuildOutput,
        [Parameter(Mandatory)] [string] $PackagePath
    )

    $archive = [System.IO.Compression.ZipFile]::Open($PackagePath, [System.IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($file in $BuildOutput.Files) {
            $sha256 = Add-CountdownDeployPackageEntry -Archive $archive -SourcePath (Join-Path $RepositoryRoot $file.Path) -EntryName $file.Path
            if ($sha256 -ne $file.Sha256) { throw "本机构建输出与载荷清单不一致：$($file.Path)" }
        }
        [void] (Add-CountdownDeployPackageEntry -Archive $archive -SourcePath $BuildOutput.ManifestPath -EntryName 'app\published-payload.json')
        foreach ($name in $script:InstallerFiles) {
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
        'param([object] $Port)'
        "`$arguments = @{ InstallRoot = $(ConvertTo-CountdownDeployLiteral -Value $InstallRoot); TaskName = $(ConvertTo-CountdownDeployLiteral -Value $TaskName) }"
        'if ($null -ne $Port) { $arguments.Port = $Port }'
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

function ConvertFrom-CountdownDeployInstallEnvelope {
    param([Parameter(Mandatory)] [object] $Envelope)

    $installerText = [string] (Get-CountdownDeployProperty -Object $Envelope -Name 'installer')
    $install = $null
    if (-not [string]::IsNullOrWhiteSpace($installerText)) {
        try { $install = $installerText | ConvertFrom-Json -DateKind String }
        catch { $install = $null }
    }
    if ($null -eq $install) {
        $install = [pscustomobject][ordered]@{ succeeded = $false; failedStage = $null; reason = $null; error = $null }
    }
    $envelopeError = Get-CountdownDeployProperty -Object $Envelope -Name 'error'
    if ($null -ne $envelopeError) { $install.error = ConvertTo-CountdownDeployBoundedText -Text ([string] $envelopeError) }
    $install | Add-Member -NotePropertyName packageVerified -NotePropertyValue (Test-CountdownDeployTrue (Get-CountdownDeployProperty -Object $Envelope -Name 'packageVerified')) -Force
    $install | Add-Member -NotePropertyName stagingRemoved -NotePropertyValue (Test-CountdownDeployTrue (Get-CountdownDeployProperty -Object $Envelope -Name 'stagingRemoved')) -Force
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
    部署入口的编排：部署，或只查状态。返回结果（由入口脚本输出为 JSON）；进度写到标准错误。
    .DESCRIPTION
    部署成功只看安装后的只读状态读取：提交是 HEAD、已安装 EXE 的哈希等于本机构建输出、达到启动就绪的进程仍在运行。
    预检通过后无论成败都写一份本地部署记录（<RecordRoot>\<运行编号>\deployment-record.json）：提交、包哈希、安装结果、
    状态快照与传输账本引用。本机的包和办公室的暂存目录都不保留。只查状态时不写任何东西。
    -Port 需要 InvokeRemoteScript（脚本路径、是否写操作 → ExitCode、Output）与 SendFile（本地文件、远端路径、
    期望 SHA-256、尝试编号 → Status、AttemptId、Sha256、Bytes、LedgerPath；远端目录由它建出，账本位置由它决定）。
    #>
    [CmdletBinding()]
    param(
        [switch] $StatusOnly,
        [Parameter(Mandatory)] [string] $RepositoryRoot,
        [Parameter(Mandatory)] [string] $RecordRoot,
        [Parameter(Mandatory)] [string] $RemoteInstallRoot,
        [Parameter(Mandatory)] [string] $TaskName,
        [Parameter(Mandatory)] [string] $RemoteStagingRoot,
        [Parameter(Mandatory)] [object] $Port
    )

    if ($StatusOnly) {
        return Invoke-CountdownDeployStatusRead -RepositoryRoot $RepositoryRoot -RemoteInstallRoot $RemoteInstallRoot -TaskName $TaskName -Port $Port
    }

    $runId = [DateTime]::Now.ToString('yyyyMMddTHHmmssfff') + '-' + [guid]::NewGuid().ToString('N').Substring(0, 8)
    $result = [ordered]@{
        schemaVersion = 1
        mode = 'deploy'
        succeeded = $false
        failedStep = $null
        error = $null
        runId = $runId
        startedAtUtc = [DateTimeOffset]::UtcNow.ToString('o')
        finishedAtUtc = $null
        commit = $null
        suiteRun = $null
        buildOutput = $null
        package = $null
        remoteStagingDirectory = $null
        probe = $null
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
        Write-CountdownDeployProgress -Step 'package' -Message '把程序目录、载荷清单和安装器打成一个包'
        [void] [System.IO.Directory]::CreateDirectory($workDirectory)
        $package = New-CountdownDeployPackage -RepositoryRoot $RepositoryRoot -BuildOutput $preflight.BuildOutput -PackagePath $packagePath
        $result.package = [ordered]@{ sha256 = $package.Sha256; bytes = $package.Bytes; retained = $true }

        $step = 'probe'
        Write-CountdownDeployProgress -Step 'probe' -Message '经远端执行入口做只读连通性探测'
        $probeScript = Write-CountdownDeployScript -Path (Join-Path $workDirectory 'probe.ps1') -Text $script:RemoteProbeScript
        $probe = Invoke-CountdownDeployRemoteScript -Port $Port -ScriptPath $probeScript -WriteOperation $false -Name '连通性探测'
        if ([string] (Get-CountdownDeployProperty -Object $probe -Name 'probe') -cne 'ok') { throw '连通性探测没有返回 ok。' }
        $result.probe = [ordered]@{ powershell = [string] (Get-CountdownDeployProperty -Object $probe -Name 'powershell') }

        $step = 'transfer'
        $staging = [System.IO.Path]::Join($RemoteStagingRoot, "clock-countdown-$runId")
        $result.remoteStagingDirectory = $staging
        Write-CountdownDeployProgress -Step 'transfer' -Message "经传输入口送到暂存目录 clock-countdown-$runId 并核对远端哈希"
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
        if ($result.transfer.status -cne 'succeeded') { throw "传输没有成功：$($result.transfer.status)" }
        if (-not [string]::Equals($remoteSha256, $package.Sha256, [System.StringComparison]::OrdinalIgnoreCase)) { throw '远端文件的哈希与包哈希不一致。' }

        $step = 'install'
        Write-CountdownDeployProgress -Step 'install' -Message '经远端执行入口核对包哈希、解包并运行包内安装器'
        $installText = $script:RemoteInstallScript
        foreach ($placeholder in @(
                @('__STAGING__', $staging), @('__SHA256__', $package.Sha256),
                @('__INSTALL_ROOT__', $RemoteInstallRoot), @('__TASK_NAME__', $TaskName))) {
            $installText = $installText.Replace($placeholder[0], (ConvertTo-CountdownDeployLiteral -Value $placeholder[1]))
        }
        $installScript = Write-CountdownDeployScript -Path (Join-Path $workDirectory 'install.ps1') -Text $installText
        $envelope = Invoke-CountdownDeployRemoteScript -Port $Port -ScriptPath $installScript -WriteOperation $true -Name '远端安装'
        $result.install = ConvertFrom-CountdownDeployInstallEnvelope -Envelope $envelope
        if (-not (Test-CountdownDeployTrue $result.install.succeeded)) {
            throw "包内安装器报告失败：$($result.install.failedStage) $($result.install.reason) $($result.install.error)"
        }

        $step = 'status'
        Write-CountdownDeployProgress -Step 'status' -Message '只读状态读取：提交、EXE 哈希与运行中的进程'
        $statusScript = Write-CountdownDeployStatusScript -RepositoryRoot $RepositoryRoot -Directory $workDirectory -InstallRoot $RemoteInstallRoot -TaskName $TaskName
        $result.status = Invoke-CountdownDeployRemoteScript -Port $Port -ScriptPath $statusScript -WriteOperation $false -Name '状态读取'
        $readyProcess = Get-CountdownDeployProperty -Object $result.install -Name 'readyProcess'
        $result.statusMismatches = @(Get-CountdownDeployStatusMismatch -Status $result.status -Commit $preflight.Commit -ExecutableSha256 $preflight.BuildOutput.ExecutableSha256 `
                -ReadyProcessId (Get-CountdownDeployProperty -Object $readyProcess -Name 'processId'))
        if ($result.statusMismatches.Count -gt 0) { throw "安装后的状态与预期不一致：$($result.statusMismatches -join '、')。" }
        $result.succeeded = $true
    }
    catch {
        $result.failedStep = $step
        $result.error = ConvertTo-CountdownDeployBoundedText -Text $_.Exception.Message
    }
    finally {
        Remove-CountdownDeployWorkDirectory -Path $workDirectory
        if ($null -ne $result.package) { $result.package.retained = Test-Path -LiteralPath $packagePath }
    }

    if ($preflightPassed) {
        Write-CountdownDeployProgress -Step 'record' -Message '写本地部署记录'
        $result.finishedAtUtc = [DateTimeOffset]::UtcNow.ToString('o')
        $recordPath = [System.IO.Path]::Join($RecordRoot, $runId, 'deployment-record.json')
        $result.recordPath = $recordPath
        try { Write-CountdownDeployRecord -Path $recordPath -Record $result }
        catch {
            $result.recordPath = $null
            $result.succeeded = $false
            $result.failedStep = 'record'
            $result.error = ConvertTo-CountdownDeployBoundedText -Text "写本地部署记录失败：$($_.Exception.Message)"
        }
    }
    else { $result.finishedAtUtc = [DateTimeOffset]::UtcNow.ToString('o') }
    Write-CountdownDeployConclusion -Message $(if ($result.succeeded) { "部署成功，提交 $($result.commit.Substring(0, 7)) 正在办公室运行。" } else { "部署没有成功（$($result.failedStep)）：$($result.error)" })
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
