#requires -Version 7.0
# 套件隔离门禁：默认隔离、fail-closed、泄漏匹配、生产进程清理与收口 runner 路由。
# 不创建业务窗、不抢焦点；可在用户 Default 桌面直跑。
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$modulePath = Join-Path $PSScriptRoot 'Countdown.SuiteIsolation.psm1'
$launcherPath = Join-Path $PSScriptRoot 'Invoke-CountdownSuiteLowImpact.ps1'
$watcherPath = Join-Path $PSScriptRoot 'Watch-DesktopWindows.ps1'
$runnerPath = Join-Path $PSScriptRoot 'Invoke-CountdownVerificationRun.ps1'
$validationPath = Join-Path $repoRoot 'Countdown.Validation.psm1'
$csharpTestsPath = Join-Path $repoRoot 'Countdown.CSharp.Tests.ps1'
$uiTestsPath = Join-Path $repoRoot 'Countdown.Ui.Tests.ps1'
$sourceValidationPath = Join-Path $repoRoot 'Invoke-CountdownSourceValidation.ps1'
$readmePath = Join-Path $repoRoot 'README.md'

function Assert-Guard {
    param(
        [Parameter(Mandatory)] [bool] $Condition,
        [Parameter(Mandatory)] [string] $Name
    )
    if (-not $Condition) {
        throw "isolation-guard 失败：$Name"
    }
}

Assert-Guard (Test-Path -LiteralPath $modulePath -PathType Leaf) 'Countdown.SuiteIsolation.psm1 存在'
Import-Module -Name $modulePath -Force

# --- 桌面名匹配 ---
Assert-Guard (Test-SuiteIsolatedDesktopName -DesktopName 'MinminSuiteIso') '缺省隔离桌面名匹配'
Assert-Guard (Test-SuiteIsolatedDesktopName -DesktopName 'MinminSuiteIso-CSharp') '阶段隔离桌面名匹配'
Assert-Guard (Test-SuiteIsolatedDesktopName -DesktopName 'MinminSuiteIso-SourceValidation') 'SourceValidation 桌面名匹配'
Assert-Guard (-not (Test-SuiteIsolatedDesktopName -DesktopName 'Default')) '用户 Default 桌面不匹配'
Assert-Guard (-not (Test-SuiteIsolatedDesktopName -DesktopName 'MinminSuiteIsoEvil')) '前缀相近但非阶段名不匹配'
Assert-Guard (-not (Test-SuiteIsolatedDesktopName -DesktopName 'Winlogon')) '无关桌面不匹配'

# --- fail-closed：注入桌面名，不弹 WPF ---
$previousAllow = $env:MINMIN_SUITE_ALLOW_UNISOLATED
try {
    Remove-Item Env:MINMIN_SUITE_ALLOW_UNISOLATED -ErrorAction SilentlyContinue

    $defaultFailed = $false
    try {
        Assert-SuiteIsolatedDesktop -DesktopName 'Default'
    }
    catch {
        $defaultFailed = [string] $_.Exception.Message -match '拒绝在用户桌面'
    }
    Assert-Guard $defaultFailed 'Default 桌面 fail-closed'

    Assert-SuiteIsolatedDesktop -DesktopName 'MinminSuiteIso-Ui'
    Assert-SuiteIsolatedDesktop -DesktopName 'MinminSuiteIso'

    $unisolatedSwitchFailed = $false
    try {
        Assert-SuiteIsolatedDesktop -DesktopName 'Default' -AllowForegroundAuthorized:$false
    }
    catch {
        $unisolatedSwitchFailed = $true
    }
    Assert-Guard $unisolatedSwitchFailed '未授权前台仍 fail-closed'

    Assert-SuiteIsolatedDesktop -DesktopName 'Default' -AllowForegroundAuthorized:$true

    $env:MINMIN_SUITE_ALLOW_UNISOLATED = '1'
    Assert-SuiteIsolatedDesktop -DesktopName 'Default'
}
finally {
    if ($null -eq $previousAllow) {
        Remove-Item Env:MINMIN_SUITE_ALLOW_UNISOLATED -ErrorAction SilentlyContinue
    }
    else {
        $env:MINMIN_SUITE_ALLOW_UNISOLATED = $previousAllow
    }
}

