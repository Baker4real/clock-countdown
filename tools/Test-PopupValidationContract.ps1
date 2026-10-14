#requires -Version 7.0

[CmdletBinding()]
param(
    [string] $AssemblyPath = (Join-Path (Split-Path -Parent $PSScriptRoot) 'app/MinminCountdown.dll')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)

Import-Module (Join-Path $PSScriptRoot 'Countdown.SuiteIsolation.psm1') -Force
if ([Environment]::GetEnvironmentVariable('MINMIN_SUITE_ALLOW_UNISOLATED') -eq '1') {
    throw '动效验证夹具拒绝非隔离绕过；必须经官方 ValidationHarness 隔离阶段执行。'
}
Assert-SuiteIsolatedDesktop
if ([Threading.Thread]::CurrentThread.ApartmentState -ne 'STA') {
    throw '弹出层验证反例必须由官方 ValidationHarness 阶段在 STA 隔离桌面中运行。'
}
if (-not (Test-Path -LiteralPath $AssemblyPath -PathType Leaf)) {
    throw "未找到被测应用程序集：$AssemblyPath"
}

$resolvedAssembly = (Resolve-Path -LiteralPath $AssemblyPath).Path
Add-Type -AssemblyName PresentationFramework, PresentationCore, WindowsBase, System.Xaml
$assemblyDirectory = Split-Path -Parent $resolvedAssembly
# PowerShell does not run the application's deps.json probing for pack URIs.
# Preload only these two collocated production dependencies before App resources.
$fixtureDependencies = @(
    foreach ($dependencyName in @('Wpf.Ui.Abstractions.dll', 'Wpf.Ui.dll')) {
        $dependencyPath = Join-Path $assemblyDirectory $dependencyName
        if (-not (Test-Path -LiteralPath $dependencyPath -PathType Leaf)) {
            throw "未找到夹具所需的生产依赖：$dependencyPath"
        }
        $dependency = [Reflection.Assembly]::LoadFrom($dependencyPath)
        [pscustomobject]@{ Path = $dependency.Location; Identity = $dependency.FullName }
    }
)
$references = @(
    Get-ChildItem -LiteralPath (Join-Path $PSHOME 'ref') -Filter '*.dll' |
        Where-Object Name -ne 'WindowsBase.dll' |
        Select-Object -ExpandProperty FullName
    [System.Windows.Window].Assembly.Location
    [System.Windows.UIElement].Assembly.Location
    [System.Windows.Threading.Dispatcher].Assembly.Location
    [System.Windows.Markup.IQueryAmbient].Assembly.Location
)

# Fixtures replace only the target popup's event handling. All verdicts come
# from the currently built application's real ExercisePopup implementation.
$harnessSourcePath = Join-Path $PSScriptRoot 'PopupValidationContract.cs'
Add-Type -ReferencedAssemblies $references -TypeDefinition ([IO.File]::ReadAllText($harnessSourcePath))

