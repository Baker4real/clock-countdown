#requires -Version 7.0

[CmdletBinding()]
param(
    [switch] $ValidateOnly,
    [switch] $DeferCommit,
    [string] $DeferredReceiptPath,
    [switch] $ParentTransactionMutexHeld,
    [string] $ExpectedInteractiveUser,
    [int] $ExpectedInteractiveSessionId,
    [string] $ExpectedDesktopPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$staging = $PSScriptRoot
$install = 'D:\敏敏子倒计时'
$taskName = 'Minmin Countdown'
$legacyTaskName = '敏敏子倒计时'
$officialCharacterAssets = @(
    'sanrio-official-mymelody.png',
    'sanrio-official-cinnamoroll.png',
    'sanrio-official-kuromi.png'
)
$floatingPlushAssets = @(
    'assets\floating-plush\floating-plush-mymelody-front.png',
    'assets\floating-plush\floating-plush-mymelody-pose.png',
    'assets\floating-plush\floating-plush-cinnamoroll-front.png',
    'assets\floating-plush\floating-plush-cinnamoroll-pose.png',
    'assets\floating-plush\floating-plush-kuromi-front.png',
    'assets\floating-plush\floating-plush-kuromi-pose.png'
)
$expected = @{
    'Build-CountdownApplication.ps1' = '151A221B784B3EA30FCDD975E143D9D26EDF55A2325F4E00BB3EC34DAEA9EC21'
    'Countdown.App.psm1' = '6F40387744A2DF43A41A6A95B3DD02AF748CE7C3B00F532FC1588FB33B5994AF'
    'Countdown.Core.psm1' = 'E1A639B06D404F7F4638CAB4DE6392FB27E5C324D77A2AC44D1B226C8375410B'
    'Countdown.InstallTransaction.psm1' = '89E7E2E7EE9255F6583D7E6421F8F7924B7E2543956EF502FB7136E6FBCD0590'
    'Countdown.Notes.psm1' = '02D65016C7E39BDF40007994A35394AB38A8461AF324C4E17C15EF8F7B5C790C'
    'Countdown.Runtime.psm1' = 'EE71577AF288212BC7A4031A64E3C9379A5F92A8091FB965E28BC871A42C1B19'
    'Countdown.Settings.psm1' = '47CBDE41E71380161127116C881A19DCDDF994CEB3811B03EFAE6007267ADD7A'
    'Countdown.Tags.psm1' = '57AE8BBE689F3838E3B9EE8B56F96D661C8A46EAD825103307482B00C472FAAC'
    'Countdown.Core.Tests.ps1' = 'D9354D996BB713A51DC927F8FCEF4B01B71D92FB7B604DF6C6E82C289E042A39'
    'Countdown.Ui.Tests.ps1' = '69C5FA6E579E0B4B9A6075A0A8F2317222662310F3172D40FE1DB480A810F64D'
    'Countdown.CSharp.Tests.ps1' = '7B7C0AC95010F04998D2E6F1B41B80BEEC7851D1C9E7C647D139875CD50C252E'
    '敏敏子倒计时.ps1' = '2729F74658E74E1F2A655E706E9F5A47F34699B1D0BD0492D5A6079930FF64D5'
    'CountdownLauncher.exe' = '195A228F647E4541AE4EEF7358BF2BDD478F705FA23D1981C0D96E8DD835CC00'
    'countdown-icon.ico' = 'C5B7CB0A09524AEFE4488291078F61B0AF54958582DA98B99B25D0F4DC6976C5'
    'verification\MinminCountdown.Verification.exe' = '11498BC8584AF5E11C1E38DCB417CEB5D00A4D0255FE84FE70F71E41585C56C7'
    'app\MinminCountdown.exe' = '71E6ED0945EFACFFF2409F88FF425BEA0C8198AD08DFB99503F19C88B71F0FF2'
    'sanrio-official-mymelody.png' = '6C53BD64E8F601C0709C69DAEE863F282EAA763AFFA2F4F96CE77B5AA2B0B41C'
    'sanrio-official-cinnamoroll.png' = '9344D335E7591C8599E7CC32730B315401037D58E15C14DCF23853DE74867520'
    'sanrio-official-kuromi.png' = '3DC52AAC57897374436C3394C434DE26090482A8C11E6BDF4BD7F3DCE581D4D0'
    'assets\floating-plush\floating-plush-mymelody-front.png' = '9D078C528CB6581222D292EA7E13AF42A77DC60C793C9A8B90AE163B30D27A4D'
    'assets\floating-plush\floating-plush-mymelody-pose.png' = 'FC8EE48368C6BAEBF45CE3250BD93EB0A43760A03089BFBECCCE2295AF9D5333'
    'assets\floating-plush\floating-plush-cinnamoroll-front.png' = 'DA9BD4C55AD11CAAADD68A8B153D104A4F170B453175BB67DB7AE99C5CFDAD9B'
    'assets\floating-plush\floating-plush-cinnamoroll-pose.png' = '5FABCFB1420B6D3162DED0BC0C0FFC2A97ACCE24C3B214A1708E652CDB62D107'
    'assets\floating-plush\floating-plush-kuromi-front.png' = '65A0ACB6B90BCFB2E4409840B1AD5A0844951951A1BBE9050330644592DFD8BD'
    'assets\floating-plush\floating-plush-kuromi-pose.png' = '92A4493D227A3EA60E5BC23D12CF6AC7F8E5CEBED4F3FDA0328DE6C8B5158F93'
    'app\assets\sanrio-official-mymelody.png' = '6C53BD64E8F601C0709C69DAEE863F282EAA763AFFA2F4F96CE77B5AA2B0B41C'
    'app\assets\sanrio-official-cinnamoroll.png' = '9344D335E7591C8599E7CC32730B315401037D58E15C14DCF23853DE74867520'
    'app\assets\sanrio-official-kuromi.png' = '3DC52AAC57897374436C3394C434DE26090482A8C11E6BDF4BD7F3DCE581D4D0'
    'app\assets\floating-plush\floating-plush-mymelody-front.png' = '9D078C528CB6581222D292EA7E13AF42A77DC60C793C9A8B90AE163B30D27A4D'
    'app\assets\floating-plush\floating-plush-mymelody-pose.png' = 'FC8EE48368C6BAEBF45CE3250BD93EB0A43760A03089BFBECCCE2295AF9D5333'
    'app\assets\floating-plush\floating-plush-cinnamoroll-front.png' = 'DA9BD4C55AD11CAAADD68A8B153D104A4F170B453175BB67DB7AE99C5CFDAD9B'
    'app\assets\floating-plush\floating-plush-cinnamoroll-pose.png' = '5FABCFB1420B6D3162DED0BC0C0FFC2A97ACCE24C3B214A1708E652CDB62D107'
    'app\assets\floating-plush\floating-plush-kuromi-front.png' = '65A0ACB6B90BCFB2E4409840B1AD5A0844951951A1BBE9050330644592DFD8BD'
    'app\assets\floating-plush\floating-plush-kuromi-pose.png' = '92A4493D227A3EA60E5BC23D12CF6AC7F8E5CEBED4F3FDA0328DE6C8B5158F93'
}
$applicationFiles = @(
    'Countdown.App.psm1',
    'Countdown.Core.psm1',
    'Countdown.InstallTransaction.psm1',
    'Countdown.Notes.psm1',
    'Countdown.Runtime.psm1',
    'Countdown.Settings.psm1',
    'Countdown.Tags.psm1',
    '敏敏子倒计时.ps1',
    'app\MinminCountdown.exe',
    'app\assets\sanrio-official-mymelody.png',
    'app\assets\sanrio-official-cinnamoroll.png',
    'app\assets\sanrio-official-kuromi.png',
    'app\assets\floating-plush\floating-plush-mymelody-front.png',
    'app\assets\floating-plush\floating-plush-mymelody-pose.png',
    'app\assets\floating-plush\floating-plush-cinnamoroll-front.png',
    'app\assets\floating-plush\floating-plush-cinnamoroll-pose.png',
    'app\assets\floating-plush\floating-plush-kuromi-front.png',
    'app\assets\floating-plush\floating-plush-kuromi-pose.png',
    'CountdownLauncher.exe',
    'countdown-icon.ico',
    'sanrio-official-mymelody.png',
    'sanrio-official-cinnamoroll.png',
    'sanrio-official-kuromi.png',
    'assets\floating-plush\floating-plush-mymelody-front.png',
    'assets\floating-plush\floating-plush-mymelody-pose.png',
    'assets\floating-plush\floating-plush-cinnamoroll-front.png',
    'assets\floating-plush\floating-plush-cinnamoroll-pose.png',
    'assets\floating-plush\floating-plush-kuromi-front.png',
    'assets\floating-plush\floating-plush-kuromi-pose.png'
)

foreach ($entry in $expected.GetEnumerator()) {
    $path = Join-Path $staging $entry.Key
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Missing staging file: $($entry.Key)"
    }
    $actual = (Get-FileHash -Algorithm SHA256 -LiteralPath $path).Hash
    if ($actual -ne $entry.Value) {
        throw "Hash mismatch: $($entry.Key)"
    }
}