# --- 启动器隔离决议 ---
$defaultMode = Resolve-SuiteLauncherIsolation -IsolatedDesktopBound:$false -IsolatedDesktop:$false -AllowUnisolatedDesktop:$false
Assert-Guard $defaultMode.Isolated '省略 -IsolatedDesktop 时默认隔离'

$explicitOn = Resolve-SuiteLauncherIsolation -IsolatedDesktopBound:$true -IsolatedDesktop:$true -AllowUnisolatedDesktop:$false
Assert-Guard $explicitOn.Isolated '显式 -IsolatedDesktop 仍隔离'

$falseRejected = $false
try {
    $null = Resolve-SuiteLauncherIsolation -IsolatedDesktopBound:$true -IsolatedDesktop:$false -AllowUnisolatedDesktop:$false
}
catch {
    $falseRejected = [string] $_.Exception.Message -match 'AllowUnisolatedDesktop'
}
Assert-Guard $falseRejected '-IsolatedDesktop:$false 无 opt-out 时拒绝'

$optOut = Resolve-SuiteLauncherIsolation -IsolatedDesktopBound:$true -IsolatedDesktop:$false -AllowUnisolatedDesktop:$true
Assert-Guard (-not $optOut.Isolated) '-AllowUnisolatedDesktop 才允许关隔离'

# --- 泄漏窗口匹配：可见敏敏子 / 套件 PID / MinminCountdown；无关窗不红 ---
Assert-Guard (Test-SuiteDesktopLeakWindow -ProcessId 42 -ProcessName 'MinminCountdown' -ClassName 'HwndWrapper[MinminCountdown.exe;;1]' -Title '敏敏子悬浮窗' -Visible:$true -SuiteProcessIds @(42)) '敏敏子可见顶层窗是泄漏'
Assert-Guard (Test-SuiteDesktopLeakWindow -ProcessId 9 -ProcessName 'pwsh' -ClassName 'HwndWrapper[pwsh.exe;;1]' -Title '敏敏子倒计时' -Visible:$true -SuiteProcessIds @(9)) 'pwsh 宿主的敏敏子标题是泄漏'
Assert-Guard (Test-SuiteDesktopLeakWindow -ProcessId 7 -ProcessName 'MinminCountdown' -ClassName 'HwndWrapper[MinminCountdown.exe;;1]' -Title '' -Visible:$true -SuiteProcessIds @(1)) 'MinminCountdown 进程可见窗是泄漏'
Assert-Guard (-not (Test-SuiteDesktopLeakWindow -ProcessId 7 -ProcessName 'MinminCountdown' -ClassName 'HwndWrapper[MinminCountdown.exe;;1]' -Title '敏敏子倒计时' -Visible:$false -SuiteProcessIds @(7))) '不可见窗不是泄漏'
Assert-Guard (-not (Test-SuiteDesktopLeakWindow -ProcessId 100 -ProcessName 'Cursor' -ClassName 'Chrome_WidgetWin_1' -Title 'README.md' -Visible:$true -SuiteProcessIds @(42))) '无关用户窗不是泄漏'
Assert-Guard (-not (Test-SuiteDesktopLeakWindow -ProcessId 200 -ProcessName 'notepad' -ClassName 'Notepad' -Title '无标题' -Visible:$true -SuiteProcessIds @(42))) '记事本不是泄漏'
Assert-Guard (-not (Test-SuiteDesktopLeakWindow -ProcessId 300 -ProcessName 'devenv' -ClassName 'HwndWrapper[devenv.exe;;1]' -Title 'Microsoft Visual Studio' -Visible:$true -SuiteProcessIds @(42))) '其他 WPF 应用不是泄漏'
Assert-Guard (Test-SuiteDesktopLeakWindow -ProcessId 400 -ProcessName 'minmin-countdown-slice' -ClassName 'Chrome_WidgetWin_1' -Title '' -Visible:$true -SuiteProcessIds @()) 'Tauri 切片无标题可见窗是泄漏'
Assert-Guard (-not (Test-SuiteDesktopLeakWindow -ProcessId 400 -ProcessName 'minmin-countdown-slice' -ClassName 'Chrome_WidgetWin_1' -Title '' -Visible:$false -SuiteProcessIds @())) 'Tauri 切片不可见窗不是泄漏'

