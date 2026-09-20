#requires -Version 7.0

<#
.SYNOPSIS
    标准化 13 步收口验证 runner。单目录、单次不间断全绿。

.DESCRIPTION
    在 D:\agents\run-evidence\clock-countdown-verification\<namespace>\<runId>\
    写入本次 attempt 证据。先创建目录并拷贝 runner，再写入 meta.json（status=running，含 runnerSha256），随后补 endUtc / 终态。

    约束：
    - namespace 仅为 soak 或 full；默认 full。soak 不抵扣收口。
    - runId 必须唯一；已存在的 attempt 目录永不覆盖，需换新 runId。
    - 13 步按固定顺序单次执行；任一步失败则整链作废，后续步不跑，保留现场。
    - 默认不重试。-Retry 必须保持关闭；打开则拒绝执行。
    - 不把 settings.json、便签或凭据拷进 evidence，不写 C:\。

    13 步（顺序不可增减）：
    1. env-pre：Countdown.ReleaseContract.psm1 IntegrityHashes 工作树 vs 契约钉（fail-fast，不继续 build）
    2. git-diff-check：git diff --check
    3. build：pwsh -NoLogo -NoProfile -File Build-CountdownApplication.ps1 -Clean
    4. pin-after-build：verification\MinminCountdown.Verification.exe 静态 pin（契约已钉时核对，未钉则跳过）与 app\MinminCountdown.exe 现役条目（fail-fast；本 runner 不改 ReleaseContract）
    5. desktop-launcher：pwsh -NoLogo -NoProfile -STA -File Countdown.CSharp.Tests.ps1 -DesktopLauncherOnly
    6. csharp-default：pwsh -NoLogo -NoProfile -STA -File Countdown.CSharp.Tests.ps1
    7. instance-activation-matrix：钉 7.6.4 pwsh -NoLogo -NoProfile -STA -File Countdown.CSharp.Tests.ps1 -InstanceActivationMatrix
    8. core：pwsh -NoLogo -NoProfile -File Countdown.Core.Tests.ps1
    9. transfer-ledger：pwsh -NoLogo -NoProfile -File Countdown.TransferLedger.Tests.ps1
    10. ui：pwsh -NoLogo -NoProfile -STA -File Countdown.Ui.Tests.ps1
    11. source-validation：pwsh -NoLogo -NoProfile -STA -File Invoke-CountdownSourceValidation.ps1
    12. install-validate：pwsh -NoLogo -NoProfile -File Install-Countdown.ps1 -ValidateOnly
    13. deploy-validate：pwsh -NoLogo -NoProfile -File Deploy-OfficeCountdown.ps1 -ValidateOnly

    非矩阵步优先使用 D:\Codex\tools\PowerShell\7.6.5\pwsh.exe；矩阵步固定 7.6.4。不修改 ExecutionPolicy。

.PARAMETER Namespace
    soak 或 full。默认 full。soak 不抵扣收口。

.PARAMETER RunId
    本次 attempt 标识，必须唯一。省略则使用 UTC yyyyMMddTHHmmssZ 加短随机后缀。
    已存在的 runId 会被拒绝，不会覆盖。

.PARAMETER DryRun
    创建 attempt 目录、先写 meta、拷贝 runner 并列出 13 步，不执行验证。

.PARAMETER Retry
    必须关闭。默认关闭。打开则拒绝执行；失败后请换新 runId 重跑。

.PARAMETER Help
    显示本帮助后退出 0。也可用 -?（以命令方式调用时）或 Get-Help。

.EXAMPLE
    pwsh.exe -NoLogo -NoProfile -File ./tools/Invoke-CountdownVerificationRun.ps1 -Help

.EXAMPLE
    pwsh.exe -NoLogo -NoProfile -File ./tools/Invoke-CountdownVerificationRun.ps1 -Namespace soak -DryRun
