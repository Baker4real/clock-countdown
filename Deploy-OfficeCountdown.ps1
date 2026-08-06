#requires -Version 7.0

[CmdletBinding()]
param(
    [switch] $ValidateOnly,
    [Parameter(DontShow)] [switch] $LoadFunctionsOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = $PSScriptRoot

function Invoke-OfficeEncodedCommand {
    param([Parameter(Mandatory)] [string] $ScriptText)

    $runtimeGuard = "`$ErrorActionPreference = 'Stop'; if (`$PSVersionTable.PSVersion.Major -lt 7 -or `$PSVersionTable.PSEdition -ne 'Core') { throw '办公室设备必须使用 PowerShell 7+（pwsh.exe）。' }"
    $encoded = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes("$runtimeGuard`r`n$ScriptText"))
    $output = @(& ssh.exe @script:sshOptions $env:REPLICATOR_OFFICE_SSH_DESTINATION "pwsh.exe -NoLogo -NoProfile -EncodedCommand $encoded" 2>&1)
    $exitCode = $LASTEXITCODE
    if ($exitCode -ne 0) {
        $diagnostic = (@($output) | ForEach-Object { [string] $_ }) -join "`n"
        if ([string]::IsNullOrWhiteSpace($diagnostic)) { $diagnostic = '<no output>' }
        throw "办公室设备上的远程命令失败（退出码 $exitCode）：$diagnostic"
    }
    [string[]] @($output | ForEach-Object { [string] $_ })
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

function Get-OfficeRemoteDeploymentFailureReceipt {
    param([Parameter(Mandatory)] [string] $RemotePath)

    $escapedRemotePath = $RemotePath.Replace("'", "''")
    $runtimeGuard = "`$ErrorActionPreference = 'Stop'; if (`$PSVersionTable.PSVersion.Major -lt 7 -or `$PSVersionTable.PSEdition -ne 'Core') { throw '办公室设备必须使用 PowerShell 7+（pwsh.exe）。' }"
    $receiptScript = @"
$runtimeGuard
if (-not (Test-Path -LiteralPath '$escapedRemotePath' -PathType Leaf)) {
  exit 0
}
`$item = Get-Item -LiteralPath '$escapedRemotePath' -Force -ErrorAction Stop
if (`$item -isnot [System.IO.FileInfo] -or (`$item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
  throw '远端部署失败回执必须是非 reparse point 的普通文件。'
}
`$length = [long] `$item.Length
if (`$length -gt 32768) {
  throw "远端部署失败回执超过允许大小：`$length"
}
[Console]::Out.Write([Convert]::ToBase64String([System.IO.File]::ReadAllBytes(`$item.FullName)))
"@
    $encoded = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($receiptScript))
    $exitCode = -1
    try {
        $output = & ssh.exe @script:sshOptions $env:REPLICATOR_OFFICE_SSH_DESTINATION "pwsh.exe -NoLogo -NoProfile -EncodedCommand $encoded"
        $exitCode = $LASTEXITCODE
    }
    catch {
        return [pscustomobject]@{
            Success = $false
            ExitCode = -1
            Bytes = $null
            FailureMessage = "无法启动办公室设备部署失败回执读取：$($_.Exception.Message)"
        }
    }
    if ($exitCode -ne 0) {
        return [pscustomobject]@{
            Success = $false
            ExitCode = $exitCode
            Bytes = $null
            FailureMessage = '无法读取办公室设备上的部署失败回执。'
        }
    }

    $text = ($output | Out-String).Trim()
    if ([string]::IsNullOrWhiteSpace($text)) {
        return [pscustomobject]@{
            Success = $true
            ExitCode = $exitCode
            Bytes = $null
            FailureMessage = $null
        }
    }
    try {
        $bytes = [Convert]::FromBase64String($text)
    }
    catch {
        return [pscustomobject]@{
            Success = $false
            ExitCode = $exitCode
            Bytes = $null
            FailureMessage = "办公室设备返回的部署失败回执不是有效 Base64：$($_.Exception.Message)"
        }
    }
    if ($bytes.Length -gt 32768) {
        return [pscustomobject]@{
            Success = $false
            ExitCode = $exitCode
            Bytes = $null
            FailureMessage = "办公室设备返回的部署失败回执超过允许大小：$($bytes.Length)"
        }
    }
    try {
        [void] (ConvertFrom-CountdownInstallFailureReceiptBytes -Bytes $bytes)
    }
    catch {
        return [pscustomobject]@{
            Success = $false
            ExitCode = $exitCode
            Bytes = $null
            FailureMessage = "办公室设备返回的部署失败回执未通过契约校验：$($_.Exception.Message)"
        }
    }
    [pscustomobject]@{
        Success = $true
        ExitCode = $exitCode
        Bytes = [byte[]] $bytes
        FailureMessage = $null
    }
}

function Get-OfficeRemotePreRollbackDiagnostics {
    param([Parameter(Mandatory)] [string] $RemotePath)

    $escapedRemotePath = $RemotePath.Replace("'", "''")
    $runtimeGuard = "`$ErrorActionPreference = 'Stop'; if (`$PSVersionTable.PSVersion.Major -lt 7 -or `$PSVersionTable.PSEdition -ne 'Core') { throw '办公室设备必须使用 PowerShell 7+（pwsh.exe）。' }"
    $readerScript = @"
$runtimeGuard
if (-not (Test-Path -LiteralPath '$escapedRemotePath' -PathType Leaf)) { exit 0 }
`$item = Get-Item -LiteralPath '$escapedRemotePath' -Force -ErrorAction Stop
if (`$item -isnot [System.IO.FileInfo] -or (`$item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
  throw 'rollback 前诊断必须是非 reparse point 的普通文件。'
}
if ([long] `$item.Length -gt 32768) { throw "rollback 前诊断超过远端读取上限：`$(`$item.Length)" }
[Console]::Out.Write([Convert]::ToBase64String([System.IO.File]::ReadAllBytes(`$item.FullName)))
"@
    $encoded = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($readerScript))
    try {
        $output = & ssh.exe @script:sshOptions $env:REPLICATOR_OFFICE_SSH_DESTINATION "pwsh.exe -NoLogo -NoProfile -EncodedCommand $encoded"
        $exitCode = $LASTEXITCODE
    }
    catch {
        return [pscustomobject]@{ Success = $false; ExitCode = -1; Bytes = $null; FailureMessage = "无法启动 rollback 前诊断读取：$($_.Exception.Message)" }
    }
    if ($exitCode -ne 0) {
        return [pscustomobject]@{ Success = $false; ExitCode = $exitCode; Bytes = $null; FailureMessage = '无法读取办公室设备上的 rollback 前诊断。' }
    }
    $text = ($output | Out-String).Trim()
    if ([string]::IsNullOrWhiteSpace($text)) {
        return [pscustomobject]@{ Success = $true; ExitCode = 0; Bytes = $null; FailureMessage = $null }
    }
    try { $bytes = [Convert]::FromBase64String($text) }
    catch {
        return [pscustomobject]@{ Success = $false; ExitCode = 0; Bytes = $null; FailureMessage = "办公室设备返回的 rollback 前诊断不是有效 Base64：$($_.Exception.Message)" }
    }
    if ($bytes.Length -gt 32768) {
        return [pscustomobject]@{ Success = $false; ExitCode = 0; Bytes = $null; FailureMessage = "办公室设备返回的 rollback 前诊断超过远端读取上限：$($bytes.Length)" }
    }
    try { [void] (ConvertFrom-CountdownPreRollbackDiagnosticsBytes -Bytes $bytes) }
    catch {
        return [pscustomobject]@{ Success = $false; ExitCode = 0; Bytes = $null; FailureMessage = "办公室设备返回的 rollback 前诊断未通过契约校验：$($_.Exception.Message)" }
    }
    [pscustomobject]@{ Success = $true; ExitCode = 0; Bytes = [byte[]] $bytes; FailureMessage = $null }
}

function Invoke-CountdownDeploymentGit {
    param(
        [Parameter(Mandatory)] [string] $GitPath,
        [Parameter(Mandatory)] [string] $RepositoryRoot,
        [Parameter(Mandatory)] [string[]] $Arguments,
        [scriptblock] $Adapter
    )

    if ($null -eq $Adapter) {
        $output = @(& $GitPath -C $RepositoryRoot @Arguments 2>&1)
        $exitCode = $LASTEXITCODE
    }
    else {
        $result = & $Adapter $RepositoryRoot $Arguments
        if ($null -eq $result -or
            $result.PSObject.Properties.Name -notcontains 'ExitCode' -or
            $result.PSObject.Properties.Name -notcontains 'Output') {
            throw 'Git 夹具必须返回 ExitCode 和 Output。'
        }
        $exitCode = [int] $result.ExitCode
        $output = @($result.Output)
    }

    if ($exitCode -ne 0) {
        $diagnostic = (@($output) | ForEach-Object { [string] $_ }) -join "`n"
        if ([string]::IsNullOrWhiteSpace($diagnostic)) { $diagnostic = '<no output>' }
        throw "Git 命令失败（退出码 $exitCode）：git $($Arguments -join ' ')；$diagnostic"
    }
    [string[]] @($output | ForEach-Object { [string] $_ })
}

function Get-CountdownDeploymentGitScalar {
    param(
        [Parameter(Mandatory)] [string] $GitPath,
        [Parameter(Mandatory)] [string] $RepositoryRoot,
        [Parameter(Mandatory)] [string[]] $Arguments,
        [scriptblock] $Adapter
    )

    $values = @(
        Invoke-CountdownDeploymentGit `
            -GitPath $GitPath `
            -RepositoryRoot $RepositoryRoot `
            -Arguments $Arguments `
            -Adapter $Adapter |
            ForEach-Object { $_.Trim() } |
            Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
    )
    if ($values.Count -ne 1) {
        throw "Git 命令未返回唯一非空结果：git $($Arguments -join ' ')"
    }
    $values[0]
}

function Assert-CountdownDeploymentRepositoryState {
    param(
        [Parameter(Mandatory)] [string] $GitPath,
        [Parameter(Mandatory)] [string] $RepositoryRoot,
        [Parameter(Mandatory)] [string] $Stage,
        [string] $ExpectedCommit,
        [scriptblock] $Adapter
    )

    [void] (Invoke-CountdownDeploymentGit `
        -GitPath $GitPath `
        -RepositoryRoot $RepositoryRoot `
        -Arguments @('fetch', '--no-tags', 'origin', 'main') `
        -Adapter $Adapter)
    $status = @(
        Invoke-CountdownDeploymentGit `
            -GitPath $GitPath `
            -RepositoryRoot $RepositoryRoot `
            -Arguments @('status', '--porcelain=v1', '--untracked-files=all') `
            -Adapter $Adapter
    )
    $branch = Get-CountdownDeploymentGitScalar `
        -GitPath $GitPath `
        -RepositoryRoot $RepositoryRoot `
        -Arguments @('branch', '--show-current') `
        -Adapter $Adapter
    $head = Get-CountdownDeploymentGitScalar `
        -GitPath $GitPath `
        -RepositoryRoot $RepositoryRoot `
        -Arguments @('rev-parse', '--verify', 'HEAD') `
        -Adapter $Adapter
    $remote = Get-CountdownDeploymentGitScalar `
        -GitPath $GitPath `
        -RepositoryRoot $RepositoryRoot `
        -Arguments @('rev-parse', '--verify', 'refs/remotes/origin/main') `
        -Adapter $Adapter

    if ($status.Count -ne 0) { throw "$Stage：工作树不干净，拒绝部署。" }
    if ($branch -ne 'main') { throw "$Stage：仅允许从 main 分支部署。" }
    if ($head -ne $remote) { throw "$Stage：本地 HEAD 与 origin/main 不一致，拒绝部署。" }
    if (-not [string]::IsNullOrWhiteSpace($ExpectedCommit) -and $head -ne $ExpectedCommit) {
        throw "$Stage：HEAD 已偏离初始已推送提交，拒绝部署。"
    }

    [pscustomobject]@{
        Branch = $branch
        Head = $head
        OriginMain = $remote
    }
}

function Export-CountdownDeploymentCommitSnapshot {
    param(
        [Parameter(Mandatory)] [string] $GitPath,
        [Parameter(Mandatory)] [string] $RepositoryRoot,
        [Parameter(Mandatory)] [string] $Commit,
        [Parameter(Mandatory)] [string] $DestinationPath,
        [scriptblock] $Adapter
    )

    [void] (Invoke-CountdownDeploymentGit `
        -GitPath $GitPath `
        -RepositoryRoot $RepositoryRoot `
        -Arguments @('archive', '--format=zip', "--output=$DestinationPath", $Commit) `
        -Adapter $Adapter)
    if (-not (Test-Path -LiteralPath $DestinationPath -PathType Leaf)) {
        throw "Git 未生成提交快照：$DestinationPath"
    }
}

function Resolve-CountdownDeploymentSnapshotPath {
    param(
        [Parameter(Mandatory)] [string] $Root,
        [Parameter(Mandatory)] [string] $RelativePath
    )

    if ([System.IO.Path]::IsPathRooted($RelativePath)) {
        throw "部署快照路径必须是相对路径：$RelativePath"
    }
    $rootPath = [System.IO.Path]::GetFullPath($Root).TrimEnd('\')
    $resolved = [System.IO.Path]::GetFullPath((Join-Path $rootPath $RelativePath))
    if (-not $resolved.StartsWith($rootPath + '\', [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "部署快照路径逃出根目录：$RelativePath"
    }
    $resolved
}

function Get-CountdownDeploymentCommitFileIdentity {
    param(
        [Parameter(Mandatory)] [string] $GitPath,
        [Parameter(Mandatory)] [string] $RepositoryRoot,
        [Parameter(Mandatory)] [string] $Commit,
        [Parameter(Mandatory)] [string] $RelativePath,
        [scriptblock] $Adapter
    )

    [void] (Resolve-CountdownDeploymentSnapshotPath -Root $RepositoryRoot -RelativePath $RelativePath)
    $gitRelativePath = $RelativePath.Replace('\', '/')
    $identity = Get-CountdownDeploymentGitScalar `
        -GitPath $GitPath `
        -RepositoryRoot $RepositoryRoot `
        -Arguments @('rev-parse', '--verify', "${Commit}:$gitRelativePath") `
        -Adapter $Adapter
    if ($identity -notmatch '^[0-9a-fA-F]{40}([0-9a-fA-F]{24})?$') {
        throw "Git 提交文件未返回有效 blob identity：$RelativePath"
    }
    $identity.ToLowerInvariant()
}

function New-CountdownDeploymentCommitIdentityManifest {
    param(
        [Parameter(Mandatory)] [string] $GitPath,
        [Parameter(Mandatory)] [string] $RepositoryRoot,
        [Parameter(Mandatory)] [string] $Commit,
        [Parameter(Mandatory)] [string[]] $RelativePaths,
        [scriptblock] $Adapter
    )

    $manifest = [ordered]@{}
    foreach ($relativePath in $RelativePaths) {
        $manifest[$relativePath] = Get-CountdownDeploymentCommitFileIdentity `
            -GitPath $GitPath `
            -RepositoryRoot $RepositoryRoot `
            -Commit $Commit `
            -RelativePath $relativePath `
            -Adapter $Adapter
    }
    , $manifest
}

function Invoke-CountdownDeploymentLockedCommitFile {
    param(
        [Parameter(Mandatory)] [string] $GitPath,
        [Parameter(Mandatory)] [string] $RepositoryRoot,
        [Parameter(Mandatory)] [string] $Root,
        [Parameter(Mandatory)] [string] $RelativePath,
        [Parameter(Mandatory)] [string] $ExpectedIdentity,
        [Parameter(Mandatory)] [string] $Stage,
        [Parameter(Mandatory)] [scriptblock] $Action,
        [scriptblock] $Adapter
    )

    if ($ExpectedIdentity -notmatch '^[0-9a-fA-F]{40}([0-9a-fA-F]{24})?$') {
        throw "$Stage：预期 Git blob identity 无效：$RelativePath"
    }
    $path = Resolve-CountdownDeploymentSnapshotPath -Root $Root -RelativePath $RelativePath
    $item = Get-Item -LiteralPath $path -Force -ErrorAction Stop
    if ($item -isnot [System.IO.FileInfo] -or
        ($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "$Stage：部署文件必须是非 reparse point 的普通文件：$RelativePath"
    }

    $stream = [System.IO.FileStream]::new(
        $item.FullName,
        [System.IO.FileMode]::Open,
        [System.IO.FileAccess]::Read,
        [System.IO.FileShare]::Read)
    try {
        $actualIdentity = Get-CountdownDeploymentGitScalar `
            -GitPath $GitPath `
            -RepositoryRoot $RepositoryRoot `
            -Arguments @('hash-object', '--no-filters', '--', $item.FullName) `
            -Adapter $Adapter
        if (-not [string]::Equals(
                $actualIdentity,
                $ExpectedIdentity,
                [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "$Stage：部署文件与已验证提交的 blob identity 不一致：$RelativePath"
        }

        $stream.Position = 0
        $sha256 = [Convert]::ToHexString(
            [System.Security.Cryptography.SHA256]::HashData($stream))
        $stream.Position = 0
        & $Action $item.FullName $sha256
    }
    finally {
        $stream.Dispose()
    }
}

function New-CountdownDeploymentCommitFileManifest {
    param(
        [Parameter(Mandatory)] [string] $GitPath,
        [Parameter(Mandatory)] [string] $RepositoryRoot,
        [Parameter(Mandatory)] [string] $Root,
        [Parameter(Mandatory)] [System.Collections.IDictionary] $IdentityManifest,
        [Parameter(Mandatory)] [string] $Stage,
        [scriptblock] $Adapter
    )

    $manifest = [ordered]@{}
    foreach ($entry in $IdentityManifest.GetEnumerator()) {
        $manifest[[string] $entry.Key] = Invoke-CountdownDeploymentLockedCommitFile `
            -GitPath $GitPath `
            -RepositoryRoot $RepositoryRoot `
            -Root $Root `
            -RelativePath ([string] $entry.Key) `
            -ExpectedIdentity ([string] $entry.Value) `
            -Stage $Stage `
            -Adapter $Adapter `
            -Action { param($lockedPath, $sha256) $sha256 }
    }
    , $manifest
}

function New-CountdownDeploymentFileManifest {
    param(
        [Parameter(Mandatory)] [string] $Root,
        [Parameter(Mandatory)] [string[]] $RelativePaths
    )

    $manifest = [ordered]@{}
    foreach ($relativePath in $RelativePaths) {
        $path = Resolve-CountdownDeploymentSnapshotPath -Root $Root -RelativePath $relativePath
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
            throw "部署快照缺少文件：$relativePath"
        }
        $manifest[$relativePath] = (Get-FileHash -Algorithm SHA256 -LiteralPath $path).Hash
    }
    , $manifest
}

function Assert-CountdownDeploymentFileManifest {
    param(
        [Parameter(Mandatory)] [string] $Root,
        [Parameter(Mandatory)] [System.Collections.IDictionary] $Manifest,
        [Parameter(Mandatory)] [string] $Stage
    )

    foreach ($entry in $Manifest.GetEnumerator()) {
        $path = Resolve-CountdownDeploymentSnapshotPath -Root $Root -RelativePath ([string] $entry.Key)
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
            throw "$Stage：部署文件缺失：$($entry.Key)"
        }
        $actual = (Get-FileHash -Algorithm SHA256 -LiteralPath $path).Hash
        if (-not [string]::Equals($actual, [string] $entry.Value, [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "$Stage：部署文件已变化：$($entry.Key)"
        }
    }
}

function Assert-CountdownDeploymentFileSet {
    param(
        [Parameter(Mandatory)] [string] $Root,
        [Parameter(Mandatory)] [string[]] $ExpectedFiles,
        [Parameter(Mandatory)] [string] $Stage
    )

    $actual = @(
        Get-ChildItem -LiteralPath $Root -File -Recurse |
            ForEach-Object { [System.IO.Path]::GetRelativePath($Root, $_.FullName) } |
            Sort-Object
    )
    $expected = @($ExpectedFiles | Sort-Object)
    if (($actual -join "`n") -ne ($expected -join "`n")) {
        throw "$Stage：发布包文件集与已验证清单不一致。"
    }
}

function Assert-CountdownRemoteInstallScript {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [string] $ScriptText
    )

    $tokens = $null
    $parseErrors = $null
    $ast = [System.Management.Automation.Language.Parser]::ParseInput(
        $ScriptText,
        [ref] $tokens,
        [ref] $parseErrors)
    if (@($parseErrors).Count -ne 0) {
        throw "远端安装脚本 AST 无效：$($parseErrors[0].Message)"
    }

    $invocations = @(
        $ast.FindAll({
                param($node)
                $node -is [System.Management.Automation.Language.CommandAst] -and
                $node.GetCommandName() -eq 'Invoke-LockedDeploymentFile'
            }, $true)
    )
    if ($invocations.Count -ne 4) {
        throw "远端安装脚本必须包含 4 次锁定文件调用，实际为 $($invocations.Count)。"
    }

    foreach ($invocation in $invocations) {
        $commandElements = @($invocation.CommandElements)
        $parameterAsts = @(
            $commandElements |
                Where-Object { $_ -is [System.Management.Automation.Language.CommandParameterAst] } |
                ForEach-Object { $_ }
        )
        $parameterNames = @($parameterAsts | ForEach-Object { $_.ParameterName })
        $missingParameters = @(
            @('Path', 'ExpectedSha256', 'Action') |
                Where-Object { $_ -notin $parameterNames }
        )
        if ($missingParameters.Count -ne 0) {
            throw "远端安装脚本第 $($invocation.Extent.StartLineNumber) 行的锁定文件调用缺少参数：$($missingParameters -join ', ')。"
        }

        foreach ($parameterName in @('Path', 'ExpectedSha256', 'Action')) {
            $parameterAst = $parameterAsts |
                Where-Object { $_.ParameterName -eq $parameterName } |
                Select-Object -First 1
            $parameterIndex = [Array]::IndexOf($commandElements, $parameterAst)
            $hasInlineArgument = $null -ne $parameterAst.Argument
            $hasFollowingArgument = $parameterIndex + 1 -lt $commandElements.Count -and
                $commandElements[$parameterIndex + 1] -isnot [System.Management.Automation.Language.CommandParameterAst]
            if (-not $hasInlineArgument -and -not $hasFollowingArgument) {
                throw "远端安装脚本第 $($invocation.Extent.StartLineNumber) 行的参数 $parameterName 缺少值。"
            }
        }
    }

    $transferLedgerImportIndex = [Array]::FindIndex(
        [object[]] $invocations,
        [Predicate[object]] { param($invocation) $invocation.Extent.Text.Contains('Countdown.TransferLedger.psm1') })
    $remoteDeploymentImportIndex = [Array]::FindIndex(
        [object[]] $invocations,
        [Predicate[object]] { param($invocation) $invocation.Extent.Text.Contains('Countdown.RemoteDeployment.psm1') })
    if ($transferLedgerImportIndex -lt 0 -or
        $remoteDeploymentImportIndex -le $transferLedgerImportIndex) {
        throw '远端安装脚本必须先锁定导入 TransferLedger，再锁定导入 RemoteDeployment。'
    }

    [pscustomobject]@{
        InvocationCount = $invocations.Count
    }
}

if ($LoadFunctionsOnly) { return }

$gitPath = (Get-Command git.exe -CommandType Application -ErrorAction Stop |
    Select-Object -First 1 -ExpandProperty Source)
$initialRepositoryState = Assert-CountdownDeploymentRepositoryState `
    -GitPath $gitPath `
    -RepositoryRoot $repoRoot `
    -Stage '构建前'
$commit = $initialRepositoryState.Head
$executionIdentityManifest = New-CountdownDeploymentCommitIdentityManifest `
    -GitPath $gitPath `
    -RepositoryRoot $repoRoot `
    -Commit $commit `
    -RelativePaths @(
        'Deploy-OfficeCountdown.ps1',
        'Countdown.ReleaseContract.psm1',
        'Build-CountdownApplication.ps1',
        'Countdown.RemoteDeployment.psm1',
        'Countdown.TransferLedger.psm1',
        'Countdown.InstallTransaction.psm1'
    )
$shortCommit = $commit.Substring(0, 7)
$runId = "$(Get-Date -Format 'yyyyMMddHHmmssfff')-$([guid]::NewGuid().ToString('N').Substring(0, 8))"
$deploymentBindingNonce = [guid]::NewGuid().ToString('N')
$deploymentBindingSha256 = [Convert]::ToHexString(
    [System.Security.Cryptography.SHA256]::HashData(
        [System.Text.UTF8Encoding]::new($false).GetBytes($deploymentBindingNonce)))
$evidenceRoot = "D:\Codex\.codex\run-evidence\clock-countdown-deploy\$shortCommit\$runId"
$sourceArchive = Join-Path $evidenceRoot 'commit-source.zip'
$sourceRoot = Join-Path $evidenceRoot 'commit-source'
$packageRoot = Join-Path $evidenceRoot 'windows-package'
$extractRoot = Join-Path $evidenceRoot 'package-check'
$archive = Join-Path $evidenceRoot 'clock-countdown-windows.zip'
$transferLedgerPath = Join-Path $evidenceRoot 'transfer-attempts.json'
New-Item -ItemType Directory -Path $evidenceRoot -Force | Out-Null
Export-CountdownDeploymentCommitSnapshot `
    -GitPath $gitPath `
    -RepositoryRoot $repoRoot `
    -Commit $commit `
    -DestinationPath $sourceArchive
Expand-Archive -LiteralPath $sourceArchive -DestinationPath $sourceRoot

$snapshotDeployScriptSha256 = Invoke-CountdownDeploymentLockedCommitFile `
    -GitPath $gitPath `
    -RepositoryRoot $repoRoot `
    -Root $sourceRoot `
    -RelativePath 'Deploy-OfficeCountdown.ps1' `
    -ExpectedIdentity $executionIdentityManifest['Deploy-OfficeCountdown.ps1'] `
    -Stage '部署脚本提交身份校验' `
    -Action { param($lockedPath, $sha256) $sha256 }
if ((Get-FileHash -Algorithm SHA256 -LiteralPath $PSCommandPath).Hash -ne $snapshotDeployScriptSha256) {
    throw '正在运行的部署脚本与已验证提交不一致。'
}

$releaseContractSha256 = Invoke-CountdownDeploymentLockedCommitFile `
    -GitPath $gitPath `
    -RepositoryRoot $repoRoot `
    -Root $sourceRoot `
    -RelativePath 'Countdown.ReleaseContract.psm1' `
    -ExpectedIdentity $executionIdentityManifest['Countdown.ReleaseContract.psm1'] `
    -Stage '首次发布契约导入' `
    -Action {
        param($lockedPath, $sha256)
        Import-Module $lockedPath -Force | Out-Null
        $sha256
    }
$releaseContract = Get-CountdownReleaseContract
$requiredFiles = [string[]] @($releaseContract.PackageFiles)
$controlFiles = [string[]] @(
    @($releaseContract.PackageTrustRoots) + @('Build-CountdownApplication.ps1') |
        Sort-Object -Unique
)
$controlIdentityManifest = New-CountdownDeploymentCommitIdentityManifest `
    -GitPath $gitPath `
    -RepositoryRoot $repoRoot `
    -Commit $commit `
    -RelativePaths $controlFiles
$controlManifest = New-CountdownDeploymentCommitFileManifest `
    -GitPath $gitPath `
    -RepositoryRoot $repoRoot `
    -Root $sourceRoot `
    -IdentityManifest $controlIdentityManifest `
    -Stage '控制文件提交身份校验'

$powerShell7 = (Get-Command pwsh.exe -CommandType Application -ErrorAction Stop | Select-Object -First 1 -ExpandProperty Source)
$transferLedgerSha256 = Invoke-CountdownDeploymentLockedCommitFile `
    -GitPath $gitPath `
    -RepositoryRoot $repoRoot `
    -Root $sourceRoot `
    -RelativePath 'Build-CountdownApplication.ps1' `
    -ExpectedIdentity $executionIdentityManifest['Build-CountdownApplication.ps1'] `
    -Stage '构建脚本执行' `
    -Action {
        param($lockedPath, $sha256)
        & $powerShell7 -NoLogo -NoProfile -NonInteractive -File $lockedPath -Clean
        if ($LASTEXITCODE -ne 0) { throw '无法从当前 main 提交重建 C# 部署产物。' }
    }

[void] (Assert-CountdownDeploymentRepositoryState `
    -GitPath $gitPath `
    -RepositoryRoot $repoRoot `
    -Stage '构建后' `
    -ExpectedCommit $commit)
Assert-CountdownDeploymentFileManifest -Root $sourceRoot -Manifest $controlManifest -Stage '构建后'
$packageValidation = Assert-CountdownReleasePackage -Root $sourceRoot
$packageManifest = [ordered]@{}
foreach ($file in $requiredFiles) {
    if ($releaseContract.IntegrityHashes.Contains($file)) {
        $packageManifest[$file] = [string] $releaseContract.IntegrityHashes[$file]
    }
    elseif ($controlManifest.Contains($file)) {
        $packageManifest[$file] = [string] $controlManifest[$file]
    }
    else {
        throw "发布包文件缺少提交或发布契约身份：$file"
    }
}
[void] (Assert-CountdownDeploymentRepositoryState `
    -GitPath $gitPath `
    -RepositoryRoot $repoRoot `
    -Stage '打包前' `
    -ExpectedCommit $commit)
Assert-CountdownDeploymentFileManifest -Root $sourceRoot -Manifest $controlManifest -Stage '打包前'

New-Item -ItemType Directory -Path $packageRoot -Force | Out-Null
foreach ($file in $requiredFiles) {
    $destination = Join-Path $packageRoot $file
    New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $sourceRoot $file) -Destination $destination -Force
}
Assert-CountdownDeploymentFileSet -Root $packageRoot -ExpectedFiles $requiredFiles -Stage 'ZIP 生成前'
Assert-CountdownDeploymentFileManifest -Root $packageRoot -Manifest $packageManifest -Stage 'ZIP 生成前'
Compress-Archive -Path (Join-Path $packageRoot '*') -DestinationPath $archive -CompressionLevel Optimal
Expand-Archive -LiteralPath $archive -DestinationPath $extractRoot -Force
[void] (Assert-CountdownReleasePackage -Root $extractRoot)
Assert-CountdownDeploymentFileSet -Root $extractRoot -ExpectedFiles $requiredFiles -Stage 'ZIP 生成后'
Assert-CountdownDeploymentFileManifest -Root $extractRoot -Manifest $packageManifest -Stage 'ZIP 生成后'
Assert-CountdownDeploymentFileManifest -Root $sourceRoot -Manifest $packageManifest -Stage '打包后'
[void] (Assert-CountdownDeploymentRepositoryState `
    -GitPath $gitPath `
    -RepositoryRoot $repoRoot `
    -Stage '打包后' `
    -ExpectedCommit $commit)

$archiveHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $archive).Hash
if ($ValidateOnly) {
    [pscustomobject]@{ Valid = $true; Commit = $shortCommit; Archive = $archive; ArchiveSha256 = $archiveHash; VerifiedFiles = $packageValidation.VerifiedFiles; Transactional = $true } | ConvertTo-Json
    exit 0
}

[void] (Assert-CountdownDeploymentRepositoryState `
    -GitPath $gitPath `
    -RepositoryRoot $repoRoot `
    -Stage '远程输出前' `
    -ExpectedCommit $commit)
Assert-CountdownDeploymentFileManifest -Root $extractRoot -Manifest $packageManifest -Stage '远程输出前'
if ((Get-FileHash -Algorithm SHA256 -LiteralPath $archive).Hash -ne $archiveHash) {
    throw '远程输出前：ZIP 已变化，拒绝部署。'
}
Invoke-CountdownDeploymentLockedCommitFile `
    -GitPath $gitPath `
    -RepositoryRoot $repoRoot `
    -Root $extractRoot `
    -RelativePath 'Countdown.TransferLedger.psm1' `
    -ExpectedIdentity $executionIdentityManifest['Countdown.TransferLedger.psm1'] `
    -Stage '传输账本模块导入' `
    -Action {
        param($lockedPath, $sha256)
        Import-Module $lockedPath -Force | Out-Null
        $sha256
    }
$remoteDeploymentSha256 = Invoke-CountdownDeploymentLockedCommitFile `
    -GitPath $gitPath `
    -RepositoryRoot $repoRoot `
    -Root $extractRoot `
    -RelativePath 'Countdown.RemoteDeployment.psm1' `
    -ExpectedIdentity $executionIdentityManifest['Countdown.RemoteDeployment.psm1'] `
    -Stage '远程部署模块导入' `
    -Action {
        param($lockedPath, $sha256)
        Import-Module $lockedPath -Force | Out-Null
        $sha256
    }
Invoke-CountdownDeploymentLockedCommitFile `
    -GitPath $gitPath `
    -RepositoryRoot $repoRoot `
    -Root $extractRoot `
    -RelativePath 'Countdown.InstallTransaction.psm1' `
    -ExpectedIdentity $executionIdentityManifest['Countdown.InstallTransaction.psm1'] `
    -Stage '安装事务模块导入' `
    -Action { param($lockedPath, $sha256) Import-Module $lockedPath -Force -DisableNameChecking | Out-Null }
Assert-CountdownDeploymentFileManifest -Root $extractRoot -Manifest $packageManifest -Stage '部署模块加载后'
. 'D:\Codex\.codex\credentials\ssh\replicator\office\Use-OfficeSshHelper.ps1' | Out-Null
$officeDestination = [string] $env:REPLICATOR_OFFICE_SSH_DESTINATION
$officeDestinationSeparator = $officeDestination.LastIndexOf('@')
if ($officeDestinationSeparator -lt 1 -or $officeDestinationSeparator -ge ($officeDestination.Length - 1)) {
    throw '受管 office helper 返回的 SSH destination 无效。'
}
$officeUser = $officeDestination.Substring(0, $officeDestinationSeparator)
$officeHost = $officeDestination.Substring($officeDestinationSeparator + 1)
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

$expectedFileManifestJson = $packageManifest | ConvertTo-Json -Compress
$expectedFileManifestBase64 = [Convert]::ToBase64String(
    [System.Text.UTF8Encoding]::new($false).GetBytes($expectedFileManifestJson))
$remoteInstall = @"
`$ErrorActionPreference = 'Stop'
`$ProgressPreference = 'SilentlyContinue'
[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new(`$false)
`$powerShell7 = Join-Path `$PSHOME 'pwsh.exe'
if (-not (Test-Path -LiteralPath `$powerShell7 -PathType Leaf)) { throw '未找到当前 PowerShell 7 可执行文件。' }
`$archive = '$remoteRoot\clock-countdown.zip'
`$source = '$remoteRoot\source'
`$expectedFileManifestJson = [System.Text.UTF8Encoding]::new(`$false, `$true).GetString(
    [Convert]::FromBase64String('$expectedFileManifestBase64'))
`$expectedFileSha256 = `$expectedFileManifestJson | ConvertFrom-Json -AsHashtable -ErrorAction Stop

function Invoke-LockedDeploymentFile {
    param(
        [Parameter(Mandatory)] [string] `$Path,
        [Parameter(Mandatory)] [string] `$ExpectedSha256,
        [Parameter(Mandatory)] [scriptblock] `$Action
    )

    `$item = Get-Item -LiteralPath `$Path -Force -ErrorAction Stop
    if (`$item -isnot [System.IO.FileInfo] -or
        (`$item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "部署执行文件必须是非 reparse point 的普通文件：`$Path"
    }
    `$stream = [System.IO.FileStream]::new(
        `$item.FullName,
        [System.IO.FileMode]::Open,
        [System.IO.FileAccess]::Read,
        [System.IO.FileShare]::Read)
    try {
        `$actualSha256 = [Convert]::ToHexString(
            [System.Security.Cryptography.SHA256]::HashData(`$stream))
        `$stream.Position = 0
        if (-not [string]::Equals(
                `$actualSha256,
                `$ExpectedSha256,
                [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "部署执行文件哈希不匹配：`$Path"
        }
        & `$Action `$item.FullName
    }
    finally {
        `$stream.Dispose()
    }
}

Invoke-LockedDeploymentFile -Path `$archive -ExpectedSha256 '$archiveHash' -Action {
    param(`$lockedPath)
    Expand-Archive -LiteralPath `$lockedPath -DestinationPath `$source -Force
}
Invoke-LockedDeploymentFile -Path (Join-Path `$source 'Countdown.ReleaseContract.psm1') -ExpectedSha256 '$releaseContractSha256' -Action { param(`$lockedPath) Import-Module `$lockedPath -Force | Out-Null }
[void] (Assert-CountdownReleasePackage -Root `$source)
Invoke-LockedDeploymentFile -Path (Join-Path `$source 'Countdown.TransferLedger.psm1') -ExpectedSha256 '$transferLedgerSha256' -Action { param(`$lockedPath) Import-Module `$lockedPath -Force | Out-Null }
Invoke-LockedDeploymentFile -Path (Join-Path `$source 'Countdown.RemoteDeployment.psm1') -ExpectedSha256 '$remoteDeploymentSha256' -Action { param(`$lockedPath) Import-Module `$lockedPath -Force | Out-Null }
`$deploymentArguments = @{
  SourceRoot = `$source
  InstallPath = 'D:\敏敏子倒计时'
  PowerShell7Path = `$powerShell7
  DeferredReceiptPath = Join-Path '$remoteRoot' 'deferred-install-receipt.json'
  TaskName = 'Minmin Countdown'
  LegacyTaskName = '敏敏子倒计时'
  DeploymentBindingPath = Join-Path '$remoteRoot' 'deployment-binding.json'
  DeploymentBindingSha256 = '$deploymentBindingSha256'
  ExpectedFileSha256 = `$expectedFileSha256
}
Invoke-CountdownRemoteDeployment @deploymentArguments | ConvertTo-Json -Compress
"@
[void] (Assert-CountdownRemoteInstallScript -ScriptText $remoteInstall)
$remoteInstallScript = Join-Path $evidenceRoot 'remote-install.ps1'
[System.IO.File]::WriteAllText(
    $remoteInstallScript,
    $remoteInstall,
    [System.Text.UTF8Encoding]::new($false)
)
$remoteInstallHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $remoteInstallScript).Hash
$remoteInstallDestination = "D:/Codex/deploy/clock-countdown-$shortCommit-$runId/remote-install.ps1"
$remoteInstallPath = "$remoteRoot\remote-install.ps1"
$remoteFailureReceiptPath = "$remoteRoot\deployment-failure.json"
$failureReceiptEvidencePath = Join-Path $evidenceRoot 'remote-deployment-failure.json'
$successReceiptEvidencePath = Join-Path $evidenceRoot 'remote-deployment-success.json'
$independentReadbackEvidencePath = Join-Path $evidenceRoot 'post-deployment-independent-readback.json'
$remoteDeploymentBindingPath = "$remoteRoot\deployment-binding.json"
$deploymentBindingEvidencePath = Join-Path $evidenceRoot 'deployment-binding.json'
$remotePreRollbackDiagnosticsPath = "$remoteRoot\pre-rollback-diagnostics.json"
$preRollbackDiagnosticsEvidencePath = Join-Path $evidenceRoot 'pre-rollback-diagnostics.json'
Invoke-CountdownRecordedScpTransfer `
    -LedgerPath $transferLedgerPath `
    -Id 'remote-install-script' `
    -SourcePath $remoteInstallScript `
    -Destination "$($env:REPLICATOR_OFFICE_SSH_DESTINATION):$remoteInstallDestination" `
    -RemotePath $remoteInstallPath `
    -ScpOptions $script:sshOptions `
    -RemoteBytesProbe $remoteBytesProbe | Out-Null
$remoteInstallInvocation = @"
`$item = Get-Item -LiteralPath '$remoteInstallPath' -Force -ErrorAction Stop
if (`$item -isnot [System.IO.FileInfo] -or
    (`$item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
    throw '远端安装脚本必须是非 reparse point 的普通文件。'
}
`$stream = [System.IO.FileStream]::new(
    `$item.FullName,
    [System.IO.FileMode]::Open,
    [System.IO.FileAccess]::Read,
    [System.IO.FileShare]::Read)
try {
    `$actualSha256 = [Convert]::ToHexString(
        [System.Security.Cryptography.SHA256]::HashData(`$stream))
    `$stream.Position = 0
    if (-not [string]::Equals(
            `$actualSha256,
            '$remoteInstallHash',
            [System.StringComparison]::OrdinalIgnoreCase)) {
        throw '远端安装脚本哈希不匹配。'
    }
    & (Join-Path `$PSHOME 'pwsh.exe') -NoLogo -NoProfile -NonInteractive -File `$item.FullName
    if (`$LASTEXITCODE -ne 0) { exit `$LASTEXITCODE }
}
finally {
    `$stream.Dispose()
}
"@
try {
    $remoteExecutionOutput = @(
        Invoke-OfficeEncodedCommand -ScriptText $remoteInstallInvocation
    )
}
catch {
    $remoteExecutionFailure = $_.Exception.Message
    $evidenceMessages = [System.Collections.Generic.List[string]]::new()
    $failureReceiptReader = {
        Get-OfficeRemoteDeploymentFailureReceipt -RemotePath $remoteFailureReceiptPath
    }.GetNewClosure()
    $failureReceiptBytes = $null
    try {
        $failureReceiptBytes = Invoke-CountdownRecordedSshRead `
            -LedgerPath $transferLedgerPath `
            -Id 'remote-deployment-failure-receipt' `
            -Reader $failureReceiptReader
    }
    catch {
        $evidenceMessages.Add("读取远端部署失败回执也失败：$($_.Exception.Message)")
    }
    if ($null -ne $failureReceiptBytes) {
        try {
            $failureReceipt = ConvertFrom-CountdownInstallFailureReceiptBytes -Bytes $failureReceiptBytes
            [System.IO.File]::WriteAllText(
                $failureReceiptEvidencePath,
                ($failureReceipt | ConvertTo-Json -Depth 4),
                [System.Text.UTF8Encoding]::new($false)
            )
            $evidenceMessages.Add("远端部署失败回执已保存：$failureReceiptEvidencePath")
        }
        catch {
            $evidenceMessages.Add("远端部署失败回执二次契约校验失败：$($_.Exception.Message)")
        }
    }
    elseif ($evidenceMessages.Count -eq 0) {
        $evidenceMessages.Add('远端部署失败回执缺失。')
    }

    $preRollbackDiagnosticsReader = {
        Get-OfficeRemotePreRollbackDiagnostics -RemotePath $remotePreRollbackDiagnosticsPath
    }.GetNewClosure()
    $preRollbackDiagnosticsBytes = $null
    try {
        $preRollbackDiagnosticsBytes = Invoke-CountdownRecordedSshRead `
            -LedgerPath $transferLedgerPath `
            -Id 'pre-rollback-diagnostics' `
            -Reader $preRollbackDiagnosticsReader
    }
    catch {
        $evidenceMessages.Add("读取 rollback 前诊断 sidecar 失败：$($_.Exception.Message)")
    }
    if ($null -ne $preRollbackDiagnosticsBytes) {
        try {
            $preRollbackDiagnostics = ConvertFrom-CountdownPreRollbackDiagnosticsBytes -Bytes $preRollbackDiagnosticsBytes
            [System.IO.File]::WriteAllText(
                $preRollbackDiagnosticsEvidencePath,
                ($preRollbackDiagnostics | ConvertTo-Json -Depth 12),
                [System.Text.UTF8Encoding]::new($false)
            )
            $evidenceMessages.Add("rollback 前诊断 sidecar 已保存：$preRollbackDiagnosticsEvidencePath")
        }
        catch {
            $evidenceMessages.Add("rollback 前诊断 sidecar 二次契约校验失败：$($_.Exception.Message)")
        }
    }
    elseif (-not ($evidenceMessages | Where-Object { $_ -like '读取 rollback 前诊断 sidecar 失败*' })) {
        $evidenceMessages.Add('rollback 前诊断 sidecar 缺失。')
    }
    throw ((@($remoteExecutionFailure) + @($evidenceMessages)) -join '；')
}

$remoteExecutionJson = (@($remoteExecutionOutput) | ForEach-Object { [string] $_ }) -join [Environment]::NewLine
$successReceipt = Write-CountdownRemoteDeploymentSuccessReceipt `
    -Path $successReceiptEvidencePath `
    -Json $remoteExecutionJson
$expectedExecutableSha256 = [string] $releaseContract.IntegrityHashes['app\MinminCountdown.exe']
$postDeploymentReadbackAdapter = New-CountdownRemoteDeploymentReadbackSshAdapter `
    -Options $script:sshOptions `
    -Destination $officeDestination
[void] (Invoke-CountdownRemoteDeploymentIndependentReadback `
        -LedgerPath $transferLedgerPath `
        -DeploymentBindingEvidencePath $deploymentBindingEvidencePath `
        -IndependentReadbackEvidencePath $independentReadbackEvidencePath `
        -RemoteDeploymentBindingPath $remoteDeploymentBindingPath `
        -DeploymentBindingSha256 $deploymentBindingSha256 `
        -SuccessReceipt $successReceipt `
        -ExpectedExecutableSha256 $expectedExecutableSha256 `
        -ExpectedRemoteHost $officeHost `
        -ExpectedRemoteUser $officeUser `
        -Adapter $postDeploymentReadbackAdapter)
$successReceipt | ConvertTo-Json -Compress -Depth 4