# --- 源码合同：启动器 / watcher / 内层脚本 / 收口 runner / 生产进程清理 ---
$launcherSource = Get-Content -LiteralPath $launcherPath -Raw -Encoding UTF8
Assert-Guard ($launcherSource -match 'AllowUnisolatedDesktop') '启动器声明 -AllowUnisolatedDesktop'
Assert-Guard ($launcherSource -match 'Resolve-SuiteLauncherIsolation') '启动器走隔离决议'
Assert-Guard ($launcherSource -match 'Watch-DesktopWindows') '启动器接入桌面泄漏监测'
Assert-Guard ($launcherSource -match '用户正在用电脑时禁止') '启动器 opt-out 写清禁止条件'

$watcherSource = Get-Content -LiteralPath $watcherPath -Raw -Encoding UTF8
Assert-Guard ($watcherSource -match 'SuiteLeakOnly') '监测脚本可只报套件泄漏'
Assert-Guard ($watcherSource -notmatch 'New-Object.*Window|System\.Windows\.Window') '监测脚本自己不弹 WPF 窗'

$csharpSource = Get-Content -LiteralPath $csharpTestsPath -Raw -Encoding UTF8
Assert-Guard ($csharpSource -match 'Assert-SuiteIsolatedDesktop') 'CSharp 测试 fail-closed'
$hiddenProbeIndex = $csharpSource.IndexOf('Assert-HiddenTopLevelWindowEnumeration')
$assertIndex = $csharpSource.IndexOf('Assert-SuiteIsolatedDesktop')
Assert-Guard ($assertIndex -ge 0 -and $assertIndex -lt $hiddenProbeIndex) 'CSharp fail-closed 在首个 WPF 探针之前'

$uiSource = Get-Content -LiteralPath $uiTestsPath -Raw -Encoding UTF8
Assert-Guard ($uiSource -match 'Assert-SuiteIsolatedDesktop') 'Ui 测试 fail-closed'

$sourceValidationSource = Get-Content -LiteralPath $sourceValidationPath -Raw -Encoding UTF8
Assert-Guard ($sourceValidationSource -match 'Assert-SuiteIsolatedDesktop') 'SourceValidation fail-closed'

$productionKillPattern = 'Stop-DesktopLauncherHostProcesses[^\r\n]*敏敏子倒计时\\app\\MinminCountdown\.exe'
Assert-Guard ($csharpSource -notmatch $productionKillPattern) '桌面启动器测试 finally 不按生产安装路径杀进程'

$runnerSource = Get-Content -LiteralPath $runnerPath -Raw -Encoding UTF8
Assert-Guard ($runnerSource -match 'AllowForegroundWindowedSteps') '收口 runner 前台窗步骤需显式授权'
Assert-Guard ($runnerSource -match 'Start-IsolatedSuiteProcess') '收口 runner 窗口步骤走隔离启动器'
Assert-Guard ($runnerSource -match '用户正在用电脑时禁止') '收口 runner 写清前台授权禁止条件'