#>
[CmdletBinding()]
param(
    [Parameter()]
    [ValidateSet('soak', 'full')]
    [string] $Namespace = 'full',

    [Parameter()]
    [string] $RunId,

    [Parameter()]
    [switch] $DryRun,

    [Parameter()]
    [switch] $Retry,

    [Parameter()]
    [switch] $AllowForegroundWindowedSteps,

    [Parameter()]
    [Alias('h')]
    [switch] $Help
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false

$script:EvidenceRoot = 'D:\agents\run-evidence\clock-countdown-verification'
$script:ManagedPwshPreferred = 'D:\Codex\tools\PowerShell\7.6.5\pwsh.exe'
$script:MatrixPwsh = 'D:\Codex\tools\PowerShell\7.6.4\pwsh.exe'
$script:Utf8NoBom = [System.Text.UTF8Encoding]::new($false)
$script:AttemptDir = $null
$script:MetaPath = $null
$script:Meta = $null
$script:ExpectedPins = $null

function Show-CountdownVerificationRunHelp {
    @'
SYNOPSIS
    标准化 13 步收口验证 runner。单目录、单次不间断全绿。

参数
    -Namespace  soak | full（默认 full）。soak 不抵扣收口。
    -RunId      必须唯一。省略则用 UTC yyyyMMddTHHmmssZ + 短随机。禁止覆盖已有 attempt。
    -DryRun     只写 meta、拷贝 runner、列出 13 步，不跑验证。
    -Retry      必须保持关闭。打开则拒绝。默认不重试。
    -AllowForegroundWindowedSteps
                前台跑 desktop-launcher / instance-activation-matrix。
                用户正在用电脑时禁止。默认拒绝这两步。
    -Help       显示本帮助。

约束
    attempt 目录永不覆盖。任一步红 = 整链作废，需新 runId 重跑。
    meta 先写（status=running）后补。runner 自拷贝并记录 SHA-256。失败保留现场。
    不写 C:\。不把 settings.json / 便签 / 凭据拷进 evidence。不改 ExecutionPolicy。

13 步（顺序不可增减）
    1. env-pre                      IntegrityHashes 工作树 vs 契约钉（fail-fast，不继续 build）
    2. git-diff-check               git diff --check
    3. build                        pwsh -NoLogo -NoProfile -File Build-CountdownApplication.ps1 -Clean
    4. pin-after-build              verification\MinminCountdown.Verification.exe 静态 pin（契约已钉时核对，未钉则跳过）
                                    与 app\MinminCountdown.exe 现役哈希（fail-fast；不改 ReleaseContract）
    5. desktop-launcher             pwsh -NoLogo -NoProfile -STA -File Countdown.CSharp.Tests.ps1 -DesktopLauncherOnly
    6. csharp-default               pwsh -NoLogo -NoProfile -STA -File Countdown.CSharp.Tests.ps1
    7. instance-activation-matrix   钉 7.6.4 pwsh -NoLogo -NoProfile -STA -File Countdown.CSharp.Tests.ps1 -InstanceActivationMatrix
    8. core                         pwsh -NoLogo -NoProfile -File Countdown.Core.Tests.ps1
    9. transfer-ledger              pwsh -NoLogo -NoProfile -File Countdown.TransferLedger.Tests.ps1
    10. ui                          pwsh -NoLogo -NoProfile -STA -File Countdown.Ui.Tests.ps1
    11. source-validation           pwsh -NoLogo -NoProfile -STA -File Invoke-CountdownSourceValidation.ps1
    12. install-validate             pwsh -NoLogo -NoProfile -File Install-Countdown.ps1 -ValidateOnly
    13. deploy-validate             pwsh -NoLogo -NoProfile -File Deploy-OfficeCountdown.ps1 -ValidateOnly

证据根
    D:\agents\run-evidence\clock-countdown-verification\<namespace>\<runId>\
'@
}

function Write-CountdownUtf8File {
    param(
        [Parameter(Mandatory)] [string] $LiteralPath,
        [AllowNull()] [string] $Content
    )

    $text = if ($null -eq $Content) { '' } else { $Content }
    $directory = Split-Path -Parent $LiteralPath
    if (-not (Test-Path -LiteralPath $directory -PathType Container)) {
        throw "证据目录不存在：$directory"
    }
    [System.IO.File]::WriteAllText($LiteralPath, $text, $script:Utf8NoBom)
}

function Save-CountdownVerificationMeta {
    param([Parameter()] [hashtable] $Patch)

    if ([string]::IsNullOrWhiteSpace($script:MetaPath) -or $null -eq $script:Meta) {
        throw 'meta.json 尚未初始化。'
    }
    if ($null -ne $Patch) {
        foreach ($key in $Patch.Keys) {
            $script:Meta[$key] = $Patch[$key]
        }
    }
    Write-CountdownUtf8File -LiteralPath $script:MetaPath -Content ($script:Meta | ConvertTo-Json -Depth 8)
}

function Test-CountdownSafeRunId {
    param([Parameter(Mandatory)] [string] $Value)

    if ($Value -notmatch '^[A-Za-z0-9][A-Za-z0-9._-]{0,127}$') {
        throw "非法 RunId（只允许字母数字、点、下划线和连字符）：$Value"
    }
}

function New-CountdownVerificationRunId {
    $utc = [datetime]::UtcNow
    $stamp = $utc.ToString("yyyyMMdd'T'HHmmss'Z'", [cultureinfo]::InvariantCulture)
    $suffix = [guid]::NewGuid().ToString('N').Substring(0, 8)
    '{0}-{1}' -f $stamp, $suffix
}

function Resolve-CountdownManagedPwsh {
    if (Test-Path -LiteralPath $script:ManagedPwshPreferred -PathType Leaf) {
        return $script:ManagedPwshPreferred
    }

    $command = Get-Command -Name pwsh.exe -CommandType Application -ErrorAction Stop | Select-Object -First 1
    $resolved = [string] $command.Source
    if ([string]::IsNullOrWhiteSpace($resolved) -or -not (Test-Path -LiteralPath $resolved -PathType Leaf)) {
        throw '未找到 pwsh.exe；本 runner 只支持受管 PowerShell 7。'
    }
    $full = [System.IO.Path]::GetFullPath($resolved)
    if ($full.IndexOf('\WindowsPowerShell\', [System.StringComparison]::OrdinalIgnoreCase) -ge 0) {
        throw "拒绝 Windows PowerShell 5.1：$full"
    }
    return $full
}

function Resolve-CountdownMatrixPwsh {
    if (-not (Test-Path -LiteralPath $script:MatrixPwsh -PathType Leaf)) {
        throw "未找到矩阵钉 PowerShell 7.6.4：$($script:MatrixPwsh)"
    }
    return $script:MatrixPwsh
}

function Resolve-CountdownRepoPath {
    param(
        [Parameter(Mandatory)] [string] $Root,
        [Parameter(Mandatory)] [string] $RelativePath
    )

    if ([string]::IsNullOrWhiteSpace($RelativePath) -or
        [System.IO.Path]::IsPathRooted($RelativePath) -or
        $RelativePath.Contains('/') -or
        $RelativePath.Contains(':')) {
        throw "拒绝不安全相对路径：$RelativePath"
    }

    $rootPath = [System.IO.Path]::GetFullPath($Root)
    $resolved = [System.IO.Path]::GetFullPath((Join-Path $rootPath $RelativePath))
    $prefix = if ($rootPath.EndsWith('\')) { $rootPath } else { $rootPath + '\' }
    if (-not $resolved.StartsWith($prefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "路径逃出仓库根：$RelativePath"
    }
    if ($resolved.StartsWith('C:\', [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "拒绝写入或引用 C:\：$resolved"
    }
    return $resolved
}

function Test-CountdownGeneratedIntegrityPath {
    param([Parameter(Mandatory)] [string] $RelativePath)

    return $RelativePath.StartsWith('app\', [System.StringComparison]::OrdinalIgnoreCase) -or
        $RelativePath.StartsWith('verification\', [System.StringComparison]::OrdinalIgnoreCase)
}

function Invoke-CountdownCapturedProcess {
    param(
        [Parameter(Mandatory)] [string] $FilePath,
        [Parameter(Mandatory)] [string[]] $ArgumentList,
        [Parameter(Mandatory)] [string] $WorkingDirectory
    )

    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $FilePath
    $startInfo.WorkingDirectory = $WorkingDirectory
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.StandardOutputEncoding = $script:Utf8NoBom
    $startInfo.StandardErrorEncoding = $script:Utf8NoBom
    foreach ($argument in $ArgumentList) {
        [void] $startInfo.ArgumentList.Add($argument)
    }

    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    try {
        if (-not $process.Start()) {
            throw "无法启动进程：$FilePath"
        }
        $stdoutTask = $process.StandardOutput.ReadToEndAsync()
        $stderrTask = $process.StandardError.ReadToEndAsync()
        $process.WaitForExit()
        return [pscustomobject]@{
            ExitCode = [int] $process.ExitCode
            Stdout = [string] $stdoutTask.GetAwaiter().GetResult()
            Stderr = [string] $stderrTask.GetAwaiter().GetResult()
        }
    }
    finally {
        $process.Dispose()
    }
}

function Invoke-CountdownIsolatedPwshStep {
    param(
        [Parameter(Mandatory)] [string] $Engine,
        [Parameter(Mandatory)] [string[]] $ArgumentList,
        [Parameter(Mandatory)] [string] $WorkingDirectory,
        [Parameter(Mandatory)] [string] $StepDir,
        [Parameter(Mandatory)] [string] $DesktopName
    )

    $stdoutPath = Join-Path $StepDir 'isolated.stdout.txt'
    $stderrPath = Join-Path $StepDir 'isolated.stderr.txt'
    $launcher = Join-Path $PSScriptRoot 'Start-IsolatedSuiteProcess.ps1'
    $output = @(
        & $launcher `
            -FilePath $Engine `
            -ArgumentList $ArgumentList `
            -WorkingDirectory $WorkingDirectory `
            -StdoutPath $stdoutPath `
            -StderrPath $stderrPath `
            -DesktopName $DesktopName
    )
    $exitCode = [int] ($output | Select-Object -Last 1)
    $stdout = if (Test-Path -LiteralPath $stdoutPath -PathType Leaf) {
        [System.IO.File]::ReadAllText($stdoutPath, $script:Utf8NoBom)
    }
    else {
        ''
    }
    $stderr = if (Test-Path -LiteralPath $stderrPath -PathType Leaf) {
        [System.IO.File]::ReadAllText($stderrPath, $script:Utf8NoBom)
    }
    else {
        ''
    }
    [pscustomobject]@{
        ExitCode = $exitCode
        Stdout = $stdout
        Stderr = $stderr
    }
}

function Get-CountdownGitState {
    param(
        [Parameter(Mandatory)] [string] $ProjectRoot,
        [Parameter(Mandatory)] [string] $GitPath
    )

    $head = Invoke-CountdownCapturedProcess -FilePath $GitPath -WorkingDirectory $ProjectRoot -ArgumentList @('rev-parse', 'HEAD')
    if ($head.ExitCode -ne 0) {
        throw "git rev-parse HEAD 失败：$($head.Stderr)"
    }

    $status = Invoke-CountdownCapturedProcess -FilePath $GitPath -WorkingDirectory $ProjectRoot -ArgumentList @('status', '--porcelain')
    if ($status.ExitCode -ne 0) {
        throw "git status --porcelain 失败：$($status.Stderr)"
    }

    [pscustomobject]@{
        Head = ([string] $head.Stdout).Trim()
        WorktreeDirty = -not [string]::IsNullOrWhiteSpace([string] $status.Stdout)
    }
}

function Get-CountdownVerificationSteps {
    @(
        [pscustomobject]@{ Index = 1; Id = 'env-pre'; Kind = 'env-pre'; Sta = $false; Engine = 'in-process'; ScriptName = ''; Arguments = @(); Isolation = 'none' }
        [pscustomobject]@{ Index = 2; Id = 'git-diff-check'; Kind = 'git'; Sta = $false; Engine = 'git'; ScriptName = ''; Arguments = @('diff', '--check'); Isolation = 'none' }
        [pscustomobject]@{ Index = 3; Id = 'build'; Kind = 'pwsh'; Sta = $false; Engine = 'managed'; ScriptName = 'Build-CountdownApplication.ps1'; Arguments = @('-Clean'); Isolation = 'none' }
        [pscustomobject]@{ Index = 4; Id = 'pin-after-build'; Kind = 'pin-after-build'; Sta = $false; Engine = 'in-process'; ScriptName = ''; Arguments = @(); Isolation = 'none' }
        [pscustomobject]@{ Index = 5; Id = 'desktop-launcher'; Kind = 'pwsh'; Sta = $true; Engine = 'managed'; ScriptName = 'Countdown.CSharp.Tests.ps1'; Arguments = @('-DesktopLauncherOnly'); Isolation = 'foreground-authorized' }
        [pscustomobject]@{ Index = 6; Id = 'csharp-default'; Kind = 'pwsh'; Sta = $true; Engine = 'managed'; ScriptName = 'Countdown.CSharp.Tests.ps1'; Arguments = @(); Isolation = 'isolated-desktop'; IsolatedDesktopName = 'MinminSuiteIso-VerifyCSharp' }
        [pscustomobject]@{ Index = 7; Id = 'instance-activation-matrix'; Kind = 'pwsh'; Sta = $true; Engine = 'matrix'; ScriptName = 'Countdown.CSharp.Tests.ps1'; Arguments = @('-InstanceActivationMatrix'); Isolation = 'foreground-authorized' }
        [pscustomobject]@{ Index = 8; Id = 'core'; Kind = 'pwsh'; Sta = $false; Engine = 'managed'; ScriptName = 'Countdown.Core.Tests.ps1'; Arguments = @(); Isolation = 'none' }
        [pscustomobject]@{ Index = 9; Id = 'transfer-ledger'; Kind = 'pwsh'; Sta = $false; Engine = 'managed'; ScriptName = 'Countdown.TransferLedger.Tests.ps1'; Arguments = @(); Isolation = 'none' }
        [pscustomobject]@{ Index = 10; Id = 'ui'; Kind = 'pwsh'; Sta = $true; Engine = 'managed'; ScriptName = 'Countdown.Ui.Tests.ps1'; Arguments = @(); Isolation = 'isolated-desktop'; IsolatedDesktopName = 'MinminSuiteIso-VerifyUi' }
        [pscustomobject]@{ Index = 11; Id = 'source-validation'; Kind = 'pwsh'; Sta = $true; Engine = 'managed'; ScriptName = 'Invoke-CountdownSourceValidation.ps1'; Arguments = @(); Isolation = 'isolated-desktop'; IsolatedDesktopName = 'MinminSuiteIso-VerifySourceValidation' }
        [pscustomobject]@{ Index = 12; Id = 'install-validate'; Kind = 'pwsh'; Sta = $false; Engine = 'managed'; ScriptName = 'Install-Countdown.ps1'; Arguments = @('-ValidateOnly'); Isolation = 'none' }
        [pscustomobject]@{ Index = 13; Id = 'deploy-validate'; Kind = 'pwsh'; Sta = $false; Engine = 'managed'; ScriptName = 'Deploy-OfficeCountdown.ps1'; Arguments = @('-ValidateOnly'); Isolation = 'none' }
    )
}

function Get-CountdownStepCommandTokens {
    param(
        [Parameter(Mandatory)] $Step,
        [Parameter(Mandatory)] [string] $ManagedPwsh,
        [Parameter(Mandatory)] [string] $MatrixPwsh,
        [Parameter(Mandatory)] [string] $GitPath
    )

    switch ([string] $Step.Kind) {
        'env-pre' { @('in-process', 'IntegrityHashes') }
        'pin-after-build' { @('in-process', 'verification.exe+apphost-pin') }
        'git' { @($GitPath) + @($Step.Arguments) }
        'pwsh' {
            $engine = if ([string] $Step.Engine -eq 'matrix') { $MatrixPwsh } else { $ManagedPwsh }
            $tokens = [System.Collections.Generic.List[string]]::new()
            if ([string] $Step.Isolation -eq 'isolated-desktop') {
                [void] $tokens.Add((Join-Path $PSScriptRoot 'Start-IsolatedSuiteProcess.ps1'))
                [void] $tokens.Add('-DesktopName')
                [void] $tokens.Add([string] $Step.IsolatedDesktopName)
            }
            [void] $tokens.Add($engine)
            [void] $tokens.Add('-NoLogo')
            [void] $tokens.Add('-NoProfile')
            if ([bool] $Step.Sta) {
                [void] $tokens.Add('-STA')
            }
            [void] $tokens.Add('-File')
            [void] $tokens.Add('.\' + [string] $Step.ScriptName)
            foreach ($argument in @($Step.Arguments)) {
                [void] $tokens.Add([string] $argument)
            }
            if ([string] $Step.Isolation -eq 'foreground-authorized') {
                [void] $tokens.Add('#requires-AllowForegroundWindowedSteps')
            }
            @($tokens)
        }
        default { throw "未知步骤类型：$($Step.Kind)" }
    }
}

function Write-CountdownStepEvidence {
    param(
        [Parameter(Mandatory)] [string] $StepDir,
        [Parameter(Mandatory)] $Record
    )

    if (-not (Test-Path -LiteralPath $StepDir -PathType Container)) {
        New-Item -ItemType Directory -Path $StepDir | Out-Null
    }

    Write-CountdownUtf8File -LiteralPath (Join-Path $StepDir 'stdout.txt') -Content ([string] $Record.Stdout)
    Write-CountdownUtf8File -LiteralPath (Join-Path $StepDir 'stderr.txt') -Content ([string] $Record.Stderr)
    Write-CountdownUtf8File -LiteralPath (Join-Path $StepDir 'exitCode.txt') -Content ([string] [int] $Record.ExitCode)
    Write-CountdownUtf8File -LiteralPath (Join-Path $StepDir 'durationMs.txt') -Content ([string] [int64] $Record.DurationMs)
    $result = [ordered]@{
        index = [int] $Record.Index
        id = [string] $Record.Id
        status = [string] $Record.Status
        exitCode = [int] $Record.ExitCode
        durationMs = [int64] $Record.DurationMs
        command = @($Record.Command)
    }
    Write-CountdownUtf8File -LiteralPath (Join-Path $StepDir 'result.json') -Content (ConvertTo-Json -InputObject $result -Depth 6)
}

function Invoke-CountdownEnvPre {
    param(
        [Parameter(Mandatory)] [string] $ProjectRoot,
        [Parameter(Mandatory)] [string] $StepDir
    )

    $stdout = [System.Text.StringBuilder]::new()
    $stderr = [System.Text.StringBuilder]::new()
    $contractModule = Resolve-CountdownRepoPath -Root $ProjectRoot -RelativePath 'Countdown.ReleaseContract.psm1'
    Import-Module -Name $contractModule -Force
    $contract = Get-CountdownReleaseContract
    $hashes = $contract.IntegrityHashes
    $verificationKey = 'verification\MinminCountdown.Verification.exe'
    $appHostKey = 'app\MinminCountdown.exe'
    $verificationExpected = if ($hashes.Contains($verificationKey)) { [string] $hashes[$verificationKey] } else { '' }
    $appHostExpected = if ($hashes.Contains($appHostKey)) { [string] $hashes[$appHostKey] } else { '' }

    $script:ExpectedPins = [ordered]@{
        verificationRelative = $verificationKey
        verificationExpectedSha256 = $verificationExpected
        appHostRelative = $appHostKey
        appHostExpectedSha256 = $appHostExpected
        capturedBeforeBuild = $true
    }
    Write-CountdownUtf8File -LiteralPath (Join-Path $StepDir 'pins.json') -Content ($script:ExpectedPins | ConvertTo-Json -Depth 4)

    $checked = 0
    $skipped = 0
    $mismatches = [System.Collections.Generic.List[object]]::new()
    foreach ($entry in $hashes.GetEnumerator()) {
        $relative = [string] $entry.Key
        $expected = [string] $entry.Value
        if (Test-CountdownGeneratedIntegrityPath -RelativePath $relative) {
            $skipped++
            continue
        }

        $fullPath = Resolve-CountdownRepoPath -Root $ProjectRoot -RelativePath $relative
        $checked++
        if (-not (Test-Path -LiteralPath $fullPath -PathType Leaf)) {
            $mismatches.Add([pscustomobject]@{
                    path = $relative
                    reason = 'missing'
                    expectedSha256 = $expected
                    actualSha256 = ''
                })
            continue
        }

        $actual = (Get-FileHash -Algorithm SHA256 -LiteralPath $fullPath).Hash
        if (-not [string]::Equals($actual, $expected, [System.StringComparison]::OrdinalIgnoreCase)) {
            $mismatches.Add([pscustomobject]@{
                    path = $relative
                    reason = 'mismatch'
                    expectedSha256 = $expected
                    actualSha256 = $actual
                })
        }
    }

    [void] $stdout.AppendLine("checked=$checked")
    [void] $stdout.AppendLine("skippedGenerated=$skipped")
    [void] $stdout.AppendLine("mismatches=$($mismatches.Count)")
    [void] $stdout.AppendLine("verificationPin=$verificationExpected")
    [void] $stdout.AppendLine("appHostPin=$appHostExpected")

    $mismatchPath = Join-Path $StepDir 'mismatches.json'
    Write-CountdownUtf8File -LiteralPath $mismatchPath -Content (ConvertTo-Json -InputObject @($mismatches) -Depth 6)

    if ([string]::IsNullOrWhiteSpace($verificationExpected)) {
        [void] $stdout.AppendLine('verificationPinSkipped=契约未钉 verification exe（构建产物不再静态 pin），跳过该项。')
    }
    if ([string]::IsNullOrWhiteSpace($appHostExpected)) {
        [void] $stderr.AppendLine("契约缺少现役条目：$appHostKey")
    }
    if ($mismatches.Count -gt 0) {
        [void] $stderr.AppendLine("IntegrityHashes 预检失败：$($mismatches.Count) 项。")
        foreach ($item in $mismatches) {
            [void] $stderr.AppendLine(('{0} {1}' -f [string] $item.reason, [string] $item.path))
        }
    }

    $failed = ($mismatches.Count -gt 0) -or
        [string]::IsNullOrWhiteSpace($appHostExpected)
    [pscustomobject]@{
        ExitCode = $(if ($failed) { 1 } else { 0 })
        Stdout = $stdout.ToString()
        Stderr = $stderr.ToString()
    }
}

function Invoke-CountdownPinAfterBuild {
    param(
        [Parameter(Mandatory)] [string] $ProjectRoot,
        [Parameter(Mandatory)] [string] $StepDir
    )

    $stdout = [System.Text.StringBuilder]::new()
    $stderr = [System.Text.StringBuilder]::new()
    $pins = $script:ExpectedPins
    if ($null -eq $pins) {
        throw 'pin-after-build 缺少 env-pre 捕获的现役 pin。'
    }

    $verificationRelative = [string] $pins['verificationRelative']
    $appHostRelative = [string] $pins['appHostRelative']
    $verificationExpected = [string] $pins['verificationExpectedSha256']
    $appHostExpected = [string] $pins['appHostExpectedSha256']
    $verificationPath = Resolve-CountdownRepoPath -Root $ProjectRoot -RelativePath $verificationRelative
    $appHostPath = Resolve-CountdownRepoPath -Root $ProjectRoot -RelativePath $appHostRelative
    $drift = [System.Collections.Generic.List[object]]::new()

    $verificationActual = ''
    if ([string]::IsNullOrWhiteSpace($verificationExpected)) {
        [void] $stdout.AppendLine('verificationPinSkipped=契约未钉 verification exe（构建产物不再静态 pin），跳过该项。')
    }
    elseif (-not (Test-Path -LiteralPath $verificationPath -PathType Leaf)) {
        $drift.Add([pscustomobject]@{ path = $verificationRelative; reason = 'missing'; expectedSha256 = $verificationExpected; actualSha256 = '' })
    }
    else {
        $verificationActual = (Get-FileHash -Algorithm SHA256 -LiteralPath $verificationPath).Hash
        if ([string]::IsNullOrWhiteSpace($verificationExpected) -or
            -not [string]::Equals($verificationActual, $verificationExpected, [System.StringComparison]::OrdinalIgnoreCase)) {
            $drift.Add([pscustomobject]@{
                    path = $verificationRelative
                    reason = 'mismatch'
                    expectedSha256 = $verificationExpected
                    actualSha256 = $verificationActual
                })
        }
    }

    $appHostActual = ''
    if (-not (Test-Path -LiteralPath $appHostPath -PathType Leaf)) {
        $drift.Add([pscustomobject]@{ path = $appHostRelative; reason = 'missing'; expectedSha256 = $appHostExpected; actualSha256 = '' })
    }
    else {
        $appHostActual = (Get-FileHash -Algorithm SHA256 -LiteralPath $appHostPath).Hash
        if ([string]::IsNullOrWhiteSpace($appHostExpected) -or
            -not [string]::Equals($appHostActual, $appHostExpected, [System.StringComparison]::OrdinalIgnoreCase)) {
            $drift.Add([pscustomobject]@{
                    path = $appHostRelative
                    reason = 'mismatch'
                    expectedSha256 = $appHostExpected
                    actualSha256 = $appHostActual
                })
        }
    }

    $report = [ordered]@{
        verificationExpectedSha256 = $verificationExpected
        verificationActualSha256 = $verificationActual
        appHostExpectedSha256 = $appHostExpected
        appHostActualSha256 = $appHostActual
        drifted = ($drift.Count -gt 0)
        drift = @($drift)
        releaseContractUpdated = $false
    }
    Write-CountdownUtf8File -LiteralPath (Join-Path $StepDir 'drift.json') -Content (ConvertTo-Json -InputObject $report -Depth 6)

    [void] $stdout.AppendLine("verificationExpected=$verificationExpected")
    [void] $stdout.AppendLine("verificationActual=$verificationActual")
    [void] $stdout.AppendLine("appHostExpected=$appHostExpected")
    [void] $stdout.AppendLine("appHostActual=$appHostActual")
    [void] $stdout.AppendLine("drifted=$($drift.Count -gt 0)")

    if ($drift.Count -gt 0) {
        [void] $stderr.AppendLine('pin-after-build 漂移。本 runner 不修改 Countdown.ReleaseContract.psm1。')
        foreach ($item in $drift) {
            [void] $stderr.AppendLine(('{0} {1} expected={2} actual={3}' -f [string] $item.reason, [string] $item.path, [string] $item.expectedSha256, [string] $item.actualSha256))
        }
    }

    [pscustomobject]@{
        ExitCode = $(if ($drift.Count -gt 0) { 1 } else { 0 })
        Stdout = $stdout.ToString()
        Stderr = $stderr.ToString()
    }
}

if ($Help) {
    Write-Output (Show-CountdownVerificationRunHelp)
    exit 0
}

if ($Retry) {
    throw '本 runner 不重试；-Retry 必须保持关闭。任一步失败后请使用新的 -RunId 重跑。'
}

$projectRoot = [System.IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
if ($projectRoot.StartsWith('C:\', [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "仓库根不得位于 C:\：$projectRoot"
}
$contractProbe = Join-Path $projectRoot 'Countdown.ReleaseContract.psm1'
if (-not (Test-Path -LiteralPath $contractProbe -PathType Leaf)) {
    throw "未在仓库根找到 Countdown.ReleaseContract.psm1：$projectRoot"
}

$managedPwsh = Resolve-CountdownManagedPwsh
$matrixPwsh = Resolve-CountdownMatrixPwsh
$gitCommand = Get-Command -Name git.exe -CommandType Application -ErrorAction Stop | Select-Object -First 1
$gitPath = [System.IO.Path]::GetFullPath([string] $gitCommand.Source)

if ([string]::IsNullOrWhiteSpace($RunId)) {
    $RunId = New-CountdownVerificationRunId
}
Test-CountdownSafeRunId -Value $RunId

$evidenceRoot = [System.IO.Path]::GetFullPath($script:EvidenceRoot)
if (-not $evidenceRoot.StartsWith('D:\', [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "证据根必须位于 D:\：$evidenceRoot"
}
$namespaceRoot = Join-Path $evidenceRoot $Namespace
$attemptDir = [System.IO.Path]::GetFullPath((Join-Path $namespaceRoot $RunId))
$evidencePrefix = if ($evidenceRoot.EndsWith('\')) { $evidenceRoot } else { $evidenceRoot + '\' }
if (-not $attemptDir.StartsWith($evidencePrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "attempt 路径逃出证据根：$attemptDir"
}
if ($attemptDir.StartsWith('C:\', [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "拒绝写入 C:\：$attemptDir"
}

if (Test-Path -LiteralPath $attemptDir) {
    throw "拒绝覆盖已有 attempt 目录：$attemptDir。请使用新的 -RunId。"
}

New-Item -ItemType Directory -Path $attemptDir | Out-Null
$script:AttemptDir = $attemptDir
$copiedRunner = Join-Path $attemptDir 'Invoke-CountdownVerificationRun.ps1'
Copy-Item -LiteralPath $PSCommandPath -Destination $copiedRunner
$runnerSha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $PSCommandPath).Hash
$copiedSha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $copiedRunner).Hash
if (-not [string]::Equals($runnerSha256, $copiedSha256, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw 'runner 自拷贝 SHA-256 不一致。'
}

$gitState = Get-CountdownGitState -ProjectRoot $projectRoot -GitPath $gitPath
$startUtc = [datetime]::UtcNow.ToString('o')
$script:MetaPath = Join-Path $attemptDir 'meta.json'
$script:Meta = [ordered]@{
    status = 'running'
    startUtc = $startUtc
    endUtc = $null
    runnerPath = $copiedRunner
    runnerSha256 = $runnerSha256
    sourceRunnerPath = $PSCommandPath
    namespace = $Namespace
    runId = $RunId
    gitHead = $gitState.Head
    worktreeDirty = [bool] $gitState.WorktreeDirty
    dryRun = [bool] $DryRun
    retry = $false
    failedStep = $null
    managedPwsh = $managedPwsh
    matrixPwsh = $matrixPwsh
}
Save-CountdownVerificationMeta

$steps = @(Get-CountdownVerificationSteps)
if ($steps.Count -ne 13) {
    throw "内部步骤表必须恰好 13 项，实际 $($steps.Count)。"
}
$stepPlan = @(
    foreach ($step in $steps) {
        $command = @(Get-CountdownStepCommandTokens -Step $step -ManagedPwsh $managedPwsh -MatrixPwsh $matrixPwsh -GitPath $gitPath)
        [pscustomobject]@{
            index = [int] $step.Index
            id = [string] $step.Id
            kind = [string] $step.Kind
            command = $command
        }
    }
)
Write-CountdownUtf8File -LiteralPath (Join-Path $attemptDir 'steps.json') -Content (ConvertTo-Json -InputObject @($stepPlan) -Depth 6)

Write-Output ("namespace={0}" -f $Namespace)
Write-Output ("runId={0}" -f $RunId)
Write-Output ("attemptDir={0}" -f $attemptDir)
Write-Output ("runnerSha256={0}" -f $runnerSha256)
Write-Output 'steps=13'
foreach ($item in $stepPlan) {
    Write-Output ('{0,2}. {1}' -f [int] $item.index, [string] $item.id)
}

if ($DryRun) {
    Save-CountdownVerificationMeta -Patch @{
        status = 'dry-run-passed'
        endUtc = [datetime]::UtcNow.ToString('o')
    }
    Write-Output 'status=dry-run-passed'
    exit 0
}

Set-Location -LiteralPath $projectRoot
$failedStepId = $null
try {
    foreach ($step in $steps) {
        $stepLabel = '{0:00}-{1}' -f [int] $step.Index, [string] $step.Id
        $stepDir = Join-Path $attemptDir $stepLabel
        New-Item -ItemType Directory -Path $stepDir | Out-Null
        $commandTokens = @(Get-CountdownStepCommandTokens -Step $step -ManagedPwsh $managedPwsh -MatrixPwsh $matrixPwsh -GitPath $gitPath)
        Write-Output ("STEP {0}/13 {1} starting" -f [int] $step.Index, [string] $step.Id)

        $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
        $captured = $null
        try {
            switch ([string] $step.Kind) {
                'env-pre' {
                    $captured = Invoke-CountdownEnvPre -ProjectRoot $projectRoot -StepDir $stepDir
                }
                'pin-after-build' {
                    $captured = Invoke-CountdownPinAfterBuild -ProjectRoot $projectRoot -StepDir $stepDir
                }
                'git' {
                    $captured = Invoke-CountdownCapturedProcess -FilePath $gitPath -WorkingDirectory $projectRoot -ArgumentList @($step.Arguments)
                }
                'pwsh' {
                    $isolation = [string] $step.Isolation
                    if ($isolation -eq 'foreground-authorized' -and -not $AllowForegroundWindowedSteps) {
                        $captured = [pscustomobject]@{
                            ExitCode = 1
                            Stdout = ''
                            Stderr = "拒绝前台窗口步骤 $($step.Id)。用户正在用电脑时禁止；需要时显式传入 -AllowForegroundWindowedSteps。"
                        }
                    }
                    else {
                        $engine = if ([string] $step.Engine -eq 'matrix') { $matrixPwsh } else { $managedPwsh }
                        $scriptPath = Resolve-CountdownRepoPath -Root $projectRoot -RelativePath ([string] $step.ScriptName)
                        $argumentList = [System.Collections.Generic.List[string]]::new()
                        [void] $argumentList.Add('-NoLogo')
                        [void] $argumentList.Add('-NoProfile')
                        if ([bool] $step.Sta) {
                            [void] $argumentList.Add('-STA')
                        }
                        [void] $argumentList.Add('-File')
                        [void] $argumentList.Add($scriptPath)
                        foreach ($argument in @($step.Arguments)) {
                            [void] $argumentList.Add([string] $argument)
                        }
                        if ($isolation -eq 'foreground-authorized') {
                            [void] $argumentList.Add('-AllowForegroundDesktopLauncher')
                        }
                        if ($isolation -eq 'isolated-desktop') {
                            $captured = Invoke-CountdownIsolatedPwshStep `
                                -Engine $engine `
                                -ArgumentList @($argumentList) `
                                -WorkingDirectory $projectRoot `
                                -StepDir $stepDir `
                                -DesktopName ([string] $step.IsolatedDesktopName)
                        }
                        else {
                            $captured = Invoke-CountdownCapturedProcess -FilePath $engine -WorkingDirectory $projectRoot -ArgumentList @($argumentList)
                        }
                    }
                }
                default {
                    throw "未知步骤类型：$($step.Kind)"
                }
            }
        }
        catch {
            $captured = [pscustomobject]@{
                ExitCode = 1
                Stdout = ''
                Stderr = [string] $_.Exception.Message
            }
        }
        $stopwatch.Stop()

        $exitCode = [int] $captured.ExitCode
        $status = if ($exitCode -eq 0) { 'passed' } else { 'failed' }
        $record = [pscustomobject]@{
            Index = [int] $step.Index
            Id = [string] $step.Id
            Status = $status
            ExitCode = $exitCode
            DurationMs = [int64] $stopwatch.ElapsedMilliseconds
            Stdout = [string] $captured.Stdout
            Stderr = [string] $captured.Stderr
            Command = $commandTokens
        }
        Write-CountdownStepEvidence -StepDir $stepDir -Record $record
        Write-Output ("STEP {0}/13 {1} {2} exit={3} durationMs={4}" -f [int] $step.Index, [string] $step.Id, $status, $exitCode, $record.DurationMs)

        if ($exitCode -ne 0) {
            $failedStepId = [string] $step.Id
            break
        }
    }

    if ([string]::IsNullOrWhiteSpace($failedStepId)) {
        Save-CountdownVerificationMeta -Patch @{
            status = 'passed'
            endUtc = [datetime]::UtcNow.ToString('o')
            failedStep = $null
        }
        Write-Output 'status=passed'
        exit 0
    }

    Save-CountdownVerificationMeta -Patch @{
        status = 'failed'
        endUtc = [datetime]::UtcNow.ToString('o')
        failedStep = $failedStepId
    }
    Write-Output ("status=failed step={0}" -f $failedStepId)
    exit 1
}
catch {
    if ($null -ne $script:Meta -and -not [string]::IsNullOrWhiteSpace($script:MetaPath)) {
        Save-CountdownVerificationMeta -Patch @{
            status = 'failed'
            endUtc = [datetime]::UtcNow.ToString('o')
            failedStep = $(if ([string]::IsNullOrWhiteSpace($failedStepId)) { 'runner' } else { $failedStepId })
        }
    }
    throw
}
