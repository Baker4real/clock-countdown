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
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
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
    public bool? ReopenedWithInitialFocus { get; set; }
}

public static class PopupValidationContractHarness
{
    public static object CheckCaptureComposite(string assemblyPath)
    {
        var method = Assembly.LoadFrom(assemblyPath).GetType("MinminCountdown.ValidationReport", true)
            .GetMethod("CompositeOverOpaqueBackground", BindingFlags.NonPublic | BindingFlags.Static);
        byte[] Composite(byte[] pixel, PixelFormat format, Color backdrop)
        {
            var source = BitmapSource.Create(1, 1, 96, 96, format, null, pixel, 4);
            var result = (BitmapSource)method.Invoke(null, new object[] { source, backdrop });
            var actual = new byte[4];
            result.CopyPixels(actual, 4, 0);
            return actual;
        }
        bool Matches(byte[] actual, byte b, byte g, byte r) =>
            actual[0] == b && actual[1] == g && actual[2] == r && actual[3] == 255;
        var backdrop = Color.FromRgb(20, 40, 60);
        var straight = Composite(new byte[] { 50, 100, 200, 128 }, PixelFormats.Bgra32, backdrop);
        var premultiplied = Composite(new byte[] { 128, 128, 128, 128 }, PixelFormats.Pbgra32, Colors.Black);
        var white = Composite(new byte[] { 255, 255, 255, 128 }, PixelFormats.Bgra32, Colors.White);
        var opaque = Composite(new byte[] { 30, 40, 50, 255 }, PixelFormats.Bgra32, backdrop);
        bool transparentRejected = false;
        try { Composite(new byte[] { 0, 0, 0, 0 }, PixelFormats.Bgra32, backdrop); }
        catch (TargetInvocationException exception) when (exception.InnerException is InvalidOperationException)
        { transparentRejected = true; }
        return new
        {
            StraightActualBgra = straight,
            PremultipliedActualBgra = premultiplied,
            WhiteActualBgra = white,
            StraightAlphaMatches = Matches(straight, 55, 70, 110),
            PremultipliedInputMatches = Matches(premultiplied, 128, 128, 128),
            HalfWhiteOverWhiteStaysWhite = Matches(white, 255, 255, 255),
            OpaqueSourcePreserved = Matches(opaque, 30, 40, 50),
            TransparentFrameRejected = transparentRejected
        };
    }

    public static PopupValidationContractResult Run(string assemblyPath, string scenario)
    {
        // Match ApplicationValidation's renderer on an isolated desktop, which
        // has no composited DWM surface for WPF's hardware animation clocks.
        RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;
        AppContext.SetSwitch("Switch.System.Windows.Media.ShouldRenderEvenWhenNoDisplayDevicesAreAvailable", true);
        if (Application.Current is null)
        {
            new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        }
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
        if (scenario == "real-close" || scenario == "real-close-motion")
        {
            if (scenario == "real-close-motion")
            {
                motion.GetMethod("Attach", BindingFlags.NonPublic | BindingFlags.Static).Invoke(
                    null, new object[] { popup });
            }
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
            bool cleanedAfterExercise = !popup.IsOpen;
            bool? reopenedWithInitialFocus = null;
            if (scenario == "real-close-motion")
            {
                reopenedWithInitialFocus = false;
                if (cleanedAfterExercise)
                {
                    Keyboard.Focus(outside);
                    popup.IsOpen = true;
                    pump.Invoke(null, new object[] { popup.Dispatcher });
                    reopenedWithInitialFocus = popup.IsOpen && ReferenceEquals(Keyboard.FocusedElement, initial);
                    input.GetMethod("ClosePopupForCleanup", BindingFlags.Public | BindingFlags.Static).Invoke(
                        null, new object[] { popup });
                }
            }
            return new PopupValidationContractResult
            {
                Scenario = scenario,
                EscapeReturnsFocus = (bool)resultType.GetProperty("EscapeReturnsFocus").GetValue(result),
                EscapeFromTriggerClosesAndKeepsFocus = (bool)resultType.GetProperty("EscapeFromTriggerClosesAndKeepsFocus").GetValue(result),
                TriggerSecondClickCloses = (bool)resultType.GetProperty("TriggerSecondClickCloses").GetValue(result),
                OutsideFocusPreserved = (bool)resultType.GetProperty("OutsideFocusPreserved").GetValue(result),
                HandledEscapeCount = handledEscapeCount,
                CleanedAfterExercise = cleanedAfterExercise,
                ReopenedWithInitialFocus = reopenedWithInitialFocus
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
    foreach ($scenario in @('real-close', 'real-close-motion', 'handled-without-close', 'close-after-deadline')) {
        [PopupValidationContractHarness]::Run($resolvedAssembly, $scenario)
    }
)
$failures = [Collections.Generic.List[string]]::new()
$captureComposite = [PopupValidationContractHarness]::CheckCaptureComposite($resolvedAssembly)
if (-not ($captureComposite.StraightAlphaMatches -and $captureComposite.PremultipliedInputMatches -and
          $captureComposite.HalfWhiteOverWhiteStaysWhite -and $captureComposite.OpaqueSourcePreserved -and
          $captureComposite.TransparentFrameRejected)) {
    $failures.Add('参考帧一像素透明合成或全透明拒绝合同未通过。')
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

[pscustomobject]@{
    Suite = 'PopupValidationContract'
    AssemblyPath = $resolvedAssembly
    AssemblySHA256 = (Get-FileHash -LiteralPath $resolvedAssembly -Algorithm SHA256).Hash
    Results = $results
    CaptureComposite = $captureComposite
    Failures = @($failures)
    Passed = $failures.Count -eq 0
} | ConvertTo-Json -Depth 5
if ($failures.Count -gt 0) {
    throw ($failures -join [Environment]::NewLine)
}