$validationSource = Get-Content -LiteralPath $validationPath -Raw -Encoding UTF8
Assert-Guard ($validationSource -match 'Start-IsolatedSuiteProcess') '验证套件窗口步骤走隔离启动器'
Assert-Guard ($validationSource -match 'MINMIN_SUITE_ALLOW_UNISOLATED') '验证套件尊重隔离 opt-out'
Assert-Guard ($validationSource -match 'MinminSuiteIso-ValidateCSharp') 'C# 回归使用独立隔离桌面'
Assert-Guard ($validationSource -match 'MinminSuiteIso-ValidateUi') '兼容 UI 回归使用独立隔离桌面'
Assert-Guard ($validationSource -match 'MinminSuiteIso-ValidateApp') 'validate-only 使用独立隔离桌面'
$coreInvoke = [regex]::Match(
    $validationSource,
    '\$core = Invoke-CountdownValidationProcess[\s\S]{0,250}Operation ''Core 回归''')
Assert-Guard ($coreInvoke.Success) 'Core 回归仍走 ValidationProcess'
Assert-Guard ($coreInvoke.Value -notmatch 'IsolatedDesktopName') 'Core 回归不进隔离桌面'

$readme = Get-Content -LiteralPath $readmePath -Raw -Encoding UTF8
Assert-Guard ($readme -match '仅启动器可跑|只经低影响启动器') 'README 标明内层命令不可当复制模板'
Assert-Guard ($readme -match 'AllowUnisolatedDesktop') 'README 记录隔离 opt-out'
Assert-Guard ($readme -match '用户正在用电脑时禁止') 'README 写清 opt-out 禁止条件'

# --- 活探针：隔离桌面上 Assert 通过；当前 Default 桌面 GetThreadDesktop 不是 MinminSuiteIso ---
$currentDesktop = Get-SuiteThreadDesktopName
Assert-Guard (-not (Test-SuiteIsolatedDesktopName -DesktopName $currentDesktop)) '测试进程本身在用户桌面（活探针负例）'
$defaultAssertFailed = $false
try {
    Assert-SuiteIsolatedDesktop
}
catch {
    $defaultAssertFailed = [string] $_.Exception.Message -match '拒绝在用户桌面'
}
Assert-Guard $defaultAssertFailed '当前线程未隔离时 fail-closed（不弹 WPF）'

$liveRoot = Join-Path $env:TEMP 'minmin-suite-isolation-guard'
[void] [System.IO.Directory]::CreateDirectory($liveRoot)
$liveResult = Join-Path $liveRoot 'assert.result.txt'
$liveScript = Join-Path $liveRoot 'assert.ps1'
$liveStdout = Join-Path $liveRoot 'assert.out.log'
$liveStderr = Join-Path $liveRoot 'assert.err.log'
if (Test-Path -LiteralPath $liveResult) { Remove-Item -LiteralPath $liveResult -Force }
@'
param([Parameter(Mandatory)] [string] $ModulePath, [Parameter(Mandatory)] [string] $ResultPath)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module -Name $ModulePath -Force
Assert-SuiteIsolatedDesktop
$desktop = Get-SuiteThreadDesktopName
[System.IO.File]::WriteAllText($ResultPath, $desktop)
'@ | Set-Content -LiteralPath $liveScript -Encoding UTF8

$liveExit = [int] (& (Join-Path $PSScriptRoot 'Start-IsolatedSuiteProcess.ps1') `
    -FilePath (Get-Command pwsh).Source `
    -ArgumentList @('-NoLogo', '-NoProfile', '-NonInteractive', '-File', $liveScript, '-ModulePath', $modulePath, '-ResultPath', $liveResult) `
    -WorkingDirectory $PSScriptRoot `
    -StdoutPath $liveStdout `
    -StderrPath $liveStderr `
    -DesktopName 'MinminSuiteIso-CSharp' | Select-Object -Last 1)
Assert-Guard ($liveExit -eq 0) "隔离桌面 Assert 退出码 0（实际 $liveExit）"
$liveDesktop = if (Test-Path -LiteralPath $liveResult) { (Get-Content -LiteralPath $liveResult -Raw).Trim() } else { '' }
Assert-Guard ($liveDesktop -ceq 'MinminSuiteIso-CSharp') "隔离桌面名回读 MinminSuiteIso-CSharp（实际 '$liveDesktop'）"

Write-Output 'isolation-guard: PASS'