$results = @(
    foreach ($scenario in @('real-close', 'real-close-motion', 'handled-without-close', 'close-after-deadline')) {
        [PopupValidationContractHarness]::Run($resolvedAssembly, $scenario)
    }
)
$failures = [Collections.Generic.List[string]]::new()
$popupCollectibility = [PopupValidationContractHarness]::CheckPopupCollectibility($resolvedAssembly)
if ($popupCollectibility.PlainRetained -ne 0 -or $popupCollectibility.AttachedRetained -ne 0 -or
    $popupCollectibility.ClippedRetained -ne 0 -or $popupCollectibility.CombinedRetained -ne 0) {
    $failures.Add('丢弃的便签菜单、触发器和画布必须可回收；焦点注册与画布约束不能成为静态强引用根。')
}
$tooltipCollectibility = [PopupValidationContractHarness]::CheckToolTipCollectibility($resolvedAssembly)
$floatingNoteCollectibility = [PopupValidationContractHarness]::CheckFloatingNoteWorkspaceCollectibility($resolvedAssembly)
if ($floatingNoteCollectibility.ObservedRetiredReferences -ne $floatingNoteCollectibility.ExpectedRetiredReferences -or
    $floatingNoteCollectibility.RetainedReferences -ne 0 -or -not $floatingNoteCollectibility.SettingsFileNotCreated) {
    $failures.Add('真实悬浮窗连续切换工作区后，已替换的便签菜单及按钮必须全部可回收。')
}
if (-not ($tooltipCollectibility.PlainCollected -and $tooltipCollectibility.AnimatedCollected)) {
    $failures.Add('临时 ToolTip 及其动效监听器应可回收；普通 ToolTip 是 GC 正向对照。')
}
$closedPopupResults = @(
    foreach ($previouslyOpened in @($false, $true)) {
        foreach ($escape in @($true, $false)) {
            [PopupValidationContractHarness]::CheckClosedPopupClose($resolvedAssembly, $previouslyOpened, $escape)
        }
    }
)
foreach ($closedPopup in $closedPopupResults) {
    if (-not ($closedPopup.PrerequisiteClosed -and $closedPopup.RemainedClosed -and $closedPopup.FinalClosed) -or
        $closedPopup.OpenedEvents -ne 0) {
        $failures.Add("关闭态 Popup 不应被 $($closedPopup.Action) 闪开；previouslyOpened=$($closedPopup.PreviouslyOpened)。")
    }
}
$captureComposite = [PopupValidationContractHarness]::CheckCaptureComposite($resolvedAssembly)
if (-not ($captureComposite.StraightAlphaMatches -and $captureComposite.PremultipliedInputMatches -and
          $captureComposite.HalfWhiteOverWhiteStaysWhite -and $captureComposite.OpaqueSourcePreserved -and
          $captureComposite.TransparentFrameRejected)) {
    $failures.Add('参考帧一像素透明合成或全透明拒绝合同未通过。')
}
$motion = [MotionValidationContractHarness]::Run($resolvedAssembly)
if (-not $motion.Passed) {
    $failures.Add('生产 WPF 动效模式、连续中断、负例、作用域恢复或卡顿重跑自检未通过；详见 Motion.Results 与 Motion.StallRetry。')
}
foreach ($result in $results) {
    if ($result.HandledEscapeCount -lt 1) {
        $failures.Add("$($result.Scenario)：未实际走到已处理的 Esc 事件。")
    }
    if (-not $result.CleanedAfterExercise) {
        $failures.Add("$($result.Scenario)：验证方法 finally 未清理弹层。")
    }
    if ($result.Scenario -eq 'real-close-motion' -and -not $result.ReopenedWithInitialFocus) {
        $failures.Add('带动画的弹层清理后重开未恢复初始键盘焦点。')
    }
    if ($result.Scenario -in @('real-close', 'real-close-motion')) {
        if (-not ($result.EscapeReturnsFocus -and $result.EscapeFromTriggerClosesAndKeepsFocus -and
                  $result.TriggerSecondClickCloses -and $result.OutsideFocusPreserved)) {
            $failures.Add('真实 PopupFocusLifecycle 关闭和焦点回归未通过。')
        }
    }
    elseif ($result.EscapeReturnsFocus) {
        $failures.Add("$($result.Scenario)：Esc 未按时关闭，却被验证器或后续清理误判为通过。")
    }
    if ($result.Scenario -eq 'handled-without-close' -and $result.EscapeFromTriggerClosesAndKeepsFocus) {
        $failures.Add('触发按钮的 Esc 仅设 Handled=true 而不关闭，被错误接受。')
    }
}

$trayMenu = $null
try {
    & (Join-Path $PSScriptRoot 'Test-CountdownTrayMenu.ps1') -AssemblyPath $resolvedAssembly |
        ForEach-Object { $trayMenu = $_ }
}
catch {
    $failures.Add($_.Exception.Message)
}
if ($null -eq $trayMenu) {
    $failures.Add('托盘菜单验证未返回可回读的报告。')
}

[pscustomobject]@{
    Suite = 'PopupValidationContract'
    AssemblyPath = $resolvedAssembly
    FixtureDependencies = $fixtureDependencies
    AssemblySHA256 = (Get-FileHash -LiteralPath $resolvedAssembly -Algorithm SHA256).Hash
    HarnessSHA256 = (Get-FileHash -LiteralPath $PSCommandPath -Algorithm SHA256).Hash
    HarnessSourceSHA256 = (Get-FileHash -LiteralPath $harnessSourcePath -Algorithm SHA256).Hash
    Results = $results
    CaptureComposite = $captureComposite
    ClosedPopupResults = $closedPopupResults
    PopupCollectibility = $popupCollectibility
    FloatingNoteWorkspaceCollectibility = $floatingNoteCollectibility
    ToolTipCollectibility = $tooltipCollectibility
    Motion = $motion
    TrayMenu = $trayMenu
    Failures = @($failures)
    Passed = $failures.Count -eq 0
} | ConvertTo-Json -Depth 10
if ($failures.Count -gt 0) {
    throw ($failures -join [Environment]::NewLine)
}
