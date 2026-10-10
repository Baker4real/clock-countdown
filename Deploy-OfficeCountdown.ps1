#requires -Version 7.0

[CmdletBinding()]
param(
    [switch] $ValidateOnly,
    [switch] $StatusOnly,
    [switch] $RecheckLatest,
    [switch] $Redeploy,
    [Parameter(DontShow)] [switch] $LoadFunctionsOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = $PSScriptRoot
$script:sshExecutablePath = $null
$script:scpExecutablePath = $null

function Invoke-OfficeEncodedCommand {
    param([Parameter(Mandatory)] [string] $ScriptText)

    $runtimeGuard = "`$ErrorActionPreference = 'Stop'; if (`$PSVersionTable.PSVersion.Major -lt 7 -or `$PSVersionTable.PSEdition -ne 'Core') { throw '办公室设备必须使用 PowerShell 7+（pwsh.exe）。' }"
    $encoded = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes("$runtimeGuard`r`n$ScriptText"))
    $output = @(& $script:sshExecutablePath @script:sshOptions $env:REPLICATOR_OFFICE_SSH_DESTINATION "pwsh.exe -NoLogo -NoProfile -EncodedCommand $encoded" 2>&1)
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
        $output = & $script:sshExecutablePath @script:sshOptions $env:REPLICATOR_OFFICE_SSH_DESTINATION "pwsh.exe -NoLogo -NoProfile -EncodedCommand $encoded"
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

function Invoke-OfficeRemoteBoundedBase64FileRead {
    param(
        [Parameter(Mandatory)] [string] $RemotePath,
        [Parameter(Mandatory)] [string] $RemoteFileLabel,
        [Parameter(Mandatory)] [string] $RemoteSizeErrorLabel,
        [Parameter(Mandatory)] [string] $ReturnedSizeErrorLabel,
        [Parameter(Mandatory)] [string] $StartupErrorPrefix,
        [Parameter(Mandatory)] [string] $ReadErrorMessage,
        [Parameter(Mandatory)] [string] $InvalidBase64ErrorPrefix,
        [Parameter(Mandatory)] [string] $ContractErrorPrefix,
        [Parameter(Mandatory)] [scriptblock] $ContractParser
    )

    $escapedRemotePath = $RemotePath.Replace("'", "''")
    $runtimeGuard = "`$ErrorActionPreference = 'Stop'; if (`$PSVersionTable.PSVersion.Major -lt 7 -or `$PSVersionTable.PSEdition -ne 'Core') { throw '办公室设备必须使用 PowerShell 7+（pwsh.exe）。' }"
    $readerScript = @"
$runtimeGuard
if (-not (Test-Path -LiteralPath '$escapedRemotePath' -PathType Leaf)) { exit 0 }
`$item = Get-Item -LiteralPath '$escapedRemotePath' -Force -ErrorAction Stop
if (`$item -isnot [System.IO.FileInfo] -or (`$item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
  throw '${RemoteFileLabel}必须是非 reparse point 的普通文件。'
}
`$length = [long] `$item.Length
if (`$length -gt 32768) { throw "${RemoteSizeErrorLabel}：`$length" }
[Console]::Out.Write([Convert]::ToBase64String([System.IO.File]::ReadAllBytes(`$item.FullName)))
"@
    $encoded = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($readerScript))
    $exitCode = -1
    try {
        $output = & $script:sshExecutablePath @script:sshOptions $env:REPLICATOR_OFFICE_SSH_DESTINATION "pwsh.exe -NoLogo -NoProfile -EncodedCommand $encoded"
        $exitCode = $LASTEXITCODE
    }
    catch {
        return [pscustomobject]@{
            Success = $false
            ExitCode = -1
            Bytes = $null
            FailureMessage = "$StartupErrorPrefix$($_.Exception.Message)"
        }
    }
    if ($exitCode -ne 0) {
        return [pscustomobject]@{
            Success = $false
            ExitCode = $exitCode
            Bytes = $null
            FailureMessage = $ReadErrorMessage
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
            FailureMessage = "$InvalidBase64ErrorPrefix$($_.Exception.Message)"
        }
    }
    if ($bytes.Length -gt 32768) {
        return [pscustomobject]@{
            Success = $false
            ExitCode = $exitCode
            Bytes = $null
            FailureMessage = "$ReturnedSizeErrorLabel$($bytes.Length)"
        }
    }
    try {
        & $ContractParser $bytes
    }
    catch {
        return [pscustomobject]@{
            Success = $false
            ExitCode = $exitCode
            Bytes = $null
            FailureMessage = "$ContractErrorPrefix$($_.Exception.Message)"
        }
    }
    [pscustomobject]@{
        Success = $true
        ExitCode = $exitCode
        Bytes = [byte[]] $bytes
        FailureMessage = $null
    }
}

function Get-OfficeRemoteDeploymentFailureReceipt {
    param([Parameter(Mandatory)] [string] $RemotePath)

    Invoke-OfficeRemoteBoundedBase64FileRead `
        -RemotePath $RemotePath `
        -RemoteFileLabel '远端部署失败回执' `
        -RemoteSizeErrorLabel '远端部署失败回执超过允许大小' `
        -ReturnedSizeErrorLabel '办公室设备返回的部署失败回执超过允许大小：' `
        -StartupErrorPrefix '无法启动办公室设备部署失败回执读取：' `
        -ReadErrorMessage '无法读取办公室设备上的部署失败回执。' `
        -InvalidBase64ErrorPrefix '办公室设备返回的部署失败回执不是有效 Base64：' `
        -ContractErrorPrefix '办公室设备返回的部署失败回执未通过契约校验：' `
        -ContractParser { param($bytes) [void] (ConvertFrom-CountdownInstallFailureReceiptBytes -Bytes $bytes) }
}

function Get-OfficeRemotePreRollbackDiagnostics {
    param([Parameter(Mandatory)] [string] $RemotePath)

    Invoke-OfficeRemoteBoundedBase64FileRead `
        -RemotePath $RemotePath `
        -RemoteFileLabel 'rollback 前诊断' `
        -RemoteSizeErrorLabel 'rollback 前诊断超过远端读取上限' `
        -ReturnedSizeErrorLabel '办公室设备返回的 rollback 前诊断超过远端读取上限：' `
        -StartupErrorPrefix '无法启动 rollback 前诊断读取：' `
        -ReadErrorMessage '无法读取办公室设备上的 rollback 前诊断。' `
        -InvalidBase64ErrorPrefix '办公室设备返回的 rollback 前诊断不是有效 Base64：' `
        -ContractErrorPrefix '办公室设备返回的 rollback 前诊断未通过契约校验：' `
        -ContractParser { param($bytes) [void] (ConvertFrom-CountdownPreRollbackDiagnosticsBytes -Bytes $bytes) }
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

function Assert-CountdownDeploymentRepositoryAndManifest {
    param(
        [Parameter(Mandatory)] [string] $GitPath,
        [Parameter(Mandatory)] [string] $RepositoryRoot,
        [Parameter(Mandatory)] [string] $Stage,
        [Parameter(Mandatory)] [string] $ExpectedCommit,
        [Parameter(Mandatory)] [string] $ManifestRoot,
        [Parameter(Mandatory)] [System.Collections.IDictionary] $Manifest,
        [switch] $ManifestFirst,
        [scriptblock] $Adapter
    )

    if ($ManifestFirst) {
        Assert-CountdownDeploymentFileManifest -Root $ManifestRoot -Manifest $Manifest -Stage $Stage
        [void] (Assert-CountdownDeploymentRepositoryState `
            -GitPath $GitPath `
            -RepositoryRoot $RepositoryRoot `
            -Stage $Stage `
            -ExpectedCommit $ExpectedCommit `
            -Adapter $Adapter)
    }
    else {
        [void] (Assert-CountdownDeploymentRepositoryState `
            -GitPath $GitPath `
            -RepositoryRoot $RepositoryRoot `
            -Stage $Stage `
            -ExpectedCommit $ExpectedCommit `
            -Adapter $Adapter)
        Assert-CountdownDeploymentFileManifest -Root $ManifestRoot -Manifest $Manifest -Stage $Stage
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

function Invoke-CountdownWorkingTreeArchiveValidation {
    param(
        [Parameter(Mandatory)] [string] $RepositoryRoot,
        [Parameter(Mandatory)] [string[]] $RelativePaths,
        [Parameter(Mandatory)] [string] $TemporaryArchivePath
    )

    if ($RelativePaths.Count -eq 0) {
        throw '本地候选发布包清单不得为空。'
    }

    $archivePath = [System.IO.Path]::GetFullPath($TemporaryArchivePath)
    if (Test-Path -LiteralPath $archivePath) {
        throw "本地候选临时 ZIP 已存在：$archivePath"
    }
    $archiveParent = [System.IO.Path]::GetDirectoryName($archivePath)
    if (-not (Test-Path -LiteralPath $archiveParent -PathType Container)) {
        throw "本地候选临时 ZIP 父目录不存在：$archiveParent"
    }

    $sourceManifest = [ordered]@{}
    $expectedEntries = [System.Collections.Generic.HashSet[string]]::new(
        [System.StringComparer]::OrdinalIgnoreCase)
    $archive = $null
    $archiveCreated = $false
    try {
        $archive = [System.IO.Compression.ZipFile]::Open(
            $archivePath,
            [System.IO.Compression.ZipArchiveMode]::Create)
        $archiveCreated = $true
        foreach ($relativePath in $RelativePaths) {
            $entryName = $relativePath.Replace('\', '/')
            if (-not $expectedEntries.Add($entryName)) {
                throw "本地候选发布包清单包含重复路径：$relativePath"
            }

            $sourcePath = Resolve-CountdownDeploymentSnapshotPath `
                -Root $RepositoryRoot `
                -RelativePath $relativePath
            $sourceItem = Get-Item -LiteralPath $sourcePath -Force -ErrorAction Stop
            if ($sourceItem -isnot [System.IO.FileInfo] -or
                ($sourceItem.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw "本地候选文件必须是非 reparse point 的普通文件：$relativePath"
            }

            $sourceStream = [System.IO.FileStream]::new(
                $sourceItem.FullName,
                [System.IO.FileMode]::Open,
                [System.IO.FileAccess]::Read,
                [System.IO.FileShare]::Read)
            try {
                $sourceManifest[$relativePath] = [Convert]::ToHexString(
                    [System.Security.Cryptography.SHA256]::HashData($sourceStream))
                $sourceStream.Position = 0
                $entry = $archive.CreateEntry(
                    $entryName,
                    [System.IO.Compression.CompressionLevel]::Optimal)
                $entryStream = $entry.Open()
                try {
                    $sourceStream.CopyTo($entryStream)
                }
                finally {
                    $entryStream.Dispose()
                }
            }
            finally {
                $sourceStream.Dispose()
            }
        }
        $archive.Dispose()
        $archive = $null

        $archive = [System.IO.Compression.ZipFile]::OpenRead($archivePath)
        $actualEntries = [System.Collections.Generic.Dictionary[string, System.IO.Compression.ZipArchiveEntry]]::new(
            [System.StringComparer]::OrdinalIgnoreCase)
        foreach ($entry in $archive.Entries) {
            if ([string]::IsNullOrWhiteSpace($entry.Name) -or
                -not $actualEntries.TryAdd($entry.FullName, $entry)) {
                throw "本地候选 ZIP 包含目录或重复 entry：$($entry.FullName)"
            }
        }
        if ($actualEntries.Count -ne $expectedEntries.Count) {
            throw '本地候选 ZIP 的 entry 数量与发布包清单不一致。'
        }
        foreach ($relativePath in $RelativePaths) {
            $entryName = $relativePath.Replace('\', '/')
            if (-not $actualEntries.ContainsKey($entryName)) {
                throw "本地候选 ZIP 缺少发布包文件：$relativePath"
            }
            $entryStream = $actualEntries[$entryName].Open()
            try {
                $actualHash = [Convert]::ToHexString(
                    [System.Security.Cryptography.SHA256]::HashData($entryStream))
            }
            finally {
                $entryStream.Dispose()
            }
            if (-not [string]::Equals(
                    $actualHash,
                    [string] $sourceManifest[$relativePath],
                    [System.StringComparison]::OrdinalIgnoreCase)) {
                throw "本地候选 ZIP 文件哈希不匹配：$relativePath"
            }
        }
        $archive.Dispose()
        $archive = $null

        Assert-CountdownDeploymentFileManifest `
            -Root $RepositoryRoot `
            -Manifest $sourceManifest `
            -Stage '本地候选 ZIP 生成后'
        $archiveHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $archivePath).Hash
        [pscustomobject]@{
            ArchiveSha256 = $archiveHash
            ArchiveFiles = $actualEntries.Count
            SourceManifest = $sourceManifest
        }
    }
    finally {
        if ($null -ne $archive) {
            $archive.Dispose()
        }
        if ($archiveCreated -and (Test-Path -LiteralPath $archivePath -PathType Leaf)) {
            Remove-Item -LiteralPath $archivePath -Force
        }
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

            if ($parameterName -eq 'ExpectedSha256') {
                $argumentAst = if ($hasInlineArgument) {
                    $parameterAst.Argument
                }
                else {
                    $commandElements[$parameterIndex + 1]
                }
                if ($argumentAst -isnot [System.Management.Automation.Language.StringConstantExpressionAst] -or
                    $argumentAst.Value -notmatch '^[0-9a-fA-F]{64}$') {
                    throw "远端安装脚本第 $($invocation.Extent.StartLineNumber) 行的 ExpectedSha256 必须是 64 位十六进制字面量。"
                }
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

function New-OfficeSshEchoProbeCommand {
    param(
        [Parameter(Mandatory)] [ValidateRange(22644, 32768)] [int] $ProbeCharacterCount
    )

    $begin = 'COUNTDOWN-SSH-ECHO-PROBE-BEGIN:'
    $end = ':COUNTDOWN-SSH-ECHO-PROBE-END'
    $prefix = 'pwsh.exe -NoLogo -NoProfile -Command "Write-Output '''
    $suffix = '''"'
    $fixedLength = $prefix.Length + $begin.Length + $end.Length + $suffix.Length
    $padLength = $ProbeCharacterCount - $fixedLength
    if ($padLength -lt 32) {
        throw "SSH 行为探针命令固定开销过大，无法在 $ProbeCharacterCount 字符内放入有效载荷。"
    }
    $pad = 'P' * $padLength
    $expectedOutput = $begin + $pad + $end
    $remoteCommand = $prefix + $expectedOutput + $suffix
    if ($remoteCommand.Length -ne $ProbeCharacterCount) {
        throw "SSH 行为探针命令长度不是 ${ProbeCharacterCount}：$($remoteCommand.Length)"
    }
    [pscustomobject]@{
        RemoteCommand = $remoteCommand
        ExpectedOutput = $expectedOutput
        ProbeCharacterCount = $ProbeCharacterCount
    }
}

function New-OfficeSshStartabilityDummyDestination {
    param([Parameter(Mandatory)] [ValidateRange(1, 4096)] [int] $Length)

    $token = 'countdown-ssh-startability'
    if ($Length -le $token.Length) {
        return $token.Substring(0, $Length)
    }
    $token + ('x' * ($Length - $token.Length))
}

function Add-OfficeWindowsProcessCommandLineArgument {
    param(
        [Parameter(Mandatory)] [System.Text.StringBuilder] $Builder,
        [AllowEmptyString()] [string] $Argument
    )

    if ($Builder.Length -gt 0) {
        [void] $Builder.Append(' ')
    }
    $needsQuoting = $Argument.Length -eq 0 -or $Argument.IndexOfAny([char[]] @(' ', "`t", "`n", [char] 11, '"')) -ge 0
    if (-not $needsQuoting) {
        [void] $Builder.Append($Argument)
        return
    }

    [void] $Builder.Append('"')
    $index = 0
    while ($index -lt $Argument.Length) {
        $character = $Argument[$index]
        $index++
        if ($character -eq [char] '\') {
            $backslashCount = 1
            while ($index -lt $Argument.Length -and $Argument[$index] -eq [char] '\') {
                $index++
                $backslashCount++
            }
            if ($index -eq $Argument.Length) {
                [void] $Builder.Append([char] '\', $backslashCount * 2)
            }
            elseif ($Argument[$index] -eq [char] '"') {
                [void] $Builder.Append([char] '\', $backslashCount * 2 + 1)
                [void] $Builder.Append('"')
                $index++
            }
            else {
                [void] $Builder.Append([char] '\', $backslashCount)
            }
        }
        elseif ($character -eq [char] '"') {
            [void] $Builder.Append('\"')
        }
        else {
            [void] $Builder.Append($character)
        }
    }
    [void] $Builder.Append('"')
}

function ConvertTo-OfficeWindowsProcessCommandLine {
    param(
        [Parameter(Mandatory)] [ValidateNotNullOrEmpty()] [string] $FileName,
        [Parameter(Mandatory)] [AllowEmptyCollection()] [string[]] $Arguments
    )

    $builder = [System.Text.StringBuilder]::new()
    [void] $builder.Append('"').Append($FileName).Append('"')
    foreach ($argument in $Arguments) {
        Add-OfficeWindowsProcessCommandLineArgument -Builder $builder -Argument $argument
    }
    $builder.ToString()
}

function Test-OfficeSshProcessArgumentListCanStart {
    param(
        [Parameter(Mandatory)] [ValidateNotNullOrEmpty()] [string] $FileName,
        [Parameter(Mandatory)] [AllowEmptyCollection()] [string[]] $Arguments
    )

    $commandLine = ConvertTo-OfficeWindowsProcessCommandLine -FileName $FileName -Arguments $Arguments
    $commandLine.Length -le 32766
}

function Get-OfficeSshClientVersion {
    param([Parameter(Mandatory)] [ValidateNotNullOrEmpty()] [string] $SshExecutablePath)

    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $SshExecutablePath
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    [void] $startInfo.ArgumentList.Add('-V')
    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    try {
        if (-not $process.Start()) { return '<unknown>' }
        $standardOutput = $process.StandardOutput.ReadToEndAsync()
        $standardError = $process.StandardError.ReadToEndAsync()
        $process.WaitForExit()
        $text = (
            [string] $standardError.GetAwaiter().GetResult() +
            [string] $standardOutput.GetAwaiter().GetResult()
        ).Trim()
        if ([string]::IsNullOrWhiteSpace($text)) { '<unknown>' } else { $text }
    }
    catch {
        '<unknown>'
    }
    finally {
        $process.Dispose()
    }
}

function Resolve-OfficeSshClientIdentity {
    param(
        [string] $SystemRoot = $env:SystemRoot,
        [scriptblock] $ApplicationCommandLookup,
        [scriptblock] $VersionReader
    )

    if ($null -eq $ApplicationCommandLookup) {
        $ApplicationCommandLookup = {
            param([Parameter(Mandatory)] [string] $Name)
            @(Get-Command -Name $Name -CommandType Application -All -ErrorAction SilentlyContinue)
        }
    }
    if ($null -eq $VersionReader) {
        $VersionReader = {
            param([Parameter(Mandatory)] [string] $SshPath)
            Get-OfficeSshClientVersion -SshExecutablePath $SshPath
        }
    }

    $candidates = [System.Collections.Generic.List[object]]::new()
    $openSshDirectory = Join-Path $SystemRoot 'System32\OpenSSH'
    $openSshSsh = Join-Path $openSshDirectory 'ssh.exe'
    $openSshScp = Join-Path $openSshDirectory 'scp.exe'
    if ((Test-Path -LiteralPath $openSshSsh -PathType Leaf) -and
        (Test-Path -LiteralPath $openSshScp -PathType Leaf)) {
        [void] $candidates.Add([pscustomobject]@{
                SshPath = [System.IO.Path]::GetFullPath($openSshSsh)
                ScpPath = [System.IO.Path]::GetFullPath($openSshScp)
                Source = 'windows-openssh'
            })
    }

    $scpByDirectory = [System.Collections.Generic.Dictionary[string, string]]::new(
        [System.StringComparer]::OrdinalIgnoreCase)
    foreach ($scpCommand in @(& $ApplicationCommandLookup 'scp.exe')) {
        $scpPath = [string] $scpCommand.Source
        if ([string]::IsNullOrWhiteSpace($scpPath) -or
            -not (Test-Path -LiteralPath $scpPath -PathType Leaf)) {
            continue
        }
        $fullScp = [System.IO.Path]::GetFullPath($scpPath)
        $directory = [System.IO.Path]::GetDirectoryName($fullScp)
        if (-not $scpByDirectory.ContainsKey($directory)) {
            $scpByDirectory[$directory] = $fullScp
        }
    }
    foreach ($sshCommand in @(& $ApplicationCommandLookup 'ssh.exe')) {
        $sshPath = [string] $sshCommand.Source
        if ([string]::IsNullOrWhiteSpace($sshPath) -or
            -not (Test-Path -LiteralPath $sshPath -PathType Leaf)) {
            continue
        }
        $fullSsh = [System.IO.Path]::GetFullPath($sshPath)
        $directory = [System.IO.Path]::GetDirectoryName($fullSsh)
        if (-not $scpByDirectory.ContainsKey($directory)) { continue }
        $alreadySelected = $false
        foreach ($existing in $candidates) {
            if ([string]::Equals(
                    [string] $existing.SshPath,
                    $fullSsh,
                    [System.StringComparison]::OrdinalIgnoreCase)) {
                $alreadySelected = $true
                break
            }
        }
        if ($alreadySelected) { continue }
        [void] $candidates.Add([pscustomobject]@{
                SshPath = $fullSsh
                ScpPath = $scpByDirectory[$directory]
                Source = 'path'
            })
    }

    if ($candidates.Count -lt 1) {
        throw '未找到可配对的 ssh/scp 候选。二进制路径：<none>；版本：<none>；实测截断长度：n/a'
    }

    $selected = $candidates[0]
    $version = [string] (& $VersionReader $selected.SshPath)
    [pscustomobject]@{
        SshPath = [string] $selected.SshPath
        ScpPath = [string] $selected.ScpPath
        Source = [string] $selected.Source
        Version = $version
    }
}

function Resolve-OfficeSshEchoProbeCharacterCount {
    param(
        [Parameter(Mandatory)] [ValidateNotNullOrEmpty()] [string] $SshExecutablePath,
        [Parameter(Mandatory)] [AllowEmptyString()] [string] $SshVersion,
        [Parameter(Mandatory)] [string[]] $Options,
        [Parameter(Mandatory)] [ValidateRange(1, 4096)] [int] $DestinationLength,
        [scriptblock] $StartabilityProbe
    )

    $target = 32768
    $minimum = 22644
    $dummyDestination = New-OfficeSshStartabilityDummyDestination -Length $DestinationLength
    if ($null -eq $StartabilityProbe) {
        $StartabilityProbe = {
            param($FileName, $Arguments)
            Test-OfficeSshProcessArgumentListCanStart -FileName $FileName -Arguments $Arguments
        }
    }

    $testSize = {
        param([int] $Size)
        $command = New-OfficeSshEchoProbeCommand -ProbeCharacterCount $Size
        $arguments = [string[]] (@($Options) + @($dummyDestination, $command.RemoteCommand))
        [bool] (& $StartabilityProbe $SshExecutablePath $arguments)
    }

    if (& $testSize $target) { return $target }
    if (-not (& $testSize $minimum)) {
        throw "SSH 行为探针无法在本机 ProcessStartInfo/ArgumentList 下启动。二进制路径：$SshExecutablePath；版本：$SshVersion；实测截断长度：n/a；最小探针字符数：$minimum"
    }

    $low = $minimum
    $high = $target
    $maxStartable = $minimum
    while ($low -le $high) {
        $mid = [int] (($low + $high) / 2)
        if (& $testSize $mid) {
            $maxStartable = $mid
            $low = $mid + 1
        }
        else {
            $high = $mid - 1
        }
    }
    $maxStartable
}

function Invoke-OfficeSshEchoBehaviorProbe {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [ValidateNotNullOrEmpty()] [string] $SshExecutablePath,
        [Parameter(Mandatory)] [AllowEmptyString()] [string] $SshVersion,
        [Parameter(Mandatory)] [string[]] $Options,
        [Parameter(Mandatory)] [ValidateNotNullOrEmpty()] [string] $Destination,
        [Parameter(Mandatory)] [ValidateRange(22644, 32768)] [int] $ProbeCharacterCount,
        [scriptblock] $CommandRunner
    )

    $probe = New-OfficeSshEchoProbeCommand -ProbeCharacterCount $ProbeCharacterCount
    if ($null -eq $CommandRunner) {
        $CommandRunner = {
            param($FileName, $Arguments)
            Invoke-CountdownExternalCommandProcess -FileName $FileName -Arguments $Arguments
        }
    }
    $arguments = [string[]] (@($Options) + @($Destination, $probe.RemoteCommand))
    try {
        $runnerOutput = @(& $CommandRunner $SshExecutablePath $arguments)
        if ($runnerOutput.Count -ne 1) {
            throw 'SSH 行为探针 command runner 必须返回一个结果对象。'
        }
        $runnerResult = $runnerOutput[0]
        foreach ($name in @('ExitCode', 'StandardOutput', 'StandardError')) {
            if ($null -eq $runnerResult.PSObject.Properties[$name]) {
                throw "SSH 行为探针 command runner 缺少字段：$name"
            }
        }
        $exitCode = [int] $runnerResult.ExitCode
        $actual = ([string] $runnerResult.StandardOutput).Trim()
        if ($exitCode -ne 0 -or $actual -ne $probe.ExpectedOutput) {
            throw "办公室 SSH 行为探针截断或失败。二进制路径：$SshExecutablePath；版本：$SshVersion；实测截断长度：$($actual.Length)；期望长度：$($probe.ExpectedOutput.Length)；退出码：$exitCode"
        }
    }
    catch {
        if ($_.Exception.Message -like '办公室 SSH 行为探针截断或失败。*') { throw }
        throw "办公室 SSH 行为探针截断或失败。二进制路径：$SshExecutablePath；版本：$SshVersion；实测截断长度：n/a；$($_.Exception.Message)"
    }
}

function Write-OfficeSshClientIdentityEvidence {
    param(
        [Parameter(Mandatory)] [ValidateNotNullOrEmpty()] [string] $EvidenceRoot,
        [Parameter(Mandatory)] [object] $Identity,
        [Parameter(Mandatory)] [ValidateRange(22644, 32768)] [int] $ProbeCharacterCount
    )

    if (-not (Test-Path -LiteralPath $EvidenceRoot -PathType Container)) {
        New-Item -ItemType Directory -Path $EvidenceRoot -Force | Out-Null
    }
    $path = Join-Path $EvidenceRoot 'ssh-client-identity.json'
    $payload = [ordered]@{
        sshPath = [string] $Identity.SshPath
        scpPath = [string] $Identity.ScpPath
        source = [string] $Identity.Source
        version = [string] $Identity.Version
        probeCharacterCount = [int] $ProbeCharacterCount
    }
    $temporaryPath = "$path.tmp-$([guid]::NewGuid().ToString('N'))"
    try {
        [System.IO.File]::WriteAllText(
            $temporaryPath,
            ($payload | ConvertTo-Json),
            [System.Text.UTF8Encoding]::new($false)
        )
        [System.IO.File]::Move($temporaryPath, $path, $true)
    }
    finally {
        if (Test-Path -LiteralPath $temporaryPath -PathType Leaf) {
            [System.IO.File]::Delete($temporaryPath)
        }
    }
    $path
}

function Get-OfficeDeploymentStatus {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [ValidatePattern('^[0-9a-fA-F]{40}$')] [string] $Commit,
        [string] $EvidenceBase = 'D:\agents\run-evidence\clock-countdown-deploy'
    )
    $runs = @()
    $commitRoot = Join-Path $EvidenceBase $Commit.Substring(0, 7)
    if (Test-Path -LiteralPath $commitRoot -PathType Container) {
        $runs = @(Get-ChildItem -LiteralPath $commitRoot -Directory |
            Where-Object Name -Match '^\d{17}-[0-9a-f]{8}$' | Sort-Object Name -Descending)
    }
    $results = @(foreach ($run in $runs) {
        $result = [ordered]@{ RunId=$run.Name; EvidenceRoot=$run.FullName; State='Incomplete'; VerifiedAtUtc=$null; Reason=$null; SuccessReceipt=$null; Binding=$null; ExecutableSha256=$null }
        try {
            if (($run.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw '证据目录不得是 reparse point。' }
            $sourceArchive = Join-Path $run.FullName 'commit-source.zip'
            # git archive ZIP ends with the 22-byte EOCD and a 40-byte commit comment.
            # Read that footer without depending on newer ZipArchive.Comment runtimes.
            $archiveItem = Get-Item -LiteralPath $sourceArchive -Force
            if (($archiveItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw '提交快照不得是 reparse point。' }
            $stream = [IO.File]::OpenRead($sourceArchive)
            try {
                if ($stream.Length -lt 62) { throw '提交快照缺少 Git ZIP footer。' }
                [void] $stream.Seek(-62, [IO.SeekOrigin]::End)
                $footer = [byte[]]::new(62)
                if ($stream.Read($footer,0,62) -ne 62 -or [BitConverter]::ToUInt32($footer,0) -ne 0x06054b50 -or
                    [BitConverter]::ToUInt16($footer,20) -ne 40 -or
                    [Text.Encoding]::ASCII.GetString($footer,22,40) -cne $Commit) { throw '提交快照的完整 commit 与目标不一致。' }
            } finally { $stream.Dispose() }
            $success = ConvertFrom-CountdownRemoteDeploymentSuccessReceiptJson (Read-OfficeEvidenceText $run.FullName 'remote-deployment-success.json')
            # Read data literals only; never execute historical scripts or modules.
            $install = Read-OfficeEvidenceText $run.FullName 'remote-install.ps1' -MaxBytes 1048576
            $bindingMatch = [regex]::Matches($install, "(?m)^  DeploymentBindingSha256 = '([A-Fa-f0-9]{64})'\r?$")
            $manifestMatch = [regex]::Matches($install, "(?m)^    \[Convert\]::FromBase64String\('([A-Za-z0-9+/=]+)'\)\)\r?$")
            if ($bindingMatch.Count -ne 1 -or $manifestMatch.Count -ne 1) { throw '安装脚本的绑定或发布清单不唯一。' }
            $manifest = [Text.UTF8Encoding]::new($false,$true).GetString([Convert]::FromBase64String($manifestMatch[0].Groups[1].Value)) | ConvertFrom-Json -AsHashtable
            $exeHash = [string] $manifest['app\MinminCountdown.exe']
            if ($exeHash -notmatch '^[A-Fa-f0-9]{64}$') { throw '安装清单缺少程序哈希。' }
            $binding = ConvertFrom-CountdownRemoteDeploymentBindingJson `
                -Json (Read-OfficeEvidenceText $run.FullName 'deployment-binding.json') `
                -ExpectedDeploymentBindingSha256 $bindingMatch[0].Groups[1].Value -ExpectedProcessId $success.RuntimeProcessId
            $readback = ConvertFrom-CountdownRemoteDeploymentReadbackJson `
                -Json (Read-OfficeEvidenceText $run.FullName 'post-deployment-independent-readback.json') `
                -ExpectedProcessId $success.RuntimeProcessId -ExpectedExecutableSha256 $exeHash `
                -ExpectedTokenSha256 $binding.TokenSha256 -ExpectedSettingsSha256 $success.SettingsHashAfter
            if ($readback.SettingsLength -ne $success.SettingsLengthAfter -or
                $readback.SettingsMtimeUtc -cne $success.SettingsMtimeUtcAfter -or
                $readback.SettingsUnchanged -ne $success.SettingsUnchanged) { throw '设置元数据与成功回执不匹配。' }
            $ledger = Read-OfficeEvidenceText $run.FullName 'transfer-attempts.json' | ConvertFrom-Json -DateKind String
            $ids = @('deployment-package','remote-install-script','remote-deployment-binding','post-deployment-independent-readback-script','post-deployment-independent-readback')
            $methods = @('scp','scp','ssh','scp','ssh')
            if ($ledger.transferAttemptsVersion -cne '1.0' -or @($ledger.attempts).Count -ne 5) { throw '传输账本版本或条目数量不匹配。' }
            for ($i=0; $i -lt $ids.Count; $i++) {
                $attempt = $ledger.attempts[$i]
                if ($attempt.id -cne $ids[$i] -or $attempt.method -cne $methods[$i] -or
                    $attempt.status -cne 'succeeded' -or $attempt.exitCode -ne 0 -or
                    $attempt.transferStarted -isnot [bool] -or -not $attempt.transferStarted -or
                    $attempt.bytesVerified -isnot [bool] -or -not $attempt.bytesVerified -or
                    $attempt.metrics.bytes -le 0 -or $attempt.metrics.elapsed_seconds -le 0) { throw '传输账本未全部成功。' }
            }
            [void] [DateTimeOffset]::Parse($ledger.generatedAtUtc, [Globalization.CultureInfo]::InvariantCulture)
            $result.State = 'HistoricallyVerified'
            $result.VerifiedAtUtc = $ledger.generatedAtUtc
            $result.SuccessReceipt = $success
            $result.Binding = $binding
            $result.ExecutableSha256 = $exeHash
        }
        catch { $result.Reason = $_.Exception.Message }
        [pscustomobject] $result
    })
    $complete = @($results | Where-Object State -EQ 'HistoricallyVerified' | Select-Object -First 1)
    [pscustomobject]@{
        Commit = $Commit
        State = if ($complete.Count) { 'HistoricallyVerified' } elseif ($runs.Count -eq 0) { 'NotFound' } else { 'Incomplete' }
        LiveVerified = $false
        LatestComplete = if ($complete.Count) { $complete[0] } else { $null }
        Runs = @($results | Select-Object RunId,EvidenceRoot,State,VerifiedAtUtc,Reason)
    }
}

function Read-OfficeEvidenceText {
    param([string] $Root, [string] $Name, [long] $MaxBytes = 32768)
    $file = Get-Item -LiteralPath (Join-Path $Root $Name) -Force
    if ($file -isnot [IO.FileInfo] -or $file.Length -gt $MaxBytes -or
        ($file.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw "证据文件无效：$Name" }
    [Text.UTF8Encoding]::new($false,$true).GetString([IO.File]::ReadAllBytes($file.FullName))
}

function Write-OfficeDeploymentProgress {
    param([Parameter(Mandatory)] [string] $Stage)
    [Console]::Error.WriteLine("[office-deploy] $([DateTime]::UtcNow.ToString('o')) $Stage")
}

if ($LoadFunctionsOnly) { return }

if (@(@($ValidateOnly,$StatusOnly,$RecheckLatest,$Redeploy) | Where-Object { $_.IsPresent }).Count -gt 1) {
    throw 'ValidateOnly、StatusOnly、RecheckLatest、Redeploy 不能组合使用。'
}

if ($ValidateOnly) {
    $releaseContractPath = Join-Path $repoRoot 'Countdown.ReleaseContract.psm1'
    Import-Module $releaseContractPath -Force | Out-Null
    $releaseContract = Get-CountdownReleaseContract
    $packageValidation = Assert-CountdownReleasePackage -Root $repoRoot
    $temporaryArchiveRoot = 'D:\Codex\.codex\tmp'
    New-Item -ItemType Directory -Path $temporaryArchiveRoot -Force | Out-Null
    $temporaryArchivePath = Join-Path `
        $temporaryArchiveRoot `
        "clock-countdown-working-tree-$([guid]::NewGuid().ToString('N')).zip"
    $candidateArchiveValidation = Invoke-CountdownWorkingTreeArchiveValidation `
        -RepositoryRoot $repoRoot `
        -RelativePaths ([string[]] @($releaseContract.PackageFiles)) `
        -TemporaryArchivePath $temporaryArchivePath
    [pscustomobject]@{
        Valid = $true
        CandidateSource = 'WorkingTree'
        ArchiveSha256 = $candidateArchiveValidation.ArchiveSha256
        VerifiedFiles = $packageValidation.VerifiedFiles
        PackageFiles = $candidateArchiveValidation.ArchiveFiles
        TemporaryArchiveRetained = Test-Path -LiteralPath $temporaryArchivePath
        Deployed = $false
    } | ConvertTo-Json
    exit 0
}

$gitPath = (Get-Command git.exe -CommandType Application -ErrorAction Stop |
    Select-Object -First 1 -ExpandProperty Source)
$statusCommit = Get-CountdownDeploymentGitScalar -GitPath $gitPath -RepositoryRoot $repoRoot -Arguments @('rev-parse','--verify','HEAD')
$statusIdentities = New-CountdownDeploymentCommitIdentityManifest -GitPath $gitPath -RepositoryRoot $repoRoot -Commit $statusCommit `
    -RelativePaths @('Countdown.TransferLedger.psm1','Countdown.RemoteDeployment.psm1')
Invoke-CountdownDeploymentLockedCommitFile -GitPath $gitPath -RepositoryRoot $repoRoot -Root $repoRoot `
    -RelativePath 'Countdown.TransferLedger.psm1' -ExpectedIdentity $statusIdentities['Countdown.TransferLedger.psm1'] `
    -Stage '状态查询依赖校验' -Action {
        param($lockedLedgerPath,$sha256)
        Import-Module $lockedLedgerPath -Force -Global | Out-Null
        Invoke-CountdownDeploymentLockedCommitFile -GitPath $gitPath -RepositoryRoot $repoRoot -Root $repoRoot `
            -RelativePath 'Countdown.RemoteDeployment.psm1' -ExpectedIdentity $statusIdentities['Countdown.RemoteDeployment.psm1'] `
            -Stage '状态查询模块校验' -Action { param($lockedPath,$sha256) Import-Module $lockedPath -Force -Global | Out-Null }
    }
$deploymentStatus = Get-OfficeDeploymentStatus -Commit $statusCommit
if ($StatusOnly) {
    $deploymentStatus | ConvertTo-Json -Depth 8
    exit 0
}
if ($RecheckLatest -or ($null -ne $deploymentStatus.LatestComplete -and -not $Redeploy)) {
    if ($null -eq $deploymentStatus.LatestComplete) { throw '没有通过本地交叉校验的完整成功记录；只读复核不会启动安装。' }
    if (-not $RecheckLatest) {
        [void] (Invoke-CountdownDeploymentGit -GitPath $gitPath -RepositoryRoot $repoRoot -Arguments @('fetch','--no-tags','origin','main'))
        $published = Get-CountdownDeploymentGitScalar -GitPath $gitPath -RepositoryRoot $repoRoot -Arguments @('rev-parse','--verify','refs/remotes/origin/main')
        $branch = Get-CountdownDeploymentGitScalar -GitPath $gitPath -RepositoryRoot $repoRoot -Arguments @('branch','--show-current')
        if ($published -ne $statusCommit -or $branch -ne 'main') { throw '部署目标必须是与 origin/main 一致的 main。' }
    }
    $completed = $deploymentStatus.LatestComplete
    Write-OfficeDeploymentProgress "发现历史成功记录 $($completed.RunId)，验证时间 $($completed.VerifiedAtUtc)；开始只读远端复核"
    . 'D:\Codex\.codex\credentials\ssh\replicator\office\Use-OfficeSshHelper.ps1' | Out-Null
    $destination = [string] $env:REPLICATOR_OFFICE_SSH_DESTINATION
    $separator = $destination.LastIndexOf('@')
    if ($separator -lt 1 -or $separator -ge $destination.Length-1) { throw '受管 office helper 返回的 SSH destination 无效。' }
    $script:sshOptions = @(
        '-i', $env:REPLICATOR_OFFICE_SSH_IDENTITY_FILE,
        '-o', 'BatchMode=yes', '-o', 'IdentitiesOnly=yes', '-o', 'StrictHostKeyChecking=yes',
        '-o', "ConnectTimeout=$env:REPLICATOR_OFFICE_SSH_CONNECT_TIMEOUT_SECONDS",
        '-o', "ConnectionAttempts=$env:REPLICATOR_OFFICE_SSH_CONNECTION_ATTEMPTS",
        '-o', "UserKnownHostsFile=$env:REPLICATOR_OFFICE_SSH_USER_KNOWN_HOSTS_FILE",
        '-o', "KexAlgorithms=$env:REPLICATOR_OFFICE_SSH_KEX_ALGORITHMS")
    $identity = Resolve-OfficeSshClientIdentity
    $script:sshExecutablePath = $identity.SshPath
    $remoteRoot = "D:\Codex\deploy\clock-countdown-$($statusCommit.Substring(0,7))-$($completed.RunId)"
    $checkRoot = Join-Path $completed.EvidenceRoot "rechecks/$([guid]::NewGuid().ToString('N'))"
    [void] (New-Item -ItemType Directory -Path $checkRoot -Force)
    $adapter = New-CountdownRemoteDeploymentReadbackSshAdapter -Options $script:sshOptions -Destination $destination `
        -RemoteRoot $remoteRoot -ScpExecutablePath $identity.ScpPath -ScpOptions $script:sshOptions `
        -CommandRunner { param($FileName,$Arguments) Invoke-CountdownExternalCommandProcess -FileName $script:sshExecutablePath -Arguments $Arguments }
    # Reuse the already uploaded reader. The existing short SSH wrapper checks its hash
    # against the freshly generated reader before execution. No remote writes or installs.
    $existingReader = Join-Path $remoteRoot 'post-deployment-independent-readback.ps1'
    $adapter.UploadIndependentReadbackScript = { param($Context) $existingReader }.GetNewClosure()
    try {
        [void] (Invoke-CountdownRemoteDeploymentIndependentReadback `
            -LedgerPath (Join-Path $checkRoot 'transfer-attempts.json') `
            -DeploymentBindingEvidencePath (Join-Path $checkRoot 'deployment-binding.json') `
            -IndependentReadbackEvidencePath (Join-Path $checkRoot 'post-deployment-independent-readback.json') `
            -RemoteDeploymentBindingPath (Join-Path $remoteRoot 'deployment-binding.json') `
            -DeploymentBindingSha256 $completed.Binding.DeploymentBindingSha256 -SuccessReceipt $completed.SuccessReceipt `
            -ExpectedExecutableSha256 $completed.ExecutableSha256 -ExpectedRemoteHost $destination.Substring($separator+1) `
            -ExpectedRemoteUser $destination.Substring(0,$separator) -Adapter $adapter)
    }
    catch { throw "历史部署当前复核失败；未安装、未回滚。证据：$checkRoot；$($_.Exception.Message)" }
    Write-OfficeDeploymentProgress '只读复核完成；沿用已部署版本'
    [pscustomobject]@{ Commit=$statusCommit; RunId=$completed.RunId; LiveVerified=$true; VerifiedAtUtc=[DateTime]::UtcNow.ToString('o'); ReusedDeployment=$true; EvidenceRoot=$checkRoot } | ConvertTo-Json
    exit 0
}
Write-OfficeDeploymentProgress "历史证据状态：$($deploymentStatus.State)；开始部署源检查"
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
$evidenceRoot = "D:\agents\run-evidence\clock-countdown-deploy\$shortCommit\$runId"
$sourceArchive = Join-Path $evidenceRoot 'commit-source.zip'
$sourceRoot = Join-Path $evidenceRoot 'commit-source'
$packageRoot = Join-Path $evidenceRoot 'windows-package'
$extractRoot = Join-Path $evidenceRoot 'package-check'
$archive = Join-Path $evidenceRoot 'clock-countdown-windows.zip'
$transferLedgerPath = Join-Path $evidenceRoot 'transfer-attempts.json'
New-Item -ItemType Directory -Path $evidenceRoot -Force | Out-Null
Write-OfficeDeploymentProgress "提交快照与构建：$shortCommit / $runId"
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
[void] (Invoke-CountdownDeploymentLockedCommitFile `
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
    })

# 构建会在快照目录生成 app 载荷与 published-payload.json；契约的 PackageFiles
# 依赖该清单，因此必须在构建后重新取契约快照，打包和 ZIP 复核才用同一份文件集。
$releaseContract = Get-CountdownReleaseContract
$requiredFiles = [string[]] @($releaseContract.PackageFiles)
if ($requiredFiles -notcontains 'app\published-payload.json') {
    throw '构建后：发布契约未包含运行时载荷清单，拒绝打包。'
}

Assert-CountdownDeploymentRepositoryAndManifest `
    -GitPath $gitPath `
    -RepositoryRoot $repoRoot `
    -Stage '构建后' `
    -ExpectedCommit $commit `
    -ManifestRoot $sourceRoot `
    -Manifest $controlManifest
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
        # 构建产物（verification exe）不进静态 IntegrityHashes，按本次构建字节锁定。
        $builtPath = Join-Path $sourceRoot $file
        if (-not (Test-Path -LiteralPath $builtPath -PathType Leaf)) {
            throw "发布包文件缺少提交或发布契约身份：$file"
        }
        $packageManifest[$file] = [string] (Get-FileHash -Algorithm SHA256 -LiteralPath $builtPath).Hash
    }
}
Assert-CountdownDeploymentRepositoryAndManifest `
    -GitPath $gitPath `
    -RepositoryRoot $repoRoot `
    -Stage '打包前' `
    -ExpectedCommit $commit `
    -ManifestRoot $sourceRoot `
    -Manifest $controlManifest

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
Assert-CountdownDeploymentRepositoryAndManifest `
    -GitPath $gitPath `
    -RepositoryRoot $repoRoot `
    -Stage '打包后' `
    -ExpectedCommit $commit `
    -ManifestRoot $sourceRoot `
    -Manifest $packageManifest `
    -ManifestFirst

$archiveHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $archive).Hash
Write-OfficeDeploymentProgress '构建及发布包校验完成；检查远端连接'

Assert-CountdownDeploymentRepositoryAndManifest `
    -GitPath $gitPath `
    -RepositoryRoot $repoRoot `
    -Stage '远程输出前' `
    -ExpectedCommit $commit `
    -ManifestRoot $extractRoot `
    -Manifest $packageManifest
if ((Get-FileHash -Algorithm SHA256 -LiteralPath $archive).Hash -ne $archiveHash) {
    throw '远程输出前：ZIP 已变化，拒绝部署。'
}
$transferLedgerSha256 = Invoke-CountdownDeploymentLockedCommitFile `
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
$officeSshIdentity = Resolve-OfficeSshClientIdentity
$script:sshExecutablePath = [string] $officeSshIdentity.SshPath
$script:scpExecutablePath = [string] $officeSshIdentity.ScpPath
$officeSshProbeCharacterCount = Resolve-OfficeSshEchoProbeCharacterCount `
    -SshExecutablePath $script:sshExecutablePath `
    -SshVersion ([string] $officeSshIdentity.Version) `
    -Options $script:sshOptions `
    -DestinationLength $officeDestination.Length
Write-OfficeSshClientIdentityEvidence `
    -EvidenceRoot $evidenceRoot `
    -Identity $officeSshIdentity `
    -ProbeCharacterCount $officeSshProbeCharacterCount | Out-Null
Invoke-OfficeSshEchoBehaviorProbe `
    -SshExecutablePath $script:sshExecutablePath `
    -SshVersion ([string] $officeSshIdentity.Version) `
    -Options $script:sshOptions `
    -Destination $officeDestination `
    -ProbeCharacterCount $officeSshProbeCharacterCount
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
    -ScpExecutablePath $script:scpExecutablePath `
    -RemoteBytesProbe $remoteBytesProbe | Out-Null

$expectedFileManifestJson = $packageManifest | ConvertTo-Json -Compress
Write-OfficeDeploymentProgress '部署包上传完成；准备安装控制脚本'
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
    -ScpExecutablePath $script:scpExecutablePath `
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
    Write-OfficeDeploymentProgress '上传完成；远端安装及事务验收中'
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
Write-OfficeDeploymentProgress '远端事务已提交；执行两次独立回读'
$expectedExecutableSha256 = [string] $releaseContract.IntegrityHashes['app\MinminCountdown.exe']
$postDeploymentReadbackAdapter = New-CountdownRemoteDeploymentReadbackSshAdapter `
    -Options $script:sshOptions `
    -Destination $officeDestination `
    -RemoteRoot $remoteRoot `
    -ScpExecutablePath $script:scpExecutablePath `
    -ScpOptions $script:sshOptions `
    -RemoteBytesProbe $remoteBytesProbe `
    -CommandRunner {
        param($FileName, $Arguments)
        Invoke-CountdownExternalCommandProcess -FileName $script:sshExecutablePath -Arguments $Arguments
    }
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
Write-OfficeDeploymentProgress '部署及独立回读全部完成'
$successReceipt | ConvertTo-Json -Compress -Depth 4
