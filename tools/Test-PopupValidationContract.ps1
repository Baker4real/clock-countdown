#requires -Version 7.0

[CmdletBinding()]
param(
    [string] $AssemblyPath = (Join-Path (Split-Path -Parent $PSScriptRoot) 'app/MinminCountdown.dll')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Import-Module (Join-Path $PSScriptRoot 'Countdown.SuiteIsolation.psm1') -Force
Assert-SuiteIsolatedDesktop
if ([Threading.Thread]::CurrentThread.ApartmentState -ne 'STA') {
    throw '弹出层验证反例必须由官方 ValidationHarness 阶段在 STA 隔离桌面中运行。'
}
if (-not (Test-Path -LiteralPath $AssemblyPath -PathType Leaf)) {
    throw "未找到被测应用程序集：$AssemblyPath"
}

$resolvedAssembly = (Resolve-Path -LiteralPath $AssemblyPath).Path
Add-Type -AssemblyName PresentationFramework, PresentationCore, WindowsBase, System.Xaml
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
Add-Type -ReferencedAssemblies $references -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;

public sealed class PopupValidationContractResult
{
    public string Scenario { get; set; }
    public bool EscapeReturnsFocus { get; set; }
    public bool EscapeFromTriggerClosesAndKeepsFocus { get; set; }
    public bool TriggerSecondClickCloses { get; set; }
    public bool OutsideFocusPreserved { get; set; }
    public int HandledEscapeCount { get; set; }
    public bool CleanedAfterExercise { get; set; }
}

public static class PopupValidationContractHarness
{
    public static PopupValidationContractResult Run(string assemblyPath, string scenario)
    {
        Assembly application = Assembly.LoadFrom(assemblyPath);
        Type input = application.GetType("MinminCountdown.WindowValidationInput", true);
        MethodInfo exercise = input.GetMethod("ExercisePopup", BindingFlags.Public | BindingFlags.Static);
        MethodInfo pump = input.GetMethod("Pump", BindingFlags.Public | BindingFlags.Static);
        Type lifecycle = application.GetType("MinminCountdown.PopupFocusLifecycle", true);
        Type motion = application.GetType("MinminCountdown.PopupMotionBehavior", true);
        MethodInfo toggle = motion.GetMethod("Toggle", BindingFlags.NonPublic | BindingFlags.Static);
        var trigger = new Button { Content = "Open popup", Width = 130, Height = 30 };
        var outside = new TextBox { Text = "Outside focus", Width = 160, Height = 30 };
        var initial = new Button { Content = "Popup action", Width = 130, Height = 30 };
        var surface = new StackPanel();
        surface.Children.Add(initial);
        var host = new StackPanel();
        host.Children.Add(trigger);
        host.Children.Add(outside);
        var window = new Window
        {
            Title = "MinminCountdown Popup Validation Contract",
            Width = 300,
            Height = 200,
            Content = host,
            ShowInTaskbar = false
        };
        var popup = new Popup
        {
            PlacementTarget = trigger,
            Placement = PlacementMode.Bottom,
            Child = surface,
            StaysOpen = true
        };
        var timers = new List<DispatcherTimer>();
        int handledEscapeCount = 0;
        if (scenario == "real-close")
        {
            lifecycle.GetMethod("Attach", BindingFlags.Public | BindingFlags.Static).Invoke(
                null, new object[] { popup, trigger, new Func<IInputElement>(() => initial) });
            KeyEventHandler recordEscape = (sender, args) =>
            {
                if (args.Key == Key.Escape && args.Handled) handledEscapeCount++;
            };
            surface.AddHandler(Keyboard.PreviewKeyDownEvent, recordEscape, true);
            trigger.AddHandler(Keyboard.PreviewKeyDownEvent, recordEscape, true);
        }
        else
        {
            popup.Opened += (sender, args) => popup.Dispatcher.BeginInvoke(
                DispatcherPriority.Input, new Action(() => Keyboard.Focus(initial)));
            KeyEventHandler brokenEscape = (sender, args) =>
            {
                if (args.Key != Key.Escape) return;
                args.Handled = true;
                handledEscapeCount++;
                window.Activate();
                FocusManager.SetFocusedElement(window, trigger);
                Keyboard.Focus(trigger);
                if (scenario == "close-after-deadline")
                {
                    // The validator's 300ms deadline must fail before this close.
                    var timer = new DispatcherTimer(DispatcherPriority.Normal, popup.Dispatcher)
                    {
                        Interval = TimeSpan.FromMilliseconds(650)
                    };
                    timer.Tick += (timerSender, timerArgs) =>
                    {
                        timer.Stop();
                        popup.IsOpen = false;
                    };
                    timers.Add(timer);
                    timer.Start();
                }
            };
            surface.AddHandler(Keyboard.PreviewKeyDownEvent, brokenEscape, true);
            trigger.AddHandler(Keyboard.PreviewKeyDownEvent, brokenEscape, true);
        }
        trigger.Click += (sender, args) => toggle.Invoke(null, new object[] { popup });
        try
        {
            window.Show();
            window.Activate();
            pump.Invoke(null, new object[] { window.Dispatcher });
            object result = exercise.Invoke(null, new object[] { popup, trigger, initial, outside });
            Type resultType = result.GetType();
            return new PopupValidationContractResult
            {
                Scenario = scenario,
                EscapeReturnsFocus = (bool)resultType.GetProperty("EscapeReturnsFocus").GetValue(result),
                EscapeFromTriggerClosesAndKeepsFocus = (bool)resultType.GetProperty("EscapeFromTriggerClosesAndKeepsFocus").GetValue(result),
                TriggerSecondClickCloses = (bool)resultType.GetProperty("TriggerSecondClickCloses").GetValue(result),
                OutsideFocusPreserved = (bool)resultType.GetProperty("OutsideFocusPreserved").GetValue(result),
                HandledEscapeCount = handledEscapeCount,
                CleanedAfterExercise = !popup.IsOpen
            };
        }
        finally
        {
            foreach (DispatcherTimer timer in timers) timer.Stop();
            popup.IsOpen = false;
            window.Close();
            pump.Invoke(null, new object[] { window.Dispatcher });
        }
    }
}
'@

$results = @(
    foreach ($scenario in @('real-close', 'handled-without-close', 'close-after-deadline')) {
        [PopupValidationContractHarness]::Run($resolvedAssembly, $scenario)
    }
)
$failures = [Collections.Generic.List[string]]::new()
foreach ($result in $results) {
    if ($result.HandledEscapeCount -lt 1) {
        $failures.Add("$($result.Scenario)：未实际走到已处理的 Esc 事件。")
    }
    if (-not $result.CleanedAfterExercise) {
        $failures.Add("$($result.Scenario)：验证方法 finally 未清理弹层。")
    }
    if ($result.Scenario -eq 'real-close') {
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

[pscustomobject]@{
    Suite = 'PopupValidationContract'
    AssemblyPath = $resolvedAssembly
    AssemblySHA256 = (Get-FileHash -LiteralPath $resolvedAssembly -Algorithm SHA256).Hash
    Results = $results
    Failures = @($failures)
    Passed = $failures.Count -eq 0
} | ConvertTo-Json -Depth 5
if ($failures.Count -gt 0) {
    throw ($failures -join [Environment]::NewLine)
}