if ($ValidateOnly) {
    [pscustomobject]@{
        Valid = $true
        SourceRoot = $staging
        VerifiedFiles = $expected.Count
        OfficialCharacterAssets = $officialCharacterAssets
        FloatingPlushAssets = $floatingPlushAssets
        Transactional = $true
        DeferredCommitSupported = $true
    } | ConvertTo-Json
    exit 0
}

if ($DeferCommit -and ((-not $ParentTransactionMutexHeld) -or [string]::IsNullOrWhiteSpace($DeferredReceiptPath))) {
    throw '延迟提交只能由已持有父安装事务锁的验收方调用，并且必须提供独立回执路径。'
}
if ($ParentTransactionMutexHeld -and -not $DeferCommit) {
    throw '父安装事务锁上下文只允许与 -DeferCommit 一起使用。'
}

$installResolved = [System.IO.Path]::GetFullPath($install)
if (-not $installResolved.StartsWith('D:\', [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Install path is outside D drive: $installResolved"
}
if ((Test-Path -LiteralPath $installResolved) -and -not (Test-Path -LiteralPath $installResolved -PathType Container)) {
    throw "Install path is not a directory: $installResolved"
}

$powerShellEngine = (Get-Command pwsh.exe -CommandType Application -ErrorAction Stop | Select-Object -First 1).Source
if ([string]::IsNullOrWhiteSpace($powerShellEngine)) {
    throw '未找到 pwsh.exe；本项目要求 PowerShell 7+，不支持 Windows PowerShell 5.1。'
}
$powerShellProcessName = [System.IO.Path]::GetFileName($powerShellEngine)
Get-Service -Name W32Time -ErrorAction Stop | Out-Null

Import-Module (Join-Path $staging 'Countdown.InstallTransaction.psm1') -Force -DisableNameChecking
$interactiveContext = Get-CountdownInteractiveDesktopContext
$contextExpectationProvided = $PSBoundParameters.ContainsKey('ExpectedInteractiveUser') -or $PSBoundParameters.ContainsKey('ExpectedInteractiveSessionId') -or $PSBoundParameters.ContainsKey('ExpectedDesktopPath')
if ($contextExpectationProvided) {
    if (-not ($PSBoundParameters.ContainsKey('ExpectedInteractiveUser') -and $PSBoundParameters.ContainsKey('ExpectedInteractiveSessionId') -and $PSBoundParameters.ContainsKey('ExpectedDesktopPath'))) {
        throw '办公室部署传入的交互上下文必须同时包含用户、会话和桌面路径。'
    }
    $expectedDesktop = Assert-CountdownInstallPathOnDDrive -Path $ExpectedDesktopPath
    if (-not [string]::Equals([string] $interactiveContext.User, $ExpectedInteractiveUser, [System.StringComparison]::OrdinalIgnoreCase) -or
        [int] $interactiveContext.SessionId -ne $ExpectedInteractiveSessionId -or
        -not [string]::Equals([string] $interactiveContext.Desktop, $expectedDesktop, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw '办公室部署开始安装前交互用户、会话或桌面路径已变化；未创建安装事务。'
    }
}
$interactiveUser = [string] $interactiveContext.User
$desktop = [string] $interactiveContext.Desktop
$legacyManagedLauncherHash = 'E5E37A9D20669E4A888969F0DB95560D45D581913B68EA7600055DE50E20AC14'
function Assert-CountdownInstallInteractiveContextStable {
    param([Parameter(Mandatory)] [object] $ExpectedContext)

    $actualContext = Get-CountdownInteractiveDesktopContext
    if (-not [string]::Equals([string] $actualContext.User, [string] $ExpectedContext.User, [System.StringComparison]::OrdinalIgnoreCase) -or
        [int] $actualContext.SessionId -ne [int] $ExpectedContext.SessionId -or
        -not [string]::Equals([string] $actualContext.Desktop, [string] $ExpectedContext.Desktop, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw '安装事务开始前交互用户、会话或桌面路径已变化；未创建安装事务。'
    }
    $actualContext
}

function Get-CountdownManagedDesktopLauncherOwnership {
    param(
        [Parameter(Mandatory)] [string] $LauncherPath,
        [Parameter(Mandatory)] [string] $LauncherStatePath,
        [Parameter(Mandatory)] [hashtable] $Expected,
        [Parameter(Mandatory)] [string] $LegacyManagedLauncherHash
    )

    if (-not (Test-Path -LiteralPath $LauncherPath -PathType Leaf)) {
        return [pscustomobject]@{ Exists = $false; Sha256 = $null; Managed = $true }
    }
    $currentLauncherHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $LauncherPath).Hash
    $managed = $false
    if (Test-Path -LiteralPath $LauncherStatePath -PathType Leaf) {
        $launcherStateJson = [System.IO.File]::ReadAllText($LauncherStatePath, [System.Text.Encoding]::UTF8)
        $previousLauncherState = $launcherStateJson | ConvertFrom-Json
        $pathMatches = [string]::Equals([string] $previousLauncherState.desktopPath, $LauncherPath, [System.StringComparison]::OrdinalIgnoreCase)
        $hashMatches = $currentLauncherHash -eq $previousLauncherState.desktopSha256 -or $currentLauncherHash -eq $Expected['CountdownLauncher.exe']
        $managed = $pathMatches -and $hashMatches
    }
    else {
        $managed = $currentLauncherHash -eq $LegacyManagedLauncherHash
    }
    [pscustomobject]@{ Exists = $true; Sha256 = $currentLauncherHash; Managed = [bool] $managed }
}

function Wait-CountdownNetworkTimeSource {
    param(
        [ValidateRange(1, 120)] [int] $TimeoutSeconds = 30,
        [ValidateRange(50, 5000)] [int] $PollMilliseconds = 500,
        [scriptblock] $QueryTimeSourceAction
    )

    if ($null -eq $QueryTimeSourceAction) {
        $QueryTimeSourceAction = {
            $source = (w32tm.exe /query /source | Out-String).Trim()
            [pscustomobject]@{ ExitCode = $LASTEXITCODE; Source = $source }
        }
    }
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)
    $lastResult = '未返回时间源'
    do {
        $probe = & $QueryTimeSourceAction
        $exitCode = if ($null -eq $probe) { -1 } else { [int] $probe.ExitCode }
        $source = if ($null -eq $probe) { '' } else { [string] $probe.Source }
        if ($exitCode -eq 0 -and -not [string]::IsNullOrWhiteSpace($source) -and $source -notmatch '(?i)Local CMOS|Free-running|VM IC') {
            return $source
        }
        $lastResult = "exit=$exitCode; source=$source"
        if ([DateTimeOffset]::UtcNow -ge $deadline) { break }
        Start-Sleep -Milliseconds $PollMilliseconds
    } while ($true)
    throw "Windows Time did not report a network source within $TimeoutSeconds seconds: $lastResult"
}

$coreTestArguments = @('-NoLogo', '-NoProfile', '-NonInteractive', '-File', (Join-Path $staging 'Countdown.Core.Tests.ps1'))
if ($ParentTransactionMutexHeld) { $coreTestArguments += '-ParentTransactionMutexHeld' }
$testOutput = & $powerShellEngine @coreTestArguments | Out-String
if ($LASTEXITCODE -ne 0) {
    throw "Remote tests failed: $testOutput"
}
$cSharpTestOutput = & $powerShellEngine `
    -NoLogo -NoProfile -NonInteractive -STA -File (Join-Path $staging 'Countdown.CSharp.Tests.ps1') | Out-String
if ($LASTEXITCODE -ne 0) {
    throw "Remote C# regression test failed: $cSharpTestOutput"
}
$uiTestOutput = & $powerShellEngine `
    -NoLogo -NoProfile -NonInteractive -STA -File (Join-Path $staging 'Countdown.Ui.Tests.ps1') | Out-String
if ($LASTEXITCODE -ne 0) {
    throw "Remote UI regression test failed: $uiTestOutput"
}
$uiValidationOutput = & $powerShellEngine `
    -NoLogo -NoProfile -NonInteractive -STA -File (Join-Path $staging '敏敏子倒计时.ps1') -ValidateOnly | Out-String
if ($LASTEXITCODE -ne 0) {
    throw "Remote WPF validation failed: $uiValidationOutput"
}

$launcherPath = Join-Path $desktop '敏敏子倒计时.exe'
$launcherStatePath = Join-Path $install 'launcher-state.json'
$legacyLauncherPath = Join-Path $desktop '敏敏子倒计时.cmd'
$launcherTempPath = "$launcherPath.new"

Import-Module (Join-Path $staging 'Countdown.Runtime.psm1') -Force
Import-Module (Join-Path $staging 'Countdown.Settings.psm1') -Force
$installMutex = $null
$settingsWriteMutex = $null
try {
    # 即使父验收已持锁也再次获取同线程可重入 mutex；公开参数不能跳过真实安装锁。
    $installMutex = Enter-CountdownInstallTransactionMutex -TimeoutSeconds 120
    $interactiveContext = Assert-CountdownInstallInteractiveContextStable -ExpectedContext $interactiveContext
    # 先按 journal 恢复崩溃留下的候选启动器；否则候选 exe 与旧 launcher-state 的哈希不一致，会永久阻断恢复。
    $recoveryResult = Recover-CountdownInstallTransactions -InstallPath $install -PowerShell7Path $powerShellEngine -ExpectedTaskName $taskName -ExpectedLegacyTaskName $legacyTaskName -ExpectedLauncherPath $launcherPath -ExpectedLegacyLauncherPath $legacyLauncherPath -ExpectedLauncherTempPath $launcherTempPath
    $recoveredTransactions = @($recoveryResult.RecoveredTransactionPaths)
    if (@($recoveryResult.UnresolvedCommittedTransactions).Count -gt 0) {
        throw '检测到已提交但未完成应用解锁确认的事务；已保留回滚证据，拒绝继续安装。'
    }
    if (@($recoveryResult.UnresolvedLauncherOwnershipConflicts).Count -gt 0) {
        throw '检测到用户替换的桌面启动器：应用、任务和时间服务已恢复，但事务证据被保留，拒绝继续安装。'
    }
    $initialLauncherOwnership = Get-CountdownManagedDesktopLauncherOwnership -LauncherPath $launcherPath -LauncherStatePath $launcherStatePath -Expected $expected -LegacyManagedLauncherHash $legacyManagedLauncherHash
    if (-not [bool] $initialLauncherOwnership.Managed) {
        throw "Refusing to overwrite an unmanaged desktop launcher: $launcherPath"
    }
    $interactiveContext = Assert-CountdownInstallInteractiveContextStable -ExpectedContext $interactiveContext
    $transaction = Start-CountdownInstallTransaction -InstallPath $install -LauncherPath $launcherPath -LegacyLauncherPath $legacyLauncherPath -TaskName $taskName -LegacyTaskName $legacyTaskName
    $transactionCompleted = $false

    try {
        Set-Service -Name W32Time -StartupType Automatic
        Start-Service -Name W32Time
        $resync = (w32tm.exe /resync /rediscover | Out-String).Trim()
        if ($LASTEXITCODE -ne 0) {
            throw "Windows Time resync failed: $resync"
        }
        $timeSource = Wait-CountdownNetworkTimeSource -TimeoutSeconds 30 -PollMilliseconds 500
        $timeStatus = (w32tm.exe /query /status | Out-String).Trim()
        if ($LASTEXITCODE -ne 0) {
            throw 'Windows Time status query failed.'
        }

        Stop-CountdownManagedApplication -InstallPath $install -TaskName $taskName -LegacyTaskName $legacyTaskName -FreezeFutureStarts
        # 旧进程已经静止后才捕获可变 settings/runtime 与桌面文件；任务原状态仍取自 prepared journal。
        $transaction = Capture-CountdownInstallPayloadSnapshot -TransactionPath $transaction.transactionPath -ExpectedInstallPath $install -ExpectedTaskName $taskName -ExpectedLegacyTaskName $legacyTaskName -ExpectedLauncherPath $launcherPath -ExpectedLegacyLauncherPath $legacyLauncherPath -ExpectedLauncherTempPath $launcherTempPath
        [void] (Remove-CountdownScheduledTaskIfPresent -TaskName $legacyTaskName)
        $install = Assert-CountdownInstallPathOnDDrive -Path $install
        [void] (Assert-CountdownInstallTreeHasNoReparsePoints -Path $install)
        if (-not (Test-Path -LiteralPath $install -PathType Container)) {
            New-Item -ItemType Directory -Path $install -Force | Out-Null
        }
        $runtimeStatusPath = Assert-CountdownInstallPathOnDDrive -Path (Join-Path $install 'runtime-status.json')
        $acceptanceStatusPath = Assert-CountdownInstallPathOnDDrive -Path (Join-Path $install 'acceptance-status.json')
        $startupRequestPath = Assert-CountdownInstallPathOnDDrive -Path (Join-Path $install 'startup-request.json')
        Remove-Item -LiteralPath $runtimeStatusPath -Force -ErrorAction SilentlyContinue
        Remove-Item -LiteralPath $acceptanceStatusPath -Force -ErrorAction SilentlyContinue
        $startupRequest = New-CountdownStartupRequest -Path $startupRequestPath -AcceptanceReadOnly

        foreach ($file in $applicationFiles) {
            $source = Assert-CountdownInstallPathOnDDrive -Path (Join-Path $staging $file)
            $destination = Assert-CountdownInstallPathOnDDrive -Path (Join-Path $install $file)
            $destinationParent = Assert-CountdownInstallPathOnDDrive -Path (Split-Path -Parent $destination)
            New-Item -ItemType Directory -Path $destinationParent -Force | Out-Null
            [void] (Assert-CountdownInstallPathOnDDrive -Path $destination)
            Copy-Item -LiteralPath $source -Destination $destination -Force
        }

        $appPath = Join-Path $install '敏敏子倒计时.ps1'
        $arguments = "-NoLogo -NoProfile -ExecutionPolicy RemoteSigned -WindowStyle Hidden -STA -File `"$appPath`""
        # 候选启动器放在受管事务目录，桌面上的 .new 仅作为用户已有文件的快照目标，绝不作为安装候选。
        $candidateLauncherPath = Assert-CountdownInstallPathOnDDrive -Path (Join-Path $transaction.transactionPath 'candidate-launcher.exe')
        $installedLauncherSourcePath = Assert-CountdownInstallPathOnDDrive -Path (Join-Path $install 'CountdownLauncher.exe')
        Copy-Item -LiteralPath $installedLauncherSourcePath -Destination $candidateLauncherPath -Force
        $candidateLauncherHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $candidateLauncherPath).Hash
        if ($candidateLauncherHash -ne $expected['CountdownLauncher.exe']) {
            Remove-Item -LiteralPath $candidateLauncherPath -Force
            throw 'Transaction launcher candidate hash mismatch.'
        }
        # Move 前持久化候选哈希；崩溃恢复只会覆盖候选文件、原备份或缺失路径，绝不静默覆盖用户后续替换的启动器。
        $transaction = Set-CountdownInstallTransactionCandidateLauncherHash -TransactionPath $transaction.transactionPath -CandidateLauncherSha256 $candidateLauncherHash -ExpectedInstallPath $install -ExpectedTaskName $taskName -ExpectedLegacyTaskName $legacyTaskName -ExpectedLauncherPath $launcherPath -ExpectedLegacyLauncherPath $legacyLauncherPath -ExpectedLauncherTempPath $launcherTempPath
        # 将所有慢操作放在此前完成；紧贴替换前比较初始哈希，准备期间的任意 launcher 替换都会安全回滚。
        $preparedLauncherOwnership = Get-CountdownManagedDesktopLauncherOwnership -LauncherPath $launcherPath -LauncherStatePath $launcherStatePath -Expected $expected -LegacyManagedLauncherHash $legacyManagedLauncherHash
        if (-not [bool] $preparedLauncherOwnership.Managed -or
            [bool] $preparedLauncherOwnership.Exists -ne [bool] $initialLauncherOwnership.Exists -or
            -not [string]::Equals([string] $preparedLauncherOwnership.Sha256, [string] $initialLauncherOwnership.Sha256, [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "Refusing to overwrite a desktop launcher changed while installation was preparing: $launcherPath"
        }
        $candidateLauncherHashBeforeMove = if (Test-Path -LiteralPath $candidateLauncherPath -PathType Leaf) { (Get-FileHash -Algorithm SHA256 -LiteralPath $candidateLauncherPath).Hash } else { $null }
        if ($candidateLauncherHashBeforeMove -ne $candidateLauncherHash) {
            throw 'Transaction launcher candidate changed before desktop replacement.'
        }
        $candidateLauncherPath = Assert-CountdownInstallPathOnDDrive -Path $candidateLauncherPath
        $launcherPath = Assert-CountdownInstallPathOnDDrive -Path $launcherPath
        Move-Item -LiteralPath $candidateLauncherPath -Destination $launcherPath -Force
        if (-not (Test-Path -LiteralPath $launcherPath -PathType Leaf)) {
            throw 'Desktop launcher was not created.'
        }
        $installedLauncherHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $launcherPath).Hash
        if ($installedLauncherHash -ne $candidateLauncherHash) {
            throw 'Desktop launcher hash mismatch after replacement.'
        }
        $launcherState = [ordered]@{ schemaVersion = 1; desktopPath = $launcherPath; desktopSha256 = $candidateLauncherHash } | ConvertTo-Json
        $launcherStatePath = Assert-CountdownInstallPathOnDDrive -Path $launcherStatePath
        $launcherStateTempPath = Assert-CountdownInstallPathOnDDrive -Path "$launcherStatePath.tmp"
        [System.IO.File]::WriteAllText($launcherStateTempPath, $launcherState, (New-Object System.Text.UTF8Encoding($false)))
        Move-Item -LiteralPath $launcherStateTempPath -Destination $launcherStatePath -Force
        $legacyLauncherPath = Assert-CountdownInstallPathOnDDrive -Path $legacyLauncherPath
        if (Test-Path -LiteralPath $legacyLauncherPath -PathType Leaf) {
            $legacyExpected = "@echo off`r`nC:\Windows\System32\schtasks.exe /Run /TN `"$taskName`" >nul 2>&1`r`nexit /b %errorlevel%`r`n"
            $legacyActual = [System.IO.File]::ReadAllText($legacyLauncherPath, [System.Text.Encoding]::ASCII)
            if ($legacyActual -eq $legacyExpected) {
                Remove-Item -LiteralPath $legacyLauncherPath -Force
            }
        }

        $action = New-ScheduledTaskAction -Execute $powerShellEngine -Argument $arguments -WorkingDirectory $install
        $trigger = New-ScheduledTaskTrigger -AtLogOn -User $interactiveUser
        $principal = New-ScheduledTaskPrincipal -UserId $interactiveUser -LogonType Interactive -RunLevel Limited
        $settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries `
            -ExecutionTimeLimit ([TimeSpan]::Zero) -MultipleInstances IgnoreNew
        Register-ScheduledTask -TaskName $taskName -Action $action -Trigger $trigger -Principal $principal `
            -Settings $settings -Description '登录后启动敏敏子倒计时，按北京时间计算。' -Force | Out-Null

        $registeredTask = Get-ScheduledTask -TaskName $taskName
        if (-not [string]::Equals(
                [System.IO.Path]::GetFullPath([string] $registeredTask.Actions.Execute),
                [System.IO.Path]::GetFullPath($powerShellEngine),
                [System.StringComparison]::OrdinalIgnoreCase
            )) {
            throw "计划任务未使用 PowerShell 7：$($registeredTask.Actions.Execute)"
        }

        Start-ScheduledTask -TaskName $taskName
        $runtimeStatus = Wait-CountdownWindowReady -Path $runtimeStatusPath -LaunchToken $startupRequest.launchToken -ExpectedSessionId ([int] $interactiveContext.SessionId) -ExpectedApplicationPath $appPath -TimeoutSeconds 30 -PollMilliseconds 250
        $result = [ordered]@{
            TestOutput = $testOutput.Trim()
            CSharpTestOutput = $cSharpTestOutput.Trim()
            UiTestOutput = $uiTestOutput.Trim()
            UiValidationOutput = $uiValidationOutput.Trim()
            InstalledFiles = @(Get-ChildItem -LiteralPath $install -File -Recurse | Select-Object FullName, Length, @{n='Hash';e={(Get-FileHash -Algorithm SHA256 -LiteralPath $_.FullName).Hash}})
            Launcher = Get-Item -LiteralPath $launcherPath | Select-Object FullName, Length
            Task = Get-ScheduledTask -TaskName $taskName | Select-Object TaskName, State, @{n='Execute';e={$_.Actions.Execute}}, @{n='Arguments';e={$_.Actions.Arguments}}, @{n='LogonType';e={$_.Principal.LogonType}}
            TaskInfo = Get-ScheduledTaskInfo -TaskName $taskName | Select-Object LastRunTime, LastTaskResult, NextRunTime
            RuntimeStatus = $runtimeStatus
            LaunchToken = $startupRequest.launchToken
            InteractiveSessionId = [int] $interactiveContext.SessionId
            TimeSource = $timeSource
            TimeStatus = $timeStatus
            RecoveredTransactions = $recoveredTransactions
            DeferredCommit = [bool] $DeferCommit
            TransactionPath = if ($DeferCommit) { $transaction.transactionPath } else { $null }
        }
        if ($DeferCommit) {
            [void] (Write-CountdownDeferredInstallReceipt -Path $DeferredReceiptPath -TransactionPath $transaction.transactionPath -LaunchToken $startupRequest.launchToken -InteractiveSessionId ([int] $interactiveContext.SessionId))
            $result.DeferredReceiptPath = [System.IO.Path]::GetFullPath($DeferredReceiptPath)
            $result | ConvertTo-Json -Depth 8
            return
        }

        [void] (Assert-CountdownInstallInteractiveContextStable -ExpectedContext $interactiveContext)
        $commitRuntimeStatus = Wait-CountdownWindowReady -Path $runtimeStatusPath -LaunchToken $startupRequest.launchToken -ExpectedSessionId ([int] $interactiveContext.SessionId) -ExpectedApplicationPath $appPath -TimeoutSeconds 0 -PollMilliseconds 1
        if ([int] $commitRuntimeStatus.processId -ne [int] $runtimeStatus.processId) {
            throw '安装提交前受管应用进程已变化；拒绝提交事务。'
        }
        $commitTask = Get-ScheduledTask -TaskName $taskName -ErrorAction Stop
        if ([string] $commitTask.State -ne 'Running') {
            throw "安装提交前计划任务未运行：$($commitTask.State)"
        }
        if (-not (Complete-CountdownStartupRequest -Path $startupRequestPath -LaunchToken $startupRequest.launchToken)) {
            throw '安装事务仍可回滚，但无法授权应用持久化 schema 迁移。'
        }
        [void] (Wait-CountdownStartupRequestState -Path $startupRequestPath -LaunchToken $startupRequest.launchToken -ExpectedState ready -TimeoutSeconds 30 -PollMilliseconds 100)
        [void] (Assert-CountdownInstallInteractiveContextStable -ExpectedContext $interactiveContext)
        $readyRuntimeStatus = Wait-CountdownWindowReady -Path $runtimeStatusPath -LaunchToken $startupRequest.launchToken -ExpectedSessionId ([int] $interactiveContext.SessionId) -ExpectedApplicationPath $appPath -TimeoutSeconds 0 -PollMilliseconds 1
        if ([int] $readyRuntimeStatus.processId -ne [int] $runtimeStatus.processId) {
            throw 'schema 迁移确认后受管应用进程已变化；拒绝提交事务。'
        }
        $readyTask = Get-ScheduledTask -TaskName $taskName -ErrorAction Stop
        if ([string] $readyTask.State -ne 'Running') {
            throw "schema 迁移确认后计划任务未运行：$($readyTask.State)"
        }
        $settingsSnapshotPath = Join-Path $transaction.transactionPath 'install-backup\settings.json'
        $settingsJsonBefore = if (Test-Path -LiteralPath $settingsSnapshotPath -PathType Leaf) { Get-Content -LiteralPath $settingsSnapshotPath -Raw -Encoding UTF8 } else { $null }
        $settingsHashBefore = if (Test-Path -LiteralPath $settingsSnapshotPath -PathType Leaf) { (Get-FileHash -Algorithm SHA256 -LiteralPath $settingsSnapshotPath).Hash } else { $null }
        $settingsPath = Join-Path $install 'settings.json'
        $settingsWriteMutex = Enter-CountdownSettingsWriteMutex -Path $settingsPath -TimeoutSeconds 10
            $settingsJsonAfter = if (Test-Path -LiteralPath $settingsPath -PathType Leaf) { Get-Content -LiteralPath $settingsPath -Raw -Encoding UTF8 } else { $null }
            $settingsHashAfter = if (Test-Path -LiteralPath $settingsPath -PathType Leaf) { (Get-FileHash -Algorithm SHA256 -LiteralPath $settingsPath).Hash } else { $null }
            if (-not (Test-CountdownSettingsDeploymentTransition -BeforeHash $settingsHashBefore -AfterHash $settingsHashAfter -BeforeJson $settingsJsonBefore -AfterJson $settingsJsonAfter)) {
                throw 'C# schema 迁移超出允许的数据等价范围。'
            }
            Complete-CountdownInstallTransaction -TransactionPath $transaction.transactionPath -ExpectedInstallPath $install -ExpectedTaskName $taskName -ExpectedLegacyTaskName $legacyTaskName -ExpectedLauncherPath $launcherPath -ExpectedLegacyLauncherPath $legacyLauncherPath -ExpectedLauncherTempPath $launcherTempPath
            if (-not (Set-CountdownStartupRequestFinalized -Path $startupRequestPath -LaunchToken $startupRequest.launchToken)) {
                throw '安装已接受且仍可回滚，但无法最终启用应用窗口。'
            }
            $acceptanceStatus = Wait-CountdownAcceptanceReadyReceipt -Path $acceptanceStatusPath -LaunchToken $startupRequest.launchToken -ExpectedSessionId ([int] $interactiveContext.SessionId) -ExpectedApplicationPath $appPath -TimeoutSeconds 30 -PollMilliseconds 100
            if ([int] $acceptanceStatus.processId -ne [int] $runtimeStatus.processId) {
                throw '应用解锁确认的进程与 window-ready 进程不一致。'
            }
            [void] (Assert-CountdownInstallInteractiveContextStable -ExpectedContext $interactiveContext)
            $finalTask = Get-ScheduledTask -TaskName $taskName -ErrorAction Stop
            if ([string] $finalTask.State -ne 'Running') {
                throw "应用解锁确认后计划任务未运行：$($finalTask.State)"
            }
            Remove-CountdownCommittedInstallTransaction -TransactionPath $transaction.transactionPath -ExpectedInstallPath $install -ExpectedTaskName $taskName -ExpectedLegacyTaskName $legacyTaskName -ExpectedLauncherPath $launcherPath -ExpectedLegacyLauncherPath $legacyLauncherPath -ExpectedLauncherTempPath $launcherTempPath
            $transactionCompleted = $true
        & "$env:SystemRoot\System32\ie4uinit.exe" -show 2>$null
        $result | ConvertTo-Json -Depth 8
    }
    catch {
        $failure = $_.Exception.Message
        if ($transactionCompleted) {
            throw "安装已提交，但提交后收尾失败，未回滚：$failure"
        }
        if (-not $transactionCompleted) {
            $rollbackResult = $null
            try {
                # 任意失败都优先恢复应用、任务与时间服务；若用户在事务窗口替换桌面文件，只保留该文件和可审计 journal。
                $rollbackResult = Undo-CountdownInstallTransaction -TransactionPath $transaction.transactionPath -PowerShell7Path $powerShellEngine -ExpectedInstallPath $install -ExpectedTaskName $taskName -ExpectedLegacyTaskName $legacyTaskName -ExpectedLauncherPath $launcherPath -ExpectedLegacyLauncherPath $legacyLauncherPath -ExpectedLauncherTempPath $launcherTempPath -PreserveUnmanagedLaunchers
            }
            catch {
                throw "安装失败：$failure；回滚也失败：$($_.Exception.Message)"
            }
            if ($null -ne $rollbackResult -and [bool] $rollbackResult.PreservedUnmanagedLaunchers) {
                throw "安装失败，已恢复应用、任务和时间服务；为保护用户替换的桌面启动器，保留了事务证据：$failure"
            }
        }
        throw "安装失败，已回滚：$failure"
    }
}
finally {
    if ($null -ne $settingsWriteMutex) {
        Exit-CountdownSettingsWriteMutex -Mutex $settingsWriteMutex
    }
    if ($null -ne $installMutex) {
        Exit-CountdownInstallTransactionMutex -Mutex $installMutex
    }
}
